using System;
using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>(renderer = 3d): the panel's rows 140-199 drawn by the card
/// (panel.gdshader, panel.gdshaderinc) from the game's values each frame - the clock, score, position, rounds, fuel,
/// energy, weapon, radar records and graves, lamps - over the panfile picture, in the panel's colours and with the pointer.
/// The message window's text is the one thing still printed by the processor (Text.cs); its plane 0 bits go up each frame.
/// Shown over the software picture's panel rows only while the game is in play with nothing laid over the panel.</summary>
public sealed partial class PanelGpu : ColorRect
{
    const int Lip = 6;                                      // rows above the panel the pointer's tip reaches (its top is y - 6; panel.gdshader LIP)
    readonly ShaderMaterial mat;
    readonly ImageTexture msgTex;
    readonly float[] msg = new float[320 * Rules.Panel.Rows];
    readonly byte[] msgBytes = new byte[320 * Rules.Panel.Rows * 4];
    bool msgSent;                                           // sent once at least; after that only when the text changed
    int slots = 1024;                                       // graves: room sized up front, grown only past it (as MapMarksGpu)
    float[] graves = new float[1024 * 3];
    byte[] gravesBytes = new byte[1024 * 3 * 4], gravesSent = [];
    ImageTexture gravesTex;

    public PanelGpu(GameData data, Rules.Panel font, byte[] pointerSprite, ImageTexture entTex)
    {
        Position = new Vector2(0, Play.PanelTop - Lip);                     // the lip: the pointer's tip over the view's last rows
        Size = new Vector2(320, Rules.Panel.Rows + Lip);
        Visible = false;
        mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://real3d/panel.gdshader") };
        Material = mat;
        var (panfile, pal) = data.Picture("panfile");
        mat.SetShaderParameter("base", Tex(320, Rules.Panel.Rows, panfile));
        var fontBytes = new byte[8 * 256];
        for (var c = 0; c < Math.Min(256, font.FontGlyphs); c++)
            for (var r = 0; r < 8; r++) fontBytes[c * 8 + r] = font.FontLine(c, r);
        mat.SetShaderParameter("font", Tex(8, 256, fontBytes));
        var sheet = data.Picture("storepic").Pixels;
        mat.SetShaderParameter("sheet", Tex(320, sheet.Length / 320, sheet));
        mat.SetShaderParameter("ent", entTex);
        msgTex = ImageTexture.CreateFromImage(Image.CreateEmpty(320, Rules.Panel.Rows, false, Image.Format.Rf));
        mat.SetShaderParameter("msg", msgTex);
        mat.SetShaderParameter("pointer_tex", Tex(16, 15, pointerSprite));
        mat.SetShaderParameter("pan_raw", Array.ConvertAll(pal, c => (int)c));
        gravesTex = ImageTexture.CreateFromImage(Image.CreateEmpty(slots, 1, false, Image.Format.Rgbf));
        mat.SetShaderParameter("graves_tex", gravesTex);
    }

    static ImageTexture Tex(int w, int h, byte[] v)
    {
        var f = new float[w * h];
        for (var i = 0; i < f.Length && i < v.Length; i++) f[i] = v[i];
        var b = new byte[f.Length * 4];
        Buffer.BlockCopy(f, 0, b, 0, b.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rf, b));
    }

    /// <summary>Each frame of play: the values, the palette's levels and the pointer. The caller shows or hides the node.</summary>
    public void Frame(Play play, RawPalette pal, int pointerX, int pointerY, ushort pointerColour, bool pointerOn)
    {
        var s = play.State;
        var p = s.Entities.All[0];
        mat.SetShaderParameter("day", (int)s.Day);
        mat.SetShaderParameter("hour", (int)s.Hour);
        mat.SetShaderParameter("minute", (int)s.Minute);
        mat.SetShaderParameter("clock_on", s.ClockOn ? 1 : 0);
        mat.SetShaderParameter("score", s.Score);
        mat.SetShaderParameter("rounds", (int)p.W(0x32));
        var (s0, s1) = s.Panel.DaySuffix(Math.Clamp((int)s.Day, 0, 22));
        mat.SetShaderParameter("suffix0", (int)s0);
        mat.SetShaderParameter("suffix1", (int)s1);
        mat.SetShaderParameter("pos_x", (int)p.UW(0x4A));
        mat.SetShaderParameter("pos_z", (int)p.UW(0x4E));
        mat.SetShaderParameter("fuel", (int)p.B[0x34]);
        mat.SetShaderParameter("energy", s.Energy);
        mat.SetShaderParameter("icon", (int)s.Weapon);
        mat.SetShaderParameter("radar_on", s.Radar);
        mat.SetShaderParameter("radar_count", s.Entities.Count);
        mat.SetShaderParameter("radar_players", s.Players);
        mat.SetShaderParameter("radar_wide", s.Maths32);
        var n = s.Graves ? s.GraveStore.Count : 0;                         // Panel.RadarDots: a dot for each grave not taken
        if (n > slots)
        {
            while (slots < n) slots *= 2;
            graves = new float[slots * 3];
            gravesBytes = new byte[slots * 3 * 4];
            gravesSent = [];
            gravesTex = ImageTexture.CreateFromImage(Image.CreateEmpty(slots, 1, false, Image.Format.Rgbf));
            mat.SetShaderParameter("graves_tex", gravesTex);
        }
        for (var i = 0; i < n; i++)
        {
            var g = s.GraveStore[i];
            graves[i * 3] = (ushort)g.X; graves[i * 3 + 1] = (ushort)g.Z; graves[i * 3 + 2] = g.Taken ? 1 : 0;
        }
        mat.SetShaderParameter("grave_count", n);
        if (n > 0)
        {
            Buffer.BlockCopy(graves, 0, gravesBytes, 0, gravesBytes.Length);
            if (!gravesBytes.AsSpan().SequenceEqual(gravesSent))
            {
                gravesTex.Update(Image.CreateFromData(slots, 1, false, Image.Format.Rgbf, gravesBytes));
                gravesSent = (byte[])gravesBytes.Clone();
            }
        }
        mat.SetShaderParameter("lamp_state", s.Panel.LampBits);
        mat.SetShaderParameter("lamp_drawn", s.Panel.LampDrawn);
        mat.SetShaderParameter("lamp_lit", s.Panel.LampLit);
        mat.SetShaderParameter("view0_raw", pal.View[0]);
        mat.SetShaderParameter("fade_level", pal.Fade);
        mat.SetShaderParameter("panel_level", pal.Panel);
        mat.SetShaderParameter("smooth_level", pal.Smooth);
        mat.SetShaderParameter("death_step", pal.Death);
        mat.SetShaderParameter("pointer_at", new Vector2I(pointerX, pointerY));
        mat.SetShaderParameter("pointer_colour", (int)pointerColour);
        mat.SetShaderParameter("pointer_on", pointerOn);
        var px = s.Panel.Pixels.Pixels;
        var changed = !msgSent;
        for (var row = 41; row < 59; row++)                                // the message window's text plane (rows 41-58)
            for (var x = 0; x < 320; x++)
            {
                float v = px[row * 320 + x] & 1;
                if (msg[row * 320 + x] != v) { msg[row * 320 + x] = v; changed = true; }
            }
        if (!changed) return;
        msgSent = true;
        Buffer.BlockCopy(msg, 0, msgBytes, 0, msgBytes.Length);
        msgTex.Update(Image.CreateFromData(320, Rules.Panel.Rows, false, Image.Format.Rf, msgBytes));
    }
}
