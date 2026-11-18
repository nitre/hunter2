using System;
using System.Collections.Generic;
using System.Linq;
using Hunter.Rules;

namespace Hunter;

/// <summary>a basic interior in the card's copy of the car ($13), the
/// jeep ($22) and the cabs (truck $12, ambulance $14, scanner van $20, amphibious car $29) - two seats, a dash across under the windscreen and a steering wheel in front of the driver's seat - seen only
/// through the glass with GLASS: SEE-THROUGH (GpuWorld.Bake gives the new faces Glass.Sunk | Bit: ORIGINAL never draws them,
/// they throw no shadow and break into no piece). The wheel is a part on animation channel 2 (a plain table channel the
/// car and jeep leave at 0); place.gdshaderinc entity_artic turns it with the record's steering byte +$18. Like RoundWheels
/// the rules keep their own objfile; every other model is byte for byte as it was.</summary>
public static class CarInside
{
    /// <summary>Triangle meta bit 16: the triangle is part of this interior (with Glass.Sunk, bit 13: drawn only with SEE-THROUGH).</summary>
    public const int Bit = 65536;

    /// <summary>Which side the driver sits on: -1 left (x &lt; 0), 1 right. Not known from the original.</summary>
    public const int DriverSide = -1;

    public const int Car = 0x13, Jeep = 0x22, Truck = 0x12, Ambulance = 0x14, Van = 0x20, Amphibious = 0x29,
        Helicopter2 = 0x17, Helicopter1 = 0x19, LightAircraft = 0x1B, RocketBoat = 0x1D, Ship = 0x1E, Gunboat = 0x21, Hovercraft = 0x23;

    /// <summary>Every model with an interior and a turning wheel or stick (place.gdshaderinc steer_models, same order).</summary>
    public static readonly int[] Models = [Car, Jeep, Truck, Ambulance, Van, Amphibious, Helicopter2, Helicopter1, LightAircraft,
        RocketBoat, Ship, Gunboat, Hovercraft];

    /// <summary>Each one's turn at full steering, 256ths of a turn (place.gdshaderinc steer_turns): a third for a wheel, the
    /// aircraft stick +-7 (jobs/vehicle_interiors/aircraft.md: the most that clears every cab).</summary>
    public static int Turn(int model) => HasStick(model) ? StickLean : FullLock;

    /// <summary>What turns: a wheel (8 rim corners from the top, the plan rows), an aircraft's stick (16: column, grip), a boat's
    /// helm (8 plate corners from the right, then its hub's 4).</summary>
    public enum Control { Wheel, Stick, Helm }

    public static Control ControlOf(int model) => Kits.TryGetValue(model, out var kit) ? kit.Control : Control.Wheel;

    /// <summary>An aircraft: a stick (16 corners after the pivot: column, grip), not a wheel (8 rim corners).</summary>
    public static bool HasStick(int model) => ControlOf(model) == Control.Stick;

    const int StickLean = 7;

    /// <summary>A plain corner flag (kind 0, as the file's own 0x0F): ends a part the model's last corners belong to.</summary>
    public const int Plain = 0x0F;

    /// <summary>The wheel's pivot corner flag: kind 1 (turn), axis 2 (roll, about z - the wheel's own column), channel 2.</summary>
    public const int WheelFlag = 0x10 | 2 << 2 | 2;

    /// <summary>The wheel's turn at full lock, in 256ths of a turn: a third (place.gdshaderinc steer_turn).</summary>
    public const int FullLock = 85;

    /// <summary>Colours of the interior (palette numbers of the models): seats black, dash grey, the wheel's sectors black and grey
    /// round one yellow mark, so its turning shows.</summary>
    const int Seat = 0, Dash = 8, Mark = 7;

    /// <summary>One model's layout, model units (the file's coordinates are these shifted down: every value even), x across, y up,
    /// z forward. The seats stand at x = +-SeatX; the driver's wheel is at x = DriverSide * WheelX.</summary>
    sealed record Plan(int Floor, int SeatX, int SeatW, int CushionY, int FrontZ, int BackZ, int TopY, int TopZ,
        int DashX, int DashTop, int DashLow, int DashFront, int DashRear,
        int WheelX, int WheelY, int WheelZ, int Round, int Diagonal, int ColumnY);

    static readonly Dictionary<int, Plan> Plans = new()
    {
        [Car] = new(0, 30, 36, 12, -2, -26, 54, -32, 56, 38, 22, 40, 30, 30, 40, 14, 12, 8, 28),
        [Jeep] = new(-2, 42, 40, 40, -6, -42, 104, -50, 78, 66, 46, 36, 24, 42, 84, 6, 14, 10, 62),
        // the cabs: jobs/vehicle_interiors/cabs.md's seats, dash and hub, snapped to the model's grid, the wheel upright as the car's
        [Truck] = new(0, 60, 48, 32, 152, 104, 88, 96, 112, 56, 4, 232, 200, 60, 76, 168, 36, 24, 40),
        [Ambulance] = new(0, 60, 48, 32, 152, 104, 88, 96, 112, 56, 4, 232, 200, 60, 76, 168, 36, 24, 40),
        [Van] = new(-40, 44, 44, -8, 24, -4, 46, -10, 74, 6, -36, 108, 82, 44, 24, 56, 30, 20, 2),
        // (its wheel 4 file units from the dash, not cabs.md's 2: nearer, FindStickers takes the lower half for a sticker)
        [Amphibious] = new(228, 28, 24, 240, -16, -36, 268, -40, 44, 252, 232, 20, 12, 28, 252, -4, 16, 12, 240),
    };

    /// <summary>The kits' 21 faces: colour, then corners numbered from the kit's first (cushion, back, dash, column, grip).</summary>
    static readonly int[][] KitFaces =
    [
        [Seat, 3, 2, 6, 7], [Seat, 4, 5, 6, 7], [Seat, 0, 3, 7, 4], [Seat, 1, 2, 6, 5],
        [Seat, 12, 13, 14, 15], [Seat, 11, 10, 14, 15], [Seat, 8, 11, 15, 12], [Seat, 9, 10, 14, 13],
        [Dash, 19, 18, 22, 23], [Seat, 16, 17, 18, 19], [Dash, 16, 19, 23, 20], [Dash, 17, 18, 22, 21],
        [Dash, 25, 26, 27, 28], [Dash, 29, 30, 31, 32], [Dash, 25, 28, 32, 29], [Dash, 26, 27, 31, 30],
        [Mark, 36, 35, 39, 40], [Mark, 33, 34, 35, 36], [Mark, 37, 38, 39, 40], [Mark, 33, 36, 40, 37], [Mark, 34, 35, 39, 38],
    ];

    /// <summary>The aircraft (jobs/vehicle_interiors/aircraft.md, corners as designed): seat cushion 0-7, seat back 8-15, dash 16-23,
    /// the stick's pivot 24 (foot of the column, turns about z), column 25-32, grip 33-40 - all to the model's end, so the
    /// stick's part runs to it. KitFaces numbers these. The boats (jobs/vehicle_interiors/boats_grid.md: boats.md's design
    /// on each model's grid, margins checked again by .scratch/boats_grid.py): seat, console, gauges, helm column, then the
    /// helm's pivot and the plate and hub that turn about it.</summary>
    static readonly Dictionary<int, Kit> Kits = new()
    {
        [Helicopter2] = new(new[,] { {-52, -12, 52}, {-20, -12, 52}, {-20, 4, 52}, {-52, 4, 52}, {-52, -12, 92}, {-20, -12, 92}, {-20, 4, 92}, {-52, 4, 92}, {-52, 4, 44}, {-20, 4, 44}, {-20, 52, 44}, {-52, 52, 44}, {-52, 4, 52}, {-20, 4, 52}, {-20, 52, 52}, {-52, 52, 52}, {-60, -16, 144}, {52, -16, 144}, {52, 52, 144}, {-60, 52, 144}, {-60, -16, 172}, {52, -16, 172}, {52, 52, 172}, {-60, 52, 172}, {-36, -16, 116}, {-40, -16, 112}, {-32, -16, 112}, {-32, 16, 112}, {-40, 16, 112}, {-40, -16, 120}, {-32, -16, 120}, {-32, 16, 120}, {-40, 16, 120}, {-48, 16, 112}, {-24, 16, 112}, {-24, 24, 112}, {-48, 24, 112}, {-48, 16, 120}, {-24, 16, 120}, {-24, 24, 120}, {-48, 24, 120} }, KitFaces, KitPivot, Control.Stick),
        [Helicopter1] = new(new[,] { {-20, -36, 44}, {4, -36, 44}, {4, -24, 44}, {-20, -24, 44}, {-20, -36, 84}, {4, -36, 84}, {4, -24, 84}, {-20, -24, 84}, {-20, -36, 36}, {4, -36, 36}, {4, 16, 36}, {-20, 16, 36}, {-20, -36, 44}, {4, -36, 44}, {4, 16, 44}, {-20, 16, 44}, {-20, -32, 124}, {20, -32, 124}, {20, 24, 124}, {-20, 24, 124}, {-20, -32, 148}, {20, -32, 148}, {20, 24, 148}, {-20, 24, 148}, {-8, -40, 96}, {-12, -40, 92}, {-4, -40, 92}, {-4, -12, 92}, {-12, -12, 92}, {-12, -40, 100}, {-4, -40, 100}, {-4, -12, 100}, {-12, -12, 100}, {-16, -12, 92}, {0, -12, 92}, {0, -4, 92}, {-16, -4, 92}, {-16, -12, 100}, {0, -12, 100}, {0, -4, 100}, {-16, -4, 100} }, KitFaces, KitPivot, Control.Stick),
        [LightAircraft] = new(new[,] { {-24, -28, 20}, {-4, -28, 20}, {-4, -16, 20}, {-24, -16, 20}, {-24, -28, 56}, {-4, -28, 56}, {-4, -16, 56}, {-24, -16, 56}, {-24, -16, 12}, {-4, -16, 12}, {-4, 24, 12}, {-24, 24, 12}, {-24, -16, 20}, {-4, -16, 20}, {-4, 24, 20}, {-24, 24, 20}, {-24, -24, 124}, {24, -24, 124}, {24, 28, 124}, {-24, 28, 124}, {-24, -24, 148}, {24, -24, 148}, {24, 28, 148}, {-24, 28, 148}, {-12, -28, 80}, {-16, -28, 76}, {-8, -28, 76}, {-8, -4, 76}, {-16, -4, 76}, {-16, -28, 84}, {-8, -28, 84}, {-8, -4, 84}, {-16, -4, 84}, {-20, -4, 76}, {-4, -4, 76}, {-4, 4, 76}, {-20, 4, 76}, {-20, -4, 84}, {-4, -4, 84}, {-4, 4, 84}, {-20, 4, 84} }, KitFaces, KitPivot, Control.Stick),
        [RocketBoat] = new(new[,] { {-64, -40, -40}, {-24, -40, -40}, {-24, -40, 0}, {-64, -40, 0}, {-64, -60, -40}, {-24, -60, -40}, {-24, -60, 0}, {-64, -60, 0}, {-64, 8, -40}, {-24, 8, -40}, {-24, 8, -32}, {-64, 8, -32}, {-24, -40, -32}, {-64, -40, -32}, {-92, 0, 68}, {92, 0, 68}, {92, 0, 96}, {-92, 0, 96}, {-92, -56, 68}, {92, -56, 68}, {92, -56, 96}, {-92, -56, 96}, {-80, -32, 64}, {-64, -32, 64}, {-64, -24, 64}, {-80, -24, 64}, {-24, -32, 64}, {-8, -32, 64}, {-8, -24, 64}, {-24, -24, 64}, {-48, -16, 60}, {-40, -16, 60}, {-40, -16, 72}, {-48, -16, 72}, {-48, -24, 60}, {-48, -24, 72}, {-40, -24, 72}, {-40, -24, 60}, {-44, -24, 56}, {-28, -24, 56}, {-32, -12, 56}, {-44, -8, 56}, {-56, -12, 56}, {-60, -24, 56}, {-56, -32, 56}, {-44, -40, 56}, {-32, -32, 56}, {-48, -24, 52}, {-40, -24, 52}, {-40, -16, 52}, {-48, -16, 52} }, [[8, 0, 1, 2, 3], [0, 4, 5, 1, 0], [8, 3, 2, 6, 7], [0, 4, 0, 3, 7], [0, 6, 2, 1, 5], [0, 8, 9, 10, 11], [0, 0, 1, 9, 8], [8, 11, 10, 12, 13], [0, 0, 8, 11, 13], [0, 12, 10, 9, 1], [8, 14, 15, 16, 17], [0, 18, 19, 15, 14], [0, 17, 16, 20, 21], [0, 18, 14, 17, 21], [0, 20, 16, 15, 19], [7, 22, 23, 24, 25], [5, 26, 27, 28, 29], [8, 30, 31, 32, 33], [8, 34, 30, 33, 35], [8, 36, 32, 31, 37], [8, 35, 36, 37, 34], [9, 39, 40, 41, 42, 43, 44, 45, 46], [7, 47, 48, 49, 50]], 38, Control.Helm),
        [Ship] = new(new[,] { {-72, 24, 96}, {-32, 24, 96}, {-32, 24, 136}, {-72, 24, 136}, {-72, 8, 96}, {-32, 8, 96}, {-32, 8, 136}, {-72, 8, 136}, {-72, 64, 96}, {-32, 64, 96}, {-32, 64, 112}, {-72, 64, 112}, {-32, 24, 112}, {-72, 24, 112}, {-80, 32, 176}, {80, 32, 176}, {80, 32, 208}, {-80, 32, 208}, {-80, 8, 176}, {80, 8, 176}, {80, 8, 208}, {-80, 8, 208}, {-16, 16, 168}, {0, 16, 168}, {0, 24, 168}, {-16, 24, 168}, {16, 16, 168}, {32, 16, 168}, {32, 24, 168}, {16, 24, 168}, {-56, 32, 168}, {-40, 32, 168}, {-40, 32, 184}, {-56, 32, 184}, {-56, 16, 168}, {-56, 16, 184}, {-40, 16, 184}, {-40, 16, 168}, {-48, 24, 160}, {-32, 24, 160}, {-40, 40, 160}, {-48, 40, 160}, {-64, 40, 160}, {-64, 24, 160}, {-64, 8, 160}, {-48, 8, 160}, {-40, 8, 160}, {-56, 16, 152}, {-48, 16, 152}, {-48, 32, 152}, {-56, 32, 152} }, [[8, 0, 1, 2, 3], [0, 4, 5, 1, 0], [8, 3, 2, 6, 7], [0, 4, 0, 3, 7], [0, 6, 2, 1, 5], [0, 8, 9, 10, 11], [0, 0, 1, 9, 8], [8, 11, 10, 12, 13], [0, 0, 8, 11, 13], [0, 12, 10, 9, 1], [8, 14, 15, 16, 17], [0, 18, 19, 15, 14], [0, 17, 16, 20, 21], [0, 18, 14, 17, 21], [0, 20, 16, 15, 19], [7, 22, 23, 24, 25], [5, 26, 27, 28, 29], [8, 30, 31, 32, 33], [8, 34, 30, 33, 35], [8, 36, 32, 31, 37], [8, 35, 36, 37, 34], [9, 39, 40, 41, 42, 43, 44, 45, 46], [7, 47, 48, 49, 50]], 38, Control.Helm),
        [Gunboat] = new(new[,] { {-40, 8, -48}, {-8, 8, -48}, {-8, 8, -20}, {-40, 8, -20}, {-40, 4, -48}, {-8, 4, -48}, {-8, 4, -20}, {-40, 4, -20}, {-40, 32, -48}, {-8, 32, -48}, {-8, 32, -44}, {-40, 32, -44}, {-8, 8, -44}, {-40, 8, -44}, {-40, 24, 56}, {40, 24, 56}, {40, 24, 80}, {-40, 24, 80}, {-40, 4, 56}, {40, 4, 56}, {40, 4, 80}, {-40, 4, 80}, {-8, 12, 48}, {0, 12, 48}, {0, 16, 48}, {-8, 16, 48}, {8, 12, 48}, {16, 12, 48}, {16, 16, 48}, {8, 16, 48}, {-28, 20, 48}, {-20, 20, 48}, {-20, 20, 56}, {-28, 20, 56}, {-28, 12, 48}, {-28, 12, 56}, {-20, 12, 56}, {-20, 12, 48}, {-24, 16, 44}, {-12, 16, 44}, {-16, 24, 44}, {-24, 28, 44}, {-32, 24, 44}, {-36, 16, 44}, {-32, 8, 44}, {-24, 4, 44}, {-16, 8, 44}, {-28, 12, 40}, {-20, 12, 40}, {-20, 20, 40}, {-28, 20, 40} }, [[8, 0, 1, 2, 3], [0, 4, 5, 1, 0], [8, 3, 2, 6, 7], [0, 4, 0, 3, 7], [0, 6, 2, 1, 5], [0, 8, 9, 10, 11], [0, 0, 1, 9, 8], [8, 11, 10, 12, 13], [0, 0, 8, 11, 13], [0, 12, 10, 9, 1], [8, 14, 15, 16, 17], [0, 18, 19, 15, 14], [0, 17, 16, 20, 21], [0, 18, 14, 17, 21], [0, 20, 16, 15, 19], [7, 22, 23, 24, 25], [5, 26, 27, 28, 29], [8, 30, 31, 32, 33], [8, 34, 30, 33, 35], [8, 36, 32, 31, 37], [8, 35, 36, 37, 34], [9, 39, 40, 41, 42, 43, 44, 45, 46], [7, 47, 48, 49, 50]], 38, Control.Helm),
        [Hovercraft] = new(new[,] { {-72, 80, -56}, {-32, 80, -56}, {-32, 80, -20}, {-72, 80, -20}, {-72, 68, -56}, {-32, 68, -56}, {-32, 68, -20}, {-72, 68, -20}, {-72, 108, -56}, {-32, 108, -56}, {-32, 108, -48}, {-72, 108, -48}, {-32, 80, -48}, {-72, 80, -48}, {-88, 92, 80}, {80, 92, 80}, {80, 92, 100}, {-88, 92, 100}, {-88, 68, 80}, {80, 68, 80}, {80, 68, 100}, {-88, 68, 100}, {-24, 76, 80}, {-8, 76, 80}, {-8, 84, 80}, {-24, 84, 80}, {8, 76, 80}, {20, 76, 80}, {20, 84, 80}, {8, 84, 80}, {-56, 84, 72}, {-48, 84, 72}, {-48, 84, 88}, {-56, 84, 88}, {-56, 76, 72}, {-56, 76, 88}, {-48, 76, 88}, {-48, 76, 72}, {-48, 80, 72}, {-40, 80, 72}, {-40, 88, 72}, {-48, 92, 72}, {-56, 88, 72}, {-64, 80, 72}, {-56, 72, 72}, {-48, 68, 72}, {-40, 72, 72}, {-56, 76, 64}, {-48, 76, 64}, {-48, 84, 64}, {-56, 84, 64} }, [[8, 0, 1, 2, 3], [0, 4, 5, 1, 0], [8, 3, 2, 6, 7], [0, 4, 0, 3, 7], [0, 6, 2, 1, 5], [0, 8, 9, 10, 11], [0, 0, 1, 9, 8], [8, 11, 10, 12, 13], [0, 0, 8, 11, 13], [0, 12, 10, 9, 1], [8, 14, 15, 16, 17], [0, 18, 19, 15, 14], [0, 17, 16, 20, 21], [0, 18, 14, 17, 21], [0, 20, 16, 15, 19], [7, 22, 23, 24, 25], [5, 26, 27, 28, 29], [8, 30, 31, 32, 33], [8, 34, 30, 33, 35], [8, 36, 32, 31, 37], [8, 35, 36, 37, 34], [9, 39, 40, 41, 42, 43, 44, 45, 46], [7, 47, 48, 49, 50]], 38, Control.Helm),
    };

    /// <summary>Designed corners, faces (colour, then corners numbered from the kit's first), the pivot's place among them
    /// (everything after it turns), what it is.</summary>
    sealed record Kit(int[,] Corners, int[][] Faces, int Pivot, Control Control);

    const int KitPivot = 24;

    /// <summary>Triangle meta bit 17: a driver's face (jobs/car_driver/DRIVER_DESIGN.md) - drawn only while someone is in (d7 bit 28
    /// clear); bits 18-21 the colour of the red look (variant 1, the computer's drivers). GpuWorld.Bake takes it from the face's
    /// origin -(2 + red colour).</summary>
    public const int Driver = 0x20000;

    /// <summary>The seated driver of the car and jeep: 11 boxes of 8 corners (thigh L, R, shin L, R, foot L, R, torso, head, cap,
    /// arm L, R; corner k = x + 2y + 4z ends), scaled to each cab. Placed before the wheel so it does not turn with it.</summary>
    static readonly Dictionary<int, int[,]> Figures = new()
    {
        [Car] = new[,] { {-40, 14, -24}, {-32, 14, -24}, {-40, 22, -24}, {-32, 22, -24}, {-40, 20, 6}, {-32, 20, 6}, {-40, 28, 6}, {-32, 28, 6}, {-28, 14, -24}, {-20, 14, -24}, {-28, 22, -24}, {-20, 22, -24}, {-28, 20, 6}, {-20, 20, 6}, {-28, 28, 6}, {-20, 28, 6}, {-40, 2, 0}, {-32, 2, 0}, {-40, 20, 0}, {-32, 20, 0}, {-40, 2, 6}, {-32, 2, 6}, {-40, 20, 6}, {-32, 20, 6}, {-28, 2, 0}, {-20, 2, 0}, {-28, 20, 0}, {-20, 20, 0}, {-28, 2, 6}, {-20, 2, 6}, {-28, 20, 6}, {-20, 20, 6}, {-40, 2, 6}, {-32, 2, 6}, {-40, 8, 6}, {-32, 8, 6}, {-40, 2, 14}, {-32, 2, 14}, {-40, 8, 14}, {-32, 8, 14}, {-28, 2, 6}, {-20, 2, 6}, {-28, 8, 6}, {-20, 8, 6}, {-28, 2, 14}, {-20, 2, 14}, {-28, 8, 14}, {-20, 8, 14}, {-42, 22, -24}, {-18, 22, -24}, {-42, 46, -24}, {-18, 46, -24}, {-42, 22, -10}, {-18, 22, -10}, {-42, 46, -10}, {-18, 46, -10}, {-36, 46, -22}, {-24, 46, -22}, {-36, 56, -22}, {-24, 56, -22}, {-36, 46, -10}, {-24, 46, -10}, {-36, 56, -10}, {-24, 56, -10}, {-36, 56, -22}, {-24, 56, -22}, {-36, 62, -22}, {-24, 62, -22}, {-36, 56, -10}, {-24, 56, -10}, {-36, 62, -10}, {-24, 62, -10}, {-48, 40, -22}, {-42, 40, -22}, {-48, 46, -22}, {-42, 46, -22}, {-46, 38, 12}, {-40, 38, 12}, {-46, 44, 12}, {-40, 44, 12}, {-18, 40, -22}, {-12, 40, -22}, {-18, 46, -22}, {-12, 46, -22}, {-20, 38, 12}, {-14, 38, 12}, {-20, 44, 12}, {-14, 44, 12} },
        [Jeep] = new[,] { {-56, 42, -38}, {-46, 42, -38}, {-56, 52, -38}, {-46, 52, -38}, {-56, 48, 4}, {-46, 48, 4}, {-56, 58, 4}, {-46, 58, 4}, {-38, 42, -38}, {-28, 42, -38}, {-38, 52, -38}, {-28, 52, -38}, {-38, 48, 4}, {-28, 48, 4}, {-38, 58, 4}, {-28, 58, 4}, {-56, 0, -2}, {-46, 0, -2}, {-56, 48, -2}, {-46, 48, -2}, {-56, 0, 4}, {-46, 0, 4}, {-56, 48, 4}, {-46, 48, 4}, {-38, 0, -2}, {-28, 0, -2}, {-38, 48, -2}, {-28, 48, -2}, {-38, 0, 4}, {-28, 0, 4}, {-38, 48, 4}, {-28, 48, 4}, {-56, 0, 4}, {-46, 0, 4}, {-56, 6, 4}, {-46, 6, 4}, {-56, 0, 16}, {-46, 0, 16}, {-56, 6, 16}, {-46, 6, 16}, {-38, 0, 4}, {-28, 0, 4}, {-38, 6, 4}, {-28, 6, 4}, {-38, 0, 16}, {-28, 0, 16}, {-38, 6, 16}, {-28, 6, 16}, {-56, 52, -40}, {-28, 52, -40}, {-56, 84, -40}, {-28, 84, -40}, {-56, 52, -22}, {-28, 52, -22}, {-56, 84, -22}, {-28, 84, -22}, {-50, 84, -38}, {-34, 84, -38}, {-50, 100, -38}, {-34, 100, -38}, {-50, 84, -24}, {-34, 84, -24}, {-50, 100, -24}, {-34, 100, -24}, {-50, 100, -38}, {-34, 100, -38}, {-50, 106, -38}, {-34, 106, -38}, {-50, 100, -24}, {-34, 100, -24}, {-50, 106, -24}, {-34, 106, -24}, {-62, 76, -36}, {-56, 76, -36}, {-62, 82, -36}, {-56, 82, -36}, {-60, 80, 4}, {-54, 80, 4}, {-60, 86, 4}, {-54, 86, 4}, {-28, 76, -36}, {-22, 76, -36}, {-28, 82, -36}, {-22, 82, -36}, {-30, 80, 4}, {-24, 80, 4}, {-30, 86, 4}, {-24, 86, 4} },
    };

    /// <summary>The figure's faces: box, side (-x, +x, -y, +y, -z, +z = 0-5), colour green (you), colour red (variant 1).</summary>
    static readonly (int Box, int Side, int Green, int Red)[] FigureFaces =
    [
        (0, 0, 3, 3), (0, 1, 12, 12), (0, 3, 13, 13), (0, 2, 14, 14), (0, 5, 2, 2), (0, 4, 13, 13),
        (1, 0, 12, 12), (1, 1, 1, 1), (1, 3, 13, 13), (1, 2, 14, 14), (1, 5, 14, 14), (1, 4, 13, 13),
        (2, 0, 2, 2), (2, 1, 12, 12), (2, 5, 13, 13), (2, 4, 14, 14),
        (3, 0, 12, 12), (3, 1, 12, 12), (3, 5, 14, 14), (3, 4, 13, 13),
        (4, 0, 0, 0), (4, 1, 0, 0), (4, 3, 0, 0), (4, 2, 0, 0), (4, 5, 0, 0),
        (5, 0, 0, 0), (5, 1, 0, 0), (5, 3, 0, 0), (5, 2, 0, 0), (5, 5, 0, 0),
        (6, 0, 1, 0), (6, 1, 12, 0), (6, 3, 2, 5), (6, 2, 14, 5), (6, 5, 13, 6), (6, 4, 13, 6),
        (7, 0, 0, 0), (7, 1, 0, 0), (7, 5, 11, 11), (7, 4, 0, 0),
        (8, 0, 12, 13), (8, 1, 12, 13), (8, 3, 13, 3), (8, 4, 1, 1), (8, 5, 1, 2),
        (9, 0, 2, 5), (9, 3, 2, 5), (9, 2, 2, 5), (9, 4, 2, 5), (9, 5, 10, 6),
        (10, 1, 1, 5), (10, 3, 1, 5), (10, 2, 1, 5), (10, 4, 1, 5), (10, 5, 10, 6),
    ];

    /// <summary>A box's side as a loop of its corners k (outward as the car's own faces).</summary>
    static readonly int[][] BoxSides = [[4, 6, 2, 0], [1, 3, 7, 5], [0, 1, 5, 4], [6, 7, 3, 2], [2, 3, 1, 0], [4, 5, 7, 6]];


    /// <summary>The card's models (CardModels) with the interiors added in place, then laid out again. A model it cannot rebuild cleanly
    /// stays as it was (named in notes). origins (RoundWheels') gets -1 for each new face: no face of the file.</summary>
    public static void Make(CardModels store, List<string>? notes = null, Dictionary<int, int[]>? origins = null)
    {
        foreach (var n in Models)
        {
            if (n >= store.Models.Length) continue;
            try { Build(store.Models[n], n, Plans.GetValueOrDefault(n), Kits.GetValueOrDefault(n), notes, origins); }
            catch (Exception e) when (e is InvalidOperationException or IndexOutOfRangeException)
            {
                notes?.Add($"model {n} left as it was: {e.Message}");
            }
        }
        store.Layout();
    }

    static void Build(CardModels.Model md, int model, Plan? p, Kit? kit, List<string>? notes, Dictionary<int, int[]>? origins)
    {
        int shift = md.Shift, nv = md.Corners.Length, ne = md.Edges.Length, nf = md.Faces.Length;
        var last = 0;                                            // the walk's rule at the end: a part would carry the new corners with it
        for (var k = nv - 1; k >= 0 && last == 0; k--) last = md.Corners[k].Flag;
        var first = ((last >> 4) & 7) is 1 or 2 ? Plain : 0;     // so the first new corner ends it

        var corners = new List<CardModels.Corner>();
        var edges = new List<(int A, int B)>();
        var faces = new List<(int Colour, int[] E, int Origin)>();   // origin: -1 the interior, -(2 + red) a driver's face
        var edgeId = new Dictionary<(int, int), int>();
        int Corner(int flag, int x, int y, int z)
        {
            var unit = 1 << shift;
            if (x % unit != 0 || y % unit != 0 || z % unit != 0) throw new InvalidOperationException($"corner {x},{y},{z} is not a multiple of {unit}");
            if (Math.Abs(x) > 32767 || Math.Abs(y) > 32767 || Math.Abs(z) > 32767) throw new InvalidOperationException($"corner {x},{y},{z} out of range (16 bits)");
            corners.Add(new(flag, x, y, z));
            return nv + corners.Count - 1;
        }
        int Edge(int a, int c)
        {
            var key = (Math.Min(a, c), Math.Max(a, c));
            if (!edgeId.TryGetValue(key, out var id)) { edgeId[key] = id = ne + edges.Count; edges.Add((a, c)); }
            return id;
        }
        void Face(int colour, params int[] loop) => FaceFrom(-1, colour, loop);
        void FaceFrom(int origin, int colour, int[] loop) => faces.Add((colour, Enumerable.Range(0, loop.Length).Select(i => Edge(loop[i], loop[(i + 1) % loop.Length])).ToArray(), origin));

        if (kit is not null)                                     // an aircraft: the designed corners, the stick's pivot flagged
        {
            for (var k = 0; k < kit.Corners.GetLength(0); k++) Corner(k == 0 ? first : k == kit.Pivot ? WheelFlag : 0, kit.Corners[k, 0], kit.Corners[k, 1], kit.Corners[k, 2]);
            foreach (var f in kit.Faces) Face(f[0], f.Skip(1).Select(c => nv + c).ToArray());
        }
        else Seats(p!);
        void Seats(Plan p)
        {
            foreach (var side in new[] { -1, 1 })                    // a seat: cushion, back, the cushion's front
            {
                var (x0, x1) = (side * p.SeatX - p.SeatW / 2, side * p.SeatX + p.SeatW / 2);
                var fl = Corner(side < 0 ? first : 0, x0, p.CushionY, p.FrontZ); var fr = Corner(0, x1, p.CushionY, p.FrontZ);
                var br = Corner(0, x1, p.CushionY, p.BackZ); var bl = Corner(0, x0, p.CushionY, p.BackZ);
                var tl = Corner(0, x0, p.TopY, p.TopZ); var tr = Corner(0, x1, p.TopY, p.TopZ);
                var dl = Corner(0, x0, p.Floor, p.FrontZ); var dr = Corner(0, x1, p.Floor, p.FrontZ);
                Face(Seat, fl, fr, br, bl);
                Face(Seat, bl, br, tr, tl);
                Face(Seat, fl, dl, dr, fr);
            }
            {                                                        // the dash: its top and the face to the driver
                var tfl = Corner(0, -p.DashX, p.DashTop, p.DashFront); var tfr = Corner(0, p.DashX, p.DashTop, p.DashFront);
                var trr = Corner(0, p.DashX, p.DashTop, p.DashRear); var trl = Corner(0, -p.DashX, p.DashTop, p.DashRear);
                var brl = Corner(0, -p.DashX, p.DashLow, p.DashRear); var brr = Corner(0, p.DashX, p.DashLow, p.DashRear);
                Face(Dash, tfl, tfr, trr, trl);
                Face(Dash, trl, trr, brr, brl);
            }
            if (Figures.TryGetValue(model, out var fig))             // the driver, before the wheel's part
            {
                var at = Enumerable.Range(0, fig.GetLength(0)).Select(k => Corner(0, fig[k, 0], fig[k, 1], fig[k, 2])).ToArray();
                foreach (var (box, side, green, red) in FigureFaces) FaceFrom(-(2 + red), green, BoxSides[side].Select(k => at[box * 8 + k]).ToArray());
            }
            var wx = DriverSide * p.WheelX;
            var ca = Corner(0, wx - 4, p.ColumnY, p.DashRear);       // the column: a triangle from the dash to the wheel's hub
            var cb = Corner(0, wx + 4, p.ColumnY, p.DashRear);
            var hub = Corner(WheelFlag, wx, p.WheelY, p.WheelZ);     // the wheel: the pivot, then 8 corners turning about it, clockwise from the top
            var (r, d) = (p.Round, p.Diagonal);
            int[,] ring = { { 0, r }, { d, d }, { r, 0 }, { d, -d }, { 0, -r }, { -d, -d }, { -r, 0 }, { -d, d } };
            var rim = Enumerable.Range(0, 8).Select(i => Corner(0, wx + ring[i, 0], p.WheelY + ring[i, 1], p.WheelZ)).ToArray();
            Face(Dash, ca, cb, hub);
            for (var i = 0; i < 8; i++) Face(i == 0 ? Mark : i % 2 == 1 ? Dash : Seat, hub, rim[i], rim[(i + 1) % 8]);
        }

        md.Replace([.. md.Corners, .. corners], [.. md.Edges, .. edges.Select(e => new CardModels.Edge(e.A, e.B))],
            [.. md.Faces, .. faces.Select(f => new CardModels.Face(f.Colour, 0, f.E))]);
        if (origins is not null)
            origins[model] = (origins.TryGetValue(model, out var old) ? old : Enumerable.Range(0, nf).ToArray()).Concat(faces.Select(f => f.Origin)).ToArray();
        notes?.Add($"model {model}: {nv} -> {nv + corners.Count} corners, {ne} -> {ne + edges.Count} edges, {nf} -> {nf + faces.Count} faces");
    }
}
