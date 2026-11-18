namespace Hunter.Rules;

/// <summary>(renderer = 3d): where the 3D view's eye stands, in world units (x
/// east, height units up, z north). From the game's camera: x = $1548 / 2, z = ($1544 - $100) / 2 - Persp's
/// depth puts the eye $100 doubled behind $1544 - and height $0E. The player stands (distance + $100) / 2
/// north of it ($1544 = 2z - distance). NEW, not the original: yaw (radians, 0 = north, positive turns left,
/// toward west) swings the eye round the player at the same distance and height.</summary>
public static class View3D
{
    public static (double X, double Y, double Z) Eye(Camera c, int distance, double yaw)
    {
        double x = c.G1548 / 2.0, z = (c.G1544 - 0x100) / 2.0;
        if (yaw == 0) return (x, c.Height0E, z);
        var px = x;
        var pz = z + (distance + 0x100) / 2.0;
        double dx = x - px, dz = z - pz;
        // a left turn (counter-clockwise seen from above, north = +z, east = +x) moves the eye from south to east
        return (px + dx * Math.Cos(yaw) - dz * Math.Sin(yaw), c.Height0E, pz + dx * Math.Sin(yaw) + dz * Math.Cos(yaw));
    }

    /// <summary>NEW, not the original (the showcase's free camera): world point (x, y height, z) as the eye sees it,
    /// the eye turned by yaw and tilted down by pitch (radians). Godot's camera axes: x right, y up, -z ahead;
    /// heights halved, as the 3D view shows them.</summary>
    public static (double X, double Y, double Z) Relative((double X, double Y, double Z) eye, double yaw, double pitch,
        double x, double y, double z)
    {
        double dx = x - eye.X, dy = (y - eye.Y) * 0.5, dz = eye.Z - z;
        double c = Math.Cos(yaw), s = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        double x1 = c * dx - s * dz, z1 = s * dx + c * dz;
        return (x1, cp * dy - sp * z1, sp * dy + cp * z1);
    }
}
