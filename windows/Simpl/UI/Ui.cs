using System.Numerics;
using Simpl.Graphics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.UI;

/// <summary>A rectangle in DIPs.</summary>
public readonly record struct R(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public float CenterX => X + W / 2;
    public float CenterY => Y + H / 2;
    public bool Contains(Vector2 p) => p.X >= X && p.X < X + W && p.Y >= Y && p.Y < Y + H;
    public R Inset(float dx, float dy) => new(X + dx, Y + dy, Math.Max(0, W - 2 * dx), Math.Max(0, H - 2 * dy));
    public R Inset(float d) => Inset(d, d);
    public R Offset(float dx, float dy) => new(X + dx, Y + dy, W, H);
    public R Intersect(R o)
    {
        var x = Math.Max(X, o.X);
        var y = Math.Max(Y, o.Y);
        return new R(x, y, Math.Max(0, Math.Min(Right, o.Right) - x), Math.Max(0, Math.Min(Bottom, o.Bottom) - y));
    }
    public RawRectF Raw => new(X, Y, X + W, Y + H);
}

/// <summary>What glass a shape is: a panel or a card (frosted), a bar (a frosted capsule), or a lens (no frost).</summary>
public enum GlassKind { Panel, Lens, Bubble, Clear }

/// <summary>Keys the screens ask about (from WM_KEYDOWN).</summary>
public enum Key { None, Back, Delete, Enter, Escape, Left, Right, Up, Down, Home, End, Tab, A, C, V, X, F, R, N, W, Comma }

/// <summary>Where the pointer is with a control: over it, held on it, and clicked (pressed and let go on it).</summary>
public readonly record struct Interaction(bool Hovered, bool Held, bool Clicked, bool RightClicked);

/// <summary>
/// The look chosen in the showcase's Tune (windows/GlassNative), Simpl's glass on Windows: a frost of 13 pt, refraction
/// 30 pt over an edge of 13 pt, dispersion 0.03, colour 1.8, tint 0.38, rim 0.53; cards are soft squircles (a
/// superellipse of degree 3 over 1.5 × the radius), bars and every other capsule round.
/// </summary>
public static class GlassStyle
{
    public const float FrostSigma = 13, Pull = 30, Band = 13, Dispersion = 0.03f, Saturation = 1.8f, Lift = 0.38f, Rim = 0.53f;
    public const float SquircleDegree = 3, SquircleScale = 1.5f;

    /// <summary>A shape's corners: a capsule (its radius reaching half its height) stays round; a card's are a soft squircle's.</summary>
    public static float Corners(float hw, float hh, ref float radius)
    {
        var minHalf = Math.Min(hw, hh);
        if (radius >= minHalf - 0.5f) { radius = Math.Min(radius, minHalf); return 0; }
        radius = Math.Min(radius * SquircleScale, minHalf);
        return SquircleDegree;
    }
}

/// <summary>
/// Simpl's screens on Windows, drawn immediate-mode (as the showcase's are): each frame the window is built afresh
/// from what the engine knows, in layers — the ground, the page and its cards, the toolbar and sidebar, popovers, the
/// lenses — and each layer's glass reads the frame drawn so far, so a bar over a page frosts what scrolls under it.
/// Direct2D and DirectWrite draw the rest. What is under the pointer is decided from the frame before (the top-most
/// control there), so a click goes to what is seen on top.
/// </summary>
public sealed partial class Ui
{
    public readonly Theme T = new();

    // layers, bottom to top
    public const int Ground = 0, Page = 10, PageOver = 15, Chrome = 20, ChromeOver = 25, Popover = 30, PopoverOver = 35, Overlay = 40;

    public float W, H;           // the window, DIPs
    public float Scale = 1;      // pixels per DIP
    public double Time;
    public float Dt;
    private long frame;

    // input (DIPs)
    public Vector2 Mouse = new(-1, -1);
    public bool Down, Pressed, Released, RightPressed, Ctrl, Shift;
    public float Wheel;
    public readonly List<char> Typed = new();
    public readonly List<Key> Keys = new();
    public string? Active, Focus;
    private string? hot, wheelHot;
    public bool Animating;       // something moved this frame: draw again soon

    private readonly List<(string Id, R Rect, long Z)> hits = new(), wheels = new();
    private long seq;
    private int glassCount;
    private readonly Dictionary<string, Spring> springs = new();
    private readonly Dictionary<string, object> state = new();

    private sealed class LayerOps
    {
        public readonly List<(GlassLook Look, bool Frosted)> Glass = new();
        public readonly List<(RawRectF? Clip, Action Act)> Draws = new();
    }
    private readonly SortedDictionary<int, LayerOps> layers = new();
    private int layer = Page;
    private readonly Stack<R> clips = new();

    // ---- frame ----

    public void BeginFrame(float dt, float w, float h, float scale)
    {
        Dt = Math.Min(dt, 1 / 15f);
        Time += Dt;
        frame++;
        W = w;
        H = h;
        Scale = scale;
        Animating = false;
        foreach (var l in layers.Values) { l.Glass.Clear(); l.Draws.Clear(); }
        hits.Clear();
        wheels.Clear();
        seq = 0;
        glassCount = 0;
        layer = Page;
        clips.Clear();
        foreach (var s in springs.Values)
        {
            if (s.LastUsed < frame - 1) continue;
            s.Step(Dt);
            if (!s.Settled) Animating = true;
        }
    }

    public void EndFrame()
    {
        hot = Top(hits);
        wheelHot = Top(wheels);
        if (Released) Active = null;
        Pressed = Released = RightPressed = false;
        Wheel = 0;
        Typed.Clear();
        Keys.Clear();
        if (frame % 600 == 0)
        {
            foreach (var k in springs.Where(p => p.Value.LastUsed < frame - 600).Select(p => p.Key).ToList()) springs.Remove(k);
            PurgeText();
        }
    }

    private string? Top(List<(string Id, R Rect, long Z)> list)
    {
        string? id = null;
        long z = long.MinValue;
        foreach (var h in list)
            if (h.Z > z && h.Rect.Contains(Mouse)) { z = h.Z; id = h.Id; }
        return id;
    }

    // ---- layers and clips ----

    /// <summary>What follows goes in this layer (Page, Chrome, Popover …) until another is chosen.</summary>
    public void Layer(int n) => layer = n;
    public int CurrentLayer => layer;

    private LayerOps Ops(int n)
    {
        if (!layers.TryGetValue(n, out var l)) layers[n] = l = new LayerOps();
        return l;
    }

    public void PushClip(R r)
    {
        clips.Push(clips.Count > 0 ? clips.Peek().Intersect(r) : r);
    }

    public void PopClip() => clips.Pop();

    private R? Clip => clips.Count > 0 ? clips.Peek() : null;

    /// <summary>Something to draw (Direct2D, DIPs) in the current layer, inside the current clip.</summary>
    public void Draw(Action act) => Ops(layer).Draws.Add((Clip?.Raw, act));

    // ---- what is under the pointer ----

    private void Hit(string id, R r)
    {
        if (Clip is { } c) r = r.Intersect(c);
        hits.Add((id, r, layer * 1_000_000_000L + seq++));
    }

    /// <summary>A control: over it, held, clicked (from the frame before: the top-most control under the pointer).</summary>
    public Interaction Press(string id, R r, bool enabled = true)
    {
        Hit(id, r);
        if (!enabled) return default;
        var over = hot == id;
        if (Pressed && over) Active = id;
        var held = Active == id && Down;
        var clicked = Released && Active == id && over;
        return new Interaction(over, held, clicked, RightPressed && over);
    }

    /// <summary>A place clicks may not pass through (a panel's background).</summary>
    public void Block(R r) => Hit("#block" + seq, r);

    public bool Hovered(string id) => hot == id;

    /// <summary>The height of the band at the top the window's title bar shares with the toolbar (DIPs).</summary>
    public const float Caption = 52;

    /// <summary>Whether a control (of the frame before) is at a point — what the window asks before it lets a drag of its title bar through.</summary>
    public bool HitsAt(Vector2 p)
    {
        foreach (var h in hits) if (h.Rect.Contains(p)) return true;
        return false;
    }

    // ---- springs and kept state ----

    public Spring S(string id, float value, float response = 0.34f, float damping = 0.86f)
    {
        if (!springs.TryGetValue(id, out var s)) springs[id] = s = new Spring(value, response, damping);
        if (s.LastUsed < frame - 1) { s.Snap(value); } // (gone for a while: back where it is asked to be)
        s.LastUsed = frame;
        return s;
    }

    /// <summary>A spring heading where asked, its value now.</summary>
    public float Animate(string id, float target, float response = 0.3f, float damping = 1)
    {
        var s = S(id, target, response, damping);
        s.To(target);
        return s.Value;
    }

    public T State<T>(string id) where T : class, new()
    {
        if (!state.TryGetValue(id, out var o) || o is not T t) state[id] = t = new T();
        return t;
    }

    // ---- glass ----

    /// <summary>What shadow a shape of glass throws: none, a card's hairline of one, or a floating panel's.</summary>
    public enum Shadow { None, Card, Panel }

    /// <summary>
    /// A shape of glass in the current layer, reading what the layers under it drew. A panel or a card (frosted) is
    /// the Mac's: untinted, it is all but the page it sits on (a shade lighter by day, darker by night, as the Mac's
    /// cards measure); tinted — the Mac's .tint(colour.opacity(opacity)) — it lifts toward the colour. A lens is a
    /// knob's or a thumb's. A panel takes the clicks over it that nothing on it takes.
    /// </summary>
    public void Glass(R r, float radius, GlassKind kind = GlassKind.Panel, Color4? tint = null, float tintOpacity = 0, Shadow shadow = Shadow.Card, float power = 1)
    {
        var k = Scale;
        var l = new GlassLook
        {
            Cx = r.CenterX * k,
            Cy = r.CenterY * k,
            Hw = Math.Max(r.W / 2, 1) * k,
            Hh = Math.Max(r.H / 2, 1) * k,
            Radius = radius * k,
            Dark = T.Dark,
            RimWidth = 1.25f * k,
            Glow = T.Dark ? 0.03f : 0.02f,
            Outline = T.Dark ? 0.5f : 0.1f,
        };
        l.Squircle = GlassStyle.Corners(l.Hw, l.Hh, ref l.Radius);
        var minHalf = Math.Min(l.Hw, l.Hh);
        var frosted = kind == GlassKind.Panel;
        if (frosted)
        {
            l.Frost = Math.Clamp(GlassStyle.FrostSigma / 3, 0, 1);
            l.Pull = Math.Min(GlassStyle.Pull * k, minHalf * 1.2f);
            l.Band = Math.Min(GlassStyle.Band * k, minHalf * 0.6f);
            l.Saturation = GlassStyle.Saturation;
            l.Dispersion = GlassStyle.Dispersion;
            l.Rim = GlassStyle.Rim;
            var w = tint == null ? 0 : Math.Min(1, tintOpacity * (T.Dark ? 2.5f : 2.0f));
            if (w <= 0.001f)
            {
                // the Mac's untinted card: the page, a shade apart
                l.Keep = T.Dark ? 0.9f : 0.45f;
                l.R = l.G = l.B = T.Dark ? 0 : 1;
            }
            else
            {
                l.Keep = 1 - GlassStyle.Lift;
                var grey = (T.Dark ? 0.55f : 0.97f) * (1 - w);
                var c = tint!.Value;
                l.R = grey + (c.R - grey) * w;
                l.G = grey + (c.G - grey) * w;
                l.B = grey + (c.B - grey) * w;
            }
            switch (shadow)
            {
                case Shadow.Panel: l.Shadow = 0.18f; l.ShadowBlur = 24 * k; l.ShadowY = 10 * k; break;
                case Shadow.Card: l.Shadow = T.Dark ? 0.2f : 0.05f; l.ShadowBlur = 3 * k; l.ShadowY = 1 * k; break;
                default: l.Shadow = 0; l.ShadowBlur = k; break;
            }
        }
        else
        {
            l.Frost = 0;
            l.Keep = 1;
            l.Saturation = 1.08f;
            l.Brightness = 1.03f;
            l.Rim = Math.Max(GlassStyle.Rim, 0.6f);
            l.Shadow = T.Dark ? 0.18f : 0.1f;
            l.ShadowBlur = 8 * k;
            l.ShadowY = 3 * k;
            switch (kind)
            {
                case GlassKind.Clear:
                    l.Pull = Math.Min(GlassStyle.Pull * k, minHalf);
                    l.Band = Math.Min(GlassStyle.Band * k, minHalf * 0.8f);
                    l.Dispersion = GlassStyle.Dispersion;
                    l.Brightness = 1.02f;
                    l.Saturation = 1;
                    break;
                case GlassKind.Bubble:
                    l.Pull = minHalf * 0.6f;
                    l.Band = minHalf * 0.9f;
                    l.Magnify = 0.18f;
                    l.Dispersion = 0.05f;
                    break;
                default:
                    l.Pull = minHalf * (0.15f + 0.45f * power);
                    l.Band = minHalf * 0.8f;
                    l.Magnify = 0.12f * power;
                    l.Dispersion = GlassStyle.Dispersion + 0.03f * power;
                    break;
            }
        }
        if (Clip is { } clip)
            l.Clip = new RawRect((int)MathF.Floor(clip.X * k), (int)MathF.Floor(clip.Y * k), (int)MathF.Ceiling(clip.Right * k), (int)MathF.Ceiling(clip.Bottom * k));
        Ops(layer).Glass.Add((l, frosted));
        glassCount++;
        if (frosted) Block(r);
    }
}
