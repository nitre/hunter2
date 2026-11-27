namespace Hunter.Rules;

/// <summary>STUNTIN's league screen, drawn on the menus' screen in their font and buttons (bank 5; the headings and the
/// player's own lines in the high plane, Menus.ScrTextPlane): the four divisions as a 2 x 2 table (I and II on top, III and
/// IV under them; each a heading and its three drivers by the round's standing, points and wins), then the next race -
/// the track, the two drivers' faces with their names under them - a line of news, and a row of buttons. The faces are
/// placed, not drawn: the shell paints them in their own colours (Faces).</summary>
public static class ScrLeagueScreen
{
    public const int RaceHandler = 0x5C20, NextHandler = 0x5C21, PracticeHandler = 0x5C22, NewHandler = 0x5C23,
        BackHandler = 0x5C24, YesHandler = 0x5C25, NoHandler = 0x5C26;
    /// <summary>The faces' size (the original's portraits on its players screen).</summary>
    public const int FaceW = 72, FaceH = 44;
    const int Line = 9, BlockTop = 13, BlockGap = 41, ColLeft = 14, ColRight = 166, ColW = 140, FaceY = 104, FaceLeft = 48,
        FaceRight = 200, ButtonY = 168, Style = 0;

    /// <summary>A face to paint: the driver id and the top left.</summary>
    public readonly record struct Face(int Id, int X, int Y);

    /// <summary>The screen for the league; names by driver id (the player's shown as YOU); news: a line under the race (the
    /// last round's result). Returns the buttons and the faces to paint.</summary>
    public static (List<Menus.Button> Buttons, List<Face> Faces) Draw(IndexedScreen s, Ui ui, ScrLeague l, string[] names, string? news)
    {
        s.Clear();
        string Name(int id) => id == ScrLeague.Player ? "YOU" : names[id];
        var hi = Menus.ScrTextPlane;
        var title = (l.Super ? "SUPER LEAGUE" : "STUNTIN' LEAGUE") + (l.Champion ? "" : $"  -  ROUND {l.Round + 1} OF {ScrLeague.Rounds}");
        Centre(s, ui, title, 160, 2, hi);
        for (var d = ScrLeague.Divisions - 1; d >= 0; d--)
        {
            var at = 3 - d;                                                  // I top left, II top right, III, IV
            int x = at % 2 == 0 ? ColLeft : ColRight, y = BlockTop + at / 2 * BlockGap;
            var mine = l.PlayerDivision == d;
            Text(s, ui, "DIVISION " + ScrLeague.DivisionNames[d], x, y, mine ? hi : 0);
            Right(s, ui, "PTS", x + ColW - 24, y, 0);
            Right(s, ui, "W", x + ColW, y, 0);
            var b = ScrLeague.DivisionBase[d];
            for (var i = 0; i < 3; i++)
            {
                var id = l.Standing[b + i];
                var row = y + Line * (i + 1) + 2;
                var plane = id == ScrLeague.Player ? hi : 0;
                Text(s, ui, Name(id), x + 6, row, plane);
                Right(s, ui, l.Points(id).ToString(), x + ColW - 24, row, plane);
                Right(s, ui, l.Wins[id].ToString(), x + ColW, row, plane);
            }
        }
        var faces = new List<Face>();
        var buttons = new List<(string Text, int Handler)>();
        if (l.Champion)
        {
            Centre(s, ui, "CHAMPION OF THE SUPER LEAGUE", 160, FaceY + 4, hi);
            faces.Add(new Face(ScrLeague.Player, 160 - FaceW / 2, FaceY + 16));
        }
        else
        {
            var d = l.PlayerDivision;
            var (a, b) = l.Pair(d, l.Round);
            var mine = l.PlayerRace is not null;
            if (mine && b == ScrLeague.Player) (a, b) = (b, a);              // the player on the left
            var track = ScrIsland.Tracks[l.Track(d, l.Round)].ToUpperInvariant();
            Centre(s, ui, mine ? track : "YOU SIT THIS ROUND OUT - " + track, 160, FaceY - 11, hi);
            faces.Add(new Face(a, FaceLeft, FaceY));
            faces.Add(new Face(b, FaceRight, FaceY));
            Centre(s, ui, "V", 160, FaceY + FaceH / 2 - 4, 0);
            Centre(s, ui, Name(a), FaceLeft + FaceW / 2, FaceY + FaceH + 1, a == ScrLeague.Player ? hi : 0);
            Centre(s, ui, Name(b), FaceRight + FaceW / 2, FaceY + FaceH + 1, 0);
            buttons.Add(mine ? ("RACE", RaceHandler) : ("NEXT", NextHandler));
            buttons.Add(("PRACTICE", PracticeHandler));
        }
        if (news is not null) Centre(s, ui, news, 160, FaceY + FaceH + 10, hi);
        buttons.Add(("NEW", NewHandler));
        buttons.Add(("BACK", BackHandler));
        return (Row(s, ui, buttons), faces);
    }

    /// <summary>The new league's question: the league so far is lost.</summary>
    public static List<Menus.Button> AskNew(IndexedScreen s, Ui ui)
    {
        s.Clear();
        Centre(s, ui, "START A NEW LEAGUE?", 160, 80, Menus.ScrTextPlane);
        Centre(s, ui, "THIS LEAGUE'S TABLE AND POINTS ARE LOST", 160, 94, 0);
        return Row(s, ui, [("YES", YesHandler), ("NO", NoHandler)]);
    }

    /// <summary>Buttons two to a row across the foot (the menus' own button, 144 wide), the rows 16 apart.</summary>
    static List<Menus.Button> Row(IndexedScreen s, Ui ui, List<(string Text, int Handler)> items)
    {
        var f = ui.Frames[Style + 0x17];
        var list = new List<Menus.Button>();
        for (var i = 0; i < items.Count; i++)
        {
            var dx = items.Count - i == 1 && i % 2 == 0 ? 0 : (i % 2 == 0 ? -38 : 38);   // a last one alone: centred
            var y = ButtonY + i / 2 * f.Height;
            var h = ui.DrawMenuItem(s, new Ui.MenuItem(Style, dx, items[i].Text), y);
            var left = (s.Width >> 1) + dx * 2 - (f.Width >> 1);
            list.Add(new Menus.Button(left, left + f.Width, y, y + h, items[i].Handler, items[i].Text));
        }
        return list;
    }

    static void Text(IndexedScreen s, Ui ui, string t, int x, int y, int plane)
    {
        foreach (var c in t) x += ui.DrawChar(s, c, x, y, plane);
    }
    static void Right(IndexedScreen s, Ui ui, string t, int x, int y, int plane) => Text(s, ui, t, x - ui.Measure(t + "*"), y, plane);
    static void Centre(IndexedScreen s, Ui ui, string t, int x, int y, int plane) => Text(s, ui, t, x - ui.Measure(t + "*") / 2, y, plane);

    /// <summary>The last round in a line: the player's race (or his division's), and at a season's end what became of him.</summary>
    public static string News(ScrLeague l, string[] names, int playerDivisionBefore)
    {
        string Name(int id) => id == ScrLeague.Player ? "YOU" : names[id];
        if (l.Champion) return "YOU WON THE SUPER LEAGUE!";
        if (l.Round == 0 && l.Season > 0)
        {
            if (l.EndedTo < 0) return "";
            if (l.Super && l.EndedFrom == 3 && l.EndedTo == 0) return "PROMOTED TO THE SUPER LEAGUE!";
            return l.EndedTo > l.EndedFrom ? $"SEASON OVER - UP TO DIVISION {ScrLeague.DivisionNames[l.EndedTo]}"
                : l.EndedTo < l.EndedFrom ? $"SEASON OVER - DOWN TO DIVISION {ScrLeague.DivisionNames[l.EndedTo]}"
                : $"SEASON OVER - STILL IN DIVISION {ScrLeague.DivisionNames[l.EndedTo]}";
        }
        var r = l.Last.FirstOrDefault(x => x[0] == playerDivisionBefore);
        if (r is null) return "";
        var (winner, loser) = (r[1], r[2]);
        return winner == ScrLeague.Player ? $"YOU BEAT {Name(loser)}" : loser == ScrLeague.Player ? $"{Name(winner)} BEAT YOU" : $"{Name(winner)} BEAT {Name(loser)}";
    }
}
