using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Simpl.Kit;

/// <summary>A cubic Bézier timing curve, as Windows 11's motion names them.</summary>
public sealed class Bezier : IEasingFunction
{
    private readonly double x1, y1, x2, y2;

    public Bezier(double x1, double y1, double x2, double y2) { this.x1 = x1; this.y1 = y1; this.x2 = x2; this.y2 = y2; }

    public double Ease(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        // the curve's x for t is solved for its parameter (Newton, then bisection), and its y returned
        double u = t;
        for (var i = 0; i < 8; i++)
        {
            var x = Sample(x1, x2, u) - t;
            if (Math.Abs(x) < 1e-5) return Sample(y1, y2, u);
            var d = Slope(x1, x2, u);
            if (Math.Abs(d) < 1e-6) break;
            u -= x / d;
        }
        double lo = 0, hi = 1;
        u = t;
        for (var i = 0; i < 30; i++)
        {
            var x = Sample(x1, x2, u);
            if (Math.Abs(x - t) < 1e-5) break;
            if (x < t) lo = u; else hi = u;
            u = (lo + hi) / 2;
        }
        return Sample(y1, y2, u);
    }

    private static double Sample(double a, double b, double u) => ((1 - 3 * b + 3 * a) * u + (3 * b - 6 * a)) * u * u + 3 * a * u;
    private static double Slope(double a, double b, double u) => 3 * (1 - 3 * b + 3 * a) * u * u + 2 * (3 * b - 6 * a) * u + 3 * a;
}

/// <summary>
/// Windows 11's motion (WinUI's timing): things arriving decelerate into place ("fast out, slow in"), things leaving
/// accelerate away, and moves on screen take the standard point-to-point curve. Durations are Windows' own: 83, 167,
/// 250, 333 ms. Under Windows' "Animation effects" off, everything arrives at once — and so where WPF draws without
/// the graphics card (Fx: render tier 0).
/// </summary>
public static class Motion
{
    public static readonly Bezier Decelerate = new(0, 0, 0, 1);
    public static readonly Bezier Accelerate = new(1, 0, 1, 1);
    public static readonly Bezier Standard = new(0.55, 0.55, 0, 1);
    public static readonly Bezier Gentle = new(0.1, 0.9, 0.2, 1);

    public const int Fast = 83, Normal = 167, Medium = 250, Slow = 333;

    /// <summary>
    /// The one motion pages move in, here as on the Mac and in the extension: a strong ease-out — quick to start, long to
    /// settle (CSS's cubic-bezier(0.23, 1, 0.32, 1)) — over <see cref="Page"/>: a page going deeper, coming back, or
    /// another place crossfading in, and the pane's mark moving to its row with it.
    /// </summary>
    public static readonly Bezier Out = new(0.23, 1, 0.32, 1);

    /// <summary>How long a page takes to come in (and the pane's mark to reach its row).</summary>
    public const int Page = 220;

    /// <summary>Windows' own "Animation effects" setting (Settings ▸ Accessibility ▸ Visual effects), where this PC can
    /// draw motion smoothly (Fx).</summary>
    public static bool On => SystemParameters.ClientAreaAnimation && Fx.Motion && Support.Launch.Get("shot") == null;

    public static Duration Ms(double ms) => new(TimeSpan.FromMilliseconds(ms));

    /// <summary>A property animated to a value from where it is now.</summary>
    public static void To(IAnimatable target, DependencyProperty dp, double to, double ms, IEasingFunction? ease = null, double delayMs = 0, Action? done = null)
    {
        if (!On)
        {
            target.BeginAnimation(dp, null);
            if (target is DependencyObject o) o.SetValue(dp, to);
            done?.Invoke();
            return;
        }
        var a = new DoubleAnimation(to, Ms(ms)) { EasingFunction = ease ?? Decelerate, BeginTime = TimeSpan.FromMilliseconds(delayMs), FillBehavior = FillBehavior.HoldEnd };
        if (done != null) a.Completed += (_, _) => done();
        target.BeginAnimation(dp, a);
    }

    /// <summary>A property animated from one value to another.</summary>
    public static void FromTo(IAnimatable target, DependencyProperty dp, double from, double to, double ms, IEasingFunction? ease = null, double delayMs = 0, Action? done = null)
    {
        if (!On)
        {
            target.BeginAnimation(dp, null);
            if (target is DependencyObject o) o.SetValue(dp, to);
            done?.Invoke();
            return;
        }
        var a = new DoubleAnimation(from, to, Ms(ms)) { EasingFunction = ease ?? Decelerate, BeginTime = TimeSpan.FromMilliseconds(delayMs), FillBehavior = FillBehavior.HoldEnd };
        if (done != null) a.Completed += (_, _) => done();
        target.BeginAnimation(dp, a);
    }

    private static TranslateTransform Translate(UIElement e)
    {
        if (e.RenderTransform is TranslateTransform t && !t.IsFrozen) return t;
        if (e.RenderTransform is TransformGroup g) foreach (var c in g.Children) if (c is TranslateTransform tt && !tt.IsFrozen) return tt;
        var n = new TranslateTransform();
        e.RenderTransform = n;
        return n;
    }

    private static ScaleTransform Scale(FrameworkElement e, double ox = 0.5, double oy = 0.5)
    {
        e.RenderTransformOrigin = new Point(ox, oy);
        if (e.RenderTransform is ScaleTransform s && !s.IsFrozen) return s;
        var n = new ScaleTransform(1, 1);
        e.RenderTransform = n;
        return n;
    }

    /// <summary>WinUI's entrance: up from a little below, fading in, decelerating into place (a page's content, a list's rows).</summary>
    public static void Enter(UIElement e, double delayMs = 0, double dy = 28, double ms = 400)
    {
        if (!On) return;
        var t = Translate(e);
        e.Opacity = 0;
        t.Y = dy;
        FromTo(e, UIElement.OpacityProperty, 0, 1, Math.Min(ms, 250), Decelerate, delayMs);
        FromTo(t, TranslateTransform.YProperty, dy, 0, ms, Decelerate, delayMs);
    }

    /// <summary>Each child entering a beat after the one before (Windows' staggered list entrance).</summary>
    public static void Stagger(IEnumerable<UIElement> items, double stepMs = 40, double dy = 28, int max = 12)
    {
        var i = 0;
        foreach (var e in items)
        {
            Enter(e, Math.Min(i, max) * stepMs, dy);
            i++;
        }
    }

    /// <summary>A drill-in: what is opened grows a little into place as it fades in (WinUI's DrillInNavigationTransition).</summary>
    public static void DrillIn(FrameworkElement e, double ms = 333)
    {
        if (!On) return;
        var s = Scale(e);
        e.Opacity = 0;
        FromTo(e, UIElement.OpacityProperty, 0, 1, Normal, Decelerate);
        FromTo(s, ScaleTransform.ScaleXProperty, 0.94, 1, ms, Decelerate);
        FromTo(s, ScaleTransform.ScaleYProperty, 0.94, 1, ms, Decelerate);
    }

    /// <summary>A step slid in from the side it is going to (the setup's steps; a sign-in step).</summary>
    public static void SlideIn(UIElement e, bool forward, double dx = 60)
    {
        if (!On) return;
        var t = Translate(e);
        e.Opacity = 0;
        FromTo(e, UIElement.OpacityProperty, 0, 1, Medium, Decelerate);
        FromTo(t, TranslateTransform.XProperty, forward ? dx : -dx, 0, Slow + 67, Decelerate);
    }

    /// <summary>A dialog's arrival: from a touch larger, fading in (WinUI's ContentDialog).</summary>
    public static void Pop(FrameworkElement e)
    {
        if (!On) return;
        var s = Scale(e);
        e.Opacity = 0;
        FromTo(e, UIElement.OpacityProperty, 0, 1, Normal, Decelerate);
        FromTo(s, ScaleTransform.ScaleXProperty, 1.05, 1, Medium, Decelerate);
        FromTo(s, ScaleTransform.ScaleYProperty, 1.05, 1, Medium, Decelerate);
    }

    /// <summary>A dialog's leaving: shrinking a touch, fading out, then done.</summary>
    public static void Unpop(FrameworkElement e, Action done)
    {
        if (!On) { done(); return; }
        var s = Scale(e);
        FromTo(e, UIElement.OpacityProperty, e.Opacity, 0, Fast, Accelerate);
        FromTo(s, ScaleTransform.ScaleXProperty, 1, 1.05, Normal, Accelerate);
        FromTo(s, ScaleTransform.ScaleYProperty, 1, 1.05, Normal, Accelerate, 0, done);
    }

    public static void FadeIn(UIElement e, double ms = Normal, double delayMs = 0)
    {
        if (!On) { e.Opacity = 1; return; }
        FromTo(e, UIElement.OpacityProperty, 0, 1, ms, Decelerate, delayMs);
    }

    public static void FadeOut(UIElement e, Action done, double ms = Fast)
    {
        if (!On) { done(); return; }
        FromTo(e, UIElement.OpacityProperty, e.Opacity, 0, ms, Accelerate, 0, done);
    }

    /// <summary>The Mac's shake for a wrong password: side to side, settling.</summary>
    public static void Shake(UIElement e)
    {
        if (!On) return;
        var t = Translate(e);
        var a = new DoubleAnimationUsingKeyFrames { Duration = Ms(500) };
        foreach (var (ms, x) in new[] { (0.0, 0.0), (60, -10), (140, 10), (220, -7), (290, 4), (440, 0) })
            a.KeyFrames.Add(new SplineDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new KeySpline(0.3, 0, 0.3, 1)));
        t.BeginAnimation(TranslateTransform.XProperty, a);
    }
}
