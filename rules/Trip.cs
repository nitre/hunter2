namespace Hunter.Rules;

/// <summary>(the BOT's trips, docs/bot-brain.md 2 and its STEP 2 PLAN): the quickest way to a
/// place over what he knows. The map's squares are cut into AREAS (Navigate.Areas: each island's land, each sea); a trip runs
/// over LINKS whose times are in blanks, length / that way's top speed (+$36; the kind's +$12 for a thing not in sight): walking
/// and swimming (the same +$36 - WalkSwimSwitch $2BAC swaps only the models), boarding a vehicle he knows of, driving, a boat
/// to the coast by the place, flying, getting out. A swim is a link only while its cost in energy (EachMinute $B05C: 1 after 3
/// minutes in the water, then 1 every 4) leaves him half his energy (as a last resort, all but 2 - Spend). Dijkstra over (where, on foot or in which vehicle); the
/// legs come back in order. Reads the world; changes nothing.</summary>
public sealed class Trip
{
    public enum Kind { Walk, Swim, Board, Ride, Fly, Out, Via }
    public enum Mode { Foot, Land, Water, Both, Air }

    /// <summary>A vehicle he knows of: the thing (null: a drone map's record, a boat he left), its type, where he believes it is,
    /// and its gauge in passes when he knows it (the one he is in, or one he left - a player sees it only once in).</summary>
    public readonly record struct Ride(Entity? E, int Type, int X, int Z, int? Lasts = null);

    /// <summary>A vehicle's gauge in passes (+$34, MoveByModeBurnFuel $2972 - bit 4 burns it twice as fast).</summary>
    public static int Lasts(Entity e) => Math.Max(0, (int)e.W(0x34)) / (e.Flag(4) ? 2 : 1);

    /// <summary>A trip out must leave a way home: a known gauge must also bring him back from the drop unless another ride is
    /// known this near it (four squares).</summary>
    const int OtherRideNear = 4 * Navigate.Square;

    /// <summary>A gun he knows of (seen, or a drone map's ground gun): where, its fire box (it fires only while |dx| <= BoxX
    /// and |dz| <= BoxZ, height not counted - its kind's +$1E / +$22, the in-range test $28EC before the only fire switch
    /// $292A), and whom it fires at (its kind's mask, +$1C low byte - $2740 ANDs it with the player's status: 1 boat,
    /// 2 helicopter, 4 aircraft, 8 tank, $10 car, $20 on foot, $40 swimming).</summary>
    public readonly record struct Danger(int X, int Z, int BoxX, int BoxZ, int Mask);

    /// <summary>The margin round a fire box: how far he goes over the ground in this many blanks (SpeedOf; 50 blanks a
    /// second) - half a second to turn off.</summary>
    public const int MarginBlanks = 25;

    /// <summary>The status a vehicle of these flags shows to a gun's mask: flag bits 10-15 as bits 0-5 ($2E72 for the player,
    /// LivingWorld.Status).</summary>
    public static int StatusOf(int flags) => flags >> 10 & 0x3F;
    /// <summary>A helicopter's height change a tick at most, up and down (Body.Fly's climb).</summary>
    const double ClimbStep = 3, SinkStep = 6;
    /// <summary>The status on foot (+$06 bit 15).</summary>
    public const int FootStatus = 1 << 5;

    /// <summary>How many vehicles he knew of when the last plan was made (one more known since: plan again).</summary>
    public int RidesKnown { get; private set; }

    /// <summary>The dangers the last plan was made with.</summary>
    public IReadOnlyList<Danger> Dangers { get; private set; } = [];

    /// <summary>Is (x, z) inside the fire box, with the margin for this speed, of a gun that fires at this status?</summary>
    bool Covered(int x, int z, int status, int speed) => Covered(Dangers, x, z, status, speed);
    public static bool Covered(IReadOnlyList<Danger> dangers, int x, int z, int status, int speed)
    {
        var m = speed * MarginBlanks;
        return dangers.Any(d => (d.Mask & status) != 0 && Math.Abs((ushort)x - (ushort)d.X) <= d.BoxX + m && Math.Abs((ushort)z - (ushort)d.Z) <= d.BoxZ + m);
    }

    /// <summary>One leg: what, to where, done within Near, by which vehicle, its planned blanks.</summary>
    public sealed record Leg(Kind Kind, int X, int Z, int Near, Ride? Vehicle, double Time);

    /// <summary>A swim's first energy point goes after 3 game minutes in the water, then one each 4 (EachMinute $B05C; a minute
    /// is 60 blanks, $3586).</summary>
    const int SwimFirst = 180, SwimEach = 240;

    public List<Leg>? Legs { get; private set; }
    int at, footSpeed = 2;
    (int X, int Z) goal;
    readonly Dictionary<(int Mode, int From, int To), double> lengths = [];

    public (int X, int Z) Goal => goal;
    public Leg? Current => Legs is { } l && at < l.Count ? l[at] : null;
    public bool For(int gx, int gz) => Legs is not null && Senses.Dist(goal.X, goal.Z, gx, gz) <= 32;
    public void Next() => at++;
    public void Drop(string why = "") { if (Legs is not null) { DropWhy = why; Drops++; } Legs = null; at = 0; }
    /// <summary>Why the last held trip was dropped, and how many were (for the run log).</summary>
    public string DropWhy { get; private set; } = "";
    public int Drops { get; private set; }

    /// <summary>The flags (+$06) a vehicle has: the thing's own, else its kind's start flags (+$00).</summary>
    public static int FlagsOf(WorldState s, Ride r) =>
        r.E is { Alive: true } e && e.W(4) == r.Type ? e.Flags : Arms.Kind(s, r.Type, 0) << 16 | Arms.Kind(s, r.Type, 2) & 0xFFFF;

    /// <summary>Flying (+$06 bit 11), land and sea (bit 1, the amphibian's switch $2BAC), on the sea (bit 10), else on land.</summary>
    public static Mode ModeOf(int flags) =>
        (flags >> 11 & 1) != 0 ? Mode.Air : (flags >> 1 & 1) != 0 ? Mode.Both : (flags >> 10 & 1) != 0 ? Mode.Water : Mode.Land;

    /// <summary>Ground covered each blank: the top speed, or a flier's cruise step (Hunt.CruiseStep - its gravity +$3A, or
    /// the kind's weight as ConstructEntity sets it, $323A: w >> 1 = 0 -> $10 for 0, else 0; otherwise w).</summary>
    public static int SpeedOf(WorldState s, Ride r)
    {
        var live = r.E is { Alive: true } e && e.W(4) == r.Type;
        int top = live ? r.E!.W(0x36) : Arms.Kind(s, r.Type, 0x12);
        if (ModeOf(FlagsOf(s, r)) != Mode.Air) return top;
        var w = Arms.Kind(s, r.Type, 0x16);
        var gravity = live ? r.E!.W(0x3A) : w >> 1 == 0 ? (w == 0 ? 0x10 : 0) : w;
        return Hunt.CruiseStep(gravity, top);
    }

    static int Sq(int x, int z) => ((ushort)z >> 9 & 127) * Navigate.Size + ((ushort)x >> 9 & 127);
    static (int X, int Z) Middle(int sq) => (sq % Navigate.Size * Navigate.Square + Navigate.Square / 2, sq / Navigate.Size * Navigate.Square + Navigate.Square / 2);
    static double Len(int ax, int az, int bx, int bz) { double dx = (ushort)bx - (ushort)ax, dz = (ushort)bz - (ushort)az; return Math.Sqrt(dx * dx + dz * dz); }

    /// <summary>The area of the land at (x, z) - or, from the sea's edge (a boat by the shore, a man wading), of the land beside it.</summary>
    static int AreaOf(WorldState s, int[] areas, int x, int z, bool sea)
    {
        var q = Sq(x, z);
        if (areas[q] >= 0 && (s.Terrain.Type(q % Navigate.Size, q / Navigate.Size) == 0) == sea) return areas[q];
        for (var dr = -1; dr <= 1; dr++)
            for (var dc = -1; dc <= 1; dc++)
            {
                int c = (q % Navigate.Size + dc + Navigate.Size) % Navigate.Size, r = (q / Navigate.Size + dr + Navigate.Size) % Navigate.Size;
                var n = r * Navigate.Size + c;
                if (areas[n] >= 0 && (s.Terrain.Type(c, r) == 0) == sea) return areas[n];
            }
        return -1;
    }

    /// <summary>(x, z) on the land of the area (tx, tz) is on (or wades from).</summary>
    public static bool SameLand(WorldState s, int x, int z, int tx, int tz)
    {
        var areas = Navigate.Areas(s);
        var q = Sq(x, z);
        return s.Terrain.Type(q % Navigate.Size, q / Navigate.Size) != 0 && areas[q] >= 0 && areas[q] == AreaOf(s, areas, tx, tz, false);
    }

    /// <summary>The way's length over the squares (Navigate.Way), kept per pair of squares; below 0: none.</summary>
    double WayLen(WorldState s, Mode m, int x, int z, int tx, int tz)
    {
        var key = ((int)m, Sq(x, z), Sq(tx, tz));
        if (lengths.TryGetValue(key, out var l)) return l;
        var way = Navigate.Way(s, x, z, tx, tz, water: m == Mode.Water, both: m == Mode.Both);
        if (way is null) l = -1;
        else { l = 0; var (px, pz) = (x, z); foreach (var p in way) { l += Len(px, pz, p.X, p.Z); (px, pz) = p; } }
        return lengths[key] = l;
    }

    /// <summary>The nearest square to (x, z) of `area` whose kind is `sea` with a square of `other` (the other kind) beside it,
    /// as its middle: a boat's coast by a land place (other = the place's land), or the shore a swim starts from.</summary>
    static (int X, int Z)? Edge(WorldState s, int[] areas, int x, int z, bool sea, int area, int other, Func<int, int, bool>? ok = null)
    {
        int c0 = Sq(x, z) % Navigate.Size, r0 = Sq(x, z) / Navigate.Size;
        for (var ring = 0; ring <= Navigate.Size / 2; ring++)
        {
            (int X, int Z)? best = null; var bd = long.MaxValue;
            for (var dr = -ring; dr <= ring; dr++)
                for (var dc = -ring; dc <= ring; dc++)
                {
                    if (Math.Max(Math.Abs(dc), Math.Abs(dr)) != ring) continue;
                    int c = (c0 + dc + Navigate.Size) % Navigate.Size, r = (r0 + dr + Navigate.Size) % Navigate.Size;
                    if (areas[r * Navigate.Size + c] != area || (s.Terrain.Type(c, r) == 0) != sea) continue;
                    var edge = false;
                    for (var k = 0; k < 4 && !edge; k++)
                    {
                        int nc = (c + (k == 0 ? 1 : k == 1 ? -1 : 0) + Navigate.Size) % Navigate.Size, nr = (r + (k == 2 ? 1 : k == 3 ? -1 : 0) + Navigate.Size) % Navigate.Size;
                        var n = nr * Navigate.Size + nc;
                        edge = (s.Terrain.Type(nc, nr) == 0) != sea && (other < 0 ? areas[n] >= 0 : areas[n] == other);
                    }
                    var d = (long)dc * dc + (long)dr * dr;
                    if (edge && d < bd && Middle(r * Navigate.Size + c) is var mid && (ok is null || ok(mid.X, mid.Z))) (best, bd) = (mid, d);
                }
            if (best is not null) return best;
        }
        return null;
    }

    /// <summary>The energy a swim of this many blanks costs: the water timer starts afresh each time he goes in ($206, Swimming
    /// $B05C), the first point after 3 minutes, then one each 4 - a swim under 3 minutes costs none.</summary>
    /// <summary>A swim counts this many times its blanks when trips are weighed: in the
    /// water he cannot fight, is a target, and a long one costs energy. Chosen, not the game's; the legs keep real times.</summary>
    const double SwimWeight = 4;

    static int SwimCost(double ticks) => ticks < SwimFirst ? 0 : 1 + (int)((ticks - SwimFirst) / SwimEach);

    /// <summary>The energy all the swims of a trip may cost: half of his; as a LAST RESORT (no other way) all but 2, so he lands alive.</summary>
    int Spend(int energy) => LastResort ? energy - 2 : (int)(energy * SpendShare);
    /// <summary>The share of his energy a trip's swims may cost (0.5: half - the floor, as energy / 2 was); a setting for the lab.</summary>
    static double SpendShare = 0.5;
    /// <summary>Where he must get back to after this trip (the HQ he believes in; null on the way home), and the rides known.</summary>
    (int X, int Z)? home;
    IReadOnlyList<Ride> rides = [];
    /// <summary>The last plan was made as a last resort.</summary>
    public bool LastResort { get; private set; }

    /// <summary>A SWIM LINK between two islands (land areas): the nearest pair of their shore squares (land with sea beside it),
    /// as middles, and the water between - the length less a square, shore to shore.</summary>
    public readonly record struct SwimLink(int To, int FromX, int FromZ, int ToX, int ToZ, double Wet);

    /// <summary>The longest swim worth a link: what the most energy he can have (the start's $20) pays for as a last resort
    /// (all but 2), at foot speed 2; each trip's own budget (Spend) cuts it down.</summary>
    const double LongestSwim = (SwimFirst + 29 * SwimEach) * 2;

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Terrain, List<SwimLink>[]> swimLinks = new();

    /// <summary>THE ISLANDS' SWIM LINKS (bot-reading.md: Quake III's areas joined by "reachabilities", each a way to travel,
    /// worked out once per map): for each land area, a link to every other within LongestSwim, the straight line over no
    /// blocked square. Indexed by area.</summary>
    static List<SwimLink>[] SwimLinks(WorldState s, int[] areas) => swimLinks.GetValue(s.Terrain, _ =>
    {
        var count = areas.Max() + 1;
        var shore = new List<int>[count];
        for (var a = 0; a < count; a++) shore[a] = [];
        bool Sea(int c, int r) => s.Terrain.Type((c + Navigate.Size) % Navigate.Size, (r + Navigate.Size) % Navigate.Size) == 0;
        for (var sq = 0; sq < areas.Length; sq++)
        {
            int c = sq % Navigate.Size, r = sq / Navigate.Size;
            if (areas[sq] < 0 || Sea(c, r)) continue;
            if (Sea(c + 1, r) || Sea(c - 1, r) || Sea(c, r + 1) || Sea(c, r - 1)) shore[areas[sq]].Add(sq);
        }
        var links = new List<SwimLink>[count];
        for (var a = 0; a < count; a++) links[a] = [];
        for (var a = 0; a < count; a++)
            for (var b = a + 1; b < count; b++)
            {
                if (shore[a].Count == 0 || shore[b].Count == 0) continue;
                (int A, int B, long D) best = (-1, -1, long.MaxValue);
                foreach (var i in shore[a])
                    foreach (var j in shore[b])
                    {
                        long dc = i % Navigate.Size - j % Navigate.Size, dr = i / Navigate.Size - j / Navigate.Size, d = dc * dc + dr * dr;
                        if (d < best.D) best = (i, j, d);
                    }
                if (Math.Sqrt(best.D) * Navigate.Square - Navigate.Square > LongestSwim) continue;
                var (ax, az) = Middle(best.A); var (bx, bz) = Middle(best.B);
                if (!Clear(s, ax, az, bx, bz)) continue;
                var wet = Water(s, ax, az, bx, bz);                          // the game's water on the line (M9 11-09: the gap less a square counted
                if (wet > LongestSwim) continue;                             // the shore squares as land - planned 3584, swum 4250, 9 where 7 was all)
                links[a].Add(new(b, ax, az, bx, bz, wet));
                links[b].Add(new(a, bx, bz, ax, az, wet));
            }
        return links;
    });

    /// <summary>The water on the straight line by the game's point test (Ground.Sample, Settle $2B0E), every 16.</summary>
    static double Water(WorldState s, int x, int z, int tx, int tz)
    {
        double dx = (ushort)tx - (ushort)x, dz = (ushort)tz - (ushort)z, len = Math.Sqrt(dx * dx + dz * dz), wet = 0;
        var n = Math.Max(1, (int)Math.Ceiling(len / 16));
        for (var i = 1; i <= n; i++)
            if (!s.Ground.Sample((ushort)(x + (int)(dx * i / n)), (ushort)(z + (int)(dz * i / n))).Land) wet += len / n;
        return wet;
    }

    /// <summary>No blocked square (the map's edge ring) on the straight line.</summary>
    static bool Clear(WorldState s, int x, int z, int tx, int tz)
    {
        double dx = (ushort)tx - (ushort)x, dz = (ushort)tz - (ushort)z;
        var n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / 64));
        for (var i = 0; i <= n; i++)
            if (s.Ground.Sample((ushort)(x + (int)(dx * i / n)), (ushort)(z + (int)(dz * i / n))).Blocked) return false;
        return true;
    }

    /// <summary>His real ground per pass on foot (walk or swim) from a toward b. On foot he faces only the 8 ways of the
    /// control table, turned with the view (Controls.FootHeading, Q / E), and the game's step ($2AE6 -> Vector $2C00) rounds
    /// each part by asr (+ toward 0, - away; StraightMoves: both toward 0) - at speed 2 most of the 8 move 1 a pass one way
    /// or both (M4 11-09 swam at heading $0C: (0, 1) a pass, 4057 in 4057, planned at 2). The pace: the best zig-zag of two
    /// of the 8 that goes the way (d = a x + b y, the fewest passes a unit), at the view he has now.</summary>
    static double Pace(WorldState s, int foot, (int X, int Z) a, (int X, int Z) b)
    {
        double dx = Senses.Diff(b.X, a.X), dz = Senses.Diff(b.Z, a.Z), len = Math.Sqrt(dx * dx + dz * dz);
        if (len == 0 || foot <= 0) return Math.Max(1, foot);
        dx /= len; dz /= len;
        int Asr(int v, int n) => s.StraightMoves && v < 0 ? -M68.Asr(-v, n) : M68.Asr(v, n);
        var steps = new List<(double X, double Z)>();
        foreach (var d in new[] { 1, 2, 4, 5, 6, 8, 9, 10 })
        {
            var h = Controls.FootHeading(s.Data.Main, d, s.ViewAngle1);
            if (h == 0) { steps.Add((0, foot)); continue; }
            var (se, sm) = s.Trig.Sin(h * 4);
            var (ce, cm) = s.Trig.Cos(h * 4);
            steps.Add(((short)Asr(M68.Muls(sm, foot), se), (short)Asr(M68.Muls(cm, foot), ce)));
        }
        var best = double.MaxValue;                                          // passes for one unit along (dx, dz)
        foreach (var p in steps)
            foreach (var q in steps)
            {
                var det = p.X * q.Z - p.Z * q.X;
                if (Math.Abs(det) < 1e-9)                                    // one way alone, along it
                {
                    var along = p.X * dx + p.Z * dz;
                    if (along > 0 && Math.Abs(p.X * dz - p.Z * dx) < 1e-9) best = Math.Min(best, 1 / along);
                    continue;
                }
                double u = (dx * q.Z - dz * q.X) / det, w = (p.X * dz - p.Z * dx) / det;
                if (u >= 0 && w >= 0) best = Math.Min(best, u + w);
            }
        return best == double.MaxValue ? foot / 2.0 : 1 / best;
    }

    /// <summary>ISLAND TO ISLAND on foot: from p on island ap to q on island aq over the swim links - the least
    /// energy first, then the quickest (walks over each island by the way, swims shore to shore), all the swims together
    /// costing no more than half his energy. Null: none.</summary>
    (double Time, List<Leg> Legs)? Hops(WorldState s, int[] areas, int foot, int energy, (int X, int Z) p, int ap, (int X, int Z) q, int aq)
    {
        var links = SwimLinks(s, areas);
        var best = new Dictionary<int, (int Cost, double Time, (int X, int Z) At, int From, SwimLink? By)>
            { [ap] = (0, 0, p, -1, null) };
        var open = new PriorityQueue<int, (int, double)>();
        open.Enqueue(ap, (0, 0));
        var done = new HashSet<int>();
        while (open.TryDequeue(out var a, out _))
        {
            if (!done.Add(a)) continue;
            if (a == aq) break;
            var here = best[a];
            foreach (var l in links[a])
            {
                if (done.Contains(l.To)) continue;
                var walk = WayLen(s, Mode.Foot, here.At.X, here.At.Z, l.FromX, l.FromZ);
                if (walk < 0) continue;
                double sp = Pace(s, foot, (l.FromX, l.FromZ), (l.ToX, l.ToZ)), wp = Pace(s, foot, here.At, (l.FromX, l.FromZ));
                var cost = here.Cost + SwimCost(l.Wet / sp);
                if (cost > Spend(energy)) continue;
                var time = here.Time + walk / wp + (Navigate.Square + l.Wet * SwimWeight) / sp;   // a swim weighs more than its time
                if (best.TryGetValue(l.To, out var was) && (was.Cost, was.Time).CompareTo((cost, time)) <= 0) continue;
                best[l.To] = (cost, time, (l.ToX, l.ToZ), a, l);
                open.Enqueue(l.To, (cost, time));
            }
        }
        if (!best.TryGetValue(aq, out var end)) return null;
        var hops = new List<SwimLink>();
        for (var a = aq; best[a].By is { } by; a = best[a].From) hops.Insert(0, by);
        var legs = new List<Leg>();
        var at = p;
        foreach (var h in hops)
        {
            var w = WayLen(s, Mode.Foot, at.X, at.Z, h.FromX, h.FromZ);
            if (Senses.Dist(at.X, at.Z, h.FromX, h.FromZ) > ShoreNear) legs.Add(new(Kind.Walk, h.FromX, h.FromZ, ShoreNear, null, w / Pace(s, foot, at, (h.FromX, h.FromZ))));
            legs.Add(new(Kind.Swim, h.ToX, h.ToZ, ShoreNear, null, (h.Wet + Navigate.Square) / Pace(s, foot, (h.FromX, h.FromZ), (h.ToX, h.ToZ))));
            at = (h.ToX, h.ToZ);
        }
        var last = WayLen(s, Mode.Foot, at.X, at.Z, q.X, q.Z);
        if (last < 0) return null;
        var lt = last / Pace(s, foot, at, q);
        return (end.Time + lt, legs.Append(new Leg(Kind.Walk, q.X, q.Z, 0, null, lt)).ToList());
    }

    /// <summary>On foot from p to q: a walk over one island; from island to island the quicker of a walk to its shore nearest q
    /// and one swim straight there, and the trip over the swim links (Hops); from the water a swim straight there. Every swim
    /// together no more than half his energy. `board`: q is that vehicle, boarded at the end.</summary>
    (double Time, List<Leg> Legs)? Foot(WorldState s, int[] areas, int foot, int energy, (int X, int Z) p, (int X, int Z) q, int near, Ride? board, bool wet = false)
    {
        int ap = wet ? -1 : AreaOf(s, areas, p.X, p.Z, false), aq = AreaOf(s, areas, q.X, q.Z, false);
        Leg End(double t) => board is { } b ? new Leg(Kind.Board, q.X, q.Z, 0, b, t) : new Leg(Kind.Walk, q.X, q.Z, near, null, t);
        if (ap >= 0 && ap == aq && WayLen(s, Mode.Foot, p.X, p.Z, q.X, q.Z) is var l and >= 0)
            return (l / Pace(s, foot, p, q), [End(l / Pace(s, foot, p, q))]);
        var straight = Straight(s, areas, foot, energy, p, q, ap, near, board, End);
        if (ap < 0 || aq < 0 || Hops(s, areas, foot, energy, p, ap, q, aq) is not var (ht, hl)) return straight;
        if (straight is var (st, _) && st <= ht) return straight;
        if (board is not null) hl[^1] = End(hl[^1].Time); else hl[^1] = hl[^1] with { Near = near };
        return (ht, hl);
    }

    /// <summary>A walk to p's island's shore nearest q (none: from the water, at p) and one swim straight to q.</summary>
    (double Time, List<Leg> Legs)? Straight(WorldState s, int[] areas, int foot, int energy, (int X, int Z) p, (int X, int Z) q, int ap, int near, Ride? board, Func<double, Leg> End)
    {
        var start = ap >= 0 ? Edge(s, areas, q.X, q.Z, false, ap, -1) : p;
        if (start is var (ex, ez) && Senses.Dist(p.X, p.Z, q.X, q.Z) <= Senses.Dist(ex, ez, q.X, q.Z)) start = p;   // nearer q than the shore point already: from here
        if (start is not var (sx, sz)) return null;                          // (M5 11-08: 130 off a gunboat, sent 200 back up the beach, 22000 passes)
        var walk = ap >= 0 ? WayLen(s, Mode.Foot, p.X, p.Z, sx, sz) : 0;
        if (walk < 0) return null;
        double dx = (ushort)q.X - (ushort)sx, dz = (ushort)q.Z - (ushort)sz, len = Math.Sqrt(dx * dx + dz * dz), wet = 0;
        var n = (int)Math.Ceiling(len / 64);
        for (var i = 1; i <= n; i++)
        {
            var (_, land, blocked) = s.Ground.Sample((ushort)(sx + (int)(dx * i / n)), (ushort)(sz + (int)(dz * i / n)));
            if (blocked) return null;
            if (!land) wet += len / n;
        }
        double wp = Pace(s, foot, p, (sx, sz)), sp = Pace(s, foot, (sx, sz), q);
        if (SwimCost(wet / sp) > Spend(energy)) return null;
        var legs = new List<Leg>();
        if (Senses.Dist(p.X, p.Z, sx, sz) > ShoreNear) legs.Add(new(Kind.Walk, sx, sz, ShoreNear, null, walk / wp));
        if (board is null || Senses.Dist(sx, sz, q.X, q.Z) > ShoreNear && !(start == p && ap >= 0))   // within a swim's end of the vehicle, or on its shore
                                                                             // and nearer it than the shore point: straight to boarding it (Board walks up to it)
            legs.Add(new(Kind.Swim, q.X, q.Z, board is null ? near : ShoreNear, null, len / sp));
        if (board is not null) legs.Add(End(0));
        return (walk / wp + (len - wet + wet * SwimWeight) / sp, legs);                  // a swim weighs more than its time
    }

    /// <summary>How near a shore square's middle a walk to it ends, or a boat's run in to the coast: a quarter square.</summary>
    public const int ShoreNear = Navigate.Square / 4;
    /// <summary>A drive on to another vehicle stops this far from it: beside it, not on it - two boxes (a helicopter's half is
    /// 156, a tank's 140) cannot meet, and a tank parked against a helicopter stood on the only way in (touching it boards it).</summary>
    const int Beside = Navigate.Square;

    /// <summary>In vehicle r from q toward the goal: by land on one island (the amphibian anywhere it has a way), a boat to the
    /// coast square of its sea by the goal's land, an aircraft straight; then out (an aircraft lands and he gets out, LandNear).
    /// The drop: where he is on foot after.</summary>
    (double Time, List<Leg> Legs, (int X, int Z) Drop)? Drive(WorldState s, int[] areas, Ride r, (int X, int Z) q, (int X, int Z) g, int near)
    {
        var speed = SpeedOf(s, r);
        if (speed <= 0) return null;
        var trip = Run(s, areas, r, q, g, near, speed);
        if (trip is var (_, _, (dx, dz)) && (Len(q.X, q.Z, dx, dz) < ShoreNear || Senses.Dist(q.X, q.Z, g.X, g.Z) <= near) && !(r.E is { } me && me == s.Entities.All[0])) return null;   // gets him nowhere (getting in is one pass, $2F8E: no cost to tip it); "already there" by the ride's own end test (Hunt.Ended, Senses.Dist - the larger of the two sides, not the straight line: a truck 506 east and 124 north of a jeep is there, at 521 straight)
        if (trip is var (t, legs, drop) && r.Lasts is int lasts)          // its gauge known (the one he is in, one he left)
        {
            var flies = ModeOf(FlagsOf(s, r)) == Mode.Air;
            if (lasts <= 0 || t > lasts && flies) return null;             // an aircraft: no flight it cannot finish
            if (home is { } h && !rides.Any(o => o != r && Senses.Dist(o.X, o.Z, drop.X, drop.Z) <= OtherRideNear)
                && Run(s, areas, r, drop, h, ShoreNear, speed) is var (back, _, _) && lasts < t + back) return null;   // ROUND TRIP: the gauge
                                                                           // must bring him back too (M1: a rocket boat ran dry 29000 from home after the dump)
            if (t > lasts)                                                 // as far as it goes, then out on the empty gauge (Hunt) and the rest on foot
                return (lasts + (1 - lasts / t) * Len(q.X, q.Z, drop.X, drop.Z) / Pace(s, footSpeed, q, drop), legs, drop);
        }
        return trip;
    }

    /// <summary>Does a flight from p to q enter a reach that fires at this status? Points every ShoreNear; a reach he is in at
    /// the start (seen late) does not count while he flies out of it - but does once the way goes nearer its gun than the
    /// start (M5 11-08: in a tracer gun's box when he saw it, he flew on in and landed 800 from it).</summary>
    bool FlightCrosses((int X, int Z) p, (int X, int Z) q, int status, int speed) => FlightCrosses(Dangers, p, q, status, speed);
    public static bool FlightCrosses(IReadOnlyList<Danger> dangers, (int X, int Z) p, (int X, int Z) q, int status, int speed)
    {
        double dx = (ushort)q.X - (ushort)p.X, dz = (ushort)q.Z - (ushort)p.Z;
        var n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / ShoreNear));
        var m = speed * MarginBlanks;
        foreach (var d in dangers)
        {
            if ((d.Mask & status) == 0) continue;
            double Near(int x, int z) => Math.Max(Math.Abs((ushort)x - (ushort)d.X) - (d.BoxX + m), Math.Abs((ushort)z - (ushort)d.Z) - (d.BoxZ + m));   // <= 0: inside
            var start = Near(p.X, p.Z);
            var leaving = start <= 0;
            for (var i = 1; i <= n; i++)
            {
                var c = Near((ushort)(p.X + (int)(dx * i / n)), (ushort)(p.Z + (int)(dz * i / n)));
                if (c > 0) leaving = false;
                else if (!leaving || c < start - ShoreNear) return true;
            }
        }
        return false;
    }

    /// <summary>A FLIGHT ROUND THE REACHES (Killzone plans round restricted areas): the points
    /// on the way from p to q, q last - straight when that meets no reach for this status, else the shortest chain over the
    /// corners of the reaches (each box with its margin, a square further out) whose every hop meets none. Null: none.</summary>
    List<(int X, int Z)>? AirWay((int X, int Z) p, (int X, int Z) q, int status, int speed)
    {
        if (!FlightCrosses(p, q, status, speed)) return [q];
        var m = speed * MarginBlanks + Navigate.Square;
        var nodes = new List<(int X, int Z)> { p, q };
        foreach (var d in Dangers)
            if ((d.Mask & status) != 0)
                foreach (var (sx, sz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
                {
                    (int X, int Z) c = ((ushort)(d.X + sx * (d.BoxX + m)), (ushort)(d.Z + sz * (d.BoxZ + m)));
                    if (!Covered(c.X, c.Z, status, speed)) nodes.Add(c);
                }
        var best = new double[nodes.Count]; Array.Fill(best, double.MaxValue);
        var from = new int[nodes.Count]; Array.Fill(from, -1);
        var open = new PriorityQueue<int, double>();
        best[0] = 0; open.Enqueue(0, 0);
        while (open.TryDequeue(out var u, out var bu))
        {
            if (bu > best[u]) continue;
            if (u == 1) break;
            for (var v = 1; v < nodes.Count; v++)
            {
                if (v == u) continue;
                var nb = best[u] + Len(nodes[u].X, nodes[u].Z, nodes[v].X, nodes[v].Z);
                if (nb >= best[v] || FlightCrosses(nodes[u], nodes[v], status, speed)) continue;
                best[v] = nb; from[v] = u; open.Enqueue(v, nb);
            }
        }
        if (best[1] == double.MaxValue) return null;
        var way = new List<(int X, int Z)>();
        for (var v = 1; v != 0; v = from[v]) way.Insert(0, nodes[v]);
        return way;
    }

    /// <summary>How many of the nearest out-of-reach spots get the full check (flight line, then the walk / swim after).</summary>
    const int LandingTries = 40, LandingWalks = 8;

    /// <summary>Where an aircraft comes down when the straight flight meets a gun: every land square (not blocked) whose middle
    /// is out of every reach for this status, nearest the goal first; of those the flight reaches without entering a reach,
    /// the one with the quickest flight + the way on foot after (Foot: walks, swims within his energy). Null: none.</summary>
    (int X, int Z)? Landing(WorldState s, int[] areas, (int X, int Z) q, (int X, int Z) g, int status, int speed, int near)
    {
        var spots = new List<(int X, int Z)>();
        for (var sq = 0; sq < Navigate.Size * Navigate.Size; sq++)
        {
            var (x, z) = Middle(sq);
            if (areas[sq] >= 0 && Navigate.Land(s, x, z) && !Covered(x, z, status, speed)) spots.Add((x, z));
        }
        (double T, (int X, int Z) At)? best = null;
        var walks = 0;
        foreach (var c in spots.OrderBy(c => Len(c.X, c.Z, g.X, g.Z)).Take(LandingTries))
        {
            if (AirWay(q, c, status, speed) is not { } way) continue;
            if (Foot(s, areas, footSpeed, s.Energy, c, g, near, null) is var (ft, _))
            {
                double fly = 0; var at = q;
                foreach (var w in way) { fly += Len(at.X, at.Z, w.X, w.Z); at = w; }
                var t = fly / speed + ft;
                if (best is null || t < best.Value.T) best = (t, c);
            }
            if (++walks == LandingWalks) break;
        }
        return best?.At;
    }

    (double Time, List<Leg> Legs, (int X, int Z) Drop)? Run(WorldState s, int[] areas, Ride r, (int X, int Z) q, (int X, int Z) g, int near, int speed)
    {
        switch (ModeOf(FlagsOf(s, r)))
        {
            case Mode.Air:                                                   // to the goal round every reach of a gun that fires at
            {                                                                // aircraft (AirWay); the goal in one: down on the best land out of them all
                var st = StatusOf(FlagsOf(s, r));
                var way = Covered(g.X, g.Z, st, speed) ? null : AirWay(q, g, st, speed);
                if (way is null && Landing(s, areas, q, g, st, speed, near) is { } c) way = AirWay(q, c, st, speed);
                if (way is null) return null;
                var legs = new List<Leg>();
                double t = 0;
                var from = q;
                var up = r.E is { Alive: true } e && e.B[0x1E] == 0 ? 0 : Hunt.Clearance / ClimbStep;   // the climb from the ground and the way down
                foreach (var p in way)                                       // (Body.Fly: 3 a tick up, 6 down) - left out, a flight looked quicker
                {                                                            // than a short walk from it (M2 11-08: in, out, in ... by a helicopter)
                    var last = p == way[^1];
                    var lt = Len(from.X, from.Z, p.X, p.Z) / speed + (p == way[0] ? up : 0) + (last ? Hunt.Clearance / SinkStep : 0); t += lt; from = p;
                    legs.Add(new(last ? Kind.Fly : Kind.Via, p.X, p.Z, last ? 0 : ShoreNear, r, lt));
                }
                return (t, legs, way[^1]);
            }
            case Mode.Water:
            {
                int sa = AreaOf(s, areas, q.X, q.Z, true), ga = AreaOf(s, areas, g.X, g.Z, false), st = StatusOf(FlagsOf(s, r));
                bool Clear(int x, int z) => !Covered(x, z, st, speed) && !Covered(x, z, FootStatus, footSpeed);   // ashore out of every gun's reach he knows of (M5 / M6 11-08: landed by guns), the boat's and his on foot
                if (sa < 0 || ga < 0 || Edge(s, areas, g.X, g.Z, true, sa, ga, Clear) is not var (cx, cz)) return null;
                var l = WayLen(s, Mode.Water, q.X, q.Z, cx, cz);
                if (l < 0) return null;
                return (l / speed, [new(Kind.Ride, cx, cz, ShoreNear, r, l / speed), new(Kind.Out, cx, cz, 0, r, 0)], (cx, cz));
            }
            default:
            {
                var m = ModeOf(FlagsOf(s, r));
                if (m == Mode.Land && (AreaOf(s, areas, q.X, q.Z, false) is var a and >= 0) && a != AreaOf(s, areas, g.X, g.Z, false)) return null;
                var l = WayLen(s, m, q.X, q.Z, g.X, g.Z);
                if (l < 0) return null;
                return (l / speed, [new(Kind.Ride, g.X, g.Z, near, r, l / speed), new(Kind.Out, g.X, g.Z, 0, r, 0)], g);
            }
        }
    }

    /// <summary>Plans the quickest trip for `me` to (gx, gz), ending on foot within `near`, over the vehicles he knows of
    /// (`rides`; riding, the first is the one he is in). Legs empty: there already; null: no way.</summary>
    public void Plan(WorldState s, Entity me, IReadOnlyList<Ride> rides, IReadOnlyList<Danger> dangers, int foot, int gx, int gz, int near, bool lastResort = false, (int X, int Z)? home = null)
    {
        at = 0; goal = (gx, gz); Dangers = dangers; footSpeed = foot; RidesKnown = rides.Count; LastResort = lastResort; this.home = home; this.rides = rides;
        var areas = Navigate.Areas(s);
        var riding = !me.Bit(0x08, 15);
        var swimming = !riding && me.Bit(0x08, 10);                          // in the sea: his start is no island's (AreaOf would take the land beside)
        int n = rides.Count, goalNode = 1 + 2 * n;
        var place = new (int X, int Z)?[goalNode + 1];
        place[0] = (me.UW(0x4A), me.UW(0x4E));
        for (var i = 0; i < n; i++) place[1 + i] = riding && i == 0 ? place[0] : (rides[i].X, rides[i].Z);
        place[goalNode] = (gx, gz);
        var best = new double[goalNode + 1]; Array.Fill(best, double.MaxValue);
        var from = new int[goalNode + 1]; var by = new List<Leg>?[goalNode + 1];
        var done = new bool[goalNode + 1];
        var open = new PriorityQueue<int, double>();
        var start = riding ? 1 : 0;
        best[start] = 0; from[start] = -1; open.Enqueue(start, 0);
        void Link(int u, int v, double t, List<Leg> legs)
        {
            if (best[u] + t >= best[v]) return;
            best[v] = best[u] + t; from[v] = u; by[v] = legs; open.Enqueue(v, best[v]);
        }
        while (open.TryDequeue(out var u, out _))
        {
            if (done[u]) continue;
            done[u] = true;
            if (u == goalNode) break;
            var p = place[u]!.Value;
            if (u == 0 || u > n)                                             // on foot: to the goal, or to a vehicle
            {
                var wet = u == 0 && swimming;
                if (Foot(s, areas, foot, s.Energy, p, (gx, gz), near, null, wet) is var (t, legs)) Link(u, goalNode, t, legs);
                for (var j = 0; j < n; j++)
                {
                    if (riding && j == 0 || u == 1 + n + j) continue;      // not the one he is in, nor back into the one just left
                    if (Foot(s, areas, foot, s.Energy, p, place[1 + j]!.Value, 0, rides[j], wet) is var (tb, lb)) Link(u, 1 + j, tb, lb);
                }
            }
            else                                                             // in vehicle u - 1: on to the goal's drop, or out here
            {
                var i = u - 1;
                if (Drive(s, areas, rides[i], p, (gx, gz), near) is var (t, legs, drop))
                {
                    place[1 + n + i] = drop;
                    Link(u, 1 + n + i, t, legs);
                }
                for (var j = 0; j < n; j++)                                  // or on in it to another vehicle he knows of, and into that
                {
                    if (j == i || riding && j == 0) continue;
                    var to = place[1 + j]!.Value;
                    if (Drive(s, areas, rides[i], p, to, Beside) is not var (dt, dl, dd)) continue;
                    if (dl[0] is { Kind: Kind.Ride, Near: Beside })         // by land it ends Beside off, not on it (Hunt.Ended: the larger side within
                    {                                                        // Near) - counted as on it, M2 11-09 got in a van 503 off a tank, out at
                        double ex = Senses.Diff(to.X, p.X), ez = Senses.Diff(to.Z, p.Z), side = Math.Max(Math.Abs(ex), Math.Abs(ez));   // once, the walk
                        if (side <= Beside) { dt = 0; dl = [new(Kind.Out, p.X, p.Z, 0, rides[i], 0)]; dd = p; }                       // left out, 22000
                        else dd = ((ushort)(p.X + (int)(ex * (1 - Beside / side))), (ushort)(p.Z + (int)(ez * (1 - Beside / side))));    // passes in and out
                    }
                    if (Foot(s, areas, foot, s.Energy, dd, to, 0, rides[j]) is not var (ft, fl)) continue;
                    Link(u, 1 + j, dt + ft, [.. dl, .. fl]);
                }
                if (riding && i == 0 && !(Body.Flies(me) && me.B[0x1E] == 0)) Link(u, 0, 0, [new(Kind.Out, p.X, p.Z, 0, rides[0], 0)]);
            }
        }
        if (best[goalNode] == double.MaxValue) { Legs = null; return; }
        var path = new List<Leg>();
        for (var v = goalNode; from[v] != -1; v = from[v]) path.InsertRange(0, by[v]!);
        Legs = path;
    }
}
