shader_type spatial;
render_mode unshaded, cull_disabled;

// (EXPLOSIONS: PIECES, the card-drawn 3D view; BETTER EXPLOSIONS step 6): a small star where a wreck's panel breaks
// (pieces.gdshaderinc break_star_corner) - one mesh of BREAK_STARS x star_slots x 3 empty vertices a blast, the blasts'
// rows as the pieces' (gpupieces.gdshader). Fire takes no sun: its own colours, hazed with distance.

#include "res://real3d/place.gdshaderinc"
#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/sunshade.gdshaderinc"
#include "res://real3d/own_colours.gdshaderinc"
#include "res://real3d/gpumodels.gdshaderinc"
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/ground_cell.gdshaderinc"
#include "res://real3d/pieces.gdshaderinc"

uniform sampler2D blast_tex : filter_nearest;     // the blown-up things' records as they were (0xA0 x BLASTS)
uniform float piece_clock = 0.0;                  // frames now
uniform float piece_life = 600.0;
uniform bool pieces_on = false;                   // EXPLOSIONS: PIECES
uniform int star_slots = 46;                      // the most triangles a blast's star model has (GpuWorld.Bake)

varying flat float tag;
varying float depth_;                             // HAZE: how far ahead of the eye (POSITION.w)

void vertex() {
    PROJECTION_MATRIX = wide_projection(PROJECTION_MATRIX);
    int row = INSTANCE_ID;
    int per = star_slots * 3;
    int n = VERTEX_ID / per;                                  // which of the blast's small stars
    int rem = VERTEX_ID - n * per, slot = rem / 3, k = rem - slot * 3;
    vec4 info = texelFetch(blast_info, ivec2(row, 0), 0);
    float t = piece_clock - info.r;
    vec3 p; int colour;
    if (!pieces_on || info.g < 0.5 || t < 0.0 || t > piece_life || inside_view
        || !break_star_corner(blast_tex, cam_map, row, texelFetch(blast_info, ivec2(row, 1), 0), int(info.b + 0.5), n, slot, k, t, p, colour)) {
        tag = 0.0;
        POSITION = vec4(2.0, 2.0, 2.0, 1.0);
    } else {
        tag = float(colour);
        POSITION = PROJECTION_MATRIX * vec4((MODELVIEW_MATRIX * vec4(view_rel(vec3(p.x, p.y, -p.z)), 1.0)).xyz, 1.0);
    }
    depth_ = POSITION.w;
}

void fragment() {
    ALBEDO = world_hazed(int(round(tag)), FRAGCOORD, cam_interior ? 0.0 : depth_);
}
