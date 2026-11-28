namespace Hunter.Rules;

/// <summary>STUNTIN': THE RAIL over a big gap, for computer-driven cars only (the AI cars and the autopilot's) - the original's computer car ran on rails and made
/// every jump; ours is a real car, and Big Ramp's gap (a climb to a lip, a floor 37 cross-sections long, a landing slope 470
/// lower) is past what a standard-league car reaches: boosted all the way up, ours and the original's both leave the lip at
/// 19500-21000 and come down short (docs/scr-physics.md, the emulator's own run). A GAP: a listed jump's lip where the road
/// drops by GapDrop or more, and its landing edge, the first cross-section on that rises GapDrop over the floor again. A car
/// that leaves the lip at no less than the jump's listed speed byte less RailBelow (x 256) is guided: each pass over the gap
/// its upward speed is raised, when it is short, to what reaches the edge with Margin to spare over the distance left - never
/// lowered (a car flying well is not touched). The physics' steps (ScrCar.WorldPosition, Scale's $EE / 256): height and
/// distance both move Step x speed world units a pass; gravity takes Gravity a pass off the upward speed.</summary>
public sealed class ScrRail
{
    const double GapDrop = 1200, Margin = 40, Step = 0xEE / 256.0 * 128 / 256 * ScrRoad.Scale / 32, StepY = 0xEE / 256.0 * 128 / 256 * ScrRoad.Rise / 32, Gravity = 320;
    const int RailBelow = 8;
    /// <summary>The rail lets go this many passes short of the edge, and adds at most MaxLift to the upward speed a pass.</summary>
    const double LetGo = 1.5, MaxLift = 800;
    /// <summary>Each road's gaps: lip and landing edge (cross-sections) and the jump's speed byte (worked out once a road).</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrRoad, List<(int Lip, int Edge, int Speed)>> Made = new();

    static List<(int Lip, int Edge, int Speed)> Gaps(ScrRoad road) => Made.GetValue(road, Find);

    static List<(int Lip, int Edge, int Speed)> Find(ScrRoad road)
    {
        var gaps = new List<(int Lip, int Edge, int Speed)>();
        var s = road.Sections;
        var n = s.Count;
        double H(int i) => (s[i % n].LY + s[i % n].RY) / 2;
        for (var i = 0; i < n; i++)
        {
            if (H(i) - H(i + 1) < GapDrop) continue;
            var p = road.Piece[i];
            var listed = road.Obstacles.Where(o => Math.Abs(o.Piece - p) <= 1).Select(o => o.Speed & 0x7F).DefaultIfEmpty(-1).Max();
            if (listed < 0) continue;
            var floor = H(i + 1);
            for (var k = 2; k <= 80; k++)
                if (H(i + k) >= floor + GapDrop) { gaps.Add((i, (i + k) % n, listed)); break; }
        }
        return gaps;
    }

    /// <summary>This car's: the gap it took off over, when it left a lip fast enough (null: none).</summary>
    (int Lip, int Edge, int Speed)? _flying;

    /// <summary>One physics pass of a computer-driven car (before its physics): over a gap it took off over fast enough, its upward
    /// speed raised to reach the landing edge. strip: the cross-section under it (any height); y: its height (world units).</summary>
    public void Pass(ScrRoad road, ScrCar c, int strip, double x, double z, double y)
    {
        var vz = c.W(ScrCar.carLocalVelocityZ);
        if (_flying is null)
        {
            foreach (var g in Gaps(road))
                if (strip >= 0 && (strip - g.Lip + road.Sections.Count + 1) % road.Sections.Count <= 2 && vz >= (g.Speed - RailBelow) << 8) { _flying = g; break; }   // at the lip (a strip either side: a pass can step over it)
            if (_flying is null) return;
        }
        var (lip, edge, _) = _flying.Value;
        var n = road.Sections.Count;
        var into = (strip - lip + n) % n;
        if (strip < 0 || into > (edge - lip + n) % n || into < 0) { _flying = null; return; }   // past the edge: landed (or off)
        var e = road.Sections[edge];
        var d = road.AlongAt(edge, (e.LX + e.RX) / 2, (e.LZ + e.RZ) / 2) - road.AlongAt(strip, x, z);
        if (d < 0) d += road.LapLength;
        if (vz <= 0 || d < LetGo * Step * vz) { _flying = null; return; }       // at the edge: let go (the distance left near 0, the sum asked for any speed)
        var t = d / (Step * vz);                                                  // passes to the edge
        var target = (e.LY + e.RY) / 2 + Margin;
        var need = (target - y) / StepY / t + Gravity * t / 2;                      // the upward speed that gets there
        var vy = c.W(ScrCar.worldYSpeed);
        if (vy < need) c.W(ScrCar.worldYSpeed, (int)Math.Round(Math.Min(need, vy + MaxLift)));   // at most MaxLift more a pass
    }

    /// <summary>A restart or a wreck: no flight going on.</summary>
    public void Reset() => _flying = null;
}
