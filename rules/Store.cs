using System.Text.Json;

namespace Hunter.Rules;

/// <summary>
/// A store (a building with +$90 negative, re/formats/buildings.md section 2 step 5): its own
/// screen where the player takes stock into the carried counts and puts it back. The stock lives
/// in the item table ($9E46, 32 bytes an item): +0 carried, +2 the most carried, +4 what the screen
/// offers, and one column per store at +6 + 2 * k (what store k keeps). Opening moves store k's
/// column into +4 (StoreOpen $9E00), closing moves +4 back (StoreClose $9E08). The click regions
/// are the table at $9AE2 (x1 x2 y1 y2 routine value): the whole screen ($9B82: put one back), the
/// count row ($9B54: take one), CLEAR ($9BCA), AUTO ($9BF0), END ($9B46), the name row ($9D26).
/// The picture is StorePicture below, the item's message ($9D3A) included.
/// </summary>
public static class Store
{
    const int Regions = 0x9AE2, Grid = 0x9D02;

    static int Item(int i) => 0x28 + i * 32;
    static short W(WorldState s, int o) => (short)(s.Items[o] << 8 | s.Items[o + 1]);
    static void Set(WorldState s, int o, int v) { s.Items[o] = (byte)(v >> 8); s.Items[o + 1] = (byte)v; }

    /// <summary>$9E00 (d0 = 2k + 6, d1 = 4) / $9E08 (exchanged): column d0 into column d1 of every
    /// item (the flag $9E44, set only by $9DE6, would add; clear here), the source cleared, then
    /// CarriedMasks ($A2A8).</summary>
    static void MoveColumn(WorldState s, int from, int to)
    {
        for (var i = 0; i < 30; i++)
        {
            var o = Item(i);
            Set(s, o + to, W(s, o + from));
            Set(s, o + from, 0);
        }
        Items.Carried(s);
    }

    /// <summary>StoreOpen ($9E00), then the screen's set-up ($9A78 -> $9AA8: the picture; it reads the
    /// usable mask $23C that StoreOpen's CarriedMasks has just set).</summary>
    public static void Open(WorldState s, int k)
    {
        MoveColumn(s, k * 2 + 6, 4);
        s.StorePicture = new StorePicture(s);
    }

    public static void Close(WorldState s, int k)
    {
        MoveColumn(s, 4, k * 2 + 6);
        s.StorePicture = null;
    }

    /// <summary>A press on the store's screen: ClickDispatch ($AF6E) on the regions; true once END ran. What the
    /// loop drew before the press came (StoreButtons) is drawn first, what the press asked for after.
    /// While the item's message waits ($9D3A's WaitClick, clicks off: $9D66 bclr #7,$96) the press only
    /// ends it: no region runs, and what it left pending (the grid: bit 4) is drawn by the Process above.</summary>
    public static bool Click(WorldState s, int x, int y)
    {
        s.StoreDone = false;
        s.StorePicture?.Process();
        if (s.StorePicture is { MessageUp: true } message)
        {
            message.MessageUp = false;                                                 // $9D6C-$9D74: the press is discarded
            return false;
        }
        s.StorePicture?.Process();
        Keys.ClickDispatch(s, Regions, x, y, 0);
        s.StorePicture?.Process();
        return s.StoreDone;
    }

    /// <summary>The routines of the regions (Keys.Run): d0 = x and d1 = y within the region.</summary>
    internal static void Run(WorldState s, int routine, int d0, int d1)
    {
        switch (routine)
        {
            case 0x9B46: s.Clickable = false; s.StoreDone = true; return;                  // END: bclr #7,$96, bclr #7,$9D
            case 0x9B54: Take(s, d0); return;
            case 0x9B82: Return(s, d0, d1); return;
            case 0x9BCA: ClearCounts(s); s.StorePicture?.Redraw(); return;                 // pea $9C4C
            case 0x9BF0: Auto(s); return;
            case 0x9D26:                                                                    // lsr.w #5,d0: carry = bit 4 of x
                if ((d0 >> 4 & 1) == 0) return;                                             // the cell's left half says nothing
                s.StorePicture?.Info(d0 >> 5);                                              // $9D2A-$9D32: $242 = cell + page * 10
                return;
            default: throw new NotSupportedException($"${routine:X}: store region routine not read");
        }
    }

    // $9B54: the count row's cell (x / 32): one from the offer (+4, above 0) into the carried count
    // (+0), unless that would pass the most carried (+2)
    static void Take(WorldState s, int d0)
    {
        var o = Item(d0 >> 5);
        if (W(s, o + 4) <= 0) return;                                                      // $9B62 ble
        var n = W(s, o) + 1;
        if (n > W(s, o + 2)) return;                                                       // $9B6E bgt
        Set(s, o, n);
        Set(s, o + 4, W(s, o + 4) - 1);
        s.StorePicture?.Redraw();                                                          // $9B7C bra $9C4C
    }

    // $9B82: the 3 x 3 grid's cell under the click (x within 32 to the right of $9D02's x * 8, y
    // within 16 below its y): one back from the carried count to the offer
    static void Return(WorldState s, int d0, int d1)
    {
        for (var k = 0; k < 9; k++)
        {
            var gx = (short)(s.Data.Main[Grid - 0x800 + k * 4] << 8 | s.Data.Main[Grid - 0x800 + k * 4 + 1]) << 3;
            var gy = (short)(s.Data.Main[Grid - 0x800 + k * 4 + 2] << 8 | s.Data.Main[Grid - 0x800 + k * 4 + 3]);
            var dx = (short)(gx - d0);
            if (dx >= 0) continue;                                                         // $9B92 bpl
            var dy = (short)(gy - d1);
            if (dy >= 0) continue;                                                         // $9B96 bpl
            if (dx <= -0x20 || dy <= -0x10) continue;                                      // $9B98 ble, $9B9E bgt
            var o = Item(k);
            if (W(s, o) <= 0) return;                                                      // $9BBC ble
            Set(s, o, W(s, o) - 1);
            Set(s, o + 4, W(s, o + 4) + 1);
            s.StorePicture?.Redraw();                                                      // $9BC6 bra $9C4C
            return;
        }
    }

    // $9BCE: every carried count of the 10 items on show back into the offer
    static void ClearCounts(WorldState s)
    {
        for (var i = 0; i < 10; i++)
        {
            var o = Item(i);
            Set(s, o + 4, W(s, o + 4) + W(s, o));
            Set(s, o, 0);
        }
    }

    // $9BF0: clear, then of each item the most carried (+2) or what the offer holds, if less
    static void Auto(WorldState s)
    {
        ClearCounts(s);
        for (var i = 0; i < 10; i++)
        {
            var o = Item(i);
            int stock = W(s, o + 4), max = W(s, o + 2);
            var d1 = stock - max;
            var d0 = max;
            if (d1 < 0) { d1 += max; d0 = d1; d1 = 0; }                                    // $9C08-$9C0C
            Set(s, o + 4, d1);
            Set(s, o, d0);
        }
        s.StorePicture?.Redraw();                                                          // $9C1E bra $9C4C
    }
}

/// <summary>The store's screen (mode 4: 320 x 200, 4 planes $1F40 apart) as the loop of $9A7A draws it:
/// StoreSetUp ($9AA8) clears it and copies panfile's rows 60-81 and 82-119 (the $835A buffer,
/// $9DAA) to rows 0-21 and 162-199; $9D bit 6 (StoreSelect(0)) makes the first StoreButtons ($9A88)
/// draw the 10 count lines ($9C2C: icon and carried count of items 0-9 at row 172, the number in
/// plane 0 at row 163) and the 3 x 3 grid ($9CA0: rows 22-161 cleared, then the icon and the offer
/// of items 0-8 at the cells of $9D02). Each StoreButtons call does one of the bits 6, 5, 3. A change
/// ($9C4C) draws the lines again at once and asks for the grid (without clearing it first).
/// Bit 3 is the item's message ($9D3A): the text area (the mode 4 window: rows 22-161, x 0,
/// 14 lines of 10 rows, plane 0) is cleared and the item's entry printed through the main font,
/// then the screen waits for a press.</summary>
public sealed class StorePicture
{
    readonly WorldState _s;
    readonly Text _text;
    public readonly IndexedScreen Screen = new(320, 200);
    bool _select = true, _grid, _clearText, _info;
    int _infoItem;

    public StorePicture(WorldState s)
    {
        _s = s;
        using var f = JsonDocument.Parse(File.ReadAllText(Path.Combine(s.Data.Directory, "tables", "font.json")));
        _text = new Text(Screen, new Ui                                                      // the main font $8372, widths $836A
        {
            Glyphs = f.RootElement.GetProperty("main_hex").EnumerateArray().Select(g => Convert.FromHexString(g.GetString()!)).ToArray(),
            Widths = f.RootElement.GetProperty("widths").EnumerateArray().Select(w => w.GetInt32()).ToArray(),
            Frames = new Dictionary<int, Frame>(),
        }, s.Data.Main);
        _text.Window(0, 22, 40, 14, 10, 0);                      // mode 4's window ($610C $6036: the table at $5F50 +$24)
        var pan = s.Data.Picture("panfile").Pixels;
        for (var y = 0; y < 22; y++) Array.Copy(pan, (60 + y) * 320, Screen.Pixels, y * 320, 320);           // $9DB8: 22 rows
        for (var y = 0; y < 38; y++) Array.Copy(pan, (82 + y) * 320, Screen.Pixels, (162 + y) * 320, 320);   // $9DC2: 38 rows
    }

    /// <summary>The press on a name row's right half asked for an item's message ($9D26); the loop's
    /// next StoreButtons shows it.</summary>
    public void Info(int item)
    {
        _infoItem = item;
        _info = true;
    }

    /// <summary>$9D3A's WaitClick holds the screen until the next press, which the loop discards.</summary>
    public bool MessageUp { get; set; }

    /// <summary>StoreButtons ($9A88) until nothing is pending.</summary>
    public void Process()
    {
        while (_select || _grid)
        {
            if (_select) { _select = false; _grid = _clearText = true; Lines(); }          // $9C2C: bits 4, 5 set
            else { _grid = false; Grid(); }                                                // $9CA0
        }
        if (_info) { _info = false; InfoMessage(); }                                       // $9A9C bclr #3 -> $9D3A
    }

    // $9D3A: bit 4 asks for the grid again (the return of the wait runs $9CA2: ClearStoreText and
    // the grid), the text area is cleared at once and the item's entry printed at the cursor (0, 0)
    void InfoMessage()
    {
        _grid = _clearText = true;
        Array.Clear(Screen.Pixels, 22 * 320, 140 * 320);                                   // $9D7C ClearStoreText
        var o = 0x28 + _infoItem * 32;
        _text.X = 0; _text.Line = 0;                                          // $9D56 clr.l $134: X and the line, every message
        _text.Printf(0xB639, _infoItem, _s.Items[o + 2] << 8 | _s.Items[o + 3], _infoItem); // d0 = d2 = $242, d1 = +$2
        MessageUp = true;
    }

    /// <summary>$9C4C: the lines at once, the grid at the next StoreButtons.</summary>
    public void Redraw()
    {
        _grid = true;
        Lines();
    }

    void Lines()
    {
        for (var i = 0; i < 10; i++)
        {
            Icon(i, i * 32, 172);                                                          // $9C68 DrawItemCell
            Number(i * 4, 163, (short)(_s.Items[0x28 + i * 32] << 8 | _s.Items[0x28 + i * 32 + 1]), 3);   // $9C9C PanelNumber2
        }
    }

    static readonly int[] Cells = [4, 0x3E, 0x10, 0x3E, 0x1C, 0x3E, 4, 0x61, 0x10, 0x61, 0x1C, 0x61, 4, 0x84, 0x10, 0x84, 0x1C, 0x84];

    void Grid()
    {
        if (_clearText)                                                                    // $9CA0 bclr #4: ClearStoreText $9D7C
        {
            _clearText = false;
            Array.Clear(Screen.Pixels, 22 * 320, 140 * 320);
        }
        for (var k = 0; k < 9; k++)
        {
            int col = Cells[k * 2], row = Cells[k * 2 + 1];
            Icon(k, col * 8, row);                                                         // $9CDC DrawItemCell
            var o = 0x28 + k * 32;
            Number(col + 5, row + 4, (short)(_s.Items[o + 4] << 8 | _s.Items[o + 5]), 2);  // $9CEC PanelNumber3
        }
    }

    // DrawItemCell ($A990): the item's icon, or icon 9 when bit item of the usable mask ($23C) is clear;
    // DrawIcon ($A99C): 32 x 16 pixels over the plane longs
    void Icon(int item, int x, int y)
    {
        var icon = (_s.Items23C >> item & 1) != 0 ? item : 9;
        for (var row = 0; row < 16; row++)
            for (var px = 0; px < 32; px++) Screen.Pixels[(y + row) * 320 + x + px] = _s.Panel.IconPixel(icon, px, row);
    }

    // PanelNumber2 / 3 ($C542 / $C546 -> $C60E): the digits after the first `skip` of five, one character
    // of the panel font a byte column, in plane 0 only
    void Number(int col, int row, int v, int skip)
    {
        var (digits, _) = Panel.Digits(v);
        for (var n = skip; n < 5; n++, col++)
            for (var line = 0; line < 8; line++)
            {
                var bits = _s.Panel.FontLine(digits[n], line);
                for (var b = 0; b < 8; b++)
                    Screen.SetPlaneBit(col * 8 + b, row + line, 0, (bits >> (7 - b) & 1) != 0);
            }
    }
}
