using System;
using System.Linq;
using Godot;

namespace Hunter;

/// <summary>STUNTIN's drawn car, measured every frame and logged every Every blanks (SCRTRACE ... metrics): how smooth the
/// body and the camera run (rms of their height's second difference, height units), each wheel's sudden moves against the
/// body (Jump model units or more in a frame), frames with a tyre more than 3 and 6 into the road, and the lift's range -
/// so a drawing shortcut shows in the numbers, not first on the screen.</summary>
public sealed class ScrMetrics(string who = "P", double jump = ScrMetrics.Jump)
{
    /// <summary>Every: blanks a line; Jump: a wheel's sudden move against the body (model units, the car racer's - a model drawn
    /// another size passes its own, so both are measured alike on screen).</summary>
    public const int Every = 500, Jump = 4;
    int _from = -1, _n, _camN;
    double _body1 = double.NaN, _body2 = double.NaN, _bodySq, _cam1 = double.NaN, _cam2 = double.NaN, _camSq;
    readonly int[] _wheelWas = new int[4], _jumps = new int[4];
    bool _haveWheels;
    int _in3, _in6, _liftLo = int.MaxValue, _liftHi = int.MinValue;
    /// <summary>Sparks thrown since the last line (every racer's).</summary>
    public int Sparks;

    /// <summary>One frame of the player's car: the body's drawn height, each wheel against the body, the tyres' gaps to the road,
    /// the lift.</summary>
    public void Car(int blank, double body, int[] wheels, double[] gaps, int lift)
    {
        if (_from < 0) _from = blank;
        if (!double.IsNaN(_body2)) { var j = body - 2 * _body1 + _body2; if (Math.Abs(j) < 200) _bodySq += j * j; }
        (_body2, _body1) = (_body1, body);
        if (_haveWheels) for (var w = 0; w < 4; w++) if (Math.Abs(wheels[w] - _wheelWas[w]) >= jump) _jumps[w]++;
        Array.Copy(wheels, _wheelWas, 4); _haveWheels = true;
        var low = gaps.Min();
        if (low < -3) _in3++;
        if (low < -6) _in6++;
        _liftLo = Math.Min(_liftLo, lift); _liftHi = Math.Max(_liftHi, lift);
        _n++;
        if (blank - _from >= Every) Flush(blank);
    }

    /// <summary>One frame of the camera (the chase or the cockpit, as drawn).</summary>
    public void Camera(double y)
    {
        if (!double.IsNaN(_cam2)) { var j = y - 2 * _cam1 + _cam2; if (Math.Abs(j) < 200) { _camSq += j * j; _camN++; } }
        (_cam2, _cam1) = (_cam1, y);
    }

    void Flush(int blank)
    {
        double Rms(double sq, int n) => n > 0 ? Math.Sqrt(sq / n) : 0;
        GD.Print($"SCRTRACE {blank} {who} metrics {_from}-{blank}: body jerk {Rms(_bodySq, _n):0.00} camera jerk {Rms(_camSq, _camN):0.00} | " +
                 $"wheel jumps FL {_jumps[0]} FR {_jumps[1]} RL {_jumps[2]} RR {_jumps[3]} | tyre in the road >3 {_in3} >6 {_in6} of {_n} | lift {_liftLo}..{_liftHi} | sparks {Sparks}");
        _from = blank; _n = _camN = 0; _bodySq = _camSq = 0; Array.Clear(_jumps); _in3 = _in6 = 0; _liftLo = int.MaxValue; _liftHi = int.MinValue; Sparks = 0;
    }
}
