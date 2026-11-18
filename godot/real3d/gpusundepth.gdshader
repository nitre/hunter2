shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_always;

// ENHANCEMENT (real shadows, step 1): the world's things as the sun sees them - a depth picture, how far each spot of
// them is from the sun. Drawn into its own SubViewport and world (GpuWorld SunDepth), never on the player's screen.
// The corners are placed exactly as the opaque model pass places them (gpumodels_body.gdshaderinc model_clip): the same
// triangles dropped, gpu_model_vertex_rot, and on a slope the same contact turn and walls_down. Only the projection
// differs: no camera, no perspective. Every point slides along the sun's direction down to the level of the box's
// centre (the player's spot), and the square of that level sun_box_size world units a side round the centre fills the
// whole picture - x east to the right, z north up. The depth is the distance along the sun's direction, 0..1 over
// sun_box_depth: 0.5 at the centre, smaller = nearer the sun. The depth test keeps the nearest (Godot's reversed depth:
// the larger z wins, so z = 1 - depth).
// Units: sun_dir is in the picture's units, where the doubled x and z and the heights are alike - the same direction
// as in the world node's units (x, height / 2, z) the 3D view shows, so the point is taken in those.
// Out: r = floor(d * 256) / 256, g = fract(d * 256) (d = r + g / 256, exact in the half-float picture), b = d, a = 1.
// Nothing drawn: (0, 0, 0, 0).
// Not drawn here (models only in this step): lines, glass, the ground, blast pieces. Not copied from the model pass,
// because they belong to the player's screen: the detail faces' pull toward the eye, the thin faces' widening to one
// game pixel, the back-to-back face test.

#include "res://real3d/place.gdshaderinc"
#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/sunshade.gdshaderinc"
#include "res://real3d/own_colours.gdshaderinc"
#include "res://real3d/gpumodels.gdshaderinc"
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/ground_contact.gdshaderinc"

uniform bool glass = false;                        // as the model pass: which faces the opaque pass draws (GpuWorld copies it)
uniform vec3 sun_box_centre = vec3(0.0);           // the box's centre: world units, x east, height (whole, not halved), z north
uniform float sun_box_size = 4096.0;               // the ground square the picture covers, world units a side
uniform float sun_box_depth = 8192.0;              // the depth range 0..1 covers, along the sun, in the shown units

varying float sun_d;

// One corner's world point as the opaque pass has it before the camera (model_clip without the view); false = not drawn.
bool sun_corner(int id, bool contact, vec4 fit, vec2 fc, out vec3 p) {
    int colour; ivec3 rot; bool own_side, pull;
    p = vec3(0.0);
    if (!gpu_model_vertex_rot(id, p, colour, pull, rot, own_side)) return false;
    if (contact) {
        p = contact_turn(fit, fc, p, float(rot.x) * 0.5, float(rot.z) * 0.5);
        p.y = walls_down(fc, p.x, p.z, p.y);
    }
    return true;
}

void vertex() {
    int corner = VERTEX_ID - (VERTEX_ID / 3) * 3;
    int first = VERTEX_ID - corner;
    int gbits = glass_bits(first);
    bool ok = glass ? (gbits & 0x9000) == 0 : (gbits & 0x2000) == 0;       // the opaque pass's own test (no glass faces)
    int per = tri_slots * 3, row0 = first / per, b25 = _eb(ent_tex, row0, 0x25);
    if (ok) ok = b25 != 0 && (first - row0 * per) / 3 < int(texelFetch(model_tex, ivec2(b25, 0), 0).b + 0.5);
    vec4 fit; vec2 fc;
    bool contact = ok && contact_fit_row(row0, fit, fc);
    vec3 p0, p1, p2;
    if (ok) {                                                               // all three, as the model pass: one failed corner drops the triangle
        ok = sun_corner(first, contact, fit, fc, p0);
        ok = sun_corner(first + 1, contact, fit, fc, p1) && ok;
        ok = sun_corner(first + 2, contact, fit, fc, p2) && ok;
    }
    sun_d = 1.0;
    if (!ok || !sun_up) {
        POSITION = vec4(2.0, 2.0, 2.0, 1.0);                                // outside the clip volume: nothing is drawn
    } else {
        vec3 p = corner == 0 ? p0 : (corner == 1 ? p1 : p2);
        vec3 s = sun_dir;
        vec3 v = vec3(p.x, p.y * 0.5, p.z) - vec3(sun_box_centre.x, sun_box_centre.y * 0.5, sun_box_centre.z);
        vec2 g = v.xz - s.xz * (v.y / max(s.y, 0.02));                      // slid along the sun to the centre's level
        float d = 0.5 - dot(v, s) / sun_box_depth;
        sun_d = d;
        // clip y is down the picture here (the card's own clip space; Godot's PROJECTION_MATRIX carries that flip, this
        // hand-made projection must make it itself): north (+z) up the picture
        POSITION = vec4(g.x * 2.0 / sun_box_size, -g.y * 2.0 / sun_box_size, clamp(1.0 - d, 0.0, 1.0), 1.0);
    }
}

void fragment() {
    float d = clamp(sun_d, 0.0, 1.0);
    float hi = floor(d * 256.0);
    ALBEDO = vec3(hi / 256.0, d * 256.0 - hi, d);                          // opaque: alpha 1 where drawn (ALPHA unwritten keeps it in the opaque pass)
}
