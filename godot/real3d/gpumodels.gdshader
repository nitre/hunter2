shader_type spatial;
render_mode unshaded, cull_disabled;

// The opaque pass of the world's things; the body is shared with the glass pass (gpuglass.gdshader).
#include "res://real3d/gpumodels_body.gdshaderinc"
