namespace Hunter.Rules;

/// <summary>the game's icon is the soldier screen's face - frame
/// $12 (the face Pregame.Panel blits at $0B,8, record byte 5 = 0) in the soldier screen's palette bank 6
/// (GameView.StartSoldierScreen). The frame is 48 x 49: a one-pixel frame of colour 14 round the face (rows 0 and 48,
/// columns 0 and 44) and three columns of colour 8 past it (45-47); the icon is the 43 x 47 face inside.
/// Built from the player's own game data at start-up, so no picture of the game is kept in the repo.</summary>
public static class GameIcon
{
    public const int Frame = 0x12, Bank = 6, X0 = 1, Y0 = 1, Width = 43, Height = 47;

    /// <summary>The loading screen (picture file 6): its whole picture - portrait and HUNTER logo - fills columns 66-254, rows 0-198, so the
    /// icon is the 199 x 199 square from column 61 (centred, the screen's own black around it). Used when
    /// game_data/pics/loadpic2.json exists; the soldier screen's face (Face) otherwise.</summary>
    public const int LoadingX0 = 61, LoadingY0 = 0, LoadingSide = 199;

    /// <summary>The loading screen's square as $0RGB colours, row by row (LoadingSide x LoadingSide), from the
    /// picture's 320-wide palette indices and its 16 colours.</summary>
    public static ushort[] Loading(byte[] pixels, ushort[] palette)
    {
        var o = new ushort[LoadingSide * LoadingSide];
        for (var y = 0; y < LoadingSide; y++)
            for (var x = 0; x < LoadingSide; x++)
                o[y * LoadingSide + x] = palette[pixels[(LoadingY0 + y) * 320 + LoadingX0 + x]];
        return o;
    }

    /// <summary>The face as $0RGB colours, row by row (Width x Height).</summary>
    public static ushort[] Face(Ui ui, IReadOnlyList<ushort[]> banks)
    {
        var f = ui.Frames[Frame];
        var bank = banks[Bank];
        var o = new ushort[Width * Height];
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                o[y * Width + x] = bank[f.Pixels[(Y0 + y) * f.Width + X0 + x]];
        return o;
    }
}
