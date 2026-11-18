namespace Hunter.Rules;

/// <summary>
/// The keys and the mouse in play (re/formats/game.md section 3). Every key goes through KeyDown
/// ($B214, from the keyboard interrupt $B1AC): its ASCII from $B33A, 'p' toggles the pause ($99
/// bit 5), and while play runs ($99 bit 7) ItemHotkeys ($A816) looks it up in "crlmfa~" + $FF
/// ($A806): a carried item's key ($238 bit, item at $A80E) does what a click on its cell of the
/// item table does (ClickDispatch $AF6E on $A5FA); any other key goes to PlayKeys ($3562: Return, Esc,
/// Space). A mouse press runs HitTestMouse ($AF64) from the blank on the play regions ($2460:
/// next weapon, SPEAK, GIVE, QUIT, save). The routines only set flags; EndOfPass ($3476) acts on
/// $A7 at the end of the pass.
/// </summary>
public static class Keys
{
    const int Ascii = 0xB33A, KeyTable = 0xA806, KeyItems = 0xA80E, Cells = 0xA5FA, Regions = 0x2460;

    static int Word(WorldState s, int addr) => (short)(s.Data.Main[addr - 0x800] << 8 | s.Data.Main[addr - 0x800 + 1]);
    static int Long(WorldState s, int addr) => Word(s, addr) << 16 | (ushort)Word(s, addr + 2);

    /// <summary>KeyDown ($B214). shift / ctrl: the key table's $60/$61 and $63 held ($B224, $B24A).
    /// d7: the interrupted code's d7, which FireShot reads (only the flare and the drone).
    /// Multiplayer: n = whose key; 0 is the original.</summary>
    public static void Down(Play play, byte raw, bool shift, bool ctrl, int d7, int n = 0)
    {
        var s = play.State;
        // $B214-$B236: $98 bit 6, raw -> $1C0, ASCII -> $1C1; $98 bit 7 (text input) is clear in
        // play; $B26C stores the character in the typed-line buffer $B31A (not used in play)
        var ascii = s.Data.Main[Ascii - 0x800 + raw + (shift ? 0x70 : 0)];
        if (play.Screens.Typing) { play.Screens.TypeKey(play.View, raw, ascii); return; }   // $B23A tst.b $98: bmi rts
        if (ascii == 0x0D) ascii = (byte)'*';                                // $B276: Return becomes '*' (and $98 bit 4)
        if (ctrl) return;                                                    // $B24A tst.b $63(a4)
        if (ascii == 'p') s.PauseKey = !s.PauseKey;                          // $B258 bchg #5,$99
        if (play.PauseLoopEnds()) return;                                    // the loop's next poll ($34E6): $34EE bset #7,$99
        if (!s.InPlay) return;                                               // $B260 btst #7,$99
        ItemHotkeys(s, raw, ascii, d7, n);
    }

    // ItemHotkeys ($A816): 8 entries of $A806 (the last is $FF); the item 8 bytes on
    static void ItemHotkeys(WorldState s, byte raw, byte ascii, int d7, int n)
    {
        for (var k = 0; k < 8; k++)
        {
            if (s.Data.Main[KeyTable - 0x800 + k] != ascii) continue;
            var item = s.Data.Main[KeyItems - 0x800 + k];                    // $A82C move.b 7(a0),d0
            if ((s.PlayerAt(n).Items238 >> item & 1) == 0) return;           // $A834 btst d0,d1: not carried, nothing (piece 4: n's mask)
            s.Item240 = (short)item;                                         // $A838
            ClickDispatch(s, Cells, item % 10 * 32, item / 10 * 16, d7, n);        // $A83C-$A850: the item's cell
            return;
        }
        PlayKeys(s, raw, ascii, n);                                          // $A824 jmp $3562
    }

    // PlayKeys ($3562): d0 = raw, d1 = ASCII
    static void PlayKeys(WorldState s, byte raw, byte ascii, int n)
    {
        if (raw == 0x44) s.Flags0A7 |= 2;                                    // Return: NextWeaponKey $3538
        else if (raw == 0x45) QuitKey(s);                                    // Esc: $351C
        else if (ascii == 0x20) Vehicles.LeaveVehicleKey(s.Entities.All[n]);        // $A766
    }

    /// <summary>The blank's press ($C726: $96 bit 7, a button newly down): HitTestMouse ($AF64)
    /// on the play regions with the pointer ($DE, $E0). Multiplayer: n = whose click.</summary>
    public static void Click(WorldState s, int d7, int n = 0) => ClickDispatch(s, Regions, s.PointerX, s.PointerY, d7, n);

    /// <summary>ClickDispatch ($AF6E): the first 16-byte region (x1, x2, y1, y2, routine, value) with
    /// x1 &lt;= x &lt; x2, y1 &lt;= y &lt; y2 until x1 is negative; $108 = its index; a value is put in
    /// $104 (menus), else its routine runs with d0/d1 = the point inside it and d7.w = the index.</summary>
    internal static void ClickDispatch(WorldState s, int table, int x, int y, int d7, int n = 0)
    {
        d7 &= unchecked((int)0xFFFF0000);                                    // $AF6E clr.w d7
        for (var a = table; Word(s, a) >= 0; a += 16, d7++)
        {
            if (x < Word(s, a) || x >= Word(s, a + 2) || y < Word(s, a + 4) || y >= Word(s, a + 6)) continue;
            s.Region108 = (short)d7;                                         // $AFA0
            if (Long(s, a + 12) != 0) { s.Choice104 = Long(s, a + 12); return; }   // $AFAE
            Run(s, Long(s, a + 8), x - Word(s, a), d7, y - Word(s, a + 4), n);   // $AFB6 move.l (a0),d2; jump
            return;
        }
    }

    // a region's routine, d0 = x within it; n = the clicking player (its record p)
    static void Run(WorldState s, int routine, int d0, int d7, int d1 = 0, int n = 0)
    {
        var p = s.Entities.All[n];
        switch (routine)
        {
            case 0: return;                                                  // $AFB8: none
            case 0xA39C: s.Weapon = (short)(d0 >> 5); Vehicles.LoadWeapon(s, p); return;   // weapons row: $A378 $200, LoadWeapon
            case 0xA714: s.Flags0A2 |= 8; return;                            // '~': the hover ($6D54 per blank)
            case 0xA702:                                                     // ClockToggle: bchg #6,$A7, PanelClock $C43A
                s.Flags0A7 ^= 0x40;
                s.Panel.Clock(s);
                return;
            case 0x3540:                                                     // RadarToggle: bchg #3,$99, $A480
                s.Radar = !s.Radar;
                s.Panel.Radar(s);
                return;
            case 0xA70C: s.Flags0A7 |= 0x20; return;                         // LogBookIcon
            case 0x354A: s.MapMarks = !s.MapMarks; return;                   // MapToggle: bchg #2,$99
            case 0xA71C: Vehicles.AddEnergy(s, 6); UseItem(s, n); return;       // AddEnergy(6), $A296
            case 0xA7DC: Refuel(s, p, n); return;
            case 0xA79C: FireFlare(s, p, d7, n); return;
            case 0xA776: LaunchDrone(s, p, d7, n); return;
            case 0xA766: Vehicles.LeaveVehicleKey(p); return;
            case 0xA726:                                                     // bchg #5,$A4, then FootModels' body ($A72C)
                s.Flags0A4 ^= 0x20;
                Vehicles.FootModels(s, p, p.Flags);
                return;
            case 0xA6F6: Messages.Show(s, 0xA6E2); return;                   // CannotUse: "You cannot use that"
            case 0x3538: s.Flags0A7 |= 2; return;                            // NextWeaponKey
            case 0x3530: s.Flags0A7 |= 8; return;                            // SpeakClick
            case 0x3528: s.Flags0A7 |= 1; return;                            // GiveClick
            case 0x8F46: Talk.Give(s, n); return;                            // GiveItem (the GIVE table $A6CE)
            case 0x351C: QuitKey(s); return;
            case 0x3508:                                                     // SaveQuitClick: not inside -> $A5 bit 6, QuitKey
                if (s.InsideView) return;
                s.Flags0A5 |= 0x40;
                QuitKey(s);
                return;
            case 0x9B46 or 0x9B54 or 0x9B82 or 0x9BCA or 0x9BF0 or 0x9D26: Store.Run(s, routine, d0, d1); return;   // a store's screen
            default: throw new NotSupportedException($"${routine:X}: region routine not read");
        }
    }

    // QuitKey ($351C): unless inside a building ($355A btst #0,$99), $A5 bit 7; MainLoop leaves at $2586
    internal static void QuitKey(WorldState s)
    {
        if (!s.InsideView) s.Flags0A5 |= 0x80;
    }

    // $A296: the item in use ($240) one less ($A344, add.w #-1), $A2A8 the carried masks, then
    // LoadWeapon ($A2A4 pea $A37C) - the using player's own ($A37C). Piece 4: n's own table.
    internal static void UseItem(WorldState s, int n = 0)
    {
        var t = s.PlayerAt(n).Items;
        var o = 0x28 + s.Item240 * 32;
        var count = (short)(t[o] << 8 | t[o + 1]) - 1;
        t[o] = (byte)(count >> 8); t[o + 1] = (byte)count;
        Items.Carried(s, n);
        Vehicles.LoadWeapon(s, s.Entities.All[n]);
    }

    // $A7DC: motorised (+$06 bit 26) only: fuel +$34 += $2000, $7F00 when that goes negative; $A296
    static void Refuel(WorldState s, Entity p, int n)
    {
        if (!p.Bit(0x06, 26)) return;                                        // $A7E4 beq $A6FE
        var d0 = (short)(0x2000 + p.W(0x34));
        if (d0 < 0) d0 = 0x7F00;                                             // $A7F4 bpl
        p.SetW(0x34, d0);
        UseItem(s, n);                                                       // $A7E8 pea $A296
    }

    // FireFlare ($A79C): not inside ($99 bit 0): a type $33 shot from the player (LaunchProjectile $30BE),
    // its +$7E word cleared, $18A += $4B0 blanks, light $184 = $8000, $A2 bit 0, $A296.
    // ($A7CA $68DC(2) fades the colours toward 15: Play's blank issues it at the bit's next change,
    // rules/Fade.cs.)
    static void FireFlare(WorldState s, Entity p, int d7, int n)
    {
        if (s.InsideView) return;                                            // $A7A2 -> $A6FE
        Weapons.Fire(s, p, 0, d7, 0x33)?.SetW(0x7E, 0);                      // $A7AC, $A7B0 clr.w $7E(a0)
        s.Timer18A += 0x4B0;
        s.Light184 = 0x8000;
        s.Flags0A2 |= 1;
        UseItem(s, n);
    }

    // LaunchDrone ($A776): not inside: a type $34 shot, +$7E cleared, $188 = 100 blanks, $A2 bit 1,
    // $A296. When $188 runs out BlankTimers sets $A7 bit 2: the map screen ($3CFA).
    static void LaunchDrone(WorldState s, Entity p, int d7, int n)
    {
        if (s.InsideView) return;                                            // $A77C -> $A6FE
        Weapons.Fire(s, p, 0, d7, 0x34)?.SetW(0x7E, 0);                      // $A784, $A788
        s.Timer188 = 100;
        s.Flags0A2 |= 2;
        UseItem(s, n);
    }

    /// <summary>EndOfPass ($3476): the first flag set runs its routine, which returns to MainLoop
    /// itself (the touch guard at $34D2 is then skipped).</summary>
    public static void EndOfPass(WorldState s)
    {
        // $3476 $99 bit 1 -> VisitBuilding $46F0: the door test that sets it is task 7
        if (s.Flags0A7 != 0)
        {
            if ((s.Flags0A7 & 1) != 0) { s.ScreenRoutine = 0xA576; return; } // $3488 -> ItemTable, which clears bit 0: the GIVE cells
            if (Clear(s, 2)) { NextWeapon(s); return; }                      // $3490 -> $A31C
            if (Clear(s, 4)) { s.ScreenRoutine = 0x3CFA; return; }           // $349C -> MapScreen (Play, Screens.cs)
            if (Clear(s, 8)) { Talk.Speak(s); return; }                      // $34A0 -> Speak $8F82
            if (Clear(s, 0x10)) { s.Light184 = 0; s.Panel.Hour(s); return; } // FlareOut $A7D4: clr.l $184, PanelTimeAndLight $C45E
            if (Clear(s, 0x20)) { s.ScreenRoutine = 0xD2EC; return; }        // $34B4 -> LogBook (Play, Screens.cs)
        }
        if (s.PointerY == 0) { s.ScreenRoutine = 0xA576; return; }           // $34C0 tst.w $E0: the pointer at the top -> ItemTable (USE)
        if ((s.Flags0A5 & 4) != 0) s.Flags0A5 &= 0xFB;                       // $34D2: still touching: keep bit 1
        else s.Flags0A5 &= 0xFD;
    }

    static bool Clear(WorldState s, byte bit)
    {
        if ((s.Flags0A7 & bit) == 0) return false;
        s.Flags0A7 &= (byte)~bit;
        return true;
    }

    // NextWeapon ($A31C): from $200 + 1 round 0-8, the first carried weapon (bits 0-8 of $238;
    // none: item 0), at most 9 steps, -> $A378 ($200, LoadWeapon); none found: nothing
    static void NextWeapon(WorldState s, int n = 0)
    {
        var d2 = s.PlayerAt(n).Items238 & 0x1FF;                             // piece 4: n's carried weapons
        if (d2 == 0) d2 = 1;                                                 // $A32C bset #0,d2
        var d0 = (int)s.Weapon;
        for (var d1 = 8; d1 >= 0; d1--)
        {
            if (++d0 >= 9) d0 = 0;
            if ((d2 >> d0 & 1) == 0) continue;
            s.Weapon = (short)d0;
            Vehicles.LoadWeapon(s, s.Entities.All[n]);
            return;
        }
    }
}
