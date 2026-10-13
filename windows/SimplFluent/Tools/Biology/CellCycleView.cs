using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;

namespace Simpl.Tools;

// The Cell cycle page's drawings (spec §4.6), the Mac's CellCycleWheel and CyclinChart: a wheel of a typical 24 h human
// cell's cycle — G1 about 11 h, S about 8 h, G2 about 4 h, M about 1 h (Alberts), each arc in proportion, M at the top,
// clockwise — with interphase bracketed, G0 as a side branch off G1, and the three checkpoints as gates: a bar across the
// ring and a pill outside it (G1/S, the restriction point; G2/M; the spindle assembly checkpoint in M). A press on an arc,
// a gate or G0 picks it (its card shows at the right; again puts it away); ← → walk round them. With "Cancer" on, the
// gates are drawn broken, in red. Under it, the cyclins' levels over the same 24 h.

/// <summary>The wheel's parts in the order ← → walk them, clockwise from the top, and their colours (the Mac's).</summary>
internal static class CycleOrder
{
    public static readonly string[] Ids = { "spindle", "g1", "g0", "g1s", "s", "g2", "g2m", "m" };

    public static string Name(string id) => id switch
    {
        "g0" => DivisionModel.G0.Title,
        _ when DivisionModel.CyclePhases.FirstOrDefault(p => p.Id == id) is { } p => p.Title,
        _ when DivisionModel.Checkpoints.FirstOrDefault(c => c.Id == id) is { } c => c.Name,
        _ => id,
    };

    public static Color PhaseColor(string id, bool dark = false) => id switch
    {
        "g1" => K.Hex("#14b8a6"),
        "s" => K.Hex("#7d5cf5"),
        "g2" => K.Hex("#3b82f6"),
        "m" => K.Hex("#f2994a"),
        _ => K.Hex("#8e8e93"),
    };

    /// <summary>Where each gate's pill sits (the two near the top spread apart so both can be pressed).</summary>
    public static double PillAngle(string id) => id switch
    {
        "g2m" => 330,
        "spindle" => 22,
        _ => 172.5,
    };
}

internal sealed class CellCycleWheel : FrameworkElement
{
    private static readonly Typeface Bold = new(K.TextFace, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface Strong = new(K.TextFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface Plain = new(K.TextFace, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Dictionary<string, Rect> pills = new();
    private string? hover;

    /// <summary>A part pressed (a phase's or a checkpoint's id, or G0's).</summary>
    public event Action<string>? Picked;

    public CellCycleWheel()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        AutomationProperties.SetName(this, "The cell cycle: G1 11 hours, S 8 hours, G2 4 hours, M 1 hour, with G0 off G1 and three checkpoints. Left and right arrows pick a part.");
        void Changed() => InvalidateVisual();
        Loaded += (_, _) => { Appearance.Changed -= Changed; Appearance.Changed += Changed; CellDivisionModel.Changed -= Changed; CellDivisionModel.Changed += Changed; };
        Unloaded += (_, _) => { Appearance.Changed -= Changed; CellDivisionModel.Changed -= Changed; };
    }

    private (Point C, double R, double Inner, Point G0) Geometry()
    {
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var r = Math.Max(Math.Min(ActualWidth, ActualHeight) * 0.36, 60);
        return (c, r, r * 0.66, At(c, 75, r * 1.42));
    }

    /// <summary>A point on the wheel: degrees clockwise from the top, at a radius.</summary>
    private static Point At(Point c, double deg, double r)
    {
        var a = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
    }

    private FormattedText Text(string s, double size, Brush brush, Typeface face) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static StreamGeometry Sector(Point c, double a, double b, double inner, double outer)
    {
        var g = new StreamGeometry();
        var steps = Math.Max((int)(Math.Abs(b - a) / 2), 2);
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(c, a, outer), true, true);
            var pts = new List<Point>();
            for (var i = 1; i <= steps; i++) pts.Add(At(c, a + (b - a) * i / steps, outer));
            for (var i = steps; i >= 0; i--) pts.Add(At(c, a + (b - a) * i / steps, inner));
            ctx.PolyLineTo(pts, true, true);
        }
        g.Freeze();
        return g;
    }

    private static StreamGeometry Arc(Point c, double a, double b, double r)
    {
        var g = new StreamGeometry();
        var steps = Math.Max((int)(Math.Abs(b - a) / 2), 2);
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(c, a, r), false, false);
            var pts = new List<Point>();
            for (var i = 1; i <= steps; i++) pts.Add(At(c, a + (b - a) * i / steps, r));
            ctx.PolyLineTo(pts, true, true);
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        pills.Clear();
        if (ActualWidth < 80 || ActualHeight < 80) return;
        var dark = Appearance.Dark;
        var (c, r, inner, g0) = Geometry();
        var selected = CellDivisionModel.CycleSelected;
        var cancer = CellDivisionModel.Cancer;
        var ink = dark ? Colors.White : Colors.Black;
        var text = DivColors.Brush(dark ? Color.FromRgb(0xf5, 0xf5, 0xf7) : Color.FromRgb(0x1d, 0x1d, 0x1f));
        var text2 = DivColors.Brush(dark ? Color.FromRgb(0xae, 0xae, 0xb2) : Color.FromRgb(0x6e, 0x6e, 0x73));
        var red = K.Hex(dark ? "#ff6961" : "#d63a34");
        var accent = Appearance.Accent;
        var arcs = DivisionModel.CycleArcs;

        // (the arcs, in proportion; the one chosen stands out)
        foreach (var (phase, start, end) in arcs)
        {
            var on = selected == phase.Id;
            var alpha = on ? 0.95 : (hover == phase.Id ? 0.88 : 0.75);
            dc.DrawGeometry(DivColors.Brush(CycleOrder.PhaseColor(phase.Id), alpha), null, Sector(c, start, end, inner, on ? r + 6 : r));
        }
        // (interphase and the mitotic phase, bracketed outside)
        var edge = DivColors.Pen(ink, 0.35, 1.5);
        dc.DrawGeometry(null, edge, Arc(c, arcs[0].Start + 1, arcs[2].End - 1, r + 16));
        dc.DrawGeometry(null, edge, Arc(c, arcs[3].Start + 0.5, arcs[3].End - 0.5, r + 16));
        // (G0: off G1 and back)
        var branch = new StreamGeometry();
        using (var ctx = branch.Open())
        {
            ctx.BeginFigure(At(c, 62, r + 2), false, false);
            ctx.QuadraticBezierTo(At(c, 64, r * 1.3), new Point(g0.X - 22, g0.Y - 12), true, true);
            ctx.BeginFigure(new Point(g0.X - 20, g0.Y + 14), false, false);
            ctx.QuadraticBezierTo(At(c, 86, r * 1.3), At(c, 88, r + 2), true, true);
        }
        branch.Freeze();
        dc.DrawGeometry(null, DivColors.Pen(ink, 0.45, 1.5, dashed: true), branch);
        var g0on = selected == "g0";
        dc.DrawEllipse(DivColors.Brush(CycleOrder.PhaseColor("g0"), g0on ? 0.5 : (hover == "g0" ? 0.38 : 0.25)), g0on ? DivColors.Pen(accent, 1, 2) : null, g0, 24, 24);
        // (the gates: a bar across the ring at each checkpoint, a leader to its pill when that is moved aside)
        foreach (var cp in DivisionModel.Checkpoints)
        {
            var on = selected == cp.Id;
            var col = cancer ? red : ink;
            var pen = DivColors.Pen(col, on ? 0.95 : 0.7, on ? 5 : 3.5, dashed: cancer);
            dc.DrawLine(pen, At(c, cp.Angle, inner - 6), At(c, cp.Angle, r + 6));
            var b = CycleOrder.PillAngle(cp.Id);
            if (Math.Abs(b - cp.Angle) > 1) dc.DrawLine(DivColors.Pen(ink, 0.4, 1), At(c, cp.Angle, r + 8), At(c, b, r + 30));
        }

        // (the phases' names on the ring; M's outside, it is thin)
        foreach (var (phase, start, end) in arcs)
        {
            var mid = (start + end) / 2;
            var thin = end - start < 30;
            var name = Text(phase.Name, thin ? 13 : 17, thin ? text : DivColors.Brush(Colors.White), Bold);
            var hours = Text($"{phase.Hours.ToString("0", CultureInfo.InvariantCulture)} h", 12, thin ? text2 : DivColors.Brush(Colors.White, 0.92), Plain);
            var p = thin ? At(c, mid - 14, r + 40) : At(c, mid, (r + inner) / 2);
            var h = name.Height + hours.Height;
            dc.DrawText(name, new Point(p.X - name.Width / 2, p.Y - h / 2));
            dc.DrawText(hours, new Point(p.X - hours.Width / 2, p.Y - h / 2 + name.Height));
        }
        var interphase = Text("Interphase · 23 h", 12, text2, Strong);
        var ip = At(c, 232, r + 30);
        dc.DrawText(interphase, new Point(ip.X - interphase.Width / 2, ip.Y - interphase.Height / 2));
        var g0t = Text("G0", 15, text, Bold);
        dc.DrawText(g0t, new Point(g0.X - g0t.Width / 2, g0.Y - g0t.Height / 2));
        var mid1 = Text(cancer ? "Checkpoints failing" : "One cell cycle", 15, cancer ? DivColors.Brush(red) : text, Strong);
        var mid2 = Text(cancer ? "The cell keeps dividing" : "G1 → S → G2 → M", 12, text2, Plain);
        dc.DrawText(mid1, new Point(c.X - mid1.Width / 2, c.Y - mid1.Height));
        dc.DrawText(mid2, new Point(c.X - mid2.Width / 2, c.Y + 1));

        // (each gate's pill: its short name, a shield, pressable)
        foreach (var cp in DivisionModel.Checkpoints)
        {
            var on = selected == cp.Id;
            var label = Text((cancer ? "✕ " : "✓ ") + cp.Short, 12, cancer ? DivColors.Brush(red) : text, Strong);
            var p = At(c, CycleOrder.PillAngle(cp.Id), r + 44);
            var rect = new Rect(p.X - label.Width / 2 - 8, p.Y - label.Height / 2 - 4, label.Width + 16, label.Height + 8);
            var fill = DivColors.Brush(dark ? Color.FromRgb(0x2c, 0x2c, 0x2e) : Colors.White, hover == cp.Id ? 1 : 0.92);
            dc.DrawRoundedRectangle(fill, on ? DivColors.Pen(accent, 1, 2) : DivColors.Pen(ink, 0.15, 1), rect, rect.Height / 2, rect.Height / 2);
            dc.DrawText(label, new Point(p.X - label.Width / 2, p.Y - label.Height / 2));
            pills[cp.Id] = rect;
        }
    }

    /// <summary>What is under a point: a gate's pill, G0, then an arc.</summary>
    private string? HitAt(Point p)
    {
        foreach (var (id, rect) in pills)
        {
            var hit = rect;
            hit.Inflate(4, 4);
            if (hit.Contains(p)) return id;
        }
        var (c, r, inner, g0) = Geometry();
        if ((p - g0).Length < 26) return "g0";
        var d = (p - c).Length;
        if (d < inner - 4 || d > r + 8) return null;
        var deg = Math.Atan2(p.X - c.X, c.Y - p.Y) * 180 / Math.PI;
        var arcs = DivisionModel.CycleArcs;
        while (deg < arcs[0].Start) deg += 360;
        foreach (var (phase, start, end) in arcs)
            if (deg >= start && deg < end) return phase.Id;
        return arcs[^1].Phase.Id;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = HitAt(e.GetPosition(this));
        var cursor = h != null ? Cursors.Hand : Cursors.Arrow;
        if (Cursor != cursor) Cursor = cursor;
        if (h == hover) return;
        hover = h;
        ToolTip = h != null ? CycleOrder.Name(h) : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover == null) return;
        hover = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Focus();
        if (HitAt(e.GetPosition(this)) is { } id)
        {
            // (a second press on what is chosen puts it away)
            if (CellDivisionModel.CycleSelected == id) { CellDivisionModel.CycleSelected = null; CellDivisionModel.Tell(); }
            else Picked?.Invoke(id);
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        var ids = CycleOrder.Ids;
        var i = CellDivisionModel.CycleSelected is { } s ? Array.IndexOf(ids, s) : -1;
        switch (e.Key)
        {
            case Key.Right or Key.Down: Picked?.Invoke(ids[(i + 1) % ids.Length]); break;
            case Key.Left or Key.Up: Picked?.Invoke(ids[(i <= 0 ? ids.Length : i) - 1]); break;
            case Key.Home: Picked?.Invoke(ids[0]); break;
            case Key.End: Picked?.Invoke(ids[^1]); break;
            case Key.Escape when CellDivisionModel.CycleSelected != null:
                CellDivisionModel.CycleSelected = null;
                CellDivisionModel.Tell();
                break;
            default: return;
        }
        e.Handled = true;
    }
}

/// <summary>The cyclins through one cycle (illustrative shapes, spec §4.6): the G1/S cyclin peaking at the G1/S boundary, the
/// M cyclin building through S and G2 and destroyed at anaphase; the CDK level steady. The phases along the bottom, the
/// key at the top right.</summary>
internal sealed class CyclinChart : FrameworkElement
{
    private static readonly Typeface Face = new(K.TextFace, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(K.TextFace, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public CyclinChart()
    {
        Height = 130;
        AutomationProperties.SetName(this, "Cyclin levels: the G1/S cyclin peaks at the start of S phase; the M cyclin builds through S and G2, peaks in M and drops at anaphase; the CDK level stays steady.");
        void Changed() => InvalidateVisual();
        Loaded += (_, _) => { Appearance.Changed -= Changed; Appearance.Changed += Changed; };
        Unloaded += (_, _) => Appearance.Changed -= Changed;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h0 = ActualHeight;
        if (w < 80 || h0 < 40) return;
        var dark = Appearance.Dark;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var grey = dark ? Color.FromRgb(0xae, 0xae, 0xb2) : Color.FromRgb(0x6e, 0x6e, 0x73);
        FormattedText T(string s, Color colour, double size = 11, bool bold = false) =>
            new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? Bold : Face, size, DivColors.Brush(colour), dpi);
        // (the key, at the top right)
        const double keyH = 18;
        var keys = new (Color Colour, string Text, bool Dashed)[] { (CycleOrder.PhaseColor("g1"), "G1/S cyclin", false), (CycleOrder.PhaseColor("m"), "M cyclin", false), (grey, "CDK", true) };
        var x0 = w;
        foreach (var (colour, label, dashed) in keys.Reverse())
        {
            var t = T(label, grey);
            x0 -= t.Width;
            dc.DrawText(t, new Point(x0, 0));
            var len = dashed ? 8 : 16;
            x0 -= len + 5;
            dc.DrawLine(DivColors.Pen(colour, 1, 3), new Point(x0, t.Height / 2), new Point(x0 + len, t.Height / 2));
            x0 -= 14;
        }
        const double band = 16;
        var top = keyH + 2;
        var h = h0 - top - band - 4;
        double X(double t) => t / 24 * w;
        double Y(double v) => top + 2 + h * (1 - v);
        // (the phases along the bottom, G1 from 0 h)
        double start = 0;
        foreach (var p in DivisionModel.CyclePhases)
        {
            var rect = new Rect(X(start), h0 - band, Math.Max(0, X(start + p.Hours) - X(start) - 1), band);
            dc.DrawRoundedRectangle(DivColors.Brush(CycleOrder.PhaseColor(p.Id), 0.75), null, rect, 3, 3);
            if (p.Hours >= 4)
            {
                var t = T(p.Name, Colors.White, 10, bold: true);
                dc.DrawText(t, new Point(rect.X + rect.Width / 2 - t.Width / 2, rect.Y + rect.Height / 2 - t.Height / 2));
            }
            start += p.Hours;
        }
        StreamGeometry Curve(Func<double, double> f)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(X(0), Y(f(0))), false, false);
                var pts = new List<Point>();
                for (var i = 1; i <= 240; i++) { var t = i / 10.0; pts.Add(new Point(X(t), Y(f(t)))); }
                ctx.PolyLineTo(pts, true, true);
            }
            g.Freeze();
            return g;
        }
        dc.DrawGeometry(null, DivColors.Pen(grey, 0.7, 1.5, dashed: true), Curve(_ => 0.82));
        dc.DrawGeometry(null, DivColors.Pen(CycleOrder.PhaseColor("g1"), 1, 2.2), Curve(DivisionModel.CyclinG1S));
        dc.DrawGeometry(null, DivColors.Pen(CycleOrder.PhaseColor("m"), 1, 2.2), Curve(DivisionModel.CyclinM));
    }
}
