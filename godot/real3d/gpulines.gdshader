shader_type spatial;
render_mode unshaded, cull_disabled;

// ENHANCEMENT (renderer = 3d): the models' line faces drawn by the card from the entity
// records ($7BF2). The original's line is ONE GAME PIXEL wide (a pixel of its 320 x 200 picture); the card draws at
// gpu_px times that, so each segment is a quad exactly one game pixel thick on the screen, whatever the window scale.
// Six vertices a segment: two triangles across the segment's two ends.
// Also the window borders (GpuWorld.Bake, GLASS: SEE-THROUGH): coloured as the faces round the window are - a machine's
// paint and tint, the sun on the window's plane - then darker.

#include "res://real3d/place.gdshaderinc"
#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/sunshade.gdshaderinc"
#include "res://real3d/own_colours.gdshaderinc"
#include "res://real3d/gpumodels.gdshaderinc"
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/ground_contact.gdshaderinc"

uniform vec2 vp_size = vec2(1280.0, 560.0);      // the 3D viewport in pixels (Real3DView)
uniform bool glass = false;                       // GLASS: SEE-THROUGH - the window borders are drawn only then (GpuWorld sets it)
const float BORDER_DARK = 0.6;                    // a window border: the panel colour round the window, darker

varying flat float tag;
varying flat float dark;
varying flat vec3 own_tint;                       // a window border: its machine's tint, 0 = none (the palette's), as gpumodels_body
varying flat float own_light;                     // ...and the sun's light on the window's plane
varying float depth_;                             // HAZE: how far ahead of the eye (POSITION.w)

// A segment end, in clip space: the world point moved to the eye (sat on the real ground with its model, contact),
// a little nearer, and back with the shell if indoors.
bool line_end(int id, mat4 mv, mat4 proj, bool contact, vec4 fit, vec2 fc, out vec4 clip, out int colour) {
    vec3 p;
    clip = vec4(2.0, 2.0, 2.0, 1.0);
    if (!gpu_line_vertex(id, p, colour)) return false;
    if (contact) {
        int row = id / (line_slots * 2);
        p = contact_turn(fit, fc, p, p.x - float(_euw(ent_tex, row, 0x4A)), p.z - float(_euw(ent_tex, row, 0x4E)));   // as its faces (model_clip)
    }

    float k = (shell_row >= 0 && id / (line_slots * 2) == shell_row) ? 1.25 : 1.0;   // the shell's own lines go back with it
    vec3 eye = (mv * vec4(view_rel(vec3(p.x, p.y, -p.z)), 1.0)).xyz;
    clip = proj * vec4(eye * 0.99 * k, 1.0);                            // a line lies on its face (which may itself be pulled 0.4% nearer): nearer than that
    return clip.w > 0.01;
}

// A window border's paint and light, worked out as gpumodels_body.gdshaderinc does for a face: the tint from its colour, a
// tinted military machine's one base paint, then the sun on the plane of the border's two ends and the window's third
// corner (line_tex alpha - 1), outward away from the model's centre.
void border_paint(int id, inout int colour, out vec3 tint, out float light) {
    tint = vec3(0.0);
    light = 1.0;
    int per = line_slots * 2;
    int row = id / per;
    int slot = (id - row * per) / 2;
    int b25 = _eb(ent_tex, row, 0x25);
    vec4 mi = texelFetch(model_tex, ivec2(b25, 0), 0);
    vec4 li = texelFetch(model_tex, ivec2(b25, 1), 0);
    vec4 seg = texelFetch(line_tex, ivec2(0, int(li.r + 0.5) + slot), 0);
    tint = own_tint_of(b25, colour, false);
    bool tinted = tint != vec3(0.0);
    bool military = texelFetch(own_tex, ivec2(b25, 0), 0).a > 0.75;
    if (tinted && military) colour = int(texelFetch(own_tex, ivec2(b25, 1), 0).r + 0.5);   // its Base (GpuWorld.OwnTints)
    int type = _ew(ent_tex, row, 4);
    bool fire = type >= 0x61 && type <= 0x67;
    if (fire || !sun_shading || !sun_up || inside_view || row == shell_row) return;
    int base = int(mi.r + 0.5), low;
    vec3 a = vec3(entity_corner_rot(trig_tex, ent_tex, entprev_tex, anim_tex, 0, 1, row, base, int(seg.r + 0.5), low));
    vec3 b = vec3(entity_corner_rot(trig_tex, ent_tex, entprev_tex, anim_tex, 0, 1, row, base, int(seg.g + 0.5), low));
    vec3 c = vec3(entity_corner_rot(trig_tex, ent_tex, entprev_tex, anim_tex, 0, 1, row, base, int(seg.a + 0.5) - 1, low));
    vec3 n = cross(b - a, c - a);
    float len = length(n);
    if (len <= 0.0) return;
    n /= len;
    if (dot(n, (a + b + c) / 3.0 - vec3(entity_centre_rot(row, b25))) < 0.0) n = -n;
    if (tinted || (colour >= 1 && colour <= 14)) light = float(sun_light64(dot(n, sun_dir))) / 64.0;
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
    vec4 fit; vec2 fc;
    bool contact = contact_fit_row(row, fit, fc);
    bool ok = line_end(base_id, MODELVIEW_MATRIX, PROJECTION_MATRIX, contact, fit, fc, ca, colour);
    int colour_b;
    ok = line_end(base_id + 1, MODELVIEW_MATRIX, PROJECTION_MATRIX, contact, fit, fc, cb, colour_b) && ok;
    bool border = gpu_line_border(base_id);
    if (border && !glass) ok = false;                                               // GLASS: ORIGINAL - painted-black windows, no borders
    dark = border ? BORDER_DARK : 1.0;
    vec3 tint = vec3(0.0);
    float light = 1.0;
    if (ok && border) border_paint(base_id, colour, tint, light);
    own_tint = tint;
    own_light = light;
    tag = float(colour);
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
    float d = cam_interior ? 0.0 : depth_;
    if (own_tint != vec3(0.0))                                                       // as gpumodels_body: tinted, lit and hazed
        ALBEDO = to_linear(floor(mix(tinted_rgb8(int(round(tag)), FRAGCOORD, own_tint, own_light), sky_nib(5) * 17.0, haze_amount(d)) + 0.5) / 255.0);
    else if (own_light != 1.0)
        ALBEDO = world_lit_hazed(int(round(tag)), FRAGCOORD, d, own_light);
    else
        ALBEDO = world_hazed(int(round(tag)), FRAGCOORD, d);
    ALBEDO *= dark;
}
