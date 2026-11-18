shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
stencil_mode read, write, compare_not_equal, 1;

// ENHANCEMENT (renderer = 3d): a thing's flat ground shadow ($52A2: every corner at the ground
// height), computed on the card like gpumodels.gdshader. Each corner lies on the ground under it ($36AA there),
// so a shadow thrown away from the sun (SHADOWS: MORE) follows the slopes it falls across.
// (layer = 1 is set on this shader's material by GpuWorld.)
// The colour, with SHADOWS: MORE off: the original's 13 shadows in their faces' own paint - colour 0, black (Objects.Draw:
// Faces(v, m, OwnShadows), halfBright only with the setting on). With it on, every shadow (those 13 too, as the flat
// picture): the original's half-bright bit (Play.ScreenOf's EHB mark): the colour already drawn under the shadow
// (the screen as the solid pass left it), each channel of its $0RGB word halved, the remainder dropped. An in-between
// colour no 16-level word gives (A1200 colours, the living sea) is halved in its 256 levels. Twice is still once,
// as the bit: every shadow reads the unshadowed picture, and the stencil lets the first one only.
// ENHANCEMENT (row HAZE): the ground under the shadow fades toward the far sea's colour with depth (world_hazed),
// so the shadow fades with it - the half mixes up to no darkening at all at the far edge (haze_amount of the same
// POSITION.w depth the card's colours haze by), in full colour. Haze off, or indoors: the half.
// ENHANCEMENT (owner 2026-11-01: sea shadows lighter, "not nothing", broken up as the sea is): on a sea square (type 0)
// the darkening is SEA_SHADOW of the land's, times the water's own brightness against SEA_BRIGHT - a lit patch
// shows the shadow, a dark one hardly - so the shadow breaks up with the sea's own pattern.

#include "res://real3d/place.gdshaderinc"
#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/gpumodels.gdshaderinc"
#include "res://real3d/view.gdshaderinc"

uniform sampler2D under_tex : hint_screen_texture, filter_nearest;
uniform sampler2D shadow_types : filter_nearest;  // 128 x 128: the cell type bytes (0 = sea)

const float SEA_SHADOW = 0.5;                     // a sea shadow's darkening, of the land's
const float SEA_BRIGHT = 0.19;                    // the sea's lit patches, brightness 0..1 (measured in the demo)

varying float depth_;                             // HAZE: how far ahead of the eye (POSITION.w), as gpupoints
varying flat int paint_;                          // the face's own colour (SHADOWS: MORE off)
varying vec2 ground_xz;                           // where on the map the shadow lies, world units

void vertex() {
    vec3 p; int colour; bool pull;
    paint_ = 0;
    ground_xz = vec2(0.0);
    if (gpu_model_vertex(VERTEX_ID, p, colour, pull)) {
        paint_ = colour;
        ground_xz = p.xz;
        if (cam_on && !inside_view) p.y = float(ground_height(cam_map, int(round(p.x)) & 0xFFFF, int(round(p.z)) & 0xFFFF));
        POSITION = PROJECTION_MATRIX * vec4((MODELVIEW_MATRIX * vec4(view_rel(vec3(p.x, p.y, -p.z)), 1.0)).xyz * 0.996, 1.0);
        depth_ = POSITION.w;
    } else {
        POSITION = vec4(2.0, 2.0, 2.0, 1.0);
        depth_ = 0.0;
    }
}

void fragment() {
    if (!own_shadows)
        ALBEDO = world_hazed(paint_, FRAGCOORD, cam_interior ? 0.0 : depth_);
    else {
        vec3 c = floor(to_display(texture(under_tex, SCREEN_UV).rgb) * 255.0 + 0.5);   // 0..255 a channel
        vec3 nib = floor(c / 17.0 + 0.5);
        bool word = all(equal(nib * 17.0, c));                                           // a 16-level colour: its $0RGB word
        vec3 half_ = word ? floor(nib / 2.0) * 17.0 : floor(c / 2.0);
        float f = haze_amount(cam_interior ? 0.0 : depth_);
        ivec2 cell = (ivec2(floor(ground_xz)) & ivec2(0xFFFF)) / 512;
        if (!cam_interior && int(texelFetch(shadow_types, cell, 0).r + 0.5) == 0) {
            float lit = clamp(dot(c / 255.0, vec3(0.299, 0.587, 0.114)) / SEA_BRIGHT, 0.0, 1.0);
            f = 1.0 - (1.0 - f) * SEA_SHADOW * lit;
        }
        vec3 m = mix(half_, c, f);
        if (f > 0.0) m = floor(m + 0.5);                                                 // full colour, as world_hazed8
        ALBEDO = to_linear(m / 255.0);
    }
}
