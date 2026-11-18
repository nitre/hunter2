using System;
using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>F3: the printed manual (rules/Manual.cs ManualBook) over any screen the shell shows.
/// It draws the page into its own 320 x 200 picture at (0, 0), so Display's placement of the root
/// scales and captures it exactly like the game picture (both looks), and draws the pointer sprite
/// over it as the game does (Pointer.Draw, colour 17 of palette bank 1). While it is up Main runs
/// Frame() here instead of the game's step: no blank, no pass, no sound tick - the music keeps
/// looping as it was (Audio's music is its own player), and closing carries on from the same
/// frame. It opens on the page it was last closed on (kept for the session), fading in over the
/// game's 15 fade steps; page turns do not fade.</summary>
public partial class ManualView : Node2D
{
    ManualPage page = null!;
    Image image = null!;
    ImageTexture texture = null!;
    readonly byte[] rgb = new byte[320 * 200 * 3], lut = new byte[64 * 3];   // made once, filled each frame
    byte[] pointer = null!;
    ushort pointerColour;
    int mouseX = 160, mouseY = 100;
    Main main = null!;
    bool up;

    /// <summary>The manual is up: the shell steps this view and nothing else.</summary>
    public bool Up => up;

    public void Init(GameData d, Main owner)
    {
        main = owner;
        page = new ManualPage(ManualBook.Load(d.Directory, Ui.Load(d.Directory)).WithFigures(new ManualFigures(d)));
        pointer = Pointer.Sprite(d.Main);
        pointerColour = d.PaletteBanks[1][17];          // colour 17 of bank 1, as every screen ($610C)
        image = Image.CreateEmpty(320, 200, false, Image.Format.Rgb8);
        texture = ImageTexture.CreateFromImage(image);
        AddChild(new Sprite2D { Texture = texture, Centered = false, TextureFilter = TextureFilterEnum.Nearest });
        Visible = false;
    }

    /// <summary>F3 opens it on the page it was last closed on; F3 or Esc closes it.</summary>
    public void Toggle()
    {
        up = !up;
        Visible = up;
        if (up) page.Show(page.Page, fade: true);
    }

    public void Page(int dir) => page.Turn(dir);
    public void Home() => page.First();
    public void End() => page.Last();

    /// <summary>A click at game-picture pixels (Display.ToGame, or the headless script): the
    /// page's own "&lt;" / "&gt;" footer hints page, the "ESC" hint closes.</summary>
    public void Click(int x, int y)
    {
        mouseX = Math.Clamp(x, 0, 319);
        mouseY = Math.Clamp(y, 0, 199);
        switch (page.FooterClick(mouseX, mouseY))
        {
            case -1: page.Turn(-1); break;
            case 1: page.Turn(1); break;
            case 0: main.ToggleManual(); break;
        }
    }

    /// <summary>The pad's A on a screen: a click at the pointer.</summary>
    public void PadClick() => Click(mouseX, mouseY);

    /// <summary>One frame while up: the pointer follows the mouse and the pad's sticks (as the
    /// menus' ReadMouse), the fade steps, the page is drawn through its palette.</summary>
    public void Frame()
    {
        TrackMouse();
        page.Frame();
        Present();
    }

    void TrackMouse()
    {
        if (Input.GetConnectedJoypads().Count > 0)
        {
            float ax = Input.GetJoyAxis(0, JoyAxis.LeftX) + Input.GetJoyAxis(0, JoyAxis.RightX),
                  ay = Input.GetJoyAxis(0, JoyAxis.LeftY) + Input.GetJoyAxis(0, JoyAxis.RightY);
            if (Mathf.Abs(ax) > 0.3f || Mathf.Abs(ay) > 0.3f)
            {
                mouseX = Math.Clamp(mouseX + (int)Math.Round(ax * 4), 0, 319);
                mouseY = Math.Clamp(mouseY + (int)Math.Round(ay * 4), 0, 199);
                return;
            }
        }
        var p = Display.I.ToGame(GetViewport().GetMousePosition());
        mouseX = Math.Clamp((int)p.X, 0, 319);
        mouseY = Math.Clamp((int)p.Y, 0, 199);
    }

    void Present()
    {
        var pal = page.PaletteNow();
        for (var i = 0; i < 64; i++)
        {
            var c = pal[i];
            lut[3 * i] = (byte)((c >> 8 & 15) * 17);
            lut[3 * i + 1] = (byte)((c >> 4 & 15) * 17);
            lut[3 * i + 2] = (byte)((c & 15) * 17);
        }
        var px = page.Screen.Pixels;
        for (var p = 0; p < px.Length; p++)
        {
            var c = 3 * px[p];
            rgb[3 * p] = lut[c]; rgb[3 * p + 1] = lut[c + 1]; rgb[3 * p + 2] = lut[c + 2];
        }
        Pointer.Draw(rgb, pointer, pointerColour, mouseX, mouseY);
        image.SetData(320, 200, false, Image.Format.Rgb8, rgb);
        texture.Update(image);
    }

    /// <summary>Save the current frame (verification: HUNTER_CAPTURE while the manual is up).</summary>
    public void Save(string path) => image.SavePng(path);
}
