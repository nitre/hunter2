// (part of CLOUDS, the card-drawn 3D view): steady
// single-pixel STARS and a blocky MOON going through its phases, on the sky backdrop only, so land, sea, things and the
// clouds are all in front of them. Fixed to world directions like the cirrus (they slide with the Q / E turn, never with
// walking). Stars come out as the hour's light falls (night_amount: none at level 12 and up, all at 8 and below - the
// light table $C4A4 gives 11 at 20:00, 10 at 22:00, 8 at 23:00 ... 7 at 04:00, 9 at 05:00, 11 at 06:00), the brightest
// first. The moon mirrors the sun's arc (rules/Objects.cs SunOffset): it rises in the east at 20:00, is highest at
// 01:00 in the south and sets in the west at 06:00 (moon_t 0..1, Real3DView.Frame). Its phase (moon_phase, 0 new, 0.5
// full) steps one eighth a night on the game's day counter, full on the first night. No moonlight on the ground: the
// night's light stays the original's. They are lit as fire is (view_nib_fire): not darkened by the night, but a fade to
// black and the death fade still take them.
// Needs world_colour.gdshaderinc and clouds.gdshaderinc (cloud_on, cloud_yaw, _cloud_hash) first.

uniform float moon_t = -1.0;            // how far through the night the moon is, 0 rising .. 1 setting; < 0 not up
uniform float moon_phase = 0.5;         // 0 new, 0.25 first quarter, 0.5 full, 0.75 last quarter

const int STAR_TURN = 1608;             // star columns in a full turn: 2 pi * 256, one a game pixel at the view's middle
const float MOON_R = 4.0;               // the moon's radius in game pixels: 8 across

// 0 none .. 1 every star out: with SKY's time of day from the sun's height (the first in nautical twilight at -6 degrees,
// all by -15, docs/sky-reference.md), else from the hour's light (SMOOTH daylight's 60ths when on).
float night_amount() {
	if (tod_on) return clamp((-6.0 - tod_elev) / 9.0, 0.0, 1.0);
	float l = smooth_level >= 0 ? float(smooth_level) / 4.0 : float(day_level);
	return clamp((12.0 - l) / 4.0, 0.0, 1.0);
}

// A star at the game pixel: 0 none, 1 dim, 2 mid, 3 bright - fewer near the horizon, none in its last 3 rows.
int star_in(int gx, int gy, float yaw) {
	int e = sky_horizon(height0e) - gy;
	if (e < 3) return 0;
	float sx = (float(gx) + 0.5 - 160.0) / 256.0;
	float u = (yaw - atan(sx)) * (float(STAR_TURN) / 6.28318531);
	float up = (yaw - atan(sx - 1.0 / 256.0)) * (float(STAR_TURN) / 6.28318531);   // the pixel to the left
	if (floor(up) == floor(u)) return 0;                                   // toward the edges a column spans two pixels: one star pixel only
	int cu = int(mod(floor(u), float(STAR_TURN)));                         // float mod: negative-safe
	uint h = _cloud_hash(uint(cu) * 7919u + uint(e) * 104729u + 0xABCDu);
	int chance = e < 10 ? 2 : e < 20 ? 5 : 8;                              // in 1024 pixels
	if (int(h & 1023u) >= chance) return 0;
	uint b = (h >> 10u) % 8u;
	return b < 4u ? 1 : b < 7u ? 2 : 3;                                    // half dim, most of the rest mid, a few bright
}

// The 8 x 8 moon's darker seas, a bit a pixel (bit 0 the left column), row 0 the top.
const int MOON_MARE[8] = int[](0x00, 0x0C, 0x1E, 0x0C, 0x40, 0x62, 0x30, 0x00);

// The moon at the game pixel: 0 none (sky, or its unlit side), 1 lit, 2 a lit sea.
int moon_in(int gx, int gy, float yaw) {
	if (moon_t < 0.0 || moon_t > 1.0) return 0;
	int hz = sky_horizon(height0e);
	float rows = max(float(hz) - 14.0, 4.0);                               // the sky's own height: the arc squashed into it, as the clouds sit
	float my = floor(float(hz) - (MOON_R + 1.0 + rows * sin(3.14159265 * moon_t)));
	float az = -1.57079633 - 3.14159265 * moon_t;                          // east -> south -> west, in the look turn's own sense (0 north, + left)
	float d = mod(yaw - az + 3.14159265, 6.28318531) - 3.14159265;
	if (abs(d) > 1.2) return 0;                                            // behind the eye
	float mx = floor(160.0 + 256.0 * tan(d));
	int ix = gx - int(mx) + 4, iy = gy - int(my) + 4;
	if (ix < 0 || ix > 7 || iy < 0 || iy > 7) return 0;
	if (gy >= hz) return 0;                                                // below the horizon
	float dx = (float(ix) - 3.5) / MOON_R, dy = (float(iy) - 3.5) / MOON_R;
	if (dx * dx + dy * dy > 1.0) return 0;
	float w = sqrt(max(0.0, 1.0 - dy * dy));
	float c = cos(6.28318531 * moon_phase);
	bool lit = moon_phase <= 0.5 ? dx > c * w : dx < -c * w;               // waxing lit from the right, waning from the left
	if (!lit) return 0;
	return ((MOON_MARE[iy] >> ix) & 1) != 0 ? 2 : 1;
}

// A $0RGB colour lit as fire: the screen's fade less the hour's, the death fade kept; 0..255 a channel.
vec3 night_lit8(int c) {
	int f = faded(c, clamp(fade_level + 15 - day_level, 0, 15));
	return nibbles(death_step > 0 ? death_faded(f, death_step) : f) * 17.0;
}

// The sky backdrop's colour sky8 at the game pixel with the stars and the moon on it (CLOUDS on, not indoors).
vec3 night_rgb8(vec3 sky8, int gx, int gy) {
	if (!cloud_on) return sky8;
	float n = night_amount();
	int m = moon_in(gx, gy, cloud_yaw);
	if (m > 0) return floor(mix(sky8, night_lit8(m == 2 ? 0xBBA : 0xEED), 0.4 + 0.6 * n) + 0.5);
	if (n <= 0.0) return sky8;
	int s = star_in(gx, gy, cloud_yaw);
	if (s == 0 || n < (s == 3 ? 0.0 : s == 2 ? 0.34 : 0.67)) return sky8;  // the brightest come out first
	return night_lit8(s == 3 ? 0xEEF : s == 2 ? 0xAAB : 0x778);
}
