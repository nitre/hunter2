namespace Hunter.Rules;

/// <summary>STUNTIN's screens in Hunter's panel look, in full colour (no 16-colour palette): a 320 x 200 picture of 0xRRGGBB
/// words the shell shows as it is (StartSequence). Its parts are the play panel's own art (panfile, in its own colours): the
/// mottled stone of the STORES banner (the seam-free patch ManualBook frames its pages with) and the panel's bevel tones;
/// text in the game's font (Ui.Glyphs) in any colour with a drop shadow; titles twice the size, red to orange top to
/// bottom like the STORES lettering.</summary>
public sealed class ScrScreenArt
{
    public const int W = 320, H = 200;
    public readonly uint[] Pixels = new uint[W * H];
    readonly Ui _ui;
    readonly byte[] _pan;
    readonly uint[] _panRgb = new uint[16];

    /// <summary>The panel's bevel tones (panfile CMAP 12, 14, 15: its bright and dark edges, its darkest shadow) and the
    /// screens' own colours.</summary>
    public readonly uint EdgeBright, EdgeDark, Shadow;
    public const uint Ground = 0x081210, BoxFill = 0x0C1A1A, Text = 0xC8D8D4, Dim = 0x7C9490, Mine = 0xF2DC5A, Heading = 0xE89A3C,
        TitleTop = 0xFFB050, TitleBottom = 0xC01808, TitleEdge = 0x200400, Red = 0xE03018;

    public ScrScreenArt(Ui ui, byte[] panPixels, ushort[] panPalette)
    {
        _ui = ui;
        _pan = panPixels;
        for (var i = 0; i < 16; i++) _panRgb[i] = Rgb(panPalette[i]);
        (EdgeBright, EdgeDark, Shadow) = (_panRgb[12], _panRgb[14], _panRgb[15]);
        Array.Fill(Pixels, Ground);
    }

    static uint Rgb(ushort w) => (uint)(((w >> 8) & 15) * 17 << 16 | ((w >> 4) & 15) * 17 << 8 | (w & 15) * 17);

    public void Set(int x, int y, uint c) { if ((uint)x < W && (uint)y < H) Pixels[y * W + x] = c; }
    public void Fill(int x0, int y0, int x1, int y1, uint c) { for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) Set(x, y, c); }

    /// <summary>The STORES banner's stone (panfile's 48 x 16 seam-free patch, ManualBook.CamX/CamY) tiled over a box,
    /// anchored to the screen so neighbouring boxes join.</summary>
    public void Stone(int x0, int y0, int x1, int y1)
    {
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
                Set(x, y, _panRgb[_pan[(ManualBook.CamY + y % ManualBook.CamH) * W + ManualBook.CamX + x % ManualBook.CamW] & 15]);
    }

    /// <summary>A bevel round a box: the panel's bright edge on top and left, its dark edge under and right (raised), or
    /// the other way (sunk).</summary>
    public void Bevel(int x0, int y0, int x1, int y1, bool sunk = false)
    {
        uint a = sunk ? EdgeDark : EdgeBright, b = sunk ? EdgeBright : EdgeDark;
        for (var x = x0; x <= x1; x++) { Set(x, y0, a); Set(x, y1, b); }
        for (var y = y0; y <= y1; y++) { Set(x0, y, a); Set(x1, y, b); }
    }

    /// <summary>A stone band round the screen (as ManualBook's pages), its edges bevelled.</summary>
    public void Frame(int band)
    {
        Stone(0, 0, W - 1, band - 1); Stone(0, H - band, W - 1, H - 1);
        Stone(0, 0, band - 1, H - 1); Stone(W - band, 0, W - 1, H - 1);
        Bevel(0, 0, W - 1, H - 1);
        Bevel(band - 1, band - 1, W - band, H - band, sunk: true);
    }

    /// <summary>A stone bar (raised), the banner and the command bar.</summary>
    public void Bar(int x0, int y0, int x1, int y1) { Stone(x0, y0, x1, y1); Bevel(x0, y0, x1, y1); }

    /// <summary>A sunk dark box (a well, as the panel's displays).</summary>
    public void Well(int x0, int y0, int x1, int y1) { Fill(x0, y0, x1, y1, BoxFill); Bevel(x0, y0, x1, y1, sunk: true); }

    /// <summary>A convex polygon (screen x, y) filled row by row in a colour, kept to the box x0..x1, y0..y1.</summary>
    public void Polygon(IReadOnlyList<(double X, double Y)> p, uint c, int x0, int y0, int x1, int y1)
    {
        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (var q in p) { minY = Math.Min(minY, q.Y); maxY = Math.Max(maxY, q.Y); }
        int top = Math.Max(y0, (int)Math.Ceiling(minY - 0.5)), bottom = Math.Min(y1, (int)Math.Floor(maxY - 0.5));
        for (var y = top; y <= bottom; y++)
        {
            var yc = y + 0.5;
            double lo = double.MaxValue, hi = double.MinValue;
            for (var k = 0; k < p.Count; k++)
            {
                var a = p[k]; var b = p[(k + 1) % p.Count];
                if ((a.Y <= yc && b.Y > yc) || (b.Y <= yc && a.Y > yc))
                {
                    var x = a.X + (yc - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                    lo = Math.Min(lo, x); hi = Math.Max(hi, x);
                }
            }
            if (lo > hi) continue;
            int xa = Math.Max(x0, (int)Math.Ceiling(lo - 0.5)), xb = Math.Min(x1, (int)Math.Floor(hi - 0.5));
            for (var x = xa; x <= xb; x++) Pixels[y * W + x] = c;
        }
    }

    /// <summary>A soft round puff at x, y, radius r: the colour over what is there, most at the middle (alpha) and none at the
    /// rim, kept to the box.</summary>
    public void Puff(double cx, double cy, double r, uint c, double alpha, int x0, int y0, int x1, int y1)
    {
        for (var y = Math.Max(y0, (int)(cy - r)); y <= Math.Min(y1, (int)(cy + r)); y++)
            for (var x = Math.Max(x0, (int)(cx - r)); x <= Math.Min(x1, (int)(cx + r)); x++)
            {
                var d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy)) / r;
                if (d >= 1) continue;
                var k = alpha * (1 - d * d);
                var o = Pixels[y * W + x];
                uint Ch(int s) => (uint)Math.Round(((o >> s) & 255) * (1 - k) + ((c >> s) & 255) * k) << s;
                Pixels[y * W + x] = Ch(16) | Ch(8) | Ch(0);
            }
    }

    /// <summary>A colour times k (0 black .. 1 as it is, more lighter, kept to white).</summary>
    public static uint Shade(uint c, double k)
    {
        uint C(int s) => (uint)Math.Clamp(Math.Round(((c >> s) & 255) * k), 0, 255) << s;
        return C(16) | C(8) | C(0);
    }

    public int Measure(string t) => _ui.Measure(t + "*");

    /// <summary>A line of the game's font at x, y in a colour, with its shadow a pixel right and down.</summary>
    public int Write(string t, int x, int y, uint c, bool shadow = true)
    {
        if (shadow) Glyphs(t, x + 1, y + 1, Shadow, 1, null);
        return Glyphs(t, x, y, c, 1, null);
    }
    public void Right(string t, int x, int y, uint c) => Write(t, x - Measure(t), y, c);
    public void Centre(string t, int x, int y, uint c) => Write(t, x - Measure(t) / 2, y, c);

    /// <summary>A title twice the font's size, centred at x: red to orange top to bottom, edged dark all round.</summary>
    public void Title(string t, int x, int y)
    {
        var left = x - Measure(t);
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 2; dx++)
                if (dx != 0 || dy != 0) Glyphs(t, left + dx, y + dy, TitleEdge, 2, null);
        Glyphs(t, left, y, 0, 2, row => Mix(TitleTop, TitleBottom, row / 15.0));
    }

    static uint Mix(uint a, uint b, double f)
    {
        uint C(int s) => (uint)Math.Round(((a >> s) & 255) * (1 - f) + ((b >> s) & 255) * f) << s;
        return C(16) | C(8) | C(0);
    }

    int Glyphs(string t, int x, int y, uint c, int scale, Func<int, uint>? byRow)
    {
        foreach (var ch in t)
        {
            var k = ch < _ui.Glyphs.Length ? ch : '?';
            var g = _ui.Glyphs[k];
            for (var line = 0; line < 8; line++)
                for (var i = 0; i < 8; i++)
                    if ((g[line] & (0x80 >> i)) != 0)
                        for (var sy = 0; sy < scale; sy++)
                            for (var sx = 0; sx < scale; sx++)
                                Set(x + i * scale + sx, y + line * scale + sy, byRow?.Invoke(line * scale + sy) ?? c);
            x += (_ui.Widths[k] + 1) * scale;
        }
        return x;
    }
}
