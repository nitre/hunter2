"""Decrypt Hunter's main program from the RawDIC Disk.1 image.

Follows the WHDLoad slave (whdload_pkg/HunterInstall/Source/HunterHD.s, Patch + Decrypt):
  - loader: 0x800 bytes from disk offset 0x400 -> $7E800 (a5)
  - program: 0x11800 bytes from disk offset 0x2800 -> $800
  - pass 1: movem.l $5E(a5),d0-a7; copy d0+1 longs (a1)+ -> a0+$600; Decrypt
  - pass 2: movem.l $128(a5),d0-a6; a3 -> $10E(a5); movem.l d3-a6,$6A(a5); Decrypt
  - check: CRC16 of $10000 bytes at $800 = $B506 (EN) / $D2F2 (DE) / $F64F (FR)
"""
import struct

M32 = 0xFFFFFFFF
mem = bytearray(0x80000)


def rl(a):
    return struct.unpack_from(">I", mem, a)[0]


def wl(a, v):
    struct.pack_into(">I", mem, a, v & M32)


def sx16(v):
    v &= 0xFFFF
    return v - 0x10000 if v & 0x8000 else v


def lo_w(reg, v):  # write the low word of a long register
    return (reg & 0xFFFF0000) | (v & 0xFFFF)


def decrypt(r):
    """r: dict d0..d7, a0..a6. Mirrors the slave's Decrypt loop exactly."""
    d = [r[f"d{i}"] for i in range(8)]
    a = [r[f"a{i}"] for i in range(7)]
    a5 = a[5]
    while True:
        d[6] = d[6] & 0xFFFF0000                              # clr.w d6
        a[6] = (a[6] - 1) & M32                               # move.b -(a6),d6
        d[6] = (d[6] & 0xFFFFFF00) | mem[a[6]]
        d[6] = (d[6] & 0xFFFFFF00) | ((d[6] * 2) & 0xFF)      # add.b d6,d6
        d[6] = rl((a5 + sx16(d[6])) & M32)                    # move.l (a5,d6.w),d6
        d0, d1 = rl(a[3]), rl(a[3] + 4)                       # movem.l (a3),d0/d1
        d[0] = d0 ^ d[4]
        d[1] = d1 ^ d[5]
        d[3] &= 15                                            # and.l #15,d3
        d[3] = lo_w(d[3], d[3] * 2)                           # add.w d3,d3
        a[2] = d[3]                                           # move.l d3,a2
        d[3] = lo_w(d[3], d[3] * 2)                           # add.w d3,d3
        d[6] = (d[6] + rl((a[4] + sx16(d[3])) & M32)) & M32   # add.l (a4,d3.w),d6
        d[1] ^= d[6]
        d[1] = lo_w(d[1], (d[1] ^ d[7]) & 0xFFFF)             # eor.w d7,d1
        d[6] = ((d[6] >> 16) | (d[6] << 16)) & M32            # swap d6
        d[0] ^= d[6]
        p = (a[2] + 0x600) & M32                              # movep.l 8(a2),d6 (vectors moved +$600)
        d[6] = (mem[p + 8] << 24) | (mem[p + 10] << 16) | (mem[p + 12] << 8) | mem[p + 14]
        d[0] = lo_w(d[0], (d[0] ^ d[6]) & 0xFFFF)             # eor.w d6,d0
        d[4] = (d[4] + sum(a)) & M32                          # add.l a0..a6,d4
        d[5] = lo_w(d[5], d[5] - 1)                           # subq.w #1,d5
        if sx16(d[5]) <= 0:                                   # bgt.b .skip
            d[6] = d[5]
            d[6] = ((d[6] >> 16) | (d[6] << 16)) & M32
            a[6] = (a[6] + sx16(d[6])) & M32                  # add.w d6,a6 (adda: sign-extends)
            d[5] = lo_w(d[5], d[6])
        wl(a[3], d[0]); a[3] = (a[3] + 4) & M32               # move.l d0,(a3)+
        d[2] ^= d[1]
        d[3] = a[3]
        d[2] = (d[2] + d[0]) & M32
        regs = d[3:8] + a[0:7]                                # movem.l d3-a6,$6A(a5)
        for i, v in enumerate(regs):
            wl(a5 + 0x6A + 4 * i, v)
        wl(a[3], d[1]); a[3] = (a[3] + 4) & M32               # move.l d1,(a3)+
        d[7] = lo_w(d[7], d[7] - 1)                           # dbf d7,Decrypt
        if d[7] & 0xFFFF == 0xFFFF:
            break


def movem(addr, names):
    return {n: rl(addr + 4 * i) for i, n in enumerate(names)}


def crc16(buf):  # WHDLoad resload_CRC16: CRC-16/ARC (poly $A001 reflected, init 0)
    c = 0
    for b in buf:
        c ^= b
        for _ in range(8):
            c = (c >> 1) ^ 0xA001 if c & 1 else c >> 1
    return c


class UnsupportedDisk(Exception):
    pass


LANGS = {0xB506: "English", 0xD2F2: "German", 0xF64F: "French"}


def decrypt_main(disk):
    """Return (program image for $800, language). Only English is supported for now:
    the file table and every address in the notes are for the English build."""
    try:
        return _decrypt_main(disk)
    except (IndexError, struct.error):  # a non-Hunter image drives the key registers out of memory
        raise UnsupportedDisk("unsupported disk: this is not a Hunter game disk image (Disk.1)") from None


def _decrypt_main(disk):
    mem[:] = bytes(len(mem))
    a5 = 0x7E800
    mem[a5:a5 + 0x800] = disk[0x400:0xC00]
    mem[0x800:0x800 + 0x11800] = disk[0x2800:0x2800 + 0x11800]

    regs = ["d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7", "a0", "a1", "a2", "a3", "a4", "a5", "a6", "a7"]
    r = movem(a5 + 0x5E, regs)
    dst, src = r["a0"] + 0x600, r["a1"]
    for _ in range((r["d0"] & 0xFFFF) + 1):
        wl(dst, rl(src)); dst += 4; src += 4
    r["a0"], r["a1"] = dst - 0x600, src                       # copy loop advanced a0/a1; both feed the key
    r["d0"] = lo_w(r["d0"], 0xFFFF)                           # dbf exits with d0.w = -1
    decrypt(r)

    r = movem(a5 + 0x128, regs[:15])
    wl(a5 + 0x10E, r["a3"])
    for i, n in enumerate(regs[3:15]):
        wl(a5 + 0x6A + 4 * i, r[n])
    decrypt(r)

    crc = crc16(mem[0x800:0x800 + 0x10000])
    lang = LANGS.get(crc)
    if lang != "English":
        raise UnsupportedDisk(f"unsupported disk: CRC ${crc:04X} ({lang or 'unknown or damaged image'}); "
                              "only the English Hunter (CRC $B506) is supported")
    return bytes(mem[0x800:0x800 + 0x11800]), lang
