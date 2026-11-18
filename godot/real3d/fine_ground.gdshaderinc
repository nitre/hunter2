// (FINER GROUND, rules/FineGround.cs): the finer, taller height store as the card reads
// it - fine_n points a map cell side ((128 fine_n + 1)^2 texels, each a height in world units, exact in a float), the
// extra column/row the points the original's memory walk reads past the map's east/south edges. fine_n 0 (the
// default, and every check harness) leaves ground_height and ground_point on the map bytes, bit for bit.
// Included by ground_height.gdshaderinc and ground_cell.gdshaderinc, in either order - hence the guard.
#ifndef FINE_GROUND_INC
#define FINE_GROUND_INC

uniform int fine_n = 0;
uniform sampler2D fine_map : filter_nearest;

int fine_at(int i, int j) { return int(texelFetch(fine_map, ivec2(i, j), 0).r + 0.5); }

// $3710-$371A in 32 bits, as FineGround.Lerp (GLSL int division truncates toward zero, >> is arithmetic)
int fine_lerp(int d0, int d1, int f) { return (((d1 - d0) * f / 256) >> 4) + d0; }

// $36AA on the fine cell (FineGround.Height): fractions in 4096ths at any fine_n, three lerps
int fine_height(int x, int z) {
	int cell = 512 / fine_n, per = 4096 / cell;
	int i = x / cell, j = z / cell, fx = (x % cell) * per, fz = (z % cell) * per;
	int a = fine_lerp(fine_at(i, j), fine_at(i, j + 1), fz);
	int b = fine_lerp(fine_at(i + 1, j), fine_at(i + 1, j + 1), fz);
	return fine_lerp(a, b, fx);
}

// The original's map point (col, row) in the fine store, walked as the memory reads it (past col 127 the next row).
int fine_map_point(int col, int row) {
	int n = row * 128 + col;
	if (n >= 128 * 128) return 0;
	return fine_at((n & 127) * fine_n, (n >> 7) * fine_n);
}

#endif
