using System.Text.Json;

namespace Hunter.Rules;

/// <summary>Thrown when game_data/ has not been built from the user's own disk image.</summary>
public sealed class GameDataMissingException(string dir)
    : Exception($"Hunter game data not found in '{dir}'. Run the asset build: cd tools && python -m hunterdata.build <path to Disk.1> ../game_data");

/// <summary>Game modes as set by the AVAILABLE GAMES menu ($2250 -> $2354/$2358/$235C, global $20E).</summary>
public enum GameMode { Hunter = 0, Missions = 1, Action = 2 }

/// <summary>One placed world object: the 8 raw words of a $83BA record (see re/formats/sapfile.md).</summary>
public sealed record WorldObject(int Slot, ushort[] Words, int X, int Z, int? Model);

/// <summary>A starting world. Header = the file's first $3E8 bytes (re/formats/sapfile.md).</summary>
public sealed record World(GameMode Mode, IReadOnlyList<WorldObject> Objects, byte[] Header)
{
    ushort Word(int off) => (ushort)(Header[off] << 8 | Header[off + 1]);
    /// <summary>+$00 -> $212(a5): the landscape seed ($CB3C).</summary>
    public ushort Seed => Word(0);
    /// <summary>+$02 -> $210(a5): sea level subtracted from every height ($CEC2).</summary>
    public short SeaLevel => (short)Word(2);
}

/// <summary>Sine/cosine in the game's float format: value = mantissa * 2^-exponent, 256 steps per turn.</summary>
public sealed record TrigTable(IReadOnlyList<(int Exponent, int Mantissa)> Sin, IReadOnlyList<(int Exponent, int Mantissa)> Cos);

/// <summary>Everything the asset build produced, loaded once.</summary>
public sealed class GameData
{
    public required string Directory { get; init; }
    public required IReadOnlyDictionary<GameMode, World> Worlds { get; init; }
    public required TrigTable Trig { get; init; }
    /// <summary>Palette banks at $6318: 7 x 32 $0RGB words.</summary>
    public required IReadOnlyList<ushort[]> PaletteBanks { get; init; }

    public string PicturePath(string name) => Path.Combine(Directory, "pics", name + ".png");

    /// <summary>An IFF picture (re/formats/pictures.md) as the asset build decoded it: 320 x 200
    /// palette indices and its 16 CMAP colours as $0RGB. panfile (file 8) and storepic (file 9)
    /// are loaded once at start-up ($84AC).</summary>
    public (byte[] Pixels, ushort[] Palette) Picture(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, "pics", name + ".json")));
        return (Convert.FromHexString(doc.RootElement.GetProperty("pixels_hex").GetString()!),
            doc.RootElement.GetProperty("palette12").EnumerateArray().Select(c => (ushort)c.GetInt32()).ToArray());
    }

    /// <summary>The decrypted program (load address $800): its data tables are read in place.</summary>
    public byte[] Main => _main ??= File.ReadAllBytes(Path.Combine(Directory, "main.bin"));
    /// <summary>The kind table as the port reads it (ExtraKinds): the program's 175 records from $D45E, then the extra
    /// types'. Offsets are type * $34 + field.</summary>
    public byte[] Kinds => _kinds ??= ExtraKinds.Table(Main);
    byte[]? _kinds;
    /// <summary>A signed word of the type's kind record.</summary>
    public short KindWord(int type, int off) => (short)(Kinds[type * ExtraKinds.Size + off] << 8 | Kinds[type * ExtraKinds.Size + off + 1]);
    /// <summary>A byte of the type's kind record.</summary>
    public byte KindByte(int type, int off) => Kinds[type * ExtraKinds.Size + off];
    /// <summary>The talk table ($892E-$8E97) as program memory: gifts add to it in place ($8F5C), no save copies
    /// it ($10530 copies $9288/$85E2/$CF54/$9072 only) and no load or new game rewrites it - so gift counts last
    /// until the machine is switched off. One per session here, shared by every game (oq_talk_opens, 2026-10-17).</summary>
    public byte[] TalkTable => _talk ??= Main[(0x892E - 0x800)..(0x8E98 - 0x800)];
    byte[]? _talk;
    /// <summary>objfile as loaded into its 64 KB buffer.</summary>
    public byte[] Objfile => _obj ??= File.ReadAllBytes(Path.Combine(Directory, "files", "01_objfile.bin"));
    /// <summary>The model index over objfile, loaded once like the original's ($1F28 -> $8450 -> $848C is the only
    /// load): StartDebris ($5A2E, $5A4A) rewrites model 0's scale and colours in it and no new game or load
    /// reloads it - the last victim's look lasts until the machine is switched off (sweep_shared_data).</summary>
    public Models Models => _models ??= new Models((byte[])Objfile.Clone());
    Models? _models;
    public byte[] Sinfile => _sin ??= File.ReadAllBytes(Path.Combine(Directory, "files", "07_sinfile.bin"));
    /// <summary>soundfile: its table at +$422 (16 bytes a sound) gives each sound's period ($B12A).</summary>
    public byte[] Soundfile => _snd ??= File.ReadAllBytes(Path.Combine(Directory, "files", "04_soundfile.bin"));
    /// <summary>animation (file 2) as loaded at $8382: its index (8 bytes a frame) and the bitmaps
    /// from +800; BlitFrame ($C884) reads frames from it (re/formats/animation.md).</summary>
    public byte[] Animation => _anim ??= File.ReadAllBytes(Path.Combine(Directory, "files", "02_animation.bin"));
    byte[]? _anim;
    /// <summary>A world file as on disk (a fresh copy: the game edits its world as it plays).</summary>
    public byte[] WorldFile(GameMode mode) =>
        File.ReadAllBytes(Path.Combine(Directory, "files", $"{12 + (int)mode:00}_sapfile{1 + (int)mode}.bin"));
    /// <summary>file 11, the attract demo's recording, as loaded at $22D8 (RunDemo's LoadFile into
    /// the buffer the pointer at $832A holds): 1000 bytes of input records, then the pace table the
    /// pointer at $8332 holds ($3E8 bytes in). The original's buffer pointers are handed out at
    /// runtime (SetBufferPointers $83F2 fills its table from $1135C as the world loads), so the
    /// port keeps one byte[] and Demo gives the two halves their names (re/formats/jfile.md).
    /// A fresh copy each call, like WorldFile: the replay spends the record counts in place
    /// ($C82E) and ResetGameState clears the first word ($242C), so the original reloads it at
    /// every RunDemo ($22D8).</summary>
    public byte[] DemoFile() => File.ReadAllBytes(Path.Combine(Directory, "files", "11_jfile.bin"));
    byte[]? _main, _obj, _sin, _snd;

    public static GameData Load(string dir)
    {
        if (!File.Exists(Path.Combine(dir, "inventory.json")))
            throw new GameDataMissingException(dir);

        var worlds = new Dictionary<GameMode, World>();
        foreach (var mode in Enum.GetValues<GameMode>())
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "worlds", mode.ToString().ToLowerInvariant() + ".json")));
            var objs = doc.RootElement.GetProperty("objects").EnumerateArray().Select(o => new WorldObject(
                o.GetProperty("slot").GetInt32(),
                o.GetProperty("words").EnumerateArray().Select(w => (ushort)w.GetInt32()).ToArray(),
                o.GetProperty("x").GetInt32(),
                o.GetProperty("z").GetInt32(),
                o.GetProperty("model").ValueKind == JsonValueKind.Null ? null : o.GetProperty("model").GetInt32())).ToList();
            worlds[mode] = new World(mode, objs, Convert.FromHexString(doc.RootElement.GetProperty("header_hex").GetString()!));
        }

        using var sin = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "tables", "sinfile.json")));
        static List<(int, int)> Pairs(JsonElement a) =>
            a.EnumerateArray().Select(p => (p[0].GetInt32(), p[1].GetInt32())).ToList();
        var trig = new TrigTable(Pairs(sin.RootElement.GetProperty("sin")), Pairs(sin.RootElement.GetProperty("cos")));

        using var pal = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "tables", "palettes.json")));
        var banks = pal.RootElement.GetProperty("banks").EnumerateArray()
            .Select(b => b.EnumerateArray().Select(c => (ushort)c.GetInt32()).ToArray()).ToList();

        return new GameData { Directory = dir, Worlds = worlds, Trig = trig, PaletteBanks = banks };
    }
}
