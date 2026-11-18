// The game's ground height as a GPU function (gpu_height): a bit-for-bit port of rules/Ground.cs
// Height ($36AA), detail level 0, for the real-3D shaders to #include. The CPU bilinearly blends the
// four map corner heights of the point's 512-unit cell, in 68000 word maths: the Lerp ($3710-$371A)
// is muls (16 x 16 -> 32), divs.w by $100 (signed, truncates toward zero; on a quotient overflow past
// 16 bits the 68000 leaves the dividend untouched - M68.Divs copies that), asr.w #4, then a 16-bit
// wrap. GLSL int division truncates toward zero and >> on int is arithmetic, so only the 16-bit
// wraps need doing by hand. x, z are world units 0..65535 (cell = 512); the result is the height in
// world units (the map byte << 4, so 0..4080).
// ENHANCEMENT (Play.Maths32, set as the "maths32" uniform wherever the haze uniforms are set): on,
// ground_height_lerp68 is the same lerp in plain 32-bit integers - no wraps, no overflow rule; off
// (the default), the word maths above, bit for bit.

uniform bool maths32;

#include "res://real3d/fine_ground.gdshaderinc"

int ground_height_lerp68(int d0, int d1, int f)
{
	if (maths32) return (((d1 - d0) * f / 256) >> 4) + d0;   // diff * f, / $100, >> 4, + d0 - no wraps
	int diff = ((d1 - d0) << 16) >> 16;              // (short)(d1 - d0): the 16-bit wrap before the multiply
	int p = diff * f;                                // muls: exact in 32 bits (|diff| <= 8191, f <= 4088)
	int q = p / 256;                                 // divs.w $100: truncates toward zero, like GLSL
	if (q < -32768 || q > 32767) q = p;              // divs overflow: the register keeps the dividend
	int w = ((q & 0xFFFF) ^ 0x8000) - 0x8000;        // the quotient's low word, sign-extended
	int h = (w >> 4) + d0;                           // asr.w #4, then add
	return (h << 16) >> 16;                          // the 16-bit wrap of the result word
}

// The cell's four corner heights (h00, h01 = next row, h10 = next column, h11), exactly as the CPU's map memory reads
// them (Terrain.Map is 130 rows of 256 bytes; rows 128/129 stay zero): at row 127 the "next row" neighbour is height
// 0, and at col 127 the "next column" walks linearly into the next row's first points (no clamp in the original).
ivec4 ground_corners(sampler2D map, int col, int row)
{
	int h00 = int(texelFetch(map, ivec2(col, row), 0).r + 0.5) << 4;
	int h01, h10, h11;
	if (row < 127) h01 = int(texelFetch(map, ivec2(col, row + 1), 0).r + 0.5) << 4;
	else h01 = 0;
	if (col < 127) h10 = int(texelFetch(map, ivec2(col + 1, row), 0).r + 0.5) << 4;
	else if (row < 127) h10 = int(texelFetch(map, ivec2(0, row + 1), 0).r + 0.5) << 4;
	else h10 = 0;
	// the diagonal neighbour is +$102 ($3726): one column right in the row below - and at col 127 the strip carries
	// it to the first point of the row TWO below (the worker's check of $36BC-$3726, gpu_move 2026-10-24)
	if (col < 127) h11 = row < 127 ? int(texelFetch(map, ivec2(col + 1, row + 1), 0).r + 0.5) << 4 : 0;
	else h11 = row < 126 ? int(texelFetch(map, ivec2(0, row + 2), 0).r + 0.5) << 4 : 0;
	return ivec4(h00, h01, h10, h11);
}

int ground_height(sampler2D map, int x, int z)
{
	if (fine_n > 0) return fine_height(x, z);        // ENHANCEMENT (FINER GROUND): the fine store, 32-bit lerps
	int col = x >> 9, row = z >> 9;                  // the 512-unit cell
	int fxx = (x & 511) << 3, fzz = (z & 511) << 3;  // the fractions in 4096ths, as $36AA shifts them
	ivec4 h = ground_corners(map, col, row);
	int a = ground_height_lerp68(h.x, h.y, fzz);
	int b = ground_height_lerp68(h.z, h.w, fzz);
	return ground_height_lerp68(a, b, fxx);
}

// ENHANCEMENT, NOT THE ORIGINAL (FINER GROUND, the second lookup - rules/Ground.cs SmoothHeight, FineGround.Smooth):
// the original's bilinear of its points times the factor (the byte wrap undone), in 32 bits; off, ground_height itself.
int ground_height_smooth(sampler2D map, int x, int z)
{
	if (fine_n == 0) return ground_height(map, x, z);
	int col = x >> 9, row = z >> 9;
	int fxx = (x & 511) << 3, fzz = (z & 511) << 3;
	// the corners from the store's own lattice: the map's points x the factor with the byte wrap undone, walked as the
	// memory reads them (+2 / +$100 / +$102) - the CPU's FineGround.Smooth holds exactly these
	ivec4 h = ivec4(fine_map_point(col, row), fine_map_point(col, row + 1), fine_map_point(col + 1, row), fine_map_point(col + 1, row + 1));
	int a = fine_lerp(h.x, h.y, fzz);
	int b = fine_lerp(h.z, h.w, fzz);
	return fine_lerp(a, b, fxx);
}
