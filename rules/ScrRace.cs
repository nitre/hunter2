namespace Hunter.Rules;

/// <summary>STUNTIN's AI cars: each a car record on the road's face, driven by its own original-physics car (ScrDrive) and
/// an AI driver (ScrBrain). They stay live wherever they are (the far write-back passes them by: they race the whole
/// lap) and nobody can climb into one. The player's own car may be driven by a brain too (Autopilot: the demo).</summary>
public static class ScrRace
{
    /// <summary>The original's drivers as the demo's two styles: its traits bytes (opponentBehaviorTraits, read from the
    /// disk: driver 0 $22 = follow + overtake, driver 8 $4A = aggressive + follow + $40) and cornering skills inside the
    /// Standard league's range for Little Ramp ($41 + 0..7).</summary>
    public static ScrBrain Racer(int cornering = 0x46, int? seed = null) => new("Overtaker", 0, 0x22, cornering, seed);
    public static ScrBrain Blocker() => new("Blocker", 1, 0x4A, 0x43);

    /// <summary>The demo: the player's car on an AI driver (so the view follows a racing car) and one AI car beside it at
    /// the start, a different style. False when there is no road or no physics data.</summary>
    public static bool Demo(WorldState s)
    {
        if (s.Ground.Road is not { } road || s.ScrDrive is null || s.Data is null) return false;
        s.ScrAutopilot = Racer();
        return Add(s, Blocker(), lane: 0xB0, back: 2, type: Jeep) is not null;
    }

    /// <summary>A league race's opponent: the original's driver (its id, name and traits), the cornering skill rolled for the
    /// track (ScrLeague.Cornering) and the race's own seed for its random moves.</summary>
    public sealed record Opponent(int Id, string Name, int Traits, int Cornering, int Seed);

    /// <summary>A league race: the named opponent beside the start, as the demo's computer car; the player drives his own
    /// car (autopilot: a driver drives it for him - checks only). False when there is no road or no physics data.</summary>
    public static bool League(WorldState s, Opponent o, bool autopilot = false)
    {
        if (s.Ground.Road is null || s.ScrDrive is null || s.Data is null) return false;
        if (autopilot) s.ScrAutopilot = Racer(o.Cornering, o.Seed * 7919 + 104729);   // the track's skill, its own dice
        return Add(s, new ScrBrain(o.Name, o.Id, o.Traits, o.Cornering, o.Seed), lane: 0xB0, back: 2, type: Jeep) is not null;
    }

    /// <summary>Hunter's car and jeep types (docs/data/maps): the body an AI car is drawn as - the physics is the same.</summary>
    public const int Car = 0x01, Jeep = 0x0D;

    /// <summary>One AI car at the start: `back` strips behind the spawn, at the lane (0 left .. 255 right), facing round the
    /// lap. Null when no slot is free or the physics data is missing.</summary>
    public static Entity? Add(WorldState s, ScrBrain brain, int lane, int back, int type = Car)
    {
        var surface = s.Ground.Road!;
        var road = surface.Road;
        var n = road.Sections.Count;
        var k = (road.SpawnSection - back + n) % n;
        var sec = road.Sections[k];
        var f = lane / 255.0;
        int x = (int)Math.Round(sec.LX + (sec.RX - sec.LX) * f), z = (int)Math.Round(sec.LZ + (sec.RZ - sec.LZ) * f);
        var y = s.Ground.Surface((ushort)x, (ushort)z, short.MaxValue);
        int slot = -1;
        for (var i = s.Players; i < s.Entities.Count; i++) if (s.Entities.All[i].B[0] == 0) { slot = i; break; }
        if (slot < 0 || ScrDrive.Load(s.Data.Directory) is not { } drive) return null;
        StartState.Construct(s, slot, x, y, z, type, 0, 0);                 // a car or a jeep
        var e = s.Entities.All[slot];
        e.SetW(0x7E, surface.Way(k)); e.HeadingFine = e.UW(0x7E);
        e.SetW(0x4A, x); e.SetW(0x4C, y); e.SetW(0x4E, z); e.SetW(0x50, x); e.SetW(0x52, y); e.SetW(0x54, z);
        e.SetW(0x34, 0x7F00);                                                // a full tank
        s.ScrAi[e] = (drive, brain);
        return e;
    }

    /// <summary>Two cars this close (ground units, between middles) touch: SCR's car is 81 wide and 121 long (the drawn
    /// opponent's footprint), so a little under the mean of the two; and within this much height of each other.</summary>
    public const double TouchApart = 95, TouchHeight = 60;
    /// <summary>How much of the closing speed comes back as they part (0 = they stick, 1 = a perfect bounce).</summary>
    public const double Bounce = 0.3;
    static int _lastBlank = -1;
    static double _kickAcc;
    /// <summary>Passes in which two cars touched (a probe's count).</summary>
    public static int Touches { get; private set; }

    /// <summary>Once per the original's pass (ScrDrive.Rate): every two original-physics cars on the road whose bodies overlap are pushed apart and swap
    /// momentum along the line between their middles (equal masses) - real cars, where the original's opponent was a fake
    /// that only nudged the player's springs. The original's own touch is kept on top (calculateOpponentCollisionForces /
    /// applyOpponentCollisionForces 12709-12843): a kick to each car's roll spring (8, away
    /// from the other) and yaw spring (half the speed difference, +-3), and damage to all three corners of each, (0x200 + the
    /// kicks) >> 8. The overlap is measured between the records (both placed for this pass; each car's physics steps on its
    /// own beat, so two physics places are from different moments). A car on the crane touches nothing.</summary>
    public static void Collide(WorldState s)
    {
        if (s.BlankCount == _lastBlank) return;
        _lastBlank = s.BlankCount;
        _kickAcc += ScrDrive.Rate;
        var kick = _kickAcc >= 1;
        if (kick) _kickAcc -= 1;
        var cars = new List<ScrDrive>();
        var p = s.Entities.All[0];
        if (s.ScrDrive is { } mine && mine.Entity == p && ScrDrive.Takes(s, p, 0) && !mine.Wrecked && !mine.Hanging) cars.Add(mine);
        foreach (var (e, ai) in s.ScrAi) if (e.B[0] != 0 && !ai.Drive.Wrecked && !ai.Drive.Hanging) cars.Add(ai.Drive);
        for (var i = 0; i < cars.Count; i++)
            for (var j = i + 1; j < cars.Count; j++)
                Touch(s, cars[i], cars[j], kick);
    }

    static double Ground(int world) => (world - ScrRoad.Offset) / ScrRoad.Scale;

    static void Touch(WorldState s, ScrDrive da, ScrDrive db, bool kick)
    {
        if (da.Entity is not { } ea || db.Entity is not { } eb) return;
        ScrCar a = da.Car, b = db.Car;
        double ax = Ground(ea.UW(0x4A)), az = Ground(ea.UW(0x4E)), ay = (a.L(ScrCar.cameraWorldY) >> 8) / 32.0;
        double bx = Ground(eb.UW(0x4A)), bz = Ground(eb.UW(0x4E)), by = (b.L(ScrCar.cameraWorldY) >> 8) / 32.0;
        double dx = bx - ax, dz = bz - az, d = Math.Sqrt(dx * dx + dz * dz);
        if (d >= TouchApart || Math.Abs(by - ay) >= TouchHeight || d < 1e-6) return;
        if (!kick) return;                                                   // the push, the momentum and the kicks: once per its pass
        Touches++;
        double nx = dx / d, nz = dz / d;
        // apart: half the overlap each, along the line between them
        var push = (TouchApart - d) / 2 * 4096;
        a.L(ScrCar.cameraWorldX, (int)Math.Round(a.L(ScrCar.cameraWorldX) - nx * push)); a.L(ScrCar.cameraWorldZ, (int)Math.Round(a.L(ScrCar.cameraWorldZ) - nz * push));
        b.L(ScrCar.cameraWorldX, (int)Math.Round(b.L(ScrCar.cameraWorldX) + nx * push)); b.L(ScrCar.cameraWorldZ, (int)Math.Round(b.L(ScrCar.cameraWorldZ) + nz * push));
        // momentum: the closing speed along the line, shared (equal masses), a share of it back as they part
        double vax = a.W(ScrCar.worldXSpeed), vaz = a.W(ScrCar.worldZSpeed), vbx = b.W(ScrCar.worldXSpeed), vbz = b.W(ScrCar.worldZSpeed);
        var closing = (vbx - vax) * nx + (vbz - vaz) * nz;
        if (closing < 0)
        {
            var jn = -(1 + Bounce) * closing / 2;
            a.W(ScrCar.worldXSpeed, Clamp(vax - jn * nx)); a.W(ScrCar.worldZSpeed, Clamp(vaz - jn * nz));
            b.W(ScrCar.worldXSpeed, Clamp(vbx + jn * nx)); b.W(ScrCar.worldZSpeed, Clamp(vbz + jn * nz));
        }
        Console.WriteLine($"SCRTRACE {s.BlankCount} touch segs {a.B(ScrCar.playerSegmentIndex)}/{b.B(ScrCar.playerSegmentIndex)} apart {d:0.0} closing {-closing:0} vz {a.W(ScrCar.carLocalVelocityZ)}/{b.W(ScrCar.carLocalVelocityZ)}");
        // the springs, the damage and the crunch: each car's own original touch (ScrCarCollide)
    }

    static short Clamp(double v) => (short)Math.Clamp((int)Math.Round(v), -0x7FFF, 0x7FFF);

    /// <summary>Every original-physics car racing on the road: the player's (when it drives one) and the AI cars'.</summary>
    public static IEnumerable<ScrDrive> Drives(WorldState s)
    {
        var p = s.Entities.All[0];
        if (s.ScrDrive is { } mine && mine.Entity == p && ScrDrive.Takes(s, p, 0)) yield return mine;
        foreach (var (e, ai) in s.ScrAi) if (e.B[0] != 0) yield return ai.Drive;
    }

    /// <summary>The other cars a driver races against: every AI car and the player's car, but itself.</summary>
    public static IEnumerable<Entity> Others(WorldState s, Entity me)
    {
        var p = s.Entities.All[0];
        if (p != me && p.W(4) == Car && s.ScrDrive is not { Wrecked: true }) yield return p;
        foreach (var (e, ai) in s.ScrAi) if (e != me && e.B[0] != 0 && !ai.Drive.Wrecked) yield return e;
    }
}
