using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>Which way the sequence comes in ($1F26 boot, $2404 a mission failed on time, $1F6C
/// every other return to the shell).</summary>
public enum StartEntry
{
    /// <summary>$1F26: the title (50 frames, no click), the credits, the menus.</summary>
    Boot,
    /// <summary>$2404: the title over song 2, one click ($9758), a fade - then the menus, no credits.</summary>
    Title,
    /// <summary>$975C / $9064: straight into the menu loop $1F6C.</summary>
    Menus,
    /// <summary>STUNTIN': back from a league race or practice - the league screen, then the menus.</summary>
    League,
}

/// <summary>The original from switch-on to the start of play, frame by frame at 50 Hz
/// (re/formats/startup.md). The original's code runs straight through and waits inside its calls;
/// here that order is one iterator, and each <c>yield</c> is one frame.
/// Title (mode 1, its own palette, picture file 5) fades in ($1F4C/$6918), held 50 frames ($1F2E)
/// on boot but clicked away ($9758) when a mission failed on time ($2404), fades out; credits
/// ($213A, bank 5) held 100 frames on boot; then the main menu loop ($1F6C): View soldier ($875A),
/// Start new game ($2310 -> AVAILABLE GAMES -> $235E): name box ($B41A) for a new soldier,
/// briefing ($8818/$9488), then <see cref="GameChosen"/>.
/// Disk Operation ($1014A) runs the disk menu; Demo, or 1500 frames on the main menu with no click ($1F78), runs
/// the demo (RunDemo $22D6): the Action game with the default soldier, its briefing for 201 frames, then play.</summary>
public partial class StartSequence : Node2D
{
    /// <summary>The player has picked a game and read the briefing; the view starts.</summary>
    public event Action<GameMode>? GameChosen;
    /// <summary>The Stunt Car Racer track chosen on the game menu (-1: none - a game of its own world).</summary>
    public int ScrTrack { get; private set; } = -1;
    /// <summary>Shai Hulud was chosen on the game menu: the sand island.</summary>
    public bool SandIsland { get; private set; }

    /// <summary>A click on the main menu's MANUAL button (a shell extra, Settings.ManualButton):
    /// Main opens the manual over the menu - nothing here moves, and when it closes the menu is
    /// exactly where it was.</summary>
    public event Action? ManualRequested;

    /// <summary>A saved game the disk menu loaded ($101F0): GameChosen then starts it, at its briefing
    /// ($23BE), instead of a new game.</summary>
    public WorldState? Loaded { get; private set; }

    /// <summary>GameChosen starts the demo (RunDemo $22D6): the default soldier, the run's record kept aside.</summary>
    public bool Demo { get; private set; }

    /// <summary>A recording (HUNTER_RECORDED, tools/run_game.py --recorded): the main menu goes straight to the demo.</summary>
    static readonly bool Recording = System.Environment.GetEnvironmentVariable("HUNTER_RECORDED") is not null;
    Menus.Button? picked;

    readonly IndexedScreen screen = new();
    readonly Fade fade = new();
    readonly Queue<(int Raw, int Ascii)> keys = new();
    Ui ui = null!;
    Menus menus = null!;
    GameData data = null!;
    Soldier soldier = null!;
    IEnumerator<string> run = null!;
    Image image = null!;
    ImageTexture texture = null!;
    readonly byte[] rgb = new byte[320 * 200 * 3], lut = new byte[16 * 3];   // made once, filled each frame
    ushort pointerColour;
    byte[] pointer = null!;
    int mouseX = 160, mouseY = 100;
    bool button, scripted;
    int buttonHold;
    bool padPointer;
    Vector2 lastOs;

    /// <summary>What the sequence is showing (verification).</summary>
    public string Current { get; private set; } = "";

    /// <summary>The soldier record this run of the shell shows and starts games with ($85E2): a
    /// kept one from the game that just ended, or a fresh one at boot. StartSequence never writes
    /// to it except the name box and $8666's default copy - the game owns it in between.</summary>
    public Soldier Soldier => soldier;
    StartEntry entry = StartEntry.Boot;

    /// <summary>The first start's question was answered (its settings set): Main builds the sound now.</summary>
    public event Action? StartAnswered;
    bool askStart;

    public void Init(GameData d, bool scriptedInput = false, Soldier? kept = null, StartEntry entry = StartEntry.Boot, bool askStart = false)
    {
        data = d;
        scripted = scriptedInput;
        this.entry = entry;
        this.askStart = askStart;
        ui = Ui.Load(d.Directory);
        menus = new Menus(ui, d.Directory);
        soldier = kept ?? new Soldier(d.Main);          // $85E2: fresh only at boot, kept across ended games
        pointer = Hunter.Pointer.Sprite(d.Main);
        pointerColour = d.PaletteBanks[1][17];          // $610C loads all 32 registers from bank 1 ($64E0); fades touch 0-15 only
        image = Image.CreateEmpty(320, 200, false, Image.Format.Rgb8);
        texture = ImageTexture.CreateFromImage(image);
        AddChild(new Sprite2D { Texture = texture, Centered = false, TextureFilter = TextureFilterEnum.Nearest });
        if (!scripted) Input.MouseMode = Input.MouseModeEnum.Hidden;
        run = Sequence().GetEnumerator();
    }

    // ---- input (the player's, or a script's when capturing headless) ----

    /// <summary>Press the left button at (x, y) and hold it 3 frames.</summary>
    public void Click(int x, int y) { mouseX = x; mouseY = y; button = true; buttonHold = 3; }

    /// <summary>A key: raw Amiga code ($44 Return, $41 Backspace, else -1) and its character.</summary>
    public void Key(int raw, int ascii) => keys.Enqueue((raw, ascii));

    /// <summary>'p' on the briefing toggled the pause bit: GameSequence cleared $99 at $23B6 just before, and the
    /// keyboard interrupt's KeyDown ($B258) still toggles bit 5 outside the name box - the game starts paused.</summary>
    public bool PausePressed { get; private set; }

    public override void _Input(InputEvent e)
    {
        if ((GetParent() as Main)?.ManualUp == true) return;                    // the manual is up: it takes the input
        if (scripted) return;
        if (Demo) return;                                                       // $B1E6: a key is QuitKey, and PlayGame clears it ($24BE sf $A5)
        if (e is InputEventKey { Pressed: true, Echo: false } pk && pk.Unicode == 'p' && Current == "briefing") PausePressed = !PausePressed;
        // ButtonsDown $AC74 keeps $12 of $D4(A5): bit 4 the left button, bit 1 the right one -
        // the menus and their screens take either
        if (e is InputEventMouseButton mb && (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right)) button = mb.Pressed;
        if (e is InputEventJoypadButton { ButtonIndex: JoyButton.A } jb) button = jb.Pressed;        // a pad's A = the button
        if (e is InputEventKey { Pressed: true, Echo: false } k)
            Key(k.Keycode switch { Godot.Key.Enter => 0x44, Godot.Key.Backspace => 0x41, _ => -1 }, (int)k.Unicode);
    }

    void ReadMouse()
    {
        if (scripted)
        {
            if (buttonHold > 0 && --buttonHold == 0) button = false;
            return;
        }
        if (Input.GetConnectedJoypads().Count > 0)                              // a pad's sticks move the pointer
        {
            float ax = Input.GetJoyAxis(0, JoyAxis.LeftX) + Input.GetJoyAxis(0, JoyAxis.RightX),
                  ay = Input.GetJoyAxis(0, JoyAxis.LeftY) + Input.GetJoyAxis(0, JoyAxis.RightY);
            if (Mathf.Abs(ax) > 0.3f || Mathf.Abs(ay) > 0.3f)
            {
                mouseX = Math.Clamp(mouseX + (int)Math.Round(ax * 4), 0, 320);
                mouseY = Math.Clamp(mouseY + (int)Math.Round(ay * 4), 0, 200);
                padPointer = true;
                lastOs = GetViewport().GetMousePosition();
                return;
            }
        }
        var os = GetViewport().GetMousePosition();
        if (padPointer && os == lastOs) return;                                 // the real mouse has not moved
        padPointer = false;
        var p = Display.I.ToGame(os);                                           // game 320 x 200 in either look
        mouseX = Math.Clamp((int)p.X, 0, 320);                                  // $C7AE: 0..$EA, 0..$EC
        mouseY = Math.Clamp((int)p.Y, 0, 200);
    }

    /// <summary>The title picture (loadpic1, its own palette) as a 320 x 200 image: what the first start's question
    /// stands over (the overlay dims it).</summary>
    Image? TitlePicture()
    {
        var path = Path.Combine(data.Directory, "pics", "loadpic1.json");
        if (!File.Exists(path)) return null;
        using var pic = JsonDocument.Parse(File.ReadAllText(path));
        var idx = Convert.FromHexString(pic.RootElement.GetProperty("pixels_hex").GetString()!);
        var pal = pic.RootElement.GetProperty("palette12").EnumerateArray().Select(c => c.GetInt32()).ToArray();
        var rgb = new byte[320 * 200 * 3];
        for (var i = 0; i < 320 * 200; i++)
        {
            var c = pal[idx[i] % pal.Length];
            rgb[3 * i] = (byte)((c >> 8 & 15) * 17); rgb[3 * i + 1] = (byte)((c >> 4 & 15) * 17); rgb[3 * i + 2] = (byte)((c & 15) * 17);
        }
        return Image.CreateFromData(320, 200, false, Image.Format.Rgb8, rgb);
    }

    // ---- the original's order ----

    IEnumerable<string> Sequence()
    {
        // a new player's first start (Settings.FirstStart): AS IT (KIND OF) WAS or AS IT COULD BE,
        // asked once before anything else - the sound is built after it, so its extras are in from the first note
        if (askStart)
        {
            var chosen = 0;
            var tuned = false;                                                  // AS IT COULD BE's extras already on (the tune screen)
            if (scripted)                                                       // scripted runs click the old page's buttons
                foreach (var f in Menu(new SettingsMenu(ui).DrawStartChoice(screen), "start_choice", h => chosen = h)) yield return f;
            else
            {
                // options design B, part 2: the question as B's menu - the answers on the left, what each gives beside
                // them; AS IT COULD BE then opens its own settings (the pages as the left column, Start at the foot) -
                // over the title picture, dimmed (OptionsOverlay, StartChoicePanel)
                var ask = new OptionsOverlay();
                AddChild(ask);
                ask.Chosen += a =>
                {
                    switch (a)
                    {
                        case int n: chosen = n; break;
                        case StartChoicePanel.Tune: GameView.ApplyAsItCouldBe(); tuned = true; break;   // its extras on now: the pages show them
                        case OptionsPanel.Result { Kind: OptionsPanel.Outcome.Changed } r: GameView.ApplyStartSetting(r.Key!, r.Value!); break;
                        case OptionsPanel.Result { Kind: OptionsPanel.Outcome.OpenKeys }: (GetParent() as Main)?.ToggleRebind(); break;
                    }
                };
                ask.Open(TitlePicture(), new StartChoicePanel(new SettingsMenu(ui), GameView.SettingsValueOf), menus.Options().Screen,
                    data.PaletteBanks[5], ui, pointer, pointerColour, dimming: 0.8f);   // the title is brighter than play: dimmed more
                while (chosen == 0) yield return "start_choice";
                if (chosen == 1 && tuned)                                       // back from the tune screen to AS IT (KIND OF) WAS:
                    foreach (var row in StartChoicePanel.Decided)                // what it turned on goes back to the original's
                        GameView.ApplyStartSetting(row.Key, StartChoicePanel.ValueUnder(0, row.Key));
                ask.Close();
                ask.QueueFree();
            }
            if (chosen == 2 && !tuned) GameView.ApplyAsItCouldBe();            // the tune screen set them already, with the player's changes
            Settings.StartChoice = chosen == 2 ? "as_it_could_be" : "as_it_was";
            StartAnswered?.Invoke();
        }
        // the loading screen (picture file 6, loadpic2): shown before the game runs - most likely by the boot loader,
        // which is not ported (docs/port-gaps.md). From game_data/pics/loadpic2.json when it exists (made from a
        // screenshot of the original by tools/loading_screen.py until a disk image has file 6). Fades in, waits for a
        // click, fades out - how the loader really showed it is not known yet. Scripted runs skip it.
        var loading = Path.Combine(data.Directory, "pics", "loadpic2.json");
        if (entry == StartEntry.Boot && !scripted && File.Exists(loading))
        {
            using (var pic = JsonDocument.Parse(File.ReadAllText(loading)))
            {
                Convert.FromHexString(pic.RootElement.GetProperty("pixels_hex").GetString()!).CopyTo(screen.Pixels, 0);
                fade.SetBank(pic.RootElement.GetProperty("palette12").EnumerateArray().Select(c => (ushort)c.GetInt32()).ToArray());
            }
            foreach (var _ in FadeTo(15)) yield return "loading";
            foreach (var _ in WaitClick()) yield return "loading";
            foreach (var _ in FadeTo(0)) yield return "loading";
        }
        if (entry == StartEntry.League)
        {
            foreach (var f in League()) yield return f;
            if (ScrTrack >= 0) { GameChosen?.Invoke(GameMode.Hunter); yield break; }
        }
        else if (entry != StartEntry.Menus)
        {
            // title ($1F4C: picture file 5, its own palette): held 50 frames on boot ($1F2E),
            // waited on with a click when $2404 got here ($9758 WaitClick)
            using (var pic = JsonDocument.Parse(File.ReadAllText(Path.Combine(data.Directory, "pics", "loadpic1.json"))))
            {
                Convert.FromHexString(pic.RootElement.GetProperty("pixels_hex").GetString()!).CopyTo(screen.Pixels, 0);
                fade.SetBank(pic.RootElement.GetProperty("palette12").EnumerateArray().Select(c => (ushort)c.GetInt32()).ToArray());
            }
            foreach (var _ in FadeTo(15)) yield return "title";
            if (entry == StartEntry.Boot && scripted)
                for (var i = 0; i < 50; i++) yield return "title";
            else if (entry == StartEntry.Boot)
            {
                // ENHANCEMENT: the boot title waits for a click. The original
                // showed it while LoadGameFiles ($1F28) read the floppy - seconds - and then held 50 frames more;
                // with no loading only those 50 frames (1 s) were left. Scripted runs keep the original's 50 frames,
                // so recorded inputs line up as before.
                for (var i = 0; i < 50; i++) yield return "title";
                foreach (var _ in WaitClick()) yield return "title";
            }
            else
                foreach (var _ in WaitClick()) yield return "title";            // $9758
            foreach (var _ in FadeTo(0)) yield return "title";

            if (entry == StartEntry.Boot)
            {
                // credits ($213A)
                Pregame.Credits(screen, ui, data.Main);
                fade.SetBank(data.PaletteBanks[5]);
                foreach (var _ in FadeTo(15)) yield return "credits";
                for (var i = 0; i < 100; i++) yield return "credits";
                foreach (var _ in FadeTo(0)) yield return "credits";
            }
        }

        while (true)                                                            // $1F6C
        {
            Audio.Instance?.Music(0);                                           // $1F72, every round of TitleLoop
            var handler = 0;
            var mainButtons = menus.MainMenu(screen);
            if (Settings.ManualButton)                                          // the shell's extra, below the last button
                mainButtons.Add(menus.ManualButton(screen, mainButtons));       // (off: the menu is byte-identical to before)
            if (Settings.QuitButton)                                            // the shell's QUIT, at the foot (playtest note 2)
                mainButtons.Add(menus.QuitButton(screen, mainButtons));
            foreach (var f in Menu(mainButtons, "main_menu", h => handler = h, timeout: Recording ? 1 : 1500)) yield return f;   // $1F78-$1F80; a recording starts the demo at once
            switch (handler)
            {
                case 0x875A:                                                    // View soldier
                    Pregame.SoldierScreen(screen, ui, data.Main, soldier);
                    fade.SetBank(data.PaletteBanks[6]);                         // $8776
                    foreach (var _ in FadeTo(15)) yield return "view_soldier";
                    foreach (var _ in WaitClick()) yield return "view_soldier";
                    foreach (var _ in FadeTo(0)) yield return "view_soldier";
                    break;
                case 0x1014A:                                                   // Disk Operation
                    foreach (var f in DiskMenu()) yield return f;
                    if (Loaded is not null) { GameChosen?.Invoke(Loaded.Mode); yield break; }
                    break;
                case 0x22D6:                                                    // Demo, or the menu's time ran out
                    Demo = true;                                                // $22E4
                    foreach (var f in Briefing(GameMode.Action, StartState.DefaultSoldier(data))) yield return f;   // $22F6, $22FC SetGameMode(2)
                    GameChosen?.Invoke(GameMode.Action);
                    yield break;
                case 0x2310:                                                    // Start new game
                    // SoldierOrDefault $8666 (at $2310): a record the checks reject is replaced by
                    // the default soldier before the game menu names its first game
                    if (!soldier.Valid) soldier = StartState.DefaultSoldier(data);
                    foreach (var f in Menu(menus.GameMenu(screen, soldier.R[2], scrTracks: true), "game_menu", h => handler = h)) yield return f;
                    if (handler == 0x22D4) break;                               // CANCEL
                    if (handler == Menus.SandHandler)
                    {                                                           // Shai Hulud: the sand island, no briefing
                        SandIsland = true;
                        GameChosen?.Invoke(GameMode.Hunter);
                        yield break;
                    }
                    if (handler == Menus.ScrHandler)
                    {                                                           // Stuntin': the league screen, its race or practice
                        foreach (var f in League()) yield return f;
                        if (ScrTrack < 0) break;
                        GameChosen?.Invoke(GameMode.Hunter);
                        yield break;
                    }
                    var mode = (GameMode)((handler - 0x2354) / 4);              // $2354/$2358/$235C
                    foreach (var f in Briefing(mode)) yield return f;
                    GameChosen?.Invoke(mode);
                    yield break;
            }
        }
    }

    /// <summary>$1FF8 + $1F96: fade the drawn menu in (bank 5), wait for a click on a button with a
    /// handler ($1FA0), fade out. timeout: SetMenuTimeout ($1F8A, the main menu only): the copper
    /// interrupt counts it down a frame at a time from before the fade-in ($C684); at 0 the wait
    /// runs the demo ($1FB4: the routine at $F0) - only a click stops it, not the pointer moving.</summary>
    IEnumerable<string> Menu(List<Menus.Button> buttons, string name, Action<int> chosen, int timeout = -1)
    {
        fade.SetBank(data.PaletteBanks[5]);
        foreach (var _ in FadeTo(15)) { if (timeout > 0) timeout--; yield return name; }
        var was = true;
        while (true)
        {
            if (timeout == 0)
            {
                chosen(0x22D6);
                break;
            }
            if (button && !was && Menus.Hit(buttons, mouseX, mouseY) is { } b)
            {
                if (b.Handler == Menus.ManualHandler)                           // MANUAL: the menu stays exactly as it
                {                                                               // is - the manual opens over it, and on
                    ManualRequested?.Invoke();                                  // closing the wait carries on here
                    continue;
                }
                if (b.Handler == Menus.QuitHandler) GetTree().Quit();           // QUIT: the program closes
                picked = b;
                chosen(b.Handler);
                break;
            }
            was = button;
            if (timeout > 0) timeout--;
            yield return name;
        }
        foreach (var _ in FadeTo(0)) yield return name;
    }

    /// <summary>STUNTIN's league screen (ScrLeagueScreen): RACE starts the round's race against the named opponent, NEXT runs
    /// a round the player sits out, PRACTICE drives his next track alone, NEW asks and starts a new league, BACK returns to
    /// the menus (ScrTrack stays -1). Without the drivers' file there is no league: straight onto Little Ramp, alone.</summary>
    IEnumerable<string> League()
    {
        ScrLeagueRun.DataDir = data.Directory;
        if (ScrLeague.Drivers(data.Directory) is null)
        {
            ScrTrack = 0;
            yield break;
        }
        while (true)
        {
            var l = ScrLeagueRun.League;
            var (buttons, shown) = ScrLeagueScreen.Draw(screen, ui, l, ScrLeagueRun.Names(), ScrLeagueRun.News);
            faces = shown.Select(f => (FaceImage(f.Id), f.X, f.Y)).Where(f => f.Item1 is not null).Select(f => (f.Item1!, f.X, f.Y)).ToList();
            var handler = 0;
            foreach (var f in Menu(buttons, "scr_league", h => handler = h)) yield return f;
            faces = null;
            switch (handler)
            {
                case ScrLeagueScreen.RaceHandler when ScrLeagueRun.Next(data.Directory) is { } next:
                    ScrLeagueRun.Race = next.Opponent;
                    ScrTrack = next.Track;
                    yield break;
                case ScrLeagueScreen.NextHandler:
                    ScrLeagueRun.SitOut();
                    break;
                case ScrLeagueScreen.PracticeHandler:
                    ScrLeagueRun.Practice = true;
                    ScrTrack = l.Track(l.PlayerDivision, l.Round);
                    yield break;
                case ScrLeagueScreen.NewHandler:
                    var yes = 0;
                    foreach (var f in Menu(ScrLeagueScreen.AskNew(screen, ui), "scr_league_new", h => yes = h)) yield return f;
                    if (yes == ScrLeagueScreen.YesHandler) ScrLeagueRun.New();
                    break;
                case ScrLeagueScreen.BackHandler:
                    ScrLeagueRun.News = null;
                    yield break;
            }
        }
    }

    /// <summary>The faces the league screen shows, painted over it in their own colours (Present).</summary>
    List<(Image Image, int X, int Y)>? faces;
    readonly Dictionary<int, Image?> faceImages = [];
    Image? FaceImage(int id)
    {
        if (faceImages.TryGetValue(id, out var img)) return img;
        var path = Path.Combine(data.Directory, "scr", "drivers", $"{id:00}.png");
        img = File.Exists(path) ? Image.LoadFromFile(path) : null;
        img?.Convert(Image.Format.Rgb8);
        return faceImages[id] = img;
    }

    /// <summary>$1014A DiskMenuRun from the main menu: Load Game picks a slot ($101F0) and ends with a
    /// loaded game; Init Disk writes an empty directory; Save Game does nothing (no game: $1021A
    /// refuses); End goes back to the main menu. easy_saves: Init Disk is not shown, and the first
    /// Load sets the save folder up by itself.</summary>
    IEnumerable<string> DiskMenu()
    {
        var disk = new SaveDisk(GameView.SavesDir);
        while (true)
        {
            var handler = 0;
            foreach (var f in Menu(menus.DiskMenu(screen, !Settings.EasySaves), "disk_menu", h => handler = h)) yield return f;
            if (handler == 0x10162) yield break;                                // End
            if (handler == 0x10176)                                             // Init Disk (easy_saves off): ConfirmMenu $1017A first
            {
                foreach (var f in Menu(menus.ConfirmMenu(screen), "confirm_menu", h => handler = h)) yield return f;
                if (handler == ConfirmScreen.YesHandler) disk.Init();           // $10180 bmi: No keeps the saves
                continue;
            }
            if (handler != 0x101F0) continue;                                   // Save: refused
            if (disk.ReadDirectory() is not { } directory)
            {
                if (!Settings.EasySaves) continue;                              // $101D0 bmi: no directory, the menu again
                directory = disk.Init();                                        // easy_saves: the folder is set up here
            }
            foreach (var f in Menu(menus.SlotMenu(screen, directory, save: false), "slot_menu", h => handler = h)) yield return f;
            if (handler == Menus.CancelHandler) continue;
            var slot = menus.SlotMenu(new IndexedScreen(), directory, false).FindIndex(b => b.Text == picked!.Text && b.Y0 == picked.Y0) - 1;
            try { Loaded = SaveGame.Read(data, disk.ReadGame(slot) ?? [], null, Settings.FixSpikes); }
            catch (InvalidDataException) { GD.PushWarning($"HUNTER: slot {slot} could not be loaded (checksum)"); continue; }
            if (Settings.RestoreGameOptions(disk.OptionsPath(16 + slot)))   // the save's options back on before GameView builds the Play from them, as the in-play load does (GameViewDisk.LoadBlock; this load skipped it until 11-11)
                GD.Print("HUNTER: the save's sound options take effect after a restart");   // no message box on the title screen
            yield break;
        }
    }

    /// <summary>$86F2 for a valid soldier: the name box if the soldier is new, then the briefing. The
    /// demo (shown = its default soldier) has no name box ($8712 bmi: "Trigger Happy", $8738) and
    /// its briefing waits 201 frames, not for a click ($AC2A: WaitBlanks with D0 = $C8).</summary>
    IEnumerable<string> Briefing(GameMode mode, Soldier? shown = null)
    {
        var soldier = shown ?? this.soldier;
        screen.Clear();                                                         // $8776: bank 6 at level 0, clear
        fade.SetBank(data.PaletteBanks[6]);
        if (soldier.New && !Demo)
        {
            foreach (var _ in FadeTo(15)) yield return "name_box";
            foreach (var f in NameBox()) yield return f;
            foreach (var _ in FadeTo(0)) yield return "name_box";
        }
        var name = Pregame.RecordName(soldier);
        Pregame.SoldierPanel(screen, ui, data.Main, soldier, name);             // $8818
        foreach (var _ in FadeTo(15)) yield return "briefing";
        // $9488: the objective is typed one character a frame ($C37A); the remake draws it whole
        // and replays the screen as it stood before each character.
        var typed = new List<byte[]>();
        Pregame.Briefing(screen, ui, data.Main, soldier, name, (int)mode, data.Worlds[mode].Header,
            () => typed.Add((byte[])screen.Pixels.Clone()));
        var final = (byte[])screen.Pixels.Clone();
        foreach (var t in typed)
        {
            t.CopyTo(screen.Pixels, 0);
            yield return "briefing";
        }
        final.CopyTo(screen.Pixels, 0);
        if (Demo) for (var i = 0; i < 201; i++) yield return "briefing";       // $AC30-$AC36
        else foreach (var _ in WaitClick()) yield return "briefing";            // $AC2A -> $AC84
        foreach (var _ in FadeTo(0)) yield return "briefing";                   // $876C
    }

    /// <summary>$B41A: wait for the button to come up; then, over and over: 4 frames, the line with
    /// '/', 4 frames, the line with ' ', then one look at the button (ends) and one key.</summary>
    IEnumerable<string> NameBox()
    {
        while (button) yield return "name_box";
        var typed = new Pregame.TypedLine();
        typed.Start(14, Pregame.RecordName(soldier));                        // $8730 moveq #$E
        while (true)
        {
            for (var i = 0; i < 4; i++) yield return "name_box";
            Pregame.NameBox(screen, ui, data.Main, typed, cursorShown: true);
            for (var i = 0; i < 4; i++) yield return "name_box";
            Pregame.NameBox(screen, ui, data.Main, typed, cursorShown: false);
            if (button) break;
            if (keys.TryDequeue(out var k) && Pregame.Key(typed, k.Raw, k.Ascii)) break;
        }
        Pregame.SetName(soldier, Pregame.FinalName(data.Main, typed));
    }

    /// <summary>$AC84: wait for the button to be up, then down.</summary>
    IEnumerable<int> WaitClick()
    {
        while (button) yield return 0;
        while (!button) yield return 0;
    }

    /// <summary>A fade to a level (0 black .. 15 full), waiting until it is done.</summary>
    IEnumerable<int> FadeTo(int level)
    {
        fade.FadeTo(level);
        while (fade.Busy) yield return 0;
    }

    public override void _PhysicsProcess(double delta) => Frame();

    /// <summary>One PAL frame (50 Hz).</summary>
    public void Frame()
    {
        ReadMouse();
        fade.Tick();
        if (run.MoveNext()) Current = run.Current;
        Present();
    }

    void Present()
    {
        for (var i = 0; i < 16; i++)
        {
            var c = fade.Colour(i);
            lut[3 * i] = (byte)(((c >> 8) & 15) * 17);
            lut[3 * i + 1] = (byte)(((c >> 4) & 15) * 17);
            lut[3 * i + 2] = (byte)((c & 15) * 17);
        }
        // mode 1's copper re-writes colour 8 per row (list $5CEC, words $5D70-$5DC8), faded with
        // the picture ($6A4A) - the title's dusk sky; the palette's placeholder $0F0 shows nowhere
        var title = Current == "title";
        for (var p = 0; p < screen.Pixels.Length; p++)
        {
            var i = screen.Pixels[p] & 15;
            if (title && i == 8)
            {
                var s = TitleSky.Colour8(p / 320, fade.Level);
                rgb[3 * p] = (byte)((s >> 8 & 15) * 17);
                rgb[3 * p + 1] = (byte)((s >> 4 & 15) * 17);
                rgb[3 * p + 2] = (byte)((s & 15) * 17);
                continue;
            }
            if ((screen.Pixels[p] & 1 << Menus.ScrTextPlane) != 0)
            {                                                   // the track buttons' text: cyan, faded with the menu
                var c = (byte)((15 * fade.Level >> 4) * 17);    // as a bank's $0FF would be
                rgb[3 * p] = 0; rgb[3 * p + 1] = c; rgb[3 * p + 2] = c;
                continue;
            }
            rgb[3 * p] = lut[3 * i]; rgb[3 * p + 1] = lut[3 * i + 1]; rgb[3 * p + 2] = lut[3 * i + 2];
        }
        if (faces is not null)                                  // the league screen's faces, faded with the menu
            foreach (var (img, fx, fy) in faces)
                for (var y = 0; y < img.GetHeight() && fy + y < 200; y++)
                    for (var x = 0; x < img.GetWidth() && fx + x < 320; x++)
                    {
                        var c = img.GetPixel(x, y);
                        var p = 3 * ((fy + y) * 320 + fx + x);
                        rgb[p] = (byte)(c.R8 * fade.Level / 15); rgb[p + 1] = (byte)(c.G8 * fade.Level / 15); rgb[p + 2] = (byte)(c.B8 * fade.Level / 15);
                    }
        // the pointer sprite over everything, top left at the mouse - (7, 6) ($AA1C); it uses colour 17 only
        Hunter.Pointer.Draw(rgb, pointer, pointerColour, mouseX, mouseY);
        image.SetData(320, 200, false, Image.Format.Rgb8, rgb);
        texture.Update(image);
    }

    /// <summary>Save the current frame (verification).</summary>
    public void Save(string path) => image.SavePng(path);
}
