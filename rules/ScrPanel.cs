namespace Hunter.Rules;

/// <summary>STUNTIN': the racing console - the panel's instrument rows (0-36) drawn new in Hunter's panel style (its colours,
/// raised plates with a light top and dark bottom edge, grooves between them, its bolts, recessed wells, blue displays as its
/// clock's, small orange labels), each instrument in a place made for it, laid out as Stunt Car Racer's dash: the driving
/// instruments in the middle (a speed dial flanked by the damage gauge - the original's crack, updateDamageBarHUD: a step a
/// damage point, wandering - and the boost gauge), where you are and your times to the left (the track map; TIME, BEST),
/// the race to the right (POS, LAP, and the GAP to the other car: renderDistanceDisplay, + ahead). The message strip (rows
/// 37-59) and the QUIT column are the panel's own; the race's messages go to that strip (ScrDrive.Announce).</summary>
public static class ScrPanel
{
    /// <summary>Racing: the player's car driven by the original's physics on a road.</summary>
    public static bool On(WorldState s) => s.Ground.Road is not null && s.ScrDrive is not null;

    const int Black = 0, Blue = 2, White = 3, Red = 5, DarkRed = 6, Yellow = 7, Orange = 8, Brown = 9,
        DarkBrown = 10, Edge = 11, Grey = 12, Dim = 13, Plate = 14, Groove = 15;

    sealed class Shown
    {
        public int Passes, Damage = -1, CrackX;
        public int[,]? Map;
        public (double X, double Z, double S) Fit;
        public double[]? Along;
        public (double X, double Z)[]? Mids;
        public double Lap, Needle;
        public byte[]? Dial;
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Panel, Shown> State = new();

    // THE LAYOUT (pixels of the 320 x 60 panel; the instruments in rows 0-36)
    /// <summary>The plates, left to right: map, timing, driving, race (the QUIT column after, the panel's own).</summary>
    static readonly (int X0, int X1)[] Plates = [(0, 47), (50, 129), (132, 209), (212, 284)];
    const int MapX = 10, MapY = 4, MapSize = 29;                                   // the map's well (inside its frame)
    const int TimeX = 62, TimeY = 9, BestY = 26;                                   // the timing displays (7 characters)
    const int DialX = 170, DialY = 19, DialR = 16, DialTop = 160;                  // the speed dial
    const int DamageX0 = 140, DamageX1 = 147, BoostX0 = 193, BoostX1 = 200, GaugeTop = 9, GaugeBottom = 34;   // the gauges
    const int PosX = 220, LapX = 252, RaceY = 9, GapX = 224, GapY = 26;           // the race displays

    /// <summary>Once a pass, in place of Hunter's UpdatePanel.</summary>
    public static void Update(WorldState s, Panel p)
    {
        var st = State.GetOrCreateValue(p);
        p.Racing = true;
        if (st.Map is null) Paint(s, p, st);
        st.Passes++;
        var me = s.ScrDrive!;
        var px = p.Pixels.Pixels;
        void Dot(int x, int y, int c) { if ((uint)x < 320 && (uint)y < Panel.Rows) px[y * 320 + x] = (byte)c; }
        void Fill(int x0, int y0, int x1, int y1, int c) { for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) Dot(x, y, c); }
        void Text(int x, int y, string t, int on, int off) => DrawText(p, px, x, y, t, on, off);

        // SPEED: the car's forward speed (the original's carLocalVelocityZ, its high byte) on the dial: the face back, the
        // needle eased toward it (the physics moves 8 times a second, the panel 50), an orange line from the white hub
        var speed = System.Math.Abs((short)me.Car.W(ScrCar.carLocalVelocityZ)) >> 8;
        st.Needle += (System.Math.Min(speed, DialTop) - st.Needle) * 0.3;
        const int side = DialR * 2 + 3;
        for (var y = 0; y < side; y++)
            for (var x = 0; x < side; x++) px[(DialY - DialR - 1 + y) * 320 + DialX - DialR - 1 + x] = st.Dial![y * side + x];
        var (nx, ny) = DialAt(st.Needle);
        for (var r = 0.0; r <= DialR - 3; r += 0.5) Dot((int)System.Math.Round(DialX + nx * r), (int)System.Math.Round(DialY + ny * r), Orange);
        Fill(DialX - 1, DialY - 1, DialX, DialY, White);

        // BOOST: its fuel filling the right gauge from the bottom, its B lit while boosting
        int fuel = me.Car.B(ScrCar.boostFuelLevel);
        var height = (fuel * (GaugeBottom - GaugeTop + 1) + me.MaxBoost - 1) / System.Math.Max(1, me.MaxBoost);   // full: the track's own tank
        for (var y = GaugeTop; y <= GaugeBottom; y++) Fill(BoostX0, y, BoostX1, y, GaugeBottom - y < height ? Yellow : Black);
        Small(px, "B", BoostX0 + 3, 2, me.Boosting ? [White, Yellow, Yellow, Orange, Orange] : Shade);

        // DAMAGE: the crack climbing the left gauge, a step a point of damage, wandering a pixel either way now and then, its
        // colour by the way it went (the original's 11 / 12); a restart's lower damage: the gauge clear again
        if (me.Damage < st.Damage || st.Damage < 0) { Fill(DamageX0, GaugeTop, DamageX1, GaugeBottom, Black); st.Damage = 0; st.CrackX = 3; }
        var rng = new System.Random(st.Damage * 7919 + 17);
        while (st.Damage < me.Damage)
        {
            st.Damage++;
            var y = GaugeBottom - st.Damage * (GaugeBottom - GaugeTop) / ScrDrive.WreckAt;
            var was = st.CrackX;
            var r = rng.Next(4);
            if ((st.Damage & 1) == 0 && r == 0 && st.CrackX < DamageX1 - DamageX0 - 1) st.CrackX++;
            else if ((st.Damage & 1) == 0 && r == 1 && st.CrackX > 0) st.CrackX--;
            var c = st.CrackX > was ? DarkRed : Red;
            Dot(DamageX0 + st.CrackX, y, c); Dot(DamageX0 + st.CrackX + 1, y, c);
        }

        // TIME this lap (from the lap line; the lap just done held on it a moment, as the original's lapTimeDisplayDuration),
        // BEST the best lap so far (Hunter's passes, 50 a second; shown in tenths)
        static string Clock(int hundredths) { var t = hundredths / 10; return $"{t / 600 % 10}:{t / 10 % 60:D2}.{t % 10}".PadLeft(7); }   // the original's clock
        Text(TimeX, TimeY, me.ShowLast > 0 || me.FinishedAt >= 0 ? Clock(me.LastLap) : Clock(me.LapPasses), White, Blue);   // the race done: its last lap
        Text(TimeX, BestY, me.BestLap >= 0 ? Clock(me.BestLap) : " -:--.-", White, Blue);

        // THE MAP: the track, then both cars on it
        for (var y = 0; y < MapSize; y++) for (var x = 0; x < MapSize; x++) Dot(MapX + x, MapY + y, st.Map![y, x]);
        var other = s.ScrAi.Values.Select(a => a.Drive).FirstOrDefault();
        foreach (var (drive, colour) in new[] { (other, Red), (me, Yellow) })
        {
            if (drive?.Entity is not { } e || drive.Wrecked) continue;
            var (mx, my) = MapAt(st, e.UW(0x4A), e.UW(0x4E));                     // world units, as the sections
            Fill(mx, my, mx + 1, my + 1, colour);
        }

        // THE GAP along the lap, each car's distance round it: its section's start and how far along the section it is
        var gap = 0.0; var gapKnown = false;
        if (other is { Entity: not null } && me.Entity is not null && me.Section >= 0 && other.Section >= 0 && st.Along is { } along)
        {
            double Round(ScrDrive d)
            {
                var m = st.Mids!; var i = d.Section; var n = m[(i + 1) % m.Length];
                double dx = n.X - m[i].X, dz = n.Z - m[i].Z, l = System.Math.Max(1e-6, System.Math.Sqrt(dx * dx + dz * dz));
                double ex = d.Entity!.UW(0x4A) - m[i].X, ez = d.Entity.UW(0x4E) - m[i].Z;      // the sections are in world units, as the records
                return along[i] + System.Math.Clamp((ex * dx + ez * dz) / l, -l, l);
            }
            gap = Round(me) - Round(other);
            if (gap > st.Lap / 2) gap -= st.Lap; else if (gap < -st.Lap / 2) gap += st.Lap;
            gapKnown = true;
        }
        var shown = (int)System.Math.Round(gap / 16);
        Text(GapX, GapY, gapKnown ? (shown >= 0 ? "+" : "-") + System.Math.Min(System.Math.Abs(shown), 9999).ToString("D4") : "     ", Orange, Black);
        // POS: by laps, then the finish order, then the gap; LAP: this lap of the race's (the last once it is done)
        var first = other is null || other.Lap < me.Lap
            || other.Lap == me.Lap && (me.FinishedAt >= 0 ? other.FinishedAt < 0 || me.FinishedAt <= other.FinishedAt : !gapKnown || gap >= 0);
        if (other is not null && other.Lap > me.Lap) first = false;
        Text(PosX, RaceY, first ? "1ST" : "2ND", White, Blue);
        Text(LapX, RaceY, $"{(me.Lap == 0 ? "-" : System.Math.Min(me.Lap, ScrRoad.Laps).ToString())}/{ScrRoad.Laps}", White, Blue);
    }

    /// <summary>A line of the panel font ($837A) at pixel x, y: its set bits in on, the rest in off.</summary>
    static void DrawText(Panel p, byte[] px, int x, int y, string t, int on, int off)
    {
        foreach (var ch in t)
        {
            if (ch is ':' or '.')                                                    // the font has neither: square dots, set narrow
            {
                for (var line = 0; line < 8; line++)
                    for (var b = 0; b < 4; b++)
                    {
                        var set = b is 1 or 2 && (ch == ':' ? line is 1 or 2 or 4 or 5 : line is 5 or 6);
                        if ((uint)(x + b) < 320 && (uint)(y + line) < Panel.Rows) px[(y + line) * 320 + x + b] = (byte)(set ? on : off);
                    }
                x += 4;
                continue;
            }
            for (var line = 0; line < 8; line++)
            {
                var bits = ch < p.FontGlyphs ? p.FontLine(ch, line) : 0;
                for (var b = 0; b < 8; b++)
                    if ((uint)(x + b) < 320 && (uint)(y + line) < Panel.Rows) px[(y + line) * 320 + x + b] = (byte)((bits >> (7 - b) & 1) != 0 ? on : off);
            }
            x += 8;
        }
    }

    /// <summary>The panel's small label letters (3 x 5, as its CREDITS and FUEL): a word at x, y, its rows in shades top to
    /// bottom, a dark shadow under each bottom edge.</summary>
    static readonly Dictionary<char, string[]> Letters3 = new()
    {
        ['A'] = [".#.", "#.#", "###", "#.#", "#.#"], ['B'] = ["##.", "#.#", "##.", "#.#", "##."], ['D'] = ["##.", "#.#", "#.#", "#.#", "##."],
        ['E'] = ["###", "#..", "##.", "#..", "###"], ['G'] = ["###", "#..", "#.#", "#.#", "###"], ['I'] = ["###", ".#.", ".#.", ".#.", "###"],
        ['L'] = ["#..", "#..", "#..", "#..", "###"], ['M'] = ["#.#", "###", "###", "#.#", "#.#"], ['O'] = ["###", "#.#", "#.#", "#.#", "###"],
        ['P'] = ["###", "#.#", "###", "#..", "#.."], ['S'] = ["###", "#..", "###", "..#", "###"], ['T'] = ["###", ".#.", ".#.", ".#.", ".#."],
    };
    static readonly int[] Shade = [Orange, Orange, Brown, Brown, DarkBrown];
    static void Small(byte[] px, string word, int x0, int y0, int[] shade)
    {
        for (var w = 0; w < word.Length; w++)
        {
            var g = Letters3[word[w]];
            for (var row = 0; row < 5; row++)
                for (var k = 0; k < 3; k++)
                {
                    int x = x0 + w * 4 + k, y = y0 + row;
                    if (g[row][k] == '#') { px[y * 320 + x] = (byte)shade[row]; if (row == 4 || g[row + 1][k] != '#') px[(y + 1) * 320 + x] = Edge; }
                }
        }
    }

    /// <summary>The way from the dial's middle to a speed on its scale: 0 at the lower left, round over the top to the lower right.</summary>
    static (double X, double Y) DialAt(double v)
    {
        var a = (-120 + 240 * System.Math.Clamp(v, 0, DialTop) / DialTop) * System.Math.PI / 180;   // from straight up, clockwise
        return (System.Math.Sin(a), -System.Math.Cos(a));
    }

    static (int X, int Y) MapAt(Shown st, double gx, double gz)
    {
        var x = (int)System.Math.Round((gx - st.Fit.X) * st.Fit.S);
        var y = (int)System.Math.Round((st.Fit.Z - gz) * st.Fit.S);                  // +z up (Hunter's radar: RadarDot)
        return (MapX + System.Math.Clamp(x, 0, MapSize - 2), MapY + System.Math.Clamp(y, 0, MapSize - 2));
    }

    /// <summary>The console drawn once: the plates, their bolts, wells, displays and labels, the dial's face, the track.</summary>
    static void Paint(WorldState s, Panel p, Shown st)
    {
        var px = p.Pixels.Pixels;
        void Dot(int x, int y, int c) { if ((uint)x < 320 && (uint)y < Panel.Rows) px[y * 320 + x] = (byte)c; }
        void Fill(int x0, int y0, int x1, int y1, int c) { for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) Dot(x, y, c); }
        // a well sunk into the plate: dark top and left edge, light bottom and right, its face inside
        void Well(int x0, int y0, int x1, int y1, int face)
        {
            Fill(x0, y0, x1, y1, face);
            for (var x = x0 - 1; x <= x1 + 1; x++) { Dot(x, y0 - 1, Edge); Dot(x, y1 + 1, Grey); }
            for (var y = y0 - 1; y <= y1 + 1; y++) { Dot(x0 - 1, y, Edge); Dot(x1 + 1, y, Grey); }
            Dot(x1 + 1, y0 - 1, Dim); Dot(x0 - 1, y1 + 1, Dim);
        }
        // the panel's bolt (its picture's, 5 x 5: a domed head lit from the top left)
        string[] bolt = [".fed.", "fdc3d", "fdccd", "fedd.", ".fff."];
        void Bolt(int x0, int y0)
        {
            for (var y = 0; y < 5; y++)
                for (var x = 0; x < 5; x++)
                    if (bolt[y][x] != '.') Dot(x0 + x, y0 + y, System.Convert.ToInt32(bolt[y][x].ToString(), 16));
        }
        // THE PLATES (rows 0-36; rows 37-59, the groove and the message strip, are the panel's own): the groove behind them,
        // each plate its face with a fine grain of the darker and lighter plate colours, a light top edge and a dark bottom one
        Fill(0, 0, 292, 39, Groove);                                                // the old panel's wells and slots gone to the QUIT column
        Fill(293, 26, 305, 38, Plate);                                              // ...and FUEL's label beside its disk
        foreach (var (x0, x1) in Plates)
        {
            for (var y = 1; y <= 35; y++)
                for (var x = x0; x <= x1; x++)
                {
                    var h = (x * 73856093 ^ y * 19349663) & 63;
                    Dot(x, y, h == 0 ? Dim : h == 1 ? Groove : Plate);
                }
            Fill(x0, 0, x1, 0, Grey); Fill(x0, 36, x1, 36, Edge);
            Fill(x0, 1, x0, 35, Grey); Fill(x1, 1, x1, 35, Edge);
        }
        foreach (var (x0, x1) in Plates) { Bolt(x0 + 2, 2); Bolt(x1 - 6, 2); Bolt(x0 + 2, 30); Bolt(x1 - 6, 30); }
        // MAP: its well, framed
        Well(MapX, MapY, MapX + MapSize - 1, MapY + MapSize - 1, Black);
        // TIMING: TIME and BEST, labelled, in blue displays
        Small(px, "TIME", TimeX, TimeY - 7, Shade); Well(TimeX - 1, TimeY - 1, TimeX + 56, TimeY + 8, Blue);
        Small(px, "BEST", TimeX, BestY - 7, Shade); Well(TimeX - 1, BestY - 1, TimeX + 56, BestY + 8, Blue);
        // DRIVING: the gauges (their letters over them) and the dial's face - black, a grey rim, ticks every 20 from 0 to 160
        // round 240 degrees, red from 120: the speed the throttle stops pushing at (processPlayerInput: under $78), above it
        // the boost - kept for the needle to be drawn over each pass
        Well(DamageX0, GaugeTop, DamageX1, GaugeBottom, Black); Small(px, "D", DamageX0 + 3, 2, Shade);
        Well(BoostX0, GaugeTop, BoostX1, GaugeBottom, Black);
        for (var y = DialY - DialR - 2; y <= DialY + DialR + 2; y++)
            for (var x = DialX - DialR - 2; x <= DialX + DialR + 2; x++)
            {
                double d = System.Math.Sqrt((x - DialX) * (x - DialX) + (y - DialY) * (y - DialY));
                if (d <= DialR - 0.5) Dot(x, y, Black);
                else if (d <= DialR + 0.6) Dot(x, y, Grey);
                else if (d <= DialR + 1.6) Dot(x, y, x + y < DialX + DialY ? Edge : Dim);   // the bezel's lip: dark above, lighter below
            }
        for (var v = 0; v <= DialTop; v += 20)
        {
            var (ax, ay) = DialAt(v);
            for (var r = DialR - 4.0; r <= DialR - 1.5; r += 0.5) Dot((int)System.Math.Round(DialX + ax * r), (int)System.Math.Round(DialY + ay * r), v >= 120 ? Red : Blue);
        }
        for (var v = 120.0; v <= DialTop; v += 2)
        {
            var (ax, ay) = DialAt(v);
            Dot((int)System.Math.Round(DialX + ax * (DialR - 1.5)), (int)System.Math.Round(DialY + ay * (DialR - 1.5)), Red);
        }
        st.Dial = new byte[(DialR * 2 + 3) * (DialR * 2 + 3)];
        for (var y = 0; y < DialR * 2 + 3; y++)
            for (var x = 0; x < DialR * 2 + 3; x++) st.Dial[y * (DialR * 2 + 3) + x] = px[(DialY - DialR - 1 + y) * 320 + DialX - DialR - 1 + x];
        // RACE: POS and LAP in blue displays, the GAP in a black one
        Small(px, "POS", PosX, RaceY - 7, Shade); Well(PosX - 1, RaceY - 1, PosX + 24, RaceY + 8, Blue);
        Small(px, "LAP", LapX, RaceY - 7, Shade); Well(LapX - 1, RaceY - 1, LapX + 24, RaceY + 8, Blue);
        Small(px, "GAP", GapX, GapY - 7, Shade); Well(GapX - 1, GapY - 1, GapX + 40, GapY + 8, Black);
        // QUIT's column (the panel's own): TALK and GIVE cleared
        Fill(294, 1, 318, 16, Plate);
        // THE TRACK: each section's middle, fitted into the map (the start line white); and its length round the lap
        var road = s.Ground.Road!.Road;
        var mids = road.Sections.Select(c => ((c.LX + c.RX) / 2, (c.LZ + c.RZ) / 2)).ToArray();
        double minX = mids.Min(m => m.Item1), maxX = mids.Max(m => m.Item1), minZ = mids.Min(m => m.Item2), maxZ = mids.Max(m => m.Item2);
        var span = MapSize - 2;
        var sc = span / System.Math.Max(1e-6, System.Math.Max(maxX - minX, maxZ - minZ));
        st.Fit = (minX - (span / sc - (maxX - minX)) / 2, maxZ + (span / sc - (maxZ - minZ)) / 2, sc);
        st.Map = new int[MapSize, MapSize];
        for (var i = 0; i < mids.Length; i++)
        {
            var a = mids[i]; var b = mids[(i + 1) % mids.Length];
            for (var t = 0.0; t <= 1; t += 0.25)
            {
                var (x, y) = MapAt(st, a.Item1 + (b.Item1 - a.Item1) * t, a.Item2 + (b.Item2 - a.Item2) * t);
                st.Map[y - MapY, x - MapX] = i == road.StartLineSection ? White : Grey;
            }
        }
        st.Mids = mids;
        st.Along = new double[mids.Length];
        for (var i = 1; i < mids.Length; i++)
            st.Along[i] = st.Along[i - 1] + System.Math.Sqrt(System.Math.Pow(mids[i].Item1 - mids[i - 1].Item1, 2) + System.Math.Pow(mids[i].Item2 - mids[i - 1].Item2, 2));
        st.Lap = st.Along[^1] + System.Math.Sqrt(System.Math.Pow(mids[0].Item1 - mids[^1].Item1, 2) + System.Math.Pow(mids[0].Item2 - mids[^1].Item2, 2));
    }
}
