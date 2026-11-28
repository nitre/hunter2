namespace Hunter.Rules;

/// <summary>STUNTIN': DRAW BRIDGE's moving leaves, as the original's animateDrawBridge ($4C0A0) moves them. Each processed
/// frame (a physics pass) the phase (drawBridgeAnimationPhase) steps one, unless the bridge is held: a car on pieces
/// $33-$37, or - while it is held - the computer car from piece $30 on (drawBridgeActive). The phase's low 5 bits less $10,
/// a negative one NOT-ed, give 0..15; the step is that + 4, x 8 ground units: each leaf's height k cross-sections from its
/// end is the bridge's base + (k + 1) x step - both leaves rise toward the middle span, which stays at the base. The game
/// data holds the bridge at phase 15 (step 32), so each leaf cross-section's k is read off its height there.</summary>
public sealed class ScrBridge
{
    /// <summary>The bridge's pieces (the original's segment indices), and the computer car's hold from.</summary>
    public const int First = 0x33, Last = 0x37, HoldFrom = 0x30;
    /// <summary>The leaves' step (ground units) the game data holds the bridge at (phase 15, its lowest).</summary>
    const int DataStep = 32;

    readonly ScrRoadSurface _surface;
    readonly (int Sec, int K)[] _leaves;
    readonly double _base;
    readonly HashSet<int> _strips = [];

    /// <summary>drawBridgeAnimationPhase (a byte; 0 when a race starts).</summary>
    public int Phase { get; private set; }
    /// <summary>drawBridgeActive: a car is on it (or the computer car close and it was already held) - it does not move.</summary>
    public bool Held { get; private set; }

    ScrBridge(ScrRoadSurface surface, (int, int)[] leaves, double @base)
    {
        (_surface, _leaves, _base) = (surface, leaves, @base);
        var n = surface.Road.Sections.Count;
        foreach (var (sec, _) in leaves) { _strips.Add(sec); _strips.Add((sec - 1 + n) % n); }
    }

    /// <summary>The track's bridge: Draw Bridge's leaf cross-sections on its pieces First..Last (those above the base, the
    /// height of the cross-section before the bridge); null on every other track.</summary>
    public static ScrBridge? For(ScrRoadSurface surface)
    {
        var road = surface.Road;
        if (road.Name != "DRAW BRIDGE") return null;
        var secs = road.Sections;
        var first = road.Piece.ToList().IndexOf(First);
        if (first <= 0) return null;
        var @base = secs[first - 1].LY / ScrRoad.Rise;
        var leaves = new List<(int, int)>();
        for (var i = first; i < secs.Count && road.Piece[i] <= Last; i++)
        {
            var k = (int)Math.Round((secs[i].LY / ScrRoad.Rise - @base) / DataStep) - 1;
            if (k >= 0) leaves.Add((i, k));
        }
        return leaves.Count > 0 ? new ScrBridge(surface, [.. leaves], @base) : null;
    }

    /// <summary>Whether strip i (cross-section i to the next) moves with the bridge.</summary>
    public bool Moves(int i) => _strips.Contains(i);

    /// <summary>The leaves' step at a phase (ground units): ((phase & $1F) - $10, NOT-ed when negative, + 4) x 8.</summary>
    public static int Step(int phase)
    {
        var d = (phase & 0x1F) - 0x10;
        if (d < 0) d = ~d;
        return (d + 4) * 8;
    }

    /// <summary>The drawn leaves after the original's pass (ScrCar.AnimateDrawBridge): its phase and whether it is held.</summary>
    public void Follow(int phase, bool held)
    {
        Held = held;
        if (held || phase == Phase) return;
        Phase = phase;
        Set(Step(Phase));
    }

    void Set(int step)
    {
        foreach (var (sec, k) in _leaves) _surface.SetHeight(sec, (_base + (k + 1) * step) * ScrRoad.Rise);
    }
}
