using System;
using System.Collections.Generic;
using System.Linq;
using Hunter.Rules;

namespace Hunter;

/// <summary>STUNTIN's race-end pictures (the original's wreck, won, lost and promotion pictures), staged with the player's
/// racer (RacingKit, built as the card builds it) on the desert and drawn flat-shaded back to front into a box of a
/// ScrScreenArt picture, a frame at a time:
/// WRECKED - its front left wheel torn off and lying flat beside it, the front right splayed, the body down on its belly
/// tipped toward the lost wheel, every wheel still on it resting on the sand; smoke rising from its stacks, flames at them.
/// WON - just over a chequered line, a chequered flag waving on a pole, confetti falling.
/// LOST - short of the line, the opponent's jeep (JeepKit) driving across it ahead.
/// PROMOTED - on a podium, a gold trophy in front of it, the camera going round, confetti falling.
/// The picture only.</summary>
public sealed class ScrResultScene
{
    public enum Kind { Wrecked, Won, Lost, Promoted }

    readonly record struct Face((double X, double Y, double Z)[] Pts, uint Colour, bool Lit = true);

    readonly Kind _kind;
    readonly List<Face> _solid = [], _ground = [], _jeep = [];
    readonly (double X, double Y, double Z)[] _smokeFrom = [];
    readonly double _cx, _cy, _cz, _reach, _lineZ;
    double _scale = -1;

    /// <summary>The camera (its way round from the car's front - radians, toward its right - its sway, its height as a share of
    /// its distance), the smoke's puffs (their life in frames, rise and drift a frame and growth in model units), the flames,
    /// the confetti, the flag, the podium and trophy, the jeep's pace; the light.</summary>
    const double Sway = 0.08, Elevation = 0.45, Rise = 1.05, Drift = 0.3, PuffGrow = 0.16, PuffStart = 5, Podium = 34, JeepPace = 2.4;
    const int Puffs = 56, PuffLife = 170, Flames = 9, Confetti = 70, FlagCols = 8, FlagRows = 5;
    static readonly (double X, double Y, double Z) Light = (0.4, 0.85, 0.35);
    static readonly uint SandTop = 0xA08A64, SandBottom = 0x6A5638, Shadow = 0x3A2E1E, Gold = 0xE0B030, PodiumTop = 0xC8C8C0, PodiumSide = 0x8C8C84,
        White = 0xF0F0F0, Black = 0x181818;
    static readonly uint[] ConfettiColours = [0xF03020, 0x30C0F0, 0xF0D020, 0x40E060, 0xF070D0, 0xFFFFFF];
    static CardModels? _store;

    public ScrResultScene(Kind kind, byte[] objfile, IReadOnlyList<ushort> palette)
    {
        _kind = kind;
        var store = _store ??= GpuWorld.CardShapes(objfile, new Dictionary<int, int[]>(), interiors: true, worm: true);   // as the card builds them
        uint Rgb(int i) { var w = i >= 0 && i < palette.Count ? palette[i] : (ushort)0x888; return (uint)(((w >> 8) & 15) * 17 << 16 | ((w >> 4) & 15) * 17 << 8 | (w & 15) * 17); }
        var md = store.Models[store.Models.Length - 8];                     // the racer (GpuWorld.RacerModel)
        var c = Units(md);
        var real = Enumerable.Range(0, c.Length).Where(k => md.Corners[k].Flag == 0).ToArray();
        var placed = new (double X, double Y, double Z)[c.Length];
        var lift = kind == Kind.Promoted ? Podium : 0;
        Func<int[], bool> keep = _ => true;
        if (kind == Kind.Wrecked)
        {
            // each wheel's corners (out past the body, x beyond 33: tyres, discs, the coil-overs' lower ends) by its corner
            const double Out = 33;
            bool Wheel(int k, int sx, int sz) => Math.Abs(c[k].X) > Out && Math.Sign(c[k].X) == sx && Math.Sign(c[k].Z) == sz;
            (double X, double Y, double Z) Hub(int sx, int sz)            // the wheel's middle: its corners' middle
            {
                var ks = Enumerable.Range(0, c.Length).Where(k => Wheel(k, sx, sz) && Math.Abs(c[k].X) > RacingKit.TyreIn).ToArray();
                return (ks.Average(k => c[k].X), (ks.Min(k => c[k].Y) + ks.Max(k => c[k].Y)) / 2, (ks.Min(k => c[k].Z) + ks.Max(k => c[k].Z)) / 2);
            }
            var hubFL = Hub(-1, 1); var hubFR = Hub(1, 1);
            for (var k = 0; k < c.Length; k++)
            {
                var p = c[k];
                if (Wheel(k, -1, 1))
                {                                                            // torn off: laid flat on its side, out beside the front
                    var (dx, dy, dz) = (p.X - hubFL.X, p.Y - hubFL.Y, p.Z - hubFL.Z);
                    (dx, dy) = (dy, -dx);
                    placed[k] = (-96 + dx, dy, 128 + dz);
                }
                else if (Wheel(k, 1, 1))
                {                                                            // splayed: leaning out about its hub
                    var (dx, dy, dz) = (p.X - hubFR.X, p.Y - hubFR.Y, p.Z - hubFR.Z);
                    double a = 0.42, cs = Math.Cos(a), sn = Math.Sin(a);
                    placed[k] = (hubFR.X + dx * cs + dy * sn, hubFR.Y - dx * sn + dy * cs, hubFR.Z + dz);
                }
                else placed[k] = Wrecked(p);
            }
            // on the sand: the body on its belly (its lowest corner the ground), each wheel still on it moved on its broken
            // suspension until its lowest point touches the ground, the torn one lying flat on it
            var ground = real.Where(k => Math.Abs(c[k].X) <= Out).Min(k => placed[k].Y);
            foreach (var (sx, sz) in new[] { (-1, 1), (1, 1), (-1, -1), (1, -1) })
            {
                var group = real.Where(k => Wheel(k, sx, sz)).ToArray();
                if (group.Length == 0) continue;
                var dy = ground - group.Min(k => placed[k].Y);
                for (var k = 0; k < c.Length; k++) if (Wheel(k, sx, sz)) placed[k] = (placed[k].X, placed[k].Y + dy, placed[k].Z);
            }
            for (var k = 0; k < c.Length; k++) placed[k] = (placed[k].X, placed[k].Y - ground, placed[k].Z);
            keep = loop => !(loop.Any(k => Wheel(k, -1, 1)) && !loop.All(k => Wheel(k, -1, 1)));   // what joined the torn wheel: broken
            _smokeFrom = [.. RacingKit.Stacks.Select(s => Wrecked(s.Tip)).Select(p => (p.X, p.Y - ground, p.Z))];
        }
        else
        {
            var ground = real.Min(k => c[k].Y);                              // standing on its wheels
            for (var k = 0; k < c.Length; k++) placed[k] = (c[k].X, c[k].Y - ground + lift, c[k].Z);
        }
        AddModel(md, placed, Rgb, keep, _solid);
        double front = real.Max(k => c[k].Z), back = real.Min(k => c[k].Z);
        // the scene round it
        switch (kind)
        {
            case Kind.Won:
                _lineZ = back - 60;                                          // just over the line
                Line(_lineZ);
                Box(-120, 0, _lineZ - 3, -116, 150, _lineZ + 1, 0x9A9A9A);   // the flag's pole
                break;
            case Kind.Lost:
                _lineZ = front + 70;                                         // short of it
                Line(_lineZ);
                var jm = store.Models[store.Models.Length - 7];              // the opponent's jeep (GpuWorld.JeepRacerModel)
                var jc = Units(jm);
                var jg = Enumerable.Range(0, jc.Length).Where(k => jm.Corners[k].Flag == 0).Min(k => jc[k].Y);
                AddModel(jm, [.. jc.Select(p => (p.X * JeepKit.Size + 40, (p.Y - jg) * JeepKit.Size, p.Z * JeepKit.Size + _lineZ + 90))], Rgb, _ => true, _jeep);   // ahead, just over the line, drawn at its size (JeepKit.Size)
                break;
            case Kind.Promoted:
                Box(-110, 0, back - 20, 110, Podium, front + 20, PodiumSide, PodiumTop);
                Trophy(0, front + 70);
                break;
        }
        var all = _solid.SelectMany(f => f.Pts).ToArray();
        (_cx, _cy, _cz) = (all.Average(p => p.X), all.Average(p => p.Y) * 0.6, all.Average(p => p.Z));
        _reach = all.Max(p => Math.Sqrt((p.X - _cx) * (p.X - _cx) + (p.Z - _cz) * (p.Z - _cz)));
    }

    static (double X, double Y, double Z)[] Units(CardModels.Model md) =>
        [.. md.Corners.Select(q => (q.X / (double)RacingKit.Fine, q.Y / (double)RacingKit.Fine, q.Z / (double)RacingKit.Fine))];

    static void AddModel(CardModels.Model md, (double X, double Y, double Z)[] placed, Func<int, uint> rgb, Func<int[], bool> keep, List<Face> into)
    {
        foreach (var f in md.Faces)
        {
            var loop = Loop(md, f);
            if (loop is null || loop.Length < 3 || !keep(loop)) continue;
            into.Add(new Face([.. loop.Select(k => placed[k])], rgb(f.Colour & 15)));
        }
    }

    /// <summary>A chequered line across the ground at z, three rows of squares.</summary>
    void Line(double z)
    {
        for (var i = -7; i < 7; i++)
            for (var j = 0; j < 3; j++)
            {
                double x0 = i * 20, z0 = z + j * 14;
                _ground.Add(new Face([(x0, 0.5, z0), (x0 + 20, 0.5, z0), (x0 + 20, 0.5, z0 + 14), (x0, 0.5, z0 + 14)], (i + j & 1) == 0 ? White : Black, false));
            }
    }

    /// <summary>A box from (x0, y0, z0) to (x1, y1, z1): sides in one colour, its top in another.</summary>
    void Box(double x0, double y0, double z0, double x1, double y1, double z1, uint side, uint? top = null)
    {
        _solid.Add(new Face([(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)], top ?? side));
        _solid.Add(new Face([(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)], side));
        _solid.Add(new Face([(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], side));
        _solid.Add(new Face([(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)], side));
        _solid.Add(new Face([(x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0)], side));
    }

    /// <summary>A gold trophy standing on the sand at (x, z): a base, a stem, a cup widening to its rim, two handles.</summary>
    void Trophy(double x, double z)
    {
        Box(x - 16, 0, z - 16, x + 16, 10, z + 16, 0x705018, Gold);
        Box(x - 3, 10, z - 3, x + 3, 30, z + 3, Gold);
        const int n = 10;
        for (var k = 0; k < n; k++)
        {
            double a0 = k * Math.Tau / n, a1 = (k + 1) * Math.Tau / n;
            (double X, double Y, double Z) P(double a, double r, double y) => (x + Math.Cos(a) * r, y, z + Math.Sin(a) * r);
            _solid.Add(new Face([P(a0, 6, 30), P(a1, 6, 30), P(a1, 20, 62), P(a0, 20, 62)], Gold));
        }
        Box(x - 26, 44, z - 2, x - 20, 56, z + 2, Gold);
        Box(x + 20, 44, z - 2, x + 26, 56, z + 2, Gold);
    }

    /// <summary>The wrecked body: down on its belly (the jack-up and the wheels' height gone), tipped toward the lost front left
    /// wheel and nose down.</summary>
    static (double X, double Y, double Z) Wrecked((double X, double Y, double Z) p)
    {
        double x = p.X, y = p.Y - RacingKit.Lift - 12, z = p.Z;
        double roll = 0.2, pitch = 0.1;
        (x, y) = (x * Math.Cos(roll) + y * Math.Sin(roll), -x * Math.Sin(roll) + y * Math.Cos(roll));   // left side down
        (z, y) = (z * Math.Cos(pitch) + y * Math.Sin(pitch), -z * Math.Sin(pitch) + y * Math.Cos(pitch)); // nose down
        return (x, y, z);
    }

    /// <summary>A face's corners in order round it, from its edges.</summary>
    static int[]? Loop(CardModels.Model md, CardModels.Face f)
    {
        if (f.Edges.Length < 3) return null;
        var e = f.Edges.Select(i => md.Edges[i]).ToArray();
        var loop = new List<int> { e[0].A, e[0].B };
        for (var i = 1; i < e.Length - 1; i++)
        {
            var last = loop[^1];
            if (e[i].A == last) loop.Add(e[i].B);
            else if (e[i].B == last) loop.Add(e[i].A);
            else if (i == 1 && (e[i].A == loop[0] || e[i].B == loop[0])) { loop.Reverse(); loop.Add(e[i].A == loop[^1] ? e[i].B : e[i].A); }
            else return null;
        }
        return [.. loop];
    }

    /// <summary>The camera's way round at frame n: each scene's own, swaying a little (the podium's going right round).</summary>
    double AngleAt(int n) => _kind switch
    {
        Kind.Wrecked => 2.45 + Math.Sin(n / 240.0) * Sway,
        Kind.Won => 0.75 + Math.Sin(n / 240.0) * Sway,
        Kind.Lost => Math.PI - 0.35 + Math.Sin(n / 240.0) * Sway,
        _ => 0.6 + n * 0.006,
    };

    /// <summary>Frame n of the scene into the box.</summary>
    public void Draw(ScrScreenArt art, int x0, int y0, int x1, int y1, int n)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        for (var y = y0; y <= y1; y++)
        {
            var c = Mix(SandTop, SandBottom, (y - y0) / (double)Math.Max(1, h - 1));
            for (var x = x0; x <= x1; x++) art.Pixels[y * ScrScreenArt.W + x] = c;
        }
        if (_scale < 0)
        {                                                                    // one scale for every angle the camera takes
            double mx = 0, my = 0;
            var angles = _kind == Kind.Promoted ? Enumerable.Range(0, 24).Select(k => k * Math.Tau / 24) : Enumerable.Range(-2, 5).Select(k => AngleAt(0) + Sway * k / 2);
            foreach (var a in angles)
            {
                var v = Camera(a);
                foreach (var f in _solid.Concat(_jeep)) foreach (var p in f.Pts) { var r = v(p); if (r.D > 1) { mx = Math.Max(mx, Math.Abs(r.X)); my = Math.Max(my, Math.Abs(r.Y)); } }
            }
            _scale = Math.Min(w / 2.0 / mx, h / 2.0 / my) * (_kind == Kind.Wrecked ? 0.97 : 0.95);
        }
        var cam = Camera(AngleAt(n));
        double midX = x0 + w / 2.0, midY = y0 + h / 2.0 + h * 0.08;
        (double X, double Y, double D) P((double X, double Y, double Z) p) { var r = cam(p); return (midX + r.X * _scale, midY + r.Y * _scale, r.D); }
        // the ground: what lies on the sand (the line), then the solid things' shadows on it - each face flattened onto the
        // ground along the sun's way (the car on the podium onto the podium's top)
        foreach (var f in _ground) Fill(art, f, P, x0, y0, x1, y1);
        var faces = new List<Face>(_solid);
        if (_kind == Kind.Lost)
        {
            var dz = n * JeepPace % 700;                                     // from the line on away, again and again
            faces.AddRange(_jeep.Select(f => f with { Pts = [.. f.Pts.Select(p => (p.X, p.Y, p.Z + dz))] }));
        }
        foreach (var f in faces)
        {
            var floor = _kind == Kind.Promoted && f.Pts.All(p => p.Y >= Podium - 0.01) ? Podium + 0.3 : 0.3;
            var flat = f.Pts.Select(p => { var up = p.Y - floor; return P((p.X - Light.X * up / Light.Y, floor, p.Z - Light.Z * up / Light.Y)); }).ToArray();
            if (flat.All(q => q.D > 1)) art.Polygon([.. flat.Select(q => (q.X, q.Y))], Shadow, x0, y0, x1, y1);
        }
        // the solid things, moving ones placed for this frame, back to front
        if (_kind == Kind.Won) faces.AddRange(Flag(n));
        if (_kind is Kind.Won or Kind.Promoted) faces.AddRange(ConfettiAt(n));
        var order = faces.Select(f => (F: f, Pts: f.Pts.Select(P).ToArray())).Where(q => q.Pts.All(p => p.D > 1)).OrderByDescending(q => q.Pts.Average(p => p.D)).ToList();
        foreach (var (f, pts) in order) Fill(art, f, pts, x0, y0, x1, y1);
        if (_kind == Kind.Wrecked) Smoke(art, P, n, x0, y0, x1, y1);
    }

    void Fill(ScrScreenArt art, Face f, Func<(double X, double Y, double Z), (double X, double Y, double D)> P, int x0, int y0, int x1, int y1) =>
        Fill(art, f, [.. f.Pts.Select(P)], x0, y0, x1, y1);

    static void Fill(ScrScreenArt art, Face f, (double X, double Y, double D)[] pts, int x0, int y0, int x1, int y1)
    {
        var colour = f.Colour;
        if (f.Lit)
        {
            var q = f.Pts;
            double ax = q[1].X - q[0].X, ay = q[1].Y - q[0].Y, az = q[1].Z - q[0].Z, bx = q[2].X - q[0].X, by = q[2].Y - q[0].Y, bz = q[2].Z - q[0].Z;
            double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            var ll = Math.Sqrt(Light.X * Light.X + Light.Y * Light.Y + Light.Z * Light.Z);
            colour = ScrScreenArt.Shade(colour, nl < 1e-9 ? 0.8 : 0.5 + 0.65 * Math.Abs(nx * Light.X + ny * Light.Y + nz * Light.Z) / nl / ll);
        }
        art.Polygon([.. pts.Select(p => (p.X, p.Y))], colour, x0, y0, x1, y1);
    }

    /// <summary>The chequered flag at the pole's top, waving: a cloth of squares rippling more toward its free end.</summary>
    IEnumerable<Face> Flag(int n)
    {
        const double Cell = 9, Top = 150, PoleX = -116;
        (double X, double Y, double Z) At(int i, int j) => (PoleX + i * Cell, Top - j * Cell, _lineZ + Math.Sin(i * 0.8 - n * 0.16) * i * 1.4);
        for (var i = 0; i < FlagCols; i++)
            for (var j = 0; j < FlagRows; j++)
                yield return new Face([At(i, j), At(i + 1, j), At(i + 1, j + 1), At(i, j + 1)], (i + j & 1) == 0 ? White : Black);
    }

    /// <summary>The confetti: small squares falling and turning over, each its own colour, drift and pace.</summary>
    IEnumerable<Face> ConfettiAt(int n)
    {
        for (var i = 0; i < Confetti; i++)
        {
            var d = new Random(i * 104729 + 7);
            double x = _cx + (d.NextDouble() * 2 - 1) * _reach * 1.1, z = _cz + (d.NextDouble() * 2 - 1) * _reach * 0.9;
            double pace = 0.5 + d.NextDouble() * 0.6, phase = d.NextDouble() * 260, spin = 0.05 + d.NextDouble() * 0.1;
            var y = 250 - (n * pace + phase) % 260;
            x += Math.Sin((n + phase) * 0.04) * 8;
            var a = n * spin + phase;
            double cs = Math.Cos(a) * 3.5, sn = Math.Sin(a) * 3.5, tip = Math.Cos(a * 1.3) * 3.5;
            var colour = ScrScreenArt.Shade(ConfettiColours[i % ConfettiColours.Length], 0.65 + 0.35 * Math.Abs(Math.Cos(a * 1.3)));
            yield return new Face([(x - cs, y - tip, z - sn), (x + sn, y + tip, z - cs), (x + cs, y + tip, z + sn), (x - sn, y - tip, z + cs)], colour, false);
        }
    }

    /// <summary>The wreck's flames at its stacks (new each frame) and its smoke: puffs born at the stacks in turn, rising and
    /// drifting off with the wind, swelling, dark to pale, thinning out.</summary>
    void Smoke(ScrScreenArt art, Func<(double X, double Y, double Z), (double X, double Y, double D)> P, int n, int x0, int y0, int x1, int y1)
    {
        var dice = new Random(n * 7919);
        for (var s = 0; s < _smokeFrom.Length; s += 2)
            for (var k = 0; k < Flames / 2; k++)
            {
                var o = _smokeFrom[s];
                var b = P((o.X + dice.NextDouble() * 4 - 2, o.Y, o.Z + dice.NextDouble() * 4 - 2));
                var t = P((o.X + dice.NextDouble() * 6 - 3, o.Y + 6 + dice.NextDouble() * 10, o.Z + dice.NextDouble() * 6 - 3));
                var half = 1.2 + dice.NextDouble() * 1.6;
                art.Polygon([(b.X - half, b.Y), (b.X + half, b.Y), (t.X, t.Y)], dice.NextDouble() < 0.5 ? 0xFF9A20u : 0xFFD850u, x0, y0, x1, y1);
            }
        for (var i = 0; i < Puffs; i++)
        {
            var age = (n + i * PuffLife / Puffs) % PuffLife;
            var o = _smokeFrom[i * 3 % _smokeFrom.Length];
            var wob = Math.Sin((n + i * 13) / 17.0) * 3;
            var at = P((o.X - age * Drift + wob, o.Y + age * Rise, o.Z + age * Drift * 0.4));
            var life = age / (double)PuffLife;
            var r = (PuffStart + age * PuffGrow) * _scale / at.D;           // model units at its distance
            var grey = (uint)(34 + life * 100);
            art.Puff(at.X, at.Y, r, grey << 16 | grey << 8 | grey, 0.7 * Math.Pow(1 - life, 0.8) * Math.Min(1, age / 5.0), x0, y0, x1, y1);
        }
    }

    Func<(double X, double Y, double Z), (double X, double Y, double D)> Camera(double angle)
    {
        double dist = _reach * 3.2, ex = _cx + Math.Sin(angle) * dist, ez = _cz + Math.Cos(angle) * dist, ey = _cy + dist * (_kind switch { Kind.Won => 0.65, Kind.Lost => 0.8, _ => Elevation });
        double fx = _cx - ex, fy = _cy - ey, fz = _cz - ez, fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
        fx /= fl; fy /= fl; fz /= fl;
        double rx = fz, rz = -fx, rl = Math.Sqrt(rx * rx + rz * rz); rx /= rl; rz /= rl;
        double ux = fy * rz, uy = fz * rx - fx * rz, uz = -fy * rx;
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
