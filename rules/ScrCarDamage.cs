namespace Hunter.Rules;

/// <summary>STUNTIN': the original's damage, ported from the original program: updateDamageAndTimers ($5DFB4) - the hits'
/// target, the holes and the impact sounds - and updateDamageBarHUD ($66106): the shown damage climbs to the target one
/// crack at a time, each crack drawn on the damage bar's picture at the bar's random row; a crack that lands on a pixel
/// already empty (an old crack, a hole) adds one to the target; past $EF, or the target wrapping, wrecks
/// (initiateCarWreck). The bar is the race screen's top rows (game_data/scr/damage_bar.bin: the program's picture and its
/// segment pictures, LoadBar), kept per car.</summary>
public sealed partial class ScrCar
{
    public const int
        accumulatedCarDamage = 0x1BB55, targetDamageLevel = 0x1BBB6, damageBarVerticalOffset = 0x662AA,
        damageBarColor = 0x662AB, wheelBaseHeight = 0x1BBDD, wreckValue1BC56 = 0x1BC56, sampleCreakVolume = 0x1B715;

    /// <summary>The bar's rows (colour 0-15 a pixel, 320 a row): rows -2..15 (the cracks' marks reach two rows up).</summary>
    readonly byte[] _bar = new byte[18 * 320];
    (int W, int H, int X, int Y, ushort[] Data)[] _segments = [];
    int Px(int row, int x) => (row + 2) * 320 + x;

    /// <summary>The race screen's top rows and the segment pictures (damage_bar.bin), then the bar drawn with the holes it
    /// has (setupDamageBar / renderAllDamage).</summary>
    public void LoadBar(byte[] f)
    {
        Array.Clear(_bar);
        Array.Copy(f, 0, _bar, Px(0, 0), 16 * 320);
        var k = 16 * 320;
        var segs = new List<(int, int, int, int, ushort[])>();
        for (var o = 0; o < 6; o++)
        {
            int Wd(int at) => f[at] << 8 | f[at + 1];
            int w = Wd(k), h = Wd(k + 2), x = Wd(k + 4), y = Wd(k + 6), n = Wd(k + 8);
            var data = new ushort[n * 5];
            for (var i = 0; i < data.Length; i++) data[i] = (ushort)Wd(k + 10 + 2 * i);
            segs.Add((w, h, x, y, data));
            k += 10 + 10 * n;
        }
        _segments = [.. segs];
        for (var d2 = 9; d2 >= 0; d2--) Segment(d2 >= B(holeRenderingPosition) ? 0x17 : 0x1B, d2);
    }

    /// <summary>renderDamageBarSegment ($6092E): segment d2 of the bar (object $17 an old hole, $19 a new one, $1B whole) at
    /// word (3 x d2 + 6) / 2, the odd half-step's picture one on.</summary>
    void Segment(int obj, int d2)
    {
        if (_segments.Length == 0) return;
        var v = 3 * d2 + 6;
        if ((v & 1) != 0) obj++;
        var (w, h, _, y, data) = _segments[obj - 0x17];
        var xw = (v >> 1) & 0xFF;
        var i = 0;
        for (var r = 0; r <= h; r++)
            for (var c = 0; c <= w; c++)
            {
                var mask = data[i++];
                var planes = new[] { data[i++], data[i++], data[i++], data[i++] };
                for (var b = 0; b < 16; b++)
                {
                    var x = (xw + c) * 16 + b;
                    if (x >= 320 || y + r > 15) continue;
                    var bit = 15 - b;
                    var col = _bar[Px(y + r, x)];
                    var keep = (mask >> bit & 1) != 0;
                    var n = 0;
                    for (var p = 0; p < 4; p++) n |= ((keep && (col >> p & 1) != 0 ? 1 : 0) | (planes[p] >> bit & 1)) << p;
                    _bar[Px(y + r, x)] = (byte)n;
                }
            }
    }

    /// <summary>The bar's colour at a pixel (rows 0-15), for drawing it.</summary>
    public int BarPixel(int row, int x) => _bar[Px(row, x)];
    public void SetBarPixel(int row, int x, int c) => _bar[Px(row, x)] = (byte)c;

    /// <summary>updateDamageAndTimers ($5DFB4), after a pass: a hit this pass sets the target from the corners' hits and the
    /// bar climbs to it; then the holes (a hit of $1400 or more with holes left: a new hole and the major impact, $45 passes)
    /// and the creak. Returns the sample played (5 the major impact, 4 the creak) or -1.</summary>
    public int DamageStep()
    {
        var active = B(damageAccumulationActive) != 0;
        if (active)
        {
            var s = B(accumulatedForceFrontLeft) + B(accumulatedForceFrontRight);
            var t = s >> 1;                                                  // ADD.B, ROXR: the 9-bit sum halved
            s = t + B(accumulatedForceRear);
            B(targetDamageLevel, s >> 1);
            DamageBar();
        }
        int timer = B(majorImpactCooldownTimer);
        if (timer != 0)
        {
            B(majorImpactCooldownTimer, timer - 1);
            if (timer == 0x45) { Segment(0x17, B(holeRenderingPosition)); return Creak(); }
            return B(damageAccumulationActive) != 0 ? Creak() : -1;
        }
        if (B(damageAccumulationActive) == 0) return -1;
        if ((ushort)W(maxCompressionVelocity) >= 0x1400 && B(holeRenderingPosition) is var holes and > 0)
        {
            B(holeRenderingPosition, holes - 1);
            Segment(0x19, holes - 1);
            B(majorImpactCooldownTimer, 0x45);
            B(damageAccumulationActive, 0);
            return 5;
        }
        return Creak();
    }

    int Creak()
    {
        int v = B(maxCompressionVelocity);
        if (v < 7) v = 7;
        v = (v << 2) & 0xFF;
        if (v >= 0x40) v = 0x40;
        B(sampleCreakVolume, v);
        B(damageAccumulationActive, 0);
        return 4;
    }

    /// <summary>updateDamageBarHUD ($66106): the shown damage one crack at a time up to the target.</summary>
    void DamageBar()
    {
        while (true)
        {
            if (B(targetDamageLevel) <= B(accumulatedCarDamage)) return;
            B(accumulatedCarDamage, B(accumulatedCarDamage) + 1);
            int d2 = B(damageBarVerticalOffset);
            if ((B(accumulatedCarDamage) & 1) == 0)
            {
                var d0 = Random();
                var c = d0 & 1; d0 >>= 1;
                if (c != 0)
                {
                    c = d0 & 1; d0 >>= 1;
                    if (c != 0)
                    {
                        if (d2 < 5) d2++;                                    // $66156
                        else { c = d0 & 1; d0 >>= 1; if (c != 0) d2 = d2 - 1 + 1; else d2 = WalkDown(d2, d0); }
                    }
                    else d2 = WalkDown(d2, d0);
                }
            }
            B(damageBarColor, d2 > (sbyte)B(damageBarVerticalOffset) ? 0x0C : 0x0B);
            B(damageBarVerticalOffset, d2);
            if (B(accumulatedCarDamage) >= 0xF0)
            {
                B(accumulatedCarDamage, B(accumulatedCarDamage) - 1);
                Wreck();
                return;
            }
            var x = 32 + B(accumulatedCarDamage) + 8;
            var row = d2 & 7;
            if (_bar[Px(row, x)] == 0)
            {                                                                // on an empty pixel: a point more
                B(targetDamageLevel, B(targetDamageLevel) + 1);
                if (B(targetDamageLevel) == 0) { Wreck(); return; }
                for (var k = 2; k >= 0; k--) B(accumulatedForceFrontLeft + k, B(targetDamageLevel));   // resetDamageAccumulators
                continue;
            }
            _bar[Px(row, x)] = 0; _bar[Px(row - 1, x)] = 0;
            _bar[Px(row - 2, x)] = B(damageBarColor);
        }
    }

    /// <summary>$6615E: the row's walk down (with its one chance back up).</summary>
    static int WalkDown(int d2, int d0)
    {
        if (d2 >= 3) return d2 - 1;
        if ((d0 & 1) == 0) return d2 + 1;
        return d2 + 1 - 1;
    }

    /// <summary>initiateCarWreck ($5E848): once (no blink running): postWreckStateFlag 2 (no throttle, heavy drag), the
    /// drawn wheels' base $92, the blink $3C.</summary>
    void Wreck()
    {
        if (B(blinkCountdownTimer) != 0) return;
        B(postWreckStateFlag, 2);
        B(wheelBaseHeight, 0x92);
        B(wreckValue1BC56, 0x82);
        B(blinkCountdownTimer, 0x3C);
    }

    /// <summary>The car is wrecked (initiateCarWreck has run).</summary>
    public bool Wrecked => B(postWreckStateFlag) != 0;
}
