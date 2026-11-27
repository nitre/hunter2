// The game's model animation and articulation (rules/Objects.cs Rotation $566E and TransformModelVertices $531E),
// number-exact on the graphics card. Builds on corner.gdshaderinc (the rotation maths). A model's header and
// corners come from the card's model store (CardModels, mdl_tex: RGBA32F, 1024 wide, a texel a = (a & 1023, a >> 10)):
// header texel At = (set, shift, corners, edges), then a texel a corner (x, y, z, flag) already scaled by the
// model's shift. mdl_at = the store address At of the model whose objfile offset (base) is the texel (base & 255,
// base >> 8) of a 256 x 256 R float table. Then a texel an edge (a, b), a texel a face (colour, flags, n, first
// slot) and the faces' edge numbers 4 a texel (CardModels has the layout).
#include "res://real3d/corner.gdshaderinc"

uniform sampler2D mdl_tex : filter_nearest;
uniform sampler2D mdl_at : filter_nearest;

int _sb(int v) { return (v << 24) >> 24; }                    // a byte as signed

int _at(int base) { return int(round(texelFetch(mdl_at, ivec2(base & 255, base >> 8), 0).r)); }   // the model's store address
ivec4 _head(int at) { return ivec4(texelFetch(mdl_tex, ivec2(at & 1023, at >> 10), 0)); }          // (set, shift, corners, edges); At + 1: (faces, list slots, texels, waterline)
// ENHANCEMENT (SHARKS AND SUBS, godot/real3d/SeaBodies.cs): a body built under the water, how far it reaches below the
// original's lowest corner (0 for every other model) - added to the lowest corner measured, so the lift is the original's
int _waterline(int at) { return _head(at + 1).w & 0xFFFF; }
int _measured(int at) { return _head(at).z - (_head(at + 1).w >> 16); }   // the corners the lowest-corner measure counts (CardModels.Model.Unmeasured)
// STUNTIN's racer (RacingKit.Fine): its corners kept at a quarter unit - the model stored FINE times its size, drawn back down
// at the last step (gpumodels: its place in the world, its lowest corner); its model's offset + 1, 0 = none
uniform int fine_model = 0;
uniform int fine_model2 = 0;     // the jeep racer (JeepKit), the same way
const float FINE = 4.0;
bool is_racer(int base) { return (fine_model != 0 && base + 1 == fine_model) || (fine_model2 != 0 && base + 1 == fine_model2); }
float fine_of(int base) { return is_racer(base) ? FINE : 1.0; }

ivec4 _corner(int at, int k) { int a = at + 2 + k; return ivec4(texelFetch(mdl_tex, ivec2(a & 1023, a >> 10), 0)); }   // (x, y, z, flag)
ivec2 _edge(int at, int e) { int a = at + 2 + _head(at).z + e; return ivec2(texelFetch(mdl_tex, ivec2(a & 1023, a >> 10), 0).xy); }   // (a, b)
ivec4 _face(int at, int f) { ivec4 h = _head(at); int a = at + 2 + h.z + h.w + f; return ivec4(texelFetch(mdl_tex, ivec2(a & 1023, a >> 10), 0)); }   // (colour, flags, n, first slot)
int _slot(int at, int s) {                                    // the edge number in list slot s
    ivec4 h = _head(at);
    int a = at + 2 + h.z + h.w + _head(at + 1).x + (s >> 2);
    return ivec4(texelFetch(mdl_tex, ivec2(a & 1023, a >> 10), 0))[s & 3];
}

// One animation channel: c = its control byte (signed), cur / step / lo / hi = its four table
// bytes, fresh = the set has not been stepped this frame, ent = the entity byte it may follow.
// Returns (new value, new step). c < 0 follows the entity; c > 0 steps (c == 1 never bounces).
ivec2 anim_channel(int c, int cur, int step, int lo, int hi, bool fresh, int ent) {
    int v = cur;
    if (c < 0) {
        v = ent;
    } else if (fresh && c > 0) {
        v = (cur + step) & 255;
        if (((c - 1) & 255) != 0) {
            int sv = _sb(v);
            if (sv <= _sb(lo) || sv >= _sb(hi)) {
                step = (-_sb(step)) & 255;
                v = (v + step) & 255;
            }
        }
    }
    return ivec2(v, step);
}

// $5484: turn a corner about the pivot by the articulation angles (roll, pitch, yaw bytes).
void _subrotate(inout ivec3 v, ivec3 piv, ivec3 art, sampler2D trig) {
    v = ivec3(_wrap16(v.x - piv.x), _wrap16(v.y - piv.y), _wrap16(v.z - piv.z));
    if (art.z != 0) _roll(v.x, v.y, art.z, trig);
    if (art.x != 0) _pitch(v.y, v.z, art.x, trig);
    if (art.y != 0) _yaw(v.x, v.z, art.y, trig);
    v = ivec3(_wrap16(v.x + piv.x), _wrap16(v.y + piv.y), _wrap16(v.z + piv.z));
}

// Corner idx of the model at store address at, as articulated (before the model's own turn and
// placing): the game walks the corners in order and a flagged corner sets the rule the unflagged
// ones after it follow (kind 1 = rotate about this pivot by an articulation channel, kind 2 =
// follow the part that moved, kind 3 / 4 = the card's belt parts (TankTracks), kind 5 = a thrown part (the worm's dust), kind 6 = a steered wheel and
// kind 7 a hand (RacingKit), otherwise plain). artic =
// the four channel angle bytes.
struct _Walk { ivec3 piv; ivec3 off; ivec3 art; int mode; };   // art = (pitch, yaw, roll) bytes

// The worm's dust (kind 5): a throw's reach in model units - out to the side, the arc's height, back (-z)
const ivec3 FLING = ivec3(12, 56, -112);
// RacingKit's steered front wheels (kind 6): the steering wheel's full lock (CarInside.FullLock, 256ths of a turn) and the road
// wheels' at that steering - 20 256ths, about 28 degrees
const int STEER_FULL = 85, WHEEL_LOCK = 20;

// Corner k of the model, given what the corners before it set up in w.
// THE STUNTIN' RACER'S SPRINGS (RacingKit, place.gdshaderinc entity_artic): channel 3 carries the right hand's way (bits 0-7)
// and above it each wheel's squash, model units + 64, 7 bits each - front left (8-14), front right (15-21), rear (22-28); 0 above
// the hand on every other model. A corner of the racer out past the body's sides at rest (x beyond RACER_WHEEL_X: the tyres, the
// discs, the struts' lower ends) rides up by its wheel's squash (by its side and end at rest), stored Fine times its size.
const int RACER_WHEEL_X = 267;   // 66.75 units x 4: the rear brackets and springs reach 66.4, the tyres start at 67
const int RACER2_WHEEL_X = 357;  // the jeep racer's (JeepKit.WheelSide 89.25 x 4): its body 80, brackets and springs to 88.9, tyres from 90
uniform int racer2_at = -1;      // the jeep racer's model (its data's place, as _walk_step's at)
uniform int racer_at = -1;       // the car racer's (RacingKit), the same way
ivec3 _racer_spring(ivec3 raw, ivec3 v, int springs, int at) {
    if (springs == 0 || abs(raw.x) <= (at == racer2_at ? RACER2_WHEEL_X : RACER_WHEEL_X)) return v;
    int sh = raw.z < 0 ? 14 : (raw.x > 0 ? 7 : 0);
    return ivec3(v.x, v.y + (((springs >> sh) & 127) - 64) * 4, v.z);
}

ivec3 _walk_step(inout _Walk w, sampler2D trig, int at, int k, ivec4 artic_in) {
    ivec4 artic = artic_in & ivec4(255);                                 // the channels' angle bytes (the racer's springs ride above its 3rd)
    ivec4 c = _corner(at, k);
    int flag = c.w;
    ivec3 v = c.xyz;
    if (flag != 0) {
        int kind = (flag >> 4) & 7, axis = (flag >> 2) & 3, which = flag & 3;
        if (kind == 1) {
            int d = artic[which];
            w.art = ivec3(axis == 0 ? d : 0, axis == 1 ? d : 0, axis >= 2 ? d : 0);
            w.piv = v;
            w.mode = 1;
        } else if (kind == 2) {
            w.off = v;
            _subrotate(v, w.piv, w.art, trig);
            w.off = ivec3(_wrap16(w.off.x - v.x), _wrap16(w.off.y - v.y), _wrap16(w.off.z - v.z));
            w.mode = 2;
        } else if (kind == 6) {                                          // ENHANCEMENT, card only (RacingKit): a STEERED WHEEL - turns about
            int d = artic[which];                                        // this pivot as kind 1 (its spin, axis x: pitch), then about y by the
            int s = artic[2] >= 128 ? artic[2] - 256 : artic[2];         // steering (channel 2: the steering wheel's angle, +-STEER_FULL at full
            w.art = ivec3(d, (s * WHEEL_LOCK / STEER_FULL) & 255, 0);    // lock) - _subrotate spins first, so the spin stays in the wheel's
            w.piv = v;                                                   // plane; + turns it the way + steering turns the heading (corner_fine's
            w.mode = 1;                                                  // yaw is the same turn)
        } else if (kind == 3) {                                          // ENHANCEMENT, card only (TankTracks): a belt part - this corner
            w.piv = v;                                                   // is the run's start A, the next one A + the pitch D; the corners
            w.art = ivec3(((axis + 1) * artic[which]) & 255, 0, 0);      // after slide by D x (L x channel) mod 256 / 256, L = axis + 1
            w.mode = 3;                                                  // (the rules' walk treats kind 3 as plain, $551E)
        } else if (kind == 4) {                                          // card only (TankTracks): a belt part WITH ENDS - this corner is the
            w.piv = v;                                                   // run's start A, the next A + the pitch D, the one after the run's end
            w.art = ivec3(((axis + 1) * artic[which]) & 255, 0, 0);      // E; the corners after slide as kind 3's, then are held between A and
            w.mode = 5;                                                  // E along the run (the rules' walk treats it as plain, $551E)
        } else if (kind == 5) {                                          // card only (the worm's dust): a FLING part - this
            float t = float((artic[2] + (flag & 15) * 16) & 255) / 256.0;    // corner and the ones after are thrown along an arc: out (the
            w.off = artic[1] == 0 ? ivec3(0) : ivec3(round(vec3(          // side the corner is on), up and back; channel 1 = 0 (still): at rest
                float(v.x < 0 ? -FLING.x : FLING.x) * t, float(FLING.y) * 4.0 * t * (1.0 - t), float(FLING.z) * t)));
            v = ivec3(_wrap16(v.x + w.off.x), _wrap16(v.y + w.off.y), _wrap16(v.z + w.off.z));
            w.mode = 4;                                                  // the rest of the part rides the same offset
        } else if (kind == 7) {                                          // ENHANCEMENT, card only (RacingKit): a HAND part - the corners after
            int t = artic[which];                                        // the next turn about this pivot with the steering wheel (channel 2,
            int s = artic[2] >= 128 ? artic[2] - 256 : artic[2];         // about z), less as the hand goes (channel `which`, 0-255), and slide
            w.art = axis == 1 ? ivec3(0) : ivec3(0, 0, (s * (255 - t) / 255) & 255);   // by that corner's offset from this pivot times it: the
            w.piv = v;                                                   // hand from the wheel to a handle off it and back (axis 1: the slide
                                                                         // only - the elbow, which rises with the hand but does not steer)
            w.off = ivec3(t, 0, 0);
            w.mode = 9;
        } else if (kind == 0 && axis == 2 && which != 0) {               // ENHANCEMENT, card only (KitBuilder.Flames): a BOOST FLAME part -
            w.piv = v;                                                   // frame `which` (1-3); channel 3's bits 29-30 the frame showing (0:
            w.mode = ((artic_in.w >> 29) & 3) == which ? 0 : 11;         // no boost); another frame's corners fold onto this one
        } else {
            w.mode = 0;
        }
    } else if (w.mode == 11) {
        v = w.piv;
    } else if (w.mode == 9) {                                            // the hand part's slide corner: its offset from the pivot, times the
        w.off = ivec3(round(vec3(v - w.piv) * float(w.off.x) / 255.0));  // channel, for the rest of the part
        w.mode = 10;
    } else if (w.mode == 10) {
        _subrotate(v, w.piv, w.art, trig);
        v = ivec3(_wrap16(v.x + w.off.x), _wrap16(v.y + w.off.y), _wrap16(v.z + w.off.z));
    } else if (w.mode == 1) {
        _subrotate(v, w.piv, w.art, trig);
    } else if (w.mode == 2) {
        v = ivec3(_wrap16(v.x - w.off.x), _wrap16(v.y - w.off.y), _wrap16(v.z - w.off.z));
    } else if (w.mode == 3) {                                            // the belt's pitch corner: the slide for the rest of the part
        w.off = ivec3(round(vec3(v - w.piv) * float(w.art.x) / 256.0));
        w.mode = 4;
    } else if (w.mode == 4) {
        v = ivec3(_wrap16(v.x + w.off.x), _wrap16(v.y + w.off.y), _wrap16(v.z + w.off.z));
    } else if (w.mode == 5) {                                            // the pitch corner, as mode 3
        w.off = ivec3(round(vec3(v - w.piv) * float(w.art.x) / 256.0));
        w.mode = 6;
    } else if (w.mode == 6) {                                            // the run's end E: the run itself, A to E
        w.art = v - w.piv;
        w.mode = 7;
    } else if (w.mode == 7) {
        // slid, then held on the run: a corner past either end stays at that end, so a bar across the belt thins to nothing
        // as it reaches the end and grows from nothing at the start - it goes round the corner, not off into the air
        vec3 s = vec3(v + w.off - w.piv), run = vec3(w.art);
        float t = dot(s, run) / max(dot(run, run), 1.0);
        v = w.piv + ivec3(round(s - (t - clamp(t, 0.0, 1.0)) * run));
    }
    return _racer_spring(c.xyz, v, artic_in.w >> 8, at);
}

ivec3 model_local(sampler2D trig, int at, int idx, ivec4 artic) {
    _Walk w = _Walk(ivec3(0), ivec3(0), ivec3(0), 0);
    ivec3 v = ivec3(0);
    for (int k = 0; k <= idx; k++) v = _walk_step(w, trig, at, k, artic);
    return v;
}

// A direction (not a corner) at corner idx of the model, turned as that corner is by the articulation:
// the part's own angles, without the walk's pivots and translations (a kind 1 corner is the pivot it
// turns about, unturned itself; a mode 2 part only slides).
ivec3 model_artic_dir(sampler2D trig, int at, int idx, ivec4 artic, ivec3 d) {
    _Walk w = _Walk(ivec3(0), ivec3(0), ivec3(0), 0);
    ivec3 v = ivec3(0);
    for (int k = 0; k <= idx; k++) v = _walk_step(w, trig, at, k, artic);
    int flag = _corner(at, idx).w;
    bool turns = (flag != 0 && ((flag >> 4) & 7) == 2) || (flag == 0 && w.mode == 1);
    if (turns) {
        if (w.art.z != 0) _roll(d.x, d.y, w.art.z, trig);
        if (w.art.x != 0) _pitch(d.y, d.z, w.art.x, trig);
        if (w.art.y != 0) _yaw(d.x, d.z, w.art.y, trig);
    }
    return d;
}

// The same for a thing drawn without rotation (flag bit): corners only scaled and placed.
int model_min_y_plain(int at, int y_origin) {
    int count = _measured(at);
    int low = 0x7FFF;
    for (int k = 0; k < count; k++)
        low = min(low, _wrap16(_corner(at, k).y + y_origin));
    return _wrap16(low + _waterline(at));
}

// How far the game lifts a thing so its lowest corner sits on the ground ($51F8, entity +$7A):
// ground height minus the lowest corner, never below 0.
int lift_to_ground(int ground, int min_y) { return max(_wrap16(ground - min_y), 0); }

// The lowest placed corner (VerticesOf's MinY, $1580): every corner turned and placed, the smallest
// height kept. The game lifts a thing by (ground - this) so its lowest point sits on the ground.
int model_min_y(sampler2D trig, int at, ivec4 artic, ivec3 angles, int turn, ivec3 origin) {
    // STUNTIN's racers: ScrDrive puts the record at its wheels' road height, the car level at rest standing there - so the
    // lowest corner AT REST (unturned, springs still): a constant lift, the tilt and the springs moving the car about it
    // instead of lifting it by whichever tyre is lowest
    if (at == racer_at || at == racer2_at) return model_min_y_plain(at, origin.y);
    int count = _measured(at);
    _Walk w = _Walk(ivec3(0), ivec3(0), ivec3(0), 0);
    int low = 0x7FFF;
    for (int k = 0; k < count; k++) {
        ivec3 v = _walk_step(w, trig, at, k, artic);
        low = min(low, corner(v, angles, turn, origin, trig).y);
    }
    return _wrap16(low + _waterline(at));
}
