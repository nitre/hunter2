shader_type spatial;
render_mode unshaded, cull_disabled;

// STUNTIN' (godot/real3d/MountainMesh.cs): Stunt Car Racer's mountain horizon - a ring round the eye, so only the view's
// turn (and the free camera's tilt) moves it, never its place: the eye at the origin. Colours the original's own $0RGB
// words, as road.gdshader's (the screen's fade less the hour's, the original's words in full light, the death fade), no haze
// (the original draws them flat). UV.x = the colour word.

#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/view.gdshaderinc"

varying flat float word_;

void vertex() {
    word_ = UV.x;
    vec3 rel = cam_free ? view_from(VERTEX, 0.0, 0.0, 0.0, cam_free_yaw, cam_free_pitch) : view_from(VERTEX, 0.0, 0.0, 0.0, cam_yaw, 0.0);
    POSITION = PROJECTION_MATRIX * vec4((MODELVIEW_MATRIX * vec4(rel, 1.0)).xyz, 1.0);
}

void fragment() {
    int lv = min(15, fade_level + 15 - day_level);
    int c = faded(int(round(word_)), lv >= 15 ? 16 : lv);
    ALBEDO = to_linear(nibbles(death_step > 0 ? death_faded(c, death_step) : c) * 17.0 / 255.0);
}
