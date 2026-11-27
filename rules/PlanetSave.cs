namespace Hunter.Rules;

/// <summary>What a save carries of the planet (PlanetSave): which face the game is on, the grown faces' records (the
/// played world's ride the original's block), the harvest and the worms.</summary>
public sealed record SavedPlanet(int Current, int Spice, int WormMeals, int WormAway, byte[]? Worm, byte[]? Departing,
    List<SavedFace> Faces);

/// <summary>A grown face's records: its grid's used slots (offset, 16 bytes) and its roomy extras.</summary>
public sealed record SavedFace(int Index, List<(int At, byte[] Rec)> Slots, Dictionary<int, byte> FuelLow, Dictionary<int, List<byte[]>> Overflow);

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (PLANET step 5, saves): the planet in a save. Its own block after the original's (and after
/// the roomy extension when there is one), laid out as that one: tag "HUNTPLN1", 4 payload length, the payload, a 2-byte
/// word sum. The payload:
///   1  the face the game is on (the player's record in the original's block is in that face's frame)
///   4  the spice taken (WorldState.Spice), 4 the worms' meals, 2 the passes till the next worm
///   4  W, then W bytes of the worm (Sandworm.Save; W = 0 none); the same for the departing worm
///   1  F, then F faces of (1 index, 2 S, S x (2 grid offset, 16 record bytes) - every slot with any byte set, emptied ones too, 2 fuel-low entries x (2 offset, 1 byte),
///      2 overflow cells x (2 cell offset, 1 count, count x 17 bytes)) - every grown face, all of its records
/// The played world's grid, fuel lows and overflow ride the original's block and the roomy extension as without the
/// planet - when the game is on another face they are the played world's, never the current face's (SaveGame.Write).
/// A load with this block builds the planet (whatever the setting says now: the save is a planet game), puts every
/// face's records back over the fresh ones, then goes to the face it was on (Planet.Enter, where the player stands).</summary>
public static class PlanetSave
{
    public const string Tag = "HUNTPLN1";

    public static byte[] Write(WorldState s, Planet planet)
    {
        var p = new List<byte>();
        void Two(int v) { p.Add((byte)(v >> 8)); p.Add((byte)v); }
        void Four(int v) { Two(v >> 16); Two(v); }
        void Bytes(byte[]? b) { Four(b?.Length ?? 0); if (b is not null) p.AddRange(b); }
        p.Add((byte)planet.Current);
        Four(s.Spice); Four(planet.WormMeals); Two(planet.WormAway);
        Bytes(planet.Worm?.Save()); Bytes(planet.Departing?.Save());
        var grown = Enumerable.Range(0, Faces.Count).Where(i => i != Planet.WorldFace).ToList();
        p.Add((byte)grown.Count);
        foreach (var i in grown)
        {
            var f = planet.Faces[i];
            p.Add((byte)i);
            var used = Enumerable.Range(0, f.Grid.Length / 16).Where(k => f.Grid.AsSpan(k * 16, 16).ContainsAnyExcept((byte)0)).ToList();   // every slot with bytes in it: an emptied one keeps its old bytes, as the game leaves them
            Two(used.Count);
            foreach (var k in used) { Two(k * 16); p.AddRange(f.Grid.AsSpan(k * 16, 16).ToArray()); }
            Two(f.FuelLow.Count);
            foreach (var (k, v) in f.FuelLow.OrderBy(kv => kv.Key)) { Two(k); p.Add(v); }
            Two(f.Overflow.Count);
            foreach (var (k, list) in f.Overflow.OrderBy(kv => kv.Key)) { Two(k); p.Add((byte)list.Count); foreach (var r in list) p.AddRange(r); }
        }
        return SaveGame.Tagged(Tag, p);
    }

    public static SavedPlanet Parse(byte[] seg)
    {
        var p = 12;
        int One() => seg[p++];
        int Two() => seg[p++] << 8 | seg[p++];
        int Four() => Two() << 16 | Two();
        byte[]? Bytes() { var n = Four(); if (n == 0) return null; var b = seg[p..(p + n)]; p += n; return b; }
        int current = One(), spice = Four(), meals = Four(), away = Two();
        byte[]? worm = Bytes(), departing = Bytes();
        var faces = new List<SavedFace>();
        for (var n = One(); n > 0; n--)
        {
            var index = One();
            var slots = new List<(int, byte[])>();
            for (var m = Two(); m > 0; m--) { var at = Two(); slots.Add((at, seg[p..(p + 16)])); p += 16; }
            var fuel = new Dictionary<int, byte>();
            for (var m = Two(); m > 0; m--) { var k = Two(); fuel[k] = seg[p++]; }
            var over = new Dictionary<int, List<byte[]>>();
            for (var m = Two(); m > 0; m--)
            {
                var k = Two(); var list = new List<byte[]>();
                for (var c = One(); c > 0; c--) { list.Add(seg[p..(p + 17)]); p += 17; }
                over[k] = list;
            }
            faces.Add(new SavedFace(index, slots, fuel, over));
        }
        return new SavedPlanet(current, spice, meals, away, worm, departing, faces);
    }
}

public sealed partial class Planet
{
    /// <summary>Passes till the next worm rises (for a save).</summary>
    public int WormAway => _wormAway;

    /// <summary>A save's planet back over the freshly built one (StartState.For): every grown face's records in place of the
    /// planted ones, the harvest and the worms, then the face the game was on - entered where the player stands.</summary>
    public void Restore(SavedPlanet saved)
    {
        foreach (var f in saved.Faces)
        {
            if (f.Index == WorldFace || f.Index >= Faces.Length) continue;
            var face = Faces[f.Index];
            Array.Clear(face.Grid);
            foreach (var (at, rec) in f.Slots) rec.CopyTo(face.Grid, at);
            face.FuelLow.Clear(); foreach (var (k, v) in f.FuelLow) face.FuelLow[k] = v;
            face.Overflow.Clear(); foreach (var (k, v) in f.Overflow) face.Overflow[k] = v;
        }
        _s.Spice = saved.Spice;
        WormMeals = saved.WormMeals;
        _wormAway = saved.WormAway;
        Worm = saved.Worm is { } w ? Sandworm.Load(w) : null;
        Departing = saved.Departing is { } d ? Sandworm.Load(d) : null;
        if (saved.Current != WorldFace && saved.Current < Faces.Length)
        {
            var p = _s.Entities.All[0];
            Enter(saved.Current, p.UW(0x4A), p.UW(0x4E));
        }
    }
}
