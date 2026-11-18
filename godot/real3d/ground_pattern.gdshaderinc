// ENHANCEMENT, NOT THE ORIGINAL (setting ground_pattern, GROUND PATTERN: SPECKS, the card-drawn 3D view): fine specks inside
// every ground square - a 16 x 16 grid of texels (nearest, blocky), most the square's plain colour, some a touch lighter or
// darker (the lit colour times ground_speck_dark / ground_speck_light, never a palette neighbour). Each square has its own
// layout, a hash of its map column and row and the texel. The haze fades the specks to the plain colour. Needs world_colour.gdshaderinc.

uniform bool ground_pattern = false;
const float ground_speck_dark = 0.94;
const float ground_speck_light = 1.06;
const int ground_speck_dark_share = 12;      // percent of texels
const int ground_speck_light_share = 12;

// The speck factor of the texel at `at` (world x and z in squares of 512): 1, the dark or the light factor.
float ground_speck(vec2 at) {
    ivec2 sq = ivec2(floor(at));
    ivec2 t = clamp(ivec2(floor(fract(at) * 16.0)), ivec2(0), ivec2(15));
    uint h = uint(sq.x) * 0x9E3779B1u ^ uint(sq.y) * 0x85EBCA77u ^ uint(t.x) * 0xC2B2AE3Du ^ uint(t.y) * 0x27D4EB2Fu;
    h ^= h >> 15u; h *= 0x2C1B3C6Du; h ^= h >> 12u; h *= 0x297A2D39u; h ^= h >> 15u;
    int v = int(h % 100u);
    return v < ground_speck_dark_share ? ground_speck_dark : (v < ground_speck_dark_share + ground_speck_light_share ? ground_speck_light : 1.0);
}

// world_lit_hazed for a specked texel: the colour times the light and the speck factor, faded to the plain lit colour by
// the haze amount, then the haze as always.
vec3 world_speck_hazed8(int idx, vec4 fragcoord, float depth, float lit_light, float speck) {     // 0..255 a channel
    vec3 plain = min(world_rgb8(idx, fragcoord) * lit_light, vec3(255.0));
    vec3 shade = min(world_rgb8(idx, fragcoord) * lit_light * speck, vec3(255.0));
    float f = haze_amount(depth);
    return floor(mix(mix(shade, plain, f), sky_nib(5) * 17.0, f) + 0.5);
}

vec3 world_speck_hazed(int idx, vec4 fragcoord, float depth, float lit_light, float speck) {
    return to_linear(world_speck_hazed8(idx, fragcoord, depth, lit_light, speck) / 255.0);
}
