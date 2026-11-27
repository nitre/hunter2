shader_type spatial;
render_mode unshaded, cull_disabled;

// STUNTIN' (rules/ScrRoad.cs, godot/real3d/RoadMesh.cs): a Stunt Car Racer road and its side walls, a mesh built once on
// the CPU in world units (x, height, -z). Moved to the eye as the card's own ground is (view.gdshaderinc view_rel). Its
// colours are the original's own $0RGB words (new to Hunter's palette), as they are in full light (see fragment), death-faded and hazed as the
// palette's (ground_bands.gdshaderinc's way). UV.x = the colour word, UV.y = the light (1 = the colour as it is).

#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/view.gdshaderinc"

varying flat float word_;
varying float light_;
varying float depth_;

void vertex() {
    word_ = UV.x;
    light_ = UV.y;
    POSITION = PROJECTION_MATRIX * vec4((MODELVIEW_MATRIX * vec4(view_rel(VERTEX), 1.0)).xyz, 1.0);
    depth_ = POSITION.w;
}

void fragment() {
    // the original's colours exactly: Stunt Car Racer has no hour, so the road takes the screen's fade less the hour's (as
    // fire, view_nib_fire - a fade to black still takes it), and at full light its own words unchanged (Hunter's full
    // level 15 would leave 15/16 of each nibble: $200 to $100)
    int lv = min(15, fade_level + 15 - day_level);
    int c = faded(int(round(word_)), lv >= 15 ? 16 : lv);
    vec3 rgb = nibbles(death_step > 0 ? death_faded(c, death_step) : c) * 17.0;
    vec3 lit = min(rgb * light_, vec3(255.0));
    ALBEDO = to_linear(floor(mix(lit, sky_nib(5) * 17.0, haze_amount(depth_)) + 0.5) / 255.0);
}
