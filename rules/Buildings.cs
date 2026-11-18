namespace Hunter.Rules;

/// <summary>
/// Buildings (re/formats/buildings.md): the door test ($458C), what a visit asks for ($46A0), the
/// inside's bounds and the way out ($42E6, $432A), the block of records a building keeps ($4922,
/// $4982). The visit itself ($46F0) and the inside's passes are in Play (Play.Buildings.cs).
/// </summary>
public static class Buildings
{
    static bool Bit(int v, int n) => (v >> n & 1) != 0;

    /// <summary>DoorBumpAndEnterRequest ($458C), after the plain bump ($44EC): A1 = the building
    /// (d7 = a4's flags: bit 6 says a4 is the building), A0 = the one that touched it. The door box
    /// is the six words at +$62 (x1 y1 z1 x2 y2 z2, relative to the building: set by the model's
    /// first two special vertices, Objects.Specials): the toucher's previous z (+$54) must not be
    /// beyond the box's z1, its x between x1 and x2. Not already inside ($99 bit 0): the building
    /// is remembered ($1554) and $99 bit 1 asks EndOfPass for the visit.</summary>
    internal static void DoorTest(WorldState s, Entity a4, int slot4, Entity a3, int slot3, int d7)
    {
        Entity a0 = a3, a1 = a4;
        var slot1 = slot4;
        if (!Bit(d7, 6)) { a0 = a4; a1 = a3; slot1 = slot3; }                // $4594-$459A exg
        var d0 = a1.W(0x62);
        var d2 = (short)(a1.W(0x66) + a1.W(0x4E));                           // $45A2
        if (d2 < a0.W(0x54)) return;                                         // $45A6 blt
        var d3 = a1.W(0x68);
        var bx = a1.W(0x4A);
        d0 = (short)(d0 + bx);
        d3 = (short)(d3 + bx);
        var x = a0.W(0x4A);
        if (x < d0 || x > d3) return;                                        // $45BA blt, $45BE bgt
        if (s.InsideView) return;                                            // $45C0
        s.BuildingSlot = slot1;                                              // $45C8
        s.VisitRequest = true;                                               // $45CC bset #1,$99
    }

    /// <summary>CheckRequiredItems ($46A0): the building's +$94 bits 11, 10, 8 ask for items $1B,
    /// $1A, $19 in that order; the first one not carried (ItemCount $A232: $240 = it, its count
    /// not above 0) says "You need a(n) X to enter this Y" ($B5F0) and answers true.</summary>
    public static bool MissingItem(WorldState s, Entity b)
    {
        foreach (var (item, bit) in new[] { (0x1B, 11), (0x1A, 10), (0x19, 8) })
        {
            if (!Bit(b.W(0x94), bit)) continue;                              // $46CE
            s.Item240 = (short)item;                                         // $A232
            var o = 0x28 + item * 32;
            if ((short)(s.Items[o] << 8 | s.Items[o + 1]) > 0) continue;     // $46D6 bgt
            Messages.Show(s, 0xB5F0, (short)(s.Items[o + 0x1A] << 8 | s.Items[o + 0x1B]), b.W(4));   // $46D8-$46E4
            return true;
        }
        return false;
    }

    /// <summary>The spot outside the building's door that PlaceAtDoor ($432A) computes: z = the
    /// building's z - its half depth - the player's half depth - $10, x = the middle of the door
    /// box ($62..$68). Shared with Graves.Capture (a death inside a building leaves its grave
    /// here); PlaceAtDoor is the only caller in the original.</summary>
    public static (int X, int Z) DoorSpot(WorldState s, int playerHalf)
    {
        var b = s.Entities.All[s.BuildingSlot];
        var z = (short)(b.W(0x4E) - b.W(0x72) - playerHalf - 0x10);                // $432E-$433E
        var d0 = b.W(0x62);
        var x = (short)(d0 + b.W(0x4A) + (short)((short)(b.W(0x68) - d0) >> 1));   // $4346-$4354
        return (x & 0xFFFF, z & 0xFFFF);                                     // word values (44054 does not fit a short)
    }

    /// <summary>PlaceAtDoor ($432A): the player put outside the building's door, heading $7F;
    /// $99 bit 0 clear. Multiplayer: n = which player; 0 is the
    /// original; the record leaving the room passes its own slot (KeepInside).</summary>
    public static void PlaceAtDoor(WorldState s, int n = 0)
    {
        var p = s.Entities.All[n];
        var (x, z) = DoorSpot(s, p.W(0x72));
        p.SetW(0x4E, (short)z);
        p.SetW(0x4A, (short)x);
        p.B[0x7E] = 0x7F;                                                    // $4358
        s.InsideView = false;                                                // $435E bclr #0,$99
    }

    /// <summary>KeepInsideBoundsOrExit ($42E6, from ViewPositionAndBucket $41D6 for a record with
    /// +$20): inside the bounds nothing. Outside: a shot or blast (+$06 bit 20) is spent (+$2A = $FF
    /// high byte); the record goes back to its previous place ($4310) - except the player past the
    /// minimum z (the door's side), who, with nothing missing ($46A0), is put outside ($432A).
    /// Returns the registers $41D0's d0-d2 hold afterwards: the old place when put back; when the
    /// player leaves, d0 = his new x, d1 whatever $46A0 left, d2 his old z ($41E0 saves them as +$50).</summary>
    internal static (int X, int Y, int Z) KeepInside(WorldState s, Entity e, int x, int y, int z)
    {
        var b = s.Bounds;
        if ((short)x >= b[0] && (short)x <= b[3] && (short)z >= b[2] && (short)z <= b[5]) return (x, y, z);   // $42E6-$42FC
        var d7 = e.Flags;
        if (!Bit(d7, 31))                                                    // $4300
        {
            if (Bit(d7, 20)) e.B[0x2A] = 0xFF;                               // $4306-$430C
            return PutBack(e);
        }
        if ((short)z > b[2]) return PutBack(e);                              // $431E-$4322
        if (MissingItem(s, s.Entities.All[s.BuildingSlot])) return PutBack(e);   // $4324 bmi
        var b0 = s.Entities.All[s.BuildingSlot];
        PlaceAtDoor(s, s.Players <= 1 ? 0 : s.PlayerOf(e));   // the player in the room is the record leaving (one player: slot 0, the original)
        // the registers after $4324-$4364: d0 = PlaceAtDoor's last (the new x), d1 = what $46A0 left (8
        // from $46BA, or the item's table offset when ItemCount ran last for item $19), d2 untouched
        var carried19 = Bit(b0.W(0x94), 8);
        return (e.UW(0x4A), carried19 ? 0x19 * 32 : 8, z);
    }

    // $4310: the previous place into the record and the registers
    static (int, int, int) PutBack(Entity e)
    {
        e.SetW(0x4A, e.W(0x50)); e.SetW(0x4C, e.W(0x52)); e.SetW(0x4E, e.W(0x54));
        return (e.UW(0x50), e.W(0x52), e.UW(0x54));
    }

    /// <summary>ModelHalfSizes ($3346): +$6E / +$70 / +$72 from the main model (+$25): half the width,
    /// the height, half the depth.</summary>
    public static void ModelHalfSizes(WorldState s, Entity e)
    {
        var o = s.Models.Offsets[e.B[0x25]];
        e.SetW(0x6E, s.Models.U(o + 4) >> 1);
        e.SetW(0x70, s.Models.U(o + 6));
        e.SetW(0x72, s.Models.U(o + 8) >> 1);
    }

    /// <summary>GameOver ($3514): $A5 bit 5, then bit 7 (QuitKey's end, $3520): the next pass
    /// leaves play at $2586. The end screens that follow are Task 8.</summary>
    public static void GameOver(WorldState s) => s.Flags0A5 |= 0xA0;

    /// <summary>FindInterior ($4922): the block (128 bytes) of Buildings whose key long (x, z of the
    /// building) matches; none: the first empty block (key 0), keyed now with the building's x and
    /// z and its seven records cleared ($494E-$4970: 14 longs from +$10). Offset into Buildings.</summary>
    public static int FindInterior(WorldState s, Entity b)
    {
        int Find(uint key)
        {
            for (var at = 0; at < 128 * 128; at += 128)
                if ((uint)(s.Buildings[at] << 24 | s.Buildings[at + 1] << 16 | s.Buildings[at + 2] << 8 | s.Buildings[at + 3]) == key) return at;
            return -1;
        }
        var at0 = Find((uint)b.UW(0x4A) << 16 | b.UW(0x4E));
        if (at0 >= 0) return at0;
        at0 = Find(0);
        // $4946 bmi $494E: no free block either -> bsr $4930 again, forever; the original crashes
        if (at0 < 0) throw new NotSupportedException("$4950 FindInterior: no free block (128 buildings in use) - the original recurses until it crashes");
        s.Buildings[at0] = b.B[0x4A]; s.Buildings[at0 + 1] = b.B[0x4B];
        s.Buildings[at0 + 2] = b.B[0x4E]; s.Buildings[at0 + 3] = b.B[0x4F];
        Array.Clear(s.Buildings, at0 + 0x10, 56);
        return at0;
    }

    /// <summary>SaveInterior ($4982): every record in slots 1-29 with +$20 and a kind goes back
    /// into the building's block, one after the other from +$10, and its slot is freed ($82D0-$8300).</summary>
    public static void SaveInterior(WorldState s)
    {
        var at = s.ContainerAt + 0x10;
        for (var n = s.Players; n < s.Entities.Count; n++)                   // the player slots are skipped (slot 0 today)
        {
            var e = s.Entities.All[n];
            if (!e.Alive || e.B[0x20] == 0 || e.W(2) == 0) continue;
            var g = s.Buildings;
            void W(int o, int v) { g[at + o] = (byte)(v >> 8); g[at + o + 1] = (byte)v; }
            W(6, e.W(0x4A)); W(8, e.W(0x4C)); W(10, e.W(0x4E));
            W(0, e.W(2));
            g[at + 2] = e.B[1];
            g[at + 0x0C] = e.B[0x7E];
            g[at + 0x0D] = e.B[0x34];
            if (s.RoomyMemory) s.BuildingFuelLow[at] = e.B[0x35];          // ENHANCEMENT (roomy_memory): the low byte too
            g[at + 0x0E] = e.B[0x33];
            W(4, e.W(0x2A));
            g[at + 3] = e.B[0x3F];
            e.B[0] = 0;
            at += 0x10;
        }
    }
}
