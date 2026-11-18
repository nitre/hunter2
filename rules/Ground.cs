namespace Hunter.Rules;

/// <summary>
/// The landscape buffer as memory: the original reads past both ends of $83D2 near the map
/// edges, into the tail of the objfile buffer before it and the sine table after it.
/// </summary>
public sealed class MapMemory
{
    const int Before = 0x1000, After = 0x1000;
    readonly byte[] _mem;

    public MapMemory(Terrain t, byte[] objfileBuffer, byte[] sinfile)
    {
        _mem = new byte[Before + t.Map.Length + After];
        var tail = objfileBuffer.AsSpan(objfileBuffer.Length - Before);
        tail.CopyTo(_mem);
        t.Map.CopyTo(_mem, Before);
        sinfile.AsSpan(0, Math.Min(After, sinfile.Length)).CopyTo(_mem.AsSpan(Before + t.Map.Length));
    }

    /// <summary>Byte at a signed offset from the start of the map.</summary>
    public byte this[int offset] => (uint)(Before + offset) < (uint)_mem.Length ? _mem[Before + offset] : (byte)0;   // ENHANCEMENT (far view): past the padding = nothing (the original never reads that far)
}

/// <summary>Camera globals set by $374C each frame (names are their a5 offsets).</summary>
public sealed class Camera
{
    public short Frac1538, Frac153A, Col153C, Row153E, G1540, G1542, G154C;
    public int G1544, G1548, MapOffset1550;
    public short Height0E;
    /// <summary>$10(a5): screen x of the view centre ($A0, set at $2432).</summary>
    public short CentreX = 0xA0;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (multiplayer piece mp_view_angle): the view's
    /// camera angle, set by Place - 0 the original (facing north), $80 a half turn (facing
    /// south; sin $80 = 0, cos $80 = -1, so every rotation is an exact negation). Objects.Place
    /// reads it to negate the +$56/+$5A deltas.</summary>
    public byte Angle;
}

/// <summary>Ground: camera set-up, grid projection and the far-to-near row loop ($39B0).</summary>
public sealed class Ground
{
    readonly MapMemory _map;
    public Ground(MapMemory map) => _map = map;

    bool _farView;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (Play.FarView): the grid grows - four more point
    /// rows beyond the far row ($1D00-$2900, one $400 depth step each) and 7 -> 11 cell rows,
    /// Row153E covering the taller grid (Place's +7 -> +11 for $153E only; MapOffset1550 keeps the +7, so the
    /// original 8 point rows do not move). Off (the default),
    /// every constant is the original's literal.</summary>
    public bool FarView { get => _farView; set => _farView = value; }
    /// <summary>ENHANCEMENT (FarView, wide): the cells per row - the original's 7, 15 with far view, so the
    /// far rows reach the screen's edges (at depth $2900 a column is ~25 pixels wide; 7 left hills cut off
    /// at the patch's side as floating slabs, owner 2026-10-24). Pts = the grid points per row; Extra = the
    /// columns added on each side.</summary>
    public int Cols => FarView ? WideCols : 7;
    public const int WideCols = 39;
    /// <summary>ENHANCEMENT (FarView): the cell rows added beyond the original's 7 - as deep as the 16-bit depth
    /// maths allows (the far row at $1900 + 24 x $400 = $7900, under $7FFF), not a notch (owner 2026-10-24).
    /// 39 columns fill the screen at that depth (~8.5 pixels a column there).</summary>
    public const int AddRows = 24;
    int Rows => FarView ? 7 + AddRows : 7;
    int Pts => Cols + 1;
    int Extra => (Cols - 7) / 2;

    bool _maths32;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (Play.Maths32, owner 2026-10-26 "32 it is"): the height
    /// lerp ($3710-$371A) in plain 32-bit integers - no 16-bit wrap, no divs overflow. Off, the
    /// original's word maths byte for byte. Set by Play.Maths32's setter. The shipped maps never
    /// overflow the divide (jobs/wrap16), so their heights are identical either way; it matters for
    /// steeper corners than they have.</summary>
    public bool Maths32 { get => _maths32; set => _maths32 = value; }

    FineGround? _fine;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (FINER GROUND): the finer, taller height store. Set, Height reads it
    /// (in 32-bit maths, whatever Maths32 says), and the flat drawer's far and near rows take their points from it;
    /// null (the default), the map bytes and the original's word maths, bit for bit.</summary>
    public FineGround? Fine { get => _fine; set => _fine = value; }

    FineGround? _smooth;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (FINER GROUND, the second lookup): the smooth ground - the original's
    /// bilinear of its points times the factor, risen from the sea (FineGround.Smooth), for the readers where the original's behaviour
    /// should hold (the camera, shots, flight heights; docs/data/height-readers.md). Null: SmoothHeight is Height.</summary>
    public FineGround? Smooth { get => _smooth; set => _smooth = value; }

    /// <summary>ENHANCEMENT (FINER GROUND): the smooth lookup; with finer ground off, Height itself ($36AA).</summary>
    public short SmoothHeight(ushort x, ushort z) => _smooth != null ? _smooth.Height(x, z) : Height(x, z);

    /// <summary>ENHANCEMENT (FINER GROUND): the shot types $52-$5F (re/formats/weapons.md 2, the record's type word
    /// +$04) - they take the smooth ground, so a shot flies and lands as the original's would.</summary>
    public static bool IsShot(Entity e) => e.W(4) is >= 0x52 and <= 0x5F;

    /// <summary>
    /// $374C for entity a4 = $250(a5) (the player): x = $4A(a4), y = $4C(a4), z = $4E(a4).
    /// distance = long $1526(a5), lift = word $1532(a5).
    /// ENHANCEMENT (multiplayer piece mp_view_angle): angle $80 - a half turn, facing south -
    /// flips the distance term's sign (depth 2z + $1526 grows southward; south.md A1) and moves
    /// the depth clamp to the NORTH edge (the look point past z = $FFFF, the same pin the south
    /// clamp gives z = 0; A20). Angle 0 runs the original's exact instructions.
    /// </summary>
    public Camera Place(ushort x, short y, ushort z, int distance, short lift, byte angle = 0)
    {
        var c = new Camera { Angle = angle };
        int d0 = x, d1 = z;
        d0 = d0 * 2 + 0x1000;
        d1 = d1 * 2 + (angle == 0 ? -distance : distance);
        if (angle == 0) { if (d1 < 0) d1 = 0; }
        else if (d1 > 0x1FFFE) d1 = 0x1FFFE;
        c.G154C = (short)(d1 & 0x3FE);
        c.Frac1538 = (short)(0x1000 - (d0 & 0x3FE));
        c.Frac153A = (short)(0x1C00 - (d1 & 0x3FE));
        c.G1544 = d1;
        c.G1548 = d0 - 0x1000;
        var saved = d1;
        d0 >>= 10;
        d1 >>= 10;
        c.G1540 = (short)(((short)d0 >> 3) - 1);
        c.G1542 = (short)((short)d1 >> 3);
        d1 = (short)(d1 + 7);                                // $37CA addi.w #7: the far edge (and the map offset)
        c.Col153C = (short)d0;
        c.Row153E = (short)(d1 + (FarView ? AddRows : 0));   // $153E: the cell mask walks 7 + AddRows rows with far view
        c.MapOffset1550 = ((short)d1 * 128 + (short)d0) * 2;
        // $37EE: ground under the camera's look point. ENHANCEMENT (FINER GROUND): the smooth lookup - the view's
        // height rides the land's shape, not each bump (the drawn ground is never 8 above it, the eye $5A + lift)
        var h = SmoothHeight((ushort)((uint)c.G1548 >> 1), (ushort)((uint)saved >> 1));
        var top = y;
        if (top < h) top = h;
        c.Height0E = (short)(top + 0x5A + lift);
        return c;
    }

    /// <summary>$36AA: ground height (x16) at world (x, z), bilinear over the cell. The type byte
    /// only sets the triangle flag $1510, which nothing here uses; sea cells interpolate too.</summary>
    public short Height(ushort x, ushort z)
    {
        if (_fine != null) return _fine.Height(x, z);              // ENHANCEMENT (FINER GROUND)
        int fx = x & 0x1FF, fz = z & 0x1FF;
        int col = x >> 9, row = z >> 9;
        int off = (row * 128 + col) * 2;
        fx <<= 3;
        fz <<= 3;
        int h00 = _map[off] << 4, h01 = _map[off + 0x100] << 4;
        int a = Lerp(h00, h01, fz, _maths32);
        int h10 = _map[off + 2] << 4, h11 = _map[off + 0x102] << 4;
        int b = Lerp(h10, h11, fz, _maths32);
        return (short)Lerp(a, b, fx, _maths32);
    }

    /// <summary>$36AA as movement uses it: the height, $1510 (the point's triangle is land) and the
    /// returned N flag (cell type byte bit 7: the cell cannot be entered). See movement.md.</summary>
    public (short Height, bool Land, bool Blocked) Sample(ushort x, ushort z, bool smooth = false)
    {
        int off = ((z >> 9) * 128 + (x >> 9)) * 2;
        var t = _map[off + 1];                                   // $36D2 -> $1511
        bool land;
        if (t == 0) land = false;                                // $36DA
        else
        {
            var k = ((t << 1) & 0xFF) >> 5;                      // $36DC add.b, lsr.b #5
            k -= 2;
            if (k < 0) land = true;                              // $36E2 bmi: sne with Z clear
            else
            {
                int fz = z & 0x1FF, fx = x & 0x1FF;
                var odd = (k & 1) != 0;                          // $36E6 lsr.b #1 -> carry
                k >>= 1;
                if (odd) fz = ~fz & 0x1FF;
                k += k;                                          // $36EE
                if (fz >= fx) k++;                               // $36F0 cmp, blt
                land = (9 >> (k & 7) & 1) != 0;                  // $36F6 btst d0,#9
            }
        }
        return (smooth ? SmoothHeight(x, z) : Height(x, z), land, (t & 0x80) != 0);   // smooth: ENHANCEMENT (FINER GROUND), shots
    }

    // $3710-$371A: (d1 - d0) * f / $100 (divs.w), asr.w #4, + d0, all in words.
    // ENHANCEMENT (Play.Maths32): on, the same lerp in plain 32-bit ints - diff = d1 - d0,
    // p = diff * f, q = p / $100 (truncate toward zero, as divs), (q >> 4) + d0; no wraps, no
    // overflow rule.
    static int Lerp(int d0, int d1, int f, bool maths32)
    {
        if (maths32) return (((d1 - d0) * f / 0x100) >> 4) + d0;
        var p = M68.Muls((short)(d1 - d0), f);
        p = M68.Divs(p, 0x100);
        return (short)(((short)p >> 4) + d0);
    }

    /// <summary>$3AD4: which of the 7 x 7 cells are drawn; template at $3AA3 trims the near corners.
    /// FarView: 11 rows - the same walk with the loop bound grown; rows past the map's north edge
    /// still drop out at the d5 &lt; 0 test, and the template's near-corner cut (its last three
    /// rows) is unchanged - the near side draws exactly as before.
    /// ENHANCEMENT (mp_view_ground): at $80 the row index starts below Row153E (14 rows to the
    /// south edge, FarView 18) and GROWS toward the north edge: the wrap-to-drop test is
    /// (short)d5 &lt; 0 with the same $8000 trick (a start of -1 wraps to $7F7F &gt; $7F, so rows past
    /// the map's south edge all drop), rows drop past $7F (the north edge) and the template's rows
    /// run in reverse order - its columns are symmetric, so only the rows flip.</summary>
    public bool[] Visible(Camera c)
    {
        var mirror = c.Angle != 0;
        int d4 = c.Col153C + Extra, d5 = c.Row153E;                     // wide: the walk starts Extra columns further east
        if (mirror) d5 -= FarView ? 14 + AddRows : 14;
        if ((short)d4 > 0x7F) d4 = (short)(d4 - 0x80 + 0x8000);
        if (mirror ? (short)d5 < 0 : (short)d5 >= 0x7F) d5 = (short)(d5 - 0x80 + 0x8000);
        var rows = Rows;
        var cols = Cols;
        var v = new bool[rows * cols];
        for (int r = 0, i = 0; r < rows; r++)
        {
            int d0 = d4;
            d5 = (short)(d5 + (mirror ? 1 : -1));
            if (mirror ? d5 > 0x7F : d5 < 0) d0 = -1;
            var tr = mirror ? rows - 1 - r : r;                         // the template row
            for (var k = 0; k < cols; k++, i++)
            {
                d0 = (short)(d0 - 1);
                var mk = k - Extra;                                      // the original's column 0-6, or outside it
                // the template ($3AA3) on the original's 7 columns; the added side columns draw on every
                // row but the three nearest, where they sit beside and behind the camera, off the screen
                var ot = tr - (rows - 7);                                // the original's template row (0-6), < 0 = an added far row
                var inMask = mk >= 0 && mk < 7 ? ot < 0 || CellMask[28 + ot * 7 + mk] : tr < rows - 3;
                v[i] = d0 >= 0 && inMask;
            }
        }
        return v;
    }

    /// <summary>The $3AA3 template with four full far rows on top: the 7-row case reads it from
    /// (11 - 7) * 7 on and sees the original's 49 bytes exactly.</summary>
    static readonly bool[] CellMask = Array.ConvertAll(new[]
    {
        1,1,1,1,1,1,1, 1,1,1,1,1,1,1, 1,1,1,1,1,1,1, 1,1,1,1,1,1,1, 1,1,1,1,1,1,1,
        1,1,1,1,1,1,1, 1,1,1,1,1,1,1, 1,1,1,1,1,1,1,
        0,1,1,1,1,1,0, 0,1,1,1,1,1,0, 0,0,1,1,1,0,0,
    }, b => b != 0);

    /// <summary>$3B1C: project the 8 x 8 grid points ($3810: x, y words), far row first, right to left.
    /// FarView: the sliding far row moves four map rows out (depth $2900) and four full rows on their map rows
    /// fill the gap, $400 depth steps; the original's six full rows and near row follow unchanged.
    /// ENHANCEMENT (multiplayer piece mp_view_ground): at angle $80 the SAME walk, half turned - the far
    /// row is the SOUTH edge (a4 = MapOffset - $E00, FarView $1200), the x ladder negated (start
    /// -(Frac1538 &lt;&lt; 8), step +$40000: east draws screen-left, facing south) with the column walk and
    /// its sample columns unchanged, rows step +$100 net toward the camera (north) and the near row's
    /// beyond-edge neighbour is +$100 (north). Depths are unchanged: the turned depth also falls $400 a
    /// row coming nearer, and the far row's blend neighbour stays a4 - $100 (beyond the far edge is south
    /// here). Angle 0 runs the original's exact instructions.</summary>
    public short[] Project(Camera c)
    {
        var rows = Rows + 1;                                     // point rows
        var pts = Pts;
        var p = new short[rows * pts * 2];
        var o = 0;
        var mirror = c.Angle != 0;
        var a4 = c.MapOffset1550 + 2 * Extra;                    // wide: the walk starts Extra columns further east
        if (mirror) a4 -= FarView ? 0xE00 + AddRows * 0x100 : 0xE00;
        var x0 = (c.Frac1538 << 8) + Extra * 0x40000;            // a row's first x (the original's, Extra columns east)
        if (mirror) x0 = -x0;
        int d6 = x0;
        var dxc = mirror ? 0x40000 : -0x40000;                   // the x ladder's column step
        var rowStep = (mirror ? 0x100 : -0x100) + 2 * pts;       // a4's per-row step (with the columns' -2 each: net ±$100; 8 points: -$F0 / $110)
        var nearNb = mirror ? 0x100 : -0x100;                    // the near row's beyond-edge neighbour
        int d4 = ~c.Frac153A & 0x3FE;
        if (FarView)
        {
            // ENHANCEMENT (FarView): the far row - the one row that slides with the camera, height interpolated by the
            // z fraction at a fixed depth - moves four map rows out to $2900; the four rows between it and the
            // original's six are FULL rows on their map rows, like the original's (lead's fix: the first version
            // interpolated all four, so the ground's colours, fixed to map cells, slid against its shape and hills
            // popped). Off: the original's far row at a4 and $1900, unchanged below.
            var af = mirror ? a4 : a4 + AddRows * 0x100;
            for (var i = 0; i < pts; i++)
            {
                Store(ref o, p, c, d6, FarDepth(af, d4, c), 0x1900 + AddRows * 0x400, af >> 1);
                af -= 2;
                d6 += dxc;                                               // the ladder's step flips at $80
            }
            var d5f = (short)(c.Frac153A - 0x300 + AddRows * 0x400);     // the full rows between, $400 steps in
            for (var r = 0; r < AddRows; r++)
            {
                af += rowStep;
                int x = x0;
                for (var i = 0; i < pts; i++)
                {
                    Store(ref o, p, c, x, FullRowDepth(af >> 1, c), d5f, af >> 1);
                    af -= 2;
                    x += dxc;
                }
                d5f = (short)(d5f - 0x400);                              // depths fall $400 a row at both angles
            }
            a4 = af;                                                     // = map row a4 - 16, as after the original's far row: its six full rows follow
        }
        else
        {
            // far row: height interpolated toward the next row by the z fraction
            for (var i = 0; i < pts; i++)
            {
                Store(ref o, p, c, d6, FarDepth(a4, d4, c), 0x1900, a4 >> 1);   // beyond the far edge: a4 - $100 at both angles (south there)
                a4 -= 2;
                d6 += dxc;
            }
        }
        // six full rows, depth stepping by $400
        var d5 = (short)(c.Frac153A - 0x300);
        for (var r = 0; r < 6; r++)
        {
            a4 += rowStep;
            int x = x0;
            for (var i = 0; i < pts; i++)
            {
                Store(ref o, p, c, x, FullRowDepth(a4 >> 1, c), d5, a4 >> 1);
                a4 -= 2;
                x += dxc;
            }
            d5 = (short)(d5 - 0x400);
        }
        // near row: pulled in to the screen edge depth
        a4 += rowStep;
        {
            int d4n = 0x400;
            int d5l = d5;                                   // ext.l
            var lo = (short)(d5 + 0x300);                   // subi.w #$FD00
            d5l = M68.SetW(d5l, lo);
            if (d5 < -0x300) d4n += d5l;                    // bge fails: add.l
            int dz = (short)(d4n - 0x300);                  // addi.w #$FD00 to the copy
            int x = x0;
            d4n <<= 8;
            for (var i = 0; i < pts; i++)
            {
                Store(ref o, p, c, x, NearDepth(a4, nearNb, d4n, c), dz, a4 >> 1);   // beyond the near edge flips side at $80
                a4 -= 2;
                x += dxc;
            }
        }
        return p;
    }

    // The sliding far row's point: its height blended toward the row beyond (a4 - $100) by the z fraction d4
    // ($3B1C's divs pair on the bytes, the difference wrapped to a byte). ENHANCEMENT (FINER GROUND): the same
    // blend of the fine store's points, in 32 bits - h(beyond) + (h - h(beyond)) * d4 / $400.
    int FarDepth(int a4, int d4, Camera c)
    {
        if (_fine != null)
        {
            int ha = FinePoint(a4), hb = FinePoint(a4 - 0x100);
            return (short)(c.Height0E - (hb + (ha - hb) * d4 / 0x400)) << 8;
        }
        int d0 = 0x400, d1 = d4;
        int d2 = (sbyte)(byte)(_map[a4] - _map[a4 - 0x100]);
        if (d2 != 0) d0 = M68.Divs(d0, d2);
        d1 = M68.Divs(d1, d0);
        d1 = (sbyte)(byte)(d1 + _map[a4 - 0x100]);
        return Depth(d1, c);
    }

    // The pulled-in near row's point: blended toward its beyond-edge neighbour a4 + nb by d4n ($400 = the point
    // itself). ENHANCEMENT (FINER GROUND): the fine store's points, in 32 bits, as FarDepth.
    int NearDepth(int a4, int nb, int d4s, Camera c)              // d4s = d4n << 8, as the loop holds it
    {
        if (_fine != null)
        {
            int ha = FinePoint(a4), hb = FinePoint(a4 + nb);
            return (short)(c.Height0E - (hb + (int)((long)(ha - hb) * d4s / 0x40000))) << 8;
        }
        int d1 = d4s;
        int d2 = (sbyte)(byte)(_map[a4] - _map[a4 + nb]);
        if (d2 != 0) d1 = M68.Divs(d1, M68.Divs(0x400, d2));
        d1 >>= 8;
        d1 = (sbyte)(byte)(d1 + _map[a4 + nb]);
        return Depth(d1, c);
    }

    // ENHANCEMENT (FINER GROUND): the height (world units) of the map point whose height byte is at offset a4 -
    // the fine store's point on the map; outside the map's 128 x 129 points, the bytes the original reads there << 4.
    int FinePoint(int a4)
    {
        var e = a4 >> 1;
        if (a4 < 0 || (a4 & 1) != 0 || e >= 129 * 128) return _map[a4] << 4;
        return _fine!.H[(e >> 7) * _fine.N * _fine.Side + (e & 0x7F) * _fine.N];
    }

    // height byte -> camera-relative y, << 8 ($3B60-$3B6A)
    static int Depth(int h, Camera c) => (short)(c.Height0E - (short)(h << 4)) << 8;

    // A FULL-ROW grid point's height through Height ($36AA): on a grid point that is the map byte << 4, byte for
    // byte Depth(byte). The sliding far row and the pulled-in near row keep the raw bytes: their points sit
    // BETWEEN map rows.
    int FullRowDepth(int entry, Camera c)
    {
        var h = Height((ushort)((entry & 0x7F) << 9), (ushort)((entry >> 7) << 9));
        return (short)(c.Height0E - h) << 8;                             // Depth ($3B60) with h already << 4
    }


    void Store(ref int o, short[] p, Camera c, int x, int y, int depth, int entry)
    {
        if ((short)depth != 0)
        {
            x = M68.Divs(x, depth);
            y = M68.Divs(y, depth);
        }
        p[o++] = (short)(x + 0xA0);
        p[o++] = (short)(y + 0x2E);
    }

    // $3922: the 6 edges of a cell (vertex pairs); $3930: faces by type bits 4-6 (edge lists)
    static readonly byte[] CellEdges = { 0, 1, 1, 2, 2, 3, 3, 0, 3, 1, 0, 2 };
    static readonly byte[][] CellFaces =
    {
        new byte[] { 0, 1, 2, 3 }, new byte[] { 0, 1, 2, 3 }, new byte[] { 1, 2, 4 }, new byte[] { 0, 1, 5 },
        new byte[] { 4, 3, 0 }, new byte[] { 5, 2, 3 }, new byte[] { 4, 3, 0 }, new byte[] { 4, 3, 0 },
    };

    /// <summary>Tests only (ViewDistanceTests): told the type byte's map offset of every cell the walk draws (land,
    /// inside the template). Null in the game.</summary>
    public Action<int>? CellVisited;

    /// <summary>
    /// $39B0: 7 rows of 7 cells, far to near; after each row, drawRow(row) draws that row's
    /// objects (list at $4D10 + row * $20). Cell type byte: colour = low nibble, shape = bits 4-6.
    /// FarView: 11 rows - the four extra far cell rows draw first, then the original's 7.
    /// ENHANCEMENT (mp_view_ground): at $80 the cell walk starts at the SOUTH edge
    /// (a4 = MapOffset - ($E01/$1201), the type byte of cell (R_far', cc-1)), steps +$10E a row
    /// (net +$100) and the x-slope test d2 negates (facing south flips front-facing) - the corner
    /// order stays walk-relative, so CellFaces's map corners come out mirrored on screen, correct.
    /// </summary>
    public void Draw(View v, Raster r, Camera c, Action<int>? drawRow = null)
    {
        var p = Project(c);
        var vis = Visible(c);
        var mirror = c.Angle != 0;
        var a4 = c.MapOffset1550 + (FarView ? AddRows * 0x100 : 0) - 0x100 - 1 + 2 * Extra;   // type byte of the cell's far-right corner
        if (mirror) a4 = c.MapOffset1550 - (FarView ? 0xE01 + AddRows * 0x100 : 0xE01) + 2 * Extra;   // the walk's far side is south of the camera
        var cols = Cols;
        var pts = Pts;
        var rowStep = (mirror ? 0x100 : -0x100) + 2 * cols;             // a4's per-row step (with the cells' -2 each: net ±$100; 7 cells: -$F2 / $10E)
        var pt = 0;                                         // index into the grid (pts points a row)
        var quad = _quad;
        var rows = Rows;
        for (int row = 0, vi = 0; row < rows; row++)
        {
            for (var cell = 0; cell < cols; cell++, vi++, a4 -= 2, pt++)
            {
                if (!vis[vi]) continue;
                int t = _map[a4];
                if (t == 0) continue;
                CellVisited?.Invoke(a4);
                // $39E0: corners = point+1, point, below, below+1 (each x, y)
                Copy(p, pt + 1, quad, 0);
                Copy(p, pt, quad, 1);
                Copy(p, pt + pts, quad, 2);
                Copy(p, pt + pts + 1, quad, 3);
                var d3 = (short)(quad[7] - quad[1]);
                var d2 = (short)(mirror ? quad[3] - quad[5] : quad[5] - quad[3]);
                if ((d3 & d2) < 0) continue;               // both edges run upward: cell faces away
                var tb = (sbyte)(byte)t;
                if (tb < 0x20 && (d2 ^ d3) < 0)            // twisted quad: two triangles
                {
                    var col = t & 0x0F;
                    Cell(v, r, quad, col | 0x20, mirror);
                    Cell(v, r, quad, col | 0x40, mirror);
                }
                else Cell(v, r, quad, t, mirror);
            }
            pt++;                                           // skip the row's 8th point
            drawRow?.Invoke(row);
            a4 += rowStep;
        }
    }

    static void Copy(short[] p, int i, short[] q, int j)
    {
        q[j * 2] = p[i * 2];
        q[j * 2 + 1] = p[i * 2 + 1];
    }

    static byte[] _faceScratch = new byte[4];
    readonly short[] _quad = new short[8];                  // a cell's four corners, refilled per cell

    // mirror: ENHANCEMENT (mp_view_finish, the $80 seams): the fill ($7800) picks its left/right
    // boundary walkers from the face's edge ORDER, and rounds each side a half pixel outward
    // (left only when its slope runs down-left, right only down-right, Raster.cs:241/:250) - so
    // neighbouring cells overlap a little along the shared edge and never show a gap. The half
    // turn mirrors the picture, flipping every quad's screen winding: the rounding then rounds
    // the shared edges APART and thin background cracks open between land cells (seams_80.png).
    // Walking the same edges backwards gives the fill the original's winding again - same
    // geometry, same corners, each shore triangle's own edge set untouched (south.md A5's
    // walk-relative corner rule). Angle 0 runs the original's exact call.
    static void Cell(View v, Raster r, short[] quad, int type, bool mirror = false)
    {
        r.Edges(CellEdges, 6, quad);
        r.Filled = true;                                    // $77B6
        var face = CellFaces[(type & 0x70) >> 4];
        if (!mirror) { r.Fill(v, face, face.Length, type & 0x0F); return; }
        var n = face.Length;
        for (var i = 0; i < n; i++) _faceScratch[i] = face[n - 1 - i];
        r.Fill(v, _faceScratch, n, type & 0x0F);
    }
}
