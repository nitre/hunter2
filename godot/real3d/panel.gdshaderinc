// The panel's number fields as a shader function (rules/Panel.cs: Position $C4E6, Clock $C43A, Hour $C45E, Minute $C480,
// Score $9A2C, RoundsShownSyncStock $A3A0; Number $C542 / $C546, the digit walk $C55C-$C5AE, the glyph draw $C60E): for any pixel
// of the 320 x 60 panel, the bit the game leaves in plane 0 there - or -1 where none of these fields lies, so the
// panel's own picture shows.
// The game redraws a field only when its value changes, so the buffer always shows the latest values: the card works
// them out from the values alone.
// The font is a texture, 8 x 256 Rf: texel (r, c) = row r of the panel font's glyph c ($837A).
// The day's suffix ("st", "nd", "rd", "th": two characters from the game's table $C4BE) comes in as its two characters.
// The two bars (panel_plane2): the energy bar (AddEnergy's $A43A-$A462) and the fuel gauge (FuelGauge $A3C6), plane 2.
// The radar (panel_radar, panel_grave_dot), the lamps (panel_lamp) and the icon follow; the message window's text comes in.

struct PanelNums {
    int pos_x;      // the player's +$4A, unsigned word
    int pos_z;      // +$4E
    int day;        // the day counter, from 0
    int hour;
    int minute;
    int clock_on;   // $A7 bit 6
    int score;      // $21A
    int rounds;     // the player's +$32, signed word
    int suffix0;
    int suffix1;
};

// o[k] of Digits ($C55C): the five ASCII digits of d2 by divu.w 10000, 1000, 100, 10 (a quotient over $FFFF leaves the
// number as it was, as divu does).
int panel_digit(int d2, int k) {
    uint v = d2 < 0 ? uint(-d2) : uint(d2);
    int o = 0;
    for (int i = 0; i < 5; i++) {
        uint div = i == 0 ? 10000u : (i == 1 ? 1000u : (i == 2 ? 100u : 10u));
        if (i < 4 && v / div <= 0xFFFFu) v = ((v % div) * 65536u) | (v / div);
        o = int((v + 0x30u) & 255u);
        if (i == k) return o;
        v = (v & 0xFFFF0000u) / 65536u;
    }
    return o;
}

int _pn_short(int v) { v &= 0xFFFF; return v >= 0x8000 ? v - 0x10000 : v; }

// One glyph cell: a text of characters starting at byte column col, pixel row top; the bit at (x, y) or the old result.
int _pn_cell(sampler2D font, int old, int x, int y, int col, int top, int c, int k) {
    if (y < top || y >= top + 8 || (x >> 3) != col + k) return old;
    return (int(texelFetch(font, ivec2(y - top, c), 0).r + 0.5) >> (7 - (x & 7))) & 1;
}

// The plane 0 bit at panel pixel (x, y), -1 where no field is. Fields in the order the game draws them: the set-up's
// (score, day, its suffix, hour, minutes), then each pass's (rounds, x, z); a later one wins a cell.
int panel_bit(sampler2D font, PanelNums n, int x, int y) {
    int bit = -1;
    int col = x >> 3;
    if (col >= 0x1B && col <= 0x20 && y >= 0x1D && y < 0x25) {                                   // score: the sign, then five digits
        int k = col - 0x1B;
        bit = _pn_cell(font, bit, x, y, 0x1B, 0x1D, k == 0 ? (n.score < 0 ? 45 : 43) : panel_digit(n.score, k - 1), k);
    }
    if (col >= 9 && col <= 10 && y >= 0x13 && y < 0x1B)                                         // the day: the last two digits
        bit = _pn_cell(font, bit, x, y, 9, 0x13, panel_digit(_pn_short(n.clock_on != 0 ? n.day + 1 : 0), col - 9 + 3), col - 9);
    if (col >= 0xB && col <= 0xC && y >= 0x13 && y < 0x1B)                                      // its suffix
        bit = _pn_cell(font, bit, x, y, 0xB, 0x13, col == 0xB ? n.suffix0 : n.suffix1, col - 0xB);
    if (col >= 9 && col <= 10 && y >= 8 && y < 16)                                              // the hour
        bit = _pn_cell(font, bit, x, y, 9, 8, panel_digit(_pn_short(n.clock_on != 0 ? n.hour : 0), col - 9 + 3), col - 9);
    if (col >= 0xC && col <= 0xD && y >= 8 && y < 16)                                           // the minutes
        bit = _pn_cell(font, bit, x, y, 0xC, 8, panel_digit(_pn_short(n.clock_on != 0 ? n.minute : 0), col - 0xC + 3), col - 0xC);
    if (col >= 0x1B && col <= 0x1C && y >= 4 && y < 12)                                         // the weapon's rounds: the last two digits
        bit = _pn_cell(font, bit, x, y, 0x1B, 4, panel_digit(n.rounds, col - 0x1B + 3), col - 0x1B);
    if (col >= 4 && col <= 6 && y >= 8 && y < 16)                                               // x, the top byte of the word
        bit = _pn_cell(font, bit, x, y, 4, 8, panel_digit(_pn_short(n.pos_x >> 8), col - 4 + 2), col - 4);
    if (col >= 4 && col <= 6 && y >= 0x18 && y < 0x20)                                          // z
        bit = _pn_cell(font, bit, x, y, 4, 0x18, panel_digit(_pn_short(n.pos_z >> 8), col - 4 + 2), col - 4);
    return bit;
}

// The weapon's icon ($A96C WeaponIcon, $A99C DrawIcon): icon n of the sheet storepic (ten a row, 32 x 16, a Rf texture
// 320 wide) at x 240, row 1; all four planes written. The colour index at panel pixel (x, y), -1 outside the icon.
int panel_icon(sampler2D sheet, int icon, int x, int y) {
    if (x < 240 || x >= 272 || y < 1 || y >= 17) return -1;
    return int(texelFetch(sheet, ivec2((icon % 10) * 32 + x - 240, (icon / 10) * 16 + y - 1), 0).r + 0.5);
}

// The radar ($A480 Radar, $A4E0 RadarDots, $A510-$A536 RadarDot): the 32 x 32 at x 144, row 4, planes 0-2.
// Off: all three planes clear. On: plane 0 all set but the cross (pixel column 15, row 16; the crossing itself set),
// plane 1 one dot for every live record from slot `players` up whose +$06 bit 3 is set, at bit (px + $1000 - x) >> 8 of
// row (pz + $1000 - z) >> 8 (pixel 31 - that bit; none if either is negative or past 31), plane 2 left as it was (-1).
// ent = the entity records (160 x N or wider, a byte a texel, the first is record row0), px / pz = the panel's player's +$4A / +$4E.
// The plane's bit at panel pixel (x, y), -1 outside the radar. wide (Maths32, rules Panel.RadarDot via WorldState.Dist): the
// differences in full, so a thing more than half the map away is not wrapped onto the radar.
int _pn_eb(sampler2D ent, int r, int off) {                             // width / 160 records a texture row (GpuWorld.PerRow)
    int per = textureSize(ent, 0).x / 160;
    return int(texelFetch(ent, ivec2(off + r % per * 160, r / per), 0).r + 0.5);
}
int panel_radar(sampler2D ent, int row0, int count, int players, int px, int pz, bool on, bool wide, int plane, int x, int y) {
    if (x < 144 || x >= 176 || y < 4 || y >= 36) return -1;
    int cx = x - 144, cy = y - 4;
    if (!on) return 0;
    if (plane == 2) return -1;
    if (plane == 0) return (cx == 15 && cy == 16) ? 1 : ((cx == 15 || cy == 16) ? 0 : 1);
    int bit = 31 - cx;                                                    // the long's bit for this pixel
    int d5 = wide ? px + 0x1000 : (px + 0x1000) & 0xFFFF, d6 = wide ? pz + 0x1000 : (pz + 0x1000) & 0xFFFF;
    for (int n = players; n < count; n++) {
        int r = row0 + n;
        if (_pn_eb(ent, r, 0) == 0) continue;
        if ((_pn_eb(ent, r, 6) & 8) == 0) continue;
        int ex = _pn_eb(ent, r, 0x4A) * 256 + _pn_eb(ent, r, 0x4B);
        int ez = _pn_eb(ent, r, 0x4E) * 256 + _pn_eb(ent, r, 0x4F);
        int dx = wide ? d5 - ex : _pn_short(d5 - ex);
        if (dx < 0) continue;
        int dz = wide ? d6 - ez : _pn_short(d6 - ez);
        if (dz < 0) continue;
        dx >>= 8; dz >>= 8;
        if (dx > 31 || dz > 31) continue;
        if (dx == bit && dz == cy) return 1;
    }
    return 0;
}

// ENHANCEMENT (Play.Graves, rules Panel.RadarDots): a plane 1 dot for each grave not taken, the same dot test as a live
// thing. graves = (x, z, taken) a texel, count of them. True where a grave's dot lies at panel pixel (x, y) of the radar.
bool panel_grave_dot(sampler2D graves, int count, int px, int pz, bool wide, int x, int y) {
    if (x < 144 || x >= 176 || y < 4 || y >= 36) return false;
    int bit = 31 - (x - 144), cy = y - 4;
    int d5 = wide ? px + 0x1000 : (px + 0x1000) & 0xFFFF, d6 = wide ? pz + 0x1000 : (pz + 0x1000) & 0xFFFF;
    for (int n = 0; n < count; n++) {
        vec3 g = texelFetch(graves, ivec2(n, 0), 0).rgb;
        if (g.b > 0.5) continue;
        int dx = wide ? d5 - int(g.r + 0.5) : _pn_short(d5 - int(g.r + 0.5));
        if (dx < 0) continue;
        int dz = wide ? d6 - int(g.g + 0.5) : _pn_short(d6 - int(g.g + 0.5));
        if (dz < 0) continue;
        if (dx >> 8 == bit && dz >> 8 == cy) return true;
    }
    return false;
}

// The seven lamps ($6CFA Flash, $6D1E PanelLamp; the table at $6CC2: kind and the plane 3 byte offset). The game toggles a
// lamp's state byte (0 / $FF) a step at a time and draws it in plane 3: a kind 0 lamp is one byte (8 pixels) set to the state;
// a kind 1 lamp sets pixels 4-5 of its byte over 14 rows when the state is $FF - and does NOT clear them when it is 0, so
// once lit it stays lit. The buffer remembers, so three masks (bit n = lamp n) come in: state, drawn (the lamp has been
// stepped at least once) and lit (it has been $FF at least once). The plane 3 bit at panel pixel (x, y), -1 where the
// panel's own picture shows.
const int PANEL_LAMP_KIND[7] = int[7](0, 1, 0, 1, 0, 0, 0);
const int PANEL_LAMP_AT[7] = int[7](40, 1720, 548, 411, 1498, 1274, 1464);

int panel_lamp(int state, int drawn, int lit, int x, int y) {
    int bit = -1;
    for (int n = 0; n < 7; n++) {
        int row = PANEL_LAMP_AT[n] / 40, col = PANEL_LAMP_AT[n] - row * 40;
        if ((x >> 3) != col) continue;
        if (PANEL_LAMP_KIND[n] == 0) {
            if (y == row && ((drawn >> n) & 1) != 0) bit = (state >> n) & 1;
        } else if (y >= row && y < row + 14 && (x & 7) >= 4 && (x & 7) <= 5 && ((lit >> n) & 1) != 0) {
            bit = 1;
        }
    }
    return bit;
}

// The plane 2 bit at panel pixel (x, y), -1 where neither bar is.
// Energy ($A43A): from row 34 (x 192) up 32 rows, pixel 192 set for the first energy + 1 rows, pixel 193 cleared.
// Fuel ($A3FE): row 36, 32 pixels from x 272, the first (fuel >> 1) & 63 of them set (a shift of 32 or more sets all).
int panel_plane2(int fuel, int energy, int x, int y) {
    if (x >= 192 && x < 194 && y >= 3 && y <= 34) return (x == 192 && 34 - y <= energy) ? 1 : 0;
    if (x >= 272 && x < 304 && y == 36) return (x - 272) < ((fuel >> 1) & 63) ? 1 : 0;
    return -1;
}

// The whole panel picture ($84AC LoadPanelPictures: panfile rows 0-59 as the base, then everything the game draws over
// it, plane by plane): the colour index 0..15 at panel pixel (x, y). base = the panfile's indices (320 x 60 Rf).
// msg_bit = the message window's plane 0 bit at (x, y) (the processor's Text.Print, sent up by PanelGpu; 0 for an empty window, which the
// set-up clears: rows 41-58, byte columns 1-38). The fuel gauge is only drawn once the fuel byte is not 0.
struct PanelState {
    PanelNums nums;
    int fuel, energy, icon;
    bool radar_on;
    int radar_count, radar_players;     // the records the radar walks (slots players .. count - 1), from the entity table
    bool radar_wide;                    // Maths32: true differences
    int grave_count;                    // the graves texture's entries (0 with GRAVES off)
    int lamp_state, lamp_drawn, lamp_lit;
};

int panel_index(sampler2D base, sampler2D font, sampler2D sheet, sampler2D ent, int row0, sampler2D graves, PanelState s, int msg_bit, int x, int y) {
    int idx = int(texelFetch(base, ivec2(x, y), 0).r + 0.5);
    int v = panel_icon(sheet, s.icon, x, y);
    if (v >= 0) idx = v;
    if (y >= 41 && y < 59 && x >= 8 && x < 312) idx = (idx & ~1) | msg_bit;
    int b = panel_bit(font, s.nums, x, y);
    if (b >= 0) idx = (idx & ~1) | b;
    for (int pl = 0; pl < 3; pl++) {
        b = panel_radar(ent, row0, s.radar_count, s.radar_players, s.nums.pos_x, s.nums.pos_z, s.radar_on, s.radar_wide, pl, x, y);
        if (b >= 0) idx = (idx & ~(1 << pl)) | (b << pl);
    }
    if (s.radar_on && panel_grave_dot(graves, s.grave_count, s.nums.pos_x, s.nums.pos_z, s.radar_wide, x, y)) idx |= 2;
    if (s.fuel != 0 || y != 36) {
        b = panel_plane2(s.fuel, s.energy, x, y);
        if (b >= 0) idx = (idx & ~4) | (b << 2);
    }
    b = panel_lamp(s.lamp_state, s.lamp_drawn, s.lamp_lit, x, y);
    if (b >= 0) idx = (idx & ~8) | (b << 3);
    return idx;
}
