using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace Hunter;

/// <summary>Playtest notes for the lead, on every real-window play (not the scripted or headless runs):
/// Esc holds the moment (Hold) and the pause menu's DEBUG page saves it (SaveNote) - a save of it (when in
/// play), a picture, the settings and the last 30 s of input and frame times - into the repo's
/// playtest\&lt;session&gt;\mark_N, with note.txt and a line in playtest\notes.md; the frame times are kept all
/// session; at quit, session.txt (how long, how smooth, the worst moments) and a copy of the settings and
/// of Godot's own log (the errors).</summary>
public sealed partial class Playtest : Node
{
    const double Window = 30;                                         // seconds of input and frame times a mark keeps

    readonly Main main;
    readonly string dir;
    readonly DateTime started = DateTime.Now;
    readonly Queue<(double T, string What)> input = new();
    readonly Queue<(double T, double Ms)> recent = new();
    readonly List<(double T, double Ms)> worst = new();               // the 10 slowest frames of the session
    long frames, slow;                                                // slow = over a 50 Hz frame (20 ms)
    double clock, totalMs;
    int marks;

    public Playtest(Main main)
    {
        this.main = main;
        ProcessMode = ProcessModeEnum.Always;                         // the pause and the focus pause too
        dir = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://../playtest/" + started.ToString("yyyy-MM-dd_HHmm")));   // the repo's own playtest (not AppData: the lead reads it)
        Directory.CreateDirectory(dir);
    }

    public override void _Process(double delta)
    {
        clock += delta;
        var ms = delta * 1000;
        frames++; totalMs += ms;
        if (ms > 20) slow++;
        recent.Enqueue((clock, ms));
        while (recent.Count > 0 && recent.Peek().T < clock - Window) recent.Dequeue();
        if (frames > 50 && (worst.Count < 10 || ms > worst[^1].Ms))  // the first second is loading
        {
            worst.Add((clock, ms));
            worst.Sort((a, b) => b.Ms.CompareTo(a.Ms));
            if (worst.Count > 10) worst.RemoveAt(10);
        }
    }

    public override void _Input(InputEvent e)
    {
        var what = e switch
        {
            InputEventKey { Echo: false } k => $"key {k.PhysicalKeycode} {(k.Pressed ? "down" : "up")}",
            InputEventMouseButton m => $"mouse {m.ButtonIndex} {(m.Pressed ? "down" : "up")} at {m.Position.X:0},{m.Position.Y:0}",
            InputEventJoypadButton j => $"pad {j.ButtonIndex} {(j.Pressed ? "down" : "up")}",
            InputEventJoypadMotion jm when Math.Abs(jm.AxisValue) > 0.5f => $"pad axis {jm.Axis} {jm.AxisValue:0.0}",
            _ => null,
        };
        if (what is null) return;
        input.Enqueue((clock, what));
        while (input.Count > 0 && input.Peek().T < clock - Window) input.Dequeue();
    }

    /// <summary>Esc in play (GameView, just before the quit's fade-out runs): hold this moment -
    /// picture, save, the settings file's own text, the last 30 s of input and frames - in memory
    /// until the DEBUG page's SAVE NOTE writes it; the next Esc holds a new one.</summary>
    public void Hold()
    {
        held = new Moment(clock,
            DisplayServer.GetName() != "headless" ? GetViewport().GetTexture().GetImage() : null,   // a headless run has no picture
            main.GetChildren().OfType<GameView>().FirstOrDefault()?.PlaytestSave(),
            File.Exists(SettingsFile) ? File.ReadAllBytes(SettingsFile) : null,
            input.ToArray(), recent.ToArray());
    }

    /// <summary>The held moment (Hold): the clock the input and frame times are relative to, the
    /// picture, the save, the settings file's text, the last 30 s of input and frames.</summary>
    sealed record Moment(double Clock, Image? Picture, byte[]? Save, byte[]? Settings, (double T, string What)[] Input, (double T, double Ms)[] Frames);

    Moment? held;

    /// <summary>The DEBUG page's SAVE NOTE: the held moment into mark_N - the files Mark writes,
    /// plus note.txt (the kind, then the words) and a line in playtest\notes.md (one file for all
    /// sessions). No moment held (the menu was not opened from play): note.txt and the line, no
    /// save.bin. Returns the mark's number.</summary>
    public int SaveNote(string kind, string words)
    {
        var at = Path.Combine(dir, $"mark_{++marks}");
        Directory.CreateDirectory(at);
        var h = held;
        if (h is not null)                                          // Esc's moment, as it was in play
        {
            if (h.Picture is not null) h.Picture.SavePng(Path.Combine(at, "picture.png"));
            if (h.Save is not null) File.WriteAllBytes(Path.Combine(at, "save.bin"), h.Save);
            if (h.Settings is not null) File.WriteAllBytes(Path.Combine(at, "settings.cfg"), h.Settings);
            File.WriteAllLines(Path.Combine(at, "input.txt"), h.Input.Select(i => $"{i.T - h.Clock,7:0.00} s  {i.What}"));
            File.WriteAllLines(Path.Combine(at, "frames.txt"), h.Frames.Select(f => $"{f.T - h.Clock,7:0.00} s  {f.Ms,6:0.0} ms"));
        }
        File.WriteAllText(Path.Combine(at, "note.txt"), kind + "\n" + words + "\n");
        File.WriteAllText(Path.Combine(at, "when.txt"), $"{DateTime.Now:HH:mm:ss}, {clock / 60:0.0} min into the session, " +
            (h is null ? "the menu was not opened from play" : h.Save is null ? "held, no save (not in play)" : "in play (save.bin)") + "\n");
        var notes = Path.Combine(Path.GetDirectoryName(dir)!, "notes.md");   // the playtest folder, one file for all sessions
        File.AppendAllText(notes, (File.Exists(notes) ? "" : "# Play-test notes\n") +
            $"{DateTime.Now:yyyy-MM-dd HH:mm} | {Path.GetFileName(dir)}\\mark_{marks} | {kind} | {words}\n");
        GD.Print($"HUNTER: playtest note {marks} ({kind}) -> {at}");
        return marks;
    }

    /// <summary>The scripted "mark" verb (Main.cs): this moment, for the lead to load and look at.</summary>
    public void Mark()
    {
        var at = Path.Combine(dir, $"mark_{++marks}");
        Directory.CreateDirectory(at);
        if (DisplayServer.GetName() != "headless")                     // a headless run has no picture to read
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(at, "picture.png"));
        var view = main.GetChildren().OfType<GameView>().FirstOrDefault();
        var save = view?.PlaytestSave();
        if (save is not null) File.WriteAllBytes(Path.Combine(at, "save.bin"), save);
        CopySettings(at);
        File.WriteAllLines(Path.Combine(at, "input.txt"), input.Select(i => $"{i.T - clock,7:0.00} s  {i.What}"));
        File.WriteAllLines(Path.Combine(at, "frames.txt"), recent.Select(f => $"{f.T - clock,7:0.00} s  {f.Ms,6:0.0} ms"));
        File.WriteAllText(Path.Combine(at, "when.txt"), $"{DateTime.Now:HH:mm:ss}, {clock / 60:0.0} min into the session, " +
            (save is null ? "not in play (no save)" : "in play (save.bin)") + "\n");
        view?.PlaytestNote($"Marked {marks}");
        GD.Print($"HUNTER: playtest mark {marks} -> {at}");
    }

    /// <summary>The settings file the shell's Settings reads (HUNTER_SETTINGS overrides: the runs).</summary>
    static string SettingsFile => ProjectSettings.GlobalizePath(System.Environment.GetEnvironmentVariable("HUNTER_SETTINGS") is { Length: > 0 } p ? p : "user://settings.cfg");

    void CopySettings(string to)
    {
        if (File.Exists(SettingsFile)) File.Copy(SettingsFile, Path.Combine(to, "settings.cfg"), true);
    }

    public override void _ExitTree()
    {
        var mins = clock / 60;
        var lines = new List<string>
        {
            $"Session {started:yyyy-MM-dd HH:mm} to {DateTime.Now:HH:mm}, {mins:0.0} minutes, {marks} marks.",
            frames == 0 ? "No frames." :
                $"Frames: {frames}, average {totalMs / frames:0.0} ms ({1000 * frames / Math.Max(totalMs, 1):0} a second); " +
                $"{slow} over 20 ms (a 50 Hz frame), {100.0 * slow / frames:0.0}%.",
            "The slowest frames (minutes into the session, ms):",
        };
        lines.AddRange(worst.Select(w => $"  {w.T / 60,6:0.00} min  {w.Ms,7:0.0} ms"));
        File.WriteAllLines(Path.Combine(dir, "session.txt"), lines);
        CopySettings(dir);
        var log = ProjectSettings.GlobalizePath("user://logs/godot.log");
        try { if (File.Exists(log)) File.Copy(log, Path.Combine(dir, "godot.log"), true); } catch (IOException) { }   // still open: best effort
    }
}
