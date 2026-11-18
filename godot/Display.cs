using System;
using Godot;

namespace Hunter;

/// <summary>How the 320 x 200 game picture fills the window. "pal" draws it as the original sat
/// on a PAL TV: the picture at the top of a 320 x 256 frame (the 56 spare lines black below), the
/// whole frame shown 4:3, so one game pixel is 4/3 / (320/256) = 16/15 times as wide as tall.
/// "centred", the default (owner 2026-10-28): the same 16/15 pixels without the spare lines, the
/// 320 x 200 picture alone in the middle. "stretched": square pixels, 320 x 200 at the largest
/// whole scale. Nearest-neighbour in every look; each places the picture itself (Apply), so the
/// window renders at its own resolution and the F1 key list (HelpOverlay) can be crisp over it.
/// Chosen by HUNTER_LOOK=pal|centred|stretched, else the settings file;
/// the game keeps its 320 x 200 images and coordinates either way.</summary>
public class Display
{
    public enum DisplayLook { Pal, Centred, Stretched }

    /// <summary>The frame's height in game rows: pal's 256-line TV frame, else the picture's 200.</summary>
    public static int FrameRows(DisplayLook look) => look == DisplayLook.Pal ? 256 : 200;

    public DisplayLook Look { get; private set; }

    Display(DisplayLook look) => Look = look;

    static Display? current;
    public static Display I => current ??= new(Parse(System.Environment.GetEnvironmentVariable("HUNTER_LOOK") is { Length: > 0 } e
        ? e : Settings.Look));

    /// <summary>HUNTER_LOOK=pal|centred|stretched (empty or unset = centred).</summary>
    public static DisplayLook Parse(string? name) => name switch
    {
        "pal" => DisplayLook.Pal,
        null or "" or "centred" => DisplayLook.Centred,
        "stretched" => DisplayLook.Stretched,
        var other => throw new ArgumentException($"HUNTER_LOOK: {other} (use pal, centred or stretched)"),
    };

    /// <summary>Where the picture sits in a window of the given size, and how big it is drawn.
    /// pal: the largest whole vertical scale that fits the window (its height and, on a window
    /// narrower than 4:3, its width too), the frame 256*k tall, its width rounded to 4:3 of that.
    /// centred: the same, the frame 200*k tall and 320 x 16/15 wide per k.
    /// stretched: 320 x 200 at the largest whole scale that fits.
    /// Centred in the window, black around.</summary>
    public static (int X, int Y, int W, int H, int FrameH) Layout(int windowW, int windowH, DisplayLook look)
    {
        if (look == DisplayLook.Stretched)
        {
            var n = Math.Max(1, Math.Min(windowW / 320, windowH / 200));
            return ((windowW - 320 * n) / 2, (windowH - 200 * n) / 2, 320 * n, 200 * n, 200 * n);
        }
        var rows = FrameRows(look);
        var k = Math.Max(1, windowH / rows);
        // a window narrower than 4:3 must still show the whole frame: drop the scale until the
        // frame's width fits too (a 4:3-or-wider window never enters the loop, so its layout is
        // exactly what the height-only scale gave)
        while (k > 1 && (int)Math.Round(1024L * k / 3.0) > windowW) k--;
        var w = (int)Math.Round(1024L * k / 3.0);                             // 320 game pixels of 16/15 = 256 * 4/3 per k
        return ((windowW - w) / 2, (windowH - rows * k) / 2, w, rows * k, rows * k);
    }

    // ---- the pal look, applied to the scene ----

    (int X, int Y, int W, int H) applied;
    Node2D? placed;
    bool active;                                                            // true: Godot's stretch is off, we place and map ourselves

    /// <summary>The game picture's rectangle in the window (after Apply): the F1 key list
    /// (HelpOverlay) keeps inside it.</summary>
    public (int X, int Y, int W, int H) Picture => applied;

    /// <summary>Put the game picture (the children of the root node) where the look wants it and
    /// keep it there as the window changes size. Both looks place themselves (content scale off,
    /// the root scaled and moved): the window then renders at its own resolution, so the F1 list
    /// (a CanvasLayer, HelpOverlay) is crisp over the picture in both.</summary>
    public void Apply(Node2D root)
    {
        active = true;
        placed = root;
        var win = root.GetWindow();
        win.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        win.SizeChanged += () => Place(root);
        Place(root);
    }

    /// <summary>F10: the next look (pal, centred, stretched, round), placed at once; kept in the settings file.</summary>
    public void ToggleLook()
    {
        Look = (DisplayLook)(((int)Look + 1) % 3);
        Settings.Look = Look.ToString().ToLowerInvariant();
        if (placed is not null) Place(placed);
    }

    void Place(Node2D root)
    {
        var size = root.GetWindow().Size;
        var l = Layout(size.X, size.Y, Look);
        applied = (l.X, l.Y, l.W, l.H);
        // the frame (pal: 320 x 256, else 320 x 200) at the picture's place and size; the
        // game image fills its top 200 rows, a pal frame's spare rows show the black clear colour
        root.Scale = new((float)l.W / 320, (float)l.H / FrameRows(Look));
        root.Position = new(l.X, l.Y);
    }

    /// <summary>A window position in game coordinates (320 x 200): the mouse. Every look places
    /// itself, so undo our placement.</summary>
    public Vector2 ToGame(Vector2 windowPos)
    {
        if (!active) return windowPos;
        var scaleY = (float)applied.H / FrameRows(Look);
        return new((windowPos.X - applied.X) * 320f / applied.W, (windowPos.Y - applied.Y) / scaleY);
    }
}
