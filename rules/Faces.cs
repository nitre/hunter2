namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (PLANET step 1, docs/planet-plan.md): the cube's net - six
/// faces, each a whole 65536 x 65536 world of 128 x 128 map cells, on the six sides of a cube; the 24 fold crossings
/// between them, each a quarter-turn table worked out from the cube's own geometry (an integer 3D frame a face: its
/// normal N, its east U (local +x) and its south V (local +z), V = N x U on every face, so every crossing is a proper
/// turn - never a mirror). A point of face f at local (x, z) is the cube point N 32768 + U (x - 32768) + V (z - 32768):
/// the face spans the cube from -32768 to 32768 either way, x = 0 and x = 65536 are its folds. Nothing here is state:
/// the tables are fixed data, tested both ways round every fold (PlanetTests).</summary>
public static class Faces
{
    public const int Count = 6;
    /// <summary>A face's width: one world, 65536 units (Terrain.Size x 512).</summary>
    public const int Width = 65536;
    const int Half = Width / 2;

    public readonly record struct V3(int X, int Y, int Z)
    {
        public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator -(V3 a) => new(-a.X, -a.Y, -a.Z);
        public static V3 operator *(V3 a, int k) => new(a.X * k, a.Y * k, a.Z * k);
        public int Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public V3 Cross(V3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    }

    /// <summary>A face's frame on the cube: N its outward normal, U its east, V its south (= N x U).</summary>
    public readonly record struct Frame(V3 N, V3 U, V3 V);

    /// <summary>The six faces' frames (the placement of the three original worlds on them, and their turns so no coast
    /// meets a fold, is step 2's). 0 top (+Y), 1 bottom, 2 +X, 3 -X, 4 +Z, 5 -Z.</summary>
    public static readonly IReadOnlyList<Frame> All = Build();

    static Frame[] Build()
    {
        (V3 N, V3 U)[] nu =
        [
            (new(0, 1, 0), new(1, 0, 0)), (new(0, -1, 0), new(1, 0, 0)),
            (new(1, 0, 0), new(0, 0, -1)), (new(-1, 0, 0), new(0, 0, 1)),
            (new(0, 0, 1), new(1, 0, 0)), (new(0, 0, -1), new(-1, 0, 0)),
        ];
        return nu.Select(f => new Frame(f.N, f.U, f.N.Cross(f.U))).ToArray();
    }

    /// <summary>The face whose normal is direction w.</summary>
    public static int FaceAt(V3 w)
    {
        for (var i = 0; i < Count; i++) if (All[i].N == w) return i;
        throw new ArgumentException("not a face normal", nameof(w));
    }

    /// <summary>The neighbour across face f's edge whose outward direction is w (U east, -U west, V south, -V north).</summary>
    public static int Neighbour(int f, V3 w) => FaceAt(w);

    /// <summary>The cube point of face f's local (x, z); x and z may run past the face (the point is then off the cube's
    /// side, folded by Cross).</summary>
    public static V3 Point(int f, int x, int z)
    {
        var fr = All[f];
        return fr.N * Half + fr.U * (x - Half) + fr.V * (z - Half);
    }

    /// <summary>The heading's quarter turns on a crossing from f to g over f's edge w: the turn k (0..3) such that a
    /// direction (dx, dz) on f, folded onto g, is R^k (dx, dz) with R one heading step of 64 ($2C00: heading 64 moves
    /// +x, so R (dx, dz) = (dz, -dx)). Every crossing is a proper turn: the frames all have V = N x U.</summary>
    public static int Turn(int f, int g, V3 w)
    {
        var a = All[f]; var b = All[g];
        (int Dx, int Dz) Fold(V3 d)                                     // the part along w becomes the part along -N
        {
            var along = d.Dot(w);
            var folded = d - w * along - a.N * along;
            return (folded.Dot(b.U), folded.Dot(b.V));
        }
        var (m00, m10) = Fold(a.U);                                     // the image of (1, 0)
        var (m01, m11) = Fold(a.V);                                     // the image of (0, 1)
        if (m00 * m11 - m01 * m10 != 1) throw new InvalidOperationException("a mirror crossing");
        for (var k = 0; k < 4; k++)
        {
            var (rx, rz) = Rotate(1, 0, k);
            var (sx, sz) = Rotate(0, 1, k);
            if ((rx, rz) == (m00, m10) && (sx, sz) == (m01, m11)) return k;
        }
        throw new InvalidOperationException("not a quarter turn");
    }

    /// <summary>(dx, dz) turned by k heading steps of 64: R (dx, dz) = (dz, -dx).</summary>
    public static (int Dx, int Dz) Rotate(int dx, int dz, int k)
    {
        for (k &= 3; k > 0; k--) (dx, dz) = (dz, -dx);
        return (dx, dz);
    }

    /// <summary>A point of face f that may lie past its folds (x or z outside 0..65535) folded onto the face it is on:
    /// (g, x', z') with both in 0..65536, and the heading turn to add (quarter turns, Rotate's k, summed over the folds
    /// crossed). In range already: (f, x, z, 0). The fold line itself: a coordinate of exactly 65536 on the face asked
    /// about crosses (it is the first point past the map, the lattice's column 128), and on the face reached a
    /// coordinate of 65536 is that same fold line, which stays there (0 and 65536 are both on the cube's edge; one
    /// crossing, never a ping-pong). Past a corner (both out of range) the first fold is x's, then z's from the face
    /// reached - the cube has no point past its corner, so this is a rule, the same for every caller.</summary>
    public static (int Face, int X, int Z, int Turn) Cross(int f, int x, int z)
    {
        var turn = 0;
        foreach (var (from, w) in CrossPath(f, x, z))
        {
            var g = Neighbour(from, w);
            turn += Turn(from, g, w);
            (f, x, z) = Across(from, w, x, z);
        }
        return (f, x, z, turn & 3);
    }

    /// <summary>Cross's folds, in order: each as (the face left, the edge's outward direction) - for UnfoldPath.</summary>
    public static List<(int From, V3 W)> CrossPath(int f, int x, int z)
    {
        var path = new List<(int, V3)>();
        for (var first = true; ; first = false)
        {
            var past = first ? x >= Width || x < 0 || z >= Width || z < 0 : x > Width || x < 0 || z > Width || z < 0;
            if (!past) return path;
            if (path.Count == 8) throw new InvalidOperationException("too far past the folds");
            var w = EdgeOut(f, x, z);
            path.Add((f, w));
            (f, x, z) = Across(f, w, x, z);
        }
    }

    /// <summary>One fold: face f's point (x, z), past (or on) its edge w, as the neighbour's (face, x', z') - exact, so a
    /// lattice point on the fold line lands on the neighbour's lattice point there.</summary>
    public static (int Face, int X, int Z) Across(int f, V3 w, int x, int z)
    {
        var fr = All[f];
        V3 c;
        if (w == fr.U) c = fr.N * (Half - (x - Width)) + fr.U * Half + fr.V * (z - Half);
        else if (w == -fr.U) c = fr.N * (Half + x) - fr.U * Half + fr.V * (z - Half);
        else if (w == fr.V) c = fr.N * (Half - (z - Width)) + fr.U * (x - Half) + fr.V * Half;
        else c = fr.N * (Half + z) + fr.U * (x - Half) - fr.V * Half;
        var g = Neighbour(f, w);
        var to = All[g];
        return (g, c.Dot(to.U) + Half, c.Dot(to.V) + Half);
    }

    /// <summary>The other way: a point (x', z') of face g, a neighbour of f across f's edge w, in f's own frame unfolded
    /// past that fold (x or z outside 0..65535) - what a live thing on g holds while the game runs in f's frame - and the
    /// turn to take off its heading (Cross's k back: subtract it).</summary>
    public static (int X, int Z, int Turn) Unfold(int f, V3 w, int x, int z)
    {
        var g = Neighbour(f, w);
        var a = All[f]; var b = All[g];
        var c = b.N * Half + b.U * (x - Half) + b.V * (z - Half);
        var depth = Half - c.Dot(a.N);                                  // 0 at the fold, 65536 at g's far side
        var ux = c.Dot(a.U) + Half + w.Dot(a.U) * depth;
        var uz = c.Dot(a.V) + Half + w.Dot(a.V) * depth;
        return (ux, uz, Turn(f, g, w));
    }

    /// <summary>A point of the face a CrossPath ends on, unfolded back along the path into the first face's frame, and
    /// the whole turn to take off a heading (the folds' turns summed).</summary>
    public static (int X, int Z, int Turn) UnfoldPath(IReadOnlyList<(int From, V3 W)> path, int x, int z)
    {
        var turn = 0;
        for (var i = path.Count - 1; i >= 0; i--)
        {
            var (f, w) = path[i];
            (x, z, var k) = Unfold(f, w, x, z);
            turn += k;
        }
        return (x, z, turn & 3);
    }

    /// <summary>The edge of f a point past (or on) its folds leaves by (Cross's first fold): its outward direction.</summary>
    public static V3 EdgeOut(int f, int x, int z)
    {
        var fr = All[f];
        return x >= Width ? fr.U : x < 0 ? -fr.U : z >= Width ? fr.V : -fr.V;
    }
}
