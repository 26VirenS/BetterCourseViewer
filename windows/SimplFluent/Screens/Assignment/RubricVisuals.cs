using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;

namespace Simpl.Screens;

// The rubric's drawings (the Mac's RubricMiniRing, RingPainter and RubricRingBack): the ring in miniature for a button, the
// ring itself on its stage — blooming in, bending to its marks, a slice swelling under the pointer — and the open
// criterion's bar with its levels' ticks. Drawn in code with WPF's own geometry, each piece of band a gradient along it.

/// <summary>A point along a band: its middle, the way out from it, its width.</summary>
internal readonly record struct RubricBandPoint(double X, double Y, double Nx, double Ny, double W);

/// <summary>
/// The ring in miniature, for a rubric's button (the Mac's RubricMiniRing): each criterion's slice as long as its points,
/// as thick as its share, bent out where it did well and in where it lost points, its colours running into each other.
/// </summary>
internal sealed class RubricMiniRing : FrameworkElement
{
    private readonly RubricModel model;
    private readonly double size;

    public RubricMiniRing(RubricModel model, double size)
    {
        this.model = model;
        this.size = size;
        Width = size;
        Height = size;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (model.N == 0) return;
        double cx = size / 2, cy = size / 2;
        var zk = size / 56;
        var outer = size / 2 - 2.5 * zk;
        var cols = model.Colors;
        var th = model.Thick.Select(t => zk * Math.Min(8, Math.Max(5, t * 0.5))).ToArray();
        var go = model.Bend.Select(b => zk * Math.Min(1.5, Math.Max(-1.8, b * 0.15))).ToArray();
        const int steps = 96;
        Point P(double r, double a) => new(cx + r * Math.Sin(a), cy - r * Math.Cos(a));
        for (var i = 0; i < steps; i++)
        {
            var t0 = (double)i / steps * RubricRing.Tau;
            var t1 = (double)(i + 1) / steps * RubricRing.Tau + 0.012; // (a hair over the next: no seam)
            var b0 = model.Between(t0);
            var b1 = model.Between(t1);
            var bm = model.Between((t0 + t1) / 2);
            var o0 = outer + RubricModel.Blend(go, b0);
            var o1 = outer + RubricModel.Blend(go, b1);
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(P(o0, t0), true, true);
                c.PolyLineTo(new[] { P(o1, t1), P(o1 - RubricModel.Blend(th, b1), t1), P(o0 - RubricModel.Blend(th, b0), t0) }, false, false);
            }
            geo.Freeze();
            var brush = K.Brush(cols[bm.From].Mix(cols[bm.To], bm.T).Color);
            brush.Freeze();
            dc.DrawGeometry(brush, null, geo);
        }
    }
}

/// <summary>
/// The ring's few moving values on one clock (the Mac's RingMotion, as much of it as the ring itself moves): how far it
/// has bloomed in, how far it has bent to its marks, and each slice's swell under the pointer — eased in and out slice by
/// slice. While any of them moves, <see cref="Frame"/> is raised every frame; settled, never. With Windows' animation
/// effects off (and in the screenshot suite) everything is where it ends at once.
/// </summary>
internal sealed class RubricClock
{
    private readonly Stopwatch watch = Stopwatch.StartNew();
    private readonly bool graded;
    private readonly double[] swellFrom, swellAim, swellAt;
    private double settleAt;
    private bool hooked;
    public bool Instant { get; }

    /// <summary>A frame while something moves.</summary>
    public event Action? Frame;

    public RubricClock(int count, bool graded)
    {
        this.graded = graded;
        Instant = !Motion.On;
        swellFrom = new double[count];
        swellAim = new double[count];
        swellAt = new double[count];
        // the ring blooms in (1.1 s); marked, it then bends to its marks (from 0.5 s, for 1.15 s)
        if (!Instant) Run(graded ? 1.65 : 1.1);
    }

    public double Now => watch.Elapsed.TotalSeconds;

    public double Bloom(double now) => Instant ? 1 : RubricRing.Clamp01(now / 1.1);

    public double Marks(double now) => !graded ? 0 : Instant ? 1 : RubricRing.Ease(RubricRing.Clamp01((now - 0.5) / 1.15));

    public double Swell(int k, double now)
    {
        if (k < 0 || k >= swellAim.Length) return 0;
        var aim = swellAim[k];
        if (Instant) return aim;
        var v = aim + (swellFrom[k] - aim) * Math.Exp(-Math.Max(0, now - swellAt[k]) / 0.115);
        return Math.Abs(v - aim) < 0.003 ? aim : v;
    }

    /// <summary>The slice under the pointer (or the keys) swells; the rest settle back.</summary>
    public void Aim(int hov)
    {
        var now = Now;
        var changed = false;
        for (var i = 0; i < swellAim.Length; i++)
        {
            double aim = i == hov ? 1 : 0;
            if (aim == swellAim[i]) continue;
            swellFrom[i] = Swell(i, now);
            swellAim[i] = aim;
            swellAt[i] = now;
            changed = true;
        }
        if (!changed) return;
        if (Instant) Frame?.Invoke(); else Run(now + 0.8);
    }

    private void Run(double until)
    {
        settleAt = Math.Max(settleAt, until);
        if (hooked) return;
        hooked = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        Frame?.Invoke();
        if (Now > settleAt + 0.06) Stop();
    }

    public void Stop()
    {
        if (!hooked) return;
        hooked = false;
        CompositionTarget.Rendering -= OnRendering;
    }
}

/// <summary>
/// The ring at rest on its stage (the Mac's RingPainter.rest): each slice blooming in — sweeping round from its start and
/// growing to its thickness on a spring, one after another — bent to its marks and turned to its grade's colour as the
/// clock says, the slice under the pointer swelling out; a dot beside each slice. Once it has bloomed, a narrow gap opens
/// where one criterion's slice meets the next, so each reads as a piece of its own to pick, not one band of colour.
/// </summary>
internal sealed class RubricRingVisual : FrameworkElement
{
    private readonly RubricGeometry geo;
    private readonly RubricClock clock;
    public int Sel { get; set; }
    public int Hov { get; set; } = -1;

    public RubricRingVisual(RubricGeometry geo, RubricClock clock)
    {
        this.geo = geo;
        this.clock = clock;
        Width = RubricRing.Width;
        Height = RubricRing.Height;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var n = geo.N;
        if (n == 0) return;
        var now = clock.Now;
        var bloom = clock.Bloom(now);
        var g = clock.Marks(now);
        var tones = geo.Tones(g);
        var go = geo.Bends(g);
        var spin = -0.6 * (1 - RubricRing.EaseOut(bloom));
        var size = 0.82 + 0.18 * RubricRing.SpringOut(bloom);
        var t = new TransformGroup();
        t.Children.Add(new TranslateTransform(-RubricRing.Cx, -RubricRing.Cy));
        t.Children.Add(new ScaleTransform(size, size));
        t.Children.Add(new RotateTransform(spin * 180 / Math.PI));
        t.Children.Add(new TranslateTransform(RubricRing.Cx, RubricRing.Cy));
        t.Freeze();
        dc.PushTransform(t);
        var lead = n > 1 ? 0.42 / (n - 1) : 0;
        // (the gaps between slices, as the bloom ends: about 4 of the stage's units wide)
        var seam = n > 1 ? 4 / RubricRing.Radius * RubricRing.Span(bloom, 0.85, 1) : 0;
        for (var k = 0; k < n; k++)
        {
            var local = RubricRing.Clamp01((bloom - lead * k) / 0.58);
            var sweep = RubricRing.EaseOut(local);
            if (sweep <= 0.001) continue;
            Slice(dc, k, sweep, RubricRing.SpringOut(local), tones, go, clock.Swell(k, now), seam);
            var d = RubricRing.Pt(RubricRing.Radius + go[k] + 22 + 9 * clock.Swell(k, now), geo.Model.Mids[k]);
            var r = k == Sel || k == Hov ? 4.5 : n > 15 ? 2.5 : 3.5;
            var a = RubricRing.Span(local, 0.5, 1);
            if (a <= 0.001) continue;
            var dot = K.Brush(tones[k].WithAlpha(a));
            dot.Freeze();
            dc.DrawEllipse(dot, null, d, r, r);
        }
        dc.Pop();
    }

    private void Slice(DrawingContext dc, int k, double sweep, double grow, RubricRGB[] tones, double[] bend, double bump, double seam)
    {
        var m = geo.Model;
        // (half the gap off each end, never more than a third of a narrow slice)
        var half = Math.Min(seam / 2, (m.Ends[k] - m.Starts[k]) / 6);
        var a0 = m.Starts[k] + half;
        var a1 = m.Ends[k] - half;
        RubricBandPoint Sample(double v)
        {
            var th = a0 + (a1 - a0) * v * sweep;
            var b = m.Between(th);
            var s1 = Math.Sin(Math.PI * Math.Min(1, v));
            var sw = bump * s1 * s1; // (the slice under the pointer swells out, smoothly, and back)
            var thick = RubricModel.Blend(m.Thick, b) * grow;
            var rad = RubricRing.Radius + RubricModel.Blend(bend, b) - (thick - 10) / 2 + 9 * sw; // (thicker inward: the outer edge on the circle)
            var sn = Math.Sin(th);
            var cs = Math.Cos(th);
            return new RubricBandPoint(RubricRing.Cx + rad * sn, RubricRing.Cy - rad * cs, sn, -cs, thick + 2.5 * sw);
        }
        RubricRGB Colour(double v)
        {
            var b = m.Between(m.Starts[k] + (m.Ends[k] - m.Starts[k]) * v * sweep);
            return tones[b.From].Mix(tones[b.To], b.T);
        }
        var count = geo.Pieces[k];
        var len = Math.Max(1, RubricRing.Radius * (a1 - a0) * sweep);
        var eps = geo.N > 1 ? 0.5 / len : 0; // (each piece runs on a hair under the next: no seam shows)
        for (var i = 0; i < count; i++)
        {
            var v0 = (double)i / count;
            var v1 = (double)(i + 1) / count;
            Piece(dc, Sample, Colour, v0, v1, v1 + eps, len / count);
        }
    }

    /// <summary>One piece of band from v0 to v1 (and on a hair to v1e, under the next piece): its outline from the
    /// sample, its colour a gradient along it.</summary>
    public static void Piece(DrawingContext dc, Func<double, RubricBandPoint> sample, Func<double, RubricRGB> colour, double v0, double v1, double v1e, double len)
    {
        var steps = Math.Max(3, Math.Min(90, (int)Math.Ceiling(len / 2.5)));
        var outer = new Point[steps + 1];
        var inner = new Point[steps + 1];
        for (var j = 0; j <= steps; j++)
        {
            var s = sample(v0 + (v1e - v0) * j / steps);
            var hw = s.W / 2;
            outer[j] = new Point(s.X + s.Nx * hw, s.Y + s.Ny * hw);
            inner[j] = new Point(s.X - s.Nx * hw, s.Y - s.Ny * hw);
        }
        var pts = new List<Point>(2 * steps + 1);
        for (var j = 1; j <= steps; j++) pts.Add(outer[j]);
        for (var j = steps; j >= 0; j--) pts.Add(inner[j]);
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(outer[0], true, true);
            c.PolyLineTo(pts, false, false);
        }
        geo.Freeze();
        var a = sample(v0);
        var b = sample(v1);
        Brush brush;
        if (Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y) < 0.05) brush = K.Brush(colour(v0 + (v1 - v0) * 0.5).Color);
        else
        {
            var stops = new GradientStopCollection(5);
            for (var q = 0; q < 5; q++)
            {
                var o = q / 4.0;
                stops.Add(new GradientStop(colour(v0 + (v1 - v0) * o).Color, o));
            }
            brush = new LinearGradientBrush(stops, new Point(a.X, a.Y), new Point(b.X, b.Y)) { MappingMode = BrushMappingMode.Absolute };
        }
        brush.Freeze();
        dc.DrawGeometry(brush, null, geo);
    }
}

/// <summary>Views placed at points of the stage, each hanging from its point the way its anchor says (the Mac's
/// PinnedLayout).</summary>
internal sealed class RubricPinPanel : Panel
{
    private readonly List<(Point At, double Ax, double Ay)> pins = new();

    public void Pin(int i, Point at, double ax, double ay)
    {
        while (pins.Count <= i) pins.Add((new Point(), 0.5, 0.5));
        pins[i] = (at, ax, ay);
    }

    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement c in InternalChildren) c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(RubricRing.Width, RubricRing.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var c = InternalChildren[i];
            var (at, ax, ay) = i < pins.Count ? pins[i] : (new Point(), 0.5, 0.5);
            var d = c.DesiredSize;
            c.Arrange(new Rect(new Point(at.X - ax * d.Width, at.Y - ay * d.Height), d));
        }
        return final;
    }
}

/// <summary>
/// The open criterion's bar (the Mac's RingPainter, the slice pulled out straight): a band of the criterion's own colour,
/// darker at the foot and lighter at the top, the stretch above its mark unearned and grey; the thread from its top up to
/// the little ring; a tick at the height of each level's points, the one given larger in the criterion's colour, and a
/// leader to any row that had to move. Where the points given are not the given level's, that level keeps a ring of its
/// colour and the mark's white dot stands at the points given, where the bar's colour stops. It grows up from its foot as
/// it opens.
/// </summary>
internal sealed class RubricBarVisual : FrameworkElement
{
    public static readonly DependencyProperty GrowProperty = DependencyProperty.Register(nameof(Grow), typeof(double), typeof(RubricBarVisual),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly RubricRGB tone;
    private readonly double frac, foot;
    private readonly List<RubricBarPlace> places;
    /// <summary>The points given, apart from the level given (null: the mark is the level's own tick).</summary>
    private readonly double? markY;
    /// <summary>The mark's own number beside its dot ("4"), so the dot is never read as the level row level with it.</summary>
    private readonly string? markLabel;

    public double Grow { get => (double)GetValue(GrowProperty); set => SetValue(GrowProperty, value); }

    public RubricBarVisual(RubricRGB tone, double frac, double foot, List<RubricBarPlace> places, double? markY = null, string? markLabel = null)
    {
        this.tone = tone;
        this.frac = frac;
        this.foot = foot;
        this.places = places;
        this.markY = markY;
        this.markLabel = markLabel;
        Width = RubricRing.Width;
        Height = RubricRing.Height;
        IsHitTestVisible = false;
    }

    private RubricRGB Track => Support.Appearance.Dark ? new RubricRGB(72, 72, 74) : new RubricRGB(206, 206, 211);

    /// <summary>The bar's colour at v (0 the foot, 1 the top).</summary>
    private RubricRGB At(double v)
    {
        var c = v < 0.5 ? tone.Mix(RubricRGB.Black, 0.36 * (0.5 - v)) : tone.Mix(RubricRGB.White, 0.44 * (v - 0.5));
        return v > frac + 1e-6 ? c.Mix(Track, 0.8) : c;
    }

    private void AddStep(GradientStopCollection stops)
    {
        var earned = frac < 0.5 ? tone.Mix(RubricRGB.Black, 0.36 * (0.5 - frac)) : tone.Mix(RubricRGB.White, 0.44 * (frac - 0.5));
        stops.Add(new GradientStop(earned.Color, frac));
        stops.Add(new GradientStop(At(Math.Min(1, frac + 2e-6)).Color, frac));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var grow = Grow;
        var top = foot - (foot - RubricRing.BarTop) * RubricRing.EaseOut(grow);
        const double w = 16;
        // the band, as far up as it has grown; its colour stepping to grey above the mark
        // (in order of offset: the earned colour up to the mark, the unearned one from it)
        var stops = new GradientStopCollection { new(At(0).Color, 0) };
        var step = frac > 1e-3 && frac < 1 - 1e-3;
        if (step && frac < 0.5) AddStep(stops);
        stops.Add(new GradientStop(At(0.5).Color, 0.5));
        if (step && frac >= 0.5) AddStep(stops);
        stops.Add(new GradientStop(At(1).Color, 1));
        var band = new LinearGradientBrush(stops, new Point(RubricRing.BarX, foot), new Point(RubricRing.BarX, RubricRing.BarTop)) { MappingMode = BrushMappingMode.Absolute };
        band.Freeze();
        dc.DrawRoundedRectangle(band, null, new Rect(RubricRing.BarX - w / 2, top - w / 2, w, Math.Max(w, foot - top + w)), w / 2, w / 2);
        // the thread from the bar's top up into the little ring (the way back), drawn once the bar is nearly up
        var stem = RubricRing.Span(grow, 0.76, 1);
        if (stem > 0)
        {
            var fy = RubricRing.MiniY + RubricRing.MiniRadius;
            var to = new Point(RubricRing.BarX, top + (fy - top) * stem);
            var pen = new Pen(new LinearGradientBrush(At(1).Color, tone.Color, new Point(RubricRing.BarX, top), new Point(RubricRing.BarX, fy)) { MappingMode = BrushMappingMode.Absolute }, 2.5)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.PushOpacity(stem);
            dc.DrawLine(pen, new Point(RubricRing.BarX, top - w / 2), to);
            dc.Pop();
        }
        // the levels' ticks, row by row, and a leader to any row that had to move
        var lead = RubricRing.Span(grow, 0.7, 1);
        if (lead > 0)
        {
            var leaderBrush = (TryFindResource(B.Text3) as Brush) ?? Brushes.Gray;
            var leader = new Pen(leaderBrush, 1.5);
            leader.Freeze();
            dc.PushOpacity(lead * 0.6);
            foreach (var p in places.Where(p => Math.Abs(p.Row - p.Tick) >= 3))
            {
                var geo = new StreamGeometry();
                using (var c = geo.Open())
                {
                    c.BeginFigure(new Point(RubricRing.BarX + 12, p.Tick), false, false);
                    c.BezierTo(new Point(RubricRing.BarX + 28, p.Tick), new Point(86, p.Row), new Point(102, p.Row), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, leader, geo);
            }
            dc.Pop();
        }
        var dark = Support.Appearance.Dark;
        for (var i = 0; i < places.Count; i++)
        {
            var p = places[i];
            var a = RubricRing.Span(grow, 0.8 + 0.04 * i, 0.96 + 0.04 * i);
            if (a <= 0.001) continue;
            var z = 0.2 + 0.8 * a;
            // (the level given, its points not the ones given: a ring of its colour; the mark's dot stands apart)
            var apart = p.Picked && markY != null;
            var r = (p.Picked && !apart ? 8 : apart ? 6.5 : 5.5) * z;
            Brush fill = p.Picked && !apart ? Brushes.White : K.Brush(dark ? Color.FromRgb(28, 28, 30) : Colors.White);
            var stroke = new Pen(p.Picked ? K.Brush(tone.Color) : K.Brush(dark ? Color.FromArgb(209, 255, 255, 255) : Color.FromArgb(150, 60, 60, 67)), 2.5 * z);
            stroke.Freeze();
            dc.PushOpacity(a);
            dc.DrawEllipse(fill, stroke, new Point(RubricRing.BarX, p.Tick), r, r);
            dc.Pop();
        }
        if (markY is { } my)
        {
            var a = RubricRing.Span(grow, 0.82, 1);
            if (a > 0.001)
            {
                var z = 0.2 + 0.8 * a;
                var stroke = new Pen(K.Brush(tone.Color), 2.5 * z);
                stroke.Freeze();
                dc.PushOpacity(a);
                dc.DrawEllipse(Brushes.White, stroke, new Point(RubricRing.BarX, my), 8 * z, 8 * z);
                // (its points beside it, at the bar's left, in the criterion's colour: the dot says what it is)
                if (!string.IsNullOrEmpty(markLabel))
                {
                    var ink = Support.Appearance.Dark ? tone.Mix(RubricRGB.White, 0.35).Color : tone.Mix(RubricRGB.Black, 0.3).Color;
                    var text = new FormattedText(markLabel, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface(K.TextFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12, K.Brush(ink), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(text, new Point(RubricRing.BarX - 14 - text.Width, my - text.Height / 2));
                }
                dc.Pop();
            }
        }
    }
}

/// <summary>
/// The ring in miniature on top of the bar (the Mac's RubricRingBack), the open criterion's slice missing from it where the
/// thread from the bar comes in — the bar is that slice, pulled out. On Windows a picture only: the way back is "‹ Ring"
/// beside ×, the screen's one back control.
/// </summary>
internal sealed class RubricRingBack : Button
{
    private readonly Face face;
    private double spun;

    private sealed class Face : FrameworkElement
    {
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(double), typeof(Face),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty SpunProperty = DependencyProperty.Register("Spun", typeof(double), typeof(Face),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        private readonly RubricModel model;
        public int Picked;

        public Face(RubricModel model)
        {
            this.model = model;
            Width = 44;
            Height = 44;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var fill = (double)GetValue(FillProperty);
            var spun = (double)GetValue(SpunProperty);
            var k = Picked;
            var c = new Point(22, 22);
            const double r = RubricRing.MiniRadius;
            const double lw = 3.5;
            var track = new Pen((TryFindResource(B.Subtle3) as Brush) ?? Brushes.Gray, lw);
            track.Freeze();
            dc.DrawEllipse(null, track, c, r, r);
            var n = model.N;
            var gap = n > 1 ? Math.Min(0.09, 2 * Math.PI / n / 5) : 0;
            var one = n == 1 ? 0.001 : 0;
            dc.PushTransform(new RotateTransform(spun, c.X, c.Y));
            for (var i = 0; i < n; i++)
                if (i != k) Arc(dc, c, r, model.Starts[i] + gap / 2, model.Ends[i] - gap / 2, model.Colors[i].Color, lw);
            if (fill > 0.001 && k >= 0 && k < n)
            {
                var mid = model.Mids[k];
                var tone = model.Colors[k].Color;
                Arc(dc, c, r, mid, mid + (model.Ends[k] - gap / 2 - one - mid) * fill, tone, lw);
                Arc(dc, c, r, mid, mid + (model.Starts[k] + gap / 2 + one - mid) * fill, tone, lw);
            }
            dc.Pop();
            var ink = new Pen((TryFindResource(B.Text) as Brush) ?? Brushes.White, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            ink.Freeze();
            var dx = -1.5 * fill;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(c.X + 1.6 + dx, c.Y - 5), false, false);
                g.PolyLineTo(new[] { new Point(c.X - 3.4 + dx, c.Y), new Point(c.X + 1.6 + dx, c.Y + 5) }, true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(null, ink, geo);
        }

        /// <summary>An arc between two angles clockwise from twelve o'clock (anticlockwise when to is before from).</summary>
        private static void Arc(DrawingContext dc, Point c, double r, double from, double to, Color color, double lw)
        {
            var sweep = Math.Abs(to - from);
            if (sweep < 1e-4) return;
            var pen = new Pen(K.Brush(color), lw);
            pen.Freeze();
            if (sweep >= RubricRing.Tau - 1e-3) { dc.DrawEllipse(null, pen, c, r, r); return; }
            Point P(double a) => new(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(P(from), false, false);
                g.ArcTo(P(to), new Size(r, r), 0, sweep > Math.PI, to > from ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
    }

    /// <summary>The open criterion: its slice missing, the little ring turned (the short way round) so the gap faces the
    /// bar.</summary>
    public void Show(RubricModel model, int k, bool animate)
    {
        var to = 180 - model.Mids[k] * 180 / Math.PI;
        var d = (to - spun) % 360;
        to = spun + (d + 540) % 360 - 180;
        spun = to;
        face.Picked = k;
        if (animate) Motion.To(face, Face.SpunProperty, to, 600, Motion.Gentle);
        else { face.BeginAnimation(Face.SpunProperty, null); face.SetValue(Face.SpunProperty, to); }
        face.InvalidateVisual();
    }

    /// <summary>For a screen reader, nothing to press here.</summary>
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Quiet(this);

    private sealed class Quiet : System.Windows.Automation.Peers.FrameworkElementAutomationPeer
    {
        public Quiet(FrameworkElement owner) : base(owner) { }
        protected override bool IsControlElementCore() => false;
        protected override bool IsContentElementCore() => false;
    }

    public RubricRingBack(RubricModel model, Action back)
    {
        face = new Face(model);
        Content = face;
        SetResourceReference(StyleProperty, "SubtleButton");
        K.SetCorner(this, new CornerRadius(22));
        Padding = new Thickness(0);
        Width = 44;
        Height = 44;
        // (a picture of where the bar came from, not a control: "‹ Ring" beside × is the one way back — Windows'
        //  one back control a screen — so the pointer, the keyboard and a screen reader pass it by)
        IsHitTestVisible = false;
        Focusable = false;
        IsTabStop = false;
        var scale = new ScaleTransform(1, 1);
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = scale;
        MouseEnter += (_, _) =>
        {
            Motion.To(face, Face.FillProperty, 1, 500, Motion.Gentle);
            Motion.To(scale, ScaleTransform.ScaleXProperty, 1.1, Motion.Medium, Motion.Gentle);
            Motion.To(scale, ScaleTransform.ScaleYProperty, 1.1, Motion.Medium, Motion.Gentle);
        };
        MouseLeave += (_, _) =>
        {
            Motion.To(face, Face.FillProperty, 0, 400, Motion.Gentle);
            Motion.To(scale, ScaleTransform.ScaleXProperty, 1, Motion.Medium, Motion.Gentle);
            Motion.To(scale, ScaleTransform.ScaleYProperty, 1, Motion.Medium, Motion.Gentle);
        };
        Click += (_, _) => back();
    }
}
