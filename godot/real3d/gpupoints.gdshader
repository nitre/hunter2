shader_type spatial;
render_mode unshaded, cull_disabled;

// ENHANCEMENT (renderer = 3d, setting 3D DRAWN BY shaders): the original's dots ($5B0A) drawn by the card from the
// entity records - the thing's spot in colour 2 and its ground spot in colour 0, one pixel each.

#include "res://real3d/place.gdshaderinc"
#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/gpumodels.gdshaderinc"
#include "res://real3d/view.gdshaderinc"

varying flat float tag;
varying float depth_;                             // HAZE: how far ahead of the eye (POSITION.w)

void vertex() {
    vec3 p; int colour;
    if (gpu_dot_vertex(VERTEX_ID, p, colour)) {
        tag = float(colour);
        vec3 eye = (MODELVIEW_MATRIX * vec4(view_rel(vec3(p.x, p.y, -p.z)), 1.0)).xyz;
        POSITION = PROJECTION_MATRIX * vec4(eye * 0.996, 1.0);
        ivec2 pix;                                    // on the game's camera: the original's own pixel (whole-number divide)
        if (view_pixel(int(p.x), int(p.y), int(p.z), pix)) {
            float vx = (float(pix.x) + 0.5) * gpu_px + (VIEWPORT_SIZE.x - 320.0 * gpu_px) * 0.5;   // column $A0 stays mid-picture
            float vy = (float(pix.y) + 0.5) * gpu_px;
            POSITION.xy = (vec2(vx / VIEWPORT_SIZE.x, vy / VIEWPORT_SIZE.y) * 2.0 - 1.0) * POSITION.w;
        }
    } else {
        tag = 0.0;
        POSITION = vec4(2.0, 2.0, 2.0, 1.0);
    }
    depth_ = POSITION.w;
}

void fragment() {
    ALBEDO = world_hazed(int(round(tag)), FRAGCOORD, cam_interior ? 0.0 : depth_);
}
