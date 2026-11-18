shader_type canvas_item;
render_mode blend_disabled;

// ENHANCEMENT (renderer = 3d): the panel's rows 140-199 drawn by the card (godot/real3d/PanelGpu.cs) from
// the game's values - panel.gdshaderinc's panel_index over the panfile picture - in the panel's colours as the game's copper
// sets them (the panfile's colour table faded to 15, colour 0 the view's), the pointer over it. Only the message window's
// text (the game's own printer, still on the processor) comes in as a plane 0 texture.

#include "res://real3d/fade.gdshaderinc"
#include "res://real3d/panel.gdshaderinc"

uniform sampler2D base : filter_nearest;      // the panfile's indices, 320 x 60 Rf
uniform sampler2D font : filter_nearest;      // 8 x 256 Rf, the panel font
uniform sampler2D sheet : filter_nearest;     // the icon sheet's indices (storepic), 320 wide
uniform sampler2D ent : filter_nearest;       // the entity records
uniform sampler2D msg : filter_nearest;       // the message window's plane 0 bits, 320 x 60 Rf
uniform sampler2D pointer_tex : filter_nearest;   // the pointer sprite, 16 x 15 Rf (0 = clear)
uniform sampler2D graves_tex : filter_nearest;    // the graves (x, z, taken) a texel, grave_count of them
uniform int grave_count = 0;

uniform int day;
uniform int hour;
uniform int minute;
uniform int clock_on;
uniform int score;
uniform int rounds;
uniform int suffix0;
uniform int suffix1;
uniform int pos_x;
uniform int pos_z;
uniform int fuel;
uniform int energy;
uniform int icon;
uniform int radar_count;
uniform int radar_players;
uniform int lamp_state;
uniform int lamp_drawn;
uniform int lamp_lit;
uniform bool radar_on;
uniform bool radar_wide;                       // Maths32 (rules WorldState.Maths32): the radar's differences in full
uniform int pan_raw[16];           // the panfile's colour table as $0RGB words
uniform int view0_raw;             // the view's colour 0 (bank 2): the panel's colour 0 in play
uniform int fade_level;            // the view's fade level (ScreenFade)
uniform int panel_level = 15;      // the panel's own colours: below 15 only while a play start / stop fade walks them (Play.PanelLevel)
uniform int smooth_level = -1;     // SmoothLight's level in 60ths, -1 = the original's
uniform int death_step;
uniform ivec2 pointer_at = ivec2(-100, -100);   // screen x, y of the pointer (the panel's row within it: y - 140)
uniform int pointer_colour;
uniform bool pointer_on = true;

const int LIP = 6;                 // rows above the panel (134-139): the pointer's tip pokes up over the view (PanelGpu.Lip)

void fragment() {
    ivec2 at = clamp(ivec2(UV * vec2(320.0, float(60 + LIP))), ivec2(0), ivec2(319, 59 + LIP));
    ivec2 p = ivec2(at.x, at.y - LIP);                                          // the panel's own row; negative: the lip
    bool lip = p.y < 0;
    int c = 0;
    if (!lip) {
    PanelState s;
    s.nums.pos_x = pos_x; s.nums.pos_z = pos_z; s.nums.day = day; s.nums.hour = hour; s.nums.minute = minute;
    s.nums.clock_on = clock_on; s.nums.score = score; s.nums.rounds = rounds; s.nums.suffix0 = suffix0; s.nums.suffix1 = suffix1;
    s.fuel = fuel; s.energy = energy; s.icon = icon; s.radar_on = radar_on;
    s.radar_count = radar_count; s.radar_players = radar_players; s.radar_wide = radar_wide; s.grave_count = grave_count;
    s.lamp_state = lamp_state; s.lamp_drawn = lamp_drawn; s.lamp_lit = lamp_lit;
    int msg_bit = int(texelFetch(msg, p, 0).r + 0.5);
    int idx = panel_index(base, font, sheet, ent, 0, graves_tex, s, msg_bit, p.x, p.y);
    if (idx == 0) c = smooth_level >= 0 ? faded60(view0_raw, smooth_level) : faded(view0_raw, fade_level);
    else c = faded(pan_raw[idx], panel_level);
    if (death_step > 0) c = death_faded(c, death_step);
    }
    bool drawn = false;
    if (pointer_on) {
        ivec2 q = p + ivec2(0, 140) - pointer_at + ivec2(7, 6);                 // Pointer.Draw: the sprite's top left is (x - 7, y - 6)
        if (q.x >= 0 && q.x < 16 && q.y >= 0 && q.y < 15 && texelFetch(pointer_tex, q, 0).r > 0.5) { c = pointer_colour; drawn = true; }
    }
    if (lip && !drawn) discard;                                                 // the lip shows the view, but for the pointer
    COLOR =vec4(float(c >> 8 & 15) * 17.0 / 255.0, float(c >> 4 & 15) * 17.0 / 255.0, float(c & 15) * 17.0 / 255.0, 1.0);
}
