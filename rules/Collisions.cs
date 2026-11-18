namespace Hunter.Rules;

/// <summary>
/// $4408: objects bumping into each other, checked during the sort, before positions are saved
/// ($41CC writes +$50). Each live object against every one after it in the depth list ($4DEC):
/// boxes from +$6E/+$70/+$72 (half-sizes) overlap -> a response chosen by both flag longs.
/// The plain response puts a mover (bit 27) back where it was ($451E, bit 3 set). Touching an
/// empty vehicle gets the one on foot in (TouchVehicle $45D4); a mover ramming something or a
/// shot / blast hitting it costs both the other's hit points ($454E/$455C). Items ($4572 ->
/// $A240), "The X is occupied" ($45F0, a message) and doors ($458C, Buildings.cs).
/// See re/formats/movement.md section 4.
/// </summary>
public static class Collisions
{
    static bool Bit(int v, int n) => (v >> n & 1) != 0;

    /// <summary>The running game (touching items needs it); null in renderer-only tests.</summary>
    [ThreadStatic] static WorldState? _s;
    /// <summary>The two slots being answered (Respond's, for the ram in PutBack).</summary>
    [ThreadStatic] static (int A, int B) _pair;

    /// <summary>ENHANCEMENT (A500 PACE, WorldState.A500Pace): may these two slots hurt each other now - the first touch at once,
    /// then once every PaceBlanks blanks (the A500 checked a touch once a pass; here a pass is a blank). Always, when off.</summary>
    static bool Paced(int a, int b)
    {
        if (_s is not { A500Pace: true } s) return true;
        var key = a < b ? (a, b) : (b, a);
        if (s.PaceHurts.TryGetValue(key, out var at) && s.PaceBlank - at < WorldState.PaceBlanks) return false;
        s.PaceHurts[key] = s.PaceBlank;
        return true;
    }

    public static void Check(EntityTable t, List<int> order, WorldState? s = null)
    {
        _s = s;
        foreach (var i in order)                                              // ENHANCEMENT (Entity.InHand): out of his box, it is free
            if (t.All[i].InHand && (Shooter(t, t.All[i]) is not { } by || !Overlap(t.All[i], by))) t.All[i].InHand = false;
        for (var a = 0; a < order.Count; a++)                                 // $4408
        {
            var e4 = t.All[order[a]];
            if (Bit(e4.Flags, 30) || Bit(e4.Flags, 21)) continue;
            for (var b = a + 1; b < order.Count; b++)                         // $4432
            {
                var e3 = t.All[order[b]];
                if (Bit(e3.Flags, 30)) continue;
                if (!Overlap(e4, e3)) continue;
                if (e4.InHand && Shooter(t, e4) == e3 || e3.InHand && Shooter(t, e3) == e4) continue;   // just left his hand
                _pair = (order[a], order[b]);
                Respond(e4, order[a], e3, order[b]);
            }
        }
    }

    /// <summary>ENHANCEMENT, picture only: a shot that ends as a blast (the star, $61-$67 - Vehicles.End; re/formats/
    /// weapons.md 3b: $53 $54 $56-$5A $5C-$5E $6F-$71) or a blast itself; not the bullets ($52 $5B) or the mine layer $55.</summary>
    public static bool MakesFire(int type) => type is 0x53 or 0x54 or (>= 0x56 and <= 0x5A) or (>= 0x5C and <= 0x5E)
        or (>= 0x6F and <= 0x71) or (>= 0x61 and <= 0x67);

    // $4448-$4498: |dz| <= sz3 + sz4, |dx| <= sx3 + sx4, |(y4 + h4) - (y3 + h3)| <= h3 + h4 (words)
    static bool Overlap(Entity a4, Entity a3)
    {
        if (WorldState.AbsDist(_s, a3.UW(0x4E), a4.UW(0x4E)) > (short)(a3.W(0x72) + a4.W(0x72))) return false;
        if (WorldState.AbsDist(_s, a4.UW(0x4A), a3.UW(0x4A)) > (short)(a3.W(0x6E) + a4.W(0x6E))) return false;
        var d1 = (short)(a4.W(0x4C) + a4.W(0x70)) - (short)(a3.W(0x4C) + a3.W(0x70));
        return Abs(d1) <= (short)(a3.W(0x70) + a4.W(0x70));
    }

    static short Abs(int v) { var w = (short)v; return w < 0 ? (short)-w : w; }      // sub.w, bpl, neg.w

    // $449E
    static void Respond(Entity a4, int slot4, Entity a3, int slot3)
    {
        int d7 = a4.Flags, d6 = a3.Flags, d4 = d6 ^ d7, d5 = d6 | d7;
        if (Bit(d5, 21) || Bit(d4, 5)) return;
        if (Bit(d4, 20))                                                      // $454E: hurt by a shot or blast
        {
            if (!Paced(slot4, slot3)) return;                                 // A500 PACE: a pair's hurt once an A500 pass
            var owner = a4.W(0x2C);                                           // +$2C: who fired it
            if (owner == 0 || (owner ^ a3.W(0x2C)) != 0)                      // not by its own shot
            {
                {                                                             // ENHANCEMENT, picture only (EXPLOSIONS: PIECES): where the hurt came from
                    var (hurter, victim) = Bit(d7, 20) ? (a4, a3) : (a3, a4);
                    if (_s is { } sh && victim == sh.Entities.All[0]) Hurt(sh, hurter);   // the bot's death log
                    victim.BlastHit = MakesFire(hurter.W(4));
                    if (victim.BlastHit) { victim.BlastX = hurter.UW(0x4A); victim.BlastY = hurter.W(0x4C); victim.BlastZ = hurter.UW(0x4E); }
                }
                if (_s is { LivingWorld: true })                              // ENHANCEMENT (LIVING WORLD): who hurt whom, for the score
                {
                    var (hurter, hs, victim) = Bit(d7, 20) ? (a4, slot4, a3) : (a3, slot3, a4);
                    victim.HurtBy = LivingWorld.Hurter(_s, hurter, hs);
                }
                if (_s is { LimbDamage: true })                                // ENHANCEMENT (LIMB DAMAGE): a bullet's hit on a person's leg or arm does not kill
                {
                    var (shot, victim) = Bit(d7, 20) ? (a4, a3) : (a3, a4);
                    var limb = Limbs.Hit(shot, victim);
                    var before = victim.W(0x2A);
                    Damage(a4, a3);
                    if (limb != 0) Limbs.Keep(victim, limb, before);
                    return;
                }
                Damage(a4, a3);
            }
            return;
        }
        if (Bit(d5, 15))
        {
            if (Bit(d5, 28)) { Vehicles.Touch(a4, slot4, a3, slot3); return; } // $45D4 TouchVehicle
            if (Bit(d5, 31))
            {
                if (Bit(d5, 16))                                              // $4572: put back, then TouchItem
                {
                    PutBack(a4, a3, d4);
                    if (_s is { } s) Items.Touch(s, Bit(d7, 31) ? a3 : a4);
                    return;
                }
                if (Bit(d5, 6))                                               // $458C DoorBumpAndEnterRequest
                {
                    Bump(a4, a3, d4);
                    if (_s is { } sd) Buildings.DoorTest(sd, a4, slot4, a3, slot3, d7);
                    return;
                }
                Bump(a4, a3, d4);                                             // $44E0
                if (Bit(d5, 26) && _s is { } s2)                              // $44E2 bit 26 -> $45F0: "The X is occupied" ($B528)
                {
                    Messages.Show(s2, 0xB528, (Bit(d7, 31) ? a3 : a4).W(4));  // $45F0-$45FA: d0 = a4's type, a3's with d7 bit 31
                    s2.Channels.Play(2, s2.Entities);                         // $460C $B116
                }
                return;
            }
        }
        Bump(a4, a3, d4);
    }

    // $44EC: one on the ground and one not -> $4616 first; then $44FC
    static void Bump(Entity a4, Entity a3, int d4)
    {
        if ((a3.B[0x1E] ^ a4.B[0x1E]) != 0) Edge(a4, a3);
        PutBack(a4, a3, d4);
    }

    // $44FC
    static void PutBack(Entity a4, Entity a3, int d4)
    {
        if (!Bit(d4, 27) && Bit(d4, 26))
        {
            var mover = Bit(a4.Flags, 26) ? a4 : a3;                          // $4508
            if (mover.W(0x3E) != 0)                                           // a mover rams something
            {
                if (!Paced(_pair.A, _pair.B)) return;                         // A500 PACE: a pair's hurt once an A500 pass
                if (_s is { } sr && (a4 == sr.Entities.All[0] || a3 == sr.Entities.All[0]) && mover != sr.Entities.All[0]) Hurt(sr, mover);
                Damage(a4, a3);
                return;
            }
        }
        foreach (var e in new[] { a4, a3 })                                   // $4518 / $4532
        {
            if (!e.Flag(27)) continue;
            e.SetW(0x4A, e.W(0x50)); e.SetW(0x4C, e.W(0x52)); e.SetW(0x4E, e.W(0x54));
            e.SetL(6, e.Flags | 1 << 3);
        }
    }

    /// <summary>A shot's shooter from its +$2C (slot x $A0 + $A56, Weapons.Fire), null if none or gone.</summary>
    static Entity? Shooter(EntityTable t, Entity shot)
    {
        var slot = (shot.UW(0x2C) - 0xA56) / EntityTable.Size;
        return shot.W(0x2C) != 0 && slot >= 0 && slot < t.Count && t.All[slot].Alive ? t.All[slot] : null;
    }

    /// <summary>The bot's death log (WorldState.LastHurtBy): the hurter's type, and a shot's shooter from its +$2C (the record's
    /// address low word, slot x $A0 + $A56 - Weapons.Fire).</summary>
    static void Hurt(WorldState s, Entity hurter)
    {
        s.LastHurtBy = hurter.W(4);
        s.LastHurtShooter = Shooter(s.Entities, hurter) is { } by ? by.W(4) : -1;
    }

    // $455C: each loses the other's hit points (+$2A), unless one is already ending
    static void Damage(Entity a4, Entity a3)
    {
        short d0 = a4.W(0x2A), d1 = a3.W(0x2A);
        if (d0 < 0 || d1 < 0) return;
        a4.SetW(0x2A, (short)(d0 - d1));
        a3.SetW(0x2A, (short)(d1 - d0));
    }

    // $4616: the airborne one (A1) gets its old x or z moved to the edge of the other's box,
    // on the axis it was further away on, so putting it back leaves it beside the object.
    static void Edge(Entity a4, Entity a3)
    {
        Entity a0 = a3, a1 = a4;
        if (a4.B[0x1E] != 0) (a0, a1) = (a4, a3);                             // $461A tst.b d0 = +$1E of A4
        int d2 = (short)(a0.W(0x6E) + a1.W(0x6E)), d3 = (short)(a0.W(0x72) + a1.W(0x72));
        var d0 = (short)(a0.W(0x4A) - a1.W(0x50));
        if (d0 < 0) { d0 = (short)-d0; d2 = (short)-d2; }
        var d1 = (short)(a0.W(0x4E) - a1.W(0x54));
        if (d1 < 0) { d1 = (short)-d1; d3 = (short)-d3; }
        if (d1 > d0) a1.SetW(0x54, (short)(-d3 + a0.W(0x4E)));               // $465C
        else a1.SetW(0x50, (short)(-d2 + a0.W(0x4A)));                        // $4650
    }
}
