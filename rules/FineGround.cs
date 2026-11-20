namespace Hunter.Rules;

/// <summary>(FINER GROUND): a finer, taller height store behind the one
/// sampler Ground.Height ($36AA). N points a map cell side (any power of two, 1..512), so (128 N + 1)^2 points, each a
/// height in world units as an int (the map byte &lt;&lt; 4 is the original's; here any 0..32767). Point (i, j) sits at
/// world (i * 512 / N, j * 512 / N); the original's map point (col, row) is (col N, row N).
/// Column 128 N and row 128 N are the points the original's memory walk reads past the map's east and south edges:
/// past the last column the next row's first points ($36AA's +2 / +$102 at col 127), past the last row the zero rows
/// 128-129 - so the sampler needs no edge case. Filled from the map alone (FromMap) it holds the original's heights
/// (32-bit lerp, Ground.Lerp maths32) exactly at every fine point; between them the second blend of already-truncated
/// points rounds differently, by at most 2 world units on the three worlds at 1-8x (FineGroundTests, measured).
/// Off (Ground.Fine null), nothing here runs: the map bytes and the original's word maths, bit for bit.</summary>
public sealed class FineGround
{
    /// <summary>Points a map cell side, a power of two.</summary>
    public int N { get; }
    /// <summary>Points a side of the store: 128 N + 1.</summary>
    public int Side { get; }
    /// <summary>The heights, [j * Side + i], world units.</summary>
    public int[] H { get; }
    /// <summary>The height factor the store was grown with (Grow; 1 / 1 from FromMap).</summary>
    public int Num { get; private set; } = 1;
    public int Den { get; private set; } = 1;
    /// <summary>ENHANCEMENT (GROUND SEED / RELIEF): the seed the detail was grown from (the world's own unless the
    /// setting drew one), Vista's dimension and the hold the detail was held to (Reliefs). FromMap: none.</summary>
    public ushort Seed { get; private set; }
    public int Dim { get; private set; } = Dimension;
    public int Hold { get; private set; } = Gentle;
    readonly int _shift, _mask, _fshift;                 // cell size 512 / N = 1 &lt;&lt; _shift; fraction to 4096ths

    /// <summary>The store's top: heights are words everywhere the game keeps them (+$4C, +$74, Height0E).</summary>
    public const int Top = 0x7FFF;

    public FineGround(int n)
    {
        if (n < 1 || n > 512 || (n & (n - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(n), "a power of two, 1..512");
        N = n;
        Side = 128 * n + 1;
        H = new int[Side * Side];
        var log = System.Numerics.BitOperations.Log2((uint)n);
        _shift = 9 - log;
        _mask = (1 << _shift) - 1;
        _fshift = 3 + log;                                // $36AA: (x &amp; $1FF) &lt;&lt; 3 - a fraction in 4096ths at any N
    }

    public int this[int i, int j]
    {
        get => H[j * Side + i];
        set => H[j * Side + i] = value;
    }

    /// <summary>The original's map point (col, row) as the store holds it.</summary>
    public int Point(int col, int row) => H[row * N * Side + col * N];

    /// <summary>$36AA on the fine store: the same three lerps ($3710-$371A) over the fine cell, in 32-bit integers
    /// (Ground.Lerp's maths32 form - the word form would overflow divs on taller ground, docs/data/byte-limits.md).</summary>
    public short Height(ushort x, ushort z)
    {
        int i = x >> _shift, j = z >> _shift;
        int fx = (x & _mask) << _fshift, fz = (z & _mask) << _fshift;
        var o = j * Side + i;
        var a = Lerp(H[o], H[o + Side], fz);
        var b = Lerp(H[o + 1], H[o + Side + 1], fz);
        return (short)Lerp(a, b, fx);
    }

    /// <summary>$3710-$371A in 32 bits: (d1 - d0) * f / $100 (toward zero, as divs), asr 4, + d0.</summary>
    public static int Lerp(int d0, int d1, int f) => (((d1 - d0) * f / 0x100) >> 4) + d0;

    /// <summary>A store of N points a side holding the original's ground: every point the original's
    /// bilinear ($36AA, 32-bit lerp) of the map bytes &lt;&lt; 4 as its memory reads them (the walk past col 127 into
    /// the next row, rows 128-129 zero). On the original's points it is the byte &lt;&lt; 4 itself.</summary>
    public static FineGround FromMap(byte[] map, int n)
    {
        var f = new FineGround(n);
        int P(int col, int row)                           // the map byte &lt;&lt; 4 as the memory walk reads it
        {
            var o = row * 0x100 + col * 2;                // col 128 = next row's col 0, as +2 / +$102 at col 127
            return o < map.Length ? map[o] << 4 : 0;      // rows 128-129 (and past): zero
        }
        var step = 4096 / n;                              // a fine step's fraction in 4096ths
        for (var j = 0; j < f.Side; j++)
            for (var i = 0; i < f.Side; i++)
            {
                int col = i / n, row = j / n, fx = i % n * step, fz = j % n * step;
                var a = Lerp(P(col, row), P(col, row + 1), fz);
                var b = Lerp(P(col + 1, row), P(col + 1, row + 1), fz);
                f.H[j * f.Side + i] = Lerp(a, b, fx);
            }
        return f;
    }

    // ------------------------------------------------------------------ growing the points between (FINER GROUND step 3)

    /// <summary>The height factor to start with, as a ratio of whole numbers.</summary>
    public const int FactorNum = 3, FactorDen = 2;
    /// <summary>Vista Pro's fractal dimension at its unit scale: "dim 32 = factor 1" (RandomNudge $73CF0-$73CF6 asr 5,
    /// vistapro-port method_build_land.md 3.6) - its default 100 is three times as rough; this keeps it gentle.</summary>
    public const int Dimension = 32;
    /// <summary>Vista's point spacing in its height units: 30 m a point, heights in metres ($19296 LoadVistaDEMHeights
    /// "/30 m per cell", ShowLandscapeInfo's size x 30, WritePNTSChunk's x = col * 30 beside z = the height word). Hunter's
    /// heights and positions share one world unit, so a step of s world units is s / 30 of Vista's points.</summary>
    public const int VistaSpacing = 30;
    /// <summary>GENTLE (nothing that slows, stops or tips a vehicle): the detail - the fine height less the smooth
    /// one - stays within +-Gentle world units, a range of 14: under 15, the least hard-landing drop (+$78 = gravity / 2
    /// + 8, ConstructEntity $3230) of anything on land that can land hard (d7 bits 14 and 22: type $02), so not even a
    /// fall from a bump's top into the next hollow lands hard ($2B9C). Points on the squares' edges keep +-EdgeGentle,
    /// even numbers, so each square's detail can sum to exactly zero (EvenOut).</summary>
    public const int Gentle = 7, EdgeGentle = 2;

    /// <summary>(RELIEF): what the FINER GROUND
    /// row's words grow - the height factor on the whole, Vista's dimension and the detail's hold. 0 = 2X, today's gentle
    /// store exactly (every number of 2026-11-01). HILLS and CLIFFS: taller (2x, 3x - under the 4x the limit
    /// tests prove, FinerGroundLimitTests), rougher (Vista's own default is 100; the nudge's spread on flat ground is about
    /// 0.7 x dim / 4 world units at 2 points, so the dimension follows the hold) and held wider. Past Gentle the detail
    /// CAN land a thing hard or tip it (the hold is more than the least hard-landing drop, 15). The tilt
    /// byte never wraps at any of them (Objects.TiltAngle holds +-63; Hold stays under 64).</summary>
    public static readonly IReadOnlyList<(string Name, int Num, int Den, int Dim, int Hold)> Reliefs =
    [
        ("2X", FactorNum, FactorDen, Dimension, Gentle),
        ("HILLS", 2, 1, 96, 21),
        ("CLIFFS", 3, 1, 256, 63),
        ("FINER", 1, 1, Dimension, Gentle),                     // the gentle detail only: factor 1 and no rise, the hills the original's height
    ];

    /// <summary>The hold on a square's edge points for a hold on its inner ones: half, even (so a square's detail can sum
    /// to exactly zero), at most what the inner points can take back (2 edge holds &lt;= (N - 1) hold at any N &gt;= 2).
    /// Gentle's is EdgeGentle.</summary>
    public static int EdgeHold(int hold) => hold / 2 & ~1;

    /// <summary>The finer, taller ground of a world. The smooth ground first: the original's bilinear of its points times
    /// the factor, plus the rise from the sea (Rise, AddRise). Then the points between grown from it by Vista Pro's FractalDetailExisting ($14060, the
    /// Fractalize command) seeded from the world's own seed (header word +$00); the detail (grown less smooth) held
    /// gentle and evened out over every square (Gentle, EvenOut); squares touching the sea's level keep none, so the sea
    /// and the coast stay the smooth ground's. The original's points are kept exactly (times the factor, plus the rise). The same world,
    /// N and factor always give the same store (a load rebuilds it from them).</summary>
    public static FineGround Grow(Terrain t, ushort seed, int n, int num = FactorNum, int den = FactorDen) =>
        Grow(t, seed, n, num, den, seed, Dimension, Gentle);

    /// <summary>ENHANCEMENT (GROUND SEED / RELIEF): the same, the detail grown from its own seed (GROUND SEED: RANDOM draws
    /// one a new game; the world's own is today's), at dimension dim and held to +-hold (Reliefs). seed stays the world's:
    /// the map's byte wrap is undone by it (Unwrapped). The smooth ground, the rise, the sea and the coast never
    /// depend on the detail seed - only the points between the original's.</summary>
    public static FineGround Grow(Terrain t, ushort seed, int n, int num, int den, ushort detailSeed, int dim, int hold)
    {
        if (hold < 1 || hold > 63) throw new ArgumentOutOfRangeException(nameof(hold), "1..63: the tilt hold (Objects.TiltAngle)");
        var map = Unwrapped(t, seed);
        var smooth = FromMap(map, n);
        smooth.Scale(num, den);
        smooth.AddRise(Rise(map, num, den));
        var f = new FineGround(n) { Num = num, Den = den, Seed = detailSeed, Dim = dim, Hold = hold };
        smooth.H.CopyTo(f.H, 0);
        f.Detail(detailSeed, dim);
        f.EvenOut(smooth, hold);
        return f;
    }

    /// <summary>The map with the byte wrap undone (32 bits): a
    /// point whose true height ($CB3C's 16-bit sum asr 4, Terrain.TrueHeights) is below 0 wrapped past 255 in the byte
    /// cut and stands as a spike (Missions' 12, byte-limits.md row 1) - it is below every sea level, so 0 here. A true
    /// height past 255 would need the sea's sums redone; the three worlds have none (FineGroundGrowTests). The type
    /// bytes stay the original's. Both lookups are built from this; with the spike fix on (StartState.For fixSpikes,
    /// docs/original-bugs.md) the map itself is.</summary>
    public static byte[] Unwrapped(Terrain t, ushort seed)
    {
        var map = (byte[])t.Map.Clone();
        var trueH = Terrain.TrueHeights(seed);
        for (var p = 0; p < trueH.Length; p++)
            if (trueH[p] < 0) map[p * 2] = 0;
        return map;
    }

    /// <summary>The smooth lookup's store: the original's points times the factor plus the rise, one point a cell - so its
    /// sampler is the original's bilinear ($36AA, 32-bit) of them. What Ground.SmoothHeight reads.</summary>
    public static FineGround Smooth(byte[] map, int num, int den)
    {
        var f = FromMap(map, 1);
        f.Scale(num, den);
        f.AddRise(Rise(map, num, den));
        (f.Num, f.Den) = (num, den);
        return f;
    }

    // ------------------------------------------------------------------ the land's rise from the sea

    /// <summary>The rise's full height: a third of the world's tallest land (1 land point in 100 above it, times the
    /// factor) - well under its real hills.</summary>
    public const int RiseShare = 3;
    /// <summary>Map cells from the sea at which the rise is half its full height.</summary>
    public const int RiseHalfCells = 6;
    /// <summary>How far the distance is looked for, map cells; past it a point counts as this far.</summary>
    public const int RiseReach = 32;

    /// <summary>measured, 40-50% of the land is flat at the sea's level. Each of the
    /// original's points rises by its distance d from the sea, R d / (d + RiseHalfCells): 0 on the coast, half way
    /// RiseHalfCells cells in, easing towards R = the tallest land / RiseShare - so a small island stays low and a big one
    /// climbs. d: from the point to the nearest point that touches a sea square (type 0) or the map's edge, straight
    /// line, in world units. Per point on the original's lattice (129 x 129, [row * 129 + col]); the hills sit on top.</summary>
    public static int[] Rise(byte[] map, int num, int den)
    {
        const int L = 129;
        if (num == den) return new int[L * L];                  // factor 1 (FINER): the original's heights, no rise - a load rebuilds the same from Num / Den

        bool Sea(int c, int r) => c < 0 || r < 0 || c > 127 || r > 127 || map[(r * 128 + c) * 2 + 1] == 0;
        var seaPoint = new bool[L * L];
        for (var r = 0; r < L; r++)
            for (var c = 0; c < L; c++)
                seaPoint[r * L + c] = Sea(c - 1, r - 1) || Sea(c, r - 1) || Sea(c - 1, r) || Sea(c, r);
        var full = RiseFull(map, num, den);
        var rise = new int[L * L];
        for (var r = 0; r < L; r++)
            for (var c = 0; c < L; c++)
            {
                var best = RiseReach * RiseReach;
                for (var rr = Math.Max(0, r - RiseReach); rr <= Math.Min(L - 1, r + RiseReach) && best > 0; rr++)
                    for (var cc = Math.Max(0, c - RiseReach); cc <= Math.Min(L - 1, c + RiseReach); cc++)
                        if (seaPoint[rr * L + cc]) best = Math.Min(best, (cc - c) * (cc - c) + (rr - r) * (rr - r));
                var d = (int)(Math.Sqrt(best) * 512);
                rise[r * L + c] = (int)((long)full * d / (d + RiseHalfCells * 512));
            }
        return rise;
    }

    /// <summary>The rise's full height R, world units: the tallest land (1 land point in 100 above it, times the factor)
    /// / RiseShare; 0 with no land.</summary>
    public static int RiseFull(byte[] map, int num, int den)
    {
        var land = new List<int>();
        for (var i = 0; i < 128 * 128; i++) if (map[i * 2 + 1] != 0) land.Add(map[i * 2] << 4);
        land.Sort();
        return land.Count == 0 ? 0 : land[land.Count * 99 / 100] * num / den / RiseShare;
    }

    /// <summary>Adds the rise (Rise, per original point) to every point, blended between the original's points as the
    /// sampler blends ($3710-$371A in 32 bits), so it is smooth and the original's points take it exactly.</summary>
    void AddRise(int[] rise)
    {
        const int L = 129;
        int R(int c, int r) => rise[Math.Min(r, L - 1) * L + Math.Min(c, L - 1)];
        var step = 4096 / N;
        for (var j = 0; j < Side; j++)
            for (var i = 0; i < Side; i++)
            {
                int col = i / N, row = j / N, fx = i % N * step, fz = j % N * step;
                var a = Lerp(R(col, row), R(col, row + 1), fz);
                var b = Lerp(R(col + 1, row), R(col + 1, row + 1), fz);
                H[j * Side + i] = Math.Min(Top, H[j * Side + i] + Lerp(a, b, fx));
            }
    }

    /// <summary>The detail made gentle and even: per point d = this less smooth. A point on a square with a corner at the
    /// sea's level (0) gets none. Vista's upward lean taken off map-wide; edge points (on a line between two of the
    /// original's points) held to +-EdgeHold(hold), even numbers toward 0; inner points held to +-hold (Gentle and
    /// EdgeGentle at 2X). Then each square's inner points moved (from the first point on, each as far as its
    /// hold allows) until inner + half the edges = 0 - the bilinear
    /// surface over the square then has exactly the smooth one's volume (the corners carry no detail). Always possible:
    /// the edges' half-sum is at most 2 (N - 1) EdgeHold, the inner points can take (N - 1)^2 hold.</summary>
    void EvenOut(FineGround smooth, int hold)
    {
        var edgeHold = EdgeHold(hold);
        var d = new int[H.Length];
        for (var k = 0; k < H.Length; k++) d[k] = H[k] - smooth.H[k];
        var flat = new bool[128 * 128];
        for (var r = 0; r < 128; r++)
            for (var c = 0; c < 128; c++)
                flat[r * 128 + c] = smooth.Point(c, r) == 0 || smooth[(c + 1) * N, r * N] == 0
                    || smooth[c * N, (r + 1) * N] == 0 || smooth[(c + 1) * N, (r + 1) * N] == 0;
        bool Flat(int c, int r) => c < 0 || r < 0 || c > 127 || r > 127 || flat[r * 128 + c];
        // Vista's nudge leans upward (b's mean is +31.5 of 64, method_build_land.md 2.4) - the evening out would then pull
        // every square's inside down into its hold. So first the lean goes, map-wide: the edge points' mean and the inner
        // points' mean (over the land squares) taken off each; then each point is held.
        var edgeAt = new List<int>();
        var innerAt = new List<int>();
        for (var j = 0; j < Side; j++)
            for (var i = 0; i < Side; i++)
            {
                bool onI = i % N == 0, onJ = j % N == 0;
                int c = i / N, r = j / N;
                if ((onI && onJ) || Flat(c, r) || (onI && Flat(c - 1, r)) || (onJ && Flat(c, r - 1)))
                    d[j * Side + i] = 0;                                    // the original's points; squares at the sea's level
                else (onI || onJ ? edgeAt : innerAt).Add(j * Side + i);
            }
        foreach (var (at, limit, even) in new[] { (edgeAt, edgeHold, true), (innerAt, hold, false) })
        {
            if (at.Count == 0) continue;
            var mean = (int)Math.Round(at.Average(k => (double)d[k]));
            foreach (var k in at)
            {
                var v = Math.Clamp(d[k] - mean, -limit, limit);
                d[k] = even ? v / 2 * 2 : v;
            }
        }
        for (var r = 0; r < 128; r++)
            for (var c = 0; c < 128; c++)
            {
                if (Flat(c, r)) continue;
                int i0 = c * N, j0 = r * N, edges = 0, inner = 0;
                var run = new List<int>();
                for (var j = j0 + 1; j < j0 + N; j++)
                    for (var i = i0 + 1; i < i0 + N; i++) run.Add(j * Side + i);
                for (var k = 1; k < N; k++)
                    edges += d[j0 * Side + i0 + k] + d[(j0 + N) * Side + i0 + k] + d[(j0 + k) * Side + i0] + d[(j0 + k) * Side + i0 + N];
                foreach (var k in run) inner += d[k];
                var need = -edges / 2 - inner;                             // edges are even: exact
                foreach (var k in run)
                {
                    if (need == 0) break;
                    var move = Math.Clamp(need, -hold - d[k], hold - d[k]);
                    d[k] += move;
                    need -= move;
                }
            }
        for (var k = 0; k < H.Length; k++) H[k] = smooth.H[k] + d[k];
    }

    /// <summary>Vista's random numbers (method_build_land.md section 2): SeedRandom $73C5C, RandomByte $73C2E, and
    /// RandomNudge $73CD2 in 32-bit whole numbers (its word wraps are Vista's quirks, not its method).</summary>
    sealed class VistaRandom(uint seed)
    {
        uint _s1 = 0x67B139E8, _s2 = seed + 0x6928CEEF;              // $73C5C-$73C72
        int Byte()                                                  // $73C2E
        {
            _s1 += _s2;
            _s1 ^= _s1 >> 11;
            return (sbyte)(byte)(_s1 ^ (_s2 >> 5));
        }
        /// <summary>$73CD2 for a step of `step` world units at dimension dim: one byte thrown away, b = (r2 r3 asr 6) +
        /// 32, then b * n / 64 * dim / 32 with n = step / 30 Vista points - one floor division, as n is not whole here.</summary>
        public int Nudge(int step, int dim)
        {
            Byte();
            int r2 = Byte(), r3 = Byte();
            var b = (r2 * r3 >> 6) + 32;
            long p = (long)b * step * dim, q = 64L * VistaSpacing * 32;
            return (int)(p >= 0 ? p / q : -((-p + q - 1) / q));    // floor, as Vista's asr
        }
    }

    /// <summary>FractalDetailExisting ($14060) over the whole store, the step from N (an original cell) halving while
    /// past 1: for each square at the step, BEFORE changing it, relief = |TL-TR| + |BL-BR| + |TL-BL| + |TR-BR| and the
    /// local dimension (relief / step in Vista points) * dim / 128 + dim / 4 ($14244-$14286); then the bottom midpoint
    /// (only if not on the last row, $14292), the right midpoint (only if not on the last column, $142DC), and the
    /// centre from the four edge midpoints, each its average (asr) + a nudge ($14298-$14392). Only the lattice at the
    /// first step - the original's points - keeps its heights; the store's outer edges keep theirs (as Vista's).</summary>
    void Detail(ushort seed, int dimension)
    {
        var rnd = new VistaRandom(seed);
        var m = 128 * N;
        var cell = 512 / N;
        for (var d = N; d > 1; d /= 2)
        {
            var half = d / 2;
            var step = d * cell;                                        // world units
            for (var r = 0; r < m - 1; r += d)
                for (var c = 0; c < m - 1; c += d)
                {
                    int tl = this[c, r], tr = this[c + d, r], bl = this[c, r + d], br = this[c + d, r + d];
                    var relief = Math.Abs(tl - tr) + Math.Abs(bl - br) + Math.Abs(tl - bl) + Math.Abs(tr - br);
                    var dim = (int)((long)relief * VistaSpacing / step) * dimension / 128 + dimension / 4;
                    if (r + d < m) this[c + half, r + d] = (bl + br >> 1) + rnd.Nudge(step, dim);
                    if (c + d < m) this[c + d, r + half] = (tr + br >> 1) + rnd.Nudge(step, dim);
                    this[c + half, r + half] = (this[c, r + half] + this[c + d, r + half] + this[c + half, r] + this[c + half, r + d] >> 2)
                        + rnd.Nudge(step, dim);
                }
        }
    }

    /// <summary>The height factor on the whole, then the store's word cap (Top).</summary>
    void Scale(int num, int den)
    {
        for (var k = 0; k < H.Length; k++) H[k] = (int)Math.Min(Top, (long)H[k] * num / den);
    }
}
