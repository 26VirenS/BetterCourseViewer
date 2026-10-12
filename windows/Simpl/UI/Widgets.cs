using System.Numerics;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.UI;

/// <summary>The controls the screens are built of, each as the Mac draws it in Liquid Glass.</summary>
public sealed partial class Ui
{
    /// <summary>A button in glass: a capsule (tinted for the prominent one); it brightens under the pointer and as pressed.</summary>
    public bool Button(string id, R r, string label, bool prominent = false, string? icon = null, bool enabled = true)
    {
        var i = Press(id, r, enabled);
        var press = Animate(id + ":p", i.Held ? 1 : 0, 0.22f, 0.8f);
        var grow = press * 2.5f;
        var gr = new R(r.X - grow, r.Y - grow * 0.5f, r.W + grow * 2, r.H + grow);
        if (prominent) Glass(gr, gr.H / 2, GlassKind.Panel, T.Accent, 0.9f, 0.3f);
        else Glass(gr, gr.H / 2);
        var ink = !enabled ? T.Ink3 : prominent ? new Color4(1, 1, 1, 1) : T.Ink();
        if (icon != null)
        {
            var tw = Measure(label, Theme.Semibold(Theme.Callout)).X;
            var total = 16 + 7 + tw;
            var x0 = r.CenterX - total / 2;
            Icon(icon, x0 + 8, r.CenterY, 14, ink);
            Text(label, x0 + 23, r.CenterY - Measure(label, Theme.Semibold(Theme.Callout)).Y / 2, Theme.Semibold(Theme.Callout), ink);
        }
        else TextIn(label, r, Theme.Semibold(Theme.Callout), ink);
        var veil = Animate(id + ":h", i.Hovered && enabled ? 1 : 0, 0.18f);
        if (veil + press > 0.01f) Fill(gr, gr.H / 2, new Color4(1, 1, 1, 0.05f * veil + 0.06f * press));
        return i.Clicked;
    }

    /// <summary>An icon that is a button (inside a capsule of glass, or on a row): it lights under the pointer.</summary>
    public bool IconButton(string id, R r, string icon, bool enabled = true, float size = 16, Color4? color = null)
    {
        var i = Press(id, r, enabled);
        var h = Animate(id + ":h", i.Hovered && enabled ? (i.Held ? 2 : 1) : 0, 0.16f);
        if (h > 0.01f) Fill(r.Inset(2), (r.H - 4) / 2, new Color4(1, 1, 1, (T.Dark ? 0.08f : 0.35f) * Math.Min(h, 1) + 0.05f * Math.Max(0, h - 1)));
        Icon(icon, r.CenterX, r.CenterY, size, enabled ? color ?? T.Ink() : T.Ink3);
        return i.Clicked;
    }

    /// <summary>Icon buttons sharing one capsule of glass (Back and Forward share one, as on the Mac); the one pressed.</summary>
    public int IconCapsule(string id, float x, float y, string[] icons, bool[]? enabled = null, float h = 38)
    {
        var w = h * icons.Length + 4;
        Glass(new R(x, y, w, h), h / 2);
        var pressed = -1;
        for (var k = 0; k < icons.Length; k++)
            if (IconButton($"{id}:{k}", new R(x + 2 + k * h, y, h, h), icons[k], enabled?[k] ?? true)) pressed = k;
        return pressed;
    }

    /// <summary>
    /// Choices in a capsule, a lens of glass sliding to the one chosen (Cards · List · Activity; Dark · Light): nearly
    /// flat at rest — the words under it stay readable — bending fully while it slides, as the Mac's does.
    /// </summary>
    public bool Segmented(string id, R r, string[] items, ref int selected, bool glassCapsule = true)
    {
        if (glassCapsule) Glass(r, r.H / 2);
        var segW = (r.W - 6) / items.Length;
        var sx = S(id + ":x", selected, 0.36f, 0.74f);
        var st = S(id + ":s", 0, 0.3f, 0.7f);
        if (Math.Abs(sx.Target - selected) > 0.001f) sx.To(selected);
        var changed = false;
        for (var k = 0; k < items.Length; k++)
        {
            var seg = new R(r.X + 3 + k * segW, r.Y, segW, r.H);
            if (Press($"{id}:{k}", seg).Clicked && k != selected) { selected = k; sx.To(k); st.To(1); changed = true; }
        }
        if (st.Value > 0.95f && st.Target == 1) st.To(0);
        var s = st.Value;
        var th = r.H - 6 + 4 * s;
        var tw = segW + 14 * s;
        var tx = r.X + 3 + sx.Value * segW - 7 * s;
        var ty = r.CenterY - th / 2;
        for (var k = 0; k < items.Length; k++)
            TextIn(items[k], new R(r.X + 3 + k * segW, r.Y, segW, r.H), Theme.Semibold(Theme.Callout), T.Ink(k == selected ? 0.98f : 0.78f));
        var moving = Math.Min(1, Math.Abs(sx.Value - sx.Target) * 1.6f + s);
        var was = CurrentLayer;
        Layer(was + 5);
        Glass(new R(tx, ty, tw, th), th / 2, GlassKind.Lens, power: 0.12f + 0.88f * moving);
        Fill(new R(tx, ty, tw, th), th / 2, T.Dark ? new Color4(1, 1, 1, 0.1f) : new Color4(1, 1, 1, 0.55f));
        Layer(was);
        return changed;
    }

    /// <summary>A row of a list (the sidebar's, a screen's): lit under the pointer, filled when chosen; clicked.</summary>
    public Interaction Row(string id, R r, bool selected = false, float radius = 9)
    {
        var i = Press(id, r);
        var h = Animate(id + ":h", i.Hovered ? 1 : 0, 0.14f);
        if (selected) Fill(r, radius, T.Selected);
        else if (h > 0.01f) Fill(r, radius, Theme.WithAlpha(T.Hover, T.Hover.A * h));
        return i;
    }

    /// <summary>A small count on a row (the Inbox's unread).</summary>
    public void Badge(float right, float cy, int n, Color4? color = null)
    {
        if (n <= 0) return;
        var s = n > 99 ? "99+" : n.ToString();
        var f = Theme.Semibold(Theme.Caption2);
        var w = Math.Max(18, Measure(s, f).X + 10);
        var r = new R(right - w, cy - 9, w, 18);
        Fill(r, 9, color ?? T.Accent);
        TextIn(s, r, f, new Color4(1, 1, 1, 1));
    }

    /// <summary>A spinner: an arc going round.</summary>
    public void Spinner(float cx, float cy, float radius = 9)
    {
        Animating = true;
        var a = (float)(Time * 5.5 % (Math.PI * 2));
        Ring(cx, cy, radius, T.Separator, 2.2f);
        Arc(cx, cy, radius, a, 1.8f, T.Ink(0.75f), 2.2f);
    }

    /// <summary>A circle to tick (a piece of work's); clicked.</summary>
    public bool Check(string id, float cx, float cy, bool on, Color4 color)
    {
        var i = Press(id, new R(cx - 12, cy - 12, 24, 24));
        var k = Animate(id + ":on", on ? 1 : 0, 0.24f, 0.75f);
        Ring(cx, cy, 8.5f, Theme.WithAlpha(color, 0.85f), 1.6f);
        if (k > 0.01f)
        {
            Circle(cx, cy, 8.5f * k, color);
            Icon("checkmark", cx, cy + 0.5f, 10 * k, new Color4(1, 1, 1, k));
        }
        else if (i.Hovered) Circle(cx, cy, 5, Theme.WithAlpha(color, 0.35f));
        return i.Clicked;
    }

    // ---- a field to type in ----

    private sealed class FieldState { public double Since; }

    /// <summary>
    /// A field to type in (the toolbar's Search): a click puts the caret in it; typing, Backspace, Ctrl+Backspace,
    /// Ctrl+V, Escape (clears, then lets go). True when Enter is pressed.
    /// </summary>
    public bool TextField(string id, R r, ref string text, string placeholder, Font f, string? icon = null, bool glassBar = true)
    {
        var i = Press(id, r);
        if (Pressed && i.Hovered) Focus = id;
        else if (Pressed && Focus == id && !i.Hovered) Focus = null;
        var focused = Focus == id;
        var st = State<FieldState>(id);
        var entered = false;
        if (focused)
        {
            foreach (var c in Typed)
                if (c >= 32 && c != 127) { text += c; st.Since = Time; }
            foreach (var k in Keys)
            {
                st.Since = Time;
                switch (k)
                {
                    case Key.Back:
                        if (text.Length == 0) break;
                        if (Ctrl)
                        {
                            var t = text.TrimEnd();
                            var cut = t.LastIndexOf(' ');
                            text = cut < 0 ? "" : t[..(cut + 1)];
                        }
                        else text = text[..^1];
                        break;
                    case Key.Escape:
                        if (text.Length > 0) text = ""; else Focus = null;
                        break;
                    case Key.Enter:
                        entered = true;
                        break;
                    case Key.V when Ctrl:
                        try { if (System.Windows.Clipboard.ContainsText()) text += System.Windows.Clipboard.GetText().Replace("\r", "").Replace("\n", " "); } catch { }
                        break;
                }
            }
        }
        if (glassBar) Glass(r, r.H / 2);
        var x = r.X + 14;
        if (icon != null) { Icon(icon, r.X + 18, r.CenterY, 13, T.Ink2); x = r.X + 34; }
        var room = r.Right - 14 - x;
        var h = Measure("Ag", f).Y;
        if (text.Length == 0) Text(placeholder, x, r.CenterY - h / 2, f, T.Ink3, room);
        else
        {
            var tw = Measure(text, f).X;
            PushClip(new R(x, r.Y, room, r.H));
            var shift = Math.Max(0, tw - room + 4);
            Text(text, x - shift, r.CenterY - h / 2, f, T.Ink(), 100000);
            PopClip();
        }
        if (focused)
        {
            Animating = true;
            var on = ((Time - st.Since) % 1.06) < 0.6;
            if (on)
            {
                var tw = Math.Min(Measure(text, f).X, room - 4);
                Fill(new R(x + tw + 1, r.CenterY - h * 0.42f, 1.5f, h * 0.84f), T.Accent);
            }
            Stroke(r, r.H / 2, Theme.WithAlpha(T.Accent, 0.55f), 1.5f);
        }
        return entered;
    }

    // ---- scrolling ----

    private sealed class ScrollState { public float Target, Content, Viewport; public Spring? Offset; public double Touched = -10; }

    /// <summary>
    /// A region that scrolls (the wheel, the touchpad): its offset now. What is drawn until <see cref="EndScroll"/> is
    /// clipped to it; the caller lays it out moved up by the offset.
    /// </summary>
    public float BeginScroll(string id, R viewport)
    {
        var st = State<ScrollState>(id);
        st.Offset ??= new Spring(0, 0.16f, 1);
        wheels.Add((id, Clip is { } c ? viewport.Intersect(c) : viewport, layer * 1_000_000_000L + seq++));
        var max = Math.Max(0, st.Content - viewport.H);
        if (wheelHot == id && Wheel != 0)
        {
            st.Target = Math.Clamp(st.Target - Wheel, 0, max);
            st.Touched = Time;
        }
        st.Target = Math.Clamp(st.Target, 0, max);
        st.Viewport = viewport.H;
        st.Offset.To(st.Target);
        st.Offset.Step(Dt);
        if (!st.Offset.Settled) Animating = true;
        PushClip(viewport);
        return st.Offset.Value;
    }

    /// <summary>The scrolled region's end: how tall what it showed is (for the next frame), and its thin bar.</summary>
    public void EndScroll(string id, R viewport, float contentHeight)
    {
        PopClip();
        var st = State<ScrollState>(id);
        st.Content = contentHeight;
        if (contentHeight <= viewport.H + 1) return;
        var since = Time - st.Touched;
        var a = since < 1.2 ? 1f : (float)Math.Max(0, 1 - (since - 1.2) / 0.4);
        if (viewport.Contains(Mouse) && Mouse.X > viewport.Right - 14) a = 1;
        if (a <= 0) return;
        if (a < 1) Animating = true;
        var frac = viewport.H / contentHeight;
        var barH = Math.Max(36, viewport.H * frac - 8);
        var pos = st.Offset!.Value / Math.Max(1, contentHeight - viewport.H);
        var y = viewport.Y + 4 + (viewport.H - 8 - barH) * Math.Clamp(pos, 0, 1);
        Fill(new R(viewport.Right - 9, y, 5, barH), 2.5f, T.Dark ? new Color4(1, 1, 1, 0.32f * a) : new Color4(0, 0, 0, 0.3f * a));
    }

    /// <summary>A scrolled region's offset, set (a new screen starts at the top).</summary>
    public void ScrollTo(string id, float offset)
    {
        var st = State<ScrollState>(id);
        st.Target = offset;
        st.Offset?.Snap(offset);
    }
}
