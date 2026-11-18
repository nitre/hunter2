// Where the game puts its camera (PlaceCamera / PlaceCameraAt $374C / $3750, rules/Ground.cs Place): from the
// player's x, y, z, the camera distance ($1526) and lift ($1532), and the ground height under the camera.
// Doubled coordinates as the game keeps them: the view x is 2x, the view z is 2z minus the distance (never
// below 0). The horizon height ($E) is the higher of the player's y and the ground under the camera, plus $5A
// and the lift. Needs _wrap16 (corner.gdshaderinc) and ground_height.gdshaderinc.

struct CameraSpot {
	int view_x;        // $1548: 2 x
	int view_z;        // $1544: 2 z - distance, not below 0
	int grid_off_x;    // $1538: the ground grid's offset inside its first cell (words)
	int grid_off_z;    // $153A
	int grid_z;        // $154C: the row bucket base
	ivec2 view_cell;   // $1540 / $1542: the first map cell the view draws
	int ground_x;      // the world x / z the horizon's ground height is read at (x, z - distance / 2)
	int ground_z;
};

CameraSpot camera_spot(int x, int z, int distance) {
	CameraSpot c;
	int d0 = (x & 0xFFFF) * 2 + 0x1000;                      // $3764: add.l D0,D0; addi.l #$1000
	int d1 = (z & 0xFFFF) * 2 - distance;                    // $3766 / $376E
	if (d1 < 0) d1 = 0;                                      // $3774
	int d2 = d0 & 0x3FE, d3 = d1 & 0x3FE;                    // $3776-$377E
	c.grid_z = d3;                                           // $3782
	c.grid_off_x = _wrap16(-d2 + 0x1000);                    // $3786-$3796
	c.grid_off_z = _wrap16(-d3 + 0x1C00);
	c.view_x = d0 - 0x1000;                                  // $379C: movem.l D1,D2 -> $1544, $1548
	c.view_z = d1;
	int cx = _wrap16((d0 >> 8) >> 2), cz = _wrap16((d1 >> 8) >> 2);   // $37AE-$37B4: asr.l #8, asr.l #2
	c.view_cell = ivec2(_wrap16((cx >> 3) - 1), _wrap16(cz >> 3));   // $37BA-$37C0: asr.w #3, then -1 on x
	c.ground_x = c.view_x >> 1;                              // $37EE: lsr.l #1
	c.ground_z = d1 >> 1;
	return c;
}

// $37F8-$380A: the horizon height. player_y = +$52 (the player's y), ground = the height under (ground_x, ground_z),
// lift = $1532.
int camera_horizon(int player_y, int ground, int lift) {
	int y = _wrap16(player_y) < _wrap16(ground) ? _wrap16(ground) : _wrap16(player_y);   // $37FC: cmp, bge
	return _wrap16(y + 0x5A + lift);
}
