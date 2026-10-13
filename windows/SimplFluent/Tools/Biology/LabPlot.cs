using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Simpl.Kit;

namespace Simpl.Tools;

// A small chart for the lab tools (the Bio Lab Calculator's SA:V, growth and error bars; the Enzyme Lab's three linked
// graphs and its energy diagram): axes with round ticks and their names, curves (solid or dashed), marked points,
// dashed guide lines with their words, and error bars — drawn in the theme's text colours, so light and dark follow
// Settings, with each curve in a colour its caller chooses (and named in a legend, never colour alone).

/// <summary>A curve: its points, its colour, dashed or not, and its name for the legend (none: not listed).</summary>
internal sealed record PlotLine(IReadOnlyList<(double X, double Y)> Points, Color Color, bool Dashed = false, string? Name = null, double Width = 2.2);

/// <summary>A marked point and its words.</summary>
internal sealed record PlotMark(double X, double Y, Color Color, string? Label = null);

/// <summary>A dashed guide across the chart at a value (horizontal: a y; else an x), with its words.</summary>
internal sealed record PlotGuide(double At, bool Horizontal, string Label, Color? Color = null);

/// <summary>Words at a point of the chart (a curve's name at its peak), centred above it.</summary>
internal sealed record PlotText(double X, double Y, string Text, bool Strong = false);

/// <summary>A shaded band across the chart from one x to another (where an enzyme unfolds), with its words.</summary>
internal sealed record PlotBand(double From, double To, Color Color, string? Label = null);

/// <summary>An error bar: its name under it, its mean (a dot) and its ends.</summary>
internal sealed record PlotBar(string Name, double Mean, double Low, double High, Color Color);

internal sealed class LabPlot : FrameworkElement
{
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(LabPlot),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty Text2BrushProperty = DependencyProperty.Register(nameof(Text2Brush), typeof(Brush), typeof(LabPlot),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(LabPlot),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CardBrushProperty = DependencyProperty.Register(nameof(CardBrush), typeof(Brush), typeof(LabPlot),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface Face = new(K.TextFace, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Strong = new(K.TextFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush Text2Brush { get => (Brush)GetValue(Text2BrushProperty); set => SetValue(Text2BrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush CardBrush { get => (Brush)GetValue(CardBrushProperty); set => SetValue(CardBrushProperty, value); }

    public string XLabel { get; set; } = "";
    public string YLabel { get; set; } = "";
    /// <summary>The axes' ranges (null: from what is drawn; y from 0 unless something is below it).</summary>
    public double? XMin { get; set; }
    public double? XMax { get; set; }
    public double? YMin { get; set; }
    public double? YMax { get; set; }
    /// <summary>No numbers on the y axis (the energy diagram's free energy is illustrative).</summary>
    public bool HideYTicks { get; set; }
    /// <summary>No numbers on the x axis (reaction progress).</summary>
    public bool HideXTicks { get; set; }

    public List<PlotLine> Lines { get; } = new();
    public List<PlotMark> Marks { get; } = new();
    public List<PlotGuide> Guides { get; } = new();
    public List<PlotBar> Bars { get; } = new();
    public List<PlotText> Texts { get; } = new();
    public List<PlotBand> Bands { get; } = new();

    public LabPlot()
    {
        SetResourceReference(TextBrushProperty, B.Text);
        SetResourceReference(Text2BrushProperty, B.Text2);
        SetResourceReference(GridBrushProperty, B.Divider);
        SetResourceReference(CardBrushProperty, B.Solid);
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        MinHeight = 120;
    }

    /// <summary>Everything cleared, for the caller to fill again.</summary>
    public void Clear()
    {
        Lines.Clear();
        Marks.Clear();
        Guides.Clear();
        Bars.Clear();
        Texts.Clear();
        Bands.Clear();
    }

    /// <summary>Drawn again with what it now holds.</summary>
    public void Redraw() => InvalidateVisual();

    private FormattedText Words(string s, double size, Brush brush, bool strong = false) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, strong ? Strong : Face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Round tick values (1, 2, 5 × 10ⁿ) across a range, about <paramref name="count"/> of them.</summary>
    public static List<double> Ticks(double lo, double hi, int count = 5)
    {
        var list = new List<double>();
        if (!(hi > lo) || !double.IsFinite(lo) || !double.IsFinite(hi)) return list;
        var raw = (hi - lo) / count;
        var p = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var step = new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * p).First(s => s >= raw);
        if (!(step > 0) || !double.IsFinite(step)) return list;
        for (var v = Math.Ceiling(lo / step - 1e-9) * step; v <= hi + step * 1e-9 && list.Count < 50; v += step) list.Add(Math.Abs(v) < step * 1e-9 ? 0 : v);
        return list;
    }

    /// <summary>A tick's words: "0", "2.5", "1,000", "10⁴".</summary>
    public static string TickText(double v)
    {
        var a = Math.Abs(v);
        if (a != 0 && (a >= 1e5 || a < 1e-3)) return LabNumbers.Sig(v, 2, trim: true);
        return LabNumbers.Minus(v.ToString(a >= 1000 ? "N0" : "0.###", Inv));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));
        if (size.Width < 60 || size.Height < 60) return;

        // ---- the ranges ----
        var xs = Lines.SelectMany(l => l.Points.Select(p => p.X)).Concat(Marks.Select(m => m.X)).Concat(Guides.Where(g => !g.Horizontal).Select(g => g.At)).ToList();
        var ys = Lines.SelectMany(l => l.Points.Select(p => p.Y)).Concat(Marks.Select(m => m.Y)).Concat(Guides.Where(g => g.Horizontal).Select(g => g.At))
            .Concat(Bars.SelectMany(b => new[] { b.Low, b.High })).Where(double.IsFinite).ToList();
        double x0 = XMin ?? (xs.Count > 0 ? Math.Min(0, xs.Min()) : 0), x1 = XMax ?? (xs.Count > 0 ? xs.Max() : 1);
        double y0 = YMin ?? (ys.Count > 0 ? Math.Min(0, ys.Min()) : 0), y1 = YMax ?? (ys.Count > 0 ? ys.Max() * 1.08 : 1);
        if (Bars.Count > 0 && YMin == null && ys.Count > 0)
        {
            // (error bars: the axis round the bars, not from 0, so their overlap shows)
            var lo = ys.Min();
            var hi = ys.Max();
            var pad = Math.Max((hi - lo) * 0.25, Math.Abs(hi) * 0.02 + 1e-9);
            y0 = lo - pad;
            y1 = hi + pad;
        }
        if (!(x1 > x0)) x1 = x0 + 1;
        if (!(y1 > y0)) y1 = y0 + 1;
        var yTicks = Ticks(y0, y1, size.Height < 160 ? 3 : 4);
        if (YMax == null && yTicks.Count > 0 && Bars.Count == 0) y1 = Math.Max(y1, yTicks[^1]);
        if (YMin == null && yTicks.Count > 0 && Bars.Count > 0) { y0 = Math.Min(y0, yTicks[0]); y1 = Math.Max(y1, yTicks[^1]); }

        // ---- the frame ----
        var tickWidth = HideYTicks ? 0 : yTicks.Select(t => Words(TickText(t), 11, Text2Brush).Width).DefaultIfEmpty(0).Max();
        var left = 14 + (YLabel.Length > 0 ? 16 : 0) + tickWidth + 6;
        var bottom = (HideXTicks && Bars.Count == 0 ? 6 : 20) + (XLabel.Length > 0 ? 16 : 0);
        var legend = Lines.Where(l => l.Name != null).ToList();
        var top = legend.Count > 0 ? 24.0 : 10.0;
        var plot = new Rect(left, top, Math.Max(10, size.Width - left - 12), Math.Max(10, size.Height - top - bottom));
        double X(double v) => plot.Left + (v - x0) / (x1 - x0) * plot.Width;
        double Y(double v) => plot.Bottom - (v - y0) / (y1 - y0) * plot.Height;

        var grid = new Pen(GridBrush, 1);
        grid.Freeze();
        var axis = new Pen(Faded(Text2Brush, 0.6), 1);
        axis.Freeze();
        foreach (var t in yTicks)
        {
            var y = Math.Round(Y(t)) + 0.5;
            if (y < plot.Top - 1 || y > plot.Bottom + 1) continue;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            if (HideYTicks) continue;
            var ft = Words(TickText(t), 11, Text2Brush);
            dc.DrawText(ft, new Point(plot.Left - 6 - ft.Width, y - ft.Height / 2));
        }
        if (Bars.Count == 0 && !HideXTicks)
            foreach (var t in Ticks(x0, x1, Math.Max(3, (int)(plot.Width / 70))))
            {
                var x = Math.Round(X(t)) + 0.5;
                dc.DrawLine(axis, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
                var ft = Words(TickText(t), 11, Text2Brush);
                dc.DrawText(ft, new Point(Math.Clamp(x - ft.Width / 2, plot.Left - 8, size.Width - ft.Width - 2), plot.Bottom + 5));
            }
        dc.DrawLine(axis, new Point(plot.Left + 0.5, plot.Top), new Point(plot.Left + 0.5, plot.Bottom));
        dc.DrawLine(axis, new Point(plot.Left, Math.Round(plot.Bottom) + 0.5), new Point(plot.Right, Math.Round(plot.Bottom) + 0.5));
        if (XLabel.Length > 0)
        {
            var ft = Words(XLabel, 11.5, Text2Brush);
            ft.MaxTextWidth = Math.Max(10, plot.Width);
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(ft, new Point(plot.Left + (plot.Width - ft.Width) / 2, size.Height - ft.Height - 1));
        }
        if (YLabel.Length > 0)
        {
            var ft = Words(YLabel, 11.5, Text2Brush);
            ft.MaxTextWidth = Math.Max(10, plot.Height);
            ft.MaxLineCount = 1;
            ft.Trimming = TextTrimming.CharacterEllipsis;
            dc.PushTransform(new RotateTransform(-90, 0, 0));
            dc.DrawText(ft, new Point(-(plot.Top + plot.Height / 2 + ft.Width / 2), 1));
            dc.Pop();
        }

        dc.PushClip(new RectangleGeometry(new Rect(plot.Left - 6, plot.Top - 8, plot.Width + 12, plot.Height + 14)));
        // ---- bands ----
        foreach (var b in Bands)
        {
            double bx0 = Math.Clamp(X(Math.Min(b.From, b.To)), plot.Left, plot.Right), bx1 = Math.Clamp(X(Math.Max(b.From, b.To)), plot.Left, plot.Right);
            if (bx1 - bx0 < 0.5) continue;
            dc.DrawRectangle(new SolidColorBrush(b.Color), null, new Rect(bx0, plot.Top, bx1 - bx0, plot.Height));
            if (string.IsNullOrEmpty(b.Label)) continue;
            var ft = Words(b.Label, 10.5, Text2Brush);
            ft.MaxTextWidth = Math.Max(10, bx1 - bx0 - 6);
            ft.MaxLineCount = 2;
            ft.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(ft, new Point(bx0 + 3, plot.Bottom - ft.Height - 3));
        }
        // ---- guides ----
        foreach (var g in Guides)
        {
            var brush = g.Color is { } gc ? (Brush)new SolidColorBrush(gc) : Text2Brush;
            var pen = new Pen(Faded(brush, 0.7), 1) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) };
            var ft = Words(g.Label, 11, brush);
            if (g.Horizontal)
            {
                var y = Y(g.At);
                dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
                dc.DrawText(ft, new Point(plot.Right - ft.Width - 4, y - ft.Height - 1));
            }
            else
            {
                var x = X(g.At);
                dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                dc.DrawText(ft, new Point(Math.Min(x + 4, plot.Right - ft.Width - 2), plot.Top + 2));
            }
        }
        // ---- curves ----
        foreach (var l in Lines)
        {
            if (l.Points.Count < 2) continue;
            var pen = new Pen(new SolidColorBrush(l.Color), l.Width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (l.Dashed) pen.DashStyle = new DashStyle(new[] { 3.0, 2.2 }, 0);
            pen.Freeze();
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(X(l.Points[0].X), Y(l.Points[0].Y)), false, false);
                ctx.PolyLineTo(l.Points.Skip(1).Select(p => new Point(X(p.X), Math.Clamp(Y(p.Y), -1e4, 1e4))).ToList(), true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
        // ---- error bars ----
        if (Bars.Count > 0)
        {
            var slot = plot.Width / Bars.Count;
            for (var i = 0; i < Bars.Count; i++)
            {
                var b = Bars[i];
                var x = plot.Left + slot * (i + 0.5);
                var brush = new SolidColorBrush(b.Color);
                var pen = new Pen(brush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, new Point(x, Y(b.Low)), new Point(x, Y(b.High)));
                dc.DrawLine(pen, new Point(x - 8, Y(b.Low)), new Point(x + 8, Y(b.Low)));
                dc.DrawLine(pen, new Point(x - 8, Y(b.High)), new Point(x + 8, Y(b.High)));
                dc.DrawEllipse(brush, new Pen(CardBrush, 2), new Point(x, Y(b.Mean)), 5, 5);
            }
        }
        // ---- words at points ----
        foreach (var t in Texts)
        {
            var ft = Words(t.Text, 10.5, t.Strong ? TextBrush : Text2Brush, t.Strong);
            var at = new Point(Math.Clamp(X(t.X) - ft.Width / 2, plot.Left + 2, plot.Right - ft.Width - 2), Math.Max(plot.Top - 6, Y(t.Y) - ft.Height - 2));
            dc.DrawText(ft, at);
        }
        // ---- marks ----
        foreach (var m in Marks)
        {
            var p = new Point(X(m.X), Y(m.Y));
            dc.DrawEllipse(new SolidColorBrush(m.Color), new Pen(CardBrush, 2), p, 5.5, 5.5);
            if (string.IsNullOrEmpty(m.Label)) continue;
            var ft = Words(m.Label, 12, TextBrush, strong: true);
            var at = new Point(p.X + 9, p.Y - ft.Height - 4);
            if (at.X + ft.Width > plot.Right) at.X = p.X - 9 - ft.Width;
            if (at.Y < plot.Top) at.Y = p.Y + 6;
            dc.DrawRoundedRectangle(Faded(CardBrush, 0.85), null, new Rect(at.X - 4, at.Y - 1, ft.Width + 8, ft.Height + 2), 4, 4);
            dc.DrawText(ft, at);
        }
        dc.Pop();
        if (Bars.Count > 0)
        {
            var slot = plot.Width / Bars.Count;
            for (var i = 0; i < Bars.Count; i++)
            {
                var ft = Words(Bars[i].Name, 11, Text2Brush);
                dc.DrawText(ft, new Point(plot.Left + slot * (i + 0.5) - ft.Width / 2, plot.Bottom + 4));
            }
        }

        // ---- legend ----
        var lx = plot.Right;
        foreach (var l in legend.AsEnumerable().Reverse())
        {
            var ft = Words(l.Name!, 11, Text2Brush);
            lx -= ft.Width;
            dc.DrawText(ft, new Point(lx, 4));
            var pen = new Pen(new SolidColorBrush(l.Color), 2.2);
            if (l.Dashed) pen.DashStyle = new DashStyle(new[] { 2.0, 1.5 }, 0);
            dc.DrawLine(pen, new Point(lx - 22, 4 + ft.Height / 2), new Point(lx - 5, 4 + ft.Height / 2));
            lx -= 34;
        }
    }

    private static Brush Faded(Brush b, double opacity)
    {
        var c = b.Clone();
        c.Opacity = opacity;
        c.Freeze();
        return c;
    }
}
