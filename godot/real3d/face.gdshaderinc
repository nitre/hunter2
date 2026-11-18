// A model face's outline, read from the card model store the way the game's own fill reads it
// (re/out disassembly $77C4 DrawOneFace / $7800 InitVertexScan, rules/Raster.Fill): a face is a
// list of EDGE numbers in order round the polygon (each edge two vertex-index bytes in the model's
// edge table); the fill walks the list from its top edge, forward for one side and backward for the
// other. Here the same list gives the corners: a closed chain (every edge shares a vertex with the
// next, round to the first) has one corner per edge, at the vertex two neighbours share; an open
// chain (one neighbouring pair shares nothing - a few dozen faces) has one more corner than edges,
// the two loose ends, and the fill closes the gap between them.
// Needs _edge, _face, _slot from model.gdshaderinc.

ivec2 _face_edge(int at, int face, int i) {
    return _edge(at, _slot(at, _face(at, face).w + i));
}

bool _shares(ivec2 a, ivec2 b) { return a.x == b.x || a.x == b.y || a.y == b.x || a.y == b.y; }
int _common(ivec2 a, ivec2 b) { return (a.x == b.x || a.x == b.y) ? a.x : a.y; }
int _other(ivec2 a, int v) { return a.x == v ? a.y : a.x; }

// x = how many corners the face has; y = the vertex index of corner k (0 when k is past the end).
// at = the model's store address, face = the face's number in the model (its n edge numbers sit in the store's lists).
ivec2 face_corner(int at, int face, int n, int k) {
    int brk = n == 1 ? 0 : -1;                  // one edge: a line, two loose ends
    for (int i = 0; i < n && brk < 0; i++)
        if (!_shares(_face_edge(at, face, i), _face_edge(at, face, (i + 1) % n))) brk = i;
    if (brk < 0) {
        if (k >= n) return ivec2(n, 0);
        return ivec2(n, _common(_face_edge(at, face, (k + n - 1) % n), _face_edge(at, face, k)));
    }
    int s = (brk + 1) % n;
    int m = n + 1;
    if (k >= m) return ivec2(m, 0);
    if (n == 1) return ivec2(m, k == 0 ? _face_edge(at, face, s).x : _face_edge(at, face, s).y);
    if (k == 0) {
        ivec2 e0 = _face_edge(at, face, s);
        return ivec2(m, _other(e0, _common(e0, _face_edge(at, face, (s + 1) % n))));
    }
    if (k == n) {
        ivec2 el = _face_edge(at, face, (s + n - 1) % n);
        return ivec2(m, _other(el, _common(_face_edge(at, face, (s + n - 2) % n), el)));
    }
    return ivec2(m, _common(_face_edge(at, face, (s + k - 1) % n), _face_edge(at, face, (s + k) % n)));
}
