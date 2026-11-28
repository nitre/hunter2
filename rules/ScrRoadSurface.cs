namespace Hunter.Rules;

/// <summary>STUNTIN': the road as a surface things stand on (ScrRoad's cross-sections). Each strip between two
/// cross-sections is two triangles; a point over one has the road's height there, by its triangle's plane. The strips are
/// filed by the 512-unit cells they cross so a lookup reads a few. A road can pass over itself (later tracks) and over
/// the land, so the surface a thing is on is the highest road under it that is not far above it (Under).</summary>
public sealed class ScrRoadSurface
{
    /// <summary>How far above a thing the road may be and still be the surface it stands on: what a wheel climbs in one
    /// step (a road start or a ramp's foot from the land).</summary>
    public const int Climb = 192;

    readonly (double X, double Y, double Z)[] _l, _r;
    readonly Dictionary<int, List<int>> _cells = [];

    /// <summary>The start: the middle of the spawn piece's first cross-section (the original's playerSpawnSegment), and the
    /// way round the lap there (a heading word).</summary>
    public (int X, int Z) Start { get; }
    public ushort StartHeading { get; }
    public ScrRoad Road { get; }

    public ScrRoadSurface(ScrRoad road)
    {
        Road = road;
        var s = road.Sections;
        var k0 = road.SpawnSection;
        Start = ((int)Math.Round((s[k0].LX + s[k0].RX) / 2), (int)Math.Round((s[k0].LZ + s[k0].RZ) / 2));
        _l = s.Select(c => (c.LX, c.LY, c.LZ)).ToArray();
        _r = s.Select(c => (c.RX, c.RY, c.RZ)).ToArray();
        for (var i = 0; i < s.Count; i++)
        {
            var j = (i + 1) % s.Count;
            var xs = new[] { _l[i].X, _r[i].X, _l[j].X, _r[j].X };
            var zs = new[] { _l[i].Z, _r[i].Z, _l[j].Z, _r[j].Z };
            for (var cz = (int)zs.Min() >> 9; cz <= (int)zs.Max() >> 9; cz++)
                for (var cx = (int)xs.Min() >> 9; cx <= (int)xs.Max() >> 9; cx++)
                {
                    if (!_cells.TryGetValue(cz * 128 + cx, out var list)) _cells[cz * 128 + cx] = list = [];
                    list.Add(i);
                }
        }
        StartHeading = Way(k0);
        Bridge = ScrBridge.For(this);
    }

    /// <summary>Draw Bridge's moving leaves (null on the other tracks).</summary>
    public ScrBridge? Bridge { get; }

    /// <summary>A cross-section's height, both edges (world units), here and in the road (the draw bridge's leaves).</summary>
    public void SetHeight(int i, double y)
    {
        _l[i] = (_l[i].X, y, _l[i].Z);
        _r[i] = (_r[i].X, y, _r[i].Z);
        Road.SetHeight(i, y);
    }

    /// <summary>The way round the lap along strip i (from cross-section i to the next), as a heading word.</summary>
    public ushort Way(int i)
    {
        var k = (i + 1) % _l.Length;
        double dx = (_l[k].X + _r[k].X - _l[i].X - _r[i].X) / 2, dz = (_l[k].Z + _r[k].Z - _l[i].Z - _r[i].Z) / 2;
        if (dx == 0 && dz == 0 && k != i) return Way(k);                    // a section on the next one (the lap's closing one)
        return (ushort)(int)Math.Round(Math.Atan2(dx, dz) / (2 * Math.PI) * 65536);
    }

    /// <summary>The strip that carries a thing at height y over (x, z) (the highest within climb, as Under); -1 none.</summary>
    public int StripAt(int x, int z, int y, int climb = Climb)
    {
        if (!_cells.TryGetValue((z >> 9) * 128 + (x >> 9), out var list)) return -1;
        int best = int.MinValue, at = -1;
        foreach (var i in list)
        {
            var h = OnStrip(i, x, z) is { } hd ? (int?)(int)Math.Round(hd) : null;
            if (h is { } v && v <= y + climb && v > best) (best, at) = (v, i);
        }
        return at;
    }

    /// <summary>The road's height at (x, z) on every strip there (none: empty).</summary>
    public IEnumerable<int> HeightsAt(int x, int z)
    {
        if (!_cells.TryGetValue((z >> 9) * 128 + (x >> 9), out var list)) yield break;
        foreach (var i in list)
        {
            if (OnStrip(i, x, z) is { } h) yield return (int)Math.Round(h);
        }
    }

    /// <summary>The road's direction under a thing at height y over (x, z), as a heading word (65536 a turn, 0 toward +z,
    /// a quarter toward +x - Movement.Vector's): the way round the lap of the strip that carries it (the highest within
    /// Climb, as Under); null when no road carries it. Each strip's own way, so on a corner it turns strip by strip.</summary>
    public ushort? Direction(int x, int z, int y) => StripAt(x, z, y) is var at && at >= 0 ? Way(at) : null;

    /// <summary>The surface for a thing at height y over (x, z): the highest road there no more than Climb above it,
    /// or the land's height when that is higher or there is no such road.</summary>
    public int Under(int x, int z, int y, int land, int climb = Climb)
    {
        var best = land;
        foreach (var h in HeightsAt(x, z))
            if (h <= y + climb && h > best) best = h;
        return best;
    }

    /// <summary>The road's height at (px, pz) on strip i (cross-section i to the next), null when the point is off it: the strip
    /// a smooth twisted surface through its four corners (bilinear: u across from the left edge, v along to the next cross-section),
    /// as RoadMesh draws it - two flat triangles creased along the diagonal wherever a strip twists, and a car's wheels dropped
    /// into each crease.</summary>
    double? OnStrip(int i, double px, double pz)
    {
        var j = (i + 1) % _l.Length;
        return Patch(px, pz, _l[i], _r[i], _r[j], _l[j]);
    }

    /// <summary>The height at (px, pz) on the bilinear patch a (u 0, v 0), b (1, 0), c (1, 1), d (0, 1); null off it.</summary>
    public static double? Patch(double px, double pz, (double X, double Y, double Z) a, (double X, double Y, double Z) b, (double X, double Y, double Z) c, (double X, double Y, double Z) d)
    {
        double ex = b.X - a.X, ez = b.Z - a.Z, fx = d.X - a.X, fz = d.Z - a.Z, gx = a.X - b.X + c.X - d.X, gz = a.Z - b.Z + c.Z - d.Z, hx = px - a.X, hz = pz - a.Z;
        static double Cross(double ax, double az, double bx, double bz) => ax * bz - az * bx;
        double k2 = Cross(gx, gz, fx, fz), k1 = Cross(ex, ez, fx, fz) + Cross(hx, hz, gx, gz), k0 = Cross(hx, hz, ex, ez);
        double? U(double v)
        {
            double dx = ex + gx * v, dz = ez + gz * v;
            if (Math.Abs(dx) < 1e-9 && Math.Abs(dz) < 1e-9) return null;
            return Math.Abs(dx) >= Math.Abs(dz) ? (hx - fx * v) / dx : (hz - fz * v) / dz;
        }
        const double e = 1e-6;
        static bool In(double t) => t >= -e && t <= 1 + e;
        double? At(double v) => In(v) && U(v) is { } u && In(u)
            ? (1 - u) * (1 - v) * a.Y + u * (1 - v) * b.Y + u * v * c.Y + (1 - u) * v * d.Y : null;
        if (Math.Abs(k2) < 1e-9) return Math.Abs(k1) < 1e-12 ? null : At(-k0 / k1);
        var w = k1 * k1 - 4 * k0 * k2;
        if (w < 0) return null;
        w = Math.Sqrt(w);
        return At((-k1 - w) / (2 * k2)) ?? At((-k1 + w) / (2 * k2));
    }

    static int? OnTriangle(double px, double pz, (double X, double Y, double Z) a, (double X, double Y, double Z) b, (double X, double Y, double Z) c)
    {
        var d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
        if (Math.Abs(d) < 1e-9) return null;
        var u = ((b.Z - c.Z) * (px - c.X) + (c.X - b.X) * (pz - c.Z)) / d;
        var v = ((c.Z - a.Z) * (px - c.X) + (a.X - c.X) * (pz - c.Z)) / d;
        var w = 1 - u - v;
        const double e = -1e-6;
        if (u < e || v < e || w < e) return null;
        return (int)Math.Round(u * a.Y + v * b.Y + w * c.Y);
    }
}
