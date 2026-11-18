namespace Hunter.Rules;

/// <summary>
/// The game's text output, ported byte for byte: a text window ($608A sets $11C-$140), the
/// printer $C1BA/$C1FA ('*' end, ']' new line, '&lt;' centre, '}nn' skip nn pixels, '%' a/an,
/// a character with 6 or fewer pixels left starts a new line and is dropped, scrolling at the
/// bottom), and the formatter $B794 (argument codes for string tables and numbers).
/// Strings are bytes from the program, not .NET text.
/// </summary>
public sealed class Text
{
    readonly IndexedScreen _s;
    readonly Ui _ui;
    readonly byte[] _main;

    // window ($138 left px, $13A top row + plane, $13C width px, $13E last line, $116 line stride)
    int _left, _top, _width, _last, _lineH, _plane;
    /// <summary>$134: x of the next character (absolute pixels, as the original stores it).</summary>
    public int X;
    /// <summary>$136: line within the window.</summary>
    public int Line;
    /// <summary>$97 bit 3: wait a frame before each character ($C37A, $118 = 0).</summary>
    public bool Typewriter;
    /// <summary>$97 bit 7: characters drawn inverse (the log book's selected line, $D3F8).</summary>
    public bool Inverse;
    /// <summary>Called for each typewriter frame (the remake shows the screen then).</summary>
    public Action? Frame;
    /// <summary>What the printer's last drawn character left in d0 ($C2B8/$C2DC: the character's last
    /// glyph row, clr.l d0, move.b (a0)+,d0b, ror.l #8, lsr.l (x &amp; $F), eor.l d4). ShowMessage
    /// ($C1B2 -> FormatText $B794) restores d0, so the callee sees this value when it returns.</summary>
    public int LeftInD0 { get; private set; }

    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (MessageLog): while on, every line the window
    /// scrolls off its top is recorded - its width x line height of plane bits, oldest first, the
    /// last MessageHistoryLines kept. Recording only, and it lives in Scroll because Scroll alone
    /// holds the window's geometry and is the one place a line leaves ($C300/$C340): nothing of
    /// the game reads the list, and with it off nothing is recorded at all. The remake's
    /// Play.Screen draws from it when the player scrolls the window back.</summary>
    public bool MessageLog;
    public const int MessageHistoryLines = 64;
    public readonly List<byte[]> MessageHistory = new();

    /// <summary>Called when Print has put new text in the window (a message, which may have
    /// scrolled it): the remake's scroll-back position returns to live. Wired only while
    /// MessageLog is on.</summary>
    public Action? NewMessage;

    /// <summary>How far back the window can be scrolled: the whole history, so the two oldest
    /// kept lines can be the two the window shows.</summary>
    public int MaxMessageBack => MessageHistory.Count;

    /// <summary>The window's geometry, for drawing the history over a copy of the panel:
    /// $138 left in pixels, $13A top row, $13C width in pixels, $11C the line height.</summary>
    public int WindowLeftPx => _left;
    public int WindowTopRow => _top;
    public int WindowWidthPx => _width;
    public int WindowLineH => _lineH;

    public Text(IndexedScreen s, Ui ui, byte[] main)
    {
        _s = s;
        _ui = ui;
        _main = main;
    }

    /// <summary>$608A: column, pixel row, columns, lines, line height, plane. It falls through into
    /// TextCursorHome $60C0 (clr.l $134: X and Line together), so the cursor starts at the window's top left.</summary>
    public void Window(int col, int row, int cols, int lines, int lineHeight, int plane)
    {
        _left = col * 8;
        _top = row;
        _width = cols * 8;
        _last = lines - 1;
        _lineH = lineHeight;
        _plane = plane;
        X = 0;
        Line = 0;
    }

    /// <summary>$60C6: continue from the window's last line.</summary>
    public void CursorToLastLine() => Line = _last;

    /// <summary>$C1B2: format then print.</summary>
    public void Printf(int template, params int[] args) => Print(Format(template, args));

    /// <summary>$C1BA: print bytes (up to '*') at the text cursor.</summary>
    public void Print(byte[] text)
    {
        int d6 = X, d7 = Line;
        var row = _top + d7 * _lineH;                       // a3
        var d5 = _width - d6;
        d6 += _left;
        for (var p = 0; p < text.Length; p++)
        {
            int c = text[p];
            if (c == '*') break;                            // $C1FE
            if (c == ']' || (c != ']' && d5 <= 6))          // $C208-$C214: new line; the char is lost
            {
                NewLine(ref d5, ref d6, ref d7, ref row);
                continue;
            }
            if (c == '}')                                    // $C230
            {
                var t = text[p + 1] - '0';
                if (t < 0) continue;
                t *= 10;
                var u = text[p + 2] - '0';
                if (u < 0) { p += 1; continue; }
                t += u;
                p += 2;
                d5 -= t;
                if (d5 < 0) { NewLine(ref d5, ref d6, ref d7, ref row); continue; }
                d6 += t;
                continue;
            }
            if (c == '<')                                    // $C38C
            {
                var w = Measure(text, p + 1, ']');
                d5 = _width;
                d6 = (d5 >> 1) - (w >> 1);
                d5 -= d6;
                d6 += _left;
                continue;
            }
            if (c == '%')                                    // $C3A6: "a%" -> "an" before a vowel
            {
                text[p] = (byte)'n';
                var next = p + 2 < text.Length ? text[p + 2] : 0;
                if ("aeiouAEIOU".IndexOf((char)next) >= 0) p--;   // print the 'n'
                continue;
            }
            if (Typewriter) Frame?.Invoke();                 // $C37A
            var cw = _ui.Widths[c] + 1;
            _ui.DrawChar(_s, (char)c, d6, row, _plane, Inverse);
            LeftInD0 = _ui.GlyphD0((char)c, d6, Inverse);  // $C2B8: what this character leaves in d0
            d6 += cw;
            d5 -= cw;
        }
        X = d6;
        Line = d7;
        if (MessageLog) NewMessage?.Invoke();                    // ENHANCEMENT: a new message returns the scroll-back to live
    }

    // $C2EA
    void NewLine(ref int d5, ref int d6, ref int d7, ref int row)
    {
        d5 = _width;
        d6 = _left;
        d7++;
        if (d7 > _last) { Scroll(); d7 = _last; return; }   // $C340 reloads d7 = $13E
        row += _lineH;
    }

    // $C300/$C340: the window's plane moves up one line; 8 rows at the bottom are cleared
    void Scroll()
    {
        if (MessageLog) RecordLine();                            // ENHANCEMENT: the line leaving the top is kept
        var rows = _last * _lineH;
        for (var y = 0; y < rows; y++)
            for (var x = _left; x < _left + _width; x++)
                _s.SetPlaneBit(x, _top + y, _plane, Bit(x, _top + y + _lineH));
        for (var y = 0; y < 8; y++)
            for (var x = _left; x < _left + _width; x++)
                _s.SetPlaneBit(x, _top + rows + y, _plane, false);
    }

    // ENHANCEMENT (MessageLog): the window's top line before $C300 moves it up - width x lineH
    // plane bits of the text plane, one byte a pixel, the oldest dropped past 64. An empty line
    // (the window's two blank lines at the start) is not kept: scrolled all the way back the
    // window shows the oldest message, not a blank window (real_game_check_2)
    void RecordLine()
    {
        var line = new byte[_width * _lineH];
        for (var y = 0; y < _lineH; y++)
            for (var x = 0; x < _width; x++)
                line[y * _width + x] = Bit(_left + x, _top + y) ? (byte)1 : (byte)0;
        if (Array.TrueForAll(line, b => b == 0)) return;
        MessageHistory.Add(line);
        if (MessageHistory.Count > MessageHistoryLines) MessageHistory.RemoveAt(0);
    }

    bool Bit(int x, int y) => (uint)y < _s.Height && (_s.Pixels[y * _s.Width + x] >> _plane & 1) != 0;

    // $C3CA: sum of (width + 1) up to the terminator
    int Measure(byte[] t, int from, char stop)
    {
        var w = 0;
        for (var i = from; i < t.Length && t[i] != stop; i++) w += _ui.Widths[t[i]] + 1;
        return w;
    }

    // ------------------------------------------------------------ $B794 formatter

    byte M(int addr) => _main[addr - 0x800];
    int MW(int addr) => M(addr) << 8 | M(addr + 1);

    /// <summary>$B794: expand the template at addr with arguments d0-d7 into a printable string
    /// (it always starts with ']' and ends with '*').</summary>
    public byte[] Format(int addr, params int[] args) => FormatBytes(_main.AsSpan(addr - 0x800).ToArray(), args);

    /// <summary>$B794 on a template held as bytes (one the game has edited in place).</summary>
    public byte[] FormatBytes(byte[] template, params int[] args)
    {
        var o = new List<byte> { (byte)']' };
        var next = 0;
        for (var a = 0; ; a++)
        {
            int c = template[a];
            if (c == 0) break;
            if (c >= 0x80)
            {
                var arg = next < args.Length ? args[next] : 0;
                next++;
                if ((c & 0x70) != 0) Number(o, c, arg);      // $B7CC: bits 6-4 -> number
                else TableString(o, c & 0x1F, arg);
            }
            else if (c < 0x20) TableString(o, 0, c);
            else o.Add((byte)c);
        }
        o.Add((byte)'*');
        return o.ToArray();
    }

    /// <summary>$B854: string number index of table (word offsets at $B86E from $B88E).</summary>
    void TableString(List<byte> o, int table, int index) => o.AddRange(TableText(_main, table, index));

    /// <summary>FormatString ($B7DC): string index of table ($B854), then a space.</summary>
    public static byte[] TableText(byte[] main, int table, int index)
    {
        byte M(int addr) => main[addr - 0x800];
        if (table == 1 && ExtraKinds.Name(index & 0xFFFF) is { } extra)      // ENHANCEMENT (ExtraKinds): the extra types' names
            return [.. System.Text.Encoding.ASCII.GetBytes(extra), (byte)' '];
        // $B854 loop: `cmpi.b #'*',(a0)+; bne; dbra d1` skips index + 1 terminators from the
        // start of the table, so string 0 is the text after the table's first '*'
        var o = new List<byte>();
        var a = 0xB88E + (short)(M(0xB86E + table * 2) << 8 | M(0xB86F + table * 2));
        for (var i = 0; i <= (index & 0xFFFF); i++)
            while (M(a++) != '*') { }
        for (; M(a) != '*'; a++) o.Add(M(a));                // $B7DE
        o.Add((byte)' ');
        return o.ToArray();
    }

    // $B832 -> $C53A: 5 decimal digits (divu.w chain), skip (code & 7) of them, drop leading
    // zeros, then a space. A non-zero width also sign-extends the argument's low word.
    static void Number(List<byte> o, int code, int arg)
    {
        var skip = code & 7;
        var v = skip != 0 ? (short)arg : arg;
        var neg = v < 0;
        var d2 = (uint)(neg ? -v : v);
        var digits = new byte[5];
        var divs = new uint[] { 10000, 1000, 100, 10 };
        for (var i = 0; i < 4; i++)                          // divu.w, addi.b #'0', clr.w, swap
        {
            var q = d2 / divs[i];
            if (q <= 0xFFFF) d2 = (d2 % divs[i]) << 16 | q;   // an overflow leaves d2 as it was
            digits[i] = (byte)((d2 & 0xFF) + '0');
            d2 >>= 16;
        }
        digits[4] = (byte)((d2 & 0xFF) + '0');
        if (neg) o.Add((byte)'-');
        var zero = true;
        for (var i = skip; i < 5; i++)
        {
            if (zero && digits[i] == '0') continue;
            zero = false;
            o.Add(digits[i]);
        }
        if (zero) o.Add((byte)'0');
        o.Add((byte)' ');
    }
}
