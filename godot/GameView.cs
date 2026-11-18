using System;
using System.Linq;
using Godot;
using Hunter.Rules;
using Sky = Hunter.Rules.Sky;                               // not Godot.Sky

namespace Hunter;

/// <summary>The 3D view of a started game (rules/Play.cs), shown on the 320 x 200 screen at 50 Hz.
/// Smooth play: every blank is a whole pass of the original's main loop in its own order
/// (blank work, update, draw; see Play.Update), with animations at the A500's rhythm
/// (Play.AnimEveryBlanks = 7). Input: the keyboard (PlayInput: Godot key -> Amiga raw code),
/// the mouse (the pointer follows it every frame, Play.Mouse; a left click presses,
/// Play.Click), the joystick (JoystickInput), or a script when capturing headless. The screens
/// the rules open over the view (map, log book, item table) are drawn by Play.Screen already,
/// in their own colours; the in-game options screen (Esc) and Quit Game's confirm are drawn
/// here, in the menus' palette bank 5.</summary>
public partial class GameView : Node2D
{
    /// <summary>The A500 took about 7 blanks per pass while walking (oracle traces: mostly 7-8).</summary>
    const int A500PassBlanks = 7;

    /// <summary>The item table ($A576): its mouse limits are screen rows 92-200 and OpenItems
    /// moves the pointer's $E0 += $30 (game.md 4), so a screen y there is 92 rows further down.</summary>
    const int ItemTableRoutine = 0xA576, ItemTableTop = 92;

    Play play = null!;
    GameData data = null!;
    Ui ui = null!;
    Image image = null!;
    readonly QuickPick quickPick = new();
    ImageTexture texture = null!;
    /// <summary>The frame's own picture and, with players = 2, the right half's - both reused
    /// every frame (a new 192 KB array each frame added up); every branch fills the whole
    /// buffer, so nothing stale survives.</summary>
    readonly byte[] frame = new byte[320 * 200 * 3];
    byte[]? frame2;
    byte[] pointer = null!;
    ushort pointerColour;
    Reticle reticle = null!;
    /// <summary>AIMING: RETICLE is on and play runs on foot's picture (not a screen, the quick pick or the demo): the mouse over the view is the reticle.</summary>
    bool Aiming => play.AimReticle && play.Mode == PlayMode.Play && !play.State.DemoMode && !scripted;
    bool scripted;
    Joystick scriptJoy;
    bool scriptZoomOut, scriptZoomIn;
    /// <summary>The pointer in game pixels (0-319, 0-199), for the drawn sprite.</summary>
    int mouseX = 160, mouseY = 100;
    readonly System.Collections.Generic.HashSet<string> unported = new();
    /// <summary>HUNTER_LOG=1: a line when the player's type changes (in / out of a vehicle) and
    /// every 50 frames.</summary>
    static readonly bool Log = System.Environment.GetEnvironmentVariable("HUNTER_LOG") == "1";
    int frames, lastType = -1;

    // the in-game options screen ($2164, Esc) and Quit Game's confirm ($21F2); not null while shown
    OptionsScreen? options;

    /// <summary>ENHANCEMENT (options design B, owner 2026-11-01): the in-game options as one menu over the paused game
    /// (OptionsOverlay over rules/OptionsPanel.cs); pausedShot is the window's picture as Esc stopped play. Scripted
    /// runs keep the original's options screen (their clicks are at its buttons).</summary>
    OptionsOverlay? optionsB;
    Image? pausedShot;
    ConfirmScreen? confirm;
    Fade? screenFade;
    /// <summary>Re-brief's screens ($23BE PreMissionSoldier $86F2): the soldier panel and the
    /// mission briefing, bank 6; not null while the briefing waits for its click ($AC84).</summary>
    IndexedScreen? brief;
    Fade? briefFade;
    /// <summary>The soldier's own screen (ViewSoldier $875A), shown when a ended run's record is
    /// no longer usable (CheckSoldierRecord $867E said no): the soldier panel, bank 6, one click
    /// ($AC84), a fade to black - and then $23CE's bmi takes $2404, the title.</summary>
    IndexedScreen? soldierScreen;
    Fade? soldierFade;
    bool soldierClick;
    /// <summary>The quit has had its PlayGameEnd ($24D6) once; the options screen is up (or the
    /// shell was told the debrief / disk menu is not wired yet).</summary>
    bool quitHandled;
    bool quitting;                         // a quit's StopPlay blanks are running (Play.Ending)
    /// <summary>The options menu's MANUAL button (a shell extra, drawn below the last one when
    /// Settings.ManualButton; null with the setting off).</summary>
    Menus.Button? manualOption;
    /// <summary>The options menu's SETUP button (a shell extra, always shown): opens the settings screen over the menu.</summary>
    Menus.Button? settingsOption;
    /// <summary>The settings screen (settings_screen.md Job B): its own 320 x 200 page over the
    /// options screen (whose pixels stay underneath untouched) and the page's buttons. Null
    /// while closed. The game is not running either way - the options screen's own freeze.</summary>
    IndexedScreen? settingsScreen;
    System.Collections.Generic.List<Menus.Button>? settingsButtons;
    /// <summary>The ended game's screens (Games.Debrief's GameEnd) being shown, one click each,
    /// with the song each EndScreen plays; endPhase 0 = showing (faded in), 1 = fading out.</summary>
    GameEnd? ending;
    int endAt, endPhase;
    Fade? endFade;
    bool endClick;
    /// <summary>F1: the key list (HelpOverlay), a CanvasLayer over the window - the game picture
    /// itself is exactly as without F1, and the game runs on as normal while it shows.</summary>
    bool help;
    HelpOverlay? helpOverlay;
    int gpuScale = 1;                                        // gpu_scale: the 3D view's SubViewport at s, its sprite back at 1/s (game pixels on screen)
    Sky? gpuSky;
    Image? gpuSkyImage;                                      // sky_steps: 1 x 140 row colours, uploaded each frame
    ImageTexture? gpuSkyTex;

    /// <summary>F1 (or the headless script's help verb): show / hide the key list.</summary>
    public void ToggleHelp()
    {
        help = !help;
        if (help && helpOverlay is null)
        {
            helpOverlay = new HelpOverlay();
            AddChild(helpOverlay);                       // a CanvasLayer: its own layer, unscaled
        }
        if (helpOverlay is not null) helpOverlay.Visible = help;
    }

    /// <summary>The game is done (Quit Game's Yes, or the end screens have all been clicked
    /// through): the shell goes back where GameSequence goes - $2404's title after a mission
    /// failed on time, the menu loop $1F6C otherwise ($975C a won game, $9064 Quit Game). Main
    /// listens and starts the start sequence again, keeping the soldier record.</summary>
    public event System.Action<AfterGame>? QuitToTitle;

    /// <summary>A click on the in-play options menu's MANUAL button (a shell extra,
    /// Settings.ManualButton): Main opens the manual over the menu - the options screen, its fade
    /// and the quit state stay exactly as they are, and closing carries on here.</summary>
    public event System.Action? ManualRequested;

    /// <summary>The soldier record the run keeps ($85E2): the state's own record, which the
    /// debrief (Games.Debrief's GameToSoldier $87E4) has written the ended game back into. Main
    /// keeps it across the return to the menus and hands it to the next game (PreMissionSoldier
    /// $86F2 reads the same record).</summary>
    public Soldier Career => play.State.Soldier;

    /// <summary>This game is the demo (RunDemo $22D6): its soldier record is not the run's.</summary>
    public bool Demo => play.State.DemoMode;

    /// <summary>A loaded game's briefing is on (main menu or in-game Load): its click starts play as the
    /// loaded state stands. Play.Rebrief is only for the options screen's Re-brief - run on a loaded game it
    /// respawns the player over the live one and the view is wrong (the rules' load test, which matches the
    /// original's passes, runs the loaded game with no Rebrief at all).</summary>
    bool afterLoad;

    public void Init(GameData d, GameMode mode, bool scriptedInput = false, WorldState? loaded = null, bool paused = false,
        Soldier? soldier = null, bool demo = false)
    {
        data = d;
        ui = Ui.Load(d.Directory);
        // ENHANCEMENT (DEMO WORLD: WILD, or a recording's HUNTER_WILD / run_game --wild; owner 2026-11-01/02): the demo plays the
        // fullest world, groups beside its own route and the way kept clear (Wild, DemoRoute) - its route holds to pass 13782
        // of 16054 (DemoRouteTests), then the last part plays out its own way (owner: accepted)
        var fuller = demo ? (Settings.WildDemo || System.Environment.GetEnvironmentVariable("HUNTER_WILD") is not null ? 4 : 0) : Settings.FullerWorld;
        // ENHANCEMENT (GROUND SEED, owner 2026-11-11): a new game with finer ground grows its detail from the world's own seed
        // (WORLD), a fresh one (RANDOM: drawn here, remembered for LAST, printed so it can be typed back) or the last one
        // (LAST; none yet: the world's). Never the demo (its own ground is the original's). The relief (2X / HILLS / CLIFFS)
        // rides with it; a loaded game's save carries both.
        ushort? groundSeed = null;
        if (loaded is null && !demo && Settings.FinerGround > 0)
        {
            if (Settings.GroundSeed == "random") { var drawn = (ushort)Random.Shared.Next(65536); groundSeed = drawn; Settings.LastGroundSeed = drawn; }
            else if (Settings.GroundSeed == "last" && Settings.LastGroundSeed >= 0) groundSeed = (ushort)Settings.LastGroundSeed;
            GD.Print($"HUNTER: ground seed {groundSeed?.ToString() ?? "the world's"}, relief {FineGround.Reliefs[Math.Clamp(Settings.GroundRelief, 0, FineGround.Reliefs.Count - 1)].Name}");
        }
        play = loaded is null ? new Play(d, mode, soldier: soldier, fullerWorld: fuller, players: Settings.Players, maths32: Settings.Maths32, finerGround: demo ? 0 : Settings.FinerGround, fixSpikes: Settings.FixSpikes, groundSeed: groundSeed, relief: Settings.GroundRelief) { AnimEveryBlanks = A500PassBlanks }   // ENHANCEMENT: the fuller world, the player's new games only - a demo is a new game too and plays its authored world; a loaded game's grid is the saved one; players = 2 spawns the second soldier (mp_two_soldiers)
                              : new Play(d, loaded) { AnimEveryBlanks = A500PassBlanks };
        if (demo) Rules.Demo.Start(play.State);                                        // RunDemo $22D6
        // ENHANCEMENT (owner 2026-11-12: "no reason to be tethered"): the demo follows the plain demo's route (rules/DemoFollow.cs)
        // - the recording's own input while he is exactly where it had him (nothing changed: the original, blank for blank), the
        // bot's body back onto the route and the key moments waited for once an extra moves him
        if (demo) play.Follow = new DemoFollow.Follower(DemoFollow.Plain(d));
        // ENHANCEMENT (recordings, HUNTER_HOUR / run_game --hour, owner 2026-11-02: long shadows): a new game or the demo
        // starts at this hour, not the world file's 09:00; Panel.Hour sets the hour's light as an hour tick does. In play the
        // clock feeds only the mission deadline and the brightness, so the demo keeps its route (docs/promo.md)
        // ENHANCEMENT (DEMO AFTERNOON, owner 2026-11-18: "For lighting forget fast day just set it in the afternoon"): the attract
        // demo starts at 13:00, not 09:00; its ~4 h 40 of game time ends near 17:40, still in the table's full or near-full light
        // ($C4A4: hours 10-15 at 15, 16 at 14, 17 at 13). The clock runs at the original's pace.
        if (demo && loaded is null && Settings.DemoAfternoon)
        {
            (play.State.Hour, play.State.Minute) = (13, 0);
            play.State.Panel.Hour(play.State);
        }
        if (loaded is null && System.Environment.GetEnvironmentVariable("HUNTER_HOUR") is { Length: 5 } hhmm)
        {
            (play.State.Hour, play.State.Minute) = (short.Parse(hhmm[..2]), short.Parse(hhmm[3..]));
            play.State.Panel.Hour(play.State);
        }
        if (demo && System.Environment.GetEnvironmentVariable("HUNTER_ORIGINAL_PACE") is null)   // recordings only: the original's own pace
        {
            play.Smooth = true;                                                  // ENHANCEMENT: in-between pictures (Play.Smooth.cs); always on now - smooth_demo's row is gone (owner 2026-10-23)
        }
        ApplySettings(play, demo, fuller);
        // Verification only (headless runs, never in a settings file): HUNTER_START=x:z carries
        // the player to a world position before its first pass - the tests' start-state move
        // (FarViewTests / MoreObjectsTests At: entity 0, words $4A / $4E).
        // Recording only: HUNTER_MISSION=N (run_game --mission) plays Missions game mission N from its start, as the bot's
        // MissionProbe sets it up - the cinema films a bot run of any mission, not only the first
        if (loaded is null && play.State.Mode == GameMode.Missions && System.Environment.GetEnvironmentVariable("HUNTER_MISSION") is { } mn && int.TryParse(mn, out var mi) && mi is >= 1 and <= 11)
        {
            play.State.Mission = 0x10 * mi;
            play.State.Log.NoteObjectives(play.State, 0x9072 + play.State.Mission); play.State.Flags0A4 = 0;
            GD.Print($"HUNTER: mission {mi}");
        }
        if (loaded is null && System.Environment.GetEnvironmentVariable("HUNTER_START") is { } start)
        {
            var f = start.Split(':');
            if (FuzzStart(f[0], f.Length > 1 ? ParseNum(f[1]) : 0) is { } spot)   // kind:n (the fuzzer)
            {
                f = [spot.X.ToString(), spot.Z.ToString()];
                GD.Print($"HUNTER: start {start} at {spot.X}:{spot.Z}");
            }
            var p = play.State.Entities.All[0];
            p.SetW(0x4A, (short)ParseNum(f[0]));
            p.SetW(0x4E, (short)ParseNum(f[1]));
            p.SetW(0x4C, play.State.Ground.Height((ushort)ParseNum(f[0]), (ushort)ParseNum(f[1])));   // the height too, as the tests' At does (without it the player kept the base's height)
            for (var o = 0; o < 6; o += 2) p.SetW(0x50 + o, p.W(0x4A + o));      // the old place too: a put-back ($2B14, $4310) sent him home on the first pass, and an animal took his slot
            var s = play.State;                                                  // the camera too, as SpawnPlayer's ($87B6): the first pass wakes things
            s.Camera = s.Ground.Place(p.UW(0x4A), p.W(0x4C), p.UW(0x4E), s.Distance, s.Lift);   // round it before it moves ($2580 before $374C)
        }
        // Recording only: HUNTER_PLACE=rec:x:z[:turn],... moves world grid records before play (PlaceRecord)
        if (loaded is null && System.Environment.GetEnvironmentVariable("HUNTER_PLACE") is { } place)
            foreach (var item in place.Split(','))
            {
                var f = item.Split(':');
                GD.Print("HUNTER_PLACE: " + PlaceRecord(ParseNum(f[0]), ParseNum(f[1]), ParseNum(f[2]), f.Length > 3 ? ParseNum(f[3]) : null));
            }
        SetupView(d, scriptedInput);
        if (loaded is null) play.StartPlay();                                    // PlayGame's PlayStart ($24FA): the fade in runs once play's frames do
        if (paused) play.State.PauseKey = !play.State.PauseKey;                  // 'p' on the new game's briefing
        if (loaded is not null) { afterLoad = true; StartBriefing(); }           // $23BE
    }

    /// <summary>The menu's settings onto a Play - the one list, for a new game, the demo, a game loaded from the
    /// main menu and one loaded in play (GameViewDisk.LoadBlock; it once built its own list and left 10 out, 11-11).
    /// fuller: the fuller world's level (0 none; a loaded game's grid with stored records needs the room too).</summary>
    static void ApplySettings(Play play, bool demo, int fuller)
    {
        play.OwnShadows = Settings.OwnShadows;                                   // ENHANCEMENT: EHB shadows, every play (game and demo)
        play.SmoothLight = Settings.SmoothLight;                                 // ENHANCEMENT: the minute drift of the daylight, every play (game and demo)
        play.SmoothAnimation = Settings.SmoothAnimation;                         // ENHANCEMENT (ANIMATION: SMOOTH): every play, game and demo (the picture only)
        Channels.SpareVoices = Settings.SoundVoices;                             // ENHANCEMENT (SOUND VOICES: MORE): the speakers only
        play.SkySteps = Settings.SkySteps;                                       // ENHANCEMENT: the stepped sky gradient, every play (game and demo)
        play.AnalogSteering = !demo && Settings.AnalogSteering;                 // ENHANCEMENT: a stick's lean turns a vehicle by as much; never the attract demo
        play.AimReticle = !demo && Settings.AimReticle;                       // ENHANCEMENT: on foot the soldier turns to the reticle, the shot goes under it; never the attract demo
        play.LimbDamage = false;                                                 // LIMB DAMAGE amputated for now (owner 11-08: "we aren't at that level") - its code (rules/Limbs.cs) kept, off everywhere, a saved "on" ignored
        // The attract demo is the recording exactly (owner 2026-11-18: "The attract has a set run"): the four extras that change
        // play (bullet box, fine tilt, A500 pace, straight moves) are never in it - each loses the recording its way and the
        // follower then steers a different world (the wiggle, the shed's items missed, the tank lost). Picture extras stay on.
        play.BulletBox = !demo && Settings.BulletBox;                          // ENHANCEMENT (BULLET BOX): a small bullet box; never the attract demo
        play.FineTilt = !demo && Settings.FineAngles;                          // ENHANCEMENT (FINE ANGLES): the ground tilt from the exact heading; never the attract demo (its picture's fine angles stay, Gpu.FineAngles)
        play.A500Pace = !demo && Settings.A500Pace;                            // ENHANCEMENT (A500 PACE): once-a-pass rules at the A500's pace; never the attract demo
        play.FarView = !demo && Settings.FarView && Settings.Renderer != "3d";   // ENHANCEMENT: the farther ground and object view - never in the demo (it keeps far things live: the recording lost its way at pass 2432, DemoTests); the flat picture's row only - with the 3D world its row is hidden (SettingsMenu.Shown), so a saved FAR no longer changes the game unseen (it widens the in-view box, Objects $4216, and so what is written back)
        play.Maths32 = Settings.Maths32; play.StraightMoves = !demo && Settings.StraightMoves; play.LeaveBehind = !demo && Settings.LeaveBehind; play.EarlyHqBack = Settings.EarlyHqBack; play.BoatsAlongShore = !demo && Settings.BoatsAlongShore;                             // ENHANCEMENT: the upgrade's 32-bit maths, every play (game and demo - owner 2026-10-27: the demo has the latest)
        play.RoomyMemory = Settings.RoomyMemory || Settings.MoreObjects || (fuller > 0);   // more objects needs it too: without it a full cell's records are freed unstored at a save or quit (WriteBackAllIfOnFoot)   // ENHANCEMENT: the lifted memory squeezes, every play (game and demo - owner 2026-10-27: the demo has the latest); a fuller world needs them - its full cells would keep far things live for ever (owner 2026-10-28: "Yes seems logical")
        play.MoreObjects = Settings.MoreObjects || fuller > 0;   // ENHANCEMENT: more live things and a bigger live area; a fuller world needs the room (its busiest 2x2 holds up to 32 records, the original has 29 free slots). The demo too since far-held things wake afresh (owner 2026-11-01; DemoTests keeps it on course)
        play.LivingWorld = demo ? fuller >= 4 : Settings.LivingWorld;           // ENHANCEMENT: LIVING WORLD - the player's games (new or loaded); the demo only in the wild cut (its recorded steering would lose the way)
        play.MessageLog = Settings.MessageLog;                                   // ENHANCEMENT: the scroll-back message window, every play (game and demo)
        play.Graves = Settings.Graves;                                           // ENHANCEMENT: graves on paid deaths, every play (game and demo)
        play.VultureGraves = Settings.VultureGraves;                             // ENHANCEMENT: a new grave draws its vulture pair, every play (game and demo)
    }

    /// <summary>HUNTER_REPLAY (Replay.cs): a game started exactly like the rules tests start one -
    /// the recording's patched world file and energy, AnimEveryBlanks at its default (0), because
    /// the rules tests run their passes with it there too. Replay drives the passes.</summary>
    public void InitReplay(GameData d, Play replayed)
    {
        data = d;
        ui = Ui.Load(d.Directory);
        play = replayed;
        SetupView(d, scriptedInput: true);
    }

    void SetupView(GameData d, bool scriptedInput)
    {
        scripted = scriptedInput;
        pointer = Pointer.Sprite(d.Main);
        pointerColour = d.PaletteBanks[1][17];          // $610C loads all 32 registers from bank 1 ($64E0)
        image = Image.CreateEmpty(320, 200, false, Image.Format.Rgb8);
        texture = ImageTexture.CreateFromImage(image);
        AddChild(new Sprite2D { Texture = texture, Centered = false, TextureFilter = TextureFilterEnum.Nearest });
        AddChild(reticle = new Reticle());               // AIMING: RETICLE - over everything, in game pixels (godot/Reticle.cs)
        AddChild(quickPick);
        // the drawn pointer is the only pointer in play, as on the Amiga (StartSequence does the
        // same for the menus): the OS arrow would sit on top of it
        if (!scripted) Input.MouseMode = Input.MouseModeEnum.Hidden;
        SetupSplit();                                            // players = 2: the screen split (GameViewSplit)
        if (Settings.Renderer == "3d") Setup3D();                // ENHANCEMENT: the real 3D view (godot/real3d)
    }

    Real3DView? real3d;
    /// <summary>Recording only (HUNTER_SPIN, radians a frame): the 3D view turns by itself for a demo clip.</summary>
    /// <summary>Recording only (HUNTER_SWAY "turn:seconds", e.g. "0.6:40"): the 3D view swings slowly up to turn radians
    /// to each side of the game's camera and back, once every seconds of game time (owner 2026-10-31: slow camera
    /// angle changes in the attract video). The view only: the demo's input and the game's camera are untouched.</summary>
    readonly (float Turn, float Seconds) sway3d = System.Environment.GetEnvironmentVariable("HUNTER_SWAY") is { } sw
        && sw.Split(':') is [var a, var b]
        && float.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var turn)
        && float.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs) && secs > 0
        ? (turn, secs) : (0f, 1f);
    DemoCamera? demoCamera;                                         // DEMO CAMERA: MOVING (rules/DemoCamera.cs), made at the demo's first frame
    static readonly bool traceDemo = System.Environment.GetEnvironmentVariable("HUNTER_TRACE_DEMO") is not null;
    static readonly bool noPaint = System.Environment.GetEnvironmentVariable("HUNTER_NO_PAINT") is not null;   // verification: the 3D view's pass (no flat painting) on a headless run
    readonly float spin3d = float.TryParse(System.Environment.GetEnvironmentVariable("HUNTER_SPIN"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0;
    Sprite2D? real3dSprite;
    int fadeWas = 15, heldAt = 15; bool hold3d;   // the 3D view's own fade: the last level, the picture held on the way down and the level it was drawn at
    PanelGpu? panelGpu;
    MapMarksGpu? mapMarks;

    /// <summary>The real 3D view (renderer 3d): its SubViewport at gpu_scale, shown over the view's rows 0-139; the
    /// software picture underneath shows every full screen, and the panel when the card's own (PanelGpu) is not up.</summary>
    void Setup3D()
    {
        gpuScale = WantedScale();
        var fine = play.State.Ground.Fine;
        (cardFine, cardMap) = (fine, play.State.Terrain.Map);            // what the card's ground was built from (live "eye")
        if (System.Environment.GetEnvironmentVariable("HUNTER_LIVE") is not null)   // live "wire": Godot keeps each mesh's edges only when asked
            RenderingServer.SetDebugGenerateWireframes(true);                       // before the meshes are made
        var pictureOnly = fine is null && Demo && Settings.FinerGround > 0;   // FINER GROUND in the demo: the picture's alone (GpuWorld.PictureLift)
        if (pictureOnly)                                                   // the world's own seed at the relief setting (a look)
        {
            var seed = data.Worlds[play.State.Mode].Seed;
            var lvl = FineGround.Reliefs[Math.Clamp(Settings.GroundRelief, 0, FineGround.Reliefs.Count - 1)];
            fine = FineGround.Grow(play.State.Terrain, seed, Settings.FinerGround, lvl.Num, lvl.Den, seed, lvl.Dim, lvl.Hold);
        }
        AddChild(real3d = new Real3DView(play.State.Terrain.Map, gpuScale, new GpuWorld(data.Objfile, data.Sinfile, play.State.Terrain.Map, data.Main, fine, seaBodies: Settings.SeaBodies)));   // FINER GROUND: the card reads the game's store; SHARKS AND SUBS baked here
        var card = real3d.Gpu;
        if (pictureOnly) card.PictureLift = (x, z) => fine!.Height(x, z) - play.State.Ground.Height(x, z);
        Objects.Exploded = (record, from) =>                                            // EXPLOSIONS: PIECES - the card throws them itself
        {
            card.AddBlast(record, GameClock(), from);                                     // this pass's blanks are already counted: the frame it blew up on
            GD.Print($"HUNTER: explosion at frame {Audio.Frame}, type {record[4] << 8 | record[5]:X2}, {(record[0x1C] != 0 ? "in view" : "out of view")}");   // a recording finds its explosions here
        };
        real3dSprite = new Sprite2D
        {
            Texture = real3d.Viewport.GetTexture(),
            Centered = false,
            TextureFilter = TextureFilterEnum.Nearest,
            Scale = new Vector2(1f / gpuScale, 1f / gpuScale),
            Visible = false,
        };
        AddChild(real3dSprite);
        AddChild(panelGpu = new PanelGpu(data, play.State.Panel, pointer, card.EntTex));   // the panel drawn by the card too (godot/real3d/PanelGpu.cs)
        AddChild(mapMarks = new MapMarksGpu(play.State.Log));                           // and the overlay map (godot/real3d/MapMarksGpu.cs)
        wide3d = Settings.WideView;
    }

    bool wide3d;

    /// <summary>Recording only (HUNTER_SHOWCASE, with renderer = 3d): the demo as a showcase that moves on a step
    /// each time the player gets in or out of something (his record's type changes): 0 the original flat
    /// picture, 1 the 3D view, 2 the 3D view opening to the window's width like curtains (3 s), 3 the camera
    /// circling (a step also comes after 3 s with no change, so a quiet stretch still moves on; owner: the
    /// camera waited too long to turn). Prints each step with its frame (HUNTER_SOUND_LOG's clock), so the clip can start at play.</summary>
    readonly bool showcase = System.Environment.GetEnvironmentVariable("HUNTER_SHOWCASE") is not null;
    int showStep = -1, showType, showAt;
    float showWide;
    Shots? shots;                                                              // step 3's cinematic shots (Shots.cs)

    /// <summary>Recording only (HUNTER_SLOWMO "from:to:factor", capture frames): from..to the game advances factor times
    /// slower - one blank and its pass every factor-th frame, the frames between draw real in-between pictures
    /// (Play.SubBlank), so the rules run the same passes in the same order. Audio.Frame is the capture frame; the sound
    /// log gets "0 slowmo from to factor" for make_video's tape slowing.</summary>
    readonly (int From, int To, int Factor) slowmo = System.Environment.GetEnvironmentVariable("HUNTER_SLOWMO") is { } sm
        && sm.Split(':') is [var sa, var sb, var sc] && int.TryParse(sa, out var sFrom) && int.TryParse(sb, out var sTo) && int.TryParse(sc, out var sFac) && sFac > 1
        ? (sFrom, sTo, sFac) : (0, 0, 1);

    /// <summary>True on a slow-motion frame that only draws; sets the picture's share of the blank.</summary>
    bool SlowmoHold()
    {
        var n = Audio.Frame - slowmo.From;
        var sub = n >= 0 && Audio.Frame < slowmo.To ? n % slowmo.Factor : 0;
        play.SubBlank = (double)sub / slowmo.Factor;
        return sub > 0;
    }

    /// <summary>Recording only (HUNTER_MISSILE_RIDE=n, run_game --missile-ride n): the nth shot fired - bazooka shell $53,
    /// SAM $57 or ASM $58, anyone's, counted as each appears - the 3D view rides behind it, looking where it flies; when it
    /// goes off (the record becomes $61 / $62, kind record +$08) the view is the game's again. The view only.
    /// In the demo the 6th is the SAM that chases the helicopter (fired at pass 5914).</summary>
    readonly int missileRide = int.TryParse(System.Environment.GetEnvironmentVariable("HUNTER_MISSILE_RIDE"), out var mr) ? mr : 0;
    readonly System.Collections.Generic.HashSet<int> shotSlots = [];
    int shotsFired, rideSlot = -1, rideHoldUntil;
    float rideTurn;
    double rideBack;
    (ushort X, short Y, ushort Z) rideAt;

    /// <summary>Recording only (HUNTER_CINEMA "chase:300,flyby:300,...", set by run_game --cinema): the promo films
    /// its own clip - 3D from the first frame (no flat or curtain steps), the listed shots for their frames each,
    /// the panel and message line hidden so the 3D view fills the whole 320x200 picture. HUNTER_SHOWCASE untouched.</summary>
    readonly string? cinema = System.Environment.GetEnvironmentVariable("HUNTER_CINEMA");
    (Shots.Kind Kind, int Frames)[] cinemaShots = [];
    int cinemaAt = -1, cinemaIdx;

    /// <summary>HUNTER_CHECK_FEED: prove frame by frame that the card's uploaded player record is the in-between
    /// one (the card-mode stutter, fixed in 9f4bb89): on every in-between frame, place the player's uploaded
    /// record and compare with the camera the on-screen picture was drawn from; 600 frames checked with 0
    /// mismatches prints CHECKFEED PASS, any mismatch prints CHECKFEED FAIL with the first frame.</summary>
    readonly bool checkFeed = System.Environment.GetEnvironmentVariable("HUNTER_CHECK_FEED") is not null;
    int feedFrames, feedBad, feedFirstBad = -1;
    string feedFirstLine = "";
    const bool ShowcaseWidens = false;                                       // owner 2026-10-25: no widescreen for now; true brings the curtains back

    void ShowcaseStep()
    {
        var type = PlayerType;
        if (showStep < 0) { showStep = 0; showType = type; showAt = Audio.Frame; GD.Print($"HUNTER: showcase play from frame {Audio.Frame}"); }
        else if (showStep < 3 && (type != showType || Audio.Frame - showAt > 50 * 3 || (showStep == 2 && ShowcaseWidens && showWide >= 1f)))   // or 3 s with no change; the circling starts as the curtains finish
        {
            showType = type;
            showAt = Audio.Frame;
            showStep++;
            GD.Print($"HUNTER: showcase step {showStep} at frame {Audio.Frame}");
        }
        if (showStep >= 2 && ShowcaseWidens) showWide =Math.Min(1f, showWide + 1f / 150);
        if (showStep >= 3 && real3d!.Gpu is { Ready: true })
        {
            // the cinematic shots replace the circling: the free camera set each frame from the player's uploaded place
            shots ??= new Shots();
            var (sx, sy, sz) = real3d.Gpu.PlayerPlace;
            real3d.Free = play.State.InsideView ? null                                  // indoors the room is drawn for the game's own camera only (the shell pushed back as a backdrop)
                : shots.Next(sx, sy, sz, play.State.Entities.All[0].B[0x7E], play.State.Ground);
        }
    }

    /// <summary>HUNTER_MISSILE_RIDE: counts the shots as they appear on the card's upload; while the chosen one flies the
    /// free camera rides behind it, from its uploaded place; otherwise (or indoors) the game's.</summary>
    void MissileRideStep()
    {
        var gpu = real3d!.Gpu;
        if (!gpu.Ready) return;
        bool Flying(int i) => gpu.RecordByte(i, 0) != 0 && gpu.RecordByte(i, 4) == 0 && gpu.RecordByte(i, 5) is 0x53 or 0x57 or 0x58;
        for (var i = 0; i < play.State.Entities.Count; i++)
            if (!Flying(i))
            {
                shotSlots.Remove(i);
                if (i != rideSlot) continue;
                rideSlot = -1;
                rideHoldUntil = Audio.Frame + 150;                              // the camera stays on the hit for 3 s
                GD.Print($"HUNTER: missile ride: shot went off at frame {Audio.Frame}");
            }
            else if (shotSlots.Add(i) && ++shotsFired == missileRide)
            {
                rideSlot = i;
                rideTurn = -gpu.RecordByte(i, 0x7E) * Mathf.Tau / 256;
                GD.Print($"HUNTER: missile ride on shot {shotsFired} (slot {i}) at frame {Audio.Frame}");
            }
        var held = rideSlot < 0 && Audio.Frame < rideHoldUntil;              // the 3 s on the hit: pulling back from where it went off
        if ((rideSlot < 0 && !held) || play.State.InsideView) { real3d.Free = null; return; }
        if (!held)
        {
            rideAt = gpu.PlaceOf(rideSlot);
            var target = -gpu.RecordByte(rideSlot, 0x7E) * Mathf.Tau / 256; // its facing, as Shots (the facing is (-sin, cos))
            rideTurn += Mathf.Wrap(target - rideTurn, -Mathf.Pi, Mathf.Pi) * 0.2f;   // eased: the heading turns in 1/256 steps
            rideBack = 160;
        }
        else rideBack += (700 - rideBack) * 0.08;                           // back far enough to see the whole blast
        var (x, y, z) = rideAt;
        var turn = rideTurn;
        var ground = play.State.Ground;
        double ex = x + Math.Sin(turn) * rideBack, ez = z - Math.Cos(turn) * rideBack;   // behind it
        var ey = Math.Max(y + 40, ground.Height((ushort)(int)ex, (ushort)(int)ez) + 30);   // a little above, never under the land
        double dx = x - Math.Sin(turn) * 800 - ex, dz = z + Math.Cos(turn) * 800 - ez;   // aimed at a point far ahead of it, level with it
        real3d.Free = new FreeCamera((ex, ey, ez), (float)Math.Atan2(-dx, dz), (float)Math.Atan((ey - y) / 2 / Math.Max(Math.Sqrt(dx * dx + dz * dz), 1)));
    }

    /// <summary>HUNTER_CINEMA's own timing: a cut at the start, then the next listed shot when its frames are up
    /// (the last one keeps playing); the free camera itself as the showcase's step 3 above.</summary>
    void CinemaStep()
    {
        if (cinemaAt < 0)
        {
            var parts = cinema!.Split(',');
            cinemaShots = new (Shots.Kind Kind, int Frames)[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                var kv = parts[i].Split(':');                                 // kind:frames
                System.Enum.TryParse(kv[0], true, out Shots.Kind kind);
                cinemaShots[i] = (kind, int.Parse(kv[1]));
            }
            real3d!.Viewport.Size = new Vector2I(View.Width * gpuScale, (Play.PanelTop + Rules.Panel.Rows) * gpuScale);   // the panel's 60 rows join the view: the whole picture
            shots ??= new Shots();
            shots.Cut(cinemaShots[0].Kind);
            cinemaAt = Audio.Frame;
            GD.Print($"HUNTER: cinema {cinema} from frame {Audio.Frame}");
        }
        else if (cinemaIdx < cinemaShots.Length && Audio.Frame - cinemaAt >= cinemaShots[cinemaIdx].Frames)
        {
            cinemaAt = Audio.Frame;
            cinemaIdx++;
            if (cinemaIdx < cinemaShots.Length)
            {
                shots!.Cut(cinemaShots[cinemaIdx].Kind);
                GD.Print($"HUNTER: cinema shot {cinemaIdx + 1} {cinemaShots[cinemaIdx].Kind} at frame {Audio.Frame}");
            }
        }
        if (real3d!.Gpu is { Ready: true })
        {
            shots ??= new Shots();
            var (sx, sy, sz) = real3d.Gpu.PlayerPlace;
            real3d.Free = play.State.InsideView ? null : shots.Next(sx, sy, sz, play.State.Entities.All[0].B[0x7E], play.State.Ground);
        }
        if (play.Pilot is Bot { Brain: Hunt actor })                         // the film's cut list: what the bot does, by frame (cinema.py cuts on it)
        {
            var kills = 0;
            foreach (var k in play.State.Kills) kills += k;
            var now = $"{actor.Doing} | ${PlayerType & 0xFF:X2}";
            if (now != filmDoing) { filmDoing = now; GD.Print($"HUNTER: film frame {Audio.Frame}: {now}"); }
            if (kills != filmKills) { if (filmKills >= 0) GD.Print($"HUNTER: film frame {Audio.Frame}: destroyed"); filmKills = kills; }
        }
    }
    string? filmDoing;
    bool filmScreen;
    int filmKills = -1;

    /// <summary>GPU SCALE: the chosen 1 / 2 / 4, or AUTO - the window's own whole scale (Display's layout: game rows to
    /// screen rows), so a drawn pixel is a screen pixel; 1 before the window is laid out.</summary>
    static int WantedScale()
    {
        if (Settings.GpuScale > 0) return Settings.GpuScale;
        var h = Display.I.Picture.H;
        return h > 0 ? System.Math.Max(1, h / Display.FrameRows(Display.I.Look)) : 1;
    }

    /// <summary>The 3D view follows GPU SCALE as it changes (the menu, the window's size, F10's look); not while a
    /// cinema run holds its own size.</summary>
    void FollowScale()
    {
        var want = WantedScale();
        if (want == gpuScale || cinema is not null) return;
        gpuScale = want;
        real3d!.Rescale(want);
        real3dSprite!.Scale = new Vector2(1f / want, 1f / want);
    }

    /// <summary>NEW, not the original (VIEW: WIDE): the 3D view as wide as the window, centred on the 320-wide
    /// frame - the camera keeps its height and scale (Camera3D keeps the height), so the extra width is more
    /// world left and right, nothing stretched. Follows the window's size.</summary>
    void Wide3D()
    {
        var open = showcase ? showWide : wide3d ? 1f : 0f;               // the showcase opens it like curtains
        if (open <= 0 && real3d!.Viewport.Size.X == View.Width * gpuScale) return;
        var picture = Display.I.Picture;
        if (picture.W <= 0) return;
        var across = View.Width + (View.Width * (float)GetWindow().Size.X / picture.W - View.Width) * open;   // in game pixels
        var w = (int)Math.Round(across * gpuScale);
        if (real3d!.Viewport.Size.X != w) real3d.Viewport.Size = new Vector2I(w, View.Height * gpuScale);
        real3dSprite!.Position = new Vector2(-(across - View.Width) / 2, 0);
    }

    /// <summary>The game's own frames for the card's moving effects (the living sea, the explosion pieces): one for each
    /// blank the game counts ($1A4 BlankCount, $C6FE) - none while paused or a screen is up. A new game's reset of the
    /// count carries on from here, never a jump back.</summary>
    float GameClock()
    {
        var now = play.State.BlankCount;
        var step = (short)(now - clockSeen);
        if (step is > 0 and <= 50) gameClock += step;
        clockSeen = now;
        return gameClock + (float)play.SubBlank;                   // HUNTER_SLOWMO's pictures between two blanks
    }
    float gameClock;
    int clockSeen;

    /// <summary>After a pass: the 3D camera follows the game's (the picture's) camera, with the game's own palette
    /// words; the card-built world draws outdoors and, inside a building, the room.</summary>
    /// <summary>The camera distance and lift the on-screen picture uses: the game's own.</summary>
    (int Distance, short Lift) ViewDistanceNow() => (play.State.Distance, play.State.Lift);

    void Real3DFrame()
    {
        if (liveCam is { } held) real3d!.Free = held;               // HUNTER_LIVE's cam: over the game's and cinema's cameras
        // Q / E: the card's view window and in-view marks follow the turned eye (Real3DView.Frame's own View3D.Eye)
        Hunter.Rules.ViewDistance.Turn? TurnedView(Camera c) => real3d!.Yaw == 0 ? null
            : Hunter.Rules.View3D.Eye(c, ViewDistanceNow().Distance, real3d.Yaw) is var (ex, _, ez) ? new(ex, ez, real3d.Yaw) : null;
        FollowScale();
        Wide3D();
        gpuSky ??= new Sky(data.Main, data.PaletteBanks);
        gpuSky.Steps = play.SkySteps;
        rawView ??= System.Array.ConvertAll(data.PaletteBanks[2], c => (int)c);       // made once: the bank and the sky's colours never change
        rawSky ??= System.Array.ConvertAll(gpuSky.Colours, c => (int)c);
        // ENHANCEMENT (owner 2026-11-01: the fades made simple): the 3D view's own fade - the game's fade level dims the
        // whole picture; on the way down the last picture is held (nothing new drawn), on the way up the new scene shows
        var fade = play.ScreenFade;
        if (fade < fadeWas) { if (!hold3d) heldAt = fadeWas; hold3d = true; }   // held only while it falls: a single step down that stays (the
        else hold3d = false;                                                    // evening's level, 14 -> 13) froze the view for good (the film 11-08)
        fadeWas = fade;
        real3d!.Viewport.RenderTargetUpdateMode = hold3d ? SubViewport.UpdateMode.Disabled : SubViewport.UpdateMode.Always;
        // the fade is the shaders' own (fade_level, below), so a drawn picture is not dimmed again (11-08: fade / 15 on top
        // dimmed it twice); a HELD picture is not drawn, so it is dimmed by how far the fade fell since it was (11-08: without
        // this the way into a building showed the outside frozen at full light, not the fade to black - the attract)
        var dim = hold3d ? fade / (float)heldAt : 1f;
        real3dSprite!.Modulate = new Color(dim, dim, dim);
        var onScreen = play.Mode == PlayMode.Screen && play.Screens.Routine != ItemTableRoutine;
        if (cinema is not null && onScreen != filmScreen) { filmScreen = onScreen; GD.Print($"HUNTER: film frame {Audio.Frame}: {(onScreen ? "screen" : "view")}"); }   // the film's editor cuts round screens
        if (onScreen)                                                    // map, log book, store: none of it shows - no upload, no draw
        {
            real3dSprite.Visible = false;
            real3d.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            return;
        }
        var raw = new RawPalette(rawView, rawSky,
            fade, play.SkyLevel, play.DeathStep, play.State.DaylightLevel, play.SmoothDrawLevel, play.PanelLevel);   // the game's own words and levels: the shaders fade them (the screen's fade too, as the original's Faded)
        var clock = GameClock();
        real3d!.Gpu.BandGain(rawView!);
        var t3 = System.Diagnostics.Stopwatch.GetTimestamp();
        var inside = play.DrawnInside;                                                   // what the game's last pass drew (the flag goes up before the fade)
        var shell = inside ? play.State.BuildingSlot : -1;
        real3d!.Gpu.PiecesStay = Settings.CardPieces && Settings.PiecesStay;            // EXPLOSIONS: LASTING
        real3d.Gpu.FineAngles = Settings.FineAngles;                                   // FINE ANGLES: the records' fine angles drawn
        play.WithInBetweenPoses(() =>                                                   // the card reads the records itself: at their in-between places, as the camera
            real3d!.Gpu.Update(play.State.Entities, play.State.Anim, inside, shell, play.State.Models.Buf, play.State.DebrisPos, real3d.Gpu is { Ready: true } ? play.StoredLooks() : null,
                play.State, Settings.SunShading, play.OwnShadows, Settings.ViewDistance, play.PictureCamera ?? play.State.Camera,
                real3d.Viewport.Size.X / (double)(View.Width * gpuScale), Settings.CardPieces, Settings.GroundCuts, TurnedView(play.PictureCamera ?? play.State.Camera), clock,
                play.AnimFraction, Settings.OwnColours, Settings.SeeThroughGlass));                  // SMOOTH PARTS: the step's fraction, the card slides the moving parts across it
        real3d!.Frame(play.PictureCamera ?? play.State.Camera, ViewDistanceNow().Distance, ViewDistanceNow().Lift, inside ? shell : 0, inside, play.State.Horizon1518, raw,
            play.SkySteps,                                                                  // SKY: SMOOTH - the stepped full-colour bands
            Settings.Haze, Hunter.Rules.ViewDistance.FarEdge(Hunter.Rules.ViewDistance.TurnedRows(play.State.Ground, Settings.ViewDistance)), Settings.LivingSea, clock / 50f, Settings.GroundPattern, Settings.Clouds, Settings.SeaCycling, Settings.SkyTime);   // SKY's time of day   // HAZE, to the drawn ground's far edge (FAR VIEW's deeper patch at ORIGINAL); SEA
        if (checkFeed && real3d!.Gpu is { Ready: true } && play.SmoothWillDraw && play.PictureCamera is { } picCam)   // Ready: the card only holds a real upload once its first Update ran
        {                                                                          // after SmoothPicture: the picture's camera and the card's upload are both this frame's
            var (x, y, z) = real3d.Gpu!.PlayerPlace;
            var got = play.State.Ground.Place(x, y, z, ViewDistanceNow().Distance, ViewDistanceNow().Lift).G1548;
            feedFrames++;
            if (got != picCam.G1548)
            {
                if (feedBad < 3)
                {
                    var p = play.State.Entities.All[0];
                    GD.Print($"CHECKFEED mismatch {feedBad + 1} at frame {Audio.Frame}: uploaded {x},{y},{z} record now {p.UW(0x4A)},{p.W(0x4C)},{p.UW(0x4E)} card G1548 {got}, picture G1548 {picCam.G1548}");
                }
                if (feedFirstBad < 0) { feedFirstBad = Audio.Frame; feedFirstLine = $"card G1548 {got}, picture G1548 {picCam.G1548}"; }
                feedBad++;
            }
            if (feedFrames == 600)
                GD.Print($"CHECKFEED {(feedBad == 0 ? "PASS" : "FAIL")} {feedFrames} frames, {feedBad} mismatches" +
                         (feedFirstBad < 0 ? "" : $", first at frame {feedFirstBad}: {feedFirstLine}"));
        }
        Frame3DTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t3;
        Frames3D++;
        var screen = play.Mode == PlayMode.Screen;                                          // map, log book, store: the whole view is the screen's
        var items = screen && play.Screens.Routine == ItemTableRoutine;                     // item table: the frozen view above its row 92 split
        real3dSprite!.Visible = (cinema is not null || !showcase || showStep >= 1) && (!screen || items);   // indoors too (the card draws the room); cinema: 3D from the first frame, the showcase opens on the original picture
        if (!real3dSprite.Visible) real3d!.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;   // map, log book, store: nothing of it shows, so it is not drawn
        real3dSprite!.RegionEnabled = true;                                               // the texture's own width: the wide view too
        var viewRows = items ? ItemTableTop : cinema is not null ? Play.PanelTop + Rules.Panel.Rows : Rules.View.Height - 1;   // row 139 is the mode 3 copper's black planes-off line (rules/Play.cs), not view; cinema: the whole picture,
                                                                                    // the panel's rows too (its viewport is that tall - with 140 the old panel picture showed under it)
        real3dSprite!.RegionRect = new Rect2(0, 0, real3dSprite.Texture.GetSize().X, viewRows * gpuScale);
        if (panelGpu is not null)                                                         // the card draws the panel while it is the plain panel: play, no history window, one player
        {
            var plain = cinema is null && real3dSprite.Visible && play.Mode == PlayMode.Play && play.MessageLogBack == 0 && play.State.Panel2 is null;   // cinema hides the panel: the 3D view is the whole picture
            if (plain) panelGpu.Frame(play, raw, mouseX, PointerRowDrawn(), pointerColour, !quickPick.Open);
            panelGpu.Visible = plain;
        }
        if (mapMarks is not null)                                                         // the overlay map over the card's picture: the processor paints no view (4c285a0)
        {
            var marksOn = real3dSprite.Visible && !inside && play.Mode == PlayMode.Play && play.State.MapMarks;
            if (marksOn) mapMarks.Frame(play, raw, (play.PictureCamera ?? play.State.Camera).Height0E, play.SkySteps ? SkyRowsTexture(play.DeathStep) : null);   // the A500 gradient's sky rows
            mapMarks.Visible = marksOn;
        }
    }

    /// <summary>HUNTER_STATS: the processor's work feeding the card each frame (GpuWorld.Update, Real3DView.Frame),
    /// summed since the start.</summary>
    public long Frame3DTicks, Frames3D;
    /// <summary>HUNTER_STATS: this frame ran a pass of play (not a menu, the briefing or a screen).</summary>
    public bool InPlay;
    /// <summary>HUNTER_STATS: the player record's type (+$04) - it changes as he gets in or out of a vehicle.</summary>
    public int PlayerType => play.State.Entities.All[0].W(4);
    /// <summary>The fuzzer's death report: the frame his record turns into an end ($35 and up, PlayerSetUpOrDie $2EC4), what
    /// he was and became, where (sea or land, height against the ground), the drowned flag ($216 bit 2), the energy
    /// left, and every live thing within 2000 (type, distance) - the shells, blasts and enemies that could have done it.</summary>
    public string? DeathWatch()
    {
        var s = play.State;
        var p = s.Entities.All[0];
        var was = watchedType;
        watchedType = p.W(4);
        if (watchedType < 0x35 || was >= 0x35 || was < 0) return null;
        int x = p.UW(0x4A), z = p.UW(0x4E);
        var near = Enumerable.Range(1, s.Entities.Count - 1).Select(r => s.Entities.All[r])
            .Where(e => e.Alive && Math.Max(Math.Abs(e.UW(0x4A) - x), Math.Abs(e.UW(0x4E) - z)) < 2000)
            .Select(e => $"{e.W(4):X2}@{Math.Max(Math.Abs(e.UW(0x4A) - x), Math.Abs(e.UW(0x4E) - z))}");
        return $"was {was:X2} became {watchedType:X2} at {x},{z} {(GroundMesh.Water(s.Terrain.Map, (ushort)x, (ushort)z) ? "sea" : "land")} " +
               $"height {p.W(0x4C)} ground {s.Ground.Height((ushort)x, (ushort)z)} drowned {((s.Status216 & 4) != 0 ? 1 : 0)} energy {s.Energy} " +
               $"near {string.Join(" ", near)}";
    }
    int watchedType = -1;

    /// <summary>The fuzzer's rule check, each frame: the player's slot holds the soldier ($1E), a vehicle ($00-$11,
    /// truck .. bicycle, the maps' lists - the boat, ship and helicopters seen ridden), $30 or above (his bail-out
    /// kinds, $30 seen in the original's own demo 2026-11-02, and his end, $35 and up, PlayerSetUpOrDie $2EC4) -
    /// anything else, an animal or a person (the rabbit of 2026-11-02), is reported once, with its frame. (The
    /// first try took flag bit 28 for "a vehicle": that bit is the soldier's, every ridden boat was a false break.)</summary>
    public string? RuleWatch()
    {
        CoverWatch();
        var p = play.State.Entities.All[0];
        var t = p.W(4);
        var ok = !p.Alive || t == 0x1E || t <= 0x11 || t >= 0x30;
        if (ok || ruleSeen == t) return null;
        ruleSeen = t;
        return $"the player's slot holds type {t:X2} (flags {p.Flags:X8}) {Whereabouts}";
    }
    int ruleSeen = -1;

    /// <summary>The fuzzer's coverage, each frame (with RuleWatch): the first time a game reaches a thing, one line
    /// "HUNTER: new at frame F: what" - his kind (+4), a vehicle under way (speed +$3E > 0), the play mode, indoors,
    /// the weapon in hand ($200), a 2048 map cell, his height above the ground in 512 bands, a kind live in any slot
    /// ("as XX": his kind at that frame).
    /// The fuzzer keeps the games that reach new things and branches from there.</summary>
    void CoverWatch()
    {
        var s = play.State;
        var p = s.Entities.All[0];
        if (!p.Alive) return;
        int x = p.UW(0x4A), z = p.UW(0x4E), t = p.W(4);
        void Note(string what) { if (covered.Add(what)) GD.Print($"HUNTER: new at frame {Audio.Frame + 1} as {t:X2}: {what}"); }   // as: his kind then (the moves to branch with)
        Note($"type {t:X2}");
        if (t < 0x1E && p.W(0x3E) != 0) Note($"going {t:X2}");
        Note($"mode {play.Mode}");
        if (s.InsideView) Note("inside");
        Note($"weapon {s.Weapon}");
        Note($"cell {x >> 11},{z >> 11}");
        Note($"up {Math.Max(0, p.W(0x4C) - s.Ground.Height((ushort)x, (ushort)z)) >> 9}");
        for (var n = 1; n < s.Entities.Count; n++)
            if (s.Entities.All[n].Alive) Note($"live {s.Entities.All[n].W(4):X2}");
    }
    readonly System.Collections.Generic.HashSet<string> covered = [];

    /// <summary>HUNTER_START=kind:n (the fuzzer): the n-th (mod the count) spot of a kind, from the map itself -
    /// vehicle (300 beside a grid record of type $00-$11, truck .. bicycle), building (150 south of a building's
    /// record, $95-$AF but the stump: its door faces south, PlaceAtDoor $432A, so the stick's up walks in), high
    /// and low (the land squares in the top and bottom tenth of height), land (any land square), shore (land beside
    /// sea), sea (open sea).
    /// Null for a plain x:z.</summary>
    (int X, int Z)? FuzzStart(string kind, int n)
    {
        var s = play.State;
        int Type(int r) => s.Grid[r] + ExtraKinds.TypeBase(data.Main, s.Grid[r + 1]);
        (int, int) Rec(int r, int dx, int dz) => ((s.Grid[r + 6] << 8 | s.Grid[r + 7]) + dx, (s.Grid[r + 10] << 8 | s.Grid[r + 11]) + dz);
        var recs = Enumerable.Range(0, s.Grid.Length / 16).Select(i => i * 16).Where(r => (s.Grid[r] | s.Grid[r + 1]) != 0).ToList();
        var squares = Enumerable.Range(0, 128 * 128).Select(i => (C: i % 128, R: i / 128)).Where(q => q.C > 2 && q.R > 2 && q.C < 125 && q.R < 125).ToList();
        bool Sea(int c, int r) => s.Terrain.Type(c, r) == 0;
        int H((int C, int R) q) => s.Ground.Height((ushort)(q.C * 512 + 256), (ushort)(q.R * 512 + 256));
        (int, int) Mid((int C, int R) q) => (q.C * 512 + 256, q.R * 512 + 256);
        var land = squares.Where(q => !Sea(q.C, q.R)).OrderBy(H).ToList();
        System.Collections.Generic.List<(int, int)> spots = kind switch
        {
            "vehicle" => recs.Where(r => Type(r) <= 0x11).Select(r => Rec(r, Clear, 0)).ToList(),
            "building" => recs.Where(r => Type(r) is >= 0x95 and <= 0xAF and not 0xA8).Select(r => Rec(r, 0, -150)).ToList(),
            "high" => land.Skip(land.Count * 9 / 10).Select(Mid).ToList(),
            "low" => land.Take(land.Count / 10).Select(Mid).ToList(),
            "land" => land.Select(Mid).ToList(),
            "shore" => land.Where(q => Sea(q.C + 1, q.R) || Sea(q.C - 1, q.R) || Sea(q.C, q.R + 1) || Sea(q.C, q.R - 1)).Select(Mid).ToList(),
            "sea" => squares.Where(q => Sea(q.C, q.R) && Sea(q.C + 1, q.R) && Sea(q.C - 1, q.R) && Sea(q.C, q.R + 1) && Sea(q.C, q.R - 1)).Select(Mid).ToList(),
            _ => null!,
        };
        if (spots is null) return null;
        if (kind != "building")                                                  // not inside a thing's box: every move put back ($4518), he never left
        {
            var at = recs.Select(r => Rec(r, 0, 0)).ToList();
            spots = spots.Where(p => !at.Any(q => Math.Abs(q.Item1 - p.Item1) < Clear && Math.Abs(q.Item2 - p.Item2) < Clear)).ToList();
        }
        return spots.Count == 0 ? (0, 0) : spots[n % spots.Count];
    }
    const int Clear = 300;                                                       // the biggest model box, 280 a side ($334A), + his 12

    /// <summary>HUNTER_STATS: where he is and what is up (the fuzzer counts the ground a game covers).</summary>
    public string Whereabouts => $"at {play.State.Entities.All[0].UW(0x4A)},{play.State.Entities.All[0].UW(0x4E)} {play.Mode}{(play.State.InsideView ? " inside" : "")} heading {play.State.Entities.All[0].B[0x7E]:X2}";

    int[]? rawView, rawSky;
    readonly byte[] skyRowsBuf = new byte[View.Height * 3], screenLut = new byte[16 * 3];   // made once, filled each frame

    // A sky colour at the sky's fade, or at SMOOTH daylight's level (Play.ScreenOf's SkyLit)
    ushort SkyLit(ushort c) => play.SmoothDrawLevel is >= 0 and var s ? Play.Faded60(c, s) : Play.Faded(c, play.SkyLevel);

    /// <summary>The 140 sky row colours (Sky.Rows with Steps, through the sky fade and the death fade) as a
    /// 1 x 140 texture - the 3D view's sky gradient.</summary>
    ImageTexture SkyRowsTexture(int death)
    {
        var rows = gpuSky!.Rows((play.PictureCamera ?? play.State.Camera).Height0E);
        var buf = skyRowsBuf;
        for (var y = 0; y < View.Height; y++)
        {
            var c = death > 0 ? Play.DeathFaded(rows[y], death) : SkyLit(rows[y]);   // from the raw row colour, as Play.ScreenOf (DeathFadeTests)
            buf[3 * y] = (byte)((c >> 8 & 15) * 17);
            buf[3 * y + 1] = (byte)((c >> 4 & 15) * 17);
            buf[3 * y + 2] = (byte)((c & 15) * 17);
        }
        var img = gpuSkyImage ??= Image.CreateEmpty(1, View.Height, false, Image.Format.Rgb8);
        img.SetData(1, View.Height, false, Image.Format.Rgb8, buf);
        (gpuSkyTex ??= ImageTexture.CreateFromImage(img)).Update(img);
        return gpuSkyTex;
    }

    public override void _ExitTree()
    {
        ExitSplit();
        if (real3d != null) Objects.Exploded = null;                               // the next view hooks its own card
    }

    /// <summary>Scripted input (headless runs): held until changed.</summary>
    public void Script(Joystick j) => scriptJoy = j;

    // Recording only: HUNTER_AUTOPILOT="x:z,x:z:f,..." steers the soldier through waypoints with the
    // ordinary joystick, one frame at a time - every game rule applies, nothing is moved by hand. Each
    // frame it holds the stick direction whose heading (the game's own table at $2632) is closest to the
    // next waypoint; ":f" also holds fire on the way; a waypoint counts as reached within 40 units (a shed's
    // door: 30 right of its centre, walked into northwards - the original's enter_building_hunter recording).
    // After the last one the stick is let go (HUNTER_INPUT items still apply between waypoints).
    System.Collections.Generic.List<(int X, int Z, bool Fire)>? waypoints;
    int waypointAt;
    byte[]? headTable;
    // a "door" waypoint (written "door"): stick up until inside; stuck 20 frames -> 12 frames to the right
    bool doorPush;
    int autoFrames, doorInside;
    bool lastInside;
    int doorLastZ = -1, doorStill, doorSide;
    public int WaypointAt => waypointAt;
    static readonly bool AutopilotLog = System.Environment.GetEnvironmentVariable("HUNTER_AUTOPILOT_LOG") is not null;

    Joystick? Autopilot()
    {
        waypoints ??= (System.Environment.GetEnvironmentVariable("HUNTER_AUTOPILOT") ?? "")
            .Split(',', System.StringSplitOptions.RemoveEmptyEntries).Select(w => w.Split(':'))
            .Select(p => p[0] == "door" ? (-1, -1, false) : (int.Parse(p[0]), int.Parse(p[1]), p.Length > 2 && p[2] == "f")).ToList();
        var insideNow = play.State.InsideView;
        var logIt = insideNow != lastInside || (autoFrames >= 790 && autoFrames <= 960);   // every change, and the entry
        lastInside = insideNow;
        if (AutopilotLog && (autoFrames++ % 50 == 0 || logIt))
        {
            var q = play.State.Entities.All[0];
            GD.Print($"AUTOPILOT f={autoFrames} wp={waypointAt} x={q.UW(0x4A)} z={q.UW(0x4E)} head={q.B[0x7E]} speed={q.W(0x3E)} inside={play.State.InsideView} +20={q.B[0x20]} mode={play.Mode}");
        }
        if (waypointAt >= waypoints.Count) return null;
        var main = play.State.Data.Main;
        headTable ??= Enumerable.Range(0, 16).Select(i => main[0x2632 - 0x800 + i]).ToArray();
        var p = play.State.Entities.All[0];
        var (tx, tz, fire) = waypoints[waypointAt];
        // through the door: keep walking in for a second - a player still on the door line the frame after entering
        // is put straight back outside ($42E6 -> PlaceAtDoor $432A, buildings.md 3); the original's own
        // enter_building_hunter recording holds the stick forward and stays in
        if (play.State.InsideView && doorPush)
        {
            if (++doorInside < 50) return new Joystick(true, false, false, false, false);
            doorPush = false; doorInside = 0; waypointAt++; return Autopilot();
        }
        if (tx == -1)                                                        // "door": walk north into it, as a player does
        {
            doorPush = true;
            var z = p.UW(0x4E);
            if (z != doorLastZ) { doorLastZ = z; doorStill = 0; }
            else if (++doorStill > 20) { doorSide = 12; doorStill = 0; }      // stopped against the wall: sidestep right
            if (doorSide > 0) { doorSide--; return new Joystick(false, false, false, true, false); }
            return new Joystick(true, false, false, false, false);
        }
        int dx = tx - p.UW(0x4A), dz = tz - p.UW(0x4E);
        if (dx * dx + dz * dz < 40 * 40) { waypointAt++; return Autopilot(); }   // tight: a door is only a little wider
        // heading byte as the guided bot computed it (0 = +z, 64 = +x), wrapped into 0..255
        var want = (byte)(((int)System.Math.Round(System.Math.Atan2(dx, dz) * 256 / (2 * System.Math.PI)) % 256 + 256) % 256);
        int best = 2, bestErr = int.MaxValue;
        for (var dir = 1; dir < 16; dir++)
        {
            if ((dir & 3) == 3 || (dir & 12) == 12) continue;                    // up+down / left+right: not a direction
            var raw = (headTable[dir] - want) & 0xFF;                           // wrapped angle difference
            var err = raw > 128 ? 256 - raw : raw;                              // Math.Abs((sbyte)-128) would overflow
            if (err < bestErr) { bestErr = err; best = dir; }
        }
        return new Joystick((best & 2) != 0, (best & 1) != 0, (best & 8) != 0, (best & 4) != 0, fire);
    }
    /// <summary>Headless: open the quick pick, light item i, let go.</summary>
    public void ScriptQuick(int i) { quickPick.Show(); quickPick.Pick(i); if (quickPick.Hide() is { } raw) play.KeyDown(raw); }
    /// <summary>Headless: open the quick pick with item i lit and leave it up (-1: nothing lit yet).</summary>
    public void ScriptQuickHold(int i) { quickPick.Show(); quickPick.Pick(i); }
    public void ScriptZoom(bool zoomOut, bool zoomIn) => (scriptZoomOut, scriptZoomIn) = (zoomOut, zoomIn);

    // Headless only (HUNTER_INPUT): a synthetic wheel / Page Up / Page Down event, delivered
    // through the same _Input a real one takes - the verbs build exactly the event Godot
    // delivers for a wheel tick / key press and Inject lifts the scripted guard for that one
    // event. The wheel sits at the message window's middle (game 160, 190), carried in the
    // event's own Position - the field a real wheel event is filled with.
    bool injecting;
    void Inject(InputEvent e) { injecting = true; _Input(e); injecting = false; }
    public void ScriptWheel(bool up)
    {
        var (px, py, pw, ph) = Display.I.Picture;
        var frameH = Display.FrameRows(Display.I.Look);
        var pos = new Vector2(px + 160f * pw / 320, py + 190f * ph / frameH);
        Inject(new InputEventMouseButton { Pressed = true, ButtonIndex = up ? MouseButton.WheelUp : MouseButton.WheelDown, Position = pos });
    }
    public void ScriptPage(bool up) =>
        Inject(new InputEventKey { Pressed = true, PhysicalKeycode = up ? Key.Pageup : Key.Pagedown });
    static int ParseNum(string s) => s.StartsWith("0x") ? System.Convert.ToInt32(s[2..], 16) : int.Parse(s);

    /// <summary>Headless (HUNTER_STATS): the live things (slots 1.., the player is slot 0),
    /// for the frame-cost runs' count.</summary>
    public int LiveCount
    {
        get { var t = play.State.Entities; var k = 0; for (var n = 1; n < t.Count; n++) if (t.All[n].Alive) k++; return k; }
    }

    public override void _Input(InputEvent e)
    {
        if ((GetParent() as Main)?.ManualUp == true) return;    // the manual is up: the game does not run, it takes the input
        JoystickInput.Note(e);
        if (optionsB?.Up == true) return;                        // options design B takes the input (OptionsOverlay)
        if (relativeMouse && e is InputEventMouseMotion mm) relMove += mm.Relative;   // TrackMouse turns it into game pixels
        if (scripted && !injecting) return;   // scripted runs take the HUNTER_INPUT verbs; Inject's synthetic events come through here too
        // the demo: every key, down or up, is QuitKey ($B1E6 bmi -> $B20C); the mouse and the
        // joystick are not read at all ($C74C)
        if (play.State.DemoMode && e is InputEventKey { Echo: false }) { play.KeyDown(0); return; }
        if (play.State.DemoMode) return;
        if (e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.A } && MenuUp) { Click(mouseX, mouseY); return; }
        // ButtonsDown $AC74 reads $D4(A5) and keeps $12: bit 4 the left button, bit 1 the right one -
        // a click on the menus, the briefing, the soldier screen or an end screen is either button
        // (WaitClick $AC84). In play only the left one acts (Play.Click's fire).
        // $D4 & $12 held: WaitItemClick takes a press instead of hovering, and the map shows a filter
        // until both buttons are up again ($3D3C WaitButtonsUp, Play.ButtonUp)
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } held)
        {
            if (held.Pressed) play.ButtonDown = true;
            else if (!Input.IsMouseButtonPressed(MouseButton.Left) && !Input.IsMouseButtonPressed(MouseButton.Right)) play.ButtonUp();
        }
        if (e is InputEventMouseButton { Pressed: true } m &&
            (m.ButtonIndex == MouseButton.Left || MenuUp && m.ButtonIndex == MouseButton.Right))
        {
            // the mouse in play and on the screens: the window position as game pixels, both looks
            // (players = 2: of the half the mouse is over, GameViewSplit)
            var g = ToGame(GetViewport().GetMousePosition());
            Click((int)g.X, (int)g.Y);
            return;
        }
        // ENHANCEMENT (message_log): the wheel over the message window (game x 8-311, rows 181-198
        // - the panel's rows 41-58, the same mapping a click uses) and Page Up / Page Down in play
        // scroll the message log back and forward; drawing only (ScreenIndices' copy), the game's
        // bytes never move. Up / Page Up go back, a page is the window's 2 lines.
        if (Settings.MessageLog && play.Mode == PlayMode.Play)
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } w)
            {
                var wg = Display.I.ToGame(w.Position);   // the event's own position, the same field a real wheel event carries
                if (wg.X >= 8 && wg.X <= 311 && wg.Y >= 181 && wg.Y <= 198)
                {
                    play.ScrollMessageBack(w.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                    return;
                }
            }
            if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Pageup }) { play.ScrollMessageBack(2); return; }
            if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Pagedown }) { play.ScrollMessageBack(-2); return; }
        }
        // the keyboard interrupt hands each key down to KeyDown ($B214) on the menus too (the ports interrupt stays
        // on, $607A/$ABD0): play has stopped there (EndPlay, $24F2), so only 'p' acts - it toggles the pause bit,
        // which the save keeps and a resume or a load obeys. Only the name box ($98 bit 7) holds keys back.
        if (e is InputEventKey { Pressed: true, Echo: false } nk && diskStage == DiskStage.Name) { DiskKey(nk); return; }
        if (e is InputEventKey { Pressed: true, Echo: false } dk && settingsDebug) { DebugKey(dk); return; }   // the DEBUG page: the keys type the note (Esc alone is BACK), nothing else sees them
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F1 }) { ToggleHelp(); return; }  // F1 does nothing in the original's play
        // easy_saves: F5 quick saves to file 22, F4 loads it back (F9 is taken: pause on focus loss)
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F5 } && Settings.EasySaves) { QuickSave(); return; }
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F4 } && Settings.EasySaves) { QuickLoad(); return; }
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape } && settingsScreen is not null) { SettingsBack(); return; }   // settings: Esc = BACK
        if (QuickPickKey(e)) return;
        if ((brief is not null || ending is not null) && Bindings.Pressed(e) is { } early)
        {
            play.KeyDownBeforePlay((byte)early.Raw, (e as InputEventKey)?.ShiftPressed ?? false);   // only 'p' acts
            return;
        }
        if (Bindings.Pressed(e) is { } action)
        {
            // the physical key, so shift does not change the key code: input.md's table is the
            // unshifted codes and shift is passed on to Keys.Down (the +$70 table)
            var k = e as InputEventKey;
            play.KeyDown((byte)action.Raw, k?.ShiftPressed ?? false, k?.CtrlPressed ?? false);
        }
    }

    /// <summary>Hold the left stick click (or Tab) in play: the quick pick strip; let go to use the lit item.
    /// Only while the game is in play, not on a screen or a menu.</summary>
    bool QuickPickKey(InputEvent e)
    {
        var down = e is InputEventJoypadButton { ButtonIndex: JoyButton.LeftStick } or InputEventKey { PhysicalKeycode: Key.Tab, Echo: false };
        if (!down) return false;
        var pressed = e is InputEventJoypadButton jb ? jb.Pressed : ((InputEventKey)e).Pressed;
        if (pressed && !quickPick.Open && options is null && confirm is null && !DiskUp && brief is null && ending is null && play.Mode == PlayMode.Play)
            quickPick.Show();
        else if (!pressed && quickPick.Open && quickPick.Hide() is { } raw)
            play.KeyDown(raw);
        return true;
    }

    /// <summary>A key going down (raw Amiga code), as the keyboard interrupt delivers it.</summary>
    public void KeyDown(byte raw)
    {
        if (diskStage == DiskStage.Name) { if (Pregame.Key(typedName, raw, 0)) NameDone(); return; }   // the name box's keys
        play.KeyDown(raw);
    }

    /// <summary>A left click at game-picture (x, y): the mouse (Display.ToGame, both looks) and the
    /// headless script both come here. Clicks outside the 320 x 200 picture are ignored.</summary>
    public void Click(int x, int y)
    {
        if (optionsB?.Up == true) return;                         // options design B: its own clicks (OptionsOverlay)
        if (relativeMouse) (x, y) = (mouseX, mouseY);              // in play the press is where the game's pointer is
        mouseX = Math.Clamp(x, 0, 319);
        mouseY = Math.Clamp(y, 0, 199);
        if (x < 0 || x >= 320 || y < 0 || y >= 200) return;
        if (ending is not null) { endClick = true; return; }      // the end screens: one click each
        if (soldierScreen is not null) { soldierClick = true; return; }   // $875A's WaitClick $AC84
        if (brief is not null)                                    // the briefing's click ($AC84): the mission again
        {
            RebriefNow();
            return;
        }
        if (afterFadeOut is not null) return;                     // the menu is fading out ($1F98): no clicks
        if (DiskClick(x, y)) return;                              // the disk menu, slot menu, name box
        if (confirm is not null)                                  // "Are you sure": Yes / No ($21FA)
        {
            if (Menus.Hit([confirm.Yes, confirm.No], x, y) is { } b) FadeOutThen(() => ConfirmChosen(b.Handler));
            return;
        }
        if (settingsScreen is not null)                           // SETTINGS over the menu: the page's buttons
        {
            if (Menus.Hit(settingsButtons!, x, y) is { } b)
            {
                if (b.Handler == Menus.CancelHandler)
                    SettingsBack();                               // BACK: a page goes to the front page, the front page closes
                else if (settingsDebug)                           // the DEBUG page's own rows (KIND, SAVE NOTE)
                    DebugClick(b.Handler);
                else if (settingsGroup is null && b.Handler == settingsDebugRow)   // the front page's DEBUG row: its own page, not a group
                {
                    settingsDebug = true;
                    DrawDebugPage();
                }
                else if (settingsGroup is { } g)                  // a group page's row: cycle the value and save it
                {
                    var menu = new SettingsMenu(ui);
                    var key = menu.Page(g).Rows[b.Handler - 1].Key;
                    if (key != SettingsMenu.KeysKey && SettingsValue(key) != ThreeDOnlyWord)   // the KEYS row only points at F2; a 3D ONLY row waits for the 3D world
                        SettingsApply(key, SettingsMenu.Next(key, SettingsValue(key)));   // the setter saves the cfg
                    DrawSettingsPage();                           // the row shows its new value at once
                }
                else if (b.Handler >= 1)                          // the front page: the group's own page
                {
                    settingsGroup = new SettingsMenu(ui).Groups[b.Handler - 1];
                    DrawSettingsPage();
                }
            }
            return;                                               // the options screen stands under it, whole
        }
        if (options is not null)                                  // IN GAME OPTIONS: the four buttons
        {
            if (manualOption is { } mb && Menus.Hit([mb], x, y) is not null)
            {
                ManualRequested?.Invoke();                        // MANUAL over the menu: nothing moves, nothing fades
                return;
            }
            if (settingsOption is { } sb && Menus.Hit([sb], x, y) is not null)
            {
                OpenSettings();                                   // SETUP: the page over the menu, nothing fades either
                return;
            }
            if (Menus.Hit(options.Buttons.Select(b => b.Button), x, y) is { } b)
            {
                var result = options.Buttons.First(o => o.Button == b).Result;
                FadeOutThen(() => OptionsChosen(result));
            }
            return;
        }
        if (Aiming && mouseY < Play.PanelTop) return;                // AIMING: a click over the view is no panel button (the reticle, not the pointer, is there)
        // in play the pointer's y is the row within the panel ($E0), on the item table 92 rows
        // further down, on the map, log book and store the screen row (ClickY)
        play.Click(x, ClickY(y));
    }

    /// <summary>Headless verification (HUNTER_ITEMS): put count of an item in the pack, as picking
    /// it up would ($A2A4), so a script can reach a key or a screen a fresh game has no item for.</summary>
    /// <summary>ENHANCEMENT (the BOT, docs/bot-plan.md; HUNTER_BOT / run_game --bot): player 1's stick, keys and clicks from the
    /// Hunt brain, to watch it play in a window. This game only - a new game is the player's again.</summary>
    public void HandToBot()
    {
        var brain = new Hunt();
        if (System.Environment.GetEnvironmentVariable("HUNTER_ORDERS") is { Length: > 0 } script) { brain.Direct(script); GD.Print($"HUNTER: orders {script}"); }   // recording: the film's scene
        play.Pilot = new Bot(brain);
    }

    public void Give(int item, int count)
    {
        var s = play.State;
        var o = 0x28 + item * 32;
        s.Items[o] = (byte)(count >> 8); s.Items[o + 1] = (byte)count;
        Items.Carried(s);
        Vehicles.LoadWeapon(s, s.Entities.All[0]);
    }

    public override void _PhysicsProcess(double delta)
    {
        Frame();
        // a pass that opened a screen (the log book, the drone's view, the map) frees the captured mouse now, not at the
        // next frame's start: a click in between was read at the play pointer and missed (the agent playtest's first clicks)
        if (!scripted) PlayMouse();
    }

    /// <summary>One frame: the blank, then a pass of the main loop (or the screen that is up).</summary>
    public void Frame()
    {
        live?.Pump(LiveRun);                               // development only: HUNTER_LIVE's commands (GameView.Live.cs)
        LiveWatch();
        if (LiveFrozen()) return;
        if (!scripted) { TrackMouse(); Hover(); UpdateReticle(); }   // $C77C: the pointer moves every blank
        play.HasSecondView = false;                            // mp_views: true again only if his pass draws this frame
        var rgb = frame;
        var rgb2 = frame2;                                     // the right half's picture (players = 2)
        bool inPlay = false, covered = false;
        InPlay = false;
        if (real3dSprite is not null) real3dSprite.Visible = false;   // only play shows the 3D view
        if (real3d is not null) real3d.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;   // nor draws it unseen (Real3DFrame turns it back on)
        if (panelGpu is not null) panelGpu.Visible = false;           // (and the card's panel)
        if (mapMarks is not null) mapMarks.Visible = false;           // (and its overlay map)
        if (real3d is not null && !scripted)                         // NEW, not the original: Q / E turn the 3D view
        {                                                            // round the player (1.8 degrees a frame), Z back to north
            if (Input.IsKeyPressed(Key.Q)) real3d.Yaw += Mathf.Pi / 100;
            if (Input.IsKeyPressed(Key.E)) real3d.Yaw -= Mathf.Pi / 100;
            if (Input.IsKeyPressed(Key.Z)) real3d.Yaw = 0;
        }
        if (real3d is not null && Demo && Settings.DemoCamera)       // DEMO CAMERA: MOVING - the attract's camera turns by itself
        {
            var was = (demoCamera ??= new DemoCamera()).Playing;
            real3d.Yaw = (float)demoCamera.Yaw(play.State);
            if (demoCamera.Playing != was) GD.Print($"HUNTER: demo camera {demoCamera.Playing} at frame {Audio.Frame}");   // a run's log shows each shot
            real3d.CloseUp = play.State.InsideView ? 1 : (float)demoCamera.Scale;   // the car's close-up (indoors: the game's own camera)
        }
        else if (real3d is not null) real3d.CloseUp = 1;
        if (real3d is not null && spin3d != 0) real3d.Yaw += spin3d;  // recording only: HUNTER_SPIN turns the 3D view by itself
        if (real3d is not null && sway3d.Turn != 0)                   // recording only: HUNTER_SWAY, a slow swing to each side and back
            real3d.Yaw = sway3d.Turn * Mathf.Sin(Mathf.Tau * play.State.BlankCount / (50f * sway3d.Seconds));
        if (real3d is not null && play.Pilot is Bot bot)             // the bot turns the view itself (Hunt.TurnView): the 3D view follows it,
        {                                                            // by its unrounded angle when that is the game's (the byte steps 1, 2, 1, 1, 2: a stutter)
            var fine = bot.Brain is Hunt hunt && (byte)((int)Math.Round(hunt.View) & 0xFF) == play.State.ViewAngle1 ? hunt.View : play.State.ViewAngle1;
            real3d.Yaw = (float)(-fine / 256 * Mathf.Tau);
        }
        else play.State.ViewAngle1 = real3d is not null && !scripted && !Demo && play.Mode == PlayMode.Play   // his stick follows the turned view (live play only:
            ? (byte)Math.Round(-real3d.Yaw / (2 * Mathf.Pi) * 256) : (byte)0;                    // the demo's recorded input and the showcase's spin stay as they are)
        if (ending is not null) EndFrame(rgb);
        else if (soldierScreen is not null) SoldierFrame(rgb);
        else if (brief is not null) BriefFrame(rgb);
        else if (options is not null || confirm is not null || DiskUp)
        {
            ScreenFrame(rgb);
        }
        else
        {
            var j = LiveJoy() ?? (scripted ? (Autopilot() ?? scriptJoy) : JoystickInput.Read());
            var j2 = scripted ? default : JoystickInput.Read2();          // mp_two_soldiers: player 2's own keys (the script drives player 1 only)
            var (zoomOut, zoomIn) = scripted ? (scriptZoomOut, scriptZoomIn) : JoystickInput.Zoom();
            var lean = scripted ? 0 : JoystickInput.Lean();            // STICK STEERING: the pad stick's sideways lean in 16ths (the rules ignore it unless the setting is on)
            if (quickPick.Open) { j = default; lean = 0; (zoomOut, zoomIn) = (false, false); }   // choosing: the soldier stands still
            try
            {
                var held = SlowmoHold();                                 // HUNTER_SLOWMO: a picture between two blanks, no blank, no pass
                if (!held) play.VerticalBlank(j, zoomOut, zoomIn, j2, lean);
                // a screen (map, log book, item table: rules/Screens.cs) waits for its click
                // (Play.Click); no pass runs meanwhile
                if (!held && play.Mode == PlayMode.Play && !play.Waiting && !play.Paused && !play.Quit)   // Waiting: a visit's fade to black ($4758); Paused: PauseLoop $34E2 runs no pass; Quit: MainLoop has left ($2586) - a pass after StopPlay's fade spawned the grid round the camera into the freed player slot
                {
                    play.Update();
                    if (traceDemo && play.State.BlankCount % 100 < 7)             // HUNTER_TRACE_DEMO: the man's state about every 100 blanks
                    {
                        var p = play.State.Entities.All[0];
                        GD.Print($"HUNTER: trace blanks {play.State.BlankCount}: at {p.UW(0x4A)},{p.UW(0x4E)} type {p.W(4):X2} inside {play.State.InsideView} energy {play.State.Energy} paint {play.PaintView}");
                    }
                    // split-draw (rules/Play.cs PaintView, docs/split-draw.md): the card draws this
                    // pass's view, so the flat painting is skipped and the pass walks its game work
                    // alone - indoors too (the card draws the room) - not while the showcase opens on
                    // the flat picture.
                    play.PaintView = !(real3d is not null && (cinema is not null || !showcase)) && !noPaint;   // cinema too: the card draws every pass, never the flat picture
                    play.Draw();
                    play.SecondView = split;                              // lead: the game never switched it on (the tests did)
                    play.State.ViewAngle2 = split ? (byte)0x80 : (byte)0;   // mp_view_finish: the seams at $80 are fixed - player 2's own camera faces south - his half of a split screen is his view, not a clone of player 1's
                    play.DrawSecondView();                                // ENHANCEMENT (mp_views): player 2's own picture (players = 2 only)
                    if (Hunter.Settings.SoundPosition && Audio.Instance is { } audio)
                        for (var ch = 0; ch < 4; ch++)
                        {
                            // sound_position: the owner's Persp screen x (Objects.cs $613) over the
                            // 320-wide screen; player (owner 0) and behind-camera owners centre.
                            var owner = play.State.Channels.Owner(ch);
                            float pan = 0;
                            if (owner != 0 && play.State.Entities.All[owner / EntityTable.Size] is { Alive: true } e)
                            {
                                var zz = (short)(e.W(0x5A) + 0x100);
                                if (zz > 0)
                                {
                                    // as Objects.Persp: x << 8 kept 32-bit, divs.w's quotient is the LOW word
                                    var sx = (short)M68.Divs(e.W(0x56) << 8, zz) + play.State.Camera.CentreX;
                                    pan = Math.Clamp((sx - play.State.Camera.CentreX) / (float)play.State.Camera.CentreX, -1f, 1f);
                                }
                            }
                            audio.SetPan(ch, pan);
                        }
                }
                play.SmoothPicture();                                    // with the card: only the in-between camera (it reads the records itself)
                if (real3d is not null) Real3DFrame();                   // once a frame (a second call here doubled the pieces' clock): the camera moves smoothly
            }
            catch (System.NotSupportedException e)
            {
                // A path of the original not ported yet (later milestones): say so once, keep running.
                if (unported.Add(e.Message)) GD.PushWarning("HUNTER: not ported yet: " + e.Message);
            }
            frames++;
            if (Log)
            {
                var p = play.State.Entities.All[0];
                if (p.W(4) != lastType || frames % 50 == 0)
                    GD.Print($"HUNTER frame {frames}: player type {p.W(4):X2} at {p.UW(0x4A):X4},{p.W(0x4C)},{p.UW(0x4E):X4} speed {p.W(0x3E)} fuel {p.W(0x34)}");
                lastType = p.W(4);
            }
            if (play.Quit && !quitHandled) { quitHandled = true; quitting = true; PlaytestNode()?.Hold(); if (!scripted) pausedShot = GetViewport().GetTexture().GetImage(); play.BeginEndPlay(); }   // Esc or the panel's QUIT ($2586): the fade-out's blanks run as frames; Hold keeps this moment for the DEBUG note - the picture is still play, the save works (no menu up yet)
            else if (quitting && !play.Ending) { quitting = false; Quit(); }
            // the card's view and panel cover the whole screen, one player: the processor's picture would only sit under them
            covered = real3dSprite is { Visible: true } && (cinema is not null || panelGpu is { Visible: true }) && rgb2 is null;   // cinema: the 3D view alone covers the whole screen (no panel)
            if (!covered) PlayFrame(rgb);
            inPlay = true;
            InPlay = true;
            if (showcase && real3d is not null) ShowcaseStep();
            else if (cinema is not null && real3d is not null) CinemaStep();   // HUNTER_CINEMA: the same place in the frame, its own timing
            else if (missileRide > 0 && real3d is not null) MissileRideStep();     // HUNTER_MISSILE_RIDE
        }
        // mp_views: the right half - player 2's own picture when his pass drew this frame,
        // otherwise the same screen as the left. A screen that belongs to no player (the
        // briefing, the soldier screen, the options, the ending - the original showed them
        // whole) is a plain copy, so neither half goes stale or black.
        if (rgb2 is { } r2)
        {
            if (inPlay) FillSecondFrame(r2, rgb);
            else Array.Copy(rgb, r2, rgb.Length);
        }
        if (covered) return;
        image.SetData(320, 200, false, Image.Format.Rgb8, rgb);
        texture.Update(image);
        if (rgb2 is { } second && secondImage is { } si && secondTexture is { } st)
        {
            si.SetData(320, 200, false, Image.Format.Rgb8, second);
            st.Update(si);
        }
    }

    /// <summary>The right half's picture (mp_views): player 2's own screen when his pass drew
    /// this frame, a copy of player 1's picture otherwise (a screen is up, a visit's wait) - no
    /// stale half. The pointer stays out of it: clicking into a half is a later piece.</summary>
    void FillSecondFrame(byte[] rgb2, byte[] rgb)
    {
        if (!play.HasSecondView) { Array.Copy(rgb, rgb2, rgb.Length); return; }
        var screen = play.Screen2;
        for (var p = 0; p < screen.Length; p++)
        {
            var c = screen[p];
            rgb2[3 * p] = (byte)((c >> 8 & 15) * 17);
            rgb2[3 * p + 1] = (byte)((c >> 4 & 15) * 17);
            rgb2[3 * p + 2] = (byte)((c & 15) * 17);
        }
    }

    /// <summary>The game's picture (Play.Screen: the view, the panel, or the screen that is up),
    /// with the mouse pointer over it - the original moves pointer sprite 0 every blank, in play too.</summary>
    void PlayFrame(byte[] rgb)
    {
        var screen = play.Screen();
        for (var p = 0; p < screen.Length; p++)
        {
            var c = screen[p];
            rgb[3 * p] = (byte)((c >> 8 & 15) * 17);
            rgb[3 * p + 1] = (byte)((c >> 4 & 15) * 17);
            rgb[3 * p + 2] = (byte)((c & 15) * 17);
        }
        // in play the pointer moves over the panel only ($E0 = the row within the panel, $60CE), so
        // its screen row is 140 + the panel row (a mouse above the border sits at the panel's top,
        // which is also what opens the item table). On a screen showing it follows the mouse.
        var py = PointerRowDrawn();
        if (!quickPick.Open) Pointer.Draw(rgb, pointer, pointerColour, mouseX, py);   // hidden while the quick pick is up
    }

    /// <summary>The in-game options screen / the confirm: the menu picture in the menus' palette
    /// bank 5, faded in ($2164 ShowMenuWaitFadeRun), with the pointer over it.</summary>
    void ScreenFrame(byte[] rgb)
    {
        screenFade?.Tick();
        if (afterFadeOut is not null && screenFade?.Busy != true)       // the fade-out is over: the handler runs
        {
            var run = afterFadeOut;
            afterFadeOut = null;
            run();
            if (diskScreen is null && confirm is null && options is null) return;   // back in play
        }
        OptionsB();
        if (diskStage == DiskStage.Name) NameBoxFrame();
        if (settingsDebug) DebugPageFrame();                      // the DEBUG page: the note's cursor blinks, as the name box's
        var screen = settingsScreen ?? diskScreen ?? confirm?.Screen ?? options!.Screen;
        DrawScreen(rgb, screen, i => screenFade?.Colour(i) ?? data.PaletteBanks[5][i]);
        Pointer.Draw(rgb, pointer, pointerColour, mouseX, mouseY);
    }

    /// <summary>Options design B: up whenever the options screen itself is the one showing (after the quit, or back from
    /// Quit Game's "No" or the disk menu), not while a choice's fade-out runs.</summary>
    void OptionsB()
    {
        var want = !scripted && options is not null && confirm is null && !DiskUp && settingsScreen is null && brief is null && afterFadeOut is null;
        if (!want || optionsB?.Up == true) return;
        if (optionsB is null)
        {
            optionsB = new OptionsOverlay { Blocked = () => (GetParent() as Main)?.ManualUp == true };
            AddChild(optionsB);
            optionsB.Chosen += OptionsBChosen;
        }
        optionsB.Open(pausedShot, new OptionsPanel(new SettingsMenu(ui), SettingsValue, Settings.Renderer == "3d"),
            options!.Screen, data.PaletteBanks[5], ui, pointer, pointerColour);
    }

    /// <summary>What design B's menu chose: a setting (applied and saved at once, as the settings pages do), the key
    /// page, or one of the original's four choices - run by the original's own handlers after its fade-out
    /// ($1F98), the menu covering that fade and closing as the handler runs.</summary>
    void OptionsBChosen(object answer)
    {
        if (answer is not OptionsPanel.Result r) return;
        if (afterFadeOut is not null) return;                    // a choice is already fading out
        switch (r.Kind)
        {
            case OptionsPanel.Outcome.Changed:
                SettingsApply(r.Key!, r.Value!);
                break;
            case OptionsPanel.Outcome.OpenKeys:
                (GetParent() as Main)?.ToggleRebind();
                break;
            default:
                var result = r.Kind switch
                {
                    OptionsPanel.Outcome.Resume => AfterQuit.Resume,
                    OptionsPanel.Outcome.Rebrief => AfterQuit.Rebrief,
                    OptionsPanel.Outcome.DiskOperation => AfterQuit.DiskOperation,
                    _ => AfterQuit.QuitGame,
                };
                FadeOutThen(() => { optionsB?.Close(); OptionsChosen(result); });
                break;
        }
    }

    /// <summary>A 320 x 200 indexed screen through 16 colours (a fade's or a bank's).</summary>
    void DrawScreen(byte[] rgb, IndexedScreen screen, System.Func<int, ushort> colour)
    {
        var lut = screenLut;
        for (var i = 0; i < 16; i++)
        {
            var c = colour(i);
            lut[3 * i] = (byte)((c >> 8 & 15) * 17);
            lut[3 * i + 1] = (byte)((c >> 4 & 15) * 17);
            lut[3 * i + 2] = (byte)((c & 15) * 17);
        }
        for (var p = 0; p < screen.Pixels.Length; p++)
        {
            var c = 3 * (screen.Pixels[p] & 15);
            rgb[3 * p] = lut[c]; rgb[3 * p + 1] = lut[c + 1]; rgb[3 * p + 2] = lut[c + 2];
        }
    }

    /// <summary>A menu-like screen is up: the options screen, the confirm, the disk menus, a briefing or
    /// the end screens. There the pad's sticks move the pointer and A clicks.</summary>
    bool padPointer;
    Vector2 lastOs;
    bool MenuUp => options is not null || confirm is not null || DiskUp || brief is not null || ending is not null || soldierScreen is not null;

    // the play pointer moves by the mouse's MOVEMENT, as the original's does (port gap, playtest note 5)
    bool relativeMouse;
    Vector2 relMove;

    /// <summary>In play the original's pointer is relative: each blank adds the mouse counters' change to $DE/$E0 and
    /// clamps to the panel ($C794-$C7D8, mode 3's limits); entering play it keeps the menus' row, clamped, + 2
    /// ($250C, Play.cs). An absolute Windows cursor over the 3D view read as the panel's top row and opened the item
    /// table at once ($34C0). So while play runs the window holds the cursor (captured) and the pointer moves by the
    /// movement, in game pixels; leaving play puts the Windows cursor where the game's pointer is.</summary>
    /// <summary>The screen row the pointer is drawn at: in play the panel's (140 + its row), on a screen the mouse's.
    /// ENHANCEMENT (owner 2026-11-01): in the attract demo it is drawn at the screen's bottom edge, out of the way - the
    /// picture only, the game's own pointer is untouched.</summary>
    int PointerRowDrawn() => play.Mode != PlayMode.Play ? mouseY : Demo ? 199 : Math.Max(mouseY, Play.PanelTop);

    bool PlayMouse()
    {
        var want = play.Mode == PlayMode.Play && !MenuUp && !scripted && !split && GetWindow().HasFocus();
        if (want != relativeMouse)
        {
            relativeMouse = want;
            relMove = Vector2.Zero;
            if (want)
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
                mouseX = play.State.PointerX;
                mouseY = Play.PanelTop + play.State.PointerY;
            }
            else
            {
                Input.MouseMode = Input.MouseModeEnum.Hidden;
                var o = Display.I.ToGame(Vector2.Zero);
                var s = Display.I.ToGame(Vector2.One) - o;                      // game pixels per window pixel
                Input.WarpMouse(new Vector2((mouseX - o.X) / s.X, (mouseY - o.Y) / s.Y));
            }
        }
        if (!relativeMouse) return false;
        var g = Display.I.ToGame(Vector2.One) - Display.I.ToGame(Vector2.Zero);
        var d = new Vector2(relMove.X * g.X, relMove.Y * g.Y);
        var dx = (int)d.X; var dy = (int)d.Y;                                   // whole game pixels; the rest carries over
        relMove -= new Vector2(dx / g.X, dy / g.Y);
        if (Aiming && Input.GetConnectedJoypads().Count > 0)                    // AIMING: the right stick moves the reticle too (4 game pixels a blank at full lean)
        {
            float ax = Input.GetJoyAxis(0, JoyAxis.RightX), ay = Input.GetJoyAxis(0, JoyAxis.RightY);
            if (Mathf.Abs(ax) > 0.25f || Mathf.Abs(ay) > 0.25f) { dx += (int)Math.Round(ax * 4); dy += (int)Math.Round(ay * 4); }
        }
        mouseX = Math.Clamp(mouseX + dx, 0, 319);
        mouseY = Math.Clamp(mouseY + dy, Aiming ? 0 : Play.PanelTop, 199);   // AIMING: the pointer goes up over the view as the reticle, down on the panel as ever
        return true;
    }

    int reticleX = -1, reticleY;
    /// <summary>AIMING: RETICLE - the mouse over the view (or the right stick's move of it) is the reticle: handed to the rules each blank and
    /// drawn as a cross. Down on the panel it keeps its last place, the panel's pointer takes the mouse. Off or no play: none.</summary>
    void UpdateReticle()
    {
        if (!Aiming || quickPick.Open) { reticleX = -1; play.SetReticle(-1, 0); reticle.Hide(); return; }
        if (mouseY < Play.PanelTop - 1) { reticleX = mouseX; reticleY = mouseY; }
        if (reticleX < 0) { play.SetReticle(-1, 0); reticle.Hide(); return; }
        play.SetReticle(reticleX, reticleY);
        reticle.Place(reticleX, reticleY, pointerColour);
    }

    void TrackMouse()
    {
        if (PlayMouse()) return;
        if (MenuUp && Input.GetConnectedJoypads().Count > 0)
        {
            float ax = Input.GetJoyAxis(0, JoyAxis.LeftX) + Input.GetJoyAxis(0, JoyAxis.RightX),
                  ay = Input.GetJoyAxis(0, JoyAxis.LeftY) + Input.GetJoyAxis(0, JoyAxis.RightY);
            if (Mathf.Abs(ax) > 0.3f || Mathf.Abs(ay) > 0.3f)
            {
                mouseX = Math.Clamp(mouseX + (int)Math.Round(ax * 4), 0, 319);
                mouseY = Math.Clamp(mouseY + (int)Math.Round(ay * 4), 0, 199);
                padPointer = true;
                lastOs = GetViewport().GetMousePosition();
                return;
            }
        }
        var os = GetViewport().GetMousePosition();
        if (padPointer && os == lastOs) return;           // the pad moved the pointer; the real mouse has not
        padPointer = false;
        var p = ToGame(os);                                               // split: the half under the mouse
        mouseX = Math.Clamp((int)p.X, 0, 319);
        mouseY = Math.Clamp((int)p.Y, 0, 199);
    }

    /// <summary>The move without the press ($C77C): the game's pointer ($DE/$E0) follows the real
    /// mouse every frame, so hovering alone opens the item table at the panel's top, highlights
    /// the item under the pointer on the table and closes it below. The same y conversion as a
    /// click; the rules clamp to each screen's limits. While the options screen or the confirm
    /// shows the pointer is the shell's: their clicks hit the buttons by position, no $DE/$E0.</summary>
    void Hover()
    {
        if (options is not null || confirm is not null || DiskUp) return;
        play.Mouse(mouseX, ClickY(mouseY));
    }

    /// <summary>The y a click or a hover passes to the rules: in play the row within the panel
    /// ($E0 = screen row - 140, clamped to the panel's top - which is what opens the item
    /// table), on the item table 92 rows further down, elsewhere the screen row.</summary>
    int ClickY(int y) => play.Mode == PlayMode.Screen
        ? (play.Screens.Routine == ItemTableRoutine ? Math.Max(0, y - ItemTableTop) : y)
        : Aiming && y < Play.PanelTop ? 1                                     // AIMING: over the view the panel's pointer waits at row 1 (row 0 opens the item table)
        : Math.Max(0, y - Play.PanelTop);

    /// <summary>The quit's PlayGameEnd ($24D6: GameToSoldier, ResetChannels, StopPlay's 29-blank
    /// fade out - Play.BeginEndPlay, its blanks run as frames) is over: whatever $23D6-$23F0 picks: the debrief, the disk
    /// menu, or - the usual - the in-game options screen ($2164).</summary>
    void Quit()
    {
        if (play.State.DemoMode)   // $23D6 bmi $2402: rts to RunDemo
        {
            GD.Print($"HUNTER: demo over at frame {Audio.Frame}");   // a recording's end frame
            QuitToTitle?.Invoke(AfterGame.DemoOver);
            return;
        }
        options = play.InOptions();
        if (options is not null)
        {
            FadeIn();
            var added = options.Buttons.Select(o => o.Button).ToList();
            var menus = new Menus(ui, data.Directory);
            settingsOption = menus.SetupButton(options.Screen, added);   // the shell's SETUP button, always (the only way to the
            added.Add(settingsOption);                            // settings), drawn with the menus' own DrawMenuItem below the last
                                                                  // real one - and MANUAL, when wanted, below it
            manualOption = Settings.ManualButton
                ? menus.ManualButton(options.Screen, added)
                : null;
        }
        else if (play.EndGame() is { } end) StartEnd(end);       // the debrief's screens (Games.Debrief)
        else if (play.SaveRequested) OpenDisk();                 // SAVE+QUIT: straight to the disk menu
    }

    /// <summary>The end screens: each EndScreen $97E2 plays its song (d7), shows its message and
    /// the soldier panel, waits for a click and fades out ($9808 bra $6948); the last one's
    /// destination decides what comes after.</summary>
    void StartEnd(GameEnd end)
    {
        ending = end;
        endAt = 0;
        endPhase = 0;
        endFade = new Fade();
        EndBank();
        endFade.FadeTo(15);
        PlayEndSong();
    }

    /// <summary>The bank the shown end screen fades in through: the briefing screens' bank 6 ($8818's), or the
    /// epic picture's own colours (bank 4, $9750).</summary>
    void EndBank() =>
        endFade!.SetBank(ending!.Screens[Math.Min(endAt, ending.Screens.Count - 1)].Bank ?? data.PaletteBanks[6]);

    void PlayEndSong()
    {
        var song = ending!.Songs[Math.Min(endAt, ending.Songs.Count - 1)];
        if (song >= 0) Audio.Instance?.Music(song);              // -1: the epic picture keeps the song
    }

    void EndFrame(byte[] rgb)
    {
        endFade!.Tick();
        if (endPhase == 0 && endClick) { endClick = false; endPhase = 1; endFade.FadeTo(0); }
        else if (endPhase == 1 && !endFade.Busy)
        {
            endPhase = 0;
            if (++endAt >= ending!.Screens.Count) { FinishEnd(); return; }
            PlayEndSong();
            EndBank();
            endFade.FadeTo(15);
        }
        DrawScreen(rgb, ending!.Screens[Math.Min(endAt, ending.Screens.Count - 1)], endFade.Colour);
    }

    /// <summary>The screens are over, GameSequence's tail ($23AA -> $23BE StartAfterLoad):
    /// StopMusicAndCheck ($B00A) stops the end screens' song, then $23CA PreMissionSoldier $86F2
    /// - a usable record ($867E) goes straight to the next briefing, the shell's StartBriefing;
    /// a rejected one takes $23CE's bmi: first the soldier's own screen $875A, then $2404's
    /// title. Title ($2404) and Menus ($975C, or Quit Game's $9064) go back to the shell.</summary>
    void FinishEnd()
    {
        var after = ending!.After;
        ending = null;
        endFade = null;
        Audio.Instance?.Music(-1);                               // $23BE: bsr StopMusicAndCheck $B00A
        if (after == AfterGame.Briefing && play.State.Soldier.Valid) StartBriefing();   // $86F2: panel + briefing
        else if (after == AfterGame.Briefing) StartSoldierScreen();                     // $23CE bmi -> $875A first
        else QuitToTitle?.Invoke(after);                                                // $2404 / $975C / $9064
    }

    /// <summary>ViewSoldier $875A: SoldierScreenSetUp $8776 (the soldier panel, top row 0),
    /// FadeIn $8750, then one click ($AC84 WaitClick) and the fade to black $876C. After it
    /// $23CE's bmi goes to $2404 - the title, which Main shows.</summary>
    void StartSoldierScreen()
    {
        soldierScreen = new IndexedScreen();
        Pregame.SoldierScreen(soldierScreen, ui, data.Main, play.State.Soldier);        // $8776 + $8812
        soldierFade = new Fade();
        soldierFade.SetBank(data.PaletteBanks[6]);                                       // $8776/$8780
        soldierFade.FadeTo(15);                                                          // $8750 FadeIn
    }

    /// <summary>The soldier's own screen, faded in through bank 6, pointer over.</summary>
    void SoldierFrame(byte[] rgb)
    {
        soldierFade!.Tick();
        if (soldierClick) { soldierClick = false; soldierFade.FadeTo(0); }               // $AC84, then $876C
        else if (!soldierFade.Busy && soldierFade.Level == 0)
        {
            soldierScreen = null; soldierFade = null;
            QuitToTitle?.Invoke(AfterGame.Title);                                        // $23CE bmi -> $2404
            return;
        }
        DrawScreen(rgb, soldierScreen!, i => soldierFade.Colour(i));
        Pointer.Draw(rgb, pointer, pointerColour, mouseX, mouseY);
    }

    /// <summary>ShowMenuWaitFadeRun ($1F94): the menu picture, faded in from black over palette
    /// bank 5, waiting for a click.</summary>
    void FadeIn()
    {
        screenFade = new Fade();
        screenFade.SetBank(data.PaletteBanks[5]);
        screenFade.FadeTo(15);
    }

    /// <summary>A menu click ($1F98-$1F9C, every ShowMenuWaitFadeRun menu): the handler is pushed, the
    /// screen fades out to 0, then the handler runs.</summary>
    void FadeOutThen(Action run)
    {
        if (screenFade is null) { run(); return; }
        screenFade.FadeTo(0);
        afterFadeOut = run;
    }

    Action? afterFadeOut;

    /// <summary>SETUP: the settings screen's front page (the groups) over the options screen. The
    /// page draws onto its own screen (ScreenFrame picks it first), so the options screen's own
    /// pixels stay whole for the close. Nothing fades: the game is not running either way (the
    /// options screen's own freeze).</summary>
    void OpenSettings()
    {
        settingsGroup = null;
        DrawSettingsFront();
    }

    /// <summary>The group we are on; null = the front page.</summary>
    string? settingsGroup;

    /// <summary>The DEBUG page (the play-test note) is up: its own page over the options screen.</summary>
    bool settingsDebug;

    /// <summary>The front page's DEBUG row: its handler, one past the groups (so not a group
    /// itself); -1 = the row is not shown (no Playtest: not a play-test session).</summary>
    int settingsDebugRow = -1;

    /// <summary>The DEBUG page's kinds, in click order (owner 2026-10-29).</summary>
    static readonly string[] DebugKinds = ["BUG (GAMEPLAY)", "BUG (LOOKS)", "SOUND", "IDEA", "NOTE"];

    /// <summary>The words' length (keystrokes) and how many letters the page shows: the last part when long, all kept.</summary>
    const int WordsMax = 200, WordsShown = 24;

    string debugKind = DebugKinds[0];
    readonly Pregame.TypedLine debugWords = new() { Left = WordsMax };   // the name box's own line, empty
    string debugSaved = "";                                       // "SAVED AS MARK N", on the page after a save
    int debugFrames;                                              // the cursor's blink, as the name box's nameFrames

    void DrawSettingsFront()
    {
        settingsScreen = new IndexedScreen();
        var menu = new SettingsMenu(ui);
        var debug = PlaytestNode() is not null && options is not null;   // the DEBUG row: recording, and over the in-game options (Esc just held the moment)
        settingsDebugRow = debug ? menu.Groups.Count + 1 : -1;
        settingsButtons = menu.DrawFront(settingsScreen, debug ? "DEBUG" : null);
    }

    void DrawSettingsPage()
    {
        settingsScreen = new IndexedScreen();
        settingsButtons = new SettingsMenu(ui).Draw(settingsScreen, settingsGroup!, SettingsValue, inGame: true, threeD: Settings.Renderer == "3d");
    }

    /// <summary>The session's play-test recorder (Main.cs adds one on a real play: a window, or
    /// HUNTER_PLAYTEST) - the DEBUG row and its page show exactly when it is there.</summary>
    Playtest? PlaytestNode() => (GetParent() as Main)?.GetChildren().OfType<Playtest>().FirstOrDefault();

    /// <summary>The DEBUG page (the play-test note): KIND (a click cycles the five), NOTE with the
    /// name box's blinking cursor, SAVE NOTE, BACK - the settings pages' own layout (SettingsMenu.Place),
    /// so it reads like the other pages. SAVED AS MARK N sits under BACK once a note is saved.</summary>
    void DrawDebugPage()
    {
        settingsScreen = new IndexedScreen();
        DebugPageFrame();
    }

    /// <summary>The page once a frame while it is up, as the name box (NameBoxFrame): the words
    /// show their last part (all are kept), the cursor blinks every 4 frames.</summary>
    void DebugPageFrame()
    {
        debugFrames++;
        var words = System.Text.Encoding.ASCII.GetString(debugWords.ToArray());
        words = words.Length <= WordsShown ? words : words[^WordsShown..];
        var items = new System.Collections.Generic.List<(Ui.MenuItem Item, int Handler)>
        {
            (new Ui.MenuItem(0, 0, "DEBUG"), 0),
            (new Ui.MenuItem(0, 0, $"KIND: {debugKind}"), 1),
            (new Ui.MenuItem(0, 0, $"NOTE: {words}{(debugFrames / 4 % 2 == 0 ? "/" : " ")}"), 2),
            (new Ui.MenuItem(0, 0, "SAVE NOTE"), 3),
            (new Ui.MenuItem(0, 0, "BACK"), Menus.CancelHandler),
        };
        if (debugSaved.Length > 0) items.Add((new Ui.MenuItem(0, 0, debugSaved), 0));
        settingsButtons = new SettingsMenu(ui).Place(settingsScreen!, items);
    }

    /// <summary>The DEBUG page's keys: the name box's own typing ($B41A, Pregame.Key) - the words
    /// take every key down and nothing else sees it (the quick keys, the bindings, F1/F5/F4); Esc
    /// alone is BACK, as on the settings pages. Enter types nothing: SAVE NOTE is the button.</summary>
    void DebugKey(InputEventKey k)
    {
        if (k.PhysicalKeycode == Key.Escape) { SettingsBack(); return; }
        var key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
        var raw = key == Key.Backspace ? 0x41 : 0;                // $B41A's Backspace; Enter ($44) would end the name box's input
        var ascii = k.Unicode is > 0 and < 128 ? (int)k.Unicode : 0;
        Pregame.Key(debugWords, raw, ascii);                      // the return (the line full) only ends the name box's input
    }

    /// <summary>The DEBUG page's buttons: KIND cycles the five kinds; SAVE NOTE writes the held
    /// moment out (Playtest.SaveNote), the page says which mark and the words clear - a second
    /// note is a second mark of the same moment; NOTE itself is only the typed words.</summary>
    void DebugClick(int handler)
    {
        if (handler == 1)
        {
            debugKind = DebugKinds[(Array.IndexOf(DebugKinds, debugKind) + 1) % DebugKinds.Length];
            DrawDebugPage();
        }
        else if (handler == 3)
        {
            debugSaved = $"SAVED AS MARK {PlaytestNode()!.SaveNote(debugKind, System.Text.Encoding.ASCII.GetString(debugWords.ToArray()))}";
            debugWords.Clear(); debugWords.Left = WordsMax;       // the words clear, the moment stays held
            DrawDebugPage();
        }
    }

    /// <summary>BACK (the button, or Esc): a page (a group's, the DEBUG page) returns to the front
    /// page, the front page closes - and then the options screen is exactly as it was - nothing of
    /// it was redrawn while the pages were up.</summary>
    void SettingsBack()
    {
        if (settingsGroup is null && !settingsDebug)
            CloseSettings();
        else
        {
            settingsGroup = null;
            settingsDebug = false;
            DrawSettingsFront();
        }
    }

    /// <summary>A click's chosen display word, applied onto the shell's own Settings - whose
    /// setters save the cfg file as they set (godot/Settings.cs). The words are the rules table's
    /// (SettingsMenu.Values, which the click wrapped to); fullscreen applies at once (owner).</summary>
    void SettingsApply(string key, string label)
    {
        ApplySetting(key, label, play);
        if (key == "brightness") (GetParent() as Main)?.ApplyBrightness();   // the row says NOW: on screen at once, as F7 (it only saved, 11-11)
    }

    /// <summary>Recordings (HUNTER_RECORDED): every extra SettingsMenu.Recorded lists, set before anything starts - Main
    /// calls it ahead of the sound, whose extras (music in play, stereo, tails) are built once at launch.</summary>
    public static void ApplyRecorded()
    {
        foreach (var (key, word) in SettingsMenu.Recorded) ApplySetting(key, word, null);
        GD.Print("RECORDED SETTINGS " + string.Join(", ", SettingsMenu.Recorded.Select(r => $"{r.Key}={SettingsValue(r.Key)}")));
    }

    /// <summary>The first start's AS IT COULD BE (StartSequence): every extra SettingsMenu.AsItCouldBe lists, before
    /// the sound is built - as ApplyRecorded.</summary>
    /// <summary>The first start's tune screen (StartChoicePanel): a row's word, and setting it (no game runs yet).</summary>
    public static string SettingsValueOf(string key) => SettingsValue(key);
    public static void ApplyStartSetting(string key, string word) => ApplySetting(key, word, null);

    public static void ApplyAsItCouldBe()
    {
        foreach (var (key, word) in SettingsMenu.AsItCouldBe) ApplySetting(key, word, null);
    }

    /// <summary>The Windows test release's first start (export feature "modern_default", owner 2026-11-12: "Full modern
    /// default"): no question - AS IT COULD BE (the pixel size following the window, safest on another machine's card),
    /// with the sharks and subs whole too (left out of it only for the first start's box).</summary>
    public static void ApplyModernDefault()
    {
        ApplyAsItCouldBe();
        ApplySetting("sea_bodies", "WHOLE", null);
        GD.Print("MODERN DEFAULT " + string.Join(", ", SettingsMenu.AsItCouldBe.Keys.Append("sea_bodies").Select(k => $"{k}={SettingsValue(k)}")));
    }

    /// <summary>A row the 3D world draws, while it is off (SettingsMenu.ThreeDOnly).</summary>
    const string ThreeDOnlyWord = "3D ONLY";

    static void ApplySetting(string key, string label, Play? play)
    {
        if (SettingsMenu.Merged.ContainsKey(key))                    // a merged row (SettingsMenu.Merged): its step's parts, each as its own row was
        {
            foreach (var (part, word) in SettingsMenu.MergedParts(key, label)) ApplySetting(part, word, play);
            return;
        }
        switch (key)
        {
            case "look": Settings.Look = label.ToLowerInvariant(); break;
            case "brightness": Settings.Brightness = float.Parse(label, System.Globalization.CultureInfo.InvariantCulture); break;
            case "ground_look":                                                                              // read every frame (Real3DFrame)
                Settings.SunShading = label != "ORIGINAL";                                                  // a saved RELIEF / FULL (retired 11-01) reads as LIT
                break;
            case "ground_pattern": Settings.GroundPattern = label == "SPECKS"; break;                       // read every frame
            case "ground_colours": Settings.GroundColours = label == "BANDS"; break;                       // read every frame
            case "clouds": Settings.Clouds = label == "CLOUDS"; break;                                       // read every frame
            case "sky_look":
                Settings.SkySteps = Settings.SmoothLight = Settings.SkyTime = label == "SMOOTH";
                if (play is not null) { play.SkySteps = Settings.SkySteps; play.SmoothLight = Settings.SmoothLight; }   // handed to the RUNNING play (none yet: Recorded)
                break;
            case "sound_extras":
                Settings.SoundPosition = Settings.SoundTails = Settings.MusicInPlay = label == "ENHANCED";
                if (play is not null) Audio.Instance?.EnterPlay();                                           // the music in play now; the rest at the next start
                break;
            case "sound_voices": Settings.SoundVoices = Channels.SpareVoices = label == "MORE"; break;      // the next sound left out
            case "demo_camera": Settings.DemoCamera = label == "MOVING"; break;                              // read every frame of the demo
            case "demo_world": Settings.WildDemo = label == "WILD"; break;                                    // read as the next demo starts
            case "card_pieces": Settings.CardPieces = label != "ORIGINAL"; Settings.PiecesStay = label == "LASTING"; break;                            // read every frame
            case "own_colours": Settings.OwnColours = label == "AGA"; break;                                // read every frame
            case "glass": Settings.SeeThroughGlass = label == "SEE-THROUGH"; break;                         // read every frame
            case "sea_bodies": Settings.SeaBodies = label == "WHOLE"; break;
            case "smooth_animation":                                                                         // the next pass (no game yet: a recording's settings)
                Settings.SmoothAnimation = label == "SMOOTH";
                if (play is not null) play.SmoothAnimation = Settings.SmoothAnimation;
                break;
            case "ground_cuts": Settings.GroundCuts = label == "2X" ? 2 : label == "4X" ? 4 : label == "8X" ? 8 : label == "16X" ? 16 : label == "32X" ? 32 : 1; break;   // read every frame
            case "haze": Settings.Haze = label == "BANDS" ? 1 : label == "FINE" ? 2 : 0; break;             // read every frame
            case "living_sea": Settings.LivingSea = label != "ORIGINAL"; Settings.SeaCycling = label == "CYCLING"; break;                               // read every frame
            case "view_distance": Settings.ViewDistance = label == "ORIGINAL" ? 7 : label == "ALL" ? 0 : int.Parse(label); break;   // read every frame
            case "wide_view": Settings.WideView = label == "WIDE"; break;                                   // the next start lays the 3D view out (Restart)
            case "own_shadows": Settings.OwnShadows = label == "MORE"; if (play is not null) play.OwnShadows = Settings.OwnShadows; break;   // handed to the RUNNING play (Objects.cs:341; needs the sun - none at night/indoors)
            case "renderer": Settings.Renderer = label == "ON" ? "3d" : "software"; break;   // the next start builds the 3D view with it (Restart)
            case "gpu_scale": Settings.GpuScale = label switch { "1" => 1, "2" => 2, "4" => 4, _ => 0 }; break;   // FollowScale applies it
            case "fuller_world": Settings.FullerWorld = label == "SOME" ? 1 : label == "MORE" ? 2 : label == "LOTS" ? 3 : 0; break;
            case "more_objects": Settings.MoreObjects = label == "MORE"; break;
            case "finer_ground":                                                                          // read at a new game
                Settings.FinerGround = label == "ORIGINAL" ? 0 : 2;                                       // 2 points a cell at every word
                Settings.GroundRelief = label == "HILLS" ? 1 : label == "CLIFFS" ? 2 : 0;                 // RELIEF: 2X is today's gentle store
                break;
            case "ground_seed": Settings.GroundSeed = label == "RANDOM" ? "random" : label == "LAST" ? "last" : "world"; break;   // read at a new game
            case "living_world": Settings.LivingWorld = label == "ON"; break;            // read at the next game
            case "leave_behind": Settings.LeaveBehind = label == "BEHIND"; if (play is not null && !play.State.DemoMode) play.LeaveBehind = Settings.LeaveBehind; break;   // read at each exit; never into the attract demo
            case "analog_steering": Settings.AnalogSteering = label == "ANALOG"; if (play is not null && !play.State.DemoMode) play.AnalogSteering = Settings.AnalogSteering; break;   // input, not a look; never the attract demo
            case "aiming": Settings.AimReticle = label == "RETICLE"; if (play is not null && !play.State.DemoMode) play.AimReticle = Settings.AimReticle; break;   // input, not a look; never the attract demo
            case "bullet_box": Settings.BulletBox = label == "SMALL"; if (play is not null && !play.State.DemoMode) play.BulletBox = Settings.BulletBox; break;   // new bullets; never the attract demo
            case "a500_pace": Settings.A500Pace = label == "ON"; if (play is not null && !play.State.DemoMode) play.A500Pace = Settings.A500Pace; break;   // rules only; never the attract demo
            case "far_view": Settings.FarView = label == "FAR"; if (play is not null) play.FarView = Settings.FarView; break;               // handed to the RUNNING play: draw bounds only, nothing is sized by it
            case "graves": Settings.Graves = label != "NONE"; Settings.VultureGraves = label == "WITH VULTURES"; if (play is not null) { play.Graves = Settings.Graves; play.VultureGraves = Settings.VultureGraves; } break;   // read at the next paid death / new grave
            case "a500_filter": Settings.A500Filter = label == "A500"; Audio.ApplyFilter(Settings.A500Filter); break;  // the filter itself, at once - F6's own call (Main.cs:127)
            case "message_log": Settings.MessageLog = label == "ON"; if (play is not null) play.MessageLog = Settings.MessageLog; break;   // handed to the RUNNING play: lines from now on are kept
            case "pause_on_focus_loss": Settings.PauseOnFocusLoss = label == "ON"; break;
            case "manual_button": Settings.ManualButton = label == "ON"; break;
            case "quit_button": Settings.QuitButton = label == "ON"; break;
            case "fullscreen":                                        // applies at once (owner)
                Settings.Fullscreen = label == "ON";
                DisplayServer.WindowSetMode(label == "ON" ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
                break;
            case "text_size": Settings.TextSizeIndex = label == "LARGE" ? 1 : label == "EXTRA LARGE" ? 2 : 0; break;
            case "players": Settings.Players = label == "2" ? 2 : 1; break;
            case "split_layout": Settings.SplitLayout = label == "STACKED" ? "stacked" : "side"; break;
        }
    }

    /// <summary>BACK (the button, or Esc): the page goes and the options screen is exactly as it
    /// was - nothing of it was redrawn while the page was up.</summary>
    void CloseSettings()
    {
        settingsScreen = null;
        settingsButtons = null;
        settingsGroup = null;
        settingsDebug = false;
    }

    /// <summary>A row's key -> its display text, from the shell's own Settings (the rules carry
    /// only the keys). A click wraps the text to its next word (SettingsMenu.Next) and
    /// SettingsApply sets it back; the keys row ignores both.</summary>
    static string SettingsValue(string key) =>
        SettingsMenu.ThreeDOnly.Contains(key) && Settings.Renderer != "3d" ? ThreeDOnlyWord
        : SettingsMenu.Merged.ContainsKey(key) ? SettingsMenu.MergedWord(key, PartValue)   // the step its parts all match, or CUSTOM
        : PartValue(key);

    /// <summary>A setting's own word, the 3D ONLY mark aside (a merged row reads its parts with it).</summary>
    static string PartValue(string key) => key switch
    {
        "look" => Settings.Look.ToUpper(),
        "renderer" => Settings.Renderer == "3d" ? "ON" : "OFF",
        "gpu_scale" => Settings.GpuScale == 0 ? "AUTO" : Settings.GpuScale.ToString(),
        "brightness" => Settings.Brightness.ToString("0.0"),
        "wide_view" => Settings.WideView ? "WIDE" : "4:3",
        "ground_look" => !Settings.SunShading ? "ORIGINAL" : "LIT",
        "ground_pattern" => Settings.GroundPattern ? "SPECKS" : "ORIGINAL",
        "ground_colours" => Settings.GroundColours ? "BANDS" : "ORIGINAL",
        "clouds" => Settings.Clouds ? "CLOUDS" : "ORIGINAL",
        "sky_look" => Settings.SkySteps || Settings.SmoothLight || Settings.SkyTime ? "SMOOTH" : "ORIGINAL",   // any part on shows on (11-11: half on showed ORIGINAL); a click from it sets all off
        "sound_extras" => Settings.SoundPosition || Settings.SoundTails || Settings.MusicInPlay ? "ENHANCED" : "ORIGINAL",
        "sound_voices" => Settings.SoundVoices ? "MORE" : "ORIGINAL",
        "demo_camera" => Settings.DemoCamera ? "MOVING" : "ORIGINAL",
        "demo_world" => Settings.WildDemo ? "WILD" : "ORIGINAL",
        "card_pieces" => Settings.CardPieces ? (Settings.PiecesStay ? "LASTING" : "PIECES") : "ORIGINAL",
        "own_colours" => Settings.OwnColours ? "AGA" : "ORIGINAL",
        "glass" => Settings.SeeThroughGlass ? "SEE-THROUGH" : "ORIGINAL",
        "sea_bodies" => Settings.SeaBodies ? "WHOLE" : "ORIGINAL",
        "smooth_animation" => Settings.SmoothAnimation ? "SMOOTH" : "ORIGINAL",
        "ground_cuts" => Settings.GroundCuts switch { 2 => "2X", 4 => "4X", 8 => "8X", 16 => "16X", 32 => "32X", _ => "ORIGINAL" },
        "haze" => Settings.Haze switch { 1 => "BANDS", 2 => "FINE", _ => "OFF" },
        "living_sea" => Settings.LivingSea ? (Settings.SeaCycling ? "CYCLING" : "LIVING") : "ORIGINAL",
        "view_distance" => Settings.ViewDistance switch { 7 => "ORIGINAL", 0 => "ALL", var n => n.ToString() },
        "own_shadows" => Settings.OwnShadows ? "MORE" : "ORIGINAL",
        "fuller_world" => Settings.FullerWorld switch { 1 => "SOME", 2 => "MORE", 3 => "LOTS", _ => "ORIGINAL" },
        "more_objects" => Settings.MoreObjects ? "MORE" : "ORIGINAL",
        "finer_ground" => Settings.FinerGround == 0 ? "ORIGINAL" : Settings.GroundRelief switch { 1 => "HILLS", 2 => "CLIFFS", _ => "2X" },
        "ground_seed" => Settings.GroundSeed switch { "random" => "RANDOM", "last" => "LAST", _ => "WORLD" },
        "living_world" => Settings.LivingWorld ? "ON" : "ORIGINAL",
        "leave_behind" => Settings.LeaveBehind ? "BEHIND" : "ORIGINAL",
        "analog_steering" => Settings.AnalogSteering ? "ANALOG" : "ORIGINAL",
        "aiming" => Settings.AimReticle ? "RETICLE" : "ORIGINAL",
        "bullet_box" => Settings.BulletBox ? "SMALL" : "ORIGINAL",
        "a500_pace" => Settings.A500Pace ? "ON" : "OFF",
        "far_view" => Settings.FarView ? "FAR" : "ORIGINAL",
        "graves" => !Settings.Graves ? "NONE" : Settings.VultureGraves ? "WITH VULTURES" : "SHOWN",
        "a500_filter" => Settings.A500Filter ? "A500" : "A1200",
        "message_log" => Settings.MessageLog.OnOff(),
        "pause_on_focus_loss" => Settings.PauseOnFocusLoss.OnOff(),
        "manual_button" => Settings.ManualButton.OnOff(),
        "quit_button" => Settings.QuitButton.OnOff(),
        "fullscreen" => Settings.Fullscreen.OnOff(),
        "text_size" => Settings.TextSizeIndex switch { 1 => "LARGE", 2 => "EXTRA LARGE", _ => "NORMAL" },
        "players" => Settings.Players.ToString(),
        "split_layout" => Settings.SplitLayout.ToUpper(),
        _ => "?",
    };

    /// <summary>A click on one of the options screen's buttons ($2186: each entry's handler).</summary>
    void OptionsChosen(AfterQuit result)
    {
        switch (result)
        {
            case AfterQuit.Resume:                               // $23D2 ResumeAfterSave: PlayGame again
                // no ResetGameState, no briefing: the world, the clock and the soldier are exactly
                // where the quit left them (Play.Resume: PlayStart + SpawnPlayer's item move)
                play.Resume();
                options = null; confirm = null; screenFade = null; quitHandled = false;
                manualOption = null;
                CloseSettings(); settingsOption = null;
                break;
            case AfterQuit.Rebrief:                              // $23BE StartAfterLoad
                StartBriefing();                                 // $86F2: the soldier panel + briefing first
                break;
            case AfterQuit.QuitGame:                             // $23F6 RetireButton -> ConfirmMenu $21F2
                confirm = new Menus(ui, data.Directory).Confirm();
                FadeIn();
                break;
            default:                                             // Disk Operation ($1013E)
                OpenDisk();
                break;
        }
    }

    /// <summary>$23BE's PreMissionSoldier $86F2 for the soldier the game ended with: the soldier
    /// panel ($8818) and the briefing ($9488) StartSequence.Briefing shows a new game, bank 6,
    /// drawn whole here (the original types the objective a character a frame) and waiting for
    /// one click ($AC84). The click runs Play.Rebrief: the $23C6 add, ResetGameState, PlayGame.</summary>
    void StartBriefing()
    {
        var s = play.State;
        brief = new IndexedScreen();
        var name = Pregame.RecordName(s.Soldier);
        Pregame.SoldierPanel(brief, ui, data.Main, s.Soldier, name);                     // $8818
        Pregame.Briefing(brief, ui, data.Main, s.Soldier, name, (int)s.Mode, s.Items, null, s.Mission, s);   // $9068: the mission on; the kills so far ($9288)
        briefFade = new Fade();
        briefFade.SetBank(data.PaletteBanks[6]);                                         // $8776/$8780
        briefFade.FadeTo(15);                                                            // $8750 FadeIn
    }

    /// <summary>The briefing's screens, faded in through bank 6, pointer over.</summary>
    void BriefFrame(byte[] rgb)
    {
        briefFade!.Tick();
        DrawScreen(rgb, brief!, i => briefFade.Colour(i));
        Pointer.Draw(rgb, pointer, pointerColour, mouseX, mouseY);
    }

    /// <summary>The briefing's click ($AC84 -> $876C FadeToBlack): StopMusicAndCheck ($B00A),
    /// then Play.Rebrief - the $23C6 add, ResetGameState ($2414) and PlayGame ($24B4).</summary>
    void RebriefNow()
    {
        var wasPlaying = Audio.Instance?.MusicPlaying == true;
        Audio.Instance?.Music(-1);                           // $B00A: music off, d0 = 2 / -1
        brief = null; briefFade = null;
        if (!afterLoad) play.Rebrief(wasPlaying);
        afterLoad = false;
        options = null; confirm = null; screenFade = null; quitHandled = false;
        manualOption = null;
        CloseSettings(); settingsOption = null;
    }

    /// <summary>Quit Game's confirm: Yes ($9064, 0) ends the career; No ($9060, -1) back to the
    /// options screen ($23FA bmi -> $23E6).</summary>
    void ConfirmChosen(int handler)
    {
        confirm = null;
        if (handler != ConfirmScreen.YesHandler)
        {
            FadeIn();                                            // the options screen again
            return;
        }
        Games.Retire(play.State);                                 // $23F6/$8642: the soldier's status 5 (AWOL)
        options = null; screenFade = null;
        manualOption = null;
        CloseSettings(); settingsOption = null;
        QuitToTitle?.Invoke(AfterGame.Menus);                     // $9064's rts returns to TitleLoop $1F6C
    }

    public void Save(string path) => image.SavePng(path);

    /// <summary>HUNTER_REPLAY_CARRY (Replay.cs): the headless capture cannot click its way to a
    /// mission end, so it says when the passes are done and this runs the quit's tail (Quit's
    /// debrief branch) - the end screens, then FinishEnd's carry-over flow.</summary>
    public void ReplayEnd()
    {
        quitHandled = true;
        if (play.EndGame() is { } end) StartEnd(end);
    }

    /// <summary>HUNTER_REPLAY (Replay.cs): the play this replay drives, and the raw rendering of
    /// one pass - play.Screen() with the pointer over it, exactly what Frame shows in real play.</summary>
    public Play ReplayPlay => play;

    public void ReplayRender(byte[] rgb)
    {
        PlayFrame(rgb);
        image.SetData(320, 200, false, Image.Format.Rgb8, rgb);
        texture.Update(image);
    }
}

/// <summary>The settings rows' value words ("ON" / "OFF").</summary>
static class SettingsText
{
    public static string OnOff(this bool b) => b ? "ON" : "OFF";
}
