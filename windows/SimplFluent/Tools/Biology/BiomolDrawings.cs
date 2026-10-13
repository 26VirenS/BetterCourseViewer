using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;

namespace Simpl.Tools;

// The Biomolecule Builder's flat drawings (spec §4.4), drawn straight onto their element (no element per line): an amino
// acid's R group as a skeletal sketch (its heavy atoms on their best-fit plane, hanging from CA), the polysaccharides as
// hexagon glyphs with their linkages shown on press, and the row of residues under the peptide's sequence where each
// residue's secondary structure is painted by dragging across it. Their ink follows the theme (a resource reference
// that redraws them when the app goes light or dark).

/// <summary>A flat drawing whose ink is the theme's text colour (it redraws when the theme changes).</summary>
internal abstract class BiomolInked : FrameworkElement
{
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush), typeof(BiomolInked),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty Ink2Property = DependencyProperty.Register(nameof(Ink2), typeof(Brush), typeof(BiomolInked),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GroundProperty = DependencyProperty.Register(nameof(Ground), typeof(Brush), typeof(BiomolInked),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public Brush Ink2 { get => (Brush)GetValue(Ink2Property); set => SetValue(Ink2Property, value); }
    /// <summary>What the drawing sits on (an atom's letter is cut out of its bonds with it).</summary>
    public Brush Ground { get => (Brush)GetValue(GroundProperty); set => SetValue(GroundProperty, value); }

    protected BiomolInked()
    {
        SetResourceReference(InkProperty, B.Text);
        SetResourceReference(Ink2Property, B.Text2);
        SetResourceReference(GroundProperty, B.Solid);
    }

    protected static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    protected static readonly Typeface Plain = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    protected static readonly Typeface MonoFace = new(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    protected FormattedText Words(string s, double size, Brush brush, Typeface? face = null) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face ?? Face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>An amino acid's R group as a skeletal sketch (§4.4 Amino acids): bonds as lines (double bonds doubled), N, O
/// and S lettered in their colours, carbons at the corners, and the CA it hangs from as a dot with a stub below.</summary>
internal sealed class BiomolSketchView : BiomolInked
{
    private readonly BiomolSketch sketch;

    public BiomolSketchView(BiomolSketch sketch, double size = 40)
    {
        this.sketch = sketch;
        Width = size;
        Height = size;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (sketch.Atoms.Count == 0) return;
        var (x0, y0, x1, y1) = sketch.Bounds;
        double pad = 5, w = Math.Max(x1 - x0, 0.01), h = Math.Max(y1 - y0, 0.01);
        // (a little room below CA for its stub)
        var s = Math.Min(Math.Min((ActualWidth - 2 * pad) / w, (ActualHeight - 2 * pad - 5) / h), 13);
        double cx = ActualWidth / 2 - (x0 + x1) / 2 * s, cy = (ActualHeight - 5) / 2 + (y0 + y1) / 2 * s;
        Point P(BiomolSketch.Atom a) => new(cx + a.X * s, cy - a.Y * s);
        var pen = new Pen(Ink, 1.3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        foreach (var b in sketch.Bonds)
        {
            Point p = P(sketch.Atoms[b.A]), q = P(sketch.Atoms[b.B]);
            if (b.Order == 1) { dc.DrawLine(pen, p, q); continue; }
            var d = q - p;
            if (d.Length < 0.1) continue;
            var n = new Vector(-d.Y, d.X);
            n.Normalize();
            var off = b.Order == 2 ? new[] { -1.4, 1.4 } : new[] { -2.2, 0, 2.2 };
            foreach (var o in off) dc.DrawLine(pen, p + n * o, q + n * o);
        }
        var dark = Appearance.Dark;
        foreach (var a in sketch.Atoms)
        {
            var p = P(a);
            if (a.Attachment)
            {
                dc.DrawLine(new Pen(Ink2, 1.3) { DashStyle = DashStyles.Dot }, p, p + new Vector(0, 6));
                dc.DrawEllipse(Ink2, null, p, 2.2, 2.2);
                continue;
            }
            if (a.Element == "C") continue;
            var colour = a.Element switch
            {
                "N" => BioPalette.Basic.HexFor(dark),
                "O" => BioPalette.Acidic.HexFor(dark),
                "S" => BioPalette.Guanine.HexFor(dark),
                _ => null,
            };
            var t = Words(a.Element, 9.5, colour != null ? Frozen(K.Hex(colour)) : Ink);
            dc.DrawEllipse(Ground, null, p, 4.6, 4.6);
            dc.DrawText(t, new Point(p.X - t.Width / 2, p.Y - t.Height / 2));
        }
    }
}

/// <summary>
/// A polysaccharide as hexagon glyphs (§4.4 Polysaccharides): each glucose (or N-acetylglucosamine) a hexagon with its
/// ring O as a red dot (at the top, or the bottom where every other sugar is flipped), the chain's links with the
/// glycosidic O, branches, H-bonds dashed, and a gap for the residues left out; a link's linkage ("α(1→4)") shows when it
/// is pressed (or chosen with ← →), and under the pointer it thickens.
/// </summary>
internal sealed class BiomolPolyView : BiomolInked
{
    private readonly BiomolPolysaccharide kind;
    private readonly BiomolPolysaccharides.Layout layout;
    private int? chosen, hover;

    public BiomolPolyView(BiomolPolysaccharide kind)
    {
        this.kind = kind;
        layout = kind.LayoutOf();
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Arrow;
        MinHeight = 220;
        AutomationProperties.SetName(this, $"{kind.Title()}: {layout.Glyphs.Count} sugars drawn as hexagons. {kind.Summary()} Links: "
            + string.Join(", ", layout.Links.Select(l => l.Text).Distinct()) + ". Press a link, or use the left and right arrow keys, to see its linkage.");
    }

    /// <summary>The link chosen changed (its text, or null).</summary>
    public event Action<string?>? LinkChosen;

    private (double Unit, double Left, double Top, double MinY, double MaxY) Frame()
    {
        var ys = layout.Glyphs.Select(g => g.Y).Concat(layout.Notes.Select(n => n.Y)).ToList();
        double minY = ys.Min() - 0.6, maxY = ys.Max() + 0.6;
        double w = layout.Width + 0.6, h = maxY - minY;
        var unit = Math.Min(Math.Min((ActualWidth - 32) / Math.Max(w, 1), (ActualHeight - 32) / Math.Max(h, 1)), 72);
        var left = (ActualWidth - w * unit) / 2 + 0.3 * unit;
        var top = (ActualHeight - h * unit) / 2;
        return (unit, left, top, minY, maxY);
    }

    private Point At(double x, double y, (double Unit, double Left, double Top, double MinY, double MaxY) f) => new(f.Left + x * f.Unit, f.Top + (f.MaxY - y) * f.Unit);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (ActualWidth < 40 || ActualHeight < 40) return;
        var f = Frame();
        var r = f.Unit * 0.34;
        var dark = Appearance.Dark;
        var sugar = K.Hex(kind == BiomolPolysaccharide.Chitin ? BioPalette.Nonpolar.HexFor(dark) : (kind is BiomolPolysaccharide.Cellulose ? BioPalette.Polar.HexFor(dark) : BioPalette.Strand2.HexFor(dark)));
        var fill = Frozen(K.WithAlpha(sugar, dark ? 0.32 : 0.22));
        var edge = new Pen(Frozen(sugar), 1.6);
        var red = Frozen(K.Hex(BioPalette.Acidic.HexFor(dark)));
        var accent = Frozen(Appearance.Accent);
        // (the links under the glyphs)
        for (var k = 0; k < layout.Links.Count; k++)
        {
            var l = layout.Links[k];
            BiomolPolysaccharides.Glyph a = layout.Glyphs[l.From], b = layout.Glyphs[l.To];
            Point p = At(a.X, a.Y, f), q = At(b.X, b.Y, f);
            var lit = k == chosen || k == hover;
            var pen = new Pen(lit ? accent : Ink, lit ? 2.6 : 1.5);
            if (l.Kind is BiomolPolysaccharides.LinkKind.HBond or BiomolPolysaccharides.LinkKind.Gap) pen.DashStyle = new DashStyle(new[] { 2.0, 2.0 }, 0);
            dc.DrawLine(pen, p, q);
            if (l.Kind is BiomolPolysaccharides.LinkKind.Chain or BiomolPolysaccharides.LinkKind.Branch)
            {
                // (the glycosidic O half way along)
                var mid = p + (q - p) / 2;
                dc.DrawEllipse(Ground, new Pen(red, 1.2), mid, Math.Max(2.5, r * 0.14), Math.Max(2.5, r * 0.14));
            }
            if (l.Kind == BiomolPolysaccharides.LinkKind.Gap)
            {
                var t = Words(l.Text, 11, Ink2, Plain);
                var mid = p + (q - p) / 2;
                dc.DrawRectangle(Ground, null, new Rect(mid.X - t.Width / 2 - 3, mid.Y - r - t.Height - 2, t.Width + 6, t.Height));
                dc.DrawText(t, new Point(mid.X - t.Width / 2, mid.Y - r - t.Height - 2));
            }
        }
        foreach (var g in layout.Glyphs)
        {
            var c = At(g.X, g.Y, f);
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (var i = 0; i < 6; i++)
                {
                    var ang = Math.PI / 6 + i * Math.PI / 3;
                    var pt = new Point(c.X + r * Math.Cos(ang), c.Y + r * Math.Sin(ang));
                    if (i == 0) ctx.BeginFigure(pt, true, true); else ctx.LineTo(pt, true, true);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(fill, edge, geo);
            // (the ring O: at the top right, or the bottom right of a flipped sugar)
            var oa = g.Flipped ? Math.PI / 6 : -Math.PI / 6;
            dc.DrawEllipse(red, null, new Point(c.X + r * Math.Cos(oa), c.Y + r * Math.Sin(oa)), Math.Max(2.2, r * 0.13), Math.Max(2.2, r * 0.13));
            if (g.Tag != null)
            {
                var t = Words(g.Tag, Math.Max(8, r * 0.45), Ink);
                dc.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
            }
        }
        foreach (var (x, y, text) in layout.Notes)
        {
            var t = Words(text, 12, Ink2, Plain);
            var p = At(x, y, f);
            dc.DrawText(t, new Point(Math.Clamp(p.X - t.Width / 2, 4, Math.Max(4, ActualWidth - t.Width - 4)), p.Y - t.Height / 2));
        }
        // (the chosen link's linkage, in a capsule above it)
        if ((chosen ?? hover) is { } shown && shown < layout.Links.Count)
        {
            var l = layout.Links[shown];
            Point p = At(layout.Glyphs[l.From].X, layout.Glyphs[l.From].Y, f), q = At(layout.Glyphs[l.To].X, layout.Glyphs[l.To].Y, f);
            var mid = p + (q - p) / 2;
            var t = Words(l.Text, 12.5, Ink);
            var box = new Rect(mid.X - t.Width / 2 - 8, mid.Y - r - t.Height - 12, t.Width + 16, t.Height + 6);
            box.X = Math.Clamp(box.X, 2, Math.Max(2, ActualWidth - box.Width - 2));
            box.Y = Math.Max(2, box.Y);
            dc.DrawRoundedRectangle(Ground, new Pen(accent, 1.2), box, 9, 9);
            dc.DrawText(t, new Point(box.X + 8, box.Y + 3));
        }
    }

    /// <summary>The link nearest a point (within a third of a glyph spacing), if any.</summary>
    private int? LinkAt(Point at)
    {
        if (layout.Links.Count == 0 || ActualWidth < 40) return null;
        var f = Frame();
        int? best = null;
        var bestD = f.Unit * 0.34;
        for (var k = 0; k < layout.Links.Count; k++)
        {
            var l = layout.Links[k];
            Point p = At(layout.Glyphs[l.From].X, layout.Glyphs[l.From].Y, f), q = At(layout.Glyphs[l.To].X, layout.Glyphs[l.To].Y, f);
            var d = q - p;
            var u = d.LengthSquared < 1e-9 ? 0 : Math.Clamp(Vector.Multiply(at - p, d) / d.LengthSquared, 0, 1);
            var dist = (at - (p + d * u)).Length;
            if (dist < bestD) { bestD = dist; best = k; }
        }
        return best;
    }

    private void Choose(int? k)
    {
        chosen = k;
        InvalidateVisual();
        LinkChosen?.Invoke(k is { } i ? layout.Links[i].Text : null);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var k = LinkAt(e.GetPosition(this));
        Cursor = k == null ? Cursors.Arrow : Cursors.Hand;
        if (k == hover) return;
        hover = k;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        hover = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        Choose(LinkAt(e.GetPosition(this)));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var n = layout.Links.Count;
        if (n == 0) return;
        switch (e.Key)
        {
            case Key.Right: Choose(chosen is { } c ? (c + 1) % n : 0); break;
            case Key.Left: Choose(chosen is { } c2 ? (c2 - 1 + n) % n : n - 1); break;
            case Key.Escape: Choose(null); break;
            default: return;
        }
        e.Handled = true;
    }
}

/// <summary>
/// The residues under the peptide's sequence (§4.4 Proteins item 1), ten to a row: each its one-letter code in its class's
/// colour over a bar in its secondary structure's colour (helix pink, strand amber, coil grey). Drag across residues to
/// paint them with the structure chosen; or with the keyboard, ← → ↑ ↓ to move, Space to paint, and C, H, E (G, P, L) to
/// set one and move on. A proline that broke a helix is marked.
/// </summary>
internal sealed class BiomolStructureEditor : BiomolInked
{
    private const double CellW = 24, CellH = 38, Gap = 2;
    public const int PerRow = 10;
    private string sequence = "";
    private BiomolSS[] structure = Array.Empty<BiomolSS>();
    private HashSet<int> breaks = new();
    private int cursor;
    private bool painting, paintedAny;

    /// <summary>What a press paints.</summary>
    public BiomolSS PaintWith { get; set; } = BiomolSS.Helix;

    /// <summary>The structures changed by the student (at the end of a drag, or a key).</summary>
    public event Action<BiomolSS[]>? Painted;

    public BiomolStructureEditor()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        ToolTip = "";
        ToolTipService.SetInitialShowDelay(this, 250);
    }

    /// <summary>What it shows: the sequence, each residue's structure, and the prolines that broke a helix.</summary>
    public void Show(string seq, BiomolSS[] ss, IEnumerable<int> prolineBreaks)
    {
        if (painting) return;
        sequence = seq;
        structure = ss.ToArray();
        breaks = prolineBreaks.ToHashSet();
        cursor = Math.Clamp(cursor, 0, Math.Max(sequence.Length - 1, 0));
        Height = Math.Max(1, (sequence.Length + PerRow - 1) / PerRow) * (CellH + Gap + 4);
        Width = PerRow * (CellW + Gap);
        AutomationProperties.SetName(this, $"Secondary structure of {sequence.Length} residues: " + string.Join(", ",
            sequence.Select((c, i) => $"{c} {i + 1} {(i < structure.Length ? structure[i].Label() : "coil")}")) + ". Drag across residues, or use the arrow keys and Space, to paint them.");
        InvalidateVisual();
    }

    private Rect Cell(int i) => new((i % PerRow) * (CellW + Gap), (i / PerRow) * (CellH + Gap + 4), CellW, CellH);

    private int? CellAt(Point p)
    {
        for (var i = 0; i < sequence.Length; i++)
        {
            var r = Cell(i);
            r.Inflate(Gap / 2, Gap / 2 + 2);
            if (r.Contains(p)) return i;
        }
        return null;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var dark = Appearance.Dark;
        for (var i = 0; i < sequence.Length; i++)
        {
            var r = Cell(i);
            var ss = i < structure.Length ? structure[i] : BiomolSS.Coil;
            var cls = K.Hex(BioPalette.AminoAcid(sequence[i]).HexFor(dark));
            dc.DrawRoundedRectangle(Frozen(K.WithAlpha(cls, dark ? 0.2 : 0.13)), null, new Rect(r.X, r.Y, r.Width, 22), 4, 4);
            var t = Words(sequence[i].ToString(), 13, Frozen(K.Darkened(cls)), MonoFace);
            dc.DrawText(t, new Point(r.X + (r.Width - t.Width) / 2, r.Y + (22 - t.Height) / 2));
            var bar = K.Hex(BioPalette.Secondary(ss.Cartoon()).HexFor(dark));
            var barRect = new Rect(r.X, r.Y + 25, r.Width, 11);
            dc.DrawRoundedRectangle(Frozen(bar), null, barRect, 3, 3);
            if (ss is not (BiomolSS.Coil or BiomolSS.Helix or BiomolSS.Strand))
            {
                var tag = Words(ss.Letter().ToString(), 8.5, Brushes.White);
                dc.DrawText(tag, new Point(barRect.X + (barRect.Width - tag.Width) / 2, barRect.Y + (barRect.Height - tag.Height) / 2));
            }
            if (breaks.Contains(i)) dc.DrawEllipse(Frozen(K.Hex(BioPalette.Acidic.HexFor(dark))), null, new Point(r.Right - 3, r.Y + 3), 2.6, 2.6);
            if (IsKeyboardFocused && i == cursor) dc.DrawRoundedRectangle(null, new Pen(Frozen(Appearance.Accent), 2), new Rect(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), 5, 5);
        }
    }

    private void Paint(int i, BiomolSS ss)
    {
        if (i < 0 || i >= structure.Length || structure[i] == ss) return;
        structure[i] = ss;
        paintedAny = true;
        InvalidateVisual();
    }

    private void Commit()
    {
        if (!paintedAny) return;
        paintedAny = false;
        Painted?.Invoke(structure.ToArray());
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (CellAt(e.GetPosition(this)) is not { } i) return;
        cursor = i;
        painting = true;
        CaptureMouse();
        Paint(i, PaintWith);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var at = CellAt(e.GetPosition(this));
        if (at is { } i && i < sequence.Length)
        {
            var aa = BioAminoAcids.ByOne.GetValueOrDefault(sequence[i]);
            ToolTip = $"{aa?.Three ?? sequence[i].ToString()} {i + 1} · {(i < structure.Length ? structure[i] : BiomolSS.Coil).Label()}{(breaks.Contains(i) ? " (proline breaks the helix here)" : "")}";
        }
        if (!painting || at is not { } j) return;
        cursor = j;
        Paint(j, PaintWith);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!painting) return;
        painting = false;
        ReleaseMouseCapture();
        Commit();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!painting) return;
        painting = false;
        Commit();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var n = sequence.Length;
        if (n == 0) return;
        void Move(int to)
        {
            cursor = Math.Clamp(to, 0, n - 1);
            InvalidateVisual();
        }
        switch (e.Key)
        {
            case Key.Left: Move(cursor - 1); break;
            case Key.Right: Move(cursor + 1); break;
            case Key.Up: Move(cursor - PerRow); break;
            case Key.Down: Move(cursor + PerRow); break;
            case Key.Home: Move(0); break;
            case Key.End: Move(n - 1); break;
            case Key.Space or Key.Enter: Paint(cursor, PaintWith); Commit(); break;
            default:
                if (Keyboard.Modifiers != ModifierKeys.None || e.Key < Key.A || e.Key > Key.Z) return;
                if (BiomolSSes.FromLetter((char)('A' + (e.Key - Key.A))) is not { } ss) return;
                Paint(cursor, ss);
                Commit();
                Move(cursor + 1);
                break;
        }
        e.Handled = true;
    }
}
