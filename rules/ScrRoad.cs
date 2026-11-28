using System.Text.Json;

namespace Hunter.Rules;

/// <summary>STUNTIN': a Stunt Car Racer track as a road in the world of its face (ScrIsland). The track is the game's own
/// data, decoded by running its loader (game_data/scr/tracks_road.json, built from the original disk - see
/// docs/scr-physics.md): cross-sections round the lap, each the road's left and right edge (x, z) and height, in SCR's
/// ground unit (a tile 2048, the grid 16 x 16 tiles, a height value x 8). Here in world units: x Scale, the grid centred
/// on the face (Offset), so one track fills the land.</summary>
public sealed class ScrRoad
{
    /// <summary>World units per SCR ground unit, matched by WIDTH: SCR's road is 384 wide and its car 81 (4.7 car widths,
    /// docs/scr-physics.md), Hunter's car 120 - so 1.5 gives a 576 road, 4.8 of ours. (1.75, matched by length, left our
    /// car small on the road: Hunter's car is longer for its width than SCR's.)</summary>
    public const double Scale = 1.5;
    /// <summary>Height units to a ground unit: Hunter's heights are drawn at half a world unit, so twice Scale keeps the track's
    /// true shape - a slope, a banking, a jump's face as steep as the original's, the car's own pitch and roll lying on it.</summary>
    public const double Rise = 2 * Scale;
    /// <summary>The grid's corner on the face: (65536 - 32768 x Scale) / 2.</summary>
    public const double Offset = (65536 - 32768 * Scale) / 2;

    /// <summary>One cross-section: the left and right edge, world units (x, height, z).</summary>
    public readonly record struct Section(double LX, double LY, double LZ, double RX, double RY, double RZ);

    public string Name { get; }
    public IReadOnlyList<Section> Sections => _sections;
    readonly List<Section> _sections;
    /// <summary>Counts every change to a cross-section's height (the draw bridge): the drawing rebuilds what moved.</summary>
    public int Version { get; private set; }

    /// <summary>A cross-section's height, both edges (world units): the draw bridge's leaves.</summary>
    public void SetHeight(int i, double y)
    {
        if (_sections[i].LY == y && _sections[i].RY == y) return;
        _sections[i] = _sections[i] with { LY = y, RY = y };
        Version++;
    }
    /// <summary>The piece each cross-section (and the strip after it) is in; the lap runs in the sections' order (the
    /// original's piece index + 1, as its opponent drives).</summary>
    public IReadOnlyList<int> Piece { get; }
    /// <summary>The piece the player starts on (playerSpawnSegment).</summary>
    public int Spawn { get; }
    /// <summary>Each piece's steering values as the original's loadTrackSegmentConfiguration sets them: segmentSteeringFlags
    /// (bit 7 a corner), segmentBaseSteeringOffset, trackDirectionInversionFlag, reverseDirectionFlag (bytes).</summary>
    public IReadOnlyList<(int Flags, int Base, int Inversion, int Reverse)> Steer { get; }

    /// <summary>The track's listed obstacles (the jumps): piece and speed byte (obstacleSegmentIndices / obstacleTypes).</summary>
    public IReadOnlyList<(int Piece, int Speed)> Obstacles { get; private init; } = [];
    /// <summary>The track's feature pieces (trackFeatureData): the restart steps back past them.</summary>
    public IReadOnlyList<int> Features { get; private init; } = [];
    /// <summary>THE RACE, from the track's header: the lap line's piece (lapLineSegment), the finish piece a car must pass
    /// before the line counts again (finishLineSegmentIndex), and per player (0 the first, 1 the second) the frames of a hard
    /// landing that count as damage (damageFrameLimit: trackMetadataBuffer / trackDamageFrameBase) and the boost fuel
    /// (trackBoostFuelPlayer1 / 2, counted into the tank in decimal).</summary>
    public int LapLine { get; private init; }
    public int Finish { get; private init; }
    public IReadOnlyList<int> Damage { get; private init; } = [6, 6];
    public IReadOnlyList<int> Boost { get; private init; } = [60, 60];
    /// <summary>Laps in a race (the original's maxLapsForRace 4: its counter runs 1 to 4).</summary>
    public const int Laps = 3;
    /// <summary>The lap's length in the original's distance units (startWorldPosition, set by its loader): its race distances
    /// (the slipstream's reach) scale to ours by LapLength / LapUnits.</summary>
    public int LapUnits { get; private init; } = 12736;
    /// <summary>The track's own tables for the original's road lookup (game_data/scr/track_N.bin, ScrCar.LoadTrack); null
    /// when the file is not there.</summary>
    public byte[]? Tables { get; private init; }

    double[]? _along;
    /// <summary>Each cross-section's middle's distance round the lap from the first (world units), and the lap's length.</summary>
    double[] Along => _along ??= Measure();
    double[] Measure()
    {
        var n = Sections.Count;
        var a = new double[n + 1];
        for (var i = 0; i < n; i++)
        {
            var (p, q) = (Sections[i], Sections[(i + 1) % n]);
            a[i + 1] = a[i] + Math.Sqrt(Math.Pow((q.LX + q.RX - p.LX - p.RX) / 2, 2) + Math.Pow((q.LZ + q.RZ - p.LZ - p.RZ) / 2, 2));
        }
        return a;
    }
    public double LapLength => Along[^1];

    /// <summary>A place's distance round the lap from the first cross-section (world units), its section given: the
    /// section's start plus how far along toward the next its middle line it is.</summary>
    public double AlongAt(int sec, double x, double z)
    {
        var n = Sections.Count;
        var (p, q) = (Sections[sec], Sections[(sec + 1) % n]);
        double mx = (p.LX + p.RX) / 2, mz = (p.LZ + p.RZ) / 2, dx = (q.LX + q.RX) / 2 - mx, dz = (q.LZ + q.RZ) / 2 - mz;
        var l = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
        return Along[sec] + Math.Clamp(((x - mx) * dx + (z - mz) * dz) / l, -l, l);
    }

    /// <summary>How far a car is into the race (world units): its lap counter's laps plus its way from the lap line - so
    /// one just short of the line and one just past it with a lap more counted are a hair apart.</summary>
    public double Progress(int lap, int sec, double x, double z)
    {
        var since = AlongAt(sec, x, z) - Along[StartLineSection];
        since = ((since % LapLength) + LapLength) % LapLength;
        return lap * LapLength + since;
    }

    /// <summary>A place across the road at its section: 0 the left edge, 1 the right (the original's lateral position / 255).</summary>
    public double Across(int sec, double x, double z)
    {
        var p = Sections[sec];
        double ax = p.RX - p.LX, az = p.RZ - p.LZ, w2 = Math.Max(1e-6, ax * ax + az * az);
        return ((x - p.LX) * ax + (z - p.LZ) * az) / w2;
    }

    ScrRoad(string name, List<Section> sections, List<int> piece, int spawn, List<(int, int, int, int)> steer) =>
        (Name, _sections, Piece, Spawn, Steer) = (name, sections, piece, spawn, steer);

    /// <summary>The first cross-section of the spawn piece: where the player starts.</summary>
    public int SpawnSection => Math.Max(0, Piece.ToList().IndexOf(Spawn));

    /// <summary>THE START LINE's cross-section: the first of the piece after the spawn piece (the original's
    /// lapLineSegment = lapApproachSegment + 1, the approach being the spawn piece on every track); the original marks the
    /// approach piece's last cross-section - the same place - with bit 0 of segmentProcessedFlags
    /// (setupTrackGeometryForFrame2), drawn as a line across the road in colour 15 (renderTrackSurfaceEdge).</summary>
    public int StartLineSection => Math.Max(0, Piece.ToList().IndexOf((Spawn + 1) % Steer.Count));

    /// <summary>The track (0-7, ScrIsland.Tracks' order) from the game data's folder; null when the file is not there.</summary>
    public static ScrRoad? Load(string dataDir, int track)
    {
        var path = Path.Combine(dataDir, "scr", "tracks_road.json");
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var t = doc.RootElement.GetProperty(track.ToString());
        var secs = t.GetProperty("sections").EnumerateArray().Select(s =>
        {
            double V(int i) => s[i].GetDouble();
            return new Section(Offset + V(0) * Scale, V(2) * Rise, Offset + V(1) * Scale,
                               Offset + V(3) * Scale, V(5) * Rise, Offset + V(4) * Scale);
        }).ToList();
        var piece = t.TryGetProperty("piece", out var pe) ? pe.EnumerateArray().Select(v => v.GetInt32()).ToList() : Enumerable.Repeat(0, secs.Count).ToList();
        var spawn = t.TryGetProperty("spawn", out var sp) ? sp.GetInt32() : 0;
        var steer = t.TryGetProperty("steer", out var st)
            ? st.EnumerateArray().Select(q => (q[0].GetInt32(), q[1].GetInt32(), q[2].GetInt32(), q[3].GetInt32())).ToList()
            : [(0, 0, 0, 0)];
        var obstacles = t.TryGetProperty("obstacles", out var ob) ? ob.EnumerateArray().Select(q => (q[0].GetInt32(), q[1].GetInt32())).ToList() : [];
        var features = t.TryGetProperty("features", out var fe) ? fe.EnumerateArray().Select(v => v.GetInt32()).ToList() : [];
        int[] Pair(string name, int fallback) => t.TryGetProperty(name, out var p) ? [.. p.EnumerateArray().Select(v => v.GetInt32())] : [fallback, fallback];
        return new ScrRoad(t.GetProperty("name").GetString()!, secs, piece, spawn, steer)
        {
            Obstacles = obstacles, Features = features,
            LapLine = t.TryGetProperty("lap_line", out var ll) ? ll.GetInt32() : (spawn + 1) % steer.Count,
            Finish = t.TryGetProperty("finish", out var fi) ? fi.GetInt32() : (spawn + steer.Count / 2) % steer.Count,
            Damage = Pair("damage", 6), Boost = Pair("boost", 60),
            LapUnits = t.TryGetProperty("lap_units", out var lu) ? lu.GetInt32() : 12736,
            Tables = File.Exists(Path.Combine(dataDir, "scr", $"track_{track}.bin")) ? File.ReadAllBytes(Path.Combine(dataDir, "scr", $"track_{track}.bin")) : null,
        };
    }
}
