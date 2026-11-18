using System;
using System.Linq;
using Godot;
using Hunter.Rules;

namespace Hunter;

// HUNTER_LIVE's commands (Live.cs): read and change the running game. Numbers: decimal, or 0x hex.
public partial class GameView
{
    readonly Live? live = System.Environment.GetEnvironmentVariable("HUNTER_LIVE") is { } port ? new Live(int.Parse(port)) : null;
    FreeCamera? liveCam;                                   // cam: held over every other camera until "cam off"
    Joystick liveJoy;                                      // joy: held over the real stick for liveJoyLeft passes
    int liveJoyLeft;

    bool liveFreeze;                                       // freeze on: the game moves only while joy or run has frames left
    int liveRunLeft;
    int liveSteps;                                         // game steps run (frame): Audio.Frame counts the recording's passes, frozen ones too

    /// <summary>Freeze on and nothing held or running: this frame does nothing (the clock stands too).</summary>
    bool LiveFrozen()
    {
        if (liveFreeze && liveJoyLeft == 0)
        {
            if (liveRunLeft == 0) return true;
            liveRunLeft--;
        }
        liveSteps++;
        return false;
    }

    Func<string>? liveWatch;                               // watch: what is watched, read as text
    string? liveWatchWas, liveWatchHit;

    /// <summary>Once a frame: a watched value that changed stops the game (freeze on, nothing held or running)
    /// and keeps what it was, what it became and what the game was doing.</summary>
    void LiveWatch()
    {
        if (liveWatch is null) return;
        var now = liveWatch();
        if (now == liveWatchWas) return;
        liveWatchHit = $"changed at frame {liveSteps}: {liveWatchWas} -> {now}; mode {play.Mode}, waiting {play.Waiting}, quit {play.Quit}, "
            + $"paused {play.Paused}, inside {play.State.InsideView}, end screens {ending is not null}, briefing {brief is not null}";
        liveWatchWas = now;
        liveFreeze = true; liveJoyLeft = 0; liveRunLeft = 0;
    }

    Joystick? LiveJoy()
    {
        if (liveJoyLeft == 0) return null;
        liveJoyLeft--;
        return liveJoy;
    }

    Hunter.Rules.FineGround? cardFine; byte[]? cardMap;

    /// <summary>run_game --input "N:freeze": the live tool's "freeze on" at frame N.</summary>
    public void LiveFreeze() => liveFreeze = true;

    const string LiveHelp = """
        ents                      live records: row type x y z ground model(+$25) shadow(+$24)
        get ROW OFF [N]           N bytes (default 2) of record ROW from OFF, hex
        set ROW OFF HEXBYTES      write bytes into record ROW from OFF (e.g. set 0 0x25 3c)
        move ROW X Z [Y]          place record ROW (Y default: the ground there)
        place REC X Z [TURN]      move world grid record REC (tools/probes/world_types.py's number)
        setting KEY LABEL         a setting as the menu words it (e.g. setting haze FINE)
        ground X Z [X Z ...]      the ground's height at each place
        clock HOUR MINUTE         the game clock (the sun follows)
        cam X Y Z YAW PITCH [ZOOM] | cam off   hold the 3D camera (world units, radians)
        pause                     press p: the game's own pause, on/off (the clock stops)
        shot PATH                 the window as a PNG
        frame                     the frame number, clock, player place
        wire on|off               Godot's own wireframe of the 3D view (a test)
        eye                       the camera's eye height, its place, the smooth and drawn ground there, the highest ground to the player
        corners ROW [shadow|raw|old]  where the card puts ROW's triangle corners (or its shadow's; raw = before the ground fit;
                                  old = the fit by heights only, as before vehicles turned, with the turned body's lift): slot corner x y z colour
        joy DIRS FRAMES           hold the stick (letters u d l r f, f = fire; "-" = let go) for FRAMES frames
        log                       the log book's places as its little map marks them (to the nearest 512-unit square), from you
        carry                     what you carry: each item and how many
        status                    the play checks in one: place and clock, screen open or not, on foot / swimming / in what, last message
        dronemap                  the marks the last drone map (the screen the drone opens) drew: kind and place on the map (what a player sees, as data)
        overlay [X Y W H]         the overlay map (key M, drawn over play) as rows of colour digits, and where you are on it
        radar                     the panel's radar as rows of dots (when it is on)
        view                      what the 3D view shows, nearest first: name, left / right / ahead, how far
        buttons                   the open screen's buttons (map, log book ...) and where to click each
        watch ROW OFF [N] | watch state NAME | watch hit | watch off
                                  stop the game (freeze) the frame the value changes; "watch hit" says when and what the game was doing
        freeze on|off        on: the game stands still (clock too) except while joy or run has frames left
        run FRAMES                let the game run FRAMES frames with the stick let go (for freeze on)
        key HEX                   a key going down (raw Amiga code, e.g. key 0x40 = space)
        click X Y                 a left click at game-picture X Y (320 x 200)
        state [NAME [OFF N]]      the game's state (rules WorldState): no NAME lists every name; a byte array prints N bytes hex from OFF
                                  (e.g. state Items 0 64 = what is carried, rules Games.cs Unmet reads it)
        text                      the last 200 lines of text drawn anywhere (messages, log book, map, store, menus), oldest first
        mission                   the mission record ($9072 + $20A, 16 bytes hex), day, hour, objectives still open (Games.Unmet)
        """;

    /// <summary>Moves world grid record rec (tools/probes/world_types.py's number) to x:z - into the first free slot
    /// of that place's cell ($82BA), as a write-back would; the old slot freed; turn: the turn byte (0 = north,
    /// growing east). HUNTER_PLACE and the live "place".</summary>
    string PlaceRecord(int rec, int x, int z, int? turn)
    {
        var grid = play.State.Grid;
        var from = rec * 16;
        var cell = ((z >> 1) & 0x7800) + ((x >> 5) & 0x780);
        var to = Enumerable.Range(0, 8).Select(k => cell + k * 16).FirstOrDefault(r => (grid[r] | grid[r + 1]) == 0, -1);
        if (to < 0) return $"cell of {x}:{z} full";
        Array.Copy(grid, from, grid, to, 16);
        if (to != from) Array.Clear(grid, from, 16);
        grid[to + 6] = (byte)(x >> 8); grid[to + 7] = (byte)x;
        grid[to + 10] = (byte)(z >> 8); grid[to + 11] = (byte)z;
        // its height too (+8): the ground at the new place. Left as the old place's, the thing hung in the air or sat in
        // the ground there, and the game's pitch for a parked vehicle ($42A6: its height less the ground ahead) came out steep
        var h = play.State.Ground.Height((ushort)x, (ushort)z);
        grid[to + 8] = (byte)(h >> 8); grid[to + 9] = (byte)h;
        if (turn is { } t) grid[to + 12] = (byte)t;
        return $"record {rec} -> slot {to / 16} at {x}:{z}";
    }

    // "corners": the card's own placing (gpumodels.gdshader's model_clip: the fit, walls down; gpushadow.gdshader: on the
    // ground) run for one record's triangle corners, read back - world units, 16ths, 24 bits a value
    const string CornersShader = """
        shader_type canvas_item;
        render_mode blend_disabled;
        #include "res://real3d/place.gdshaderinc"
        #include "res://real3d/world_colour.gdshaderinc"
        #include "res://real3d/sunshade.gdshaderinc"
        #include "res://real3d/gpumodels.gdshaderinc"
        #include "res://real3d/view.gdshaderinc"
        #include "res://real3d/ground_contact.gdshaderinc"
        uniform int live_row;
        uniform int live_raw;                                   // 1: the corners as turned and placed, without the ground fit; 2: the fit by heights only (the rule before vehicles turned)

        void fragment() {
            ivec2 fc = ivec2(FRAGCOORD.xy);
            int id = live_row * tri_slots * 3 + fc.y;
            vec3 p; int colour; bool pull; ivec3 rot; bool own_side;
            bool ok;
            if (layer == 0) {
                ok = gpu_model_vertex_rot(id, p, colour, pull, rot, own_side);
                vec4 fit; vec2 c;
                if (ok && live_raw != 1 && contact_fit_row(live_row, fit, c)) {
                    if (live_raw == 2) fit.w = CONTACT_NO_TURN;
                    p = contact_turn(fit, c, p, float(rot.x) * 0.5, float(rot.z) * 0.5); p.y = walls_down(c, p.x, p.z, p.y);
                }
            } else {
                ok = gpu_model_vertex(id, p, colour, pull);
                if (ok && cam_on && !inside_view) p.y = float(ground_height(cam_map, int(round(p.x)) & 0xFFFF, int(round(p.z)) & 0xFFFF));
            }
            float v = fc.x == 0 ? p.x : (fc.x == 1 ? p.y : (fc.x == 2 ? p.z : (ok ? float(colour) + 1.0 : 0.0)));
            int iv = int(round(v * 16.0));
            COLOR = vec4(float(iv & 0xFF) / 255.0, float((iv >> 8) & 0xFF) / 255.0, float((iv >> 16) & 0xFF) / 255.0, 1.0);
        }
        """;

    /// <summary>Where the card puts record row's triangle corners this frame: "slot corner x y z colour" a line,
    /// world units; shadow = its shadow layer (on the ground), else the model as drawn.</summary>
    async System.Threading.Tasks.Task<string> LiveCorners(int row, bool shadow, int raw = 0)
    {
        var gpu = real3d?.Gpu ?? throw new InvalidOperationException("no card view (renderer=3d)");
        var src = shadow ? gpu.ShadowsMaterial : gpu.ModelsMaterial;
        var slots = (int)src.GetShaderParameter("tri_slots");
        var mat = new ShaderMaterial { Shader = new Shader { Code = CornersShader } };
        foreach (var u in src.Shader.GetShaderUniformList())
        {
            var name = (string)u.AsGodotDictionary()["name"];
            mat.SetShaderParameter(name, src.GetShaderParameter(name));
        }
        mat.SetShaderParameter("layer", shadow ? 1 : 0);
        mat.SetShaderParameter("live_row", row);
        mat.SetShaderParameter("live_raw", raw);
        var size = new Vector2I(4, slots * 3);
        var vp = new SubViewport { Size = size, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Once };
        AddChild(vp);
        vp.AddChild(new ColorRect { Size = size, Material = mat });
        await ToSignal(GetTree(), "process_frame");
        await ToSignal(GetTree(), "process_frame");
        var img = vp.GetTexture().GetImage();
        vp.QueueFree();
        double V(int x, int y) { var c = img.GetPixel(x, y); return ((c.R8 | c.G8 << 8 | c.B8 << 16) << 8 >> 8) / 16.0; }
        var lines = new System.Collections.Generic.List<string>();
        for (var y = 0; y < size.Y; y++)
            if (V(3, y) > 0) lines.Add($"{y / 3} {y % 3} {V(0, y)} {V(1, y)} {V(2, y)} {V(3, y) - 1}");
        return lines.Count == 0 ? "none drawn" : string.Join("\n", lines);
    }

    System.Threading.Tasks.Task<string> LiveRun(string line)
    {
        var a = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length > 1 && a[0] == "corners") return LiveCorners(ParseNum(a[1]), a.Length > 2 && a[2] == "shadow", a.Length < 3 ? 0 : a[2] == "raw" ? 1 : a[2] == "old" ? 2 : 0);
        return System.Threading.Tasks.Task.FromResult(LiveCommand(line));
    }

    // the shell's screens outside play (MenuUp): play.Mode stays Play under them and the freeze does not hold them
    string? ShellScreen() =>
        brief is not null ? "the briefing / HQ report" : ending is not null ? "the HQ report (the game's end screens)"
        : soldierScreen is not null ? "the soldier panel" : options is not null ? "the options menu"
        : confirm is not null ? "a yes / no box" : DiskUp ? "the disk menu" : null;

    string LiveCommand(string line)
    {
        var a = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0) return "";
        int N(int i) => ParseNum(a[i]);
        var ents = play.State.Entities.All;
        switch (a[0])
        {
            case "help": return LiveHelp;
            case "ents":
                return string.Join("\n", Enumerable.Range(0, ents.Length).Where(r => ents[r].B[0] != 0).Select(r =>
                {
                    var e = ents[r];
                    return $"{r} ${e.W(4):X2} {e.UW(0x4A)} {e.W(0x4C)} {e.UW(0x4E)} {e.W(0x74)} {e.B[0x25]} {e.B[0x24]}";
                }));
            case "get":
            {
                var e = ents[N(1)];
                int o = N(2), n = a.Length > 3 ? N(3) : 2;
                return string.Join(" ", e.B.Skip(o).Take(n).Select(b => b.ToString("x2")));
            }
            case "set":
            {
                var e = ents[N(1)];
                var bytes = Convert.FromHexString(string.Concat(a.Skip(3)));
                bytes.CopyTo(e.B, N(2));
                return $"ok {bytes.Length}";
            }
            case "move":
            {
                var e = ents[N(1)];
                int x = N(2), z = N(3);
                e.SetW(0x4A, x); e.SetW(0x4E, z);
                var g = play.State.Ground.Height((ushort)x, (ushort)z);
                e.SetW(0x4C, a.Length > 4 ? N(4) : g);
                e.SetW(0x74, g);                                                        // the ground under it, as the game keeps it
                return "ok";
            }
            case "place": return PlaceRecord(N(1), N(2), N(3), a.Length > 4 ? N(4) : null);
            case "setting": SettingsApply(a[1], string.Join(' ', a.Skip(2))); return "ok";
            case "ground": return string.Join(" ", Enumerable.Range(0, (a.Length - 1) / 2).Select(i =>   // height, and L (land) or S (sea) as movement's $36AA sees it
            {
                var (h, land, _) = play.State.Ground.Sample((ushort)N(1 + 2 * i), (ushort)N(2 + 2 * i));
                return $"{h}{(land ? "L" : "S")}";
            }));
            case "clock": play.State.Hour = (short)N(1); play.State.Minute = (short)N(2); return "ok";
            case "cam":
                if (a.Length > 1 && a[1] == "off") { liveCam = null; return "ok"; }
                liveCam = new FreeCamera((double.Parse(a[1]), double.Parse(a[2]), double.Parse(a[3])), float.Parse(a[4]), float.Parse(a[5]),
                    a.Length > 6 ? float.Parse(a[6]) : 1f);
                return "ok";
            case "joy":
                liveJoy = new Joystick(a[1].Contains('u'), a[1].Contains('d'), a[1].Contains('l'), a[1].Contains('r'), a[1].Contains('f'));
                liveJoyLeft = N(2);
                return "ok";
            case "key": KeyDown((byte)N(1)); return "ok";
            case "dronemap":                                                                  // what the drone map shows: kind, place on the map
            {
                string[] kinds = ["vehicle", "ground gun", "structure", "building", "person"];   // mask bits 0-4: the buttons' moveq values (Screens.Click)
                var marks = play.Screens.MapMarks;
                return marks.Count == 0 ? "no map drawn yet"
                    : "map pixels, 64 world units each; you at 112-119, 64-71 (x right = east, y down = south)\n"
                      + string.Join("\n", marks.Select(m => $"{(m.Bit < kinds.Length ? kinds[m.Bit] : "bit " + m.Bit)} {m.X} {m.Y}"));
            }
            case "overlay":                                                                 // the overlay map (M): its 128 x 128 pixels as colour digits
            {
                var m = play.State.Log.Marks;                                               // as MapMarksGpu.Encode reads it
                int x0 = a.Length > 1 ? N(1) : 0, y0 = a.Length > 2 ? N(2) : 0, w = a.Length > 3 ? N(3) : 128, h = a.Length > 4 ? N(4) : 128;
                var p = ents[0];
                var sb = new System.Text.StringBuilder($"overlay map {(play.State.MapMarks ? "on" : "off")}; a pixel = 512 world units, x = east, y = 128 - north; you at {p.UW(0x4A) >> 9} {128 - (p.UW(0x4E) >> 9)}; '.' = no mark, else the colour (hex)\n");
                for (var row = y0; row < Math.Min(128, y0 + h); row++)
                {
                    sb.Append($"{row,3} ");
                    for (var col = x0; col < Math.Min(128, x0 + w); col++)
                    {
                        int at = row * 16 + (col >> 3), bit = 7 - (col & 7);
                        sb.Append((m[0x2000 + at] >> bit & 1) == 0 ? '.'
                            : "0123456789abcdef"[(m[at] >> bit & 1) | (m[at + 0x800] >> bit & 1) << 1 | (m[at + 0x1000] >> bit & 1) << 2 | (m[at + 0x1800] >> bit & 1) << 3]);
                    }
                    sb.Append('\n');
                }
                return sb.ToString();
            }
            case "radar":                                                                   // the panel's radar as a player sees it
            {
                if (!play.State.Radar) return "radar off (key r, when you carry the radar)";
                var rows = play.State.Panel.RadarRows();
                return "radar: a cell = 256 world units, north up, you at the + (row 16, column 15); # = something there\n"
                    + string.Join("\n", rows.Select((r, y) => new string(Enumerable.Range(0, 32).Select(c =>
                        (r >> (31 - c) & 1) != 0 ? '#' : y == 16 && c == 15 ? '+' : '.').ToArray())));
            }
            case "view":                                                                    // what the game draws in the 3D view (+$1C, Objects.Place), as words
            {
                var main = play.State.Data.Main;
                var inside = play.State.InsideView;                                         // inside: every room thing (+$20), the room's camera stands still
                var seen = Enumerable.Range(1, ents.Length - 1).Where(r => ents[r].B[0] != 0 && (inside ? ents[r].B[0x20] != 0 : ents[r].B[0x1C] != 0 && ents[r].B[0x20] == 0)).Select(r =>
                {
                    var e = ents[r];
                    int cx = inside ? (short)(e.W(0x4A) - ents[0].W(0x4A)) : (e.W(0x56) - ents[0].W(0x56)) / 2,   // from the player; camera units are doubled world units
                        cz = inside ? (short)(e.W(0x4E) - ents[0].W(0x4E)) : (e.W(0x5A) - ents[0].W(0x5A)) / 2;
                    var name = System.Text.Encoding.Latin1.GetString(Text.TableText(main, 1, e.W(4))).Trim();
                    if (e.W(4) == 0x60) name += " (explosion pieces)";             // the game's own name for it is "a"
                    var side = cz < 0 ? "behind" : Math.Abs(cx) < cz / 4 ? "ahead" : cx < 0 ? "left" : "right";
                    var door = "";
                    if (e.W(0x68) != e.W(0x62))                                             // a door box (+$62-$68, Buildings.DoorTest): its middle on the south face
                    {
                        int dx = (short)(e.W(0x4A) + (e.W(0x62) + e.W(0x68)) / 2 - ents[0].W(0x4A)), dz = (short)(e.W(0x4E) + e.W(0x66) - ents[0].W(0x4E));
                        door = $"; door {Math.Abs(dx)} {(dx < 0 ? "left" : "right")}, {Math.Abs(dz)} {(dz < 0 ? "behind" : "ahead")} (walk north into it)";
                    }
                    return (Dist: (int)Math.Sqrt(cx * cx + cz * cz), Line: $"{name}: {side}, {(int)Math.Sqrt(cx * cx + cz * cz)} away ({Math.Abs(cx)} {(cx < 0 ? "left" : "right")}, {Math.Abs(cz)} {(cz < 0 ? "behind" : "ahead")}){door}, row {r}");
                }).OrderBy(t => t.Dist).Select(t => t.Line);
                return "in view (the view faces north; from you, in world units):\n" + string.Join("\n", seen);
            }
            case "buttons":                                                                 // the open screen's buttons: where to click
                if (ShellScreen() is { } up)
                    return brief is not null || ending is not null || soldierScreen is not null
                        ? $"{up}: one click anywhere leaves it (click 160 100)" : $"{up}: its buttons are not listed yet";
                return play.Mode == PlayMode.Play ? "no screen open (in play)"
                    : string.Join("\n", play.Screens.Buttons.Select(b => $"{b.Text}: click {(b.X0 + b.X1) / 2} {(b.Y0 + b.Y1) / 2} (x {b.X0}-{b.X1}, y {b.Y0}-{b.Y1})"));
            case "watch":
            {
                if (a.Length > 1 && a[1] == "off") { liveWatch = null; return "watch off"; }
                if (a.Length > 1 && a[1] == "hit") return liveWatchHit ?? "no change yet";
                if (a.Length > 2 && a[1] == "state") { var path = a[2]; liveWatch = () => LiveCommand("state " + path); }
                else { int row = N(1), off = N(2), n = a.Length > 3 ? N(3) : 2; liveWatch = () => string.Join(" ", ents[row].B.Skip(off).Take(n).Select(b => b.ToString("x2"))); }
                liveWatchWas = liveWatch(); liveWatchHit = null;
                return $"watching: {liveWatchWas}";
            }
            case "freeze": liveFreeze = a[1] == "on"; return $"freeze {(liveFreeze ? "on" : "off")}";
            case "run": liveRunLeft = N(1); return "ok";
            case "click":                                                                   // the pointer first: a press lands where the pointer is, and a scripted run never hovers
                mouseX = N(1); mouseY = N(2); play.Mouse(mouseX, ClickY(mouseY));
                Click(N(1), N(2)); return "ok";
            case "state":                                                                   // any field or property of the game's state, by name
            {
                var s = play.State;
                if (a.Length < 2)
                    return string.Join(" ", s.GetType().GetFields().Select(f => f.Name).Concat(s.GetType().GetProperties().Select(p => p.Name)).Order());
                object? v = s;                                                              // a dotted path goes deeper (Soldier.R)
                foreach (var part in a[1].Split('.'))
                    v = v?.GetType().GetField(part)?.GetValue(v) ?? v?.GetType().GetProperty(part)?.GetValue(v);
                if (v is byte[] b)
                {
                    int o = a.Length > 2 ? N(2) : 0, n = a.Length > 3 ? N(3) : Math.Min(64, b.Length - o);
                    return $"{b.Length} bytes; from {o}: " + string.Join(" ", b.Skip(o).Take(n).Select(x => x.ToString("x2")));
                }
                return v is System.Collections.IEnumerable e and not string ? string.Join(" ", e.Cast<object>()) : $"{v ?? "no such name"}";
            }
            case "text": Ui.Drawn.Flush(); lock (Ui.Drawn.Lines) return string.Join("\n", Ui.Drawn.Lines);
            case "mission":
            {
                var s = play.State;
                var rec = 0x9072 + s.Mission;
                var bytes = string.Join(" ", s.Data.Main.Skip(rec - 0x800).Take(16).Select(b => b.ToString("x2")));
                return $"mission ${s.Mission:X2} record {bytes} day {s.Day} hour {s.Hour} open {Games.Unmet(s, rec)}";
            }
            case "pause": KeyDown(0x19); return play.State.PauseKey ? "paused" : "running";   // the 'p' key ($B33A raw $19): PauseLoop's own way in and out
            case "shot": GetViewport().GetTexture().GetImage().SavePng(a[1]); return "ok";
            case "carry":                                                                   // the player's item table: count +$00, most +$02 per item ($A240)
            {
                var t = play.State.PlayerAt(0).Items;
                var main = play.State.Data.Main;
                string[] names = { "hover", "clock", "radar", "log book", "map marks", "item 14 (adds energy)", "item 15 (adds fuel)", "flare", "drone", "item 18 (gets you out, as Space)", "item 19 (changes your look)" };   // items 9-19 (re/formats/game.md, the item table)
                var o = new System.Text.StringBuilder();
                for (var i = 0; i < 30; i++)
                {
                    int at = 0x28 + i * 32, count = (short)(t[at] << 8 | t[at + 1]), most = (short)(t[at + 2] << 8 | t[at + 3]);
                    if (count <= 0 && (sbyte)t[at + 0x19] >= 0) continue;
                    var shot = t[at + 0x1E] << 8 | t[at + 0x1F];
                    var name = i < 9 ? (shot != 0 ? "gun firing " + System.Text.Encoding.Latin1.GetString(Text.TableText(main, 1, shot & 0xFF)).Trim() : "weapon " + i)
                        : i < 20 ? names[i - 9] : "item " + i;
                    o.Append($"{name}: {count} (most {most})\n");
                }
                return o.Length == 0 ? "carrying nothing" : o.ToString().TrimEnd();
            }
            case "log":                                                                  // the log book's places as its map marks them: a 512-unit square ($4076 x >> 9, z >> 9)
            {
                var lb = play.State.Log;
                var me = ents[0];
                return string.Join("\n", Enumerable.Range(0, LogBook.Entries).Where(lb.Noted).Select(n =>
                {
                    int o = n * LogBook.Size, x = (lb.Places[o] << 8 | lb.Places[o + 1]) >> 9 << 9 | 256, z = (lb.Places[o + 2] << 8 | lb.Places[o + 3]) >> 9 << 9 | 256;
                    int dx = x - me.UW(0x4A), dz = z - me.UW(0x4E);
                    return $"{n}{(n == lb.Selected ? " (selected)" : "")} {lb.Name(n).Trim()}: about x {x} z {z} - {Math.Abs(dx)} {(dx < 0 ? "west" : "east")}, {Math.Abs(dz)} {(dz < 0 ? "south" : "north")} of you";
                }));
            }
            case "status":                                                                  // the play checks in one: place, screen, vehicle, last message
            {
                Ui.Drawn.Flush();
                string last; lock (Ui.Drawn.Lines) last = Ui.Drawn.Lines.LastOrDefault()?.Trim() ?? "";
                var type = ents[0].W(4);
                return LiveCommand("frame") + $"\n{(ShellScreen() is { } shell ? $"A SCREEN IS OPEN: {shell} (see buttons)" : play.Mode == PlayMode.Play ? "no screen open" :$"A SCREEN IS OPEN: {play.Mode} ${play.Screens.Routine:X4}{(play.Fade.FadeActive ? ", fading" : "")} (see buttons)")}"
                    + $"\n{(type == 0x1E ? ((play.State.PlayerAt(play.State.PlayerOf(ents[0])).Status219 & 0x40) != 0 ? "SWIMMING - in the sea: sharks, energy drains, get out" : "on foot") : "in a "+ System.Text.Encoding.Latin1.GetString(Text.TableText(play.State.Data.Main, 1, type)).Trim())}"
                    + $"\nlast message: {last}"
                    + (play.Pilot is null ? "" : $"\nplayed by {play.Pilot.GetType().Name}{(play.Pilot is Bot { Brain: Hunt h } ? $": {h.Doing}" : "")}");   // a bot run: who has the stick and what it is doing
            }
            case "frame":
            {
                var p = ents[0];
                return $"frame {liveSteps} clock {play.State.Hour:00}:{play.State.Minute:00} player {p.UW(0x4A)} {p.W(0x4C)} {p.UW(0x4E)} inside {play.State.InsideView}";
            }
            case "yaw":   // the 3D view turned as Q / E turn it: yaw N, N in 256ths of a turn (64 = looking east)
                if (real3d is null) return "ERROR no 3D view";
                real3d.Yaw = -N(1) / 256f * Mathf.Tau;
                return $"yaw {N(1)}";
            case "wire":   // a test: Godot's own wireframe of the 3D view (does it follow the shaders that place the world?)
                if (real3d is null) return "ERROR no 3D view";
                real3d.Viewport.DebugDraw = a.Length > 1 && a[1] == "on" ? Viewport.DebugDrawEnum.Wireframe : Viewport.DebugDrawEnum.Disabled;
                return $"wire {real3d.Viewport.DebugDraw}";
            case "eye":   // the camera's eye (Ground.Place: $0E = max(y, smooth ground there) + $5A + lift) against the drawn ground
            {
                var s = play.State; var c = s.Camera; var g = s.Ground; var p = ents[0];
                ushort cx = (ushort)((uint)c.G1548 >> 1), cz = (ushort)((uint)c.G1544 >> 1);
                int px = p.UW(0x4A), pz = p.UW(0x4E), top = int.MinValue, atX = 0, atZ = 0;
                for (var i = 0; i <= 32; i++)                                     // the highest drawn ground from the camera to the player
                {
                    ushort x = (ushort)(cx + (px - cx) * i / 32), z = (ushort)(cz + (pz - cz) * i / 32);
                    int h = g.Height(x, z); if (h > top) { top = h; atX = x; atZ = z; }
                }
                return $"eye {c.Height0E} lift {s.Lift} distance {s.Distance} camera {cx} {cz} smooth {g.SmoothHeight(cx, cz)} drawn {g.Height(cx, cz)} " +
                       $"player {px} {p.W(0x4C)} {pz} highest-between {top} at {atX} {atZ}" +
                       $" | card built from: fine {(ReferenceEquals(cardFine, g.Fine) ? "same" : "OTHER")} map {(ReferenceEquals(cardMap, s.Terrain.Map) ? "same" : "OTHER")}" +
                       (cardFine is { } cf ? $" card ground at camera {cf.Height(cx, cz)}" : "") +
                       (play.PictureCamera is { } pc ? $" | picture camera {(uint)pc.G1548 >> 1} {(uint)pc.G1544 >> 1} eye {pc.Height0E}" : " | no picture camera");
            }
            default: return "ERROR unknown command (help)";
        }
    }
}
