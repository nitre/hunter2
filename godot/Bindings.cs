using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>
/// What the player's keys and gamepad do, as one small remappable table (shell only: the rules still
/// see the original's joystick bits and raw key codes, nothing else).
///
/// Every action has one key and one gamepad button, either may be none. The defaults: the arrow keys move (a
/// keyboard without a keypad - 85% and smaller - could not walk), right Ctrl fires, PageUp / PageDown
/// zoom (as two-player already had), the table's letters; plus a gamepad (A fire, shoulders zoom, B leave vehicle,
/// X next weapon, Y hover, Start options, Back pause). Two keys held give a diagonal.
/// Not remappable, always on: the keypad - 8 2 4 6, its diagonals 7 9 1 3 and 0 to fire (the original's keys as
/// WinUAE maps the joystick) - and the d-pad and the left stick for movement (the original's joystick). The rebind screen (BindingsOverlay, F2) changes the table; it is
/// kept in the settings file as "key,pad" per action, and an action missing from it keeps its default.
/// </summary>
public static class Bindings
{
    public const int None = -1;

    /// <summary>Godot reports both Ctrl keys as Key.Ctrl; the original's joystick fire sits on the right one
    /// (WinUAE's mapping), so it is a key code of its own here.</summary>
    public const int RightCtrl = -2;

    public sealed record Action(string Id, string Label, string Group, int Raw, int DefaultKey, int DefaultPad);

    static Action A(string id, string label, string group, Key key, JoyButton? pad, int raw = 0) =>
        new(id, label, group, raw, (int)key, pad is { } p ? (int)p : None);

    public static readonly Action MoveUp = A("up", "move / steer forward", "Move", Key.Up, JoyButton.DpadUp);
    public static readonly Action MoveDown = A("down", "move / steer back", "Move", Key.Down, JoyButton.DpadDown);
    public static readonly Action MoveLeft = A("left", "move / steer left", "Move", Key.Left, JoyButton.DpadLeft);
    public static readonly Action MoveRight = A("right", "move / steer right", "Move", Key.Right, JoyButton.DpadRight);
    public static readonly Action Fire = new("fire", "fire", "Weapons", 0, RightCtrl, (int)JoyButton.A);
    public static readonly Action ZoomOut = A("zoomout", "zoom out", "Screens", Key.Pageup, JoyButton.LeftShoulder);
    public static readonly Action ZoomIn = A("zoomin", "zoom in", "Screens", Key.Pagedown, JoyButton.RightShoulder);

    /// <summary>The pad defaults for the play keys, by raw code (PlayInput.Table rows).</summary>
    static readonly Dictionary<int, JoyButton> PadDefault = new()
    {
        [0x40] = JoyButton.B,          // leave a vehicle
        [0x44] = JoyButton.X,          // next weapon
        [0x5F] = JoyButton.Y,          // hover
        [0x45] = JoyButton.Start,      // the in-game options screen
        [0x19] = JoyButton.Back,       // pause
    };

    static readonly string[] GroupOrder = ["Move", "Weapons", "Vehicles", "Items", "Screens"];

    /// <summary>Every action, in the order the screens list them: the joystick ones, then PlayInput's table
    /// (its help text and group; its key is the keyboard default).</summary>
    public static readonly IReadOnlyList<Action> All = new[] { MoveUp, MoveDown, MoveLeft, MoveRight, Fire, ZoomOut, ZoomIn }
        .Concat(PlayInput.Table.Select(t => new Action("raw" + t.Raw.ToString("X2"), t.Help, t.Group, t.Raw, (int)t.Key,
            PadDefault.TryGetValue(t.Raw, out var b) ? (int)b : None)))
        .OrderBy(a => Array.IndexOf(GroupOrder, a.Group))
        .ToArray();

    /// <summary>Keys the shell keeps for itself (F1 list, F2 this screen, F3 the manual, F7 F8 brightness and text size, F9-F11 window settings).</summary>
    static readonly Key[] Reserved = [Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11];   // F4/F5: easy_saves' quick load / quick save
    public static bool IsReservedKey(int code) => code >= 0 && Reserved.Contains((Key)code);

    static readonly Dictionary<string, (int Key, int Pad)> Current = Load();

    static Dictionary<string, (int Key, int Pad)> Load()
    {
        var d = new Dictionary<string, (int, int)>();
        foreach (var a in All)
        {
            var p = Settings.GetString("bindings", a.Id)?.Split(',');
            d[a.Id] = p is { Length: 2 } && int.TryParse(p[0], out var k) && int.TryParse(p[1], out var b) ? (k, b) : (a.DefaultKey, a.DefaultPad);
        }
        return d;
    }

    static void Set(Action a, int key, int pad)
    {
        Current[a.Id] = (key, pad);
        Settings.SetString("bindings", a.Id, key + "," + pad);
    }

    public static int KeyOf(Action a) => Current[a.Id].Key;
    public static int PadOf(Action a) => Current[a.Id].Pad;

    /// <summary>Give an action a key (isPad false) or a pad button; whoever had it loses it (returned).
    /// Reserved keys are refused.</summary>
    public static Action? Bind(Action a, bool isPad, int code)
    {
        if (!isPad && IsReservedKey(code)) return null;
        Action? taken = null;
        foreach (var o in All)
        {
            if (o == a) continue;
            var (k, p) = Current[o.Id];
            if (!isPad && k == code) { Set(o, None, p); taken = o; }
            if (isPad && p == code) { Set(o, k, None); taken = o; }
        }
        var (ak, ap) = Current[a.Id];
        Set(a, isPad ? ak : code, isPad ? code : ap);
        return taken;
    }

    public static void Clear(Action a) => Set(a, None, None);
    public static void Reset(Action a) => Set(a, a.DefaultKey, a.DefaultPad);
    public static void ResetAll() { foreach (var a in All) Reset(a); }

    // ---- reading ----

    const float Dead = 0.5f;

    // Godot reports both Ctrl keys as Key.Ctrl; only key events say which side. Same for Shift
    // (player 2's fire, mp_two_soldiers).
    static bool _rightCtrl;
    static bool _rightShift;

    /// <summary>Feed every input event here (GameView._Input) so the right Ctrl key is tracked.</summary>
    public static void Note(InputEvent e)
    {
        if (e is InputEventKey { Keycode: Godot.Key.Ctrl } k && k.Location == KeyLocation.Right) _rightCtrl = k.Pressed;
        if (e is InputEventKey { Keycode: Godot.Key.Shift } sh && sh.Location == KeyLocation.Right) _rightShift = sh.Pressed;
    }

    static bool KeyHeld(int code) =>
        code == RightCtrl ? _rightCtrl : code >= 0 && Godot.Input.IsPhysicalKeyPressed((Key)code);

    static bool PadHeld(int code) =>
        code >= 0 && PadIn() && Godot.Input.IsJoyButtonPressed(0, (JoyButton)code);

    static float Axis(JoyAxis a) => PadIn() ? Godot.Input.GetJoyAxis(0, a) : 0;

    // a pad plugged in: asked once a frame, not on every key test (the list is a fresh array each call)
    static ulong padFrame = ulong.MaxValue;
    static bool padIn;
    static bool PadIn()
    {
        var f = Godot.Engine.GetProcessFrames();
        if (f != padFrame) { padFrame = f; padIn = Godot.Input.GetConnectedJoypads().Count > 0; }
        return padIn;
    }

    static bool IsDown(Action a, params Key[] fixedKeys) =>
        KeyHeld(Own(KeyOf(a))) || PadHeld(PadOf(a)) || fixedKeys.Any(k => KeyHeld((int)k));

    /// <summary>With two players the arrow keys are player 2's (ReadJoystick2), so player 1's actions skip one bound
    /// there; player 1 then moves with the keypad or a pad. Two players on one keyboard without a keypad: not needed
    /// for now.</summary>
    static int Own(int key) => Settings.Players == 2 && key is (int)Key.Up or (int)Key.Down or (int)Key.Left or (int)Key.Right ? None : key;

    /// <summary>The joystick bits for the game (port 2). Always on besides the table: the keypad (8 2 4 6, the
    /// diagonals, 0 fire), and the left stick.</summary>
    public static Joystick ReadJoystick()
    {
        bool up = IsDown(MoveUp, Key.Kp8, Key.Kp7, Key.Kp9) || Axis(JoyAxis.LeftY) < -Dead,
            down = IsDown(MoveDown, Key.Kp2, Key.Kp1, Key.Kp3) || Axis(JoyAxis.LeftY) > Dead,
            left = IsDown(MoveLeft, Key.Kp4, Key.Kp7, Key.Kp1) || Axis(JoyAxis.LeftX) < -Dead,
            right = IsDown(MoveRight, Key.Kp6, Key.Kp9, Key.Kp3) || Axis(JoyAxis.LeftX) > Dead;
        // a stick cannot lean both ways at once (JOY1DAT never reads up+down or left+right): two opposite
        // keys held = neither, so the game never sees a direction its readers cannot get (sweep_shell_input)
        if (up && down) up = down = false;
        if (left && right) left = right = false;
        return new(up, down, left, right, IsDown(Fire, Key.Kp0));
    }

    /// <summary>STICK STEERING (rules WorldState.AnalogLean): the left stick's sideways lean in 16ths of the full turn. Past the dead zone
    /// (the same 0.5 that gives ReadJoystick its left / right) the lean runs 1..16 up to full tilt, so the turn grows from nothing
    /// there; inside it, 0. Right is positive. Only the stick - keys and the d-pad give 0 and turn fully, as the original.</summary>
    public static int ReadLean()
    {
        var x = Axis(JoyAxis.LeftX);
        var m = (Mathf.Abs(x) - Dead) / (1 - Dead);
        if (m <= 0) return 0;
        var n = Math.Clamp((int)Mathf.Ceil(m * 16), 1, 16);
        return x < 0 ? -n : n;
    }

    // ---- player 2 (mp_two_soldiers) ----
    // Player 2's own keys, fixed (not in the rebind table): the cursor arrows move, right Shift
    // fires, and a SECOND connected gamepad's d-pad / left stick / A drive him as well. With
    // players = 2 the cursor keys are his movement, so the shared zoom is on PageUp / PageDown (see Zoom) -
    // since 2026-11-01 the one-player default too, the arrows moving player 1.

    static float Axis2(JoyAxis a) => Godot.Input.GetConnectedJoypads().Count > 1 ? Godot.Input.GetJoyAxis(1, a) : 0;
    static bool Pad2Held(JoyButton b) => Godot.Input.GetConnectedJoypads().Count > 1 && Godot.Input.IsJoyButtonPressed(1, b);

    /// <summary>The joystick bits for player 2 (the split's second half).</summary>
    public static Joystick ReadJoystick2()
    {
        bool up = KeyHeld((int)Key.Up) || Axis2(JoyAxis.LeftY) < -Dead || Pad2Held(JoyButton.DpadUp),
            down = KeyHeld((int)Key.Down) || Axis2(JoyAxis.LeftY) > Dead || Pad2Held(JoyButton.DpadDown),
            left = KeyHeld((int)Key.Left) || Axis2(JoyAxis.LeftX) < -Dead || Pad2Held(JoyButton.DpadLeft),
            right = KeyHeld((int)Key.Right) || Axis2(JoyAxis.LeftX) > Dead || Pad2Held(JoyButton.DpadRight);
        // a stick cannot lean both ways at once (as ReadJoystick): two opposite keys = neither
        if (up && down) up = down = false;
        if (left && right) left = right = false;
        return new(up, down, left, right, _rightShift || Pad2Held(JoyButton.A));
    }

    public static (bool Out, bool In) Zoom() => Settings.Players == 2
        ? (KeyHeld((int)Key.Pageup) || PadHeld(PadOf(ZoomOut)), KeyHeld((int)Key.Pagedown) || PadHeld(PadOf(ZoomIn)))
        : (IsDown(ZoomOut), IsDown(ZoomIn));

    /// <summary>The play-key action a key or gamepad press event is bound to, or null.</summary>
    public static Action? Pressed(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } k:
                var code = k.Keycode == Key.Ctrl && k.Location == KeyLocation.Right ? RightCtrl
                    : (int)(k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode);
                return All.FirstOrDefault(a => a.Raw != 0 && KeyOf(a) == code);
            case InputEventJoypadButton { Pressed: true } b:
                return All.FirstOrDefault(a => a.Raw != 0 && PadOf(a) == (int)b.ButtonIndex);
            default:
                return null;
        }
    }

    // ---- names ----

    public static string KeyName(int code) => code switch
    {
        None => "-",
        RightCtrl => "right Ctrl",
        (int)Key.Space => "Space",
        (int)Key.Enter => "Return (Enter)",
        (int)Key.Escape => "Esc",
        (int)Key.Quoteleft => "` (left of 1)",
        >= (int)Key.Kp0 and <= (int)Key.Kp9 => "keypad " + (code - (int)Key.Kp0),
        (int)Key.Up => "cursor up", (int)Key.Down => "cursor down",
        (int)Key.Left => "cursor left", (int)Key.Right => "cursor right",
        _ => OS.GetKeycodeString((Key)code),
    };

    public static string PadName(int code) => code switch
    {
        None => "-",
        (int)JoyButton.A => "pad A", (int)JoyButton.B => "pad B", (int)JoyButton.X => "pad X", (int)JoyButton.Y => "pad Y",
        (int)JoyButton.Back => "pad Back", (int)JoyButton.Start => "pad Start",
        (int)JoyButton.LeftStick => "left stick click", (int)JoyButton.RightStick => "right stick click",
        (int)JoyButton.LeftShoulder => "left shoulder", (int)JoyButton.RightShoulder => "right shoulder",
        (int)JoyButton.DpadUp => "d-pad up", (int)JoyButton.DpadDown => "d-pad down",
        (int)JoyButton.DpadLeft => "d-pad left", (int)JoyButton.DpadRight => "d-pad right",
        _ => "button " + code,
    };

    /// <summary>"key, pad" as the screens show an action's inputs (a missing one left out).</summary>
    public static string Describe(Action a)
    {
        var parts = new List<string>();
        if (KeyOf(a) != None) parts.Add(KeyName(KeyOf(a)));
        if (PadOf(a) != None) parts.Add(PadName(PadOf(a)));
        return parts.Count == 0 ? "(nothing)" : string.Join(", ", parts);
    }

    /// <summary>The F1 list's rows: what each action is bound to now, grouped.</summary>
    public static IEnumerable<(string Keys, string Help, string Group)> HelpRows() =>
        All.Select(a => (Describe(a), a.Label, a.Group)).Where(r => r.Item1 != "(nothing)");
}
