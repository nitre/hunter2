namespace Hunter.Rules;

/// <summary>STUNTIN': the player's car on the road's face driven by Stunt Car Racer's own physics (ScrCar - the original's
/// passes, number for number). The car's record is moved by it instead of the game's own movement while he drives a car
/// (type $01) on a ground with a road (Ground.Road); anywhere else, and on foot, nothing here runs.
/// Pace: the original ran about 8 passes a second, Hunter's rules 50: one ScrCar pass every 50/8 of ours (Rate), the
/// record placed between the last two passes so the car glides (the passes themselves the original's, unchanged).
/// Units (ScrCar's report): x, z = cameraWorldX/Z, ground units x 4096; ground unit = ScrRoad.Scale world units, the grid
/// at ScrRoad.Offset. Height: cameraWorldY >> 8 = wheel units (ground units x 32); the body rests RestOffset wheel units
/// below the road it stands on. Angles: words, a turn $10000, yaw 0 toward +z and $4000 toward +x (as Hunter's heading);
/// pitch positive nose up (Hunter's +$7C is nose down). Contact points: wheelCorner X across (+ right), Y along, 16 to a
/// ground unit, in the ROAD's frame - turned by segmentTargetAngle (calculateTransformMatrices uses yaw - target), not the
/// car's yaw.</summary>
public sealed class ScrDrive
{
    /// <summary>Original passes a second against Hunter's 50.</summary>
    public const double Rate = 8.0 / 50;
    /// <summary>Wheel units the body rests below the road (measured: on a road of 5120 it rests at 4801).</summary>
    public const int RestOffset = 319;
    /// <summary>How far above the car a road still counts as the road under a contact point (world units): in the original
    /// there is always road under the wheels and a car meeting a ramp's face gets the spring's push; ours has roads over
    /// land and over each other, so a road this much above the body is still its road (Little Ramp's gap faces rise 480),
    /// and one higher overhead is an overpass to drive under.</summary>
    public const int Reach = 480;
    /// <summary>The boost's fuel before the car is put on a track (the track's own amount then: Race).</summary>
    public const int BoostFuel = 60;
    /// <summary>The league the race is in (0 the Standard League, 1 the Super League): the original's playerContextConfiguration
    /// rows - the engine's push (/usr/bin/bash0F0 / /usr/bin/bash140), the boost's burn rate (one unit every rate + 1 boosting passes: 0 / /usr/bin/bashC)
    /// and the damage allowance (trackWidthMultiplier 0 / 1) - and which of the track's two boost and damage values it takes.</summary>
    public static int League { get; set; }
    static readonly int[] Push = [0x00F0, 0x0140], BurnRate = [0x10, 0x0C], DamageAllowance = [0, 1];
    /// <summary>The boost fuel this car started the race with (its track's, for its player): the panel's full gauge.</summary>
    public int MaxBoost { get; private set; } = BoostFuel;

    // THE RACE (the original's checkLapCompletion / checkRaceCompletion 7110-7228, updateLapTimer 7270)
    /// <summary>The lap counter: 0 before the lap line, 1 on the first lap ... ScrRoad.Laps + 1 when the race is done.</summary>
    public int Lap { get; private set; }
    /// <summary>The lap line was crossed and the finish piece not reached since (lapCrossingDetectionFlag's bit 7): the
    /// line does not count again until it is - no laps from rocking over it.</summary>
    bool _lapCrossed;
    /// <summary>Hunter passes into this lap (50 a second); the last lap's and the best (passes, -1 = none yet).</summary>
    public int LapPasses { get; private set; }
    public int LastLap { get; private set; } = -1;
    public int BestLap { get; private set; } = -1;
    /// <summary>Passes the lap just done stays on the timer (the original's lapTimeDisplayDuration, $1B of its frames).</summary>
    public int ShowLast { get; private set; }
    public const int ShowLastFor = 150;
    /// <summary>The pass the car finished the race on (s.BlankCount), -1 while racing.</summary>
    public long FinishedAt { get; private set; } = -1;
    /// <summary>A lap was counted this pass (the new lap number), 0 otherwise: the messages.</summary>
    public int LapJustDone { get; private set; }
    ScrRoad? _raced;

    /// <summary>The car put in the race on this road: the league's values (League) - the track's boost fuel and damage frame
    /// limit for the league (the original's 6788 / 12559 take the track's second values in the Super League), the engine's
    /// push, the boost's burn rate, the damage allowance - the laps and times cleared. Every car the same: the original's
    /// opponent rode the track with neither.</summary>
    void Race(ScrRoad road)
    {
        _raced = road;
        var l = Math.Clamp(League, 0, 1);
        MaxBoost = road.Boost[l];
        Car.B(ScrCar.boostFuelLevel, MaxBoost);
        Car.B(ScrCar.damageFrameLimit, road.Damage[l]);
        Car.B(ScrCar.gasInputAccumulatorValue, Push[l] >> 8); Car.B(ScrCar.gasInputIntensityValue, Push[l] & 0xFF);
        Car.B(ScrCar.fuelConsumptionRate, BurnRate[l]);
        Car.B(ScrCar.trackWidthMultiplier, DamageAllowance[l]);
        Lap = 0; _lapCrossed = false; LapPasses = 0; LastLap = BestLap = -1; ShowLast = 0; FinishedAt = -1;
    }

    /// <summary>How far the car is into the race (world units: ScrRoad.Progress), NaN before its first pass.</summary>
    public double Progress(ScrRoad road) =>
        Entity is { } e && _lastStrip >= 0 ? road.Progress(Lap, _lastStrip, e.UW(0x4A), e.UW(0x4E)) : double.NaN;
    /// <summary>Its place across the road (0 the left edge .. 1 the right), NaN before its first pass.</summary>
    public double Across(ScrRoad road) => Entity is { } e && _lastStrip >= 0 ? road.Across(_lastStrip, e.UW(0x4A), e.UW(0x4E)) : double.NaN;
    /// <summary>Original passes run so far (the drivers' once-a-pass steps).</summary>
    public long Passes { get; private set; }
    /// <summary>Passes spent in another car's slipstream (a probe's count).</summary>
    public int DraftPasses { get; private set; }

    /// <summary>THE SLIPSTREAM (the original's handleOpponentPositioning 12287-12313 setting draftingTimer, calculatePlayerDistance
    /// its opponentAheadFlag - set when the opponent is BEHIND - and the physics' drag 11576 / the opponent's speed 12585): a
    /// car close behind another - within 64 of the original's units round the lap (opponentDistance under $40), scaled to
    /// ours - loses less to the air: its drag less $14 x 128 (ScrCar's drag step). The player only in the other's line
    /// (under $32 of 255 across the road apart); a computer car whenever it is that close behind. Set before each pass.</summary>
    void Slipstream(WorldState s, ScrRoad road)
    {
        var draft = false;
        var p = Progress(road);
        if (!double.IsNaN(p))
        {
            var reach = 64.0 * road.LapLength / road.LapUnits;
            var player = this == s.ScrDrive;
            foreach (var o in ScrRace.Drives(s))
            {
                if (o == this || o.Wrecked) continue;
                var gap = o.Progress(road) - p;
                if (!(gap > 0 && gap < reach)) continue;
                if (player && Math.Abs(o.Across(road) - Across(road)) >= 0x32 / 255.0) continue;
                draft = true;
                break;
            }
        }
        if (draft) DraftPasses++;
        Car.B(ScrCar.draftingTimer, draft ? 0xFF : 0);
        Car.B(ScrCar.opponentAheadFlag, draft ? 0 : 0x80);                   // clear: the car in front is ahead (the drag's test)
    }

    /// <summary>The player's lap on the message line (the original's displayGameMessage): the next lap, the last one, and at
    /// the end the race won - first over the line after the last lap - or lost.</summary>
    void Announce(WorldState s)
    {
        string text;
        if (LapJustDone > ScrRoad.Laps)
        {
            var beaten = s.ScrAi.Values.Any(a => a.Drive.FinishedAt >= 0 && a.Drive.FinishedAt < FinishedAt);
            text = beaten ? "Race lost" : "Race won";
        }
        else text = LapJustDone == ScrRoad.Laps ? "Final lap" : $"Lap {LapJustDone} of {ScrRoad.Laps}";
        s.Panel.Text.Print(System.Text.Encoding.ASCII.GetBytes("]" + text));
    }

    /// <summary>Once a pass, from the piece the car was last on: past the finish piece the line is armed again; on the lap
    /// line, armed, a lap is counted - the first starts the race, each after it ends a lap (its time kept, the best), the
    /// last ends the race. The lap's time runs from the line (updateLapTimer) and stops at the race's end.</summary>
    void CountLap(WorldState s, ScrRoad road)
    {
        LapJustDone = 0;
        if (ShowLast > 0) ShowLast--;
        if (_lastStrip < 0) return;
        var piece = road.Piece[_lastStrip];
        if (_lapCrossed) { if (piece == road.Finish) _lapCrossed = false; }
        else if (piece == road.LapLine && Lap <= ScrRoad.Laps)
        {
            _lapCrossed = true;
            Lap++;
            LapJustDone = Lap;
            if (Lap > 1)
            {
                LastLap = LapPasses;
                if (BestLap < 0 || LapPasses < BestLap) BestLap = LapPasses;
                ShowLast = ShowLastFor;
            }
            LapPasses = 0;
            if (Lap > ScrRoad.Laps) FinishedAt = s.BlankCount;
        }
        if (Lap >= 1 && Lap <= ScrRoad.Laps) LapPasses++;
    }

    public readonly ScrCar Car = new();
    /// <summary>The road's height under the three contact points at the last original pass (wheel units: front left,
    /// front right, rear) - what the physics was given.</summary>
    public (int FL, int FR, int Rear) LastHeights { get; private set; }    public Entity? Entity { get; private set; }
    double _acc;
    byte _steer;                                                                 // the drawn steering: SteerPasses' mean, sixteenths of full lock
    /// <summary>The drawn steering wheel's span: the stick's on / off of the last passes averaged (a feathered stick holds a
    /// share of SteerPasses), so the wheel stands where the share is instead of flicking lock to lock each pass.</summary>
    const int SteerPasses = 8;
    readonly sbyte[] _steerRing = new sbyte[SteerPasses];
    int _steerAt;
    int _lastStrip = -1, _off;
    /// <summary>Passes off the road before the restart: the original's $10 processed frames, about 2 seconds.</summary>
    public const int RestartAfter = 100;
    /// <summary>A car on the road that gets no further round the lap for StuckAfter Hunter passes (5 seconds) is stuck.</summary>
    const int StuckAfter = 250;
    int _still, _furthest = -1;
    /// <summary>How far into its piece a restarted car is put (world units: about the car's length).</summary>
    const double RestartInto = 120;
    /// <summary>A restart this close (world units) to another racing car takes the far side of the road from it.</summary>
    const double RestartClear = 600;
    /// <summary>The run-up a restart leaves before the next jump (strips): from still, a car needs it to reach a jump's speed.</summary>
    public const int RunUp = 40;
    /// <summary>Restarts so far (a probe's count).</summary>
    public int Restarts { get; private set; }
    /// <summary>The wreck's value of the shown damage (the original's updateDamageBarHUD: $F0 of 255).</summary>
    public const int WreckAt = 0xF0;
    /// <summary>The shown damage (the original's accumulatedCarDamage: climbs to the target the same pass).</summary>
    public int Damage { get; private set; }
    /// <summary>The road section the car was last on (its place round the lap; -1 before the first pass).</summary>
    public int Section => _lastStrip;
    /// <summary>Wrecks so far (a probe's count).</summary>
    public int Wrecks { get; private set; }
    /// <summary>Hunter passes left with the car in pieces before the restart; 0 = not wrecked.</summary>
    int _wreck;
    /// <summary>The car is in pieces (hidden, waiting for the restart): it touches nothing and nobody races it.</summary>
    public bool Wrecked => _wreck > 0;
    /// <summary>Where the record is kept while the car is in pieces (out of sight under the world).</summary>
    const int HiddenY = -4000;

    /// <summary>THE SOUND, every car's (the original had only the player's - its opponent was silent): an effect started
    /// for this car (the original's sample number 0-6, its volume 0-64, its period - the table's, or the one the original
    /// wrote into the table for it - and the original's channel, the table's +$C).</summary>
    public static event Action<Entity, int, int, int, int>? Sound;
    /// <summary>A car's engine every one of Hunter's passes (the original's vertical blank): which of its eight versions
    /// (generateEngineSamples), its volume and its period (updateEngineAudioPitch / the blank's own step).</summary>
    public static event Action<Entity, int, int, int>? Engine;
    /// <summary>Where a car is heard from, every pass: its level 0-1 by Hunter's own distance law (SoundVolume $2DA6,
    /// as every one of the game's sounds) and its pan -1..1 by where it is on the screen (sound_position's: the Persp
    /// screen x of its camera-relative place, centre when behind the camera).</summary>
    public static event Action<Entity, float, float>? Heard;
    /// <summary>Where the cars are heard from when the view is not the game's own camera (the chase camera, GameView): its
    /// spot (world units) and facing (radians, the facing (-sin, cos)); null = the game's camera (+$56 / +$5A).</summary>
    public static (double X, double Z, double Yaw)? Listener;
    readonly Random _noise = new(1);                                    // the ground grind's pitch wobble (sound only)

    /// <summary>The car back on the road: the last piece it was on, stepped back while that piece is a corner or one of the
    /// track's listed feature pieces (as the original's initializePlayerAtSegment) or one of its jumps' takeoff pieces, and on
    /// back till RunUp strips before the next jump (from still a car cannot clear a jump without a run-up), at its first
    /// cross-section's middle, pointing round the lap, still.</summary>
    void Restart(WorldState s, Entity e)
    {
        var surface = s.Ground.Road!;
        var road = surface.Road;
        if (_lastStrip < 0) _lastStrip = road.SpawnSection;
        var piece = road.Piece[_lastStrip];
        bool Jump(int p) => road.Obstacles.Any(o => o.Piece == p);
        int ToJump(int p)                                                    // strips from piece p's start to the next jump's
        {
            var at = road.Piece.ToList().IndexOf(p);
            for (var k = 0; k < RunUp; k++) if (Jump(road.Piece[(at + k) % road.Sections.Count])) return k;
            return RunUp;
        }
        for (var k = 0; k < road.Steer.Count && ((road.Steer[piece].Flags & 0x80) != 0 || road.Features.Contains(piece)
                 || Jump(piece) || ToJump(piece) < RunUp); k++)
            piece = (piece - 1 + road.Steer.Count) % road.Steer.Count;      // past corners, the track's feature pieces and its jumps,
                                                                            // and back till RunUp strips before the next jump
        var sec = road.Piece.ToList().IndexOf(piece);
        // ...a car's length into the piece (its rear contact point on it too): at its very first cross-section, on a jump's
        // landing that starts with a step up, the rear hung over the gap behind and the car tipped back down into it, unable to
        // climb out
        for (double run = 0; run < RestartInto && road.Piece[(sec + 1) % road.Sections.Count] == piece; sec = (sec + 1) % road.Sections.Count)
        {
            var (a, b) = (road.Sections[sec], road.Sections[(sec + 1) % road.Sections.Count]);
            run += Math.Sqrt(Math.Pow((b.LX + b.RX - a.LX - a.RX) / 2, 2) + Math.Pow((b.LZ + b.RZ - a.LZ - a.RZ) / 2, 2));
        }
        var c = road.Sections[sec];
        // the middle, or the far side of the road from another racing car within RestartClear
        var f = 0.5;
        foreach (var o in ScrRace.Drives(s))
        {
            if (o == this || o.Entity is not { } oe) continue;
            double mx = (c.LX + c.RX) / 2 - oe.UW(0x4A), mz = (c.LZ + c.RZ) / 2 - oe.UW(0x4E);
            if (mx * mx + mz * mz < RestartClear * RestartClear) f = road.Across(sec, oe.UW(0x4A), oe.UW(0x4E)) < 0.5 ? 0.75 : 0.25;
        }
        int x = (int)Math.Round(c.LX + (c.RX - c.LX) * f), z = (int)Math.Round(c.LZ + (c.RZ - c.LZ) * f);
        e.SetW(0x4A, x); e.SetW(0x4E, z); e.SetW(0x4C, s.Ground.SurfaceOnFace((ushort)x, (ushort)z, short.MaxValue));
        e.SetW(0x7E, surface.Way(sec)); e.HeadingFine = e.UW(0x7E);
        if (s.Planet is { } planet && e == s.Entities.All[0]) planet.Anchor();   // put there by hand: not a walk past an edge (a
                                                                                // jump of over half a face read as one, turned back)
        Bind(s, e);
        _lastStrip = sec; _off = 0;
        foreach (var a in new[] { ScrCar.accumulatedForceFrontLeft, ScrCar.accumulatedForceFrontRight, ScrCar.accumulatedForceRear, ScrCar.damageAccumulationActive })
            Car.B(a, 0);                                                     // a fresh car: no damage carried
        Car.B(ScrCar.holeRenderingPosition, 0x0A); Car.B(ScrCar.majorImpactCooldownTimer, 0);
        Damage = 0;
        Restarts++;
        // the crane: held up on its chains, still, then let go
        var road0 = s.Ground.SurfaceOnFace(e.UW(0x4A), e.UW(0x4E), e.W(0x4C));
        Car.L(ScrCar.cameraWorldY, (Wheel(road0 + HangLift) - RestOffset) << 8);
        _now = _was = Place();
        _nowEye = _wasEye = (Car.L(ScrCar.cameraWorldY) >> 8) * ScrRoad.Scale / 32;
        _hang = HangPasses;
        _chainRise = -1;
        _hungAt = (_now.X, _now.Y, _now.Z, _now.Yaw);
    }

    // THE CRANE (basic, after the original's restart and updateChainAnimation): the restarted car hangs on chains HangLift
    // above the road for HangPasses, still; then the physics drops it and the chains are pulled up, faster each pass (the
    // original's lift: 8 more each frame), until ChainLength has gone up out of sight.
    /// <summary>Height units the car hangs above the road, Hunter passes it hangs.</summary>
    public const int HangLift = 60, HangPasses = 75;
    /// <summary>The chains' length above the car (height units) and their lift's gain a pass (height units a pass, a pass).</summary>
    public const double ChainLength = 1500, ChainGain = 1.5;
    int _hang;
    /// <summary>How far the chains have been pulled up since the drop (height units); -1 = still holding the car.</summary>
    double _chainRise = -1, _chainSpeed;
    (double X, double Y, double Z, int Yaw) _hungAt;
    /// <summary>The car is on the crane (held still above the road).</summary>
    public bool Hanging => _hang > 0;
    /// <summary>The crane's chains: the place the car hangs or hung (its record's place and heading) and how far they have
    /// been pulled up from it (0 while holding it); null = no chains.</summary>
    public (double X, double Y, double Z, int Yaw, double Rise)? Chains =>
        _hang > 0 ? (_hungAt.X, _hungAt.Y, _hungAt.Z, _hungAt.Yaw, 0)
        : _chainRise >= 0 ? (_hungAt.X, _hungAt.Y, _hungAt.Z, _hungAt.Yaw, _chainRise) : null;

    /// <summary>One pass on the crane: the record held at the hang, the lap's time running; at the end the car let go (no
    /// speed, level) and the chains' lift begun.</summary>
    void HangPass(WorldState s, Entity e)
    {
        _hungAt = (_now.X, _now.Y, _now.Z, _now.Yaw);
        e.SetW(0x50, e.W(0x4A)); e.SetW(0x52, e.W(0x4C)); e.SetW(0x54, e.W(0x4E));
        e.SetW(0x4A, (int)Math.Round(_now.X)); e.SetW(0x4C, (int)Math.Round(_now.Y)); e.SetW(0x4E, (int)Math.Round(_now.Z));
        e.SetW(0x74, e.W(0x4C));
        e.SetW(0x7E, _now.Yaw); e.HeadingFine = e.UW(0x7E);
        e.B[0x7C] = 0; e.B[0x80] = 0; e.PitchFine = 0; e.RollFine = 0; e.B[0x18] = 0; e.SetW(0x3E, 0);
        ViewAngles = (0, 0);
        EyeHeight = _nowEye;
        if (Lap >= 1 && Lap <= ScrRoad.Laps) LapPasses++;
        if (--_hang > 0) return;
        foreach (var a in new[] { ScrCar.worldXSpeed, ScrCar.worldYSpeed, ScrCar.worldZSpeed, ScrCar.rotationSpeedX, ScrCar.rotationSpeedY, ScrCar.rotationSpeedZ })
            Car.W(a, 0);
        Car.W(ScrCar.cameraAngleX, 0); Car.W(ScrCar.cameraAngleZ, 0);
        _acc = 1 - Rate;                                                     // the next Hunter pass runs a physics pass: the drop at once
        _chainRise = 0; _chainSpeed = 0;
    }

    /// <summary>The chains' lift after the drop, once a Hunter pass.</summary>
    void ChainPass()
    {
        if (_chainRise < 0) return;
        _chainSpeed += ChainGain;
        _chainRise += _chainSpeed;
        if (_chainRise >= ChainLength) _chainRise = -1;
    }
    long _lastBlank = long.MinValue / 2;
    (double X, double Y, double Z, int Yaw, int Pitch, int Roll) _was, _now;

    /// <summary>The original's starting physics memory (game_data/scr/car_start.bin, from the original disk - the state the
    /// emulator proof began from); null when the file is not there.</summary>
    public static ScrDrive? Load(string dataDir)
    {
        var path = Path.Combine(dataDir, "scr", "car_start.bin");
        if (!File.Exists(path)) return null;
        var f = File.ReadAllBytes(path);
        var d = new ScrDrive();
        int lo = f[0] << 24 | f[1] << 16 | f[2] << 8 | f[3], n = f[4] << 8 | f[5];
        Array.Copy(f, 6, d.Car.Ram, lo, n);
        for (var k = 6 + n; k + 5 <= f.Length; k += 5) d.Car.Ram[f[k] << 24 | f[k + 1] << 16 | f[k + 2] << 8 | f[k + 3]] = f[k + 4];
        d.Car.B(ScrCar.boostFuelLevel, BoostFuel);
        d.Car.B(ScrCar.fuelConsumptionRate, BurnRate[0]);
        d.Car.FuelTick += () => d.Car.B(ScrCar.boostFuelLevel, Math.Max(0, d.Car.B(ScrCar.boostFuelLevel) - 1));
        d.Car.B(ScrCar.holeRenderingPosition, 0x0A);                       // a new car's ten holes left (as a new season, 4221)
        d.Car.Sample += d.Play;
        return d;
    }

    /// <summary>Whether this record is driven here: the player's own car on a ground with a road.</summary>
    public static bool Takes(WorldState s, Entity e, int slot) =>
        s.Ground.Road is not null && (slot == 0 && s.Players == 1 && s.ScrDrive is not null && e.W(4) == 1 || s.ScrAi.ContainsKey(e));

    /// <summary>One of Hunter's passes for a car this drives: the player's (his stick, or the autopilot's) or an AI car's
    /// (its driver's).</summary>
    public static void Drive(WorldState s, Entity e, int slot)
    {
        ScrRace.Collide(s);                                                  // once a pass, before the first car moves
        if (s.ScrAi.TryGetValue(e, out var ai))
        {
            var (d6, fire) = ai.Brain.Decide(s, e, ai.Drive, ScrRace.Others(s, e), opponent: true);
            ai.Drive.Pass(s, e, d6, fire);
            return;
        }
        var me = s.ScrDrive!;
        if (s.ScrAutopilot is { } pilot)
        {
            var (d6, fire) = pilot.Decide(s, e, me, ScrRace.Others(s, e));
            me.Pass(s, e, d6, fire);
        }
        else me.Pass(s, e, s.StickD6, (s.StickD4 & 1) != 0, s.AnalogLean);   // STICK STEERING: the pad's lean (0 off)
    }

    static double ToGround(double world) => (world - ScrRoad.Offset) / ScrRoad.Scale;
    static double ToWorld(double ground) => ScrRoad.Offset + ground * ScrRoad.Scale;
    static int Wheel(int worldHeight) => (int)Math.Round(worldHeight / ScrRoad.Scale * 32);

    /// <summary>The car put at the record's place and heading, standing on the surface there, still (a new boarding).</summary>
    void Bind(WorldState s, Entity e)
    {
        Entity = e;
        var c = Car;
        c.L(ScrCar.cameraWorldX, (int)Math.Round(ToGround(e.UW(0x4A)) * 4096));
        c.L(ScrCar.cameraWorldZ, (int)Math.Round(ToGround(e.UW(0x4E)) * 4096));
        var h = s.Ground.SurfaceOnFace(e.UW(0x4A), e.UW(0x4E), e.W(0x4C));
        c.L(ScrCar.cameraWorldY, (Wheel(h) - RestOffset) << 8);
        c.W(ScrCar.cameraAngleY, e.UW(0x7E)); c.W(ScrCar.cameraAngleX, 0); c.W(ScrCar.cameraAngleZ, 0);
        foreach (var a in new[] { ScrCar.worldXSpeed, ScrCar.worldYSpeed, ScrCar.worldZSpeed, ScrCar.rotationSpeedX, ScrCar.rotationSpeedY, ScrCar.rotationSpeedZ })
            c.W(a, 0);
        _now = _was = Place();
        _acc = 0;
    }

    /// <summary>Where the record goes: the car drawn standing on its WHEELS, at wheelHeight (wheel units: the road under
    /// the contact points plus any hang - Pass). The original's height is the body's (its camera) and its springs move
    /// the body over wheels that stay on the road; Hunter's car is one rigid model, so it goes where the wheels are.
    /// Each wheel's travel is road - (body + its tilt) - trackOffsetAdjustment (updateWheels), the tilts cancelling in
    /// the average ((front pair) / 2 + rear) / 2. Null (a boarding, a restart - before any pass): the body's measured
    /// rest above the road.</summary>
    (double X, double Y, double Z, int Yaw, int Pitch, int Roll) Place(double? wheelHeight = null)
    {
        var c = Car;
        var h = wheelHeight ?? (c.L(ScrCar.cameraWorldY) >> 8) + RestOffset;
        return (ToWorld(c.L(ScrCar.cameraWorldX) / 4096.0), h * ScrRoad.Scale / 32,
                ToWorld(c.L(ScrCar.cameraWorldZ) / 4096.0), (ushort)c.W(ScrCar.cameraAngleY), c.W(ScrCar.cameraAngleX), c.W(ScrCar.cameraAngleZ));
    }

    static double Mean((int FL, int FR, int Rear) h) => ((h.FL + h.FR) / 2.0 + h.Rear) / 2;

    /// <summary>The road's height (wheel units) under the three contact points with the body at ground (gx, gz). The
    /// offsets are in the ROAD's frame: calculateTransformMatrices turns them by cameraAngleY - segmentTargetAngle, so X
    /// is across and Y along the target's direction (on a reversed piece half a turn round). edge: set when a point is
    /// off the road's strips.</summary>
    (int FL, int FR, int Rear) Contacts(WorldState s, double gx, double gz, int y, ref bool edge)
    {
        var c = Car;
        var road = s.Ground.Road!;
        double a = (ushort)c.W(ScrCar.segmentTargetAngle) / 65536.0 * 2 * Math.PI, sn = Math.Sin(a), cs = Math.Cos(a);
        var off = false;
        int H(int cx, int cy)
        {
            double ax = cx / 16.0, ay = cy / 16.0;                                  // across (+ right), forward
            double wx = ToWorld(gx + ax * cs + ay * sn), wz = ToWorld(gz - ax * sn + ay * cs);
            if (road.StripAt((int)Math.Round(wx), (int)Math.Round(wz), y, Reach) < 0) off = true;
            return Wheel(s.Ground.SurfaceOnFace((ushort)Math.Clamp((int)Math.Round(wx), 0, 65535), (ushort)Math.Clamp((int)Math.Round(wz), 0, 65535), y, Reach));
        }
        var h = (H(c.W(ScrCar.wheelCornerXFrontLeft), c.W(ScrCar.wheelCornerYFrontLeft)),
                 H(c.W(ScrCar.wheelCornerXFrontRight), c.W(ScrCar.wheelCornerYFrontRight)),
                 H(c.W(ScrCar.wheelCornerXRearCenter), c.W(ScrCar.wheelCornerYRearCenter)));
        edge |= off;
        return h;
    }

    /// <summary>One of Hunter's passes: the stick to the original's input flags, an original pass when one is due (the road's
    /// height under the three contact points; on the road its direction as the steering's target, else the car's own), then
    /// the record placed between the last two passes. stickD6: down 1, up 2, right 4, left 8; fire: boost.</summary>
    /// <summary>STICK STEERING (WorldState.AnalogSteering): the original's steering is on or off, so a stick's lean (-16..16,
    /// + right) holds it for that share of the physics passes, spread evenly (a running count: half a lean every other pass).</summary>
    double _leanAcc;

    /// <summary>One physics pass's stick under a lean: the left / right bits replaced by the lean's share of the passes (acc carries
    /// the running count between passes); no lean leaves the stick as it is.</summary>
    public static byte Feather(ref double acc, byte stickD6, int lean)
    {
        if (lean == 0) { acc = 0; return stickD6; }
        acc += Math.Min(16, Math.Abs(lean)) / 16.0;
        var steer = acc >= 1;
        if (steer) acc -= 1;
        return (byte)((stickD6 & ~0xC) | (steer ? (lean > 0 ? 4 : 8) : 0));
    }

    public void Pass(WorldState s, Entity e, byte stickD6, bool fire, int lean = 0)
    {
        if (Entity != e || s.BlankCount - _lastBlank > 2) Bind(s, e);         // a new boarding (or the record's first drive)
        _lastBlank = s.BlankCount;
        if (_raced != s.Ground.Road!.Road) Race(s.Ground.Road.Road);
        var c = Car;
        Hear(s, e);
        EngineBlank();
        if (_wreck > 0)
        {                                                                        // in pieces: the record out of sight, then the restart
            e.SetW(0x4C, HiddenY); e.SetW(0x74, HiddenY); e.SetW(0x3E, 0);
            if (Lap >= 1 && Lap <= ScrRoad.Laps) LapPasses++;                  // the lap's time runs on through the wreck
            if (--_wreck == 0) Restart(s, e);
            return;
        }
        if (_hang > 0) { HangPass(s, e); return; }
        ChainPass();
        _acc += Rate;
        while (_acc >= 1)
        {
            _acc -= 1;
            stickD6 = Feather(ref _leanAcc, stickD6, lean);
            var flags = ((stickD6 & 2) != 0 ? 1 : (stickD6 & 1) != 0 ? 2 : 0) | ((stickD6 & 8) != 0 ? 4 : (stickD6 & 4) != 0 ? 8 : 0) | (fire ? 0x10 : 0);
            c.Input(flags);
            // the steering wheel drawn (the record's steering byte, as Hunter's own cars: + turns the heading up): measured on both
            // cars, the stick's 8 turns the heading up, its 4 down
            _steerRing[_steerAt] = (sbyte)((flags & 0xC) == 8 ? 1 : (flags & 0xC) == 4 ? -1 : 0);
            _steerAt = (_steerAt + 1) % SteerPasses;
            var sum = 0; foreach (var v in _steerRing) sum += v;
            _steer = (byte)(sbyte)(sum * 16 / SteerPasses);                    // -16..16 (place.gdshaderinc: sixteenths on the racers)
            var gx = c.L(ScrCar.cameraWorldX) / 4096.0; var gz = c.L(ScrCar.cameraWorldZ) / 4096.0;
            var y = (int)Math.Round(_now.Y);
            var road = s.Ground.Road!;
            var yaw = (ushort)c.W(ScrCar.cameraAngleY);
            var strip = road.StripAt((int)ToWorld(gx), (int)ToWorld(gz), y, Reach);
            if (strip >= 0)
            {                                                                           // on the road: the piece's own values, as
                var (pf, bas, inv, rev) = road.Road.Steer[road.Road.Piece[strip]];     // loadTrackSegmentConfiguration sets them
                c.B(ScrCar.segmentSteeringFlags, pf); c.B(ScrCar.segmentBaseSteeringOffset, bas);
                c.B(ScrCar.trackDirectionInversionFlag, inv); c.B(ScrCar.reverseDirectionFlag, rev);
                // the target: the steering takes (target - yaw) EOR reverseDirectionFlag's word - so the target is put where
                // that comes out as the road's own way round the lap here
                var r = (ushort)c.W(ScrCar.reverseDirectionFlag);
                c.W(ScrCar.segmentTargetAngle, (ushort)((((ushort)(road.Way(strip) - yaw)) ^ r) + yaw));
            }
            else
            {                                                                           // off the road: a straight, no pull
                c.B(ScrCar.segmentSteeringFlags, 0); c.B(ScrCar.segmentBaseSteeringOffset, 0);
                c.B(ScrCar.trackDirectionInversionFlag, 0); c.B(ScrCar.reverseDirectionFlag, 0);
                c.W(ScrCar.segmentTargetAngle, yaw);
            }
            var edge = false;                                                      // a contact point off the road (the grind)
            Slipstream(s, road.Road);
            Passes++;
            c.Physics(car =>
            {
                LastHeights = Contacts(s, gx, gz, y, ref edge);
                return LastHeights;
            });
            // the drawn car on its wheels, on the road where the car is NOW: the physics measured the road where the pass
            // began and then moved the car, so its travels are a pass behind (on a climb at speed a pass's rise under the
            // road, on a descent as much over it). The wheels reach down at most to the springs' full stretch (travel 0:
            // body + adjustment); a road further down - off a ramp's lip - leaves them hanging there, the car in the air
            var ignore = false;
            var under = Mean(Contacts(s, c.L(ScrCar.cameraWorldX) / 4096.0, c.L(ScrCar.cameraWorldZ) / 4096.0, y, ref ignore));
            var stretch = (c.L(ScrCar.cameraWorldY) >> 8) + c.L(ScrCar.trackOffsetAdjustment);
            _was = _now;
            _now = Place(Math.Max(under, stretch));
            _wasEye = _nowEye;
            _nowEye = (c.L(ScrCar.cameraWorldY) >> 8) * ScrRoad.Scale / 32;   // the body: the original's own eye
            EnginePitch(flags);
            Grind(strip < 0, edge);
            if (DamagePass()) { Wreck(e); return; }
        }
        // THE RESTART (the original's continueGameLoop / initializePlayerAtSegment, docs/scr-physics.md): off the road for
        // RestartAfter passes (its $10 processed frames, 2 s), or in the sea, the car is put back on the last piece it was
        // on - stepped back past corners - at that piece's start, in the middle, pointing round the lap, still
        var here = s.Ground.Road!.StripAt((int)Math.Round(_now.X), (int)Math.Round(_now.Z), (int)Math.Round(_now.Y), Reach);
        // STUCK: on the road but no strip further round the lap for StuckAfter passes (rocking in a jump's gap, which is not
        // marked: a car in it counts as on the road), the car is restarted too
        if (here >= 0)
        {
            var n = s.Ground.Road!.Road.Sections.Count;
            var on = _furthest < 0 ? 1 : (here - _furthest + n) % n;
            if (on > 0 && on < n / 2) { _furthest = here; _still = 0; } else _still++;
        }
        if (here >= 0) { _lastStrip = here; _off = 0; }
        if (here < 0 && (++_off >= RestartAfter || _now.Y < 8) || _still >= StuckAfter) { _still = 0; _furthest = -1; Restart(s, e); }
        CountLap(s, s.Ground.Road!.Road);
        if (this == s.ScrDrive && LapJustDone > 1) Announce(s);
        var f = Math.Clamp(_acc, 0, 1);                                          // the share of the way to the next pass
        double L(double p, double q) => p + (q - p) * f;
        int A(int p, int q) => p + (int)Math.Round((short)(q - p) * f);
        var px = L(_was.X, _now.X); var py = L(_was.Y, _now.Y); var pz = L(_was.Z, _now.Z);
        e.SetW(0x50, e.W(0x4A)); e.SetW(0x52, e.W(0x4C)); e.SetW(0x54, e.W(0x4E));   // where it was (the game's own old place words)
        e.SetW(0x4A, (int)Math.Round(px)); e.SetW(0x4C, (int)Math.Round(py)); e.SetW(0x4E, (int)Math.Round(pz));
        e.SetW(0x7E, A(_was.Yaw, _now.Yaw)); e.HeadingFine = e.UW(0x7E);
        e.B[0x18] = _steer;                                                      // every frame (Hunter's controls clear the player's)
        // pitch and roll as fine words (Entity.PitchFine / RollFine, drawn when FINE ANGLES is on) with the bytes their whole
        // steps, so the card turns the car - and the in-car eye with it - smoothly instead of a byte (1.4 degrees) at a time
        var pitchWord = (ushort)-A(_was.Pitch, _now.Pitch);                    // Hunter's pitch: nose down positive
        var rollWord = (ushort)A(_was.Roll, _now.Roll);
        e.B[0x7C] = (byte)(pitchWord >> 8); e.PitchFine = pitchWord;
        e.B[0x80] = (byte)(rollWord >> 8); e.RollFine = rollWord;
        ViewAngles = (A(_was.Pitch, _now.Pitch), A(_was.Roll, _now.Roll));
        EyeHeight = L(_wasEye, _nowEye);
        e.SetW(0x74, e.W(0x4C));                                                 // stands on what it rests on (the card's fit: ground_contact)
        e.SetW(0x3E, Math.Min(32767, Math.Abs((int)c.W(ScrCar.carLocalVelocityZ)) >> 8));   // a speed for the wheels' spin and the sound
    }

    /// <summary>The original's damage step (updateDamageAndTimers 7318 / updateDamageBarHUD 15054), after each pass: when a
    /// corner took a hit this pass the target is ((front left + front right) / 2 + rear) / 2 (9-bit sums, as its ROXRs),
    /// the shown damage climbs to it at once (its loop), and reaching WreckAt wrecks. Then the sounds: a hit of $1400 or
    /// more with holes left makes a new hole and the major impact (its cooldown $45 passes - the original's, the source
    /// has $FF counted in blanks), any other hit the creak, louder with the hit (and the pass after a new hole creaks too).
    /// The hit flag is cleared after, as there. Not taken: the bar's crack pixels (a crack landing on an old one adds a
    /// point to the target) - no bar here; the source's added creak cooldown.</summary>
    bool DamagePass()
    {
        var c = Car;
        var active = c.B(ScrCar.damageAccumulationActive) != 0;
        var wreck = false;
        if (active)
        {
            var target = ((c.B(ScrCar.accumulatedForceFrontLeft) + c.B(ScrCar.accumulatedForceFrontRight)) / 2 + c.B(ScrCar.accumulatedForceRear)) / 2;
            if (target > Damage) Damage = target;
            if (Damage >= WreckAt) { Damage = WreckAt - 1; wreck = true; }       // as the original's SUBQ before initiateCarWreck
        }
        var timer = c.B(ScrCar.majorImpactCooldownTimer);
        if (timer != 0)
        {
            c.B(ScrCar.majorImpactCooldownTimer, timer - 1);
            if (timer != MajorCooldown && !active) return wreck;
            Creak();
        }
        else if (active)
        {
            var holes = c.B(ScrCar.holeRenderingPosition);
            if ((ushort)c.W(ScrCar.maxCompressionVelocity) >= 0x1400 && holes != 0)
            {
                c.B(ScrCar.holeRenderingPosition, holes - 1);
                c.B(ScrCar.majorImpactCooldownTimer, MajorCooldown);
                Play(5);
            }
            else Creak();
        }
        c.B(ScrCar.damageAccumulationActive, 0);
        return wreck;
    }

    /// <summary>MAJOR_IMPACT_COOLDOWN_TIME, the original's.</summary>
    const int MajorCooldown = 0x45;

    void Creak()
    {
        int v = Car.B(ScrCar.maxCompressionVelocity);
        if (v < 7) v = 7;
        v = (v << 2) & 0xFF;
        if (v >= 0x40) v = 0x40;
        Car.W(ScrCar.sampleParameterTable + 4 * 16 + 0xA, v);                 // audioSample4Volume
        Play(4);
    }

    /// <summary>playSample: the effect at the period and volume its table entry holds now.</summary>
    void Play(int sample)
    {
        if (Entity is not { } e) return;
        var at = ScrCar.sampleParameterTable + 16 * sample;
        Sound?.Invoke(e, sample, Car.W(at + 0xA), (ushort)Car.W(at + 8), Car.W(at + 0xC));
    }

    /// <summary>The car's level and pan this pass (Heard).</summary>
    static void Hear(WorldState s, Entity e)
    {
        if (Heard is null) return;
        if (Listener is { } l)
        {                                                                   // the chase camera's spot and facing: the same law, its
            double dx = e.UW(0x4A) - l.X, dz = e.UW(0x4E) - l.Z;           // offsets in the game's doubled units (+$56 / +$5A's)
            double fx = -Math.Sin(l.Yaw), fz = Math.Cos(l.Yaw), rxv = Math.Cos(l.Yaw), rzv = Math.Sin(l.Yaw);
            double rz = 2 * (dx * fx + dz * fz), rx = 2 * (dx * rxv + dz * rzv);
            var far = (int)Math.Min(32767, Math.Max(Math.Abs(rx), Math.Abs(rz))) >> 7;
            var level = (63 - Math.Min(63, far)) / 63f;
            var p = rz + 0x100 > 0 ? (float)Math.Clamp(rx * 256 / (rz + 0x100) / s.Camera.CentreX, -1, 1) : 0f;
            Heard(e, level, p);
            return;
        }
        float pan = 0;
        var zz = (short)(e.W(0x5A) + 0x100);
        if (zz > 0)
        {                                                                   // as Objects.Persp: divs.w's quotient is the low word
            var sx = (short)M68.Divs(e.W(0x56) << 8, zz);
            pan = Math.Clamp(sx / (float)s.Camera.CentreX, -1f, 1f);
        }
        Heard(e, Channels.Volume(e) / 63f, pan);
    }

    /// <summary>updateEngineAudioPitch (4973), once an original pass: the engine's target from the forward speed (in the
    /// air: high with the throttle or brake held, else idle), and the step the accumulator takes each blank toward it -
    /// up at most $100, down $100 on the ground and $20 in the air, the last $FF away an eighth of the gap; under $C0 it
    /// climbs 2.</summary>
    void EnginePitch(int flags)
    {
        var c = Car;
        int d0;
        var ground = c.B(ScrCar.wheelMovementActive) != 0;
        if (!ground) d0 = (flags & 3) != 0 ? 0x9000 : 0;
        else
        {
            d0 = c.W(ScrCar.carLocalVelocityZ) & 0xFFF0;
            if ((short)d0 < 0) d0 = -(short)d0 & 0xFFFF;
        }
        d0 = ((d0 + 0x580) & 0xFFFF) >> 3;
        int acc = c.W(ScrCar.enginePitchAccumulator);
        var delta = acc < 0xC0 ? 2 : (short)(d0 - acc) >> 3;
        int hi = delta >> 8 & 0xFF, lo = delta & 0xFF;
        if (hi != 0)
        {
            if (hi < 0x80) { hi = 1; lo = 0; }
            else if (ground) { if (hi != 0xFF) { hi = 0xFF; lo = 0; } }
            else if (hi != 0xFF || lo < 0xE0) { hi = 0xFF; lo = 0xE0; }
        }
        c.W(ScrCar.enginePitchDelta, hi << 8 | lo);
    }

    /// <summary>The vertical blank's engine (10777): the accumulator takes its step (never below 0), the period is
    /// $493E00 / (it + $17A) kept to $7C-$3FFE, halved until under $100 while stepping from the eighth version down
    /// (the shorter versions are the same sound an octave up each).</summary>
    void EngineBlank()
    {
        if (Entity is not { } e) return;
        var c = Car;
        var acc = (short)(c.W(ScrCar.enginePitchAccumulator) + c.W(ScrCar.enginePitchDelta));
        if (acc < 0) acc = 0;
        c.W(ScrCar.enginePitchAccumulator, acc);
        var d3 = 0x493E00 / (acc + 0x17A);
        if (d3 >= 0x3FFF) d3 = 0x3FFE;
        if (d3 < 0x7C) d3 = 0x7C;
        var version = 6;
        while (d3 >= 0x100) { d3 >>= 1; if (--version < 0) version = 0; }
        Engine?.Invoke(e, version, Car.W(ScrCar.sampleParameterTable + 7 * 16 + 0xA), d3);
    }

    /// <summary>handleGroundCollision / handleEdgeCollision (9998 / 10033), once an original pass: off the road, the ground
    /// grind at a wobbling period; on it with a wheel over the edge (or wrecked), the edge grind, higher with speed -
    /// both only with the wheels on the ground. (The edge here: a contact point off the road's strips, where the original
    /// had checkLateralBoundsAndBounce's flag.)</summary>
    void Grind(bool offRoad, bool edge)
    {
        var c = Car;
        var ground = c.B(ScrCar.wheelMovementActive) != 0;
        if (offRoad)
        {
            if (!ground) return;
            c.W(ScrCar.sampleParameterTable + 6 * 16 + 8, 0x1C2 + (_noise.Next(256) & 0x1C));   // sampleGroundCollisionPeriod
            Play(6);
            return;
        }
        if (!edge && c.B(ScrCar.postWreckStateFlag) == 0) return;
        int speed = c.B(ScrCar.carSpeedMagnitude);                               // MOVE.B: the word's high half
        if (speed >= 0x80) speed -= 0x100;                                       // CMP.B / BLT: signed
        if (speed < 1 || !ground) return;
        var level = Math.Min(speed, 0x32) >> 1;
        if (level >= 0x1F) level = 0x1F;
        c.W(ScrCar.sampleParameterTable + 1 * 16 + 8, ((level ^ 0x1F) << 2) + 0xAA);   // sampleGrindPeriod
        Play(1);
    }

    /// <summary>Picture units a frame per world speed: one original pass moves worldX/ZSpeed x $EE/256 x 64/4096 ground
    /// units and worldYSpeed x $EE/256 x 128/256 wheel units, at Rate passes a frame; the picture's x, z are doubled world
    /// units, its heights whole.</summary>
    const double CarryXz = 0xEE / 256.0 * 64 / 4096 * ScrRoad.Scale * Rate * 2, CarryY = 0xEE / 256.0 * 128 / 256 * ScrRoad.Scale / 32 * Rate;

    /// <summary>THE WRECK: the car comes apart where it is, the pieces carrying its speed, then it is out of sight until the
    /// restart (RestartAfter passes, the off-road wait).</summary>
    void Wreck(Entity e)
    {
        var c = Car;
        e.SetW(0x4A, (int)Math.Round(_now.X)); e.SetW(0x4C, (int)Math.Round(_now.Y)); e.SetW(0x4E, (int)Math.Round(_now.Z));
        e.SetW(0x7E, _now.Yaw); e.HeadingFine = e.UW(0x7E);
        e.B[0x7C] = (byte)(-(_now.Pitch >> 8)); e.B[0x80] = (byte)(_now.Roll >> 8);
        Objects.Wrecked?.Invoke((byte[])e.B.Clone(), ((float)(c.W(ScrCar.worldXSpeed) * CarryXz), (float)(c.W(ScrCar.worldYSpeed) * CarryY), (float)(c.W(ScrCar.worldZSpeed) * CarryXz)));
        Wrecks++;
        _wreck = RestartAfter;
        e.SetW(0x4C, HiddenY); e.SetW(0x74, HiddenY); e.SetW(0x3E, 0);
    }

    /// <summary>The boost is burning this pass (the original's flag: the flames).</summary>
    /// <summary>The car's pitch and roll words (pitch nose up positive, roll as the original's cameraAngleZ) glided between
    /// passes as the record is - for the in-car view, finer than the record's bytes.</summary>
    public (int Pitch, int Roll) ViewAngles { get; private set; }
    /// <summary>The body's height (world units) glided between passes: the original's camera, the in-car eye.</summary>
    public double EyeHeight { get; private set; }
    double _wasEye, _nowEye;

    public bool Boosting => (Car.B(ScrCar.boostActiveFlag) & 0x80) != 0;
}
