using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Support;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// Cell Division's drawing (spec §4.6 Drawing rules), the Mac's CellDivisionCanvas.swift: a 2D cell drawn with a
// DrawingContext from DivPainter's list — crisp at any size, and cheap to draw again every frame while a step moves. One
// cell unit is 0.42 × the view's shorter side (smaller when the cells would not fit, as two or four daughter cells spread
// out), y up. Over it, the step's labels with leader lines (spec §3.5's columns, BioLabelLayout), pressable: a press shows
// the part's card; hovering one lights its part. Labels fade out as a step moves and back in (120 ms) when it rests.
// Between steps the drawing is the two keyframes mixed (BioStepAnimator, 600 ms, eased), or crossfaded in 200 ms with
// Windows' animations off.

/// <summary>Cell Division's colours, light and dark (the chromosomes' are §3.8's maternal red and paternal blue).</summary>
internal static class DivColors
{
    public static Color Of(DivInk ink, bool dark) => ink switch
    {
        DivInk.Cytoplasm => K.Hex(dark ? "#1d3436" : "#e3f1f0"),
        DivInk.Membrane => K.Hex(dark ? "#9fb6b8" : "#55676a"),
        DivInk.Wall => K.Hex(dark ? "#6e9a4e" : "#9cc47a"),
        DivInk.Nucleoplasm => K.Hex(dark ? "#253f43" : "#d3e6e8"),
        DivInk.Envelope => K.Hex(dark ? "#8fa6aa" : "#6f8286"),
        DivInk.Nucleolus => K.Hex(dark ? "#5e6d72" : "#aab4b9"),
        DivInk.Furrow => K.Hex(dark ? "#ff7aa8" : "#e8508a"),
        DivInk.CellPlate => K.Hex(dark ? "#e2b85a" : "#a77b16"),
        DivInk.Plate => K.Hex(dark ? "#aeaeb2" : "#7c7c82"),
        DivInk.Microtubule => K.Hex(dark ? "#5fd1bd" : "#2f9a88"),
        DivInk.Aster => K.Hex(dark ? "#5fd1bd" : "#2f9a88"),
        DivInk.Centrosome => K.Hex(dark ? "#ffd60a" : "#d9a406"),
        DivInk.Centriole => K.Hex(dark ? "#ffcf70" : "#8a5a00"),
        DivInk.Maternal => BioPalette.Maternal.ToColor(dark),
        DivInk.Paternal => BioPalette.Paternal.ToColor(dark),
        DivInk.Centromere => K.Hex(dark ? "#141416" : "#1d1d1f"),
        _ => K.Hex(dark ? "#000000" : "#1d1d1f"),
    };

    /// <summary>A frozen brush of a colour at an opacity (made fresh: opacities change every frame while a step moves).</summary>
    public static SolidColorBrush Brush(Color c, double alpha = 1)
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1) * c.A / 255.0), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    public static Pen Pen(Color c, double alpha, double width, bool dashed = false)
    {
        var p = new Pen(Brush(c, alpha), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (dashed) p.DashStyle = new DashStyle(new[] { 3.0, 2.6 }, 0);
        p.Freeze();
        return p;
    }
}

/// <summary>Where a drawing is shown: points per cell unit and the view's point for (0, 0) of the cells (y up).</summary>
internal readonly record struct DivFit(double Scale, double Cx, double Cy)
{
    public Point P(DivPoint p) => new(Cx + p.X * Scale, Cy - p.Y * Scale);
    public DivPoint Back(Point p) => new((p.X - Cx) / Scale, (Cy - p.Y) / Scale);
}

/// <summary>
/// The cell at a step (or part way between two): cytoplasm, membranes and walls, nuclei, spindle, centrosomes and the
/// chromosomes, with the ploidy badges in each cell after a division. A press on a chromatid reports it.
/// </summary>
internal sealed class DivisionDrawing : FrameworkElement
{
    private static readonly Typeface Badge = new(K.TextFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public DivisionSequence? Sequence { get; set; }
    public BioStepPhase Phase { get; set; } = BioStepPhase.At(0);
    /// <summary>Room kept at the sides for the labels' columns.</summary>
    public bool RoomForLabels { get; set; } = true;
    /// <summary>A chromatid lit (hovered, or the one whose card shows).</summary>
    public int? LitChromatid { get; set; }
    /// <summary>A point lit (a hovered label's anchor that is not a chromatid).</summary>
    public DivPoint? LitPoint { get; set; }
    public int? SelectedChromatid { get; set; }

    /// <summary>A chromatid pressed (its index), or empty space (null).</summary>
    public event Action<int?>? ChromatidPressed;

    /// <summary>What was drawn last: the frame, its chromatids' shapes and the fit (the labels' anchors and the pointer).</summary>
    public DivisionFrame? Drawn { get; private set; }
    public DivChromatidShape[] Shapes { get; private set; } = Array.Empty<DivChromatidShape>();
    public DivFit Fit { get; private set; } = new(1, 0, 0);

    public DivisionDrawing()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        Cursor = Cursors.Arrow;
        SnapsToDevicePixels = false;
    }

    /// <summary>The fit for a size and the cells' bounds: 0.42 × the shorter side per unit, smaller if the cells would not
    /// fit (with room for the labels' columns), centred on the cells.</summary>
    public static DivFit FitFor(Size size, DivRect bounds, bool labels)
    {
        double w = Math.Max(size.Width, 1), h = Math.Max(size.Height, 1);
        // (the Mac's rule: room either side for the labels' columns, 12 above and below)
        var side = labels ? Math.Min(190, w * 0.24) : 14;
        double roomW = Math.Max(w - 2 * side, 60), roomH = Math.Max(h - 24, 60);
        var s = Math.Min(0.42 * Math.Min(w, h), Math.Min(roomW / Math.Max(bounds.Width, 0.1), roomH / Math.Max(bounds.Height, 0.1)));
        s = Math.Max(s, 1);
        var c = bounds.Width > 0 ? bounds.Centre : DivPoint.Zero;
        return new DivFit(s, w / 2 - c.X * s, h / 2 + c.Y * s);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        // (the whole of it takes the pointer, not only what is drawn)
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));
        if (Sequence is not { } seq || seq.Keyframes.Count == 0) return;
        var ph = Phase;
        int from = Math.Clamp(ph.From, 0, seq.Keyframes.Count - 1), to = Math.Clamp(ph.To, 0, seq.Keyframes.Count - 1);
        var a = seq.Keyframes[from].Frame;
        var b = seq.Keyframes[to].Frame;
        var dark = Appearance.Dark;
        var chiasma = seq.Options.Chiasma;
        if (ph.Crossfade && from != to && ph.T < 1)
        {
            // (animations off: the two keyframes faded across each other, each where it is)
            var fitA = FitFor(size, a.Bounds, RoomForLabels);
            var fitB = FitFor(size, b.Bounds, RoomForLabels);
            Paint(dc, a, fitA, chiasma, 1 - ph.T, dark, false);
            var (_, shapes) = Paint(dc, b, fitB, chiasma, ph.T, dark, false);
            Drawn = b;
            Shapes = shapes;
            Fit = fitB;
            return;
        }
        var f = from == to ? b : a.Mixed(b, ph.T);
        var fit = FitFor(size, f.Bounds, RoomForLabels);
        var (_, s) = Paint(dc, f, fit, chiasma, 1, dark, true);
        Drawn = f;
        Shapes = s;
        Fit = fit;
        if (ph.IsMoving) return;
        // (at rest: each cell's ploidy after a division)
        var kf = seq.Keyframes[to];
        foreach (var badge in kf.Badges) DrawBadge(dc, seq.Badge(to, badge.Cell), fit.P(badge.At), dark);
    }

    private (List<DivPrimitive>, DivChromatidShape[]) Paint(DrawingContext dc, DivisionFrame f, DivFit fit, double chiasma, double fade, bool dark, bool lights)
    {
        var (prims, shapes) = DivPainter.Paint(f, chiasma, fade);
        var s = fit.Scale;
        // (lines drawn thinner on a small drawing, as the side-by-side Compare page's)
        var lineScale = Math.Clamp(s / 180, 0.7, 1.15);
        foreach (var p in prims)
        {
            switch (p)
            {
                case DivFill fill:
                {
                    var g = new StreamGeometry { FillRule = FillRule.Nonzero };
                    using (var ctx = g.Open())
                        foreach (var shape in fill.Shapes)
                        {
                            if (shape.Length < 3) continue;
                            ctx.BeginFigure(fit.P(shape[0]), true, true);
                            ctx.PolyLineTo(shape.Skip(1).Select(fit.P).ToList(), true, true);
                        }
                    g.Freeze();
                    dc.DrawGeometry(DivColors.Brush(DivColors.Of(fill.Ink, dark), fill.Alpha), null, g);
                    break;
                }
                case DivLine line:
                    if (line.Alpha <= 0.005) break;
                    dc.DrawGeometry(null, DivColors.Pen(DivColors.Of(line.Ink, dark), line.Alpha, line.Width * lineScale, line.Dashed), Lines(line.Lines, fit));
                    break;
                case DivBand band:
                    if (band.Alpha <= 0.005) break;
                    dc.DrawGeometry(null, DivColors.Pen(DivColors.Of(band.Ink, dark), band.Alpha, Math.Max(1, band.Width * s)), Lines(band.Lines, fit));
                    break;
                case DivDisc disc:
                    if (disc.Alpha <= 0.005) break;
                    var r = Math.Max(0.8, disc.R * s);
                    if (disc.Ink == DivInk.Centromere && dark)
                        dc.DrawEllipse(DivColors.Brush(DivColors.Of(disc.Ink, dark), disc.Alpha), DivColors.Pen(Colors.White, disc.Alpha * 0.7, 1), fit.P(disc.C), r, r);
                    else dc.DrawEllipse(DivColors.Brush(DivColors.Of(disc.Ink, dark), disc.Alpha), null, fit.P(disc.C), r, r);
                    break;
                case DivChromatidPrimitive ch:
                    Chromatid(dc, ch.Paint, fit, dark, lights);
                    break;
            }
        }
        if (lights && LitPoint is { } lit)
        {
            var accent = Appearance.Accent;
            dc.DrawEllipse(DivColors.Brush(accent, 0.18), DivColors.Pen(accent, 0.9, 1.5), fit.P(lit), 9, 9);
        }
        return (prims, shapes);
    }

    private static StreamGeometry Lines(List<DivPoint[]> lines, DivFit fit)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
            foreach (var l in lines)
            {
                if (l.Length < 2) continue;
                ctx.BeginFigure(fit.P(l[0]), false, false);
                ctx.PolyLineTo(l.Skip(1).Select(fit.P).ToList(), true, true);
            }
        g.Freeze();
        return g;
    }

    private static StreamGeometry Poly(DivPoint[] pts, int from, int to, DivFit fit)
    {
        var g = new StreamGeometry();
        from = Math.Max(0, from);
        to = Math.Min(pts.Length - 1, to);
        if (to > from)
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(fit.P(pts[from]), false, false);
                var list = new List<Point>(to - from);
                for (var i = from + 1; i <= to; i++) list.Add(fit.P(pts[i]));
                ctx.PolyLineTo(list, true, true);
            }
        g.Freeze();
        return g;
    }

    /// <summary>A chromatid: an edge a shade darker, its colour (a recombinant part in the other parent's, mixed in as it
    /// crosses over), and a sheen along it as it condenses; lit in the accent when hovered or chosen.</summary>
    private void Chromatid(DrawingContext dc, DivChromatidPaint c, DivFit fit, bool dark, bool lights)
    {
        var w = Math.Max(1.4, c.Width * fit.Scale);
        var whole = Poly(c.Points, 0, c.Points.Length - 1, fit);
        dc.PushOpacity(Math.Clamp(c.Alpha, 0, 1));
        var lit = lights && (LitChromatid == c.Index || SelectedChromatid == c.Index);
        if (lit)
        {
            var accent = Appearance.Accent;
            dc.DrawGeometry(null, DivColors.Pen(accent, 0.9, w + 6), whole);
        }
        var own = DivColors.Of(c.Runs[0].Ink, dark);
        dc.DrawGeometry(null, DivColors.Pen(K.Mix(own, Colors.Black, dark ? 0.5 : 0.38), 1, w + 1.6), whole);
        foreach (var run in c.Runs)
        {
            var colour = K.Mix(DivColors.Of(run.Ink, dark), DivColors.Of(run.Other, dark), Math.Clamp(run.Mix, 0, 1));
            dc.DrawGeometry(null, DivColors.Pen(colour, 1, w), Poly(c.Points, run.From, run.To, fit));
        }
        if (c.K > 0.3 && w > 4)
            dc.DrawGeometry(null, DivColors.Pen(Colors.White, 0.28 * DivMath.Smoothstep(0.3, 1, c.K), Math.Max(1, w * 0.26)), whole);
        dc.Pop();
    }

    private void DrawBadge(DrawingContext dc, string text, Point at, bool dark)
    {
        var tint = BioColors.Tool("divide");
        var ink = dark ? K.Mix(tint, Colors.White, 0.3) : K.Mix(tint, Colors.Black, 0.35);
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Badge, 14, DivColors.Brush(ink), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var rect = new Rect(at.X - ft.Width / 2 - 8, at.Y - ft.Height / 2 - 2, ft.Width + 16, ft.Height + 4);
        dc.DrawRoundedRectangle(DivColors.Brush(dark ? Color.FromRgb(0x2b, 0x2b, 0x2e) : Colors.White, 0.92), DivColors.Pen(tint, 0.6, 1), rect, rect.Height / 2, rect.Height / 2);
        dc.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Focus();
        if (Drawn is not { } f || Phase.IsMoving) return;
        var at = Fit.Back(e.GetPosition(this));
        ChromatidPressed?.Invoke(DivPainter.Hit(at, f, Shapes, 6 / Math.Max(Fit.Scale, 1)));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Drawn is not { } f || Phase.IsMoving) return;
        var hit = DivPainter.Hit(Fit.Back(e.GetPosition(this)), f, Shapes, 6 / Math.Max(Fit.Scale, 1));
        var cursor = hit != null ? Cursors.Hand : Cursors.Arrow;
        if (Cursor != cursor) Cursor = cursor;
        if (hit != LitChromatid)
        {
            LitChromatid = hit;
            ToolTip = hit is { } i ? DivParts.ChromatidName(Sequence, Phase.To, i) : null;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (LitChromatid == null) return;
        LitChromatid = null;
        InvalidateVisual();
    }
}

/// <summary>
/// A process at a step, drawn: the drawing, and over it the step's labels with leader lines (pressable; hovering one
/// lights its part). Driven by a step-through (its own, or the Compare page's mapped onto this process's steps).
/// </summary>
internal sealed class DivisionStageView : Grid
{
    private readonly Func<DivisionSequence> sequence;
    private readonly BioStepController steps;
    private readonly Func<int, int> map;
    private readonly bool labelsAllowed;
    private readonly DivisionDrawing drawing = new();
    private readonly Canvas overlay = new() { ClipToBounds = false };
    private readonly BioStepAnimator animator;
    private bool restingDrawn;
    private string? hoverPart;

    public DivisionDrawing Drawing => drawing;

    /// <param name="map">The process's step for a step of the controller (the Compare page's pairs).</param>
    public DivisionStageView(Func<DivisionSequence> sequence, BioStepController steps, Func<int, int>? map = null, bool labels = true)
    {
        this.sequence = sequence;
        this.steps = steps;
        this.map = map ?? (i => i);
        labelsAllowed = labels;
        ClipToBounds = true;
        Children.Add(drawing);
        Children.Add(overlay);
        animator = new BioStepAnimator(steps, Draw);
        // (a press on a chromosome shows its card; on empty space, puts the card away)
        drawing.ChromatidPressed += i =>
        {
            if (!labelsAllowed) return;
            var sel = i is { } k ? $"chromatid:{k}" : null;
            if (sel == null && CellDivisionModel.Selected == null) return;
            CellDivisionModel.Selected = sel == CellDivisionModel.Selected ? null : sel;
            CellDivisionModel.Tell();
        };
        BioStepKeys.Attach(drawing, steps);
        SizeChanged += (_, _) => Refresh();
        void Model() => Refresh();
        void Look() => Refresh();
        Loaded += (_, _) =>
        {
            animator.Start();
            CellDivisionModel.Changed -= Model;
            CellDivisionModel.Changed += Model;
            Appearance.Changed -= Look;
            Appearance.Changed += Look;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            animator.Stop();
            CellDivisionModel.Changed -= Model;
            Appearance.Changed -= Look;
        };
    }

    /// <summary>Draw again as things are (options, the theme, the size, a selection).</summary>
    public void Refresh()
    {
        restingDrawn = false;
        Draw(steps.Phase());
    }

    private BioStepPhase Mapped(BioStepPhase p)
    {
        var seq = sequence();
        int last = Math.Max(0, seq.Keyframes.Count - 1);
        int from = Math.Clamp(map(p.From), 0, last), to = Math.Clamp(map(p.To), 0, last);
        return from == to ? BioStepPhase.At(to) : new BioStepPhase(from, to, p.T, p.Crossfade);
    }

    private void Draw(BioStepPhase controllerPhase)
    {
        var seq = sequence();
        var ph = Mapped(controllerPhase);
        drawing.Sequence = seq;
        drawing.Phase = ph;
        drawing.RoomForLabels = labelsAllowed && CellDivisionModel.ShowLabels;
        drawing.SelectedChromatid = CellDivisionModel.Selected is { } sel && sel.StartsWith("chromatid:", StringComparison.Ordinal) && int.TryParse(sel[10..], out var ci) ? ci : null;
        drawing.InvalidateVisual();
        var step = ph.To;
        if (step >= 0 && step < seq.Stages.Count)
            AutomationProperties.SetName(drawing, $"{seq.Process.Title()}, step {step + 1} of {seq.Stages.Count}: {seq.Stages[step].Title}. {DivParts.Describe(seq, step)}");
        if (ph.IsMoving)
        {
            if (restingDrawn)
            {
                restingDrawn = false;
                if (Motion.On) Motion.To(overlay, OpacityProperty, 0, 120);
                else overlay.Opacity = 0;
            }
            return;
        }
        if (restingDrawn) return;
        restingDrawn = true;
        Labels(seq, step);
    }

    /// <summary>The step's labels: anchors from the step's frame (as the drawing draws it at rest), laid out in two columns
    /// with leader lines.</summary>
    private void Labels(DivisionSequence seq, int step)
    {
        overlay.Children.Clear();
        if (!labelsAllowed || !CellDivisionModel.ShowLabels || step < 0 || step >= seq.Keyframes.Count || ActualWidth < 40 || ActualHeight < 40)
            return;
        var kf = seq.Keyframes[step];
        var f = kf.Frame;
        var shapes = f.Shapes(seq.Options.Chiasma);
        var fit = DivisionDrawing.FitFor(new Size(ActualWidth, ActualHeight), f.Bounds, true);
        var inputs = new List<BioLabelInput>();
        var anchors = new Dictionary<string, (Point P, DivLabelDef Def)>();
        foreach (var l in kf.Labels)
        {
            var (pt, visible) = DivPainter.Anchor(l.Anchor, f, shapes);
            var p = fit.P(pt);
            if (anchors.ContainsKey(l.Id)) continue;
            anchors[l.Id] = (p, l);
            inputs.Add(new BioLabelInput(l.Id, l.Text, p.X, p.Y, visible, 1, Measure(l.Text) + 20));
        }
        var b = f.Bounds;
        var r = b.Width / 2 * fit.Scale;
        var centre = fit.P(b.Centre);
        var (placed, more) = BioLabelLayout.Layout(inputs, centre.X, centre.Y, r, ActualWidth, ActualHeight);
        var dark = Appearance.Dark;
        var leaders = new StreamGeometry();
        using (var ctx = leaders.Open())
            foreach (var pl in placed)
            {
                ctx.BeginFigure(new Point(pl.AnchorX, pl.AnchorY), false, false);
                ctx.LineTo(new Point(pl.ElbowX, pl.Y), true, true);
                ctx.LineTo(new Point(pl.EdgeX, pl.Y), true, true);
            }
        leaders.Freeze();
        var ink = dark ? Color.FromRgb(0xe8, 0xe8, 0xea) : Color.FromRgb(0x2b, 0x2b, 0x2e);
        overlay.Children.Add(new Path { Data = leaders, Stroke = DivColors.Brush(ink, 0.55), StrokeThickness = 1, IsHitTestVisible = false });
        foreach (var pl in placed)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Fill = DivColors.Brush(ink, 0.8), IsHitTestVisible = false };
            Canvas.SetLeft(dot, pl.AnchorX - 3);
            Canvas.SetTop(dot, pl.AnchorY - 3);
            overlay.Children.Add(dot);
        }
        foreach (var pl in placed)
        {
            if (!anchors.TryGetValue(pl.Id, out var a)) continue;
            overlay.Children.Add(Capsule(a.Def, pl, fit.Back(a.P)));
        }
        if (more > 0)
        {
            var t = K.Text($"+{more} more", Ty.Caption, B.Text2);
            Canvas.SetLeft(t, 16);
            Canvas.SetTop(t, ActualHeight - 28);
            overlay.Children.Add(t);
        }
        if (Motion.On && overlay.Opacity < 1) Motion.To(overlay, OpacityProperty, 1, 120);
        else overlay.Opacity = 1;
    }

    private static double Measure(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = K.TextFace };
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(t.DesiredSize.Width);
    }

    /// <summary>A label's capsule: its words at 12 semibold on the card's fill (85%), pressable; the chosen one in the accent.</summary>
    private Button Capsule(DivLabelDef def, BioLabelPlacement pl, DivPoint anchor)
    {
        var chosen = CellDivisionModel.Selected == def.Part;
        var words = new TextBlock { Text = def.Text, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = K.TextFace, VerticalAlignment = VerticalAlignment.Center };
        if (chosen) words.SetResourceReference(TextBlock.ForegroundProperty, B.OnAccent);
        else words.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
        var face = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 0, 10, 0), Height = BioLabelLayout.Height, Child = words, BorderThickness = new Thickness(1) };
        if (chosen) face.SetResourceReference(Border.BackgroundProperty, B.Accent);
        else
        {
            face.Background = DivColors.Brush(Appearance.Dark ? Color.FromRgb(0x2c, 0x2c, 0x2e) : Colors.White, 0.85);
            face.SetResourceReference(Border.BorderBrushProperty, B.CardStroke);
        }
        var b = K.Subtle(face, () =>
        {
            CellDivisionModel.Selected = chosen ? null : def.Part;
            CellDivisionModel.Tell();
        }, new Thickness(0), 9);
        b.MinWidth = 0;
        b.MinHeight = 0;
        b.ToolTip = DivParts.Of(def.Part) != null ? $"{def.Text} — press for its card" : def.Text;
        AutomationProperties.SetName(b, $"{def.Text}. Press for its card.");
        var (x, y, w, _) = pl.Frame;
        Canvas.SetLeft(b, x);
        Canvas.SetTop(b, y);
        b.Width = w;
        // (hovering a label lights its part in the drawing)
        b.MouseEnter += (_, _) => Light(def, anchor, true);
        b.MouseLeave += (_, _) => Light(def, anchor, false);
        b.GotKeyboardFocus += (_, _) => Light(def, anchor, true);
        b.LostKeyboardFocus += (_, _) => Light(def, anchor, false);
        return b;
    }

    private void Light(DivLabelDef def, DivPoint anchor, bool on)
    {
        if (!on && hoverPart != def.Id) return;
        hoverPart = on ? def.Id : null;
        var chromatid = def.Anchor.Kind is DivAnchorKind.Arm or DivAnchorKind.ShortArm or DivAnchorKind.Centromere ? def.Anchor.I : (int?)null;
        drawing.LitChromatid = on ? chromatid : null;
        drawing.LitPoint = on && chromatid == null ? anchor : null;
        drawing.InvalidateVisual();
    }
}
