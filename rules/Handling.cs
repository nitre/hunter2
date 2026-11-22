namespace Hunter.Rules;

/// <summary>ENHANCEMENT (VEHICLE HANDLING: WEIGHTY, WorldState.WeightyHandling): the player's land vehicle has momentum and
/// a reverse gear. Its speed is no longer a number set a step a blank and held: up is the engine, letting go coasts to a
/// stop, down brakes and then backs up, and the slope under it pulls. Heavier machines gather and lose speed more slowly.
/// It still goes exactly where it points (no sliding) and turns as the original does while it moves - the other way
/// round going backward, as a steered vehicle does. The original's step is untouched for everything else, and with the
/// option off. The move itself goes through Settle ($2AF2), so the shore, blocked squares and the ground's height are the
/// game's own. Lengths in world units, time in blanks; the heading word is 65536 a turn (0 = +z, a quarter = +x).</summary>
public static partial class Movement
{
    /// <summary>The speed beside the record (Entity.Drive). Not saved: a load starts it again from the record's speed.</summary>
    public sealed class DriveBody
    {
        public double Speed;           // along the heading, negative backward
        public int Type = -1;          // the record this belongs to: its type and place as this left them
        public ushort AtX, AtZ;
    }

    /// <summary>How quickly each machine answers, 1 = the car: the engine's push and the brakes are this share of the
    /// car's (a bicycle is light and quick, a tank heavy and slow). By type (+$04); anything else on land drives as the car.</summary>
    static double Liveliness(int type) => type switch
    {
        0x11 => 1.6,                   // bicycle
        0x01 or 0x0D => 1.0,           // car, jeep
        0x02 => 0.7,                   // ambulance
        0x0F => 0.65,                  // armoured car
        0x00 or 0x12 => 0.55,          // truck
        0x03 or 0x04 => 0.4,           // tanks
        _ => 1.0,
    };

    const double
        Thrust = 0.03,                 // the car's push from rest as a share of its top speed, a blank (fading to 0 at the top speed)
        Brake = 0.05,                  // its brakes, the same way
        ReverseShare = 0.4,            // backward: this share of the top speed
        Roll = 0.012,                  // rolling drag, a blank (the engine makes it up, so the top speed stands)
        Downhill = 0.5,                // gravity along a slope of 1 in 1, a blank squared
        HoldSpeed = 0.12, HoldSlope = 0.05,   // stopped with no stick on a gentler slope than this: it stays put
        TurnFrom = 3.0;                // slower than this it turns in proportion (at a crawl the original spun nearly on the spot)

    /// <summary>The player's own land vehicle on the ground (+$06 bit 14: the kind that cannot turn standing still - wheels
    /// and tracks; not boats, not aircraft), a hand at the stick.</summary>
    static bool Weighty(WorldState s, Entity e, int slot, int d7) =>
        s.WeightyNow && slot == 0 && Bit(d7, 14) && e.B[0x1E] != 0;

    /// <summary>Whether Blank's throttle ($362E) leaves this record's speed word alone: Weighty sets it.</summary>
    public static bool WeightyDrives(WorldState s, Entity e) => s.WeightyNow && e.Flag(14) && e.Flag(27) && !e.Flag(15) && ReferenceEquals(e, s.Entities.All[0]);

    static Step WeightyStep(WorldState s, Entity e, int slot, int d7, int ticks)
    {
        var b = e.Drive ??= new DriveBody();
        ushort x = e.UW(0x4A), z = e.UW(0x4E);
        int top = Math.Max((int)e.W(0x36), 1);
        if (b.Type != e.W(4)) { b.Speed = e.W(0x3E); e.FracX = e.FracZ = 0; }             // just boarded (or loaded): moving as the record says
        else if (b.AtX != x || b.AtZ != z) { b.Speed = 0; e.FracX = e.FracZ = 0; }        // put back by a bump, or moved from outside: it stops
        b.Type = e.W(4);

        bool up = (s.DriveStick & 2) != 0, down = (s.DriveStick & 1) != 0;
        var motor = Bit(d7, 26);
        if (motor && (up || down))                               // fuel, as $2972: a blank a blank under power (twice with bit 4)
            e.SetW(0x34, Math.Max(e.W(0x34) - (Bit(d7, 4) ? ticks * 2 : ticks), 0));
        var dry = motor && e.W(0x34) <= 0;
        var lively = Liveliness(e.W(4));
        // the slope along its heading: the ground's own heights a little way ahead and behind (a height counts half a length)
        var heading = e.UW(0x7E) * Math.PI * 2 / 65536;
        double fx = Math.Sin(heading), fz = Math.Cos(heading);
        const int reach = 24;
        double H(double along) => s.Ground.Sample((ushort)(x + (int)Math.Round(fx * along)), (ushort)(z + (int)Math.Round(fz * along))).Height;
        var slope = (H(reach) - H(-reach)) / (4.0 * reach);
        double moved = 0;
        for (var n = 0; n < ticks; n++)
        {
            var v = b.Speed;
            double push = 0;
            if (up && !dry) push = Thrust * lively * top * Math.Max(1 - v / top, 0) + Roll * Math.Clamp(v, 0, top);
            else if (down && v > 0.3) push = -Brake * lively * top;
            else if (down && !dry) push = -(Thrust * lively * top * Math.Max(1 + v / (top * ReverseShare), 0) + Roll * Math.Clamp(-v, 0, top));
            b.Speed += push - Roll * v - Downhill * slope;
            if (down && v > 0.3 && b.Speed < 0) b.Speed = 0;     // the brakes stop it; they do not throw it backward
            moved += b.Speed;
        }
        if (!up && !down && Math.Abs(slope) < HoldSlope && Math.Abs(b.Speed) < HoldSpeed) b.Speed = 0;   // at rest
        // whole units go into the record, the rest is carried (FINE POSITIONS' own fields)
        static short Whole(double v, ref double carry) { v += carry; var w = Math.Floor(v); carry = v - w; return (short)w; }
        short dx = Whole(fx * moved, ref e.FracX), dz = Whole(fz * moved, ref e.FracZ);
        ushort wantX = (ushort)(x + dx), wantZ = (ushort)(z + dz);
        var step = Settle(s, e, slot, d7, dx, 0, dz, ticks);
        if (step.Ended) return step;
        if (e.UW(0x4A) != wantX || e.UW(0x4E) != wantZ) { b.Speed = 0; e.FracX = e.FracZ = 0; }   // the shore or a blocked square stopped it
        e.FracAt = ((short)e.W(0x4A), (short)e.W(0x4C), (short)e.W(0x4E));
        (b.AtX, b.AtZ) = (e.UW(0x4A), e.UW(0x4E));
        e.SetW(0x3E, Math.Clamp((int)Math.Round(b.Speed), 0, top));   // the speed the rest of the game reads (sound, panel): forward only
        // the turn ($29FE): the original's step while it moves, less at a crawl, the other way round going backward
        double lean = s.AnalogLean != 0 ? s.AnalogLean / 16.0 : Math.Sign((sbyte)e.B[0x18]);
        var shift = e.W(0x3C) & 63;
        if (lean != 0 && shift < 16 && b.Speed != 0)
        {
            var share = Math.Min(Math.Abs(b.Speed) / TurnFrom, 1) * Math.Sign(b.Speed);
            e.SetW(0x7E, e.UW(0x7E) + (int)Math.Round(lean * (1 << shift) * ticks * share));
            e.HeadingFine = e.UW(0x7E);
        }
        return step;
    }
}
