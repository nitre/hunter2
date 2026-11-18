namespace Hunter.Rules;

/// <summary>
/// (vulture_graves): a NEW grave - a paid death's placed $7C
/// record (rules/Graves.cs Place) - brings a pair of vultures ($1C) that circles over it for a
/// while. The pair is the nearest live vulture in reach (FindNearestInRange's measure, $340E:
/// max of |dx| and |dz| as signed words) drafted to the spot, one per circle centre; a missing
/// one is spawned at the circle (the fuller world's own recipe: Fill.cs places its crypt pairs
/// at ground 600 up). The flight is the vulture's own: the wander bit of its behaviour goes on
/// (+$0A = its code | bit 14) so SteerToTarget ($2804) flies it at the wander point +$44/46/48
/// over the grave with its own turn table and speed; its code $0CD1 has no stop bit (bit 5
/// clear), so a reached point is flown through and turned back on - the orbit. Gravity (+$3A =
/// 5 a pass) sinks a circling vulture, so the circle height is held - a direct +$4C write, the
/// height guard's own idiom (Behaviour.cs:153). Off - the default - nothing here runs and the
/// game is byte for byte today's. Not in the save: a load drops the visit (like a debris
/// shower) and, because Summoned rides no save either, a restored grave is visited fresh.
/// </summary>
public static class VultureGraves
{
    /// <summary>One grave's visit: the pair, how many passes are left, and each vulture's own
    /// circle centre (one either side of the grave) at the shared circle height.</summary>
    public sealed class Post
    {
        public Grave Grave = null!;
        public Entity? A, B;
        public int Left;
        public int Ax, Az, Bx, Bz, Wy;
    }

    const int Visit = 600;          // passes the pair stays (about 12 s of play)
    const int Reach = 0x2000;       // a live vulture within this of the grave is drafted
    const int PairGap = 0x180;      // the two circle centres, one either side of the grave
    const int Vulture = 0x1C;

    /// <summary>A placed grave asks for its pair (Graves.Place, vulture_graves on). Once per
    /// grave: Summoned guards the retry (a grave whose record is re-placed after a save/load
    /// asks again otherwise). No free slot for a spawn: that vulture is simply missing.</summary>
    public static void Summon(WorldState s, Grave g)
    {
        if (g.Summoned) return;
        g.Summoned = true;
        var a = Nearest(s, g, null);
        var b = Nearest(s, g, a);                                            // the pair is two birds: a is taken
        if (a is null) a = Spawn(s, g.X + PairGap, g.Z);
        if (b is null) b = Spawn(s, g.X - PairGap, g.Z);
        if (a is null || b is null) return;
        var wy = CircleHeight(s, g.X, g.Z);
        s.VulturePosts.Add(new Post { Grave = g, A = a, B = b, Left = Visit, Ax = g.X + PairGap, Az = g.Z, Bx = g.X - PairGap, Bz = g.Z, Wy = wy });
    }

    /// <summary>Once a pass (Play.EndOfPass, vulture_graves on): the visit's vultures keep
    /// flying at their circle spots, and the visit ends its while - the grave taken (the kit
    /// picked up), the while run out, or a vulture gone (killed; no replacement mid-visit).</summary>
    public static void Tick(WorldState s)
    {
        for (var i = s.VulturePosts.Count - 1; i >= 0; i--)
        {
            var post = s.VulturePosts[i];
            if (post.Grave.Taken || post.Left-- <= 0 || !Flying(post.A) || !Flying(post.B))
            {
                s.VulturePosts.RemoveAt(i);
                continue;
            }
            Circle(post.A!, post.Ax, post.Az, post.Wy);
            Circle(post.B!, post.Bx, post.Bz, post.Wy);
        }
    }

    // The vulture's own plan flag (+$06 bit 0, $2712's own set) and wander plan: +$0A = its
    // behaviour code | bit 14 (wander target), replan bit 12 and flee bit 13 held off, so Steer
    // ($2804) turns it toward the spot and flies it there on its own speed ($36 = 16).
    static void Circle(Entity e, int cx, int cz, int wy)
    {
        e.SetL(6, e.Flags | 1);
        e.SetL(0x0A, (e.UW(0x0C) | 0x4000) & ~0x3001);       // Plan's own write (Behaviour.cs:26): Steer reads the LONG +$0A - bits 12/13/14 of it are the LOW word +$0C; a word at +$0A put the wander bit in the high word where Steer never looks. Bit 0 off too: "keep above the target" (Behaviour.cs:143) reads slot 0's height even on a wander target (Behaviour.cs:136) - it would pin the bird to the PLAYER's height +$12, not the circle
        if (e.W(0x3E) == 0) e.SetW(0x3E, e.W(0x36));         // a spawned bird stands ON its circle spot: Steer only sets the speed out of range (Behaviour.cs:196), so it would sit there stalled for ever - one kick to its cruise speed and the fly-through orbits
        e.B[0x7C] = e.B[0x39];                               // level flight: the movement's pitch is $7C - $39 (Movement.cs Vector) and a spawned bird's $7C is 0, near full climb (+16 a pass up) - set the record's own trim so the circle stays a circle; the height guard below holds the altitude
        e.SetW(0x44, cx); e.SetW(0x46, wy); e.SetW(0x48, cz);
        if (e.W(0x4C) < wy) e.SetW(0x4C, wy);                   // gravity sinks 5 a pass: hold the circle height
    }

    static bool Flying(Entity? e) => e is not null && e.Alive && e.W(4) == Vulture;

    /// <summary>The nearest live vulture not already posted (nor the pair's first bird), in
    /// reach of the grave by FindNearestInRange's measure (Behaviour.TargetPlayer's own loop).</summary>
    static Entity? Nearest(WorldState s, Grave g, Entity? skip)
    {
        Entity? best = null;
        var bestD = Reach;
        foreach (var e in s.Entities.All)
        {
            if (!Flying(e) || e == skip || Posted(s, e)) continue;
            var dx = WorldState.Dist(s, e.UW(0x4A), (ushort)g.X);
            var dz = WorldState.Dist(s, e.UW(0x4E), (ushort)g.Z);
            if (dx < 0) dx = -dx;
            if (dz < 0) dz = -dz;
            var d = dx > dz ? dx : dz;
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    static bool Posted(WorldState s, Entity e)
    {
        foreach (var post in s.VulturePosts)
            if (post.A == e || post.B == e) return true;
        return false;
    }

    /// <summary>A vulture spawned at a circle spot, the fuller world's recipe (Fill.Plan's vulture pairs:
    /// ground 600 up, never below 640): kind word $0208 (class 2, offset 8 over the $81BA
    /// table - the same word Fill's records carry), variant 0, heading 0.</summary>
    static Entity? Spawn(WorldState s, int x, int z)
    {
        var y = CircleHeight(s, x, z);
        return StartState.SpawnInFreeSlot(s, (short)x, y, (short)z, 0x0208 << 16 | Vulture, 0, 0);
    }

    // The circle height over the world spot: the ground (x16, $36AA), never below 640, 600 up.
    static int CircleHeight(WorldState s, int x, int z)
    {
        var gy = s.Ground.SmoothHeight((ushort)x, (ushort)z);              // FINER GROUND: a flight height, the smooth ground
        return (gy > 640 ? gy : 640) + 600;
    }
}
