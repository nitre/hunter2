// The CPU's palette fades as shader functions, so a fragment can fade its own colour with the
// game's exact maths. ENHANCEMENT, NOT THE ORIGINAL: the original runs these on the 68000 and
// uploads finished colours (godot/GameView.cs ViewPalette only looks them up).
// All three work on $0RGB ints: three nibbles 0..15, red = c >> 8 & 15, green = c >> 4 & 15, blue = c & 15.
// Bit-exact against rules/Play.cs; checked colour by colour on the card by real3d/CheckFade.cs.

/// Play.Faded ($6A14): each nibble n -> (n * level) >> 4, level 0..15.
int faded(int c, int level) {
	return ((c >> 8 & 15) * level >> 4) << 8 | ((c >> 4 & 15) * level >> 4) << 4 | (c & 15) * level >> 4;
}

/// Play.Faded60 (SmoothLight): each nibble n -> n * level60 / 960 (level60 in 60ths, 0..900);
/// integer division truncates, as on the CPU.
int faded60(int c, int level60) {
	return (c >> 8 & 15) * level60 / 960 << 8 | (c >> 4 & 15) * level60 / 960 << 4 | (c & 15) * level60 / 960;
}

/// Play.DeathFaded ($6AC2, $6B4C): 15 steps from the colour on show towards (average, 0, 0);
/// >> of a negative int is arithmetic on both the 68000 and the card.
int death_faded(int c, int steps) {
	int r = (c >> 8 & 15) * 16;
	int g = (c >> 4 & 15) * 16;
	int b = (c & 15) * 16;
	int avg = (r + g + b) / 3;
	return (r + steps * ((avg - r) >> 4) >> 4 & 15) << 8
	     | (g + steps * ((0 - g) >> 4) >> 4 & 15) << 4
	     | (b + steps * ((0 - b) >> 4) >> 4 & 15);
}
