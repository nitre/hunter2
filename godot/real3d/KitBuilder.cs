using System.Collections.Generic;
using System.Linq;

namespace Hunter;

/// <summary>STUNTIN': the building blocks the racing kits (RacingKit's car, JeepKit's jeep) are made of - a model's corner, edge
/// and face lists (the model stored Fine times its size) and the pipes, boxes and faces put on them, each in the kit's units.</summary>
public sealed class KitBuilder
{
    public List<CardModels.Corner> Corners;
    public List<CardModels.Edge> Edges;
    public readonly List<CardModels.Face> Faces;
    public readonly List<int> Origin;
    /// <summary>The origin the next face takes (Tube / Bent set the solid one, Box the plain one).</summary>
    public int FaceOrigin = RacingKit.Origin;
    /// <summary>While set, a corner at a place already made is reused, so faces built then share their edges.</summary>
    public Dictionary<(int, int, int), int>? Shared;
    /// <summary>How far back along each length a bent pipe's corner starts curving (model units): square tube takes a tight bend.</summary>
    public const double Bend = 5;
    const int Fine = RacingKit.Fine;
    readonly Dictionary<(int, int), int> edgeId = new();

    public KitBuilder(List<CardModels.Corner> corners, List<CardModels.Edge> edges, List<CardModels.Face> faces, List<int> origin)
    {
        Corners = corners; Edges = edges; Faces = faces; Origin = origin;
        for (var i = 0; i < edges.Count; i++) edgeId.TryAdd((System.Math.Min(edges[i].A, edges[i].B), System.Math.Max(edges[i].A, edges[i].B)), i);
    }

    /// <summary>A corner at x, y, z (units; kept to 1 / Fine of one).</summary>
    public int Corner(double x, double y, double z)
    {
        int ix = (int)System.Math.Round(x * Fine), iy = (int)System.Math.Round(y * Fine), iz = (int)System.Math.Round(z * Fine);
        if (Shared is not null && Shared.TryGetValue((ix, iy, iz), out var at)) return at;
        Corners.Add(new(0, ix, iy, iz));
        if (Shared is not null) Shared[(ix, iy, iz)] = Corners.Count - 1;
        return Corners.Count - 1;
    }

    /// <summary>A length snapped to the kit's grid.</summary>
    public static double Q(double v) => System.Math.Round(v * Fine) / Fine;

    public int Edge(int a, int b)
    {
        var key = (System.Math.Min(a, b), System.Math.Max(a, b));
        if (!edgeId.TryGetValue(key, out var id)) { edgeId[key] = id = Edges.Count; Edges.Add(new(a, b)); }
        return id;
    }

    /// <summary>A face wound OUTWARD (its corners' cross product - the shader's own, gpumodels_body - pointing away from inside):
    /// the kit's faces keep their own side (meta bit 7, GpuWorld.Bake) - lit by their own direction, and not widened to a game
    /// pixel (widened, a pipe's narrow faces sawtoothed its outline and speckled the stacks).</summary>
    public void Face(int colour, (double X, double Y, double Z) inside, params int[] loop)
    {
        var p = loop.Select(i => Corners[i]).ToArray();
        double ux = p[1].X - p[0].X, uy = p[1].Y - p[0].Y, uz = p[1].Z - p[0].Z, vx = p[2].X - p[0].X, vy = p[2].Y - p[0].Y, vz = p[2].Z - p[0].Z;
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        double cx = p.Average(q => q.X) - inside.X * Fine, cy = p.Average(q => q.Y) - inside.Y * Fine, cz = p.Average(q => q.Z) - inside.Z * Fine;
        if (nx * cx + ny * cy + nz * cz < 0) loop = [.. loop.Reverse()];
        Faces.Add(new(colour, 0, Enumerable.Range(0, loop.Length).Select(i => Edge(loop[i], loop[(i + 1) % loop.Length])).ToArray()));
        Origin.Add(FaceOrigin);
    }

    /// <summary>A tube from a to b, radius r: square (the cage - r its half width, its corners on the grid) or round, 8 sides (the
    /// stacks); the ends closed in their own colours (-1: left open - it stands in the body).</summary>
    public void Tube(int colour, (double X, double Y, double Z) a, (double X, double Y, double Z) b, double r, int capA = -1, int capB = -1, bool round = false)
    {
        FaceOrigin = RacingKit.SolidOrigin;
        var n = round ? 8 : 4;
        double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z, dl = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        dx /= dl; dy /= dl; dz /= dl;
        double ux = dz, uy = 0, uz = -dx;                                          // across the tube: axis x up, or (if upright) x across
        if (ux * ux + uz * uz < 1e-6) { ux = 1; uy = 0; uz = 0; }
        var ul = System.Math.Sqrt(ux * ux + uy * uy + uz * uz); ux /= ul; uy /= ul; uz /= ul;
        double vx = dy * uz - dz * uy, vy = dz * ux - dx * uz, vz = dx * uy - dy * ux;
        int[] Ring((double X, double Y, double Z) c) => Enumerable.Range(0, n).Select(i =>
        {
            var k = round ? 1 : System.Math.Sqrt(2);                               // a square's corners out to r on each face
            double t = i * System.Math.Tau / n + (round ? 0 : System.Math.PI / 4), cs = System.Math.Cos(t) * r * k, sn = System.Math.Sin(t) * r * k;
            // the shape snapped once (whole-unit offsets from the centre), the centre snapped apart: both ends the same square,
            // the faces between them flat (each corner snapped on its own, the ends differed and the faces twisted)
            return Corner(Q(c.X) + Q(ux * cs + vx * sn), Q(c.Y) + Q(uy * cs + vy * sn), Q(c.Z) + Q(uz * cs + vz * sn));
        }).ToArray();
        var ra = Ring(a); var rb = Ring(b);
        var mid = ((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
        for (var i = 0; i < n; i++) Face(colour, mid, ra[i], ra[(i + 1) % n], rb[(i + 1) % n], rb[i]);
        if (capA >= 0) Face(capA, b, ra);
        if (capB >= 0) Face(capB, a, rb);
    }

    /// <summary>A BENT PIPE through the points: one pipe, its rings carried along without a twist (each ring's "across" the last
    /// one's turned onto the new direction), at a corner a ring across the two lengths' middle direction (stretched across the
    /// bend) so the pipe keeps its thickness round it - no ends meeting at a corner, nothing sticking out.</summary>
    public void Bent(int colour, double r, params (double X, double Y, double Z)[] corners0) => BentPath(colour, r, Rounded(corners0));

    /// <summary>A CLOSED bent pipe: the path round and back to its start (the start a point along a straight length, so it is
    /// not a corner): carried round a loop that is not flat, the rings come back turned about the pipe - that turn (less whole
    /// sides) is taken off evenly along the loop, so the last ring lies on the first.</summary>
    public void Loop(int colour, double r, params (double X, double Y, double Z)[] corners0)
    {
        var round = Rounded([.. corners0, corners0[0]]);
        BentPath(colour, r, round, closed: true);
    }

    void BentPath(int colour, double r, (double X, double Y, double Z)[] pts, bool closed = false)
    {
        FaceOrigin = RacingKit.SolidOrigin;
        const int n = 4;
        (double, double, double) Sub((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        (double X, double Y, double Z) Norm((double X, double Y, double Z) a) { var l = System.Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z); return (a.X / l, a.Y / l, a.Z / l); }
        double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        var seg = Enumerable.Range(0, pts.Length - 1).Select(i => Norm(Sub(pts[i + 1], pts[i]))).ToArray();
        // u turned by the rotation taking direction a to direction b (about a x b; Rodrigues)
        (double X, double Y, double Z) Turn((double X, double Y, double Z) q, (double X, double Y, double Z) a, (double X, double Y, double Z) b)
        {
            var axis = Cross(a, b);
            var sin = System.Math.Sqrt(Dot(axis, axis));
            var cos = Dot(a, b);
            if (sin < 1e-9) return q;
            axis = (axis.X / sin, axis.Y / sin, axis.Z / sin);
            var kq = Cross(axis, q);
            var kd = Dot(axis, q) * (1 - cos);
            return Norm((q.X * cos + kq.X * sin + axis.X * kd, q.Y * cos + kq.Y * sin + axis.Y * kd, q.Z * cos + kq.Z * sin + axis.Z * kd));
        }
        var rings = new int[pts.Length][];
        (double X, double Y, double Z) u = (seg[0].Z, 0, -seg[0].X);
        if (u.X * u.X + u.Z * u.Z < 1e-6) u = (1, 0, 0);
        u = Norm(u);
        // a ring's way: at the ends, or beside a straight stretch (a side more than half again the other's: a rounded corner's
        // first and last points), square to that stretch - both ends of a straight stretch then the same square, its faces
        // flat after snapping; the bend's middle cut across both ways (the mitre)
        double Len(int i) { var a = pts[i + 1]; var b = pts[i]; return System.Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z)); }
        bool Square(int k) => k == 0 || k == pts.Length - 1 || Len(k - 1) > 1.5 * Len(k) || Len(k) > 1.5 * Len(k - 1);
        (double X, double Y, double Z) Dir(int k) => k == 0 ? seg[0] : k == pts.Length - 1 ? seg[^1]
            : Len(k - 1) > 1.5 * Len(k) ? seg[k - 1] : Len(k) > 1.5 * Len(k - 1) ? seg[k]
            : Norm((seg[k - 1].X + seg[k].X, seg[k - 1].Y + seg[k].Y, seg[k - 1].Z + seg[k].Z));
        var us = new (double X, double Y, double Z)[pts.Length];
        for (var k = 0; k < pts.Length; k++)
        {                                                                           // turned WITH the bend (about its axis, by the angle
            if (k > 0) u = Turn(u, Dir(k - 1), Dir(k));                             // from the last ring's direction to this one's) - a
            us[k] = u;                                                              // projection flipped where "across" ran down the next length
        }
        if (closed)
        {                                                                           // the turn the loop brought back, less whole sides,
            var d0 = Dir(0); var u0 = us[0]; var ue = us[^1];                       // taken off evenly along it (about each ring's own way)
            var twist = System.Math.Atan2(Dot(Cross(ue, u0), d0), Dot(ue, u0));
            var side = System.Math.Tau / n;
            twist -= System.Math.Round(twist / side) * side;
            for (var k = 1; k < pts.Length; k++)
            {
                var a = twist * k / (pts.Length - 1); var dk = Dir(k); var q = us[k];
                var kq = Cross(dk, q);
                us[k] = Norm((q.X * System.Math.Cos(a) + kq.X * System.Math.Sin(a), q.Y * System.Math.Cos(a) + kq.Y * System.Math.Sin(a), q.Z * System.Math.Cos(a) + kq.Z * System.Math.Sin(a)));
            }
        }
        for (var k = 0; k < pts.Length; k++)
        {
            var dir = Dir(k); u = us[k];
            var v = Cross(dir, u);
            // at a corner the ring stretches ACROSS THE BEND only (w: in the bend's plane, square to the ring's direction) by
            // 1 / cos of half the turn; its other way keeps the pipe's own thickness (stretched both ways it swelled at each
            // corner and looked twisted)
            var widen = Square(k) ? 1.0 : 1.0 / System.Math.Max(0.5, Dot(seg[k - 1], dir));
            (double X, double Y, double Z) w = (0, 0, 0);
            if (widen > 1.0001) w = Norm(Cross(Cross(seg[k - 1], seg[k]), dir));
            var c = pts[k];
            rings[k] = Enumerable.Range(0, n).Select(i =>
            {
                double t = i * System.Math.Tau / n + System.Math.PI / 4, a = System.Math.Cos(t) * r * System.Math.Sqrt(2), b = System.Math.Sin(t) * r * System.Math.Sqrt(2);
                (double X, double Y, double Z) q = (u.X * a + v.X * b, u.Y * a + v.Y * b, u.Z * a + v.Z * b);
                var along = Dot(q, w) * (widen - 1);
                q = (q.X + w.X * along, q.Y + w.Y * along, q.Z + w.Z * along);
                return Corner(Q(c.X) + Q(q.X), Q(c.Y) + Q(q.Y), Q(c.Z) + Q(q.Z));   // as Tube's: a straight run's rings the same square
            }).ToArray();
        }
        for (var k = 0; k + 1 < pts.Length; k++)
        {
            var mid = ((pts[k].X + pts[k + 1].X) / 2, (pts[k].Y + pts[k + 1].Y) / 2, (pts[k].Z + pts[k + 1].Z) / 2);
            for (var i = 0; i < n; i++) Face(colour, mid, rings[k][i], rings[k][(i + 1) % n], rings[k + 1][(i + 1) % n], rings[k + 1][i]);
        }
    }

    /// <summary>A bent pipe's corners rounded: each corner c between a (before) and b (after) becomes the points Bend back from c
    /// along each length and the curve's middle between them (a quarter of each and half of c).</summary>
    public static (double X, double Y, double Z)[] Rounded((double X, double Y, double Z)[] p)
    {
        var o = new List<(double X, double Y, double Z)> { p[0] };
        for (var k = 1; k + 1 < p.Length; k++)
        {
            (double X, double Y, double Z) Toward((double X, double Y, double Z) a, (double X, double Y, double Z) b, double d)
            {
                double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z, l = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                return (a.X + dx / l * d, a.Y + dy / l * d, a.Z + dz / l * d);
            }
            var a = Toward(p[k], p[k - 1], Bend); var b = Toward(p[k], p[k + 1], Bend);
            o.Add(a);
            o.Add((0.25 * a.X + 0.5 * p[k].X + 0.25 * b.X, 0.25 * a.Y + 0.5 * p[k].Y + 0.25 * b.Y, 0.25 * a.Z + 0.5 * p[k].Z + 0.25 * b.Z));
            o.Add(b);
        }
        o.Add(p[^1]);
        return [.. o];
    }

    /// <summary>Where a bar meets a bent pipe's rounded corner: the curve's middle (as Rounded puts it).</summary>
    public static (double X, double Y, double Z) BendMiddle((double X, double Y, double Z) before, (double X, double Y, double Z) c, (double X, double Y, double Z) after)
        => Rounded([before, c, after])[2];

    /// <summary>THE WHEELS ALONG THE CAR, in the model's own units (before WidenWheels): each spin-about-x part (its pivot and the
    /// discs' corners at x +-wheelX after it) moved along so its axle is at +-axleZ (the front ones forward, the rear back).</summary>
    public void MoveWheels(int wheelX, int axleZ)
    {
        var spin = false; int dz = 0;
        for (var k = 0; k < Corners.Count; k++)
        {
            var c = Corners[k];
            if (c.Flag != 0)
            {
                spin = ((c.Flag >> 4) & 7) == 1 && ((c.Flag >> 2) & 3) == 0;
                if (!spin) continue;
                dz = System.Math.Sign(c.Z) * axleZ - c.Z;
                Corners[k] = c with { Z = c.Z + dz };
                continue;
            }
            if (spin && System.Math.Abs(c.X) == wheelX) Corners[k] = c with { Z = c.Z + dz };
        }
    }

    /// <summary>THE WHEELS OUT WIDE, in the model's own units (before FineScale): each spin-about-x part's corners at x +-wheelX
    /// (rim and pivot) move out to +-discX, the disc shrunk to 4/5 of its rim - set into the tyre, the hub's sectors (drawn pulled
    /// toward the eye, 0.4% of the distance - a face on a face, gpumodels_body) beat neither the tyre's outer edge nor its tread.
    /// Returns each wheel's side, pivot y and z and radius.</summary>
    public List<(int Sign, int Py, int Pz, int R)> WidenWheels(int wheelX, int discX)
    {
        var rims = new List<(int Sign, int Py, int Pz, int R)>();
        var spin = false; int py = 0, pz = 0;
        var low = new Dictionary<(int, int, int, int), int>();
        for (var k = 0; k < Corners.Count; k++)
        {
            var c = Corners[k];
            if (c.Flag != 0) { spin = ((c.Flag >> 4) & 7) == 1 && ((c.Flag >> 2) & 3) == 0; py = c.Y; pz = c.Z; }
            if (!spin || System.Math.Abs(c.X) != wheelX) continue;
            var s = System.Math.Sign(c.X);
            Corners[k] = c.Flag != 0 ? c with { X = s * discX }
                : c with { X = s * discX, Y = py + (int)System.Math.Round((c.Y - py) * 0.8), Z = pz + (int)System.Math.Round((c.Z - pz) * 0.8) };
            if (c.Flag != 0) continue;
            var key = (s, py, pz, 0);
            low[key] = low.TryGetValue(key, out var l) ? System.Math.Min(l, c.Y) : c.Y;
        }
        foreach (var ((s, y0, z0, _), l) in low) rims.Add((s, y0, z0, y0 - l));
        return rims;
    }

    /// <summary>BIGGER WHEELS (whole units, after WidenWheels): each spin-about-x part's disc at +-discX scaled by k about its
    /// pivot; the rims' radii with it (for Tyres).</summary>
    public List<(int Sign, int Py, int Pz, int R)> ScaleWheels(List<(int Sign, int Py, int Pz, int R)> rims, int discX, double k)
    {
        var spin = false; int py = 0, pz = 0;
        for (var i = 0; i < Corners.Count; i++)
        {
            var c = Corners[i];
            if (c.Flag != 0) { spin = ((c.Flag >> 4) & 7) == 1 && ((c.Flag >> 2) & 3) == 0; py = c.Y; pz = c.Z; continue; }
            if (!spin || System.Math.Abs(c.X) != discX) continue;
            Corners[i] = c with { Y = py + (int)System.Math.Round((c.Y - py) * k), Z = pz + (int)System.Math.Round((c.Z - pz) * k) };
        }
        return [.. rims.Select(r => (r.Sign, r.Py, r.Pz, (int)System.Math.Round(r.R * k)))];
    }

    /// <summary>THE FINE GRID: the model's own corners (whole units) scaled up to the kit's (Fine to a unit).</summary>
    public void FineScale()
    {
        for (var k = 0; k < Corners.Count; k++) Corners[k] = Corners[k] with { X = Corners[k].X * Fine, Y = Corners[k].Y * Fine, Z = Corners[k].Z * Fine };
    }

    /// <summary>THE TYRES: on each wheel a black tread band from +-tyreIn out to a unit past +-wheelOut (its 16 corners exactly the
    /// rim's, RoundWheels' rounding; the unit proud of the disc hides the disc's coloured face seen edge-on), a black inner wall at
    /// +-tyreIn and an outer sidewall ring from sidewallIn out to the rim (the disc's sectors show only inside it). The bands
    /// stand still (round, so a turning band would look the same). Returns each tyre's corners by side and pivot z.</summary>
    public Dictionary<(int Sign, int Pz), List<int>> Tyres(List<(int Sign, int Py, int Pz, int R)> rims, int tyreIn, int wheelOut, int sidewallIn)
    {
        const int Black = 0;
        FaceOrigin = RacingKit.Origin;
        var tyreCorners = new Dictionary<(int Sign, int Pz), List<int>>();
        foreach (var (s, py, pz, r) in rims)
        {
            var first = Corners.Count;
            const int n = RoundWheels.Sides;
            int[] Ring(int x, int rad = -1) => Enumerable.Range(0, n).Select(j =>
            {
                var a = j * 2 * System.Math.PI / n;
                var rr = rad < 0 ? r : rad;
                return Corner(x, py + (int)System.Math.Round(rr * System.Math.Cos(a), System.MidpointRounding.AwayFromZero),
                    pz + (int)System.Math.Round(rr * System.Math.Sin(a), System.MidpointRounding.AwayFromZero));
            }).ToArray();
            var inner = Ring(s * tyreIn); var outer = Ring(s * (wheelOut + 1));
            for (var j = 0; j < n; j++) Face(Black, (s * (tyreIn + wheelOut) / 2.0, py, pz), inner[j], inner[(j + 1) % n], outer[(j + 1) % n], outer[j]);
            var hub = Corner(s * tyreIn, py, pz);
            for (var j = 0; j < n; j++) Face(Black, (s * wheelOut, py, pz), hub, inner[j], inner[(j + 1) % n]);
            var wall = Ring(s * (wheelOut + 1), sidewallIn);
            for (var j = 0; j < n; j++) Face(Black, (s * tyreIn, py, pz), wall[j], wall[(j + 1) % n], outer[(j + 1) % n], outer[j]);
            tyreCorners[(s, pz)] = Enumerable.Range(first, Corners.Count - first).ToList();
        }
        return tyreCorners;
    }

    /// <summary>THE FRONT WHEELS STEER: the model's front axle is one spinning part (its pivot, then both discs' corners); the
    /// kit's is two steered wheels (kind 6, model.gdshaderinc: spin as before, then turned by the steering), each its own pivot at
    /// its tyre's middle, then its disc's corners, then its tyre's (a turning part takes the unflagged corners after its pivot, so
    /// the corners are put in that order and the edges renumbered). After the model's own corners come runOn's (parts of the kit
    /// that run on from them); the rest of the kit stays still - its first corner ends the part before (CarInside.Plain).</summary>
    public void SteerFront(int nOld, int tyreIn, int wheelOut, Dictionary<(int Sign, int Pz), List<int>> tyreCorners, IEnumerable<int> runOn)
    {
        const int SteerFlag = 6 << 4;
        var order = new List<int>();
        int frontPivot = -1;
        for (var k = 0; k < nOld && frontPivot < 0; k++)
        {
            var c = Corners[k];
            if (c.Flag != 0 && ((c.Flag >> 4) & 7) == 1 && ((c.Flag >> 2) & 3) == 0 && c.Z > 0) frontPivot = k;
        }
        if (frontPivot < 0) throw new System.InvalidOperationException("racing kit: no front axle part");
        var end = frontPivot + 1;
        while (end < nOld && Corners[end].Flag == 0) end++;
        var piv = Corners[frontPivot];
        var steered = SteerFlag | (piv.Flag & 3);                                 // its own spin channel kept
        for (var k = 0; k < frontPivot; k++) order.Add(k);
        foreach (var s in new[] { 1, -1 })
        {
            var mid = s * (tyreIn + wheelOut) / 2;                               // turned about the tyre's middle (its spin is about x, so
            if (s == System.Math.Sign(piv.X)) { Corners[frontPivot] = piv with { Flag = steered, X = mid * Fine }; order.Add(frontPivot); }   // the pivot's x is free)
            else { Corners.Add(new(steered, mid * Fine, piv.Y, piv.Z)); order.Add(Corners.Count - 1); }
            for (var k = frontPivot + 1; k < end; k++) if (System.Math.Sign(Corners[k].X) == s) order.Add(k);
            order.AddRange(tyreCorners[(s, piv.Z / Fine)]);
        }
        for (var k = end; k < nOld; k++) order.Add(k);
        order.AddRange(runOn);
        var placed = order.ToHashSet();
        var kitFirst = order.Count;
        for (var k = nOld; k < Corners.Count; k++) if (!placed.Contains(k)) order.Add(k);
        if (kitFirst < order.Count && Corners[order[kitFirst]].Flag == 0) Corners[order[kitFirst]] = Corners[order[kitFirst]] with { Flag = CarInside.Plain };
        var map = new int[Corners.Count];
        for (var i = 0; i < order.Count; i++) map[order[i]] = i;
        Corners = order.Select(k => Corners[k]).ToList();
        Edges = Edges.Select(e => new CardModels.Edge(map[e.A], map[e.B])).ToList();
    }

    /// <summary>A box (its four sides and top; its bottom only when it does not stand in the body).</summary>
    public void Box(int colour, int top, int x0, int y0, int z0, int x1, int y1, int z1, bool bottom = false)
    {
        FaceOrigin = RacingKit.Origin;
        int a = Corner(x0, y0, z0), b = Corner(x1, y0, z0), c = Corner(x1, y0, z1), d = Corner(x0, y0, z1);
        int e = Corner(x0, y1, z0), f = Corner(x1, y1, z0), g = Corner(x1, y1, z1), h = Corner(x0, y1, z1);
        var mid = ((x0 + x1) / 2.0, (y0 + y1) / 2.0, (z0 + z1) / 2.0);
        Face(colour, mid, a, e, f, b); Face(colour, mid, c, g, h, d); Face(colour, mid, d, h, e, a); Face(colour, mid, b, f, g, c);
        Face(top, mid, e, h, g, f);
        if (bottom) Face(colour, mid, a, b, c, d);
    }
}
