namespace Hunter.Rules;

/// <summary>(setting ground_colours, GROUND COLOURS: BANDS, the 3D view):
/// the land shaded by height and slope after Vista Pro's method (vistapro-port docs/method_colour_light.md 1-2:
/// CalculateColors $13C4E, CalculateCliffs $136FA) - adapted to Hunter: each square keeps the original map's own kind of
/// land, green (colours 12-14) or brown (9-11), and Vista's height steps run inside it, along a ramp of the original's own
/// greens or browns, each band a set share of that kind of land (Heights). Measured: 40-50% of Hunter's land is flat at
/// sea level, so height alone painted half the land one green and lost the original's patchwork. Where green land meets
/// brown the two blend across a square (Brown64). Sand at the shore, rock on steep sides. The picture only - the map's
/// bytes stay as they are. Each small ground piece (GROUND SHAPE's cuts) takes its own colour from its middle, four steps
/// a band as Vista's four codes a band. The checker is only a touch darker every second square. Vista's bend
/// (ComputeSlope: + on ridges, - in hollows) lowers hollows a little and lifts ridges. No random touches.
/// The shader's twin is godot/real3d/ground_bands.gdshaderinc (the same whole numbers).</summary>
public static class GroundBands
{
    /// <summary>The ramps up each kind of land: (percent of that land below, $0RGB) - the original's own ground colours
    /// (docs/original-tricks.md: low land dark green 0130 / 0251 / light 0372, high land brown 0521 / 0732 / 0A53).
    /// Between two keys four even steps, each rounded to the Amiga's 4 bits a channel. The green ramp starts at the
    /// middle green (the darker green is left for the shadows): 0251, halfway 0362, 0372.</summary>
    public static readonly int[] GreenKeys = [0, 0x251, 50, 0x362, 100, 0x372], BrownKeys = [0, 0x521, 50, 0x732, 100, 0xA53];

    /// <summary>Sand at the shore, dark (wet) to light; the rock of steep sides, low (dark) to high (light).</summary>
    public const int SandDark = 0x974, SandLight = 0xCA6, SteepLow = 0x554, SteepHigh = 0x998;

    /// <summary>A slope this steep is rock: world units of rise per 512 (16 height bytes a square - measured: about 1 land
    /// square in 16 in Hunter and Action).</summary>
    public const int SteepAt = 256;

    /// <summary>The beach: sand grades into the land over this many world units from the sea (ShoreDistance) and this
    /// height.</summary>
    public const int ShoreReach = 256, ShoreHigh = 24;

    /// <summary>The checker: every second square's colour times this, in 64ths.</summary>
    public const int Checker = 60;

    /// <summary>Brown land: the original's colours 9-11 (green land 12-14).</summary>
    public static bool Brown(int colour) => colour is >= 9 and <= 11;

    /// <summary>Whether a square of this colour takes the bands (the land families 9-14).</summary>
    public static bool Banded(int colour) => colour is >= 9 and <= 14;

    /// <summary>The height of each of a ramp's keys (its share of that kind of land's points below it, above the beach's
    /// height), world units, rising by at least 1; the last at 99 points in 100, so one lone peak doesn't set it. map: the
    /// terrain's two bytes a point (height, type). fine: ENHANCEMENT (FINER GROUND) - the store's heights are the map's
    /// times its factor, so the keys are too.</summary>
    public static int[] Heights(byte[] map, bool brown, FineGround? fine = null)
    {
        var keys = brown ? BrownKeys : GreenKeys;
        var land = new List<int>();
        for (var i = 0; i < 128 * 128; i++)
        {
            var t = map[i * 2 + 1] & 15;
            if (Banded(t) && Brown(t) == brown && map[i * 2] * 16 >= ShoreHigh) land.Add(map[i * 2] * 16);
        }
        land.Sort();
        var heights = new int[keys.Length / 2];
        for (var k = 0; k < heights.Length; k++)
        {
            var at = land.Count == 0 ? 0 : land[Math.Min(land.Count * keys[k * 2] / 100, land.Count * 99 / 100)];
            if (fine is not null) at = at * fine.Num / fine.Den;
            heights[k] = k == 0 ? at : Math.Max(at, heights[k - 1] + 1);
        }
        return heights;
    }

    /// <summary>How brown the land is at world (x, z), 0 (green) to 64 (brown): the squares' kinds blended between square
    /// middles, so green land fades into brown across a square. A square that is neither (sea, grey) counts as the square
    /// the point is in. map: the terrain's two bytes a point.</summary>
    public static int Brown64(byte[] map, int x, int z)
    {
        var own = Brown(map[(Math.Clamp(z / 512, 0, 127) * 128 + Math.Clamp(x / 512, 0, 127)) * 2 + 1] & 15) ? 64 : 0;
        int B(int c, int r)
        {
            var t = map[(Math.Clamp(r, 0, 127) * 128 + Math.Clamp(c, 0, 127)) * 2 + 1] & 15;
            return !Banded(t) ? own : Brown(t) ? 64 : 0;
        }
        int gx = x + 256, gz = z + 256, c0 = gx / 512 - 1, r0 = gz / 512 - 1, fx = gx % 512, fz = gz % 512;   // between square middles
        return ((B(c0, r0) * (512 - fx) + B(c0 + 1, r0) * fx) * (512 - fz) + (B(c0, r0 + 1) * (512 - fx) + B(c0 + 1, r0 + 1) * fx) * fz) / (512 * 512);
    }

    /// <summary>A ramp's colour at height e: four steps between its keys.</summary>
    static int Ramp(int[] keys, int[] heights, int e)
    {
        var k = 0;
        while (k < heights.Length - 2 && heights[k + 1] <= e) k++;
        return Mix(keys[k * 2 + 1], keys[k * 2 + 3], Math.Clamp((e - heights[k]) * 4 / (heights[k + 1] - heights[k]), 0, 3), 4);
    }

    /// <summary>The $0RGB colour of a ground piece whose middle is at height h: bend 4h less the heights 1024 off each way,
    /// slope there (Slope), shore its distance from the sea, brown64 Brown64 there, all world units; green / brown the two
    /// Heights. Before the checker (Checker) and the light.</summary>
    public static int Colour(int h, int bend, int slope, int shore, int brown64, int[] green, int[] brown)
    {
        if (slope >= SteepAt) return Mix(SteepLow, SteepHigh, Math.Clamp(h * 4 / Math.Max(Math.Max(green[^1], brown[^1]), 1), 0, 3), 4);
        var e = h + bend / 8;                                                   // hollows a little lower, ridges a little higher
        var land = Mix(Ramp(GreenKeys, green, e), Ramp(BrownKeys, brown, e), (brown64 * 4 + 32) / 64, 4);
        // the beach, graded as Vista's shore band (ApplySeaLevel: a step down from the water's edge): step 0 wet sand,
        // 1 dry sand, 2-3 sand into the land, 4 the land - by distance from the sea and height
        var t = Math.Clamp(Math.Max(shore * 4 / ShoreReach, h * 4 / ShoreHigh), 0, 4);
        return t == 0 ? SandDark : Mix(SandLight, land, t - 1, 3);
    }

    /// <summary>The slope at a map point: the steeper of its two centred height steps, plus a sixteenth of the other,
    /// world units of rise per 512. map: the terrain's two bytes a point.</summary>
    public static int PointSlope(byte[] map, int c, int r)
    {
        int H(int cc, int rr) => map[(Math.Clamp(rr, 0, 127) * 128 + Math.Clamp(cc, 0, 127)) * 2] * 16;
        int a = Math.Abs(H(c + 1, r) - H(c - 1, r)) / 2, b = Math.Abs(H(c, r + 1) - H(c, r - 1)) / 2;
        return Math.Max(a, b) + (Math.Min(a, b) >> 4);
    }

    /// <summary>The slope at world (x, z): its square's four corner PointSlopes blended by its place in the square, so
    /// steepness - and rock - changes smoothly through a square, not square by square (inside a square the game's height
    /// is a blend of its corners, whose own slope hardly changes).</summary>
    public static int Slope(byte[] map, int x, int z)
    {
        int c = x >> 9, r = z >> 9, fx = x & 511, fz = z & 511;
        int s00 = PointSlope(map, c, r), s10 = PointSlope(map, c + 1, r), s01 = PointSlope(map, c, r + 1), s11 = PointSlope(map, c + 1, r + 1);
        return ((s00 * (512 - fx) + s10 * fx) * (512 - fz) + (s01 * (512 - fx) + s11 * fx) * fz) >> 18;
    }

    /// <summary>What the bands' colours are multiplied by so the land is as bright as the original's on average (the
    /// remake was much darker - the ramps start at their darkest key, and 40-50% of the land is flat at sea
    /// level). Over the map's banded land above the beach: the brightness of each square's own colour in the original's
    /// palette (view: bank 2's $0RGB words), over the bands' colour there with the checker's average; 1 at the least.</summary>
    public static double Gain(byte[] map, IReadOnlyList<int> view)
    {
        int[] green = Heights(map, false), brown = Heights(map, true);
        static int Luma(int w) => 299 * (w >> 8 & 15) + 587 * (w >> 4 & 15) + 114 * (w & 15);
        int H(int c, int r) => map[(Math.Clamp(r, 0, 127) * 128 + Math.Clamp(c, 0, 127)) * 2] * 16;
        double own = 0, bands = 0;
        for (var r = 0; r < 128; r++)
            for (var c = 0; c < 128; c++)
            {
                var t = map[(r * 128 + c) * 2 + 1] & 15;
                var h = H(c, r);
                if (!Banded(t) || h < ShoreHigh) continue;
                var bend = 4 * h - H(c + 2, r) - H(c - 2, r) - H(c, r + 2) - H(c, r - 2);   // 1024 each way, as the card
                own += Luma(view[t]);
                bands += Luma(Colour(h, bend, PointSlope(map, c, r), ShoreReach * 4, Brown64(map, c * 512, r * 512), green, brown)) * (64 + Checker) / 128.0;
            }
        return bands <= 0 ? 1 : Math.Max(1, own / bands);
    }

    /// <summary>Step `step` of `steps` from colour a to colour b, each 4-bit channel rounded.</summary>
    public static int Mix(int a, int b, int step, int steps)
    {
        var w = 0;
        for (var sh = 8; sh >= 0; sh -= 4)
        {
            int ca = a >> sh & 15, cb = b >> sh & 15;
            w |= (ca * steps + (cb - ca) * step + steps / 2) / steps << sh;
        }
        return w;
    }
}
