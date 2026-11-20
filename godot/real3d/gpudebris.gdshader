shader_type spatial;
render_mode unshaded, cull_disabled;

// ENHANCEMENT (renderer = 3d, setting 3D DRAWN BY shaders): the original's explosion debris ($4F22) drawn by the card -
// model 0 as the game rewrote it, ten triangles flying apart (gpumodels.gdshaderinc gpu_debris_vertex).

#include "res://real3d/place.gdshaderinc"
#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/gpumodels.gdshaderinc"
#include "res://real3d/view.gdshaderinc"

varying flat float tag;
varying float depth_;                             // HAZE: how far ahead of the eye (POSITION.w)
uniform bool debris_off = false;     // EXPLOSIONS: PIECES - the card's own pieces (gpupieces.gdshader) stand in for these

void vertex() {
    PROJECTION_MATRIX = wide_projection(PROJECTION_MATRIX);   // the TV looks' wide game pixels (world_colour.gdshaderinc)
    vec3 p; int colour;
    if (!debris_off && gpu_debris_vertex(VERTEX_ID, p, colour)) {
        tag = float(colour);
        POSITION = PROJECTION_MATRIX * vec4((MODELVIEW_MATRIX * vec4(view_rel(vec3(p.x, p.y, -p.z)), 1.0)).xyz, 1.0);
    } else {
        tag = 0.0;
        POSITION = vec4(2.0, 2.0, 2.0, 1.0);
    }
    depth_ = POSITION.w;
}

void fragment() {
    ALBEDO = world_hazed(int(round(tag)), FRAGCOORD, cam_interior ? 0.0 : depth_);
}
