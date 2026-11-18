shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
stencil_mode read, write, compare_not_equal, 2;

// ENHANCEMENT (renderer = 3d): TRACK MARKS - the tread bars a tank leaves behind its two tracks (GpuWorld.TreadMarks),
// each a flat four-cornered patch turned to the heading it was made at, read from GpuWorld's ring (mark_tex, two texels
// an entry: x, z, heading, clock / kind, half width, half length). Each corner is pressed to the ground under it as
// gpushadow.gdshader's are, so a bar lies on the slopes it was made on. Only kind 1 (a tread bar) is drawn here.
// The colour: the ground already drawn under it, halved as the shadow's half-bright (gpushadow.gdshader), going evenly
// to no darkening at all as the mark reaches MARK_LIFE blanks of the game's clock (track_clock: still while paused).
// The stencil: a mark writes 2 and draws only where 2 is not already, so where a tank crosses its own path the ground
// is darkened once. The material is drawn before the shadows (RenderPriority -1): a shadow (1) still passes over a
// mark and lays its own half from the unshadowed picture, so a shadow is never weakened by a mark under it.
// No mark on the sea (GpuWorld notes none there), none drawn indoors (the shadows' own ground pressing is off there).

#include "res://real3d/place.gdshaderinc"
#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/gpumodels.gdshaderinc"
#include "res://real3d/view.gdshaderinc"

uniform sampler2D under_tex : hint_screen_texture, filter_nearest;
uniform sampler2D mark_tex : filter_nearest;

const float MARK_LIFE = 300.0;                    // blanks a mark lasts (GpuWorld.MarkLife)

varying float depth_;                             // HAZE: how far ahead of the eye (POSITION.w), as gpushadow
varying float age_;                               // the mark's age, 0..1 of MARK_LIFE

void vertex() {
    int i = VERTEX_ID / 6, k = VERTEX_ID % 6;
    vec4 a = texelFetch(mark_tex, ivec2(i, 0), 0);
    vec4 b = texelFetch(mark_tex, ivec2(i, 1), 0);
    float age = track_clock - a.w;
    age_ = clamp(age / MARK_LIFE, 0.0, 1.0);
    depth_ = 0.0;
    if (int(b.x + 0.5) != 1 || age < 0.0 || age >= MARK_LIFE || cam_interior || inside_view)
        POSITION = vec4(2.0, 2.0, 2.0, 1.0);
    else {
        int q = k < 3 ? k : (k == 3 ? 0 : k - 2);           // two triangles: corners 0 1 2, 0 2 3
        vec2 c = vec2(q == 1 || q == 2 ? 1.0 : -1.0, q >= 2 ? 1.0 : -1.0);   // (along, across)
        vec2 fwd = vec2(sin(a.z), cos(a.z)), across = vec2(cos(a.z), -sin(a.z));
        vec2 xz = a.xy + fwd * (c.x * b.z) + across * (c.y * b.y);
        float y = float(ground_height(cam_map, int(round(xz.x)) & 0xFFFF, int(round(xz.y)) & 0xFFFF));
        POSITION = PROJECTION_MATRIX * vec4((MODELVIEW_MATRIX * vec4(view_rel(vec3(xz.x, y, -xz.y)), 1.0)).xyz * 0.996, 1.0);
        depth_ = POSITION.w;
    }
}

void fragment() {
    vec3 c = floor(to_display(texture(under_tex, SCREEN_UV).rgb) * 255.0 + 0.5);   // 0..255 a channel
    vec3 nib = floor(c / 17.0 + 0.5);
    bool word = all(equal(nib * 17.0, c));                                           // a 16-level colour: its $0RGB word
    vec3 half_ = word ? floor(nib / 2.0) * 17.0 : floor(c / 2.0);
    float f = 1.0 - (1.0 - haze_amount(depth_)) * (1.0 - age_);                     // 0 = the full half, 1 = none
    vec3 m = mix(half_, c, f);
    if (f > 0.0) m = floor(m + 0.5);
    ALBEDO = to_linear(m / 255.0);
}
