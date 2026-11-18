using System;
using System.Collections.Generic;
using System.Linq;

namespace Hunter;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (BETTER EXPLOSIONS step 0, with EXPLOSIONS: PIECES; docs/better-explosions-plan.md):
/// the blasts' star models ($6E-$74, the fire $61-$67 draws - re/formats/weapons.md 3c) get a faceted fireball core in the
/// card's copy: an icosahedron (12 corners, 20 flat faces) round the star's own centre corner, half the spikes'
/// reach across. Its faces carry meta bit 22 (Bit): drawn only with PIECES, when the spikes also burst, burn and fade by
/// the blast's life (gpumodels.gdshaderinc fire_look); ORIGINAL draws the plain star. The rules keep the original models.</summary>
public static class Fireball
{
    /// <summary>Triangle meta bit 22: a fireball core's face.</summary>
    public const int Bit = 0x400000;

    /// <summary>The face origin GpuWorld.Bake reads as a core face (CarInside uses -1 and -(2 + red)).</summary>
    public const int Origin = -1000;

    /// <summary>The blasts' models, $61-$67's (weapons.md 3c).</summary>
    public const int First = 0x6E, Last = 0x74;

    public const int Colour = 7;                                   // the core's paint at rest: the fire's yellow (the shader steps it)

    public static void Make(CardModels store, Dictionary<int, int[]>? origins = null)
    {
        for (var n = First; n <= Last && n < store.Models.Length; n++) Build(store.Models[n], n, origins);
        store.Layout();
    }

    static void Build(CardModels.Model md, int model, Dictionary<int, int[]>? origins)
    {
        int nv = md.Corners.Length, ne = md.Edges.Length, nf = md.Faces.Length;
        if (nv < 2) return;
        var centre = md.Raw(0);                                    // the star's articulated centre corner (flag $14)
        var reach = Enumerable.Range(1, nv - 1).Select(k => { var c = md.Raw(k); return Math.Sqrt(Sq(c.X - centre.X) + Sq(c.Y - centre.Y) + Sq(c.Z - centre.Z)); }).Average();
        var r = Math.Max(10.0, reach / 2);                            // at least 10: smaller, the rounded corners bend its faces (the bake splits them)
        // the icosahedron: (0, +-1, +-p) and its turns, p the golden ratio, scaled to r
        var p = (1 + Math.Sqrt(5)) / 2;
        var unit = new List<(double X, double Y, double Z)>();
        foreach (var a in new[] { -1.0, 1.0 })
            foreach (var b in new[] { -p, p })
            {
                unit.Add((0, a, b)); unit.Add((a, b, 0)); unit.Add((b, 0, a));
            }
        var len = Math.Sqrt(1 + p * p);
        var corners = unit.Select(u => md.RawCorner(0, centre.X + (int)Math.Round(u.X / len * r), centre.Y + (int)Math.Round(u.Y / len * r), centre.Z + (int)Math.Round(u.Z / len * r))).ToList();
        // its 20 faces: every three corners each an edge apart
        var edgeLen = 2 / len;
        bool Near(int i, int j) { var (a, b) = (unit[i], unit[j]); return Math.Abs(Math.Sqrt(Sq(a.X - b.X) + Sq(a.Y - b.Y) + Sq(a.Z - b.Z)) / len - edgeLen) < 1e-6; }
        var edges = new List<CardModels.Edge>();
        var edgeId = new Dictionary<(int, int), int>();
        int Edge(int a, int b)
        {
            var key = (Math.Min(a, b), Math.Max(a, b));
            if (!edgeId.TryGetValue(key, out var id)) { edgeId[key] = id = ne + edges.Count; edges.Add(new(nv + a, nv + b)); }
            return id;
        }
        var faces = new List<CardModels.Face>();
        for (var i = 0; i < 12; i++)
            for (var j = i + 1; j < 12; j++)
                for (var k = j + 1; k < 12; k++)
                    if (Near(i, j) && Near(j, k) && Near(i, k))
                        faces.Add(new(Colour, 0, [Edge(i, j), Edge(j, k), Edge(k, i)]));
        if (faces.Count != 20) throw new InvalidOperationException($"model {model}: the fireball core came out with {faces.Count} faces, not 20");
        md.Replace([.. md.Corners, .. corners], [.. md.Edges, .. edges], [.. md.Faces, .. faces]);
        md.Unmeasured = corners.Count;                                // the star's lift stays the original's: the core is not measured
        if (origins is not null)
            origins[model] = (origins.TryGetValue(model, out var old) ? old : Enumerable.Range(0, nf).ToArray()).Concat(faces.Select(_ => Origin)).ToArray();
    }

    static double Sq(double v) => v * v;
}
