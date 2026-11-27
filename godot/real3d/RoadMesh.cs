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
    /// <summary>Its depth along the road, world units: the original's line is a pixel at any distance; 48 (about a real
    /// start line on a road this wide) keeps it in sight from the grid, 8 cross-sections back.</summary>
    public const double StartDepth = 48;
    /// <summary>A height step to the next cross-section this big or more (ground units, $280) is a jump's face: colour 0.</summary>
    public const double GapStep = 0x280;
    /// <summary>The lines' width, world units: the original's are a pixel; here a 48th of the road's usual width (576).</summary>
    public const double Line = 12;

    public static MeshInstance3D Make(ScrRoad road, double ground, ShaderMaterial material)
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
        for (var i = 0; i < s.Count; i++)
        {
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
            Vector3 In(Vector3 edge, Vector3 other) => edge + (other - edge) * (float)System.Math.Min(0.25, Line / System.Math.Max(1e-3, (other - edge).Length()));
            Vector3 ali = In(al, ar), ari = In(ar, al), bli = In(bl, br), bri = In(br, bl);
            var edgeWord = oddSection ? EdgeYellow : EdgeRed;
            var face = System.Math.Abs(b.LY - a.LY) / ScrRoad.Scale >= GapStep || System.Math.Abs(b.RY - a.RY) / ScrRoad.Scale >= GapStep;
            var top = face ? 0x000 : oddPiece ? RoadB : RoadA;
            if (i == road.StartLineSection)
            {                                                                    // the start line across the top at this cross-section
                Vector3 Ahead(Vector3 from, Vector3 to) => from + (to - from) * (float)System.Math.Min(0.5, StartDepth / System.Math.Max(1e-3, (to - from).Length()));
                Vector3 alw = Ahead(ali, bli), arw = Ahead(ari, bri);
                Quad(ali, ari, arw, alw, StartLine, 1f);
                Quad(alw, arw, bri, bli, top, 1f);
            }
            else Quad(ali, ari, bri, bli, top, 1f);
            Quad(al, ali, bli, bl, edgeWord, 1f);
            Quad(ari, ar, br, bri, edgeWord, 1f);
            // the side panels, from the edge down to the land
            Vector3 alg = P(a.LX, ground, a.LZ), blg = P(b.LX, ground, b.LZ), arg = P(a.RX, ground, a.RZ), brg = P(b.RX, ground, b.RZ);
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
