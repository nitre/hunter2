namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (LIVING WORLD, owner 2026-11-01, docs/living-world.md): people and animals
/// notice each other, not only the player. In the original every planner's target is the player (PlanEntity $270E:
/// +$16 = slot 0). Here a planner may take another awake thing as its target, chosen by the original's own data:
/// - the side, from the kind's kill score (+$32): above 0 an enemy, below 0 a friend; the animals $14-$1C are wild;
/// - what it notices, its concern mask (+$0F) against the target's status (the player's $219, or for anyone else the
///   same bits made from its flags, as PlayerSetUpOrDie $2E72 makes $219);
/// - attack or run, by strength (+$0E), as for the player ($2792).
/// Enemies go for armed friends (civilians are not shot: they run, owner 2026-11-01), friends for enemies, and
/// wild things on the ground for any person or vehicle. The player stays the enemy's first target: a friend is
/// chosen only when nearer. Flyers (gulls, vultures) and anything noticing nothing keep the original's ways.
/// Off (WorldState.LivingWorld false): nothing here runs.</summary>
public static class LivingWorld
{
    public enum Side : byte { None, Player, Enemy, Friend, Wild }

    /// <summary>How far (FindNearestInRange's measure, $340E: the larger of |dx| and |dz|) another thing is noticed, in
    /// world units: 8 map squares. Tuned, not the original's (it has no other targets); the player has no limit.</summary>
    public const int Reach = 0x1000;

    /// <summary>How often, in passes, every planner chooses again with the setting on (the original replans only when
    /// the player's status changes, $A5 bit 4) - so targets follow who has come near.</summary>
    public const int ReplanEvery = 16;

    static bool Bit(int v, int n) => (v >> n & 1) != 0;

    static short KindWord(WorldState s, int type, int off)
    {
        return s.Data.KindWord(type, off);                                   // ExtraKinds
    }

    /// <summary>A shot: the shot types $52-$5F, the game's own test (Ground.IsShot).</summary>
    public static bool Shot(Entity e) => Ground.IsShot(e);

    /// <summary>Has a gun: the record's weapon, the shot type it fires (+$26, 0 or less = none: NoWeaponReturn $2680, Movement.FireTimer).</summary>
    public static bool HasGun(Entity e) => e.W(0x26) > 0;

    /// <summary>Fires when in range: its kind's behaviour bit 3 (SteerToTarget $291A). Read from the kind's start plan
    /// (+$1A, $329E), not the record's +$0C, which the planner rewrites (to 0 when it gives up).</summary>
    public static bool FiresInRange(WorldState s, Entity e) => (KindWord(s, e.W(4), 0x1A) & 1 << 3) != 0;

    /// <summary>Cries instead of firing: its kind's behaviour bit 1 (FireTimer $26AA: a sound, no round - the gull).
    /// From the kind's start plan, as FiresInRange.</summary>
    public static bool Cries(WorldState s, Entity e) => (KindWord(s, e.W(4), 0x1A) & 1 << 1) != 0;

    /// <summary>A thing's side. None: shots and blasts, buildings and items (d7 bit 27 clear), empty vehicles (bit 28:
    /// nobody in it, re/formats/vehicles.md - a parked car frightens nothing), kinds with a kill score of 0.</summary>
    public static Side SideOf(WorldState s, Entity e)
    {
        var type = e.W(4);
        if (!e.Alive || type >= ExtraKinds.End || !e.Flag(27) || e.Flag(28) || Shot(e)) return Side.None;
        if (type is >= 0x14 and <= 0x1C) return Side.Wild;                 // seagull .. vulture (the kind table's animals)
        var score = KindWord(s, type, 0x32);
        return score > 0 ? Side.Enemy : score < 0 ? Side.Friend : Side.None;
    }

    /// <summary>Armed: a gun that it fires when in range, and not a cry.</summary>
    public static bool Armed(WorldState s, Entity e) => HasGun(e) && FiresInRange(s, e) && !Cries(s, e);

    /// <summary>A flyer (d7 bit 11, the helicopter class): the wild ones keep the original's ways.</summary>
    static bool Flyer(Entity e) => e.Flag(11);

    /// <summary>Regions: each map square's island (positive) or stretch of water (negative), joined edge to edge -
    /// the sea squares (type 0) apart from the rest. Built once a world (WorldState.LivingRegions).</summary>
    static int[] Regions(WorldState s)
    {
        if (s.LivingRegions is { } done) return done;
        var t = s.Terrain;
        var id = new int[128 * 128];
        int land = 0, sea = 0;
        var todo = new Stack<int>();
        for (var start = 0; start < id.Length; start++)
        {
            if (id[start] != 0) continue;
            var wet = t.Type(start % 128, start / 128) == 0;
            var mark = wet ? -++sea : ++land;
            id[start] = mark;
            todo.Push(start);
            while (todo.Count > 0)
            {
                var k = todo.Pop();
                int c = k % 128, r = k / 128;
                foreach (var (nc, nr) in new[] { (c + 1, r), (c - 1, r), (c, r + 1), (c, r - 1) })
                {
                    if (nc < 0 || nr < 0 || nc > 127 || nr > 127) continue;
                    var n = nr * 128 + nc;
                    if (id[n] == 0 && (t.Type(nc, nr) == 0) == wet) { id[n] = mark; todo.Push(n); }
                }
            }
        }
        return s.LivingRegions = id;
    }

    /// <summary>The region at a point: by the original's own land test ($36AA, the coast's triangles) - a coast
    /// square's water half is the water beside it.</summary>
    public static int RegionAt(WorldState s, int x, int z)
    {
        var id = Regions(s);
        int c = (x & 0xFFFF) >> 9, r = (z & 0xFFFF) >> 9;
        var own = id[r * 128 + c];
        var land = s.Ground.Sample((ushort)x, (ushort)z).Land;
        if (land == own > 0) return own;
        for (var dr = -1; dr <= 1; dr++)
            for (var dc = -1; dc <= 1; dc++)
            {
                int nc = c + dc, nr = r + dr;
                if (nc < 0 || nr < 0 || nc > 127 || nr > 127) continue;
                var n = id[nr * 128 + nc];
                if (n > 0 == land) return n;
            }
        return own;
    }

    /// <summary>Owner 2026-11-01: a wild thing only stares at what can't reach it - anything not on the ground in its
    /// own region (a boat seen from land, an aircraft, a jeep on another island; for a fish, anything not in its
    /// water): it turns to watch and stands still (Behaviour.Steer), neither charging nor running.</summary>
    public static bool Stares(WorldState s, Entity e, Entity t)
    {
        if (SideOf(s, e) != Side.Wild || Flyer(e)) return false;
        if (t.B[0x1E] == 0) return true;                                     // off the ground: flying
        return RegionAt(s, e.UW(0x4A), e.UW(0x4E)) != RegionAt(s, t.UW(0x4A), t.UW(0x4E));
    }

    /// <summary>What e may take as a target besides the player.</summary>
    public static bool Opponent(WorldState s, Entity e, Entity t)
    {
        var me = SideOf(s, e);
        var it = SideOf(s, t);
        return me switch
        {
            Side.Enemy => it == Side.Friend && Armed(s, t),
            Side.Friend => it == Side.Enemy,
            Side.Wild => !Flyer(e) && it is Side.Enemy or Side.Friend,
            _ => false,
        };
    }

    /// <summary>The status bits a thing shows to a concern mask: its flag bits 10-15 as bits 0-5, as $2E72 makes $219
    /// for the player (boat, helicopter, aircraft, tank, car, on foot); a person in the water (on foot and bit 10, the
    /// swim toggle) shows bit 6 alone, as the player's $219 after WalkSwimSwitch ($2BAC).</summary>
    public static byte Status(Entity t)
    {
        var st = (byte)(t.Flags >> 10 & 0x3F);
        return (st & 0x21) == 0x21 ? (byte)0x40 : st;
    }

    static int Measure(WorldState s, Entity a, Entity b)
    {
        static int A(int v) => v < 0 ? 0x8000 : v;                           // off: exactly 32768 apart stays -32768 as a word
        return Math.Max(A(WorldState.AbsDist(s, a.UW(0x4A), b.UW(0x4A))), A(WorldState.AbsDist(s, a.UW(0x4E), b.UW(0x4E))));
    }

    /// <summary>The slot of a shot's shooter, from its owner word +$2C (the record's address low word, Weapons.Fire);
    /// -1 if it has none.</summary>
    public static int OwnerSlot(WorldState s, Entity shot)
    {
        var off = shot.UW(0x2C);
        if (off == 0) return -1;
        var slot = (off - 0x250 - 0x806) / EntityTable.Size;
        return slot >= 0 && slot < s.Entities.Count && (off - 0x250 - 0x806) % EntityTable.Size == 0 ? slot : -1;
    }

    /// <summary>The target slot for a plan (PlanEntity's d4 / +$16), given the player slot the original would take.
    /// A shot homes on its shooter's target. Enemies and wild things: the nearest noticed opponent in Reach if nearer
    /// than the player (or the player unnoticed); friends: the nearest noticed enemy, else the player.</summary>
    public static int Target(WorldState s, Entity e, int player)
    {
        if (Shot(e))                                                         // a shot: its shooter's target
        {
            var o = OwnerSlot(s, e);
            if (o < s.Players) return player;                                // the player's own, or no shooter
            var ot = s.Entities.All[o].UW(0x16) / EntityTable.Size;
            return ot < s.Entities.Count && s.Entities.All[ot].Alive ? ot : player;
        }
        var mask = e.B[0x0F];
        var side = SideOf(s, e);
        if (mask == 0 || side is Side.None) return player;
        int best = -1, bestD = int.MaxValue;
        for (var n = s.Players; n < s.Entities.Count; n++)
        {
            var t = s.Entities.All[n];
            if (t == e || !t.Alive || (mask & Status(t)) == 0 || !Opponent(s, e, t)) continue;
            var d = Measure(s, e, t);
            if (d <= Reach && d < bestD) (best, bestD) = (n, d);
        }
        if (best < 0) return player;
        if (side == Side.Friend) return best;
        var p = s.Entities.All[player];
        var pd = (mask & s.PlayerAt(player).Status219) != 0 && p.Alive ? Measure(s, e, p) : int.MaxValue;
        return bestD < pd ? best : player;
    }

    /// <summary>Who did the harm, for the kill's score (Collisions $454E): the shot's shooter, or the hurter itself.</summary>
    public static Side Hurter(WorldState s, Entity hurter, int hurterSlot)
    {
        var o = OwnerSlot(s, hurter);
        var slot = o >= 0 ? o : hurterSlot;
        return slot < s.Players ? Side.Player : SideOf(s, s.Entities.All[slot]);
    }

    /// <summary>A kill that does not score for the player: done by an enemy or a wild thing (an enemy shooting your
    /// comrade must not cost you; an enemy's loss to a bull earns you nothing). Counted for objectives either way
    /// (owner 2026-11-01: a friend's kill counts; the original counts every end, $9288).</summary>
    public static bool ScoresNothing(Entity e) => e.HurtBy is Side.Enemy or Side.Wild;
}
