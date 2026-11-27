namespace Hunter.Rules;

/// <summary>SHAI HULUD (the game menu's sand island, until the planet is finished): a planet game that starts on THE SAND
/// ZONE's face with every crossing closed, so the sand island - its sand and rock, spice, harvester and worm - is the
/// whole world. The player starts on rock (the worm cannot go there), the rock point nearest the face's middle.</summary>
public static class ShaiHulud
{
    public static void Start(Planet p)
    {
        p.Open.Clear();
        p.Open.Add(SandZone.Face);                                  // at every edge he is turned back at the line
        var (x, z) = StartSpot(p.Faces[SandZone.Face]);
        p.Enter(SandZone.Face, x, z);
    }

    /// <summary>The middle of the rock map point nearest the face's middle (the face's middle itself when it has no rock).</summary>
    public static (int X, int Z) StartSpot(Planet.Face f)
    {
        var sand = f.Sand!;
        int best = int.MaxValue, bx = 32768, bz = 32768;
        for (var r = 0; r < Terrain.Size; r++)
            for (var c = 0; c < Terrain.Size; c++)
            {
                if (sand.Kind[r * Terrain.Size + c] != SandZone.Rock) continue;
                var d = (c - 64) * (c - 64) + (r - 64) * (r - 64);
                if (d < best) (best, bx, bz) = (d, c * 512 + 256, r * 512 + 256);
            }
        return (bx, bz);
    }
}
