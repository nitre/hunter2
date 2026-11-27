using System.Text;

namespace Hunter.Rules;

/// <summary>What a load puts back besides the world image (SaveOrLoad $1047C, CopySaveBlocks $10530):
/// the globals of the header, the kill counts, the soldier record, the log book, the mission table.
/// Extras (roomy_memory): the extension block's payload, when the save has one - kept apart from
/// every original array.</summary>
public sealed record SavedGame(short MissionNo, short LogLine, byte Flags99, byte FlagsA7, byte FlagsA4,
    byte[] Kills, byte[] SoldierRecord, byte[] MissionTable, byte[] LogPlaces, SaveExtras? Extras = null, SavedPlanet? Planet = null);

/// <summary>The roomy_memory extras a save can carry (the layout is in findings/notes.md):
/// the player's fuel low byte, the parked things' fuel low bytes by grid record offset, the full
/// cells' overflow records by cell base offset, and the kill counts' unwrapped values.</summary>
public sealed record SaveExtras(byte PlayerFuelLow, Dictionary<int, byte> FuelLow,
    Dictionary<int, List<byte[]>> Overflow, int[] KillsWide, Dictionary<int, byte>? BuildingFuelLow = null,
    List<SavedGrave>? Graves = null, FineSpec? Fine = null);

/// <summary>ENHANCEMENT (FINER GROUND): what a save carries of the fine store - its points a side and height factor;
/// the store is rebuilt from them and the world's seed at the load (FineGround.Grow is exact). GROUND SEED / RELIEF
/// (2026-11-11): the detail's own seed, Vista's dimension and the hold too; a save from before carries only the first
/// three (Seed null: the world's own, the dimension and hold today's gentle ones) and rebuilds the store it had.</summary>
public sealed record FineSpec(int N, int Num, int Den, ushort? Seed = null, int Dim = FineGround.Dimension, int Hold = FineGround.Gentle);

/// <summary>One grave of a save's extension block as it was read (the store goes back to
/// WorldState.GraveStore on a load).</summary>
public sealed record SavedGrave(int Number, int X, int Y, int Z, byte Heading, bool Placed, int Offset, bool Taken = false)
{
    public readonly int[] Counts = new int[30];
}

/// <summary>
/// The save block of the disk menu's Save Game (re/formats/save.md): $C880 bytes, file 16 + slot.
/// The remake keeps them in a file of its own; the bytes are the original's.
///   +$00 $208 game, +$02 $20A mission, +$04 clock $1B0-$1B5, +$0C $234 log line, +$0E $99, +$0F $A7,
///   +$10 $A4, +$3E checksum, +$40 kills $9288 ($AF), +$240 soldier $85E2 ($20), +$260 mission table
///   $9072 ($C0), +$320 log book $CF54 ($176), +$496 the world image (a world file: seed, sea level,
///   clock, player record, items, grid and building table).
/// </summary>
public static class SaveGame
{
    public const int Size = 0xC880, WorldAt = 0x496, WorldSize = 0xC3E8;

    /// <summary>Bytes the save never writes: the original's buffer is the screen memory ($8342, screen0
    /// and part of screen1), so they hold whatever was on screen. Write keeps what the scratch buffer
    /// holds there (zeros by default).</summary>
    public static readonly (int From, int To)[] Unwritten =
        [(0xA, 0xC), (0x11, 0x3E), (0xEE, 0x240), (WorldAt + 4, WorldAt + 0x10), (WorldAt + 0x26, WorldAt + 0x28), (WorldAt + WorldSize, Size)];

    static void W(byte[] b, int o, int v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
    static int W(byte[] b, int o) => b[o] << 8 | b[o + 1];

    /// <summary>GameToSoldier ($87E4, run at $24D6 whenever play ends): the status word, energy and score
    /// into the soldier record, the player's record StoreEntityRecord ($82D0: kind, variant, energy,
    /// position, heading, fuel, speed's low byte) into $8602 and its slot freed, then
    /// CountsToItemColumn ($9DF6: every item's count moves to the soldier's stock column $214*2+$E)
    /// and the masks of what is carried ($A2A8). Does nothing when the player is already gone.</summary>
    public static void GameToSoldier(WorldState s)
    {
        var p = s.Entities.All[0];
        var sol = s.Soldier.R;
        sol[0] = s.Status216;                                                // $87E8 move.w $216,(a2): $216 the high byte; $217, the low,
                                                                             // is the record's own +1 - SoldierToGlobals' move.w ($87A4)
                                                                             // loaded it and nothing in play writes it, so it goes back as it was
        W(sol, 0xC, s.Score >> 16); W(sol, 0xE, s.Score);                    // $87EC
        W(sol, 6, s.Energy);                                                 // $87F2
        var r = s.PlayerRecord;
        W(r, 6, p.W(0x4A)); W(r, 8, p.W(0x4C)); W(r, 10, p.W(0x4E));         // $82D0
        W(r, 0, p.W(2));                                                     // $82D6 kind
        r[2] = p.B[1];                                                       // $82DA variant
        r[0xC] = p.B[0x7E];                                                  // $82E0 heading
        r[0xD] = p.B[0x34];                                                  // $82E6 fuel
        if (s.RoomyMemory) s.PlayerFuelLow = p.B[0x35];   // ENHANCEMENT (roomy_memory): the low byte the record cannot hold
        r[0xE] = p.B[0x33];                                                  // $82EC
        W(r, 4, p.W(0x2A));                                                  // $82F2 energy
        r[3] = (byte)p.W(0x3E);                                              // $82F8 speed, low byte
        r.CopyTo(sol, 0x20);                                                 // $8602 is soldier +$20
        p.B[0] = 0;                                                          // $8300 sf (a4)
        for (var i = 0; i < 30; i++)                                         // $9DF6 -> MoveItemColumn $9E12, $214 = 0
        {
            var o = 0x28 + i * 32;
            var n = W(s.Items, o);
            if (s.Data.Main[0x9E44 - 0x800] >= 0x80) n += W(s.Items, o + 0xE);   // $9E1E tst.b $9E44: the flag is clear
            W(s.Items, o + 0xE, n);
            W(s.Items, o, 0);
        }
        Items.Carried(s);                                                    // $9E40
    }

    /// <summary>SaveOrLoad ($1047C) saving: GameToSoldier first if the player is still in play, then the
    /// header, SaveWorldImage ($105A8: SetClock $C498 also clears the minute's wait $1AE), the four
    /// blocks ($10530) and the checksum ($1056E). scratch: what the buffer held before (see Unwritten).</summary>
    public static byte[] Write(WorldState s, byte[]? scratch = null)
    {
        if (s.Entities.All[0].Alive) GameToSoldier(s);                       // not again after EndPlay
        var b = scratch is null ? new byte[Size] : (byte[])scratch.Clone();
        var home = Home(s);                                                  // PLANET: the played world's records, wherever the game is
        W(b, 0, (int)s.Mode); W(b, 2, s.MissionNo);                          // $1048A, $1048E
        W(b, 4, s.Minute); W(b, 6, s.Hour); W(b, 8, s.Day);                  // $104AC-$104B2
        W(b, 0xC, s.Log.Selected);                                           // $10494
        // $99 whole ($104A0): bits 1 (visit) and 7 (in play) are always 0 here - EndOfPass consumes 1 every pass,
        // StopPlay $24F2 clears 7 - but bit 5 (pause) is whatever 'p' left it: KeyDown toggles it on the menus too
        b[0xE] = (byte)((s.Radar ? 8 : 0) | (s.MapMarks ? 4 : 0) | (s.RespawnPending ? 0x10 : 0) | (s.InsideView ? 1 : 0)
            | (s.PauseKey ? 0x20 : 0));
        b[0xF] = s.Flags0A7; b[0x10] = s.Flags0A4;
        var w = s.Data.Worlds[s.Mode];
        W(b, WorldAt, w.Seed); W(b, WorldAt + 2, w.SeaLevel);                // $105A8, $105AC
        s.ClockWait = 0;                                                     // $105B8 SetClock: clr.w $1AE
        W(b, WorldAt + 0x10, s.Minute); W(b, WorldAt + 0x12, s.Hour); W(b, WorldAt + 0x14, s.Day);   // $105BC
        Array.Copy(s.PlayerRecord, 0, b, WorldAt + 0x16, 16);                // $10618 (exchanged: memory -> image)
        Array.Copy(s.Items, 0x28, b, WorldAt + 0x28, 0x3C0);                 // $10608
        Array.Copy(home.Grid, 0, b, WorldAt + 0x3E8, home.Grid.Length);      // $10626: $C000 bytes from $83BA, the grid ...
        Array.Copy(s.Buildings, 0, b, WorldAt + 0x3E8 + home.Grid.Length, s.Buildings.Length);   // ... and the table after it
        Array.Copy(s.Kills, 0, b, 0x40, 0xAE);                               // $10530: CopyBytes $10658 moves $AF/4 longs and
                                                                             // one word - 174 bytes; the TV-room's count (+$EE) stays unwritten
        Array.Copy(s.Soldier.R, 0, b, 0x240, 0x20);                          // $10540
        Array.Copy(s.Log.Places, 0, b, 0x320, 0x176);                        // $1054E
        Array.Copy(s.MissionTable, 0, b, 0x260, 0xC0);                       // $1055C
        W(b, 0x3E, Checksum(b));
        var planet = s.Planet is { } pl ? PlanetSave.Write(s, pl) : [];       // ENHANCEMENT (PLANET): its own block, last
        if (!s.RoomyMemory && !s.Graves && s.FineN == 0) return [.. b, .. planet];   // the original's block, byte for byte (and the planet's)
        var ext = ExtrasBlock(s);                                            // ENHANCEMENT (roomy_memory / graves): the
        return [.. b, .. ext, .. planet];                                    // extras ride after the block
    }

    /// <summary>The played world's grid and roomy extras: the state's own, or - the game on another face of the planet -
    /// the played world face's (Planet.Faces[WorldFace]), never the current face's.</summary>
    static (byte[] Grid, Dictionary<int, byte> FuelLow, Dictionary<int, List<byte[]>> Overflow) Home(WorldState s) =>
        s.Planet is { } p && p.Current != Planet.WorldFace
            ? (p.Faces[Planet.WorldFace].Grid, p.Faces[Planet.WorldFace].FuelLow, p.Faces[Planet.WorldFace].Overflow)
            : (s.Grid, s.FuelLow, s.Overflow);

    /// <summary>A tagged block: 8 tag, 4 payload length (to the end, the sum included), the payload padded even, 2 word sum.</summary>
    public static byte[] Tagged(string tag, List<byte> p)
    {
        if (p.Count % 2 == 1) p.Add(0);
        var b = new byte[12 + p.Count + 2];
        Encoding.ASCII.GetBytes(tag).CopyTo(b, 0);
        var l = p.Count + 2;
        b[8] = (byte)(l >> 24); b[9] = (byte)(l >> 16); b[10] = (byte)(l >> 8); b[11] = (byte)l;
        p.CopyTo(b, 12);
        int sum = 0;
        for (var i = 12; i < b.Length - 2; i += 2) sum += b[i] << 8 | b[i + 1];
        W(b, b.Length - 2, sum);
        return b;
    }

    /// <summary>The tagged blocks after the original's (the roomy extension, the planet's), each whole: tag and bytes.
    /// Throws on bytes that are not one, a wrong length or a failed sum - the save is then damaged where the original's
    /// checksum cannot see.</summary>
    static List<(string Tag, byte[] Seg)> Segments(byte[] block)
    {
        var list = new List<(string, byte[])>();
        for (var o = Size; o < block.Length;)
        {
            if (block.Length - o < 14) throw new InvalidDataException("trailing bytes that are not an extension");
            var tag = Encoding.ASCII.GetString(block, o, 8);
            if (tag is not ("HUNTRMY1" or "HUNTRMY2" or PlanetSave.Tag)) throw new InvalidDataException("trailing bytes that are not a roomy_memory extension");
            var len = block[o + 8] << 24 | block[o + 9] << 16 | block[o + 10] << 8 | block[o + 11];
            if (len < 2 || o + 12 + len > block.Length) throw new InvalidDataException("the extension's length is wrong");
            var seg = block[o..(o + 12 + len)];
            int sum = 0;
            for (var i = 12; i < seg.Length - 2; i += 2) sum += seg[i] << 8 | seg[i + 1];
            if ((sum & 0xFFFF) != (seg[^2] << 8 | seg[^1])) throw new InvalidDataException("the extension fails its checksum");
            list.Add((tag, seg));
            o += 12 + len;
        }
        return list;
    }

    /// <summary>The planet's block of a save, or null (PlanetSave).</summary>
    public static SavedPlanet? ParsePlanet(byte[] block) =>
        block.Length <= Size ? null : Segments(block).Where(t => t.Tag == PlanetSave.Tag).Select(t => PlanetSave.Parse(t.Seg)).FirstOrDefault();

    /// <summary>The extension block (only with roomy_memory or graves on), byte by byte:
    ///   +$00  8  tag "HUNTRMY1"
    ///   +$08  4  payload length L, big-endian (from +$0C to the block's end, checksum included)
    ///   +$0C  L  the payload (see below), padded to an even byte count with 0
    ///   +$0C+L-2  2  checksum: every big-endian word of the payload, summed, low 16 bits
    /// The payload:
    ///   1  the player's fuel low byte
    ///   2  F, then F entries of (2 grid record offset, 1 fuel low byte)
    ///   2  O, then O cells of (2 cell base offset, 1 record count, count x 17 record bytes: the 16 a grid
    ///      slot holds plus the fuel low byte at +$10)
    ///   2  K, then K entries of (1 type, 4 count, big-endian)
    ///   2  B, then B entries of (2 building-table record offset, 1 fuel low byte) - things left inside buildings
    ///   2  G, then G entries of (2 grave number, 2 X, 2 Y, 2 Z, 1 heading, 1 placed flag, 2 grid record
    ///      offset when placed, 30 x 2 the carried counts) - the graves store; present only with graves on, absent
    ///      in older extensions (a load then has no graves). The placed grave's own grid record rides
    ///      the original's block: this section only carries the kit and the waiting ones.
    ///   12 FINER GROUND only: (2 N, 2 factor numerator, 2 denominator, 2 the detail's seed, 2 Vista's dimension, 2 the
    ///      hold) - the fine store's spec, rebuilt at the load from it and the world's seed; the graves count before it
    ///      is then always written (0 with graves off). The last three ride since GROUND SEED / RELIEF (2026-11-11): a
    ///      save with only the first 6 loads as the world's own seed, gentle.
    /// Parse takes the original's $C880 exactly as before and keeps this apart; a save without one
    /// loads with the extras empty.</summary>
    public static byte[] ExtrasBlock(WorldState s)
    {
        var p = new List<byte> { s.PlayerFuelLow };
        void Two(int v) { p.Add((byte)(v >> 8)); p.Add((byte)v); }
        var home = Home(s);
        var fuel = home.FuelLow.OrderBy(kv => kv.Key);
        Two(home.FuelLow.Count);
        foreach (var (k, v) in fuel) { Two(k); p.Add(v); }
        var over = home.Overflow.OrderBy(kv => kv.Key);
        Two(home.Overflow.Count);
        foreach (var (k, list) in over) { Two(k); p.Add((byte)list.Count); foreach (var r in list) p.AddRange(r); }
        var wide = Enumerable.Range(0, s.KillsWide.Length).Where(t => s.KillsWide[t] != 0).ToList();
        Two(wide.Count);
        foreach (var t in wide) { Two(t); for (var sh = 24; sh >= 0; sh -= 8) p.Add((byte)(s.KillsWide[t] >> sh)); }
        Two(s.BuildingFuelLow.Count);                                        // things left inside buildings (lead's review)
        foreach (var (k, v) in s.BuildingFuelLow.OrderBy(kv => kv.Key)) { Two(k); p.Add(v); }
        if (s.Graves || s.FineN > 0)                                         // ENHANCEMENT (graves): the store, only with the setting on
        {                                                                    // (an empty one when only finer ground follows it)
            var graves = s.Graves ? s.GraveStore : [];
            Two(graves.Count);
            foreach (var g in graves)
            {
                Two(g.Number); Two(g.X); Two(g.Y & 0xFFFF); Two(g.Z);
                p.Add(g.Heading);
                p.Add((byte)(g.Taken ? 2 : g.Placed ? 1 : 0));               // 0 waiting, 1 placed, 2 taken (emptied by a touch)
                Two(g.Placed ? g.Offset : 0);
                for (var i = 0; i < 30; i++) Two(g.Counts[i]);
            }
        }
        if (s.FineN > 0)                                                     // ENHANCEMENT (FINER GROUND): its spec, after the graves
        { Two(s.FineN); Two(s.FineNum); Two(s.FineDen); Two(s.FineSeed); Two(s.FineDim); Two(s.FineHold); }
        return Tagged("HUNTRMY2", p);                                       // 2: the kill counts' types in two bytes (1: one)
    }

    /// <summary>The extension of a block written past Size ( ExtrasBlock's bytes back), or null when
    /// there is none. Throws when the bytes say there is one and it does not check out: the save is
    /// then damaged where the original's checksum cannot see.</summary>
    public static SaveExtras? ParseExtras(byte[] block)
    {
        if (block.Length == Size) return null;
        var found = Segments(block).FirstOrDefault(t => t.Tag.StartsWith("HUNTRMY"));
        if (found.Seg is null) return null;                                  // the planet's block alone
        var wideTypes = found.Tag == "HUNTRMY2";
        block = [.. new byte[Size], .. found.Seg];                           // the extension alone after the block's room, as read below
        var p = Size + 12;                                                   // the payload
        int Two() => block[p++] << 8 | block[p++];
        var player = block[p++];
        var fuel = new Dictionary<int, byte>();
        for (var n = Two(); n > 0; n--) { var k = Two(); fuel[k] = block[p++]; }
        var over = new Dictionary<int, List<byte[]>>();
        for (var n = Two(); n > 0; n--)
        {
            var k = Two();
            var list = new List<byte[]>();
            for (var m = block[p++]; m > 0; m--) { var r = new byte[17]; Array.Copy(block, p, r, 0, 17); p += 17; list.Add(r); }
            over[k] = list;
        }
        var wide = new int[ExtraKinds.Slots];
        for (var n = Two(); n > 0; n--) { var t = wideTypes ? Two() : block[p++]; if (t >= wide.Length) Array.Resize(ref wide, t + 1); for (var sh = 24; sh >= 0; sh -= 8) wide[t] = wide[t] << 8 | block[p++]; }
        var inside = new Dictionary<int, byte>();
        if (block.Length - 2 - p >= 2)                                       // the building section (absent in older extensions)
            for (var n = Two(); n > 0; n--) { var k = Two(); inside[k] = block[p++]; }
        var graves = new List<SavedGrave>();
        if (block.Length - 2 - p >= 2)                                       // the graves section (absent in older extensions)
            for (var n = Two(); n > 0; n--)
            {
                int number = Two(), x = Two(), y = (short)Two(), z = Two(); var heading = block[p++]; var state = block[p++];
                var g = new SavedGrave(number, x, y, z, heading, state != 0, Two(), state == 2);
                for (var i = 0; i < 30; i++) g.Counts[i] = Two();
                graves.Add(g);
            }
        FineSpec? fine = null;
        if (block.Length - 2 - p >= 6) fine = new FineSpec(Two(), Two(), Two());   // the finer ground section (absent without it)
        if (fine is not null && block.Length - 2 - p >= 6)                  // GROUND SEED / RELIEF: its seed, dimension and hold
            fine = fine with { Seed = (ushort)Two(), Dim = Two(), Hold = Two() };   // (absent in a save from before: the world's, gentle)
        return new SaveExtras(player, fuel, over, wide, inside, graves, fine);
    }

    /// <summary>SaveChecksum ($1056E): +$3E counted as $B241, $1B0 plus every word of the block (16 bits).</summary>
    public static int Checksum(byte[] b)
    {
        var sum = 0x1B0;
        for (var i = 0; i < Size; i += 2) sum += i == 0x3E ? 0xB241 : W(b, i);
        return sum & 0xFFFF;
    }

    /// <summary>The extras of a block for StartState.For (null when the checksum fails: $10464, "load failed").
    /// A block past Size is the original's plus a roomy_memory extension: the original's $C880 bytes are
    /// read exactly as before and the extension is kept apart (SavedGame.Extras) - a load with the
    /// setting off ignores it, and loses the extras at its next save (none is written then).</summary>
    public static (GameMode Mode, byte[] World, SavedGame Saved)? Parse(byte[] block)
    {
        if (block.Length < Size || W(block, 0x3E) != Checksum(block)) return null;            // $104F4-$104FC
        var mode = (GameMode)W(block, 0);
        var saved = new SavedGame((short)W(block, 2), (short)W(block, 0xC), block[0xE], block[0xF], block[0x10],
            block.AsSpan(0x40, 0xAE).ToArray(), block.AsSpan(0x240, 0x20).ToArray(),
            block.AsSpan(0x260, 0xC0).ToArray(), block.AsSpan(0x320, 0x176).ToArray());
        if (block.Length > Size) saved = saved with { Extras = ParseExtras(block), Planet = ParsePlanet(block) };
        return (mode, block.AsSpan(WorldAt, WorldSize).ToArray(), saved);
    }

    /// <summary>Load: the saved game as a running state (the world image is a world file: $105EC copies
    /// it as a new game does, then the save's globals, kills, soldier, log book and mission table go over
    /// it; the game starts at $23BE). The save holds neither $1AA nor $1CA, so they stay where the game
    /// that just ended left them: the caller passes that game's leftovers (a fresh boot passes none).
    /// Throws when the checksum is wrong.</summary>
    public static WorldState Read(GameData d, byte[] block, MachineLeftovers? leftovers = null, bool fixSpikes = false)
    {
        var (mode, world, saved) = Parse(block) ?? throw new InvalidDataException("the saved game fails its checksum ($1056E)");
        return StartState.For(mode, d, world, saved: saved, leftovers: leftovers, fixSpikes: fixSpikes);
    }
}
