namespace Hunter.Rules;

/// <summary>Everything the 3D view needs from a running game.</summary>
public sealed class WorldState
{
    public required GameMode Mode { get; init; }
    public required Terrain Terrain { get; set; }
    public required Ground Ground { get; set; }
    public required EntityTable Entities { get; init; }
    /// <summary>$83BA: 2048 world records of 16 bytes (16 x 16 cells x 8 slots); spawning clears them.</summary>
    public required byte[] Grid { get; set; }
    /// <summary>$577E animation table (see Objects).</summary>
    public required byte[] Anim { get; init; }
    /// <summary>$1526: camera distance behind the player; $152A target, $152E step per VBL ($3578).</summary>
    public int Distance = 0x1000, Target = 0x1000, Step = 0x20;
    /// <summary>$1532: camera lift = distance >> 3, set each VBL by $3578.</summary>
    public short Lift;
    public Camera Camera = new();
    /// <summary>sinfile sine/cosine ($83DA/$83E2) for movement vectors ($2C00).</summary>
    public required Trig Trig { get; init; }
    /// <summary>$219: player status bits; movement toggles 5 (on land) / 6 (in water) ($2BAC).
    /// Per player (multiplayer piece 2): PlayerSetUpOrDie writes the player's own byte ($2E7A), Movement's
    /// walk/swim switch the moving player's ($2BF4). Blank ($B054) and Behaviour ($2744) still read
    /// player 0's through this property - later pieces (mp_reads, mp_ai) re-point them.</summary>
    public byte Status219 { get => _player[0].Status219; set => _player[0].Status219 = value; }
    /// <summary>$1AC: set to $206 when the player goes into the water ($2BDE).</summary>
    public short Timer1AC;
    /// <summary>$A5 bit 4: entities re-plan next pass ($2BF8 -> $25BC -> $26C0).</summary>
    public bool Replan;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (multiplayer piece 1): how many of the leading
    /// entity slots are reserved for players. 1 is the original - there the player is slot 0 only
    /// because it spawns first and SpawnInFreeSlot ($314C) takes the first free slot. Not saved,
    /// not a setting yet.</summary>
    public int Players = 1;
    /// <summary>Player slots are 0..Players-1; the ordinary records start at Players.</summary>
    public bool IsPlayerSlot(int n) => n < Players;
    /// <summary>The state one player owns (multiplayer piece 2) - the fields PlayerSetUpOrDie
    /// ($2E52) and the walk/swim switch ($2BAC) write for their player: $219, $228, $1536,
    /// $99 bit 4, and the ENH fuel low byte. The old WorldState names survive as properties
    /// over Player[0], so every reader and writer outside PlayerSetUpOrDie / Movement still touches
    /// player 0's values (mp_reads, mp_ai, mp_play re-point them later).</summary>
    public sealed class PlayerState
    {
        public byte Status219;                                       // $219 ($2E7A, $2BF4)
        public byte Fuel228;                                         // $228 ($2E56)
        public short Respawn1536;                                    // $1536 ($2ED2, $2EE0)
        public bool RespawnPending;                                  // $99 bit 4 ($2ED8, $2EE6)
        public byte FuelLow;                                         // the ENH extension's player fuel low byte
        /// <summary>The player's own item table (the world file's first $3E8 bytes, the 30
        /// items at +$28), masks $238/$23C and talk table $892E (mp_packs). Player 0's are the
        /// originals the WorldState properties point at; players 1.. get theirs in NewPack.</summary>
        public byte[] Items = [];
        public byte[] TalkTable = [];
        public int Items238, Items23C;
    }
    PlayerState[] _player = [new PlayerState()];
    /// <summary>Player n's state, growing the table as players are added (Players is set after
    /// the state is built). One player: always [0], the original exactly.</summary>
    public PlayerState PlayerAt(int n)
    {
        if (n >= _player.Length)
        {
            var grown = new PlayerState[n + 1];
            _player.CopyTo(grown, 0);
            for (var i = _player.Length; i <= n; i++) grown[i] = new PlayerState();
            _player = grown;
        }
        return _player[n];
    }
    /// <summary>The player number of a record: its slot. Only a player's record carries the
    /// player bit (d7 bit 31, set by $2E5A), and a player's record lives in its own reserved
    /// slot, so the slot index is the player's number.</summary>
    public int PlayerOf(Entity e) => Array.IndexOf(Entities.All, e);
    /// <summary>The world file's item table (held at $9E46 = file +$28) at its file offsets; from +$28 the 30 items, 32 bytes
    /// each (word +0 = count carried), set up by $9DEC. Per player (multiplayer piece 4): player 0's own table - the
    /// original array; players 1.. carry copies (NewPack). Readers elsewhere still go through player 0 here.</summary>
    public byte[] Items { get => _player[0].Items; set => _player[0].Items = value; }
    /// <summary>$151E camera zoom steps (-30..30, arrow keys); $1522 base distance ($2F56).</summary>
    public short Zoom;
    public int Base = 0x1000;
    /// <summary>Game clock ($C404): $1AE blanks to the next minute, $1B0 minute, $1B2 hour, $1B4 day.</summary>
    public short ClockWait, Minute, Hour, Day;
    /// <summary>$18F: the hour's light level 0-15, from the 24-byte table at $C4A4 (PanelHour $C466);
    /// the palette fades toward it ($68DC), 15 while a flare burns ($68E4).</summary>
    public byte DaylightLevel;
    public required GameData Data { get; init; }
    public required Models Models { get; init; }
    /// <summary>The soldier record ($85E2, 48 bytes with the player's record at +$20; Pregame.cs): SoldierToGlobals ($87A0) reads status,
    /// energy and score from it when a game starts, GameToSoldier ($87E4) writes them back.</summary>
    public Soldier Soldier = null!;
    /// <summary>The player's world record ($8602, 16 bytes): the game spawns the player from it ($87B6)
    /// and StoreEntityRecord ($82D0) writes the player back into it when play ends.</summary>
    public byte[] PlayerRecord = new byte[16];
    /// <summary>$9072: the 12 mission records of 16 bytes (a save holds them, save.md 3); $20A the
    /// current one's offset ($9134).</summary>
    public byte[] MissionTable = [];
    public short MissionNo;
    /// <summary>$1CA/$1CC sound channels (Sound.cs).</summary>
    public required Channels Channels { get; init; }
    /// <summary>$1AA: game minutes to the next ambient sound (AmbientSound $B08E).</summary>
    public short AmbientWait;
    /// <summary>$200 weapon in hand (item index), $202 the one held before getting into a vehicle.</summary>
    public short Weapon, WeaponBefore;
    /// <summary>$A4 bit 5 picks the soldier's variant in FootModels ($A72C); the variant cell
    /// ($A726 bchg) toggles it on every click.</summary>
    public byte Flags0A4;
    /// <summary>$9288: ends counted per type (EntityEnds $3078); $21A score.</summary>
    public readonly byte[] Kills = new byte[ExtraKinds.Slots];   // (the program's 256 bytes; ExtraKinds: every type)
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (Play.RoomyMemory): the extras of the roomy_memory
    /// setting, kept beside the original's arrays - the grid ($C000), Kills and the save block stay
    /// the original's layout and bytes. The defaults are the empty extras: nothing reads them while
    /// the setting is off, so every byte and every rule is the original's.</summary>
    public bool RoomyMemory;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (Play.MoreObjects, set with it): things woken only by the bigger window
    /// stand until they are in the original's (Behaviour.Steer).</summary>
    public bool MoreObjects;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (LIVING WORLD, owner 2026-11-01, rules/LivingWorld.cs): people and animals
    /// notice each other, not only the player. Off: every planner targets the player, as the original.</summary>
    public bool LivingWorld;
    /// <summary>LIVING WORLD: passes counted for its replans (LivingWorld.ReplanEvery). Not saved.</summary>
    public int LivingPass;
    /// <summary>LIVING WORLD: each map square's island or stretch of water (LivingWorld.RegionAt), built on first use.</summary>
    public int[]? LivingRegions;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (Play.Maths32, owner 2026-10-26 "32 it is"): the upgrade's maths in 32 bits -
    /// sums, differences and divides that the original wraps or overflows at 16 bits work in full. Off: every 16-bit wrap
    /// and divs overflow is the original's. Groups move onto it one at a time (docs/maths32.md); the stored words
    /// (entity records, the map byte, colours) stay the original's layout either way.</summary>
    public bool Maths32;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (FINER GROUND, owner 2026-11-01): the fine height store's spec - N points a
    /// cell side (0 = off, the original's ground) and the height factor FineNum / FineDen. Set at a new game (never the
    /// attract demo) or from a save's extension; the store itself (Ground.Fine) is rebuilt from it and the world's seed.</summary>
    public int FineN, FineNum = FineGround.FactorNum, FineDen = FineGround.FactorDen;
    /// <summary>ENHANCEMENT (GROUND SEED / RELIEF, owner 2026-11-11): the rest of the store's spec - the seed its detail
    /// was grown from (the world's own, or the one GROUND SEED drew), Vista's dimension and the hold (FineGround.Reliefs).
    /// With FineN: in the save's extension, so a load rebuilds the same store whatever the settings say then.</summary>
    public ushort FineSeed;
    public int FineDim = FineGround.Dimension, FineHold = FineGround.Gentle;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (owner 2026-10-30 "seems like a bug", on by default): moving along a heading
    /// rounds the sin/cos products toward 0 - the original's asr.l rounds a tiny negative cos($40) / sin($80) down to -1,
    /// so walking east also went 1 south a frame and south 1 west (docs/original-bugs.md). Off: the original's rounding.</summary>
    public bool StraightMoves;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (FINE POSITIONS, owner 11-12 "nyquist" item 3): a step along a heading keeps
    /// the fraction the shift drops (Movement.Vector's asr), carried on the entity (Entity.FracX..) to the next - so a
    /// pass a blank, 9 a step, goes where a 7-blank pass of 63 went (rounded each step it lost up to 1 a step an axis: a
    /// car 300 off the demo's road by blank 2000). Off: the original's whole units.</summary>
    public bool FinePositions;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (owner 2026-11-02, off by default): leaving a vehicle first tries the spot behind it
    /// (away from its heading), then the original's four. Off: the original puts you on its +x side first, so a boat heading
    /// that way runs into you and takes you back in (PlaceBeside).</summary>
    public bool LeaveBehind;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (owner 2026-11-03, off by default): a gamepad stick's sideways lean turns the player's
    /// vehicle by as much as it leans. AnalogLean is the shell's lean, -16..16 sixteenths of the full turn, set each blank (0 =
    /// keys / d-pad / no stick, or the flag off); +$18 stays the original's -1 / 0 / +1. MoveAndTurn reads both (slot 0 only).</summary>
    public bool AnalogSteering;
    public int AnalogLean;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (LIMB DAMAGE, owner 2026-11-03, off by default): a bullet hitting a person hurts the
    /// limb at the hit's height (Limbs.Classify); a leg or arm hit does not kill (at least 1 energy stays). Off: the original.</summary>
    public bool LimbDamage;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (BULLET BOX, owner 2026-11-09): bullets ($52, $5B) have no model, so SetCollisionBox
    /// ($334A) gives them model 0's box - 0 wide, 288 long north-south whatever way they fly: two on a north-south line jam
    /// each other, one born by a corner hits it. On: Arms.BulletHalf across both ways, the height kept (no aiming up or down
    /// on foot). Off: the original.</summary>
    public bool BulletBox;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (A500 PACE, owner 2026-11-08, on by default, never the attract demo): the game
    /// runs a pass every blank where the A500 ran one about every PaceBlanks; moves, turns, fuel and clocks scale by the
    /// blanks, but what the original does once a pass does not. On: those happen only on an A500 pass (PaceTick, every
    /// PaceBlanks blanks) - contact hurt (Collisions $454E, a ram $44FC), a record ending ($2646), the player's rebuild
    /// ($25C8), speed lost out of fuel or bumped, the bump's turn aside and +$1B; a landing's drop is measured from the
    /// height at the last A500 pass (Entity.PaceY). Off: the original's code at a pass a blank.</summary>
    public bool A500Pace;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (FINE ANGLES, owner 11-12, on with the setting, never the attract demo): a grounded
    /// thing's pitch from the exact heading and a blended height (Objects.FineTilt), kept fine. Off: the original's $42A6.</summary>
    public bool FineTilt;
    /// <summary>The A500's blanks a pass for A500 PACE (measured mean 6.3; the animations use the same, Play.AnimEveryBlanks).</summary>
    public const int PaceBlanks = 7;
    /// <summary>This pass is an A500 pass (always, A500 PACE off); PaceGathered: blanks since the last one; PaceBlank: blanks
    /// played; PaceHurts: when two slots last hurt each other (Collisions.Paced). Not saved.</summary>
    public bool PaceTick = true;
    public int PaceGathered, PaceBlank;
    public readonly Dictionary<(int, int), int> PaceHurts = [];
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (the BOT's PLOT ARMOUR, owner 2026-11-07, docs/bot-plan.md): in the bot's own films
    /// the player's soldier does not die - his record's energy is kept at 1 when it would end (Movement.Pass, $2646), the panel's
    /// energy at 1 (Vehicles.AddEnergy: no drowning), his own hard landing ends nothing (CrashDestroyEntity). He is still hit,
    /// so the bot flinches. Set only by whatever starts a bot film; never in a player's game; not saved.</summary>
    public bool PlotArmour;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (the BOT's death log, testing only - nothing in the game reads it, not saved): what
    /// last hurt the player's soldier (Collisions: the shot, blast or rammer's type, and for a shot its shooter's type, -1 when
    /// none), and how many times plot armour kept him alive, by what (Movement.Pass "hit", AddEnergy "drowning", the crash
    /// "fall"). Hunt reads them to write each death down.</summary>
    public int LastHurtBy = -1, LastHurtShooter = -1;
    public readonly List<string> ArmourSaves = [];
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (the BOT's hearing, docs/bot-brain.md 1): called with each message's template and
    /// arguments as ShowMessage prints it (Messages.Show, $C1B2) - the message line a player reads. Nothing in the game reads
    /// it; unset, nothing happens.</summary>
    public Action<int, int[]>? Heard;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (AIMING: RETICLE, owner 2026-11-03, off by default; play input, never the demo): on foot
    /// the player's soldier turns to the point under the reticle and his shots go there (rules/Aim.cs). ReticleX / Y are the
    /// reticle in game pixels over the view (set by the shell each blank); X below 0 = no reticle yet (the keys turn as ever).</summary>
    public bool AimReticle;
    public int ReticleX = -1, ReticleY;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (owner 2026-10-30, on by default): walking into the HQ (the goal building)
    /// before the mission is done says the debrief's "not yet completed" and puts you back outside its door - the game
    /// goes on. Off: the original's visit ends the game (GameOver $3514, the debrief, the briefing again).</summary>
    public bool EarlyHqBack;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (owner 2026-11-17: "Every boat you find is in parallel to the shore"; on by
    /// default, not in the attract demo): an empty sea-only boat coming to life from the map is turned along the shore
    /// (BoatShore.Heading), so it can be driven off as soon as it is boarded. Off: the record's own heading.</summary>
    public bool BoatsAlongShore;
    /// <summary>Maths32 group 2 (docs/maths32.md): the difference a-b of two position words (+$4A/+$4E)
    /// read as unsigned - the true int distance with Maths32 on (the map has a border, not a wrap, so
    /// things 32768+ apart ARE far); off, or with no game state (Collisions' renderer-only tests),
    /// the original's signed-word difference, wrapped past half the map.</summary>
    public static int Dist(WorldState? s, int a, int b) => s != null && s.Maths32 ? a - b : (short)(a - b);
    /// <summary>|Dist|: off, the original's word negate (sub.w, bpl, neg.w - things exactly 32768 apart stay -32768).</summary>
    public static int AbsDist(WorldState? s, int a, int b)
    {
        var d = Dist(s, a, b);
        return s != null && s.Maths32 ? Math.Abs(d) : (short)(d < 0 ? -d : d);
    }
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (Play.Graves): the graves of paid deaths, each holding
    /// the 30 carried counts of the death - out of play - beside the original's arrays, which stay
    /// the original's layout and bytes. The placed grave itself is an ordinary grid record; only the
    /// store (and the graves still waiting for a grid slot) rides the save's extension block. The
    /// defaults are the empty store: nothing reads it while the setting is off.</summary>
    public bool Graves;
    public readonly List<Grave> GraveStore = new();
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (Play.VultureGraves): the running grave visits -
    /// a new grave's vulture pair circling it for a while. Not in the save: a load drops the
    /// visit (like a debris shower). Nothing reads the list while the setting is off.</summary>
    public bool VultureGraves;
    public readonly List<VultureGraves.Post> VulturePosts = new();
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (multiplayer piece mp_view_angle): the second
    /// view's camera angle - 0 the original (facing north), $80 a half turn (facing south).
    /// Consumed at the two camera writers behind a guard (Ground.Place's depth term and clamp,
    /// Objects.Place's +$56/+$5A deltas): at 0 they run the original's exact instructions, so
    /// player 1's view - which never has an angle - stays byte for byte. Not in the save: no
    /// byte of it rides SaveGame, the saved block stays the original's.</summary>
    public byte ViewAngle2;
    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (real 3D): player 1's view turn as a heading offset
    /// (a byte, $100 = a full turn, clockwise like the heading; a left turn of the view is $100 minus
    /// its angle). Read by Controls only: on foot his stick walks where the turned view points.
    /// 0 = the original byte for byte. The old drawer never sees it.</summary>
    public byte ViewAngle1;
    /// <summary>The mode's own start record (the world file's 16 bytes at +$16 - StartState.For reads
    /// the player's from the same spot at a new game): the respawn target with graves on. Read from
    /// the mode's file on disk, not from the running PlayerRecord - a save overwrites that with the
    /// current player (SaveGame.Write WorldAt+0x16).</summary>
    public byte[] HomeRecord = new byte[16];
    /// <summary>The real counts (KillsWide[type]); Kills keeps the original's wrapped bytes beside
    /// them, untouched. Seeded from the bytes when the setting turns on (a save with no extension:
    /// the bytes are then the whole truth) or filled from a save's extension block.</summary>
    public readonly int[] KillsWide = new int[ExtraKinds.Slots];
    /// <summary>The wide counts are already the truth (seeded or from the extension): a second
    /// setting-on must not reset them over the bytes.</summary>
    public bool KillsWideLoaded;
    /// <summary>The live player's fuel low byte (+$35), kept by GameToSoldier ($82E6 keeps only the
    /// high byte in the record). A loaded game carries it back from the extension block.
    /// Per player (multiplayer piece 2); every save/load site touches player 0's - the per-player
    /// save format is the owner's question (GAPS).</summary>
    public byte PlayerFuelLow { get => _player[0].FuelLow; set => _player[0].FuelLow = value; }
    /// <summary>The parked things' fuel low bytes, by grid record offset ($82E6 writes only the high
    /// byte into the record; $8246 restores it << 8). An entry is consumed when the record spawns.</summary>
    public Dictionary<int, byte> FuelLow = new();
    /// <summary>roomy_memory: the fuel low byte of things left inside a building, by their record's offset in the
    /// building table (SaveInterior $4982 keeps only the high byte, as the grid's write-back does).</summary>
    public Dictionary<int, byte> BuildingFuelLow = new();
    /// <summary>The records written back into full cells ($830E leaves them live in the original),
    /// by cell base offset, each list in write-back order (oldest first). A record is 17 bytes: the
    /// 16 a grid slot holds plus its fuel low byte at +$10.</summary>
    public Dictionary<int, List<byte[]>> Overflow = new();
    public int Score;
    /// <summary>$228: the fuel byte the gauge last showed ($FF after PlayerSetUpOrDie); $A3 bit 5 the
    /// reserve flashing; $226 the rounds last shown. Per player (multiplayer piece 2): PlayerSetUpOrDie
    /// sets the player's own ($2E56), the panel redraw still goes through player 0 here (mp_reads).</summary>
    public byte Fuel228 { get => _player[0].Fuel228; set => _player[0].Fuel228 = value; }
    /// <summary>$24A/$24C/$24E: the next construct's owner (+$2C), speed added to its top speed,
    /// and +$21 (a player's shot) - set by FireShot, cleared after each construct ($32F0).</summary>
    public short Spawn24A, Spawn24C;
    public byte Spawn24E;
    /// <summary>$A2 timer bits ($6C46): bit 4 runs $222, the alarm's blanks ($A4 bit 1).</summary>
    public byte Flags0A2;
    /// <summary>$A5 bits 1/2: TouchItem's once-per-contact guard ($34F6, cleared by EndOfPass).</summary>
    public byte Flags0A5;
    /// <summary>$99 bit 4 / $1536: rebuild the player as this type next pass ($2E28); $99 bit 0
    /// inside a building's view; $18C blanks to the end of the game after the last death.</summary>
    public bool RespawnPending { get => _player[0].RespawnPending; set => _player[0].RespawnPending = value; }
    public bool InsideView;
    /// <summary>The last death ran $2F04: Play's next blank starts the death fade (colours only;
    /// skipped inside a building's view, $2F04 btst #0,$99).</summary>
    public bool DeathFadePending;
    /// <summary>$1554: the building in play (slot of its record; -1 none); $99 bit 1: a door asked
    /// to be visited at the end of the pass ($458C, EndOfPass $3476); $155E-$1568: the inside's
    /// bounds (x, y, z minima then maxima); $155C: the building's own model word while inside;
    /// $1558: its block in Buildings (offset, -1 none); $1518 / $151A: the inside camera's
    /// horizon height and the cache key of the backdrop drawn ($48BE); $1526 as it was outside.
    /// See Buildings.cs and re/formats/buildings.md.</summary>
    public int BuildingSlot = -1, ContainerAt = -1, SavedDistance;
    public bool VisitRequest;
    /// <summary>The store showing ($9A78): its number k, and END pressed ($9D bit 7 clear).</summary>
    public int StoreK;
    public bool StoreDone;
    public StorePicture? StorePicture;
    public readonly short[] Bounds = new short[6];
    public short SavedModel155C, Horizon1518;
    /// <summary>$216: the soldier's status word, low byte (bit 2 drowned, bit 7 dead).</summary>
    public byte Status216;
    /// <summary>$1536 per player (multiplayer piece 2): PlayerSetUpOrDie writes the dying player's own
    /// ($2ED2 / $2EE0); Respawn still reads player 0's here.</summary>
    public short Respawn1536 { get => _player[0].Respawn1536; set => _player[0].Respawn1536 = value; }
    public short Timer18C;
    /// <summary>$A7: what EndOfPass ($3476) does at the end of the pass: bit 0 the GIVE table, 1
    /// next weapon, 2 the map (drone), 3 a word (SpeakClick, a soldier's touch $A8DC), 4 the flare
    /// out, 5 the log book; bit 6 the panel clock shows its values (ClockToggle $A702, $C488).</summary>
    public byte Flags0A7;
    /// <summary>The screen routine EndOfPass jumped to this pass (MapScreen $3CFA), 0 if none:
    /// Play opens it (Screens.cs).</summary>
    public int ScreenRoutine;
    /// <summary>The world file's table after the grid (+$83E8, 128 blocks of $80, pointer $83C2).</summary>
    public byte[] Buildings = [];
    /// <summary>The log book's places and the marks bitmap (Screens.cs).</summary>
    public LogBook Log = null!;
    /// <summary>The talk table $892E-$8E97 (TalkRecord's code follows) (Talk.cs): gifts change +3. In the original it is the
    /// program's own data, never reset by a new game (talk.md 0); fresh per game here. Per player (multiplayer piece 4):
    /// player 0's goodwill table - the program data itself; players 1.. carry copies (NewPack).</summary>
    public byte[] TalkTable { get => _player[0].TalkTable; set => _player[0].TalkTable = value; }
    public bool Talk
    {
        get => (Flags0A7 & 8) != 0;
        set => Flags0A7 = (byte)(value ? Flags0A7 | 8 : Flags0A7 & ~8);
    }
    public bool ClockOn => (Flags0A7 & 0x40) != 0;
    /// <summary>$99 bit 7: play runs (set by MainLoop $2560 each pass, cleared by LeavePlay $2E08):
    /// the blank counts and does the game's work, keys reach ItemHotkeys; bit 5 the pause key
    /// (KeyDown $B258); bit 2 the map marks over the view (MapToggle $354A).</summary>
    public bool InPlay = true, PauseKey, MapMarks;
    /// <summary>$96 bit 7: a mouse press hit-tests the play regions (PlayStart $2550; cleared on
    /// leaving, $258E).</summary>
    public bool Clickable = true;
    /// <summary>$DE/$E0: the pointer; in play x 0-320 and the panel's row 0-60 ($60D8 limits from
    /// screen mode 3; the menu click's y, 80, is clamped to 60 by the blank $C77C).</summary>
    public short PointerX = 160, PointerY = 60;
    /// <summary>$240 the item a key or cell uses; $108 the region last hit; $104 a region's value.</summary>
    public short Item240, Region108;
    public int Choice104;
    /// <summary>$18A the flare's blanks, $188 the drone's ($A2 bits 0 / 1, BlankTimers $6C46); $184
    /// the flare's light (FireFlare $A79C; read by the palette code $6A56).</summary>
    public short Timer18A, Timer188;
    public int Light184;
    /// <summary>$238 / $23C: masks of the items carried / usable ($A2A8). Per player (multiplayer
    /// piece 4): player 0's masks; every player's own live in its PlayerState (Items.Carried).</summary>
    public int Items238 { get => _player[0].Items238; set => _player[0].Items238 = value; }
    public int Items23C { get => _player[0].Items23C; set => _player[0].Items23C = value; }
    /// <summary>$5A66: draws left of the one debris ($59FE sets 20, $5AE0 counts).</summary>
    public short Debris5A66;
    /// <summary>$5A68: the debris pieces' offsets, X[10] Z[10] Y[10] ($5AE0 adds $5AA4 each draw).</summary>
    public readonly short[] DebrisPos = new short[30];
    public short Timer222;
    /// <summary>$9B G.MenuFlags bit 7 - the demo is on (RunDemo sets it $22E4 and clears it $2302,
    /// the only place it is cleared): the blank counts only up to the pace word ($C6EC), the input
    /// replay applies records ($C706), a real key quits ($B1E6); ten sites test it in all
    /// (demo_map CHECKED.md).</summary>
    public bool DemoMode;
    /// <summary>$1A4 G.BlankCount (ResetGameState $2414 clears it): the counted blanks of this
    /// game - $C6FE stores it once per blank, capped in the demo at the pace word ($C6E6-$C6FC).</summary>
    public int BlankCount;
    /// <summary>$D2 G.DemoPaceCursor (ResetGameState $2418 = $F9C): signed bytes from the pace
    /// table's end to the word this game's passes wait on; $6226 subtracts 2 a pass and $622C
    /// quits the demo when it goes negative.</summary>
    public short DemoPaceCursor;
    /// <summary>ENHANCEMENT (rules/DemoFollow.cs): the followed demo has outlasted its pace table - blanks counted and passes
    /// run with no pace (Demo.CountBlank, PaceHeld). Never set in the original's replay, which quits there.</summary>
    public bool DemoUnpaced;
    /// <summary>$CE G.DemoInputPtr (ResetGameState $241E = the pointer at $832A, the demo file's
    /// record start): an offset into DemoBuffer where ReplayDemoInput $C826 stands.</summary>
    public int DemoInputPtr;
    /// <summary>The buffer the pointer at $832A holds: file 11 once RunDemo loads it ($22D8,
    /// Data.DemoFile()). Outside a demo nothing replays it (ReplayDemoInput runs only in the demo,
    /// $C706), so an empty buffer stands for whatever it held.</summary>
    public byte[] DemoBuffer = new byte[Demo.RecordsBytes + Demo.PaceBytes];
    // NOT THE GAME - copy protection, left out (owner 2026-10-17): $161A is the disk wiper's counter
    // (docs/cut-content.md "the disk wiper"; re/vars.txt DiskWiperCount), not a clock.
    // public int MusicClock161A;
    /// <summary>$22A: the soldier's energy ($87A0: word +6 of the soldier record $85E2, $20 for a
    /// new one); $A3 bit 6 low-energy warning.</summary>
    public short Energy = 0x20;
    /// <summary>$20A: the mission's offset in the table at $9072 (16 bytes a mission; $9134 picks the
    /// first of the game, NextMission $915A the next) - MissionNo by another name.</summary>
    public int Mission { get => MissionNo; set => MissionNo = (short)value; }
    /// <summary>$20D: a copy of byte +3 of the current mission's $9072 record, written by the tail
    /// NextMission and the first-mission scan share ($9176-$917C); the single write of $20D in the
    /// binary and nothing reads it back. The oracle records it as m20d.</summary>
    public int M20d => MissionTable[Mission + 3];
    /// <summary>$A6 MissionFlags: the debrief's MissionCheck ($9978, its only caller) clears bit 6
    /// and sets it while an objective is still open ($99B4/$99EA), MissionDeadline ($999A) sets bit 7
    /// past the deadline - and bit 6 with it ($998A runs on into $9990); GameSequence then clears bit 7 before it branches ($9708), so the byte the
    /// oracle records as mission_flags is bit 6 (0x40) alone - and 0 when death ($96FA), the traitor
    /// ($96F0) or a quit from the options ($9064) leaves before MissionCheck.</summary>
    public byte MissionFlags;
    public short Rounds226;
    /// <summary>$A3: the panel lamps that flash (bit 5 fuel reserve, 6 low energy; bit 7 set once
    /// any lamp has stepped); $220 blanks to the next step (BlankTimers $6C46).</summary>
    public byte Flags0A3;
    public short Timer220;
    /// <summary>$99 bit 3: the radar is on (RadarToggle $3540).</summary>
    public bool Radar;
    /// <summary>The panel buffer ($150) and its drawing (Panel.cs).</summary>
    public Panel Panel = null!;
    /// <summary>Player 2's own panel (ENHANCEMENT, mp_panel): a second buffer, updated each pass
    /// with his entity and n = 1; the right half's screen (Play.Screen2) shows its rows. Null with
    /// fewer than two players - the original's single panel byte for byte.</summary>
    public Panel? Panel2;

    /// <summary>The leftovers a game that follows this one inherits: $1AA and the channel cursor
    /// ($1CA). Machine state, not game state - a save does not hold it and no start-up clears it.</summary>
    public MachineLeftovers Leftovers => new(AmbientWait, Channels.Cursor, Kills[0xAE]);
}

/// <summary>The state the original never resets because it belongs to the machine, not to the game:
/// $1AA (G.AmbientTimer, AmbientSound $B08E) and $1CA (AllocChannel $2D30's channel cursor).
/// Neither is in the save block (save.md 3) and no start-up routine clears them, so a quit, a save
/// and a load all leave them alone: a game that follows another inherits them. A fresh boot finds
/// the program's own image zeroed, so a new game starts at 0 (StartState.For's leftovers).</summary>
/// KillTvRoom: the kill count of type $AE (the last one) - the save's CopyBytes $10658 copies 174 of the 175
/// kill bytes, so a load leaves that byte as the game before it left it (the clear $9134 runs only for a new game).
public readonly record struct MachineLeftovers(short AmbientWait, int Cursor, byte KillTvRoom = 0);

/// <summary>
/// A game as it starts: $235E loads the world (file 12 + mode, terrain from its seed), $24B4 resets
/// the animation table ($5762), $87B6 spawns the player from the world record at +$16 and places
/// the camera ($374C); then each frame the spawner $8186 brings in the records of the 2 x 2 grid
/// cells around the camera. See re/formats/renderer.md.
/// </summary>
public static class StartState
{
    /// <summary>The player starts (the survey's tables) and the multiplayer bases (docs/extras.md
    /// mp_start_spots) - the fuller world's keep-clear keeps everything off them: Fill's own
    /// 800 clear around every building and gun already covers the bases (they are buildings on
    /// the grid); this is the wider 4096 start radius that keeps sharks and gulls off a spawn.
    /// The same tables FillTests uses, so the tests' expectations are the game's.</summary>
    public static readonly IReadOnlyDictionary<GameMode, (int X, int Z)[]> StartSpots =
        new Dictionary<GameMode, (int X, int Z)[]>
        {
            [GameMode.Hunter] = [(61187, 19307)],
            [GameMode.Missions] = [(8280, 7543)],
            [GameMode.Action] = [(40526, 9935)],
        };
    /// <summary>mp_bases: moved OFF the buildings they stood on (the old spots were each exactly a
    /// building's own spot - a bungalow $9E for Hunter, sheds $99 for Missions and Action - so with
    /// the area live (mp_near) the building spawned on player 2 and the game walked him inside).
    /// Nearest clear dry ground in each base's own area: land, flat, outside every footprint,
    /// not on a full grid cell (MpBasesTests). matched_starts: moved NEXT to a shed cluster that
    /// matches player 1's own surroundings - a shed within 2048, a vehicle within 4000, no enemy
    /// within 4096, 8192+ from player 1 (MatchedStartsTests): one HQ per map, so the cluster's
    /// sheds stand in for the HQ he starts beside. fair_starts: re-judged by what a walk from the
    /// base REACHES (guns, heavy weapons, armed vehicles, fuel, med kits, stores - FairStartsTests
    /// scores it against player 1's own start). Hunter and Missions KEEP matched_starts' spots:
    /// every candidate that reached more like player 1 was under water (the rich south-west shore,
    /// a land probe: height 0) or broke one of matched_starts' own bounds - Hunter's pick is actually
    /// RICHER than player 1 (18 gun sources within 8192 vs his 1, 14 heavy vs his 0). Action moves
    /// to (16896,16896): the old north pick had 2 enemy camps within 8192 where player 1 has 1 and
    /// no store in reach, this one matches his mission route to 1% with one camp.
    /// p2_spot_clear: Missions moves (21378,6585) -> (21442,6265) - 384 units, the least that clears
    /// every parked vehicle's box: at the old spot a helicopter ($05) stood at 21481,6741 whose box
    /// 148/152/148 overlapped the soldier's 12/58/10 on all three axes (gaps -57/-2/-115), and the
    /// game's touch rule (Collisions $4448 -> TouchVehicle $45D4 -> EnterOrLeaveVehicle $2F8E) boarded
    /// player 2 into it at pass 2. The new spot clears every vehicle box by 318
    /// (NoVehicleBoxTouchesPlayerTwosStart) and keeps matched_starts' and fair_starts' bounds
    /// (shed and building 1320, vehicle 476, no enemy within 8192, 13162 from player 1, reach
    /// score 51, route gap 20%). Hunter and Action already cleared every box (3747 and 637).</summary>
    public static readonly (int X, int Z)[] BaseSpots = [(56840, 32409), (21442, 6265), (16896, 16896)];
    /// <summary>world: the world file to play (default the one on disk; tests pass an edited copy).
    /// soldier: the record the pre-game made (default: the program's, with the default name).
    /// saved: a loaded game's extras (SaveGame.Read): the world is its image.
    /// energy: the soldier's energy word +6 instead of the record's (tests: the oracle's energy=).
    /// leftovers: the machine's $1AA / $1CA from the game that ended (a load does not reset them;
    /// null for a game from a fresh boot, whose machine starts zeroed).</summary>
    public static WorldState For(GameMode mode, GameData d, byte[]? world = null, Soldier? soldier = null, SavedGame? saved = null,
        short? energy = null, MachineLeftovers? leftovers = null, int fullerWorld = 0, int players = 1, bool maths32 = false,
        int finerGround = 0, bool fixSpikes = false, ushort? groundSeed = null, int relief = 0)
    {
        var w = d.Worlds[mode];
        var file = world ?? d.WorldFile(mode);
        var terrain = Terrain.Generate(w.Seed, w.SeaLevel);
        if (fixSpikes) FineGround.Unwrapped(terrain, w.Seed).CopyTo(terrain.Map, 0);   // ENHANCEMENT (the spikes, a bug fix)
        var ground = new Ground(new MapMemory(terrain, d.Objfile, d.Sinfile));
        var s = new WorldState
        {
            Mode = mode, Terrain = terrain, Ground = ground, Entities = new EntityTable(),
            Players = players,                                          // ENHANCEMENT (mp_two_soldiers): 1 is the original, byte for byte
            Grid = file.AsSpan(0x3E8, 0x8000).ToArray(), Anim = Objects.InitialAnim(d.Main),
            Trig = new Trig(d.Sinfile), Data = d, Models = d.Models,     // one per session: StartDebris rewrites model 0
            Channels = new Channels(d.Main, d.Soundfile),
        };
        // ENHANCEMENT, NOT THE ORIGINAL (FINER GROUND): a new game with the setting, or a load of a game that had it,
        // grows the fine store from the world's seed (the same store every time); before the fill, which stands
        // things on the height the game will use. finerGround 0 and no spec in the save = the original's ground.
        // GROUND SEED / RELIEF (owner 2026-11-11): a new game grows its detail from groundSeed (null: the world's own) at
        // the relief level (FineGround.Reliefs: 0 = 2X, today's); a load from the spec its save carries, the settings unread.
        var lvl = FineGround.Reliefs[Math.Clamp(relief, 0, FineGround.Reliefs.Count - 1)];
        var (fineN, fineNum, fineDen, fineSeed, fineDim, fineHold) = saved is null
            ? (finerGround, lvl.Num, lvl.Den, groundSeed ?? w.Seed, lvl.Dim, lvl.Hold)
            : saved.Extras?.Fine is { } spec ? (spec.N, spec.Num, spec.Den, spec.Seed ?? w.Seed, spec.Dim, spec.Hold)
            : (0, FineGround.FactorNum, FineGround.FactorDen, w.Seed, FineGround.Dimension, FineGround.Gentle);
        if (fineN > 0)
        {
            ground.Fine = FineGround.Grow(terrain, w.Seed, fineN, fineNum, fineDen, fineSeed, fineDim, fineHold);
            ground.Smooth = FineGround.Smooth(FineGround.Unwrapped(terrain, w.Seed), fineNum, fineDen);
            (s.FineN, s.FineNum, s.FineDen, s.FineSeed, s.FineDim, s.FineHold) = (fineN, fineNum, fineDen, fineSeed, fineDim, fineHold);
            if (saved is null) StandOnFine(s.Grid, ground);        // a loaded grid was stood on it when its game began
        }
        if (saved is null && fullerWorld > 0)
        {                                                          // ENHANCEMENT, NOT THE ORIGINAL (fuller world, build
            var table = TypeTable(d);                              // job 2): a NEW game works the extra records out of
                                                                   // the map itself (Fill.Plan) and writes each into a free
                                                                   // slot of its cell. A loaded game's grid IS the saved
                                                                   // world - never filled again, so saves keep the records
            var starts = StartSpots[mode].Concat(BaseSpots).ToArray();   // and never double them. fullerWorld 0 = the original.
            s.Maths32 = ground.Maths32 = maths32;                  // the fill stands things on the height the game will use
            var recs = Fill.Plan(w.Seed, terrain.Map, s.Grid, Math.Min(fullerWorld, 3), table, starts, ground);
            if (fullerWorld >= 4)                                  // WILD: the attract's own (owner 2026-11-02) - groups
            {                                                      // beside the demo's route, its way kept clear of all
                recs.AddRange(Wild.Plan(terrain.Map, s.Grid, table, starts, ground, recs));   // that was added
                var route = DemoRoute.Points(d);
                recs.AddRange(Wild.AlongRoute(terrain.Map, s.Grid, table, route, ground, recs));
                recs = Wild.KeepClear(recs, route, Wild.RouteClear, table);
            }
            foreach (var rec in recs) StoreRecord(s.Grid, s.Overflow, rec);
        }
        s.Buildings = file.AsSpan(0x83E8, 0x4000).ToArray();
        s.MissionTable = saved?.MissionTable ?? d.Main.AsSpan(0x9072 - 0x800, 0xC0).ToArray();
        s.MissionNo = saved?.MissionNo ?? (short)Pregame.Mission(d.Main, (int)mode);
        if (saved is null) s.Log = LogBook.NewGame(s, 0x9072 + s.MissionNo);   // $235E-$23AA, mission $9134
        else
        {                                                          // a load: the log book comes from the save ($10556)
            s.Log = new LogBook { Selected = saved.LogLine };
            saved.LogPlaces.CopyTo(s.Log.Places, 0);
            saved.Kills.AsSpan(0, 0xAE).CopyTo(s.Kills);              // $10530: 174 bytes, see SaveGame.Write
            if (saved.Extras is { } ex)
            {                                                          // the extension block (roomy_memory): the extras
                s.PlayerFuelLow = ex.PlayerFuelLow;                    // go beside the arrays, where the setting finds
                s.FuelLow = new Dictionary<int, byte>(ex.FuelLow);     // them when it is on; with it off they are inert
                s.BuildingFuelLow = new Dictionary<int, byte>(ex.BuildingFuelLow ?? new());
                s.Overflow = ex.Overflow.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
                Array.Copy(ex.KillsWide, s.KillsWide, Math.Min(ex.KillsWide.Length, s.KillsWide.Length));
                s.KillsWideLoaded = true;
                foreach (var g in ex.Graves ?? [])                           // ENHANCEMENT (graves): the store rides the
                {                                                            // extension; with the setting off it stays inert
                    var grave = new Grave { Number = g.Number, X = g.X, Y = g.Y, Z = g.Z, Heading = (byte)g.Heading,
                        Placed = g.Placed, Offset = g.Offset, Counts = [.. g.Counts], Taken = g.Taken };
                    if (grave.Placed && !grave.Taken)                        // where is its record now? A save writes a
                    {                                                        // live grave back into its cell's first free
                        grave.Offset = Graves.FindRecord(s, s.Grid, grave);  // slot - found there, it stays; not found
                        grave.Placed = grave.Offset != -1;                   // (none written back): wait, the retry places
                    }                                                        // it. A taken grave never comes back.
                    s.GraveStore.Add(grave);
                }
            }
            for (var ch = 3; ch >= 0; ch--) s.Channels.Set(1, ch, 0);   // the game before it ended with ResetChannels ($24DA)
            (s.Flags0A7, s.Flags0A4) = (saved.FlagsA7, saved.FlagsA4);   // $99 / $A7 / $A4 as saved ($10510-$1051C)
            (s.Radar, s.MapMarks, s.RespawnPending, s.InsideView) = ((saved.Flags99 & 8) != 0, (saved.Flags99 & 4) != 0,
                (saved.Flags99 & 0x10) != 0, (saved.Flags99 & 1) != 0);
            s.PauseKey = (saved.Flags99 & 0x20) != 0;                // a game saved paused loads paused (MainLoop $256E)
        }
        if (leftovers is { } lo)
        {                                                          // $1AA AmbientSound $B08E, $1CA AllocChannel $2D30: the
            s.AmbientWait = lo.AmbientWait;                        // original leaves both where the last game left them ...
            s.Channels.Cursor = lo.Cursor;                         // ... then the player spawn below allocates with that cursor
            if (saved is not null) s.Kills[0xAE] = lo.KillTvRoom;  // a load leaves the 175th kill byte as it was
        }
        s.Log.BuildMarks(terrain);                                 // PlayStart $24FA $409C ($D43A)
        var hd = file.AsSpan(0, 0x3E8).ToArray();                  // $105F6 -> $C498: clock from the world file +$10
        (s.Minute, s.Hour, s.Day) = ((short)(hd[0x10] << 8 | hd[0x11]), (short)(hd[0x12] << 8 | hd[0x13]), (short)(hd[0x14] << 8 | hd[0x15]));
        s.Items = hd;                                              // $1060C: items to $9E46 as the world loads
        s.PlayerRecord = hd.AsSpan(0x16, 16).ToArray();            // $10618: the player record to $8602
        s.HomeRecord = d.WorldFile(mode).AsSpan(0x16, 16).ToArray();   // ENHANCEMENT (graves): the mode's own start record, from the file on disk
        s.TalkTable = d.TalkTable;                                 // program memory: kept across games and loads
        s.Soldier = soldier ?? DefaultSoldier(d);
        if (saved is not null) saved.SoldierRecord.CopyTo(s.Soldier.R, 0);   // $10540: the saved soldier record
        s.PlayerRecord.CopyTo(s.Soldier.R, 0x20);                  // $8602 = soldier +$20: the world load's player record
        if (energy is { } e) s.Soldier.SetWord(6, e);
        s.Soldier.R[4] = 1;                                        // $87B6 SetSoldierStatus(1)
        s.Status216 = s.Soldier.R[0];                             // $87A0 SoldierToGlobals: $216 (word), $21A score, $22A energy
        s.Score = s.Soldier.Long(0xC);
        s.Energy = (short)s.Soldier.Word(6);
        s.Panel = new Panel(d);                                // $84AC at start-up: panfile rows 0-59
        s.Panel.SetUp(s);                                          // $24C6 $24FA: $9A3E, PanelClock
        if (s.Players > 1)
        {                                                          // ENHANCEMENT, NOT THE ORIGINAL (mp_panel):
            s.Panel2 = new Panel(d);                           // player 2's own buffer, set up the same way
            s.Panel2.SetUp(s);                                 // ($9A3E, PanelClock) - his X/Y and rounds fill in
        }                                                      // from his entity as the passes run (Play.Draw)
        var b = new Builder(d, s);
        // $87B6: the player record ($8602 = world file +$16) into slot 0 (sf $250 first);
        // ConstructEntity runs PlayerSetUpOrDie ($2E52: $219, CameraBase, weapon)
        var player = (byte[])s.PlayerRecord.Clone();
        b.SpawnPlayer(player, 0);
        var p = s.Entities.All[0];
        s.Camera = ground.Place(p.UW(0x4A), p.W(0x4C), p.UW(0x4E), s.Distance, s.Lift);
        if (s.Players > 1)
        {                                                          // ENHANCEMENT, NOT THE ORIGINAL (mp_two_soldiers): player 2
            var rec2 = file.AsSpan(0x16, 16).ToArray();            // spawns at his own base (docs/extras.md mp_start_spots,
            var (bx, bz) = BaseSpots[(int)mode];                   // BaseSpots above); player 1 keeps the original start. Same
            var y2 = ground.Height((ushort)bx, (ushort)bz);        // record, moved to the base and set on the ground there.
            rec2[6] = (byte)(bx >> 8); rec2[7] = (byte)bx;         // word +6 x
            rec2[8] = (byte)(y2 >> 8); rec2[9] = (byte)y2;         // word +8 y
            rec2[10] = (byte)(bz >> 8); rec2[11] = (byte)bz;       // word +$A z
            b.SpawnPlayer(rec2, 1);
        }
        s.Items = Equip(p, s.Items, d.Main[0x9E44 - 0x800]);
        Items.Carried(s);                                          // $9E40 bra $A2A8: the carried masks $238 / $23C
        s.Panel.Weapon(s);                                        // $87DC LoadWeapon -> $A96C
        if (s.Players > 1)
            Vehicles.LoadWeapon(s, s.Entities.All[1]);             // $87B6 ends in $A37C: the record's +$32 byte
                                                                   // (99 in the world files) must not outlive the
                                                                   // weapon load - his gun comes from HIS pack
                                                                   // (NewPack), and the icon and count go to his
                                                                   // own panel (ENHANCEMENT mp_panel)
        return s;
    }

    /// <summary>The record a game made from the menus: the program's, named "Trigger Happy" ($86E2, what the
    /// name box gives when nothing is typed).</summary>
    public static Soldier DefaultSoldier(GameData d)
    {
        var sol = new Soldier(d.Main);
        Pregame.SetName(sol, Pregame.FinalName(d.Main, []));
        return sol;
    }

    /// <summary>SpawnPlayer ($87B6): the 16-byte record of a player (the soldier's +$20, what the
    /// quit's StoreEntityRecord $82D0 wrote; a world file's +$16 at a new game) spawned back into
    /// that player's own slot (player 0 today - the original's first-free search takes it; see
    /// Builder.SpawnPlayer) - the slot is freed first ($8300 sf $250), PlayerSetUpOrDie ($2E52) runs
    /// inside.</summary>
    public static void SpawnPlayer(WorldState s, byte[] rec, int player = 0) => new Builder(s.Data, s).SpawnPlayer(rec, player);

    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (multiplayer piece 4): players 1.. carry their own
    /// item table, masks and talk table, made the first time the player is set up (Builder.Construct,
    /// before PlayerSetUpOrDie whose $A37C LoadWeapon reads the table). What a new game gives player 0,
    /// from the code: a copy of the world file's first $3E8 bytes (fresh counts - the file's, not
    /// player 0's current pack) with Equip's stock-column move ($9DEC, the $9E44 flag), the masks
    /// ($A2A8), and a copy of the talk table's program data ($892E) as it stands; only that
    /// player's gifts change the copy from then on. Respawn reuses the pack (only a still-empty
    /// one is made).</summary>
    static void NewPack(WorldState s, Entity p, int player)
    {
        var me = s.PlayerAt(player);
        if (me.Items.Length != 0) return;
        me.TalkTable = s.TalkTable.ToArray();
        var header = s.Data.WorldFile(s.Mode).AsSpan(0, 0x3E8).ToArray();
        me.Items = Equip(p, header, s.Data.Main[0x9E44 - 0x800]);
        Items.Carried(s, player);
    }

    /// <summary>SpawnInterior ($4972): the 7 records of a building's block ($81EE with $A0 = $FF: each
    /// becomes a live record marked inside, +$20 = $FF and +$06 bit 5).</summary>
    public static void SpawnInterior(WorldState s, int at)
    {
        var b = new Builder(s.Data, s);
        for (var k = 0; k < 7; k++) b.Spawn(s.Buildings, at + 0x10 + k * 16, 0xFF);
    }

    /// <summary>SpawnInFreeSlot ($314C) with the shooter extras in Spawn24A/24C/24E (FireShot).</summary>
    public static Entity? SpawnInFreeSlot(WorldState s, int x, int y, int z, int d3, int d4, byte playerFlag) =>
        new Builder(s.Data, s).Construct(x, y, z, d3, d4, playerFlag);

    /// <summary>ConstructEntity ($3196) into a given slot (ReplaceEntity $3172 and EntityEnds).</summary>
    public static void Construct(WorldState s, int slot, int x, int y, int z, int d3, int d4, byte playerFlag) =>
        new Builder(s.Data, s).Construct(slot, x, y, z, d3, d4, playerFlag);

    /// <summary>The end of $87B6: $9DEC moves each of the 30 items' starting stock for $214 (0 at
    /// a new game, $86AE: word +$E) into its count (word +0; added to it only when the program
    /// byte $9E44 is set, by $9DE6), then $A37C loads the current weapon $200 (0) into the player:
    /// +$32 rounds, +$26 from +$1E, +$2E from +$1C. The items are the world file from +$28,
    /// copied to $9E46 by $1060C ($3C0 bytes); returned with the file's offsets (table at +$28).</summary>
    public static byte[] Equip(Entity p, byte[] header, byte flag9E44)
    {
        var t = header.ToArray();
        int W(int o) => (short)(t[o] << 8 | t[o + 1]);
        void Set(int o, int v) { t[o] = (byte)(v >> 8); t[o + 1] = (byte)v; }
        const int Stock = 0 * 2 + 0xE;                                        // $9DEC: d0 = $214 * 2 + $E
        for (var i = 0; i < 30; i++)
        {
            var o = 0x28 + i * 32;
            var n = W(o + Stock);
            if ((sbyte)flag9E44 < 0) n += W(o);                               // $9E1E tst.b $9E44
            Set(o, n);
            Set(o + Stock, 0);
        }                                                                     // ($9E3A sf $9E44)
        const int Weapon = 0x28 + 0 * 32;                                     // $A37C: $200 = 0 ($242E)
        p.SetW(0x32, W(Weapon)); p.SetW(0x26, W(Weapon + 0x1E)); p.SetW(0x2E, W(Weapon + 0x1C));
        return t;
    }

    /// <summary>$8186: spawn the records of the live window's cells at ($1540, $1542) from the
    /// camera - the original's 2 x 2; with Play.MoreObjects on the same 4 x 4 window the write-back
    /// test uses (Objects.Window), so nothing is spawned and written back in the same pass.
    /// mp_near: with 2 players the spawner walks player 2's own window cells too - his camera is
    /// never stored, so its cells come from Objects.CameraCells on his position. Both windows go
    /// through the ONE Builder: free slots come from the same search (from s.Players with 2
    /// players, mp_two_soldiers), and a cell in both windows just finds its records already live
    /// (Spawn no-ops on a cleared record). One player: the original's single walk, byte for byte.</summary>
    public static void SpawnNearCamera(GameData d, WorldState s, bool moreObjects = false)
    {
        var b = new Builder(d, s);
        if (moreObjects)   // ENHANCEMENT: a thing held out there that has come into the original's wake area goes back to the
            foreach (var e in s.Entities.All)   // map and wakes again below - fresh, as the original wakes it there
                if (e.HeldFar && e.Alive && Objects.InWindow(s, e, false))
                {
                    e.HeldFar = false;
                    Objects.WriteBack(s, e, e.Flags);
                }
        var (dx0, dx1, dz0, dz1) = Objects.Window(moreObjects);
        SpawnWindow(b, s, s.Camera.G1540, s.Camera.G1542, dx0, dx1, dz0, dz1);
        if (s.Players > 1)
        {
            var p2 = s.Entities.All[1];
            var (cx2, cz2) = Objects.CameraCells(p2.UW(0x4A), p2.UW(0x4E), s.Distance, s.ViewAngle2);
            SpawnWindow(b, s, cx2, cz2, dx0, dx1, dz0, dz1);
        }
    }

    static void SpawnWindow(Builder b, WorldState s, int ccx, int ccz, int dx0, int dx1, int dz0, int dz1)
    {
        for (var dz = dz0; dz <= dz1; dz++)
            for (var dx = dx0; dx <= dx1; dx++)
            {
                int cx = ccx + dx, cz = ccz + dz;
                if (cx < 0 || cz < 0 || cx > 15 || cz > 15) continue;
                var cell = (cz * 16 + cx) * 128;            // $81D4
                for (var slot = 0; slot < 8; slot++) b.Spawn(s.Grid, cell + slot * 16);
                if (s.RoomyMemory && s.Overflow.Remove(cell, out var extra))     // roomy_memory: a full cell's
                {
                    for (var i = 0; i < extra.Count; i++)                        // overflow records spawn after the 8
                    {                                                            // grid records, oldest first (the order
                        var rec = extra[i];                                      // they were written back in); a refused
                        var e2 = b.Spawn(rec, 0);                                // spawn leaves its record in the list
                        if ((rec[0] | rec[1]) == 0)                              // went live: out of the list, and its
                        {                                                        // fuel low byte (record byte 16) back
                            extra.RemoveAt(i); i--;
                            if (e2 is not null) e2.B[0x35] = rec[0x10];
                        }
                    }
                    if (extra.Count > 0) s.Overflow[cell] = extra;                // refused (no free slot): kept for the next pass, not lost (lead's review)
                }
            }
    }

    /// <summary>The type table at $81BA: a kind's low byte -> its type word (Fill, Wild, the spawner's $81EE).</summary>
    public static int[] TypeTable(GameData d)
    {
        var table = new int[256];
        for (var i = 0; i < 256; i++) table[i] = ExtraKinds.TypeBase(d.Main, i);   // the program's 12 words, then ExtraKinds' classes
        return table;
    }

    /// <summary>ENHANCEMENT (fuller world): a planned record into a free slot of its grid cell ((z >> 12) * 16 + (x >> 12),
    /// records cell-major, 16 x 16 cells x 8 slots: MoreObjectsTests.cs:202) - past the 8 slots the cell's overflow list
    /// (roomy memory, which the fuller world turns on), as Objects.WriteBack keeps them: the 16 bytes and the fuel low byte (0).</summary>
    /// <summary>ENHANCEMENT (FINER GROUND): the map's own things onto the finer ground. Each stored record that stood on
    /// the original ground (its height within OnGround of it) moves up or down by the difference between the finer ground
    /// and the original there, keeping its own offset; the rest (in the air, on top of something) stay. Before, they kept
    /// the original map's heights under the finer ground, which is higher nearly everywhere (Missions: 344 of 485 buried,
    /// the bike at 21069,14640 36 under) - the card drew them up on top, but the game read the buried height: a parked
    /// vehicle's pitch ($42A6, Objects.Tilt: its height less the ground ahead) came out steep, the bike's 0xDB (52 degrees
    /// nose up), and the rider took it for a frame on boarding (owner 11-12: "the bike never got fixed").</summary>
    public static void StandOnFine(byte[] grid, Ground ground)
    {
        const int OnGround = 16;
        var fine = ground.Fine;
        for (var r = 0; r < grid.Length; r += 16)
        {
            if ((grid[r] | grid[r + 1]) == 0) continue;                // empty
            var x = (ushort)(grid[r + 6] << 8 | grid[r + 7]);
            var z = (ushort)(grid[r + 10] << 8 | grid[r + 11]);
            int y = (short)(grid[r + 8] << 8 | grid[r + 9]);
            ground.Fine = null;
            int coarse = ground.Height(x, z);
            ground.Fine = fine;
            if (Math.Abs(y - coarse) > OnGround) continue;
            y += ground.Height(x, z) - coarse;
            grid[r + 8] = (byte)(y >> 8); grid[r + 9] = (byte)y;
        }
    }

    public static void StoreRecord(byte[] grid, Dictionary<int, List<byte[]>> overflow, byte[] rec)
    {
        var cell = ((rec[10] & 0xF0) >> 4) * 16 + ((rec[6] & 0xF0) >> 4);
        for (var slot = 0; slot < 8; slot++)
        {
            var at = cell * 128 + slot * 16;
            if ((grid[at] | grid[at + 1]) != 0) continue;              // empty = both name bytes 0, like the spawner reads
            rec.CopyTo(grid, at);
            return;
        }
        if (!overflow.TryGetValue(cell * 128, out var list)) overflow[cell * 128] = list = [];
        var big = new byte[17];
        rec.CopyTo(big, 0);
        list.Add(big);
    }

    /// <summary>ENHANCEMENT, NOT THE ORIGINAL (renderer = 3d): a stored map record's LOOK in e - the bytes
    /// ConstructEntity ($3196) sets that the model draw reads (models, angles, flags, position, ground height),
    /// none of its side effects (no slot, no plan, no sound). False for an empty or unknown record.</summary>
    public static bool Appearance(GameData d, WorldState s, byte[] rec, int at, Entity e) => new Builder(d, s).Appearance(rec, at, e);

    sealed class Builder(GameData d, WorldState s)
    {
        public bool Appearance(byte[] rec, int at, Entity e)
        {
            var kind = rec[at] << 8 | rec[at + 1];                          // as Spawn ($81EE)
            if (kind == 0) return false;
            var type = (short)(rec[at] + ExtraKinds.TypeBase(d.Main, rec[at + 1]));
            if (type < 0 || type >= ExtraKinds.End) return false;
            var d3 = kind << 16 | (ushort)type;
            var d4 = rec[at + 0x0C] << 16 | rec[at + 2];
            int x = rec[at + 6] << 8 | rec[at + 7], y = (short)(rec[at + 8] << 8 | rec[at + 9]), z = rec[at + 10] << 8 | rec[at + 11];
            Array.Clear(e.B);                                                // as Construct, the look only
            e.B[0] = 0xFF;
            e.SetL(2, d3);
            var kt = Kinds + (ushort)d3 * Size;
            var d7 = Long(kt);
            e.SetL(6, d7);
            e.B[1] = (byte)d4;
            e.SetW(0x80, d4 & 0xFFFF);
            e.B[0x7E] = (byte)(d4 >> 16);
            e.SetW(0x7C, (d7 >> 15 & 1) != 0 ? 0 : d4 >> 16 & 0xFFFF);
            e.SetW(0x4A, x); e.SetW(0x4C, y); e.SetW(0x4E, z);
            e.SetW(0x74, s.Ground.Height((ushort)x, (ushort)z));
            for (var k = 0; k < 4; k++) e.SetW(0x24 + k * 2, Word(kt + 4 + k * 2));
            e.SetW(0x3E, rec[at + 3]);
            return true;
        }

        const int Kinds = 0xD45E, Variants = 0xDA76, Types = 0x81BA, Size = 0x34;
        readonly Models _models = s.Models;

        // ExtraKinds: a read inside the kind table ($D45E on, the extra types past its end) comes from the port's copy
        static bool InKinds(int addr) => addr >= Kinds && addr < Kinds + ExtraKinds.End * Size;
        int Word(int addr) => InKinds(addr) ? d.Kinds[addr - Kinds] << 8 | d.Kinds[addr - Kinds + 1] : d.Main[addr - 0x800] << 8 | d.Main[addr - 0x800 + 1];
        int Long(int addr) => Word(addr) << 16 | Word(addr + 2);
        byte Byte(int addr) => InKinds(addr) ? d.Kinds[addr - Kinds] : d.Main[addr - 0x800];

        public Entity? Spawn(byte[] rec, int at, byte inside = 0, Dictionary<int, byte>? fuelFrom = null, int fuelAt = 0)
        {
            // $81EE
            var kind = rec[at] << 8 | rec[at + 1];
            if (kind == 0) return null;
            var type = (short)(rec[at] + ExtraKinds.TypeBase(d.Main, rec[at + 1]));
            if (type < 0 || type >= ExtraKinds.End) return null;
            var d3 = kind << 16 | (ushort)type;
            var d4 = rec[at + 0x0C] << 16 | rec[at + 2];
            int x = rec[at + 6] << 8 | rec[at + 7], y = (short)(rec[at + 8] << 8 | rec[at + 9]), z = rec[at + 10] << 8 | rec[at + 11];
            if (s.BoatsAlongShore && inside == 0 && rec[at + 2] == 0)       // ENHANCEMENT (BoatsAlongShore): an empty boat (variant 0: no driver)
            {
                var f = Long(Kinds + (ushort)type * Size);                  // the kind's flags: enterable (28), water-going (10), not amphibious (1)
                if ((f >> 28 & 1) != 0 && (f >> 10 & 1) != 0 && (f >> 1 & 1) == 0)
                    d4 = BoatShore.Heading(s.Ground, x, z, rec[at + 0x0C]) << 16 | rec[at + 2];
            }
            var e = Construct(x, y, z, d3, d4, playerFlag: inside);         // $8222 $24F = $A0
            if (e is null) return null;
            rec[at] = rec[at + 1] = 0;                      // clr.w (a2): now live, not in the grid
            e.SetW(0x3E, rec[at + 3]);
            e.SetW(0x2A, rec[at + 4] << 8 | rec[at + 5]);
            byte low = 0;
            var carried = false;
            if (fuelFrom is not null) carried = s.RoomyMemory && fuelFrom.Remove(fuelAt, out low);   // another grid's own fuel bytes
            else if (s.RoomyMemory && ReferenceEquals(rec, s.Grid)) carried = s.FuelLow.Remove(at, out low);
            else if (s.RoomyMemory && ReferenceEquals(rec, s.Buildings)) carried = s.BuildingFuelLow.Remove(at, out low);   // left inside a building (lead's review)   // roomy_memory: the parked thing's fuel low byte ($8246 keeps only the high)
            // m32_graves: a grave record saved before roomy memory holds its whole number in the
            // one byte +$D - no low entry was ever written (kind word 8 is what WriteRecord gives
            // every grave, and it only runs with the setting on). Read as the high half it would
            // spawn as number x 256, so the old byte goes to +$35 with +$34 = 0: e.UW(0x34) then
            // reads the number the record held. Roomy off never reaches here - one byte, as the
            // original - and any other record without a low entry keeps today's high-half read.
            if (!carried && s.RoomyMemory && s.Graves && kind == 8) e.SetW(0x34, rec[at + 0x0D]);
            else e.SetW(0x34, rec[at + 0x0D] << 8 | low);
            e.SetW(0x32, (sbyte)rec[at + 0x0E]);
            return e;
        }

        // SpawnInFreeSlot ($314C): the first free slot of 0-Spawnable-1, then $3196; none -> $24C-$24F
        // cleared. Multiplayer piece 1: with more than one player the reserved player slots are
        // skipped - the search starts at s.Players. One player: from 0, the original exactly,
        // quirk and all (a record may take slot 0 while the player is dead; Respawn clears it).
        public Entity? Construct(int x, int y, int z, int d3, int d4, byte playerFlag)
        {
            var from = s.Players > 1 ? s.Players : 0;
            var slot = Array.FindIndex(s.Entities.All, from, s.Entities.Spawnable - from, en => !en.Alive);
            if (slot < 0) { s.Spawn24C = 0; s.Spawn24E = 0; return null; }   // $3166 clr.l $24C
            return Construct(slot, x, y, z, d3, d4, playerFlag);
        }

        /// <summary>The player's record into its own reserved slot n (piece 1 of $87B6 made
        /// explicit). One player: Spawn(rec, 0), the original's path byte for byte. More: straight
        /// into slot n - no free search - and PlayerSetUpOrDie runs there (Construct, IsPlayerSlot).</summary>
        public Entity? SpawnPlayer(byte[] rec, int n)
        {
            if (s.Players == 1) return Spawn(rec, 0);
            var kind = rec[0] << 8 | rec[1];
            if (kind == 0) return null;
            var type = (short)(rec[0] + ExtraKinds.TypeBase(d.Main, rec[1]));
            if (type < 0 || type >= ExtraKinds.End) return null;
            var d3 = kind << 16 | (ushort)type;
            var d4 = rec[0x0C] << 16 | rec[2];
            int x = rec[6] << 8 | rec[7], y = (short)(rec[8] << 8 | rec[9]), z = rec[10] << 8 | rec[11];
            var e = Construct(n, x, y, z, d3, d4, playerFlag: 0);
            if (e is null) return null;
            rec[0] = rec[1] = 0;                            // clr.w (a2): now live, not in the grid
            e.SetW(0x3E, rec[3]);
            e.SetW(0x2A, rec[4] << 8 | rec[5]);
            e.SetW(0x34, rec[0x0D] << 8);
            e.SetW(0x32, (sbyte)rec[0x0E]);
            return e;
        }

        // ConstructEntity ($3196) in this slot
        public Entity Construct(int slot, int x, int y, int z, int d3, int d4, byte playerFlag)
        {
            var e = s.Entities.All[slot];
            Array.Clear(e.B);
            e.HeldFar = false;
            e.HurtBy = LivingWorld.Side.None;
            e.Limbs = 0; e.PaceCarry = 0;
            e.BlastHit = false;
            e.PaceY = int.MinValue;
            if (s.PaceHurts.Count > 0)                                       // A500 PACE: a new thing here is touched afresh
                foreach (var k in s.PaceHurts.Keys.Where(k => k.Item1 == slot || k.Item2 == slot).ToList()) s.PaceHurts.Remove(k);
            e.B[0] = 0xFF;
            e.SetL(2, d3);
            e.SetW(0x2C, s.Spawn24A);                                        // $31A8: a shot's owner
            e.InHand = s.Spawn24A != 0;
            e.B[0x21] = s.Spawn24E;                                          // $31AE: the player's shot
            var kt = Kinds + (ushort)d3 * Size;
            var d7 = Long(kt);
            e.B[0x20] = playerFlag;
            if (playerFlag != 0) d7 |= 1 << 5;
            e.SetL(6, d7);
            e.B[1] = (byte)d4;
            e.SetW(0x80, d4 & 0xFFFF);
            e.B[0x7E] = (byte)(d4 >> 16);
            e.SetW(0x7C, (d7 >> 15 & 1) != 0 ? 0 : d4 >> 16 & 0xFFFF);
            // $3302
            e.B[0x1E] = 0xFF;
            e.SetW(0x4A, x); e.SetW(0x4C, y); e.SetW(0x4E, z);
            e.SetW(0x50, x); e.SetW(0x52, y); e.SetW(0x54, z);
            var h = Ground.IsShot(e) ? s.Ground.SmoothHeight((ushort)x, (ushort)z) : s.Ground.Height((ushort)x, (ushort)z);   // FINER GROUND: a shot the smooth ground
            e.SetW(0x74, h);
            e.B[0x1E] = (byte)(h >= (short)y ? 0xFF : 0);
            e.SetW(0x56, x * 2 - s.Camera.G1548);
            e.SetW(0x58, y);
            e.SetW(0x5A, z * 2 - s.Camera.G1544);
            for (var k = 0; k < 4; k++) e.SetW(0x24 + k * 2, Word(kt + 4 + k * 2));
            Extents(e);
            e.B[0x42] = Byte(kt + 0x0C);
            e.B[0x2F] = Byte(kt + 0x0D);
            e.SetW(0x30, Word(kt + 0x0E));
            var d1 = Word(kt + 0x10);
            e.SetW(0x32, d1 & 0xFF);
            var hi = (d1 & 0xFF00) >> 2;                   // sf.b d1, lsr.w #2, lsr.b #1
            e.SetW(0x34, (hi & 0xFF00) | ((hi & 0xFF) >> 1));
            int a = (short)(Word(kt + 0x12) + s.Spawn24C), bb = Word(kt + 0x14), c = Word(kt + 0x16), dd = Word(kt + 0x18);   // $3230 + $24C
            e.SetW(0x36, a); e.SetW(0x38, bb); e.SetW(0x3A, c); e.SetW(0x3C, dd);
            var half = c >> 1;
            if (half == 0) e.SetW(0x3A, (c & 1) != 0 ? 0 : 0x10);
            e.SetW(0x78, half + 8);
            e.SetW(0x3E, e.W(4) >= 0x33 && !ExtraKinds.IsVehicle(e.W(4)) ? a : 0);            for (var k = 0; k < 4; k++) e.SetW(0x90 + k * 2, Word(kt + 0x2C + k * 2));
            e.SetW(0x9E, Word(kt + 0x2E));
            if ((d7 >> 0x18 & 1) != 0) Ahead(e);
            for (var k = 0; k < 4; k++) e.SetW(0x0E + k * 2, Word(kt + 0x1C + k * 2));
            if (s.IsPlayerSlot(slot))
            {
                if (slot > 0) NewPack(s, e, slot);                           // ENHANCEMENT (mp_packs): own pack first,
                d7 = Vehicles.PlayerSetUpOrDie(s, e, d7);                         // $3294 $2E52 - its $A37C reads the player's table
            }
            else
            {
                e.SetW(0x0A, 0);
                e.SetW(0x0C, Word(kt + 0x1A));
                if (e.W(0x0C) != 0)
                {
                    if (e.B[0x0C] != 0) d7 = Hunter.Rules.Behaviour.Plan(s, e, d7);                // $32AC
                    else if (e.B[1] != 0) d7 = Hunter.Rules.Behaviour.SwapModelsAndPlan(s, e, d7); // $32B8: a driver
                }
            }
            e.SetL(0x88, Long(kt + 0x24));
            e.SetL(0x8C, Long(kt + 0x28));
            if (e.L(0x88) > 0)                                               // $32C8
            {
                if ((d7 >> 0x1C & 1) != 0 && e.W(0x3E) == 0) d7 &= ~(1 << 0x13);
                else { e.SetL(6, d7); s.Channels.StartEntity(e, ref d7, slot, s.Entities); }   // $32E0
            }
            e.SetL(6, d7);
            s.Spawn24A = 0; s.Spawn24C = 0; s.Spawn24E = 0;                  // $32F0
            return e;
        }

        // $334A: half width, height, half depth of the main model -> $6E-$72
        void Extents(Entity e)
        {
            var o = _models.Offsets[e.B[0x25]];
            e.SetW(0x6E, _models.U(o + 4) >> 1);
            e.SetW(0x70, _models.U(o + 6));
            e.SetW(0x72, _models.U(o + 8) >> 1);
            if (s.BulletBox && Arms.IsBullet(e.W(4))) { e.SetW(0x6E, Arms.BulletHalf); e.SetW(0x72, Arms.BulletHalf); }   // ENHANCEMENT (BULLET BOX)
        }

        // $2C00 with d0 = $3E: the point $3E ahead along the heading -> $5C-$60
        void Ahead(Entity e)
        {
            var trig = new Trig(d.Sinfile);
            short x = 0, y = 0, z = e.W(0x3E);
            var p = ((byte)(e.B[0x7C] - e.B[0x39])) * 4;
            var q = e.B[0x7E] * 4;
            if (p != 0)
            {
                var (s1, c1) = (trig.Sin(p), trig.Cos(p));
                var nz = (short)((M68.Muls(z, c1.M) >> c1.E) + (M68.Muls(y, s1.M) >> s1.E));
                var ny = (short)((M68.Muls(y, c1.M) >> c1.E) - (M68.Muls(z, s1.M) >> s1.E));
                y = ny; z = nz;
            }
            if (e.B[0x7E] != 0)
            {
                var (s2, c2) = (trig.Sin(q), trig.Cos(q));
                var nx = (short)((M68.Muls(x, c2.M) >> c2.E) + (M68.Muls(z, s2.M) >> s2.E));
                var nz = (short)((M68.Muls(z, c2.M) >> c2.E) - (M68.Muls(x, s2.M) >> s2.E));
                x = nx; z = nz;
            }
            e.SetW(0x5C, x); e.SetW(0x5E, y); e.SetW(0x60, z);
        }


    }
}
