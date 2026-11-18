namespace Hunter.Rules;

/// <summary>ENHANCEMENT: a menu drawn as its own layer over the window (options design B and the first start's
/// question): its keys and mouse, and its 640 x 400 picture, OptionsPanel.Clear where what is behind shows through.
/// Each answer is the menu's own (null: nothing chosen yet); godot/OptionsOverlay.cs shows any of them.</summary>
public interface IMenuPanel
{
    object? Up();
    object? Down();
    object? Left();
    object? Right();
    object? Enter();
    object? Back();
    void Hover(int x, int y);
    object? Click(int x, int y);
    ushort[] DrawLayer(IndexedScreen bars, IReadOnlyList<ushort> bank5, Ui ui);
}

/// <summary>
/// ENHANCEMENT (options design B, part 2): the first start's question - AS IT (KIND OF) WAS or AS IT COULD BE
/// (owner 2026-11-02, SettingsMenu.DrawStartChoice) - as B's menu.
///
/// First the QUESTION: the question and its two answers as the original's bars on the left; beside them, in B's box,
/// what the picked answer gives, setting by setting (the original's marked *). AS IT (KIND OF) WAS answers at once.
/// AS IT COULD BE goes on to its own settings (owner 2026-11-01: "As it could be still needs its own subs"; the
/// question is not needed as the left column once chosen): the TUNE screen - the settings' pages as the left column,
/// with START at its foot, and the picked page's rows in the box beside them, already set to the extras, each changed
/// with < >. Esc steps back: out of a page to the column, and from the column back to the question.
///
/// Answers (what Enter, a click or a key gives back): 1 - as it was, done; 2 - as it could be, START; a Tune - the
/// question was answered AS IT COULD BE (the shell turns its extras on now, so the pages show them); an
/// OptionsPanel.Result - a row changed (Changed) or the keys row (OpenKeys); null - nothing.
/// </summary>
public sealed class StartChoicePanel : IMenuPanel
{
    public const string Title = "How would you like it?";
    public static readonly IReadOnlyList<string> Answers = ["As it (kind of) was", "New"];   // was "As it could be": the game as we play it (owner 2026-11-03)
    public const string StartBar = "Start";
    public const int ListStep = 10;                                  // the question's list, closer than B's pages so all fit (8-high letters; 13 held 21 rows, 12 held 23, GLASS and LEAVING made 25, 11 held 25, LIMB DAMAGE made 26)

    /// <summary>The question answered AS IT COULD BE: the tune screen opens.</summary>
    public sealed record Tune;

    public enum Stage { Question, Column, Rows }

    /// <summary>What each answer gives, in plain words (the help line).</summary>
    public static readonly IReadOnlyList<string> Help =
    [
        "The original game, as the Amiga played it (a few fixes always on).",
        "The game as we play it: real 3D, clouds, a living sea, glass and more.",
    ];
    public const string Later = "Change any of it later: Esc in a game, then Settings.";

    readonly SettingsMenu? _settings;
    readonly Func<string, string>? _value;

    public Stage At { get; private set; } = Stage.Question;
    public int Picked { get; private set; }                          // the question's answer picked
    public int ColumnLine { get; private set; }                      // the tune column: its pages, then Start
    public int RowIndex { get; private set; }

    /// <summary>The question alone (pictures, tests): no tune screen behind it.</summary>
    public StartChoicePanel() { }

    /// <param name="value">a row's key to its current word (the shell's live settings)</param>
    public StartChoicePanel(SettingsMenu settings, Func<string, string> value)
    {
        _settings = settings;
        _value = value;
    }

    /// <summary>The settings the answer decides: every row AS IT COULD BE sets that has an original (pixel size does not).</summary>
    public static IReadOnlyList<SettingsMenu.Row> Decided { get; } = SettingsMenu.EveryRow
        .Where(r => SettingsMenu.AsItCouldBe.ContainsKey(r.Key) && OptionsPanel.OriginalOf(r.Key) is not null).ToList();

    /// <summary>A decided setting's value under an answer (0: as it was - the original's; 1: as it could be).</summary>
    public static string ValueUnder(int answer, string key) => answer == 0 ? OptionsPanel.OriginalOf(key)! : SettingsMenu.AsItCouldBe[key];

    bool ThreeD => _value?.Invoke("renderer") == "ON";
    public IReadOnlyList<string> Pages => _settings?.Groups ?? [];
    public IReadOnlyList<SettingsMenu.Row> Rows(int page) =>
        _settings!.Page(Pages[page]).Rows.Where(r => SettingsMenu.Shown(r.Key, ThreeD)).ToList();
    int ColumnCount => Pages.Count + 1;                              // the pages, then Start
    bool OnStart => ColumnLine == Pages.Count;
    int ShownPage => OnStart ? 0 : ColumnLine;                       // Start shows the first page beside it
    public SettingsMenu.Row? PickedRow => At == Stage.Rows ? Rows(ColumnLine)[RowIndex] : null;

    // ---- keys ----

    public object? Up() { Move(-1); return null; }
    public object? Down() { Move(1); return null; }

    void Move(int d)
    {
        switch (At)
        {
            case Stage.Question: Picked = ((Picked + d) % 2 + 2) % 2; break;
            case Stage.Column: ColumnLine = ((ColumnLine + d) % ColumnCount + ColumnCount) % ColumnCount; break;
            default: var n = Rows(ColumnLine).Count; RowIndex = ((RowIndex + d) % n + n) % n; break;
        }
    }

    public object? Enter()
    {
        switch (At)
        {
            case Stage.Question:
                if (Picked == 0 || _settings is null) return Picked + 1;
                At = Stage.Column;
                ColumnLine = 0;
                return new Tune();
            case Stage.Column:
                if (OnStart) return 2;
                At = Stage.Rows;
                RowIndex = 0;
                return null;
            default:
                return PickedRow!.Key == SettingsMenu.KeysKey ? new OptionsPanel.Result(OptionsPanel.Outcome.OpenKeys) : Step(1);
        }
    }

    public object? Right() => At switch
    {
        Stage.Rows => PickedRow!.Key == SettingsMenu.KeysKey ? null : Step(1),
        Stage.Column when !OnStart => Enter(),
        _ => null,
    };

    public object? Left() => At == Stage.Rows && PickedRow!.Key != SettingsMenu.KeysKey ? Step(-1) : Back();

    /// <summary>Esc: out of a page to the column, from the column back to the question; the question must be answered.</summary>
    public object? Back()
    {
        switch (At)
        {
            case Stage.Rows: At = Stage.Column; break;
            case Stage.Column: At = Stage.Question; Picked = 1; break;
        }
        return null;
    }

    object? Step(int d)
    {
        var key = PickedRow!.Key;
        if (!ThreeD && SettingsMenu.ThreeDOnly.Contains(key)) return null;
        var current = _value!(key);
        return new OptionsPanel.Result(OptionsPanel.Outcome.Changed, key,
            d > 0 ? SettingsMenu.Next(key, current) : OptionsPanel.Previous(key, current));
    }

    // ---- where things are ----

    /// <summary>A bar in the left column (the question's answers, or the tune screen's pages and Start): under its title bar.</summary>
    public static (int X0, int Y0, int X1, int Y1) BarBox(int i) => OptionsPanel.BarBox(i);

    int BarCount => At == Stage.Question ? 2 : ColumnCount;

    public void Hover(int x, int y)
    {
        for (var i = 0; i < BarCount; i++)
        {
            var (x0, y0, x1, y1) = BarBox(i);
            if (x < x0 || x >= x1 || y < y0 || y >= y1) continue;
            if (At == Stage.Question) Picked = i;
            else { At = Stage.Column; ColumnLine = i; }
            return;
        }
        if (At == Stage.Question || OnStart) return;
        var rows = Rows(ColumnLine).Count;
        for (var i = 0; i < rows; i++)
        {
            var (x0, y0, x1, y1) = OptionsPanel.LineBox(i);
            if (x < x0 || x >= x1 || y < y0 || y >= y1) continue;
            At = Stage.Rows;
            RowIndex = i;
            return;
        }
    }

    int _lessX = -1;                                                 // the picked row's "<", as last drawn

    public object? Click(int x, int y)
    {
        var before = (At, RowIndex);
        for (var i = 0; i < BarCount; i++)
        {
            var (x0, y0, x1, y1) = BarBox(i);
            if (x < x0 || x >= x1 || y < y0 || y >= y1) continue;
            Hover(x, y);
            return At == Stage.Question ? Enter() : OnStart ? 2 : null;   // a page's bar shows it beside; Start starts
        }
        if (At == Stage.Question || OnStart) return null;
        var rows = Rows(ColumnLine).Count;
        for (var i = 0; i < rows; i++)
        {
            var (x0, y0, x1, y1) = OptionsPanel.LineBox(i);
            if (x < x0 || x >= x1 || y < y0 || y >= y1) continue;
            Hover(x, y);
            if (PickedRow!.Key == SettingsMenu.KeysKey) return Enter();
            var onLess = before.At == Stage.Rows && before.RowIndex == RowIndex && _lessX >= 0 && x >= _lessX - 2 && x < _lessX + 8;
            return Step(onLess ? -1 : 1);
        }
        return null;
    }

    // ---- drawing ----

    public ushort[] DrawLayer(IndexedScreen bars, IReadOnlyList<ushort> bank5, Ui ui)
    {
        var px = new ushort[OptionsPanel.Width * OptionsPanel.Height];
        Array.Fill(px, OptionsPanel.Clear);
        var art = new OptionsPanel.BarArt(bars, bank5);
        var g = new OptionsPanel.Paint(px, ui);
        if (At == Stage.Question) DrawQuestion(art, g, bank5);
        else DrawTune(art, g, bank5);
        g.Fill((0, OptionsPanel.HelpY - 22, OptionsPanel.Width, OptionsPanel.HelpY + 14), 0x110);   // a dark strip for the help, as play's message strip
        var (help, second) = HelpLines();
        g.Text(16, OptionsPanel.HelpY - 16, help, 0xBB9);
        g.Text(16, OptionsPanel.HelpY, second, 0x886);
        return px;
    }

    /// <summary>The two help lines: what the picked thing does, and how to get on.</summary>
    public (string, string) HelpLines() => At switch
    {
        Stage.Question => (Help[Picked], Later),
        Stage.Column => (OnStart ? "Start the game with these settings." : OptionsPanel.PageHelp(Pages[ColumnLine]),
                         "Esc: back to the question."),
        _ => (OptionsPanel.ValueHelp.TryGetValue(PickedRow!.Key, out var vh) && vh.TryGetValue(_value!(PickedRow.Key), out var v) ? v
              : OptionsPanel.Help.GetValueOrDefault(PickedRow.Key, ""), "< > to change it. Esc: back to the pages."),
    };

    void DrawQuestion(OptionsPanel.BarArt art, OptionsPanel.Paint g, IReadOnlyList<ushort> bank5)
    {
        art.Draw(g, OptionsPanel.BarX, OptionsPanel.BarY, OptionsPanel.BarW, Title, null);
        for (var i = 0; i < 2; i++)
        {
            var (x0, y0, _, _) = BarBox(i);
            art.Draw(g, x0, y0, OptionsPanel.BarW, Answers[i], i == Picked ? OptionsPanel.PickedOlive : null);
        }
        art.Draw(g, OptionsPanel.BoxX, OptionsPanel.BoxTitleY, OptionsPanel.BoxW, "What you get: " + Answers[Picked], null);
        var h = OptionsPanel.RowY - OptionsPanel.BoxY + ListStep * Decided.Count + 22;
        g.Box(OptionsPanel.BoxX, OptionsPanel.BoxY, OptionsPanel.BoxW, h, bank5);
        var any = false;
        for (var i = 0; i < Decided.Count; i++)
        {
            var r = Decided[i];
            var y = OptionsPanel.RowY + ListStep * i;
            var v = ValueUnder(Picked, r.Key);
            var original = v == OptionsPanel.OriginalOf(r.Key);
            var shown = OptionsPanel.Mixed(v);
            g.Text(OptionsPanel.BoxX + 12, y, OptionsPanel.Label(r), 0xBB9);
            var vx = OptionsPanel.BoxX + OptionsPanel.BoxW - 14 - g.Measure(shown);
            g.Text(vx, y, shown, original ? (ushort)0xBB9 : (ushort)0xBDF);
            if (original) { g.Text(vx - 10, y, "*", 0xA84); any = true; }
        }
        if (any) g.Text(OptionsPanel.BoxX + 12, OptionsPanel.BoxY + h - 16, "* = what the original game did", 0x886);
    }

    void DrawTune(OptionsPanel.BarArt art, OptionsPanel.Paint g, IReadOnlyList<ushort> bank5)
    {
        art.Draw(g, OptionsPanel.BarX, OptionsPanel.BarY, OptionsPanel.BarW, Answers[1], null);
        for (var i = 0; i < ColumnCount; i++)
        {
            var (x0, y0, _, _) = BarBox(i);
            var text = i == Pages.Count ? StartBar : OptionsPanel.Mixed(Pages[i]);
            art.Draw(g, x0, y0, OptionsPanel.BarW, text, i == ColumnLine ? OptionsPanel.PickedOlive : null);
        }
        var page = ShownPage;
        var rows = Rows(page);
        art.Draw(g, OptionsPanel.BoxX, OptionsPanel.BoxTitleY, OptionsPanel.BoxW, OptionsPanel.Mixed(Pages[page]), null);
        bool IsOriginal(SettingsMenu.Row r) => OptionsPanel.OriginalOf(r.Key) is { } o && _value!(r.Key) == o &&
            !(SettingsMenu.ThreeDOnly.Contains(r.Key) && !ThreeD);
        var marked = rows.Any(IsOriginal);
        var boxH = OptionsPanel.RowY - OptionsPanel.BoxY + OptionsPanel.RowStep * rows.Count + (marked ? 22 : 4);
        g.Box(OptionsPanel.BoxX, OptionsPanel.BoxY, OptionsPanel.BoxW, boxH, bank5);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var y = OptionsPanel.RowY + OptionsPanel.RowStep * i;
            var picked = At == Stage.Rows && i == RowIndex;
            if (picked) g.Fill(OptionsPanel.LineBox(i), 0x553);
            g.Text(OptionsPanel.BoxX + 12, y, OptionsPanel.Label(r), picked ? (ushort)0xBDF : (ushort)0xBB9);
            var v = r.Key == SettingsMenu.KeysKey ? "F2" : !ThreeD && SettingsMenu.ThreeDOnly.Contains(r.Key) ? "3D only" : OptionsPanel.Mixed(_value!(r.Key));
            var changeable = r.Key != SettingsMenu.KeysKey && v != "3D only";
            if (picked && changeable) v = "< " + v + " >";
            var vx = OptionsPanel.BoxX + OptionsPanel.BoxW - 14 - g.Measure(v);
            if (picked) _lessX = changeable ? vx : -1;
            g.Text(vx, y, v, picked ? (ushort)0xFFF : (ushort)0xBDF);
            if (IsOriginal(r)) g.Text(vx - 10, y, "*", 0xA84);
        }
        if (marked) g.Text(OptionsPanel.BoxX + 12, OptionsPanel.BoxY + boxH - 16, "* = what the original game did", 0x886);
    }
}
