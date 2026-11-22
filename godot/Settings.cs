using Godot;

namespace Hunter;

/// <summary>The player's window settings, kept in user://settings.cfg (a Godot ConfigFile). Only
/// the shell reads them: the game's rules never see a setting, so the original's behaviour is the
/// default and the recordings are untouched. HUNTER_LOOK still wins over the file when set.
/// HUNTER_SETTINGS points the file elsewhere (a verification run's own copy); unset = user:// as ever.</summary>
public static class Settings
{
    const string DefaultPath = "user://settings.cfg", Section = "window";
    static ConfigFile? file;

    static string Path => System.Environment.GetEnvironmentVariable("HUNTER_SETTINGS") is { Length: > 0 } p ? p : DefaultPath;

    static ConfigFile File()
    {
        if (file is null)
        {
            file = new ConfigFile();
            file.Load(Path);                                       // missing file: all defaults
        }
        return file;
    }

    static readonly System.Collections.Generic.Dictionary<string, Variant> read = [];   // each value read once: the getters run every frame

    /// <summary>THE FLAT PICTURE IS THE ORIGINAL: with the 3D world off these read as the original's (each one's own
    /// default), whatever the file holds - the smooth sky, the far view, more shadows, the fuller and living world,
    /// graves, finer ground, the sound extras, stick steering, the reticle. The file keeps the player's words for the
    /// 3D world. The flat picture keeps what is not a change to the game: the window's rows, smooth animation, the
    /// fixes (bullet box, leaving a vehicle), the A500's pace.</summary>
    static readonly System.Collections.Generic.HashSet<string> FlatOriginal =
    [
        "sky_steps", "smooth_light", "sky_time", "far_view", "own_shadows", "real_shadows", "fuller_world", "more_objects",
        "living_world", "graves", "vulture_graves", "finer_ground", "ground_relief", "ground_seed", "sound_position",
        "sound_tails", "sound_voices", "music_in_play", "analog_steering", "aiming",
    ];

    static Variant Get(string key, Variant def)
    {
        if (FlatOriginal.Contains(key) && Renderer != "3d") return def;
        if (!read.TryGetValue(key, out var v)) read[key] = v = File().GetValue(Section, key, def);
        return v;
    }

    static void Set(string key, Variant v)
    {
        File().SetValue(Section, key, v);
        File().Save(Path);
        read.Remove(key);
    }

    /// <summary>"pal" (the original's TV look), "centred" (default: the same pixels, no spare TV lines) or "stretched" (square pixels).</summary>
    public static string Look
    {
        get => (string)Get("look", "centred");
        set => Set("look", value);
    }

    /// <summary>who draws the view - "software" (default: the original's picture,
    /// byte for byte) or "3d" (godot/real3d: the card draws the world from the game's own data, GpuWorld). The
    /// retired "gpu" (the flat picture coloured on the card) reads as 3d: a file that asked for the card
    /// keeps the card.</summary>
    public static string Renderer
    {
        get => (string)Get("renderer", "software") switch { "3d" or "gpu" => "3d", _ => "software" };
        set => Set("renderer", value);
    }

    /// <summary>with renderer = 3d, how many window pixels each game view pixel
    /// gets - 1, 2 or 4 (the 3D view's SubViewport at that scale), or 0 = AUTO (the default): the window's own whole
    /// scale, followed as the window changes, so one drawn pixel is one screen pixel; anything else reads AUTO.</summary>
    public static int GpuScale
    {
        get { var v = (int)Get("gpu_scale", 0); return v is 1 or 2 or 4 ? v : 0; }
        set => Set("gpu_scale", value is 1 or 2 or 4 ? value : 0);
    }

    public static bool Fullscreen
    {
        get => (bool)Get("fullscreen", false);
        set => Set("fullscreen", value);
    }

    /// <summary>The Amiga 500's fixed audio low-pass (about 4.4 kHz, 6 dB per octave, in its output stage,
    /// always on - unlike the power-light filter, which the game itself turns off at $1139A). Default on:
    /// Hunter as it sounded on an A500; off = clean, like an A1200. F6 toggles it.</summary>
    public static bool A500Filter
    {
        get => (bool)Get("a500_filter", true);
        set => Set("a500_filter", value);
    }

    /// <summary>Pause the game while the window is not the one in front (default on).</summary>
    public static bool PauseOnFocusLoss
    {
        get => (bool)Get("pause_on_focus_loss", true);
        set => Set("pause_on_focus_loss", value);
    }

    /// <summary>a MANUAL button on the main menu and the in-play
    /// options menu (the manual itself is always reachable on F3). Default off: with it off every
    /// menu is byte-identical to before (ManualScreenTests draws them and compares).</summary>
    public static bool ManualButton
    {
        get => (bool)Get("manual_button", false);
        set => Set("manual_button", value);
    }

    /// <summary>(DEMO CAMERA, 3D world only): the attract demo's camera moves by itself -
    /// the game's own camera turned round the player (rules/DemoCamera.cs). Default off: the north view.</summary>
    public static bool DemoCamera
    {
        get => (bool)Get("demo_camera", false);
        set => Set("demo_camera", value);
    }

    /// <summary>(DEMO WORLD: WILD): the attract demo plays the wild world - the fullest fill,
    /// groups beside the demo's own route, living world on (rules Wild, DemoRoute). Default off: the original's world.</summary>
    public static bool WildDemo
    {
        get => (bool)Get("demo_world", false);
        set => Set("demo_world", value);
    }

    /// <summary>A fresh settings file (nothing set yet): a new player, asked AS IT WAS / AS IT COULD BE at the
    /// first start (StartSequence). A player who already has settings is never asked.</summary>
    public static bool FirstStart => !File().HasSection(Section);

    /// <summary>The first start's answer, kept for the record: "as_it_was" or "as_it_could_be" (the PC's own, not a save's).</summary>
    public static string StartChoice
    {
        get => (string)Get("start_choice", "");
        set => Set("start_choice", value);
    }

    /// <summary>DEFAULT ON (playtest note 2): a QUIT button at the foot of
    /// the main menu that closes the program - the original has none. Off: the menu as the original.</summary>
    public static bool QuitButton
    {
        get => (bool)Get("quit_button", true);
        set => Set("quit_button", value);
    }

    /// <summary>DEFAULT ON: saving and loading without the disk
    /// handling - the first Load or Save sets the save folder up by itself (SaveDisk.Init, nothing
    /// existing is wiped), the disk menu hides Init Disk (Load Game / Save Game / End stay where
    /// the original puts them), a save left unnamed names itself (the game day and the slot), and
    /// F5 quick saves to its own file 22 with F4 loading it back ("Game saved" / "Game loaded" /
    /// "No quick save" in the message box). Default on; off = the original's disk menu exactly.</summary>
    public static bool EasySaves
    {
        get => (bool)Get("easy_saves", true);   // Always on, no menu row
        set => Set("easy_saves", value);
    }

    /// <summary>every object casts a half-bright (EHB) shadow from
    /// its own main model (rules/Objects.cs OwnShadows), where the original draws black shadows for
    /// its 13 shadow types only. Default off: with it off the picture is byte-identical to before,
    /// and the original's 13 shadows stay their colour-0 black.</summary>
    public static bool OwnShadows
    {
        get => (bool)Get("own_shadows", false);
        set => Set("own_shadows", value);
    }

    /// <summary>SHADOWS: REAL (with OwnShadows on, so the rules and the flat software picture keep MORE): the 3D view
    /// throws true shadows from the sun's depth picture (GpuWorld SunDepth, sunshadow.gdshaderinc) onto the ground and the
    /// models, and the flat pressed-down shadow pass is not drawn while the sun's picture is. Default off.</summary>
    public static bool RealShadows
    {
        get => (bool)Get("real_shadows", false);
        set => Set("real_shadows", value);
    }

    /// <summary>(row SUN, the card-drawn 3D view): the sun lights every face and ground
    /// square from its angle to it, in the game's own shades and their half-bright copies (rules/SunShade.cs); the
    /// ground's checker stays. Default off: the faces keep their flat paint, as the original. Read every frame.</summary>
    public static bool SunShading
    {
        get => (bool)Get("sun_shading", false);
        set => Set("sun_shading", value);
    }

    /// <summary>(row 3D VIEW DISTANCE, the card-drawn 3D view): how far the card draws -
    /// 7 rows (the original's patch and in-view things, the default), 11 / 15 / 23 / 31 rows, or 0 = all of the map
    /// (rules/ViewDistance.cs). The picture only; the game is untouched. Read every frame.</summary>
    public static int ViewDistance
    {
        get { var v = (int)Get("view_distance", 7); return System.Array.IndexOf(Hunter.Rules.ViewDistance.Choices, v) >= 0 ? v : 7; }
        set => Set("view_distance", value);
    }

    /// <summary>(row EXPLOSIONS, the card-drawn 3D view): a thing that blows up breaks into its
    /// own triangles, thrown out, falling, bouncing once, sinking at sea (godot/real3d/pieces.gdshaderinc), instead of
    /// the original's ten triangles. The picture only. Default off.</summary>
    public static bool CardPieces
    {
        get => (bool)Get("card_pieces", false);
        set => Set("card_pieces", value);
    }

    /// <summary>ENHANCEMENT (EXPLOSIONS: LASTING): with CardPieces, the pieces lie where
    /// they fell for good instead of shrinking away after 5 s - until the card's ring of 2048 blasts comes round to
    /// theirs. The picture only: nothing to touch or move. Default off.</summary>
    public static bool PiecesStay
    {
        get => (bool)Get("pieces_stay", false);
        set => Set("pieces_stay", value);
    }

    /// <summary>(row MODEL COLOURS, the card-drawn 3D view): those models drawn in their own shades, A1200-style, apart from the
    /// grass (GpuWorld.OwnColours). The picture only. Default off.</summary>
    public static bool OwnColours
    {
        get => (bool)Get("own_colours", false);
        set => Set("own_colours", value);
    }

    /// <summary>(row SHARKS AND SUBS, the card-drawn 3D view): the shark a whole shark under its fin, the submarine a hull and sail under its
    /// periscope (godot/real3d/SeaBodies.cs). The models are baked at launch: a change shows at the next start. The
    /// picture only. Default off.</summary>
    public static bool SeaBodies
    {
        get => (bool)Get("sea_bodies", false);
        set => Set("sea_bodies", value);
    }

    /// <summary>A build that starts a new machine on the full modern settings without asking (the export's custom feature
    /// "modern_default" - the Windows release GameView.ApplyModernDefault).</summary>
    public static bool ModernDefault => OS.HasFeature("modern_default");

    /// <summary>(row GLASS, the card-drawn 3D view): the windows'
    /// faces (Glass.cs) drawn see-through. The picture only. Default off.</summary>
    public static bool SeeThroughGlass
    {
        get => (bool)Get("glass", false);
        set => Set("glass", value);
    }

    /// <summary>(row ANIMATION): the joints move a share of a step every pass (Play.SmoothAnimation, Objects.AnimShare). The
    /// picture only - no rule reads the joints. Default ON.</summary>
    public static bool SmoothAnimation
    {
        get => (bool)Get("smooth_animation", true);
        set => Set("smooth_animation", value);
    }

    /// <summary>(row GROUND PATTERN, the card-drawn 3D view): fine blocky 16 x 16 specks
    /// (a touch lighter / darker) inside every ground square (ground_pattern.gdshaderinc), fading to plain with the haze.
    /// The picture only. Default off.</summary>
    public static bool GroundPattern
    {
        get => (bool)Get("ground_pattern", false);
        set => Set("ground_pattern", value);
    }

    /// <summary>(row GROUND COLOURS, the card-drawn 3D view): sand, grasses, the hills' browns
    /// and rock by height and slope, Vista Pro's method (rules/GroundBands.cs). The picture only. Default off.</summary>
    public static bool GroundColours
    {
        get => (bool)Get("ground_colours", false);
        set => Set("ground_colours", value);
    }

    /// <summary>(row CLOUDS, the card-drawn 3D view): a few separate blocky clouds in the sky, far
    /// away, sliding with the turn and drifting slowly on the game's clock (godot/real3d/clouds.gdshaderinc); at night also steady stars and a moon going
    /// through its phases over the game's days (godot/real3d/stars.gdshaderinc). Default off.</summary>
    public static bool Clouds
    {
        get => (bool)Get("clouds", false);
        set => Set("clouds", value);
    }

    /// <summary>(row SKY: SMOOTH, the card-drawn 3D view): the sky's colours by
    /// the sun's height and direction - golden hour, the twilights, Earth's shadow and the Belt of Venus, night - and the
    /// sea lit by that sky (godot/real3d/world_colour.gdshaderinc tod_band, docs/sky-reference.md). The picture only.
    /// Unsaved, it follows SKY: SMOOTH's sky (sky_steps), so a saved SMOOTH gets it.</summary>
    public static bool SkyTime
    {
        get => (bool)Get("sky_time", SkySteps);
        set => Set("sky_time", value);
    }

    /// <summary>(row GROUND SHAPE, the card-drawn 3D view): each drawn ground triangle cut into
    /// N x N pieces whose corners take the game's own height ($36AA), so the drawn ground follows the height the rules
    /// use (groundgpu.gdshader). Default 4: the original's flat triangles (1, row ORIGINAL) disagree with the height the
    /// rules use; 2, 4 or 8. The picture only. Recordings that compare against
    /// the original game set ground_cuts=1.</summary>
    public static int GroundCuts
    {
        get { var n = (int)Get("ground_cuts", 4); return n is 1 or 2 or 4 or 8 or 16 or 32 ? n : 4; }
        set => Set("ground_cuts", value);
    }

    /// <summary>(row HAZE, the card-drawn 3D view): 0 off, 1 BANDS (4 clear steps), 2 FINE -
    /// from halfway out to the 3D VIEW DISTANCE's far edge everything fades to the far sea's colour (world_colour
    /// world_hazed), in full colour. Default off.</summary>
    public static int Haze
    {
        get { var v = (int)Get("haze", 0); return v is 1 or 2 ? v : 0; }
        set => Set("haze", value is 1 or 2 ? value : 0);
    }

    /// <summary>(row SEA, the card-drawn 3D view): LIVING colours the sea per pixel from the
    /// hour's own colours - pale by the coast, the original's colour out at sea, slow quiet waves, the sky's bands near the horizon, full colour
    /// (world_colour sea_rgb8). Default ORIGINAL: one flat colour.</summary>
    public static bool LivingSea
    {
        get => (bool)Get("living_sea", false);
        set => Set("living_sea", value);
    }

    /// <summary>(row SEA's third choice, CYCLING; with LivingSea): the sea in blocky cells whose
    /// colours glide round a fixed ramp on the game clock, the Amiga's colour cycling (world_colour sea_rgb8). Default off.</summary>
    public static bool SeaCycling
    {
        get => (bool)Get("sea_cycling", false);
        set => Set("sea_cycling", value);
    }

    /// <summary>while the light is settled on the hour's level the
    /// view and the sky band drift minute by minute between the hour table's levels (rules/Play.cs
    /// SmoothLight), where the original holds one level for 59 minutes and then steps. Default off:
    /// with it off the picture is byte-identical to before, and at every full hour it is the
    /// original's either way.</summary>
    public static bool SmoothLight
    {
        get => (bool)Get("smooth_light", false);
        set => Set("smooth_light", value);
    }

    /// <summary>NEW, NOT THE ORIGINAL (row VIEW): with renderer = 3d the 3D view fills the
    /// window's whole width - more of the world left and right, nothing stretched; the panel stays 320 wide in
    /// the middle. Default off (4:3, the original's frame). Other renderers read false.</summary>
    public static bool WideView
    {
        get => Renderer == "3d" && (bool)Get("wide_view", false);
        set => Set("wide_view", value);
    }

    /// <summary>the sky's six flat bands become a stepped Amiga-style
    /// gradient - between each two band colours only the in-between colours the Amiga can show, one
    /// step at least a row high (rules/Sky.cs Steps); the band rows and the sea do not move. Default
    /// off: with it off the picture is byte-identical to before.</summary>
    public static bool SkySteps
    {
        get => (bool)Get("sky_steps", false);
        set => Set("sky_steps", value);
    }

    /// <summary>the ground and objects are placed and drawn farther
    /// (rules/Play.cs FarView -> Objects and Ground). Default off: with it off the picture is
    /// byte-identical to before. With it on the farther ground draws, but few things live out
    /// there yet - a later change widens the live area (more_objects).</summary>
    public static bool FarView
    {
        get => (bool)Get("far_view", false);
        set => Set("far_view", value);
    }

    /// <summary>more live things and a bigger live area - 30 live
    /// records (29 handed out, the last reserved) become 256 (255 handed out) and the spawn/live
    /// window 2 x 2 cells becomes 4 x 4 (rules/Play.cs MoreObjects). Not part of the save: the
    /// save holds no live-thing table, the game writes its live things back into the grid. Default
    /// off: with it off the game is byte-identical to before. With far_view on as well the farther
    /// view has things in it - the bigger live area is what puts live things out past $1800.</summary>
    /// <summary>LIVING WORLD - people and animals notice each other, not only the player:
    /// enemies go for armed friends, friends for enemies, civilians run, wild things charge or run (rules/LivingWorld.cs).
    /// The player's games only, never the attract demo. Default off: the original's planning. Original / On.</summary>
    public static bool LivingWorld
    {
        get => (bool)Get("living_world", false);
        set => Set("living_world", value);
    }

    /// <summary>LEAVING A VEHICLE - you step out behind it first (rules WorldState.LeaveBehind),
    /// not on its east side; the player's games only, never the attract demo. Default off. Original / Behind.</summary>
    public static bool LeaveBehind
    {
        get => (bool)Get("leave_behind", false);
        set => Set("leave_behind", value);
    }

    /// <summary>AIMING - on foot the soldier turns to a free reticle (the mouse over the view, the right
    /// stick) and his shot goes to the point under it (rules WorldState.AimReticle); never the attract demo. Default off. Original / Reticle.</summary>
    public static bool AimReticle
    {
        get => (bool)Get("aiming", false);
        set => Set("aiming", value);
    }

    /// <summary>VEHICLE HANDLING - the player's car as a body with momentum, spin and tyres (rules Handling.cs,
    /// WorldState.WeightyHandling); the player's games only, never the attract demo. Default off. Original / Weighty.</summary>
    public static bool WeightyHandling
    {
        get => (bool)Get("handling", false);
        set => Set("handling", value);
    }

    /// <summary>STICK STEERING - a gamepad stick's lean turns a vehicle by as much as it leans
    /// (rules WorldState.AnalogSteering); the player's games only, never the attract demo. Default off. Original / Analog.</summary>
    public static bool AnalogSteering
    {
        get => (bool)Get("analog_steering", false);
        set => Set("analog_steering", value);
    }

    /// <summary>LIMB DAMAGE - a bullet hurts the limb it hits and a leg or arm hit does not kill
    /// (rules WorldState.LimbDamage); the player's games only, never the attract demo. Default off. Original / On.</summary>
    public static bool LimbDamage
    {
        get => (bool)Get("limb_damage", false);
        set => Set("limb_damage", value);
    }

    /// <summary>BULLET BOX - bullets get a small box (16 across, the height kept) instead of
    /// model 0's 0 x 288 (rules WorldState.BulletBox); never the attract demo (it plays the recording exactly). Default on.
    /// Original / Small.</summary>
    public static bool BulletBox
    {
        get => (bool)Get("bullet_box", true);
        set => Set("bullet_box", value);
    }

    /// <summary>A500 PACE - what the original does once a pass (a touch's hurt, a record's end,
    /// a hard landing, ...) at the A500's ~7 blanks a pass (rules WorldState.A500Pace); never the attract demo (it plays the
    /// recording exactly). Always on: not a setting (a file's old a500_pace is not read).</summary>
    public static bool A500Pace => true;

    public static bool MoreObjects
    {
        get => (bool)Get("more_objects", false);
        set => Set("more_objects", value);
    }

    /// <summary>the panel's 2-line message window keeps the last 64
    /// lines that scrolled off its top and the shell can scroll back through them (mouse wheel over
    /// the window, Page Up / Page Down; rules/Text.cs history, rules/Play.cs drawing). A new message
    /// returns to live. Default off: with it off nothing is recorded and the game is byte-identical
    /// to before.</summary>
    public static bool MessageLog
    {
        get => (bool)Get("message_log", false);
        set => Set("message_log", value);
    }

    /// <summary>the three memory squeezes are lifted - a parked thing
    /// keeps its fuel's low byte, a full grid cell takes more than 8 records, and the kill counts do
    /// not wrap at a byte (rules/Play.cs RoomyMemory). The original's arrays and the save's first
    /// $C880 bytes stay the original's; the extras ride the save as an extension block after them.
    /// Always on; off only from a settings file for original-only runs - then the game,
    /// the rules and the save are byte-identical to the original.</summary>
    public static bool RoomyMemory
    {
        get => (bool)Get("roomy_memory", true);   // Always on, no menu row; false only in a settings file for original-only runs
        set => Set("roomy_memory", value);
    }

    /// <summary>the upgrade's maths in 32 bits (rules WorldState.Maths32, docs/maths32.md) -
    /// no 16-bit wraps or divide overflows. On the shipped maps in one-player nothing changes (the wraps never fire there);
    /// it matters for taller hills, two players and things alive farther out. Always on like roomy_memory, no menu row;
    /// false only in a settings file for original-only runs.</summary>
    /// <summary>the card turns
    /// each thing by its fine angles where the game has them (Entity.PitchFine: a turn's own angle word, a flier's climb, the
    /// smooth picture's in-between) instead of the bytes' 256 whole steps; and a grounded thing's pitch is worked out from the
    /// exact heading and kept fine (rules Objects.FineTilt, Play.FineTilt) - that one changes play a little (the pitch aims a
    /// thrown thing); the attract demo gets the picture's fine angles but not the tilt rule (it plays the recording exactly). Always on like maths32, no menu row; false only in a settings file for
    /// original-only runs.</summary>
    public static bool FineAngles
    {
        get => (bool)Get("fine_angles", true);
        set => Set("fine_angles", value);
    }

    public static bool Maths32
    {
        get => (bool)Get("maths32", true);
        set => Set("maths32", value);
    }

    /// <summary>straight walking (rules WorldState.StraightMoves) - the original's rounding
    /// walked east 1 south a frame and south 1 west (docs/original-bugs.md). On by default; no menu
    /// row, false only in a settings file for original-only runs. Never the attract demo (it plays the recording exactly).</summary>
    public static bool StraightMoves
    {
        get => (bool)Get("straight_moves", true);
        set => Set("straight_moves", value);
    }

    /// <summary>the spike fix - Missions' 12 spikes are spots meant to be under the sea
    /// whose height (just below 0) wrapped round in the map's one byte to the top (docs/original-bugs.md); fixed, they are
    /// at the sea's level. On by default, no menu row, false only in a settings file for original-only runs.</summary>
    public static bool FixSpikes
    {
        get => (bool)Get("fix_spikes", true);
        set => Set("fix_spikes", value);
    }

    /// <summary>walking into the HQ before the mission is done tells you off and puts
    /// you back outside, the game going on (rules WorldState.EarlyHqBack). On by default; false
    /// gives the original's game-ending visit.</summary>
    public static bool EarlyHqBack
    {
        get => (bool)Get("early_hq_back", true);
        set => Set("early_hq_back", value);
    }

    /// <summary>every parked boat you come to lies along the shore, ready to drive off
    /// (rules WorldState.BoatsAlongShore).; never in the attract demo (its recording boards boats as parked); false gives the maps' own headings, most bow-in to the shore.</summary>
    public static bool BoatsAlongShore
    {
        get => (bool)Get("boats_along_shore", true);
        set => Set("boats_along_shore", value);
    }

    /// <summary>the attract demo starts at 13:00 rather than the world file's 09:00, so it
    /// plays in afternoon light throughout (GameView.Init; the clock only feeds the light there, the route is the same).
    /// on by default, no menu row.</summary>
    public static bool DemoAfternoon
    {
        get => (bool)Get("demo_afternoon", true);
        set => Set("demo_afternoon", value);
    }

    /// <summary>FINER GROUND - the game's ground grown finer between the map's points
    /// (2 points a cell side - "2 for now" - Vista Pro's method from the world's seed, gentle, evening out over every
    /// square) and 1.5x taller (rules/FineGround.cs).
    /// New games only, never the attract demo; a save carries it and a load rebuilds it. Default 0 (off): the
    /// original's ground, bit for bit. Original / 2X.</summary>
    public static int FinerGround
    {
        get => (int)Get("finer_ground", 0);
        set => Set("finer_ground", value);
    }

    /// <summary>RELIEF - the FINER GROUND
    /// row's HILLS and CLIFFS: with finer ground on, how tall and rough the ground grows (FineGround.Reliefs: 0 = 2X,
    /// today's gentle store, 1 HILLS 2x taller, 2 CLIFFS 3x taller, each rougher). Read at a new game with finer ground;
    /// a save carries the numbers. Default 0.</summary>
    public static int GroundRelief
    {
        get => (int)Get("ground_relief", 0);
        set => Set("ground_relief", value);
    }

    /// <summary>GROUND SEED - where a new game's
    /// finer ground grows its detail from: "world" (default, the world's own seed: the same hills every game), "random"
    /// (a fresh seed each new game, remembered in LastGroundSeed and printed to the log) or "last" (the remembered one
    /// again - or any typed into ground_seed_last in the settings file). Only the points between the map's own move:
    /// the coast, the sea, every placed thing and the starts are the world's whatever the seed. Never the attract demo.</summary>
    public static string GroundSeed
    {
        get => (string)Get("ground_seed", "world");
        set => Set("ground_seed", value);
    }

    /// <summary>The seed GROUND SEED: RANDOM drew last (0..65535), -1 before any; LAST plays it again.</summary>
    public static int LastGroundSeed
    {
        get => (int)Get("ground_seed_last", -1);
        set => Set("ground_seed_last", value);
    }

    /// <summary>the fuller world - extra trees, flowers, stones,
    /// ducks, fish, gulls and animals worked out from the map itself and written into the grid
    /// at a new game (rules/Fill.cs; 1/2/3 = about 145/300/450 records). A loaded game's grid
    /// is the saved one and never fills again, so saves keep the records. Default 0 (off):
    /// with it off the game is byte-identical to before. Off / Some / More / Lots.</summary>
    public static int FullerWorld
    {
        get => (int)Get("fuller_world", 0);
        set => Set("fuller_world", value);
    }

    /// <summary>each paid death - never the last - leaves a grave
    /// holding what the soldier carried, out of play so no free resources, and the next soldier starts
    /// at home with the normal starting kit (rules/Play.cs Graves). The life bar, 4 a death and the
    /// last death are the original's; unlimited graves in one game; the graves ride the save's
    /// extension block. Default off: with it off the game, the rules and the save are byte-identical
    /// to before.</summary>
    public static bool Graves
    {
        get => (bool)Get("graves", false);
        set => Set("graves", value);
    }

    /// <summary>a new grave brings a pair of vultures that circles
    /// over it for a while - the nearest live ones drafted, a missing one spawned at the circle
    /// (rules/Play.cs VultureGraves). Needs graves on to have a grave at all. Default off: with
    /// it off nothing runs and the game is byte-identical to before. Not part of the save.</summary>
    public static bool VultureGraves
    {
        get => (bool)Get("vulture_graves", false);
        set => Set("vulture_graves", value);
    }


    /// <summary>each Paula channel is panned by where its owner is on
    /// screen (the original hard-wires channels 0,3 left and 1,2 right). Default off: with it off the
    /// buses, gains and the mix are byte-identical to before.</summary>
    public static bool SoundPosition
    {
        get => (bool)Get("sound_position", false);
        set => Set("sound_position", value);
    }

    /// <summary>an effect still sounding when its channel is taken
    /// again fades out over 80 ms on a spare player (the original cuts it dead). Default off: with
    /// it off a new start stops the old stream exactly as before.</summary>
    public static bool SoundTails
    {
        get => (bool)Get("sound_tails", false);
        set => Set("sound_tails", value);
    }

    /// <summary>(row SOUND VOICES): what the four channels leave out
    /// plays on spare voices (Channels.Spare, Audio.OnSpare) - the game's channels untouched. Default off.</summary>
    public static bool SoundVoices
    {
        get => (bool)Get("sound_voices", false);
        set => Set("sound_voices", value);
    }

    /// <summary>music keeps playing during a hunt at 12 dB below the
    /// menu level (the original stops it the moment play starts). Default off: with it off play
    /// starts with Music(-1), exactly as before.</summary>
    public static bool MusicInPlay
    {
        get => (bool)Get("music_in_play", false);
        set => Set("music_in_play", value);
    }


    /// <summary>A text value from any section (the key bindings), or null when never saved.</summary>
    public static string? GetString(string section, string key) =>
        File().HasSectionKey(section, key) ? (string)File().GetValue(section, key) : null;

    public static void SetString(string section, string key, string value)
    {
        File().SetValue(section, key, value);
        File().Save(Path);
        if (section == Section) read.Remove(key);
    }

    // ---- text size (F8) ----
    // The shell's own overlays (the F1 key list, the F2 rebind screen) only: each multiplies the
    // base size it already picks (about 1/32 of the window height, min 14 px) by this, and its
    // shrink-to-fit loop keeps the box inside the window and the game picture. The 320 x 200 game
    // picture and its 8 px fonts are never touched.

    /// <summary>The names F8 cycles through, each with its scale below (same index).</summary>
    public static readonly string[] TextSizes = ["normal", "large", "extra large"];
    public static readonly float[] TextScales = [1.0f, 1.35f, 1.7f];

    public static int TextSizeIndex
    {
        get
        {
            var v = (string)Get("text_size", "normal");
            var i = System.Array.IndexOf(TextSizes, v);
            return i < 0 ? 0 : i;
        }
        set => Set("text_size", TextSizes[System.Math.Clamp(value, 0, TextSizes.Length - 1)]);
    }

    /// <summary>The factor the overlays' base font size is multiplied by (1.0 = as before).</summary>
    public static float TextScale => TextScales[TextSizeIndex];

    // ---- players (the two-player screen split, mp_godot_head) ----

    /// <summary>(multiplayer piece 3c, layout only): how many player
    /// halves the window shows while a game runs - 1 (default: today's single screen, byte for
    /// byte - nothing of the split runs) or 2 (two 320 x 200 halves plus a thin divider; the
    /// second view itself is a later piece, mp_views: both halves show player 1's frame for
    /// now, through GameView.PlayerFrame). Nothing in rules\ ever sees this.</summary>
    public static int Players
    {
        get => (int)Get("players", 1) == 2 ? 2 : 1;
        set => Set("players", value == 2 ? 2 : 1);
    }

    /// <summary>With Players = 2: "side" (two halves next to each other, default) or "stacked"
    /// (one above the other). No effect with Players = 1.</summary>
    public static string SplitLayout
    {
        get => (string)Get("split_layout", "side") == "stacked" ? "stacked" : "side";
        set => Set("split_layout", value == "stacked" ? "stacked" : "side");
    }

    /// <summary>F8: normal -> large -> extra large -> normal.</summary>
    public static void CycleTextSize() => TextSizeIndex = (TextSizeIndex + 1) % TextSizes.Length;

    // ---- brightness (F7 / Shift+F7) ----

    /// <summary>The final picture's brightness, 0.8..1.4 in steps of 0.1, default 1.0 = no change:
    /// no extra pass at all, so the output is byte-identical. A picture-only multiply, never the
    /// game's palettes or its own night light. F7 brighter, Shift+F7 darker.</summary>
    public static float Brightness
    {
        get => System.Math.Clamp((float)(double)Get("brightness", 1.0), 0.8f, 1.4f);
        set => Set("brightness", System.Math.Round((double)System.Math.Clamp(value, 0.8f, 1.4f), 1));
    }

    // ---- the options that ride a save ----

    /// <summary>The PC's own rows: a loaded game leaves these as they are; every other key goes with a save.</summary>
    static readonly string[] PcKeys = ["brightness", "easy_saves", "fullscreen", "gpu_scale", "look", "manual_button", "pause_on_focus_loss",
        "players", "quit_button", "split_layout", "start_choice", "text_size",
        "renderer", "wide_view", "demo_camera", "demo_world"];   // 11-11: the rest of the setup page and the attract demo's world - a load switched the 3D world or the screen shape (both need a restart) to the save's

    /// <summary>The game's options as they are now, for the file beside a save: every key of the section that is not the PC's own.</summary>
    public static ConfigFile GameOptions()
    {
        var f = new ConfigFile();
        var keys = File().GetSectionKeys(Section);
        if (keys is null) return f;                                            // nothing set yet: an empty file, so a load changes nothing
        foreach (var k in keys)
            if (System.Array.IndexOf(PcKeys, k) < 0) f.SetValue(Section, k, File().GetValue(Section, k));
        return f;
    }

    /// <summary>The sound rows built at launch: a load that changes one cannot switch it live, so the caller says restart.</summary>
    static readonly string[] SoundKeys = ["music_in_play", "sound_position", "sound_tails"];

    /// <summary>A loaded save's options back on, each set as a menu click is (the value in, the cfg saved);
    /// a missing file changes nothing. True when a sound row changed: those are built at launch, so the
    /// caller says the restart message.</summary>
    public static bool RestoreGameOptions(string path)
    {
        var f = new ConfigFile();
        if (!System.IO.File.Exists(path) || f.Load(path) != Error.Ok) return false;
        var keys = f.GetSectionKeys(Section);
        if (keys is null) return false;
        var restart = false;
        foreach (var k in File().GetSectionKeys(Section) ?? [])       // a game key the save never set: back to its default (erased)
            if (System.Array.IndexOf(PcKeys, k) < 0 && System.Array.IndexOf(keys, k) < 0)
            {
                restart |= System.Array.IndexOf(SoundKeys, k) >= 0;
                File().EraseSectionKey(Section, k);
                read.Remove(k);
            }
        File().Save(Path);
        foreach (var k in keys)
        {
            if (System.Array.IndexOf(PcKeys, k) >= 0) continue;                // the PC's own rows stay as they are
            var v = f.GetValue(Section, k);
            var was = Get(k, k == "a500_filter");                              // today's value before Set writes over it
            Set(k, v);
            if (System.Array.IndexOf(SoundKeys, k) >= 0 && !was.Equals(v)) restart = true;
            if (k == "a500_filter" && !was.Equals(v)) Audio.ApplyFilter((bool)v);    // the filter itself, at once, as SettingsApply does
        }
        return restart;
    }
}
