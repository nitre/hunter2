using System.Linq;
using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>STUNTIN's league between the league screen and the race: the league itself (kept in user://scr_league.json, or
/// HUNTER_SCR_LEAGUE's file), the race being run and its result. A race ends ShowFor frames after the player crosses the
/// line; a race left before that (Quit Game) is a loss with the other point to the opponent, as the original's.</summary>
public static class ScrLeagueRun
{
    /// <summary>Frames the finish is held before the league screen comes back.</summary>
    const int ShowFor = 150;
    static ScrLeague? _league;
    static int _after;
    static ScrLeague.Result? _result;

    /// <summary>The race on now (null: none, or practice).</summary>
    public static ScrRace.Opponent? Race { get; set; }
    /// <summary>A practice drive from the league screen is on (no opponent, nothing counted).</summary>
    public static bool Practice { get; set; }
    /// <summary>The line the league screen shows after a round.</summary>
    public static string? News { get; set; }

    static string FilePath => ProjectSettings.GlobalizePath(System.Environment.GetEnvironmentVariable("HUNTER_SCR_LEAGUE") is { Length: > 0 } p ? p : "user://scr_league.json");

    /// <summary>The league: the saved one, else a new one with fresh dice.</summary>
    public static ScrLeague League
    {
        get
        {
            if (_league is not null) return _league;
            try { if (System.IO.File.Exists(FilePath)) _league = ScrLeague.Load(System.IO.File.ReadAllText(FilePath)); }
            catch (System.IO.IOException) { }
            return _league ??= New();
        }
    }

    public static ScrLeague New()
    {
        _league = new ScrLeague((uint)System.Random.Shared.Next(1, int.MaxValue));
        News = null;
        Save();
        return _league;
    }

    public static void Save()
    {
        try { System.IO.File.WriteAllText(FilePath, _league!.Save()); }
        catch (System.IO.IOException e) { GD.PrintErr($"HUNTER: the league could not be saved: {e.Message}"); }
    }

    /// <summary>The round's race set up for the player: his opponent with its traits and the track's rolled cornering skill.
    /// Null when he has no race this round or the drivers' file is missing.</summary>
    public static (ScrRace.Opponent Opponent, int Track)? Next(string dataDir)
    {
        var l = League;
        if (l.PlayerRace is not { } race || ScrLeague.Drivers(dataDir) is not { } drivers) return null;
        var o = new ScrRace.Opponent(race.Opponent, drivers[race.Opponent].Name, drivers[race.Opponent].Traits,
            l.Cornering(race.Track), l.Byte() << 8 | l.Byte());
        Save();
        return (o, race.Track);
    }

    /// <summary>Once a frame in play: true once the race is over and held - the result is kept for Done.</summary>
    public static bool Over(WorldState s)
    {
        if (Race is null && !Practice || s.ScrDrive is not { FinishedAt: >= 0 } me) { _after = 0; return false; }
        if (++_after < ShowFor) return false;
        var opp = s.ScrAi.Values.Select(a => a.Drive).FirstOrDefault();
        var lost = opp is { FinishedAt: >= 0 } && opp.FinishedAt < me.FinishedAt;
        var oppFastest = opp is { BestLap: >= 0 } && (me.BestLap < 0 || opp.BestLap < me.BestLap);
        _result = new ScrLeague.Result(lost, oppFastest);
        GD.Print($"HUNTER: league race over - {(lost ? "lost" : "won")}, home at {me.FinishedAt} / {opp?.FinishedAt}, best laps {me.BestLap} / {opp?.BestLap}, restarts {me.Restarts} / {opp?.Restarts} (wrecks {me.Wrecks} / {opp?.Wrecks}), damage {me.Damage} / {opp?.Damage}, touches {ScrRace.Touches}, dice {s.ScrAutopilot?.CornerRoll}:{s.ScrAutopilot?.LaneRoll} / {s.ScrAi.Values.FirstOrDefault().Brain?.CornerRoll}:{s.ScrAi.Values.FirstOrDefault().Brain?.LaneRoll}");
        return true;
    }

    /// <summary>Back from a game: a league race's round is played (its result, or a loss when it was left), saved. True when
    /// the league screen is next.</summary>
    public static bool Done()
    {
        if (Practice) { Practice = false; _after = 0; News = null; return true; }
        if (Race is null) return false;
        var l = League;
        var before = l.PlayerDivision;
        l.Play(_result);
        News = ScrLeagueScreen.News(l, Names(), before);
        Race = null; _result = null; _after = 0;
        Save();
        return true;
    }

    /// <summary>A round the player sits out, run from the league screen.</summary>
    public static void SitOut()
    {
        var l = League;
        var before = l.PlayerDivision;
        l.Play(null);
        News = ScrLeagueScreen.News(l, Names(), before);
        Save();
    }

    static string[]? _names;
    public static string DataDir { get; set; } = "";
    public static string[] Names() => _names ??= ScrLeague.Drivers(DataDir)?.Select(d => d.Name).ToArray()
        ?? Enumerable.Range(0, ScrLeague.Count).Select(i => $"DRIVER {i + 1}").ToArray();
}
