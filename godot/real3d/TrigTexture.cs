using Godot;

namespace Hunter;

/// <summary>The sinfile (game_data/files/07_sinfile.bin) as a texture the 3D shaders can fetch:
/// 512 x 2, Format.Rf, nearest - the layout corner.gdshaderinc reads. Row 0 = Sin, row 1 = Cos
/// (the table's own +$400 half); entry a (the angle byte) sits at x = a*2 (the exponent word E,
/// 0..65535) and x = a*2 + 1 (the SIGNED mantissa word M). Floats hold 16-bit words exactly and
/// texelFetch reads them back unfiltered, so the card rotates corners number-exact.</summary>
public static class TrigTexture
{
    public static ImageTexture From(byte[] sinfile)
    {
        var img = Image.CreateEmpty(512, 2, false, Image.Format.Rf);
        for (var a = 0; a < 256; a++)
        {
            var o = a * 4;
            img.SetPixel(a * 2, 0, new Color(sinfile[o] << 8 | sinfile[o + 1], 0, 0));
            img.SetPixel(a * 2 + 1, 0, new Color((short)(sinfile[o + 2] << 8 | sinfile[o + 3]), 0, 0));
            img.SetPixel(a * 2, 1, new Color(sinfile[o + 0x400] << 8 | sinfile[o + 0x401], 0, 0));
            img.SetPixel(a * 2 + 1, 1, new Color((short)(sinfile[o + 0x402] << 8 | sinfile[o + 0x403]), 0, 0));
        }
        return ImageTexture.CreateFromImage(img);
    }
}
