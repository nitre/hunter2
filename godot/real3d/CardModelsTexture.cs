using System;
using Godot;

namespace Hunter;

/// <summary>The store as the card's textures (the part of CardModels that needs the engine).</summary>
public sealed partial class CardModels
{
    /// <summary>mdl_at for the shaders: 256 x 256 R floats, the texel (offset &amp; 255, offset &gt;&gt; 8) = that model's address (0 elsewhere).</summary>
    public ImageTexture AtTexture()
    {
        var f = new float[256 * 256];
        foreach (var m in Models) f[m.Offset] = m.At;
        var b = new byte[f.Length * 4];
        Buffer.BlockCopy(f, 0, b, 0, b.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(256, 256, false, Image.Format.Rf, b));
    }

    /// <summary>The store as the card's texture (mdl_tex), only the rows used.</summary>
    public ImageTexture Texture()
    {
        var b = new byte[Rows * Width * 16];
        Buffer.BlockCopy(Texels, 0, b, 0, b.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(Width, Rows, false, Image.Format.Rgbaf, b));
    }

    /// <summary>Set mdl_tex and mdl_at on a material (the checks that build their own set-up).</summary>
    public void Bind(ShaderMaterial m) { m.SetShaderParameter("mdl_tex", Texture()); m.SetShaderParameter("mdl_at", AtTexture()); }
}
