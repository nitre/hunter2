// The original's explosion debris on the card (DebrisVertices $536E, DebrisProject $524E, the step $5AE0): model
// 0 is the debris model; 10 pieces of 3 points each - the explosion's centre and two rotated corners of model 0,
// each piece pushed out by its own offset. Corners are read three to a piece (the first, the centre's, is
// skipped), the pitch and yaw angle sets SWAP from one piece to the next, and the roll stays.
// The offsets are the game's 30-word table (rules WorldState.DebrisPos): X[10], Z[10], Y[10]; the game adds the
// velocity table to them each draw ($5AE0), which the rules do - the shader is handed the current table.
// Needs corner.gdshaderinc and model.gdshaderinc (_at, _corner); returns the point DOUBLED in x / z like place.gdshaderinc.


// piece 0..9, j 0..2 (0 = the centre); base0 = model 0's objfile offset (its store corners carry the shower's model scale
// ($1578); roll / pitch / yaw = the record's +$80 / +$7C / +$7E; pos = (2 x, y, 2 z) of the record; off[30] the table.
ivec3 debris_point2(sampler2D trig, int base0, int roll, int pitch, int yaw,
                    ivec3 pos2, int offx, int offy, int offz, int piece, int j) {
    if (j == 0) return ivec3(pos2.x + offx, _wrap16(pos2.y + offy), pos2.z + offz);
    int v = piece * 3 + j;                                  // the corner stream: skip one, read two, per piece
    ivec3 c = _corner(_at(base0), v).xyz;
    bool swapped = (piece & 1) != 0;                        // $53A4: the pitch and yaw sets trade places
    ivec3 ang = ivec3(roll, swapped ? yaw : pitch, swapped ? pitch : yaw);
    ivec3 r = corner(c, ang, 0, ivec3(0), trig);
    return ivec3(pos2.x + r.x + offx, _wrap16(pos2.y + r.y + offy), pos2.z + r.z + offz);
}
