namespace Hunter.Rules;

/// <summary>The soldier record at $85E2 (32 bytes): +0 flags, +2 medal bits, +4 status, +5 face,
/// +8 title, +$A decoration bits, +$C credit (long), +$10 name ('*'-terminated), then the
/// player's world record at +$20 ($8602).</summary>
public sealed class Soldier
{
    public readonly byte[] R;
    public Soldier(byte[] main) => R = main.AsSpan(0x85E2 - 0x800, 48).ToArray();
    public int Word(int o) => R[o] << 8 | R[o + 1];
    public void SetWord(int o, int v) { R[o] = (byte)(v >> 8); R[o + 1] = (byte)v; }
    public void SetLong(int o, int v) { SetWord(o, v >> 16); SetWord(o + 2, v); }
    public int Long(int o) => Word(o) << 16 | Word(o + 2);
    /// <summary>$867E: a usable record (status at most 1, face not negative, flags 7-4 clear).</summary>
    public bool Valid => (sbyte)R[4] <= 1 && (sbyte)R[5] >= 0 && (R[0] & 0xF0) == 0;
    /// <summary>+4 = 0: no name yet, so the game asks for one ($870A).</summary>
    public bool New => R[4] == 0;
}

/// <summary>
/// The screens between choosing a game and playing ($235E -> $86F2): the name box ($8776, $B41A)
/// and the briefing (soldier panel $8818, mission objective $9488). Drawn on screen mode 0
/// with palette bank 6.
/// </summary>
public static class Pregame
{
    const int Prompt = 0x86BC, DefaultName = 0x86E2;

    static byte M(byte[] main, int addr) => main[addr - 0x800];

    /// <summary>$213A: the credits, a cleared screen with the text at $20B8 formatted and printed
    /// from the window's top ($60C0 clears the cursor).</summary>
    public static void Credits(IndexedScreen s, Ui ui, byte[] main)
    {
        s.Clear();
        var t = new Text(s, ui, main);
        t.Window(0, 0, 40, 20, 10, 0);
        t.Printf(0x20B8);
    }

    /// <summary>$B41A's line: prompt + '-' + name so far + cursor ('/' or ' ', every 3 frames).</summary>
    public static byte[] NameLine(byte[] main, IReadOnlyList<byte> typed, bool cursorShown)
    {
        var o = new List<byte>();
        for (var a = Prompt; M(main, a) != '*' && o.Count < 0x3F; a++) o.Add(M(main, a));   // $B80E, $3F max
        o.Add((byte)'-');
        o.AddRange(typed);
        o.Add((byte)(cursorShown ? '/' : ' '));
        o.Add((byte)'*');
        return o.ToArray();
    }

    /// <summary>The name box: a cleared screen ($CA9E) with the line at the top of mode 0's text
    /// window (40 columns, 20 lines of 10 pixels).</summary>
    public static void NameBox(IndexedScreen s, Ui ui, byte[] main, IReadOnlyList<byte> typed, bool cursorShown)
    {
        s.Clear();
        var t = new Text(s, ui, main);
        t.Window(0, 0, 40, 20, 10, 0);
        t.Print(NameLine(main, typed, cursorShown));
    }

    /// <summary>$B41A keys: Return ($44) or a click ends; Backspace ($41) deletes; 0-9, space,
    /// a-z, A-Z are kept while keystrokes are left (typing past them ends the input).</summary>
    public static bool Key(TypedLine typed, int raw, int ascii)
    {
        if (raw == 0x44) return true;
        if (raw == 0x41)                                                     // $B4B8 tst.w $1BC; bmi: nothing to delete
        {
            if (typed.Count > 0) { typed.RemoveAt(typed.Count - 1); typed.Left++; }   // $B4BE-$B4C4: the keystroke back (addq #1,d7)
            return false;
        }
        var ok = ascii is >= '0' and <= '9' or ' ' or >= 'a' and <= 'z' or >= 'A' and <= 'Z';
        if (!ok) return false;
        if (--typed.Left < 0) return true;                                   // $B4F6 subq #1,d7; bmi
        typed.Add((byte)ascii);
        return false;
    }

    /// <summary>TypeText's line ($B41A): the letters so far and d7, the keystrokes left. The start text
    /// counts against the length ($B452-$B45A: d7 = what CopyName left); Backspace gives one back ($B4C2
    /// addq #1,d7 - nothing the loop calls touches d7: PrintAtCursor saves it, WaitBlanks and PollKey leave it).</summary>
    public sealed class TypedLine : List<byte>
    {
        public int Left;

        public void Start(int max, IEnumerable<byte> text)
        {
            Clear();
            AddRange(text.Take(max));
            Left = max - Count;
        }
    }

    /// <summary>The name the soldier gets: what was typed, or "Trigger Happy" ($86E2) if nothing.</summary>
    public static byte[] FinalName(byte[] main, IReadOnlyList<byte> typed)
    {
        if (typed.Count > 0) return typed.ToArray();
        var o = new List<byte>();
        for (var a = DefaultName; M(main, a) != '*'; a++) o.Add(M(main, a));
        return o.ToArray();
    }

    /// <summary>$9134: the mission for a game mode = the first 16-byte record at $9072 whose low
    /// nibble is the mode; returns its offset ($20A).</summary>
    public static int Mission(byte[] main, int mode)
    {
        for (var off = 0; ; off += 16)
            if ((M(main, 0x9072 + off) & 0x0F) == mode) return off;
    }

    /// <summary>
    /// The briefing as it stands once typed out. typewriterFrame is called before each typed
    /// character of the objective (the original waits a frame there).
    /// worldHeader = the world file's first $3E8 bytes ($9E46 is its +$28 table).
    /// game = the game being played, for its kills ($9288): only a new game's ResetMissionsFindFirst ($9134, from
    /// $236E) clears them - a Re-brief ($23BE) and the next mission after a win ($98C4 NextMission, $23AA) brief
    /// with the kills so far. Null: a new game's first briefing, all zero.
    /// </summary>
    public static void Briefing(IndexedScreen s, Ui ui, byte[] main, Soldier sol, byte[] name, int mode,
        byte[] worldHeader, Action? typewriterFrame = null, int mission = -1, WorldState? game = null)
    {
        var t = new Text(s, ui, main);
        Panel(s, ui, main, sol, name, t);
        // $9490: window 0,$40 / 40 columns, 15 lines of 9; start on the last line ($60C6)
        t.Window(0, 0x40, 0x28, 0x0F, 9, 0);
        t.CursorToLastLine();
        // $94A4 MissionRecord $9068: the CURRENT mission ($9072 + $20A), not the mode's first - a Re-brief,
        // the next mission after a win and a loaded game brief the mission being played (-1: a new game's first)
        var rec = 0x9072 + (mission >= 0 ? mission : Mission(main, mode));
        t.Printf(0x9186, M(main, rec) >> 4 & 0x0F);
        t.Typewriter = true;
        t.Frame = typewriterFrame;
        Items(t, main, rec, worldHeader, game, collect: true, 0x91D2);
        if (Items(t, main, rec, worldHeader, game, collect: false, 0x91E5)) t.Printf(0x91F8);
        t.Printf(0x925A, M(main, rec + 5), M(main, rec + 4));
        t.Typewriter = false;
    }

    // $950A/$9550: the items still to collect (worldHeader table) or destroy ($9288)
    static bool Items(Text t, byte[] main, int rec, byte[] header, WorldState? game, bool collect, int title)
    {
        var left = new List<(int Count, int Kind)>();
        for (var k = 0; k < 4; k++)
        {
            var w = M(main, rec + 8 + k * 2) << 8 | M(main, rec + 9 + k * 2);
            if (w == 0 || ((w & 0x8000) != 0) != collect) continue;
            int count = w >> 8 & 0x7F, kind = w & 0xFF;
            // $9578 sub.w for the stock; $9580 sub.b for the kills - a byte, so 128+ kills over the goal wrap
            // to a positive count still to do; bpl, else 0 ($9586). ENHANCEMENT (roomy_memory): the wide count,
            // as the debrief and the bot read it (Games.Open, Hunt.OpenLeft)
            int remain = collect ? (short)(count - Collected(main, header, kind))
                : game is { RoomyMemory: true } ? count - game.KillsWide[kind]
                : (sbyte)(count - (game?.Kills[kind] ?? 0));
            if (remain < 0) remain = 0;
            left.Add((remain, kind));
        }
        if (!left.Any(x => x.Count > 0)) return false;
        t.Printf(title);
        foreach (var (count, kind) in left) t.Printf(0x91AE, count, kind);
        return true;
    }

    // $99D4: the kind record's byte +$2E picks a 32-byte block of $9E46; its word +$E counts
    static int Collected(byte[] main, byte[] header, int kind)
    {
        var block = M(main, 0xD45E + kind * 0x34 + 0x2E) << 5;
        var o = 0x28 + block + 0x0E;
        return (short)(header[o] << 8 | header[o + 1]);
    }

    /// <summary>The soldier's name as the record holds it (+$10, up to the '*').</summary>
    public static byte[] RecordName(Soldier sol) => sol.R.Skip(0x10).TakeWhile(b => b != '*').ToArray();

    /// <summary>View soldier ($875A): a cleared screen ($8776) with the soldier panel ($8812, top 0)
    /// showing the record's own name.</summary>
    public static void SoldierScreen(IndexedScreen s, Ui ui, byte[] main, Soldier sol)
    {
        s.Clear();
        SoldierPanel(s, ui, main, sol, RecordName(sol));
    }

    /// <summary>The soldier panel alone, as the briefing shows it while fading in ($874C, $8750).</summary>
    public static void SoldierPanel(IndexedScreen s, Ui ui, byte[] main, Soldier sol, byte[] name, int top = 0) =>
        Panel(s, ui, main, sol, name, new Text(s, ui, main), top);

    /// <summary>$873C: the chosen name into the record (+$10) by $B80E (d0 = 13): at most 14 bytes
    /// copied, then a '*' over the last one copied, so a 14-letter name keeps 13.</summary>
    public static void SetName(Soldier sol, byte[] name)
    {
        var n = Math.Min(name.Length, 13);
        for (var i = 0; i < n; i++) sol.R[0x10 + i] = name[i];
        sol.R[0x10 + n] = (byte)'*';
    }

    /// <summary>$8818 with $C4 = top: the soldier panel from row top (0 across the top, $88 under
    /// the end of a game's message, $96EA).</summary>
    static void Panel(IndexedScreen s, Ui ui, byte[] main, Soldier sol, byte[] name, Text t, int top = 0)
    {
        t.Window(9, 5 + top, 0x1F, 0x11, 0x0B, 0);
        t.Line--;
        Ui.Blit(s, ui.Frames[0x16], 0, top);                         // panel
        Ui.Blit(s, ui.Frames[0x12 + sol.R[5]], 0x0B, 8 + top);        // face
        Ui.Blit(s, ui.Frames[(sol.R[0] & 1) != 0 ? 2 : 1], 0x118, 6 + top);
        Ui.Blit(s, ui.Frames[sol.Word(8) + 3], 0x4C, 6 + top);        // rank badge
        for (int k = 0, x = 0x4C, f = 0x13; k < 3; k++, x += 0x10, f++)
            if ((sol.R[2] >> k & 1) != 0) Ui.Blit(s, ui.Frames[f], x, 0x1C + top);
        var bits = sol.Word(0x0A);
        for (int row = 0, y = 0x19 + top, f = 0x0A; row < 2; row++, y += 0x12)
            for (int k = 0, x = 0xA0; k < 4; k++, x += 0x20, f++)
            {
                if ((bits & 1) != 0) Ui.Blit(s, ui.Frames[f], x, y);
                bits >>= 1;
            }
        // $8906: the name into the status template's field ($B61C) by $B80E (d0 = 13), padded
        // with spaces by $B822. A full 14-letter name leaves a '*' in its last place, which
        // ends the status text there.
        var template = main.AsSpan(0xB605 - 0x800, 0x40).ToArray();
        var field = 0xB61C - 0xB605;
        if (name.Length < 14)
            for (var i = 0; i < 14; i++) template[field + i] = i < name.Length ? name[i] : (byte)' ';
        else
        {
            for (var i = 0; i < 13; i++) template[field + i] = name[i];
            template[field + 13] = (byte)'*';
        }
        t.Print(t.FormatBytes(template, sol.R[4], sol.Word(8), sol.Long(0x0C)));
    }
}
