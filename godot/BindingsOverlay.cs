using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hunter;

/// <summary>F2: the rebind screen, drawn over the window like the F1 key list (a CanvasLayer, so the
/// 320 x 200 picture and its captures are untouched). The game is paused while it is open. Each row is
/// one action (Bindings.All) with the keys and gamepad inputs bound to it now.
/// Up / Down (or the d-pad) choose a row, Enter (or pad A) sets the action's key or gamepad button to the next one pressed - taking it from
/// another action - Backspace clears the
/// row, Delete puts its defaults back, Home puts them all back, F2 (or Esc, pad Start) closes. While waiting
/// for an input F2 cancels, and Esc is just another key to bind.</summary>
public partial class BindingsOverlay : CanvasLayer
{
    readonly BindingsList list = new();

    public BindingsOverlay()
    {
        ProcessMode = ProcessModeEnum.Always;                        // runs while the game is paused
        list.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(list);
        Layer = 10;
    }

    public bool Open => Visible;

    public void Toggle()
    {
        Visible = !Visible;
        GetTree().Paused = Visible;
        if (Visible) list.Reset();
        list.SetProcessInput(Visible);
        list.QueueRedraw();
    }
}

public partial class BindingsList : Control
{
    readonly Font font = new SystemFont { FontNames = ["Consolas", "Lucida Console", "Courier New"] };
    int row;
    bool capturing;
    string message = "";

    public BindingsList()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetProcessInput(false);
    }

    public override void _Ready() => GetWindow().SizeChanged += QueueRedraw;

    public void Reset() { row = 0; capturing = false; message = ""; }

    Bindings.Action Current => Bindings.All[row];

    public override void _Input(InputEvent e)
    {
        if (capturing) { Capture(e); return; }
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } k:
                switch (k.Keycode)
                {
                    case Key.Up: Move(-1); break;
                    case Key.Down: Move(1); break;
                    case Key.Enter or Key.KpEnter: Begin(); break;
                    case Key.Backspace: Bindings.Clear(Current); message = "cleared"; break;
                    case Key.Delete: Bindings.Reset(Current); message = "default restored"; break;
                    case Key.Home: Bindings.ResetAll(); message = "all defaults restored"; break;
                    case Key.F2 or Key.Escape: Close(); break;
                    default: return;
                }
                break;
            case InputEventJoypadButton { Pressed: true } b:
                switch (b.ButtonIndex)
                {
                    case JoyButton.DpadUp: Move(-1); break;
                    case JoyButton.DpadDown: Move(1); break;
                    case JoyButton.A: Begin(); break;
                    case JoyButton.Start: Close(); break;
                    default: return;
                }
                break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
        QueueRedraw();
    }

    void Move(int d) { row = (row + d + Bindings.All.Count) % Bindings.All.Count; message = ""; }

    void Begin() { capturing = true; message = ""; }

    void Close() => ((BindingsOverlay)GetParent()).Toggle();

    void Capture(InputEvent e)
    {
        bool isPad;
        int code;
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F2 }:
                capturing = false; message = "cancelled";
                GetViewport().SetInputAsHandled();
                QueueRedraw();
                return;
            case InputEventKey { Pressed: true, Echo: false } k:
                isPad = false;
                code = k.Keycode == Key.Ctrl && k.Location == KeyLocation.Right ? Bindings.RightCtrl
                    : (int)(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode);
                break;
            case InputEventJoypadButton { Pressed: true } b:
                isPad = true;
                code = (int)b.ButtonIndex;
                break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
        capturing = false;
        var name = isPad ? Bindings.PadName(code) : Bindings.KeyName(code);
        if (!isPad && Bindings.IsReservedKey(code)) message = name + " is kept for the window keys";
        else
        {
            var taken = Bindings.Bind(Current, isPad, code);
            message = taken is null ? "set " + name : $"set {name} (taken from \"{taken.Label}\")";
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var win = GetViewport().GetVisibleRect().Size;
        var pic = Display.I.Picture;
        const float pad = 8;
        var rows = Bindings.All;
        // lines: title, one per action plus a heading per group, blank, footer (2), message
        var groups = rows.Select(a => a.Group).Distinct().Count();
        var lineCount = 1 + rows.Count + groups + 4;
        // the text-size setting (F8, normal / large / extra large) multiplies the base; the shrink
        // loop keeps the box inside the window and the game picture (the floor stays 14 px)
        var size = (int)System.Math.Round(System.Math.Min(System.Math.Max(14, (int)win.Y / 32),
            System.Math.Max(14, (int)((win.Y - pad * 2) / (1.5f * lineCount)))) * Settings.TextScale);
        float labelW, bindW, boxW;
        while (true)
        {
            labelW = bindW = 0;
            foreach (var a in rows)
            {
                labelW = System.Math.Max(labelW, font.GetStringSize(a.Label, HorizontalAlignment.Left, -1, size).X);
                bindW = System.Math.Max(bindW, font.GetStringSize(Text(a), HorizontalAlignment.Left, -1, size).X);
            }
            var charW = font.GetStringSize("M", HorizontalAlignment.Left, -1, size).X;
            boxW = pad * 2 + labelW + 2 * charW + bindW;
            var needH = pad * 2 + lineCount * (size + size / 2);
            if (boxW <= pic.W && needH <= win.Y - pad * 2 || size <= 14) break;
            size--;
        }
        boxW = System.Math.Min(boxW, pic.W);
        var line = size + size / 2;
        var boxH = pad * 2 + lineCount * line;
        var box = new Rect2((win.X - boxW) / 2, (win.Y - boxH) / 2, boxW, boxH);
        DrawRect(box, new Color(0, 0, 0, 0.85f));
        DrawRect(box, Colors.White, false, 1);

        var x = box.Position.X + pad;
        var y = box.Position.Y + pad + size * 0.8f;
        var soft = new Color(0.62f, 0.62f, 0.62f);
        DrawString(font, new(x, y), "CHANGE KEYS AND GAMEPAD  (F2 closes)", HorizontalAlignment.Left, -1, size, Colors.White);
        y += line;
        string? lastGroup = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var a = rows[i];
            if (a.Group != lastGroup)
            {
                DrawString(font, new(x, y), a.Group.ToUpperInvariant(), HorizontalAlignment.Left, -1, size, new(0.42f, 0.66f, 1f));
                y += line;
                lastGroup = a.Group;
            }
            if (i == row)
                DrawRect(new Rect2(box.Position.X + 2, y - size * 0.85f, boxW - 4, line), new Color(0.25f, 0.35f, 0.6f, 0.6f));
            var text = i == row && capturing ? "press a key or a pad button..." : Text(a);
            DrawString(font, new(x, y), a.Label, HorizontalAlignment.Left, -1, size, Colors.White);
            DrawString(font, new(x + labelW + font.GetStringSize("MM", HorizontalAlignment.Left, -1, size).X, y), text,
                HorizontalAlignment.Left, -1, size, soft);
            y += line;
        }
        y += line / 2;
        DrawString(font, new(x, y), "Up/Down choose   Enter set   Backspace clear   Delete default   Home all", HorizontalAlignment.Left, -1, size - 2, soft);
        y += line;
        DrawString(font, new(x, y), message, HorizontalAlignment.Left, -1, size, Colors.White);
    }

    static string Text(Bindings.Action a) => Bindings.Describe(a);
}
