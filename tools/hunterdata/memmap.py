"""Hunter's memory buffers, by replaying its own allocator.

FUN_00008410 calls FUN_000083f2 with a0 = $8312, a1 = $1135C: for each 8-byte entry
(pointer, size) until size < 0, the pointer gets a1 and a1 += size. See re/formats/memory.md.
"""
import struct
from dataclasses import dataclass

TABLE = 0x8312
HEAP = 0x1135C
BASE = 0x800

# pointer variable -> name, each backed by a load call or users listed in re/formats/memory.md
NAMES = {
    0x831A: "soundfile",   # file 4 loaded at $8450, then jsr (a0) at $845C = player init
    0x8322: "musfile",     # file 3 loaded at $8466
    0x8342: "screen0",     # 32000 = 320x200x4 planes; sapfile (file 12+mode) loaded here at $105C8
    0x834A: "screen1",     # 32000, second screen
    0x838A: "animation",   # 35000 = file 2 size exactly
    0x83CA: "objfile",     # file 1 loaded at $49BE (65536 bytes)
    0x83DA: "sinfile",     # file 7 loaded at $847A (2048 bytes spans $83DA + $83E2)
}


@dataclass(frozen=True)
class Buffer:
    pointer: int
    addr: int
    size: int


def buffers(main):
    out, a, p = {}, HEAP, TABLE
    while True:
        _, size = struct.unpack_from(">Ii", main, p - BASE)
        if size < 0:
            return out
        out[NAMES.get(p, "buf_%04X" % p)] = Buffer(p, a, size)
        a += size
        p += 8
