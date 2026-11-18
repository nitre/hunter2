"""jfile (file 11): the recorded demo (main menu "Demo"). See re/formats/jfile.md.

5000 bytes = buffers $832A (1000) + $8332 (4000). FUN_00002414 points `$CE(a5)` at $832A
(and clears it unless demo mode, `$9B(a5)` negative). Playback FUN_0000c826: pairs of bytes
(count, value); the count is decremented once per call and the pointer moves on when it hits 0.
value bit 7 set -> key press `value & $7F` passed to the key handler $B214; otherwise the
value is the recorded control state.
"""
from dataclasses import dataclass


@dataclass
class Step:
    count: int
    value: int

    @property
    def is_key(self):
        return bool(self.value & 0x80)


def decode(d):
    return [Step(d[i], d[i + 1]) for i in range(0, len(d) - 1, 2)]


def encode(steps):
    return bytes(b for s in steps for b in (s.count, s.value))
