namespace Hunter.Rules;

/// <summary>STUNTIN's league screen in Hunter's panel look (ScrScreenArt, full colour): a stone frame round the screen as
/// the manual's pages, a stone banner with the league's name, the four divisions in sunk wells as a 2 x 2 table (I and II on
/// top, III and IV under them; each its heading and its three drivers by the round's standing, points and wins, the
/// player's lines lit), then the round and its track, the two drivers' faces in sunk frames with their names under them, a
/// line of news, and a stone command bar along the foot as the store's (CLEAR / AUTO / END), its last word red. The faces
/// are placed, not drawn: the shell paints them in their own colours (Faces).</summary>
public static class ScrLeagueScreen
{
    public const int RaceHandler = 0x5C20, NextHandler = 0x5C21, PracticeHandler = 0x5C22, NewHandler = 0x5C23,
        BackHandler = 0x5C24, YesHandler = 0x5C25, NoHandler = 0x5C26;
    /// <summary>The faces' size (the original's portraits on its players screen).</summary>
    public const int FaceW = 72, FaceH = 44;
    const int Band = 4, BannerTop = 4, BannerBottom = 21, Line = 9, BoxTop = 25, BoxH = 39, BoxGap = 2, ColLeft = 8, ColRight = 163,
        ColW = 149, RaceY = 108, FaceY = 118, FaceLeft = 44, FaceRight = 204, NewsY = 173, BarTop = 183, BarBottom = 195;

    /// <summary>A face to paint: the driver id and the top left.</summary>
    public readonly record struct Face(int Id, int X, int Y);

    /// <summary>The screen for the league; names by driver id (the player's shown as You); news: a line under the race (the
    /// last round's result). Returns the buttons, the faces to paint and the picture.</summary>
    public static (List<Menus.Button> Buttons, List<Face> Faces, ScrScreenArt Art) Draw(IndexedScreen s, Ui ui, (byte[] Pixels, ushort[] Palette) pan,
        ScrLeague l, string[] names, string? news)
    {
        s.Clear();
        var a = Page(ui, pan, l.Super ? "Super League" : "Stuntin' League");
        string Name(int id) => id == ScrLeague.Player ? "You" : names[id];
        for (var d = ScrLeague.Divisions - 1; d >= 0; d--)
        {
            var at = 3 - d;                                                  // I top left, II top right, III, IV
            int x = at % 2 == 0 ? ColLeft : ColRight, y = BoxTop + at / 2 * (BoxH + BoxGap);
            var mine = l.PlayerDivision == d;
            a.Well(x, y, x + ColW - 1, y + BoxH - 1);
            a.Write("Division " + ScrLeague.DivisionNames[d], x + 4, y + 2, mine ? ScrScreenArt.Mine : ScrScreenArt.Heading);
            a.Right("Pts", x + ColW - 26, y + 2, ScrScreenArt.Dim);
            a.Right("W", x + ColW - 6, y + 2, ScrScreenArt.Dim);
            var b = ScrLeague.DivisionBase[d];
            for (var i = 0; i < 3; i++)
            {
                var id = l.Standing[b + i];
                var row = y + 2 + Line * (i + 1);
                var c = id == ScrLeague.Player ? ScrScreenArt.Mine : ScrScreenArt.Text;
                a.Write(Name(id), x + 10, row, c);
                a.Right(l.Points(id).ToString(), x + ColW - 26, row, c);
                a.Right(l.Wins[id].ToString(), x + ColW - 6, row, c);
            }
        }
        var faces = new List<Face>();
        var buttons = new List<(string Text, int Handler)>();
        if (l.Champion)
        {
            a.Centre("Champion of the Super League", 160, RaceY, ScrScreenArt.Mine);
            faces.Add(FaceAt(a, ScrLeague.Player, 160 - FaceW / 2));
        }
        else
        {
            var d = l.PlayerDivision;
            var (p, q) = l.Pair(d, l.Round);
            var mine = l.PlayerRace is not null;
            if (mine && q == ScrLeague.Player) (p, q) = (q, p);              // the player on the left
            var track = ScrIsland.Tracks[l.Track(d, l.Round)];
            a.Centre($"Round {l.Round + 1} of {ScrLeague.Rounds} - {track}" + (mine ? "" : " (you sit this one out)"), 160, RaceY, ScrScreenArt.Heading);
            faces.Add(FaceAt(a, p, FaceLeft));
            faces.Add(FaceAt(a, q, FaceRight));
            a.Centre("v", 160, FaceY + FaceH / 2 - 4, ScrScreenArt.Dim);
            a.Centre(Name(p), FaceLeft + FaceW / 2, FaceY + FaceH + 3, p == ScrLeague.Player ? ScrScreenArt.Mine : ScrScreenArt.Text);
            a.Centre(Name(q), FaceRight + FaceW / 2, FaceY + FaceH + 3, ScrScreenArt.Text);
            buttons.Add(mine ? ("Race", RaceHandler) : ("Next", NextHandler));
            buttons.Add(("Practice", PracticeHandler));
        }
        if (!string.IsNullOrEmpty(news)) a.Centre(news, 160, NewsY, ScrScreenArt.Mine);
        buttons.Add(("New", NewHandler));
        buttons.Add(("Back", BackHandler));
        return (CommandBar(a, buttons), faces, a);
    }

    /// <summary>One car's race as it ended (ScrLeagueRun): home at (blank, -1 not home), best lap (hundredths, -1 none), restarts,
    /// wrecked, damage.</summary>
    public readonly record struct CarReport(long FinishedAt, int BestLap, int Restarts, bool Wrecked, int Damage);
    /// <summary>The player's race: the track, the opponent, both cars.</summary>
    public readonly record struct RaceReport(int Track, int Opponent, CarReport Me, CarReport Them);

    /// <summary>The round's results after it is played: the player's race when he ran one (both cars side by side, the
    /// margin), then every division's race (winner, loser, the fastest lap's extra point), the news, Continue.</summary>
    public static (List<Menus.Button> Buttons, ScrScreenArt Art) Results(IndexedScreen s, Ui ui, (byte[] Pixels, ushort[] Palette) pan,
        ScrLeague l, string[] names, RaceReport? race, int round, string? news)
    {
        s.Clear();
        string Name(int id) => id == ScrLeague.Player ? "You" : names[id];
        var lost = race is { } rr && (rr.Me.Wrecked || rr.Me.FinishedAt < 0 || rr.Them.FinishedAt >= 0 && rr.Them.FinishedAt < rr.Me.FinishedAt);
        var a = Page(ui, pan, race is null ? $"Round {round + 1} Results" : race.Value.Me.Wrecked ? "Wrecked" : lost ? "You Lost" : "You Won");
        var y = BoxTop;
        if (race is { } r)
        {
            const int Label = 20, MeX = 200, ThemX = 290;
            a.Well(ColLeft, y, ScrScreenArt.W - 1 - ColLeft, y + 66);
            a.Write(ScrIsland.Tracks[r.Track], ColLeft + 4, y + 2, ScrScreenArt.Heading);
            a.Right("You", MeX, y + 2, ScrScreenArt.Mine);
            a.Right(names[r.Opponent], ThemX, y + 2, ScrScreenArt.Text);
            static string Finish(CarReport c, CarReport o) => c.Wrecked ? "Wrecked" : c.FinishedAt < 0 ? "Not home" : o.FinishedAt >= 0 && o.FinishedAt < c.FinishedAt && !o.Wrecked ? "2nd" : "1st";
            static string Lap(int h) { if (h < 0) return "-"; var t = h / 10; return $"{t / 600}:{t / 10 % 60:D2}.{t % 10}"; }
            (string, string, string)[] rows =
            [
                ("Finished", Finish(r.Me, r.Them), Finish(r.Them, r.Me)),
                ("Best lap", Lap(r.Me.BestLap), Lap(r.Them.BestLap)),
                ("Restarts", r.Me.Restarts.ToString(), r.Them.Restarts.ToString()),
                ("Damage", r.Me.Damage.ToString(), r.Them.Damage.ToString()),
            ];
            for (var i = 0; i < rows.Length; i++)
            {
                var ry = y + 2 + Line * (i + 1) + 2;
                a.Write(rows[i].Item1, ColLeft + Label, ry, ScrScreenArt.Dim);
                a.Right(rows[i].Item2, MeX, ry, ScrScreenArt.Mine);
                a.Right(rows[i].Item3, ThemX, ry, ScrScreenArt.Text);
            }
            if (r.Me.FinishedAt >= 0 && r.Them.FinishedAt >= 0 && !r.Me.Wrecked && !r.Them.Wrecked)
            {
                var gap = Math.Abs(r.Me.FinishedAt - r.Them.FinishedAt) / 50.0;
                a.Centre((lost ? "Lost by " : "Won by ") + gap.ToString("0.0") + " seconds", 160, y + 2 + Line * 5 + 6, lost ? ScrScreenArt.Text : ScrScreenArt.Mine);
            }
            y += 70;
        }
        // the round as a table: each division's winner, loser and the fastest lap (its extra point)
        const int DivX = ColLeft + 6, WinX = ColLeft + 70, LoseX = ColLeft + 154, FastX = ColLeft + 238;
        var seasonNews = news is not null && (news.StartsWith("Season", StringComparison.Ordinal) || news.StartsWith("Promoted", StringComparison.Ordinal) || news.StartsWith("You won the", StringComparison.Ordinal));
        var bottom = y + 2 + Line * 2 + 4 * (Line + 1) + 4 + (seasonNews ? Line + 4 : 0);
        a.Well(ColLeft, y, ScrScreenArt.W - 1 - ColLeft, bottom);
        a.Write($"Round {round + 1} of {ScrLeague.Rounds}", DivX - 2, y + 2, ScrScreenArt.Heading);
        a.Write("Winner", WinX, y + 2 + Line + 1, ScrScreenArt.Dim);
        a.Write("Loser", LoseX, y + 2 + Line + 1, ScrScreenArt.Dim);
        a.Write("Fastest lap", FastX, y + 2 + Line + 1, ScrScreenArt.Dim);
        var line = y + 2 + Line * 2 + 4;
        foreach (var x in l.Last.OrderByDescending(x => x[0]))
        {
            var mine = x[1] == ScrLeague.Player || x[2] == ScrLeague.Player;
            uint C(int id) => id == ScrLeague.Player ? ScrScreenArt.Mine : ScrScreenArt.Text;
            a.Write("Div " + ScrLeague.DivisionNames[x[0]], DivX, line, mine ? ScrScreenArt.Mine : ScrScreenArt.Dim);
            a.Write(Name(x[1]), WinX, line, C(x[1]));
            a.Write(Name(x[2]), LoseX, line, C(x[2]));
            a.Write(Name(x[3]), FastX, line, C(x[3]));
            line += Line + 1;
        }
        if (seasonNews) a.Centre(news!, 160, bottom - Line - 2, ScrScreenArt.Mine);
        return (CommandBar(a, [("Continue", BackHandler)]), a);
    }

    /// <summary>Before a race, the track (the original's preview): its name, the race's division and round and laps, the track
    /// itself turning in a sunk well (ScrTrackPreview, drawn again each frame by Turn), Race and Back.</summary>
    public static (List<Menus.Button> Buttons, ScrScreenArt Art, Action<double> Turn) Preview(IndexedScreen s, Ui ui, (byte[] Pixels, ushort[] Palette) pan,
        ScrRoad road, int track, string line)
    {
        s.Clear();
        var a = Page(ui, pan, ScrIsland.Tracks[track]);
        const int X0 = ColLeft, Y0 = BoxTop, X1 = ScrScreenArt.W - 1 - ColLeft, Y1 = BarTop - 14;
        a.Bevel(X0, Y0, X1, Y1, sunk: true);
        a.Bevel(X0 + 1, Y0 + 1, X1 - 1, Y1 - 1, sunk: true);
        var view = new ScrTrackPreview(road);
        void Turn(double angle)
        {
            view.Draw(a, X0 + 2, Y0 + 2, X1 - 2, Y1 - 2, angle);
            a.Write(line, X0 + 6, Y0 + 5, ScrScreenArt.Mine);
            a.Right($"{ScrRoad.Laps} laps", X1 - 6, Y0 + 5, ScrScreenArt.Mine);
        }
        Turn(0.6);
        a.Centre("The track turns until you race", 160, Y1 + 3, ScrScreenArt.Dim);
        return (CommandBar(a, [("Race", RaceHandler), ("Back", BackHandler)]), a, Turn);
    }

    /// <summary>A race-end picture (the original's wreck, won, lost and promotion pictures): the banner, a sunk well the shell
    /// draws the scene into each frame (its box returned), a line under it, Continue.</summary>
    public static (List<Menus.Button> Buttons, ScrScreenArt Art, (int X0, int Y0, int X1, int Y1) Box) Picture(IndexedScreen s, Ui ui, (byte[] Pixels, ushort[] Palette) pan, string title, string line)
    {
        s.Clear();
        var a = Page(ui, pan, title);
        const int X0 = ColLeft, Y0 = BoxTop, X1 = ScrScreenArt.W - 1 - ColLeft, Y1 = BarTop - 14;
        a.Bevel(X0, Y0, X1, Y1, sunk: true);
        a.Bevel(X0 + 1, Y0 + 1, X1 - 1, Y1 - 1, sunk: true);
        a.Centre(line, 160, Y1 + 3, ScrScreenArt.Text);
        return (CommandBar(a, [("Continue", BackHandler)]), a, (X0 + 2, Y0 + 2, X1 - 2, Y1 - 2));
    }

    /// <summary>The new league's question: the league so far is lost.</summary>
    public static (List<Menus.Button> Buttons, ScrScreenArt Art) AskNew(IndexedScreen s, Ui ui, (byte[] Pixels, ushort[] Palette) pan)
    {
        s.Clear();
        var a = Page(ui, pan, "New League");
        a.Well(40, 70, 279, 120);
        a.Centre("Start a new league?", 160, 82, ScrScreenArt.Mine);
        a.Centre("This league's table and points are lost.", 160, 100, ScrScreenArt.Text);
        return (CommandBar(a, [("Yes", YesHandler), ("No", NoHandler)]), a);
    }

    /// <summary>A screen's ground: the stone frame and the banner with its title.</summary>
    static ScrScreenArt Page(Ui ui, (byte[] Pixels, ushort[] Palette) pan, string title)
    {
        var a = new ScrScreenArt(ui, pan.Pixels, pan.Palette);
        a.Frame(Band);
        a.Bar(Band, BannerTop, ScrScreenArt.W - 1 - Band, BannerBottom);
        a.Title(title, 160, BannerTop + 1);
        return a;
    }

    /// <summary>A face's sunk frame round where the shell paints it.</summary>
    static Face FaceAt(ScrScreenArt a, int id, int x)
    {
        a.Bevel(x - 2, FaceY - 2, x + FaceW + 1, FaceY + FaceH + 1, sunk: true);
        a.Bevel(x - 1, FaceY - 1, x + FaceW, FaceY + FaceH, sunk: true);
        return new Face(id, x, FaceY);
    }

    /// <summary>The command bar along the foot (the store's): the words spread across it, each its own button, the last red.</summary>
    static List<Menus.Button> CommandBar(ScrScreenArt a, List<(string Text, int Handler)> items)
    {
        int x0 = Band, x1 = ScrScreenArt.W - 1 - Band;
        a.Bar(x0, BarTop, x1, BarBottom);
        var list = new List<Menus.Button>();
        var w = (x1 - x0 + 1) / items.Count;
        for (var i = 0; i < items.Count; i++)
        {
            int left = x0 + i * w, right = i == items.Count - 1 ? x1 : left + w - 1;
            if (i > 0) { for (var y = BarTop + 2; y <= BarBottom - 2; y++) { a.Set(left - 1, y, a.EdgeDark); a.Set(left, y, a.EdgeBright); } }
            a.Centre(items[i].Text, (left + right) / 2, BarTop + 3, i == items.Count - 1 ? ScrScreenArt.Red : ScrScreenArt.Text);
            list.Add(new Menus.Button(left, right + 1, BarTop, BarBottom + 1, items[i].Handler, items[i].Text));
        }
        return list;
    }

    /// <summary>The last round in a line: the player's race (or his division's), and at a season's end what became of him.</summary>
    public static string News(ScrLeague l, string[] names, int playerDivisionBefore)
    {
        string Name(int id) => id == ScrLeague.Player ? "You" : names[id];
        if (l.Champion) return "You won the Super League!";
        if (l.Round == 0 && l.Season > 0)
        {
            if (l.EndedTo < 0) return "";
            if (l.Super && l.EndedFrom == 3 && l.EndedTo == 0) return "Promoted to the Super League!";
            return l.EndedTo > l.EndedFrom ? $"Season over - up to Division {ScrLeague.DivisionNames[l.EndedTo]}"
                : l.EndedTo < l.EndedFrom ? $"Season over - down to Division {ScrLeague.DivisionNames[l.EndedTo]}"
                : $"Season over - still in Division {ScrLeague.DivisionNames[l.EndedTo]}";
        }
        var r = l.Last.FirstOrDefault(x => x[0] == playerDivisionBefore);
        if (r is null) return "";
        var (winner, loser) = (r[1], r[2]);
        return winner == ScrLeague.Player ? $"You beat {Name(loser)}" : loser == ScrLeague.Player ? $"{Name(winner)} beat you" : $"{Name(winner)} beat {Name(loser)}";
    }
}
