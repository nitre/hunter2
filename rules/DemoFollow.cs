namespace Hunter.Rules;

/// <summary>ENHANCEMENT, NOT THE ORIGINAL (owner 2026-11-12: "We should also be able to upgrade the attract demo there's no
/// reason to be tethered" - "it doesn't matter if anything changes slightly, it's the key things that matter"): the attract
/// demo played by following the plain demo's route instead of replaying its stick blindly. The recording (file 11) steers a
/// soldier only while the world is the one it was made in; with any change (finer ground, other rules) he loses his way at
/// once and the rest of the recording plays at nothing. Here the plain demo is played once (Track) and each counted blank:
/// - he stands exactly where the plain demo had him (place, heading, speed, what he is in): the recording's own stick, fire
///   and keys for that blank - with nothing changed the demo is the original, byte for byte;
/// - otherwise the bot's body (Body.Stick) steers him toward the route a little ahead, the recording's fire kept;
/// - the key moments wait for him: getting into or out of a vehicle (what he is in changes) and every key the recording
///   presses (the key goes in once he is near where the plain demo pressed it).
/// The route's time runs on while he keeps up, waits while he is far behind, jumps on when he is ahead. The demo ends at the
/// route's end, or when he has been stuck on one stretch too long (dead, blocked).</summary>
public sealed class DemoFollow
{
    /// <summary>The plain demo, a counted blank at a time: the player as that blank began (place, heading, pitch, speed, type)
    /// and the recording's stick, fire and keys for it.</summary>
    /// Over: how high the player was above the plain demo's OWN ground there (its world's Ground.Height) - the coarse
    /// logic the follower flies by over any ground, finer too.
    public sealed record Track(int[] X, int[] Y, int[] Z, byte[] Heading, byte[] Pitch, short[] Speed, short[] Type,
        byte[] D6, byte[] D4, byte[][] Keys, bool[] Grounded, int[] AimX, int[] AimZ, int[] Over)
    {
        public int Count => X.Length;
    }

    static Track? cached;

    /// <summary>The plain demo played once (Action game, default soldier, as RunDemo $22D6 and DemoRoute) with a recorder in
    /// the follower's place. Pure: the recording steers him, so the track is the same every time; kept after the first ask.</summary>
    public static Track Plain(GameData d)
    {
        if (cached is not null) return cached;
        var play = new Play(d, GameMode.Action, soldier: StartState.DefaultSoldier(d));
        Demo.Start(play.State);
        var rec = new Recorder();
        play.Follow = rec;
        for (var pass = 0; !play.Quit && pass < 40000; pass++)
        {
            play.VerticalBlank(default);
            play.Frame();
        }
        return cached = rec.Done();
    }

    /// <summary>A counted blank of the demo: the follower's stick, fire and keys (Demo.Replay's place in Play.VerticalBlank).</summary>
    public interface IBlank { void Blank(Play play, ref byte d6, ref byte d4); }

    sealed class Recorder : IBlank
    {
        readonly List<int> x = [], y = [], z = [];
        readonly List<byte> heading = [], pitch = [], d6s = [], d4s = [];
        readonly List<short> speed = [], type = [];
        readonly List<byte[]> keys = [];
        readonly List<bool> grounded = [];
        readonly List<int> aimX = [], aimZ = [], over = [];

        public void Blank(Play play, ref byte d6, ref byte d4)
        {
            var me = play.State.Entities.All[0];
            x.Add(me.UW(0x4A)); y.Add(me.W(0x4C)); z.Add(me.UW(0x4E));
            over.Add(me.W(0x4C) - play.State.Ground.Height(me.UW(0x4A), me.UW(0x4E)));
            heading.Add(me.B[0x7E]); pitch.Add(me.B[0x7C]); speed.Add(me.W(0x3E)); type.Add(me.W(4)); grounded.Add(me.B[0x1E] != 0);
            var k = new List<byte>();
            Demo.Replay(play, ref d6, ref d4, k);
            d6s.Add(d6); d4s.Add(d4); keys.Add([.. k]);
            var mark = (d4 & 1) != 0 ? AimedAt(play.State, me.UW(0x4A), me.UW(0x4E), me.B[0x7E]) : null;
            aimX.Add(mark?.UW(0x4A) ?? -1); aimZ.Add(mark?.UW(0x4E) ?? -1);
        }

        public Track Done() => new([.. x], [.. y], [.. z], [.. heading], [.. pitch], [.. speed], [.. type], [.. d6s], [.. d4s], [.. keys], [.. grounded], [.. aimX], [.. aimZ], [.. over]);
    }

    /// <summary>What a shot from (x, z) along a heading is at: the nearest live thing (not the player, not a shot) within
    /// AimNear steps of the heading, within AimFar.</summary>
    static Entity? AimedAt(WorldState s, int x, int z, byte heading)
    {
        Entity? best = null; var bestD = AimFar;
        for (var i = 1; i < s.Entities.Count; i++)
        {
            var e = s.Entities.All[i];
            if (!e.Alive || Ground.IsShot(e)) continue;                          // (its own shell in flight is nearest on the line)
            int ex = e.UW(0x4A), ez = e.UW(0x4E), d = Follower.Distance(x, z, ex, ez);
            if (d >= bestD || d == 0) continue;
            if (Math.Abs((int)(sbyte)(Wish.Toward(x, z, ex, ez) - heading)) > AimNear) continue;
            (best, bestD) = (e, d);
        }
        return best;
    }
    const int AimNear = 6, AimFar = 3000;

    /// <summary>The follower playing one demo.</summary>
    public sealed class Follower(Track t) : IBlank
    {
        /// <summary>Blanks of route ahead he steers for.</summary>
        public const int Ahead = 24;
        /// <summary>Farther than this from the route's place, its time waits for him.</summary>
        public const int Behind = 384;
        /// <summary>A key goes in within this of where the plain demo pressed it.</summary>
        public const int KeyNear = 160;
        /// <summary>Counted blanks a key waits for him to come near; then it goes in where he is (a parked car may stand
        /// farther off than that, and the route keeps it standing).</summary>
        public const int KeyWait = 150;
        /// <summary>How far ahead (blanks of route) he may be found and the time jump on to him.</summary>
        public const int Look = 256;
        /// <summary>Counted blanks on one stretch before the demo gives up and ends.</summary>
        public const int Stuck = 1500;

        int j, held = -1, still, blanks;
        /// <summary>Route blanks he may be ahead of his own count of blanks (the plain demo's clock) before he waits.</summary>
        public const int Early = Ahead;

        /// <summary>The route blank he is at (for tests: how far he got).</summary>
        public int At => j;
        /// <summary>Blanks the recording's own stick was used / the follower steered (for tests).</summary>
        public int Recorded { get; private set; }
        public int Steered { get; private set; }
        /// <summary>Key moments passed, in order (route blank), for tests.</summary>
        public List<int> Moments { get; } = [];

        bool Moment(int k) => t.Keys[k].Length > 0 || (k > 0 && t.Type[k] != t.Type[k - 1]);

        int NextMoment(int from)
        {
            for (var k = from; k < t.Count; k++) if (Moment(k)) return k;
            return t.Count - 1;
        }

        static int Dist(int ax, int az, int bx, int bz) => Math.Max(Math.Abs(Senses.Diff(ax, bx)), Math.Abs(Senses.Diff(az, bz)));

        public void Blank(Play play, ref byte d6, ref byte d4)
        {
            var s = play.State;
            var me = s.Entities.All[0];
            blanks++;
            if (j >= t.Count || ++still > Stuck) { d6 = 0; d4 = 0; Keys.QuitKey(s); return; }
            if (wasType == t.Type[0] && me.W(4) != wasType) rides.Add(me.W(4));  // walked onto it: one he gets into
            wasType = me.W(4);
            int mx = me.UW(0x4A), mz = me.UW(0x4E);
            int D(int k) => Dist(mx, mz, t.X[k], t.Z[k]);

            if (me.B[0x7E] == t.Heading[j] && me.W(0x3E) == t.Speed[j] && me.W(4) == t.Type[j] && mx == t.X[j] && mz == t.Z[j]
                && me.W(0x4C) == t.Y[j] && me.B[0x7C] == t.Pitch[j])
            {                                                                    // exactly where the plain demo had him: its own blank
                foreach (var k in t.Keys[j]) Keys.Down(play, k, false, false, 0);
                d6 = t.D6[j]; d4 = t.D4[j];
                if (Moment(j)) Moments.Add(j);
                Advance(); Recorded++; held = -1;
                return;
            }

            var toward = Reconcile(play, me);
            if (j >= t.Count) { d6 = 0; d4 = 0; return; }
            var next = NextMoment(j);
            if (!Moment(j))                                                      // between moments: catch the route's time up to him
            {
                var (best, bestD) = (j, D(j));
                for (var k = j + 1; k <= Math.Min(next, j + Look); k++)
                    if (D(k) < bestD) (best, bestD) = (k, D(k));
                if (best > j) { j = best; still = 0; }
                next = NextMoment(j);
            }
            var early = j - blanks > Early && !Body.Flies(me) && me.B[0x1E] != 0;   // on the ground ahead of the plain demo's clock: he waits
            // (the world runs on that clock - 127 blanks ahead, a gun's shot by the rocket launcher at 36866,21682 burst on his
            // tank where the plain demo's passed before it; in the air he flies, falls on: waiting there is coming down, and
            // held as he fell, the parachute's moment at 6565 went by)
            var airborne = Body.Flies(me) && (me.B[0x1E] == 0 || me.W(0x3E) != 0) && t.Grounded[j] && t.Keys[j].Contains((byte)0x40);
            // out of a landed helicopter: landed and its rotor stopped first, as the route's (not a jump; touching down for a blank
            // at speed 36 he stepped out and it was gone)
            if (early) still = 0;                                                // waiting on the clock is not being stuck
            else if (Moment(j) && me.W(4) == t.Type[j] && !airborne && (t.Keys[j].Length == 0 || D(j) <= KeyNear || still > KeyWait))
            {                                                                    // the moment met: its keys, and on
                foreach (var k in t.Keys[j]) Keys.Down(play, k, false, false, 0);
                Moments.Add(j);
                Advance();
                next = NextMoment(j < t.Count ? j : t.Count - 1);
            }
            else if (!Moment(j) && D(j) <= Behind) Advance();                    // keeping up: the route's time runs on
            var at = Math.Min(j, t.Count - 1);
            var aim = Math.Min(at + Ahead, Math.Max(next, at));
            var onFoot = me.Bit(0x08, 15);                                       // $25EA: +$08 bit 15
            int ax = t.X[aim], az = t.Z[aim];
            if (toward is var (tx, tz)) (ax, az) = (tx, tz);                     // back to what the route rides
            else if (aim == next && Boards(next) && me.W(4) != t.Type[next] && Nearest(s, t.Type[next], ax, az) is { } v)
                (ax, az) = (v.UW(0x4A), v.UW(0x4E));                             // getting in: to the vehicle itself, where it stands
            KeyOut(play, me, ax, az);
            var go = t.Speed[at] != 0 || (onFoot && Dist(mx, mz, ax, az) > 24)   // a vehicle stands while the route's does,
                || (wrongRide && !Body.Flies(me) && Dist(mx, mz, ax, az) >= RideNear);   // the wrong one drives on to where he gets out
            if (wrongRide && Body.Flies(me)) go = false;                         // (a helicopter lands where it is first)
            // (where his stands need not be where the plain demo's stood: chasing that spot, a car can only circle)
            var stopping = wrongRide && !Body.Flies(me) && Dist(mx, mz, ax, az) < RideNear;
            if (early && toward is null && !wrongRide) go = false;
            if (stopping) go = false;                                            // near where he gets out: stopped first, on its own
            // heading (another one makes a standing car creep to turn, never 0) - left at speed 9 on finer ground, an empty car
            // kept its speed and turn and drove a ring 350 across; he walked after it to the stuck limit (route blank 1151)
            var alt = t.Y[aim];
            if (Body.Flies(me))                                                  // on another line than the route's: no lower over the
            {                                                                    // ground ahead than the route was over its own (a hillside
                // the route's height over ITS OWN ground (Track.Over, the plain demo's world), not over his: on finer ground
                // the plain helicopter's 831 at its landing was under his ground (1404) - "over" read 0, he flew for a height
                // under his hills and wandered off the line, 326 blanks slower on the first leg, and ran dry in the second
                var over = Math.Clamp(t.Over[aim], 0, Hunt.Clearance);           // took the
                if (Dist(mx, mz, t.X[aim], t.Z[aim]) > Behind) over = Hunt.Clearance;   // far from it: over the way at the cruise's
                // (landed on a slope 400 off the route's hilltop, its 60 over his ground was the hover: it hopped there to the end)
                alt = over < Hunt.Clearance                                      // tilt demo's at 1537); low (landing): the route's height
                    ? s.Ground.Height((ushort)mx, (ushort)mz) + over             // over HIS ground (landing on a slope 50 above the route's
                    : Math.Max(alt, Hunt.GroundAhead(s, me, t.X[aim], t.Z[aim]) + over);   // spot, it came down hard and was wrecked)
            }
            var way = Wish.Toward(mx, mz, ax, az);
            if (onFoot && go) way = Around(s, me, way, toward is not null ? t.Type[at] : Boards(next) && aim == next ? t.Type[next] : (short)-1);
            else if (go && !onFoot && !Body.Flies(me) && me.W(4) == t.Type[at]) way = NoRam(s, me, way);
            var fire = (t.D4[at] & 1) != 0 && (!Body.Flies(me) || Math.Abs((sbyte)t.Pitch[at] - (sbyte)me.B[0x7C]) <= 1);
            // in a helicopter the recording's fire goes in only on blanks its pitch is where the route's is: fire with the stick up /
            // down is the throttle ($3662), so Body.Fly leaves the pitch alone while fire is held - the recording's long fire,
            // handed in every blank, kept a combined run's pitch tipped back and it flew away backwards (5881); the blanks between
            // put the pitch right
            byte? shotAt = null;                                                 // a vehicle's shot at where the plain demo's was aimed
            if (fire && !onFoot && !Body.Flies(me) && t.AimX[at] >= 0)     // in its own world (from his place, not along the
            {                                                                    // route's heading: 150 off it, the shot at the rocket
                shotAt = Wish.Toward(mx, mz, t.AimX[at], t.AimZ[at]);            // launcher at 36866,21682 missed and it wrecked his
                fire = Math.Abs((int)(sbyte)(shotAt.Value - me.B[0x7E])) <= 2;   // tank; picked in his own world, the wild one's beasts
            }                                                                    // in front of it were the mark)
            var w = new Wish(go, shotAt ?? (go ? way : stopping ? me.B[0x7E] : t.Heading[at]), fire,
                Hold: !go, Alt: alt, Tilt: (sbyte)t.Pitch[at], Travel: true, Thrust: t.Speed[at]);
            if (!onFoot && !Body.Flies(me) && me.W(4) == t.Type[at] && Gun(s, me, t.X[aim], t.Z[aim]) is { } g) w = g;
            var stick = Body.Stick(play, w, ref held);
            d6 = stick.D6; d4 = (byte)(stick.Fire ? 1 : 0);
            Steered++;
        }

        void Advance() { j++; still = 0; }

        /// <summary>Kinds he gets into by walking onto them: the route's own, and any he has found himself in.</summary>
        readonly HashSet<short> rides = [.. t.Type.Distinct()];
        short wasType = -1;
        public const int Clear = 250;
        /// <summary>Within this of him things are surely live (in Entities); farther, a missing one may only be stored.</summary>
        public const int Live = 1000;
        /// <summary>Got in this few route blanks before the route does, he stays in (the boarding is that close).</summary>
        public const int EarlyNear = 2 * Ahead;

        /// <summary>On foot, the way past things he would get into and is not going for: one within Clear whose box his way
        /// passes through - touching it gets him in (both boxes, +$6E / +$72, Collisions $41xx) - he goes along its edge, the
        /// side his way leans: the bearing to it, turned by the angle its box takes at that distance (corner to middle) and
        /// Pad more. (By bearing alone, less than 48 off: a helicopter 254 east of him, exactly 48 off his way, got him back in
        /// at its corner - out again, back in, to the stuck limit.)</summary>
        byte Around(WorldState s, Entity me, byte way, short goingFor)
        {
            int mx = me.UW(0x4A), mz = me.UW(0x4E);
            for (var i = 1; i < s.Entities.Count; i++)
            {
                var e = s.Entities.All[i];
                var type = e.W(4);
                if (!e.Alive || type == goingFor || type == t.Type[0] || !rides.Contains(type)) continue;
                int ex = e.UW(0x4A), ez = e.UW(0x4E);
                if (Dist(mx, mz, ex, ez) > Clear) continue;
                double dx = (short)Senses.Diff(ex, mx), dz = (short)Senses.Diff(ez, mz);   // heading 0 = +z, $40 = +x
                double r = way * Math.PI / 128, ahead = dx * Math.Sin(r) + dz * Math.Cos(r), side = dx * Math.Cos(r) - dz * Math.Sin(r);
                var reach = Math.Sqrt(Math.Pow(me.W(0x6E) + e.W(0x6E), 2) + Math.Pow(me.W(0x72) + e.W(0x72), 2));
                if (ahead <= 0 || Math.Abs(side) >= reach) continue;              // not in his way
                var d = Math.Sqrt(dx * dx + dz * dz);
                var turn = (d > reach ? Math.Asin(reach / d) * 128 / Math.PI : 64) + Pad;
                var at = Wish.Toward(mx, mz, ex, ez);
                return (byte)Math.Round((sbyte)(at - way) >= 0 ? at - turn : at + turn);
            }
            return way;
        }
        const int Pad = 6;

        /// <summary>In a land vehicle, the way past an armed thing (+$26 a weapon, +$32 rounds) that ramming would cost him
        /// dear: a ram costs each the other's hit points (+$2A, $455C) - at least half his. Its box in his way within Clear: along
        /// its edge, as Around. Not the kinds he rides (an empty tank is armed). Owner 11-12: getting past the rocket launcher
        /// alive is enough - the plain demo's kills it by chance (its own shell, at 18 after a hit in its own world); the
        /// straight demo's tank, 46, rammed it at 30 and its shell finished him.</summary>
        /// <summary>Owner 11-12: "don't ram if shoot is fine". An armed tank (turns on the spot, a weapon, rounds - Hunt.ArmedTank)
        /// meeting an enemy it cannot wreck by ramming and live (FoeInWay) shoots it as the bot does (Hunt.Ride): stopped at its
        /// standoff (Hunt.Standoff + its half size, 200 of play), turned on the spot to its bearing, firing once on it within the
        /// angle its box takes. Given up after Hunt.Patience blanks of firing with it none the worse - then round it (NoRam).
        /// Not seek and destroy: only one near him that the route's way ahead (aim) comes by.</summary>
        Wish? Gun(WorldState s, Entity me, int ax, int az)
        {
            if (!me.Bit(0x08, 13) || me.W(0x26) <= 0 || me.W(0x32) <= 0) return null;   // a tank (bit 13; not the boats, bit 10: the rocket boat stood shooting a $92 at 3464)
            int mx = me.UW(0x4A), mz = me.UW(0x4E);
            Entity? foe = null; double fd = 0; var half = 0;
            for (var i = 1; i < s.Entities.Count; i++)
            {
                var e = s.Entities.All[i];
                if (!Enemy(e) || e.W(0x36) != 0 || givenUp.Contains(i)) continue;   // one that stays put: top speed (+$36) 0 - the
                // launcher (the mover bit, +$06 27, it has: it turns); a soldier's is 2
                int ex = e.UW(0x4A), ez = e.UW(0x4E), h = Math.Max(Math.Abs((int)e.W(0x6E)), Math.Abs((int)e.W(0x72)));
                var d = Math.Sqrt(Math.Pow(Senses.Diff(ex, mx), 2) + Math.Pow(Senses.Diff(ez, mz), 2));
                var near = Math.Sqrt(Math.Pow(Senses.Diff(ex, ax), 2) + Math.Pow(Senses.Diff(ez, az), 2));
                if (d > Hunt.Standoff + h + 200 || near > Hunt.Standoff + h + 200) continue;
                double wx = Senses.Diff(ax, mx), wz = Senses.Diff(az, mz), len = Math.Max(1, Math.Sqrt(wx * wx + wz * wz));
                double along = (Senses.Diff(ex, mx) * wx + Senses.Diff(ez, mz) * wz) / len, across = Math.Abs(Senses.Diff(ex, mx) * wz - Senses.Diff(ez, mz) * wx) / len;
                var reach = Math.Sqrt(Math.Pow(me.W(0x6E) + e.W(0x6E), 2) + Math.Pow(me.W(0x72) + e.W(0x72), 2));
                if (along <= 0 || across > reach + InWay) continue;              // not on his way: left alone (the car stood shooting a
                // $92 near the route at 3427 and the plane's moment at 6545 went by - not seek and destroy, owner 11-12)
                if (foe is null || d < fd) (foe, fd, half) = (e, d, h);
            }
            if (foe is null) { gunAt = -1; return null; }
            var slot = Array.IndexOf(s.Entities.All, foe);
            if (slot != gunAt || foe.W(0x2A) < gunHealth) (gunAt, gunHealth, gunning) = (slot, foe.W(0x2A), 0);   // a new one, or hurt
            if (++gunning > Hunt.Patience) { givenUp.Add(slot); gunAt = -1; return null; }   // shooting does it no good: round it (NoRam)
            var bearing = Wish.Toward(mx, mz, foe.UW(0x4A), foe.UW(0x4E));
            var tol = Math.Max(1, (int)(Math.Atan2(half, fd) * 128 / Math.PI) / 2);
            var on = Math.Abs((int)(sbyte)(bearing - me.B[0x7E])) <= tol && me.W(0x3E) == 0;
            // a shell flies straight along his pitch (the tilt-demo's climbed 20 a 125 along): where it starts ($30D2: +$64 + +$4C +
            // +$7A) and its climb at the foe's distance against the foe's box (Collisions.Overlap's height test); too high (on a
            // slope tipped up, every shell from 591 off passed over the launcher) - closer, no nearer than the bot's half + 150
            var box = Arms.ShotBox(s, me.W(0x26) is var st and > 0 ? st : 0x59);
            var shellY = me.W(0x64) + me.W(0x4C) + me.W(0x7A) - Math.Tan((sbyte)me.B[0x7C] * Math.PI / 128) * fd;
            var level = Math.Abs(shellY + box.H - (foe.W(0x4C) + foe.W(0x70))) <= foe.W(0x70) + box.H;
            if (!level && fd > half + Near) return new Wish(true, bearing, false, Travel: true);
            return new Wish(false, bearing, on && level, Hold: true, Travel: true);
        }
        int gunAt = -1, gunHealth, gunning;
        /// <summary>Past the two boxes, how far off his way an enemy still counts as in it.</summary>
        const int InWay = 150;
        /// <summary>Past its half size, the nearest he closes on one he shoots (the bot's: Hunt.Ride, half + 150). 250 - clear of
        /// where the launcher's own shells start, 184 off it - measured worse: shell and straight lost the tank.</summary>
        const int Near = 150;
        readonly HashSet<int> givenUp = [];

        /// <summary>An enemy: alive, worth credits wrecked (+$96, "destroyed a X for N credits" - a rabbit's are less than 0),
        /// not a shot, not a kind he rides.</summary>
        bool Enemy(Entity e) => e.Alive && !Ground.IsShot(e) && !rides.Contains(e.W(4)) && e.W(0x96) > 0;

        byte NoRam(WorldState s, Entity me, byte way)
        {
            int mx = me.UW(0x4A), mz = me.UW(0x4E), mine = me.W(0x2A);
            for (var i = 1; i < s.Entities.Count; i++)
            {
                var e = s.Entities.All[i];
                if (!Enemy(e) || e.W(0x36) != 0 && e.W(0x2A) < me.W(0x2A)) continue;   // a moving enemy it can wreck: through it; one it
                // cannot, or one that stands (shot at, or given up): round it - the launcher, 30 to his 46, rammed: its shell finished him
                int ex = e.UW(0x4A), ez = e.UW(0x4E);
                if (Dist(mx, mz, ex, ez) > Clear) continue;
                double dx = (short)Senses.Diff(ex, mx), dz = (short)Senses.Diff(ez, mz);
                double r = way * Math.PI / 128, ahead = dx * Math.Sin(r) + dz * Math.Cos(r), side = dx * Math.Cos(r) - dz * Math.Sin(r);
                var reach = Math.Sqrt(Math.Pow(me.W(0x6E) + e.W(0x6E), 2) + Math.Pow(me.W(0x72) + e.W(0x72), 2));
                if (ahead <= 0 || Math.Abs(side) >= reach) continue;
                var d = Math.Sqrt(dx * dx + dz * dz);
                var turn = (d > reach ? Math.Asin(reach / d) * 128 / Math.PI : 64) + Pad;
                var at = Wish.Toward(mx, mz, ex, ez);
                return (byte)Math.Round((sbyte)(at - way) >= 0 ? at - turn : at + turn);
            }
            return way;
        }

        internal static int Distance(int ax, int az, int bx, int bz) => Dist(ax, az, bx, bz);

        /// <summary>The route gets into a vehicle at k (on foot before, in it from k).</summary>
        bool Boards(int k) => k > 0 && k < t.Count && t.Type[k] != t.Type[k - 1] && t.Type[k - 1] == t.Type[0];

        /// <summary>The live thing of a type nearest (x, z), within 2000.</summary>
        static Entity? Nearest(WorldState s, short type, int x, int z, int within = 2000)
        {
            Entity? best = null; var bestD = within;
            for (var i = 1; i < s.Entities.Count; i++)
            {
                var e = s.Entities.All[i];
                if (!e.Alive || e.W(4) != type) continue;
                var d = Dist(x, z, e.UW(0x4A), e.UW(0x4E));
                if (d < bestD) (best, bestD) = (e, d);
            }
            return best;
        }

        /// <summary>He is in something else than the route has him in (what he rides was lost, or he got in on the way):
        /// on foot while the route rides - on to where it is on foot again (the ride's keys go with it); in a vehicle the
        /// route gets into later - on to that; in one the route has left (and not the falling, the parachute: never got
        /// into from the ground) - the key out (space, $40, as the recording's).</summary>
        (int X, int Z)? Reconcile(Play play, Entity me)
        {
            var s = play.State;
            short mine = me.W(4), foot = t.Type[0];
            wrongRide = false;
            if (mine == t.Type[j] || !me.Alive) { off = 0; return null; }
            off++;
            int mx = me.UW(0x4A), mz = me.UW(0x4E);
            if (mine == foot)
            {
                var near = Boards(j) ? Nearest(s, t.Type[j], t.X[j], t.Z[j]) : Nearest(s, t.Type[j], mx, mz, 1000);
                if (near is not null) return (near.UW(0x4A), near.UW(0x4E));    // getting in, or back in: to it, where it stands
                if (Boards(j) && Dist(mx, mz, t.X[j], t.Z[j]) > Live) return null;   // far off: not live yet (only things near him are) - on there
                var k = j;                                                       // none to get into (lost, wrecked): on to where the
                while (k < t.Count && t.Type[k] != foot) k++;                    // route is on foot again, the ride's keys with it
                if (k > j) { j = k; still = 0; }
                return null;
            }
            for (var k = j; k < t.Count; k++)
                if (Boards(k))
                {
                    if (t.Type[k] == mine && t.Type[j] == foot && k - j <= EarlyNear) { j = k; still = 0; return null; }
                    // in early, the boarding just ahead: on to it. Farther, he gets out and walks the route's way: jumping on to a
                    // boarding far ahead skipped what the plain demo did meanwhile (two Return keys and a walk) and put him 1100
                    // blanks ahead of its time - a gun's shell then burst on his tank where the plain demo's passed unharmed
                    break;
                }
            wrongRide = Enumerable.Range(1, t.Count - 1).Any(k => Boards(k) && t.Type[k] == mine) || !Falling(mine);
            return null;
        }
        /// <summary>This blank he is in a vehicle the route does not have him in (and can get out of).</summary>
        bool wrongRide;
        (int X, int Z) lastAt;
        int stalled;

        /// <summary>In the wrong vehicle: a land one he drives on toward where the route goes and gets out near it (within
        /// RideNear, stopped: an empty car keeps its speed and turn) or once it stands (Stalled blanks unmoved: a shore, a
        /// wall); out of one that flies at once. The key out (space, $40, as the recording's) after KeyOutAfter blanks, then
        /// every KeyOutEvery.</summary>
        void KeyOut(Play play, Entity me, int ax, int az)
        {
            int mx = me.UW(0x4A), mz = me.UW(0x4E);
            if ((mx, mz) == lastAt) stalled++; else (lastAt, stalled) = ((mx, mz), 0);
            sincePress++;
            if (!wrongRide || off <= KeyOutAfter || sincePress < KeyOutEvery) return;
            var down = Body.Flies(me) && me.B[0x1E] != 0 && me.W(0x3E) == 0;   // a helicopter: landed, the rotor stopped (Body.Fly)
            if (down || !Body.Flies(me) && (Dist(mx, mz, ax, az) < RideNear && me.W(0x3E) == 0 || stalled > Stalled)) { Keys.Down(play, 0x40, false, false, 0); sincePress = 0; }
        }
        int sincePress = KeyOutEvery;
        const int RideNear = 300, Stalled = 60;
        /// <summary>Counted blanks he has been in something else than the route has him in.</summary>
        int off;
        /// <summary>The key out goes in after this many blanks in the wrong thing, then every KeyOutEvery (one already under way,
        /// pressed again, put him back in).</summary>
        const int KeyOutAfter = 50, KeyOutEvery = 100;

        /// <summary>What the route rides only after leaving a vehicle in the air (the falling soldier, the parachute), never
        /// got into from the ground: no key out of it.</summary>
        bool Falling(short type)
        {
            for (var k = 1; k < t.Count; k++)
                if (t.Type[k] == type && t.Type[k - 1] != type) return t.Type[k - 1] != t.Type[0] && !Boards(k);
            return false;
        }
    }
}
