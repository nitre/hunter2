using System;
using System.Collections.Generic;
using System.Linq;

namespace Hunter;

/// <summary>in the objfile both flying helicopters' main rotor hub (the turning corner, flag $14) stands
/// 32 units above the roof, joined to it only by an edge no face lists (model $19 edge 65, corners 41-42; model $17
/// edge 48, corners 24-25) - the original draws edges through faces alone, so the blades hang in the air. The card's copy
/// gets a MAST: a round post from the roof up into the rotor head, a wider drum round the hub that the blade roots come
/// out of: the post a 12-sided ring of radius 12, the head a 16-sided ring of radius
/// 16, in whole units (the objfile's 4-unit steps and its 15-edge faces are its format, lifted on the card: "technical limits
/// we remove", 2026-11-12). The corners go after the last part
/// (fixed, the mast does not turn); grey 8 like the blades, a machine part that keeps its paint (GpuWorld MODEL COLOURS).
/// Its faces carry Origin so the bake draws them as plain faces (not an interior's, seen only through glass). Model $1B,
/// once called the landed helicopter, is the light aircraft (kind $07): its sheets at y 80 are wings, and it gets nothing.
/// The rules keep their objfile.</summary>
public static class RotorMast
{
    /// <summary>The face origin GpuWorld.Bake reads as a mast face (CarInside uses -1 and -(2 + red), Fireball -1000).</summary>
    public const int Origin = -1001;

    public const int Colour = 8;                                     // grey, the blades' own
    const int MastRadius = 12, MastSides = 12;                        // full units; the head narrower than a blade root
    const int HeadRadius = 16, HeadSides = 16;
    const int HeadBelow = 8, HeadAbove = 12;                         // the head round the hub: from 8 under the blade plane to 12 over it ($17's
                                                                     // pitched blades run 8 under the hub at their trailing edge: the head holds them)

    /// <summary>Where each helicopter's rotor stands (full units): the hub, the roof under it, and the mast's own middle (on $19
    /// the roof runs x -16..8 round a hub at x 0, so the post stands at x -4 to keep on it; the head is round the hub).</summary>
    static readonly (int Model, int HubX, int MastX, int Z, int RoofY, int HubY)[] Masts =
    [
        (0x17, 0, 0, 104, 120, 152),                                 // helicopter (kind $05): roof corner 24, hub corner 25
        (0x19, 0, -4, 100, 108, 140),                                // helicopter 2 (kind $06, the one with the tail rotor): roof corner 41, hub corner 42
    ];

    // EVEN BLADES: with the head round the hub
    // the blades no longer have to meet at the turning corner, so each is an even-width rectangle from inside the head out to
    // its tip, centred on the line through the hub (was: a spike pinched to the hub - the straight blades of 2026-10-31,
    // before that the original's kinked wedge). The hub corner is the part's pivot and the blade faces used it, so each rotor
    // becomes a new part at the end of the corners (a pivot with the old one's flag and place, then the blades' corners);
    // the old blade faces keep their numbers and windings (the Rotors table and the thin sheets go by them) but take the new
    // corners. The old corners stay where they were, unused. Full units: (model, old pivot corner, the faces, the blades as
    // corner loops). $17's blades keep their pitch (leading edge 8 above the trailing one).
    static readonly (int Model, int Pivot, int[][] Faces, (int X, int Y, int Z)[][] Blades)[] Rotors =
    [
        (0x19, 42, [[0, 38], [1, 37]],                               // helicopter 2's main rotor: flat at y 140, 24 wide, to 308 out
            [[(-8, 140, 88), (-308, 140, 88), (-308, 140, 112), (-8, 140, 112)], [(8, 140, 112), (308, 140, 112), (308, 140, 88), (8, 140, 88)]]),
        (0x19, 28, [[10, 16], [11, 17]],                             // its tail rotor at x 8: 8 wide, up to y 140 and down to 12
            [[(8, 76, -296), (8, 140, -296), (8, 140, -288), (8, 76, -288)], [(8, 76, -288), (8, 12, -288), (8, 12, -296), (8, 76, -296)]]),
        (0x17, 25, [[0, 30], [1, 29]],                               // helicopter's main rotor, pitched: 32 wide, to 288 out
            [[(-8, 152, 88), (-288, 152, 88), (-288, 144, 120), (-8, 144, 120)], [(8, 152, 120), (288, 152, 120), (288, 144, 88), (8, 144, 88)]]),
    ];

    /// <summary>The even blades into the card's models (GpuWorld.StraightBlades); the rules keep theirs.</summary>
    public static void Blades(CardModels store)
    {
        foreach (var model in Rotors.Select(r => r.Model).Distinct())
        {
            if (model >= store.Models.Length) continue;
            var md = store.Models[model];
            var pile = new Pile(md);
            var faces = md.Faces.ToArray();
            foreach (var (_, pivot, faceSets, blades) in Rotors.Where(r => r.Model == model))
            {
                var hub = md.Corners[pivot];
                pile.P(hub.X, hub.Y, hub.Z, hub.Flag);                   // the new part: the old pivot's flag, channel and place
                for (var b = 0; b < blades.Length; b++)
                {
                    var ring = blades[b].Select(c => pile.P(c.X, c.Y, c.Z)).ToArray();
                    foreach (var f in faceSets[b])
                    {
                        var was = Normal(md, faces[f].Edges.Select(e => md.Edges[e]).ToArray(), k => md.Corners[k]);
                        var now = Normal(md, null, k => pile.Corner(k), ring);
                        var loop = was.X * now.X + was.Y * now.Y + was.Z * now.Z < 0 ? ring.Reverse().ToArray() : ring;   // the old winding
                        faces[f] = faces[f] with { Edges = pile.Loop(loop) };
                    }
                }
            }
            md.Replace([.. md.Corners, .. pile.Corners], [.. md.Edges, .. pile.Edges], faces);
        }
        store.Layout();
    }

    /// <summary>A face's normal (Newell's) from its edges walked into a loop, or from a loop given.</summary>
    static (double X, double Y, double Z) Normal(CardModels.Model md, CardModels.Edge[]? edges, Func<int, CardModels.Corner> at, int[]? loop = null)
    {
        if (loop is null)
        {
            var rest = edges!.ToList();
            var l = new List<int> { rest[0].A, rest[0].B };
            rest.RemoveAt(0);
            while (rest.Count > 0)
            {
                var i = rest.FindIndex(e => e.A == l[^1] || e.B == l[^1]);
                if (i < 0) break;
                l.Add(rest[i].A == l[^1] ? rest[i].B : rest[i].A);
                rest.RemoveAt(i);
            }
            if (l[0] == l[^1]) l.RemoveAt(l.Count - 1);
            loop = [.. l];
        }
        double nx = 0, ny = 0, nz = 0;
        for (var i = 0; i < loop.Length; i++)
        {
            var (p, q) = (at(loop[i]), at(loop[(i + 1) % loop.Length]));
            nx += (double)(p.Y - q.Y) * (p.Z + q.Z); ny += (double)(p.Z - q.Z) * (p.X + q.X); nz += (double)(p.X - q.X) * (p.Y + q.Y);
        }
        return (nx, ny, nz);
    }

    public static void Make(CardModels store, Dictionary<int, int[]>? origins = null)
    {
        foreach (var (model, hubX, mastX, z, roofY, hubY) in Masts)
            if (model < store.Models.Length) Build(store.Models[model], model, hubX, mastX, z, roofY, hubY, origins);
        store.Layout();
    }

    static void Build(CardModels.Model md, int model, int hubX, int mastX, int z, int roofY, int hubY, Dictionary<int, int[]>? origins)
    {
        var nf = md.Faces.Length;
        var pile = new Pile(md);
        var last = md.Corners.LastOrDefault(c => c.Flag != 0).Flag;    // the even blades' rotor parts end the corners: the mast must not turn with them
        pile.EndPart = ((last >> 4) & 7) is 1 or 2;
        // the mast is an open tube from the roof into the head: its foot would lie in the roof's plane and its top in the
        // head's bottom, and two faces in one plane flicker (seen from inside through the glass, and from under the head)
        pile.Drum(mastX, z, MastRadius, MastSides, roofY, hubY, ends: false);
        pile.Drum(hubX, z, HeadRadius, HeadSides, hubY - HeadBelow, hubY + HeadAbove, ends: true);   // the head, round the hub
        md.Replace([.. md.Corners, .. pile.Corners], [.. md.Edges, .. pile.Edges], [.. md.Faces, .. pile.Faces]);
        if (origins is not null)
            origins[model] = (origins.TryGetValue(model, out var old) ? old : Enumerable.Range(0, nf).ToArray()).Concat(pile.Faces.Select(_ => Origin)).ToArray();
    }

    /// <summary>The corners, edges and faces to add to a model, numbered after the model's own.</summary>
    sealed class Pile(CardModels.Model md)
    {
        public readonly List<CardModels.Corner> Corners = [];
        public readonly List<CardModels.Edge> Edges = [];
        public readonly List<CardModels.Face> Faces = [];
        readonly Dictionary<(int, int), int> edgeId = [];

        /// <summary>The first corner added carries the plain flag $0F (CarInside.Plain), ending the part the model's corners end in.</summary>
        public bool EndPart;

        /// <summary>A corner at these full units (the card's store: no scale steps - CardModels.Model.FullCorner).</summary>
        public int P(int x, int y, int z, int flag = 0)
        {
            if (Corners.Count == 0 && EndPart && flag == 0) flag = CarInside.Plain;
            Corners.Add(md.FullCorner(flag, x, y, z));
            return md.Corners.Length + Corners.Count - 1;
        }

        public CardModels.Corner Corner(int k) => k < md.Corners.Length ? md.Corners[k] : Corners[k - md.Corners.Length];

        /// <summary>A closed loop of corners as edge numbers (each edge made once).</summary>
        public int[] Loop(int[] ring)
        {
            var es = new int[ring.Length];
            for (var i = 0; i < ring.Length; i++) es[i] = Edge(ring[i], ring[(i + 1) % ring.Length]);
            return es;
        }

        int Edge(int a, int b)
        {
            var key = (Math.Min(a, b), Math.Max(a, b));
            if (!edgeId.TryGetValue(key, out var id)) { edgeId[key] = id = md.Edges.Length + Edges.Count; Edges.Add(new(a, b)); }
            return id;
        }

        void Face(int[] ring) => Faces.Add(new(Colour, 0, Loop(ring)));

        /// <summary>A round drum about (x, z) from y0 up to y1: two rings of sides corners each, every one rounded to a
        /// whole unit (the points must still come out distinct), a quad a side, and
        /// the top and bottom discs when ends is true (left off for an open tube). Wound as DuneModels.Shape.Box winds a box:
        /// the top ring as it goes round from +x toward +z, the bottom the other way, each side from the bottom ring up.</summary>
        public void Drum(int x, int z, int radius, int sides, int y0, int y1, bool ends)
        {
            var ring = new (int X, int Z)[sides];
            for (var k = 0; k < sides; k++)
            {
                var a = 2 * Math.PI * k / sides;
                ring[k] = (x + (int)Math.Round(radius * Math.Cos(a), MidpointRounding.AwayFromZero),
                           z + (int)Math.Round(radius * Math.Sin(a), MidpointRounding.AwayFromZero));
            }
            if (sides > CardModels.MaxFaceCorners) throw new InvalidOperationException($"model {md.Number}: a disc of {sides} corners - the card cuts faces of at most {CardModels.MaxFaceCorners}");
            if (ring.Distinct().Count() != sides) throw new InvalidOperationException($"model {md.Number}: a ring of {sides} sides and radius {radius} folds onto whole units");
            var lo = ring.Select(r => P(r.X, y0, r.Z)).ToArray();
            var hi = ring.Select(r => P(r.X, y1, r.Z)).ToArray();
            if (ends) Face(hi);
            if (ends) Face(lo.Reverse().ToArray());
            for (var k = 0; k < sides; k++) Face([lo[k], lo[(k + 1) % sides], hi[(k + 1) % sides], hi[k]]);
        }
    }
}
