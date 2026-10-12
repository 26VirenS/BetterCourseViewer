using Simpl.Graphics;
using Simpl.Shell;
using Simpl.UI;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.Screens;

/// <summary>
/// The first thing the window shows (the Mac's school picker): a field to find the school — by its name, or its
/// address typed — and what matches, in glass over a calm ground; one picked opens it.
/// </summary>
public sealed class SchoolPicker
{
    private readonly GlassHost host;
    private readonly Action<string> picked;
    private readonly SchoolSearch search = new();
    private string text = "";

    public SchoolPicker(GlassHost host, Action<string> picked)
    {
        this.host = host;
        this.picked = picked;
        search.PropertyChanged += (_, _) => host.Invalidate();
        _ = Load();
    }

    private async Task Load()
    {
        await search.Load();
        host.Invalidate();
    }

    public void Frame(Ui ui)
    {
        var t = ui.T;
        t.Dark = true;
        ui.Layer(Ui.Page);
        // a calm ground of colour for the glass to frost
        Blob(ui, ui.W * 0.22f, ui.H * 0.3f, 360, Theme.Rgb(0, 122, 255, 0.35f));
        Blob(ui, ui.W * 0.8f, ui.H * 0.7f, 420, Theme.Rgb(255, 149, 0, 0.28f));
        Blob(ui, ui.W * 0.6f, ui.H * 0.15f, 300, Theme.Rgb(52, 199, 89, 0.22f));

        ui.Layer(Ui.Chrome);
        var w = Math.Min(560, ui.W - 48);
        var card = new R((ui.W - w) / 2, Math.Max(40, ui.H * 0.16f), w, Math.Min(520, ui.H - 80));
        ui.Glass(card, 26);
        ui.Text("Welcome to Simpl", card.X, card.Y + 34, Theme.Title, t.Ink(), card.W, TextAlignment.Center);
        ui.Text("Find your school to sign in with Canvas or Brightspace.", card.X + 30, card.Y + 78, Theme.Callout, t.Ink2, card.W - 60, TextAlignment.Center);
        var field = new R(card.X + 28, card.Y + 116, card.W - 56, 42);
        ui.Focus ??= "school";
        var before = text;
        var entered = ui.TextField("school", field, ref text, "Your school's name or address", Theme.Body, "magnifyingglass");
        if (text != before) search.Find(text);

        var y = field.Bottom + 14;
        var results = search.Results;
        if (entered)
        {
            if (results.Count > 0) { picked(results[0].Domain); return; }
            if (SchoolSearch.AddressLike(text) is { } addr) { picked(addr); return; }
        }
        var room = card.Bottom - 20 - y;
        var vp = new R(card.X + 14, y, card.W - 28, room);
        var off = ui.BeginScroll("schools", vp);
        var ry = y - off;
        foreach (var s in results.Take(60))
        {
            var r = new R(vp.X, ry, vp.W, 52);
            var i = ui.Row("school:" + s.Id, r, false, 12);
            ui.Icon("graduationcap", r.X + 22, r.CenterY, 16, t.Accent);
            ui.Text(s.Name, r.X + 44, r.Y + 7, Theme.Body, t.Ink(), r.W - 56);
            ui.Text(s.Domain, r.X + 44, r.Y + 28, Theme.Footnote, t.Ink2, r.W - 56);
            if (i.Clicked) { picked(s.Domain); return; }
            ry += 54;
        }
        if (results.Count == 0 && text.Trim().Length > 1)
        {
            if (search.Asking) ui.Spinner(vp.CenterX, ry + 30, 10);
            else if (SchoolSearch.AddressLike(text) is { } addr)
            {
                var r = new R(vp.X, ry, vp.W, 52);
                if (ui.Row("school:addr", r, false, 12).Clicked) { picked(addr); return; }
                ui.Icon("globe", r.X + 22, r.CenterY, 16, t.Accent);
                ui.Text($"Open {addr}", r.X + 44, r.CenterY - 10, Theme.Body, t.Ink(), r.W - 56);
                ry += 54;
            }
            else ui.Text("No school by that name yet — try its address (school.instructure.com).", vp.X + 12, ry + 10, Theme.Callout, t.Ink2, vp.W - 24, TextAlignment.Leading, 2);
        }
        ui.EndScroll("schools", vp, ry + off - y);
    }

    private static void Blob(Ui ui, float cx, float cy, float r, Color4 c)
    {
        for (var k = 6; k >= 1; k--) ui.Circle(cx, cy, r * k / 6f, Theme.WithAlpha(c, c.A * 0.22f));
    }
}
