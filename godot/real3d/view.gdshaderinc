// The camera in the shaders (ENHANCEMENT renderer, setting 3D DRAWN BY shaders): Godot's Camera3D stays at the origin
// as the lens only (256 pixels' focal length, the horizon on view row $2E, Real3DView), and every card-built shader
// moves the world to the eye itself. The eye is the game's: PlaceCameraAt $3750 (camera.gdshaderinc) from the player's
// record (slot 0: x +$4A, y +$4C, z +$4E), the camera distance ($1526) and lift ($1532), and the ground height under
// the camera ($36AA, ground_height.gdshaderinc): x = $1548 / 2, z = ($1544 - $100) / 2, height = $0E (the 3D view's
// Eye, rules/View3D.cs). NEW, not the original: the yaw (Q / E turn) swings the eye round the player at the same
// distance and height, as View3D.Eye does.
// Needs corner.gdshaderinc (_wrap16), ground_height.gdshaderinc and camera.gdshaderinc, and place.gdshaderinc (_euw, _ew).

#include "res://real3d/place.gdshaderinc"
#include "res://real3d/ground_height.gdshaderinc"
#include "res://real3d/camera.gdshaderinc"

uniform bool cam_on = false;
uniform sampler2D cam_ent : filter_nearest;      // the entity records (the player is row 0)
uniform sampler2D cam_map : filter_nearest;      // the 128 x 128 height map
uniform int cam_distance = 0x1000;               // $1526
uniform int cam_lift = 0;                        // $1532
uniform float cam_yaw = 0.0;                     // radians, 0 = north, positive turns left
uniform float cam_near = 1.0;                    // NEW (DEMO CAMERA: the car's close-up): the eye at this share of the way from him
// Inside a building ($48BE InteriorView): the camera is on the BUILDING (its record), not the player, and the horizon is
// $1518 plus the building's ground ($48D8) - the distance and lift are the interior ones the rules hold.
uniform int cam_row = 0;                         // the record the camera stands on: the player (0), or the building
uniform bool cam_interior = false;
uniform int cam_interior_h = 0;                  // $1518
// NEW, not the original (the showcase's camera, recording only): the eye anywhere (x, height, z north in world units,
// as View3D.Eye), looking toward cam_free_yaw and down by cam_free_pitch (radians) - rules/View3D.cs Relative.
uniform bool cam_free = false;
uniform vec3 cam_free_eye = vec3(0.0);
uniform float cam_free_yaw = 0.0;
uniform float cam_free_pitch = 0.0;

// The point relative to an eye at (ex, eh, ez), turned by yaw and tilted down by pitch. The tilt works on the halved
// heights the world node shows, so the heights are halved before it and doubled after (MODELVIEW_MATRIX halves them).
vec3 view_from(vec3 local, float ex, float eh, float ez, float yaw, float pitch) {
    vec3 d = local - vec3(ex, eh, -ez);                                               // the world's z is the node's -z
    float c = cos(yaw), sn = sin(yaw);
    vec3 v = vec3(c * d.x - sn * d.z, d.y, sn * d.x + c * d.z);                       // R(-yaw): Godot's +y turn looks left
    if (pitch == 0.0) return v;
    float cp = cos(pitch), sp = sin(pitch), h = v.y * 0.5;
    return vec3(v.x, (cp * h - sp * v.z) * 2.0, sp * h + cp * v.z);
}

// A world point as the 3D world node holds it (x, height, -z in world units) -> relative to the eye and turned by the
// yaw, ready for MODELVIEW_MATRIX (the world node's own scale, the camera at the origin). Unchanged when cam_on is false.
// The game camera's spot and eye height, from the player's record (used by view_rel and view_pixel).
CameraSpot _game_eye(out int eh) {
    int px = _euw(cam_ent, cam_row, 0x4A), py = _ew(cam_ent, cam_row, 0x4C), pz = _euw(cam_ent, cam_row, 0x4E);
    CameraSpot s = camera_spot(px, pz, cam_distance);
    int ground = ground_height_smooth(cam_map, s.ground_x & 0xFFFF, s.ground_z & 0xFFFF);   // FINER GROUND: the smooth lookup, as Ground.Place
    eh = cam_interior ? _wrap16(cam_interior_h + _ew(cam_ent, cam_row, 0x74)) : camera_horizon(py, ground, cam_lift);
    return s;
}

vec3 view_rel(vec3 local) {
    if (!cam_on) return local;
    if (cam_free) return view_from(local, cam_free_eye.x, cam_free_eye.y, cam_free_eye.z, cam_free_yaw, cam_free_pitch);
    int ih;
    CameraSpot s = _game_eye(ih);
    float eh = float(ih);
    float ex = float(s.view_x) * 0.5, ez = float(s.view_z - 0x100) * 0.5;
    if (cam_yaw != 0.0) {
        float qx = ex, qz = ez + float(cam_distance + 0x100) * 0.5;               // the player's spot, north of the eye
        float dx = ex - qx, dz = ez - qz;
        float c = cos(cam_yaw), sn = sin(cam_yaw);
        ex = qx + dx * c - dz * sn;
        ez = qz + dx * sn + dz * c;
        // the eye's height from the ground where the turned eye stands (Ground.Place's rule, its spot turned with the
        // eye): the unturned spot's height left the eye inside a slope behind him when the view turned uphill (the
        // bot's TurnView, Q / E) - the red-and-blue view of the bot's Mission 2 (2026-11-11)
        float half = float(cam_distance) * 0.5;                                    // Place reads the ground distance / 2 behind him (distance is doubled units)
        int gx = int(round(qx + half * sn)), gz = int(round(qz - half * c));
        int turned = ground_height_smooth(cam_map, gx & 0xFFFF, gz & 0xFFFF);
        eh = float(camera_horizon(_ew(cam_ent, cam_row, 0x4C), turned, cam_lift));
    }
    if (cam_near < 1.0) {                                                         // the close-up: toward him, height too (DemoCamera.EyeAt)
        float qx = float(s.view_x) * 0.5, qz = float(s.view_z - 0x100) * 0.5 + float(cam_distance + 0x100) * 0.5;
        float qy = float(_ew(cam_ent, cam_row, 0x4C));
        ex = qx + (ex - qx) * cam_near;
        ez = qz + (ez - qz) * cam_near;
        eh = qy + (eh - qy) * cam_near;
    }
    return view_from(local, ex, eh, ez, cam_yaw, 0.0);
}

// divs.w's quotient: truncated toward zero (GLSL leaves negative int division loose); false on overflow (no pixel).
bool _divs_q(int n, int d, out int q) {
    q = (n < 0) == (d < 0) ? abs(n) / abs(d) : -(abs(n) / abs(d));
    return q >= -32768 && q <= 32767;
}

// The original's own pixel for a point (x +$4A, y, z +$4E, whole world units) on the game's camera: Place's view copy
// ($41FC, rules/Objects.cs Place: cx = 2x - $1548, cz = 2z - $1544) and Pixel ($5B0A, rules/Objects.cs Pixel:
// sx = $A0 + divs(cx << 8, cz + $100), sy = $2E + divs((short)($0E - y) << 8, cz + $100)). Worked from the same camera
// view_rel uses, so the in-between pictures match. False (place it in 3D instead) for the free camera, the Q / E turn, the close-up,
// or a point the original would not divide (cz + $100 <= 0: behind the eye). No screen-edge test: the card clips.
bool view_pixel(int x, int y, int z, out ivec2 pix) {
    pix = ivec2(0);
    if (!cam_on || cam_free || cam_yaw != 0.0 || cam_near < 1.0) return false;
    int eh;
    CameraSpot s = _game_eye(eh);
    int cx = _wrap16(x * 2 - s.view_x), cz = _wrap16(z * 2 - s.view_z);
    int zz = _wrap16(cz + 0x100);
    int qx, qy;
    if (zz <= 0 || !_divs_q(cx * 256, zz, qx) || !_divs_q(_wrap16(eh - y) * 256, zz, qy)) return false;
    pix = ivec2(_wrap16(qx + 0xA0), _wrap16(qy + 0x2E));
    return true;
}
