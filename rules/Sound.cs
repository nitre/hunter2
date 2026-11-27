namespace Hunter.Rules;

/// <summary>
/// The four sound channels as game state (re/formats/vehicles.md section 6): which entity holds
/// which channel ends up in records (+$98, d7 bit 2), so the allocation is ported; the audio
/// itself plays in godot/Audio.cs (Changed). Byte for byte: $1CA cursor word, then $1CC + 8 * channel: +0 held,
/// +1 priority, +2 volume, +4 period, +6 owner (record offset from $250).
/// </summary>
public sealed class Channels(byte[] main, byte[] soundfile)
{
    readonly byte[] _b = new byte[0x22];
    /// <summary>For the player's speakers only (no game state): (channel, sound, volume 0-63) when
    /// SetChannel starts a sound, sound -1 when only the volume moves (UpdateEntitySound). The last
    /// argument is the period the original hands the driver that pass: SetChannel the base from the
    /// sound file's table (one row off, $B12A reads +$422 where the driver's own table is +$430),
    /// UpdateEntitySound the base - 4 x speed ($2DFA).</summary>
    /// Sound -2: the driver's countdown for the channel's effect ran out (Tick) - the channel is
    /// silent until the next start, volume moves no longer reach it.
    public static event Action<int, int, int, int>? Changed;

    /// <summary>(SOUND VOICES: MORE): where the four channels leave a sound out - a one-shot no channel takes, a thing's engine that gets none
    /// this pass - the speakers play it on a spare voice: (key, sound, volume 0-63, period), key = the thing's slot,
    /// -1 a one-shot, -3 every spare voice silent. The game's four channels, the records' +$98 and bit 2, the save -
    /// all exactly as the original: only the speakers hear it.</summary>
    public static event Action<int, int, int, int>? Spare;
    public static bool SpareVoices;

    int BasePeriod(int sound) => soundfile[0x422 + sound * 16] << 8 | soundfile[0x423 + sound * 16];   // as Set's +$422 row

    // The sample player's own countdown per channel (1430: the effect record's +4 at soundfile
    // +$430, $114DE subq each tick, at 0 $114E4 clears AUDxVOL; $FF = no end, $114D8). Speaker state only.
    readonly int[] _left = new int[4];

    /// <summary>One tick of the sample player ($113A6, jmp'd from the copper tick $C6C6 while play runs):
    /// each effect's countdown; one that runs out goes silent (Changed with sound -2).</summary>
    public void Tick()
    {
        for (var ch = 0; ch < 4; ch++)
        {
            if (_left[ch] is 0 or 0xFF) continue;
            if (--_left[ch] == 0) Changed?.Invoke(ch, -2, 0, 0);
        }
    }
    /// <summary>$1CA: AllocChannel's cursor (StartState.MachineLeftovers). Machine state, not game
    /// state: the program never initialises it and ResetChannels $B186 does not clear it, so it
    /// survives a quit, a save and a load.</summary>
    public int Cursor { get => _b[0] << 8 | _b[1]; set { _b[0] = (byte)(value >> 8); _b[1] = (byte)value; } }
    int E(int ch) => 2 + ch * 8;
    public byte[] Bytes() => _b.ToArray();
    /// <summary>unsigned: the original's adda.w sign-extends ($2D78) - fine to slot 204; more_objects
    /// goes to 393, and a signed read here would index All[] negative from slot 205.</summary>
    public int Owner(int ch) => _b[E(ch) + 6] << 8 | _b[E(ch) + 7];
    public void SetOwner(int ch, int offset) { _b[E(ch) + 6] = (byte)(offset >> 8); _b[E(ch) + 7] = (byte)offset; }
    public void SetVolume(int ch, int v) { _b[E(ch) + 2] = (byte)(v >> 8); _b[E(ch) + 3] = (byte)v; }
    /// <summary>The channel's base period (state +4, $B12C: word from the sound file's +$422 table).</summary>
    public int Period(int ch) => _b[E(ch) + 4] << 8 | _b[E(ch) + 5];
    public void Hold(int ch, bool held) => _b[E(ch)] = (byte)(held ? 0xFF : 0);

    /// <summary>LeavePlaySound ($2E12): the sample player's 4 volume words at soundfile +$2D6 cleared - every
    /// channel goes quiet; the owners at $1CC keep their volumes (only the speakers hear it).</summary>
    public static void Silence()
    {
        for (var ch = 0; ch < 4; ch++) Changed?.Invoke(ch, -1, 0, 0);
        Spare?.Invoke(-3, -1, 0, 0);
    }
    sbyte Priority(int sound) => (sbyte)main[0xB164 - 0x800 + sound];

    /// <summary>AllocChannel ($2D30): the channel for sound d4, or -1. Stealing a held channel
    /// takes the owner's d7 bit 2 away ($2D70 -> $2D96).</summary>
    public int Alloc(int sound, EntityTable t)
    {
        var d1 = Step(Cursor);
        Cursor = d1;
        for (var n = 0; n < 4; n++, d1 = Step(d1))                          // $2D3E: a free one
            if (_b[2 + d1] == 0) return d1 >> 3;
        var d2 = Priority(sound);                                             // $2D4C
        for (var n = 0; n < 4; n++, d1 = Step(d1))
            if (d2 < (sbyte)_b[2 + d1 + 1])
            {
                var owner = t.All[Owner(d1 >> 3) / EntityTable.Size];
                owner.SetL(6, owner.Flags & ~(1 << 2));
                return d1 >> 3;
            }
        return -1;
    }

    static int Step(int d1) => d1 - 8 < 0 ? 0x18 : d1 - 8;                    // $2D68

    /// <summary>SetChannel ($B12A): d0 sound, d1 channel, d2 volume; held cleared.</summary>
    public void Set(int sound, int ch, int volume)
    {
        ch &= 3;
        var o = E(ch);
        _b[o + 1] = (byte)Priority(sound);
        SetVolume(ch, volume);
        var at = 0x422 + sound * 16;                                          // sound file table, 16 bytes a sound
        _b[o + 4] = soundfile[at]; _b[o + 5] = soundfile[at + 1];
        _b[o] = 0;
        _left[ch] = soundfile[0x430 + sound * 16 + 4];                        // $11430 (the driver's row, not +$422)
        Changed?.Invoke(ch, sound, volume, Period(ch));
    }

    /// <summary>PlaySound ($B11C): a one-shot effect.</summary>
    public void Play(int sound, EntityTable t, int volume = 0x3F)
    {
        var ch = Alloc(sound, t);
        if (ch >= 0) Set(sound, ch, volume);
        else if (SpareVoices) Spare?.Invoke(-1, sound, volume, BasePeriod(sound));
    }

    /// <summary>SoundVolume ($2DA6): 63 - min(63, max(|+$56|, |+$5A|) >> 7).</summary>
    public static int Volume(Entity e)
    {
        short x = e.W(0x56), z = e.W(0x5A);                                   // bpl / neg.w: $8000 stays $8000 (-32768)
        if (x < 0) x = (short)-x;
        if (z < 0) z = (short)-z;
        var d2 = (ushort)(z > x ? z : x) >> 7;                                // $2DB6 cmp.w, bgt: signed; $2DBC lsr.w: unsigned
        if (d2 > 63) d2 = 63;
        return ~d2 & 63;
    }

    /// <summary>StartEntitySound ($2CD6): the entity's +$88 sound on a channel.</summary>
    public void StartEntity(Entity e, ref int d7, int slot, EntityTable t)
    {
        var sound = e.L(0x88) & 0x1F;
        var ch = Alloc(sound, t);
        var v = Volume(e);
        if ((d7 >> 19 & 1) != 0 && e.W(0x3E) == 0) v = 0;
        if (ch < 0)
        {
            if (SpareVoices)
                Spare?.Invoke((d7 >> 19 & 1) != 0 ? slot : -1, sound, v,                                          // a held engine (every pass it goes
                    (d7 >> 19 & 1) != 0 ? BasePeriod(sound) - 4 * e.W(0x3E) : BasePeriod(sound));               // without) or a one-shot of its own
            return;
        }
        e.SetW(0x98, ch);
        Set(sound, ch, v);
        if ((d7 >> 19 & 1) == 0) { d7 &= ~(1 << 2); e.SetL(6, e.Flags & ~(1 << 2)); return; }   // $2D96
        Hold(ch, true);
        SetOwner(ch, slot * EntityTable.Size);
        d7 |= 1 << 2; e.SetL(6, e.Flags | 1 << 2);                            // $2D86
    }

    /// <summary>UpdateEntitySound ($2DCC, d7 bit 19 each pass).</summary>
    public void UpdateEntity(Entity e, ref int d7, int slot, EntityTable t)
    {
        if ((d7 >> 2 & 1) == 0) { StartEntity(e, ref d7, slot, t); return; }
        var v = Volume(e);
        if (e.W(0x3E) == 0) v = 0;
        var ch = e.W(0x98) & 3;
        SetVolume(ch, v);                                                     // $2E02
        Changed?.Invoke(ch, -1, v, Period(ch) - 4 * e.W(0x3E));               // $2DFA: PER := base - 4 x speed
    }

    /// <summary>A held engine kept silent (volume 0, the game's own silence) - a car whose sound is not Hunter's.</summary>
    public void Hush(Entity e, int d7)
    {
        if ((d7 >> 2 & 1) == 0) return;                                       // no channel held
        var ch = e.W(0x98) & 3;
        SetVolume(ch, 0);
        Changed?.Invoke(ch, -1, 0, Period(ch));
    }

    /// <summary>StopEntitySound ($2D24): bits 2 and 19 off, the channel silent.</summary>
    public void StopEntity(Entity e, ref int d7)
    {
        d7 &= ~(1 << 2 | 1 << 19);
        e.SetL(6, e.Flags & ~(1 << 2 | 1 << 19));
        Set(1, e.W(0x98), 0);                                                 // StopChannel $B17A
    }
}
