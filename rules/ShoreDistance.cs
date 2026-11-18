namespace Hunter.Rules;

/// <summary>(setting sea, the card's living sea): how far each map cell lies from the
/// nearest land, so the sea is pale in the shallows by the coast and deep out at sea. The game never reads it. Land =
/// any cell the ground draws (type != 0, coast cells included); a sea cell's distance runs from its centre to the
/// nearest land cell's edge (centre to centre less half a cell), capped at Far.</summary>
public static class ShoreDistance
{
    public const int Far = 2048;                       // world units: 4 cells, past the sea's 1200-unit fade

    /// <summary>One value per cell, row by row (128 x 128), in world units 0..Far: how far a cell is from land (0 on land).
    /// fromSea: how far from the sea instead (0 at sea) - GROUND COLOURS: BANDS' shore.</summary>
    public static float[] Build(byte[] map, bool fromSea = false)
    {
        const int n = GroundMesh.Points, reach = Far / GroundMesh.Cell + 1;
        var d = new float[n * n];
        for (var row = 0; row < n; row++)
            for (var col = 0; col < n; col++)
            {
                var best = (float)Far;
                for (var r = Math.Max(0, row - reach); r <= Math.Min(n - 1, row + reach); r++)
                    for (var c = Math.Max(0, col - reach); c <= Math.Min(n - 1, col + reach); c++)
                        if (GroundMesh.CellType(map, c, r) != 0 != fromSea)
                            best = Math.Min(best, Math.Max(0f, MathF.Sqrt((c - col) * (c - col) + (r - row) * (r - row)) * GroundMesh.Cell - GroundMesh.Cell / 2f));
                d[row * n + col] = best;
            }
        return d;
    }

    /// <summary>Samples a cell each way in BuildFromCoast.</summary>
    public const int FinePerCell = 8;

    /// <summary>GROUND COLOURS: BANDS' shore: how far each
    /// sample is from the sea as the ground draws it - a sea cell's square and a coast cell's cut-off triangle
    /// (GroundMesh.Water's shapes), measured to the shapes themselves, so the beach follows the diagonals in straight
    /// lines (one value a cell put 40% of the beach in the wrong place; a grid of water spots left scallops along
    /// diagonal coasts). One value per sample, FinePerCell a cell each way, row by row, at the sample's middle; world
    /// units, 0 on water, capped at Far.</summary>
    public static float[] BuildFromCoast(byte[] map)
    {
        const int n = GroundMesh.Points * FinePerCell, step = GroundMesh.Cell / FinePerCell, reach = Far / GroundMesh.Cell + 1;
        var water = new (float X, float Z)[GroundMesh.Points * GroundMesh.Points][];
        for (var r = 0; r < GroundMesh.Points; r++)
            for (var c = 0; c < GroundMesh.Points; c++)
                water[r * GroundMesh.Points + c] = WaterShape(map, c, r);
        var f = new float[n * n];
        Parallel.For(0, n, j =>
        {
            for (var i = 0; i < n; i++)
            {
                float x = (i + 0.5f) * step, z = (j + 0.5f) * step;
                int c0 = i / FinePerCell, r0 = j / FinePerCell;
                var best = (float)Far;
                for (var r = Math.Max(0, r0 - reach); r <= Math.Min(GroundMesh.Points - 1, r0 + reach); r++)
                    for (var c = Math.Max(0, c0 - reach); c <= Math.Min(GroundMesh.Points - 1, c0 + reach); c++)
                        if (water[r * GroundMesh.Points + c] is { } w) best = Math.Min(best, Distance(x, z, w));
                f[j * n + i] = best;
            }
        });
        return f;
    }

    // A cell's water in world units: the whole square (type 0), the cut-off triangle of a coast cell (GroundMesh.Land:
    // shapes 2, 4, 6, 7 keep x >= z inside the cell, 3 keep x + z >= 1, 5 keep x + z <= 1), or null.
    static (float X, float Z)[]? WaterShape(byte[] map, int c, int r)
    {
        var t = GroundMesh.CellType(map, c, r);
        float x0 = c * GroundMesh.Cell, z0 = r * GroundMesh.Cell, x1 = x0 + GroundMesh.Cell, z1 = z0 + GroundMesh.Cell;
        if (t == 0) return [(x0, z0), (x1, z0), (x1, z1), (x0, z1)];
        return (t >> 4 & 7) switch
        {
            2 => [(x0, z0), (x1, z1), (x0, z1)],
            4 or 6 or 7 => [(x0, z0), (x1, z0), (x1, z1)],
            3 => [(x0, z0), (x1, z0), (x0, z1)],
            5 => [(x1, z0), (x1, z1), (x0, z1)],
            _ => null,
        };
    }

    // From a point to a convex shape: 0 inside, else to its nearest edge.
    static float Distance(float x, float z, (float X, float Z)[] p)
    {
        var inside = true; var best = float.MaxValue;
        for (var k = 0; k < p.Length; k++)
        {
            var (ax, az) = p[k]; var (bx, bz) = p[(k + 1) % p.Length];
            float ex = bx - ax, ez = bz - az, px = x - ax, pz = z - az;
            var cross = ex * pz - ez * px;
            if (cross < 0) inside = false;
            var t = Math.Clamp((px * ex + pz * ez) / (ex * ex + ez * ez), 0f, 1f);
            float dx = px - t * ex, dz = pz - t * ez;
            best = Math.Min(best, dx * dx + dz * dz);
        }
        return inside ? 0 : MathF.Sqrt(best);
    }
}
