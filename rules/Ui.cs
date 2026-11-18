using System.Text.Json;

namespace Hunter.Rules;

/// <summary>A 320x200 screen of palette indices (the game's 4 bitplanes, combined per pixel).</summary>
public sealed class IndexedScreen(int width = 320, int height = 200)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = new byte[width * height];
    /// <summary>The bank the screen fades in through when it is not the shown screen's usual one (null).</summary>
    public ushort[]? Bank { get; init; }

    public void Clear() => Array.Clear(Pixels);

    /// <summary>A rectangle cleared to 0 (easy_saves hides a menu button this way: the menu is the
    /// original's data, so the draw keeps every position and only the button's area goes blank).</summary>
    public void ClearRect(int x, int y, int w, int h)
    {
        int x0 = Math.Max(x, 0), x1 = Math.Min(x + w, Width);
        for (var r = Math.Max(y, 0); r < Math.Min(y + h, Height); r++)
            Array.Clear(Pixels, r * Width + x0, x1 - x0);
    }

    public void SetPlaneBit(int x, int y, int plane, bool on)
    {
        if ((uint)x >= Width || (uint)y >= Height) return;
        ref var p = ref Pixels[y * Width + x];
        p = on ? (byte)(p | (1 << plane)) : (byte)(p & ~(1 << plane));
    }
}

/// <summary>A blitter frame from the animation file, decoded opaque (blit mode 1).</summary>
public sealed record Frame(int Index, int Width, int Height, byte[] Pixels);

/// <summary>The game's 2D drawing, ported from the original routines (addresses in comments).</summary>
public sealed class Ui
{
    public required byte[][] Glyphs { get; init; }        // main font, $8372: 280 x 8 bytes
    public required int[] Widths { get; init; }           // $836A
    public required IReadOnlyDictionary<int, Frame> Frames { get; init; }

    public static Ui Load(string dir)
    {
        using var f = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "tables", "font.json")));
        var glyphs = f.RootElement.GetProperty("main_hex").EnumerateArray().Select(g => Convert.FromHexString(g.GetString()!)).ToArray();
        var widths = f.RootElement.GetProperty("widths").EnumerateArray().Select(w => w.GetInt32()).ToArray();
        using var fr = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "tables", "frames.json")));
        var frames = fr.RootElement.GetProperty("frames").EnumerateArray().Select(e => new Frame(
            e.GetProperty("index").GetInt32(), e.GetProperty("width").GetInt32(), e.GetProperty("height").GetInt32(),
            Convert.FromHexString(e.GetProperty("pixels_hex").GetString()!))).ToDictionary(x => x.Index);
        return new Ui { Glyphs = glyphs, Widths = widths, Frames = frames };
    }

    /// <summary>Blit mode 1 ($C884, BLTCON0 $07CA, no mask channel): opaque copy of the frame.</summary>
    public static void Blit(IndexedScreen s, Frame f, int x, int y)
    {
        for (var j = 0; j < f.Height; j++)
            for (var i = 0; i < f.Width; i++)
            {
                int px = x + i, py = y + j;
                if ((uint)px < s.Width && (uint)py < s.Height) s.Pixels[py * s.Width + px] = f.Pixels[j * f.Width + i];
            }
    }

    /// <summary>Debug only (the live "text"): every glyph drawn, joined into lines - a glyph at the same y just right of the
    /// last one carries the line on, anything else starts a new one; the last 200 lines kept. Nothing of the game reads it.</summary>
    public static class Drawn
    {
        public static readonly List<string> Lines = new();
        static int _x = -1, _y = -1;
        static readonly System.Text.StringBuilder _line = new();

        public static void Note(char c, int x, int y)
        {
            if (y != _y || x < _x || x > _x + 16) Flush();
            _line.Append(c is >= ' ' and < (char)127 ? c : '|');
            (_x, _y) = (x, y);
        }

        public static void Flush()
        {
            if (_line.Length > 0) { lock (Lines) { Lines.Add(_line.ToString()); if (Lines.Count > 200) Lines.RemoveAt(0); } }
            _line.Clear();
            _x = _y = -1;
        }
    }

    /// <summary>$C3CA: width of text up to the terminator = sum of (width + 1).</summary>
    public int Measure(string text, char terminator = '*')
    {
        var w = 0;
        foreach (var c in text) { if (c == terminator) break; w += Widths[c] + 1; }
        return w;
    }

    /// <summary>One glyph, $C26A-$C2C0: clears width+1 pixels of the cell in the text plane and sets the glyph bits;
    /// inverse ($97 bit 7, $C292): the glyph eor-ed with the cell ($C298 not.l d4).</summary>
    public int DrawChar(IndexedScreen s, char c, int x, int y, int plane, bool inverse = false)
    {
        Drawn.Note(c, x, y);
        var w = Widths[c];
        var g = Glyphs[c];
        for (var line = 0; line < 8; line++)
            for (var i = 0; i <= w; i++)
                s.SetPlaneBit(x + i, y + line, plane, (i < 8 && (g[line] & (0x80 >> i)) != 0) != inverse);
        return w + 1;
    }

    /// <summary>What one character leaves in d0 ($C26E-$C2DC): d3 = $FF >> (width + 1) rotated right
    /// by 8 + (x &amp; $F) ($C286-$C28A), d4 = ~d3 when inverse else 0 ($C290-$C29A); each of the 8 rows
    /// clr.l d0, move.b (glyph row),d0b, ror.l #8, lsr.l $d2, eor.l d4, so the last (7th) row's byte
    /// is what is left.</summary>
    public int GlyphD0(char c, int x, bool inverse)
    {
        var r = x & 0xF;
        var mask = uint.RotateRight(0xFFu >> (Widths[c] + 1), 8 + r);
        var d4 = inverse ? ~mask : 0u;
        return (int)((uint)Glyphs[c][7] << 24 >> r ^ d4);
    }

    public sealed record MenuItem(int Style, int Dx, string Text);

    /// <summary>One menu button, $AD08: frame = style + $17, centred on 160 + dx*2, text centred on it.
    /// Returns the button height (the next button's y offset).</summary>
    public int DrawMenuItem(IndexedScreen s, MenuItem item, int y)
    {
        var f = Frames[item.Style + 0x17];
        var centre = (s.Width >> 1) + item.Dx * 2;          // $AD26-$AD36
        var left = centre - (f.Width >> 1);                  // $AD24, $AD3A
        Blit(s, f, left, y);
        var tx = centre - (Measure(item.Text + "*") >> 1);   // $AD6E-$AD78
        var ty = (f.Height >> 1) + y - 4;                    // $AD7C-$AD86
        var x = tx;
        foreach (var c in item.Text) x += DrawChar(s, c, x, ty, 0);
        return f.Height;
    }
}
