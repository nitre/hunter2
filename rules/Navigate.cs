namespace Hunter.Rules;

/// <summary>(the BOT step 2, docs/bot-plan.md): a way on foot across the map's 128 x 128 squares
/// (512 units each, Terrain.Type 0 = sea), round the sea and up the gentler slopes. A* over the squares, 8 ways, the map's
/// wrap at 16 bits; a step costs its length plus Climb for each unit of height between the squares' middles
/// (Ground.Height); he walks up anything, as the game lets him. Blocked squares (type byte bit 7, Settle $2B14) are never
/// entered. The way comes back as points, cut short wherever a straight line stays on land (no zig-zag along the grid).
/// Reads the world; changes nothing.</summary>
public static class Navigate
{
    public const int Square = 512, Size = 128;
    public const double Climb = 4;        // a unit of height costs four units of walking

    static int Col(int x) => ((ushort)x >> 9) & (Size - 1);
    static int Middle(int c) => c * Square + Square / 2;

    /// <summary>Land under (x, z): not the sea's squares.</summary>
    public static bool Land(WorldState s, int x, int z) => s.Terrain.Type(Col(x), Col(z)) != 0;

    /// <summary>A square that cannot be entered: its type byte's bit 7 ($36AA's N flag, Settle $2B14).</summary>
    static bool Blocked(WorldState s, int c, int r) => (s.Terrain.Type(c, r) & 0x80) != 0;

    /// <summary>Cells a side in a square for Joined: the near sight's cell (NearSight.Cell).</summary>
    const int Sub = Square / 32;
    sealed class Fine { public readonly sbyte[] Full = new sbyte[Size * Size]; public readonly bool[]?[] Cells = new bool[]?[Size * Size]; public readonly sbyte[] Join = new sbyte[Size * Size * 8]; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Terrain, Fine> fine = new();

    /// <summary>A square's cells (Sub x Sub, 32 each) a walker (sea: a boat) can stand on by the near sight's rule: the cell's
    /// middle and its patch's corners (Cell / 2 + 4 out) all the kind, none blocked.</summary>
    static bool[] Cells(WorldState s, Fine f, int c, int r, bool sea)
    {
        var i = r * Size + c;
        if (f.Cells[i] is { } done) return done;
        var cells = new bool[Sub * Sub];
        const int m = 32 / 2 + 4;
        for (var j = 0; j < Sub; j++)
            for (var k = 0; k < Sub; k++)
            {
                int x = c * Square + k * 32 + 16, z = r * Square + j * 32 + 16;
                bool Kind(int ox, int oz) { var g = s.Ground.Sample((ushort)(x + ox), (ushort)(z + oz)); return !g.Blocked && g.Land != sea; }
                cells[j * Sub + k] = Kind(0, 0) && Kind(m, m) && Kind(-m, m) && Kind(m, -m) && Kind(-m, -m);
            }
        return f.Cells[i] = cells;
    }

    /// <summary>The square all its kind at the point test (8 x 8 inside it): its cells need no look.</summary>
    static bool Full(WorldState s, Fine f, int c, int r, bool sea)
    {
        var i = r * Size + c;
        if (f.Full[i] == 0)
        {
            var all = true;
            for (var j = 0; j < 8 && all; j++)
                for (var k = 0; k < 8 && all; k++)
                {
                    var g = s.Ground.Sample((ushort)(c * Square + 32 + k * 64), (ushort)(r * Square + 32 + j * 64));
                    all = !g.Blocked && g.Land != sea;
                }
            f.Full[i] = (sbyte)(all ? 1 : -1);
        }
        return f.Full[i] > 0;
    }

    /// <summary>JOINED: can one go from square (c, r) into its neighbour (c + dc, r + dr) keeping to his kind (sea: a boat) -
    /// a run of cells he can stand on (Cells) across their edge, searched inside the two squares (a diagonal: the 2 x 2 block).
    /// The squares' types are not enough (M6 11-09: two land squares meeting in an hourglass, their land touching at a point -
    /// the planner walked him over it, the walker found the sea, four times overdue, 20000 passes). Worked out once a pair.</summary>
    public static bool Joined(WorldState s, int c, int r, int dc, int dr, bool sea)
    {
        var f = fine.GetValue(s.Terrain, _ => new Fine());
        int nc = (c + dc + Size) % Size, nr = (r + dr + Size) % Size, dir = (dr + 1) * 3 + dc + 1;
        var key = (r * Size + c) * 8 + (dir > 4 ? dir - 1 : dir);
        if (f.Join[key] != 0) return f.Join[key] > 0;
        var sq = new List<(int C, int R, int Ox, int Oz)> { (c, r, 0, 0), (nc, nr, dc, dr) };
        if (dc != 0 && dr != 0) { sq.Add(((c + dc + Size) % Size, r, dc, 0)); sq.Add((c, (r + dr + Size) % Size, 0, dr)); }
        bool joined;
        if (Full(s, f, c, r, sea) && Full(s, f, nc, nr, sea) && (sq.Count == 2 || Full(s, f, sq[2].C, sq[2].R, sea) && Full(s, f, sq[3].C, sq[3].R, sea)))
            joined = true;
        else
        {
            const int W = 2 * Sub;                                          // the block, (0, 0) its top left square
            int ox0 = Math.Min(0, dc), oz0 = Math.Min(0, dr);
            var on = new bool[W * W];
            var inBlock = new bool[W * W];
            foreach (var (qc, qr, ox, oz) in sq)
            {
                var cells = Cells(s, f, qc, qr, sea);
                for (var j = 0; j < Sub; j++)
                    for (var k = 0; k < Sub; k++)
                    {
                        int bx = (ox - ox0) * Sub + k, bz = (oz - oz0) * Sub + j;
                        on[bz * W + bx] = cells[j * Sub + k]; inBlock[bz * W + bx] = true;
                    }
            }
            int ax = -ox0 * Sub, az = -oz0 * Sub, gx = (dc - ox0) * Sub, gz = (dr - oz0) * Sub;
            var seen = new bool[W * W];
            var q = new Queue<int>();
            for (var j = 0; j < Sub; j++)
                for (var k = 0; k < Sub; k++)
                    if (on[(az + j) * W + ax + k]) { seen[(az + j) * W + ax + k] = true; q.Enqueue((az + j) * W + ax + k); }
            joined = false;
            while (!joined && q.TryDequeue(out var at))
            {
                int x = at % W, z = at / W;
                if (x >= gx && x < gx + Sub && z >= gz && z < gz + Sub) { joined = true; break; }
                for (var j = -1; j <= 1; j++)
                    for (var k = -1; k <= 1; k++)
                    {
                        int xx = x + k, zz = z + j;
                        if (k == 0 && j == 0 || xx < 0 || zz < 0 || xx >= W || zz >= W) continue;
                        var n = zz * W + xx;
                        if (!inBlock[n] || !on[n] || seen[n]) continue;
                        if (k != 0 && j != 0 && (!on[z * W + xx] || !on[zz * W + x])) continue;   // no corner cut (as NearSight.Way)
                        seen[n] = true; q.Enqueue(n);
                    }
            }
        }
        f.Join[key] = (sbyte)(joined ? 1 : -1);
        return joined;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Terrain, int[]> areas = new();

    /// <summary>Each square's AREA: the squares one can go between without leaving their kind - each island's land, each sea -
    /// by a flood over the 8 ways with no corner cut past the other kind (as Way goes), the map's wrap at 16 bits. Blocked: -1.
    /// Worked out once per map.</summary>
    public static int[] Areas(WorldState s) => areas.GetValue(s.Terrain, _ =>
    {
        var id = new int[Size * Size]; Array.Fill(id, -1); var count = 0;
        bool SeaAt(int c, int r) => s.Terrain.Type(c, r) == 0;
        for (var i = 0; i < Size * Size; i++)
        {
            if (id[i] >= 0 || Blocked(s, i % Size, i / Size)) continue;
            var sea = SeaAt(i % Size, i / Size);
            bool Off(int c, int r) => Blocked(s, c, r) || SeaAt(c, r) != sea;
            var q = new Queue<int>([i]); id[i] = count;
            while (q.TryDequeue(out var at))
            {
                int c = at % Size, r = at / Size;
                for (var dr = -1; dr <= 1; dr++)
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        int nc = (c + dc + Size) % Size, nr = (r + dr + Size) % Size, k = nr * Size + nc;
                        if (id[k] >= 0 || Off(nc, nr) || dc != 0 && dr != 0 && (Off(nc, r) || Off(c, nr)) || !Joined(s, c, r, dc, dr, sea)) continue;
                        id[k] = count; q.Enqueue(k);
                    }
            }
            count++;
        }
        return id;
    });

    /// <summary>An amphibian's straight run: land or sea, no blocked square.</summary>
    static bool StraightBoth(WorldState s, int x, int z, int tx, int tz) => Crossed(x, z, tx, tz, (c, r) => !Blocked(s, c, r));


    /// <summary>The nearest square to (x, z) of the kind asked (sea or land) that has the other kind beside it - the shore -
    /// within `reach` squares, as its middle; null if none. `skip`: middles not to give (ones that did not work).</summary>
    public static (int X, int Z)? Shore(WorldState s, int x, int z, bool sea, int reach = 24, Func<(int X, int Z), bool>? skip = null)
    {
        int c0 = Col(x), r0 = Col(z);
        for (var ring = 0; ring <= reach; ring++)
        {
            (int X, int Z)? best = null; var bd = long.MaxValue;
            for (var dr = -ring; dr <= ring; dr++)
                for (var dc = -ring; dc <= ring; dc++)
                {
                    if (Math.Max(Math.Abs(dc), Math.Abs(dr)) != ring) continue;
                    int c = (c0 + dc + Size) % Size, r = (r0 + dr + Size) % Size;
                    if ((s.Terrain.Type(c, r) == 0) != sea || skip?.Invoke((Middle(c), Middle(r))) == true) continue;
                    var edge = false;
                    for (var k = 0; k < 4 && !edge; k++)
                    {
                        int nc = (c + (k == 0 ? 1 : k == 1 ? -1 : 0) + Size) % Size, nr = (r + (k == 2 ? 1 : k == 3 ? -1 : 0) + Size) % Size;
                        edge = (s.Terrain.Type(nc, nr) == 0) != sea;
                    }
                    if (!edge) continue;
                    var d = (long)dc * dc + (long)dr * dr;
                    if (d < bd) (best, bd) = ((Middle(c), Middle(r)), d);
                }
            if (best is not null) return best;
        }
        return null;
    }

    static int H(WorldState s, int c, int r) => s.Ground.Height((ushort)Middle(c), (ushort)Middle(r));

    /// <summary>A boat's squares: the sea's (Terrain.Type 0) - a boat (+$08 bit 10) is put back off the land (Settle $2B6C).</summary>
    public static bool Sea(WorldState s, int x, int z) => s.Terrain.Type(Col(x), Col(z)) == 0;

    /// <summary>The way from (x, z) to (tx, tz) as points to walk to in turn, the last (tx, tz) itself; null when the sea or
    /// the cliffs cut it off. From the sea (a boat, a swim) the first land square is the start. water: a boat's way instead,
    /// over the sea's squares only, no climbs. both: an amphibian's (+$08 bit 1, WalkSwimSwitch $2BAC) - land and sea, the
    /// climbs counted on the land.</summary>
    public static List<(int X, int Z)>? Way(WorldState s, int x, int z, int tx, int tz, bool water = false, bool both = false, Func<int, int, bool>? avoid = null)
    {
        int sc = Col(x), sr = Col(z), gc = Col(tx), gr = Col(tz);
        bool Shunned(int c, int r) => avoid is not null && (c != sc || r != sr) && avoid(Middle(c), Middle(r));   // `avoid` (a square's middle): kept off, but for where he is
        bool Off(int c, int r) => Blocked(s, c, r) || !both && (s.Terrain.Type(c, r) == 0) != water || Shunned(c, r);
        if (Off(gc, gr)) return null;
        if (sc == gc && sr == gr) return [(tx, tz)];
        var cost = new double[Size * Size];
        Array.Fill(cost, double.MaxValue);
        var from = new int[Size * Size];
        var open = new PriorityQueue<int, double>();
        int Idx(int c, int r) => r * Size + c;
        double Guess(int c, int r)
        {
            int dc = Math.Abs(c - gc), dr = Math.Abs(r - gr);
            dc = Math.Min(dc, Size - dc); dr = Math.Min(dr, Size - dr);
            return Square * (Math.Max(dc, dr) + 0.41421356 * Math.Min(dc, dr));
        }
        var start = Idx(sc, sr);
        cost[start] = 0; from[start] = -1;
        open.Enqueue(start, Guess(sc, sr));
        var goal = Idx(gc, gr);
        while (open.TryDequeue(out var at, out _))
        {
            if (at == goal) break;
            int c = at % Size, r = at / Size, h = H(s, c, r);
            for (var dr = -1; dr <= 1; dr++)
                for (var dc = -1; dc <= 1; dc++)
                {
                    if (dc == 0 && dr == 0) continue;
                    int nc = (c + dc + Size) % Size, nr = (r + dr + Size) % Size;
                    if (Off(nc, nr)) continue;
                    if (dc != 0 && dr != 0 && (Off(nc, r) || Off(c, nr))) continue;   // no corner cut past the sea (a boat: past the land)
                    if (!both && !Joined(s, c, r, dc, dr, water)) continue;    // his kind's cells run across their edge
                    var climb = water || both && (s.Terrain.Type(nc, nr) == 0 || s.Terrain.Type(c, r) == 0) ? 0 : Math.Abs(H(s, nc, nr) - h);
                    var n = Idx(nc, nr);
                    var step = cost[at] + Square * (dc != 0 && dr != 0 ? 1.41421356 : 1) + Climb * climb;
                    if (step >= cost[n]) continue;
                    cost[n] = step; from[n] = at;
                    open.Enqueue(n, step + Guess(nc, nr));
                }
        }
        if (cost[goal] == double.MaxValue) return null;
        var squares = new List<int>();
        for (var at = goal; at != -1; at = from[at]) squares.Add(at);
        squares.Reverse();
        // the squares' middles, then the straight cuts: from each kept point, the farthest later one in a straight line on land
        var pts = squares.Select(i => (X: Middle(i % Size), Z: Middle(i / Size))).ToList();
        pts[^1] = (tx, tz);
        var kept = new List<(int X, int Z)>();
        (int X, int Z) here = (x, z);
        var k = 0;
        while (k < pts.Count)
        {
            var far = k;
            for (var j = pts.Count - 1; j > k; j--)
                if ((both ? StraightBoth(s, here.X, here.Z, pts[j].X, pts[j].Z) : Straight(s, here.X, here.Z, pts[j].X, pts[j].Z, water))
                    && (avoid is null || Crossed(here.X, here.Z, pts[j].X, pts[j].Z, (c, r) => !Shunned(c, r)))) { far = j; break; }
            kept.Add(pts[far]);
            here = pts[far];
            k = far + 1;
        }
        return kept;
    }

    /// <summary>A straight run from (x, z) to (tx, tz) stays on its kind (land, or the sea's squares for a boat) and off
    /// blocked squares: every square the line passes through, a corner clipped too.</summary>
    public static bool Straight(WorldState s, int x, int z, int tx, int tz, bool water = false) =>
        Crossed(x, z, tx, tz, (c, r) => (s.Terrain.Type(c, r) == 0) == water && !Blocked(s, c, r), (c, r, dc, dr) => Joined(s, c, r, dc, dr, water));

    /// <summary>Each square the line from (x, z) to (tx, tz) passes through (a grid walk, the map's wrap), all `ok`.</summary>
    static bool Crossed(int x, int z, int tx, int tz, Func<int, int, bool> ok, Func<int, int, int, int, bool>? step = null)
    {
        double dx = Senses.Diff(tx, x), dz = Senses.Diff(tz, z);
        int c = (ushort)x >> 9, r = (ushort)z >> 9;
        double fx = ((ushort)x & (Square - 1)) / (double)Square, fz = ((ushort)z & (Square - 1)) / (double)Square;
        int sc = Math.Sign(dx), sr = Math.Sign(dz);
        double ux = Math.Abs(dx) / Square, uz = Math.Abs(dz) / Square;    // the line's length in squares, each way
        double nx = ux == 0 ? double.MaxValue : (sc > 0 ? 1 - fx : fx) / ux;   // the line's share at the next column / row edge
        double nz = uz == 0 ? double.MaxValue : (sr > 0 ? 1 - fz : fz) / uz;
        double stepX = ux == 0 ? double.MaxValue : 1 / ux, stepZ = uz == 0 ? double.MaxValue : 1 / uz;
        while (true)
        {
            if (!ok(c & (Size - 1), r & (Size - 1))) return false;
            if (Math.Min(nx, nz) > 1) return true;
            if (nx <= nz) { if (step?.Invoke(c & (Size - 1), r & (Size - 1), sc, 0) == false) return false; c += sc; nx += stepX; }
            else { if (step?.Invoke(c & (Size - 1), r & (Size - 1), 0, sr) == false) return false; r += sr; nz += stepZ; }
        }
    }
}

/// <summary>The bot's near sight: a way across 64 x 64 cells of 32 units round him, the map's squares too coarse for the
/// things on them. A cell is off for good where the coarse map says so (sea on foot, land in a boat) or it is blocked; a
/// climb costs Navigate.Climb a unit; it is dear (Bump) where his box would meet a thing's that the game would stop him on,
/// board him into or take him in by its door ($449E, Collisions.Respond: anything but shots, items, the ones skipped by bits
/// 30 / 21 or a different bit 5, or out of reach in height) - so he goes round buildings, parked vehicles and the rest, and
/// only through one when there is no other way (or he stands in it already). Reads the world; changes nothing.</summary>
public static class NearSight
{
    public const int Cell = 32, N = 64;
    const double Bump = 4000;
    public const int Clearance = 16;             // kept clear of a thing's box: his step drifts a unit or two off the line, and a touch puts him back ($44FC)
    const double Wet = 20;                // a swum cell costs this many walked: energy goes only in the water (Swimming $B05C)
    /// <summary>Switch for tries (MissionTry sets it by reflection): a swimmer's corners and straight lines through the sea.</summary>
    public static bool SwimStraight = true;    // right by its mechanism (M7's bank, controlled from the old run's pass 35700); the whole
                                                // runs it re-times are judged by what fails in them, not by the count

    /// <summary>Would the game stop `me` on `e`, board him or take him in (Collisions.Respond for a pair with `me`)?</summary>
    public static bool InTheWay(Entity me, Entity e)
    {
        if (!e.Alive || e == me || e.Flag(30) || e.Flag(21)) return false;
        int d4 = me.Flags ^ e.Flags;
        if ((d4 >> 5 & 1) != 0 || e.Flag(20) || e.Flag(16)) return false;  // other side of a door; a shot; an item (picked up)
        var dy = (short)(me.W(0x4C) + me.W(0x70)) - (short)(e.W(0x4C) + e.W(0x70));
        return Math.Abs(dy) <= (short)(me.W(0x70) + e.W(0x70));             // a helicopter overhead: no
    }

    /// <summary>The cells' way from him toward (gx, gz) (within the window, else the window's nearest to it), as world points;
    /// `allow` is not in the way (the one he means to board).</summary>
    public static List<(int X, int Z)> Way(WorldState s, Entity me, int gx, int gz, bool water, bool both, Entity? allow = null)
    {
        int mx = me.UW(0x4A), mz = me.UW(0x4E), half = N / 2;
        var cost = new double[N * N];
        var off = new bool[N * N];
        var wall = new bool[N * N];                                          // what a corner may not be cut past: off, but for a swimmer
        var afoot = me.Bit(0x08, 15);                                        // on foot: he can swim (the sea's cells are his, at a price)
        var swimming = SwimStraight && me.Bit(0x08, 15) && me.Bit(0x08, 10); // only the blocked squares - the sea is his (M7 11-09: no diagonal
                                                                             // in the sea, a north / east staircase, 30 in 80 passes, drowned)
        int Wx(int i) => (ushort)(mx + (i - half) * Cell);
        int Wz(int j) => (ushort)(mz + (j - half) * Cell);
        var h = new int[N * N];
        for (var j = 0; j < N; j++)
            for (var i = 0; i < N; i++)
            {
                int x = Wx(i), z = Wz(j);
                var (hh, land, blocked) = s.Ground.Sample((ushort)x, (ushort)z);
                bool Wrong(int ox, int oz) => s.Ground.Sample((ushort)(x + ox), (ushort)(z + oz)).Land == water;
                const int m = Cell / 2 + 4;                                      // the shore runs across the squares' triangles: the cell's whole patch, a margin
                off[j * N + i] = blocked || !both && (land == water || Wrong(m, m) || Wrong(-m, m) || Wrong(m, -m) || Wrong(-m, -m));   // Settle: a blocked square ($2B14), the wrong side of the shore ($2B6C)
                wall[j * N + i] = swimming ? blocked : off[j * N + i];
                h[j * N + i] = hh;
            }
        foreach (var e in s.Entities.All)
        {
            if (e == allow || !InTheWay(me, e)) continue;
            int ox = Senses.Diff(e.UW(0x4A), mx), oz = Senses.Diff(e.UW(0x4E), mz);
            int rx = Math.Abs((int)e.W(0x6E)) + Math.Abs((int)me.W(0x6E)) + Clearance, rz = Math.Abs((int)e.W(0x72)) + Math.Abs((int)me.W(0x72)) + Clearance;
            if (Math.Abs(ox) > half * Cell + rx || Math.Abs(oz) > half * Cell + rz) continue;
            int i0 = Math.Max(0, (ox - rx) / Cell + half - 1), i1 = Math.Min(N - 1, (ox + rx) / Cell + half + 1);
            int j0 = Math.Max(0, (oz - rz) / Cell + half - 1), j1 = Math.Min(N - 1, (oz + rz) / Cell + half + 1);
            for (var j = j0; j <= j1; j++)
                for (var i = i0; i <= i1; i++)
                    if (Math.Abs((i - half) * Cell - ox) < rx && Math.Abs((j - half) * Cell - oz) < rz) cost[j * N + i] = Bump;
        }
        int gi = Math.Clamp((int)Math.Round(Senses.Diff(gx, mx) / (double)Cell) + half, 0, N - 1);
        int gj = Math.Clamp((int)Math.Round(Senses.Diff(gz, mz) / (double)Cell) + half, 0, N - 1);
        var best = new double[N * N]; Array.Fill(best, double.MaxValue);
        var from = new int[N * N];
        var open = new PriorityQueue<int, double>();
        var start = half * N + half;
        best[start] = 0; from[start] = -1; open.Enqueue(start, 0);
        double Guess(int i, int j) { int di = Math.Abs(i - gi), dj = Math.Abs(j - gj); return Cell * (Math.Max(di, dj) + 0.41421356 * Math.Min(di, dj)); }
        int end = start; var endGuess = Guess(half, half);
        while (open.TryDequeue(out var at, out _))
        {
            int i = at % N, j = at / N;
            var g = Guess(i, j);
            if (g < endGuess || g == endGuess && best[at] < best[end]) (end, endGuess) = (at, g);
            if (i == gi && j == gj) break;
            for (var dj = -1; dj <= 1; dj++)
                for (var di = -1; di <= 1; di++)
                {
                    if (di == 0 && dj == 0) continue;
                    int ni = i + di, nj = j + dj;
                    if (ni < 0 || nj < 0 || ni >= N || nj >= N) continue;
                    var n = nj * N + ni;
                    if (off[n] && (!off[at] || !afoot && at != start)) continue;   // swimming out of the sea is allowed, not into it; a vehicle
                                                                                     // only steps out of its own cell (M6 11-09: a tank on a shore,
                                                                                     // its cell off by its patch, routed on through the sea)
                    if (di != 0 && dj != 0 && (wall[j * N + ni] || wall[nj * N + i] || cost[j * N + ni] > 0 || cost[nj * N + i] > 0) && cost[n] == 0) continue;   // no corner cut
                    var climb = water ? 0 : Math.Abs(h[n] - h[at]);
                    var step = best[at] + Cell * (di != 0 && dj != 0 ? 1.41421356 : 1) * (off[n] ? Wet : 1) + Navigate.Climb * climb + cost[n];   // into a thing's margin: dear (where he stands is never entered, so he steps out the short way); in the water: out the short way
                    if (step >= best[n]) continue;
                    best[n] = step; from[n] = at;
                    open.Enqueue(n, step + Guess(ni, nj));
                }
        }
        var cells = new List<int>();
        for (var at = end; at != -1; at = from[at]) cells.Add(at);
        cells.Reverse();
        var pts = cells.Skip(1).Select(c => (X: Wx(c % N), Z: Wz(c / N))).ToList();
        if (end == gj * N + gi && cost[end] == 0) { if (pts.Count > 0) pts[^1] = ((ushort)gx, (ushort)gz); else pts.Add(((ushort)gx, (ushort)gz)); }
        return pts;
    }
}

/// <summary>The bot's walking brain: the way to a point (Navigate.Way), walked point by point - each leg by his near sight
/// (NearSight.Way, planned again every Replan passes as things move); stuck (moved under 8 units in 60 passes) it sidesteps
/// a while at right angles, then plans again from where it is. Stands once there.</summary>
public sealed class GoTo(int x, int z, int near = 64, bool water = false, bool both = false, Entity? allow = null, Func<int, int, bool>? avoid = null) : IBrain
{
    const int Replan = 12, Leg = 900;
    List<(int X, int Z)>? way, leg;
    int next, quiet, sidestep, side = 1, legAge;
    byte? sideWay;
    /// <summary>How far a sidestep's side must be clear: its 40 passes at a walk (2 a pass) less a little.</summary>
    const int SideLook = 64;
    /// <summary>Swimming, he aims no farther along his leg than its first dry cell (a switch for on / off runs). The stick holds a
    /// way while the wish is within 24 of it (Body.Hold); the farthest straight point, the sea no wall to a swimmer, lay ~12 off
    /// his way for ever - the leg starts from where he is, up onto the bank, then along it. M2 in one game 11-09: 1536 swum along
    /// a bank 17 off the land, 6 energy, every leg saying "out here".</summary>
    public static bool ShoreAim = true;
    (int X, int Z) legFor, replanFor = (-1, -1);
    (int X, int Z) lastSpot;
    public bool Arrived { get; private set; }
    public bool NoWay { get; private set; }
    public IReadOnlyList<(int X, int Z)>? Path => way;

    public Wish Decide(Play play)
    {
        var s = play.State;
        var me = s.Entities.All[0];
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        static int Dist(int ax, int az, int bx, int bz) => Math.Max(Math.Abs(Senses.Diff(ax, bx)), Math.Abs(Senses.Diff(az, bz)));
        if (Dist(mx, mz, x, z) <= near) { Arrived = true; return Wish.Stand; }
        Arrived = false;
        if (way is null)
        {
            way = (avoid is null ? null : Navigate.Way(s, mx, mz, x, z, water, both, avoid)) ?? Navigate.Way(s, mx, mz, x, z, water, both);   // none round what to avoid: the plain way
            if (way is null && Dist(mx, mz, x, z) <= Leg) way = [(x, z)];   // no way on the squares (it is in the water: a boat), but near: his near sight's
            next = 0; NoWay = way is null; lastSpot = (mx, mz);
        }
        if (way is null) return Wish.Stand;
        if (sidestep > 0)
        {
            sidestep--;
            if (sidestep == 0) way = null;                               // round the thing in the way: plan again from here
            if (sideWay is { } sw) return new(true, sw, false);
            var to = way is null ? (x, z) : way[next];
            return new(true, (byte)(Wish.Toward(mx, mz, to.Item1, to.Item2) + side * 64), false);
        }
        if (++quiet >= 60)                                               // not getting on: a thing, a wall, a fence in the way
        {
            if (Dist(mx, mz, lastSpot.X, lastSpot.Z) < 8)
            {                                                            // on foot, a side clear of things (M2 11-09: the side he took was a
                sidestep = 40; side = -side; sideWay = null;             // parked helicopter - touching it put him in); neither: back the way
                if (me.Bit(0x08, 15))                                    // he came. Riding: as before (a touch boards nothing)
                {
                    var at = way[next];
                    var on = Wish.Toward(mx, mz, at.X, at.Z);
                    bool Free(byte h) => Clear(s, me, mx, mz, (ushort)(mx + (int)(SideLook * Math.Sin(h * Math.PI / 128))), (ushort)(mz + (int)(SideLook * Math.Cos(h * Math.PI / 128))));
                    byte Side(int k) => (byte)(on + k * 64);
                    sideWay = Free(Side(side)) ? Side(side) : Free(Side(-side)) ? Side(side = -side) : (byte)(on + 128);
                }
            }
            quiet = 0; lastSpot = (mx, mz);
        }
        while (next < way.Count - 1 && Dist(mx, mz, way[next].X, way[next].Z) <= Navigate.Square / 4) next++;
        var (wx, wz) = way[next];
        var d = Dist(mx, mz, wx, wz);
        if (d > Leg)                                                     // a leg's end within his near sight
        {
            double f = (double)Leg / d;
            (wx, wz) = ((ushort)(mx + (int)(Senses.Diff(wx, mx) * f)), (ushort)(mz + (int)(Senses.Diff(wz, mz) * f)));
        }
        if (leg is null || ++legAge >= Replan || Dist(legFor.X, legFor.Z, wx, wz) > 64 || leg.Count == 0)
        { leg = NearSight.Way(play.State, me, wx, wz, water, both, allow); legAge = 0; legFor = (wx, wz); }
        while (leg.Count > 1 && Dist(mx, mz, leg[0].X, leg[0].Z) <= NearSight.Cell / 2) leg.RemoveAt(0);
        if (leg.Count == 0 || leg.Count == 1 && Dist(mx, mz, leg[0].X, leg[0].Z) <= NearSight.Cell / 2)
        {                                                                // the leg's end, short of its way point (in the water, in a thing):
            if (next < way.Count - 1)                                    // a way point his near sight cannot reach (the squares' way runs along a
            {                                                            // coast square that is mostly sea): skipping on ended in a run straight at
                if (!me.Bit(0x08, 15)) { NoWay = true; return Wish.Stand; }   // the goal, into the sea (M6 11-08: three vehicles, 16000 passes). Riding:
                if (replanFor != way[next]) { replanFor = way[next]; way = null; leg = null; return Decide(play); }   // no way (out, on foot);
                next++; leg = null; return Decide(play);                 // on foot: the way again from here once, then on to the next point
            }
            return new(true, Wish.Toward(mx, mz, wx, wz), false);        // the goal itself (a boat, a door): straight in
        }
        if (Pressed(s, me) is { } outWay) return new(true, outWay, false);
        var aim = leg[0];
        var far = Math.Min(leg.Count - 1, 8);
        if (ShoreAim && me.Bit(0x08, 15) && me.Bit(0x08, 10))            // swimming: out where the leg says (energy goes only in the water)
            for (var k = 0; k < far; k++)
                if (s.Ground.Sample((ushort)leg[k].X, (ushort)leg[k].Z).Land) { far = k; break; }
        for (var k = far; k > 0; k--)                                    // the farthest of the next few he walks to straight, nothing between
            if (Clear(play.State, me, mx, mz, leg[k].X, leg[k].Z)) { aim = leg[k]; break; }
        return new(true, Wish.Toward(mx, mz, aim.X, aim.Z), false, Travel: true);   // a way to go: the stick by where its step goes (Body.RealStep)
    }

    /// <summary>Up against a thing (within half the clearance of its box): the way straight out of its nearest face - walking
    /// along it his step drifts into it and the game puts him back every pass ($44FC). Null when clear.</summary>
    byte? Pressed(WorldState s, Entity me)
    {
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        foreach (var e in s.Entities.All)
        {
            if (e == allow || !NearSight.InTheWay(me, e)) continue;
            int ox = Senses.Diff(mx, e.UW(0x4A)), oz = Senses.Diff(mz, e.UW(0x4E));
            int rx = Math.Abs((int)e.W(0x6E)) + Math.Abs((int)me.W(0x6E)) + NearSight.Clearance / 2;
            int rz = Math.Abs((int)e.W(0x72)) + Math.Abs((int)me.W(0x72)) + NearSight.Clearance / 2;
            if (Math.Abs(ox) >= rx || Math.Abs(oz) >= rz) continue;
            return rx - Math.Abs(ox) < rz - Math.Abs(oz) ? (byte)(ox >= 0 ? 0x40 : 0xC0) : (byte)(oz >= 0 ? 0x00 : 0x80);
        }
        return null;
    }

    /// <summary>A straight walk with nothing in the way (NearSight.InTheWay's boxes, his own added) and, on foot, on land.</summary>
    bool Clear(WorldState s, Entity me, int x, int z, int tx, int tz)
    {
        double dx = Senses.Diff(tx, x), dz = Senses.Diff(tz, z);
        var n = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dz)) / 16.0);
        var swimming = NearSight.SwimStraight && me.Bit(0x08, 15) && me.Bit(0x08, 10);
        var things = s.Entities.All.Where(e => e != allow && NearSight.InTheWay(me, e)
            && Math.Max(Math.Abs(Senses.Diff(e.UW(0x4A), x)), Math.Abs(Senses.Diff(e.UW(0x4E), z))) < 600).ToList();
        for (var i = 1; i <= n; i++)
        {
            int px = (ushort)(x + (int)(dx * i / n)), pz = (ushort)(z + (int)(dz * i / n));
            var (_, land, blocked) = s.Ground.Sample((ushort)px, (ushort)pz);
            if (blocked || !both && land == water && !swimming) return false;   // swimming: the sea is no wall (NearSight.SwimStraight)
            foreach (var e in things)
                if (Math.Abs(Senses.Diff(e.UW(0x4A), px)) < Math.Abs((int)e.W(0x6E)) + Math.Abs((int)me.W(0x6E)) + NearSight.Clearance
                    && Math.Abs(Senses.Diff(e.UW(0x4E), pz)) < Math.Abs((int)e.W(0x72)) + Math.Abs((int)me.W(0x72)) + NearSight.Clearance) return false;
        }
        return true;
    }
}
