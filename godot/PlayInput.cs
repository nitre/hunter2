using Godot;

namespace Hunter;

/// <summary>
/// The game's keys in play: a Godot key -> the Amiga raw key code the original's keyboard interrupt
/// hands to Play.KeyDown ($B214). The raw codes are the original's own, not guessed: its ASCII table
/// at $B33A (raw -> ASCII, +$70 the shifted characters) is in game_data/main.bin and was read out
/// for every code 0-127; re/formats/input.md names the same keys and its raw codes ($37 'm', $44
/// Return, $45 Esc, $4C/$4D the cursors, "crlmfa~" at $A806) agree with it.
///
/// Only the keys the original does something with are here. Its ASCII is looked up as
/// $B33A[raw + (shift ? $70 : 0)], so the raw code is the UNSHIFTED one and shift is passed
/// separately (Keys.Down): the two keys the table uses ($4C/$4D, the cursors) are the zoom keys
/// ($3578 reads their key STATE $B29A+$4C/$4D), not KeyDown at all, so the arrows are not in the
/// table (JoystickInput.Zoom already reads them). Every other raw code the original's interrupt
/// sees (letters with no handler, the numeric keypad's operator keys, function keys) reaches
/// PlayKeys and does nothing, so leaving them unwired changes nothing in play.
/// </summary>
public static class PlayInput
{
    /// <summary>Godot key -> Amiga raw code, with what the key does in play. 'c' 'r' 'l' 'm' 'f'
    /// 'a' '~' are ItemHotkeys' table ($A806 = "crlmfa~"): each does what a click on that carried
    /// item's cell of the item table does, and does nothing when the item is not carried. Help is
    /// the same in plain words and Group the F1 list's section (HelpOverlay), both for the player:
    /// built into the table so the list is never a second copy of it.</summary>
    public static readonly (Key Key, byte Raw, string What, string Help, string Group)[] Table =
    [
        (Key.Space,  0x40, "leave a vehicle ($A766; the key's ASCII is $20)",
            "get out (in the air: parachute)", "Vehicles"),
        (Key.Enter,  0x44, "next weapon ($3538)", "next weapon", "Weapons"),
        (Key.Escape, 0x45, "quit: the in-game options screen ($351C)",
            "the in-game options screen", "Screens"),
        (Key.P,      0x19, "pause ($B258)", "pause (P again plays on)", "Screens"),
        (Key.C,      0x33, "clock ($A702, item 10)", "clock on / off", "Items"),
        (Key.R,      0x13, "radar ($3540, item 11)", "radar on / off", "Items"),
        (Key.L,      0x28, "log book ($A70C, item 12)", "log book", "Items"),
        (Key.M,      0x37, "map marks ($354A, item 13; input.md's raw $37)",
            "map marks on / off", "Items"),
        (Key.F,      0x23, "flare ($A79C, item 16)", "light a flare", "Items"),
        (Key.A,      0x20, "drone ($A776, item 17)", "drone (opens the map 100 blanks later)", "Items"),
        (Key.Quoteleft, 0x5F, "the hover ($A714, item 9; the original's key '~')",
            "hover: level off, hold speed", "Vehicles"),
    ];

    /// <summary>The Amiga raw code for a Godot key, or null: a key the original reads in play but
    /// that has no PC equivalent, or one it ignores.</summary>
    public static byte? Raw(Key key)
    {
        foreach (var (k, raw, _, _, _) in Table)
            if (k == key) return raw;
        return null;
    }
}
