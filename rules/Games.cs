namespace Hunter.Rules;

/// <summary>Where the original goes after an ended game's screens.</summary>
public enum AfterGame
{
    /// <summary>$23CA PreMissionSoldier: the soldier panel and the briefing of the next mission (or the
    /// same one again), or the soldier's own screen when the record is no longer usable.</summary>
    Briefing,
    /// <summary>$2404: the title picture, then the menus (a mission failed on time).</summary>
    Title,
    /// <summary>$975C: the picture a won game ends on was shown; back to the menus.</summary>
    Menus,
    /// <summary>The demo ended ($23D6 bmi -> RunDemo $22FE): the soldier record restored, the title
    /// picture ($2308 TitlePicture), then $1F2E: 50 blanks, the title music, the credits, the menus.</summary>
    DemoOver,
}

/// <summary>An ended game: the pictures the original waits on (a click each), where it goes next,
/// and the song each picture plays (EndScreen $97E2's d7 -> PlayMusic $AFBE at $97E8; -1 = the
/// song keeps playing, the epic picture $9748 plays none of its own).</summary>
public sealed record GameEnd(AfterGame After, IReadOnlyList<IndexedScreen> Screens, IReadOnlyList<int> Songs);

/// <summary>
/// The three games' ends (re/formats/game.md 7). A game ends one way only: GameOver ($3514) - the
/// end timer after the last death ($18C, BlankTimers $6CB8) or a building's door ($4668) - and
/// the loop's return ($24D6 GameToSoldier, $23DC -> $96EA). What the soldier did is judged there,
/// not while playing: the objectives are checked against the kill counts ($9288) and the items
/// carried only when the game is over (MissionCheck $9978).
/// </summary>
public static class Games
{
    /// <summary>GameOver ($3514): $A5 bit 5, then QuitKey's bit 7: MainLoop leaves at $2586.</summary>
    public static void GameOver(WorldState s) => s.Flags0A5 |= 0xA0;

    // NOT THE GAME - copy protection, left out (owner 2026-10-17). StopMusicAndCheck ($B00A) stops
    // the music (here the shell's Audio.Music(-1)), but its tail ($B028-$B032) is an anti-piracy
    // check: the word at $7E92E against $68, and $23C6 adds 2 to the disk wiper's counter $161A
    // when it fails (docs/cut-content.md "the disk wiper").
    // public static int StopMusicAndCheck(bool wasPlaying) => wasPlaying ? 2 : -1;

    /// <summary>$23BE StartAfterLoad, the options screen's Re-brief (and the address the disk
    /// menu's Load returns to): StopMusicAndCheck (its music stop is the shell's; its copy-protection
    /// check is left out, see above); PreMissionSoldier $86F2 - the soldier panel and the briefing
    /// of the same mission, the shell's screens (GameView, bank 6); then ResetGameState. What
    /// follows - $23D2 ResumeAfterSave = PlayGame $24B4 - is Play.Rebrief's tail, shared with Resume.</summary>
    public static void StartAfterLoad(WorldState s, bool musicWasPlaying)
    {
        // s.MusicClock161A += StopMusicAndCheck(musicWasPlaying);           // $23C4 beq, $23C6 - copy protection, not the game
        ResetGameState(s);                                                   // $23D0 bsr $2414
    }

    /// <summary>ResetGameState ($2414), everything a Re-brief clears before the mission runs
    /// again. The demo's three variables too - the demo runs it as well: $2414's only caller is
    /// $23D0, on the demo's way through GameSequence, so the $2426 keep applies. $1A4 G.BlankCount
    /// cleared ($2414), the demo's pace $D2 back to $F9C = 3996 bytes from the pace table's end
    /// ($2418; WaitSwapAndDemoPace $620E counts it down 2 a pass and quits to the menus at 0), and
    /// $CE = the pointer at $832A ($241E), the demo input script's 1000-byte buffer ($832E = $3E8),
    /// whose first word (A0) is cleared unless MenuFlags $9B bit 7 ($2426: already the demo's) -
    /// ReplayDemoInput $C826 walks it. The pointer itself is runtime data (SetBufferPointers $83F2
    /// fills the table at $8312 from $1135C when the world loads); the jmp at $245E goes to the
    /// mission overlay those buffers hold, not to anything in the program image.</summary>
    public static void ResetGameState(WorldState s)
    {
        Demo.Reset(s);                                                       // $2414-$242C: the demo's three, and the recording's first word
        s.Weapon = 0; s.WeaponBefore = 0;                                    // $242E clr.l $200 G.WeaponSel
        s.Distance = 0x1000;                                                 // $243E $1526 G.CamDistance
        s.Base = 0x1000;                                                     // $2442 $1522 G.CamBase
        s.Target = 0x1000;                                                   // $2446 $152A G.CamTarget
        s.Step = 0x20;                                                       // $244A $152E: the camera's step per VBL
        s.Zoom = 0;                                                          // $2452 $151E G.Zoom
        s.Timer222 = 0;                                                      // $2456 $222 G.TimerBits
        // $10 G.ViewCentreX = $A0 ($2432): the view is fixed at column 160.
    }

    /// <summary>Quit Game's Yes ($9064 returns 0, so $23FA's bmi is not taken): moveq #5,d0 ->
    /// SetSoldierStatus $8642, whose only act is the soldier's status byte $85E6 = 5 (AWOL); its
    /// rts returns to TitleLoop $1F6C. Nothing else runs: $9064 is moveq 0,d0/rts and $8642 is
    /// move.b d0,$85E6/rts (the confirm's No, $9060, returns -1 and $23FA bmi goes back to the
    /// options screen).</summary>
    public static void Retire(WorldState s) => s.Soldier.R[4] = 5;

    static byte M(byte[] main, int addr) => main[addr - 0x800];

    /// <summary>GameToSoldier ($87E4, after the loop at $24D6): $216, score and energy into the soldier
    /// record, the player's record (StoreEntityRecord $82D0) at +$20 (the slot is freed), the item counts
    /// back to the stock column (CountsToItemColumn $9DF6 = MoveItemColumn: word +$E := word +0, +0 := 0
    /// for each of the 30 items; $9E44 is clear in play) and CarriedMasks.</summary>
    public static void GameToSoldier(WorldState s) => SaveGame.GameToSoldier(s);

    /// <summary>Does the clock say the mission's time is up (MissionDeadline $999A)? The record's +4 is the
    /// last day + 1 and +5 the hour on it, against $1B5 and $1B3 (the day and hour words' low bytes).</summary>
    internal static bool TimeUp(WorldState s, int rec)
    {
        var main = s.Data.Main;
        var d = (sbyte)(M(main, rec + 4) - 1 - (s.Day & 0xFF));
        if (d < 0) return true;                                              // $99A4 bmi
        if (d > 0) return false;                                             // $99A6 bgt
        return (sbyte)(M(main, rec + 5) - (s.Hour & 0xFF)) <= 0;             // $99B0 ble
    }

    // SplitObjective ($9A08): a word = (flag << 15) | (count << 8) | type
    static (int Count, int Type) Split(int w) => (w >> 8 & 0x7F, w & 0xFF);

    /// <summary>The objectives still open (MissionCheck's two checks). A word with bit 15 set is an item to
    /// collect: its count against the stock column's word +$E of the type's item block ($99B4: TypeGroup
    /// $99D4 = byte +$2E of the type record $D45E + type * $34, * 32; signed word compare). A positive word is
    /// something to destroy: its count against the ends counted for the type ($9288, $99EA: a byte compare).</summary>
    public static bool Unmet(WorldState s, int rec)
    {
        var main = s.Data.Main;
        for (var k = 0; k < 4; k++)
        {
            var w = (short)(M(main, rec + 8 + k * 2) << 8 | M(main, rec + 9 + k * 2));
            if (w < 0)
            {
                var (count, type) = Split(w);
                var block = M(main, 0xD45E + type * 0x34 + 0x2E) << 5;
                var have = (short)(s.Items[0x28 + block + 0xE] << 8 | s.Items[0x28 + block + 0xF]);
                if (count > have) return true;                                // $99C6 sub.w, bgt: count > have, signed words
            }
            else if (w > 0)
            {
                var (count, type) = Split(w);
                // $99FA sub.b, bgt: open while count > the kills read as a signed byte (bgt weighs the
                // overflow, so it is that compare, not the sign of the wrapped difference) - 128 to 255 ends
                // read negative and the objective is open again, and the byte itself wraps at 256.
                // ENHANCEMENT (Play.RoomyMemory): on, the plain count (WorldState.KillsWide) compares instead.
                if (s.RoomyMemory ? count - s.KillsWide[type] > 0 : count > (sbyte)s.Kills[type]) return true;
            }
        }
        return false;
    }

    /// <summary>The end of a game, as GameSequence ($96EA) judges it: draws the screens, changes the soldier
    /// record and the mission ($20A) exactly as the original does, and says where it goes next. The game's state
    /// (WorldState.Soldier, .Mission) is the one the next game starts from.</summary>
    public static GameEnd Debrief(WorldState s, Ui ui)
    {
        var main = s.Data.Main;                                              // $96EA bsr DebriefScreen: no GameToSoldier here - EndPlay ran it ($24D6); a second run zeroed the stock column
        var sol = s.Soldier;
        var r = sol.R;
        var scr = new EndScreen(ui, main);                                   // DebriefScreen $96BC
        var shown = new List<IndexedScreen>();
        var songs = new List<int>();
        GameEnd End(AfterGame after, int song, int template, params int[] args)
        {
            scr.Message(template, args);                                     // EndScreen $97E2: PlayMusic(d7) $97E8, ShowMessage
            scr.Panel(sol);                                                  // SoldierPanel($88), WaitClick
            shown.Add(scr.Screen);
            songs.Add(song);
            return new GameEnd(after, shown, songs);
        }
        if ((s.Status216 & 0x20) != 0)                                       // $96F0: left a guarded building as a traitor
        {
            s.MissionFlags = 0;                                              // $A6: nothing ran to set it
            r[0] |= 0x21; r[4] = 2;                                          // $97B2: +0 bits 5 and 0, status 2
            return End(AfterGame.Briefing, 2, 0x9633);                       // $97C4: moveq #2,d7
        }
        if ((s.Status216 & 0x80) != 0)                                       // $96FA: died
        {
            s.MissionFlags = 0;                                              // $A6: nothing ran to set it
            r[0] |= 0x80; r[4] = 4;                                          // $97CC
            return End(AfterGame.Briefing, 2, 0x9662);                       // $97DA: moveq #2,d7
        }
        var rec = 0x9072 + s.Mission;                                        // MissionRecord $9068
        var timeUp = TimeUp(s, rec);
        var unmet = Unmet(s, rec);
        // MissionCheck $9978 (the debrief's first act, $9704) leaves $A6 bit 6 set while an objective
        // is still open and bit 7 past the deadline - and past the deadline bit 6 too: its bset #7
        // ($998A) runs on into ObjectiveOpenSetFlag's bset #6 ($9990), objectives met or not. $9708
        // then clears bit 7 before branching on it, so the recorded byte is bit 6 alone.
        s.MissionFlags = (byte)(unmet || timeUp ? 0x40 : 0);
        if (timeUp) { r[4] = 6; return End(AfterGame.Title, 2, 0x95DD); }    // $977C: moveq #2,d7 -> $2404
        if (unmet)                                                           // $9710 btst #6,$A6
        {
            if (Dismissed(sol, scr, shown, songs) is { } dismissed) return dismissed;
            return End(AfterGame.Briefing, 0, 0x9612);                       // $9720: moveq #0,d7
        }
        // success ($98A6): the reward is added to the credit and said
        var reward = M(main, rec + 6) << 8 | M(main, rec + 7);
        sol.SetLong(0x0C, sol.Long(0x0C) + reward);
        scr.Message(0x980C, reward);
        var d0 = scr.D0;                                                     // $C1B2 restores d0: what the reward message left
        if (Dismissed(sol, scr, shown, songs) is { } d2) return d2;
        Medal(s, rec, scr, d0);
        Promote(s, scr);
        // NextMission ($915A): the next record when it is for the same game
        var next = s.Mission + 0x10;
        if ((M(main, 0x9072 + next) & 0xF) == (int)s.Mode)
        {
            s.Mission = next;
            var briefed = End(AfterGame.Briefing, 1, 0x9888, M(main, 0x9072 + next) >> 4 & 0xF);  // $98D8: moveq #1,d7
            s.Log.NoteObjectives(s, 0x9072 + next);                          // $98DE bra $23AA -> NoteObjectives $D0CA
            s.Flags0A4 = 0;                                                  // $23B0 bra $23BA ClearPlayerFlags: sf $A4 (alarm, look) before $23BE
            return briefed;
        }
        r[4] = 0;                                                            // $972A: the game is won
        r[2] |= (byte)(1 << (int)s.Mode);                                    // $973A bset d0,(2,A4)
        var end = End(AfterGame.Menus, 1, 0x968C, (int)s.Mode);              // $9742: moveq #1,d7
        shown.Add(EndScreen.Epic(s.Data));                                   // $9748 LoadScreenPicture(10), WaitClick
        songs.Add(-1);                                                       // no PlayMusic of its own
        return end;
    }

    // DismissedCheck ($9784): a credit below -200 ends the soldier's career
    static GameEnd? Dismissed(Soldier sol, EndScreen scr, List<IndexedScreen> shown, List<int> songs)
    {
        if (sol.Long(0x0C) >= -0xC8) return null;
        sol.R[0] |= 0x41;                                                    // $9792, $9796
        sol.SetWord(8, 6);                                                   // $979A
        sol.R[4] = 3;
        scr.Message(0x95A0);
        scr.Panel(sol);
        shown.Add(scr.Screen);
        songs.Add(2);                                                        // $97AA: moveq #2,d7
        return new GameEnd(AfterGame.Briefing, shown, songs);
    }

    // AwardMedal ($98E2): the mission's medal (+1; 0 = none) as the grade in +$A, its bit in +$B, the
    // message and the medal's frame (10 + grade - 1) on the next line. $98E6 reads the grade with
    // move.b, so d0 keeps the upper bits the reward message ($980C) left there; the medal message's
    // argument ($C4: the word's last digit) is therefore that word's last digit, not the grade's.
    static void Medal(WorldState s, int rec, EndScreen scr, int d0)
    {
        var grade = M(s.Data.Main, rec + 1);
        if (grade == 0) return;                                              // $98EA beq
        var r = s.Soldier.R;
        r[0xA] = (byte)(grade - 1);
        r[0xB] |= (byte)(1 << (grade - 1 & 7));                              // $98F4 bset d0,(+$B): the bit number mod 8
        scr.Message(0x985A, d0 & ~0xFF | grade);                             // $98FA: d0 = (reward's upper bits) | grade
        scr.Badge(10 + r[0xA]);
    }

    // Promote ($991E): the credit (less 1) against the ranks' thresholds ($995C: 0, 5000, 15000, 30000,
    // 50000, 80000, 500000), as many ranks up as it reaches; frame 3 + rank
    static void Promote(WorldState s, EndScreen scr)
    {
        var sol = s.Soldier;
        var main = s.Data.Main;
        long T(int i) => (uint)(M(main, 0x995C + i * 4) << 24 | M(main, 0x995D + i * 4) << 16 | M(main, 0x995E + i * 4) << 8 | M(main, 0x995F + i * 4));
        var credit = sol.Long(0x0C) - 1;                                     // $9922
        var n = (short)sol.Word(8) + 1;                                      // $9924 title + 1
        if (credit < (int)T(n)) return;                                      // $9930 blt (signed long compare)
        while (credit >= (int)T(n + 1)) n++;                                 // $9936-$9940
        sol.SetWord(8, n);                                                   // $9948
        scr.Message(0x9878, n);
        scr.Badge(3 + n);                                                    // $9954
    }
}
