# hunter2

An unofficial remake of Hunter (Activision, 1991, Amiga), written in C# with Godot 4.7 and Claude Code.

![Helicopter over the coast](screenshots/flying.png)

<img src="screenshots/car.png" width="49%" alt="Car"> <img src="screenshots/boat.png" width="49%" alt="Rocket boat by the stores">
<img src="screenshots/attack.png" width="49%" alt="Helicopter attacking a gun"> <img src="screenshots/tank_blast.png" width="49%" alt="A tank destroyed on the hills">
<img src="screenshots/tank.png" width="49%" alt="Tank firing"> <img src="screenshots/on_foot.png" width="49%" alt="On foot by a helicopter">
<img src="screenshots/sunset.png" width="49%" alt="Sunset over the sea"> <img src="screenshots/night.png" width="49%" alt="A tank at night under the moon and stars">
<img src="screenshots/inside.png" width="49%" alt="Inside a building"> <img src="screenshots/beach.png" width="49%" alt="On foot along the coast">

## Features

### Graphics
- Expanded colour palette
- 50 fps
- Free camera rotation (no longer locked facing north)
- Debris and fireball explosions
- Per-object shadows

### World
- Gradient sky, clouds and haze
- Finer ground mesh with more colour variation
- Animated sea
- Extended view distance, widescreen

### Models
- Round wheels, rotor masts, animated tank tracks
- Vehicle interiors and see-through glass

### Gameplay
- Game rules taken from the original's assembly code
- Experimental world mode: more objects on the islands, people and animals that react to each other, graves
- Fixes for several original bugs

### Sound
- More sound channels for effects

### Input
- Gamepad with analog steering
- Rebindable keys

## Controls

- Arrow keys or numeric keypad: move / steer (keypad 0 or right Ctrl fires)
- Gamepad: d-pad or left stick, A fires
- Space: get out of a vehicle (parachute when flying)
- Return: next weapon
- Esc: options screen (resume, re-brief, save/load, quit)
- P: pause
- C, R, L, M, F, A: clock, radar, log book, map marks, flare, drone (when carried)
- Page Up / Page Down: zoom the camera
- Q / E: turn the 3D view
- F2: change key bindings
- Mouse: the panel, item table, map, log book and store

## Settings

The settings menu is grouped into pages:

- Screen: TV look, fullscreen, the 3D view on or off, pixel size, screen shape
- Land and sky: view distance, ground lighting and colours, sky, clouds, sea
- Models: model colours, see-through glass, animation, shadows, explosions
- World: more objects, finer ground, vehicle exit side, bullet size, A500 timing
- Sound: Amiga filter, extra sound effects, voices
- Controls: message log, analog steering, mouse aiming, keys

The release build starts with these changes turned on. A build run from source asks on first start whether to
turn them on.

## Download

Windows builds are on the [Releases](../../releases) page. Unzip and run `hunter2.exe`.

There are no macOS or Linux builds yet.

## Building

Requirements:

- .NET 10 SDK
- Godot 4.7.2, .NET version

```
cd godot
dotnet build
<path to Godot>/Godot_v4.7.2-stable_mono_win64.exe --path .
```

On Windows, `hunter2.bat` does the same if the `GODOT` environment variable points at the Godot executable.

To export a build, open `godot/project.godot` in the Godot editor and use Project > Export (a Windows Desktop preset
is included). Copy `game_data/` next to the exported executable.

## Game data

`game_data/` holds the files the game reads: the original program, models, worlds, pictures, sound and music.
`HUNTER_DATA` can point the game at another folder.

The data can be regenerated from an Amiga disk image of the original game:

```
cd tools
python -m hunterdata.build <path to Disk.1> ../game_data
```

## Layout

- `rules/` - game rules, world, objects, save files
- `godot/` - window, input, sound, menus
- `godot/real3d/` - the 3D view (shaders and model building)
- `tools/hunterdata/` - reads the original disk into `game_data/`
- `game_data/` - game data

## Credits

Hunter was designed and programmed by Paul Holmes, with music and sound effects by Martin Walker, and published by
Activision in 1991. This project is not affiliated with them.
