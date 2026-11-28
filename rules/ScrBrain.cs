namespace Hunter.Rules;

/// <summary>STUNTIN's AI driver: the original opponent's rules (StuntCarRacer.s processOpponentLogic, docs/scr-physics.md
/// "The opponent") as a brain that drives a REAL car - an original-physics ScrCar (ScrDrive) - with a stick, as a player
/// would. The original's opponent rides the track; here the same rules decide what a driver wants and the stick gets it:
/// - SPEED: each piece's limit worked out backwards round the lap from the corners (a corner = the cornering skill, each
///   piece before it the next one's + $0A, at most $7F = none) - the original's segmentVisibilityData; the throttle up
///   below it, the brake above it (the original's throttle latches, so up and down are both needed).
/// - LANE (0 the left edge .. 255 the right, the original's opponentLateralPosition): $40, or $6E when aggressive, from
///   its own side (mirrored for an odd driver number); the middle ($80) when a corner is within the next 2 pieces; near
///   another car its traits decide - aggressive ($08) keeps to that car's side (blocks), follow ($02) takes its lane,
///   overtake ($20) pulls out to the other side when behind it.
/// - STICK: steered toward the road's way a strip ahead plus a share of the lane error, feathered (on for a share of the
///   passes in proportion to the want) - the original's corners need it: centred they turn too little, held too much.
/// - REAL-CAR SENSE the original's fake needed none of: corners two speed steps slower; a gap kept to a car just ahead
///   (longer the faster it closes), a lane clear of a car alongside (into a corner, tucked in behind it); closing on a car
///   in its path it pulls out to pass (in a corner holds back to that car's speed); lined up in the middle before a crest,
///   straight at a jump and over a crest (but back in off an edge, and never on a corner); a run of jumps at the run's
///   highest speed, the boost there (wheels down, pointing down the road) while under it; off the throttle running wide in
///   a corner; a lane change turns it at most LaneTurn off the road's way, gentler the faster (off a corner's approach).
/// Better than the original where it was a fake: it drives the real physics, so it can fly, land short, slide and fall.</summary>
public sealed class ScrBrain
{
    /// <summary>The original's traits bits.</summary>
    public const int Follow = 0x02, Aggressive = 0x08, Overtake = 0x20;

    public int Traits { get; }
    public int Driver { get; }
    /// <summary>The corner speed limit, the original's aiCorneringSkill scale (speed byte = carLocalVelocityZ >> 8).</summary>
    public int CorneringSkill { get; }
    public string Name { get; }

    int[]? _limits;
    readonly HashSet<int> _inRun = [];                                       // pieces of a run of jumps (and the 3 before)
    int _pass;
    /// <summary>A car this many strips ahead or fewer, and closer across than InPath (lanes, 0-255: the car is about 54 wide on
    /// the 384-wide road), is in the way.</summary>
    const int ClosingStrips = 8, InPath = 0x48;
    /// <summary>A gap kept to a car ahead: two car lengths along the road (the car is 121 ground units long, 181 world), and
    /// across within a car's width and a bit (81 wide, 122 world).</summary>
    const int KeepBack = 360, SideBy = 150;
    /// <summary>The gap grows by this (world units) per speed byte the car is faster than the one ahead: room to slow.</summary>
    const int KeepPerSpeed = 40;
    /// <summary>A car within this far along the road (world units, a car's length) is alongside; a lane Apart (lanes: a car's
    /// width, 54, and a half) from it is kept.</summary>
    const int Alongside = 180, Apart = 0x50;
    /// <summary>Near a jump, a heading off the road's way by more than this (a turn is $10000: about 3.3 degrees) is steered.</summary>
    const int JumpDeadZone = 600;
    /// <summary>Near a jump, a car closer to an edge than this lane (0-255; the car is about 54 wide) steers back in.</summary>
    const int SafeLane = 0x30;
    /// <summary>The lanes a car aims for (0-255): no target nearer an edge than EdgeLane - a car (about 54 wide) aimed at
    /// $20 had 5 lanes left, and slid the rest off in a corner.</summary>
    const int EdgeLane = 0x40;
    /// <summary>Outside EdgeLane the pull back is full and may turn the car up to EdgeTurn off the road's way.</summary>
    const int EdgeTurn = 3 * LaneTurn;
    /// <summary>Running wide brakes only above this speed: braking still is reverse (a car held on the brake at the edge
    /// stopped, then backed off the road).</summary>
    const int BrakeAbove = 0x10;
    /// <summary>The most a lane change turns the car off the road's way (a turn is $10000: about 6.6 degrees).</summary>
    const int LaneTurn = 1200;
    /// <summary>The lane's pull on the heading (a turn per lane off it), full below speed LanePullBelow, less above.</summary>
    const int LanePull = 24, LanePullBelow = 30;
    /// <summary>The corners' speed two of the original's steps ($0A each) under the cornering skill: the original's limit was
    /// set for a fake car that could not slide.</summary>
    const int CornerDrop = 0x14;
    /// <summary>A crest: within CrestAhead strips the road's climb a strip falls by more than CrestDrop (world units) - a car
    /// carried by the climb leaves the ground there, so it is driven as a jump.</summary>
    const int CrestAhead = 6, CrestDrop = 80;
    /// <summary>A car this close over the road (world units, the record's height) has its wheels on it.</summary>
    const int GroundedBy = 24;
    /// <summary>Strips before a crest from which a car makes for the middle of the road.</summary>
    const int LineUp = 20;
    /// <summary>The boost only with the heading this close to the road's way (a turn is $10000: about 2.2 degrees).</summary>
    const int BoostAligned = 400;
    /// <summary>The boost in the last BoostBefore strips before a jump too, under its speed: without it a real car reaches the
    /// takeoff well under the original's jump speeds and lands on the far lip.</summary>
    const int BoostBefore = 12, RunCost = 10;
    /// <summary>A corner that leads straight into a jump (one starting within IntoJumpStrips of the corner's end, no corner
    /// between) is taken JumpCornerLift faster: from the usual corner speed a real car reaches such a jump's takeoff well under
    /// its speed (Hump Back's: ~13500 against ~16000 needed) and lands on the far lip.</summary>
    const int JumpCornerLift = 0x0B, IntoJumpStrips = 25;
    static bool IntoJump(ScrRoad road, int piece)
    {
        var n = road.Sections.Count;
        var end = road.Piece.ToList().LastIndexOf(piece);
        for (var k = 1; k <= IntoJumpStrips; k++)
        {
            int s0 = (end + k) % n, p = road.Piece[s0];
            if (p == piece) continue;
            if (road.Obstacles.Any(o => o.Piece == p)) return true;
            if ((road.Steer[p].Flags & 0x80) != 0) return false;
        }
        return false;
    }
    bool JumpAhead(ScrRoad road, int strip)
    {
        var n = road.Sections.Count;
        var trench = Trenches(road);
        for (var k = 1; k <= BoostBefore; k++)
        {
            int s0 = (strip + k) % n, p = road.Piece[s0], q = road.Piece[(s0 - 1 + n) % n];
            if (p != q && road.Obstacles.Any(o => o.Piece == p) || trench[s0]) return true;
        }
        return false;
    }
    bool[]? _trenches;

    /// <summary>A TRENCH (a gap the track's list of jumps leaves out): the road drops more than TrenchDepth below a strip and
    /// climbs back to within TrenchBack of it no more than TrenchLong strips on, no listed jump within a piece - a car must
    /// fly it. Per strip: the lip.</summary>
    bool[] Trenches(ScrRoad road)
    {
        if (_trenches is not null) return _trenches;
        var n = road.Sections.Count;
        double H(int i) { var c = road.Sections[(i % n + n) % n]; return (c.LY + c.RY) / 2; }
        var lip = new bool[n];
        for (var i = 0; i < n; i++)
        {
            if (H(i + 1) > H(i) - TrenchDepth) continue;
            var p = road.Piece[i];                                           // a listed jump's own gap is not one
            if (road.Obstacles.Any(o => Math.Abs((o.Piece - p + road.Steer.Count + road.Steer.Count / 2) % road.Steer.Count - road.Steer.Count / 2) <= 1)) continue;
            for (var m = 2; m <= TrenchLong; m++)
                if (H(i + m) >= H(i) - TrenchBack) { lip[i] = true; break; }
        }
        return _trenches = lip;
    }
    const int TrenchDepth = 400, TrenchBack = 200, TrenchLong = 12;

    /// <summary>THE RUN-IN TO A TRENCH: from TrenchRunFrom to BoostBefore strips before its lip the car keeps under
    /// TrenchSpeed - a car must be on the road at the lip; one flying in off a jump before it (Stepping Stones' ramp onto the
    /// plateau, boosted to 17700) came down on the lip nose first.</summary>
    bool TrenchRunIn(ScrRoad road, int strip)
    {
        var n = road.Sections.Count;
        var trench = Trenches(road);
        for (var k = BoostBefore + 1; k <= TrenchRunFrom; k++)
            if (trench[(strip + k) % n]) return true;
        return false;
    }
    const int TrenchRunFrom = 25, TrenchSpeed = 0x37;

    /// <summary>A BIG JUMP: a listed jump whose speed byte is BigJump or more - more than a car's engine gives it on the flat
    /// (Big Ramp's $56). Per strip: up the climb to its lip (a crest on its piece within ClimbStrips, no corner between) - the
    /// boost the whole way up (a car boosted only the last BoostBefore strips left the lip at 17400 and landed far short; the
    /// whole climb gives about 20500, enough for ScrRail).</summary>
    bool[] BigClimbs(ScrRoad road)
    {
        if (_bigClimbs is not null) return _bigClimbs;
        var n = road.Sections.Count;
        var crest = Crests(road);
        var big = road.Obstacles.Where(o => (o.Speed & 0x7F) >= BigJump).Select(o => o.Piece).ToHashSet();
        var at = new bool[n];
        for (var i = 0; i < n; i++)
            for (var k = 1; k <= ClimbStrips; k++)
            {
                var j = (i + k) % n;
                if ((road.Steer[road.Piece[j]].Flags & 0x80) != 0) break;            // a corner between: not this climb
                if (crest[j] && big.Contains(road.Piece[j])) { at[i] = true; break; }
            }
        return _bigClimbs = at;
    }
    bool[]? _bigClimbs;
    /// <summary>A big jump's speed byte and how far back its climb's boost reaches; the fuel a climb costs (3 units in a run up
    /// Big Ramp, kept for each lap still to come).</summary>
    const int BigJump = 0x50, ClimbStrips = 55, ClimbCost = 4;
    bool[]? _crests;

    /// <summary>Per strip: a crest within CrestAhead strips (the road's mean height, strip to strip).</summary>
    bool[] Crests(ScrRoad road)
    {
        if (_crests is not null) return _crests;
        var n = road.Sections.Count;
        double H(int i) { var c = road.Sections[(i % n + n) % n]; return (c.LY + c.RY) / 2; }
        double Climb(int i) => H(i + 1) - H(i);
        var crest = new bool[n];
        for (var i = 0; i < n; i++)                                          // from the strip before (the lip's own strip too)
            for (var a = i - 1; a < i + CrestAhead && !crest[i]; a++)
                for (var b = a + 1; b <= i + CrestAhead && !crest[i]; b++)
                    crest[i] = Climb(a) - Climb(b) > CrestDrop;
        return _crests = crest;
    }

    // THE LEADER'S SLIP (the original's processOpponentAI 5470-5533, setAITargetSpeed 12649, the lateral move 12416-12434):
    // an opponent ahead of the player, on a straight just off a corner (aiPatternControlFlags: the last pass's piece's
    // corner bit, EOR its reverse bit >> 1), not near a jump, may lose it: when a draw of 0-31 is no more than its driver
    // number, for 16 passes it weaves - its lane moved 9 of 255 a pass to the sign of the original's sine table
    // (aiMovementPatterns), read from index (timer + 8, or + 16 by that bit 6) + 5 - and aims $19 slower. Computer cars only.
    static readonly int[] Patterns = [32, 80, 96, 112, 112, 96, 80, 32, -32, -80, -96, -112, -112, -96, -80, -32];
    const int SlipPasses = 0x10, SlipStep = 9, SlipSlower = 0x19;
    int _slip, _slipOffset, _patternFlags, _weave, _slipSpeed;
    long _lastPass = -1;
    Random? _draw;
    /// <summary>The slip draw's seed: a league race's own, else fixed by the driver.</summary>
    readonly int? _seed;
    /// <summary>Slips so far (a probe's count).</summary>
    public int Slips { get; private set; }

    /// <summary>Once an original pass: the slip's timer and the weave, or the chance of a new slip.</summary>
    void SlipPass(WorldState s, ScrDrive drive, ScrRoad road, int piece)
    {
        _draw ??= new Random(_seed ?? Driver * 7919 + 17);
        if (_slip > 0)
        {
            var idx = (_slip + _slipOffset) & 15;
            _slip--;
            _weave = Math.Clamp(_weave + Math.Sign(Patterns[(idx + 5) & 15]) * SlipStep, -0x80, 0x80);
        }
        else
        {
            if (_weave != 0) _weave -= Math.Sign(_weave) * Math.Min(SlipStep, Math.Abs(_weave));   // back to its own lane
            var lim = Limits(road);
            var leading = s.ScrDrive is { } p && !double.IsNaN(p.Progress(road)) && drive.Progress(road) > p.Progress(road);
            if ((lim[piece] & 0x80) == 0 && leading && (road.Steer[piece].Flags & 0x80) == 0 && (_patternFlags & 0x80) != 0 && !JumpWithin(road, piece))
            {
                _slipOffset = (_patternFlags & 0x40) != 0 ? 0x10 : 8;
                if (_draw.Next(32) <= Driver) { _slip = SlipPasses; Slips++; _slipSpeed = drive.Car.W(ScrCar.carLocalVelocityZ) >> 8; }
            }
        }
        _patternFlags = ((road.Steer[piece].Reverse >> 1) ^ road.Steer[piece].Flags) & 0xFF;
    }

    /// <summary>A jump within the run-up a car needs to reach its speed (ScrDrive.RunUp strips from the piece's start): no slip
    /// there - a slip leaves the car SlipSlower under its speed, too slow at the takeoff.</summary>
    static bool JumpWithin(ScrRoad road, int piece)
    {
        var n = road.Sections.Count;
        var at = road.Piece.ToList().IndexOf(piece);
        for (var k = 0; k < ScrDrive.RunUp; k++)
        {
            var p = road.Piece[(at + k) % n];
            if (road.Obstacles.Any(o => o.Piece == p)) return true;
        }
        return false;
    }

    public ScrBrain(string name, int driver, int traits, int corneringSkill, int? seed = null)
    {
        (Name, Driver, Traits, _seed) = (name, driver, traits, seed);
        if (seed is { } sd)
        {
            _dice = new Random(sd ^ 0x5BD1E995);
            CornerRoll = _dice.Next(CornerRollLow, CornerRollHigh + 1);
            LaneRoll = _dice.Next(-LaneRollReach, LaneRollReach + 1);
            if (!DiceParts.Contains("corner")) CornerRoll = 0;
            if (!DiceParts.Contains("lane")) LaneRoll = 0;
            if (DiceParts.Contains("phase")) _phase = _dice.Next(8);
        }
        CorneringSkill = corneringSkill + CornerRoll;
    }

    /// <summary>THE RACE'S DICE (a seeded driver only; unseeded, it drives as it always has): its corner speed this race, a few
    /// speed bytes either side of its skill; its home lane moved up to LaneRollReach; and where its steering's feathering beat
    /// starts - so no two races take the same line.</summary>
    readonly Random? _dice;
    public int CornerRoll { get; }
    /// <summary>Which of the dice are rolled (checks turn parts off one at a time): corner, lane, phase.</summary>
    public static string DiceParts { get; set; } = "corner,lane,phase";
    readonly int _phase;
    public int LaneRoll { get; }
    const int CornerRollLow = -6, CornerRollHigh = 3, LaneRollReach = 0x18;

    /// <summary>The piece speed limits as the original works them out (StuntCarRacer.s 4703-4774, segmentVisibilityData):
    /// two laps backwards from the last piece; a listed obstacle (a jump) takes its own speed byte, its top bit marking it
    /// and the 3 pieces before; a corner takes the cornering skill and leaves skill - $0A for the piece before; any other
    /// piece the one after's + $0A while that stays under $80. Kept as the original's bytes (top bit = an obstacle near);
    /// a speed compares against the low 7 bits.</summary>
    int[] Limits(ScrRoad road)
    {
        if (_limits is not null) return _limits;
        var n = road.Steer.Count;
        var lim = new int[n];
        int carry = 0x7C, repeat = 0;                                        // tempByte4, segmentRepeatCounter
        for (var lap = 0; lap < 2; lap++)
            for (var p = n - 1; p >= 0; p--)
            {
                var ob = road.Obstacles.FirstOrDefault(o => o.Piece == p, (Piece: -1, Speed: 0));
                if (ob.Piece >= 0)
                {
                    lim[p] = ob.Speed;
                    if ((ob.Speed & 0x80) != 0) repeat = 3;
                    carry = ob.Speed & 0x7F;
                    continue;
                }
                int d0;
                if ((road.Steer[p].Flags & 0x80) != 0)
                {
                    var skill = CorneringSkill - CornerDrop + (IntoJump(road, p) ? JumpCornerLift : 0);
                    carry = (skill - 0x0A) & 0xFF; d0 = skill;
                }
                else { if (((carry + 0x0A) & 0x80) == 0) carry += 0x0A; d0 = carry; }
                if (repeat > 0) { repeat--; d0 |= 0x80; }
                lim[p] = d0 & 0xFF;
            }
        // A RUN OF JUMPS (jumps 2 pieces apart or closer - stepping stones) takes the run's highest speed at each: the same gap
        // needs the same speed of a real car (the original's falling speeds, its car on rails, landed a real one on the next
        // stone's edge, each landing slower)
        var jumps = road.Obstacles.Select(o => o.Piece).Distinct().ToList();
        foreach (var p in jumps)
        {
            var run = new HashSet<int> { p };
            for (var grew = true; grew;)
            {
                grew = false;
                foreach (var q in jumps)
                    if (!run.Contains(q) && run.Any(r => Math.Abs(r - q) <= 2)) { run.Add(q); grew = true; }
            }
            var top = run.Max(q => lim[q] & 0x7F);
            lim[p] = (lim[p] & 0x80) | top;
            if (run.Count > 1)                                               // a run: its pieces and the 3 before boost
                for (var q = run.Min() - 3; q <= run.Max(); q++) _inRun.Add((q + lim.Length) % lim.Length);
        }
        return _limits = lim;
    }

    /// <summary>DRAW BRIDGE's timing: the bridge holds at whatever its leaves are at when a car reaches its first piece, so a
    /// car arriving with them raised runs up a steep leaf (a hard hit) and lands hard past it. From BridgeLook pieces before
    /// it, the phase the leaves will have when the car gets there at its speed (they step one a pass while free) is worked
    /// out; with them higher than BridgeLow then, the car eases off (arriving later). Held raised by the other car, it stays
    /// short of piece HoldFrom (a computer car there keeps a held bridge held). Null: no limit.</summary>
    static int? BridgeWait(ScrRoad road, ScrBridge bridge, int strip, int piece, int speed, int limit)
    {
        if (piece >= ScrBridge.First || piece < ScrBridge.First - BridgeLook) return null;
        var n = road.Sections.Count;
        double to = 0, toHold = -1;
        for (int k = 0, i = strip; k < n && road.Piece[i] != ScrBridge.First; k++, i = (i + 1) % n)
        {
            if (toHold < 0 && road.Piece[i] >= ScrBridge.HoldFrom) toHold = to;
            var a = road.Sections[i]; var b = road.Sections[(i + 1) % n];
            to += Math.Sqrt(Math.Pow((b.LX + b.RX - a.LX - a.RX) / 2, 2) + Math.Pow((b.LZ + b.RZ - a.LZ - a.RZ) / 2, 2));
        }
        if (bridge.Held)
        {                                                                    // held: it goes on a low one; short of HoldFrom it
            if (Low(bridge.Phase) || toHold < 0 || piece >= ScrBridge.HoldFrom) return null;   // waits for a raised one to be
            return (int)Math.Min(speed, toHold / BridgeStopIn);              // freed (past it, waiting would keep it held)
        }
        // free: the leaves are lowest between phases $0F and $10 (of each $20); the first such moment the car can reach at
        // its piece's limit, and the speed that gets it there then
        var at = bridge.Phase & 0x1F;
        for (var t = (0x0F - at + 0x20) % 0x20 + 0.5; t < 4 * 0x20; t += 0x20)
        {
            var needed = to * WorldPerPassAt / t / 256;                       // the speed byte that arrives in t passes
            if (needed <= limit) return (int)needed;
        }
        return null;
    }
    static bool Low(int phase) => (ScrBridge.Step(phase) / 8 - 4) <= BridgeLow;
    /// <summary>Pieces before the bridge the timing starts; the leaves' highest index (0 flat .. 15) still driven onto; a pass's
    /// way (world units) is carLocalVelocityZ / WorldPerPassAt; the world units a speed byte stops in (waiting short of
    /// HoldFrom).</summary>
    const int BridgeLook = 6, BridgeLow = 3;
    const double WorldPerPassAt = 46, BridgeStopIn = 12;

    /// <summary>What the driver does this pass: the stick ($D6 bits: down 1, up 2, right 4, left 8) and the boost.
    /// others: the other cars on the road (for the lane); opponent: a computer car (it may slip when leading).</summary>
    public (byte D6, bool Fire) Decide(WorldState s, Entity me, ScrDrive drive, IEnumerable<Entity> others, bool opponent = false)
    {
        _pass++;
        var surface = s.Ground.Road!;
        var road = surface.Road;
        var n = road.Sections.Count;
        int x = me.UW(0x4A), z = me.UW(0x4E);
        var strip = surface.StripAt(x, z, me.W(0x4C), ScrDrive.Reach);
        var car = drive.Car;
        var speed = car.W(ScrCar.carLocalVelocityZ) >> 8;
        if (strip < 0) return (2, false);                                    // off the road: on, straight (the restart is the game's)

        // speed: the piece's limit (and the next piece's, a look ahead the size of the original's progressive ramp)
        var lim = Limits(road);
        var piece = road.Piece[strip];
        if (opponent && drive.Passes != _lastPass) { _lastPass = drive.Passes; SlipPass(s, drive, road, piece); }
        var limit = Math.Min(lim[piece] & 0x7F, lim[(piece + 1) % lim.Length] & 0x7F);
        if (TrenchRunIn(road, strip)) limit = Math.Min(limit, TrenchSpeed);
        if (_slip > 0) limit = Math.Max(0, Math.Min(limit, _slipSpeed - SlipSlower));   // slipping: $19 under its racing speed (the
                                                                                        // original's base speed; a straight's limit is none)
        if (surface.Bridge is { } bridge && BridgeWait(road, bridge, strip, piece, speed, limit) is { } wait) limit = Math.Min(limit, wait);
        bool up = speed < limit, down = speed > limit + 2;

        // the lane
        int Lane(Entity e, int st)
        {
            var sec = road.Sections[st];
            double ax = sec.RX - sec.LX, az = sec.RZ - sec.LZ, w = Math.Sqrt(ax * ax + az * az);
            var lat = ((e.UW(0x4A) - (sec.LX + sec.RX) / 2) * ax + (e.UW(0x4E) - (sec.LZ + sec.RZ) / 2) * az) / w;   // + right
            return (int)Math.Round(128 + lat / w * 256);
        }
        var myLane = Lane(me, strip);
        var target = ((Traits & Aggressive) != 0 ? 0x6E : 0x40) + LaneRoll;
        if ((Driver & 1) != 0) target = 255 - target;
        var jumpNear = (lim[piece] & 0x80) != 0                              // a jump here or within 3 pieces (the original's mark),
                       && (road.Steer[piece].Flags & 0x80) == 0;              // not on a corner (a corner is driven as one)
        var lip = Crests(road)[strip];                                       // a crest (a jump's lip among them) within a few strips
        var cornerNear = false;
        for (var k = 0; k <= 2; k++)
            if ((road.Steer[(piece + k) % road.Steer.Count].Flags & 0x80) != 0) { cornerNear = true; break; }
        int? keepOff = null;                                                 // a lane clear of a car alongside
        foreach (var o in others)
        {
            var os = surface.StripAt(o.UW(0x4A), o.UW(0x4E), o.W(0x4C), ScrDrive.Reach);
            if (os < 0) continue;
            var ahead = (os - strip + n) % n;                                // strips the other car is ahead (wrapped)
            var behind = (strip - os + n) % n;
            var otherLane = Lane(o, os);
            var otherSpeed = o.W(0x3E);                                      // ScrDrive's speed (carLocalVelocityZ >> 8, unsigned)
            // NOT INTO THE CAR AHEAD (the original's opponent was a fake that drove through the player; this one is a real car):
            // closing on a car just ahead in its path (lanes closer than a car's width and a bit), whatever its traits, it pulls
            // out to pass on the other side - in a corner it holds back to that car's speed instead (near a jump it keeps both
            // its speed and its line, below)
            var closing = ahead > 0 && ahead <= ClosingStrips && Math.Abs(otherLane - myLane) < InPath && speed > otherSpeed;
            // A GAP: a car less than KeepBack ahead along the road (and KeepPerSpeed more a speed byte it is slower) and less than
            // SideBy across (world units), however fast: it eases off under that car's speed (touching cars take damage every pass)
            var wa = surface.Way(strip) / 65536.0 * 2 * Math.PI;
            double odx = o.UW(0x4A) - x, odz = o.UW(0x4E) - z;
            double along = odx * Math.Sin(wa) + odz * Math.Cos(wa), across = odx * Math.Cos(wa) - odz * Math.Sin(wa);
            if (along > 0 && along < KeepBack + Math.Max(0, speed - otherSpeed) * KeepPerSpeed && Math.Abs(across) < SideBy)
            {
                limit = Math.Min(limit, otherSpeed - 2); up = speed < limit; down = speed > limit + 2;
                continue;
            }
            // ALONGSIDE (within a car's length along, two widths across): a lane Apart from it on its own side; into a corner
            // with it a little ahead, it eases off and tucks in behind (side by side into a corner, the inside car slid out
            // into the outside one and pushed it off)
            if (Math.Abs(along) < Alongside && Math.Abs(across) < 2 * SideBy)
            {
                keepOff = myLane < otherLane ? Math.Max(EdgeLane, otherLane - Apart) : Math.Min(255 - EdgeLane, otherLane + Apart);
                if (cornerNear && along > 0) { limit = Math.Min(limit, otherSpeed - 4); up = speed < limit; down = speed > limit + 2; }
            }
            if (closing && cornerNear) { limit = Math.Min(limit, otherSpeed); up = speed < limit; down = speed > limit + 2; }
            else if (closing) target = otherLane < 128 ? 255 - EdgeLane : EdgeLane;
            else if (ahead > 0 && ahead <= 6)                                // just ahead of me
            {
                if ((Traits & Overtake) != 0) target = otherLane < 128 ? 255 - EdgeLane : EdgeLane;   // pull out past it
                else if ((Traits & Follow) != 0) target = otherLane;          // tuck in behind (not closing on it)
            }
            else if (behind > 0 && behind <= 6 && (Traits & Aggressive) != 0)
                target = otherLane;                                          // block the one behind
        }
        if (cornerNear) target = 0x80;                                       // a corner within the next 2 pieces: the middle
        for (var k = 1; k <= LineUp; k++)                                    // LINED UP FOR A JUMP: a crest within LineUp strips: the
            if (Crests(road)[(strip + k) % n]) { target = 0x80; break; }     // middle, while it may still move (at the lip it may not)
        if (keepOff is { } clear) target = clear;
        // RUNNING WIDE: on a corner, in the outer quarter of the road it comes off the throttle, past SafeLane it brakes (at the
        // grip's edge a car slides out a little each strip, and a long corner adds it up past the edge)
        if ((road.Steer[piece].Flags & 0x80) != 0)
        {
            var turn = (short)(surface.Way((strip + 2) % n) - surface.Way(strip));
            var outer = turn > 0 ? myLane : 255 - myLane;                    // lanes from the outside edge (the way turning up: the left)
            if (outer < 0x40) { up = false; down |= outer < SafeLane && speed > BrakeAbove; }
        }
        target = Math.Clamp(target + _weave, EdgeLane, 255 - EdgeLane);                   // a slip's weave on top

        // the stick: toward the road's way a strip on, and back toward the lane, feathered. STRAIGHT AT A JUMP: on the jump's
        // pieces and before a crest no lane moves and no small corrections - only a heading clearly off the road's way is
        // steered (a turn taken at the lip lasts the whole flight); but off a crest, a car nearer an edge than SafeLane is
        // steered back in to it
        var way = surface.Way((strip + 1) % n);
        var off = (short)(way - (ushort)car.W(ScrCar.cameraAngleY));
        // a lane change: at most LaneTurn off the road's way, and gentler the faster (the same angle crosses the road quicker;
        // at 14000 one at full pull ran on past its lane and off the road before the car straightened)
        var pull = cornerNear ? LanePull                                     // into and on a corner the full pull (against the slide)
                 : Math.Min(LanePull, LanePull * LanePullBelow / Math.Max(1, Math.Abs(speed)));
        var nearEdge = myLane < EdgeLane || myLane > 255 - EdgeLane;
        int Toward(int lane) => nearEdge ? Math.Clamp((lane - myLane) * LanePull, -EdgeTurn, EdgeTurn)
                                         : Math.Clamp((lane - myLane) * pull, -LaneTurn, LaneTurn);
        var safe = Math.Clamp(myLane, SafeLane, 255 - SafeLane);
        var gap = !(jumpNear || lip) ? off + Toward(target)
                : !lip && safe != myLane ? off + Toward(safe)
                : Math.Abs((int)off) > JumpDeadZone ? off : 0;
        var duty = Math.Clamp(gap / 3000.0, -1, 1);
        var on = Math.Abs(duty) > ((_pass + _phase) % 8) / 8.0;
        var right = on && duty > 0;
        var left = on && duty < 0;
        // THE BOOST in a run of jumps while under its speed, wheels on the road: a run of stones costs speed at each flight and
        // landing, more than the engine alone gives back between them. A race's fuel is short (about 50 units, one a 19
        // passes): none in the air, where it pushes nothing and still burns; none pointing off the road's way (out of a
        // corner crooked, boosted, a car crossed the road and off it)
        var grounded = me.W(0x4C) - surface.Under(x, z, me.W(0x4C), -1000) <= GroundedBy;
        // a single jump's boost keeps a reserve for the runs of jumps still to come (RunCost a lap left)
        var lapsLeft = Math.Clamp(ScrRoad.Laps - drive.Lap + 1, 1, ScrRoad.Laps);
        // up the whole climb to a big jump (BigClimbs), while the tank holds ClimbCost a lap still to come after this one
        var climb = BigClimbs(road)[strip] && car.B(ScrCar.boostFuelLevel) > ClimbCost * (lapsLeft - 1);
        var single = (JumpAhead(road, strip) || climb) && (_inRun.Count == 0 || car.B(ScrCar.boostFuelLevel) > RunCost * lapsLeft);
        var boost = (_inRun.Contains(piece) || single) && grounded && up && speed < limit && Math.Abs((int)off) < BoostAligned;
        return ((byte)((down ? 1 : up ? 2 : 0) | (right ? 4 : 0) | (left ? 8 : 0)), boost);
    }
}
