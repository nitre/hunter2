namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (the wild attract, owner 2026-11-02: "the player doesn't go to wild town"):
/// where the attract demo's player goes - the plain demo played once (Action game, default soldier, as RunDemo $22D6),
/// his x and z every few passes. Pure: the recording steers him, so the route is the same every time; kept after the
/// first ask.</summary>
public static class DemoRoute
{
    const int Every = 4;                                            // passes between kept points
    static (int X, int Z)[]? cached;

    public static (int X, int Z)[] Points(GameData d)
    {
        if (cached is not null) return cached;
        var play = new Play(d, GameMode.Action, soldier: StartState.DefaultSoldier(d));
        Demo.Start(play.State);
        var points = new List<(int, int)>();
        for (var pass = 0; !play.Quit && pass < 40000; pass++)
        {
            play.VerticalBlank(default);                            // one pass, as the tests' GameStep.Pass
            play.Frame();
            var p = play.State.Entities.All[0];
            if (pass % Every == 0) points.Add((p.UW(0x4A), p.UW(0x4E)));
        }
        return cached = [.. points];
    }
}
