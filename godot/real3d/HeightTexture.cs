using System;
using Godot;

namespace Hunter;

/// <summary>(gpu_height): the whole map's heights as one float
/// texture for the GPU ground shaders. Texel (col, row) = the height byte of map point (col, row) as a float
/// (map[(row * 128 + col) * 2], 0..255 - float32 holds it exactly), 128 x 128, Rf. Sample with texelFetch:
/// nearest, no repeat, no filtering involved (the edge neighbours are fetched explicitly, see
/// ground_height.gdshaderinc). The height in world units is the texel value &lt;&lt; 4, as on the CPU.</summary>
public static class HeightTexture
{
    public static ImageTexture From(byte[] map)
    {
        var data = new byte[128 * 128 * 4];
        for (var i = 0; i < 128 * 128; i++)
            BitConverter.GetBytes((float)map[i * 2]).CopyTo(data, i * 4);
        return ImageTexture.CreateFromImage(Image.CreateFromData(128, 128, false, Image.Format.Rf, data));
    }

    /// <summary>(FINER GROUND): the fine store (rules/FineGround.cs) as one Rf texture,
    /// Side x Side, texel (i, j) = the height in world units (0..32767, exact in a float) - fine_ground.gdshaderinc.</summary>
    public static ImageTexture From(Hunter.Rules.FineGround fine)
    {
        var data = new byte[fine.H.Length * 4];
        for (var i = 0; i < fine.H.Length; i++)
            BitConverter.GetBytes((float)fine.H[i]).CopyTo(data, i * 4);
        return ImageTexture.CreateFromImage(Image.CreateFromData(fine.Side, fine.Side, false, Image.Format.Rf, data));
    }
}
