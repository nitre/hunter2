using System;
using System.Collections.Generic;
using System.Linq;

namespace Hunter;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (the Dune II models "need integrating"): Dune II's vehicles, built by
/// hand from the Amiga game's sprites and mentat paintings (research/dune2/sprites.md; the shapes are
/// research/dune2/tools/hand_models.py, ported here box for box): the harvester, the tank, the quad and the trike. They are
/// MODELS OF THEIR OWN, appended after the objfile's (CardModels.AppendCopy, each from the Hunter model nearest it - the truck
/// $12, the tank $15, the jeep $22, the armoured car $24 - for its header and animation set): (from 11-11 the row VEHICLES: DUNE II put them on the card in place of those four; the
/// row is gone). Nothing draws them until the rules have kinds of their own for them (the DUNE II EXTRAS work); GpuWorld.CardShapes
/// builds them when asked (dune). Shai-Hulud is built here too (PutWorm, PLANET's sand zone).
/// The rules keep their own objfile (boxes, collisions, the special corners): the picture only, like RoundWheels and SeaBodies.
///
/// THE PARTS, as Hunter's own models carry them (a flagged pivot corner followed by the part's corners): the quad's and trike's
/// axles are spin parts about x on channel 0 like the jeep's and the armoured car's (flag $10, both their wheel groups are on
/// channel 0), so the wheels turn with the vehicle; RoundWheels then rounds the eight-cornered discs to sixteen as it does
/// the jeep's. The tank's turret and barrel and the trike's and quad's guns are turn-about-y parts on channel 1 (flag $15,
/// the submarine's channel) - nothing drives it on these kinds yet (the tank has no animation set), so they point ahead; a
/// steerable turret is rules work for when these become kinds of their own.
///
/// IN THE SAND: the worm is built with the sand at model y 0 and most of it below; GameView.WithWorm places it with no lift
/// (its record's ground word far below), so it stands where it is put and the ground hides what is under.
///
/// Every box is closed (a bottom on each, where the Python left the hidden ones off), so the models are their own shells:
/// no automatic back walls (ModelCaps), and EXPLOSIONS: PIECES throws whole panels. Colours are the play palette's (bank 2):
/// Green uses the greens Hunter's vehicles use; Desert bank 2's browns and greys (on a desert island they need
/// not be green); the Arrakis sand bank the Python proposes is a palette change, not made here. The worm's hide is grey (8).</summary>
public static class DuneModels
{
    public const int Harvester = 0x12, Tank = 0x15, Quad = 0x22, Trike = 0x24;

    /// <summary>Each Dune vehicle and the Hunter model it is copied from (its header and animation set: the axles turn on the jeep's
    /// channel 0 as the jeep's do).</summary>
    public static readonly (int From, string Name)[] Models = [(Harvester, "harvester"), (Tank, "tank"), (Quad, "quad"), (Trike, "trike")];

    public enum Scheme { Green, Desert }

    /// <summary>A part flag: kind 1 (turn), axis 1 (about y), channel 1 - Hunter's own $15 (the submarine's periscope).</summary>
    const int Turret = 0x10 | 1 << 2 | 1;
    /// <summary>The axles: kind 1, axis 0 (about x), channel 0 - the jeep's $10.</summary>
    const int AxleFlag = 0x10;


    /// <summary>Palette indexes a role: top, side, deep (shadowed faces), dark, light (highlight), track, track dark, under, metal.</summary>
    sealed record Colours(int Top, int Side, int Deep, int Dark, int Light, int Track, int TrackDark, int Under, int Metal);
    static readonly Dictionary<Scheme, Colours> Schemes = new()
    {
        [Scheme.Green] = new(3, 2, 14, 13, 4, 1, 0, 9, 12),
        [Scheme.Desert] = new(8, 11, 10, 9, 7, 9, 0, 9, 10),
    };
    const int WormHide = 8, WormThroat = 9, WormGullet = 0, WormTooth = 8;   // no green on the worm: grey hide, dark brown throat, the crystal teeth the palette's palest that is not green (a white waits for the sand zone's bank)

    /// <summary>The five models into the card's store, then the quad's and trike's wheels rounded (RoundWheels), then laid out.
    /// origins: every face of a replaced model counts as the file's face 0 - plain paint (no glass, no interior), as SeaBodies'
    /// added faces do. notes: what was built, and a wheel RoundWheels could not round.</summary>
    /// <summary>The four vehicles appended to the store as models of their own; returns each one's model number by name. Hunter's
    /// models are not touched. The appended models are closed shells and past the bare file's models, so they get no back walls.</summary>
    public static Dictionary<string, int> Make(CardModels store, List<string>? notes = null, Dictionary<int, int[]>? origins = null, Scheme scheme = Scheme.Green)
    {
        var c = Schemes[scheme];
        var built = new Dictionary<string, int>();
        foreach (var (from, name) in Models)
        {
            if (from >= store.Models.Length) continue;
            var s = name switch { "harvester" => HarvesterShape(c), "tank" => TankShape(c), "quad" => QuadShape(c), _ => TrikeShape(c) };
            var model = store.AppendCopy(from);
            s.Into(store.Models[model], true);
            built[name] = model;
            notes?.Add($"model {model} ({name}, from ${from:X2}): {s.Corners.Count} corners, {s.Edges.Count} edges, {s.Faces.Count} faces, shift {store.Models[model].Shift}");
        }
        RoundWheels.Make(store, built.Where(b => b.Key is "quad" or "trike").Select(b => b.Value), notes, null);
        if (origins is not null) foreach (var m in built.Values) origins[m] = new int[store.Models[m].Faces.Length];
        store.Layout();
        return built;
    }

    /// <summary>THE HARVESTER as the game's own vehicle (rules ExtraKinds.Harvester): the
    /// Dune II harvester appended alone (from the truck, $12 - its header and animation set), plain paint. Returns its number.</summary>
    public static int PutHarvester(CardModels store, Dictionary<int, int[]>? origins = null)
    {
        var model = store.AppendCopy(Harvester);
        HarvesterShape(Schemes[Scheme.Green]).Into(store.Models[model], true);
        if (origins is not null) origins[model] = new int[store.Models[model].Faces.Length];
        return model;
    }

    /// <summary>THE SPICE FLOWER (rules ExtraKinds.SpiceFlower): the
    /// red flower ($7A) copied as it is; the card paints its petals blue-black (own_colours.gdshaderinc spice_model), its stem
    /// keeps its green. Returns its number.</summary>
    public static int PutSpiceFlower(CardModels store, Dictionary<int, int[]>? origins = null)
    {
        var model = store.AppendCopy(SpiceFrom);
        if (origins is not null) origins[model] = new int[store.Models[model].Faces.Length];
        return model;
    }

    /// <summary>The model the spice flower is copied from: the red flower's (type $77's +$25).</summary>
    public const int SpiceFrom = 0x7A;

    /// <summary>THE WORM'S SIGN (/// "only a ripple and dust, not a worm shaped dark outline"): a broad, gentle swell of sand - 400 long, 240 wide, 20 high,
    /// rising and falling smoothly (a cosine both ways) with its edge 6 under the sand, so it has no outline - and dust thrown
    /// up off its front: eight small grains, each a FLING part (walk kind 5) on its own share of the throw,
    /// flying only while the record's speed (+$3E) is not 0. Its faces are grey (8) and the card draws them in the sand's own
    /// colour (own_colours.gdshaderinc sand_model). GameView.WithWorm puts one where the worm is, near the surface.</summary>
    public static void PutWormSign(CardModels store, int model) => WormSignShape().Into(store.Models[model], false);

    const int SignGrey = 8;

    static Shape WormSignShape()
    {
        var s = new Shape(SignGrey);
        const int nu = 12, nw = 8;
        const double L = 200, W = 120, H = 26, Under = 6;
        double Bell(double t) => (1 + Math.Cos(Math.PI * t)) / 2;       // 1 in the middle, 0 at the ends, flat at both
        var grid = new int[nu + 1][];
        for (var i = 0; i <= nu; i++)
        {
            var u = -1 + 2.0 * i / nu;
            grid[i] = Enumerable.Range(0, nw + 1).Select(j =>
            {
                var w = -1 + 2.0 * j / nw;
                return s.P(W * w, H * Bell(u) * Bell(w) - Under, L * u);
            }).ToArray();
        }
        for (var i = 0; i < nu; i++)
            for (var j = 0; j < nw; j++)
                s.Face(SignGrey, [grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]]);
        for (var k = 0; k < 8; k++)                                     // the dust off its front
        {
            var side = k % 2 == 0 ? 1 : -1;
            var (x, y, z) = (side * (10 + 12 * (k / 2)), 12, 150 - 14 * (k / 2));
            var a = s.P(x, y, z, 0x50 | (k * 3) & 15);
            s.Face(SignGrey, [a, s.P(x + side * 6, y + 8, z - 5), s.P(x - side * 4, y + 4, z + 8)]);
        }
        return s;
    }

    /// <summary>Shai-Hulud into this model of the store - for the worm's own kind, when the rules have one: a thing of the sand,
    /// never of the sea. Nothing calls it yet. The caller lays the store out.</summary>
    /// <summary>One body segment of the worm's chain: a short ringed tube along +y from model y 0 to WormSegmentLength (a little
    /// longer than the spacing, so neighbours overlap at the joints), the plate ring wider in the middle, a cap at its rear.</summary>
    public static void PutWormSegment(CardModels store, int model) => WormSegmentShape().Into(store.Models[model], false);

    static Shape WormSegmentShape()
    {
        var s = new Shape(WormHide);
        const int sides = 16;
        int[] Ring(double r, double y) => Enumerable.Range(0, sides).Select(k => s.P(r * Math.Sin(2 * Math.PI * k / sides), y, r * Math.Cos(2 * Math.PI * k / sides))).ToArray();
        int[][] rings = [Ring(WormRadius, 0), Ring(WormRadius + 16, WormSegmentLength / 2), Ring(WormRadius, WormSegmentLength)];
        s.Tube(rings, WormHide);
        s.Face(WormHide, rings[0].Reverse().ToArray());
        return s;
    }

    /// <summary>THE SAND GIVING WAY where the worm strikes (the novel's whirlpool and wide hole): a shallow funnel in the sand - a
    /// low rim 420 out (its apron running down under the sand, so no edge shows), the wall sloping in, each ring of it turned a
    /// little further round so the facets run in a spiral, darker toward the middle, and a black hole there. Its faces drawn in
    /// the sand's own colour at their own brightness (own_colours.gdshaderinc sand_model2); GameView.WithWorm turns it, so the spiral swirls. Dust grains round the rim on
    /// the clods' fling part, flying while its record's speed (+$3E) is not 0.</summary>
    public static void PutWormFunnel(CardModels store, int model) => WormFunnelShape().Into(store.Models[model], false);

    static Shape WormFunnelShape()
    {
        var s = new Shape(SignGrey);
        const int n = 24;
        (double R, double Y)[] steps = [(520, -6), (420, 30), (340, 18), (260, 10), (180, 5), (110, 2)];
        var rings = steps.Select((st, i) => Enumerable.Range(0, n).Select(k =>
        {
            var a = 2 * Math.PI * k / n + i * 0.26;                       // each ring further round: the spiral
            return s.P(st.R * Math.Sin(a), st.Y, st.R * Math.Cos(a));
        }).ToArray()).ToArray();
        // shaded darker toward the middle so it reads as a pit (the card cannot cut into the ground): grey, then darker paints -
        // the sand tint keeps each one's brightness (palette 8, 2, 1: light, middling, dark), and the hole black
        int[] shade = [SignGrey, SignGrey, 2, 1, 1];
        for (var b = 0; b + 1 < rings.Length; b++)
            for (var k = 0; k < n; k++)
                s.Face(shade[b], [rings[b][k], rings[b][(k + 1) % n], rings[b + 1][(k + 1) % n], rings[b + 1][k]]);
        s.Face(0, rings[^1]);                                           // the hole
        for (var k = 0; k < 8; k++)                                     // dust off the rim
        {
            var a = 2 * Math.PI * k / 8;
            int x = (int)(400 * Math.Sin(a)), z = (int)(400 * Math.Cos(a));
            var g = s.P(x, 32, z, 0x50 | (k * 2) & 15);
            s.Face(SignGrey, [g, s.P(x + 8, 42, z - 6), s.P(x - 6, 38, z + 9)]);
        }
        return s;
    }

    public static void PutWorm(CardModels store, int model) => WormShape().Into(store.Models[model], false);

    // ------------------------------------------------------------------ the shapes (full model units: 16 a sprite pixel)

    /// <summary>The harvester (plan 15 x 21 px: the body 14 wide over 13 rows, a narrower rounded nose of 9 across the front
    /// rows with the intake's mouth in it, the rear third of the roof a step; the painting HARVEST.WSA: a slab body 0.45 of
    /// its length high, sides to 0.8 of the height then a chamfer to the roof, the front leaning forward over a low intake,
    /// a raised cab at the rear, the tracks a tenth of the length high with sloped ends).</summary>
    static Shape HarvesterShape(Colours c)
    {
        var s = new Shape(c.Under);
        foreach (var sx in new[] { -1, 1 })                                                                                    // tracks, their ends sloped
            s.Box(sx * 88, sx * 120, 0, 56, -176, 120, c.Track, c.TrackDark, bottom: c.TrackDark, zt0: -160, zt1: 104);
        s.Box(-96, 96, 40, 136, -168, 100, c.Side, c.Side, bottom: c.Under, zt1: 116, front: c.Deep, back: c.Dark);        // the body, front leaning
        s.Box(-72, 72, 136, 160, -168, 116, c.Top, c.Deep, zt1: 128, front: c.Side, back: c.Dark);                         // the chamfered roof
        s.Box(-56, 56, 160, 180, -168, -80, c.Light, c.Side, back: c.Dark);                                                // the raised cab at the rear
        s.Box(-72, 72, 48, 124, 100, 164, c.Top, c.Side, bottom: c.Under, zt1: 180, front: c.Deep);                        // the nose, narrower, leaning
        s.Box(-64, 64, 16, 64, 120, 204, c.Metal, c.Dark, bottom: c.Metal, front: 0);                                      // the intake under it, its mouth dark
        s.Box(-40, 40, 64, 96, 164, 196, c.Metal, c.Dark, front: c.Dark);                                                  // the intake's hood
        return s;
    }

    /// <summary>The tank (plan 12 x 13 px; the painting HTANK.WSA: a low hull a quarter of its length high with the glacis sloped,
    /// a boxy turret set rear of centre, a long barrel).</summary>
    static Shape TankShape(Colours c)
    {
        var s = new Shape(c.Under);
        foreach (var sx in new[] { -1, 1 })                                                                                    // tracks: the 2 px outer columns
            s.Box(sx * 64, sx * 96, 0, 40, -104, 104, c.Track, c.TrackDark, bottom: c.TrackDark);
        s.Box(-72, 72, 24, 72, -96, 96, c.Top, c.Side, bottom: c.Under, zt1: 72, front: c.Deep, back: c.Dark);              // hull, glacis sloped back
        s.BeginPart(Turret, 0, 72, -20);                                                                                   // the turret turns about y on channel 1: it and the barrel
        s.Box(-40, 40, 72, 112, -64, 24, c.Light, c.Deep, back: c.Dark);                                                   // turret, rear-centred as in the painting
        s.Box(-8, 8, 88, 100, 24, 128, c.Dark, c.Metal);                                                                   // barrel
        return s;
    }

    /// <summary>The quad (plan 14 x 14 px: wheels 4 wide and 5 long at the corners, the body 6 wide between them, the gun's
    /// marks at the front; the painting QUAD.WSA: big wheels 0.35 of the length across, a wedge body with a dark canopy,
    /// the gun forward). Two axles as spin parts, the gun a turn-about-y part.</summary>
    static Shape QuadShape(Colours c)
    {
        var s = new Shape(c.Under);
        s.Box(-48, 48, 32, 88, -88, 88, c.Side, c.Side, bottom: c.Under, zt1: 72, front: c.Deep, back: c.Dark);              // the body, nose sloped
        s.Box(-40, 40, 88, 112, -56, 40, c.Dark, c.Deep, zt1: 24, front: c.Dark);                                          // the canopy, dark as painted
        s.BeginPart(Turret, 0, 104, 0);                                                                                    // the gun turns about y on channel 1
        s.Box(-10, 10, 104, 118, -24, 56, c.Light, c.Side, front: c.Deep);
        s.Box(-4, 4, 108, 114, 56, 120, c.Dark, c.Metal);
        s.Axle(40, 56, [(-116, -52, 40), (52, 116, 40)], c.Dark, c.Metal);                                                 // front axle: plan cols 1-4 and 11-14, rows 2-6
        s.Axle(40, -56, [(-116, -52, 40), (52, 116, 40)], c.Dark, c.Metal);                                                // rear axle: rows 9-13
        return s;
    }

    /// <summary>The trike (sprite 12 x 14 px): one wheel in front, two big ones behind, the cab over the rear axle, the gun on
    /// top of the cab (the painting TRIKE.WSA: rear wheels 0.4 of the length across, the front one smaller).</summary>
    static Shape TrikeShape(Colours c)
    {
        var s = new Shape(c.Under);
        s.Box(-56, 56, 40, 104, -80, 16, c.Top, c.Side, bottom: c.Under, zt1: 8, front: c.Deep, back: c.Dark);               // cab over the rear axle
        s.Box(-36, 36, 40, 72, 16, 64, c.Top, c.Side, bottom: c.Under, zt1: 56, front: c.Deep);                             // bonnet to the fork
        s.Box(-8, 8, 24, 48, 64, 96, c.Metal, c.Dark);                                                                     // the fork
        s.BeginPart(Turret, 0, 104, 8);                                                                                    // the gun turns about y on channel 1
        s.Box(-12, 12, 104, 120, -40, 56, c.Light, c.Side, front: c.Deep);                                                 // the gun mount
        s.Box(-4, 4, 108, 116, 56, 104, c.Dark, c.Metal);                                                                  // the barrel
        s.Axle(48, -56, [(-96, -52, 48), (52, 96, 48)], c.Dark, c.Metal);                                                  // rear axle: a fat wheel each side
        s.Axle(36, 88, [(-18, 18, 36)], c.Dark, c.Metal);                                                                  // front wheel: one drum 36 wide
        return s;
    }

    /// <summary>Shai-Hulud, our own, as Dune II shows its worm in play: never a rearing tube, a MAW coming up out of the ground
    /// with the back breaking the sand behind it (head and tail details; not a Worms-game worm; a new shape, not the old
    /// arch thicker; "No water. It's a sand worm"). The sand is model y 0. THE BACK: a long plated body lying along the surface, its centre under the sand so
    /// only the top 60-70 of each plate shows - every other ring wider, the plates' ridges. NO TAIL, ever: behind, the
    /// body dives under and is cut off deep, as much worm again left to the mind. THE HEAD: the front of the body turns up out of the sand and opens into a mouth 380 across, gaping up and
    /// forward: a crumpled rim, thin crystal teeth raked BACKWARD down the dark throat (sixteen inside the rim, eight deeper), the
    /// gullet black. Grey hide, no green. The whole worm is one part (Rear). 8-cornered rings; about 700 long, the mouth's rim 250 above the sand. Inside the file format's 255 edges.</summary>
    // THE WORM (then "sinking victim so worm body is hardly ever shown", and the
    // novel's mouth - research/dune2/novel-reference.md: round, as wide as the worm, ringed with curved crystal teeth that spread
    // like a flower): a CHAIN of this head and WormSegments body segments (PutWormSegment), each its own record, which
    // GameView.WithWorm places one below the other under the spot. The head is a short neck (WormNeck long, its rear at model
    // y 0) ending in the MOUTH: a lip as wide as the body, the dark throat going down inside it to a black gullet, and a ring of
    // WormTeeth curved crystal teeth standing on the lip and curving in toward the throat. Nothing on it moves: the
    // sand closing over it is the mouth shutting. Grey, sixteen-sided (the teeth twenty-four round).
    public const int WormNeck = 80, WormRadius = 340, WormSegmentLength = 128, WormSpacing = 120, WormSegments = 10, WormTeeth = 24;

    static Shape WormShape()
    {
        var s = new Shape(WormHide);
        const int sides = 16;
        int[] Ring(double r, double y) => Enumerable.Range(0, sides).Select(k => s.P(r * Math.Sin(2 * Math.PI * k / sides), y, r * Math.Cos(2 * Math.PI * k / sides))).ToArray();
        int[][] neck = [Ring(WormRadius, 0), Ring(WormRadius + 16, WormNeck / 2), Ring(WormRadius + 20, WormNeck)];
        s.Tube(neck, WormHide);
        int[][] throat = [Ring(WormRadius - 10, WormNeck), Ring(WormRadius - 70, WormNeck - 50)];
        s.Tube(throat, WormThroat);                                     // the inside, down to the gullet
        s.Tube([neck[2], throat[0]], WormHide);                         // the lip's top
        s.Face(WormGullet, throat[1].Reverse().ToArray());              // the black down the throat
        // the teeth: each a curved blade from the lip up and IN, toward the throat (the notes' "tooth spokes running round the inside of the rim"; they had splayed outward);
        // the tips stop well short of the middle, so the throat stays open for what it takes
        for (var k = 0; k < WormTeeth; k++)
        {
            var a = 2 * Math.PI * (k + 0.5) / WormTeeth;
            double sx = Math.Sin(a), sz = Math.Cos(a), tx = Math.Cos(a), tz = -Math.Sin(a);   // out, and along the lip
            int At(double r, double y, double side) => s.P(r * sx + side * tx, y, r * sz + side * tz);
            var (b0, b1) = (At(WormRadius, WormNeck, -14), At(WormRadius, WormNeck, 14));
            var mid = At(WormRadius - 40, WormNeck + 50, 0);
            var tip = At(WormRadius - 110, WormNeck + 70, 0);
            s.Face(WormTooth, [b0, b1, mid]);
            s.Face(WormTooth, [mid, b1, tip]);
            s.Face(WormTooth, [b0, mid, tip]);
        }
        return s;
    }

    // ------------------------------------------------------------------ the builder

    /// <summary>A model in full units, boxes and rings; Into puts it in a card model at the smallest scale shift its corners fit.</summary>
    sealed class Shape(int underside)
    {
        public readonly List<(int Flag, double X, double Y, double Z)> Corners = [];
        public readonly List<(int A, int B)> Edges = [];
        public readonly List<(int Colour, int Flags, int[] Edges)> Faces = [];
        readonly Dictionary<(int, int), int> edgeId = [];

        public int P(double x, double y, double z, int flag = 0) { Corners.Add((flag, x, y, z)); return Corners.Count - 1; }

        int Edge(int a, int b)
        {
            var key = (Math.Min(a, b), Math.Max(a, b));
            if (!edgeId.TryGetValue(key, out var id)) { edgeId[key] = id = Edges.Count; Edges.Add((a, b)); }
            return id;
        }

        public void Face(int colour, int[] ring, int flags = 0) =>
            Faces.Add((colour, flags, ring.Select((_, i) => Edge(ring[i], ring[(i + 1) % ring.Length])).ToArray()));

        /// <summary>A box; zt0 / zt1 shift the top face's rear / front edge in z (a sloped back or front). Every box gets a bottom:
        /// the one given, else the model's underside colour (the Python left hidden bottoms off; the card wants whole shells).</summary>
        public void Box(int x0, int x1, int y0, int y1, int z0, int z1, int top, int side, int? bottom = null, int? front = null, int? back = null, int? zt0 = null, int? zt1 = null)
        {
            int t0 = zt0 ?? z0, t1 = zt1 ?? z1;
            int[] a = [P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1)];
            int[] b = [P(x0, y1, t0), P(x1, y1, t0), P(x1, y1, t1), P(x0, y1, t1)];
            Face(top, b);
            Face(bottom ?? underside, [a[3], a[2], a[1], a[0]]);
            Face(side, [a[1], a[2], b[2], b[1]]);                                            // right
            Face(side, [a[3], a[0], b[0], b[3]]);                                            // left
            Face(front ?? side, [a[2], a[3], b[3], b[2]]);                                   // front (+z)
            Face(back ?? side, [a[0], a[1], b[1], b[0]]);                                    // back (-z)
        }

        /// <summary>A pivot corner with a part flag (kind &lt;&lt; 4 | axis &lt;&lt; 2 | channel): everything built after it, up to the
        /// next pivot, turns about it.</summary>
        public void BeginPart(int flag, double x, double y, double z) => P(x, y, z, flag);

        /// <summary>A wheel part as Hunter's jeep has them: a pivot flagged $10 (a spin about x on channel 0) at the axle, then each
        /// wheel as two flat eight-cornered discs (x0 and x1; one face each way round, the back one flagged $80, a band across
        /// each that shows the turn) with a tread of quads between them - the form RoundWheels rounds to sixteen sides.
        /// wheels: (x0, x1, radius).</summary>
        public void Axle(int y, int z, (int X0, int X1, int R)[] wheels, int tyre, int hub, int sides = 8)
        {
            BeginPart(AxleFlag, 0, y, z);
            foreach (var (x0, x1, r) in wheels)
            {
                var rings = new List<int[]>();
                foreach (var x in new[] { x0, x1 })
                {
                    var ring = Enumerable.Range(0, sides).Select(k => P(x, y + r * Math.Cos(2 * Math.PI * k / sides), z - r * Math.Sin(2 * Math.PI * k / sides))).ToArray();
                    Face(tyre, ring);
                    Face(tyre, ring.Reverse().ToArray(), 0x80);
                    var q = sides / 4;
                    Face(hub, [ring[1], ring[2], ring[2 * q + 1], ring[2 * q + 2]], 0x80);
                    rings.Add(ring);
                }
                var (a, bb) = (rings[0], rings[1]);
                for (var k = 0; k < sides; k++)
                {
                    var j = (k + 1) % sides;
                    Face(tyre, [a[k], a[j], bb[j], bb[k]]);
                }
            }
        }

        /// <summary>`sides` corners round centre c in the plane normal to tangent t, radius r (every other corner r2 when given: a
        /// crumpled rim); corner 0 at the top.</summary>
        public int[] Ring(double[] c, double[] t, double r, double? r2 = null, int sides = 8)
        {
            var tl = Math.Sqrt(t[0] * t[0] + t[1] * t[1] + t[2] * t[2]);
            t = [t[0] / tl, t[1] / tl, t[2] / tl];
            double[] up = [0, 1, 0];
            var side = Cross(up, t);
            var sl = Math.Sqrt(side[0] * side[0] + side[1] * side[1] + side[2] * side[2]);
            side = [side[0] / sl, side[1] / sl, side[2] / sl];
            up = Cross(t, side);
            return Enumerable.Range(0, sides).Select(k =>
            {
                var rr = k % 2 == 0 || r2 is null ? r : r2.Value;
                double ca = Math.Cos(2 * Math.PI * k / sides), sa = Math.Sin(2 * Math.PI * k / sides);
                return P(c[0] + rr * (ca * up[0] + sa * side[0]), c[1] + rr * (ca * up[1] + sa * side[1]), c[2] + rr * (ca * up[2] + sa * side[2]));
            }).ToArray();
        }

        static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

        /// <summary>Quads between successive rings, one colour.</summary>
        public void Tube(int[][] rings, int colour)
        {
            for (var k = 0; k + 1 < rings.Length; k++)
                for (var i = 0; i < rings[k].Length; i++)
                {
                    var j = (i + 1) % rings[k].Length;
                    Face(colour, [rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]]);
                }
        }

        /// <summary>Into the card model: y centred between the lowest and highest corner when asked (the vehicles; the worm keeps
        /// the sand at 0), the scale shift the smallest from 2 that fits every corner in the file's signed bytes, the header's
        /// extents the new ones and its special corners none; Waterline = how far the lowest corner is below y 0.</summary>
        public void Into(CardModels.Model md, bool centreY)
        {
            var ymid = centreY ? (Corners.Min(c => c.Y) + Corners.Max(c => c.Y)) / 2 : 0;
            var pts = Corners.Select(c => (c.Flag, c.X, Y: c.Y - ymid, c.Z)).ToArray();
            // whole units: the corners are no longer cut to a signed byte on the
            // scale's steps (the worm, 390 long, was on a 4-unit grid); the shift is 0 and a model past the file's format goes to the
            // rules extended (CardModels.ToWideBytes)
            md.Shift = 0;
            var head = md.Head.ToArray();
            void Word(int at, double v) { var w = (int)Math.Round(v); head[at] = (byte)(w >> 8); head[at + 1] = (byte)w; }
            Word(4, pts.Max(p => Math.Abs(p.X))); Word(6, pts.Max(p => Math.Abs(p.Y))); Word(8, pts.Max(p => Math.Abs(p.Z)));
            for (var i = 0x0C; i < 0x10; i++) head[i] = 0;
            var corners = pts.Select(p => md.FullCorner(p.Flag, Whole(p.X), Whole(p.Y), Whole(p.Z))).ToArray();
            md.Replace(corners, Edges.Select(e => new CardModels.Edge(e.A, e.B)).ToArray(),
                Faces.Select(f => new CardModels.Face(f.Colour, f.Flags, f.Edges)).ToArray(), head);
            md.Waterline = centreY ? 0 : Math.Max(0, -corners.Min(c => c.Y));
        }

        static int Whole(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
    }
}
