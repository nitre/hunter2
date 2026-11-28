using System.Collections.Generic;
using System.Linq;

namespace Hunter;

/// <summary>STUNTIN': the racer - Hunter's car ($13) as the card draws it (its round wheels, seats, steering wheel and driver:
/// CarInside) rebuilt to Stunt Car Racer's car (docs/scr-physics.md "The drawn car", x 1.5 into our units, the road at the
/// model's y -20): a single-seater, its wheels at the corners - the physics' contact points, track 64 and wheelbase 128,
/// here x +-46 and z +-96 - outside a narrow body, the whole 121 across; the driver low in the middle, the eye 30 over the road
/// (the original's 20, calculateCameraTransform). A model of its own after the objfile's (GpuWorld.RacerModel; the Stuntin'
/// cars draw it through GpuWorld.ExtraModel - the rules keep $13): the car's glass, roof and body box off; in their place a
/// low tub (+-30, a flat nose between the front wheels), an engine on the nose with a blower and four exhaust stacks a side
/// close beside it (their flames in the in-car view), coil-overs out in the open from the body to each hub, a roll hoop
/// behind the seat. Nothing ahead of the driver stands into his view but the engine and the stacks.</summary>
public static class RacingKit
{
    /// <summary>The kit's faces' origin: plain faces of the body, always drawn (GpuWorld.Bake, as the rotor masts).</summary>
    public const int Origin = -1003;
    /// <summary>The kit's pipes (the cage and the stacks): shaded by the sun, but each face one even colour, no dither (GpuWorld.EvenPaint) -
    /// at a couple of game pixels across, the shading's dots broke a pipe's edges into scraps.</summary>
    public const int SolidOrigin = -1004;
    /// <summary>The kit's panes: glass faces (GpuWorld.Bake; none on the open racer now, kept for the paint).</summary>
    public const int GlassOrigin = -1006;
    /// <summary>The car's faces the racer leaves off: its glass and its roof (the objfile's face numbers)...</summary>
    static readonly int[] Off = [6, 18, 19, 21, 23, 1, 7, 12, 17, 22, 20, 0];   // ...and the body's box (bottom, sides, bonnet, front, back)
    const int Cage = 2, Metal = 8, Black = 0;                                     // the cage painted the body's green
    const int SeatRed = 5;
    /// <summary>How much higher the racer's seat, driver and steering wheel sit than the car's (the in-car eye with them,
    /// GameView.HeadY = 52 + SeatRaise): the eye at 12 - 32 over the road, about Stunt Car Racer's 30 (the seat and the driver
    /// below the hub then pressed onto the floor: Make).</summary>
    public const int SeatRaise = -40;
    /// <summary>How far the driver moves across to the car's middle (from the car's left seat, x -30; GameView.HeadX with it).</summary>
    public const int Centre = 30;
    /// <summary>How much higher the body rides than the car's (model units, Make): the physics' springs squash 10-30 in a lap,
    /// up to 60 on a banking - at the car's 2 of clearance the body bottomed out half the time; jacked up (with the monster wheels'
    /// 12 more under the axles), 30.</summary>
    public const int Lift = 16;
    /// <summary>The racer drawn this many times its model units: the kit was built to the original's car x 1.5 in model units,
    /// and a model unit is half a world unit - drawn as built it was half the car the physics drives (its wheels 46 out to the
    /// contact points' 96), its wheels on other road than the physics felt.</summary>
    public const double Size = 2;
    /// <summary>The eye's height in the model (GameView.HeadY).</summary>
    const int EyeY = 52 + SeatRaise;
    /// <summary>The steering wheel's hub, under the eye: from it (z -16) through the in-car lens's 45 degrees the rim's top
    /// (HubY + 5.5, 16.75 world units ahead) lands on row ~110 - the top third of the rim and the hands along the view's bottom.</summary>
    const int HubY = EyeY - 11;
    /// <summary>The seat's cushion once lowered by SeatRaise (CarInside's car cushion at 12).</summary>
    const int SeatCushion = 12 + SeatRaise;
    /// <summary>THE BODY: the tub's half width, the nose's (between the front wheels: their tyres' inner edges swing in to about
    /// +-25 at full lock), the floor, the tub's top edge, the nose's front top, and the body's ends (the nose at the original's
    /// 57.5 x 1.5 ahead, the tail at its rear contact point).</summary>
    const int Tub = 30, Nose = 22, FloorY = -18, TubTop = 2, NoseTip = -8, NoseZ = 86, NoseFrom = 66, Tail = -96, CockpitFront = 20, CockpitBack = -40;
    /// <summary>THE WHEELS at the original's contact points: axles at +-WheelZ (the car's at +-80 moved along), tyres from
    /// +-TyreIn out to +-WheelOut (28 wide, radius WheelR), discs at +-DiscX.</summary>
    public const int WheelZ = 96, TyreIn = 36, WheelOut = 64, DiscX = 60, SidewallIn = 21;
    /// <summary>The discs' halves: the car's lighter brown (10) recoloured the view's grey (8, $999) against the dark brown (9, $521).</summary>
    const int DiscPale = 10, DiscLight = 8;
    /// <summary>MONSTER WHEELS: the car's wheels (radius 20) this much bigger - radius 32, the tyres 28 wide; the body's Lift
    /// less by the 12 the axles rise, so the floor keeps its clearance and the in-car eye its height, the tyres' tops over it.</summary>
    public const double WheelGrow = 1.6;
    /// <summary>The wheels' radius (model units).</summary>
    public const int WheelR = 32;
    /// <summary>THE ENGINE sunk into the nose (block +-18, its top EngineTop, the blower 3 over it) and THE STACKS close beside it,
    /// leaning out StackLean - all under the eye (12): from it the block's back edge (z 30) lands on row ~113, the blower's top
    /// on ~70-80, the stacks' tops on ~72. In the in-car view a thing shows while it is no further out than 0.41 x its way ahead
    /// of the eye (z -16): the rearmost stack (z 44) to about 25.</summary>
    const int EngineTop = 4;
    const int StackIn = 22, StackLean = 2, StackBase = -4;
    static readonly int[] StackZ = [44, 52, 60, 68];
    /// <summary>A stack's shape: round (StackSides, radius StackR), straight from its foot (x StackIn, StackBase) leaning out
    /// StackLean in 12 up to StackStraightTo, then curving out by StackBend degrees round a StackBendR radius, StackArcSteps
    /// steps.</summary>
    const int StackSides = 8, StackArcSteps = 3;
    const double StackR = 4, StackStraightTo = 4, StackBendR = 6, StackBend = 25;

    /// <summary>A stack's centre line, foot to open end (units, before the jack-up): each point, the pipe's way there and its
    /// across-way in the bend's plane (out from the bend's middle; the other across-way is z).</summary>
    static ((double X, double Y, double Z) At, (double X, double Y, double Z) Way, (double X, double Y, double Z) Out)[] StackPath(int x, int z)
    {
        double dl = System.Math.Sqrt(StackLean * StackLean + 144.0);
        (double X, double Y) d0 = (x * StackLean / dl, 12 / dl);
        var dot = x * d0.X;                                                  // out (x, 0) less its share along d0: out in the plane
        (double X, double Y) n0 = (x - dot * d0.X, -dot * d0.Y);
        var nl = System.Math.Sqrt(n0.X * n0.X + n0.Y * n0.Y); n0 = (n0.X / nl, n0.Y / nl);
        (double X, double Y) foot = (x * StackIn, StackBase);
        var run = (StackStraightTo - StackBase) / d0.Y;
        (double X, double Y) s = (foot.X + d0.X * run, foot.Y + d0.Y * run), c = (s.X + n0.X * StackBendR, s.Y + n0.Y * StackBendR);
        var list = new List<((double X, double Y, double Z), (double X, double Y, double Z), (double X, double Y, double Z))>
        {
            ((foot.X, foot.Y, z), (d0.X, d0.Y, 0), (n0.X, n0.Y, 0)),
        };
        for (var k = 0; k <= StackArcSteps; k++)
        {
            var f = StackBend * System.Math.PI / 180 * k / StackArcSteps;
            double cf = System.Math.Cos(f), sf = System.Math.Sin(f);
            (double X, double Y) m = (n0.X * cf - d0.X * sf, n0.Y * cf - d0.Y * sf);   // from the bend's middle to the pipe's line, inward
            list.Add(((c.X - m.X * StackBendR, c.Y - m.Y * StackBendR, z), (d0.X * cf + n0.X * sf, d0.Y * cf + n0.Y * sf, 0), (m.X, m.Y, 0)));
        }
        return [.. list];
    }

    /// <summary>The stacks' open ends (units, as drawn: jacked up by Lift) and their way out - where the boost flames come from
    /// (GameView.FlameStep).</summary>
    public static readonly ((double X, double Y, double Z) Tip, (double X, double Y, double Z) Axis)[] Stacks =
        [.. from x in new[] { -1, 1 } from z in StackZ let end = StackPath(x, z)[^1] select ((end.At.X, end.At.Y + Lift, end.At.Z), end.Way)];
    /// <summary>The racer's grid: its corners kept at 1 / Fine of a unit (the model stored Fine times its size; the card draws it
    /// back down, GpuWorld fine_model) - on whole units its thin tube came out lumpy and warped.</summary>
    public const int Fine = 4;
    /// <summary>The racer's corners (its own numbering) that ride up and down with their wheel besides those out past the spring
    /// line: its struts' damper rods (model.gdshaderinc racer_ride_from / racer_ride_to).</summary>
    public static int RideFrom { get; private set; }
    public static int RideTo { get; private set; }

    /// <summary>Appends the racer to the store (after the car's interior is built) and returns its number.</summary>
    public static int Make(CardModels store, Dictionary<int, int[]> origins)
    {
        var racer = store.AppendClone(CarInside.Car);
        var md = store.Models[racer];
        var from = origins.TryGetValue(CarInside.Car, out var o) ? o : Enumerable.Range(0, md.Faces.Length).ToArray();
        var keep = Enumerable.Range(0, md.Faces.Length).Where(i => System.Array.IndexOf(Off, from[i]) < 0).ToArray();
        var corners = md.Corners.ToList();
        var edges = md.Edges.ToList();
        var faces = keep.Select(i => md.Faces[i]).ToList();
        var origin = keep.Select(i => from[i]).ToList();
        // THE INSIDE: CarInside built it as two seats (3 faces each), then the shelf, then the rest (its dash panel) - in that
        // order, the first of its faces (origin -1) after the car's own. The seats racing red; the shelf and the rest off (wider
        // than the racer's tub; its own decks below)
        var inside = Enumerable.Range(0, faces.Count).Where(i => origin[i] == -1).ToArray();
        for (var k = 0; k < 6 && k < inside.Length; k++) faces[inside[k]] = faces[inside[k]] with { Colour = SeatRed };
        // THE SEAT, THE DRIVER AND THE WHEEL by SeatRaise: the seats' corners (CarInside's first six faces), the driver's (its
        // faces' origins -(2 + red)), the steering wheel's hub and rim (its pivot, CarInside.WheelFlag, and the 8 after)
        {
            var up = new HashSet<int>();
            void Take(int f) { foreach (var e in faces[f].Edges) { up.Add(edges[e].A); up.Add(edges[e].B); } }
            for (var k = 0; k < 6 && k < inside.Length; k++) Take(inside[k]);
            for (var f = 0; f < faces.Count; f++) if (origin[f] <= -2) Take(f);
            var hub = corners.FindIndex(c => c.Flag == CarInside.WheelFlag);
            if (hub >= 0) for (var k = hub; k <= hub + 8 && k < corners.Count; k++) up.Add(k);
            foreach (var k in up) corners[k] = corners[k] with { Y = corners[k].Y + SeatRaise };
        }
        // THE CENTRE SEAT (Stunt Car Racer's car is a single-seater, the driver in the middle): the passenger's seat off, the
        // driver's seat, the driver and the steering wheel moved Centre across to the middle; the shelf and the rest off
        {
            double MeanX(int f) => faces[f].Edges.SelectMany(e => new[] { edges[e].A, edges[e].B }).Average(c => corners[c].X);
            var seats = inside.Take(6).ToList();
            var drop = seats.Where(f => MeanX(f) > 0).Concat(inside.Skip(6)).ToHashSet();
            var move = new HashSet<int>();
            void Take(int f) { foreach (var e in faces[f].Edges) { move.Add(edges[e].A); move.Add(edges[e].B); } }
            foreach (var f in seats.Where(f => !drop.Contains(f))) Take(f);
            for (var f = 0; f < faces.Count; f++) if (origin[f] <= -2) Take(f);
            var hub = corners.FindIndex(c => c.Flag == CarInside.WheelFlag);
            if (hub >= 0) for (var k = hub; k <= hub + 8 && k < corners.Count; k++) move.Add(k);
            foreach (var k in move) corners[k] = corners[k] with { X = corners[k].X + Centre };
            foreach (var f in drop.OrderByDescending(f => f)) { faces.RemoveAt(f); origin.RemoveAt(f); }
        }
        // CORNERS NO FACE USES (the passenger's seat and the car's own faces left off): up to the floor - the card stands the racer
        // on its lowest corner at rest (model_min_y_plain), and the passenger seat's, lowered with the seat to -40, held every
        // tyre 20 off the road
        {
            var used = faces.SelectMany(f => f.Edges).SelectMany(e => new[] { edges[e].A, edges[e].B }).ToHashSet();
            for (var k = 0; k < corners.Count; k++)
                if (!used.Contains(k) && corners[k].Flag == 0 && corners[k].Y < FloorY) corners[k] = corners[k] with { Y = FloorY };
        }
        var seated = faces.Select((f, i) => (f, i)).Where(p => p.f.Colour == SeatRed || origin[p.i] <= -2)   // the seat's and the driver's corners
            .SelectMany(p => p.f.Edges).SelectMany(e => new[] { edges[e].A, edges[e].B }).ToHashSet();
        var kb = new KitBuilder(corners, edges, faces, origin);           // Tube / Bent make solid faces, Box shaded ones
        int Corner(double x, double y, double z) => kb.Corner(x, y, z);
        void Face(int colour, (double X, double Y, double Z) inside, params int[] loop) => kb.Face(colour, inside, loop);
        void Tube(int colour, (double X, double Y, double Z) a, (double X, double Y, double Z) b, double r, int capA = -1, int capB = -1, bool round = false)
            => kb.Tube(colour, a, b, r, capA, capB, round);
        void Bent(int colour, double r, params (double X, double Y, double Z)[] p) => kb.Bent(colour, r, p);
        void Box(int colour, int top, int x0, int y0, int z0, int x1, int y1, int z1, bool bottom = false) => kb.Box(colour, top, x0, y0, z0, x1, y1, z1, bottom);
        static (double X, double Y, double Z) BendMiddle((double X, double Y, double Z) a, (double X, double Y, double Z) c, (double X, double Y, double Z) b) => KitBuilder.BendMiddle(a, c, b);
        // THE WHEELS AT THE CORNERS ON TYRES: the car's wheels are flat discs at x +-60, axles at z +-80; the racer's move along to
        // +-WheelZ (KitBuilder.MoveWheels) and across to +-DiscX, each on a black tread band from +-TyreIn out to +-WheelOut (its 16
        // corners exactly the rim's, RoundWheels' rounding) with a black inner wall at +-TyreIn. The bands stand still (round, so a
        // turning band would look the same); the discs turn.
        kb.MoveWheels(60, WheelZ);
        var rims = kb.ScaleWheels(kb.WidenWheels(60, DiscX), DiscX, WheelGrow);
        // THE DISCS READ THEIR SPIN: each disc is two halves, the car's two near browns (9, 10) - alike, the split looked the same a
        // half turn on, and at racing speed (a quarter to a third of a turn a frame) the wheel seemed still or turning back. One half
        // DiscLight: the pattern repeats once a turn, so up to half a turn a frame the nearest match is the true way round
        for (var f = 0; f < kb.Faces.Count; f++)
            if (kb.Faces[f].Colour == DiscPale && kb.Faces[f].Edges.All(e => System.Math.Abs(kb.Corners[kb.Edges[e].A].X) == DiscX && System.Math.Abs(kb.Corners[kb.Edges[e].B].X) == DiscX))
                kb.Faces[f] = kb.Faces[f] with { Colour = DiscLight };
        // THE FINE GRID: the car's corners (seat and wheels set above, in units) scaled up; the kit's from here in units through Corner
        kb.FineScale();
        // THE SEAT ON THE FLOOR: the car's seated driver is 40 from cushion to eye, the racer's floor 30 under the eye - the seat's
        // and the driver's corners between the cushion and the wheel's hub are pressed into the floor to the hub, those under the
        // cushion (the seat's front, the feet) onto the floor; the eye, the hands and the wheel stay
        {
            int cushion = SeatCushion * Fine, hub = HubY * Fine, floor = FloorY * Fine;
            foreach (var k in seated)
            {
                var y = kb.Corners[k].Y;
                if (y >= hub) continue;
                kb.Corners[k] = kb.Corners[k] with { Y = y <= cushion ? floor : floor + (int)System.Math.Round((double)(y - cushion) * (hub - floor) / (hub - cushion)) };
            }
        }
        var tyreCorners = kb.Tyres(rims, TyreIn, WheelOut, SidewallIn);
        // THE TUB: the floor and the sides up to TubTop from the tail to NoseFrom, a step in to the nose there, the nose's sides
        // falling to NoseTip at its front, the front and back faces; decks over the tail (behind the cockpit) and over the nose
        // (ahead of it), the cockpit open between CockpitBack and CockpitFront
        kb.FaceOrigin = Origin;
        kb.Shared = [];                                                      // their edges shared with each other
        const int Body = 2, Bonnet = 3, Front = 1, Under = 0, FloorPaint = 1, DeckPaint = 2;
        foreach (var s in new[] { -1, 1 })
        {
            Face(Body, (0, -8, -10), Corner(s * Tub, FloorY, Tail), Corner(s * Tub, TubTop, Tail), Corner(s * Tub, TubTop, NoseFrom), Corner(s * Tub, FloorY, NoseFrom));
            Face(Body, (0, -8, 76), Corner(s * Nose, FloorY, NoseFrom), Corner(s * Nose, TubTop, NoseFrom), Corner(s * Nose, NoseTip, NoseZ), Corner(s * Nose, FloorY, NoseZ));
            Face(Body, (s * 26, -8, 50), Corner(s * Nose, FloorY, NoseFrom), Corner(s * Tub, FloorY, NoseFrom), Corner(s * Tub, TubTop, NoseFrom), Corner(s * Nose, TubTop, NoseFrom));
        }
        Face(Front, (0, -8, 60), Corner(-Nose, FloorY, NoseZ), Corner(Nose, FloorY, NoseZ), Corner(Nose, NoseTip, NoseZ), Corner(-Nose, NoseTip, NoseZ));
        Face(Front, (0, -8, 0), Corner(-Tub, FloorY, Tail), Corner(Tub, FloorY, Tail), Corner(Tub, TubTop, Tail), Corner(-Tub, TubTop, Tail));
        Face(FloorPaint, (0, 10, 0), Corner(-Tub, FloorY, Tail), Corner(Tub, FloorY, Tail), Corner(Tub, FloorY, NoseFrom), Corner(-Tub, FloorY, NoseFrom));
        Face(Under, (0, 10, 76), Corner(-Nose, FloorY, NoseFrom), Corner(Nose, FloorY, NoseFrom), Corner(Nose, FloorY, NoseZ), Corner(-Nose, FloorY, NoseZ));
        Face(DeckPaint, (0, -10, -60), Corner(-Tub, TubTop, Tail), Corner(Tub, TubTop, Tail), Corner(Tub, TubTop, CockpitBack), Corner(-Tub, TubTop, CockpitBack));
        Face(Bonnet, (0, -10, 40), Corner(-Tub, TubTop, CockpitFront), Corner(Tub, TubTop, CockpitFront), Corner(Tub, TubTop, NoseFrom), Corner(-Tub, TubTop, NoseFrom));
        Face(Bonnet, (0, -10, 76), Corner(-Nose, TubTop, NoseFrom), Corner(Nose, TubTop, NoseFrom), Corner(Nose, NoseTip, NoseZ), Corner(-Nose, NoseTip, NoseZ));
        // the cockpit's front and back walls, the tub's top to its floor
        Face(Body, (0, -8, 0), Corner(-Tub, FloorY, CockpitFront), Corner(Tub, FloorY, CockpitFront), Corner(Tub, TubTop, CockpitFront), Corner(-Tub, TubTop, CockpitFront));
        Face(Body, (0, -8, 0), Corner(-Tub, FloorY, CockpitBack), Corner(Tub, FloorY, CockpitBack), Corner(Tub, TubTop, CockpitBack), Corner(-Tub, TubTop, CockpitBack));
        kb.Shared = null;
        // THE ROLL CAGE over the driver, flat-topped, pipes radius 3 bent as a real cage is (a pipe joining another ends on that
        // pipe's middle line - no burr): the main hoop behind the seat (z -36) and a front hoop just ahead of his head (FrontHoopZ),
        // each from the tub's top edge over his head (22) at HoopTop; the roof rails join their top corners, level (the two hoops'
        // tops the roof's only cross bars); the bottom frame, one bent pipe along the tub's top edges (its middle R above them) from the
        // cockpit's front round the tail; struts from the main hoop's top corners down onto the frame's back corners. OUT OF THE
        // SEAT'S VIEW: the front hoop's posts (+-22 to +-27) stand 14 ahead of the eye, where the 45 degree view reaches only 6
        // out; its top and the roof 16 over the eye, above the view
        const double R = 3, Low = TubTop + R, Wall = Tub - R, HoopZ = -36, HoopTop = 28, FrontHoopZ = -2;
        (double X, double Y, double Z)[] main = [(-Wall, Low, HoopZ), (-22, HoopTop, HoopZ), (22, HoopTop, HoopZ), (Wall, Low, HoopZ)];
        (double X, double Y, double Z)[] front = [(-Wall, Low, FrontHoopZ), (-22, HoopTop, FrontHoopZ), (22, HoopTop, FrontHoopZ), (Wall, Low, FrontHoopZ)];
        (double X, double Y, double Z)[] bottom = [(-Wall, Low, CockpitFront), (-Wall, Low, Tail + R), (Wall, Low, Tail + R), (Wall, Low, CockpitFront)];
        Bent(Cage, R, bottom);
        Bent(Cage, R, main);
        Bent(Cage, R, front);
        foreach (var x in new[] { -1, 1 })
        {
            int at = x < 0 ? 1 : 2;
            var mainTop = BendMiddle(main[at - 1], main[at], main[at + 1]);
            var frontTop = BendMiddle(front[at - 1], front[at], front[at + 1]);
            var backCorner = BendMiddle(bottom[at - 1], bottom[at], bottom[at + 1]);
            Tube(Cage, mainTop, frontTop, R);                                         // roof rail, hoop corner to hoop corner
            Tube(Cage, mainTop, backCorner, R);                                       // rear strut, down onto the frame's back corner
        }
        // THE NUMBER PLATE on the back face (z Tail), low and centred: a light plate a unit proud of it, STUNTIN on it in square
        // letters 4 x 5 units, strokes a unit thick, a unit apart (each glyph drawn in half units, 8 x 5: a stroke is "##", so the
        // T's and I's stems sit centred), each row's run one face, a unit proud of the plate - both facing back
        kb.FaceOrigin = Origin;
        {
            var glyph = new Dictionary<char, string[]>
            {
                ['S'] = ["########", "##......", "########", "......##", "########"],
                ['T'] = ["########", "...##...", "...##...", "...##...", "...##..."],
                ['N'] = ["##....##", "####..##", "##..####", "##....##", "##....##"],
                ['U'] = ["##....##", "##....##", "##....##", "##....##", "########"],
                ['I'] = ["########", "...##...", "...##...", "...##...", "########"],
            };
            const string Text = "STUNTIN";
            double Gap(int i) => i == Text.Length - 1 ? 0 : 1;                   // a unit between letters
            var w = Enumerable.Range(0, Text.Length).Sum(i => glyph[Text[i]][0].Length / 2.0 + Gap(i));   // 34 wide, 5 high
            double x0 = -w / 2; int y0 = -1;
            const int PlateColour = 8, Letters = 0;
            Face(PlateColour, (0, -6, Tail + 14), Corner(x0 - 2, y0 - 7, Tail - 1), Corner(x0 + w + 2, y0 - 7, Tail - 1), Corner(x0 + w + 2, y0 + 2, Tail - 1), Corner(x0 - 2, y0 + 2, Tail - 1));
            var at = x0;
            for (var i = 0; i < Text.Length; i++)
            {
                var g = glyph[Text[i]];
                for (var row = 0; row < 5; row++)
                    for (var k = 0; k < g[row].Length; k++)
                    {
                        if (g[row][k] != '#' || (k > 0 && g[row][k - 1] == '#')) continue;
                        var end = k; while (end + 1 < g[row].Length && g[row][end + 1] == '#') end++;
                        double ax = at + k / 2.0, bx = at + (end + 1) / 2.0; int top = y0 - row, low = top - 1;
                        Face(Letters, (0, -6, Tail + 14), Corner(ax, low, Tail - 2), Corner(bx, low, Tail - 2), Corner(bx, top, Tail - 2), Corner(ax, top, Tail - 2));
                    }
                at += g[0].Length / 2.0 + Gap(i);
            }
        }
        // THE ENGINE sunk into the nose, in small parts: a crankcase low in the nose; FOUR CYLINDERS A SIDE (x 9 to 17, 6 long, 2
        // apart, their tops EngineTop + 1), one beside each stack, a short header pipe from each out to its stack; the intake valley
        // between the banks; the blower on it, ribbed across fore and aft, its intake on top with a dark mouth; a pulley at the
        // front. FOUR STACKS A SIDE close beside the cylinders leaning out (StackIn, StackLean), round, open-topped (dark inside)
        Box(Metal, Metal, -12, EngineTop - 8, 32, 12, EngineTop - 3, 76);                 // the crankcase
        foreach (var s in new[] { -1, 1 })
            foreach (var z in StackZ)
            {
                Box(Metal, Metal, s < 0 ? -17 : 9, EngineTop - 3, z - 3, s < 0 ? -9 : 17, EngineTop + 1, z + 3);   // a cylinder
                Tube(Metal, (s * 17.0, EngineTop - 1.0, z), (s * (StackIn - 2.5), EngineTop - 1.0, z), 1.2, round: true);   // its header
            }
        Box(Metal, Metal, -7, EngineTop - 3, 36, 7, EngineTop, 72);                       // the intake valley
        Box(Metal, Metal, -5, EngineTop, 42, 5, EngineTop + 2, 66);                       // the blower
        foreach (var z in new[] { 44, 63 }) Box(Metal, Metal, -5, EngineTop + 2, z, 5, EngineTop + 3, z + 1);   // its ribs
        Box(Metal, Black, -3, EngineTop + 2, 48, 3, EngineTop + 4, 58);                   // its intake, the mouth dark
        Box(Metal, Metal, -4, EngineTop - 4, 76, 4, EngineTop, 79);                       // the front pulley
        // each stack round (StackSides), straight up from its foot leaning out, then curving out (StackPath), its open end dark
        foreach (var x in new[] { -1, 1 })
            foreach (var z in StackZ)
            {
                var path = StackPath(x, z);
                var rings = path.Select(p => Enumerable.Range(0, StackSides).Select(i =>
                {
                    double t = i * System.Math.Tau / StackSides, cs = System.Math.Cos(t) * StackR, sn = System.Math.Sin(t) * StackR;
                    return Corner(p.At.X + p.Out.X * cs, p.At.Y + p.Out.Y * cs, p.At.Z + sn);   // across: out in the bend's plane, and along z
                }).ToArray()).ToArray();
                for (var k = 0; k + 1 < path.Length; k++)
                {
                    (double X, double Y, double Z) mid = ((path[k].At.X + path[k + 1].At.X) / 2, (path[k].At.Y + path[k + 1].At.Y) / 2, (path[k].At.Z + path[k + 1].At.Z) / 2);
                    for (var i = 0; i < StackSides; i++) Face(Metal, mid, rings[k][i], rings[k][(i + 1) % StackSides], rings[k + 1][(i + 1) % StackSides], rings[k + 1][i]);
                }
                var end = path[^1];
                Face(Black, (end.At.X - end.Way.X, end.At.Y - end.Way.Y, end.At.Z - end.Way.Z), [.. rings[^1]]);
            }
        // THE SUSPENSION, out in the open between the body and each wheel: DOUBLE WISHBONES - an upper and a lower A-arm, each two
        // grey arms from the body to the hub's upright (its top and foot at x +-38, 8 over and under the axle) - and a COIL-OVER
        // per wheel (below). The spring line is 33 (_racer_spring): the arms' body ends and the springs inside it ride with the
        // body, the arms' hub ends and the uprights with the wheel, the dampers by the ride block.
        // The coil-over: its eye CoilLowX out on the lower arm (the tyre from TyreIn), its bracket CoilInset in from the body's side
        // on a post CoilTower over the tub's top; the damper DamperUp tall, the shaft down to ShaftFootUp over the eye, the spring's
        // Coils above the damper (each CoilFill of its share): over the wheel's travel, 12 up and 8 down, the shaft's foot stays
        // inside the damper (over its eye by 12, under its top by 8) and the damper's top under the bracket (SpringBumpTravel)
        const double Upright = 38, JointUp = 8, JointLow = -8, Arm = 1.2;
        const double CoilLowX = 34, CoilInset = 3, CoilTower = 12, ShaftFootUp = 12.5, DamperUp = 21, SpringBumpTravel = 12, CoilFill = 0.6,
            SpringR = 2.2, DamperR = 1.4, ShaftR = 0.6, EyeR = 1.2, EyeHalf = 1.8;
        const int Coils = 5;
        var rides = new List<((double X, double Y, double Z) A, (double X, double Y, double Z) B, double R, int Colour)>();
        foreach (var s in new[] { -1, 1 })
        {
            var ends = new[]
            {
                (Wheel: (double)WheelZ, In: (double)Nose, UpY: -4.0, LowY: -15.0, Za: 80.0, Zb: 86.0, Strut: 84.0),
                (Wheel: -(double)WheelZ, In: (double)Tub, UpY: (double)TubTop, LowY: -15.0, Za: -86.0, Zb: -94.0, Strut: -90.0),
            };
            foreach (var e in ends)
            {
                (double X, double Y, double Z) top = (s * Upright, JointUp, e.Wheel), foot = (s * Upright, JointLow, e.Wheel);
                foreach (var z in new[] { e.Za, e.Zb })
                {
                    Tube(Metal, (s * e.In, e.UpY, z), top, Arm, round: true);                    // the upper A-arm
                    Tube(Metal, (s * e.In, e.LowY, z), foot, Arm, round: true);                  // the lower A-arm
                }
                Tube(Metal, top, foot, 1.5, round: true);                                         // the hub's upright
                // the COIL-OVER, as on any racing or RC car: a mounting eye on the front lower A-arm just inboard of the tyre (CoilLowX)
                // and a bracket on the tub's top edge, the unit between them leaning in. Moving with the WHEEL (the ride block,
                // RideFrom..RideTo): the eye and the DAMPER's dark body up from it. Moving with the BODY (jacked up with it): the
                // bracket, the grey SHAFT down from it into the damper, and the yellow SPRING's coils round the upper part. A bump
                // slides the damper up into the spring, a droop pulls it down and shows the shaft. Worked in the jacked car's final
                // heights (the body's minus Lift on the way in)
                var t = (CoilLowX - e.In) / (Upright - e.In);                                    // the eye's share along the arm
                (double X, double Y, double Z) low = (s * CoilLowX, e.LowY + Lift + (JointLow - e.LowY - Lift) * t, e.Za + (e.Wheel - e.Za) * t);
                (double X, double Y, double Z) high = (s * (e.In - CoilInset), TubTop + Lift + CoilTower, low.Z);
                (double X, double Y, double Z) At(double f) => (low.X + (high.X - low.X) * f, low.Y + (high.Y - low.Y) * f, low.Z + (high.Z - low.Z) * f);
                double Up(double y) => y / (high.Y - low.Y);                                     // a height over the eye as a share of the unit
                (double X, double Y, double Z) B((double X, double Y, double Z) p) => (p.X, p.Y - Lift, p.Z);   // a body point, before the lift
                Tube(Metal, B((high.X, high.Y, high.Z - EyeHalf)), B((high.X, high.Y, high.Z + EyeHalf)), EyeR, capA: Metal, capB: Metal, round: true);   // the bracket
                Tube(Metal, B((high.X, TubTop + Lift, high.Z)), B(high), EyeR, capA: Metal, capB: Metal, round: true);                               // the tower post
                Tube(Metal, B(high), B(At(Up(ShaftFootUp))), ShaftR, capA: Metal, capB: Metal, round: true);                                                // the shaft
                for (var c = 0; c < Coils; c++)                                                                                                           // the spring's coils
                {
                    var coilBase = Up(DamperUp + 1);
                    var f0 = coilBase + (1 - coilBase) * c / Coils;
                    Tube(7, B(At(f0)), B(At(f0 + (1 - coilBase) / Coils * CoilFill)), SpringR, capA: 7, capB: 7, round: true);
                }
                rides.Add(((low.X, low.Y, low.Z - EyeHalf), (low.X, low.Y, low.Z + EyeHalf), EyeR, Metal));                                              // the eye
                rides.Add((low, At(Up(DamperUp)), DamperR, Black));                                                                                        // the damper
                if (high.Y - low.Y < DamperUp + SpringBumpTravel) throw new System.InvalidOperationException("racing kit: a coil-over too short for its travel");
                if (System.Math.Abs(At(Up(DamperUp + 1)).X) + SpringR > 33 || System.Math.Abs(high.X) + EyeR > 33) throw new System.InvalidOperationException("racing kit: a spring past the spring line");
                if (System.Math.Abs(low.X) + DamperR >= TyreIn) throw new System.InvalidOperationException("racing kit: a damper into the tyre");            }
        }
        kb.FaceOrigin = Origin;
        // THE STEERING WHEEL: a small racing wheel low in front of the driver in place of CarInside's disc (its sectors and column
        // off) - a black rim (radius 5.5, round tube) on three spokes and a boss, about the car's wheel pivot moved to HubY, z 18:
        // low in the in-car view, the top of its rim and the hands along the bottom. THE DRIVER'S ARMS are the soldier's (model
        // $01: an upper arm and a forearm, each a flat strip painted on both sides - the sleeve 12 / 1, the forearm and hand 9 / 10),
        // from the shoulders to hands on the rim at half past 10 and half past 1: the left forearm's hand end turns with the wheel
        // (its corners in the wheel's part, put there below); the right one's is a HAND part (kind 7, model.gdshaderinc: turns
        // with the wheel, and slides in to the red boost button on the boss as channel 3 - its way there, GpuWorld.RacerParts -
        // rises to 255, the elbow following, not steered); the left elbow and the shoulders stay. A column from the floor to the boss
        var hubK = corners.FindIndex(c => c.Flag == CarInside.WheelFlag);
        corners[hubK] = corners[hubK] with { Y = HubY * Fine, Z = 18 * Fine };
        double hx = corners[hubK].X / (double)Fine, hy = corners[hubK].Y / (double)Fine, hz = corners[hubK].Z / (double)Fine;
        {
            var oldWheel = Enumerable.Range(hubK, 9).ToHashSet();
            for (var f = faces.Count - 1; f >= 0; f--)
                if (faces[f].Edges.Any(e => oldWheel.Contains(edges[e].A) || oldWheel.Contains(edges[e].B))) { faces.RemoveAt(f); origin.RemoveAt(f); }
        }
        const double Rim = 5.5, RimTube = 0.5;
        const int Sleeve = 12, SleeveBack = 1, Hand = 9, HandBack = 10, Button = 5;   // the soldier's paints
        (double X, double Y, double Z) OnRim(double deg) => (hx + Rim * System.Math.Cos(deg * System.Math.PI / 180), hy + Rim * System.Math.Sin(deg * System.Math.PI / 180), hz - 0.5);
        // a strip's end at p, w wide, level and square to the strip's way (dir)
        int[] StripEnd((double X, double Y, double Z) p, (double X, double Y, double Z) dir, double w)
        {
            double ux = dir.Z, uz = -dir.X, l = System.Math.Sqrt(ux * ux + uz * uz);
            ux *= w / 2 / l; uz *= w / 2 / l;
            return [Corner(p.X - ux, p.Y, p.Z - uz), Corner(p.X + ux, p.Y, p.Z + uz)];
        }
        // a strip between two ends, painted on both sides (two faces back to back: the card draws such a pair as one thin slab)
        void Strip(int front, int back, int[] a, int[] b)
        {
            kb.FaceOrigin = Origin;
            (double X, double Y, double Z) U(int k) => (corners[k].X / (double)Fine, corners[k].Y / (double)Fine, corners[k].Z / (double)Fine);
            var (p0, p1, p2) = (U(a[0]), U(a[1]), U(b[1]));
            double ex = p1.X - p0.X, ey = p1.Y - p0.Y, ez = p1.Z - p0.Z, fx = p2.X - p0.X, fy = p2.Y - p0.Y, fz = p2.Z - p0.Z;
            double nx = ey * fz - ez * fy, ny = ez * fx - ex * fz, nz = ex * fy - ey * fx;
            if (ny < 0) { nx = -nx; ny = -ny; nz = -nz; }                        // the front: the upper side
            double cx = (p0.X + p2.X) / 2, cy = (p0.Y + p2.Y) / 2, cz = (p0.Z + p2.Z) / 2;
            Face(front, (cx - nx, cy - ny, cz - nz), a[0], a[1], b[1], b[0]);
            Face(back, (cx + nx, cy + ny, cz + nz), a[0], a[1], b[1], b[0]);
        }
        static (double X, double Y, double Z) Way((double X, double Y, double Z) from, (double X, double Y, double Z) to) => (to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        (double X, double Y, double Z) leftGrip = OnRim(135), rightGrip = OnRim(45);
        // the elbows 9 under the hub, the shoulders 3 under the eye (as at first: hub 49, elbows 40, shoulders 57, the eye 60)
        (double X, double Y, double Z) leftElbow = (-47 + Centre, HubY - 9, 1), rightElbow = (-13 + Centre, HubY - 9, 1),
            leftShoulder = (-44 + Centre, EyeY - 3, -15), rightShoulder = (-16 + Centre, EyeY - 3, -15);
        var wheelFrom = corners.Count;
        for (var i = 0; i < 20; i++) Tube(Black, OnRim(i * 18), OnRim((i + 1) * 18), RimTube, round: true);
        foreach (var deg in new[] { 0.0, 180, 270 }) Tube(Metal, (hx, hy, hz), OnRim(deg), 0.6);
        Tube(Metal, (hx, hy, hz + 1), (hx, hy, hz - 1.5), 1.8, capA: Metal, capB: Metal);
        Tube(Button, (hx, hy, hz - 1.5), (hx, hy, hz - 2.1), 1.4, capA: Button, capB: Button, round: true);   // the boost button, on the boss
        var leftHand = StripEnd(leftGrip, Way(leftElbow, leftGrip), 5);
        var wheelTo = corners.Count;
        (double X, double Y, double Z) buttonAt = (hx + 0.5, hy + 0.3, hz - 2.6);   // the hand's end on the boost button
        var handPivot = corners.Count;
        corners.Add(new(7 << 4 | 3, (int)System.Math.Round(hx * Fine), (int)System.Math.Round(hy * Fine), (int)System.Math.Round(hz * Fine)));
        corners.Add(new(0, (int)System.Math.Round((hx + buttonAt.X - rightGrip.X) * Fine), (int)System.Math.Round((hy + buttonAt.Y - rightGrip.Y) * Fine),
            (int)System.Math.Round((hz + buttonAt.Z - rightGrip.Z) * Fine)));                               // the slide: the button less the hand at rest
        var handFrom = corners.Count;
        var rightHand = StripEnd(rightGrip, Way(rightElbow, rightGrip), 5);
        corners.Add(corners[handPivot] with { Flag = 7 << 4 | 1 << 2 | 3 });     // the right elbow: the same slide, not steered (axis 1)
        corners.Add(corners[handPivot + 1]);
        var rightElbowEnd = StripEnd(rightElbow, Way(rightElbow, rightGrip), 5.5);
        var handTo = corners.Count;
        var leftElbowEnd = StripEnd(leftElbow, Way(leftElbow, leftGrip), 5.5);
        foreach (var (hand, elbowEnd, elbow, shoulder) in new[] { (leftHand, leftElbowEnd, leftElbow, leftShoulder), (rightHand, rightElbowEnd, rightElbow, rightShoulder) })
        {
            Strip(Hand, HandBack, hand, elbowEnd);                                // the forearm and hand
            Strip(Sleeve, SleeveBack, elbowEnd, StripEnd(shoulder, Way(shoulder, elbow), 6));   // the upper arm
        }
        Tube(Metal, (hx, FloorY, 31), (hx, hy - 1, hz + 1), 1.2);                // the column
        kb.FaceOrigin = Origin;
        // THE FRONT WHEELS STEER (KitBuilder.SteerFront); the steering wheel's part runs on after the car's own corners - its rim,
        // spokes and left hand - then the right hand's part
        kb.SteerFront(md.Corners.Length, TyreIn, WheelOut, tyreCorners,
            [.. Enumerable.Range(wheelFrom, wheelTo - wheelFrom), handPivot, handPivot + 1, .. Enumerable.Range(handFrom, handTo - handFrom)]);
        // THE COIL-OVERS' DAMPERS, a block of their own riding with their wheels (RideFrom..RideTo, GpuWorld racer_ride_from / _to)
        RideFrom = kb.Corners.Count;
        foreach (var (ra, rb, rr, rc) in rides) Tube(rc, ra, rb, rr, capA: rc, capB: rc, round: true);   // the coil-overs' eyes and dampers
        RideTo = kb.Corners.Count;
        // JACKED UP: everything inside the spring line (the body, the cage, the engine, the seat, the driver, the struts and
        // their rods, the arms' body ends) Lift higher; the wheels, their pivots, the uprights and the arms' hub ends stay - the
        // tyres on the road, the arms reaching down to the hubs
        for (var k = 0; k < kb.Corners.Count; k++)
        {
            var c = kb.Corners[k];
            if (System.Math.Abs(c.X) > 33 * Fine || k >= RideFrom && k < RideTo) continue;   // the wheels'; the dampers ride with them
            var kind = (c.Flag >> 4) & 7;
            if (c.Flag != 0 && (kind == 1 && ((c.Flag >> 2) & 3) == 0 || kind == 6)) continue;   // a wheel's spin or steering pivot
            kb.Corners[k] = c with { Y = c.Y + Lift * Fine };
        }
        corners = kb.Corners; edges = kb.Edges;        md.Replace([.. corners], [.. edges], [.. faces]);
        origins[racer] = [.. origin];
        if (System.Environment.GetEnvironmentVariable("HUNTER_DUMP_RACER") is { Length: > 0 } dump)   // checks: the model's faces for a ray cast
            System.IO.File.WriteAllText(dump, System.Text.Json.JsonSerializer.Serialize(new
            {
                fine = Fine,
                rideFrom = RideFrom, rideTo = RideTo,
                corners = corners.Select(c => new[] { c.X, c.Y, c.Z }).ToArray(),
                faces = faces.Select((f, i) => new { colour = f.Colour, origin = origin[i], loop = f.Edges.Select(e => new[] { edges[e].A, edges[e].B }).ToArray() }).ToArray(),
            }));
        store.Layout();
        return racer;
    }
}
