namespace Hunter.Rules;

/// <summary>STUNTIN' (the Stunt Car Racer zone): a face of the planet, face 3, low and flat, nothing on it but a car
/// parked at its middle. The game menu's Stuntin' starts a planet game on this face with its edges closed (Start);
/// the crossings onto it come later. At the car's scale (1.75 world units to SCR's ground unit) a track's 16 x 16
/// tile grid is about 57000 units across, so one track fills the face's land.</summary>
public static class ScrIsland
{
    /// <summary>The planet face the zone is (one of the grown faces' numbers; its seed is not used).</summary>
    public const int Face = 3;

    public static readonly string[] Tracks =
        ["Little Ramp", "Stepping Stones", "Hump Back", "Big Ramp", "Ski Jump", "Draw Bridge", "High Jump", "Roller Coaster"];

    /// <summary>The land's raw height byte and its edge, in map points from the centre. 16 is the highest the
    /// classify ($CEC2) still calls low land - green (types $0C-$0E), its height halved to 8 (128 world units);
    /// higher is brown high land.</summary>
    public const int LandHeight = 16, Flat = 58, Shore = 61;
    /// <summary>The land's squareness: the edge is |dx|^n + |dz|^n = r^n - high enough that the road's corners (the track's
    /// grid spans map points 16 to 112 both ways at ScrRoad.Scale 1.5) stay over the flat land, inside the grown faces' sea band.</summary>
    public const int Squareness = 16;

    /// <summary>The Hunter world's record of a car (type $01, docs/data/maps/hunter.md): a copy is parked at the start.</summary>
    public const int CarRecord = 1156;

    /// <summary>Where the player starts, at the car: the original's spawn piece (ScrRoadSurface.Start), on the road; the
    /// face's middle when there is no road (no track data).</summary>
    public static (int X, int Z) StartOf(Ground g) => g.Road?.Start ?? (32768, 32768);

    /// <summary>The land: flat inside a rounded square, sloping to the sea at its edge (inside the grown faces' sea band).</summary>
    public static Terrain Land() => Terrain.Shaped((col, row) =>
    {
        double dx = Math.Abs(col - 63.5), dz = Math.Abs(row - 63.5);
        var d = Math.Pow(Math.Pow(dx, Squareness) + Math.Pow(dz, Squareness), 1.0 / Squareness);
        if (d <= Flat) return LandHeight;
        if (d >= Shore) return 0;
        return (int)Math.Round(LandHeight * (Shore - d) / (Shore - Flat));
    }, 0);

    /// <summary>The face's full heights on the map's lattice (129 x 129, world units) for its own store (TALL GROUND,
    /// FineGround.FromHeights: past the byte's 4080 when the land needs it): today the land's own, the map byte as the
    /// game reads it &lt;&lt; 4. (A 12900 test hill proved the store, 2026-11-24: TallGroundTests.)</summary>
    public static int[] Heights(Terrain t)
    {
        var h = new int[129 * 129];
        for (var r = 0; r < 128; r++)
            for (var c = 0; c < 128; c++)
                h[r * 129 + c] = t.Height(c, r) << 4;
        return h;
    }

    /// <summary>The highest surface at (x, z): the road's top where there is road, else the land.</summary>
    public static short Top(Ground g, int x, int z) => g.Surface((ushort)x, (ushort)z, short.MaxValue);

    /// <summary>The car's record: the Hunter world's car moved to the start, on the road, a full tank.</summary>
    public static byte[] Car(GameData d, Ground ground)
    {
        var car = d.Worlds[GameMode.Hunter].Objects.First(o => o.Slot == CarRecord);
        var rec = new byte[16];
        for (var i = 0; i < 8; i++) Put(rec, 2 * i, car.Words[i]);
        var (sx, sz) = StartOf(ground);
        Put(rec, 6, sx);
        Put(rec, 8, Top(ground, sx, sz));
        Put(rec, 0xA, sz);
        rec[0xC] = (byte)((ground.Road?.StartHeading ?? 0) >> 8);   // facing round the lap (the original's spawn)
        rec[0xD] = 0x7F;                                    // the fullest tank: +$34 = byte << 8 is a signed word ($FF would be empty)
        return rec;
    }

    /// <summary>The menu's Stuntin': the game goes to the face, the player at the car (a touch boards it), every edge
    /// turning him back for now.</summary>
    public static void Start(Planet p)
    {
        p.Open.Clear();
        p.Open.Add(Face);
        var (sx, sz) = StartOf(p.Faces[Face].Ground);
        p.Enter(Face, sx, sz);                               // on the road's top, with the car (Enter: the highest surface)
    }

    static void Put(byte[] b, int at, int v) { b[at] = (byte)(v >> 8); b[at + 1] = (byte)v; }
}
