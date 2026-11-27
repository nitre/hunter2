namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (THE SAND ZONE's worm, docs/planet-plan.md; it behaves as Dune
/// II's, "Rock is safe yes", "All good"): Shai-Hulud's brain, read from OpenDUNE's code (github.com/OpenDUNE/OpenDUNE, the
/// open rebuild of the PC game whose tables match the Amiga's - research/dune2/amiga-tables.md) and checked there 11-11:
/// - WHAT IT HUNTS - NOT DUNE II'S HERE: the SHAKE in the sand. Only things standing on SAND. Each kind has a weight of ours (WeightOf - the game has
///   none: its +$16 is a fall rate) and its shake is the weight, raised by how fast it is going (Shake). That is divided by its
///   distance in map cells and doubled inside two cells, as Dune II does with its movement scores (on foot 100, tracked
///   1000, wheeled 5000, times 4 when moving - which were game balance, not weight). The best wins, the later of two equals
///   (the loop's &gt;=); 0 is no target. So it comes for the heaviest, fastest thing on the sand nearest it: tanks first.
/// - WHERE IT GOES (table/landscapeinfo.c, the slither column): sand, dunes and the harvest fields at the same speed; ROCK
///   OF EVERY KIND AT 0 - partial rock too - and mountains, slabs, walls: it cannot enter them. A thing on rock is never
///   scored and never reached.
/// - THE SWALLOW (script/unit.c, case UNIT_SANDWORM): the target is removed whole - no damage, no wreck - the worm's
///   count goes down one and it rests 12 script ticks. It starts with 3 (pool/unit.c: amount = 3; 4 before version 1.07)
///   and leaves when none is left.
/// - HALF ITS HIT POINTS (unit.c Unit_Damage: `if (hitpoints >= max / 2) return; if (type == SANDWORM) SetAction(DIE)`): 1000 hit
///   points; the hit that takes it UNDER 500 ends it - Dune II gives it the die action there and it is gone from the map,
///   so half its hit points is all it has. Whether ours dies or is driven off is the picture's choice; here it is Gone.
/// Positions are the game's world units on the sand face (512 a map cell). The worm is not one of the original's records:
/// it is this object beside them, as VultureGraves' posts are. Planet.Pass runs it while the game is on the sand face
/// (Planet.WormPass). Hunter's own numbers (Speed, Reach, Rest and the strike's times) are a first pick to tune in play.
/// HUNTER'S OWN - DEPTH AND THE STRIKE:
/// - IT MOVES FOR REAL, its head in three dimensions: the
///   head has a Depth under the sand as well as a place. It cruises deep (CruiseDepth: no sign), comes up near the surface
///   (ShallowDepth: its sign shows) as it closes on its target, and once under the target it RISES (RiseSpeed a pass), still
///   following it at Speed. When its mouth reaches the surface it takes the best thing within Reach of where it is - so
///   whatever outruns it while it rises gets away - and BACKS DOWN the way it came (BackSpeed), its body sliding back down the
///   hole, until it is under its shallow depth again. The head's real path is kept (Trail), so the picture can lay the body
///   along where the head has been, whichever way it goes along it. The novel's way of it
///   (research/dune2/novel-reference.md): the sand gives way over the rising head and the prey goes down into the mouth.
/// </summary>
public sealed class Sandworm
{
    public const int FullHitPoints = 1000;      // unitinfo.c hitpoints
    public const int Meals = 3;                 // pool/unit.c amount (version 1.07 on)
    public const int Rest = 12;                 // script->delay after a swallow, in passes here
    public const int Speed = 10;                // world units a pass: slower than any vehicle at speed (a car's 21)
    public const int Reach = 256;               // half a cell: under the thing
    public const int ShallowCells = 8;          // it comes up near the surface when its target is this close (map cells)
    public const int CruiseDepth = 800, ShallowDepth = 420;   // the head's depth under the sand travelling (world height units):
                                                             // near the surface its back (the body is 340 round) is still under
    public const int ClimbSpeed = 10, RiseSpeed = 8, SinkSpeed = 10; // height a pass: going up or down between depths, rising under
                                                                     // its target (420 to the surface in about a second), going back down
    public const int BackSpeed = 16;            // after a meal it backs down its own path this far a pass (the picture's units)
    public const int TrailStep = 30, TrailLength = 64;   // the head's path: a point every TrailStep of its travel (the picture's model
                                                         // units: x and z twice the world's), the last TrailLength points

    /// <summary>A thing the worm could take: where it is, its weight (WeightOf; 0 is never taken) and how fast it is going
    /// (world units moved since the last pass).</summary>
    public readonly record struct Prey(int Id, int X, int Z, int Weight, int Speed);

    /// <summary>How hard a thing standing still shakes the sand against one going: shake = weight x (StillShake + speed) /
    /// StillShake - a car at its 21 a pass shakes over six times what it does parked. A first pick, to tune.</summary>
    public const int StillShake = 4;

    /// <summary>A kind's weight in kilograms, ours to tune (a kind's number: the record's +$04 low byte, rules/tests/
    /// DebrisExpected.cs): the two tanks far heaviest (tracked way heavier than wheels), then the wheels - the
    /// armoured car, truck, the vans, the amphibious car, car and jeep, the bicycle with its rider; every person ($1E-$2F) and
    /// the land animals (cow, bull, rabbit, hedgehog). 0 - what flies, floats or hovers, shots, items, buildings - is never
    /// taken. The harvester (ExtraKinds) on tracks.</summary>
    public static int WeightOf(int kind) => kind switch
    {
        0x03 or 0x04 => 40000,          // the tanks
        ExtraKinds.Harvester => 30000,  // the harvester: on tracks, loaded (ExtraKinds)
        0x0F => 10000,                  // armoured car
        0x00 => 8000,                   // truck
        0x02 or 0x0B => 3000,           // ambulance, scanner van
        0x12 => 2000,                   // the amphibious car
        0x0D => 1500,                   // jeep
        0x01 => 1200,                   // car
        0x11 => 90,                     // bicycle and rider
        >= 0x1E and <= 0x2F => 80,      // a person
        0x1B => 900,                    // bull
        0x1A => 600,                    // cow
        0x15 => 2,                      // rabbit
        0x1D => 1,                      // hedgehog
        _ => 0,
    };

    /// <summary>How hard a thing shakes the sand: its weight, raised by its speed.</summary>
    public static long Shake(Prey p) => (long)p.Weight * (StillShake + Math.Max(0, p.Speed)) / StillShake;

    public int X, Z;
    /// <summary>How deep its head (the mouth's lip) is under the sand, world height units; 0 or less: at the surface.</summary>
    public int Depth { get; private set; } = CruiseDepth;
    public int HitPoints = FullHitPoints;
    public int Left = Meals;
    /// <summary>It has left: three taken, or driven off. It goes back down and hunts nothing more.</summary>
    public bool Gone { get; private set; }
    int rest;
    /// <summary>It is under its target and coming up.</summary>
    public bool Rising { get; private set; }
    /// <summary>It has eaten and is going back down the way it came.</summary>
    public bool Backing { get; private set; }
    /// <summary>The thing it is after (its Prey.Id: the record's slot), -1 none.</summary>
    public int Target { get; private set; } = -1;
    /// <summary>Passes since it last took something (int.MaxValue: never).</summary>
    public int SinceMeal { get; private set; } = int.MaxValue;
    /// <summary>It moved this pass (along the sand).</summary>
    public bool Moved { get; private set; }
    /// <summary>The head's path, oldest first: places (world x, z) and depths, a point every TrailStep of its travel.</summary>
    public readonly List<(int X, int Z, int Depth)> Trail = [];
    byte heading;

    public Sandworm(int x, int z) => (X, Z) = (x, z);

    /// <summary>All of it as bytes, for a save (PlanetSave): where it is, how deep, its hit points and meals left, what it is
    /// doing (rest, rising, backing, gone), its target, the passes since its last meal, its heading and its whole path.</summary>
    public byte[] Save()
    {
        var b = new List<byte>();
        void Four(int v) { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }
        Four(X); Four(Z); Four(Depth); Four(HitPoints); Four(Left); Four(rest); Four(Target); Four(SinceMeal);
        b.Add((byte)((Gone ? 1 : 0) | (Rising ? 2 : 0) | (Backing ? 4 : 0) | (Moved ? 8 : 0))); b.Add(heading);
        Four(Trail.Count);
        foreach (var (tx, tz, td) in Trail) { Four(tx); Four(tz); Four(td); }
        return [.. b];
    }

    /// <summary>A worm back from Save's bytes.</summary>
    public static Sandworm Load(byte[] b)
    {
        var p = 0;
        int Four() { var v = b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3]; p += 4; return v; }
        var w = new Sandworm(Four(), Four()) { Depth = Four(), HitPoints = Four(), Left = Four() };
        w.rest = Four(); w.Target = Four(); w.SinceMeal = Four();
        var flags = b[p++]; w.heading = b[p++];
        (w.Gone, w.Rising, w.Backing, w.Moved) = ((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, (flags & 8) != 0);
        for (var n = Four(); n > 0; n--) w.Trail.Add((Four(), Four(), Four()));
        return w;
    }

    /// <summary>Distance in map cells, rounded to the nearest (Tile_GetDistanceRoundedUp: the longer side plus half the shorter).</summary>
    public static int Cells(int x0, int z0, int x1, int z1)
    {
        int dx = Math.Abs(x1 - x0), dz = Math.Abs(z1 - z0);
        return (Math.Max(dx, dz) + Math.Min(dx, dz) / 2 + 256) >> 9;
    }

    /// <summary>Distance in world units, the same way (the longer side plus half the shorter): how far a thing went in a pass.</summary>
    public static int Units(int x0, int z0, int x1, int z1)
    {
        int dx = Math.Abs(x1 - x0), dz = Math.Abs(z1 - z0);
        return Math.Max(dx, dz) + Math.Min(dx, dz) / 2;
    }

    /// <summary>The score of one thing (0: not a target): its shake over its distance in cells, doubled inside two.</summary>
    public long Priority(Prey p, SandZone ground)
    {
        if (!ground.IsWormSand(p.X, p.Z)) return 0;
        var res = Shake(p);
        var distance = Cells(X, Z, p.X, p.Z);
        if (distance != 0) res /= distance;
        if (distance < 2) res *= 2;
        return res;
    }

    /// <summary>The best target, or null.</summary>
    public Prey? Best(IEnumerable<Prey> things, SandZone ground)
    {
        Prey? best = null;
        var bestScore = 0L;
        foreach (var p in things)
        {
            var score = Priority(p, ground);
            if (score >= bestScore) { best = p; bestScore = score; }
        }
        return bestScore == 0 ? null : best;
    }

    /// <summary>One pass: rest after a meal, else go for the best target under the sand and take it when under it. Returns
    /// the Id of the thing swallowed this pass (the caller removes it whole), or null.</summary>
    public int? Step(IEnumerable<Prey> things, SandZone ground)
    {
        Moved = false;
        if (SinceMeal < int.MaxValue) SinceMeal++;
        if (rest > 0) rest--;
        if (Backing)                                                     // after a meal: back down the way it came, its body sliding
        {                                                                // back down the hole behind it - its path walked backwards
            (Rising, Target) = (false, -1);
            BackDown();
            if (Depth >= ShallowDepth || Trail.Count < 2) Backing = false;
            return null;
        }
        if (Gone || rest > 0)                                            // gone, or resting: down where it is
        {
            (Rising, Target) = (false, -1);
            Toward(CruiseDepth, SinkSpeed);
            return Mark(null);
        }
        if (Best(things, ground) is not { } t) { (Rising, Target) = (false, -1); Toward(CruiseDepth, ClimbSpeed); return Mark(null); }
        Target = t.Id;
        Rising = Math.Max(Math.Abs(t.X - X), Math.Abs(t.Z - Z)) <= Reach;
        if (Rising) Toward(-1, RiseSpeed);                               // under it: up, mouth first
        else Toward(Cells(X, Z, t.X, t.Z) <= ShallowCells ? ShallowDepth : CruiseDepth, ClimbSpeed);
        Go(t.X, t.Z, ground);                                            // after it, under the sand, whatever it does
        if (!Rising || Depth > 0) return Mark(null);
        // the mouth is at the surface: the best thing within reach of it, or nothing - it got away
        var near = things.Where(p => Math.Max(Math.Abs(p.X - X), Math.Abs(p.Z - Z)) <= Reach);
        var caught = Best(near, ground);
        Rising = false;
        if (caught is null) return Mark(null);
        Left--;
        rest = Rest;
        SinceMeal = 0;
        Backing = true;
        if (Left < 1) Gone = true;
        return Mark(caught.Value.Id);
    }

    /// <summary>A step back along its own path: toward the newest point of the trail by BackSpeed (the picture's units), the
    /// point dropped once reached - so the body goes back the way it came.</summary>
    void BackDown()
    {
        var budget = (double)BackSpeed;
        while (budget > 0 && Trail.Count > 1)
        {
            var (tx, tz, td) = Trail[^1];
            if ((tx, tz, td) == (X, Z, Depth)) { Trail.RemoveAt(Trail.Count - 1); continue; }
            double dx = 2.0 * (tx - X), dz = 2.0 * (tz - Z), dy = td - Depth, len = Math.Sqrt(dx * dx + dz * dz + dy * dy);
            if (len <= budget) { (X, Z, Depth) = (tx, tz, td); Trail.RemoveAt(Trail.Count - 1); budget -= len; continue; }
            var t = budget / len;
            (X, Z, Depth) = (X + (int)Math.Round(dx * t / 2), Z + (int)Math.Round(dz * t / 2), Depth + (int)Math.Round(dy * t));
            budget = 0;
        }
    }

    void Toward(int depth, int speed) => Depth = Depth < depth ? Math.Min(depth, Depth + speed) : Math.Max(depth, Depth - speed);

    /// <summary>A step of Speed toward (tx, tz) through sand only: straight if the next spot is sand, else along one axis, else
    /// it waits at the rock's edge.</summary>
    void Go(int tx, int tz, SandZone ground)
    {
        int dx = tx - X, dz = tz - Z;
        var far = Math.Max(Math.Abs(dx), Math.Abs(dz));
        if (far == 0) return;
        var step = Math.Min(Speed, far);
        int sx = dx * step / far, sz = dz * step / far;
        foreach (var (mx, mz) in new[] { (sx, sz), (Math.Sign(dx) * step, 0), (0, Math.Sign(dz) * step) })
        {
            if (mx == 0 && mz == 0) continue;
            if (!ground.IsWormSand(X + mx, Z + mz)) continue;
            heading = Wish.Toward(X, Z, X + mx, Z + mz);
            (X, Z) = (X + mx, Z + mz);
            Moved = true;
            break;
        }
    }

    /// <summary>The head's path: a point when it has gone TrailStep (in the picture's units: x and z twice the world's).</summary>
    int? Mark(int? taken)
    {
        if (Trail.Count == 0) Trail.Add((X, Z, Depth));
        var (lx, lz, ld) = Trail[^1];
        double dx = 2.0 * (X - lx), dz = 2.0 * (Z - lz), dy = Depth - ld;
        if (dx * dx + dz * dz + dy * dy >= TrailStep * TrailStep)
        {
            Trail.Add((X, Z, Depth));
            if (Trail.Count > TrailLength) Trail.RemoveAt(0);
        }
        return taken;
    }

    /// <summary>A hit: under half its hit points it leaves.</summary>
    public void Hurt(int damage)
    {
        if (Gone) return;
        HitPoints -= damage;
        if (HitPoints < FullHitPoints / 2) Gone = true;
    }
}
