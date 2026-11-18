namespace Hunter.Rules;

/// <summary>
/// The message line (re/formats/game.md section 2). Every message goes through $C1B2: the
/// formatter $B794 (arguments from d0, d1, ... in the order the template's codes use them) puts
/// ']' in front, so each message starts a new line; $C1BA prints it at the text cursor ($134 x,
/// $136 line) in the panel's text window (SetScreenMode(3): rows 41-58 of the panel, x 8-311, two
/// lines of 9 rows, plane 0). The cursor sits on the last line ($2556 $60C6), so the new line
/// scrolls the window ($C300: the bottom line moves up, 8 rows under it cleared) and the message
/// is drawn on the bottom line at once. Nothing times it out: it stays until two more messages
/// have scrolled it away. A second message in the same pass simply pushes the first one up.
/// </summary>
public static class Messages
{
    /// <summary>$C1B2: format the template at textAddr with args (d0, d1, ...) and print it.</summary>
    public static void Show(WorldState s, int textAddr, params int[] args)
    {
        s.Panel.Text.Printf(textAddr, args);
        s.Heard?.Invoke(textAddr, args);                                    // ENHANCEMENT: the bot hears it (WorldState.Heard)
    }

    /// <summary>$B688 (d7 = 0): a0 past the next 0 byte - the template after this one (the
    /// singular forms after $A284's "taken some" / "cannot carry any more").</summary>
    public static int Next(WorldState s, int textAddr)
    {
        var a = textAddr - 0x800;
        while (s.Data.Main[a++] != 0) { }
        return a + 0x800;
    }
}
