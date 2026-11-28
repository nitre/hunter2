using System.Collections.Generic;
using System.Linq;

namespace Hunter;

/// <summary>STUNTIN': the jeep racer - Hunter's jeep ($22) as the card draws it (round wheels, seats, dash, steering wheel and
/// driver: CarInside) with a racing kit, lifted: a model of its own after the car racer (GpuWorld.JeepRacerModel; the Stuntin'
/// jeeps draw it through GpuWorld.ExtraModel - the rules keep $22). In the jeep's full units (x across, y up, z forward; its body
/// x +-80 from y -2 to its top edge, 62 at the ends and 70 at the cowl, z 40; the bonnet from the cowl to the front, z 140; its
/// wheels discs at x +-80, radius 32, about y 0 at z 90 and -80): the canvas top and its windscreen off; the body and all in it
/// LIFTED over the wheels on long coil-overs; the wheels out wide on black tyres, the front pair steered; seats racing red; a
/// roll cage in the body's green; an engine through the bonnet with a blower and four stacks a side.</summary>
public static class JeepKit
{
    /// <summary>The jeep's faces the racer leaves off (the objfile's face numbers): the windscreen (6), the canvas top's sides,
    /// back and roof (18, 19, 20, 22) and the skid plate under it (1: lifted, it hung in the air under the body).</summary>
    static readonly int[] Off = [6, 18, 19, 20, 22, 1];
    /// <summary>How far the body rides up over its wheels (units).</summary>
    public const int Lift = 16;
    /// <summary>The stacks' open tops (units, as drawn) and their way out - where the boost flames come from (GameView.FlameStep).</summary>
    public static readonly ((double X, double Y, double Z) Tip, (double X, double Y, double Z) Axis)[] Stacks =
        [.. from s in new[] { -1, 1 } from z in new[] { 70, 84, 98, 112 } select ((s * 62.0, 96.0, (double)z), (s * 6.0, 16.0, 0.0))];
    /// <summary>The tyres: the tread from +-TyreIn to +-WheelOut, the disc at +-DiscX, the outer sidewall's ring from SidewallIn.</summary>
    const int TyreIn = 90, WheelOut = 122, SidewallIn = 30, DiscX = 118;
    /// <summary>MONSTER WHEELS, more so than the car racer's (RacingKit.WheelGrow 1.6): the jeep's (radius 32) this much bigger -
    /// about 56, the tyres 32 wide out from the body's side; the card stands it on its tyres, so it rides that much higher.</summary>
    const double WheelGrow = 1.75;
    /// <summary>The wheels' radius (units): the jeep's 32 grown by WheelGrow.</summary>
    public const double WheelR = 32 * WheelGrow;
    /// <summary>THE PHYSICS' FOOTPRINT: the jeep drawn this much smaller (model.gdshaderinc JEEP_SIZE) - Hunter's jeep is twice
    /// the physics car's width; at 0.62 it is the car racer's width - and its wheels moved out along it to the physics car's
    /// axles (the car racer's +-96 drawn: AxleZ here), at its corners past the shortened body's ends, as a monster truck's.</summary>
    public const double Size = 0.62;
    public const int AxleZ = 155;
    /// <summary>The corners riding on the wheels' springs: out past this (units; model.gdshaderinc RACER2_WHEEL_X = this x Fine) -
    /// the tyres, discs and the dampers' lower ends; the body (80), the cage, the brackets and the springs (to 88.9) stay inside it.</summary>
    public const double WheelSide = 89.25;
    const int Cage = 2, Metal = 8, Black = 0, SeatRed = 5, Spring = 7;
    const int Fine = RacingKit.Fine;

    /// <summary>Appends the jeep racer to the store (after the jeep's interior is built) and returns its number.</summary>
    public static int Make(CardModels store, Dictionary<int, int[]> origins)
    {
        var racer = store.AppendClone(CarInside.Jeep);
        var md = store.Models[racer];
        var from = origins.TryGetValue(CarInside.Jeep, out var o) ? o : Enumerable.Range(0, md.Faces.Length).ToArray();
        var keep = Enumerable.Range(0, md.Faces.Length).Where(i => System.Array.IndexOf(Off, from[i]) < 0).ToArray();
        var corners = md.Corners.ToList();
        var edges = md.Edges.ToList();
        var faces = keep.Select(i => md.Faces[i]).ToList();
        var origin = keep.Select(i => from[i]).ToList();
        // THE SEATS racing red: CarInside's first six faces (origin -1), two seats of three
        var inside = Enumerable.Range(0, faces.Count).Where(i => origin[i] == -1).ToArray();
        for (var k = 0; k < 6 && k < inside.Length; k++) faces[inside[k]] = faces[inside[k]] with { Colour = SeatRed };
        // THE LIFT: every corner but the wheels' (the spin-about-x parts: their pivots and discs) up by Lift - the body, its
        // inside, the driver and his steering wheel
        {
            var spin = false;
            for (var k = 0; k < corners.Count; k++)
            {
                var c = corners[k];
                if (c.Flag != 0) spin = ((c.Flag >> 4) & 7) == 1 && ((c.Flag >> 2) & 3) == 0;
                if (!spin) corners[k] = c with { Y = c.Y + Lift };
            }
        }
        var kb = new KitBuilder(corners, edges, faces, origin);
        kb.MoveWheels(80 + RoundWheels.Proud, AxleZ);
        var rims = kb.ScaleWheels(kb.WidenWheels(80 + RoundWheels.Proud, DiscX), DiscX, WheelGrow);   // the jeep's rims stand out by Proud (RoundWheels.ProudModels)
        kb.FineScale();
        var tyreCorners = kb.Tyres(rims, TyreIn, WheelOut, SidewallIn);
        // THE ROLL CAGE, square tube 6 across in the body's green, its feet on the body's top edge (x +-77: the tube inside the
        // skin): the main hoop behind the seats' backs (z -56, their top 120 at z -50), the front hoop up from the cowl (z 38)
        // raked back to its top bar at z 20; both tops at 136, over the driver's head (122); roof rails between the hoops'
        // corners, rear struts from the main hoop's top corners down onto the body's back corners, a bar along each side's top
        // edge from hoop to hoop and one across the back
        const double R = 3, X = 77, Top = 136;
        (double X, double Y, double Z)[] main = [(-X, 85, -56), (-66, Top, -56), (66, Top, -56), (X, 85, -56)];
        (double X, double Y, double Z)[] front = [(-X, 89, 38), (-66, Top, 20), (66, Top, 20), (X, 89, 38)];
        kb.Bent(Cage, R, main);
        kb.Bent(Cage, R, front);
        foreach (var s in new[] { -1, 1 })
        {
            int at = s < 0 ? 1 : 2;
            var mainTop = KitBuilder.BendMiddle(main[at - 1], main[at], main[at + 1]);
            var frontTop = KitBuilder.BendMiddle(front[at - 1], front[at], front[at + 1]);
            kb.Tube(Cage, mainTop, frontTop, R);                                 // roof rail
            kb.Tube(Cage, mainTop, (s * X, 78 + R, -136), R);                     // rear strut onto the back corner
            kb.Tube(Cage, (s * X, 89, 38), (s * X, 85, -56), R);                  // side bar, hoop foot to hoop foot
        }
        kb.Tube(Cage, (-X, 78 + R, -136), (X, 78 + R, -136), R);                  // the back bar
        // THE ENGINE through the bonnet (its top 86 at the cowl down to 78 at the front): a block, two cylinder heads along it,
        // the blower between them with its intake on top; FOUR STACKS A SIDE leaning out and back, round, open-topped
        kb.Box(Metal, Metal, -26, 76, 60, 26, 92, 124);
        kb.Box(Metal, Metal, -24, 92, 64, -10, 95, 120);
        kb.Box(Metal, Metal, 10, 92, 64, 24, 95, 120);
        kb.Box(Metal, Metal, -9, 92, 72, 9, 97, 108);
        kb.Box(Metal, Black, -8, 97, 80, 8, 99, 98);
        foreach (var s in new[] { -1, 1 })
            foreach (var z in new[] { 70, 84, 98, 112 })
                kb.Tube(Metal, (s * 56, 80, z), (s * 62, 96, z), 4.5, capA: -1, capB: Black, round: true);
        // THE SUSPENSION, lifted: a long coil-over to each wheel, hung clear of the body's side on a bracket - a grey bracket out
        // from the side by its top edge, a spring (colour 7) from it down to a grey damper rod, the rod on to the hub. The spring
        // stands 3.6 off the side (its middle at 86, radius 2.4) and 1.1 inside the tyre's inner wall. The rod's lower end lies out
        // past WheelSide, so it rides up and down with its wheel on the card (model.gdshaderinc _racer_spring); the spring stays
        // on the body: the rod slides in and out as the wheel squashes
        // the wheels past the body's ends (AxleZ): each bracket on the body's end corner (z +-ShockZ), the coil-over leaning out
        // along the car from it down to its hub
        const int ShockZ = 126;
        foreach (var s in new[] { -1, 1 })
            foreach (var e in new[] { 1, -1 })
            {
                double zt = e * ShockZ, zl = e * AxleZ;
                kb.Box(Metal, Metal, s < 0 ? -88 : 80, 74, (int)zt - 4, s < 0 ? -80 : 88, 79, (int)zt + 4, bottom: true);
                (double X, double Y, double Z) top = (s * 86, 74, zt), low = (s * 91, 6, zl);
                var mid = (X: top.X + (low.X - top.X) * 0.44, Y: top.Y + (low.Y - top.Y) * 0.44, Z: top.Z + (low.Z - top.Z) * 0.44);
                kb.Tube(Spring, top, mid, 2.4, capA: Spring, capB: Spring, round: true);
                kb.Tube(Metal, mid, low, 1, round: true);
            }
        kb.FaceOrigin = RacingKit.Origin;
        // THE FRONT WHEELS STEER (KitBuilder.SteerFront); CarInside's steering wheel stays the jeep's own part
        kb.SteerFront(md.Corners.Length, TyreIn, WheelOut, tyreCorners, []);
        md.Replace([.. kb.Corners], [.. kb.Edges], [.. faces]);
        origins[racer] = [.. origin];
        store.Layout();
        return racer;
    }
}
