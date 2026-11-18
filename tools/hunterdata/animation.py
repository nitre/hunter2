"""animation (file 2): blitter graphics. See re/formats/animation.md.

Loaded by FUN_00002016 at $2026 into buffer $8382, so the first 800 bytes land in $8382 (index)
and the rest in $838A (bitmap data). The blit routine at $C884 reads entry N of the index at
$8382 + N*8: +0 width in words, +1 height in lines, +2 first-word mask, +4 width-1,
+6 offset in longs from $838A. Frames are 4 bitplanes of width*height*2 bytes each
(mode table $C86C: 4 planes for modes 0-2). Entry 0 is a header (36, 22640).
"""
import struct
from dataclasses import dataclass

INDEX = 800
ENTRIES = 100


@dataclass
class Frame:
    index: int
    width_words: int
    height: int
    first_mask: int
    width_minus_1: int
    offset_longs: int
    data: bytes          # 4 planes, plane after plane


def decode(d):
    frames = []
    for i in range(1, ENTRIES):
        w, h, fm, wm1, off = struct.unpack_from(">BBHHH", d, 8 * i)
        start = INDEX + 4 * off
        frames.append(Frame(i, w, h, fm, wm1, off, d[start:start + w * h * 2 * 4] if w and h else b""))
    return d[:8], frames


def pixels(frame):
    """Palette indices of a frame drawn opaque (blit mode 1: BLTCON0 $07CA, no A-channel mask,
    so D = B - a straight copy of the 4 planes). Planes are stored one after another, each
    height lines of width_words*2 bytes."""
    w, h = frame.width_words * 16, frame.height
    row = frame.width_words * 2
    plane = row * h
    out = bytearray(w * h)
    for p in range(4):
        base = p * plane
        for y in range(h):
            for x in range(w):
                if frame.data[base + y * row + (x >> 3)] & (0x80 >> (x & 7)):
                    out[y * w + x] |= 1 << p
    return w, h, bytes(out)


def encode(header, frames, d_original):
    """Index only (the bitmap area may share bytes between frames, so it is kept from the file)."""
    out = bytearray(d_original)
    out[:8] = header
    for f in frames:
        struct.pack_into(">BBHHH", out, 8 * f.index, f.width_words, f.height, f.first_mask, f.width_minus_1, f.offset_longs)
    return bytes(out)
