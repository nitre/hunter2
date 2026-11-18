// The sky and sea bands, as the copper draws them (SetSkyCopper $62B4 / SkyCopperFull $62B0; rules/Sky.cs Bands):
// the view is cleared to colour 15 and six copper lines reload it, so open sea and sky are a stepped gradient.
// The horizon row h = (camera height $E * 256) / $2B00 + $2E (divs.w: on a quotient overflow past 16 bits the
// 68000 leaves the dividend, and only its low word is used). The six lines sit at h - 62, -30, -14, -6, -2 and
// 0 (the table at $62F8: 2, 4, 8, 16, 32, 0, taken off one after the other going up from the horizon), each
// clamped to view rows 0..$8B. A band's colour shows from the row AFTER its line.
// Self-contained (no other include needed).

int _sky16(int v) { return (v << 16) >> 16; }

// The horizon row ($1534) for the camera height Height0E ($E).
int sky_horizon(int height0E)
{
	int d = _sky16(height0E) << 8;                       // ext.l, lsl.l #8
	int q = d / 0x2B00;                                  // divs.w #$2B00: truncates toward zero
	if (q < -32768 || q > 32767) q = d;                  // overflow: the dividend stays
	return _sky16(q + 0x2E);
}

// Band k's line, k = 0 (the top band) .. 5 (the horizon's own), in view rows.
int sky_band_row(int height0E, int k)
{
	int off[6] = int[6](62, 30, 14, 6, 2, 0);            // the running sum of 32, 16, 8, 4, 2 up from the horizon
	int r = _sky16(sky_horizon(height0E) - off[k]);
	return clamp(r, 0, 0x8B);
}

// Which of the 7 colours (0 = the bank's own colour 15, k + 1 = band k's) view row y shows.
int sky_colour_index(int height0E, int y)
{
	int c = 0;
	for (int k = 0; k < 6; k++)
		if (y > sky_band_row(height0E, k)) c = k + 1;
	return c;
}
