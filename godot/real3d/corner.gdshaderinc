// The game's model-corner rotation (rules/Objects.cs $53E2: Roll, Pitch, Yaw, Main), number-exact
// on the graphics card. The trig table comes in as the texture TrigTexture.From built (512 x 2,
// Rf: row 0 = Sin, row 1 = Cos; entry a at x = a*2 = exponent word, x = a*2+1 = signed mantissa).
// Every step mirrors the 68000: a signed 16 x 16 multiply to 32 bits, an ARITHMETIC shift right
// by the exponent (on int, >> is arithmetic), and a wrap to 16 bits after each rotation and after
// the view position is added - (v << 16) >> 16 sign-extends the low 16 bits.

int _wrap16(int v) { return (v << 16) >> 16; }

// Sin(a) (half 0) or Cos(a) (half 1) of an angle byte as (exponent, mantissa).
ivec2 _em(int a, int half, sampler2D trig) {
    return ivec2(int(round(texelFetch(trig, ivec2(a * 2, half), 0).r)),
                 int(round(texelFetch(trig, ivec2(a * 2 + 1, half), 0).r)));
}

// Mul((E, M), v) = Muls(v, M) >> E: signed 16 x 16 -> 32 bits, arithmetic shift right.
int _mul(ivec2 f, int v) { return (v * f.y) >> f.x; }

void _roll(inout int x, inout int y, int a, sampler2D trig) {
    int nx = _wrap16(_mul(_em(a, 1, trig), x) + _mul(_em(a, 0, trig), y));
    int ny = _wrap16(_mul(_em(a, 1, trig), y) - _mul(_em(a, 0, trig), x));
    x = nx; y = ny;
}

void _pitch(inout int y, inout int z, int a, sampler2D trig) {
    int nz = _wrap16(_mul(_em(a, 1, trig), z) + _mul(_em(a, 0, trig), y));
    int ny = _wrap16(_mul(_em(a, 1, trig), y) - _mul(_em(a, 0, trig), z));
    y = ny; z = nz;
}

void _yaw(inout int x, inout int z, int a, sampler2D trig) {
    int nx = _wrap16(_mul(_em(a, 1, trig), x) + _mul(_em(a, 0, trig), z));
    int nz = _wrap16(_mul(_em(a, 1, trig), z) - _mul(_em(a, 0, trig), x));
    x = nx; z = nz;
}

// Main: p = the corner as signed bytes << the model's scale shift; angles = (roll, pitch, yaw)
// bytes - each rotation runs only when its own byte is non-zero (the game skips a 0 angle, and
// the table's cos(0) is exactly 1, so skipping is what keeps it bit-exact); turn = the model's
// own turn byte ($80 negates x and z EXACTLY, the table's sin(180) is not 0); origin = the
// entity's view position, added last and wrapped.
ivec3 corner(ivec3 p, ivec3 angles, int turn, ivec3 origin, sampler2D trig) {
    if (angles.x != 0) _roll(p.x, p.y, angles.x, trig);
    if (angles.y != 0) _pitch(p.y, p.z, angles.y, trig);
    if (angles.z != 0) _yaw(p.x, p.z, angles.z, trig);
    if (turn == 0x80) { p.x = _wrap16(-p.x); p.z = _wrap16(-p.z); }
    else if (turn != 0) _yaw(p.x, p.z, turn, trig);
    return ivec3(_wrap16(p.x + origin.x), _wrap16(p.y + origin.y), _wrap16(p.z + origin.z));
}

// ENHANCEMENT (FINE ANGLES, the card): the same three turns at a finer angle - frac = each angle's part of a step past
// its byte (roll, pitch, yaw; 1/256 steps, from the record's fine angles, Entity.PitchFine etc.). No fraction: the exact
// path above, number for number. With one: real sines (the table's are the same to its precision), rounded once at the end.
ivec3 corner_fine(ivec3 p, ivec3 angles, vec3 frac, sampler2D trig) {
    if (frac == vec3(0.0)) return corner(p, angles, 0, ivec3(0), trig);
    vec3 a = (vec3(angles) + frac / 256.0) * (6.28318530718 / 256.0);
    float x = float(p.x), y = float(p.y), z = float(p.z), c, s, t;
    c = cos(a.x); s = sin(a.x); t = c * x + s * y; y = c * y - s * x; x = t;   // roll  (_roll)
    c = cos(a.y); s = sin(a.y); t = c * z + s * y; y = c * y - s * z; z = t;   // pitch (_pitch)
    c = cos(a.z); s = sin(a.z); t = c * x + s * z; z = c * z - s * x; x = t;   // yaw   (_yaw)
    return ivec3(_wrap16(int(round(x))), _wrap16(int(round(y))), _wrap16(int(round(z))));
}
