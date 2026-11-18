namespace Hunter.Rules;

/// <summary>The joystick in port 2 as the game holds it: $D6(a5), built each blank by $C7E0
/// (re/formats/input.md). Bit 0 down, 1 up, 2 right, 3 left, 4 fire.</summary>
public readonly record struct Joystick(bool Up, bool Down, bool Left, bool Right, bool Fire)
{
    public byte D6 => (byte)((Down ? 1 : 0) | (Up ? 2 : 0) | (Right ? 4 : 0) | (Left ? 8 : 0) | (Fire ? 0x10 : 0));
    public static Joystick FromD6(byte d6) => new((d6 & 2) != 0, (d6 & 1) != 0, (d6 & 8) != 0, (d6 & 4) != 0, (d6 & 0x10) != 0);
    /// <summary>Two devices on one port: any contact closed on either is closed.</summary>
    public static Joystick Combine(Joystick a, Joystick b) =>
        new(a.Up || b.Up, a.Down || b.Down, a.Left || b.Left, a.Right || b.Right, a.Fire || b.Fire);
}

/// <summary>$25D8-$260C: the joystick into the player's record (entity 0) once per pass.</summary>
public static class Controls
{
    const int HeadingTable = 0x2632, SteerTable = 0x818;      // byte per direction; word per direction ($12(a5))

    /// <summary>viewAngle: ENHANCEMENT - the player's view turn (mp_view_finish, south.md B: player 2's
    /// camera faces south, $80; real 3D: any angle), so the stick is camera-relative: the on-foot
    /// heading byte adds it (stick up, table entry $00, walks world south at $80). One line, on foot
    /// only - vehicles and helicopters steer in their own frame (the $818 table untouched).</summary>
    /// <summary>The heading byte the stick's direction d6 &amp; 15 (1-15, the switches' bits) gives a soldier on foot ($25F6),
    /// with the view turn added as Apply adds it. The BOT (rules/Bot.cs) picks its stick from these.</summary>
    public static byte FootHeading(byte[] main, int dir, byte viewAngle = 0) => (byte)(main[HeadingTable - 0x800 + (dir & 15)] + viewAngle);

    public static void Apply(byte[] main, Entity player, byte d6, bool fire, byte viewAngle = 0)
    {
        player.SetW(0x18, 0);                                         // $25DC clr.w $268
        var dir = d6 & 15;                                            // $25E0
        if (dir != 0)
        {
            if (player.Bit(0x08, 15))                                 // $25EA btst.b #15 = bit 7 of +$08: on foot
            {
                player.B[0x19] = 0xFF;                                // $25F2 st $269: walk
                player.B[0x7E] = (byte)(main[HeadingTable - 0x800 + dir] + viewAngle);    // $25F6: face that side of the screen
            }
            else                                                      // $25FE: steer (+$18 turn, +$19 forward/back)
                player.SetW(0x18, main[SteerTable - 0x800 + 2 * dir] << 8 | main[SteerTable - 0x800 + 2 * dir + 1]);
        }
        player.B[0x1D] = (byte)(fire ? 0xFF : 0);                     // $2606 sne $26D
    }
}
