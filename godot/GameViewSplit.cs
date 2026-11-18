using System;
using Godot;

namespace Hunter;

/// <summary>The two-player screen split (ENHANCEMENT, not the original; multiplayer piece 3c,
/// LAYOUT ONLY). Settings.Players == 2: the window shows the game's 320 x 200 frame TWICE, at the
/// same pixel scale as today's single picture, with a 2-px divider - side by side, or one above
/// the other (Settings.SplitLayout). The second frame source is a STUB the later piece mp_views
/// fills: PlayerFrame(player) below returns player 1's picture for every player, so both halves
/// show the same game until a second view exists. Everything here is behind the Players == 2 gate:
/// with the default 1, none of it runs and the window is byte-for-byte today's.
/// How it sits with Display: Display (Main._Ready, before this view exists) places the ROOT node
/// for one picture and re-places it on every window SizeChanged; this view connects its own
/// handler AFTER that, so each re-place of Display's single layout is at once corrected to the
/// split layout (same frame geometry per half: stretched 320 x 200 at whole n, pal the 320 x 256
/// frame at whole k with the width rounded to 4:3 - whole-pixel scaling kept). On leaving the
/// game (_ExitTree) the root goes back to Display's placement for the title and menus.
/// Input is a later piece (mp_views / godot input half): clicks and the mouse are mapped into
/// whichever half they are over, but both halves take the same frame and the same rules play.</summary>
public partial class GameView
{
    /// <summary>The divider between the halves, in window pixels.</summary>
    const int SplitGap = 2;

    bool split;
    Sprite2D? secondFrame;
    ColorRect? divider;
    /// <summary>Player 2's own texture and its image (mp_views): Frame() updates it from
    /// play.View2 each frame, PlayerFrame(2) hands it to the right half.</summary>
    ImageTexture? secondTexture;
    Image? secondImage;
    /// <summary>The split layout, window pixels: each half's frame size (stretched: 320n x 200n;
    /// pal: round(256k*4/3) x 256k), its scales from game units, and where the content starts.</summary>
    int halfW, halfH, originX, originY;
    float scaleX, scaleY;
    bool stacked;

    /// <summary>THE HOOK for mp_views: the picture half `player` shows. Player 2 gets his own
    /// texture (built in SetupSplit, updated in Frame from play's second picture); when it does
    /// not exist yet, everyone gets player 1's frame.</summary>
    ImageTexture PlayerFrame(int player) => player == 2 && secondTexture is { } t ? t : texture;

    /// <summary>Players = 2: size the window for two halves at the largest whole scale that fits
    /// the screen, build the second frame and the divider, and take over placement (after
    /// Display's) for as long as this view is shown. Players = 1: nothing at all.</summary>
    void SetupSplit()
    {
        if (Settings.Players != 2) return;
        split = true;
        frame2 = new byte[320 * 200 * 3];   // the right half's buffer, filled every frame (Frame)
        stacked = Settings.SplitLayout == "stacked";
        // Verification only (headless runs, never in a settings file): HUNTER_SPLIT_SIZE="WxH"
        // names the window instead of the screen's size, so a run can shoot an exact 2x / 3x
        // layout whatever the (headless) screen says.
        var avail = System.Environment.GetEnvironmentVariable("HUNTER_SPLIT_SIZE") is { Length: > 0 } s
            ? new Vector2I(int.Parse(s.Split('x')[0]), int.Parse(s.Split('x')[1]))
            : DisplayServer.ScreenGetSize();
        if (avail.X < 100 || avail.Y < 100) avail = new Vector2I(1280, 720);   // headless: no screen
        var win = GetWindow();
        var (w, h, _, _, _, _) = SplitWindow(avail.X, avail.Y);
        win.Size = new Vector2I(w, h);
        // the second half (mp_views' hook): its own texture, Frame() fills it with player 2's picture
        secondImage = Image.CreateEmpty(320, 200, false, Image.Format.Rgb8);
        secondTexture = ImageTexture.CreateFromImage(secondImage);
        secondFrame = new Sprite2D
        {
            Texture = PlayerFrame(2),
            Centered = false,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        AddChild(secondFrame);
        // ... and the thin divider between the halves, in game units so the placement scales it
        divider = new ColorRect
        {
            Color = new Color(0.13f, 0.13f, 0.13f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(divider);
        win.SizeChanged += PlaceSplit;              // connected after Display's: corrects its place
        PlaceSplit();
    }

    /// <summary>The window size for two halves of the current look at the largest whole scale
    /// that fits (availW x availH): side by side, or stacked.</summary>
    (int W, int H, int HalfW, int HalfH, float ScaleX, float ScaleY) SplitWindow(int availW, int availH)
    {
        if (Display.I.Look == Display.DisplayLook.Stretched)
        {
            var n = stacked ? Math.Max(1, Math.Min(availW / 320, (availH - SplitGap) / 400))
                            : Math.Max(1, Math.Min((availW - SplitGap) / 640, availH / 200));
            return stacked ? (320 * n, 400 * n + SplitGap, 320 * n, 200 * n, n, n)
                           : (640 * n + SplitGap, 200 * n, 320 * n, 200 * n, n, n);
        }
        var k = Math.Max(1, stacked ? Math.Min((availH - SplitGap) / 512, availW * 3 / 1024)
                                    : Math.Min(availH / 256, (availW - SplitGap) * 3 / 2048));
        var w = (int)Math.Round(256L * k * 4 / 3.0);
        return stacked ? (w, 512 * k + SplitGap, w, 256 * k, w / 320f, k)
                       : (2 * w + SplitGap, 256 * k, w, 256 * k, w / 320f, k);
    }

    /// <summary>Every window size change: Display has just placed the ROOT for its single
    /// picture - put it where the split wants it (the same placement for both halves, whole
    /// window pixels: the halves' edges and the divider land on integers).</summary>
    void PlaceSplit()
    {
        if (!split || GetParent() is not Node2D root) return;
        var size = root.GetWindow().Size;
        (var winW, var winH, halfW, halfH, scaleX, scaleY) = SplitWindow((int)size.X, (int)size.Y);
        originX = ((int)size.X - winW) / 2;
        originY = ((int)size.Y - winH) / 2;
        root.Scale = new(scaleX, scaleY);
        root.Position = new(originX, originY);
        var contentH = halfH / scaleY;                                     // game rows of one half (pal: 256)
        if (stacked)
        {
            secondFrame!.Position = new(0f, (halfH + SplitGap) / scaleY);
            divider!.Position = new(0f, halfH / scaleY);
            divider.Size = new(320f, SplitGap / scaleY);
        }
        else
        {
            secondFrame!.Position = new((halfW + SplitGap) / scaleX, 0f);
            divider!.Position = new(halfW / scaleX, 0f);
            divider.Size = new(SplitGap / scaleX, contentH);
        }
    }

    /// <summary>A window point in game coordinates of the half it is over (320 x 200, with the
    /// half's number out): the split's mouse and click mapping. Outside both halves: half 1's
    /// frame clamped, as today.</summary>
    Vector2 SplitToGame(Vector2 windowPos, out int player)
    {
        var lx = windowPos.X - originX;
        var ly = windowPos.Y - originY;
        if (stacked)
        {
            player = ly < halfH + SplitGap / 2f ? 1 : 2;
            return new(lx / scaleX, Math.Max(0f, ly - (player == 2 ? halfH + SplitGap : 0)) / scaleY);
        }
        player = lx < halfW + SplitGap / 2f ? 1 : 2;
        return new(Math.Max(0f, lx - (player == 2 ? halfW + SplitGap : 0)) / scaleX, ly / scaleY);
    }

    /// <summary>The mouse / click mapping: the split's when it is up, Display's as ever when not.</summary>
    Vector2 ToGame(Vector2 windowPos) =>
        split ? SplitToGame(windowPos, out _) : Display.I.ToGame(windowPos);

    /// <summary>Leaving the game (back to the title and menus): let go of the window and put the
    /// ROOT back to Display's own single-picture placement, which the shell's other screens
    /// (StartSequence, the manual) were placed for.</summary>
    void ExitSplit()
    {
        if (!split) return;
        split = false;
        GetWindow().SizeChanged -= PlaceSplit;
        if (GetParent() is not Node2D root) return;
        var size = root.GetWindow().Size;
        var look = Display.I.Look;
        var l = Display.Layout((int)size.X, (int)size.Y, look);
        root.Scale = new((float)l.W / 320, (float)l.H / (look == Display.DisplayLook.Pal ? 256 : 200));
        root.Position = new(l.X, l.Y);
    }
}
