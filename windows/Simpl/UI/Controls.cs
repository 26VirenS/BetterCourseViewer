using System.Numerics;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.UI;

/// <summary>The Mac's form controls (Settings) and its menus, drawn by the renderer.</summary>
public sealed partial class Ui
{
    // ---- switches, boxes, steppers, sliders ----

    /// <summary>A switch (the Mac's Toggle, 36 × 20): the accent when on; clicked, it turns.</summary>
    public bool Toggle(string id, float x, float cy, ref bool on, bool enabled = true)
    {
        const float w = 36, h = 20;
        var r = new R(x, cy - h / 2, w, h);
        var i = Press(id, r, enabled);
        if (i.Clicked) on = !on;
        var k = Animate(id + ":k", on ? 1 : 0, 0.22f, 0.9f);
        var off = T.Dark ? new Color4(1, 1, 1, 0.22f) : new Color4(0, 0, 0, 0.18f);
        var track = Theme.Mix(off, T.Accent, k);
        if (!enabled) track = Theme.WithAlpha(track, 0.5f);
        Fill(r, h / 2, track);
        var kx = r.X + 2 + (w - h) * k + (h - 4) / 2;
        Circle(kx, cy + 1, (h - 4) / 2, new Color4(0, 0, 0, T.Dark ? 0.25f : 0.14f));
        Circle(kx, cy, (h - 4) / 2, new Color4(1, 1, 1, enabled ? 1 : 0.8f));
        return i.Clicked;
    }

    /// <summary>A check box (the Mac's .checkbox toggle, 16): the accent with a tick when on; its words beside it; clicked, it turns.</summary>
    public bool Checkbox(string id, float x, float cy, ref bool on, string label, Font? font = null, bool enabled = true)
    {
        var f = font ?? Theme.Body;
        var m = Measure(label, f);
        var r = new R(x, cy - 10, 16 + 8 + m.X, 20);
        var i = Press(id, r, enabled);
        if (i.Clicked) on = !on;
        var box = new R(x, cy - 8, 16, 16);
        if (on)
        {
            Fill(box, 4, enabled ? T.Accent : Theme.WithAlpha(T.Accent, 0.5f));
            Icon("checkmark", box.CenterX, box.CenterY + 0.5f, 10, new Color4(1, 1, 1, 1), FontWeight.Bold);
        }
        else
        {
            Fill(box, 4, T.Dark ? new Color4(1, 1, 1, 0.12f) : new Color4(1, 1, 1, 1));
            Stroke(box, 4, T.Dark ? new Color4(1, 1, 1, 0.2f) : new Color4(0, 0, 0, 0.2f));
        }
        Text(label, x + 24, cy - m.Y / 2, f, enabled ? T.Ink() : T.Ink3);
        return i.Clicked;
    }

    /// <summary>A stepper (the Mac's, 20 × 26: up over down); +1, −1, or 0.</summary>
    public int Stepper(string id, float x, float cy, bool enabled = true)
    {
        var r = new R(x, cy - 13, 20, 26);
        Fill(r, 6, T.Dark ? new Color4(1, 1, 1, 0.12f) : new Color4(1, 1, 1, 1));
        Stroke(r, 6, T.Dark ? new Color4(1, 1, 1, 0.14f) : new Color4(0, 0, 0, 0.14f));
        Fill(new R(x + 3, cy - 0.5f, 14, 1), T.Primary(0.12f));
        var delta = 0;
        for (var k = 0; k < 2; k++)
        {
            var half = new R(x, cy - 13 + k * 13, 20, 13);
            var i = Press($"{id}:{k}", half, enabled);
            if (i.Held) Fill(half, 5, T.Primary(0.08f));
            Icon(k == 0 ? "chevron.up" : "chevron.down", half.CenterX, half.CenterY + (k == 0 ? 0.5f : -0.5f), 8, enabled ? T.Ink() : T.Ink3, FontWeight.Bold);
            if (i.Clicked) delta = k == 0 ? 1 : -1;
        }
        return delta;
    }

    private sealed class SliderState { public bool Dragging; }

    /// <summary>A slider (the Mac's): a track, the accent up to the knob; dragged. True while the value changes.</summary>
    public bool Slider(string id, float x, float cy, float w, ref float value, float min = 0, float max = 1)
    {
        var r = new R(x, cy - 10, w, 20);
        var i = Press(id, r);
        var st = State<SliderState>(id);
        if (i.Held && Pressed) st.Dragging = true;
        if (!Down) st.Dragging = false;
        var changed = false;
        if (st.Dragging && Down)
        {
            var v = min + (max - min) * Math.Clamp((Mouse.X - x - 10) / Math.Max(1, w - 20), 0, 1);
            if (Math.Abs(v - value) > 0.0001f) { value = v; changed = true; }
            Animating = true;
        }
        var t = (value - min) / Math.Max(0.0001f, max - min);
        var kx = x + 10 + (w - 20) * Math.Clamp(t, 0, 1);
        Fill(new R(x, cy - 2, w, 4), 2, T.Primary(0.14f));
        Fill(new R(x, cy - 2, Math.Max(4, kx - x), 4), 2, T.Accent);
        Circle(kx, cy + 1, 10, new Color4(0, 0, 0, T.Dark ? 0.3f : 0.12f));
        Circle(kx, cy, 10, T.Dark ? Theme.Rgb(220, 220, 224) : new Color4(1, 1, 1, 1));
        Ring(kx, cy, 10, new Color4(0, 0, 0, 0.06f), 1);
        return changed;
    }

    /// <summary>A button in a form (the Mac's bordered button, macOS 26: a tinted capsule, its words in the accent — red when destructive); clicked.</summary>
    public bool FormButton(string id, float x, float cy, string label, bool enabled = true, bool destructive = false, bool prominent = false, float h = 26)
    {
        var f = Theme.Body;
        var m = Measure(label, f);
        var r = new R(x, cy - h / 2, m.X + 24, h);
        var i = Press(id, r, enabled);
        var hover = Animate(id + ":h", i.Hovered && enabled ? (i.Held ? 2 : 1) : 0, 0.16f);
        var tone = destructive ? T.Red : T.Accent;
        if (prominent)
        {
            Fill(r, h / 2, enabled ? tone : Theme.WithAlpha(tone, 0.5f));
            if (hover > 0.01f) Fill(r, h / 2, new Color4(1, 1, 1, 0.1f * Math.Min(hover, 1) + 0.08f * Math.Max(0, hover - 1)));
            Text(label, r.X + 12, cy - m.Y / 2, f, new Color4(1, 1, 1, enabled ? 1 : 0.8f));
        }
        else
        {
            Fill(r, h / 2, Theme.WithAlpha(tone, (T.Dark ? 0.2f : 0.12f) + 0.05f * Math.Min(hover, 1) + 0.05f * Math.Max(0, hover - 1)));
            Text(label, r.X + 12, cy - m.Y / 2, f, enabled ? tone : Theme.WithAlpha(tone, 0.45f));
        }
        return i.Clicked;
    }

    public float FormButtonWidth(string label) => Measure(label, Theme.Body).X + 24;

    /// <summary>A pop-up button (the Mac's menu picker: the choice, a chevron in a small circle of the accent); clicked.</summary>
    public bool PopupButton(string id, float right, float cy, string choice, float h = 26)
    {
        var f = Theme.Body;
        var m = Measure(choice, f);
        var r = new R(right - (m.X + 12 + 30), cy - h / 2, m.X + 12 + 30, h);
        var i = Press(id, r);
        var hover = Animate(id + ":h", i.Hovered ? 1 : 0, 0.16f);
        Text(choice, r.X, cy - m.Y / 2, f, T.Accent);
        Circle(right - 13, cy, 13, Theme.WithAlpha(T.Accent, (T.Dark ? 0.22f : 0.14f) + 0.06f * hover));
        Icon("chevron.down", right - 13, cy + 0.5f, 9, T.Accent, FontWeight.Bold);
        return i.Clicked;
    }

    /// <summary>A disclosure's chevron and its words (the Mac's DisclosureGroup); clicked, it opens or folds.</summary>
    public bool Disclosure(string id, float x, float cy, string label, ref bool open)
    {
        var f = Theme.Body;
        var m = Measure(label, f);
        var r = new R(x, cy - 12, 20 + m.X, 24);
        var i = Press(id, r);
        if (i.Clicked) open = !open;
        var turn = Animate(id + ":t", open ? 1 : 0, 0.2f);
        Icon(turn > 0.5f ? "chevron.down" : "chevron.right", x + 7, cy, 10, T.Ink2, FontWeight.Bold);
        Text(label, x + 20, cy - m.Y / 2, f, T.Ink());
        return i.Clicked;
    }

    // ---- menus ----

    public sealed class MenuState
    {
        public bool Open;
        public double ClosedAt = -10;
    }

    /// <summary>A menu opened (unless it was just put away by the press that opens it).</summary>
    public void OpenMenu(string id)
    {
        var st = State<MenuState>(id);
        if (Time - st.ClosedAt < 0.3) return;
        st.Open = true;
    }

    public bool MenuOpen(string id) => State<MenuState>(id).Open;

    /// <summary>
    /// A menu (the Mac's) popped up beside an anchor — below it, or above when there is more room there — its items
    /// (null: a divider) on a panel of glass, the one under the pointer lit in the accent; the index chosen, or −1. A
    /// press outside, or Escape, puts it away.
    /// </summary>
    public int Menu(string id, R anchor, string?[] items, bool alignRight = false)
    {
        var st = State<MenuState>(id);
        if (!st.Open) return -1;
        const float rowH = 24, divH = 9, pad = 5;
        var f = Theme.Body;
        var w = 150f;
        foreach (var s in items) if (s != null) w = Math.Max(w, Measure(s, f).X + 36);
        var h = pad * 2;
        foreach (var s in items) h += s == null ? divH : rowH;
        var below = anchor.Bottom + 4 + h <= H - 8;
        var y = below ? anchor.Bottom + 4 : anchor.Y - 4 - h;
        var x = alignRight ? anchor.Right - w : anchor.X;
        x = Math.Clamp(x, 8, Math.Max(8, W - w - 8));
        var panel = new R(x, y, w, h);
        var was = CurrentLayer;
        Layer(PopoverOver);
        var grow = Animate(id + ":in", 1, 0.2f, 1);
        Glass(panel, 10, GlassKind.Panel, null, 0, Shadow.Panel);
        Fill(panel, 10, Theme.WithAlpha(T.Card, T.Dark ? 0.6f : 0.75f));
        Stroke(panel, 10, T.Edge);
        var chosen = -1;
        var ry = y + pad;
        for (var k = 0; k < items.Length; k++)
        {
            var s = items[k];
            if (s == null)
            {
                Fill(new R(x + 10, ry + divH / 2, w - 20, 1), T.Separator);
                ry += divH;
                continue;
            }
            var row = new R(x + pad, ry, w - 2 * pad, rowH);
            var i = Press($"{id}:{k}", row);
            if (i.Hovered) Fill(row, 6, T.Accent);
            Text(s, row.X + 10, row.CenterY - Measure(s, f).Y / 2, f, i.Hovered ? new Color4(1, 1, 1, 1) : T.Ink());
            if (i.Clicked) chosen = k;
            ry += rowH;
        }
        Layer(was);
        // put away: a choice, a press elsewhere, Escape
        if (chosen >= 0 || (Pressed && !panel.Contains(Mouse)) || Keys.Contains(Key.Escape))
        {
            st.Open = false;
            st.ClosedAt = Time;
            springs.Remove(id + ":in");
        }
        return chosen;
    }
}
