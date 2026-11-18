namespace Hunter.Rules;

/// <summary>
/// The 3D view's picture: 320 x 140 palette indices, the area the original draws into
/// (`$148(a5)`, 4 planes `$48(a5)` = $15E0 bytes apart). See re/formats/renderer.md.
/// </summary>
public sealed class View
{
    public const int Width = 320, Height = 140;
    public byte[] Pixels { get; } = new byte[Width * Height];

    /// <summary>(Objects.OwnShadows): a shadow span ORs this bit
    /// into the pixels it covers instead of writing a colour - Extra Half-Brite, colour n + 32 =
    /// colour n with each channel halved. The reader of a pixel halves the looked-up colour when
    /// the bit is set (Play.Screen); a later plain colour write clears it, and Clear() overwrites
    /// it as the blitter's $FFFF clear does every frame.</summary>
    public const byte HalfBright = 0x20;

    /// <summary>$CA9A (main loop, $257A): the blitter writes $FFFF over the view (minterm $AA,
    /// C data = d2 = -1), so every frame starts as colour 15. That fill IS the sky and sea: the
    /// pixels stay index 15 and the copper changes colour 15 per row (Sky.Rows), so the GPU list
    /// gets the background as one full-view rectangle of colour 15, handed out first - every
    /// model then draws over it in list order, and the per-row palette turns it into the
    /// gradient.</summary>
    public void Clear()
    {
        Array.Fill(Pixels, (byte)15);
    }

    // $7EB8-$7F74 (picked per colour by $7CF6 with $7CF4 = $18): set pixels x0..x1 of a row
    internal void Span(int y, int x0, int x1, int colour)
    {
        var row = y * Width;
        for (var x = x0; x <= x1; x++) Pixels[row + x] = (byte)colour;
    }

    /// <summary>A shadow span: the EHB bit only, so two shadows on one pixel are still one half
    /// (as EHB) and anything drawn later over the pixel writes a plain colour through it.</summary>
    internal void MarkSpan(int y, int x0, int x1)
    {
        var row = y * Width;
        for (var x = x0; x <= x1; x++) Pixels[row + x] |= HalfBright;
    }
}

/// <summary>Edge table and scanline polygon filler, ported from $76FC and $7800.</summary>
public sealed class Raster
{
    // $6D9C: 16-byte edge records (the walk reads X, Y, Dy, Slope, Half)
    internal struct Edge
    {
        public int X; public short Y, Dy; public int Slope, Half;
    }

    readonly Edge[] _edges = new Edge[256];
    readonly int[] _list = new int[64];         // $7764: edge numbers, written twice in a row
    readonly short[] _spans = new short[2 * 160]; // $79BA
    public bool SkipFlag;                        // $1600(a5)
    public bool Filled;                          // $1601(a5)
    /// <summary>(Objects.OwnShadows): Line and Fill mark the EHB
    /// bit instead of writing a colour while set. Clear with the faces done - Faces does.</summary>
    public bool Mark;

    /// <summary>$76FC: pairs of vertex numbers -> edge records. pts = projected (x, y) words.</summary>
    public void Edges(ReadOnlySpan<byte> pairs, int count, ReadOnlySpan<short> pts)
    {
        for (var i = 0; i < count; i++)
        {
            int a = pairs[i * 2], b = pairs[i * 2 + 1];
            int d0 = pts[a * 2], d1 = pts[a * 2 + 1], d2 = pts[b * 2], d3 = pts[b * 2 + 1];
            // $7732: order by y, then x
            if (!((short)d3 > (short)d1 || ((short)d3 == (short)d1 && (short)d2 >= (short)d0)))
            {
                (d0, d2) = (d2, d0);
                (d1, d3) = (d3, d1);
            }
            int dx = (short)(d2 - d0);
            dx <<= 5;                                       // ext.l, lsl.l #5
            var dy = (short)(d3 - d1);
            if (dy != 0)
            {
                dx = M68.Divs(dx, dy);
                dx = (short)dx;                             // ext.l: low word, even after overflow
            }                                               // dy = 0: $7748 beq skips divs AND ext.l - the long dx << 5 stays
            _edges[i] = new Edge
            {
                X = (short)d0, Y = (short)d1, Dy = dy, Slope = dx, Half = dx >> 1,
            };
        }
    }

    /// <summary>$7C12: one edge drawn as a line, one span per row from x to x + slope.</summary>
    public void Line(View v, int edge, int colour)
    {
        ref var e = ref _edges[edge];
        int y = e.Y, count = e.Dy;
        var bottom = (short)(y + count);
        if (bottom < 0) return;
        var over = (short)(bottom - 0x8B);
        if (over >= 0)
        {
            count = (short)(count - over);
            if (count < 0) return;
        }
        var ycount = (short)y;                              // $15F2
        var row = y;
        if (y < 0) { count = (short)(count + y); row = 0; }
        var x = e.X << 5;
        while (true)
        {
            int x0, x1;
            do
            {
                x0 = x;
                x += e.Slope;
                ycount++;
            } while (ycount <= 0);
            x1 = x;
            x0 >>= 5;
            x1 >>= 5;
            if ((short)x1 < (short)x0) (x0, x1) = (x1, x0);
            if ((short)x1 >= 0 && (short)x0 <= 0x13F)
            {
                if ((short)x0 < 0) x0 = 0;
                if ((short)x1 >= 0x140) x1 = 0x13F;
                if (Mark) v.MarkSpan(row, x0, x1); else v.Span(row, x0, x1, colour);
            }
            row++;
            if (--count <= 0) return;
        }
    }

    /// <summary>$7800: fill a face of n edges (edge numbers in order round the face).</summary>
    public void Fill(View v, ReadOnlySpan<byte> edgeNumbers, int n, int colour)
    {
        short ymax = short.MinValue, ymin = short.MaxValue, minX = short.MaxValue, minSlope = short.MaxValue;
        var top = 0;
        for (var k = 0; k < n; k++)
        {
            int e = edgeNumbers[k];
            _list[k] = e;
            _list[k + n] = e;
            ref var ed = ref _edges[e];
            var y2 = (short)(ed.Y + ed.Dy);
            if (ymax < y2) ymax = y2;                       // $7836
            var take = false;                               // $783C: topmost, then leftmost, then shallowest
            if (ymin > ed.Y) take = true;
            else if (ymin == ed.Y)
            {
                var x = (short)ed.X;
                if (minX > x) take = true;
                else if (minX == x && minSlope > (short)ed.Slope) take = true;
            }
            if (take)
            {
                minX = (short)ed.X;
                minSlope = (short)ed.Slope;
                ymin = ed.Y;
                top = k;
            }
        }
        if (ymin >= 0x8C || ymax < 0) { SkipFlag = true; return; }
        if (ymax >= 0x8C) ymax = 0x8B;
        int rows = (short)(ymax - ymin);
        if (rows < 0) { SkipFlag = true; return; }
        if (rows != 0) rows--;

        int right = top + 1;                                // a2: walks forward through the list
        int left = top + 1 + n;                             // a3: walks backward
        ref var t1 = ref _edges[_list[left - 1]];
        ref var t2 = ref _edges[_list[right]];
        var ystart = t1.Y;                                  // $78B4: the top edge's y, read before any shift
        if ((short)t1.X != (short)t2.X || t1.Y != t2.Y)     // $78A0: the two edges must share the top point
        {
            if (!Filled) { SkipFlag = true; return; }
            right--;
            left--;
        }
        SkipFlag = false;

        int lc = 0, rc = 0, lx = 0, rx = 0, ls = 0, rs = 0, out_ = 0;
        var y = (short)(ystart - 1);
        for (var r = rows; r >= 0; r--)
        {
            // a degenerate face (seen in debris showers, the fuzzer: seeds 979, 1161, 1177 of 2026-11-02) walks off the
            // list: the original's -(a3) / (a2)+ read on into the code round $7764 and draw a stray span or two; the
            // port's list ends there, so the face's last rows are left undrawn instead
            if (lc <= 1 && left <= 0 || rc <= 1 && right >= _list.Length - 1) break;
            if (--lc <= 0)                                  // $78C8: next left edge
            {
                ref var e = ref _edges[_list[--left]];
                lx = e.X << 5;
                lc = e.Dy;
                ls = e.Slope;
                if (ls < 0) lx += e.Half;
            }
            if (--rc <= 0)                                  // $78E4: next right edge, skipping one flat edge
            {
                var e = _edges[_list[right++]];
                rc = e.Dy;
                if (rc == 0) { e = _edges[_list[right++]]; rc = e.Dy; }
                rx = e.X << 5;
                rs = e.Slope;
                if (rs >= 0) rx += e.Half;
            }
            y++;
            if (y >= 0)                                     // $7906
            {
                int a = lx, b = rx;
                if (b < a) (a, b) = (b, a);
                int x1 = b >> 5, x0;
                if (x1 < 0) { x0 = a; x1 = -1; }
                else
                {
                    if (x1 > 0x13F) x1 = 0x13F;
                    x0 = a >> 5;
                    if (x0 < 0) x0 = 0;
                    if (x1 < x0) x1 = -1;
                }
                _spans[out_++] = (short)x0;
                _spans[out_++] = (short)x1;
            }
            lx += ls;
            rx += rs;
        }

        // $793C: plot the stored rows
        int count = rows, py = ystart;
        if (py < 0) { count += py; if (count < 0) { SkipFlag = true; return; } py = 0; }
        for (int i = 0; i <= count; i++, py++)
        {
            int x0 = _spans[i * 2], x1 = _spans[i * 2 + 1];
            if (x1 < 0) continue;
            if (Mark) v.MarkSpan(py, x0, x1); else v.Span(py, x0, x1, colour);
        }
    }

    /// <summary>(Play.View2, mp_views): the second picture draws
    /// with this raster and must leave it byte for byte as the pass's own draw did. The scratch
    /// is never fully rewritten between faces (spans and edge records stop at each face's count),
    /// so a picture at another camera leaves other leftovers behind - the state hash counts the
    /// raster scratch and would call that a leak into the game. Save before the picture, Restore
    /// after; nothing in play reads the scratch between draws, so the game never sees it.</summary>
    public sealed class Saved
    {
        internal Edge[] Edges = [];
        internal int[] List = [];
        internal short[] Spans = [];
        internal bool Skip, Filled, Mark;
    }

    public Saved Save() => new()
    {
        Edges = (Edge[])_edges.Clone(),
        List = (int[])_list.Clone(),
        Spans = (short[])_spans.Clone(),
        Skip = SkipFlag, Filled = Filled, Mark = Mark,
    };

    public void Restore(Saved s)
    {
        Array.Copy(s.Edges, _edges, _edges.Length);
        Array.Copy(s.List, _list, _list.Length);
        Array.Copy(s.Spans, _spans, _spans.Length);
        SkipFlag = s.Skip; Filled = s.Filled; Mark = s.Mark;
    }
}
