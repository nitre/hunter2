using System.Text.Json;

namespace Hunter.Rules;

/// <summary>
/// The menus, as data from ui.json (extracted from the program) and drawn by Ui: the screens before
/// play and the in-game options screen ($216C, after an Esc quit: OptionsScreen).
/// A button is also a click region ($ADA2): x0 &lt;= x &lt; x1, y0 &lt;= y &lt; y1; a click on it gives
/// the handler address the original jumps to (data word 2 of the entry, $AF64).
/// </summary>
public sealed class Menus
{
    public sealed record Button(int X0, int X1, int Y0, int Y1, int Handler, string Text);

    readonly Ui _ui;
    readonly JsonElement _json;

    public Menus(Ui ui, string dataDir)
    {
        _ui = ui;
        _json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir, "tables", "ui.json"))).RootElement;
    }

    static Ui.MenuItem Item(JsonElement i) =>
        new(i.GetProperty("style").GetInt32(), i.GetProperty("dx").GetInt32(), i.GetProperty("text").GetString()!);

    /// <summary>$2032 via $1F94: the main menu.</summary>
    public List<Button> MainMenu(IndexedScreen s) => DrawMenu(s, _json.GetProperty("main_menu"));

    /// <summary>$216C via $2164 (ShowMenuWaitFadeRun): the in-game options screen an Esc quit ends
    /// in (DrawMenuScreen $2000: the menu picture once, ClearScreen, then the entries). The first
    /// entry is the title ("IN GAME OPTIONS", handler 0: no region, $AF64 skips it) - not a button.</summary>
    public OptionsScreen Options()
    {
        var s = new IndexedScreen();
        var buttons = DrawMenu(s, _json.GetProperty("options_menu")).Where(b => b.Handler != 0)
            .Select(b => new OptionsButton(b, OptionsScreen.Result(b.Handler) ?? throw new InvalidOperationException(
                $"options button ${b.Handler:X}: not one of the four handlers"))).ToList();
        return new OptionsScreen(s, buttons);
    }

    /// <summary>$21FA via $21F2 (ConfirmMenu): Quit Game's "Are you sure".</summary>
    public ConfirmScreen Confirm()
    {
        var s = new IndexedScreen();
        var b = ConfirmMenu(s);
        return new ConfirmScreen(s, b.First(x => x.Handler == ConfirmScreen.YesHandler),
                                    b.First(x => x.Handler == ConfirmScreen.NoHandler));
    }

    /// <summary>ConfirmMenu $21F2 drawn on s: the Yes ($9064) and No ($9060) buttons. Quit Game and Init
    /// Disk ($1017A) both ask it.</summary>
    public List<Button> ConfirmMenu(IndexedScreen s) =>
        DrawMenu(s, _json.GetProperty("confirm_menu")).Where(b => b.Handler != 0).ToList();

    /// <summary>$100B8 via DiskMenuRun $10154 (DiskMenu $1013E, after Disk Operation): the title
    /// "DISK OPTIONS" then Load Game / Save Game / Init Disk / End. DiskMenu: D0 > 0 returns, D0 = 0
    /// (End $10162) jumps to $23D2 PlayGame, D0 &lt; 0 loops the menu. withInitDisk false (the
    /// shell's easy_saves): the menu is drawn whole from the original's data - so every button keeps
    /// the original's position - and then Init Disk's area is cleared and its button dropped, the
    /// others exactly where the original puts them.</summary>
    public List<Button> DiskMenu(IndexedScreen s, bool withInitDisk = true)
    {
        var buttons = DrawMenu(s, _json.GetProperty("disk_menu"));
        if (withInitDisk) return buttons.Where(b => b.Handler != 0).ToList();
        var init = buttons.First(b => b.Handler == 0x10176);
        s.ClearRect(init.X0, init.Y0, init.X1 - init.X0, init.Y1 - init.Y0);
        return buttons.Where(b => b.Handler != 0 && b.Handler != 0x10176).ToList();
    }

    List<Button> DrawMenu(IndexedScreen s, JsonElement m) =>
        Draw(s, m.GetProperty("y").GetInt32(), m.GetProperty("items").EnumerateArray().ToList());

    public const int SaveSlotHandler = 0x10328, CancelHandler = 0x9060;

    /// <summary>$10334 DrawSlotMenu: the title ($102B0 "LOAD A GAME" / $102CE "SAVE A GAME", y $28), the 6
    /// slots (the slot's name; a free slot's routine is the caller's A1 - Load 0 = no click, Save $10328 -
    /// a used one's $10328), then CANCEL ($102EC, routine $9060). Buttons 1-6 are slots 0-5.</summary>
    public List<Button> SlotMenu(IndexedScreen s, byte[] directory, bool save)
    {
        var items = new List<(Ui.MenuItem, int)> { (new Ui.MenuItem(0, 0, save ? "SAVE A GAME" : "LOAD A GAME"), 0) };
        for (var slot = 0; slot < SaveDisk.Slots; slot++)
            items.Add((new Ui.MenuItem(0, 0, SaveDisk.Name(directory, slot)),
                       SaveDisk.IsFree(directory, slot) && !save ? 0 : SaveSlotHandler));
        items.Add((new Ui.MenuItem(0, 0, "CANCEL"), CancelHandler));
        return Draw(s, 0x28, items);
    }

    /// <summary>$2310: AVAILABLE GAMES, then each game whose bit in $85E4 (completed games, soldier
    /// record +2) is clear, then CANCEL ($2346).</summary>
    public List<Button> GameMenu(IndexedScreen s, int completedBits, bool scrTracks = false)
    {
        var title = _json.GetProperty("game_menu");
        var games = _json.GetProperty("game_items").EnumerateArray().ToList();
        var items = new List<JsonElement> { title.GetProperty("items")[0] };
        for (var g = 0; g < 3; g++)
            if ((completedBits >> g & 1) == 0) items.Add(games[g]);
        if (!scrTracks)
        {
            items.Add(games[3]);
            return Draw(s, title.GetProperty("y").GetInt32(), items);
        }
        // the zones' islands under the games, the text in the shell's cyan (plane ScrTextPlane), then CANCEL below them
        var buttons = Draw(s, title.GetProperty("y").GetInt32(), items);
        var f = _ui.Frames[0x17];
        foreach (var (text, handler) in Zones)
        {
            var y = buttons[^1].Y1;
            var x0 = (s.Width >> 1) - (f.Width >> 1);
            var h = _ui.DrawMenuItem(s, new Ui.MenuItem(0, 0, text), y, ScrTextPlane);
            buttons.Add(new Button(x0, x0 + f.Width, y, y + h, handler, text));
        }
        var cancelY = buttons[^1].Y1;
        var cancel = Item(games[3]);
        var ch = _ui.DrawMenuItem(s, cancel, cancelY);
        var cf = _ui.Frames[cancel.Style + 0x17];
        var cx0 = (s.Width >> 1) + cancel.Dx * 2 - (cf.Width >> 1);
        buttons.Add(new Button(cx0, cx0 + cf.Width, cancelY, cancelY + ch, games[3].GetProperty("handler").GetInt32(), cancel.Text));
        return buttons;
    }

    /// <summary>The zones' buttons (no original address): Shai Hulud, the sand island (ShaiHulud), and Stuntin', the
    /// Stunt Car Racer island (ScrIsland).</summary>
    public const int ScrHandler = 0x5C00, SandHandler = 0x5C10;
    public static readonly (string Text, int Handler)[] Zones = [("Shai Hulud", SandHandler), ("Stuntin'", ScrHandler)];
    /// <summary>The plane the zones' text is drawn in: above the menu's 4, shown cyan by the start screen.</summary>
    public const int ScrTextPlane = 7;

    List<Button> Draw(IndexedScreen s, int y, List<JsonElement> items) =>
        Draw(s, y, items.Select(i => (Item(i), i.GetProperty("handler").GetInt32())).ToList());

    List<Button> Draw(IndexedScreen s, int y, List<(Ui.MenuItem Item, int Handler)> items)
    {
        s.Clear();                                           // $2016 -> $CA9E clears the screen
        var buttons = new List<Button>();
        foreach (var (it, handler) in items)
        {
            var f = _ui.Frames[it.Style + 0x17];
            var x0 = (s.Width >> 1) + it.Dx * 2 - (f.Width >> 1);
            var h = _ui.DrawMenuItem(s, it, y);
            buttons.Add(new Button(x0, x0 + f.Width, y, y + h, handler, it.Text));
            y += h;
        }
        return buttons;
    }

    /// <summary>$AF64: the first button containing the point; null when none (or it has no handler).</summary>
    public static Button? Hit(IEnumerable<Button> buttons, int x, int y) =>
        buttons.FirstOrDefault(b => x >= b.X0 && x < b.X1 && y >= b.Y0 && y < b.Y1 && b.Handler != 0);

    /// <summary>The shell's MANUAL button (a Godot setting, default OFF; F3 opens the manual
    /// either way): no original address - the shell checks for it before the menus' own handlers.
    /// Drawn with the menus' own button drawing ($AD08, Ui.DrawMenuItem), in the menu's own style
    /// 0 centred on 160, below the last button - so no original button moves (the menus start at
    /// y 40 and their buttons are 16 px tall: the main menu's five end at y 120, MANUAL sits at
    /// 120-136).</summary>
    public const int ManualHandler = 0x4D41;

    public Button ManualButton(IndexedScreen s, IReadOnlyList<Button> buttons) => ShellButton(s, buttons, "MANUAL", ManualHandler);

    /// <summary>A shell button (MANUAL, SETUP, QUIT): one menu item centred on 160, below the last button given.</summary>
    Button ShellButton(IndexedScreen s, IReadOnlyList<Button> buttons, string text, int handler)
    {
        var y = buttons[^1].Y1;
        var h = _ui.DrawMenuItem(s, new Ui.MenuItem(0, 0, text), y);
        var f = _ui.Frames[0x17];
        return new Button((s.Width >> 1) - (f.Width >> 1), (s.Width >> 1) + (f.Width >> 1), y, y + h, handler, text);
    }

    /// <summary>The shell's SETUP button (settings_screen.md Job B, the MANUAL wiring copied; always
    /// shown since 2026-11-02 - it is the only way to the settings): no original address - the
    /// shell checks for it before the menus' own handlers, and it opens the settings screen over
    /// the menu. Same drawing and place as MANUAL: below the last button it is given (the shell
    /// stacks SETUP above MANUAL so each sits where it would alone).</summary>
    public const int SetupHandler = 0x5345;

    public Button SetupButton(IndexedScreen s, IReadOnlyList<Button> buttons) => ShellButton(s, buttons, "SETUP", SetupHandler);

    /// <summary>The shell's QUIT button on the main menu (playtest note 2; godot/Settings.cs quit_button,
    /// default ON): the original has no way out - the shell closes the program. Drawn and placed like MANUAL.</summary>
    public const int QuitHandler = 0x5155;

    public Button QuitButton(IndexedScreen s, IReadOnlyList<Button> buttons) => ShellButton(s, buttons, "QUIT", QuitHandler);
}
