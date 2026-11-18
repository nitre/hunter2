"""Hunter's IFF pictures (packer type 1), decoded the way the game does it.

Game reader at $1128A (see re/formats/pictures.md):
  - scans byte-wise for "CMAP" ($11334), skips the length, reads 16 colours: each RGB byte
    keeps its high nibble -> 12-bit $0RGB words into the palette table at $6418 ($112A2-$112B2);
  - scans for "BODY", unpacks with Unpack ($1130A, ByteRun1: n>=0 copy n+1, n<0 and n!=-128 repeat -n+1,
    -128 skip) into buffer $83D2, $163E(a5) bytes;
  - de-interleaves ILBM rows into separate bitplanes at the destination ($112CE-$11308).
BMHD is ignored by the game; every Hunter picture is 320x200, 4 planes.
"""
import struct
from dataclasses import dataclass

W, H, PLANES = 320, 200, 4
ROW = W // 8


@dataclass
class Picture:
    palette12: list   # 16 x $0RGB words, exactly what the game loads into $6418
    pixels: bytes     # W*H palette indices

    @property
    def palette(self):
        return [expand12(c) for c in self.palette12]


def expand12(c):
    return (((c >> 8) & 15) * 17, ((c >> 4) & 15) * 17, (c & 15) * 17)


def _find(d, tag):
    i = d.find(tag)
    if i < 0:
        raise ValueError("no %s chunk" % tag.decode())
    return i + 8  # skip tag + length, as the game does (move.l (a0)+,d0)


def unpack_byterun1(src, i, need):
    out = bytearray()
    while len(out) < need:
        n = src[i] - 256 if src[i] > 127 else src[i]
        i += 1
        if n == -128:
            continue
        if n >= 0:
            out += src[i:i + n + 1]
            i += n + 1
        else:
            out += bytes([src[i]]) * (1 - n)
            i += 1
    return bytes(out[:need]), i


def decode(d):
    c = _find(d, b"CMAP")
    pal = [((d[c + 3 * k] >> 4) << 8) | ((d[c + 3 * k + 1] >> 4) << 4) | (d[c + 3 * k + 2] >> 4) for k in range(16)]
    raw, _ = unpack_byterun1(d, _find(d, b"BODY"), ROW * PLANES * H)
    px = bytearray(W * H)
    for y in range(H):
        for p in range(PLANES):
            row = raw[(y * PLANES + p) * ROW:(y * PLANES + p + 1) * ROW]
            for x in range(W):
                if row[x >> 3] >> (7 - (x & 7)) & 1:
                    px[y * W + x] |= 1 << p
    return Picture(pal, bytes(px))


def to_png(pic, path):
    from PIL import Image
    im = Image.new("RGB", (W, H))
    pal = pic.palette
    im.putdata([pal[i] for i in pic.pixels])
    im.save(path)
