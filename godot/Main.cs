using System.Collections.Generic;
using System.Linq;
using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>Entry scene. Loads game_data (env HUNTER_DATA, default ../game_data) or shows why it can't,
/// then runs the original start sequence at 50 Hz (PAL frame rate) and, once a game is chosen,
/// its 3D view.
/// Verification: HUNTER_CAPTURE="frame:path,frame:path" steps headless, saves those frames as PNG
/// and quits; HUNTER_CAPTURE_WINDOW saves the whole window at its own resolution instead (the F1
/// key list shows only there). HUNTER_INPUT="frame:click:x:y,frame:type:text,frame:enter" plays
/// the player's part (applied before that frame). HUNTER_REPLAY=<oracle recording .json>
/// replays the original's own recording pass by pass (Replay.cs), measured against its fields.</summary>
public partial class Main : Node2D
{
    public static GameData? Data { get; private set; }

    Node2D shown = null!;
    System.Action step = null!;
    System.Action<string> save = null!;
    System.Func<string> what = null!;
    bool capturing;
    /// <summary>The run's one soldier record ($85E2), kept across every return to the shell and
    /// handed to the next game: a fresh record is made only at boot (StartSequence.Init's
    /// `new Soldier`), everything after - the debrief's writes (GameToSoldier $87E4), the next
    /// briefing (PreMissionSoldier $86F2) and the next game's start (StartState.For) - reads and
    /// writes this one, as the original's $85E2.</summary>
    Soldier? career;

    /// <summary>The window settings' keys (not the game's: none of these is a key the original reads):
    /// F11 fullscreen, F10 the other look, F9 pause-on-focus-loss, F8 text size, F7 / Shift+F7 brightness,
    /// F6 the Amiga 500 sound filter.
    /// Each is kept in the settings file.</summary>
    BindingsOverlay? rebind;

    /// <summary>The brightness pass, only while the brightness is not 1.0 (then it is freed, and
    /// the output has no extra pass at all).</summary>
    BrightnessLayer? brightness;

    /// <summary>F2 (or the headless script's rebind verb): the rebind screen on / off.</summary>
    public void ToggleRebind()
    {
        if (rebind is null) { rebind = new BindingsOverlay { Visible = false }; AddChild(rebind); }
        rebind.Toggle();
    }

    /// <summary>The manual (F3 anywhere; a MANUAL button on the menus with the setting on):
    /// ManualView over the screen that is up. Null until the first F3.</summary>
    ManualView? manual;

    /// <summary>The manual is up: the game does not run (the shell steps the manual instead of
    /// the game's step, the way the options screen's Frame branches away from the pass -
    /// GameView.cs `else if (options is not null || confirm is not null || DiskUp) ScreenFrame`),
    /// and the input goes to the manual.</summary>
    public bool ManualUp => manual is { Up: true };

    /// <summary>F3 opens the manual from ANY screen (title, menus, briefing, play, store, map,
    /// end screens - Main._Input runs on them all); F3 or Esc closes it. While it is up the game
    /// does NOT run: the shell steps the manual instead of the game's step - no VerticalBlank, no
    /// Frame, no blank counted, no sound tick (Channels.Tick runs per blank in the rules) - while
    /// the music keeps looping as it was (Audio.Music is its own looping player). Closing it
    /// carries on from the same frame: the game's step is the very next one, nothing skipped.</summary>
    public void ToggleManual()
    {
        if (manual is null) { manual = new ManualView(); AddChild(manual); manual.Init(Data!, this); }
        manual.Toggle();
        // capturing: Main's capture loop steps the manual; live: the game's own physics process
        // is suspended while the manual is up and resumed after
        if (manual.Up) shown?.SetPhysicsProcess(false);
        else if (!capturing) shown?.SetPhysicsProcess(true);
    }

    /// <summary>The headless script's manual verbs ("manual" = F3, "page:left|right").</summary>
    public void ManualPage(int dir) => manual?.Page(dir);

    /// <summary>The headless script's click while the manual is up.</summary>
    public void ManualClick(int x, int y) => manual?.Click(x, y);

    public override void _Input(InputEvent e)
    {
        if (ManualUp)                                                       // the manual takes the input while it is up
        {
            switch (e)
            {
                case InputEventKey { Pressed: true, Echo: false } key:
                    if (key.Keycode is Key.F3 or Key.Escape) ToggleManual(); // F3 or Esc closes it
                    else if (key.Keycode == Key.Left) ManualPage(-1);
                    else if (key.Keycode == Key.Right) ManualPage(1);
                    else if (key.Keycode == Key.Home) manual!.Home();
                    else if (key.Keycode == Key.End) manual!.End();
                    return;
                case InputEventJoypadButton { Pressed: true } jb:
                    if (jb.ButtonIndex == JoyButton.DpadLeft) ManualPage(-1);
                    else if (jb.ButtonIndex == JoyButton.DpadRight) ManualPage(1);
                    else if (jb.ButtonIndex == JoyButton.A) manual!.PadClick();
                    return;
                case InputEventMouseButton { Pressed: true } m:
                    var at = Display.I.ToGame(GetViewport().GetMousePosition());
                    if (m.ButtonIndex == MouseButton.WheelDown) ManualPage(1);
                    else if (m.ButtonIndex == MouseButton.WheelUp) ManualPage(-1);
                    else if (m.ButtonIndex == MouseButton.Left) ManualClick((int)at.X, (int)at.Y);
                    return;
            }
            return;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        switch (k.Keycode)
        {
            case Key.F3:
                ToggleManual();                                             // the manual from any screen
                break;
            case Key.F11:
                var full = GetWindow().Mode != Window.ModeEnum.Fullscreen;
                GetWindow().Mode = full ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
                Settings.Fullscreen = full;
                break;
            case Key.F2:
                ToggleRebind();
                break;
            case Key.F10:
                Display.I.ToggleLook();
                break;
            case Key.F6:
                Settings.A500Filter = !Settings.A500Filter;
                Audio.ApplyFilter(Settings.A500Filter);
                break;
            case Key.F9:
                Settings.PauseOnFocusLoss = !Settings.PauseOnFocusLoss;
                if (!Settings.PauseOnFocusLoss) GetTree().Paused = false;
                break;
            case Key.F7:                                                   // Shift+F7 darker
                Settings.Brightness += k.ShiftPressed ? -0.1f : 0.1f;
                ApplyBrightness();
                break;
            case Key.F8:
                Settings.CycleTextSize();
                break;
        }
    }

    /// <summary>Losing the window's focus pauses the whole game (sound too) until it comes back.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut && Settings.PauseOnFocusLoss) GetTree().Paused = true;
        else if (what == NotificationApplicationFocusIn) GetTree().Paused = false;
    }

    /// <summary>The brightness pass, added above the game picture while the brightness is not
    /// 1.0 and removed again when it comes back to 1.0 - at 1.0 there is no extra pass at all,
    /// so the picture (and every capture of it) is byte-identical to the original's.</summary>
    public void ApplyBrightness()
    {
        if (Settings.Brightness == 1.0)
        {
            brightness?.QueueFree();
            brightness = null;
        }
        else
        {
            if (brightness is null) { brightness = new BrightnessLayer(); AddChild(brightness); }
            brightness.Set(Settings.Brightness);
        }
    }

    /// <summary>ENHANCEMENT: the window's icon is the loading screen's picture, logo and all
    /// (rules/GameIcon.Loading) when game_data/pics/loadpic2.json exists, else the soldier screen's face - from
    /// the player's own game data, the largest whole scale that fits a 256 x 256 square, nearest neighbour.</summary>
    void SetWindowIcon(string dir)
    {
        ushort[] colours; int w, h;
        var loading = System.IO.Path.Combine(dir, "pics", "loadpic2.json");
        if (System.IO.File.Exists(loading))
        {
            using var pic = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(loading));
            colours = GameIcon.Loading(System.Convert.FromHexString(pic.RootElement.GetProperty("pixels_hex").GetString()!),
                pic.RootElement.GetProperty("palette12").EnumerateArray().Select(c => (ushort)c.GetInt32()).ToArray());
            w = h = GameIcon.LoadingSide;
        }
        else
        {
            colours = GameIcon.Face(Ui.Load(dir), Data!.PaletteBanks);
            (w, h) = (GameIcon.Width, GameIcon.Height);
        }
        const int Side = 256;
        var k = System.Math.Max(1, System.Math.Min(Side / w, Side / h));
        var img = Image.CreateEmpty(Side, Side, false, Image.Format.Rgb8);
        img.Fill(Colors.Black);
        int ox = (Side - w * k) / 2, oy = (Side - h * k) / 2;
        for (var y = 0; y < h * k; y++)
            for (var x = 0; x < w * k; x++)
            {
                var c = colours[(y / k) * w + x / k];
                img.SetPixel(ox + x, oy + y, new Color((c >> 8 & 15) / 15f, (c >> 4 & 15) / 15f, (c & 15) / 15f));
            }
        DisplayServer.SetIcon(img);
    }

    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 50;
        Display.I.Apply(this);                                              // HUNTER_LOOK: where the picture goes in the window
        ApplyBrightness();
        // fullscreen applies whenever there is a real window - a scripted run that also
        // captures (HUNTER_CAPTURE with --windowed) gets it too; only the headless runs,
        // which have no window to fill, skip it (the setting changed nothing there before:
        // HUNTER_CAPTURE is set on every scripted run, windowed ones included).
        if (Settings.Fullscreen && DisplayServer.GetName() != "headless")
            GetWindow().Mode = Window.ModeEnum.Fullscreen;
        // Verification: HUNTER_TABLE=1 prints the display layout table and quits.
        if (System.Environment.GetEnvironmentVariable("HUNTER_TABLE") is not null)
        {
            foreach (var (w, h) in new[] { (1920, 1080), (2560, 1440), (1280, 720), (1280, 1024), (800, 600) })
                foreach (var look in new[] { Display.DisplayLook.Stretched, Display.DisplayLook.Pal, Display.DisplayLook.Centred })
                {
                    var (x, y, w2, h2, _) = Display.Layout(w, h, look);
                    GD.Print($"HUNTER table {w}x{h} {look}: picture {w2}x{h2} at ({x},{y})");
                }
            GetTree().Quit();
            return;
        }
        var dir = System.Environment.GetEnvironmentVariable("HUNTER_DATA")
                  ?? ProjectSettings.GlobalizePath("res://../game_data");
        if (!OS.HasFeature("editor") && !System.IO.Directory.Exists(dir))   // an exported release: game_data beside the exe (docs/handoff-history.md, the Windows test release)
        {
            var exeDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!;
            dir = System.IO.Path.Combine(exeDir, "game_data");
            // macOS: the program is inside Hunter.app/Contents/MacOS. The export puts game_data in Contents/Resources before
            // it signs the app (addons/mac_game_data), so it travels with the app, also into the hidden temporary copy
            // macOS runs a downloaded app from; an older layout kept it beside Hunter.app
            if (OS.GetName() == "macOS" && !System.IO.Directory.Exists(dir))
            {
                dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..", "Resources", "game_data"));
                if (!System.IO.Directory.Exists(dir))
                    dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..", "..", "..", "game_data"));
            }
        }
        try
        {
            Data = GameData.Load(dir);
            GD.Print($"HUNTER: game data loaded from {dir}: {Data.Worlds.Count} worlds");
            SetWindowIcon(dir);
            if (System.Environment.GetEnvironmentVariable("HUNTER_RECORDED") is not null) GameView.ApplyRecorded();   // before the sound builds its extras
            else if (Settings.ModernDefault && Settings.FirstStart) GameView.ApplyModernDefault();   // the test release: full modern, no question
            // a new player's first start asks AS IT (KIND OF) WAS / AS IT COULD BE first (StartSequence): only a real
            // play, never a scripted, captured, replayed or recorded run; the sound is built once it is answered
            askStart = Settings.FirstStart && DisplayServer.GetName() != "headless"
                && new[] { "HUNTER_CAPTURE", "HUNTER_CAPTURE_WINDOW", "HUNTER_REPLAY", "HUNTER_GAME", "HUNTER_RECORDED", "HUNTER_INPUT" }
                    .All(v => System.Environment.GetEnvironmentVariable(v) is null);
            if (!askStart) StartAudio();
            if (System.Environment.GetEnvironmentVariable("HUNTER_PLAYTEST") is not null || DisplayServer.GetName() != "headless" && System.Environment.GetEnvironmentVariable("HUNTER_CAPTURE_WINDOW") is null
                && System.Environment.GetEnvironmentVariable("HUNTER_INPUT") is null)
                AddChild(new Playtest(this));                               // a real play: the pause menu's DEBUG notes, frame times, session.txt
        }
        catch (GameDataMissingException e)
        {
            GD.Print("HUNTER: " + e.Message);
            // where it looked, and what to do: macOS runs a downloaded app it has not cleared from a hidden temporary copy
            // (/AppTranslocation/), which holds the app alone, so the game_data beside it is never found there
            var hint = dir.Contains("/AppTranslocation/")
                ? "macOS is running Hunter from a temporary copy, away from its game_data folder. To fix it once: quit, open Terminal, type  xattr -cr  and a space, drag the folder that holds Hunter.app and game_data into the Terminal window, press Return, then open Hunter.app again."
                : OS.HasFeature("editor")
                    ? "Build it from the original disk: see README.md, Game data."
                    : OS.GetName() == "macOS"
                        ? "The game_data folder belongs inside Hunter.app (Contents/Resources). Download the Mac zip again and use the Hunter.app in it."
                        : "Keep the game_data folder next to the program.";
            AddChild(new Label
            {
                Text = $"Game data missing.\n\nLooked in:\n{dir}\n\n{hint}",
                Position = new Vector2(8, 8), Size = new Vector2(304, 184),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,   // a long folder path is broken where it must be
                LabelSettings = new LabelSettings { FontSize = 8 },
            });
            return;
        }

        var capture = System.Environment.GetEnvironmentVariable("HUNTER_CAPTURE");
        // Verification only: HUNTER_CAPTURE_WINDOW="frame:file.png" saves the whole window - the
        // viewport texture, at the window's own resolution - instead of the 320 x 200 game image.
        // Only here does the F1 key list show (HelpOverlay is a CanvasLayer over the window, never
        // in the game picture). Same syntax and everything else as HUNTER_CAPTURE.
        var windowCapture = System.Environment.GetEnvironmentVariable("HUNTER_CAPTURE_WINDOW");
        capturing = capture is not null || windowCapture is not null;
        // Verification: HUNTER_REPLAY=<oracle recording .json> replays the ORIGINAL's own recording
        // in this build, one pass per Step, exactly the way the rules tests run one (Replay.cs: the
        // recording's world patch, energy and per-pass input; measured against its player record,
        // camera, palette and screen). HUNTER_REPLAY_FRAMES="pass,pass" names the passes saved as
        // <recording>_f<pass>.png in HUNTER_REPLAY_OUT (default: first, quarters, last).
        var replayPath = System.Environment.GetEnvironmentVariable("HUNTER_REPLAY");
        if (replayPath is not null && Data is not null)
        {
            var replay = new Replay(this, Data!, replayPath);
            shown = replay.View;
            shown.SetPhysicsProcess(false);
            while (!replay.Step()) { }                       // Step quits the tree at the recording's end
            return;
        }

        // Verification only: HUNTER_GAME=hunter|missions|action goes straight to a started game's view.
        var game = System.Environment.GetEnvironmentVariable("HUNTER_GAME");
        if (game is not null) StartGame(System.Enum.Parse<GameMode>(game, ignoreCase: true));
        else ShowTitle(0, StartEntry.Boot);

        // Verification only: HUNTER_ITEMS="13:1,1:6" puts items in the pack (a fresh game carries
        // only 9, 17 and 18, game.md 3), so a script can reach the keys and screens they lead to.
        if (shown is GameView given && System.Environment.GetEnvironmentVariable("HUNTER_ITEMS") is { } pack)
            foreach (var it in pack.Split(','))
            {
                var f = it.Split(':');
                given.Give(int.Parse(f[0]), int.Parse(f[1]));
            }

        // HUNTER_BOT (run_game --bot): the started game is played by the bot (Rules.Hunt) - to watch long bot runs
        if (shown is GameView botGame && System.Environment.GetEnvironmentVariable("HUNTER_BOT") is not null) botGame.HandToBot();

        if (capture is null && windowCapture is null) return;
        var inputs = Script(System.Environment.GetEnvironmentVariable("HUNTER_INPUT"));
        shown.SetPhysicsProcess(false);
        var shots = Shots(capture, false).Concat(Shots(windowCapture, true)).OrderBy(s => s.Frame).ToList();
        Capture(shots, inputs);
    }

    /// <summary>Step 50 Hz frames to each shot, save it and quit. A window shot waits one process
    /// frame first: the viewport texture is only what the renderer has drawn, and _Ready runs
    /// before the first draw.</summary>
    async void Capture(List<(int Frame, string Path, bool Window)> shots,
        Dictionary<int, System.Action<Node2D>> inputs)
    {
        var frame = 0;
        var saving = new List<System.Threading.Tasks.Task>();
        // HUNTER_STATS on a window run: wall time from the first window draw to the last - the step, the card's draw and
        // the read back (which waits for the card); Godot's own viewport timer reads 0 for the 3D SubViewport
        var drawn = System.Diagnostics.Stopwatch.StartNew(); var draws = 0;
        if (shots.Count(s => s.Window) > 1)   // a recording (faster recording runs only): no wait for the screen's refresh
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        if (Settings.Renderer == "3d")   // the card's bake of the models needs engine frames; a scripted run would step every game frame before any
            for (var w = 0; w < 6; w++) await ToSignal(GetTree(), "process_frame");
        // HUNTER_STOP_OVER (the fuzzer): play over - out of play with nothing live for 500 frames (his death's end screen,
        // which waits for a click) - ends the run there, the last shot taken at once
        var stopOver = System.Environment.GetEnvironmentVariable("HUNTER_STOP_OVER") is not null;
        var idle = 0;
        var ended = false;
        foreach (var (f, path, window) in shots)
        {
            if (ended && path != shots[^1].Path) continue;           // play over: only the last shot, at once
            while (frame < f && !ended)
            {
                if (inputs.TryGetValue(frame, out var act)) act(shown);
                Audio.Frame = frame;                                  // HUNTER_SOUND_LOG's clock
                if (ManualUp) manual!.Frame(); else step();           // the manual up: the game's step does not run
                frame++;
                if (stopOver && shown is GameView over)
                {
                    if (over.DeathWatch() is { } death) GD.Print($"HUNTER: death at frame {frame}: {death}");
                    if (over.RuleWatch() is { } rule) GD.Print($"HUNTER: RULE at frame {frame}: {rule}");
                    idle = !over.InPlay && over.LiveCount == 0 ? idle + 1 : 0;
                    if (idle == 500)
                    {
                        GD.Print($"HUNTER: play over at frame {frame - 500}: type {over.PlayerType:X2} {over.Whereabouts}");
                        ended = true;
                    }
                }
                if (Settings.Renderer == "3d" && frame % 50 == 0) await ToSignal(GetTree(), "process_frame");   // a view built mid-run (menu -> game) bakes on engine frames
            }
            if (System.Environment.GetEnvironmentVariable("HUNTER_STATS") is not null && shown is not GameView)
                GD.Print($"HUNTER: frame {frame} shown {shown.GetType().Name}");   // a menu or the title (a quit's way out)
            if (System.Environment.GetEnvironmentVariable("HUNTER_STATS") is not null && shown is GameView gv)
            {
                GD.Print($"HUNTER: frame {frame} live {gv.LiveCount} play {gv.InPlay} type {gv.PlayerType:X2} {gv.Whereabouts}");
                if (gv.Frames3D > 0)
                    GD.Print($"HUNTER: 3d over {gv.Frames3D} frames: card feed {gv.Frame3DTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / gv.Frames3D:F2} ms a frame; " +
                             $"process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:F2} ms, fps {Performance.GetMonitor(Performance.Monitor.TimeFps)}");
            }
            if (window)   // the next finished draw is the first with this step in it (steps run after a draw); the PNG is written on a thread
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var img = GetViewport().GetTexture().GetImage();
                // HUNTER_SUN_DEPTH=<png> (real shadows, step 1): the sun's depth picture of the same draw, at the last shot only
                if (GpuWorld.SunDepthPath is { } sunPng && path == shots[^1].Path) GpuWorld.SaveSunDepth(sunPng);
                if (draws++ == 0) drawn.Restart();
                if (path.EndsWith(".mp4")) { Pipe(img, path); continue; }   // straight to video, no picture files
                saving.Add(System.Threading.Tasks.Task.Run(() => img.SavePng(path)));
            }
            else if (ManualUp) manual!.Save(path);                    // the manual's own 320 x 200 picture
            else save(path);
            GD.Print($"HUNTER: frame {f} ({what()}{(window ? ", window" : "")}) -> {path}");
        }
        System.Threading.Tasks.Task.WaitAll([.. saving]);
        if (video is not null) { video.StandardInput.Close(); video.WaitForExit(); }
        if (draws > 1 && System.Environment.GetEnvironmentVariable("HUNTER_STATS") is not null)
            GD.Print($"HUNTER: window frames {drawn.Elapsed.TotalMilliseconds / (draws - 1):F2} ms each over {draws - 1}");
        GetTree().Quit();
    }

    System.Diagnostics.Process? video;
    string? videoPath;

    /// <summary>A window range to "name.mp4": each frame goes raw into ffmpeg (on PATH) as it is drawn,
    /// 50 a second, the picture only - make_video adds the sound.</summary>
    void Pipe(Image img, string path)
    {
        if (img.GetFormat() != Image.Format.Rgb8) img.Convert(Image.Format.Rgb8);
        if (video is not null && videoPath != path) { video.StandardInput.Close(); video.WaitForExit(); video = null; }   // the next clip of a run: its own file (before, every clip went into the first)
        if (video is null)
        {
            videoPath = path;
            var info = new System.Diagnostics.ProcessStartInfo("ffmpeg",
                $"-y -loglevel error -f rawvideo -pix_fmt rgb24 -s {img.GetWidth()}x{img.GetHeight()} -framerate 50 -i - " +
                $"-c:v libx264 -preset fast -crf 18 -pix_fmt yuv420p \"{path}\"")
                { RedirectStandardInput = true, UseShellExecute = false };
            video = System.Diagnostics.Process.Start(info)!;
        }
        video.StandardInput.BaseStream.Write(img.GetData());
    }

    /// <summary>Parse one HUNTER_CAPTURE / HUNTER_CAPTURE_WINDOW list ("frame:path,..."). A range
    /// "from-to:folder" saves every frame from..to as folder/f00000.png (a video's frames);
    /// "from-to:name.mp4" pipes them into one video instead (Pipe).</summary>
    static IEnumerable<(int Frame, string Path, bool Window)> Shots(string? spec, bool window) =>
        spec is null ? [] : spec.Split(',').Select(s => s.Split(':', 2)).SelectMany(p =>
            p[0].Contains('-')
                ? Enumerable.Range(int.Parse(p[0].Split('-')[0]), int.Parse(p[0].Split('-')[1]) - int.Parse(p[0].Split('-')[0]) + 1)
                    .Select(f => (Frame: f, Path: p[1].EndsWith(".mp4") ? p[1] : System.IO.Path.Combine(p[1], $"f{f:D5}.png"), Window: window))
                : [(Frame: int.Parse(p[0]), Path: p[1], Window: window)]);

    /// <summary>HUNTER_INPUT items: click:x:y (the menus' or the game's own: game-picture pixels,
    /// 320 x 200), type:text, enter (menus); key:hex (a raw Amiga key code to the game view) or
    /// key:pgup|pgdn (message_log's Page Up / Page Down), wheel:up|down (message_log's wheel, at
    /// the message window's middle) - both delivered through the same _Input a real event takes,
    /// joy:letters (u d l r f, held until the next joy item; "joy:" releases), zoom:out|in|off and
    /// space (the game view).</summary>
    static Dictionary<int, System.Action<Node2D>> Script(string? spec)
    {
        var acts = new Dictionary<int, System.Action<Node2D>>();
        if (string.IsNullOrEmpty(spec)) return acts;
        foreach (var item in spec.Split(','))
        {
            var p = item.Split(':');
            System.Action<Node2D> a = p[1] switch
            {
                "click" => n =>
                {
                    var owner = n.GetParent() as Main;
                    if (owner is { ManualUp: true }) { owner.ManualClick(int.Parse(p[2]), int.Parse(p[3])); return; }   // the manual's footer hints
                    (n as StartSequence)?.Click(int.Parse(p[2]), int.Parse(p[3])); (n as GameView)?.Click(int.Parse(p[2]), int.Parse(p[3]));
                },
                "type" => n => { foreach (var c in p[2]) (n as StartSequence)?.Key(-1, c); (n as GameView)?.DiskType(p[2]); },
                "enter" => n => { (n as StartSequence)?.Key(0x44, 13); (n as GameView)?.DiskEnter(); },
                "manual" => n => (n.GetParent() as Main)?.ToggleManual(),      // F3: open / close the manual
                "mark" => n => (n.GetParent() as Main)?.GetChildren().OfType<Playtest>().FirstOrDefault()?.Mark(),   // a mark at once, as the DEBUG page's SAVE NOTE does (HUNTER_PLAYTEST=1)
                "page" => n => (n.GetParent() as Main)?.ManualPage(p[2] == "left" ? -1 : 1),   // a page turn
                "key" => n => { if (p[2] == "pgup") (n as GameView)?.ScriptPage(true);        // message_log's Page Up
                                else if (p[2] == "pgdn") (n as GameView)?.ScriptPage(false);  // message_log's Page Down
                                else (n as GameView)?.KeyDown(System.Convert.ToByte(p[2], 16)); },
                "wheel" => n => (n as GameView)?.ScriptWheel(p[2] == "up"),   // message_log's wheel, at the window's middle
                "joy" => n => (n as GameView)?.Script(new Joystick(p[2].Contains('u'), p[2].Contains('d'),
                    p[2].Contains('l'), p[2].Contains('r'), p[2].Contains('f'))),
                "zoom" => n => (n as GameView)?.ScriptZoom(p[2] == "out", p[2] == "in"),
                "quick" => n => (n as GameView)?.ScriptQuick(int.Parse(p[2])),
                "quickhold" => n => (n as GameView)?.ScriptQuickHold(int.Parse(p[2])),
                "space" => n => (n as GameView)?.KeyDown(0x40),
                "help" => n => (n as GameView)?.ToggleHelp(),
                "quicksave" => n => (n as GameView)?.QuickSave(),             // easy_saves F5
                "quickload" => n => (n as GameView)?.QuickLoad(),             // easy_saves F4
                "freeze" => n => (n as GameView)?.LiveFreeze(),               // HUNTER_LIVE's "freeze on" at this frame (a fast run stops exactly there)
                "rebind" => n => (n.GetParent() as Main)?.ToggleRebind(),
                _ => throw new System.ArgumentException("HUNTER_INPUT: " + item),
            };
            acts[int.Parse(p[0])] = acts.TryGetValue(int.Parse(p[0]), out var prev) ? prev + a : a;
        }
        return acts;
    }

    /// <summary>The start sequence (title, credits, menus): the start of the run, and where a
    /// ended game goes back to - GameSequence's destinations. Boot shows the title, the credits
    /// and the menus ($1F26-$1F6C); a mission failed on time lands on $2404's title (song 2 over
    /// the title picture - file 5, the same picture the boot title shows - one click, fade) and
    /// then the menus, which play song 0 again ($1F72, PlayMusic only restarts on a change);
    /// everything else ($975C a won game, $9064 Quit Game) goes straight back to the menus. The
    /// soldier record goes round with it: $85E2 is one record for the whole run.</summary>
    /// <summary>The first start's question is up (Settings.FirstStart): the sound waits for its answer.</summary>
    bool askStart;

    void StartAudio()
    {
        var audio = new Audio();
        AddChild(audio);
        audio.Init(Data!.Directory);
    }

    void ShowTitle(int song, StartEntry entry = StartEntry.Boot)
    {
        var old = shown;
        if (old is GameView { Demo: false } ended) career = ended.Career;   // the debrief wrote the game into the record (a demo's is dropped: SoldierRestore $864A)
        var seq = new StartSequence();
        AddChild(seq);
        seq.ManualRequested += ToggleManual;                                // the main menu's MANUAL button (setting on)
        Audio.Instance?.Music(song);   // song 0: the title ($1F44) and the menus after it ($1F72)
        if (askStart)
            seq.StartAnswered += () => { askStart = false; StartAudio(); Audio.Instance?.Music(song); };
        seq.Init(Data!, scriptedInput: capturing, career, entry, askStart);
        seq.GameChosen += m => Swap(m, seq.Loaded, seq.PausePressed, seq.Demo ? StartState.DefaultSoldier(Data!) : seq.Soldier, seq.Demo);
        (shown, step, save, what) = (seq, seq.Frame, seq.Save, () => seq.Current);
        if (capturing) seq.SetPhysicsProcess(false);   // a headless run steps the frames itself
        if (old is not null) old.QueueFree();
    }

    void Swap(GameMode mode, WorldState? loaded, bool paused = false, Soldier? soldier = null, bool demo = false)
    {
        var old = shown;
        StartGame(mode, loaded, paused, soldier, demo);
        old.QueueFree();
    }

    void StartGame(GameMode mode, WorldState? loaded = null, bool paused = false, Soldier? soldier = null, bool demo = false)
    {
        var view = new GameView();
        AddChild(view);
        view.ManualRequested += ToggleManual;                               // the options menu's MANUAL button (setting on)
        Audio.Instance?.EnterPlay();  // music never plays in play (music_in_play: it keeps on quietly)
        view.Init(Data!, mode, scriptedInput: capturing, loaded, paused, soldier, demo);
        // the run's record now lives in this game - not the demo's: SoldierBackup $22F2 keeps the run's aside,
        // CopyDefaultSoldier $22F6 plays the demo with the default one, SoldierRestore $22FE puts it back
        if (!demo) career = view.Career;
        // a mission failed on time returns through $2404 (the title, song 2); everything else -
        // $975C a won game, $9064 Quit Game - straight back to the menu loop $1F6C (song 0)
        view.QuitToTitle += after => ShowTitle(after == AfterGame.Title ? 2 : 0,
            after switch { AfterGame.Title => StartEntry.Title, AfterGame.DemoOver => StartEntry.Boot, _ => StartEntry.Menus });   // DemoOver: $2308 TitlePicture, $230C bra $1F2E - the boot's way in
        if (shown is not null && !shown.IsPhysicsProcessing()) view.SetPhysicsProcess(false);
        (shown, step, save, what) = (view, view.Frame, view.Save, () => "game " + mode);
    }
}

/// <summary>The brightness pass (F7 / Shift+F7, Settings.Brightness): a full-window ColorRect on
/// a CanvasLayer that redraws the screen times the brightness (a screen-reading shader - multiply
/// alone could only darken). Layer 1, added before the F1/F2 overlays ever are, so it multiplies
/// the game picture but sits under the shell's own overlays (the key list keeps its colours).
/// It exists only while the brightness is not 1.0: no layer, no pass, the output byte-identical.</summary>
public partial class BrightnessLayer : CanvasLayer
{
    static readonly Shader Shader = new()
    {
        Code = """
            shader_type canvas_item;
            uniform sampler2D screen_texture : hint_screen_texture;
            uniform float brightness = 1.0;
            void fragment()
            {
                COLOR = vec4(texture(screen_texture, SCREEN_UV).rgb * brightness, 1.0);
            }
            """,
    };

    ColorRect? rect;

    public BrightnessLayer() => Layer = 1;

    /// <summary>b != 1.0: show the pass at that brightness; b == 1.0: remove it.</summary>
    public void Set(float b)
    {
        if (b == 1.0f)
        {
            if (rect is not null) { rect.QueueFree(); rect = null; }
            return;
        }
        if (rect is null)
        {
            rect = new ColorRect
            {
                Material = new ShaderMaterial { Shader = Shader },
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(rect);
        }
        ((ShaderMaterial)rect.Material).SetShaderParameter("brightness", b);
    }
}
