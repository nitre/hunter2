shader_type spatial;
render_mode unshaded, cull_disabled;

// (setting clouds, CLOUDS, the card-drawn 3D view): the LOW cloud layer with THICKNESS - the same cumulus as
// lowclouds.gdshader (clouds.gdshaderinc low_cloud / low_heap: the places, the heaps, the blocks, the five shades), but
// each heap is a real rounded lump standing in the world: half an ellipsoid on the flat base at low_cloud_y, as deep as
// it is wide, the heaps beside the first set a little north or south of it. A card square to the eye still carries
// each cloud; every block on it looks along its own line from the eye and takes the first heap that line meets, so the
// outline and the shading are right from any side, from above and from underneath (where the flat base shows), and
// change as the eye goes round. Seen level from the south or north it is the flat card's picture.
// The mesh is LOW_CLOUD_N x 6 empty corners (GpuWorld.Empty); each six make one cloud's card.

#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/clouds.gdshaderinc"

const float REACH = 0.85;                    // the card's half size, in cloud widths: every heap inside from any side
const float HEAP_DEPTH = 0.22;               // how far north or south of the first a heap may stand, in cloud widths

uniform bool sun_up = false;                 // the sun is up (Real3DView.Frame)
uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);  // toward the sun, unit length, x east / y up / z north

varying vec2 at_;                            // on the card from its middle: right and up, world units
varying flat int cid;                        // the cloud
varying flat vec3 mid_;                      // the card's middle, from the eye
varying flat vec3 right_;                    // the card's own right and up, unit length
varying flat vec3 up_;
varying flat vec3 base_;                     // the cloud's base middle, from the eye
varying flat vec3 east_;                     // one world unit east, one of height and one north, as the eye sees them
varying flat vec3 high_;
varying flat vec3 north_;
varying float depth_;                        // HAZE: how far ahead of the eye (POSITION.w)

vec3 eye_of(mat4 mv, vec3 local) { return (mv * vec4(view_rel(local), 1.0)).xyz; }

void vertex() {
    PROJECTION_MATRIX = wide_projection(PROJECTION_MATRIX);   // the TV looks' wide game pixels (world_colour.gdshaderinc)
    int k = VERTEX_ID % 6;
    cid = VERTEX_ID / 6;
    vec2 corner = k == 0 || k == 3 ? vec2(-1.0) : k == 1 ? vec2(1.0, -1.0) : k == 5 ? vec2(-1.0, 1.0) : vec2(1.0);
    LowCloud c = low_cloud(cid);
    vec3 m = vec3(c.x, low_cloud_y, -c.z);
    base_ = eye_of(MODELVIEW_MATRIX, m);
    east_ = eye_of(MODELVIEW_MATRIX, m + vec3(1.0, 0.0, 0.0)) - base_;
    high_ = eye_of(MODELVIEW_MATRIX, m + vec3(0.0, 1.0, 0.0)) - base_;
    north_ = eye_of(MODELVIEW_MATRIX, m + vec3(0.0, 0.0, -1.0)) - base_;
    mid_ = base_ + high_ * (c.box * 0.5);
    vec3 dir = normalize(mid_), upward = normalize(high_);
    vec3 r = cross(dir, upward);
    right_ = dot(r, r) < 1e-6 ? east_ : normalize(r);          // straight overhead or underneath: any level way will do
    up_ = cross(right_, dir);
    at_ = corner * (c.width * REACH);
    POSITION = PROJECTION_MATRIX * vec4(mid_ + right_ * at_.x + up_ * at_.y, 1.0);
    depth_ = POSITION.w;
}

// an eye-space arrow in the cloud's own lengths: x east, y height, z north (world units; heights whole, not halved)
vec3 own(vec3 v) { return vec3(dot(v, east_), dot(v, high_) / dot(high_, high_), dot(v, north_)); }

void fragment() {
    LowCloud c = low_cloud(cid);
    // the block this pixel is in: square on a card seen level (as wide as the flat card's, half as tall shown), and
    // evened out as the card tips over when the eye is above or below
    float step_h = c.width / LOW_CLOUD_STEPS;
    float tip = abs(dot(normalize(mid_), normalize(high_)));
    vec2 block = vec2(step_h, step_h * mix(0.5, 1.0, tip));
    vec2 at = (floor(at_ / block) + 0.5) * block;
    vec3 through = mid_ + right_ * at.x + up_ * at.y;                     // the block's middle, from the eye
    // the line from the eye through it, in cloud widths from the base's middle
    vec3 eye = own(-base_) / c.width, dir = own(through) / c.width;
    float t_base = abs(dir.y) < 1e-6 ? 0.0 : -eye.y / dir.y;              // where the line crosses the flat base
    float best = 1e9;
    bool found = false, on_base = false;
    vec3 n = vec3(0.0, 1.0, 0.0);
    LowHeap front = low_heap(c, 0);
    float hit_y = 0.0;
    for (int i = 0; i < 5; i++) {
        if (i >= c.heaps) break;
        LowHeap h = low_heap(c, i);
        float back = i == 0 ? 0.0 : (_cloud_unit(_cloud_hash(c.seed + uint(i) * 4513u + 29u)) - 0.5) * 2.0 * HEAP_DEPTH;
        vec3 size = vec3(h.a, h.b, h.a);
        vec3 q = (eye - vec3(h.o, 0.0, back)) / size, d = dir / size;
        float qa = dot(d, d), qb = dot(q, d), qc = dot(q, q) - 1.0;
        float disc = qb * qb - qa * qc;
        if (disc <= 0.0) continue;
        float root = sqrt(disc), t_in = (-qb - root) / qa, t_out = (-qb + root) / qa;
        bool base_first = false;                                         // only the half above the base is cloud
        if (dir.y > 1e-6) { if (t_base > t_in) { t_in = t_base; base_first = true; } }
        else if (dir.y < -1e-6) t_out = min(t_out, t_base);
        else if (eye.y < 0.0) continue;
        if (t_in >= t_out || t_in <= 0.0 || t_in >= best) continue;
        found = true; best = t_in; front = h; on_base = base_first;
        vec3 p = q + d * t_in;
        n = normalize(p / size);
        hit_y = eye.y + dir.y * t_in;
    }
    if (!found) discard;
    // the shade, as the flat card's: lit from the sun's side, from above and a little from the eye
    vec3 to_eye = normalize(vec3(eye.x, 0.0, eye.z) + vec3(0.0, 0.0, 1e-6));
    vec3 across = vec3(-to_eye.z, 0.0, to_eye.x);
    float sun_x = sun_up ? dot(sun_dir, across) : 0.0;
    float lit = dot(n, normalize(across * (sun_x * 0.75) + vec3(0.0, 0.6, 0.0) + to_eye * 0.45));
    int level = lit > 0.8 ? 4 : lit > 0.5 ? 3 : lit > 0.1 ? 2 : 1;
    if (on_base || hit_y < min(c.box / c.width * 0.14, front.b * 0.25)) level = 0;   // the flat base, in its own shadow
    // near the eye: the classic dot dissolve, by how far off the cloud's surface is here
    float from_eye = best * length(through);
    float keep = clamp((from_eye - low_fade_near) / max(1.0, low_fade_far - low_fade_near), 0.0, 1.0);
    ivec2 g = ivec2(floor(FRAGCOORD.xy / vec2(gpu_px_x, gpu_px)));
    if (keep * 16.0 <= float(LOW_CLOUD_DOTS[(g.y & 3) * 4 + (g.x & 3)])) discard;
    ALBEDO = to_linear(low_cloud_rgb8(level, depth_, FRAGCOORD.x) / 255.0);
}
