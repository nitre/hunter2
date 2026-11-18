"""Hunter's two 8x8 fonts, cut from the store picture (file 9) the way the game does it.

$84EE loads storepic into screen1 ($834A), then $8552 copies 7 rows x 40 characters x 8 lines
from bitplane 0 (40 bytes per line): offset $1040 (picture row 104) -> buffer $837A, offset $780
(row 48) -> buffer $8372. $8590 then builds the width table $836A from $8372: OR the 8 lines,
width = 1 + index of the lowest set bit counted from the left... i.e. the loop at $85AC-$85B6 adds 1
per left shift until the byte is zero; an empty glyph gets 5.
The text renderer ($C26A) draws character code c from $836A + $118 + c*8 = $8372 + c*8 with
advance width($836A[c]) + 1 ($C282).
"""
from . import pictures

ROW_BYTES = 40
CHARS = 280


def _cut(plane0, offset):
    glyphs = []
    a = offset
    for _row in range(7):
        for col in range(40):
            glyphs.append(bytes(plane0[a + col + ROW_BYTES * line] for line in range(8)))
        a += 40 + 0x118
    return glyphs


def plane0_of(pic):
    """Bitplane 0 of a decoded picture as 40-byte lines (what the de-interleaved screen holds)."""
    out = bytearray(ROW_BYTES * pictures.H)
    for i, v in enumerate(pic.pixels):
        if v & 1:
            out[i >> 3] |= 0x80 >> (i & 7)
    return bytes(out)


def width(glyph):
    """$859C-$85BA."""
    d0 = 0
    for b in glyph:
        d0 |= b
    if d0 == 0:
        return 5
    w = 1
    for _ in range(8):
        d0 = (d0 + d0) & 0xFF
        if d0 == 0:
            break
        w += 1
    return w


def fonts(storepic_iff):
    p0 = plane0_of(pictures.decode(storepic_iff))
    main, second = _cut(p0, 0x780), _cut(p0, 0x1040)
    return main, second, [width(g) for g in main]
