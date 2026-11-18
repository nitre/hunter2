namespace Hunter.Rules;

/// <summary>The attract demo's recording (file 11, re/formats/jfile.md), as far as this piece of
/// the port goes: the loaded bytes' layout, the pace word each pass waits for, and the input
/// records' decode. The flow itself - RunDemo $22D6, the per-blank replay applying the values,
/// the quit tests - is the pieces that follow.</summary>
public static class Demo
{
    /// <summary>The records' start in DemoBuffer (the buffer the pointer at $832A holds, which
    /// $CE points into): (count, value) pairs, the count spent one per counted blank.</summary>
    public const int Records = 0, RecordsBytes = 0x3E8;
    /// <summary>The pace table in DemoBuffer (the pointer at $8332, $3E8 bytes in): 2000
    /// big-endian words read BACKWARDS - the cursor starts at the last word ($F9C = 3996 bytes
    /// in) and walks down 2 a pass.</summary>
    public const int Pace = 0x3E8, PaceBytes = 4000;

    /// <summary>RunDemo $22D6's part of the game state: file 11 into the buffer ($22D8), the demo
    /// flag on ($22E4), the joystick and buttons cleared ($22EA-$22EE: a new Play starts them at 0),
    /// then ResetGameState's demo part ($23D0 -> $2414) on the way into play. The default soldier
    /// ($22F6 CopyDefaultSoldier $866C: the 32 bytes at $8612 equal main.bin's own $85E2) named
    /// "Trigger Happy" ($8738) is StartState.DefaultSoldier.</summary>
    public static void Start(WorldState s)
    {
        s.DemoBuffer = s.Data.DemoFile();
        s.DemoMode = true;
        Reset(s);
    }

    /// <summary>ResetGameState's demo part ($2414-$242C).</summary>
    public static void Reset(WorldState s)
    {
        s.BlankCount = 0;                                                    // $2414 clr.w $1A4 G.BlankCount
        s.DemoPaceCursor = 0xF9C;                                            // $2418: the pace table is read backwards from its last word
        s.DemoInputPtr = Records;                                            // $241E-$2422: $CE = *($832A), the record start
        if (!s.DemoMode) ClearFirstRecord(s.DemoBuffer);                     // $2426 tst.b $9B, $242C clr.w (A0): not the demo's own recording
    }

    /// <summary>CapBlankAtDemoPace $C6E6-$C6FE, on a blank in play: the blank counts ($1A4 + 1) - in
    /// the demo only while that stays at or under the pace word ($C6FA cmp.w, bgt: signed); a blank
    /// over it runs no game work at all, only the fade and the press ($C71A).</summary>
    public static bool CountBlank(WorldState s)
    {
        var n = (short)(s.BlankCount + 1);
        if (s.DemoMode && !s.DemoUnpaced && n > (short)PaceWord(s.DemoBuffer, s.DemoPaceCursor)) return false;
        s.BlankCount = n;                                                    // $C6FE
        return true;
    }

    /// <summary>WaitSwapAndDemoPace $6218-$6224: in the demo a pass waits until the counted blanks
    /// reach the pace word. The original copies the pass's blanks into its time step ($623A) before
    /// this wait, so a blank counted during it belongs to the next pass; the port runs one blank per
    /// pass and holds the pass instead (Play.Waiting), so each pass's time step is the pace table's
    /// step - the same blanks in all, the same inputs at each counted blank (frame-exact pass
    /// distribution is not a goal).</summary>
    /// ENHANCEMENT: past the table under DemoFollow (WorldState.DemoUnpaced) a pass waits for nothing.
    public static bool PaceHeld(WorldState s) =>
        s.DemoMode && !s.DemoUnpaced && (short)PaceWord(s.DemoBuffer, s.DemoPaceCursor) > s.BlankCount;

    /// <summary>$6226-$622C, after the wait: the cursor 2 bytes on (back) to the next word; past the
    /// table's start the demo quits (QuitKey $351C).</summary>
    /// quitAtEnd false: ENHANCEMENT (DemoFollow) - the follower ends the demo; past the table no pace at all (DemoUnpaced: the
    /// cursor left at -2 read the recording's bytes before the table as a cap - no blank counted, the follower never ran
    /// again, the picture frozen).
    public static void StepPace(WorldState s, bool quitAtEnd = true)
    {
        if (s.DemoUnpaced) return;
        s.DemoPaceCursor -= 2;
        if (s.DemoPaceCursor >= 0) return;
        if (quitAtEnd) Keys.QuitKey(s); else s.DemoUnpaced = true;
    }

    /// <summary>ReplayDemoInput $C826-$C85A on a counted blank ($C706, before BlankTimers $C710): the
    /// record at $CE; a key (bit 7) goes through KeyDown $B214 and the NEXT record applies in the
    /// same blank ($C84A bra $C826); a joystick record sets $D6 and $D4 = its bit 4 (fire) and ends
    /// the blank's replay. The delay loops after each ($C844 $D7, $C85A $7B: CPU time) hold no state.
    /// The key's d7 is the interrupted code's (FireShot reads it for the flare and the drone): 0, as
    /// for every key the port hands in between passes.</summary>
    /// keys: ENHANCEMENT (DemoFollow's track) - told each key as it is applied (null: none kept).
    public static void Replay(Play play, ref byte d6, ref byte d4, List<byte>? keys = null)
    {
        var s = play.State;
        while (true)
        {
            (s.DemoInputPtr, var value) = NextRecord(s.DemoBuffer, s.DemoInputPtr);
            if ((value & 0x80) == 0)
            {
                d6 = value;                                                  // $C84C
                d4 = (byte)(value >> 4 & 1);                                 // $C850-$C856
                return;
            }
            keys?.Add((byte)(value & 0x7F));
            Keys.Down(play, (byte)(value & 0x7F), false, false, 0);          // $C83C-$C840
        }
    }

    /// <summary>ResetGameState $242C (clr.w A0, only when the demo is off): the recording's first
    /// word is emptied, so a stray replay decodes an already-spent record and moves on.</summary>
    public static void ClearFirstRecord(byte[] demoFile)
    {
        demoFile[Records] = 0;
        demoFile[Records + 1] = 0;
    }

    /// <summary>ReadDemoPaceFrames $620E-$6224: the pace word at the cursor - the BlankCount the
    /// next pass waits for ($6220) and the cap the blank counts up to ($C6E6-$C6FC). The cursor
    /// ($D2) is the byte offset from the table's end, so $F9C is the last word of the 4000.</summary>
    /// Past the table's start (the cursor still stepping while a quit inside a building does nothing,
    /// $355A) the original reads whatever lies before it; the port reads the buffer while it lasts, then
    /// $7FFF (no cap, no wait) - never reached by the recording, which quits at 16054.
    public static int PaceWord(byte[] demoFile, int cursor) =>
        Pace + cursor < 0 ? 0x7FFF : demoFile[Pace + cursor] << 8 | demoFile[Pace + cursor + 1];

    /// <summary>ReplayDemoInput $C826, the decode only (applying it - Joystick $D6, Buttons $D4,
    /// KeyDown $B214 - is the replay loop's piece): the record at the pointer is (count, value).
    /// The count is spent in place ($C82E subq.b #1,(A0)) and the pointer moves 2 on only when it
    /// reaches 0 ($C832-$C834), so a count of n holds the value for n counted blanks. Value bit 7
    /// set is a key down ($C83A, and #$7F -> KeyDown $B214) and the next record applies in the
    /// SAME blank ($C84A loops back); otherwise the value is the joystick ($C84C) and its bit 4
    /// the fire button ($C84C-$C856).</summary>
    public static (int Ptr, byte Value) NextRecord(byte[] demoFile, int ptr)
    {
        var value = demoFile[ptr + 1];                                       // $C82A move.b 1(A0),D0
        if (--demoFile[ptr] == 0) ptr += 2;                                  // $C82E-$C834
        return (ptr, value);
    }
}
