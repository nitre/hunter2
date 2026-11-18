namespace Hunter.Rules;

/// <summary>
/// The log book's noted places ($CF54: 17 entries of 22 bytes: +0 x, +2 z, +4 locked, +5 the
/// marks bitmap's colour under the place, +6 the name, 16 bytes up to '*') and the marks bitmap
/// ($839A, animation frame 0: 128 x 128, 4 planes and a mask $800 apart) the 'm' overlay and
/// the log book draw (re/formats/game.md 4).
/// </summary>
public sealed class LogBook
{
    public const int Entries = 17, Size = 0x16;
    public readonly byte[] Places = new byte[Entries * Size];
    /// <summary>The text buffer $B694 the names are formatted into (16 bytes seen): the menus left "]]]]<----------]"
    /// in it before a new game's first note (read from the original at $902E, all three games); a name overwrites its
    /// own length, so the places keep the tail of the name before (save.md 3).</summary>
    readonly byte[] Fmt = "]]]]<----------]"u8.ToArray();
    /// <summary>$234: the selected line.</summary>
    public short Selected;
    /// <summary>The marks bitmap; past plane 4 the spill of row 128 ($4076 with z >> 9 = 0).</summary>
    public readonly byte[] Marks = new byte[6 * 0x800];

    int W(int o) => Places[o] << 8 | Places[o + 1];
    public bool Noted(int n) => Places[n * Size + 6] != 0;                  // $D134 tst.b 6(a0)
    /// <summary>Entry n's place on the map (+0 x, +2 z) - what the player reads off the book.</summary>
    public (int X, int Z) Place(int n) => (W(n * Size), W(n * Size + 2));
    /// <summary>Entry n is locked (+4): the HQ, the stores, Hunter's first man - not an objective.</summary>
    public bool Locked(int n) => Places[n * Size + 4] != 0;
    public string Name(int n)
    {
        var o = "";
        for (var a = n * Size + 6; a < (n + 1) * Size && Places[a] != '*'; a++) o += (char)Places[a];
        return o;
    }

    /// <summary>A new game ($235E): every place dropped ($D0FE: $A5 bit 3, $D104), the HQ ($AA)
    /// and the stores ($95) noted locked ($2380-$2394), in Hunter the first man ($21, $239E), then
    /// $D0CA: each objective word of the mission ($9068 + 8) above 0, (count << 8 | type): the
    /// count's records of that type, last first ($D0E6), unlocked.</summary>
    public static LogBook NewGame(WorldState s, int missionRecord)
    {
        var log = new LogBook();
        log.Note(s, 0xAA, 0, 0xFF);
        log.Note(s, 0x95, 0, 0xFF);
        if (s.Mode == GameMode.Hunter) log.Note(s, 0x21, 0, 0xFF);           // $2398 tst.w $208; bgt
        log.NoteObjectives(s, missionRecord);                                // $23AA -> $D0CA
        return log;
    }

    /// <summary>NoteObjectives ($D0CA, entered from $23AA after a debrief's "mission n" screen as well
    /// as at a new game): every place dropped first ($D104 DropPlaces: DropPlace $D17C from 16 down to
    /// 0), then each objective word of the mission record at +8 above 0 - (count &lt;&lt; 8 | type, $9A08)
    /// SplitObjective - noted as count records of that type, last first, unlocked ($D0E6 NoteRecords).
    /// (The collect objectives, a word with bit 15 set, are negative and skipped.)</summary>
    public void NoteObjectives(WorldState s, int missionRecord)
    {
        for (var n = Entries - 1; n >= 0; n--) Drop(n);                      // $D104: $D17C with 16 down to 0
        var main = s.Data.Main;
        for (var k = 0; k < 4; k++)
        {
            var w = (short)(main[missionRecord + 8 + 2 * k - 0x800] << 8 | main[missionRecord + 9 + 2 * k - 0x800]);
            if (w <= 0) continue;                                            // $D0D8 ble
            int count = w >> 8 & 0x7F, type = w & 0xFF;                      // $9A08
            for (var n = count; n > 0; n--) Note(s, type, n - 1, 0);         // $D0EA-$D0F8
        }
    }

    // $902E: the name = FormatString(table 1, type) with '*' over its space; FindRecord ($80E0):
    // the nth record of the type in the grid ($80FC), else in the building table ($8124); none:
    // nothing. $D146: into the first free entry ($D11A), +4 = locked. (+5 is set at PlayStart;
    // the original copies 16 bytes of its text buffer $B694, past the '*' whatever was there: Fmt.)
    void Note(WorldState s, int type, int n, byte locked)
    {
        var name = Text.TableText(s.Data.Main, 1, type);
        name[^1] = (byte)'*';
        for (var i = 0; i < name.Length && i < Fmt.Length; i++) Fmt[i] = name[i];   // $903E: formatted into $B694 first
        if (FindRecord(s, type, n) is not var (x, z)) return;
        var e = Enumerable.Range(0, Entries).FirstOrDefault(i => !Noted(i), -1);
        if (e < 0) return;
        var o = e * Size;
        Places[o] = (byte)(x >> 8); Places[o + 1] = (byte)x; Places[o + 2] = (byte)(z >> 8); Places[o + 3] = (byte)z;
        Places[o + 4] = locked;
        Array.Copy(Fmt, 0, Places, o + 6, 16);                               // $D154: 16 bytes of $B694, what the name left of it too
        Places[o + 6 + 15] = (byte)'*';                                      // $D166 move.b #'*',-1(a1)
    }

    // $80E0: KindForType ($8160); $80FC: the nth grid record ($83BA, $800 x 16 bytes) whose word
    // +0 is that kind word: (+6, +$A); else $8124: the table after the grid (128 blocks of $80:
    // a header, 7 entries at +$10): the block's first two words
    /// <summary>FindRecord for the tests: where the first record of a type lies.</summary>
    public static (int X, int Z)? FindRecordForTests(WorldState s, int type) => FindRecord(s, type, 0);

    internal static (int X, int Z)? FindRecord(WorldState s, int type, int n)
    {
        // $8162 bge: a type of $AF or more leaves D0 = the type itself, which is then compared (not kind 0,
        // which would match an empty record)
        var kind = type >= 0xAF && !ExtraKinds.Is(type) ? type & 0xFFFF : Vehicles.KindForType(s.Data.Main, type) & 0xFFFF;
        var g = s.Grid;
        var d1 = n;
        for (var r = 0; r < 0x800; r++)
            if ((g[r * 16] << 8 | g[r * 16 + 1]) == kind && --d1 < 0)
                return (g[r * 16 + 6] << 8 | g[r * 16 + 7], g[r * 16 + 10] << 8 | g[r * 16 + 11]);
        if (s.RoomyMemory)                                                   // ENHANCEMENT (roomy_memory): the cells' overflow
            foreach (var (_, list) in s.Overflow.OrderBy(kv => kv.Key))      // records after the grid's, cell by cell
                foreach (var o in list)
                    if ((o[0] << 8 | o[1]) == kind && --d1 < 0) return (o[6] << 8 | o[7], o[10] << 8 | o[11]);
        d1 = n;
        var b = s.Buildings;
        for (var blk = 0; blk < 0x80; blk++)
            for (var k = 1; k <= 7; k++)
                if ((b[blk * 0x80 + k * 16] << 8 | b[blk * 0x80 + k * 16 + 1]) == kind && --d1 < 0)
                    return (b[blk * 0x80] << 8 | b[blk * 0x80 + 1], b[blk * 0x80 + 2] << 8 | b[blk * 0x80 + 3]);
        return null;
    }

    /// <summary>PlayStart's $409C: the bitmap from the terrain (each cell's type & $F, row 127 -
    /// terrain row), the mask = the 4 planes or-ed ($4142); then $D43A: for each place up to the
    /// first empty one, +5 = the colour under it ($405C) and its mark drawn ($D428).</summary>
    public void BuildMarks(Terrain t)
    {
        Array.Clear(Marks);
        for (var row = 0; row < 128; row++)
            for (var col = 0; col < 128; col++)
            {
                var c = t.Type(col, row) & 0xF;
                for (var p = 0; p < 4; p++) Set(p * 0x800 + (127 - row) * 16 + (col >> 3), 7 - (col & 7), (c >> p & 1) != 0);
            }
        for (var i = 0; i < 0x800; i++)
            Marks[0x2000 + i] = (byte)(Marks[i] | Marks[0x800 + i] | Marks[0x1000 + i] | Marks[0x1800 + i]);
        for (var n = 0; n < Entries && Noted(n); n++)
        {
            Places[n * Size + 5] = (byte)Colour(W(n * Size), W(n * Size + 2));
            Mark(n);
        }
    }

    /// <summary>$D17C: $234 = n; a noted, unlocked place (+4 = 0; all with $A5 bit 3, the new
    /// game's $D0FE): the colour under its mark back ($D1DA), the places after it moved up one
    /// ($D19A-$D1B0), the last one's name cleared; then $234 back to a noted place ($D1B6).</summary>
    public void Drop(int n)
    {
        Selected = (short)n;
        if (!Noted(n) || Places[n * Size + 4] != 0) return;                  // $D182, $D18C
        Draw(W(n * Size), W(n * Size + 2), Places[n * Size + 5]);
        for (var k = n; k < 0x10; k++)                                       // $D1AC SetPlace: the entry below, '*' at its
        {                                                                    // name's last byte ($D166) - an empty one's too
            Array.Copy(Places, (k + 1) * Size, Places, k * Size, Size);
            Places[k * Size + 6 + 15] = (byte)'*';
        }
        Places[0x10 * Size + 6] = 0;                                        // $D1B2 sf 6(a1)
        while (Selected != 0 && !Noted(Selected)) Selected--;
    }

    /// <summary>$D11A FreePlace: the first entry whose name byte +6 is 0, else -1.</summary>
    public int Free() => Enumerable.Range(0, Entries).FirstOrDefault(i => !Noted(i), -1);

    /// <summary>NotePlace ($D146) from Add Item: the first free entry gets x, z, +4 = d2 (0:
    /// unlocked), +5 the colour under it ($D1CA -> $405C), the name's 16 bytes, '*' at the last.</summary>
    public void Add(int x, int z, byte[] name)
    {
        var e = Free();
        if (e < 0) return;                                                   // $D148 bmi
        var o = e * Size;
        Places[o] = (byte)(x >> 8); Places[o + 1] = (byte)x; Places[o + 2] = (byte)(z >> 8); Places[o + 3] = (byte)z;
        Places[o + 4] = 0;
        Places[o + 5] = (byte)Colour(x, z);
        CopyName([.. name, (byte)'*'], 0);                                   // $D2A4: the typed name into $D3CC
        Array.Copy(Line, 0, Places, o + 6, 16);                              // $D160: past its '*', what the last line left
        Places[o + 6 + 15] = (byte)'*';                                      // $D166
    }

    /// <summary>The line buffer $D3CC (20 spaces in the game file, "&lt; " just before it at $D3CA): LogLineAndMark ($D400)
    /// builds each printed line in it - the name (CopyName), then " ]*" - and Add Item ($D29E) copies the typed name
    /// into it and notes 16 bytes of it ($D2D6), so past the typed '*' a new place keeps what the last printed line
    /// left. Kept per game here; the original's lasts the whole session.</summary>
    readonly byte[] Line = Enumerable.Repeat((byte)' ', 20).ToArray();

    // CopyName ($B80E, d0 15): up to 16 bytes into Line, stopping after a '*'; '*' over the last one copied
    // ($B81C move.b #'*',-(a1)). The '*''s place in Line.
    int CopyName(byte[] from, int at)
    {
        var i = 0;
        while (true)
        {
            var c = from[at + i];
            Line[i++] = c;
            if (c == '*' || i == 16) break;
        }
        Line[--i] = (byte)'*';
        return i;
    }

    /// <summary>Add Item's name field done ($D29E-$D2A4): the typed name into $D3CC - it stays there even when
    /// a number field is then left empty and nothing is noted.</summary>
    public void TypedName(byte[] name) => CopyName([.. name, (byte)'*'], 0);

    /// <summary>LogLineAndMark's text ($D3FE-$D416): the name copied into $D3CC, " ]*" after it, printed from
    /// $D3CA ("&lt; " first).</summary>
    public byte[] LineText(int n)
    {
        var i = CopyName(Places, n * Size + 6);
        Line[i] = (byte)' '; Line[i + 1] = (byte)']'; Line[i + 2] = (byte)'*';
        return [(byte)'<', (byte)' ', .. Line.AsSpan(0, i + 3)];
    }

    // $D428: colour 5, the selected place 7, mask bit set ($D432 bset #4), drawn by $4046
    public void Mark(int n) => Draw(W(n * Size), W(n * Size + 2), (n == Selected ? 7 : 5) | 0x10);

    void Set(int at, int bit, bool on)
    {
        if (on) Marks[at] |= (byte)(1 << bit); else Marks[at] &= (byte)~(1 << bit);
    }

    // $4076: byte (128 - (z >> 9)) * 16 + (x >> 9) / 8, bit 7 - (x >> 9 & 7)
    static (int At, int Bit) Spot(int x, int z) => ((128 - (z >> 9)) * 16 + ((x >> 9) >> 3), 7 - ((x >> 9) & 7));

    // $4046: bit k of d3 into plane k, planes 0-4
    void Draw(int x, int z, int d3)
    {
        var (at, bit) = Spot(x, z);
        for (var p = 0; p < 5; p++) Set(at + p * 0x800, bit, (d3 >> p & 1) != 0);
    }

    // $405C: the 5 planes' bits, assembled as sne / ror.w #1 / sf / rol.w #5 do it
    int Colour(int x, int z)
    {
        var (at, bit) = Spot(x, z);
        var d2 = 0;
        for (var p = 0; p < 5; p++)
        {
            d2 = d2 & 0xFF00 | ((Marks[at + p * 0x800] >> bit & 1) != 0 ? 0xFF : 0);
            d2 = (d2 >> 1 | d2 << 15) & 0xFFFF;
        }
        d2 &= 0xFF00;
        return (d2 << 5 | d2 >> 11) & 0xFFFF;
    }

    /// <summary>The map marks over the view ($3FFE, MainLoop $25A4 with $99 bit 2): frame 0 =
    /// the bitmap, BlitFrame mode 0 at (0, 0) (only where the mask is set); then the player at
    /// (x >> 9, 128 - (z >> 9)) in colour 4 ($400C, $4020). ENHANCEMENT, NOT THE ORIGINAL
    /// (Play.Graves): graves not null - after them, one mark per grave not taken - placed or
    /// still waiting in the store - at its X/Z through the same mapping ($4076 Spot), a
    /// five-pixel plus in colour 6. Every mark the original draws is a single pixel (a noted
    /// place 5, the selected place 7, the player 4) and the terrain fills all 16 colours as the
    /// background, so the free choice is the shape: no existing mark covers more than one pixel.</summary>
    public void DrawMarks(View v, Entity player, IEnumerable<Grave>? graves = null)
    {
        for (var row = 0; row < 128; row++)
            for (var col = 0; col < 128; col++)
            {
                int at = row * 16 + (col >> 3), bit = 7 - (col & 7);
                if ((Marks[0x2000 + at] >> bit & 1) == 0) continue;
                var c = 0;
                for (var p = 0; p < 4; p++) c |= (Marks[p * 0x800 + at] >> bit & 1) << p;
                v.Pixels[row * View.Width + col] = (byte)c;
            }
        int px = player.UW(0x4A) >> 9, pz = 128 - (player.UW(0x4E) >> 9);
        v.Pixels[pz * View.Width + px] = 4;
        if (graves != null)
            foreach (var g in graves) GraveMark(v, g);
    }

    // the grave's plus: its centre at ($4076) row 128 - (z >> 9), column x >> 9, colour 6 on the
    // centre and the 4 neighbours, clipped to the 128 x 128 marks area (a taken grave draws nothing)
    static void GraveMark(View v, Grave g)
    {
        if (g.Taken) return;
        int cx = g.X >> 9, cy = 128 - (g.Z >> 9);
        foreach (var (dx, dy) in stackalloc (int, int)[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int x = cx + dx, y = cy + dy;
            if ((uint)x < 128 && (uint)y < 128) v.Pixels[y * View.Width + x] = 6;
        }
    }
}

/// <summary>Play or a full screen the original waits in (re/formats/game.md 4).</summary>
public enum PlayMode { Play, Screen }

/// <summary>
/// The screens EndOfPass ($3476) opens over the view (re/formats/game.md 4). Each leaves play
/// through LeavePlay ($2E08: one more blank in play, then $99 bit 7 clear, so the blank counts
/// nothing and runs no game work), draws into the view buffer and waits in WaitMenuButton
/// ($1FA0) for a click on one of its buttons (Click).
/// MapScreen ($3CFA), after the drone: FlareOut if the flare burns ($3CFE), WriteBackAllIfOnFoot
/// ($3D0A: every slot back into the grid), DrawMapScreen ($3D7C) twice; a filter button
/// redraws with its mask and, once let go, the whole map again ($3D3C-$3D40); RESUME leaves
/// via $252A: view cleared to colour 15 ($CA96), $1A6 = $10001 ($2530: time step 1).
/// </summary>
public sealed class Screens
{
    const int MapMenu = 0x3C5A, Patterns = 0x3E7A, KindFrames = 0x3FB0, PlaneBytes = 0x15E0, RowBytes = 0x28;

    readonly WorldState _s;
    readonly Ui _ui;
    readonly byte[] _anim;
    public Screens(WorldState s, Ui ui, byte[] animation) { _s = s; _ui = ui; _anim = animation; }
    public Ui Ui => _ui;

    /// <summary>The screen routine showing ($3CFA MapScreen), 0 in play.</summary>
    public int Routine { get; private set; }

    /// <summary>The player the screen shows (ENHANCEMENT, NOT THE ORIGINAL): whose marks LogMarks
    /// ($3FFE) draws and whose position DrawMap ($3DE4) centres on. 0 is the original.</summary>
    public int ScreenPlayer;
    List<Menus.Button> _buttons = [];
    /// <summary>The buttons of the screen showing (tests press them by their text).</summary>
    public IReadOnlyList<Menus.Button> Buttons => _buttons;

    byte M(int addr) => _s.Data.Main[addr - 0x800];
    int MW(int addr) => (short)(M(addr) << 8 | M(addr + 1));
    int ML(int addr) => MW(addr) << 16 | (ushort)MW(addr + 2);

    /// <summary>StoreScreen ($9A78) after LeavePlay's blank: Store.cs (StorePicture, $9A78-$9D3A).</summary>
    internal void OpenStore() => Routine = 0x9A78;
    internal void CloseStore() => Routine = 0;

    /// <summary>MapScreen ($3CFA) after LeavePlay's blank: $3CFE bclr #0,$A2 -> FlareOut ($A7D4),
    /// WriteBackAllIfOnFoot ($33E6), $69E8(-16) / MouseLimits / $62B4(0) (fade, pointer, sky bands:
    /// colours only), DrawMapScreen ($3D2A, $3D40: drawn and swapped in twice). The fade calls
    /// (the jump down at $3D10, the FadeIn at $3D2C) run in Play's opening blank, rules/Fade.cs.</summary>
    public void OpenMap(View v)
    {
        Routine = 0x3CFA;
        _fullMapOnLetGo = false;
        if ((_s.Flags0A2 & 1) != 0) { _s.Flags0A2 &= 0xFE; _s.Light184 = 0; _s.Panel.Hour(_s); }   // $A7D4
        Play.WriteBackAllIfOnFoot(_s);
        DrawMap(v, 0x1F);
    }

    /// <summary>LogBook ($D2EC) after LeavePlay's blank and its fade-out ($D318, waited - Play
    /// stages the two): DrawLogBook into the buffer on show ($D31C $148 = $14C): cleared ($CA9E),
    /// the menu $D1F4 (Drop Item, Add Item, Resume), the lines' region ($D35E -> $AD9A: x $8C-$CE,
    /// y 0-$8C, value $D3C2), the list ($D374) in plane 1 ($D366). Resume ($D258) fades out first
    /// ($D300, waited) and leaves via $252A like the map; the buffer then on show is the one this
    /// pass drew its view into (LeaveLog puts it back, $D304).</summary>
    public void OpenLog(View v)
    {
        Routine = 0xD2EC;
        _kept = (byte[])v.Pixels.Clone();
        _sc = new IndexedScreen(View.Width, View.Height);
        _buttons = DrawMenuAndActivate(_sc, 0xD1F4);
        const int r = 0xD246;
        _buttons.Add(new Menus.Button(MW(r), MW(r + 2), MW(r + 4), MW(r + 6), ML(r + 12), ""));
        _text = new Text(_sc, _ui, _s.Data.Main);
        LogList();
        Show(v);
    }

    // $D304, once Resume's fade out ($D300 FadeToBlack, waited) is over: the view kept at the open
    // ($D2FA) comes back, the log screen closed - then out via $252A like the map
    public void LeaveLog(View v)
    {
        Routine = 0;
        Array.Copy(_kept, v.Pixels, v.Pixels.Length);
    }
    byte[] _kept = [];
    IndexedScreen _sc = new();
    Text _text = null!;

    void Show(View v) => Array.Copy(_sc.Pixels, v.Pixels, v.Pixels.Length);

    // $D374: window column $11, row 0, 13 columns, 18 lines of 8 in plane 1; ClearTextWindow
    // ($C328: 18 x 8 = 144 rows, the last 4 past the plane into plane 2's rows 0-3); each line
    // ($D3E0); then, a place selected ($234 <= 16, noted), its line again and the marks ($3FFE)
    void LogList()
    {
        _text.Window(0x11, 0, 0x0D, 0x12, 8, 1);
        for (var row = 0; row < 0x12 * 8; row++)
            for (var x = 0x11 * 8; x < (0x11 + 0x0D) * 8; x++)
                if (row < View.Height) _sc.SetPlaneBit(x, row, 1, false); else _sc.SetPlaneBit(x, row - View.Height, 2, false);
        var log = _s.Log;
        for (var n = 0; n <= 0x10; n++) LogLine(n);
        if (log.Selected > 0x10 || !log.Noted(log.Selected)) return;
        LogLine(log.Selected);
        LogLine(log.Selected);
        LogMarks();
    }

    // $D3E0: a noted place's line n: cursor (0, n), inverse when selected ($97 bit 7), "< " +
    // the name ($B80E, 16 at most) + " ]" printed (centred, '<'); then its mark ($D428)
    void LogLine(int n)
    {
        var log = _s.Log;
        if (!log.Noted(n)) return;                                           // $D3E4 beq $9060
        _text.X = 0;
        _text.Line = n;
        _text.Inverse = n == log.Selected;
        _text.Print(log.LineText(n));
        _text.Inverse = false;
        log.Mark(n);
    }

    // $3FFE on the log book's buffer: the marks bitmap and the screen's player
    void LogMarks()
    {
        var v = new View();
        Array.Copy(_sc.Pixels, v.Pixels, v.Pixels.Length);
        _s.Log.DrawMarks(v, _s.Entities.All[ScreenPlayer], _s.Graves ? _s.GraveStore : null);
        Array.Copy(v.Pixels, _sc.Pixels, v.Pixels.Length);
    }

    /// <summary>A click while the screen waits (WaitMenuButtonOrTimeout $1FA0 -> the button's value, jsr).
    /// True when the screen is left.</summary>
    public bool Click(View v, int x, int y)
    {
        if (Typing) { EndField(); Show(v); return false; }
        var b = Menus.Hit(_buttons, x, y);
        if (b is null) return false;
        if (Routine == 0xD2EC) return LogClick(v, b.Handler, y);
        // the map's buttons ($3D64-$3D78): moveq #n,d0 - RESUME -1, the filters 8/1/4/2/$10
        var d0 = (sbyte)M(b.Handler + 1);
        if (d0 < 0) return true;                                             // $3D4C bpl: RESUME - the caller fades out, then LeaveMap
        DrawMap(v, d0);                                                      // $3D7E: mask, then $3D84 on
        _fullMapOnLetGo = true;                                              // shown while the button is held: $3D3C WaitButtonsUp
        return false;
    }

    bool _fullMapOnLetGo;

    /// <summary>The mouse buttons let go ($AC74 ButtonsDown: $D4 &amp; $12 clear): after a filter, the map's loop
    /// leaves WaitButtonsUp ($3D3C) and draws the whole map again ($3D40 DrawMapScreen, mask $1F); after an item's
    /// press the table closes ($AC7E returns to $A5D2). True when the item table closed.</summary>
    internal bool LetGo(View v)
    {
        if (Routine == 0xA576 && _itemsOnLetGo) { CloseItems(); return true; }
        if (Routine != 0x3CFA || !_fullMapOnLetGo) return false;
        _fullMapOnLetGo = false;
        DrawMap(v, 0x1F);
        return false;
    }

    /// <summary>LeaveMapScreen $3D4E (RESUME, or the demo's 201 blanks): $252A clears the view to colour 15 ($CA96).</summary>
    internal void LeaveMap(View v) { Routine = 0; Array.Fill(v.Pixels, (byte)15); }

    /// <summary>The screen's own picture of the view rows while the item table shows (it draws
    /// into the buffer on show, $14C: the view the pass before drew); null otherwise.</summary>
    public byte[]? Overlay { get; private set; }

    /// <summary>ItemTable ($A576, the USE table $A5FA) after LeavePlay's blank: $AC58, mouse
    /// limits y $5C-$6C ($E0 0-108 from screen row 92), $E0 += $30, MovePanelSplit(-47) (copper),
    /// CarriedMasks ($A2A8), the 30 cells' icons into the buffer on show from row 124 up ($A946:
    /// an item not carried shows icon 9); then WaitItemClick ($A8E4).</summary>
    int _cells = 0xA5FA;

    public void OpenItems(byte[] shown)
    {
        Routine = 0xA576;
        _itemsOnLetGo = false;
        _s.PointerY += 0x30;                                                 // $A596
        Items.Carried(_s);                                                   // $A5A4
        Overlay = (byte[])shown.Clone();
        for (var item = 0; item < 30; item++)
        {
            var icon = (_s.Items238 >> item & 1) != 0 ? item : 9;            // $A990 btst d2,d3; moveq #9,d2
            for (var row = 0; row < 16; row++)
                for (var x = 0; x < 32; x++)
                    Overlay[(92 + item / 10 * 16 + row) * View.Width + item % 10 * 32 + x] = _s.Panel.IconPixel(icon, x, row);
        }
        _cells = 0xA5FA;                                                     // $A5B8: the USE cells
        if ((_s.Flags0A7 & 1) != 0)                                          // $A5BC bclr #0,$A7: GIVE
        {
            _s.Flags0A7 &= 0xFE;
            _s.PointerY -= 0x30;                                             // $A5C4
            _cells = 0xA6CE;                                                 // $A5CA: one cell, GiveItem $8F46
        }
        _s.Item240 = (short)Cell();                                          // $A90C; the hover ($A924) waits for a blank
    }

    // WaitItemClick ($A8EC): the cell under the pointer ($DE >> 5, $E0 >> 4: row 0, 1, else 2) -> $240
    int Cell()
    {
        int d0 = _s.PointerX >> 5, d1 = _s.PointerY >> 4;
        if (d1 != 0) d0 += d1 == 1 ? 10 : 20;
        return d0;
    }

    /// <summary>WaitItemClick's blank with no button ($A924): the pointer below $30 leaves; else
    /// the carried item under it shown in the panel's weapon spot (WeaponIcon $A96C). True when
    /// the table is left.</summary>
    public bool Hover()
    {
        var item = Cell();
        _s.Item240 = (short)item;                                            // $A90C
        if (_s.PointerY > 0x30) { CloseItems(); return true; }               // $A934 ble; rts
        if ((_s.Items238 >> item & 1) != 0) _s.Panel.WeaponIcon(_s, item);   // $A940 bne $A96C
        return false;
    }

    /// <summary>WaitItemClick's press ($A916): a carried item's cell runs (ClickDispatch $AF68 on $A5FA), then
    /// $A91C's return WaitButtonsUp ($AC7E): the table closes once the buttons are let go (LetGo) - at once when
    /// the press came with no button held (a caller that reports none, held: false). An item not carried: nothing.
    /// True when the table is closed.</summary>
    public bool ItemClick(int x, int y, bool held = false)
    {
        _s.PointerX = (short)x; _s.PointerY = (short)y;
        var item = Cell();
        _s.Item240 = (short)item;
        if ((_s.Items238 >> item & 1) == 0) return false;                    // $A91A beq $A8EC
        Keys.ClickDispatch(_s, _cells, x, y, 0);                             // d7: DrawItemCells' moveq #9 left its high word 0 (FireShot reads bits 25, 31)
        if (held) { _itemsOnLetGo = true; return false; }
        CloseItems();
        return true;
    }

    bool _itemsOnLetGo;

    // $A5D2-$A5F6: $AC58, $96 bit 7 and $99 bit 7 set, PlayMouseLimits, WeaponIcon($200), $E0 = 2,
    // MovePanelSplit(+47)
    void CloseItems()
    {
        Routine = 0;
        Overlay = null;
        _s.Clickable = true;
        _s.InPlay = true;
        _s.Panel.Weapon(_s);
        _s.PointerY = 2;
    }

    // the log book's buttons: d0 negative = stay ($D2FE bmi)
    bool LogClick(View v, int handler, int y)
    {
        var log = _s.Log;
        switch (handler)
        {
            case 0xD258:                                                     // Resume: moveq #0,d0 - the fade first ($D300),
                return true;                                                // the kept view comes back at $D304 (LeaveLog)
            case 0xD3C2:                                                     // a line: $E0 >> 3 -> $D39A
            {
                var n = y >> 3;
                if (n > 0x10 || !log.Noted(n)) return false;
                var old = log.Selected;
                log.Selected = (short)n;
                LogLine(old);
                LogLine(n);
                LogMarks();
                Show(v);
                return false;
            }
            case 0xD16E:                                                     // Drop Item: $D17C($234), the list again
                log.Drop(log.Selected);
                LogList();
                Show(v);
                return false;
            default:                                                         // $D278 Add Item
                if (log.Free() < 0) return false;                            // FreePlace bmi: LogStay
                _field = 0;
                StartField();
                Show(v);
                return false;
        }
    }

    // Add Item ($D278): TypeText (name, 16) into $D3CC (CopyName, 15 + '*'), TypeNumber (X-pos, 3)
    // into $B8, TypeNumber (Y-pos, 3); NotePlace(X << 8, Y << 8, unlocked). Nothing typed in any
    // field (TypedLength -1, $B4 bmi) skips the rest. Each field: SetTextWindow(1, 130, 30, 1, 8)
    // in plane 1 (DrawLogBook moved $154 to plane 1), the line prompt + '-' + typed + cursor
    // ($B41A, as Pregame.NameLine); ClearTextWindow ($C328) between fields and at the end, then LogList.
    static readonly int[] Prompts = [0xD264, 0xD26A, 0xD271];
    static readonly int[] Lengths = [0x10, 3, 3];
    int _field = -1, _x;
    byte[] _name = [];
    readonly Pregame.TypedLine _typed = [];

    /// <summary>A field of Add Item is being typed ($B41A / $B420 loop): keys go to TypeKey.</summary>
    public bool Typing => _field >= 0;

    void StartField()
    {
        _typed.Start(Lengths[_field], []);
        _text.Window(1, 0x82, 0x1E, 1, 8, 1);
        TypeLine(false);
    }

    /// <summary>The field's line with the cursor ('/' shown, ' ' hidden: 3 blanks each).</summary>
    public void TypeLine(bool cursorShown)
    {
        var line = new List<byte>();
        for (var a = Prompts[_field]; M(a) != '*' && line.Count < 0x3F; a++) line.Add(M(a));
        line.Add((byte)'-');
        line.AddRange(_typed);
        line.Add((byte)(cursorShown ? '/' : ' '));
        line.Add((byte)'*');
        _text.X = 0;
        _text.Line = 0;
        _text.Print([.. line]);
    }

    /// <summary>A key while a field is typed (KeyDown with $98 bit 7: only raw and ASCII kept; the
    /// loop's PollKey takes it): Pregame.Key's rules; Return or a full field ends it.</summary>
    public void TypeKey(View v, byte raw, byte ascii)
    {
        if (Pregame.Key(_typed, raw, ascii)) EndField();
        else TypeLine(false);
        Show(v);
    }

    /// <summary>A press while typing ends the field ($B49C: $D4 & $12).</summary>
    void EndField()
    {
        ClearField();
        if (_typed.Count == 0) { _field = -1; LogList(); return; }           // $B522 tst.w $1BC: bmi
        switch (_field)
        {
            case 0:                                                          // $B80E with d0 15: 16 bytes at most, the last '*'
                _name = _typed.Count > 15 ? [.. _typed.Take(15)] : [.. _typed];
                _s.Log.TypedName(_name);                                     // $D2A4 CopyName into $D3CC
                break;
            case 1:
                _x = Number();                                               // $D2B8 move.w d0,$B8
                break;
            default:
                _s.Log.Add((ushort)(_x << 8), (ushort)(Number() << 8), _name);   // lsl.w #8 each
                _field = -1;
                LogList();
                return;
        }
        _field++;
        StartField();
    }

    // ParseNumber ($C5B8): each character - '0' (a byte) times 1, 10, 100, 1000 ($C5B0), first
    // character the highest; letters and spaces count too
    int Number()
    {
        var n = 0;
        for (var i = 0; i < _typed.Count; i++)
            n += (byte)(_typed[i] - 0x30) * MW(0xC5B0 + 2 * (_typed.Count - 1 - i));
        return n;
    }

    // ClearTextWindow on the field's window: plane 1, x 8-247, rows 130-137
    void ClearField()
    {
        for (var row = 0x82; row < 0x82 + 8; row++)
            for (var x = 8; x < 8 + 0x1E * 8; x++) _sc.SetPlaneBit(x, row, 1, false);
    }

    // DrawMapScreen ($3D7C, mask $3FAE): $2000 clears the view buffer ($CA9E) and draws the menu
    // $3C5A (DrawMenuAndActivate $ACEA); $148 += $50 (the map from row 2); SetTextWindow(0,0,28,17,8) and
    // FillTextWindowPlanes ($C3E8: $FF into 4 planes): colour 15 over x 0-223, rows 2-137; the
    // terrain round the player ($3E3A), the grid's records by kind ($3F44), the player ($4020)
    void DrawMap(View v, int mask)
    {
        var sc = new IndexedScreen(View.Width, View.Height);
        _buttons = DrawMenuAndActivate(sc, MapMenu);
        var pl = new byte[4 * PlaneBytes + 2 * RowBytes];                    // planes $15E0 apart (spill past plane 3)
        for (var i = 0; i < View.Width * View.Height; i++)
            for (var p = 0; p < 4; p++)
                if ((sc.Pixels[i] >> p & 1) != 0) pl[p * PlaneBytes + i / 8] |= (byte)(0x80 >> (i & 7));
        const int top = 0x50;
        for (var p = 0; p < 4; p++)
            for (var r = 0; r < 17 * 8; r++)
                for (var c = 0; c < 28; c++) pl[p * PlaneBytes + top + r * RowBytes + c] = 0xFF;
        var player = _s.Entities.All[ScreenPlayer];
        int px = player.UW(0x4A), pz = player.UW(0x4E);                     // $29A, $29E
        // $3DE4-$3E0C: column (x >> 9) + 14, row (z >> 9) + 8, 17 rows of 28 cells right to left
        int col0 = (px >> 9) + 14, row = (pz >> 9) + 8;
        for (var r = 0; r <= 16; r++, row--)
        {
            var col = col0;
            for (var k = 27; k >= 0; k--)
            {
                col--;                                                       // $3E4A subq.w #1,d2
                if (col < 0 || col > 0x7F || row < 0 || row > 0x7F) continue;
                Tile(pl, top + r * 0x140 + k, _s.Terrain.Map[row * Terrain.RowBytes + col * 2 + 1]);
            }
        }
        Records(pl, top, px, pz, mask);
        // $3E12-$3E2E: the player in the middle cell: x 112 + (x >> 6 & 7), row 64 + (~(z >> 6) & 7)
        Dot(pl, top, 0x70 + (px >> 6 & 7), 0x40 + (~(pz >> 6) & 7));
        for (var i = 0; i < View.Width * View.Height; i++)
        {
            var c = 0;
            for (var p = 0; p < 4; p++) c |= (pl[p * PlaneBytes + i / 8] >> (7 - (i & 7)) & 1) << p;
            v.Pixels[i] = (byte)c;
        }
    }

    /// <summary>DrawMenuAndActivate ($ACEA): y word, then 26-byte entries (style, dx, text up to '*', +$12
    /// routine, +$16 value) up to a negative style; each a button ($AD08) and a region.</summary>
    List<Menus.Button> DrawMenuAndActivate(IndexedScreen sc, int at)
    {
        var buttons = new List<Menus.Button>();
        var y = MW(at);
        for (var e = at + 2; (sbyte)M(e) >= 0; e += 0x1A)
        {
            var text = "";
            for (var a = e + 2; M(a) != '*'; a++) text += (char)M(a);
            var item = new Ui.MenuItem(M(e), (sbyte)M(e + 1), text);
            var f = _ui.Frames[item.Style + 0x17];
            var x0 = (sc.Width >> 1) + item.Dx * 2 - (f.Width >> 1);
            var h = _ui.DrawMenuItem(sc, item, y);
            buttons.Add(new Menus.Button(x0, x0 + f.Width, y, y + h, ML(e + 0x16), text));
            y += h;
        }
        return buttons;
    }

    // $3EBA: cell type byte: pattern (type & $70) >> 1 of $3E7A read bottom up, colour type & $F
    void Tile(byte[] pl, int at, byte type)
    {
        var pat = Patterns + ((type & 0x70) >> 1);
        var c = type & 0xF;
        for (var j = 0; j < 8; j++, at += RowBytes)
        {
            var m = M(pat + 7 - j);                                          // $3EFA move.b -(a4),d3
            for (var p = 0; p < 4; p++)
            {
                ref var b = ref pl[p * PlaneBytes + at];
                b = (byte)(b & ~m | ((c >> p & 1) != 0 ? m : 0));
            }
        }
    }

    // $3F44: the grid's $800 records ($83BA), kind word +0 non-zero, x +6 / z +$A from the map's
    // corner ((x - $1C00) & ~$1FF, (z - $1000) & ~$1FF) >> 6 inside 224 x 136; $3FCA: frame
    // ($3FB0[kind & $F] - 1) * 2 + $1B when its bit is in the mask, at (x - 5, 136 - z - 4)
    /// <summary>Debug only (the live "map"): the marks the last map drew - the mask bit of its kind and its top-left
    /// on the map (pixels, 64 world units each); the player's dot is at 112 + (x >> 6 &amp; 7), 64 + (~(z >> 6) &amp; 7).</summary>
    public readonly List<(int Bit, int X, int Y)> MapMarks = new();

    void Records(byte[] pl, int top, int px, int pz, int mask)
    {
        MapMarks.Clear();
        int d5 = (px - 0x1C00) & ~0x1FF, d6 = (pz - 0x1000) & ~0x1FF;
        for (var r = 0; r < 0x800; r++) Record(_s.Grid, r * 16);
        if (_s.RoomyMemory)                                                  // ENHANCEMENT (roomy_memory): the cells' overflow
            foreach (var list in _s.Overflow.Values)                         // records too
                foreach (var rec in list) Record(rec, 0);

        void Record(byte[] g, int o)
        {
            var kind = g[o] << 8 | g[o + 1];
            if (kind == 0) return;
            int d0 = (g[o + 6] << 8 | g[o + 7]) - d5, d1 = (g[o + 10] << 8 | g[o + 11]) - d6;
            if (d0 < 0 || d1 < 0) return;
            d0 >>= 6; d1 >>= 6;
            if (d0 >= 0xE0 || d1 >= 0x88) return;
            var x = Math.Max(d0 - 5, 0);
            var y = Math.Max(0x88 - d1 - 4, 0);
            var d2 = MW(KindFrames + (kind & 0xF) * 2) - 1;
            if (d2 < 0 || (mask >> (d2 & 7) & 1) == 0) return;
            MapMarks.Add((d2 & 7, x, y));
            BlitMasked(pl, top, d2 * 2 + 0x1B, x, y);
        }
    }

    // BlitFrame ($C884) mode 0 (BLTCON0 $0FCA: D = A ? B : C): the frame's 4 planes where its
    // 5th plane (the mask) is set; skipped at x >= 320 / y >= 140, cut at row 140 ($C962)
    void BlitMasked(byte[] pl, int top, int frame, int x, int y)
    {
        int w = _anim[frame * 8], h = _anim[frame * 8 + 1];
        if (w == 0 || h == 0 || x >= 0x140 || y >= 0x8C) return;
        if (y + h > 0x8C) h = 0x8C - y;
        var src = 800 + (_anim[frame * 8 + 6] << 8 | _anim[frame * 8 + 7]) * 4;
        var plane = w * 2 * _anim[frame * 8 + 1];
        for (var j = 0; j < h; j++)
            for (var i = 0; i < w * 16; i++)
            {
                var sb = src + j * w * 2 + (i >> 3);
                var bit = 0x80 >> (i & 7);
                if ((_anim[sb + 4 * plane] & bit) == 0) continue;
                var at = top + (y + j) * RowBytes + ((x + i) >> 3);
                var db = (byte)(0x80 >> ((x + i) & 7));
                for (var p = 0; p < 4; p++)
                {
                    ref var b = ref pl[p * PlaneBytes + at];
                    b = (_anim[sb + p * plane] & bit) != 0 ? (byte)(b | db) : (byte)(b & ~db);
                }
            }
    }

    // $4020: colour 4 at (x, row) of the buffer: planes 0, 1, 3 cleared, plane 2 set
    static void Dot(byte[] pl, int top, int x, int row)
    {
        var at = top + row * RowBytes + (x >> 3);
        var bit = (byte)(0x80 >> (x & 7));
        for (var p = 0; p < 4; p++)
            pl[p * PlaneBytes + at] = p == 2 ? (byte)(pl[p * PlaneBytes + at] | bit) : (byte)(pl[p * PlaneBytes + at] & ~bit);
    }
}
