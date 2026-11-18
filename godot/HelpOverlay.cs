using System.Collections.Generic;
using Godot;

namespace Hunter;

/// <summary>The F1 key list, drawn over the window at its own resolution - a CanvasLayer, so it
/// never lands in the 320 x 200 game picture: the picture and every capture of it are exactly as
/// without F1, and the game runs on underneath. Every play key comes from PlayInput's table (with
/// its group); the joystick, gamepad and zoom keys are added here - those are key state the
/// original reads outside the keyboard interrupt ($3578, port 2), so they are not in that table.
/// Old-style look: a square box with a 1 px border, a dark, slightly see-through background, no
/// rounded corners, centred and never wider than the game picture. A system monospace font
/// (Consolas / Lucida Console / Courier New) at about 1/32 of the window height (min 14 px), half
/// a line between lines; two aligned columns, the key bright, what it does in a lighter grey,
/// grouped Move / Vehicles / Weapons / Items / Screens, each group opened by a heading in upper
/// case and a soft blue, with half a line of space above it. The box's width is the measured width
/// of the widest row plus padding; if that is wider than the game picture the font shrinks until it
/// fits (floor 14 px), so no row ever crosses the border.</summary>
public partial class HelpOverlay : CanvasLayer
{
    public HelpOverlay()
    {
        var list = new HelpList();
        list.SetAnchorsPreset(Control.LayoutPreset.FullRect);           // the whole window
        AddChild(list);
    }
}

/// <summary>The list itself: a Control over the whole window, so _Draw has the window's own
/// resolution (a CanvasLayer alone cannot draw).</summary>
public partial class HelpList : Control
{
    /// <summary>The list's sections, in this order; keys are grouped in PlayInput's table.</summary>
    static readonly string[] Groups = ["Move", "Vehicles", "Weapons", "Items", "Screens", "Window"];

    /// <summary>The keys that are not remappable (the shell's own), with their section.</summary>
    static readonly (string Text, string Group)[] Fixed =
    [
        ("F2 = change the keys and gamepad buttons", "Window"),
        ("F3 = the manual", "Window"),
        ("F11 = fullscreen on / off", "Window"),
        ("F10 = original TV look / square pixels", "Window"),
        ("F9 = pause when the window loses focus, on / off", "Window"),
        ("F6 = Amiga 500 sound filter on / off", "Window"),
        ("F8 = text size: normal / large / extra large", "Window"),
        ("F7 = brighter (Shift+F7 darker)", "Window"),
        ("Page Up / Page Down = scroll the messages back (message_log setting)", "Window"),
    ];

    const string Title = "KEYS IN PLAY  (F1 hides this list)";
    const int MinFont = 14;

    readonly Font font = new SystemFont
    {
        FontNames = ["Consolas", "Lucida Console", "Courier New"],
    };

    public override void _Ready() => GetWindow().SizeChanged += QueueRedraw;

    public override void _Draw()
    {
        var win = GetViewport().GetVisibleRect().Size;
        const float pad = 8, colGap = 2;

        // the rows: the title, then each group header and its keys left and what they do right
        var rows = new List<(string Key, string Help, bool Header)> { ("", Title, true) };
        foreach (var group in Groups)
        {
            rows.Add((group, "", true));
            foreach (var (name, help, g) in Bindings.HelpRows())
                if (g == group) rows.Add((name, help, false));
            foreach (var (text, g) in Fixed)
                if (g == group)
                {
                    var eq = text.IndexOf(" = ");
                    rows.Add((text[..eq], text[(eq + 3)..], false));
                }
        }

        // a little space (half a line) above each group heading; the title sits at the very top
        var headings = 0;
        foreach (var (key, _, header) in rows)
            if (header && key.Length > 0) headings++;

        // about 1/32 of the window height (min 14 px), half a line between lines plus half a line
        // above each heading; the text-size setting (F8, normal / large / extra large) multiplies
        // that base, then the shrink loop below keeps the box inside the window and the picture
        var size = (int)System.Math.Round(System.Math.Min(System.Math.Max(MinFont, (int)win.Y / 32),
            System.Math.Max(MinFont,
                (int)((win.Y - pad * 2) / (1.5f * rows.Count + 0.75f * headings)))) * Settings.TextScale);
        var line = size + size / 2;                                     // half a line between lines

        // the box's width comes from the MEASURED width of the widest drawn row (GetStringSize at
        // the chosen size - a character count lies, the system monospace face is not exactly
        // uniform) plus padding; if that is wider than the game picture, or the box taller than
        // the window, shrink the font one size at a time until it fits (the floor stays 14 px) -
        // the text never crosses the border
        var pic = Display.I.Picture;
        var charW = 0f;
        float keyW = 0, helpW = 0, boxW = 0;
        while (true)
        {
            line = size + size / 2;
            charW = font.GetStringSize("M", HorizontalAlignment.Left, -1, size).X;
            keyW = helpW = 0;
            foreach (var (key, help, header) in rows)
            {
                var text = header ? (key.Length > 0 ? key.ToUpperInvariant() : help) : key;
                keyW = System.Math.Max(keyW,
                    font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X);
                if (!header)
                    helpW = System.Math.Max(helpW,
                        font.GetStringSize(help, HorizontalAlignment.Left, -1, size).X);
            }
            boxW = pad * 2 + keyW + colGap * charW + helpW;
            var boxH = pad * 2 + rows.Count * line + headings * line / 2;
            if (boxW <= pic.W && boxH <= win.Y - pad * 2 || size <= MinFont) break;
            size--;
        }
        boxW = System.Math.Min(boxW, pic.W);

        var boxH2 = pad * 2 + rows.Count * line + headings * line / 2;  // incl. the heading space
        var box = new Rect2((win.X - boxW) / 2, (win.Y - boxH2) / 2, boxW, boxH2);

        DrawRect(box, new Color(0, 0, 0, 0.75f));                       // dark, slightly see-through
        DrawRect(box, Colors.White, false, 1);                           // 1 px border, square corners

        var y = box.Position.Y + pad + size * 0.8f;                      // the baseline of row 1
        foreach (var (key, help, header) in rows)
        {
            if (header)
            {
                if (key.Length > 0)                                     // a group heading: upper case
                {
                    y += line / 2;                                      // a little space above it
                    DrawString(font, new(box.Position.X + pad, y), key.ToUpperInvariant(),
                        HorizontalAlignment.Left, -1, size, new(0.42f, 0.66f, 1f));   // soft blue (no amber: user)
                }
                else                                                    // the title stays bright
                    DrawString(font, new(box.Position.X + pad, y), help,
                        HorizontalAlignment.Left, -1, size, Colors.White);
            }
            else
            {
                DrawString(font, new(box.Position.X + pad, y), key,
                    HorizontalAlignment.Left, -1, size, Colors.White);   // the key, bright
                DrawString(font, new(box.Position.X + pad + keyW + colGap * charW, y), help,
                    HorizontalAlignment.Left, -1, size, new(0.62f, 0.62f, 0.62f));
            }
            y += line;
        }
    }
}
