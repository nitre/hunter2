namespace Hunter.Rules;

/// <summary>objfile as the program holds it: the 64 KB buffer $83CA and the offset table $49F0.
/// THE EXTENDED MODEL ("OW"): the card
/// builds models past the file's format (more than 255 corners or edges, faces of more than 15 edges, corners off the scale's
/// steps - godot/real3d/CardModels.cs ToObjfile). So the checks can still read them through the rules, a model may start "OW":
///   +$00 "OW", +$02 size word, +$04..+$0F as an "OB" model, +$10 set, +$11 shift, +$12 corner count word,
///   +$14 corners, 8 bytes each: flag byte, 0, x, y, z words (coordinates before the shift, like the bytes they stand for),
///   then the edge count word and edges as two corner-number words,
///   then the face count word and faces: colour byte, flags byte (its high nibble; the low nibble 0), edge count word, that many
///   edge-number words; a negative colour byte ends the list.
/// The game's own file never holds one; the processor's picture (Faces, the raster) refuses one.</summary>
public sealed class Models
{
    public readonly byte[] Buf;
    public readonly List<int> Offsets = new();

    public Models(byte[] objfileBuffer)
    {
        Buf = objfileBuffer;
        for (var off = 0; Buf[off] == 'O' && (Buf[off + 1] == 'B' || Buf[off + 1] == 'W'); off += U(off + 2))   // $49C4 (and "OW": the card's extended model)
            Offsets.Add(off);
    }

    /// <summary>Whether the model at this offset is an extended ("OW") one.</summary>
    public bool Wide(int offset) => Buf[offset + 1] == 'W';

    public int U(int o) => Buf[o] << 8 | Buf[o + 1];
    public short S(int o) => (short)U(o);
}

/// <summary>sinfile: angle byte * 4 indexes a long (exponent word, mantissa word); cos 1024 bytes on.</summary>
public sealed class Trig
{
    readonly byte[] _t;
    public Trig(byte[] sinfile) => _t = sinfile;
    public (int E, int M) Sin(int byteOffset) => Pair(byteOffset);
    public (int E, int M) Cos(int byteOffset) => Pair(0x400 + byteOffset);
    (int, int) Pair(int o) => (_t[o] << 8 | _t[o + 1], (short)(_t[o + 2] << 8 | _t[o + 3]));
}

/// <summary>
/// Objects in the 3D view: depth sort into the 7 row lists ($4164/$41CC), and per entity the
/// model draw ($4EA2): lookup $4F54, rotation and animation $566E, vertices $531E, projection
/// $51F8 (or the ground shadow $52A2), edges $76FC, faces $77AA/$77C4, lines $7BF2.
/// </summary>
public sealed class Objects
{
    readonly Models _models;
    readonly Trig _trig;
    readonly Ground _ground0;
    /// <summary>The ground: the running game's own, else the one given.</summary>
    Ground _ground => World?.Ground ?? _ground0;
    readonly Raster _raster;
    /// <summary>$577E: 16 animation sets x 20 bytes (4 control bytes, then 4 x value/step/min/max).
    /// Lives inside the program and persists across frames; $5762 resets it at game start.</summary>
    public readonly byte[] Anim;
    uint _ticked;                 // $5618: sets already stepped this frame
    readonly byte[] _artic = new byte[4];   // $561C: articulation angles of the current model
    public ReadOnlySpan<byte> Artic => _artic;
    public void HoldAnimation() => _ticked = uint.MaxValue;   // every set counts as stepped this frame

    public Objects(Models models, Trig trig, Ground ground, Raster raster, byte[] anim)
    {
        _models = models;
        _trig = trig;
        _ground0 = ground;
        _raster = raster;
        Anim = anim;
    }

    /// <summary>The program's initial animation table ($5762 copies $577E + $140 over $577E).</summary>
    public static byte[] InitialAnim(byte[] main) => main.AsSpan(0x577E + 0x140 - 0x800, 320).ToArray();

    // ---------------------------------------------------------------- $4164: sort into rows

    List<int>[] _rows = MakeRows(7);

    /// <summary>The bucket rows, far (0) first. (Play.FarView):
    /// 11 rows with far view on, so entities out to $2800 still land in one - the original's
    /// bound $1800 gives 7 ($4216's cmp: 1 + (bound + $3FE) >> 10). Setting it rebuilds the
    /// lists, so a flip never leaves entities queued under the other depth.</summary>
    public List<int>[] Rows => _rows;

    static List<int>[] MakeRows(int n) => Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();

    bool _farView;
    /// <summary>(Play.FarView): the in-view box grows with the ground -
    /// depth $1800 -> $1800 + Ground.AddRows x $400 ($4216's cmp.w #0x1800 at Place), the bucket rows
    /// to Ground's 7 + AddRows, and the side bound +-$800 by $400 per added column each side, so nothing
    /// pops at the sides of the wide ground. Off (the default), every constant is the original's literal.</summary>
    public bool FarView
    {
        get => _farView;
        set { _farView = value; _rows = MakeRows(value ? 7 + Ground.AddRows : 7); }
    }
    static int FarDepth => 0x1800 + Ground.AddRows * 0x400;
    static int FarSide => 0x800 + (Ground.WideCols - 7) / 2 * 0x400;   // the columns far view adds on each side

    bool _moreObjects;
    /// <summary>(Play.MoreObjects): the live window around the
    /// camera's cells grows 2 x 2 -> 4 x 4 (Window) and the table grows 30 -> 395 live records
    /// (EntityTable.Count, the last slot of each still reserved). Off (the default), every
    /// constant is the original's literal.</summary>
    public bool MoreObjects { get => _moreObjects; set => _moreObjects = value; }

    /// <summary>The live window, as cell deltas from the camera's (G1540, G1542) - the ONE
    /// definition the spawner ($8186 SpawnNearCamera) and the write-back test ($826E
    /// WriteBackIfFar) both use, so a thing is never spawned and written back in the same pass.
    /// The original: the 2 x 2 cells at and past the camera cell ($8186's two moveq 0x1 loops;
    /// $828C's subq/bgt: dx, dz of 0-1 stay live). MoreObjects: one cell more each side and two
    /// more ahead of the camera's z cell ($1542 - the view looks towards +z), 4 x 4.</summary>
    public static (int Dx0, int Dx1, int Dz0, int Dz1) Window(bool moreObjects) =>
        moreObjects ? (-1, 2, 0, 3) : (0, 1, 0, 1);

    /// <summary>The camera cell pair (G1540, G1542) a Place ($374C) at (x, z) would give - the
    /// same instructions: d0 = 2x + $1000, d1 = 2z -/+ distance (clamped at the map edge, angle
    /// $80 looks south), then (short)(d >> 10) >> 3, -1 on x only. mp_near uses it for player 2's
    /// own camera, which the running game never stores.</summary>
    public static (int CellX, int CellZ) CameraCells(ushort x, ushort z, int distance, byte angle)
    {
        int d0 = x * 2 + 0x1000;
        int d1 = z * 2 + (angle == 0 ? -distance : distance);
        if (angle == 0) { if (d1 < 0) d1 = 0; }
        else if (d1 > 0x1FFFE) d1 = 0x1FFFE;
        return (((short)(d0 >> 10) >> 3) - 1, (short)(d1 >> 10) >> 3);
    }

    /// <summary>animate = false holds every animation set this pass (smooth play: see Play.AnimEveryBlanks).</summary>
    public void Sort(EntityTable t, Camera c, bool animate = true)
    {
        _ticked = animate ? 0 : uint.MaxValue;              // clr.l $5618 (all sets "already stepped" = held)
        _stepDebris = animate;
        var half = c.G1544 >> 1;                            // asr.l #1
        var order = _order;                                 // $4DEC: by camera z, far first
        order.Clear();
        for (var i = 0; i < t.Count; i++)
        {
            var e = t.All[i];
            if (!e.Alive) continue;
            var z = (short)(e.W(0x4E) - half);
            var at = 0;
            while (at < order.Count && z < order[at].Z) at++;   // blt: equal z goes in front
            order.Insert(at, (i, z));
        }
        _orderIdx.Clear();
        foreach (var o in order) _orderIdx.Add(o.Index);
        Collisions.Check(t, _orderIdx, World);                                   // $41B2 -> $4408
        foreach (var (i, _) in order) Place(t.All[i], i, c);
    }

    readonly List<(int Index, short Z)> _order = [];        // Sort's and PlaceForPicture's lists, made once
    readonly List<int> _orderIdx = [];

    /// <summary>ENHANCEMENT (Play.Smooth, not the original): Sort for a picture only - the same order and
    /// placing, but no collision check, no write-back of far objects, every animation held. The caller
    /// restores the records afterwards.</summary>
    public void PlaceForPicture(EntityTable t, Camera c)
    {
        _ticked = uint.MaxValue;
        _stepDebris = false;                                // a picture redraws the shower, never steps it
        // ENHANCEMENT (mp_view_finish, south.md A3): the picture's order is by TURNED depth - far
        // first. At 0 that is world z descending, the original's compare; at $80 the camera faces
        // south, so far is small world z and the compare flips. Rows is the order DrawRow walks,
        // and no Collisions.Check runs here - player 1's sort+collision order is untouched.
        var mirror = c.Angle != 0;
        var half = c.G1544 >> 1;
        var order = _order;
        order.Clear();
        for (var i = 0; i < t.Count; i++)
        {
            var e = t.All[i];
            if (!e.Alive) continue;
            var z = (short)(e.W(0x4E) - half);
            var at = 0;
            while (at < order.Count && (mirror ? z > order[at].Z : z < order[at].Z)) at++;
            order.Insert(at, (i, z));
        }
        foreach (var (i, _) in order) Place(t.All[i], i, c, writeBack: false);
    }

    /// <summary>Place's in-view box ($420C-$4216): x within +-side of the camera and the depth past the thing's own
    /// near edge (cz - its +$72) from 0 to depth, in camera units. The card's view distance grows the bounds
    /// (ViewDistance.InView); the game's own are $800 and $1800.</summary>
    public static bool SeenBox(int cx, int d2, int side, int depth) => cx <= side && cx >= -side && d2 >= 0 && d2 <= depth;

    // $41CC (bounds $42E6 and the far write-back $826E: Buildings.KeepInside / WriteBackIfFar)
    void Place(Entity e, int index, Camera c, bool writeBack = true)
    {
        if (c.Angle != _turn.Item1) _turn = (c.Angle, _trig.Sin(c.Angle * 4), _trig.Cos(c.Angle * 4));
        int x = e.UW(0x4A), y = e.W(0x4C), z = e.UW(0x4E);
        if (e.B[0x20] != 0 && World is { } w) (x, y, z) = Buildings.KeepInside(w, e, x, y, z);   // $41D6
        e.SetW(0x50, x); e.SetW(0x52, y); e.SetW(0x54, z);
        // $41FC: cx = 2x - $1548, cz = 2z - $1544. At a half turn ($80, mp_view_angle) both
        // deltas negate exactly (south.md A2): cx' = $1548 - 2x, cz' = $1544 - 2z - no new
        // maths; the in-view box (abs) and the buckets on +$154C follow unchanged (A8/A9).
        var turned = c.Angle != 0;
        var cx = (short)(turned ? c.G1548 - x * 2 : x * 2 - c.G1548);
        var cz = (short)(turned ? c.G1544 - z * 2 : z * 2 - c.G1544);
        e.SetW(0x56, cx); e.SetW(0x58, y); e.SetW(0x5A, cz);
        var d2 = (short)(cz - e.W(0x72));
        var seen = SeenBox(cx, d2, FarView ? FarSide : 0x800, FarView ? FarDepth : 0x1800);   // $4216 cmp.w #0x1800
        e.B[0x1C] = (byte)(seen ? 0xFF : 0);
        if (!seen)
        {
            if (writeBack && e.W(2) != 0 && World is { } s) WriteBackIfFar(s, e, _moreObjects);   // $4224 -> $826E
            return;
        }
        var row = (short)(d2 + c.G154C);
        row = (short)((Rows.Length - 1) - (int)((uint)(int)row >> 10));   // ext.l, lsr.l #8, lsr.l #2, neg, +6 ($4230-$4244)
        if (row < 0 || row >= Rows.Length) return;
        Rows[row].Add(index);
        if (e.B[0x1E] != 0 && !e.Flag(0x17)) Tilt(e);
    }

    /// <summary>The running game, for writing far objects back into the world grid (null in the
    /// renderer-only tests).</summary>
    public WorldState? World;

    // $826E: outside the live window's cells the spawner keeps ($1540/$1542, Window) -> WriteBack ($8298)
    // mp_near: with 2 players "far" is far from BOTH - a thing inside player 2's own window cells
    // (his camera is never stored, so its cells come from CameraCells on his position) stays live
    // too. One player runs the original's single test, byte for byte.
    static void WriteBackIfFar(WorldState s, Entity e, bool moreObjects)
    {
        if (!InWindow(s, e, moreObjects)) WriteBack(s, e, e.Flags);
    }

    /// <summary>$826E's test: the thing's cell inside the live window of a camera (player 2's too).</summary>
    public static bool InWindow(WorldState s, Entity e, bool moreObjects)
    {
        var (dx0, dx1, dz0, dz1) = Window(moreObjects);
        int ex = e.UW(0x4A), ez = e.UW(0x4E);
        int dx = (ex >> 12) - s.Camera.G1540, dz = (ez >> 12) - s.Camera.G1542;
        if (dx >= dx0 && dx <= dx1 && dz >= dz0 && dz <= dz1) return true;
        if (s.Players > 1)
        {
            var p2 = s.Entities.All[1];
            var (cx2, cz2) = CameraCells(p2.UW(0x4A), p2.UW(0x4E), s.Distance, s.ViewAngle2);
            dx = (ex >> 12) - cx2; dz = (ez >> 12) - cz2;
            if (dx >= dx0 && dx <= dx1 && dz >= dz0 && dz <= dz1) return true;
        }
        return false;
    }

    /// <summary>WriteBack ($8298): the record back into a free record of its grid cell, its slot
    /// freed; d7 bit 2 stops its sound channel (d7 = the record's +$06 from $826E, WriteBackAllIfOnFoot's
    /// loop counter from $33FE). +$20 (the player's building): nothing.
    /// (Play.RoomyMemory): on, the fuel's low byte (+$35) is kept
    /// beside the record ($82E6 stores only the high byte at +$0D; $8246 restores it << 8), and a
    /// full cell ($830E leaves the thing live for ever) takes the record in an overflow list beside
    /// the grid - the thing is freed and spawns again when its cell comes into play.</summary>
    public static void WriteBack(WorldState s, Entity e, int d7)
    {
        if (e.B[0x20] != 0) return;                                          // $8298: the player's building
        var pn = s.PlayerOf(e);                                              // mp_life: a player's own slot never goes
        if (s.Players > 1 && pn >= 0 && pn < s.Players) return;              // back to the grid - he stands far from the
                                                                             // OTHER player's camera (one player: his
                                                                             // camera is always on him, byte for byte)
        if ((d7 >> 2 & 1) != 0) s.Channels.Set(1, e.W(0x98), 0);             // $82A8 StopChannel
        int x = e.UW(0x4A), z = e.UW(0x4E);
        var heading = e.B[0x7E];
        byte[] grid = s.Grid; var fuelLow = s.FuelLow; var overflow = s.Overflow;
        var cell = ((z >> 1) & 0x7800) + ((x >> 5) & 0x780);                 // $82BA
        void Store(byte[] g, int r)
        {
            void W(int o, int v) { g[r + o] = (byte)(v >> 8); g[r + o + 1] = (byte)v; }
            W(6, x); W(8, e.W(0x4C)); W(10, z);                              // $82D0
            W(0, e.W(2));
            g[r + 2] = e.B[1];
            g[r + 0x0C] = heading;
            g[r + 0x0D] = e.B[0x34];
            g[r + 0x0E] = e.B[0x33];
            W(4, e.W(0x2A));
            g[r + 3] = e.B[0x3F];
        }
        for (var k = 0; k < 8; k++)
        {
            var r = cell + k * 16;
            if ((grid[r] | grid[r + 1]) != 0) continue;
            Store(grid, r);
            if (s.RoomyMemory) fuelLow[r] = e.B[0x35];                       // the enhancement: the fuel low byte
            e.B[0] = 0;                                                      // $8300 sf (a4)
            return;
        }
        if (!s.RoomyMemory) return;                                          // $830E: moveq -1, the thing stays live
        if (!overflow.TryGetValue(cell, out var list)) overflow[cell] = list = [];
        var rec = new byte[17];
        Store(rec, 0);                                                       // the same 16 bytes a slot would take,
        rec[0x10] = e.B[0x35];                                               // plus the fuel low byte the slot record cannot hold
        list.Add(rec);
        e.B[0] = 0;
    }

    // $42A6: pitch from the ground one step ahead in the heading's direction
    static readonly short[] AheadX = { 0, 15, 30, 30, 30, 30, 30, 15, 0, -15, -30, -30, -30, -30, -30, -15 };
    static readonly short[] AheadZ = { 30, 30, 30, 15, 0, -15, -30, -30, -30, -30, -30, -15, 0, 15, 30, 30 };

    void Tilt(Entity e)
    {
        if (World is { FineTilt: true }) { FineTilt(e); return; }
        var k = (e.B[0x7E] >> 3 & 0x1E) / 2;
        var h = _ground.Height((ushort)(e.W(0x4A) + AheadX[k]), (ushort)(e.W(0x4E) + AheadZ[k]));
        e.B[0x7C] = TiltAngle(e.W(0x4C), h, _ground.Fine != null);
    }

    /// <summary>$42CC move.b: the pitch byte = the height here less the height one step ahead, cut to a byte - an
    /// angle, 256 a turn (Rotation, Movement.Vector and StartState.Ahead read it x 4 into the 1024 sine table; Smooth,
    /// Blank's hover and Behaviour's climb read it as a signed byte). (FINER GROUND, fine
    /// on): taller and finer ground makes the difference pass 127 and wrap (the thing tips over backwards, arcs and
    /// climbs bend the wrong way, docs/data/byte-limits.md row 2), so it is held to +-63 instead: under a quarter turn
    /// (64 = upright), so no two tilts are half a turn apart (Smooth's sbyte difference would go the long way). No user
    /// needs a wider number - every one reads an angle - so the byte stays the only store (+$7D is not free: it is the
    /// pitch word's low byte for flying things, Movement $2A16/$2A24/$2A8A, StartState's spawns). Off: the original's cut.</summary>
    public static byte TiltAngle(int y, int h, bool fine) => (byte)(fine ? Math.Clamp(y - h, -63, 63) : y - h);

    /// <summary>(FINE ANGLES, WorldState.FineTilt): $42A6's pitch from
    /// the exact heading, not one of 16. The original looks ahead to a point of a square (AheadX / AheadZ: 30 along the
    /// axes, 30 + 30 on the diagonals) picked by the heading's top 4 bits, at a whole height: a turning thing's pitch jumped
    /// at each of the 16 headings. Here the point slides round the same square with the fine heading (Entity.HeadingFine,
    /// else the byte), the height there is blended between the whole points about it, and the pitch - the same "height
    /// here less the height ahead", in steps - keeps its fraction: Entity.PitchFine, the byte its rounding (held to +-63
    /// on finer ground, as TiltAngle). Play changes a little - the pitch also aims a thrown thing ($2C00 Vector).</summary>
    void FineTilt(Entity e)
    {
        var p = FinePitch(_ground, e.W(0x4A), e.W(0x4C), e.W(0x4E), (ushort)((e.B[0x7E] << 8) + Entity.Frac(e.HeadingFine, e.B[0x7E])));
        var whole = (int)Math.Floor(p + 0.5);
        e.B[0x7C] = (byte)whole;
        e.PitchFine = (ushort)((whole << 8) + (int)Math.Round((p - whole) * 256));
    }

    /// <summary>FineTilt's pitch in steps (256 a turn) at (x, y, z) facing heading (65536 a turn): the height here less the
    /// blended height at the point of $42A6's square the heading points to.</summary>
    public static double FinePitch(Ground g, int x, int y, int z, ushort heading)
    {
        var a = heading / 65536.0 * 2 * Math.PI;
        double sx = Math.Sin(a), cz = Math.Cos(a), reach = 30 / Math.Max(Math.Abs(sx), Math.Abs(cz));   // on the square
        double ax = x + sx * reach, az = z + cz * reach;
        int x0 = (int)Math.Floor(ax), z0 = (int)Math.Floor(az);
        double fx = ax - x0, fz = az - z0;
        double H(int px, int pz) => g.Height((ushort)px, (ushort)pz);
        var h = (H(x0, z0) * (1 - fx) + H(x0 + 1, z0) * fx) * (1 - fz) + (H(x0, z0 + 1) * (1 - fx) + H(x0 + 1, z0 + 1) * fx) * fz;
        var p = y - h;
        return g.Fine != null ? Math.Clamp(p, -63, 63) : p;
    }

    // ---------------------------------------------------------------- $4EA2: one entity

    /// <summary>every object casts a half-bright (EHB) shadow from
    /// its own main model, where the original squashes a flat model onto the ground for its 13
    /// shadow types only (faces of colour 0, $4EC4-$4EF6). On, each drawn object first draws its
    /// shadow - with the sun up, every one (the 13 too) its main model (+$25) through Rotation /
    /// Vertices / ProjectModelFlat ($52A2) with the sun's offset; at night and inside, the 13 their own
    /// flat model straight down, as the original. The sun's offset (SunOffset: away from the clock's sun, none at night or inside) - every face
    /// of it a View.MarkSpan EHB mark, just before the main model as the original orders it. No
    /// entity byte is written (+$24 stays 0). Off (the default), not one instruction of the
    /// original's path changes.</summary>
    public bool OwnShadows;

    /// <summary>The sun from the game clock ($1B2 hour, $1B0 minute; SunPath - carried on to SunLowest, ~05:15-20:50,
    /// so shadows last until sunset; before, up 06:00-20:00): elevation
    /// 10 degrees at 06:00/20:00 to 60 at 13:00 (a sine over the 14 daylight hours; the light
    /// table $C4A4 peaks 10:00-15:00, highest at 13:00). The azimuth sweeps east -> south ->
    /// west over those hours: 0 = due east, pi = due west, due south at 13:00. Axes from the
    /// game's own screens: map dot "0x70 + (px >> 6 & 7), 0x40 + (~(pz >> 6) & 7)" (Screens.DrawMap
    /// $3E12) and radar "bit (x + $1000 - its x) >> 8 of row (z + $1000 - its z) >> 8" ($A510):
    /// world x runs RIGHT on both, world z runs UP, so +x is east and +z is north, and the 3D
    /// view (camera far = +z, $374C/$4164) looks north with east to the right. Returns the
    /// direction the SHADOW falls (away from the sun: west in the morning, north at 13:00, east
    /// in the evening) and tan(elevation); null at night - no own shadows. A flare changes
    /// nothing here (InsideView is checked at the draw). Up 06:00-20:00, elevation 10 + 50 sin(pi t/840):
    /// 07:00 / 19:00 21 deg (shadow 2.6x the height), 08:00 / 18:00 32 (1.6x), 09:00 / 17:00 41 (1.1x),
    /// 13:00 60 (0.6x) - symmetric about 13:00, not noon.</summary>
    public static (double DirX, double DirZ, double TanElev)? SunOffset(WorldState? s)
    {
        if (s == null) return null;
        var minutes = (s.Hour - 6) * 60 + s.Minute;         // 0 at 06:00, 840 at 20:00
        if (_sunAt is { } at && at.Minutes == minutes) return at.Sun;   // worked out once a game minute, not once an object
        (double, double, double)? sun = null;
        var (elevDeg, az) = SunPath(s.Hour, s.Minute);
        if (elevDeg >= SunLowest)
        {
            var elev = elevDeg * Math.PI / 180;
            sun = (-Math.Cos(az), Math.Sin(az), Math.Tan(elev));
        }
        _sunAt = new SunAt(minutes, sun);                   // one object, swapped whole: safe if two worlds run at once
        return sun;
    }
    /// <summary>The sun's lowest height in degrees that still lights and throws shadows: under it the light is too weak and a shadow would run 30 heights and more.</summary>
    public const double SunLowest = 2.0;

    /// <summary>The sun's height in degrees and its direction (0 due east, pi due west, as SunOffset) at any time: by day
    /// 10 + 50 sin(pi t), t 0 at 06:00 .. 1 at 20:00, east -> south -> west; carried on through the night down to its lowest at
    /// 01:00, symmetric about it as the day is about 13:00, and on round past north back to the east - sunset ~20:58,
    /// sunrise ~05:03 (docs/sky-reference.md). One curve for the shadows, the shading and SKY's time of day.</summary>
    public static (double ElevDeg, double Az) SunPath(int hour, int minute)
    {
        var m = ((hour * 60 + minute) % 1440 + 1440) % 1440;
        if (m >= 360 && m <= 1200) { var t = (m - 360) / 840.0; return (10 + 50 * Math.Sin(Math.PI * t), Math.PI * t); }
        var x = (m - 1200 + 1440) % 1440;                                  // minutes since 20:00, 0..600
        return (10 - 50 * Math.Sin(Math.PI * Math.Min(x, 600 - x) / 840.0), Math.PI + Math.PI * x / 600.0);
    }

    sealed record SunAt(int Minutes, (double DirX, double DirZ, double TanElev)? Sun);
    static SunAt? _sunAt;

    /// <summary>$99(a5) bit 0: inside view (only entities with +$20 set are drawn).</summary>
    public bool InsideView => World?.InsideView ?? false;

    /// <summary>The building's own model as InteriorView ($48F8-$491E) draws it into the backdrop:
    /// the main model (+$25), no filter on +$1C / +$20.</summary>
    public void DrawBuildingModel(View v, Camera c, Entity e)
    {
        var main = Lookup(e, e.B[0x25]);
        Rotation(e, main);
        Vertices(e, main);
        Specials(e, main);
        Project(e, c, main);
        Faces(v, main);
    }

    /// <summary>The card's check of the thrown shadow (godot/real3d/CheckSun.cs): a record's main model squashed flat
    /// and thrown away from the sun as Draw's own-shadows branch does (ProjectModelFlat with the sun), placed like DrawStored
    /// so the sink gets world spots. The sink's faces carry the corners; nothing else is drawn.</summary>
    public void DrawStoredSunShadow(Camera c, Entity e, (double DirX, double DirZ, double TanElev) sun)
    {
        if (Sink3D == null || e.B[0x25] == 0) return;
        _ticked = uint.MaxValue;
        Sink3D.Begin(c);
        c = new Camera { G1548 = e.UW(0x4A) * 2, G1544 = e.UW(0x4E) * 2, Height0E = c.Height0E, CentreX = c.CentreX };
        e.SetW(0x56, 0); e.SetW(0x58, e.W(0x4C)); e.SetW(0x5A, 0);
        var m = Lookup(e, e.B[0x25]);
        Rotation(e, m);
        Vertices(e, m);
        ProjectModelFlat(e, c, m, sun);
        StoredFaces(m);
    }

    /// <summary>The card's checks (godot/real3d/CheckPlace.cs, CheckPieces.cs): one stored map object
    /// (StartState.Appearance) into Sink3D only - placed for this camera as Place ($41FC) does, its model's corners
    /// as the draw makes them, its faces handed over without the flat raster, animations held. Call it on an
    /// Objects of its own: it uses this one's scratch.</summary>
    public void DrawStored(Camera c, Entity e)
    {
        if (Sink3D == null) return;
        _ticked = uint.MaxValue;
        Sink3D.Begin(c);
        // the camera's doubled offsets would wrap a word across the map: the model is placed at 0 and a
        // camera standing on the object itself carries its position into the world corners (ToWorld)
        c = new Camera { G1548 = e.UW(0x4A) * 2, G1544 = e.UW(0x4E) * 2, Height0E = c.Height0E, CentreX = c.CentreX };
        e.SetW(0x56, 0); e.SetW(0x58, e.W(0x4C)); e.SetW(0x5A, 0);
        if ((sbyte)e.B[0x24] > 0)
        {
            var m = Lookup(e, (byte)(e.B[0x24] + e.B[0x25]));
            Rotation(e, m);
            Vertices(e, m);
            ProjectModelFlat(e, c, m);
            StoredFaces(m);
        }
        if (e.B[0x25] != 0)
        {
            var m = Lookup(e, e.B[0x25]);
            Rotation(e, m);
            Vertices(e, m);
            Project(e, c, m);
            StoredFaces(m);
        }
    }

    void StoredFaces(Model m)
    {
        var b = m.Buf;
        var a0 = m.FacesAt;
        while ((sbyte)b[a0] >= 0)
        {
            int flags = b[a0 + 1];
            var n = m.Wide ? b[a0 + 2] << 8 | b[a0 + 3] : flags & 0x0F;
            var kind = flags & 0x70;
            var at = a0 + (m.Wide ? 4 : 2);
            Span<int> list = n <= 64 ? stackalloc int[n] : new int[n];
            for (var i = 0; i < n; i++) list[i] = m.Wide ? b[at + i * 2] << 8 | b[at + i * 2 + 1] : b[at + i];
            Sink3D!.Face(m, list, b[a0] & 0x0F, kind != 0 && kind != 0x40, (flags & 0x80) != 0, false);
            a0 = at + n * (m.Wide ? 2 : 1);
        }
    }

    public void DrawRow(View v, Camera c, EntityTable t, int row)
    {
        Sink3D?.Begin(c);
        foreach (var i in Rows[row]) Draw(v, c, t.All[i], i);
        Rows[row].Clear();                                  // $4E88: clr.w (a1)+
    }

    /// <summary>(split-draw, docs\split-draw.md): the row's game work
    /// alone, for a pass that paints nothing (Play.PaintView off - the card-built world draws the
    /// view). The same walk as DrawRow, in the same order, making every state write Draw makes:
    /// Lookup's +$9C, the animation step ($566E), the gun ports +$62/+$68 and the ground lift +$7A
    /// off the vertices, the debris stepping, spin and kill ($5AE0, $4F2E) - and the bucket drains
    /// ($4E88). No view, no sink, no pixels.</summary>
    public void StepRow(EntityTable t, int row)
    {
        foreach (var i in Rows[row]) Step(t.All[i]);
        Rows[row].Clear();                                  // $4E88: clr.w (a1)+
    }

    void Draw(View v, Camera c, Entity e, int slot)
    {
        if (e.B[0x1C] == 0) return;
        if (InsideView ? e.B[0x20] == 0 : e.B[0x20] != 0) return;
        var b24 = (sbyte)e.B[0x24];
        if (b24 < 0) { DrawDebrisSpin(v, c, e, slot); return; }       // $4F22: an explosion's debris
        // own shadows with the sun up come first: the 14 kinds' flat shadow models (+$24) are the original's fake
        // ones and go; at night and indoors they stay
        if (OwnShadows && e.B[0x25] != 0 && !InsideView && SunOffset(World) is { } sun)
        {                                                   // the enhancement: the main model squashed
            var m = StepModel(e, e.B[0x25]);                // flat, drawn as EHB marks before the model,
            Vertices(e, m);                                 // falling away from the sun (none at night,
                                                            // none inside a building)
            // ENHANCEMENT (mp_view_finish, south.md A10): the sun offset is a WORLD direction and
            // the camera words are world-negated at $80 - the offset negates with them.
            if (c.Angle != 0) sun = (-sun.DirX, -sun.DirZ, sun.TanElev);
            ProjectModelFlat(e, c, m, sun);
            Faces(v, m, halfBright: true);
        }
        else if (b24 > 0)
        {
            var m = StepModel(e, e.B[0x24] + e.B[0x25]);
            Vertices(e, m);
            ProjectModelFlat(e, c, m);
            Faces(v, m, OwnShadows);
        }
        if (e.B[0x25] == 0) { Dot(v, c, e); return; }
        var main = StepModel(e, e.B[0x25]);
        Vertices(e, main);
        Specials(e, main);
        Project(e, c, main);
        Faces(v, main);
    }

    /// <summary>Draw's state writes for one thing, without the painting (StepRow's walk): Draw's own
    /// guards, then each branch's game work. The dot things and the squashed sun shadow write
    /// nothing - their paint is skipped (the shadow's Rotation steps the main model's own set, which
    /// the main path's step below covers either way).</summary>
    void Step(Entity e)
    {
        if (e.B[0x1C] == 0) return;
        if (InsideView ? e.B[0x20] == 0 : e.B[0x20] != 0) return;
        var b24 = (sbyte)e.B[0x24];
        if (b24 < 0) { StepDebris(e); return; }
        if (b24 > 0) StepModel(e, e.B[0x24] + e.B[0x25]);   // the extra part: +$9C, its own set's step
        if (e.B[0x25] == 0) return;                         // a dot: picture only
        var m = StepModel(e, e.B[0x25]);
        Vertices(e, m);                                     // Specials and the lift read the vertices
        Specials(e, m);
        LiftOntoGround(e, m);
    }

    /// <summary>Lookup + Rotation - the part of drawing a model that is game work: the +$9C scale
    /// write ($4F54) and the animation step ($566E).</summary>
    Model StepModel(Entity e, int n)
    {
        var m = Lookup(e, n);
        Rotation(e, m);
        return m;
    }

    // $4F22: the debris is model 0 (StartDebris gave it the victim's scale and colours): 10
    // pieces of 3 points - the explosion's centre and two rotated vertices - flying apart by the
    // offsets $5A68 ($5AE0 adds the velocities $5AA4 each draw), spinning as they are drawn.

    /// <summary>DrawDebrisSpin's state writes, without the painting (StepRow's walk): the shower's
    /// stepping and its kill after 20 draws ($5AE0), the spin bytes ($4F2E), model 0's +$9C and its
    /// animation step. Placing the pieces touches only model scratch. Returns model 0 as DrawDebrisSpin
    /// draws it (Lookup + Rotation), instead of leaving it in the shared scratch.</summary>
    Model StepDebris(Entity e)
    {
        var s = World;
        if (s != null && _stepDebris)
        {
            if (--s.Debris5A66 < 0) e.B[0] = 0;              // $5AE0: 20 draws, then gone (drawn once more)
            else
                for (var k = 0; k < 30; k++)
                    s.DebrisPos[k] = (short)(s.DebrisPos[k] + DebrisVelocity(s.Data.Main, k));
        }
        var m = Lookup(e, 0);
        if (_stepDebris)
        {
            e.B[0x7C] = (byte)(e.B[0x7C] - 8);              // $4F2E
            e.B[0x7E] = (byte)(e.B[0x7E] + 8);
        }
        Rotation(e, m);
        return m;
    }

    void DrawDebrisSpin(View v, Camera c, Entity e, int slot)
    {
        var s = World;
        var m = StepDebris(e);                              // the state writes, then the model they leave (model 0)
        var b = m.Buf;
        var p = m.Base + 0x14;
        for (var k = 0; k < 10; k++)                        // $536E
        {
            p += 4;                                         // the centre point's vertex is not read
            m.X[k * 3] = e.W(0x56); m.Y[k * 3] = e.W(0x58); m.Z[k * 3] = e.W(0x5A);
            for (var j = 1; j <= 2; j++)
            {
                p += 1;
                var x = (short)((sbyte)b[p] << m.Shift);
                var y = (short)((sbyte)b[p + 1] << m.Shift);
                var z = (short)((sbyte)b[p + 2] << m.Shift);
                p += 3;
                Main(e, m, k * 3 + j, x, y, z);
            }
            (_sA, _cA, _sB, _cB) = (_sB, _cB, _sA, _cA);    // $53A4: the next piece turns the other way
        }
        // ENHANCEMENT (mp_view_finish, south.md A21): the flight offsets are world directions
        // (camera-space constants while the camera stood north); at $80 their x/z negate - the
        // picture only: the shower's own stepping above is untouched.
        var flipDebris = c.Angle != 0;
        for (var k = 0; k < 10; k++)                        // $524E
        {
            short ox = s?.DebrisPos[k] ?? 0, oz = s?.DebrisPos[10 + k] ?? 0, oy = s?.DebrisPos[20 + k] ?? 0;
            if (flipDebris) { ox = (short)-ox; oz = (short)-oz; }
            for (var j = 0; j < 3; j++)
            {
                var i = k * 3 + j;
                m.X[i] = (short)(m.X[i] + ox);
                m.Z[i] = (short)(m.Z[i] + oz);
                Persp(c, m, i, (short)(c.Height0E - (short)(m.Y[i] + oy)));
            }
        }
        Faces(v, m);
    }

    // Whether this draw steps a debris shower (its counter, offsets and spin - $5AE0, $4F2E) or
    // only draws it where it is. The original steps it every draw, and the A500 drew a pass about
    // every 7 blanks; the real game here runs a pass every blank (Play.AnimEveryBlanks), so the
    // shower steps only on the passes the animations step - the original's pace, not 7x faster.
    // Every pass when AnimEveryBlanks is 0 (the tests, the oracle), as the original.
    bool _stepDebris = true;

    static short DebrisVelocity(byte[] main, int k) => (short)(main[0x5AA4 - 0x800 + 2 * k] << 8 | main[0x5AA5 - 0x800 + 2 * k]);

    /// <summary>ENHANCEMENT (the card's EXPLOSIONS: PIECES): told of every thing that blows up, a copy of its record as it
    /// was at that moment, and where the shell, missile or blast that did it was (Entity.BlastHit; null = none, a bullet,
    /// a ram, a crash). The picture only - nothing in the game reads it; null outside the card-drawn view.</summary>
    public static Action<byte[], (ushort X, short Y, ushort Z)?, int>? Exploded;   // the int: that shell's, missile's or blast's type, 0 with none

    /// <summary>StartDebris ($59FE): 20 draws, offsets cleared, model 0 takes the victim's scale
    /// (+$9C) and, face by face, its faces' colours (cycling). Rewrites the game's own models.</summary>
    public static void StartDebris(WorldState s, Entity victim, int slot)
    {
        Exploded?.Invoke((byte[])victim.B.Clone(), victim.BlastHit ? (victim.BlastX, victim.BlastY, victim.BlastZ) : null, victim.BlastHit ? victim.BlastType : 0);
        var models = s.Models;
        var b = models.Buf;
        var shift = (byte)victim.W(0x9C);                    // $5A1A: read before the lookup rewrites it
        var vm = models.Offsets[victim.B[0x25]];
        victim.SetW(0x9C, b[vm + 0x11]);                     // $4F54 on the victim: +$9C = its model's scale
        var edges = vm + 0x14 + models.U(vm + 0x12) * 4;    // $4F54: past the vertices
        var ne = models.U(edges);
        var faces = edges + 2 + ne * 2 + 2;                  // past the edges and the face count
        s.Debris5A66 = 0x14;
        Array.Clear(s.DebrisPos);
        var m00 = models.Offsets[0];
        b[m00 + 0x11] = shift;                               // $5A2E
        var a00 = m00 + 0x14 + models.U(m00 + 0x12) * 4;
        a00 += 2 + models.U(a00) * 2 + 2;                    // model 0's first face
        var a10 = faces;
        for (var k = 0; k < 10; k++, a00 += 5)               // $5A4A: colour byte, then the next face
        {
            b[a00] = b[a10];
            var n = b[a10 + 1] & 0x0F;
            a10 += 2 + n;
            if ((sbyte)b[a10] < 0) a10 = faces;
        }
    }

    public sealed class Model
    {
        public int Base, Anim, Shift, Count, Edges, FacesAt;
        /// <summary>ENHANCEMENT: an extended ("OW") model - word corners, edges and face lists (Models).</summary>
        public bool Wide;
        public uint Selector;
        public byte[] Buf = [];                              // the bytes this model reads: the session's objfile, or a shower's copy of model 0
        public short[] X = new short[256], Y = new short[256], Z = new short[256];   // grown for an extended model (Lookup)
        /// <summary>Each corner as articulated, before the model's own turn and placing (CheckModel reads it).</summary>
        public short[] LX = new short[256], LY = new short[256], LZ = new short[256];
        public short[] P = new short[512];
        /// <summary>ENHANCEMENT (the 3D sink): each vertex in world units (x, height, z) as Persp
        /// placed it; null while no Sink3D is set.</summary>
        public double[]? W;
        public short MinY;
    }

    /// <summary>(renderer = 3d, godot/real3d): handed every face of
    /// every model drawn, hidden or not (the 3D view's depth buffer decides), with the model's
    /// world vertices in m.W.
    /// Null = the faithful path.</summary>
    public interface ISink3D
    {
        void Face(Model m, ReadOnlySpan<int> edgeNumbers, int colour, bool line, bool detail, bool half);
        void Point(double x, double y, double z, int colour);
        void Begin(Camera c);                               // the camera of the faces that follow
    }
    public static ISink3D? Sink3D;

    /// <summary>The card's checks (CheckPieces): a point in the main model's own units - a back wall's extra point,
    /// godot/real3d/ModelCaps - turned and placed as Main turns a plain corner of it (no articulation), right after
    /// DrawStored(e): world units, as the sink's corners (y lifted onto the ground as Project's).</summary>
    public (double X, double Y, double Z) StoredExtra(Entity e, short x, short y, short z)
    {
        if (!e.Bit(7, 0x12))
        {
            if (e.B[0x80] != 0) Roll(ref x, ref y, _sC, _cC);
            if (e.B[0x7C] != 0) Pitch(ref y, ref z, _sA, _cA);
            if (e.B[0x7E] != 0) Yaw(ref x, ref z, _sB, _cB);
            var (turn, sT, cT) = _turn;
            if (turn == 0x80) { x = (short)-x; z = (short)-z; }
            else if (turn != 0) Yaw(ref x, ref z, sT, cT);
        }
        x += e.W(0x56); y += e.W(0x58); z += e.W(0x5A);
        return ((x + e.UW(0x4A) * 2) / 2.0, (short)(y + e.W(0x7A)), (z + e.UW(0x4E) * 2) / 2.0);
    }

    // Place's camera-relative doubled x/z back to world units ($4164: cx = 2x - $1548; at angle $80 negated)
    static (double X, double Z) ToWorld(Camera c, double x, double z) =>
        c.Angle != 0 ? ((c.G1548 - x) / 2, (c.G1544 - z) / 2) : ((x + c.G1548) / 2, (z + c.G1544) / 2);

    readonly Model _m = new();

    // $4F54
    public Model Lookup(Entity e, int n)
    {
        var a2 = _models.Offsets[n & 0xFF];
        var b = _models.Buf;
        _m.Buf = b;
        _m.Base = a2;
        _m.Anim = b[a2 + 0x10];
        _m.Shift = b[a2 + 0x11];
        e.SetW(0x9C, _m.Shift);
        _m.Count = _models.U(a2 + 0x12);
        _m.Selector = (uint)(_models.U(a2 + 0x0C) << 16 | _models.U(a2 + 0x0E));
        _m.Wide = _models.Wide(a2);
        _m.Edges = a2 + 0x14 + _m.Count * (_m.Wide ? 8 : 4);
        var a3 = _m.Edges;
        var ne = _models.U(a3);
        _m.FacesAt = a3 + 2 + ne * (_m.Wide ? 4 : 2) + 2;   // past the face count word
        if (_m.Count > _m.X.Length)                          // an extended model past the original's 256 corners: the scratch grows
        {
            var size = _m.Count;
            (_m.X, _m.Y, _m.Z, _m.LX, _m.LY, _m.LZ, _m.P, _m.W) = (new short[size], new short[size], new short[size], new short[size], new short[size], new short[size], new short[size * 2], null);
        }
        _raster.SkipFlag = false;                           // sf $1600
        return _m;
    }

    /// <summary>Model 0's extent, base to past its faces' $FF terminator (Faces $77AA's walk:
    /// each face is 2 bytes of header + n vertex bytes, until a negative colour byte).</summary>
    public static int Model0Length(Models models)
    {
        var b = models.Buf;
        var m0 = models.Offsets[0];
        var a0 = m0 + 0x14 + models.U(m0 + 0x12) * 4;
        a0 += 2 + models.U(a0) * 2 + 2;                      // model 0's first face
        while ((sbyte)b[a0] >= 0) a0 += 2 + (b[a0 + 1] & 0x0F);
        return a0 + 1 - m0;                                  // the $FF itself
    }

    // ---------------------------------------------------------------- $566E rotation + animation

    (int E, int M) _sA, _cA, _sB, _cB, _sC, _cC;            // $1594-$15AA: pitch, yaw, roll
    (int E, int M) _sP, _cP, _sQ, _cQ, _sR, _cR;            // $15AC-$15C2: articulation
    int _ap, _aq, _ar;                                      // $15C4/$15C6/$15C8
    short _pivX, _pivY, _pivZ, _offX, _offY, _offZ;          // $1588, $158E

    public void Rotation(Entity e, Model m)
    {
        m.MinY = 0x7FFF;
        int a = e.B[0x7C] * 4, b = e.B[0x7E] * 4, r = e.B[0x80] * 4;
        _sA = _trig.Sin(a); _cA = _trig.Cos(a);
        _sB = _trig.Sin(b); _cB = _trig.Cos(b);
        _sC = _trig.Sin(r); _cC = _trig.Cos(r);
        if (m.Anim == 0) return;
        var d7 = e.Flags;
        if ((d7 >> 0x19 & 1) == 0 && (d7 >> 0x1B & 1) != 0 && e.W(0x3E) <= 0)
        {
            Array.Clear(_artic);                            // $56D0: clr.l $561C
            return;
        }
        var set = m.Anim - 1;
        var at = set * 20;
        var ctl = (uint)(Anim[at] << 24 | Anim[at + 1] << 16 | Anim[at + 2] << 8 | Anim[at + 3]);
        at += 4;
        var bit = 1u << (set & 31);
        var fresh = (_ticked & bit) == 0;
        _ticked |= bit;
        for (var k = 0; k < 4; k++, at += 4)
        {
            ctl = ctl << 8 | ctl >> 24;                     // rol.l #8
            var c = (sbyte)(byte)ctl;
            int v = Anim[at];
            if (c < 0) v = e.B[0x84 + k + (int)(ctl & 3)];  // $5710: A2 = A4+$84 steps one byte a channel ($5732)
            else if (fresh && c > 0 && AnimShare > 0) v = FineStep(at, c);
            else if (fresh && c > 0)
            {
                v = (byte)(v + Anim[at + 1]);
                if ((byte)(c - 1) != 0)
                {
                    var sv = (sbyte)(byte)v;
                    if (sv <= (sbyte)Anim[at + 2] || sv >= (sbyte)Anim[at + 3])
                    {
                        Anim[at + 1] = (byte)-(sbyte)Anim[at + 1];   // $5728: bounce
                        v = (byte)(v + Anim[at + 1]);
                    }
                }
            }
            _artic[k] = (byte)v;
            Anim[at] = (byte)v;
        }
    }

    /// <summary>(SMOOTH ANIMATION): this share of a whole step (256ths) every pass instead of the whole step every 7th blank -
    /// Play sets it from the pass's blanks (AnimEveryBlanks), so the joints keep the original's rhythm, reach and
    /// turning-back points, moving every pass. 0 = the original's whole steps.</summary>
    public int AnimShare;

    // each table channel's angle in 256ths of a 256th (the table byte is its whole part); out of step with the table
    // byte (a new game, a load, the original's whole step) it starts again from the byte
    readonly int[] _fine = new int[320];

    int FineStep(int at, int c)
    {
        var fine = _fine[at];
        if ((fine >> 8 & 0xFF) != Anim[at]) fine = Anim[at] << 8;
        var d = (sbyte)Anim[at + 1] * AnimShare;
        fine = (fine + d) & 0xFFFF;
        if ((byte)(c - 1) != 0)                                 // as $5728: past a limit the step turns back
        {
            var sv = (sbyte)(byte)(fine >> 8);
            if (sv <= (sbyte)Anim[at + 2] || sv >= (sbyte)Anim[at + 3])
            {
                Anim[at + 1] = (byte)-(sbyte)Anim[at + 1];
                fine = (fine - d) & 0xFFFF;
            }
        }
        _fine[at] = fine;
        return fine >> 8;
    }

    // ---------------------------------------------------------------- $531E vertices

    public void Vertices(Entity e, Model m)
    {
        var b = m.Buf;
        var p = m.Base + 0x14;
        var stride = m.Wide ? 8 : 4;                        // an extended model's corners are words (Models)
        // corner k's coordinate (0 x, 1 y, 2 z) from the corner at p, shifted: a signed byte, or an extended model's word
        short C(int p, int k) => m.Wide ? (short)((short)(b[p + 2 + k * 2] << 8 | b[p + 3 + k * 2]) << m.Shift) : (short)((sbyte)b[p + 1 + k] << m.Shift);
        short px = e.W(0x56), py = e.W(0x58), pz = e.W(0x5A);
        if (e.Bit(7, 0x12))                                 // no rotation: just place
        {
            for (var i = 0; i < m.Count; i++, p += stride)
            {
                var x = (short)(px + C(p, 0));
                var y = (short)(py + C(p, 1));
                var z = (short)(pz + C(p, 2));
                m.LX[i] = (short)(x - px); m.LY[i] = (short)(y - py); m.LZ[i] = (short)(z - pz);
                if (y <= m.MinY) m.MinY = y;
                m.X[i] = x; m.Y[i] = y; m.Z[i] = z;
            }
            return;
        }
        var mode = 0;                                       // 0 plain, 1 rotate about pivot, 2 follow
        for (var i = 0; i < m.Count; i++, p += stride)
        {
            int flag = b[p];
            var x = C(p, 0);
            var y = C(p, 1);
            var z = C(p, 2);
            if (flag != 0)                                  // $551E
            {
                var kind = flag >> 4 & 7;
                int axis = flag >> 2 & 3, which = flag & 3;
                if (kind == 1)
                {
                    Articulate(which, axis);                // $5620
                    _pivX = x; _pivY = y; _pivZ = z;
                    mode = 1;
                    Main(e, m, i, x, y, z);
                    continue;
                }
                if (kind == 2)
                {
                    _offX = x; _offY = y; _offZ = z;
                    SubRotate(ref x, ref y, ref z);
                    _offX -= x; _offY -= y; _offZ -= z;
                    mode = 2;
                    Main(e, m, i, x, y, z);
                    continue;
                }
                mode = 0;                                   // kind 0, and 3-7 ($5558 also flashes COLOR00)
                Main(e, m, i, x, y, z);
                continue;
            }
            if (mode == 1) SubRotate(ref x, ref y, ref z);
            else if (mode == 2) { x -= _offX; y -= _offY; z -= _offZ; }
            Main(e, m, i, x, y, z);
        }
    }

    void Articulate(int which, int axis)
    {
        var d1 = _artic[which] * 4;
        _ap = axis == 0 ? d1 : 0;
        _aq = axis == 1 ? d1 : 0;
        _ar = axis >= 2 ? d1 : 0;
        _sP = _trig.Sin(_ap); _cP = _trig.Cos(_ap);
        _sQ = _trig.Sin(_aq); _cQ = _trig.Cos(_aq);
        _sR = _trig.Sin(_ar); _cR = _trig.Cos(_ar);
    }

    // $5484: rotate about the pivot by the articulation angle
    void SubRotate(ref short x, ref short y, ref short z)
    {
        x -= _pivX; y -= _pivY; z -= _pivZ;
        if (_ar != 0) Roll(ref x, ref y, _sR, _cR);
        if (_ap != 0) Pitch(ref y, ref z, _sP, _cP);
        if (_aq != 0) Yaw(ref x, ref z, _sQ, _cQ);
        x += _pivX; y += _pivY; z += _pivZ;
    }

    // $53E2: roll, pitch, yaw, then to camera space; tracks the lowest y in $1580.
    // ENHANCEMENT (mp_view_finish, south.md A23): the camera's own angle turns the finished
    // model - a FINAL yaw AFTER the model's roll/pitch/yaw (they do not commute: folding the
    // angle into the heading byte would pitch a wrongly-turned model), BEFORE the view words
    // are added. At $80 sin = 0, cos = -1: camera-space x and z negate exactly.
    (byte, (int, int), (int, int)) _turn;                       // the camera angle and its sin/cos
    void Main(Entity e, Model m, int i, short x, short y, short z)
    {
        m.LX[i] = x; m.LY[i] = y; m.LZ[i] = z;
        if (e.B[0x80] != 0) Roll(ref x, ref y, _sC, _cC);
        if (e.B[0x7C] != 0) Pitch(ref y, ref z, _sA, _cA);
        if (e.B[0x7E] != 0) Yaw(ref x, ref z, _sB, _cB);
        var (turn, sT, cT) = _turn;
        if (turn == 0x80) { x = (short)-x; z = (short)-z; }   // the half turn negates EXACTLY: the table's
                                                              // sin(180) is -25858>>21, not 0 - a trig yaw
                                                              // here rounds x and z off by one (68 turned -69)
        else if (turn != 0) Yaw(ref x, ref z, sT, cT);
        x += e.W(0x56); y += e.W(0x58); z += e.W(0x5A);
        if (y <= m.MinY) m.MinY = y;
        m.X[i] = x; m.Y[i] = y; m.Z[i] = z;
    }

    static int Mul((int E, int M) f, short v) => M68.Muls(v, f.M) >> f.E;

    static void Roll(ref short x, ref short y, (int E, int M) s, (int E, int M) c)
    {
        var nx = (short)(Mul(c, x) + Mul(s, y));
        var ny = (short)(Mul(c, y) - Mul(s, x));
        x = nx; y = ny;
    }

    static void Pitch(ref short y, ref short z, (int E, int M) s, (int E, int M) c)
    {
        var nz = (short)(Mul(c, z) + Mul(s, y));
        var ny = (short)(Mul(c, y) - Mul(s, z));
        y = ny; z = nz;
    }

    static void Yaw(ref short x, ref short z, (int E, int M) s, (int E, int M) c)
    {
        var nx = (short)(Mul(c, x) + Mul(s, z));
        var nz = (short)(Mul(c, z) - Mul(s, x));
        x = nx; z = nz;
    }

    // $52E0: the selector's first two vertices, relative to the entity (gun ports): +$62, +$68
    void Specials(Entity e, Model m)
    {
        if (m.Selector == 0) return;
        var sel = m.Selector;
        for (var k = 0; k < 2; k++)
        {
            sel = sel << 8 | sel >> 24;
            var n = (sbyte)(byte)sel;
            if (n == 0) continue;
            var i = n - 1;
            var o = 0x62 + k * 6;
            e.SetW(o, (short)(m.X[i] - e.W(0x56)) >> 1);
            e.SetW(o + 2, (short)(m.Y[i] - e.W(0x58)));
            e.SetW(o + 4, (short)(m.Z[i] - e.W(0x5A)) >> 1);
        }
    }

    // $51F8: perspective, lifted so the lowest point sits on the ground
    void Project(Entity e, Camera c, Model m)
    {
        var lift = LiftOntoGround(e, m);
        for (var i = 0; i < m.Count; i++)
            Persp(c, m, i, (short)(c.Height0E - (short)(m.Y[i] + lift)));
    }

    /// <summary>Project's state write, without the painting (StepRow's walk): the lift that sits
    /// the model's lowest point on the ground, stored at +$7A ($51F8) - bullet spawn heights and the
    /// doors' tests read it.</summary>
    short LiftOntoGround(Entity e, Model m)
    {
        var lift = (short)(e.W(0x74) - m.MinY);
        if (lift < 0) lift = 0;
        e.SetW(0x7A, lift);
        return lift;
    }

    double GroundAt(double x, double z) => _ground.Height((ushort)Math.Clamp((int)Math.Round(x), 0, 0xFFFF), (ushort)Math.Clamp((int)Math.Round(z), 0, 0xFFFF));

    // NEW (3D view only): a shadow lies ON the ground under each of its corners (the original stamps it flat at the
    // height under the centre, and the sun's offset slides it off over ground that is higher or lower there).
    void ShadowOnGround(Model m)
    {
        if (Sink3D == null || m.W is not { } w || _ground == null) return;              // no ground: a check of the corners alone
        for (var i = 0; i < m.Count; i++) w[i * 3 + 1] = GroundAt(w[i * 3], w[i * 3 + 2]);
    }

    // $52A2: every vertex at ground height: the shadow / base part
    void ProjectModelFlat(Entity e, Camera c, Model m)
    {
        var y = (short)(c.Height0E - e.W(0x74));
        for (var i = 0; i < m.Count; i++) Persp(c, m, i, y);
        ShadowOnGround(m);
    }

    // Own sun shadows: every vertex still at ground height, but also moved AWAY from the sun
    // by h / tan(elevation), h = the vertex's height above the ground with the model's lowest
    // point sat on it (the lift of $51F8; an object above the ground carries its own height).
    // The offset is not turned: the camera does not rotate - Place ($4164) sets
    // cx = x*2 - $1548, cz = z*2 - $1544, a translation and a doubling only - so world east (+x)
    // is camera +x and world north (+z) is camera +z. Nor is it doubled: the model's corners are
    // added to that position unscaled in x, y and z alike (Vertices $531E), so the picture is
    // camera units every way and h (a model's own height) is already one. No length cap
    // (SunShade.ShadowLength): the sun never drops under 10 degrees, so at most 5.7 x the height.
    void ProjectModelFlat(Entity e, Camera c, Model m, (double DirX, double DirZ, double TanElev) sun)
    {
        var ground = e.W(0x74);
        var lift = Math.Max(0, ground - m.MinY);
        var y = (short)(c.Height0E - ground);
        for (var i = 0; i < m.Count; i++)
        {
            var x = m.X[i];
            var z = m.Z[i];
            var h = m.Y[i] + lift - ground;
            if (h > 0)
            {
                var d = SunShade.ShadowLength(h, sun.TanElev);   // h and x / z are both camera units (see below)
                x = (short)(x + sun.DirX * d);
                z = (short)(z + sun.DirZ * d);
            }
            Persp(c, m, i, y, x, z);
        }
        ShadowOnGround(m);
    }

    void Persp(Camera c, Model m, int i, short y) => Persp(c, m, i, y, m.X[i], m.Z[i]);

    void Persp(Camera c, Model m, int i, short y, double x, double z)
    {
        int px = (short)x << 8, yy = y << 8;
        var zz = (short)((short)z + 0x100);
        if (zz > 0)
        {
            px = M68.Divs(px, zz);
            yy = M68.Divs(yy, zz);
        }
        m.P[i * 2] = (short)(px + c.CentreX);
        m.P[i * 2 + 1] = (short)(yy + 0x2E);
        if (Sink3D != null)
        {
            var w = m.W ??= new double[m.X.Length * 3];
            (w[i * 3], w[i * 3 + 2]) = ToWorld(c, x, z);
            w[i * 3 + 1] = c.Height0E - y;
        }
    }

    // $5B0A: model 0 is a dot over its ground dot. d3 = 4 then 0, shifted out bit 3 first into
    // planes 0-3 ($5B7C), so the dot is colour 2 and its ground dot colour 0.
    void Dot(View v, Camera c, Entity e)
    {
        Pixel(v, c, e.W(0x56), e.W(0x58), e.W(0x5A), 2);
        Pixel(v, c, e.W(0x56), e.W(0x74), e.W(0x5A), 0);
        if (Sink3D != null)
        {
            var (x, z) = ToWorld(c, e.W(0x56), e.W(0x5A));
            Sink3D.Point(x, e.W(0x58), z, 2);
            Sink3D.Point(x, e.W(0x74), z, 0);
        }
    }

    static void Pixel(View v, Camera c, short x, short y, short z, int colour)
    {
        int px = x << 8, py = (short)(c.Height0E - y) << 8;
        var zz = (short)(z + 0x100);
        if (zz > 0) { px = M68.Divs(px, zz); py = M68.Divs(py, zz); }
        var sy = (short)(py + 0x2E);
        if (sy < 0 || sy > 0x8B) return;
        var sx = (short)(px + c.CentreX);
        if (sx < 0 || sx >= 0x140) return;
        v.Pixels[sy * View.Width + sx] = (byte)colour;
    }

    // ---------------------------------------------------------------- $76FC + $77AA faces

    /// <summary>Every face of the model - filled and line faces alike. halfBright (OwnShadows'
    /// shadow): the spans set the EHB bit instead of writing a colour, colour ignored.</summary>
    void Faces(View v, Model m, bool halfBright = false)
    {
        if (m.Wide) throw new InvalidOperationException("an extended (OW) model is the card's own: the processor's picture draws the file's models");
        _raster.Mark = halfBright;
        var b = m.Buf;
        var ne = b[m.Edges] << 8 | b[m.Edges + 1];          // from the model's own bytes (a shower's copy is base 0)
        _raster.Edges(b.AsSpan(m.Edges + 2, ne * 2), ne, m.P);
        var a0 = m.FacesAt;
        while ((sbyte)b[a0] >= 0)                           // $77AA: until $FF
        {
            var colour = b[a0] & 0x0F;
            int flags = b[a0 + 1];
            var n = flags & 0x0F;
            var list = b.AsSpan(a0 + 2, n);
            a0 += 2 + n;
            var kind = flags & 0x70;
            if (Sink3D != null)
            {
                Span<int> ints = stackalloc int[n];
                for (var i = 0; i < n; i++) ints[i] = list[i];
                Sink3D.Face(m, ints, colour, kind != 0 && kind != 0x40, (flags & 0x80) != 0, halfBright);
            }
            if ((flags & 0x80) != 0 && _raster.SkipFlag) continue;   // detail face of a hidden face
            _raster.Filled = kind != 0;
            if (kind == 0 || kind == 0x40) _raster.Fill(v, list, n, colour);
            else foreach (var edge in list) _raster.Line(v, edge, colour);
        }
        _raster.Mark = false;
    }
}
