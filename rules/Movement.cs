namespace Hunter.Rules;

/// <summary>
/// One pass of an entity: UpdateEntity ($2642) and the movement it runs (re/formats/movement.md
/// section 2, vehicles.md section 4). Paths read as belonging to later milestones throw, so a
/// missed path fails loudly. CrashDestroyEntity ($2C92) pops MoveByModeBurnFuel's return: UpdateEntity's tail
/// then goes on with whatever record EntityEnds left (the original's A4 / d7).
/// </summary>
public static class Movement
{
    static bool Bit(int d7, int n) => (d7 >> n & 1) != 0;

    /// <summary>UpdateEntity ($2642) for the record in this slot; +$06 = d7 afterwards ($2620,
    /// written to the record A4 points at by then).</summary>
    public static void Update(WorldState s, Entity e, int ticks, int slot = 0)
    {
        var (rec, d7) = Pass(s, e, slot, e.Flags, ticks);
        rec.SetL(6, d7);
    }

    static (Entity, int) Pass(WorldState s, Entity a4, int slot, int d7, int ticks)
    {
        if (s.PlotArmour && slot == 0 && a4.W(0x2A) < 0) { a4.SetW(0x2A, 1); s.ArmourSaves.Add("hit"); }  // ENHANCEMENT (PLOT ARMOUR, bot films only): he lives
        if (a4.W(0x2A) < 0) return s.PaceTick ? Ends(s, a4, slot, d7) : (a4, d7);   // $2646 (A500 PACE: it stands till an A500 pass)
        if (a4.B[0x1F] != 0) return Vehicles.EnterOrLeaveVehicle(s, a4, slot, d7); // $264E
        if (Bit(d7, 17))                                                     // $2652: a timed record
        {
            var left = (short)(a4.W(0x30) - ticks);
            a4.SetW(0x30, left);
            if (left < 0) return Ends(s, a4, slot, d7);
        }
        var (rec, recSlot, nd7, ended, noTail) = MoveByModeBurnFuel(s, a4, slot, d7, ticks);     // $2664
        a4 = rec; d7 = nd7;
        if (ended && noTail) return (a4, d7);                                // a crash in a fall reached by a branch (Direct): no steer, sound or fire this pass
        if (Bit(d7, 0)) a4.SetL(0x0A, Behaviour.Steer(s, a4, ref d7, ticks));   // $266E, $2672
        if (Bit(d7, 19)) s.Channels.UpdateEntity(a4, ref d7, recSlot, s.Entities);   // $267C
        FireTimer(s, a4, recSlot, ref d7, ticks);                            // $2680
        return (a4, d7);
    }

    // EntityEnds: a landed parachutist becomes its soldier (+$28); the player's death (+$28 =
    // $60, the explosion) goes on to SetUpDeath $2EE0 (Vehicles.End)
    static (Entity, int) Ends(WorldState s, Entity e, int slot, int d7) => Vehicles.End(s, e, slot, d7);

    // $2680-$26BE: +$26 shots allowed, +$2E/+$2F reload timer, +$32 rounds
    static void FireTimer(WorldState s, Entity e, int slot, ref int d7, int ticks)
    {
        if (e.W(0x26) <= 0) return;
        if ((sbyte)e.B[0x2E] >= 0) { e.B[0x2E] = (byte)(e.B[0x2E] - ticks); return; }   // $268E sub.b
        if (e.B[0x1D] == 0) return;
        var arms = s.LimbDamage ? Limbs.HurtArms(e) : 0;                     // ENHANCEMENT (LIMB DAMAGE): see Limbs.OneArmReload
        if (arms == 2) { e.B[0x1D] = 0; return; }                            // both arms hurt: no shot, the round stays
        e.B[0x2E] = arms == 1 ? Limbs.Reload(e.B[0x2F]) : e.B[0x2F];         // $269E
        if (e.Bit(0x0D, 1)) { s.Channels.StartEntity(e, ref d7, slot, s.Entities); return; }   // $26AA: a cry, not a shot
        var rounds = (short)(e.W(0x32) - 1);
        e.SetW(0x32, rounds);
        if (rounds >= 0)                                                     // $26B2 -> LaunchProjectile $30BE
        {
            var shot = Weapons.Fire(s, e, slot, d7);
            if (s.AimReticle) Aim.Redirect(s, e, shot, slot, d7);            // ENHANCEMENT (AIMING: RETICLE): the shot to the point under it
            return;
        }
        e.SetW(0x32, 0);
        e.SetW(0x26, 0);
    }

    /// <summary>The record and d7 after a movement step; Ended = CrashDestroyEntity happened (the rest
    /// of the mode was skipped and the record may be another one).</summary>
    readonly record struct Step(Entity Rec, int Slot, int D7, bool Ended, bool NoTail = false);

    /// <summary>A FallIfAirborne MoveByModeBurnFuel reaches by a branch, not a bsr ($2968, $29A2 / $29AC, $29F6): the only
    /// return address left is UpdateEntity's, so a crash in it (CrashDestroyEntity's addq #4,SP, $2C92) pops that one and
    /// UpdateEntity's tail - steer, sound, fire timer ($2668-$26BE) - is skipped for the pass. Every other crash path pops
    /// a bsr inside the mode and returns to $2668 (the tail runs).</summary>
    static Step Direct(Step st) => st.Ended ? st with { NoTail = true } : st;

    // MoveByModeBurnFuel ($2964)
    static Step MoveByModeBurnFuel(WorldState s, Entity e, int slot, int d7, int ticks)
    {
        if (!Bit(d7, 27)) return Direct(FallIfAirborne(s, e, slot, d7, ticks));   // $2968: buildings, items: gravity only
        if ((d7 & 0x8000) != 0) return MoveAndTurn(s, e, slot, d7, ticks);   // $296E on foot
        if (Bit(d7, 26) && e.W(0x3E) != 0)                                   // $2972 fuel
        {
            var use = Bit(d7, 4) ? ticks * 2 : ticks;
            var fuel = (short)(e.W(0x34) - use);
            e.SetW(0x34, fuel);
            if (fuel <= 0)
            {
                e.SetW(0x34, 0);
                var sp = (short)(e.W(0x3E) - (s.PaceTick ? 1 : 0));        // A500 PACE: a step an A500 pass
                e.SetW(0x3E, sp);
                if (sp <= 0)
                {
                    e.SetW(0x3E, 0);
                    if (e.W(0x0C) != 0) e.B[0x0A] |= 0x80;                   // $29A6 bset #31 (byte +$0A bit 7)
                    return Direct(FallIfAirborne(s, e, slot, d7, ticks));   // $29A2 / $29AC bra
                }
            }
        }
        var w = d7 << 1;                                                     // $29B0: bits 14..9 in turn
        if ((w & 0x8000) != 0)                                               // 14: land, no turning when stopped
            return e.W(0x3E) == 0 ? Direct(FallIfAirborne(s, e, slot, d7, ticks)) : MoveAndTurn(s, e, slot, d7, ticks);   // $29F6 beq.w
        w <<= 1; if ((w & 0x8000) != 0) return MoveAndTurn(s, e, slot, d7, ticks);   // 13
        w <<= 1;
        if ((w & 0x8000) != 0)                                               // 12: $2A16
        {
            var st = MoveVelocityOrSpeed(s, e, slot, d7, ticks);
            if (st.Ended) return st;
            var d = st.D7;
            var (p, h) = TurnPitchDeltas(s, e, ref d, ticks);
            e.SetW(0x7C, p); e.SetW(0x7E, h); FineFromWords(e);
            return st with { D7 = d };
        }
        w <<= 1;
        if ((w & 0x8000) != 0)                                               // 11: $2A24 helicopters
        {
            var st = MoveAndSettleOnGround(s, e, slot, d7, ticks);
            if (st.Ended) return st;
            var d = st.D7;
            if (e.B[0x1E] != 0)
            {
                e.SetW(0x4A, e.W(0x50)); e.SetW(0x4E, e.W(0x54));
                if (e.W(0x7C) != 0) e.SetW(0x7C, e.W(0x7C) >> 1);            // asr
                FineFromWords(e);
                return st;
            }
            var (p, h) = TurnPitchDeltas(s, e, ref d, ticks);
            e.SetW(0x7C, p); e.SetW(0x7E, h); FineFromWords(e);
            return st with { D7 = d };
        }
        w <<= 1; if ((w & 0x8000) != 0) return MoveAndTurn(s, e, slot, d7, ticks);   // 10: boats
        w <<= 1;
        if ((w & 0x8000) != 0)                                               // 9: $2A8A falling
        {
            var st = MoveAndSettleOnGround(s, e, slot, d7, ticks);
            if (st.Ended) return st;
            var d = st.D7;
            if (e.B[0x1E] != 0)
            {
                e.SetW(0x28, e.B[1] + 0x1E);
                e.B[0x2A] = 0xFF;                                            // st +$2A
                return st;
            }
            var (p, h) = TurnPitchDeltas(s, e, ref d, ticks);
            if (p >= -0x901 && p <= 0xA00) e.SetW(0x7C, p);
            e.SetW(0x7E, h); FineFromWords(e);
            return st with { D7 = d };
        }
        if ((d7 & 0x1FF) == 0) return new Step(e, slot, d7, false);         // $29CA: no bit 8-0 left
        var f = FallIfAirborne(s, e, slot, d7, ticks);                       // $29CE
        if (!f.Ended) e.B[0x84] = e.B[0x1A];
        return f;
    }

    // MoveAndTurn ($29FA)
    static Step MoveAndTurn(WorldState s, Entity e, int slot, int d7, int ticks)
    {
        var st = MoveVelocityOrSpeed(s, e, slot, d7, ticks);
        if (st.Ended) return st;
        if (Aim.Active(s, slot, d7)) { Aim.Turn(s, e, ticks); return st; }   // ENHANCEMENT (AIMING: RETICLE): the soldier turns to the reticle, not by the keys
        var turn = (sbyte)e.B[0x18];                                         // $29FE
        if (turn == 0) return st;
        var shift = e.W(0x3C) & 63;
        if (slot == 0 && s.AnalogLean != 0 && (d7 & 0x8000) == 0 && Math.Sign(s.AnalogLean) == Math.Sign(turn))
        {                                                                    // ENHANCEMENT (AnalogSteering): the lean's share of the full step
            var full = shift >= 16 ? 0 : 1 << shift;                         // the byte's whole step; exact for shifts of 4 and up, else toward 0
            e.SetW(0x7E, e.UW(0x7E) + s.AnalogLean * full / 16 * ticks);     // lean 16 = the original's step
            e.HeadingFine = e.UW(0x7E);
            return st;
        }
        var t = shift >= 16 ? 0 : ((short)turn << shift) & 0xFFFF;           // ext.w, lsl.w
        e.SetW(0x7E, e.UW(0x7E) + (int)((uint)t * (uint)ticks));             // mulu.w, add.w
        e.HeadingFine = e.UW(0x7E);                                          // FINE ANGLES: the word is the turn's own fine heading
        return st;
    }

    /// <summary>FINE ANGLES (Entity.PitchFine): the angle words as the original keeps them whole here (a flier's pitch and
    /// heading, turned by a fraction of a step a pass) - the picture's fine angles. Nothing in play reads them.</summary>
    static void FineFromWords(Entity e) { e.PitchFine = e.UW(0x7C); e.HeadingFine = e.UW(0x7E); }

    // TurnPitchDeltas ($2A50): new pitch / heading words (not stored here)
    static (short Pitch, short Heading) TurnPitchDeltas(WorldState s, Entity e, ref int d7, int ticks)
    {
        if (Bit(d7, 3))
        {
            d7 &= ~(1 << 3);
            var sp = (short)(e.W(0x3E) - (s.PaceTick ? 1 : 0));            // A500 PACE: a step an A500 pass
            e.SetW(0x3E, sp < 0 ? 0 : sp);
        }
        var shift = e.W(0x3C) & 63;
        int Scaled(sbyte v) => v == 0 ? 0 : (int)((uint)((shift >= 16 ? 0 : ((short)v << shift)) & 0xFFFF) * (uint)ticks);
        var d0 = Scaled((sbyte)e.B[0x19]);
        var d1 = Scaled((sbyte)e.B[0x18]);
        return ((short)(d0 + e.W(0x7C)), (short)(d1 + e.W(0x7E)));
    }

    // FallIfAirborne ($2ABE)
    static Step FallIfAirborne(WorldState s, Entity e, int slot, int d7, int ticks) =>
        e.B[0x1E] != 0 ? new Step(e, slot, d7, false) : Settle(s, e, slot, d7, 0, 0, 0, ticks);

    // MoveVelocityOrSpeed ($2ACE)
    static Step MoveVelocityOrSpeed(WorldState s, Entity e, int slot, int d7, int ticks)
    {
        if (!Bit(d7, 24)) return MoveAndSettleOnGround(s, e, slot, d7, ticks);
        short Sc(int o) => (short)((uint)e.UW(o) * (uint)ticks);
        return Settle(s, e, slot, d7, Sc(0x5C), Sc(0x5E), Sc(0x60), ticks);
    }

    // MoveAndSettleOnGround ($2AE6): move along heading/pitch by speed * ticks
    static Step MoveAndSettleOnGround(WorldState s, Entity e, int slot, int d7, int ticks)
    {
        var dist = (short)((uint)e.UW(0x3E) * (uint)ticks);                 // mulu.w; d0.w used
        if (s.LimbDamage) dist = Limbs.Pace(e, dist);                        // ENHANCEMENT (LIMB DAMAGE): a hurt leg limps
        var (x, y, z) = s.FinePositions ? FineStep(s, e, dist) : Vector(s.Trig, e, dist, s.StraightMoves);
        var step = Settle(s, e, slot, d7, x, y, z, ticks);
        if (s.FinePositions) e.FracAt = ((short)e.W(0x4A), (short)e.W(0x4C), (short)e.W(0x4E));   // the words this fraction is above
        return step;
    }

    /// <summary>ENHANCEMENT (FINE POSITIONS, WorldState.FinePositions): Vector's step from the same sinfile tables, the shift's
    /// dropped fraction kept and added to the entity's carried one; the whole units go, the rest is carried. Moved from
    /// anywhere else since (its words not FracAt), the carry starts again at 0.</summary>
    static (short X, short Y, short Z) FineStep(WorldState s, Entity e, short dist)
    {
        var (fx, fy, fz) = VectorExact(s.Trig, (byte)(e.B[0x7C] - e.B[0x39]), e.B[0x7E], dist);
        if (e.FracAt != ((short)e.W(0x4A), (short)e.W(0x4C), (short)e.W(0x4E))) e.FracX = e.FracY = e.FracZ = 0;
        static short Whole(double v, ref double carry) { v += carry; var w = Math.Floor(v); carry = v - w; return (short)w; }
        return (Whole(fx, ref e.FracX), Whole(fy, ref e.FracY), Whole(fz, ref e.FracZ));
    }

    /// <summary>Vector without the shift's rounding: each product over 2^exponent as it is.</summary>
    public static (double X, double Y, double Z) VectorExact(Trig trig, byte p, byte h, short d)
    {
        double x = 0, y = 0, z = d;
        if (p != 0)
        {
            var (se, sm) = trig.Sin(p * 4);
            var (ce, cm) = trig.Cos(p * 4);
            double S = sm / Math.Pow(2, se), C = cm / Math.Pow(2, ce);
            (z, y) = (C * z + S * y, C * y - S * z);
        }
        if (h != 0)
        {
            var (se, sm) = trig.Sin(h * 4);
            var (ce, cm) = trig.Cos(h * 4);
            double S = sm / Math.Pow(2, se), C = cm / Math.Pow(2, ce);
            (x, z) = (C * x + S * z, C * z - S * x);
        }
        return (x, y, z);
    }

    // $2AF2: gravity, new position, ground, water and shore, landing
    static Step Settle(WorldState s, Entity e, int slot, int d7, short x, short y, short z, int ticks)
    {
        y = (short)(y - (short)M68.Muls(e.W(0x3A), ticks));                 // gravity
        x = (short)(x + e.W(0x4A)); y = (short)(y + e.W(0x4C)); z = (short)(z + e.W(0x4E));
        e.SetW(0x4A, x); e.SetW(0x4C, y); e.SetW(0x4E, z);
        var (h, land, blocked) = s.Ground.Sample((ushort)x, (ushort)z, Ground.IsShot(e));   // $2B0E (FINER GROUND: shots the smooth ground)
        if (blocked) { e.SetW(0x4A, e.W(0x50)); e.SetW(0x4E, e.W(0x54)); }  // $2B14
        e.SetW(0x74, h);                                                     // $2B20
        var onGround = h >= e.W(0x4C);                                       // $2B24 cmp, sge
        if (onGround)
        {
            e.SetW(0x4C, h);                                                 // $2B2C
            if (Bit(d7, 27))
            {
                var waterGoing = Bit(d7, 10);
                if (waterGoing == land)                                      // $2B36-$2B66: on the wrong side of the shore
                {
                    if (Bit(d7, 1)) Switch(s, e, ref d7);                    // $2BAC
                    else if (!waterGoing && Bit(d7, 11))
                    {
                        if (Bit(d7, 26))
                        {
                            var (r, d) = Vehicles.CrashDestroyEntity(s, e, slot, d7);   // $2B58
                            return new Step(r, slot, d, true);
                        }
                    }
                    else
                    {
                        e.SetW(0x4A, e.W(0x50)); e.SetW(0x4C, e.W(0x52)); e.SetW(0x4E, e.W(0x54));   // $2B6C
                        d7 |= 1 << 3;
                    }
                }
            }
        }
        var drop = (short)(e.W(0x52) - e.W(0x4C));                           // $2B7C
        e.SetW(0x76, drop);
        if (!onGround) { e.B[0x1E] = 0; return new Step(e, slot, d7, false); }   // $2B96
        var was = e.B[0x1E] & 0x80;
        e.B[0x1E] |= 0x80;                                                   // $2B8C bset #7
        if (s.A500Pace && e.PaceY != int.MinValue) drop = (short)Math.Max(drop, e.PaceY - e.W(0x4C));   // A500 PACE: the drop since the last A500 pass
        if (was == 0 && Bit(d7, 22) && drop > e.W(0x78))                     // $2B9C landing hard
        {
            var (r, d) = Vehicles.CrashDestroyEntity(s, e, slot, d7);
            return new Step(r, slot, d, true);
        }
        return new Step(e, slot, d7, false);
    }

    // $2BAC: walking <-> swimming, and amphibious vehicles crossing the shore.
    // Multiplayer piece 2: the $219 write goes to the moving player's own byte ($2BF4); the
    // player's number is its slot (only a player's record carries d7 bit 31, set at $2E5A).
    static void Switch(WorldState s, Entity e, ref int d7)
    {
        d7 ^= 1 << 10;                                                       // bchg #10
        if (Bit(d7, 10)) s.Channels.Play(0x13, s.Entities);                  // $2BB4 into the water
        var m = e.W(0x90); e.SetW(0x90, e.W(0x24)); e.SetW(0x24, m);         // swap models
        if (!Bit(d7, 31)) return;
        var mine = s.PlayerAt(s.PlayerOf(e));
        var st = mine.Status219;
        if (Bit(d7, 10))
        {
            if ((st & 0x20) == 0) return;                                    // $2BD8 bclr #5
            st = (byte)(st & ~0x20 | 0x40);
            s.Timer1AC = 0x206;                                              // $2BDE
        }
        else
        {
            if ((st & 0x40) == 0) return;                                    // $2BEA bclr #6
            st = (byte)(st & ~0x40 | 0x20);
        }
        mine.Status219 = st;                                                 // $2BF4
        s.Replan = true;                                                     // $2BF8 $A5 bit 4
    }

    /// <summary>ENHANCEMENT (WorldState.LeaveBehind): the flat offset straight behind the entity's heading (+$7E high byte;
    /// 0 = +z, $40 = +x), from the same sin/cos tables as Vector - exactly behind, not the nearest side. Its length is twice
    /// the box's reach along the heading (half-widths +$6E in x, +$72 in z), so you stand clear of the box: twice +$6E
    /// heading +-x as the original's spots, twice +$72 heading +-z (the boat is longer there).</summary>
    public static (int X, int Z) Behind(Trig trig, Entity e)
    {
        var h = e.B[0x7E];
        var (se, sm) = trig.Sin(h * 4);
        var (ce, cm) = trig.Cos(h * 4);
        var reach = Math.Abs(M68.Asr(M68.Muls(sm, e.W(0x6E)), se)) + Math.Abs(M68.Asr(M68.Muls(cm, e.W(0x72)), ce));
        var d = (short)(2 * reach);
        return (-M68.Asr(M68.Muls(sm, d), se), -M68.Asr(M68.Muls(cm, d), ce));
    }

    /// <summary>$2C00: a vector of length d along the entity's pitch (+$7C - +$39) and heading
    /// (+$7E high byte), from the sinfile tables (value = mantissa >> exponent).</summary>
    public static (short X, short Y, short Z) Vector(Trig trig, Entity e, short d, bool straight = false) =>
        Vector(trig, (byte)(e.B[0x7C] - e.B[0x39]), e.B[0x7E], d, straight);

    /// <summary>The same for a pitch (+$7C - +$39) and heading given: where a step at another heading would go.</summary>
    public static (short X, short Y, short Z) Vector(Trig trig, byte p, byte h, short d, bool straight)
    {
        int x = 0, y = 0, z = d;
        int Asr(int v, int n) => straight && v < 0 ? -M68.Asr(-v, n) : M68.Asr(v, n);   // WorldState.StraightMoves: toward 0
        if (p != 0)
        {
            var (se, sm) = trig.Sin(p * 4);
            var (ce, cm) = trig.Cos(p * 4);
            var nz = (short)((short)Asr(M68.Muls(cm, z), ce) + (short)Asr(M68.Muls(sm, y), se));
            var ny = (short)((short)Asr(M68.Muls(cm, y), ce) - (short)Asr(M68.Muls(sm, z), se));
            z = nz; y = ny;
        }
        if (h != 0)
        {
            var (se, sm) = trig.Sin(h * 4);
            var (ce, cm) = trig.Cos(h * 4);
            var nx = (short)((short)Asr(M68.Muls(cm, x), ce) + (short)Asr(M68.Muls(sm, z), se));
            var nz = (short)((short)Asr(M68.Muls(cm, z), ce) - (short)Asr(M68.Muls(sm, x), se));
            x = nx; z = nz;
        }
        return ((short)x, (short)y, (short)z);
    }
}
