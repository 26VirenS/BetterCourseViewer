using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;

namespace Simpl.Tools;

/// <summary>The scale and middle a drawing is shown at.</summary>
internal readonly record struct LFit(double Scale, Point Centre);

/// <summary>
/// A structure drawn (the Mac's LewisStructureCanvas): its bonds (one to three lines), its atoms, their lone electrons as
/// dots (in pairs; an odd one alone, orange) where the bonds leave room, their formal charges small beside them, and
/// brackets with charges round ions. Drawn in the theme's text colours, so light and dark follow Settings.
/// </summary>
internal class LewisCanvas : FrameworkElement
{
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(LewisCanvas),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty Text2BrushProperty = DependencyProperty.Register(nameof(Text2Brush), typeof(Brush), typeof(LewisCanvas),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(LewisCanvas),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface Bold = new(K.DisplayFace, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush Text2Brush { get => (Brush)GetValue(Text2BrushProperty); set => SetValue(Text2BrushProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    public LGraph Graph { get; set; } = new();
    public LStructure? Structure { get; set; }
    public LResult Result { get; set; } = new();
    /// <summary>The largest scale it is drawn at (smaller when it would not fit).</summary>
    public double MaxScale { get; set; } = 1;
    public int? Selected { get; set; }
    /// <summary>A bond being drawn: from an atom to the pointer.</summary>
    public (Point From, Point To)? Preview { get; set; }

    public LewisCanvas()
    {
        SetResourceReference(TextBrushProperty, B.Text);
        SetResourceReference(Text2BrushProperty, B.Text2);
        SetResourceReference(AccentBrushProperty, B.Accent);
        ClipToBounds = true;
    }

    /// <summary>The view it is drawn at: fitted to the drawing (the editor holds its own).</summary>
    protected virtual LFit View(Size size) => Fit(Graph, size, MaxScale);

    /// <summary>
    /// The scale and middle that fit a drawing in a size: drawn round its own origin at full scale when it fits there (so
    /// drawing atoms one by one doesn't shift it), otherwise centred on its atoms and shrunk to fit.
    /// </summary>
    public static LFit Fit(LGraph g, Size size, double maxScale)
    {
        if (g.Atoms.Count == 0 || size.Width <= 1 || size.Height <= 1) return new LFit(maxScale, new Point());
        // (room round the atoms for their lone electrons, brackets and charges)
        const double pad = 52;
        double x0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
        foreach (var a in g.Atoms)
        {
            x0 = Math.Min(x0, a.Pos.X - pad);
            x1 = Math.Max(x1, a.Pos.X + pad);
            y0 = Math.Min(y0, a.Pos.Y - pad);
            y1 = Math.Max(y1, a.Pos.Y + pad);
        }
        x1 += 14;
        double halfW = size.Width / 2 / maxScale, halfH = size.Height / 2 / maxScale;
        if (x0 >= -halfW && x1 <= halfW && y0 >= -halfH && y1 <= halfH) return new LFit(maxScale, new Point());
        double sx = size.Width / (x1 - x0), sy = size.Height / (y1 - y0);
        return new LFit(Math.Min(maxScale, Math.Min(sx, sy)), new Point((x0 + x1) / 2, (y0 + y1) / 2));
    }

    /// <summary>
    /// The editor's view: as large as fits, up to half again full size, so a small molecule isn't lost in a big space;
    /// drawn round the origin while that fits, so drawing atoms one by one doesn't shift it.
    /// </summary>
    public static LFit EditorFit(LGraph g, Size size)
    {
        var big = Fit(g, size, 1.5);
        if (big.Centre == new Point()) return big;
        return Fit(g, size, 1);
    }

    public static Point ToView(Point p, Size size, double scale, Point centre) =>
        new(size.Width / 2 + (p.X - centre.X) * scale, size.Height / 2 + (p.Y - centre.Y) * scale);

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        // (the whole of it takes the pointer, not only what is drawn)
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));
        var f = View(size);
        Draw(dc, size, f.Scale, f.Centre);
    }

    private static Brush Faded(Brush b, double opacity)
    {
        var c = b.Clone();
        c.Opacity = opacity;
        c.Freeze();
        return c;
    }

    private void Text(DrawingContext dc, string s, double size, Brush brush, Point at)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Bold, Math.Max(1, size), brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
    }

    private void Draw(DrawingContext dc, Size size, double scale, Point centre)
    {
        var g = Graph;
        var s = Structure;
        var r = Result;
        var atomR = 16 * scale;
        var line = Math.Max(scale, 0.6);
        Point? At(int id) => g.Atom(id) is { } a ? ToView(a.Pos, size, scale, centre) : null;
        if (Preview is { } pv)
        {
            var pen = new Pen(Faded(AccentBrush, 0.7), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, DashCap = PenLineCap.Round, DashStyle = new DashStyle(new[] { 2.5, 2 }, 0) };
            dc.DrawLine(pen, ToView(pv.From, size, scale, centre), ToView(pv.To, size, scale, centre));
        }
        // (bonds)
        var bondPen = new Pen(Faded(TextBrush, 0.75), 2.4 * line) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        bondPen.Freeze();
        foreach (var b in g.Bonds)
        {
            if (At(b.A) is not { } p || At(b.B) is not { } q) continue;
            double dx = q.X - p.X, dy = q.Y - p.Y;
            var len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.01);
            double ux = dx / len, uy = dy / len;
            var order = s != null && s.Orders.TryGetValue(b.Id, out var so) ? so : b.Order;
            var gap = 4.5 * line;
            var offsets = order <= 1 ? new[] { 0.0 } : order == 2 ? new[] { -gap, gap } : new[] { -gap * 1.6, 0, gap * 1.6 };
            foreach (var o in offsets)
            {
                double ax = ux * atomR - uy * o, ay = uy * atomR + ux * o;
                double bx = -ux * atomR - uy * o, by = -uy * atomR + ux * o;
                dc.DrawLine(bondPen, new Point(p.X + ax, p.Y + ay), new Point(q.X + bx, q.Y + by));
            }
        }
        // (atoms, their formal charges and their lone electrons)
        var yellow = new SolidColorBrush(ChemKit.Yellow);
        var orange = new SolidColorBrush(ChemKit.Orange);
        foreach (var a in g.Atoms)
        {
            var p = ToView(a.Pos, size, scale, centre);
            if (Selected == a.Id) dc.DrawEllipse(Faded(AccentBrush, 0.18), null, p, 22, 22);
            var tint = LChem.ColorOf(a.Symbol) is { } c ? new SolidColorBrush(c) : TextBrush;
            Text(dc, a.Symbol, 30 * scale, tint, p);
            if (s == null) continue;
            var alone = r.Ions.Any(i => i.Atoms.Count == 1 && i.Atoms[0] == a.Id && i.Charge != 0);
            if (s.Formal.TryGetValue(a.Id, out var fc) && fc != 0 && !alone)
            {
                var t = (fc > 0 ? "+" : "−") + (Math.Abs(fc) > 1 ? Math.Abs(fc).ToString(CultureInfo.InvariantCulture) : "");
                Text(dc, t, 13 * Math.Max(scale, 0.7), Text2Brush, new Point(p.X + 20 * scale, p.Y - 19 * scale));
            }
            var lone = s.LoneOf(a.Id);
            if (lone <= 0) continue;
            var bondAngles = new List<double>();
            foreach (var b in g.Bonds)
            {
                if (b.A != a.Id && b.B != a.Id) continue;
                if (g.Atom(b.Other(a.Id)) is { } o) bondAngles.Add(Math.Atan2(o.Pos.Y - a.Pos.Y, o.Pos.X - a.Pos.X));
            }
            var angles = FreeAngles(lone / 2 + lone % 2, bondAngles);
            for (var k = 0; k < angles.Count; k++)
            {
                var single = k == angles.Count - 1 && lone % 2 == 1;
                var ang = angles[k];
                var d = 25 * scale;
                var m = new Point(p.X + d * Math.Cos(ang), p.Y + d * Math.Sin(ang));
                var dot = 3 * line;
                var offs = single ? new[] { 0.0 } : new[] { -5 * line, 5 * line };
                foreach (var o in offs)
                    dc.DrawEllipse(single ? orange : yellow, null, new Point(m.X - Math.Sin(ang) * o, m.Y + Math.Cos(ang) * o), dot, dot);
            }
        }
        // (brackets round ions and charged molecules, the charge at the top right)
        var bracket = new Pen(Text2Brush, 1.6);
        foreach (var ion in r.Ions)
        {
            if (ion.Charge == 0) continue;
            var pts = ion.Atoms.Select(At).OfType<Point>().ToList();
            if (pts.Count == 0) continue;
            var pad = 36 * scale;
            var rect = new Rect(pts.Min(x => x.X) - pad, pts.Min(x => x.Y) - pad, pts.Max(x => x.X) - pts.Min(x => x.X) + 2 * pad, pts.Max(x => x.Y) - pts.Min(x => x.Y) + 2 * pad);
            if (ion.Atoms.Count > 1 || ion.Charge < 0)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(rect.Left + 7, rect.Top), false, false);
                    ctx.LineTo(new Point(rect.Left, rect.Top), true, false);
                    ctx.LineTo(new Point(rect.Left, rect.Bottom), true, false);
                    ctx.LineTo(new Point(rect.Left + 7, rect.Bottom), true, false);
                    ctx.BeginFigure(new Point(rect.Right - 7, rect.Top), false, false);
                    ctx.LineTo(new Point(rect.Right, rect.Top), true, false);
                    ctx.LineTo(new Point(rect.Right, rect.Bottom), true, false);
                    ctx.LineTo(new Point(rect.Right - 7, rect.Bottom), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, bracket, geo);
            }
            // (plain digits and signs: superscript ones draw too small to read)
            var q = ion.Charge;
            var qt = (Math.Abs(q) > 1 ? Math.Abs(q).ToString(CultureInfo.InvariantCulture) : "") + (q > 0 ? "+" : "−");
            Text(dc, qt, 19 * Math.Max(scale, 0.7), TextBrush, new Point(rect.Right + 10, rect.Top + 4));
        }
    }

    /// <summary>Directions for lone electrons round an atom: as far from its bonds, and from each other, as they can be.</summary>
    public static List<double> FreeAngles(int count, List<double> bonds)
    {
        var taken = new List<double>(bonds);
        var outs = new List<double>();
        var candidates = Enumerable.Range(0, 24).Select(i => i * Math.PI / 12 - Math.PI / 2).ToArray();
        for (var n = 0; n < count; n++)
        {
            var bestAngle = candidates[0];
            var bestGap = -1.0;
            foreach (var c in candidates)
            {
                var gap = taken.Count == 0 ? Math.PI : taken.Min(t => Math.Abs(Math.Atan2(Math.Sin(c - t), Math.Cos(c - t))));
                if (gap > bestGap)
                {
                    bestGap = gap;
                    bestAngle = c;
                }
            }
            outs.Add(bestAngle);
            taken.Add(bestAngle);
        }
        return outs;
    }
}

/// <summary>
/// The drawing, editable (the Mac's LewisEditorCanvas): in Draw, a click places an atom (bonded to the one selected) or
/// selects one, a drag from an atom bonds it to another (or to a new atom where it is let go), a click on a bond cycles
/// its order; in Move, atoms are dragged; in Erase, a click removes an atom or a bond. A change to the quick way's
/// drawing makes it a drawing.
/// </summary>
internal sealed class LewisEditor : LewisCanvas
{
    private readonly LewisState st;
    /// <summary>The view held still while the pointer is down, so what is under it doesn't move.</summary>
    private LFit? held;
    private Point? downAt;
    private int? dragFrom;
    private int? moving;

    public LewisEditor(LewisState state)
    {
        st = state;
        Focusable = false;
        System.Windows.Automation.AutomationProperties.SetName(this, "Lewis structure");
    }

    /// <summary>The quick way's drawing (not the student's own yet).</summary>
    public bool Editable { get; set; }

    protected override LFit View(Size size) => held ?? EditorFit(Graph, size);

    /// <summary>From the view's points to the drawing's.</summary>
    private Point Local(Point p)
    {
        var f = View(RenderSize);
        return new Point(f.Centre.X + (p.X - RenderSize.Width / 2) / f.Scale, f.Centre.Y + (p.Y - RenderSize.Height / 2) / f.Scale);
    }

    private int? AtomAt(Point p)
    {
        (int Id, double D)? best = null;
        foreach (var a in Graph.Atoms)
        {
            var d = Math.Sqrt((a.Pos.X - p.X) * (a.Pos.X - p.X) + (a.Pos.Y - p.Y) * (a.Pos.Y - p.Y));
            if (d < 22 && (best == null || d < best.Value.D)) best = (a.Id, d);
        }
        return best?.Id;
    }

    private int? BondAt(Point p)
    {
        foreach (var b in Graph.Bonds)
        {
            if (Graph.Atom(b.A)?.Pos is not { } x || Graph.Atom(b.B)?.Pos is not { } y) continue;
            double dx = y.X - x.X, dy = y.Y - x.Y;
            var l2 = Math.Max(dx * dx + dy * dy, 0.01);
            var dot = (p.X - x.X) * dx + (p.Y - x.Y) * dy;
            var t = Math.Clamp(dot / l2, 0, 1);
            double cx = x.X + t * dx, cy = x.Y + t * dy;
            if (Math.Sqrt((cx - p.X) * (cx - p.X) + (cy - p.Y) * (cy - p.Y)) < 8 && t > 0.15 && t < 0.85) return b.Id;
        }
        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        held = EditorFit(Graph, RenderSize);
        downAt = e.GetPosition(this);
        dragFrom = null;
        moving = null;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (downAt is not { } from0 || !IsMouseCaptured) return;
        var at = e.GetPosition(this);
        if ((at - from0).Length <= 4) return;
        Point start = Local(from0), now = Local(at);
        if (st.Tool == LTool.Move || !Editable)
        {
            moving ??= AtomAt(start);
            if (moving is { } id && Graph.Atom(id) is { } a)
            {
                a.Pos = now;
                st.Move();
            }
        }
        else if (st.Tool == LTool.Draw)
        {
            dragFrom ??= AtomAt(start);
            if (dragFrom is { } f && Graph.Atom(f) is { } a)
            {
                Preview = (a.Pos, now);
                InvalidateVisual();
            }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (downAt is not { } from0) return;
        var at = e.GetPosition(this);
        Point start = Local(from0), end = Local(at);
        var click = (at - from0).Length <= 4;
        var from = dragFrom;
        var wasMoving = moving != null;
        Reset();
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
        Ended(start, end, click, from, wasMoving);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (downAt == null) return;
        // (the pointer taken away mid-press — another window, a menu: nothing is done)
        var wasMoving = moving != null;
        Reset();
        if (wasMoving) st.Change(); else InvalidateVisual();
    }

    private void Reset()
    {
        downAt = null;
        dragFrom = null;
        moving = null;
        held = null;
        Preview = null;
    }

    private void Ended(Point start, Point end, bool click, int? from, bool wasMoving)
    {
        // (an atom let go where it was moved to: the rest — the resonance structures' drawings — follow)
        if (wasMoving) { st.Change(); return; }
        var g = Graph;
        if (click)
        {
            if (AtomAt(start) is { } id)
            {
                if (st.Tool == LTool.Erase && Editable)
                {
                    g.Remove(id);
                    if (st.Selected == id) st.Selected = null;
                    st.Edited();
                }
                else
                {
                    st.Selected = st.Selected == id ? null : id;
                    st.Change();
                }
                return;
            }
            if (BondAt(start) is { } bid && g.Bonds.FindIndex(b => b.Id == bid) is var i and >= 0)
            {
                if (st.Tool == LTool.Erase && Editable)
                {
                    g.Bonds.RemoveAt(i);
                    st.Edited();
                    return;
                }
                // (a bond's order cycled: the orders the student's own from now on, starting from those shown)
                if (!g.Manual)
                {
                    foreach (var b in g.Bonds) b.Order = Structure != null && Structure.Orders.TryGetValue(b.Id, out var o) ? o : b.Order;
                    g.Manual = true;
                }
                g.Bonds[i].Order = g.Bonds[i].Order % 3 + 1;
                st.Edited();
                return;
            }
            if (st.Tool == LTool.Draw)
            {
                var anchor = st.Selected;
                var nid = g.Add(st.Element, start);
                if (anchor is { } an && g.Atom(an) != null) g.Connect(an, nid);
                st.Selected = nid;
                st.Edited();
            }
            else
            {
                st.Selected = null;
                st.Change();
            }
            return;
        }
        if (st.Tool != LTool.Draw || from is not { } f)
        {
            InvalidateVisual();
            return;
        }
        if (AtomAt(end) is { } to)
        {
            if (to != f)
            {
                g.Connect(f, to);
                st.Edited();
            }
            else InvalidateVisual();
        }
        else
        {
            var nid = g.Add(st.Element, end);
            g.Connect(f, nid);
            st.Selected = nid;
            st.Edited();
        }
    }
}
