namespace Hunter.Rules;

/// <summary>(the BOT, docs/bot-plan.md "finding weapons"): what the bot knows of its weapons, read
/// from the game's own tables as a player learns it by playing. A hit costs both sides the other's energy (Collisions $455C), so a
/// weapon's damage is its shot's energy (kind +$0A, $D45E + type * $34) - or, for a shot with none, the blast it ends as (kind +$08,
/// EndsAs: bazooka $53 -> $61 21, grenade $54 -> $64 21, ...). A thing ends when its energy goes below 0 ($2646), so it takes
/// energy / damage + 1 hits. Only the weapons that fly are used (bullets, bazooka shells, grenades): the mines, timed explosives
/// and the bomb lie where they are let go (speed 0), the SAM is for the air, items 6-8 are the vehicles' own.</summary>
public static class Arms
{
    public const int Bullets = 0, Bazooka = 1, Grenades = 2;
    public static readonly int[] Flown = [Bullets, Bazooka, Grenades];

    /// <summary>The bullets ($52, $5B): no model of their own (kind +$04 word 0).</summary>
    public static bool IsBullet(int type) => type is 0x52 or 0x5B;
    /// <summary>ENHANCEMENT (BULLET BOX, WorldState.BulletBox): a bullet's half width and half length - 16 across.</summary>
    public const int BulletHalf = 8;

    /// <summary>A shot type's touch box as it is born (SetCollisionBox $334A: its model's size, x and z halved; BULLET BOX's).</summary>
    public static (int W, int H, int D) ShotBox(WorldState s, int type)
    {
        var mo = s.Models.Offsets[Kind(s, type, 4) & 0xFF];
        var box = (s.Models.U(mo + 4) >> 1, s.Models.U(mo + 6), s.Models.U(mo + 8) >> 1);
        return s.BulletBox && IsBullet(type) ? (BulletHalf, box.Item2, BulletHalf) : box;
    }

    /// <summary>A word of the type's kind record ($D45E + type * $34, re/kind_fields.txt).</summary>
    public static int Kind(WorldState s, int type, int off)
    {
        return s.Data.KindWord(type, off);                                   // ExtraKinds: the port's copy, the extra types after
    }

    static int Iw(WorldState s, int item, int off) => (short)(s.Items[0x28 + item * 32 + off] << 8 | s.Items[0x28 + item * 32 + off + 1]);

    /// <summary>The kind's starting energy (+$0A); a map record may give a thing its own (StartState.Spawn's +4).</summary>
    public static int Energy(WorldState s, int type) => Kind(s, type, 0x0A);

    /// <summary>The shot type weapon item n fires (item +$1E, LoadWeapon $A37C).</summary>
    public static int Shot(WorldState s, int item) => Iw(s, item, 0x1E);

    /// <summary>What one shot of item n takes off a thing it hits (its shot's energy, or its blast's).</summary>
    public static int Damage(WorldState s, int item)
    {
        var shot = Shot(s, item);
        if (shot <= 0) return 0;
        var e = Energy(s, shot);
        if (e > 0) return e;
        var ends = Kind(s, shot, 0x08) & 0x7FFF;
        return ends > 0 ? Energy(s, ends) : 0;
    }

    /// <summary>How far round where it goes off item n's blast reaches, 0 for none: the shot's EndsAs (kind +$08) when that hurts
    /// (energy +$0A, contact damage $454E - not the $60 debris), its size its model's (kind byte +5 -> +$25, $334A: the header's
    /// +4 / +8 halved), the larger half.</summary>
    public static int Blast(WorldState s, int item)
    {
        var shot = Shot(s, item);
        if (shot <= 0) return 0;
        var ends = Kind(s, shot, 0x08) & 0x7FFF;
        if (ends <= 0 || ends == 0x60 || Energy(s, ends) <= 0) return 0;
        var model = s.Data.KindByte(ends, 5);
        var o = s.Models.Offsets[model];
        return Math.Max(s.Models.U(o + 4), s.Models.U(o + 8)) >> 1;
    }

    /// <summary>How far it flies: its kind's top speed (+$12) a tick for its life (+$0E) ticks.</summary>
    public static int Range(WorldState s, int item) => Kind(s, Shot(s, item), 0x12) * Kind(s, Shot(s, item), 0x0E);

    /// <summary>Rounds of item n carried: the weapon in hand counts down in the record (+$32), the rest in the item table (+0).</summary>
    /// (The table's count follows the hand's: UpdatePanel's RoundsShownSyncStock $A3A0 writes +$32 into it every pass the count
    /// changed, before EndOfPass can change weapon - so no rounds are gained by changing back and forth. The hand's count is
    /// read for the weapon in hand as it is the newer within a pass.)
    /// In a vehicle the count is what he will carry out of it: an armed vehicle's rounds are added to its weapon's item on
    /// boarding ($A376) and every count is cut to its most (+2) when he gets out (ClampItemCounts $A302).
    public static int Rounds(WorldState s, int item)
    {
        var me = s.Entities.All[0];
        var n = item != s.Weapon ? Iw(s, item, 0) : me.W(0x26) > 0 ? Math.Max(0, (int)me.W(0x32)) : 0;
        return me.Bit(0x08, 15) ? n : Math.Min(n, Iw(s, item, 2));
    }

    /// <summary>The hits a thing of `energy` takes from item n.</summary>
    public static int Hits(WorldState s, int item, int energy) => Damage(s, item) is var d and > 0 ? energy / d + 1 : int.MaxValue;

    /// <summary>The weapon to use on a thing of `energy`: of the flown weapons carried, one with the rounds to finish it in the
    /// fewest hits (the lighter one on a tie: the shells are kept for what needs them); none can finish it: the one that
    /// does it the most harm. -1 with nothing carried.</summary>
    public static int Choose(WorldState s, int energy)
    {
        int best = -1, bestHits = int.MaxValue, mostHarm = -1, harm = 0;
        foreach (var i in Flown)
        {
            var r = Rounds(s, i);
            if (r <= 0) continue;
            var h = Hits(s, i, energy);
            if (h <= r && h < bestHits) (best, bestHits) = (i, h);
            if (r * Damage(s, i) > harm) (mostHarm, harm) = (i, r * Damage(s, i));
        }
        return best >= 0 ? best : mostHarm;
    }

    /// <summary>Is what is carried enough for everything still to destroy (the open objectives' counts left, each at its kind's
    /// energy)? Each target is given the weapon Choose would, rounds spent as it goes.</summary>
    public static bool Enough(WorldState s, IEnumerable<(int Type, int Left)> open)
    {
        var rounds = Flown.ToDictionary(i => i, i => Rounds(s, i));
        foreach (var (type, left) in open.OrderByDescending(o => Energy(s, o.Type)))
            for (var k = 0; k < left; k++)
            {
                var e = Energy(s, type);
                var pick = Flown.Where(i => rounds[i] >= Hits(s, i, e)).OrderBy(i => Hits(s, i, e)).Cast<int?>().FirstOrDefault();
                if (pick is not { } p) return false;
                rounds[p] -= Hits(s, p, e);
            }
        return true;
    }

    /// <summary>A shot type's flight (its kind record): speed a tick (+$12; a helicopter adds none, $24C = 0 with d7 bit 11),
    /// the fall a tick (+$16 Weight: w / 2 = 0 -> $10 for 0, none for 1; else w - Settle takes it off y every tick), life in
    /// ticks (+$0E), the pitch trim (+$14 low byte, +$39: Vector turns by the pitch less it).</summary>
    public static (int Speed, int Fall, int Life, int Trim) Flight(WorldState s, int shotType)
    {
        var w = Kind(s, shotType, 0x16);
        var fall = w >> 1 == 0 ? (w == 0 ? 0x10 : 0) : w;
        return (Kind(s, shotType, 0x12), fall, Kind(s, shotType, 0x0E), Kind(s, shotType, 0x14) & 0xFF);
    }

    /// <summary>Would a shot fired now from `from` (its pitch +$7C and heading +$7E bytes, the weapon in hand +$26) meet `t`?
    /// It starts at the gun (Weapons.Fire $30D2: +$62, +$64 + +$7A up, +$66 from the shooter) and goes tick by tick
    /// (Movement.Vector's turn by pitch then heading, Settle's fall) until it is in t's box (half widths +$6E / +$72, from +$4C
    /// up 2 x +$70) or out of life. The ground never ends it: Settle puts a shot under it back on it ($2B24-$2B2C, the smooth
    /// ground for shots), so a shell fired level climbs a shore; a blocked square holds it where it was ($2B14): no hit. The
    /// bomb ($5A) only leaves more than $258 above the ground ($30C4).</summary>
    public static bool WouldHit(WorldState s, Entity from, Entity t) => FirstMet(s, from, t) == t;

    /// <summary>What a shot fired now meets first (WouldHit's flight): t, another thing in the way, or null (none, the ground, out of life).</summary>
    public static Entity? FirstMet(WorldState s, Entity from, Entity t)
    {
        var type = from.W(0x26);
        if (type <= 0) return null;
        if (type == 0x5A && from.W(0x4C) - from.W(0x74) <= 0x258) return null;
        var (speed, fall, life, trim) = Flight(s, type);
        if (!from.Bit(0x08, 11)) speed += from.W(0x3E);                       // $24C: the shooter's speed (none from a helicopter, d7 bit 11)
        var shot = new Entity();                                             // the step by the game's own tables (Movement.Vector, $2C00):
        shot.B[0x7C] = from.B[0x7C]; shot.B[0x39] = (byte)trim; shot.B[0x7E] = from.B[0x7E];   // its rounding turns a shell 45 degrees
        var (sx, sy, sz) = Movement.Vector(s.Trig, shot, (short)speed, s.StraightMoves);       // off by ~2 (17 east, 16 north a pass)
        double vx = sx, vz = sz, vy = sy - fall;
        double x = from.W(0x62), y = from.W(0x64) + from.W(0x4C) + from.W(0x7A), z = from.W(0x66);
        if (y <= 0) return null;                                             // $30E2: in the ground, no shot
        double tx = (short)(t.UW(0x4A) - from.UW(0x4A)), tz = (short)(t.UW(0x4E) - from.UW(0x4E));
        int bottom = t.W(0x4C), top = t.W(0x4C) + 2 * Math.Max((int)t.W(0x70), 16);
        var slot = Array.IndexOf(s.Entities.All, from);
        var owner = (short)(slot * EntityTable.Size + 0x250 + 0x806);       // the shot's +$2C (Weapons.Fire $3112)
        var inside = from.B[0x20] != 0;                                      // $313C: born inside, +$06 bit 5
        var box = ShotBox(s, type);                                          // the shot's own box (a bullet's is 0 x 248 x 144, BULLET BOX's 8 x 248 x 8)
        var stops = new List<Entity>();
        foreach (var e in s.Entities.All)
            if (e != from && e != t && e.Alive && !e.Flag(30) && !e.Flag(21) && e.Flag(5) == inside && !e.Flag(20) && e.W(0x2C) != owner
                && Dist(from, e) < 1600) stops.Add(e);
        for (var k = 0; k < life; k++)                                       // its life, no more (a floor of 64 said "hits" for bullets fired standing that die ~30 short - M2 11-08, GunShotProbe)
        {
            x += vx; y += vy; z += vz;
            var (ground, _, blocked) = s.Ground.Sample((ushort)(from.UW(0x4A) + (int)x), (ushort)(from.UW(0x4E) + (int)z), true);
            if (blocked) return null;
            if (y <= ground) y = ground;
            if (In(from, t, x, y, z, box)) return t;                         // the game's own test only (a looser one, +8 round its box, said "hits" for
                                                                             // bullets 55 off a tracer gun's 52 - M2 11-08, GunShotProbe)
            foreach (var e in stops) if (In(from, e, x, y, z, box)) return e;     // met first: it takes the shot
        }
        return null;
    }

    /// <summary>Can `from` see `t`? A straight line from his eyes (the top of his box) to t's middle, stopped by anything
    /// solid that stands still (no +$06 bit 27 mover: buildings, structures, rocks) - not by ghosts (bit 21), shots or blasts
    /// (bit 20), a building he is in (bit 30). The ground is not looked at.</summary>
    public static bool Sees(WorldState s, Entity from, Entity t) =>
        Clear(s, from, t, (short)(t.UW(0x4A) - from.UW(0x4A)), (short)(t.UW(0x4E) - from.UW(0x4E)), t.W(0x4C) + t.W(0x70));

    /// <summary>Could `from` see a spot - (x, z) at height y (a thing's middle as he last saw it there)? The same line as
    /// Sees: what stands in the way hides the spot, whatever is there now (Senses: a thing is "gone" only from a spot he
    /// can see - M3 in one game 11-09: two helicopters behind the HQ, marked gone, a 434 s way instead of a 72 s one).</summary>
    public static bool SeesSpot(WorldState s, Entity from, int x, int z, int y) =>
        Clear(s, from, null, (short)(x - from.UW(0x4A)), (short)(z - from.UW(0x4E)), y);

    static bool Clear(WorldState s, Entity from, Entity? t, double tx, double tz, double y1)
    {
        double y0 = from.W(0x4C) + 2 * from.W(0x70);
        var reach = (int)Math.Max(Math.Abs(tx), Math.Abs(tz));
        var walls = new List<Entity>();
        foreach (var e in s.Entities.All)
            if (e != from && e != t && e.Alive && !e.Flag(27) && !e.Flag(21) && !e.Flag(20) && !e.Flag(30) && Dist(from, e) <= reach + 512) walls.Add(e);
        if (walls.Count == 0) return true;
        var steps = Math.Max(1, (int)(Math.Max(Math.Abs(tx), Math.Abs(tz)) / 16));
        for (var k = 1; k < steps; k++)
        {
            double f = (double)k / steps;
            foreach (var e in walls) if (In(from, e, tx * f, y0 + (y1 - y0) * f, tz * f, (0, 0, 0))) return false;
        }
        return true;
    }

    /// <summary>A box of half sizes `b` at (x, y, z) - x and z from `from`, y its foot - touching e's, by Collisions' own test
    /// ($4448-$4498: each way within the two half sizes; up and down the middles within the two half heights)?</summary>
    static bool In(Entity from, Entity e, double x, double y, double z, (int W, int H, int D) b)
    {
        double ex = (short)(e.UW(0x4A) - from.UW(0x4A)), ez = (short)(e.UW(0x4E) - from.UW(0x4E));
        int eh = e.W(0x70);
        return Math.Abs(x - ex) <= Math.Abs((int)e.W(0x6E)) + b.W && Math.Abs(z - ez) <= Math.Abs((int)e.W(0x72)) + b.D
            && Math.Abs(y + b.H - (e.W(0x4C) + eh)) <= Math.Abs(eh) + b.H;
    }

    static int Dist(Entity a, Entity b) =>
        Math.Max(Math.Abs((int)(short)(a.UW(0x4A) - b.UW(0x4A))), Math.Abs((int)(short)(a.UW(0x4E) - b.UW(0x4E))));
}
