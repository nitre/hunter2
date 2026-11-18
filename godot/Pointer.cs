namespace Hunter;

/// <summary>The mouse pointer: sprite 0 at $AA46 (15 rows of two 16-bit words, plane A and plane B;
/// $AA32 copies it out) drawn with its top left at the pointer - (7, 6) ($AA1C). It uses colour 17
/// of the palette bank on show only. The start screens and the in-game options screen both use it.
/// </summary>
public static class Pointer
{
    /// <summary>Decode the 16 x 15 sprite from the main binary.</summary>
    public static byte[] Sprite(byte[] main)
    {
        var o = new byte[15 * 16];
        for (var r = 0; r < 15; r++)
        {
            var at = 0xAA46 - 0x800 + 4 + r * 4;
            int a = main[at] << 8 | main[at + 1], b = main[at + 2] << 8 | main[at + 3];
            for (var x = 0; x < 16; x++) o[r * 16 + x] = (byte)((a >> 15 - x & 1) | (b >> 15 - x & 1) << 1);
        }
        return o;
    }

    /// <summary>Draw the sprite over a 320 x 200 RGB picture at pointer (x, y).</summary>
    public static void Draw(byte[] rgb, byte[] sprite, ushort colour, int x, int y)
    {
        for (var r = 0; r < 15; r++)
            for (var c = 0; c < 16; c++)
            {
                int px = x - 7 + c, py = y - 6 + r;
                if (sprite[r * 16 + c] == 0 || px < 0 || px >= 320 || py < 0 || py >= 200) continue;
                var p = 3 * (py * 320 + px);
                rgb[p] = (byte)((colour >> 8 & 15) * 17);
                rgb[p + 1] = (byte)((colour >> 4 & 15) * 17);
                rgb[p + 2] = (byte)((colour & 15) * 17);
            }
    }
}
