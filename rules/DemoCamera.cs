namespace Hunter.Rules;

/// <summary>NEW, NOT THE ORIGINAL (DEMO CAMERA: MOVING, owner 2026-11-02: a smart camera, not circling): the attract
/// demo's camera picks its own shot from what happens. Every shot is the game's own camera turned round the player
/// (View3D.Eye: the same distance and height - the owner's camera rule, near the original view; his one exception:
/// in the car the eye moves in, Scale), so
/// only where it looks changes. Shots, the first that applies:
///   Blast  - an explosion near him: face it, held a moment
///   Shot   - a shell or missile in flight near him: follow it
///   Group  - passing something worth seeing (a herd, boats, a squadron): bring it into the picture, once a group
///   Chase  - moving: swing round behind him
///   North  - standing still: the original view
/// Calm (owner 2026-11-02: the first try was sickening): a pan speeds up and slows down gently and never passes
/// MaxTurn (a film pan's rule: a frame width in some seconds; 40 a second is where the eye stops following smoothly);
/// a shot plays at least MinShot before another takes over; a subject further round than Reach is let go; the chase
/// follows his heading smoothed over seconds and only re-aims past Deadband. A pan that would take the eye into the
/// land or a thing stops where it is; if he moves so that the eye is in one, it cuts to the nearest clear turn (Cuts).
/// The view only: the rules never see it. DemoCameraTests walks the whole demo with it.</summary>
public sealed class DemoCamera
{
    public enum Shot { North, Chase, Group, Flight, Blast, Fire, Hit, Sight, Side }

    // THE SHOWCASE (owner 2026-11-17: "showcase that we now have lifted that limitation but tastefully"): a building or a
    // parked vehicle near his way not shown yet is turned to (Sight, once each, one every SightGap at most, within Reach);
    // a long chase gives way to a look across his way (Side) after SideAfter; the chase leads into a bend by up to
    // LeadMax, a second's turn ahead.
    // Owner 2026-11-18 "The camera is turned too fast and too often" (the plain demo: 55 shots in 5.4 min, a new one every 6 s on
    // the median, 26 under 4 s): the turns at half speed, every shot held twice as long, a sight at most every 45 s.
    const int SightGap = 50 * 45, SideAfter = 50 * 40, SightNear = 1500;
    const double LeadMax = 20 * Math.PI / 180, LeadAhead = 50;

    const int Near = 2500;                                          // world units: what counts as near him
    const int MinShot = 50 * 12, ChaseAfter = 50 * 2, NorthAfter = 50 * 10;   // blanks
    // THE FIGHT (owner 2026-11-12: "the camera should follow aim of weapon fire", "not destroy things off screen when
    // you could rotate view and see it"; 11-17 "showcase that we now have lifted that limitation but tastefully"): a
    // blast near him, a thing near him losing energy, his own fire (bullets too) take over any other shot at once and
    // from all the way round (no Reach); a fight shot holds FightMin before another fight takes it; his fire is
    // followed while he fires and held FireHold after; fight pans may go FightTurn times as fast.
    const int FightMin = 50 * 2, FireHold = 50 * 2;                 // (held longer, the next shot elsewhere went unfollowed: 82 passes off screen)
    const double FightTurn = 3;                                     // fights stay at 60 degrees a second (slower lost kills off screen)
    const double Deg = Math.PI / 180;
    public const double MaxTurn = 20 * Deg / 50;                    // radians a blank: 20 degrees a second (fights: 60)
    const double Speedup = 20 * Deg / 50 / 50;                      // radians a blank, a blank: full speed in a second
    /// <summary>A fight shot: a blast, a hit, his fire, a shell in flight.</summary>
    public bool Fighting => Playing is Shot.Blast or Shot.Hit or Shot.Fire or Shot.Flight;
    const double Reach = 90 * Deg, Deadband = 25 * Deg;
    const int Margin = 256;                                         // world units the pans keep the eye from the land and things
    const double HeadEase = 1.0 / 100;                              // his smoothed heading: 2 s to catch up
    const double CloseBy = 0.55, CloseEase = 1.0 / 100;             // in the car: the eye 55% nearer him, moved in over 2 s

    /// <summary>How far in the car's close-up is, 0 (the game's distance) to 1.</summary>
    public double Close { get; private set; }
    /// <summary>The eye's share of the game's distance: 1, or less in the car's close-up (eased in and out).</summary>
    public double Scale => 1 - CloseBy * Close * Close * (3 - 2 * Close);

    public Shot Playing { get; private set; } = Shot.North;
    /// <summary>Where the shot looks (world x, z); none for North and Chase.</summary>
    public (int X, int Z)? Aim { get; private set; }
    /// <summary>The blanks the shot has played.</summary>
    public int Age => lastBlank - startBlank;
    /// <summary>The cuts so far: the eye found in the land or a thing, put at the nearest clear turn.</summary>
    public int Cuts { get; private set; }
    /// <summary>Of those, the cuts to a fight no pan could reach (the land in the way both ways round).</summary>
    public int FightCuts { get; private set; }
    /// <summary>The view is still turning (a pan under way).</summary>
    public bool Turning => spin != 0;

    double yaw, spin, headX, headZ = 1, chaseAim;
    int startBlank, lastBlank = -1, flightSlot = -1;
    (int X, int Z) lastPlayer;
    readonly HashSet<int> shownGroups = [], knownBlasts = [], knownFlights = [];
    bool first = true;

    /// <summary>The turn (radians, Real3DView.Yaw's way: 0 north, positive left) after this pass of s.</summary>
    public double Yaw(WorldState s)
    {
        var blank = s.BlankCount;
        if (blank == lastBlank) return yaw;                         // once a blank, however often asked
        var steps = first ? 0 : Math.Clamp(blank - lastBlank, 0, 50);
        lastBlank = blank;
        var p = s.Entities.All[0];
        int px = p.UW(0x4A), pz = p.UW(0x4E);
        if (first || blank - checkBlank >= 25)                      // moving: the ground covered each half second
        {                                                           // (not a pass's step: the demo steps at its own pace)
            moving = !first && Math.Abs(px - lastPlayer.X) + Math.Abs(pz - lastPlayer.Z) > 60;
            lastPlayer = (px, pz);
            checkBlank = blank;
        }
        Choose(s, blank, px, pz, moving);
        first = false;

        var head = -p.B[0x7E] * Math.Tau / 256;                     // his facing, (-sin, cos) - Shots' way round
        for (var i = 0; i < steps; i++)
        {
            Close = Math.Clamp(Close + (p.W(4) == 0x01 ? CloseEase : -CloseEase), 0, 1);   // the car (owner: not the helicopter)
            headX += (-Math.Sin(head) - headX) * HeadEase;
            headZ += (Math.Cos(head) - headZ) * HeadEase;
            var smooth = Math.Atan2(-headX, headZ);
            bend += (Wrap(smooth - lastSmooth) * LeadAhead - bend) / 50;   // his turning, a second's worth, smoothed over a second
            lastSmooth = smooth;
            var lead = Math.Clamp(bend, -LeadMax, LeadMax);
            if (Playing != Shot.Chase) chaseAim = smooth;           // a chase starts from where he has been heading
            else if (Math.Abs(Wrap(smooth + lead - chaseAim)) > Deadband) chaseAim = smooth + lead;   // into the bend
            var aim = Playing switch
            {
                Shot.North => 0,
                Shot.Chase => chaseAim,
                Shot.Side => Wrap(smooth + sideWay * Math.PI / 2),   // across his way, from the side
                _ => Facing(Aim!.Value.X - px, Aim.Value.Z - pz),
            };
            Turn(s, aim);
        }
        if (Blocked(s, yaw, 0)) { yaw = Clear(s, yaw); spin = 0; Cuts++; }
        return yaw;
    }

    /// <summary>One blank of the pan toward aim: speed up and slow down by Speedup, never past MaxTurn, stop short
    /// of Margin from the land or a thing (braking ahead of it); an aim within Margin moves to the nearest clear turn.</summary>
    void Turn(WorldState s, double aim)
    {
        double top = Fighting ? MaxTurn * FightTurn : MaxTurn, acc = Fighting ? Speedup * FightTurn : Speedup;   // fights: faster, as gently shaped
        var crowded = Blocked(s, yaw, Margin);
        if (Blocked(s, aim, Margin)) aim = Clear(s, aim, Margin);   // aim at the nearest turn to it clear of the gap
        var left = Wrap(aim - yaw);
        if (Math.Abs(aim - longAim) > 10 * Deg) longWay = false;   // a new aim: the short way first
        if (Fighting && !longWay && Math.Abs(left) > 10 * Deg && !PathClear(s, yaw, left))   // a fight the short way can't reach (a hill on the way)
        {
            longWay = PathClear(s, yaw, left - Math.Sign(left) * Math.Tau);               // round the other side, if that is clear
            if (!longWay && !Blocked(s, aim, Margin)) { yaw = Wrap(aim); spin = 0; Cuts++; FightCuts++; return; }   // neither: cut to it, as a film cuts between cameras
        }
        longAim = aim;
        if (longWay) left -= Math.Sign(left) * Math.Tau;
        var want = Math.Sign(left) * Math.Min(top, acc * (Math.Sqrt(0.25 + 2 * Math.Abs(left) / acc) - 0.5));   // slows to stop on it, a blank at a time
        var stop = Wrap(yaw + spin + Math.Sign(spin) * spin * spin / (2 * acc));   // where braking now would stop
        if (!crowded && spin != 0 && Blocked(s, stop, Margin)) want = 0;   // the gap ahead: brake
        spin += Math.Clamp(want - spin, -acc, acc);
        if (Math.Abs(left) <= Math.Max(Math.Abs(spin), acc) && Math.Abs(spin) <= 2 * acc) { spin = 0; yaw = Wrap(aim); return; }
        var next = Wrap(yaw + spin);
        if (Blocked(s, next, crowded ? 0 : Margin)) { spin = 0; return; }
        yaw = next;
    }

    double longAim;
    bool longWay;

    /// <summary>Every 3 degrees from 'from' round by 'by' keeps the eye clear by Margin.</summary>
    bool PathClear(WorldState s, double from, double by)
    {
        var steps = (int)(Math.Abs(by) / (3 * Deg));
        for (var i = 1; i <= steps; i++) if (Blocked(s, Wrap(from + Math.Sign(by) * i * 3 * Deg), Margin)) return false;
        return true;
    }

    /// <summary>The eye at the view's turn and Scale.</summary>
    public (double X, double Y, double Z) Eye(WorldState s) => EyeAt(s, yaw, Scale);

    /// <summary>The eye turned by yaw, brought toward him to scale of the game's distance (height too, so he stays
    /// where he is on the screen, only bigger).</summary>
    public static (double X, double Y, double Z) EyeAt(WorldState s, double yaw, double scale)
    {
        var e = View3D.Eye(s.Camera, s.Distance, yaw);
        if (scale >= 1) return e;
        double px = s.Camera.G1548 / 2.0, pz = (s.Camera.G1544 - 0x100) / 2.0 + (s.Distance + 0x100) / 2.0;   // the turn's middle (View3D.Eye)
        double py = s.Entities.All[0].W(0x4C);
        return (px + (e.X - px) * scale, py + (e.Y - py) * scale, pz + (e.Z - pz) * scale);
    }

    /// <summary>The eye at turn t stands within margin of the land or a thing - north at the game's distance never
    /// (the game's own camera).</summary>
    bool Blocked(WorldState s, double t, int margin) => !(Math.Abs(t) < 1e-6 && Scale >= 1) && Inside(s, EyeAt(s, t, Scale), margin);

    /// <summary>The turn nearest from whose eye is clear by margin, north (the game's own camera) when none is.</summary>
    double Clear(WorldState s, double from, int margin = 0)
    {
        for (var d = 3 * Deg; d <= Math.PI; d += 3 * Deg)
            foreach (var t in new[] { Wrap(from + d), Wrap(from - d) })
                if (!Blocked(s, t, margin)) return t;
        return 0;
    }

    void Choose(WorldState s, int blank, int px, int pz, bool moving)
    {
        var following = false;
        if (Playing == Shot.Flight && flightSlot >= 0)              // following a shell: until it goes off
        {
            var e = s.Entities.All[flightSlot];
            if (e.Alive && IsFlight(e.W(4))) { Aim = (e.UW(0x4A), e.UW(0x4E)); following = true; }
            else flightSlot = -1;                                   // gone off: its blast is picked up below
        }
        // a calm shot plays MinShot before another calm one takes over; a fight (a new blast near him, a thing near him
        // losing energy, his fire, a new shell in flight) takes over any calm shot at once and from all the way round,
        // and another fight after FightMin (blasts and shells seen meanwhile are marked, so they don't come up later)
        var free = first || blank - startBlank >= MinShot
            // a look at a building or a group ends early once its subject can't be shown (the turn blocked, or too far round):
            // held the whole MinShot it sat on the north view for seconds (11-18, with the longer shots)
            || Playing is Shot.Sight or Shot.Group && Aim is { } sa && (!Reachable(sa.X - px, sa.Z - pz) || Blocked(s, Facing(sa.X - px, sa.Z - pz), Margin));
        var fightFree = first || !Fighting || blank - startBlank >= FightMin;
        if (moving) { stillSince = -1; movingSince = movingSince < 0 ? blank : movingSince; }
        else { movingSince = -1; stillSince = stillSince < 0 ? blank : stillSince; }
        (int, int)? hit = null, blast = null, fire = null;
        int flightAt = -1, hitAt = -1; (int, int) flight = default;
        if (Playing == Shot.Hit && hitSlot >= 0 && s.Entities.All[hitSlot] is { Alive: true } hs)   // the one hit, as it moves
            Aim = (hs.UW(0x4A), hs.UW(0x4E));
        for (var r = 1; r < s.Entities.Count; r++)
        {
            var e = s.Entities.All[r];
            var ty = e.W(4);
            var key = r << 16 | ty;
            if (!e.Alive) { knownBlasts.Remove(key); knownFlights.Remove(key); energy.Remove(r); continue; }
            int ex = e.UW(0x4A), ez = e.UW(0x4E);
            var near = NearHim(ex, ez, px, pz) && !Blocked(s, Facing(ex - px, ez - pz), Margin);   // near, and a turn can show it (11-17: a building beside him behind a hill held every fight shot on nothing)
            int en = (short)e.W(0x2A);
            var dropped = energy.TryGetValue(r, out var was) && was.Type == ty && en < was.Energy;
            energy[r] = (ty, en);
            if (IsBlast(ty) && knownBlasts.Add(key) && near) blast ??= (ex, ez);
            else if (dropped && near && !IsShot(ty) && !IsBlast(ty) && hit is null) { hit = (ex, ez); hitAt = r; }
            if (IsShot(ty) && e.B[0x21] != 0 && near) fire = (ex, ez);   // his own shot ($31AE +$21), bullets too: the latest
            if (IsFlight(ty) && knownFlights.Add(key) && near && flightAt < 0) { flightAt = r; flight = (ex, ez); }
        }
        if (fire is not null) lastFire = blank;
        if (fightFree)
        {
            if (blast is { } b) { Start(Shot.Blast, blank, b); return; }
            if (hit is { } h) { Start(Shot.Hit, blank, h); hitSlot = hitAt; return; }
            if (fire is { } f && Playing != Shot.Fire) { Start(Shot.Fire, blank, f); return; }
            if (flightAt >= 0 && !following && Playing != Shot.Fire) { Start(Shot.Flight, blank, flight); flightSlot = flightAt; return; }
        }
        if (Playing == Shot.Fire)                                   // his fire: where his shots go, held a while after
        {
            if (fire is { } f) Aim = f;
            if (blank - lastFire < FireHold) return;
        }
        if (following) return;
        if (Fighting && blank - startBlank < FightMin) return;      // a fight shot plays its own while
        if (!free && !Fighting) return;
        if (BestGroup(s, px, pz) is { } g)
        {
            shownGroups.Add(g.Cell);
            Start(Shot.Group, blank, (g.X, g.Z));
            return;
        }
        if (blank - lastSight >= SightGap && BestSight(s, px, pz) is { } sight)   // a building or a parked vehicle by his way
        {
            shownSights.Add(sight.Key);
            lastSight = blank;
            Start(Shot.Sight, blank, (sight.X, sight.Z));
            return;
        }
        if (Playing == Shot.Chase && moving && blank - startBlank >= SideAfter)   // a long chase: a look across his way
        {
            sideWay = -sideWay;                                     // the other side each time
            Start(Shot.Side, blank, null);
            return;
        }
        if (Playing == Shot.Side && moving) { Start(Shot.Chase, blank, null); return; }   // after the side look: behind him again
        // patience: chase after a second of moving, back to north after five seconds still (the demo's passes come
        // at its own pace, so a single pass without a step means nothing)
        var want = Playing is Shot.Chase or Shot.North ? Playing : moving ? Shot.Chase : Shot.North;
        if (Playing != Shot.Chase && movingSince >= 0 && blank - movingSince >= ChaseAfter) want = Shot.Chase;
        if (Playing == Shot.Chase && stillSince >= 0 && blank - stillSince >= NorthAfter) want = Shot.North;
        if (Playing != want) Start(want, blank, null);
    }

    bool Reachable(int dx, int dz) => Math.Abs(Wrap(Facing(dx, dz) - yaw)) <= Reach;

    int movingSince = -1, stillSince = -1, checkBlank, lastFire = -1000, hitSlot = -1, lastSight = -SightGap, sideWay = 1;
    double bend, lastSmooth;
    readonly HashSet<int> shownSights = [];

    /// <summary>The nearest building ($95-$AE) or parked vehicle (enterable: an empty one, +$06 bit 28) within SightNear of
    /// him, not shown yet, a turn within Reach: its place and its key (the record's slot and type).</summary>
    (int X, int Z, int Key)? BestSight(WorldState s, int px, int pz)
    {
        (int X, int Z, int Key)? best = null;
        var near = long.MaxValue;
        for (var r = 1; r < s.Entities.Count; r++)
        {
            var e = s.Entities.All[r];
            if (!e.Alive) continue;
            int ty = e.W(4), ex = e.UW(0x4A), ez = e.UW(0x4E);
            var parked = ty <= 0x13 && e.Flag(28);
            if (!(ty is >= 0x95 and <= 0xAE) && !parked) continue;
            var key = ex << 16 ^ ez << 4 ^ ty;                      // where it stands: a record keeps its place while it waits
            long dx = ex - px, dz = ez - pz, d = dx * dx + dz * dz;
            if (d > (long)SightNear * SightNear || d >= near || shownSights.Contains(key) || !Reachable((int)dx, (int)dz)
                || Blocked(s, Facing((int)dx, (int)dz), Margin)) continue;
            near = d;
            best = (ex, ez, key);
        }
        return best;
    }
    bool moving;
    readonly Dictionary<int, (int Type, int Energy)> energy = [];   // each live slot's energy last blank: a drop is a hit

    void Start(Shot k, int blank, (int, int)? aim) { Playing = k; startBlank = blank; Aim = aim; }

    /// <summary>The busiest group of things worth seeing near him and not yet shown: its middle and its cell.</summary>
    (int X, int Z, int Cell)? BestGroup(WorldState s, int px, int pz)
    {
        var near = new List<(int X, int Z)>();
        for (var r = 1; r < s.Entities.Count; r++)
        {
            var e = s.Entities.All[r];
            if (e.Alive && Worth(e.W(4)) && NearHim(e.UW(0x4A), e.UW(0x4E), px, pz)) near.Add((e.UW(0x4A), e.UW(0x4E)));
        }
        (int X, int Z, int Cell)? best = null;
        var most = 2;                                               // a group is three or more together
        foreach (var a in near)
        {
            var cell = (a.Z >> 10) * 64 + (a.X >> 10);
            if (shownGroups.Contains(cell) || !Reachable(a.X - px, a.Z - pz)) continue;
            var mates = near.Where(b => Math.Abs(b.X - a.X) < 800 && Math.Abs(b.Z - a.Z) < 800).ToList();
            if (mates.Count <= most) continue;
            most = mates.Count;
            best = ((int)mates.Average(m => m.X), (int)mates.Average(m => m.Z), cell);
        }
        return best;
    }

    // what is worth a look: tanks, helicopters, the plane, boats, the jeep and armoured car, animals and birds
    static bool Worth(int ty) => ty is 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x0C or 0x0D or 0x0F or 0x13
        or 0x14 or 0x15 or 0x17 or 0x18 or 0x19 or 0x1A or 0x1B or 0x1C;
    static bool IsFlight(int ty) => ty is >= 0x53 and <= 0x5D;      // bazooka shell .. the shells, bombs and missiles
    static bool IsShot(int ty) => ty is >= 0x52 and <= 0x5D;        // the bullet too
    static bool IsBlast(int ty) => ty is >= 0x61 and <= 0x66;       // As .. Gb: the air and ground blasts
    static bool NearHim(int x, int z, int px, int pz) => Math.Abs(x - px) < Near && Math.Abs(z - pz) < Near;

    /// <summary>The turn that looks from him toward (dx, dz): the facing is (-sin, cos).</summary>
    static double Facing(int dx, int dz) => Math.Atan2(-dx, dz);
    static double Wrap(double a) { a %= Math.Tau; if (a > Math.PI) a -= Math.Tau; else if (a < -Math.PI) a += Math.Tau; return a; }

    /// <summary>The eye turned by yaw stands in the land (a hill behind him) or inside a live thing's box
    /// (Play.Buildings' box: x +- $6E, z +- $72, height $4C up to $4C + 2 * $70).
    /// With a margin: that much nearer (the pans keep it, so a cut is rare).</summary>
    public static bool Inside(WorldState s, double yaw, int margin = 0) => Inside(s, View3D.Eye(s.Camera, s.Distance, yaw), margin);

    /// <summary>As above, for an eye already placed.</summary>
    public static bool Inside(WorldState s, (double X, double Y, double Z) eye, int margin = 0)
    {
        // heights are the map's bytes x16, never below 0, and the sea's top is 0: 16 above the land is above the water too
        // (no shot under the water until there is underwater vision, owner 2026-11-02)
        if (eye.Y < s.Ground.Height((ushort)(int)eye.X, (ushort)(int)eye.Z) + 16 + margin) return true;
        for (var r = 1; r < s.Entities.Count; r++)
        {
            var e = s.Entities.All[r];
            if (!e.Alive || e.W(0x72) <= 0 || e.W(0x6E) <= 0) continue;
            if (Math.Abs(e.UW(0x4A) - eye.X) <= e.W(0x6E) + margin && Math.Abs(e.UW(0x4E) - eye.Z) <= e.W(0x72) + margin
                && eye.Y >= e.W(0x4C) - margin && eye.Y <= e.W(0x4C) + 2 * e.W(0x70) + margin) return true;
        }
        return false;
    }
}
