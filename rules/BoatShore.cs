namespace Hunter.Rules;

/// <summary>(WorldState.BoatsAlongShore). The maps park most boats bow-in to the shore (64 of the 104 sea-only boats have land within
/// 128 ahead; 29 within 32), so a boat boarded from the beach goes nowhere until it is steered round. A parked boat that
/// comes to life (Builder.Spawn, $81EE) is turned along the shore instead: of the two headings square to the way the
/// land lies, the one with more open water ahead, the nearer to its own heading on a tie. No land near: left as it is.
/// The land test is the game's own ($36AA, Ground.Sample). Headings are the record's byte (+$0C): 0 north, 64 east.</summary>
public static class BoatShore
{
    /// <summary>How far round a boat land is looked for, and how far ahead open water is measured.</summary>
    public const int Reach = 512;
    const int Step = 16;

    /// <summary>The heading a parked boat at x,z with heading h is given; h itself when no land lies within Reach.</summary>
    public static byte Heading(Ground g, int x, int z, byte h)
    {
        double sx = 0, sz = 0;
        for (var k = 0; k < 32; k++)                                   // 32 directions: where the land lies, the nearer the more
        {
            var a = k * 8;
            var d = LandAt(g, x, z, (byte)a);
            if (d > Reach) continue;
            var w = Reach + Step - d;
            sx += Math.Sin(a * Math.PI / 128) * w;
            sz += Math.Cos(a * Math.PI / 128) * w;
        }
        if (sx == 0 && sz == 0) return h;
        var toLand = (int)Math.Round(Math.Atan2(sx, sz) * 128 / Math.PI) & 0xFF;
        byte left = (byte)(toLand - 64), right = (byte)(toLand + 64);
        int openL = LandAt(g, x, z, left), openR = LandAt(g, x, z, right);
        if (openL != openR) return openL > openR ? left : right;
        static int Turn(byte a, byte b) { var t = (byte)(a - b); return Math.Min(t, 256 - t); }   // 0-128 (a half turn, 128, has no signed byte)
        return Turn(left, h) <= Turn(right, h) ? left : right;
    }

    /// <summary>The distance along heading a to the first land, in steps of 16; Reach + 16 when there is none so near.</summary>
    static int LandAt(Ground g, int x, int z, byte a)
    {
        double s = Math.Sin(a * Math.PI / 128), c = Math.Cos(a * Math.PI / 128);
        for (var d = Step; d <= Reach; d += Step)
            if (g.Sample((ushort)(x + (int)Math.Round(s * d)), (ushort)(z + (int)Math.Round(c * d))).Land) return d;
        return Reach + Step;
    }
}
