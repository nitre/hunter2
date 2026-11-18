using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>The joystick in port 2 and the zoom keys, as the player's bindings say (Bindings: the arrow keys move,
/// right Ctrl fires, PageUp / PageDown zoom by default, the keypad always works as the original's joystick, plus a
/// gamepad). The original zoomed on cursor up / down (manual 5); for keyboards without a keypad
/// the arrows move by default and zoom is on PageUp / PageDown - the original's keys are one rebind away (F2).</summary>
public static class JoystickInput
{
    public static Joystick Read() => Bindings.ReadJoystick();

    /// <summary>STICK STEERING: the left stick's sideways lean past the dead zone, -16 (full left) .. 16 (full right), 0 inside it
    /// (Bindings.ReadLean). Keys and the d-pad never give a lean.</summary>
    public static int Lean() => Bindings.ReadLean();

    /// <summary>Player 2's joystick (mp_two_soldiers): the cursor arrows, right Shift fire,
    /// a second gamepad (Bindings.ReadJoystick2).</summary>
    public static Joystick Read2() => Bindings.ReadJoystick2();

    /// <summary>Feed every input event here (GameView._Input) so right Ctrl is tracked.</summary>
    public static void Note(InputEvent e) => Bindings.Note(e);

    /// <summary>Zoom out / in: PageUp / PageDown by default (the original's cursor up / down, $B29A + $4C / $4D), or
    /// whatever is bound.</summary>
    public static (bool Out, bool In) Zoom() => Bindings.Zoom();
}
