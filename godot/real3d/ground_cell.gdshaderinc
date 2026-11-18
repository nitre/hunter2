// The game's ground drawn from the map, as GPU functions: DrawView ($39B0), DrawGroundPolygon ($3A46) and the
// cell tables at $3922 / $3930 (rules/Ground.cs CellEdges / CellFaces, Draw), without the 7 x 7 window and the
// 1991 screen projection - the real camera and the depth buffer do those.
//
// A map point (col, row) is the cell's NEAR-LEFT corner (the terrain generator writes its type from the point
// and the neighbours at +col and +row, terrain.md $CE8E; $36AA bilinears the same cell). Its four corners:
//   P3 = (col, row)  P2 = (col+1, row)  P1 = (col+1, row+1)  P0 = (col, row+1)      heights = byte << 4
// (the drawing's quad[0..3] = left-far, right-far, right-near, left-near = P0..P3). The type byte: 0 = no
// cell; colour = low nibble; shape = bits 4-6 picking the faces at $3930:
//   0, 1       the full square: the two triangles A = (P1, P2, P3) and B = (P3, P0, P1), split P1-P3 (the
//              game draws it as one polygon unless it is folded on screen, then as exactly these two)
//   2          A only          3  (P0, P1, P2)          4, 6, 7  B only          5  (P0, P2, P3)
// Needs ground_height.gdshaderinc's map texture (heights) and the type texture of ground_flags.gdshaderinc.

#include "res://real3d/fine_ground.gdshaderinc"

// One map point's height in world units, the map exactly as the game's memory reads it (see ground_height):
// past the last column the walk runs into the next row, past the last row it is zero.
int ground_point(sampler2D map, int col, int row) {
	if (fine_n > 0) return fine_map_point(col, row);     // ENHANCEMENT (FINER GROUND): the store's point on the map
	int n = row * 128 + col;
	if (n >= 128 * 128) return 0;
	return int(texelFetch(map, ivec2(n & 127, n >> 7), 0).r + 0.5) << 4;
}

// Cell corner k (0..3 = P0..P3) as (x, height, z) in world units.
ivec3 ground_corner(sampler2D map, int col, int row, int k) {
	int dc = (k == 1 || k == 2) ? 1 : 0;
	int dr = (k == 0 || k == 1) ? 1 : 0;
	return ivec3((col + dc) * 512, ground_point(map, col + dc, row + dr), (row + dr) * 512);
}

// Triangle slot s (0 or 1) of the cell: returns its three corner numbers (P-indices) in x, y, z, or (-1, -1, -1)
// when the cell has no such triangle.
ivec3 ground_cell_triangle(sampler2D types, int col, int row, int s) {
	int t = int(texelFetch(types, ivec2(col, row), 0).r + 0.5);
	if (t == 0) return ivec3(-1);
	int shape = (t >> 4) & 7;
	if (shape <= 1) return s == 0 ? ivec3(1, 2, 3) : ivec3(3, 0, 1);
	if (shape == 2) return s == 0 ? ivec3(1, 2, 3) : ivec3(-1);
	if (shape == 3) return s == 0 ? ivec3(0, 1, 2) : ivec3(-1);
	if (shape == 5) return s == 0 ? ivec3(0, 2, 3) : ivec3(-1);
	return s == 0 ? ivec3(3, 0, 1) : ivec3(-1);                      // 4, 6, 7
}

int ground_cell_colour(sampler2D types, int col, int row) {
	return int(texelFetch(types, ivec2(col, row), 0).r + 0.5) & 15;
}

// The water half of slot s (0 or 1): the flat sea-level triangle DrawView leaves undrawn - a type-0 cell (no
// land) both slots, a coast shape (2-7) its empty slot - the other triangle of the square along the land half's
// own diagonal (rules/GroundMesh.cs Land). A full square has none. The water's height is the sea's 0 and
// its colour the Sea tag 16, never the map's - the caller's job.
ivec3 ground_water_triangle(sampler2D types, int col, int row, int s) {
	int t = int(texelFetch(types, ivec2(col, row), 0).r + 0.5);
	if (t == 0) return s == 0 ? ivec3(1, 2, 3) : ivec3(3, 0, 1);
	int shape = (t >> 4) & 7;
	if (shape <= 1) return ivec3(-1);
	if (shape == 2) return s == 1 ? ivec3(3, 0, 1) : ivec3(-1);
	if (shape == 3) return s == 1 ? ivec3(0, 2, 3) : ivec3(-1);
	if (shape == 5) return s == 1 ? ivec3(0, 1, 2) : ivec3(-1);
	return s == 1 ? ivec3(1, 2, 3) : ivec3(-1);                      // 4, 6, 7
}

// Whether the world spot (x, z) is on water: a type-0 cell, or the inside of a coast shape's water half (above;
// rules/GroundMesh.cs Water).
// u, v = across the cell from P3, east and north; each land triangle is the side of its diagonal holding its own corners.
bool ground_is_water(sampler2D types, int x, int z) {
	int t = int(texelFetch(types, ivec2((x & 0xFFFF) >> 9, (z & 0xFFFF) >> 9), 0).r + 0.5);
	if (t == 0) return true;
	int shape = (t >> 4) & 7, u = x & 511, v = z & 511;
	if (shape <= 1) return false;
	if (shape == 2) return u < v;                                    // land (P1, P2, P3)
	if (shape == 3) return u + v < 512;                              // land (P0, P1, P2)
	if (shape == 5) return u + v > 512;                              // land (P0, P2, P3)
	return u > v;                                                    // land (P3, P0, P1)
}
