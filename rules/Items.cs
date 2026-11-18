namespace Hunter.Rules;

/// <summary>
/// Touching things with d7 bit 16 (re/formats/people.md section 5): TouchItem ($A240), once per
/// contact ($34F6 / EndOfPass). +$92 >= 0: item +$92 into the pack (+$93 more, up to the
/// item's maximum), the record freed. +$92 negative: a special touch by +$93 ($A856): a
/// soldier (2) asks for a word ($A7 bit 3 -> $8F82 at the end of the pass; the word itself is
/// task 6). Pick-ups and refusals say so on the message line ($A284 -> $C1B2, Messages.cs).
/// The guard's end-of-pass part and the word's sound are in Keys.EndOfPass ($3476).
/// </summary>
public static class Items
{
    static int Item(int i) => 0x28 + i * 32;
    static short W(byte[] t, int o) => (short)(t[o] << 8 | t[o + 1]);
    static void Set(byte[] t, int o, int v) { t[o] = (byte)(v >> 8); t[o + 1] = (byte)v; }
    /// <summary>Multiplayer piece 4: every table access is the touching player's own pack.</summary>
    static byte[] T(WorldState s, int n) => s.PlayerAt(n).Items;

    /// <summary>TouchItem ($A240). Multiplayer: n = the touching player
    /// (its $A37C LoadWeapon); 0 is the original.</summary>
    public static void Touch(WorldState s, Entity e, int n = 0)
    {
        var was = s.Flags0A5 & 2;                                            // $34F6: once per contact
        s.Flags0A5 |= 6;
        if (was != 0) return;
        var t = T(s, n);
        var item = (sbyte)e.B[0x92];
        if (item < 0) { Special(s, e, n); return; }                          // $A24A -> $A856
        var o = Item(item);
        int count = W(t, o), max = W(t, o + 2), more = e.B[0x93];
        if (count >= max) { Said(s, e, 0xB558, more); return; }              // $A262 bge $A280: "You cannot carry any more X"
        count += more;
        if (count > max) { more -= count - max; count = max; }
        Set(t, o, count);
        e.B[0] = 0;                                                          // $A272 sf (a4): picked up
        Said(s, e, 0xB591, more);                                            // $A274 $A278: "taken some X"
        s.Channels.Play(1, s.Entities);                                      // $A27A $B11A
        Carried(s, n);                                                       // $A2A4 -> $A2A8
        Vehicles.LoadWeapon(s, s.Entities.All[n]);                           // ... then $A37C
    }

    // $A284: d0 = the item's type (+$04); d2 (how many, +$93 less what did not fit) 1 or less takes
    // the next template ($B688: "You do not require another X", "taken a X"), then $C1B2
    static void Said(WorldState s, Entity e, int text, int d2)
    {
        if (d2 <= 1) text = Messages.Next(s, text);                          // $A288 cmp.w #1,d2; bgt
        Messages.Show(s, text, e.W(4));                                      // $A292 bra $C1B2
    }

    // $A856: the record is taken, then a handler by +$93 (table $A872)
    static void Special(WorldState s, Entity e, int n)
    {
        e.B[0] = 0;
        switch (e.B[0x93])
        {
            case 0: GiveItems(s, e, n); break;
            case 1: Clue(s, e, n); break;
            case 2: e.B[0] = 0xFF; s.Talk = true; break;                     // $A8DC: st (a4), $A7 bit 3
            default: throw new NotSupportedException($"$A86A: special touch {e.B[0x93]} has no handler (white screen)");
        }
    }

    // TouchItemStockTaken $A8AC (type $7C): AddStocks $9DE6 of stock column +$34, CarriedThenLoad $A2A4
    // (n = the receiving player's own $A37C), "taken the possesions" $A896
    static void GiveItems(WorldState s, Entity e, int n)
    {
        var t = T(s, n);
        var grave = s.RoomyMemory ? e.UW(0x34) : e.B[0x34];                  // ENHANCEMENT (xf_graves_wrap): with roomy
        if (s.Graves && e.W(4) == 0x7C && grave < s.GraveStore.Count)        // memory the grave's number is the whole
        {                                                                    // fuel word (+$34 high half, +$35 the low half
            var g = s.GraveStore[grave];                                     // roomy carries) - one byte +$34 with it off
            for (var i = 0; i < 30; i++)                                     // ($A856 sf) and the entry is emptied, so
            {                                                                // nothing is given twice. The item-table
                var o = Item(i);                                             // column route below would hand over stock
                Set(t, o, W(t, o) + g.Counts[i]);                            // column N instead (column 0 = a fresh kit)
                g.Counts[i] = 0;
            }
            g.Taken = true;
            Carried(s, n);
            Vehicles.LoadWeapon(s, s.Entities.All[n]);
            Messages.Show(s, 0xA896);                                        // "You have taken the possesions"
            return;
        }
        var col = e.B[0x34] * 2 + 0xE;                                       // $9DEC: d0 * 2 + $E
        for (var i = 0; i < 30; i++)                                         // $9E14: $9E44 set -> added
        {
            var o = Item(i);
            Set(t, o, W(t, o) + W(t, o + col));
            Set(t, o + col, 0);
        }
        Carried(s, n);                                                       // $9E40, again in $A2A4
        Vehicles.LoadWeapon(s, s.Entities.All[n]);                           // $A2A4's pushed $A37C
        Messages.Show(s, 0xA896);
    }

    // TouchItemPutBack $A8C2 (type $89): the record stays, no more touches (ClearStatusBit $2D98: +$06 bit 16);
    // item $16 not carried -> NotGotItem $A212; else LocateMessage $901A of the item's +$1E type
    static void Clue(WorldState s, Entity e, int n)
    {
        e.B[0] = 0xFF;
        e.B[7] &= 0xFE;                                                      // flags long +$06, bit 16
        s.Item240 = 0x16;                                                    // ItemCount $A232: $240 = it
        var t = T(s, n);
        var o = Item(0x16);
        if (W(t, o) < 0) { Messages.Show(s, 0xA206, W(t, o + 2), W(t, o + 0x1A)); return; }
        var type = W(t, o + 0x1E);
        if (LogBook.FindRecord(s, type, 0) is not var (x, z)) return;        // $9020 bmi $9060
        Messages.Show(s, 0x900E, type, x >> 8, z >> 8);
    }

    /// <summary>$A2A8: bit masks of the 30 items: $238 carried (count > 0 or +$19 bit 7), $23C
    /// +$18 not negative. Multiplayer piece 4: the player's own masks ($238/$23C of n).</summary>
    public static void Carried(WorldState s, int n = 0)
    {
        int d1 = 0, d2 = 0;
        var t = T(s, n);
        for (var i = 29; i >= 0; i--)                                        // from the last item down
        {
            var o = Item(i);
            d1 <<= 1; d2 <<= 1;
            if ((sbyte)t[o + 0x18] >= 0) d2 |= 1;
            if (W(t, o) > 0 || (sbyte)t[o + 0x19] < 0) d1 |= 1;
        }
        var me = s.PlayerAt(n);
        me.Items238 = d1; me.Items23C = d2;
    }
}
