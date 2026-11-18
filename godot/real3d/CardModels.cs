using System;
using System.Collections.Generic;
using System.Linq;
using Hunter.Rules;

namespace Hunter;

/// <summary>The card's models in our own form (CARD MODELS: the shaders read the header and corners from it): the card's objfile parsed into
/// full-number corners, edges and faces, laid out as the texels of the card's mdl_tex (RGBA32F, 1024 x 1024), and back to
/// the objfile's bytes for the check. A model, from its header texel At (the address is a texel number, x = a & 1023,
/// y = a >> 10; models follow each other):
///   At      (set, shift, corners, edges)                 the header's +$10, +$11 and its counts
///   At + 1  (faces, list slots, texels in all, waterline: SeaBodies' depth below the original's lowest corner, else 0)
///   corners (x, y, z, flag) a texel each: x, y, z = wrap16(signed byte << shift), exactly as model.gdshaderinc _walk_step
///   edges   (a, b, 0, 0) a texel each
///   faces   (colour, flags, n, first slot) a texel each: flags = the byte's high nibble, n = its edge count
///   lists   the faces' edge numbers, 4 a texel, one run after the other; a face's first slot counts from the first list texel
/// The header's other 16 bytes and the bytes after the face list (up to the size word) stay here as they were.
/// The card's builders (GpuWorld.CardShapes: straight blades, RoundWheels, CarInside) edit the models here in full numbers
/// (Model.Replace) and then call Layout; a model may hold up to MaxCorners corners, MaxEdges edges and MaxFaces faces, and a
/// builder's corners need not sit on the scale's steps (Model.FullCorner). A model past the objfile's byte format goes to the
/// rules as an extended ("OW") model (ToObjfile, ToWideBytes; rules/Objects.cs Models).
/// A model's Offset is its key (the rules' objfile offset): it stays what it was when the model grows.</summary>
public sealed partial class CardModels
{
    public const int Width = 1024, Height = 1024;                    // the cap: 1M texels = 16 MB
    public const int MaxCorners = 1024, MaxEdges = 4096, MaxFaces = 2048;   // a model's cap
    /// <summary>The most corners a face may have: slots.gdshaderinc FACE_MAX, the bake's width (GpuWorld) and CheckSlots' follow it.
    /// An array size on the card, not the objfile's 15 edges (an open loop has a corner more than its edges).</summary>
    public const int MaxFaceCorners = 64;
    /// <summary>The texture's rows: as many as the models use (a debris re-upload sends only these).</summary>
    public int Rows => Math.Max(1, (Used + Width - 1) / Width);

    public readonly record struct Corner(int Flag, int X, int Y, int Z);
    public readonly record struct Edge(int A, int B);
    public readonly record struct Face(int Colour, int Flags, int[] Edges);

    public sealed class Model
    {
        public int Number;                   // its number among the models
        public int Offset;                   // the model's key: its byte offset in the rules' objfile
        public byte[] Head = [];             // bytes 0..$0F as they are (OB, size word, the rest)
        public int Set, Shift;               // header +$10, +$11
        public Corner[] Corners = [];
        public Edge[] Edges = [];
        public Face[] Faces = [];
        public byte[] Tail = [];             // the bytes after the $FF that ends the face list, up to the size word
        public int At, Texels;               // header texel address, texels the model takes
        public bool Replaced;                // a builder made its lists (its size word and padding are made again in ToBytes)
        /// <summary>ENHANCEMENT (SeaBodies): how far a body built under the water reaches below the original's lowest corner (full
        /// units, 0 for every other model). The card adds it to the lowest corner it measures, so the game's lift is the
        /// original's. Written in the header's second texel, w (model.gdshaderinc _waterline).</summary>
        public int Waterline;
        /// <summary>ENHANCEMENT (Fireball): how many corners at the end of the list are the card's own additions that the
        /// lowest-corner measure must not count (the fireball core's 12 hang below the star; counted, the card lifted the
        /// whole star off its place - CheckPieces start 12 of 7026). Written with Waterline: w = Waterline + Unmeasured << 16
        /// (model.gdshaderinc _waterline / _measured).</summary>
        public int Unmeasured;
        public int ListAt => At + 2 + Corners.Length + Edges.Length + Faces.Length;

        /// <summary>A corner as the objfile's signed bytes held it (the coordinate in model units before the scale shift).</summary>
        public (int X, int Y, int Z) Raw(int k) { var c = Corners[k]; return (c.X >> Shift, c.Y >> Shift, c.Z >> Shift); }

        /// <summary>Put a corner at these coordinates in the objfile's signed bytes (the value cut to a byte as the file would).</summary>
        public void SetRaw(int k, int x, int y, int z) => Corners[k] = RawCorner(Corners[k].Flag, x, y, z);

        /// <summary>A corner from the objfile's signed bytes (the coordinate in model units before the scale shift, cut to a byte as the file would).</summary>
        public Corner RawCorner(int flag, int x, int y, int z) => new(flag, Wrap16((sbyte)x << Shift), Wrap16((sbyte)y << Shift), Wrap16((sbyte)z << Shift));

        /// <summary>A corner at these full units (no scale steps, no byte range): the card's own builders place their corners so -
        /// the 4-unit steps and the byte's reach are the objfile's format, not the card's. Such a model no longer fits the objfile's bytes (FitsBytes).</summary>
        public Corner FullCorner(int flag, int x, int y, int z) => new(flag, Wrap16(x), Wrap16(y), Wrap16(z));

        /// <summary>Swap the model's corners, edges and faces (and its header bytes, when given) for these: nothing changes if the
        /// cap (MaxCorners, MaxEdges, MaxFaces) is passed. The texels follow with Layout.</summary>
        public void Replace(Corner[] corners, Edge[] edges, Face[] faces, byte[]? head = null)
        {
            if (corners.Length > MaxCorners) throw new InvalidOperationException($"model {Number}: {corners.Length} corners, the model store keeps at most {MaxCorners} a model (CardModels.MaxCorners)");
            if (edges.Length > MaxEdges) throw new InvalidOperationException($"model {Number}: {edges.Length} edges, the model store keeps at most {MaxEdges} a model (CardModels.MaxEdges)");
            if (faces.Length > MaxFaces) throw new InvalidOperationException($"model {Number}: {faces.Length} faces, the model store keeps at most {MaxFaces} a model (CardModels.MaxFaces)");
            if (faces.FirstOrDefault(f => f.Edges.Length + 1 > MaxFaceCorners) is { Edges: { } big }) throw new InvalidOperationException($"model {Number}: a face of {big.Length} edges, the card cuts faces of at most {MaxFaceCorners} corners (CardModels.MaxFaceCorners, slots.gdshaderinc FACE_MAX)");
            (Corners, Edges, Faces) = (corners, edges, faces);
            if (head is not null) Head = head;
            Tail = [];
            Replaced = true;
        }
    }

    public Model[] Models { get; private set; }

    /// <summary>ENHANCEMENT: one more model, a copy of model from (its header bytes, set and shift) under
    /// a key no objfile model has - the highest free offset - for a builder to fill (Model.Replace).
    /// The rules never see it; its number is the next after the objfile's.</summary>
    public int AppendCopy(int from)
    {
        var used = new HashSet<int>(Models.Select(m => m.Offset));
        var key = 0xFFFE;
        while (used.Contains(key)) key -= 2;
        var m = Parse(src, Models[from].Offset);
        m.Offset = key;
        m.Number = Models.Length;
        Models = [.. Models, m];
        Layout();
        return m.Number;
    }
    public readonly float[] Texels = new float[Width * Height * 4];
    public int Used { get; private set; }
    readonly byte[] src;

    public CardModels(byte[] objfile)
    {
        src = objfile;
        var list = new List<Model>();
        foreach (var off in new Hunter.Rules.Models(objfile).Offsets) list.Add(Parse(objfile, off));
        for (var i = 0; i < list.Count; i++) list[i].Number = i;
        Models = list.ToArray();
        Layout();
    }

    static int U(byte[] b, int o) => b[o] << 8 | b[o + 1];
    static int Wrap16(int v) => (v << 16) >> 16;
    static int Place(byte[] b, int o, int shift) => Wrap16((sbyte)b[o] << shift);

    static Model Parse(byte[] b, int o)
    {
        var m = new Model { Offset = o, Head = b.AsSpan(o, 0x10).ToArray(), Set = b[o + 0x10], Shift = b[o + 0x11] };
        var nv = U(b, o + 0x12);
        m.Corners = new Corner[nv];
        for (var k = 0; k < nv; k++)
        {
            var p = o + 0x14 + k * 4;
            m.Corners[k] = new Corner(b[p], Place(b, p + 1, m.Shift), Place(b, p + 2, m.Shift), Place(b, p + 3, m.Shift));
        }
        var ea = o + 0x14 + nv * 4;
        m.Edges = new Edge[U(b, ea)];
        for (var e = 0; e < m.Edges.Length; e++) m.Edges[e] = new Edge(b[ea + 2 + e * 2], b[ea + 3 + e * 2]);
        var fa = ea + 2 + m.Edges.Length * 2;
        m.Faces = new Face[U(b, fa)];
        var q = fa + 2;
        for (var f = 0; f < m.Faces.Length; f++)
        {
            var n = b[q + 1] & 15;
            var es = new int[n];
            for (var i = 0; i < n; i++) es[i] = b[q + 2 + i];
            m.Faces[f] = new Face(b[q], b[q + 1] & 0xF0, es);
            q += 2 + n;
        }
        if (b[q] != 0xFF) throw new InvalidOperationException($"model at {o}: the face list does not end in $FF");
        q++;
        var end = o + U(b, o + 2);
        if (end < q) throw new InvalidOperationException($"model at {o}: the size word ends before the face list does");
        m.Tail = b.AsSpan(q, end - q).ToArray();
        return m;
    }

    /// <summary>The header texel address of the model at this objfile offset.</summary>
    public int AtOf(int offset) => byAt[offset];

    readonly Dictionary<int, int> byAt = new();

    void T(int a, float x, float y, float z, float w)
    {
        var i = a * 4;
        Texels[i] = x; Texels[i + 1] = y; Texels[i + 2] = z; Texels[i + 3] = w;
    }

    /// <summary>Put the models' texels in place, one after the other (the constructor does it; the builders call it again once they are done).
    /// The store's cap is checked here: Width x Height texels in all.</summary>
    public void Layout()
    {
        Array.Clear(Texels);
        var a = 0;
        foreach (var m in Models)
        {
            m.At = a;
            byAt[m.Offset] = a;
            var slots = 0;
            foreach (var f in m.Faces) slots += f.Edges.Length;
            m.Texels = 2 + m.Corners.Length + m.Edges.Length + m.Faces.Length + (slots + 3) / 4;
            if (a + m.Texels > Width * Height) throw new InvalidOperationException($"model {m.Number}: the model store is full, {a + m.Texels} texels, room for {Width * Height} (CardModels.Width x Height)");
            T(a, m.Set, m.Shift, m.Corners.Length, m.Edges.Length);
            T(a + 1, m.Faces.Length, slots, m.Texels, m.Waterline + (m.Unmeasured << 16));
            for (var k = 0; k < m.Corners.Length; k++) PutCorner(m, k);
            var ea = a + 2 + m.Corners.Length;
            for (var e = 0; e < m.Edges.Length; e++) T(ea + e, m.Edges[e].A, m.Edges[e].B, 0, 0);
            var fa = ea + m.Edges.Length;
            var la = fa + m.Faces.Length;
            var slot = 0;
            for (var f = 0; f < m.Faces.Length; f++)
            {
                var face = m.Faces[f];
                T(fa + f, face.Colour, face.Flags, face.Edges.Length, slot);
                foreach (var e in face.Edges) { Texels[(la + (slot >> 2)) * 4 + (slot & 3)] = e; slot++; }
            }
            a += m.Texels;
        }
        Used = a;
    }

    void PutCorner(Model m, int k)
    {
        var c = m.Corners[k];
        T(m.At + 2 + k, c.X, c.Y, c.Z, c.Flag);
    }

    /// <summary>The debris model ($59FE rewrites model 0 in the session's buffer): its scale byte and the first faces' colours.
    /// The corners come out again from the objfile's signed bytes with the new shift.</summary>
    public void PatchDebris(int shift, ReadOnlySpan<byte> colours)
    {
        var m = Models[0];
        if (shift != m.Shift)
        {
            m.Shift = shift;
            for (var k = 0; k < m.Corners.Length; k++)
            {
                var p = m.Offset + 0x14 + k * 4;
                m.Corners[k] = m.Corners[k] with { X = Place(src, p + 1, shift), Y = Place(src, p + 2, shift), Z = Place(src, p + 3, shift) };
                PutCorner(m, k);
            }
            T(m.At, m.Set, shift, m.Corners.Length, m.Edges.Length);
        }
        var fa = m.At + 2 + m.Corners.Length + m.Edges.Length;
        for (var f = 0; f < colours.Length && f < m.Faces.Length; f++)
        {
            m.Faces[f] = m.Faces[f] with { Colour = colours[f] };
            Texels[(fa + f) * 4] = colours[f];
        }
    }

    /// <summary>The model's objfile bytes made from the texels (the store as the card holds it) and its kept header and tail bytes.</summary>
    public byte[] ToBytes(int model, float[] texels)
    {
        var m = Models[model];
        float F(int a, int c) => texels[a * 4 + c];
        var o = new List<byte>(m.Head);
        var shift = (int)F(m.At, 1);
        var nv = (int)F(m.At, 2);
        var ne = (int)F(m.At, 3);
        var nf = (int)F(m.At + 1, 0);
        if (nv > 255 || ne > 255) throw new InvalidOperationException($"model {m.Number}: {nv} corners, {ne} edges do not fit the objfile's bytes");
        o.Add((byte)F(m.At, 0));
        o.Add((byte)shift);
        o.Add((byte)(nv >> 8)); o.Add((byte)nv);
        for (var k = 0; k < nv; k++)
        {
            var a = m.At + 2 + k;
            o.Add((byte)F(a, 3));
            for (var c = 0; c < 3; c++) o.Add((byte)((int)F(a, c) >> shift));
        }
        o.Add((byte)(ne >> 8)); o.Add((byte)ne);
        var ea = m.At + 2 + nv;
        for (var e = 0; e < ne; e++) { o.Add((byte)F(ea + e, 0)); o.Add((byte)F(ea + e, 1)); }
        o.Add((byte)(nf >> 8)); o.Add((byte)nf);
        var fa = ea + ne;
        var la = fa + nf;
        for (var f = 0; f < nf; f++)
        {
            var n = (int)F(fa + f, 2);
            var slot = (int)F(fa + f, 3);
            o.Add((byte)F(fa + f, 0));
            o.Add((byte)((int)F(fa + f, 1) | n));
            for (var i = 0; i < n; i++) o.Add((byte)F(la + ((slot + i) >> 2), (slot + i) & 3));
        }
        o.Add(0xFF);
        o.AddRange(m.Tail);
        if (m.Replaced)                                          // a model a builder made: padded to 4 bytes, the size word its own
        {
            while (o.Count % 4 != 0) o.Add(0);
            o[2] = (byte)(o.Count >> 8); o[3] = (byte)o.Count;
        }
        return o.ToArray();
    }

    /// <summary>The model as an extended ("OW") model (rules/Objects.cs Models): its own header bytes with "OW" and a shift of 0,
    /// the corners in full units as words, edges and face lists as words; padded to 4 bytes, the size word its own.</summary>
    public byte[] ToWideBytes(int model)
    {
        var m = Models[model];
        var o = new List<byte>(m.Head);
        o[1] = (byte)'W';
        void W(int v) { o.Add((byte)(v >> 8)); o.Add((byte)v); }
        o.Add((byte)m.Set);
        o.Add(0);                                                // full units: no shift
        W(m.Corners.Length);
        foreach (var c in m.Corners) { o.Add((byte)c.Flag); o.Add(0); W(c.X); W(c.Y); W(c.Z); }
        W(m.Edges.Length);
        foreach (var e in m.Edges) { W(e.A); W(e.B); }
        W(m.Faces.Length);
        foreach (var f in m.Faces)
        {
            o.Add((byte)(f.Colour & 0x7F));
            o.Add((byte)(f.Flags & 0xF0));
            W(f.Edges.Length);
            foreach (var e in f.Edges) W(e);
        }
        o.Add(0xFF);
        while (o.Count % 4 != 0) o.Add(0);
        if (o.Count > 0xFFFF) throw new InvalidOperationException($"model {m.Number}: {o.Count} bytes, past an extended model's size word");
        o[2] = (byte)(o.Count >> 8); o[3] = (byte)o.Count;
        return o.ToArray();
    }

    /// <summary>Whether the model can be written as the objfile's bytes exactly: at most 255 corners and edges, at most 15 edges a
    /// face, every corner a whole step of the scale shift within a signed byte. The card's own builders are not held to it (the
    /// store keeps full numbers: MaxCorners, MaxEdges, MaxFaces); a model that does not fit goes to the rules extended (ToWideBytes).</summary>
    public bool FitsBytes(int model)
    {
        var m = Models[model];
        if (m.Corners.Length > 255 || m.Edges.Length > 255 || m.Faces.Any(f => f.Edges.Length > 15)) return false;
        var step = 1 << m.Shift;
        bool On(int v) => v % step == 0 && v / step >= -128 && v / step <= 127;
        return m.Corners.All(c => On(c.X) && On(c.Y) && On(c.Z));
    }

    /// <summary>The models as one objfile (the checks that read the card's models through the rules' Models): each model's bytes one
    /// after the other, then zeros. A model that fits the file's format (FitsBytes) is written as the file's bytes; one that does
    /// not, as an EXTENDED model ("OW", rules/Objects.cs Models: word corners in full units, word edges, word face lists), so the
    /// rules read every one of the card's shapes. The models past the file's (AppendCopy) come last, extended too where needed.</summary>
    public byte[] ToObjfile()
    {
        var o = new List<byte>();
        for (var m = 0; m < Models.Length; m++) o.AddRange(FitsBytes(m) ? ToBytes(m, Texels) : ToWideBytes(m));
        o.AddRange(new byte[16]);
        return o.ToArray();
    }
}
