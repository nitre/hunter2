using System;
using System.Collections.Generic;
using System.Linq;

namespace Hunter;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (row SHARKS AND SUBS, the card-drawn 3D view; owner 2026-11-11: "flesh out the shark
/// and submarine models"; the shark first asked 2026-11-02: "make sharks more than just the fin"): the card's copy of the
/// shark (type $18, model 50: the original's lone black fin) and the submarine (type $0A, model 31: a periscope) given
/// whole bodies. The rules keep their own objfile (boxes, collisions, +$7A): the picture only, like RoundWheels.
///
/// THE WATER LINE. The game lifts every thing till its lowest corner touches the ground ($51F8), so a body hung under the
/// fin would lift the shark out of the sea (the parked cloud/shark branch). Each body here is built with the water at
/// model y 0 - the original's own lowest corner - and the model carries Waterline = how far its new lowest corner is below
/// that (CardModels.Model.Waterline). The card adds it to the lowest corner it measures (model.gdshaderinc model_min_y,
/// ground_contact.gdshaderinc), so the lift is the original's: the fin's foot and the hull's water line sit where the game
/// puts them, and the sea (an opaque plane) hides what is under. What shows: the shark's back breaking the water round its
/// fin and the upper lobe of its tail behind; the submarine's sail with the periscope on it, the deck awash and the upper
/// rudder. Under the sea: the rest, seen when it blows apart (EXPLOSIONS: PIECES) and as a dark shape with SHADOWS: MORE.
///
/// THE SHARK keeps its six corners first and as they were (0 the pivot, 1-5 the fin) and its fin face: the whole body
/// rides the fin's part (pivot flag $14: a turn about y on channel 0, animation set 9 stepping 0..24), so the shark sways
/// as the fin always did. Grey (8) body, black (0) tail lobes like the fin. Shift 2 as the file's.
///
/// THE SUBMARINE keeps its fourteen corners first and in order (the header's special corner is number 14), so the
/// periscope is still the one part (pivot $14, channel 0 on the record's byte +$84): it now stands on the sail's top. Its
/// scale goes 0 -> 1 (the file's bytes reach only 127 at 0); every periscope corner keeps its place in full units but the
/// 35 high top, which goes to 36 (odd: not a shift-1 byte). The hull starts with a plain corner (flag $0F), so it holds
/// still while the periscope turns, and it turns with the boat's heading. Dark green (1) hull, the darker deck (12), the
/// sail in grass mid (13).</summary>
public static class SeaBodies
{
    public const int Shark = 50, Submarine = 31;

    /// <summary>A plain corner flag (kind 0, as the file's own $0F): ends the part before it (CarInside.Plain).</summary>
    const int Plain = 0x0F;

    /// <summary>Both are built closed: no back walls (ModelCaps) - its extra points would land on the bodies' own corners.</summary>
    public static bool Closed(int model) => model is Shark or Submarine;

    public static void Make(CardModels store, Dictionary<int, int[]>? origins = null)
    {
        if (store.Models.Length > Shark && IsOriginal(store.Models[Shark], 6, 1)) BuildShark(store.Models[Shark], origins);
        if (store.Models.Length > Submarine && IsOriginal(store.Models[Submarine], 14, 9)) BuildSubmarine(store.Models[Submarine], origins);
        store.Layout();
    }

    static bool IsOriginal(CardModels.Model md, int corners, int faces) => !md.Replaced && md.Corners.Length == corners && md.Faces.Length == faces;

    /// <summary>A model's new lists: the original's first, then what is built here (raw model units: the file's bytes before the shift).</summary>
    sealed class Build(CardModels.Model md)
    {
        public readonly List<CardModels.Corner> Corners = [.. md.Corners];
        public readonly List<CardModels.Edge> Edges = [.. md.Edges];
        public readonly List<CardModels.Face> Faces = [.. md.Faces];
        readonly Dictionary<(int, int), int> edgeId = md.Edges.Select((e, i) => (e, i)).GroupBy(t => (Math.Min(t.e.A, t.e.B), Math.Max(t.e.A, t.e.B))).ToDictionary(g => g.Key, g => g.First().i);

        public int P(int x, int y, int z, int flag = 0)
        {
            if (x is < -128 or > 127 || y is < -128 or > 127 || z is < -128 or > 127) throw new InvalidOperationException($"model {md.Number}: corner ({x}, {y}, {z}) does not fit the file's bytes");
            Corners.Add(md.RawCorner(flag, x, y, z));
            return Corners.Count - 1;
        }

        public void F(int colour, params int[] loop)
        {
            var e = new int[loop.Length];
            for (var i = 0; i < loop.Length; i++)
            {
                int a = loop[i], b = loop[(i + 1) % loop.Length];
                var key = (Math.Min(a, b), Math.Max(a, b));
                if (!edgeId.TryGetValue(key, out var id)) { edgeId[key] = id = Edges.Count; Edges.Add(new(a, b)); }
                e[i] = id;
            }
            Faces.Add(new(colour, 0, e));                              // flags 0: a filled face, as the solid models'
        }

        /// <summary>A ring of corners round z at height cy, (x, y) offsets each.</summary>
        public int[] Ring(int z, int cy, IEnumerable<(int X, int Y)> pts) => pts.Select(p => P(p.X, cy + p.Y, z)).ToArray();

        /// <summary>Quads between each ring and the next.</summary>
        public void Tube(int[][] rings, Func<int, int> colour)
        {
            for (var s = 0; s + 1 < rings.Length; s++)
                for (var i = 0; i < rings[s].Length; i++)
                {
                    var j = (i + 1) % rings[s].Length;
                    F(colour(i), rings[s][i], rings[s][j], rings[s + 1][j], rings[s + 1][i]);
                }
        }

        /// <summary>Triangles from a ring to a tip.</summary>
        public void Cap(int[] ring, int tip, Func<int, int> colour)
        {
            for (var i = 0; i < ring.Length; i++) F(colour(i), tip, ring[i], ring[(i + 1) % ring.Length]);
        }

        /// <summary>Swap the lists in. Waterline: how far the lowest corner at rest is below model y 0 (full units).</summary>
        public void Done(Dictionary<int, int[]>? origins, int model, int fileFaces)
        {
            md.Replace(Corners.ToArray(), Edges.ToArray(), Faces.ToArray());
            md.Waterline = Math.Max(0, -Corners.Min(c => c.Y));
            // each card face's face in the file: the original's own, then the added ones counted as face 0 - plain paint (not
            // glass, not an interior: a negative origin would hide them behind GLASS: SEE-THROUGH)
            if (origins is not null) origins[model] = Enumerable.Range(0, Faces.Count).Select(f => f < fileFaces ? f : 0).ToArray();
        }
    }

    // ------------------------------------------------------------------ the shark (shift 2: a raw unit is 4)
    static void BuildShark(CardModels.Model md, Dictionary<int, int[]>? origins)
    {
        const int Grey = 8, Fin = 0;
        var b = new Build(md);
        // the stations nose to tail: z, half width, back, belly. The back breaks the water a little round the fin (its foot
        // is the water at y 0, z -15..6), the rest is under it
        (int Z, int W, int Top, int Bottom)[] stations = [(42, 5, 0, -8), (26, 10, 3, -14), (6, 12, 4, -16), (-14, 11, 4, -15), (-30, 7, 2, -10), (-42, 3, -1, -5)];
        var rings = stations.Select(s =>
        {
            double c = (s.Top + s.Bottom) / 2.0, h = (s.Top - s.Bottom) / 2.0;
            return Enumerable.Range(0, 8).Select(k =>                  // 8 round: the back first, then down the right side
            {
                var a = k * Math.PI / 4;
                return b.P((int)Math.Round(s.W * Math.Sin(a), MidpointRounding.AwayFromZero), (int)Math.Round(c + h * Math.Cos(a), MidpointRounding.AwayFromZero), s.Z);
            }).ToArray();
        }).ToArray();
        var nose = b.P(0, -3, 52);
        b.Cap(rings[0].Reverse().ToArray(), nose, _ => Grey);
        b.Tube(rings, _ => Grey);
        var stem = rings[^1];
        // the tail: an upper lobe rising out of the water (a second fin behind the first) and a shorter lower one
        int upTip = b.P(0, 15, -62), notch = b.P(0, 0, -52), lowTip = b.P(0, -13, -57);
        b.F(Grey, stem.Reverse().ToArray());                          // the stem closed
        b.F(Fin, stem[0], upTip, notch);
        b.F(Fin, stem[4], notch, lowTip);
        b.F(Fin, stem[0], notch, stem[4]);                            // the tail's root between the lobes
        // the pectoral fins, swept back from the belly's sides
        b.F(Grey, rings[1][3], rings[2][3], b.P(26, -16, -2));
        b.F(Grey, rings[1][5], rings[2][5], b.P(-26, -16, -2));
        b.Done(origins, Shark, 1);
    }

    // ------------------------------------------------------------------ the submarine (shift 0 -> 1: a raw unit is 2)
    /// <summary>The sail's top (raw y) and how far forward the periscope stands on it (raw z).</summary>
    const int SailTop = 30, PeriscopeZ = 40;

    static void BuildSubmarine(CardModels.Model md, Dictionary<int, int[]>? origins)
    {
        const int Hull = 1, Deck = 12, Sail = 13;
        var file = md.Corners.ToArray();
        md.Shift = 1;
        var b = new Build(md);
        for (var k = 0; k < file.Length; k++)                         // the periscope up on the sail: full units, the 35 top to 36
        {
            var c = file[k];
            var y = (c.Y + 1) / 2 * 2;                                 // odd heights up one (35 -> 36), even ones as they were
            b.Corners[k] = md.RawCorner(c.Flag, c.X / 2, y / 2 + SailTop, c.Z / 2 + PeriscopeZ);
        }
        // the hull: a ring of 10 (the deck's two edges first, down the right side, up the left), round y -10, scaled along
        (int X, int Y)[] section = [(5, 13), (13, 4), (14, -3), (10, -11), (4, -16), (-4, -16), (-10, -11), (-14, -3), (-13, 4), (-5, 13)];
        (int Z, double S)[] stations = [(104, 0.45), (88, 0.8), (64, 1.0), (-40, 1.0), (-72, 0.75), (-96, 0.42)];
        var first = true;
        var rings = stations.Select(st => section.Select(p =>
        {
            var k = b.P((int)Math.Round(p.X * st.S, MidpointRounding.AwayFromZero), (int)Math.Round(-10 + p.Y * st.S, MidpointRounding.AwayFromZero), st.Z,
                first ? Plain : 0);                          // $0F: the hull is no part of the periscope's turn
            first = false;
            return k;
        }).ToArray()).ToArray();
        Func<int, int> hull = i => i == section.Length - 1 ? Deck : Hull;   // the face between the deck's two edges is the deck
        b.Cap(rings[0].Reverse().ToArray(), b.P(0, -9, 116), hull);   // the bow
        b.Tube(rings, hull);
        b.Cap(rings[^1], b.P(0, -9, -112), hull);                      // the stern
        // the sail: a tall narrow block on the deck, its front rounded, its back sloped forward
        (int X, int Z)[] plan = [(0, 62), (4, 58), (5, 50), (5, 26), (3, 18), (-3, 18), (-5, 26), (-5, 50), (-4, 58)];
        var low = plan.Select(p => b.P(p.X, 2, p.Z)).ToArray();
        var top = plan.Select(p => b.P(p.X, SailTop, p.Z > 30 ? p.Z : p.Z + 4)).ToArray();
        for (var i = 0; i < plan.Length; i++)
        {
            var j = (i + 1) % plan.Length;
            b.F(Math.Abs(plan[i].X) == 5 && Math.Abs(plan[j].X) == 5 ? Sail : Hull, low[i], low[j], top[j], top[i]);
        }
        b.F(Deck, top);
        // the planes: out each side of the sail and at the stern; a rudder up out of the water and one down
        foreach (var sx in new[] { 1, -1 })
        {
            b.F(Hull, b.P(sx * 5, 20, 52), b.P(sx * 14, 20, 49), b.P(sx * 14, 20, 43), b.P(sx * 5, 20, 40));
            b.F(Hull, b.P(sx * 5, -10, -82), b.P(sx * 18, -10, -92), b.P(sx * 18, -10, -100), b.P(sx * 3, -10, -102));
        }
        b.F(Hull, b.P(0, 1, -80), b.P(0, 16, -94), b.P(0, 16, -104), b.P(0, -4, -106));
        b.F(Hull, b.P(0, -20, -80), b.P(0, -32, -94), b.P(0, -32, -104), b.P(0, -14, -106));
        b.Done(origins, Submarine, 9);
    }
}
