// A thing from the game's own entity record to a point in the world, on the graphics card: the model
// walk and rotation (model.gdshaderinc) with the record's raw bytes as the input, the way the draw
// ($4EA2: Rotation $566E, Vertices $531E, Project $51F8) reads them.
//   ent: one record per row, one byte per texel (0xA0 wide, R float) - the record exactly as the rules hold it
//   anim: the animation table's 320 bytes, two rows - row 0 as it is now, row 1 as the table was one
//         step back (SMOOTH PARTS); a row's current values ARE the channel angles the game's last
//         step left ($566E writes each back), so the shader needs no state of its own
// Coordinates come back DOUBLED where the game halves them (x and z), so they stay whole numbers.

#include "res://real3d/model.gdshaderinc"

// A texture row holds width / 160 records side by side (GpuWorld.PerRow; a check's own table: 1).
int _eb(sampler2D ent, int row, int off) { int per = textureSize(ent, 0).x / 160; return int(round(texelFetch(ent, ivec2(off + row % per * 160, row / per), 0).r)); }
int _ew(sampler2D ent, int row, int off) { return _wrap16(_eb(ent, row, off) * 256 + _eb(ent, row, off + 1)); }
int _euw(sampler2D ent, int row, int off) { return _eb(ent, row, off) * 256 + _eb(ent, row, off + 1); }
int _ab(sampler2D anim, int arow, int i) { return int(round(texelFetch(anim, ivec2(i, arow), 0).r)); }   // arow: 0 in play, 1 one step back

// SMOOTH PARTS (owner 2026-10-29): how far into the step's blanks the game is, 0 just after a step to
// nearly 1 at the next (Play.AnimFraction). 0 when the caller sets nothing: the held step, no slide.
uniform float anim_frac = 0.0;

// FAST ROTORS (owner 2026-10-31: "they turn very slowly"): the helicopters' objfile offsets (GpuWorld); their
// wrapping channels (main and tail rotor) are drawn rotor_times as far round - 1/8 turn a step x 8 is about 7
// turns a second, a real rotor of that size. The game's own counter is untouched; 1 = the original.
uniform ivec2 rotor_models = ivec2(-1);
uniform int rotor_times = 1;

// STEERING WHEEL (CarInside, owner 2026-11-03): the card's cars and cabs carry a wheel part, the aircraft a stick, on channel 2, a table channel their set
// leaves at 0. Its angle is the record's steering byte +$18 (signed; -1, 0, +1 today) times its model's steer_turns, capped at one full
// lock (256ths of a turn; +1 turns the top of the wheel to the right, clockwise from the driver's seat), slid from
// the byte one step back (SMOOTH PARTS, GpuWorld holds +$18 with the part bytes). A finer (analog) byte later needs only another
// scale. steer_models = the objfile offsets + 1 of CarInside.Models (cars, cabs, aircraft), 0 = none
// (a uniform array takes no default).
uniform int steer_models[13];
uniform int steer_turns[13];
uniform int steer_lean = 0;   // STICK STEERING: the player's stick lean, -16..16 sixteenths of full lock (0 = none or off), on steer_lean_row only
uniform int steer_lean_row = 0;   // the player's record (slot 0); a check may point it elsewhere

// The four articulation angles of the record's model (base = the model's objfile offset), each slid from
// its value one step back to its current one by anim_frac - one step behind, as the in-between pictures
// are (SMOOTH PARTS). arow = the animation table's row, prow = the row holding the same table as it was
// one step back; pent = the record's own part bytes one step back (record-shaped, like ent). pent = ent
// and prow = arow hold the step still (checks, frozen records). The slid angles stay whole numbers: the
// trig table is indexed by whole 256ths of a turn.
// TANK TRACKS (TankTracks, owner 2026-11-12): the tanks' wheels and belt links turn on channel 0 by the tank's own rolling -
// the 256ths of a wheel turn its record has moved along its heading, added up by GpuWorld (TrackRoll) in roll_tex, a texel
// a record row (x = row & 255, y = row >> 8). Their models have no animation set, so nothing else drives the channel. Kept
// when the tank stops (the table's parts rest at 0 then, $56D0), so the wheels do not jump. track_models = the objfile
// offsets + 1 of the two tank models; only the live table's materials turn it on (a blast's record row is not a live row).
// Channel 1 is the tank's speed (+$3E, 0 when still) and channel 2 is where its clods are in their throw: the
// game's clock (track_clock, its own frames, still while paused) over FLING_BLANKS, offset by the row so tanks differ.
uniform bool track_on = false;
uniform int track_models[2];   // the two tanks
uniform sampler2D roll_tex : hint_default_black, filter_nearest;
uniform float track_clock = 0.0;
const float FLING_BLANKS = 24.0;

ivec4 entity_artic(sampler2D ent, sampler2D pent, sampler2D anim, int arow, int prow, int row, int base) {
    if (track_on && (base + 1 == track_models[0] || base + 1 == track_models[1]))
        return ivec4(int(round(texelFetch(roll_tex, ivec2(row & 255, row >> 8), 0).r)) & 255,
            clamp(_ew(ent, row, 0x3E), 0, 255),                          // the tanks: their speed (0 still, backing up as still) - the clods' trail
            int(fract(track_clock / FLING_BLANKS + float(row) * 0.618) * 256.0) & 255, 0);
    int set = _head(_at(base)).x;
    bool f19 = ((_eb(ent, row, 6) >> 1) & 1) != 0, f1b = ((_eb(ent, row, 6) >> 3) & 1) != 0;
    if (!f19 && f1b && _ew(ent, row, 0x3E) <= 0) return ivec4(0);        // $56D0: a dead thing's parts rest
    int at = (set - 1) * 20;
    float times = base == rotor_models.x || base == rotor_models.y ? float(rotor_times) : 1.0;
    ivec4 a = ivec4(0);
    for (int k = 0; k < 4 && set != 0; k++) {                            // no set: no table parts (a steering stick still turns, below)
        int c = _sb(_ab(anim, arow, at + k));
        int v = c < 0 ? _eb(ent, row, 0x84 + k + (c & 3)) : _ab(anim, arow, at + 4 + k * 4);
        if (c < 0) {                                                     // a record's own part byte: slid from its value one step back
            int pv = _eb(pent, row, 0x84 + k + (c & 3));
            if (v - pv < -128 || v - pv > 128) pv = v;                   // a jump over half a turn shows at once, not the long way round
            v = (pv + int(round(float(v - pv) * anim_frac))) & 0xFF;
        } else {                                                         // a table channel: slid from the previous step's value
            int pv = _ab(anim, prow, at + 4 + k * 4);
            int d = ((v - pv + 128) & 255) - 128;                        // the short way round: ctl 1 wraps (+32 from 240 is 16), a bounce
                                                                             // crosses 0 (8 -> 252 is -12; the long way swung the cow round)
            if (c == 1) v = int(round((float(pv) + float(d) * anim_frac) * times)) & 0xFF;   // whole times: seamless at the wrap
            else v = (pv + int(round(float(d) * anim_frac))) & 0xFF;
        }
        a[k] = v;
    }
    int steer_turn = 0;
    for (int i = 0; i < 13; i++) if (base + 1 == steer_models[i]) steer_turn = steer_turns[i];
    if (steer_turn != 0) {                                                        // the steering wheel: slid from the byte one step back like the part bytes
        int now = clamp(_sb(_eb(ent, row, 0x18)) * steer_turn, -steer_turn, steer_turn);
        int was = clamp(_sb(_eb(pent, row, 0x18)) * steer_turn, -steer_turn, steer_turn);
        if (row == steer_lean_row && steer_lean != 0) { now = steer_lean * steer_turn / 16; was = now; }   // the stick's own lean: as far as it leans
        a[2] = (was + int(round(float(now - was) * anim_frac))) & 0xFF;
    }
    return a;
}

bool entity_unrotated(sampler2D ent, int row) { return ((_eb(ent, row, 7) >> 2) & 1) != 0; }

// FINE ANGLES: the record's (roll, pitch, yaw) parts of a step past their bytes, 1/256 steps - GpuWorld puts them in the
// angle words' low-byte slots (+$81, +$7D, +$7F), which nothing else on the card reads; 0 when off or not known.
vec3 entity_frac(sampler2D ent, int row) {
    int per = textureSize(ent, 0).x / 160, x = row % per * 160, y = row / per;
    return vec3(texelFetch(ent, ivec2(x + 0x81, y), 0).r, texelFetch(ent, ivec2(x + 0x7D, y), 0).r, texelFetch(ent, ivec2(x + 0x7F, y), 0).r);
}

// Corner vidx of the record's model turned into place but not yet put at the record's position: the
// vector from the record's centre (x and z still doubled on the game's scale). low = the lowest placed
// corner of the whole model (the game's $1580), for the lift.
ivec3 entity_corner_rot(sampler2D trig, sampler2D ent, sampler2D pent, sampler2D anim, int arow, int prow, int row, int base, int vidx, out int low) {
    int at = _at(base);
    int y = _ew(ent, row, 0x4C);
    ivec4 artic = entity_artic(ent, pent, anim, arow, prow, row, base);
    ivec3 ang = ivec3(_eb(ent, row, 0x80), _eb(ent, row, 0x7C), _eb(ent, row, 0x7E));
    ivec3 rot;
    if (entity_unrotated(ent, row)) {
        rot = _corner(at, vidx).xyz;
        low = model_min_y_plain(at, y);
    } else {
        rot = corner_fine(model_local(trig, at, vidx, artic), ang, entity_frac(ent, row), trig);
        low = model_min_y(trig, at, artic, ang, 0, ivec3(0, y, 0));
    }
    return rot;
}

// The same corner less a thin sheet's slab thickness (GpuWorld's FindSheets): thick = the face's own
// rest direction, already scaled - the thickness 1 model unit before the model's scale shift.  Turned
// by the part's articulation and then as the corner is, so the slab rides a turning part (a rotor
// blade) and banks with the whole thing, whatever way the sheet faces.
ivec3 entity_corner_rot_thick(sampler2D trig, sampler2D ent, sampler2D pent, sampler2D anim, int arow, int prow, int row, int base, int vidx, ivec3 thick, out int low) {
    ivec3 rot = entity_corner_rot(trig, ent, pent, anim, arow, prow, row, base, vidx, low);
    ivec3 d = thick;
    if (!entity_unrotated(ent, row)) {
        d = model_artic_dir(trig, _at(base), vidx, entity_artic(ent, pent, anim, arow, prow, row, base), d);
        d = corner_fine(d, ivec3(_eb(ent, row, 0x80), _eb(ent, row, 0x7C), _eb(ent, row, 0x7E)), entity_frac(ent, row), trig);
    }
    return rot - d;
}

// Corner vidx of the record's model: x and z doubled, y whole (the model's own turn is 0 here; the
// camera's half turn belongs to the view). ground = +$74, y = +$4C, position = +$4A / +$4E.
ivec3 entity_vertex2(sampler2D trig, sampler2D ent, sampler2D pent, sampler2D anim, int arow, int prow, int row, int base, int vidx) {
    int x = _euw(ent, row, 0x4A), y = _ew(ent, row, 0x4C), z = _euw(ent, row, 0x4E);
    int low;
    ivec3 rot = entity_corner_rot(trig, ent, pent, anim, arow, prow, row, base, vidx, low);
    int lift = lift_to_ground(_ew(ent, row, 0x74), low);
    return ivec3(2 * x + rot.x, _wrap16(_wrap16(rot.y + y) + lift), 2 * z + rot.z);
}

