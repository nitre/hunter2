"""Picture sheets of the small graphics (for looking at, not used by the game):
fonts (tables/font.json: 8 rows a glyph, one bit a pixel), the 99 animation frames
(files/02_animation.bin as BlitFrame reads it, each in its screen's palette bank), and the mouse pointer
images (3 x 64 bytes at $AA46, copied by CopyPointerImages $AA32: a control long, then rows of
two plane words). python -m hunterdata.sheets <game_data>"""
import json, os, sys
from PIL import Image

SCALE = 3
BACK = (24, 24, 24)    # sheet background: not a game colour


def amiga_rgb(c):
    return ((c >> 8 & 15) * 17, (c >> 4 & 15) * 17, (c & 15) * 17)


def font_sheet(font, key, path):
    glyphs = font[key]
    cols, w, h = 32, 9, 9
    im = Image.new("RGB", (cols * w, (len(glyphs) + cols - 1) // cols * h), (40, 40, 60))
    for i, g in enumerate(glyphs):
        rows = bytes.fromhex(g)
        for y, r in enumerate(rows[:8]):
            for x in range(8):
                if r & (0x80 >> x):
                    im.putpixel((i % cols * w + x, i // cols * h + y), (255, 255, 255))
    im.resize((im.width * SCALE, im.height * SCALE), Image.NEAREST).save(path)


# Who draws which frame (BlitFrame $C884, d2 = frame, d3 = mode) and the palette bank showing:
# SoldierScreenSetUp $8776 (bank 6): 1-17 (rank, ribbons, medals $8884/$889E, loop $88DE from
# $A), 18-21 ($8868 $12 + rank, loop $88A8 from $13), 22 (the panel, $8850); DebriefScreen $96BC
# (FadeInBank 6): 26; DrawMenuButton $AD52: 23 in the menus (bank 5), 24-25 on the log book and
# map; the map's kinds $3FB0 (mode 0): 27-37; MapMarks: 0 (built at run time, BuildMarks).
# The map and the log book show bank 1, which is built at run time (the play colours one step
# dimmer): taken from the copper as the oracle's map screen waits ("map", map_and_back.json).
# Nothing draws 38-99 (checked: every BlitFrame call site, and a run through the menus, the three
# pregames and the screens): leftovers, five planes each, some overlapping; shown in "map" too.
BANKS = {**{n: 6 for n in range(1, 23)}, 23: 5, 24: "map", 25: "map", 26: 6, **{n: "map" for n in range(27, 38)}}
MASKED = set(range(27, 100))     # mode 0 (cookie-cut): the mask = the plane after the 4 ($C9DE)


def frame_pixels(anim, n):
    """Frame n as BlitFrame reads it: 4 planes of width*height*2 bytes from 800 + offset*4, and
    for masked frames the next plane as the mask. None where the mask is clear."""
    w, h, off = anim[8 * n], anim[8 * n + 1], anim[8 * n + 6] << 8 | anim[8 * n + 7]
    row, plane, s = 2 * w, 2 * w * h, 800 + 4 * off
    out = []
    for y in range(h):
        for x in range(16 * w):
            a, bit = s + y * row + (x >> 3), 0x80 >> (x & 7)
            if n in MASKED and not anim[a + 4 * plane] & bit:
                out.append(None)
                continue
            out.append(sum(1 << p for p in range(4) if anim[a + p * plane] & bit))
    return 16 * w, h, out


def frames_sheet(anim, banks, frames, path, unused_bank="map"):
    """Shelf-packed, 2 px apart, each frame in the colours its screen shows; 38-99 (never drawn)
    under a grey bar."""
    width, place, x, y, row_h = 320, [], 0, 0, 0
    for n in frames:
        w, h = 16 * anim[8 * n], anim[8 * n + 1]
        if x + w > width or n == 38:
            x, y, row_h = 0, y + row_h + (8 if n == 38 else 2), 0
        if n == 38:
            bar = y - 5
        place.append((x, y))
        x += w + 2
        row_h = max(row_h, h)
    im = Image.new("RGB", (width, y + row_h), BACK)
    for x in range(width):
        for k in range(2):
            im.putpixel((x, bar + k), (128, 128, 128))
    for (ox, oy), n in zip(place, frames):
        pal = [amiga_rgb(c) for c in banks[BANKS.get(n, unused_bank)]]
        w, h, px = frame_pixels(anim, n)
        for i, c in enumerate(px):
            if c is not None:
                im.putpixel((ox + i % w, oy + i // w), pal[c])
    im.resize((im.width * SCALE, im.height * SCALE), Image.NEAREST).save(path)


def pointer_sheet(main, path):
    cols = [BACK, (0, 0, 0), (255, 255, 255), (170, 170, 170)]
    im = Image.new("RGB", (3 * 18, 18), BACK)
    for k in range(3):                               # $AA46-$AB3A holds 3 (the 544-byte copy runs into code)
        img = main[0xAA46 - 0x800 + 64 * k:0xAA46 - 0x800 + 64 * (k + 1)]
        for y in range(15):
            p0 = img[4 + 4 * y] << 8 | img[5 + 4 * y]
            p1 = img[6 + 4 * y] << 8 | img[7 + 4 * y]
            for x in range(16):
                c = (p0 >> (15 - x) & 1) | (p1 >> (15 - x) & 1) << 1
                if c:
                    im.putpixel((k * 18 + x, y), cols[c])
    im.resize((im.width * SCALE * 2, im.height * SCALE * 2), Image.NEAREST).save(path)


def main(game_data):
    out = os.path.join(game_data, "sheets")
    os.makedirs(out, exist_ok=True)
    t = os.path.join(game_data, "tables")
    font = json.load(open(os.path.join(t, "font.json")))
    font_sheet(font, "main_hex", os.path.join(out, "font_main.png"))
    font_sheet(font, "second_hex", os.path.join(out, "font_panel.png"))
    anim = open(os.path.join(game_data, "files", "02_animation.bin"), "rb").read()
    banks = dict(enumerate(json.load(open(os.path.join(t, "palettes.json")))["banks"]))
    rec = json.load(open(os.path.join(game_data, "oracle", "game", "map_and_back.json")))["passes"]
    banks["map"] = next(p["colours"] for p in rec if "screen" in p)
    frames_sheet(anim, banks, range(1, 100), os.path.join(out, "animation_frames.png"))
    pointer_sheet(open(os.path.join(game_data, "main.bin"), "rb").read(), os.path.join(out, "pointers.png"))
    print("sheets written to", out)


if __name__ == "__main__":
    main(sys.argv[1])
