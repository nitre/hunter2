shader_type spatial;
render_mode unshaded, cull_disabled;

// (EXPLOSIONS: PIECES, the card-drawn 3D view; BETTER EXPLOSIONS step 7): the smoke where a star went out
// (smoke.gdshaderinc) - one mesh of SMOKE_PUFFS x SMOKE_TRIS x 3 empty vertices a star, a row a star of the card's own
// ring (GpuWorld.AddSmoke). The palette's one grey, its sides lighter and darker; hazed with distance.

#include "res://real3d/place.gdshaderinc"
#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/sunshade.gdshaderinc"
#include "res://real3d/own_colours.gdshaderinc"
#include "res://real3d/gpumodels.gdshaderinc"
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/ground_cell.gdshaderinc"
#include "res://real3d/pieces.gdshaderinc"
#include "res://real3d/smoke.gdshaderinc"

uniform vec4 smoke_at[24];                        // where each star went out (world x, height, z) and a number of its own
uniform vec2 smoke_info[24];                      // x = the frame it went out, y = the star's reach (0 = none)
uniform float piece_clock = 0.0;                  // frames now
uniform bool pieces_on = false;                   // EXPLOSIONS: PIECES
const int SMOKE_GREY = 8;                         // the palette's grey ($999)

varying flat float light_;
varying float depth_;                             // HAZE: how far ahead of the eye (POSITION.w)

void vertex() {
    PROJECTION_MATRIX = wide_projection(PROJECTION_MATRIX);
    int row = INSTANCE_ID;
    int puff = VERTEX_ID / (SMOKE_TRIS * 3);
    int rem = VERTEX_ID - puff * SMOKE_TRIS * 3, tri = rem / 3, k = rem - tri * 3;
    vec3 p; float light;
    if (!pieces_on || inside_view || !smoke_corner(smoke_at[row], smoke_info[row].y, piece_clock - smoke_info[row].x, puff, tri, k, p, light)) {
        light_ = 1.0;
        POSITION = vec4(2.0, 2.0, 2.0, 1.0);
    } else {
        light_ = light;
        POSITION = PROJECTION_MATRIX * vec4((MODELVIEW_MATRIX * vec4(view_rel(vec3(p.x, p.y, -p.z)), 1.0)).xyz, 1.0);
    }
    depth_ = POSITION.w;
}

void fragment() {
    ALBEDO = world_lit_hazed(SMOKE_GREY, FRAGCOORD, cam_interior ? 0.0 : depth_, light_);
}
