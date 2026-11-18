// ENHANCEMENT (renderer = 3d): the world's things drawn by the card from the entity records (gpumodels.gdshaderinc),
// coloured by world_colour.gdshaderinc (world_hazed).
//
// THIN FACES: the game's polygon fill paints every span from its left pixel to its right pixel INCLUSIVE ($78C8-$7938,
// rules/Raster.Fill), so a face only a fraction of a pixel across still paints a whole pixel - the lettering on signs
// and books is made of such faces. A card paints a pixel only if its centre is inside the triangle, so those slivers
// vanish. Each triangle is therefore measured on the screen here, and when it is thinner than one GAME pixel
// (gpu_px viewport pixels) its far corner is moved out to make it one game pixel across and its long edge is
// extended half a pixel at each end.

#include "res://real3d/place.gdshaderinc"
#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/sunshade.gdshaderinc"
#include "res://real3d/own_colours.gdshaderinc"
#include "res://real3d/gpumodels.gdshaderinc"
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/ground_contact.gdshaderinc"

uniform vec2 vp_size = vec2(1280.0, 560.0);      // the 3D viewport in pixels (Real3DView)
uniform bool glass = false;                       // GLASS: see-through windows (GpuWorld sets it; meta bit 12 marks the faces)
const float LAYER_NEARER = 0.002;                 // how much nearer the eye a face is drawn for each layer it lies on in its own plane (measured on the truck's wheel: 0.00016 lost to the face under it in a patch, 0.0016 won everywhere)
const float GLASS_SEE = 0.35;                    // how much of the glass's own colour shows (the rest is what is behind it)
const vec3 GLASS_TINT = vec3(0.85, 0.93, 1.1);    // the pane's light grey, a little blue
varying flat float glass_dark;                    // a military machine's panes: their brightness (GpuWorld.OwnTints Glass; 1 the pale glass)
varying flat float glass_see;                     // ...and how much of the pane shows (OwnTints Opaque; GLASS_SEE otherwise)

varying flat float tag;
varying float depth_;                             // HAZE: how far ahead of the eye (POSITION.w)
varying flat vec3 own_tint;                       // MODEL COLOURS: this face's tint, 0 = none (the palette's)
varying flat float own_light;                     // ...and the sun's light on it (sun_light64 / 64); untinted: SunShade.Over


// One corner of a triangle in clip space (the id's own world point moved to the eye, nearer if a detail face, back with
// the shell indoors), sat on the real ground when contact (ground_contact.gdshaderinc); false = this triangle is not drawn.
bool model_clip(int id, mat4 mv, mat4 proj, bool contact, vec4 fit, vec2 fc, out vec4 clip, out int colour, out ivec3 rot, out bool own_side) {
    vec3 p; bool pull;
    clip = vec4(2.0, 2.0, 2.0, 1.0);
    if (!gpu_model_vertex_rot(id, p, colour, pull, rot, own_side)) return false;
    if (contact) {
        vec3 q = contact_turn(fit, fc, p, float(rot.x) * 0.5, float(rot.z) * 0.5);
        rot += ivec3(int(round((q.x - p.x) * 2.0)), int(round(q.y - p.y)), int(round((q.z - p.z) * 2.0)));   // the sun's face direction leans (a vehicle: turns) with it
        p = q;
        p.y = walls_down(fc, p.x, p.z, p.y);
    }
    vec3 eye = (mv * vec4(view_rel(vec3(p.x, p.y, -p.z)), 1.0)).xyz;
    float k = (shell_row >= 0 && id / (tri_slots * 3) == shell_row) ? 1.25 : 1.0;   // indoors the building's shell is a backdrop ($48F8)
    int slot = (id - (id / (tri_slots * 3)) * (tri_slots * 3)) / 3;                      // the triangle's place in its model's face list
    // later faces win a tie on a shared plane, as the painter's order did ($77AA walks the list in order): a face lying on
    // earlier faces of its own plane is drawn LAYER_NEARER closer for each layer (face_layer) - a step rounding cannot beat;
    // the hair for the face's place in the list stays for faces that only meet along an edge
    float order = 1.0 - float(slot) * 0.000002 - float(face_layer(id)) * LAYER_NEARER;
    clip = proj * vec4((pull ? eye * 0.996 : eye) * k * order, 1.0);
    return clip.w > 0.01;
}

void vertex() {
    int corner = VERTEX_ID - (VERTEX_ID / 3) * 3;
    int first = VERTEX_ID - corner;
    vec4 c0, c1, c2;
    int col0, col1, col2;
    ivec3 r0, r1, r2;
    vec4 fit; vec2 fc;
    bool own_side;
    int gbits = glass_bits(first);                             // meta bits 12-15, told before the corners are worked out
#ifdef GLASS_PASS
    bool ok = glass && (gbits & 0x1000) != 0 && (gbits & 0x4000) == 0;   // the glass pass: the glass faces and sunk panes, not a face that was sunk; none with ORIGINAL
#else
    bool ok = glass ? (gbits & 0x9000) == 0 : (gbits & 0x2000) == 0;     // the opaque pass: no glass faces and no walls to be holed (bit 15) with SEE-THROUGH, no sunk panes, frames or holed walls without
#endif
    int per = tri_slots * 3, row0 = first / per, b25 = _eb(ent_tex, row0, 0x25);   // a slot past the main model's count (or no model): dropped
    if (ok) ok = b25 != 0 && (first - row0 * per) / 3 < int(texelFetch(model_tex, ivec2(b25, 0), 0).b + 0.5);   // before the fit, as model_clip would
    bool contact = ok && contact_fit_row(row0, fit, fc);      // the record: one fit for its whole model, only for a triangle drawn
    if (ok) {
        ok = model_clip(first, MODELVIEW_MATRIX, PROJECTION_MATRIX, contact, fit, fc, c0, col0, r0, own_side);
        ok = model_clip(first + 1, MODELVIEW_MATRIX, PROJECTION_MATRIX, contact, fit, fc, c1, col1, r1, own_side) && ok;
        ok = model_clip(first + 2, MODELVIEW_MATRIX, PROJECTION_MATRIX, contact, fit, fc, c2, col2, r2, own_side) && ok;
    }
    if (!ok) {
        tag = 0.0;
        POSITION = vec4(2.0, 2.0, 2.0, 1.0);                   // outside the clip volume: nothing is drawn
    } else {
        vec4 mine = corner == 0 ? c0 : (corner == 1 ? c1 : c2);
        int col = corner == 0 ? col0 : (corner == 1 ? col1 : col2);
        bool painted = (col & 128) != 0, copied = (col & 256) != 0;           // PaintedLight: the painters' light in it, a copied back
        col &= 127;
        int row = first / (tri_slots * 3);
        int type = _ew(ent_tex, row, 4);
        bool fire = type >= 0x61 && type <= 0x67;                        // a shot's blast ($61-$67) is fire: never the sun's shades
        if (fire) col += 64;                                             // ...and not darkened by the night
        own_tint = own_tint_of(_eb(ent_tex, row, 0x25), col, on_part(first));   // MODEL COLOURS: a machine's parts keep their paint
        bool tinted = own_tint != vec3(0.0);
        // a tinted machine (MODEL COLOURS a = 1: helicopters, tanks): every green face one paint, the helicopter's tail green (14) -
        // its several greens are the original's painted shading, which under the tint and the sun's own shading made patches
        // of grey; the sun alone shades the faces apart
        bool military = texelFetch(own_tex, ivec2(_eb(ent_tex, row, 0x25), 0), 0).a > 0.75;
        if (tinted && military) { col = int(texelFetch(own_tex, ivec2(_eb(ent_tex, row, 0x25), 1), 0).r + 0.5); painted = false; }   // its Base (GpuWorld.OwnTints)
        vec4 own1 = texelFetch(own_tex, ivec2(_eb(ent_tex, row, 0x25), 1), 0);
        glass_dark = military ? own1.g : 1.0;                      // GLASS: a military machine's window tint (GpuWorld.OwnTints)
        glass_see = military ? own1.b : GLASS_SEE;
        own_light = 1.0;
        if (!fire && sun_shading && sun_up && !inside_view && row != shell_row) {
            // the sun on this face (rules/SunShade.cs Pick): its direction from its own three corners, outward = away
            // from the model's centre, the light the cosine to the sun
            vec3 a = vec3(r0), b = vec3(r1), cc = vec3(r2);
            vec3 n = cross(b - a, cc - a);
            float len = length(n);
            if (len > 0.0) {
                n /= len;
                vec3 out_dir = (a + b + cc) / 3.0 - vec3(entity_centre_rot(row, _eb(ent_tex, row, 0x25)));
                if (!own_side && dot(n, out_dir) < 0.0) n = -n;                  // a slab's face keeps its own side
                // painted light: against the painters' sun on the side the paint was made for (a copied back: the opposite)
                float kp = dot(copied ? -n : n, painter_dir);
                if (tinted) own_light = float(sun_light64(dot(n, sun_dir))) / float(painted ? sun_light64(kp) : 64);
                else {
                    // its own paint times the sun's light, smooth as the ground's and the tinted machines' - the nearest shade of its
                    // palette family (SunShade.Pick) jumped from shade to shade as a car turned
                    float k = dot(n, sun_dir);
                    if (col >= 1 && col <= 14) own_light = float(sun_light64(k)) / float(painted ? sun_light64(kp) : 64);
                }
            }
        }
        tag = float(col);
        vec2 half_vp = vp_size * 0.5;
        vec2 s0 = c0.xy / c0.w * half_vp, s1 = c1.xy / c1.w * half_vp, s2 = c2.xy / c2.w * half_vp;
        // the longest edge (i, j) and the corner k opposite it
        float l01 = length(s1 - s0), l12 = length(s2 - s1), l20 = length(s0 - s2);
        int i = 0, j = 1, k = 2;
        float len = l01;
        if (l12 > len) { i = 1; j = 2; k = 0; len = l12; }
        if (l20 > len) { i = 2; j = 0; k = 1; len = l20; }
        vec2 si = i == 0 ? s0 : (i == 1 ? s1 : s2);
        vec2 sj = j == 0 ? s0 : (j == 1 ? s1 : s2);
        vec2 sk = k == 0 ? s0 : (k == 1 ? s1 : s2);
        vec2 mine_px = mine.xy / mine.w * half_vp;
        if (len > 0.0001) {
            vec2 d = (sj - si) / len;
            vec2 n = vec2(-d.y, d.x);
            float t = dot(sk - si, n);
            float h = abs(t);
            if (h < gpu_px && !stays_thin(first)) {                                       // a sheet's side and resting copy stay as thin as they are
                // one game pixel across, widened evenly about the triangle's own line: an edge-on face's two
                // triangles then widen alike
                vec2 out_ = n * (t >= 0.0 ? 1.0 : -1.0) * (gpu_px - h) * 0.5;
                if (corner == k) mine_px += out_;                                     // the far corner out half the shortfall
                else if (corner == i) mine_px += -out_ - d * 0.5 * gpu_px;            // the long edge back the other half,
                else mine_px += -out_ + d * 0.5 * gpu_px;                             // and half a pixel longer each end
            }
        }
        POSITION = vec4(mine_px / half_vp * mine.w, mine.z, mine.w);
    }
    depth_ = POSITION.w;
}

void fragment() {
    float d = cam_interior ? 0.0 : depth_;
    if (own_tint != vec3(0.0))
        ALBEDO = to_linear(floor(mix(tinted_rgb8(int(round(tag)), FRAGCOORD, own_tint, own_light), sky_nib(5) * 17.0, haze_amount(d)) + 0.5) / 255.0);   // lit and hazed as world_lit_hazed
    else if (own_light != 1.0)
        ALBEDO = world_lit_hazed(int(round(tag)), FRAGCOORD, d, own_light);   // lighter than its lightest paint (SunShade.Over)
    else
        ALBEDO = world_hazed(int(round(tag)), FRAGCOORD, d);
#ifdef GLASS_PASS
    // the original paints its windows black, so 35% of the face's own paint only dimmed what lay behind - the panes looked
    // like holes. A pane is the palette's light grey (8, faded with the day and hazed as any face) with a little blue:
    // pale glass, the inside still showing through
    ALBEDO = world_hazed(8, FRAGCOORD, d) * GLASS_TINT * glass_dark;
    ALPHA = glass_see;
#endif
}
