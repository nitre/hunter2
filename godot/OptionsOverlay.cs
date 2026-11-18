using System;
using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>
/// ENHANCEMENT, NOT THE ORIGINAL (options design B, owner 2026-11-01): the in-game options as one menu over the paused
/// game - rules/OptionsPanel.cs is the menu (its levels, keys, values, help, picture); this lays it over the window.
/// The picture underneath is the last frame of play, taken as Esc was pressed (the original fades play out before its
/// options screen; that fade still runs, unseen, under this), dimmed. The menu sits at the largest whole scale that
/// fits the window, centred, nearest-neighbour, so its pixels stay even. Keys, the mouse and a pad drive it; the game's
/// own pointer sprite is drawn on it (the window's cursor is hidden on menus, as everywhere in the game).
/// Whatever is chosen goes back through Chosen; GameView runs the original's handlers.
/// </summary>
public partial class OptionsOverlay : CanvasLayer
{
    readonly TextureRect shot = new() { StretchMode = TextureRect.StretchModeEnum.Scale, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
    readonly ColorRect dim = new() { Color = new Color(0, 0, 0, 0.7f), MouseFilter = Control.MouseFilterEnum.Ignore };
    readonly TextureRect menu = new() { StretchMode = TextureRect.StretchModeEnum.Scale, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
    readonly Image image = Image.CreateEmpty(OptionsPanel.Width, OptionsPanel.Height, false, Image.Format.Rgba8);
    ImageTexture? texture;

    IMenuPanel? panel;
    IndexedScreen bars = null!;
    System.Collections.Generic.IReadOnlyList<ushort> bank5 = null!;
    Ui ui = null!;
    byte[] pointer = null!;
    ushort pointerColour;
    (int X, int Y) mouse = (-100, -100);                // the pointer, in the menu's own 640 x 400
    Vector2 lastSize;

    /// <summary>Whatever the player chose - the menu's own answer (OptionsPanel.Result for the options, the answer's
    /// number for the first start's question); "nothing chosen" never comes through.</summary>
    public event Action<object>? Chosen;

    /// <summary>True while something else takes the input (the manual over the menu).</summary>
    public Func<bool>? Blocked;

    public bool Up => Visible;

    public OptionsOverlay()
    {
        Layer = 9;                                        // over the game, under the F2 key page (10)
        Visible = false;
        foreach (var c in new Control[] { shot, dim, menu })
        {
            c.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(c);
        }
        shot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
    }

    /// <summary>Up over `paused` (the window's picture as play stopped; null: black), with the game's menu art,
    /// colours, font and pointer.</summary>
    public void Open(Image? paused, IMenuPanel p, IndexedScreen optionsScreen, System.Collections.Generic.IReadOnlyList<ushort> menuBank,
                     Ui font, byte[] pointerSprite, ushort pointerRgb, float dimming = 0.7f)
    {
        panel = p;
        bars = optionsScreen;
        bank5 = menuBank;
        ui = font;
        pointer = pointerSprite;
        pointerColour = pointerRgb;
        shot.Texture = paused is null ? null : ImageTexture.CreateFromImage(paused);
        dim.Color = new Color(0, 0, 0, paused is null ? 1f : dimming);     // nothing behind: plain black
        var at = OptionsPanel.ToMenu((int)GetViewport().GetMousePosition().X, (int)GetViewport().GetMousePosition().Y, Win.X, Win.Y);
        mouse = at;
        Visible = true;
        Redraw();
    }

    public void Close()
    {
        Visible = false;
        panel = null;
        shot.Texture = null;
    }

    (int X, int Y) Win
    {
        get { var s = GetViewport().GetVisibleRect().Size; return ((int)s.X, (int)s.Y); }
    }

    /// <summary>The menu drawn again (a key, a click, a changed value, the pointer moved, the window resized).</summary>
    public void Redraw()
    {
        if (panel is null) return;
        var layer = panel.DrawLayer(bars, bank5, ui);
        var rgba = new byte[OptionsPanel.Width * OptionsPanel.Height * 4];
        for (var i = 0; i < layer.Length; i++)
        {
            var c = layer[i];
            if (c == OptionsPanel.Clear) continue;                                  // transparent: the dimmed game shows
            rgba[4 * i] = (byte)((c >> 8 & 15) * 17);
            rgba[4 * i + 1] = (byte)((c >> 4 & 15) * 17);
            rgba[4 * i + 2] = (byte)((c & 15) * 17);
            rgba[4 * i + 3] = 255;
        }
        DrawPointer(rgba);
        image.SetData(OptionsPanel.Width, OptionsPanel.Height, false, Image.Format.Rgba8, rgba);
        if (texture is null) { texture = ImageTexture.CreateFromImage(image); menu.Texture = texture; }
        else texture.Update(image);
        Place();
    }

    /// <summary>The game's pointer sprite ($AA46, 16 x 15, hot spot 7, 6), twice its size: the menu is drawn at twice
    /// the Amiga's 320 x 200, so the pointer keeps its size against the bars.</summary>
    void DrawPointer(byte[] rgba)
    {
        byte r = (byte)((pointerColour >> 8 & 15) * 17), g = (byte)((pointerColour >> 4 & 15) * 17), b = (byte)((pointerColour & 15) * 17);
        for (var row = 0; row < 15; row++)
            for (var col = 0; col < 16; col++)
            {
                if (pointer[row * 16 + col] == 0) continue;
                for (var dy = 0; dy < 2; dy++)
                    for (var dx = 0; dx < 2; dx++)
                    {
                        int x = mouse.X + (col - 7) * 2 + dx, y = mouse.Y + (row - 6) * 2 + dy;
                        if ((uint)x >= OptionsPanel.Width || (uint)y >= OptionsPanel.Height) continue;
                        var p = 4 * (y * OptionsPanel.Width + x);
                        rgba[p] = r; rgba[p + 1] = g; rgba[p + 2] = b; rgba[p + 3] = 255;
                    }
            }
    }

    void Place()
    {
        var (w, h) = Win;
        var k = OptionsPanel.ScaleFor(w, h);
        var (ox, oy) = OptionsPanel.OriginFor(w, h);
        menu.Position = new Vector2(ox, oy);
        menu.Size = new Vector2(OptionsPanel.Width * k, OptionsPanel.Height * k);
        lastSize = new Vector2(w, h);
    }

    public override void _Process(double delta)
    {
        if (Visible && GetViewport().GetVisibleRect().Size != lastSize) Place();
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || panel is null) return;
        if (Blocked?.Invoke() == true) return;
        object? r = null;
        switch (e)
        {
            case InputEventMouseMotion m:
                mouse = OptionsPanel.ToMenu((int)m.Position.X, (int)m.Position.Y, Win.X, Win.Y);
                panel.Hover(mouse.X, mouse.Y);
                Redraw();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b:
                mouse = OptionsPanel.ToMenu((int)b.Position.X, (int)b.Position.Y, Win.X, Win.Y);
                r = panel.Click(mouse.X, mouse.Y);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                r = panel.Back();                                                    // the right button steps back, as Esc
                break;
            case InputEventKey { Pressed: true } k:
                r = k.PhysicalKeycode switch
                {
                    Key.Up => panel.Up(),
                    Key.Down => panel.Down(),
                    Key.Left => panel.Left(),
                    Key.Right => panel.Right(),
                    Key.Enter or Key.KpEnter or Key.Space => k.Echo ? null : panel.Enter(),
                    Key.Escape or Key.Backspace => k.Echo ? null : panel.Back(),
                    _ => null,
                };
                if (k.PhysicalKeycode is not (Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter or Key.KpEnter or Key.Space or Key.Escape or Key.Backspace)) return;   // other keys (F2, F3 ...) go on
                break;
            case InputEventJoypadButton { Pressed: true } j:
                r = j.ButtonIndex switch
                {
                    JoyButton.DpadUp => panel.Up(),
                    JoyButton.DpadDown => panel.Down(),
                    JoyButton.DpadLeft => panel.Left(),
                    JoyButton.DpadRight => panel.Right(),
                    JoyButton.A => panel.Enter(),
                    JoyButton.B or JoyButton.Start => panel.Back(),
                    _ => null,
                };
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
        if (r is not null and not OptionsPanel.Result { Kind: OptionsPanel.Outcome.None }) Chosen?.Invoke(r);
        if (Visible) Redraw();
    }
}
