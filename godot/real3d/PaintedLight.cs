using System;
using System.Collections.Generic;
using System.Linq;

namespace Hunter;

/// <summary>(SUN SHADING): light painted into the models. About 70 model
/// colour groups paint each wall direction its own shade of one paint, lit from the south-east (the east wall the lighter
/// shade 28 times, the west 8; faces judged outward from the model's middle, as the sun shader does). The sun picks a
/// face's shade by its angle starting from the painted shade, so those walls were lit twice. Found from the data alone:
/// a model's upright faces of one paint family, sorted by the way they face; painted light = every direction one shade,
/// two or more directions and shades. Two shades on walls facing the same way is two-tone paint, left alone. Such a face
/// is marked (GpuWorld.Bake meta bit 10) and the sun lights it by how much more or less light it gets than from the
/// painters' sun (gpumodels.gdshader painter_dir) - so with the sun where they painted it, it is the original's shade.</summary>
public static class PaintedLight
{
    /// <summary>SunShade's families with more than one shade.</summary>
    static readonly int[][] Families = [[1, 2, 3, 4], [6, 5], [9, 10, 11], [12, 13, 14]];

    /// <summary>Whether this face's shade has the painters' light in it (face: the face's number in the model).</summary>
    public static bool Painted(CardModels.Model md, int face)
    {
        var groups = Of(md);
        return groups.Contains(Family(md.Faces[face].Colour & 15)) && Direction(md, face) is not null;
    }

    /// <summary>Whether this paint has the painters' light in it on this model (for its copied back walls, ModelCaps).</summary>
    public static bool PaintedColour(CardModels.Model md, int colour) => Of(md).Contains(Family(colour & 15));

    static readonly Dictionary<CardModels.Model, HashSet<int>> Cache = new();

    /// <summary>A new card build: the last build's models go (kept, the startup line counted both builds - 148 groups in 128
    /// models after the demo's title loop built the world again, 74 in 64 each time - and held the old models).</summary>
    public static void Forget() => Cache.Clear();

    /// <summary>The startup line's counts: colour groups with light painted in, and the models holding them.</summary>
    public static int Groups => Cache.Values.Sum(g => g.Count);
    public static int ModelsWith => Cache.Values.Count(g => g.Count > 0);

    /// <summary>A model's paint families with light painted into their walls.</summary>
    static HashSet<int> Of(CardModels.Model md)
    {
        if (Cache.TryGetValue(md, out var known)) return known;
        var by = new Dictionary<(int Fam, char Dir), HashSet<int>>();
        for (var f = 0; f < md.Faces.Length; f++)
        {
            int n = md.Faces[f].Edges.Length, kind = md.Faces[f].Flags & 0x70, colour = md.Faces[f].Colour & 15;
            if ((kind == 0 || kind == 0x40) && n >= 3 && Family(colour) >= 0 && Direction(md, f) is { } d)
            {
                if (!by.TryGetValue((Family(colour), d), out var set)) by[(Family(colour), d)] = set = [];
                set.Add(colour);
            }
        }
        var painted = new HashSet<int>();
        foreach (var fam in by.Keys.Select(k => k.Fam).Distinct())
        {
            var dirs = by.Where(kv => kv.Key.Fam == fam).Select(kv => kv.Value).ToList();
            if (dirs.Count >= 2 && dirs.SelectMany(s => s).Distinct().Count() >= 2 && dirs.All(s => s.Count == 1))
                painted.Add(fam);                                    // one shade a direction, shades differ by direction
        }
        return Cache[md] = painted;
    }

    static int Family(int colour)
    {
        for (var f = 0; f < Families.Length; f++) if (Array.IndexOf(Families[f], colour) >= 0) return f;
        return -1;
    }

    /// <summary>An upright face's direction at rest - E, W, N or S, outward from the model's middle - or null (tilted,
    /// too few corners, or on a corner that starts an articulated part).</summary>
    static char? Direction(CardModels.Model md, int face)
    {
        var count = md.Corners.Length;
        var vs = new List<int>();
        foreach (var e in md.Faces[face].Edges)
        {
            if (e >= md.Edges.Length) return null;
            foreach (var v in new[] { md.Edges[e].A, md.Edges[e].B })
                if (!vs.Contains(v)) vs.Add(v);
        }
        if (vs.Count < 3 || vs.Any(v => v >= count || md.Corners[v].Flag != 0)) return null;
        (double X, double Y, double Z) P(int v) => (md.Corners[v].X, md.Corners[v].Y, md.Corners[v].Z);
        var (a, b, c) = (P(vs[0]), P(vs[1]), P(vs[2]));
        double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, wx = c.X - a.X, wy = c.Y - a.Y, wz = c.Z - a.Z;
        double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
        double cx = 0, cz = 0, mx = 0, mz = 0;
        for (var k = 0; k < count; k++) { cx += P(k).X; cz += P(k).Z; }
        foreach (var v in vs) { mx += P(v).X; mz += P(v).Z; }
        if (nx * (mx / vs.Count - cx / count) + nz * (mz / vs.Count - cz / count) < 0) (nx, ny, nz) = (-nx, -ny, -nz);   // outward
        if (Math.Abs(ny) > 0.3 * (Math.Abs(nx) + Math.Abs(nz) + 1e-9)) return null;                 // not upright
        return Math.Abs(nx) > Math.Abs(nz) ? (nx > 0 ? 'E' : 'W') : (nz > 0 ? 'N' : 'S');
    }
}
