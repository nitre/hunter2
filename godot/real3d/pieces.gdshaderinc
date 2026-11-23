// (setting card_pieces, DEBRIS: PIECES, the card-drawn 3D view): when a thing blows up, the
// card breaks it apart - written for the card, not the old picture's code. The game itself is untouched: it only notes
// the thing's record at the moment it blew up (rules Objects.Exploded) and when; everything here is a pure function of
// that record, the time since, and the ground (ground_height.gdshaderinc, the game's own $36AA, number-checked).
// Two stages: each panel (a filled face, wheels included) flies up whole from where it sat, spinning as the push gave
// it; at the top of its rise
// it breaks into its triangles, each going on with the speed and spin it had there; a triangle lands when its lowest
// point can touch, no bounce, tipping where it landed to lie flat along the ground's slope, and lies
// still at most 5 seconds, shrinking away at the end; at sea it slips under and is gone. Nothing jumps at the break,
// and on land no corner is ever under the ground under it.
// Spin is the push's own: the blast pushes a panel at its centre of pressure, off its
// middle toward the blast, so spin = offset x push / the panel's spread (a flat plate's moment, GPU Gems 3 ch. 29):
// small panels and edge-on panels turn fast, big ones facing the blast hardly at all.
// Units: the picture's own (x and z doubled world units, heights whole - alike every way); t in game frames (blanks).
// Needs place.gdshaderinc, gpumodels.gdshaderinc (model_tex, tri_tex, tri_slots, trig_tex, anim_tex),
// ground_height.gdshaderinc and ground_cell.gdshaderinc.

uniform float piece_air = 100.0;         // frames a panel would be in the air on flat ground: two seconds, whole for the first (was 75)
uniform float piece_spread = 1024.0;     // how far out the panels land, in the picture's units (a ground square is 1024)
uniform float piece_rise = 400.0;        // how high they go before they break
uniform float piece_pressure = 0.25;     // centre of pressure: this much of a panel's radius toward the blast
uniform float piece_break_down = 0.5;   // a panel breaks this far down from its top to the ground: halfway
uniform float piece_settle = 8.0;       // frames a landed triangle takes to tip flat where it lies (no bounce)
uniform float piece_crack = 0.15;        // the break's own push on each triangle, as a part of the panel's speed
uniform float piece_near = 1.6;          // BETTER EXPLOSIONS: a panel at the hit's own point flies this much faster than the average one...
uniform float piece_far = 0.6;           // ...and one the thing's size or more away this much (the push falls off with distance)
uniform float piece_kick = 0.5;          // BETTER EXPLOSIONS: the star throws the panels out with it - this much of its reach, at once...
uniform float piece_kick_time = 3.0;     // ...over about this many frames (the star is at its full size in 6); they land where they did. 0 kick = the flight without it
uniform float piece_rest = 250.0;       // frames a piece lies on the ground at most
uniform float piece_shrink = 25.0;       // the last of them it shrinks away in, lying where it is
uniform bool piece_stay = false;         // EXPLOSIONS: LASTING - no end to lying still

vec3 _piece_rotate(vec3 v, vec3 axis, float a) {
    float c = cos(a), s = sin(a);
    return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
}

vec3 _piece_hash(int n) {
    vec3 h = fract(sin(vec3(float(n) * 12.9898, float(n) * 78.233, float(n) * 37.719)) * 43758.5453);
    return normalize(h * 2.0 - 1.0 + vec3(0.0, 0.0, 0.001));
}

float _piece_ground(sampler2D map, float x, float z) { return float(ground_height(map, int(x * 0.5) & 0xFFFF, int(z * 0.5) & 0xFFFF)); }

// Which way is up off the ground at x, z (the picture's units): its slope from the heights either side.
vec3 _piece_ground_up(sampler2D map, float x, float z) {
    const float d = 16.0;
    float hx = _piece_ground(map, x + d, z) - _piece_ground(map, x - d, z);
    float hz = _piece_ground(map, x, z + d) - _piece_ground(map, x, z - d);
    return normalize(vec3(-hx, 2.0 * d, -hz));
}

// The landing time from height drop above the ground, rising speed vy, gravity g.
float _piece_land(float drop, float vy, float g) { return (vy + sqrt(max(vy * vy + 2.0 * g * max(drop, 0.0), 0.0))) / g; }

// Triangle tri_row of the record in blast (row), placed as the game drew it: its three corners (x, height, z).
void _piece_tri(sampler2D blast, int row, int base, int model, int tri_row, out vec3 a, out vec3 b, out vec3 c) {
    vec4 tri = texelFetch(tri_tex, tri_texel(tri_row, 0), 0);
    int x = _euw(blast, row, 0x4A), y = _ew(blast, row, 0x4C), z = _euw(blast, row, 0x4E), ground = _ew(blast, row, 0x74);
    vec3 p[3];
    int low = 0;
    int corners = _head(_at(base)).z;
    for (int i = 0; i < 3; i++) {
        int vidx = int((i == 0 ? tri.r : (i == 1 ? tri.g : tri.b)) + 0.5);   // the corner number is plain (the slab's moved-corner marks are in x 2's alpha): its plain self here
        // SMOOTH PARTS: pent = blast, prow = arow - the frozen record's parts stay at the step it blew up on
        vec3 rot;
        if (vidx >= corners)                                          // a back wall's extra point (entity_extra_rot)
            rot = entity_extra_rot(blast, blast, 0, 0, row, base, model, vidx, low);
        else
            rot = entity_corner_rot(trig_tex, blast, blast, anim_tex, 0, 0, row, base, vidx, low);
        p[i] = vec3(float(2 * x) + rot.x, float(y + lift_to_ground(ground, low)) + rot.y, float(2 * z) + rot.z);
    }
    a = p[0]; b = p[1]; c = p[2];
}

// A panel's flight, the same for every triangle of it (slot = any triangle of the panel): the triangle's own corners
// as the thing stood (p0 p1 p2, their middle c), the panel's middle, its speed and spin from the push, gravity, its
// round size, and when it breaks (tb) - all in the picture's units. ok false = no such triangle, or not a panel.
// The star's kick: the first part of the way out is done at once (kick, over about kick_time frames) and the steady
// speed v is that much less, so the panel is where it was after piece_air frames; push = the speed without the kick
// (what the spin and the break's own push go by).
struct PieceFlight { bool ok; int colour; vec3 p0; vec3 p1; vec3 p2; vec3 c; vec3 mid; vec3 v; vec3 omega; float g; float radius; float tb; vec3 kick; float kick_time; float push; };

// The panel's middle t frames after the thing blew up, flying whole (t no later than its break).
vec3 _piece_kicked(vec3 kick, float kick_time, float t) { return kick * (1.0 - exp(-t / kick_time)); }
vec3 _piece_mid(PieceFlight f, float t) {
    return f.mid + f.v * t - vec3(0.0, 0.5 * f.g * t * t, 0.0) + _piece_kicked(f.kick, f.kick_time, t);
}

PieceFlight _piece_flight(sampler2D blast, sampler2D map, int row, vec4 from, int slot) {
    PieceFlight f = PieceFlight(false, 0, vec3(0.0), vec3(0.0), vec3(0.0), vec3(0.0), vec3(0.0), vec3(0.0), vec3(0.0), 1.0, 1.0, 0.0, vec3(0.0), 1.0, 0.0);
    int model = _eb(blast, row, 0x25);
    if (model == 0) return f;
    vec4 mi = texelFetch(model_tex, ivec2(model, 0), 0);
    int base = int(mi.r + 0.5), start = int(mi.g + 0.5), count = int(mi.b + 0.5);
    if (slot >= count) return f;
    int meta = int(texelFetch(tri_tex, tri_texel(start + slot, 0), 0).a + 0.5);
    if ((meta & 0x100) != 0) return f;                                // a rod (meta bit 8, GpuWorld.RodTriangles) does not break into pieces
    if ((meta & 0x2000) != 0) return f;                               // the sunk glass and its frame (meta bit 13, Glass.Sunk) are no panel of the wreck
    int colour = meta & 15;
    vec3 p[3];
    _piece_tri(blast, row, base, model, start + slot, p[0], p[1], p[2]);
    vec3 c = (p[0] + p[1] + p[2]) / 3.0;

    // the panel: its triangles' area-weighted middle, its area, its face direction
    vec4 pi = texelFetch(tri_tex, tri_texel(start + slot, 1), 0);
    int first = int(pi.r + 0.5), n = int(pi.g + 0.5), key = int(pi.b + 0.5);   // key: where the panel sat before the glass recess's triangles (GpuWorld.Bake) - what the hash takes
    vec3 mid = vec3(0.0), across_sum = vec3(0.0); float area = 0.0;
    for (int j = 0; j < n; j++) {                                     // every triangle: a slab's panel is its face twice and 2 an edge, past 16 from 6 corners (a cap of 16 skewed its middle)
        vec3 a, b, d;
        _piece_tri(blast, row, base, model, first + j, a, b, d);
        vec3 cr = cross(b - a, d - a);
        float ar = 0.5 * length(cr);
        mid += (a + b + d) / 3.0 * ar; area += ar; across_sum += cr;
    }
    if (area > 0.0) mid /= area; else mid = c;
    vec3 face = length(across_sum) > 0.0 ? normalize(across_sum) : vec3(0.0, 1.0, 0.0);
    float spread2 = max(area, 1.0) / 6.2831853;                       // a flat plate's moment over its mass (disc: r^2 / 2)
    float radius = sqrt(max(area, 1.0) / 3.1415927);

    // the thing's centre (its corners' box at rest, model_tex row 2), turned and placed as its corners are
    int x = _euw(blast, row, 0x4A), y = _ew(blast, row, 0x4C), z = _euw(blast, row, 0x4E), ground = _ew(blast, row, 0x74);
    int low = 0;
    int c0 = int(texelFetch(tri_tex, tri_texel(start + slot, 0), 0).r + 0.5);
    if (c0 >= _head(_at(base)).z)   // a back wall's extra point: the lift is the same
        entity_extra_rot(blast, blast, 0, 0, row, base, model, c0, low);
    else
        entity_corner_rot(trig_tex, blast, blast, anim_tex, 0, 0, row, base, c0, low);
    vec4 mc = texelFetch(model_tex, ivec2(model, 2), 0);
    ivec3 cl = ivec3(int(round(mc.r)), int(round(mc.g)), int(round(mc.b)));
    vec3 cr = entity_unrotated(blast, row) ? vec3(cl) : corner_fine(cl, ivec3(_eb(blast, row, 0x80), _eb(blast, row, 0x7C), _eb(blast, row, 0x7E)), entity_frac(blast, row), trig_tex);
    vec3 centre = vec3(float(2 * x) + cr.x, float(y + lift_to_ground(ground, low)) + cr.y, float(2 * z) + cr.z);

    // BETTER EXPLOSIONS: hit by a shell, missile or blast, the
    // push comes from where that was, not the thing's middle - away from it, the nearest panels fastest
    vec3 blast_at = centre;
    float near = 1.0;
    if (from.w > 0.5) {
        blast_at = vec3(float(2 * x) + 2.0 * from.x, float(y) + from.y + float(lift_to_ground(ground, low)), float(2 * z) + 2.0 * from.z);
        float size = max(length(vec3(2.0 * float(_ew(blast, row, 0x6E)), float(_ew(blast, row, 0x70)), 2.0 * float(_ew(blast, row, 0x72)))), 16.0);   // the thing's half box, the picture's units
        near = mix(piece_near, piece_far, clamp(length(mid - blast_at) / size, 0.0, 1.0));
    }

    // the panel's push: out along the ground from the blast and up, each panel a little faster or slower and
    // higher or lower than the next (keyed on the panel, so its triangles agree)
    vec3 r = _piece_hash(key * 131 + row * 7 + 3) * 0.5 + 0.5;
    vec2 out2 = vec2(mid.x - blast_at.x, mid.z - blast_at.z);
    if (length(out2) < 1.0) out2 = _piece_hash(row * 251 + key).xz;
    out2 = normalize(out2);
    float g = 8.0 * piece_rise / (piece_air * piece_air);            // the rise and the air time fix gravity
    float up = g * piece_air * 0.5 * (0.8 + 0.4 * r.y) * sqrt(near);
    vec3 v = vec3(out2.x, 0.0, out2.y) * (piece_spread / piece_air) * (0.6 + 0.7 * r.x) * near + vec3(0.0, up, 0.0);

    // its spin: the push at the centre of pressure, on the panel, off its middle toward the blast
    vec3 toward = blast_at - mid;
    toward -= face * dot(toward, face);
    vec3 lever = length(toward) > 0.001 ? normalize(toward) * radius * piece_pressure : vec3(0.0);
    vec3 omega = cross(lever, v) / spread2;
    float spin = length(omega);
    vec3 axis = spin > 0.0 ? omega / spin : vec3(0.0, 1.0, 0.0);

    // the star's kick (from.w = 1 + how far the star that did it reaches as drawn, world units): that part of the
    // panel's way out at once, the rest at a steady speed that much lower - never more than half of it
    float push = length(v);
    float part = min(piece_kick * 2.0 * max(from.w - 1.0, 0.0) / piece_spread, 0.5);
    vec3 kick = vec3(v.x, 0.0, v.z) * piece_air * part;
    float kt = max(piece_kick_time, 0.01);
    v = vec3(v.x * (1.0 - part), v.y, v.z * (1.0 - part));

    // stage 1: the panel whole, up and out and back down to halfway between its top and the ground under it, then it
    // breaks into its triangles - the same moment for every triangle of the panel
    float top = v.y / g;
    vec3 pt = mid + v * top - vec3(0.0, 0.5 * g * top * top, 0.0) + _piece_kicked(kick, kt, top);
    float tb = top + sqrt(max(pt.y - _piece_ground(map, pt.x, pt.z), 0.0) * 2.0 * piece_break_down / g);
    // but never once the panel, falling whole, is down to its reach above the ground under it (a slope, a short drop):
    // halved back toward the top - the panel's own middle and size, so all its triangles still break together
    float reach0 = radius * 2.5;                                      // a long panel's far corner reaches past its round size
    for (int i = 0; i < 8; i++) {
        vec3 mb = mid + v * tb - vec3(0.0, 0.5 * g * tb * tb, 0.0) + _piece_kicked(kick, kt, tb);
        if (mb.y - reach0 - _piece_ground(map, mb.x, mb.z) > 0.0) break;
        tb *= 0.5;                                                    // even before the top: a low flight into rising ground
    }
    kt = min(kt, max(tb, 0.04) * 0.25);                               // a panel that breaks at once: its kick is over by the break (the triangles go on at the steady speed)
    return PieceFlight(true, colour, p[0], p[1], p[2], c, mid, v, omega, g, radius, tb, kick, kt, push);
}

// BETTER EXPLOSIONS step 6: a small star where a panel breaks. The thing's six biggest panels (model_tex rows 3 and 4,
// GpuWorld.Bake) each get a small copy of the star that blew it up, standing where the panel breaks for break_star_life
// frames: turning an eighth at a time, stepping through the fire's colours as the big one does, sized by the panel.
// Only a thing a shell, missile or blast blew up has them (a star must have done it); the picture only.
uniform float break_star_life = 8.0;     // the game's frames a small star stands (the big one stands about 13 in the demo)
uniform float break_star_step = 2.0;     // frames an eighth of a turn
uniform float break_star_size = 1.5;     // how far its spikes reach, in the panel's round size
uniform float fire_tall[7];              // how far each star model reaches up or down from its middle (Fireball.Tall)

// Small star n (0-5) of the blast in row: where it stands (the picture's units), when its panel breaks, the panel's round size.
bool break_star_at(sampler2D blast, sampler2D map, int row, vec4 from, int n, out vec3 at, out float tb, out float radius) {
    at = vec3(0.0); tb = 0.0; radius = 0.0;
    if (from.w < 0.5) return false;                                   // no star did it: none here either
    vec4 big = texelFetch(model_tex, ivec2(_eb(blast, row, 0x25), 3 + n / 3), 0);
    int m = n - (n / 3) * 3;
    float slot = m == 0 ? big.r : (m == 1 ? big.g : big.b);
    if (slot < -0.5) return false;
    PieceFlight f = _piece_flight(blast, map, row, from, int(slot + 0.5));
    if (!f.ok) return false;
    tb = f.tb; radius = f.radius;
    at = _piece_mid(f, tb);
    return true;
}

// The middle of that same panel (the picture's units) t frames after the thing blew up, while it flies whole - for
// reading back how far out it is (the live "breakstars").
bool break_panel_at(sampler2D blast, sampler2D map, int row, vec4 from, int n, float t, out vec3 at) {
    at = vec3(0.0);
    vec4 big = texelFetch(model_tex, ivec2(_eb(blast, row, 0x25), 3 + n / 3), 0);
    int m = n - (n / 3) * 3;
    float slot = m == 0 ? big.r : (m == 1 ? big.g : big.b);
    if (slot < -0.5) return false;
    PieceFlight f = _piece_flight(blast, map, row, from, int(slot + 0.5));
    if (!f.ok) return false;
    at = _piece_mid(f, min(t, f.tb));
    return true;
}

// Corner k of triangle slot of that small star (a copy of the model star, one of the blasts' $6E-$74) t frames after
// the thing blew up: a world point as piece_corner's, and its colour. False = not standing now, or no such triangle.
bool break_star_corner(sampler2D blast, sampler2D map, int row, vec4 from, int star, int n, int slot, int k, float t, out vec3 pos, out int colour) {
    pos = vec3(0.0); colour = 0;
    if (star < 0x6E || star > 0x74) return false;
    vec3 at; float tb, radius;
    if (!break_star_at(blast, map, row, from, n, at, tb, radius)) return false;
    float s = t - tb;
    if (s < 0.0 || s >= break_star_life) return false;
    vec4 mi = texelFetch(model_tex, ivec2(star, 0), 0);
    int base = int(mi.r + 0.5), start = int(mi.g + 0.5), count = int(mi.b + 0.5);
    if (slot >= count) return false;
    vec4 tri = texelFetch(tri_tex, tri_texel(start + slot, 0), 0);
    int meta = int(tri.a + 0.5);
    int vidx = int((k == 0 ? tri.r : (k == 1 ? tri.g : tri.b)) + 0.5);
    int sat = _at(base);
    if (vidx >= _head(sat).z) return false;
    vec3 rel = vec3(model_local(trig_tex, sat, vidx, ivec4(0)) - model_local(trig_tex, sat, 0, ivec4(0)));   // from the star's middle (its corner 0), as the model stands
    float age = s / break_star_life;
    int third = min(int(age * 3.0), 2);
    bool core = (meta & 0x400000) != 0;                               // a fireball core's face (Fireball.Bit)
    int heat = core ? third + (slot & 1) : max(_fire_heat(meta & 15), third + 1);
    colour = FIRE_HOT[min(heat, 3)];
    float size = break_star_size * radius / max(2.0 * fire_reach[star - 0x6E], 1.0) * min(1.0, 0.5 + 0.25 * floor(age * 4.0));   // out in steps: 0.5 0.75 1
    float turn = floor(s / break_star_step) * 0.7853982;
    float cs = cos(turn), sn = sin(turn);
    rel = vec3(rel.x * cs - rel.z * sn, rel.y, rel.x * sn + rel.z * cs) * size;
    at.y = max(at.y, _piece_ground(map, at.x, at.z) + fire_tall[star - 0x6E] * size);   // never into the ground
    vec3 q = at + rel;
    pos = vec3(q.x * 0.5, q.y, q.z * 0.5);
    return true;
}

// Triangle slot of the record in blast (row) at t frames after it blew up: its corner k as a world point (x, height,
// z in world units, z north, like gpu_model_vertex), its colour, its face direction (unit, the picture's units) for the
// sun, when its panel breaks and when it came to rest. False = no such triangle, or the piece is gone.
// from: where the shell, missile or blast that did it was (GpuWorld.blastInfo row 1: world units off the record's
// +$4A / +$4C / +$4E, w = 1), or w = 0 - none: from the thing's own middle, as before.
bool piece_corner(sampler2D blast, sampler2D map, sampler2D types, int row, vec4 from, int slot, int k, float t,
                  out vec3 pos, out int colour, out vec3 normal, out bool resting, out float t_break, out float t_rest) {
    pos = vec3(0.0); colour = 0; normal = vec3(0.0, 1.0, 0.0); resting = false; t_break = 0.0; t_rest = 0.0;
    PieceFlight f = _piece_flight(blast, map, row, from, slot);
    if (!f.ok) return false;
    colour = f.colour;
    vec3 p[3]; p[0] = f.p0; p[1] = f.p1; p[2] = f.p2;
    vec3 c = f.c, mid = f.mid, v = f.v, omega = f.omega;
    float g = f.g, tb = f.tb;
    float spin = length(omega);
    vec3 axis = spin > 0.0 ? omega / spin : vec3(0.0, 1.0, 0.0);
    t_break = tb;
    vec3 at; float turn; vec3 about;
    if (t < tb) {
        at = _piece_mid(f, t);
        turn = spin * t;
        about = mid;                                                  // turned about the panel's middle
    } else {
        // stage 2: the triangle on its own, from where it was at the break, with the speed its spot on the panel had
        vec3 pb = _piece_mid(f, tb);
        vec3 off = _piece_rotate(c - mid, axis, spin * tb);
        vec3 cb = pb + off;
        vec3 vb = vec3(v.x, v.y - g * tb, v.z) + cross(omega, off) + (length(off) > 0.001 ? normalize(off) : _piece_hash(slot * 97 + row + 1)) * piece_crack * f.push;
        float s = t - tb;
        // it lands when its lowest point can touch: its middle a corner's reach above the ground
        float reach = max(max(length(p[0] - c), length(p[1] - c)), length(p[2] - c));
        float h = _piece_ground(map, cb.x, cb.z);
        float t1 = _piece_land(cb.y - reach - h, vb.y, g);
        for (int i = 0; i < 3; i++) {
            h = _piece_ground(map, cb.x + vb.x * t1, cb.z + vb.z * t1);
            t1 = _piece_land(cb.y - reach - h, vb.y, g);
        }
        // one already lower than its reach when the panel breaks lands where it is (h + reach popped it up at the break)
        vec3 land = vec3(cb.x + vb.x * t1, min(h + reach, cb.y + vb.y * t1 - 0.5 * g * t1 * t1), cb.z + vb.z * t1);
        bool sea = ground_is_water(types, int(land.x * 0.5), int(land.z * 0.5));   // the coast's water halves too
        vec3 o[3];
        if (s <= t1 || sea) {
            if (sea && s - t1 > 10.0) return false;                   // at sea: no bounce, under the surface and gone
            at = s <= t1 ? cb + vb * s - vec3(0.0, 0.5 * g * s * s, 0.0) : vec3(land.x, land.y - (g * t1 - vb.y) * (s - t1), land.z);
            for (int i = 0; i < 3; i++) o[i] = _piece_rotate(p[i] - c, axis, spin * t);
        } else {
            // no bounce: where it landed it tips over piece_settle frames from the way it came down to lying flat along the
            // ground's slope, then lies still
            float t2 = t1 + piece_settle;
            float sb = min(s, t2) - t1;
            float flat_ = sb / piece_settle;
            at = land;
            for (int i = 0; i < 3; i++) o[i] = _piece_rotate(p[i] - c, axis, spin * (tb + t1));
            vec3 up_g = _piece_ground_up(map, at.x, at.z);
            vec3 nl = cross(o[1] - o[0], o[2] - o[0]);
            nl = length(nl) > 0.0 ? normalize(nl) : up_g;
            if (dot(nl, up_g) < 0.0) nl = -nl;
            vec3 tip = cross(nl, up_g);
            float tip_a = atan(length(tip), dot(nl, up_g)) * flat_;
            if (length(tip) > 1e-6) for (int i = 0; i < 3; i++) o[i] = _piece_rotate(o[i], normalize(tip), tip_a);
            at.y -= reach * flat_;                                    // the lift it landed with goes as it lies down
            if (s >= t2) {                                            // lying still: at most piece_rest frames, the last ones shrinking away
                resting = true;
                at.y = _piece_ground(map, at.x, at.z);
                t_rest = tb + t2;
                float left = piece_stay ? piece_shrink : tb + t2 + piece_rest - t;   // LASTING: always whole
                if (left <= 0.0) return false;
                float size = clamp(left / piece_shrink, 0.0, 1.0);
                for (int i = 0; i < 3; i++) o[i] *= size;
            }
        }
        if (!sea || s <= t1) {                                        // no corner under the ground under it (still flying to the sea: over the shore too)
            float lift = 0.0;
            for (int i = 0; i < 3; i++) lift = max(lift, _piece_ground(map, at.x + o[i].x, at.z + o[i].z) - (at.y + o[i].y));
            at.y += lift;
        }
        vec3 nn = cross(o[1] - o[0], o[2] - o[0]);
        if (length(nn) > 0.0) normal = normalize(nn);
        vec3 q = (k == 0 ? o[0] : (k == 1 ? o[1] : o[2])) + at;
        pos = vec3(q.x * 0.5, q.y, q.z * 0.5);
        return true;
    }
    vec3 q0 = _piece_rotate(p[0] - about, axis, turn), q1 = _piece_rotate(p[1] - about, axis, turn), q2 = _piece_rotate(p[2] - about, axis, turn);
    vec3 nn = cross(q1 - q0, q2 - q0);
    if (length(nn) > 0.0) normal = normalize(nn);
    vec3 q = (k == 0 ? q0 : (k == 1 ? q1 : q2)) + at;                // stage 1: at = the panel's middle
    pos = vec3(q.x * 0.5, q.y, q.z * 0.5);
    return true;
}
