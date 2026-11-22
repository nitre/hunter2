using System.Collections.Generic;

namespace Hunter;

/// <summary>The glass faces of the objfile's models (docs/data/windows.md): model number -> the face numbers, counted
/// as the FILLED faces in file order (kind 0 or 0x40, 3 or more edges) - the numbering of models.json "f". The game marks
/// no face as a window; these were picked by eye. GpuWorld.Bake sets triangle meta bit 12 on them.</summary>
public static class Glass
{
    /// <summary>Triangle meta bit 12: the triangle is part of a glass face (the face and its slab copy, not the slab's sides).</summary>
    public const int Bit = 4096;

    /// <summary>Triangle meta bit 13: drawn only with GLASS: SEE-THROUGH - the sunk pane (with bit 12) and the frame walls.</summary>
    public const int Sunk = 8192;

    /// <summary>Triangle meta bit 14: a glass face at rest that has a sunk copy - with SEE-THROUGH the glass pass skips it (a glass
    /// face without a copy - a slab, twin or sticker - is drawn there as it was).</summary>
    public const int Replaced = 16384;

    public static readonly Dictionary<int, int[]> Faces = new()
    {
        [0x12] = [20],                              // truck
        [0x13] = [6, 18, 19, 21],                   // car
        [0x14] = [26],                              // ambulance
        // vehicles: the faces the file paints black or grey - its own windows - and the light aircraft's cockpit sides
        [0x17] = [13],                              // helicopter
        [0x19] = [20, 21, 24],                      // helicopter: the nose pane and its two side windows (the pilot sits behind them)
        [0x1B] = [4, 13, 14, 15],                   // light aircraft: its cockpit side windows too (14, 15)
        [0x1D] = [12],                              // rocket boat
        [0x1E] = [5],                               // ship
        [0x20] = [19, 21],                          // scanner van
        [0x21] = [1],                               // gunboat
        [0x22] = [6, 20],                           // jeep
        [0x23] = [1, 13, 14],                       // hovercraft
                                                    // amphibious car ($29): no face painted as a window
        [0x98] = [5],                               // out post
        [0x99] = [4],                               // shed
        [0x9A] = [1, 3],                            // hospital
        [0x9B] = [1, 3],                            // office
        [0x9E] = [2],                               // bungalow
        [0x9F] = [11],                              // coms hut
        [0xA0] = [1, 4, 7],                         // phone box
        [0xA1] = [2, 3],                            // light house
        [0xA3] = [10],                              // enemy fort
        [0xA6] = [2],                               // prison
        [0xA7] = [7, 8],                            // control tower
        [0xA9] = [4],                               // garage
                                                    // Allied HQ ($AA): its black panels are its flag and the letters HQ - a sign, no window
    };

    /// <summary>Is the filled face with this number (0 up, as models.json) of this model glass?</summary>
    public static bool Is(int model, int filledNumber) => Faces.TryGetValue(model, out var f) && System.Array.IndexOf(f, filledNumber) >= 0;
}
