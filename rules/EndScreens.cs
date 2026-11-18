namespace Hunter.Rules;

/// <summary>
/// The screen an ended game shows (DebriefScreen $96BC, then the messages and EndScreen $97E2's
/// soldier panel; re/formats/game.md 7): screen mode 0 cleared, the HEADQUARTERS banner (frame
/// $1A at 0, 0), the text window of mode 0 two lines down, the messages one under the other and
/// the soldier panel at row $88. The picture the original waits on is Screen.
/// </summary>
public sealed class EndScreen
{
    public IndexedScreen Screen { get; } = new();
    readonly Text _t;
    readonly Ui _ui;
    readonly byte[] _main;

    public EndScreen(Ui ui, byte[] main)
    {
        _ui = ui;
        _main = main;
        _t = new Text(Screen, ui, main);
        _t.Window(0, 0, 40, 20, 10, 0);                  // $610C SetScreenMode(0): $608A
        Screen.Clear();                                  // $96C2 ClearScreen ($2016 loads the menu picture only once: $9B bit 1)
        _t.Line += 2;                                    // $96CC addq.w #2,$136
        Ui.Blit(Screen, ui.Frames[0x1A], 0, 0);          // $96D8 BlitFrame(0, 0, $1A, mode 1)
        _t.Typewriter = true;                            // $96DC bset #3,$97 (a frame a letter: timing only)
    }

    /// <summary>ShowMessage ($C1B2) at the cursor.</summary>
    public void Message(int template, params int[] args) => _t.Printf(template, args);

    /// <summary>$C1B2 leaves d0 as the printer's last drawn character did ($C2B8): what the message
    /// just shown left in d0. The reward message ($980C) leaves 0 (measured d0 = 0 at $98E2 in all
    /// three probe runs: C:\LAB\deepy\jobs\ends_open\findings\probe_d0.py; see Text.LeftInD0).</summary>
    public int D0 => _t.LeftInD0;

    /// <summary>The frame AwardMedal ($98E2) and Promote ($991E) blit at x $90 on the line the cursor is
    /// on ($9908: line * the window's line height $124), then the cursor goes down one.</summary>
    public void Badge(int frame)
    {
        Ui.Blit(Screen, _ui.Frames[frame], 0x90, _t.Line * 10);
        _t.Line++;
    }

    /// <summary>EndScreen ($97E2) after its message: the typing bit off, SoldierPanel($88).</summary>
    public void Panel(Soldier sol)
    {
        _t.Typewriter = false;
        Pregame.SoldierPanel(Screen, _ui, _main, sol, Pregame.RecordName(sol), top: 0x88);
    }

    /// <summary>The picture a won game ends on (LoadScreenPicture $9760: file 10, epic). The picture loader puts
    /// its CMAP into bank 4 ($1128A -> $6418) and $9750 fades it in through bank 4: its own colours.</summary>
    public static IndexedScreen Epic(GameData d)
    {
        var pic = d.Picture("epic");
        var s = new IndexedScreen { Bank = pic.Palette };
        pic.Pixels.CopyTo(s.Pixels, 0);
        return s;
    }
}
