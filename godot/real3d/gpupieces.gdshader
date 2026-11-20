shader_type spatial;
render_mode unshaded, cull_disabled;

// ENHANCEMENT (DEBRIS: PIECES, the card-drawn 3D view): every explosion's own pieces, worked out here each frame
// (pieces.gdshaderinc) - one mesh of BLASTS x tri_slots x 3 empty vertices; the game's original ten triangles
// (gpudebris) are not drawn meanwhile. The sun lights a piece from either side (a thin shard).

#include "res://real3d/place.gdshaderinc"
#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/sunshade.gdshaderinc"
#include "res://real3d/own_colours.gdshaderinc"
#include "res://real3d/gpumodels.gdshaderinc"
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/ground_cell.gdshaderinc"
#include "res://real3d/pieces.gdshaderinc"

uniform sampler2D blast_tex : filter_nearest;     // the blown-up things' records as they were (0xA0 x BLASTS)
uniform sampler2D blast_info : filter_nearest;    // BLASTS x 2: row 0 r = the frame it blew up, g = 1 in use; row 1 where the hit came from (GpuWorld.blastInfo)
uniform sampler2D types : filter_nearest;         // the map's cell types (0 = sea)
uniform float piece_clock = 0.0;                  // frames now
uniform float piece_life = 600.0;                 // a cap only: each piece goes itself, at most piece_rest frames after it lies still
uniform bool pieces_on = false;                   // EXPLOSIONS: PIECES

varying flat float tag;
varying float depth_;                             // HAZE: how far ahead of the eye (POSITION.w)
varying flat float own_light;                     // past the ramp's lightest paint (SunShade.Over), else 1; tinted: the sun's light
varying flat vec3 own_tint;                       // MODEL COLOURS: the blown-up model's tint, as its faces had it; 0 = none

void vertex() {
    PROJECTION_MATRIX = wide_projection(PROJECTION_MATRIX);   // the TV looks' wide game pixels (world_colour.gdshaderinc)
    int per = tri_slots * 3;
    int row = INSTANCE_ID;                                     // one blast's corners drawn once a blast (GpuWorld.SlotRows)
    int rem = VERTEX_ID;
    int slot = rem / 3;
    int k = rem - slot * 3;
    vec4 info = texelFetch(blast_info, ivec2(row, 0), 0);
    float t = piece_clock - info.r;
    vec3 p; int colour; vec3 n; bool resting; float t_break, t_rest;
    if (!pieces_on || info.g < 0.5 || t < 0.0 || t > piece_life || inside_view || !piece_corner(blast_tex, cam_map, types, row, texelFetch(blast_info, ivec2(row, 1), 0), slot, k, t, p, colour, n, resting, t_break, t_rest)) {
        tag = 0.0;
        own_light = 1.0;
        own_tint = vec3(0.0);
        POSITION = vec4(2.0, 2.0, 2.0, 1.0);
    } else {
        bool lit = sun_shading && sun_up;
        float k = abs(dot(n, sun_dir));
        int model = _eb(blast_tex, row, 0x25);
        int meta = int(texelFetch(tri_tex, tri_texel(int(texelFetch(model_tex, ivec2(model, 0), 0).g + 0.5) + slot, 0), 0).a + 0.5);
        own_tint = own_tint_of(model, colour, (meta & 0x200) != 0);       // as gpumodels.gdshader: a machine's parts keep their paint
        if (own_tint != vec3(0.0)) {
            tag = float(colour);
            own_light = lit ? float(sun_light64(k)) / 64.0 : 1.0;
        } else {
            tag = float(lit ? sun_pick(colour, k) : colour);
            own_light = lit ? sun_over(colour, k) : 1.0;
        }
        POSITION = PROJECTION_MATRIX * vec4((MODELVIEW_MATRIX * vec4(view_rel(vec3(p.x, p.y, -p.z)), 1.0)).xyz, 1.0);
    }
    depth_ = POSITION.w;
}

void fragment() {
    float d = cam_interior ? 0.0 : depth_;
    if (own_tint != vec3(0.0))
        ALBEDO = to_linear(floor(mix(tinted_rgb8(int(round(tag)), FRAGCOORD, own_tint, own_light), sky_nib(5) * 17.0, haze_amount(d)) + 0.5) / 255.0);   // lit and hazed as world_lit_hazed
    else ALBEDO = own_light != 1.0 ? world_lit_hazed(int(round(tag)), FRAGCOORD, d, own_light) : world_hazed(int(round(tag)), FRAGCOORD, d);
}
