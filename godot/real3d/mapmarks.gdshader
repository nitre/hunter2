shader_type canvas_item;

// ENHANCEMENT (renderer = 3d): the overlay map (the marks switched on over the view,
// rules/Screens.cs LogBook.DrawMarks $3FFE) drawn by the card (godot/real3d/MapMarksGpu.cs) over its 3D picture -
// the processor paints no view while the card draws (4c285a0). DrawMarks' order: the log book's bitmap where its
// mask is set, then the player's pixel in colour 4, then each grave not taken a five-pixel plus in colour 6, clipped
// to the 128 x 128 block. The player's row can be 128 (z < 512), one past the block - the original writes it there
// too, so the node is 129 rows. Coloured like the rest of the view (world_colour view_nib), colour 15 as the sky of its
// game row (Play.ScreenOf's lut[15]: the band, or SKY: FINE GRADIENT's row colour); elsewhere transparent.

#include "res://real3d/world_colour.gdshaderinc"

uniform sampler2D marks_tex : filter_nearest;    // 128 x 128 Rf: the mark's colour index + 1, 0 where the mask is clear
uniform ivec2 player_at = ivec2(-1, -1);         // the player's pixel (x >> 9, 128 - (z >> 9)), $400C / $4020
uniform sampler2D graves_tex : filter_nearest;   // (x, y, taken) a texel, grave_count of them
uniform int grave_count = 0;

void fragment() {
    ivec2 p = clamp(ivec2(UV * vec2(128.0, 129.0)), ivec2(0), ivec2(127, 128));
    int idx = -1;
    if (p.y < 128) idx = int(texelFetch(marks_tex, p, 0).r + 0.5) - 1;
    if (p == player_at) idx = 4;
    for (int n = 0; n < grave_count && p.y < 128; n++) {
        vec3 g = texelFetch(graves_tex, ivec2(n, 0), 0).rgb;
        if (g.b > 0.5) continue;                                                  // a taken grave draws nothing
        if (abs(p.x - int(g.r + 0.5)) + abs(p.y - int(g.g + 0.5)) <= 1) idx = 6;  // the plus: the centre and its 4 neighbours
    }
    vec3 nib = idx != 15 ? view_nib(max(idx, 0))
             : sky_steps ? floor(texelFetch(sky_tex, ivec2(0, p.y), 0).rgb * (255.0 / 17.0) + 0.5)   // the row colours, faded already
             : sky_nib(sky_colour_index(height0e, p.y));                                     // the node sits on game row 0
    COLOR = idx < 0 ? vec4(0.0) : vec4(nib * 17.0 / 255.0, 1.0);   // no mark: see-through
}
