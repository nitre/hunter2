using System;
using System.Collections.Generic;

namespace Hunter.Rules;

/// <summary>
/// (the fuller world, default off - the setting is build job 2;
/// fuller_world_plan + the lead's CHECKED 2026-10-22): Fill.Plan works out, from the map's own
/// seed and terrain, the extra grid records a fuller world adds - ordinary 16-byte records in
/// the consumer's layout ($81EE / StartState.cs:495-505: x = bytes 6-7, y = 8-9, z = 10-11,
/// 2048 records, 16 x 16 cells x 8 slots, StartState.cs:10) - so saves, write-back and tests
/// are unchanged. Pure: reads its arguments, returns the new records, touches nothing - not
/// even the grid it is given. Integer maths only; the random stream is the terrain generator's
/// own ($CC18, Terrain.Step) reseeded from the world seed, so the same map + level always
/// gives the same records.
/// Type numbers are the game's own (tools/probes/names.py): cow $1A, bull $1B, vulture $1C -
/// the plan's fuller.py placed $19/$1A/$1B (mad bill/cow/bull) and is corrected here.
/// Rules with the lead's CHECKED fixes: vultures $1C circle the map's crypt $AC / tomb $8A /
/// grave $7C spots (never gun cells, never open sea); ducks $17 sit on water within 1 terrain
/// point of the beach and never on a puddle (the water region must be 8+ points); sharks $18
/// the farthest out of anything (dl >= 8, region 64+) plus 1-2 by every shark sign $92 today's
/// world leaves unguarded; fish $16 mid-water (dl 3-4); cows $1A and rabbits $15 on the low
/// coastal flats (rabbits) or gentler ground than the plan asked (cows).
/// A record's y is the ground height $36AA at its spot - bilinear over the cell's four corner
/// height bytes (Ground.cs:99-110) - which is what the spawner reads into +$74 at spawn
/// (StartState.cs:575) and the draw uses (Objects.cs:1046); Graves.cs:57 places its records the
/// same way. Sea kinds carry y = 0 like today's records do.
/// Keep-clear (about 450 at the fullest is fine): 800 units around every building, gun
/// and start; 300 around any existing or just-added record; cells already holding 6+ records
/// left alone; up to 32 records in a cell: the first 8 go in the
/// cell's grid slots, the rest in roomy memory's overflow list for that cell (StartState), which
/// the fuller world always has on.
/// </summary>
public static class Fill
{
    // --- the placed types (tools/probes/names.py) ---
    public const int Gull = 0x14, Rabbit = 0x15, Fish = 0x16, Duck = 0x17, Shark = 0x18,
                     Cow = 0x1A, Bull = 0x1B, Vulture = 0x1C, SharkSign = 0x92,
                     TreeLarge = 0x72, Tree = 0x73, TreeSmall = 0x74, Rock = 0x75,
                     Menhir = 0x76, FlowerRed = 0x77, FlowerYellow = 0x78, Stump = 0xA8;

    const int RowBytes = 256, Points = 128, MapBytes = 33280;
    const int GridRecords = 2048, RecordBytes = 16, CellsPerSide = 16, SlotsPerCell = 8;
    const int Point = 512;                                               // world units per terrain point
    const int BuildingKeep = 800, RecordKeep = 300, StartKeep = 4096;
    const int MaxPerCell = 32, RoomyCell = 6;                            // cells at 6+ left alone; past the 8 grid
                                                                         // slots a cell's records go in its overflow list

    static readonly int[] FarmTypes = { 0x99, 0x9C, 0x9E, 0xA2 };        // shed, tent, bungalow, small hut
    static readonly int[] GraveSpotTypes = { 0x7C, 0x8A, 0xAC };         // grave, tomb, crypt (names.py)

    static bool IsBuilding(int t) => t >= 0x95 && t <= 0xAF && t != 0xA8;    // hangar $96 .. Ready for action $AF, stump $A8 is scenery
    static bool IsGun(int t) => t >= 0x68 && t <= 0x71;                      // SAM launcher $68 .. mine $71
    static bool IsFarm(int t) => Array.IndexOf(FarmTypes, t) >= 0;
    static bool IsGraveSpot(int t) => Array.IndexOf(GraveSpotTypes, t) >= 0;

    /// <summary>Level 1/2/3 - the plan's light ~150, medium ~400, dense ~800 (~450 at
    /// the fullest is fine, so level 3 places what the keep-clear leaves room for).</summary>
    public static int Budget(int level) => level switch { 1 => 150, 2 => 400, 3 => 800, _ => 0 };

    /// <summary>The new records for one map + level. terrain = the game's 33280-byte map
    /// (height byte, type byte per point, row-major 128 x 128); grid = today's 2048 x 16
    /// records, read only; typeTable = 256 words read from $81BA (StartState.cs:500 - a
    /// record's type = rec[0] + typeTable[rec[1]]; only the first 12 are class bases, the
    /// rest is the code that follows - the original reads those words the same way), needed
    /// to sort today's records into buildings/guns/farms/grave spots and to write new
    /// records' kind words; starts = the start and base spots, which nothing may sit on;
    /// ground = the game's own height ($36AA, with its Maths32 already set) the records stand on.</summary>
    public static List<byte[]> Plan(int seed, byte[] terrain, byte[] grid, int level,
                                    int[] typeTable, (int X, int Z)[] starts, Ground ground)
    {
        int GroundY(int x, int z) => ground.Height((ushort)x, (ushort)z);
        var added = new List<byte[]>();
        var budget = Budget(level);
        if (budget == 0 || terrain.Length < MapBytes || grid.Length < GridRecords * RecordBytes
            || typeTable.Length < 256)
            return added;

        var land = new bool[Points * Points];
        var hgt = new int[Points * Points];
        var typ = new int[Points * Points];
        for (var r = 0; r < Points; r++)
            for (var c = 0; c < Points; c++)
            {
                hgt[r * Points + c] = terrain[r * RowBytes + c * 2];
                typ[r * Points + c] = terrain[r * RowBytes + c * 2 + 1];
                land[r * Points + c] = hgt[r * Points + c] > 0;
            }
        var dw = DistanceField(land, false);                             // land points: points to nearest water
        var dl = DistanceField(land, true);                              // water points: points to nearest land
        var region = WaterRegions(land);                                 // per water point: its region's point count
        var wet = new bool[Points * Points];                             // open water = terrain class $00 only
        for (var p = 0; p < wet.Length; p++) wet[p] = typ[p] == 0;       // (Terrain.cs $CF3E: all four corners
        var coast = DistanceField(wet, true);                            // under the sea). hgt 0 alone is NOT water -
        var maxH = 0;                                                    // the original's own trees stand on height-0
        for (var p = 0; p < hgt.Length; p++)                              // grass (median 0-2), so scenery may too
            if (typ[p] != 0 && hgt[p] > maxH) maxH = hgt[p];
        var oldGround = 0;                                               // the old gate's tree ground (height above
        for (var p = 0; p < hgt.Length; p++)                              // sea): the level caps below size against it
            if (hgt[p] > 0 && typ[p] != 0 && Slope(p) <= 6) oldGround++;
        // Own-kind gaps per tier (world units): a single keeps this far from its own kind - random
        // spots, no grid - so the singles' spacing lands near the original's at level 1 and keeps a
        // floor as the world fills. A wood/clump packs its own members close; only its centre owes
        // the gap. Tier 1 = the original's own density, so its gap is the widest.
        int[] treeGap = { 1536, 1280, 1024 }, flowerGap = { 1024, 896, 896 }, stoneGap = { 2048, 1536, 1280 };

        // today's records: positions, per-cell counts, and the special spots
        var cellRecs = new int[CellsPerSide, CellsPerSide];
        var occupied = new List<(int X, int Z)>();
        var treeKind = new List<(int X, int Z)>();                      // own-kind gaps are measured against
        var flowerKind = new List<(int X, int Z)>();                    // the original's own scenery of that
        var stoneKind = new List<(int X, int Z)>();                     // kind too, not only ours
        var keeps = new List<(int X, int Z)>(starts);
        var farms = new List<(int X, int Z)>();
        var graveSpots = new List<(int X, int Z)>();
        var sharkSigns = new List<(int X, int Z)>();                     // warning posts $92 (FillHomesTests:
        var sharksToday = new List<(int X, int Z)>();                    // 9 of 11 stand in the water itself)
        var origAnimals = new Dictionary<int, int>();                    // today's count of each type (bulls as cows)
        for (var cell = 0; cell < GridRecords; cell++)
        {
            var at = cell * RecordBytes;
            if ((grid[at] | grid[at + 1]) == 0) continue;
            var type = grid[at] + typeTable[grid[at + 1]];
            int x = grid[at + 6] << 8 | grid[at + 7], z = grid[at + 10] << 8 | grid[at + 11];
            occupied.Add((x, z));
            if (type >= TreeLarge && type <= TreeSmall) treeKind.Add((x, z));
            else if (type == Rock || type == Menhir) stoneKind.Add((x, z));
            else if (type == FlowerRed || type == FlowerYellow) flowerKind.Add((x, z));
            cellRecs[(z >> 12) & 15, (x >> 12) & 15]++;
            // the crypt/tomb/grave types sit inside the $95-$AF building range, but they are what
            // vultures are meant to circle - the 300 record clear still keeps ground records off them.
            // The stump $A8 is scenery by type, but it is the green monk's tree - a place you visit -
            // so it gets the same 800 keep-clear as a building (lead, 2026-10-28)
            if ((IsBuilding(type) || type == Stump || IsGun(type)) && !IsGraveSpot(type)) keeps.Add((x, z));
            if (IsFarm(type)) farms.Add((x, z));
            if (IsGraveSpot(type)) graveSpots.Add((x, z));
            if (type == Shark) sharksToday.Add((x, z));
            if (type == SharkSign) sharkSigns.Add((x, z));
            origAnimals[type == Bull ? Cow : type] = origAnimals.GetValueOrDefault(type == Bull ? Cow : type) + 1;
        }
        var baseCell = (int[,])cellRecs.Clone();                         // today's counts, before Fill

        bool Room(int x, int z, (int X, int Z)? allow = null)
        {
            if (cellRecs[(z >> 12) & 15, (x >> 12) & 15] >= RoomyCell) return false;   // a try starts only in a cell under 6: spreads the fill
            foreach (var k in keeps)
            {
                if (k == allow) continue;                    // a cow may stand near its own shed
                if (Near(x, z, k, BuildingKeep)) return false;
            }
            if (NearAny(x, z, occupied, RecordKeep)) return false;
            return true;
        }
        /// <summary>The one gate every record passes: cells holding 6+ of TODAY'S records stay
        /// empty, never more than 8 a cell, the 800 building/start clear for every record, and
        /// the 300 record clear for everything on the ground - each member of a ground group
        /// faces it alone, like the plan's Python (its fuller.py never skipped a flockmate). The
        /// air kinds (gulls, vultures) flock, and a flock is supposed to crowd the sky, not the
        /// ground. allowKeep = the shed a cow may graze near.</summary>
        bool Add(int ty, int x, int y, int z, (int X, int Z)? allowKeep = null)
        {
            var cr = (z >> 12) & 15; var cc = (x >> 12) & 15;
            if (baseCell[cr, cc] >= RoomyCell) return false;            // cells at 6+ left alone
            if (cellRecs[cr, cc] >= MaxPerCell) return false;           // the fill's own cap a cell
            foreach (var k in keeps)
            {
                if (k == allowKeep) continue;
                if (Near(x, z, k, BuildingKeep)) return false;
            }
            var air = ty == Gull || ty == Vulture;                       // the flock stays together
            if (!air && NearAny(x, z, occupied, RecordKeep)) return false;
            cellRecs[cr, cc]++;
            var rec = new byte[RecordBytes];
            var cls = 2;                                                 // the kind word: the highest class
            for (var i = 3; i < 12; i++)                                 // base at $81BA <= the type, like
                if (typeTable[i] <= ty) cls = i;                         // today's records (a tree's word 7 = $72)
            rec[0] = (byte)(ty - typeTable[cls]);
            rec[1] = (byte)cls;
            var w = SpawnWord(ty);
            rec[4] = (byte)(w >> 8); rec[5] = (byte)w;
            rec[6] = (byte)(x >> 8); rec[7] = (byte)x;                   // $81EE: x = bytes 6-7
            rec[8] = (byte)(y >> 8); rec[9] = (byte)y;                   // y = 8-9
            rec[10] = (byte)(z >> 8); rec[11] = (byte)z;                 // z = 10-11
            added.Add(rec);
            occupied.Add((x, z));
            return true;
        }

        // vultures first: pairs circle each crypt/tomb/grave spot on the map,
        // 600+ up, never over open sea. Some spots sit right on the beach (the missions crypt's
        // own point is water), so draw the circle centre over the points within 2 of the spot's
        // point and keep trying - ring 0 (the spot's own point) can be water, and 3 draws missed
        // 32 times out of 32 on that seed. 30 draws over the 5x5 always lands one on that map.
        var rngV = new Gen(seed ^ 0x1B1B);
        foreach (var (gx, gz) in graveSpots)
        {
            var pc = gx >> 9; var pr = gz >> 9;
            for (var k = 0; k < 2; k++)
                for (var tries = 0; tries < 30; tries++)
                {
                    var ring = tries % 3;
                    var c2 = pc + (int)(rngV.Rnd() % (2 * ring + 1)) - ring;
                    var r2 = pr + (int)(rngV.Rnd() % (2 * ring + 1)) - ring;
                    if (c2 < 0 || c2 >= Points || r2 < 0 || r2 >= Points) continue;
                    var p2 = r2 * Points + c2;
                    if (!land[p2]) continue;
                    var lo = Math.Max(0, gx - 1024 - c2 * Point);        // the fine offset stays
                    var hi = Math.Min(Point, gx + 1024 - c2 * Point);    // inside 1024 of the spot
                    if (hi <= lo) continue;
                    var x = c2 * Point + lo + (int)(rngV.Rnd() % (hi - lo));
                    var zlo = Math.Max(0, gz - 1024 - r2 * Point);
                    var zhi = Math.Min(Point, gz + 1024 - r2 * Point);
                    if (zhi <= zlo) continue;
                    var z = r2 * Point + zlo + (int)(rngV.Rnd() % (zhi - zlo));
                    var dx = x - gx; var dz = z - gz;
                    if (dx * dx + dz * dz > 1024 * 1024) continue;       // the corner outside the disc
                    var gy = GroundY(x, z);
                    if (!Add(Vulture, x, (gy > 640 ? gy : 640) + 600, z)) continue;
                    break;
                }
        }

        // SHARK SIGNS: a sign today's world leaves
        // without a shark within 4 points gets 1-2 in the nearest water deep enough for a shark
        // (dl >= 3; the open sea's dl >= 8 is 2.8-19.7 points from the signs, so it would guard
        // nothing). 8 of the 11 signs start unguarded, nearest qualifying water 0-2.2 points off.
        var rngS = new Gen(seed ^ 0x5EED);
        foreach (var (sx, sz) in sharkSigns)
        {
            if (NearAny(sx, sz, sharksToday, 4 * Point)) continue;       // today's shark already close
            var deep2 = new List<(int D2, int P)>();                     // the sign's deep-enough water,
            var pc = sx >> 9; var pr = sz >> 9;                          // nearest first
            for (var r2 = Math.Max(0, pr - 8); r2 <= Math.Min(Points - 1, pr + 8); r2++)
                for (var c2 = Math.Max(0, pc - 8); c2 <= Math.Min(Points - 1, pc + 8); c2++)
                {
                    var p2 = r2 * Points + c2;
                    if (!Sea(p2) || dl[p2] < 3) continue;
                    deep2.Add(((r2 - pr) * (r2 - pr) + (c2 - pc) * (c2 - pc), p2));
                }
            deep2.Sort((a, b) => a.D2.CompareTo(b.D2));
            var want = 1 + (int)(rngS.Rnd() % 2);                        // 1-2 sharks by the sign
            for (var k = 0; k < want; k++)
                foreach (var t in deep2)
                {
                    var x = (t.P % Points) * Point + (int)(rngS.Rnd() & 511);
                    var z = (t.P / Points) * Point + (int)(rngS.Rnd() & 511);
                    if (!Add(Shark, x, 0, z)) continue;                  // keep-clear says no: next water
                    sharksToday.Add((x, z));
                    break;
                }
        }

        // the quotas: fixed fractions of the budget, so the mix is the same at every density.
        // A higher level must never place FEWER of a kind than a lower one (it did: Hunter
        // trees went 66/79/44), so level N runs the tiers 1..N in order, each tier its own
        // random stream and only its quota INCREMENT over the tier before - tier 1's records
        // come out identical at every level, so counts can only grow.
        int[] tiers = { 0, Budget(1), Budget(2), Budget(3) };
        // Level caps: the fullness setting is the level, not more
        // trees. Today's totals sit under the quota because the old narrow gate starved it, so
        // each level is capped at what today's code actually placed: trees at the old gate's
        // ground x 0.072 / 0.0765 (measured against today's 106/90/118 and 108/97/139), flowers
        // at 82% / 52% of their quota. The three maps' densities differ, so no single share
        // lands all of them inside 10% - these land within about 11%.
        int TreeCap(int t) => t <= 1 ? int.MaxValue
            : t == 2 ? oldGround * 72 / 1000 : oldGround * 765 / 10000;
        int FlowerCap(int t) => t <= 1 ? int.MaxValue : tiers[t] * 12 / 100 * (t == 2 ? 82 : 52) / 100;
        Dictionary<string, int> Quota(int t) => new()
        {
            ["tree"] = Math.Min(tiers[t] * 45 / 100, TreeCap(t)) - Math.Min(tiers[t - 1] * 45 / 100, TreeCap(t - 1)),
            ["flower"] = Math.Min(tiers[t] * 12 / 100, FlowerCap(t)) - Math.Min(tiers[t - 1] * 12 / 100, FlowerCap(t - 1)),
            ["stone"] = tiers[t] * 8 / 100 - tiers[t - 1] * 8 / 100,
            ["duck"] = Animal(t, Duck) - Animal(t - 1, Duck),
            ["fish"] = Animal(t, Fish) - Animal(t - 1, Fish),
            ["shark"] = Animal(t, Shark) - Animal(t - 1, Shark),
            ["gull"] = Animal(t, Gull) - Animal(t - 1, Gull),
            ["rabbit"] = Animal(t, Rabbit) - Animal(t - 1, Rabbit),
            ["cow"] = Animal(t, Cow) - Animal(t - 1, Cow),
            ["vulture"] = Animal(t, Vulture) - Animal(t - 1, Vulture),
        };
        // The animals: 34% of a tier's budget, as before, but in the
        // map's OWN mix - each kind by how many of it the original map holds - except ducks (15%) and fish
        // (11%), which the original has only a handful of (Hunter level 3: about 40 ducks, 30 fish, 87 gulls, 62 rabbits, 27 sharks, 11 cows, 14 vultures).
        // The crypt and grave vulture pairs above come on top.
        int Animal(int t, int type)
        {
            var all = tiers[t] * 34 / 100;
            if (type == Duck) return all * 15 / 100;
            if (type == Fish) return all * 11 / 100;
            int[] mixed = { Gull, Rabbit, Shark, Cow, Vulture };
            var sum = 0;
            foreach (var k in mixed) sum += origAnimals.GetValueOrDefault(k);
            return sum == 0 ? 0 : all * 74 / 100 * origAnimals.GetValueOrDefault(type) / sum;
        }

        int LandAt(int x, int z) => (x >= 0 && z >= 0 && x < Points * Point && z >= 0 && z < Points * Point)
            ? (z >> 9) * Points + (x >> 9) : -1;
        // a sea animal's spot: below the waterline AND open water by type (class $00) - height 0 alone
        // is also the original's own flat grass, where its trees stand
        bool Sea(int p) => !land[p] && typ[p] == 0;
        bool Gentle(int p, int maxSlope, int minDw, int maxH) =>
            p >= 0 && land[p] && dw[p] >= minDw && (typ[p] & 0xF0) == 0x10
            && Slope(p) <= maxSlope && hgt[p] <= maxH;
        int Slope(int p)
        {
            var r = p / Points; var c = p % Points;
            return Math.Abs(hgt[r * Points + Math.Min(c + 1, Points - 1)] - hgt[p])
                 + Math.Abs(hgt[Math.Min(r + 1, Points - 1) * Points + c] - hgt[p]);
        }

        /// <summary>A single tree's size by its ground: larger on lower,
        /// sheltered ground, smaller higher up and near the coast. Bands are thirds of this
        /// map's own dry-land height range.</summary>
        int TreeSize(int p) =>
            hgt[p] <= maxH / 3 && coast[p] >= 4 ? TreeLarge :
            hgt[p] > maxH * 2 / 3 || coast[p] <= 2 ? TreeSmall : Tree;

        /// <summary>One member of a group: within radius units of the centre, on dry ground,
        /// kept off its own group's earlier members (Add still owes every record its 300 clear).
        /// The scatter crosses into the neighbouring point easily - the maps are mostly water -
        /// so the dry test lives here and a bad draw is simply redrawn, up to tries times.</summary>
        (int X, int Z)? Spread(ref Gen rng, int x, int z, List<(int X, int Z)> group, int radius, int tries)
        {
            for (var t = 0; t < tries; t++)
            {
                var fx = x + (int)(rng.Rnd() % (2 * radius)) - radius;
                var fz = z + (int)(rng.Rnd() % (2 * radius)) - radius;
                var fp = LandAt(fx, fz);
                if (fp < 0 || typ[fp] == 0) continue;
                if (NearAny(fx, fz, group, RecordKeep)) continue;
                return (fx, fz);
            }
            return null;
        }

        /// <summary>Free room (up to MaxPerCell a cell) across the cells a box of this radius
        /// touches: a group fires only where its whole size fits.</summary>
        int BoxFree(int x, int z, int radius)
        {
            var free = 0;
            for (var rr = Math.Max(0, (z - radius) >> 12); rr <= Math.Min(15, (z + radius) >> 12); rr++)
                for (var cc = Math.Max(0, (x - radius) >> 12); cc <= Math.Min(15, (x + radius) >> 12); cc++)
                    free += baseCell[rr, cc] < RoomyCell ? MaxPerCell - cellRecs[rr, cc] : 0;
            return free;
        }

        void PlaceTrees(ref Gen rng, Dictionary<string, int> left, int tier)
        {
            var c = (int)(rng.Rnd() & 127); var r = (int)(rng.Rnd() & 127);
            var p = r * Points + c;
            if (typ[p] == 0 || Slope(p) > 6) return;                     // any dry ground, like the original's
            var x = c * Point + (int)(rng.Rnd() & 511); var z = r * Point + (int)(rng.Rnd() & 511);
            if (!Room(x, z)) return;
            var roll = (int)(rng.Rnd() % 24);
            if (roll == 0 && left["tree"] >= 4 && BoxFree(x, z, 768) >= 4)  // 1 try in 24: a small wood, 4-8 close
            {                                                            // together, shaped - large in the middle,
                if (NearAny(x, z, treeKind, treeGap[tier - 1])) return;  // small round the edge; fired only where
                var many = Math.Min(Math.Min(4 + (int)(rng.Rnd() % 5), left["tree"]), BoxFree(x, z, 768));
                var group = new List<(int X, int Z)>();
                var misses = 0;                                          // a member Add refuses (its cell filled)
                for (var k = 0; k < many && misses < 12; k++)            // is redrawn, not dropped
                {
                    var fx = x; var fz = z;
                    if (k > 0)
                    {
                        var s = Spread(ref rng, x, z, group, 768, 14);   // wide and patient: every member owes
                        // the others 300 of clear ground, and a 512 box fits only ~5 at that spacing
                        if (s is null) { misses++; k--; continue; }
                        fx = s.Value.X; fz = s.Value.Z;
                    }
                    long dx = fx - x, dz = fz - z; var d2 = dx * dx + dz * dz;
                    var ty = d2 < 450 * 450 ? TreeLarge : d2 < 650 * 650 ? Tree : TreeSmall;   // members keep 300 apart,
                                                                                               // so the large ring must reach past it
                    if (Add(ty, fx, GroundY(fx, fz), fz)) { left["tree"]--; treeKind.Add((fx, fz)); group.Add((fx, fz)); }
                    else { misses++; k--; }
                }
            }
            else if (roll == 1 && left["tree"] >= 2)                     // 1 try in 24: a pair or clump of 2-3
            {
                if (NearAny(x, z, treeKind, treeGap[tier - 1])) return;
                var clump = Math.Min(2 + (int)(rng.Rnd() % 2), left["tree"]);
                var group = new List<(int X, int Z)>();
                for (var k = 0; k < clump; k++)
                {
                    var fx = x; var fz = z; var fp = p;
                    if (k > 0)
                    {
                        var s = Spread(ref rng, x, z, group, 384, 6);
                        if (s is null) continue;
                        fx = s.Value.X; fz = s.Value.Z;
                        fp = LandAt(fx, fz);
                    }
                    if (Add(TreeSize(fp), fx, GroundY(fx, fz), fz)) { left["tree"]--; treeKind.Add((fx, fz)); group.Add((fx, fz)); }
                }
            }
            else                                                         // the default: one tree, with room round it
            {
                if (NearAny(x, z, treeKind, treeGap[tier - 1])) return;
                if (Add(TreeSize(p), x, GroundY(x, z), z)) { left["tree"]--; treeKind.Add((x, z)); }
            }
        }
        void PlaceFlowers(ref Gen rng, Dictionary<string, int> left, int tier)
        {
            var c = (int)(rng.Rnd() & 127); var r = (int)(rng.Rnd() & 127);
            var p = r * Points + c;
            if (typ[p] == 0 || coast[p] > 6 || hgt[p] > 48 || Slope(p) > 2) return;
            var x = c * Point + (int)(rng.Rnd() & 511); var z = r * Point + (int)(rng.Rnd() & 511);
            if (!Room(x, z)) return;
            var roll = (int)(rng.Rnd() % 20);
            if (roll == 0 && left["flower"] >= 6 && hgt[p] <= 32 && Slope(p) <= 1   // 1 try in 20: a meadow,
                && BoxFree(x, z, 768) >= 6)                              // 6-12 flowers on a low flat meadow -
            {                                                            // fired only where the whole meadow fits
                if (NearAny(x, z, flowerKind, flowerGap[tier - 1])) return;  // the cells it spans
                var many = Math.Min(Math.Min(6 + (int)(rng.Rnd() % 7), left["flower"]), BoxFree(x, z, 768));
                var one = FlowerRed + (int)(rng.Rnd() % 2);              // one colour - or mixed
                var mixed = (rng.Rnd() & 1) != 0;
                var group = new List<(int X, int Z)>();
                var misses = 0;                                          // a member Add refuses is redrawn
                for (var k = 0; k < many && misses < 12; k++)
                {
                    var fx = x; var fz = z;
                    if (k > 0)
                    {
                        var s = Spread(ref rng, x, z, group, 768, 14);   // 12 flowers each owe the others 300 of
                        // clear ground - a 512 box fits only ~5 at that spacing, like the woods found
                        if (s is null) { misses++; k--; continue; }
                        fx = s.Value.X; fz = s.Value.Z;
                    }
                    var ty = mixed ? FlowerRed + (int)(rng.Rnd() % 2) : one;
                    if (Add(ty, fx, GroundY(fx, fz), fz)) { left["flower"]--; flowerKind.Add((fx, fz)); group.Add((fx, fz)); }
                    else { misses++; k--; }
                }
            }
            else if (roll <= 3 && left["flower"] >= 2)                   // 3 tries in 20: a patch of 2-5 (and
            {                                                            // any meadow try that could not fire)
                if (NearAny(x, z, flowerKind, flowerGap[tier - 1])) return;
                var patch = Math.Min(2 + (int)(rng.Rnd() % 4), left["flower"]);
                var group = new List<(int X, int Z)>();
                for (var k = 0; k < patch; k++)
                {
                    var fx = x; var fz = z;
                    if (k > 0)
                    {
                        var s = Spread(ref rng, x, z, group, 384, 6);
                        if (s is null) continue;
                        fx = s.Value.X; fz = s.Value.Z;
                    }
                    if (Add((rng.Rnd() & 1) != 0 ? FlowerRed : FlowerYellow, fx, GroundY(fx, fz), fz))
                    { left["flower"]--; flowerKind.Add((fx, fz)); group.Add((fx, fz)); }
                }
            }
            else                                                         // the default: one flower, with room round it
            {
                if (NearAny(x, z, flowerKind, flowerGap[tier - 1])) return;
                if (Add((rng.Rnd() & 1) != 0 ? FlowerRed : FlowerYellow, x, GroundY(x, z), z))
                { left["flower"]--; flowerKind.Add((x, z)); }
            }
        }
        void PlaceStones(ref Gen rng, Dictionary<string, int> left, int tier)
        {
            var c = (int)(rng.Rnd() & 127); var r = (int)(rng.Rnd() & 127);
            var p = r * Points + c;
            if (typ[p] == 0) return;
            var slope = Slope(p);
            if (slope < 4 && hgt[p] <= 80) return;
            var x = c * Point + (int)(rng.Rnd() & 511); var z = r * Point + (int)(rng.Rnd() & 511);
            if (!Room(x, z)) return;
            if (slope >= 4 && (rng.Rnd() & 3) == 0 && left["stone"] >= 2)    // 1 rock try in 4: a small
            {                                                            // outcrop of 2-4 on the steep ground
                if (NearAny(x, z, stoneKind, stoneGap[tier - 1])) return;
                var grp = Math.Min(2 + (int)(rng.Rnd() % 3), left["stone"]);
                var group = new List<(int X, int Z)>();
                for (var k = 0; k < grp; k++)
                {
                    var fx = x; var fz = z;
                    if (k > 0)
                    {
                        var s = Spread(ref rng, x, z, group, 384, 6);
                        if (s is null) continue;
                        fx = s.Value.X; fz = s.Value.Z;
                    }
                    if (Add(Rock, fx, GroundY(fx, fz), fz)) { left["stone"]--; stoneKind.Add((fx, fz)); group.Add((fx, fz)); }
                }
            }
            else                                                         // the default: one stone with room round it
            {                                                            // (menhirs are always single)
                if (NearAny(x, z, stoneKind, stoneGap[tier - 1])) return;
                if (Add(slope >= 4 ? Rock : Menhir, x, GroundY(x, z), z)) { left["stone"]--; stoneKind.Add((x, z)); }
            }
        }
        void PlaceDucks(ref Gen rng, Dictionary<string, int> left, int tier)
        {
            var c = (int)(rng.Rnd() & 127); var r = (int)(rng.Rnd() & 127);
            var p = r * Points + c;
            if (!Sea(p) || dl[p] > 1 || region[p] < 8) return;           // in sight of the beach, no puddle
            var x = c * Point + (int)(rng.Rnd() & 511); var z = r * Point + (int)(rng.Rnd() & 511);
            var grp = Math.Min(2 + (int)(rng.Rnd() % 3), left["duck"]);  // 2-4 in a group
            for (var k = 0; k < grp; k++)
            {
                if (k > 0)
                {
                    var c2 = c + (int)(rng.Rnd() % 3) - 1; var r2 = r + (int)(rng.Rnd() % 3) - 1;
                    if (c2 < 0 || c2 >= Points || r2 < 0 || r2 >= Points) continue;
                    var p2 = r2 * Points + c2;
                    if (!Sea(p2) || dl[p2] > 1 || region[p2] < 8) continue;
                    x = c2 * Point + (int)(rng.Rnd() & 511); z = r2 * Point + (int)(rng.Rnd() & 511);
                }
                if (Add(Duck, x, 0, z)) left["duck"]--;
            }
        }
        void PlaceFish(ref Gen rng, Dictionary<string, int> left, int tier)
        {
            var c = (int)(rng.Rnd() & 127); var r = (int)(rng.Rnd() & 127);
            var p = r * Points + c;
            if (!Sea(p) || dl[p] < 3 || dl[p] > 4) return;               // mid-water (people.md 3.1-4.1)
            var x = c * Point + (int)(rng.Rnd() & 511); var z = r * Point + (int)(rng.Rnd() & 511);
            var grp = Math.Min(3 + (int)(rng.Rnd() % 3), left["fish"]);  // 3-5 in a shoal
            for (var k = 0; k < grp; k++)
            {
                var fx = x + (int)(rng.Rnd() % 1024) - 512; var fz = z + (int)(rng.Rnd() % 1024) - 512;
                var fp = LandAt(fx, fz);
                // a fish is never on land, and the mask holds for every member, not just the
                // shoal's centre: the ±512 scatter alone pulled Missions' mean out to 4.4.
                if (fp < 0 || !Sea(fp) || dl[fp] < 3 || dl[fp] > 4) continue;
                if (Add(Fish, fx, 0, fz)) left["fish"]--;
            }
        }
        void PlaceSharks(ref Gen rng, Dictionary<string, int> left, int tier)
        {
            var c = (int)(rng.Rnd() & 127); var r = (int)(rng.Rnd() & 127);
            var p = r * Points + c;
            // the farthest out of anything (people.md 8.6-11.0 points; the study's mask dl >= 8).
            // A map with little water that deep simply places fewer - the count is measured in
            // FillHomesTests (today's three maps hold 3654-4145 dl>=8 points).
            if (!Sea(p) || dl[p] < 8 || region[p] < 64) return;          // wide water only
            var x = c * Point + 256; var z = r * Point + 256;
            if (NearAny(x, z, keeps, StartKeep)) return;                 // a cell clear of starts and bases
            if (Add(Shark, x, 0, z)) left["shark"]--;
        }
        void PlaceGulls(ref Gen rng, Dictionary<string, int> left, int tier)
        {
            var c = (int)(rng.Rnd() & 127); var r = (int)(rng.Rnd() & 127);
            var p = r * Points + c;
            if (dw[p] > 3) return;                                       // over the coast
            var x = c * Point + (int)(rng.Rnd() & 511); var z = r * Point + (int)(rng.Rnd() & 511);
            if (NearAny(x, z, keeps, StartKeep)) return;
            var grp = Math.Min(2 + (int)(rng.Rnd() % 2), left["gull"]);  // 2-3 in the air
            for (var k = 0; k < grp; k++)
                if (Add(Gull, x + (int)(rng.Rnd() % 512) - 256,
                        GroundY(x, z) + 300 + (int)(rng.Rnd() % 200),
                        z + (int)(rng.Rnd() % 512) - 256)) left["gull"]--;
        }
        void PlaceRabbits(ref Gen rng, Dictionary<string, int> left, int tier)
        {
            var c = (int)(rng.Rnd() & 127); var r = (int)(rng.Rnd() & 127);
            var p = r * Points + c;
            // the original's rabbits graze the LOW COASTAL FLATS (people.md: heights 1-6 against
            // land means of 10.0-14.6, 1-2 points from the water), not any gentle ground: height
            // 6 and under, within 3 points of the water, never the beach itself (dw >= 1). The
            // old gates let them climb (dw>=2, height<=112: 0.87-1.17 of the land mean; the low
            // third of the map's own max: Hunter's coast climbs inside that band, 1.27).
            if (!Gentle(p, 6, 1, 6) || dw[p] > 3) return;
            var x = c * Point + (int)(rng.Rnd() & 511); var z = r * Point + (int)(rng.Rnd() & 511);
            if (NearAny(x, z, starts, StartKeep) || !Room(x, z)) return;
            var grp = Math.Min(3 + (int)(rng.Rnd() % 3), left["rabbit"]);      // 3-5 in a group
            for (var k = 0; k < grp; k++)
            {
                var fx = x + (int)(rng.Rnd() % 768) - 384; var fz = z + (int)(rng.Rnd() % 768) - 384;
                var fp = LandAt(fx, fz);
                if (fp < 0 || !land[fp]) continue;                        // a rabbit is never on water
                if (Add(Rabbit, fx, GroundY(fx, fz), fz)) left["rabbit"]--;
            }
        }
        // the rest of the vultures (beyond the crypt pairs): a pair over the high ground, where the
        // original's own perch (layout study: Action/Missions 18.7/14.8 against land 14.6/12.4)
        void PlaceVultures(ref Gen rng, Dictionary<string, int> left, int tier)
        {
            var c = (int)(rng.Rnd() & 127); var r = (int)(rng.Rnd() & 127);
            var p = r * Points + c;
            if (!land[p] || hgt[p] < maxH * 2 / 3) return;               // the top third of this map's heights
            var x = c * Point + (int)(rng.Rnd() & 511); var z = r * Point + (int)(rng.Rnd() & 511);
            if (NearAny(x, z, starts, StartKeep)) return;
            for (var k = 0; k < Math.Min(2, left["vulture"]); k++)
            {
                var fx = x + (int)(rng.Rnd() % 1024) - 512; var fz = z + (int)(rng.Rnd() % 1024) - 512;
                var gy = GroundY(fx, fz);
                if (Add(Vulture, fx, (gy > 640 ? gy : 640) + 600, fz)) left["vulture"]--;   // as the crypt pairs fly
            }
        }

        void PlaceCows(ref Gen rng, Dictionary<string, int> left, int tier)
        {
            if (farms.Count == 0) return;
            var f = farms[(int)(rng.Rnd() % farms.Count)];
            // wider ring than the plan's (2048..6144 with the strict gentle test placed ~0): the lead's fix
            var rad = 1024 + (int)(rng.Rnd() % 5120);                    // 0.25 - 1.5 cells from the shed
            var x = f.X + (int)(rng.Rnd() % (2 * rad)) - rad;
            var z = f.Z + (int)(rng.Rnd() % (2 * rad)) - rad;
            if (!Gentle(LandAt(x, z), 6, 2, 112)) return;
            if (!Room(x, z, f)) return;                                  // the shed itself is allowed
            var grp = Math.Min(2 + (int)(rng.Rnd() % 2), left["cow"]);   // 2-3 cows
            for (var k = 0; k < grp; k++)
            {
                var fx = x + (int)(rng.Rnd() % 512) - 256; var fz = z + (int)(rng.Rnd() % 512) - 256;
                var fp = LandAt(fx, fz);
                if (fp < 0 || !land[fp]) continue;                       // a cow is never on water
                if (Add(Cow, fx, GroundY(fx, fz), fz, f)) left["cow"]--;
            }
            if (left["cow"] > 0 && (rng.Rnd() & 1) == 0)                 // a bull only where cows are
            {
                var bx = x + 512; var bz = z;
                var bp = LandAt(bx, bz);
                if (bp >= 0 && land[bp])
                    if (Add(Bull, bx, GroundY(bx, bz), bz, f)) left["cow"]--;
            }
        }

        var placers = new Dictionary<string, Placer>                       // by ref: every draw a placer makes moves
                                                                           // the level's stream on (a copy re-drew
                                                                           // the next try's numbers, one along)
        {
            ["tree"] = PlaceTrees, ["flower"] = PlaceFlowers, ["stone"] = PlaceStones,
            ["duck"] = PlaceDucks, ["fish"] = PlaceFish, ["shark"] = PlaceSharks,
            ["gull"] = PlaceGulls, ["rabbit"] = PlaceRabbits, ["cow"] = PlaceCows, ["vulture"] = PlaceVultures,
        };
        for (var tier = 1; tier <= level; tier++)
        {
            var left = Quota(tier);
            var pools = new List<string>(left.Keys);
            var rng = new Gen(seed ^ tiers[tier] * 251);                     // the plan's level seed
            for (var attempts = 0; attempts < 200000; attempts++)
            {
                var tot = 0;
                foreach (var k in left.Keys) tot += Math.Max(0, left[k]);
                if (tot <= 0) break;
                var rollv = (int)(rng.Rnd() % tot);
                string? pick = null;
                foreach (var pool in pools)
                {
                    if (left[pool] <= 0) continue;
                    if (rollv < left[pool]) { pick = pool; break; }
                    rollv -= left[pool];
                }
                if (pick is null) break;
                placers[pick](ref rng, left, tier);
            }
        }
        return added;
    }

    /// <summary>The word today's records carry at bytes 4-5 per placed type, copied from the
    /// world files (animals 2, vulture 3, cow/bull 12, scenery 20, shark 20 of 20/30/50).</summary>
    static int SpawnWord(int ty) => ty switch
    {
        Vulture => 3, Cow or Bull => 12, Shark => 20, >= TreeLarge => 20, _ => 2,
    };

    /// <summary>Multi-source BFS in points: toWater=false gives every land point its distance to
    /// the nearest water point (dw); toWater=true gives every water point its distance to the
    /// nearest land point (dl). Two fields because one BFS from the sea measures every water
    /// point 0 from itself (the plan's lesson).</summary>
    public static int[] DistanceField(byte[] terrain, bool toWater)
    {
        var land = new bool[Points * Points];
        for (var p = 0; p < land.Length; p++)
            land[p] = terrain[(p / Points) * RowBytes + (p % Points) * 2] > 0;
        return DistanceField(land, toWater);
    }

    public static int[] DistanceField(bool[] land, bool toWater)
    {
        var d = new int[Points * Points];
        var q = new int[Points * Points];
        var head = 0; var tail = 0;
        for (var p = 0; p < d.Length; p++)
        {
            if (land[p] == toWater) { d[p] = 0; q[tail++] = p; }
            else d[p] = int.MaxValue;
        }
        while (head < tail)
        {
            var p = q[head++];
            var r = p / Points; var c = p % Points;
            for (var k = 0; k < 4; k++)
            {
                int r2 = r + (k == 0 ? 1 : k == 1 ? -1 : 0), c2 = c + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (r2 < 0 || r2 >= Points || c2 < 0 || c2 >= Points) continue;
                var p2 = r2 * Points + c2;
                if (d[p2] == int.MaxValue) { d[p2] = d[p] + 1; q[tail++] = p2; }
            }
        }
        return d;
    }

    /// <summary>Per water point: the size (in points) of its connected water region - what keeps
    /// ducks off 1-point puddles and sharks in wide water.</summary>
    static int[] WaterRegions(bool[] land)
    {
        var region = new int[Points * Points];
        var size = new List<int> { 0 };
        var q = new int[Points * Points];
        for (var start = 0; start < region.Length; start++)
        {
            if (land[start] || region[start] != 0) continue;
            var id = size.Count;
            var count = 0;
            var head = 0; var tail = 0;
            q[tail++] = start; region[start] = id;
            while (head < tail)
            {
                var p = q[head++]; count++;
                var r = p / Points; var c = p % Points;
                for (var k = 0; k < 4; k++)
                {
                    int r2 = r + (k == 0 ? 1 : k == 1 ? -1 : 0), c2 = c + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (r2 < 0 || r2 >= Points || c2 < 0 || c2 >= Points) continue;
                    var p2 = r2 * Points + c2;
                    if (!land[p2] && region[p2] == 0) { region[p2] = id; q[tail++] = p2; }
                }
            }
            size.Add(count);
        }
        for (var p = 0; p < region.Length; p++) region[p] = size[region[p]];
        return region;
    }

    static bool NearAny(int x, int z, List<(int X, int Z)> pts, int radius)
    {
        foreach (var p in pts) if (Near(x, z, p, radius)) return true;
        return false;
    }

    static bool NearAny(int x, int z, (int X, int Z)[] pts, int radius)
    {
        foreach (var p in pts) if (Near(x, z, p, radius)) return true;
        return false;
    }

    static bool Near(int x, int z, (int X, int Z) p, int radius)
    {
        long dx = x - p.X; long dz = z - p.Z;                        // long: the map is 65536 wide,
        return dx * dx + dz * dz < (long)radius * radius;            // dx*dx overflows int past 46340
    }

    /// <summary>The terrain generator's own random stream ($CC18, Terrain.Step) - the same
    /// arithmetic, seeded here from the world seed, integer maths only.</summary>
    delegate void Placer(ref Gen rng, Dictionary<string, int> left, int tier);

    struct Gen
    {
        uint _state;

        public Gen(int seed)
        {
            uint s = (uint)seed;
            var lo = ((s << 4) | (s >> 12)) & 0xFFFF;
            lo ^= 0x00FF;
            _state = (s << 16) | lo;
        }

        public uint Rnd() => Terrain.Step(ref _state);
    }
}
