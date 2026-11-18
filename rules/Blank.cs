namespace Hunter.Rules;

/// <summary>
/// The game's work in each vertical blank while it runs ($C6DC -> $3578): the game clock, the
/// camera zoom keys and distance, and the player's speed on foot (re/formats/movement.md section 3).
/// </summary>
public static class Blank
{
    /// <summary>d6 = $D6 (joystick), d4 = $D4 (buttons, bit 0 fire) as the blank read them;
    /// SpeedControl may clear them before the pass reads them.</summary>
    public static void Run(WorldState s, Entity player, bool zoomOut, bool zoomIn, ref byte d6, ref byte d4)
    {
        // $3578: $216 bit 7 (soldier record +0) is clear in play
        if ((s.Status216 & 0x80) == 0 && --s.ClockWait < 0) { Clock(s); EachMinute(s, player); }   // $3578 btst #7,$216: dead, the clock stops; $3580 $C404, $358C $B054
        var step = zoomOut ? 1 : zoomIn ? -1 : 0;                            // $35A0: key table +$4C up, +$4D down
        if (s.InsideView) step = 0;                                          // $3592 btst #0,$99: inside, no zoom and no camera step
        if (step != 0)
        {
            var z = step + s.Zoom;                                           // $35B0
            if (z >= -30 && z <= 30)
            {
                s.Zoom = (short)z;
                var shift = (player.W(0x9C) + 4) & 63;                       // $35C4: $2EC = player +$9C
                var w = shift >= 16 ? 0 : (short)(z << shift);               // lsl.w, ext.l
                s.Target = s.Base + w;                                       // $35CE
            }
        }
        var diff = s.InsideView ? 0 : s.Target - s.Distance;                 // $35D6
        if (diff != 0)
        {
            s.Distance += diff > 0 ? s.Step : -s.Step;
            s.Lift = (short)((ushort)s.Distance >> 3);                       // $35EE lsr.w #3
        }
        SpeedControl(player, ref d6, ref d4);                                // $35F6
    }

    /// <summary>(mp_two_soldiers): the same SpeedControl ($35F6)
    /// on another player's record with HIS joystick, so his walk flag (and his vehicle throttle,
    /// fire press) reach HIS speed. The clock, the zoom and the camera distance stay the
    /// original's single shared work - only $35F6 is per player.</summary>
    public static void Speed(Entity player, ref byte d6, ref byte d4) => SpeedControl(player, ref d6, ref d4);

    /// <summary>BlankTimers ($6C46, before $3578): the panel lamps, then the timers of $A2: bit 4
    /// the alarm ($222; out: $A4 bit 1 cleared), bit 0 the flare ($18A; out: $A7 bit 4, FlareOut),
    /// bit 1 the drone ($188; out: $A7 bit 2, the map), bit 3 the hover ($6D54), bit 2 the end
    /// timer after the last death ($18C; out: GameOver $3514). d6 = $D6 as this blank read it.</summary>
    public static void Timers(WorldState s, byte d6)
    {
        s.Panel.Blank(s);                                                    // $6C46-$6C58: the lamps
        var d7 = s.Flags0A2;                                                 // $6C5C
        if (d7 == 0) return;
        if ((d7 & 0x10) != 0 && --s.Timer222 < 0)                            // $6C62
        {
            d7 &= 0xEF;
            s.Flags0A4 &= 0xFD;
        }
        if ((d7 & 1) != 0 && --s.Timer18A < 0) { d7 &= 0xFE; s.Flags0A7 |= 0x10; }   // $6C78
        if ((d7 & 2) != 0 && --s.Timer188 < 0) { d7 &= 0xFD; s.Flags0A7 |= 4; }      // $6C8E
        if ((d7 & 8) != 0 && !Hover(s, s.Entities.All[0], d6)) d7 &= 0xF7;   // $6CA4 $6D54
        s.Flags0A2 = d7;                                                     // $6CAE
        if ((d7 & 4) != 0 && --s.Timer18C < 0)                               // $6CB2
            Games.GameOver(s);                                               // $6CBC bmi $3514
    }

    // $6D54 ('~', $A714): a helicopter (+$08 bit 3) in the air (+$1E clear) with neither up nor down
    // on the stick: its pitch (+$7C) one step toward 0 and its speed (+$3E) one toward $26 each
    // blank; false (bclr #3,d7: the hover ends) otherwise
    static bool Hover(WorldState s, Entity p, byte d6)
    {
        if ((p.B[0x08] & 8) == 0 || (d6 & 3) != 0 || p.B[0x1E] != 0) return false;   // $6D54-$6D68
        var pitch = (sbyte)p.B[0x7C];                                        // $6D6A $2CC
        if (pitch != 0) p.B[0x7C] = (byte)(pitch < 0 ? pitch + 1 : pitch - 1);       // $6D70-$6D7A
        var v = p.W(0x3E);                                                   // $6D80 $28E
        if (v != 0x26) p.SetW(0x3E, v + (v > 0x26 ? -1 : 1));               // $6D84-$6D90
        return true;
    }

    // SpeedControl ($35F6): by the first set bit of +$08 from 7 down (vehicles.md section 4)
    static void SpeedControl(Entity p, ref byte d6, ref byte d4)
    {
        var b = p.B[0x08];
        if ((b & 0x80) != 0) { p.SetW(0x3E, p.B[0x19] != 0 ? p.W(0x36) : 0); return; }   // $3614 on foot
        if ((b & 0x60) != 0) { Throttle(p, d6); return; }                    // bits 6, 5 -> $362E
        if ((b & 0x18) != 0) { AirThrottleFromFire(p, ref d6, ref d4); return; }   // bits 4, 3 -> $3662
        if ((b & 0x04) != 0) { Throttle(p, d6); return; }                    // bit 2 -> $362E
        p.SetW(0x3E, p.W(0x36));                                             // $3626 top speed
    }

    // $362E: up +1, down -1 a blank; not for a motorised one with no fuel
    static void Throttle(Entity p, byte d6)
    {
        int d0;
        if ((d6 & 2) != 0) d0 = 1;
        else if ((d6 & 1) != 0) d0 = -1;
        else return;
        if (p.W(0x34) <= 0 && p.Bit(0x06, 26)) return;                       // $3648 btst #26 on +$06 = bit 2
        d0 += p.W(0x3E);
        if (d0 < 0 || d0 > p.W(0x36)) return;
        p.SetW(0x3E, d0);
    }

    // $3662: fire with exactly up (+1) or down (-1); the press is used up
    static void AirThrottleFromFire(Entity p, ref byte d6, ref byte d4)
    {
        if ((d4 & 1) == 0) return;
        int d0;
        var dir = d6 & 0xF;
        if (dir == 2) d0 = 1;
        else if (dir == 1) d0 = -1;
        else return;
        if (p.W(0x34) <= 0) { d4 = 0; return; }                              // $36A4 sf $D4
        d4 &= 0xFE;                                                          // $3688
        d6 = 0;                                                              // $368E
        d0 += p.W(0x3E);
        if (d0 < 0 || d0 > p.W(0x36)) { d4 = 0; return; }
        p.SetW(0x3E, d0);
    }

    // $B05C: bytes $1AC / $1AD of Timer1AC ($206 on going in) count minutes in the water
    static void Swimming(WorldState s, Entity player)
    {
        int hi = (sbyte)(s.Timer1AC >> 8) - 1, lo = (sbyte)s.Timer1AC;
        if (hi < 0)
        {
            hi += 4;
            if (!Vehicles.AddEnergy(s, -1))                                  // $B068 $A42C(-1)
            {
                player.B[0x2A] = 0xFF;                                       // $B06E st $27A: drowned
                s.Status216 |= 4;                                            // $B072: the death says "drowned!" ($2F3E)
            }
            if (--lo < 0)
            {
                lo += 4;
                Messages.Show(s, 0xB034);                                    // $B082 $B086 $C1B2: "getting tired!"
                s.Channels.Play(2, s.Entities);                              // $B08A $B116
            }
        }
        s.Timer1AC = (short)((hi & 0xFF) << 8 | lo & 0xFF);
    }

    /// <summary>EachMinute ($B054): in the water the swim countdown; then
    /// AmbientSound ($B08E): $1AA minutes to the next one, its sound by the player's height.</summary>
    static void EachMinute(WorldState s, Entity player)
    {
        if ((s.PlayerAt(s.PlayerOf(player)).Status219 & 0x40) != 0) Swimming(s, player);   // $B054 btst #6,$219: the blank's own player's byte (player 0 today)
        if (--s.AmbientWait >= 0) return;                                    // $B08E
        var at = 0xB044 - 0x800 + (player.W(0x4C) > 0x64 ? 0 : 8);
        var main = s.Data.Main;
        var sound = main[at] << 8 | main[at + 1];
        s.AmbientWait = (short)(main[at + 2] << 8 | main[at + 3]);
        var ch = s.Channels.Alloc(sound, s.Entities);                        // $B0B2
        if (ch >= 0) s.Channels.Set(sound, ch, 0x1F);
    }

    /// <summary>AdvanceClockMinute ($C404): a minute every 60 blanks, hours 0-23, days, each redrawn on
    /// the panel from the field that changed ($C480 minutes, PanelTimeAndLight $C45E, PanelClock $C43A).</summary>
    static void Clock(WorldState s)
    {
        s.ClockWait = 0x3B;                                                  // $C404
        if (++s.Minute <= 0x3B) { s.Panel.Minute(s); return; }
        s.Minute = 0;
        if (++s.Hour <= 0x17) { s.Panel.Hour(s); return; }
        s.Hour = 0;
        s.Day++;
        s.Panel.Clock(s);
    }

    /// <summary>$2F56 (from the player set-up $2E52): the camera's base distance from the size of
    /// the player's model (+$25): half-sizes from its header +4/+8 ($3346), the larger, << 4,
    /// + $20F, rounded down to 16. Target = base, step $10, zoom 0.</summary>
    public static void CameraBase(WorldState s, Models models, Entity player)
    {
        var h = models.Offsets[player.B[0x25]];
        player.SetW(0x9C, models.Buf[h + 0x11]);                             // $2F5A $4F54: the zoom shift
        int x = models.U(h + 4) >> 1, z = models.U(h + 8) >> 1;
        player.SetW(0x6E, x); player.SetW(0x70, models.U(h + 6)); player.SetW(0x72, z);   // $3366
        var d = (short)((z > x ? z : x) << 4);
        s.Base = s.Target = ((d + 0x200 + 0xF) & 0xFFFFF0);                  // $2F6A-$2F7C
        s.Step = 0x10;
        s.Zoom = 0;
    }
}
