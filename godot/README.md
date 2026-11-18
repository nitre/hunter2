# Hunter remake - Godot project

Godot **4.7.2 .NET** (installed at `C:\LAB\tools\godot\Godot_v4.7.2-stable_mono_win64\`).

1. Build the game data from your own disk image (once):
   `cd tools && python -m hunterdata.build <path to Disk.1> ../game_data`
2. Build the C#: `cd godot && dotnet build`
3. Run: `Godot_v4.7.2-stable_mono_win64.exe --path godot` (or open `godot/project.godot` in the editor).

`HUNTER_DATA` overrides where game data is read from (default `../game_data`).

What it does (milestone 1): title, credits, main menu. Mouse moves the crosshair, left click picks.
View soldier shows the soldier panel. Start new game -> pick Hunter / Missions / Action -> type a
name (Return ends, Backspace deletes; nothing typed = "Trigger Happy") -> briefing types out, click
-> the game. Disk Operation and Demo do nothing yet.

In the game: on foot the joystick moves the soldier toward that side of the screen (manual
4.1); walk into the sea to swim; buildings stop you. Walk into an empty vehicle to get in
(milestone 3): up / down = faster / slower, left / right steer the vehicle; helicopters need
fire with up (take off, more power) or down; Space gets out (in the air: a parachute).
- Joystick = the arrow keys (two held = a diagonal), fire = right Ctrl; the numeric keypad always
  works too (8 2 4 6, diagonals 7 9 1 3, 0 fires - the original's keys); or a gamepad's d-pad /
  left stick, fire = A. (Default since 2026-11-01, for keyboards without a keypad.)
- PageUp / PageDown: camera zoom out / in (the original used cursor up / down; read as a key
  state: never sent to KeyDown, so they cannot fire twice). F2 changes any of these.
- Keys, each with the Amiga raw key code the original's keyboard interrupt hands to KeyDown
  ($B214; `re/formats/input.md` and the original's own ASCII table at $B33A, `PlayInput.cs`):
  Space $40 gets out of a vehicle; Return $44 next weapon; Esc $45 quit to the options screen;
  P $19 pause; C $33 clock; R $13 radar; L $28 log book; M $37 map marks; F $23 flare; A $20
  drone; the key left of 1 (` ~) $5F the item hover. C R L M F and ` do what a click on that
  carried item's cell of the item table does, and nothing when the item is not carried: a fresh
  game carries only items 9, 17 and 18, so at the start only A (the drone, which opens the map
  100 blanks later) and ` do anything. Shift and Ctrl go with the key (the original's +$70
  table and its Ctrl-drop).
- Mouse, left button: in play the panel's regions (the weapon icon, SPEAK / GIVE / QUIT,
  SAVE + QUIT, `$2460`) and the item table; on the map, the log book and the store the screen's
  own buttons; on the options screen its four menu buttons. The pointer is the panel's row
  ($E0 = the screen row - 140), so a click above the panel's top edge is row 0, which opens the
  item table at the end of the pass - exactly what moving the pointer above the border does in
  the original. The pointer sprite is drawn (the options screen and, in play, over the panel).
  Positions come from `Display.ToGame`, both looks; a click outside the 320 x 200 picture is
  ignored.
- Esc leaves play for the in-game options screen ($2164, ShowMenuWaitFadeRun: the menu in
  palette bank 5, faded in): Resume Game plays on with the world, the clock and the soldier
  where the quit left them; Re-brief runs the briefing again and plays on; Quit Game asks
  "Are you sure" and Yes ends the soldier's career back at the title; Disk Operation opens
  the disk menu (save/load, GameViewDisk.cs).
- Ported in the rules (milestones 4-5, checked against the original): people's behaviour,
  weapons, items, damage and death; the panel (gauges, clock, energy, rounds, icons), its
  messages, in-play keys (c r l m f a ~, Return = next weapon, P pause, Esc quit), the map, log
  book and item tables, soldiers' talk, gifts and clues.
- Wired since that note was written: the debrief / end screens at the end of a game
  (GameView.cs StartEnd), save/load with the disk menu (GameViewDisk.cs), sound (Audio.cs,
  four channels + music), and buildings' insides (rules/Buildings.cs, Play.Buildings.cs).
- The demo (rules/Demo.cs): the Demo button, or 1500 frames on the main menu with no click, plays the
  original's recording (file 11) in the Action game; any key ends it, then the title, credits and menus.
  ENHANCEMENT (not the original, setting smooth_demo, default on): the demo is drawn every frame with
  in-between pictures of the original's passes (rules/Play.Smooth.cs); the game itself keeps the original's pace.
  ENHANCEMENT (not the original, setting own_shadows, default off): every object casts a half-bright (EHB)
  shadow from its own shape (rules/Objects.cs OwnShadows); off is the original's black 13-type shadows.
  ENHANCEMENT (not the original, setting smooth_light, default off): the daylight drifts minute by minute
  between the hour table's levels (rules/Play.cs SmoothLight); off is the original's hold-then-step each hour.
  ENHANCEMENT (not the original, setting sky_steps, default off): the sky's six flat bands become a stepped
  Amiga-style gradient of only in-between colours the Amiga can show (rules/Sky.cs Steps); off is the original's
  six flat bands.
  ENHANCEMENT (not the original, setting far_view, default off): the ground and objects are drawn farther
  (rules/Play.cs FarView); off is the original's byte-identical view. With it on the farther ground draws, but
  few things live out there yet - a later change (more_objects) widens the live area.
  ENHANCEMENT (not the original, setting more_objects, default off): more live things and a bigger live area -
  30 live records (29 handed out, the last reserved) become 256 (255 handed out) and the spawn/live window
  2 x 2 cells becomes 4 x 4 (rules/Play.cs MoreObjects); the save format does not change (the save holds no
  live-thing table). With far_view on as well, the farther view has things in it. Off is byte-identical.
  ENHANCEMENT (not the original, setting message_log, default off): the panel's 2-line message window keeps
  the last 64 lines that scrolled off its top; the mouse wheel over the window or Page Up / Page Down scroll
  back through them (a '^' marks the window's right end, a new message returns to live) (rules/Text.cs
  history, rules/Play.cs drawing); off records nothing and is byte-identical to the original.
  ENHANCEMENT (not the original, setting roomy_memory, default off): the original's three memory squeezes are
  lifted - a parked thing keeps its fuel's low byte, a full grid cell takes more than 8 records, and the kill
  counts do not wrap at a byte (rules/Play.cs RoomyMemory); the save's original $C880 bytes stay as they are,
  the extras ride after them as a small extension block (a save without one loads as before; loading a save
  with one and the setting off drops the extras at the next save); off is byte-identical, save included.
  ENHANCEMENT (not the original, setting graves, default off): each paid death - never the last - leaves a
  grave holding what the soldier carried (out of play, so no free resources), and the next soldier starts at
  home with the normal starting kit (rules/Play.cs Graves); the life bar, 4 a death and the last death are
  the original's, unlimited graves in one game; the graves ride the save's extension block, off is
  byte-identical, save included.
  ENHANCEMENT (not the original, setting st_daylight, default off): the Atari ST build's brighter, longer
  days - the 24-hour light table every reader takes (the panel clock's light $18F, smooth_light's per-hour
  levels) is the ST's (rules/StDaylightTable.cs, from an ST disk image) instead of the Amiga's $C4A4;
  off is byte-identical to the original.
  ENHANCEMENT (not the original, setting sound_position, default off): each channel pans by where its owner
  is on screen (godot/Audio.cs SetPan, one panner bus per channel; the player and behind-camera owners stay
  centre); off is the original's hard-wired 0,3 left / 1,2 right.
  ENHANCEMENT (not the original, setting sound_tails, default off): a sound still playing when its channel is
  taken again fades out over 80 ms on a spare player (godot/Audio.cs); off is the original's dead cut.
  ENHANCEMENT (not the original, setting music_in_play, default off): the song keeps playing during a hunt at
  12 dB down (godot/Audio.cs EnterPlay); off is the original's Music(-1) the moment play starts.
  ENHANCEMENT (not the original, setting easy_saves, DEFAULT ON): saving and loading without the disk
  handling - the first Load or Save sets the save folder up itself (nothing existing is wiped), the disk
  menu hides Init Disk (Load Game / Save Game / End keep the original's positions), a save left unnamed
  names itself (the game day and the slot), and F5 quick saves to its own file 22 with F4 loading it
  back ("Game saved" / "Game loaded" / "No quick save" in the message box); off is the original's disk
  menu exactly.
Smooth play: a whole pass of the original's loop every 50 Hz frame; movement is time-scaled as
the original does, and animations step once per 7 frames (the A500's usual pass length).

Headless checks (use the `_console.exe`): `HUNTER_CAPTURE="frame:file.png,..."` steps that many
50 Hz frames, saves them and quits; `HUNTER_INPUT="300:click:160:80,360:click:160:64,420:type:Bob,480:enter"`
plays the player's part before the given frames: `click:x:y` (the menus' buttons, or - in a
game - game-picture pixels, 320 x 200; while the manual is up, its footer hints), `type:text`,
`enter`, `manual` (F3: open / close the manual over whatever is showing), `page:left|right`
(a manual page turn), `key:hex` (a raw Amiga key code
straight to the game view, as the keyboard interrupt delivers it: `key:37` = 'm', `key:44` =
Return, `key:45` = Esc, `key:20` = 'a'), `key:pgup` / `key:pgdn` (message_log's Page Up / Page
Down) and `wheel:up|down` (message_log's mouse wheel, at the message window's middle) - both
built as the very InputEvent Godot delivers for a real wheel tick / key press and sent through
the same `_Input` (GameView.cs), so the feature's own bounds checks all run, `quicksave` /
`quickload` (easy_saves' F5 / F4),
`joy:udlrf` (held until the next `joy:`, `joy:` =
released), `zoom:out|in|off` and `space`; `HUNTER_GAME=hunter|missions|action` starts a game's
view directly, and `HUNTER_ITEMS="13:1,1:6"` (verification only) puts items in its pack, since a
fresh game carries only items 9, 17 and 18. `HUNTER_START=x:z` (verification only, headless runs,
never in a settings file) carries the player to that world position before its first pass - the
tests' start-state move (FarViewTests / MoreObjectsTests At, entity 0's words $4A / $4E); decimal
or 0x-hex, e.g. the busy camera cell (9,12) is `0x9800:0xC800`; `HUNTER_START=kind:n` picks the n-th spot of a
kind from the map (vehicle, building, high, low, shore, sea - GameView.FuzzStart, the fuzzer's). `HUNTER_STOP_OVER=1`
ends a capture run when play is over (500 frames out of play with nothing live) and prints each death (`HUNTER: death
at frame N: was .. became .. at x,z sea|land ... near type@distance`). `HUNTER_STATS=1` prints
`HUNTER: frame N live M` (the live things, player apart, his type, place and screen) every captured frame, and on a window range `HUNTER: window frames X ms each` (wall time a drawn frame, vsync off). `HUNTER_CAPTURE_WINDOW="frame:file.png"` saves the
whole window - the viewport texture, the window's own resolution - instead of the 320 x 200 game
image; the F1 key list (`HelpOverlay`, needs a window, so not with `--headless`) shows only there
and never in a `HUNTER_CAPTURE` frame. A GPU video: run the GUI exe (not `--headless`) from boot with
`renderer="gpu"` in the settings file, `HUNTER_CAPTURE_WINDOW="from-to:folder"` and `HUNTER_SOUND_LOG`, then
`tools/make_video.py` (window frames keep their size). `"from-to:name.mp4"` instead pipes the frames straight into
ffmpeg (no picture files); make_video takes that .mp4 in place of the folder and adds the sound. With no `HUNTER_GAME` and no input the main menu starts
the demo on its own; its briefing is up from frame 1726 (2026-10-23 run, 1280 x 800 window).

Save/load regression check: `python -I tools/check_saveload.py` from the repo root (needs Pillow; build
`godot/` first; Godot is found at the GODOT environment variable or the default path in the script). It runs
the disk menu headless three times - saves to slot 0 as "Test" (and checks file15.bin / file16.bin), loads that
save in a fresh process and starts it from the briefing, and captures a fresh game - then compares the loaded
game's picture with the fresh one: once the loaded game has settled (a few hundred frames after the
briefing's click) they must be the same picture, panel included. It uses only `.scratch/saveload_tmp/` (its
own saves folder via HUNTER_SAVES), never the real user:// saves. About 4 seconds; exit code 0 only when all
6 checks pass. Put the old Rebrief-on-load bug back and it fails with about 33,000 pixels differing.

Display: the whole game renders into a 320x200 image (the original resolution), shown by
`HUNTER_LOOK` (read once at start, `Display.cs`), else the settings file's TV LOOK row; F10 steps through them:
- `pal`: the picture at the top of a 320x256 PAL frame (56 black lines below, as on a
  PAL TV - the game's display window is lines $2C-$F3 of the $2C-$12B PAL frame), the whole frame
  shown 4:3: the largest whole vertical scale that fits the window, width round(4/3 of that),
  centred, nearest-neighbour; one game pixel is 16/15 as wide as tall.
- `centred` (default): the same 16/15 pixels without the 56 spare lines - 320x200
  alone, 200k tall and round(1024k/3) wide, in the middle of the window.
- `stretched`: the 320x200 viewport scaled by whole numbers, square pixels, centred.
Headless captures are the plain 320x200 game image in every look. `HUNTER_TABLE=1` prints the
layout for a few window sizes and quits.

The F1 key list (HelpOverlay): a window-resolution CanvasLayer over the picture, monospace
(Consolas / Lucida Console / Courier New) at about 1/32 of the window height (min 14 px), key
bright / what it does in grey, two columns, grouped Move / Vehicles / Weapons / Items / Screens,
in a square 1 px-bordered box, dark and slightly see-through, centred, never wider than the
picture. F1 toggles it; the game runs on underneath.
