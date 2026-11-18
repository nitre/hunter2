using System.Text.Json;

namespace Hunter.Rules;

/// <summary>Hook for the picture job: draws one manual figure in the game's own colours.</summary>
public interface IFigureSource
{
    void Draw(string id, IndexedScreen screen, int x, int y, int w, int h);
}

/// <summary>
/// The printed manual inside the game, in the game's native look: its own proportional font
/// (Ui.Glyphs / Ui.Widths, 8 px high) and the briefing's window rules ($9490: lines of 9 px),
/// every page inside the frame look the game's own screens use (the menu buttons' bevel of
/// frames $17+ and the soldier panel's frame $16, in palette bank 6: bright cream $0ED3
/// outside, brown $0A62 inside). The pages are the Amiga's Extra Half-Brite upgrade (6
/// bitplanes, 64 colours): 0-15 = bank 4, the play panel's own CMAP (the frame band, and the
/// text of a page that carries a picture), 16-31 = the text's bank 6 (the briefing's colours)
/// on a text page, the page picture's own bank on a picture page, 32-63 = the half-brightness
/// copy for the drop shadows. The faithful game is not touched: this reads
/// game_data/manual/manual.json (built by tools/manual/build.py from docs/manual) and draws
/// onto a screen it is given.
/// </summary>
public sealed class ManualBook
{
    // Screen page of 320 x 200 through the 64-colour EHB palette (Palette(page)). The frame
    // is a band 8 px wide round the page (see the constants below), tiled with the panel's
    // camouflage; the text window sits inside its opening with a character cell (8 px) of
    // margin on every side: 35 columns, title bar on top, 14 lines of 9 px, the page line
    // under the body - all inside the frame.
    public const int ScreenW = 320, ScreenH = 200;
    public const int Cols = 35, LineH = 9, GlyphH = 8, HalfGap = 4;
    public const int WinX = 20, WinW = Cols * 8;                        // window x 20-299
    public const int TitleY = 20, BodyTop = 38, BodyLines = 14, FooterY = 173;
    public const int BodyBottom = BodyTop + BodyLines * LineH;          // 164: the last line starts <= 155
    public const int FigTop = TitleY + GlyphH + 2;                      // 30: a screen on a page of its own starts here
    // The frame: a band 8 px wide round the page (the outer edge at 2,2 - 317,197), tiled with
    // the play panel's own camouflage - the mottled grey behind the STORES banner's letters:
    // the 48 x 16 patch of pure texture at panfile (257,60), clear of the word STORES, the item
    // buttons and every edge line (its pixels are only the mottle's dark greys 9-15) - with the
    // panel's edge lines on the band's outer and inner border: bright 12 ($0566, panel row 0)
    // outside, dark 14 ($0233, panel row 1) inside; on the bottom edge the bevel flips (panel
    // row 59 = 14 below, the banner box's bright inner row over its dark 15s). The band's shadow
    // falls 1 px right and down INSIDE the opening: the EHB copy (index + 32) of 15 ($0122, the
    // panel's darkest edge tone, banner rows 79-80).
    public const int FrameX0 = 2, FrameY0 = 2, FrameX1 = 317, FrameY1 = 197;
    public const int Band = 8;
    public const int CamX = 257, CamY = 60, CamW = 48, CamH = 16;       // the seam-free camo patch in panfile
    public const int EdgeBright = 12, EdgeDark = 14, ColShadow = 15;    // the panel's bevel indices in the CMAP
    // The text reads the briefing's colours (bank 6, what the game's briefing text shows): body
    // 17 = 16 + bank 6's 1, $0CCE pale - the briefing draws its glyphs in plane 0, so index 1 is
    // its own text tone; bold 18 = its 2 $0ECA warm pale; heading2 22 = its 6 $0D00 red. The
    // title and heading1 bars are bank 6's pale bar style: bar 17 $0CCE, and the inverse draw
    // leaves the bar's pixels without plane 0 - black (16) text on it. On a picture page 16-31
    // is the picture's own bank, so the
    // text falls back to the panel's nearest tones in 0-15 (bank 4 = panfile's CMAP): body 7 =
    // $0ED2 cream (the panel's own text tone), bold 3 = $0DEE its brightest neutral (also the
    // picture-page bar), heading2 5 = $0D20 red, figure frames 14 = $0233 (the panel's dark edge).
    public const int ColBody = 17, ColBold = 18, ColHeading2 = 22;      // bank 6 = 16 + its index
    public const int PicBody = 7, PicBold = 3, PicHeading2 = 5, ColFrame = 14;

    public sealed class Span { public string Text = ""; public int X; public K Style; public bool Inverse; }
    public sealed class Line { public List<Span> Spans = new(); public int Y; public bool Underline; public int Width; }
    // W/H = the manual.json box (what the print had), SW/SH = the box scaled into the window
    // (never wider than it, never taller than the body); the figure draws into X/Y/SW/SH.
    public sealed class Figure { public string Id = ""; public string Desc = ""; public int X, Y, W, H, SW, SH; }
    public sealed class Page { public string Title = ""; public List<Line> Lines = new(); public List<Figure> Figures = new(); }

    /// <summary>The laid-out screen pages, in order. Page 1 is the cover as a title page.</summary>
    public IReadOnlyList<Page> Pages => _pages;
    readonly List<Page> _pages = new();
    readonly Ui _ui;
    readonly bool[] _blank;                                          // glyphs with no pixels
    IFigureSource? _figures;

    // palettes.json's banks (7 x 32 $0RGB) and the two print-art cuts' own 16 colours
    readonly ushort[] _bank6;
    readonly ushort[][] _banks;
    readonly ushort[]? _cover, _logo;
    // panfile's own CMAP and its 8 x 4 camouflage patch: the frame's art
    readonly ushort[] _cmap;
    readonly byte[] _camo;
    readonly string _dir;

    ManualBook(Ui ui, string dir)
    {
        _ui = ui;
        _dir = dir;
        _blank = new bool[ui.Glyphs.Length];
        for (var c = 0; c < ui.Glyphs.Length; c++)
            _blank[c] = c != ' ' && ui.Glyphs[c].All(b => b == 0);
        _banks = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "tables", "palettes.json")))
            .RootElement.GetProperty("banks").EnumerateArray()
            .Select(b => b.EnumerateArray().Select(c => (ushort)c.GetInt32()).ToArray()).ToArray();
        _bank6 = _banks[6];
        using var pf = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "pics", "panfile.json")));
        _cmap = pf.RootElement.GetProperty("palette12").EnumerateArray().Select(c => (ushort)c.GetInt32()).ToArray();
        var pan = Convert.FromHexString(pf.RootElement.GetProperty("pixels_hex").GetString()!);
        _camo = new byte[CamW * CamH];
        for (var j = 0; j < CamH; j++)
            for (var i = 0; i < CamW; i++)
                _camo[j * CamW + i] = pan[(CamY + j) * ScreenW + CamX + i];
        _cover = ArtPalette(dir, "cover");
        _logo = ArtPalette(dir, "logo");
    }

    // A print-art cut's own best 16 colours (gen_assets.py's median cut): palette12 + indices.
    static ushort[]? ArtPalette(string dir, string name)
    {
        var p = Path.Combine(dir, "manual", name + ".json");
        if (!File.Exists(p)) return null;
        using var f = JsonDocument.Parse(File.ReadAllText(p));
        return f.RootElement.GetProperty("palette12").EnumerateArray().Select(c => (ushort)c.GetInt32()).ToArray();
    }

    /// <summary>Characters the font lacks, to the nearest glyph it has (checked against the font:
    /// every glyph 32-122 and 161-255 has pixels; { | } ~ and 127-160 are blank). Anything else '?'.</summary>
    public string MapText(string s)
    {
        var o = new System.Text.StringBuilder();
        foreach (var c in s)
        {
            var m = c switch
            {
                '—' or '–' => '-',
                '★' => '*',
                '‘' or '’' => '\'',
                '“' or '”' => '"',
                '…' => '.',
                _ => c,
            };
            if (m == c && (uint)c >= _blank.Length || m == c && _blank[c]) m = '?';
            o.Append(m);
        }
        return o.ToString();
    }

    public static ManualBook Load(string gameDataDir, Ui ui)
    {
        var book = new ManualBook(ui, gameDataDir);
        using var f = JsonDocument.Parse(File.ReadAllText(Path.Combine(gameDataDir, "manual", "manual.json")));
        book.Layout(f.RootElement.GetProperty("pages"));
        return book;
    }

    /// <summary>The later picture job plugs in here; without one figures draw as frames.</summary>
    public ManualBook WithFigures(IFigureSource source) { _figures = source; return this; }

    // ------------------------------------------------------------ palette

    /// <summary>The page's 64 $0RGB colours, Extra Half-Brite: 0-15 = panfile's CMAP (the panel's
    /// bank 4: the frame band, and the text of a picture page), 16-31 = the text's bank 6 on a
    /// text page (as the briefing shows it), else the page's picture bank (the menus' bank 5,
    /// the map's bank 2, the icons' bank 4, a print-art cut's own cut-down scan; a bank-6 figure
    /// draws in 0-15, and an icon page's pixels are 0-15 too - the icons share the panel CMAP -
    /// so 16-31 stay unused there), 32-63 = colour n-32 with each of R, G, B halved, rounded
    /// down - the Amiga's EHB mode.</summary>
    public ushort[] Palette(int page)
    {
        var pal = new ushort[64];
        Array.Copy(_cmap, pal, 16);
        var pic = _pages[page].Figures.Select(f => FigureBank(f.Id)).FirstOrDefault(b => b != null) ?? _bank6;
        Array.Copy(pic, 0, pal, 16, 16);
        for (var i = 0; i < 32; i++)
        {
            int c = pal[i], r = c >> 8 & 0xF, g = c >> 4 & 0xF, b = c & 0xF;
            pal[32 + i] = (ushort)((r >> 1) << 8 | (g >> 1) << 4 | (b >> 1));
        }
        return pal;
    }

    /// <summary>Which bank a figure's pixels belong to (in\figures.md): the menus' bank 5, the
    /// map's bank 2, the panel icons' bank 4 (panfile's CMAP), the print art's own 16 colours;
    /// null = the soldier bar and briefing are bank 6 already, so they draw in 0-15.</summary>
    public ushort[]? FigureBank(string id)
    {
        if (id == "p01-1") return _cover;
        if (id == "p29-1") return _logo;
        if (id is "p07-1" or "p08-1" or "p09-1") return _banks[5];
        if (id == "p17-1") return _banks[2];
        if (ManualFigures.IsIcon(id)) return _banks[4];
        return null;
    }

    // ------------------------------------------------------------ layout

    public enum K { Body, Bold, Heading1, Heading2 }

    // Space = the source had a space in front of this word (inside a run, or the run itself
    // starts with one). Punctuation like "Resume" in quotes has none and is drawn touching.
    readonly record struct Word(string Text, K Style, bool Space);

    string _title = "HUNTER";                                        // before the first heading1 (the cover)
    Page _p = new() { Title = "HUNTER" };
    int _y = BodyTop;
    bool _wantBlank;                                                 // a full blank line after a heading
    bool _wantHalf;                                                  // a blank half-line after a paragraph or item
    // headings1 swallowed into the page title bar instead of printed twice (in order)
    readonly List<string> _suppressed = new();
    /// <summary>Heading1 blocks that opened a fresh page: the title bar shows them, so the body
    /// does not repeat them (the word test drops them from its source the same way).</summary>
    public IReadOnlyList<string> SuppressedHeadings => _suppressed;

    void Layout(JsonElement pages)
    {
        TitlePage(pages);
        var first = true;                                                // page 1 IS the title page
        foreach (var src in pages.EnumerateArray())
        {
            var skip = first;                                            // its words and art are already placed
            first = false;
            if (skip) continue;
            foreach (var b in src.GetProperty("blocks").EnumerateArray())
            {
            var kind = b.GetProperty("kind").GetString();
            if (kind == "figure")
            {
                var id = b.GetProperty("id").GetString()!;
                var h0 = b.GetProperty("h").GetInt32();
                var w0 = b.GetProperty("w").GetInt32();
                int sw, sh, top;
                double f;
                if (ManualFigures.IsIcon(id))
                {                                                    // the icon cell at 2x in its button frame
                    sw = ManualFigures.IconW;
                    sh = ManualFigures.IconH;
                    if (_y + sh > BodyBottom) NewPage();             // a figure that does not fit gets a page of its own
                    top = _y;
                }
                else if (ManualFigures.IsScreen(id))
                {                                                    // a whole game screen: a page of its own
                    if (_p.Lines.Count > 0 || _p.Figures.Count > 0) NewPage();
                    var (cw, ch) = ManualFigures.FigureSize(_ui, _dir, id);   // the menu's button block, or the screen
                    f = Math.Min(1.0, Math.Min((double)WinW / cw, (double)(BodyBottom - FigTop) / ch));
                    sw = (int)Math.Round(cw * f);
                    sh = (int)Math.Round(ch * f);
                    top = FigTop;                                    // under the title bar, above the footer's blank line
                }
                else
                {
                    f = Math.Min(1.0, (double)WinW / w0);            // never wider than the window
                    sw = (int)Math.Round(w0 * f);
                    sh = (int)Math.Round(h0 * f);
                    if (_y + sh > BodyBottom) NewPage();             // a figure that does not fit gets a page of its own
                    top = _y;
                }
                _p.Figures.Add(new Figure
                {
                    Id = id,
                    Desc = MapText(b.GetProperty("desc").GetString()!),
                    X = WinX + (WinW - sw) / 2,
                    Y = top,
                    W = w0, H = h0, SW = sw, SH = sh
                });
                _y = top + sh + LineH;
                _wantBlank = _wantHalf = false;
                continue;
            }
            var runs = b.GetProperty("spans").EnumerateArray()
                .Select(s => (MapText(s.GetProperty("text").GetString()!), s.GetProperty("bold").GetBoolean()))
                .ToList();
            var style = kind == "heading1" ? K.Heading1 : kind == "heading2" ? K.Heading2 : K.Body;
            if (_y > BodyTop)
            {                                                        // the flow's gap first: a heading is judged
                var gap = _wantBlank ? LineH : _wantHalf ? HalfGap : 0;
                if (gap > 0)                                         // on the page it really lands on (its own
                {                                                    // page break may happen right here)
                    if (_y + gap > BodyBottom) NewPage(); else _y += gap;
                }
            }
            if (style == K.Heading1 && runs.Count > 0)
            {
                var text = string.Join(" ", runs.Select(r => r.Item1).ToArray()).Trim();
                _title = text;                                       // the section the next pages are in
                if (_y + LineH > BodyBottom) NewPage();              // the bar must fit where the heading
                                                                     // lands, or the break happens here - not
                if (_p.Lines.Count == 0)                             // inside EmitLine, after _title changed
                {                                                    // nothing printed where it landed (a fresh
                    _p.Title = text;                                 // page, or one holding only a screen): the
                    _suppressed.Add(text);                           // title bar carries it - never print twice
                    _wantBlank = _wantHalf = false;
                    continue;
                }
            }
            var words = Words(runs, style);
            var indent = kind == "item" ? ItemIndent : 0;
            if (kind == "item") words.Insert(0, new Word("- ", K.Body, false));
            EmitWrapped(words, indent, underline: style == K.Heading2);
            _wantBlank = style is K.Heading1 or K.Heading2;
            _wantHalf = !_wantBlank;
            }
        }
        _pages.Add(_p);
    }

    // Page 1 = the manual's cover as a title page: the cover scan (p01-1) scaled to fit inside
    // the frame keeping its shape, "HUNTER" over it (the title bar) and under it, with the
    // cover's own lines ("CK - 2940", "ACTIVISION") as the print has them.
    void TitlePage(JsonElement pages)
    {
        int w0 = 320, h0 = 150;                                          // p01-1's box in manual.json
        foreach (var b in pages.EnumerateArray().First().GetProperty("blocks").EnumerateArray())
            if (b.GetProperty("kind").GetString() == "figure" && b.GetProperty("id").GetString() == "p01-1")
            { w0 = b.GetProperty("w").GetInt32(); h0 = b.GetProperty("h").GetInt32(); break; }
        const int artSh = 104;                                           // the art's height under its two lines
        var sh = Math.Min(artSh, h0);
        var sw = (int)Math.Round((double)w0 * sh / h0);
        if (sw > WinW) { sw = WinW; sh = (int)Math.Round((double)h0 * sw / w0); }
        _p.Lines.Add(Centred("CK - 2940", BodyTop, K.Body));
        _p.Figures.Add(new Figure
        {
            Id = "p01-1", Desc = "the cover",
            X = WinX + (WinW - sw) / 2, Y = 40, W = w0, H = h0, SW = sw, SH = sh
        });
        _p.Lines.Add(Centred("HUNTER", 150, K.Bold));
        _p.Lines.Add(Centred("ACTIVISION", 160, K.Bold));
        NewPage();                                                       // the cover stands alone: page 2 starts fresh
    }

    Line Centred(string text, int y, K style) => new()
    {
        Y = y,
        Width = WidthOf(text),
        Spans = { new Span { Text = text, X = (WinW - WidthOf(text)) / 2, Style = style } }
    };

    void NewPage()
    {
        _pages.Add(_p);
        _p = new Page { Title = _title };
        _y = BodyTop;
        _wantBlank = _wantHalf = false;
    }

    List<Word> Words(List<(string Text, bool Bold)> runs, K style)
    {
        var o = new List<Word>();
        var prevSpace = false;
        foreach (var (text, bold) in runs)
        {
            var st = bold ? K.Bold : style;
            var ws = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lead = text.Length > 0 && (text[0] == ' ' || prevSpace);
            for (var j = 0; j < ws.Length; j++)
                o.Add(new Word(ws[j], st, j > 0 || lead && j == 0));
            prevSpace = text.Length > 0 && text[^1] == ' ' || ws.Length == 0 && prevSpace;
        }
        return o;
    }

    int WidthOf(string s)
    {
        var w = 0;
        foreach (var c in s) w += _ui.Widths[c] + 1;
        return w;
    }

    /// <summary>The drawn width of a span, gaps included (the tests read line extents with it).</summary>
    public int TextWidth(string s) => WidthOf(s);

    /// <summary>How far a list item's wrapped lines are indented: the width of its "- " marker.</summary>
    public int ItemIndent => WidthOf("- ");

    // Word wrap by the font's real widths: a word never splits, a line never passes 36 columns
    // (the window inside the frame). Continuation lines of a list item sit under the item's
    // text, not its "- " marker.
    void EmitWrapped(List<Word> words, int indent, bool underline)
    {
        words = BreakLong(words, indent);
        var start = 0;
        var width = 0;
        var first = true;
        for (var i = 0; i < words.Count; i++)
        {
            var ww = WidthOf(words[i].Text);
            var gap = width == 0 || !words[i].Space ? 0 : WidthOf(" ");
            var max = first ? WinW : WinW - indent;                  // continuation lines keep the item's indent
            if (width > 0 && width + gap + ww > max)
            {
                EmitLine(words, start, i, first ? 0 : indent, underline);
                first = false;
                start = i;
                width = ww;                                          // the word that wrapped starts the new line
            }
            else width += gap + ww;
        }
        if (words.Count > start) EmitLine(words, start, words.Count, first ? 0 : indent, underline);
    }

    // One token wider than the whole window (the contents page's dot leaders) breaks at the
    // column edge, the way the printed page breaks them; chunks count as words. An indented
    // item's continuation lines are narrower, so its chunks cap at the indented width.
    List<Word> BreakLong(List<Word> words, int indent)
    {
        var o = new List<Word>();
        foreach (var w in words)
        {
            if (WidthOf(w.Text) <= WinW - indent) { o.Add(w); continue; }
            var chunk = "";
            foreach (var c in w.Text)
            {
                if (WidthOf(chunk + c) > WinW - indent) { o.Add(new Word(chunk, w.Style, w.Space)); chunk = ""; }
                chunk += c;
            }
            if (chunk.Length > 0) o.Add(new Word(chunk, w.Style, w.Space));
        }
        return o;
    }

    /// <summary>How the layout breaks a token wider than the window; the tests use the same rule.</summary>
    public string[] BreakLongWord(string text, int indent = 0)
    {
        if (WidthOf(text) <= WinW - indent) return new[] { text };
        var o = new List<string>();
        var chunk = "";
        foreach (var c in text)
        {
            if (WidthOf(chunk + c) > WinW - indent) { o.Add(chunk); chunk = ""; }
            chunk += c;
        }
        if (chunk.Length > 0) o.Add(chunk);
        return o.ToArray();
    }

    void EmitLine(List<Word> words, int from, int to, int x, bool underline)
    {
        if (_y + LineH > BodyBottom) NewPage();
        var line = new Line { Y = _y, Underline = underline };
        var i = from;
        while (i < to)
        {
            var style = words[i].Style;
            var text = words[i].Text;
            var gapBefore = words[i].Space;
            i++;
            while (i < to && words[i].Style == style)
            {
                if (words[i].Space && text[^1] != ' ') text += " ";  // the "- " marker already ends in a space
                text += words[i++].Text;
            }
            if (line.Spans.Count > 0 && gapBefore && line.Spans[^1].Text[^1] != ' ')
                text = " " + text;                                   // a style change keeps the source's space
            line.Spans.Add(new Span { Text = text, X = x, Style = style, Inverse = style == K.Heading1 });
            x += WidthOf(text);
        }
        line.Width = x;
        _p.Lines.Add(line);
        _y += LineH;
    }

    // The span's palette index: bank 6's tones on a text page, the panel's own (bank 4) on a
    // picture page - there 16-31 belongs to the picture, and 0-15 is what stays readable.
    int ColourOf(K style, bool picture) => style switch
    {
        K.Bold => picture ? PicBold : ColBold,
        K.Heading2 => picture ? PicHeading2 : ColHeading2,
        _ => picture ? PicBody : ColBody                             // heading1 is drawn inverse
    };

    // ------------------------------------------------------------ draw

    /// <summary>Draws one screen page (the caller fades it in through Palette(page)).</summary>
    public void Draw(IndexedScreen s, int page)
    {
        s.Clear();                                                       // a whole page: Fill and the text only SET plane bits, so nothing of an earlier page may stay
        var p = _pages[page];
        var picture = p.Figures.Select(f => FigureBank(f.Id)).FirstOrDefault(b => b != null) != null;
        Chrome(s);
        Bar(s, WinX, TitleY, WinW, GlyphH, picture ? PicBold : ColBody); // title bar, pale with dark
        DrawText(s, p.Title, WinX, TitleY, 1, inverse: true);            // text (the inverse draw's index 1)
        foreach (var line in p.Lines)
        {
            if (line.Spans.Any(sp => sp.Inverse))                        // a heading1 row: pale bar, dark text
                Bar(s, WinX, line.Y, WinW, GlyphH, picture ? PicBold : ColBody);
            foreach (var sp in line.Spans)
                DrawText(s, sp.Text, WinX + sp.X, line.Y, ColourOf(sp.Style, picture), sp.Inverse);
            if (line.Underline) Fill(s, WinX, line.Y + GlyphH, line.Width, 1, ColourOf(K.Heading2, picture));
        }
        foreach (var f in p.Figures)
        {
            Frame(s, f.X, f.Y, f.SW, f.SH);
            DrawText(s, f.Id, f.X + (f.SW - WidthOf(f.Id)) / 2, f.Y + (f.SH - GlyphH) / 2, ColourOf(K.Body, picture));
            _figures?.Draw(f.Id, s, f.X, f.Y, f.SW, f.SH);
        }
        // bottom line, inside the frame: where you are and how to move
        DrawText(s, $"PAGE {page + 1} OF {_pages.Count}", WinX, FooterY, ColourOf(K.Body, picture));
        var hints = "<  >  ESC";
        DrawText(s, hints, WinX + WinW - WidthOf(hints), FooterY, ColourOf(K.Body, picture));
    }

    // The frame: a band 8 px wide round the page, tiled with the panel's own camouflage patch,
    // the panel's edge lines on its outer and inner border (bright 12 outside, dark 14 inside;
    // both flip on the bottom edge, as the panel's own boxes do), and the band's EHB shadow
    // 1 px right and down inside the opening.
    void Chrome(IndexedScreen s)
    {
        for (var y = FrameY0; y <= FrameY1; y++)
            for (var x = FrameX0; x <= FrameX1; x++)
            {
                var inBand = x <= FrameX0 + Band || x >= FrameX1 - Band ||
                             y <= FrameY0 + Band || y >= FrameY1 - Band;
                if (!inBand) continue;
                int idx;
                if (x == FrameX0 || x == FrameX1 || y == FrameY0 || y == FrameY1)
                    idx = y == FrameY1 ? EdgeDark : EdgeBright;              // outer border: bright, dark under
                else if (x == FrameX0 + Band || x == FrameX1 - Band ||
                         y == FrameY0 + Band || y == FrameY1 - Band)
                    idx = y == FrameY1 - Band ? EdgeBright : EdgeDark;       // inner border: the bevel flips
                else
                    idx = _camo[(y - FrameY0) % CamH * CamW + (x - FrameX0) % CamW];
                s.Pixels[y * ScreenW + x] = (byte)idx;
            }
        // the band's shadow on the page, 1 px in from the inner border, right and bottom
        for (var y = FrameY0 + Band + 2; y <= FrameY1 - Band - 2; y++) Shadow(s, FrameX1 - Band - 1, y, ColShadow + 32);
        for (var x = FrameX0 + Band + 2; x <= FrameX1 - Band - 1; x++) Shadow(s, x, FrameY1 - Band - 1, ColShadow + 32);
    }

    // A bar (title bar, heading1) with its drop shadow 1 px right and down.
    static void Bar(IndexedScreen s, int x, int y, int w, int h, int colour)
    {
        for (var i = 0; i < w; i++) Shadow(s, x + i + 1, y + h, colour + 32);
        for (var j = 0; j < h; j++) Shadow(s, x + w, y + j + 1, colour + 32);
        Fill(s, x, y, w, h, colour);
    }

    void DrawText(IndexedScreen s, string text, int x, int y, int colour, bool inverse = false)
    {
        foreach (var c in text)
        {
            if (inverse) _ui.DrawChar(s, c, x, y, 0, inverse: true);
            else
            {
                for (var plane = 0; plane < 6; plane++)                  // 6 planes: indices reach 31 + EHB;
                    if ((colour & (1 << plane)) != 0)                    // EVERY set plane draws - the old
                        _ui.DrawChar(s, c, x, y, plane);                 // break left body text as index 1
                GlyphShadow(s, c, x, y, colour + 32);                    // the EHB shadow under the glyph
            }
            x += _ui.Widths[c] + 1;
        }
    }

    // The glyph's own pixels again, 1 px right and down, into free (background or shadow) pixels.
    void GlyphShadow(IndexedScreen s, char c, int x, int y, int idx)
    {
        var g = _ui.Glyphs[c];
        var w = _ui.Widths[c];
        for (var line = 0; line < 8; line++)
            for (var i = 0; i < 8 && i <= w; i++)
                if ((g[line] & (0x80 >> i)) != 0)
                    Shadow(s, x + i + 1, y + line + 1, idx);
    }

    /// <summary>Writes an EHB shadow index, but only where nothing drawn sits yet.</summary>
    internal static void Shadow(IndexedScreen s, int x, int y, int idx)
    {
        if ((uint)x >= s.Width || (uint)y >= s.Height) return;
        ref var t = ref s.Pixels[y * s.Width + x];
        if (t == 0 || t >= 32) t = (byte)idx;
    }

    static void Fill(IndexedScreen s, int x, int y, int w, int h, int colour)
    {
        for (var plane = 0; plane < 6; plane++)
            if ((colour & (1 << plane)) != 0)
                for (var j = 0; j < h; j++)
                    for (var i = 0; i < w; i++)
                        s.SetPlaneBit(x + i, y + j, plane, true);
    }

    void Frame(IndexedScreen s, int x, int y, int w, int h)
    {
        Fill(s, x, y, w, 1, ColFrame);
        Fill(s, x, y + h - 1, w, 1, ColFrame);
        Fill(s, x, y, 1, h, ColFrame);
        Fill(s, x + w - 1, y, 1, h, ColFrame);
    }
}
