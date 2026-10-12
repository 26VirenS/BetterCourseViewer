using Simpl.Support;
using Simpl.UI;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.Shell;

/// <summary>A question put to the student (the Mac's alert, a confirmation dialog): its title, its words, its buttons, and who hears the answer.</summary>
public sealed class Alert
{
    public string Title = "";
    public string Message = "";
    /// <summary>Each button: its words; whether it is the one that goes ahead (prominent), the one that destroys something, or the way out (cancel).</summary>
    public (string Label, bool Prominent, bool Destructive, bool Cancel)[] Buttons = Array.Empty<(string, bool, bool, bool)>();
    public Action<int>? Picked;
    /// <summary>The window it is over (its host): the Settings window's alerts are not the main window's.</summary>
    public object? Owner;
}

/// <summary>The alert up over a window, drawn by the renderer as the Mac's own (the app's icon, the title, the words, the buttons).</summary>
public static class Alerts
{
    public static Alert? Current { get; private set; }
    public static event Action? Changed;

    /// <summary>Put a question: a destructive choice and a way out (the Mac's confirmationDialog).</summary>
    public static void Confirm(string title, string message, string go, Action onGo, object? owner = null, bool destructive = true)
    {
        Show(new Alert
        {
            Title = title,
            Message = message,
            Buttons = new[] { (go, !destructive, destructive, false), ("Cancel", false, false, true) },
            Picked = i => { if (i == 0) onGo(); },
            Owner = owner,
        });
    }

    public static void Show(Alert a)
    {
        Current = a;
        Changed?.Invoke();
    }

    public static void Close()
    {
        Current = null;
        Changed?.Invoke();
    }

    /// <summary>The alert, if one is up over this window (its host): over everything, centred (the Mac's: 260 wide, the icon over the title).</summary>
    public static void Draw(Ui ui, object? owner)
    {
        if (Current is not { } a || !ReferenceEquals(a.Owner, owner)) return;
        var t = ui.T;
        ui.Layer(Ui.Overlay + 2);
        ui.Block(new R(0, 0, ui.W, ui.H));
        const float w = 260, pad = 16;
        var titleH = ui.Measure(a.Title, Theme.Bold(Theme.Subheadline), w - 2 * pad, 0).Y;
        var msgH = a.Message.Length > 0 ? ui.Measure(a.Message, Theme.Caption2, w - 2 * pad, 0).Y + 8 : 0;
        // (the buttons side by side when two short ones fit; one under another otherwise, as the Mac lays them)
        var stacked = a.Buttons.Length > 2 || a.Buttons.Any(b => b.Label.Length > 12);
        var buttonsH = stacked ? a.Buttons.Length * 28 + (a.Buttons.Length - 1) * 8 : 28;
        var h = pad + 64 + 12 + titleH + msgH + 16 + buttonsH + pad;
        var panel = new R(MathF.Round((ui.W - w) / 2), MathF.Round((ui.H - h) / 2 - 40), w, h);
        ui.Glass(panel, 12, GlassKind.Panel, null, 0, Ui.Shadow.Panel);
        ui.Fill(panel, 12, Theme.WithAlpha(t.Card, t.Dark ? 0.7f : 0.8f));
        var y = panel.Y + pad;
        ui.Image(System.IO.Path.Combine(AppPaths.AppDir, "Assets", "Simpl-256.png"), new R(panel.CenterX - 32, y, 64, 64));
        y += 64 + 12;
        ui.Text(a.Title, panel.X + pad, y, Theme.Bold(Theme.Subheadline), t.Ink(), w - 2 * pad, TextAlignment.Center, 0);
        y += titleH;
        if (a.Message.Length > 0)
        {
            y += 8;
            ui.Text(a.Message, panel.X + pad, y, Theme.Caption2, t.Ink(), w - 2 * pad, TextAlignment.Center, 0);
            y += msgH - 8;
        }
        y += 16;
        ui.Layer(Ui.Overlay + 3);
        var picked = -1;
        if (stacked)
        {
            for (var k = 0; k < a.Buttons.Length; k++)
            {
                var b = a.Buttons[k];
                if (AlertButton(ui, $"alert:{k}", new R(panel.X + pad, y, w - 2 * pad, 28), b)) picked = k;
                y += 28 + 8;
            }
        }
        else
        {
            var bw = (w - 2 * pad - 8) / a.Buttons.Length;
            for (var k = 0; k < a.Buttons.Length; k++)
            {
                // (the way out on the left, the one that goes ahead on the right, as the Mac orders them)
                var b = a.Buttons[a.Buttons.Length - 1 - k];
                if (AlertButton(ui, $"alert:{k}", new R(panel.X + pad + k * (bw + 8), y, bw, 28), b)) picked = a.Buttons.Length - 1 - k;
            }
        }
        foreach (var k in ui.Keys)
        {
            if (k == Key.Escape) picked = Array.FindIndex(a.Buttons, b => b.Cancel);
            if (k == Key.Enter) picked = Array.FindIndex(a.Buttons, b => b.Prominent);
        }
        if (picked >= 0)
        {
            Current = null;
            Changed?.Invoke();
            a.Picked?.Invoke(picked);
        }
    }

    private static bool AlertButton(Ui ui, string id, R r, (string Label, bool Prominent, bool Destructive, bool Cancel) b)
    {
        var t = ui.T;
        if (b.Prominent) return ui.Button(id, r, b.Label, prominent: true);
        var i = ui.Press(id, r);
        var hover = ui.Animate(id + ":h", i.Hovered ? (i.Held ? 2 : 1) : 0, 0.16f);
        ui.Glass(r, r.H / 2, GlassKind.Panel, null, 0, Ui.Shadow.None);
        if (hover > 0.01f) ui.Fill(r, r.H / 2, t.Primary(0.05f * Math.Min(hover, 1) + 0.06f * Math.Max(0, hover - 1)));
        var m = ui.Measure(b.Label, Theme.Subheadline);
        ui.Text(b.Label, r.CenterX - m.X / 2, r.CenterY - m.Y / 2, Theme.Subheadline, b.Destructive ? t.Red : t.Ink());
        return i.Clicked;
    }
}
