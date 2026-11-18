shader_type spatial;
render_mode unshaded, cull_disabled;

// ENHANCEMENT (renderer = 3d): the models' line faces drawn by the card from the entity
// records ($7BF2). The original's line is ONE GAME PIXEL wide (a pixel of its 320 x 200 picture); the card draws at
// gpu_px times that, so each segment is a quad exactly one game pixel thick on the screen, whatever the window scale.
// Six vertices a segment: two triangles across the segment's two ends.

#include "res://real3d/place.gdshaderinc"
#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/gpumodels.gdshaderinc"
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/ground_contact.gdshaderinc"

uniform vec2 vp_size = vec2(1280.0, 560.0);      // the 3D viewport in pixels (Real3DView)

varying flat float tag;
varying float depth_;                             // HAZE: how far ahead of the eye (POSITION.w)

// A segment end, in clip space: the world point moved to the eye (sat on the real ground with its model, contact),
// a little nearer, and back with the shell if indoors.
bool line_end(int id, mat4 mv, mat4 proj, bool contact, vec3 fit, vec2 fc, out vec4 clip, out int colour) {
    vec3 p;
    clip = vec4(2.0, 2.0, 2.0, 1.0);
    if (!gpu_line_vertex(id, p, colour)) return false;
    if (contact) {
        int row = id / (line_slots * 2);
        p.y += contact_lift_at(fit, fc, p.x - float(_euw(ent_tex, row, 0x4A)), p.z - float(_euw(ent_tex, row, 0x4E)));
    }

    float k = (shell_row >= 0 && id / (line_slots * 2) == shell_row) ? 1.25 : 1.0;   // the shell's own lines go back with it
    vec3 eye = (mv * vec4(view_rel(vec3(p.x, p.y, -p.z)), 1.0)).xyz;
    clip = proj * vec4(eye * 0.99 * k, 1.0);                            // a line lies on its face (which may itself be pulled 0.4% nearer): nearer than that
    return clip.w > 0.01;
}

void vertex() {
    int per = line_slots * 6;
    int row = VERTEX_ID / per;
    int rem = VERTEX_ID - row * per;
    int slot = rem / 6;
    int c = rem - slot * 6;
    int end = (c == 0 || c == 1 || c == 3) ? 0 : 1;                                  // a, a, b, a, b, b
    float side = (c == 0 || c == 2 || c == 5) ? -1.0 : 1.0;
    int base_id = (row * line_slots + slot) * 2;
    vec4 ca, cb;
    int colour;
    vec3 fit; vec2 fc;
    bool contact = contact_fit_row(row, fit, fc);
    bool ok = line_end(base_id, MODELVIEW_MATRIX, PROJECTION_MATRIX, contact, fit, fc, ca, colour);
    tag = float(colour);
    int colour_b;
    ok = line_end(base_id + 1, MODELVIEW_MATRIX, PROJECTION_MATRIX, contact, fit, fc, cb, colour_b) && ok;
    if (ok) {
        vec2 sa = ca.xy / ca.w * vp_size * 0.5, sb = cb.xy / cb.w * vp_size * 0.5;   // the two ends in pixels
        vec2 d = sb - sa;
        d = length(d) > 0.0001 ? normalize(d) : vec2(1.0, 0.0);
        vec2 n = vec2(-d.y, d.x) * side * gpu_px * 0.5;                              // half a game pixel each side
        vec4 e = end == 0 ? ca : cb;
        POSITION = vec4(e.xy + n * 2.0 / vp_size * e.w, e.z, e.w);
    } else {
        POSITION = vec4(2.0, 2.0, 2.0, 1.0);
    }
    depth_ = POSITION.w;
}

void fragment() {
    ALBEDO = world_hazed(int(round(tag)), FRAGCOORD, cam_interior ? 0.0 : depth_);
}
