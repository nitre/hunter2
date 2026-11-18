namespace Hunter.Rules;

/// <summary>
/// Shots (re/formats/weapons.md): Movement.FireTimer (NoWeaponReturn $2680 to $26BE) with rounds in hand runs LaunchProjectile ($30BE),
/// which builds a record of the weapon's shot type (+$26) at the muzzle (+$62.., the gun port
/// the renderer found, $52E0) through SpawnInFreeSlot ($314C). The shot is an ordinary record:
/// it moves by its own mode, homes in by behaviour (enemy shots), ends on its timer (d7 bit 17)
/// and hurts what it touches (d7 bit 20, $454E, not its owner +$2C).
/// </summary>
public static class Weapons
{
    static bool Bit(int v, int n) => (v >> n & 1) != 0;

    /// <summary>LaunchProjectile ($30BE): d3 = the shot type (+$26 when fired by FireTimer; the flare and
    /// the drone pass $33 / $34). A round was already taken. Returns a0 as FireShot leaves it: the
    /// new record, slot 1 when the table is full ($316A lea $2F0,a0), null when it fired nothing
    /// before the search (a0 is then the caller's).</summary>
    public static Entity? Fire(WorldState s, Entity e, int slot, int d7, int? type = null)
    {
        var d3 = type ?? e.W(0x26);
        if (d3 == 0x5A && e.W(0x4C) - e.W(0x74) <= 0x258) { e.SetW(0x32, e.W(0x32) + 1); return null; }   // $30C4: too low
        int x = e.W(0x62), z = e.W(0x66);
        var y = (short)(e.W(0x64) + e.W(0x4C) + e.W(0x7A));                  // $30D2
        if (y <= 0) { e.SetW(0x32, e.W(0x32) + 1); return null; }            // $30E2: in the ground: round back
        if ((s.Flags0A4 & 2) == 0)                                           // $30E8: the alarm
        {
            s.Flags0A4 |= 2;
            s.Timer222 = 0x3E8;
            s.Flags0A2 |= 0x10;
        }
        e.B[0x1D] = 0;                                                       // $30FE
        var off = (short)(slot * EntityTable.Size + 0x250 + 0x806);          // A4's low word: the record's address
        s.Spawn24C = Bit(d7, 11) ? (short)0 : e.W(0x3E);                     // the shooter's speed (not helicopters)
        e.SetW(0x2C, off);                                                   // $3112 owner, on the shooter too
        s.Spawn24A = off;
        x = (short)(x + e.W(0x4A));
        z = (short)(z + e.W(0x4E));
        var d4 = (e.W(0x7C) & 0xFF00 | (Bit(d7, 25) ? e.B[0x1A] : e.B[0x7E])) << 16 | e.B[0x80] << 8;   // $3122-$313A
        var f20 = (byte)(e.B[0x20] != 0 ? 0xFF : 0);                         // $313C $24F
        s.Spawn24E = (byte)(Bit(d7, 31) ? 0xFF : 0);                         // $3144: the player's shot
        return StartState.SpawnInFreeSlot(s, (ushort)x, y, (ushort)z, d3, d4, f20)   // $314C, d3 ext.l: kind 0
            ?? s.Entities.All[s.Players];                                    // $316A: none free, a0 = the first non-player slot (1 today)
    }
}
