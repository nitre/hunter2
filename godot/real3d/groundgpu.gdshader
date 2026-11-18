shader_type spatial;
render_mode unshaded, cull_disabled;

// ENHANCEMENT (renderer = 3d): the whole map's ground drawn by the card from the map and its
// type bytes (ground_cell.gdshaderinc: DrawView $39B0's cell tables) - 128 x 128 cells x 2 triangles x 3 corners
// of empty vertices, each worked out here. Coloured by world_colour.gdshaderinc (world_hazed).

#include "res://real3d/corner.gdshaderinc"
#include "res://real3d/ground_cell.gdshaderinc"
#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/sunshade.gdshaderinc"
#include "res://real3d/ground_pattern.gdshaderinc"
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/ground_bands.gdshaderinc"

uniform sampler2D map : filter_nearest;      // 128 x 128 Rf: the height bytes
uniform sampler2D types : filter_nearest;    // 128 x 128 Rf: the cell type bytes

// GROUND: TRUE HEIGHT (Ground.Height $36AA): every drawn triangle cut into cuts x cuts small triangles whose
// corners take ground_height at their own (x, z) - the card's ground follows the game's smooth bilinear height
// inside the cell. 1 = the flat triangle below, bit for bit. The mesh is 128*128*2*3*cuts*cuts (GpuWorld).
uniform int cuts = 1;

// The cut's lattice corners can sit exactly on the world's far edge (x = 65536), one column past ground_height's
// texture; the game's memory walk carries that into the next row's first point (ground_point does the same walk),
// which is ground_height at x = 0 one cell down (the x fraction is 0 there, so no other term survives).
int cut_height(sampler2D hmap, int x, int z) {
    if (z >= 65536) return 0;                                      // past the last row the memory walk is zero
    if (x >= 65536) return z + 512 >= 65536 ? 0 : ground_height(hmap, 0, z + 512);
    return ground_height(hmap, x, z);
}

// 3D VIEW DISTANCE (rules/ViewDistance.cs): the cells to draw, one texel a cell (nonzero = drawn); win_all = every cell.
uniform bool win_all = true;
uniform sampler2D win_tex : filter_nearest;
// GROUND SHAPE over the view distance (GpuWorld.Fit): the mesh covers only this rectangle of cells, the drawn ones' bounds
uniform ivec2 win_origin = ivec2(0, 0);
uniform ivec2 win_size = ivec2(128, 128);

varying flat float tag;
varying flat float banded_;                       // GROUND COLOURS: BANDS - a banded piece (no specks: the pieces are the grain)
varying vec3 band_at;                             // the pixel's ground point (x, height, z), its band colour worked out there (ground_point_word)
varying flat float band_check_;                   // its square's checker (ground_band_checker)
varying float ground_light;                       // SUN SHADING: the square's own colour times this (1 = as it is), blended from its points

// How far ahead of the eye a ground spot is, in world units (the haze's depth).
float ground_ahead(float x, float z) { return -view_rel(vec3(x, 0.0, -z)).z; }

// The far edge, as the original's far row ($3B1C, rules/Ground.cs Project): the ground ends at a fixed distance ahead
// (haze_far), not at a whole square, and a point past it is pulled back onto that line, its height the land's there -
// so a far hill grows a little every step instead of a whole row popping up.
ivec3 ground_far_edge(ivec3 p) {
    if (!cam_on || cam_interior) return p;
    float d = ground_ahead(float(p.x), float(p.z));
    if (d <= haze_far) return p;
    vec2 g = vec2(ground_ahead(float(p.x) + 1.0, float(p.z)) - d, ground_ahead(float(p.x), float(p.z) + 1.0) - d);   // straight ahead, along the ground
    vec2 q = vec2(p.xz) - g * (d - haze_far) / dot(g, g);
    int x = clamp(int(round(q.x)), 0, 65536), z = clamp(int(round(q.y)), 0, 65536);
    return ivec3(x, cut_height(map, x, z), z);
}

// SUN SHADING on ground point p (SunShade.GroundK): its slope from the heights one square either side, the world's
// edge held at the edge.
float ground_point_sun(ivec3 p) {
    int xl = max(p.x - 512, 0), xr = min(p.x + 512, 65536), zb = max(p.z - 512, 0), zf = min(p.z + 512, 65536);
    return ground_point_k(cut_height(map, xl, p.z), cut_height(map, xr, p.z), cut_height(map, p.x, zb), cut_height(map, p.x, zf));
}
varying float depth_;                             // HAZE: how far ahead of the eye (POSITION.w)
varying vec2 pat_at;                              // GROUND PATTERN (SPECKS): the vertex's world x and z in squares of 512
varying vec2 sea_at;                              // SEA: LIVING - the water's world x and z, as the sea plane's (world.gdshader)

void vertex() {
    int n = max(cuts, 1);                             // pieces per triangle side
    int id = VERTEX_ID;
    int cell = id / (6 * n * n);
    int rest = id - cell * 6 * n * n;
    int s = rest / (3 * n * n);                       // the slot's two triangles (ground_cell_triangle)
    rest -= s * 3 * n * n;
    int k = rest % 3;                                 // the corner of the small triangle
    int piece = rest / 3;                             // the small triangle, row by row: ups then downs
    int col = win_origin.x + cell % win_size.x, row = win_origin.y + cell / win_size.x;
    bool offwin = cell >= win_size.x * win_size.y || col > 127 || row > 127
               || (!win_all && texelFetch(win_tex, ivec2(col, row), 0).r <= 0.0);   // past the rectangle, or outside the 3D view window: nothing at all
    ivec3 t = ground_cell_triangle(types, col, row, s);
    bool water = false;
    if (!offwin && t.x < 0) { t = ground_water_triangle(types, col, row, s); water = t.x >= 0; }
    ivec3 p = ivec3(0);
    int colour = 0;
    ground_light = 1.0;
    banded_ = 0.0;
    band_at = vec3(0.0);
    if (offwin || t.x < 0 || (water && cuts > 1 && piece != 0)) {
        POSITION = vec4(2.0, 2.0, 2.0, 1.0);          // nothing drawn: outside the window, an empty slot, or flat water past its one piece
        tag = 0.0;
    } else if (water) {
        // ENHANCEMENT (the 3D view's coast, owner 2026-10-27): the water DrawView never draws - a type-0 cell whole,
        // a coast shape's other half - flat at the sea's level (Terrain's clamp, always 0) in the sea's own tag 16
        // (world_colour idx 16 = sky_nib(6), the sea plane's Real3DView force_idx), no sun on it and no cutting:
        // piece 0 draws the whole slot however many cuts.
        ivec3 c = ground_corner(map, col, row, k == 0 ? t.x : (k == 1 ? t.y : t.z));
        p = ivec3(c.x, 0, c.z);
        sea_at = vec2(float(p.x), -float(p.z));
        colour = 16;
        tag = float(colour);
        float kk = cam_interior ? 1.5 : 1.0;
        POSITION = PROJECTION_MATRIX * vec4((MODELVIEW_MATRIX * vec4(view_rel(vec3(float(p.x), float(p.y), -float(p.z))), 1.0)).xyz * kk, 1.0);
        if (cam_interior) POSITION.z = POSITION.w * 0.000001;   // indoors at the far depth (reversed z): x 1.5 alone let ground near the eye in front of the shell
    } else if (cuts == 1) {
        p = ground_far_edge(ground_corner(map, col, row, k == 0 ? t.x : (k == 1 ? t.y : t.z)));
        colour = ground_cell_colour(types, col, row);
        if (sun_shading && sun_up && !cam_interior) {
            // the sun on this corner (rules/SunShade.cs GroundK, GroundLevel), on the original's own colour
            ground_light = ground_sun_level(ground_point_sun(p));
        }
        banded_ = !cam_interior && ground_banded(colour) ? 1.0 : 0.0;   // GROUND COLOURS: BANDS - the whole triangle is the piece
        if (banded_ > 0.5) { band_at = vec3(p); band_check_ = ground_band_checker(col, row); }
        tag = float(colour);
        float kk = cam_interior ? 1.5 : 1.0;                   // indoors the ground is the furthest backdrop: the room's shell paints over it ($48F4 then $48F8)
        POSITION = PROJECTION_MATRIX * vec4((MODELVIEW_MATRIX * vec4(view_rel(vec3(float(p.x), float(p.y), -float(p.z))), 1.0)).xyz * kk, 1.0);
        if (cam_interior) POSITION.z = POSITION.w * 0.000001;   // indoors at the far depth (reversed z): x 1.5 alone let ground near the eye in front of the shell
    } else {
        // the small triangle `piece` of the slot: the slot's P corners as lattice points of the cell, cut n x n
        ivec2 pa = ground_corner(map, col, row, t.x).xz, pb = ground_corner(map, col, row, t.y).xz, pc = ground_corner(map, col, row, t.z).xz;
        ivec2 d10 = (pb - pa) / n, d01 = (pc - pa) / n;        // one lattice step (512 whole world units a side / n)
        int i = 0, j = 0;                                      // the piece's lattice cell, row by row: the row's ups then its downs
        bool down = false;
        int rem = piece;
        for (int r = 0; r < n; r++) {
            if (rem < n - r) { i = r; j = rem; break; }
            rem -= n - r;
            if (rem < n - 1 - r) { i = r; j = rem; down = true; break; }
            rem -= n - 1 - r;
        }
        ivec2 a0, a1, a2;                                      // the piece's corners, the slot's winding kept
        if (!down) { a0 = pa + i * d10 + j * d01; a1 = pa + (i + 1) * d10 + j * d01; a2 = pa + i * d10 + (j + 1) * d01; }
        else       { a0 = pa + (i + 1) * d10 + j * d01; a1 = pa + (i + 1) * d10 + (j + 1) * d01; a2 = pa + i * d10 + (j + 1) * d01; }
        ivec3 v0 = ivec3(a0.x, cut_height(map, a0.x, a0.y), a0.y);
        ivec3 v1 = ivec3(a1.x, cut_height(map, a1.x, a1.y), a1.y);
        ivec3 v2 = ivec3(a2.x, cut_height(map, a2.x, a2.y), a2.y);
        p = ground_far_edge(k == 0 ? v0 : (k == 1 ? v1 : v2));
        colour = ground_cell_colour(types, col, row);
        if (sun_shading && sun_up && !cam_interior) {
            // the sun on this lattice corner, as on the square's own corners
            ground_light = ground_sun_level(ground_point_sun(p));
        }
        banded_ = !cam_interior && ground_banded(colour) ? 1.0 : 0.0;
        if (banded_ > 0.5) { band_at = vec3(p); band_check_ = ground_band_checker(col, row); }
        tag = float(colour);
        float kk = cam_interior ? 1.5 : 1.0;
        POSITION = PROJECTION_MATRIX * vec4((MODELVIEW_MATRIX * vec4(view_rel(vec3(float(p.x), float(p.y), -float(p.z))), 1.0)).xyz * kk, 1.0);
        if (cam_interior) POSITION.z = POSITION.w * 0.000001;   // indoors at the far depth (reversed z): x 1.5 alone let ground near the eye in front of the shell
    }
    depth_ = POSITION.w;
    pat_at = vec2(float(p.x), float(p.z)) / 512.0;
}

void fragment() {
    // the coast water with SEA: LIVING coloured exactly as the sea plane around it, so the two meet unseen
    int ti = int(round(tag));
    if (banded_ > 0.5)                                                         // GROUND COLOURS: BANDS
        ALBEDO = ground_band_albedo(ground_point_word(map, types, ivec2(round(band_at.xz)), int(round(band_at.y))), cam_interior ? 0.0 : depth_, ground_light * band_check_);
    else if (ground_pattern && ti < 16)                                        // GROUND PATTERN: SPECKS (not the sea, tag 16; none indoors)
        ALBEDO = world_speck_hazed(ti, FRAGCOORD, cam_interior ? 0.0 : depth_, ground_light, cam_interior ? 1.0 : ground_speck(pat_at));
    else
    ALBEDO = int(round(tag)) == 16 && sea_living ? sea_albedo(FRAGCOORD, sea_at, cam_interior ? 0.0 : depth_)
                 : ground_light != 1.0 ? world_lit_hazed(int(round(tag)), FRAGCOORD, cam_interior ? 0.0 : depth_, ground_light)
                                       : world_hazed(int(round(tag)), FRAGCOORD, cam_interior ? 0.0 : depth_);
}
