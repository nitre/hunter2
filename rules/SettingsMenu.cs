namespace Hunter.Rules;

/// <summary>The settings screen's pages, rules-side (design: settings_screen.md Job A). The shell's
/// Settings live in godot\Settings.cs, which this project cannot see - so the pages are data here
/// (each row: the cfg key the shell's property saves under, the label, when a change takes effect)
/// and the caller supplies the values: Draw takes key -&gt; display text, the shell reads its own
/// Settings, a test reads its own stand-in. Clicking a row is the caller's job too: the button
/// handler is the row's index in the page (Menus.Button.Handler), BACK is Menus.CancelHandler.
/// Rows are grouped by page; smooth_demo's row is gone - the demo is always
/// smooth (the cfg key stays in Settings.cs, unused). A row is named by its
/// TOPIC, the values name the CHOICES, and the original's word is always FIRST - so every page shows what the original did. Window
/// rows show RESTART but fullscreen applies at once; the new-game settings
/// say so in play only.</summary>
public sealed class SettingsMenu
{
    /// <summary>When a change takes effect, shown on the row after the value.</summary>
    public enum Effect
    {
        Now,                                            // the shell applies it as soon as it is set
        Restart,                                        // the row says RESTART: applied next launch
        NewGame                                         // in play the row says (NEW GAME): next new game
    }

    /// <summary>One row of a page. Key is the shell's cfg key (godot/Settings.cs); KeysKey marks the
    /// one row that is not a setting - it opens the existing F2 rebind screen.</summary>
    public sealed record Row(string Key, string Label, Effect Effect);

    public const string KeysKey = "keys";               // the F2 rebind row, no setting behind it

    /// <summary>The pages, in order. Every shell setting appears on exactly one page; only the
    /// key bindings are absent (the KEYS row stands for them).</summary>
    /// Grouped: the screen, the land and sky, the models, the world, sound,
    /// controls. The SETUP button's own row went (switched off, nothing opened this screen again).
    static readonly (string Group, Row[] Rows)[] Pages =
    [
        ("SCREEN",
        [
            new Row("look", "TV LOOK", Effect.Restart),                 // pal / centred / stretched
            new Row("brightness", "BRIGHTNESS", Effect.Now),            // 0.8 - 1.4, F7 live
            new Row("fullscreen", "FULLSCREEN", Effect.Now),            // applies at once
            new Row("text_size", "TEXT SIZE", Effect.Now),              // F8 live
            new Row("renderer", "3D WORLD", Effect.Restart),            // off (the original's flat picture) / on (the card draws the world, godot/real3d)
            new Row("gpu_scale", "PIXEL SIZE", Effect.Now),             // auto (the window's own scale) / 1 / 2 / 4
            new Row("wide_view", "SCREEN SHAPE", Effect.Restart),       // 4:3 (the original) / wide: the 3D view fills the window's width
            new Row("demo_camera", "DEMO CAMERA", Effect.Now),          // original (north) / moving: the attract demo's camera circles and sways by itself (DemoCamera)
            new Row("demo_world", "DEMO WORLD", Effect.Now),             // (moved here from WORLD 2026-11-11 beside DEMO CAMERA: the attract demo's two rows together, and WORLD's ten rows ran past the screen - SettingsPagesTests) original / wild: the attract demo in the fullest world, groups beside its route (Wild, DemoRoute)
        ]),
        ("LAND AND SKY",
        [
            new Row("far_view", "VIEW DISTANCE", Effect.Now),           // original / far - the flat picture's (shown with the 3D world off)
            new Row("view_distance", "VIEW DISTANCE", Effect.Now),      // original (7 rows) / 11 / 15 / 23 / 31 / 47 / 63 / all: how far the card draws (shown with it on)
            new Row("ground", "GROUND", Effect.Now),                    // MERGED (Merged): sun and ground, ground colours, ground pattern, ground shape
            new Row("sky", "SKY", Effect.Now),                          // MERGED: sky look, clouds, haze
            new Row("living_sea", "SEA", Effect.Now),                   // original (one flat colour) / living: pale by the coast, the original colour out at sea, slow quiet waves, the sky near the horizon; cycling: blocky cells whose colours glide round a ramp, the Amiga's colour cycling
        ]),
        ("MODELS",
        [
            new Row("own_colours", "MODEL COLOURS", Effect.Now),        // original (the 16 colours) / aga: helicopters, tanks, soldiers in their own shades, apart from the grass
            new Row("glass", "GLASS", Effect.Now),                      // original (painted panels) / see-through: the windows' faces (Glass.cs) drawn as glass
            new Row("sea_bodies", "SHARKS AND SUBS", Effect.Restart),   // original (a fin, a periscope) / whole: a shark under the fin, a hull and sail under the periscope (godot/real3d/SeaBodies.cs, baked at launch)
            new Row("smooth_animation", "ANIMATION", Effect.Now),       // original (a whole step every 7th blank) / smooth: a share every pass (Objects.AnimShare)
            new Row("own_shadows", "SHADOWS", Effect.Now),              // original / more
            new Row("card_pieces", "EXPLOSIONS", Effect.Now),           // original (ten triangles) / pieces: the thing's own panels thrown, breaking into triangles
        ]),
        ("WORLD",
        [
            new Row("world", "WORLD", Effect.NewGame),                  // MERGED: fuller world, objects, living world, graves (graves at once, the rest at a new game)
            new Row("finer_ground", "FINER GROUND", Effect.NewGame),    // original / 2x / hills / cliffs: the game's ground grown finer between the map's points and 1.5x taller (FineGround; HILLS 2x and CLIFFS 3x, rougher: Reliefs), new games (the attract demo: the picture alone)
            new Row("ground_seed", "GROUND SEED", Effect.NewGame),      // world / random / last: where finer ground's detail grows from - the world's own seed, a fresh one each new game, the last drawn one again (the coast and every placed thing stay the world's)
            new Row("leave_behind", "LEAVING A VEHICLE", Effect.Now),   // original (you step out on its east side first) / behind: on the side away from its heading, so a moving boat does not run you down (LeaveBehind)
            new Row("bullet_box", "BULLET BOX", Effect.Now),            // original (model 0's: 0 wide, 288 long - bullets jam on north-south lines) / small: 16 across, the height kept; new bullets, the attract demo too (DemoFollow)
        ]),
        ("SOUND",
        [
            new Row("a500_filter", "SOUND FILTER", Effect.Now),         // a500 (filter on) / a1200; the click applies the filter itself (F6's call)
            new Row("sound", "SOUND", Effect.Restart),                  // MERGED: sound extras, voices (extras at the next start)
        ]),
        ("CONTROLS",
        [
            new Row("message_log", "MESSAGE LOG", Effect.Now),          // off / on
            new Row("pause_on_focus_loss", "PAUSE IN BACKGROUND", Effect.Now),
            new Row("manual_button", "MANUAL BUTTON", Effect.Now),
            new Row("quit_button", "QUIT BUTTON", Effect.Now),          // QUIT on the main menu (playtest note 2)
            new Row("analog_steering", "STICK STEERING", Effect.Now),   // original (a stick = a key: full turn) / analog: a vehicle turns by as much as the gamepad stick leans (AnalogSteering)
            new Row("aiming", "AIMING", Effect.Now),                    // original (keys turn the soldier) / reticle: on foot he turns to the reticle and the shot goes to the point under it (AimReticle)
            new Row(KeysKey, "KEYS - USE F2", Effect.Now),              // opens the existing rebind screen
            new Row("players", "PLAYERS", Effect.Restart),              // hidden while two players are left out (Shown)
            new Row("split_layout", "SPLIT LAYOUT", Effect.Restart),
        ]),
    ];

    /// <summary>Rows the card's 3D world draws: with it off they show 3D ONLY and a click leaves them.</summary>
    public static readonly IReadOnlySet<string> ThreeDOnly = new HashSet<string>
    {
        "gpu_scale", "wide_view", "demo_camera", "view_distance", "ground",
        "living_sea", "own_colours", "glass", "sea_bodies", "card_pieces",
        // the flat picture is the original: these could show or play in it, and are held at the original's there (Settings.FlatOriginal)
        "sky", "own_shadows", "world", "finer_ground", "ground_seed", "sound", "analog_steering", "aiming",
    };

    /// <summary>MERGED ROWS: one row moves several settings together in steps. The settings behind it stay as they
    /// were, each in the settings file under its own key (and still settable there by hand), so the game reads them as
    /// before; the row shows the step they all match, or CUSTOM. FINER GROUND stays its own row: it changes the heights
    /// you drive and land on, a rules change as BULLET BOX. GROUND's ORIGINAL keeps the 4X shape: the original's flat
    /// triangles disagree with the height the rules use.
    /// Every step's parts are words of the part's own list in Parts.</summary>
    public static readonly IReadOnlyDictionary<string, (string Word, IReadOnlyDictionary<string, string> Parts)[]> Merged =
        new Dictionary<string, (string, IReadOnlyDictionary<string, string>)[]>
    {
        ["ground"] =
        [
            Step("ORIGINAL", ("ground_look", "ORIGINAL"), ("ground_colours", "ORIGINAL"), ("ground_pattern", "ORIGINAL"), ("ground_cuts", "4X")),
            Step("LIT", ("ground_look", "LIT"), ("ground_colours", "BANDS"), ("ground_pattern", "ORIGINAL"), ("ground_cuts", "8X")),
            Step("FULL", ("ground_look", "LIT"), ("ground_colours", "BANDS"), ("ground_pattern", "SPECKS"), ("ground_cuts", "32X")),
        ],
        ["sky"] =
        [
            Step("ORIGINAL", ("sky_look", "ORIGINAL"), ("clouds", "ORIGINAL"), ("haze", "OFF")),
            Step("SMOOTH", ("sky_look", "SMOOTH"), ("clouds", "CLOUDS"), ("haze", "BANDS")),
            Step("FULL", ("sky_look", "SMOOTH"), ("clouds", "CLOUDS"), ("haze", "FINE")),
        ],
        ["world"] =
        [
            Step("ORIGINAL", ("fuller_world", "ORIGINAL"), ("more_objects", "ORIGINAL"), ("living_world", "ORIGINAL"), ("graves", "NONE")),
            Step("SOME", ("fuller_world", "SOME"), ("more_objects", "ORIGINAL"), ("living_world", "ORIGINAL"), ("graves", "SHOWN")),
            Step("MORE", ("fuller_world", "MORE"), ("more_objects", "MORE"), ("living_world", "ON"), ("graves", "WITH VULTURES")),
            Step("LOTS", ("fuller_world", "LOTS"), ("more_objects", "MORE"), ("living_world", "ON"), ("graves", "WITH VULTURES")),
        ],
        ["sound"] =
        [
            Step("ORIGINAL", ("sound_extras", "ORIGINAL"), ("sound_voices", "ORIGINAL")),
            Step("ENHANCED", ("sound_extras", "ENHANCED"), ("sound_voices", "MORE")),
        ],
    };

    static (string, IReadOnlyDictionary<string, string>) Step(string word, params (string Key, string Word)[] parts) =>
        (word, parts.ToDictionary(p => p.Key, p => p.Word));

    /// <summary>What a merged row shows when its settings match none of its steps (a hand-edited file); a click
    /// from it goes to the first step (Next / OptionsPanel.Previous: a word not in the list steps to the first).</summary>
    public const string Custom = "CUSTOM";

    /// <summary>A merged row's word: the step every part matches, read with part (a part's key -> its word), or CUSTOM.</summary>
    public static string MergedWord(string key, Func<string, string> part) =>
        Merged[key].FirstOrDefault(s => s.Parts.All(p => part(p.Key) == p.Value)).Word ?? Custom;

    /// <summary>The part settings a merged row's step sets (none for CUSTOM or a word it does not have).</summary>
    public static IEnumerable<KeyValuePair<string, string>> MergedParts(string key, string word) =>
        Merged[key].FirstOrDefault(s => s.Word == word).Parts ?? new Dictionary<string, string>();

    /// <summary>Every setting the merged rows stand for, each a word list of its own (Parts).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Parts = new Dictionary<string, string[]>
    {
        ["ground_look"] = ["ORIGINAL", "LIT"],
        ["ground_pattern"] = ["ORIGINAL", "SPECKS"],
        ["ground_colours"] = ["ORIGINAL", "BANDS"],
        ["ground_cuts"] = ["ORIGINAL", "2X", "4X", "8X", "16X", "32X"],
        ["sky_look"] = ["ORIGINAL", "SMOOTH"],
        ["clouds"] = ["ORIGINAL", "CLOUDS"],
        ["haze"] = ["OFF", "BANDS", "FINE"],
        ["fuller_world"] = ["ORIGINAL", "SOME", "MORE", "LOTS"],
        ["more_objects"] = ["ORIGINAL", "MORE"],
        ["living_world"] = ["ORIGINAL", "ON"],
        ["graves"] = ["NONE", "SHOWN", "WITH VULTURES"],
        ["sound_extras"] = ["ORIGINAL", "ENHANCED"],
        ["sound_voices"] = ["ORIGINAL", "MORE"],
    };

    /// <summary>Whether a row is on the page: one VIEW DISTANCE, the one that applies (the flat picture's or the 3D
    /// world's); the two-player rows not while two players are left out.</summary>
    public static bool Shown(string key, bool threeD) => key switch
    {
        "far_view" => false,                                     // the flat picture's far view: off the page, the flat picture draws as far as the original
        "view_distance" => true,                                 // 3D ONLY with the 3D world off
        "players" or "split_layout" => false,
        _ => true,
    };

    public IReadOnlyList<string> Groups => Pages.Select(p => p.Group).ToArray();

    /// <summary>Every row's allowed display values, in click order (the click wraps at the end) -
    /// the same words the shell's SettingsValue lookup draws (SettingsMenuTests checks the two
    /// against each other, and the shell applies the chosen word back onto its Settings). The keys
    /// row is not a setting and has no entry.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Values = new Dictionary<string, string[]>
    {
        ["look"] = ["PAL", "CENTRED", "STRETCHED"],
        ["brightness"] = ["0.8", "0.9", "1.0", "1.1", "1.2", "1.3", "1.4"],
        ["renderer"] = ["OFF", "ON"],
        ["demo_camera"] = ["ORIGINAL", "MOVING"],
        ["demo_world"] = ["ORIGINAL", "WILD"],
        ["gpu_scale"] = ["AUTO", "1", "2", "4"],
        ["wide_view"] = ["4:3", "WIDE"],
        ["card_pieces"] = ["ORIGINAL", "PIECES", "LASTING"],   // LASTING: the pieces stay where they fall (Settings.PiecesStay)
        ["own_colours"] = ["ORIGINAL", "AGA"],
        ["glass"] = ["ORIGINAL", "SEE-THROUGH"],
        ["sea_bodies"] = ["ORIGINAL", "WHOLE"],
        ["smooth_animation"] = ["ORIGINAL", "SMOOTH"],
        ["living_sea"] = ["ORIGINAL", "LIVING", "CYCLING"],
        ["view_distance"] = ["ORIGINAL", "11", "15", "23", "31", "47", "63", "ALL"],
        ["text_size"] = ["NORMAL", "LARGE", "EXTRA LARGE"],
        ["players"] = ["1", "2"],
        ["split_layout"] = ["SIDE", "STACKED"],
        ["own_shadows"] = ["ORIGINAL", "MORE", "REAL"],   // REAL: true shadows from the sun's picture (3D view; the rules keep MORE)
        ["finer_ground"] = ["ORIGINAL", "FINER", "2X", "HILLS", "CLIFFS"],   // FINER: the detail at the original's height; 2X on: taller (FineGround.Reliefs)
        ["ground_seed"] = ["WORLD", "RANDOM", "LAST"],
        ["leave_behind"] = ["ORIGINAL", "BEHIND"],
        ["analog_steering"] = ["ORIGINAL", "ANALOG"],
        ["aiming"] = ["ORIGINAL", "RETICLE"],
        ["bullet_box"] = ["ORIGINAL", "SMALL"],
        ["far_view"] = ["ORIGINAL", "FAR"],
        ["a500_filter"] = ["A500", "A1200"],
        ["ground"] = ["ORIGINAL", "LIT", "FULL"],          // the merged rows: their steps' words, in Merged's order (MergedRowsTests)
        ["sky"] = ["ORIGINAL", "SMOOTH", "FULL"],
        ["world"] = ["ORIGINAL", "SOME", "MORE", "LOTS"],
        ["sound"] = ["ORIGINAL", "ENHANCED"],
    };

    /// <summary>What every recording (film, video) runs with: each extra row's word. A new row must go here or in NotRecorded
    /// (SettingsMenuTests), so a new extra can't be left out of the films unnoticed.</summary>
    public static readonly IReadOnlyDictionary<string, string> Recorded = new Dictionary<string, string>
    {
        ["renderer"] = "ON",
        ["gpu_scale"] = "4",
        ["sky"] = "FULL",                        // smooth sky, clouds, fine haze (as the films had them row by row)
        ["own_shadows"] = "REAL",                // the 3D view's sun shadows; the flat renderer keeps MORE (GameView: REAL turns OwnShadows on too)
        ["view_distance"] = "11",               // the shorter distance with fine haze, not the longest
        ["ground"] = "FULL",                     // lit, bands, specks, 32X
        ["card_pieces"] = "PIECES",
        ["own_colours"] = "AGA",
        ["glass"] = "SEE-THROUGH",              // In films too
        ["sea_bodies"] = "WHOLE",
        ["leave_behind"] = "BEHIND",            // In films too (the attract demo itself never takes it - GameView)
        ["bullet_box"] = "SMALL",
        ["smooth_animation"] = "SMOOTH",
        ["living_sea"] = "CYCLING",
        ["world"] = "MORE",                      // fuller world MORE (not LOTS), more objects, living world
                                                 // (the player's games; never the attract demo), graves with vultures
        ["finer_ground"] = "FINER",              // the finer detail at the original's height (2X and up also make the hills taller)
        ["sound"] = "ENHANCED",                  // extras, voices
        ["demo_camera"] = "MOVING",              // the attract's own camera moves; run_game --sway / --spin still override it
    };

    /// <summary>AS IT COULD BE, the first start's second answer: the films' extras, but the
    /// pixel size follows the player's own window.
    /// AS IT (KIND OF) WAS sets nothing: a fresh file is the original already, its always-on fixes aside.</summary>
    public static readonly IReadOnlyDictionary<string, string> AsItCouldBe = Recorded
        .Where(r => r.Key != "sea_bodies")                               // the first start's box is full at 27 rows (StartChoicePanel.ListStep): set it on the MODELS page
        .Select(r => r.Key switch { "gpu_scale" => new KeyValuePair<string, string>(r.Key, "AUTO"), _ => r })
        .Append(new KeyValuePair<string, string>("analog_steering", "ANALOG"))   // play input, so not in films; NEW is the game as we play it
        .Append(new KeyValuePair<string, string>("aiming", "RETICLE"))   // play input too
        .ToDictionary(r => r.Key, r => r.Value);

    /// <summary>Rows recordings leave as they are, and why.</summary>
    public static readonly IReadOnlyDictionary<string, string> NotRecorded = new Dictionary<string, string>
    {
        ["look"] = "how the window shows the picture, not the game",
        ["brightness"] = "the screen's brightness, not the game",
        ["wide_view"] = "the film's frame shape - not decided",
        ["far_view"] = "the old flat drawer's view distance - not drawn by the card",
        ["demo_world"] = "the wild cut is picked per video (run_game --wild)",
        ["ground_seed"] = "a film's hills must come out the same each run: the world's own seed",
        ["a500_filter"] = "which Amiga's sound filter - a choice of machine, not an extra",
        ["message_log"] = "a control, nothing on screen in play",
        ["pause_on_focus_loss"] = "a control",
        ["manual_button"] = "a control",
        ["quit_button"] = "a control",
        ["analog_steering"] = "play input, not a look - films replay recorded input; never the attract demo",
        ["aiming"] = "play input, not a look - films replay recorded input; never the attract demo",
        [KeysKey] = "the key bindings",
        ["fullscreen"] = "the window",
        ["text_size"] = "the window",
        ["players"] = "two players are left out for now",
        ["split_layout"] = "two players are left out for now",
    };

    /// <summary>The next value of a row after `current` (wraps to the first; a value the table
    /// does not know steps to the first). Every plain on/off row is OFF then ON.</summary>
    public static string Next(string key, string current)
    {
        if (!Values.TryGetValue(key, out var v)) v = ["OFF", "ON"];   // the on/off rows share one list
        var i = Array.IndexOf(v, current);
        return v[(i + 1) % v.Length];
    }

    /// <summary>One page's rows, title first so a caller can draw a plain menu page.</summary>
    public (string Title, IReadOnlyList<Row> Rows) Page(string group)
    {
        var p = Pages.First(x => x.Group == group);
        return (p.Group, Hidden.Count == 0 ? p.Rows : p.Rows.Where(r => !Hidden.Contains(r.Key)).ToArray());
    }

    /// <summary>Rows a build leaves off its pages (the shell fills it). Every row still exists for the exactly-once test (AllRows).</summary>
    public static readonly HashSet<string> Hidden = [];

    /// <summary>Every row on every page - the exactly-once test walks this.</summary>
    public IEnumerable<Row> AllRows => EveryRow;
    public static IEnumerable<Row> EveryRow => Pages.SelectMany(p => p.Rows);

    /// <summary>Draws one page on s with the menus' own drawing (Ui.DrawMenuItem, style 0, centred
    /// on 160, the pattern Menus.Draw rules/Menus.cs:105): the group's name as title, each row as
    /// "LABEL: value" plus the effect's mark (a row applied at the next start carries " +", a
    /// new-game row in play " ++"; the full words ran past the 144-px button - settings_a's
    /// pictures). The mark is a plus, NOT a star: the width routine Measure stops at a '*' (the
    /// original's column terminator, Ui.cs:67), so a leading-star note ("* = AFTER RESTART")
    /// measured 0 and drew from the centre, past its frame - seen in Job B's own window picture.
    /// BACK (Menus.CancelHandler, the menus' own cancel), then one note line per mark the page
    /// uses ("+ = AFTER RESTART" / "++ = AT A NEW GAME"). value maps a row's key to its display
    /// text; the keys row ignores it. A page too tall for y 40 starts lower so its bottom row
    /// ends on 200 (PICTURE was the case before smooth_demo went: title + 9 rows + BACK + note).
    /// Returns the buttons: a row's handler
    /// is its index in the page, BACK's is CancelHandler - all offsets from 1, so 0 stays "no
    /// region" as the menus' title and note use.</summary>
    public List<Menus.Button> Draw(IndexedScreen s, string group, Func<string, string> value, bool inGame, bool threeD)
    {
        var (title, rows) = Page(group);
        var items = new List<(Ui.MenuItem Item, int Handler)> { (new Ui.MenuItem(0, 0, title), 0) };
        bool restart = false, newGame = false;
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (!Shown(r.Key, threeD)) continue;                 // its handler stays its index in the page all the same
            var text = r.Key == KeysKey ? r.Label : $"{r.Label}: {value(r.Key)}";
            if (r.Effect == Effect.Restart) { text += " +"; restart = true; }
            if (r.Effect == Effect.NewGame && inGame) { text += " ++"; newGame = true; }
            items.Add((new Ui.MenuItem(0, 0, text), i + 1));
        }
        items.Add((new Ui.MenuItem(0, 0, "BACK"), Menus.CancelHandler));
        if (restart) items.Add((new Ui.MenuItem(0, 0, "+ = AFTER RESTART"), 0));
        if (newGame) items.Add((new Ui.MenuItem(0, 0, "++ = AT A NEW GAME"), 0));

        return Place(s, items);
    }

    /// <summary>The front page: the settings' groups, one row each (the groups' own order), a
    /// row's handler its index in Groups (offset from 1), BACK (CancelHandler) last - the shell
    /// opens the group's page on a row and closes on BACK. extra, when given, is one more row
    /// before BACK (the shell's DEBUG page: its handler is one past the groups, not a group).</summary>
    public List<Menus.Button> DrawFront(IndexedScreen s, string? extra = null)
    {
        var items = new List<(Ui.MenuItem Item, int Handler)> { (new Ui.MenuItem(0, 0, "SETTINGS"), 0) };
        foreach (var g in Groups)
            items.Add((new Ui.MenuItem(0, 0, g), items.Count));
        if (extra is not null) items.Add((new Ui.MenuItem(0, 0, extra), items.Count));
        items.Add((new Ui.MenuItem(0, 0, "BACK"), Menus.CancelHandler));
        return Place(s, items);
    }

    /// <summary>The first start's question, drawn as a settings page: AS IT (KIND OF) WAS is
    /// handler 1, AS IT COULD BE handler 2.</summary>
    public List<Menus.Button> DrawStartChoice(IndexedScreen s) => Place(s,
    [
        (new Ui.MenuItem(0, 0, "HOW WOULD YOU LIKE IT?"), 0),
        (new Ui.MenuItem(0, 0, StartAsItWas), 1),
        (new Ui.MenuItem(0, 0, StartAsItCouldBe), 2),
        (new Ui.MenuItem(0, 0, "CHANGE IT: ESC, SETUP"), 0),
    ]);

    public const string StartAsItWas = "AS IT (KIND OF) WAS", StartAsItCouldBe = "NEW";   // the game as we play it (was AS IT COULD BE)

    /// <summary>Draws the items centred on 160, one under the other - starting low enough that the
    /// last one ends on 200 when the page would run past it (the menus start at 40, Menus.Draw's
    /// callers) - and returns their buttons, all offsets from 1 so 0 stays "no region" as the
    /// menus' title and note use.</summary>
    public List<Menus.Button> Place(IndexedScreen s, List<(Ui.MenuItem Item, int Handler)> items)   // public: the shell's DEBUG page draws with it
    {
        var y = items.Count * 16 > 160 ? Math.Max(4, 200 - items.Count * 16) : 40;   // the menus start at 40 (Menus.Draw's callers)
        s.Clear();                                       // $2016 -> $CA9E, as Menus.Draw
        var buttons = new List<Menus.Button>();
        foreach (var (it, handler) in items)
        {
            var f = _ui.Frames[it.Style + 0x17];
            var x0 = (s.Width >> 1) + it.Dx * 2 - (f.Width >> 1);
            var h = _ui.DrawMenuItem(s, it, y);
            buttons.Add(new Menus.Button(x0, x0 + f.Width, y, y + h, handler, it.Text));
            y += h;
        }
        return buttons;
    }

    readonly Ui _ui;

    /// <summary>The Ui (font + button frames) the pages draw with.</summary>
    public SettingsMenu(Ui ui) => _ui = ui;
}
