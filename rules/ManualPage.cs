using System.Text.Json;
namespace Hunter.Rules;

/// <summary>
/// The manual screen as the shell shows it: one page of the book on its own 320 x 200
/// IndexedScreen, faded in over 15 steps with the game's own Fade ($6918/$6948/$69B2) through the
/// page's palette. Palette(page) is 64 EHB colours - two banks of 16 and their halves - so the
/// fade is two Fade banks (0-15 the frame's CMAP, 16-31 the page picture's own) and the 32-63
/// halves follow the faded colours (the Amiga halves the displayed colour). A page turn draws
/// without a fade. The WorldState is not even handed to it: the shell runs Frame() instead of the
/// game's step while the manual is up, so no blank, no pass and no sound tick runs, and closing
/// carries on from the same frame.
/// </summary>
public sealed class ManualPage
{
    readonly ManualBook _book;
    readonly IndexedScreen _screen = new();
    readonly Fade _low = new(), _high = new();                      // indices 0-15, 16-31
    int _page;

    public ManualPage(ManualBook book) => _book = book;

    public int Page => _page;
    public int Pages => _book.Pages.Count;
    public IndexedScreen Screen => _screen;
    public bool Fading => _low.Busy;

    /// <summary>Show a page. fade: 15 steps from black (opening the manual); without it the page
    /// is full on at once (a page turn).</summary>
    public void Show(int page, bool fade)
    {
        _page = ((page % _book.Pages.Count) + _book.Pages.Count) % _book.Pages.Count;
        _screen.Clear();                                             // ManualBook.Draw only draws over: a turn left the last page under it
        _book.Draw(_screen, _page);
        var pal = _book.Palette(_page);
        _low.SetBank(pal[..16]);
        _high.SetBank(pal[16..32]);
        _low.FadeTo(15);
        _high.FadeTo(15);
        if (!fade) for (var i = 0; i < 15; i++) { _low.Tick(); _high.Tick(); }
    }

    /// <summary>Left / right (wrapping), Home / End: a page turn, no fade.</summary>
    public void Turn(int dir) => Show(_page + dir, fade: false);
    public void First() => Show(0, fade: false);
    public void Last() => Show(_book.Pages.Count - 1, fade: false);

    /// <summary>One frame while up: the fade's step. The picture is drawn from PaletteNow().</summary>
    public void Frame()
    {
        _low.Tick();
        _high.Tick();
    }

    /// <summary>The page's 64 colours as they show now: the two faded banks and their EHB halves
    /// (each channel of colour n-32 halved, rounded down, as Palette does).</summary>
    public ushort[] PaletteNow()
    {
        var pal = new ushort[64];
        for (var i = 0; i < 16; i++) pal[i] = _low.Colour(i);
        for (var i = 0; i < 16; i++) pal[16 + i] = _high.Colour(i);
        for (var i = 0; i < 32; i++)
        {
            int c = pal[i], r = c >> 8 & 0xF, g = c >> 4 & 0xF, b = c & 0xF;
            pal[32 + i] = (ushort)((r >> 1) << 8 | (g >> 1) << 4 | (b >> 1));
        }
        return pal;
    }

    /// <summary>A click on the page's own footer hints (Draw's "&lt;  &gt;  ESC" at FooterY, 8 px
    /// tall): -1 the "&lt;", 1 the "&gt;", 0 the "ESC" (close), null nowhere.</summary>
    public int? FooterClick(int x, int y)
    {
        if (y < ManualBook.FooterY || y >= ManualBook.FooterY + ManualBook.GlyphH) return null;
        var x0 = ManualBook.WinX + ManualBook.WinW - _book.TextWidth("<  >  ESC");
        foreach (var (text, act) in new[] { ("<", -1), (">", 1), ("ESC", 0) })
        {
            var w = _book.TextWidth(text);
            if (x >= x0 && x < x0 + w) return act;
            x0 += w + _book.TextWidth("  ");
        }
        return null;
    }
}
