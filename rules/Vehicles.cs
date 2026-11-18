namespace Hunter.Rules;

/// <summary>
/// Getting into and out of vehicles, the player set-up, and a vehicle ending (re/formats/vehicles.md
/// sections 2, 3 and the hard landing in 4). Slots are record indices; offsets as the original's
/// (slot * $A0 from $250).
/// </summary>
public static class Vehicles
{
    static bool Bit(int v, int n) => (v >> n & 1) != 0;

    /// <summary>TouchVehicle ($45D4): the one on foot (A4 if A4's d7 has bit 15, else A3) gets
    /// +$1F = $FF and +$22 = the other's offset.</summary>
    public static void Touch(Entity a4, int slot4, Entity a3, int slot3)
    {
        var (who, other) = Bit(a4.Flags, 15) ? (a4, slot3) : (a3, slot4);
        who.B[0x1F] = 0xFF;
        who.SetW(0x22, other * EntityTable.Size);
    }

    /// <summary>LeaveVehicleKey ($A766, from the keyboard interrupt): Space in a vehicle.</summary>
    public static void LeaveVehicleKey(Entity player)
    {
        if (!player.Bit(0x08, 15)) player.B[0x1F] = 0x7F;
    }

    /// <summary>EnterOrLeaveVehicle ($2F8E). Returns the record UpdateEntities writes d7 back to
    /// (the original's A4 after the call) with that d7.</summary>
    public static (Entity Record, int D7) EnterOrLeaveVehicle(WorldState s, Entity e, int slot, int d7)
    {
        var t = s.Entities;
        if ((sbyte)e.B[0x1F] >= 0) return Leave(s, e, slot, d7);
        var vslot = e.UW(0x22) / EntityTable.Size;                           // $2F90. unsigned: the original's adda.w sign-extends ($2F94) - fine to slot 204; more_objects goes to 393
        var v = t.All[vslot];
        v.SetL(0x0A, e.L(0x0A));
        v.B[1] = e.B[1];
        var old = d7;
        Copy(s, vslot, slot);                                                // $2FAA CopyEntity
        v.B[0] = 0;                                                          // $2FAE sf (a3)
        d7 = e.Flags & ~(1 << 28);                                           // $2FB0
        if (!Bit(d7, 2) && e.L(0x88) != 0)
        {
            d7 |= 1 << 19;
            s.Channels.StartEntity(e, ref d7, slot, t);                      // $2FCA
        }
        d7 = Bit(old, 31) ? PlayerSetUpOrDie(s, e, d7)                            // $2FD8
                          : Behaviour.SwapModelsAndPlan(s, e, d7);           // $2FD4: a driver getting in
        return (e, d7);
    }

    /// <summary>CopyEntity ($33AE): 160 bytes from -> to; a held sound channel follows.</summary>
    static void Copy(WorldState s, int from, int to)
    {
        var a0 = s.Entities.All[from];
        if (a0.Bit(0x09, 2)) s.Channels.SetOwner(a0.W(0x98) & 3, to * EntityTable.Size);
        a0.B.CopyTo(s.Entities.All[to].B, 0);
    }

    /// <summary>FindFreeSlot ($3398): the first free slot of 0-Spawnable-1 (A1 = $250, D5 = $1C, 29 tests), -1 if none.
    /// The last slot is never handed out - so at most 29 things live with the setting off and the sort list $4DF0 (30 entries, no room for
    /// a 31st end marker before DrawInteriorRows $4E68) never overflows. (Was 1-29 before 2026-10-17.)
    /// ENHANCEMENT (Play.MoreObjects): 394 handed out with more_objects on - the port's sort list is unbounded.</summary>
    public static int FindFreeSlot(WorldState s) => Array.FindIndex(s.Entities.All, 0, s.Entities.Spawnable, x => !x.Alive);

    /// <summary>MoveToFreeSlot ($337C): copy to the first free slot (FindFreeSlot), free this one; -1 if none.</summary>
    static int MoveToFreeSlot(WorldState s, int slot)
    {
        var free = FindFreeSlot(s);                                          // $337C bsr $3398
        if (free < 0) return -1;
        if (s.BuildingSlot == slot) s.BuildingSlot = free;                   // $3382-$338A: $1554 follows the copy
        Copy(s, slot, free);
        s.Entities.All[slot].B[0] = 0;
        return free;
    }

    // LeaveVehicle ($2FDC)
    static (Entity, int) Leave(WorldState s, Entity e, int slot, int d7)
    {
        e.B[0x1F] = 0;
        if (!ExtraKinds.IsVehicle(e.W(4))) return (e, d7);                    // $2FE8: not in a vehicle
        var vslot = MoveToFreeSlot(s, slot);
        if (vslot < 0) return (e, d7);                                       // $316A: no room, nothing happens
        var v = s.Entities.All[vslot];
        if (Bit(d7, 31)) RoundsBack(s, v, Of(s, e));                         // $2FFA
        e.B[1] = v.B[1];                                                     // $3000
        int type;
        if (e.B[0x1E] != 0)
        {
            type = e.B[1] + 0x1E;                                            // a soldier of the driver's variant
            PlaceBeside(s, v, e);                                            // $4366
            e.SetW(0x4C, s.Ground.Height(e.UW(0x4A), e.UW(0x4E)));
        }
        else
        {
            type = 0x30;                                                     // in the air: a parachutist
            PlaceBeside(s, v, e);                                            // $4372
        }
        Replace(s, slot, type);                                              // $302A $8158, $302E $3172
        var m = v.W(0x9E); v.SetW(0x9E, v.W(0x24)); v.SetW(0x24, m);         // $3034
        v.B[1] = 0;
        v.SetL(0x0A, 0);
        var vd7 = (v.Flags | 1 << 28) & ~(1 << 31);
        if (Bit(vd7, 2) && v.W(0x3E) == 0) s.Channels.StopEntity(v, ref vd7);   // $3066
        v.SetL(6, vd7);
        return (v, vd7);
    }

    /// <summary>PlaceBeside ($4372): four spots at twice the vehicle's half-width (+x, -x, +z, -z),
    /// the first clear of every live record's own box; none -> where the vehicle was. The spot mask
    /// is `move.w #-0x7bdf,d5` at $4380 = $8421: one bit per 4-bit try.</summary>
    static void PlaceBeside(WorldState s, Entity v, Entity e)
    {
        var all = s.Entities.All;
        v.B[0] = 0; e.B[0] = 0;
        var d3 = (short)(v.W(0x6E) * 2);
        int d5 = 0x8421;                                                     // $4380
        int x0 = e.W(0x4A), z0 = e.W(0x4E), x = x0, z = z0;
        var found = false;
        var (bx, bz) = s.LeaveBehind ? Movement.Behind(s.Trig, v) : (0, 0);   // ENHANCEMENT (LeaveBehind): exactly behind, tried first
        for (var tryN = s.LeaveBehind ? -1 : 0; tryN < 4 && !found; tryN++)
        {
            x = x0; z = z0;
            if (tryN < 0) { x += bx; z += bz; }
            else
            {
                if ((d5 & 0x8000) != 0) x += d3; d5 = d5 << 1 & 0xFFFF;
                if ((d5 & 0x8000) != 0) x -= d3; d5 = d5 << 1 & 0xFFFF;
                if ((d5 & 0x8000) != 0) z += d3; d5 = d5 << 1 & 0xFFFF;
                if ((d5 & 0x8000) != 0) z -= d3; d5 = d5 << 1 & 0xFFFF;
            }
            found = true;
            foreach (var a3 in all)
            {
                var clear = true;                                            // st d1
                if (a3.Alive)
                {
                    clear = WorldState.AbsDist(s, a3.UW(0x4E), (ushort)z) > a3.W(0x72);
                    if (!clear) clear = WorldState.AbsDist(s, a3.UW(0x4A), (ushort)x) > a3.W(0x6E);
                }
                if (a3.Bit(0x06, 30)) clear = !clear;                         // $43D2
                if (!clear) { found = false; break; }
            }
        }
        if (!found) { x = x0; z = z0; }
        e.SetW(0x4A, x); e.SetW(0x4E, z);                                    // y kept ($43F4)
        v.B[0] = 0xFF; e.B[0] = 0xFF;
    }


    /// <summary>$8158 + ReplaceEntity ($3172): a new record of this type in this slot, at the
    /// old record's place and angles.</summary>
    static Entity Replace(WorldState s, int slot, int type)
    {
        var e = s.Entities.All[slot];
        var d3 = KindAndType(s.Data.Main, type);
        int x = e.UW(0x4A), y = e.W(0x4C), z = e.UW(0x4E);
        var d4 = (e.W(0x7C) & 0xFF00 | e.B[0x7E]) << 16 | (e.B[0x80] << 8) | e.B[1];   // $3178-$3186
        var player = e.B[0x20] != 0 ? (byte)0xFF : (byte)0;
        StartState.Construct(s, slot, x, y, z, d3, d4, player);
        return e;
    }

    /// <summary>KindForType ($8160): the kind word of a type (table $81BA). Type $AF or more gives 0, as the
    /// original's guard does ($8162 cmp #$AF, bge -> D3 = 0) - without it the walk runs past the table.</summary>
    public static int KindForType(byte[] main, int type)
    {
        if (ExtraKinds.Is(type)) return ExtraKinds.KindWord(type);          // ENHANCEMENT (ExtraKinds): the last class carries on
        if (type >= 0xAF) return 0;
        int T(int i) => (short)(main[0x81BA - 0x800 + 2 * i] << 8 | main[0x81BB - 0x800 + 2 * i]);
        var i = 0;
        while (type >= T(i + 1)) i++;
        return (type - T(i)) << 8 | i;
    }

    /// <summary>SetKindForType ($8158): the long it stores at +$02 = kind word, type word. KindForType clears the
    /// whole long first ($8160 clr.l D3), so a type of $AF or more becomes kind 0 AND type 0.</summary>
    public static int KindAndType(byte[] main, int type) =>
        type >= ExtraKinds.End || type >= 0xAF && !ExtraKinds.Is(type) ? 0 : KindForType(main, type) << 16 | (ushort)type;

    /// <summary>PlayerSetUpOrDie ($2E52): this record is the player now. Returns d7. Multiplayer
    /// piece 2: the $228 / $219 / $1536 / $99-bit-4 writes go to this player's own state
    /// (player number = its slot, WorldState.PlayerOf); the other writes stay shared or go
    /// through the player-0 properties (see findings\design.md).</summary>
    public static int PlayerSetUpOrDie(WorldState s, Entity e, int d7)
    {
        var player = s.PlayerOf(e);
        var me = s.PlayerAt(player);
        e.SetL(0x0A, 0);
        me.Fuel228 = 0xFF;                                                   // $2E56 st $228: redraw the gauge
        d7 |= 1 << 31;
        d7 &= ~(1 << 28 | 1 | 1 << 16);
        e.SetL(6, d7);
        FootModels(s, e, d7);                                                // $2E6E $A72C
        var w = d7 & 0xFFFF;
        me.Status219 = (byte)((w << 6 | w >> 10) & 0x3F);                     // $2E72
        var type = e.W(4);
        if (ExtraKinds.IsEnd(type))                                          // $2EC4: the player's record became an end
        {
            if (type == 0x7C) return d7;                                     // already dead
            if (AddEnergy(s, -4))                                            // $2ECA: a life costs 4 energy
            {
                if (s.Graves) Graves.Capture(s, e, player: player);   // ENHANCEMENT (Play.Graves): the kit into a grave at the death spot, out of play
                me.Respawn1536 = 0x1E;                                       // $2ED2: a new soldier next pass
            }
            else
            {
                me.Respawn1536 = 0x7C;                                       // $2EE0: the last one: dead
                if (s.Graves) Graves.Capture(s, e, place: false, player: player);   // ENHANCEMENT (Play.Graves): the last grave's kit in the store too - the record itself stays the original's
                s.Timer18C = 0xFA;                                           // the end comes after 250 blanks ($6CB8)
                s.Flags0A2 |= 4;
                s.Status216 |= 0x80;                                         // $2EF8: dead (the clock stops, $3578; the debrief reads it)
                s.Clickable = false;                                         // $2EFE bclr #7,$96
                // $2F04-$2F2C: unless inside a building, the death fade of the palettes (colours
                // only, Play.StartDeathFade / DeathFaded: game.md 7)
                s.DeathFadePending = !s.InsideView;                          // $2F04 btst #0,$99: not in a building's view
                s.Channels.Play(0, s.Entities);                              // $2F32 $B11C d4 = 0
                var drowned = (s.Status216 & 4) != 0;                        // $2F3E bclr #2,$216 (set by $B072)
                s.Status216 &= 0xFB;
                Messages.Show(s, drowned ? 0xB5E0 : 0xB5EA);                 // $2F4C $C1B2: "drowned!" / "DEAD"
                me.RespawnPending = true;
                return d7;
            }
            me.RespawnPending = true;                                        // $99 bit 4
            WeaponOnSetUp(s, e, d7);                                         // $2EBA
            return d7;
        }
        s.Replan = true;                                                     // $2E8C $A5 bit 4
        Blank.CameraBase(s, s.Models, e);                                    // $2E92 $2F56
        if (ExtraKinds.IsVehicle(type))
        {
            var m = e.W(0x9E); e.SetW(0x9E, e.W(0x24)); e.SetW(0x24, m);     // $2EA0 $2EE / $274
            Messages.Show(s, 0xB539, type);                                  // $2EAE $2EB4 $C1B2: "boarded a X" (d0 = $254)
        }
        WeaponOnSetUp(s, e, d7);                                             // $2EBA $A34E
        return d7;
    }

    /// <summary>AddEnergy ($A42C): energy += d0, not below 0, the panel bar ($A43A); false when
    /// it is 0 (exhausted); else $A3 bit 6 (the lamp flashes) when 6 or less.</summary>
    public static bool AddEnergy(WorldState s, int d0)
    {
        int v = (short)(s.Energy + d0);                                      // $A42C add.w: wraps at 16 bits
        if (v < 0) v = 0;                                                    // $A430 bpl
        if (s.PlotArmour && d0 < 0 && v < 1) { v = 1; s.ArmourSaves.Add("drowning"); }                          // ENHANCEMENT (PLOT ARMOUR, bot films only): never empty
        s.Energy = (short)v;
        s.Panel.EnergyBar(v);
        if (v == 0) return false;
        s.Flags0A3 = (byte)(v <= 6 ? s.Flags0A3 | 0x40 : s.Flags0A3 & ~0x40);   // $A468-$A472
        return true;
    }

    /// <summary>$2E28 (UpdateEntities $25C8, $99 bit 4): the player's slot rebuilt as type $1536
    /// where the player was, through SpawnInFreeSlot (slot 0 is the first free - the quirk kept
    /// byte for byte with one player). Multiplayer piece 1: takes the player number (0 today);
    /// with more than one player the respawn goes straight into that player's own reserved slot.
    /// ENHANCEMENT (graves): type 0 (set by Graves.Capture - a drowning at sea) leaves the player's
    /// slot dead - no grave, no soldier.</summary>
    public static void Respawn(WorldState s, int player = 0)
    {
        var e = s.Entities.All[player];
        var type = s.PlayerAt(player).Respawn1536;                           // mp_life: this player's own $1536
        e.B[0] = 0;                                                          // st then sf $250
        if (s.Graves && type == 0) return;                                   // the drowned soldier stays dead
        var d3 = KindAndType(s.Data.Main, type);                             // $2E3C $8158 (the low word only)
        e.SetL(2, d3);
        int x = e.UW(0x4A), y = e.W(0x4C), z = e.UW(0x4E);
        var d4 = e.B[0x7E] << 16;
        var inside = s.InsideView;                                           // $2E2C $99 bit 0, read before AtHome
        if (s.Graves && type == 0x1E)                                        // ENHANCEMENT (Play.Graves): the next soldier
            (x, y, z, d4) = Graves.AtHome(s, player);                        // starts at his own home, his own kit loaded
        var f24F = (byte)(inside ? 0xFF : 0);                                // - the respawn runs mid inside pass ($25C8 in
        if (s.Players == 1)
            StartState.SpawnInFreeSlot(s, x, y, z, d3, d4, f24F);            // $47A8's loop): the soldier carries +$20 = FF
        else                                                                 // through it, and the way out ($478C) clears it
            StartState.Construct(s, player, x, y, z, d3, d4, f24F);          // the player's own reserved slot
    }

    /// <summary>FootModels ($A72C, d2 = the player's +$06): on foot, the variant and the walk /
    /// swim models (also the item 19 cell, $A726).</summary>
    public static void FootModels(WorldState s, Entity e, int d7)
    {
        if (!Bit(d7, 15)) return;
        var v = (s.Flags0A4 & 0x20) != 0 ? 1 : 0;
        e.B[1] = (byte)v;
        int d0 = v + 1, d1 = v + 0xE;
        if (Bit(d7, 10)) (d0, d1) = (d1, d0);
        e.B[0x25] = (byte)d0;
        e.B[0x91] = (byte)d1;
        s.Replan = true;                                                     // $2BF8
    }

    static int Item(int i) => 0x28 + i * 32;

    /// <summary>mp_life: the player number of a record, 0 when it is no player's (the original's
    /// one shared pack - byte for byte with one player).</summary>
    static int Of(WorldState s, Entity e) { var n = s.PlayerOf(e); return n < 0 ? 0 : n; }

    // WeaponOnSetUp ($A34E)
    static void WeaponOnSetUp(WorldState s, Entity e, int d7)
    {
        var t = s.PlayerAt(Of(s, e)).Items;                                  // mp_life: this player's own pack
        short Iw(int o) => (short)(t[o] << 8 | t[o + 1]);
        void Iset(int o, int v) { t[o] = (byte)(v >> 8); t[o + 1] = (byte)v; }
        int d0;
        if (Bit(d7, 15)) d0 = OnFoot();
        else
        {
            s.WeaponBefore = s.Weapon;                                       // $A35C
            d0 = e.W(0x26) - 0x52;
            if (d0 < 0) d0 = OnFoot();
            else
            {
                var rounds = e.W(0x32);
                if (rounds == 0) d0 = OnFoot();
                else Iset(Item(d0), Iw(Item(d0)) + rounds);                  // $A376
            }
        }
        s.Weapon = (short)d0;                                                // $A378
        LoadWeapon(s, e);
        return;

        int OnFoot()
        {
            for (var i = 0; i < 9; i++)                                      // $A302 ClampItemCounts
                if (Iw(Item(i)) > Iw(Item(i) + 2)) Iset(Item(i), Iw(Item(i) + 2));
            return s.WeaponBefore;
        }
    }

    /// <summary>LoadWeapon ($A37C): item $200 into +$32 / +$26 / +$2E, then the panel's icon and
    /// count ($A96C). mp_life: the counts come from the record's own player's pack, the icon and
    /// count go to his own panel.</summary>
    public static void LoadWeapon(WorldState s, Entity e)
    {
        var n = Of(s, e);
        var t = s.PlayerAt(n).Items;
        short Iw(int o) => (short)(t[o] << 8 | t[o + 1]);
        var o = Item(s.Weapon);
        e.SetW(0x32, Iw(o)); e.SetW(0x26, Iw(o + 0x1E)); e.SetW(0x2E, Iw(o + 0x1C));
        (n == 0 ? s.Panel : s.Panel2)?.WeaponIcon(s, s.Weapon, n);
    }

    // RoundsBackToVehicle ($A2DA). mp_life: the leaving driver's own pack.
    static void RoundsBack(WorldState s, Entity v, int player)
    {
        if (v.W(0x26) <= 0) return;
        var t = s.PlayerAt(player).Items;
        short Iw(int o) => (short)(t[o] << 8 | t[o + 1]);
        void Iset(int o, int x) { t[o] = (byte)(x >> 8); t[o + 1] = (byte)x; }
        var o = Item(s.Weapon);
        var d2 = 0;
        var d1 = Iw(o + 2) - Iw(o);
        if (d1 < 0) { Iset(o, Iw(o) + d1); d2 = -d1; }
        v.SetW(0x32, d2);
    }

    /// <summary>CrashDestroyEntity ($2C92) for a record in a vehicle: the vehicle moves to a free slot
    /// and ends, the driver is rebuilt here. Returns the record and d7 UpdateEntities writes.</summary>
    public static (Entity, int) CrashDestroyEntity(WorldState s, Entity e, int slot, int d7)
    {
        if (s.PlotArmour && slot == 0 && !ExtraKinds.IsVehicle(e.W(4)) && !Bit(d7, 28)) { s.ArmourSaves.Add("fall"); return (e, d7); }   // ENHANCEMENT (PLOT ARMOUR): his own fall ends nothing
        if (Bit(d7, 28) || !ExtraKinds.IsVehicle(e.W(4))) return End(s, e, slot, d7);
        var vslot = MoveToFreeSlot(s, slot);
        if (vslot < 0) return (e, d7);                                       // $2CA8 bmi $316A: rts to UpdateEntities, nothing ends
        var driver = Replace(s, slot, e.B[1] + 0x1E);
        driver.SetW(0x7C, 0);                                                // $2CC0 (a0 = the new record)
        var v = s.Entities.All[vslot];
        var vd7 = v.Flags;
        v.B[1] = 0;
        v.SetL(0x0A, 0);
        return End(s, v, vslot, vd7);
    }

    /// <summary>EntityEnds ($3070).</summary>
    public static (Entity, int) End(WorldState s, Entity e, int slot, int d7)
    {
        var type = e.W(4);
        s.Kills[type]++;                                                     // $3078 $9288[type]
        if (s.RoomyMemory) s.KillsWide[type]++;   // ENHANCEMENT (Play.RoomyMemory): the count past a byte, beside the wrapped one
        if (Bit(d7, 2)) s.Channels.StopEntity(e, ref d7);
        var d3 = e.W(0x28);
        if (d3 == 0) { e.B[0] = 0; return (e, d7); }
        d3 &= 0x7FFF;
        if (d3 == 0x60)
        {
            if (!Bit(d7, 31) && !(s.LivingWorld && LivingWorld.ScoresNothing(e)))   // LIVING WORLD: not for an enemy's or wild thing's kill
            {
                s.Score += e.W(0x96);                                        // $9A16 ScoreKill: $21A += +$96 (ext.l)
                Messages.Show(s, 0xB5B5, e.W(4), e.W(0x96));                 // $9A28 $C1B2: "destroyed a X for N credits!" (d0 = +$04, d1)
                s.Panel.Score(s);                                            // $9A2C
            }
            Objects.StartDebris(s, e, slot);                                 // $59FE
        }
        e.B[1] = 0;
        StartState.Construct(s, slot, e.UW(0x4A), e.W(0x4C), e.UW(0x4E), d3,
            (e.W(0x7C) & 0xFF00 | e.B[0x7E]) << 16 | e.B[0x80] << 8, (byte)(e.B[0x20] != 0 ? 0xFF : 0));
        return (e, e.Flags);
    }
}
