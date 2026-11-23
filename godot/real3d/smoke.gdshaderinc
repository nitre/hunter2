// (EXPLOSIONS: PIECES, the card-drawn 3D view; BETTER EXPLOSIONS step 7): low-poly smoke where a star went out. A few
// small flat-sided grey shapes in the star's own manner - eight triangles each, hard edges, nothing see-through - come one
// after another from where the star stood, rise in steps, swell a little, then shrink away. No wind: straight up.
// The picture only: the card keeps where each star ended (GpuWorld.AddSmoke), everything here is a pure function of
// that place, the star's reach and the time since. World units; t in the game's frames. A height counts half of a
// distance along the ground in the view (the picture's x and z are doubled), so everything up and down is doubled
// here (SMOKE_UP) - or a shape as tall as it is wide is drawn flat as a kite.
// Needs pieces.gdshaderinc (_piece_hash) and ground_height.gdshaderinc.

uniform float smoke_life = 100.0;        // the game's frames a shape lasts at most: two seconds
uniform float smoke_step = 4.0;          // frames a step of its rise and size
uniform float smoke_next = 2.0;          // frames between one shape starting and the next
uniform float smoke_rise = 1.6;          // how high it goes by its end, in the star's reach
uniform float smoke_puff = 0.16;         // a shape's size, in the star's reach
const int SMOKE_PUFFS = 12;              // shapes a star at most (GpuWorld.SmokePuffs); a small star has 8
const int SMOKE_TRIS = 8;                // triangles a shape
const float SMOKE_UP = 2.0;              // height units a world unit along the ground

// How many shapes a star of that reach leaves.
int smoke_count(float size) { return size >= 120.0 ? SMOKE_PUFFS : 8; }

// Shape puff of the smoke left at at.xyz (world units; at.w = a number of its own, for the shapes to differ) by a
// star reaching size, t frames after the star went out: where its middle is and how big it is. False = not there now.
bool smoke_puff_at(vec4 at, float size, float t, int puff, out vec3 mid, out float radius, out float age) {
    mid = vec3(0.0); radius = 0.0; age = 0.0;
    if (size <= 0.0 || puff >= smoke_count(size)) return false;
    vec3 h = _piece_hash(puff * 37 + int(at.w) * 11 + 5) * 0.5 + 0.5;
    float life = smoke_life * (0.7 + 0.3 * h.x);
    float s = t - float(puff) * smoke_next;
    if (s < 0.0 || s >= life) return false;
    age = floor(s / smoke_step) * smoke_step / life;                  // in steps, 0 .. nearly 1
    vec3 side = _piece_hash(puff * 91 + int(at.w) * 7 + 2);
    mid = at.xyz + vec3(side.x, 0.4 * SMOKE_UP * side.y, side.z) * 0.3 * size;   // from within the star, each its own spot
    mid.y += SMOKE_UP * size * smoke_rise * (0.6 + 0.4 * h.y) * age;  // straight up
    float swell = age < 0.3 ? mix(0.6, 1.0, age / 0.3) : (age > 0.6 ? 1.0 - (age - 0.6) / 0.4 : 1.0);
    radius = size * smoke_puff * (0.7 + 0.3 * h.z) * swell;
    mid.y = max(mid.y, float(ground_height(cam_map, int(mid.x) & 0xFFFF, int(mid.z) & 0xFFFF)) + SMOKE_UP * radius);   // never into the ground
    return radius > 0.0;
}

// Corner k of triangle tri of that shape: a world point, and how light that side is (1 = the grey itself).
bool smoke_corner(vec4 at, float size, float t, int puff, int tri, int k, out vec3 pos, out float light) {
    pos = vec3(0.0); light = 1.0;
    vec3 mid; float radius, age;
    if (tri >= SMOKE_TRIS || !smoke_puff_at(at, size, t, puff, mid, radius, age)) return false;
    // eight sides round six points (one each way along x, y, z), stretched and turned its own way so no two look alike
    vec3 sgn = vec3((tri & 1) != 0 ? 1.0 : -1.0, (tri & 2) != 0 ? 1.0 : -1.0, (tri & 4) != 0 ? 1.0 : -1.0);
    vec3 h = _piece_hash(puff * 53 + int(at.w) * 3 + 9) * 0.5 + 0.5;
    vec3 stretch = vec3(1.0 + 0.4 * h.x, 0.75 + 0.2 * h.y, 1.0 + 0.4 * h.z);
    vec3 p = k == 0 ? vec3(sgn.x, 0.0, 0.0) : (k == 1 ? vec3(0.0, sgn.y, 0.0) : vec3(0.0, 0.0, sgn.z));
    p *= stretch * radius * vec3(1.0, SMOKE_UP, 1.0);
    float turn = h.x * 6.2831853, cs = cos(turn), sn = sin(turn);
    pos = mid + vec3(p.x * cs - p.z * sn, p.y, p.x * sn + p.z * cs);
    // flat sides, four greys: the top ones lighter than the under ones, every other one a shade down; darker while new
    light = (sgn.y > 0.0 ? 1.0 : 0.62) - (sgn.x * sgn.z > 0.0 ? 0.0 : 0.15);
    light *= mix(0.6, 1.0, min(age / 0.3, 1.0));
    return true;
}
