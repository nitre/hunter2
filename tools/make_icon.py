"""Write game_data/hunters_loading.ico - the desktop shortcut's icon, the same picture as the game window's
(rules/GameIcon.cs): the loading screen, logo and all (game_data/pics/loadpic2.json - the 199 x 199 square from
column 61, row 0) when it exists, else the soldier screen's face (frame $12, bank 6, the 43 x 47 face inside its
colour-14 frame). Centred on black; whole-number scales where the picture fits, scaled down smoothly below that.
From the player's own game data, so it lives in game_data/ (not in git).
Run from the repo root: python -I tools/make_icon.py"""
import json, os
from PIL import Image

data = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "game_data")
rgb = lambda c: tuple((c >> s & 15) * 17 for s in (8, 4, 0))
loading = os.path.join(data, "pics", "loadpic2.json")
if os.path.exists(loading):
    X0, Y0, S = 61, 0, 199                                  # rules/GameIcon.cs LoadingX0 / LoadingY0 / LoadingSide
    p = json.load(open(loading))
    px, pal = bytes.fromhex(p["pixels_hex"]), p["palette12"]
    pic = Image.new("RGB", (S, S))
    pic.putdata([rgb(pal[px[(Y0 + y) * 320 + X0 + x]]) for y in range(S) for x in range(S)])
else:
    FRAME, BANK, X0, Y0, W, H = 0x12, 6, 1, 1, 43, 47     # rules/GameIcon.cs Frame / Bank / X0 / Y0 / Width / Height
    f = {e["index"]: e for e in json.load(open(os.path.join(data, "tables", "frames.json")))["frames"]}[FRAME]
    px = bytes.fromhex(f["pixels_hex"])
    bank = json.load(open(os.path.join(data, "tables", "palettes.json")))["banks"][BANK]
    pic = Image.new("RGB", (W, H))
    pic.putdata([rgb(bank[px[(Y0 + y) * f["width"] + X0 + x]]) for y in range(H) for x in range(W)])
sizes = []
for side in (16, 24, 32, 48, 64, 128, 256):
    img = Image.new("RGB", (side, side))
    k = min(side // pic.width, side // pic.height)
    if k >= 1:      # whole-number scale, sharp pixels
        big = pic.resize((pic.width * k, pic.height * k), Image.NEAREST)
    else:           # smaller than the picture: the whole picture scaled down, not a piece cut out
        r = side / max(pic.width, pic.height)
        big = pic.resize((max(1, round(pic.width * r)), max(1, round(pic.height * r))), Image.LANCZOS)
    img.paste(big, ((side - big.width) // 2, (side - big.height) // 2))
    sizes.append(img)
out = os.path.join(data, "hunters_loading.ico")   # a new name: Windows caches a shortcut's icon by its path
sizes[-1].save(out, sizes=[(s.width, s.height) for s in sizes], append_images=sizes[:-1])
print("wrote", os.path.abspath(out), "from", "the loading screen" if os.path.exists(loading) else "the soldier's face")
