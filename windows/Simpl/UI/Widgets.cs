using System.Numerics;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.UI;

/// <summary>The parts the screens are built of, each as the Mac draws it (mac/Simpl/Components) in Liquid Glass.</summary>
public sealed partial class Ui
{
    // ---- the toolbar's items (the Mac's: 32 tall capsules and circles of glass in the title bar's band) ----

    public const float ToolbarItem = 32;

    /// <summary>A round toolbar button of glass with a symbol in it (Refresh, +, Search); clicked.</summary>
    public bool ToolbarButton(string id, float x, float y, string icon, bool enabled = true, float size = 13, float width = ToolbarItem)
    {
        var r = new R(x, y, width, ToolbarItem);
        Glass(r, ToolbarItem / 2, GlassKind.Panel, null, 0, Shadow.None);
        var i = Press(id, r, enabled);
        var h = Animate(id + ":h", i.Hovered && enabled ? (i.Held ? 2 : 1) : 0, 0.16f);
        if (h > 0.01f) Fill(r, r.H / 2, T.Primary(0.06f * Math.Min(h, 1) + 0.05f * Math.Max(0, h - 1)));
        Icon(icon, r.CenterX, r.CenterY, size, enabled ? T.Ink() : T.Ink3, FontWeight.SemiBold);
        return i.Clicked;
    }

    /// <summary>Back and Forward in one capsule of glass, a hairline between them (the Mac's navigation control group); 0 or 1 clicked, else -1.</summary>
    public int BackForward(string id, float x, float y, bool canBack, bool canForward)
    {
        const float half = 35;
        var r = new R(x, y, half * 2, ToolbarItem);
        Glass(r, ToolbarItem / 2, GlassKind.Panel, null, 0, Shadow.None);
        var clicked = -1;
        for (var k = 0; k < 2; k++)
        {
            var seg = new R(x + k * half, y, half, ToolbarItem);
            var on = k == 0 ? canBack : canForward;
            var i = Press($"{id}:{k}", seg, on);
            var h = Animate($"{id}:{k}:h", i.Hovered && on ? (i.Held ? 2 : 1) : 0, 0.16f);
            if (h > 0.01f) Fill(seg.Inset(2), (seg.H - 4) / 2, T.Primary(0.06f * Math.Min(h, 1) + 0.05f * Math.Max(0, h - 1)));
            Icon(k == 0 ? "chevron.left" : "chevron.right", seg.CenterX + (k == 0 ? -1 : 1), seg.CenterY, 13, on ? T.Ink() : T.Ink3, FontWeight.SemiBold);
            if (i.Clicked) clicked = k;
        }
        Fill(new R(x + half - 0.5f, y + 9, 1, ToolbarItem - 18), T.Primary(0.12f));
        return clicked;
    }

    /// <summary>
    /// Choices in a capsule of glass (the Mac's segmented picker in the toolbar: Cards · List · Activity): the one
    /// chosen on a lighter pill that slides — a lens of glass while it moves, as the Mac's does — the words 14 medium.
    /// </summary>
    public bool Segmented(string id, R r, string[] items, ref int selected, bool glassCapsule = true, float? segWidth = null)
    {
        if (glassCapsule) Glass(r, r.H / 2, GlassKind.Panel, null, 0, Shadow.None);
        var segW = segWidth ?? (r.W - 6) / items.Length;
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
        var moving = Math.Min(1, Math.Abs(sx.Value - sx.Target) * 1.6f + s);
        var th = r.H - 6 + 4 * s;
        var tw = segW + 10 * s;
        var tx = r.X + 3 + sx.Value * segW - 5 * s;
        var ty = r.CenterY - th / 2;
        var thumb = new R(tx, ty, tw, th);
        // the pill: a solid wash at rest (its own soft shadow), bending as a lens while it slides
        var was = CurrentLayer;
        if (moving > 0.02f)
        {
            Layer(was + 5);
            Glass(thumb, th / 2, GlassKind.Lens, power: 0.12f + 0.88f * moving);
            Layer(was);
        }
        for (var i = 1; i <= 2; i++) Fill(thumb.Offset(0, i * 0.8f), th / 2, new Color4(0, 0, 0, (T.Dark ? 0.12f : 0.05f) * (1 - moving)));
        Fill(thumb, th / 2, T.Dark ? new Color4(1, 1, 1, 0.14f * (1 - 0.6f * moving)) : new Color4(0, 0, 0, 0.08f * (1 - 0.6f * moving)));
        for (var k = 0; k < items.Length; k++)
            TextIn(items[k], new R(r.X + 3 + k * segW, r.Y, segW, r.H), Theme.Medium(Theme.Callout), T.Ink(k == selected ? 1 : 0.85f));
        return changed;
    }

    // ---- buttons ----

    /// <summary>A button of glass (the Mac's .glass button style: a capsule, its words 14 with a symbol before them); prominent in the accent.</summary>
    public bool GlassButton(string id, float x, float y, string label, string? icon = null, bool prominent = false, bool enabled = true, float h = 28, Color4? tint = null)
    {
        var f = Theme.Callout;
        var tw = Measure(label, f).X;
        var w = tw + 24 + (icon != null ? 18 : 0);
        var r = new R(x, y, w, h);
        var i = Press(id, r, enabled);
        var press = Animate(id + ":p", i.Held ? 1 : 0, 0.22f, 0.8f);
        if (prominent) Glass(r, h / 2, GlassKind.Panel, tint ?? T.Accent, 0.9f, Shadow.None);
        else Glass(r, h / 2, GlassKind.Panel, tint, tint == null ? 0 : 0.1f, Shadow.None);
        var ink = !enabled ? T.Ink3 : prominent ? new Color4(1, 1, 1, 1) : T.Ink();
        var cx = r.X + 12;
        if (icon != null) { Icon(icon, cx + 6, r.CenterY, 12, ink, FontWeight.SemiBold); cx += 18; }
        Text(label, cx, r.CenterY - Measure(label, f).Y / 2, f, ink);
        var veil = Animate(id + ":h", i.Hovered && enabled ? 1 : 0, 0.18f);
        if (veil + press > 0.01f) Fill(r, h / 2, T.Primary(0.05f * veil + 0.06f * press));
        return i.Clicked;
    }

    /// <summary>The width a glass button takes (to lay one out from the right).</summary>
    public float GlassButtonWidth(string label, bool icon) => Measure(label, Theme.Callout).X + 24 + (icon ? 18 : 0);

    /// <summary>
    /// A button at a given size (the Mac's bordered button at the large control size, or prominent in the accent): a
    /// capsule, its words 15 (14 when it is small), a symbol before them; clicked.
    /// </summary>
    public bool Button(string id, R r, string label, bool prominent = false, bool enabled = true, string? icon = null)
    {
        var f = r.H >= 30 ? Theme.Subheadline : Theme.Callout; // (the Mac's large buttons: 13)
        var i = Press(id, r, enabled);
        var press = Animate(id + ":p", i.Held ? 1 : 0, 0.22f, 0.8f);
        if (prominent && enabled)
        {
            // (the Mac's prominent button: the accent, solid, a thread of light along its top)
            Fill(r, r.H / 2, T.Accent);
            Stroke(r, r.H / 2, new Color4(1, 1, 1, T.Dark ? 0.18f : 0.28f), 1);
        }
        else if (prominent) Fill(r, r.H / 2, T.Primary(T.Dark ? 0.1f : 0.07f)); // (one that cannot be pressed: a gray capsule, as the Mac's)
        else Glass(r, r.H / 2, GlassKind.Panel, null, 0, Shadow.None);
        var ink = !enabled ? T.Ink2 : prominent ? new Color4(1, 1, 1, 1) : T.Ink();
        var m = Measure(label, f);
        var iw = icon != null ? 20 : 0;
        var cx = r.CenterX - (m.X + iw) / 2;
        if (icon != null) { Icon(icon, cx + 7, r.CenterY, f.Size * 0.9f, ink, FontWeight.SemiBold); cx += iw; }
        Text(label, cx, r.CenterY - m.Y / 2, f, ink);
        var veil = Animate(id + ":h", i.Hovered && enabled ? 1 : 0, 0.18f);
        if (veil + press > 0.01f) Fill(r, r.H / 2, T.Primary(0.05f * veil + 0.06f * press));
        return i.Clicked;
    }

    /// <summary>The width <see cref="Button"/> wants for its words (and a symbol before them).</summary>
    public float ButtonWidth(string label, bool icon = false, float h = 30) => Measure(label, h >= 30 ? Theme.Subheadline : Theme.Callout).X + 32 + (icon ? 20 : 0);

    /// <summary>A button that is a link in the accent (the Mac's .link style: "All Courses"); clicked.</summary>
    public bool LinkButton(string id, float x, float y, string label, Font? font = null)
    {
        var f = font ?? Theme.Callout;
        var m = Measure(label, f);
        var r = new R(x - 4, y - 2, m.X + 8, m.Y + 4);
        var i = Press(id, r);
        Text(label, x, y, f, Theme.WithAlpha(T.Accent, i.Held ? 0.6f : i.Hovered ? 0.85f : 1));
        return i.Clicked;
    }

    /// <summary>A small round icon button (the Mac's DashIconButtonStyle: 30 round, a wash under the pointer); clicked.</summary>
    public bool RoundIconButton(string id, float cx, float cy, string icon, float size = 13.5f, Color4? color = null, FontWeight weight = FontWeight.Medium)
    {
        var r = new R(cx - 15, cy - 15, 30, 30);
        var i = Press(id, r);
        var h = Animate(id + ":h", i.Held ? 2 : i.Hovered ? 1 : 0, 0.16f);
        if (h > 0.01f) Circle(cx, cy, 15, T.Primary(0.08f * Math.Min(h, 1) + 0.06f * Math.Max(0, h - 1)));
        Icon(icon, cx, cy, size, color ?? T.Ink2, weight);
        return i.Clicked;
    }

    /// <summary>An icon that is a button in a row (the sidebar's chevron); it lights under the pointer.</summary>
    public bool IconButton(string id, R r, string icon, bool enabled = true, float size = 16, Color4? color = null, FontWeight weight = FontWeight.Regular)
    {
        var i = Press(id, r, enabled);
        var h = Animate(id + ":h", i.Hovered && enabled ? (i.Held ? 2 : 1) : 0, 0.16f);
        if (h > 0.01f) Fill(r.Inset(2), (r.H - 4) / 2, T.Primary(0.07f * Math.Min(h, 1) + 0.05f * Math.Max(0, h - 1)));
        Icon(icon, r.CenterX, r.CenterY, size, enabled ? color ?? T.Ink() : T.Ink3, weight);
        return i.Clicked;
    }

    // ---- rows ----

    /// <summary>A row's wash (the Mac's RowButtonStyle): primary 5% under the pointer, 9% pressed, radius 10; clicked.</summary>
    public Interaction RowWash(string id, R r, float radius = 10)
    {
        var i = Press(id, r);
        var h = Animate(id + ":h", i.Held ? 2 : i.Hovered ? 1 : 0, 0.24f, 1);
        if (h > 0.01f) Fill(r, radius, T.Primary(0.05f * Math.Min(h, 1) + 0.04f * Math.Max(0, h - 1)));
        return i;
    }

    /// <summary>The sidebar's row: filled with the accent when chosen (the Mac's, its window active: its words and symbol then white), a wash under the pointer; clicked.</summary>
    public Interaction SideRowWash(string id, R r, bool selected)
    {
        var i = Press(id, r);
        var h = Animate(id + ":h", !selected && i.Hovered ? 1 : 0, 0.14f);
        if (selected) Fill(r, 8, T.Accent);
        else if (h > 0.01f) Fill(r, 8, T.Primary(0.06f * h));
        return i;
    }

    /// <summary>The hairline between two rows of a card (the Mac's RowDivider): from where the rows' words start to 8 before the card's inner edge.</summary>
    public void RowDivider(float x0, float x1, float y) => Fill(new R(x0, y, Math.Max(0, x1 - x0), 1), T.Separator);

    // ---- chips, badges, ticks, tiles ----

    /// <summary>A status in words on a wash of its colour (the Mac's StatusChip): 12 semibold, padding 7 × 2.5; returns its width.</summary>
    public float Chip(string text, float x, float cy, Color4 color)
    {
        var f = Theme.Semibold(Theme.Caption);
        var m = Measure(text, f);
        var w = m.X + 14;
        var h = m.Y + 5;
        var r = new R(x, cy - h / 2, w, h);
        Fill(r, h / 2, Theme.WithAlpha(color, 0.14f));
        Text(text, x + 7, cy - m.Y / 2, f, color);
        return w;
    }

    public float ChipWidth(string text) => Measure(text, Theme.Semibold(Theme.Caption)).X + 14;

    /// <summary>A course's chip: its short name on a wash of its colour.</summary>
    public float CourseChip(string text, float x, float cy, string? colorHex) => Chip(text, x, cy, Theme.Hex(colorHex));

    /// <summary>A count beside a row's words (the sidebar's unread), 11 semibold secondary; returns its width.</summary>
    public float CountLabel(int n, float right, float cy)
    {
        var f = Theme.Semibold(Theme.Caption2);
        var s = n.ToString();
        var m = Measure(s, f);
        Text(s, right - m.X, cy - m.Y / 2, f, T.Ink2);
        return m.X;
    }

    /// <summary>The round tick of a piece of work (the Mac's CheckCircle, 22): a ring, filled with the colour and ticked when done; clicked.</summary>
    public bool CheckCircle(string id, float cx, float cy, bool done, Color4? color = null)
    {
        var c = color ?? T.Green;
        var i = Press(id, new R(cx - 13, cy - 13, 26, 26));
        var k = Animate(id + ":on", done ? 1 : 0, 0.3f, 0.86f);
        var hover = Animate(id + ":h", i.Hovered ? 1 : 0, 0.24f, 1);
        if (k < 0.99f)
        {
            if (hover > 0.01f) Circle(cx, cy, 10.25f, Theme.WithAlpha(c, 0.12f * hover * (1 - k)));
            Ring(cx, cy, 10.25f, Theme.WithAlpha(T.Dark ? new Color4(1, 1, 1, 1) : new Color4(0, 0, 0, 1), (0.25f + 0.2f * hover) * (1 - k)), 1.5f);
        }
        if (k > 0.01f)
        {
            Circle(cx, cy, 11 * Math.Min(1, 0.4f + 0.6f * k), Theme.WithAlpha(c, k));
            Icon("checkmark", cx, cy + 0.5f, 11 * Math.Min(1, 0.4f + 0.6f * k), new Color4(1, 1, 1, k), FontWeight.Bold);
        }
        return i.Clicked;
    }

    /// <summary>An icon on a rounded wash of a colour (the Mac's IconTile): radius 0.27 × size, the wash 16%, the symbol 0.46 × size semibold.</summary>
    public void IconTile(string symbol, float x, float y, Color4 color, float size = 30)
    {
        Fill(new R(x, y, size, size), size * 0.27f, Theme.WithAlpha(color, 0.16f));
        Icon(symbol, x + size / 2, y + size / 2, size * 0.46f, color, FontWeight.SemiBold);
    }

    /// <summary>The student's initials on the accent (the Mac's Avatar), round.</summary>
    public void Avatar(string? initials, float cx, float cy, float size = 30)
    {
        Circle(cx, cy, size / 2, T.Accent);
        Circle(cx, cy - size * 0.12f, size * 0.42f, Theme.WithAlpha(new Color4(1, 1, 1, 1), 0.08f)); // (the accent's gradient: lighter above)
        TextIn(string.IsNullOrEmpty(initials) ? "·" : initials!, new R(cx - size / 2, cy - size / 2, size, size), new Font(Theme.TextFace, size * 0.4f, FontWeight.SemiBold), new Color4(1, 1, 1, 1));
    }

    /// <summary>A person's initials on grey (the Mac's PersonAvatar), round.</summary>
    public void PersonAvatar(string name, float cx, float cy, float size = 32)
    {
        Circle(cx, cy, size / 2, Theme.WithAlpha(T.Gray, 0.55f));
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])).ToArray();
        if (parts.Length == 0) Icon("person", cx, cy, size * 0.5f, new Color4(1, 1, 1, 1));
        else TextIn(new string(parts), new R(cx - size / 2, cy - size / 2, size, size), new Font(Theme.TextFace, size * 0.38f, FontWeight.SemiBold), new Color4(1, 1, 1, 1));
    }

    /// <summary>A count in a filled capsule (a badge on a hero).</summary>
    public float Pill(string text, float x, float cy, Color4 fill, Color4 ink, string? icon = null)
    {
        var f = Theme.Bold(Theme.Caption);
        var m = Measure(text, f);
        var w = m.X + 16 + (icon != null ? 16 : 0);
        var r = new R(x, cy - (m.Y + 6) / 2, w, m.Y + 6);
        Fill(r, r.H / 2, fill);
        var tx = x + 8;
        if (icon != null) { Icon(icon, tx + 6, cy, 11, ink, FontWeight.Bold); tx += 16; }
        Text(text, tx, cy - m.Y / 2, f, ink);
        return w;
    }

    // ---- rings, bars ----

    /// <summary>A grade ring (the Mac's Ring): the track at 18% and the arc to the score in the colour, round-capped, filling on a soft spring the first time.</summary>
    public void GradeRing(string id, float cx, float cy, float size, double? value, Color4 color, float lineWidth = 6)
    {
        var radius = size / 2 - lineWidth / 2;
        Ring(cx, cy, radius, Theme.WithAlpha(color, 0.18f), lineWidth);
        var target = (float)Math.Clamp((value ?? 0) / 100, 0, 1);
        var shown = Animate(id + ":fill", target, 0.8f, 0.9f);
        if (shown > 0.002f) Arc(cx, cy, radius, 0, shown * MathF.PI * 2, color, lineWidth);
    }

    /// <summary>A share as a bar in a colour (the Mac's DashBar): a capsule track at 16%, filling on the soft spring.</summary>
    public void Bar(string id, float x, float y, float w, double fraction, Color4 color, float h = 7)
    {
        Fill(new R(x, y, w, h), h / 2, Theme.WithAlpha(color, 0.16f));
        var shown = Animate(id + ":fill", (float)Math.Clamp(fraction, 0, 1), 0.8f, 0.9f);
        if (shown > 0.001f) Fill(new R(x, y, Math.Max(w * shown, h), h), h / 2, color);
    }

    /// <summary>A spinner: an arc going round.</summary>
    public void Spinner(float cx, float cy, float radius = 9)
    {
        Animating = true;
        var a = (float)(Time * 5.5 % (Math.PI * 2));
        Ring(cx, cy, radius, T.Primary(0.12f), 2.2f);
        Arc(cx, cy, radius, a, 1.8f, T.Ink2, 2.2f);
    }

    // ---- a page's parts ----

    /// <summary>A section's heading on the page (the Mac's PageSection): the title 19 semibold, what is said at its end 14 secondary; returns the heading's height.</summary>
    public float SectionHeading(string title, float x, float y, float w, string? trailing = null, float accessoryWidth = 0)
    {
        var th = Measure(title, Theme.Title3).Y;
        Text(title, x, y, Theme.Title3, T.Ink(), w * 0.6f);
        if (!string.IsNullOrEmpty(trailing))
        {
            var m = Measure(trailing!, Theme.Callout);
            Text(trailing!, x + w - accessoryWidth - (accessoryWidth > 0 ? 10 : 0) - m.X, y + th - m.Y - 1, Theme.Callout, T.Ink2);
        }
        return th;
    }

    /// <summary>A short line where a list is empty (the Mac's EmptyNote): a symbol and the words, 14; returns its height.</summary>
    public float EmptyNote(string text, float x, float y, float w, string symbol = "checkmark.circle")
    {
        Icon(symbol, x + 8, y + 12 + 9, 14, T.Ink3);
        Text(text, x + 24, y + 12, Theme.Callout, T.Ink2, w - 24);
        return 42;
    }

    // ---- a field to type in ----

    private sealed class FieldState { public double Since; }

    /// <summary>
    /// A field to type in (the toolbar's Search): a click puts the caret in it; typing, Backspace, Ctrl+Backspace,
    /// Ctrl+V, Escape (clears, then lets go). True when Enter is pressed. <paramref name="secret"/> shows a dot for each
    /// character (a password); <paramref name="boxed"/> draws the Mac's rounded-border field (a well with a hairline
    /// edge, the accent's ring when focused) in place of a glass bar.
    /// </summary>
    public bool TextField(string id, R r, ref string text, string placeholder, Font f, string? icon = null, bool glassBar = true, bool secret = false, bool boxed = false)
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
        if (boxed)
        {
            // (the Mac's rounded-border field as macOS 26 draws it at the large size: a gray well, 22 tall, its corners 5)
            const float radius = 5;
            Fill(r, radius, T.Primary(T.Dark ? 0.1f : 0.07f));
            if (focused) Stroke(r.Inset(-1.5f), radius + 1.5f, Theme.WithAlpha(T.Accent, 0.55f), 3f);
        }
        else if (glassBar) Glass(r, r.H / 2, GlassKind.Panel, null, 0, Shadow.None);
        var shown = secret ? new string('•', text.Length) : text;
        var x = r.X + (boxed ? 7 : 12);
        if (icon != null) { Icon(icon, r.X + 17, r.CenterY, 12.5f, T.Ink2, FontWeight.SemiBold); x = r.X + 30; }
        var room = r.Right - (boxed ? 7 : 12) - x;
        var h = Measure("Ag", f).Y;
        if (shown.Length == 0) Text(placeholder, x, r.CenterY - h / 2, f, boxed ? T.Ink2 : T.Ink3, room);
        else
        {
            var tw = Measure(shown, f).X;
            PushClip(new R(x, r.Y, room, r.H));
            var shift = Math.Max(0, tw - room + 4);
            Text(shown, x - shift, r.CenterY - h / 2, f, T.Ink(), 100000);
            PopClip();
        }
        if (focused)
        {
            Animating = true;
            var on = ((Time - st.Since) % 1.06) < 0.6;
            if (on)
            {
                var tw = Math.Min(Measure(shown, f).X, room - 4);
                Fill(new R(x + tw + 1, r.CenterY - h * 0.42f, 1.5f, h * 0.84f), T.Accent);
            }
            if (!boxed) Stroke(r, r.H / 2, Theme.WithAlpha(T.Accent, 0.6f), 2f);
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

    /// <summary>The scrolled region's end: how tall what it showed is (for the next frame), and its thin bar (the Mac's overlay scroller).</summary>
    public void EndScroll(string id, R viewport, float contentHeight)
    {
        PopClip();
        var st = State<ScrollState>(id);
        st.Content = contentHeight;
        if (contentHeight <= viewport.H + 1) return;
        var since = Time - st.Touched;
        var a = since < 1.0 ? 1f : (float)Math.Max(0, 1 - (since - 1.0) / 0.5);
        if (viewport.Contains(Mouse) && Mouse.X > viewport.Right - 14) a = 1;
        if (a <= 0) return;
        if (a < 1) Animating = true;
        var frac = viewport.H / contentHeight;
        var barH = Math.Max(36, viewport.H * frac - 8);
        var pos = st.Offset!.Value / Math.Max(1, contentHeight - viewport.H);
        var y = viewport.Y + 4 + (viewport.H - 8 - barH) * Math.Clamp(pos, 0, 1);
        Fill(new R(viewport.Right - 9, y, 6, barH), 3, T.Dark ? new Color4(1, 1, 1, 0.35f * a) : new Color4(0, 0, 0, 0.32f * a));
    }

    /// <summary>A scrolled region's offset, set (a new screen starts at the top).</summary>
    public void ScrollTo(string id, float offset)
    {
        var st = State<ScrollState>(id);
        st.Target = offset;
        st.Offset?.Snap(offset);
    }

    /// <summary>A scrolled region's offset now.</summary>
    public float ScrollOffset(string id) => State<ScrollState>(id).Offset?.Value ?? 0;
}
