namespace Hunter.Rules;

/// <summary>room for types past the original's 175. The program holds $00-$AE at
/// $D45E + type * $34 (re/kind_fields.txt); right after it, where $AF's record would be, is the copy-protection code. So
/// the port keeps its own copy of the table (GameData.Kinds): the original's 175 records byte for byte, then ours. Every
/// kind read goes through it (Arms.Kind, LivingWorld, Games, Pregame, Hunt, StartState's builder).
/// - THE HARVESTER ($AF): a vehicle you drive. Its record starts as the truck's ($00) - flags (a road vehicle you can get
///   in, burns fuel), its sound, fuel, boarding and box (the rules keep the truck's objfile models; the card draws the Dune
///   II harvester, DuneModels) - then slower (top speed 4, the truck's 6) and tougher (energy 40, the truck's 20).
/// - THE SPICE FLOWER ($B0): the red flower's ($77) record with its ghost bit (21) off, so a touch registers: a harvester
///   that touches one takes it (Spice.Touch), anything else goes straight through as through any flower. Only on the sand
///   face (Spice.Plant). The card draws it blue-black.
/// The kind word of an extra type carries on the type table's last class (11, from $AE): (type - $AE) &lt;&lt; 8 | 11, so a
/// map record's rec[0] + Types[rec[1]] gives the type back as for any original one; past 256 of them the classes go on,
/// 12, 13, ... each 256 types on (TypeBase). Nothing reads the class byte. NO CEILING: types are full words everywhere the port reads them
/// (+$04, not its low byte), the kill counts and the save's extension are sized by End, so End can grow past 256.</summary>
public static class ExtraKinds
{
    public const int Original = 0xAF;            // the program's own types: $00-$AE
    public const int Harvester = 0xAF, SpiceFlower = 0xB0;
    /// <summary>One past the last type.</summary>
    public const int End = 0xB1;
    public const int At = 0xD45E, Size = 0x34;
    const int LastClass = 11, ClassBase = 0xAE;

    public static bool Is(int type) => type >= Original && type < End;

    /// <summary>A record of this type is a vehicle a driver sits in - the original's test (type below $1E: "not in a vehicle"
    /// at $2FE8, "boarded a X" at $2EA0, a fall at $2C92) and the harvester. The original's tests that read "past the
    /// people and items: an end" ($2EC4 type >= $35) skip it the same way (IsEnd).</summary>
    public static bool IsVehicle(int type) => type < 0x1E || type == Harvester;

    /// <summary>The player's record became an end - an explosion, a grave ($2EC4: type $35 or more) - not the harvester.</summary>
    public static bool IsEnd(int type) => type >= 0x35 && type != Harvester;

    /// <summary>The kind word of an extra type (KindForType's carry-on): index &lt;&lt; 8 | class.</summary>
    public static int KindWord(int type) { var (i, c) = RecordKind(type); return i << 8 | c; }

    /// <summary>A map record's first two bytes for a type (its kind word): class 11 from $AE, then a class every 256 types.</summary>
    public static (byte Index, byte Class) RecordKind(int type) => ((byte)((type - ClassBase) % 256), (byte)(LastClass + (type - ClassBase) / 256));

    /// <summary>The type a record's class starts at ($81BA's word for the program's 0-11; ours, 256 types each, from 12 on -
    /// the program's word 12 is the walk's end mark $7FFF, never a record's class).</summary>
    public static int TypeBase(byte[] main, int cls) =>
        cls <= LastClass ? main[0x81BA - 0x800 + cls * 2] << 8 | main[0x81BA - 0x800 + cls * 2 + 1] : ClassBase + (cls - LastClass) * 256;

    /// <summary>How many types the port's per-type tables hold (the kill counts, the save's): every type, at least the original's 256 bytes.</summary>
    public static int Slots => Math.Max(256, End);

    /// <summary>The type's name as the game prints it (the names list, Text table 1), for the extra types.</summary>
    public static string? Name(int type) => type switch { Harvester => "harvester", SpiceFlower => "spice flower", _ => null };

    /// <summary>The whole table: the program's 175 records, then the extra ones.</summary>
    public static byte[] Table(byte[] main)
    {
        var t = new byte[End * Size];
        Array.Copy(main, At - 0x800, t, 0, Original * Size);
        Make(t, Harvester, from: 0x00, r =>
        {
            Word(r, 0x0A, 40);                   // energy: twice the truck's
            Word(r, 0x12, 4);                    // top speed: the truck's 6, a tank's 5 - it is a slow, heavy thing
            Word(r, 0x32, 30);                   // kill score: the truck's 20
        });
        Make(t, SpiceFlower, from: 0x77, r =>
        {
            var flags = (r[0] << 24 | r[1] << 16 | r[2] << 8 | r[3]) & ~(1 << 21);   // not a ghost: its touch counts
            r[0] = (byte)(flags >> 24); r[1] = (byte)(flags >> 16); r[2] = (byte)(flags >> 8); r[3] = (byte)flags;
        });
        return t;
    }

    static void Make(byte[] t, int type, int from, Action<byte[]> change)
    {
        Array.Copy(t, from * Size, t, type * Size, Size);
        var r = new byte[Size];
        Array.Copy(t, type * Size, r, 0, Size);
        change(r);
        r.CopyTo(t, type * Size);
    }

    static void Word(byte[] r, int off, int v) { r[off] = (byte)(v >> 8); r[off + 1] = (byte)v; }
}
