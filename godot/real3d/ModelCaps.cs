using System;
using System.Collections.Generic;
using System.Linq;
using Hunter.Rules;

namespace Hunter;

/// <summary>Back from the retired processor-built 3D view. The original's
/// camera only ever looked north, so many models are open somewhere (every building: no back wall; ~80 models:
/// no bottom). Each open side (north, south, east, west) whose opposite side is built gets that half's filled,
/// non-detail faces (on plain, non-articulated corners) mirrored across the model's middle, in their own
/// colours; an open bottom gets a floor round the model's lowest corners, coloured as its lowest face. The new
/// corners are extra points in the model's own unturned shape; GpuWorld.Bake turns them into triangles the
/// card draws like the model's own (gpumodels.gdshaderinc entity_extra_rot). Only plain corners are mirrored, so
/// wheels (moving parts) are left out here; the card's round wheels are RoundWheels. Built once per model.</summary>
public static class ModelCaps
{
    /// <summary>Points: the extra corners (x, y, z, model units, already shifted); Faces: each a loop of those
    /// extra corners' numbers (0-based) and its colour.</summary>
    public sealed record Back(short[] Points, (int[] Loop, int Colour)[] Faces);

    static readonly Dictionary<(CardModels.Model, bool), Back?> Cache = new();

    /// <summary>Hand-made backs, by model number;
    /// they replace the automatic ones. Model units, x east, y up, z north; the front is the south (low z) side.</summary>
    static readonly Dictionary<int, Back> HandMade = new()
    {
        // $9E the bungalow: front wall z -160 (door, window), hip roof complete; missing: the back wall (corners
        // 2 3 4 7), the back eave (7 4 10 13). Back wall colour 4 and eave 10 as the front's; a window like the
        // front's (6, sill 1, curtain 0), mirrored so it sits on the same side seen from behind, just off the wall.
        [0x9E] = new Back(
        [
            -200, -132, 160,   200, -132, 160,   200, 52, 160,   -200, 52, 160,   164, 104, 112,   -164, 104, 112,  // 0-5 wall, eave
            160, -80, 161,   160, 28, 161,   -4, 28, 161,   -4, -80, 161,                                        // 6-9 window
            160, -64, 162,   -4, -64, 162,   160, -80, 162,   -4, -80, 162,                                      // 10-13 sill
            108, -64, 162,   88, 28, 162,   68, 28, 162,   44, -64, 162,                                         // 14-17 curtain
        ],
        [
            ([0, 1, 2, 3], 4), ([3, 2, 4, 5], 10),
            ([6, 7, 8, 9], 6), ([12, 13, 11, 10], 1), ([14, 15, 16, 17], 0),
        ]),
        // $99 the shed: sides and roof complete; missing: the back wall (0 1 5 4) and the back gable (4 5 8),
        // wall 10 and gable 2 as the front's; no door or window.
        [0x99] = new Back(
        [
            -140, -100, 280,   140, -100, 280,   140, 40, 280,   -140, 40, 280,   0, 112, 228,
        ],
        [
            ([0, 1, 2, 3], 10), ([3, 2, 4], 2),
        ]),
        // $9F the coms hut: flat roof, sides and dish complete; missing: the back wall (corners 3 1 7 4) and the
        // strip under the roof (4 7 8 11). Wall 4 and strip 10 as the front's; no window (the front has none).
        [0x9F] = new Back(
        [
            -240, -100, 120,   240, -100, 120,   240, 100, 120,   -240, 100, 120,   240, 120, 120,   -240, 120, 120,
        ],
        [
            ([0, 1, 2, 3], 4), ([3, 2, 4, 5], 10),
        ]),
        // $A2 the small hut: front, sides and lower roof all round complete; missing: the back wall (2 3 4 7) and
        // the back top roof triangle (11 8 12), coloured 10 as the back lower roof under it.
        [0xA2] = new Back(
        [
            -100, -80, 100,   100, -80, 100,   100, 46, 100,   -100, 46, 100,   -60, 78, 60,   60, 78, 60,   0, 156, 0,
        ],
        [
            ([0, 1, 2, 3], 4), ([4, 5, 6], 10),
        ]),
    };

    /// <summary>Does this model number have a hand-made back (for the bake's count)?</summary>
    public static bool IsHandMade(int model) => HandMade.ContainsKey(model);

    /// <summary>Which models get careful backs (only side mirrors and a floor, the front waits for hand-made): the
    /// models of the vehicle, animal and person types (below $30; their main model is byte +$5 of the kind record
    /// $D45E + type * $34, re/formats/vehicles.md 1), plus the people's walk and swim models 1, 2, $E, $F
    /// ($A72C FootModels' own constants).</summary>
    public static bool[] Careful(byte[]? main, int count)
    {
        var careful = new bool[count];
        if (main != null)
            for (var t = 0; t < 0x30; t++)
            {
                var cm = main[0xD45E - 0x800 + t * 0x34 + 5];
                if (cm < careful.Length) careful[cm] = true;
            }
        foreach (var cm in new[] { 1, 2, 0xE, 0xF })
            if (cm < careful.Length) careful[cm] = true;
        return careful;
    }

    /// <summary>careful (vehicles, animals, people: types below $30, re/formats/vehicles.md 1): not the same front and
    /// back, so only left / right mirrors and the bottom - their open ends wait for hand-made backs.</summary>
    public static Back? For(CardModels.Model md, bool careful = false, int model = -1)
    {
        lock (Cache)
        {
            if (HandMade.TryGetValue(model, out var hand)) return hand;
            if (Cache.TryGetValue((md, careful), out var c)) return c;
            return Cache[(md, careful)] = Build(md, careful);
        }
    }

    sealed record Scan((int X, int Y, int Z, bool Plain)[] P, int[] Lo, int[] Hi,
        List<(int[] Loop, int Colour, bool Detail, int Axis, double[] Centre)> All);

    /// <summary>The model's corners and its filled faces as outlines; null if it has none.</summary>
    static Scan? Read(CardModels.Model md)
    {
        var count = md.Corners.Length;
        if (count < 3) return null;
        var p = new (int X, int Y, int Z, bool Plain)[count];
        var rule = 0;                                                     // the walk's rule (model.gdshaderinc _walk_step): a flagged corner
        for (var i = 0; i < count; i++)                                   // sets it for the unflagged ones after it - 1 turn, 2 follow
        {
            var c = md.Corners[i];
            if (c.Flag != 0) rule = c.Flag >> 4 & 7;                      // (the radar unit's dish: corner 0 flagged, 1-21 turn with it)
            p[i] = (c.X, c.Y, c.Z, rule is not (1 or 2));
        }
        int[] lo = [p.Min(q => q.X), p.Min(q => q.Y), p.Min(q => q.Z)], hi = [p.Max(q => q.X), p.Max(q => q.Y), p.Max(q => q.Z)];
        int C(int i, int k) => k == 0 ? p[i].X : k == 1 ? p[i].Y : p[i].Z;
        var loop = new List<int>();
        var all = new List<(int[] Loop, int Colour, bool Detail, int Axis, double[] Centre)>();
        foreach (var face in md.Faces)
        {
            int colour = face.Colour & 0x0F, flags = face.Flags, n = face.Edges.Length, kind = flags & 0x70;
            if ((kind != 0 && kind != 0x40) || n == 0) continue;
            OutlineOf(md.Edges, face.Edges, loop);
            if (loop.Count < 3 || loop.Any(i => i >= count)) continue;
            double nx = 0, ny = 0, nz = 0;                            // Newell's normal
            for (var i = 0; i < loop.Count; i++)
            {
                var (x1, y1, z1, _) = p[loop[i]];
                var (x2, y2, z2, _) = p[loop[(i + 1) % loop.Count]];
                nx += (y1 - y2) * (z1 + z2); ny += (z1 - z2) * (x1 + x2); nz += (x1 - x2) * (y1 + y2);
            }
            double ax = Math.Abs(nx), ay = Math.Abs(ny), az = Math.Abs(nz);
            var axis = az >= Math.Max(ax, ay) ? 2 : ax >= ay ? 0 : 1;   // the axis the face looks along
            var centre = new double[3];
            for (var k = 0; k < 3; k++) centre[k] = loop.Average(i => C(i, k));
            all.Add((loop.ToArray(), colour, (flags & 0x80) != 0, axis, centre));
        }
        return all.Count == 0 ? null : new Scan(p, lo, hi, all);
    }

    /// <summary>A face's corners in order round it, as rules/Outline.Of walks them, on the model's edges (full numbers, no byte limit):
    /// from edge 0 the next unused edge that shares the end corner, a repeated edge never twice; true when the outline closed.</summary>
    static bool OutlineOf(CardModels.Edge[] model, int[] edges, List<int> loop)
    {
        loop.Clear();
        if (edges.Length == 0) return false;
        var pa = new int[edges.Length];
        var pb = new int[edges.Length];
        for (var k = 0; k < edges.Length; k++) { pa[k] = model[edges[k]].A; pb[k] = model[edges[k]].B; }
        var used = new bool[edges.Length];
        used[0] = true;
        loop.Add(pa[0]);
        var end = pb[0];
        while (end != loop[0])
        {
            var found = -1;
            for (var k = 0; k < edges.Length && found < 0; k++)
            {
                if (used[k] || (pa[k] != end && pb[k] != end)) continue;
                var dup = false;                             // the same two corners as an edge already walked
                for (var j = 0; j < edges.Length && !dup; j++)
                    dup = used[j] && Math.Min(pa[j], pb[j]) == Math.Min(pa[k], pb[k]) && Math.Max(pa[j], pb[j]) == Math.Max(pa[k], pb[k]);
                if (dup) { used[k] = true; continue; }
                found = k;
            }
            if (found < 0) break;
            used[found] = true;
            loop.Add(end);
            end = pa[found] == end ? pb[found] : pa[found];
        }
        if (end == loop[0]) return true;
        loop.Add(end);                                      // open: grow the start end too (edge 0 may sit mid-chain)
        for (var grew = true; grew;)
        {
            grew = false;
            for (var k = 0; k < edges.Length && !grew; k++)
                if (!used[k] && (pa[k] == loop[0] || pb[k] == loop[0]))
                {
                    used[k] = grew = true;
                    loop.Insert(0, pa[k] == loop[0] ? pb[k] : pa[k]);
                }
        }
        return false;
    }

    static Back? Build(CardModels.Model md, bool careful)
    {
        if (Read(md) is not { } sc) return null;
        var (p, lo, hi, all) = (sc.P, sc.Lo, sc.Hi, sc.All);
        var count = p.Length;
        int C(int i, int k) => k == 0 ? p[i].X : k == 1 ? p[i].Y : p[i].Z;
        // where along axis k a face sits: 1 = the high edge (north, east, top), -1 = the low edge, 0 = between
        int Edge(double[] centre, int k) => hi[k] - lo[k] < 2 ? 0 : ((centre[k] - lo[k]) / (hi[k] - lo[k])) switch { > 0.75 => 1, < 0.25 => -1, _ => 0 };
        bool Wall(int k, int side) => all.Any(f => f.Axis == k && Edge(f.Centre, k) == side);

        var points = new List<short>();
        var index = new Dictionary<(int, int, int), int>();
        int Extra(int x, int y, int z)
        {
            if (!index.TryGetValue((x, y, z), out var k))
            {
                index[(x, y, z)] = k = points.Count / 3;
                points.Add((short)x); points.Add((short)y); points.Add((short)z);
            }
            return k;
        }
        var faces = new List<(int[] Loop, int Colour)>();
        // an open side (north, south, east, west) whose opposite side is built: that half's faces mirrored across
        foreach (var (k, side) in new[] { (2, 1), (2, -1), (0, 1), (0, -1) })
        {
            if (careful && k == 2) continue;                          // front / back: hand-made only
            if (hi[k] - lo[k] < 2 || Wall(k, side) || !Wall(k, -side)) continue;
            var mid = (lo[k] + hi[k]) / 2.0;
            foreach (var f in all)
            {
                if (f.Detail || !f.Loop.All(i => p[i].Plain && (side > 0 ? C(i, k) <= mid : C(i, k) >= mid))) continue;
                faces.Add((f.Loop.Select(i => k == 0 ? Extra(lo[0] + hi[0] - p[i].X, p[i].Y, p[i].Z)
                                                     : Extra(p[i].X, p[i].Y, lo[2] + hi[2] - p[i].Z)).Reverse().ToArray(), f.Colour));
            }
        }
        // an open bottom on a model with height: a floor round its lowest corners, coloured as its lowest face. Only corners
        // of filled faces count: a base drawn in lines alone (the tracer gun's frame, model 106) is open in the original,
        // and a floor there drew a solid dark square under it
        if (hi[1] - lo[1] >= 2 && !Wall(1, -1))
        {
            var filled = all.SelectMany(f => f.Loop).ToHashSet();
            var low = Enumerable.Range(0, count).Where(i => filled.Contains(i) && p[i].Y <= lo[1] + 1).ToList();
            var hull = Hull(low, i => (p[i].X, p[i].Z));
            if (hull.Count >= 3)
            {
                var colour = all.Where(f => !f.Detail).MinBy(f => f.Centre[1]).Colour;
                faces.Add((hull.Select(i => Extra(p[i].X, p[i].Y, p[i].Z)).ToArray(), colour));
            }
        }
        return faces.Count == 0 ? null : new Back(points.ToArray(), faces.ToArray());
    }

    // Andrew's monotone chain on (u, v); duplicate spots keep one corner
    static List<int> Hull(List<int> ids, Func<int, (int U, int V)> at)
    {
        var pts = ids.GroupBy(at).Select(g => g.First()).OrderBy(i => at(i).U).ThenBy(i => at(i).V).ToList();
        if (pts.Count < 3) return pts;
        long Cross(int o, int u, int v)
        {
            var (ou, ov) = at(o); var (uu, uv) = at(u); var (vu, vv) = at(v);
            return (long)(uu - ou) * (vv - ov) - (long)(uv - ov) * (vu - ou);
        }
        var h = new List<int>();
        for (var pass = 0; pass < 2; pass++)
        {
            var start = h.Count;
            foreach (var i in pass == 0 ? pts : Enumerable.Reverse(pts))
            {
                while (h.Count >= start + 2 && Cross(h[^2], h[^1], i) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(i);
            }
            h.RemoveAt(h.Count - 1);
        }
        return h;
    }
}
