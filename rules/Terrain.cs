namespace Hunter.Rules;

/// <summary>
/// The landscape, generated from the world file's seed exactly as $CB3C does after a world loads
/// ($105EC pushes $CB3C as its return address). 128 x 128 grid points, 2 bytes each (height,
/// type) in rows of $100 bytes: the layout of buffer $83D2 (33280 bytes = 130 rows; the last two
/// rows stay zero and are only read as neighbours). See re/formats/terrain.md.
/// </summary>
public sealed class Terrain
{
    public const int Size = 128;
    public const int RowBytes = 0x100;

    /// <summary>The raw buffer as the original holds it: [row * $100 + col * 2] = height, +1 = type.</summary>
    public byte[] Map { get; } = new byte[33280];

    public int Height(int col, int row) => Map[row * RowBytes + col * 2];
    public int Type(int col, int row) => Map[row * RowBytes + col * 2 + 1];

    uint _state;   // the long at $1C6(a5)

    // $CBC0: random masks per subdivision level, indexed by level 6..0. Index 6 runs into the
    // next instruction's first byte ($30 of `move.w #$7F,d0` at $CBC6), as the original reads it.
    static readonly byte[] Masks = { 0x0F, 0x1F, 0x3F, 0x3F, 0x7F, 0x7F, 0x30 };
    // $CEB2 / $CEBA: type bytes for the two checkerboard phases; $CF44: flag bits by corner mask.
    static readonly byte[] PhaseA = { 0x09, 0x0B, 0x0C, 0x0E };
    static readonly byte[] PhaseB = { 0x0A, 0x0A, 0x0D, 0x0D };
    static readonly byte[] CornerFlags =
        { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x50, 0x10, 0x10, 0x10, 0x20, 0x10, 0x30, 0x40, 0x00 };
    const byte Border = 0x98;   // $CE46

    /// <summary>seed = world header word +$00 (-> $212(a5)), seaLevel = word +$02 (-> $210(a5)), $105EC.</summary>
    public static Terrain Generate(ushort seed, short seaLevel)
    {
        var t = new Terrain();
        t.Heights(seed);
        t.Classify(seaLevel);
        t.Edges();
        return t;
    }

    /// <summary>A landscape from given raw heights (col, row) instead of a seed's, then the same
    /// classify and outer ring as a generated one (the Stunt Car Racer island).</summary>
    public static Terrain Shaped(Func<int, int, int> height, short seaLevel)
    {
        var t = new Terrain();
        for (var row = 0; row < Size; row++)
            for (var col = 0; col < Size; col++)
                t.Map[row * RowBytes + col * 2] = (byte)Math.Clamp(height(col, row), 0, 255);
        t.Classify(seaLevel);
        t.Edges();
        return t;
    }

    // $CB3C
    void Heights(ushort seed)
    {
        var lo = (ushort)((seed << 4) | (seed >> 12));            // rol.w #4
        lo ^= 0x00FF;                                              // not.b
        _state = ((uint)seed << 16) | lo;
        var acc = new short[128];                                  // $CC34, cleared by $CB8C
        var line = new short[129];                                 // $CD36
        var p = 0;
        for (var pair = 0; pair < 64; pair++)
        {
            NewLine(line, acc);
            for (var r = 0; r < 2; r++)
                for (var i = 0; i < 128; i++)
                {
                    var d0 = (short)(acc[i] + line[i] + line[i]);  // $CB66-$CB6C
                    acc[i] = d0;
                    Map[p] = (byte)((ushort)d0 >> 4);              // lsr.w #4, low byte
                    p += 2;
                }
        }
    }

    /// <summary>(FINER GROUND): $CB3C's heights before its byte cut - each point's 16-bit
    /// sum asr 4, signed. Where it is outside 0..255 the map's byte wrapped (Missions' spikes, byte-limits.md row 1).</summary>
    public static short[] TrueHeights(ushort seed)
    {
        var t = new Terrain();
        var lo = (ushort)((seed << 4) | (seed >> 12));
        lo ^= 0x00FF;
        t._state = ((uint)seed << 16) | lo;
        var acc = new short[128];
        var line = new short[129];
        var heights = new short[128 * 128];
        var p = 0;
        for (var pair = 0; pair < 64; pair++)
        {
            t.NewLine(line, acc);
            for (var r = 0; r < 2; r++)
                for (var i = 0; i < 128; i++)
                {
                    var d0 = (short)(acc[i] + line[i] + line[i]);
                    acc[i] = d0;
                    heights[p++] = (short)(d0 >> 4);
                }
        }
        return heights;
    }

    // $CB96: a fresh midpoint-displaced line, then turned into a slope against the accumulator
    void NewLine(short[] line, short[] acc)
    {
        Midpoint(line);
        for (var i = 0; i < 128; i++)
            line[i] = (short)((short)((line[i] << 4) - acc[i]) >> 4);   // $CBAC-$CBB4
    }

    // $CBC6
    void Midpoint(short[] b)
    {
        b[0] = 0x7F;
        b[128] = 0x7F;
        var span = 128;                                            // words ($100 bytes)
        var count = 1;
        for (var level = 6; level >= 0; level--)
        {
            int mask = Masks[level];
            for (int k = 0, i = 0; k < count; k++, i += span)
            {
                var half = span / 2;
                var d0 = (short)((ushort)Random() & mask);         // $CBF0-$CBF2
                d0 = (short)(d0 + d0 - mask);                      // $CBF4-$CBF6
                var d1 = (short)((short)(b[i + span] - b[i]) >> 1);  // $CBF8-$CBFC
                d1 = (short)(d1 + b[i]);
                b[i + half] = (short)(d0 + d1);
            }
            span >>= 1;
            count <<= 1;
        }
    }

    uint Random() => Step(ref _state);

    /// <summary>$CC18: one step of the terrain's random stream (also Fill's scatter stream).</summary>
    internal static uint Step(ref uint _state)
    {
        var old = _state;
        var d0 = old ^ 0xFFFF;                                     // not.w
        d0 = (d0 << 16) | (d0 >> 16);                              // swap
        d0 = (d0 & 0xFFFFFF00) | (byte)(d0 + (old >> 24));         // add.b $1C6(a5): old top byte
        var n = (int)(d0 & 7);
        if (n != 0) d0 = (d0 << n) | (d0 >> (32 - n));             // rol.l d1
        d0 = (d0 & 0xFFFFFF00) | (byte)(d0 + n);                   // add.b d1
        _state = d0;
        return d0;
    }

    // $CE8E -> $CEC2 per point, row-major; the type table swaps every point and once more per row
    void Classify(short sea)
    {
        var phase = false;
        for (var row = 0; row < 128; row++)
        {
            for (var col = 0; col < 128; col++)
            {
                Point(row * RowBytes + col * 2, sea, phase ? PhaseB : PhaseA);
                phase = !phase;
            }
            phase = !phase;
        }
    }

    // $CEC2
    void Point(int p, short sea, byte[] table)
    {
        int d2 = Map[p] - sea;
        if (d2 < 0) d2 = 0;
        Map[p] = (byte)d2;
        var here = (sbyte)(byte)d2 <= 0;                           // flags of move.b d2,(a4)
        int d3 = Map[p + 2] - sea, d4 = Map[p + 0x102] - sea, d5 = Map[p + 0x100] - sea;
        var mask = (here ? 8 : 0) | (d3 <= 0 ? 4 : 0) | (d4 <= 0 ? 2 : 0) | (d5 <= 0 ? 1 : 0);
        if (mask == 15)
        {
            Map[p + 1] = 0;                                        // $CF3E: all under the sea
            return;
        }
        var flags = CornerFlags[mask];
        int index;
        if (mask == 0 && (d2 > 16 || d4 > 16 || d3 > 16))
            index = 1;                                             // $CF26
        else
        {
            d2 >>= 1;                                              // $CF0C: shore or low ground
            Map[p] = (byte)d2;
            index = 3;
        }
        if (!((sbyte)d3 < (sbyte)d2) && !((sbyte)d4 < (sbyte)d5)) // $CF28-$CF30, byte compares
            index--;
        Map[p + 1] = (byte)(table[index] | flags);
    }

    // $CE38: the map's outer ring gets type $98
    void Edges()
    {
        for (var i = 0; i < 128; i++)
        {
            Map[i * 2 + 1] = Border;                               // row 0
            Map[0x8000 - 1 - i * 2] = Border;                      // row 127, from the end
        }
        for (var row = 0; row < 127; row++)
        {
            Map[row * RowBytes + 1] = Border;                      // column 0
            Map[row * RowBytes + 1 + 0xFE] = Border;               // column 127
        }
    }
}
