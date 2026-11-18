using Godot;

namespace Hunter;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (AIMING: RETICLE): the free reticle, a small cross of single game pixels with a gap in the
/// middle, in the pointer's own colour (a 12-bit Amiga colour). A child of the game view, so it sits in game pixels over the picture
/// whether the plain drawer or the card draws it. Hidden unless Place is called each frame.</summary>
public partial class Reticle : Node2D
{
    int x, y;
    Color colour = Colors.White;

    public Reticle() { ZIndex = 100; Visible = false; }

    /// <summary>Show the cross at this game pixel in this colour; Hide to remove it.</summary>
    public void Place(int gx, int gy, ushort amiga)
    {
        var c = new Color((amiga >> 8 & 15) / 15f, (amiga >> 4 & 15) / 15f, (amiga & 15) / 15f);
        if (!Visible || gx != x || gy != y || c != colour) { x = gx; y = gy; colour = c; Visible = true; QueueRedraw(); }
    }

    public void Hide() => Visible = false;

    public override void _Draw()
    {
        for (var i = 3; i <= 8; i++)                                         // arms 6 long, a gap of 2 round the point
        {
            DrawRect(new Rect2(x + i, y, 1, 1), colour); DrawRect(new Rect2(x - i, y, 1, 1), colour);
            DrawRect(new Rect2(x, y + i, 1, 1), colour); DrawRect(new Rect2(x, y - i, 1, 1), colour);
        }
        DrawRect(new Rect2(x, y, 1, 1), colour);
    }
}
