using System;
using Hunter.Rules;

namespace Hunter;

/// <summary>NEW, NOT THE ORIGINAL (the showcase, recording only): cinematic camera shots on the card's free camera
/// (Real3DView.Free). Each frame Next(...) gives the camera for the shot playing; the rules never see it. Angles are
/// Real3DView's own (yaw 0 = north, positive left, rules/View3D.cs); the record's turn byte runs the other way round
/// (Controls.Apply $25F6: heading 0 walks north, growing east), so the subject's facing is -heading * 2pi / 256.</summary>
public sealed class Shots
{
    public enum Kind { Chase, FlyBy, Roadside, Orbit }

    public Kind Playing { get; private set; } = Kind.Chase;
    int frames;

    const int ShotFrames = 50 * 6;                                  // a shot plays 6 s at the game's 50 frames a second
    const double Tau = Math.PI * 2;

    // the camera eased toward its targets (world units, as View3D.Eye); a cut snaps once, then everything eases
    (double X, double Y, double Z) eye = (0, 0, 0);
    float camYaw, camPitch, camZoom = 1f;
    double follow;                                                  // CHASE: the camera's own eased turn, swinging behind with a lag
    double orbitAt;                                                 // ORBIT: the slow walk round the subject
    bool fresh = true;

    /// <summary>Start a shot (cut).</summary>
    public void Cut(Kind k) { Playing = k; frames = 0; fresh = true; }

    /// <summary>The camera this frame, from the subject's in-between place (world units, as View3D.Eye), its turn
    /// byte (0 = north, growing east) and the ground, which keeps every eye above the land it stands over.</summary>
    public FreeCamera Next(double x, double y, double z, int heading, Ground ground)
    {
        if (++frames > ShotFrames) Cut(Playing switch { Kind.Chase => Kind.FlyBy, Kind.FlyBy => Kind.Roadside, Kind.Roadside => Kind.Orbit, _ => Kind.Chase });
        var turn = -heading * Tau / 256;                            // the subject's facing, Real3DView's way round
        double ex, ey, ez, ly;                                      // this shot's standing place and look height
        float zoomT;                                                // the lens for this shot (1 = the game's own)
        switch (Playing)
        {
            case Kind.Chase:
            {
                follow = fresh ? turn : follow + Wrap(turn - follow) * 0.06;   // a cut starts behind; then it swings round with a lag
                ex = x + Math.Sin(follow) * 300;                    // behind the facing (the facing itself is (-sin, cos)); owner 2026-10-29: closer (520 -> 300, 150 -> 100)
                ez = z - Math.Cos(follow) * 300;
                ey = y + 100;
                ly = y + 70;
                zoomT = 1f;
                Ease(0.15, ex, ey, ez);
                break;
            }
            case Kind.FlyBy:
            case Kind.Roadside:                                     // still cameras: stand once at the cut, then only pan
            {
                var far = Playing == Kind.FlyBy ? 500 : 150;        // the fly-by parks far ahead on the path, the roadside near the side; owner 2026-10-29: closer (900 -> 500, 260/320 -> 150/160)
                var side = Playing == Kind.FlyBy ? 140 : 160;
                if (fresh)
                {
                    var st = Math.Sin(turn);
                    var ct = Math.Cos(turn);
                    ex = x - st * far - ct * side;                  // ahead on the path (the facing is (-sin, cos)), off to its left (-cos, -sin)
                    ez = z + ct * far - st * side;
                    eye = (ex, ground.Height((ushort)(int)ex, (ushort)(int)ez) + (Playing == Kind.FlyBy ? 90 : 40), ez);   // low by the road, higher for the fly-by
                }
                ly = y + (Playing == Kind.FlyBy ? 60 : 55);
                zoomT = Playing == Kind.FlyBy
                    ? 1f + 0.6f * (1f - (float)frames / ShotFrames)     // a long lens waiting, pulling back as it closes
                    : 1f + 0.8f * ((float)frames / ShotFrames);         // leaning in as it goes off down the road, to 1.8 (owner 2026-10-29)
                break;
            }
            default:                                                // ORBIT
            {
                orbitAt += Tau / (50 * 20);                         // a full circle in 20 s: 108 degrees inside one shot
                ex = x + Math.Sin(orbitAt) * 380;                   // owner 2026-10-29: closer (650 -> 380, 110 -> 80)
                ez = z + Math.Cos(orbitAt) * 380;
                ey = y + 80;
                ly = y + 60;
                zoomT = 1f;
                Ease(0.12, ex, ey, ez);
                break;
            }
        }
        eye.Y = Math.Max(eye.Y, ground.Height((ushort)(int)eye.X, (ushort)(int)eye.Z) + 30);   // never under the land
        var dx = x - eye.X;
        var dz = z - eye.Z;
        for (var i = 1; i < 8; i++)                                 // and the land between never hides him: a hill in the sight line
        {                                                           // lifts the eye till the line clears it (the film: a shot into a hillside)
            var t = i / 8.0;
            var hill = ground.Height((ushort)(int)(eye.X + dx * t), (ushort)(int)(eye.Z + dz * t)) + 20;
            if (eye.Y + (ly - eye.Y) * t < hill) eye.Y = (hill - ly * t) / (1 - t);
        }
        var dist = Math.Sqrt(dx * dx + dz * dz);
        var yawT = Math.Atan2(-dx, dz);                             // aim the lens at the subject (view.gdshaderinc view_from)
        var pitchT = Math.Atan((eye.Y - ly) / 2 / Math.Max(dist, 1));   // the tilt halves the heights, as the world node does
        var k = fresh ? 1f : 0.2f;
        camYaw += (float)Wrap(yawT - camYaw) * k;
        camPitch += (float)(pitchT - camPitch) * k;
        camZoom += (zoomT - camZoom) * (fresh ? 1f : 0.1f);
        fresh = false;
        return new FreeCamera(eye, camYaw, camPitch, camZoom);
    }

    /// <summary>The eye eases toward this shot's standing place (the chase's lag, the orbit's trailing).</summary>
    void Ease(double k, double ex, double ey, double ez)
    {
        if (fresh) { eye = (ex, ey, ez); return; }
        eye = (eye.X + (ex - eye.X) * k, eye.Y + (ey - eye.Y) * k, eye.Z + (ez - eye.Z) * k);
    }

    /// <summary>The short way round between two angles, -pi..pi.</summary>
    static double Wrap(double a) { a %= Tau; if (a > Math.PI) a -= Tau; else if (a < -Math.PI) a += Tau; return a; }
}
