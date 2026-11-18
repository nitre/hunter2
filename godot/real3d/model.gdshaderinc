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
// follow the part that moved, kind 3 = the card's belt part (TankTracks), otherwise plain). artic =
// the four channel angle bytes.
struct _Walk { ivec3 piv; ivec3 off; ivec3 art; int mode; };   // art = (pitch, yaw, roll) bytes

// TankTracks' clods (kind 5): each lets go of the belt where it rests - most just inside the track's rear run, where the belt
// climbs round the last wheel, a few inside its top rear corner - with a small hop up and back, falls to the hull's bottom
// (the ground) and lies there; the tank drives on, so in its own frame the clod falls back by the tank's speed, in the air
// and on the ground (channel 1 = the speed +$3E: at 5 the tank covers ~4 world units a blank, ~6.7 model units - so 32 model
// units a speed step over the 24 blanks of a flight, about 1.3 a blank). Channel 2 = where the clod is in its cycle of
// FLING_BLANKS of the game's clock (place.gdshaderinc entity_artic): the first CLOD_FLIGHT_BLANKS it flies, the rest (26
// blanks, over half a second at 50 a second) it lies on the ground, then it is back at its rest spot inside the track.
const float FLING_BLANKS = 50.0, CLOD_FLIGHT_BLANKS = 24.0;
const int CLOD_HOP = 20, CLOD_OUT = 4, CLOD_BACK = 32;
const int CLOD_BACK_MAX = 70;   // in flight no further back than two wheels' length (72; the wheels are 32 and 40 across) behind
                                // the belt's rearmost point: a clod's back corner can rest 2 behind it

// Corner k of the model, given what the corners before it set up in w.
ivec3 _walk_step(inout _Walk w, sampler2D trig, int at, int k, ivec4 artic) {
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
        } else if (kind == 3) {                                          // ENHANCEMENT, card only (TankTracks): a belt part - this corner
            w.piv = v;                                                   // is the run's start A, the next one A + the pitch D; the corners
            w.art = ivec3(((axis + 1) * artic[which]) & 255, 0, 0);      // after slide by D x (L x channel) mod 256 / 256, L = axis + 1
            w.mode = 3;                                                  // (the rules' walk treats kind 3 as plain, $551E)
        } else if (kind == 5) {                                          // ENHANCEMENT, card only (TankTracks' clods): a clod - this corner
            // and the ones after: still (channel 1 = 0) where they rest - inside the track, and where the rules' walk has them, so a
            // blast's pieces start there (CheckPieces); moving, thrown along an arc that ends with this corner (the clod's lowest)
            // on the hull's bottom - the flag's bits 8 up are how far it rests above it - and then lying there, left behind
            float u = float((artic[2] + (flag & 15) * 16) & 255) / 256.0, fly = CLOD_FLIGHT_BLANKS / FLING_BLANKS;
            float t = min(u / fly, 1.0), sp = float(artic[1]), drop = float(flag >> 8);
            float back = min(float(CLOD_BACK) * sp, float(CLOD_BACK_MAX)) * t + float(CLOD_BACK) * sp * max(u - fly, 0.0) / fly;
            w.off = artic[1] == 0 ? ivec3(0) : ivec3(round(vec3(
                float(v.x < 0 ? -CLOD_OUT : CLOD_OUT) * t,
                float(CLOD_HOP) * 4.0 * t * (1.0 - t) - drop * t * t,
                -back)));
            v = ivec3(_wrap16(v.x + w.off.x), _wrap16(v.y + w.off.y), _wrap16(v.z + w.off.z));
            w.mode = 4;
        } else {
            w.mode = 0;
        }
    } else if (w.mode == 1) {
        _subrotate(v, w.piv, w.art, trig);
    } else if (w.mode == 2) {
        v = ivec3(_wrap16(v.x - w.off.x), _wrap16(v.y - w.off.y), _wrap16(v.z - w.off.z));
    } else if (w.mode == 3) {                                            // the belt's pitch corner: the slide for the rest of the part
        w.off = ivec3(round(vec3(v - w.piv) * float(w.art.x) / 256.0));
        w.mode = 4;
    } else if (w.mode == 4) {
        v = ivec3(_wrap16(v.x + w.off.x), _wrap16(v.y + w.off.y), _wrap16(v.z + w.off.z));
    }
    return v;
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
    int count = _measured(at);
    _Walk w = _Walk(ivec3(0), ivec3(0), ivec3(0), 0);
    int low = 0x7FFF;
    for (int k = 0; k < count; k++) {
        ivec3 v = _walk_step(w, trig, at, k, artic);
        low = min(low, corner(v, angles, turn, origin, trig).y);
    }
    return _wrap16(low + _waterline(at));
}
