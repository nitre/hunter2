using System;
using System.Collections.Generic;
using System.Linq;
using Hunter.Rules;

namespace Hunter;

/// <summary>the card's copy of the objfile with the
/// vehicles' wheels round. The original's wheel rims are 6 or 8 sided; here each rim is 16 corners on the same pivot,
/// axis and plane, its radius the old rim's reach below the pivot (so the wheel's lowest point stays), placed in whole units
/// (not on the model's scale steps: the objfile's format, lifted on the card 2026-11-12), and every face,
/// colour and hub split of the old rim is kept (an old rim edge becomes a run of new ones). The rules keep their own
/// objfile; like GpuWorld.StraightBlades this is the card's picture only.</summary>
public static class RoundWheels
{
    public const int Sides = 16;

    /// <summary>How far a rebuilt wheel of a ProudModels model stands out from the body, whole units, away from the model's
    /// middle (x 0). Those four have their wheels in the plane of a body side; the original paints the wheel after that side
    /// (its hub faces come later in the file), so the whole wheel shows on top. On the card two faces in one plane fought
    /// pixel by pixel; a unit out, the wheel is always in front, as the original draws it. The other wheel models keep their
    /// rims where they were: the car's and the amphimobile's are already clear of the body, the bicycle's are at x 0, and the
    /// scanner van's would be pushed through its leaning side panels, which the original paints over them.</summary>
    public const int Proud = 1;

    /// <summary>The wheel models whose rims stand out by Proud: truck, ambulance, jeep, armoured car.</summary>
    static readonly int[] ProudModels = [18, 20, 34, 36];

    /// <summary>The models whose spin-x parts are wheels (the other spin-x parts are legs): truck, car, ambulance,
    /// scanner van, jeep, armoured car, the bicycle (boarded model $28), amphimobile.</summary>
    static readonly int[] WheelModels = [18, 19, 20, 32, 34, 36, 40, 41];

    /// <summary>The card's models (CardModels) with round wheels: the wheel models rebuilt in place, then laid out again. A model it
    /// cannot rebuild cleanly stays as it was (named in notes).</summary>
    public static void Make(CardModels store, List<string>? notes = null, Dictionary<int, int[]>? origins = null) => Make(store, WheelModels, notes, origins);

    /// <summary>The same for these models only.</summary>
    public static void Make(CardModels store, IEnumerable<int> models, List<string>? notes, Dictionary<int, int[]>? origins)
    {
        foreach (var n in models)
        {
            if (n >= store.Models.Length) continue;
            try { Rebuild(store.Models[n], n, notes, origins); }
            catch (Exception e) when (e is InvalidOperationException or IndexOutOfRangeException)
            {
                notes?.Add($"model {n} left as it was: {e.Message}");
            }
        }
        store.Layout();
    }

    sealed class Ring
    {
        public int X, Py, Pz, R;                                 // the rim's plane, the pivot's y and z, the new radius
        public List<int> Members = [];                           // old corners, round the rim in order of angle
        public int[] J = [];                                     // each member's new corner number in 0..Sides-1
        public int[] New = new int[Sides];                       // the 16 new corners' numbers (set when laid out)
        public int Part;                                         // the pivot corner of its part
    }

    static void Rebuild(CardModels.Model md, int model, List<string>? notes, Dictionary<int, int[]>? origins)
    {
        var nv = md.Corners.Length;
        var corners = new List<int[]>();                         // flag, x, y, z: full units, no 4-unit grid
        for (var k = 0; k < nv; k++) corners.Add([md.Corners[k].Flag, md.Corners[k].X, md.Corners[k].Y, md.Corners[k].Z]);
        var edges = md.Edges.Select(e => (e.A, e.B)).ToList();
        var faces = md.Faces.Select(f => (f.Colour, f.Flags, f.Edges)).ToList();

        // the rims: in a spin-x part (a kind 1, axis 0 pivot) the unflagged corners joined by edges at one x
        var rings = new List<Ring>();
        var ringOf = new int[nv];
        Array.Fill(ringOf, -1);
        for (var k = 0; k < nv; k++)
        {
            var flag = corners[k][0];
            if (flag == 0 || ((flag >> 4) & 7) != 1 || ((flag >> 2) & 3) != 0) continue;
            var members = new List<int>();
            for (var c = k + 1; c < nv && corners[c][0] == 0; c++) members.Add(c);
            var set = members.ToHashSet();
            var parent = members.ToDictionary(c => c, c => c);
            int Find(int c) => parent[c] == c ? c : parent[c] = Find(parent[c]);
            var linked = new HashSet<int>();
            foreach (var (a, c) in edges)
                if (set.Contains(a) && set.Contains(c) && corners[a][1] == corners[c][1]) { parent[Find(a)] = Find(c); linked.Add(a); linked.Add(c); }
            foreach (var g in linked.GroupBy(Find))
            {
                var r = new Ring { Part = k, X = corners[g.First()][1], Py = corners[k][2], Pz = corners[k][3] };
                r.Members = g.OrderBy(c => Math.Atan2(corners[c][3] - r.Pz, corners[c][2] - r.Py)).ToList();
                var low = r.Members.Min(c => corners[c][2]);
                r.R = r.Py - low;
                if (r.Members.Count < 5 || r.R < 4 << md.Shift) throw new InvalidOperationException($"part at corner {k} has a rim of {r.Members.Count} corners, radius {r.R}");
                r.J = r.Members.Select(c => (((int)Math.Round(Math.Atan2(corners[c][3] - r.Pz, corners[c][2] - r.Py) / (2 * Math.PI / Sides)) % Sides) + Sides) % Sides).ToArray();
                for (var i = 0; i < r.J.Length; i++)
                {
                    var step = (r.J[(i + 1) % r.J.Length] - r.J[i] + Sides) % Sides;
                    if (step == 0 || step > Sides / 2 + 1) throw new InvalidOperationException($"rim at corner {r.Members[0]}: corners {r.Members[i]} and {r.Members[(i + 1) % r.J.Length]} meet on the same new corner");
                }
                foreach (var c in r.Members) ringOf[c] = rings.Count;
                rings.Add(r);
            }
        }
        if (rings.Count == 0) throw new InvalidOperationException("no wheel rim found");

        // the new corner list: a rim's 16 stand where its first old corner stood; the rest of its old corners go
        var map = new int[nv];
        var outCorners = new List<CardModels.Corner>();
        var proud = ProudModels.Contains(model) ? Proud : 0;     // 0 leaves the rim in its old plane
        for (var k = 0; k < nv; k++)
        {
            if (ringOf[k] < 0) { map[k] = outCorners.Count; outCorners.Add(md.Corners[k]); continue; }
            var r = rings[ringOf[k]];
            if (r.Members.Min() == k)
                for (var j = 0; j < Sides; j++)
                {
                    var a = j * 2 * Math.PI / Sides;
                    r.New[j] = outCorners.Count;
                    outCorners.Add(md.FullCorner(0, corners[k][1] + Math.Sign(corners[k][1]) * proud,
                        r.Py + (int)Math.Round(r.R * Math.Cos(a), MidpointRounding.AwayFromZero),
                        r.Pz + (int)Math.Round(r.R * Math.Sin(a), MidpointRounding.AwayFromZero)));
                }
        }
        for (var k = 0; k < nv; k++)
            if (ringOf[k] >= 0) { var r = rings[ringOf[k]]; map[k] = r.New[r.J[r.Members.IndexOf(k)]]; }

        // edges: those off the rims keep their order; the rim's own come as they are asked for
        var outEdges = new List<(int A, int B)>();
        var edgeId = new Dictionary<(int, int), int>();
        int Edge(int a, int c)
        {
            var key = (Math.Min(a, c), Math.Max(a, c));
            if (!edgeId.TryGetValue(key, out var id)) { edgeId[key] = id = outEdges.Count; outEdges.Add((a, c)); }
            return id;
        }
        var oldEdge = new int[edges.Count];
        for (var e = 0; e < edges.Count; e++)
        {
            var (a, c) = edges[e];
            int ra = ringOf[a], rc = ringOf[c];
            if (ra < 0 && rc < 0) oldEdge[e] = Edge(map[a], map[c]);
            else if (ra < 0 || rc < 0) throw new InvalidOperationException($"edge {e} joins a rim to the body");
            else oldEdge[e] = -1;
        }

        // A wheel's whole side with two later halves lying on it (the truck: a black disc, then a black half and a brown
        // half that meet along one line across it): the original paints the halves over the disc, so the disc is never seen.
        // On the card all three lie in one plane, and in places the disc came out in front of a half - a dark patch on the
        // half, its edges the disc's own triangles. The disc is left out; the two halves are the wheel's side.
        // Only when the cover is certain: exactly two later faces on that rim, each the rim's steps on its side closed by
        // one line across, the same line for both, their steps together the whole rim and none shared.
        var hidden = new HashSet<int>();
        var flat = new List<(int Face, int Ring, HashSet<int> Steps, List<(int, int)> Across, bool Forward)>();
        for (var f = 0; f < faces.Count; f++)
        {
            var es = faces[f].Edges;
            if (es.Length < 3 || es.All(e => oldEdge[e] >= 0)) continue;
            var loop = new int[es.Length];
            var closed = true;
            for (var i = 0; i < es.Length && closed; i++)
            {
                var (pa, pc) = edges[es[(i + es.Length - 1) % es.Length]];
                var (qa, qc) = edges[es[i]];
                if (pa == qa || pa == qc) loop[i] = pa; else if (pc == qa || pc == qc) loop[i] = pc; else closed = false;
            }
            if (!closed) continue;
            var ring = ringOf[loop[0]];
            if (ring < 0 || loop.Any(c => ringOf[c] != ring)) continue;
            var (members, js) = (rings[ring].Members, rings[ring].J);
            var steps = new HashSet<int>();
            var across = new List<(int, int)>();
            bool? way = null;                                    // the way its rim steps run round the rim
            for (var i = 0; i < loop.Length; i++)
            {
                int s = loop[i], t = loop[(i + 1) % loop.Length], si = members.IndexOf(s), ti = members.IndexOf(t);
                var forward = members[(si + 1) % members.Count] == t;
                if (!forward && members[(si + members.Count - 1) % members.Count] != t) { across.Add((Math.Min(s, t), Math.Max(s, t))); continue; }
                way ??= forward;
                for (var j = js[si]; j != js[ti]; j = (j + (forward ? 1 : Sides - 1)) % Sides) steps.Add(forward ? j : (j + Sides - 1) % Sides);
            }
            flat.Add((f, ring, steps, across, way ?? true));
        }
        foreach (var whole in flat.Where(w => w.Steps.Count == Sides && w.Across.Count == 0))
        {
            var later = flat.Where(o => o.Ring == whole.Ring && o.Face > whole.Face).ToList();
            if (later.Count == 2 && later.All(o => o.Across.Count == 1) && later[0].Across[0] == later[1].Across[0]
                && !later[0].Steps.Overlaps(later[1].Steps) && later[0].Steps.Count + later[1].Steps.Count == Sides)
                hidden.Add(whole.Face);
        }

        // A wheel's whole side (one disc or several of one colour) with later faces lying on part of it (the jeep: two
        // black discs, then a brown piece across the middle): the same fight for the part the later faces cover. The discs
        // are left out and the rest of the side is drawn as pieces of its own beside the later faces - each stretch of rim
        // they leave free, closed by the line across that ends it. Only when that is certain: the later faces share no rim
        // step, and every free stretch ends on a line across that one of them has.
        var beside = new Dictionary<int, List<(int Colour, int Flags, int[] E)>>();   // the last disc's face number -> the pieces in its place
        foreach (var g in flat.GroupBy(o => o.Ring))
        {
            var wholes = g.Where(o => o.Steps.Count == Sides && o.Across.Count == 0).ToList();
            if (wholes.Count == 0 || wholes.Any(o => hidden.Contains(o.Face))) continue;
            var last = wholes.MaxBy(o => o.Face);
            var parts = g.Where(o => !(o.Steps.Count == Sides && o.Across.Count == 0)).ToList();
            if (parts.Count == 0 || parts.Any(o => o.Face < last.Face)) continue;
            if (wholes.Select(o => faces[o.Face].Colour).Distinct().Count() != 1) continue;
            var covered = new HashSet<int>();
            var certain = true;
            foreach (var o in parts) foreach (var s in o.Steps) certain &= covered.Add(s);
            if (!certain || covered.Count == 0 || covered.Count == Sides) continue;
            var r = rings[g.Key];
            var lines = parts.SelectMany(o => o.Across).Select(a =>
            {
                int ja = r.J[r.Members.IndexOf(a.Item1)], jb = r.J[r.Members.IndexOf(a.Item2)];
                return (Math.Min(ja, jb), Math.Max(ja, jb));
            }).ToHashSet();
            var pieces = new List<(int Colour, int Flags, int[] E)>();
            for (var j = 0; j < Sides && certain; j++)
            {
                if (covered.Contains(j) || !covered.Contains((j + Sides - 1) % Sides)) continue;   // a free stretch starts at step j
                var end = j;
                while (!covered.Contains(end % Sides)) end++;                                       // its steps j .. end - 1, its corners j .. end
                int a = j, b = end % Sides;
                if (end - j > 14 || !lines.Contains((Math.Min(a, b), Math.Max(a, b)))) { certain = false; break; }
                var es = new List<int>();
                if (last.Forward)
                {
                    for (var k = j; k < end; k++) es.Add(Edge(r.New[k % Sides], r.New[(k + 1) % Sides]));
                    es.Add(Edge(r.New[b], r.New[a]));
                }
                else
                {
                    for (var k = end; k > j; k--) es.Add(Edge(r.New[k % Sides], r.New[(k - 1) % Sides]));
                    es.Add(Edge(r.New[a], r.New[b]));
                }
                pieces.Add((faces[last.Face].Colour, faces[last.Face].Flags, es.ToArray()));
            }
            if (!certain || pieces.Count == 0) continue;
            foreach (var o in wholes) hidden.Add(o.Face);
            beside[last.Face] = pieces;
        }

        // the faces
        var outFaces = new List<(int Colour, int Flags, int[] E)>();
        var origin = new List<int>();                            // each new face's number among the old ones (a band of treads is many)
        for (var oldFace = 0; oldFace < faces.Count; oldFace++)
        {
            while (origin.Count < outFaces.Count) origin.Add(oldFace - 1);
            var (colour, flags, es) = faces[oldFace];
            if (hidden.Contains(oldFace))
            {
                if (beside.TryGetValue(oldFace, out var pieces)) outFaces.AddRange(pieces);
                continue;
            }
            if (es.All(e => oldEdge[e] >= 0)) { outFaces.Add((colour, flags, es.Select(e => oldEdge[e]).ToArray())); continue; }
            var n = es.Length;
            var loop = new int[n];                               // the corners, one an edge: edge i runs corner i to corner i + 1
            for (var i = 0; i < n; i++)
            {
                var (pa, pc) = edges[es[(i + n - 1) % n]];
                var (qa, qc) = edges[es[i]];
                loop[i] = pa == qa || pa == qc ? pa : pc == qa || pc == qc ? pc : throw new InvalidOperationException("a rim face is not a closed chain");
            }
            // one rim edge from corner s to corner t: the run of new rim edges between their new corners
            List<(int Ring, int From, int To)> Run(int s, int t)
            {
                var r = ringOf[s];
                var i = rings[r].Members.IndexOf(s);
                var m = rings[r].Members.Count;
                var j0 = rings[r].J[i];
                var forward = rings[r].Members[(i + 1) % m] == t;
                var jt = rings[r].J[rings[r].Members.IndexOf(t)];
                var run = new List<(int, int, int)>();
                for (var j = j0; j != jt; j = (j + (forward ? 1 : Sides - 1)) % Sides)
                    run.Add((r, j, (j + (forward ? 1 : Sides - 1)) % Sides));
                return run;
            }
            bool Adjacent(int s, int t)
            {
                var r = ringOf[s];
                var i = rings[r].Members.IndexOf(s);
                var m = rings[r].Members.Count;
                return ringOf[t] == r && (rings[r].Members[(i + 1) % m] == t || rings[r].Members[(i + m - 1) % m] == t);
            }
            var kinds = Enumerable.Range(0, n).Select(i => ringOf[loop[i]] != ringOf[loop[(i + 1) % n]] ? 'x' : Adjacent(loop[i], loop[(i + 1) % n]) ? 'r' : 'c').ToArray();
            if (!kinds.Contains('x'))
            {
                var list = new List<int>();
                for (var i = 0; i < n; i++)
                {
                    int s = loop[i], t = loop[(i + 1) % n];
                    if (kinds[i] == 'c') list.Add(Edge(map[s], map[t]));
                    else foreach (var (r, j, j2) in Run(s, t)) list.Add(Edge(rings[r].New[j], rings[r].New[j2]));
                }
                if (list.Count > 15 && list.Count == Sides && kinds.All(k => k == 'r')) list.RemoveAt(list.Count - 1);   // the whole rim: an open chain of 15 (the card closes it)
                if (list.Count > 15) throw new InvalidOperationException($"a rim face of {list.Count} edges");
                outFaces.Add((colour, flags, list.ToArray()));
            }
            else if (n == 4 && kinds[0] == 'r' && kinds[1] == 'x' && kinds[2] == 'r' && kinds[3] == 'x')
            {
                // a band between two rims (the tread): one quad a new rim step
                var run = Run(loop[0], loop[1]);
                var ra = ringOf[loop[0]];
                var rb = ringOf[loop[2]];
                var back = Run(loop[2], loop[3]);
                for (var q = 0; q < run.Count; q++)
                {
                    var (_, j, j2) = run[q];
                    var (_, k, k2) = back[run.Count - 1 - q];
                    if (rings[rb].J[rings[rb].Members.IndexOf(loop[2])] != rings[ra].J[rings[ra].Members.IndexOf(loop[1])] && q == 0) throw new InvalidOperationException("a tread edge does not meet its rim's other side");
                    outFaces.Add((colour, flags, [Edge(rings[ra].New[j], rings[ra].New[j2]), Edge(rings[ra].New[j2], rings[rb].New[k]),
                                                  Edge(rings[rb].New[k], rings[rb].New[k2]), Edge(rings[rb].New[k2], rings[ra].New[j])]));
                }
            }
            else throw new InvalidOperationException("a face crosses between rims in a way not handled");
        }
        while (origin.Count < outFaces.Count) origin.Add(faces.Count - 1);

        // swap the model's lists in
        var head = md.Head.ToArray();
        for (var s = 0; s < 4; s++)                              // the special-corner selector: 1-based corner numbers
        {
            var v = head[0x0C + s];
            if (v == 0) continue;
            if (ringOf[v - 1] >= 0) throw new InvalidOperationException("the special corner is on a rim");
            if (map[v - 1] + 1 > 255) throw new InvalidOperationException("the special corner's number does not fit its header byte");
            head[0x0C + s] = (byte)(map[v - 1] + 1);
        }
        md.Replace(outCorners.ToArray(), outEdges.Select(e => new CardModels.Edge(e.A, e.B)).ToArray(),
            outFaces.Select(f => new CardModels.Face(f.Colour, f.Flags, f.E)).ToArray(), head);
        if (origins is not null) origins[model] = origin.ToArray();
        notes?.Add($"model {model}: {nv} -> {outCorners.Count} corners, {edges.Count} -> {outEdges.Count} edges, {faces.Count} -> {outFaces.Count} faces, {rings.Count} rims");
    }
}
