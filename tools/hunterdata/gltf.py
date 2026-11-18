"""Write one Hunter model as glTF 2.0 (single .gltf file, embedded buffer).

Positions are the game's own integer coordinates after the scale shift (x, y, z as stored; the
projection at $5222-$5244 negates y for the screen, so y is up). Faces are edge loops in the
game; filled faces (type 0 or 4) become triangle fans over the loop's vertex ring, every other
type becomes LINES over the listed edges. Colour is NOT baked: the game picks the palette per
screen and blends it for time of day, so each material carries `extras.hunter_colour` (0-15)
and the remake applies the palette at run time.
"""
import base64
import json
import struct

FILLED = (0x00, 0x40)


def ring(model, face):
    """Vertex ring of a face's edge loop (the filler closes it implicitly if the last edge is missing)."""
    es = [model.edges[i] for i in face.vertices]
    if len(es) == 1:
        return list(es[0])
    a, b = es[0]
    out = [b, a] if a in es[1] else [a, b]
    for e in es[1:]:
        last = out[-1]
        nxt = e[1] if e[0] == last else e[0] if e[1] == last else None
        if nxt is None:            # loop not chained in order: fall back to the edge's own order
            out += [v for v in e if v not in out]
        elif nxt != out[0]:
            out.append(nxt)
    return out


def build(model):
    pos = model.positions()
    tris, lines = {}, {}
    for f in model.faces:
        kind = f.flags & 0x70
        if kind in FILLED:
            r = ring(model, f)
            tris.setdefault(f.colour, []).extend(i for k in range(1, len(r) - 1) for i in (r[0], r[k], r[k + 1]))
        else:
            lines.setdefault(f.colour, []).extend(v for i in f.vertices for v in model.edges[i])
    return pos, tris, lines


def write(model, path):
    pos, tris, lines = build(model)
    blob = bytearray(b"".join(struct.pack("<3f", *p) for p in pos))
    views = [{"buffer": 0, "byteOffset": 0, "byteLength": len(blob), "target": 34962}]
    xs, ys, zs = zip(*pos) if pos else ((0,), (0,), (0,))
    accessors = [{"bufferView": 0, "componentType": 5126, "count": len(pos), "type": "VEC3",
                  "min": [min(xs), min(ys), min(zs)], "max": [max(xs), max(ys), max(zs)]}]
    materials, prims = [], []
    for mode, groups in ((4, tris), (1, lines)):
        for colour, idx in sorted(groups.items()):
            if not idx:
                continue
            while len(blob) % 4:
                blob.append(0)
            off = len(blob)
            blob += struct.pack("<%dH" % len(idx), *idx)
            views.append({"buffer": 0, "byteOffset": off, "byteLength": 2 * len(idx), "target": 34963})
            accessors.append({"bufferView": len(views) - 1, "componentType": 5123, "count": len(idx), "type": "SCALAR"})
            materials.append({"name": "hunter_colour_%d" % colour, "extras": {"hunter_colour": colour},
                              "pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1]}, "doubleSided": True})
            prims.append({"attributes": {"POSITION": 0}, "indices": len(accessors) - 1,
                          "material": len(materials) - 1, "mode": mode})
    doc = {
        "asset": {"version": "2.0", "generator": "hunterdata.gltf"},
        "scene": 0, "scenes": [{"nodes": [0]}],
        "nodes": [{"mesh": 0, "name": "hunter_object_%03d" % model.index}],
        "meshes": [{"primitives": prims, "extras": {
            "scale_shift": model.scale, "extents": list(model.extents), "byte_10": model.byte_10,
            "special": model.special,
            "vertex_flags": [v[0] for v in model.vertices]}}],
        "materials": materials,
        "buffers": [{"byteLength": len(blob), "uri": "data:application/octet-stream;base64," + base64.b64encode(bytes(blob)).decode()}],
        "bufferViews": views, "accessors": accessors,
    }
    json.dump(doc, open(path, "w"))
