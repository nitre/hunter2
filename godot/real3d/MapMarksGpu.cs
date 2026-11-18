using System;
using System.Collections.Generic;
using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>ENHANCEMENT (renderer = 3d): the overlay map's marks drawn by the card
/// (mapmarks.gdshader) over its 3D picture, the same block at the view's top-left the original draws ($3FFE,
/// MainLoop $25A4 with $99 bit 2) - the processor paints no view while the card draws (4c285a0). The log book's
/// bitmap is resent only when it changes; the player's pixel and the graves go up each frame.</summary>
public sealed partial class MapMarksGpu : ColorRect
{
    const int Side = 128, FirstSlots = 1024;              // graves: room sized up front (big cards), grown only past it

    readonly ShaderMaterial mat;
    readonly LogBook log;
    readonly ImageTexture marksTex;
    readonly byte[] seen = new byte[0x2800];                // the planes DrawMarks reads: 0-3 and the mask ($2000)
    int slots = FirstSlots;                                 // graves per frame, grown when a game has more ("unlimited graves")
    float[] graves = new float[FirstSlots * 3];
    byte[] gravesBytes = new byte[FirstSlots * 3 * 4], gravesSent = [];
    readonly float[] marksFloats = new float[Side * Side];
    readonly byte[] marksBytes = new byte[Side * Side * 4];
    ImageTexture gravesTex;

    public MapMarksGpu(LogBook log)
    {
        this.log = log;
        Position = Vector2.Zero;
        Size = new Vector2(Side, Side + 1);                 // row 128: the player's pixel at the map's south edge
        Visible = false;
        mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://real3d/mapmarks.gdshader") };
        Material = mat;
        marksTex = ImageTexture.CreateFromImage(Image.CreateEmpty(Side, Side, false, Image.Format.Rf));
        mat.SetShaderParameter("marks_tex", marksTex);
        gravesTex = ImageTexture.CreateFromImage(Image.CreateEmpty(slots, 1, false, Image.Format.Rgbf));
        mat.SetShaderParameter("graves_tex", gravesTex);
    }

    /// <summary>Each frame of play: the bitmap (only when the log book changed it), the player's pixel, the
    /// graves and the palette's levels; colour 15 is the sky of its row, so the picture camera's height (the bands)
    /// and skyRows (SKY: FINE GRADIENT's 140 row colours, null = the bands) too. The caller shows or hides the node.</summary>
    public void Frame(Play play, RawPalette pal, int height0e, Texture2D? skyRows)
    {
        var s = play.State;
        var m = log.Marks;
        if (!m.AsSpan(0, seen.Length).SequenceEqual(seen))
        {
            Encode(log, marksFloats);
            Buffer.BlockCopy(marksFloats, 0, marksBytes, 0, marksBytes.Length);
            marksTex.Update(Image.CreateFromData(Side, Side, false, Image.Format.Rf, marksBytes));
            Array.Copy(m, seen, seen.Length);
        }
        var (px, py) = PlayerPixel(s.Entities.All[0]);
        mat.SetShaderParameter("player_at", new Vector2I(px, py));
        var need = s.Graves ? s.GraveStore.Count : 0;
        if (need > slots)
        {
            while (slots < need) slots *= 2;
            graves = new float[slots * 3];
            gravesBytes = new byte[slots * 3 * 4];
            gravesSent = [];
            gravesTex = ImageTexture.CreateFromImage(Image.CreateEmpty(slots, 1, false, Image.Format.Rgbf));
            mat.SetShaderParameter("graves_tex", gravesTex);
        }
        var n = EncodeGraves(s.Graves ? s.GraveStore : null, graves);
        mat.SetShaderParameter("grave_count", n);
        if (n > 0)
        {
            Buffer.BlockCopy(graves, 0, gravesBytes, 0, gravesBytes.Length);
            if (!gravesBytes.AsSpan().SequenceEqual(gravesSent))           // sent again only when a grave moved or was taken
            {
                gravesTex.Update(Image.CreateFromData(slots, 1, false, Image.Format.Rgbf, gravesBytes));
                gravesSent = (byte[])gravesBytes.Clone();
            }
        }
        mat.SetShaderParameter("pal_raw", pal.View);
        mat.SetShaderParameter("fade_level", pal.Fade);
        mat.SetShaderParameter("smooth_level", pal.Smooth);
        mat.SetShaderParameter("death_step", pal.Death);
        mat.SetShaderParameter("sky_raw", pal.Sky);
        mat.SetShaderParameter("sky_level", pal.SkyLevel);
        mat.SetShaderParameter("height0e", height0e);
        mat.SetShaderParameter("sky_steps", skyRows != null);
        mat.SetShaderParameter("sky_tex", skyRows);
    }

    /// <summary>The bitmap as the shader's texture (128 x 128, a texel a pixel): the mark's colour index + 1
    /// where the mask bit is set (Marks[0x2000 + at], the 4 planes below it), 0 where it is not - colour 0
    /// drawn must not read as "nothing here".</summary>
    static void Encode(LogBook log, float[] f)
    {
        var m = log.Marks;
        for (var row = 0; row < Side; row++)
            for (var col = 0; col < Side; col++)
            {
                int at = row * 16 + (col >> 3), bit = 7 - (col & 7);
                f[row * Side + col] = (m[0x2000 + at] >> bit & 1) == 0 ? 0
                    : ((m[at] >> bit & 1) | (m[at + 0x800] >> bit & 1) << 1 | (m[at + 0x1000] >> bit & 1) << 2 | (m[at + 0x1800] >> bit & 1) << 3) + 1;
            }
    }

    /// <summary>The player's pixel ($400C / $4020: x &gt;&gt; 9, 128 - (z &gt;&gt; 9)); row 128, the far north
    /// edge, is one past the block - the original writes it into the view buffer there, the card's block ends
    /// at row 127.</summary>
    static (int X, int Y) PlayerPixel(Entity p) => (p.UW(0x4A) >> 9, 128 - (p.UW(0x4E) >> 9));

    /// <summary>The graves as the shader's texture row: (x &gt;&gt; 9, 128 - (z &gt;&gt; 9), taken) each - the
    /// shader draws the plus itself and skips the taken ones, as GraveMark does. f holds slots * 3 floats.</summary>
    static int EncodeGraves(IEnumerable<Grave>? graves, float[] f)
    {
        var n = 0;
        if (graves != null)
            foreach (var g in graves)
            {
                f[n * 3] = g.X >> 9;
                f[n * 3 + 1] = 128 - (g.Z >> 9);
                f[n * 3 + 2] = g.Taken ? 1 : 0;
                n++;
            }
        return n;
    }
}
