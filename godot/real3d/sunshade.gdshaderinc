// (setting sun_shading): the sun lights every face and ground square from its own angle
// to it - the card's twin of rules/SunShade.cs (Pick, GroundLight), checked against it number for number (CheckSun.cs).
// A face's shade comes back as a TAG for world_albedo: the palette index, + 32 for its Extra Half-Brite copy. A ground
// square keeps its own colour and gets a light to multiply it by (world_lit_hazed). Needs world_colour.gdshaderinc (pal_raw).

uniform bool sun_shading = false;
uniform bool sun_up = false;                     // the sun is up (Objects.SunOffset not null)
uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);      // toward the sun, unit length, x east / y up / z north (SunShade.Toward)

// SunShade.Families, flattened: each colour's ramp (dark to light by the palette) and where it sits in this list.
const int SUN_RAMP[14] = int[](1, 2, 3, 4, 6, 5, 7, 8, 9, 10, 11, 12, 13, 14);
const int SUN_FAMILY_AT[16] = int[](-1, 0, 0, 0, 0, 4, 4, 6, 7, 8, 8, 8, 11, 11, 11, -1);
const int SUN_FAMILY_LEN[16] = int[](0, 4, 4, 4, 4, 2, 2, 1, 1, 3, 3, 3, 3, 3, 3, 0);

int sun_half(int rgb) { return (rgb >> 1) & 0x777; }
int sun_luma(int rgb) { return 299 * ((rgb >> 8) & 15) + 587 * ((rgb >> 4) & 15) + 114 * (rgb & 15); }

int sun_light64(float k) {
    int kq = clamp(int(floor(k * 256.0 + 0.5)), -256, 256);
    return 64 + ((77 * kq + 512) >> 10);                                      // SunShade.Light64: 45 away, 64 edge-on, 83 full on
}

// The colour's ramp, dark to light (SunShade.Ladder: the members, then their halves, stably sorted by brightness),
// and the entry nearest target64 (SunShade.Position: a tie goes to the brighter, then the lower colour, then the
// full one).
int _sun_choose(int colour, int target64) {
    int at = SUN_FAMILY_AT[colour], n = SUN_FAMILY_LEN[colour];
    int tags[8]; int lumas[8];
    for (int i = 0; i < 8; i++) { tags[i] = 0; lumas[i] = 0; }
    for (int i = 0; i < n; i++) {
        int c = SUN_RAMP[at + i];
        tags[i] = c; lumas[i] = sun_luma(pal_raw[c]);
        tags[n + i] = c + 32; lumas[n + i] = sun_luma(sun_half(pal_raw[c]));
    }
    int m = 2 * n;
    for (int i = 1; i < m; i++)
        for (int j = i; j > 0; j--) {
            if (lumas[j - 1] <= lumas[j]) break;
            int t = tags[j]; tags[j] = tags[j - 1]; tags[j - 1] = t;
            int l = lumas[j]; lumas[j] = lumas[j - 1]; lumas[j - 1] = l;
        }
    int best = 0;
    for (int i = 1; i < m; i++) {
        int d = abs(64 * lumas[i] - target64), bd = abs(64 * lumas[best] - target64);
        bool better = lumas[i] != lumas[best] ? lumas[i] > lumas[best]
                    : ((tags[i] & 31) != (tags[best] & 31) ? (tags[i] & 31) < (tags[best] & 31) : (tags[i] < 32 && tags[best] >= 32));
        if (d < bd || (d == bd && better)) best = i;
    }
    return tags[best];
}

// A face of this paint with the sun's light k on it (SunShade.Pick). Black and the sky stay as they are.
int sun_pick(int colour, float k) {
    if (colour < 0 || colour > 15 || SUN_FAMILY_LEN[colour] == 0) return colour;
    return _sun_choose(colour, sun_luma(pal_raw[colour]) * sun_light64(k));
}

// ENHANCEMENT (PaintedLight): a wall whose paint has the painters' light in it, kp its light from their sun - lit by how
// much more or less the real sun gives it than theirs did, so with the sun where they painted it, it is its own paint.
uniform vec3 painter_dir = vec3(0.0, 1.0, 0.0);  // toward the painters' sun, as sun_dir (SunShade.PainterSun)
int sun_pick_painted(int colour, float k, float kp) {
    if (colour < 0 || colour > 15 || SUN_FAMILY_LEN[colour] == 0) return colour;
    return _sun_choose(colour, sun_luma(pal_raw[colour]) * sun_light64(k) * 64 / sun_light64(kp));
}

// ENHANCEMENT (SunShade.Over): when the sun asks for more than the ramp's lightest paint, sun_pick gives
// that paint and this is what to multiply it by in full colour (world_lit_hazed), at most sun_light64(1) / 64; else 1.
float _sun_over(int colour, int target64) {
    if (colour < 0 || colour > 15 || SUN_FAMILY_LEN[colour] == 0) return 1.0;
    int top = 0;
    for (int i = 0; i < SUN_FAMILY_LEN[colour]; i++) top = max(top, sun_luma(pal_raw[SUN_RAMP[SUN_FAMILY_AT[colour] + i]]));
    return target64 <= 64 * top ? 1.0 : min(float(target64) / float(64 * top), float(sun_light64(1.0)) / 64.0);
}
float sun_over(int colour, float k) {
    return colour < 0 || colour > 15 ? 1.0 : _sun_over(colour, sun_luma(pal_raw[colour]) * sun_light64(k));
}
float sun_over_painted(int colour, float k, float kp) {
    return colour < 0 || colour > 15 ? 1.0 : _sun_over(colour, sun_luma(pal_raw[colour]) * sun_light64(k) * 64 / sun_light64(kp));
}

// A ground square in the sun (SunShade.GroundLight): what its
// own colour is multiplied by, in full colour - the sun's light (sun_light64 / 64). Light is per ground point
// (ground_point_k), so neighbouring squares share it along their edge and the checker's colour step always shows there.
// (GROUND: RELIEF and GROUND VARIETY, a step on top of it, were retired 2026-11-01 - finer ground, island rise, the
// colour bands and this smooth sun do what they tried.)

// A lit ground square keeps the original's own colour (rules/SunShade.cs: the swap back to the dark checker colours was
// taken out 11-08 - it made the view far darker than the original).

// SunShade.GroundK: the sun on a ground point, smooth - its slope from the heights one square either side
// (hl / hr west / east, hb / hf at z - 512 / z + 512), in the picture's own units (x and z doubled).
float ground_point_k(int hl, int hr, int hb, int hf) { return dot(normalize(vec3(float(hl - hr), 2048.0, float(hb - hf))), sun_dir); }
float ground_sun_light(float k) { return float(sun_light64(k)) / 64.0; }
// What the ground is drawn with (SunShade.GroundLevel): its light over flat ground's, so flat land is the
// original's own colour and only slopes go lighter or darker - ground faces up, so the bare light never fell below 1.
float ground_sun_level(float k) { return ground_sun_light(k) / ground_sun_light(sun_dir.y); }
