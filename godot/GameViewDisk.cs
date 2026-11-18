using System.Collections.Generic;
using System.Linq;
using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>The disk menu in play (DiskMenu $1013E, reached from the options screen's Disk Operation
/// or the panel's SAVE+QUIT): Load Game / Save Game / Init Disk / End, the slot menu ($10334), and
/// the name box ($10260 TypeText) for a save. Re-uses the rules' SaveDisk and SaveGame; the files
/// sit in a folder (no disk swapping, re/formats/save.md 2). Doc in save.md section 1-3.</summary>
public partial class GameView
{
    enum DiskStage { None, Menu, Confirm, Slots, Name }

    DiskStage diskStage;
    IndexedScreen? diskScreen;
    List<Menus.Button> diskButtons = [];
    bool diskSave;
    int diskSlot, nameFrames;
    readonly Pregame.TypedLine typedName = [];
    SaveDisk? saveDisk;
    byte[] diskDirectory = [];

    /// <summary>Where the save disk's files live: HUNTER_SAVES, or Godot's user:// folder.</summary>
    internal static string SavesDir =>
        System.Environment.GetEnvironmentVariable("HUNTER_SAVES") is { Length: > 0 } e
            ? e : ProjectSettings.GlobalizePath("user://saves");

    bool DiskUp => diskStage != DiskStage.None;

    /// <summary>$1013E: the disk menu's screen, faded in over the menus' bank like the options screen.
    /// easy_saves: Init Disk is not shown (players do not format disks); the other buttons keep the
    /// original's positions (Menus.DiskMenu draws the original's data whole and clears one area).</summary>
    void OpenDisk()
    {
        saveDisk ??= new SaveDisk(SavesDir);
        diskScreen = new IndexedScreen();
        diskButtons = new Menus(ui, data.Directory).DiskMenu(diskScreen, !Settings.EasySaves);
        diskStage = DiskStage.Menu;
        FadeIn();
    }

    void OpenSlots()
    {
        diskScreen = new IndexedScreen();
        diskButtons = new Menus(ui, data.Directory).SlotMenu(diskScreen, diskDirectory, diskSave);
        diskStage = DiskStage.Slots;
        FadeIn();                                                       // $10334: the slot menu fades in like every menu screen
    }

    /// <summary>A click while a disk screen is up; false when none is.</summary>
    bool DiskClick(int x, int y)
    {
        switch (diskStage)
        {
            case DiskStage.None: return false;
            case DiskStage.Name: NameDone(); return true;               // $B41A: a click ends the input
        }
        if (Menus.Hit(diskButtons, x, y) is not { } b) return true;
        var stage = diskStage;
        var index = diskButtons.IndexOf(b);
        if (stage == DiskStage.Slots && diskSave)                       // the save slots run $1FD6: fade in, wait, NO fade-out -
        {                                                               // the name box is typed over the lit slot menu ($10230)
            SlotChosen(index, b.Handler);
            return true;
        }
        FadeOutThen(() =>                                               // $1F98 / $1FD2: fade out, then the handler
        {
            if (stage == DiskStage.Menu) DiskMenuChosen(b.Handler);
            else if (stage == DiskStage.Confirm)                        // $1017A ConfirmMenu: Yes wipes, either way the menu again
            {
                if (b.Handler == ConfirmScreen.YesHandler) saveDisk!.Init();
                OpenDisk();
            }
            else SlotChosen(index, b.Handler);
        });
        return true;
    }

    void DiskMenuChosen(int handler)
    {
        switch (handler)
        {
            case 0x101F0 or 0x1021A:                                    // Load Game / Save Game
                diskSave = handler == 0x1021A;
                var dir = saveDisk!.ReadDirectory();                    // ReadDirectory $101C2
                if (dir is null && Settings.EasySaves)                  // easy_saves: no Init Disk needed - the
                    dir = saveDisk.Init();                              // folder is set up here (Init backs up what exists)
                if (dir is null) { OpenDisk(); break; }                 // $101D0 bmi: fails, the menu again
                diskDirectory = dir;
                OpenSlots();
                break;
            case 0x10176:                                               // Init Disk: "Are you sure" first ($1017A ConfirmMenu)
                diskScreen = new IndexedScreen();
                diskButtons = new Menus(ui, data.Directory).ConfirmMenu(diskScreen);
                diskStage = DiskStage.Confirm;
                FadeIn();
                break;
            default:                                                    // End $10162: d0 = 0 -> $23D2 PlayGame
                CloseDisk();
                play.Resume();
                options = null; confirm = null; screenFade = null; quitHandled = false;
                break;
        }
    }

    /// <summary>A slot button: button 0 is the title, 1-6 the slots, 7 CANCEL ($9060: the menu again).</summary>
    void SlotChosen(int button, int handler)
    {
        if (handler == Menus.CancelHandler) { OpenDisk(); return; }
        diskSlot = button - 1;                                          // $10328: $108 -= 1
        if (diskSave)
        {
            seededFree = SaveDisk.IsFree(diskDirectory, diskSlot);      // easy_saves: an unedited "free" names itself
            typedName.Start(14, System.Text.Encoding.ASCII.GetBytes(SaveDisk.Name(diskDirectory, diskSlot)));   // $1025E moveq #$E: TypeText from the slot's name
            nameFrames = 0;
            diskStage = DiskStage.Name;
            return;
        }
        LoadSlot();
    }

    /// <summary>The slot the name box came up on was free (the box seeded "free"); easy_saves only.</summary>
    bool seededFree;

    /// <summary>easy_saves' name for an untyped save: the game day as the panel clock shows it
    /// (State.Day + 1, PanelClock $C43A) and the slot number, e.g. "Day 1 Slot 3" (14 characters
    /// at most; Save cuts at NameMax).</summary>
    IReadOnlyList<byte> AutoName() =>
        System.Text.Encoding.ASCII.GetBytes($"Day {play.State.Day + 1} Slot {diskSlot + 1}");

    /// <summary>$101F0 load: the block, its checksum ($1056E; a failure is $10464, the menu again),
    /// then $23BE StartAfterLoad over the rebuilt game: the soldier panel and briefing, a click.
    /// The quick load (easy_saves) reads the same kind of block from file 22.</summary>
    void LoadSlot() => LoadBlock(saveDisk!.ReadGame(diskSlot), $"slot {diskSlot}", saveDisk.OptionsPath(16 + diskSlot));

    void LoadBlock(byte[]? block, string what, string optionsPath)
    {
        WorldState state;
        try
        {
            state = SaveGame.Read(data, block ?? [], play.State.Leftovers, Settings.FixSpikes);
        }
        catch (System.IO.InvalidDataException)
        {
            GD.PushWarning($"HUNTER: {what} could not be loaded (checksum)");
            if (DiskUp) OpenDisk();                                     // $10464: from the disk menu, the menu again
            return;
        }
        CloseDisk();
        var soundRestart = Settings.RestoreGameOptions(optionsPath);   // the save's options back on, before the new Play is built from them
        var fuller = state.Overflow.Count > 0 ? System.Math.Max(Settings.FullerWorld, 1) : Settings.FullerWorld;   // a fuller grid needs the room on a load too, as on a new game (GameView)
        play = new Play(data, state) { AnimEveryBlanks = A500PassBlanks };
        ApplySettings(play, demo: false, fuller);                        // the same list as a new game (it once left 10 out here)
        afterLoad = true;
        StartBriefing();
        if (soundRestart) Say("Sound options: restart to hear them");   // the sound rows are built at launch, no live switch (SettingsApply)
    }

    /// <summary>The name box ends (Return, a click, or typing past 14): the game is written ($1047C) to
    /// file 16 + slot, the directory too, and play goes on at $23D2. easy_saves: nothing typed, or
    /// "free" left as the slot menu seeded it, names itself (AutoName) instead of going back.</summary>
    void NameDone()
    {
        var nothing = typedName.Count == 0 ||                           // $B522 tst.w $1BC: nothing typed -> $10264 bmi: the slots again, no save
                      Settings.EasySaves && seededFree && typedName.SequenceEqual("free"u8.ToArray());
        if (nothing && !Settings.EasySaves)
        {
            OpenSlots();
            return;
        }
        saveDisk!.Save(diskSlot, nothing ? AutoName() : typedName, SaveGame.Write(play.State));
        Settings.GameOptions().Save(saveDisk.OptionsPath(16 + diskSlot));    // the options ride the save, in the file beside it
        CloseDisk();
        play.Resume();
        options = null; confirm = null; screenFade = null; quitHandled = false;
    }

    // ---- easy_saves: the quick slot (F5 save, F4 load), file 22 ----

    /// <summary>F5 (easy_saves, in play, nothing else up): the game written to the quick slot -
    /// file 22, none of the 6 slots - and "Game saved" in the message box.</summary>
    public void QuickSave()
    {
        if (!Settings.EasySaves || MenuUp || play.Mode != PlayMode.Play) return;
        saveDisk ??= new SaveDisk(SavesDir);
        saveDisk.SaveQuick(play.SaveInPlay());                          // play goes on as it was
        Settings.GameOptions().Save(saveDisk.OptionsPath(SaveDisk.QuickFile));   // the options ride the quick save too
        Say("Game saved");
    }

    /// <summary>Playtest's mark and Hold: the game as a save block, in play with nothing up (as QuickSave), else null.</summary>
    public byte[]? PlaytestSave() => MenuUp || play.Mode != PlayMode.Play ? null : play.SaveInPlay();

    /// <summary>Playtest's "Marked N", in the message box when in play.</summary>
    public void PlaytestNote(string text) { if (!MenuUp && play.Mode == PlayMode.Play) Say(text); }

    /// <summary>F4 (easy_saves, in play, nothing else up): the quick slot loaded ($23BE's flow, the
    /// same briefing a slot load shows); "No quick save" when there is none yet.</summary>
    public void QuickLoad()
    {
        if (!Settings.EasySaves || MenuUp || play.Mode != PlayMode.Play) return;
        saveDisk ??= new SaveDisk(SavesDir);
        var block = saveDisk.ReadQuick();
        if (block is null) { Say("No quick save"); return; }
        LoadBlock(block, "the quick save", saveDisk.OptionsPath(SaveDisk.QuickFile));
    }

    /// <summary>A one-line message in the panel's message box (the shell's own, easy_saves only:
    /// printed at the window's last line, $60C6 CursorToLastLine, like the game's messages - and on a line
    /// of its own: $B794 starts every message with ']', the new line that scrolls the window).</summary>
    void Say(string text)
    {
        var t = play.State.Panel.Text;
        t.X = 0;
        t.CursorToLastLine();
        t.Print(System.Text.Encoding.ASCII.GetBytes("]" + text + "*"));
    }

    void CloseDisk()
    {
        diskStage = DiskStage.None;
        diskScreen = null;
    }

    /// <summary>The name box keys ($B41A): raw code and character from the Godot key.</summary>
    void DiskKey(InputEventKey k)
    {
        var key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
        var raw = key switch { Key.Enter or Key.KpEnter => 0x44, Key.Backspace => 0x41, _ => 0 };   // $B41A: Return, Backspace
        var ascii = k.Unicode is > 0 and < 128 ? (int)k.Unicode : 0;
        if (Pregame.Key(typedName, raw, ascii)) NameDone();
    }

    /// <summary>The name box picture: 4 frames with the cursor '/', 4 without (the original's blink).</summary>
    void NameBoxFrame()
    {
        nameFrames++;
        Pregame.NameBox(diskScreen!, ui, data.Main, typedName, cursorShown: nameFrames / 4 % 2 == 0);
    }

    /// <summary>HUNTER_INPUT verbs for the headless run: type a name into the name box.</summary>
    public void DiskType(string text)
    {
        if (diskStage != DiskStage.Name) return;
        foreach (var c in text)
            if (Pregame.Key(typedName, 0, c)) { NameDone(); return; }
    }

    /// <summary>The headless Return in the name box.</summary>
    public void DiskEnter()
    {
        if (diskStage == DiskStage.Name) NameDone();
    }
}
