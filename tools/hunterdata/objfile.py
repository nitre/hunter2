"""objfile (file 1): Hunter's 3D object models. See re/formats/objfile.md.

The file is a run of objects, each starting "OB" + size word ($49C4 walks them and builds the
offset table at $49F0; FUN_00004f54 opens object N):
  +$00 "OB"        +$02 size (bytes, multiple of 4)
  +$04 w, h, d     extents (words; $3358 halves x and z into the object's box)
  +$0A word        (zero in every object)
  +$0C long        special-vertex selector -> $157C(a5), used by FUN_000052e0
  +$10 byte        -> $1576(a5)
  +$11 byte        scale shift -> $1578(a5): every coordinate is `lsl.w` by it
  +$12 word        vertex count
  +$14             vertices, 4 bytes: flag, x, y, z (signed). flag != 0 starts an articulated
                   sub-part ($551E: bits 4-2 pick one of 8 modes via the table at $5538)
  then             edge count word + count words (two vertex-index bytes each)
  then             face count word + faces: colour byte, flags byte (low nibble = n), n indices;
                   list ends with $FF
  then             padding to the object size (leftover bytes kept verbatim)
After the last object the rest of the 64 KB buffer is kept as raw tail bytes.
"""
import struct
from dataclasses import dataclass, field


@dataclass
class Face:
    colour: int
    flags: int          # high nibble of the flags byte
    vertices: list


@dataclass
class Model:
    index: int
    extents: tuple
    word_0a: int
    special: int
    byte_10: int
    scale: int
    vertices: list      # (flag, x, y, z)
    edges: list         # (first byte, second byte)
    faces: list
    pad: bytes = b""

    def positions(self):
        return [(x << self.scale, y << self.scale, z << self.scale) for _, x, y, z in self.vertices]


@dataclass
class ObjFile:
    models: list = field(default_factory=list)
    tail: bytes = b""


def _sb(b):
    return b - 256 if b > 127 else b


def decode(d):
    out, o = ObjFile(), 0
    while d[o:o + 2] == b"OB":
        size, w, h, dd, w0a, special, b10, scale, nv = struct.unpack_from(">HhhhHIBBH", d, o + 2)
        p = o + 0x14
        verts = [(d[p + 4 * i], _sb(d[p + 4 * i + 1]), _sb(d[p + 4 * i + 2]), _sb(d[p + 4 * i + 3])) for i in range(nv)]
        p += 4 * nv
        ne = struct.unpack_from(">H", d, p)[0]
        edges = [(d[p + 2 + 2 * i], d[p + 3 + 2 * i]) for i in range(ne)]
        p += 2 + 2 * ne
        nf = struct.unpack_from(">H", d, p)[0]
        p += 2
        faces = []
        for _ in range(nf):
            n = d[p + 1] & 15
            faces.append(Face(d[p], d[p + 1] & 0xF0, list(d[p + 2:p + 2 + n])))
            p += 2 + n
        assert d[p] == 0xFF, "face list of object %d does not end in $FF" % len(out.models)
        p += 1
        out.models.append(Model(len(out.models), (w, h, dd), w0a, special, b10, scale, verts, edges, faces, d[p:o + size]))
        o += size
    out.tail = d[o:]
    return out


def encode(of):
    out = bytearray()
    for m in of.models:
        body = bytearray(struct.pack(">hhhHIBBH", *m.extents, m.word_0a, m.special, m.byte_10, m.scale, len(m.vertices)))
        for f, x, y, z in m.vertices:
            body += bytes([f, x & 255, y & 255, z & 255])
        body += struct.pack(">H", len(m.edges)) + b"".join(bytes(e) for e in m.edges)
        body += struct.pack(">H", len(m.faces))
        for f in m.faces:
            body += bytes([f.colour, f.flags | len(f.vertices)] + f.vertices)
        body += b"\xff" + m.pad
        out += b"OB" + struct.pack(">H", len(body) + 4) + body
    return bytes(out + of.tail)
