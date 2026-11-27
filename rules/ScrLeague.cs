using System.Text.Json;

namespace Hunter.Rules;

/// <summary>STUNTIN's league, the original's rules (StuntCarRacer.s initializeGameTables, setupRandomRaceParameters,
/// configurePlayersAndCars, setupRaceHandicapping, sortRaceResults, updateLeagueStandingsAfterRace; docs/scr-physics.md
/// "The drivers and the league"):
/// - THE TABLE: 12 entries of driver ids, division d (0 = IV the bottom .. 3 = I the top) entries DivisionBase[d] .. +2;
///   a new game puts id i at entry i. The player is id 11 (Player), in division IV.
/// - A SEASON: 6 rounds; round n's race in every division is between its entries PairA[n] and PairB[n] on the division's
///   track n &amp; 1. The player's own race is driven; every other race is decided by the round's skill rolls.
/// - POINTS: 2 a win, 1 for the race's other point - in a decided race to the winner, or 96 in 256 to the loser; in the
///   player's race to whoever drove the fastest lap. Each division sorts by points, then wins, then a coin.
/// - SEASON'S END: one up, one down between neighbouring divisions. The player top of division I: the Super League (the
///   table as a new game's, faster cars), and top of it again: the championship.
/// The dice are the league's own (a seeded generator kept in the league), so a saved league plays on the same.</summary>
public sealed class ScrLeague
{
    public const int Count = 12, Player = 11, Rounds = 6, Divisions = 4;
    /// <summary>divisionBaseOffsets: division d's first (top) entry.</summary>
    public static readonly int[] DivisionBase = [9, 6, 3, 0];
    /// <summary>aiBaseSkillTable by driver id: 120 down by 10.</summary>
    public static readonly int[] SkillBase = [0x78, 0x6E, 0x64, 0x5A, 0x50, 0x46, 0x3C, 0x32, 0x28, 0x1E, 0x14, 0x0A];
    /// <summary>player1OpponentIndices / player2OpponentIndices: round n's two entries within a division.</summary>
    public static readonly int[] PairA = [0, 0, 0, 0, 1, 1], PairB = [1, 1, 2, 2, 2, 2];
    /// <summary>trackIDLookupTable: each division's two tracks (ScrIsland.Tracks' order).</summary>
    public static readonly int[][] DivisionTracks = [[0, 2], [1, 3], [6, 7], [4, 5]];
    /// <summary>The division's name as the original shows it.</summary>
    public static readonly string[] DivisionNames = ["IV", "III", "II", "I"];
    /// <summary>The computer car's cornering skill per track (lbW01172C +$10 masks, +$18 bases): random &amp; mask + base,
    /// row 0 the Standard League, row 1 the Super League.</summary>
    static readonly int[][] CornerMask = [[0, 0, 0, 0, 0, 0, 0, 0], [7, 3, 3, 3, 3, 1, 3, 3]];
    static readonly int[][] CornerBase = [[0x48, 0x41, 0x45, 0x48, 0x4F, 0x58, 0x4F, 0x56], [0x61, 0x55, 0x53, 0x56, 0x58, 0x5B, 0x5A, 0x62]];

    /// <summary>leagueStandingsTable: entry -> driver id, as the season started.</summary>
    public int[] Table { get; set; } = Enumerable.Range(0, Count).ToArray();
    /// <summary>sortedRaceStandings: entry -> driver id, each division sorted by its points so far.</summary>
    public int[] Standing { get; set; } = Enumerable.Range(0, Count).ToArray();
    /// <summary>By driver id: races won (2 points each), other points, races run, this round's skill.</summary>
    public int[] Wins { get; set; } = new int[Count];
    public int[] Extra { get; set; } = new int[Count];
    public int[] Races { get; set; } = new int[Count];
    public int[] Skill { get; set; } = new int[Count];
    /// <summary>The round about to be run (0 .. Rounds - 1).</summary>
    public int Round { get; set; }
    /// <summary>Seasons finished.</summary>
    public int Season { get; set; }
    public bool Super { get; set; }
    public bool Champion { get; set; }
    /// <summary>The dice's state (xorshift32, never 0).</summary>
    public uint Dice { get; set; } = 0x2545F491;
    /// <summary>The last round's results, for the screen: (division, winner, loser, who took the other point).</summary>
    public List<int[]> Last { get; set; } = [];
    /// <summary>The season just ended (for the screen): the player's division before and after, -1 if none yet.</summary>
    public int EndedFrom { get; set; } = -1;
    public int EndedTo { get; set; } = -1;

    public ScrLeague() { }
    public ScrLeague(uint seed) => Dice = seed == 0 ? 1u : seed;

    /// <summary>The next die, 0-255.</summary>
    public int Byte()
    {
        var x = Dice;
        x ^= x << 13; x ^= x >> 17; x ^= x << 5;
        Dice = x;
        return (int)(x >> 24);
    }

    public static int Points(ScrLeague l, int id) => l.Wins[id] * 2 + l.Extra[id];
    public int Points(int id) => Points(this, id);

    /// <summary>The division (0 = IV .. 3 = I) driver id is in.</summary>
    public int DivisionOf(int id) => 3 - Array.IndexOf(Table, id) / 3;
    [System.Text.Json.Serialization.JsonIgnore] public int PlayerDivision => DivisionOf(Player);

    /// <summary>Round n's two drivers in division d: (player 1, player 2) as the original's D2, D1.</summary>
    public (int A, int B) Pair(int d, int round) => (Table[DivisionBase[d] + PairA[round]], Table[DivisionBase[d] + PairB[round]]);
    public int Track(int d, int round) => DivisionTracks[d][round & 1];

    /// <summary>The player's race this round: his opponent and the track, null when he sits this round out.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public (int Opponent, int Track)? PlayerRace
    {
        get
        {
            if (Champion) return null;
            var d = PlayerDivision;
            var (a, b) = Pair(d, Round);
            if (a != Player && b != Player) return null;
            return (a == Player ? b : a, Track(d, Round));
        }
    }

    /// <summary>A computer car's cornering skill for a race on the track (initializeTrackDifficultyParameters).</summary>
    public int Cornering(int track)
    {
        var row = Super ? 1 : 0;
        return (Byte() & CornerMask[row][track]) + CornerBase[row][track];
    }

    /// <summary>The player's race as it ended: lost (or not finished), and whether the fastest lap was the opponent's.</summary>
    public readonly record struct Result(bool Lost, bool OpponentFastest);

    /// <summary>The round is run: every driver's skill rolled, each division's race decided (the player's from his result,
    /// null when he did not race or gave it up: a loss, the point to the other), each division sorted; after the sixth
    /// round the season ends. A round the player has no race in takes no result.</summary>
    public void Play(Result? mine)
    {
        if (Champion) return;
        for (var id = Count - 1; id >= 0; id--) Skill[id] = (Byte() & 0x3F) + SkillBase[id];
        Last = [];
        for (var d = 0; d < Divisions; d++)
        {
            var flags = Byte() >= 0xA0 ? 0x40 : 0;                          // raceSetupFlags: the other point to the loser
            var (p1, p2) = Pair(d, Round);
            Races[p1]++; Races[p2]++;
            if (p1 == Player || p2 == Player)
            {
                var r = mine ?? new Result(true, true);
                flags = (r.Lost ? 0x80 : 0) | (r.OpponentFastest ? 0x40 : 0);   // raceOutcomeFlags, the player as player 2
                if (p1 == Player) flags ^= 0xC0;
            }
            else if (Skill[p1] > Skill[p2] || (Skill[p1] == Skill[p2] && (Byte() & 1) == 0))
                flags ^= 0xC0;                                              // player 1 the higher skill (a tie a coin): his win
            var winner = (flags & 0x80) != 0 ? p1 : p2;
            var point = (flags & 0x40) != 0 ? p1 : p2;
            Wins[winner]++; Extra[point]++;
            Last.Add([d, winner, winner == p1 ? p2 : p1, point]);
            Sort(d);
        }
        if (++Round >= Rounds) EndSeason();
    }

    /// <summary>sortRaceResults: division d from the table, bubbled by points, then wins, then a coin (a swap on heads).</summary>
    void Sort(int d)
    {
        var b = DivisionBase[d];
        for (var i = 0; i < 3; i++) Standing[b + i] = Table[b + i];
        for (var swapped = true; swapped;)
        {
            swapped = false;
            for (var i = b; i < b + 2; i++)
            {
                int x = Standing[i], y = Standing[i + 1];
                var c = Points(x).CompareTo(Points(y));
                if (c == 0) c = Wins[x].CompareTo(Wins[y]);
                if (c < 0 || (c == 0 && (Byte() & 1) == 1)) { (Standing[i], Standing[i + 1]) = (y, x); swapped = true; }
            }
        }
    }

    /// <summary>updateLeagueStandingsAfterRace + initializeGameTables: the sorted table kept, each division's top swapped with
    /// the bottom of the one above; the player top of division I goes to the Super League (a new game's table), or, there
    /// already, is the champion. The points start again.</summary>
    void EndSeason()
    {
        EndedFrom = PlayerDivision;
        var top = Standing[0];
        Table = (int[])Standing.Clone();
        for (var d = 2; d >= 0; d--)
        {
            var b = DivisionBase[d];
            (Table[b], Table[b - 1]) = (Table[b - 1], Table[b]);
        }
        if (top == Player)
        {
            if (Super) Champion = true;
            else { Super = true; Table = Enumerable.Range(0, Count).ToArray(); }
        }
        EndedTo = PlayerDivision;
        Standing = (int[])Table.Clone();
        Wins = new int[Count]; Extra = new int[Count]; Races = new int[Count];
        Round = 0;
        Season++;
    }

    public string Save() => JsonSerializer.Serialize(this);
    public static ScrLeague? Load(string json)
    {
        try { return JsonSerializer.Deserialize<ScrLeague>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>The original's drivers by id (game_data/scr/drivers.json, exported from the original disk): name and traits byte. Null
    /// when the file is not there.</summary>
    public static (string Name, int Traits)[]? Drivers(string dataDir)
    {
        var path = Path.Combine(dataDir, "scr", "drivers.json");
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var list = doc.RootElement.GetProperty("drivers").EnumerateArray()
            .Select(e => (e.GetProperty("name").GetString() ?? "", e.GetProperty("traits").GetInt32())).ToArray();
        return list.Length == Count ? list : null;
    }
}
