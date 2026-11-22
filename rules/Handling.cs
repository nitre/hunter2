namespace Hunter.Rules;

/// <summary>ENHANCEMENT (VEHICLE HANDLING: WEIGHTY, WorldState.WeightyHandling): the player's car as a body with its own
/// velocity and spin on the ground's plane, pushed by its engine, the slope under it and two tyres - one at each axle -
/// that roll freely along their heading and resist sliding sideways up to the ground's grip (after the 2D rigid body and
/// tyre damping of the first Grand Theft Auto's cars). The original's step - a set speed along the heading, a fixed turn a
/// blank - is untouched for everything else, and with the option off. The move itself still goes through Settle ($2AF2),
/// so the shore, blocked squares and the ground's height are the game's own.
/// Lengths in world units, time in blanks, angles in radians (the record's heading word is 65536 a turn: 0 = +z, a
/// quarter turn = +x; right raises it).</summary>
public static partial class Movement
{
    /// <summary>The body beside the record (Entity.Drive). Not saved: a load starts it again from the record's speed.</summary>
    public sealed class DriveBody
    {
        public double Vx, Vz;          // velocity over the ground
        public double Spin;            // radians a blank, positive = turning right
        public double Heading;         // radians
        public double Steer;           // the front wheels' angle, eased toward the stick
        public int Type = -1;          // the record these belong to: its type, place and heading word as this left them
        public ushort AtX, AtZ, HeadWord;
    }

    // The car's numbers (type $01). Its top speed is the record's own (+$36, 9 a blank).
    const int HandlingType = 0x01;
    const double
        Thrust = 0.30,                 // the engine's push from rest, a blank squared (fading to 0 at the top speed)
        Brake = 0.45,                  // the brakes
        ReverseShare = 0.4,            // backward: this share of the top speed and of the push
        Roll = 0.012,                  // rolling drag on grass, a blank (the engine makes it up, so the top speed stands)
        SideGrip = 0.5,                // a tyre's pull against its sideways speed, a blank
        SideMax = 0.42,                // ...and the most it can pull on full grip, a blank squared: past it the car slides
        SteerSlow = 0.42, SteerFast = 0.21,   // the front wheels' full angle at rest / at the top speed
        SteerRate = 0.05,              // how fast they turn, a blank
        AxleShare = 0.7,               // each axle's distance from the middle, as a share of the box's half length (+$72)
        Downhill = 0.5,                // gravity along a slope of 1 in 1, a blank squared
        HoldSpeed = 0.12, HoldSlope = 0.05;   // stopped with no stick on a gentler slope than this: it stays put
    const int Substeps = 4;            // a blank's step in parts: the tyres' pull is stiff

    static bool Weighty(WorldState s, Entity e, int slot) =>
        s.WeightyNow && slot == 0 && e.W(4) == HandlingType && e.B[0x1E] != 0;   // the player's car, on the ground

    /// <summary>Whether Blank's throttle ($362E) leaves this record's speed word alone: the body sets it.</summary>
    public static bool WeightyDrives(WorldState s, Entity e) => s.WeightyNow && e.W(4) == HandlingType && ReferenceEquals(e, s.Entities.All[0]);

    /// <summary>How well tyres hold here, 0..1, and the extra rolling drag: grass 1, the brown high land a little less,
    /// sand (the beach, by the same distance from the sea and height that paint it - GroundBands.Colour) loose and
    /// heavy going.</summary>
    static (double Grip, double Drag) Surface(WorldState s, int x, int z, int h)
    {
        var map = s.Terrain.Map;
        var shore = ShoreDistance.FromSea(map, x, z, GroundBands.ShoreReach);
        var t = Math.Clamp(Math.Max(shore * 4.0 / GroundBands.ShoreReach, h * 4.0 / GroundBands.ShoreHigh), 0, 4);
        var sand = 1 - t / 4;
        var brown = GroundBands.Brown64(map, x, z) / 64.0;
        var land = 1 - 0.15 * brown;
        return (land + (0.55 - land) * sand, 0.035 * sand);
    }

    static Step WeightyStep(WorldState s, Entity e, int slot, int d7, int ticks)
    {
        var b = e.Drive ??= new DriveBody();
        ushort x = e.UW(0x4A), z = e.UW(0x4E), head = e.UW(0x7E);
        int top = Math.Max((int)e.W(0x36), 1);
        if (b.Type != e.W(4))                                    // just boarded (or loaded): moving as the record says
        {
            b.Heading = head * Math.PI * 2 / 65536;
            b.Vx = Math.Sin(b.Heading) * e.W(0x3E); b.Vz = Math.Cos(b.Heading) * e.W(0x3E);
            b.Spin = b.Steer = 0;
            e.FracX = e.FracZ = 0;
        }
        else if (b.AtX != x || b.AtZ != z)                       // put back by a bump, or moved from outside: it stops
        {
            b.Vx = b.Vz = b.Spin = 0;
            e.FracX = e.FracZ = 0;
        }
        if (b.HeadWord != head) b.Heading = head * Math.PI * 2 / 65536;   // turned from outside
        b.Type = e.W(4);

        bool up = (s.DriveStick & 2) != 0, down = (s.DriveStick & 1) != 0;
        double turn = s.AnalogLean != 0 ? s.AnalogLean / 16.0 : Math.Sign((sbyte)e.B[0x18]);
        var motor = Bit(d7, 26);
        if (motor && (up || down))                               // fuel, as $2972: a blank a blank under power (twice with bit 4)
        {
            var fuel = e.W(0x34) - (Bit(d7, 4) ? ticks * 2 : ticks);
            e.SetW(0x34, Math.Max(fuel, 0));
        }
        var dry = motor && e.W(0x34) <= 0;
        double half = Math.Max(e.W(0x72) * AxleShare, 8);
        double px = 0, pz = 0;                                   // moved this pass
        int ground = e.W(0x74);
        var (grip, drag) = Surface(s, x, z, ground);
        // the slope under it: the ground's own heights a little way each side (a height counts half a length)
        const int reach = 24;
        double H(int ox, int oz) => s.Ground.Sample((ushort)(x + ox), (ushort)(z + oz)).Height;
        double gx = (H(reach, 0) - H(-reach, 0)) / (4.0 * reach), gz = (H(0, reach) - H(0, -reach)) / (4.0 * reach);
        var steepness = Math.Sqrt(gx * gx + gz * gz);
        const double dt = 1.0 / Substeps;
        for (var n = 0; n < Substeps * ticks; n++)
        {
            double sin = Math.Sin(b.Heading), cos = Math.Cos(b.Heading);
            double fx = sin, fz = cos, rx = cos, rz = -sin;      // forward, right
            double vf = b.Vx * fx + b.Vz * fz;
            // the front wheels ease to the stick, less far the faster it goes
            var full = SteerSlow + (SteerFast - SteerSlow) * Math.Clamp(Math.Abs(vf) / top, 0, 1);
            var want = turn * full;
            b.Steer += Math.Clamp(want - b.Steer, -SteerRate * dt, SteerRate * dt);
            // engine and brakes, along the body, no more than the tyres can put down
            double push = 0;
            if (up && !dry) push = Thrust * Math.Max(1 - vf / top, 0) + Roll * Math.Clamp(vf, 0, top);
            else if (down && vf > 0.3) push = -Brake;
            else if (down && !dry) push = -(Thrust * ReverseShare * Math.Max(1 + vf / (top * ReverseShare), 0) + Roll * Math.Clamp(-vf, 0, top));
            push = Math.Clamp(push, -grip * 0.6, grip * 0.6);
            double ax = push * fx - (Roll + drag) * b.Vx - Downhill * gx, az = push * fz - (Roll + drag) * b.Vz - Downhill * gz;
            // the two tyres: each axle's own velocity, split along and across its wheel; the across part is pulled back
            double torque = 0;
            for (var axle = 0; axle < 2; axle++)
            {
                var at = axle == 0 ? half : -half;                // front, rear
                double wvx = b.Vx + at * b.Spin * rx, wvz = b.Vz + at * b.Spin * rz;
                var angle = b.Heading + (axle == 0 ? b.Steer : 0);
                double sx = Math.Cos(angle), sz = -Math.Sin(angle);   // across this wheel
                var side = wvx * sx + wvz * sz;
                var pull = Math.Clamp(-SideGrip * side, -SideMax * grip, SideMax * grip);
                ax += 0.5 * pull * sx; az += 0.5 * pull * sz;
                torque += at * 0.5 * pull * (sx * rx + sz * rz);
            }
            b.Vx += ax * dt; b.Vz += az * dt;
            b.Spin += torque / (half * half) * dt;
            b.Spin *= 1 - 0.03 * dt;
            b.Heading += b.Spin * dt;
            px += b.Vx * dt; pz += b.Vz * dt;
        }
        if (!up && !down && steepness < HoldSlope && Math.Sqrt(b.Vx * b.Vx + b.Vz * b.Vz) < HoldSpeed) b.Vx = b.Vz = b.Spin = 0;   // at rest
        b.Heading %= Math.PI * 2;
        // whole units go into the record, the rest is carried (FINE POSITIONS' own fields)
        static short Whole(double v, ref double carry) { v += carry; var w = Math.Floor(v); carry = v - w; return (short)w; }
        short dx = Whole(px, ref e.FracX), dz = Whole(pz, ref e.FracZ);
        ushort wantX = (ushort)(x + dx), wantZ = (ushort)(z + dz);
        var step = Settle(s, e, slot, d7, dx, 0, dz, ticks);
        if (step.Ended) return step;
        if (e.UW(0x4A) != wantX || e.UW(0x4E) != wantZ) { b.Vx = b.Vz = b.Spin = 0; e.FracX = e.FracZ = 0; }   // the shore or a blocked square stopped it
        e.FracAt = ((short)e.W(0x4A), (short)e.W(0x4C), (short)e.W(0x4E));
        var word = (ushort)(int)Math.Round((b.Heading < 0 ? b.Heading + Math.PI * 2 : b.Heading) * 65536 / (Math.PI * 2));
        e.SetW(0x7E, word); e.HeadingFine = word;
        (b.AtX, b.AtZ, b.HeadWord) = (e.UW(0x4A), e.UW(0x4E), word);
        var forward = b.Vx * Math.Sin(b.Heading) + b.Vz * Math.Cos(b.Heading);
        e.SetW(0x3E, Math.Clamp((int)Math.Round(forward), 0, top));   // the speed the rest of the game reads (sound, panel): forward only
        return step;
    }
}
