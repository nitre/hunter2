namespace Hunter.Rules;

/// <summary>
/// The in-game options screen ($2164, the table at $216C) that an Esc / panel QUIT ends up in when
/// the game is not over and the panel's SAVE was not used: after PlayGame's loop returns, $23DC
/// bclr #5,$A5 (GameOver -> $96EA) and $23E6 bclr #6,$A5 (the save -> $1013E); neither set, $2164
/// jumps to ShowMenuWaitFadeRun, which fades the menu in and waits for a button ($1FA0). The button
/// the player clicks is the handler address of its entry (DrawMenuAndActivate $ACEA / $AF64), and
/// that is all the screen returns: what the address *does* is the shell's business (task 10), so it
/// is handed back as data here, as Games.Debrief hands back a GameEnd.
/// </summary>
public enum AfterQuit
{
    /// <summary>$23D2 ResumeAfterSave: PlayGame again - no ResetGameState, no briefing, so the world,
    /// the clock and the soldier are exactly where the quit left them.</summary>
    Resume,
    /// <summary>$23BE StartAfterLoad: StopMusicAndCheck, PreMissionSoldier (the soldier panel and the
    /// briefing of the same mission), ResetGameState, PlayGame - the mission starts over.</summary>
    Rebrief,
    /// <summary>$1013E DiskMenu: the disk menu's own screen ($100D2: Load Game / Save Game /
    /// Init Disk / End). Load returns to $23BE and Save to $23D2; End goes back into play.</summary>
    DiskOperation,
    /// <summary>$23F6 RetireButton: ConfirmMenu $21F2 first ("Are you sure"); Yes sets the soldier's
    /// status 5 (AWOL, $85E6) and its rts returns to TitleLoop $1F6C, No goes back to $2164.</summary>
    QuitGame,
}

/// <summary>A button of the options screen: the entry's region and text ($2186 onward) and what its
/// handler address does.</summary>
public sealed record OptionsButton(Menus.Button Button, AfterQuit Result);

/// <summary>Quit Game's confirm (ConfirmMenu $21F2, the table at $21FA): "Are you sure", Yes $9064
/// returns 0 and No $9060 returns -1 ($23FA bmi.b: No -> $23E6, the options screen again).</summary>
public sealed record ConfirmScreen(IndexedScreen Screen, Menus.Button Yes, Menus.Button No)
{
    /// <summary>$9064: moveq #0,D0; rts (yes).</summary>
    public const int YesHandler = 0x9064;
    /// <summary>$9060: moveq #-1,D0; rts (no).</summary>
    public const int NoHandler = 0x9060;
}

/// <summary>The in-game options screen as data: the picture it shows (the menu drawn over an empty
/// screen, exactly as the pre-play menus) and its buttons.</summary>
public sealed record OptionsScreen(IndexedScreen Screen, IReadOnlyList<OptionsButton> Buttons)
{
    /// <summary>$216C: the menu table (title "IN GAME OPTIONS", Resume Game, Re-brief, Disk
    /// Operation, Quit Game).</summary>
    public const int Table = 0x216C;
    /// <summary>$21FA: the confirm's table.</summary>
    public const int ConfirmTable = 0x21FA;

    /// <summary>What a button's handler address does, or null for anything else (the title entry's
    /// 0, a stray address).</summary>
    public static AfterQuit? Result(int handler) => handler switch
    {
        0x23D2 => AfterQuit.Resume,
        0x23BE => AfterQuit.Rebrief,
        0x1013E => AfterQuit.DiskOperation,
        0x23F6 => AfterQuit.QuitGame,
        _ => null,
    };
}
