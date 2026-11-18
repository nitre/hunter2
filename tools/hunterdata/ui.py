"""Start-up screens' data, read from the program. See re/formats/startup.md.

- Credits text: $20B8, drawn by FUN_0000213a via $C1B2; zero-terminated, $B794 prefixes "]" and
  appends "*". Control codes ($C1FA): "*" end, "]" new line, "<" centre the line, "}nn" tab.
- Menus ($ACEA): header word = first button y, then 26-byte entries until a byte with bit 7 set
  where the next entry's style would be ($ACEA: tst.b (A4); bmi): $21F0 = $FF ends $216C's table,
  $2268 = $FFFF ends $224C's, which holds only its title (GameMenu draws its items itself).
  +0 style (frame = style + $17), +1 x offset (signed, *2 from the screen centre), +2 text
  ("*"-terminated, 16 bytes), +$12 two longs (hot-spot data / handler).
- Screen mode 0 descriptor ($5F50): palette bank 5, geometry 4 (320x200, 4 planes), text window
  col 0, row 0, 40 columns, 20 lines, 10 px line height, plane 0.
"""
import struct

BASE = 0x800
CREDITS = 0x20B8
MAIN_MENU = 0x2032
GAME_MENU = 0x224C          # AVAILABLE GAMES (header word $0028 just before the title entry)
OPTIONS_MENU = 0x216C       # IN GAME OPTIONS (ShowMenuWaitFadeRun $2164, the Esc quit's menu)
CONFIRM_MENU = 0x21FA       # ConfirmMenu $21F2: "Are you sure" (Yes $9064 / No $9060)
DISK_MENU = 0x100B8         # DiskMenuRun $10154 lea here: DISK OPTIONS / Load Game / Save Game /
                            # Init Disk / End ($100D2 is the table *inside* that menu's code)


def credits_text(main):
    a = CREDITS - BASE
    end = main.index(b"\x00", a)
    return "]" + main[a:end].decode("latin-1") + "*"


GAME_ITEMS = 0x226A        # Hunter, Missions, Action, CANCEL - drawn one by one by $2322-$2346
SOLDIER = 0x85E2           # 32-byte soldier record; byte +2 = completed-games bits (hide menu items)


def _item(main, a):
    style, dx = main[a], main[a + 1]
    text = main[a + 2:a + 18].split(b"*")[0].decode("latin-1")
    h1, h2 = struct.unpack_from(">II", main, a + 18)
    return {"style": style, "dx": dx - 256 if dx > 127 else dx, "text": text, "data": h1, "handler": h2}


def game_items(main):
    """$2322: for modes 0-2 draw the entry unless bit `mode` of $85E4 is set; then CANCEL."""
    return [_item(main, GAME_ITEMS - BASE + 26 * i) for i in range(4)]


def menu(main, addr):
    a = addr - BASE
    y0 = struct.unpack_from(">H", main, a)[0]
    a += 2
    items = []
    while main[a] < 0x80:
        items.append(_item(main, a))
        a += 26
    return {"y": y0, "items": items}
