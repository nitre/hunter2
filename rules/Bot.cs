namespace Hunter.Rules;

/// <summary>(the BOT, docs/bot-plan.md): what plays player 1 instead of the port - asked for
/// the joystick every blank of play (Play.Pilot, Play.VerticalBlank).</summary>
public interface IPilot
{
    Joystick Next(Play play);
    /// <summary>The keys and the mouse, as a player's hands: called at the start of every blank outside the demo (in play and
    /// on the screens - a store's, the map's), before the stick; it may call Play.KeyDown and Play.Click as the keyboard and
    /// the mouse would between blanks.</summary>
    void Act(Play play) { }
}

/// <summary>What the brain wants this blank: to go (or stand), which way (a world heading byte as +$7E holds it:
/// 0 = +z, $40 = +x - Movement.Vector), and to fire. Hold: in a vehicle, stop and turn to the heading where it stands (a tank
/// turns on the spot); on foot the same as standing. In a helicopter (Body.Fly): Alt the height (+$4C) to hold (-1: none,
/// stay level), Tilt the pitch byte (+$7C high) to hold - 0 thrusts straight up, more leans the thrust forward (Go false:
/// tilt 0, land and stop the rotor).</summary>
public readonly record struct Wish(bool Go, byte Heading, bool Fire, bool Hold = false, int Alt = -1, int Tilt = 0, bool Travel = false, int Thrust = -1)
{
    public static readonly Wish Stand = new(false, 0, false);

    /// <summary>The heading byte from (x, z) toward (tx, tz) (0 = +z, $40 = +x). Straight, never round: the map does not wrap -
    /// its edge ring of squares is blocked (Settle $2B14; a probe confirmed it).</summary>
    public static byte Toward(int x, int z, int tx, int tz)
    {
        int dx = (ushort)tx - (ushort)x, dz = (ushort)tz - (ushort)z;
        if (dx == 0 && dz == 0) return 0;
        return (byte)((int)Math.Round(Math.Atan2(dx, dz) * 128 / Math.PI) & 0xFF);
    }
}

/// <summary>The bot's mind (and later the computer soldiers'): sees the game as a soldier would, decides a Wish each blank.</summary>
public interface IBrain
{
    Wish Decide(Play play);
    /// <summary>Keys and clicks this blank (IPilot.Act): weapon changes, a store's screen.</summary>
    void Act(Play play) { }
}

/// <summary>The BOT: a brain playing player 1 through the joystick, by the game's own rules - nothing in the world is moved
/// by hand, so the same brain on the same game plays the same film every time.</summary>
public sealed class Bot(IBrain brain) : IPilot
{
    public IBrain Brain { get; } = brain;

    int held = -1, still;
    (int X, int Z) at;

    /// <summary>The held direction (Body.Hold) is let go once he has not moved for 25 blanks - held into a wall it gets him
    /// nowhere; counted in blanks, as a pass can take several.</summary>
    public Joystick Next(Play play)
    {
        var me = play.State.Entities.All[0];
        var here = (me.UW(0x4A), me.UW(0x4E));
        if (here != at) { at = here; still = 0; } else if (++still >= 25) held = -1;
        return Body.Stick(play, Brain.Decide(play), ref held);
    }
    public void Act(Play play) => Brain.Act(play);
}

/// <summary>The bot's body: a Wish into the stick. On foot ($25EA: +$08 bit 15) the stick's 8 directions face 8 headings
/// (Controls.FootHeading, the view turn added): the one nearest the wish. In a land vehicle (Drive) left / right turn it
/// (+$18: right raises +$7E) and up / down step the speed a blank (SpeedControl $362E, kept within 0 and the top speed +$36).</summary>
public static class Body
{
    static readonly int[] Directions = [2, 6, 4, 5, 1, 9, 8, 10];   // up, up-right, right, down-right, down, down-left, left, up-left (D6 bits)

    public static Joystick Stick(Play play, Wish w) { var none = -1; return Stick(play, w, ref none); }

    /// <summary>How far (heading steps) the wish may lie off the held direction before the stick moves: the 8 ways are 32
    /// apart, so the nearest is never more than 16 off - holding to 24 stops the shake between two ways (seen: 531 flips in
    /// 1000 passes) while the brain's re-aiming still brings him there.</summary>
    const int Hold = 24;

    /// <summary>On foot, a way to go (Wish.Travel) judges each direction by where its step really goes, not the way it faces: the
    /// step ($2AE6: +$3E along pitch and heading, Vector $2C00) rounds each part by asr - at swim speed 2, facing 46 goes (1, 0),
    /// due east. M2 in one game 11-09: the aim onto the bank lay inside the hold from that facing - 1536 swum along it. Never
    /// for a facing (a shot goes the way he faces: judged by the step, he never faced his aim - 23000 passes "shooting").
    /// A switch for on / off runs.</summary>
    public static bool RealStep = true;

    /// <summary>The stick for a wish; on foot `held` is the direction kept from the last blank (-1: none).</summary>
    public static Joystick Stick(Play play, Wish w, ref int held)
    {
        var me = play.State.Entities.All[0];
        if (me.Alive && Flies(me)) { held = -1; return Fly(play.State, me, w, DropTicks(play)); }
        if (me.Alive && !me.Bit(0x08, 15)) { held = -1; return Drive(me, w); }
        if (!w.Go || !me.Alive) { held = -1; return new(false, false, false, false, w.Fire); }
        var s = play.State;
        byte Goes(int d)
        {
            var h = Controls.FootHeading(play.Data.Main, d, s.ViewAngle1);
            if (!RealStep || !w.Travel || me.UW(0x3E) == 0) return h;        // a facing, or standing (no step yet): the way it faces
            var (x, _, z) = Movement.Vector(s.Trig, (byte)(me.B[0x7C] - me.B[0x39]), h, (short)me.UW(0x3E), s.StraightMoves);
            return x == 0 && z == 0 ? h : Wish.Toward(0x8000, 0x8000, 0x8000 + x, 0x8000 + z);
        }
        int Off(int d) => Math.Abs((int)(sbyte)(Goes(d) - w.Heading));
        var best = Directions[0]; var bestOff = int.MaxValue;
        foreach (var d in Directions)
            if (Off(d) < bestOff) (best, bestOff) = (d, Off(d));
        if (held >= 0 && Off(held) <= Hold) best = held;
        held = best;
        return Joystick.FromD6((byte)(best | (w.Fire ? 0x10 : 0)));
    }

    /// <summary>A helicopter: +$08 bit 11 ($2A24, SpeedControl $3662).</summary>
    public static bool Flies(Entity v) => v.Bit(0x08, 11);

    /// <summary>The speed that holds a helicopter's height at a tilt (pitch byte, 64 = a right angle): its thrust along the
    /// pitch (Movement.Vector: the pitch less the trim +$39, 63) against gravity +$3A a tick (Settle).</summary>
    public static double HoverSpeed(Entity v, int tilt) => v.W(0x3A) / Math.Cos(Math.Min(tilt, 56) * Math.PI / 128);

    /// <summary>The ticks a landing's drop is measured over ($2B7C: from where the pass began; A500 PACE: from the last A500
    /// pass, up to PaceBlanks more).</summary>
    static int DropTicks(Play play) => Math.Max(1, play.LastTicks) + (play.State.A500Pace ? WorldState.PaceBlanks : 0);

    /// <summary>How far a helicopter at this thrust comes down over these ticks, as the pass works it: the thrust times the
    /// ticks along its pitch and heading ($2AE6, Movement.Vector), gravity +$3A a tick off the height (Settle $2AF2).</summary>
    static int Drop(WorldState s, Entity v, int thrust, int ticks) =>
        v.W(0x3A) * ticks - Movement.Vector(s.Trig, v, (short)(thrust * ticks), s.StraightMoves).Y;

    /// <summary>A helicopter's stick. The throttle (fire with the stick exactly up / down, $3662: a step a blank, and the stick
    /// hidden from that pass) holds the height: the speed for the tilt's hover, more to climb, less to sink (at most 6 a tick
    /// down). Coming down near the ground the thrust is at least the least whose drop over the pass stays within +$78 (a soft
    /// landing: CrashDestroyEntity past it, $2B9C), the throttle before the stick while it is under. Otherwise the stick: up /
    /// down move the pitch a byte a tick toward the tilt wished (+$19, TurnPitchDeltas), left / right turn (+$18); fire alone
    /// fires. On the ground x and z hold ($2A24): it rises before it leans. Go false on the ground: the throttle down to 0, the
    /// rotor stopped.</summary>
    static Joystick Fly(WorldState s, Entity v, Wish w, int ticks = 1)
    {
        int speed = v.W(0x3E), top = v.W(0x36), y = v.W(0x4C), ground = v.W(0x74);
        var grounded = v.B[0x1E] != 0;
        var pitch = (sbyte)v.B[0x7C];
        if (!w.Go && grounded)
            return speed > 0 ? new(false, true, false, false, true) : new(false, false, false, false, false);
        var alt = w.Go ? (w.Alt >= 0 ? w.Alt : y) : ground;                  // not going: down where it is
        var tilt = grounded || !w.Go ? 0 : alt - y > 200 ? 0 : alt - y > 64 ? w.Tilt / 2 : w.Tilt;   // low: lean less, the thrust to the climb
        var climb = Math.Clamp((alt - y) / 8.0, -6, 3);
        if (!w.Go) climb = Math.Clamp((ground - y) / 8.0, -4, -1);
        var want = (int)Math.Round((v.W(0x3A) + climb) / Math.Cos(Math.Max(0, (int)pitch) * Math.PI / 128));
        // A THRUST WISHED (DemoFollow: the route's own): at the height wished, flown at the route's throttle and lean it goes as the route's did - by the climb alone (gravity + 3) the bomber held 43 to the route's 48 and fell 250 blanks behind it
        if (w.Go && w.Thrust >= 0 && !grounded && Math.Abs(alt - y) <= 64) want = w.Thrust;
        want = Math.Clamp(want, 0, top);
        // A SOFT LANDING: coming down within two passes' drop (and the limit) of the ground, no less thrust than the least whose
        // pass's drop stays within +$78 - the demo's passes run 11 blanks: 6 a tick came down 61 a pass onto a hilltop, past the
        // helicopter's 28, and it was wrecked; a thrust step is a tick's fall, so one short while the pitch was put right was 35
        var lim = v.W(0x78);
        var landing = !grounded && climb < 0 && y - ground <= 2 * Math.Max(0, Drop(s, v, speed, ticks)) + lim;
        if (landing)
        {
            var least = 0;
            while (least < top && Drop(s, v, least, ticks) >= lim) least++;
            want = Math.Max(want, least);
        }
        var dSpeed = v.W(0x34) > 0 ? want - speed : 0;                       // no fuel, no throttle ($36A4: the fire is dropped
        // and the stick goes through - pressed up for more, it put the nose down a step a tick and dived it into the sea)
        var dPitch = tilt - pitch;
        var dTurn = (sbyte)(w.Heading - v.B[0x7E]);
        var steer = Math.Abs(dPitch) > 1 || (w.Go && !grounded && Math.Abs((int)dTurn) > 1);
        // STOPPING IN THE AIR: level first, above the landing band - a helicopter moves by its lean, and the throttle's turn
        // (the descent's, then the soft landing's one more step every pass) never let the pitch back: told to stop over its
        // landing spot it came down nose 5 down and drifted 1100 off it (the demo by its route, shell's four + finer ground)
        var levelFirst = !w.Go && !grounded && !landing && Math.Abs(dPitch) > 1;
        if (!levelFirst && (Math.Abs(dSpeed) >= 2 || (!steer && !w.Fire && dSpeed != 0) || grounded || landing && dSpeed > 0))
            return dSpeed > 0 ? new(true, false, false, false, true) : dSpeed < 0 ? new(false, true, false, false, true) : new(false, false, false, false, w.Fire);
        bool up = dPitch > 1, down = dPitch < -1;
        bool right = !grounded && dTurn > 1, left = !grounded && dTurn < -1;
        if (w.Fire && !(left || right)) up = down = false;                  // fire with the stick straight up / down would be the throttle
        if ((up || down) && !(left || right) && w.Fire) up = down = false;
        return new(up, down, left, right, w.Fire);
    }

    /// <summary>Turns in place: +$08 bit 13 (the tanks, the amphibian) and bit 10 (the boats) - MoveAndTurn turns at speed 0;
    /// the rest ($29F2) only while moving.</summary>
    public static bool TurnsStill(Entity v) => v.Bit(0x08, 13) || v.Bit(0x08, 10);   // the boats too ($29FA)

    /// <summary>A land vehicle's stick: turn toward the wished heading (a byte's 2 either side is near enough), the speed up
    /// to the top on a straight, half on a bend, a crawl (or, turning in place, nothing) on a sharp one; standing or holding:
    /// down to 0.</summary>
    static Joystick Drive(Entity v, Wish w)
    {
        int speed = v.W(0x3E), top = v.W(0x36);
        var diff = (sbyte)(w.Heading - v.B[0x7E]);
        bool turnR = false, turnL = false, up = false, down = false;
        if (w.Go || w.Hold)
        {
            var near = w.Hold ? 0 : 2;
            if (diff > near) turnR = true; else if (diff < -near) turnL = true;
        }
        int want;
        if (!w.Go) want = 0;
        else
        {
            var a = Math.Abs((int)diff);
            want = a < 16 ? top : a < 48 ? Math.Max(1, top / 2) : TurnsStill(v) ? 0 : Math.Min(2, top);
        }
        if (speed < want) up = true; else if (speed > want) down = true;
        if (w.Hold && !TurnsStill(v) && speed == 0 && (turnR || turnL)) up = true;   // a car turns only on the move: creep
        return new(up, down, turnL, turnR, w.Fire);
    }
}

/// <summary>The simplest brain: walk to a point on the map and stand there (within `near` units). Step 2 (navigation) grows
/// from it.</summary>
public sealed class WalkTo(int x, int z, int near = 64) : IBrain
{
    public Wish Decide(Play play)
    {
        var me = play.State.Entities.All[0];
        int mx = me.UW(0x4A), mz = me.UW(0x4E);
        if (Math.Max(Math.Abs(Senses.Diff(x, mx)), Math.Abs(Senses.Diff(z, mz))) <= near) return Wish.Stand;
        return new(true, Wish.Toward(mx, mz, x, z), false);
    }
}
