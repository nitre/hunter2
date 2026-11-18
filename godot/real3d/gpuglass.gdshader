shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;

// GLASS: SEE-THROUGH - the glass faces (meta bit 12) alone, drawn see-through in their own colour over what the opaque
// pass drew. Same body as the opaque pass (gpumodels_body.gdshaderinc); GLASS_PASS turns on the hide rule and ALPHA.
#define GLASS_PASS
#include "res://real3d/gpumodels_body.gdshaderinc"
