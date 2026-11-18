namespace Hunter.Rules;

/// <summary>
/// What everyone but the player does (re/formats/people.md): PlanEntity ($270E, $2740) picks a
/// plan from the record's behaviour code +$0C into d6 (+$0A) and a target (+$16, always the
/// player's slot 0, or a wander point +$44 with d6 bit 14); SteerToTarget ($2804) turns toward
/// (or away from) the target each pass, sets the speed, and fires when in range.
/// </summary>
public static class Behaviour
{
    static bool Bit(int v, int n) => (v >> n & 1) != 0;

    /// <summary>PlanEntity ($270E): d7 bit 28 off, bit 0 on; writes +$16, +$0A, +$06. Returns d7.</summary>
    public static int Plan(WorldState s, Entity e, int d7)
    {
        d7 &= ~(1 << 28);
        d7 |= 1;
        var d4 = 0;
        var d6 = (int)e.UW(0x0C);
        if (!Bit(d6, 12) || (s.Flags0A4 & 2) != 0)                           // $271E: bit 12 waits for the alarm
        {
            d6 &= ~(1 << 12);
            Choose(s, e, ref d6, ref d4, ref d7);                            // $2740
        }
        e.SetW(0x16, d4);
        e.SetL(0x0A, d6);
        e.SetL(6, d7);
        return d7;
    }

    // $2740: d0 = +$0F & $219 (does the player's status concern it), d1 = +$0E - the player's +$0E
    static void Choose(WorldState s, Entity e, ref int d6, ref int d4, ref int d7)
    {
        var slot = TargetPlayer(s, e);
        if (s.LivingWorld) slot = LivingWorld.Target(s, e, slot);           // ENHANCEMENT (LIVING WORLD): maybe another thing
        var isPlayer = slot < s.Players;
        d4 = slot * EntityTable.Size;                                        // $2716 clr.w d4, $2732 base+offset -> +$16: the target's byte offset ($2834 divides it back; slot 0 = 0, the original's only value)
        var player = s.Entities.All[slot];
        var d0 = (byte)(e.B[0x0F] & (isPlayer ? s.PlayerAt(slot).Status219 : LivingWorld.Status(player)));
        var d1 = (sbyte)(byte)(e.B[0x0E] - player.B[0x0E]);
        if ((short)d6 < 0)                                                   // $2752 bit 15
        {
            if ((sbyte)e.B[0x21] < 0) Nothing(ref d6, ref d4, ref d7);       // $27C8
            return;
        }
        if (!isPlayer && (Bit(d6, 8) || Bit(d6, 9)))                         // LIVING WORLD: the player's gates (in a building,
        {                                                                    // the disguise, the alarm) are the player's alone
            Concerned(e, d0, d1, ref d6, ref d7);
            return;
        }
        if (Bit(d6, 8))                                                      // $277A
        {
            if ((player.B[0x08] & 1) != 0) { Moving(e, ref d6, ref d7); return; }
            if ((player.B[0x08] & 0x80) != 0 && (s.Flags0A4 & 0x20) != 0) { Moving(e, ref d6, ref d7); return; }
            Concerned(e, d0, d1, ref d6, ref d7);
            return;
        }
        if (Bit(d6, 9))                                                      // $27A4
        {
            if ((s.Flags0A4 & 1) != 0 || (s.Flags0A4 & 0x20) != 0) { Concerned(e, d0, d1, ref d6, ref d7); return; }
            Nothing(ref d6, ref d4, ref d7);
            return;
        }
        if (Bit(d6, 13)) { if (d0 == 0) d7 &= ~1; return; }                  // $2774
        if (Bit(d6, 11)) { Concerned(e, d0, d1, ref d6, ref d7); return; }   // $2792
        if (Bit(d6, 14)) { Wander(s, e); return; }                           // $27D0
        Nothing(ref d6, ref d4, ref d7);                                     // $27B4
    }

    /// <summary>The player slot an animal/soldier plans against ($2732 writes d4). ENHANCEMENT,
    /// NOT THE ORIGINAL: one player is always slot 0 - even when slot 0 holds another record
    /// after the player died, the original's quirk ($2732 writes slot 0 unconditionally). More
    /// players: the nearest LIVE player (a player slot whose record is alive and not the grave
    /// type $7C, Play.cs $33EC) by FindNearestInRange's measure ($340E/$3454: max of |dx| and
    /// |dz| as signed words of +$4A/+$4E, the true difference with Maths32 - WorldState.Dist);
    /// ties go to the lower slot; no live player: slot 0,
    /// today's behaviour. Once per entity per plan, no new per-pass lists.</summary>
    static int TargetPlayer(WorldState s, Entity e)
    {
        if (s.Players <= 1) return 0;
        var best = 0;
        var bestD = int.MaxValue;
        for (var n = 0; n < s.Players; n++)
        {
            var p = s.Entities.All[n];
            if (!p.Alive || p.W(4) == 0x7C) continue;
            var dx = WorldState.Dist(s, p.UW(0x4A), e.UW(0x4A));
            var dz = WorldState.Dist(s, p.UW(0x4E), e.UW(0x4E));
            if (dx < 0) dx = s.Maths32 ? -dx : (short)-dx;               // off: the word's neg keeps -32768 negative, as the original
            if (dz < 0) dz = s.Maths32 ? -dz : (short)-dz;
            var d = dx > dz ? dx : dz;
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    // $2792: not concerned -> $27C0; concerned and weaker (d1 < 0) -> flee (bit 13)
    static void Concerned(Entity e, byte d0, sbyte d1, ref int d6, ref int d7)
    {
        d6 &= ~(1 << 13);
        if (d0 == 0) { Moving(e, ref d6, ref d7); return; }
        if (d1 >= 0) return;
        d6 |= 1 << 13;
    }

    // $27C0: moving -> flee, else stop planning
    static void Moving(Entity e, ref int d6, ref int d7)
    {
        if (e.W(0x3E) != 0) d6 |= 1 << 13;
        else d7 &= ~1;
    }

    // $27B4
    static void Nothing(ref int d6, ref int d4, ref int d7) { d6 = 0; d4 = 0; d7 &= ~1; }

    // $27D0: a point beside itself from the word at $806 + 6 (the program's $0400)
    static void Wander(WorldState s, Entity e)
    {
        var r = (short)(s.Data.Main[0x80C - 0x800] << 8 | s.Data.Main[0x80D - 0x800]);
        var x = (short)(e.W(0x4A) + (r >> 4));
        var ror = (short)((ushort)r >> 4 | (ushort)r << 12);
        var z = (short)(e.W(0x4E) + (ror >> 4));
        e.SetW(0x44, x); e.SetW(0x46, e.W(0x4C)); e.SetW(0x48, z);
    }

    /// <summary>SteerToTarget ($2804) for UpdateEntity's d7 bit 0. Returns d6 (+$0A after, $2672).</summary>
    public static int Steer(WorldState s, Entity e, ref int d7, int ticks)
    {
        var d6 = e.L(0x0A);
        if (d6 == 0) { d7 &= ~1; return d6; }                                // $27F2
        if (d6 < 0) { d6 &= int.MaxValue; e.B[0x1F] = 0x7F; return d6; }     // $27F8: get out of the vehicle
        if (Bit(d6, 12)) { d7 = Plan(s, e, d7); return e.L(0x0A); }          // $2810
        if (Bit(d7, 3))                                                      // $2814: bumped: turn aside
        {
            d7 &= ~(1 << 3);
            if (s.PaceTick) e.B[0x7E] = (byte)(e.B[0x7E] + (byte)((e.B[0x18] | 1) << 2));   // A500 PACE: a turn an A500 pass
            return d6;
        }
        // ENHANCEMENT (more objects, owner 2026-11-01): a thing awake only in the bigger window stands until it is
        // inside the original's - so nothing comes for the player from farther than the original wakes things
        if (s.MoreObjects && e.W(2) != 0 && !Bit(d6, 14) && !Objects.InWindow(s, e, false))   // +2: a map thing (shots have none)
        {
            e.B[0x18] = 0;
            e.SetW(0x3E, 0);
            e.B[0x1D] = 0;                                                   // no firing on from out there (the original writes it back instead)
            e.HeldFar = true;                                                // woken afresh on coming in (StartState.SpawnNearCamera)
            return d6 & ~(1 << 30);
        }
        int d3, d5;                                                            // differences of positions: int, Dist's true width with Maths32
        short d4;                                                              // a height difference: a word, as the original
        Entity t;
        if (Bit(d6, 14))
        {
            (d3, d4, d5) = (e.W(0x44), e.W(0x46), e.W(0x48));
            t = s.Entities.All[0];                                           // $286A reads slot 0's height even on a wander target
        }
        else
        {
            var ts = e.UW(0x16) / EntityTable.Size;
            t = s.Entities.All[ts];                                          // $2834. unsigned: the original's adda.w sign-extends - fine to slot 204; more_objects goes to 393
            // ENHANCEMENT (LIVING WORLD): a thing gone, or its slot now another's that is no opponent - choose again
            if (s.LivingWorld && ts >= s.Players && (!t.Alive || (!LivingWorld.Opponent(s, e, t) && !LivingWorld.Shot(e))))
            {
                d7 = Plan(s, e, d7);
                return e.L(0x0A);
            }
            (d3, d4, d5) = (t.UW(0x4A), t.W(0x4C), t.UW(0x4E));                // UW: Dist subtracts the unsigned positions
        }
        if (Bit(d6, 0))                                                      // $2846: keep above the target
        {
            d4 = (short)(d4 - e.W(0x4C));
            if (d4 >= 0) d4 = 0;
            d4 = (short)-d4;
            var p = (byte)(e.B[0x7C] - (byte)ticks);
            if ((sbyte)p < 0) p = 0;
            var lift = e.W(0x12);
            if (d4 >= lift)
            {
                e.SetW(0x4C, (short)(lift + (s.Players <= 1 && !s.LivingWorld ? s.Entities.All[0] : t).W(0x4C)));   // $286A-$286E: the plan's target player's height (slot 0 with one player); LIVING WORLD: the target's
                p = (byte)(p + (byte)ticks);
            }
            e.B[0x7C] = p;
        }
        int w = 0;                                                           // d0.w: sign bits, then index
        d3 = Bit(d6, 14) ? (short)(d3 - e.W(0x4A)) : WorldState.Dist(s, d3, e.UW(0x4A));   // a wander point is its own relative word; a target a true position
        w = w & 0xFF00 | (d3 < 0 ? 0xFF : 0);
        if (d3 < 0) d3 = s.Maths32 ? -d3 : (short)-d3;                       // off: the word's neg, as the original
        w = w * 2 & 0xFFFF;
        d5 = Bit(d6, 14) ? (short)(d5 - e.W(0x4E)) : WorldState.Dist(s, d5, e.UW(0x4E));
        w = w & 0xFF00 | (d5 < 0 ? 0xFF : 0);
        if (d5 < 0) d5 = s.Maths32 ? -d5 : (short)-d5;
        w = w * 2 & 0xFFFF;
        byte turn = 0;
        if (Bit(d6, 4) || !Bit(d6, 30))                                      // $2896
        {
            int big = d3, small = d5;
            w = w & 0xFF00 | (small < big ? 0xFF : 0);                       // $28A6 cmp, slt
            if (small > big) (big, small) = (small, big);                    // ble: no exg
            w = w * 2 & 0xFFFF;
            big = s.Maths32 ? big >> 1 : (short)((ushort)big >> 1);
            w = w & 0xFF00 | (small < big ? 0xFF : 0);
            w = (ushort)w >> 7;
            var want = (byte)(s.Data.Main[0x28FE - 0x800 + w] - 4);          // $28B8: heading toward it
            e.B[0x1A] = want;
            var d1 = e.B[0x1B];
            if (d1 != 0 && s.PaceTick) e.B[0x1B] = (byte)(e.B[0x1B] - 2);   // A500 PACE: a step an A500 pass
            d1 = (byte)((d1 + e.B[0x7E]) & 0xFC);
            var diff = (byte)(want - d1);
            if (diff != 0) turn = (byte)(((sbyte)(byte)(diff - 0x7C) >= 0 ? 0xFF : 0) | 1);
        }
        e.B[0x18] = turn;                                                    // $28E4
        d6 &= ~(1 << 30);
        if (d3 <= e.W(0x10) && d5 <= e.W(0x14)) d6 |= 1 << 30;               // $28EC: within range
        if (s.LivingWorld && !Bit(d6, 14) && LivingWorld.Stares(s, e, t))   // ENHANCEMENT (LIVING WORLD): out of its reach -
        {                                                                    // it turns to watch and stands (owner 2026-11-01)
            e.SetW(0x3E, 0);
            return d6;
        }
        if (Bit(d6, 13))                                                     // $2942: flee
        {
            d6 ^= 1 << 30;
            e.B[0x18] = (byte)-(sbyte)e.B[0x18];
            if (Bit(d6, 30) && !Bit(d6, 8) && Bit(d6, 5)) e.SetW(0x3E, 0);
            else e.SetW(0x3E, e.W(0x36));
            return d6;
        }
        if (!Bit(d6, 30)) { e.SetW(0x3E, e.W(0x36)); return d6; }           // $295C: go for it
        if (Bit(d6, 3) && (sbyte)e.B[0x2E] < 0)                              // $291A: fire when loaded
        {
            var rounds = e.W(0x32);
            e.B[0x1D] = (byte)(rounds > 0 ? 0xFF : 0);
            if (rounds <= 0) d6 |= 1 << 13;                                  // out of rounds: flee
            // ENHANCEMENT (LIMB DAMAGE + LIVING WORLD, owner): both arms hurt, it cannot fire (FireTimer) - so it flees as
            // one out of rounds does
            else if (s.LimbDamage && s.LivingWorld && Limbs.HurtArms(e) == 2) d6 |= 1 << 13;
        }
        if (Bit(d6, 5)) e.SetW(0x3E, 0);                                     // $2934: stop there
        return d6;
    }

    /// <summary>ReplanEntities ($26C0): every live record with +$0C and +$0F set plans again.</summary>
    public static void Replan(WorldState s)
    {
        foreach (var e in s.Entities.All)
        {
            if (!e.Alive || e.B[0x0C] == 0 || e.B[0x0F] == 0) continue;
            Plan(s, e, e.Flags);
        }
    }

    /// <summary>SwapModelsAndPlan ($26EA): the other model, the behaviour byte of its variant, plan.</summary>
    public static int SwapModelsAndPlan(WorldState s, Entity e, int d7)
    {
        var m = e.W(0x9E); e.SetW(0x9E, e.W(0x24)); e.SetW(0x24, m);
        e.B[0x0C] = s.Data.Main[0xDA76 - 0x800 + e.B[1] * 0x34 + 0x1A];
        return Plan(s, e, d7);
    }
}
