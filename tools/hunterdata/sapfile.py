"""sapfile1-3 (files 12-14): the starting world of each game, in the game's save-image format.

Mode ($20E(a5), set by the AVAILABLE GAMES menu at $2250 -> $2354/$2358/$235C) picks file 12+mode:
  0 = Hunter -> sapfile1, 1 = Missions -> sapfile2, 2 = Action -> sapfile3.
FUN_000105c8 loads it into screen0, checks the checksum ($10638) and copies it out ($105EC/$10608).
See re/formats/sapfile.md.
"""
import struct
from dataclasses import dataclass

MODES = {0: "hunter", 1: "missions", 2: "action"}
SIZE = 50152
WORLD, CONTAINERS = 0x3E8, 0x3E8 + 0x8000
CELL = 4096   # world units per grid cell; 16 x 16 cells


@dataclass
class WorldObject:
    slot: int          # 0..2047; cell = slot // 8
    words: tuple       # the 8 raw words
    @property
    def kind(self):
        return self.words[0]
    @property
    def x(self):
        return self.words[3]
    @property
    def z(self):
        return self.words[5]


@dataclass
class World:
    raw: bytes

    def objects(self):
        out = []
        for i in range(2048):
            w = struct.unpack_from(">8H", self.raw, WORLD + 16 * i)
            if w[0]:
                out.append(WorldObject(i, w))
        return out

    def containers(self):
        return [self.raw[CONTAINERS + 128 * i:CONTAINERS + 128 * (i + 1)] for i in range(128)]


def checksum(d):
    """$10638: words summed with +$26 forced to $AD20, plus $7B; must equal the stored +$26."""
    b = bytearray(d)
    struct.pack_into(">H", b, 0x26, 0xAD20)
    return (0x7B + sum(struct.unpack(">%dH" % (len(b) // 2), b))) & 0xFFFF


def model_index(main, kind):
    """$81EE: model = high byte + word table $81BA[low byte]; drawable only if 0..$AF."""
    t = struct.unpack_from(">16h", main, 0x81BA - 0x800)
    lo = kind & 0xFF
    if lo >= 16:
        return None
    m = (kind >> 8) + t[lo]
    return m if 0 <= m <= 0xAF else None


def decode(d):
    assert len(d) == SIZE
    return World(bytes(d))
