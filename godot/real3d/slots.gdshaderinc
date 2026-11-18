// A model face cut into triangles (the graphics card draws triangles; the game's fill took a polygon
// of edges). Built on face_corner (face.gdshaderinc) and model_local (model.gdshaderinc): the face's
// corners in the model's rest pose (the articulation angles at 0), then EAR CLIPPING in the face's own
// plane - a fan from one corner fills a convex face right but a hollow (concave) face wrong, and the
// data has both. Each triangle comes back as three positions in the face's corner loop, in loop order,
// so every triangle keeps the polygon's own turning direction and together they add up to the polygon.

int _plane_axis(ivec3 n) {                       // the axis the face is flattest along
    ivec3 a = abs(n);
    return (a.x >= a.y && a.x >= a.z) ? 0 : (a.y >= a.z ? 1 : 2);
}

ivec2 _flat(ivec3 p, int axis) {                 // drop that axis: (y,z), (z,x) or (x,y), turning the same way as the normal
    return axis == 0 ? p.yz : (axis == 1 ? p.zx : p.xy);
}

int _turn(ivec2 a, ivec2 b, ivec2 c) {           // twice the signed area of a, b, c
    return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
}

// FACE_MAX: the most corners a face may have here (CardModels.MaxFaceCorners, the same number - it says so when a builder passes
// it). Was 16, the objfile's own most (a face's edge count is 4 bits, an open loop one corner more); the card's own builders are
// not held to the file's format, so it is a size for the arrays below, kept
// generous - the loops stop at the face's own corner count, so a small face costs what it did.
const int FACE_MAX = 64;

// Triangle t of the face (t = 0 .. corners-3): x, y, z = positions in the corner loop. (-1, -1, -1) past the end.
ivec3 face_triangle(sampler2D trig, int at, int face, int n, int t) {
    int m = face_corner(at, face, n, 0).x;
    if (m < 3 || t > m - 3) return ivec3(-1);
    ivec3 P[FACE_MAX];
    for (int i = 0; i < FACE_MAX; i++)
        P[i] = i < m ? model_local(trig, at, face_corner(at, face, n, i).y, ivec4(0)) : ivec3(0);
    ivec3 N = ivec3(0);                          // Newell's vector area of the loop
    for (int i = 0; i < FACE_MAX; i++) {
        if (i >= m) break;
        ivec3 p = P[i], q = P[(i + 1) % m];
        N += ivec3((p.y - q.y) * (p.z + q.z), (p.z - q.z) * (p.x + q.x), (p.x - q.x) * (p.y + q.y));
    }
    int axis = _plane_axis(N);
    int sgn = (axis == 0 ? N.x : (axis == 1 ? N.y : N.z)) < 0 ? -1 : 1;
    int idx[FACE_MAX];
    for (int i = 0; i < FACE_MAX; i++) idx[i] = i;
    int rem = m;
    for (int step = 0; step < FACE_MAX - 2; step++) {
        if (rem == 3) return ivec3(idx[0], idx[1], idx[2]);
        int ear = 0;
        bool found = false;
        for (int i = 0; i < FACE_MAX && !found; i++) {
            if (i >= rem) break;
            int a = idx[(i + rem - 1) % rem], b = idx[i], c = idx[(i + 1) % rem];
            ivec2 fa = _flat(P[a], axis), fb = _flat(P[b], axis), fc = _flat(P[c], axis);
            if (sgn * _turn(fa, fb, fc) <= 0) continue;          // not convex here
            bool clear = true;
            for (int j = 0; j < FACE_MAX; j++) {
                if (j >= rem) break;
                int q = idx[j];
                if (q == a || q == b || q == c) continue;
                ivec2 fq = _flat(P[q], axis);
                if (sgn * _turn(fa, fb, fq) >= 0 && sgn * _turn(fb, fc, fq) >= 0 && sgn * _turn(fc, fa, fq) >= 0) clear = false;
            }
            if (clear) { ear = i; found = true; }
        }
        int a = idx[(ear + rem - 1) % rem], b = idx[ear], c = idx[(ear + 1) % rem];
        if (step == t) return ivec3(a, b, c);
        for (int i = 0; i < FACE_MAX; i++) {            // take the ear's tip out of the list
            if (i >= rem - 1) break;
            if (i >= ear) idx[i] = idx[i + 1];
        }
        rem--;
    }
    return ivec3(-1);
}
