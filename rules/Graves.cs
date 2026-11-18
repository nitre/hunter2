namespace Hunter.Rules;

/// <summary>One grave: the soldier's 30 carried counts at his paid death, the spot, and whether the
/// $7C record has reached the grid yet (a full cell - 8 records - leaves it waiting in the store).</summary>
public sealed class Grave
{
    public int Number, X, Y, Z;
    public byte Heading;
    public int[] Counts = new int[30];
    public bool Placed;
    public int Offset = -1;                                                  // the record's offset in the grid, once placed
    public bool Taken;                                                       // emptied by a touch (Items.GiveItems): gone for good
    public bool Summoned;                                                    // ENHANCEMENT (vulture_graves): the pair already asked for -
                                                                             // not saved, so a load's re-placed grave asks fresh
}

/// <summary>
/// ENHANCEMENT, NOT THE ORIGINAL (Play.Graves, docs/extras.md "graves"): each paid death - not the
/// last - leaves a grave holding what the soldier carried, taken out of play so no free resources,
/// and the next soldier starts at the mode's home record with the normal starting kit. The life bar,
/// 4 a death and the last death (the original's own grave, 250 blanks, debrief) are untouched;
/// unlimited graves in one game. Nothing here runs while the setting is off.
/// The store lives beside the original's arrays (like roomy_memory's extras) and rides the save's
/// extension block (SaveGame.ExtrasBlock); the placed grave itself is an ordinary grid record
/// (type $7C, fuel word = the grave's number, low half through roomy memory's FuelLow), so it
/// saves in the original's block and spawns like
/// any grid furniture. The touch still routes to GiveItems (kind table $7C: +$92 = $FF, +$93 = 0) -
/// rewiring it to the store is graves_3's pickup.
/// </summary>
public static class Graves
{
    /// <summary>The carried counts into a new store entry (number = its index in the store) and out
    /// of play (the counts zeroed); e is the ended record at the death spot (End rebuilt it where it
    /// stood). place = false: the last death's entry only - its grave record is the original's own
    /// ($2EE0 -> $2E28), byte for byte.
    /// ENHANCEMENT, NOT THE ORIGINAL (graves_2): a death at sea - a drowning ($216 bit 2, $B072)
    /// or water under the spot - leaves NO grave and NO store entry: the kit is simply lost. A death
    /// inside a building leaves the grave outside at the door spot PlaceAtDoor computes.</summary>
    public static void Capture(WorldState s, Entity e, bool place = true, int player = 0)
    {
        var x = e.UW(0x4A);
        var z = e.UW(0x4E);
        var sea = (s.Status216 & 4) != 0 || InWater(s, x, z);                // $216 bit 2 ($B072), $36D2
        var g = new Grave { Number = s.GraveStore.Count, X = x, Y = e.W(0x4C), Z = z, Heading = e.B[0x7E] };
        var t = s.PlayerAt(player).Items;                                    // mp_life: the dying player's own pack
        for (var i = 0; i < 30; i++)
        {
            var o = 0x28 + i * 32;
            g.Counts[i] = t[o] << 8 | t[o + 1];
            t[o] = t[o + 1] = 0;                                             // the counts are taken either way
        }
        Items.Carried(s, player);                                            // the masks follow the emptied counts
        if (sea)
        {
            if (!place) s.PlayerAt(player).Respawn1536 = 0;                  // no $7C of his own: the drowned
            return;                                                          // soldier stays dead (Respawn reads 0)
        }                                                                    // no grave and no store entry at sea
        if (place && s.InsideView && s.BuildingSlot >= 0)                    // died inside: out to the door, like
        {                                                                    // PlaceAtDoor ($432A). Slot 0 is the $60
            (g.X, g.Z) = Buildings.DoorSpot(s, PlayerHalf(s));               // end record here - its +$72 is not the
            g.Y = s.Ground.Height((ushort)g.X, (ushort)g.Z);                 // player's, so the half comes from his
            g.Heading = 0x7F;                                                // model; Y = the outside ground ($36AA)
        }
        s.GraveStore.Add(g);
        g.Placed = !place;                                                   // the last death: no record of its own.
    }                                                                        // A paid death's grave waits for the
                                                                             // respawn (RetryPending): placed now, the
                                                                             // spawner ($2580) would take it into the dead
                                                                             // soldier's free slot 0 and the respawn
                                                                             // ($25C8 -> $2E28) rebuild slot 0 over it

    /// <summary>Water under an X/Z: the port's own test - Ground.Sample reads the terrain cell's
    /// type byte, 0 = all four corners under sea level ($CF3E), which Sample reports as Land false
    /// (rules\Ground.cs:99-122).</summary>
    public static bool InWater(WorldState s, int x, int z) => !s.Ground.Sample((ushort)x, (ushort)z).Land;

    /// <summary>The on-foot player's half depth at capture time: slot 0 is the $60 end record by
    /// then, so this rebuilds the model FootModels ($A72C) would have given him - variant from $A4
    /// bit 5, on-foot model v+1 - and takes half depth from the model header like ModelHalfSizes
    /// ($3346): header +8 >> 1.</summary>
    static int PlayerHalf(WorldState s)
    {
        var model = ((s.Flags0A4 & 0x20) != 0 ? 2 : 1);                      // the walk model FootModels sets
        var o = s.Models.Offsets[model];
        return s.Models.U(o + 8) >> 1;
    }

    /// <summary>Offset of a grave whose record sits in its cell's overflow list (roomy memory), not a grid slot.</summary>
    public const int InOverflow = -2;

    /// <summary>The $7C record into the first free slot of the death spot's cell (the same cell and
    /// layout Objects.WriteBack $82BA/$82D0 uses); a full cell with roomy memory puts it in the cell's
    /// overflow list, as WriteBack does (cap hunt 2026-11-02: it waited for ever in a busy fuller-world
    /// cell). With roomy memory off a full cell leaves it waiting in the store.</summary>
    static void Place(WorldState s, Grave g)
    {
        var cell = ((g.Z >> 1) & 0x7800) + ((g.X >> 5) & 0x780);             // $82BA
        for (var k = 0; k < 8; k++)
        {
            var r = cell + k * 16;
            if ((s.Grid[r] | s.Grid[r + 1]) != 0) continue;
            WriteRecord(s, r, g);
            Placed(s, g, r);
            return;
        }
        if (!s.RoomyMemory) return;
        if (!s.Overflow.TryGetValue(cell, out var list)) s.Overflow[cell] = list = [];
        var rec = new byte[17];                                              // the slot's 16 bytes and the number's low half
        WriteRecord(s, rec, 0, g);                                           // (+$10, as WriteBack keeps a fuel low byte)
        rec[0x10] = (byte)g.Number;
        list.Add(rec);
        Placed(s, g, InOverflow);
    }

    static void Placed(WorldState s, Grave g, int offset)
    {
        g.Placed = true;
        g.Offset = offset;
        if (s.VultureGraves) VultureGraves.Summon(s, g);                     // ENHANCEMENT (vulture_graves): a new grave draws its pair
    }

    /// <summary>The 16 bytes a grid slot holds, kind word $0008 (class 8, offset 0 - Builder.Spawn
    /// turns that into type $7C by its own arithmetic over the $81BA boundary words) and the grave's
    /// number as the whole fuel word (xf_graves_wrap): with roomy memory on, the high half in the
    /// fuel byte +$D (spawning into +$34) and the low half in roomy's FuelLow for the slot
    /// (spawning into +$35 - the channel parked fuel already uses), so numbers past 255 no longer
    /// wrap; with roomy off (original-only runs) the number keeps today's one byte +$D.</summary>
    public static void WriteRecord(WorldState s, int r, Grave g)
    {
        WriteRecord(s, s.Grid, r, g);
        if (s.RoomyMemory) s.FuelLow[r] = (byte)g.Number;
    }

    // the 16 bytes into a grid slot or an overflow record at r
    static void WriteRecord(WorldState s, byte[] grid, int r, Grave g)
    {
        void W(int o, int v) { grid[r + o] = (byte)(v >> 8); grid[r + o + 1] = (byte)v; }
        grid[r] = 0; grid[r + 1] = 8;                                        // the kind word
        grid[r + 2] = 0; grid[r + 3] = 0;                                    // variant, speed
        W(4, 0x03E8);                                                        // energy 1000: a blast on the death spot
                                                                             // must not destroy the grave (0-energy
                                                                             // entities do not survive one)
        W(6, g.X); W(8, g.Y); W(10, g.Z);
        grid[r + 0x0C] = g.Heading;
        grid[r + 0x0D] = (byte)(s.RoomyMemory ? g.Number >> 8 : g.Number);
        grid[r + 0x0E] = 0; grid[r + 0x0F] = 0;
    }

    /// <summary>Once a pass (Play.EndOfPass, graves on): the graves still waiting in the store try
    /// the first free slot of their cell again - a write-back or a spawn frees one, and the oldest
    /// waiting grave wins. A grave is never lost.</summary>
    public static void RetryPending(WorldState s)
    {
        if (s.RespawnPending) return;                                        // not before the next soldier holds slot 0
        foreach (var g in s.GraveStore)
            if (!g.Placed) Place(s, g);
    }

    /// <summary>The grave's number as its record holds it: the whole fuel word when roomy memory
    /// put a low half beside the record (+$D the high, FuelLow the low - xf_graves_wrap), the one
    /// byte +$D when it did not (the setting off, or an older save's byte record). The tell is the
    /// FuelLow entry itself, not the setting: a load hunts records before the Play's settings land
    /// (StartState.For runs in the constructor, the properties after it), and a run with the
    /// setting off never writes a FuelLow entry at all.</summary>
    static int RecordNumber(WorldState s, byte[] grid, int r) =>
        s.FuelLow.TryGetValue(r, out var low) ? grid[r + 0x0D] << 8 | low : grid[r + 0x0D];

    /// <summary>Whether the grave's $7C record still sits in the grid at its offset (kind word and
    /// number). A save taken while the grave's entity is live keeps no record - the spawner cleared
    /// the slot when the grave spawned - so a load marks such a grave waiting again (StartState.For)
    /// and the retry puts the record back: a grave is never lost to a save either.</summary>
    public static bool RecordIsThere(WorldState s, byte[] grid, Grave g) =>
        g.Offset >= 0 && grid[g.Offset] == 0 && grid[g.Offset + 1] == 8 && RecordNumber(s, grid, g.Offset) == g.Number;

    /// <summary>The grave's record anywhere in its cell (a save writes live things back into the first
    /// free slot of their cell, $33E6 -> $82BA, which need not be the slot the grave first had):
    /// its offset, or -1. Searching the whole cell is what keeps a load from placing a second grave.</summary>
    public static int FindRecord(WorldState s, byte[] grid, Grave g)
    {
        var cell = ((g.Z >> 1) & 0x7800) + ((g.X >> 5) & 0x780);             // $82BA
        for (var k = 0; k < 8; k++)
        {
            var r = cell + k * 16;
            if (grid[r] == 0 && grid[r + 1] == 8 && RecordNumber(s, grid, r) == g.Number
                && (grid[r + 6] << 8 | grid[r + 7]) == g.X && (grid[r + 10] << 8 | grid[r + 11]) == g.Z) return r;
        }
        if (s.Overflow.TryGetValue(cell, out var list))                      // roomy memory: Place or a write-back put it past the 8
            foreach (var rec in list)
                if (rec[0] == 0 && rec[1] == 8 && (rec[0x0D] << 8 | rec[0x10]) == g.Number
                    && (rec[6] << 8 | rec[7]) == g.X && (rec[10] << 8 | rec[11]) == g.Z) return InOverflow;
        return -1;
    }

    /// <summary>Where the next soldier starts (Respawn $2E28, graves on, paid death): the mode's own
    /// start record - WorldState.HomeRecord, read from the mode's file at start, never a copied
    /// number - and its heading. mp_life: player 1's home is that record; player 2's is HIS OWN
    /// base (StartState.BaseSpots, the spot he spawned at), on the ground there. Loads the starting
    /// kit first: the stock column 0 of the mode's own file ($9DEC route, $214 = 0) into THIS
    /// player's counts, so the construct's PlayerSetUpOrDie -> WeaponOnSetUp arms him from it exactly as
    /// a new game does.</summary>
    public static (int X, int Y, int Z, int D4) AtHome(WorldState s, int player = 0)
    {
        s.InsideView = false;                                                // died inside: Respawn builds the new
        var w = s.Data.WorldFile(s.Mode);                                    // soldier with +$20 = 0 (outside), and
                                                                             // AfterInteriorPass's own way out ($4774)
                                                                             // then restores the building and the view
        int x, y, z, d4;
        if (player == 0)
        {
            var h = s.HomeRecord;
            x = h[6] << 8 | h[7];
            y = (short)(h[8] << 8 | h[9]);
            z = h[10] << 8 | h[11];
            d4 = h[0x0C] << 16;
        }
        else
        {
            var (bx, bz) = StartState.BaseSpots[(int)s.Mode];                // the player's own base
            x = bx;
            y = s.Ground.Height((ushort)bx, (ushort)bz);
            z = bz;
            d4 = w[0x16 + 0x0C] << 16;                                       // the start record's heading
        }
        s.PlayerAt(player).Items = StartState.Equip(s.Entities.All[player], w, s.Data.Main[0x9E44 - 0x800]);
        Items.Carried(s, player);
        return (x, y, z, d4);
    }
}
