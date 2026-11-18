using System;
using System.Collections.Generic;
using System.Linq;
using Hunter.Rules;

namespace Hunter;

/// <summary>GLASS: SEE-THROUGH, walls with a window painted flat on them: the wall's face is drawn with a hole where each
/// window is, so the sunk pane and the room behind show. Find picks the wall of each such window (a building's glass face in
/// the plane of another face whose outline holds it); Cut makes the holed wall's triangles (ear clipping, each hole bridged
/// into the outline). GpuWorld.Bake adds them (meta Glass.Sunk) and hides the wall's own (Glass.Hidden) with SEE-THROUGH.</summary>
public static class GlassWalls
{
    /// <summary>Triangle meta bit 15: the wall's own triangles - hidden with GLASS: SEE-THROUGH (their holed copies are drawn instead).</summary>
    public const int HiddenBit = 32768;

    public sealed class Wall
    {
        public int Model, Face;                               // the wall's face (index in the bake's face list)
        public int First, Count;                              // its holed triangles in the model's table run (set by Bake)
        public int[] Outer = [];                              // the wall's outline corners
        public List<int[]> Holes = [];                        // the windows' outlines
        public List<int> Windows = [];                        // the windows' faces
        public List<(int, int, int)> Tris = [];               // the holed triangles (corner numbers)
        public string Why = "";                               // set when Bake could not use the wall
    }

    public sealed class Result
    {
        public readonly Dictionary<int, Wall> Walls = new();                 // by the wall's face
        public readonly Dictionary<int, int> WallOf = new();                 // a window's face -> its wall (the frame takes the wall's colour)
        public readonly List<(int Model, int Number, string Why)> Skipped = [];   // windows flat on nothing usable, by model and glass number
        public readonly List<(int Model, int Number, int Face)> Blocked = [];      // windows lying in a slab, twin or sticker face (drawn as a moved copy: not cut)
    }

    /// <summary>The wall of every sunk glass face of the buildings that lies flat in a face (see the class). unsunk: faces SinkGlass
    /// leaves at rest or that cannot be a wall (slab, twin, sticker).</summary>
    public static Result Find(CardModels store, IReadOnlyList<(int Model, int Base, int N, int Meta, int Ordinal)> faces,
        bool[] unsunk, Dictionary<(int, int), int> glassNumber)
    {
        var res = new Result();
        for (var lo = 0; lo < faces.Count;)
        {
            var hi = lo;
            while (hi + 1 < faces.Count && faces[hi + 1].Model == faces[lo].Model) hi++;
            if (faces[lo].Model >= 0x80) FindIn(store, faces, unsunk, glassNumber, lo, hi, res);   // buildings: a vehicle's windows share their edges
            lo = hi + 1;
        }
        return res;
    }

    static void FindIn(CardModels store, IReadOnlyList<(int Model, int Base, int N, int Meta, int Ordinal)> faces,
        bool[] unsunk, Dictionary<(int, int), int> glassNumber, int lo, int hi, Result res)
    {
        var model = faces[lo].Model;
        var md = store.Models[model];
        (int, int, int) Q(int k) => (md.Corners[k].X, md.Corners[k].Y, md.Corners[k].Z);
        double[] Pt(int k) { var q = Q(k); return [q.Item1, q.Item2, q.Item3]; }
        int[] Loop(int j)
        {
            var es = new List<(int, int)>();
            foreach (var e in md.Faces[faces[j].Ordinal].Edges) es.Add((md.Edges[e].A, md.Edges[e].B));
            return GpuWorld.LoopCorners(es).ToArray();
        }
        var perWall = new Dictionary<int, List<int>>();
        var windowLoop = new Dictionary<int, int[]>();
        var windowNormal = new Dictionary<int, double[]>();
        for (var w = lo; w <= hi; w++)
        {
            if ((faces[w].Meta & Glass.Bit) == 0 || unsunk[w] || !glassNumber.TryGetValue((model, faces[w].Ordinal), out var number)) continue;
            var wl = Loop(w);
            if (GpuWorld.Plane(wl.Select(Q).ToList()) is not { } n) continue;       // SinkGlass leaves these
            var nd = new double[] { n.Item1, n.Item2, n.Item3 };
            windowLoop[w] = wl; windowNormal[w] = nd;
            var q0 = Q(wl[0]);
            int coplanar = 0, touches = 0, across = 0;
            for (var j = lo; j <= hi; j++)
            {
                if (j == w || (faces[j].Meta & Glass.Bit) != 0 || faces[j].N < 3) continue;
                var jl = Loop(j);
                if (jl.Length < 3 || !jl.All(k => { var q = Q(k); return (double)n.Item1 * (q.Item1 - q0.Item1) + (double)n.Item2 * (q.Item2 - q0.Item2) + (double)n.Item3 * (q.Item3 - q0.Item3) == 0; })) continue;
                coplanar++;
                var (u, v) = Axes(nd);
                var poly = jl.Select(k => (Pt(k)[u], Pt(k)[v])).ToList();
                int inside = 0, outside = 0;
                foreach (var k in wl) { var r = Locate(poly, (Pt(k)[u], Pt(k)[v])); if (r > 0) inside++; else if (r < 0) outside++; }
                if (outside == 0 && inside > 0 && unsunk[j]) res.Blocked.Add((model, number, j - lo));
                else if (outside == 0 && inside > 0) { if (!perWall.TryGetValue(j, out var l)) perWall[j] = l = []; l.Add(w); res.WallOf[w] = j; }
                else if (outside == 0) touches++;                       // every corner on its outline
                else if (inside > 0) across++;
            }
            if (!res.WallOf.ContainsKey(w))
                res.Skipped.Add((model, number, coplanar == 0 ? "no face in its plane" : touches > 0 ? "touches the wall's outline" : across > 0 ? "across two walls" : "in no wall"));
        }
        foreach (var (j, ws) in perWall)
        {
            var (u, v) = Axes(windowNormal[ws[0]]);
            // windows whose boxes overlap: the later one is left out
            var keep = new List<int>();
            foreach (var w in ws)
            {
                var clash = keep.Any(o => Overlap(windowLoop[o], windowLoop[w], Pt, u, v));
                if (clash)
                {
                    res.WallOf.Remove(w);
                    res.Skipped.Add((model, glassNumber[(model, faces[w].Ordinal)], "overlaps another window in its wall"));
                }
                else keep.Add(w);
            }
            var wall = new Wall { Model = model, Face = j, Outer = Loop(j), Windows = keep };
            foreach (var w in keep) wall.Holes.Add(windowLoop[w]);
            var cut = Cut(wall.Outer, wall.Holes, Pt, windowNormal[keep[0]]);
            if (cut == null)
            {
                foreach (var w in keep) { res.WallOf.Remove(w); res.Skipped.Add((model, glassNumber[(model, faces[w].Ordinal)], "ear clipping failed")); }
                continue;
            }
            wall.Tris = cut;
            res.Walls[j] = wall;
        }
    }

    // The two axes the normal is not along (its biggest axis dropped), ascending.
    public static (int, int) Axes(double[] n)
    {
        var ax = 0;
        for (var c = 1; c < 3; c++) if (Math.Abs(n[c]) > Math.Abs(n[ax])) ax = c;
        return (ax == 0 ? 1 : 0, ax == 2 ? 1 : 2);
    }

    static bool Overlap(int[] a, int[] b, Func<int, double[]> pt, int u, int v)
    {
        double[] Box(int[] l) => [l.Min(k => pt(k)[u]), l.Max(k => pt(k)[u]), l.Min(k => pt(k)[v]), l.Max(k => pt(k)[v])];
        var p = Box(a); var q = Box(b);
        return p[0] < q[1] && q[0] < p[1] && p[2] < q[3] && q[2] < p[3];
    }

    /// <summary>1 inside, 0 on the boundary, -1 outside (even-odd).</summary>
    public static int Locate(List<(double X, double Y)> poly, (double X, double Y) p)
    {
        var c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (a, b) = (poly[i], poly[j]);
            var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            if (cross == 0 && p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y)) return 0;
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) c = !c;
        }
        return c ? 1 : -1;
    }

    /// <summary>The 2D turn of a triangle on the plane's two axes (positive: counter-clockwise, as Cut returns them).</summary>
    public static double Turn((int, int, int) t, Func<int, double[]> pt, double[] n)
    {
        var (u, v) = Axes(n);
        double[] a = pt(t.Item1), b = pt(t.Item2), c = pt(t.Item3);
        return (b[u] - a[u]) * (c[v] - a[v]) - (b[v] - a[v]) * (c[u] - a[u]);
    }

    /// <summary>A flat outline with holes as triangles of the outline's and the holes' corners, counter-clockwise on the plane's two
    /// axes: the holes (run the other way round) each bridged to the outline, then ear clipping. Null when no ear is left.</summary>
    public static List<(int, int, int)>? Cut(int[] outer, List<int[]> holes, Func<int, double[]> pt, double[] n)
    {
        var (u, v) = Axes(n);
        double X(int k) => pt(k)[u];
        double Y(int k) => pt(k)[v];
        double Signed(IList<int> l) { double s = 0; for (var i = 0; i < l.Count; i++) { var j = (i + 1) % l.Count; s += X(l[i]) * Y(l[j]) - X(l[j]) * Y(l[i]); } return s; }
        double Cross(int a, int b, int c) => (X(b) - X(a)) * (Y(c) - Y(a)) - (Y(b) - Y(a)) * (X(c) - X(a));
        bool Same(int a, int b) => X(a) == X(b) && Y(a) == Y(b);
        List<(double, double)> Pts(IEnumerable<int> l) => l.Select(k => (X(k), Y(k))).ToList();
        var poly = outer.ToList();
        if (Signed(poly) < 0) poly.Reverse();
        var rest = holes.Select(h => Signed(h) > 0 ? h.Reverse().ToArray() : h.ToArray()).ToList();
        // segment a-b properly crossing c-d (touching at a shared position does not count)
        bool Crosses(int a, int b, int c, int d)
        {
            if (Same(a, c) || Same(a, d) || Same(b, c) || Same(b, d)) return false;
            var d1 = Cross(a, b, c); var d2 = Cross(a, b, d); var d3 = Cross(c, d, a); var d4 = Cross(c, d, b);
            if (d1 != 0 && d2 != 0 && d3 != 0 && d4 != 0) return (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0);
            return (d1 == 0 && OnSeg(a, b, c)) || (d2 == 0 && OnSeg(a, b, d)) || (d3 == 0 && OnSeg(c, d, a)) || (d4 == 0 && OnSeg(c, d, b));   // one end lies on the other segment
        }
        bool OnSeg(int a, int b, int p) => X(p) >= Math.Min(X(a), X(b)) && X(p) <= Math.Max(X(a), X(b)) && Y(p) >= Math.Min(Y(a), Y(b)) && Y(p) <= Math.Max(Y(a), Y(b));
        // a hole with an edge lying on the outline is a notch in it: its other corners join the outline (it runs the other way round, q nearer A)
        for (var h = rest.Count - 1; h >= 0; h--)
        {
            var ring = rest[h];
            var m = ring.Length;
            var notched = false;
            for (var k = 0; k < m && !notched; k++)
            {
                int p = ring[k], q = ring[(k + 1) % m];
                for (var i = 0; i < poly.Count && !notched; i++)
                {
                    int A = poly[i], B = poly[(i + 1) % poly.Count];
                    if (Same(A, B) || Same(p, q) || Cross(A, B, p) != 0 || Cross(A, B, q) != 0 || !OnSeg(A, B, p) || !OnSeg(A, B, q)) continue;
                    if (Math.Pow(X(q) - X(A), 2) + Math.Pow(Y(q) - Y(A), 2) > Math.Pow(X(p) - X(A), 2) + Math.Pow(Y(p) - Y(A), 2)) continue;
                    var ins = new List<int>();
                    if (!Same(q, A)) ins.Add(q);
                    for (var j = 2; j < m; j++) ins.Add(ring[(k + j) % m]);
                    if (!Same(p, B)) ins.Add(p);
                    poly.InsertRange(i + 1, ins);
                    notched = true;
                }
            }
            if (notched) rest.RemoveAt(h);
        }
        while (rest.Count > 0)
        {
            var pairs = new List<(double D, int H, int Hi, int Vi)>();
            for (var h = 0; h < rest.Count; h++)
                for (var hi = 0; hi < rest[h].Length; hi++)
                    for (var vi = 0; vi < poly.Count; vi++)
                        pairs.Add((Math.Pow(X(rest[h][hi]) - X(poly[vi]), 2) + Math.Pow(Y(rest[h][hi]) - Y(poly[vi]), 2), h, hi, vi));
            pairs.Sort((p, q) => p.D.CompareTo(q.D));
            var done = false;
            foreach (var (_, h, hi, vi) in pairs)
            {
                int a = rest[h][hi], b = poly[vi];
                if (Same(a, b))                                                // two holes touching at a corner: a bridge of no length, if the hole's own edges leave into the open
                {
                    var open = true;
                    foreach (var nb in new[] { rest[h][(hi + 1) % rest[h].Length], rest[h][(hi + rest[h].Length - 1) % rest[h].Length] })
                        open &= Locate(Pts(poly), (X(a) + (X(nb) - X(a)) / 4, Y(a) + (Y(nb) - Y(a)) / 4)) > 0;
                    if (!open) continue;
                }
                else
                {
                    var ok = true;
                    for (var k = 0; k < poly.Count && ok; k++) ok = !Crosses(a, b, poly[k], poly[(k + 1) % poly.Count]);
                    foreach (var hole in rest)
                        for (var k = 0; k < hole.Length && ok; k++) ok = !Crosses(a, b, hole[k], hole[(k + 1) % hole.Length]);
                    if (!ok) continue;
                    var mid = ((X(a) + X(b)) / 2, (Y(a) + Y(b)) / 2);
                    if (Locate(Pts(poly), mid) <= 0) continue;
                    if (rest.Any(hole => Locate(Pts(hole), mid) >= 0)) continue;
                }
                var m = rest[h].Length;
                var merged = poly.Take(vi + 1).ToList();
                for (var k = 0; k < m; k++) merged.Add(rest[h][(hi + k) % m]);
                merged.Add(rest[h][hi]);
                merged.Add(poly[vi]);
                merged.AddRange(poly.Skip(vi + 1));
                poly = merged;
                rest.RemoveAt(h);
                done = true;
                break;
            }
            if (!done) return null;
        }
        var tris = new List<(int, int, int)>();
        while (poly.Count > 3)
        {
            var clipped = false;
            for (var i = 0; i < poly.Count && !clipped; i++)
            {
                int a = poly[(i + poly.Count - 1) % poly.Count], b = poly[i], c = poly[(i + 1) % poly.Count];
                if (Cross(a, b, c) <= 0) continue;
                var empty = true;
                foreach (var p in poly)
                {
                    if (Same(p, a) || Same(p, b) || Same(p, c)) continue;
                    if (Cross(a, b, p) >= 0 && Cross(b, c, p) >= 0 && Cross(c, a, p) >= 0) { empty = false; break; }
                }
                if (!empty) continue;
                tris.Add((a, b, c));
                poly.RemoveAt(i);
                clipped = true;
            }
            if (clipped) continue;
            for (var i = 0; i < poly.Count && !clipped; i++)                    // a straight run: the middle corner goes, no triangle
                if (Cross(poly[(i + poly.Count - 1) % poly.Count], poly[i], poly[(i + 1) % poly.Count]) == 0) { poly.RemoveAt(i); clipped = true; }
            if (!clipped) return null;
        }
        if (Cross(poly[0], poly[1], poly[2]) > 0) tris.Add((poly[0], poly[1], poly[2]));
        return tris;
    }
}
