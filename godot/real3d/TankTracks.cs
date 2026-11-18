using System;
using System.Collections.Generic;
using System.Linq;

namespace Hunter;

/// <summary>the card's copy of both tanks ($15 kind $03, $16 kind $04) gets, on each black track panel,
///   ROAD WHEELS - a row of grey discs proud of the panel, each its own spin-x part on channel 0 (a pivot flagged $10 at the
///     disc's centre, the disc both ways round and a black bowtie from the hub that shows the turn), their
///     lowest point 12 over the hull's bottom, so the ground fit (fixed corners within 3 of the lowest) is the original's;
///   LINKS - flat grey strips across the belt, one every Pitch along each run of the belt (top, front, bottom, rear), half a
///     pitch long so strip and gap are equal: on the panel 8 deep from 4 in of its outline, 4 proud of it; and, on the runs
///     where the model has an outer tread surface of its own (TreadRuns: $16's front and rear slope), right across that
///     surface from 4 in of the track's inner edge (TreadX) to 4 in of its outer, lying on it and pulled nearer (face flag
///     $80), so the outline is the model's. A run too short for a strip keeps a short line (8 across) instead. Each run is a
///     BELT part (kind 3, card only): its first corner A is flagged $30 | (L - 1) << 2, the next B = A + D is the run's pitch,
///     and every corner after slides by D * ((L * channel 0) mod 256) / 256 (model.gdshaderinc _walk_step) - a strip's four
///     corners all slide together, the strips repeat every pitch, so the pattern moves without end. L pitches make one turn
///     of the wheels (2 pi R = L x Pitch, near enough: the belt slips under 5%).
///   CLODS:
///     not parts of the model: a moving tank throws small triangles of earth from its tracks' rear, each noted once in the
///     world (GpuWorld ClodThrows, the marks' ring) with where it left the belt (ClodSpots: most from the rear run, where the
///     belt climbs round the last wheel, every fourth from the top rear corner), its colour (brown, or the land's under the
///     tank) and the clock. gputracks.gdshader flies it from that note - a small hop up and back, down to the ground's own
///     height - and leaves it lying there, still, until it goes. A clod in the tank's own frame could only ride with the tank.
/// Channel 0 of these two models is not the animation table's (their set is 0): the card gives it the tank's own rolling,
/// the distance its record has moved along its heading (back counts back), turned into the wheels' 256ths (GpuWorld
/// TrackRoll -> roll_tex, place.gdshaderinc entity_artic). The rules treat kind 3 as a plain corner ($551E) and keep their
/// objfile.</summary>
public static class TankTracks
{
    /// <summary>The face origin GpuWorld.Bake reads as a track face: a plain part of the body, no glass.</summary>
    public const int Origin = -1002;

    public const int Wheel = 8, Band = 0, Link = 8;                  // grey wheels with a black band, grey links (the panels are black 0)
    public const int ClodsATrack = 12;                               // CLODS: thrown a track in a cycle (GpuWorld.ClodThrows)

    /// <summary>CLODS: a tank model's ClodsATrack throw spots in turn (right side, model units; the left mirrors x), y given as
    /// the height above the belt's bottom (the ground): every fourth (0, 4, 8) at one of the Flap spots, moved a few units
    /// apart, the rest at the Rear spots - or null for any other model. The clods are no part of the model: GpuWorld notes
    /// each in the world when it is thrown and gputracks.gdshader flies it from there.</summary>
    public static (int X, int Y, int Z)[]? ClodSpots(int model)
    {
        if (Tanks.FirstOrDefault(t => t.Model == model) is not { } t) return null;
        var ground = t.Belt.Min(p => p.Y);
        var spots = new (int X, int Y, int Z)[ClodsATrack];
        for (int i = 0, r = 0; i < ClodsATrack; i++)
        {
            int x, y, z;
            if (i % 4 == 0)
            {
                (x, y, z) = t.Flap[i % t.Flap.Length];
                x += (i / t.Flap.Length - 1) * 5;
                y += i % 2;
                z += i * 7 % 5 - 2;
            }
            else (x, y, z) = t.Rear[r++];
            spots[i] = (x, y - ground, z);
        }
        return spots;
    }
    const int Inset = 4, LinkLength = 8, Proud = 4;                  // full units

    /// <summary>A tank: its panel's outer face as x of y (right side; the left mirrors it), the belt's outline (z, y) in the order
    /// the belt moves when the tank goes forward (the top run toward the front), the wheels (centre y, radius, sides, x of their
    /// plane, centres in z), the link pitch with its L (pitches a wheel turn), and where the clods rest (right side): Flap, three
    /// spots inside the track's top rear corner under the bodywork, and Rear, one spot each for the ClodsATrack - 3 clods that
    /// rest on the belt's rear run, just inside its surface at different heights.</summary>
    sealed record Tank(int Model, Func<double, double> PanelX, (int Z, int Y)[] Belt, int WheelY, int Radius, int Sides, int WheelX,
        int[] WheelZ, int Pitch, int L, (int X, int Y, int Z)[] Flap, (int X, int Y, int Z)[] Rear);

    static readonly Tank[] Tanks =
    [
        // $15: the lower hull's black side is the panel, sloping out from x 100 (y -64) to 112 (y -14); 50 high, so the wheels
        // are radius 16; 2 pi 16 = 100.5 = 2 x 50
        new(0x15, y => 100 + 12 * (y + 64) / 50.0, [(-184, -12), (208, -16), (160, -64), (-160, -64)],
            -36, 16, 16, 112, [128, 64, 0, -64, -128], 50, 2, [(96, -18, -176), (80, -16, -180), (64, -20, -172)],   // at the belt's top rear (-184, -12)
            // the rear run (-160, -64) -> (-184, -12): each spot about 2 inside it, so the whole triangle is hidden at rest and the
            // clod is behind the run a tenth into its flight at speed 5 (a few units of depth do both)
            [(88, -60, -160), (72, -56, -161), (96, -52, -163), (64, -48, -165), (80, -44, -167), (96, -40, -169),
             (72, -36, -171), (88, -32, -173), (64, -28, -174)]),
        // $16: track blocks x 48..120 with a black outer face at x 120, the top run hidden at y 32 under the hull's side;
        // five wheels, radius 20; 2 pi 20 = 125.7 = 3 x 42. The rear's last step (-272, 0) -> (-280, 32) is shorter than a pitch
        new(0x16, _ => 120, [(-280, 32), (152, 32), (76, -56), (-208, -56), (-272, 0)],
            -24, 20, 16, 124, [52, -8, -68, -124, -184], 42, 3, [(72, 24, -272), (88, 26, -276), (104, 22, -268)],   // at the belt's top rear (-280, 32)
            // the rear run's upper step (-272, 0) -> (-280, 32) and just below it, each spot the one z that is both hidden at rest
            // and out a tenth into the flight at speed 5; lower down, the slope (-208, -56) -> (-272, 0) leans back about as fast
            // as a clod rises, so a clod resting there would stay inside the track for most of its flight
            [(64, -4, -266), (96, -2, -267), (80, 0, -267), (104, 4, -268), (72, 6, -269), (88, 8, -269),
             (60, 10, -270), (100, 14, -271), (76, 16, -271)]),
    ];

    /// <summary>The tanks' models (the objfile numbers): GpuWorld sends their offsets as track_models.</summary>
    public static IEnumerable<int> Models => Tanks.Select(t => t.Model);

    // TRACK MARKS: each tank's track across its side, model x (the two sides alike). $16's track block is x 48..120. $15's
    // belt is taken as x 64..112: its black panel is 100..112, and the clods resting on its rear run sit at x 64..96, inside it.
    static readonly Dictionary<int, (int Inner, int Outer)> TreadX = new() { [0x15] = (64, 112), [0x16] = (48, 120) };

    // LINKS: the belt runs (indices into Belt) that have an outer tread surface of their own in the model, to carry strips across
    // it. $16: the front run (its face across x 48..120) and the rear slope; its bottom faces the ground (strips there would be
    // the model's lowest corners and change its lift), its top is under the hull and its rear step is shorter than a pitch.
    // $15 has none: its belt is the hull's sloping side, the hull's own ends and bottom span its whole width.
    static readonly Dictionary<int, int[]> TreadRuns = new() { [0x16] = [1, 3] };

    /// <summary>TRACK MARKS: a tank model's track on the ground in model units - the middle of a track out from the tank's
    /// middle, its width, and the link pitch (one tread bar and its gap) - or null for any other model.</summary>
    public static (int Centre, int Width, int Pitch)? Tread(int model) =>
        TreadX.TryGetValue(model, out var x) && Tanks.FirstOrDefault(t => t.Model == model) is { } t
            ? ((x.Inner + x.Outer) / 2, x.Outer - x.Inner, t.Pitch) : null;

    /// <summary>A tank model's wheel radius (full units), 0 for any other model: GpuWorld turns a record's rolling into 256ths of it.</summary>
    public static int Radius(int model) => Tanks.FirstOrDefault(t => t.Model == model)?.Radius ?? 0;

    public static void Make(CardModels store, Dictionary<int, int[]>? origins = null)
    {
        foreach (var t in Tanks)
            if (t.Model < store.Models.Length) Build(store.Models[t.Model], t, origins);
        store.Layout();
    }

    static int R(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    static void Build(CardModels.Model md, Tank t, Dictionary<int, int[]>? origins)
    {
        if (md.Corners.Any(c => c.Flag != 0)) throw new InvalidOperationException($"model {t.Model}: the tank already has parts");
        var nf = md.Faces.Length;
        var pile = new Pile(md);
        // the wheels: a part a disc - its pivot the disc's own centre (a spin about x does not care where the pivot is in x), so
        // the bowtie across the hub can meet there
        foreach (var z in t.WheelZ)
            foreach (var side in new[] { 1, -1 })
            {
                var hub = pile.P(side * t.WheelX, t.WheelY, z, 0x10);     // spin about x on channel 0
                var ring = new int[t.Sides];
                var seen = new HashSet<(int, int)>();
                for (var k = 0; k < t.Sides; k++)
                {
                    var a = 2 * Math.PI * k / t.Sides;
                    var (y, zz) = (t.WheelY + R(t.Radius * Math.Cos(a)), z - R(t.Radius * Math.Sin(a)));
                    if (!seen.Add((y, zz))) throw new InvalidOperationException($"model {t.Model}: a wheel of {t.Sides} sides and radius {t.Radius} folds onto whole units");
                    ring[k] = pile.P(side * t.WheelX, y, zz);
                }
                var (h, w) = (t.Sides / 2, Math.Max(1, t.Sides / 8));       // a wedge spans an eighth of the rim
                pile.Face(Wheel, ring);
                pile.Face(Wheel, ring.Reverse().ToArray(), 0x80);
                pile.Face(Band, [hub, .. Enumerable.Range(1, w + 1).Select(k => ring[k])], 0x80);       // the BOWTIE: two opposite black wedges
                pile.Face(Band, [hub, .. Enumerable.Range(h + 1, w + 1).Select(k => ring[k])], 0x80);   // from the hub
            }
        // the links: a belt part a run, a side
        foreach (var side in new[] { 1, -1 })
        {
            int X(double y) => side * R(t.PanelX(y) + Proud);
            for (var r = 0; r < t.Belt.Length; r++)
            {
                var (az, ay) = t.Belt[r];
                var (bz, by) = t.Belt[(r + 1) % t.Belt.Length];
                double len = Math.Sqrt((bz - az) * (bz - az) + (by - ay) * (by - ay));
                if (len < t.Pitch + 3 * Inset) continue;
                double uz = (bz - az) / len, uy = (by - ay) / len;
                double nz = -uy, ny = uz;                                // the left of the run: inward when the belt goes round anticlockwise
                if (Turn(t.Belt) < 0) (nz, ny) = (-nz, -ny);             // clockwise (both tanks' belts): the right is inward
                var (dz, dy) = (R(uz * t.Pitch), R(uy * t.Pitch));
                double step = Math.Sqrt(dz * dz + dy * dy);
                // the run's start: 8 along it and 4 in, clear of both edges at its corner (in from the edge alone, an acute corner's
                // start stood outside the panel or on the hull's bottom)
                var (sz, sy) = (R(az + uz * 2 * Inset + nz * Inset), R(ay + uy * 2 * Inset + ny * Inset));
                var (ez, ey) = (R(nz * LinkLength), R(ny * LinkLength));  // a link, across the belt
                var (wz, wy) = (R(dz / 2.0), R(dy / 2.0));                   // a strip's length along the run: half a pitch, strip and gap equal
                // the last strip's far end slides to 4 short of the run's end, no further; a run too short for one strip keeps a line
                var strips = (int)Math.Floor((len - 3 * Inset - Math.Sqrt(wz * wz + wy * wy)) / step);
                var links = strips >= 1 ? strips : (int)Math.Floor((len - 3 * Inset) / step);
                if (links < 1) continue;
                pile.P(X(sy), sy, sz, 0x30 | (t.L - 1) << 2);                 // A: a belt part on channel 0
                pile.P(X(sy + dy), sy + dy, sz + dz);                         // B = A + D: the pitch
                var segs = new List<(int, int)>();
                for (var k = 0; k < links; k++)
                {
                    var (pz, py) = (sz + k * dz, sy + k * dy);
                    if (strips < 1) { segs.Add((pile.P(X(py), py, pz), pile.P(X(py + ey), py + ey, pz + ez))); continue; }
                    // a flat strip on the panel: the link's 8 across the belt by half a pitch along, its corners all in this belt part
                    pile.Face(Link, [pile.P(X(py), py, pz), pile.P(X(py + ey), py + ey, pz + ez),
                        pile.P(X(py + ey + wy), py + ey + wy, pz + ez + wz), pile.P(X(py + wy), py + wy, pz + wz)]);
                }
                for (var i = 0; i < segs.Count; i += 15) pile.Lines(Link, segs.Skip(i).Take(15));   // a line face holds 15 edges
                // the same strips right across the belt's outer tread surface where the model has one: on the run's own line (the
                // tread face), from 4 in of the track's inner edge to 4 in of its outer, pulled nearer (face flag $80) so it draws
                // over the tread without standing proud of it - the track's outline stays the model's
                if (strips < 1 || !TreadRuns.TryGetValue(t.Model, out var runs) || !runs.Contains(r) || !TreadX.TryGetValue(t.Model, out var tx)) continue;
                var (tz, ty) = (R(az + uz * 2 * Inset), R(ay + uy * 2 * Inset));
                var (xi, xo) = (side * (tx.Inner + Inset), side * (tx.Outer - Inset));
                for (var k = 0; k < strips; k++)
                {
                    var (pz, py) = (tz + k * dz, ty + k * dy);
                    pile.Face(Link, [pile.P(xi, py, pz), pile.P(xo, py, pz), pile.P(xo, py + wy, pz + wz), pile.P(xi, py + wy, pz + wz)], 0x80);
                }
            }
        }
        md.Replace([.. md.Corners, .. pile.Corners], [.. md.Edges, .. pile.Edges], [.. md.Faces, .. pile.Faces]);
        if (origins is not null)
            origins[t.Model] = (origins.TryGetValue(t.Model, out var old) ? old : Enumerable.Range(0, nf).ToArray()).Concat(pile.Faces.Select(_ => Origin)).ToArray();
    }

    // THE BICYCLE'S CHAIN: the same belt part, on channel 2 -
    // the channel the ridden bicycle's wheels spin on (set 11: a steady eighth of a turn a step while it moves, at rest when it
    // stops, $56D0). A dashed black line on the right side (x 3) round the rear sprocket (hub z -45, radius 4) and the chainring
    // (the bottom bracket z 17, radius 8): the top run moves forward to the chainring, the bottom run back, a dash 3 long every 6.
    // The models' shift is 0, so whole units. $28 the ridden bicycle; $27 the parked one (the same frame 15 higher, no parts:
    // its chain stands still). (model, y of the rear hub, y of the bottom bracket.)
    static readonly (int Model, int HubY, int BracketY)[] Bicycles = [(0x28, -21, -32), (0x27, -6, -17)];
    const int ChainX = 3, ChainPitch = 6, ChainDash = 3, ChainColour = 0, SprocketR = 4, ChainringR = 8, HubZ = -45, BracketZ = 17;
    const int ChainFlag = 0x30 | 2;                                   // a belt part, L 1, on channel 2

    /// <summary>The chains into the bicycles (after RoundWheels: it rebuilds the bicycle's rims); the rules keep their objfile.</summary>
    public static void Chains(CardModels store, Dictionary<int, int[]>? origins = null)
    {
        foreach (var (model, hubY, bracketY) in Bicycles)
        {
            if (model >= store.Models.Length) continue;
            var md = store.Models[model];
            var nf = md.Faces.Length;
            var pile = new Pile(md);
            // the two runs in the order the chain moves riding forward
            foreach (var ((az, ay), (bz, by)) in new[] {
                ((HubZ, hubY + SprocketR), (BracketZ, bracketY + ChainringR)),       // top: back to front
                ((BracketZ, bracketY - ChainringR), (HubZ, hubY - SprocketR)) })    // bottom: front to back
            {
                double len = Math.Sqrt((bz - az) * (bz - az) + (by - ay) * (by - ay)), uz = (bz - az) / len, uy = (by - ay) / len;
                var (dz, dy) = ((int)Math.Round(uz * ChainPitch), (int)Math.Round(uy * ChainPitch));
                var (ez, ey) = ((int)Math.Round(uz * ChainDash), (int)Math.Round(uy * ChainDash));
                var links = (int)Math.Floor((len - ChainDash) / ChainPitch);
                pile.P(ChainX, ay, az, ChainFlag);                             // A
                pile.P(ChainX, ay + dy, az + dz);                              // B = A + D
                var segs = new List<(int, int)>();
                for (var k = 0; k < links; k++)
                {
                    var (pz, py) = (az + k * dz, ay + k * dy);
                    segs.Add((pile.P(ChainX, py, pz), pile.P(ChainX, py + ey, pz + ez)));
                }
                for (var i = 0; i < segs.Count; i += 15) pile.Lines(ChainColour, segs.Skip(i).Take(15));
            }
            md.Replace([.. md.Corners, .. pile.Corners], [.. md.Edges, .. pile.Edges], [.. md.Faces, .. pile.Faces]);
            if (origins is not null)
                origins[model] = (origins.TryGetValue(model, out var old) ? old : Enumerable.Range(0, nf).ToArray()).Concat(pile.Faces.Select(_ => Origin)).ToArray();
        }
        store.Layout();
    }

    /// <summary>The belt outline's turn in (z, y): 1 when it goes round anticlockwise (z right, y up), -1 clockwise.</summary>
    static int Turn((int Z, int Y)[] belt)
    {
        long a = 0;
        for (var i = 0; i < belt.Length; i++) { var (p, q) = (belt[i], belt[(i + 1) % belt.Length]); a += (long)p.Z * q.Y - (long)q.Z * p.Y; }
        return a > 0 ? 1 : -1;
    }

    /// <summary>The corners, edges and faces to add to a model, numbered after the model's own.</summary>
    sealed class Pile(CardModels.Model md)
    {
        public readonly List<CardModels.Corner> Corners = [];
        public readonly List<CardModels.Edge> Edges = [];
        public readonly List<CardModels.Face> Faces = [];
        readonly Dictionary<(int, int), int> edgeId = [];

        /// <summary>A corner at these full units (the card's store: no scale steps - CardModels.Model.FullCorner).</summary>
        public int P(int x, int y, int z, int flag = 0)
        {
            Corners.Add(md.FullCorner(flag, x, y, z));
            return md.Corners.Length + Corners.Count - 1;
        }

        int Edge(int a, int b)
        {
            var key = (Math.Min(a, b), Math.Max(a, b));
            if (!edgeId.TryGetValue(key, out var id)) { edgeId[key] = id = md.Edges.Length + Edges.Count; Edges.Add(new(a, b)); }
            return id;
        }

        public void Face(int colour, int[] ring, int flags = 0)
        {
            var es = new int[ring.Length];
            for (var i = 0; i < ring.Length; i++) es[i] = Edge(ring[i], ring[(i + 1) % ring.Length]);
            Faces.Add(new(colour, flags, es));
        }

        /// <summary>A line face (kind $20: drawn as its separate edges) of these segments.</summary>
        public void Lines(int colour, IEnumerable<(int A, int B)> segs) => Faces.Add(new(colour, 0x20, segs.Select(s => Edge(s.A, s.B)).ToArray()));
    }
}
