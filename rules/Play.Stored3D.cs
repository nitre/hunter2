namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (renderer = 3d): every object stored on the map (the grid's 16 x 16 cells of
/// 8 records, and roomy_memory's overflow) as look records for the card-built world - shown where they stand, not
/// brought to life - handed over only when those records changed (a thing spawned or written back), so a still
/// map costs a byte compare a frame. Nothing in the game changes.</summary>
public sealed partial class Play
{
    readonly Entity _look = new();
    byte[] _looksGrid = [];
    int _looksSum = -1;

    /// <summary>The stored objects as look records ($A0 bytes, the card-built world's rows), only when the map's
    /// records changed since the last call; null = the last list still holds.</summary>
    public List<byte[]>? StoredLooks()
    {
        var s = State;
        var sum = 0;
        foreach (var list in s.Overflow.Values)
            foreach (var rec in list)
                for (var i = 0; i < rec.Length; i++) sum = sum * 31 + rec[i];
        if (_looksSum == sum && s.Grid.AsSpan().SequenceEqual(_looksGrid)) return null;
        _looksGrid = (byte[])s.Grid.Clone();
        _looksSum = sum;
        var looks = new List<byte[]>();
        for (var at = 0; at < s.Grid.Length; at += 16)
            if (StartState.Appearance(_d, s, s.Grid, at, _look)) looks.Add((byte[])_look.B.Clone());
        foreach (var list in s.Overflow.Values)
            foreach (var rec in list)
                if (StartState.Appearance(_d, s, rec, 0, _look)) looks.Add((byte[])_look.B.Clone());
        return looks;
    }
}
