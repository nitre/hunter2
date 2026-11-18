using Godot;

namespace Hunter;

/// <summary>The pad's quick pick for the keyboard-only items (clock, radar, log book, map marks, flare, drone).
/// Hold the left stick click (Tab on the keyboard): a strip of six boxes shows along the bottom of the view.
/// Push the stick (or d-pad) left or right to step along it and let go to use the one lit, which sends the
/// same raw key the keyboard does. Letting go before choosing does nothing. A child of the game picture, so it
/// draws in game pixels and is not in the 320 x 200 captures.</summary>
public partial class QuickPick : Node2D
{
    static readonly (string Label, byte Raw)[] Items =
    [
        ("CLOCK", 0x33), ("RADAR", 0x13), ("LOG", 0x28), ("MAP", 0x37), ("FLARE", 0x23), ("DRONE", 0x20),
    ];

    const int BoxW = 44, BoxH = 14, Gap = 4, Top = 106;   // the view ends at row 139, the panel starts there
    const float Dead = 0.5f;

    int chosen = -1;
    bool pushed;
    readonly Font font = ThemeDB.FallbackFont;

    public bool Open => Visible;

    /// <summary>The headless script's way of stepping to an item.</summary>
    public void Pick(int i) { chosen = i; QueueRedraw(); }

    public QuickPick()
    {
        Visible = false;
        ZIndex = 20;
    }

    public new void Show() { chosen = -1; pushed = false; Visible = true; QueueRedraw(); }

    /// <summary>Close it; the raw key of the lit item, or null.</summary>
    public new byte? Hide()
    {
        Visible = false;
        return chosen >= 0 ? Items[chosen].Raw : null;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var x = Input.GetActionRawStrength("ui_right") - Input.GetActionRawStrength("ui_left");
        if (Input.GetConnectedJoypads().Count > 0) x += Input.GetJoyAxis(0, JoyAxis.LeftX);
        var step = x > Dead ? 1 : x < -Dead ? -1 : 0;
        if (step != 0 && !pushed)                        // one step per push, not per frame
        {
            chosen = chosen < 0 ? (step > 0 ? 0 : Items.Length - 1) : Mathf.Clamp(chosen + step, 0, Items.Length - 1);
            QueueRedraw();
        }
        pushed = step != 0;
    }

    public override void _Draw()
    {
        var line = new Color(0.6f, 0.75f, 1f, 0.9f);
        var gold = new Color(1f, 0.9f, 0.3f);
        var left = (320 - (Items.Length * BoxW + (Items.Length - 1) * Gap)) / 2f;
        var hub = new Vector2(160, 139);                 // the bottom edge of the view, clear of the soldier; only the lit item has a line to it
        if (chosen >= 0)                                 // a faint guide, so the box outline is what reads as the selection
            DrawLine(hub, new Vector2(left + chosen * (BoxW + Gap) + BoxW / 2f, Top + BoxH), new Color(gold, 0.3f), 1);
        for (var i = 0; i < Items.Length; i++)
        {
            var box = new Rect2(left + i * (BoxW + Gap), Top, BoxW, BoxH);
            var hot = i == chosen;
            DrawRect(box, hot ? new Color(0.35f, 0.3f, 0.05f, 0.95f) : new Color(0.05f, 0.1f, 0.25f, 0.9f));
            DrawRect(box, hot ? gold : line, false, hot ? 2 : 1);
            DrawString(font, box.Position + new Vector2(0, 10.5f), Items[i].Label, HorizontalAlignment.Center, BoxW, 8,
                hot ? new Color(1f, 1f, 0.7f) : Colors.White);
        }
    }
}
