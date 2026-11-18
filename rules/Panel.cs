using System.Text.Json;

namespace Hunter.Rules;

/// <summary>
/// The panel under the view (re/formats/game.md section 1): its own 60-row, 4-plane buffer ($150 =
/// $154 in play, planes $960 bytes apart), shown by the copper on screen rows 140-199. At start-up
/// $84AC copies panfile rows 0-59 into it; the game then draws into it with byte, long and bit
/// writes at fixed offsets (ported here as such) and the 8x8 panel font ($C60E). Nothing clears it
/// between draws: what a routine leaves stays until another writes over it.
/// </summary>
public sealed class Panel
{
    public const int Rows = 60, RowBytes = 40, PlaneBytes = Rows * RowBytes;     // $960
    /// <summary>The panel buffer as palette indices (320 x 60).</summary>
    public IndexedScreen Pixels { get; } = new(320, Rows);
    /// <summary>The 16 colours the copper loads at line $B6 (colour 0 is not reloaded: the view's).
    /// panfile's CMAP ($84AC: bank 4, copied to bank 3 by $64D8) faded in to 15 by the start's FadeIn
    /// ($251A); $69E8 at $2526 then takes only the view's colours back down, so the panel stays at 15.</summary>
    public ushort[] Palette { get; }
    /// <summary>panfile's CMAP as loaded: what the fade walks while it counts the panel's colours.</summary>
    public ushort[] Raw { get; }

    readonly byte[] _icons;            // storepic rows 0-47: 30 icons of 32 x 16 ($851C -> panel + $4B00 = $8362)
    readonly byte[][] _font;           // the panel font $837A (storepic row 104, plane 0; tables/font.json second)
    readonly byte[][] _mainFont;       // the main font $8372 (font.json main_hex), for drawing-only marks
    readonly byte[] _main;
    readonly byte[] _flash;            // $6CC2-$6CF9: 7 lamps of 8 bytes (state, kind, offset), the state toggles

    public Panel(GameData d)
    {
        var (pan, pal) = d.Picture("panfile");
        Array.Copy(pan, Pixels.Pixels, 320 * Rows);                          // $84E2 $8504: rows 0-59
        Raw = pal;
        Palette = pal.Select(c => Play.Faded(c, 15)).ToArray();
        _icons = d.Picture("storepic").Pixels;
        using var f = JsonDocument.Parse(File.ReadAllText(Path.Combine(d.Directory, "tables", "font.json")));
        _font = f.RootElement.GetProperty("second_hex").EnumerateArray().Select(g => Convert.FromHexString(g.GetString()!)).ToArray();
        _main = d.Main;
        _flash = d.Main.AsSpan(0x6CC2 - 0x800, 7 * 8).ToArray();
        var ui = new Ui                                                      // the main font $8372, widths $836A
        {
            Glyphs = f.RootElement.GetProperty("main_hex").EnumerateArray().Select(g => Convert.FromHexString(g.GetString()!)).ToArray(),
            Widths = f.RootElement.GetProperty("widths").EnumerateArray().Select(w => w.GetInt32()).ToArray(),
            Frames = new Dictionary<int, Frame>(),
        };
        Text = new Text(Pixels, ui, d.Main);
        _mainFont = ui.Glyphs;
    }

    /// <summary>The messages' printer ($C1B2) on the text plane of this buffer; its X / Line are
    /// the cursor $134 / $136 (Messages.cs).</summary>
    public Text Text { get; }

    // --- the buffer as the 68000 sees it: plane p, byte offset off = row * 40 + column ---
    int Byte(int p, int off)
    {
        if ((uint)off >= PlaneBytes) return 0;
        var v = 0;
        for (var i = 0; i < 8; i++) v |= (Pixels.Pixels[off * 8 + i] >> p & 1) << (7 - i);
        return v;
    }

    void SetByte(int p, int off, int v)
    {
        if ((uint)off >= PlaneBytes) return;
        for (var i = 0; i < 8; i++)
        {
            ref var px = ref Pixels.Pixels[off * 8 + i];
            px = (v >> (7 - i) & 1) != 0 ? (byte)(px | 1 << p) : (byte)(px & ~(1 << p));
        }
    }

    uint Long(int p, int off) => (uint)(Byte(p, off) << 24 | Byte(p, off + 1) << 16 | Byte(p, off + 2) << 8 | Byte(p, off + 3));

    void SetLong(int p, int off, uint v)
    {
        for (var k = 0; k < 4; k++) SetByte(p, off + k, (int)(v >> (24 - 8 * k)));
    }

    /// <summary>The set-up as play starts ($24FA, before the player is spawned at $24C8 $87B6):
    /// $9A3E, then PanelClock ($255A).</summary>
    public void SetUp(WorldState s)
    {
        Text.Window(1, 0x29, 0x26, 2, 9, 0);                                 // $2504 SetScreenMode(3) $615E $6036 -> $608A: mode 3's window ($5FDA +$22/$24), cursor 0
        ClearMessages();                                                     // $9A3E $C328
        s.WeaponBefore = 0;                                                  // $9A42 clr $202
        var energy = s.Energy;                                               // $9A46: $22A from the soldier record ($8700 $87A0)
        s.Energy = 0;
        Vehicles.AddEnergy(s, energy);                                       // $9A4E: the bar
        Radar(s);                                                            // $9A52 $A480
        Score(s);                                                            // $9A56 $9A2C
        s.Flags0A3 = 0;                                                      // $9A58 sf $A3
        Flash(s);                                                            // $9A5C $6CFA
        Text.CursorToLastLine();                                                   // $2556 $60C6: the cursor on the last line ($13E)
        Clock(s);                                                            // $255A jmp PanelClock
    }

    /// <summary>$C328 (d4 = 0) on SetScreenMode(3)'s text window ($138 x 8, $13A byte 1640 = row
    /// 41, $13C 304 wide, $13E one line of $124 = 9 rows): $C340, then $C32A clears
    /// ($13E * 9 - 1 + 9) + 1 = 18 rows of 38 bytes in the text plane ($154 = plane 0).</summary>
    void ClearMessages()
    {
        for (var row = 41; row < 41 + 18; row++)
            for (var b = 1; b <= 38; b++) SetByte(0, row * RowBytes + b, 0);
    }

    /// <summary>UpdatePanel ($9A60), once a pass after the draw. Multiplayer: n = whose panel
    ///; 0 is the original.</summary>
    public void Update(WorldState s, Entity p, int n = 0)
    {
        FuelGauge(s, p, n);                                                  // $9A60 $A3C6
        if (s.Radar) RadarDots(s, p);                                        // $9A64 $99 bit 3 -> $A4E0
        RoundsShownSyncStock(s, p, n);                                                // $9A70 $A3A0
        Position(p);                                                         // $9A74 $C4E6
    }

    // FuelGauge ($A3C6): the fuel byte (+$34) as a bar of fuel / 2 pixels in plane 2 of the long at
    // $5C2 (row 36, x 272-303), only when it changed ($228: this player's own byte, mp piece 2);
    // on reserve (motorised, <= 4) the lamp flashes ($A3 bit 5) and ReserveWarning ($A420) follows.
    void FuelGauge(WorldState s, Entity p, int n)
    {
        var fuel = p.B[0x34];                                                // $A3C6 $284
        var me = s.PlayerAt(n);
        if (fuel == me.Fuel228) return;
        me.Fuel228 = fuel;
        s.Flags0A3 &= 0xDF;                                                  // $A3D6
        var reserve = p.Bit(0x06, 26) && (sbyte)fuel <= 4;                   // $A3DC btst #$1A,$256 (= +$06 bit 26)
        if (reserve) s.Flags0A3 |= 0x20;                                     // $A3EA
        var shift = (fuel >> 1) & 63;                                        // $A3FE lsr.b #1, lsr.l d1 (count mod 64)
        SetLong(2, 0x5C2, ~M68.Lsr(0xFFFFFFFF, shift));                      // $A3F4-$A404: plane 2 ($12C0) + $5C2
        if (!reserve) return;
        Messages.Show(s, 0xA40A);                                            // $A3F0 pea ReserveWarning $A420: "low on fuel" ($A40A)
        s.Channels.Play(2, s.Entities);                                      // $A428 $B116
    }

    /// <summary>AddEnergy's bar ($A43A-$A462): plane 2, from $568 (row 34, x 192) up 32 rows, bit 7
    /// set for the first energy + 1 rows (dbf on d0), bits 7-6 clear above.</summary>
    public void EnergyBar(int energy)
    {
        var off = 0x568;
        var d0 = energy;
        var d2 = 0x80;
        for (var k = 0; k < 32; k++, off -= RowBytes)
        {
            SetByte(2, off, Byte(2, off) & 0x3F | d2);
            if (--d0 == -1) d2 = 0;                                          // $A456 dbf d0 (word)
        }
    }

    /// <summary>RoundsShownSyncStock ($A3A0): the player's rounds (+$32, $282) when they changed ($226):
    /// into the weapon's item count, then two digits at column 27, row 4 ($A3BE).</summary>
    void RoundsShownSyncStock(WorldState s, Entity p, int n)
    {
        var rounds = p.W(0x32);
        if (rounds == s.Rounds226) return;                                   // $A3A8 beq $9064 (rts)
        s.Rounds226 = rounds;
        var o = 0x28 + s.Weapon * 32;                                        // $A3B0 $9E46 + $200 * 32
        var t = s.PlayerAt(n).Items;                                         // piece 4: this panel's player's table
        t[o] = (byte)(rounds >> 8); t[o + 1] = (byte)rounds;
        Number(0x1B, 4, rounds, 3);                                          // $A3BE $C542
    }

    /// <summary>$A96C (from LoadWeapon $A37C, d0 = $200): the weapon's icon at row 1, x 240
    /// ($150 + $46, $A99C), then its item count at column 27, row 4 ($A3BE).</summary>
    public void Weapon(WorldState s) => WeaponIcon(s, s.Weapon);

    /// <summary>WeaponIcon ($A96C) with d0 = item: also the item under the pointer in the item
    /// table (WaitItemClick $A93C). mp_life: n's own pack's count (0 = the original's array).</summary>
    public void WeaponIcon(WorldState s, int item, int n = 0)
    {
        Icon(item, 0x46);
        var o = 0x28 + item * 32;                                            // $A982 $9E46 + d0 * 32
        var t = s.PlayerAt(n).Items;
        Number(0x1B, 4, (short)(t[o] << 8 | t[o + 1]), 3);
    }

    /// <summary>One line of a character of the panel font ($837A).</summary>
    public byte FontLine(int c, int line) => _font[c][line];
    /// <summary>How many glyphs the panel font holds.</summary>
    public int FontGlyphs => _font.Length;

    /// <summary>One line of a character of the main font ($8372), for drawing-only marks over
    /// the panel (Play's scrolled-back mark by the message window).</summary>
    public byte MainFontLine(int c, int line) => _mainFont[c][line];

    /// <summary>A pixel of icon n (storepic rows 0-47, ten a row of 32 x 16; $851C).</summary>
    public byte IconPixel(int icon, int x, int row) => _icons[(icon / 10 * 16 + row) * 320 + icon % 10 * 32 + x];

    // $A99C: icon d2 ([$8362] + d2 * 256: 16 rows of 4 planes x a long) to a1, planes $A8 = $960 apart
    void Icon(int icon, int at)
    {
        int ix = icon % 10 * 32, iy = icon / 10 * 16;
        for (var row = 0; row < 16; row++)
            for (var x = 0; x < 32; x++)
            {
                var v = _icons[(iy + row) * 320 + ix + x];
                var px = (at + row * RowBytes) * 8 + x;
                if ((uint)px < Pixels.Pixels.Length) Pixels.Pixels[px] = v;
            }
    }

    /// <summary>$9A2C (set-up, and ScoreKill $9A16 falls into it): the score $21A as a sign and five
    /// digits at column 27, row 29 ($C53A: no ext.l; the sign goes in front, $9A34).</summary>
    public void Score(WorldState s)
    {
        var (digits, sign) = Digits(s.Score);
        PanelText(0x1B, 0x1D, [sign, .. digits]);
    }

    /// <summary>$C4E6 (end of UpdatePanel): the player's x and z ($29A, $29E) >> 8 as three digits
    /// at column 4, rows 8 and 24 ($C546).</summary>
    void Position(Entity p)
    {
        Number(4, 8, p.UW(0x4A) >> 8, 2);
        Number(4, 0x18, p.UW(0x4E) >> 8, 2);
    }

    /// <summary>PanelClock ($C43A): day + 1 at column 9, row 19, its suffix ("st", "nd", "rd", "th":
    /// $C4BE + 4 * byte $C4CE[day]) at column 11, then PanelHour.</summary>
    public void Clock(WorldState s)
    {
        ClockField(s, 9, 0x13, s.Day + 1);                                   // $C43A-$C444
        var k = _main[0xC4CE - 0x800 + s.Day];                               // $C44E move.b $10(a0,d0.w)
        PanelText(0xB, 0x13, Until(0xC4BE + k * 4));                         // $C452-$C45A $C60E
        Hour(s);
    }

    /// <summary>PanelTimeAndLight ($C45E): $C466 sets the light $18F from the 24-hour table at $C4A4,
    /// then the hour at column 9, row 8, then the minutes; $C472-$C47C calls FadeToLight(2) while
    /// no flare burns ($A2 bit 0) - Play's blank issues that once per light change (rules/Fade.cs, $68DC).
    /// The table, hours 0-23: 6 5 5 6 7 9 11 13 14 14 15 15 15 15 15 15 14 14 13 12 11 11 10 8 (15 = full light).</summary>
    public void Hour(WorldState s)
    {
        s.DaylightLevel = _main[0xC4A4 - 0x800 + s.Hour];                   // $C462-$C466: the table's byte, no wrap (a save at hour 24/25 reads bytes 24/25)
        ClockField(s, 9, 8, s.Hour);
        Minute(s);
    }

    /// <summary>$C480: the minutes at column 12, row 8.</summary>
    public void Minute(WorldState s) => ClockField(s, 0xC, 8, s.Minute);

    // $C488: the value only while the clock is on ($A7 bit 6, ClockToggle $A702), else 0; $C542
    void ClockField(WorldState s, int col, int row, int v) => Number(col, row, s.ClockOn ? v : 0, 3);

    /// <summary>BlankTimers ($6C46): while any $A3 bit is set, every 16th blank ($220 from 15) the
    /// lamps step ($6CFA).</summary>
    public void Blank(WorldState s)
    {
        if (s.Flags0A3 == 0 || --s.Timer220 >= 0) return;
        s.Timer220 = 0xF;
        Flash(s);
    }

    /// <summary>Lamp n's state byte (0 or $FF), as $6CFA last left it.</summary>
    public byte LampState(int n) => _flash[n * 8];

    /// <summary>For the card-drawn panel (panel.gdshaderinc): the lamps' state bits (bit n = lamp n lit now), the lamps $6CFA
    /// has drawn at least once, and those that have been lit at least once (a lit kind 1 lamp is never cleared).</summary>
    public int LampBits { get { var m = 0; for (var n = 0; n < 7; n++) if (_flash[n * 8] != 0) m |= 1 << n; return m; } }
    public int LampDrawn { get; private set; }
    public int LampLit { get; private set; }

    /// <summary>The day's suffix as PanelClock prints it ($C4BE + 4 * $C4CE[day]): its first two characters.</summary>
    public (byte, byte) DaySuffix(int day)
    {
        var t = Until(0xC4BE + 4 * _main[0xC4CE - 0x800 + day]);
        return (t.Length > 0 ? t[0] : (byte)32, t.Length > 1 ? t[1] : (byte)32);
    }

    // $6CFA: lamps 6..0 ($6CF2 down in 8s): one lit, or whose $A3 bit is set, toggles ($6D1E)
    void Flash(WorldState s)
    {
        for (var n = 6; n >= 0; n--)
        {
            var e = n * 8;
            if (_flash[e] == 0 && (s.Flags0A3 >> n & 1) == 0) continue;
            _flash[e] ^= 0xFF;                                               // $6D12 not.b
            LampDrawn |= 1 << n;
            if (_flash[e] != 0) LampLit |= 1 << n;
            s.Flags0A3 |= 0x80;                                              // $6D1E
            int d6 = _flash[e], off = _flash[e + 2] << 8 | _flash[e + 3];   // $6D2A: plane 3 ($1C20) + the offset
            if (_flash[e + 1] == 0) { SetByte(3, off, d6); continue; }       // $6D34: plane 3 ($1C20) byte
            d6 &= 0x0C;                                                      // $6D38: pixels 4-5, 14 rows
            for (var k = 0; k < 14; k++, off += RowBytes) SetByte(3, off, Byte(3, off) & ~d6 | d6);
        }
    }

    /// <summary>$A480 (set-up, RadarToggle $3540): radar off ($99 bit 3 clear) clears planes 0-2 of
    /// the 32 x 32 at $B2 (row 4, x 144); on fills plane 0, draws the cross ($A544) and clears plane 1.</summary>
    public void Radar(WorldState s)
    {
        if (!s.Radar)
        {
            for (var p = 0; p < 3; p++) RadarFill(p, 0);
            return;
        }
        RadarFill(0, 0xFFFFFFFF);
        for (var d7 = 31; d7 >= 0; d7--) RadarFlip(16, d7);                  // $A54E: bit 16 of every row
        for (var d7 = 31; d7 >= 0; d7--) RadarFlip(d7, 16);                  // $A55A: every bit of row 16
        RadarFill(1, 0);                                                     // $A4A8
    }

    /// <summary>Debug only (the live "radar"): the 32 rows of dots (plane 1 at $B2), row 0 north; bit 31 west.</summary>
    public uint[] RadarRows() => Enumerable.Range(0, 32).Select(row => Long(1, 0xB2 + row * RowBytes)).ToArray();

    // $A4AE: a long in rows 4-35 at $B2 of plane p
    void RadarFill(int p, uint v)
    {
        for (var row = 0; row < 32; row++) SetLong(p, 0xB2 + row * RowBytes, v);
    }

    // $A566: bchg bit d0 of the long at row d1
    void RadarFlip(int bit, int row) => SetLong(0, 0xB2 + row * RowBytes, Long(0, 0xB2 + row * RowBytes) ^ 1u << bit);

    // $A4E0: plane 1 cleared ($A4A8), then a dot for every live entity of slots 1-29 with +$06
    // bit 27 within $1000 west / north of this panel's player pl ... ($A510-$A536: bit (x + $1000 -
    // its x) >> 8 of row (z + $1000 - its z) >> 8, both 0-31). ENHANCEMENT: pl is the panel's own
    // player (0 today); the loop below skips every player slot.
    void RadarDots(WorldState s, Entity pl)
    {
        RadarFill(1, 0);
        int d5 = pl.UW(0x4A) + 0x1000, d6 = pl.UW(0x4E) + 0x1000;              // Dist wraps the difference back to the word with Maths32 off
        for (var n = s.Players; n < s.Entities.Count; n++)                   // $A4EA $2F0 = slot 1, d7 = 28 (slots 1-29; 1-393 with Play.MoreObjects); the player slots are skipped (1 today)
        {
            var e = s.Entities.All[n];
            if (!e.Alive || (e.B[0x06] & 0x08) == 0) continue;               // $A508 btst.b #$1B -> bit 3 of +$06
            RadarDot(s, d5, d6, e.UW(0x4A), e.UW(0x4E));
        }
        if (s.Graves)                                                        // (Play.Graves):
            foreach (var g in s.GraveStore)                                  // one dot per grave not taken, mapped exactly
                if (!g.Taken) RadarDot(s, d5, d6, (ushort)g.X, (ushort)g.Z);    // like a live thing. A grave live as a $7C entity
    }                                                                        // gets no second dot: the kind-table entry its
                                                                             // spawn reads ($ED8E) has +$06 = $00850000, bit 27
                                                                             // clear, and nothing in the port sets that bit on a
                                                                             // live record - RadarDots never draws for it, so the
                                                                             // store dot is the one dot the standing grave shows.

    // $A510-$A536: the dot of a thing at its x/z - bit (x + $1000 - its x) >> 8 of row
    // (z + $1000 - its z) >> 8, plane 1, both 0-31 (outside: no dot)
    void RadarDot(WorldState s, int d5, int d6, int x, int z)
    {
        var dx = WorldState.Dist(s, d5, x);
        if (dx < 0) return;
        var dz = WorldState.Dist(s, d6, z);
        if (dz < 0) return;
        dx >>= 8; dz >>= 8;
        if (dx > 31 || dz > 31) return;
        var off = 0xB2 + dz * RowBytes;
        SetLong(1, off, Long(1, off) | 1u << dx);
    }

    // $C542 / $C546: ext.l d2, five digits ($C556), the last 2 (d3 = 3) or 3 (d3 = 2) drawn by $C60E
    void Number(int col, int row, int v, int skip)
    {
        var (digits, _) = Digits((short)v);
        PanelText(col, row, digits[skip..]);
    }

    /// <summary>$C55C-$C5AE: d2 into five ASCII digits at $C522 by divu.w 10000, 1000, 100, 10
    /// (a quotient over $FFFF leaves d2 as it was, as divu does); negative: '-' and negated.</summary>
    public static (byte[] Digits, byte Sign) Digits(int d2)
    {
        byte sign = (byte)'+';
        var v = (uint)d2;
        if (d2 < 0) { sign = (byte)'-'; v = (uint)-d2; }
        var o = new byte[5];
        var k = 0;
        foreach (var div in Divisors)
        {
            if (v / div <= 0xFFFF) v = (v % div) << 16 | v / div;            // divu.w
            o[k++] = (byte)(v + 0x30);                                       // addi.b #$30, move.b
            v = (v & 0xFFFF0000) >> 16;                                      // clr.w, swap
        }
        o[k] = (byte)(v + 0x30);
        return (o, sign);
    }
    static readonly uint[] Divisors = [10000, 1000, 100, 10];

    // a '*'-terminated string from the program
    byte[] Until(int addr)
    {
        var o = new List<byte>();
        for (var a = addr - 0x800; _main[a] != '*'; a++) o.Add(_main[a]);
        return o.ToArray();
    }

    /// <summary>$C60E: characters of the panel font ($837A, 8 bytes each) into the text plane ($154 =
    /// plane 0) at byte column col, pixel row row, one byte a line (stride $5C = 40).</summary>
    void PanelText(int col, int row, byte[] text)
    {
        var off = row * RowBytes + col;
        foreach (var c in text)
        {
            for (var line = 0; line < 8; line++) SetByte(0, off + line * RowBytes, _font[c][line]);
            off++;
        }
    }
}
