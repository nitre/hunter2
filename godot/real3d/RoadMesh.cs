using Godot;
using Hunter.Rules;
using System.Collections.Generic;

namespace Hunter;

/// <summary>STUNTIN': a Stunt Car Racer road (rules/ScrRoad.cs) as one mesh for the card, drawn by road.gdshader in the
/// card's own space. As the original draws it (StuntCarRacer.s renderTrackSurface, renderLeft/RightTrackSidePanel): the
/// road's top in two tones a piece each (its main palette's colours 1 and 2), and on each side a solid panel from the
/// road's edge down to the land, two tones a piece each (colour 15 and track colour 2); the edge lines, start line and
/// jump faces as below. Vertices in world units
/// (x, height, -z), as the card's ground's; built once.</summary>
public static class RoadMesh
{
    /// <summary>The original's colour words (the main game screen's palette, imageMainGameBackgroundPalette, read from the
    /// original disk): the road's two tones, the side panels' two.</summary>
    public const int RoadA = 0x443, RoadB = 0x554, PanelA = 0x777, PanelB = 0x311;
    /// <summary>THE BORDER (renderLeft/RightRoadEdge, renderLeft/RightBarrierEdges; the colour byte +$1D set in
    /// generateTrackEdgeLines): a line along the road's left and right edge on every cross-section, colour 3 ($770
    /// yellow) on every other one (segmentAlternateRenderFlag - its phase carried across the pieces, so it runs round the
    /// lap unbroken) and colour 9 ($200 dark red) on the rest; on those red ones also a line down each side panel at the
    /// cross-section, from the edge to the land, in track colour 1 (9). The original fades the red with distance (10,
    /// 11, then none) as its fog; the card's own distance does that here.</summary>
    public const int EdgeYellow = 0x770, EdgeRed = 0x200;
    /// <summary>The start line (ScrRoad.StartLineSection): colour 15, $777, across the road between the edge lines.</summary>
    public const int StartLine = 0x777;
    /// <summary>The finish line's chequered strip: its squares across the road and along it, and their two colours.</summary>
    public const int CheckCols = 8, CheckRows = 2, CheckWhite = 0xEEE, CheckBlack = 0x111;
    /// <summary>Its depth along the road, world units: the original's line is a pixel at any distance; 48 (about a real
    /// start line on a road this wide) keeps it in sight from the grid, 8 cross-sections back.</summary>
    public const double StartDepth = 48;
    /// <summary>A height step to the next cross-section this big or more (ground units, $280) is a jump's face: colour 0.</summary>
    public const double GapStep = 0x280;
    /// <summary>The lines' width, world units: the original's are a pixel; here a 48th of the road's usual width (576).</summary>
    public const double Line = 12;
    /// <summary>How far the side panels go on into the ground under them (world units): no seam where the land's drawn surface
    /// rounds differently between its points.</summary>
    const double Sink = 32;
    /// <summary>Each strip's top drawn as Grid x Grid small quads on its smooth surface.</summary>
    const int Grid = 4;

    /// <summary>ground: the land's height under a point (x, z; world units) - the side panels reach down to it and Sink into
    /// it, to the sea's floor where the track runs out past the shore; only: the strips to build (null all) - the draw
    /// bridge's moving strips are a mesh of their own.</summary>
    public static MeshInstance3D Make(ScrRoad road, System.Func<double, double, double> ground, ShaderMaterial material, System.Func<int, bool>? only = null)
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        void Tri(Vector3 a, Vector3 b, Vector3 c, int word, float light)
        {
            v.Add(a); v.Add(b); v.Add(c);
            for (var i = 0; i < 3; i++) uv.Add(new Vector2(word, light));
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int word, float light) { Tri(a, b, c, word, light); Tri(a, c, d, word, light); }
        static Vector3 P(double x, double y, double z) => new((float)x, (float)y, (float)-z);

        var s = road.Sections;
        // the finish line's strip: each cross-section's way along from the line (its middle), for the squares over the sections it spans
        var fromLine = new double[s.Count];
        for (int k = 1, i0 = road.StartLineSection; k < s.Count; k++)
        {
            var p = s[(i0 + k - 1) % s.Count]; var q = s[(i0 + k) % s.Count];
            double dx = (q.LX + q.RX - p.LX - p.RX) / 2, dz = (q.LZ + q.RZ - p.LZ - p.RZ) / 2;
            fromLine[(i0 + k) % s.Count] = fromLine[(i0 + k - 1) % s.Count] + System.Math.Sqrt(dx * dx + dz * dz);
        }
        var lineSec = s[road.StartLineSection];
        var square = System.Math.Sqrt((lineSec.RX - lineSec.LX) * (lineSec.RX - lineSec.LX) + (lineSec.RZ - lineSec.LZ) * (lineSec.RZ - lineSec.LZ)) / CheckCols;
        for (var i = 0; i < s.Count; i++)
        {
            if (only is not null && !only(i)) continue;
            var a = s[i];
            var b = s[(i + 1) % s.Count];
            Vector3 al = P(a.LX, a.LY, a.LZ), ar = P(a.RX, a.RY, a.RZ), bl = P(b.LX, b.LY, b.LZ), br = P(b.RX, b.RY, b.RZ);
            // the top's and the walls' tone: the original's +$1C bit 0, the PIECE's number (generateTrackEdgeLines runs once
            // a piece with currentSegmentIndex = that piece) - two tones piece by piece; the edge lines alternate cross-section
            // by cross-section (segmentAlternateRenderFlag)
            var oddPiece = (road.Piece[i] & 1) != 0;
            var oddSection = (i & 1) != 0;
            // the top: the two edge lines set into it (not laid over it - no fight for the same depth) and the road between;
            // a step of $280 (640 ground units) or more up or down to the next cross-section - a jump's face - is colour 0
            // (setupTrackGeometryForFrame2 sets bit 5, renderTrackSurface draws it black)
            // THE STRIP'S SURFACE: smooth through its four corners (bilinear, u across from the left edge, v along) - the surface the
            // game's road lookup reads (ScrRoadSurface.OnStrip) - drawn as a Grid x Grid of small quads; two flat triangles creased
            // wherever a strip twists, and a car's wheels dropped into each crease
            Vector3 Bi(double u, double v) => al * (float)((1 - u) * (1 - v)) + ar * (float)(u * (1 - v)) + br * (float)(u * v) + bl * (float)((1 - u) * v);
            double inA = System.Math.Min(0.25, Line / System.Math.Max(1e-3, (ar - al).Length())), inB = System.Math.Min(0.25, Line / System.Math.Max(1e-3, (br - bl).Length()));
            double U0(double v) => inA + (inB - inA) * v;                     // the edge line's inner side
            var edgeWord = oddSection ? EdgeYellow : EdgeRed;
            var face = System.Math.Abs(b.LY - a.LY) / ScrRoad.Rise >= GapStep || System.Math.Abs(b.RY - a.RY) / ScrRoad.Rise >= GapStep;
            var top = face ? 0x000 : oddPiece ? RoadB : RoadA;
            void Rows(double v0, double v1, int rows, int word)
            {                                                                    // the top between the edge lines, v0 to v1
                for (var r = 0; r < rows; r++)
                {
                    double va = v0 + (v1 - v0) * r / rows, vb = v0 + (v1 - v0) * (r + 1) / rows;
                    for (var c = 0; c < Grid; c++)
                    {
                        Vector3 G2(double s, double v) => Bi(U0(v) + (1 - 2 * U0(v)) * s, v);
                        Quad(G2((double)c / Grid, va), G2((double)(c + 1) / Grid, va), G2((double)(c + 1) / Grid, vb), G2((double)c / Grid, vb), word, 1f);
                    }
                }
            }
            var stripLeft = CheckRows * square - fromLine[i];
            if (stripLeft > 0 && (i == road.StartLineSection || fromLine[i] > 0))
            {                                                                    // the finish line: a chequered strip across the top, CheckRows of
                double len = System.Math.Max(1e-3, fromLine[(i + 1) % s.Count] - fromLine[i]);   // squares CheckCols across, over the sections it spans
                if (fromLine[(i + 1) % s.Count] <= fromLine[i]) len = System.Math.Max(1e-3, ((bl + br) - (al + ar)).Length() / 2);
                var covered = System.Math.Min(len, stripLeft);
                for (var p0 = 0.0; p0 < covered - 1e-6;)
                {
                    var row = (int)System.Math.Floor((fromLine[i] + p0) / square + 1e-9);
                    var p1 = System.Math.Min(covered, (row + 1) * square - fromLine[i]);
                    double va = p0 / len, vb = p1 / len;
                    for (var c = 0; c < CheckCols; c++)
                    {
                        Vector3 G2(double u, double vv) => Bi(U0(vv) + (1 - 2 * U0(vv)) * u, vv);
                        Quad(G2((double)c / CheckCols, va), G2((double)(c + 1) / CheckCols, va), G2((double)(c + 1) / CheckCols, vb), G2((double)c / CheckCols, vb),
                            (row + c & 1) == 0 ? CheckWhite : CheckBlack, 1f);
                    }
                    p0 = p1;
                }
                if (covered < len) Rows(covered / len, 1, Grid, top);
            }
            else Rows(0, 1, Grid, top);
            for (var r = 0; r < Grid; r++)
            {                                                                    // the edge lines
                double va = (double)r / Grid, vb = (double)(r + 1) / Grid;
                Quad(Bi(0, va), Bi(U0(va), va), Bi(U0(vb), vb), Bi(0, vb), edgeWord, 1f);
                Quad(Bi(1 - U0(va), va), Bi(1, va), Bi(1, vb), Bi(1 - U0(vb), vb), edgeWord, 1f);
            }
            // the side panels, from the edge down to the land
            Vector3 G(double x, double z) => P(x, ground(x, z) - Sink, z);
            Vector3 alg = G(a.LX, a.LZ), blg = G(b.LX, b.LZ), arg = G(a.RX, a.RZ), brg = G(b.RX, b.RZ);
            var panel = oddPiece ? PanelB : PanelA;
            if (oddSection)
            {
                Quad(al, bl, blg, alg, panel, 1f);
                Quad(ar, arg, brg, br, panel, 1f);
            }
            else
            {                                                                    // a red cross-section: the panel's line down at its start
                Vector3 Along(Vector3 from, Vector3 to) => from + (to - from) * (float)System.Math.Min(0.25, Line / System.Math.Max(1e-3, (to - from).Length()));
                Vector3 alp = Along(al, bl), algp = Along(alg, blg), arp = Along(ar, br), argp = Along(arg, brg);
                Quad(al, alp, algp, alg, EdgeRed, 1f);
                Quad(alp, bl, blg, algp, panel, 1f);
                Quad(ar, arg, argp, arp, EdgeRed, 1f);
                Quad(arp, argp, brg, br, panel, 1f);
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return new MeshInstance3D { Mesh = mesh, MaterialOverride = material, CustomAabb = GpuWorld.Huge };
    }
}
