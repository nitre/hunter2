using System.Text;

namespace Hunter.Rules;

/// <summary>
/// The save disk (re/formats/save.md section 2) as a folder: file 15 is the directory (224 bytes),
/// file 16 + slot the saved game. The original swaps disks ($103B6/$103DC/$1043E); the remake has
/// no swapping, so the files just sit in the folder and no "insert disk" prompt is shown.
/// </summary>
public sealed class SaveDisk
{
    public const int Slots = 6, DirectorySize = 224, SlotAt = 0x20, SlotSize = 0x20, NameAt = 0x10, NameMax = 14;
    const string Header = "HUNTER SAVES 1.1";                                       // $10166

    readonly string _dir;

    public SaveDisk(string dir) => _dir = dir;

    string PathOf(int file) => Path.Combine(_dir, $"file{file:00}.bin");

    /// <summary>The options file the shell keeps beside save file NN (the same number, .cfg): the rules
    /// name it, the shell fills it (the rules never see a setting).</summary>
    public string OptionsPath(int file) => Path.Combine(_dir, $"file{file:00}.cfg");

    /// <summary>ReadDirectory $101C2: the directory, or null when the disk has none yet - the read
    /// fails ($101D0 bmi -> $101B2, d0 = -1) and Load / Save go back to the disk menu: Init Disk first.</summary>
    public byte[]? ReadDirectory()
    {
        var p = PathOf(15);
        return File.Exists(p) && File.ReadAllBytes(p) is { Length: DirectorySize } d ? d : null;
    }

    /// <summary>InitDiskButton $10176/$10182-$101B4: the header, then 6 slots from +$20, each marked free
    /// ($FF at +0, "free*" at +$10); written as file 15. The remake first copies every save file into
    /// backup\&lt;date-time&gt; (owner 2026-10-20: a disk is never wiped without a copy).</summary>
    public byte[] Init()
    {
        Backup();
        var d = new byte[DirectorySize];
        Encoding.ASCII.GetBytes(Header).CopyTo(d, 0);
        for (var s = 0; s < Slots; s++)
        {
            d[SlotAt + s * SlotSize] = 0xFF;
            Encoding.ASCII.GetBytes("free*").CopyTo(d, SlotAt + s * SlotSize + NameAt);
        }
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(PathOf(15), d);
        return d;
    }

    /// <summary>Copies the directory, the saved games (files 15-21) and the quick slot (file 22)
    /// that exist into backup\yyyyMMdd-HHmmss; the folder of what was there before an Init Disk,
    /// or null when there was nothing.</summary>
    public string? Backup()
    {
        var files = Enumerable.Range(15, 1 + Slots).Append(QuickFile).SelectMany(n => new[] { PathOf(n), OptionsPath(n) }).Where(File.Exists).ToList();   // the options files beside them ride the backup too
        if (files.Count == 0) return null;
        var to = Path.Combine(_dir, "backup", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        for (var n = 2; Directory.Exists(to); n++) to = Path.Combine(_dir, "backup", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + n);
        Directory.CreateDirectory(to);
        foreach (var f in files) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        return to;
    }

    /// <summary>$10356 tst.b (A3): a negative first byte is a free slot.</summary>
    public static bool IsFree(byte[] directory, int slot) => (sbyte)directory[SlotAt + slot * SlotSize] < 0;

    /// <summary>CopyName $B80E from slot +$10: up to 14 characters, ending at the '*'.</summary>
    public static string Name(byte[] directory, int slot)
    {
        var sb = new StringBuilder();
        var o = SlotAt + slot * SlotSize + NameAt;
        for (var i = 0; i < NameMax && directory[o + i] != '*' && directory[o + i] != 0; i++) sb.Append((char)directory[o + i]);
        return sb.ToString();
    }

    /// <summary>The block of a saved game (file 16 + slot).</summary>
    public byte[]? ReadGame(int slot)
    {
        var p = PathOf(16 + slot);
        return File.Exists(p) ? File.ReadAllBytes(p) : null;
    }

    /// <summary>$1024A-$10290: the slot number goes in as a word at +0, the typed name at +$10 ('*'
    /// ends it), file 15 is rewritten; $1047C writes the block as file 16 + slot.</summary>
    public void Save(int slot, IReadOnlyList<byte> name, byte[] block)
    {
        var d = ReadDirectory() ?? throw new InvalidOperationException("no save directory: Init Disk first ($101C2)");
        var o = SlotAt + slot * SlotSize;
        d[o] = 0; d[o + 1] = (byte)slot;
        Array.Clear(d, o + NameAt, 16);
        var n = Math.Min(name.Count, NameMax);
        for (var i = 0; i < n; i++) d[o + NameAt + i] = name[i];
        d[o + NameAt + n] = (byte)'*';
        File.WriteAllBytes(PathOf(15), d);
        File.WriteAllBytes(PathOf(16 + slot), block);
    }

    /// <summary>The quick slot (the shell's easy_saves, F5 / F4): its own file, file 22 - none of
    /// the 6 slots (16-21), no directory entry, so it never shows in the slot menu. The original
    /// has no quick save; the block is the same one a slot save writes.</summary>
    public const int QuickFile = 22;

    /// <summary>The game block written to the quick slot, file 22.</summary>
    public void SaveQuick(byte[] block)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(PathOf(QuickFile), block);
    }

    /// <summary>The quick slot's block, or null when no quick save has been made.</summary>
    public byte[]? ReadQuick() => File.Exists(PathOf(QuickFile)) ? File.ReadAllBytes(PathOf(QuickFile)) : null;
}
