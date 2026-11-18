// The things of the world drawn entirely from the game's own data (ENHANCEMENT renderer, godot/real3d/GpuWorld.cs):
// one mesh of E x T x 3 empty vertices, and this vertex stage works out each one from the entity records and the
// objfile - which record, which triangle of its model, which corner - with the game's own maths
// (place.gdshaderinc). A record that is not drawn, or a triangle past its model's count, is parked outside the view.
//   layer 0: the thing's own model ($4EA2's Faces after Project)    layer 1: its flat ground shadow ($52A2)
// Needs place.gdshaderinc. Uniforms:
//   ent_tex  160 x 16384 Rf the entity records, one byte a texel     anim_tex 320 x 2 Rf   the animation table, row 1 one step back
//   entprev_tex 160 x 16384 Rf the records' part bytes +$84 as they were one step back (SMOOTH PARTS)
//   trig_tex   the sinfile (TrigTexture)
//   mdl_tex  1024 x R Rgbaf the card's models (CardModels): header, corners, edges, faces; mdl_at 256 x 256 Rf  objfile offset -> store address (model.gdshaderinc)
//   tri_tex  4096 x N/1024 Rgbaf, 1024 triangles a row (tri_texel) - per triangle: x 0 corner a, b, c (vertex numbers), colour | detail << 4;
//                           x 1 its face (panel): first triangle row, triangle count
//                           x 2 a slab's offset, a thin sheet moved off its plane (GpuWorld.Bake); alpha: the corners' marks, 3 bits a corner
//                           x 3 a rod's second offset (a rod is a line made solid: GpuWorld.RodTriangles)
//   model_tex 256 x 1 Rgbaf per model number: base (objfile offset), first triangle, triangle count
//   extra_tex 128 x N Rgbaf per model (row): its back walls' extra points (GpuWorld.Bake, ModelCaps), model units

#include "res://real3d/debris.gdshaderinc"

uniform sampler2D ent_tex : filter_nearest;
uniform bool painted_light = false;  // the sun shades the models (GpuWorld: sun_shading, the sun up): walls with the painters' light in them are marked (PaintedLight)
uniform sampler2D entprev_tex : filter_nearest;   // SMOOTH PARTS: the records' part bytes one step back
uniform sampler2D anim_tex : filter_nearest;
uniform sampler2D trig_tex : filter_nearest;
uniform sampler2D tri_tex : filter_nearest;
// Where triangle row's column x (0-3) sits in tri_tex: TRI_TEX_PER_ROW triangles a texture row (GpuWorld.TriPerRow).
const int TRI_TEX_PER_ROW = 1024;
ivec2 tri_texel(int row, int x) { return ivec2(x + 4 * (row % TRI_TEX_PER_ROW), row / TRI_TEX_PER_ROW); }
uniform sampler2D model_tex : filter_nearest;
uniform sampler2D extra_tex : filter_nearest;   // the back walls' extra points, one row a model (GpuWorld.Bake)
uniform int tri_slots = 1;
uniform int layer = 0;
uniform bool inside_view = false;
uniform int shell_row = -1;     // inside a building: the building's own record, drawn whatever the usual tests say ($48F8 DrawBuildingShell)

// the thing's seat on the real ground (the shadow is thrown from where the thing is drawn)
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/ground_contact.gdshaderinc"

// Line faces ($7BF2: a face whose kind bits are neither 0 nor $40 is drawn as its edges): the segments as a table,
// line_tex 1 x M Rgbaf per segment: corner a, corner b, colour; model_tex row 1 per model: first segment, count.
uniform sampler2D line_tex : filter_nearest;
uniform int line_slots = 1;

// VERTEX_ID of the line mesh (two per segment) -> a world point and the colour.
bool gpu_line_vertex(int id, out vec3 pos, out int colour) {
    pos = vec3(0.0); colour = 0;
    int per = line_slots * 2;
    int row = id / per;
    int rem = id - row * per;
    int slot = rem / 2;
    int end = rem - slot * 2;
    bool shell = row == shell_row;
    if (_eb(ent_tex, row, 0) == 0 || (!shell && _eb(ent_tex, row, 0x1C) == 0)) return false;
    int b20 = _eb(ent_tex, row, 0x20);
    if (!shell && (inside_view ? b20 == 0 : b20 != 0)) return false;
    int b24 = _sb(_eb(ent_tex, row, 0x24)), b25 = _eb(ent_tex, row, 0x25);
    if (b24 < 0 || b25 == 0) return false;
    vec4 mi = texelFetch(model_tex, ivec2(b25, 0), 0);
    vec4 li = texelFetch(model_tex, ivec2(b25, 1), 0);
    int base = int(mi.r + 0.5), start = int(li.r + 0.5), count = int(li.g + 0.5);
    if (slot >= count) return false;
    vec4 seg = texelFetch(line_tex, ivec2(0, start + slot), 0);
    int vidx = int((end == 0 ? seg.r : seg.g) + 0.5);
    colour = int(seg.b + 0.5);
    ivec3 w = entity_vertex2(trig_tex, ent_tex, entprev_tex, anim_tex, 0, 1, row, base, vidx);
    pos = vec3(float(w.x) * 0.5, float(w.y), float(w.z) * 0.5);
    return true;
}

// A segment's alpha in line_tex: 1 = a window's border (GpuWorld.Bake, the pane on the outer edge), 0 = the model's own line.
bool gpu_line_border(int id) {
    int per = line_slots * 2;
    int row = id / per;
    int slot = (id - row * per) / 2;
    int b25 = _eb(ent_tex, row, 0x25);
    vec4 li = texelFetch(model_tex, ivec2(b25, 1), 0);
    if (b25 == 0 || slot >= int(li.g + 0.5)) return false;
    return texelFetch(line_tex, ivec2(0, int(li.r + 0.5) + slot), 0).a > 0.5;
}

// The original's explosion debris ($4F22 DrawDebrisSpin, debris.gdshaderinc): the thing's model number slot says debris
// (+$24 negative), the model drawn is model 0 as $59FE rewrote it - the victim's scale shift in its header and the
// victim's colours in its first ten faces - ten triangles of 3 corners, each
// pushed out by the game's 30-word offset table (dpos_tex, 30 x 1: X[10], Z[10], Y[10]). VERTEX_ID of the debris mesh.
uniform sampler2D dpos_tex : filter_nearest;
uniform int debris_base = 0;

bool gpu_debris_vertex(int id, out vec3 pos, out int colour) {
    pos = vec3(0.0); colour = 0;
    int row = id / 30;
    int rem = id - row * 30;
    int piece = rem / 3;
    int j = rem - piece * 3;
    if (_eb(ent_tex, row, 0) == 0 || _eb(ent_tex, row, 0x1C) == 0) return false;
    int b20 = _eb(ent_tex, row, 0x20);
    if (inside_view ? b20 == 0 : b20 != 0) return false;
    if (_sb(_eb(ent_tex, row, 0x24)) >= 0) return false;
    int offx = int(texelFetch(dpos_tex, ivec2(piece, 0), 0).r), offz = int(texelFetch(dpos_tex, ivec2(10 + piece, 0), 0).r),
        offy = int(texelFetch(dpos_tex, ivec2(20 + piece, 0), 0).r);
    ivec3 pos2 = ivec3(2 * _euw(ent_tex, row, 0x4A), _ew(ent_tex, row, 0x4C), 2 * _euw(ent_tex, row, 0x4E));
    ivec3 w = debris_point2(trig_tex, debris_base, _eb(ent_tex, row, 0x80),
                            _eb(ent_tex, row, 0x7C), _eb(ent_tex, row, 0x7E), pos2, offx, offy, offz, piece, j);
    pos = vec3(float(w.x) * 0.5, float(w.y), float(w.z) * 0.5);
    colour = _face(_at(debris_base), piece).x & 15;
    return true;
}

// The dot ($5B0A: a thing whose model number is 0 is a dot over its ground dot): two points a record, the thing's
// own spot in colour 2 and the ground under it in colour 0 ($5B7C's plane bits). VERTEX_ID of the point mesh.
bool gpu_dot_vertex(int id, out vec3 pos, out int colour) {
    pos = vec3(0.0); colour = 0;
    int row = id / 2;
    int end = id - row * 2;
    if (_eb(ent_tex, row, 0) == 0 || _eb(ent_tex, row, 0x1C) == 0) return false;
    int b20 = _eb(ent_tex, row, 0x20);
    if (inside_view ? b20 == 0 : b20 != 0) return false;
    if (_sb(_eb(ent_tex, row, 0x24)) < 0 || _eb(ent_tex, row, 0x25) != 0) return false;
    pos = vec3(float(_euw(ent_tex, row, 0x4A)), float(end == 0 ? _ew(ent_tex, row, 0x4C) : _ew(ent_tex, row, 0x74)), float(_euw(ent_tex, row, 0x4E)));
    colour = end == 0 ? 2 : 0;
    return true;
}

// ENHANCEMENT (setting own_shadows, SHADOWS: MORE): a thing with no shadow model of its own throws its main model's
// shadow away from the sun (rules/Objects.cs ProjectModelFlat with the sun): each corner h above the ground moves
// SunShade.ShadowLength(h) along the way the shadows fall (no cap: limits only for artistic effect).
uniform bool own_shadows = false;
uniform bool shadow_sun_up = false;
uniform vec3 sun_shadow = vec3(0.0, 0.0, 1.0);   // Objects.SunOffset: DirX, DirZ (the way shadows fall), tan(elevation)

// One corner of the thrown shadow (rot / low: the corner turned and the model's lowest, entity_corner_rot): world units,
// y = the record's ground (+$74; gpushadow lays it on the ground under the corner). sun = DirX, DirZ, tan(elevation).
// rise: how much higher above the real ground the corner is DRAWN than the game places it (the contact fit and walls
// down, gpu_model_vertex_rot); 0 = as the game has it.
vec3 entity_sun_shadow_corner(sampler2D trig, sampler2D ent, sampler2D pent, sampler2D anim, int arow, int prow, int row, int base, ivec3 rot, int low, vec3 sun, float rise) {
    int x = _euw(ent, row, 0x4A), y = _ew(ent, row, 0x4C), z = _euw(ent, row, 0x4E), ground = _ew(ent, row, 0x74);
    float h = float(_wrap16(_wrap16(rot.y + y) + lift_to_ground(ground, low)) - ground) + rise;
    float d = h > 0.0 ? h / sun.z : 0.0;                                                 // SunShade.ShadowLength: no cap
    return vec3((float(2 * x + rot.x) + sun.x * d) * 0.5, float(ground), (float(2 * z + rot.z) + sun.y * d) * 0.5);
}

// An extra point of the record's model (GpuWorld.Bake's back walls): a corner number at or past the model's own
// corner count - no real face points past the last corner.
// Its place in extra_tex (model units, already shifted), turned and placed as a plain corner
// of that model: no articulation walk, the model's own turn only. low = the model's own lowest corner as
// entity_corner_rot finds it (moving parts included), so every corner of the record gets the same lift.
ivec3 entity_extra_rot(sampler2D ent, sampler2D pent, int arow, int prow, int row, int base, int model, int vidx, out int low) {
    int at = _at(base);
    int count = _head(at).z;
    vec4 p = texelFetch(extra_tex, ivec2(vidx - count, model), 0);
    ivec3 pt = ivec3(int(round(p.r)), int(round(p.g)), int(round(p.b)));
    int y = _ew(ent, row, 0x4C);
    if (entity_unrotated(ent, row)) {
        low = model_min_y_plain(at, y);
        return pt;
    }
    ivec3 ang = ivec3(_eb(ent, row, 0x80), _eb(ent, row, 0x7C), _eb(ent, row, 0x7E));
    low = model_min_y(trig_tex, at, entity_artic(ent, pent, anim_tex, arow, prow, row, base), ang, 0, ivec3(0, y, 0));
    return corner_fine(pt, ang, entity_frac(ent, row), trig_tex);
}

// ENHANCEMENT: a boat drawn sitting in the water, not on it - the game lifts every thing till its
// lowest corner touches the ground ($51F8), so a hull floated on its keel with its bow in the air; drawn a third of that lift
// lower, the sea hides the part under the waterline. Boat: movement mode 10 (kind flags +$08, no higher mode bit).
float boat_sink(int row, int ground, int low) {
    if ((_eb(ent_tex, row, 0x08) & 0xFC) != 0x04) return 0.0;
    return float(lift_to_ground(ground, low) / 3);
}

// VERTEX_ID -> a world point (x, y, z in world units, z north) and the face's colour / pull flag (gpu_model_vertex),
// and the corner as turned but not yet placed (x and z doubled, y whole: the picture's own units, alike every way) -
// for the sun's shading, which needs the face's true direction. False = park it.
// own_side: a slab's face (meta bit 7) - its winding already points away from its twin, so the sun takes it as it is.
// BETTER EXPLOSIONS step 0 (with EXPLOSIONS: PIECES; docs/better-explosions-plan.md): a blast ($61-$67) as a faceted
// fireball (Fireball.cs: its core's faces, meta bit 22) with the star's own spikes round it, its look stepped by the life
// left (+$30, counting down from 32 ticks, $67's 16 - re/formats/weapons.md 3c): BURST - all of it out from 0.4 of its
// size to whole over the first quarter, in steps; BURN - the fire's colours from the hottest, 4 (pale) 7 (yellow) 6 (red)
// 5 (dark red), the core a third of the life on each of 4 7 6 (every other facet a step cooler), a spike never hotter than one step past the core (the edge
// darker) and never hotter than its own paint; FADE - the last third the core shrinks to half, the spikes stay out.
// Stepped, not smooth: a throwback look. Off: the original's star (the core not drawn).
uniform bool better_fire = false;
const int FIRE_HOT[4] = int[4](4, 7, 6, 5);
int _fire_heat(int c) { return c == 4 ? 0 : (c == 7 ? 1 : (c == 6 ? 2 : 3)); }
float fire_age(int row, int type) {
    float life = type == 0x67 ? 16.0 : 32.0;
    return clamp(1.0 - float(_ew(ent_tex, row, 0x30)) / life, 0.0, 1.0);
}
// The corner (rot) and colour of a blast's triangle with the fireball look.
void fire_look(int row, int type, bool core, int slot, inout ivec3 rot, inout int colour) {
    float age = fire_age(row, type);
    int step = min(int(age * 3.0), 2);                              // 0, 1, 2: a third of the life each
    float size = min(1.0, 0.4 + 0.2 * floor(age * 16.0));           // the burst: 0.4 0.6 0.8 1.0, a 16th of the life a step
    if (core && step == 2) size *= 1.0 - 0.5 * min(floor((age - 2.0 / 3.0) * 9.0 + 1.0), 3.0) / 3.0;   // the fade: 5/6, 4/6, 3/6
    rot = ivec3(round(vec3(rot) * size));
    int heat = core ? step + (slot & 1) : max(_fire_heat(colour & 15), step + 1);   // every other core face a step cooler: the facets show (the fire takes no sun)
    colour = (colour & ~15) | FIRE_HOT[min(heat, 3)];
}

bool gpu_model_vertex_rot(int id, out vec3 pos, out int colour, out bool pull, out ivec3 rot, out bool own_side) {
    pos = vec3(0.0); colour = 0; pull = false; rot = ivec3(0); own_side = false;
    int per = tri_slots * 3;
    int row = id / per;
    int rem = id - row * per;
    int slot = rem / 3;
    int corner = rem - slot * 3;
    bool shell = layer == 0 && row == shell_row;
    if (_eb(ent_tex, row, 0) == 0 || (!shell && _eb(ent_tex, row, 0x1C) == 0)) return false;          // not alive / not in view ($4F2E)
    int b20 = _eb(ent_tex, row, 0x20);
    if (!shell && (inside_view ? b20 == 0 : b20 != 0)) return false;                       // Draw's first tests
    int b24 = _sb(_eb(ent_tex, row, 0x24)), b25 = _eb(ent_tex, row, 0x25);
    if (b24 < 0) return false;                                                             // debris: its own draw
    int model;
    bool sun = false;                                                                      // own shadows: the main model, away from the sun
    bool plates = false;                                                                   // the main model's ground plates only
    // own shadows with the sun up: every thing throws its own shape, none of the original's fake ones -
    // not the 14 kinds' flat shadow models (+$24), not the black patches painted under a model (plates)
    int type = _ew(ent_tex, row, 4);
    bool sun_shape = own_shadows && shadow_sun_up && !inside_view && b25 != 0 && !(type >= 0x61 && type <= 0x67);   // fire throws no shadow
    if (layer == 0) { if (b25 == 0) return false; model = b25; }
    else if (sun_shape) { model = b25; sun = true; }
    else if (b24 > 0) model = (b24 + b25) & 255;
    else {
        if (b25 == 0) return false;
        model = b25;
        plates = true;
    }
    vec4 mi = texelFetch(model_tex, ivec2(model, 0), 0);
    int base = int(mi.r + 0.5), start = int(mi.g + 0.5), count = int(mi.b + 0.5);
    if (slot >= count) return false;
    if (inside_view && slot >= count - int(mi.a + 0.5)) return false;                      // indoors no back walls: a room is an open box seen from inside
    vec4 tri = texelFetch(tri_tex, tri_texel(start + slot, 0), 0);
    int vidx = int((corner == 0 ? tri.r : (corner == 1 ? tri.g : tri.b)) + 0.5);
    // the corner's marks (GpuWorld.Marks), 3 bits a corner in x 2's alpha (a, b, c from bit 0): bit 0 = a thin sheet slab's
    // moved corner (GpuWorld's FindSheets; with meta bit 6, the offset in x 2: the same corner less it, entity_corner_rot_thick),
    // bit 1 = a rod's corner less the second offset (x 3), bit 2 = a rod's corner centred on its line - the corner plus both
    // half-offsets, bits 0 / 1 turning one to minus
    vec4 off1 = texelFetch(tri_tex, tri_texel(start + slot, 2), 0);
    int mk = (int(floor(off1.a + 0.5)) >> (3 * corner)) & 7;
    bool moved = (mk & 1) != 0, moved2 = (mk & 2) != 0, centred = (mk & 4) != 0;
    int meta = int(tri.a + 0.5);
    // a ground plate (GpuWorld.Bake: black, every corner on the model's lowest level - the car's underside, a shadow
    // painted into the model) is the card's shadow, not the thing's: the shadow pass lays it on the ground
    bool plate = ((meta >> 5) & 1) != 0;
    if (layer == 0 ? plate && !shell : (plates && !plate) || (sun && plate)) return false;
    if (layer != 0 && !sun && ((meta >> 8) & 1) != 0) return false;                       // a rod (meta bit 8) casts a flat shadow only in the sun's
    if (layer != 0 && ((meta >> 13) & 1) != 0) return false;                              // the sunk glass and its frame (bit 13) throw no shadow of their own
    colour = (meta & 15) | (painted_light && !inside_view && ((meta >> 10) & 1) != 0 ? 128 | ((meta >> 11) & 1) * 256 : 0);   // + 128 painted light, + 256 a copied back
    pull = ((meta >> 4) & 1) != 0;
    own_side = ((meta >> 7) & 1) != 0;
    bool fire = type >= 0x61 && type <= 0x67;
    bool core = ((meta >> 22) & 1) != 0;                                                  // a fireball core's face (Fireball.Bit)
    if (core && !(better_fire && fire && layer == 0)) return false;                       // only the fireball look draws it (no shadow either)
    if (((meta >> 17) & 1) != 0) {                                                       // a driver's face (CarInside.Driver)
        if ((_eb(ent_tex, row, 6) & 0x10) != 0) return false;                             // d7 bit 28 set: empty, no one to draw
        if (_eb(ent_tex, row, 1) == 1) colour = (colour & ~15) | ((meta >> 18) & 15);     // variant 1: the red look
        own_side = true;                                                                  // lit as wound (the figure stands off the model's middle)
    }
    int low;
    bool slab = ((meta >> 6) & 1) != 0;
    ivec3 thick;
    ivec3 d1 = ivec3(floor(off1.xyz + vec3(0.5)));
    ivec3 d2 = ivec3(floor(texelFetch(tri_tex, tri_texel(start + slot, 3), 0).xyz + vec3(0.5)));
    if (centred) thick = (moved ? d1 : -d1) + (moved2 ? d2 : -d2);
    else thick = (moved ? d1 : ivec3(0)) + (moved2 ? d2 : ivec3(0));
    if ((moved || moved2 || centred) && slab)
        rot = entity_corner_rot_thick(trig_tex, ent_tex, entprev_tex, anim_tex, 0, 1, row, base, vidx, thick, low);
    else if (vidx >= _head(_at(base)).z)   // a back wall's extra point
        rot = entity_extra_rot(ent_tex, entprev_tex, 0, 1, row, base, model, vidx, low);
    else
        rot = entity_corner_rot(trig_tex, ent_tex, entprev_tex, anim_tex, 0, 1, row, base, vidx, low);
    int x = _euw(ent_tex, row, 0x4A), y = _ew(ent_tex, row, 0x4C), z = _euw(ent_tex, row, 0x4E), ground = _ew(ent_tex, row, 0x74);
    if (layer == 0) {
        if (better_fire && fire) fire_look(row, type, core, slot, rot, colour);
        ivec3 w = ivec3(2 * x + rot.x, _wrap16(_wrap16(rot.y + y) + lift_to_ground(ground, low)), 2 * z + rot.z);   // entity_vertex2
        pos = vec3(float(w.x) * 0.5, float(w.y) - boat_sink(row, ground, low), float(w.z) * 0.5);
    } else if (!sun) {
        pos = vec3(float(2 * x + rot.x) * 0.5, float(ground), float(2 * z + rot.z) * 0.5);                      // entity_shadow_vertex2
    } else {
        // thrown from where the thing is drawn: its height above the real ground under the corner, with the fit and walls down
        float rise = -boat_sink(row, ground, low);
        vec4 fit; vec2 fc;
        if (contact_fit_row(row, fit, fc)) {
            vec3 p = vec3(float(2 * x + rot.x) * 0.5, float(_wrap16(_wrap16(rot.y + y) + lift_to_ground(ground, low))), float(2 * z + rot.z) * 0.5);
            float drawn = walls_down(fc, p.x, p.z, contact_turn(fit, fc, p, float(rot.x) * 0.5, float(rot.z) * 0.5).y);   // the drawn height (a turned vehicle's small move sideways is left out of its shadow)
            rise += drawn - p.y - float(ground_height(cam_map, int(p.x) & 0xFFFF, int(p.z) & 0xFFFF) - ground);
        }
        pos = entity_sun_shadow_corner(trig_tex, ent_tex, entprev_tex, anim_tex, 0, 1, row, base, rot, low, sun_shadow, rise);
    }
    return true;
}

// The table row of the triangle that vertex id starts (corners, meta).
vec4 tri_row(int id) {
    int per = tri_slots * 3;
    int row = id / per;
    int slot = (id - row * per) / 3;
    vec4 mi = texelFetch(model_tex, ivec2(_eb(ent_tex, row, 0x25), 0), 0);
    return texelFetch(tri_tex, tri_texel(int(mi.g + 0.5) + slot, 0), 0);
}

// A triangle whose corners all sit on a moving part (meta bit 9, GpuWorld.Bake): blades, wheels, fans keep their paint.
bool on_part(int id) { return (int(tri_row(id).a + 0.5) & 0x200) != 0; }

// The glass bits of a triangle's meta: 12 a glass face (Glass.Bit), 13 drawn only with GLASS: SEE-THROUGH (Glass.Sunk: the
// sunk pane, the frame walls and the holed walls), 14 a glass face at rest that has a sunk copy (Glass.Replaced), 15 a wall's
// own triangle, hidden with SEE-THROUGH where its holed copy is drawn (GlassWalls.HiddenBit).
int glass_bits(int id) { return int(tri_row(id).a + 0.5) & 0xF000; }

// A face drawn from its front only (meta bit 23, GpuWorld.FindBacks): one of two faces in one place looking opposite
// ways, of which the original shows only the one facing the eye. Its triangles are wound as the face is.
bool front_only(int id) { return ((int(tri_row(id).a + 0.5) >> 23) & 1) != 0; }

// A thin sheet's parts that stay as thin as they are (GpuWorld.Bake), not widened to a game pixel: a slab's side
// (meta bit 6, not a rod, some corners moved and some not), the thin edge between the sheet's two copies, and the
// sheet's resting copy (bit 7 without bit 6) - edge-on, its moved copy alone is widened, one line of one colour.
bool stays_thin(int id) {
    vec4 tri = tri_row(id);
    int meta = int(tri.a + 0.5);
    if (((meta >> 8) & 1) != 0) return false;                                  // a rod
    if (((meta >> 6) & 1) == 0) return ((meta >> 7) & 1) != 0;                 // a sheet's resting copy
    int per = tri_slots * 3;
    int row = id / per;
    int slot = (id - row * per) / 3;
    vec4 mi = texelFetch(model_tex, ivec2(_eb(ent_tex, row, 0x25), 0), 0);
    int mk = int(floor(texelFetch(tri_tex, tri_texel(int(mi.g + 0.5) + slot, 2), 0).a + 0.5));   // x 2 alpha: 3 bits a corner, bit 0 moved
    int moved = (mk & 1) + ((mk >> 3) & 1) + ((mk >> 6) & 1);
    return moved == 1 || moved == 2;
}

bool gpu_model_vertex(int id, out vec3 pos, out int colour, out bool pull) {
    ivec3 rot; bool own_side;
    return gpu_model_vertex_rot(id, pos, colour, pull, rot, own_side);
}

// The record's model centre (the middle of its corners at rest, GpuWorld's model_tex row 2) turned as its corners are,
// not yet placed: the faces' "outward" is away from it.
ivec3 entity_centre_rot(int row, int model) {
    vec4 c = texelFetch(model_tex, ivec2(model, 2), 0);
    ivec3 local = ivec3(int(round(c.r)), int(round(c.g)), int(round(c.b)));
    if (entity_unrotated(ent_tex, row)) return local;
    return corner_fine(local, ivec3(_eb(ent_tex, row, 0x80), _eb(ent_tex, row, 0x7C), _eb(ent_tex, row, 0x7E)), entity_frac(ent_tex, row), trig_tex);
}
