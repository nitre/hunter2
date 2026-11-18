using System;
using System.Collections.Generic;
using System.Linq;

namespace Hunter;

/// <summary>the card's copy of both tanks ($15 kind $03, $16 kind $04): their sides stay closed and plain, as the original's,
///   RIBS - bars across the tread where the model has one in view ($16's front run and rear slope), moving along it as the
///     tank rolls: each run a BELT part (kind 3, card only; Build). No wheels and no marks on the side panels: small
///     things there came out unalike (a turned model's corners land on the nearest half unit). Pitch is the spacing of
///     the marks a tank leaves on the ground;
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

    public const int Rib = 0;                                        // the ribs: black on the tread's brown (the model's colour 10)
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

    /// <summary>A tank: the belt's outline (z, y) in the order the belt moves when the tank goes forward (the top run toward the
    /// front); Radius, the circle its rolling is counted in (one turn of channel 0 is 2 pi Radius along the ground); the pitch of
    /// the marks it leaves on the ground; the ribs' own pitch along the tread with their L (rib pitches a turn of channel 0, so
    /// L x RibPitch is 2 pi Radius, near enough: the ribs go by at the ground's speed); and where the clods rest (right side):
    /// Flap, three spots inside the track's top rear corner under the bodywork, and Rear, one spot each for the ClodsATrack - 3
    /// clods that rest on the belt's rear run, just inside its surface at different heights.</summary>
    sealed record Tank(int Model, (int Z, int Y)[] Belt, int Radius, int Pitch, int RibPitch, int L,
        (int X, int Y, int Z)[] Flap, (int X, int Y, int Z)[] Rear);

    static readonly Tank[] Tanks =
    [
        // $15: the lower hull's black side slopes out from x 100 (y -64) to 112 (y -14). It has no tread surface of its own
        // (TreadRuns): the hull's ends and bottom span its whole width
        new(0x15, [(-184, -12), (208, -16), (160, -64), (-160, -64)],
            16, 50, 25, 4, [(96, -18, -176), (80, -16, -180), (64, -20, -172)],   // at the belt's top rear (-184, -12); 2 pi 16 = 100.5 = 4 x 25
            // the rear run (-160, -64) -> (-184, -12): each spot about 2 inside it, so the whole triangle is hidden at rest and the
            // clod is behind the run a tenth into its flight at speed 5 (a few units of depth do both)
            [(88, -60, -160), (72, -56, -161), (96, -52, -163), (64, -48, -165), (80, -44, -167), (96, -40, -169),
             (72, -36, -171), (88, -32, -173), (64, -28, -174)]),
        // $16: track blocks x 48..120 with a black outer face at x 120, the top run hidden at y 32 under the hull's side.
        // 2 pi 13 = 81.7 = 4 x 21, near enough. The rear's last step (-272, 0) -> (-280, 32) is too short for a rib
        new(0x16, [(-280, 32), (152, 32), (76, -56), (-208, -56), (-272, 0)],
            13, 42, 21, 4, [(72, 24, -272), (88, 26, -276), (104, 22, -268)],   // at the belt's top rear (-280, 32)
            // the rear run's upper step (-272, 0) -> (-280, 32) and just below it, each spot the one z that is both hidden at rest
            // and out a tenth into the flight at speed 5; lower down, the slope (-208, -56) -> (-272, 0) leans back about as fast
            // as a clod rises, so a clod resting there would stay inside the track for most of its flight
            [(64, -4, -266), (96, -2, -267), (80, 0, -267), (104, 4, -268), (72, 6, -269), (88, 8, -269),
             (60, 10, -270), (100, 14, -271), (76, 16, -271)]),
    ];

    // RIBS: the belt runs (indices into Belt) whose tread the model has as a surface of its own and that can be seen.
    // $16: the front run and the rear slope, across x 48..120. Its bottom faces the ground (ribs there would be the model's
    // lowest corners and change its lift), its top is under the hull and its rear step is shorter than a rib and its gap.
    // $15 has none.
    static readonly Dictionary<int, int[]> TreadRuns = new() { [0x16] = [1, 3] };
    // full units: in from the tread's side edges; out from its surface - more than a model unit (4 here), so the bake takes a
    // rib for a free sheet and gives every one the same thickness (within a unit it counts as a mark lying on the tread and
    // stays flat: half a world unit off, where a turned model's corners landing on half units sank part of a rib into the
    // tread); and in from each end of a run, so no rib corner comes lower than the model's own lowest (that sets its lift)
    const int Inset = 4, RibProud = 6, RunInset = 14;

    /// <summary>The tanks' models (the objfile numbers): GpuWorld sends their offsets as track_models.</summary>
    public static IEnumerable<int> Models => Tanks.Select(t => t.Model);

    // TRACK MARKS: each tank's track across its side, model x (the two sides alike). $16's track block is x 48..120. $15's
    // belt is taken as x 64..112: its black panel is 100..112, and the clods resting on its rear run sit at x 64..96, inside it.
    static readonly Dictionary<int, (int Inner, int Outer)> TreadX = new() { [0x15] = (64, 112), [0x16] = (48, 120) };

    /// <summary>TRACK MARKS: a tank model's track on the ground in model units - the middle of a track out from the tank's
    /// middle, its width, and the link pitch (one tread bar and its gap) - or null for any other model.</summary>
    public static (int Centre, int Width, int Pitch)? Tread(int model) =>
        TreadX.TryGetValue(model, out var x) && Tanks.FirstOrDefault(t => t.Model == model) is { } t
            ? ((x.Inner + x.Outer) / 2, x.Outer - x.Inner, t.Pitch) : null;

    /// <summary>A tank model's rolling circle's radius (full units), 0 for any other model: GpuWorld turns a record's rolling into 256ths of it.</summary>
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
        // RIBS: bars across the tread, one every RibPitch along a run, half a pitch long (bar and gap equal), from 4 in of the
        // track's inner edge to 4 in of its outer, standing 2 out from the tread's surface - clear of it, so the two never
        // share a plane. Each run a side is a BELT part WITH ENDS (kind 4, card only): its first corner A is flagged
        // $40 | (L - 1) << 2, the next B = A + D is the pitch, the third E the run's end, and every corner after slides by
        // D * ((L * channel 0) mod 256) / 256 and is then held between A and E along the run (model.gdshaderinc _walk_step).
        if (TreadRuns.TryGetValue(t.Model, out var runs) && TreadX.TryGetValue(t.Model, out var tx))
        {
            long turn = 0;                                               // the outline's turn in (z, y): > 0 anticlockwise (z right, y up)
            for (var i = 0; i < t.Belt.Length; i++) { var (p, q) = (t.Belt[i], t.Belt[(i + 1) % t.Belt.Length]); turn += (long)p.Z * q.Y - (long)q.Z * p.Y; }
            foreach (var side in new[] { 1, -1 })
                foreach (var r in runs)
                {
                    var (az, ay) = t.Belt[r];
                    var (bz, by) = t.Belt[(r + 1) % t.Belt.Length];
                    double len = Math.Sqrt((bz - az) * (bz - az) + (by - ay) * (by - ay));
                    double uz = (bz - az) / len, uy = (by - ay) / len;
                    double oz = uy, oy = -uz;                            // the right of the run: outward when the belt goes round anticlockwise
                    if (turn < 0) (oz, oy) = (-oz, -oy);                 // clockwise (both tanks' belts): the left is outward
                    var (dz, dy) = (R(uz * t.RibPitch), R(uy * t.RibPitch));
                    double step = Math.Sqrt(dz * dz + dy * dy);
                    var (wz, wy) = (R(dz / 2.0), R(dy / 2.0));           // a rib's length along the run
                    // the run the ribs keep to: RunInset in from each of its corners. A rib a pitch from one before the start to the
                    // end: the card slides them all together and holds each corner on the run, so the first grows out of the
                    // start as the last thins away into the end - the ribs go round the corners, none pops in or out
                    var (tz, ty) = (R(az + uz * RunInset + oz * RibProud), R(ay + uy * RunInset + oy * RibProud));
                    var (ez, ey) = (R(bz - uz * RunInset + oz * RibProud), R(by - uy * RunInset + oy * RibProud));
                    var (xi, xo) = (side * (tx.Inner + Inset), side * (tx.Outer - Inset));
                    pile.P(xi, ty, tz, 0x40 | (t.L - 1) << 2);           // A: a belt part with ends, on channel 0
                    pile.P(xi, ty + dy, tz + dz);                        // B = A + D: the pitch
                    pile.P(xi, ey, ez);                                  // E: the run's end
                    var outFirst = ((double)(xo - xi) * (wy * oz - wz * oy)) < 0;   // wound so the rib's own direction is IN toward the tread: the bake's moved copy is the corner less that direction, so its thickness goes outward
                    for (var k = -1; k * step < len - 2 * RunInset; k++)
                    {
                        var (pz, py) = (tz + k * dz, ty + k * dy);
                        int[] quad = [pile.P(xi, py, pz), pile.P(xo, py, pz), pile.P(xo, py + wy, pz + wz), pile.P(xi, py + wy, pz + wz)];
                        pile.Face(Rib, outFirst ? quad : [.. quad.Reverse()]);
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
