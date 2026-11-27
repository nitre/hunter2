namespace Hunter.Rules;

/// <summary>
/// One of the live objects: a mirror of the original's $A0-byte record at $250(a5) + n * $A0,
/// kept byte-for-byte so ported routines can use the same offsets. Fields the renderer reads:
/// +$00 alive, +$06 flags (long), +$1C in view, +$1E on ground, +$20 player inside,
/// +$24/+$25 base and main model, +$4A/+$4C/+$4E position, +$56-$5A camera-relative position,
/// +$72 depth offset, +$74 ground height, +$7C/+$7E/+$80 pitch/yaw/roll bytes, +$84 animation inputs.
/// </summary>
public sealed class Entity
{
    public readonly byte[] B = new byte[0xA0];

    public short W(int o) => (short)(B[o] << 8 | B[o + 1]);
    public ushort UW(int o) => (ushort)(B[o] << 8 | B[o + 1]);
    public int L(int o) => B[o] << 24 | B[o + 1] << 16 | B[o + 2] << 8 | B[o + 3];
    public void SetW(int o, int v) { B[o] = (byte)(v >> 8); B[o + 1] = (byte)v; }
    public void SetL(int o, int v) { SetW(o, v >> 16); SetW(o + 2, v); }

    public bool Alive => B[0] != 0;
    public int Flags => L(6);
    /// <summary>(more objects): held out past the original's wake area (Behaviour.Steer);
    /// on coming in it is woken afresh (StartState.SpawnNearCamera). Not saved: after a load it just walks on.</summary>
    public bool HeldFar;
    /// <summary>(LIVING WORLD): who last hurt it (Collisions $454E), for its kill's score
    /// (Vehicles.End). Not saved.</summary>
    public LivingWorld.Side HurtBy;
    /// <summary>picture only (EXPLOSIONS: PIECES, the card): where the last shell, missile,
    /// mine or blast that hurt it was at the touch (Collisions $454E) - its pieces fly from there, not from its own
    /// middle. Cleared by any other hurt (a bullet, a ram) and by the slot's rebuild. Nothing in the game reads it; not saved.</summary>
    public bool BlastHit;
    public ushort BlastX, BlastZ;
    public short BlastY;
    /// <summary>picture only, with BlastHit: that shell's, missile's or blast's type (+4) - the card's small stars copy its star.</summary>
    public int BlastType;
    /// <summary>(a pass per blank): a shot still in its shooter's box since it left his hand -
    /// the two do not bump (Collisions.Check). The A500's ~7 blanks a pass took its first step clear of him; at one
    /// blank a grenade (a mover, no owner check on a bump) went off on the thrower. Not saved.</summary>
    public bool InHand;
    /// <summary>ENHANCEMENT (A500 PACE, WorldState.A500Pace): its height (+$4C) at the last A500 pass, int.MinValue for none -
    /// a landing's drop is measured from there (Movement.Settle). Not saved.</summary>
    public int PaceY = int.MinValue;
    /// <summary>ENHANCEMENT (FINE POSITIONS, WorldState.FinePositions): the part of a unit each axis
    /// of a step left over (0 <= f < 1, above the record's word), and the words it belongs to - set from anywhere else, it
    /// starts again at 0. Not saved.</summary>
    public double FracX, FracY, FracZ;
    public (short X, short Y, short Z) FracAt = (short.MinValue, short.MinValue, short.MinValue);
    /// <summary>ENHANCEMENT (FINE ANGLES): the pitch, heading and roll
    /// at 65536 a turn, beside the record's bytes (+$7C, +$7E, +$80: 256 a turn). Filled where the game has the finer value
    /// itself - its own angle words where its code keeps them whole (a turn, Movement $29FA / $2A50; a flier's climb) and
    /// the smooth picture's in-between poses - so nothing in play reads them and the original's maths is untouched. Only
    /// the picture does (the card, GpuWorld), and only while the byte still agrees (Frac): any code that writes the byte
    /// alone, or copies a record, leaves a fine value that no longer fits it, and the byte's own whole step is drawn. Not saved.</summary>
    public ushort PitchFine, HeadingFine, RollFine;
    /// <summary>ENHANCEMENT (VEHICLE HANDLING: WEIGHTY, Handling.cs): the driven vehicle's own speed. Not saved.</summary>
    public Movement.DriveBody? Drive;

    /// <summary>The part of a step (1/256 steps, -128..255) the fine angle lies past the byte b, or 0 when it does not fit
    /// it (more than half a step below or a whole step above: the byte was written since).</summary>
    public static int Frac(ushort fine, byte b)
    {
        var d = (short)(fine - (b << 8));
        return d >= -128 && d <= 255 ? d : 0;
    }

    /// <summary>btst.b #n on byte +off of the record (the 68000 takes n mod 8 for memory).</summary>
    public bool Bit(int off, int n) => (B[off] >> (n & 7) & 1) != 0;
    /// <summary>btst on the flags long held in a register (d7 = +$06).</summary>
    public bool Flag(int n) => (Flags >> n & 1) != 0;
}

public sealed class EntityTable
{
    /// <summary>The records there is room for: the original's 30, 395 with more_objects (394 handed out):
    /// the most the original's links between records hold. A shot's shooter (+$2C) is the record's
    /// address low word, slot x $A0 + $A56, which slot 393 is the last to fit ($FFF6); the target
    /// (+$16), the vehicle (+$22) and a sound's owner are slot x $A0 and fit to 409. The most seen
    /// live with more objects: 226, the wild demo (byte-limits.md). All always holds this many; how
    /// many are LIVE is Count.</summary>
    public const int MaxCount = 395, Size = 0xA0;
    int _count = 30;
    /// <summary>The live records every pass walks ($2610 behaviour, $4164 sort, $33E6 write-back,
    /// $A4EA radar). 30 as the original ($250-$48F); MaxCount with Play.MoreObjects, which sizes the
    /// table at Init / load, before the first pass. Slots past it exist but stay free - nothing
    /// hands them out (Spawnable) - so turning it back off cannot strand a live record.</summary>
    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            for (var i = value; i < MaxCount; i++) Array.Clear(All[i].B);
        }
    }
    /// <summary>The slots handed out ($3398 FindFreeSlot / $314C SpawnInFreeSlot: 29 tests,
    /// slots 0-28). The last slot stays reserved exactly as the original never hands out slot 29 -
    /// the port keeps that margin for the same reason (Vehicles.FindFreeSlot's note). 394 with
    /// more_objects: every slot read that turns a word back into a record is unsigned (see
    /// Vehicles.EnterOrLeaveVehicle / Behaviour.Steer / Sound.Owner / LivingWorld.OwnerSlot), so the
    /// last handed out, slot 393, reads fine (MaxCount's note).</summary>
    public int Spawnable => Count - 1;

    public readonly Entity[] All = Enumerable.Range(0, MaxCount).Select(_ => new Entity()).ToArray();

    /// <summary>The live records in slot order, as at $250(a5).</summary>
    public byte[] Bytes() => All.Take(Count).SelectMany(e => e.B).ToArray();

    public static EntityTable FromBytes(ReadOnlySpan<byte> raw)
    {
        var t = new EntityTable();
        for (var i = 0; i < t.Count; i++) raw.Slice(i * Size, Size).CopyTo(t.All[i].B);
        return t;
    }
}
