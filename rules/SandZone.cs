namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (THE SAND ZONE, docs/planet-plan.md): the planet's first grown
/// face as Dune II's ground. Every land point of the face is SAND or ROCK; there is no grass. Rock is the high ground and
/// the steep ground - the top tenth of the face's inland heights, and any inland point 24 or more map steps above or below
/// a neighbour - so the outcrops stand where the land already rises. Sand is everything else on land, the shore included.
/// The sea is neither. Rock is where the worm cannot go and takes nothing (Sandworm); sand is where it hunts.
/// The rule reads the face's own map and nothing else, so the same seed gives the same rocks. The picture (sand colours,
/// the card's own faces) is the planet's step 4; the fill on this face is stones only (Stones).</summary>
public sealed class SandZone
{
    /// <summary>The first grown face NEXT TO the played world: face 2, past its east edge. (Face 1, the first by number, is the
    /// cube's bottom - the far side of the planet from the played world on top, with no edge in common; found 2026-11-11 when
    /// the crossing was built. Only the first grown face, as the others aren't done yet.)</summary>
    public const int Face = 2;
    /// <summary>A step between neighbours this high is rock (map height steps).</summary>
    public const int Steep = 24;
    /// <summary>The face's sea level: the worlds' own is $80, which leaves a grown face a quarter land in 50 islands; at $40 the
    /// same seed is ONE island over 87% of the face inside its sea band (measured 2026-11-11), its tallest point 125.</summary>
    public const short SeaLevel = 0x40;

    /// <summary>Per map point, row-major 128 x 128: 0 sea, 1 sand, 2 rock.</summary>
    public readonly byte[] Kind = new byte[Terrain.Size * Terrain.Size];
    public const byte Sea = 0, Sand = 1, Rock = 2;

    public SandZone(Terrain t)
    {
        const int n = Terrain.Size;
        bool Land(int c, int r) => t.Type(c, r) != 0;
        bool Inland(int c, int r) => (t.Type(c, r) & 0xF0) == 0x10;            // the generator's plain land flag: no corner at the sea
        var inland = new List<int>();
        for (var r = 0; r < n; r++) for (var c = 0; c < n; c++) if (Inland(c, r)) inland.Add(t.Height(c, r));
        inland.Sort();
        var high = inland.Count == 0 ? int.MaxValue : inland[inland.Count * 9 / 10];
        for (var r = 0; r < n; r++)
            for (var c = 0; c < n; c++)
            {
                if (!Land(c, r)) continue;
                var rock = false;
                if (Inland(c, r))
                {
                    var h = t.Height(c, r);
                    rock = h >= high && h > 0;
                    foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int c2 = c + dc, r2 = r + dr;
                        if (c2 < 0 || c2 >= n || r2 < 0 || r2 >= n || !Land(c2, r2)) continue;
                        if (Math.Abs(t.Height(c2, r2) - h) >= Steep) rock = true;
                    }
                }
                Kind[r * n + c] = rock ? Rock : Sand;
            }
        // how far each point is from the sea, in map points (steps along rows and columns), for the worm's keep-off
        Array.Fill(SeaSteps, int.MaxValue);
        var queue = new Queue<int>();
        for (var i = 0; i < n * n; i++) if (Kind[i] == Sea) { SeaSteps[i] = 0; queue.Enqueue(i); }
        while (queue.Count > 0)
        {
            var k = queue.Dequeue();
            foreach (var j in new[] { k + 1, k - 1, k + n, k - n })
                if (j >= 0 && j < n * n && Math.Abs(j % n - k % n) <= 1 && SeaSteps[j] == int.MaxValue) { SeaSteps[j] = SeaSteps[k] + 1; queue.Enqueue(j); }
        }
    }

    /// <summary>The worm keeps this many map points off the sea (water is
    /// death to it): sand nearer the shore is no hunting ground, it neither goes there nor takes what stands there.</summary>
    public const int WormKeepsOffSea = 3;
    /// <summary>Per map point: steps to the nearest sea point (int.MaxValue: no sea on the face).</summary>
    public readonly int[] SeaSteps = new int[Terrain.Size * Terrain.Size];

    /// <summary>ONE BIG ISLAND: every piece of land that does not join the biggest piece goes under the sea (height 0, type 0) -
    /// the islets the generator leaves round the main land. Before the ground is built on the map.</summary>
    public static void OneIsland(Terrain t)
    {
        const int n = Terrain.Size;
        var piece = new int[n * n];
        var sizes = new List<int> { 0 };
        for (var i = 0; i < n * n; i++)
        {
            if (piece[i] != 0 || t.Type(i % n, i / n) == 0) continue;
            var id = sizes.Count; var size = 0;
            var stack = new Stack<int>(); stack.Push(i); piece[i] = id;
            while (stack.Count > 0)
            {
                var k = stack.Pop(); size++;
                foreach (var j in new[] { k + 1, k - 1, k + n, k - n })
                    if (j >= 0 && j < n * n && Math.Abs(j % n - k % n) <= 1 && piece[j] == 0 && t.Type(j % n, j / n) != 0) { piece[j] = id; stack.Push(j); }
            }
            sizes.Add(size);
        }
        var main = sizes.IndexOf(sizes.Max());
        for (var i = 0; i < n * n; i++)
            if (piece[i] != 0 && piece[i] != main) { var at = i / n * Terrain.RowBytes + i % n * 2; t.Map[at] = 0; t.Map[at + 1] = 0; }
    }

    /// <summary>The ground at a world position of the face (the original's cell: 512 units a map point).</summary>
    public byte At(int x, int z) => Kind[((z & 0xFFFF) >> 9) * Terrain.Size + ((x & 0xFFFF) >> 9)];
    public bool IsSand(int x, int z) => At(x, z) == Sand;
    /// <summary>Sand the worm goes on and hunts over: sand at least WormKeepsOffSea map points from the sea.</summary>
    public bool IsWormSand(int x, int z) => IsSand(x, z) && SeaSteps[((z & 0xFFFF) >> 9) * Terrain.Size + ((x & 0xFFFF) >> 9)] >= WormKeepsOffSea;
    public bool IsRock(int x, int z) => At(x, z) == Rock;

    /// <summary>The fuller world's fill on the sand face: stones and rocks only - no trees, flowers or herds on sand
    /// (typeTable as Fill.Plan's: a record's kind = rec[0] + typeTable[rec[1]]).</summary>
    public static IEnumerable<byte[]> Stones(IEnumerable<byte[]> records, IReadOnlyList<int> typeTable) =>
        records.Where(rec => rec[0] + typeTable[rec[1]] is Fill.Rock or Fill.Menhir);
}
