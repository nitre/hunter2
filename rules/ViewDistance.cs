namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (setting view_distance, the card-drawn 3D view): how much of the world the card
/// draws. The game itself never reads it - only the picture changes. ORIGINAL (7) is exactly what the original draws:
/// the ground walk's 7 x 7 cells with its template ($39B0 / $3AA3, Ground.Visible) and the things Place marks in view
/// (+$1C); nothing stored on the map. A bigger value adds ground rows beyond the far row, the patch widening so its far
/// edge still fills the view, and things show inside Place's box grown the same way (Objects.SeenBox). ALL (0) draws
/// the whole map and every stored thing, as the card did before this setting.</summary>
public static class ViewDistance
{
    public static readonly int[] Choices = [7, 11, 15, 23, 31, 47, 63, 0];

    /// <summary>HAZE (the card): world units ahead of the eye where this distance's drawn ground ends - its far row at
    /// rows - 0.75 cells (as HalfWidth), the whole map's 128 cells for ALL.</summary>
    public static float FarEdge(int rows) => (rows <= 0 ? 128f : rows - 0.75f) * 512f;

    /// <summary>Columns each side of the camera's column: the original's 3 at 7 rows; beyond, the view's half width at
    /// the far row (depth rows - 0.75 cells, 160 of 256 pixels' focal length, times how much wider than 4:3 the view
    /// is) and one more.</summary>
    public static int HalfWidth(int rows, double wide) => rows == 7 ? 3 : (int)Math.Ceiling((rows - 0.75) * 0.625 * wide) + 1;

    /// <summary>The cells the card draws, one byte a map cell (row * 128 + col, 1 = drawn). False for ALL (draw all).
    /// At 7 the cells are Ground.Visible's, walked as Ground.Draw walks them (far-right first); beyond, a rectangle from
    /// the camera's near row out to rows rows, HalfWidth columns each side.</summary>
    public static bool Cells(Ground g, Camera c, int rows, double wide, byte[] mask, Turn? turn = null)
    {
        if (rows <= 0) return false;
        Array.Clear(mask);
        if (turn is { } t)                                 // Q / E: every cell whose middle the turned view reaches
        {
            for (var cr = 0; cr < 128; cr++)
                for (var cc = 0; cc < 128; cc++)
                    if (t.Sees((cc + 0.5) * 512, (cr + 0.5) * 512, TurnedRows(g, rows), wide, 640))   // the cell's half diagonal, widened by the view's spread
                        mask[cr * 128 + cc] = 1;
            return true;
        }
        if (rows == 7)                                     // the game's own walk, far view's 31 x 39 included (Ground.Visible's cells)
        {
            var vis = g.Visible(c);
            int cols = g.Cols, extra = (cols - 7) / 2;
            for (var r = 0; r < vis.Length / cols; r++)
                for (var k = 0; k < cols; k++)
                    if (vis[r * cols + k])
                        Mark(mask, c.Col153C + extra - 1 - k, c.Row153E - 1 - r);
            return true;
        }
        int near = c.Row153E - 7 - (g.FarView ? Ground.AddRows : 0), col = c.Col153C - 4, h = HalfWidth(rows, wide);
        for (var r = near; r < near + rows; r++)
            for (var x = col - h; x <= col + h; x++) Mark(mask, x, r);
        return true;
    }

    /// <summary>NEW, not the original (Q / E): the eye swung round the player by yaw (View3D.Eye, world units, z north),
    /// looking yaw left of north. The original's patch and Place's box only describe the unturned view, so a turned view
    /// takes every cell and thing inside its own field of view out to the distance's far edge instead.</summary>
    public readonly record struct Turn(double EyeX, double EyeZ, double Yaw)
    {
        readonly double s = Math.Sin(Yaw), c = Math.Cos(Yaw);     // once a turn, not once a cell

        /// <summary>World (x, z) within margin of the view: ahead of the eye up to FarEdge(rows), and inside the
        /// 160-of-256 pixels' half width (times wide) either side.</summary>
        public bool Sees(double x, double z, int rows, double wide, double margin)
        {
            double dx = x - EyeX, dz = z - EyeZ;
            double ahead = c * dz - s * dx, side = c * dx + s * dz;
            return ahead > -margin && ahead < FarEdge(rows) + margin && Math.Abs(side) < Math.Max(ahead, 0) * 0.625 * wide + margin;
        }
    }

    /// <summary>The rows a turned view reaches at this distance: at ORIGINAL, the game's own patch depth (far view's 31).</summary>
    public static int TurnedRows(Ground g, int rows) => rows == 7 && g.FarView ? 7 + Ground.AddRows : rows;

    static void Mark(byte[] mask, int col, int row)
    {
        if (col >= 0 && col < 128 && row >= 0 && row < 128) mask[row * 128 + col] = 1;
    }

    /// <summary>At 3D VIEW DISTANCE ALL the card keeps things beside and behind the camera (a turned camera may see
    /// them), but never one the camera stands inside. The original draws nothing whose near edge is behind the camera
    /// (Place $4216: cz - +$72 >= 0, cz in its doubled camera units against +$72 as it stands); this takes the things
    /// that rule drops AND that reach past the camera: far edge in front (cz + +$72 >= 0) and the camera within its
    /// sides (|cx| <= +$6E, the same scale). So anything this hides, the original hides too.</summary>
    public static bool AroundCamera(Camera c, int x, int z, int half6E, int half72)
    {
        int cx = 2 * x - c.G1548, cz = 2 * z - c.G1544;
        return cz - half72 < 0 && cz + half72 >= 0 && Math.Abs(cx) <= half6E;
    }

    /// <summary>A thing at world (x, z) with near edge +$72 in view at this distance: Place's box (Objects.SeenBox) with
    /// the depth grown a cell ($400) a row past 7 and the sides a cell a column past the original's 3.</summary>
    public static bool InView(Camera c, int x, int z, int near72, int rows, double wide)
    {
        int cx = 2 * x - c.G1548, cz = 2 * z - c.G1544;
        return Objects.SeenBox(cx, cz - near72, 0x800 + (HalfWidth(rows, wide) - 3) * 0x400, 0x1800 + (rows - 7) * 0x400);
    }
}
