namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL, recordings only (fuller world 4 = WILD, run_game --wild; owner 2026-11-01: "a
/// wild attract", "flotillas of boats etc go wild", "Tanks,.. planes??"): on top of the fullest fill, every boat,
/// helicopter, tank, armoured car, jeep, gun, cow, duck and rabbit of today's map gets a group of copies beside it - flotillas,
/// squadrons, columns, gun nests, herds. A copy is the original's own 16-byte record with x, y and z changed; some
/// copies take a cut kind instead (the light aircraft on a squadron's wings, Mad Bill in a herd - built and armed, on no
/// map, docs/cut-content.md), the record's kind word changed as Fill writes it. Pure, like Fill.Plan: reads the grid,
/// returns the new records.</summary>
public static class Wild
{
    const int Keep = 800;                                // start and base spots stay clear, as the fill's
    const int MaxPerCell = 32;                           // the fill's roomy cap a cell, today's records counted
    const int Plane = 0x07, MadBill = 0x19;

    enum Where { Sea, Air, Land }

    /// <summary>Kind -> where its copies go: (dx, dz) offsets in world units from the original, and the kind of each
    /// copy (0 = the original's own).</summary>
    static readonly Dictionary<int, (Where W, (int X, int Z)[] At, int[] As)> Groups = new()
    {
        [0x08] = (Where.Sea, Vee(500, 5), []),                          // rocket boat: a flotilla in a V
        [0x0C] = (Where.Sea, Vee(600, 5), []),                          // gunboat
        [0x13] = (Where.Sea, Vee(400, 3), []),                          // rowing boat
        [0x05] = (Where.Air, Vee(600, 4), [0, Plane, 0, Plane]),        // helicopters: a squadron, planes on the wings
        [0x06] = (Where.Air, Vee(600, 4), [0, Plane, 0, Plane]),
        [0x03] = (Where.Land, Vee(500, 4), []),                         // tanks: a column
        [0x04] = (Where.Land, Vee(500, 4), []),
        [0x0F] = (Where.Land, Vee(450, 3), []),                         // armoured car
        [0x0D] = (Where.Land, Vee(400, 3), []),                         // jeep
        [0x6C] = (Where.Land, Ring(500, 3), []),                        // machine gun: a nest
        [0x6E] = (Where.Land, Ring(500, 3), []),                        // tracer gun
        [0x1A] = (Where.Land, Ring(400, 5), [0, 0, MadBill, 0, 0]),     // cow: a herd, Mad Bill among it
        [0x1B] = (Where.Land, Ring(450, 2), []),                        // bull
        [0x17] = (Where.Sea, Ring(250, 6), []),                         // duck: a raft of them
        [0x15] = (Where.Land, Ring(300, 6), []),                        // rabbit: a warren
    };

    // n places behind the leader in a V, gap apart: left, right, left, right...
    static (int, int)[] Vee(int gap, int n) =>
        [.. Enumerable.Range(1, n).Select(i => ((i % 2 == 1 ? -1 : 1) * gap * ((i + 1) / 2), -gap * ((i + 1) / 2)))];

    // n places round the original, r away
    static (int, int)[] Ring(int r, int n) =>
        [.. Enumerable.Range(0, n).Select(i => ((int)(r * Math.Cos(i * 2 * Math.PI / n)), (int)(r * Math.Sin(i * 2 * Math.PI / n))))];

    public static List<byte[]> Plan(byte[] terrain, byte[] grid, int[] typeTable, (int X, int Z)[] starts, Ground ground,
        IEnumerable<byte[]> filled)
    {
        var perCell = new int[256];
        for (var at = 0; at < grid.Length; at += 16)
            if ((grid[at] | grid[at + 1]) != 0) perCell[at / 128]++;
        foreach (var rec in filled) perCell[Cell(rec)]++;
        var added = new List<byte[]>();
        var sources = Enumerable.Range(0, grid.Length / 16).Select(i => grid.AsSpan(i * 16, 16).ToArray()).Concat(filled.ToList());
        foreach (var src in sources)                     // today's records and the fill's: the fill's ducks and rabbits too
        {
            if ((src[0] | src[1]) == 0) continue;
            var ty = src[0] + typeTable[src[1]];
            if (!Groups.TryGetValue(ty, out var g)) continue;
            int x0 = src[6] << 8 | src[7], y0 = (short)(src[8] << 8 | src[9]), z0 = src[10] << 8 | src[11];
            for (var k = 0; k < g.At.Length; k++)
            {
                var (dx, dz) = g.At[k];
                int x = x0 + dx, z = z0 + dz;
                if (x < 0 || z < 0 || x > 65535 || z > 65535) continue;
                if (starts.Any(s => Math.Abs(s.X - x) < Keep && Math.Abs(s.Z - z) < Keep)) continue;
                var water = GroundMesh.Water(terrain, (ushort)x, (ushort)z);
                if (g.W != Where.Air && g.W == Where.Sea != water) continue;
                var rec = src[..16];
                var y = g.W == Where.Land ? ground.Height((ushort)x, (ushort)z) : y0;
                rec[6] = (byte)(x >> 8); rec[7] = (byte)x;
                rec[8] = (byte)(y >> 8); rec[9] = (byte)y;
                rec[10] = (byte)(z >> 8); rec[11] = (byte)z;
                if (k < g.As.Length && g.As[k] != 0) Kind(rec, g.As[k], typeTable);
                if (perCell[Cell(rec)] >= MaxPerCell) continue;
                perCell[Cell(rec)]++;
                added.Add(rec);
            }
        }
        // ducks like ponds (owner 2026-11-01): a raft on every small inland pool - a region of sea cells, not touching the
        // map's edge, of at most PondMost cells - round its middle cell, a copy of the first duck's record each
        var duck = sources.FirstOrDefault(r => (r[0] | r[1]) != 0 && r[0] + typeTable[r[1]] == 0x17);
        if (duck is not null)
            foreach (var (cx, cz) in Ponds(terrain))
                foreach (var (dx, dz) in Ring(150, 4))
                {
                    int x = cx + dx, z = cz + dz;
                    if (!GroundMesh.Water(terrain, (ushort)x, (ushort)z)) continue;
                    var rec = duck[..16];
                    rec[6] = (byte)(x >> 8); rec[7] = (byte)x; rec[8] = 0; rec[9] = 0; rec[10] = (byte)(z >> 8); rec[11] = (byte)z;
                    if (perCell[Cell(rec)] >= MaxPerCell) continue;
                    perCell[Cell(rec)]++;
                    added.Add(rec);
                }
        return added;
    }

    /// <summary>The wild attract (owner 2026-11-02: "the player doesn't go to wild town"; "not many trees or rocks"):
    /// groups beside the demo's own route (DemoRoute) - every Step of his travel a group RouteSide off to one side,
    /// sides taking turns: on land a stand of trees and rocks, a herd of cows (Mad Bill among it) or a rabbit warren;
    /// on the sea a flotilla of rocket boats or a raft of ducks. Animals and boats copy today's first record of their
    /// kind; trees and rocks are made as the fill makes them.</summary>
    public static List<byte[]> AlongRoute(byte[] terrain, byte[] grid, int[] typeTable, (int X, int Z)[] route, Ground ground,
        IEnumerable<byte[]> filled)
    {
        var perCell = new int[256];
        for (var at = 0; at < grid.Length; at += 16)
            if ((grid[at] | grid[at + 1]) != 0) perCell[at / 128]++;
        foreach (var rec in filled) perCell[Cell(rec)]++;
        var sources = Enumerable.Range(0, grid.Length / 16).Select(i => grid.AsSpan(i * 16, 16).ToArray())
            .Where(r => (r[0] | r[1]) != 0).ToList();
        byte[]? First(int ty) => sources.FirstOrDefault(r => r[0] + typeTable[r[1]] == ty);
        var added = new List<byte[]>();
        void Put(byte[]? template, int ty, int x, int z, bool sea)
        {
            if (x < 0 || z < 0 || x > 65535 || z > 65535 || GroundMesh.Water(terrain, (ushort)x, (ushort)z) != sea) return;
            var rec = template?[..16] ?? new byte[16];
            if (template is null) { rec[4] = 0; rec[5] = 20; }      // scenery's word, as the fill's SpawnWord
            if (template is null || ty != template[0] + typeTable[template[1]]) Kind(rec, ty, typeTable);
            var y = sea ? 0 : ground.Height((ushort)x, (ushort)z);
            rec[6] = (byte)(x >> 8); rec[7] = (byte)x; rec[8] = (byte)(y >> 8); rec[9] = (byte)y; rec[10] = (byte)(z >> 8); rec[11] = (byte)z;
            if (perCell[Cell(rec)] >= MaxPerCell) return;
            perCell[Cell(rec)]++;
            added.Add(rec);
        }
        double travelled = 0;
        int n = 0, landPick = 0, seaPick = 0;
        for (var i = 1; i < route.Length; i++)
        {
            double dx = route[i].X - route[i - 1].X, dz = route[i].Z - route[i - 1].Z, step = Math.Sqrt(dx * dx + dz * dz);
            if (step > 2000) continue;                                // a jump (HQ, a death): no direction
            travelled += step;
            if (travelled < RouteStep || step == 0) continue;
            travelled = 0;
            var side = n++ % 2 == 0 ? 1 : -1;
            // clear of the WHOLE route, not just this point's leg: the route doubles back, and KeepClear dropped the
            // group again (all 42 route rabbits went that way) - so the other side when this one is not clear
            int cx = route[i].X + (int)(-dz / step * RouteSide * side), cz = route[i].Z + (int)(dx / step * RouteSide * side);
            int fx = route[i].X - (int)(-dz / step * RouteSide * side), fz = route[i].Z - (int)(dx / step * RouteSide * side);   // the far side
            bool Near(int x, int z) => route.Any(p => Math.Abs(p.X - x) < RouteClear + 500 && Math.Abs(p.Z - z) < RouteClear + 500);
            if (Near(cx, cz) && fx >= 0 && fz >= 0 && fx <= 65535 && fz <= 65535 && !Near(fx, fz)) (cx, cz) = (fx, fz);   // the other side when it is clear
            if (cx < 0 || cz < 0 || cx > 65535 || cz > 65535) continue;
            if (GroundMesh.Water(terrain, (ushort)cx, (ushort)cz))
            {
                if (seaPick++ % 2 == 0) foreach (var (ox, oz) in Vee(400, 5).Prepend((0, 0))) Put(First(0x13), 0x13, cx + ox, cz + oz, true);   // rowing boats: unarmed
                else foreach (var (ox, oz) in Ring(250, 6)) Put(First(0x17), 0x17, cx + ox, cz + oz, true);
            }
            else switch ((landPick++ + 2) % 3)                              // a warren first: few spots clear the whole route
            {
                case 0:                                               // a stand of trees, rocks among them
                    var kinds = new[] { Fill.TreeLarge, Fill.Tree, Fill.Rock, Fill.TreeSmall, Fill.Tree, Fill.Rock, Fill.TreeLarge };
                    var at = Ring(450, 6).Prepend((0, 0)).ToArray();
                    for (var k = 0; k < at.Length; k++) Put(null, kinds[k], cx + at[k].Item1, cz + at[k].Item2, false);
                    break;
                case 1:
                    var herd = Ring(400, 5);
                    for (var k = 0; k < herd.Length; k++) Put(First(0x1A), k == 2 ? MadBill : 0x1A, cx + herd[k].Item1, cz + herd[k].Item2, false);
                    break;
                default:
                    foreach (var (ox, oz) in Ring(300, 6)) Put(First(0x15), 0x15, cx + ox, cz + oz, false);
                    break;
            }
        }
        return added;
    }

    const int RouteStep = 2500, RouteSide = 2200;
    public const int RouteClear = 1500;                             // DemoRouteTests: the demo keeps its way with this

    /// <summary>Every added record at least clear from the demo's route, so nothing added stands in his way - an armed
    /// one (it would shoot him from further off) ArmedClear.</summary>
    public static List<byte[]> KeepClear(List<byte[]> recs, (int X, int Z)[] route, int clear, int[] typeTable) =>
        recs.Where(r =>
        {
            int x = r[6] << 8 | r[7], z = r[10] << 8 | r[11];
            var c = Armed(r[0] + typeTable[r[1]]) ? ArmedClear : clear;
            return !route.Any(p => Math.Abs(p.X - x) < c && Math.Abs(p.Z - z) < c);
        }).ToList();

    // tanks, helicopters, the plane, rocket boat, gunboat, jeep, armoured car, Mad Bill, and the guns SAM launcher .. mine
    static bool Armed(int ty) => ty is 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x0C or 0x0D or 0x0F or MadBill
        || ty >= 0x68 && ty <= 0x71;

    const int ArmedClear = 4000;

    const int PondMost = 12;

    // the middle (world units) of each pond: its cell nearest the region's mean
    static IEnumerable<(int X, int Z)> Ponds(byte[] terrain)
    {
        var seen = new bool[128 * 128];
        for (var start = 0; start < seen.Length; start++)
        {
            if (seen[start] || GroundMesh.CellType(terrain, start % 128, start / 128) != 0) continue;
            var cells = new List<int>(); var todo = new Stack<int>(); todo.Push(start); seen[start] = true; var edge = false;
            while (todo.Count > 0)
            {
                var c = todo.Pop(); cells.Add(c);
                int col = c % 128, row = c / 128;
                if (col == 0 || row == 0 || col == 127 || row == 127) edge = true;
                foreach (var n in new[] { (col - 1, row), (col + 1, row), (col, row - 1), (col, row + 1) })
                {
                    if (n.Item1 < 0 || n.Item2 < 0 || n.Item1 > 127 || n.Item2 > 127) continue;
                    var k = n.Item2 * 128 + n.Item1;
                    if (seen[k] || GroundMesh.CellType(terrain, n.Item1, n.Item2) != 0) continue;
                    seen[k] = true; todo.Push(k);
                }
            }
            if (edge || cells.Count > PondMost) continue;
            double mx = cells.Average(c => c % 128), mz = cells.Average(c => c / 128);
            var mid = cells.MinBy(c => (c % 128 - mx) * (c % 128 - mx) + (c / 128 - mz) * (c / 128 - mz));
            yield return (mid % 128 * 512 + 256, mid / 128 * 512 + 256);
        }
    }

    // a record's kind word as Fill.Add writes it: the class whose base at $81BA is the highest at or under the type
    // (all twelve classes - vehicles sit under the fill's lowest)
    static void Kind(byte[] rec, int ty, int[] typeTable)
    {
        var cls = -1;
        for (var i = 0; i < 12; i++)
            if (typeTable[i] <= ty && (cls < 0 || typeTable[i] > typeTable[cls])) cls = i;
        rec[0] = (byte)(ty - typeTable[cls]); rec[1] = (byte)cls;
    }

    static int Cell(byte[] rec) => ((rec[10] & 0xF0) >> 4) * 16 + ((rec[6] & 0xF0) >> 4);
}
