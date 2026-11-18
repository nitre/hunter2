namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (renderer 3d): the map as the card-built world reads it (Terrain.Map):
/// a map point (col, row) is 2 bytes at (row * 128 + col) * 2 - its height (above the sea, Terrain.Point $CEC2
/// clamps the sea to 0; world y = height << 4) then the type byte of the cell whose near-left corner it is
/// (Ground.Draw's a4 = MapOffset1550 - $101: the odd byte of that corner). The point sits at world
/// (col * 512, row * 512). Type 0 = no land (Draw skips it; the sea shows). Colour = type &amp; $0F; shape =
/// bits 4-6 -> Ground.CellFaces: a full square, or one triangle where the coast cuts it. Pure: no Godot types.</summary>
public static class GroundMesh
{
    public const int Points = 128, Cell = 512;

    /// <summary>The type byte of the cell whose near-left (smallest col, row) corner is (col, row).</summary>
    public static byte CellType(byte[] map, int col, int row) => map[(row * Points + col) * 2 + 1];

    /// <summary>The world spot (x, z) is on water: a type-0 cell, or a coast cell's side cut off (Land).</summary>
    public static bool Water(byte[] map, ushort x, ushort z)
    {
        var t = CellType(map, x / Cell, z / Cell);
        return t == 0 || !Land(t >> 4 & 7, 1 - x % Cell / (double)Cell, 1 - z % Cell / (double)Cell);
    }

    // A coast cell (shapes 2-7) keeps the land side of its diagonal. u, v run from the cell's far-right corner (its
    // largest col, row - corner 1 of Ground.Draw's order) back across it: corner 1 is (0, 0), 0 is (1, 0), 2 is
    // (0, 1), 3 is (1, 1).
    static bool Land(int shape, double u, double v) => shape switch
    {
        2 => u <= v,                                                 // 1-2-3: corner 0 cut off
        3 => u + v <= 1,                                             // 0-1-2: corner 3 cut off
        5 => u + v >= 1,                                             // 0-2-3: corner 1 cut off
        >= 4 => u >= v,                                              // 3-0-1 (4, 6, 7): corner 2 cut off
        _ => true,
    };
}
