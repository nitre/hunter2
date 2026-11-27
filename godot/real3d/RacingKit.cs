using System.Collections.Generic;
using System.Linq;

namespace Hunter;

/// <summary>STUNTIN': the racer - Hunter's car ($13) as the card draws it (its round wheels, seats, dash, steering wheel and
/// driver: CarInside) with a racing kit, a model of its own after the objfile's (GpuWorld.RacerModel; the Stuntin' cars draw it
/// through GpuWorld.ExtraModel - the rules keep $13). Open: the car's glass (Glass.cs: faces 6 windscreen, 18 and 19 side
/// windows, 21 rear window) and its roof (face 23) off. The kit, in the car's full units (x across, y up, z forward; its body
/// x +-58 up to y 44, the bonnet's top y 36 at the front z 106 to 44 at the windscreen's foot, z 44): a roll cage (a hoop at
/// the windscreen's foot and one behind the seats, rails between them along the top) in the body's green, an engine block through
/// the bonnet with a blower on it, and four exhaust stacks a side, grey 8 with black tops (as Stunt Car Racer's own car), all
/// under the in-car eye.</summary>
public static class RacingKit
{
    /// <summary>The kit's faces' origin: plain faces of the body, always drawn (GpuWorld.Bake, as the rotor masts).</summary>
    public const int Origin = -1003;
    /// <summary>The kit's pipes (the cage and the stacks): shaded by the sun, but each face one even colour, no dither (GpuWorld.EvenPaint) -
    /// at a couple of game pixels across, the shading's dots broke a pipe's edges into scraps.</summary>
    public const int SolidOrigin = -1004;
    /// <summary>The kit's panes (the windscreen, the side windows): glass faces (GpuWorld.Bake), glass whatever GLASS says and
    /// tinted by their paint (gpumodels_body kit_glass_model: black the side windows, darker; grey the windscreen, barely).</summary>
    public const int GlassOrigin = -1006;
    /// <summary>The car's faces the racer leaves off: its glass and its roof (the objfile's face numbers).</summary>
    static readonly int[] Off = [6, 18, 19, 21, 23, 1, 7, 12, 17, 22, 20, 0];   // ...and the body's box (bottom, sides, bonnet, front): rebuilt with a nose
    const int Cage = 2, Metal = 8, Black = 0;                                     // the cage painted the body's green
    /// <summary>The open inside: seats red 5, the rear deck the body's green 2 at DeckY (the body's top edge is 44), the floor 1.</summary>
    const int SeatRed = 5, Deck = 2, DeckY = 42, Floor = 1;
    /// <summary>How much higher the racer's seats, driver and steering wheel sit than the car's (and the in-car eye with them,
    /// GameView.HeadY): from 52 the bonnet pointed at the eye; from 64 it lies below the view, the engine lower in it.</summary>
    public const int SeatRaise = 12;
    /// <summary>The racer's grid: its corners kept at 1 / Fine of a unit (the model stored Fine times its size; the card draws it
    /// back down, GpuWorld fine_model) - on whole units its thin tube came out lumpy and warped.</summary>
    public const int Fine = 4;

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
        // THE INSIDE, open now: CarInside built it as two seats (3 faces each), then the shelf, then the rest - in that order, the
        // first of its faces (origin -1) after the car's own. The seats racing red, the shelf a rear deck in the body's green just
        // under the body's top edge (the parcel shelf under a rear window that is gone), the floor (the car's face 1) dark
        var inside = Enumerable.Range(0, faces.Count).Where(i => origin[i] == -1).ToArray();
        for (var k = 0; k < 6 && k < inside.Length; k++) faces[inside[k]] = faces[inside[k]] with { Colour = SeatRed };
        // THE SEATS, THE DRIVER AND THE WHEEL up by SeatRaise: the seats' corners (CarInside's first six faces), the driver's
        // (its faces' origins -(2 + red)), the steering wheel's hub and rim (its pivot, CarInside.WheelFlag, and the 8 after)
        {
            var up = new HashSet<int>();
            void Take(int f) { foreach (var e in faces[f].Edges) { up.Add(edges[e].A); up.Add(edges[e].B); } }
            for (var k = 0; k < 6 && k < inside.Length; k++) Take(inside[k]);
            for (var f = 0; f < faces.Count; f++) if (origin[f] <= -2) Take(f);
            var hub = corners.FindIndex(c => c.Flag == CarInside.WheelFlag);
            if (hub >= 0) for (var k = hub; k <= hub + 8 && k < corners.Count; k++) up.Add(k);
            foreach (var k in up) corners[k] = corners[k] with { Y = corners[k].Y + SeatRaise };
        }
        if (inside.Length > 6)
        {
            var shelf = faces[inside[6]];
            faces[inside[6]] = shelf with { Colour = Deck };
            foreach (var e in shelf.Edges)
                foreach (var c in new[] { edges[e].A, edges[e].B })
                    corners[c] = corners[c] with { Y = DeckY };
        }
        var kb = new KitBuilder(corners, edges, faces, origin);           // Tube / Bent make solid faces, Box shaded ones
        int Corner(double x, double y, double z) => kb.Corner(x, y, z);
        void Face(int colour, (double X, double Y, double Z) inside, params int[] loop) => kb.Face(colour, inside, loop);
        void Tube(int colour, (double X, double Y, double Z) a, (double X, double Y, double Z) b, double r, int capA = -1, int capB = -1, bool round = false)
            => kb.Tube(colour, a, b, r, capA, capB, round);
        void Bent(int colour, double r, params (double X, double Y, double Z)[] p) => kb.Bent(colour, r, p);
        void Loop(int colour, double r, params (double X, double Y, double Z)[] p) => kb.Loop(colour, r, p);
        void Box(int colour, int top, int x0, int y0, int z0, int x1, int y1, int z1, bool bottom = false) => kb.Box(colour, top, x0, y0, z0, x1, y1, z1, bottom);
        static (double X, double Y, double Z) BendMiddle((double X, double Y, double Z) a, (double X, double Y, double Z) c, (double X, double Y, double Z) b) => KitBuilder.BendMiddle(a, c, b);
        // THE WHEELS OUT WIDE ON TYRES: the car's wheels are flat discs at x +-60, 2 units off its sides (seen almost edge-on
        // from behind); the racer's discs (each spin-about-x part's corners at x +-60: rim and pivot) move out to +-WheelOut, and
        // each gets a black tread band from +-TyreIn out to the disc (its 16 corners exactly the rim's, RoundWheels' rounding) and
        // a black inner wall at +-TyreIn. The bands stand still (round, so a turning band would look the same); the discs turn.
        const int WheelOut = 79, TyreIn = 67, SidewallIn = 13, DiscX = 75;   // out past the rear coil-overs' brackets (below)
        var rims = kb.WidenWheels(60, DiscX);
        // THE FINE GRID: the car's corners (seats, deck and wheels set above, in units) scaled up; the kit's from here in
        // units through Corner
        kb.FineScale();
        var tyreCorners = kb.Tyres(rims, TyreIn, WheelOut, SidewallIn);
        // THE ROLL CAGE, pipes radius 4 (an even octagon) bent as a real cage is, and a pipe joining another ends on that pipe's
        // middle line (its end then lies wholly inside it - no burr): the main hoop behind the seats (z -42) and the front hoop up the
        // windscreen's frame (from its foot, z 42, leaning back to z 20), each one bent pipe from the floor over the top and down;
        // the bottom frame, one bent pipe level just over the body's top edge from the left front post round the back to the right
        // one, its corners rounded; the roof rails join the hoops' corners, the rear struts run from the main hoop's top corners
        // down onto the bottom frame's back corners. No diagonal across the main hoop.
        // ON THE BODY'S WALLS: the frame lies along the top of the side walls and the back wall (x +-58, z -114, their top at 44:
        // the pipe's middle R above it) - the cage seen resting on the panels, clear of the seats (out to 48) and the rear deck
        // (42) - open at the front, the main hoop's feet on its side rails (their middle line at z -42). The front posts stand
        // on the dash, out of the driver's way.
        const double R = 3, Low = 44 + R, Wall = 58, Back = -114, Fore = 44;       // square tube 6 across
        // the front post's middle line across the car at height y: from its foot on the dash (x 55, y 50 - the tube inside the
        // car's sides) to the top bar (44, 86)
        static double PostX(double y) => 55 - 11 * (y - 50) / 36.0;
        static double PostZ(double y) => 42 - 34 * (y - 50) / 36.0;
        (double X, double Y, double Z)[] main = [(-Wall, Low, -42), (-46, 86, -42), (46, 86, -42), (Wall, Low, -42)];
        // each front post one straight tube from inside the dash (its foot at y 46: the slanted end, 2 either way of it, under the
        // dash's top at 50 and behind its edge from the seat), raked back to the windscreen's top bar level with the main hoop's
        // (86) at z 8 - a sloped windscreen within a unit or two of its line; the posts and the top bar one bent pipe, the bottom
        // frame another
        // the bottom frame's front ends inside the dash (z 30-44, y 44-50: the tube's 6 fits it), their caps hidden
        (double X, double Y, double Z)[] bottom = [(-Wall, Low, 40), (-Wall, Low, Back), (Wall, Low, Back), (Wall, Low, 40)];
        (double X, double Y, double Z)[] front = [(-PostX(46), 46, PostZ(46)), (-44, 86, 8), (44, 86, 8), (PostX(46), 46, PostZ(46))];
        Bent(Cage, R, bottom);                                                      // the bottom frame, open at the front
        Bent(Cage, R, front);                                                       // the posts and top bar, their feet on its front corners (in the dash)
        Bent(Cage, R, main);
        foreach (var x in new[] { -1, 1 })
        {
            int at = x < 0 ? 1 : 2;
            var mainTop = BendMiddle(main[at - 1], main[at], main[at + 1]);
            int ft = x < 0 ? 1 : 2;                                                    // the front posts' top corners
            var frontTop = BendMiddle(front[ft - 1], front[ft], front[ft + 1]);
            var backCorner = BendMiddle(bottom[at - 1], bottom[at], bottom[at + 1]);
            Tube(Cage, mainTop, frontTop, R);                                         // roof rail, hoop corner to hoop corner
            Tube(Cage, mainTop, backCorner, R);                                       // rear strut, down onto the bottom frame's back corner
        }
        // THE DASH BAR across the front inside the dash (its top 49, the dash's 50), its ends on the body's sides
        Tube(Cage, (-Wall + R, 46, 40), (Wall - R, 46, 40), R);
        // THE FIREWALL: with the windscreen off nothing closed the engine bay from the cockpit - from the eye the bonnet (its slope
        // pointing almost at the eye) showed as a hairline, and under it the engine block's lower half through the gap over the
        // dash. A panel across at the bonnet's back edge (z 44), floor to the bonnet (y 44), side to side, facing the cockpit.
        kb.FaceOrigin = Origin;
        kb.Shared = [];                                                      // its edges shared with the body's sides (below)
        Face(1, (0, 22, 80), Corner(-58, 0, 44), Corner(58, 0, 44), Corner(58, 44, 44), Corner(-58, 44, 44));
        // THE DASH: flat, in the body's green, the car's full width from z 30 to the firewall, its top level at 50 - the front
        // posts rise out of its ends, the dash bar inside it. Its back edge at z 30 puts the car's left side on the console's
        // left edge at rest (the eye 28 right of it: screen x 160 - 256 * 28 / 46 = 1); from the eye its top shows on rows
        // ~106-124, its face below down to the console
        Box(2, 2, -58, 44, 30, 58, 50, 44);
        // THE DOORS: the sides raised beside the seats, 4 thick, from the dash (level with it at the front, z 44) rising to 58
        // by z 20 and level back to the main hoop (z -42); the front posts rise out of their fronts, the side rails run in them
        // its front an upright face at z 39 (from inside the dash up to 54), just behind the front post's middle (z 42 at the
        // dash, 38 at 54) - the post, 6 across, covers it from the seat; the top slopes from there back to 58
        (double Z, double Y)[] door = [(39, 44), (39, 54), (20, 58), (-42, 58), (-42, 44)];
        foreach (var (x0, x1) in new[] { (-58, -54), (54, 58) })
        {
            var a = door.Select(p => Corner(x0, p.Y, p.Z)).ToArray();
            var b = door.Select(p => Corner(x1, p.Y, p.Z)).ToArray();
            var mid = ((x0 + x1) / 2.0, 52.0, 0.0);
            Face(2, mid, a); Face(2, mid, b);
            for (var k = 0; k < door.Length; k++)
            {
                var n = (k + 1) % door.Length;
                if (door[k].Y == 44 && door[n].Y == 44) continue;               // its bottom stands on the body's side
                Face(2, mid, a[k], a[n], b[n], b[k]);
            }
        }
        // THE SIDE WINDOWS: a dark tinted pane in each side's opening of the cage, which frames it (painted the body's green): its
        // front edge just behind the raked front post, its top under the roof rail (83), its back at the main hoop, its bottom
        // the door's top; leaning in with the cage, in the front post's middle plane (PostX: 55 at its foot to 44 at the top bar, so the
        // post and the rail hide its edges - inside them, the post showed washed through it). The door's kink at
        // z 20 makes the opening a pentagon with a notch: a triangle ahead of z 20, the rest behind
        foreach (var s in new[] { -1, 1 })
        {
            int Pt(double y, double z) => Corner(s * (PostX(y) + R), y, z);           // the front post's outer face: from the seat the post covers the edge
            kb.FaceOrigin = GlassOrigin;
            Face(Black, (0, 60, -10), Pt(51, 40), Pt(71.6, 20), Pt(58.5, 20));
            Face(Black, (0, 60, -10), Pt(71.6, 20), Pt(83, 9), Pt(83, -39), Pt(58.5, -39), Pt(58.5, 20));
            kb.FaceOrigin = Origin;
        }
        // THE WINDSCREEN, barely tinted: from inside the dash's front edge (49, its edge hidden) to under the top bar (83), its
        // sides on the posts' middle lines (their tubes cover the edges), raked back with them (one plane: z by y, 45 at the foot
        // to 11 at the top, within a unit or three of the posts' middles)
        {
            kb.FaceOrigin = GlassOrigin;
            int Wp(double x, double y) => Corner(x, y, 43 - (y - 51));
            Face(Metal, (0, 60, -10), Wp(-PostX(49), 49), Wp(PostX(49), 49), Wp(PostX(83), 83), Wp(-PostX(83), 83));
            kb.FaceOrigin = Origin;
        }
        // THE BODY WITH A NOSE: the car's box (sides x +-58 from the back to the front, its bonnet and front as wide) narrowed ahead
        // of the firewall to a nose +-Nose between the front wheels - the steered tyres' inner edges swing in to x +-58 at full lock
        // (half the tyre's width and its radius turned 28 degrees about its middle) - with a step across at the firewall. The
        // back's top is level at 44 (the car's fell to 36, under the rear deck at 42 and the cage's frame at 46); its back panel
        // (the car's face 20) and its floor plate (face 0, drawn thickened below the tyres, one corner 2 wider on the left) off.
        const int Nose = 50, Body = 2, Bonnet = 3, Front = 1, Under = 0;
        foreach (var s in new[] { -1, 1 })
        {
            Face(Body, (0, 22, 0), Corner(s * 58, 0, 44), Corner(s * 58, 44, 44), Corner(s * 58, 44, -114), Corner(s * 58, 0, -114));   // its top level (a tub: the deck at 42 inside it)
            Face(Body, (0, 22, 80), Corner(s * Nose, 0, 112), Corner(s * Nose, 36, 106), Corner(s * Nose, 44, 44), Corner(s * Nose, 0, 44));
            Face(Body, (s * 54, 22, 0), Corner(s * Nose, 0, 44), Corner(s * 58, 0, 44), Corner(s * 58, 44, 44), Corner(s * Nose, 44, 44));
        }
        Face(Bonnet, (0, 0, 80), Corner(-Nose, 36, 106), Corner(Nose, 36, 106), Corner(Nose, 44, 44), Corner(-Nose, 44, 44));
        Face(Front, (0, 22, 0), Corner(-Nose, 0, 112), Corner(Nose, 0, 112), Corner(Nose, 36, 106), Corner(-Nose, 36, 106));
        Face(Front, (0, 22, 0), Corner(-58, 0, -114), Corner(58, 0, -114), Corner(58, 44, -114), Corner(-58, 44, -114));   // the back, up to the level top
        Face(Floor, (0, 22, -30), Corner(-58, 0, -114), Corner(58, 0, -114), Corner(58, 0, 44), Corner(-58, 0, 44));   // the cockpit's floor
        Face(Under, (0, 22, 80), Corner(-Nose, 0, 44), Corner(Nose, 0, 44), Corner(Nose, 0, 112), Corner(-Nose, 0, 112));
        kb.Shared = null;
        // THE NUMBER PLATE on the back panel (z -114), low and centred: a light plate a unit proud of the panel, STUNTIN on it
        // in square letters 4 x 5 units, strokes a unit thick, a unit apart (each glyph drawn in half units, 8 x 5: a stroke is
        // "##", so the T's and I's stems sit centred), each row's run one face, a unit proud of the plate - both facing back
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
            double x0 = -w / 2; int y0 = 22;
            const int PlateColour = 8, Letters = 0;
            Face(PlateColour, (0, 20, -100), Corner(x0 - 2, y0 - 7, -115), Corner(x0 + w + 2, y0 - 7, -115), Corner(x0 + w + 2, y0 + 2, -115), Corner(x0 - 2, y0 + 2, -115));
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
                        Face(Letters, (0, 20, -100), Corner(ax, low, -116), Corner(bx, low, -116), Corner(bx, top, -116), Corner(ax, top, -116));
                    }
                at += g[0].Length / 2.0 + Gap(i);
            }
        }
        // THE ENGINE through the bonnet (its top y 44 at z 44 down to 36 at the front): a block, two cylinder heads along it, the
        // blower between them with its intake on top; FOUR STACKS A SIDE leaning out and back, round, open-topped (dark inside)
        Box(Metal, Metal, -20, 36, 54, 20, 46, 98);
        Box(Metal, Metal, -18, 46, 58, -8, 48, 94);
        Box(Metal, Metal, 8, 46, 58, 18, 48, 94);
        Box(Metal, Metal, -7, 46, 64, 7, 49, 86);
        Box(Metal, Black, -6, 49, 70, 6, 50, 80);
        foreach (var x in new[] { -1, 1 })
            foreach (var z in new[] { 58, 68, 78, 88 })
                Tube(Metal, (x * 42, 38, z), (x * 46, 50, z), 4, capA: -1, capB: Black, round: true);   // tops under the driver's eyes (52)
        // THE SUSPENSION: a coil-over to each wheel - a spring (colour 7) from a mount on the body and a grey damper rod down to the
        // hub. The front pair from the nose's side (x +-50) over the front wheels, leaning out; the rear pair hung clear of the side
        // panel (x +-58) on a grey bracket out from it by its top edge, upright, the spring's middle at 64 (3.6 off the panel). The
        // rod's lower end lies out past the spring line (66.75: the springs and brackets reach 66.4, the tyres start at 67), so it
        // rides up and down with its wheel on the card (model.gdshaderinc _racer_spring); the spring's end stays on the body: the
        // rod slides in and out as the wheel squashes
        foreach (var s in new[] { -1, 1 })
        {
            (double X, double Y, double Z) frontTop = (s * 50, 34, 80), frontMid = (s * 55.6, 23.6, 80);
            Tube(7, frontTop, frontMid, 2.4, capA: 7, capB: 7, round: true);
            Tube(Metal, frontMid, (s * 70, 8, 80), 1, round: true);
            Box(Metal, Metal, s < 0 ? -66 : 58, 38, -84, s < 0 ? -58 : 66, 43, -76, bottom: true);
            (double X, double Y, double Z) rearTop = (s * 64, 38, -80), rearMid = (s * 64, 22, -80);
            Tube(7, rearTop, rearMid, 2.4, capA: 7, capB: 7, round: true);
            Tube(Metal, rearMid, (s * 70, 8, -80), 1, round: true);
        }
        kb.FaceOrigin = Origin;
        // THE STEERING WHEEL: a small racing wheel low in front of the driver in place of CarInside's disc (its sectors and column
        // off) - a black rim (radius 5.5, round tube) on three spokes and a boss, about the car's wheel pivot moved to 54, z 18:
        // from the eye its upper half shows, the boss's button on row ~121, the rim's top on ~80). THE DRIVER'S ARMS are the
        // soldier's (model $01: an upper arm and a forearm, each a flat strip painted on both sides - the sleeve 12 / 1, the
        // forearm and hand 9 / 10), from the shoulders to hands on the rim at half past 10 and half past 1: the left forearm's hand end turns
        // with the wheel (its corners in the wheel's part, put there below); the right one's is a HAND part (kind 7,
        // model.gdshaderinc: turns with the wheel, and slides in to the red boost button on the boss as channel 3 - its way
        // there, GpuWorld.RacerParts - rises to 255, the elbow following, not steered); the left elbow and the shoulders stay.
        // A column from the dash to the boss
        var hubK = corners.FindIndex(c => c.Flag == CarInside.WheelFlag);
        corners[hubK] = corners[hubK] with { Y = 54 * Fine, Z = 18 * Fine };
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
        (double X, double Y, double Z) leftGrip = OnRim(135), rightGrip = OnRim(45);    // high enough on the rim to show over the console
        (double X, double Y, double Z) leftElbow = (-47, 45, 1), rightElbow = (-13, 45, 1), leftShoulder = (-44, 57, -15), rightShoulder = (-16, 57, -15);
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
        var elbowPivot = corners.Count;                                           // the right elbow: the same slide, not steered (axis 1)
        corners.Add(corners[handPivot] with { Flag = 7 << 4 | 1 << 2 | 3 });
        corners.Add(corners[handPivot + 1]);
        var rightElbowEnd = StripEnd(rightElbow, Way(rightElbow, rightGrip), 5.5);
        var handTo = corners.Count;
        var leftElbowEnd = StripEnd(leftElbow, Way(leftElbow, leftGrip), 5.5);
        foreach (var (hand, elbowEnd, elbow, shoulder) in new[] { (leftHand, leftElbowEnd, leftElbow, leftShoulder), (rightHand, rightElbowEnd, rightElbow, rightShoulder) })
        {
            Strip(Hand, HandBack, hand, elbowEnd);                                // the forearm and hand
            Strip(Sleeve, SleeveBack, elbowEnd, StripEnd(shoulder, Way(shoulder, elbow), 6));   // the upper arm
        }
        Tube(Metal, (hx, 45, 31), (hx, hy - 1, hz + 1), 1.2);                     // the column
        kb.FaceOrigin = Origin;
        // THE FRONT WHEELS STEER (KitBuilder.SteerFront); the steering wheel's part runs on after the car's own corners - its rim,
        // spokes and left hand - then the right hand's part
        kb.SteerFront(md.Corners.Length, TyreIn, WheelOut, tyreCorners,
            [.. Enumerable.Range(wheelFrom, wheelTo - wheelFrom), handPivot, handPivot + 1, .. Enumerable.Range(handFrom, handTo - handFrom)]);
        // BOOST FLAMES out of the stacks' tops (KitBuilder.Flames: the last parts of the model)
        kb.Flames(from x in new[] { -1, 1 } from z in new[] { 58, 68, 78, 88 } select ((x * 46.0, 50.0, (double)z), (x * 4.0, 12.0, 0.0)), 1.0);
        corners = kb.Corners; edges = kb.Edges;        md.Replace([.. corners], [.. edges], [.. faces]);
        origins[racer] = [.. origin];
        if (System.Environment.GetEnvironmentVariable("HUNTER_DUMP_RACER") is { Length: > 0 } dump)   // checks: the model's faces for a ray cast
            System.IO.File.WriteAllText(dump, System.Text.Json.JsonSerializer.Serialize(new
            {
                fine = Fine,
                corners = corners.Select(c => new[] { c.X, c.Y, c.Z }).ToArray(),
                faces = faces.Select((f, i) => new { colour = f.Colour, origin = origin[i], loop = f.Edges.Select(e => new[] { edges[e].A, edges[e].B }).ToArray() }).ToArray(),
            }));
        store.Layout();
        return racer;
    }
}
