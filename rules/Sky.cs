namespace Hunter.Rules;

/// <summary>
/// Sky and sea: the view is cleared to colour 15 and the copper reloads COLOR15 at six lines
/// placed each frame by $62B0 from the camera height, so open sea and sky are a gradient.
/// Colours 0-14 come from palette bank 2 ($6318 + 2 * 64), the band colours from the 3D view's
/// copper template (screen descriptor 3 at $5F50 + 3 * $2E -> $5DD8).
/// </summary>
public sealed class Sky
{
    /// <summary>COLOR15 from the top of the view down: the bank's colour, then the 6 band colours.</summary>
    public readonly ushort[] Colours = new ushort[7];

    public Sky(byte[] main, IReadOnlyList<ushort[]> banks)
    {
        Colours[0] = banks[2][15];
        int Word(int a) => main[a - 0x800] << 8 | main[a - 0x800 + 1];
        var desc = 0x5F50 + 3 * 0x2E;
        int t = Word(desc) << 16 | Word(desc + 2), n = Word(desc + 4) + 1, k = 1;
        for (var i = 0; i < n && k < 7; i++, t += 4)
        {
            int w1 = Word(t), w2 = Word(t + 2);
            if ((w1 & 1) == 0 && w1 == 0x19E && w2 != 0) Colours[k++] = (ushort)w2;
        }
    }

    /// <summary>$62B0: horizon row ($1534) = (height $E * 256) / $2B00 + $2E; the bands start at
    /// horizon - 62, -30, -14, -6, -2 and 0, each clamped to rows 0..$8B. Returns those 6 rows.</summary>
    public static int[] Bands(short height0E)
    {
        var h = (short)(M68.Divs(height0E << 8, 0x2B00) + 0x2E);
        var rows = new int[6];
        int d0 = h;
        int[] steps = { 2, 4, 8, 16, 32, 0 };
        for (var k = 0; k < 6; k++)
        {
            var r = (short)d0;
            if (r < 0) r = 0;
            if (r > 0x8B) r = 0x8B;
            rows[5 - k] = r;                                // written from the copper's last band backwards
            d0 = (short)(d0 - steps[k]);
        }
        return rows;
    }

    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (the card's SKY: SMOOTH, owner 2026-10-30: "Six bands is obviously
    /// better"): the original's six bands, each line moved halfway toward an even spread from the top of the view to
    /// the horizon, so the sky lightens all the way down instead of only in the last rows. The horizon's line stays.</summary>
    public static int[] SmoothBands(short height0E) => Smooth(Bands(height0E));

    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (the card's SKY: SMOOTH sky itself, owner 2026-11-01 picked 10 bands from a
    /// mock-up): view row 0 down to the horizon row h (Bands[5]) in 10 bands, each 0.8 the height of the one above. Returns the
    /// 9 rows where bands 1..9 begin (band 0 begins at row 0), rounded to whole game rows.</summary>
    public static int[] TenEdges(int h)
    {
        int[] share = [14684, 26431, 35829, 43347, 49361, 54173, 58022, 61102, 63565];   // the bands' running share of 65536 (0.8^k weights): whole numbers, same on the card
        return Array.ConvertAll(share, s => ((h + 1) * s + 32768) >> 16);
    }

    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (see TenEdges): the 10 band colours, 0..255 a channel, evenly in OKLab from
    /// top to a paler horizon (band 5 (the last sky band) colour mixed 0.25 toward its brightest channel), each channel rounded to a multiple of 17.</summary>
    public static int[][] TenColours(double[] top, double[] last) => Array.ConvertAll(TenLevels(top, last), c => Array.ConvertAll(c, v => (int)Math.Clamp(Math.Floor(v + 0.5), 0, 15) * 17));

    /// <summary>TenColours before the rounding: each channel as a level 0..15 with its fraction (the check allows a half-level tie).</summary>
    public static double[][] TenLevels(double[] top, double[] last)
    {
        var peak = Math.Max(last[0], Math.Max(last[1], last[2]));
        var a = OkLab(top);
        var b = OkLab([last[0] + (peak - last[0]) * 0.25, last[1] + (peak - last[1]) * 0.25, last[2] + (peak - last[2]) * 0.25]);
        var cols = new double[10][];
        for (var k = 0; k < 10; k++)
        {
            var t = k / 9.0;
            cols[k] = FromOkLab([a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t]);
        }
        return cols;
    }

    static double Lin(double c) { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
    static double Unlin(double c) => c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;

    static double[] OkLab(double[] c)
    {
        double r = Lin(c[0]), g = Lin(c[1]), b = Lin(c[2]);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return [0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s, 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s];
    }

    static double[] FromOkLab(double[] v)
    {
        var l = Math.Pow(v[0] + 0.3963377774 * v[1] + 0.2158037573 * v[2], 3);
        var m = Math.Pow(v[0] - 0.1055613458 * v[1] - 0.0638541728 * v[2], 3);
        var s = Math.Pow(v[0] - 0.0894841775 * v[1] - 1.2914855480 * v[2], 3);
        double[] lin = [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                        -0.0041960771 * l - 0.7034186147 * m + 1.7076147010 * s];
        return Array.ConvertAll(lin, x => Unlin(Math.Max(x, 0)) * 15);
    }

    /// <summary>SmoothBands from the original's six band rows.</summary>
    public static int[] Smooth(int[] raw)
    {
        var rows = new int[6];
        for (var k = 0; k < 6; k++) rows[k] = (raw[k] + raw[5] * k / 5) / 2;          // raw is already 0..$8B, so all whole and >= 0
        return rows;
    }

    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (Play.SkySteps): with Steps on, each band's interior
    /// (the rows where a band's colour shows, from the row after the copper's line down to the next
    /// band's row) steps through the in-between 12-bit colours of its two band colours - only such
    /// colours as the Amiga can show, so there are as many steps as the biggest channel difference
    /// allows (006 -> 118: one, 44C -> 55F: two), each channel rounded to its nearest 4-bit level;
    /// the band is cut into equal parts, its own colour the last part, never a part thinner than a row. The colour at each original band row and the sea below
    /// the horizon stay the original's, so the horizon line and the sea do not move.</summary>
    public bool Steps;

    /// <summary>The colour-15 value for each of the 140 view rows. Each band's WAIT is for
    /// line $2C + row at h $FC, beyond the last beam position of a line, so the new colour shows
    /// from the following row; row 0 always keeps the bank's colour 15. With Steps on, a band's
    /// interior rows step through the in-between colours (Sky.Steps).</summary>
    public ushort[] Rows(short height0E)
    {
        var bands = Bands(height0E);
        var col = new ushort[View.Height];
        for (var y = 0; y < View.Height; y++)
        {
            var c = Colours[0];
            for (var k = 0; k < 6; k++) if (y > bands[k]) c = Colours[k + 1];
            col[y] = c;
        }
        if (!Steps) return col;
        for (var k = 0; k < 5; k++)
        {
            var lo = bands[k] + 1;                                          // the band's colour shows from the row after the copper's line
            var rows = bands[k + 1] - bands[k];                             // its rows, down to and with the next band's row
            if (rows <= 1) continue;                                        // one row or clamped together: nothing between
            int a = Colours[k], b = Colours[k + 1];
            int Gap(int s) => Math.Abs((a >> s & 15) - (b >> s & 15));      // $0RGB: ch 0 R, 1 G, 2 B
            var n = Math.Max(Gap(8), Math.Max(Gap(4), Gap(0)));             // the in-between levels are t = 1..n-1 of n, then the band's own
            var parts = Math.Min(n, rows);                                  // equal parts, the band's own colour the last; never thinner than a row
            for (var j = 0; j < parts - 1; j++)
            {
                var t = (j + 1) * n / parts;
                var c = (ushort)(Ramp(a >> 8 & 15, b >> 8 & 15, t, n) << 8 |
                                 Ramp(a >> 4 & 15, b >> 4 & 15, t, n) << 4 |
                                 Ramp(a & 15, b & 15, t, n));
                for (int y = lo + j * rows / parts, end = lo + (j + 1) * rows / parts; y < end; y++) col[y] = c;
            }
        }
        return col;
    }

    /// <summary>The channel from level x to y, t/n of the way, rounded to its nearest 4-bit level.</summary>
    static int Ramp(int x, int y, int t, int n) => (x * (n - t) + y * t + n / 2) / n;
}
