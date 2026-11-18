using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>HUNTER_REPLAY=<oracle recording .json>: replay the ORIGINAL game's own recording in
/// this build, one pass per frame, exactly the way the rules tests run one (the same loader shape
/// as rules/tests/VehicleTests.cs Trace / NewPlay / Run / Begin - the recording's world patch,
/// energy, key presses, clicks, pokes and per-pass blank counts), and measure against the
/// recording's own fields: the player record (position, type), the camera (zoom, target,
/// distance, lift), the palette and the whole 320 x 200 screen. Lines go to stdout as
/// "REPLAY p<N>: ..." (run_godot saves them); frames HUNTER_REPLAY_FRAMES names are saved as
/// <recording>_f<frame>.png in HUNTER_REPLAY_OUT. This is verification only: nothing here runs
/// without HUNTER_REPLAY.</summary>
public sealed class Replay
{
    public sealed record Pass(int Index, byte Joy, bool Fire, int Blanks, int Ticks, byte? Key, byte[] Entities, byte[] Player,
        int JoyBlanks = 1, int CamBlanks = 1, int CopyBlanks = 0,
        byte[]? Screen = null, int? PanelBlanks = null, int ScreenBlanks = 0,
        int KeyD7 = 0, (int X, int Y)? Mouse = null, int PauseFrom = 0, int PauseBlanks = 0, bool Quit = false,
        int ScreenFrom = 0, int ResumeAt = 0, List<(byte[] Pixels, int X, int Y)>? Screens = null, List<byte[]>? Typed = null,
        bool GameOver = false, List<(int Addr, byte[] Data)>? Pokes = null,
        bool Inside = false, int ExitAt = 0, int LeaveAt = 0, int VisitAt = 0, int PlaceAt = 0, int LeavePlayAt = 0,
        List<int[]>? Palette = null,
        byte[]? Keys = null, int Zoom = 0, int Target = 0, int Distance = 0, int Lift = 0);

    readonly GameView view;
    readonly List<Pass> passes = [];
    readonly HashSet<int> frames = [];
    readonly byte[] rgb = new byte[320 * 200 * 3];                      // made once, filled each pass
    readonly string name, outDir;
    /// <summary>The screen the pass before drew, compared where the original showed it: at this
    /// pass's time-step copy ($623A), after Begin - the same moment the rules tests compare.</summary>
    byte[]? shownWant;
    int shownPass = -1, shownDiff = -1;
    /// <summary>The pass after the quit runs on a resumed game ($23D2 ResumeAfterSave: the save's
    /// menu screens sat between them, the same order the rules tests' RebriefTests replay them in).</summary>
    bool resumeNext;
    public int n;

    public GameView View => view;
    public string Label => $"replay {name} pass {n}/{passes.Count}";

    /// <summary>Starts the game the way the rules tests do (VehicleTests.NewPlay): the recording's
    /// mode, its place patches into the world file and its energy.</summary>
    public Replay(Main main, GameData data, string path)
    {
        name = System.IO.Path.GetFileNameWithoutExtension(path);
        outDir = System.Environment.GetEnvironmentVariable("HUNTER_REPLAY_OUT") ?? ".";
        view = new GameView();
        main.AddChild(view);
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var d = GameData.Load(data.Directory);
        var mode = (GameMode)doc.RootElement.GetProperty("mode").GetInt32();
        var world = d.WorldFile(mode);
        foreach (var p in doc.RootElement.GetProperty("place").EnumerateArray())
            System.Convert.FromHexString(p[1].GetString()!).CopyTo(world, p[0].GetInt32());
        var energy = doc.RootElement.TryGetProperty("energy", out var en) && en.ValueKind == JsonValueKind.Number ? (short)en.GetInt32() : (short)0x20;
        view.InitReplay(data, new Play(d, mode, world, energy));     // never own_shadows: a replay is checked against the original's screens
        Load(doc);
        var spec = System.Environment.GetEnvironmentVariable("HUNTER_REPLAY_FRAMES");
        if (spec is { Length: > 0 }) frames.UnionWith(spec.Split(',').Select(int.Parse));
        else frames.UnionWith(new[] { 0, passes.Count / 4, passes.Count / 2, passes.Count * 3 / 4, passes.Count - 1 });
        GD.Print($"REPLAY {name}: {passes.Count} passes, frames {string.Join(",", frames.OrderBy(f => f))}");
    }

    void Load(JsonDocument doc)
    {
        var presses = doc.RootElement.GetProperty("presses").EnumerateArray()
            .ToDictionary(p => p[0].GetInt32(), p => (byte)p[1].GetInt32());
        var mouse = doc.RootElement.TryGetProperty("mouse", out var ms)
            ? ms.EnumerateArray().ToDictionary(p => p[0].GetInt32(), p => (p[1].GetInt32(), p[2].GetInt32()))
            : [];
        int Int(JsonElement p, string name, int def = 0) => p.TryGetProperty(name, out var v) ? (int)v.GetInt64() : def;
        var clicks = new[] { "clicks", "options" }.Where(n => doc.RootElement.TryGetProperty(n, out _))
            .SelectMany(n => doc.RootElement.GetProperty(n).EnumerateArray())
            .ToDictionary(c => c[0].GetInt32(), c => (c[1].GetInt32(), c[2].GetInt32()));
        var typed = doc.RootElement.TryGetProperty("typed", out var ty)
            ? ty.EnumerateArray().ToDictionary(t => t[0].GetInt32(), t => t[1].EnumerateArray().Select(c => (byte)c.GetInt32()).ToArray())
            : [];
        var over = doc.RootElement.TryGetProperty("gameover", out var go) ? go.EnumerateArray().Select(x => x.GetInt32()).ToHashSet() : [];
        var pokes = doc.RootElement.TryGetProperty("pokes", out var pk)
            ? pk.EnumerateArray().Select(x => (Pass: x[0].GetInt32(), Addr: x[1].GetInt32(), Data: System.Convert.FromHexString(x[2].GetString()!))).ToList() : [];
        var i = 0;
        var screens = new List<(byte[], int, int)>();
        var keys = new List<byte[]>();
        foreach (var p in doc.RootElement.GetProperty("passes").EnumerateArray().ToList())
        {
            if (p.TryGetProperty("ended", out _)) continue;
            if (p.TryGetProperty("screen", out var sn))
            {
                if (p.GetProperty("routine").GetString() == "$34E2") continue;
                var (cx, cy) = clicks[sn.GetInt32()];
                screens.Add((System.Convert.FromHexString(p.GetProperty("pixels_hex").GetString()!), cx, cy));
                keys.Add(typed.TryGetValue(sn.GetInt32(), out var t) ? t : []);
                continue;
            }
            passes.Add(new Pass(i, (byte)p.GetProperty("joy").GetInt32(), p.GetProperty("fire").GetInt32() != 0,
                p.GetProperty("blanks").GetInt32(), p.GetProperty("ticks").GetInt32(), presses.TryGetValue(i, out var k) ? k : null,
                System.Convert.FromHexString(p.GetProperty("entities_hex").GetString()!),
                System.Convert.FromHexString(p.GetProperty("player_hex").GetString()!),
                Int(p, "joy_blanks", 1),
                Int(p, "cam_blanks", 1),
                Int(p, "copy_blanks"),
                p.TryGetProperty("screen_hex", out var sc) ? System.Convert.FromHexString(sc.GetString()!) : null,
                p.TryGetProperty("panel_blanks", out var pb) ? pb.GetInt32() : null,
                Int(p, "screen_blanks"),
                Int(p, "key_d7"), mouse.TryGetValue(i, out var mo) ? mo : null,
                Int(p, "pause_from"), Int(p, "pause_blanks"), p.TryGetProperty("quit", out _),
                Int(p, "screen_from"), Int(p, "resume_at"), screens,
                [.. keys],
                over.Contains(i), pokes.Where(q => q.Pass == i).Select(q => (q.Addr, q.Data)).ToList(),
                p.TryGetProperty("inside", out _), Int(p, "exit_at"), Int(p, "leave_at"), Int(p, "visit_at"), Int(p, "place_at"),
                Int(p, "leave_play_at"),
                p.TryGetProperty("palette", out var pl)
                    ? pl.EnumerateArray().Take(2).Select(a => a.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToList() : null,
                p.TryGetProperty("keys", out var kh) ? kh.EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray() : null,
                Int(p, "zoom"), Int(p, "target"), Int(p, "distance"), Int(p, "lift")));
            screens = [];
            keys = [];
            i++;
        }
    }

    /// <summary>One replayed pass per call: the pass as the tests run it, then the frame (the live
    /// Godot picture), the measurement and the frame saves. Quits the tree after the last pass and
    /// returns true - Main just calls Step until it says done.</summary>
    public bool Step()
    {
        if (n >= passes.Count) { Carry(); view.GetTree().Quit(); return true; }
        var p = passes[n];
        if (resumeNext)                                                          // $23D2, after the save's screens
        {
            view.ReplayPlay.Resume();
            while (view.ReplayPlay.Starting) view.ReplayPlay.VerticalBlank();    // PlayStart's fade: before $2530, where the recording's passes count from
        }
        resumeNext = false;
        var note = "";
        try
        {
            Run(view.ReplayPlay, p);
        }
        catch (System.NotSupportedException e)
        {
            note = $" NOTPORTED {e.Message}";
        }
        view.ReplayRender(rgb);
        Measure(p, note);
        if (p.Quit)                                                              // $24D6: the quit, then the save's screens
        {
            view.ReplayPlay.EndPlay(Stick(p));
            resumeNext = true;
        }
        shownWant = p.Screen;
        shownPass = p.Index;
        shownDiff = -1;          // the next pass's Begin samples it at $623A
        n++;
        if (n >= passes.Count)
        {
            GD.Print($"REPLAY {name}: end, {passes.Count} passes");
            Carry();
            view.GetTree().Quit();
            return true;
        }
        return false;
    }

    /// <summary>HUNTER_REPLAY_CARRY (verification only): the recording's passes are done - a
    /// capture cannot click its way to a mission end, so the run says when the game is over and
    /// this runs the quit's tail by hand (view.ReplayEnd: the debrief, then FinishEnd's flow) and
    /// clicks the screens through, saving frames as they go. Everything after ReplayEnd is the
    /// real shell: the end screens, then $23CA's next briefing for the ended soldier's own record.
    /// The saved PNGs and the two CARRY lines (the soldier record and mission before and after)
    /// are the evidence; the name on the briefing is the record's own.</summary>
    void Carry()
    {
        if (System.Environment.GetEnvironmentVariable("HUNTER_REPLAY_CARRY") is null) return;
        var play = view.ReplayPlay;
        string Name(Soldier s) => System.Text.Encoding.ASCII.GetString(Pregame.RecordName(s));
        var sol = play.State.Soldier;
        GD.Print($"CARRY before: soldier '{Name(sol)}' score {sol.Long(0xC)} rank {sol.Word(8)} status {sol.R[4]} mission {play.State.Mission:X4}");
        view.ReplayEnd();                                        // the quit's tail: EndGame -> the end screens
        for (var f = 0; f < 1200; f++)
        {
            view.Frame();                                        // the end screens, then the next briefing
            if (f % 40 == 20) view.Click(160, 100);              // WaitClick $AC84 takes any position
            if (f % 20 == 0 || (f >= 30 && f <= 70))     // the briefing is up around f35-f60: save it densely
                view.Save(System.IO.Path.Combine(outDir, $"{name}_carry_f{f:D4}.png"));
        }
        var after = play.State.Soldier;
        GD.Print($"CARRY after: soldier '{Name(after)}' score {after.Long(0xC)} rank {after.Word(8)} status {after.R[4]} mission {play.State.Mission:X4} mode {play.Mode}");
    }

    /// <summary>The player record, the camera and the screen against the recording's own fields.</summary>
    void Measure(Pass p, string note)
    {
        var play = view.ReplayPlay;
        var got = play.State.Entities.Bytes();
        var bad = new List<string>();
        var diffs = Enumerable.Range(0, 0xA0).Where(k => got[k] != p.Player[k]).ToList();
        var cam = new List<string>();
        if (play.State.Zoom != (short)p.Zoom) cam.Add($"zoom {play.State.Zoom}/{p.Zoom}");
        if (play.State.Target != p.Target) cam.Add($"target {play.State.Target}/{p.Target}");
        if (play.State.Distance != p.Distance) cam.Add($"distance {play.State.Distance}/{p.Distance}");
        if (play.State.Lift != (short)p.Lift) cam.Add($"lift {play.State.Lift}/{p.Lift}");
        var (px, py, pz) = (p.Player[0x4A] << 8 | p.Player[0x4B], (short)(p.Player[0x4C] << 8 | p.Player[0x4D]), p.Player[0x4E] << 8 | p.Player[0x4F]);
        var (mx, my, mz) = (got[0x4A] << 8 | got[0x4B], (short)(got[0x4C] << 8 | got[0x4D]), got[0x4E] << 8 | got[0x4F]);
        if (px != mx || py != my || pz != mz) bad.Add($"pos ({mx},{my},{mz})/({px},{py},{pz})");
        if (got[4] != p.Player[4] || got[5] != p.Player[5]) bad.Add($"type ${got[4]:X2}{got[5]:X2}/${p.Player[4]:X2}{p.Player[5]:X2}");
        var other = diffs.Where(k => k is < 0x4A or > 0x4F && k != 4 && k != 5).ToList();
        if (other.Count > 0) bad.Add($"rec +{string.Join(",+", other.Take(6).Select(k => $"{k:X2}={got[k]:X2}/{p.Player[k]:X2}"))}{(other.Count > 6 ? $" +{other.Count - 6} more" : "")}");
        if (cam.Count > 0) bad.Add("cam " + string.Join(" ", cam));
        if (play.LastTicks != p.Ticks) bad.Add($"ticks {play.LastTicks}/{p.Ticks}");
        if (play.Quit != p.Quit) bad.Add($"quit {play.Quit}/{p.Quit}");
        // the drawn screen, right after the pass's Draw (the original's screen_hex is what it had
        // on show; the tests compare at the NEXT pass's $623A, which is the S number below)
        var drawn = p.Screen is { } want ? ScreenDiff(play.ScreenIndices().Pixels, want) : -1;
        if (shownWant is { } sw && shownDiff < 0) shownDiff = ScreenDiff(play.ScreenIndices().Pixels, sw);
        var pal = "";
        if (p.Palette is { } want2)
        {
            var (v, _) = play.Palette();
            var bad2 = Enumerable.Range(0, 15).Where(i => v[i] != want2[0][i]).ToList();
            pal = $" palbad={bad2.Count}{(bad2.Count > 0 ? $" c{bad2[0]}={v[bad2[0]]:X3}/{want2[0][bad2[0]]:X3}" : "")}";
        }
        if (note.Length > 0 || bad.Count > 0 || drawn > 0 || shownDiff > 0 || frames.Contains(p.Index))
        {
            var pos = $"p ({mx},{my},{mz}) type ${got[4]:X2}{got[5]:X2}";
            var line = $"REPLAY {name} p{p.Index}: {(bad.Count == 0 && note.Length == 0 ? "ok" : "BAD " + string.Join(" ", bad))}{note} {pos} D={drawn} S(p{shownPass})={shownDiff}{pal}";
            if (bad.Count > 0 || note.Length > 0) GD.PushWarning(line);
            else GD.Print(line);
        }
        if (frames.Contains(p.Index)) Save(p.Index);
    }

    static int ScreenDiff(byte[] got, byte[] want)
    {
        var bad = Enumerable.Range(0, want.Length).Where(i => ((got[i] ^ want[i]) & 0xF) != 0).ToList();
        return bad.Count == 0 ? 0 : bad.Count;
    }

    void Save(int pass)
    {
        var path = System.IO.Path.Combine(outDir, $"{name}_f{pass}.png");
        view.Save(path);
        // the frame's raw screen indices and palette, so the measurement (python) can compare the
        // PNG with the recording's screen_hex without guessing colours
        var (pixels, banks) = view.ReplayPlay.ScreenIndices();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, $"{name}_f{pass}.idx"), pixels);
        var pal = view.ReplayPlay.Palette();
        using var w = new System.IO.BinaryWriter(System.IO.File.Open(System.IO.Path.Combine(outDir, $"{name}_f{pass}.pal"), System.IO.FileMode.Create));
        for (var which = 0; which < 2; which++)
            foreach (var c in which == 0 ? pal.View : pal.Panel) w.Write(c);
        GD.Print($"REPLAY {name}: frame pass {pass} -> {path}");
    }

    Joystick Stick(Pass p) => Joystick.FromD6((byte)(p.Joy | (p.Fire ? 0x10 : 0)));

    (bool ZoomOut, bool ZoomIn) ZoomKeys(Pass p) =>
        (p.Keys is { } k && k.Contains((byte)0x4C), p.Keys is { } k2 && k2.Contains((byte)0x4D));

    /// <summary>The pass up to its time-step copy ($623A), as the tests' Begin.</summary>
    void Begin(Play play, Pass p)
    {
        if (p.Key is { } key) play.KeyDown(key, d7: p.KeyD7);
        if (p.Mouse is { } m) play.Click(m.X, m.Y);
        foreach (var (addr, data) in p.Pokes ?? []) Poke(play.State, addr, data);
        if (p.GameOver) Games.GameOver(play.State);
        var j = Stick(p);
        var (zoomOut, zoomIn) = ZoomKeys(p);
        for (var done = 0; done < p.CopyBlanks; done++) play.VerticalBlank(j, zoomOut, zoomIn);
        // the screen the pass BEFORE drew is on show exactly here ($623A) - the moment the rules
        // tests compare it at
        if (shownWant is { } sw && shownDiff < 0) shownDiff = ScreenDiff(play.ScreenIndices().Pixels, sw);
    }

    /// <summary>One pass, the tests' Run with the asserts turned into measurements.</summary>
    void Run(Play play, Pass p)
    {
        Begin(play, p);
        var j = Stick(p);
        var (zoomOut, zoomIn) = ZoomKeys(p);
        var done = p.CopyBlanks;
        play.Top();
        if (p.Inside) { RunInside(play, p, j, zoomOut, zoomIn, ref done); return; }
        if (p.PauseBlanks > 0)
        {
            for (; done < p.PauseFrom; done++) play.VerticalBlank(j, zoomOut, zoomIn);
            if (!play.Paused) GD.PushWarning($"REPLAY {name} p{p.Index}: the original paused here, the remake did not");
            for (var k = 0; k < p.PauseBlanks; k++, done++) play.VerticalBlank(j, zoomOut, zoomIn);
            play.KeyDown(0x19);
        }
        for (; done < p.JoyBlanks; done++) play.VerticalBlank(j, zoomOut, zoomIn);
        play.UpdateEntities();
        for (; done < p.CamBlanks; done++) play.VerticalBlank(j, zoomOut, zoomIn);
        play.CameraAndSort();
        for (; done < (p.PanelBlanks ?? p.Blanks); done++) play.VerticalBlank(j, zoomOut, zoomIn);
        play.Draw(endOfPass: p.ScreenFrom == 0 && p.VisitAt == 0);
        if (p.VisitAt > 0 && p.ScreenFrom == 0)
        {
            for (; done < p.VisitAt; done++) play.VerticalBlank(j, zoomOut, zoomIn);
            play.ManualResume = true;
            play.EndOfPass();
            if (p.PlaceAt > 0)
            {
                for (; done < p.PlaceAt; done++) play.VerticalBlank(j, zoomOut, zoomIn);
                play.FinishVisit();
            }
            play.ManualResume = false;
        }
        if (p.ScreenFrom > 0)
        {
            for (; done < p.ScreenFrom; done++) play.VerticalBlank(j, zoomOut, zoomIn);
            play.ExtraLeaveBlanks = System.Math.Max(0, p.LeavePlayAt - p.ScreenFrom);   // a store: its message took blanks before LeavePlay
            play.EndOfPass();
            play.ButtonDown = p.Screens!.Count > 0;
            for (; done < p.ResumeAt; done++) play.VerticalBlank(j, zoomOut, zoomIn);
            for (var k = 0; k < p.Screens.Count; k++)
            {
                var (pixels, x, y) = p.Screens[k];
                play.Click(x, y);
                foreach (var raw in p.Typed?[k] ?? []) play.KeyDown(raw);
                play.ButtonUp();                                             // let go before the next press (the map's $3D3C WaitButtonsUp)
                play.ButtonDown = true;
            }
            play.ButtonDown = false;
        }
        for (; done < p.Blanks; done++) play.VerticalBlank(j, zoomOut, zoomIn);
    }

    void RunInside(Play play, Pass p, Joystick j, bool zoomOut, bool zoomIn, ref int done)
    {
        for (; done < (p.PanelBlanks ?? 0); done++) play.VerticalBlank(j, zoomOut, zoomIn);
        play.InteriorBegin();
        for (; done < p.JoyBlanks; done++) play.VerticalBlank(j, zoomOut, zoomIn);
        play.UpdateEntities();
        play.CameraAndSort();
        play.Draw(endOfPass: false);
        if (p.ExitAt > 0)
        {
            for (; done < p.LeaveAt; done++) play.VerticalBlank(j, zoomOut, zoomIn);
            play.ManualResume = true;
            play.EndOfPass();
            for (; done < p.ExitAt; done++) play.VerticalBlank(j, zoomOut, zoomIn);
            play.ResumeAfterBuilding();
            play.ManualResume = false;
        }
        else play.EndOfPass();
        for (; done < p.Blanks; done++) play.VerticalBlank(j, zoomOut, zoomIn);
    }

    /// <summary>The tests' VehicleTests.Poke: the state a recording's poke changed.</summary>
    static void Poke(WorldState s, int addr, byte[] data)
    {
        for (var k = 0; k < data.Length; k++)
        {
            var a = addr + k;
            if (a >= 0x9288 && a < 0x9288 + 256) s.Kills[a - 0x9288] = data[k];
            else if (a >= 0x9E46 && a < 0x9E46 + 0x3C0) s.Items[a - 0x9E46 + 0x28] = data[k];
            else if (a == 0x806 + 0x216) s.Status216 = data[k];
            else if (a == 0x806 + 0x20A) s.Mission = s.Mission & ~0xFF00 | data[k] << 8;
            else if (a == 0x806 + 0x20B) s.Mission = s.Mission & ~0xFF | data[k];
            else if (a >= 0x806 + 0x21A && a < 0x806 + 0x21E)
                s.Score = s.Score & ~(0xFF << 8 * (0x21D - (a - 0x806))) | data[k] << 8 * (0x21D - (a - 0x806));
            else throw new System.NotSupportedException($"poke at ${a:X}");
        }
    }
}
