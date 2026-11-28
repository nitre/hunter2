// (setting ground_colours, GROUND COLOURS: BANDS, the card-drawn 3D view): each ground
// pixel's colour from its own ground point (per pixel since 2026-11-02; the colours are still the stepped $0RGB words) - the twin of rules/GroundBands.cs (Colour, Brown64, Mix: the same whole numbers): the
// original map's own green or brown land, a ramp of its own colours up each by height (each band a set share of that
// land), green and brown blended across a square, sand at the shore, rock on steep sides, every second square a touch
// darker (the checker, enough to show movement). The pieces are the grain, so the specks are left off banded ground.
// Needs world_colour.gdshaderinc (shore_far), ground_height.gdshaderinc and ground_cell.gdshaderinc.

uniform bool ground_bands = false;
uniform float band_gain = 1.0;                   // GroundBands.Gain: the land as bright as the original's on average
uniform int band_green_h[3];                     // GroundBands.Heights(green): each key's height, world units
uniform int band_brown_h[3];                     // GroundBands.Heights(brown)
const int BAND_GREEN[3] = int[](0x251, 0x362, 0x372);   // GroundBands.GreenKeys' colours
const int BAND_BROWN[3] = int[](0x521, 0x732, 0xA53);   // GroundBands.BrownKeys' colours
// ENHANCEMENT (PLANET, THE SAND ZONE): the sand face's own keys - its sand where the green would be, its rock where the
// brown would be (rock = a cell coloured 9, SandZone.Rock; sand = 11), wet sand at the water and dry sand above it
uniform bool sand_face = false;
const int SAND_KEYS[3] = int[](0xB84, 0xC95, 0xDB7);
const int ROCK_KEYS[3] = int[](0x532, 0x643, 0x865);
int band_key(bool brown, int k) { return sand_face ? (brown ? ROCK_KEYS[k] : SAND_KEYS[k]) : (brown ? BAND_BROWN[k] : BAND_GREEN[k]); }
// Stuntin's flat island: DUST - the original's ground is sand (its renderHorizon's colour 13, $332 on its
// 0-7 scale, about $664): dusty keys, the colour varied over the flat land by soft patches (DUST_PATCH, DUST_FINE world
// units) and fine wind ripples (DUST_RIPPLE apart, bent by the patches), no checker; wet sand at the water
uniform bool dust_face = false;
const int DUST_KEYS[4] = int[](0x653, 0x764, 0x875, 0x986);
const float DUST_PATCH = 2600.0, DUST_FINE = 700.0, DUST_RIPPLE = 70.0;
float dust_hash(ivec2 p) {
    uint h = uint(p.x) * 374761393u + uint(p.y) * 668265263u;
    h = (h ^ (h >> 13u)) * 1274126177u;
    return float((h ^ (h >> 16u)) & 0xFFFFu) / 65535.0;
}
float dust_noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    ivec2 c = ivec2(i);
    return mix(mix(dust_hash(c), dust_hash(c + ivec2(1, 0)), f.x), mix(dust_hash(c + ivec2(0, 1)), dust_hash(c + ivec2(1, 1)), f.x), f.y);
}
uniform sampler2D band_shore : filter_linear, repeat_disable;   // ShoreDistance.BuildFromCoast: the distance from the sea as drawn, 8 samples a cell, 0..1 of shore_far

int band_mix(int a, int b, int step, int steps) {
    int w = 0;
    for (int sh = 8; sh >= 0; sh -= 4) {
        int ca = (a >> sh) & 15, cb = (b >> sh) & 15;
        w |= ((ca * steps + (cb - ca) * step + steps / 2) / steps) << sh;
    }
    return w;
}

// the dust's $0RGB at a ground point: patches and ripples to a share 0..1 along the keys, stepped (12 steps) as the bands are
int dust_word(ivec2 at, int shore, int h) {
    vec2 p = vec2(at);
    float blot = dust_noise(p / DUST_PATCH) * 0.65 + dust_noise(p / DUST_FINE + 17.0) * 0.35;
    float ripple = sin((p.x * 0.8 + p.y * 0.6) / DUST_RIPPLE + blot * 9.0);
    float s = clamp(blot * 1.1 - 0.05 + ripple * 0.06, 0.0, 0.999);
    int st = int(s * 12.0), k = st / 4;
    int land = band_mix(DUST_KEYS[k], DUST_KEYS[k + 1], st % 4, 4);
    int t = clamp(max(shore * 4 / 256, h * 4 / 24), 0, 4);
    return t == 0 ? 0x542 : band_mix(0x653, land, t - 1, 3);
}

// GroundBands.Ramp: four steps between a ramp's keys
int band_ramp_green(int e) {
    int k = band_green_h[1] <= e ? 1 : 0;
    return band_mix(band_key(false, k), band_key(false, k + 1), clamp((e - band_green_h[k]) * 4 / (band_green_h[k + 1] - band_green_h[k]), 0, 3), 4);
}
int band_ramp_brown(int e) {
    int k = band_brown_h[1] <= e ? 1 : 0;
    return band_mix(band_key(true, k), band_key(true, k + 1), clamp((e - band_brown_h[k]) * 4 / (band_brown_h[k + 1] - band_brown_h[k]), 0, 3), 4);
}

// GroundBands.Colour: the point's $0RGB
int ground_band_word(int h, int bend, int slope, int shore, int brown64) {
    if (slope >= 256) return band_mix(0x554, 0x998, clamp(h * 4 / max(max(band_green_h[2], band_brown_h[2]), 1), 0, 3), 4);
    int e = h + bend / 8;
    int land = band_mix(band_ramp_green(e), band_ramp_brown(e), (brown64 * 4 + 32) / 64, 4);
    int t = clamp(max(shore * 4 / 256, h * 4 / 24), 0, 4);   // the beach: ShoreReach, ShoreHigh
    if (sand_face) return t == 0 ? 0xA86 : band_mix(0xDB8, land, t - 1, 3);
    return t == 0 ? 0x974 : band_mix(0xCA6, land, t - 1, 3);
}

// GroundBands.Brown64: how brown the land is at (x, z), 0..64, the squares' kinds blended between square middles
int band_brown_at(sampler2D types, int c, int r, int own) {
    int t = ground_cell_colour(types, clamp(c, 0, 127), clamp(r, 0, 127));
    if (sand_face) return (t < 9 || t > 14) ? own : (t == 9 ? 64 : 0);
    return (t < 9 || t > 14) ? own : (t <= 11 ? 64 : 0);
}
int band_brown64(sampler2D types, int x, int z) {
    int t = ground_cell_colour(types, clamp(x / 512, 0, 127), clamp(z / 512, 0, 127));
    int own = sand_face ? (t == 9 ? 64 : 0) : (t >= 9 && t <= 11) ? 64 : 0;
    int gx = x + 256, gz = z + 256, c0 = gx / 512 - 1, r0 = gz / 512 - 1, fx = gx % 512, fz = gz % 512;
    return ((band_brown_at(types, c0, r0, own) * (512 - fx) + band_brown_at(types, c0 + 1, r0, own) * fx) * (512 - fz)
          + (band_brown_at(types, c0, r0 + 1, own) * (512 - fx) + band_brown_at(types, c0 + 1, r0 + 1, own) * fx) * fz) / (512 * 512);
}

// GroundBands.PointSlope and Slope: the corners' slopes blended across the square
int band_point_slope(sampler2D hmap, int c, int r) {
    int hl = ground_point(hmap, clamp(c - 1, 0, 127), clamp(r, 0, 127)), hr = ground_point(hmap, clamp(c + 1, 0, 127), clamp(r, 0, 127));
    int hb = ground_point(hmap, clamp(c, 0, 127), clamp(r - 1, 0, 127)), hf = ground_point(hmap, clamp(c, 0, 127), clamp(r + 1, 0, 127));
    int a = abs(hr - hl) / 2, b = abs(hf - hb) / 2;
    return max(a, b) + (min(a, b) >> 4);
}

int band_slope(sampler2D hmap, int x, int z) {
    int c = x >> 9, r = z >> 9, fx = x & 511, fz = z & 511;
    int s00 = band_point_slope(hmap, c, r), s10 = band_point_slope(hmap, c + 1, r);
    int s01 = band_point_slope(hmap, c, r + 1), s11 = band_point_slope(hmap, c + 1, r + 1);
    return ((s00 * (512 - fx) + s10 * fx) * (512 - fz) + (s01 * (512 - fx) + s11 * fx) * fz) >> 18;
}

bool ground_banded(int colour) { return ground_bands && colour >= 9 && colour <= 14; }

// The band colour at one ground point (the colours stay the stepped words)
int ground_point_word(sampler2D hmap, sampler2D types, ivec2 mid, int h) {
    int bend = 4 * h;
    ivec2 offs[4] = ivec2[](ivec2(1024, 0), ivec2(-1024, 0), ivec2(0, 1024), ivec2(0, -1024));
    for (int i = 0; i < 4; i++) {
        ivec2 o = clamp(mid + offs[i], ivec2(0), ivec2(65535));
        bend -= ground_height(hmap, o.x, o.y);
    }
    int slope = band_slope(hmap, mid.x, mid.y);
    int shore = int(texture(band_shore, vec2(mid) / 65536.0).r * shore_far);
    if (dust_face) return dust_word(mid, shore, h);
    return ground_band_word(h, bend, slope, shore, band_brown64(types, mid.x, mid.y));
}

// The checker (GroundBands.Checker): every second square times 60 / 64
float ground_band_checker(int col, int row) { return dust_face || ((col + row) & 1) != 0 ? 1.0 : 60.0 / 64.0; }

// The piece's colour, faded as the palette, times its light and the checker, then the haze (world_lit_hazed's steps)
vec3 ground_band_albedo(int word, float depth, float lit_light) {
    int c = view_faded(word, fade_level);
    vec3 rgb = nibbles(death_step > 0 ? death_faded(c, death_step) : c) * 17.0;
    vec3 lit_rgb = min(rgb * lit_light * band_gain, vec3(255.0));
    return to_linear(floor(mix(lit_rgb, sky_nib(5) * 17.0, haze_amount(depth)) + 0.5) / 255.0);
}
