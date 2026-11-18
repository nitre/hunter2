namespace Hunter.Rules;

/// <summary>(setting sun_shading, the card-drawn 3D view): the sun lights every face and
/// ground square from its own angle to it, using only the game's own colours. The models' face colours hold no
/// light - each is just its paint - so a face's brightness comes from the sun alone: the paint's own ramp of shades
/// in the view palette (bank 2) and their Extra Half-Brite copies (each channel halved, as the Amiga shows colour
/// n + 32), and the face takes the shade nearest its paint's brightness times the sun's light on it. A ground square
/// keeps its own colour, in full colour, times its light (GroundLight), so the checker always shows. This is the
/// reference the card's shaders (godot/real3d/sunshade.gdshaderinc) are checked against, number for number.</summary>
public static class SunShade
{
    /// <summary>The ramps of one colour in bank 2, dark to light by the palette's own words ($0242 $0484 $08C8 $0CFC
    /// greens, $0800 $0A00 reds, $0521 $0732 $0A53 browns, $0130 $0251 $0372 dark greens; yellow and grey alone).
    /// 0 (black) and 15 (the sky) are not lit.</summary>
    public static readonly int[][] Families = [[1, 2, 3, 4], [6, 5], [7], [8], [9, 10, 11], [12, 13, 14]];

    public static int[]? FamilyOf(int colour) => Array.Find(Families, f => Array.IndexOf(f, colour) >= 0);

    /// <summary>A $0RGB word at half brightness (Extra Half-Brite: each 4-bit channel shifted right once).</summary>
    public static int Half(int rgb) => rgb >> 1 & 0x777;

    /// <summary>Brightness of a $0RGB word, whole numbers (0.299 / 0.587 / 0.114 by the thousand).</summary>
    public static int Luma(int rgb) => 299 * (rgb >> 8 & 15) + 587 * (rgb >> 4 & 15) + 114 * (rgb & 15);

    /// <summary>The sun's light on a face, in 64ths: 64 (the original's own colour) edge-on, 83 full on, 45 turned
    /// straight away - faces of every direction average the original (the old 32 + 48k averaged 0.69). k = the cosine between the face's outward direction and the direction to the
    /// sun, taken to 1/256.</summary>
    public static int Light64(float k)
    {
        var kq = Math.Clamp((int)MathF.Floor(k * 256f + 0.5f), -256, 256);
        return 64 + (77 * kq + 512 >> 10);
    }

    /// <summary>The colour's ramp, dark to light: every member and its half-bright copy as (index, half, word), ties in
    /// brightness kept in the order found. Empty for black and the sky.</summary>
    public static List<(int Index, bool Half, int Rgb)> Ladder(IReadOnlyList<ushort> palette, int colour)
    {
        var list = new List<(int Index, bool Half, int Rgb)>();
        if (FamilyOf(colour) is not { } fam) return list;
        foreach (var i in fam) list.Add((i, false, palette[i]));
        foreach (var i in fam) list.Add((i, true, Half(palette[i])));
        return list.OrderBy(e => Luma(e.Rgb)).ToList();
    }

    /// <summary>The shade a face of this paint takes with the sun's light k on it: the ladder entry whose brightness is
    /// nearest the paint's own times Light64 (a tie: the brighter, then the lower colour, then the full one). Black and
    /// the sky stay as they are.</summary>
    public static (int Index, bool Half) Pick(IReadOnlyList<ushort> palette, int colour, float k)
    {
        var ladder = Ladder(palette, colour);
        if (ladder.Count == 0) return (colour, false);
        return ladder[Position(ladder, Luma(palette[colour]) * Light64(k))] is var e ? (e.Index, e.Half) : default;
    }

    /// <summary>ENHANCEMENT (the 3D view's painted light, sunshade.gdshaderinc sun_pick_painted): a wall whose paint has
    /// the artists' light in it, kp its light from their sun (PainterSun) - its own brightness times how much more or
    /// less light the real sun gives it than theirs did. With the sun where they painted it, its own paint.</summary>
    public static (int Index, bool Half) PickPainted(IReadOnlyList<ushort> palette, int colour, float k, float kp)
    {
        var ladder = Ladder(palette, colour);
        if (ladder.Count == 0) return (colour, false);
        return ladder[Position(ladder, Luma(palette[colour]) * Light64(k) * 64 / Light64(kp))] is var e ? (e.Index, e.Half) : default;
    }

    /// <summary>ENHANCEMENT: a face lighter than its ramp's lightest paint - the sun asks for more than
    /// the palette has (light green 4, red 5, yellow 7, grey 8, brown 11, dark green 14 in full sun). Pick then gives
    /// the lightest paint; this is what the card multiplies it by in full colour, as the ground (GroundLight): the
    /// asked-for brightness over the lightest's, at most Light64(1) / 64. 1 when the ramp has the shade.</summary>
    public static double Over(IReadOnlyList<ushort> palette, int colour, float k) =>
        OverTarget(palette, colour, Luma(palette[colour]) * Light64(k));

    /// <summary>Over for a painted wall (PickPainted's brightness).</summary>
    public static double OverPainted(IReadOnlyList<ushort> palette, int colour, float k, float kp) =>
        OverTarget(palette, colour, Luma(palette[colour]) * Light64(k) * 64 / Light64(kp));

    static double OverTarget(IReadOnlyList<ushort> palette, int colour, int target64)
    {
        if (FamilyOf(colour) is not { } fam) return 1;
        var top = fam.Max(i => Luma(palette[i]));
        return target64 <= 64 * top ? 1 : Math.Min((double)target64 / (64 * top), Light64(1) / 64.0);
    }

    // (A lit ground square keeps the original's own colour, its fake sun's lighter B / E on slopes falling east included
    // (Terrain.Point $CF28). Swapping those back to the dark 9 / C, as GroundColour did till 11-08, took two thirds of
    // The light off the slopes the original lights and made the 3D view far darker than it -.)

    /// <summary>The sun on a ground POINT: the land's slope there from the
    /// heights one square either side (west / east, z - 512 / z + 512), x and z doubled as the picture's units; the
    /// card blends the points' light across each triangle, so neighbouring squares share it along their edge.</summary>
    public static float GroundK(int hl, int hr, int hb, int hf, (float X, float Y, float Z) sun)
    {
        float x = hl - hr, z = hb - hf, len = MathF.Sqrt(x * x + 2048f * 2048f + z * z);
        return (x * sun.X + 2048f * sun.Y + z * sun.Z) / len;
    }

    /// <summary>What a lit ground point's own colour is multiplied by: the sun's light, Light64 / 64 (RELIEF / VARIETY's extra step went with them, retired 11-01).
    /// The card multiplies in full colour, then hazes (world_colour.gdshaderinc world_lit_hazed).</summary>
    public static double GroundLight(float k) => Light64(k) / 64.0;

    /// <summary>What a lit ground point is drawn with: its light
    /// over flat ground's (flatK = the sun's height, Toward's Y), so flat land is the original's own colour and only
    /// slopes go lighter or darker. Ground faces up, so GroundLight alone never fell below 1 - 1.3 at midday.</summary>
    public static double GroundLevel(float k, float flatK) => GroundLight(k) / GroundLight(flatK);

    static int Position(List<(int Index, bool Half, int Rgb)> ladder, int target64)
    {
        var best = 0;
        for (var i = 1; i < ladder.Count; i++)
        {
            int d = Math.Abs(64 * Luma(ladder[i].Rgb) - target64), bd = Math.Abs(64 * Luma(ladder[best].Rgb) - target64);
            if (d < bd || (d == bd && Better(ladder[i], ladder[best]))) best = i;
        }
        return best;
    }

    static bool Better((int Index, bool Half, int Rgb) a, (int Index, bool Half, int Rgb) b) =>
        Luma(a.Rgb) != Luma(b.Rgb) ? Luma(a.Rgb) > Luma(b.Rgb) : a.Index != b.Index ? a.Index < b.Index : !a.Half && b.Half;

    /// <summary>The direction TO the sun, unit length, in the picture's own units (x east, y up, z north - the camera
    /// units where heights and the doubled x and z are alike, Objects.ProjectModelFlat): from Objects.SunOffset, the way
    /// the shadows fall, turned round, and its elevation. Null at night or with no world.</summary>
    public static (float X, float Y, float Z)? Toward(WorldState? s)
    {
        if (Objects.SunOffset(s) is not { } sun) return null;
        var elev = Math.Atan(sun.TanElev);
        return ((float)(-sun.DirX * Math.Cos(elev)), (float)Math.Sin(elev), (float)(-sun.DirZ * Math.Cos(elev)));
    }

    /// <summary>ENHANCEMENT (the 3D view's painted light): the sun the original's artists painted into their models - from
    /// the south-east, the east walls the lighter shade (godot/real3d/PaintedLight.cs) - as the game's own sun at 09:30
    /// (SunOffset: halfway from due east to due south, 45 degrees up), toward it as Toward.</summary>
    public static (float X, float Y, float Z) PainterSun
    {
        get
        {
            double frac = 210 / 840.0, elev = (10 + 50 * Math.Sin(Math.PI * frac)) * Math.PI / 180, az = Math.PI * frac;
            return ((float)(Math.Cos(az) * Math.Cos(elev)), (float)Math.Sin(elev), (float)(-Math.Sin(az) * Math.Cos(elev)));
        }
    }

    /// <summary>How far a corner h above the ground throws its shadow away from the sun (own shadows,
    /// Objects.ProjectModelFlat): h / tan(elevation), no cap; nothing for a corner on or under the ground.</summary>
    public static double ShadowLength(double h, double tanElev) => h > 0 ? h / tanElev : 0;
}
