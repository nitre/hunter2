namespace Hunter.Rules;

/// <summary>
/// Soldiers' words and gifts (re/formats/talk.md section 0). SPEAK ($A7 bit 3: the panel's SPEAK or
/// a talking soldier's touch) runs Speak $8F82 at the end of the pass; GIVE opens the item table
/// with the GIVE cells ($A6CE), whose click runs GiveItem $8F46. The talk table $892E holds one
/// record per soldier type $1E-$2F: what it wants, how many, how many it was given (+3, changed by
/// gifts), then the argument list and two templates (still wants / satisfied). Clues name a thing
/// whose place is looked up in the world now (FindRecord $80E0), so they follow it as it moves.
/// </summary>
public static class Talk
{
    public const int Table = 0x892E, TableEnd = 0x8E98;
    const int SpeakerText = 0x8F28, AloneText = 0x8F0F, ThanksText = 0x8F32, RefuseText = 0x8EFA, NowhereText = 0x8FEE;

    /// <summary>Speak ($8F82). Multiplayer: n = the speaking player;
    /// 0 is the original. Piece 4: n's own goodwill table (NewPack's copy) and n's
    /// own item table for the item words.</summary>
    public static void Speak(WorldState s, int n = 0)
    {
        var tt = s.PlayerAt(n).TalkTable;
        s.Channels.Play(2, s.Entities);                                      // $B116
        if (Listener(s, n) is not { } who) return;
        Messages.Show(s, SpeakerText, who.W(4));                             // SpeakerName $8EEE
        var rec = Record(tt, who);
        var done = WantsMore(tt, rec) ? 0 : 1;                               // $8F96: list +4 / +$A, d0 = d7 = 0 / 1
        var list = rec + (done == 0 ? 4 : 0xA);
        // $8FA6-$8FD0: the 8 pushed registers are the argument block (d0 = d7 = done; d1-d6 left
        // as they were: the templates never read past their list); words up to 0: positive = as is,
        // negative = ItemArg + FindRecord -> (thing, x >> 8, z >> 8)
        var args = new int[8];
        args[0] = args[7] = done;
        var k = 0;
        for (var w = W(tt, list); w != 0; w = W(tt, list += 2))
        {
            if (w > 0) { args[k++] = w; continue; }
            var d0 = ItemArg(s, tt, n, w);
            if (LogBook.FindRecord(s, d0, 0) is not var (x, z))
            {
                Messages.Show(s, NowhereText, d0);                           // $8FE4: the block dropped, $8FEE
                return;
            }
            args[k++] = d0; args[k++] = x >> 8; args[k++] = z >> 8;
        }
        // $8FD6-$8FE0: a0 past list B's 0 word, back one (its low byte: an empty string); $B68A
        // skips d7 + 1 strings -> template 1 (still wants) or 2 (satisfied)
        var a = 0xA;
        while (W(tt, rec + a) != 0) a += 2;
        var t = Table + rec + a + 1;
        for (var skip = 0; skip <= args[7]; skip++) t = Messages.Next(s, t);
        Messages.Show(s, t, args);
    }

    /// <summary>GiveItem ($8F46, the GIVE table's cell): wanted and still wanted -> one more given
    /// (+3), "Thank-you for the X", one fewer carried ($A296); else "I do not want your X".
    /// Multiplayer: n = the giving player. Piece 4: the given byte moves in n's own table.</summary>
    public static void Give(WorldState s, int n = 0)
    {
        var tt = s.PlayerAt(n).TalkTable;
        if (Listener(s, n) is not { } who) return;
        Messages.Show(s, SpeakerText, who.W(4));
        var rec = Record(tt, who);
        var wanted = WantsMore(tt, rec) && s.Item240 == W(tt, rec);          // $8F52 bpl, $8F58 cmp.w (a3)
        if (wanted) tt[rec + 3]++;                                           // $8F5C addq.b #1,3(a3)
        Messages.Show(s, wanted ? ThanksText : RefuseText, ItemName(s, n, s.Item240));   // $8F6E: ItemCount($240) +$1A
        if (wanted) Keys.UseItem(s, n);                                      // $8F66 bra $A296
    }

    // FindListener ($8EB2): FindNearestInRange ($340E) from the speaking player p, kind 3 (types
    // from the word pair at $81BA + 6), range $3E8 by max(|dx|, |dz|); a later record at an equal
    // distance wins (bgt). None, or type >= $30: "You are the only person present" and the caller
    // stops. ENHANCEMENT: p is the speaking player's record (0 today).
    static Entity? Listener(WorldState s, int n)
    {
        var main = s.Data.Main;
        int lo = main[0x81BA + 6 - 0x800] << 8 | main[0x81BA + 7 - 0x800];
        int hi = main[0x81BA + 8 - 0x800] << 8 | main[0x81BA + 9 - 0x800];
        var p = s.Entities.All[n];
        Entity? best = null;
        var range = 0x3E8;
        foreach (var e in s.Entities.All)
        {
            if (e == p || !e.Alive) continue;
            var t = e.W(4);
            if (t < lo || t >= hi) continue;
            var d = Math.Max(WorldState.AbsDist(s, e.UW(0x4A), p.UW(0x4A)), WorldState.AbsDist(s, e.UW(0x4E), p.UW(0x4E)));   // $3454 cmp.w, signed; Maths32: Dist's true difference
            if (d > range) continue;
            range = d; best = e;
        }
        if (best == null || best.W(4) >= 0x30)
        {
            Messages.Show(s, AloneText);
            return null;
        }
        return best;
    }

    // $3444/$3450 bpl / neg.w: a word |d|; $8000 stays $8000 (-32768), so max() takes the other axis

    // TalkRecord ($8E98): past (type - $1E) + 1 $FEFE words from the table's start; an offset into TalkTable
    static int Record(byte[] tt, Entity who)
    {
        var o = 0;
        for (var n = who.W(4) - 0x1E; n >= 0; n--)
        {
            while (W(tt, o) != unchecked((short)0xFEFE)) o += 2;
            o += 2;
        }
        return o;
    }

    // WantsMore ($8EDA): +2 (how many wanted) non-zero and +2 - +3 > 0 (signed bytes)
    static bool WantsMore(byte[] tt, int rec)
    {
        var want = tt[rec + 2];
        return want != 0 && (sbyte)(want - tt[rec + 3]) > 0;
    }

    // ItemArg ($8FF6): low byte; bits 14-8 set -> an item: ItemCount ($A232, also $240 = it) +$1A
    // of the speaking player's own table (piece 4)
    static int ItemArg(WorldState s, byte[] tt, int n, int w)
    {
        if ((w & 0x7F00) == 0) return w & 0xFF;
        s.Item240 = (short)(w & 0xFF);
        return ItemName(s, n, w & 0xFF);
    }

    // ItemEntry ($A344, $9E46 + 32 * item) +$1A: the item's name / type word (piece 4: the
    // speaking player's own pack)
    static int ItemName(WorldState s, int n, int item)
    {
        var t = s.PlayerAt(n).Items;
        var o = 0x28 + item * 32 + 0x1A;
        return (short)(t[o] << 8 | t[o + 1]);
    }

    static short W(byte[] tt, int o) => (short)(tt[o] << 8 | tt[o + 1]);
}
