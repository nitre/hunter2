namespace Hunter.Rules;

/// <summary>
/// the in-game
/// options as one menu over the paused game. Left, the original's own bars (IN GAME OPTIONS, Resume Game, Re-brief,
/// Disk Operation, then Settings and Quit Game) in the original's bar art and lettering; picking Settings opens the
/// settings beside them, in a box with the same bevel - first the pages, then a page's rows. The four original
/// choices do exactly what the original's do (OptionsScreen.Result); this only changes how they are shown.
///
/// Keys: Up / Down move the pick; Enter or Right goes in a level; Esc or Left comes back out, and Esc at the bars
/// resumes (playtest note 20). On a row Left / Right step its value back / on, Enter steps it on (the keys row opens
/// the key page). The picked bar or row is a lighter olive (note 19); rows are in mixed case (note 16); the
/// original's choice is marked *; one help line says what the picked thing does.
///
/// Drawn at 640 x 400, the original's art one pixel a pixel (not limited to 320 x 200), over the paused
/// game's 320 x 200 picture, as $0RGB words. The settings themselves are SettingsMenu's (pages, values, Next).
/// </summary>
public sealed class OptionsPanel : IMenuPanel
{
    public const int Width = 640, Height = 400;

    public enum Level { Bars, Pages, Rows }

    public enum Outcome { None, Resume, Rebrief, DiskOperation, QuitGame, Changed, OpenKeys, Preset }

    /// <summary>Two lines after the pages that are not pages: every setting at once - AS IT (MOSTLY) WAS puts each row
    /// back to the original game's (the flat picture too), NEW turns the extras on as a new release starts (Preset,
    /// Value = the line).</summary>
    public const string AsItWasPage = "AS IT (MOSTLY) WAS", NewPage = "NEW";
    static bool IsPreset(string page) => page is AsItWasPage or NewPage;

    /// <summary>What a key or click did: an original choice, a setting changed to Value, the key page, or nothing.</summary>
    public sealed record Result(Outcome Kind, string? Key = null, string? Value = null)
    {
        public static readonly Result Nothing = new(Outcome.None);
    }

    /// <summary>The bars, top to bottom, under the title. Settings is the one extra.</summary>
    public static readonly IReadOnlyList<(string Text, Outcome Kind)> Bars =
    [
        ("Resume Game", Outcome.Resume), ("Re-brief", Outcome.Rebrief), ("Disk Operation", Outcome.DiskOperation),
        ("Settings", Outcome.None), ("Quit Game", Outcome.QuitGame),
    ];
    public const int SettingsBar = 3;
    public const string Title = "IN GAME OPTIONS";        // the original's own title, in its capitals

    readonly SettingsMenu _settings;
    readonly Func<string, string> _value;
    readonly bool _threeD;

    public Level At { get; private set; } = Level.Bars;
    public int Bar { get; private set; }
    public int PageIndex { get; private set; }
    public int RowIndex { get; private set; }             // among the page's shown rows

    /// <param name="value">a row's key to its current word (as SettingsMenu.Values spells it)</param>
    /// <param name="threeD">the 3D world on: which VIEW DISTANCE shows, and whether the 3D-only rows can change</param>
    public OptionsPanel(SettingsMenu settings, Func<string, string> value, bool threeD)
    {
        _settings = settings;
        _value = value;
        _threeD = threeD;
    }

    public IReadOnlyList<string> Pages => [.. _settings.Groups, AsItWasPage, NewPage];

    /// <summary>The rows a page shows (SettingsMenu.Shown: one VIEW DISTANCE, no two-player rows); none on the two
    /// all-at-once lines.</summary>
    public IReadOnlyList<SettingsMenu.Row> Rows(int page) => IsPreset(Pages[page]) ? [] :
        _settings.Page(Pages[page]).Rows.Where(r => SettingsMenu.Shown(r.Key, _threeD)).ToList();

    public SettingsMenu.Row? PickedRow => At == Level.Rows ? Rows(PageIndex)[RowIndex] : null;

    // ---- keys ----

    public Result Up() { Move(-1); return Result.Nothing; }
    public Result Down() { Move(1); return Result.Nothing; }

    void Move(int d)
    {
        switch (At)
        {
            case Level.Bars: Bar = Wrap(Bar + d, Bars.Count); break;
            case Level.Pages: PageIndex = Wrap(PageIndex + d, Pages.Count); break;
            case Level.Rows: RowIndex = Wrap(RowIndex + d, Rows(PageIndex).Count); break;
        }
    }

    static int Wrap(int i, int n) => ((i % n) + n) % n;

    public Result Enter()
    {
        switch (At)
        {
            case Level.Bars:
                if (Bar != SettingsBar) return new Result(Bars[Bar].Kind);
                At = Level.Pages;
                return Result.Nothing;
            case Level.Pages:
                if (IsPreset(Pages[PageIndex])) return new Result(Outcome.Preset, Value: Pages[PageIndex]);   // every setting at once; the pick stays here
                At = Level.Rows;
                RowIndex = 0;
                return Result.Nothing;
            default:
                return PickedRow!.Key == SettingsMenu.KeysKey ? new Result(Outcome.OpenKeys) : Step(1);
        }
    }

    public Result Right() => At == Level.Rows ? (PickedRow!.Key == SettingsMenu.KeysKey ? Result.Nothing : Step(1))
                           : At == Level.Bars && Bar != SettingsBar ? Result.Nothing : Enter();

    public Result Left() => At == Level.Rows && PickedRow!.Key != SettingsMenu.KeysKey ? Step(-1) : Back();

    /// <summary>Esc: back a level; at the bars, resume the game.</summary>
    public Result Back()
    {
        switch (At)
        {
            case Level.Rows: At = Level.Pages; return Result.Nothing;
            case Level.Pages: At = Level.Bars; Bar = SettingsBar; return Result.Nothing;
            default: return new Result(Outcome.Resume);
        }
    }

    Result Step(int d)
    {
        var key = PickedRow!.Key;
        if (!_threeD && SettingsMenu.ThreeDOnly.Contains(key)) return Result.Nothing;   // shown as 3D ONLY: nothing to change
        var current = _value(key);
        var next = d > 0 ? SettingsMenu.Next(key, current) : Previous(key, current);
        return new Result(Outcome.Changed, key, next);
    }

    /// <summary>The value before `current` (wraps to the last), SettingsMenu.Next's mirror.</summary>
    public static string Previous(string key, string current)
    {
        var v = SettingsMenu.Values.TryGetValue(key, out var w) ? w : ["OFF", "ON"];
        var i = Array.IndexOf(v, current);
        return v[i < 0 ? 0 : (i - 1 + v.Length) % v.Length];
    }

    object? IMenuPanel.Up() => Up();
    object? IMenuPanel.Down() => Down();
    object? IMenuPanel.Left() => Left();
    object? IMenuPanel.Right() => Right();
    object? IMenuPanel.Enter() => Enter();
    object? IMenuPanel.Back() => Back();
    object? IMenuPanel.Click(int x, int y) => Click(x, y);

    // ---- the original's choice ----

    /// <summary>Rows that are a choice between the original game and an extra: the extras (SettingsMenu.Recorded) and
    /// the Amiga's own look and sound filter. Window and control rows (pixel size, fullscreen, text size, the buttons)
    /// have no original, so no mark.</summary>
    public static string? OriginalOf(string key)
    {
        if (key is "gpu_scale") return null;                                  // the window's pixel size: no original
        if (!SettingsMenu.Recorded.ContainsKey(key) && key is not ("look" or "a500_filter")) return null;
        return SettingsMenu.Values.TryGetValue(key, out var v) ? v[0] : "OFF";  // the original's value always first (playtest 15)
    }

    // ---- words ----

    static readonly HashSet<string> KeepCaps = ["TV", "3D", "AGA", "PAL", "A500", "A1200", "F2", "4:3"];

    /// <summary>A label or value in mixed case (playtest note 16): the first letter capital, the rest small, the
    /// shorthand that is capitals anyway kept (TV, 3D, AGA, 2X ...).</summary>
    public static string Mixed(string s)
    {
        var words = s.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (KeepCaps.Contains(w) || w.Any(char.IsDigit)) continue;
            w = w.ToLowerInvariant();
            if (i == 0 && w.Length > 0) w = char.ToUpperInvariant(w[0]) + w[1..];
            words[i] = w;
        }
        return string.Join(' ', words);
    }

    /// <summary>The help line for a bar.</summary>
    public static string BarHelp(int bar) => bar switch
    {
        0 => "Back to the game, exactly where you left it.",
        1 => "Hear the briefing again and start this mission over.",
        2 => "Load or save a game.",
        3 => "How the game looks, sounds and plays.",
        _ => "Leave this soldier's career (it asks first).",
    };

    /// <summary>The help line for a page.</summary>
    public static string PageHelp(string page) => page switch
    {
        "SCREEN" => "The window and the picture: TV look, brightness, the 3D world.",
        "LAND AND SKY" => "How far you see, the ground, the sky and the sea.",
        "MODELS" => "The vehicles, people and buildings: colours, shadows, explosions.",
        "WORLD" => "What fills the islands and how it behaves. New games only.",
        "SOUND" => "The Amiga's sound filter, the extra sound and the music.",
        "CONTROLS" => "Buttons, the message log and the keys.",
        AsItWasPage => "Every setting back to the original game's, the flat picture too.",
        NewPage => "Every extra on, as the game starts the first time.",
        _ => "",
    };

    /// <summary>Every row's help line, in plain words (playtest: names say the topic, the help says what it does).</summary>
    public static readonly IReadOnlyDictionary<string, string> Help = new Dictionary<string, string>
    {
        ["look"] = "The picture on a PAL TV as the Amiga showed it, centred, or stretched to fill.",
        ["brightness"] = "Lighter or darker, as a TV's knob (also F7).",
        ["fullscreen"] = "The whole screen or a window.",
        ["text_size"] = "Bigger letters on the help and key screens (also F8).",
        ["renderer"] = "Off: the original's flat picture. On: the world drawn in real 3D.",
        ["gpu_scale"] = "How big each pixel of the 3D picture is. Auto follows the window.",
        ["wide_view"] = "The original's 4:3 picture, or the 3D view across the whole width.",
        ["far_view"] = "How far the flat picture draws the land.",
        ["view_distance"] = "How far the 3D world draws. Original: the 7 rows the Amiga drew.",
        ["ground"] = "The land: sun shading, colour bands, specks, finer pieces.",
        ["sky"] = "The sky: smooth colours and daylight, clouds, haze in the distance.",
        ["world"] = "More things on the islands, people and animals that notice each other, graves.",
        ["sound"] = "Sounds left and right, more at once. After a restart.",
        ["living_sea"] = "A moving sea: pale by the coast, waves, or the Amiga's colour cycling.",
        ["own_colours"] = "Helicopters, tanks and soldiers in their own shades, apart from the grass.",
        ["glass"] = "Windows of vehicles and buildings drawn as see-through glass, not painted panels.",
        ["sea_bodies"] = "Sharks and submarines with whole bodies under the water. At the next start.",
        ["smooth_animation"] = "Legs, arms and moving parts move a little every frame, not in jumps.",
        ["demo_camera"] = "The title demo's camera turns calmly round to show what happens.",
        ["demo_world"] = "The title demo in a busy world: herds, trees, boats along the way.",
        ["own_shadows"] = "Every thing casts a shadow of its own shape.",
        ["card_pieces"] = "Things blow apart into their own panels, not ten triangles. Lasting: the pieces stay where they fall.",
        ["finer_ground"] = "The land grown finer between the map's points; 2X and up also make the hills taller. At a new game.",
        ["ground_seed"] = "The same hills between the map's points every game, or new ones each new game.",
        ["leave_behind"] = "Step out behind a vehicle, not on its east side: a moving boat will not run you down.",
        ["aiming"] = "On foot the soldier turns to a reticle you move with the mouse or right stick, and shoots at the point under it.",
        ["bullet_box"] = "Bullets hit only what they fly into. The original's jam on north-south lines.",
        ["analog_steering"] = "A gamepad stick turns a vehicle by as much as it leans. Keys and d-pad turn fully; on foot is the same.",
        ["a500_filter"] = "The Amiga 500's soft sound, or the clearer Amiga 1200's.",
        ["message_log"] = "Keep the last messages to read again.",
        ["pause_on_focus_loss"] = "Pause when you click away from the game.",
        ["manual_button"] = "A Manual button on the main menu.",
        ["quit_button"] = "A Quit button on the main menu.",
        [SettingsMenu.KeysKey] = "Choose your own keys and gamepad buttons.",
        ["players"] = "One or two players.",
        ["split_layout"] = "Two players: side by side or one above the other.",
    };

    /// <summary>Values whose meaning differs from the row's others: their own help line, said instead of the row's
    /// while that value is on.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ValueHelp =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["look"] = new Dictionary<string, string>
        {
            ["PAL"] = "A PAL TV as the Amiga showed it: the picture at the top, black below.",
            ["CENTRED"] = "The Amiga's picture in the middle of the screen.",
            ["STRETCHED"] = "The picture stretched to fill, square pixels.",
        },
        ["renderer"] = new Dictionary<string, string>
        {
            ["OFF"] = "The original's own flat picture, drawn as the Amiga drew it.",
            ["ON"] = "The world drawn in real 3D, at your screen's own resolution.",
        },
        ["gpu_scale"] = new Dictionary<string, string> { ["AUTO"] = "Each pixel of the 3D picture as big as the window needs." },
        ["wide_view"] = new Dictionary<string, string>
        {
            ["4:3"] = "The original's picture shape.",
            ["WIDE"] = "The 3D view across your screen's whole width.",
        },
        ["view_distance"] = new Dictionary<string, string>
        {
            ["ORIGINAL"] = "The 7 rows of land ahead the Amiga drew.",
            ["ALL"] = "The whole map, as far as the eye can see.",
        },
        ["living_sea"] = new Dictionary<string, string>
        {
            ["ORIGINAL"] = "The sea in one flat colour, as the original.",
            ["LIVING"] = "Pale by the coast, slow waves, the sky mirrored near the horizon.",
            ["CYCLING"] = "Blocks of sea whose colours glide round, the Amiga's colour cycling.",
        },
        ["finer_ground"] = new Dictionary<string, string>
        {
            ["ORIGINAL"] = "The map's own points, flat-sided squares, as the original.",
            ["FINER"] = "Gentle rolls between the map's points, the hills as tall as the original's. At a new game.",
            ["2X"] = "Gentle rolls between the map's points, hills 1.5x as tall. At a new game.",
            ["HILLS"] = "Rougher ground between the points, hills twice as tall. At a new game.",
            ["CLIFFS"] = "Craggy ground, hills three times as tall: steep faces, hard falls. At a new game.",
        },
        ["ground_seed"] = new Dictionary<string, string>
        {
            ["WORLD"] = "The world's own seed: the same hills every game.",
            ["RANDOM"] = "New hills between the map's points each new game; the seed is kept for LAST.",
            ["LAST"] = "The last random hills again, or the seed typed into the settings file.",
        },
        ["a500_filter"] = new Dictionary<string, string>
        {
            ["A500"] = "The Amiga 500's soft sound (its filter always on).",
            ["A1200"] = "The Amiga 1200's clearer sound.",
        },
        // the merged rows (SettingsMenu.Merged): each step says what it turns on; CUSTOM is a hand-edited mix
        ["ground"] = new Dictionary<string, string>
        {
            ["ORIGINAL"] = "The original's flat paint, the ground following the land's real height.",
            ["LIT"] = "The sun lights the land, sand, grass, hills and rock in their own colours.",
            ["FULL"] = "Sun, colours, fine specks in every square, the ground in the finest pieces.",
        },
        ["sky"] = new Dictionary<string, string>
        {
            ["ORIGINAL"] = "The original's sky bands, daylight changing every hour, no haze.",
            ["SMOOTH"] = "A smooth sky, daylight changing gradually, clouds, far land fading in steps.",
            ["FULL"] = "A smooth sky, gradual daylight, clouds, far land fading smoothly.",
        },
        ["world"] = new Dictionary<string, string>
        {
            ["ORIGINAL"] = "The islands as the original filled them, no graves.",
            ["SOME"] = "A few more of the map's own things, graves where people fell. At a new game.",
            ["MORE"] = "More things, room for them, a living world, graves with vultures. At a new game.",
            ["LOTS"] = "As MORE, the islands filled with the most the map's own things can be.",
        },
        ["sound"] = new Dictionary<string, string>
        {
            ["ORIGINAL"] = "The Amiga's four sounds, as the original.",
            ["ENHANCED"] = "Sounds left and right, softer cuts, music in play, none drop out. After a restart.",
        },
    };

    /// <summary>What the help line says now.</summary>
    public string HelpLine => At switch
    {
        Level.Bars => BarHelp(Bar),
        Level.Pages => PageHelp(Pages[PageIndex]),
        _ => ValueHelp.TryGetValue(PickedRow!.Key, out var vh) && vh.TryGetValue(_value(PickedRow.Key), out var v) ? v
           : Help.TryGetValue(PickedRow.Key, out var h) ? h : "",
    };

    /// <summary>What a row shows as its value: its word in mixed case; 3D ONLY with the 3D world off; nothing for keys.</summary>
    public string Shown(SettingsMenu.Row r)
    {
        if (r.Key == SettingsMenu.KeysKey) return "F2";
        if (!_threeD && SettingsMenu.ThreeDOnly.Contains(r.Key)) return "3D only";
        return Mixed(_value(r.Key));
    }

    /// <summary>A row's label without the old "- USE F2" hint (the row itself opens the keys now).</summary>
    public static string Label(SettingsMenu.Row r) => Mixed(r.Key == SettingsMenu.KeysKey ? "KEYS" : r.Label);

    // ---- where things are (640 x 400) ----

    public const int BarX = 12, BarW = 168, BarY = 30, BarStep = 18;
    public const int BoxX = 192, BoxW = 436, BoxTitleY = 30, BoxY = 48, RowY = 58, RowStep = 16;
    public const int HelpY = 377;

    public static (int X0, int Y0, int X1, int Y1) BarBox(int bar) => (BarX, BarY + BarStep * (bar + 1), BarX + BarW, BarY + BarStep * (bar + 1) + 16);
    public static (int X0, int Y0, int X1, int Y1) LineBox(int line) => (BoxX + 4, RowY + RowStep * line - 4, BoxX + BoxW - 4, RowY + RowStep * line + 12);

    bool BoxShown => At != Level.Bars || Bar == SettingsBar;

    int _lessX = -1;                                       // where the picked row's "<" was last drawn (-1: none)

    /// <summary>The picked row's "<", as last drawn: a click there steps the value back.</summary>
    public (int X0, int X1)? LessAt => _lessX < 0 ? null : (_lessX - 2, _lessX + 8);

    // ---- the mouse ----

    /// <summary>The pointer over a bar, page or row picks it (and moves the level there).</summary>
    public void Hover(int x, int y)
    {
        for (var b = 0; b < Bars.Count; b++)
        {
            var (x0, y0, x1, y1) = BarBox(b);
            if (x >= x0 && x < x1 && y >= y0 && y < y1) { At = Level.Bars; Bar = b; return; }
        }
        if (!BoxShown) return;
        var n = At == Level.Rows ? Rows(PageIndex).Count : Pages.Count;
        for (var i = 0; i < n; i++)
        {
            var (x0, y0, x1, y1) = LineBox(i);
            if (x < x0 || x >= x1 || y < y0 || y >= y1) continue;
            if (At == Level.Rows) RowIndex = i;
            else { At = Level.Pages; PageIndex = i; }
            return;
        }
    }

    /// <summary>A click: hover there, then Enter on whatever it picked (on a bar, page or row; elsewhere nothing).</summary>
    public Result Click(int x, int y)
    {
        var before = (At, Bar, PageIndex, RowIndex);
        Hover(x, y);
        var on = OnSomething(x, y);
        if (!on) { (At, Bar, PageIndex, RowIndex) = before; return Result.Nothing; }
        // on the picked row's "<": back a value; anywhere else on a row (its ">" too): on a value
        if (At == Level.Rows && before.At == Level.Rows && before.RowIndex == RowIndex && LessAt is { } l && x >= l.X0 && x < l.X1
            && PickedRow!.Key != SettingsMenu.KeysKey)
            return Step(-1);
        return Enter();
    }

    bool OnSomething(int x, int y)
    {
        for (var b = 0; b < Bars.Count; b++)
        {
            var (x0, y0, x1, y1) = BarBox(b);
            if (x >= x0 && x < x1 && y >= y0 && y < y1) return true;
        }
        if (!BoxShown) return false;
        var n = At == Level.Rows ? Rows(PageIndex).Count : Pages.Count;
        for (var i = 0; i < n; i++)
        {
            var (x0, y0, x1, y1) = LineBox(i);
            if (x >= x0 && x < x1 && y >= y0 && y < y1) return true;
        }
        return false;
    }

    // ---- drawing ----

    /// <summary>A layer pixel the game shows through (no $0RGB colour has bit 15).</summary>
    public const ushort Clear = 0x8000;

    /// <summary>How big the window shows the menu: the largest whole number of times 640 x 400 fits (at least 1),
    /// so its pixels stay even and sharp; centred. Nothing ties it to 320 x 200.</summary>
    public static int ScaleFor(int windowW, int windowH) => Math.Max(1, Math.Min(windowW / Width, windowH / Height));

    /// <summary>Where the scaled menu's top-left corner sits in the window (centred).</summary>
    public static (int X, int Y) OriginFor(int windowW, int windowH)
    {
        var k = ScaleFor(windowW, windowH);
        return ((windowW - Width * k) / 2, (windowH - Height * k) / 2);
    }

    /// <summary>A window point in the menu's own 640 x 400 coordinates (for Hover / Click).</summary>
    public static (int X, int Y) ToMenu(int windowX, int windowY, int windowW, int windowH)
    {
        var k = ScaleFor(windowW, windowH);
        var (ox, oy) = OriginFor(windowW, windowH);
        return ((int)Math.Floor((windowX - ox) / (double)k), (int)Math.Floor((windowY - oy) / (double)k));
    }

    /// <summary>The menu alone, for the window to lay over the real paused picture (dimmed there): 640 x 400 $0RGB,
    /// Clear wherever the game shows through. bars: the original's options screen as drawn (Menus.Options().Screen)
    /// and bank5 its colours - the bar art and its lettering trick come from there; ui: the game font.</summary>
    public ushort[] DrawLayer(IndexedScreen bars, IReadOnlyList<ushort> bank5, Ui ui)
    {
        var px = new ushort[Width * Height];
        Array.Fill(px, Clear);
        DrawOn(px, bars, bank5, ui);
        return px;
    }

    /// <summary>The menu over a paused 320 x 200 picture ($0RGB, Play.Screen), drawn twice the size and dimmed to
    /// 3/10 - the pictures for checking and the tests; the window uses DrawLayer over its own picture.</summary>
    public ushort[] Draw(ushort[] paused, IndexedScreen bars, IReadOnlyList<ushort> bank5, Ui ui)
    {
        var px = DrawLayer(bars, bank5, ui);
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                if (px[y * Width + x] == Clear) px[y * Width + x] = Scale(paused[(y >> 1) * 320 + (x >> 1)], 3, 10);
        return px;
    }

    void DrawOn(ushort[] px, IndexedScreen bars, IReadOnlyList<ushort> bank5, Ui ui)
    {
        var art = new BarArt(bars, bank5);
        var g = new Paint(px, ui);

        art.Draw(g, BarX, BarY, BarW, Title, null);
        for (var b = 0; b < Bars.Count; b++)
            art.Draw(g, BarX, BarY + BarStep * (b + 1), BarW, Bars[b].Text, At == Level.Bars && Bar == b || At != Level.Bars && b == SettingsBar ? Picked : null);

        if (BoxShown)
        {
            var page = Pages[PageIndex];
            art.Draw(g, BoxX, BoxTitleY, BoxW, At == Level.Rows ? "Settings: " + Mixed(page) : "Settings", null);
            var lines = At == Level.Rows ? Rows(PageIndex).Count : Pages.Count;
            var marked = At == Level.Rows && Rows(PageIndex).Any(r => OriginalOf(r.Key) is { } o0 && _value(r.Key) == o0 && Shown(r) != "3D only");
            var boxH = RowY - BoxY + RowStep * lines + (marked ? 22 : 4);   // fits its lines, and the footnote when there is one
            g.Box(BoxX, BoxY, BoxW, boxH, bank5);
            if (At == Level.Rows)
            {
                var rows = Rows(PageIndex);
                var anyOriginal = false;
                for (var i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    var y = RowY + RowStep * i;
                    var picked = i == RowIndex;
                    if (picked) g.Fill(LineBox(i), RowLit);
                    g.Text(BoxX + 12, y, Label(r), picked ? TextBright : TextSoft);
                    var v = Shown(r);
                    var isOriginal = OriginalOf(r.Key) is { } o && _value(r.Key) == o && v != "3D only";
                    if (picked && r.Key != SettingsMenu.KeysKey && v != "3D only") v = "< " + v + " >";
                    var vx = BoxX + BoxW - 14 - g.Measure(v);
                    if (picked) _lessX = v.StartsWith("< ") ? vx : -1;
                    g.Text(vx, y, v, picked ? White : ValueBlue);
                    if (isOriginal) { g.Text(vx - 10, y, "*", Star); anyOriginal = true; }
                }
                if (anyOriginal) g.Text(BoxX + 12, BoxY + boxH - 16, "* = what the original game did", TextDim);
            }
            else
            {
                for (var i = 0; i < Pages.Count; i++)
                {
                    var y = RowY + RowStep * i;
                    var picked = At == Level.Pages && i == PageIndex;
                    if (picked) g.Fill(LineBox(i), RowLit);
                    g.Text(BoxX + 12, y, Mixed(Pages[i]), picked ? TextBright : TextSoft);
                    if (!IsPreset(Pages[i])) g.Text(BoxX + BoxW - 14 - g.Measure(">"), y, ">", picked ? White : TextDim);   // a page opens; the two all-at-once lines act
                }
            }
        }
        g.Text(16, HelpY, HelpLine, TextSoft);
    }

    // the colours, $0RGB, from the original's own families (the bar's olives, its lettering's pale blue)
    internal const ushort PickedOlive = 0x884;
    const ushort Picked = PickedOlive, RowLit = 0x553, TextBright = 0xBDF, TextSoft = 0xBB9, TextDim = 0x886,
                 White = 0xFFF, ValueBlue = 0xBDF, Star = 0xA84, BoxFill = 0x221;

    static ushort Scale(ushort c, int num, int den) =>
        (ushort)(((c >> 8 & 15) * num / den) << 8 | ((c >> 4 & 15) * num / den) << 4 | (c & 15) * num / den);

    /// <summary>The original options screen's bar, at any width: its left edge, one clean middle column repeated,
    /// its right edge (Resume Game's bar, rows 56-71, x 88-95 / 100 / 222-231 of $216C's screen). Its lettering as
    /// the original's: a glyph pixel sets plane 0 (index | 1), so each row of the olive body turns to its own shade
    /// of the text's white-to-blue.</summary>
    internal sealed class BarArt
    {
        readonly byte[][] _left = new byte[16][], _right = new byte[16][];
        readonly byte[] _mid = new byte[16];
        readonly IReadOnlyList<ushort> _bank;

        public BarArt(IndexedScreen s, IReadOnlyList<ushort> bank)
        {
            _bank = bank;
            for (var r = 0; r < 16; r++)
            {
                _left[r] = Enumerable.Range(88, 8).Select(x => s.Pixels[(56 + r) * s.Width + x]).ToArray();
                _right[r] = Enumerable.Range(222, 10).Select(x => s.Pixels[(56 + r) * s.Width + x]).ToArray();
                _mid[r] = s.Pixels[(56 + r) * s.Width + 100];
            }
        }

        public void Draw(Paint g, int x, int y, int w, string text, ushort? body)
        {
            var idx = new byte[16, w];
            for (var r = 0; r < 16; r++)
                for (var c = 0; c < w; c++)
                    idx[r, c] = c < 8 ? _left[r][c] : c >= w - 10 ? _right[r][c - (w - 10)] : _mid[r];
            var cx = (w - g.Measure(text)) / 2;
            foreach (var ch in text)
            {
                var (glyph, gw) = g.Glyph(ch);
                for (var line = 0; line < 8; line++)
                    for (var i = 0; i <= gw && i < 8; i++)
                        if ((glyph[line] & (0x80 >> i)) != 0 && cx + i >= 0 && cx + i < w) idx[4 + line, cx + i] |= 1;
                cx += gw + 1;
            }
            for (var r = 0; r < 16; r++)
                for (var c = 0; c < w; c++)
                {
                    var k = idx[r, c];
                    if (k == 0) continue;                                // the bar's own black corners: the game shows through
                    g.Put(x + c, y + r, body is { } b && k is 8 or 10 or 12 or 14 ? b : _bank[k]);
                }
        }
    }

    /// <summary>Plain drawing on the $0RGB picture: the game font, filled lines, the bevelled box.</summary>
    internal sealed class Paint(ushort[] px, Ui ui)
    {
        public void Put(int x, int y, ushort c) { if ((uint)x < Width && (uint)y < Height) px[y * Width + x] = c; }

        public (byte[] Glyph, int Width) Glyph(char ch) => ch < ui.Glyphs.Length ? (ui.Glyphs[ch], ui.Widths[ch]) : (ui.Glyphs['?'], ui.Widths['?']);

        public int Measure(string t) => t.Sum(ch => Glyph(ch).Width + 1);

        public void Text(int x, int y, string t, ushort c)
        {
            foreach (var ch in t)
            {
                var (glyph, gw) = Glyph(ch);
                for (var line = 0; line < 8; line++)
                    for (var i = 0; i <= gw && i < 8; i++)
                        if ((glyph[line] & (0x80 >> i)) != 0) Put(x + i, y + line, c);
                x += gw + 1;
            }
        }

        public void Fill((int X0, int Y0, int X1, int Y1) r, ushort c)
        {
            for (var y = r.Y0; y < r.Y1; y++) for (var x = r.X0; x < r.X1; x++) Put(x, y, c);
        }

        /// <summary>A box in the bar's own bevel colours: dark outline, light top-left, dark bottom-right, a dark olive
        /// inside that lets the game show only faintly through.</summary>
        public void Box(int x, int y, int w, int h, IReadOnlyList<ushort> bank)
        {
            for (var yy = y; yy < y + h; yy++)
                for (var xx = x; xx < x + w; xx++)
                {
                    ushort c;
                    if (xx == x || xx == x + w - 1 || yy == y || yy == y + h - 1) c = bank[7];
                    else if (xx == x + 1 || yy == y + 1) c = bank[1];
                    else if (xx == x + w - 2 || yy == y + h - 2) c = bank[6];
                    else
                    {
                        var under = (uint)xx < Width && (uint)yy < Height ? px[yy * Width + xx] : Clear;
                        c = under == Clear ? BoxFill : Mix(under, BoxFill);
                    }
                    Put(xx, yy, c);
                }
        }

        static ushort Mix(ushort a, ushort b)   // 1/16 the game, 15/16 the box's olive: the text reads clearly
        {
            int Ch(int s) => ((a >> s & 15) + 15 * (b >> s & 15)) / 16;
            return (ushort)(Ch(8) << 8 | Ch(4) << 4 | Ch(0));
        }
    }
}
