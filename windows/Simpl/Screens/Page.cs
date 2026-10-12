using Simpl.UI;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.Screens;

/// <summary>
/// A screen's scrolling page (the Mac's Page): Simpl's quiet ground, its content in a column that keeps a readable
/// width on a wide window — padded 40 (22 on a narrow window), at most 1480 wide, 26 under the toolbar's band, 40 at
/// the foot — scrolling under the toolbar, which stays legible on a soft edge of the page's colour.
/// </summary>
public struct PageFrame
{
    public R Content;
    public string Id;
    public float X, W, Top, Offset;

    public static PageFrame Begin(Ui ui, R content, string id = "page")
    {
        var pad = content.W < 700 ? 22 : 40;
        var room = Math.Max(0, Math.Min(1480, content.W - pad * 2));
        var off = ui.BeginScroll(id, content);
        return new PageFrame { Content = content, Id = id, X = content.X + pad, W = room, Top = Ui.Caption + 26 - off, Offset = off };
    }

    /// <summary>The page's end: how tall it was (the foot's 40 added), and the toolbar's edge when scrolled.</summary>
    public void End(Ui ui, float bottom)
    {
        ui.EndScroll(Id, Content, bottom + Offset - Content.Y + 40);
        if (Offset > 0.5f)
        {
            var was = ui.CurrentLayer;
            ui.Layer(Ui.PageOver);
            var a = Math.Min(1, Offset / 24);
            var c = ui.T.Page;
            ui.Fill(new R(Content.X, Content.Y, Content.W, Ui.Caption), Theme.WithAlpha(c, 0.92f * a));
            ui.FillGradientV(new R(Content.X, Content.Y + Ui.Caption, Content.W, 18), Theme.WithAlpha(c, 0.92f * a), Theme.WithAlpha(c, 0));
            ui.Layer(was);
        }
    }
}

/// <summary>The Mac's line heights for its type scale, so spacing measures the same with Segoe's glyphs in SF's lines.</summary>
public static class Lines
{
    public static float Of(Font f) => f.Size switch
    {
        >= 34 => 41,
        >= 28 => 34,
        >= 22 => 27,
        >= 19 => 23,
        >= 16 => 20,
        >= 15 => 18,
        >= 14 => 17,
        >= 13 => 16,
        >= 12 => 15,
        _ => 13,
    };

    /// <summary>One line of words in its line box (the Mac's height), left-aligned; returns the line's height.</summary>
    public static float Text(Ui ui, string s, float x, float y, Font f, Color4 c, float maxW = 100000, TextAlignment align = TextAlignment.Leading, bool strike = false)
    {
        var lh = Of(f);
        var m = ui.Measure(s, f, maxW).Y;
        ui.Text(s, x, y + (lh - m) / 2, f, c, maxW, align, 1, strike);
        return lh;
    }

    /// <summary>Words wrapped over at most <paramref name="lines"/> lines; returns the height taken.</summary>
    public static float Wrapped(Ui ui, string s, float x, float y, Font f, Color4 c, float maxW, int lines, bool strike = false)
    {
        var m = ui.Measure(s, f, maxW, lines);
        var one = Of(f);
        var n = Math.Max(1, (int)MathF.Round(m.Y / Math.Max(1, ui.Measure("Ag", f).Y)));
        n = Math.Min(n, lines);
        var h = one * n;
        ui.Text(s, x, y + (h - m.Y) / 2, f, c, maxW, TextAlignment.Leading, lines, strike);
        return h;
    }

    /// <summary>How many lines words take when wrapped.</summary>
    public static int Count(Ui ui, string s, Font f, float maxW, int lines)
    {
        var m = ui.Measure(s, f, maxW, lines);
        return Math.Clamp((int)MathF.Round(m.Y / Math.Max(1, ui.Measure("Ag", f).Y)), 1, lines);
    }
}

/// <summary>A screen's own large heading (the Mac's ScreenHeading): 34 bold, the line under it 14 secondary; returns its height.</summary>
public static class Headings
{
    public static float Screen(Ui ui, string title, string? sub, float x, float y, float w, Color4? swatch = null)
    {
        var tx = x;
        if (swatch is { } c)
        {
            ui.Fill(new R(x, y + 14, 14, 14), 4, c);
            tx += 24;
        }
        Lines.Text(ui, title, tx, y, Theme.LargeTitle, ui.T.Ink(), w);
        var h = 41f;
        if (!string.IsNullOrEmpty(sub))
        {
            Lines.Text(ui, sub!, x, y + 45, Theme.Callout, ui.T.Ink2, w);
            h = 62;
        }
        return h;
    }

    /// <summary>A section's heading (the Mac's PageSection): 19 semibold, with what is said at its end 14 secondary, and room for an accessory; returns 23.</summary>
    public static float Section(Ui ui, string title, float x, float y, float w, string? trailing = null, float accessoryW = 0)
    {
        Lines.Text(ui, title, x, y, Theme.Title3, ui.T.Ink(), w * 0.6f);
        if (!string.IsNullOrEmpty(trailing))
        {
            var m = ui.Measure(trailing!, Theme.Callout).X;
            Lines.Text(ui, trailing!, x + w - accessoryW - (accessoryW > 0 ? 10 : 0) - m, y + 3, Theme.Callout, ui.T.Ink2);
        }
        return 23;
    }
}
