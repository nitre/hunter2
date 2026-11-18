namespace Hunter.Rules;

/// <summary>Mode 1's copper colour-8 rows - the title's dusk sky. The mode 1 copper list ($5CEC)
/// writes colour 8 ($DFF190) after each wait; the twelve (line, colour) pairs below were read
/// from hunter_en_800.bin (copper words $5D70-$5DC8; findings\copper.md has the addresses).
/// Screen row = wait line - $2C (the display window starts at DIWSTRT $2C80, line $2C), so the
/// copper's colour 8 covers every visible row and the picture palette's placeholder $0F0 never
/// shows. WriteFadedColours $6A4A re-writes each of these copper words with the faded value on
/// every fade step ($6C06 builds the offset table, $6AA0 writes it), so the sky fades in and
/// out with the picture. Mode 0 (credits, menus) has no colour-8 words - unchanged.</summary>
public static class TitleSky
{
    /// <summary>(screen row the band starts on, the copper's $0RGB colour 8 for it).</summary>
    public static readonly (int Row, ushort Colour)[] Bands =
    {
        (0, 0x0338), (61, 0x0438), (76, 0x0539), (85, 0x0639), (92, 0x072A), (99, 0x092A),
        (106, 0x0A2A), (110, 0x0B29), (115, 0x0B18), (119, 0x0C17), (123, 0x0C14), (125, 0x0D00),
    };

    /// <summary>The colour 8 the title shows at a screen row and fade level: the row's band's
    /// copper colour at the level ($6A4A's copper write, the same * level >> 4 as Play.Faded).</summary>
    public static ushort Colour8(int row, int level)
    {
        var c = Bands[0].Colour;
        for (var i = Bands.Length - 1; i >= 0; i--)
            if (row >= Bands[i].Row) { c = Bands[i].Colour; break; }
        return Play.Faded(c, level);
    }
}
