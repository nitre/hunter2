// (the card-drawn 3D view): the original stands every thing LEVEL, its lowest corner on the ground under its CENTRE (+$74), and
// tips it only nose up or down ($42A6). In real 3D a slope leaves the downhill wheels in the air and the uphill ones in
// the ground. Here the contact corners (the model's corners within 3 of its lowest) sit on the real ground: a plane
// y' = y + A * (x - cx) + B * (z - cz) + D through the gaps (ground height under each contact minus its own height),
// least squares, lifted by D so no contact ends below the ground. The picture only: the record, +$7A and every rule
// are the game's. Vehicles, animals and people (type < $30) standing on the ground, outdoors. Everything else that
// stands there (buildings, trees, things) stays level, as the original, but sinks or rises so its lowest contact is on
// the LOWEST ground under its contacts - no side floating over a slope (its shadow showed the gap), the uphill side in
// the ground like a foundation.
// The maths is the processor view's rules/GroundContact.cs (647cfe7, retired with that view in 527cdbc) line for line.
// Needs place.gdshaderinc, ground_height.gdshaderinc and view.gdshaderinc (cam_on, cam_map).

const float CONTACT_MAX_SLOPE = 0.7;        // the steepest tilt (height per world unit): a vehicle does not stand on a wall
const int CONTACT_MAX = 64;                 // contact corners kept (a model's corners within 3 of its lowest)
const int CONTACT_LEVEL_BAND = 16;         // a level thing's bottom corners: within 16 of its lowest - a door 4 below its wall ($97 house: wall -160, door -164), a window sill 40 up stays (-124)
const float CONTACT_LEVEL = 1e6;            // c.y of a level thing's fit (no tilt, so c is free): walls_down applies
const float CONTACT_NO_TURN = -1e6;         // fit.w of a thing that is not turned onto its plane (contact_turn): its heights follow the plane, its sides stay upright
const int CONTACT_TURNS_TO = 0x13;          // the vehicles (types $00-$13): rigid bodies, turned as a whole

// The plane for record row's model (base): fit = (A, B, D, floor), c = the centre (cx, cz) the tilt turns about, in
// world units from the record's own x and z; floor = the contacts' own height before the fit (the height the body
// turns about), or CONTACT_NO_TURN. False = this thing stands as the game has it.
bool contact_fit(int row, int base, out vec4 fit, out vec2 c) {
    fit = vec4(0.0, 0.0, 0.0, CONTACT_NO_TURN); c = vec2(0.0);
    if (!cam_on || inside_view || _eb(ent_tex, row, 0x20) != 0) return false;
    bool level = _ew(ent_tex, row, 4) >= 0x30;                                          // stands level: sinks only
    int x = _euw(ent_tex, row, 0x4A), y = _ew(ent_tex, row, 0x4C), z = _euw(ent_tex, row, 0x4E), ground = _ew(ent_tex, row, 0x74);
    if (y > ground + 2) return false;                                                   // in the air: not standing
    int at = _at(base);
    ivec4 head = _head(at);
    int shift = head.y, count = head.z;
    bool plain = entity_unrotated(ent_tex, row);
    ivec4 artic = entity_artic(ent_tex, entprev_tex, anim_tex, 0, 1, row, base);
    ivec3 ang = ivec3(_eb(ent_tex, row, 0x80), _eb(ent_tex, row, 0x7C), _eb(ent_tex, row, 0x7E));
    // the corners as the picture turns them (FINE ANGLES: the byte and its fraction, entity_corner_rot), not the bytes
    // alone: on a 14 degree hill a third of a pitch step put the fit's tank up to 5 above the one drawn, its tread end in the hill
    vec3 frac = entity_frac(ent_tex, row);
    int low = plain ? model_min_y_plain(at, y) : model_min_y(trig_tex, at, artic, ang, 0, ivec3(0, y, 0));
    int lift = lift_to_ground(ground, low);
    float px[CONTACT_MAX]; float pz[CONTACT_MAX]; float pg[CONTACT_MAX]; float ph[CONTACT_MAX];
    int n = 0;
    // a tilted thing's contacts are its bottom in its OWN frame (moving parts in, the game's tilt out): on a slope the
    // game's pitch tips one end lower, and "lowest once tilted" kept only that end - the bike's front wheel floated
    // a vehicle ($00-$12, truck .. amphibian) stands on its fixed corners and its wheels (parts spun about x: kind 1,
    // axis 0) only - the bicycle's feet on the turning cranks (axis 2, and kind 2 following them) dip 2 below its wheels
    // on each stroke, and as contacts they rolled the bike onto one foot, then the other; parked, it leaned on its low pedal. People and animals keep their feet.
    bool wheeled = _ew(ent_tex, row, 4) <= 0x12;
    int bottom = 32767;
    _Walk w = _Walk(ivec3(0), ivec3(0), ivec3(0), 0);
    bool rides = true;                                                                  // this corner's part: fixed or a wheel
    if (!level && !plain)
        for (int k = 0; k < count; k++) {
            int f = _corner(at, k).w;
            if (f != 0) rides = ((f >> 4) & 7) == 0 || (((f >> 4) & 7) == 1 && ((f >> 2) & 3) == 0);
            int oy = _walk_step(w, trig_tex, at, k, artic).y;
            if (!wheeled || rides) bottom = min(bottom, oy);
        }
    w = _Walk(ivec3(0), ivec3(0), ivec3(0), 0);
    rides = true;
    for (int k = 0; k < count && n < CONTACT_MAX; k++) {
        vec3 v;                                                                         // exact, as the picture turns it (corner_fine)
        if (plain) v = vec3(_corner(at, k).xyz);
        else {
            int f = _corner(at, k).w;
            if (f != 0) rides = ((f >> 4) & 7) == 0 || (((f >> 4) & 7) == 1 && ((f >> 2) & 3) == 0);
            ivec3 own = _walk_step(w, trig_tex, at, k, artic);
            v = corner_fine(own, ang, frac, trig_tex);
            if (!level && (own.y > bottom + 3 || (wheeled && !rides))) continue;
        }
        float h = v.y + float(y + _waterline(at));                                      // as low: the corner's height before the lift (SeaBodies: from the water line)
        if ((level || plain) && h > float(low + (level ? CONTACT_LEVEL_BAND : 3))) continue;
        px[n] = v.x * 0.5; pz[n] = v.z * 0.5;
        pg[n] = float(ground_height(cam_map, int(floor(float(x) + px[n])) & 0xFFFF, int(floor(float(z) + pz[n])) & 0xFFFF)) - (h + float(lift));
        ph[n] = h;
        n++;
    }
    if (n == 0) return false;
    if (level) {
        // a door one step below its walls (the original's buildings, 149-171: two corners one model unit under four or
        // more): the building stands on its walls, the door's foot one step into the ground - no gap under the walls
        int y0 = 32767, y1 = 32767, c0 = 0, c1 = 0;
        for (int k = 0; k < count; k++) {
            int ry = _corner(at, k).y >> shift;                                    // the corner's height in model units (the byte)
            if (ry < y0) { y1 = y0; c1 = c0; y0 = ry; c0 = 1; }
            else if (ry == y0) c0++;
            else if (ry < y1) { y1 = ry; c1 = 1; }
            else if (ry == y1) c1++;
        }
        int step = (c0 <= 2 && c1 >= 4 && y1 - y0 == 1) ? (1 << shift) : 0;
        float d = -1e30;                                                                // the highest ground under a contact: nothing buried
        for (int i = 0; i < n; i++) d = max(d, ph[i] >= float(low + step) ? pg[i] : pg[i] - float(step));   // the door's foot at most one step in
        fit = vec4(0.0, 0.0, d, CONTACT_NO_TURN);
        c = vec2(float(_wrap16(low + step + lift)) + d, CONTACT_LEVEL);                 // the bottom's height once placed; walls_down takes it on
        return true;
    }
    float cx = 0.0, cz = 0.0, cg = 0.0;
    for (int i = 0; i < n; i++) { cx += px[i]; cz += pz[i]; cg += pg[i]; }
    cx /= float(n); cz /= float(n); cg /= float(n);
    float sxx = 0.0, szz = 0.0, sxz = 0.0, sxg = 0.0, szg = 0.0;
    for (int i = 0; i < n; i++) {
        float dx = px[i] - cx, dz = pz[i] - cz, dg = pg[i] - cg;
        sxx += dx * dx; szz += dz * dz; sxz += dx * dz; sxg += dx * dg; szg += dz * dg;
    }
    float a = 0.0, b = 0.0;
    float det = sxx * szz - sxz * sxz;
    if (det > 1e-6 * max(1.0, sxx * szz)) {                                             // the contacts span an area: the full plane
        a = (sxg * szz - szg * sxz) / det;
        b = (szg * sxx - sxg * sxz) / det;
    } else if (sxx + szz > 1e-9) {                                                      // a line (two feet): tilt along it only
        vec2 vv = sxx >= szz ? vec2(sxx, sxz) : vec2(sxz, szz);                         // a column of the rank-1 matrix: along the line
        vec2 u = vv / length(vv);
        float along = (sxg * u.x + szg * u.y) / (sxx * u.x * u.x + 2.0 * sxz * u.x * u.y + szz * u.y * u.y);
        a = along * u.x; b = along * u.y;
    }
    a = clamp(a, -CONTACT_MAX_SLOPE, CONTACT_MAX_SLOPE);
    b = clamp(b, -CONTACT_MAX_SLOPE, CONTACT_MAX_SLOPE);
    float d = -1e30;                                                                    // no contact below the ground
    for (int i = 0; i < n; i++) d = max(d, pg[i] - (a * (px[i] - cx) + b * (pz[i] - cz)));
    float floor_ = CONTACT_NO_TURN;
    if (_ew(ent_tex, row, 4) <= CONTACT_TURNS_TO) {                                     // a vehicle: the height its contacts stand at, the turn's middle
        floor_ = 0.0;
        for (int i = 0; i < n; i++) floor_ += ph[i] + float(lift);
        floor_ /= float(n);
        // the lift for the TURNED body: EVERY corner it stands on (fixed parts and wheels, as the contacts - a bike's
        // pedals left out) where contact_turn puts it (without D), against the ground under THAT place - no corner in the
        // ground. The contacts alone were not enough: a pitched body's corners stand at different heights (the game tips a
        // tank ~100 end to end on a steep hill), so the turn slides the high and low ones up or down the slope, and the
        // tread's up-curving end, not a contact, swung 4.8 into a 14 degree hill
        float hx = a * 0.5, hz = b * 0.5, k = inversesqrt(1.0 + hx * hx + hz * hz);
        vec3 nn = vec3(-hx * k, k, -hz * k);
        float ax = nn.z, az = -nn.x;
        d = -1e30;
        _Walk w2 = _Walk(ivec3(0), ivec3(0), ivec3(0), 0);
        bool rides2 = true;
        for (int i = 0; i < count; i++) {
            vec3 v;
            if (plain) v = vec3(_corner(at, i).xyz);
            else {
                int f = _corner(at, i).w;
                if (f != 0) rides2 = ((f >> 4) & 7) == 0 || (((f >> 4) & 7) == 1 && ((f >> 2) & 3) == 0);
                ivec3 own = _walk_step(w2, trig_tex, at, i, artic);                    // every corner steps the walk
                if (wheeled && !rides2) continue;
                v = corner_fine(own, ang, frac, trig_tex);
            }
            float h = v.y + float(y + _waterline(at) + lift);
            vec3 r = vec3(v.x * 0.5 - cx, (h - floor_) * 0.5, v.z * 0.5 - cz);
            float f = (ax * r.x + az * r.z) / (1.0 + nn.y);
            vec3 t = vec3(r.x * nn.y - az * r.y + ax * f, r.y * nn.y + az * r.x - ax * r.z, r.z * nn.y + ax * r.y + az * f);
            int gx = int(floor(float(x) + cx + t.x)), gz = int(floor(float(z) + cz + t.z));
            d = max(d, float(ground_height(cam_map, gx & 0xFFFF, gz & 0xFFFF)) - (floor_ + t.y * 2.0));
        }
    }
    fit = vec4(a, b, d, floor_); c = vec2(cx, cz);
    return true;
}

// The height change for a corner (dx, dz) world units from the record's x and z, of that fit (GroundContact.Lift).
float contact_lift_at(vec4 fit, vec2 c, float dx, float dz) {
    return fit.x * (dx - c.x) + fit.y * (dz - c.y) + fit.z;
}

// A corner placed on its thing's fit: p = its world place (x, height, z), (dx, dz) = that place from the record's x and z.
// A vehicle is TURNED onto the plane as one rigid body - about the fit's centre at its contacts' height, by the smallest
// turn that takes "up" to the plane's normal - then lifted by D. Moving the heights alone (the plane added to each
// corner, as people and animals still get: they stand upright on a slope) leaves the sides upright over a sloping
// floor: on a steep slope a box is pushed out of square, and the lean travels round the body as it turns.
// Worked in true lengths: a height counts half a world unit (the scene's heights are drawn at half), so the plane's
// slope is half its number and a turned height is doubled on the way back.
vec3 contact_turn(vec4 fit, vec2 c, vec3 p, float dx, float dz) {
    if (fit.w == CONTACT_NO_TURN || (fit.x == 0.0 && fit.y == 0.0)) {
        p.y += contact_lift_at(fit, c, dx, dz);
        return p;
    }
    float hx = fit.x * 0.5, hz = fit.y * 0.5;
    float k = inversesqrt(1.0 + hx * hx + hz * hz);
    vec3 n = vec3(-hx * k, k, -hz * k);                                                 // the plane's normal: where "up" goes
    vec3 r = vec3(dx - c.x, (p.y - fit.w) * 0.5, dz - c.y);                             // from the turn's middle
    float ax = n.z, az = -n.x;                                                          // the axis up x n (it has no height)
    float f = (ax * r.x + az * r.z) / (1.0 + n.y);
    vec3 t = vec3(r.x * n.y - az * r.y + ax * f, r.y * n.y + az * r.x - ax * r.z, r.z * n.y + ax * r.y + az * f);
    return vec3(p.x + (c.x + t.x - dx), fit.w + t.y * 2.0 + fit.z, p.z + (c.y + t.z - dz));
}

// WALLS DOWN: a level thing stands on the highest ground under
// its contacts, and each bottom corner (within CONTACT_LEVEL_BAND of the bottom) reaches down to the ground right under it - no gap on
// the downhill side, nothing buried uphill. y = the corner's placed height, at world x, z; the new height.
float walls_down(vec2 c, float x, float z, float y) {
    if (c.y != CONTACT_LEVEL || y > c.x + float(CONTACT_LEVEL_BAND)) return y;
    return y + min(0.0, float(ground_height(cam_map, int(x) & 0xFFFF, int(z) & 0xFFFF)) - c.x);   // down by the ground's dip below the bottom only: on flat ground the shape stays (a shell 14 tall is not flattened)
}

// The same for a corner rot as turned, not placed (x and z doubled).
float contact_lift(vec4 fit, vec2 c, vec3 rot) { return contact_lift_at(fit, c, rot.x * 0.5, rot.z * 0.5); }

// The fit of record row's main model (+$25), false when it stands as the game has it (no model, debris, not drawn).
bool contact_fit_row(int row, out vec4 fit, out vec2 c) {
    fit = vec4(0.0, 0.0, 0.0, CONTACT_NO_TURN); c = vec2(0.0);
    int model = _eb(ent_tex, row, 0x25);
#ifdef SUN_PASS
    if (_eb(ent_tex, row, 0) == 0 || model == 0 || _sb(_eb(ent_tex, row, 0x24)) < 0) return false;   // the sun's picture: in view or not
#else
    if (_eb(ent_tex, row, 0) == 0 || _eb(ent_tex, row, 0x1C) == 0 || model == 0 || _sb(_eb(ent_tex, row, 0x24)) < 0) return false;
#endif
    return contact_fit(row, int(texelFetch(model_tex, ivec2(model, 0), 0).r + 0.5), fit, c);
}
