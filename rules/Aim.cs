namespace Hunter.Rules;

/// <summary>
/// (AIMING: RETICLE; play input, never the demo or a film). On foot the player's soldier turns to the point under a free reticle and his shots go there.
/// The reticle is a game pixel (ReticleX, ReticleY) over the view. The camera is the original's: it looks north only, so the
/// picture's projection is Objects.Persp ($52A2: screen x = cx * 256 / (cz + $100) + CentreX, screen y = (Height0E - y) * 256 /
/// (cz + $100) + $2E, with cx = 2x - G1548 and cz = 2z - G1544, Objects.Place $41FC). The ray from the eye (cz = -$100, y = Height0E)
/// through the reticle is walked in steps; the first person's box (Collisions.Overlap's: half widths +$6E / +$72, y from +$4C to
/// +$4C + 2 * +$70) or the ground (Ground.SmoothHeight, the shots' own) it meets is the point. Above the horizon the ray meets nothing
/// and the point is the ray's far end. The soldier turns at the original's rate (MoveAndTurn $29FA: 1 &lt;&lt; (+$3C and 63) per tick on
/// +$7E); a shot is turned (+$7E high byte, bearing) and tilted (+$7C high byte, chosen from the shot's own Vector / gravity so it
/// ends on the point's height) when it is made. Only the original's 256 headings exist, so a far shot can miss by up to a few units.
/// </summary>
public static class Aim
{
    public readonly record struct Point(double X, double Y, double Z, bool Thing);

    /// <summary>The ray's step, in camera units (world / 2 sideways and in depth), and its length (the view's depth $1800 + the eye's).</summary>
    const int Step = 0x10, Far = 0x1A00;

    /// <summary>Reticle aiming is on for this pass of this record: the flag, a reticle, the player's soldier on foot (slot 0, d7
    /// bit 15), the original's north-looking camera, not the demo.</summary>
    public static bool Active(WorldState s, int slot, int d7) =>
        s.AimReticle && s.ReticleX >= 0 && slot == 0 && (d7 & 0x8000) != 0 && !s.DemoMode && s.Camera.Angle == 0;

    /// <summary>The world point under the reticle (world words, y in the +$4C units), or null with no view.</summary>
    public static Point? Target(WorldState s, int rx, int ry)
    {
        var c = s.Camera;
        double u = (rx - c.CentreX) / 256.0, v = (ry - 0x2E) / 256.0;       // Persp's inverse: x / depth, y / depth
        double px = 0, py = 0, pz = 0, pt = 0, ty = 0;
        for (var t = 0x40; t <= Far; t += Step)
        {
            var wx = (u * t + c.G1548) / 2;                                  // Place: cx = 2x - G1548
            var wz = (t - 0x100 + c.G1544) / 2.0;                            // cz = 2z - G1544, depth = cz + $100 = t
            var wy = c.Height0E - v * t;
            if (Thing(s, wx, wy, wz) is { } who)                             // a person: his middle sideways and in depth, the height the ray met him at
                return new Point(who.UW(0x4A), wy, who.UW(0x4E), true);
            var g = s.Ground.SmoothHeight((ushort)(int)Math.Round(wx), (ushort)(int)Math.Round(wz));
            if (wy <= g && t > 0x40)                                         // through the ground: between the last step and this
            {
                double lo = pt, hi = t;
                for (var i = 0; i < 10; i++)
                {
                    var m = (lo + hi) / 2;
                    var mx = (u * m + c.G1548) / 2; var mz = (m - 0x100 + c.G1544) / 2.0; var my = c.Height0E - v * m;
                    if (my <= s.Ground.SmoothHeight((ushort)(int)Math.Round(mx), (ushort)(int)Math.Round(mz))) hi = m; else lo = m;
                }
                var fx = (u * hi + c.G1548) / 2; var fz = (hi - 0x100 + c.G1544) / 2.0;
                return new Point(fx, s.Ground.SmoothHeight((ushort)(int)Math.Round(fx), (ushort)(int)Math.Round(fz)), fz, false);
            }
            (px, py, pz, pt, ty) = (wx, wy, wz, t, wy);
        }
        return new Point(px, py, pz, false);                                 // the sky: the ray's far end
    }

    static Entity? Thing(WorldState s, double wx, double wy, double wz)
    {
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            if (!(e.Alive && e.W(4) is >= 0x1E and <= 0x2F) || e.B[0x1C] == 0) continue;               // people in view (the cart / the box of a vehicle: not yet)
            var dx = (short)((ushort)(int)Math.Round(wx) - e.UW(0x4A)); var dz = (short)((ushort)(int)Math.Round(wz) - e.UW(0x4E));
            if (Math.Abs(dx) > e.W(0x6E) || Math.Abs(dz) > e.W(0x72)) continue;
            if (wy >= e.W(0x4C) && wy <= e.W(0x4C) + 2 * e.W(0x70)) return e;
        }
        return null;
    }

    static (double Dx, double Dy, double Dz) From(WorldState s, double x, double y, double z, Point t) =>
        ((short)((ushort)(int)Math.Round(t.X) - (ushort)(int)Math.Round(x)), t.Y - y, (short)((ushort)(int)Math.Round(t.Z) - (ushort)(int)Math.Round(z)));

    static int HeadingByte(double dx, double dz) => (int)Math.Round(Math.Atan2(dx, dz) * 128 / Math.PI) & 255;   // 0 = +z, $40 = +x

    /// <summary>The soldier's turn this pass: +$7E (word) toward the bearing of the point from his gun, by the original's step per
    /// tick (MoveAndTurn $29FA, a held turn key's) - never past it.</summary>
    public static void Turn(WorldState s, Entity e, int ticks)
    {
        if (Target(s, s.ReticleX, s.ReticleY) is not { } t) return;
        var (dx, _, dz) = From(s, e.UW(0x4A) + e.W(0x62), 0, e.UW(0x4E) + e.W(0x66), t);
        if (Math.Abs(dx) < 4 && Math.Abs(dz) < 4) return;                    // right on him: no bearing
        var want = HeadingByte(dx, dz) << 8;
        var diff = (short)(want - e.W(0x7E));
        var shift = e.W(0x3C) & 63;
        var step = (shift >= 16 ? 0 : 1 << shift) * ticks;
        if (step == 0) return;
        e.SetW(0x7E, Math.Abs(diff) <= step ? want : e.UW(0x7E) + Math.Sign(diff) * step);
    }

    /// <summary>The shot just made by the player's soldier (Weapons.Fire's record) goes to the point: bearing into +$7E, a pitch
    /// byte chosen over the 256 of them so that, with the shot's own speed and gravity (Settle $2AF2: y falls +$3A each tick), it
    /// is at the point's height when it has gone the horizontal way. Returns the point (null when nothing was changed).</summary>
    public static Point? Redirect(WorldState s, Entity shooter, Entity? shot, int slot, int d7)
    {
        if (!Active(s, slot, d7) || shot is null || shot == shooter || !Ground.IsShot(shot) || shot.W(0x2C) != shooter.W(0x2C)) return null;
        if (Target(s, s.ReticleX, s.ReticleY) is not { } t) return null;
        double x0 = shot.UW(0x4A), y0 = shot.W(0x4C), z0 = shot.UW(0x4E);
        var (dx, dy, dz) = From(s, x0, y0, z0, t);
        var run = Math.Sqrt(dx * dx + dz * dz);
        if (run < 4) return null;
        shot.B[0x7E] = (byte)HeadingByte(dx, dz);
        var speed = (short)shot.W(0x3E);
        var best = 0; var bestErr = double.MaxValue;
        for (var p = 0; p < 256; p++)
        {
            shot.B[0x7C] = (byte)(p + shot.B[0x39]);
            var (vx, vy, vz) = Movement.Vector(s.Trig, shot, speed);
            var vh = Math.Sqrt((double)vx * vx + (double)vz * vz);
            if (vh < 1 || vx * dx + vz * dz <= 0) continue;                  // it must go the way of the bearing, not back along it
            var err = Math.Abs((vy - shot.W(0x3A)) / vh * run - dy);        // height at the run, against the point's
            if (err < bestErr) { bestErr = err; best = p; }
        }
        shot.B[0x7C] = (byte)(best + shot.B[0x39]);
        if ((shot.Flags >> 24 & 1) != 0)                                     // $3302 Ahead: the velocity from heading, pitch, speed
        {
            var (vx, vy, vz) = Movement.Vector(s.Trig, shot, speed);
            shot.SetW(0x5C, vx); shot.SetW(0x5E, vy); shot.SetW(0x60, vz);
        }
        return t;
    }
}
