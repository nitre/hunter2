// MODEL COLOURS (GpuWorld.OwnTints): per model one tint (own_tex x = model; a = 1 a machine, 0.5 a person, 0 none), the
// greens take it, each keeping its own brightness - a machine's moving parts excepted. The models and their explosion
// pieces (gpumodels.gdshader, gpupieces.gdshader). Needs world_colour.gdshaderinc (world_rgb8).
uniform bool own_colours = false;
uniform sampler2D own_tex : filter_nearest;

bool own_green(int c) { return c == 1 || c == 2 || c == 3 || c == 4 || c == 12 || c == 13 || c == 14; }

// The tint a face of this model and paint takes, vec3(0) = none (its paint); part = it lies on a moving part.
vec3 own_tint_of(int model, int c, bool part) {
    vec4 tint = own_colours ? texelFetch(own_tex, ivec2(model, 0), 0) : vec4(0.0);
    return tint.a > 0.25 && own_green(c) && (tint.a < 0.75 || !part) ? tint.rgb : vec3(0.0);
}

// The palette paint (faded as the palette is) with its brightness kept and its hue the tint's, times the sun's light.
vec3 tinted_rgb8(int idx, vec4 fragcoord, vec3 tint, float light) {
    float luma = dot(world_rgb8(idx, fragcoord), vec3(0.299, 0.587, 0.114));
    return min(luma * tint * light, vec3(255.0));
}
