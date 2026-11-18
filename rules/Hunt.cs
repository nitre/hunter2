namespace Hunter.Rules;

/// <summary>(the BOT step 3, docs/bot-plan.md): the brain that plays a mission's destroy
/// objectives. It reads what a player reads - the log book's unlocked places (NoteObjectives $D0CA notes every target there)
/// and the briefing's kill list (the mission record's objective words, $9A08) against the kills so far ($9288, as MissionCheck
/// $9978 counts them) - and what a soldier would see at a place: the live things there. Nearest open place first; walks there
/// (GoTo), picks the open target nearest the place, and lines up on it: on foot the stick faces only 8 ways (Controls.FootHeading,
/// the view turn added), so it walks to a standoff point straight down the nearest of them from the target, Reach plus the
/// target's half width short of it, taps the way to face it and holds fire (the shot leaves along +$7E, Weapons.Fire) until it
/// ends. A place with no open target left is done. All objectives met: back to the HQ ($AA), where the game is handed in.
/// WEAPONS: each target gets the weapon Arms.Choose
/// picks for its energy left (+$2A), changed to with the Return key (NextWeaponKey $3538, round the carried ones) as a player
/// does; the standoff grows with a blast weapon's reach. When what is carried cannot finish every open objective (Arms.Enough)
/// it goes to the stores first ($95, the log book's locked place): in through the door on the south side (the door box +$62,
/// Buildings.DoorTest), on the store's screen AUTO (the most of everything, $93F0) and END, clicked as the mouse would.
/// TRIPS (docs/bot-brain.md 2): every "get to a place" is Travel - the quickest way over what he knows (Trip.Plan: walking,
/// swimming, a vehicle he has seen or the drone's map shows, boarding by walking into it, TouchVehicle $45D4, out by Space,
/// LeaveVehicleKey $A766), held, and planned again at each leg's end or when a leg breaks. Riding at a place, a tank fights
/// from inside: it stops Standoff short, turns on the spot to the target's bearing and fires its gun (the vehicle's weapon
/// is in hand, WeaponOnSetUp $A34E); a car makes runs, a boat and a helicopter fire on their way; else he gets out.
/// Collect objectives (bit 15 words) are a later step.</summary>
public sealed class Hunt(int seed = 1107) : IBrain
{
    /// <summary>The average player's slips (hesitations, pauses, how long a flinch lasts), from a fixed seed: the same film
    /// every time.</summary>
    readonly Random rnd = new(seed);
    /// <summary>How far short of a target's side it stands to fire: a bullet ($52) flies about 10 a tick for 32 ticks.</summary>
    public const int Reach = 160;
    /// <summary>Close enough to a place to look for its target among the live things (they are woken round the camera).</summary>
    public const int Sight = 1024;
    /// <summary>Firing this many passes without an end: line up from another side.</summary>
    public const int Patience = 600;

    /// <summary>The store's screen regions ($9AE2): AUTO x $78-$F0, END $F0-$140, both y $BC-$C8.</summary>
    const int AutoX = 0xB4, EndX = 0x118, ButtonsY = 0xC2;
    /// <summary>Blanks between key presses (a key goes down once; the next pass reads it, Keys.cs).</summary>
    const int KeyGap = 4;
    /// <summary>Passes walking at a store's door before it is given up (a refusal: an item asked for, $46A0).</summary>
    const int DoorPatience = 600;

    /// <summary>A tank stops this far short of a target's side to fire.</summary>
    public const int Standoff = 500;

    int passes;
    bool wantLeave;
    /// <summary>In a vehicle now (+$08 bit 15 clear).</summary>
    public bool Riding { get; private set; }
    /// <summary>Times it got into a vehicle.</summary>
    public int Rides { get; private set; }

    GoTo? walk;
    int wantWeapon = -1, keyWait, storeClicks, storeWait, doorPasses;
    bool stocked, inStore;
    /// <summary>His drones when he last went to a store for more (none went for them yet: no limit).</summary>
    int droneStoreGave = int.MaxValue;
    (int X, int Z) walkTo;
    readonly HashSet<(int X, int Z)> done = [];
    (int X, int Z)? place;
    Entity? target;
    int firing;
    byte? aim;                                                               // the way he fires at the target from (Shoot), one of the 8
    readonly HashSet<byte> failedAims = [];                                  // ways that gave no end on this target
    void FailAim() { if (aim is { } h) failedAims.Add(h); aim = null; lined = false; sidesFailed++; }
    void NewAim() { aim = null; failedAims.Clear(); lined = false; sidesFailed = 0; }
    // NO GOOD: sides tried on this one with it
    // none the worse; at SidesToTry he gives it up as one he cannot hurt (on foot, as he is) and remembers where it stands
    int sidesFailed, sidesFrom = -1;
    Entity? sidesOn;
    const int SidesToTry = 8;
    readonly List<(int Type, int X, int Z)> cantHurt = [];
    Entity? gaveUp;
    bool CantHurt(WorldState s, Entity e) => cantHurt.Any(c => c.Type == e.W(4) && Dist(c.X, c.Z, e.UW(0x4A), e.UW(0x4E)) < 256);
    /// <summary>The last thing given up as no good - a fight, a shore - and why (the run log writes it once).</summary>
    public string? GaveUpWhy { get; private set; }
    readonly List<(int X, int Z)> badShores = [];
    (int X, int Z)? shoreFor;
    (int X, int Z) shoreFrom;
    int shoreTries;
    const int ShorePatience = 150;

    public (int X, int Z)? Place => place;
    public Entity? Target => target;
    public bool OutOfRounds { get; private set; }
    /// <summary>Every destroy objective met (the kills counted): it is on its way to the HQ.</summary>
    public bool Met { get; private set; }
    /// <summary>Been to the stores (or given them up): it goes once.</summary>
    public bool Stocked => stocked;
    /// <summary>The weapon item it last chose (Arms: 0 bullets, 1 bazooka shells, 2 grenades).</summary>
    public int Weapon => wantWeapon;

    /// <summary>Each open destroy objective's type and how many are left to end.</summary>
    public static List<(int Type, int Left)> OpenLeft(WorldState s)
    {
        var main = s.Data.Main; var rec = 0x9072 + s.Mission - 0x800;
        var open = new List<(int, int)>();
        for (var k = 0; k < 4; k++)
        {
            var w = (short)(main[rec + 8 + 2 * k] << 8 | main[rec + 9 + 2 * k]);
            if (w <= 0) continue;
            int count = w >> 8 & 0x7F, type = w & 0xFF;
            var left = s.RoomyMemory ? count - s.KillsWide[type] : (sbyte)(count - s.Kills[type]);
            if (left > 0) open.Add((type, left));
        }
        return open;
    }

    /// <summary>The types still to destroy: each positive objective word of the mission (count &lt;&lt; 8 | type, $9A08) whose
    /// count is not yet reached ($99EA; the plain count with RoomyMemory, as Games.Unmet).</summary>
    public static List<int> OpenTypes(WorldState s)
    {
        var main = s.Data.Main; var rec = 0x9072 + s.Mission - 0x800;
        var open = new List<int>();
        for (var k = 0; k < 4; k++)
        {
            var w = (short)(main[rec + 8 + 2 * k] << 8 | main[rec + 9 + 2 * k]);
            if (w <= 0) continue;
            int count = w >> 8 & 0x7F, type = w & 0xFF;
            if (s.RoomyMemory ? count - s.KillsWide[type] > 0 : (sbyte)(count - s.Kills[type]) > 0) open.Add(type);
        }
        return open;
    }

    /// <summary>The log book's objective places: noted and unlocked.</summary>
    public static List<(int X, int Z)> Places(WorldState s)
    {
        var o = new List<(int, int)>();
        for (var n = 0; n < LogBook.Entries && s.Log.Noted(n); n++) if (!s.Log.Locked(n)) o.Add(s.Log.Place(n));
        return o;
    }

    /// <summary>What he knows - sight, the message line, places, his memory (docs/bot-brain.md 1): nothing else here looks for
    /// things the player could not.</summary>
    public Senses Senses { get; } = new();

    /// <summary>A new brain with what the same player learnt of the world in the last mission of this game (the Missions
    /// mode goes on in one world - E2).</summary>
    public Hunt(Senses memory, int seed = 1107) : this(seed) => Senses = memory;

    /// <summary>The brain for the next mission of this game: the same player - what he knows of the world, and that he has
    /// stocked up and what the store's drones came to (the stores are not filled again between missions).
    /// Out of rounds, or down to his kept drone, sends him back as in any mission. M4 in one game 11-09: a fresh brain went
    /// "to the stores" again at every mission's start - a 152 s trip with a swim, 9 energy to 6, then no way.</summary>
    public Hunt NextMission() => new(Senses) { stocked = stocked, droneStoreGave = droneStoreGave };

    (int X, int Z)? Known(WorldState s, int type) => Senses.Place(s, type);
    static Entity? LiveNear(WorldState s, int type, int x, int z) => Senses.LiveNear(s, type, x, z);
    static int Dist(int ax, int az, int bx, int bz) => Senses.Dist(ax, az, bx, bz);

    /// <summary>Something of an open type in sight near the place that he has not yet taken in (inside Senses.Reaction):
    /// wait for it rather than give the place up.</summary>
    bool Noticing(WorldState s, List<int> open, int px, int pz)
    {
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            if (e.Alive && open.Contains(e.W(4)) && Dist(e.UW(0x4A), e.UW(0x4E), px, pz) <= 2 * Sight
                && Senses.NoteOf(e) is { InSight: > 0 and < Senses.Reaction }) return true;
        }
        return false;
    }

    /// <summary>A missile coming at him, as a player sees it: a shot in flight (not a bullet, not his own) on his screen
    /// (Senses.InView) within MissileWatch, flying at him (its heading within 24 of the way to him). It flies straight on
    /// (MissileProbe, M10: heading kept, 24 a pass, ~190 passes - past the reach of the gun that fired it).</summary>
    static Entity? MissileComing(WorldState s, Entity me)
    {
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            if (!e.Alive || !e.Flag(20) || e.W(0x3E) <= 0 || e.W(4) == 0x52 || (e.UW(0x2C) - 0xA56) / EntityTable.Size == 0) continue;
            int ex = e.UW(0x4A), ez = e.UW(0x4E);
            if (Dist(mx, mz, ex, ez) > MissileWatch || !Senses.InView(s, ex, ez, 0)) continue;
            if (Math.Abs((int)(sbyte)(Wish.Toward(ex, ez, mx, mz) - e.B[0x7E])) <= 24) return e;
        }
        return null;
    }
    const int MissileWatch = 3000, MissileLow = 25;
    /// <summary>Passes spent low under a missile coming.</summary>
    public int Ducks { get; private set; }

    /// <summary>Whatever he means to do: never treading water with no plan - swimming (+$08 bit 10 on foot) with nothing to go
    /// for, he makes for the nearest land.</summary>
    public Wish Decide(Play play)
    {
        checkSpot = null;
        var w = Think(play);
        var me = play.State.Entities.All[0];
        TurnView(play.State, me);
        if (w.Go && Body.Flies(me) && !me.Bit(0x08, 15) && MissileComing(play.State, me) is not null)
        {                                                                    // UNDER IT (DodgeProbe, 6 ways tried at once on M10's
            Ducks++; Doing += ", under a missile";                           // guard tower: down low his own way, alive 600 passes untouched - backing,
            w = w with { Alt = me.W(0x74) + MissileLow };                    // hovering, climbing: shot down or stuck dodging). A tower's missiles come
        }                                                                    // from its top and sink slowly; a turn takes 120 passes, a drop a few
        if (!me.Bit(0x08, 10)) { badShores.Clear(); shoreFor = null; }       // out of the water: every shore worth trying again
        if (w.Go || !me.Alive || !me.Bit(0x08, 15) || !me.Bit(0x08, 10)) return w;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        if (Navigate.Shore(play.State, mx, mz, sea: false, skip: badShores.Contains) is not var (lx, lz)) return w;
        if (shoreFor != (lx, lz) || Dist(mx, mz, shoreFrom.X, shoreFrom.Z) > 48) { shoreFor = (lx, lz); shoreFrom = (mx, mz); shoreTries = 0; }
        else if (++shoreTries > ShorePatience)                               // NO GOOD: swum at it all this while
        {                                                                    // and not moved - a bank he cannot climb (M10 tough: 165,889 passes
            badShores.Add((lx, lz)); shoreFor = null;                         // on one spot): crossed off, the next nearest
            GaveUpWhy = $"gave up on the shore at {lx},{lz}: {ShorePatience} passes swimming at it, not moved - the next nearest";
        }
        Doing = "swimming ashore";
        var sw = Walk(play, lx, lz, 24, swim: true);                         // by GoTo, round what would board him: straight, he swam back into the
        return walk!.NoWay ? new(true, Wish.Toward(mx, mz, lx, lz), false) : sw;   // dry boat he had just left, again and again (M1 11-08, BumpProbe)
    }

    Wish Think(Play play)
    {
        var s = play.State;
        var me = s.Entities.All[0];
        Watch(s, me);
        var open = OpenTypes(s);
        if (!play.Quit) Senses.Update(s, open);                              // the loop has returned (a quit, the HQ's door): the world is written back
                                                                             // (only he is left) and the screen fades out - nothing seen, nothing gone (every
                                                                             // hand-in in one game 11-09 lost the vehicles by the HQ that way)
        if (!me.Alive || me.W(4) == 0x7C) return Wish.Stand;              // dead, or his record now his grave ($7C, $33EC) - no plans from it (M6 11-08)
        passes++;
        Doing = "walking";
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        var onFoot = me.Bit(0x08, 15);
        if (Riding != !onFoot)
        {
            Riding = !onFoot; walk = null; wantLeave = false; descending = null; landingFor = null;
            outTo = null;                                                    // a way out held in the last vehicle (M7 11-09: kept on foot, the next boat
                                                                             // drove to it - 4000 off, through a launcher's reach, sunk)
            if (Riding && trip.Current is { Kind: Trip.Kind.Board, Vehicle: { } bv } && bv.Type != me.W(4) && ++wrongBoard >= 2)
            { Senses.GiveUp(bv.X, bv.Z); wrongBoard = 0; }                  // on the way to board one, put in another twice (parked against it - M2 11-08): that one given up
            if (Riding) { Rides++; Senses.GotIn(me.W(4), mx, mz); rideHealth0 = rideLast = me.W(0x2A); vfoe = null; flee = 0; rideFrom = (passes, mx, mz); }
            else if (leftIn is var (lt, lx, lz))                             // parked where he left it, its gauge as he last saw it (round trip);
                Senses.GotOut(lt, lx, lz, leftLasts,                           // THE IN/OUT FAMILY: a ride that went
                    passes - rideFrom.P < NowhereRide && Dist(lx, lz, rideFrom.X, rideFrom.Z) < 256);   // nowhere - out where he got in - is no use from here, so the
                                                                             // plan on foot and the plan in it can no longer send him back and forth (M4, M5 11-08)
        }
        if (!onFoot) { leftIn = (me.W(4), mx, mz); leftLasts = me.Flag(26) ? Trip.Lasts(me) : null; }   // where he gets out, remembered
        if (!onFoot && RideSurvive(play, me) is { } rs) return rs;
        if (onFoot) pinnedOut = false;
        if (!onFoot && (pinnedOut || Pinned(me, mx, mz))) { pinnedOut = true; Doing = "stuck: out"; return Leave(); }   // held till he is out: stopped to get
                                                                             // out it was "not pinned" (speed 0), and he drove on (M3 11-08: 11000 passes, a jeep)
        if (!onFoot && me.Flag(26) && me.W(0x34) <= 0 && me.W(0x3E) == 0)   // the gauge at empty (motorised, +$34 - MoveByModeBurnFuel $2972):
        {                                                                    // out, and it let be (at sea he swims for land)
            Doing = "out of fuel: out";
            Senses.GiveUp(mx, mz);
            return Body.Flies(me) && me.B[0x1E] == 0 ? LandNear(play, me, mx, mz) : Leave();
        }
        if (!onFoot && Body.Flies(me) && (me.B[0x1E] == 0 || landingFor is not null) && me.Flag(26) && LowFuel(s, me) is var (fx, fz))
        {                                                                    // down for fuel: kept on the ground, so LandNear gets him out
                                                                             // (M4 11-08: down, up, down - 450 passes at 40772,13795)
            var lw = LandNear(play, me, fx, fz, 0);
            Doing = "low on fuel: landing";                                  // after LandNear's own "landing"
            return lw;
        }
        if (s.InsideView && onFoot)                                          // walked in through a door he did not mean (lining up on it): out over the
        {                                                                    // room's south edge, the door side (KeepInsideBoundsOrExit $42E6)
            Doing = "leaving a building"; walk = null;
            return new(true, 0x80, false);
        }
        if (onFoot && Survive(play, me) is { } alive) return alive;
        if (directed) return Direct(play, me, onFoot, mx, mz);
        Met = open.Count == 0;
        if (Met)
        {
            target = null; place = null;
            if (Known(s, 0xAA) is not var (hx, hz)) return Wish.Stand;
            if (!onFoot) return Travel(play, me, hx, hz, Sight / 2, "home to the HQ") ?? Leave();
            Doing = "home to the HQ";
            return GoIn(play, me, 0xAA, hx, hz, out _);                      // in at its door: the mission handed in
        }
        if (stocked && Drones(s) <= DronesKept && Drones(s) < droneStoreGave && Known(s, 0x95) is not null)   // down to the drone he keeps: a store's
        {                                                                    // AUTO fills each item to the most he carries from what it has ($9BF0)
            droneStoreGave = Drones(s); stocked = false; forDrones = true;   // a store that gave none is not tried again till one more is spent
        }
        if (!stocked)                                                        // a player stocks up before he sets out
        {
            if (onFoot)
            {
                var wr = Restock(play, me);
                if (!forDrones || trip.Legs is not { } fl || fl.Sum(l => l.Time) <= DroneTrip) return wr;
                stocked = true; forDrones = false; trip.Drop("the stores too far for a drone"); return Wish.Stand;   // on foot too (M3 11-08: walked 5000 passes to a tank for one)
            }
            if (Known(s, 0x95) is var (sx0, sz0))
            {
                var ws = Travel(play, me, sx0, sz0, Sight / 2, "to the stores") ?? Leave();
                if (forDrones && trip.Legs is { } sl && sl.Sum(l => l.Time) > DroneTrip)
                { stocked = true; forDrones = false; trip.Drop("the stores too far for a drone"); return Wish.Stand; }   // a drone is worth a short trip only
                if (trip.Legs is not null || wantDrone || DroneUp(s) || onMap) return ws;
                stocked = true; trip.Drop("no way to the stores");                               // no way to the stores: on with the mission without
            }
        }
        if (vfoe is not null && !Threat(vfoe))                               // the foe from a ride, riding or not: dead, or its slot now something else
        {
            if (target == vfoe) target = keptTarget is { Alive: true } k && open.Contains(k.W(4)) ? k : null;
            vfoe = null; firing = 0;
        }
        if (target is not null && target != vfoe && (!target.Alive || !open.Contains(target.W(4)))) { target = null; firing = 0; }
        if (place is null || done.Contains(place.Value))
        {
            place = null;
            foreach (var p in Places(s))
                if (!done.Contains(p) && (place is null || Dist(mx, mz, p.X, p.Z) < Dist(mx, mz, place.Value.X, place.Value.Z))) place = p;
            if (place is null) return Wish.Stand;                            // nothing noted left to go to
        }
        var (px, pz) = place.Value;
        if (target is null && Peek(play, me, px, pz) is { } peek) return peek;
        if (target is null && (onFoot || gunFirst is not null) && GunFirst(play, me, px, pz) is { } gf) return gf;   // riding on the way to it too
        if (!onFoot) return Ride(play, me, open, px, pz);
        if (target is null)
        {
            if (Dist(mx, mz, px, pz) > Sight / 2 && lookAt != place) return Travel(play, me, px, pz, Sight / 2, "to a target") ?? Wish.Stand;   // looking round it already: kept at (a way round to a spot may lead off past Sight / 2 - M4 went to and fro)
            target = Nearest(s, open, px, pz);
            if (target is null && Noticing(s, open, px, pz)) return Wish.Stand;   // taking in what he sees
            if (target is null) return LookRound(play, me, px, pz);
            NewAim(); firing = 0;
        }
        return Shoot(play, me, target);
    }

    // ---- THE DIRECTOR'S ORDERS (recording only) ----
    /// <summary>One order: board the vehicle of Type nearest (X, Z); go to (X, Z) by whatever way he finds; destroy a thing
    /// of Type near (X, Z) with what he is in or carries; get out; land near (X, Z).</summary>
    public readonly record struct Order(string Kind, int Type, int X, int Z);
    readonly List<Order> orders = [];
    bool directed;
    int orderKills = -1;
    /// <summary>The order being carried out (for the film's log); null: none, or all done.</summary>
    public Order? Ordered => orders.Count > 0 ? orders[0] : null;

    /// <summary>Orders in place of the mission: "kind:type hex:x:z;..." (kinds board, go, kill, out, land) - his skills
    /// (the planner, flying, aiming, staying alive) carry each out; no stores trip first. Done: he stands (hovers).</summary>
    public void Direct(string script)
    {
        foreach (var part in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = part.Split(':');
            orders.Add(new(f[0].ToLowerInvariant(), f.Length > 1 && f[1].Length > 0 ? Convert.ToInt32(f[1], 16) : 0,
                f.Length > 2 ? int.Parse(f[2]) : 0, f.Length > 3 ? int.Parse(f[3]) : 0));
        }
        directed = true; stocked = true;
    }

    Wish Direct(Play play, Entity me, bool onFoot, int mx, int mz)
    {
        var s = play.State;
        if (orders.Count == 0) { Doing = "scene over"; return Wish.Stand; }
        var o = orders[0];
        Wish Next(string why) { orders.RemoveAt(0); orderKills = -1; target = null; place = null; walk = null; firing = 0; trip.Drop(why); return Wish.Stand; }
        switch (o.Kind)
        {
            case "board":
            {
                if (!onFoot && me.W(4) == o.Type) return Next("boarded");
                if (passes < 5) return Wish.Stand;                           // the world round him wakes on the first passes
                var v = NearestOf(s, o.Type, o.X, o.Z);                       // only things near him are live
                if (v is null && Dist(mx, mz, o.X, o.Z) <= Sight / 2) return Next("no such vehicle");
                int vx = v?.UW(0x4A) ?? o.X, vz = v?.UW(0x4E) ?? o.Z;
                var by = Dist(mx, mz, vx, vz) < 600;
                if (by && !onFoot) { Doing = "scene: out of the wrong vehicle"; return Leave(); }
                if (by && !me.Bit(0x08, 10) && Trip.SameLand(s, mx, mz, vx, vz))   // on its own land, out of the water: walk on to it
                {
                    Doing = "scene: boarding";
                    if (v is { } fv && fv.Flag(26) && fv.W(0x34) < 0x4000) fv.SetW(0x34, 0x7F00);   // the director fills its tank (+$34 = record +$0D << 8; $7F the fullest a record has)
                    var bw = Walk(play, vx, vz, 0, allow: v);
                    if (!walk!.NoWay) return bw;
                }
                return Travel(play, me, vx, vz, 64, "scene: to the vehicle", air: false) ?? Wish.Stand;   // the planner: boats, swims, other rides
            }
            case "go":
                if (Dist(mx, mz, o.X, o.Z) <= Sight / 4) return Next("there");
                return Travel(play, me, o.X, o.Z, Sight / 4, "scene: going") ?? Wish.Stand;
            case "out":
                if (onFoot) return Next("out");
                Doing = "scene: getting out";
                return Body.Flies(me) && me.B[0x1E] == 0 ? LandNear(play, me, mx, mz) : Leave();
            case "land":
                if (onFoot || me.B[0x1E] != 0 && Dist(mx, mz, o.X, o.Z) < 600) return Next("landed");
                Doing = "scene: landing";
                return LandNear(play, me, o.X, o.Z);
            case "kill":
                if (orderKills < 0) orderKills = s.Kills[o.Type];
                if (s.Kills[o.Type] > orderKills) return Next("destroyed");
                var open = new List<int> { o.Type };
                place = (o.X, o.Z);
                if (NearestOf(s, o.Type, o.X, o.Z) is null)                  // not live: far off (to it first), or near and gone
                {
                    if (Dist(mx, mz, o.X, o.Z) <= Sight / 2) return Next("not there");
                    return Travel(play, me, o.X, o.Z, Sight / 2, "scene: to the target") ?? Wish.Stand;
                }
                if (!onFoot) return Ride(play, me, open, o.X, o.Z);
                if (target is null || !target.Alive)
                {
                    if (Dist(mx, mz, o.X, o.Z) > Sight / 2) return Travel(play, me, o.X, o.Z, Sight / 2, "scene: to the target", air: false) ?? Wish.Stand;
                    target = Nearest(s, open, o.X, o.Z);
                    if (target is null) return LookRound(play, me, o.X, o.Z);
                    NewAim(); firing = 0;
                }
                return Shoot(play, me, target);
            default: return Next("unknown order");
        }
    }

    /// <summary>The live thing of this type nearest (x, z) within 2000 - the director knows his set.</summary>
    static Entity? NearestOf(WorldState s, int type, int x, int z)
    {
        Entity? best = null; var bd = 2000;
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            if (!e.Alive || e.W(4) != type) continue;
            var d = Dist(e.UW(0x4A), e.UW(0x4E), x, z);
            if (d < bd) (best, bd) = (e, d);
        }
        return best;
    }

    /// <summary>Of the live things of an open type he can see (within Sight of him) near the place, the one nearest him: any of
    /// the type counts for the kill ($9288), so he takes the closest in view, not the one the log book happened to note
    /// (Mission 1: three dumps, one noted). Only what a player could see: nothing out of his sight, nothing he has not yet
    /// taken in (Senses.SeesNow).</summary>
    Entity? Nearest(WorldState s, List<int> open, int px, int pz)
    {
        var me = s.Entities.All[0];
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        Entity? best = null; var bd = int.MaxValue;
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            if (!e.Alive || !open.Contains(e.W(4)) || Dist(e.UW(0x4A), e.UW(0x4E), px, pz) > 2 * Sight) continue;
            var d = Dist(e.UW(0x4A), e.UW(0x4E), mx, mz);
            if (d > Sight) continue;                                         // out of his sight
            if (d < bd && Senses.SeesNow(e)) (best, bd) = (e, d);            // not behind a building, and taken in
        }
        return best;
    }

    /// <summary>Is (x, z) inside a still thing he would bump into (NearSight.InTheWay, no +$06 bit 27 mover) - its box, his half
    /// width and NearSight.Clearance round it? Not the target itself.</summary>
    static bool InAThing(WorldState s, Entity me, Entity t, int x, int z)
    {
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            if (e == t || e.Flag(27) || !NearSight.InTheWay(me, e)) continue;
            var pad = Math.Abs((int)me.W(0x6E)) + NearSight.Clearance;
            if (Math.Abs(Senses.Diff(x, e.UW(0x4A))) <= Math.Abs((int)e.W(0x6E)) + pad
                && Math.Abs(Senses.Diff(z, e.UW(0x4E))) <= Math.Abs((int)e.W(0x72)) + pad) return true;
        }
        return false;
    }

    /// <summary>At the log book's place with nothing of the kind in sight (it may stand behind a building): round the place
    /// to 4 spots LookOut off it (north, east, south, west - one he cannot get to is passed), then it is given up.</summary>
    Wish LookRound(Play play, Entity me, int px, int pz)
    {
        if (lookAt != place) { lookAt = place; looked = 0; lookSpot = -1; }
        while (looked < 4)
        {
            var (sx, sz) = ((ushort)(px + (looked == 1 ? LookOut : looked == 3 ? -LookOut : 0)), (ushort)(pz + (looked == 0 ? LookOut : looked == 2 ? -LookOut : 0)));
            if (lookSpot != looked) { lookSpot = looked; lookSince = passes; lookFor = 3 * Dist(me.UW(0x4A), me.UW(0x4E), sx, sz) + StartAllowance; }
            if (Dist(me.UW(0x4A), me.UW(0x4E), sx, sz) > 64 && play.State.Ground.Sample(sx, sz).Item2   // a spot in the sea (the game's point test) is passed:
                && passes - lookSince <= lookFor)                                                        // no swims to look (M4 11-09: 3 of his last 8 energy);
            {                                                                                           // so is one not reached in 3x the straight walk (M4 11-08:
                var w = Walk(play, sx, sz, 64);                                                         // a spot inside his parked helicopter, 8700 passes at its edge)
                if (!walk!.NoWay) { Doing = "looking round"; return w; }
            }
            looked++; walk = null;
        }
        done.Add(place!.Value); place = null;
        return Wish.Stand;
    }
    const int LookOut = 300;
    int lookSpot = -1, lookSince, lookFor;
    Entity? missT;
    int missArm = -1, missE, missRounds;
    /// <summary>Times a firing side was given up because its shots did not hurt.</summary>
    public int Misses { get; private set; }
    (int X, int Z)? lookAt;
    int looked;

    Wish Shoot(Play play, Entity me, Entity t)
    {
        Doing = "shooting";
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E), tx = t.UW(0x4A), tz = t.UW(0x4E);
        var half = Math.Max(Math.Abs((int)t.W(0x6E)), Math.Abs((int)t.W(0x72)));
        var arm = Arms.Choose(s, Math.Max(0, (int)t.W(0x2A)));
        if (arm < 0) { OutOfRounds = true; stocked = false; forDrones = false; doorPasses = 0; return Wish.Stand; }   // nothing left that ends it: back to the stores
        wantWeapon = arm;
        var left = Arms.Rounds(s, arm);                                      // his shots land or not - he sees it: rounds gone and
        if (t != missT || arm != missArm || t.W(0x2A) < missE) { missT = t; missArm = arm; missE = t.W(0x2A); missRounds = left; }   // it no worse
        else if (missRounds - left >= (arm == Arms.Bullets ? 8 : 3)) { FailAim(); missRounds = left; Misses++; }   // from this side: the next
        if (t != sidesOn || t.W(0x2A) < sidesFrom) { sidesOn = t; sidesFrom = t.W(0x2A); sidesFailed = 0; }   // a new one, or it was hurt: count afresh
        if (sidesFailed >= SidesToTry)                                       // every side tried and it is no worse: no good - say so (the
        {                                                                    // fight and the gun in the way let go of it; a target starts again)
            cantHurt.Add((t.W(4), tx, tz)); gaveUp = t;
            GaveUpWhy = $"gave up on ${t.W(4):X2} at {tx},{tz}: {sidesFailed} sides tried, its energy still {t.W(0x2A)} - cannot hurt it from here";
            failedAims.Clear(); sidesFailed = 0; aim = null; lined = false; firing = 0;
            return Wish.Stand;
        }
        var r = (arm == Arms.Bullets ? Reach : Math.Clamp(Arms.Range(s, arm) / 4, 2 * Reach, 3 * Reach)) + half;   // a blast: well back
        (int X, int Z) Spot(byte hk) { var ak = hk * Math.PI / 128; return ((ushort)(tx - (int)Math.Round(r * Math.Sin(ak))), (ushort)(tz - (int)Math.Round(r * Math.Cos(ak)))); }
        var ways = Enumerable.Range(0, 8).Select(k => Facing(play, 0, k)).ToList();   // the 8 ways he can face (the view's turn in them)
        bool Bad((int X, int Z) p) => !s.Ground.Sample((ushort)p.X, (ushort)p.Z).Item2 || InAThing(s, me, t, p.X, p.Z);   // water by the game's own point test ($2B0E, as
                                                                             // the swim switch reads it) - a land square's shore can be sea (M4 11-09: a spot he swam at)
        if (aim is { } was && !ways.Contains(was))                           // the view turned, the 8 ways with it: the same side, the nearest way to it
        {                                                                    // (chosen afresh, it flipped sides every 3 passes - M1 11-08, FightProbe)
            var near = ways.MinBy(w => Math.Abs((int)(sbyte)(w - was)));
            aim = Math.Abs((int)(sbyte)(near - was)) <= 16 ? near : null;
        }
        if (aim is not { } kept || Bad(Spot(kept)))
        {                                                                    // the nearest spot round it on land that has not failed him
            aim = null; var bd = int.MaxValue;
            for (var pass = 0; pass < 2 && aim is null; pass++)
            {
                if (pass == 1) failedAims.Clear();                           // every way failed: start again
                foreach (var hk in ways)
                {
                    var (x2, z2) = Spot(hk);
                    if (failedAims.Contains(hk) || Bad((x2, z2))) continue;
                    var d2 = Dist(mx, mz, x2, z2);
                    if (d2 < bd) (aim, bd) = (hk, d2);
                }
            }
            aim ??= Facing(play, Wish.Toward(mx, mz, tx, tz), 0);
        }
        var h = aim.Value;
        var a = h * Math.PI / 128;
        var (sx, sz) = Spot(h);
        // across the line of fire: how far the target's middle is off the way he would face
        double dx = Senses.Diff(tx, mx), dz = Senses.Diff(tz, mz);
        var across = Math.Abs(dx * Math.Cos(a) - dz * Math.Sin(a));
        var along = dx * Math.Sin(a) + dz * Math.Cos(a);
        var safe = Arms.Blast(s, arm) is var b and > 0 ? b + Math.Abs((int)me.W(0x6E)) + half + 32 : half;   // its blast must not reach him
        var slack = lined ? 24 : 0;                                          // once lined up, a step (the turn to face it is one) does not undo it
        var wet = me.Bit(0x08, 10);                                          // swimming: no shot (M1 11-08) - to the spot on land first (M4 11-09:
        lined = !wet && !(across > Math.Max(20, half * 3 / 4) + slack || along > r + 48 + slack || along < safe - slack);   // "in line" in the sea, a step
        if (!lined && !wet && s.Weapon == arm && Dist(mx, mz, tx, tz) <= r + 48 && Arms.WouldHit(s, me, t) && Arms.Blast(s, arm) is var bb && (bb == 0 || Dist(mx, mz, tx, tz) >= safe))
            return new(false, me.B[0x7E], true);                             // in line now, wherever he stands: fire
        if (!lined)
        {
            firing = 0;
            var w = Walk(play, sx, sz, 12, swim: wet);                       // ashore and back each pass, energy 7 -> 0)
            if (walk!.NoWay) { FailAim(); walk = null; }                        // that side is sea or cliff: the next way round
            return w;
        }
        walk = null;
        if (s.Weapon != arm) return Wish.Stand;                              // Act presses Return till it is in hand
        if (me.B[0x7E] != h) return new(true, h, false);                    // a tap to face it (a step, too)
        if (me.W(0x26) <= 0 && me.W(0x32) <= 0) { OutOfRounds = true; stocked = false; forDrones = false; doorPasses = 0; return Wish.Stand; }   // FireTimer: no rounds, +$26 = 0
        if (++firing > Patience) { FailAim(); firing = 0; }                     // no end: try the next way round
        if (!Arms.WouldHit(s, me, t))                                        // from here the shot would not reach it (a rise, a wall between): not wasted -
        {                                                                    // a while to let it come into line, then the next way round
            if (++blind > BlindPatience) { FailAim(); blind = 0; firing = 0; }
            return new(false, h, false);
        }
        blind = 0;
        if (pause > 0) { pause--; return new(false, h, false); }             // an average player: not every pass on the trigger
        if (firing == 1) pause = 10 + rnd.Next(30);                          // a moment before the first shot
        else if (rnd.Next(150) == 0) pause = 10 + rnd.Next(25);              // and a breath now and then
        return new(false, h, true);
    }
    int pause, blind;
    bool lined;
    const int BlindPatience = 40;

    /// <summary>The foot heading among the 8 (FootHeading, the view turn added) nearest `want`, `turn` ways on round.</summary>
    static byte Facing(Play play, byte want, int turn)
    {
        byte best = 0; var off = int.MaxValue;
        foreach (var d in new[] { 2, 6, 4, 5, 1, 9, 8, 10 })
        {
            var h = Controls.FootHeading(play.Data.Main, d, play.State.ViewAngle1);
            var o = Math.Abs((int)(sbyte)(h - want));
            if (o < off) (best, off) = (h, o);
        }
        return (byte)(best + 32 * turn);
    }

    /// <summary>To (x, z) by GoTo - over the sea's squares in a boat (+$08 bit 10, not on foot), over the land otherwise;
    /// `allow`: the vehicle he means to touch (board), not gone round. `swim`: on foot over land and sea.</summary>
    Wish Walk(Play play, int x, int z, int near, Entity? allow = null, bool swim = false, IReadOnlyList<Trip.Danger>? shun = null)
    {
        var me = play.State.Entities.All[0];
        swim |= SwimWalks && me.Bit(0x08, 15) && me.Bit(0x08, 10);           // on foot in the water: any walk is a swim (M7 11-09: boarding a helicopter
                                                                             // 800 off from the sea by the land planner - 640 passes zig-zagging at the
                                                                             // bank, drowned; SwimProbe: straight there, alive)
        var both = swim || !me.Bit(0x08, 15) && me.Flag(1);                  // an amphibian: land and sea (+$06 bit 1, $2BAC)
        var water = !both && !me.Bit(0x08, 15) && me.Bit(0x08, 10);
        var shunCount = shun?.Count ?? -1;
        if (walk is null || walkWater != water || walkBoth != both || Dist(walkTo.X, walkTo.Z, x, z) > 32 || walkAllow != allow || walkShun != shunCount)
        {
            Func<int, int, bool>? avoid = null;
            if (shun is { Count: > 0 })
            {
                int status = Trip.StatusOf(me.Flags), m = me.W(0x36) * Trip.MarginBlanks + Navigate.Square / 2;   // OutOfReach's margin, and half a square:
                var ds = shun.Where(d => (d.Mask & status) != 0).ToList();                                       // only each square's middle is tested
                avoid = (sx, sz) => ds.Any(d => Math.Abs(Senses.Diff(sx, d.X)) <= d.BoxX + m && Math.Abs(Senses.Diff(sz, d.Z)) <= d.BoxZ + m);
            }
            walk = new GoTo(x, z, near, water, both, allow, avoid); walkTo = (x, z); walkWater = water; walkBoth = both; walkAllow = allow; walkShun = shunCount;
        }
        return walk.Decide(play);
    }
    bool walkWater, walkBoth;
    int walkShun = -1;
    Entity? walkAllow;

    /// <summary>To the stores: the live store ($95, +$90 negative) by the log book's place, its door spot (Buildings.DoorSpot's
    /// sums: the door box's middle, south of the front by the two half depths and $10), then north into the door.</summary>
    Wish Restock(Play play, Entity me)
    {
        Doing = "to the stores";
        var s = play.State;
        if (Known(s, 0x95) is not var (px, pz)) { stocked = true; return Wish.Stand; }
        var w = GoIn(play, me, 0x95, px, pz, out var atDoor);
        if (atDoor && ++doorPasses > DoorPatience) { stocked = true; return Wish.Stand; }   // not let in: on without
        if ((LiveNear(s, 0x95, px, pz) is null || walk is { NoWay: true }) && trip.Legs is null && !wantDrone && !DroneUp(s) && !onMap) { stocked = true; trip.Drop("no way to the stores"); }   // no way to the stores: on with the mission without (M4 11-08 stood 28000 passes)
        return w;
    }

    /// <summary>In at a building's door: a trip to its place till the live building of that type is in sight,
    /// then its door spot (Buildings.DoorSpot's sums, $432E-$4354: the door box's middle, south of the front by the two half
    /// depths and $10) and north into the door - the stores' screen, or the HQ, where a met mission ends ($466C).</summary>
    Wish GoIn(Play play, Entity me, int type, int px, int pz, out bool atDoor)
    {
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        var b = LiveNear(s, type, px, pz);
        atDoor = false;
        if (b is null) return Travel(play, me, px, pz, 64, Doing) ?? Wish.Stand;
        int dx = (ushort)(b.W(0x4A) + (b.W(0x62) + b.W(0x68)) / 2);
        int dz = (ushort)(b.W(0x4E) - b.W(0x72) - me.W(0x72) - 0x10);
        atDoor = Dist(mx, mz, dx, dz) <= 160;                                // at the door: the patience counts from here
        var ox = Senses.Diff(mx, b.UW(0x4A));                                // the door box's x (the game's test, $45BA: +$62 .. +$68 of the building)
        int half = (b.W(0x68) - b.W(0x62)) / 2, mid = (b.W(0x62) + b.W(0x68)) / 2;
        var south = Senses.Diff(mz, b.UW(0x4E)) < 0;
        var inBox = ox >= b.W(0x62) && ox <= b.W(0x68) && south;
        if (goingIn != b || !inBox) goingIn = null;                          // GOING IN, held till he is in or out of the box (M9 11-09: started from
        if (goingIn is null && Math.Abs(ox - mid) <= half / 2 && Senses.Diff(mz, dz) >= -24 && south) goingIn = b;   // its edge, 2 inside; the stick's
        if (goingIn is null)                                                 // LIVE BUT FAR (11-11: MORE OBJECTS' wider live area has the stores live
        {                                                                    // from M1's start, across the water): no way on foot to the door - the
            var ws = Walk(play, dx, dz, Math.Clamp(half / 4, 1, 12));        // trip planner (swims, rides) as when it is out of the live area; he
            return walk!.NoWay ? Travel(play, me, px, pz, 64, Doing) ?? ws : ws;   // stood at the start for ever (BotLiveFarTests)
        }                                                                    // 8 ways, turned by the view, drift a unit a step - out in 3,
        walk = null;                                                         // back 1, 9800 passes; M1 11-09: 16 off, into the wall). Lined up in
        return new(true, Wish.Toward(mx, mz, dx, b.UW(0x4E)), false);        // its middle half first, then north, in at the door
    }
    Entity? goingIn;

    /// <summary>EYES ONLY (a tape's replay, rules/Tape.cs): what a player takes in this blank while someone else plays - his
    /// sight in play, the drone's map when it is up - and nothing decided. Called where Decide would be (and Act for the map).</summary>
    public void Watch(Play play)
    {
        var s = play.State;
        if (play.Mode == PlayMode.Screen && play.Screens.Routine == 0x3CFA)
        {
            if (!watchedMap && !play.ScreenFading) { Senses.Look(s, LookTypes); watchedMap = true; }
            return;
        }
        if (play.Mode == PlayMode.Screen && play.Screens.Routine == 0x9A78) { inStore = true; return; }   // a store's screen: he was stocked
        watchedMap = false;                                                  // (M1 11-09: a bot taking over after the stores went back to them)
        LeftStore(s);
        if (s.InPlay && !play.Quit) Senses.Update(s, OpenTypes(s));
    }
    bool watchedMap;

    /// <summary>Back in play from a store's screen: stocked.</summary>
    void LeftStore(WorldState s)
    {
        if (!inStore) return;
        inStore = false; stocked = true; forDrones = false; doorPasses = 0; OutOfRounds = false;
        if (Drones(s) > droneStoreGave) droneStoreGave = int.MaxValue;     // it had drones: try a store again when low
    }

    /// <summary>The hands: Return till the chosen weapon is in hand (play); AUTO then END on a store's screen.</summary>
    public void Act(Play play)
    {
        var s = play.State;
        if (keyWait > 0) keyWait--;
        if (play.Mode == PlayMode.Screen)
        {
            if (play.Screens.Routine == 0x3CFA) { DroneScreen(play); return; }
            if (play.Screens.Routine != 0x9A78 || play.ScreenFading) return;
            if (!inStore) { inStore = true; storeClicks = 0; storeWait = 25; }
            if (--storeWait > 0) return;                                      // a look round the shelves first
            play.Click(storeClicks == 0 ? AutoX : EndX, ButtonsY);           // AUTO once, then END (again, should a message eat it)
            storeClicks++; storeWait = 25;
            return;
        }
        LeftStore(s);
        onMap = false;
        if (!s.InPlay) return;
        var me = s.Entities.All[0];
        if (wantDrone && keyWait == 0)
        {
            wantDrone = false;
            if (Drones(s) > 0 && !s.InsideView && !DroneUp(s)) { play.KeyDown(Raw(s, 'a')); keyWait = KeyGap; }   // ItemHotkeys $A816: 'a', LaunchDrone $A776
            return;
        }
        if (!me.Bit(0x08, 15))
        {
            if (wantLeave && me.W(0x3E) == 0 && (!Body.Flies(me) || me.B[0x1E] != 0) && keyWait == 0) { play.KeyDown(0x40); keyWait = KeyGap; }   // Space: LeaveVehicleKey $A766
            return;
        }
        if (wantWeapon < 0 || s.Weapon == wantWeapon || keyWait > 0) return;
        play.KeyDown(0x44);                                                  // Return: NextWeaponKey $3538
        keyWait = KeyGap;
    }

    // ---- THE DRONE: launched for real, its map read, RESUME ----
    bool wantDrone, onMap, mapRead;
    int mapWait;

    /// <summary>The raw key whose ASCII ($B33A) is c.</summary>
    static byte Raw(WorldState s, char c) => (byte)Enumerable.Range(0, 0x68).First(r => s.Data.Main[0xB33A - 0x800 + r] == c);

    /// <summary>Drones he carries: the count of the item the 'a' hotkey uses ($A806 keys, $A80E items; counts at +$28 + 32 x
    /// item, Keys.UseItem), 0 when not carried ($238).</summary>
    static int Drones(WorldState s)
    {
        var k = Enumerable.Range(0, 8).First(i => s.Data.Main[0xA806 - 0x800 + i] == 'a');
        var item = s.Data.Main[0xA80E - 0x800 + k];
        if ((s.Items238 >> item & 1) == 0) return 0;
        var o = 0x28 + item * 32;
        return (short)(s.Items[o] << 8 | s.Items[o + 1]);
    }

    /// <summary>A drone in the air: $188 counting down, $A2 bit 1 (LaunchDrone $A776).</summary>
    static bool DroneUp(WorldState s) => s.Timer188 > 0 || (s.Flags0A2 & 2) != 0;

    /// <summary>How far into the drone's patch a target must sit before he looks: as soon as it is on it - the patch's short
    /// half (8.5 squares, 4352) is barely past a gun's reach (3840); at 0.8 he hovered inside it and was shot during the look.</summary>
    const double PeekPart = 1, FarPeek = 2;
    /// <summary>Drones kept back from target looks: one for being stranded (Travel's look when there is no way).</summary>
    const int DronesKept = 1;
    /// <summary>The longest planned trip (passes, 30 s) he makes to the stores for drones alone: M4 11-08 planned 92 s for one,
    /// took 11800 passes, and ran out of passes on the way home with all four targets down. Out of rounds he goes however far.</summary>
    const int DroneTrip = 1500;
    /// <summary>A trip with a way back is given up for one without when it takes more than this many times as long.</summary>
    const double OneWay = 2;
    bool forDrones;

    /// <summary>On the way to a target place no look has shown: once it sits PeekPart into the patch round him, he stops (a
    /// hover in the air) and sends a drone up; the guns it shows feed the trip (fly on, or land out of their reach).</summary>
    Wish? Peek(Play play, Entity me, int px, int pz)
    {
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        var hold = Body.Flies(me) && me.B[0x1E] == 0 ? Wish.Stand with { Go = true, Alt = me.W(0x4C) } : Wish.Stand;
        if (peeking && (DroneUp(s) || onMap || play.Mode == PlayMode.Screen)) { Doing = "a drone look at the target"; return hold; }
        peeking = false;
        if (Drones(s) <= DronesKept) return null;
        var near = !Senses.LookedAt(px, pz, PeekPart) && Senses.OnDroneMap(mx, mz, px, pz, PeekPart);
        var far = Body.Flies(me) && !Senses.LookedAt(px, pz, FarPeek) && Senses.OnDroneMap(mx, mz, px, pz, FarPeek) && !Senses.LookedNear(mx, mz);   // the way in too, at twice
        if (!near && !far) return null;                                      // the map's reach: the guns guarding it (M6 11-08: two guard towers, off his
                                                                             // screen, shot his helicopter down 7000 from the prison, before the look)
        wantDrone = peeking = true; Doing = "launching the drone at the target";
        return hold;
    }

    /// <summary>Within this of a target place he keeps his view on it: two drone patches (OnDroneMap's $1A00 each).</summary>
    const int NearTarget = 2 * 0x1A00;

    /// <summary>The view turn a Q / E press gives each blank (GameView: pi / 100 a physics tick, 50 a second = a blank), as
    /// a heading byte.</summary>
    const double ViewStep = 256 / 200.0;
    /// <summary>The point ahead his view keeps on screen with nothing else to watch: well inside the box's 3072, past its sides.</summary>
    const int LookAhead = 2000;
    double view;

    /// <summary>The view angle as he turns it, not rounded (256 a turn): the game keeps the byte (ViewAngle1), the 3D view
    /// turns by this so 1.28 a blank shows as an even turn, not steps of 1 and 2 (picture only).</summary>
    public double View => view;
    /// <summary>A place he must see to judge (a vehicle's spot he is going to), set by the step that needs it each pass.</summary>
    (int X, int Z, int Near)? checkSpot;
    /// <summary>A kind's half length (+$72 as SetCollisionBox $334A gives it: its main model's +8 halved) - how far in the screen's
    /// box its middle must be for him to see it (Senses.Update's InView).</summary>
    static int HalfDepth(WorldState s, int type) => s.Models.U(s.Models.Offsets[s.Data.KindByte(type, 5)] + 8) >> 1;

    /// <summary>THE VIEW: the screen shows the
    /// original's box ahead of the view (Senses.InView), so he keeps it on what matters - the target he fights, else the target
    /// place when within NearTarget, else the way he is facing - turning at most a key press a blank (ViewAngle1, which
    /// his stick on foot follows as a player's does).</summary>
    void TurnView(WorldState s, Entity me)
    {
        if (!me.Alive) return;
        if ((byte)((int)Math.Round(view) & 0xFF) != s.ViewAngle1) view = s.ViewAngle1;   // the view as the game has it (a tape's, taken over)
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        var h = me.B[0x7E] * Math.PI / 128;
        var (wx, wz) = target is { Alive: true } t ? (t.UW(0x4A), t.UW(0x4E))
            : checkSpot is var (lx, lz, _) ? (lx, lz)
            : place is var (px, pz) && Dist(mx, mz, px, pz) <= NearTarget ? (px, pz)
            : ((ushort)(mx + (int)(LookAhead * Math.Sin(h))), (ushort)(mz + (int)(LookAhead * Math.Cos(h))));
        if (Senses.InView(s, wx, wz, target is null && checkSpot is var (_, _, cn) ? cn : 0)) return;                        // on the screen: held (on foot his stick turns with it - M2 chased his own standoff)
        var want = Wish.Toward(mx, mz, wx, wz);
        var off = ((want - view) % 256 + 384) % 256 - 128;
        view = (view + Math.Clamp(off, -ViewStep, ViewStep) + 256) % 256;
        s.ViewAngle1 = (byte)((int)Math.Round(view) & 0xFF);
    }

    bool peeking;

    /// <summary>The types a look notes: the boats he could take and the ground guns.</summary>
    static int[] LookTypes => [.. Enumerable.Range(0, 0x13), .. GunTypes];   // every vehicle the map shows (truck $00 to the amphibian $12) and the ground guns

    /// <summary>The drone's map screen ($3CFA): a look at it (as long as the store's shelves), what it shows noted, RESUME.</summary>
    void DroneScreen(Play play)
    {
        if (!onMap) { onMap = true; mapRead = false; mapWait = 25; }
        if (play.ScreenFading || --mapWait > 0) return;
        if (!mapRead) { Senses.Look(play.State, LookTypes); mapRead = true; }
        if (play.Screens.Buttons.FirstOrDefault(b => b.Text.Contains("RESUME", StringComparison.OrdinalIgnoreCase)) is { } r)
            play.Click((r.X0 + r.X1) / 2, (r.Y0 + r.Y1) / 2);
        mapWait = 25;
    }

    static bool LandVehicle(Entity e) =>
        e.Alive && e.W(4) < 0x13 && e.Flag(28) && ((e.Flag(13) || e.Flag(14)) && !e.Flag(10) || e.Flag(11)) && !e.Flag(12);

    static bool Armed(Entity v) => v.W(0x26) > 0 && v.W(0x32) > 0;

    static bool ArmedTank(Entity v) => Body.TurnsStill(v) && v.W(0x26) > 0 && v.W(0x32) > 0;

    (int X, int Z) pinAt;
    int pinned;
    bool pinnedOut;

    /// <summary>A vehicle on the ground with speed that has not moved for 200 passes - wedged (Settle puts a blocked move
    /// back, $2B14), turning gets it nowhere: out, and that spot let be. Not an aircraft in the air.</summary>
    bool Pinned(Entity me, int mx, int mz)
    {
        if (Body.Flies(me) && me.B[0x1E] == 0 || me.W(0x3E) == 0 || Dist(mx, mz, pinAt.X, pinAt.Z) > 8) { pinAt = (mx, mz); pinned = 0; return false; }   // a creep of 1 is no move
        if (++pinned < 200) return false;
        if (pinned == 200) Senses.GiveUp(mx, mz);
        return true;
    }

    /// <summary>Stop, then Space (Act presses it once the speed is 0).</summary>
    Wish Leave() { wantLeave = true; return Wish.Stand; }

    /// <summary>In a vehicle, with a place to go: a ride drives there and stops short of it for him to get out; a tank with
    /// shells fights.</summary>
    Wish Ride(Play play, Entity me, List<int> open, int px, int pz)
    {
        Doing = "driving";
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        if (Body.Flies(me)) return FlyRide(play, me, open, px, pz);
        if (me.Bit(0x08, 10)) return BoatRide(play, me, open, px, pz);
        if (target is null)
        {
            if (Dist(mx, mz, px, pz) > Sight / 2 + Standoff) return Travel(play, me, px, pz, Sight / 2, "to a target") ?? Leave();
            if (!ArmedTank(me) && !ArmedCar(me)) return Leave();
            target = Nearest(s, open, px, pz);
            if (target is null && Noticing(s, open, px, pz)) return new(false, me.B[0x7E], false, Hold: true);   // taking it in
            if (target is null) { Doing = "out to look round"; return Leave(); }   // nothing in sight: on foot, round the buildings
            firing = 0;
        }
        if (ArmedCar(me)) return CarRun(play, me, target);
        if (!ArmedTank(me)) return Leave();                                  // the shells are gone: on foot
        var t = target;
        int tx = t.UW(0x4A), tz = t.UW(0x4E);
        var half = Math.Max(Math.Abs((int)t.W(0x6E)), Math.Abs((int)t.W(0x72)));
        var r = Standoff + half;
        var d = Math.Sqrt(Math.Pow(Senses.Diff(tx, mx), 2) + Math.Pow(Senses.Diff(tz, mz), 2));
        if (d > r + 200 || d < half + 150)                                   // to the standoff on the line from the target to him
        {
            var away = Wish.Toward(tx, tz, mx, mz) * Math.PI / 128;
            int sx = (ushort)(tx + (int)Math.Round(r * Math.Sin(away))), sz = (ushort)(tz + (int)Math.Round(r * Math.Cos(away)));
            var w = Walk(play, sx, sz, 96);
            if (walk!.NoWay) return Leave();
            return w;
        }
        walk = null;
        var h = Wish.Toward(mx, mz, tx, tz);
        var tol = Math.Max(1, (int)(Math.Atan2(half, d) * 128 / Math.PI) / 2);
        var on = Math.Abs((int)(sbyte)(h - me.B[0x7E])) <= tol && me.W(0x3E) == 0;
        if (on && ++firing > Patience) return Leave();                       // no end in sight: try on foot
        return new(false, h, on, Hold: true);
    }

    /// <summary>Height over the ground kept in the air, and over a target's ground on a missile run.</summary>
    public const int Clearance = 350, RunHeight = 400;
    /// <summary>The bomb leaves only past $258 over the ground ($30C4): the bomber comes in higher.</summary>
    public const int BombHeight = 0x2EE;

    /// <summary>The tilt a helicopter cruises at: most of the lean its top speed less one can hover at (acos(gravity / (top -
    /// 1))): the bomber (top 43) about 10 bytes, the missile one (48) about 18.</summary>
    static int Cruise(Entity v) => Cruise(v.W(0x3A), v.W(0x36));
    static int Cruise(int gravity, int top) =>
        (int)(Math.Acos(Math.Min(1, gravity / Math.Max(1.0, top - 1))) * 128 / Math.PI * 0.8);

    /// <summary>How far a helicopter goes over the ground each blank at the cruise tilt: its step (speed, $2AE6) is tipped by
    /// the pitch first ($2C00, Movement.Vector), so only top x sin(tilt) goes along the ground - the missile one ~20 at 48.</summary>
    public static int CruiseStep(int gravity, int top) => Math.Max(1, (int)Math.Round(top * Math.Sin(Cruise(gravity, top) * Math.PI / 128)));

    /// <summary>The highest ground under the way from him toward (x, z), the next 2048 units, sampled every 128 - and the top
    /// of any thing in sight that would stop him (NearSight.InTheWay at any height), or a blast, whose box meets his along that way: a
    /// touch puts an aircraft back where it was ($44FC), so one flown into a light house's corner hangs there.</summary>
    internal static int GroundAhead(WorldState s, Entity me, int x, int z)   // DemoFollow's helicopter too
    {
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        double dx = Senses.Diff(x, mx), dz = Senses.Diff(z, mz);
        var len = Math.Max(1, Math.Sqrt(dx * dx + dz * dz));
        var reach = Math.Min(len, 2048);
        var top = 0;
        for (var d = 0.0; d <= reach; d += 128)
            top = Math.Max(top, s.Ground.Height((ushort)(mx + (int)(dx / len * d)), (ushort)(mz + (int)(dz / len * d))));
        double ux = Math.Abs(dx) / len, uz = Math.Abs(dz) / len;
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            var blast = e.W(4) is >= 0x61 and <= 0x67;             // a blast hurts what touches it ($454E): kept above (M4 11-08: down
            if (!e.Alive || e == me || e.Flag(30) || e.Flag(21) || e.Flag(20) && !blast || e.Flag(16)) continue;   // into the shark sign's, 51 off) - not shots or items; movers too (11-08 M3: a parked $0E, a mover, 456 tall - not counted, he hung on its side 12000 passes)
            double ex = Senses.Diff(e.UW(0x4A), mx), ez = Senses.Diff(e.UW(0x4E), mz);
            if (Math.Sqrt(ex * ex + ez * ez) > Sight) continue;                // out of his sight
            var along = (ex * dx + ez * dz) / len;
            var across = Math.Abs(ex * dz - ez * dx) / len;
            double hx = Math.Abs((int)e.W(0x6E)) + Math.Abs((int)me.W(0x6E)) + NearSight.Clearance,   // the two boxes, square to the map as the game
                hz = Math.Abs((int)e.W(0x72)) + Math.Abs((int)me.W(0x72)) + NearSight.Clearance;      // meets them: on a slant a box reaches up to 1.4 halves
            if (along < -(hx * ux + hz * uz) || along > reach + hx * ux + hz * uz || across > hx * uz + hz * ux) continue;   // across (M5 11-08: the light
                                                                             // house's corner again, 281 off a NE line, "clear" by the round test; hung 14000 passes)
            top = Math.Max(top, e.W(0x4C) + 2 * e.W(0x70));                    // its top: +$4C, the box's middle +$70 up, +$70 again
        }
        return top;
    }

    /// <summary>Fly toward (x, z), `over` above the ground ahead (and at least `floor`), leaning less as it nears (a hover over
    /// the point) - `lean` false keeps the cruise tilt (a run).</summary>
    Wish FlyTo(Play play, Entity me, int x, int z, int over, int floor = 0, bool lean = true, bool fire = false)
    {
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        var d = Dist(mx, mz, x, z);
        var tilt = lean ? Math.Clamp(d / 30, 0, Cruise(me)) : Cruise(me);
        var alt = Math.Max(GroundAhead(play.State, me, x, z) + over, floor);
        return new(true, Wish.Toward(mx, mz, x, z), fire, false, alt, tilt);
    }

    /// <summary>Riding inside the reach of a gun he knows of that fires at what he is in (seen late): out of it the shortest
    /// way first - across the box's nearer edge, a square past it - and the trip planned from there (M5 11-08: no way from
    /// inside, every way on goes nearer the gun). Null: in no such reach.</summary>
    const int BackTilt = 20;                                                 // backing out: the pitch leaned back (40 sank him to the ground)
    /// <summary>Backing out: the height over the ground behind him (tries.py sets it per try, by reflection).</summary>
    static int BackOver = Clearance;
    Wish? OutOfReach(Play play, Entity me, IReadOnlyList<Trip.Danger> dangers)
    {
        int mx = me.UW(0x4A), mz = me.UW(0x4E), status = Trip.StatusOf(me.Flags);
        var flies = Body.Flies(me);
        int step = flies ? CruiseStep(me.W(0x3A), me.W(0x36)) : me.W(0x36), m = step * Trip.MarginBlanks;
        var firing = dangers.Where(d => (d.Mask & status) != 0).ToList();
        bool In(Trip.Danger d, int x, int z, int margin) => Math.Abs(Senses.Diff(x, d.X)) <= d.BoxX + margin && Math.Abs(Senses.Diff(z, d.Z)) <= d.BoxZ + margin;
        if (outTo is var (hx, hz, hd) && (Dist(mx, mz, hx, hz) <= OutNear || !firing.Any(d => Dist(d.X, d.Z, hd.X, hd.Z) < 256)
            || !firing.Any(f => In(f, mx, mz, m + Navigate.Square)))) outTo = null;   // there, the gun gone, or clear of all as the point is
        var inside = firing.FirstOrDefault(d => In(d, mx, mz, m));
        if (outTo is null && inside == default) return null;
        var d = outTo?.D ?? inside;
        int ox, oz;
        if (outTo is var (tx, tz, _)) (ox, oz) = (tx, tz);                   // THE WAY OUT HELD to its point (M8 11-09: let go at the margin's line,
        else                                                                 // the trip turned him back in, a boat stopped to turn every 32 frames)
        {                                                                    // - and clear of EVERY reach he knows of, not the one he is in (M8 11-09:
            var boat = !flies && me.Bit(0x08, 10);                           // between two rocket launchers, out of each the nearest edge was in the
            (ox, oz) = OutPoint(play.State, mx, mz, p => !firing.Any(f => In(f, p.X, p.Z, m + Navigate.Square))   // other's margin: east, west, east -
                && (flies || Navigate.Sea(play.State, p.X, p.Z) == boat)) ?? Edge(d, mx, mz, m);   // SitTrace; out the south, 950, both clear)
            outTo = (ox, oz, d);
        }
        Doing = "out of a gun's reach";
        if (flies)                                                           // facing well away from the way out: upright to slow and turn tight
        {                                                                    // (M6 11-08: a wide turn at the cruise lean carried him in deeper)
            var outWay = Wish.Toward(mx, mz, ox, oz);
            if (Math.Abs((int)(sbyte)(outWay - me.B[0x7E])) > 64)            // the way out behind him: BACK out, tail first (M10 11-09, EscapeProbe on
                return new(true, (byte)(outWay + 128), false, false,         // a copy of the moment he saw the gun: turning round, shot down at +54;
                    GroundAhead(play.State, me, ox, oz) + BackOver, -BackTilt);   // backing, out at +44 untouched - a half turn takes 120 passes)
            var fw = FlyTo(play, me, ox, oz, Clearance, lean: false);
            return Math.Abs((int)(sbyte)(fw.Heading - me.B[0x7E])) > 32 ? fw with { Tilt = 0 } : fw;
        }
        var w = Walk(play, ox, oz, OutNear);
        if (walk!.NoWay) { outTo = null; return null; }
        return w;
    }
    /// <summary>The way out of a reach being taken: its point, and the gun it is out of.</summary>
    (int X, int Z, Trip.Danger D)? outTo;
    const int OutNear = 64;

    /// <summary>The nearest point round (x, z) that `clear` takes: rings a half square apart out to OutLook, 32 ways each.</summary>
    static (int X, int Z)? OutPoint(WorldState s, int x, int z, Func<(int X, int Z), bool> clear)
    {
        for (var r = Navigate.Square / 2; r <= OutLook; r += Navigate.Square / 2)
            for (var k = 0; k < 32; k++)
            {
                var a = k * Math.PI / 16;
                var p = ((int)(ushort)(x + (int)(r * Math.Sin(a))), (int)(ushort)(z + (int)(r * Math.Cos(a))));
                if (clear(p)) return p;
            }
        return null;
    }
    const int OutLook = 8 * Navigate.Square;

    /// <summary>A square past the reach's nearer edge (none clear of all: the one he is in).</summary>
    static (int X, int Z) Edge(Trip.Danger d, int mx, int mz, int m)
    {
        int dx = Senses.Diff(mx, d.X), dz = Senses.Diff(mz, d.Z), ex = d.BoxX + m - Math.Abs(dx), ez = d.BoxZ + m - Math.Abs(dz);
        return ex <= ez ? ((ushort)(mx + Math.Sign(dx == 0 ? 1 : dx) * (ex + Navigate.Square)), mz)
            : (mx, (ushort)(mz + Math.Sign(dz == 0 ? 1 : dz) * (ez + Navigate.Square)));
    }

    /// <summary>In the air, the gauge watched: the nearest dry spot (rings out to FuelLook) when what is left (Trip.Lasts) only
    /// covers the flight there at the cruise step, the way down (Body.Fly sinks 1-4 a blank: a third of the height) and
    /// FuelMargin - else null. Empty in the air it falls and is destroyed (M4 11-08 twice, M5 once: fuel 0 aloft).</summary>
    (int X, int Z)? LowFuel(WorldState s, Entity me)
    {
        var left = Trip.Lasts(me);
        if (left > FuelWatch) return null;
        int mx = me.UW(0x4A), mz = me.UW(0x4E), down = Math.Max(0, me.W(0x4C) - me.W(0x74)) / 3 + FuelMargin;
        var step = CruiseStep(me.W(0x3A), me.W(0x36));
        if (landingFor is var (lx, lz) && Dry(s, lx, lz, DryMargin)) return (lx, lz);   // once going, kept
        for (var r = 0; r <= FuelLook; r += 256)
            for (var k = 0; k < (r == 0 ? 1 : 16); k++)
            {
                var a = k * Math.PI / 8;
                int x = (ushort)(mx + (int)(r * Math.Sin(a))), z = (ushort)(mz + (int)(r * Math.Cos(a)));
                if (!Dry(s, x, z, DryMargin)) continue;
                if (left > Dist(mx, mz, x, z) / step + down) return null;   // enough left to go on
                landingFor = (x, z);
                return (x, z);
            }
        return null;                                                         // no land in reach: on as planned
    }
    const int FuelWatch = 3000, FuelLook = 4096, FuelMargin = 150;
    (int X, int Z)? landingFor;

    /// <summary>Land by the game's own point test ($2B0E, Ground.Sample - what Settle destroys a flier on the sea by) at (x, z)
    /// and `margin` off it each way: a land map square's shore can be sea (M4 11-08: down softly at 37421,19610, Navigate.Land's
    /// square land, the point sea - the helicopter destroyed).</summary>
    static bool Dry(WorldState s, int x, int z, int margin) =>
        new[] { (0, 0), (margin, 0), (-margin, 0), (0, margin), (0, -margin) }
            .All(d => s.Ground.Sample((ushort)(x + d.Item1), (ushort)(z + d.Item2)).Item2);
    const int DryMargin = 64;

    /// <summary>Room to come down at (x, z): no other thing's box within his and a gap to walk (64), and one of the four spots
    /// he can be put out on clear (PlaceBeside $4372: twice his half-width +x, -x, +z, -z) - M4 11-08: down 304 from the
    /// helicopter he went for, out on its far side and back into his own on the way round. Not all four (M5 11-08: no spot
    /// within 516 of the one he went for, down 43 from where he sat - out, in, out). Shots and items do not count.</summary>
    static bool RoomAt(WorldState s, Entity me, int x, int z)
    {
        int hx = Math.Abs((int)me.W(0x6E)), hz = Math.Abs((int)me.W(0x72)), d = 2 * hx;
        bool Clear(int cx, int cz, int rx, int rz)
        {
            foreach (var e in s.Entities.All)
            {
                if (!e.Alive || e == me || e.Flag(20) || e.Flag(16) || e.Flag(30) || e.Flag(21)) continue;
                if (Math.Abs(Senses.Diff(e.UW(0x4A), cx)) < rx + Math.Abs((int)e.W(0x6E)) && Math.Abs(Senses.Diff(e.UW(0x4E), cz)) < rz + Math.Abs((int)e.W(0x72))) return false;
            }
            return true;
        }
        return Clear(x, z, hx + 64, hz + 64)
            && new[] { (d, 0), (-d, 0), (0, d), (0, -d) }.Any(o => Clear((ushort)(x + o.Item1), (ushort)(z + o.Item2), ManHalf, ManHalf));
    }
    const int ManHalf = 16;                                                  // the man's box (12 x 10) and a little
    const int LandLook = 1024;

    /// <summary>The spot he is going down on (LandNear), held while he drifts within 400 of it.</summary>
    (int X, int Z)? descending;
    /// <summary>The last LandNear found no spot at all.</summary>
    bool noSpot;

    /// <summary>A landing spot `off` from (x, z), dry and with room (RoomAt), on his side first, else round the compass, else
    /// a ring further out (to LandLook more); the spot he is going down on is kept while dry. Down there, rotor stopped, out.</summary>
    Wish LandNear(Play play, Entity me, int x, int z, int off = 500)
    {
        Doing = "landing";
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        var back = Wish.Toward(x, z, mx, mz);
        (int X, int Z)? spot = descending is var (ox, oz) && Dist(ox, oz, x, z) <= off + LandLook && Dry(s, ox, oz, DryMargin) ? descending : null;
        for (var r = off; spot is null && r <= off + LandLook; r += 128)
            for (var k = 0; k < (r == 0 ? 1 : 8) && spot is null; k++)
            {
                var a = (byte)(back + 32 * k) * Math.PI / 128;
                int sx = (ushort)(x + (int)(r * Math.Sin(a))), sz = (ushort)(z + (int)(r * Math.Cos(a)));
                if (Dry(s, sx, sz, DryMargin) && RoomAt(s, me, sx, sz)) spot = (sx, sz);
            }
        noSpot = spot is null;
        if (spot is not var (lx, lz)) { descending = null; return FlyTo(play, me, x, z, Clearance); }
        var keep = descending == (lx, lz) ? 400 : 150;                       // once going down, a drift off the spot does not start it over
        if (Dist(mx, mz, lx, lz) > keep || !Dry(s, mx, mz, 0)) { descending = null; return FlyTo(play, me, lx, lz, Clearance); }
        descending = (lx, lz);
        if (me.B[0x1E] != 0 && me.W(0x3E) == 0) return Leave();
        return new(false, me.B[0x7E], false);                                 // down where it is: Body.Fly sinks it softly
    }

    /// <summary>In a helicopter with a place to go. Armed: at the place's target, runs that fire whenever the shot's path
    /// would meet it (Arms.WouldHit) - missiles from a run at RunHeight, the bomber a slow pass over it at BombHeight. A ride,
    /// out of rounds, or no end in sight: down near the place, and out.</summary>
    Wish FlyRide(Play play, Entity me, List<int> open, int px, int pz)
    {
        Doing = "flying";
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        int status = Trip.StatusOf(me.Flags), step = CruiseStep(me.W(0x3A), me.W(0x36));
        var dangers = KnownDangers(s, me);
        if (OutOfReach(play, me, dangers) is { } away) return away;          // runs at a target too (M6 11-09: shot down by a SAM
                                                                             // gun he knew of, 450 off, making runs on another)
        var covered = Trip.Covered(dangers, px, pz, status, step)              // a gun he knows of fires at aircraft there,
            || target is null && Dist(mx, mz, px, pz) > Sight / 2 && Trip.FlightCrosses(dangers, (mx, mz), (px, pz), status, step);   // or on the straight way there
        var need = Dist(mx, mz, px, pz) / step + (Known(s, 0xAA) is var (hx, hz) ? Dist(px, pz, hx, hz) / step : 0) + FuelMargin;
        var gaugeShort = target is null && me.Flag(26) && Trip.Lasts(me) < need;   // the gauge short of there and back to the HQ: the trip
                                                                             // planner, which knows it (M4 11-08: in a helicopter of unknown gauge,
                                                                             // straight for the place, down for fuel short of it, stranded)
        if (!Armed(me) || covered || gaugeShort || firing > 2 * Patience) return Travel(play, me, px, pz, Sight / 2, "to a target") ?? Leave();
        if (target is null)
        {
            if (Dist(mx, mz, px, pz) > Sight / 2)
            {
                return FlyTo(play, me, px, pz, Clearance, lean: false);
            }
            target = Nearest(s, open, px, pz);
            if (target is null && Noticing(s, open, px, pz)) return Wish.Stand with { Go = true, Alt = me.W(0x4C) };   // a hover: taking it in
            if (target is null) { done.Add(place!.Value); place = null; return Wish.Stand with { Go = true, Alt = me.W(0x4C) }; }
            firing = 0;
        }
        var t = target;
        firing++;
        var hit = Arms.WouldHit(s, me, t);
        var tg = t.W(0x4C);
        if (me.W(0x26) == 0x5A)                                               // the bomber: over it, slow, high
            return FlyTo(play, me, t.UW(0x4A), t.UW(0x4E), Clearance, tg + BombHeight, lean: true, fire: hit);
        return FlyTo(play, me, t.UW(0x4A), t.UW(0x4E), Clearance, tg + RunHeight, lean: false, fire: hit);
    }

    static bool Boat(Entity e) => e.Alive && e.W(4) < 0x13 && e.Flag(28) && (e.Flag(10) || e.Flag(1));   // a boat, or an amphibian (bit 1)

    /// <summary>The boats: rocket boat $08, ship $09, gunboat $0C, wind surfer $10 (the submarine $0A cannot be entered).</summary>
    static readonly int[] BoatTypes = [0x08, 0x09, 0x0C, 0x10, 0x0E, 0x12];   // + the hovercraft and the amphibian
    (int Type, int X, int Z)? leftIn;
    int? leftLasts;

    /// <summary>A vehicle he could get into: an empty land vehicle or aircraft, or an empty boat (the amphibian too).</summary>
    public static bool Boardable(Entity e) => LandVehicle(e) || Boat(e);

    // ---- TRIPS (docs/bot-brain.md 2) ----
    readonly Trip trip = new();
    int legPasses, travelled = int.MinValue;
    /// <summary>What the last "no way" was planned from: the goal, his square, what he is in, how many rides and guns he knows
    /// of, his energy, whether his drone has looked round there.</summary>
    (int, int, (int, int), int, int, int, int, bool) noWayAsked;
    int scoutDepth;

    /// <summary>A leg overdue - this many times its planned blanks, and StartAllowance more for turning and starting off - is
    /// given up (a vehicle he could not reach is let be) and the trip planned again.</summary>
    const int Late = 3, StartAllowance = 200;

    /// <summary>The vehicles he knows of: riding, the one he is in first; then each parked one of his vehicle memory
    /// (Senses.Cars - seen, shown by the drone, or left by him), last known empty, not given up.</summary>
    List<Trip.Ride> KnownRides(WorldState s, Entity me)
    {
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        var o = new List<Trip.Ride>();
        if (!me.Bit(0x08, 15)) o.Add(new(me, me.W(4), mx, mz, me.Flag(26) ? Trip.Lasts(me) : null));
        foreach (var c in Senses.Cars)                                       // one entry each (Senses.Car): parked, empty, not given up
            if (c.State == Senses.CarState.Parked && c.Free && EverEmpty(s, c.Type) && !Senses.GivenUp(c.X, c.Z, 256))
            {
                var e = c.E is { Alive: true } ce && ce.W(4) == c.Type && ce != me && Dist(ce.UW(0x4A), ce.UW(0x4E), c.X, c.Z) < 256 ? ce : null;
                o.Add(new(e, c.Type, c.X, c.Z, c.Lasts));
            }
        return o;
    }

    /// <summary>A kind ever found empty - so ever boardable (TouchVehicle wants +$06 bit 28): a new one's flags are its kind's
    /// first words (KindProbe 11-09: 1C48 -> 1C40xxxx), and only the submarine's ($0A, 0AA0) lacks bit 28 - always crewed
    /// (seen 4x, never empty). M8 11-09: planned twice to board one from the drone map, drowned swimming out to it.</summary>
    static bool EverEmpty(WorldState s, int type) => !NoCrewed || (Arms.Kind(s, type, 0) & 0x1000) != 0;
    /// <summary>Switches for tries (tries.py sets them by reflection, a process each): the lessons of 11-09 on or off.</summary>
    static bool NoCrewed = true, SwimWalks = false, HiddenSpot = true;                          // SwimWalks OFF: it saved M7 but broke M9 and M6 (MissionTry
                                                                             // 11-09, each switch alone) - held till what it does there is shown

    /// <summary>The ground guns: the emplacements (SAM launcher $68 to tracer gun $6E, docs/data/flags.md).</summary>
    static readonly int[] GunTypes = [0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E];

    /// <summary>The guns he knows of: each thing noted armed against him and not seen gone, and the ground guns his drone looks
    /// showed - with its fire box and its mask (Trip.Danger).</summary>
    List<Trip.Danger> KnownDangers(WorldState s, Entity me) => [.. KnownGuns(s).Select(g => g.D)];

    /// <summary>KnownDangers with each one's type.</summary>
    List<(int Type, Trip.Danger D)> KnownGuns(WorldState s)
    {
        var o = new List<(int, Trip.Danger)>();
        void Add(int type, int x, int z)
        {
            int bx = Arms.Kind(s, type, 0x1E), bz = Arms.Kind(s, type, 0x22), mask = Arms.Kind(s, type, 0x1C) & 0xFF;
            if (Arms.Kind(s, type, 6) > 0 && mask != 0 && !o.Any(d => Dist(d.Item2.X, d.Item2.Z, x, z) < 256)) o.Add((type, new(x, z, bx, bz, mask)));
        }
        foreach (var n in Senses.Notes) if (n.Threat && !n.Gone) Add(n.Type, n.X, n.Z);
        foreach (var (t, x, z) in Senses.Looked) if (GunTypes.Contains(t)) Add(t, x, z);
        return o;
    }

    // ---- SCOUTING ----
    /// <summary>The unseen land he is going to look over first, for which goal, and the vehicles he knew of then (one more
    /// known ends it: the goal is planned again with it); spots he found no way to.</summary>
    (int X, int Z)? scout;
    (int, int) scoutFor;
    int scoutRides;
    bool scouting;
    readonly HashSet<(int X, int Z)> scoutFailed = [];
    /// <summary>What he went to look over and why - for the run log.</summary>
    public string? ScoutWhy { get; private set; }
    /// <summary>A way this long on foot (walks and swims, passes: half a minute) is worth a look for a vehicle near it.</summary>
    const int LongFoot = 1500;
    /// <summary>Spots tried for a way each time (the nearest first): a plan each.</summary>
    const int ScoutTries = 6;

    static double FootTime(List<Trip.Leg>? legs) => legs?.Where(l => l.Kind is Trip.Kind.Walk or Trip.Kind.Swim).Sum(l => l.Time) ?? 0;

    /// <summary>The middle of the nearest unseen land square (the game's point test) he has a way to - with `limit` (passes):
    /// one he reaches within it, nearer the goal than he is (on the way); without (stuck): any - before the last resort.</summary>
    (int X, int Z)? PickScout(WorldState s, Entity me, IReadOnlyList<Trip.Ride> rides, IReadOnlyList<Trip.Danger> dangers, int gx, int gz, double? limit)
    {
        int mx = me.UW(0x4A), mz = me.UW(0x4E), foot = Arms.Kind(s, 0x1E, 0x12);
        var toGoal = Dist(mx, mz, gx, gz);
        var spots = Senses.Unseen().Select(Senses.Middle)
            .Where(c => !scoutFailed.Contains(c) && s.Ground.Sample((ushort)c.X, (ushort)c.Z).Item2
                && (limit is not { } l || Dist(mx, mz, c.X, c.Z) <= l * foot && Dist(c.X, c.Z, gx, gz) < toGoal))
            .OrderBy(c => Dist(mx, mz, c.X, c.Z)).Take(ScoutTries);
        foreach (var c in spots)
        {
            var probe = new Trip();
            probe.Plan(s, me, rides, dangers, foot, c.X, c.Z, Sight / 2);   // a plain way only: no last-resort swims to look
            if (probe.Legs is { } legs && (limit is not { } l2 || legs.Sum(g => g.Time) <= l2)) return c;
            if (probe.Legs is null && limit is null) scoutFailed.Add(c);    // no way there: not asked again
        }
        return null;
    }

    // ---- GUNS FIRST ----
    /// <summary>The gun he went to end before a flight: its type and where he knows it; and those he gave up on.</summary>
    (int Type, int X, int Z)? gunFirst;
    readonly List<(int X, int Z)> gunsLeft = [];
    int gunPasses, gunShots, gunKills;
    Entity? gunSeen;
    /// <summary>The guns last found in a flight's way and what he made of each - for the run log.</summary>
    public string? GunWhy { get; private set; }

    /// <summary>On foot, his trip to the place boarding an aircraft: a gun he knows of (seen, or on a drone's map - KnownGuns)
    /// that fires at that aircraft but not at him on foot, in the flight's straight way from where he boards to the place
    /// (Trip.FlightCrosses - the way the plan flies goes round it, or lands short), on his own land, and that what he
    /// carries ends (its energy: seen, else its kind's +$0A) - walked to and shot first, the nearest. Held till it ends,
    /// is not there, or cannot be got at. Null: none.</summary>
    Wish? GunFirst(Play play, Entity me, int px, int pz)
    {
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        if (gunFirst is null)
        {
            if (!trip.For(px, pz) || trip.Legs is not { } legs || trip.Current is not { } now) return null;
            var board = legs.Skip(legs.IndexOf(now)).FirstOrDefault(l => l.Kind == Trip.Kind.Board && l.Vehicle is { } v && Trip.ModeOf(Trip.FlagsOf(s, v)) == Trip.Mode.Air);
            if (board?.Vehicle is not { } ride) return null;
            int st = Trip.StatusOf(Trip.FlagsOf(s, ride)), step = Trip.SpeedOf(s, ride), bd = int.MaxValue;
            var why = new List<string>();
            foreach (var (type, d) in KnownGuns(s))
            {
                if ((d.Mask & st) == 0 || !Trip.FlightCrosses([d], (board.X, board.Z), (px, pz), st, step)) continue;
                var at = $"${type:X2} at {d.X},{d.Z}";
                if ((d.Mask & Trip.FootStatus) != 0) { why.Add(at + ": fires on foot too"); continue; }
                if (gunsLeft.Any(g => Dist(g.X, g.Z, d.X, d.Z) < 256)) { why.Add(at + ": given up"); continue; }
                if (!Trip.SameLand(s, mx, mz, d.X, d.Z)) { why.Add(at + ": not on his land"); continue; }
                var e = GunAt(s, type, d.X, d.Z);
                var energy = Math.Max(0, e is null ? Arms.Energy(s, type) : (int)e.W(0x2A));
                var arm = Arms.Choose(s, energy);
                if (arm < 0 || Arms.Hits(s, arm, energy) > Arms.Rounds(s, arm)) { why.Add(at + $": cannot end it (energy {energy}, weapon {arm})"); continue; }
                why.Add(at + ": going for it");
                var dd = Dist(mx, mz, d.X, d.Z);
                if (dd < bd) { bd = dd; gunFirst = (type, d.X, d.Z); }
            }
            GunWhy = why.Count == 0 ? null : "guns in the flight's way: " + string.Join("; ", why);
            if (gunFirst is null) return null;
            gunPasses = gunShots = 0; gunKills = s.Kills[gunFirst.Value.Type]; trip.Drop("going for a gun"); NewAim(); firing = 0;
        }
        var (gt, gx, gz) = gunFirst.Value;
        var onFoot = me.Bit(0x08, 15);
        if (gunSeen is { } g0 && !(g0.Alive && g0.W(4) == gt)) gunSeen = null;   // its slot now something else (a kill, or re-used far off - M2 11-08)
        var gun = gunSeen ?? GunAt(s, gt, gx, gz);
        string let;
        if (s.Kills[gt] > gunKills) let = "seen it end";                    // the kill count of its kind went up ($9288): ended, not looked for
        else if (gun is not null && (gun == gunSeen || Senses.SeesNow(gun)) && onFoot)
        {
            gunSeen = gun;
            if (++gunShots >= 4 * Patience) { gunsLeft.Add((gx, gz)); let = "shot at too long"; }   // no end to it: the flight goes round after all
            else
            {
                var sw = Shoot(play, me, gun);
                if (gaveUp != gun && stocked) { Doing = "shooting a gun in the flight's way"; return sw; }
                gunsLeft.Add((gx, gz));
                let = gaveUp == gun ? "his shots do not hurt it"                // the flight goes round
                    : $"out of rounds, its energy {gun.W(0x2A)}";                 // off to the stores: not come back for from there (M2 11-08: a 337 s way back)
                gaveUp = null;
            }
        }
        else if (Dist(mx, mz, gx, gz) > Sight / 2 || !onFoot)
        {
            var w = Travel(play, me, gx, gz, Sight / 2, "to a gun in the flight's way", air: false);
            if (!Doing.EndsWith(": no way")) return w ?? Wish.Stand;
            gunsLeft.Add((gx, gz)); let = "no way to it" + (onFoot ? "" : " from the vehicle he is in");
        }
        else if (++gunPasses < Patience) { Doing = "looking for a gun in the flight's way"; return Wish.Stand; }
        else let = "not there";
        GunWhy = $"let go of the ${gt:X2} at {gx},{gz}: {let}";
        Senses.Looked.RemoveAll(l => l.Type == gt && Dist(l.X, l.Z, gx, gz) < 256);   // ended, or not there: off his drone map's list
        gunFirst = null; gunSeen = null; trip.Drop("gun let go"); firing = 0;
        return null;
    }

    /// <summary>The live thing of this type nearest (x, z) within 256: the gun a note or a drone map put there.</summary>
    static Entity? GunAt(WorldState s, int type, int x, int z)
    {
        Entity? best = null; var bd = 256;
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            if (!e.Alive || e.W(4) != type) continue;
            var d = Dist(x, z, e.UW(0x4A), e.UW(0x4E));
            if (d <= bd) (best, bd) = (e, d);
        }
        return best;
    }

    /// <summary>To (gx, gz), on foot within `near` at the end: the trip's current leg, planned when there is none, held, and
    /// planned again from where he is at a leg's end, when he is not in what the leg needs (walked into a vehicle, thrown out),
    /// when its vehicle is given up or seen gone, or when it is overdue. Null: there. No way: he stands.</summary>
    Wish? Travel(Play play, Entity me, int gx, int gz, int near, string why, bool air = true)
    {
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        var onFoot = me.Bit(0x08, 15);
        if (onFoot && Dist(mx, mz, gx, gz) <= near) { trip.Drop("there"); return null; }
        if (scout is { } sc && !scouting)                                    // SCOUTING: unseen land to look over first
        {
            if (Senses.Seen(sc.X, sc.Z) || scoutFor != (gx, gz) || KnownRides(s, me).Count > scoutRides) scout = null;   // looked at, another goal, a vehicle found
            else
            {
                scouting = true;
                var sw = Travel(play, me, sc.X, sc.Z, Sight / 2, why + ": scouting", air);
                scouting = false;
                if (sw is not null && (trip.Legs is not null || wantDrone || DroneUp(s) || onMap)) return sw;
                if (sw is not null) scoutFailed.Add(sc);                     // no way there after all
                scout = null; trip.Drop("scouted");
            }
        }
        var dangers = KnownDangers(s, me);
        if (!onFoot && OutOfReach(play, me, dangers) is { } away) { trip.Drop("in a gun's reach"); return away; }
        var known = KnownRides(s, me);
        if (!air) known.RemoveAll(r => r.E != me && Trip.ModeOf(Trip.FlagsOf(s, r)) == Trip.Mode.Air);   // no aircraft (to a gun that fires at them)
        if (!trip.For(gx, gz)) trip.Drop("another goal");                    // held only while followed (something else steered between) and while he knows
        else if (travelled != passes - 1) trip.Drop("something else steered");   // of no new gun nor a new ride
        else if (dangers.Count != trip.Dangers.Count) trip.Drop("guns known changed");
        else if (known.Count > trip.RidesKnown) trip.Drop("a new vehicle known");
        travelled = passes;
        if (trip.Current is { } held)
        {
            var late = ++legPasses > Late * held.Time + StartAllowance && !(held.Kind == Trip.Kind.Fly && descending is not null);   // a landing under way is not overdue: the plan's time leaves the way down out
            if (Ended(s, me, held)) trip.Drop("leg done");
            else if (Foot(held.Kind) != onFoot || Broken(held) || late)
            {
                if (late && held.Vehicle is { } lv && held.Kind == Trip.Kind.Board) Senses.GiveUp(lv.X, lv.Z);
                trip.Drop(Foot(held.Kind) != onFoot ? (onFoot ? "out of the vehicle unplanned" : "in a vehicle unplanned") : late ? "leg overdue" : "its vehicle given up or gone");
            }
        }
        if (trip.Legs is null)
        {
            var rides = known;
            var looked = Drones(s) == 0 || Senses.LookedNear(mx, mz);
            var ask = (gx, gz, Square: (mx >> 9, mz >> 9), Type: me.W(4), Rides: rides.Count, Dangers: dangers.Count, (int)s.Energy, looked);
            if (ask != noWayAsked)                                           // a "no way" is asked again only when something it rests on changed
            {
                var home = why.StartsWith("home") ? null : Known(s, 0xAA);   // a trip out must leave him a way back (round trip)
                trip.Plan(s, me, rides, dangers, Arms.Kind(s, 0x1E, 0x12), gx, gz, near, home: home);
                if (home is not null)                                        // ONE WAY: far quicker without a way back
                {                                                            // known (M5: a helicopter 53 s off, a wind surfer 350 s round trip) -
                    var safe = trip.Legs?.Sum(l => l.Time);                  // there, the way home is planned from what is found
                    trip.Plan(s, me, rides, dangers, Arms.Kind(s, 0x1E, 0x12), gx, gz, near);
                    if (trip.Legs?.Sum(l => l.Time) is not double one || safe is double sf && sf <= OneWay * one)
                        trip.Plan(s, me, rides, dangers, Arms.Kind(s, 0x1E, 0x12), gx, gz, near, home: home);
                }
                if (trip.Legs is null && looked && !scouting && scoutDepth < 3 && PickScout(s, me, rides, dangers, gx, gz, null) is { } st)   // stuck: unseen land
                {                                                            // to look over before any last resort (M4 11-09: drowned on a long swim first)
                    (scout, scoutFor, scoutRides) = (st, (gx, gz), rides.Count);
                    ScoutWhy = $"scouting: unseen land at {st.X},{st.Z} (no way)";
                    scoutDepth++;                                            // a spot with no way to it picks the next in the same pass: a few,
                    try { return Travel(play, me, gx, gz, near, why, air); } // not every unseen square (M6 11-08: the stack overflowed)
                    finally { scoutDepth--; }
                }
                if (trip.Legs is null && looked)                             // no way, and his drone has looked round (or he has none): a LAST RESORT -
                    trip.Plan(s, me, rides, dangers, Arms.Kind(s, 0x1E, 0x12), gx, gz, near, lastResort: true, home: home);   // swims down to 2 energy left (M1 / M4 11-08)
                noWayAsked = trip.Legs is null ? ask : default;
                if (!Body.Flies(me) && !scouting && scoutDepth < 3 && FootTime(trip.Legs) > LongFoot   // a long way on foot: unseen land near it to look over
                                                                             // first - not from the air (M4 11-08: a helicopter flown square to square, its fuel gone)
                    && PickScout(s, me, rides, dangers, gx, gz, trip.Legs!.Sum(l => l.Time) / 4) is { } c)
                {
                    (scout, scoutFor, scoutRides) = (c, (gx, gz), rides.Count);
                    ScoutWhy = $"scouting: unseen land at {c.X},{c.Z} (near the way)";
                    scoutDepth++;
                    try { return Travel(play, me, gx, gz, near, why, air); }
                    finally { scoutDepth--; }
                }
            }
            legPasses = 0; walk = null;
            while (trip.Current is { } c && Ended(s, me, c)) trip.Next();   // what is done already (there in the vehicle, out)
        }
        if (trip.LastResort && trip.Legs is not null) why += " (last resort: a long swim)";
        Doing = why;
        wantDrone = false;                                                   // asked afresh each time: a way found since, or a look, and none goes up
        if (DroneUp(s) || onMap || play.Mode == PlayMode.Screen) { Doing = why + ": the drone up"; return Wish.Stand; }
        if (trip.Legs is null && Drones(s) > 0 && !Senses.LookedNear(mx, mz))   // no way over what he knows: look round first
        {
            wantDrone = true; Doing = why + ": launching the drone"; return Wish.Stand;
        }
        if (trip.Legs is null) { Doing = why + ": no way"; return Wish.Stand; }
        if (trip.Current is not { } leg) { trip.Drop("all legs done"); return onFoot ? null : Leave(); }
        return Follow(play, me, leg);
    }

    static bool Foot(Trip.Kind k) => k is Trip.Kind.Walk or Trip.Kind.Swim or Trip.Kind.Board;

    bool Ended(WorldState s, Entity me, Trip.Leg l)
    {
        var onFoot = me.Bit(0x08, 15);
        var d = Dist(me.UW(0x4A), me.UW(0x4E), l.X, l.Z);
        return l.Kind switch
        {
            Trip.Kind.Walk => onFoot && d <= l.Near,
            Trip.Kind.Swim => onFoot && (d <= l.Near || !me.Bit(0x08, 10) && Navigate.Land(s, l.X, l.Z) && Trip.SameLand(s, me.UW(0x4A), me.UW(0x4E), l.X, l.Z)),   // ashore counts only for a swim to land: a boat's sea square takes the land beside it (AreaOf) - M4 11-09 stood on that beach, the swim "done"
            Trip.Kind.Board => !onFoot,
            Trip.Kind.Ride or Trip.Kind.Via => !onFoot && d <= l.Near,
            _ => onFoot,                                                     // Fly (landed, out), Out
        };
    }

    /// <summary>Its vehicle given up (occupied, not reached, wedged, out of fuel) or seen gone or taken.</summary>
    bool Broken(Trip.Leg l)
    {
        if (l.Vehicle is not { } r || l.Kind != Trip.Kind.Board) return false;
        if (Senses.Heard(Senses.Occupied) is { } o && (o.Args[0] & 0xFF) == r.Type) { Senses.GiveUp(r.X, r.Z); return true; }   // "The X is occupied": he read it
        if (Senses.GivenUp(r.X, r.Z, 256)) return true;
        return Senses.CarAt(r.Type, r.X, r.Z, r.E) is { } c && (c.State is Senses.CarState.Gone or Senses.CarState.NoUse || !c.Free);
    }

    /// <summary>One leg, by what exists: a walk (GoTo, his near sight), a swim straight across, boarding (to it, then straight
    /// into it), a drive or a boat's run (GoTo in the vehicle), a flight (LandNear: down by the place and out), out (Space).</summary>
    Wish Follow(Play play, Entity me, Trip.Leg l)
    {
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        switch (l.Kind)
        {
            case Trip.Kind.Walk:
            {
                var w = Walk(play, l.X, l.Z, l.Near);
                return walk!.NoWay ? new(true, Wish.Toward(mx, mz, l.X, l.Z), false) : w;
            }
            case Trip.Kind.Swim:                                             // by GoTo over land and sea: round what would board him (the boat just left)
            {
                Doing += ", swimming";
                var after = trip.Legs is { } ls && ls.IndexOf(l) is var at and >= 0 && at + 1 < ls.Count ? ls[at + 1] : (Trip.Leg?)null;
                var to = after is { Kind: Trip.Kind.Board, Vehicle.E: { } ve } && ve.Alive && Dist(ve.UW(0x4A), ve.UW(0x4E), l.X, l.Z) < 256 ? ve : null;
                var w = Walk(play, l.X, l.Z, l.Near, to, swim: true);           // to a vehicle the next leg boards: not round it (M1 11-09: the boat's
                                                                             // margin round his goal, its middle - the side with fewer of its cells
                                                                             // flipped with 2 units of his place, east / west for 7000 frames, drowned)
                return walk!.NoWay ? new(true, Wish.Toward(mx, mz, l.X, l.Z), false) : w;
            }
            case Trip.Kind.Board:
            {
                Doing += ", boarding";
                var v = l.Vehicle!.Value.E is { } e && Boardable(e) && e.W(4) == l.Vehicle.Value.Type ? e
                    : s.Entities.All.Skip(1).FirstOrDefault(e => Boardable(e) && e.W(4) == l.Vehicle.Value.Type
                        && Dist(e.UW(0x4A), e.UW(0x4E), l.X, l.Z) < 256 && Senses.SeesNow(e));
                if (v is null)                                               // a record's boat not yet in sight: to its place
                {
                    if (HalfDepth(s, l.Vehicle.Value.Type) is var hd && !Senses.InView(s, l.X, l.Z, hd)) checkSpot = (l.X, l.Z, hd);  // its place on his screen first, the view turned to it (Q / E) -
                                                                             // M4 11-09: gave up a boat off the turned view, then stood 31750 passes
                    if (Dist(mx, mz, l.X, l.Z) > Sight / 2 || checkSpot is not null) return Walk(play, l.X, l.Z, 64);
                    var mid = Senses.CarAt(l.Vehicle.Value.Type, l.X, l.Z)?.Mid ?? s.Ground.Sample((ushort)l.X, (ushort)l.Z).Item1;
                    if (HiddenSpot && !Arms.SeesSpot(s, me, l.X, l.Z, mid)) return Walk(play, l.X, l.Z, 64);   // on screen but a building in the
                                                                             // line: closer, not "not there" (M4 in one game 11-09: a helicopter with
                                                                             // 11238 of fuel behind the HQ given up at pass 97 - then swims only, no way)
                    if (Noticing(s, [l.Vehicle.Value.Type], l.X, l.Z)) return Wish.Stand;
                    Senses.GiveUp(l.X, l.Z); trip.Drop("there, and it is not");                    // there, and it is not
                    return Wish.Stand;
                }
                int vx = v.UW(0x4A), vz = v.UW(0x4E);
                var reach = Math.Max(Math.Abs((int)v.W(0x6E)), Math.Abs((int)v.W(0x72))) + 64;
                if (Dist(mx, mz, vx, vz) > reach)
                {
                    var w = Walk(play, vx, vz, reach - 16, v);
                    if (!walk!.NoWay) return w;
                }
                walk = null;
                return new(true, Wish.Toward(mx, mz, vx, vz), false);       // straight into it: TouchVehicle
            }
            case Trip.Kind.Ride:
            {
                Doing += me.Bit(0x08, 10) ? ", in a boat" : ", driving";
                var w = Walk(play, l.X, l.Z, l.Near, shun: KnownDangers(play.State, me));   // round the reaches that fire at it, as a flight goes (M5 11-08:
                                                                             // a boat on along a tracer gun's edge, pushed out and back in, 2000 passes)
                if (!walk!.NoWay) return w;
                Senses.GiveUp(mx, mz); return Leave();                       // no way on in it: that vehicle no use from here (M6 11-08: back into the next)
            }
            case Trip.Kind.Via:                                              // a corner of the way round the guns' reaches: on past it
                Doing += ", flying round a gun's reach";
                descending = null;
                return FlyTo(play, me, l.X, l.Z, Clearance);
            case Trip.Kind.Fly:
                Doing += ", flying";
                var li = trip.Legs!.IndexOf(l);
                var toBoard = li >= 0 && li + 1 < trip.Legs.Count && trip.Legs[li + 1].Kind == Trip.Kind.Board;   // to a vehicle: beside it, not on it (M4 11-08:
                                                                             // down 304 from a helicopter, out on the far side - PlaceBeside's east spot in
                                                                             // its box - and back into his own on the way round, 3x; SwapProbe)
                var off = Dist(l.X, l.Z, trip.Goal.X, trip.Goal.Z) <= 32 || toBoard ? 500 : 0;   // by the place; a drop short of a gun: on it
                var lw = LandNear(play, me, l.X, l.Z, off);
                if (!noSpot || Dist(mx, mz, l.X, l.Z) > off + LandLook) return lw;
                var (gx0, gz0) = toBoard ? (trip.Legs[li + 1].X, trip.Legs[li + 1].Z) : (l.X, l.Z);   // there, and nowhere dry with room to come down:
                Senses.GiveUp(gx0, gz0); trip.Drop("no room to land");      // that vehicle (or spot) given up, the trip planned again (M4 11-08:
                return lw;                                                   // round a rocket boat 2400 passes, no spot, till low on fuel)
            default:
                Doing += ", getting out";
                return Leave();
        }
    }
    /// <summary>In a boat, a place to go. Armed (the rocket boat and ship's 80mm, the gunboat's bullets): to the sea nearest
    /// the place's target, near enough for half its gun's reach, turned on the spot to it and firing when the shot would meet
    /// it (Arms.WouldHit) or it is close. Unarmed, out of rounds, or no end in sight: ashore by the place, on foot.</summary>
    Wish BoatRide(Play play, Entity me, List<int> open, int px, int pz)
    {
        Doing = "in a boat";
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        var ashore = () => Travel(play, me, px, pz, Sight / 2, "to a target") ?? Leave();
        if (!Armed(me) || firing > Patience) return ashore();
        if (target is null && Dist(mx, mz, px, pz) <= Sight + 1500)
        {
            target = Nearest(s, open, px, pz);
            if (target is null && Noticing(s, open, px, pz)) return new(false, me.B[0x7E], false, Hold: true);   // taking it in
            firing = 0;
        }
        if (target is null) return ashore();                                 // nothing in sight from the sea: the trip, ashore to look
        int ax = target.UW(0x4A), az = target.UW(0x4E);
        var reach = Math.Min(Arms.Range(s, me.W(0x26) - 0x52) / 2, 1500);
        var d = Math.Sqrt(Math.Pow(Senses.Diff(ax, mx), 2) + Math.Pow(Senses.Diff(az, mz), 2));
        if (d > reach)
        {
            if (Navigate.Shore(s, ax, az, sea: true) is not var (sx, sz)) return ashore();
            var w = Walk(play, sx, sz, Trip.ShoreNear);
            if (walk!.NoWay || walk.Arrived) return ashore();               // the sea does not come near enough
            return w;
        }
        walk = null;
        var h = Wish.Toward(mx, mz, ax, az);
        var half = Math.Max(Math.Abs((int)target.W(0x6E)), Math.Abs((int)target.W(0x72)));
        var tol = Math.Max(1, (int)(Math.Atan2(half, d) * 128 / Math.PI) / 2);
        var on = Math.Abs((int)(sbyte)(h - me.B[0x7E])) <= tol && me.W(0x3E) == 0 && (Arms.WouldHit(s, me, target) || d < 600);
        firing++;                                                            // every pass in range, as FlyRide (M4 11-09: lined up 1015 off, no shot
                                                                             // would hit, never fired - 13500 passes; now ashore after Patience)
        return new(false, h, on, Hold: true);
    }

    /// <summary>Armed but turning only on the move (+$08 bit 14: the car's and armoured car's bullets).</summary>
    static bool ArmedCar(Entity v) => !Body.TurnsStill(v) && !v.Bit(0x08, 11) && Armed(v);

    bool runOut;
    (int X, int Z) runTo;

    /// <summary>A car's gun points where the car does, and a car turns only on the move: runs. In at the target at speed,
    /// firing whenever the shot (its speed plus the car's, $24C) would meet it; past it (inside its half width and 250), out
    /// to a point 900 beyond on the far side, then round and in again. No end in Patience x 2 passes, or the rounds gone: out
    /// of the car to fight on foot.</summary>
    Wish CarRun(Play play, Entity me, Entity t)
    {
        Doing = "a run in a car";
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E), tx = t.UW(0x4A), tz = t.UW(0x4E);
        if (++firing > 2 * Patience) return Leave();
        var half = Math.Max(Math.Abs((int)t.W(0x6E)), Math.Abs((int)t.W(0x72)));
        var d = Dist(mx, mz, tx, tz);
        if (!runOut && d < half + 250)
        {
            var a = me.B[0x7E] * Math.PI / 128;                               // on past it, the way he is going
            runTo = ((ushort)(tx + (int)(900 * Math.Sin(a))), (ushort)(tz + (int)(900 * Math.Cos(a))));
            runOut = true;
        }
        if (runOut)
        {
            if (Dist(mx, mz, runTo.X, runTo.Z) < 200 || walk is { NoWay: true }) { runOut = false; walk = null; }
            else return Walk(play, runTo.X, runTo.Z, 160);
        }
        walk = null;
        var fire = Arms.WouldHit(s, me, t);
        return new(true, Wish.Toward(mx, mz, tx, tz), fire);
    }

    // ---- STAYING ALIVE (step 5) ----
    /// <summary>An armed foe this near is shot at; one too strong for what he carries, backed away from.</summary>
    public const int FoeNear = 450, FoeGone = 700;
    int lastHealth = int.MinValue, flinch, backOff, hesitate, pickPasses;
    byte away;
    Entity? foe, pick;
    readonly List<(int X, int Z)> notPicked = [];
    /// <summary>Times it was hit (on foot), foes it fought, things it picked up - for the films' and tests' eyes.</summary>
    public int Hits { get; private set; }
    public int Foes { get; private set; }
    public int Picks { get; private set; }

    /// <summary>An armed thing against him: alive, a weapon (+$26), not a shot, not an empty vehicle (+$06 bit 28), not a comrade
    /// ($20) - enemy soldiers, the ground guns, driven vehicles.</summary>
    public static bool Threat(Entity e) =>
        e.Alive && e.W(0x26) > 0 && !Ground.IsShot(e) && !e.Flag(28) && e.W(4) != 0x20;

    (Entity E, int D)? NearestThreat(WorldState s, int mx, int mz, int within)
    {
        (Entity, int)? best = null;
        var status = Trip.StatusOf(s.Entities.All[0].Flags);
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            if (!Threat(e) || CantHurt(s, e)) continue;                      // one he found he cannot hurt: not a fight
            var mask = Arms.Kind(s, e.W(4), 0x1C) & 0xFF;              // it fires only at what its mask meets (KnownDangers): a SAM
            if (mask != 0 && (mask & status) == 0) continue;                 // soldier ($06) never at him on foot (M1 11-08: 95 rounds at one, then stood)
            var d = Dist(mx, mz, e.UW(0x4A), e.UW(0x4E));
            if (d <= within && (best is null || d < best.Value.Item2) && Senses.SeesNow(e)) best = (e, d);   // seen and taken in
        }
        return best;
    }

    /// <summary>On foot, before the mission: a hit (+$2A down) makes him flinch away from the nearest foe (or back the way he
    /// faced) a moment; an armed foe within FoeNear is fought (after a hesitation) with the weapon Arms.Choose gives - unless
    /// what he carries cannot end it: then he backs off out of FoeGone; a useful thing lying within 350 (rounds for a flown
    /// weapon he has room for, a medical kit when his energy is low) is walked over (Items: touching takes it). Null: nothing
    /// pressing - on with the mission.</summary>
    Wish? Survive(Play play, Entity me)
    {
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        var health = (int)me.W(0x2A);
        var threat = NearestThreat(s, mx, mz, FoeGone);
        if (lastHealth != int.MinValue && health < lastHealth)
        {
            Hits++;
            flinch = 12 + rnd.Next(20);
            away = threat is { } th ? Wish.Toward(th.E.UW(0x4A), th.E.UW(0x4E), mx, mz) : (byte)(me.B[0x7E] + 128 + rnd.Next(64) - 32);
            walk = null;
        }
        lastHealth = health;
        if (me.Bit(0x08, 10)) { foe = null; cover = null; backOff = flinch = 0; return null; }   // in the water: no fights - ashore first (M1 11-08: 5600 passes
                                                                             // "shooting" a soldier from the sea, not one round fired, drowned)
        if (flinch > 0) { flinch--; Doing = "flinching"; return new(true, away, false); }
        if (cover is var (cx, cz))
        {
            if (health < coverHealth) { cover = null; backOff = 60 + rnd.Next(60); }          // found there: away instead
            else if (Dist(mx, mz, cx, cz) > 40)
            {
                var w = Walk(play, cx, cz, 24);
                if (walk!.NoWay) { cover = null; backOff = 60 + rnd.Next(60); } else return w;
            }
            else if (--coverWait <= 0 || threat is null) { cover = null; walk = null; }
            else { coverHealth = health; Doing = "in cover"; return Wish.Stand; }                                    // behind it, waiting
        }
        if (backOff > 0)
        {
            backOff--; Doing = "backing off";
            if (threat is { } b) away = Wish.Toward(b.E.UW(0x4A), b.E.UW(0x4E), mx, mz);
            return new(true, away, false);
        }
        if (foe is not null && (!Threat(foe) || Dist(mx, mz, foe.UW(0x4A), foe.UW(0x4E)) > FoeGone)) { foe = null; firing = 0; }
        if (foe is { } spent && Arms.Choose(s, Math.Max(0, (int)spent.W(0x2A))) < 0)   // nothing left that ends it: let go, away as from one too strong
        {
            foe = null; firing = 0;
            away = Wish.Toward(spent.UW(0x4A), spent.UW(0x4E), mx, mz);
            if (Cover(s, spent, mx, mz) is { } c) { cover = c; coverWait = 150 + rnd.Next(150); coverHealth = health; walk = null; return Wish.Stand; }
            backOff = 60 + rnd.Next(60);
            return new(true, away, false);
        }
        if (foe is null && threat is { } t && t.D <= FoeNear)
        {
            var arm = Arms.Choose(s, Math.Max(0, (int)t.E.W(0x2A)));
            if (arm < 0 || Arms.Hits(s, arm, Math.Max(0, (int)t.E.W(0x2A))) > Arms.Rounds(s, arm))
            {
                away = Wish.Toward(t.E.UW(0x4A), t.E.UW(0x4E), mx, mz);
                if (Cover(s, t.E, mx, mz) is { } c) { cover = c; coverWait = 150 + rnd.Next(150); coverHealth = health; walk = null; return Wish.Stand; }
                backOff = 60 + rnd.Next(60);                                 // too strong for him, nowhere to hide: away
                return new(true, away, false);
            }
            foe = t.E; Foes++; firing = 0; NewAim();
            hesitate = 6 + rnd.Next(24);
        }
        if (foe is not null)
        {
            Doing = "fighting a foe";
            if (hesitate > 0) { hesitate--; return new(false, Wish.Toward(mx, mz, foe.UW(0x4A), foe.UW(0x4E)), false); }
            var sw = Shoot(play, me, foe);
            if (gaveUp != foe) return sw;
            foe = null; gaveUp = null; firing = 0;                           // no good: on with the mission (NearestThreat leaves it out now)
        }
        return Pickup(play, me);
    }

    /// <summary>A useful thing on the ground within 350. What a thing gives is on it, not in its type (TouchItem $A240): it is
    /// touchable with +$06 bit 16, +$92 the item (0-29) and +$93 how many; +$92 negative is a special touch by +$93 ($A856:
    /// 0 a grave's kit, 1 a clue, 2 a soldier to talk to). Useful: rounds for a flown weapon below its most carried (+2), a
    /// medical kit (item 14) with his energy at a third or less, a grave's kit when a flown weapon is under half. Walked
    /// straight over; one not reached in 300 passes is left.</summary>
    Wish? Pickup(Play play, Entity me)
    {
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        int Iw(int item, int off) => (short)(s.Items[0x28 + item * 32 + off] << 8 | s.Items[0x28 + item * 32 + off + 1]);
        bool Useful(Entity e)
        {
            if (!e.Alive || !e.Flag(16)) return false;
            var item = (sbyte)e.B[0x92];
            if (item < 0) return e.B[0x93] == 0 && Arms.Flown.Any(i => Iw(i, 0) < Iw(i, 2) / 2);   // a grave's kit
            if (item == 14) return s.Energy <= 0x20 / 3;
            return Arms.Flown.Contains(item) && Iw(item, 0) < Iw(item, 2);
        }
        if (pick is not null && !Useful(pick)) { if (!pick.Alive) Picks++; pick = null; }
        if (pick is null)
        {
            var bd = 351;
            for (var i = 1; i < s.Entities.Count; i++)
            {
                var e = s.Entities.All[i];
                if (!Useful(e) || notPicked.Any(p => Dist(p.X, p.Z, e.UW(0x4A), e.UW(0x4E)) < 64)) continue;
                var d = Dist(mx, mz, e.UW(0x4A), e.UW(0x4E));
                if (d < bd) (pick, bd) = (e, d);
            }
            if (pick is null) return null;
            pickPasses = 0;
        }
        if (++pickPasses > 300) { notPicked.Add((pick.UW(0x4A), pick.UW(0x4E))); pick = null; return null; }
        Doing = "picking up";
        return new(true, Wish.Toward(mx, mz, pick.UW(0x4A), pick.UW(0x4E)), false);
    }

    // ---- COVER ----
    (int X, int Z)? cover;
    int coverWait, coverHealth;

    /// <summary>A building to hide behind from `foe`: the nearest live one ($95-$AD) within 800 of him, the spot just past its
    /// far side from the foe (its half width and 48 out), on land.</summary>
    static (int X, int Z)? Cover(WorldState s, Entity foe, int mx, int mz)
    {
        Entity? best = null; var bd = 801;
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            var t = e.W(4);
            if (!e.Alive || t < 0x95 || t > 0xAD) continue;
            var d = Dist(mx, mz, e.UW(0x4A), e.UW(0x4E));
            if (d < bd) (best, bd) = (e, d);
        }
        if (best is null) return null;
        var a = Wish.Toward(foe.UW(0x4A), foe.UW(0x4E), best.UW(0x4A), best.UW(0x4E)) * Math.PI / 128;
        var r = Math.Max(Math.Abs((int)best.W(0x6E)), Math.Abs((int)best.W(0x72))) + 48;
        int x = (ushort)(best.UW(0x4A) + (int)(r * Math.Sin(a))), z = (ushort)(best.UW(0x4E) + (int)(r * Math.Cos(a)));
        return Navigate.Land(s, x, z) ? (x, z) : null;
    }

    // ---- STAYING ALIVE IN A VEHICLE ----
    int rideHealth0, rideLast, flee, wrongBoard;
    /// <summary>When and where he last got in; a ride shorter than NowhereRide that ends within 256 of its start went nowhere.</summary>
    (int P, int X, int Z) rideFrom = (int.MinValue / 2, 0, 0);
    const int NowhereRide = 600;
    byte fleeWay;
    Entity? vfoe, keptTarget;

    /// <summary>In a vehicle: worn to a quarter of its energy at boarding, out (a helicopter down first); hit by an armed foe -
    /// an armed vehicle turns on it (the foe its target till it ends or is 2000 off, then back to the mission's), an unarmed
    /// one runs from it a while. Null: on as before.</summary>
    Wish? RideSurvive(Play play, Entity me)
    {
        var s = play.State;
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        var health = (int)me.W(0x2A);
        if (health < rideHealth0 / 4) return Body.Flies(me) ? LandNear(play, me, mx, mz) : Leave();
        if (health < rideLast)
        {
            Hits++;
            var by = NearestThreat(s, mx, mz, 1500);
            if (by is { } b && Armed(me) && vfoe is null && b.E != target)
            {
                vfoe = b.E; keptTarget = target; target = vfoe; firing = 0; Foes++;
            }
            else if (by is { } r && !Armed(me))
            {
                flee = 80 + rnd.Next(60);
                fleeWay = Wish.Toward(r.E.UW(0x4A), r.E.UW(0x4E), mx, mz);
                walk = null;
            }
        }
        rideLast = health;
        if (vfoe is not null && (!Threat(vfoe) || Dist(mx, mz, vfoe.UW(0x4A), vfoe.UW(0x4E)) > 2000))
        {
            target = keptTarget is { Alive: true } ? keptTarget : null; vfoe = null; firing = 0;
        }
        if (flee > 0)
        {
            flee--;
            if (Body.Flies(me))
            {
                var a = fleeWay * Math.PI / 128;
                return FlyTo(play, me, (ushort)(mx + (int)(1500 * Math.Sin(a))), (ushort)(mz + (int)(1500 * Math.Cos(a))), Clearance, lean: false);
            }
            return new(true, fleeWay, false);
        }
        return null;
    }

    // ---- THE DEATH LOG ----
    /// <summary>What it was about when a death came (set as it decides).</summary>
    public string Doing { get; private set; } = "starting";

    /// <summary>A death, or one plot armour stopped (Saved): when, how, what hurt him and who fired it, what the bot was doing,
    /// what he was in, where, in which mission. Each is read for a new rule (docs/bot-plan.md, the death rules).</summary>
    public sealed record Death(int Pass, bool Saved, string Cause, string By, string Shooter, string Doing, string In, int X, int Z, int Mission)
    {
        public override string ToString() =>
            $"pass {Pass}{(Saved ? " (saved by plot armour)" : "")}: {Cause}, by {By}{(Shooter != "" ? $" fired by {Shooter}" : "")}, while {Doing}, in {In}, at {X},{Z}, mission ${Mission:X2}";
    }
    public List<Death> Deaths { get; } = [];
    int savesSeen;
    bool dead;

    static string Name(WorldState s, int type)
    {
        if (type < 0) return "";
        var b = Text.TableText(s.Data.Main, 1, type);
        return new string(b.Select(c => (char)c).Where(c => c != '*' && c != ']' && c != 0).ToArray()).Trim();
    }

    void Watch(WorldState s, Entity me)
    {
        var inside = me.Bit(0x08, 15) ? "foot" : Name(s, me.W(4));
        Death Note(bool saved, string cause) =>
            new(passes, saved, cause, Name(s, s.LastHurtBy), Name(s, s.LastHurtShooter), Doing, inside, me.UW(0x4A), me.UW(0x4E), s.Mission);
        var noted = false;
        for (; savesSeen < s.ArmourSaves.Count; savesSeen++) { Deaths.Add(Note(true, s.ArmourSaves[savesSeen])); noted = true; }
        var gone = (!me.Alive || ExtraKinds.IsEnd(me.W(4))) && s.Energy <= 0;   // the record became an end (an explosion, a grave) with no
                                                                             // energy left - a hit on foot makes it $60 for a pass, then $1E again (GrenadeProbe)
        if (gone && !dead)
        {
            Deaths.Add(Note(false, (s.Status216 & 4) != 0 ? "drowned" : s.LastHurtBy >= 0 ? "hit" : "a crash or a fall"));
            noted = true;
        }
        dead = gone;
        if (noted) s.LastHurtBy = s.LastHurtShooter = -1;                     // the next one's own cause
    }
}
