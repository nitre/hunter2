// The 3D world's colouring, shared by world.gdshader (ground, sky, sea) and the card-built shaders (ground, things):
// a face's colour is a TAG, the palette index 0-15; index 15 - the sky and the open sea, the original's cleared
// view under the copper - takes the colour of its screen row's sky band ($62B0's six band rows).
// The palette arrives RAW, as the game holds it - bank 2's colours and the sky's band colours as $0RGB words -
// with the fade level, the sky's fade level and the death-fade step; the fades are the game's own maths
// (fade.gdshaderinc: Play.Faded $6A14, the death fade $6AC2), done here.

#include "res://real3d/fade.gdshaderinc"
#include "res://real3d/sky.gdshaderinc"

uniform int pal_raw[16];     // bank 2 colours 0-14 as $0RGB words; 15 unused (the sky)
uniform int sky_raw[7];      // colour 15: the bank's colour, then the 6 band colours, as $0RGB words
uniform int fade_level = 15;     // the screen's fade, 0 (black) .. 15 (Play.ScreenFade)
uniform int sky_level = 15;      // the sky's own fade (Play.SkyLevel)
uniform int death_step = 0;      // the death fade's step, 0 = none
uniform int day_level = 15;      // the hour's light ($18F, WorldState.DaylightLevel): fire is not darkened by the night
uniform int height0e = 0;    // the camera's height ($E): the shader works the 6 band rows out itself (sky.gdshaderinc)
uniform bool sky_steps = false;                  // SKY: SMOOTH - the stepped full-colour bands (sky_aga), not the 6 flat ones
uniform sampler2D sky_tex : filter_nearest;      // the A500 gradient's 140 row colours: the overlay map's marks only (mapmarks.gdshader)
uniform float gpu_px = 1.0;                      // viewport pixels per game row
// The view drawn at the screen's own pixels in the TV looks, where a game pixel is wider than tall (16/15, Display):
// gpu_px_x viewport pixels per game column (= gpu_px with square pixels), and gpu_wide = gpu_px_x / gpu_px, the
// projection's sideways squeeze so the wider viewport shows the same world as a square-pixel one (Camera3D keeps the
// height: more width would otherwise be more world). Real3DView sets all three.
uniform float gpu_px_x = 1.0;
uniform float gpu_wide = 1.0;

// Each 3D pass's vertex() starts with PROJECTION_MATRIX = wide_projection(PROJECTION_MATRIX). The camera keeps its
// height (Real3DView: frustum, KeepAspect Height), so a viewport gpu_wide times wider only divides the sideways scale
// [0][0] by gpu_wide (the frustum's sideways offset term [2][0] stays as it is): this puts it back.
mat4 wide_projection(mat4 p) { p[0][0] *= gpu_wide; return p; }

vec3 nibbles(int c) { return vec3(float((c >> 8) & 15), float((c >> 4) & 15), float(c & 15)); }

uniform int smooth_level = -1;   // SMOOTH daylight's level in 60ths (Play.SmoothDrawLevel), -1 = the original's levels

// A view colour at the screen's fade, or at SMOOTH daylight's level (Play.ScreenOf's Lit / SkyLit).
int view_faded(int c, int level) { return smooth_level >= 0 ? faded60(c, smooth_level) : faded(c, level); }

vec3 view_nib(int i) {
    int c = view_faded(pal_raw[i], fade_level);
    return nibbles(death_step > 0 ? death_faded(c, death_step) : c);
}

vec3 sky_nib(int k) {                 // the death fade walks the sky from its raw colour, as Play.ScreenOf (DeathFadeTests)
    return nibbles(death_step > 0 ? death_faded(sky_raw[k], death_step) : view_faded(sky_raw[k], sky_level));
}

vec3 to_linear(vec3 c) {     // the nibble colour is a display (sRGB) colour; spatial output is linear
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

vec3 to_display(vec3 l) {    // to_linear's inverse: a linear colour read back off the screen, as the display colour 0..1
    return mix(l * 12.92, 1.055 * pow(max(l, vec3(0.0)), vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), l));
}

// The palette colour at half brightness, as Extra Half-Brite shows colour n + 32: the faded register's channels halved.
vec3 view_nib_half(int i) {
    int c = view_faded(pal_raw[i], fade_level);
    c = death_step > 0 ? death_faded(c, death_step) : c;
    return nibbles((c >> 1) & 0x777);
}

// ENHANCEMENT (the card): fire is not darkened by the night - the screen's fade less the hour's (a fade to black
// still takes it), the death fade kept.
vec3 view_nib_fire(int i) {
    int c = faded(pal_raw[i], min(15, fade_level + 15 - day_level));
    return nibbles(death_step > 0 ? death_faded(c, death_step) : c);
}

// (SKY: SMOOTH's time of day; docs/sky-reference.md): the sky's six colours (sky_raw 0..5, top to the horizon) worked out from the sun's HEIGHT
// (tod_elev, degrees: the game's sun curve carried on past 06:00 and 20:00, Real3DView.SunPath) and the pixel column's
// DIRECTION from it (tod_d) - day (10 degrees up and more) exactly the original's; golden hour (10 .. 0) the sun's side
// warming to red-orange at the horizon; civil twilight (0 .. -6, the blue hour at -4 .. -6) a deeper blue (ozone's
// Chappuis bands), a low red glow under the sun, Earth's shadow and the pink Belt of Venus opposite (strongest at -2, gone
// by -5); nautical (-6 .. -12) still blue, the glow sinking; astronomical (-12 .. -18) to the night's near black. At full
// light: it carries the hour's darkness itself, so only a fade to black and the death fade take it (as fire, view_nib_fire).
// Full colour across the screen: the fade toward and away from the sun is smooth (tod_lit) - on the Amiga's 12-bit steps
// each band broke into blocks wherever a channel stepped. The sea (sky_raw 6) is the water's own colour lit by this sky (tod_sea_nib).
uniform bool tod_on = false;
uniform float tod_elev = 60.0;      // the sun's height, degrees
uniform float tod_sun_az = 0.0;     // the sun's direction in the look turn's sense (0 north, + left)
uniform float tod_yaw = 0.0;        // the look turn this frame (Real3DView.Yaw / the free camera's)

const int TOD_DAY[6] = int[6](0x006, 0x118, 0x22A, 0x33A, 0x44C, 0x55F);    // the original's own (palette bank 2 colour 15, the copper's 5)
const int TOD_BLUE[6] = int[6](0x002, 0x013, 0x124, 0x135, 0x246, 0x358);   // blue hour: deeper, more saturated
const int TOD_NIGHT[6] = int[6](0x000, 0x001, 0x001, 0x002, 0x002, 0x113);  // night: near black, the faintest glow low down
const int TOD_GLOW[6] = int[6](0x125, 0x236, 0x457, 0x987, 0xE95, 0xF82);   // the sun's side at sunset: grey-blue to red-orange (~2000-3000 K)
const int TOD_EMBER[6] = int[6](0x013, 0x124, 0x235, 0x346, 0x634, 0x942);  // the sun's side in the blue hour: a low deep red
const int TOD_BELT[6] = int[6](0x124, 0x236, 0x979, 0xB89, 0x537, 0x426);   // away from the sun: the Belt of Venus over Earth's shadow

float tod_smooth(float a, float b, float x) { float t = clamp((x - a) / (b - a), 0.0, 1.0); return t * t * (3.0 - 2.0 * t); }

// The direction of viewport column fx from the sun's, radians (the clouds' own column turn, clouds.gdshaderinc cloud_in).
float tod_d(float fx) { return tod_yaw - atan((floor(fx / gpu_px_x) + 0.5 - 160.0) / 256.0) - tod_sun_az; }

// Band k's colour (0 the top .. 5 at the horizon) d radians from the sun's direction, at full light: nibbles 0..15, unrounded.
vec3 tod_band(int k, float d) {
    float e = tod_elev;
    vec3 day = nibbles(TOD_DAY[k]), blue = nibbles(TOD_BLUE[k]), night = nibbles(TOD_NIGHT[k]);
    vec3 glow = nibbles(TOD_GLOW[k]), ember = nibbles(TOD_EMBER[k]), belt = nibbles(TOD_BELT[k]);
    float c = cos(d), toward = pow(0.5 + 0.5 * c, 3.0), away = pow(0.5 - 0.5 * c, 3.0);
    if (e >= 10.0) return day;
    if (e >= 0.0) {                                                      // golden hour
        vec3 g = mix(day, glow, (10.0 - e) / 10.0 * toward);
        return mix(g, belt, 0.5 * tod_smooth(2.0, 0.0, e) * away);
    }
    if (e >= -6.0) {                                                     // civil twilight, the blue hour at its end
        float f = -e / 6.0;
        vec3 g = mix(mix(day, blue, tod_smooth(0.0, 1.0, f)), mix(glow, ember, f), toward * (1.0 - 0.3 * f));
        return mix(g, belt, max(0.0, 1.0 - abs(e + 2.0) / 3.0) * away);
    }
    if (e >= -12.0) {                                                    // nautical
        float f = (-6.0 - e) / 6.0;
        return mix(mix(blue, night, 0.6 * f), ember, toward * (1.0 - f) * 0.7);
    }
    if (e >= -18.0) return mix(mix(blue, night, 0.6), night, (-12.0 - e) / 6.0);   // astronomical
    return night;
}

// A full-light colour (nibbles, unrounded), then the screen's fade less the hour's and the death fade. Full colour: the two
// 12-bit colours either side of it faded by the game's own maths (Play.Faded), mixed by how far it lies between them - a
// whole colour (the original's day sky, the night) is exactly the game's; the death fade keeps its 12-bit steps.
vec3 tod_lit(vec3 n) {
    int level = clamp(fade_level + 15 - day_level, 0, 15);
    vec3 c = clamp(n, vec3(0.0), vec3(15.0));
    if (death_step > 0) {
        ivec3 q = ivec3(floor(c + 0.5));
        return nibbles(death_faded(faded(q.r << 8 | q.g << 4 | q.b, level), death_step));
    }
    ivec3 lo = ivec3(floor(c)), hi = min(lo + 1, ivec3(15));
    return mix(nibbles(faded(lo.r << 8 | lo.g << 4 | lo.b, level)), nibbles(faded(hi.r << 8 | hi.g << 4 | hi.b, level)), c - vec3(lo));
}

// The water's own colour lit by the sky: the
// original sea colour (sky_raw 6 at full light) times the whole sky's light now over its light by day - mostly how bright,
// a quarter of its hue. The whole sky: every band toward, across and away from the sun.
vec3 tod_sea_nib() {
    vec3 now = vec3(0.0), day = vec3(0.0);
    for (int k = 0; k < 6; k++) {
        now += tod_band(k, 0.0) + 2.0 * tod_band(k, 1.5707963) + tod_band(k, 3.1415927);
        day += 4.0 * nibbles(TOD_DAY[k]);
    }
    vec3 r = (now + 2.0) / (day + 2.0);
    float lum = sqrt((now.r + now.g + now.b) / (day.r + day.g + day.b));
    return tod_lit(nibbles(sky_raw[6]) * lum * (0.75 + 0.25 * r / ((r.r + r.g + r.b) / 3.0)));
}

// The sky's colour k (0..5 the bands, 6 the sea) at viewport column fx: the time of day's when on, else sky_nib's.
vec3 sky_nib_x(int k, float fx) {
    if (!tod_on) return sky_nib(k);
    return k >= 6 ? tod_sea_nib() : tod_lit(tod_band(k, tod_d(fx)));
}

// (SKY: SMOOTH on the card, full colour): the original's six bands, no steps between
// - each line halfway between the
// original's and an even spread from the view's top to the horizon (rules/Sky.cs SmoothBands), so the sky lightens
// all the way down. The horizon's line stays. 0..255 a channel; CheckSky.AgaRef is the same.
int sky_smooth_row(int height, int k) { int h = sky_band_row(height, 5); return (sky_band_row(height, k) + h * k / 5) / 2; }

vec3 sky_six_at(int y, int height, float fx) {                      // the living sea's mirror keeps this six-band sky
    vec3 c = sky_nib_x(0, fx) * 17.0;
    for (int k = 0; k < 6; k++) if (y > sky_smooth_row(height, k)) c = sky_nib_x(k + 1, fx) * 17.0;
    return c;
}

// (SKY: SMOOTH, the sky itself): picked 10 bands from a mock-up. View row 0
// down to the horizon line; each band 0.8 the height of the one above (edges on whole game rows); colours evenly in OKLab
// from the top colour to the horizon's paler one (band 5 (the last sky band) colour mixed 0.25 toward its brightest channel), each
// channel rounded to the Amiga's 12-bit (a multiple of 17). Below the horizon the sea's colour. rules/Sky.cs TenEdges, TenColours.
vec3 sky_ok_lab(vec3 c8) {
    vec3 c = to_linear(c8 / 255.0);
    vec3 q = pow(vec3(dot(c, vec3(0.4122214708, 0.5363325363, 0.0514459929)), dot(c, vec3(0.2119034982, 0.6806995451, 0.1073969566)),
                      dot(c, vec3(0.0883024619, 0.2817188376, 0.6299787005))), vec3(1.0 / 3.0));
    return vec3(dot(q, vec3(0.2104542553, 0.7936177850, -0.0040720468)), dot(q, vec3(1.9779984951, -2.4285922050, 0.4505937099)),
                dot(q, vec3(0.0259040371, 0.7827717662, -0.8086757660)));
}

vec3 sky_ok_rgb8(vec3 lab) {          // rounded to a multiple of 17 - not while the low sun's glow runs across the screen (tod_lit)
    vec3 q = vec3(lab.x + 0.3963377774 * lab.y + 0.2158037573 * lab.z, lab.x - 0.1055613458 * lab.y - 0.0638541728 * lab.z,
                  lab.x - 0.0894841775 * lab.y - 1.2914855480 * lab.z);
    q = q * q * q;
    vec3 l = vec3(dot(q, vec3(4.0767416621, -3.3077115913, 0.2309699292)), dot(q, vec3(-1.2684380046, 2.6097574011, -0.3413193965)),
                  dot(q, vec3(-0.0041960771, -0.7034186147, 1.7076147010)));
    vec3 n = clamp(to_display(l) * 15.0, vec3(0.0), vec3(15.0));
    return (tod_on && tod_elev < 10.0 ? n : floor(n + 0.5)) * 17.0;   // by day (and with the time of day off) the 12-bit colours, exactly
}

vec3 sky_aga_at(int y, int height, float fx) {                      // y: the game row; fx: the viewport column (SKY's time of day)
    int h = sky_band_row(height, 5);
    if (y > h) return sky_nib_x(6, fx) * 17.0;                       // the sea's colour (band 6); the sky's last colour is band 5's
    vec3 c5 = sky_nib_x(5, fx) * 17.0;
    vec3 lab0 = sky_ok_lab(sky_nib_x(0, fx) * 17.0), lab1 = sky_ok_lab(mix(c5, vec3(max(c5.r, max(c5.g, c5.b))), 0.25));
    int share[9] = int[9](14684, 26431, 35829, 43347, 49361, 54173, 58022, 61102, 63565);   // running share of 65536 (0.8^k weights)
    int k = 0;
    for (int j = 0; j < 9; j++) if (y >= ((h + 1) * share[j] + 32768) >> 16) k = j + 1;
    return sky_ok_rgb8(mix(lab0, lab1, float(k) / 9.0));
}

vec3 sky_aga(float y, float fx) { return sky_aga_at(int(floor(y / gpu_px)), height0e, fx); }   // y: the viewport row

// Band k's line in viewport rows, on the original's own rows (the band shows from the row after it): a game row y is
// past it <=> viewport row >= (r + 1) * gpu_px, so a band starts on the same game row at every scale. Line 5 is the
// sea's edge: the original's horizon drops below the eye line as the camera climbs (sky_horizon), as a round sea's
// edge would, and the sea plane shows sky above it (world.gdshader).
float sky_line(int k) { return float(sky_band_row(height0e, k) + 1) * gpu_px - 1.0; }

vec3 sky_flat(float y, float fx) {    // the original's six flat bands at viewport row y (fx: the column, SKY's time of day)
    vec3 nib = sky_nib_x(0, fx);
    for (int k = 0; k < 6; k++) if (y > sky_line(k)) nib = sky_nib_x(k + 1, fx);
    return nib * 17.0;
}

// idx 0-14 the palette, 15 the sky band at this pixel row, 16 the sea (the last band's colour); 32-46 palette colour
// idx - 32 at half brightness (the sun's shades, sunshade.gdshaderinc); 64-78 colour idx - 64 as fire (not darkened).
// The colour as 0..255 a channel (world_albedo's, before the display's linear step).
vec3 world_rgb8(int idx, vec4 fragcoord) {
    if (idx >= 64) return view_nib_fire(idx - 64) * 17.0;
    if (idx >= 32) return view_nib_half(idx - 32) * 17.0;
    vec3 nib = idx < 15 ? view_nib(idx) : idx == 16 ? sky_nib_x(6, fragcoord.x) : sky_nib(0);
    if (idx == 15) return sky_steps ? sky_aga(floor(fragcoord.y), fragcoord.x) : sky_flat(floor(fragcoord.y), fragcoord.x);
    return nib * 17.0;
}

// The sky the living sea mirrors: SKY: SMOOTH's six bands (not the sky's ten), or the flat ones.
vec3 sky_sea_rgb8(float y, float fx) { return sky_steps ? sky_six_at(int(floor(y / gpu_px)), height0e, fx) : sky_flat(y, fx); }

vec3 world_albedo(int idx, vec4 fragcoord) { return to_linear(world_rgb8(idx, fragcoord) / 255.0); }

// (row HAZE, the card-drawn 3D view): a thing depth world units ahead of the eye fades
// toward the light sky just above the horizon (sky_nib(5)), over the last quarter before the view's far edge (haze_far, Real3DView: the chosen
// 3D VIEW DISTANCE's last row) to all the way there - only enough to hide things coming in; nearer, things keep their
// own colours (was from halfway).
// BANDS: 4 clear steps; FINE: every step the colours allow - the A500's 16 levels a channel, the A1200's 256
// (COLOURS). Off (haze_mode 0) and indoors: the colour as it is.
uniform int haze_mode = 0;                       // 0 off, 1 bands, 2 fine
uniform float haze_far = 3200.0;                 // world units ahead of the eye where the drawn ground ends

float haze_amount(float depth) {
    if (haze_mode == 0) return 0.0;
    float f = clamp((depth - 0.75 * haze_far) / (0.25 * haze_far), 0.0, 1.0);
    return haze_mode == 1 ? floor(f * 4.0) / 4.0 : f;
}

vec3 world_hazed8(int idx, vec4 fragcoord, float depth) {        // 0..255 a channel
    vec3 c = world_rgb8(idx, fragcoord);
    float f = haze_amount(depth);
    if (f <= 0.0) return c;
    vec3 m = mix(c, sky_nib_x(5, fragcoord.x) * 17.0, f);
    return floor(m + 0.5);                                         // full colour
}

vec3 world_hazed(int idx, vec4 fragcoord, float depth) { return to_linear(world_hazed8(idx, fragcoord, depth) / 255.0); }

// SUN SHADING's ground (sunshade.gdshaderinc ground_sun_light): the colour times its light, in full colour, then the haze.
vec3 world_lit_hazed(int idx, vec4 fragcoord, float depth, float lit_light) {
    vec3 lit_rgb = min(world_rgb8(idx, fragcoord) * lit_light, vec3(255.0));
    return to_linear(floor(mix(lit_rgb, sky_nib_x(5, fragcoord.x) * 17.0, haze_amount(depth)) + 0.5) / 255.0);
}

// Setting SEA: LIVING, the card's sea (ideas 2, 3 and 4 of the eight sea ideas): the sea was ONE flat colour - index 16, the last sky band, the same everywhere.
// Now each pixel's colour is worked out from the hour's own sea colour, so it darkens with the daylight: out at sea
// the original's colour, lighter in the shallows by the coast (2), two slow crossing waves lighten and darken it,
// quiet, not a storm (3), and near the horizon the water mirrors the sky actually drawn about the eye line, so dawn
// water looks like dawn (4); the haze takes the result as every other colour. Full colour, 256 levels a channel,
// (SEA: ORIGINAL is the original).
// world.gdshader passes the pixel's world x and z (at: the map's x and minus its z, as the ground mesh); fragcoord
// as everywhere here (world_rgb8).
uniform bool sea_living = false;      // SEA: ORIGINAL (index 16, flat) / LIVING / CYCLING (also true for CYCLING)
uniform bool sea_cycle = false;       // SEA: CYCLING - blocky 8 x 8 tile, a 32-colour ramp rotating on the game clock, 3 shallow bands
uniform float sea_cycle_rate = 12.0;  // CYCLING: ramp steps a second (every second game tick: the motion glides)
uniform float sea_deep = 1200.0;      // (2) world units from the coast: the shallows' pale fading to the open sea's colour
uniform float sea_shallow_lift = 1.6; // (2) the shallows: the sea's colour times this at the coast
uniform float sea_wave_depth = 0.10;  // (3) the waves lighten / darken the colour by up to this share
uniform float sea_mirror = 62.0;      // (4) game rows below the eye line over which the sky mirrors (its own 62)
uniform float sea_mirror_share = 0.8; // (4) how much of the sky it mirrors at the eye line
uniform float sea_mirror_wobble = 6.0; // (4) CYCLING: game rows the ripple moves the mirrored row up or down
uniform sampler2D shore_tex : filter_linear, repeat_disable;   // (2) rules/ShoreDistance per cell, 0..1 of shore_far
uniform float shore_far = 2048.0;     // ShoreDistance.Far
uniform float sea_clock = 0.0;        // (3) the game's own seconds (Play.Blanks / 50): stops while paused, as everything else

vec3 sea_rgb8(vec4 fragcoord, vec2 at, float depth) {
    float sea_row = floor(fragcoord.y);
    float sea_eye = sky_line(5);                                                                    // the sea's edge, viewport rows
    vec2 sea_uv = vec2(at.x, -at.y) / 65536.0;                                                      // the map's 128 cells of 512
    float sea_shore = all(greaterThanEqual(sea_uv, vec2(0.0))) && all(lessThanEqual(sea_uv, vec2(1.0))) ? texture(shore_tex, sea_uv).r * shore_far : shore_far;
    vec3 sea_open = sky_nib_x(6, fragcoord.x) * 17.0;
    vec3 sea_rgb;
    float sea_ripple = 0.0;                                                                         // CYCLING: -1..1, the texel's place on the ramp
    if (sea_cycle) {
        // CYCLING (the Amiga's colour cycling): 3 clear bands by the coast, each a fixed ramp of 32 colours (a triangle,
        // 85% .. 115% of the band's colour, so the loop closes); each tile texel sits at its own place on the ramp and
        // all step round together on the game clock - the texels stay blocky, the movement glides
        float sea_band_lift = sea_shore < sea_deep / 3.0 ? sea_shallow_lift : sea_shore < sea_deep * 2.0 / 3.0 ? 1.0 + 0.5 * (sea_shallow_lift - 1.0) : 1.0;
        vec3 sea_base = floor(sky_nib_x(6, fragcoord.x) * sea_band_lift + 0.5) * 17.0;
        ivec2 sea_t = clamp(ivec2(floor(fract(at / 512.0) * 8.0)), ivec2(0), ivec2(7));                   // the tile's texel: 8 x 8 per 512 world units
        int sea_k = ((sea_t.x * 5 + sea_t.y * 3 + ((sea_t.x * sea_t.y) & 3) * 4) + int(floor(sea_clock * sea_cycle_rate))) & 31;
        float sea_up = float(sea_k < 16 ? sea_k : 31 - sea_k) / 15.0;                                       // 0..1..0 round the ramp
        sea_rgb = clamp(floor(sea_base * (0.85 + 0.30 * sea_up) + 0.5), vec3(0.0), vec3(255.0));
        sea_ripple = sea_up * 2.0 - 1.0;
    } else
    sea_rgb = mix(min(sea_open * sea_shallow_lift, vec3(255.0)), sea_open, clamp(sea_shore / sea_deep, 0.0, 1.0));   // (2) pale by the coast, the original's out at sea
    float sea_down = clamp((sea_row - sea_eye) / (sea_mirror * gpu_px), 0.0, 1.0);                                  // (4) 0 at the eye line, 1 at sea_mirror rows down
    // the ripple breaks the mirror by the shore: clean horizontal bands on
    // the open sea, its strength and its row wobbling with the ripple in the shallows
    float sea_near = 1.0 - clamp(sea_shore / sea_deep, 0.0, 1.0);
    float sea_mirror_amount = sea_mirror_share * (1.0 - sea_down) * mix(1.0, 0.6 + 0.2 * (sea_ripple + 1.0), sea_near);
    if (sea_mirror_amount > 0.0) {
        float sea_row_mirrored = 2.0 * sea_eye - sea_row + sea_ripple * sea_mirror_wobble * gpu_px * sea_near;        // the row mirrored about the eye line
        sea_rgb = mix(sea_rgb, sky_sea_rgb8(floor(sea_row_mirrored), fragcoord.x), sea_mirror_amount);                    // the sky drawn at that row (SKY: SMOOTH's six bands)
    }
    if (!sea_cycle) {
    float sea_wave = sin(dot(at, vec2(0.80, 0.60)) * 0.028 + sea_clock * 0.40) * 0.6                     // (3) two slow crossings, quiet
               + sin(dot(at, vec2(-0.61, 0.79)) * 0.047 - sea_clock * 0.55) * 0.4;
    sea_rgb = clamp(sea_rgb * (1.0 + sea_wave_depth * sea_wave), vec3(0.0), vec3(255.0));
    }
    sea_rgb = mix(sea_rgb, sky_nib_x(5, fragcoord.x) * 17.0, haze_amount(depth));                                          // HAZE, as every other colour (world_hazed8)
    return floor(sea_rgb + 0.5);
}

vec3 sea_albedo(vec4 fragcoord, vec2 at, float depth) { return to_linear(sea_rgb8(fragcoord, at, depth) / 255.0); }
