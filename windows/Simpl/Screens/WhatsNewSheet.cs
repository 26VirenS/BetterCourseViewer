using System.IO;
using Simpl.Models;
using Simpl.Support;
using Simpl.UI;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.Screens;

/// <summary>
/// What's New (the Mac's WhatsNewSheet, measured on its picture): a sheet over the window — the app's icon, "What's New
/// in Simpl for Windows", the version, then each version's notes, newest first, a symbol for each note's kind — and
/// Continue at the foot.
/// </summary>
public sealed class WhatsNewSheet
{
    private readonly WhatsNewData data;
    private readonly Action close;

    public WhatsNewSheet(WhatsNewData data, Action close)
    {
        this.data = data;
        this.close = close;
    }

    public void Frame(Ui ui)
    {
        var t = ui.T;
        ui.Layer(Ui.Overlay);
        ui.Fill(new R(0, 0, ui.W, ui.H), new Color4(0, 0, 0, t.Dark ? 0.4f : 0.22f));
        ui.Block(new R(0, 0, ui.W, ui.H));
        const float w = 500;
        var h = Math.Min(560, ui.H - 124);
        var card = new R(MathF.Round((ui.W - w) / 2), MathF.Round((ui.H - h) / 2), w, h);
        ui.Glass(card, 14, GlassKind.Panel, null, 0, Ui.Shadow.Panel);
        ui.Fill(card, 14, Theme.WithAlpha(t.Card, t.Dark ? 0.6f : 0.85f));

        // the head: the icon in its tile, the title, the version
        var y = card.Y + 34;
        ui.Fill(new R(card.CenterX - 28, y, 56, 56), 13, t.Dark ? new Color4(1, 1, 1, 0.08f) : new Color4(1, 1, 1, 1));
        ui.Stroke(new R(card.CenterX - 28, y, 56, 56), 13, t.Edge);
        ui.Image(Path.Combine(AppPaths.AppDir, "Assets", "Simpl-256.png"), new R(card.CenterX - 24, y + 4, 48, 48));
        y += 56 + 16;
        ui.Text("What’s New in Simpl for Windows", card.X, y, Theme.Title2, t.Ink(), w, TextAlignment.Center);
        y += 30;
        ui.Text("Version " + data.Version, card.X, y, Theme.Callout, t.Ink2, w, TextAlignment.Center);
        y += 26;

        // the notes, scrolling to the foot
        const float footH = 62;
        var vp = new R(card.X, y, w, card.Bottom - footH - y);
        var off = ui.BeginScroll("whatsnew", vp);
        var ly = vp.Y - off;
        var lx = card.X + 32;
        var lw = w - 64;
        foreach (var r in data.Releases)
        {
            ui.Text("VERSION " + r.Version, lx, ly, Theme.Semibold(Theme.Caption), t.Ink2);
            if (!string.IsNullOrEmpty(r.Date))
            {
                var dm = ui.Measure(r.Date, Theme.Caption);
                ui.Text(r.Date, lx + lw - dm.X, ly, Theme.Caption, t.Ink2);
            }
            ly += 24;
            foreach (var n in r.Notes)
            {
                var (symbol, colour) = n.Kind switch
                {
                    "new" => ("sparkles", t.Green),
                    "fixed" => ("wrench.adjustable", t.Orange),
                    _ => ("arrow.up.circle", t.Accent),
                };
                ui.Fill(new R(lx, ly + 2, 30, 30), 15, Theme.WithAlpha(colour, 0.14f));
                ui.Icon(symbol, lx + 15, ly + 17, 13, colour, FontWeight.SemiBold);
                ui.Text(n.Title, lx + 44, ly, Theme.Semibold(Theme.Body), t.Ink(), lw - 44);
                var bh = ui.Text(n.Body ?? "", lx + 44, ly + 21, Theme.Callout, t.Ink2, lw - 44, TextAlignment.Leading, 0);
                ly += 21 + Math.Max(bh, 18) + 14;
            }
            ly += 10;
        }
        ui.EndScroll("whatsnew", vp, ly + off - vp.Y + 8);

        // the foot: Continue
        ui.Fill(new R(card.X, card.Bottom - footH, w, 1), t.Separator);
        var done = ui.Button("whatsnew:continue", new R(card.Right - 16 - 84, card.Bottom - footH + 16, 84, 30), "Continue");
        foreach (var k in ui.Keys) if (k is Key.Escape or Key.Enter) done = true;
        if (done) close();
    }
}
