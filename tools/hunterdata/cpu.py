"""Test oracle: the original program run on a 68000 CPU emulator (Musashi, via machine68k).

Used only by tests, to get expected values from the original's own code: call a routine with
known inputs and read memory back. No screenshots, no WinUAE. The bits of the Amiga the code
touches are modelled just enough: the level-3 interrupt (vertical blank + copper) on a cycle
count, the blitter in area mode, the mouse, and the disk replaced by a hook that hands over
the files (as the WHDLoad slave does).

Unicorn was used first and dropped: its lazily kept m68k flags go wrong whenever a hook touches
the CPU mid-block (a cmp/beq at $CF02 and a move.l/bne at $1FB0 took the wrong way).

Slave patches mirrored (whdload_pkg/HunterInstall/Source/HunterHD.s, PLGAME_EN, offsets + $800):
- copylock $F7EA -> key $7B8669F8 into program base + $16A ($96A), rts
- track reader $10AB8 -> copy the file's bytes to a0 (the game still unpacks IFF files itself)
- ext-mem check / preload $109A8 -> rts

machine68k needs building on Windows (sdist only): see tools/README or re/NOTES.md.
"""
import struct
import sys
import time

import machine68k
from machine68k import CPUType, Register as R

BASE = 0x800
A5 = 0x806
STACK = 0x7FFF0
CUSTOM = 0xDF0000
END = 0xFFFF0           # magic return address: a routine returning here has finished
FRAME = 141_800         # 68000 cycles per PAL frame (7.09 MHz / 50)
NOP = 0xFFFE0           # a nop the CPU steps to drop its prefetched words
SLICE = 1000           # cycles between interrupt checks (a blit's interrupt comes promptly)
HEARTBEAT = 5          # seconds between progress lines while a call runs
T0 = time.time()

_current = None


def _reg(n, kind):
    return R(n if kind == "d" else 8 + n)


class Machine:
    def __init__(self, main, files):
        """main: program image for $800; files: list of file contents by disk index."""
        global _current
        if _current is not None:          # Musashi is one global CPU
            _current.mach.cleanup()
        _current = self
        self.files = files
        self.mach = machine68k.Machine(CPUType.M68000, 4096)
        self.cpu, self.mem = self.mach.cpu, self.mach.mem
        self.mem.set_special_range_read_funcs(CUSTOM, 1, self._c8, self._c16, self._c32)
        self.mem.set_special_range_write_funcs(CUSTOM, 1, self._cw8, self._cw16, self._cw32)
        self.mem.set_special_range_read_funcs(0xBF0000, 1, self._cia, self._cia16, None)
        self.mem.set_special_range_write_funcs(0xBF0000, 1, lambda a, v: None, lambda a, v: None, None)
        self.mem.set_invalid_func(self._invalid)
        self.mem.w_block(BASE, bytes(main))
        self.mem.w_block(END, b"\x4e\x71" * 4)
        self.mem.w_block(NOP, b"\x4e\x71" * 4)
        self.wl(0, STACK)             # reset vectors, so Musashi starts in a known state
        self.wl(4, END)
        self.cpu.pulse_reset()
        self.wl(A5, 0x7FFE0)          # boot stores its SP at (a5) ($1EFA); $CE76 reads it back
        self.regs = {}                # custom register shadow, by offset
        self.intena = self.intreq = 0
        self.beam = 0
        self.hooks = {}
        self.code = {}                # original words under the trap opcodes
        self.left_button = False
        self.cycles = 0               # within the current frame
        self.frames = 0
        self._jumped = False
        self.watchdog = None          # called once a frame; raise from it to stop a stalled run
        self.stage = "boot"           # shown in progress lines; callers set it
        self.patch(0xF7EA, self._copylock)
        self.patch(0x10AB8, self._read)
        self.patch(0x109A8, lambda m: m.rts())
        self._done = False
        self.patch(END, self._end)

    def _end(self, m):
        self._done = True
        self._jump(END)

    # --- memory helpers ---
    def rb(self, a): return self.mem.r8(a)
    def rw(self, a): return self.mem.r16(a)
    def rl(self, a): return self.mem.r32(a)
    def wb(self, a, v): self.mem.w8(a, v & 0xFF)
    def ww(self, a, v): self.mem.w16(a, v & 0xFFFF)
    def wl(self, a, v): self.mem.w32(a, v & 0xFFFFFFFF)
    def read(self, a, n): return bytes(self.mem.r_block(a, n))
    def write(self, a, data): self.mem.w_block(a, bytes(data))
    def d(self, n): return self.cpu.r_reg(_reg(n, "d"))
    def a(self, n): return self.cpu.r_reg(_reg(n, "a"))
    def set_d(self, n, v): self.cpu.w_reg(_reg(n, "d"), v & 0xFFFFFFFF)
    def set_a(self, n, v): self.cpu.w_reg(_reg(n, "a"), v & 0xFFFFFFFF)
    def pc(self): return self.cpu.r_pc()
    def sr(self): return self.cpu.r_sr()
    def set_sr(self, v): self.cpu.w_sr(v)
    def set_z(self):
        self.set_sr((self.sr() & ~0x1F) | 0x04)

    def _invalid(self, mode, width, addr):
        raise RuntimeError("bad memory access %s%d at $%X, pc=$%X" % (mode, width, addr, self.pc()))

    # --- hooks: an A-line trap word over the instruction; the hook runs between instructions ---
    def patch(self, addr, fn):
        if addr not in self.hooks:
            tid = self.mach.traps.alloc(lambda op, pc: None)
            self.code[addr] = self.mem.r16(addr)
            self.mem.w16(addr, 0xA000 | tid)
        self.hooks[addr] = fn

    def unpatch(self, addr):
        self.mem.w16(addr, self.code.pop(addr))
        self.hooks.pop(addr)

    def rts(self):
        sp = self.a(7)
        self._jump(self.rl(sp))
        self.set_a(7, sp + 4)

    def _jump(self, addr):
        self.cpu.w_pc(addr)
        self._jumped = True

    def call(self, addr, seconds=60, stop=None, **regs):
        """Call a subroutine with registers set (d0=.., a4=..); A5 is the globals block.
        stop = an address to halt at instead of waiting for the return (for code that loops)."""
        if stop is not None:
            self.patch(stop, lambda m: m._jump(END))
        self.set_sr(0x2000)
        self.set_a(5, A5)
        for k, v in regs.items():
            (self.set_d if k[0] == "d" else self.set_a)(int(k[1]), v)
        sp = self.a(7)
        if sp < 0x1000 or sp > STACK:
            sp = STACK
        sp -= 4
        self.wl(sp, END)
        self.set_a(7, sp)
        self.cpu.w_pc(addr)
        self.run(seconds)
        if stop is not None:
            self.unpatch(stop)

    def log(self, msg):
        """Progress line to stderr: elapsed time, emulated frames, stage, message."""
        sys.stderr.write("[oracle %6.1fs f%-6d %s] %s\n" % (time.time() - T0, self.frames, self.stage, msg))
        sys.stderr.flush()

    def run(self, seconds):
        deadline = time.time() + seconds
        beat = time.time() + HEARTBEAT
        self._done = False
        while True:
            now = time.time()
            if now > deadline:
                raise RuntimeError("no return within %ds, pc=$%X" % (seconds, self.pc()))
            if now > beat:
                beat = now + HEARTBEAT
                self.log("running, pc=$%X" % self.pc())
            res = self.cpu.execute(min(SLICE, max(FRAME - self.cycles, 1)))
            self.cycles += res.cycles
            if res.was_trap:
                self._trap()
                if self._done:
                    return
            if self.cycles >= FRAME:
                self.cycles -= FRAME
                self.frames += 1
                if self.watchdog:
                    self.watchdog(self)
                self.intreq |= 0x30                   # vertical blank + copper, once a frame
            self._raise()

    def _trap(self):
        info = self.mach.traps.get_info()
        addr = info.pc
        fn = self.hooks.get(addr)
        if fn is None:
            raise RuntimeError("unexpected A-line opcode $%04X at $%X" % (info.opcode, addr))
        self.cpu.w_pc(addr)
        self._jumped = False
        fn(self)
        if self._jumped:
            return
        # Run the original instruction once, then put the trap back. The CPU prefetches: it
        # still holds the trap word for this address, so first make it fetch elsewhere (a nop),
        # or it would skip the instruction (seen: a bne at $1FB0 never branching).
        trap = self.mem.r16(addr)
        self.mem.w16(addr, self.code[addr])
        self.cpu.w_pc(NOP)
        self.cpu.execute(1)
        self.cpu.w_pc(addr)
        res = self.cpu.execute(1)
        self.cycles += res.cycles
        self.mem.w16(addr, trap)

    def _raise(self):
        """Take the level-3 interrupt (blitter, vertical blank, copper) when one is pending."""
        sr = self.sr()
        if not (self.intena & 0x4000 and self.intena & self.intreq & 0x70) or (sr >> 8) & 7 >= 3:
            return
        sp = self.a(7) - 6
        self.ww(sp, sr)
        self.wl(sp + 2, self.pc())
        self.set_a(7, sp)
        self.set_sr((sr & 0xF8FF) | 0x2300)
        self.cpu.w_pc(self.rl(0x6C))

    # --- slave patches ---
    def _copylock(self, m):
        # the slave writes ($16A, reloc_base): $16A past the program start $800, i.e. $164(a5).
        # $AFBE checks it ($878A) and NOPs the menu's bne at $1FB0 if wrong (demo only).
        m.wl(BASE + 0x16A, 0x7B8669F8)
        m.rts()

    def _read(self, m):
        # $10AB8 reads the file's tracks (file number $1602, size $162E, set by $1096C) to a0;
        # the caller then unpacks IFF files itself ($10A62 -> $1128A), as on the real disk.
        n, size, dest = m.rw(A5 + 0x1602), m.rl(A5 + 0x162E), m.a(0)
        m.write(dest, self.files[n][:size])
        m.wb(A5 + 0x1621, 0)                     # no retries used
        m.set_z()                                # tst.b $1621
        m.rts()

    # --- custom chips ---
    def _c8(self, addr):
        w = self._c16(addr & ~1)
        return w & 0xFF if addr & 1 else w >> 8

    def _c32(self, addr):
        return self._c16(addr) << 16 | self._c16(addr + 2)

    def _c16(self, addr):
        off = addr & 0xFFF
        if off == 0x002:
            return 0                                   # DMACONR: blitter never busy
        if off in (0x004, 0x006):                      # beam position from the cycle count
            line = self.cycles * 313 // FRAME
            h = (self.cycles * 313 % FRAME) * 227 // FRAME
            return (line >> 8) & 1 if off == 0x004 else ((line & 0xFF) << 8) | h
        if off == 0x016:
            return 0xFF00                              # POTGOR: right/middle buttons up
        if off == 0x01C:
            return self.intena
        if off == 0x01E:
            return self.intreq
        return self.regs.get(off, 0)

    def _cw8(self, addr, v):
        self._cw16(addr & ~1, v << 8 if not addr & 1 else v)

    def _cw32(self, addr, v):
        self._cw16(addr, v >> 16)
        self._cw16(addr + 2, v & 0xFFFF)

    def _cw16(self, addr, value):
        off = addr & 0xFFF
        if off == 0x09A:
            self.intena = self.intena | (value & 0x7FFF) if value & 0x8000 else self.intena & ~value
        elif off == 0x09C:
            self.intreq = self.intreq | (value & 0x7FFF) if value & 0x8000 else self.intreq & ~value
        else:
            self.regs[off] = value
            if off == 0x058:
                self._blit(value)

    def _cia(self, addr):
        reg = (addr >> 8) & 0xF                        # A8-A11 pick the register; any mirror
        if reg == 0xD:
            return 0                                   # ICRs: nothing pending
        cia_a = addr & 1 and not addr & 0x1000         # CIA-A: odd addresses with A12 low
        if cia_a and reg == 0 and self.left_button:
            return 0xBF                                # CIA-A PRA bit 6 low = left button down
        return 0xFF                                    # ports idle high

    def _cia16(self, addr):
        return self._cia(addr) << 8 | self._cia(addr + 1)

    # --- blitter, area mode (HRM ch. 6) ---
    def _ptr(self, off):
        return ((self.regs.get(off, 0) << 16) | self.regs.get(off + 2, 0)) & 0x7FFFE

    def _blit(self, size):
        r = self.regs.get
        con0, con1 = r(0x40, 0), r(0x42, 0)
        if con1 & 1:
            raise RuntimeError("blitter line mode not modelled")
        h = (size >> 6) or 1024
        w = (size & 0x3F) or 64
        desc = bool(con1 & 2)
        step = -2 if desc else 2
        ash, bsh = con0 >> 12, con1 >> 12
        use = (con0 >> 8) & 0xF          # A B C D
        mt = con0 & 0xFF
        fill = (con1 >> 3) & 3            # IFE, EFE
        if use == 1 and not fill and not desc:          # D only: a fill with a constant
            word = 0
            cdat, bdat, adat = r(0x70, 0), r(0x72, 0), r(0x74, 0)
            for bit in range(8):
                if mt >> bit & 1:
                    ta = adat if bit & 4 else ~adat
                    tb = bdat if bit & 2 else ~bdat
                    tc = cdat if bit & 1 else ~cdat
                    word |= ta & tb & tc
            row = struct.pack(">H", word & 0xFFFF) * w
            pd = self._ptr(0x54)
            mod = struct.unpack(">h", struct.pack(">H", r(0x66, 0)))[0]
            for _y in range(h):
                self.mem.w_block(pd, row)
                pd += 2 * w + mod
            self.intreq |= 0x40
            return
        fwm, lwm = r(0x44, 0xFFFF), r(0x46, 0xFFFF)
        pa, pb, pc_, pd = self._ptr(0x50), self._ptr(0x4C), self._ptr(0x48), self._ptr(0x54)
        mods = {k: struct.unpack(">h", struct.pack(">H", r(o, 0)))[0]
                for k, o in (("a", 0x64), ("b", 0x62), ("c", 0x60), ("d", 0x66))}
        if desc:
            mods = {k: -v for k, v in mods.items()}
        adat, bdat, cdat = r(0x74, 0), r(0x72, 0), r(0x70, 0)
        fci = (con1 >> 2) & 1
        for _y in range(h):
            aold = bold = 0
            carry = fci
            for x in range(w):
                if use & 8:
                    adat = self.rw(pa); pa += step
                if use & 4:
                    bdat = self.rw(pb); pb += step
                if use & 2:
                    cdat = self.rw(pc_); pc_ += step
                am = adat
                if x == 0:
                    am &= fwm
                if x == w - 1:
                    am &= lwm
                if desc:
                    a_s = ((am << ash) | (aold >> (16 - ash))) & 0xFFFF
                    b_s = ((bdat << bsh) | (bold >> (16 - bsh))) & 0xFFFF
                else:
                    a_s = ((aold << (16 - ash)) | (am >> ash)) & 0xFFFF
                    b_s = ((bold << (16 - bsh)) | (bdat >> bsh)) & 0xFFFF
                aold, bold = am, bdat
                out = 0
                for bit in range(8):
                    if mt >> bit & 1:
                        ta = a_s if bit & 4 else ~a_s
                        tb = b_s if bit & 2 else ~b_s
                        tc = cdat if bit & 1 else ~cdat
                        out |= ta & tb & tc
                out &= 0xFFFF
                if fill:
                    res = 0
                    for bit in range(16):
                        v = out >> bit & 1
                        if fill & 2:          # exclusive
                            carry ^= v
                            res |= carry << bit
                        else:                 # inclusive
                            res |= (carry | v) << bit
                            carry ^= v
                    out = res
                if use & 1:
                    self.ww(pd, out); pd += step
            pa += mods["a"] if use & 8 else 0
            pb += mods["b"] if use & 4 else 0
            pc_ += mods["c"] if use & 2 else 0
            pd += mods["d"] if use & 1 else 0
        self.intreq |= 0x40
