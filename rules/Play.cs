namespace Hunter.Rules;

/// <summary>
/// The running 3D view, as the main loop ($2560) and the vertical-blank interrupt drive it.
/// Per vertical blank: camera distance steps toward its target ($3578), the fade steps every
/// third blank ($69B2, reload 2). Per frame: spawn nearby records ($8186), place the camera
/// ($374C), sort ($4164), clear and draw ($CA9A, $39B0). Entity behaviour: $25BC (Movement).
/// </summary>
public sealed partial class Play
{
    readonly GameData _d;
    public readonly WorldState State;
    readonly Raster _raster = new();
    readonly Objects _objects;
    readonly Sky _sky;
    public readonly View View = new();

    /// <summary>(Objects.OwnShadows): every object casts a half-bright
    /// (EHB) shadow from its own main model. Off - the default - the view is the original's: black
    /// colour-0 faces for its 13 shadow types only.</summary>
    public bool OwnShadows { get => _objects.OwnShadows; set => _objects.OwnShadows = value; }
    /// <summary>objects and ground are placed and drawn farther -
    /// the depth bound $1800 -> $2800, the grid four point rows farther ($2900). Handed to Objects
    /// and Ground; not in the world state and not in the save. Off - the default - the view is the
    /// original's byte for byte. (The far ground draws, but few live things are out there until a
    /// later change widens the live area.)</summary>
    public bool FarView
    {
        get => _objects.FarView;
        set { _objects.FarView = value; State.Ground.FarView = value; }
    }

    /// <summary>the upgrade's 32-bit maths (WorldState.Maths32). Set before
    /// the first pass; also switches the Ground's height lerp (group 1, docs/maths32.md; Fill places on the shipped maps only, where the lerp never overflows).</summary>
    public bool Maths32
    {
        get => State.Maths32;
        set { State.Maths32 = value; State.Ground.Maths32 = value; }
    }

    /// <summary>no drift when moving along a heading (WorldState.StraightMoves).</summary>
    public bool StraightMoves { get => State.StraightMoves; set => State.StraightMoves = value; }
    /// <summary>a step keeps its fraction (WorldState.FinePositions).</summary>
    public bool FinePositions { get => State.FinePositions; set => State.FinePositions = value; }
    /// <summary>leaving a vehicle puts you behind it first (WorldState.LeaveBehind).</summary>
    public bool LeaveBehind { get => State.LeaveBehind; set => State.LeaveBehind = value; }
    /// <summary>a stick's lean turns the player's vehicle by that share of the full turn (WorldState.AnalogSteering).</summary>
    public bool AnalogSteering { get => State.AnalogSteering; set => State.AnalogSteering = value; }
    /// <summary>the player's car handles as a body with tyres (WorldState.WeightyHandling).</summary>
    public bool WeightyHandling { get => State.WeightyHandling; set => State.WeightyHandling = value; }
    /// <summary>a bullet hurts the limb it hits and a limb hit does not kill (WorldState.LimbDamage).</summary>
    public bool LimbDamage { get => State.LimbDamage; set => State.LimbDamage = value; }
    /// <summary>bullets get a small box, not model 0's (WorldState.BulletBox).</summary>
    public bool BulletBox { get => State.BulletBox; set => State.BulletBox = value; }
    /// <summary>what the original does once a pass happens at the A500's pace (WorldState.A500Pace).</summary>
    public bool A500Pace { get => State.A500Pace; set => State.A500Pace = value; }
    /// <summary>(FINE ANGLES): the ground tilt from the exact heading (WorldState.FineTilt).</summary>
    public bool FineTilt { get => State.FineTilt; set => State.FineTilt = value; }
    /// <summary>on foot the soldier turns to the reticle and his shots go to the point under it (WorldState.AimReticle, rules/Aim.cs).</summary>
    public bool AimReticle { get => State.AimReticle; set => State.AimReticle = value; }
    /// <summary>The reticle in game pixels over the view (the shell sets it each blank); x below 0 = none.</summary>
    public void SetReticle(int x, int y) { State.ReticleX = x; State.ReticleY = y; }
    /// <summary>an early HQ visit sends you back out (WorldState.EarlyHqBack).</summary>
    public bool EarlyHqBack { get => State.EarlyHqBack; set => State.EarlyHqBack = value; }
    /// <summary>parked boats lie along the shore (WorldState.BoatsAlongShore).</summary>
    public bool BoatsAlongShore { get => State.BoatsAlongShore; set => State.BoatsAlongShore = value; }

    /// <summary>the original's three memory squeezes are lifted - a
    /// parked thing keeps its fuel's low byte (Objects.WriteBack $82E6 / Builder.Spawn $8246), a
    /// full grid cell takes more than 8 records ($830E: an overflow list beside the grid, spawned
    /// after the cell's 8, oldest first), and the kill counts do not wrap at a byte ($99EA's signed
    /// byte compare becomes a plain one over WorldState.KillsWide). The original's arrays - the grid
    /// ($C000), Kills, the save block - stay the original's layout and bytes; the extras sit beside
    /// them in WorldState and ride the save as an extension block after SaveGame.Size (a save
    /// without one loads with the extras empty). Off - the default - every byte, rule and save is
    /// the original's. Set before the first pass; the flag lives on the state, so the load and the
    /// save paths see it too.</summary>
    public bool RoomyMemory
    {
        get => State.RoomyMemory;
        set
        {
            State.RoomyMemory = value;
            if (!value || State.KillsWideLoaded) return;
            for (var t = 0; t < State.Kills.Length; t++) State.KillsWide[t] = State.Kills[t];   // a save with no extension: the bytes are the whole truth
            State.KillsWideLoaded = true;
            State.Entities.All[0].B[0x35] = State.PlayerFuelLow;                 // a loaded game's player: his fuel low byte back
        }
    }

    /// <summary>(Graves): a paid death - never the last - leaves a
    /// grave holding what the soldier carried, out of play so no free resources, and the next soldier
    /// starts at the mode's home record with the normal starting kit. The life bar, 4 a death and
    /// the last death (the original's own grave, 250 blanks, debrief) are the original's; unlimited
    /// graves in one game. The kit lives in WorldState.GraveStore beside the original's arrays and
    /// rides the save's extension block (SaveGame.ExtrasBlock, its section only with this on); the
    /// placed grave itself is an ordinary type-$7C grid record, so it saves in the original's block.
    /// Off - the default - every byte, rule and save is the original's. Set before the first pass;
    /// the flag lives on the state, so the load and the save paths see it too.</summary>
    public bool Graves
    {
        get => State.Graves;
        set => State.Graves = value;
    }

    /// <summary>(VultureGraves): a new grave - a paid death's
    /// placed $7C, so it needs Graves on to have one - brings a pair of vultures that circles
    /// over it for a while: the nearest live vultures drafted, a missing one spawned at the
    /// circle. The flight is the vulture's own steering at the spot over the grave. Off - the
    /// default - nothing runs and the game is byte for byte today's. Not in the save.</summary>
    public bool VultureGraves
    {
        get => State.VultureGraves;
        set => State.VultureGraves = value;
    }

    /// <summary>(Objects.MoreObjects): more live things and a bigger
    /// live area - 30 records (29 handed out, the last reserved, $3398/$3154) become 395 (394 handed
    /// out; the port's sort list is unbounded, so it cannot overflow; the three word reads that turn
    /// a slot back into a record are unsigned), and the spawn/live window
    /// 2 x 2 cells becomes 4 x 4 (Objects.Window, one cell more each side of the camera and two
    /// more ahead - $1542 is the camera point's z cell and the view looks towards +z). Handed to
    /// Objects; the live count is EntityTable.Count, not in the world state and not in the save
    /// (the save holds no live-thing table - plan B). Set before the first pass (the shell sets it
    /// at Init / load). Off - the default - the game is the original's byte for byte: 30 records,
    /// the 2 x 2 window, the radar's slots 1-29.</summary>
    /// <summary>(LIVING WORLD, rules/LivingWorld.cs): people and animals notice each
    /// other. The shell sets it for the player's games, never the attract demo. Off: the original's planning.</summary>
    public bool LivingWorld { get => State.LivingWorld; set => State.LivingWorld = value; }

    public bool MoreObjects
    {
        get => _objects.MoreObjects;
        set { _objects.MoreObjects = value; State.MoreObjects = value; State.Entities.Count = value ? EntityTable.MaxCount : 30; }
    }
    /// <summary>(SmoothLight): while the light is settled on the
    /// hour's level, the view and the sky band drift minute by minute between this hour's table
    /// level ($C4A4[h]) and the next hour's, where the original holds the hour's level for 59
    /// minutes and then steps ($C466, FadeToLight $C47C). Off - the default - the view is the
    /// original's: at hh:00 it is byte-identical either way.</summary>
    public bool SmoothLight;

    /// <summary>(split-draw, docs\split-draw.md): off - when the
    /// card-built world (renderer 3d) draws the view - this pass skips the flat
    /// painting only: the shown copy, the clear, the ground walk, the map marks. Everything else
    /// the draw does is game work and still runs: the animation steps and the record writes in
    /// DrawRow's order, the bucket drain, the panel update, EndOfPass. On - the default - the
    /// pass is exactly the original's.</summary>
    public bool PaintView = true;

    /// <summary>(Sky.Steps): the sky's six flat colour-15 bands
    /// become a stepped Amiga-style gradient - each band's interior steps through the in-between
    /// 12-bit colours of its two band colours, never a step thinner than a row. The colours at the
    /// original band rows and the sea below the horizon do not move. Off - the default - the view
    /// is the original's.</summary>
    public bool SkySteps { get => _sky.Steps; set => _sky.Steps = value; }

    /// <summary>(MessageLog): the panel's 2-line message window can
    /// be scrolled back through the last 64 lines that left it (rules/Text.cs MessageHistory,
    /// recorded only while this is on). Off - the default - nothing is recorded and every byte is
    /// the original's. On, the panel buffer and the game bytes are still the original's: only
    /// Screen() draws differently, while the position stands back (MessageLogBack > 0).</summary>
    public bool MessageLog
    {
        get => State.Panel.Text.MessageLog;
        set
        {
            var t = State.Panel.Text;
            t.MessageLog = value;
            t.NewMessage = value ? () => _messageBack = 0 : null;   // a new message returns to live
            if (!value) _messageBack = 0;
        }
    }

    /// <summary>How many lines the message window stands scrolled back (0 = live, the window as
    /// the original draws it). Set by ScrollMessageBack, cleared by Text's NewMessage; drawing only.</summary>
    public int MessageLogBack => _messageBack;
    int _messageBack;

    /// <summary>Scroll the message window back (lines > 0) or toward live (lines < 0) through the
    /// history, clamped so the two lines shown never pass the oldest kept one (the history's
    /// count: the two oldest can be the two shown). The shell's mouse wheel over the window and
    /// Page Up / Page Down come here; only in play, where the window is the game's.</summary>
    public void ScrollMessageBack(int lines)
    {
        if (!MessageLog || Mode != PlayMode.Play) return;
        _messageBack = Math.Clamp(_messageBack + lines, 0, State.Panel.Text.MaxMessageBack);
    }

    // ENHANCEMENT (MessageLog), drawing only: the window's two lines drawn from the history over
    // the copy in screen (ScreenIndices' own buffer - the panel buffer keeps the live lines, and
    // only plane 0, the text plane, is written). Back k: the top line is the k-th back from the
    // newest of the history and the line under it the next newer - back 1's lower line is the
    // live top line, which the buffer already holds. Then the scrolled-back mark: the main font's
    // '^' (glyph 0x5E - rows 0x20/0x50/0x88, a peak spreading down; no font has a true up-arrow,
    // both were dumped) in plane 0 inside the window's right end (x 304-311, 8 rows). Not
    // x 312-319: those border columns carry decor pixels whose palette indices are already odd,
    // so an OR of plane 0 there would not show (found by diffing whole frames).
    void DrawMessageHistory(byte[] screen)
    {
        var t = State.Panel.Text;
        var h = t.MessageHistory;
        var back = Math.Min(_messageBack, t.MaxMessageBack);
        if (back < 1) return;
        int left = t.WindowLeftPx, top = t.WindowTopRow, width = t.WindowWidthPx, lineH = t.WindowLineH;
        // the lines in order: the history, then the window's two live lines (read off the copy before it is
        // cleared); back k shows lines n-2-k and n-1-k of them - back 1 = the newest kept line over the live top
        // line. The window's text plane is cleared first: drawn over the live text, the two mixed (lead's review)
        byte[] Live(int line)
        {
            var b = new byte[width * lineH];
            for (var y = 0; y < lineH; y++)
                for (var x = 0; x < width; x++)
                    b[y * width + x] = (byte)(screen[(PanelTop + top + line * lineH + y) * 320 + left + x] & 1);
            return b;
        }
        var seq = new List<byte[]>(h) { Live(0), Live(1) };
        for (var y = 0; y < 2 * lineH; y++)
            for (var x = 0; x < width; x++)
                screen[(PanelTop + top + y) * 320 + left + x] &= 0xFE;
        for (var line = 0; line < 2; line++)
        {
            var bits = seq[seq.Count - 2 - back + line];
            for (var y = 0; y < lineH; y++)
                for (var x = 0; x < width; x++)
                    if (bits[y * width + x] != 0)
                        screen[(PanelTop + top + line * lineH + y) * 320 + left + x] |= 1;
        }
        for (var line = 0; line < 8; line++)
        {
            var bits = State.Panel.MainFontLine('^', line);
            for (var x = 0; x < 8; x++)
                if ((bits & (0x80 >> x)) != 0)
                    screen[(PanelTop + top + line) * 320 + left + width - 8 + x] |= 1;
        }
    }

    /// <summary>The level the view draws at with SmoothLight on, in 60ths of a level:
    /// table[h] * 60 + (table[h+1 mod 24] - table[h]) * minute, the table read from main.bin at
    /// $C4A4 as Panel.Hour does ($C462-$C466) - or -1 whenever the original's integer FadeLevel
    /// applies: a screen showing, a flare burning ($A2 bit 0, or $184 bit 15 still holding the
    /// sky's colour), a visit's fade wait, the death fade, or a fade that is not the hour's own.
    /// The hour's own fade (FadeToLight $C47C, target = the new hour's level, from the old hour's)
    /// is drawn over too: the drift already stands at the new level at minute 0, and handing over
    /// to FadeLevel there would step the picture back to the old hour's level and up again.
    /// Drawing only: no rule, timer or save reads it - FadeLevel and SkyLevel keep the original's
    /// values everywhere else (VerticalBlank's fade stepper, Screens, Demo, SaveGame).</summary>
    public int SmoothDrawLevel
    {
        get
        {
            if (!SmoothLight || Mode != PlayMode.Play || DeathFading || _visitInto is not null) return -1;
            if ((State.Flags0A2 & 1) != 0 || (State.Light184 & 0x8000) != 0) return -1;
            int Table(int h) => _d.Main[0xC4A4 - 0x800 + (h + 24) % 24];            // the hourly light table $C4A4
            var a = Table(State.Hour);
            var settled = FadeLevel == FadeTarget && FadeLevel == State.DaylightLevel;
            if (!settled && !(_hourFade && FadeTarget == State.DaylightLevel))
                return -1;                                                   // neither settled on the hour's level nor the hour's own fade to it
            return a * 60 + (Table(State.Hour + 1) - a) * State.Minute;
        }
    }

    // SmoothLight's note of the hour's own fade: the hour's level ($18F, PanelHour $C466) changed
    // while the light stood settled on the old one. Any other fade - the start's fade-in from
    // black, a flare's way back, a visit - is not it, so those draw at the original's levels.
    int _lastDaylight = -1;
    bool _hourFade;

    void NoteHourFade()
    {
        if (State.DaylightLevel != _lastDaylight)
        {
            _hourFade = _lastDaylight >= 0 && FadeLevel == _lastDaylight;
            _lastDaylight = State.DaylightLevel;
        }
        else if (FadeLevel == FadeTarget) _hourFade = false;
    }

    /// <summary>Faded at a level given in 60ths (SmoothLight): each nibble n -> floor(n * L / 16)
    /// with L = level60 / 60, so a whole level gives exactly Faded's n * level >> 4.</summary>
    public static ushort Faded60(ushort c, int level60) =>
        (ushort)(((c >> 8 & 15) * level60 / 960) << 8 | ((c >> 4 & 15) * level60 / 960) << 4 | (c & 15) * level60 / 960);
    /// <summary>The game's one fade, ported whole in Fade.cs ($68AC-$6AC0): every fade in this
    /// port runs through it - the hour's light, the flare, the death, the map, the store and the
    /// log book - stepped from VerticalBlank like the original's $C722 (Fade.Blank).</summary>
    public Fade Fade { get; } = new();
    /// <summary>$190: the engine's own level - 0-15; the game fades its view in to the hour's light
    /// ($2524, $69E8 - 14 at the 09:00 start, table $C4A4[9]).</summary>
    public int FadeLevel => Fade.FadeLevel;
    /// <summary>The last target a FadeToLight(2) was issued for ($C45E PanelTimeAndLight: an hour
    /// tick $C430, the flare's fire $A7CC and out $A7D4) - once per change, never per pass: a
    /// pass's re-issue would reset the start counter $19C and no fade could ever begin. -1 asks
    /// for the issue at the next blank (a screen's return, $252A -> PanelClock $255A).</summary>
    int _lastFadeTarget = -1;
    /// <summary>$68E2-$68EC: the fade's target - 15 while a flare burns ($A2 bit 0, FireFlare's
    /// FadeToLight $A7CC), else the hour's light $18F (PanelHour $C466; FlareOut $A7D4 fades back).
    /// That light byte IS the low byte of the engine's target word $18E (Fade.FadeTarget, synced
    /// from it each blank).</summary>
    int FadeTarget => (State.Flags0A2 & 1) != 0 ? 15 : State.DaylightLevel;
    bool _deathFadeOn;
    /// <summary>The view's level the moment the death fade began: its on-show colours stay there
    /// ($2F14's bank reload keeps the shown registers; the oracle's settled post-death palette is
    /// the bank at this level walked 15 steps, one channel low for each at 15).</summary>
    int _deathOnShow;
    /// <summary>The death fade is running or done: the view and panel show its colours, not the
    /// level fade's (it never undoes; the game ends 250 blanks after the death).</summary>
    public bool DeathFading => _deathFadeOn;
    /// <summary>How many of the death fade's 15 steps have run: the engine's $190 while the fade
    /// runs (for the death it is only that counter, the bank reload keeping the shown colours),
    /// frozen at 15 once it is over - a later restart's fade must not walk the death's colours back.</summary>
    public int DeathStep => _deathStep;
    int _deathStep;

    /// <summary>$2F04-$2F2C, the last death's fade of the palettes: FlareOff, MaskedAveragePalette(1)
    /// - each colour's target is its masked average, R = (r+g+b)/3 with G and B 0 ($6B4C, the
    /// unchosen bytes of the B,G,R order) - then the palette bank set at level 15 AT ONCE
    /// (SetPaletteBank's D5 = -1), the word $18E = 15 (so $18F daylight = 15), clr $190, StartFadeTo(4):
    /// the drawing Lits at 15 while DeathFading; the first step comes 2 blanks later ($19C), one
    /// every 4th after that ($69B2's d1 = 4), 15 steps.</summary>
    public void StartDeathFade()
    {
        _deathFadeOn = true;
        State.Flags0A2 &= 0xFE;                                             // $2F04 FlareOff
        State.Light184 = 0;
        State.DaylightLevel = 15;                                           // $2F22: the word $18E = 15
        _lastFadeTarget = 15;                                               // $C47C never runs here: no FadeToLight on top
        _deathOnShow = Fade.FadeLevel;                                      // the view's colours stay at its level
        _deathStep = 0;
        Fade.SnapTo(0);                                                     // $2F14 the bank reload, $2F26 clr $190 (the step counter)
        Fade.StartFadeTo(4, 15);                                            // $2F2C
    }

    /// <summary>A colour of the death fade `steps` steps in: it walks in bytes from the colour on
    /// show at the death, each channel by its step (target - now) >> 4 set once ($6AC2), so after
    /// 15 steps it is 15/16 of the way to (avg, 0, 0).</summary>
    public static ushort DeathFaded(ushort c, int steps)
    {
        var r = (c >> 8 & 15) * 16;
        var g = (c >> 4 & 15) * 16;
        var b = (c & 15) * 16;
        var avg = (r + g + b) / 3;                                           // $6B5C-$6B6C divu 3
        return (ushort)((r + steps * ((avg - r) >> 4) >> 4 & 15) << 8 |
                        (g + steps * ((0 - g) >> 4) >> 4 & 15) << 4 |
                        (b + steps * ((0 - b) >> 4) >> 4 & 15));
    }

    public Play(GameData d, GameMode mode, byte[]? world = null, short? energy = null, Soldier? soldier = null, int fullerWorld = 0, int players = 1, bool maths32 = false,
        int finerGround = 0, bool fixSpikes = false, ushort? groundSeed = null, int relief = 0)
        : this(d, StartState.For(mode, d, world, soldier, energy: energy, fullerWorld: fullerWorld, players: players, maths32: maths32,
            finerGround: finerGround, fixSpikes: fixSpikes, groundSeed: groundSeed, relief: relief)) { }

    /// <summary>A game from a state already made: a loaded one (SaveGame.Read; PlayGame $24B4 from $23BE).</summary>
    public Play(GameData d, WorldState state)
    {
        _d = d;
        State = state;
        _objects = new Objects(State.Models,new Trig(d.Sinfile), State.Ground, _raster, State.Anim) { World = State };
        _sky = new Sky(d.Main, d.PaletteBanks);
    }

    /// <summary>(the BOT, docs/bot-plan.md): when set, player 1's joystick comes from it every blank
    /// of play instead of the port - a computer player, playing by the same rules through the same input. Null: the port.</summary>
    public IPilot? Pilot { get; set; }

    /// <summary>ENHANCEMENT (the bot's TAPES, rules/Tape.cs): told of every key and click as it is given (null: none kept).</summary>
    public TapeRecorder? Taping { get; set; }

    /// <summary>(rules/DemoFollow.cs): in the demo, each counted blank's stick, fire and keys come
    /// from it instead of the recording's replay ($C826), and the demo ends when it says (Keys.QuitKey), not when the pace
    /// table runs out (past it a pass waits for nothing, Demo.PaceWord). Null: the original replay.</summary>
    public DemoFollow.IBlank? Follow { get; set; }

    /// <summary>The game's own tables (main.bin and the rest), for a pilot that reads them as the game does.</summary>
    public GameData Data => _d;

    /// <summary>$D6: the joystick byte as the last blank read it ($C7E0).</summary>
    public byte D6 { get; private set; }
    /// <summary>$D4 bit 0: fire as the last blank left it (SpeedControl may clear it).</summary>
    public byte D4 { get; private set; }
    /// <summary>(mp_two_soldiers): player 2's own joystick and
    /// fire, read the same blank as player 1's. Nothing reads them while State.Players is 1 -
    /// one player stays byte for byte the original.</summary>
    public byte D6Second { get; private set; }
    public byte D4Second { get; private set; }
    /// <summary>$1A6: blanks since the last pass began; 1 at the start ($2530 move.l #$10001,$1A6).</summary>
    public int Blanks { get; private set; } = 1;

    /// <summary>One vertical blank (50 Hz), in the order $C6DC runs it: read the joystick ($C74C),
    /// count the blank ($C702), the game's blank work ($3578, Blank.Run), then the fade ($69B2).
    /// Zoom = the cursor up / down keys held.</summary>
    public void VerticalBlank(Joystick j = default, bool zoomOut = false, bool zoomIn = false, Joystick j2 = default, int lean = 0)
    {
        if (Pilot is not null && !State.DemoMode) Pilot.Act(this);           // ENHANCEMENT (the BOT): its keys and clicks, as between blanks
        if (Pilot is not null && !State.DemoMode && State.InPlay) j = Pilot.Next(this);   // ENHANCEMENT (the BOT, rules/Bot.cs): a computer player's stick, read where the port is
        State.AnalogLean = State.AnalogSteering && !State.DemoMode ? Math.Clamp(lean, -16, 16) : 0;   // the stick's sideways lean in 16ths (never the demo)
        byte d6 = j.D6, d4 = (byte)(j.Fire ? 1 : 0);                        // $C74C / $C7E0
        byte d62 = j2.D6, d42 = (byte)(j2.Fire ? 1 : 0);                    // mp_two_soldiers: player 2's port, read the same blank
        if (State.DemoMode) (d6, d4) = (D6, D4);                             // $C750 bmi: the demo reads no input - $D6/$D4 keep the replay's
        State.WeightyNow = State.WeightyHandling && !State.DemoMode && Pilot is null;   // VEHICLE HANDLING: WEIGHTY: a hand at the stick only
        State.DriveStick = d6;
        if (State.InPlay) State.Channels.Tick();                             // $C6BA-$C6C6: the sample player's tick while play runs
        if (State.InPlay && Demo.CountBlank(State))                          // $C6DE btst #7,$99: not while paused; $C6E6 the demo's cap
        {
            Blanks++;                                                        // $C702
            if (State.DemoMode && Follow is not null) Follow.Blank(this, ref d6, ref d4);   // ENHANCEMENT (DemoFollow): the demo by its route
            else if (State.DemoMode) Demo.Replay(this, ref d6, ref d4);      // $C706 -> $C826
            Blank.Timers(State, d6);                                         // $C710 $6C46
            Blank.Run(State, State.Entities.All[0], zoomOut, zoomIn, ref d6, ref d4);   // $C714 $3578
            if (State.Players > 1 && State.Entities.Count > 1 && State.Entities.All[1].Alive)   // mp_two_soldiers:
                Blank.Speed(State.Entities.All[1], ref d62, ref d42);        // player 2's own $35F6 with his joystick
            if (State.DeathFadePending)                                          // $2F04 ran in the death's blank
            {
                State.DeathFadePending = false;
                StartDeathFade();
            }
        }
        var extra = _leaving && ExtraLeaveBlanks > 0;                        // a blank before LeavePlay's (a store's message took one)
        if (extra) ExtraLeaveBlanks--;
        var opened = _leaving && !extra;                                     // the table's first hover is the next blank
        if (opened)                                                          // LeavePlay's blank is over: $2E0A bclr #7,$99
        {
            _leaving = false;
            State.InPlay = false;
            if (_opening == 0x3CFA)                                           // MapScreen goes on after $3CFA bsr LeavePlay:
            {                                                                 // $3CFE FlareOff + $3D0A WriteBackAllIfOnFoot (Screens.OpenMap), then
                Screens.OpenMap(View);
                Fade.JumpFadeLevel(-16);                                      // $3D10: down at once, stopping at 1 ($6A04's bound)
                Fade.FadeIn();                                                // $3D2C: back up to 15, a step a blank, waited (clicks below)
            }
            if (_opening == 0x3CFA && State.DemoMode) _demoMapWait = 201;    // $3D30 bmi -> $3D36 WaitClickOrDemoDelay: 201 blanks, no buttons
            if (_opening == 0xD2EC) { _logOpening = true; Fade.FadeToBlack(); }   // LogBook $D2EC: $D318 fades the view out first, waited
            if (_opening == 0xA576) Screens.OpenItems(_shown);               // ItemTable after $A57C bsr LeavePlay (no fade of its own)
            if (_opening == 0x9A78) { StopPlayFade(); _storeOpening = true; }   // $4734 StopPlay's fade out, waited, then StoreScreen
            _opening = 0;
        }
        D6 = d6; D4 = d4;
        D6Second = d62; D4Second = d42;
        // The screens' waited fades ($6918/$691E/$6924 all end in the $6952 WaitFade spin): the work
        // the original ran once one released runs here, on the first blank after its last step.
        if (_mapLeaving && !Fade.FadeActive)                                  // $3D4E FadeToBlack over: $3D52-$3D60, then $252A
        {
            _mapLeaving = false;
            Screens.LeaveMap(View);
            BackToPlay();                                                     // $255A: FadeToLight(2) issued below, this blank
        }
        if (_logOpening && !Fade.FadeActive)                                  // $D318 FadeToBlack over: the book drawn, $D370 FadeIn
        {
            _logOpening = false;
            Screens.OpenLog(View);
            Fade.FadeIn();
        }
        if (_logLeaving && !Fade.FadeActive)                                  // $D300 FadeToBlack over: $D304 the kept view back, $252A
        {
            _logLeaving = false;
            Screens.LeaveLog(View);
            BackToPlay();
        }
        if (_storeOpening && !Fade.FadeActive)                                // StopPlay's fade over: StoreSetUp ($9AA8), ending
        {                                                                     // $9ABE fading bank 3 in from black, waited
            _storeOpening = false;
            _panelFades = false;
            Screens.OpenStore();
            Fade.FadeInBank(_d.PaletteBanks[3], 0);
        }
        if (_storeLeaving && !Fade.FadeActive)                                // $9A84 FadeToBlack over: $473C LeaveStore
        {
            _storeLeaving = false;
            LeaveStore();
        }
        if (_playStarting && !Fade.FadeActive)                                // PlayStart's FadeIn over ($251A): $251E the view's
        {                                                                     // 16 colours only, $2526 JumpFadeLevel(-15) down to 1, $252A
            _playStarting = false;
            _panelFades = false;
            Fade.JumpFadeLevel(-15);
            State.InPlay = true;
            State.Clickable = true;                                          // $2550 bset #7,$96
            Blanks = 1;
            LastTicks = 1;                                                   // $2530 move.l #$10001,$1A6
            _lastFadeTarget = -1;                                            // $255A PanelClock: FadeToLight(2), this blank
        }
        if (_ending > 0 && --_ending == 0) State.InPlay = false;             // $24F2 bclr #7,$99 after StopPlay's 29 blanks
        if (_demoMapWait > 0 && !opened && Fade.FadeLevel == 15 && !_mapLeaving && --_demoMapWait == 0)   // $AC34-$AC36 dbf done (counted after
            _mapLeaving = true;                                              // $3D2C's waited FadeIn): $3D3A bra $3D4E, as RESUME
        if (Mode == PlayMode.Screen && !opened && !ButtonDown && _opening == 0 && Screens.Routine == 0xA576 && Screens.Hover())
            Mode = PlayMode.Play;                                            // WaitItemClick $A924 per blank: the pointer left
        if (SmoothLight) NoteHourFade();                                     // ENHANCEMENT (SmoothLight): drawing state only
        // $C45E PanelTimeAndLight -> $C47C FadeToLight(2): issued once per target change - an hour
        // tick ($C430), the flare's fire ($A7CC) and out ($A7D4), and the restart ($255A, _lastFadeTarget
        // -1) - never per pass, or the start counter $19C would reset every blank and no fade would begin
        Fade.FlareOn = (State.Flags0A2 & 1) != 0;
        Fade.FlareLight = State.Light184;
        Fade.FadeTarget = State.DaylightLevel;
        if (Mode == PlayMode.Play && _visitInto is null && !_playStarting && FadeTarget != _lastFadeTarget)
        {
            _lastFadeTarget = FadeTarget;
            Fade.FadeToLight(2);                                             // toward the hour's light, 15 while a flare burns
        }
        if (_visitInto is not null) VisitWaitBlank();                        // a visit's FadeToAndWait ($4758): the room's set-up blanks
        Fade.Blank();                                                        // $C722 FadeStep ($95 bit 5) + the $6234-$6250 start counter
        if (_deathFadeOn && _deathStep < 15) _deathStep = Fade.FadeLevel;    // the death's steps: frozen at 15 once it is over
        // WriteFadedColours $6A4A skips colour i while bit i of $184 is set ($6A62 base colours, $6A98 the copper's):
        // a flare ($A7C0 bit 15) leaves colour 15 - the sky band and its six horizon reloads - at the level it had
        if ((State.Light184 & 0x8000) == 0 && !_deathFadeOn) SkyLevel = FadeLevel;   // not through the death: $190 is its step counter then
        if (_press && State.Clickable && Mode == PlayMode.Play)              // $C726: a button newly down ($96 bit 6 edge)
            Keys.Click(State, 0);                                            // $C73E HitTestMouse $AF64
        _press = false;
    }

    /// <summary>A key going down (KeyDown $B214, from the keyboard interrupt; Keys.cs). shift /
    /// ctrl: either shift key or ctrl held. d7: the interrupted code's d7, which the flare and the
    /// drone's FireShot read (bits 25 and 31); the remake's keys come between passes, where the
    /// original's d7 is whatever DrawView left (tests pass the oracle's).
    /// In the demo every key (a release too) is QuitKey before it is decoded ($B1E6 bmi -> $B20C);
    /// the recording's own keys go to Keys.Down from the blank (Demo.Replay).</summary>
    public void KeyDown(byte raw, bool shift = false, bool ctrl = false, int d7 = 0)
    {
        Taping?.Key(raw, shift, ctrl, d7);                                   // ENHANCEMENT (the bot's TAPES, rules/Tape.cs)
        if (State.DemoMode) Keys.QuitKey(State);
        else Keys.Down(this, raw, shift, ctrl, d7);
    }
    int _demoMapWait;

    // The screens' fades, staged over blanks in VerticalBlank (the map's $3D2C/$3D4E, the log book's
    // $D318/$D300, the store's $9ABE/$9A84): a flag goes up with the fade's issue, and the work the
    // original ran after its WaitFade spin released runs on the first blank the fade is over.
    bool _mapLeaving;                                                        // RESUME / the demo's wait: $3D4E FadeToBlack first
    bool _logOpening;                                                        // LogBook $D2EC: $D318 FadeToBlack first, the book drawn after
    bool _logLeaving;                                                        // the log's Resume $D258: $D300 FadeToBlack first, $D304 after
    bool _storeLeaving;                                                      // the store's END: $9A84 FadeToBlack first, LeaveStore after
    bool _storeOpening;                                                      // a store's door: StopPlay's fade out first, StoreSetUp after
    bool _playStarting;                                                      // PlayStart's FadeIn ($251A), waited: play begins after it
    int _ending;                                                             // StopPlay's blanks left at a quit ($24DA-$24F2)
    /// <summary>The fade walks the panel's colours too: SetPaletteBank's copper scan ($6C40) counts
    /// them in $192 until PlayStart's $251E takes them off - StopPlay's fade out and PlayStart's FadeIn.</summary>
    bool _panelFades;
    /// <summary>The level the panel's colours show at: 15 in play, the fade's own while it walks them.</summary>
    public int PanelLevel => _panelFades ? FadeLevel : 15;
    /// <summary>PlayStart's fade runs: no pass meanwhile.</summary>
    public bool Starting => _playStarting;

    /// <summary>Test only (the day test, a frozen game whose clock runs on): the blank's light steps alone - the fade
    /// toward the hour's light and the sky's level following it - so the picture brightens and darkens with the hours.</summary>
    public void DayTestLight()
    {
        Fade.FadeTarget = State.DaylightLevel;
        if (FadeTarget != _lastFadeTarget) { _lastFadeTarget = FadeTarget; Fade.FadeToLight(2); }
        Fade.Blank();
        if ((State.Light184 & 0x8000) == 0 && !_deathFadeOn) SkyLevel = FadeLevel;
    }
    /// <summary>A screen's waited fade runs (a store's door, the store's, the map's, the log book's):
    /// the original takes no click until its WaitFade spin lets go.</summary>
    public bool ScreenFading => Mode == PlayMode.Screen && (_storeOpening || _logOpening || Fade.FadeActive);
    /// <summary>A quit's StopPlay blanks run: no pass meanwhile.</summary>
    public bool Ending => _ending > 0;

    // StopPlay ($24DA, a store's door and the quit's $24D6): FlareOff, SetPaletteBank(-1, -1) - the
    // bank reloaded at 15 at once, panel and all - then FadeOut(1), waited: 15 steps over 29 blanks
    void StopPlayFade()
    {
        State.Flags0A2 &= 0xFE;                                              // $24E0 FlareOff $689A
        State.Light184 = 0;
        _lastFadeTarget = FadeTarget;                                        // the flare's end issues no FadeToLight here
        Fade.SnapTo(15);                                                     // $24E8 SetPaletteBank: $190 = 15 & -1
        _panelFades = true;
        Fade.FadeOut(1);                                                     // $24EE
    }

    // PlayStart ($24FA) from $251A: FadeIn from black over every colour, the panel's too, waited;
    // the rest runs when it is over (VerticalBlank). The view on show is the buffer the last pass
    // drew ($2510 ClearScreen clears the other).
    void PlayStartFade()
    {
        State.InPlay = false;
        State.Clickable = false;
        Fade.SnapTo(0);
        _panelFades = true;
        Fade.FadeIn();
        _playStarting = true;
    }

    /// <summary>A new game's PlayStart (GameView: not the replays, whose recordings start at $2530).</summary>
    public void StartPlay() => PlayStartFade();

    /// <summary>A key on a screen before the game's first pass (the soldier screen, the briefing): the original's
    /// $99 is cleared there ($23B6) and bit 7 waits for MainLoop $2560, so KeyDown reaches only 'p' - which then
    /// starts the game paused. The port keeps "in play" set until its first pass (its blank counting needs it),
    /// so it is cleared just for this key.</summary>
    public void KeyDownBeforePlay(byte raw, bool shift = false)
    {
        var was = State.InPlay;
        State.InPlay = false;
        Keys.Down(this, raw, shift, false, 0);
        State.InPlay = was;
    }

    /// <summary>The mouse pressed at ($DE, $E0) = (x 0-320, the panel's row 0-60: screen row 140 +
    /// y): the pointer moves there and the next blank hit-tests the press ($C726 -> $AF64).
    /// y 0 is the pointer at the panel's top: EndOfPass opens the item table ($34C0).</summary>
    public void Click(int x, int y)
    {
        Taping?.Click(x, y);                                                 // ENHANCEMENT (the bot's TAPES, rules/Tape.cs)
        if (State.DemoMode) return;                                          // $C750: the demo reads no mouse (its buttons stay the replay's)
        if (Mode == PlayMode.Screen)                                        // WaitMenuButtonOrTimeout ($1FA0): the screen's buttons
        {
            if (Screens.Routine == 0x9A78)                                   // the store's loop ($9A7A): its regions
            {
                if (Fade.FadeActive || _storeLeaving) return;                // $9ABE's fade-in is waited; so is END's way out ($9A84)
                if (Store.Click(State, x, y)) { Fade.FadeToBlack(); _storeLeaving = true; }   // END: out at $9A84, LeaveStore after
                return;
            }
            if (Screens.Routine == 0xA576)                                   // WaitItemClick $A8E4: a cell; back in play at $A5DC
            {
                if (Screens.ItemClick(x, y, ButtonDown)) Mode = PlayMode.Play;   // held: back in play at the let go (ButtonUp)
                return;
            }
            if (Screens.Routine == 0x3CFA && (Fade.FadeActive || _mapLeaving)) return;   // $3D2C/$3D4E are waited: no clicks
            if (Screens.Routine == 0xD2EC && Fade.FadeActive) return;        // $D318/$D300 are waited: no clicks
            if (Screens.Routine == 0 || !Screens.Click(View, x, y)) return;
            if (Screens.Routine == 0x3CFA) { Fade.FadeToBlack(); _mapLeaving = true; return; }   // RESUME: $3D4E fades out first
            if (Screens.Routine == 0xD2EC) { Fade.FadeToBlack(); _logLeaving = true; return; }   // Resume: $D300 fades out first, $D304 after
            BackToPlay();
            return;
        }
        Mouse(x, y);                                                         // $C77C moved the pointer at the blank already
        _press = true;
    }
    bool _press;

    // back via $252A: $AC48 waits for a blank first, then $2530 move.l #$10001,$1A6 (time
    // step 1 so far), $2550 bset #7,$96, PanelClock, rts to MainLoop whose $2560 sets $99
    // bit 7 before any further blank: play counts again from here
    void BackToPlay()
    {
        Mode = PlayMode.Play;
        State.InPlay = true;
        Blanks = 1;
        LastTicks = 1;
        _lastFadeTarget = -1;                                                // $255A PanelClock: FadeToLight(2) at the next blank
    }

    /// <summary>The pointer moved to ($DE, $E0) = (x 0-320, the panel's row 0-60) with no press
    /// ($C77C: the original moves the pointer every blank, to the limits the screen set). The
    /// move alone is what hover is: in play the pointer resting at the panel's top (y 0) opens
    /// the item table at the end of the pass ($34C0), with no click; on the item table
    /// WaitItemClick ($A924, Play.VerticalBlank) highlights the carried item under it per blank
    /// and leaves below the table's row $30. On the map, the log book and the store the pointer
    /// moves too, but those read it only on a press. Click = Mouse + the press.</summary>
    public void Mouse(int x, int y)
    {
        var maxY = Mode == PlayMode.Screen ? Screens.Routine switch
        {
            0xA576 => 108,                                                   // MouseLimits(0, 92, 320, 108): $E0 0-108
            0x3CFA or 0xD2EC => 140,                                         // MouseLimits(0, 0, 320, 140): $E0 0-140
            _ => 199,                                                        // the store: its clicks take the screen point
        } : 60;                                                              // $60D8, screen mode 3: the panel's rows
        State.PointerX = (short)Math.Clamp(x, 0, 320);                       // $C7AE-$C7B8, $EA
        State.PointerY = (short)Math.Clamp(y, 0, maxY);                      // $C7CE-$C7D8, $EC
    }

    /// <summary>ResumeAfterSave ($23D2): PlayGame ($24B4) again over the same world - PlayStart
    /// ($24FA), SpawnPlayer ($87B6), MainLoop - with no ResetGameState and no briefing: the
    /// world, the clock and the soldier are where the quit left them. The quit's GameToSoldier
    /// ($24D6) moved the item counts into the stock column, so SpawnPlayer's $9DEC moves them
    /// back (its $9E44 flag was cleared by the first $9DEC, at the game's start: stock alone).
    /// The player's slot is freed there ($8300) and SpawnPlayer re-spawns it from the soldier's
    /// record +$20 - the record $24D6 wrote it from (PlayGame below does both).</summary>
    public void Resume() => PlayGame();                                      // $23D2 bsr $24B4

    /// <summary>ENHANCEMENT (easy_saves' F5): a save made while the game plays on. The original saves
    /// only after play has ended, and SaveGame.Write's GameToSoldier ($24D6) frees the player's slot
    /// and moves the counts into the stock column, its SetClock clears the minute's wait - so the file
    /// is written the original's way and everything those two change is put back: the running game
    /// goes on as if no save was made. The quit's write-back runs here too ($2594 WriteBackAllIfOnFoot $33E6:
    /// every live record into its grid cell, $8298): the menu's save never finds a thing that came
    /// alive missing from the grid. What the write-back touches is put back as well (entities, grid,
    /// the roomy_memory extras); the running game is not.
    /// NOT put back: the sound channel a write-back stops ($82A8, its counter's bit 2) - Channels
    /// cannot be snapshotted here, and the entities come back live, so the sound code re-starts them
    /// on the next pass, the same silence the original's quit makes to the next pass.</summary>
    static void Refill<TK, TV>(Dictionary<TK, TV> into, Dictionary<TK, TV> from) where TK : notnull
    {
        into.Clear();
        foreach (var (k, v) in from) into[k] = v;
    }

    public byte[] SaveInPlay()
    {
        var s = State;
        var player = (byte[])s.Entities.All[0].B.Clone();
        var soldier = (byte[])s.Soldier.R.Clone();
        var record = (byte[])s.PlayerRecord.Clone();
        var items = (byte[])s.Items.Clone();
        var (i238, i23C, fuelLow, wait) = (s.Items238, s.Items23C, s.PlayerFuelLow, s.ClockWait);
        var live = s.Entities.Bytes();                                       // the write-back frees every slot
        var grid = (byte[])s.Grid.Clone();
        var parkedFuel = new Dictionary<int, byte>(s.FuelLow);
        var overflow = s.Overflow.ToDictionary(kv => kv.Key, kv => kv.Value.Select(r => (byte[])r.Clone()).ToList());
        var insideFuel = new Dictionary<int, byte>(s.BuildingFuelLow);
        WriteBackAllIfOnFoot(s);                                                     // $2594, as the quit reaches it
        var block = SaveGame.Write(s);
        for (var n = 0; n < s.Entities.Count; n++)
            Array.Copy(live, n * EntityTable.Size, s.Entities.All[n].B, 0, EntityTable.Size);
        grid.CopyTo(s.Grid, 0);
        Refill(s.FuelLow, parkedFuel);                                       // in place
        Refill(s.Overflow, overflow);                                        // (a new dictionary would cut the current face loose from its own)
        s.BuildingFuelLow = insideFuel;
        player.CopyTo(s.Entities.All[0].B, 0);
        soldier.CopyTo(s.Soldier.R, 0);
        record.CopyTo(s.PlayerRecord, 0);
        items.CopyTo(s.Items, 0);
        (s.Items238, s.Items23C, s.PlayerFuelLow, s.ClockWait) = (i238, i23C, fuelLow, wait);
        return block;
    }

    /// <summary>$23BE StartAfterLoad, the options screen's Re-brief (the disk menu's Load returns
    /// here too, over a rebuilt world): Games.StartAfterLoad runs ResetGameState ($2414; the
    /// $23C6 copy-protection add is left out); PreMissionSoldier $86F2's SoldierToGlobals ($87A0)
    /// makes the soldier record the quit wrote back the play's numbers again; its soldier panel
    /// and briefing ($8818/$9488) are the shell's screens. Then the same PlayGame as Resume.</summary>
    public void Rebrief(bool musicWasPlaying)
    {
        Games.StartAfterLoad(State, musicWasPlaying);
        var s = State;
        var sol = s.Soldier;
        s.Status216 = sol.R[0]; s.Score = sol.Long(0xC); s.Energy = (short)sol.Word(6);   // $87A0
        PlayGame();
    }

    /// <summary>PlayGame ($24B4) over the state as it stands: PlayStart ($24FA), SpawnPlayer
    /// ($87B6, whose $9DEC moves the quit's stock column back into the counts), MainLoop. What
    /// the state holds decides the game: after a Re-brief ResetGameState has cleared the weapon
    /// ($200) and the camera, so the soldier starts with weapon 0 and the camera at $1000.</summary>
    void PlayGame()
    {
        var s = State;
        s.Log.BuildMarks(s.Terrain);                                         // $24FA $409C
        Objects.InitialAnim(_d.Main).CopyTo(s.Anim, 0);                      // $24FE $5762 ResetAnimations
        s.Panel.SetUp(s);                                                    // $2516 $9A3E PanelSetUp (+ PanelClock $255A)
        // $24C8 bsr $87B6 SpawnPlayer: the player's slot is freed ($8300) and the 16-byte record
        // the quit's StoreEntityRecord wrote (soldier +$20) is spawned back into slot 0 - a save
        // made in a vehicle resumes on foot beside it. The camera follows the new player ($374C).
        var rec = new byte[16];
        Array.Copy(s.Soldier.R, 0x20, rec, 0, 16);
        StartState.SpawnPlayer(s, rec);
        var p = s.Entities.All[0];
        s.Camera = s.Ground.Place(p.UW(0x4A), p.W(0x4C), p.UW(0x4E), s.Distance, s.Lift);
        for (var i = 0; i < 30; i++)                                         // $87B6 $9DEC: stock -> count, $9E3A sf $9E44
        {
            var o = 0x28 + i * 32;
            var n = s.Items[o + 0xE] << 8 | s.Items[o + 0xF];
            s.Items[o] = (byte)(n >> 8); s.Items[o + 1] = (byte)n;
            s.Items[o + 0xE] = 0; s.Items[o + 0xF] = 0;
        }
        Items.Carried(s);                                                    // $9E40 bra $A2A8 CarriedMasks
        Vehicles.LoadWeapon(s, s.Entities.All[0]);                           // $87DC / $87E2 LoadWeapon -> WeaponIcon
        s.Flags0A2 = 0; s.Flags0A5 = 0; s.Replan = false;                    // $24BE sf $A2, $24C2 sf $A5 ($24C2's $A0 unported)
        Mode = PlayMode.Play;
        Quit = false;
        Options = null;
        Ended = null;                                                        // each game's end runs its own debrief ($23DC -> $96EA): the last one's kept
                                                                             // here sent mission 2's hand-in back to mission 2's briefing (bot one-game run 11-09)
        s.PointerY = (short)Math.Min(s.PointerY + 2, 60);                    // $250C addq #2,$E0 (the menus' y, clamped to 60)
        PlayStartFade();                                                     // $251A-$2560: play begins once it is over
    }

    /// <summary>The pause: 'p' set $99 bit 5 and MainLoop sits in PauseLoop ($34E2) - the blank
    /// counts nothing and runs no game work ($99 bit 7 clear) until 'p' again.</summary>
    public bool Paused => _pauseLoop;
    bool _pauseLoop;

    /// <summary>Keys.Down after 'p': PauseLoop's poll ($34E6) sees $99 bit 5 clear and goes on with
    /// the pass ($34EE bset #7,$99). True when this key ended the pause.</summary>
    internal bool PauseLoopEnds()
    {
        if (!_pauseLoop || State.PauseKey) return false;
        _pauseLoop = _leaving = false;
        State.InPlay = true;
        return true;
    }

    /// <summary>The game left play (QuitKey $351C: Esc or the panel's QUIT, at $2586): every slot
    /// was written back (WriteBackAllIfOnFoot $33E6) and MainLoop returned to PlayGame ($24D6). What
    /// follows is the menus (StartSequence); $A5 bit 6 asks for the save first (SaveQuitClick, task 9).</summary>
    public bool Quit { get; private set; }

    /// <summary>The rest of PlayGame after MainLoop ($24D6-$24F2) for a game that left play: GameToSoldier
    /// (the soldier record, the player's record, the items' counts into the stock column), then
    /// ResetChannels (every channel set to the silent sound 1), StopPlay's fade out (StopPlayFade: 15 steps, 29 blanks) in which the game's blank work still runs, for $99 bit 7 is cleared only at $24F2:
    /// the clock may count a minute. A save follows this (SaveGame.Write).</summary>
    public void EndPlay(Joystick j = default)
    {
        BeginEndPlay();
        for (var n = 0; n < 29; n++) VerticalBlank(j);                       // $24F2 bclr #7,$99 at the last: keys reach only 'p' now
    }

    /// <summary>EndPlay up to its blanks: the shell runs those itself, one a frame, so the fade
    /// out shows (Ending while they run).</summary>
    public void BeginEndPlay()
    {
        SaveGame.GameToSoldier(State);
        for (var ch = 3; ch >= 0; ch--) State.Channels.Set(1, ch, 0);            // $24DA ResetChannels $B186: SilenceChannel x 4
        StopPlayFade();
        _ending = 29;
    }

    /// <summary>The game ended by GameOver ($A5 bit 5: the end timer after the last death, a building's door)
    /// rather than the player's QuitKey: the loop's return goes on to the debrief ($23DC -> $96EA).</summary>
    public bool GameIsOver => (State.Flags0A5 & 0x20) != 0;

    /// <summary>The end of a game that is over: GameToSoldier, then the debrief judges it (Games.Debrief);
    /// its pictures are what the original waits on, one click each. Null while the game plays on or was quit.</summary>
    public GameEnd? Ended { get; private set; }

    /// <summary>The loop has returned: the debrief of a game that is over.</summary>
    public GameEnd? EndGame()
    {
        if (!Quit || !GameIsOver || State.DemoMode) return null;           // $23D6 bmi: the demo has no debrief
        return Ended ??= Games.Debrief(State, Screens.Ui);
    }

    /// <summary>The panel's SAVE + QUIT was used ($3508: $A5 bit 6, then QuitKey's bit 7): $23E6
    /// sends the game into the disk menu ($1013E) instead of the options screen.</summary>
    public bool SaveRequested => (State.Flags0A5 & 0x40) != 0 && !State.DemoMode;   // $23D6 comes first

    /// <summary>The in-game options screen after a quit ($2164; ShowMenuWaitFadeRun), when the loop
    /// returned with neither GameOver ($A5 bit 5 -> the debrief) nor the save ($A5 bit 6 -> the disk
    /// menu). The buttons are data (OptionsScreen): $23D2 Resume, $23BE Re-brief, $1013E Disk
    /// Operation, $23F6 Quit Game. Null while the game plays on, is over, or asked for the save.</summary>
    public OptionsScreen? Options { get; private set; }

    /// <summary>The loop has returned and neither end applies: the options screen the player is in.
    /// The shell (task 10) shows it and runs the button's result; the four handlers are in Options.cs.</summary>
    public OptionsScreen? InOptions()
    {
        if (!Quit || GameIsOver || SaveRequested || State.DemoMode) return null;
        return Options ??= new Menus(Screens.Ui, _d.Directory).Options();
    }

    /// <summary>The time step the last pass used ($1A8).</summary>
    public int LastTicks { get; private set; }

    /// <summary>0 = models animate every pass, as the original does. Smooth play runs a pass every
    /// blank, where the A500 blank count varies 2-14 (mean 6.3 over 1130 measured passes; mode 3 on
    /// foot, 10 in a driven tank): set 7 to step animations once per 7 blanks so legs and
    /// moving parts keep the original's rhythm (movement is time-scaled already).</summary>
    public int AnimEveryBlanks { get; init; }
    int _animBlanks;

    /// <summary>How far the animation table is toward its next step: 0 just after a step to nearly 1 at the
    /// next (_animBlanks / AnimEveryBlanks; 1 when the table is not stepped at all). The card slides the
    /// moving parts across it, as the in-between pictures do. Read-only; no rule reads it. The demo (Smooth)
    /// runs the original's passes of several blanks, the table stepping at most once a pass: its fraction is the
    /// in-between pictures' own (Between) - _animBlanks there gained the pass's surplus every pass, so it read
    /// 93 by the helicopter and the rotors stood still.</summary>
    public float AnimFraction => Smooth ? (float)Between() : SmoothAnimation || AnimEveryBlanks == 0 ? 1f : (float)_animBlanks / AnimEveryBlanks;

    /// <summary>ENHANCEMENT (SMOOTH ANIMATION, Objects.AnimShare): the joints move a share of a step every pass instead
    /// of a whole step every AnimEveryBlanks blanks; the card then shows the table as it is (AnimFraction 1).</summary>
    public bool SmoothAnimation { get; set; }

    /// <summary>One pass of the main loop ($2560) with no blank inside it: Update, then Draw.</summary>
    public View Frame()
    {
        if (Waiting || Mode == PlayMode.Screen) return View;                 // a visit's fade wait, or a screen (map, log book, store: their own loops, no pass): the blank only
        Update();
        return Draw();
    }

    /// <summary>The first part of a pass ($2560-$25A0): time step = blanks since the last pass
    /// ($620E), spawn ($8186), update ($25BC: every live record in slot order, $2610),
    /// camera ($374C), sort ($4164). On the A500 the blanks of a pass mostly fall after this,
    /// during the long draw (measured: $623A, $374C and the joystick read run 1 blank after the
    /// pass top), so the blank work (speed, camera distance) is seen by Draw: run
    /// VerticalBlank between Update and Draw to keep that order.</summary>
    public void Update()
    {
        Top();
        UpdateEntities();
        CameraAndSort();
    }

    /// <summary>WaitSwapAndDemoPace ($620E, $623A): the blanks counted so far become this pass's time step.
    /// On the A500 blanks can fall between the top of a pass and this copy, between it and the
    /// update, and between the update and the camera; tests replaying the original call Top,
    /// UpdateEntities and CameraAndSort with the blanks where the original had them.</summary>
    public void Top()
    {
        if (_visitInto is not null) EndVisitWait();                          // the original's wait ends before its next pass
        State.InPlay = true;                                                // $2560 / $47BE bset #7,$99
        LastTicks = Blanks;
        Blanks = 0;
        if (State.DemoMode) Demo.StepPace(State, Follow is not DemoFollow.Follower);   // $6226: its wait (Waiting) is over (the follower ends the demo itself)
        _drawsA = !_drawsA;                                                  // $620E SwapBuffers
        _interior = State.InsideView;
        _interiorBegun = false;                                              // a pass of the inside loop ($47A8), not of the main loop
        if (!State.PauseKey || _interior) return;                            // $256E btst #5,$99 (the inside loop has no pause check)
        // PauseLoop $34E2: LeavePlay $2E08 ($2E12: the sample player's 4 words at soundfile +$2D6
        // cleared, Channels.Silence) - then $AC3C: $1A2 is negative by now (the copper
        // interrupt counts it down, $C692), so it only waits for the next blank to clear $95 bit 7
        // ($C672)); that blank still runs in play, then $99 bit 7 is cleared. Then it waits for
        // 'p' (Paused, PauseLoopEnds) before the rest of the pass.
        Channels.Silence();
        _pauseLoop = _leaving = true;
    }
    bool _leaving;
    /// <summary>Blanks counted as play before LeavePlay's own after a visit's EndOfPass: the message the
    /// store's visit prints waits for one ($470E $C1B2); tests replaying the original's blanks set it.</summary>
    public int ExtraLeaveBlanks;

    /// <summary>Spawn ($8186) and UpdateEntities ($25BC) with the time step from Top.</summary>
    public void UpdateEntities()
    {
        var s = State;
        if (_interior) { if (!_interiorBegun) InteriorBegin(); }             // $47C8 CopyBackdropToView, $47CE UpdatePanel
        else StartState.SpawnNearCamera(_d, s, _objects.MoreObjects);             // $2580
        if (s.LivingWorld && ++s.LivingPass % Hunter.Rules.LivingWorld.ReplanEvery == 0) s.Replan = true;   // ENHANCEMENT (LIVING WORLD): choose again now and then
        if (s.Replan) { s.Replan = false; Behaviour.Replan(s); }             // $25BC $A5 bit 4 -> $26C0
        PaceClock(s);
        for (var p = 0; p < s.Players; p++)                                  // mp_life: every player's own $99 bit 4 /
            if (s.PlayerAt(p).RespawnPending && s.PaceTick)                      // $1536 - the dying player's own state (A500 PACE: on an A500 pass)
            { s.PlayerAt(p).RespawnPending = false; Vehicles.Respawn(s, p); }    // $25C8 $99 bit 4 -> $2E28
        Controls.Apply(_d.Main, s.Entities.All[0], D6, (D4 & 1) != 0, s.ViewAngle1);   // $25D8
        if (s.Players > 1 && s.Entities.Count > 1 && s.Entities.All[1].Alive) // mp_two_soldiers: player 2's own port drives HIS
            Controls.Apply(_d.Main, s.Entities.All[1], D6Second, (D4Second & 1) != 0,   // record - the original ran $25D8 for entity 0 only
                s.ViewAngle2);                                // mp_view_finish: his stick is relative to HIS south-facing camera
        for (var n = 0; n < s.Entities.Count; n++)                           // $2610: every live record, in slot order
            if (s.Entities.All[n].Alive) Movement.Update(s, s.Entities.All[n], LastTicks, n);
    }

    // ENHANCEMENT (A500 PACE, WorldState.A500Pace): whether this pass is an A500 pass - every PaceBlanks blanks gathered;
    // each record's height then is where its next landing's drop is measured from (Entity.PaceY). Off: every pass is one.
    void PaceClock(WorldState s)
    {
        if (!s.A500Pace) { s.PaceTick = true; s.PaceGathered = 0; s.PaceHurts.Clear(); return; }
        s.PaceBlank += LastTicks;
        s.PaceGathered += LastTicks;
        s.PaceTick = s.PaceGathered >= WorldState.PaceBlanks;
        if (!s.PaceTick) return;
        s.PaceGathered %= WorldState.PaceBlanks;
        foreach (var e in s.Entities.All) if (e.Alive) e.PaceY = e.W(0x4C);
        foreach (var k in s.PaceHurts.Where(h => s.PaceBlank - h.Value >= WorldState.PaceBlanks).Select(h => h.Key).ToList()) s.PaceHurts.Remove(k);
    }

    /// <summary>PlaceCamera ($374C) and SortAndCollideObjects ($4164).</summary>
    public void CameraAndSort()
    {
        var s = State;
        if (Quit) return;
        if (_interior)                                                       // $47D6: sort only, the camera was placed on entering
        {
            _objects.Sort(s.Entities, s.Camera, AnimateThisPass());          // the sort steps the animations indoors too (was held whenever paced)
            return;
        }
        if ((s.Flags0A5 & 0x80) != 0)                                        // $2586 bclr #7,$A5: QuitKey
        {
            s.Flags0A5 &= 0x7F;
            s.Clickable = false;                                             // $258E bclr #7,$96
            WriteBackAllIfOnFoot(s);                                                 // $2594 bra $33E6, whose rts leaves MainLoop
            Quit = true;
            return;
        }
        var p = s.Entities.All[0];
        p.SetW(0x52, p.W(0x4C));                                             // $3760: a put-back keeps this y
        s.Camera = s.Ground.Place(p.UW(0x4A), p.W(0x4C), p.UW(0x4E), s.Distance, s.Lift);
        _objects.Sort(s.Entities, s.Camera, AnimateThisPass());
    }

    // Whether the sort steps the animations this pass, and by how much (Objects.AnimShare): every pass a share with
    // SMOOTH ANIMATION, else a whole step once AnimEveryBlanks blanks have gathered (every pass when 0, the original).
    // The demo keeps the original's whole steps: its passes are 2-14 blanks, the original stepped once a pass, and its
    // in-between pictures slide them (Play.Smooth); a share of LastTicks / 7 would quicken its legs.
    bool AnimateThisPass()
    {
        _objects.AnimShare = SmoothAnimation && AnimEveryBlanks > 0 && !State.DemoMode ? 256 * LastTicks / AnimEveryBlanks : 0;
        if (AnimEveryBlanks == 0 || _objects.AnimShare > 0) return true;
        _animBlanks += LastTicks;
        if (_animBlanks < AnimEveryBlanks) return false;
        _animBlanks -= AnimEveryBlanks;
        return true;
    }

    /// <summary>Play, or a screen showing (Screens: its click is Click; ScreenIndices shows it).</summary>
    public PlayMode Mode { get; private set; }
    Screens? _screens;
    public Screens Screens => _screens ??= new Screens(State, Ui.Load(_d.Directory), _d.Animation);
    int _opening;
    /// <summary>A mouse button held ($D4 & $12): WaitItemClick takes the press ($A916) instead of
    /// hovering ($A924), so the panel's weapon spot is not redrawn.</summary>
    public bool ButtonDown;

    /// <summary>Both mouse buttons up ($D4 &amp; $12 clear, read at the blank): ButtonDown off, and a screen
    /// waiting in WaitButtonsUp goes on (the map after a filter: the whole map again, Screens.LetGo).</summary>
    public void ButtonUp()
    {
        ButtonDown = false;
        if (_screens?.LetGo(View) == true) Mode = PlayMode.Play;            // the item table: $A5D2-$A5F6 after the let go
    }
    readonly byte[] _shown = new byte[View.Width * View.Height];

    /// <summary>The screen showing as palette indices (the view buffer it draws into + the panel)
    /// with the bank per row; null in play.</summary>
    public (byte[] Pixels, int[] Bank)? CurrentScreen => Mode == PlayMode.Screen ? ScreenIndices() : null;

    /// <summary>The draw ($CA9A clear, $39B0; models animate as they are drawn, $566E), the panel
    /// and EndOfPass (endOfPass false: the caller runs EndOfPass itself, after the blanks the
    /// original had between UpdatePanel and it).</summary>
    public View Draw(bool endOfPass = true)
    {
        var s = State;
        if (Quit) return View;
        _forceShown = null;
        if (_interior)                                                       // DrawInteriorRows $4E68: the rows' objects over the backdrop
        {
            for (var row = 0; row < _objects.Rows.Length; row++)
                if (PaintView) _objects.DrawRow(View, s.Camera, s.Entities, row);
                else _objects.StepRow(s.Entities, row);                       // split-draw: the walk, no paint
            if (endOfPass) EndOfPass();
            return View;
        }
        if (PaintView)
        {
            Array.Copy(View.Pixels, _shown, _shown.Length);                  // the buffer on show while this pass draws ($14C)
            View.Clear();
            s.Ground.Draw(View, _raster, s.Camera, row => _objects.DrawRow(View, s.Camera, s.Entities, row));
            if (s.MapMarks) s.Log.DrawMarks(View, s.Entities.All[0],           // $25A4 btst #2,$99 -> $3FFE;
                s.Graves ? s.GraveStore : null);                             // the grave marks (Play.Graves) after them
        }
        else
        {
            // split-draw: the ground walk hands every row to DrawRow (Ground.cs:544); the same
            // rows in the same order, stepped without the paint
            for (var row = 0; row < _objects.Rows.Length; row++) _objects.StepRow(s.Entities, row);
        }
        s.Panel.Update(s, s.Entities.All[0]);                                // $25B0 UpdatePanel $9A60
        if (s.Panel2 is { } p2) p2.Update(s, s.Entities.All[1], 1);          // ENHANCEMENT (mp_panel): his own panel, his entity, n = 1
        _pictureCamera = null;
        if (Smooth) NoteSmoothPass();                                        // ENHANCEMENT (Play.Smooth.cs)
        if (endOfPass) EndOfPass();
        return View;
    }

    /// <summary>EndOfPass ($3476, Keys.EndOfPass); a screen routine it jumps to leaves play
    /// (LeavePlay $2E08: one more blank in play, then the screen: Screens).</summary>
    public void EndOfPass()
    {
        if (Quit) return;
        if (!_interior && State.VisitRequest)                                // $3476 bclr #1,$99: VisitBuilding $46F0
        {
            State.VisitRequest = false;
            Visit();
            return;
        }
        Keys.EndOfPass(State);
        if (State.Graves) Hunter.Rules.Graves.RetryPending(State);   // ENHANCEMENT (Play.Graves): a grave whose cell was full enters when a slot frees
        if (State.VultureGraves) Hunter.Rules.VultureGraves.Tick(State);   // ENHANCEMENT (Play.VultureGraves): the visits' pairs keep circling
        if (State.ScreenRoutine == 0) { if (_interior) AfterInteriorPass(); return; }
        _opening = State.ScreenRoutine;
        State.ScreenRoutine = 0;
        if (_opening == 0xA576) State.Clickable = false;                     // $A576 bclr #7,$96
        Mode = PlayMode.Screen;
        Channels.Silence();                                                  // LeavePlay $2E08 -> $2E12: every channel quiet
        _leaving = true;
    }

    // WriteBackAllIfOnFoot ($33E6): slots 1-29 (and the player's slot 0 too when its type is $7C) with a
    // kind (+$02) written back ($8298, its d7 the loop counter 29 - slot: bit 2 stops the channel)
    // and freed (sf (a4)), whether written or not. Multiplayer piece 1: every player slot is
    // checked the way slot 0 is - written back and freed only when a grave ($7C) - the ordinary
    // slots from s.Players always (1 today: the original exactly).
    internal static void WriteBackAllIfOnFoot(WorldState s)
    {
        for (var n = 0; n < s.Entities.Count; n++)
        {
            var e = s.Entities.All[n];
            if (n < s.Players && e.W(4) != 0x7C) continue;                   // $33EC cmpi.w #$7C,$254 (a player slot only when a grave)
            if (e.B[0] != 0 && e.W(2) != 0) Objects.WriteBack(s, e, 0x1D - n);   // $33F4-$33FE: the original's counter, whatever the slot count
            e.B[0] = 0;                                                      // $3402 sf (a4)
        }
    }

    /// <summary>The panel under the view (UpdatePanel $9A60 and the routines that draw into it).</summary>
    public Panel Panel => State.Panel;

    /// <summary>First panel row on the screen: the copper shows the view on lines $2C-$B6 (rows
    /// 0-138), turns the planes off for line $B7 (row 139, colour 0) and shows the panel buffer
    /// from row 140 (re/formats/game.md 0, "What the screen looks like").</summary>
    public const int PanelTop = 140;

    /// <summary>The screen as the copper shows it, as palette indices, with the palette bank of each
    /// row: view rows 0-138 (bank 2, its colour 15 per row from the sky bands), row 139 blank (-1),
    /// panel rows 140-199 (Panel.Palette, panfile's colours: bank 3). With the item table open:
    /// row 92 blank (-1, the moved split) and rows 93-139 bank 3 too (the panel's colours reload
    /// at the split, $6298 moving it).</summary>
    public (byte[] Pixels, int[] Bank) ScreenIndices(byte[]? view = null, Panel? panel = null)
    {
        panel ??= Panel;                                                     // the shared panel (mp_panel: player 2's screen passes his)
        var o = new byte[320 * 200];
        var bank = new int[200];
        if (Mode == PlayMode.Screen && Screens.Routine == 0x9A78 && State.StorePicture is { } store)   // a store: mode 4, the whole screen, bank 3
        {
            Array.Copy(store.Screen.Pixels, o, o.Length);
            Array.Fill(bank, 3);
            return (o, bank);
        }
        Array.Fill(bank, 2, 0, View.Height - 1);
        if (Mode == PlayMode.Screen && _screens?.Overlay is { } ov)
        {
            // the item table: MovePanelSplit ($6298, d1 = -47) moves the copper's planes-off line
            // from row 139 to row 92; the view's plane pointers fetch nothing there, so rows 93-139
            // show the buffer's rows 92-138 (as the oracle's copper picture shows). The panel's
            // colour reload rides on the same split (game.md 0: the panel's 15 colours are loaded
            // at the planes-off line, BankToCopper $6304 put panfile's CMAP there), so those rows
            // show the panel's palette - bank 3 - not the view's
            const int split = View.Height - 1 - 47;
            Array.Copy(ov, o, 320 * split);
            Array.Copy(ov, 320 * split, o, 320 * (split + 1), 320 * (View.Height - 1 - split - 1));
            Array.Copy(ov, 320 * (View.Height - 2), o, 320 * (View.Height - 1), 320);
            bank[split] = -1;                                                // the copper's planes-off line, row 92
            Array.Fill(bank, 3, split + 1, View.Height - 1 - split);         // rows 93-139, the panel's colours
        }
        else
        {
            Array.Copy(_forceShown ?? view ?? View.Pixels, o, 320 * (View.Height - 1));   // view: player 2's own picture (mp_views; _forceShown is the pass's, never his)
            bank[View.Height - 1] = -1;
        }
        Array.Copy(panel.Pixels.Pixels, 0, o, 320 * PanelTop, 320 * Panel.Rows);
        Array.Fill(bank, 3, PanelTop, Panel.Rows);
        if (_messageBack > 0) DrawMessageHistory(o);             // ENHANCEMENT (MessageLog): the window from the history
        return (o, bank);
    }

    /// <summary>The fade level the screen showing shows at - the engine's own ($190), which is
    /// each screen's fade now that they all run through it: the map's JumpFadeLevel(-16) + FadeIn
    /// ($3D10/$3D2C), the log book's FadeToBlack/FadeIn ($D318/$D370/$D300), the store's bank-3
    /// FadeInBank/FadeToBlack ($9ABE/$9A84). The item table ($A576) issues no fade, so it keeps
    /// the view's level. The oracle's recorded copper colours show the map at 15 against the item
    /// table's 14: colour 3 $7B7 (bank 2 at 15) against $7A7 (bank 2 at 14).</summary>
    public int ScreenFade => FadeLevel;

    /// <summary>The level colour 15 (the sky) was last written at: FadeLevel, except that a burning flare's
    /// bit 15 of $184 stops WriteFadedColours writing it ($6A62 / $6A98) - the ground brightens, the sky keeps
    /// its colour until FlareOut clears $184.</summary>
    public int SkyLevel { get; private set; } = 15;

    int SkyFade => SkyLevel;                                              // written from FadeLevel each blank (below), so a screen's fade walks it too

    /// <summary>The camera height the sky bands (colour 15) show: play, the item table and the
    /// log book use the camera's; the map re-arms the bands with $62B4(0) ($3D24), which leaves
    /// the horizon at line 0 - every row of the view shows the deepest band (the oracle's map
    /// picture: colour 15 = $25, the fade's $136, from the first row down).</summary>
    public bool OnMap => Mode == PlayMode.Screen && Screens.Routine == 0x3CFA;

    /// <summary>The 320 x 200 screen as $0RGB, each row through the palette bank ScreenIndices
    /// gives it: bank 2, the view in bank 2 at ScreenFade, colour 15 the sky band the row falls on
    /// (the deepest band everywhere on the map, OnMap); bank 3, the panel's rows and the item
    /// table's rows 93-139 (panfile's colours, at 15) - or a store's whole screen, whose
    /// copper loads bank 3 into all 16 registers (the oracle's store colours: CMAP at 15 from
    /// colour 1 up); -1, the copper's planes-off line, colour 0. In play the panel's colour 0 is
    /// the view's (the copper does not reload it at line $B6).</summary>
    public ushort[] Screen() => ScreenOf(View.Pixels, _pictureCamera);

    /// <summary>Player 2's screen (ENHANCEMENT, mp_views): his own picture in the view's rows
    /// 0-139, the sky maths otherwise. With two players his panel's rows (mp_panel); the shared
    /// panel when his does not exist. Null-camera fall-back: the pass's own camera (only before
    /// his first picture).</summary>
    public ushort[] Screen2 => ScreenOf(View2.Pixels, Picture2Camera, State.Panel2);

    ushort[] ScreenOf(byte[] viewPixels, Camera? cam, Panel? panel2 = null)
    {
        var bank2 = _d.PaletteBanks[2];
        var rows = _sky.Rows((cam ?? State.Camera).Height0E);               // an in-between / second picture has its own camera (Play.Smooth.cs, Play.View2.cs)
        var sky15 = OnMap ? _sky.Colours[6] : (ushort)0;                     // $62B4(0): the horizon at line 0
        var smooth = SmoothDrawLevel;                                        // ENHANCEMENT (SmoothLight): -1 = the original's levels
        ushort Lit(ushort c) => DeathFading ? Faded(c, _deathOnShow) : smooth >= 0 ? Faded60(c, smooth) : Faded(c, ScreenFade);
        ushort SkyLit(ushort c) => DeathFading ? Faded(c, _deathOnShow) : smooth >= 0 ? Faded60(c, smooth) : Faded(c, SkyFade);
        var death = DeathStep;                                               // $2F04: the bank was loaded at 15 at once; $190 is only the step counter
        var view = new ushort[16];
        for (var i = 0; i < 15; i++) view[i] = death > 0 ? DeathFaded(Lit(bank2[i]), death) : Lit(bank2[i]);
        var (px, banks) = ScreenIndices(viewPixels, panel2);
        var panel = (ushort[])(panel2 ?? Panel).Palette.Clone();
        if (death > 0) for (var i = 0; i < 16; i++) panel[i] = DeathFaded(panel[i], death);   // $192 counts the panel's too (game.md, colours)
        var wholePanel = banks[0] == 3 && banks[PanelTop] == 3;              // a store: bank 3 for every row
        if (!wholePanel) { if (_panelFades) for (var i = 1; i < 16; i++) panel[i] = Faded((panel2 ?? Panel).Raw[i], FadeLevel); panel[0] = view[0]; }
        else for (var i = 0; i < 16; i++) panel[i] = Faded(panel[i], ScreenFade);   // the store's own fade: bank 3 walked in and out ($9ABE/$9A84)
        var o = new ushort[320 * 200];
        for (var y = 0; y < o.Length / 320; y++)
        {
            var lut = banks[y] switch { 2 => view, 3 => panel, _ => null };
            if (lut is null) { Array.Fill(o, (ushort)0, y * 320, 320); continue; }
            if (banks[y] == 2) lut[15] = death > 0 ? DeathFaded(OnMap ? sky15 : rows[y], death) : SkyLit(OnMap ? sky15 : rows[y]);
            for (var x = 0; x < 320; x++)
            {
                var p = px[y * 320 + x];
                var c = lut[p & 15];
                // the EHB mark of an OwnShadows shadow: the Amiga halves the displayed register
                // (colour n + 32 = colour n, each channel >> 1), after the fade / death fade / sky
                o[y * 320 + x] = (p & View.HalfBright) != 0 ? (ushort)((c >> 1) & 0x0777) : c;
            }
        }
        return o;
    }

    /// <summary>The 16 colours the copper leaves at the view's line $40 and at the panel's line $B8,
    /// as $0RGB (WriteFadedColours $6A4A writes both from the accumulators the fade steps). The view
    /// is bank 2 at the fade level, colour 15 the sky band at the picture row the line falls on; the
    /// panel's are Panel.Palette with colour 0 the view's (the copper does not reload it).</summary>
    public (ushort[] View, ushort[] Panel) Palette(int row = 20)
    {
        var bank = _d.PaletteBanks[2];
        var rows = _sky.Rows((_pictureCamera ?? State.Camera).Height0E);    // an in-between picture has its own camera (Play.Smooth.cs)
        var smooth = SmoothDrawLevel;                                        // ENHANCEMENT (SmoothLight): -1 = the original's levels
        ushort Level(ushort c) => DeathFading ? Faded(c, _deathOnShow) : smooth >= 0 ? Faded60(c, smooth) : Faded(c, FadeLevel);
        var view = new ushort[16];
        for (var i = 0; i < 15; i++) view[i] = DeathFading ? DeathFaded(Level(bank[i]), DeathStep) : Level(bank[i]);
        // colour 15 is the sky's per-line write: the death fade walks the band itself ($6A4A's
        // 16th colour, the copper's per-line sky write over it at the fade level)
        view[15] = DeathFading ? DeathFaded(rows[row], DeathStep) : smooth >= 0 ? Faded60(rows[row], smooth) : Faded(rows[row], SkyLevel);
        var panel = (ushort[])Panel.Palette.Clone();
        if (DeathFading) for (var i = 0; i < 16; i++) panel[i] = DeathFaded(panel[i], DeathStep);
        if (_panelFades) for (var i = 1; i < 16; i++) panel[i] = Faded(Panel.Raw[i], FadeLevel);
        panel[0] = view[0];
        return (view, panel);
    }

    /// <summary>The fade's colour at a level: each channel * level >> 4 ($6A14 accumulation).</summary>
    public static ushort Faded(ushort c, int level) =>
        (ushort)(((c >> 8 & 15) * level >> 4) << 8 | ((c >> 4 & 15) * level >> 4) << 4 | (c & 15) * level >> 4);
}
