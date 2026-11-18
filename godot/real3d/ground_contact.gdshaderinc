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

// The plane for record row's model (base): fit = (A, B, D), c = the centre (cx, cz) the tilt turns about, in world
// units from the record's own x and z. False = this thing stands as the game has it.
bool contact_fit(int row, int base, out vec3 fit, out vec2 c) {
    fit = vec3(0.0); c = vec2(0.0);
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
    int low = plain ? model_min_y_plain(at, y) : model_min_y(trig_tex, at, artic, ang, 0, ivec3(0, y, 0));
    int lift = lift_to_ground(ground, low);
    float px[CONTACT_MAX]; float pz[CONTACT_MAX]; float pg[CONTACT_MAX]; int ph[CONTACT_MAX];
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
        ivec3 v;
        if (plain) v = _corner(at, k).xyz;
        else {
            int f = _corner(at, k).w;
            if (f != 0) rides = ((f >> 4) & 7) == 0 || (((f >> 4) & 7) == 1 && ((f >> 2) & 3) == 0);
            ivec3 own = _walk_step(w, trig_tex, at, k, artic);
            v = corner(own, ang, 0, ivec3(0), trig_tex);
            if (!level && (own.y > bottom + 3 || (wheeled && !rides))) continue;
        }
        int h = _wrap16(v.y + y + _waterline(at));                                      // as low: the corner's height before the lift (SeaBodies: from the water line)
        if ((level || plain) && h > low + (level ? CONTACT_LEVEL_BAND : 3)) continue;
        int wx = 2 * x + v.x, wz = 2 * z + v.z;                                         // doubled world place
        px[n] = float(v.x) * 0.5; pz[n] = float(v.z) * 0.5;
        pg[n] = float(ground_height(cam_map, (wx >> 1) & 0xFFFF, (wz >> 1) & 0xFFFF) - _wrap16(h + lift));
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
        for (int i = 0; i < n; i++) d = max(d, ph[i] >= low + step ? pg[i] : pg[i] - float(step));   // the door's foot at most one step in
        fit = vec3(0.0, 0.0, d);
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
    fit = vec3(a, b, d); c = vec2(cx, cz);
    return true;
}

// The height change for a corner (dx, dz) world units from the record's x and z, of that fit (GroundContact.Lift).
float contact_lift_at(vec3 fit, vec2 c, float dx, float dz) {
    return fit.x * (dx - c.x) + fit.y * (dz - c.y) + fit.z;
}

// WALLS DOWN: a level thing stands on the highest ground under
// its contacts, and each bottom corner (within CONTACT_LEVEL_BAND of the bottom) reaches down to the ground right under it - no gap on
// the downhill side, nothing buried uphill. y = the corner's placed height, at world x, z; the new height.
float walls_down(vec2 c, float x, float z, float y) {
    if (c.y != CONTACT_LEVEL || y > c.x + float(CONTACT_LEVEL_BAND)) return y;
    return y + min(0.0, float(ground_height(cam_map, int(x) & 0xFFFF, int(z) & 0xFFFF)) - c.x);   // down by the ground's dip below the bottom only: on flat ground the shape stays (a shell 14 tall is not flattened)
}

// The same for a corner rot as turned, not placed (x and z doubled).
float contact_lift(vec3 fit, vec2 c, ivec3 rot) { return contact_lift_at(fit, c, float(rot.x) * 0.5, float(rot.z) * 0.5); }

// The fit of record row's main model (+$25), false when it stands as the game has it (no model, debris, not drawn).
bool contact_fit_row(int row, out vec3 fit, out vec2 c) {
    fit = vec3(0.0); c = vec2(0.0);
    int model = _eb(ent_tex, row, 0x25);
    if (_eb(ent_tex, row, 0) == 0 || _eb(ent_tex, row, 0x1C) == 0 || model == 0 || _sb(_eb(ent_tex, row, 0x24)) < 0) return false;
    return contact_fit(row, int(texelFetch(model_tex, ivec2(model, 0), 0).r + 0.5), fit, c);
}
