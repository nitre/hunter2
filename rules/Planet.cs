namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (PLANET step 1, docs/planet-plan.md): the world layer - six
/// faces (Faces), each a whole world: its terrain, its ground, its grid of records. The game runs in the FRAME of one
/// face (Current): every live thing keeps the original's two 16-bit position words in that face's frame, modulo
/// 65536 - a thing just past a fold holds a word that has wrapped, as the original's arithmetic already treats every
/// difference. The layer UNFOLDS a word pair against the player (Unfold: the player's own words are the anchor; a
/// live thing is never half a world away) and ROUTES what lies past a fold to the neighbour face: the ground sampler
/// (Route), the spawner's window cells (CellBeyond) and the write-back (Beyond). The folds themselves are stitched:
/// each face's store has, past its east and south edges, the neighbour's own points (Stitch), so the sampler's cell at
/// the fold blends to the next face. The player's own crossing (the frame switching face) is step 3.
/// Null on the WorldState (the default): nothing here runs, every path the original's.</summary>
public sealed partial class Planet
{
    public sealed class Face
    {
        public required int Index { get; init; }
        /// <summary>The terrain seed (a world's own, or a grown face's).</summary>
        public required ushort Seed { get; init; }
        /// <summary>One of the three worlds, or null: a grown face (step 2 fills it).</summary>
        public GameMode? Mode { get; init; }
        public required Terrain Terrain { get; init; }
        public required Ground Ground { get; init; }
        /// <summary>$83BA's layout: 16 x 16 cells x 8 slots of 16 bytes (the current face's is the WorldState's own).</summary>
        public required byte[] Grid { get; init; }
        /// <summary>The roomy-memory extras of this face's grid (the current face's are the WorldState's own).</summary>
        public required Dictionary<int, byte> FuelLow { get; init; }
        public required Dictionary<int, List<byte[]>> Overflow { get; init; }
        /// <summary>THE SAND ZONE (SandZone.Face only): this face's sand and rock; null on every other face.</summary>
        public SandZone? Sand { get; init; }
    }

    public readonly Face[] Faces = new Face[Rules.Faces.Count];
    /// <summary>The face the game runs in: its frame holds every live thing's words.</summary>
    public int Current { get; private set; }
    readonly WorldState _s;
    int _ax, _az;                                                   // the anchor: the player's last live position
    int _px, _pz;                                                   // the player's last position inside the current face (Pass)

    /// <summary>The played world is always face 0 (the top); the other five are GROWN from these seeds (bytes doubled
    /// like the worlds' own $B2B2 / $0909 / $3737; placeholders - the other game modes' worlds are NOT
    /// the other faces; "making our one once I know the tech works").</summary>
    public const int WorldFace = 0;
    public static readonly IReadOnlyDictionary<int, ushort> GrownSeeds = new Dictionary<int, ushort>
        { [1] = 0x5151, [2] = 0xA3A3, [3] = 0x6C6C, [4] = 0x2D2D, [5] = 0xE7E7 };
    /// <summary>The sea band of a grown face: its outer ring and this many rows and columns inside it open sea (height
    /// 0, type 0, the ring's border type too) - so every fold is under water on the grown side,
    /// and the played world's coasts, which run to its south, east and west edges (measured 2026-11-11: Hunter's south
    /// edge row has 42 land points, its north none; Missions 20 / 0; Action 5 / 0), end in the neighbour's sea.</summary>
    public const int Band = 2;

    Planet(WorldState s) => _s = s;

    /// <summary>The planet round a started game: the state's own world is its mode's face (the same terrain, ground
    /// and grid objects); the other two worlds and the three grown faces are built beside it, each with the ground
    /// the state's settings give (maths32, finer ground at the state's spec, each face's detail from its own seed;
    /// the spike fix as the state had it). seaBorder: a grown face's outer ring of heights set to the sea's level
    /// (the rule that keeps every fold under water; the three worlds' maps are never touched). Then the folds are
    /// stitched and every ground routes through the layer.</summary>
    public static Planet Build(WorldState s, GameData d, bool fixSpikes, int fullerWorld = 0, int scrTrack = 0)
    {
        var p = new Planet(s) { Current = WorldFace };
        p.Faces[WorldFace] = new Face { Index = WorldFace, Seed = d.Worlds[s.Mode].Seed, Mode = s.Mode, Terrain = s.Terrain, Ground = s.Ground,
                                        Grid = s.Grid, FuelLow = s.FuelLow, Overflow = s.Overflow };
        var table = fullerWorld > 0 ? StartState.TypeTable(d) : null;
        foreach (var (i, seed) in GrownSeeds)
        {
            var scr = i == ScrIsland.Face;                                  // STUNTIN': its own flat land, a car, nothing else
            var terrain = scr ? ScrIsland.Land() : Terrain.Generate(seed, i == SandZone.Face ? SandZone.SeaLevel : (short)0x80);   // all three worlds use sea level $80 (terrain.md); THE SAND ZONE a lower one: one big island
            if (fixSpikes && !scr) FineGround.Unwrapped(terrain, seed).CopyTo(terrain.Map, 0);
            SeaBorder(terrain, Band);
            if (i == SandZone.Face) SandZone.OneIsland(terrain);              // THE SAND ZONE: the islets round the main land go under
            var ground = new Ground(new MapMemory(terrain, d.Objfile, d.Sinfile)) { Maths32 = s.Maths32 };
            if (scr)                                                        // STUNTIN' (TALL GROUND): its full heights, past the byte's
            {                                                               // 4080, in the stores - at the game's N (1 without finer ground)
                var heights = ScrIsland.Heights(terrain);
                ground.Fine = FineGround.FromHeights(heights, s.FineN > 0 ? s.FineN : 1);
                ground.Smooth = FineGround.FromHeights(heights, 1);
                if (ScrRoad.Load(d.Directory, scrTrack) is { } road) ground.Road = new ScrRoadSurface(road);   // the game's track (ScrIsland.Tracks)
            }
            else if (s.FineN > 0)
            {
                ground.Fine = FineGround.Grow(terrain, seed, s.FineN, s.FineNum, s.FineDen, seed, s.FineDim, s.FineHold);
                ground.Smooth = FineGround.Smooth(FineGround.Unwrapped(terrain, seed), s.FineNum, s.FineDen);
            }
            var face = new Face { Index = i, Seed = seed, Mode = null, Terrain = terrain, Ground = ground, Grid = new byte[0x8000], FuelLow = [], Overflow = [],
                                  Sand = i == SandZone.Face ? new SandZone(terrain) : null };   // THE SAND ZONE: the first grown face
            if (scr) StartState.StoreRecord(face.Grid, face.Overflow, ScrIsland.Car(d, ground));
            else if (table is not null)                                     // the fuller world's own things on a grown face
                foreach (var rec in face.Sand is null ? Fill.Plan(seed, terrain.Map, face.Grid, Math.Min(fullerWorld, 3), table, [], ground)
                                                      : SandZone.Stones(Fill.Plan(seed, terrain.Map, face.Grid, Math.Min(fullerWorld, 3), table, [], ground), table))   // sand: stones only
                    StartState.StoreRecord(face.Grid, face.Overflow, rec);  // (no starts to keep clear; never the wild)
            if (face.Sand is { } sand)                                      // THE SAND ZONE's harvest: spice fields and a harvester (Spice)
                foreach (var rec in Spice.Plant(sand, ground, seed, Spice.EdgeTo(i, WorldFace)))
                    StartState.StoreRecord(face.Grid, face.Overflow, rec);
            p.Faces[i] = face;
        }
        p.Stitch();
        for (var i = 0; i < Rules.Faces.Count; i++) p.Faces[i].Ground.SetPlanet(p, i);
        var start = s.PlayerRecord;                                         // the anchor before the player is live
        (p._ax, p._az) = (start[6] << 8 | start[7], start[10] << 8 | start[11]);
        (p._px, p._pz) = (p._ax, p._az);
        return p;
    }

    /// <summary>The sea rule on a grown face: the outer ring of map points (row 0, row 127, column 0, column 127) and
    /// `band` rows and columns inside it open sea - height 0, type 0, the ring's border type $98 too (its bit 7 is the
    /// sampler's BLOCKED flag, the original's wall at the map's edge, $2B14: a grown face's edge is open water, nothing
    /// to pass). The sampler's land flag is off there ($36DA), the fill sees water: a fold is always under water on
    /// this side.</summary>
    public static void SeaBorder(Terrain t, int band = Band)
    {
        for (var k = 0; k <= band; k++)
            for (var i = 0; i < Terrain.Size; i++)
                foreach (var (c, r) in new[] { (i, k), (i, 127 - k), (k, i), (127 - k, i) })
                {
                    var at = r * Terrain.RowBytes + c * 2;
                    t.Map[at] = 0; t.Map[at + 1] = 0;
                }
    }

    // ------------------------------------------------------------------ the folds stitched

    /// <summary>Every face's points past its east and south edges (column 128 N, row 128 N of a store of N points a
    /// cell; the no-store ground's 129-long edge lines) set from the cube: a point on a fold owned by the neighbour
    /// (on its own lattice, inside its far edges) takes the neighbour's own value; a point no face owns (a fold where
    /// both sides reach it only past their edges) takes the mean of the two faces' nearest own points - the same from
    /// either side, so both faces agree. Where two faces both own a fold's points (their first rows meet) each keeps
    /// its own: the plan's sea rule (SeaBorder, step 2) is what makes those agree. The raw values are read before any
    /// edge is written.</summary>
    void Stitch()
    {
        var n = _s.FineN > 0 ? _s.FineN : 1;
        StitchStores(n, f => f.Ground.Fine?.H, (f, east, south) =>
        {
            if (f.Ground.Fine is { } fine) { var m = 128 * n; for (var k = 0; k <= m; k++) { fine[m, k] = east[k]; fine[k, m] = south[k]; } }
            else f.Ground.SetPlanetEdges(east, south);
        });
        if (_s.FineN > 0)                                                // the smooth stores: one point a cell
            StitchStores(1, f => f.Ground.Smooth?.H, (f, east, south) =>
            {
                if (f.Ground.Smooth is { } sm) for (var k = 0; k <= 128; k++) { sm[128, k] = east[k]; sm[k, 128] = south[k]; }
            });
    }

    /// <summary>One kind of store over every face, n points a cell: the raw lattices read first (a copy each), then
    /// each face's east and south edge lines handed to write.</summary>
    void StitchStores(int n, Func<Face, int[]?> rawOf, Action<Face, int[], int[]> write)
    {
        var raw = Faces.Select(f => rawOf(f) is { } h ? (int[])h.Clone() : null).ToArray();
        var m = 128 * n;                                                 // the last own index is m - 1; m is past the edge
        var cell = 512 / n;
        int Raw(int f, int i, int j)                                     // the face's own lattice, n points a cell
        {
            if (raw[f] is { } store) return store[j * (m + 1) + i];
            return Faces[f].Terrain.Map[j * Terrain.RowBytes + i * 2] << 4;   // no store: n = 1, the map byte << 4
        }
        int Value(int f, int i, int j)                                   // my point (i, j) with i == m or j == m
        {
            var (g, x, z, _) = Rules.Faces.Cross(f, i * cell, j * cell);
            int gi = x / cell, gj = z / cell;
            if (gi < m && gj < m) return Raw(g, gi, gj);                                         // the neighbour's own
            var mine = Raw(f, Math.Min(i, m - 1), Math.Min(j, m - 1));                           // no owner: the mean
            var theirs = Raw(g, Math.Min(gi, m - 1), Math.Min(gj, m - 1));
            return (mine + theirs) / 2;
        }
        for (var f = 0; f < Rules.Faces.Count; f++)
        {
            var east = new int[m + 1]; var south = new int[m + 1];
            for (var k = 0; k <= m; k++) { east[k] = Value(f, m, k); south[k] = Value(f, k, m); }
            south[m] = east[m];                                          // the corner once: the east crossing's
            write(Faces[f], east, south);
        }
    }

    // ------------------------------------------------------------------ the frame switch (step 3)

    /// <summary>THE ZONE (kept to the zone by the fiction, "Turned back at the line"): the faces the
    /// player may cross onto. The played world and THE SAND ZONE to begin with (a first pick: the campaign opens
    /// faces); at any other fold he is turned back.</summary>
    public readonly HashSet<int> Open = [WorldFace, SandZone.Face];
    /// <summary>How many times the game changed face, and how many times the player was turned back at a line.</summary>
    public int Entered { get; private set; }
    public int TurnedBack { get; private set; }

    /// <summary>THE FRAME SWITCH: the game goes to another face, the player at (x, z) of it with his heading turned by
    /// `turn` quarter turns. Every other live thing goes back to the map first, in the old frame (the write-back
    /// sends one past a fold to its own face) - shots in flight are dropped; the spawner wakes what is near on the
    /// new face on the next pass. Then the state's world IS the new face: its terrain, ground, grid and roomy
    /// extras (each face keeps its own, by reference), the planet's frame, the camera placed again, the map's marks.
    /// One player only. Saves carry it (PlanetSave, step 5). Not carried yet: the building fuel extras.</summary>
    public void Enter(int face, int x, int z, int turn = 0)
    {
        var s = _s;
        var p = s.Entities.All[0];
        for (var n = s.Players; n < s.Entities.Count; n++)
        {
            var e = s.Entities.All[n];
            if (e.B[0] != 0 && e.W(2) != 0) Objects.WriteBack(s, e, 0x1D - n);
            e.B[0] = 0;
        }
        var f = Faces[face];
        (s.Terrain, s.Ground, s.Grid, s.FuelLow, s.Overflow) = (f.Terrain, f.Ground, f.Grid, f.FuelLow, f.Overflow);
        Current = face;
        p.SetW(0x4A, x); p.SetW(0x4E, z);
        p.B[0x7E] = (byte)(p.B[0x7E] + turn * 64);
        p.SetW(0x4C, Math.Max(p.W(0x4C), s.Ground.Surface((ushort)x, (ushort)z, short.MaxValue)));   // never under the new ground (STUNTIN': on a road's top)
        (_ax, _az) = (_px, _pz) = (x, z);
        s.Camera = s.Ground.Place(p.UW(0x4A), p.W(0x4C), p.UW(0x4E), s.Distance, s.Lift);
        s.Log.BuildMarks(s.Terrain);
        Entered++;
    }

    /// <summary>The player was put somewhere by hand (a start, a test, an arrival): his words as they are become the anchor.</summary>
    public void Anchor()
    {
        var p = _s.Entities.All[0];
        (_ax, _az) = (_px, _pz) = (p.UW(0x4A), p.UW(0x4E));
    }

    /// <summary>Once a pass, after the player has moved (Play.EndOfPass): his words are the original's 16 bits, so
    /// walking past an edge WRAPS them - seen here as the step from his last position inside the face. Past an
    /// edge: onto the neighbour (Enter, at the crossed point, his heading turned with the fold) when that face is
    /// open, else turned back at the line - put where he last stood inside, facing the way he came. True: the
    /// frame switched this pass.</summary>
    public bool Pass()
    {
        var p = _s.Entities.All[0];
        if (_s.Players > 1 || !p.Alive) return false;
        int nx = _px + (short)(p.UW(0x4A) - _px), nz = _pz + (short)(p.UW(0x4E) - _pz);
        if ((uint)nx < Rules.Faces.Width && (uint)nz < Rules.Faces.Width) { (_px, _pz) = (nx, nz); WormPass(); return false; }
        var (g, lx, lz, turn) = Rules.Faces.Cross(Current, nx, nz);
        p.SetW(0x4A, _px); p.SetW(0x4E, _pz);                               // back inside: the old frame's anchor for the write-backs
        if (!Open.Contains(g))
        {
            p.B[0x7E] = (byte)(p.B[0x7E] + 128);
            TurnedBack++;
            return false;
        }
        Enter(g, Math.Clamp(lx, 0, 65535), Math.Clamp(lz, 0, 65535), turn);
        return true;
    }

    // ------------------------------------------------------------------ the sand zone's worm

    /// <summary>THE SAND ZONE's worm while the game is on the sand face (null anywhere else, and between worms).</summary>
    public Sandworm? Worm { get; private set; }
    /// <summary>A worm that has just left (its last meal taken), kept while it goes back down to its cruising depth - it hunts
    /// nothing; the picture draws it till it is deep.</summary>
    public Sandworm? Departing { get; private set; }
    /// <summary>Things the worms have taken in this game.</summary>
    public int WormMeals { get; private set; }
    /// <summary>A worm rises this far from the player, on sand (12 map cells); the next comes this many passes after one has
    /// left (a minute of play). First picks, to tune.</summary>
    public const int WormRises = 12 * 512, WormReturns = 3000;
    int _wormAway;
    readonly Dictionary<int, (int X, int Z)> _was = [];

    /// <summary>Once a pass on the sand face: a worm rises on sand away from the player, hunts as Dune II's (Sandworm) over
    /// the live things of this face - a thing's speed is how far it moved since the last pass - and what it comes up under is
    /// swallowed whole: the record freed with no wreck, its sound stopped; the player's own record loses its energy and
    /// the game ends him its own way. Three taken or driven off, it is gone and another comes after a while.</summary>
    void WormPass()
    {
        var s = _s;
        if (Faces[Current].Sand is not { } zone) { Worm = null; Departing = null; _was.Clear(); _wormAway = 0; return; }
        var all = s.Entities.All;
        if (Departing is { } d)                                             // a worm that has left backs down and sinks, then it is gone
        {
            d.Step([], zone);
            if (!d.Backing && d.Depth >= Sandworm.CruiseDepth) Departing = null;
        }
        if (Worm is { Gone: true }) { Departing = Worm; Worm = null; _wormAway = WormReturns; }
        if (Worm is null)
        {
            if (_wormAway > 0) { _wormAway--; return; }
            foreach (var (dx, dz) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (-1, -1), (1, -1) })
            {
                int x = _px + dx * WormRises, z = _pz + dz * WormRises;
                if ((uint)x >= Rules.Faces.Width || (uint)z >= Rules.Faces.Width || !zone.IsWormSand(x, z)) continue;
                Worm = new Sandworm(x, z);
                break;
            }
            if (Worm is null) return;                                       // no sand out there: it waits
        }
        var prey = new List<Sandworm.Prey>();
        for (var n = 0; n < s.Entities.Count; n++)
        {
            var e = all[n];
            if (e.B[0] == 0 || e.W(2) == 0) { _was.Remove(n); continue; }
            int x = e.UW(0x4A), z = e.UW(0x4E);
            if (Beyond((ushort)x, (ushort)z) is not null) continue;        // past a fold: not this face's sand
            var weight = Sandworm.WeightOf(e.W(4));
            var speed = _was.TryGetValue(n, out var was) ? Sandworm.Units(was.X, was.Z, x, z) : 0;
            _was[n] = (x, z);
            if (weight > 0) prey.Add(new(n, x, z, weight, speed));
        }
        if (Worm.Step(prey, zone) is not { } taken) return;
        WormMeals++;
        var victim = all[taken];
        if (taken < s.Players) victim.SetW(0x2A, -1);                       // the player: no energy left, the game's own end
        else
        {
            s.Channels.Set(1, victim.W(0x98), 0);                           // its sound stops ($82A8's way)
            victim.B[0] = 0;                                                // swallowed whole: no wreck
        }
        _was.Remove(taken);
    }

    // ------------------------------------------------------------------ the frame

    /// <summary>A word pair of the current frame unfolded against the player: the player's last live position is the
    /// anchor (both players' with two - the nearer), the words' signed difference from it added back - so a thing just
    /// past a fold comes out past 65535 or below 0, where it is.</summary>
    public (int X, int Z) Unfold(ushort x, ushort z)
    {
        var p = _s.Entities.All[0];
        if (p.Alive) (_ax, _az) = (p.UW(0x4A), p.UW(0x4E));
        int ax = _ax, az = _az;
        if (_s.Players > 1 && _s.Entities.All[1].Alive)
        {
            var q = _s.Entities.All[1];
            int bx = q.UW(0x4A), bz = q.UW(0x4E);
            if (Math.Abs((short)(x - bx)) + Math.Abs((short)(z - bz)) < Math.Abs((short)(x - ax)) + Math.Abs((short)(z - az)))
                (ax, az) = (bx, bz);
        }
        return (ax + (short)(x - ax), az + (short)(z - az));
    }

    /// <summary>The face and local words a point of the current frame is on, or null when it is on the current face.</summary>
    public (Face Face, ushort X, ushort Z, int Turn)? Beyond(ushort x, ushort z)
    {
        var (ux, uz) = Unfold(x, z);
        if ((uint)ux < Rules.Faces.Width && (uint)uz < Rules.Faces.Width) return null;
        var (g, lx, lz, turn) = Rules.Faces.Cross(Current, ux, uz);
        return (Faces[g], (ushort)Math.Min(lx, 65535), (ushort)Math.Min(lz, 65535), turn);   // the fold line itself: the last word before it
    }

    /// <summary>The ground a point of the current frame is read from, with the point in that ground's own words.</summary>
    public (Ground Ground, ushort X, ushort Z) Route(ushort x, ushort z)
    {
        var b = Beyond(x, z);
        return b is null ? (Faces[Current].Ground, x, z) : (b.Value.Face.Ground, b.Value.X, b.Value.Z);
    }

    /// <summary>A window cell past the current face's edges (cx or cz outside 0..15): the face and grid cell it is on,
    /// and the fold back - a record there is spawned with its words unfolded into the current frame and its heading
    /// turned back (Unfold: the whole path of folds, so a window cell past a cube corner comes right too).</summary>
    public (Face Face, int Cell, Action<byte[]> Unfold) CellBeyond(int cx, int cz)
    {
        int ux = cx * 4096 + 2048, uz = cz * 4096 + 2048;
        var (g, lx, lz, _) = Rules.Faces.Cross(Current, ux, uz);
        var path = Rules.Faces.CrossPath(Current, ux, uz);
        var cell = ((lz >> 12) * 16 + (lx >> 12)) * 128;
        return (Faces[g], cell, rec =>
        {
            int x = rec[6] << 8 | rec[7], z = rec[10] << 8 | rec[11];
            var (fx, fz, turn) = Rules.Faces.UnfoldPath(path, x, z);
            rec[6] = (byte)(fx >> 8); rec[7] = (byte)fx;
            rec[10] = (byte)(fz >> 8); rec[11] = (byte)fz;
            rec[0x0C] = (byte)(rec[0x0C] - turn * 64);
        });
    }
}
