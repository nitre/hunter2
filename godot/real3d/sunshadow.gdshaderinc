// ENHANCEMENT (SHADOWS: REAL): a spot takes the sun's shadow when the sun's depth picture (GpuWorld SunDepth,
// gpusundepth.gdshader) holds something nearer the sun on the ray through it. The lookup is the sun pass's own mapping
// run for the spot: slid along the sun to the box centre's level, the picture's pixel there (nearest, so a shadow's
// edge is hard), the depth along the sun compared with the one stored. A spot in shadow loses the sun's share of its
// light and keeps the sky's: the light of a face turned straight away from the sun (SunShade.Light64 at k = -1, 45 of 64).
// Needs sunshade.gdshaderinc (sun_dir). Never included by gpusundepth.gdshader: GpuWorld copies the model pass's
// uniforms onto the sun pass by name, and the sun pass must not be handed its own picture.

uniform bool real_shadows = false;                 // on while the sun's picture is drawn this frame (GpuWorld.SunDepthFrame)
uniform sampler2D sun_depth_tex : filter_nearest;  // the sun's picture: r + g / 256 = depth, a = 1 where something is drawn
uniform vec3 shadow_box_centre = vec3(0.0);        // = the sun pass's sun_box_centre (world units, height whole)
uniform float shadow_box_size = 4096.0;            // = sun_box_size
uniform float shadow_box_depth = 8192.0;           // = sun_box_depth
// the close-up picture (GpuWorld NearDepth): the same mapping and centre, a smaller square, so finer pixels - a man's legs
// and arms, a pilot's helmet. A spot inside it (a few pixels in from its edge) reads it alone; outside, the big one.
uniform sampler2D near_depth_tex : filter_nearest;
uniform float shadow_near_size = 512.0;            // = the close-up pass's sun_box_size
uniform bool shadow_near_on = false;               // the close-up picture is drawn
uniform int shadow_force = 0;                      // a debug aid (HUNTER_SHADOW_FORCE): 1 = every tested spot in shadow, 2 = none

const float SHADOW_K = -1.0;                       // the light a shadowed spot keeps: a face turned straight away

// p: world units (x east, height whole, z north); bias: how much nearer the sun, in the shown units along its ray, a
// stored depth must be to throw shadow on p (the picture's own steps and slopes are not shadow) - given for the big
// picture's pixel; the close-up picture's is that much smaller, and so is the allowance.
bool in_sun_shadow(vec3 p, float bias) {
    if (!real_shadows) return false;
    if (shadow_force != 0) return shadow_force == 1;
    vec3 v = vec3(p.x, p.y * 0.5, p.z) - vec3(shadow_box_centre.x, shadow_box_centre.y * 0.5, shadow_box_centre.z);
    vec2 g = v.xz - sun_dir.xz * (v.y / max(sun_dir.y, 0.02));
    float d = 0.5 - dot(v, sun_dir) / shadow_box_depth;
    ivec2 px = textureSize(sun_depth_tex, 0);
    if (shadow_near_on) {
        ivec2 npx = textureSize(near_depth_tex, 0);
        vec2 nuv = vec2(0.5 + g.x / shadow_near_size, 0.5 - g.y / shadow_near_size);
        vec2 edge = vec2(4.0) / vec2(npx);                                       // 4 pixels in from its edge
        if (all(greaterThanEqual(nuv, edge)) && all(lessThan(nuv, vec2(1.0) - edge))) {
            vec4 n = texelFetch(near_depth_tex, ivec2(nuv * vec2(npx)), 0);
            if (n.a < 0.5) return false;                                         // nothing drawn there: lit (the big picture is not asked)
            float fine = (shadow_near_size / float(npx.x)) / (shadow_box_size / float(px.x));
            return n.r + n.g / 256.0 < d - bias * fine / shadow_box_depth;
        }
    }
    vec2 uv = vec2(0.5 + g.x / shadow_box_size, 0.5 - g.y / shadow_box_size);
    if (uv.x < 0.0 || uv.x >= 1.0 || uv.y < 0.0 || uv.y >= 1.0) return false;   // past the picture: no shadow
    vec4 s = texelFetch(sun_depth_tex, ivec2(uv * vec2(px)), 0);
    if (s.a < 0.5) return false;                                                // nothing drawn there
    return s.r + s.g / 256.0 < d - bias / shadow_box_depth;
}

// The bias for a face whose cosine to the sun is k (> 0): the picture holds one depth per pixel, and across half a
// pixel's diagonal (2.83 units allowed) a face tilted from the sun by angle a changes its depth by 2.83 tan(a). 1 shown
// unit for the depth's own steps (0.016), plus 3 tan(a) - all the way to SHADOW_EDGE_ON (1 degree from edge-on, tan
// 57: 173 units). The cap used to be tan 10 (6 degrees): a face nearer edge-on than that shadowed itself in diagonal
// stripes - a helicopter parked nose to the sun, its sides and tail. Nearer edge-on than SHADOW_EDGE_ON a face is not
// tested at all (it takes almost no sun; SHADOW_UNTESTED).
const float SHADOW_EDGE_ON = 0.0175;
const float SHADOW_UNTESTED = -1.0;
float shadow_face_bias(float k) {
    if (k < SHADOW_EDGE_ON) return SHADOW_UNTESTED;
    return 1.0 + 3.0 * sqrt(1.0 - k * k) / k;
}
