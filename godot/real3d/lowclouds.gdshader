shader_type spatial;
render_mode unshaded, cull_disabled;

// (setting clouds, CLOUDS, the card-drawn 3D view): the LOW cloud layer - fair-weather
// cumulus (docs/extras.md): LOW_CLOUD_N heaps at fixed places over the map, all
// with their flat base at low_cloud_y, so near ones slide past fast and far ones slow as you move (parallax). Each is a
// flat card standing upright and turned to face the eye, its shape drawn here (clouds.gdshaderinc low_cloud_*).
// The mesh is LOW_CLOUD_N x 6 empty corners (GpuWorld.Empty); each six make one cloud's card. Each heap is shaded as a
// rounded lump lit from the sun's side (clouds.gdshaderinc low_cloud_level).

#include "res://real3d/world_colour.gdshaderinc"
#include "res://real3d/view.gdshaderinc"
#include "res://real3d/clouds.gdshaderinc"

varying vec2 uv;                     // across the card 0..1, up from the base 0..1
varying flat int cid;                // the cloud
varying float from_eye;              // the cloud's distance from the eye, world units
varying float depth_;                // HAZE: how far ahead of the eye (POSITION.w)
varying flat float sun_x;            // the sun's side across the card: -1 the left, 1 the right (0 at night: light from above)

uniform bool sun_up = false;         // the sun is up (Objects.SunOffset not null; Real3DView.Frame)
uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);   // toward the sun, unit length, x east / y up / z north (SunShade.Toward)

void vertex() {
    PROJECTION_MATRIX = wide_projection(PROJECTION_MATRIX);   // the TV looks' wide game pixels (world_colour.gdshaderinc)
    int k = VERTEX_ID % 6;
    cid = VERTEX_ID / 6;
    vec2 corner = k == 0 || k == 3 ? vec2(0.0) : k == 1 ? vec2(1.0, 0.0) : k == 5 ? vec2(0.0, 1.0) : vec2(1.0);
    LowCloud c = low_cloud(cid);
    vec3 mid = view_rel(vec3(c.x, low_cloud_y, -c.z));                     // the base's middle, from the eye
    from_eye = length(mid);
    vec3 p = mid + vec3((corner.x - 0.5) * c.width, corner.y * c.box, 0.0);   // upright, square to the view
    uv = corner;
    // the card's right-hand way in the world is the view's (view_from: R(-yaw)), so the sun's share along it is its x after the same turn
    sun_x = sun_up ? view_from(vec3(sun_dir.x, sun_dir.y, -sun_dir.z), 0.0, 0.0, 0.0, cam_free ? cam_free_yaw : cam_yaw, 0.0).x : 0.0;
    POSITION = PROJECTION_MATRIX * vec4(p * vec3(1.0, 0.5, 1.0), 1.0);
    depth_ = POSITION.w;
}

void fragment() {
    LowCloud c = low_cloud(cid);
    int level = low_cloud_level(c, uv, sun_x);                              // -1 sky, 0 base shadow .. 4 bright top
    if (level < 0) discard;
    float keep = clamp((from_eye - low_fade_near) / max(1.0, low_fade_far - low_fade_near), 0.0, 1.0);
    ivec2 g = ivec2(floor(FRAGCOORD.xy / vec2(gpu_px_x, gpu_px)));                           // near the camera: the classic dot dissolve
    if (keep * 16.0 <= float(LOW_CLOUD_DOTS[(g.y & 3) * 4 + (g.x & 3)])) discard;
    ALBEDO = to_linear(low_cloud_rgb8(level, depth_, FRAGCOORD.x) / 255.0);
}
