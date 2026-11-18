"""Build game_data/ from the user's own Disk.1.

usage: python -I -m hunterdata.build DISK1 OUTDIR   (run from tools/)
"""
import os
import sys
from dataclasses import dataclass, field

import json

import struct

from . import animation, decrypt, disk, font, gltf, music, objfile, pictures, sapfile, sinfile, sound, ui

PICTURES = ("loadpic1", "panfile", "storepic", "epic")
PALETTE_BANKS, PALETTE_BANK_COUNT = 0x6318, 7   # 32 x $0RGB each; $64E0 copies bank N to COLOR00-31


def palette_banks(prog):
    return [list(struct.unpack_from(">32H", prog, PALETTE_BANKS - 0x800 + 64 * b)) for b in range(PALETTE_BANK_COUNT)]


@dataclass
class Report:
    gaps: list = field(default_factory=list)


def main(disk_path, out_dir):
    image = open(disk_path, "rb").read()
    prog, _ = decrypt.decrypt_main(image)
    files_dir = os.path.join(out_dir, "files")
    os.makedirs(files_dir, exist_ok=True)
    open(os.path.join(out_dir, "main.bin"), "wb").write(prog)
    os.makedirs(os.path.join(out_dir, "tables"), exist_ok=True)
    json.dump({"source": "$6318, 64 bytes per bank; bank 4 ($6418) is filled by the IFF loader",
               "banks": palette_banks(prog)}, open(os.path.join(out_dir, "tables", "palettes.json"), "w"))
    report = Report()
    for e in disk.read_table(prog):
        data = disk.read_file(image, e)
        # loadpic2: its 6 own tracks are blank; its 32000-byte size runs past them into epic
        if not any(data[:e.ntracks * disk.TRACK_BYTES]):
            report.gaps.append(e.name)
        open(os.path.join(files_dir, "%02d_%s.bin" % (e.index, e.name)), "wb").write(data)
        if e.name in PICTURES:
            pic = pictures.decode(data)
            os.makedirs(os.path.join(out_dir, "pics"), exist_ok=True)
            pictures.to_png(pic, os.path.join(out_dir, "pics", e.name + ".png"))
            json.dump({"palette12": pic.palette12, "pixels_hex": pic.pixels.hex()},
                      open(os.path.join(out_dir, "pics", e.name + ".json"), "w"))
        if e.name in ("sapfile1", "sapfile2", "sapfile3"):
            mode = int(e.name[-1]) - 1
            w = sapfile.decode(data)
            os.makedirs(os.path.join(out_dir, "worlds"), exist_ok=True)
            json.dump({"mode": mode, "game": sapfile.MODES[mode], "checksum_ok": sapfile.checksum(data) == struct.unpack_from(">H", data, 0x26)[0],
                       "header_hex": data[:0x3E8].hex(),
                       "objects": [{"slot": o.slot, "words": o.words, "x": o.x, "z": o.z,
                                    "model": sapfile.model_index(prog, o.kind)} for o in w.objects()],
                       "containers_hex": [c.hex() for c in w.containers()]},
                      open(os.path.join(out_dir, "worlds", sapfile.MODES[mode] + ".json"), "w"))
        if e.name == "animation":
            _, frames = animation.decode(data)
            out = []
            for f in frames:
                w, h, px = animation.pixels(f)
                out.append({"index": f.index, "width": w, "height": h, "pixels_hex": px.hex()})
            json.dump({"note": "opaque decode (blit mode 1); frame N = index N", "frames": out},
                      open(os.path.join(out_dir, "tables", "frames.json"), "w"))
            json.dump({"credits": ui.credits_text(prog), "main_menu": ui.menu(prog, ui.MAIN_MENU),
                       "game_menu": ui.menu(prog, ui.GAME_MENU), "game_items": ui.game_items(prog),
                       "options_menu": ui.menu(prog, ui.OPTIONS_MENU),
                       "confirm_menu": ui.menu(prog, ui.CONFIRM_MENU),
                       "disk_menu": ui.menu(prog, ui.DISK_MENU),
                       "completed_games_bits": prog[ui.SOLDIER - 0x800 + 2],
                       "screen0": {"palette_bank": 5, "text": {"col": 0, "row": 0, "cols": 40, "lines": 20,
                                                               "line_height": 10, "plane": 0}}},
                      open(os.path.join(out_dir, "tables", "ui.json"), "w"), indent=1)
        if e.name == "storepic":
            main_f, second_f, widths = font.fonts(data)
            json.dump({"source": "storepic bitplane 0, rows 48 ($8372, main) and 104 ($837A); widths per $8590",
                       "main_hex": [g.hex() for g in main_f], "second_hex": [g.hex() for g in second_f],
                       "widths": widths}, open(os.path.join(out_dir, "tables", "font.json"), "w"))
        if e.name == "soundfile":
            sd = os.path.join(out_dir, "sound")
            os.makedirs(sd, exist_ok=True)
            fx = sound.effects(data)
            for s in sound.samples(data):
                first = next((f for f in fx if f.sample == s.index), None)
                rate = round(sound.PAL_CLOCK / first.period) if first else 8287
                sound.write_wav(s.pcm, rate, os.path.join(sd, "sample_%02d.wav" % s.index))
            json.dump({"pal_clock": sound.PAL_CLOCK,
                       "samples": [{"index": s.index, "length_words": s.length_words, "loop_offset": s.loop_offset,
                                    "repeat_words": s.repeat_words} for s in sound.samples(data)],
                       "effects": [vars(f) for f in fx], "envelopes_hex": sound.envelopes(data).hex()},
                      open(os.path.join(sd, "sound.json"), "w"), indent=1)
        if e.name == "musfile":
            md = os.path.join(out_dir, "music")
            os.makedirs(md, exist_ok=True)
            for i, pcm, rep in music.samples(data):
                sound.write_wav(pcm, 8287, os.path.join(md, "instrument_sample_%02d.wav" % i))
            json.dump({"periods": music.periods(data),
                       "samples": [{"index": i, "length": len(pcm), "repeat_words": rep} for i, pcm, rep in music.samples(data)],
                       "instruments_hex": [x.hex() for x in music.instruments(data)],
                       "patterns": [None if music.pattern(data, n) is None else [vars(ev) for ev in music.pattern(data, n)]
                                    for n in range(music.pattern_count())],
                       "songs": music.songs(data)}, open(os.path.join(md, "music.json"), "w"))
        if e.name == "objfile":
            os.makedirs(os.path.join(out_dir, "models"), exist_ok=True)
            for m in objfile.decode(data).models:
                gltf.write(m, os.path.join(out_dir, "models", "%03d.gltf" % m.index))
        if e.name == "sinfile":
            s, c = sinfile.decode(data)
            os.makedirs(os.path.join(out_dir, "tables"), exist_ok=True)
            json.dump({"format": "[exponent, mantissa], value = mantissa * 2^-exponent, 256 steps/turn",
                       "sin": s, "cos": c}, open(os.path.join(out_dir, "tables", "sinfile.json"), "w"))
    inventory = {}
    for root, _, names in os.walk(out_dir):
        for n in names:
            rel = os.path.relpath(os.path.join(root, n), out_dir).replace(os.sep, "/")
            if rel != "inventory.json":
                inventory.setdefault(rel.split("/")[0], []).append(rel)
    json.dump({"gaps": report.gaps, "outputs": {k: sorted(v) for k, v in sorted(inventory.items())}},
              open(os.path.join(out_dir, "inventory.json"), "w"), indent=1)
    return report


if __name__ == "__main__":
    try:
        r = main(sys.argv[1], sys.argv[2])
    except decrypt.UnsupportedDisk as ex:
        sys.exit(str(ex))
    print("built %s; known gaps (blank on this disk): %s" % (sys.argv[2], ", ".join(r.gaps) or "none"))
