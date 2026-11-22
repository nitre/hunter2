using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>(renderer = 3d): the world drawn by the card from the
/// game's own data. The ground comes from the map and its type bytes (groundgpu.gdshader), every thing's model and
/// ground shadow from the entity records, the animation table and the objfile (gpumodels.gdshader,
/// gpushadow.gdshader). Nothing is built per frame on the processor: Update only uploads the raw bytes the rules
/// already hold - the records, the animation table - and the shaders do the rest.
/// At the start the card cuts every model face into triangles once (slots.gdshaderinc), and this node keeps the
/// result as a table (a face's corner numbers) - the models themselves never leave the objfile. Line faces
/// (gpulines), the dots (gpupoints), the original's debris (gpudebris) and the explosion pieces (gpupieces) too.</summary>
public sealed partial class GpuWorld : Node3D
{
    public const int LiveRows = EntityTable.MaxCount;   // the live records
    public const int StoredRows = 2048 + 14080;        // after them, the map's stored objects (the grid's 16 x 16 cells of 8, then a large fixed overflow - maths32.md group 3: the room is sized up front, never grown)
    const int Rows = LiveRows + StoredRows;      // entity records the texture holds
    /// <summary>Records side by side in a texture row of ent_tex (the live records only: a row each). Record r is texel
    /// (r % PerRow * $A0 + offset, r / PerRow); a row past them is a stored object (place.gdshaderinc _eb).</summary>
    public const int PerRow = 1;
    const int TexRows = LiveRows, Records = LiveRows;           // ent_tex / entprev_tex: the live records, as floats (the fine angle parts are -128..255)
    // The map's stored objects: bytes (R8), two records a texture row, sent only when they change (they did go as 10 MB
    // of floats every frame); their in-view marks +$1C, which change as the view moves, in a strip of their own
    const int StoredTexRows = (StoredRows + 1) / 2, SeenW = 128, SeenH = (StoredRows + SeenW - 1) / SeenW;
    readonly byte[] storedBytes = new byte[StoredTexRows * 2 * 0xA0], storedSent = new byte[StoredTexRows * 2 * 0xA0];
    readonly byte[] seenBytes = new byte[SeenW * SeenH], seenSent = new byte[SeenW * SeenH];
    readonly ImageTexture storedTex = ImageTexture.CreateFromImage(Image.CreateEmpty(0xA0 * 2, StoredTexRows, false, Image.Format.R8));
    readonly ImageTexture seenTex = ImageTexture.CreateFromImage(Image.CreateEmpty(SeenW, SeenH, false, Image.Format.R8));
    // The stored objects each pass draws this frame (gpumodels.gdshaderinc slot_row): their rows, in table order - the ones in
    // view (the view distance's mark) and, while the sun's picture is drawn, the ones near enough to throw a shadow into it
    readonly float[] drawList = new float[SeenW * SeenH], drawSent = new float[SeenW * SeenH];
    readonly byte[] drawBytes = new byte[SeenW * SeenH * 4];
    readonly ImageTexture drawTex = ImageTexture.CreateFromImage(Image.CreateEmpty(SeenW, SeenH, false, Image.Format.Rf));
    int drawStored, drawSentCount = -1;
    int storedUsed;
    readonly byte[] sinfile;
    byte[] map;                                                  // the ground the card draws: the played world's
    ImageTexture typesTex = null!;
    readonly byte[]? main;              // main.bin for the bake: the type records say which models are vehicles, animals, people
    readonly ImageTexture entTex, animTex;
    readonly float[] entFloats = new float[Records * 0xA0];
    readonly byte[] entBytes = new byte[Records * 0xA0 * 4];       // entFloats as the texture takes them: one buffer kept, not 10 MB new a frame
    readonly byte[] entSent = new byte[Records * 0xA0 * 4], animSent = new byte[320 * 2 * 4];   // what the card holds now: the same is not sent again
    readonly ShaderMaterial ground, models, glassFaces, shadows, lines, dots, debris;   // glassFaces: the models' glass faces, see-through (gpuglass.gdshader)
    MultiMeshInstance3D? glassMesh;
    MultiMesh? slotRows, lineRows;                                // the model slots (models, glass, shadows, the sun's picture) and the lines: each frame only the rows in use are drawn
    int leanShown, leanRowShown;                                  // the stick lean (and its row) last given to the shaders

    /// <summary>The record row the stick lean turns (the player's, 0); CheckCarInside points it at one of its own rows.</summary>
    public int LeanRow { get; set; }                                    // the glass pass, shown with SEE-THROUGH only
    readonly ImageTexture dposTex;
    readonly ImageTexture mdlTex, atTex;         // CARD MODELS: the card's models in our own form (CardModels): every shader reads headers, corners, edges and faces from here; atTex = objfile offset -> store address
    byte[] mdlBytes = [];
    /// <summary>The card's models as the store holds them, and its texture (the check reads both).</summary>
    public CardModels Store { get; }
    public ImageTexture MdlTex => mdlTex;
    /// <summary>The records and the height map as textures: the camera (view.gdshaderinc) reads the player and the ground from them.</summary>
    public ImageTexture EntTex => entTex;
    public ImageTexture MapTex { get; }
    /// <summary>ENHANCEMENT (FINER GROUND): the fine store's points a cell side (0 = off) and its texture (null off).</summary>
    public int FineN { get; }
    public ImageTexture? FineTex { get; }
    readonly int model0, faces0;                 // model 0 (the debris model) and where its ten faces start ($59FE)
    readonly byte[] model0Seen = new byte[11], model0Now = new byte[11];   // its scale byte and the ten face colours: as last uploaded, and now
    /// <summary>The materials that need the palette uniforms every frame (Real3DView.Frame).</summary>
    public ShaderMaterial[] Materials => materials ??= [ground, models, glassFaces, shadows, lines, dots, debris, pieces, marks];
    ShaderMaterial[]? materials, things, sunLit;  // made once: the lists Update sets uniforms on every frame
    readonly float[] animFloats = new float[320 * 2], dposFloats = new float[30];   // SMOOTH PARTS: anim row 1 = the table one step back
    readonly byte[] animBytes = new byte[320 * 2 * 4], dposBytes = new byte[30 * 4];
    // SMOOTH PARTS: the animation table and the record part bytes (+$84.., +$18) as they were one
    // step back, so the card can slide the moving parts between the steps (place.gdshaderinc entity_artic)
    readonly byte[] animHeld = new byte[320], animHeldPrev = new byte[320];
    const int Held = 9;                                                  // a record's held bytes: +$84..+$8B, then the steering byte +$18 (CarInside's wheel)
    static int HeldAt(int o) => o < 8 ? 0x84 + o : 0x18;
    readonly byte[] recHeld = new byte[LiveRows * Held], recHeldPrev = new byte[LiveRows * Held];   // live records only (stored ones never step)
    readonly ImageTexture entprevTex = ImageTexture.CreateFromImage(Image.CreateEmpty(0xA0 * PerRow, TexRows, false, Image.Format.Rf));   // record-shaped, the bytes at their true offsets, so it drops in anywhere ent_tex does
    readonly float[] entprevFloats = new float[Records * 0xA0];
    readonly byte[] entprevBytes = new byte[Records * 0xA0 * 4];
    // TANK TRACKS (TankTracks): each live tank's rolling in 256ths of its wheel's turn, added up from its own moves (TrackRoll),
    // sent as roll_tex (a texel a record row: x = row & 255, y = row >> 8; stored rows stay 0 - a stored tank never moves)
    const int RollW = 256, RollH = (Rows + RollW - 1) / RollW;
    readonly ImageTexture rollTex = ImageTexture.CreateFromImage(Image.CreateEmpty(RollW, RollH, false, Image.Format.Rf));
    readonly float[] rollFloats = new float[RollW * RollH];
    readonly byte[] rollBytes = new byte[RollW * RollH * 4];
    readonly int[] rollModel = Enumerable.Repeat(-1, LiveRows).ToArray();
    readonly int[] rollX = new int[LiveRows], rollZ = new int[LiveRows];
    bool rollDirty = true;
    // TRACK MARKS: a ring of things noted on the ground in the world, the oldest written over. An entry is two texels of
    // mark_tex, column = the entry: row 0 (world x, world z, heading in radians, clock in blanks), row 1 (kind, half width,
    // half length, spare) in world units. Kind 0 is an empty entry, MarkTread a tread bar (gputracks.gdshader draws only
    // those), so other kinds of note can share the ring. 1024: a tank at full speed goes 5 world units a blank and lays a
    // bar a side every 21 units ($16's pitch 42 model units), 2 x 12 bars in the 50 blanks a bar lasts, with a clod a side
    // about every 4 blanks beside them: room for many tanks at once.
    const int MarkRing = 1024, MarkTread = 1;
    const float MarkLife = 50;                   // blanks a mark lasts, one second (gputracks.gdshader MARK_LIFE)
    const int MarkJump = 64;                     // world units in one picture: further than a tank drives (about 4 a blank at speed 5) even over a dozen blanks
    readonly ImageTexture markTex = ImageTexture.CreateFromImage(Image.CreateEmpty(MarkRing, 2, false, Image.Format.Rgbaf));
    readonly float[] markFloats = new float[MarkRing * 2 * 4];
    readonly byte[] markBytes = new byte[MarkRing * 2 * 4 * 4];
    readonly double[] markGone = new double[LiveRows];   // world units a tank has gone since its last bar
    int markNext, markLive = -1;
    bool markDirty = true;
    readonly ShaderMaterial marks = new() { Shader = GD.Load<Shader>("res://real3d/gputracks.gdshader"), RenderPriority = -1 };   // before the shadows (see gputracks.gdshader)
    static readonly bool MarkLog = System.Environment.GetEnvironmentVariable("HUNTER_TRACK_MARKS") == "1";   // print each mark noted
    float animFracLast = 2f;                     // > 1 until the first update
    bool entprevDirty = true;

    // DEBRIS: PIECES (pieces.gdshaderinc): the last Blasts things that blew up, their records as they were and when
    const int Blasts = 2048;                     // the blast room too is fixed and generous (maths32.md group 3), the ring reused oldest-first as before
    readonly ShaderMaterial pieces = new() { Shader = GD.Load<Shader>("res://real3d/gpupieces.gdshader") };
    readonly float[] blastFloats = new float[Blasts * 0xA0];
    readonly byte[] blastBytes = new byte[Blasts * 0xA0 * 4];
    readonly ImageTexture blastTex = ImageTexture.CreateFromImage(Image.CreateEmpty(0xA0, Blasts, false, Image.Format.Rf));
    // row 0: r = the frame it blew up, g = 1 in use; row 1 (BETTER EXPLOSIONS): where the shell, missile or blast that did it
    // was, from the thing's own +$4A / +$4C / +$4E (world units, x and z the shorter way round), a = 1 - or all 0: none
    readonly Image blastInfo = Image.CreateEmpty(Blasts, 2, false, Image.Format.Rgbaf);
    readonly ImageTexture blastInfoTex = ImageTexture.CreateFromImage(Image.CreateEmpty(Blasts, 2, false, Image.Format.Rgbaf));
    int blastNext, blastHigh;                    // blastHigh: one past the last row whose pieces may still show - only those are drawn (pieceRows)
    readonly float[] blastStart = Enumerable.Repeat(float.NegativeInfinity, Blasts).ToArray();   // each row's blast: the frame it blew up on
    const float BlastLife = 600;                 // gpupieces.gdshader piece_life: past it a blast draws nothing
    readonly Vector4[] fireHits = new Vector4[8];   // BETTER EXPLOSIONS step 5 (gpumodels.gdshaderinc fire_hits): where a shell or blast blew a thing up, and that thing's size
    readonly float[] fireHitUntil = new float[8];
    int fireHitNext;
    const float FireHitLife = 300;               // the game's frames a hit counts: past any star's own life
    MultiMesh? pieceRows;
    /// <summary>Vertices a triangle slot of a blast: a piece is a thin slab of 8 triangles (gpupieces.gdshader PIECE_CORNERS).</summary>
    internal const int PieceCorners = 24;
    bool blastsChanged;

    /// <summary>(FINER GROUND in the attract demo): the game runs on the original ground so
    /// the recording keeps its way; the picture draws the finer ground and every thing raised by the gap between the two
    /// at its place (x, z) - set, the uploaded height (+$4C) and ground under it (+$74) take it. Null: as the game's.</summary>
    public Func<ushort, ushort, int>? PictureLift;
    bool painterSet;

    static readonly int[] LiftedWords = [0x4C, 0x74];               // the record words the picture lift moves (made once)

    void Lift(float[] rec, int at)
    {
        if (PictureLift is null || rec[at] == 0) return;
        int U(int o) => (int)rec[at + o] << 8 | (int)rec[at + o + 1];
        var lift = PictureLift((ushort)U(0x4A), (ushort)U(0x4E));
        foreach (var o in LiftedWords)
        {
            var w = (short)U(o) + lift;
            rec[at + o] = (byte)(w >> 8); rec[at + o + 1] = (byte)w;
        }
    }

    /// <summary>Each frame, after the sun's picture knows whether it is drawn: every pass draws the live records and the
    /// stored objects that can show - in view (the view distance's mark +$1C, seenBytes), or, while the sun's picture is
    /// drawn, inside its box round the player (gpusundepth.gdshader's own test) - not the whole map's. The room stays: an
    /// empty or far row drew nothing, but each pass still ran its corners.</summary>
    void DrawList()
    {
        var sunBox = sunView is not null && sunView.RenderTargetUpdateMode == SubViewport.UpdateMode.Always;
        int P(int o) => (int)entFloats[o] << 8 | (int)entFloats[o + 1];   // the player, record 0
        int px = P(0x4A), pz = P(0x4E);
        drawStored = 0;
        for (var r = 0; r < storedUsed; r++)
        {
            var at = r * 0xA0;
            var near = sunBox && Math.Abs((storedBytes[at + 0x4A] << 8 | storedBytes[at + 0x4B]) - px) < SunBoxSize
                              && Math.Abs((storedBytes[at + 0x4E] << 8 | storedBytes[at + 0x4F]) - pz) < SunBoxSize;
            if (seenBytes[r] != 0 || near) drawList[drawStored++] = LiveRows + r;
        }
        if (drawStored != drawSentCount || !drawList.AsSpan(0, drawStored).SequenceEqual(drawSent.AsSpan(0, drawStored)))
        {
            Buffer.BlockCopy(drawList, 0, drawBytes, 0, drawBytes.Length);
            Put(drawTex, Image.CreateFromData(SeenW, SeenH, false, Image.Format.Rf, drawBytes));
            Array.Copy(drawList, drawSent, drawStored);
            drawSentCount = drawStored;
        }
        var drawn = LiveRows + drawStored;
        if (slotRows is not null && slotRows.VisibleInstanceCount != drawn) { slotRows.VisibleInstanceCount = drawn; lineRows!.VisibleInstanceCount = drawn; }
    }

    /// <summary>Lift for a stored object's record, held as bytes (storedBytes): the same words, the same sums.</summary>
    void Lift(byte[] rec, int at)
    {
        if (PictureLift is null || rec[at] == 0) return;
        int U(int o) => rec[at + o] << 8 | rec[at + o + 1];
        var lift = PictureLift((ushort)U(0x4A), (ushort)U(0x4E));
        foreach (var o in LiftedWords)
        {
            var w = (short)U(o) + lift;
            rec[at + o] = (byte)(w >> 8); rec[at + o + 1] = (byte)w;
        }
    }

    /// <summary>A thing blew up (rules Objects.Exploded): its record as it was, clock = the game's frame it blew up on
    /// (GameView.GameClock), and from = where the shell, missile or blast that did it was (null: none - the pieces fly from
    /// the thing's middle, as before) - the card does the rest. Sent with the frame's Update (SendBlasts), once however many.</summary>
    public void AddBlast(byte[] record, float clock, (ushort X, short Y, ushort Z)? from = null)
    {
        // the lowest row whose pieces are gone, so the rows drawn stay as few as the blasts still showing (a slab piece is
        // 24 vertices a triangle slot: every row ever used, drawn every frame, tripled the frame's time by the demo's end);
        // none free (or EXPLOSIONS: LASTING, where nothing goes): the ring, oldest first, as before
        var row = -1;
        if (!PiecesStay) for (var i = 0; i < Blasts && row < 0; i++) if (clock - blastStart[i] > BlastLife) row = i;
        if (row < 0) { row = blastNext; blastNext = (blastNext + 1) % Blasts; }
        blastStart[row] = clock;
        blastHigh = Math.Max(blastHigh, row + 1);
        if (pieceRows is not null) pieceRows.VisibleInstanceCount = blastHigh;
        for (var o = 0; o < 0xA0; o++) blastFloats[row * 0xA0 + o] = record[o];
        Lift(blastFloats, row * 0xA0);
        blastInfo.SetPixel(row, 0, new Color(clock, 1, 0, 0));
        if (from is { } f)
        {
            int U(int o) => record[o] << 8 | record[o + 1];
            blastInfo.SetPixel(row, 1, new Color((short)(f.X - U(0x4A)), (short)(f.Y - U(0x4C)), (short)(f.Z - U(0x4E)), 1));
            // BETTER EXPLOSIONS step 5: the star there is drawn at this thing's size (gpumodels.gdshaderinc fire_big)
            var size = Math.Max((short)U(0x6E), Math.Max((short)U(0x70), (short)U(0x72)));
            fireHits[fireHitNext] = new Vector4(f.X, f.Z, size, 1);
            fireHitUntil[fireHitNext] = clock + FireHitLife;
            fireHitNext = (fireHitNext + 1) % fireHits.Length;
        }
        else blastInfo.SetPixel(row, 1, new Color(0, 0, 0, 0));
        blastsChanged = true;
    }

    /// <summary>The blasts added since the last send, to the card.</summary>
    public void SendBlasts()
    {
        if (!blastsChanged) return;
        blastsChanged = false;
        Buffer.BlockCopy(blastFloats, 0, blastBytes, 0, blastBytes.Length);
        Put(blastTex, Image.CreateFromData(0xA0, Blasts, false, Image.Format.Rf, blastBytes));
        blastInfoTex.Update(blastInfo);
    }
    public bool Ready { get; private set; }
    /// <summary>EXPLOSIONS: LASTING (Settings.PiecesStay): resting pieces stay, not shrinking away after piece_rest.</summary>
    public bool PiecesStay { get; set; }
    /// <summary>The sun this frame, toward it (SunShade.Toward), null at night - for the low clouds' shading.</summary>
    public (float X, float Y, float Z)? SunToward { get; private set; }
    /// <summary>The game's clock this frame ($1B2 hour, $1B0 minute, the day) - for CLOUDS' night sky's moon.</summary>
    public (int Hour, int Minute, int Day) Clock { get; private set; } = (12, 0, 0);
    /// <summary>The card has said once that a stored list was bigger than its fixed room (maths32.md group 3: the
    /// room is sized up front, never grown - it draws what fits and says so).</summary>
    public bool StoredOverflowWarned { get; private set; }
    /// <summary>The baked tables once Ready (CheckPieces reads them): per triangle its corners and colour, per model its
    /// first triangle and count (row 0), segments (row 1) and centre (row 2), the most triangles a model has.</summary>
    public ImageTexture? TriTable { get; private set; }

    /// <summary>The triangle table's rows in use (the texture holds whole rows of TriPerRow).</summary>
    public int TriCount { get; private set; }

    /// <summary>Triangles a texture row of TriTable (gpumodels.gdshaderinc TRI_TEX_PER_ROW): 4 texels each, 4096 wide.</summary>
    public const int TriPerRow = 1024;

    /// <summary>The cap on triangles in all (2026-sized): 1024 texture rows, 64 MB of Rgbaf.</summary>
    public const int MaxTriangles = TriPerRow * 1024;

    /// <summary>Where triangle row's column x (0-3) sits in TriTable (gpumodels.gdshaderinc tri_texel).</summary>
    public static (int X, int Y) TriAt(int row, int x) => (x + 4 * (row % TriPerRow), row / TriPerRow);

    /// <summary>Triangle row's column x read from TriTable's image (x first, as Image.GetPixel).</summary>
    public static Color TriPixel(Image table, int x, int row) { var (px, py) = TriAt(row, x); return table.GetPixel(px, py); }
    /// <summary>The pieces' own material as the game wires it (CheckPieces reads its inputs).</summary>
    public ShaderMaterial PiecesMaterial => pieces;
    /// <summary>The models' and shadows' materials as the game wires them (the live "corners" readback copies their inputs).</summary>
    public ShaderMaterial ModelsMaterial => models;
    /// <summary>GLASS: SEE-THROUGH's pass over the models (CheckGlass hides either mesh by its material).</summary>
    public ShaderMaterial GlassMaterial => glassFaces;
    public ShaderMaterial ShadowsMaterial => shadows;
    public ImageTexture? ModelTable { get; private set; }
    /// <summary>Per model the filled faces the bake found and how many of them it marked glass (CheckGlass reads them).</summary>
    public int[] FilledFaces { get; private set; } = [];
    public int[] GlassFaces { get; private set; } = [];
    /// <summary>GLASS: SEE-THROUGH's recess, per model: glass faces sunk, their edges (a frame quad each), the sunk copies' and frame walls'
    /// triangles, frame walls that took the glass face's own colour (no other face on the edge), and the glass faces left at rest
    /// (a slab, twin or sticker), by model, face number and reason (CheckGlass reads them).</summary>
    public int[] GlassSunkFaces { get; private set; } = [];
    public int[] GlassSunkEdges { get; private set; } = [];
    public int[] GlassSunkTris { get; private set; } = [];
    public int[] GlassFrameTris { get; private set; } = [];
    public int[] GlassFrameOwn { get; private set; } = [];
    /// <summary>Per model: the window borders baked as flat lines (pane on the outer edge, GlassInset 0) - one a window edge, a
    /// post two windows share counted once.</summary>
    public int[] GlassBorders { get; private set; } = [];
    readonly IReadOnlyList<ushort>? viewPalette;
    /// <summary>A view palette colour's brightness ($0RGB, the usual 299 / 587 / 114 weights); 0 without the palette.</summary>
    int Brightness(int colour) => viewPalette is { } p && colour < p.Count
        ? 299 * (p[colour] >> 8 & 15) + 587 * (p[colour] >> 4 & 15) + 114 * (p[colour] & 15) : 0;
    public List<(int Model, int Face, string Why)> GlassSkipped { get; } = [];
    /// <summary>GLASS: SEE-THROUGH's holed walls (GlassWalls): each wall of a window painted flat on it with its outline, the windows'
    /// outlines and its triangles' place in the table; the windows with no wall to hole, by model, glass number and reason; per model the
    /// holed walls' triangles.</summary>
    public List<GlassWalls.Wall> GlassHoled { get; } = [];
    public List<(int Model, int Number, string Why)> GlassWallSkipped { get; } = [];
    public List<(int Model, int Number, int Face)> GlassWallBlocked { get; } = [];
    public int[] GlassWallTris { get; private set; } = [];
    public int GlassWallFrames { get; private set; }
    /// <summary>How deep GLASS: SEE-THROUGH sinks a glass face, as a part of the window's smaller size. 0: the pane lies on the
    /// model's outer edge, where the original's window is; its edges then get a flat frame (GlassFrame).</summary>
    public const double GlassInset = 0;
    /// <summary>With the pane on the outer edge (GlassInset 0): how wide the flat frame round it is, as a part of the window's
    /// smaller size (at least one unit of the model's scale).</summary>
    public const double GlassFrame = 0.05;
    /// <summary>A window frame's colour: the panel colour round it one shade darker in its ramp (the sun shades it as the
    /// faces round it); black, grey and the sky stay.</summary>
    static int Darker(int colour) => colour switch { 2 or 3 or 4 or 13 or 14 or 10 or 11 => colour - 1, 5 => 6, _ => colour };   // ramps 1-4, 12-14, 9-11, 6-5 (PaintedLight.Families)
    /// <summary>Each model's back walls as baked (null: none) - CheckPieces places their extra points.</summary>
    public ModelCaps.Back?[] Backs { get; private set; } = [];
    public int TriSlots { get; private set; }
    bool logged;
    readonly byte[] rawObjfile;                                      // the file as the rules have it: glass face numbers (Glass.cs) count its filled faces
    readonly Dictionary<int, int[]> faceOrigins = [];                // per rebuilt model: each card face's face number in the file (round wheels split treads)
    readonly bool seaBodies;                                         // SHARKS AND SUBS: WHOLE (SeaBodies), set at launch

    /// <summary>interiors false: a card without the car and jeep interiors (CarInside) - CheckCarInside's reference picture.
    /// edit: a check's own change to the card's models (CheckStore's 300-corner ladder), given the store and the face origins.
    /// viewPalette: the view palette (bank 2, $0RGB words) - a window border's colour, when two body colours touch the window
    /// equally, is the darker of them; without it the first found.</summary>
    public GpuWorld(byte[] objfile, byte[] sinfile, byte[] terrainMap, byte[]? main = null, Hunter.Rules.FineGround? fine = null, bool interiors = true,
        Action<CardModels, Dictionary<int, int[]>>? edit = null, bool seaBodies = false, IReadOnlyList<ushort>? viewPalette = null)
    {
        this.viewPalette = viewPalette;
        PaintedLight.Forget();                                   // the last build's models (its counts doubled the startup line's)
        rawObjfile = objfile;
        this.seaBodies = seaBodies;
        Store = CardShapes(objfile, faceOrigins, interiors, seaBodies);
        if (edit is not null) { edit(Store, faceOrigins); Store.Layout(); }
        this.sinfile = sinfile;
        map = terrainMap;
        this.main = main;
        entTex = ImageTexture.CreateFromImage(Image.CreateEmpty(0xA0 * PerRow, TexRows, false, Image.Format.Rf));
        animTex = ImageTexture.CreateFromImage(Image.CreateEmpty(320, 2, false, Image.Format.Rf));   // SMOOTH PARTS: row 1 = the table one step back
        ground = new ShaderMaterial { Shader = GD.Load<Shader>("res://real3d/groundgpu.gdshader") };
        models = new ShaderMaterial { Shader = GD.Load<Shader>("res://real3d/gpumodels.gdshader") };
        glassFaces = new ShaderMaterial { Shader = GD.Load<Shader>("res://real3d/gpuglass.gdshader") };
        shadows = new ShaderMaterial { Shader = GD.Load<Shader>("res://real3d/gpushadow.gdshader") };
        shadows.SetShaderParameter("layer", 1);
        lines = new ShaderMaterial { Shader = GD.Load<Shader>("res://real3d/gpulines.gdshader") };
        dots = new ShaderMaterial { Shader = GD.Load<Shader>("res://real3d/gpupoints.gdshader") };
        debris = new ShaderMaterial { Shader = GD.Load<Shader>("res://real3d/gpudebris.gdshader") };
        var types = new byte[128 * 128];                             // the map's two bytes a point, as flat textures
        for (var i = 0; i < types.Length; i++) types[i] = terrainMap[i * 2 + 1];
        MapTex = HeightTexture.From(terrainMap);
        ground.SetShaderParameter("map", MapTex);
        if (fine is not null)                                         // ENHANCEMENT (FINER GROUND): every card reader of the
        {                                                             // ground (ground_height / ground_point) takes the store
            FineN = fine.N;
            FineTex = HeightTexture.From(fine);
            foreach (var m in Materials) { m.SetShaderParameter("fine_n", FineN); m.SetShaderParameter("fine_map", FineTex); }
        }
        typesTex = Bytes(128, 128, types);
        ground.SetShaderParameter("types", typesTex);
        ground.SetShaderParameter("band_green_h", GroundBands.Heights(terrainMap, false, fine));   // GROUND COLOURS: BANDS
        ground.SetShaderParameter("band_brown_h", GroundBands.Heights(terrainMap, true, fine));
        ground.SetShaderParameter("band_shore", Real3DView.CoastTexture(terrainMap));
        ground.SetShaderParameter("cuts", cuts);                 // GROUND: TRUE HEIGHT (pieces a triangle side)
        pieces.SetShaderParameter("types", typesTex);
        shadows.SetShaderParameter("shadow_types", typesTex);     // lighter shadows on the sea
        pieces.SetShaderParameter("blast_tex", blastTex);
        pieces.SetShaderParameter("blast_info", blastInfoTex);
        AddChild(groundMesh = new MeshInstance3D { Mesh = Empty(meshCorners = 128 * 128 * 2 * 3 * cuts * cuts), MaterialOverride = ground, CustomAabb = Huge });
        marks.SetShaderParameter("mark_tex", markTex);                // TRACK MARKS: two triangles an entry of the ring
        AddChild(new MeshInstance3D { Mesh = Empty(MarkRing * 6), MaterialOverride = marks, CustomAabb = Huge });
        mdlTex = ImageTexture.CreateFromImage(Image.CreateFromData(CardModels.Width, Store.Rows, false, Image.Format.Rgbaf, StoreBytes()));
        atTex = Store.AtTexture();
        dposTex = ImageTexture.CreateFromImage(Image.CreateEmpty(30, 1, false, Image.Format.Rf));
        debris.SetShaderParameter("dpos_tex", dposTex);
        var m0 = new Models(objfile);                                      // the debris model is the file's: no builder touches it
        model0 = m0.Offsets[0];
        var edges0 = model0 + 0x14 + m0.U(model0 + 0x12) * 4;
        faces0 = edges0 + 2 + m0.U(edges0) * 2 + 2;
        debris.SetShaderParameter("debris_base", model0);
        var trigTex = TrigTexture.From(sinfile);
        foreach (var m in new[] { models, glassFaces, shadows, lines, dots, debris, pieces })
        {
            m.SetShaderParameter("mdl_tex", mdlTex);
            m.SetShaderParameter("mdl_at", atTex);
            m.SetShaderParameter("trig_tex", trigTex);
            m.SetShaderParameter("ent_tex", entTex);
            m.SetShaderParameter("stored_tex", storedTex);          // the stored objects' records and in-view marks (place.gdshaderinc _eb)
            m.SetShaderParameter("stored_seen", seenTex);
            m.SetShaderParameter("draw_rows", drawTex);             // the stored objects the passes draw this frame (slot_row)
            m.SetShaderParameter("live_rows", LiveRows);
            m.SetShaderParameter("anim_tex", animTex);
            m.SetShaderParameter("entprev_tex", entprevTex);      // SMOOTH PARTS: record part bytes one step back
            m.SetShaderParameter("rotor_models", new Vector2I(Store.Models[0x17].Offset, Store.Models[0x19].Offset));   // FAST ROTORS: both helicopters
            m.SetShaderParameter("rotor_times", 8);
            m.SetShaderParameter("steer_models", CarInside.Models.Select(n => Store.Models[n].Offset + 1).ToArray());   // the steering wheels (CarInside), + 1: 0 = none
            m.SetShaderParameter("steer_turns", CarInside.Models.Select(CarInside.Turn).ToArray());
        }
        foreach (var m in new[] { models, glassFaces, shadows, lines })   // TANK TRACKS: the live table's materials (a blast's row is not a live row)
        {
            m.SetShaderParameter("track_on", true);
            m.SetShaderParameter("track_models", TankTracks.Models.Select(n => n < Store.Models.Length ? Store.Models[n].Offset + 1 : 0).ToArray());
            m.SetShaderParameter("roll_tex", rollTex);
        }
        var ownTex = OwnTintTable();                                       // MODEL COLOURS: the models and their pieces
        models.SetShaderParameter("own_tex", ownTex);
        glassFaces.SetShaderParameter("own_tex", ownTex);
        pieces.SetShaderParameter("own_tex", ownTex);
        lines.SetShaderParameter("own_tex", ownTex);                  // the window borders: a machine's paint as its faces
    }

    // STRAIGHT BLADES were spikes pinched to the hub; since 2026-11-12 the
    // blades are EVEN BLADES, rectangles rooted in the rotor head (RotorMast.Blades).

    // MODEL COLOURS (the A1200's colours; the sun shades the colour, so no shade is hand-picked): one tint a model. A face painted
    // in the greens (1-4, 12-14) takes the tint, keeping its own brightness - a machine's moving parts (blades, wheels)
    // keep their paint, a person's limbs are the person and take it; the sun then shades it smoothly in full colour, like
    // the ground (gpumodels.gdshader). Black, grey, reds, browns, yellow stay.
    bool glassShown;                                                 // the last glass sent (the shader's default is off)
    bool ownShown;                                                   // the last own_colours sent (the shader's default is off)
    // Base: a machine's one paint for all its greens (gpumodels_body: the sun alone shades its faces apart)
    // Glass, Opaque: a military machine's window tint - how bright its panes are (1 = the pale glass) and how much of the pane
    // shows over what is behind it (gpumodels_body GLASS_SEE 0.35 for everything else)
    static readonly (int Model, Color Tint, bool Machine, int Base, float Glass, float Opaque)[] OwnTints =   // the colour a unit of brightness becomes
    {
        (0x17, new Color(0, 0, 0), true, 2, 0.70f, 0.25f),               // helicopters: no tint - the original's own greens, face by face (the grey-green
        (0x19, new Color(0, 0, 0), true, 2, 0.70f, 0.25f),               // one-paint look was too far from it); their panes paler and clearer than the tanks': the pilot shows
        (0x15, new Color(0.85f, 1.00f, 0.60f), true, 3, 0.30f, 0.50f),   // tanks: khaki green, on the light green paint (14 drew them near black)
        (0x16, new Color(0.85f, 1.00f, 0.60f), true, 3, 0.30f, 0.50f),
        (0x02, new Color(0.80f, 1.10f, 0.98f), false, 0, 1f, 0.35f),     // enemy soldier: blue-green fatigues, arms and legs too
        (0x01, new Color(0.98f, 1.06f, 0.74f), false, 0, 1f, 0.35f),     // you and your comrades: olive; in disguise you wear model 2 (FootModels)
    };

    /// <summary>MODEL COLOURS as a 256 x 2 table: x = model; row 0 rgb = its tint, a = 1 a machine (its parts untinted), 0.5 not;
    /// row 1 r = a machine's one paint (Base), g = its panes' brightness (Glass), b = how much of a pane shows (Opaque).</summary>
    static ImageTexture OwnTintTable()
    {
        var img = Image.CreateEmpty(256, 2, false, Image.Format.Rgbaf);
        foreach (var (m, tint, machine, paint, glass, opaque) in OwnTints)
        {
            img.SetPixel(m, 0, new Color(tint.R, tint.G, tint.B, machine ? 1 : 0.5f));
            img.SetPixel(m, 1, new Color(paint, glass, opaque, 1));
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Which of a model's corners sit on a moving part: the walk of model.gdshaderinc, a flagged corner of kind 1
    /// (the pivot) or 2 starts a part, the unflagged corners after it are on it, any other flag ends it.</summary>
    /// <summary>The helicopters' greens are shading painted in for a fixed light - the tops the lightest, the sides darker,
    /// the tail between, each left and right pair alike - not paint; so are the three browns of the belly. In the 3D view
    /// the sun shades them, so every green face of the file's fixed body (not the rotors, not the cab we add) takes one
    /// green, PlainGreen, every brown one brown, PlainBrown, and only the sun makes them lighter or darker. Through
    /// recolour[], so the window borders read the same green. Returns the faces changed.</summary>
    int PlainGreens(List<(int Model, int Base, int N, int Meta, int Ordinal)> faces, int rows, int[] recolour)
    {
        var changed = 0;
        foreach (var model in PlainGreenModels)
        {
            var fm = Store.Models[model];
            var part = PartCorners(fm);
            for (var i = 0; i < rows; i++)
            {
                if (faces[i].Model != model || faces[i].Ordinal >= fm.Faces.Length || (faces[i].Meta & CarInside.Bit) != 0) continue;
                var colour = recolour[i] >= 0 ? recolour[i] : faces[i].Meta & 15;
                var plainColour = Array.IndexOf(Greens, colour) >= 0 ? PlainGreen : Array.IndexOf(Browns, colour) >= 0 ? PlainBrown : -1;
                if (plainColour < 0) continue;
                var onPart = true;                                           // every corner on a moving part: a rotor, left alone
                foreach (var e in fm.Faces[faces[i].Ordinal].Edges)
                    if (e < fm.Edges.Length && !(fm.Edges[e].A < part.Length && part[fm.Edges[e].A] && fm.Edges[e].B < part.Length && part[fm.Edges[e].B])) onPart = false;
                if (onPart) continue;
                recolour[i] = plainColour;
                changed++;
            }
        }
        return changed + PlainSkins(faces, rows, recolour);
    }

    /// <summary>The people's browns (skin, and some clothes and robes) have the same painted-in light: one piece - its
    /// brown faces joined by shared corners, a forearm, a face, a robe - in two or three shades of the ramp, darker to the
    /// front and lighter to the side. Such a piece takes the middle brown, PlainBrown, and the sun shades it; a piece in
    /// one brown is that paint and stays. Pieces, not the whole model, so a brown robe and the skin beside it keep apart.
    /// Returns the faces changed.</summary>
    int PlainSkins(List<(int Model, int Base, int N, int Meta, int Ordinal)> faces, int rows, int[] recolour)
    {
        var changed = 0;
        foreach (var model in PeopleModels)
        {
            if (model >= Store.Models.Length) continue;
            var fm = Store.Models[model];
            var rowsOf = new Dictionary<int, List<int>>();                  // face ordinal -> its rows (a face can have more than one)
            var shade = new Dictionary<int, int>();
            for (var i = 0; i < rows; i++)
            {
                if (faces[i].Model != model || faces[i].Ordinal >= fm.Faces.Length || (faces[i].Meta & CarInside.Bit) != 0) continue;
                var colour = recolour[i] >= 0 ? recolour[i] : faces[i].Meta & 15;
                if (Array.IndexOf(Browns, colour) < 0) continue;
                if (!rowsOf.TryGetValue(faces[i].Ordinal, out var list)) rowsOf[faces[i].Ordinal] = list = [];
                list.Add(i);
                shade[faces[i].Ordinal] = colour;
            }
            var ords = rowsOf.Keys.ToList();
            var cornersOf = ords.ToDictionary(o => o, o => fm.Faces[o].Edges.Where(e => e < fm.Edges.Length)
                .SelectMany(e => new[] { fm.Edges[e].A, fm.Edges[e].B }).ToHashSet());
            var piece = ords.ToDictionary(o => o, o => o);
            int Find(int o) { while (piece[o] != o) o = piece[o] = piece[piece[o]]; return o; }
            for (var a = 0; a < ords.Count; a++)
                for (var b = a + 1; b < ords.Count; b++)
                    if (cornersOf[ords[a]].Overlaps(cornersOf[ords[b]])) piece[Find(ords[a])] = Find(ords[b]);
            foreach (var g in ords.GroupBy(Find))
            {
                if (g.Select(o => shade[o]).Distinct().Count() < 2) continue;  // one brown: its paint
                foreach (var o in g)
                    foreach (var i in rowsOf[o]) { recolour[i] = PlainBrown; changed++; }
            }
        }
        return changed;
    }
    static readonly int[] PeopleModels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];   // you, the soldiers and the people (models.json's users)
    // the helicopters, and the gun emplacements and radars (portable radar, SAM launcher, rocket launcher, machine gun,
    // tracer gun, radar unit): their darkest greens shaded the sides away from the sun near black - a rocket launcher seen
    // end-on read as its shadow and its stand
    static readonly int[] PlainGreenModels = [0x17, 0x19, 82, 100, 101, 104, 106, 141];
    static readonly int[] Greens = [1, 2, 3, 4, 12, 13, 14];             // both green ramps (SunShade's families)
    const int PlainGreen = 2;                                            // the middle green: the sun takes it up the ramp or down
    static readonly int[] Browns = [9, 10, 11];
    const int PlainBrown = 10;                                           // the middle brown

    static bool[] PartCorners(CardModels.Model md)
    {
        var on = new bool[md.Corners.Length];
        var part = false;
        for (var k = 0; k < on.Length; k++)
        {
            var flag = md.Corners[k].Flag;
            if (flag != 0) part = ((flag >> 4) & 7) is 1 or 2 or 3 or 4;  // 3, 4: a belt part (TankTracks), card only
            on[k] = part;
        }
        return on;
    }

    /// <summary>A model's faces in file order: each one's number among the filled ones (kind 0 or 0x40, 3 or more edges;
    /// 0 up, as models.json's f), -1 for a line face. The numbering of docs/data/windows.md.</summary>
    static int[] FilledNumbers(byte[] obj, Models m, int model)
    {
        var a = m.Offsets[model];
        var ea = a + 0x14 + m.U(a + 0x12) * 4;
        var p = ea + 2 + m.U(ea) * 2 + 2;
        var list = new List<int>();
        var filled = 0;
        while ((sbyte)obj[p] >= 0)
        {
            var kind = obj[p + 1] & 0x70;
            list.Add((kind == 0 || kind == 0x40) && (obj[p + 1] & 15) >= 3 ? filled++ : -1);
            p += 2 + (obj[p + 1] & 15);
        }
        return list.ToArray();
    }

    /// <summary>The models the card draws (CardModels): the file's with the straight blades, the rotor masts (RotorMast), the tank tracks
    /// (TankTracks), the round wheels (RoundWheels) and the car and jeep interiors (CarInside) built into them; the rules keep their objfile. origins: each rebuilt model's face numbers in the file.</summary>
    public static CardModels CardShapes(byte[] objfile, Dictionary<int, int[]>? origins = null, bool interiors = true, bool seaBodies = false)
    {
        var store = new CardModels(objfile);
        StraightBlades(store);
        RotorMast.Make(store, origins);                                 // ROTOR MASTS: the helicopters' blades joined to the body
        TankTracks.Make(store, origins);                                // TANK TRACKS: road wheels and moving links
        RoundWheels.Make(store, null, origins);
        TankTracks.Chains(store, origins);                              // the bicycles' chains: the tank's belt part, after their rims are round
        if (seaBodies) SeaBodies.Make(store, origins);                  // SHARKS AND SUBS: WHOLE - the shark's and the submarine's bodies
        if (interiors) CarInside.Make(store, null, origins);
        if (interiors) Fireball.Make(store, origins);                   // BETTER EXPLOSIONS: the blasts' fireball cores (shown with PIECES)
        return store;
    }

    /// <summary>TANK TRACKS: each live tank's rolling (rollFloats, 256ths of its wheel's turn, kept in 0..256) moved on by how far its
    /// record went since the last picture along its heading (+$7E: 0 = +z, $40 = +x; back counts back), in model units (x and z
    /// twice the world's, Place $41FC) over the wheel's round. A new thing in a row starts at 0; a jump (into a building, a
    /// respawn: Play.Smooth's limit) adds nothing; turning on the spot adds nothing (the original's tanks turn standing).</summary>
    void TrackRoll(EntityTable entities, float clock)
    {
        for (var r = 0; r < LiveRows; r++)
        {
            var e = r < entities.Count ? entities.All[r] : null;
            var radius = e is not null && e.B[0] != 0 && (sbyte)e.B[0x24] >= 0 ? TankTracks.Radius(e.B[0x25]) : 0;
            if (radius == 0)
            {
                if (rollFloats[r] != 0) { rollFloats[r] = 0; rollDirty = true; }
                rollModel[r] = -1;
                continue;
            }
            int x = e!.UW(0x4A), z = e.UW(0x4E);
            if (rollModel[r] != e.B[0x25])
            {
                (rollModel[r], rollX[r], rollZ[r]) = (e.B[0x25], x, z);
                if (rollFloats[r] != 0) { rollFloats[r] = 0; rollDirty = true; }
                markGone[r] = 0;
                continue;
            }
            int dx = (short)(x - rollX[r]), dz = (short)(z - rollZ[r]);
            (rollX[r], rollZ[r]) = (x, z);
            if (Math.Abs(dx) > 0x800 || Math.Abs(dz) > 0x800) { markGone[r] = 0; continue; }
            if (dx == 0 && dz == 0) continue;
            var h = e.B[0x7E] * Math.PI / 128;
            var speed = Math.Clamp((int)(short)e.UW(0x3E), 0, 255);
            // marks and clods only where the tank DROVE: a move of more than MarkJump in one picture is the thing being put
            // somewhere (a new place for a boarded tank, a respawn), not driving - no line of bars along the jump - and a
            // tank moved while it stands (speed 0) throws no earth
            if (Math.Abs(dx) > MarkJump || Math.Abs(dz) > MarkJump) markGone[r] = clodGone[r] = 0;
            else
            {
                TreadMarks(r, x, z, dx, dz, h, clock, e.B[0x25]);
                if (speed > 0) ClodThrows(r, x, z, h, clock, e.B[0x25], speed, Math.Sqrt((double)dx * dx + (double)dz * dz));
            }
            var along = dx * Math.Sin(h) + dz * Math.Cos(h);
            var d = 2 * Math.Sqrt((double)dx * dx + (double)dz * dz) * Math.Sign(along);
            var v = (rollFloats[r] + d / (2 * Math.PI * radius) * 256) % 256;
            rollFloats[r] = (float)(v < 0 ? v + 256 : v);
            rollDirty = true;
            if (MarkLog) GD.Print($"TRACKROLL row {r} moved {d:F2} model units, roll {rollFloats[r]:F2} of 256 (radius {radius})");
        }
    }

    /// <summary>TRACK MARKS: a tread bar under each track, where the track comes up off the ground (behind the tread going
    /// forward, ahead of it going back: TankTracks.Tread's Rear / Front - not under the tank's own place, in its middle),
    /// each time a tank has gone one link pitch (bar and gap
    /// each half a pitch long, so they alternate along the line), placed at the exact spot along this move where the pitch was
    /// crossed, so the bars lie evenly however far a frame moves. Model x and z are twice the world's (Place $41FC).</summary>
    void TreadMarks(int r, int x, int z, int dx, int dz, double h, float clock, int model)
    {
        if (TankTracks.Tread(model) is not { } t) return;
        var step = t.Pitch / 2.0;
        var moved = Math.Sqrt((double)dx * dx + (double)dz * dz);
        var gone = markGone[r] + moved;
        var (ax, az) = (Math.Cos(h), -Math.Sin(h));                  // across the tank (forward is sin h, cos h)
        for (; gone >= step; gone -= step)
        {
            var back = (gone - step) / moved;                        // the pitch was crossed this share of the move back
            var end = (dx * Math.Sin(h) + dz * Math.Cos(h) >= 0 ? t.Rear : t.Front) / 2.0;   // along the tank, world units (forward is sin h, cos h)
            double px = x - dx * back + Math.Sin(h) * end, pz = z - dz * back + Math.Cos(h) * end;
            foreach (var side in new[] { -1, 1 })
                NoteMark(px + side * ax * t.Centre / 2.0, pz + side * az * t.Centre / 2.0, h, clock, MarkTread, t.Width / 4.0, t.Pitch / 8.0,
                    MarkLog ? $"row {r} side {side} tank {x} {z} heading {h:F4}" : null);
        }
        markGone[r] = gone;
    }

    // CLODS: the lumps of earth a moving tank's tracks throw, noted in the mark ring as kind 2 and flown, landed and left
    // lying by gputracks.gdshader from that note alone. Thrown by the DISTANCE the track has gone, not by the clock: one a
    // track every two of its ribs (TankTracks.ClodEvery) - the faster the tank, the more earth; a crawl throws little.
    const int MarkClod = 2;
    readonly double[] clodGone = new double[256];        // each live row's way since its last throw, world units
    readonly int[] clodCount = new int[256];             // each live row's throws so far: which rest spot, which colour

    /// <summary>CLODS: one clod a track each time a moving tank's track has gone two ribs' pitches (moved: this picture's way,
    /// world units), thrown from its next rest spot
    /// (TankTracks.ClodSpots: most on the track's rear run, every fourth inside its top rear corner), turned into the world
    /// by the tank's place and heading (model units twice the world's, Place $41FC). Its colour is fixed now: every second
    /// clod of a track brown (9 and 10 in turn), the others the colour of the land square under the tank - the low nibble of
    /// its type byte - or brown on sea or on land that is already brown.</summary>
    void ClodThrows(int r, int x, int z, double h, float clock, int model, int speed, double moved)
    {
        if (TankTracks.ClodSpots(model) is not { } spots) return;
        var every = TankTracks.ClodEvery(model);
        if (every <= 0) return;
        clodGone[r] += moved;
        var (fx, fz, ax, az) = (Math.Sin(h), Math.Cos(h), Math.Cos(h), -Math.Sin(h));
        var land = map[(((z & 0xFFFF) >> 9) * 128 + ((x & 0xFFFF) >> 9)) * 2 + 1] & 15;
        for (; clodGone[r] >= every; clodGone[r] -= every)
        {
            var n = clodCount[r]++;
            var (sx, sy, sz) = spots[n % spots.Length];
            foreach (var side in new[] { 1, -1 })
            {
                var odd = (n + (side < 0 ? 1 : 0)) % 2 != 0;
                var colour = odd && land is not (0 or 9 or 10) ? land : 9 + n / 2 % 2;
                NoteMark(x + (side * sx * ax + sz * fx) / 2.0, z + (side * sx * az + sz * fz) / 2.0, h, clock, MarkClod, sy / 2.0, speed,
                    MarkLog ? $"CLOD row {r} side {side} n {n} start {sy / 2.0:F1} speed {speed} colour {colour} land {land} entry {markNext}" : null, colour);
            }
        }
    }

    /// <summary>TRACK MARKS: one entry into the ring at a world spot, none on a sea square (type 0, as gpushadow.gdshader's
    /// shadow_types). The ring's oldest entry is written over. A clod's entry carries its palette colour in the spare.</summary>
    void NoteMark(double x, double z, double h, float clock, int kind, double halfW, double halfL, string? log, float spare = 0)
    {
        (x, z) = ((x % 65536 + 65536) % 65536, (z % 65536 + 65536) % 65536);
        var type = map[(((int)z >> 9) * 128 + ((int)x >> 9)) * 2 + 1];
        if (log is not null) GD.Print($"TRACKMARK {log} mark {x:F1} {z:F1} clock {clock:F1} type {type}{(type == 0 ? " SEA none" : "")}");
        if (type == 0) return;
        var (a, b) = (markNext * 4, (MarkRing + markNext) * 4);
        (markFloats[a], markFloats[a + 1], markFloats[a + 2], markFloats[a + 3]) = ((float)x, (float)z, (float)h, clock);
        (markFloats[b], markFloats[b + 1], markFloats[b + 2], markFloats[b + 3]) = (kind, (float)halfW, (float)halfL, spare);
        markNext = (markNext + 1) % MarkRing;
        markDirty = true;
    }

    /// <summary>The helicopters' even blades into the models (RotorMast.Blades); the rules keep theirs.</summary>
    public static void StraightBlades(CardModels store) => RotorMast.Blades(store);

    internal static readonly Aabb Huge =new(new Vector3(-1e6f, -1e6f, -1e6f), new Vector3(2e6f, 2e6f, 2e6f));

    int cuts = 1;                                                // GROUND: TRUE HEIGHT: pieces per drawn triangle's side (groundgpu.gdshader)
    MeshInstance3D groundMesh;

    /// <summary>GROUND PATTERN: SPECKS on or off (the specks are a hash in the ground shader).</summary>
    public void SetGroundPattern(bool on) => ground.SetShaderParameter("ground_pattern", on);
    public void SetGroundColours(bool on) => ground.SetShaderParameter("ground_bands", on);

    int wantCuts = 1, meshCorners;
    (int X, int Z, int Cols, int Rows) win = (0, 0, 128, 128);

    /// <summary>Cut every drawn ground triangle into cuts x cuts small triangles whose corners take the game's own
    /// height (Ground.Height, groundgpu.gdshader). Default 1 = the flat triangle, exactly as before. Only whole-world
    /// lattice cuts are allowed: cuts must divide 512. Applied in Update with the view's cells (Fit).</summary>
    public void SetCuts(int value)
    {
        if (value >= 1 && 512 % value == 0) wantCuts = value;
    }

    /// <summary>GROUND SHAPE over the 3D view distance: the mesh covers only the rectangle of cells the distance draws
    /// (ViewDistance.Cells), so every drawn square can be cut fine at the cost of a few hundred squares, not the map's
    /// 16384. The whole map (ALL, or indoors where the ground is the backdrop) stays at 8 cuts at most. The mesh only
    /// grows: corners past the rectangle draw nothing.</summary>
    void Fit(bool some)
    {
        var rect = (X: 0, Z: 0, Cols: 128, Rows: 128);
        if (some)
        {
            int c0 = 128, c1 = -1, r0 = 128, r1 = -1;
            for (var i = 0; i < viewMask.Length; i++)
                if (viewMask[i] != 0) { int c = i & 127, r = i >> 7; c0 = Math.Min(c0, c); c1 = Math.Max(c1, c); r0 = Math.Min(r0, r); r1 = Math.Max(r1, r); }
            if (c1 >= 0) rect = (c0, r0, c1 - c0 + 1, r1 - r0 + 1);
        }
        var n = rect.Cols * rect.Rows == 128 * 128 ? Math.Min(wantCuts, 8) : wantCuts;
        if (n != cuts) { cuts = n; ground.SetShaderParameter("cuts", cuts); }
        if (rect != win)
        {
            win = rect;
            ground.SetShaderParameter("win_origin", new Vector2I(rect.X, rect.Z));
            ground.SetShaderParameter("win_size", new Vector2I(rect.Cols, rect.Rows));
        }
        var need = rect.Cols * rect.Rows * 2 * 3 * cuts * cuts;
        if (need > meshCorners || need < meshCorners / 4) { meshCorners = need; groundMesh.Mesh = Empty(need); }   // shrink too when far too big (a lower distance)
    }

    static ImageTexture Bytes(int w, int h, byte[] v)
    {
        var f = new float[w * h];
        for (var i = 0; i < f.Length && i < v.Length; i++) f[i] = v[i];
        var b = new byte[f.Length * 4];
        Buffer.BlockCopy(f, 0, b, 0, b.Length);
        return ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rf, b));
    }

    byte[] StoreBytes()                                      // the store's texels as the texture takes them (one buffer kept)
    {
        if (mdlBytes.Length != Store.Rows * CardModels.Width * 16) mdlBytes = new byte[Store.Rows * CardModels.Width * 16];
        Buffer.BlockCopy(Store.Texels, 0, mdlBytes, 0, mdlBytes.Length);
        return mdlBytes;
    }

    // A frame's picture to the card, then freed: left to the collector, dozens were still alive at exit and the
    // engine's shutdown crashed on them.
    static void Put(ImageTexture t, Image img)
    {
        t.Update(img);
        img.Dispose();
    }

    internal static ArrayMesh Empty(int vertices, Mesh.PrimitiveType kind = Mesh.PrimitiveType.Triangles)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[vertices];
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(kind, arrays);
        mesh.CustomAabb = Huge;
        return mesh;
    }

    /// <summary>The slots for rows of perRow corners each: ONE row's corners drawn once a row (instancing), not every row's
    /// corners in one buffer. The corners hold nothing (the shaders place them from VERTEX_ID and INSTANCE_ID, the row), so
    /// the same room costs one row's buffer, not 16523 rows' (the model slots were 272 MB). Every instance stands where the
    /// mesh does (identity): the shaders' MODELVIEW_MATRIX takes it in.</summary>
    internal static MultiMesh SlotRows(int perRow, int rows)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = Empty(perRow), InstanceCount = rows, CustomAabb = Huge };
        for (var i = 0; i < rows; i++) mm.SetInstanceTransform(i, Transform3D.Identity);
        return mm;
    }

    // The bake: row = one filled face, two pixels a triangle t (px 2t, 2t+1): the three corner numbers' high bytes, then their low bytes
    // (16 bits each, 8-bit colour channels /255); no triangle = both pixels white (high bytes 255,255,255 - no real corner number is that high).
    /// <summary>The bake's picture: two pixels a triangle (high and low bytes of its corners), as many triangles as the biggest face
    /// can have (CardModels.MaxFaceCorners - 2).</summary>
    const int BakeWidth = 2 * (CardModels.MaxFaceCorners - 2);

    const string BakeShader = """
        shader_type canvas_item;
        render_mode blend_disabled;
        #include "res://real3d/model.gdshaderinc"
        #include "res://real3d/face.gdshaderinc"
        #include "res://real3d/slots.gdshaderinc"
        uniform sampler2D inputs;
        uniform sampler2D trig_tex;

        void fragment() {
            ivec2 fc = ivec2(FRAGCOORD.xy);
            vec4 a = texelFetch(inputs, ivec2(0, fc.y), 0);
            vec4 b = texelFetch(inputs, ivec2(1, fc.y), 0);
            int at = int(round(a.g)), face = int(round(a.b));
            int n = int(round(b.r));
            ivec3 t = face_triangle(trig_tex, at, face, n, fc.x >> 1);
            COLOR = vec4(1.0, 1.0, 1.0, 1.0);
            if (t.x >= 0) {
                ivec3 c = ivec3(face_corner(at, face, n, t.x).y, face_corner(at, face, n, t.y).y, face_corner(at, face, n, t.z).y);
                COLOR = vec4(vec3((fc.x & 1) == 0 ? c >> 8 : c & ivec3(255)) / 255.0, 1.0);
            }
        }
        """;

    public override async void _Ready()
    {
        try { await Bake(); }
        catch (Exception e) { GD.PushError("GpuWorld bake failed: " + e); }
    }

    async System.Threading.Tasks.Task Bake()
    {
        var nModels = Store.Models.Length;
        var faces = new List<(int Model, int Base, int N, int Meta, int Ordinal)>();   // Base: the model's key (its offset in the file)
        var lineFaces = new List<(int Model, int A, int B, int Colour)>();   // line faces ($7BF2): each listed edge, a segment
        var rawModels = new Models(rawObjfile);
        FilledFaces = new int[nModels];
        GlassFaces = new int[nModels];
        GlassSunkFaces = new int[nModels]; GlassSunkEdges = new int[nModels]; GlassSunkTris = new int[nModels];
        GlassFrameTris = new int[nModels]; GlassFrameOwn = new int[nModels]; GlassBorders = new int[nModels];
        var glassNumber = new Dictionary<(int, int), int>();              // a glass face's number (windows.md), by model and ordinal
        for (var model = 0; model < nModels; model++)
        {
            var md = Store.Models[model];
            var ordinal = 0;
            var filled = 0;                                               // the card's filled faces so far (CheckGlass)
            var number = model < rawModels.Offsets.Count ? FilledNumbers(rawObjfile, rawModels, model)   // each file face's number among the filled ones (docs/data/windows.md)
                : [.. Enumerable.Range(0, md.Faces.Length)];              // a model past the objfile's (CardModels.AppendCopy) - no glass to number
            faceOrigins.TryGetValue(model, out var origin);
            foreach (var face in md.Faces)
            {
                var flags = face.Flags;
                var n = face.Edges.Length;
                var kind = flags & 0x70;
                if ((kind == 0 || kind == 0x40) && n >= 3)
                {
                    // bit 12: a glass face (Glass.cs)
                    filled++;
                    var from = origin?[ordinal] ?? ordinal;                   // its face in the file; -1: one the card added (CarInside), no glass, seen only with SEE-THROUGH
                    var glass = from >= 0 && Glass.Is(model, number[from]);
                    var core = from == Fireball.Origin;                       // BETTER EXPLOSIONS: a fireball core's face (Fireball), drawn only with PIECES
                    var mast = from == RotorMast.Origin || from == TankTracks.Origin;   // ROTOR MASTS, TANK TRACKS: plain faces of the body, no glass, always drawn
                    faces.Add((model, md.Offset, n, (face.Colour & 15) | ((flags & 0x80) != 0 ? 16 : 0) | (glass ? Glass.Bit : 0) | (core ? Fireball.Bit : 0)
                        | (from < 0 && !core && !mast ? Glass.Sunk | CarInside.Bit : 0)
                        | (from <= -2 && !core && !mast ? CarInside.Driver | (-from - 2) << 18 : 0), ordinal));   // -(2 + red): a driver's face (CarInside.Driver)
                    FilledFaces[model] = filled;
                    if (glass) { GlassFaces[model]++; glassNumber[(model, ordinal)] = number[from]; }
                }
                else if (kind != 0 && kind != 0x40)
                    foreach (var e in face.Edges) lineFaces.Add((model, md.Edges[e].A, md.Edges[e].B, face.Colour & 15));
                ordinal++;
            }
        }
        // RODS: a line standing free of every face is a thin solid rod (triangles, so it takes the sun and throws a
        // shadow); the rest are markings on a face and stay flat lines (gpulines.gdshader)
        var rod = FindRods(Store, faces, lineFaces, out var lineThird);
        var segments = new List<Color>();                         // the flat lines: corner a, corner b, colour, -(1 + a third corner of its face) or 0
        var lineStart = new int[nModels];
        var lineCount = new int[nModels];
        for (int m = 0, l = 0; m < nModels; m++)
        {
            lineStart[m] = segments.Count;
            for (; l < lineFaces.Count && lineFaces[l].Model == m; l++)
                if (!rod[l]) { segments.Add(new Color(lineFaces[l].A, lineFaces[l].B, lineFaces[l].Colour, lineThird[l] < 0 ? 0 : -(1 + lineThird[l]))); lineCount[m]++; }
        }
        var rodsOf = Enumerable.Range(0, lineFaces.Count).Where(l => rod[l]).ToLookup(l => lineFaces[l].Model);
        var borders = new List<Color>?[nModels];                 // each model's window borders (GLASS: SEE-THROUGH), joined to its lines after the faces
        var borderEdges = new HashSet<(int, int)>?[nModels];
        // BACK WALLS (ModelCaps, back from the retired 3D view:): each model's open sides mirrored and open bottom floored, as faces on extra points the
        // objfile never held. A back triangle's corner numbers run on from the model's own corner count (no
        // real face points past the last corner), their places in extra_tex, one row a model. Careful - only side mirrors and a
        // floor, the front waits for hand-made - are ModelCaps.Careful's models.
        var careful = ModelCaps.Careful(main, nModels);
        var backs = new ModelCaps.Back?[nModels];
        var bare = CardShapes(rawObjfile, interiors: false, seaBodies: seaBodies);   // the back walls close the model's own shell: not an interior's open boxes
        for (var m = 0; m < nModels; m++) backs[m] = m >= bare.Models.Length || (seaBodies && SeaBodies.Closed(m)) ? null : ModelCaps.For(bare.Models[m], careful[m], m);   // the sea bodies are closed already; any added model comes after the bare file's
        var extra = Image.CreateEmpty(Math.Max(1, backs.Max(bk => bk is null ? 0 : bk.Points.Length / 3)), nModels, false, Image.Format.Rgbaf);   // as wide as the most extra points a model has
        for (var m = 0; m < nModels; m++)
            if (backs[m] is { } bk)
                for (var j = 0; j < bk.Points.Length / 3; j++)
                    extra.SetPixel(j, m, new Color(bk.Points[j * 3], bk.Points[j * 3 + 1], bk.Points[j * 3 + 2], 0));
        var extraTex = ImageTexture.CreateFromImage(extra);
        Backs = backs;
        int backsAuto = 0, backsHand = 0;                     // the startup line's counts
        var slab = new bool[faces.Count];                    // THIN SHEETS: the rows Bake copies away from
        var slabOff = new Color[faces.Count];                // their plane, and how far (FindSheets below)
        var twin = new int[faces.Count];
        var recolour = new int[faces.Count];
        Array.Fill(twin, -1);
        Array.Fill(recolour, -1);
        var found = FindSheets(Store, faces, slab, slabOff, twin, recolour);
        var sticker = new bool[faces.Count];                 // STICKERS: drawn only as a copy moved onto the face under them
        var stickers = FindStickers(Store, faces, sticker, slab, slabOff);
        // BACK TO BACK: OFF. The original has no front-only faces: its filler ($7800) swaps a span's ends when they come out
        // reversed ($7906), so it fills a face whichever way it looks, and a model's faces paint over one another in file
        // order from either side. Drawing FindBacks' pairs from their front only showed the wrong one of some (the wind
        // surfer's sail turned from yellow to grey); the faces of one plane keep the later-is-nearer order instead.
        var frontOnly = new bool[faces.Count];
        // GLASS: SEE-THROUGH, a window painted flat on a wall: the wall to be drawn with a hole (GlassWalls)
        var notPlain = new bool[faces.Count];
        for (var i = 0; i < faces.Count; i++) notPlain[i] = slab[i] || twin[i] >= 0 || sticker[i];
        var flatWalls = GlassWalls.Find(Store, faces, notPlain, glassNumber);
        GlassWallSkipped.AddRange(flatWalls.Skipped);
        GlassWallBlocked.AddRange(flatWalls.Blocked);
        GlassWallTris = new int[nModels];
        int slabFlat = 0;                                   // faces FindSheets marked but the plate test left flat
        var rows = faces.Count;
        var inputs = Image.CreateEmpty(2, rows, false, Image.Format.Rgbaf);
        for (var i = 0; i < rows; i++)
        {
            var f = faces[i];
            inputs.SetPixel(0, i, new Color(f.Base, Store.AtOf(f.Base), f.Ordinal, 0));
            inputs.SetPixel(1, i, new Color(f.N, 0, 0, 0));
        }
        var vp = new SubViewport { Size = new Vector2I(BakeWidth, rows), Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Once };
        AddChild(vp);
        var mat = new ShaderMaterial { Shader = new Shader { Code = BakeShader } };
        mat.SetShaderParameter("inputs", ImageTexture.CreateFromImage(inputs));
        mat.SetShaderParameter("mdl_tex", mdlTex);
        mat.SetShaderParameter("mdl_at", atTex);
        mat.SetShaderParameter("trig_tex", TrigTexture.From(sinfile));
        vp.AddChild(new ColorRect { Size = new Vector2I(BakeWidth, rows), Material = mat });
        await ToSignal(GetTree(), "process_frame");
        await ToSignal(GetTree(), "process_frame");
        var img = vp.GetTexture().GetImage();
        vp.QueueFree();

        var tris = new List<Color>();
        var triOff = new List<Color>();                           // per triangle: a slab's offset, x 2 of the table
        var panels = new List<Color>();                                     // per triangle: its face's first triangle and count (pieces.gdshaderinc)
        var info = Image.CreateEmpty(256, 3, false, Image.Format.Rgbaf);       // row 0: triangles, row 1: line segments, row 2: the centre
        var start = new int[nModels];
        var count = new int[nModels];
        var backCount = new int[nModels];                     // the back walls' triangles, last in the run
        for (var i = 0; i < nModels; i++) start[i] = -1;
        var triOff2 = new Dictionary<int, Color>();                       // a rod triangle's second offset, x 3 of the table
        // a triangle's row: plain corner numbers and meta in x 0, the slab's offset in x 2 with the corners' marks in its alpha
        // (Marks) - the one place the two are written together
        void AddTri(int a, int b, int c, int meta, Color off, int marks = 0)
        {
            tris.Add(new Color(a, b, c, meta));
            triOff.Add(new Color(off.R, off.G, off.B, marks));
        }
        // the model's rods, 12 triangles each (RodTriangles), after its faces and before its back walls
        void AddRods(int m)
        {
            foreach (var l in rodsOf[m])
            {
                var first = tris.Count;
                var (corners, d1, d2) = RodTriangles(Store.Models[m], lineFaces[l].A, lineFaces[l].B, lineFaces[l].Colour);
                foreach (var (c, marks) in corners)
                {
                    triOff2[tris.Count] = d2;
                    AddTri((int)c.R, (int)c.G, (int)c.B, (int)c.A, d1, marks);
                    count[m]++;
                }
                for (var t = first; t < tris.Count; t++) panels.Add(new Color(first, tris.Count - first, 0, 0));
            }
        }
        // a holed wall (GlassWalls): its own triangles (first..own, just made) get bit 15 - hidden with SEE-THROUGH - and its holed
        // triangles follow, the wall's meta | Glass.Sunk (drawn only with SEE-THROUGH, no shadow, no piece), turned as its own are
        void HoleWall(GlassWalls.Wall w, int first, int own)
        {
            GlassHoled.Add(w);
            var wm = Store.Models[w.Model];
            double[] Pt(int k) => [wm.Corners[k].X, wm.Corners[k].Y, wm.Corners[k].Z];
            var n = Plane(w.Outer.Select(k => (wm.Corners[k].X, wm.Corners[k].Y, wm.Corners[k].Z)).ToList());
            var meta = (int)tris[first].A;
            var turn = 0.0;
            for (var t = first; t < own; t++)
            {
                if (((int)tris[t].A & (32 | 512)) != 0) { w.Why = "a plate or a moving part"; return; }
                if (turn == 0 && n is { } nn) turn = GlassWalls.Turn(((int)tris[t].R, (int)tris[t].G, (int)tris[t].B), Pt, [nn.Item1, nn.Item2, nn.Item3]);
            }
            if (own == first || turn == 0) { w.Why = "no own triangle with a turn"; return; }
            var flip = turn < 0;
            for (var t = first; t < own; t++) tris[t] = new Color(tris[t].R, tris[t].G, tris[t].B, (int)tris[t].A | GlassWalls.HiddenBit);
            w.First = tris.Count;
            w.Count = w.Tris.Count;
            var panel = new Color(w.First, w.Count, 0, 0);
            foreach (var (a, b, c) in w.Tris)
            {
                AddTri(a, flip ? c : b, flip ? b : c, (meta & ~(GlassWalls.HiddenBit | Glass.Replaced | Glass.Bit)) | Glass.Sunk, default);
                panels.Add(panel);
                count[w.Model]++;
                GlassWallTris[w.Model]++;
            }
        }
        var frontWay = new (int, int, int)?[faces.Count];     // a front-only face's own direction at rest (its winding's)
        for (var i = 0; i < faces.Count; i++)
            if (frontOnly[i]) frontWay[i] = Plane(WoundPoints(Store, faces[i]));
        GD.Print($"GpuWorld back to back: {frontOnly.Count(v => v)} faces drawn from their front only");
        var plain = PlainGreens(faces, rows, recolour);
        GD.Print($"GpuWorld plain paint: {plain} faces whose painted-in light went (the helicopters' greens and browns, the people's two- and three-shade browns), shaded by the sun alone");        for (var i = 0; i < rows; i++)
        {
            var f = faces[i];
            if (recolour[i] >= 0) f.Meta = (f.Meta & ~15) | recolour[i];
            if (start[f.Model] < 0) start[f.Model] = tris.Count;
            var first = tris.Count;
            var low = int.MaxValue;                                             // the model's lowest corner at rest
            var fm = Store.Models[f.Model];
            int shift = fm.Shift, corners = fm.Corners.Length;
            int RestY(int k) => fm.Corners[k].Y;
            for (var k = 0; k < corners; k++) low = Math.Min(low, RestY(k));
            var part = PartCorners(fm);
            for (var t = 0; t < CardModels.MaxFaceCorners - 2; t++)
            {
                var hi = img.GetPixel(t * 2, i);
                if (hi.R8 == 255 && hi.G8 == 255 && hi.B8 == 255) break;
                var lo = img.GetPixel(t * 2 + 1, i);
                int c0 = hi.R8 * 256 + lo.R8, c1 = hi.G8 * 256 + lo.G8, c2 = hi.B8 * 256 + lo.B8;
                // bit 5: a ground plate - black, all three corners on the lowest level (gpumodels.gdshaderinc)
                var plate = (f.Meta & 15) == 0 && RestY(c0) == low && RestY(c1) == low && RestY(c2) == low;
                // bit 9: all three corners on a moving part - it keeps its paint (MODEL COLOURS)
                var moving = part[c0] && part[c1] && part[c2];
                // bit 10: its paint has the painters' light in it (PaintedLight) - the sun lights it against their sun
                var painted = recolour[i] < 0 && !moving && PaintedLight.Painted(fm, f.Ordinal);
                var front = 0;
                if (frontOnly[i] && c0 < corners && c1 < corners && c2 < corners && frontWay[i] is { } way)
                {
                    // the triangle wound as its face is, so the card can tell the face's front from its back
                    var (p0, p1, p2) = (fm.Raw(c0), fm.Raw(c1), fm.Raw(c2));
                    long tx = (long)(p1.Item2 - p0.Item2) * (p2.Item3 - p0.Item3) - (long)(p1.Item3 - p0.Item3) * (p2.Item2 - p0.Item2),
                         ty = (long)(p1.Item3 - p0.Item3) * (p2.Item1 - p0.Item1) - (long)(p1.Item1 - p0.Item1) * (p2.Item3 - p0.Item3),
                         tz = (long)(p1.Item1 - p0.Item1) * (p2.Item2 - p0.Item2) - (long)(p1.Item2 - p0.Item2) * (p2.Item1 - p0.Item1);
                    if (tx * way.Item1 + ty * way.Item2 + tz * way.Item3 < 0) (c1, c2) = (c2, c1);
                    front = FrontOnly;
                }
                AddTri(c0, c1, c2, f.Meta | (plate ? 32 : 0) | (moving ? 512 : 0) | (painted ? 1024 : 0) | front, default);
                count[f.Model]++;
            }
            var own = tris.Count;
            // a ground plate is a shadow painted into the model, not material: a face made only of
            // plates stays flat, or its slab's sides would show on the thing itself
            var material = false;
            for (var t = first; t < own; t++) material |= ((int)tris[t].A & 32) == 0;
            // a slab's two faces each turned to their own side for the sun (meta bit 7): the face at rest
            // towards +offset, the moved copy (the corner less the offset) away from it
            Color Side(Color t, Color d)
            {
                (int, int, int) P(float k) => fm.Raw((int)k);
                var (a, b, c) = (P(t.R), P(t.G), P(t.B));
                var n = Plane([a, b, c]) ?? (0, 0, 0);
                var flip = n.Item1 * d.R + n.Item2 * d.G + n.Item3 * d.B < 0;
                return new Color(t.R, flip ? t.B : t.G, flip ? t.G : t.B, (int)t.A | 0x80);
            }
            if (twin[i] >= 0 && !slab[i])
                for (var t = first; t < own; t++) tris[t] = Side(tris[t], slabOff[twin[i]]);
            if (slab[i] && !material) slabFlat++;
            else if (slab[i])
            {
                // the slab: the face's own triangles again with every corner marked Moved (x 2's alpha)
                // and meta bit 6, the offset in x 2 of the table, then a side quad (2 triangles) an edge
                var meta = f.Meta | 0x40;
                var d = slabOff[i];
                var sideMeta = meta & ~Glass.Bit;                              // the slab's side quads are not glass
                int Meta(int a, int b) => sideMeta | (part[a] && part[b] ? 512 : 0);
                var away = new Color(-d.R, -d.G, -d.B, 0);
                for (var t = first; t < own; t++)
                {
                    var sd = Side(new Color(tris[t].R, tris[t].G, tris[t].B, (int)tris[t].A | 0x40), away);   // Side may swap the winding, the marks are the same on all three
                    AddTri((int)sd.R, (int)sd.G, (int)sd.B, (int)sd.A, d, Marks(Moved, Moved, Moved));
                    count[f.Model]++;
                    tris[t] = Side(tris[t], d);
                }
                foreach (var ei in fm.Faces[f.Ordinal].Edges)
                {
                    var a2 = fm.Edges[ei].A;
                    var b2 = fm.Edges[ei].B;
                    AddTri(a2, b2, b2, Meta(a2, b2), slabOff[i], Marks(0, 0, Moved));
                    AddTri(a2, b2, a2, Meta(a2, b2), slabOff[i], Marks(0, Moved, Moved));
                    count[f.Model] += 2;
                }
                if (twin[i] >= 0 || sticker[i])
                {
                    // a pair's moved face is only its moved copy: at rest it would lie on its twin and win there
                    // (a sticker's resting face lies on or behind the face it is stuck to)
                    tris.RemoveRange(first, own - first);
                    triOff.RemoveRange(first, own - first);
                    count[f.Model] -= own - first;
                }
            }
            for (var t = first; t < tris.Count; t++) panels.Add(new Color(first, tris.Count - first, 0, 0));
            if (flatWalls.Walls.TryGetValue(i, out var holed)) HoleWall(holed, first, own);
            if ((f.Meta & Glass.Bit) != 0 && glassNumber.TryGetValue((f.Model, f.Ordinal), out var glassNo)) SinkGlass(glassNo);
            // GLASS: SEE-THROUGH's recess (Glass.Sunk): a glass face's triangles again with every corner marked Moved and the offset (as the slab's
            // copy, but pushed into the model, along the face's inward normal), a frame quad an edge between the face's outline and
            // the sunk pane, in the colour of the other face on that edge. The face at rest keeps bit 12 and gains bit 14 (Glass.Replaced).
            void SinkGlass(int glassNo)
            {
                string? why = slab[i] ? "slab" : twin[i] >= 0 ? "twin" : sticker[i] ? "sticker" : null;
                var n = (0, 0, 0);
                var ctr = Centre(fm);
                double[] sum = [0, 0, 0], lo = [1e9, 1e9, 1e9], hi = [-1e9, -1e9, -1e9];
                var seen = 0;
                if (why == null)
                    for (var t = first; t < own; t++)
                    {
                        var pt = new List<(int, int, int)>();
                        foreach (var k in new[] { tris[t].R, tris[t].G, tris[t].B })
                        {
                            var q = (fm.Corners[(int)k].X, fm.Corners[(int)k].Y, fm.Corners[(int)k].Z);
                            pt.Add(q);
                            for (var c = 0; c < 3; c++) { var v = Axis(q, c); sum[c] += v; lo[c] = Math.Min(lo[c], v); hi[c] = Math.Max(hi[c], v); }
                            seen++;
                        }
                        if (n == (0, 0, 0) && Plane(pt) is { } pn) n = pn;
                    }
                if (why == null && n == (0, 0, 0)) why = "no plane";
                if (why != null) { GlassSkipped.Add((f.Model, glassNo, why)); return; }
                var out3 = new[] { (double)n.Item1, n.Item2, n.Item3 };
                var towards = (sum[0] / seen - ctr.R) * out3[0] + (sum[1] / seen - ctr.G) * out3[1] + (sum[2] / seen - ctr.B) * out3[2];
                var len = Math.Sqrt(out3[0] * out3[0] + out3[1] * out3[1] + out3[2] * out3[2]);
                var ax = 0;                                                       // the window's smaller size: the box of its corners on the two axes the normal is not along
                for (var c = 1; c < 3; c++) if (Math.Abs(out3[c]) > Math.Abs(out3[ax])) ax = c;
                double small = double.MaxValue;
                for (var c = 0; c < 3; c++) if (c != ax) small = Math.Min(small, hi[c] - lo[c]);
                var inset = GlassInset == 0 ? 0 : Math.Max(1 << shift, Math.Round(GlassInset * small));
                var sign = towards > 0 ? 1 : -1;                                  // moved = corner less the offset: the offset points out of the model
                var d = new Color((int)Math.Round(sign * inset * out3[0] / len), (int)Math.Round(sign * inset * out3[1] / len), (int)Math.Round(sign * inset * out3[2] / len), 0);
                var panel = new Color(first, own - first, 0, 0);
                for (var t = first; t < own; t++)
                {
                    AddTri((int)tris[t].R, (int)tris[t].G, (int)tris[t].B, (int)tris[t].A | Glass.Sunk | 0x40, d, Marks(Moved, Moved, Moved));       // 0x40: the shader moves the marked corners of a slab's rows
                    panels.Add(panel);
                    count[f.Model]++;
                    GlassSunkTris[f.Model]++;
                }
                for (var t = first; t < own; t++) tris[t] = new Color(tris[t].R, tris[t].G, tris[t].B, (int)tris[t].A | Glass.Replaced);
                var lo2 = i; while (lo2 > 0 && faces[lo2 - 1].Model == f.Model) lo2--;
                var hi2 = i; while (hi2 + 1 < faces.Count && faces[hi2 + 1].Model == f.Model) hi2++;
                int OtherOf(int a2, int b2)                                       // a face of the model, not glass, on the same two corners
                {
                    for (var j = lo2; j <= hi2; j++)
                    {
                        if (j == i || (faces[j].Meta & Glass.Bit) != 0) continue;
                        foreach (var ej in fm.Faces[faces[j].Ordinal].Edges)
                        {
                            int u = fm.Edges[ej].A, v = fm.Edges[ej].B;
                            if ((u == a2 && v == b2) || (u == b2 && v == a2)) return j;
                        }
                    }
                    return -1;
                }
                int ColourOf(int other) => recolour[other] >= 0 ? recolour[other] : faces[other].Meta & 15;
                if (inset == 0)
                {
                    // the pane on the outer edge: a border on each of its edges, a flat line (gpulines) one game pixel wide at any
                    // distance, so the posts and rails never fall under a pixel as the bands in the window's plane did. Its colour:
                    // the panel colour round the window - the commonest of the body faces on its edges, one colour a window (the
                    // edges' own colours made a pale rail, a black post and a grey side), on a tie the middle one by brightness - of
                    // two the darker (a car's window touches the pale roof and the body once each: the body), of three the one
                    // between (a helicopter's side window touches three greens: the darkest drew a near-black frame) - drawn a
                    // little darker, only with SEE-THROUGH
                    var roundIt = fm.Faces[f.Ordinal].Edges.Select(ei => OtherOf(fm.Edges[ei].A, fm.Edges[ei].B)).Where(o => o >= 0)
                        .GroupBy(ColourOf).OrderByDescending(g => g.Count()).ToList();
                    var tied = roundIt.Where(g => g.Count() == roundIt[0].Count()).Select(g => g.Key).OrderBy(Brightness).ToList();
                    var frameColour = Darker(tied.Count == 0 ? f.Meta & 15 : tied[(tied.Count - 1) / 2]);
                    // the frame is the model's own: a thin band flat in the window's plane along each edge, toward the window's middle,
                    // GlassFrame of the window's smaller size wide (world units - it scales with the thing), drawn with the faces at
                    // their depth and lit by the sun as they are (as before d6f5d9a; the one-game-pixel screen lines it was swapped for
                    // were a whole game pixel thick at any size, floated in front of the body and met at no corner)
                    foreach (var ei in fm.Faces[f.Ordinal].Edges)
                    {
                        int a2 = fm.Edges[ei].A, b2 = fm.Edges[ei].B;
                        double[] pa = [fm.Corners[a2].X, fm.Corners[a2].Y, fm.Corners[a2].Z], pb = [fm.Corners[b2].X, fm.Corners[b2].Y, fm.Corners[b2].Z];
                        double[] e = [pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2]];
                        double[] perp = [out3[1] * e[2] - out3[2] * e[1], out3[2] * e[0] - out3[0] * e[2], out3[0] * e[1] - out3[1] * e[0]];
                        var pl = Math.Sqrt(perp[0] * perp[0] + perp[1] * perp[1] + perp[2] * perp[2]);
                        if (pl <= 0) continue;
                        double toMid = 0;                                                 // the band goes toward the window's middle
                        for (var c = 0; c < 3; c++) toMid += (sum[c] / seen - (pa[c] + pb[c]) / 2) * perp[c];
                        var w = Math.Max(1 << shift, Math.Round(GlassFrame * small)) * (toMid > 0 ? -1 : 1) / pl;   // moved = corner less the offset
                        var de = new Color((int)Math.Round(w * perp[0]), (int)Math.Round(w * perp[1]), (int)Math.Round(w * perp[2]), 0);
                        AddTri(a2, b2, b2, frameColour | Glass.Sunk | 0x40, de, Marks(0, 0, Moved));
                        AddTri(a2, b2, a2, frameColour | Glass.Sunk | 0x40, de, Marks(0, Moved, Moved));
                        panels.Add(panel); panels.Add(panel);
                        count[f.Model] += 2;
                        GlassFrameTris[f.Model] += 2;
                    }
                }
                else foreach (var ei in fm.Faces[f.Ordinal].Edges)
                {
                    var a2 = fm.Edges[ei].A;
                    var b2 = fm.Edges[ei].B;
                    var other = OtherOf(a2, b2);
                    if (other < 0 && flatWalls.WallOf.TryGetValue(i, out var wallFace)) { other = wallFace; GlassWallFrames++; }    // flat on a wall: the wall's colour
                    var colour = other < 0 ? f.Meta & 15 : ColourOf(other);
                    if (other < 0) GlassFrameOwn[f.Model]++;
                    AddTri(a2, b2, b2, colour | Glass.Sunk | 0x40, d, Marks(0, 0, Moved));
                    AddTri(a2, b2, a2, colour | Glass.Sunk | 0x40, d, Marks(0, Moved, Moved));
                    panels.Add(panel); panels.Add(panel);
                    count[f.Model] += 2;
                    GlassFrameTris[f.Model] += 2;
                }
                GlassSunkFaces[f.Model]++;
                GlassSunkEdges[f.Model] += f.N;
            }
            // the model's last face: its back walls' triangles follow here, a fan a face like the bake shader's
            // own cut, on the extra points (corner numbers from the model's own corner count up)
            if (i == rows - 1 || faces[i + 1].Model != f.Model)
            {
                AddRods(f.Model);
                if (backs[f.Model] is { } bk)
                {
                    if (ModelCaps.IsHandMade(f.Model)) backsHand++;
                    else backsAuto++;
                    foreach (var (loop, colour) in bk.Faces)
                    {
                        var firstBack = tris.Count;
                        for (var t = 1; t < loop.Length - 1; t++)
                        {
                            // bits 10 + 11: a copy of a painted-light wall, its paint lit for the opposite side (PaintedLight)
                            var copied = PaintedLight.PaintedColour(fm, colour) ? 1024 | 2048 : 0;
                            AddTri(corners + loop[0], corners + loop[t], corners + loop[t + 1], colour | copied, default);
                            count[f.Model]++;
                            backCount[f.Model]++;
                        }
                        for (var t = firstBack; t < tris.Count; t++) panels.Add(new Color(firstBack, tris.Count - firstBack, 0, 0));
                    }
                }
            }
        }
        for (var m = 0; m < nModels; m++)                                    // a model of lines only has no face to come after
            if (start[m] < 0 && rodsOf[m].Any()) { start[m] = tris.Count; AddRods(m); }
        // the window borders join their model's lines (line_tex keeps a model's segments together: first, count)
        if (borders.Any(b => b is not null))
        {
            var own = segments.ToList();
            segments.Clear();
            for (var m = 0; m < nModels; m++)
            {
                var s = lineStart[m];
                lineStart[m] = segments.Count;
                segments.AddRange(own.GetRange(s, lineCount[m]));
                if (borders[m] is { } bm) { segments.AddRange(bm); lineCount[m] += bm.Count; }
            }
        }
        int maxTris = 1, maxLines = 1;
        for (var i = 0; i < nModels; i++)
        {
            info.SetPixel(i, 0, new Color(Store.Models[i].Offset, Math.Max(start[i], 0), count[i], backCount[i]));
            info.SetPixel(i, 1, new Color(lineStart[i], lineCount[i], 0, 0));
            info.SetPixel(i, 2, Centre(Store.Models[i]));
            maxTris = Math.Max(maxTris, count[i]);
            maxLines = Math.Max(maxLines, lineCount[i]);
        }
        var lineTable = Image.CreateEmpty(1, Math.Max(segments.Count, 1), false, Image.Format.Rgbaf);
        for (var i = 0; i < segments.Count; i++) lineTable.SetPixel(0, i, segments[i]);
        var lineTex = ImageTexture.CreateFromImage(lineTable);
        if (tris.Count > MaxTriangles) throw new InvalidOperationException($"{tris.Count} triangles, the triangle table keeps at most {MaxTriangles} (GpuWorld.MaxTriangles)");
        // TriPerRow triangles a texture row (TriAt), 4 texels each - x 0: corners + colour, x 1: its panel, x 2: its slab's offset, x 3: a rod's second
        var table = Image.CreateEmpty(4 * TriPerRow, Math.Max((tris.Count + TriPerRow - 1) / TriPerRow, 1), false, Image.Format.Rgbaf);
        void Put(int x, int row, Color c) { var (px, py) = TriAt(row, x); table.SetPixel(px, py, c); }
        // a panel's key (x 1, blue): where its first triangle sat before GLASS: SEE-THROUGH's recess triangles (Glass.Sunk) were added -
        // the wreck's pieces hash it, so their flight stays what it was
        var recessBefore = new int[tris.Count + 1];
        for (var i = 0; i < tris.Count; i++) recessBefore[i + 1] = recessBefore[i] + (((int)tris[i].A & Glass.Sunk) != 0 ? 1 : 0);
        for (var i = 0; i < tris.Count; i++)
        {
            var pn = panels[i];
            panels[i] = new Color(pn.R, pn.G, pn.R - recessBefore[(int)pn.R], pn.A);
            Put(0, i, tris[i]); Put(1, i, panels[i]); Put(2, i, triOff[i]);
            Put(3, i, triOff2.GetValueOrDefault(i));
        }
        var triTex = ImageTexture.CreateFromImage(table);
        var infoTex = ImageTexture.CreateFromImage(info);
        (TriTable, ModelTable, TriSlots, TriCount) = (triTex, infoTex, maxTris, tris.Count);
        pieceRows = SlotRows(maxTris * PieceCorners, Blasts);
        pieceRows.VisibleInstanceCount = blastHigh;                          // no blast yet: nothing to run the pieces' shader for
        AddChild(new MultiMeshInstance3D { Multimesh = pieceRows, MaterialOverride = pieces, CustomAabb = Huge });
        foreach (var m in new[] { this.models, glassFaces, shadows, lines, dots, debris, pieces })
        {
            m.SetShaderParameter("tri_tex", triTex);
            m.SetShaderParameter("model_tex", infoTex);
            m.SetShaderParameter("extra_tex", extraTex);
            m.SetShaderParameter("tri_slots", maxTris);
            m.SetShaderParameter("line_tex", lineTex);
            m.SetShaderParameter("line_slots", maxLines);
        }
        AddChild(new MultiMeshInstance3D { Multimesh = lineRows = SlotRows(maxLines * 6, Rows), MaterialOverride = lines, CustomAabb = Huge });   // a quad a segment (gpulines.gdshader)
        AddChild(new MeshInstance3D { Mesh = Empty(LiveRows * 2, Mesh.PrimitiveType.Points), MaterialOverride = dots, CustomAabb = Huge });
        AddChild(new MeshInstance3D { Mesh = Empty(LiveRows * 30), MaterialOverride = debris, CustomAabb = Huge });
        var slotMesh = slotRows = SlotRows(maxTris * 3, Rows);      // one set of slots for the passes over the same triangle slots
        AddChild(new MultiMeshInstance3D { Multimesh = slotMesh, MaterialOverride = this.models, CustomAabb = Huge });
        // GLASS: the glass ones alone, SEE-THROUGH only. Made in the glass state Update already set: the game's first Update
        // runs before this bake ends, so a mesh made hidden was never shown and the panes were missing in every game
        AddChild(glassMesh = new MultiMeshInstance3D { Multimesh = slotMesh, MaterialOverride = glassFaces, CustomAabb = Huge, Visible = glassShown });
        AddChild(shadowMesh = new MultiMeshInstance3D { Multimesh = slotMesh, MaterialOverride = shadows, CustomAabb = Huge });
        sunSlots = slotMesh;
        if (SunDepthPath is not null) MakeSunDepth(slotMesh);
        Ready = true;
        // the room's card memory: ent_tex + two instance transforms a row (model slots, lines; 48 bytes each) + one row of each
        // slot mesh (a vertex is 12 bytes) - SlotRows: the corners are drawn once a row, not stored for every row
        long rowBytes = Rows * (0xA0L * 4 + 2 * 48) + maxTris * 3L * 12 + maxLines * 6L * 12;
        long blastBytes = Blasts * (0xA0L * 4 + 16 + 48) + maxTris * (long)PieceCorners * 12;            // the two blast textures + a transform a blast + one blast's mesh
        GD.Print($"GpuWorld ready: {tris.Count} of {MaxTriangles} triangles (GpuWorld.MaxTriangles; table {table.GetWidth() * table.GetHeight() * 16 / 1048576.0:F1} MB of {4L * MaxTriangles * 16 / 1048576.0:F0} MB), up to {maxTris} a model; caps a model {CardModels.MaxCorners} corners, {CardModels.MaxEdges} edges, {CardModels.MaxFaces} faces, up to {maxLines} lines; card room {Rows} rows = {rowBytes / 1048576.0:F1} MB, {Blasts} blasts = {blastBytes / 1048576.0:F1} MB; model store {Store.Used} texels in {Store.Rows} rows = {Store.Rows * CardModels.Width * 16 / 1048576.0:F2} MB (cap {CardModels.Width * CardModels.Height * 16 / 1048576.0:F0} MB)");
        GD.Print($"GpuWorld thin sheets: {found.Pairs} back-to-back pairs, {found.Sheets} free sheets, {slabFlat} of them painted ground shadows left flat, {found.Decals} decals left flat; {stickers} stickers");
        GD.Print($"GpuWorld rods: {rodsOf.Sum(g => g.Count())} free-standing lines made solid, {segments.Count} lines on faces left flat, {lineFaces.Count} in all");
        GD.Print($"GpuWorld backs: {backsAuto} models closed automatically, {backsHand} hand-made; painted light: {PaintedLight.Groups} colour groups in {PaintedLight.ModelsWith} models");
    }

    // THIN SHEETS (the tests of in/sheets.py over the bake's own face list, in place of the rotor job's
    // hand list): a filled face no other filled face shares an edge with is a lone sheet unless it lies
    // flat on another face of the model (same plane within 1 unit, its centre inside it - a decal,
    // windows and hubs, which stays flat), and two filled faces on the very same edges, every edge of
    // theirs shared by no third face, are a back-to-back pair (a rotor blade's top and underside).
    // slab[i] marks the row to copy away (a pair's second face, the underside on the rotors, or the
    // sheet itself) and off[i] how far: 1 model unit before the scale shift, along the face's own rest
    // direction (its winding's normal), so slanted sheets offset the right way too.
    // Rotor blades, the darker face underneath and the lighter one moved up (play palette: 0 black < 8 grey < 4 pale
    // green). The aircraft's wings (1B) share an edge with a third face, so the pair test misses them.
    static readonly (int Model, int Under, int Top)[] Rotors =
    {
        (0x19, 0, 38), (0x19, 1, 37),        // helicopter 1's main rotor
        (0x17, 0, 30), (0x17, 1, 29),        // helicopter 2's
        (0x1B, 1, 24), (0x1B, 2, 25),        // the light aircraft's wings (kind $07; not a landed helicopter): light on top the same way
    };

    // twin[] links a pair's two rows; recolour[] gives the unmoved one the later face's colour (-1 = its own).
    // Rotors keep both colours instead, the lighter face moved up.
    static (int Pairs, int Sheets, int Decals) FindSheets(CardModels store,
        List<(int Model, int Base, int N, int Meta, int Ordinal)> faces, bool[] slab, Color[] off,
        int[] twin, int[] recolour)
    {
        int pairs = 0, sheets = 0, decals = 0;
        for (var i = 0; i < faces.Count; )
        {
            var end = i;
            while (end < faces.Count && faces[end].Model == faces[i].Model) end++;
            var md = store.Models[faces[i].Model];
            int corners = md.Corners.Length, ne = md.Edges.Length, shift = md.Shift;
            (int, int) Edge(int e)
            {
                var u = md.Edges[e].A;
                var v = md.Edges[e].B;
                return (Math.Min(u, v), Math.Max(u, v));
            }
            var edges = new List<(int, int)>[end - i];       // each filled face's edges, file order, vertex pairs
            var keys = new string[end - i];                  // the same, sorted, once each: the twin test's key
            var use = new Dictionary<(int, int), int>();     // how many filled faces an edge belongs to
            for (var r = i; r < end; r++)
            {
                edges[r - i] = [];
                foreach (var ei in md.Faces[faces[r].Ordinal].Edges)
                    if (ei < ne) edges[r - i].Add(Edge(ei));
                foreach (var k in edges[r - i]) use[k] = (use.TryGetValue(k, out var n) ? n : 0) + 1;
                var sorted = new List<(int, int)>(edges[r - i]);
                sorted.Sort();
                keys[r - i] = "";
                for (var k = 0; k < sorted.Count; k++)
                    if (k == 0 || sorted[k] != sorted[k - 1])             // an edge listed twice: once in the key
                        keys[r - i] += $"{sorted[k].Item1}:{sorted[k].Item2};";
            }
            var groups = new Dictionary<string, List<int>>();
            for (var r = i; r < end; r++)
            {
                if (!groups.TryGetValue(keys[r - i], out var g)) groups[keys[r - i]] = g = [];
                g.Add(r);
            }
            bool AllEdges(int r, int n)
            {
                foreach (var k in edges[r - i]) if (use[k] != n) return false;
                return true;
            }
            var pts = new List<(int, int, int)>[end - i];    // the faces' corners at rest, chained into loops
            for (var r = i; r < end; r++)
            {
                pts[r - i] = [];
                foreach (var k in LoopCorners(edges[r - i]))
                    if (k < corners)
                        pts[r - i].Add(md.Raw(k));
            }
            for (var r = i; r < end; r++)
            {
                var g = groups[keys[r - i]];
                if (g.Count == 2 && AllEdges(r, 2))
                {
                    if (r != Math.Max(g[0], g[1])) continue;      // the pair's second face moves, once
                    pairs++;
                    var u = Math.Min(g[0], g[1]);
                    if (Plane(pts[r - i]) is { } nrm) { slab[r] = true; off[r] = Offset(nrm, shift); twin[r] = u; twin[u] = r; }
                    recolour[u] = faces[r].Meta & 15;             // both sides the face that wins in the original (the later one)
                    continue;
                }
                if (!AllEdges(r, 1) || pts[r - i].Count < 3) continue;   // not a lone sheet
                if (Plane(pts[r - i]) is not { } normal) continue;
                double cx = 0, cy = 0, cz = 0;                   // the decal test: lying on another filled face
                foreach (var p in pts[r - i]) { cx += p.Item1; cy += p.Item2; cz += p.Item3; }
                var cen = new[] { cx / pts[r - i].Count, cy / pts[r - i].Count, cz / pts[r - i].Count };
                var on = false;
                for (var o = i; o < end && !on; o++)
                {
                    if (o == r || pts[o - i].Count < 3 || Plane(pts[o - i]) is not { } onrm) continue;
                    var ln = Math.Sqrt((double)onrm.Item1 * onrm.Item1 + (double)onrm.Item2 * onrm.Item2 + (double)onrm.Item3 * onrm.Item3);
                    var o0 = pts[o - i][0];
                    var flat = true;                                 // every corner within 1 unit of that plane
                    foreach (var q in pts[r - i])
                        if (Math.Abs((double)(onrm.Item1 * (q.Item1 - o0.Item1) + onrm.Item2 * (q.Item2 - o0.Item2) + onrm.Item3 * (q.Item3 - o0.Item3))) / ln >= 1.01)
                            flat = false;
                    if (flat && InsidePoly(cen, pts[o - i], onrm))
                        on = true;
                }
                if (on) decals++;
                else { slab[r] = true; off[r] = Offset(normal, shift); sheets++; }
            }
            foreach (var (m, under, top) in Rotors)
            {
                if (faces[i].Model != m) continue;
                int ru = -1, rt = -1;
                for (var r = i; r < end; r++)
                {
                    if (faces[r].Ordinal == under) ru = r;
                    if (faces[r].Ordinal == top) rt = r;
                }
                if (ru < 0 || rt < 0 || Plane(pts[rt - i]) is not { } n) continue;
                if (n.Item2 > 0) n = (-n.Item1, -n.Item2, -n.Item3);        // the corner less the offset: up
                (slab[ru], slab[rt], off[rt], twin[ru], twin[rt], recolour[ru]) = (false, true, Offset(n, shift), rt, ru, -1);
            }
            i = end;
        }
        return (pairs, sheets, decals);
    }

    // STICKERS: '91 painted faces in file order, so a
    // later face lying on a bigger earlier face, or up to 3 units behind it, showed as a sticker on it; with real depth
    // it hides or speckles. Such a face is drawn only as its moved copy (the thin sheets' machinery: corners marked Moved, meta
    // bit 6, the offset turned with the part) with edges, a plate 1 unit proud of that face, 2 if it lies on another
    // sticker, so file order still decides. Only faces behind their face, and faces on a lifted one (the 34 for
    // now; the 345 lying flat on a face stay as they are). Same rule as tools/model_viewer stickers().
    static int FindStickers(CardModels store,
        List<(int Model, int Base, int N, int Meta, int Ordinal)> faces, bool[] sticker, bool[] slab, Color[] off)
    {
        var count = 0;
        for (var i = 0; i < faces.Count; )
        {
            var end = i;
            while (end < faces.Count && faces[end].Model == faces[i].Model) end++;
            var md = store.Models[faces[i].Model];
            int corners = md.Corners.Length, shift = md.Shift;
            var pts = new List<(int, int, int)>[end - i];
            for (var r = i; r < end; r++) pts[r - i] = FacePoints(store, faces[r]);
            double Area((int, int, int) n) => Math.Sqrt((double)n.Item1 * n.Item1 + (double)n.Item2 * n.Item2 + (double)n.Item3 * n.Item3);
            double[] Centre(List<(int, int, int)> p) =>
                [p.Average(q => (double)q.Item1), p.Average(q => (double)q.Item2), p.Average(q => (double)q.Item3)];
            var mid = Centre(Enumerable.Range(0, corners).Select(k => md.Raw(k)).Select(q => (q.X, q.Y, q.Z)).ToList());
            var under = new int[end - i];
            for (var r = i; r < end; r++)
            {
                under[r - i] = -1;
                if ((faces[r].Meta & CarInside.Bit) != 0) continue;           // an interior's part is shape, not a mark painted on a wall
                if (pts[r - i].Count < 3 || Plane(pts[r - i]) is not { } nr) continue;
                var cen = Centre(pts[r - i]);
                double best = 0, depth = 0;
                (int, int, int) bn = default;
                var sign = 1;
                for (var o = i; o < r; o++)
                {
                    if ((faces[o].Meta & CarInside.Bit) != 0 || pts[o - i].Count < 3 || Plane(pts[o - i]) is not { } on || Area(on) <= Area(nr)) continue;
                    var ln = Area(on);
                    var o0 = pts[o - i][0];
                    var ou = Centre(pts[o - i]);                                   // that face's out side: away from the model's middle
                    var so = on.Item1 * (ou[0] - mid[0]) + on.Item2 * (ou[1] - mid[1]) + on.Item3 * (ou[2] - mid[2]) < 0 ? -1 : 1;
                    double lo = double.MaxValue, hi = double.MinValue;            // how far out of it each corner stands
                    foreach (var q in pts[r - i])
                    {
                        var d = so * (on.Item1 * (q.Item1 - o0.Item1) + on.Item2 * (q.Item2 - o0.Item2) + on.Item3 * (q.Item3 - o0.Item3)) / ln;
                        lo = Math.Min(lo, d); hi = Math.Max(hi, d);
                    }
                    // on it or up to 3 units behind it (one standing out is real shape and stays)
                    if (lo < -3.5 || hi > 0.5 || !InsidePoly(cen, pts[o - i], on) || ln <= best) continue;
                    (best, depth, bn, sign, under[r - i]) = (ln, (lo + hi) / 2, on, so, o);
                }
                if (under[r - i] < 0) continue;
                var top = false;                                                  // lying on an earlier sticker of the same face
                for (var o = i; o < r && !top; o++)
                    top = sticker[o] && under[o - i] == under[r - i] && Plane(pts[o - i]) is { } sn && InsidePoly(cen, pts[o - i], sn);
                // only the ones behind their face for now; one lying ON the face is already drawn
                // right, unless it sits on a sticker that is lifted (the nuclear device's marks on its yellow panel)
                if (depth > -0.5 && !top) { under[r - i] = -1; continue; }
                var lift = (top ? 2 : 1) - depth;                                 // from where it lies to 1 (or 2) units out
                var ln2 = Area(bn);
                var t = 1 << shift;
                // the copy is the corner LESS the offset, so the offset points into the face
                off[r] = new Color((int)Math.Round(-sign * lift * t * bn.Item1 / ln2), (int)Math.Round(-sign * lift * t * bn.Item2 / ln2),
                    (int)Math.Round(-sign * lift * t * bn.Item3 / ln2), 0);
                sticker[r] = true;
                slab[r] = true;                                                   // a plate: the moved copy plus its edges down into the face
                count++;
            }
            i = end;
        }
        return count;
    }

    // A filled face's corners at rest (signed bytes), chained into its loop.
    static List<(int, int, int)> FacePoints(CardModels store, (int Model, int Base, int N, int Meta, int Ordinal) f)
    {
        var md = store.Models[f.Model];
        var es = new List<(int, int)>();
        foreach (var ei in md.Faces[f.Ordinal].Edges)
            if (ei < md.Edges.Length) es.Add((md.Edges[ei].A, md.Edges[ei].B));
        var pts = new List<(int, int, int)>();
        foreach (var k in LoopCorners(es))
            if (k < md.Corners.Length) pts.Add(md.Raw(k));
        return pts;
    }

    // RODS: a line is a painted marking, and stays flat, when both its ends lie on a filled face's plane (within
    // 1 unit, as the decal test) and its middle inside that face (the floppy disk's shutter line); every other line
    // stands free (posts, legs, poles) and is a rod.
    // third: for a marking, a corner of the face it lies on that is off the line - with the line's two ends it gives the
    // face's plane, so the marking takes the sun as its face does (gpulines.gdshader); -1 when there is none.
    static bool[] FindRods(CardModels store,
        List<(int Model, int Base, int N, int Meta, int Ordinal)> faces,
        List<(int Model, int A, int B, int Colour)> lines, out int[] third)
    {
        var onModel = new Dictionary<int, List<List<(int, int, int)>>>();      // each model's filled faces' loops
        var loopOf = new Dictionary<List<(int, int, int)>, List<int>>();        // ...and each loop's corner numbers
        foreach (var f in faces)
        {
            if (!onModel.TryGetValue(f.Model, out var list)) onModel[f.Model] = list = [];
            var loop = FacePoints(store, f);
            list.Add(loop);
            var fm = store.Models[f.Model];
            loopOf[loop] = LoopCorners(fm.Faces[f.Ordinal].Edges.Where(ei => ei < fm.Edges.Length).Select(ei => (fm.Edges[ei].A, fm.Edges[ei].B)).ToList())
                .Where(k => k < fm.Corners.Length).ToList();
        }
        third = Enumerable.Repeat(-1, lines.Count).ToArray();
        var rod = new bool[lines.Count];
        for (var l = 0; l < lines.Count; l++)
        {
            var (m, a, b, _) = lines[l];
            var md = store.Models[m];
            var corners = md.Corners.Length;
            if (a == b || a >= corners || b >= corners) continue;
            (int, int, int) P(int k) => md.Raw(k);
            var (pa, pb) = (P(a), P(b));
            var mid = new[] { (pa.Item1 + pb.Item1) / 2.0, (pa.Item2 + pb.Item2) / 2.0, (pa.Item3 + pb.Item3) / 2.0 };
            var on = false;
            foreach (var pts in onModel.GetValueOrDefault(m) ?? [])
            {
                if (pts.Count < 3 || Plane(pts) is not { } n) continue;
                var ln = Math.Sqrt((double)n.Item1 * n.Item1 + (double)n.Item2 * n.Item2 + (double)n.Item3 * n.Item3);
                double Off((int, int, int) q) => Math.Abs((double)(n.Item1 * (q.Item1 - pts[0].Item1) + n.Item2 * (q.Item2 - pts[0].Item2) + n.Item3 * (q.Item3 - pts[0].Item3))) / ln;
                if (Off(pa) < 1.01 && Off(pb) < 1.01 && InsidePoly(mid, pts, n))
                {
                    on = true;
                    foreach (var k in loopOf[pts])                          // a corner of the face off the line
                    {
                        var q = P(k);
                        long ux = pb.Item1 - pa.Item1, uy = pb.Item2 - pa.Item2, uz = pb.Item3 - pa.Item3;
                        long vx = q.Item1 - pa.Item1, vy = q.Item2 - pa.Item2, vz = q.Item3 - pa.Item3;
                        if (uy * vz - uz * vy != 0 || uz * vx - ux * vz != 0 || ux * vy - uy * vx != 0) { third[l] = k; break; }
                    }
                    break;
                }
            }
            rod[l] = !on;
        }
        return rod;
    }

    // A rod's 12 triangles (a square tube centred on the line from corner va to vb, both ends capped), as table rows:
    // every corner is marked Centred, then Moved = the corner less the first half-offset instead of
    // plus it, Moved2 the same for the second (Marks, gpumodels.gdshaderinc); the colour has meta bit 6 (moved corners), bit 7
    // (the face's own side, winding outward) and bit 8 (a rod: the pieces leave it out). The half-offsets are half a
    // model unit before the scale shift (a whole unit, so width 2, when the shift is 0): the first along the axis the
    // line leans on least, the second across both.
    // A triangle's corner marks, x 2 of tri_tex alpha: 3 bits a corner (a from bit 0, b from 3, c from 6), each Moved (the corner
    // less the offset x 2), Moved2 (less the second offset x 3), Centred (a rod's corner on its line).
    internal const int Moved = 1, Moved2 = 2, Centred = 4;
    internal static int Marks(int a, int b, int c) => a | (b << 3) | (c << 6);

    static (List<(Color Tri, int Marks)> Tris, Color D1, Color D2) RodTriangles(CardModels.Model md, int va, int vb, int colour)
    {
        int shift = md.Shift, half = Math.Max(1, (1 << shift) / 2);
        int[] P(int k) => [md.Corners[k].X, md.Corners[k].Y, md.Corners[k].Z];
        int[] pa = P(va), pb = P(vb), u = [pb[0] - pa[0], pb[1] - pa[1], pb[2] - pa[2]];
        var axis = 0;                                                   // x, z, then y: a post's cross-section stays above the ground
        if (Math.Abs(u[2]) < Math.Abs(u[axis])) axis = 2;
        if (Math.Abs(u[1]) < Math.Abs(u[axis])) axis = 1;
        int[] e = [0, 0, 0];
        e[axis] = half;
        var cross = (u[1] * e[2] - u[2] * e[1], u[2] * e[0] - u[0] * e[2], u[0] * e[1] - u[1] * e[0]);
        if (cross == (0, 0, 0)) return ([], default, default);           // two corners on one spot
        var len = Math.Sqrt((double)cross.Item1 * cross.Item1 + (double)cross.Item2 * cross.Item2 + (double)cross.Item3 * cross.Item3);
        int[] d2 = [(int)Math.Round(half * cross.Item1 / len), (int)Math.Round(half * cross.Item2 / len), (int)Math.Round(half * cross.Item3 / len)];
        // the ring round the line, at each end: plus both, less d1, less both, less d2
        int[] bit = [0, Moved, Moved | Moved2, Moved2];
        double[] At(int[] p, int k) => [p[0] + (k is 1 or 2 ? -e[0] : e[0]) + (k >= 2 ? -d2[0] : d2[0]),
                                        p[1] + (k is 1 or 2 ? -e[1] : e[1]) + (k >= 2 ? -d2[1] : d2[1]),
                                        p[2] + (k is 1 or 2 ? -e[2] : e[2]) + (k >= 2 ? -d2[2] : d2[2])];
        var ring = new (int Corner, int Mark, double[] At)[8];
        for (var k = 0; k < 4; k++)
        {
            ring[k] = (va, bit[k] | Centred, At(pa, k));
            ring[4 + k] = (vb, bit[k] | Centred, At(pb, k));
        }
        var mid = new double[3];
        foreach (var r in ring) for (var k = 0; k < 3; k++) mid[k] += r.At[k] / 8;
        var tris = new List<(Color, int)>();
        var meta = colour | 0x40 | 0x80 | 0x100;
        void Tri(int i, int j, int k)
        {
            double[] x = ring[i].At, y = ring[j].At, z = ring[k].At;
            var n = new[] { (y[1] - x[1]) * (z[2] - x[2]) - (y[2] - x[2]) * (z[1] - x[1]), (y[2] - x[2]) * (z[0] - x[0]) - (y[0] - x[0]) * (z[2] - x[2]),
                            (y[0] - x[0]) * (z[1] - x[1]) - (y[1] - x[1]) * (z[0] - x[0]) };
            var outward = 0.0;
            for (var c = 0; c < 3; c++) outward += n[c] * ((x[c] + y[c] + z[c]) / 3 - mid[c]);
            if (outward < 0) (j, k) = (k, j);
            tris.Add((new Color(ring[i].Corner, ring[j].Corner, ring[k].Corner, meta), Marks(ring[i].Mark, ring[j].Mark, ring[k].Mark)));
        }
        for (var k = 0; k < 4; k++)
        {
            int j = (k + 1) % 4;
            Tri(k, j, 4 + j);
            Tri(k, 4 + j, 4 + k);
        }
        Tri(0, 1, 2); Tri(0, 2, 3);
        Tri(4, 5, 6); Tri(4, 6, 7);
        return (tris, new Color(e[0], e[1], e[2], 0), new Color(d2[0], d2[1], d2[2], 0));
    }

    // The corners of a face's edges chained into one loop, as sheets.py's loop() walks them.
    internal static List<int> LoopCorners(List<(int, int)> es)
    {
        if (es.Count == 0) return [];
        var rest = new List<(int, int)>(es);
        var outs = new List<int> { rest[0].Item1, rest[0].Item2 };
        rest.RemoveAt(0);
        while (rest.Count > 0)
        {
            var next = -1;
            for (var k = 0; k < rest.Count; k++)
            {
                if (rest[k].Item1 == outs[^1]) next = rest[k].Item2;
                else if (rest[k].Item2 == outs[^1]) next = rest[k].Item1;
                else continue;
                rest.RemoveAt(k);
                break;
            }
            if (next < 0) break;
            outs.Add(next);
        }
        if (outs[0] == outs[^1]) outs.RemoveAt(outs.Count - 1);
        return outs;
    }

    /// <summary>Meta bit 23: a face drawn only when seen from its front (gpumodels.gdshaderinc front_only).</summary>
    internal const int FrontOnly = 0x800000;

    // BACK TO BACK: the original draws a face from its front only, so a model can hold two faces in one place looking
    // opposite ways - a panel's outside and inside in two colours, a body's side and the back of the part behind it - and
    // only one of them is ever seen. The card draws every face from both sides, so the two fought over the same pixels.
    // A plain face with such a partner (same plane, looking the other way, the two overlapping) is drawn from its front
    // only, as the original does; every other face stays seen from both sides. Thin sheets, their twins, stickers and
    // a vehicle's inside take no part: they are drawn as moved copies already.
    // A face's corners at rest in the order the file winds them: edge i runs from corner i to corner i + 1, so corner i
    // is the one edge i shares with the edge before it. (FacePoints chains the edges into a loop from either end: its
    // order says nothing of which way the face looks.) Empty when the edges are not such a chain.
    static List<(int, int, int)> WoundPoints(CardModels store, (int Model, int Base, int N, int Meta, int Ordinal) f)
    {
        var md = store.Models[f.Model];
        var es = md.Faces[f.Ordinal].Edges;
        var pts = new List<(int, int, int)>();
        for (var i = 0; i < es.Length; i++)
        {
            int p = es[(i + es.Length - 1) % es.Length], c = es[i];
            if (p >= md.Edges.Length || c >= md.Edges.Length) return [];
            var (pa, pb, ca, cb) = (md.Edges[p].A, md.Edges[p].B, md.Edges[c].A, md.Edges[c].B);
            var k = pa == ca || pa == cb ? pa : pb == ca || pb == cb ? pb : -1;
            if (k < 0 || k >= md.Corners.Length) return [];
            pts.Add(md.Raw(k));
        }
        return pts;
    }

    static bool[] FindBacks(CardModels store, List<(int Model, int Base, int N, int Meta, int Ordinal)> faces, bool[] slab, bool[] sticker, int[] twin)
    {
        var back = new bool[faces.Count];
        for (var i = 0; i < faces.Count; )
        {
            var end = i;
            while (end < faces.Count && faces[end].Model == faces[i].Model) end++;
            var pts = new List<(int, int, int)>[end - i];
            var plane = new (int, int, int)?[end - i];
            var centre = new double[end - i][];
            for (var r = i; r < end; r++)
            {
                var p = pts[r - i] = WoundPoints(store, faces[r]);
                if (p.Count < 3 || slab[r] || sticker[r] || twin[r] >= 0 || (faces[r].Meta & CarInside.Bit) != 0) continue;
                plane[r - i] = Plane(p);
                centre[r - i] = [p.Average(q => (double)q.Item1), p.Average(q => (double)q.Item2), p.Average(q => (double)q.Item3)];
            }
            for (var r = i; r < end; r++)
            {
                if (plane[r - i] is not { } nr) continue;
                for (var o = i; o < r; o++)
                {
                    if (plane[o - i] is not { } on) continue;
                    double lr = Math.Sqrt((double)nr.Item1 * nr.Item1 + (double)nr.Item2 * nr.Item2 + (double)nr.Item3 * nr.Item3),
                           lo = Math.Sqrt((double)on.Item1 * on.Item1 + (double)on.Item2 * on.Item2 + (double)on.Item3 * on.Item3);
                    var facing = ((double)nr.Item1 * on.Item1 + (double)nr.Item2 * on.Item2 + (double)nr.Item3 * on.Item3) / (lr * lo);
                    if (facing > -0.999) continue;                                // not looking opposite ways
                    var o0 = pts[o - i][0];
                    var same = true;                                              // every corner of the one within half a unit of the other's plane
                    foreach (var q in pts[r - i])
                        if (Math.Abs((double)on.Item1 * (q.Item1 - o0.Item1) + (double)on.Item2 * (q.Item2 - o0.Item2) + (double)on.Item3 * (q.Item3 - o0.Item3)) / lo > 0.5) { same = false; break; }
                    if (!same || (!InsidePoly(centre[r - i], pts[o - i], on) && !InsidePoly(centre[o - i], pts[r - i], nr))) continue;
                    back[r] = back[o] = true;
                }
            }
            i = end;
        }
        return back;
    }

    // A face's rest direction: the first of its corner triples with a cross product (its winding's
    // normal), null when every triple is collinear.
    internal static (int, int, int)? Plane(List<(int, int, int)> pts)
    {
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var c = pts[(i + 2) % pts.Count];
            var n = ((b.Item2 - a.Item2) * (c.Item3 - a.Item3) - (b.Item3 - a.Item3) * (c.Item2 - a.Item2),
                     (b.Item3 - a.Item3) * (c.Item1 - a.Item1) - (b.Item1 - a.Item1) * (c.Item3 - a.Item3),
                     (b.Item1 - a.Item1) * (c.Item2 - a.Item2) - (b.Item2 - a.Item2) * (c.Item1 - a.Item1));
            if (n.Item1 != 0 || n.Item2 != 0 || n.Item3 != 0) return n;
        }
        return null;
    }

    // A point inside a polygon (sheets.py's inside()): the normal's biggest axis dropped, a ray cast.
    static bool InsidePoly(double[] p, List<(int, int, int)> poly, (int, int, int) n)
    {
        var na = new[] { n.Item1, n.Item2, n.Item3 };
        var ax = 0;
        for (var k = 1; k < 3; k++) if (Math.Abs(na[k]) > Math.Abs(na[ax])) ax = k;
        int u = ax == 0 ? 1 : 0, v = ax == 2 ? 1 : 2;            // the other two axes, ascending
        var c = false;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + poly.Count - 1) % poly.Count];
            double au = Axis(a, u), av = Axis(a, v), bu = Axis(b, u), bv = Axis(b, v);
            if ((av > p[v]) != (bv > p[v]) && p[u] < (bu - au) * (p[v] - av) / (bv - av) + au) c = !c;
        }
        return c;
    }
    static double Axis((int, int, int) p, int k) => k == 0 ? p.Item1 : k == 1 ? p.Item2 : p.Item3;

    // The slab's offset: the thickness (1 model unit before the scale shift) along a face's rest direction.
    static Color Offset((int, int, int) n, int shift)
    {
        var len = Math.Sqrt((double)n.Item1 * n.Item1 + (double)n.Item2 * n.Item2 + (double)n.Item3 * n.Item3);
        var t = 1 << shift;
        return new Color((int)Math.Round(t * n.Item1 / len), (int)Math.Round(t * n.Item2 / len), (int)Math.Round(t * n.Item3 / len), 0);
    }

    /// <summary>A model's centre at rest: the middle of its corners' box, each corner its signed bytes shifted by the
    /// scale (as ModelCaps reads them) - the point the sun's shading takes "outward" from (gpumodels.gdshader).</summary>
    static Color Centre(CardModels.Model md)
    {
        var count = md.Corners.Length;
        if (count == 0) return new Color(0, 0, 0, 0);
        int[] lo = [int.MaxValue, int.MaxValue, int.MaxValue], hi = [int.MinValue, int.MinValue, int.MinValue];
        for (var i = 0; i < count; i++)
            for (var k = 0; k < 3; k++)
            {
                var v = k == 0 ? md.Corners[i].X : k == 1 ? md.Corners[i].Y : md.Corners[i].Z;
                lo[k] = Math.Min(lo[k], v); hi[k] = Math.Max(hi[k], v);
            }
        return new Color((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2, 0);
    }

    /// <summary>HUNTER_CHECK_FEED: the player's place as the card last uploaded it - record 0's x, y, z words
    /// (bytes $4A-$4F, big-endian, y signed) read back out of entFloats.</summary>
    public (ushort X, short Y, ushort Z) PlayerPlace => PlaceOf(0);

    /// <summary>Live record slot's byte o as the card last uploaded it (in-between place, like PlayerPlace).</summary>
    public byte RecordByte(int slot, int o) => (byte)entFloats[slot * 0xA0 + o];

    /// <summary>ENHANCEMENT (FINE ANGLES, setting fine_angles): the records' fine angles drawn (Entity.PitchFine etc.).
    /// Off: every thing turns by the bytes' whole steps, number for number the game's own rotation.</summary>
    public bool FineAngles;

    /// <summary>FINE ANGLES: the angle words' low-byte slots (+$7D, +$7F, +$81 - read by nothing else on the card) carry
    /// each angle's part of a step past its byte (Entity.Frac), 0 for a stored record, off, or a fine value that no longer fits.</summary>
    /// <summary>A record as the upload sends it with FINE ANGLES off (the angle words' low-byte slots 0): for the checks that
    /// hand the card records of their own.</summary>
    public static byte[] AsUploaded(byte[] rec) { var b = (byte[])rec.Clone(); b[0x7D] = b[0x7F] = b[0x81] = 0; return b; }

    static void Fine(float[] f, int at, Hunter.Rules.Entity? e)
    {
        f[at + 0x7D] = e is null ? 0 : Hunter.Rules.Entity.Frac(e.PitchFine, e.B[0x7C]);
        f[at + 0x7F] = e is null ? 0 : Hunter.Rules.Entity.Frac(e.HeadingFine, e.B[0x7E]);
        f[at + 0x81] = e is null ? 0 : Hunter.Rules.Entity.Frac(e.RollFine, e.B[0x80]);
    }

    /// <summary>Live record slot's x, y, z words as the card last uploaded them.</summary>
    public (ushort X, short Y, ushort Z) PlaceOf(int slot) =>
        ((ushort)(RecordByte(slot, 0x4A) << 8 | RecordByte(slot, 0x4B)),
         (short)(RecordByte(slot, 0x4C) << 8 | RecordByte(slot, 0x4D)),
         (ushort)(RecordByte(slot, 0x4E) << 8 | RecordByte(slot, 0x4F)));

    readonly byte[] viewMask = new byte[128 * 128], viewMaskShown = new byte[128 * 128];
    ImageTexture? viewMaskTex;

    /// <summary>Each frame: the records and the animation table, as the rules hold them; world gives the game clock's
    /// sun, sunShading / ownShadows the settings (SUN, SHADOWS); viewRows the 3D VIEW DISTANCE (rules/ViewDistance.cs:
    /// 7 = the original's patch and in-view things, 0 = everything), seen from viewCamera (the picture's camera),
    /// viewWide = how much wider than 4:3 the view is.</summary>
    /// <summary>GROUND COLOURS: BANDS as bright as the original's land (GroundBands.Gain), worked out once for this map and
    /// the view palette (bank 2 never changes).</summary>
    public void BandGain(int[] view)
    {
        if (ReferenceEquals(view, bandGainFor)) return;
        bandGainFor = view;
        var gain = GroundBands.Gain(map, view);
        ground.SetShaderParameter("band_gain", (float)gain);
        GD.Print($"HUNTER: ground bands gain {gain:F2}");
    }
    int[]? bandGainFor;

    // ENHANCEMENT (real shadows, step 1 - nothing on screen uses it yet): the sun's depth picture. The models' triangle
    // slots drawn once more by gpusundepth.gdshader, as the sun sees them, into a SubViewport with a world of its own
    // (OwnWorld3D): the player's camera never sees these triangles and the sun's picture holds nothing else. Made only
    // with HUNTER_SUN_DEPTH=<png path> set (a debug aid): a normal run builds none of it.
    public static readonly string? SunDepthPath = System.Environment.GetEnvironmentVariable("HUNTER_SUN_DEPTH");
    public const int SunDepthPixels = 2048;          // the picture, square: 2 world units a pixel (at 1024, 4: a thin thing's shadow edge stair-stepped; 4096 cost 684 MB more card memory than 1024)
    public const float SunBoxSize = 4096f;           // the ground square round the player it covers, world units a side
    public const float SunBoxDepth = 8192f;          // the depth range 0..1 covers along the sun (shown units: x, height / 2, z)
    // the close-up picture: the same pass, centre and depth over a small square round the player - 0.25 world units a
    // pixel, so a man's legs and arm and a pilot's helmet throw clean shadows (at 2 a pixel a leg was 2 pixels wide and
    // its shadow came in specks). Every record the big box places is placed here too: a thing outside the square still
    // throws into it.
    public const int NearDepthPixels = 2048;
    public const float NearBoxSize = 512f;
    static GpuWorld? sunOwner;                       // the live one, for Main's capture (SaveSunDepth)
    SubViewport? sunView, nearView;
    ShaderMaterial? sunDepth, nearDepth;
    string[] sunCopied = [];                         // the model pass's uniforms the sun's pass shares, copied every frame
    Vector3 sunCentre;                               // the box's centre this frame (sun_box_centre)
    Vector3 sunToward;                               // and the sun (sun_dir)
    /// <summary>SHADOWS: REAL (GameView sets it each frame): the sun's picture is made the first frame it is asked for.</summary>
    public bool RealShadows;
    bool realShown;                                  // real_shadows as last sent (and the flat pass hidden)
    MultiMeshInstance3D? shadowMesh;                 // the flat pressed-down shadow pass (gpushadow.gdshader)
    MultiMesh? sunSlots;                             // the triangle slots, for a sun pass made later
    // HUNTER_SHADOW_FORCE=dark|lit (a debug aid for checking REAL): every tested spot in shadow / none
    static readonly int ShadowForce = System.Environment.GetEnvironmentVariable("HUNTER_SHADOW_FORCE") switch { "dark" => 1, "lit" => 2, _ => 0 };

    void MakeSunDepth(MultiMesh slotMesh)
    {
        (sunView, sunDepth) = SunPicture(slotMesh, SunDepthPixels, SunBoxSize);
        (nearView, nearDepth) = SunPicture(slotMesh, NearDepthPixels, NearBoxSize);
        sunCopied = [.. sunDepth.Shader.GetShaderUniformList().Select(u => (string)u.AsGodotDictionary()["name"]).Where(n => !n.StartsWith("sun_box_"))];
        sunOwner = this;
        // SHADOWS: REAL - the receivers read the pictures (sunshadow.gdshaderinc), with the same boxes
        foreach (var m in new[] { ground, models, lines })
        {
            m.SetShaderParameter("sun_depth_tex", sunView.GetTexture());
            m.SetShaderParameter("shadow_box_size", SunBoxSize);
            m.SetShaderParameter("shadow_box_depth", SunBoxDepth);
            m.SetShaderParameter("near_depth_tex", nearView.GetTexture());
            m.SetShaderParameter("shadow_near_size", NearBoxSize);
            if (ShadowForce != 0) m.SetShaderParameter("shadow_force", ShadowForce);
        }
        GD.Print($"SUNDEPTH: on, {SunDepthPixels} x {SunDepthPixels} RGBA half float, box {SunBoxSize} world units; close-up {NearDepthPixels} x {NearDepthPixels}, box {NearBoxSize}; depth range {SunBoxDepth}, {sunCopied.Length} uniforms shared with the model pass");
    }

    /// <summary>One sun picture: the slots drawn by gpusundepth.gdshader into a SubViewport and world of their own,
    /// over a square size world units a side round the centre.</summary>
    (SubViewport, ShaderMaterial) SunPicture(MultiMesh slotMesh, int pixels, float size)
    {
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://real3d/gpusundepth.gdshader") };
        mat.SetShaderParameter("sun_box_size", size);
        mat.SetShaderParameter("sun_box_depth", SunBoxDepth);
        mat.SetShaderParameter("sun_box_reach", SunBoxSize);
        var view = new SubViewport
        {
            Size = new Vector2I(pixels, pixels),
            OwnWorld3D = true,
            TransparentBg = true,                    // nothing drawn = (0, 0, 0, 0)
            UseHdr2D = true,                         // a half-float picture, no sRGB step: the depth comes back as written
            Msaa3D = Viewport.Msaa.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,   // on only while the sun is up (SunDepthFrame)
        };
        AddChild(view);
        view.AddChild(new Camera3D { Current = true });           // a camera for the world to be drawn at all; the shader writes POSITION itself
        view.AddChild(new MultiMeshInstance3D { Multimesh = slotMesh, MaterialOverride = mat, CustomAabb = Huge });
        return (view, mat);
    }

    /// <summary>Each frame: the model pass's uniforms copied over, the box centred on the player (record 0's x +$4A,
    /// height +$4C, z +$4E), the sun's direction; the picture drawn only while the sun is up and the view is outdoors.</summary>
    void SunDepthFrame((float X, float Y, float Z)? toward, bool insideView, bool sunShading)
    {
        var on = toward is not null && !insideView && (SunDepthPath is not null || RealShadows);
        sunView!.RenderTargetUpdateMode = on ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        nearView!.RenderTargetUpdateMode = sunView.RenderTargetUpdateMode;
        // SHADOWS: REAL while the picture is drawn (the sun up, outdoors) and the faces take the sun (SUN SHADING: the shade
        // is its paint): the receivers read it and the flat pass is not drawn. Otherwise as MORE: a picture not drawn this
        // frame is last frame's or older, so nothing reads it
        var real = on && RealShadows && sunShading;
        if (real != realShown)
        {
            realShown = real;
            foreach (var m in new[] { ground, models, lines }) { m.SetShaderParameter("real_shadows", real); m.SetShaderParameter("shadow_near_on", real); }
            shadowMesh!.Visible = !real;
            GD.Print($"REALSHADOWS: {(real ? "on" : "off")} (flat shadow pass {(real ? "hidden" : "drawn")})");
        }
        if (!on) return;
        foreach (var n in sunCopied) { var val = models.GetShaderParameter(n); sunDepth!.SetShaderParameter(n, val); nearDepth!.SetShaderParameter(n, val); }
        int U(int o) => (int)entFloats[o] << 8 | (int)entFloats[o + 1];
        sunCentre = new Vector3(U(0x4A), (short)U(0x4C), U(0x4E));
        var t = toward!.Value;
        sunToward = new Vector3(t.X, t.Y, t.Z);
        sunDepth!.SetShaderParameter("sun_box_centre", sunCentre);
        nearDepth!.SetShaderParameter("sun_box_centre", sunCentre);
        if (real) foreach (var m in new[] { ground, models, lines }) m.SetShaderParameter("shadow_box_centre", sunCentre);
    }

    /// <summary>HUNTER_SUN_DEPTH: the depth picture as a grey PNG (covered pixels stretched from their farthest, dark, to
    /// their nearest the sun, white; nothing drawn black), and its numbers in the log: the pixels covered, the depth
    /// range, the nearest pixel, and at the picture's centre (the player's spot) the depth read back and the height
    /// above the player's point it means along the sun's ray.</summary>
    public static void SaveSunDepth(string path)
    {
        if (sunOwner?.sunView is not { } view) { GD.Print("SUNDEPTH: no picture (no 3D view)"); return; }
        if (view.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled) { GD.Print("SUNDEPTH: off (no sun, or indoors)"); return; }
        SavePicture(view, SunBoxSize, path);
        GD.Print("SUNDEPTH: the close-up picture:");
        SavePicture(sunOwner.nearView!, NearBoxSize, System.IO.Path.ChangeExtension(path, null) + "_near.png");
    }

    static void SavePicture(SubViewport view, float boxSize, string path)
    {
        var img = view.GetTexture().GetImage();
        int w = img.GetWidth(), h = img.GetHeight();
        var d = new float[w * h];
        var covered = 0; float lo = 1, hi = 0; int near = -1; var worst = 0f;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var c = img.GetPixel(x, y);
                if (c.A < 0.5f) { d[y * w + x] = -1; continue; }
                var v = c.R + c.G / 256f;                                // the packed depth (gpusundepth.gdshader)
                worst = Math.Max(worst, Math.Abs(v - c.B));
                d[y * w + x] = v; covered++;
                if (v < lo) { lo = v; near = y * w + x; }
                hi = Math.Max(hi, v);
            }
        var grey = new byte[w * h];
        for (var i = 0; i < grey.Length; i++)
            grey[i] = d[i] < 0 ? (byte)0 : (byte)Math.Round(hi > lo ? 40 + 215 * (hi - d[i]) / (hi - lo) : 255);
        Image.CreateFromData(w, h, false, Image.Format.L8, grey).SavePng(path);
        var o = sunOwner!;
        // pixel (x, y) is the spot g of the centre's level (x east, y down = south); that spot reads 0.5 - (g . sun) / depth,
        // and a point t shown units up the sun's ray from it reads t / depth less and stands t * sun_dir.y shown units
        // (twice that in world height) above the centre's level
        float Above(int x, int y, float v)
        {
            float gx = ((x + 0.5f) / w - 0.5f) * boxSize, gz = (0.5f - (y + 0.5f) / h) * boxSize;
            var level = 0.5f - (gx * o.sunToward.X + gz * o.sunToward.Z) / SunBoxDepth;
            return (level - v) * SunBoxDepth * o.sunToward.Y * 2f;
        }
        float aLo = float.MaxValue, aHi = float.MinValue;
        for (var i = 0; i < d.Length; i++)
            if (d[i] >= 0) { var a = Above(i % w, i / w, d[i]); aLo = Math.Min(aLo, a); aHi = Math.Max(aHi, a); }
        if (covered > 0) GD.Print($"SUNDEPTH: height above the centre's level over the covered pixels {aLo:F1}..{aHi:F1} world units");
        // the lean: the mean pixel of the low parts (under 40 above the level) and of the high ones (over 150) - the high
        // ones lie away from the sun in the picture
        double lx = 0, ly = 0, hx = 0, hy = 0; int ln = 0, hn = 0;
        for (var i = 0; i < d.Length; i++)
        {
            if (d[i] < 0) continue;
            var a = Above(i % w, i / w, d[i]);
            if (a < 40) { lx += i % w; ly += i / w; ln++; } else if (a > 150) { hx += i % w; hy += i / w; hn++; }
        }
        if (ln > 0 && hn > 0) GD.Print($"SUNDEPTH: lean: low parts {ln} pixels mean ({lx / ln:F1}, {ly / ln:F1}), high parts {hn} pixels mean ({hx / hn:F1}, {hy / hn:F1}): high - low = ({hx / hn - lx / ln:F1}, {hy / hn - ly / ln:F1}) pixels (x east, y south)");
        var mid = d[(h / 2) * w + w / 2];
        GD.Print($"SUNDEPTH: {w} x {h} {img.GetFormat()}, sun_dir ({o.sunToward.X:F4}, {o.sunToward.Y:F4}, {o.sunToward.Z:F4}), centre {o.sunCentre}, " +
                 $"{covered} pixels covered, depth {lo:F6}..{hi:F6}, packed vs plain off by up to {worst:F6}");
        if (near >= 0) GD.Print($"SUNDEPTH: nearest the sun at pixel ({near % w}, {near / w}) depth {lo:F6}");
        GD.Print($"SUNDEPTH: centre pixel ({w / 2}, {h / 2}) " + (mid < 0 ? "empty" : $"depth {mid:F6} = {Above(w / 2, h / 2, mid):F1} world height above the player's point along the sun's ray"));
        for (var k = -6; k <= 6; k++)                                 // a row of samples through the centre, west to east, 4 pixels apart
        {
            var x = w / 2 + k * 4; var v = d[(h / 2) * w + x];
            GD.Print($"SUNDEPTH: row {h / 2} x {x}: " + (v < 0 ? "empty" : $"{v:F6} ({Above(x, h / 2, v):F1} above the level)"));
        }
        GD.Print($"SUNDEPTH: -> {path}");
    }

    public void Update(EntityTable entities, byte[] anim, bool insideView, int shellRow, byte[] modelBuf, short[] debrisPos, List<byte[]>? stored,
        WorldState? world = null, bool sunShading = false, bool ownShadows = false, int viewRows = 0, Camera? viewCamera = null, double viewWide = 1, bool cardPieces = false,
        int groundCuts = 1, ViewDistance.Turn? turn = null, float clock = 0, float animFrac = 0, bool ownColours = false, bool seeThrough = false)
    {
        if (seeThrough != glassShown)                                // GLASS, on change (meta bit 12 marks the faces): both passes
        {
            models.SetShaderParameter("glass", seeThrough);
            glassFaces.SetShaderParameter("glass", glassShown = seeThrough);
            lines.SetShaderParameter("glass", seeThrough);           // the window borders show with the see-through panes only
            if (glassMesh is not null) glassMesh.Visible = seeThrough;   // ORIGINAL: the pass draws nothing, so not run at all
        }
        if (ownColours != ownShown)                                  // MODEL COLOURS, on change: the models and their pieces
        {
            models.SetShaderParameter("own_colours", ownShown = ownColours);
            glassFaces.SetShaderParameter("own_colours", ownColours);
            pieces.SetShaderParameter("own_colours", ownColours);
            lines.SetShaderParameter("own_colours", ownColours);
        }
        SetCuts(groundCuts);                                         // GROUND SHAPE: the mesh is rebuilt only when it changes
        if (!Ready) return;
        if (viewCamera == null || world == null) viewRows = 0;
        if (stored != null)                                           // the map's stored objects, shown where they stand (Play.StoredLooks)
        {
            Array.Clear(storedBytes);
            storedUsed = Math.Min(stored.Count, StoredRows);
            if (stored.Count > StoredRows && !StoredOverflowWarned)
            {
                StoredOverflowWarned = true;                        // said once, never silent (maths32.md group 3: draw what fits, never grow)
                GD.PushWarning($"GpuWorld: {stored.Count} stored objects, the fixed room holds {StoredRows} - drawing the first {StoredRows}");
            }
            for (var r = 0; r < storedUsed; r++)
            {
                var b = stored[r];
                var at = r * 0xA0;
                Array.Copy(b, 0, storedBytes, at, 0xA0);
                storedBytes[at + 0x7D] = storedBytes[at + 0x7F] = storedBytes[at + 0x81] = 0;   // Fine with no record: no fine angle parts
                Lift(storedBytes, at);
                storedBytes[at + 0x1C] = 0xFF;                       // in view: the card places every one (the shaders read the mark from seenBytes)
            }
        }
        if (!logged) { logged = true; GD.Print($"GpuWorld first update: {entities.Count} records, record 0 alive {entities.All[0].B[0]} seen {entities.All[0].B[0x1C]} models {entities.All[0].B[0x24]}/{entities.All[0].B[0x25]}"); }
        Array.Clear(entFloats, 0, LiveRows * 0xA0);
        for (var r = 0; r < entities.Count && r < LiveRows; r++)
        {
            var b = entities.All[r].B;
            for (var o = 0; o < 0xA0; o++) entFloats[r * 0xA0 + o] = b[o];
            Fine(entFloats, r * 0xA0, FineAngles ? entities.All[r] : null);
            if (!insideView) Lift(entFloats, r * 0xA0);
            if (b[0] == 0 || b[0x20] != 0 || (sbyte)b[0x24] < 0 || insideView || viewRows == 7 && turn == null) continue;   // at ORIGINAL: the game's own in-view mark
            if (viewRows == 0)                                       // ALL: beside or behind the old camera too (the free camera may see it),
            {                                                         // but not a thing the camera stands inside (ViewDistance.AroundCamera)
                var e = entities.All[r];
                entFloats[r * 0xA0 + 0x1C] = viewCamera != null && ViewDistance.AroundCamera(viewCamera, e.UW(0x4A), e.UW(0x4E), e.W(0x6E), e.W(0x72)) ? 0 : 0xFF;
            }
            else entFloats[r * 0xA0 + 0x1C] = Seen(entities.All[r].UW(0x4A), entities.All[r].UW(0x4E), entities.All[r].W(0x72)) ? 0xFF : 0;
        }
        Array.Clear(seenBytes);
        for (var r = 0; r < storedUsed; r++)                          // the map's stored objects: none at ORIGINAL, all at ALL, else in the grown box
        {
            var at = r * 0xA0;
            int U(int o) => storedBytes[at + o] << 8 | storedBytes[at + o + 1];
            seenBytes[r] = (byte)(viewRows == 0 ? (viewCamera != null && ViewDistance.AroundCamera(viewCamera, U(0x4A), U(0x4E), (short)U(0x6E), (short)U(0x72)) ? 0 : 0xFF)
                : viewRows != 7 && !insideView && Seen(U(0x4A), U(0x4E), (short)U(0x72)) ? 0xFF : 0);
        }
        // Q / E turned: inside the turned view (a cell's margin round each thing); else Place's box grown to this distance
        bool Seen(int x, int z, short near72) => turn is { } t
            ? t.Sees(x, z, ViewDistance.TurnedRows(world!.Ground, viewRows), viewWide, 512)
            : ViewDistance.InView(viewCamera!, x, z, near72, viewRows, viewWide);
        // the ground: only the cells this distance draws (ViewDistance.Cells), sent again only when they change
        var some = viewRows != 0 && !insideView && ViewDistance.Cells(world!.Ground, viewCamera!, viewRows, viewWide, viewMask, turn);
        ground.SetShaderParameter("win_all", !some);
        Fit(some);                                                   // GROUND SHAPE: the mesh over the drawn cells only
        if (some && (viewMaskTex == null || !viewMask.AsSpan().SequenceEqual(viewMaskShown)))
        {
            viewMask.CopyTo(viewMaskShown, 0);
            using var img = Image.CreateFromData(128, 128, false, Image.Format.R8, viewMask);
            if (viewMaskTex == null) { viewMaskTex = ImageTexture.CreateFromImage(img); ground.SetShaderParameter("win_tex", viewMaskTex); }
            else viewMaskTex.Update(img);
        }
        TrackRoll(entities, clock);
        foreach (var m in new[] { models, glassFaces, shadows, lines, marks }) m.SetShaderParameter("track_clock", clock);   // TANK TRACKS: the clods' throw, the marks' age
        if (markDirty)
        {
            Buffer.BlockCopy(markFloats, 0, markBytes, 0, markBytes.Length);
            Put(markTex, Image.CreateFromData(MarkRing, 2, false, Image.Format.Rgbaf, markBytes));
            markDirty = false;
        }
        if (MarkLog)                                                 // the count of marks still fading, each time it changes
        {
            var live = 0;
            for (var i = 0; i < MarkRing; i++)
                if (markFloats[(MarkRing + i) * 4] == MarkTread && clock - markFloats[i * 4 + 3] is >= 0 and < MarkLife) live++;
            if (live != markLive) GD.Print($"TRACKMARK live {live} clock {clock:F1}");
            markLive = live;
        }
        if (rollDirty)
        {
            Buffer.BlockCopy(rollFloats, 0, rollBytes, 0, rollBytes.Length);
            Put(rollTex, Image.CreateFromData(RollW, RollH, false, Image.Format.Rf, rollBytes));
            rollDirty = false;
        }
        Buffer.BlockCopy(entFloats, 0, entBytes, 0, entBytes.Length);
        if (!entBytes.AsSpan().SequenceEqual(entSent))               // paused or held: the same table is not sent again
        {
            Put(entTex, Image.CreateFromData(0xA0 * PerRow, TexRows, false, Image.Format.Rf, entBytes));
            entBytes.CopyTo(entSent, 0);
        }
        if (!storedBytes.AsSpan().SequenceEqual(storedSent))         // the stored objects: only when one came, went or changed
        {
            Put(storedTex, Image.CreateFromData(0xA0 * 2, StoredTexRows, false, Image.Format.R8, storedBytes));
            storedBytes.CopyTo(storedSent, 0);
        }
        if (!seenBytes.AsSpan().SequenceEqual(seenSent))             // their in-view marks: as the view moves
        {
            Put(seenTex, Image.CreateFromData(SeenW, SeenH, false, Image.Format.R8, seenBytes));
            seenBytes.CopyTo(seenSent, 0);
        }
        const int used = LiveRows;                                   // the held bytes: live records only (a stored one's "one step back" is itself, place.gdshaderinc _eb)
        // SMOOTH PARTS: the fraction restarts exactly when the table steps (Play subtracts _animBlanks then),
        // so at that moment the table and the record part bytes held until now become the previous ones
        // (first update: the previous is the current, nothing slides yet)
        if (animFracLast > 1f || animFrac < animFracLast)
        {
            if (animFracLast > 1f)
            {
                for (var i = 0; i < 320 && i < anim.Length; i++) { animHeldPrev[i] = anim[i]; animHeld[i] = anim[i]; }
                for (var r = 0; r < used; r++) for (var o = 0; o < Held; o++) recHeldPrev[r * Held + o] = (byte)entFloats[r * 0xA0 + HeldAt(o)];
            }
            else
            {
                Buffer.BlockCopy(animHeld, 0, animHeldPrev, 0, 320);
                Buffer.BlockCopy(recHeld, 0, recHeldPrev, 0, recHeld.Length);
            }
            entprevDirty = true;
        }
        animFracLast = animFrac;
        for (var i = 0; i < 320 && i < anim.Length; i++) animHeld[i] = anim[i];
        for (var r = 0; r < used; r++) for (var o = 0; o < Held; o++) recHeld[r * Held + o] = (byte)entFloats[r * 0xA0 + HeldAt(o)];   // for the next step's snapshot
        for (var i = 0; i < 320 && i < anim.Length; i++) animFloats[i] = anim[i];
        for (var i = 0; i < 320; i++) animFloats[320 + i] = animHeldPrev[i];
        Buffer.BlockCopy(animFloats, 0, animBytes, 0, animBytes.Length);
        if (!animBytes.AsSpan().SequenceEqual(animSent)) { Put(animTex, Image.CreateFromData(320, 2, false, Image.Format.Rf, animBytes)); animBytes.CopyTo(animSent, 0); }
        if (entprevDirty)                                                    // the previous record bytes change only at a step
        {
            for (var r = 0; r < used; r++) for (var o = 0; o < Held; o++) entprevFloats[r * 0xA0 + HeldAt(o)] = recHeldPrev[r * Held + o];
            Buffer.BlockCopy(entprevFloats, 0, entprevBytes, 0, entprevBytes.Length);
            Put(entprevTex, Image.CreateFromData(0xA0 * PerRow, TexRows, false, Image.Format.Rf, entprevBytes));
            entprevDirty = false;
        }
        var lean = world?.AnalogLean ?? 0;                           // STICK STEERING: the wheel or stick follows the lean
        if (lean != leanShown || LeanRow != leanRowShown)
        {
            (leanShown, leanRowShown) = (lean, LeanRow);
            foreach (var m in things ??= [models, glassFaces, shadows, lines, dots, debris, pieces, marks]) { m.SetShaderParameter("steer_lean", lean); m.SetShaderParameter("steer_lean_row", LeanRow); }
        }
        SendBlasts();
        if (!PiecesStay)                                                // the rows past the last live blast are not drawn
        {
            var high = blastHigh;
            while (high > 0 && clock - blastStart[high - 1] > BlastLife) high--;
            if (high != blastHigh) { blastHigh = high; if (pieceRows is not null) pieceRows.VisibleInstanceCount = high; }
        }
        pieces.SetShaderParameter("piece_clock", clock);                // the game's own frames (GameView.GameClock): still while paused
        debris.SetShaderParameter("debris_off", cardPieces);         // the pieces stand in for the original's ten triangles
        pieces.SetShaderParameter("pieces_on", cardPieces);
        pieces.SetShaderParameter("piece_stay", PiecesStay);           // EXPLOSIONS: LASTING - they lie where they fell
        models.SetShaderParameter("better_fire", cardPieces);          // BETTER EXPLOSIONS: the fireball (gpumodels.gdshaderinc fire_look)
        for (var i = 0; i < fireHits.Length; i++) if (fireHits[i].W != 0 && clock > fireHitUntil[i]) fireHits[i] = default;
        models.SetShaderParameter("fire_reach", Fireball.Reach);
        models.SetShaderParameter("fire_hits", fireHits);              // step 5: the star sized by what it blew up
        foreach (var m in things ??= [models, glassFaces, shadows, lines, dots, debris, pieces, marks])
        {
            m.SetShaderParameter("inside_view", insideView);
            m.SetShaderParameter("shell_row", shellRow);                  // the building drawn from inside ($48F8)
            m.SetShaderParameter("anim_frac", animFrac);                 // SMOOTH PARTS: how far into the step's blanks the game is
        }
        // the sun, one copy - the rules' (Objects.SunOffset from the game clock): the faces' and the ground's shades
        // (sun_shading) and the thrown shadows (own_shadows) all follow it
        var toward = SunShade.Toward(world);
        SunToward = toward;                                               // CLOUDS: the low layer's lit side (Real3DView.Frame)
        if (world != null) Clock = (world.Hour, world.Minute, world.Day); // CLOUDS' night: the moon's place and phase (Real3DView.Frame)
        var offset = Objects.SunOffset(world);
        foreach (var m in sunLit ??= [ground, models, glassFaces, pieces, lines])   // lines: the window borders take the sun as the faces do
        {
            m.SetShaderParameter("sun_shading", sunShading);
            m.SetShaderParameter("sun_up", toward != null);
            if (toward is { } t) m.SetShaderParameter("sun_dir", new Vector3(t.X, t.Y, t.Z));
        }
        models.SetShaderParameter("painted_light", sunShading && toward != null);
        glassFaces.SetShaderParameter("painted_light", sunShading && toward != null);   // PaintedLight: the models' walls with the painters' light
        if (!painterSet) { painterSet = true; var ps = SunShade.PainterSun; foreach (var m in new[] { models, glassFaces }) m.SetShaderParameter("painter_dir", new Vector3(ps.X, ps.Y, ps.Z)); }
        shadows.SetShaderParameter("own_shadows", ownShadows);
        shadows.SetShaderParameter("shadow_sun_up", offset != null);
        if (offset is { } sun) shadows.SetShaderParameter("sun_shadow", new Vector3((float)sun.DirX, (float)sun.DirZ, (float)sun.TanElev));
        if (sunView is null && RealShadows && sunSlots is not null) MakeSunDepth(sunSlots);   // SHADOWS: REAL, first asked for
        if (sunView is not null) SunDepthFrame(toward, insideView, sunShading);
        DrawList();

        // The original's debris: $59FE rewrites model 0 in the session's buffer - its scale byte and the ten face
        // colours. Only that is sent over again, and only when it changed.
        var now = model0Now;
        now[0] = modelBuf[model0 + 0x11];
        for (var k = 0; k < 10; k++) now[1 + k] = modelBuf[faces0 + 5 * k];
        if (!now.AsSpan().SequenceEqual(model0Seen))
        {
            now.CopyTo(model0Seen, 0);
            Store.PatchDebris(now[0], now.AsSpan(1));                // the store takes the same values
            Put(mdlTex, Image.CreateFromData(CardModels.Width, Store.Rows, false, Image.Format.Rgbaf, StoreBytes()));
        }
        for (var i = 0; i < 30; i++) dposFloats[i] = debrisPos[i];
        Buffer.BlockCopy(dposFloats, 0, dposBytes, 0, dposBytes.Length);
        Put(dposTex, Image.CreateFromData(30, 1, false, Image.Format.Rf, dposBytes));
    }
}
