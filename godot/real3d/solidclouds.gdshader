shader_type spatial;
render_mode unshaded, cull_disabled;

// (setting clouds, CLOUDS, the card-drawn 3D view): the LOW cloud layer as SOLID shapes - the same clouds as the flat
// cards of lowclouds.gdshader (clouds.gdshaderinc low_cloud: their places, widths, kinds and drift), each now a cluster
// of 2-5 PUFFS: flattened balls of a few flat facets standing on one flat base at low_cloud_y, so a cloud has thickness
// and its shape changes as the eye goes round or under it. Each facet is one flat shade of the cumulus' own stepped
// colours, lit from above and from the sun's side. SEE-THROUGH in the game's dot pattern (LOW_CLOUD_DOTS): the sunlit
// top is whole, the sides thin toward the base, the underside is the thinnest (the sky shows through, so it needs no
// dark shade), and a facet seen edge-on thins too; near the eye every facet dissolves by its own distance. Only the
// facets turned to the eye are drawn, so the holes show the sky and not the cloud's own inside.
// The mesh is LOW_CLOUD_N x CLOUD_CORNERS empty corners (Real3DView.SolidCloudCorners), each worked out here.

#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/clouds.gdshaderinc"

const int PUFFS = 5;                             // the most a cloud has (low_cloud's heaps: 2..5)
const int MAX_SEGS = 8;                          // round a puff, at most
const int MAX_BANDS = 4;                         // from its base to its top, at most
const int PUFF_CORNERS = MAX_SEGS * MAX_BANDS * 2 * 3;
const int CLOUD_CORNERS = PUFFS * PUFF_CORNERS;
const float PUFF_UP = 0.62;                      // a puff's height above its middle, of its radius
const float PUFF_DOWN = 0.28;                    // and below it: a flatter base

uniform bool sun_up = false;                     // the sun is up (Real3DView.Frame)
uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);      // toward the sun, unit length, x east / y up / z north
uniform int puff_segs = 8;                     // facets round a puff (3..MAX_SEGS)
uniform int puff_bands = 4;                    // and from base to top (2..MAX_BANDS)
uniform float under_keep = 0.5;                  // the share of dots the underside keeps
uniform float side_keep = 0.75;                  // and a side at the base's height (whole by half way up)
uniform float edge_keep = 0.5;                   // and a facet seen edge-on

varying flat int level_;                         // the facet's shade: 1 the shaded side .. 4 the bright top
varying flat float keep_;                        // the share of its dots the facet keeps, 0..1
varying float depth_;                            // how far ahead of the eye (POSITION.w): the haze, the near dissolve

// puff i of cloud c: its middle (x east, y shown height above the base, z north, from the cloud's own middle) and radius (w)
vec4 puff_at(LowCloud c, int i, float tall) {
    uint hp = _cloud_hash(c.seed + uint(i) * 7919u + 3u), hq = _cloud_hash(hp + 11u);
    float wide = c.width * 0.42;
    float place = (float(i) + 0.5) / float(c.heaps) - 0.5;               // strung out east-west
    float radius = wide * mix(0.42, 0.62, _cloud_unit(hp)) * (1.0 - 1.6 * place * place);   // the middle ones the biggest
    return vec4(place * wide * 1.7, radius * PUFF_DOWN * tall, (_cloud_unit(hq >> 5u) - 0.5) * wide * 0.7, radius);
}

// the direction of a puff's corner (band bi of bands, segment si of segs, the puff turned by turn)
vec3 puff_dir(int bi, int si, int bands, int segs, float turn) {
    float lat = -1.5708 + 3.1416 * float(bi) / float(bands), lon = turn + 6.2832 * float(si) / float(segs);
    return vec3(cos(lat) * cos(lon), sin(lat), cos(lat) * sin(lon));
}

// that corner's place from the puff's middle, in shown lengths (heights halved); each corner off the poles is pushed in
// or out a little by its own number (seed), the same for every facet that shares it, so a puff is a lumpy rock of a
// cloud and not a turned shape
vec3 puff_corner(uint seed, int bi, int si, int bands, int segs, float turn, float radius, float tall) {
    vec3 n = puff_dir(bi, si, bands, segs, turn);
    float lump = bi == 0 || bi == bands ? 1.0 : mix(0.8, 1.18, _cloud_unit(_cloud_hash(seed + uint(bi) * 53u + uint(si - (si / segs) * segs) * 17u)));
    return radius * lump * vec3(n.x, n.y * (n.y > 0.0 ? PUFF_UP : PUFF_DOWN) * tall, n.z);
}

void vertex() {
    PROJECTION_MATRIX = wide_projection(PROJECTION_MATRIX);   // the TV looks' wide game pixels (world_colour.gdshaderinc)
    int segs = clamp(puff_segs, 3, MAX_SEGS), bands = clamp(puff_bands, 2, MAX_BANDS);
    int cloud = VERTEX_ID / CLOUD_CORNERS;
    int rest = VERTEX_ID - cloud * CLOUD_CORNERS;
    int puff = rest / PUFF_CORNERS;
    rest -= puff * PUFF_CORNERS;
    int tri = rest / 3, k = rest - tri * 3;
    int quad = tri / 2;
    int band = quad / segs, seg = quad - band * segs;
    LowCloud c = low_cloud(cloud);
    level_ = 1; keep_ = 0.0; depth_ = 0.0;
    POSITION = vec4(2.0, 2.0, 2.0, 1.0);                                 // outside the clip volume: nothing is drawn
    if (puff < c.heaps && band < bands) {
        float tall = clamp(c.aspect, 0.5, 1.5);                          // the cloud's kind: flat, lumpy or tall
        vec4 me = puff_at(c, puff, tall);
        float turn = _cloud_unit(_cloud_hash(c.seed + uint(puff) * 131u + 5u)) * 6.2832;
        // the facet's four corners: (band, seg), (band, seg + 1), (band + 1, seg + 1), (band + 1, seg); two triangles
        int q = (tri & 1) == 1 ? (k == 0 ? 0 : (k == 1 ? 2 : 3)) : k;
        uint ps = _cloud_hash(c.seed + uint(puff) * 613u + 9u);
        vec3 off = puff_corner(ps, band + (q >= 2 ? 1 : 0), seg + (q == 1 || q == 2 ? 1 : 0), bands, segs, turn, me.w, tall);
        // the facet's own middle and the way it faces (its four corners' mean; outward from the puff's middle)
        vec3 a = puff_corner(ps, band, seg, bands, segs, turn, me.w, tall), b = puff_corner(ps, band, seg + 1, bands, segs, turn, me.w, tall);
        vec3 d = puff_corner(ps, band + 1, seg + 1, bands, segs, turn, me.w, tall), e = puff_corner(ps, band + 1, seg, bands, segs, turn, me.w, tall);
        vec3 mid = (a + b + d + e) * 0.25;
        vec3 n = cross(d - a, e - b);
        n = dot(n, n) < 1e-6 ? normalize(mid) : normalize(n);
        if (dot(n, mid) < 0.0) n = -n;
        // to the eye: the world's lengths with the heights doubled back (the world node shows them at half)
        vec3 base = vec3(c.x + me.x, low_cloud_y + me.y * 2.0, c.z + me.z);
        vec3 eye_mid = (MODELVIEW_MATRIX * vec4(view_rel(vec3(base.x + mid.x, base.y + mid.y * 2.0, -(base.z + mid.z))), 1.0)).xyz;
        vec3 eye_out = (MODELVIEW_MATRIX * vec4(view_rel(vec3(base.x + mid.x + n.x, base.y + (mid.y + n.y) * 2.0, -(base.z + mid.z + n.z))), 1.0)).xyz;
        float facing = dot(normalize(eye_out - eye_mid), normalize(-eye_mid));   // 1 square to the eye, 0 edge-on, below 0 turned away
        if (facing > 0.0) {
            vec3 p = vec3(base.x + off.x, base.y + off.y * 2.0, base.z + off.z);
            POSITION = PROJECTION_MATRIX * vec4((MODELVIEW_MATRIX * vec4(view_rel(vec3(p.x, p.y, -p.z)), 1.0)).xyz, 1.0);
            depth_ = POSITION.w;
            // the light: from above, and from the sun's side while it is up
            vec3 l = sun_up ? normalize(vec3(sun_dir.x * 0.75, 0.6 + 0.4 * max(sun_dir.y, 0.0), sun_dir.z * 0.75)) : vec3(0.0, 1.0, 0.0);
            float lit = dot(n, l);
            level_ = lit > 0.7 ? 4 : lit > 0.35 ? 3 : lit > 0.0 ? 2 : 1;
            // the dots it keeps: the top whole; the underside the fewest; a side fewer the nearer the base; fewer edge-on
            float up = (me.y + mid.y) / max((PUFF_UP + PUFF_DOWN) * tall * c.width * 0.42 * 0.62, 1.0);   // 0 the base .. 1 the tallest puff's top
            // in whole quarters, so a facet is whole or plainly thinned - never one stray hole in sixteen
            float body = n.y > 0.35 ? 1.0 : n.y < -0.35 ? under_keep : up > 0.4 ? 1.0 : side_keep;
            float edge = n.y > 0.35 || facing > 0.25 ? 1.0 : edge_keep;
            keep_ = max(0.25, floor(body * edge * 4.0 + 0.5) / 4.0);
        }
    }
}

void fragment() {
    float near = clamp((depth_ - low_fade_near) / max(1.0, low_fade_far - low_fade_near), 0.0, 1.0);   // near the eye: dissolving, each facet by its own distance
    ivec2 g = ivec2(floor(FRAGCOORD.xy / vec2(gpu_px_x, gpu_px)));
    if (keep_ * near * 16.0 <= float(LOW_CLOUD_DOTS[(g.y & 3) * 4 + (g.x & 3)])) discard;
    ALBEDO = to_linear(low_cloud_rgb8(level_, depth_, FRAGCOORD.x) / 255.0);
}
