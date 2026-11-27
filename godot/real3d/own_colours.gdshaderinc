// MODEL COLOURS (GpuWorld.OwnTints): per model one tint (own_tex x = model; a = 1 a machine, 0.5 a person, 0 none), the
// greens take it, each keeping its own brightness - a machine's moving parts excepted. The models and their explosion
// pieces (gpumodels.gdshader, gpupieces.gdshader). Needs world_colour.gdshaderinc (world_rgb8).
uniform bool own_colours = false;
uniform sampler2D own_tex : filter_nearest;

bool own_green(int c) { return c == 1 || c == 2 || c == 3 || c == 4 || c == 12 || c == 13 || c == 14; }

// The tint a face of this model and paint takes, vec3(0) = none (its paint); part = it lies on a moving part.
// THE WORM'S SIGN (GpuWorld.WormSignModel): its swell and dust are the sand's own colour, whatever MODEL COLOURS says - its grey
// faces (8) take sand_tint, the sand's middle key ($C95, ground_bands SAND_KEYS) over grey's brightness, then the sun and haze.
uniform int sand_model = -1;    // the sign
uniform int sand_model2 = -1;   // and the sand funnel where it strikes
uniform vec3 sand_tint = vec3(1.0);
// THE SPICE FLOWER (GpuWorld.SpiceModel, rules ExtraKinds): the red
// flower's copy, its petals (every paint but the greens) a deep blue over their brightness - the red's low brightness makes it
// near black, a blue-black; the stem keeps its own green.
uniform int spice_model = -1;
const vec3 SPICE_TINT = vec3(0.30, 0.34, 1.25);

vec3 own_tint_of(int model, int c, bool part) {
    if (model == sand_model || model == sand_model2) return sand_tint;
    if (model == spice_model) return own_green(c) ? vec3(0.0) : SPICE_TINT;
    vec4 tint = own_colours ? texelFetch(own_tex, ivec2(model, 0), 0) : vec4(0.0);
    return tint.a > 0.25 && own_green(c) && (tint.a < 0.75 || !part) ? tint.rgb : vec3(0.0);
}

// The palette paint (faded as the palette is) with its brightness kept and its hue the tint's, times the sun's light.
vec3 tinted_rgb8(int idx, vec4 fragcoord, vec3 tint, float light) {
    float luma = dot(world_rgb8(idx, fragcoord), vec3(0.299, 0.587, 0.114));
    return min(luma * tint * light, vec3(255.0));
}
