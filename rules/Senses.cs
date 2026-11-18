namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (the BOT's senses and memory, docs/bot-brain.md 1 and its step 1 plan): everything
/// the brain knows comes through here, and only what a player could know - what is in sight (on the original's screen,
/// InView, and not behind a wall, Arms.Sees), the message line (WorldState.Heard, understood by template), the log book's
/// places, the drone's map (OnDroneMap), and his own memory. One NOTE per thing he has seen (Thief's sense links, Halo 2's
/// props): where and when he last saw it and how aware of it he is. A thing comes into sight SEEN only after Reaction
/// blanks in sight; out of sight it falls Seen -> Lately -> Known, never jumping down.</summary>
public sealed class Senses
{
    /// <summary>Blanks a thing must be in sight before he reacts to it: about a quarter second at 50 blanks a second, the
    /// usual figure for a person's simple reaction (Q3's bots hold one per character).</summary>
    public const int Reaction = 13;
    /// <summary>Blanks after he last saw it that a thing stays Lately (then Known).</summary>
    public const int LatelyFor = 250;

    public enum Aware { Known, Lately, Seen }

    /// <summary>What he knows of one thing: its slot and type (a slot rebuilt as something else - EntityEnds $3070 - is a new
    /// thing), where and when he last saw it, which way it was heading, and how many blanks running it has been in sight.</summary>
    public sealed class Note(Entity e, int type)
    {
        public Entity E { get; } = e;
        public int Type { get; } = type;
        public int X, Z, Seen = int.MinValue, InSight;
        /// <summary>Its near edge (+$72) when last seen: the game draws a thing by that edge, not its middle (InView).</summary>
        public int Near;
        /// <summary>The height of its middle when last seen (+$4C + $70): what the line from his eyes goes to (Arms.SeesSpot).</summary>
        public int Mid;
        public byte Heading;
        /// <summary>Last seen a vehicle he could get into, empty (Hunt.Boardable).</summary>
        public bool Free;
        /// <summary>Last seen armed against him (Hunt.Threat).</summary>
        public bool Threat;
        /// <summary>Where it was is on his screen, near him (Hunt.Sight), and it is not there: moved or ended.</summary>
        public bool Gone;
        public Aware Aware(int now) => InSight >= Reaction ? Senses.Aware.Seen : now - Seen <= LatelyFor ? Senses.Aware.Lately : Senses.Aware.Known;
    }

    /// <summary>A line he read on the message line: the blank, the template's address (re/texts.txt) and its arguments.</summary>
    public readonly record struct Line(int Blank, int Template, int[] Args);

    /// <summary>The templates he acts on (re/formats/game.md 2).</summary>
    public const int Boarded = 0xB539, Destroyed = 0xB5B5, Occupied = 0xB528, LowFuel = 0xA40A, Tired = 0xB034;

    readonly Dictionary<Entity, Note> notes = [];
    readonly Dictionary<int, (int X, int Z)> known = [];
    readonly List<(int X, int Z)> givenUp = [];
    WorldState? heardFrom;

    public int Now { get; private set; }
    public List<Line> Lines { get; } = [];
    public IEnumerable<Note> Notes => notes.Values;

    // ---- VEHICLE MEMORY (owner 11-08: flag them, never forget them - docs/bot-brain.md "VEHICLE MEMORY PLAN") ----
    public enum CarState { Parked, Mine, NoUse, Gone }

    /// <summary>One vehicle he knows of, kept for good: its type, where he last knew it, its slot when last seen (the game
    /// moves a vehicle to another slot when he gets out, LeaveVehicle $2FDC), the gauge he last saw on it (passes - a player
    /// sees one only once in), empty or not, its near edge (+$72), and what it is to him now.</summary>
    public sealed class Car(int type, int x, int z)
    {
        public int Type { get; } = type;
        public int X = x, Z = z, Seen = int.MinValue, Near;
        /// <summary>The height of its middle when last seen (+$4C + $70); null: never seen by him (a drone look, left by him).</summary>
        public int? Mid;
        public Entity? E;
        public int? Lasts;
        public bool Free = true;
        public CarState State;
    }
    public List<Car> Cars { get; } = [];
    /// <summary>Vehicles are the types below $13 (the boats, cars and aircraft).</summary>
    public static bool IsCar(int type) => type < 0x13;

    /// <summary>The entry for a vehicle of this type at (x, z): the one with that slot first, else the nearest within `within`
    /// that he is not in.</summary>
    public Car? CarAt(int type, int x, int z, Entity? e = null, int within = 256)
    {
        if (e is not null && Cars.FirstOrDefault(c => c.E == e && c.Type == type && c.State != CarState.Mine) is { } same) return same;
        return Cars.Where(c => c.Type == type && c.State != CarState.Mine && Dist(c.X, c.Z, x, z) < within).MinBy(c => Dist(c.X, c.Z, x, z));
    }

    /// <summary>He got into a vehicle of this type here: the nearest entry within 512 is the one (else a new one) - Mine.</summary>
    public void GotIn(int type, int x, int z)
    {
        var c = CarAt(type, x, z, null, 512);
        if (c is null) Cars.Add(c = new Car(type, x, z));
        c.State = CarState.Mine; c.E = null;
    }

    /// <summary>He got out: the entry he was in is parked where he left it, with the gauge he saw (null: no gauge); a ride that
    /// went nowhere leaves it no use from here.</summary>
    public void GotOut(int type, int x, int z, int? lasts, bool nowhere)
    {
        var c = Cars.FirstOrDefault(c => c.State == CarState.Mine && c.Type == type);
        if (c is null) Cars.Add(c = new Car(type, x, z));
        Entity? slot = null;                                                 // the senses run before he knows he is out: the vehicle seen there this pass
        if (Cars.Where(o => o != c && o.Type == type && o.State != CarState.Mine && Now - o.Seen <= 1 && Dist(o.X, o.Z, x, z) < 256).MinBy(o => Dist(o.X, o.Z, x, z)) is { } twin)
        { slot = twin.E; Cars.Remove(twin); }                               // was this one, not another (11-08: a twin entry, parked, sent him straight back in)
        (c.X, c.Z, c.E, c.Free, c.Seen) = (x, z, slot, true, Now);
        if (lasts is int l) c.Lasts = l;
        c.State = nowhere ? CarState.NoUse : CarState.Parked;
    }

    /// <summary>The drone's looks (owner 11-08: he launches it for real, docs/bot-brain.md): where each was launched from,
    /// and the guns and targets its map showed then (vehicles go to Cars) - known only after a look, held as he saw them.</summary>
    public List<(int X, int Z)> Looks { get; } = [];
    public List<(int Type, int X, int Z)> Looked { get; } = [];

    /// <summary>The map squares he has seen (SCOUTING, owner 11-09): each whose middle was on his screen (InView), and each a
    /// drone look showed - what he has looked over for vehicles.</summary>
    readonly bool[] seen = new bool[Navigate.Size * Navigate.Size];
    static int Sq(int x, int z) => ((ushort)z >> 9 & (Navigate.Size - 1)) * Navigate.Size + ((ushort)x >> 9 & (Navigate.Size - 1));
    public bool Seen(int x, int z) => seen[Sq(x, z)];
    /// <summary>The middle of square q.</summary>
    public static (int X, int Z) Middle(int q) => ((q % Navigate.Size) * Navigate.Square + Navigate.Square / 2, (q / Navigate.Size) * Navigate.Square + Navigate.Square / 2);
    /// <summary>Every square not yet seen, by index.</summary>
    public IEnumerable<int> Unseen() => Enumerable.Range(0, seen.Length).Where(q => !seen[q]);

    /// <summary>The map screen is up: what it shows round him of these types (DroneMap, the grid written back as $3D0A does),
    /// replacing what an older look held on the same patch.</summary>
    public void Look(WorldState s, IReadOnlyCollection<int> types)
    {
        var me = s.Entities.All[0];
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        Looks.Add((mx, mz));
        LastShown.Clear();
        LastShown.AddRange(DroneMap(s, mx, mz, types));
        Looked.RemoveAll(r => OnDroneMap(mx, mz, r.X, r.Z));
        Looked.AddRange(LastShown.Where(r => !IsCar(r.Type)));
        var shownCars = new HashSet<Car>();
        foreach (var (t, x, z) in LastShown.Where(r => IsCar(r.Type)))   // a vehicle shown: its entry moved there (a new one if none)
        {
            var c = CarAt(t, x, z);
            if (c is null) Cars.Add(c = new Car(t, x, z));
            else { (c.X, c.Z) = (x, z); if (c.State == CarState.Gone) c.State = CarState.Parked; }
            shownCars.Add(c);
        }
        foreach (var c in Cars)                                              // on the patch and not shown: gone
            if (c.State == CarState.Parked && !shownCars.Contains(c) && OnDroneMap(mx, mz, c.X, c.Z)) c.State = CarState.Gone;
        for (var q = 0; q < seen.Length; q++)
            if (!seen[q] && Middle(q) is var (x, z) && OnDroneMap(mx, mz, x, z)) seen[q] = true;
    }
    /// <summary>Everything the last drone look showed (the run log counts it).</summary>
    public List<(int Type, int X, int Z)> LastShown { get; } = [];

    /// <summary>A look from within a quarter of the drone's patch across (7 squares) of (x, z): nothing new to see there.</summary>
    public bool LookedNear(int x, int z) => Looks.Any(l => Dist(l.X, l.Z, x, z) < 7 * Navigate.Square);

    /// <summary>Each blank, before the brain thinks: the message line hooked up, and every thing that matters to him (a
    /// threat, an open target, a vehicle) noted as in sight or not.</summary>
    public void Update(WorldState s, List<int> open)
    {
        Now++;
        if (heardFrom != s) { if (heardFrom is not null) heardFrom.Heard -= OnHeard; s.Heard += OnHeard; heardFrom = s; }
        if (!s.InPlay) return;                                               // the screen fading in or out ($99 bit 7 clear): the world near him not there
                                                                             // yet - nothing seen, nothing judged gone (M4 in one game 11-09: 15 passes of
                                                                             // fade, only he alive, the helicopter and bicycle by the HQ "gone" - no way off)
        var me = s.Entities.All[0];
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        foreach (var n in notes.Values) if (n.E.W(4) != n.Type || !n.E.Alive) n.InSight = 0;
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            if (!e.Alive) continue;
            var type = e.W(4);
            if (!Matters(e, type, open)) continue;
            int x = e.UW(0x4A), z = e.UW(0x4E);
            var inSight = InView(s, x, z, e.W(0x72)) && Arms.Sees(s, me, e);
            if (!notes.TryGetValue(e, out var n) || n.Type != type)
            {
                if (!inSight) continue;
                notes[e] = n = new Note(e, type);
            }
            if (!inSight) { n.InSight = 0; continue; }
            n.InSight++; n.Seen = Now; n.X = x; n.Z = z; n.Near = e.W(0x72); n.Mid = e.W(0x4C) + e.W(0x70); n.Heading = e.B[0x7E]; n.Free = Hunt.Boardable(e); n.Threat = Hunt.Threat(e); n.Gone = false;
            if (IsCar(type))                                                 // a vehicle in sight: its entry (that slot, else the nearest of the type)
            {
                var c = CarAt(type, x, z, e);
                if (c is null) Cars.Add(c = new Car(type, x, z));
                (c.X, c.Z, c.E, c.Seen, c.Near, c.Free, c.Mid) = (x, z, e, Now, e.W(0x72), n.Free, n.Mid);
                if (c.State == CarState.Gone) c.State = CarState.Parked;
            }
        }
        foreach (var c in Cars)                                              // a parked one's place in view, by its near edge as the game draws, near him, nothing
            if (c.State is CarState.Parked or CarState.NoUse && Now - c.Seen > Reaction && InView(s, c.X, c.Z, c.Near) && Dist(mx, mz, c.X, c.Z) <= Hunt.Sight
                && Arms.SeesSpot(s, me, c.X, c.Z, c.Mid ?? s.Ground.Sample((ushort)c.X, (ushort)c.Z).Item1))   // in the way of the spot (seen is the same line:
                c.State = CarState.Gone;                                     // a building hides both - M3 in one game 11-09), and not seen there: gone (a shark ended M4's boat 11-09)
        foreach (var n in notes.Values)
            if (n.InSight == 0 && Now - n.Seen > Reaction && InView(s, n.X, n.Z, n.Near) && Dist(mx, mz, n.X, n.Z) <= Hunt.Sight
                && Arms.SeesSpot(s, me, n.X, n.Z, n.Mid)) n.Gone = true;   // by its near edge, as
                                                                             // in sight is: its middle on screen, the game not drawing it, a parked helicopter
                                                                             // "gone" (M4 11-08: walked 5400 passes instead of flying 13 s)
        for (var dr = -7; dr <= 7; dr++)                                     // the screen's squares: 1024 either side, 3072 north of the camera
            for (var dc = -7; dc <= 7; dc++)                                 // (south of him), turned any way with the view (Q / E)
            {
                var q = Sq(mx + dc * Navigate.Square, mz + dr * Navigate.Square);
                if (!seen[q] && Middle(q) is var (x, z) && InView(s, x, z, 0)) seen[q] = true;
            }
    }

    /// <summary>On his screen as the ORIGINAL shows it (owner 11-08: the original's, not the card's view distance): Place's
    /// box ($4216, the +$1C mark - ViewDistance.InView at 7 rows equals it, ViewDistanceTests): 1024 either side of the
    /// camera, out to 3072 north of it, nothing behind - the camera sits south of him ($374C). The port's view turns round him
    /// (Q / E, ViewAngle1, owner 11-08): the point is turned back by it about him, then the original's box.</summary>
    public static bool InView(WorldState s, int x, int z, int near72)
    {
        if (s.ViewAngle1 != 0)
        {
            var me = s.Entities.All[0];
            double dx = Diff(x, me.UW(0x4A)), dz = Diff(z, me.UW(0x4E)), v = s.ViewAngle1 * Math.PI / 128;
            x = (ushort)(me.UW(0x4A) + (int)Math.Round(dx * Math.Cos(v) - dz * Math.Sin(v)));     // across the turned view
            z = (ushort)(me.UW(0x4E) + (int)Math.Round(dx * Math.Sin(v) + dz * Math.Cos(v)));     // along it
        }
        return ViewDistance.InView(s.Camera, x, z, near72, 7, 1);
    }

    static bool Matters(Entity e, int type, List<int> open) => Hunt.Threat(e) || open.Contains(type) || e.Flag(28);

    void OnHeard(int template, int[] args) => Lines.Add(new(Now, template, args));

    /// <summary>The line of that template he read this blank or the one before, if any.</summary>
    public Line? Heard(int template) =>
        Lines.Count > 0 && Lines[^1] is var l && l.Template == template && Now - l.Blank <= 1 ? l : null;

    /// <summary>In sight now, and long enough to have reacted to it.</summary>
    public bool SeesNow(Entity e) => notes.TryGetValue(e, out var n) && n.Type == e.W(4) && n.Aware(Now) == Aware.Seen;

    public Note? NoteOf(Entity e) => notes.TryGetValue(e, out var n) && n.Type == e.W(4) ? n : null;

    /// <summary>A place he has given up on (a vehicle he could not reach, occupied, wedged, out of fuel).</summary>
    public void GiveUp(int x, int z)
    {
        if (!givenUp.Contains((x, z))) givenUp.Add((x, z));
        if (Cars.Where(c => c.State == CarState.Parked && Dist(c.X, c.Z, x, z) < 256).MinBy(c => Dist(c.X, c.Z, x, z)) is { } car) car.State = CarState.NoUse;   // the vehicle there, flagged
    }
    public bool GivenUp(int x, int z, int within) => givenUp.Any(p => Dist(p.X, p.Z, x, z) < within);

    /// <summary>Where the first thing of a type is (LogBook.FindRecord), kept once found: a record leaves the grid while its
    /// thing is awake near the camera, so the lookup misses the stores or the HQ just as he comes up to them. Seen gone: at
    /// the place with no live one of the type there (destroyed - EntityEnds $3070 builds its ruin in the slot), he forgets
    /// it, as a player who sees the ruin would.</summary>
    public (int X, int Z)? Place(WorldState s, int type)
    {
        if (LogBook.FindRecord(s, type, 0) is { } at) known[type] = at;
        else if (known.TryGetValue(type, out var was))
        {
            var me = s.Entities.All[0];
            if (Dist(me.UW(0x4A), me.UW(0x4E), was.X, was.Z) <= Hunt.Sight / 2 && LiveNear(s, type, was.X, was.Z) is null) known.Remove(type);
        }
        if (!known.ContainsKey(type) && LiveNear(s, type, -1, -1) is { } e)  // awake now: the live one
            known[type] = (e.UW(0x4A), e.UW(0x4E));
        return known.TryGetValue(type, out var k) ? k : null;
    }

    /// <summary>A live thing of the type (a store: +$90 negative) within Sight of (x, z); x below 0: anywhere.</summary>
    public static Entity? LiveNear(WorldState s, int type, int x, int z)
    {
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            if (e.Alive && e.W(4) == type && (type != 0x95 || e.W(0x90) < 0) && (x < 0 || Dist(e.UW(0x4A), e.UW(0x4E), x, z) < Hunt.Sight)) return e;
        }
        return null;
    }

    /// <summary>What a player may know of the world's records: the drone's map (MapScreen $3CFA, DrawMapScreen $3D7C) shows
    /// 28 x 17 squares round him - 14 squares back and 13 on across (MapRecords $3F44: x - $1C00), 8 either way up and down -
    /// with every record by kind (building, vehicle, structure, ground gun, person). Beyond it he knows only the log book's
    /// places. (He does not yet launch the drone itself: key A, 100 blanks, the map, RESUME - step 7.)</summary>
    public static bool OnDroneMap(int mx, int mz, int x, int z, double part = 1)
    {
        int dx = Diff(x, mx), dz = Diff(z, mz);
        return dx >= -0x1C00 * part && dx < 0x1A00 * part && Math.Abs(dz) <= (8 * Navigate.Square + Navigate.Square / 2) * part;
    }

    /// <summary>Has a drone look shown (x, z) well inside its patch (`part` of the way to its edge)?</summary>
    public bool LookedAt(int x, int z, double part) => Looks.Any(l => OnDroneMap(l.X, l.Z, x, z, part));

    /// <summary>The map records of these types the drone's map would show round him (OnDroneMap): the grid's records ($83BA,
    /// +0 the kind word, +6 / +$A the place) and roomy memory's overflow, in one pass.</summary>
    public static IEnumerable<(int Type, int X, int Z)> DroneMap(WorldState s, int mx, int mz, IEnumerable<int> types)
    {
        var kinds = new Dictionary<int, int>();                                // kind word -> type; two types of one kind word: the first
        foreach (var t in types) kinds.TryAdd(Vehicles.KindForType(s.Data.Main, t) & 0xFFFF, t);
        var g = s.Grid;
        for (var r = 0; r < 0x800; r++)
            if (kinds.TryGetValue(g[r * 16] << 8 | g[r * 16 + 1], out var t) && (g[r * 16 + 6] << 8 | g[r * 16 + 7]) is var x
                && (g[r * 16 + 10] << 8 | g[r * 16 + 11]) is var z && OnDroneMap(mx, mz, x, z)) yield return (t, x, z);
        if (!s.RoomyMemory) yield break;
        foreach (var list in s.Overflow.Values)
            foreach (var o in list)
                if (kinds.TryGetValue(o[0] << 8 | o[1], out var t) && (o[6] << 8 | o[7]) is var x && (o[10] << 8 | o[11]) is var z
                    && OnDroneMap(mx, mz, x, z)) yield return (t, x, z);
    }

    /// <summary>The larger of the two straight differences (the map does not wrap, Wish.Toward).</summary>
    /// <summary>a - b across the map: whole, never 16 bits - the map does not wrap (its edge ring is blocked), and a 16-bit
    /// difference turns one over 32767 the wrong way (11-08: GroundAhead took the ground west of a flight going east).</summary>
    public static int Diff(int a, int b) => (ushort)a - (ushort)b;

    public static int Dist(int ax, int az, int bx, int bz) => Math.Max(Math.Abs((ushort)ax - (ushort)bx), Math.Abs((ushort)az - (ushort)bz));
}
