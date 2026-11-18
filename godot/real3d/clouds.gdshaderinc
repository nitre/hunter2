// (setting clouds, CLOUDS, the card-drawn 3D view; Knights of the Sky drew flat clumps the same way): CLOUDS
// separate blocky clumps in the sky, far away - fixed to world directions (they slide with the Q / E turn, never with
// walking) and drifting one fixed way at a slow pace on the game's clock (cloud_clock, frozen while paused).
// Drawn on the sky backdrop only, so land, sea and things (the depth buffer) are always in front of them.
// Needs world_colour.gdshaderinc (world_rgb8, sky_nib, the sky's fade) and sky.gdshaderinc (sky_horizon).

uniform bool cloud_on = false;          // CLOUDS: the setting, and not indoors
uniform float cloud_yaw = 0.0;          // the look turn, radians (Real3DView.Yaw), 0 = north, positive left
uniform float cloud_clock = 0.0;        // the game's seconds, paused with the game

const int CIRRUS_N = 11;                // cirrus streaks in a full turn of the sky
const int CLOUD_TURN = 201;             // cells in a full turn: 201 * 8 game pixels = 2 pi * 256 (the focal length)
const int CLOUD_W = 8;                  // a cell: 8 game pixels wide
const int CLOUD_H = 2;                  // and 2 rows tall
const float CLOUD_DRIFT = 2.0;          // game pixels a second, one way

uint _cloud_hash(uint a) {
	a = (a ^ 61u) ^ (a >> 16u);
	a *= 9u;
	a ^= a >> 4u;
	a *= 0x27d4eb2du;
	a ^= a >> 15u;
	return a;
}

// 0 = blue sky, 1 = cloud, 3 = its faint ends. gx: the game pixel column (0..319), gy: the game row. High CIRRUS as
// the first blocky streaks: blocks one cell tall, but not a repeating row of
// bars - each streak has its own place round the turn, length (5..13 cells), height and a gentle tilt of its own
// (a cell row every 10..21 cells, up or down, some flat); a few gaps; the end cells faint, the middle full.
int cloud_in(int gx, int gy, float yaw) {
	int e = sky_horizon(height0e) - gy;                                    // rows above the horizon
	if (e < CLOUD_H) return 0;
	float sx = (float(gx) + 0.5 - 160.0) / 256.0;
	float u = (yaw - atan(sx)) * (float(CLOUD_TURN * CLOUD_W) / 6.28318531) + cloud_clock * CLOUD_DRIFT;
	int cu = int(mod(floor(u / float(CLOUD_W)), float(CLOUD_TURN)));      // the cell column round the turn (float mod: negative-safe)
	int cr = e / CLOUD_H;
	for (int i = 0; i < CIRRUS_N; i++) {
		uint hs = _cloud_hash(uint(i) * 2654435u + 0x1234u);
		int start = i * CLOUD_TURN / CIRRUS_N + int(hs % 9u);             // spread round the turn, uneven gaps
		int w = 5 + int((hs >> 4u) % 9u);
		int d = cu - start;                                               // the cell along the streak
		if (d < 0) d += CLOUD_TURN;
		if (d >= w) continue;
		int base = 5 + int((hs >> 9u) % 10u);                             // its cell row above the horizon
		int tilt = int((hs >> 13u) % 3u) - 1;                             // -1 falls, 0 flat, 1 rises
		int every = 10 + int((hs >> 15u) % 12u);
		if (cr != base + tilt * (d / every)) continue;
		int ends = min(d, w - 1 - d);                                     // cells from the nearer end
		uint hc = _cloud_hash(uint(i) * 977u + uint(d) * 31u + 7u);
		if (ends > 1 && (hc & 7u) == 0u) continue;                        // the odd gap in the middle
		return ends < 2 ? 3 : 1;
	}
	return 0;
}

int cloud_at(int gx, int gy) { return cloud_on ? cloud_in(gx, gy, cloud_yaw) : 0; }

// The sky's colour at the pixel with a cloud of kind k (1 body, 2 top, 3 faint) on it: that colour lightened toward the sky's
// white (white through the sky's own fade, so it darkens at night with the bands), 0..255 a channel.
// With SKY's time of day (world_colour.gdshaderinc tod_band) the light on a cloud follows the sun's height
// instead: white by day, warm in the golden hour, lit pink-red from below just after sunset, then grey-blue and dark at night
// (docs/clouds-reference.md "Dawn and dusk"); lift: degrees the cloud sees the sun above the ground's horizon - the high
// cirrus stays lit after the low cumulus has gone dark.
vec3 cloud_sun_nib(float e) {
	if (e >= 10.0) return vec3(15.0);
	if (e >= 0.0) return mix(nibbles(0xFB8), vec3(15.0), e / 10.0);                       // golden: ~3000 K
	if (e >= -6.0) return mix(nibbles(0xFB8), nibbles(0xA67), -e / 6.0);                  // lit from below, pink-red, fading
	if (e >= -12.0) return mix(nibbles(0xA67), nibbles(0x335), (-6.0 - e) / 6.0);
	return mix(nibbles(0x335), nibbles(0x112), clamp((-12.0 - e) / 6.0, 0.0, 1.0));
}

vec3 cloud_white8_at(float lift) {
	if (tod_on) return 17.0 * tod_lit(cloud_sun_nib(tod_elev + lift));
	return 17.0 * (death_step > 0 ? nibbles(death_faded(0xFFF, death_step)) : nibbles(view_faded(0xFFF, sky_level)));
}

vec3 cloud_white8() { return cloud_white8_at(0.0); }

vec3 cloud_rgb8(vec3 sky8, int k) {
	return floor(mix(sky8, cloud_white8_at(3.0), k == 2 ? 0.95 : k == 3 ? 0.55 : 0.85) + 0.5);   // white cirrus (docs/clouds-reference.md); high, so lit 3 degrees longer
}

// The LOW layer (the look from the cloud atlas, docs/extras.md): fair-weather cumulus -
// LOW_CLOUD_N heaps at fixed places over the map and a little past it, drifting one way on the game's clock, every flat
// base at low_cloud_y. Each is 2-5 rounded heaps side by side (small flat / wide lumpy / taller kinds), stepped in
// blocks. Drawn by lowclouds.gdshader as upright cards facing the eye. SHADED: each heap is a rounded lump (half an ellipsoid on the flat base, as deep as it is wide), the lump that bulges
// nearest the eye shows at each block, and the sun lights it from its own side - a bright top, a lit side, a shaded
// side, the flat base in shadow; the heaps 1.4x taller than the first flat try.
uniform float low_cloud_y = 6144.0;     // world units up: half again the highest ground (255 x 16). A flier has no ceiling in the movement (rules/Movement.cs Settle $2AF2: only the height word's 32767), so the helicopter can climb above
uniform float low_fade_near = 800.0;    // near the camera a cloud dissolves in dots: gone by here
uniform float low_fade_far = 3000.0;    // and whole from here
const int LOW_CLOUD_N = 64;
const float LOW_CLOUD_DRIFT = 24.0;     // world units a second
const float LOW_CLOUD_SPAN = 98304.0;   // the map (65536) and half its width again, wrapped as they drift
const float LOW_CLOUD_STEPS = 24.0;     // blocks across a cloud
const float LOW_CLOUD_TALL = 1.4;       // the heaps' height over the first (flat) try's
const int LOW_CLOUD_DOTS[16] = int[](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);

struct LowCloud { float x; float z; float width; float aspect; float box; uint seed; int heaps; };
struct LowHeap { float o; float a; float b; };   // the heap's middle across (-0.5..0.5), half width and height, shares of the cloud's width

float _cloud_unit(uint h) { return float(h & 0xFFFFu) / 65535.0; }

LowCloud low_cloud(int id) {
	LowCloud c;
	c.seed = _cloud_hash(uint(id) * 2654435761u + 0x51u);
	uint h2 = _cloud_hash(c.seed ^ 0x9E37u), h3 = _cloud_hash(h2 + 7u);
	c.x = -16384.0 + mod(_cloud_unit(c.seed) * LOW_CLOUD_SPAN + cloud_clock * LOW_CLOUD_DRIFT, LOW_CLOUD_SPAN);
	c.z = -16384.0 + _cloud_unit(h2) * LOW_CLOUD_SPAN;
	int kind = int((h3 >> 16u) % 5u);                                         // 0, 1 small flat; 2, 3 wide lumpy; 4 taller
	float r = _cloud_unit(h3);
	c.width = kind < 2 ? mix(1500.0, 4000.0, r) : kind < 4 ? mix(4000.0, 9000.0, r) : mix(3000.0, 5500.0, r);
	c.aspect = (kind < 2 ? mix(0.25, 0.4, r) : kind < 4 ? mix(0.5, 0.7, r) : mix(0.9, 1.2, r)) * LOW_CLOUD_TALL;
	c.box = c.width * 0.5 * 0.65 * c.aspect;                                  // the tallest a heap can reach
	c.heaps = 2 + int((h2 >> 20u) % 4u);
	return c;
}

LowHeap low_heap(LowCloud c, int i) {
	uint hh = _cloud_hash(c.seed + uint(i) * 977u);
	LowHeap h;
	h.o = i == 0 ? mix(-0.15, 0.15, _cloud_unit(hh)) : mix(-0.35, 0.35, _cloud_unit(hh));
	float w = i == 0 ? mix(0.45, 0.65, _cloud_unit(hh >> 8u)) : mix(0.25, 0.5, _cloud_unit(hh >> 8u));
	float hgt = i == 0 ? 1.0 : mix(0.6, 0.9, _cloud_unit(hh >> 16u));
	h.a = w * 0.5;
	h.b = hgt * w * 0.5 * c.aspect;
	return h;
}

// The shade of cloud c at the card's point uv (across 0..1, up from the base 0..1), lit from the sun's side sun_x (-1
// the left, 1 the right, 0 straight above): -1 sky, 0 the flat base's shadow, 1 the shaded side, 2 the body, 3 the lit
// side, 4 the bright top. Judged at the block's middle, so the shade steps in blocks like the shape.
int low_cloud_level(LowCloud c, vec2 uv, float sun_x) {
	float step_h = c.width / LOW_CLOUD_STEPS;                                  // blocks are square
	float u = (min(LOW_CLOUD_STEPS - 1.0, floor(uv.x * LOW_CLOUD_STEPS)) + 0.5) / LOW_CLOUD_STEPS - 0.5;
	float y = (floor(uv.y * c.box / step_h) + 0.5) * step_h / c.width;
	float best = 0.0;
	vec3 n = vec3(0.0);
	LowHeap front = low_heap(c, 0);
	bool found = false;
	for (int i = 0; i < 5; i++) {
		if (i >= c.heaps) break;
		LowHeap h = low_heap(c, i);
		float dx = (u - h.o) / h.a, dy = y / h.b;
		float q = 1.0 - dx * dx - dy * dy;
		if (q <= 0.0) continue;
		float z = h.a * sqrt(q);                                               // how far the lump bulges toward the eye here
		if (!found || z > best) { found = true; best = z; front = h; n = normalize(vec3(dx / h.a, dy / h.b, sqrt(q) / h.a)); }
	}
	if (!found) return -1;
	vec3 l = normalize(vec3(sun_x * 0.75, 0.6, 0.45));                        // from the sun's side, above, a little from the eye
	float k = dot(n, l);
	int lvl = k > 0.8 ? 4 : k > 0.5 ? 3 : k > 0.1 ? 2 : 1;
	if (y < min(c.box / c.width * 0.14, front.b * 0.25)) lvl = 0;              // the flat base, in its own shadow
	return lvl;
}

// A low cloud's colour (docs/clouds-reference.md): a white sunlit top, the lit side near white, the body paler, the
// shaded side grey-blue, the flat base darker; through the sky's fade (darker at night), paler and bluer far off - over
// their own reach, not the land's haze, whose far edge (5248 at distance 11) is nearer than the clouds' own height: every
// cloud came out the sky's blue.
uniform float low_haze_far = 49152.0;   // a cloud this far is the sky's colour (half the clouds' span)
vec3 low_cloud_rgb8(int level, float depth, float fx) {                    // fx: the viewport column (SKY's time of day: the sky behind it there)
	vec3 sky5 = sky_nib_x(5, fx) * 17.0, sky4 = sky_nib_x(4, fx) * 17.0, white = cloud_white8();
	vec3 base = mix(sky4, white * 0.75, 0.6);
	vec3 c = level == 4 ? white : level == 3 ? mix(sky5, white, 0.92) : level == 2 ? mix(sky5, white, 0.78)
	       : level == 1 ? mix(sky5, white, 0.55) : mix(base, sky4, 0.3);
	float f = haze_mode == 0 ? 0.0 : clamp((depth - 0.25 * low_haze_far) / (0.75 * low_haze_far), 0.0, 1.0);
	return floor(mix(c, sky5, f) + 0.5);
}
