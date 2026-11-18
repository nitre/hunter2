using System.Text.Json;

namespace Hunter.Rules;

/// <summary>
/// Draws the manual's figures from the game's own graphics (the table in the picture job's
/// in\figures.md): the panel icons straight from storepic (32 x 16 each, icon n at
/// (n%10*32, n/10*16), the layout Panel.IconPixel reads, $851C) at 1x; the menu, soldier bar,
/// briefing and map screens by the port's OWN drawing code into a 320 x 200 screen, scaled
/// down to the figure's box (nearest pixel); the cover and the back-cover logo as the
/// median-cut scans (game_data\manual\cover.json / logo.json). Every pixel keeps its real
/// bank's colours: the menus' bank 5, the map's bank 2 and the icons' bank 4 go in at
/// indices 16-31, the bank-6 soldier bar and briefing draw in 0-15 - the pages are the
/// Extra Half-Brite upgrade, never a remap into bank 6.
/// </summary>
public sealed class ManualFigures : IFigureSource
{
    const int ScreenW = 320, ScreenH = 200;
    // figure id -> the game icon (storepic row-major number); figures.md, all SURE matches
    static readonly Dictionary<string, int> Icons = new()
    {
        ["p12-1"] = 10, ["p12-2"] = 11, ["p14-1"] = 15, ["p14-2"] = 22,
        ["p15-1"] = 15, ["p15-2"] = 10, ["p15-3"] = 11, ["p15-4"] = 12,
        ["p16-1"] = 13, ["p16-2"] = 14, ["p16-3"] = 16, ["p16-4"] = 17,
        ["p18-1"] = 18, ["p18-2"] = 0, ["p18-3"] = 1, ["p18-4"] = 4, ["p18-5"] = 3,
        ["p19-1"] = 5,
    };

    public static bool IsIcon(string id) => Icons.ContainsKey(id);

    /// <summary>The figure ids that are whole game screens: the menus, the soldier bar, the
    /// briefing and the map. Each gets a page of its own and draws as large as the frame's
    /// opening fits (the menus as the 1x crop round their buttons).</summary>
    public static bool IsScreen(string id) => id is "p07-1" or "p08-1" or "p09-1" or "p07-2" or "p11-1" or "p17-1";

    /// <summary>The icon's drawn box: the 32 x 16 storepic cell at 2x inside a 1 px panel-button
    /// bevel (bright 12 top/left, dark 15 bottom/right) - 66 x 34.</summary>
    public static readonly int IconW = 66, IconH = 34;

    static readonly Dictionary<string, (int W, int H)> MenuSizes = new();

    /// <summary>The box a screen figure's art really fills: the menus' button block, cut round
    /// the buttons the game itself draws (so the crop shows them at 1x), or the whole 320 x 200
    /// screen. ManualBook.Layout scales from this box.</summary>
    public static (int W, int H) FigureSize(Ui ui, string dataDir, string id)
    {
        if (id is "p07-1" or "p08-1" or "p09-1")
        {
            if (!MenuSizes.TryGetValue(id, out var size))
            {
                var (x, y, w, h) = ButtonBlock(DrawMenu(id, ui, dataDir));
                size = (w, h);
                MenuSizes[id] = size;
            }
            return size;
        }
        return (ScreenW, ScreenH);
    }

    static List<Menus.Button> DrawMenu(string id, Ui ui, string dataDir)
    {
        var menus = new Menus(ui, dataDir);
        var sc = new IndexedScreen();
        return id switch
        {
            "p07-1" => menus.MainMenu(sc),
            "p08-1" => menus.GameMenu(sc, 0),
            _ => menus.DiskMenu(sc),
        };
    }

    /// <summary>The buttons' bounding block, 4 px of margin round it, clamped to the screen.</summary>
    static (int X, int Y, int W, int H) ButtonBlock(List<Menus.Button> buttons)
    {
        var x0 = Math.Max(0, buttons.Min(b => b.X0) - 4);
        var y0 = Math.Max(0, buttons.Min(b => b.Y0) - 4);
        var x1 = Math.Min(ScreenW, buttons.Max(b => b.X1) + 4);
        var y1 = Math.Min(ScreenH, buttons.Max(b => b.Y1) + 4);
        return (x0, y0, x1 - x0, y1 - y0);
    }

    readonly GameData _d;
    readonly Ui _ui;
    readonly byte[] _store, _main;
    readonly Dictionary<string, (ushort[] Pal, byte[] Px, int W, int H)> _art = new();

    public ManualFigures(GameData d)
    {
        _d = d;
        _main = d.Main;
        _ui = Ui.Load(d.Directory);
        _store = d.Picture("storepic").Pixels;
    }

    /// <summary>Draws the figure into the box (x, y, w, h); everything lands inside it plus
    /// its 1 px drop shadow (the EHB copy of the pixel, index + 32).</summary>
    public void Draw(string id, IndexedScreen s, int x, int y, int w, int h)
    {
        if (Icons.TryGetValue(id, out var icon)) { Icon(s, icon, x, y, w, h); return; }
        if (id == "p01-1") { Art("cover", s, x, y, w, h); return; }
        if (id == "p29-1") { Art("logo", s, x, y, w, h); return; }
        var sc = new IndexedScreen();
        List<Menus.Button>? buttons = null;
        var menus = new Menus(_ui, _d.Directory);
        switch (id)
        {
            case "p07-1": buttons = menus.MainMenu(sc); break;                // MAIN OPTIONS ($2032)
            case "p08-1": buttons = menus.GameMenu(sc, 0); break;             // AVAILABLE GAMES, none completed ($2310)
            case "p09-1": buttons = menus.DiskMenu(sc); break;                // DISK OPTIONS ($100B8)
            case "p07-2": Pregame.SoldierScreen(sc, _ui, _main, Soldier()); break;   // View soldier ($875A)
            case "p11-1":                                                     // the briefing ($874C -> $8818/$9488)
                sc.Clear();
                Pregame.Briefing(sc, _ui, _main, Soldier(), "FRANCK"u8.ToArray(),
                    (int)GameMode.Hunter, _d.Worlds[GameMode.Hunter].Header);
                break;
            case "p17-1": Map(sc); break;                                     // MapScreen ($3CFA -> $3D7C)
            default: return;                                                  // nothing draws (should not happen)
        }
        if (buttons != null)
        {
            var (cx, cy, cw, ch) = ButtonBlock(buttons);                      // the buttons at 1x, as the game draws them
            Blit(s, sc, cx, cy, cw, ch, x, y, w, h, 16);
        }
        else
            Blit(s, sc, 0, 0, sc.Width, sc.Height, x, y, w, h, id == "p17-1" ? 16 : 0);
    }

    // The soldier the scans show: Private FRANCK, ready for action, face 0, no medals.
    Soldier Soldier()
    {
        var m = (byte[])_main.Clone();
        var o = 0x85E2 - 0x800;                                               // the soldier record
        m[o + 4] = 1;                                                         // status: ready for action
        m[o + 5] = 0;                                                         // face 0
        m[o + 8] = m[o + 9] = 0;                                              // title word 0 = Private (frame 3)
        var name = "FRANCK"u8.ToArray();
        for (var i = 0; i < name.Length; i++) m[o + 0x10 + i] = name[i];
        m[o + 0x10 + name.Length] = (byte)'*';
        return new Soldier(m);
    }

    // The map the way play shows it ($3CFA MapScreen, Screens.OpenMap -> DrawMapScreen $3D7C), in a fresh view.
    void Map(IndexedScreen sc)
    {
        var play = new Play(_d, GameMode.Hunter);
        var v = new View();
        play.Screens.OpenMap(v);
        Array.Copy(v.Pixels, sc.Pixels, Math.Min(v.Pixels.Length, sc.Pixels.Length));
    }

    // One panel icon as the play panel shows it: the whole storepic cell (the icon figure AND
    // its own camouflage ground) at 2x, nearest pixel, inside a 1 px panel-button bevel -
    // bright 12 top/left, dark 15 bottom/right (the panel's edge indices). The cell's pixels
    // stay at their raw 0-15 indices: the page's own palette IS the panel CMAP.
    void Icon(IndexedScreen s, int n, int x, int y, int w, int h)
    {
        var fx = x + (w - IconW) / 2;
        var fy = y + (h - IconH) / 2;
        for (var i = 0; i < IconW; i++) { Put(s, fx + i, fy, 12); Put(s, fx + i, fy + IconH - 1, 15); }
        for (var j = 0; j < IconH; j++) { Put(s, fx, fy + j, 12); Put(s, fx + IconW - 1, fy + j, 15); }
        int ix = n % 10 * 32, iy = n / 10 * 16;
        for (var j = 0; j < 16; j++)
            for (var i = 0; i < 32; i++)
            {
                var v = _store[(iy + j) * 320 + ix + i];
                Put(s, fx + 1 + i * 2, fy + 1 + j * 2, v);                    // 2x: every cell pixel twice
                Put(s, fx + 2 + i * 2, fy + 1 + j * 2, v);                    // in each direction
                Put(s, fx + 1 + i * 2, fy + 2 + j * 2, v);
                Put(s, fx + 2 + i * 2, fy + 2 + j * 2, v);
            }
    }

    // A print-art cut, 1:1 centred (it was generated at its box's size), its own 16 colours.
    void Art(string name, IndexedScreen s, int x, int y, int w, int h)
    {
        if (!_art.TryGetValue(name, out var a))
        {
            using var f = JsonDocument.Parse(File.ReadAllText(Path.Combine(_d.Directory, "manual", name + ".json")));
            var e = f.RootElement;
            a = (e.GetProperty("palette12").EnumerateArray().Select(c => (ushort)c.GetInt32()).ToArray(),
                 Convert.FromHexString(e.GetProperty("pixels_hex").GetString()!),
                 e.GetProperty("w").GetInt32(), e.GetProperty("h").GetInt32());
            _art[name] = a;
        }
        var ox = x + (w - a.W) / 2;
        var oy = y + (h - a.H) / 2;
        for (var j = 0; j < a.H; j++)
            for (var i = 0; i < a.W; i++)
                Put(s, ox + i, oy + j, a.Px[j * a.W + i] + 16);
    }

    // The source box (sx, sy, sw, sh) of the drawn screen scaled into the box, nearest pixel;
    // +16 moves a bank's colours up into 16-31, 0 keeps a bank-6 screen in the page's own 0-15.
    static void Blit(IndexedScreen dst, IndexedScreen src, int sx, int sy, int sw, int sh,
        int x, int y, int w, int h, int add)
    {
        for (var j = 0; j < h; j++)
            for (var i = 0; i < w; i++)
                Put(dst, x + i, y + j, src.Pixels[(sy + j * sh / h) * src.Width + sx + i * sw / w] + add);
    }

    /// <summary>One figure pixel plus its drop shadow 1 px right and down (the EHB copy).</summary>
    static void Put(IndexedScreen s, int x, int y, int idx)
    {
        if ((uint)x < s.Width && (uint)y < s.Height)
            s.Pixels[y * s.Width + x] = (byte)idx;
        ManualBook.Shadow(s, x + 1, y + 1, idx + 32);
    }
}
