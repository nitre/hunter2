using System.Collections.Generic;
using System.Linq;
using Godot;
using Hunter.Rules;

namespace Hunter;

/// <summary>The palette as the game holds it, for the 3D shaders to fade themselves (world_colour.gdshaderinc): bank 2's
/// 16 $0RGB words, the sky's 7, the screen's fade level, the sky's, the death-fade step, the hour's light and SMOOTH
/// daylight's level in 60ths (Play.SmoothDrawLevel, -1 = the original's levels).</summary>
public readonly record struct RawPalette(int[] View, int[] Sky, int Fade, int SkyLevel, int Death, int Day = 15, int Smooth = -1, int Panel = 15);

/// <summary>NEW, NOT THE ORIGINAL (the showcase, recording only): a camera free of the game's - the eye in world units
/// (x, height, z north, as View3D.Eye), the look turn (0 = north, positive left), the tilt down (radians) and the
/// zoom (1 = the game's 256-pixel focal length). The view only: the rules never see it.</summary>
public readonly record struct FreeCamera((double X, double Y, double Z) Eye, float Yaw, float Pitch, float Zoom = 1f);

/// <summary>(setting renderer = 3d): the view as a real 3D scene on the
/// graphics card - the card-built world (GpuWorld: the ground and the things from the game's own data), the sea as a
/// plane to the horizon, the sky as a backdrop, a real camera, the GPU's depth buffer. The game's rules are untouched:
/// the card's shaders follow the game's own camera each frame (view.gdshaderinc).
/// The original's framing (Ground.Store / Objects.Persp): screen x = $A0 + 256 * dx / dz, screen y =
/// $2E + 128 * dy / dz (dy in height units, x and z in world units; the depth's camera point sits $100 doubled =
/// 128 world units behind $1544). So the scene's heights are halved (the 128) and the camera's focal length is
/// 256 view pixels with the horizon on view row $2E of 140. World z runs north; Godot's z is its negative, so the
/// camera's default -z look is north and screen-right is +x, as in the game.</summary>
public sealed partial class Real3DView : Node
{
    public SubViewport Viewport { get; }
    public ShaderMaterial Sky { get; }
    public ShaderMaterial Sea { get; }
    public ShaderMaterial LowClouds { get; }                            // CLOUDS: the low layer's cumulus
    readonly MeshInstance3D lowClouds;
    internal const int LowCloudCount = 64;                               // clouds.gdshaderinc LOW_CLOUD_N
    public ShaderMaterial SolidClouds { get; }                          // CLOUDS: the same clouds as solid clusters of puffs (solidclouds.gdshader)
    readonly MeshInstance3D solidClouds;
    const int SolidCloudCorners = 5 * 8 * 4 * 2 * 3;                     // solidclouds.gdshader CLOUD_CORNERS: PUFFS x MAX_SEGS x MAX_BANDS, two triangles a facet
    /// <summary>CLOUDS' low layer: solid puffs (the flat cards stand down), or the flat cards. Development: HUNTER_CLOUDS
    /// = flat, off (neither), or SEGSxBANDS for the puffs' facets (8x4 the shader's own).</summary>
    static readonly string cloudsAs = System.Environment.GetEnvironmentVariable("HUNTER_CLOUDS") ?? "";
    ImageTexture shoreTex;                                       // SEA: LIVING's shallows (ShoreDistance)
    readonly Camera3D camera;
    /// <summary>NEW, NOT THE ORIGINAL: the 3D camera's look turn round the player, radians
    /// (0 = north, the original's only view). The view only: the game's own camera and rules never see it.</summary>
    public float Yaw;
    /// <summary>DEMO CAMERA's car close-up: the eye at this share of the game's distance from him (1 = the game's).</summary>
    public float CloseUp = 1;
    /// <summary>The showcase's camera instead of the game's (null = the game's).</summary>
    public FreeCamera? Free;
    int scale;                                                          // viewport pixels a game row
    float scaleX;                                                       // ... and a game column: wider in the TV looks (16/15), = scale with square pixels

    /// <summary>The view's width in viewport pixels at sx a game column (the screen's own pixels: no stretch after).</summary>
    public static int Across(float sx) => (int)System.Math.Round(Rules.View.Width * sx);

    /// <summary>GPU SCALE changed (GameView.FollowScale): the view drawn at the new size; gpu_px follows next Frame.</summary>
    public void Rescale(float sx, int s)
    {
        (scaleX, scale) = (sx, s);
        Viewport.Size = new Vector2I(Across(sx), Rules.View.Height * s);
        GD.Print($"3D VIEW: {Viewport.Size.X} x {Viewport.Size.Y}, {sx:F3} x {s} viewport pixels a game pixel");
    }
    const float Near = 16f, Far = 100000f;                             // just past the map's diagonal (~92700): depth precision kept fine

    /// <summary>The card-built world (godot/real3d/GpuWorld.cs): the ground and the things; its materials get the
    /// palette each frame with the sky's and the sea's.</summary>
    public GpuWorld Gpu { get; }
    ShaderMaterial[]? viewed, painted;   // made once: the camera's materials (the sea + the world's) and the palette's (+ the sky)

    public Real3DView(byte[] map, float scaleX, int scale, GpuWorld gpu)
    {
        Viewport = new SubViewport
        {
            Size = new Vector2I(Across(scaleX), Rules.View.Height * scale),
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(Viewport);
        var shader = GD.Load<Shader>("res://real3d/world.gdshader");
        Sky = new ShaderMaterial { Shader = shader };
        Sky.SetShaderParameter("force_idx", 15);
        Sea = new ShaderMaterial { Shader = shader };
        Sea.SetShaderParameter("force_idx", 16);                         // the sea: one colour out to the real horizon
        this.scale = scale;
        this.scaleX = scaleX;
        GD.Print($"3D VIEW: {Viewport.Size.X} x {Viewport.Size.Y}, {scaleX:F3} x {scale} viewport pixels a game pixel");

        var world = new Node3D { Scale = new Vector3(1, 0.5f, 1) };    // heights halved: the original's 128 vs 256
        Viewport.AddChild(world);
        const float span = 65536f;                                       // the map, and far beyond
        world.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(span * 8, span * 8) },
            Position = new Vector3(span / 2, -1f, -span / 2),            // just under the sea-level coast cells
            MaterialOverride = Sea,
        });
        shoreTex = ShoreTexture(map);                                     // SEA: LIVING's shallows - set on every material each frame: the card's coast water uses it too
        Sea.SetShaderParameter("sea_off", new Vector3(span / 2, -1f, -span / 2));   // the sea's own place: the card-drawn path moves the plane to the eye itself (world.gdshader)
        LowClouds = new ShaderMaterial { Shader = GD.Load<Shader>("res://real3d/lowclouds.gdshader") };   // CLOUDS: the low layer's cumulus, one card each
        lowClouds = new MeshInstance3D { Mesh = GpuWorld.Empty(LowCloudCount * 6), MaterialOverride = LowClouds, CustomAabb = GpuWorld.Huge, Visible = false };
        world.AddChild(lowClouds);
        SolidClouds = new ShaderMaterial { Shader = GD.Load<Shader>("res://real3d/solidclouds.gdshader") };
        solidClouds = new MeshInstance3D { Mesh = GpuWorld.Empty(LowCloudCount * SolidCloudCorners), MaterialOverride = SolidClouds, CustomAabb = GpuWorld.Huge, Visible = false };
        world.AddChild(solidClouds);
        if (cloudsAs.Split('x') is [var segs, var bands] && int.TryParse(segs, out var ns) && int.TryParse(bands, out var nb))
        {
            SolidClouds.SetShaderParameter("puff_segs", ns);
            SolidClouds.SetShaderParameter("puff_bands", nb);
        }
        world.AddChild(Gpu = gpu);

        camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Frustum,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Size = Lens.Size,
            FrustumOffset = Lens.Offset,
            Near = Near,
            Far = Far,
            Current = true,
        };
        Viewport.AddChild(camera);
        camera.AddChild(new MeshInstance3D                               // the sky: a backdrop just inside the far plane
        {
            Mesh = new QuadMesh { Size = new Vector2(Far * 4, Far * 4) },
            Position = new Vector3(0, 0, -Far * 0.95f),
            MaterialOverride = Sky,
        });
    }

    /// <summary>The game's lens: 140 rows at 256 pixels' focal length, the horizon on row $2E.</summary>
    static readonly (float Size, Vector2 Offset) Lens = (Near * Rules.View.Height / 256f, new Vector2(0, -Near * (Rules.View.Height / 2f - 0x2E) / 256f));

    /// <summary>The free camera's uniforms (view.gdshaderinc cam_free...) on one card-built material.</summary>
    public static void SetFree(ShaderMaterial m, FreeCamera? free)
    {
        m.SetShaderParameter("cam_free", free is not null);
        if (free is not { } f) return;
        m.SetShaderParameter("cam_free_eye", new Vector3((float)f.Eye.X, (float)f.Eye.Y, (float)f.Eye.Z));
        m.SetShaderParameter("cam_free_yaw", f.Yaw);
        m.SetShaderParameter("cam_free_pitch", f.Pitch);
    }

    /// <summary>ShoreDistance per cell as a texture, 0..1 of ShoreDistance.Far: from land (the sea's shallows), or fromSea
    /// (GROUND COLOURS: BANDS' shore).</summary>
    public void SetShore(byte[] map) => shoreTex = ShoreTexture(map);

    public static ImageTexture ShoreTexture(byte[] map, bool fromSea = false)
    {
        var img = Image.CreateEmpty(GroundMesh.Points, GroundMesh.Points, false, Image.Format.Rf);
        var dist = ShoreDistance.Build(map, fromSea);
        for (var i = 0; i < dist.Length; i++)
            img.SetPixel(i % GroundMesh.Points, i / GroundMesh.Points, new Color(dist[i] / ShoreDistance.Far, 0, 0));
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>GROUND COLOURS: BANDS' shore from the coast as drawn (ShoreDistance.BuildFromCoast), FinePerCell samples
    /// a cell each way, the same 0..1 of Far.</summary>
    public static ImageTexture CoastTexture(byte[] map)
    {
        const int n = GroundMesh.Points * ShoreDistance.FinePerCell;
        var dist = ShoreDistance.BuildFromCoast(map);
        var bytes = new byte[n * n * 4];
        for (var i = 0; i < dist.Length; i++) System.BitConverter.TryWriteBytes(new System.Span<byte>(bytes, i * 4, 4), dist[i] / ShoreDistance.Far);
        return ImageTexture.CreateFromImage(Image.CreateFromData(n, n, false, Image.Format.Rf, bytes));
    }

    /// <summary>CLOUDS' night (stars.gdshaderinc): how far the moon is through the night, 0 rising in the east at 20:00
    /// .. 1 setting in the west at 06:00, -1 when it is not up; and its phase, 0 new .. 0.5 full, an eighth a night on the
    /// day counter (a night belongs to the day it began on: before noon it is the day before's), full on the first night.</summary>
    public static (float T, float Phase) Moon(int hour, int minute, int day)
    {
        var since = ((hour * 60 + minute - 20 * 60) % 1440 + 1440) % 1440;   // minutes since 20:00
        var t = since <= 600 ? since / 600f : -1f;
        var night = hour >= 12 ? day : day - 1;
        return (t, (((night + 4) % 8 + 8) % 8) / 8f);
    }

    /// <summary>SKY's time of day (world_colour.gdshaderinc tod_band): the sun's height in degrees and its direction in the
    /// look turn's sense (0 north, + left), from the rules' one sun curve (Objects.SunPath).</summary>
    public static (float Elev, float Az) SunPath(int hour, int minute)
    {
        var (elev, az) = Objects.SunPath(hour, minute);                    // az: 0 east, pi west
        return ((float)elev, (float)(-System.Math.PI / 2 - az));
    }

    /// <summary>Each frame: the game's camera ($1548 = 2x, $1544 = 2z - distance, $0E = the eye height) and
    /// the palettes the 2D views use; skySteps (SKY: SMOOTH) = the stepped full-colour bands, false = the flat bands.</summary>
    public void Frame(Rules.Camera c, int distance, int lift, int cameraRow, bool interior, int interiorHorizon, RawPalette pal, bool skySteps = false, int haze = 0, float hazeFar = 3200f, bool livingSea = false, float gameSeconds = 0f, bool groundPattern = false, bool clouds = false, bool seaCycling = false, bool skyTime = false)
    {
        Gpu.SetGroundPattern(groundPattern);               // GROUND PATTERN: SPECKS
        Gpu.SetGroundColours(Settings.GroundColours);   // GROUND COLOURS: BANDS
        Sky.SetShaderParameter("cloud_on", clouds && !interior);     // CLOUDS: none indoors
        Sky.SetShaderParameter("cloud_yaw", Free?.Yaw ?? Yaw);
        Sky.SetShaderParameter("cloud_clock", gameSeconds);          // the game's clock: frozen while paused
        var (moonT, moonPhase) = Moon(Gpu.Clock.Hour, Gpu.Clock.Minute, Gpu.Clock.Day);
        Sky.SetShaderParameter("moon_t", moonT);                     // CLOUDS' night: the moon (stars.gdshaderinc)
        Sky.SetShaderParameter("moon_phase", moonPhase);
        bool flat = cloudsAs == "flat";
        lowClouds.Visible = clouds && !interior && flat;
        solidClouds.Visible = clouds && !interior && !flat && cloudsAs != "off";
        foreach (var m in (ShaderMaterial[])[LowClouds, SolidClouds])
        {
            m.SetShaderParameter("cloud_clock", gameSeconds);
            m.SetShaderParameter("sun_up", Gpu.SunToward != null);          // lit from the sun's side (clouds.gdshaderinc low_cloud_level, solidclouds.gdshader)
            if (Gpu.SunToward is { } sunT) m.SetShaderParameter("sun_dir", new Vector3(sunT.X, sunT.Y, sunT.Z));
        }
        var zoom = Free?.Zoom ?? 1f;
        camera.Size = Lens.Size / zoom;
        camera.FrustumOffset = Lens.Offset / zoom;
        camera.Position = Vector3.Zero;                                  // the camera is the lens only: the card-built shaders (and the sea) move
        camera.Rotation = Vector3.Zero;                                  // the world to the eye themselves (view.gdshaderinc)
        viewed ??= [Sea, LowClouds, SolidClouds, .. Gpu.Materials];
        painted ??= [Sky, Sea, LowClouds, SolidClouds, .. Gpu.Materials];
        var vp = new Vector2(Viewport.Size.X, Viewport.Size.Y);
        foreach (var m in viewed)
        {
            m.SetShaderParameter("vp_size", vp);
            m.SetShaderParameter("cam_on", true);
            m.SetShaderParameter("cam_ent", Gpu.EntTex);
            m.SetShaderParameter("cam_map", Gpu.MapTex);
            m.SetShaderParameter("cam_distance", distance);
            m.SetShaderParameter("cam_lift", lift);
            m.SetShaderParameter("cam_yaw", Yaw);
            m.SetShaderParameter("cam_near", CloseUp);
            m.SetShaderParameter("cam_row", cameraRow);                   // inside a building the camera stands on the building (InteriorView $48BE)
            m.SetShaderParameter("cam_interior", interior);
            m.SetShaderParameter("cam_interior_h", interiorHorizon);
            SetFree(m, Free);
        }
        // the bands stay on the original's rows: its sea starts below the eye line, where a round sea's edge would
        // be from the camera's height, and the flat sea plane shows sky above that edge (world.gdshader)
        Variant palRaw = pal.View, skyRaw = pal.Sky;                    // turned into the card's arrays once a frame, not once a material
        var (sunElev, sunAz) = SunPath(Gpu.Clock.Hour, Gpu.Clock.Minute);     // SKY's time of day: none indoors
        bool todOn = skyTime && !interior;
        float todYaw = Free?.Yaw ?? Yaw;
        foreach (var m in painted)
        {
            m.SetShaderParameter("pal_raw", palRaw);                 // the raw palette words and the fade levels: the shader fades them itself
            m.SetShaderParameter("sky_raw", skyRaw);
            m.SetShaderParameter("fade_level", pal.Fade);
            m.SetShaderParameter("sky_level", pal.SkyLevel);
            m.SetShaderParameter("death_step", pal.Death);
            m.SetShaderParameter("day_level", pal.Day);              // the hour's light: fire is not darkened by it
            m.SetShaderParameter("smooth_level", pal.Smooth);        // SMOOTH daylight: the view and the sky at it, as Play.Screen
            m.SetShaderParameter("height0e", (int)c.Height0E);       // the shader works the band rows out from it (sky.gdshaderinc)
            m.SetShaderParameter("sky_steps", skySteps);
            m.SetShaderParameter("tod_on", todOn);                   // SKY's time of day (world_colour.gdshaderinc tod_band)
            m.SetShaderParameter("tod_elev", sunElev);
            m.SetShaderParameter("tod_sun_az", sunAz);
            m.SetShaderParameter("tod_yaw", todYaw);
            m.SetShaderParameter("gpu_px", (float)scale);
            m.SetShaderParameter("gpu_px_x", scaleX);                // a game column: wider than a row in the TV looks
            m.SetShaderParameter("gpu_wide", scaleX / scale);        // the projection's sideways squeeze (world_colour.gdshaderinc wide_projection)
            m.SetShaderParameter("haze_mode", haze);                 // HAZE: off / bands / fine
            m.SetShaderParameter("haze_far", hazeFar);               // the chosen distance's far edge (ViewDistance.FarEdge)
            m.SetShaderParameter("sea_living", livingSea);           // SEA: original / living (the sea plane and the card's coast water)
            m.SetShaderParameter("sea_cycle", livingSea && seaCycling);   // SEA: CYCLING
            m.SetShaderParameter("shore_tex", shoreTex);
            m.SetShaderParameter("shore_far", (float)ShoreDistance.Far);
            m.SetShaderParameter("sea_clock", gameSeconds);          // the living sea's waves: the game's clock, frozen while paused
            m.SetShaderParameter("maths32", Settings.Maths32);       // MATHS32: ground_height's lerp in 32 bits - the same setting Play.Maths32 comes from
            m.SetShaderParameter("fine_n", Gpu.FineN);               // FINER GROUND: the game's store (0 = off), for every ground_height reader
            if (Gpu.FineTex is not null) m.SetShaderParameter("fine_map", Gpu.FineTex);   // (view.gdshaderinc's camera ground too)
        }
    }
}
