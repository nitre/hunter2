"""Hunter's own disk file table.

Table: $10672 in the decrypted main (read by FUN_0001096c), 32 bytes per entry:
  +0 start track, +2 size in longs, +4 track count, +6 flags (bit 6 = preload),
  +7 packer type (1 = IFF ILBM), +8 name (24 chars, space padded).
Entries 0-14 are on the game disk; 15+ are on the save disk. Track -> image offset follows the
WHDLoad slave's Load: 5120 bytes per track, tracks >= 80 shifted down by one.
"""
import struct
from dataclasses import dataclass

TABLE = 0x10672
BASE = 0x800
GAME_DISK_FILES = 15
TRACK_BYTES = 5120


@dataclass(frozen=True)
class FileEntry:
    index: int
    name: str
    track: int
    size: int
    ntracks: int
    flags: int
    packer: int


def read_table(main):
    out = []
    for i in range(GAME_DISK_FILES):
        e = main[TABLE - BASE + i * 32:TABLE - BASE + i * 32 + 32]
        trk, longs, ntrk = struct.unpack(">HHH", e[:6])
        name = e[8:].decode("ascii").strip().split("/")[-1]
        out.append(FileEntry(i, name, trk, longs * 4, ntrk, e[6], e[7]))
    return out


def read_file(disk, e):
    off = (e.track if e.track < 80 else e.track - 1) * TRACK_BYTES
    return disk[off:off + e.size]
