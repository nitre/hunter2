shader_type spatial;
render_mode unshaded, cull_disabled;

// ENHANCEMENT, NOT THE ORIGINAL (setting renderer = 3d, godot/real3d/Real3DView.cs): the sky backdrop and the sea
// plane, each one colour of the game's (world_colour.gdshaderinc): force_idx 15 the sky's bands, 16 the sea.

#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/clouds.gdshaderinc"
#include "res://real3d/stars.gdshaderinc"

uniform int force_idx = 15;  // 15 the sky backdrop, 16 the sea plane
uniform vec3 sea_off = vec3(0.0);  // the sea plane's own place in the world node (Real3DView)

varying vec2 sea_at;   // the sea pixel's world x and z: its shore distance and the ripples' anchor
varying float depth_;  // HAZE: how far ahead of the eye (POSITION.w), as groundgpu.gdshader

void vertex() {
    // (POSITION is set for EVERY vertex: a shader that writes it takes over the projection for all of them)
    vec3 eye;
    if (force_idx == 16) {
        vec3 w = VERTEX + sea_off;                                  // the sea pixel's own world place
        sea_at = w.xz;
        // the card moves the world to the eye itself (view.gdshaderinc), so the plane's node move is taken here
        // and bypassed; the world node itself only halves the heights, as MODELVIEW would
        eye = view_rel(w) * vec3(1.0, 0.5, 1.0);
    } else
        eye = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;           // the sky rides the camera
    POSITION = PROJECTION_MATRIX * vec4(eye, 1.0);
    depth_ = POSITION.w;
}

void fragment() {
    // the sea with SEA: LIVING (ENHANCEMENT - world_colour.gdshaderinc sea_rgb8): its colour worked out per pixel;
    // SEA: ORIGINAL and the sky backdrop: their one colour. The flat sea plane runs up to the eye line, but the
    // original's sea starts below its own edge (sky_line 5): above that the plane shows the sky's band there
    int idx = force_idx == 16 && floor(FRAGCOORD.y) <= sky_line(5) ? 15 : force_idx;
    bool backdrop = idx == 15 && force_idx == 15;
    int gx = int(floor(FRAGCOORD.x / gpu_px)), gy = int(floor(FRAGCOORD.y / gpu_px));
    int cloud = backdrop ? cloud_at(gx, gy) : 0;                     // CLOUDS: on the sky backdrop only
    ALBEDO = cloud > 0 ? to_linear(cloud_rgb8(world_rgb8(15, FRAGCOORD), cloud) / 255.0)
           : backdrop && cloud_on ? to_linear(night_rgb8(world_rgb8(15, FRAGCOORD), gx, gy) / 255.0)   // CLOUDS' night: the stars and the moon, behind the clouds (stars.gdshaderinc)
           : idx == 16 && sea_living ? sea_albedo(FRAGCOORD, sea_at, cam_interior ? 0.0 : depth_)
                                     : world_albedo(idx, FRAGCOORD);
}
