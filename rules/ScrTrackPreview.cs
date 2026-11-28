namespace Hunter.Rules;

/// <summary>STUNTIN's track preview (the original's runTrackPreviewScreen, here the track itself in 3D): the road from its
/// cross-sections (ScrRoad) - its top two-toned a piece each, a solid panel each side from the edge down to the land, a jump's
/// gap left open - in the road mesh's own colours (RoadMesh: the original's), lit by a low sun, seen from a camera going
/// round the track; drawn into a box of a ScrScreenArt picture over the desert's sand, back to front (painter's).</summary>
public sealed class ScrTrackPreview
{
    readonly List<(double X, double Y, double Z)[]> _quads = [];
    readonly List<uint> _colours = [];
    readonly double _cx, _cz, _base, _reach;

    /// <summary>The road mesh's colour words (RoadMesh: the original's main game palette).</summary>
    const int RoadA = 0x443, RoadB = 0x554, PanelA = 0x777, PanelB = 0x311, Face = 0x111;
    /// <summary>A height step this big or more to the next cross-section is a jump's gap (RoadMesh.GapStep).</summary>
    const double GapStep = 0x280;
    /// <summary>The camera's height over the track's middle, as a share of its distance out, and how much of the box the
    /// track fills.</summary>
    const double Elevation = 0.5, Fit = 0.95;
    static readonly uint SandTop = 0x9C8460, SandBottom = 0x6E5A3C;

    public ScrTrackPreview(ScrRoad road)
    {
        var s = road.Sections;
        _cx = s.Average(q => (q.LX + q.RX) / 2);
        _cz = s.Average(q => (q.LZ + q.RZ) / 2);
        _base = s.Min(q => Math.Min(q.LY, q.RY));
        _reach = s.Max(q => Math.Max(Math.Sqrt((q.LX - _cx) * (q.LX - _cx) + (q.LZ - _cz) * (q.LZ - _cz)), Math.Sqrt((q.RX - _cx) * (q.RX - _cx) + (q.RZ - _cz) * (q.RZ - _cz))));
        for (var i = 0; i < s.Count; i++)
        {
            var a = s[i]; var b = s[(i + 1) % s.Count];
            var gap = Math.Abs((b.LY + b.RY) / 2 - (a.LY + a.RY) / 2) >= GapStep;
            if (gap) continue;                                               // a jump's gap: open
            var odd = road.Piece[i] % 2 == 1;
            Add(odd ? RoadB : RoadA, (a.LX, a.LY, a.LZ), (a.RX, a.RY, a.RZ), (b.RX, b.RY, b.RZ), (b.LX, b.LY, b.LZ));
            Add(odd ? PanelB : PanelA, (a.LX, a.LY, a.LZ), (b.LX, b.LY, b.LZ), (b.LX, _base, b.LZ), (a.LX, _base, a.LZ));
            Add(odd ? PanelB : PanelA, (a.RX, a.RY, a.RZ), (b.RX, b.RY, b.RZ), (b.RX, _base, b.RZ), (a.RX, _base, a.RZ));
        }
        for (var i = 0; i < s.Count; i++)                                    // a jump's faces: the drop at each end of a gap, dark
        {
            var a = s[i]; var b = s[(i + 1) % s.Count];
            if (Math.Abs((b.LY + b.RY) / 2 - (a.LY + a.RY) / 2) < GapStep) continue;
            Add(Face, (a.LX, a.LY, a.LZ), (a.RX, a.RY, a.RZ), (a.RX, _base, a.RZ), (a.LX, _base, a.LZ));
            Add(Face, (b.LX, b.LY, b.LZ), (b.RX, b.RY, b.RZ), (b.RX, _base, b.RZ), (b.LX, _base, b.LZ));
        }
    }

    void Add(int word, params (double X, double Y, double Z)[] q)
    {
        _quads.Add(q);
        _colours.Add((uint)(((word >> 8) & 15) * 17 << 16 | ((word >> 4) & 15) * 17 << 8 | (word & 15) * 17));
    }

    /// <summary>The track seen from angle (radians round it) into the box x0..x1, y0..y1: the sand, then the road back to front.</summary>
    public void Draw(ScrScreenArt art, int x0, int y0, int x1, int y1, double angle)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        for (var y = y0; y <= y1; y++)
        {
            var f = (y - y0) / (double)Math.Max(1, h - 1);
            var c = Mix(SandTop, SandBottom, f);
            for (var x = x0; x <= x1; x++) art.Pixels[y * ScrScreenArt.W + x] = c;
        }
        // the track's middle at the box's middle, at one scale for every angle (the most that keeps the whole track inside Fit
        // of the box all the way round - fitted each frame it swelled and shrank as it turned)
        if (_fitFor != (w, h))
        {
            _fitFor = (w, h);
            _scale = double.MaxValue;
            for (var k = 0; k < 24; k++)
            {
                var raw = Camera(k * Math.Tau / 24);
                double mx = 0, my = 0;
                foreach (var q in _quads) foreach (var v in q) { var r = raw(v); if (r.D > 1) { mx = Math.Max(mx, Math.Abs(r.X)); my = Math.Max(my, Math.Abs(r.Y)); } }
                _scale = Math.Min(_scale, Math.Min(w / 2.0 / Math.Max(1e-9, mx), h / 2.0 / Math.Max(1e-9, my)) * Fit);
            }
        }
        var view = Camera(angle);
        double midX = x0 + w / 2.0, midY = y0 + h / 2.0;
        (double X, double Y, double D) P((double X, double Y, double Z) p) { var r = view(p); return (midX + r.X * _scale, midY + r.Y * _scale, r.D); }
        double lx = 0.45, ly = 0.8, lz = 0.4, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
        var order = new List<(double D, int I, (double X, double Y, double D)[] Pts)>(_quads.Count);
        for (var i = 0; i < _quads.Count; i++)
        {
            var pts = _quads[i].Select(P).ToArray();
            if (pts.Any(p => p.D <= 1)) continue;
            order.Add((pts.Average(p => p.D), i, pts));
        }
        order.Sort((a, b) => b.D.CompareTo(a.D));
        foreach (var (_, i, pts) in order)
        {
            var q = _quads[i];
            double ax = q[1].X - q[0].X, ay = q[1].Y - q[0].Y, az = q[1].Z - q[0].Z, bx = q[3].X - q[0].X, by = q[3].Y - q[0].Y, bz = q[3].Z - q[0].Z;
            double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            var lit = nl < 1e-9 ? 1 : 0.55 + 0.6 * Math.Abs(nx * lx + ny * ly + nz * lz) / nl / ll;
            art.Polygon(pts.Select(p => (p.X, p.Y)).ToArray(), ScrScreenArt.Shade(_colours[i], lit), x0, y0, x1, y1);
        }
    }

    (int W, int H) _fitFor;
    double _scale;

    /// <summary>The camera at angle round the track: out from its middle at the track's reach x 3.2 (flatter, the near side not swinging far down), up by Elevation, looking at
    /// the middle; a point's place across and up at a unit's distance (+ right, + down) and its distance.</summary>
    Func<(double X, double Y, double Z), (double X, double Y, double D)> Camera(double angle)
    {
        double dist = _reach * 3.2, ex = _cx + Math.Sin(angle) * dist, ez = _cz + Math.Cos(angle) * dist, ey = _base + dist * Elevation;
        double fx = _cx - ex, fy = _base - ey, fz = _cz - ez, fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
        fx /= fl; fy /= fl; fz /= fl;
        double rx = fz, rz = -fx, rl = Math.Sqrt(rx * rx + rz * rz); rx /= rl; rz /= rl;            // right: across, level
        double ux = fy * rz, uy = fz * rx - fx * rz, uz = -fy * rx;                                // up = forward x right
        return p =>
        {
            double dx = p.X - ex, dy = p.Y - ey, dz = p.Z - ez;
            var d = dx * fx + dy * fy + dz * fz;
            return ((dx * rx + dz * rz) / d, -(dx * ux + dy * uy + dz * uz) / d, d);
        };
    }

    static uint Mix(uint a, uint b, double f)
    {
        uint C(int s) => (uint)Math.Round(((a >> s) & 255) * (1 - f) + ((b >> s) & 255) * f) << s;
        return C(16) | C(8) | C(0);
    }
}
