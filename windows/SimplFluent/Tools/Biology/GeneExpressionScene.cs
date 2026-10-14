using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Simpl.Tools;

// The Transcribe & translate page's scene (spec §4.3), the Mac's GeneExpressionScene.swift: a 2D drawing — sharper for
// letters than 3D — in four lanes, top to bottom: the coding strand (5′→3′), the template strand (3′→5′) aligned base for
// base under it, the RNA as it is made (aligned with the DNA it came from, its 3′ end paired with the template inside the
// polymerase's bubble, then let go), and below the nuclear envelope the ribosome's lane (the mRNA with its codons boxed
// from the AUG, the ribosome's E, P and A sites, the tRNAs with their anticodons written 3′→5′ over the codons they pair
// with, and the growing polypeptide, each amino acid over the codon it came from). The promoter, UTRs, exons and intron
// are tinted bands; the TATA box, start and stop codons and poly-A signal are boxed. An overview strip shows the whole
// gene and the part in view. The view follows the work (the polymerase, the processing, the ribosome); a drag looks aside.

/// <summary>
/// The scene for a moment of the step-through: at rest on a step, moving between two neighbouring steps (GXScene.Make),
/// or — for a jump, and with animations off — the two steps crossfaded. Drag looks aside; the pointer over an amino acid
/// or a codon says what it is.
/// </summary>
internal sealed class GXSceneView : GXCanvas
{
    private GXProcess? process;
    private BioStepPhase phase = BioStepPhase.At(0);
    private double pan;
    private double? dragFrom, panFrom;
    private Point? hover;

    public GXSceneView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Arrow;
        MinHeight = 300;
    }

    /// <summary>What is drawn: the process and where its step-through is.</summary>
    public void Show(GXProcess? p, BioStepPhase at)
    {
        if (!ReferenceEquals(p, process)) pan = 0;
        process = p;
        phase = at;
        InvalidateVisual();
    }

    /// <summary>The view back on the work (a step taken).</summary>
    public void Recentre()
    {
        pan = 0;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        dragFrom = e.GetPosition(this).X;
        panFrom = pan;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (dragFrom is { } a && panFrom is { } b && IsMouseCaptured)
        {
            pan = b + p.X - a;
            Cursor = Cursors.SizeWE;
        }
        hover = p;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        dragFrom = null;
        panFrom = null;
        Cursor = Cursors.Arrow;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        hover = null;
        InvalidateVisual();
    }

    protected override void Draw(DrawingContext dc, Size size)
    {
        if (process is not { } p) return;
        var n = p.Frames.Count;
        if (n == 0) return;
        int from = Math.Clamp(phase.From, 0, n - 1), to = Math.Clamp(phase.To, 0, n - 1);
        var notice = new HashSet<string>(p.Steps[to].Notices);
        if (phase.IsMoving && (phase.Crossfade || Math.Abs(from - to) != 1))
        {
            dc.PushOpacity(1 - phase.T);
            new GXPainter(this, p, GXScene.Rest(p, from), size, pan, new HashSet<string>(), null).Draw(dc);
            dc.Pop();
            dc.PushOpacity(phase.T);
            new GXPainter(this, p, GXScene.Rest(p, to), size, pan, notice, null).Draw(dc);
            dc.Pop();
        }
        else
        {
            var scene = phase.IsMoving ? GXScene.Make(p, from, to, phase.T) : GXScene.Rest(p, to);
            new GXPainter(this, p, scene, size, pan, notice, phase.IsMoving || dragFrom != null ? null : hover).Draw(dc);
        }
    }
}

/// <summary>Draws a GXScene (the Mac's GXPainter, coordinate for coordinate). Lanes are laid out for a view 400 tall
/// (squeezed when shorter); columns are a base pair's place.</summary>
internal sealed class GXPainter
{
    private readonly GXCanvas v;
    private readonly GXProcess p;
    private readonly GXScene s;
    private readonly Size size;
    private readonly HashSet<string> notice;
    private readonly Point? hover;
    private GXGene G => p.Gene;
    private GXFrame F => s.Frame;

    public const double Gutter = 150;
    // (the lanes' heights in a view 400 tall)
    private const double YOverview = 8, YMarks = 26, YRegions = 54, Y1 = 84, Y2 = 110, Y3 = 170, YEnvelope = 218, Y4 = 354;

    private readonly double left, right, width, sy, top;
    /// <summary>Points per column.</summary>
    private readonly double cell;
    /// <summary>The x of column 0.</summary>
    private readonly double x0;
    private readonly Color primary, secondary, accent;

    public GXPainter(GXCanvas view, GXProcess process, GXScene scene, Size size, double pan, HashSet<string> notice, Point? hover)
    {
        v = view;
        p = process;
        s = scene;
        this.size = size;
        this.notice = notice;
        this.hover = hover;
        primary = view.Primary;
        secondary = view.Secondary;
        accent = view.Accent;
        left = Gutter + 8;
        right = size.Width - 14;
        width = Math.Max(right - left, 40);
        sy = Math.Min(1, size.Height / 400);
        top = Math.Max(0, (size.Height - 400) / 2) * 0.6;
        // (the view: from the whole gene to bases at full size, centred on the work, kept on the gene)
        var span = process.Gene.ColumnSpan;
        var total = span.Hi - span.Lo + 1;
        const double full = 17;
        var fitCell = Math.Min(full, width / (total + 2));
        cell = full + (fitCell - full) * Math.Clamp(scene.Frame.Fit, 0, 1);
        var visible = width / cell;
        double lo = span.Lo - 1 + visible / 2, hi = span.Hi + 1 - visible / 2;
        var focus = lo >= hi ? (span.Lo + span.Hi) / 2 : Math.Clamp(scene.Frame.Focus, lo, hi);
        var reach = total * cell;
        x0 = left + width / 2 - focus * cell + Math.Clamp(pan, -reach, reach);
    }

    private double X(double col) => x0 + col * cell;
    private double Y(double v0) => top + v0 * sy;

    private static double Ease(double t)
    {
        var u = Math.Clamp(t, 0, 1);
        return u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;
    }

    /// <summary>The columns in view (and one more each side).</summary>
    private (int Lo, int Hi) Visible
    {
        get
        {
            int a = (int)Math.Floor((left - x0) / cell) - 1, b = (int)Math.Ceiling((right - x0) / cell) + 1;
            return (a, Math.Max(a, b));
        }
    }

    // ---- drawing ----

    public void Draw(DrawingContext dc)
    {
        Overview(dc);
        dc.PushClip(new RectangleGeometry(new Rect(left - 4, 0, width + 8, size.Height)));
        Regions(dc);
        Factors(dc);
        Polymerase(dc);
        Dna(dc);
        CodonBoxes(dc);
        Rna(dc);
        Envelope(dc);
        Ribosome(dc);
        foreach (var t in s.Trnas) Trna(dc, t);
        ReleaseFactor(dc);
        Chain(dc);
        Coupling(dc);
        Tooltip(dc);
        dc.Pop();
        GutterLabels(dc);
    }

    private void Text(DrawingContext dc, string text, Point at, double size = 11, FontWeight? weight = null, Color? colour = null,
        GXDraw.Anchor anchor = GXDraw.Anchor.Centre, double alpha = 1, bool mono = false) =>
        v.Say(dc, text, at, size, weight ?? FontWeights.SemiBold, colour ?? secondary, anchor, alpha, mono);

    /// <summary>A base: its letter on its colour's tint (a bar of its colour when zoomed out too far for letters).</summary>
    private void Base(DrawingContext dc, char b, Point at, double alpha = 1, Color? ring = null)
    {
        if (alpha <= 0.01) return;
        if (alpha < 0.99) dc.PushOpacity(alpha);
        double w = Math.Max(cell - 1.6, 1), h = 17;
        var colour = BioColors.Base(b);
        var lettered = Math.Clamp((cell - 9) / 4, 0, 1);
        var rect = new Rect(at.X - w / 2, at.Y - h / 2, w, h);
        if (lettered < 1) dc.DrawRectangle(GXDraw.Brush(colour, 0.8 * (1 - lettered)), null, rect);
        if (lettered > 0)
        {
            GXDraw.Round(dc, GXDraw.Brush(colour, 0.17 * lettered), null, rect, 3.5);
            var t = GXDraw.Text(b.ToString(), 12, FontWeights.Bold, colour, true, v.Ppd);
            if (lettered < 0.99) dc.PushOpacity(lettered);
            GXDraw.Draw(dc, t, at);
            if (lettered < 0.99) dc.Pop();
        }
        if (ring is { } r) GXDraw.Round(dc, null, GXDraw.Pen(r, 1.5), new Rect(rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2), 4.5);
        if (alpha < 0.99) dc.Pop();
    }

    // ---- the overview strip ----

    private void Overview(DrawingContext dc)
    {
        var span = G.ColumnSpan;
        var total = span.Hi - span.Lo + 1;
        double Ox(double col) => left + (col - span.Lo) / total * width;
        var yy = Y(YOverview);
        GXDraw.Round(dc, GXDraw.Brush(primary, 0.07), null, new Rect(left, yy - 3, width, 6), 3);
        foreach (var r in G.Regions)
        {
            double a = Ox(r.Range.Lo - 0.5), b = Ox(r.Range.Hi + 0.5);
            GXDraw.Round(dc, GXDraw.Brush(GXColors.RegionSolid(r.Kind)), null, new Rect(a, yy - 3, Math.Max(b - a, 1), 6), 2);
        }
        double va = (left - x0) / cell, vb = (right - x0) / cell;
        double wa = Math.Max(Ox(va), left), wb = Math.Min(Ox(vb), right);
        if (wb - wa < width - 2) GXDraw.Round(dc, null, GXDraw.Pen(accent, 1.5), new Rect(wa, yy - 6, Math.Max(wb - wa, 3), 12), 3);
        if (F.Polymerase > 0.05)
            dc.DrawEllipse(GXDraw.Brush(GXColors.Polymerase.ToColor(), F.Polymerase), null, new Point(Ox(F.PolX), yy), 4, 4);
        if (F.Small > 0.05 && G.CdsStart != null)
        {
            var col = F.Large > 0.01 ? G.CodonColumn(s.Ribosome) : G.ColumnOfMature(F.SmallAt);
            dc.DrawEllipse(GXDraw.Brush(GXColors.Ribosome.ToColor(), F.Small), null, new Point(Ox(col), yy), 4, 4);
        }
    }

    // ---- the gene's bands and marks ----

    private void Regions(DrawingContext dc)
    {
        double bandTop = Y(Y1) - 15, bandBottom = Y(Y2) + 15;
        foreach (var r in G.Regions)
        {
            double a = X(r.Range.Lo - 0.5), b = X(r.Range.Hi + 0.5);
            if (!(b > left - 4 && a < right + 4)) continue;
            dc.DrawRectangle(GXDraw.Brush(GXColors.Region(r.Kind)), null, new Rect(a, bandTop, b - a, bandBottom - bandTop));
            // (its name kept whole inside the part of it in view: at the view's edge it slides in, and where too little of it
            //  shows for its name, it goes unnamed — "Coding sequence" was cut to "oding sequence")
            var half = r.Name.Length * 3.1 + 4;
            double va = Math.Max(a, left), vb = Math.Min(b, right);
            var lit = r.Kind == GXRegionKind.Intron && (notice.Contains("intron") || notice.Contains("gu-ag"));
            if (vb - va > 2 * half)
                Text(dc, r.Name, new Point(Math.Clamp((a + b) / 2, va + half, vb - half), Y(YRegions)), 10.5, lit ? FontWeights.Bold : FontWeights.SemiBold, lit ? accent : secondary);
        }
        // (the boxed marks, their names in one or two rows above)
        var rowEnds = new[] { double.NegativeInfinity, double.NegativeInfinity };
        foreach (var m in G.Marks)
        {
            double a = X(m.Range.Lo - 0.5), b = X(m.Range.Hi + 0.5);
            if (!(b > left - 40 && a < right + 40)) continue;
            var colour = MarkColour(m.Kind);
            var lit = notice.Contains(MarkKey(m.Kind));
            var box = new Rect(a, bandTop + 2, b - a, bandBottom - bandTop - 4);
            if (lit) GXDraw.Round(dc, null, GXDraw.Pen(colour, 6, 0.3), box, 4);
            GXDraw.Round(dc, null, GXDraw.Pen(colour, lit ? 2.2 : 1.3), box, 4);
            var w = m.Name.Length * 6.2 + 8;
            // (its name over it, kept inside the view: a mark half scrolled out has its name at the edge it is still at —
            //  "Start codon" was cut to "art codon"; one wholly out of view goes unnamed)
            if (!(b > left && a < right)) continue;
            var mid = Math.Clamp((a + b) / 2, left + w / 2, Math.Max(left + w / 2, right - w / 2));
            var row = rowEnds[0] < mid - w / 2 ? 0 : 1;
            rowEnds[row] = mid + w / 2;
            Text(dc, m.Name, new Point(mid, Y(YMarks + row * 13)), 10.5, lit ? FontWeights.Bold : FontWeights.SemiBold, colour);
        }
        // (+1: the transcription start site)
        double sx = X(G.TxStart - 0.5), ty = Y(Y1) - 11;
        var plusLit = notice.Contains("plus1");
        var arrowColour = plusLit ? accent : primary;
        var hx = sx + Math.Max(cell * 1.4, 9);
        dc.DrawGeometry(null, GXDraw.Pen(arrowColour, plusLit ? 2 : 1.4, 0.8), GXDraw.Lines(new[] { new Point(sx, ty), new Point(sx, ty - 12), new Point(hx, ty - 12) }));
        dc.DrawGeometry(GXDraw.Brush(arrowColour, 0.8), null, GXDraw.Polygon(new Point(hx + 4, ty - 12), new Point(hx - 2, ty - 16), new Point(hx - 2, ty - 8)));
        Text(dc, "+1", new Point(sx - 3, ty - 12), 10.5, FontWeights.Bold, arrowColour, GXDraw.Anchor.Trailing);
        // (the splice sites, GU…AG, on the coding strand)
        if (notice.Contains("gu-ag"))
            foreach (var intron in G.Introns)
                foreach (var (lo, hi) in new[] { (intron.Lo, intron.Lo + 1), (intron.Hi - 1, intron.Hi) })
                {
                    double a = X(lo - 0.5), b = X(hi + 0.5);
                    GXDraw.Round(dc, null, GXDraw.Pen(accent, 2), new Rect(a, Y(Y1) - 10, b - a, 20), 4);
                }
    }

    private static Color MarkColour(GXRegionKind k) => k switch
    {
        GXRegionKind.Tata => BioPalette.Guanine.ToColor(),
        GXRegionKind.Start => GXColors.Start.ToColor(),
        GXRegionKind.Stop => GXColors.Stop.ToColor(),
        _ => BioPalette.Cytosine.ToColor(),
    };

    private static string MarkKey(GXRegionKind k) => k switch
    {
        GXRegionKind.Tata => "tata",
        GXRegionKind.Start => "start",
        GXRegionKind.Stop => "stop",
        _ => "polyA",
    };

    // ---- transcription's machinery ----

    /// <summary>How far the DNA is opened at a column (the bubble round the polymerase: about 13 bp, from 9 behind it to 3 ahead).</summary>
    private double Open(double col)
    {
        if (F.Bubble <= 0.001) return 0;
        var d = col - F.PolX;
        const double lo = -9.0, hi = 3.0, ramp = 1.5;
        double v0;
        if (d < lo - ramp || d > hi + ramp) v0 = 0;
        else if (d < lo) v0 = (d - (lo - ramp)) / ramp;
        else if (d > hi) v0 = (hi + ramp - d) / ramp;
        else v0 = 1;
        return v0 * v0 * (3 - 2 * v0) * F.Bubble;
    }

    private void Factors(DrawingContext dc)
    {
        if (F.Factors <= 0.01) return;
        var factor = GXColors.Factor.ToColor();
        if (G.Cell == GXCell.Eukaryote && G.Marks.FirstOrDefault(m => m.Kind == GXRegionKind.Tata) is { } tata)
        {
            double cx = X((tata.Range.Lo + tata.Range.Hi) / 2.0), cy = Y(Y1) - 22;
            dc.PushOpacity(F.Factors);
            foreach (var (dx, w) in new[] { (-9.0, 26.0), (8.0, 22.0) })
                dc.DrawEllipse(GXDraw.Brush(factor, 0.55), null, new Point(cx + dx, cy), w / 2, 8);
            dc.Pop();
            Text(dc, "Transcription factors", new Point(cx - 26, cy), 10, FontWeights.SemiBold, factor, GXDraw.Anchor.Trailing, F.Factors);
        }
        else if (G.Cell == GXCell.Prokaryote && F.Polymerase > 0.01)
        {
            // (σ helps RNA polymerase find the promoter, and lets go after the first nucleotides)
            var a = F.Factors * Math.Clamp((12 - F.Made) / 4, 0, 1) * F.Polymerase;
            if (a <= 0.01) return;
            double cx = X(F.PolX - 11.5), cy = Y(Y1) - 18;
            dc.PushOpacity(a);
            dc.DrawEllipse(GXDraw.Brush(factor, 0.6), null, new Point(cx, cy), 10, 10);
            v.Say(dc, "σ", new Point(cx, cy), 13, FontWeights.Bold, Colors.White);
            dc.Pop();
        }
    }

    private void Polymerase(DrawingContext dc)
    {
        if (F.Polymerase <= 0.01) return;
        double a = X(F.PolX - 10.5), b = X(F.PolX + 4.5);
        var rect = new Rect(a, Y(Y1) - 26, b - a, Y(Y2) + 48 - (Y(Y1) - 26));
        var colour = GXColors.Polymerase.ToColor();
        dc.PushOpacity(F.Polymerase);
        GXDraw.Round(dc, GXDraw.Brush(colour, 0.2), GXDraw.Pen(colour, 1.5, 0.65), rect, 26);
        dc.Pop();
        var name = G.Cell == GXCell.Eukaryote ? "RNA polymerase II" : "RNA polymerase";
        Text(dc, name, new Point(b + 6, Y(Y1) - 17), 11, FontWeights.Bold, colour, GXDraw.Anchor.Leading, F.Polymerase);
        Text(dc, "reads the template 3′→5′ →", new Point(b + 6, Y(Y2) + 24), 10, FontWeights.Medium, colour, GXDraw.Anchor.Leading, F.Polymerase);
        // (free nucleotides waiting downstream, under the template)
        if (F.Stage.IsTranscription())
        {
            foreach (var (dx, dy, l) in new[] { (6.0, -2.0, 'A'), (8.5, 14.0, 'U'), (11.0, 2.0, 'G'), (13.5, 16.0, 'C'), (15.5, 0.0, 'A') })
                Ntp(dc, l, new Point(X(F.PolX + dx), Y(Y3) + dy), 0.6 * F.Polymerase);
            // (their name kept whole inside the view, its last letter was cut at the edge)
            var half = v.Measure("free NTPs", 10, FontWeights.SemiBold) / 2;
            Text(dc, "free NTPs", new Point(Math.Min(X(F.PolX + 10.5), right + 10 - half), Y(Y3) + 32), 10, alpha: F.Polymerase);
        }
    }

    /// <summary>A nucleoside triphosphate: its base, and three small phosphates trailing.</summary>
    private void Ntp(DrawingContext dc, char b, Point at, double alpha, double tail = 1)
    {
        Base(dc, b, at, alpha);
        if (tail <= 0.01) return;
        var c = BioPalette.Uracil.ToColor();
        for (var k = 0; k < 3; k++)
        {
            var px = at.X + Math.Max(cell, 10) / 2 + 3 + k * 5;
            dc.DrawEllipse(GXDraw.Brush(c, 0.7 * alpha * tail), null, new Point(px, at.Y), 2, 2);
        }
    }

    private void Dna(DrawingContext dc)
    {
        var n = G.Coding.Length;
        if (n == 0) return;
        var vis = Visible;
        int lo = Math.Max(vis.Lo, 0), hi = Math.Min(vis.Hi, n - 1);
        if (lo > hi) return;
        var coding = new List<Point>();
        var template = new List<Point>();
        for (var col = lo; col <= hi; col++)
        {
            var o = Open(col);
            coding.Add(new Point(X(col), Y(Y1) - 11 * o));
            template.Add(new Point(X(col), Y(Y2) + 11 * o));
        }
        dc.DrawGeometry(null, GXDraw.Pen(BioPalette.Strand1.ToColor(), 2, 0.55), GXDraw.Lines(coding.ToArray()));
        dc.DrawGeometry(null, GXDraw.Pen(BioPalette.Strand2.ToColor(), 2, 0.55), GXDraw.Lines(template.ToArray()));
        var paired = cell >= 6;
        var hbond = BioPalette.HBond.ToColor();
        for (var col = lo; col <= hi; col++)
        {
            var o = Open(col);
            var cx = X(col);
            double y1 = Y(Y1) - 11 * o, y2 = Y(Y2) + 11 * o;
            if (paired && o < 0.9) dc.DrawLine(GXDraw.Dashed(hbond, 1.5, 2, 2, 0.6 * (1 - o)), new Point(cx, y1 + 9), new Point(cx, y2 - 9));
            Base(dc, G.Coding[col], new Point(cx, y1));
            Base(dc, G.Template[col], new Point(cx, y2));
        }
        double l = X(-0.5) - 10, r = X(n - 0.5) + 10;
        Text(dc, "5′", new Point(l, Y(Y1)), 11, FontWeights.Bold);
        Text(dc, "3′", new Point(r, Y(Y1)), 11, FontWeights.Bold);
        Text(dc, "3′", new Point(l, Y(Y2)), 11, FontWeights.Bold);
        Text(dc, "5′", new Point(r, Y(Y2)), 11, FontWeights.Bold);
    }

    // ---- the RNA ----

    /// <summary>Where a pre-mRNA base is now: its column and height (paired in the bubble, in its lane, exported, or an
    /// intron looping away), and how visible.</summary>
    private (double X, double Y, double Alpha) Place(int i)
    {
        var n = G.PreMRNA.Length;
        var col = G.Column(i, F.Splice);
        var yy = Y(Y3);
        // (the last 8 nt paired with the template inside the bubble; further back the RNA peels away)
        var d = G.TxStart + i - F.PolX;
        var hyb = (d >= -7 ? 1 : (d >= -10 ? (d + 10) / 3 : 0)) * (1 - F.Released);
        if (hyb > 0)
        {
            var pairedY = Y(Y2) + 11 * Open(G.TxStart + i) + 21;
            yy += (pairedY - yy) * hyb;
        }
        // (an intron loops away and fades as it is cut out)
        var alpha = 1.0;
        if (G.InIntron(i) && G.Introns.FirstOrDefault(x => x.Contains(G.TxStart + i)) is var intron && intron.Count > 0)
        {
            var q = (double)(G.TxStart + i - intron.Lo) / Math.Max(intron.Count - 1, 1);
            yy -= 48 * Math.Sin(Math.PI * q) * F.Splice;
            alpha = 1 - F.Splice;
        }
        // (export: down to the ribosome's lane, the 5′ end first)
        var e = Math.Clamp(F.Export * 1.6 - 0.6 * i / Math.Max(n - 1, 1), 0, 1);
        if (e > 0) yy += (Y(Y4) - yy) * Ease(e);
        return (X(col), yy, alpha);
    }

    private void Rna(DrawingContext dc)
    {
        var n = G.PreMRNA.Length;
        var settled = Math.Min(s.Incoming ?? (int)(F.Made + 1e-6), n);
        if (settled <= 0 && s.Incoming == null) return;
        var colour = BioPalette.Rna.ToColor();
        var places = Enumerable.Range(0, Math.Max(settled, 0)).Select(Place).ToList();
        // (the backbone, then the bases)
        var runs = new List<Point[]>();
        var run = new List<Point>();
        for (var i = 0; i < places.Count; i++)
        {
            if (places[i].Alpha <= 0.5)
            {
                if (run.Count > 1) runs.Add(run.ToArray());
                run = new List<Point>();
                continue;
            }
            run.Add(new Point(places[i].X, places[i].Y));
        }
        if (run.Count > 1) runs.Add(run.ToArray());
        if (runs.Count > 0) dc.DrawGeometry(null, GXDraw.Pen(colour, 2, 0.5), GXDraw.Lines(runs.ToArray()));
        var vis = Visible;
        var hbond = BioPalette.HBond.ToColor();
        for (var i = 0; i < places.Count; i++)
        {
            var pl = places[i];
            var col = (int)Math.Round((pl.X - x0) / cell);
            if (col < vis.Lo - 2 || col > vis.Hi + 2) continue;
            // (paired with the template inside the bubble: dashed H-bonds)
            var d = G.TxStart + i - F.PolX;
            if (d >= -7 && F.Released < 0.5 && F.Polymerase > 0.3 && cell >= 6)
                dc.DrawLine(GXDraw.Dashed(hbond, 1.5, 2, 2, 0.6), new Point(pl.X, pl.Y - 9), new Point(pl.X, Y(Y2) + 11 * Open(G.TxStart + i) + 9));
            Base(dc, G.PreMRNA[i], new Point(pl.X, pl.Y), pl.Alpha);
        }
        // (the nucleotide coming in from the free NTPs, to the RNA's 3′ end)
        if (s.Incoming is { } k && k < n)
        {
            var u = s.IncomingProgress;
            var dest = Place(k);
            var src = new Point(X(F.PolX + 6), Y(Y3) - 2);
            var e = 1 - Math.Pow(1 - u, 3);
            var at = new Point(src.X + (dest.X - src.X) * e, src.Y + (dest.Y - src.Y) * e);
            Ntp(dc, G.PreMRNA[k], at, Math.Min(1, 0.4 + u), 1 - u);
            GXDraw.Round(dc, null, GXDraw.Pen(accent, 1.5, 0.9), new Rect(at.X - cell / 2, at.Y - 9.5, cell, 19), 4.5);
        }
        else if (F.Stage == GXStage.TxElong && settled > 0 && F.Polymerase > 0.5)
        {
            var last0 = places[settled - 1];
            GXDraw.Round(dc, null, GXDraw.Pen(accent, 1.5, 0.9), new Rect(last0.X - cell / 2, last0.Y - 9.5, cell, 19), 4.5);
        }
        if (settled <= 0) return;
        var first = places[0];
        var last = places[settled - 1];
        // (the 5′ cap: 7-methylguanosine, joined 5′ to 5′)
        if (F.Cap > 0.01)
        {
            var cx = first.X - cell * 1.6 - 4;
            var g = BioPalette.Guanine.ToColor();
            dc.PushOpacity(F.Cap);
            dc.DrawLine(GXDraw.Pen(colour, 2, 0.6), new Point(cx + 8, first.Y), new Point(first.X - cell / 2, first.Y));
            dc.DrawEllipse(GXDraw.Brush(g, 0.3), GXDraw.Pen(g, 1.5), new Point(cx, first.Y), 9, 9);
            v.Say(dc, "G", new Point(cx, first.Y), 11, FontWeights.Bold, g, mono: true);
            dc.Pop();
            Text(dc, "m⁷G cap", new Point(cx, first.Y - 18), 10.5, FontWeights.Bold, notice.Contains("cap") ? accent : secondary, alpha: F.Cap);
        }
        // (the poly-A tail, 20 of its ≈200 A drawn)
        var end = last.X;
        if (F.Tail > 0.01)
        {
            var count = (int)Math.Round(F.Tail * GXProcess.TailDrawn, MidpointRounding.AwayFromZero);
            for (var t = 0; t < count; t++) Base(dc, 'A', new Point(last.X + (t + 1) * cell, last.Y), Math.Min(1, F.Tail * 1.5));
            end = last.X + count * cell;
            if (count > 0)
            {
                Text(dc, "…", new Point(end + cell * 0.9, last.Y), 12, FontWeights.Bold, alpha: F.Tail);
                Text(dc, "poly-A tail (≈200 A)", new Point(last.X + (count + 1) * cell / 2, last.Y - 18), 10.5, FontWeights.Bold,
                    notice.Contains("tail") ? accent : secondary, alpha: F.Tail);
                end += cell;
            }
        }
        // (a bacterial terminator: a hairpin the RNA folds into at its 3′ end, drawn as a sketch)
        if (F.Hairpin > 0.01)
        {
            var hx = last.X - cell * 2.5;
            var a = F.Hairpin * (1 - F.Export);
            dc.PushOpacity(a);
            var pen = GXDraw.Pen(colour, 2);
            dc.DrawGeometry(null, pen, GXDraw.Lines(new[] { new Point(hx - 4, last.Y - 10), new Point(hx - 4, last.Y - 32) }, new[] { new Point(hx + 4, last.Y - 10), new Point(hx + 4, last.Y - 32) }));
            dc.DrawEllipse(null, pen, new Point(hx, last.Y - 40), 8, 8);
            dc.Pop();
            Text(dc, "terminator hairpin", new Point(hx + 14, last.Y - 40), 10.5, FontWeights.Bold, colour, GXDraw.Anchor.Leading, a);
        }
        // (the spliceosome over the intron while it works)
        if (F.Splice > 0.01 && F.Splice < 0.99)
            foreach (var intron in G.Introns)
            {
                double a = X(intron.Lo - 1), b = X(intron.Hi + 1);
                var kk = Math.Sin(Math.PI * F.Splice);
                var pc = GXColors.Polymerase.ToColor();
                dc.PushOpacity(0.9 * kk);
                GXDraw.Round(dc, GXDraw.Brush(pc, 0.16), GXDraw.Pen(pc, 1.2, 0.6), new Rect(a, Y(Y3) - 62, b - a, 80), 30);
                dc.Pop();
                Text(dc, "spliceosome", new Point((a + b) / 2, Y(Y3) - 52), 11, FontWeights.Bold, pc, alpha: kk);
            }
        // (where the exons now join, and the codon split across them)
        if (notice.Contains("junction") && F.Splice > 0.5 && G.SplitCodon is { } sc && G.CdsStart is { } s0)
        {
            var j = G.Junctions.FirstOrDefault();
            var jx = X(G.ColumnOfMature(j) - 0.5);
            dc.DrawLine(GXDraw.Dashed(accent, 2, 3, 2), new Point(jx, first.Y - 14), new Point(jx, first.Y + 14));
            double a = X(G.ColumnOfMature(s0 + 3 * sc.Index) - 0.5), b = X(G.ColumnOfMature(s0 + 3 * sc.Index + 2) + 0.5);
            GXDraw.Round(dc, null, GXDraw.Pen(accent, 2), new Rect(a, first.Y - 11, b - a, 22), 5);
            Text(dc, $"exon 1 | exon 2 · codon {sc.Index + 1} {sc.Codon} spans the joint", new Point(jx, first.Y + 26), 10.5, FontWeights.Bold, accent);
        }
        // (the ends)
        var capped = F.Cap > 0.5;
        Text(dc, "5′", new Point(first.X - cell / 2 - (capped ? cell * 1.6 + 22 : 10), first.Y), 11, FontWeights.Bold);
        Text(dc, "3′", new Point(end + cell / 2 + 10, last.Y), 11, FontWeights.Bold);
    }

    // ---- the nuclear envelope ----

    private void Envelope(DrawingContext dc)
    {
        if (G.Cell != GXCell.Eukaryote) return;
        var ye = Y(YEnvelope);
        var colour = GXDraw.Alpha(secondary, 0.55);
        // (two membranes, with a pore every so often)
        const double spacing = 180;
        var phase = (x0 % spacing + spacing) % spacing;
        var pores = new List<double>();
        for (var xx = left - spacing + phase; xx < right + spacing; xx += spacing) pores.Add(xx);
        var runs = new List<Point[]>();
        var from = left - 4;
        foreach (var p0 in pores.Where(p0 => p0 > left - 4 && p0 < right + 4))
        {
            foreach (var dy in new[] { -3.0, 3.0 }) runs.Add(new[] { new Point(from, ye + dy), new Point(p0 - 8, ye + dy) });
            from = p0 + 8;
        }
        foreach (var dy in new[] { -3.0, 3.0 }) runs.Add(new[] { new Point(from, ye + dy), new Point(right + 4, ye + dy) });
        var pen = GXDraw.Pen(colour, 1.5);
        dc.DrawGeometry(null, pen, GXDraw.Lines(runs.ToArray()));
        foreach (var p0 in pores.Where(p0 => p0 > left && p0 < right))
        {
            // (each membrane's end rounded off at the pore)
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(p0 - 8, ye - 3), false, false);
                c.ArcTo(new Point(p0 - 8, ye + 3), new Size(3, 3), 0, false, SweepDirection.Clockwise, true, true);
                c.BeginFigure(new Point(p0 + 8, ye - 3), false, false);
                c.ArcTo(new Point(p0 + 8, ye + 3), new Size(3, 3), 0, false, SweepDirection.Counterclockwise, true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
        if (F.Stage == GXStage.Export || (F.Export > 0.01 && F.Export < 0.99))
        {
            var pore = pores.FirstOrDefault(p0 => p0 > left + 40, double.NaN);
            if (!double.IsNaN(pore)) Text(dc, "nuclear pore", new Point(pore + 12, ye + 12), 10, FontWeights.SemiBold, anchor: GXDraw.Anchor.Leading);
        }
    }

    // ---- translation ----

    /// <summary>The reading frame on the ribosome's lane: alternating boxes round each codon from the AUG on, numbered
    /// every 5 (the 5′ UTR before it unboxed: it is not read).</summary>
    private void CodonBoxes(DrawingContext dc)
    {
        if (G.CdsStart is not { } s0 || F.Export <= 0.5) return;
        var a = Math.Min((F.Export - 0.5) * 2, 1) * (1 - 0.5 * F.Result);
        var yy = Y(Y4);
        dc.PushOpacity(a);
        var startPen = GXDraw.Pen(GXColors.Start.ToColor(), 1.5);
        var stopPen = GXDraw.Pen(GXColors.Stop.ToColor(), 1.5);
        foreach (var cd in G.Codons)
        {
            double l = X(G.ColumnOfMature(s0 + 3 * cd.Index) - 0.5), r = X(G.ColumnOfMature(s0 + 3 * cd.Index + 2) + 0.5);
            if (!(r > left - 4 && l < right + 4)) continue;
            var rect = new Rect(l + 0.5, yy - 12, r - l - 1, 24);
            GXDraw.Round(dc, GXDraw.Brush(primary, cd.Index % 2 == 0 ? 0.06 : 0.13), cd.Index == 0 ? startPen : cd.Aa == '*' ? stopPen : null, rect, 5);
            if (cd.Index == 0 || (cd.Index + 1) % 5 == 0)
                v.Say(dc, $"{cd.Index + 1}", new Point((l + r) / 2, yy + 42), 9.5, FontWeights.SemiBold, secondary, mono: true);
        }
        dc.Pop();
        // (the context the scanning ribosome stops at: Kozak in a eukaryote; where a Shine–Dalgarno site would be in a bacterium)
        if (notice.Contains("kozak") && G.Kozak is { } k)
        {
            double l = X(G.ColumnOfMature(k) - 0.5), r = X(G.ColumnOfMature(k + 9) + 0.5);
            GXDraw.Round(dc, null, GXDraw.Dashed(accent, 1.8, 4, 3), new Rect(l, yy - 14, r - l, 28), 6);
            Text(dc, "Kozak context", new Point(l - 6, yy - 20), 10.5, FontWeights.Bold, accent, GXDraw.Anchor.Trailing, a);
        }
        if (notice.Contains("sd") && s0 >= 9)
        {
            double l = X(G.ColumnOfMature(s0 - 10) - 0.5), r = X(G.ColumnOfMature(s0 - 5) + 0.5);
            GXDraw.Round(dc, null, GXDraw.Dashed(accent, 1.8, 4, 3), new Rect(l, yy - 14, r - l, 28), 6);
            Text(dc, "Shine–Dalgarno region", new Point(l - 6, yy - 20), 10.5, FontWeights.Bold, accent, GXDraw.Anchor.Trailing, a);
        }
    }

    private double Apart => G.HasStop ? F.Freed : 0;

    private void Ribosome(DrawingContext dc)
    {
        if (G.CdsStart == null || !(F.Small > 0.01 || F.Large > 0.01)) return;
        var codonW = 3 * cell;
        var yy = Y(Y4);
        var joined = F.Large > 0.01 || F.Stage > GXStage.TlInit;
        var cx = X(joined ? G.CodonColumn(s.Ribosome) : G.ColumnOfMature(F.SmallAt));
        var fade = (1 - 0.55 * Apart) * (1 - F.Result);
        var colour = GXColors.Ribosome.ToColor();
        // (the small subunit, under the mRNA)
        var sw = 4 * codonW;
        dc.PushOpacity(F.Small * fade);
        GXDraw.Round(dc, GXDraw.Brush(colour, 0.45), GXDraw.Pen(colour, 1.2, 0.9), new Rect(cx - sw / 2, yy + 9 + 18 * Apart, sw, 24), 12);
        dc.Pop();
        if (!joined || F.Large < 0.99)
            Text(dc, "small subunit", new Point(cx, yy + 21 + 18 * Apart), 10, FontWeights.Bold, primary, alpha: F.Small * fade * 0.8);
        // (the large subunit, over it, with its E, P and A sites one codon each)
        if (F.Large <= 0.01) return;
        var lw = 4.5 * codonW;
        var drop = (1 - F.Large) * 34 + 24 * Apart;
        var rect = new Rect(cx - lw / 2, yy - 92 - drop, lw, 84);
        dc.PushOpacity(F.Large * fade);
        GXDraw.Round(dc, GXDraw.Brush(colour, 0.26), GXDraw.Pen(colour, 1.2, 0.85), rect, 26);
        foreach (var (k, name) in new[] { (-1.0, "E"), (0.0, "P"), (1.0, "A") })
        {
            var mid = X(G.CodonColumn(s.Ribosome + k));
            v.Say(dc, name, new Point(mid, rect.Top + 11), 12, FontWeights.Black, colour);
            if (k < 1)
            {
                var bx = mid + codonW / 2;
                dc.DrawLine(GXDraw.Dashed(colour, 1, 3, 3, 0.4), new Point(bx, rect.Top + 20), new Point(bx, rect.Bottom - 6));
            }
        }
        dc.Pop();
        Text(dc, "ribosome", new Point(rect.Left - 6, rect.Top + 12), 11, FontWeights.Bold, colour, GXDraw.Anchor.Trailing, F.Large * fade);
    }

    /// <summary>Where a tRNA's anticodon sits (over its codon, or lifted away up and to the side it comes from or leaves to).</summary>
    private Point TrnaAnchor(GXTRNA t)
    {
        var xx = X(G.CodonColumn(t.Codon));
        var dir = t.Codon > s.Ribosome + 0.01 ? 1 : (t.Codon < s.Ribosome - 0.01 ? -1 : 0.4);
        return new Point(xx + dir * t.Lift * 3 * cell * 0.9, Y(Y4) - 22 - t.Lift * 120);
    }

    private void Trna(DrawingContext dc, GXTRNA t)
    {
        if (t.Alpha <= 0.01 || t.Codon < 0 || t.Codon >= G.Codons.Count) return;
        var codon = G.Codons[t.Codon].Codon;
        var at = TrnaAnchor(t);
        double cx = at.X, by = at.Y;
        var colour = GXColors.Trna.ToColor();
        var a = t.Alpha * (1 - F.Result);
        if (a <= 0.01) return;
        dc.PushOpacity(a);
        // (a cloverleaf in small: the acceptor stem up to the amino acid, two side loops, the anticodon loop at the foot)
        dc.DrawLine(GXDraw.Pen(colour, 6, 0.9), new Point(cx, by - 10), new Point(cx, by - 46));
        foreach (var dx in new[] { -13.0, 13.0 })
            dc.DrawEllipse(GXDraw.Brush(colour, 0.18), GXDraw.Pen(colour, 2, 0.9), new Point(cx + dx, by - 29.5), 6.5, 6.5);
        var w = 3 * cell - 3;
        GXDraw.Round(dc, GXDraw.Brush(colour, 0.16), GXDraw.Pen(colour, 1.5, 0.9), new Rect(cx - w / 2, by - 10, w, 20), 6);
        // (the anticodon, 3′→5′ left to right, each base over the codon base it pairs with)
        var anti = BioCode.Anticodon3To5(codon);
        for (var k = 0; k < anti.Length; k++) Base(dc, anti[k], new Point(cx + (k - 1) * cell, by));
        // (the directions, on the tRNA in the P site)
        if (t.Lift < 0.05 && Math.Abs(t.Codon - s.Ribosome) < 0.01 && cell >= 12)
        {
            Text(dc, "3′", new Point(cx - w / 2 - 7, by), 9.5, FontWeights.Bold);
            Text(dc, "5′", new Point(cx + w / 2 + 7, by), 9.5, FontWeights.Bold);
            if (t.Initiator && F.Stage == GXStage.TlInit)
                Text(dc, "initiator tRNA", new Point(cx + 22, by - 46), 10, FontWeights.Bold, colour, GXDraw.Anchor.Leading);
        }
        dc.Pop();
    }

    private void ReleaseFactor(DrawingContext dc)
    {
        if (s.ReleaseLift is not { } lift || F.ReleaseFactor <= 0.01 || !G.HasStop || G.Codons.Count == 0) return;
        var stop = G.Codons[^1];
        var xx = X(G.CodonColumn(stop.Index)) + lift * 3 * cell * 0.9;
        var by = Y(Y4) - 22 - lift * 120;
        var colour = GXColors.ReleaseFactor.ToColor();
        var w = 3 * cell - 3;
        var a = F.ReleaseFactor * (1 - F.Result);
        dc.PushOpacity(a);
        GXDraw.Round(dc, GXDraw.Brush(colour, 0.35), GXDraw.Pen(colour, 1.5), new Rect(xx - w / 2, by - 50, w, 60), 10);
        v.Say(dc, "RF", new Point(xx, by - 20), 12, FontWeights.Black, colour);
        dc.Pop();
        Text(dc, "release factor (a protein)", new Point(xx + w / 2 + 6, by - 40), 10.5, FontWeights.Bold, colour, GXDraw.Anchor.Leading, a);
    }

    /// <summary>Each amino acid of the chain: over the codon it came from, or on top of its tRNA.</summary>
    private List<Point> ResiduePoints()
    {
        // (kept below the nuclear envelope: in a short view the lane comes up close to it, and the chain ran along its line)
        var row = Math.Max(Y(Y4) - 118 - 18 * Apart, Y(YEnvelope) + 14);
        return s.Residues.Select(r =>
        {
            var rowPt = new Point(X(G.CodonColumn(r.X)), row);
            if (r.OnTRNA <= 0 || !s.Trnas.Any(t => t.Codon == r.Index)) return rowPt;
            var a = TrnaAnchor(s.Trnas.First(t => t.Codon == r.Index));
            var topPt = new Point(a.X, a.Y - 58);
            var o = r.OnTRNA;
            return new Point(rowPt.X + (topPt.X - rowPt.X) * o, rowPt.Y + (topPt.Y - rowPt.Y) * o);
        }).ToList();
    }

    private void Chain(DrawingContext dc)
    {
        if (s.Residues.Count == 0) return;
        var pts = ResiduePoints();
        // (peptide bonds)
        for (var k = 1; k < s.Residues.Count; k++)
        {
            var r = s.Residues[k];
            if (r.Bonded <= 0.01) continue;
            dc.DrawLine(GXDraw.Pen(primary, 2.5, 0.45 * r.Bonded * Math.Min(r.Alpha, s.Residues[k - 1].Alpha)), pts[k - 1], pts[k]);
        }
        var ring = GXDraw.Pen(Colors.White, 1, 0.7);
        for (var k = 0; k < s.Residues.Count; k++)
        {
            var r = s.Residues[k];
            var pt = pts[k];
            if (!(pt.X > left - 20 && pt.X < right + 20) || r.Alpha <= 0.01) continue;
            if (r.Alpha < 0.99) dc.PushOpacity(r.Alpha);
            dc.DrawEllipse(GXDraw.Brush(BioColors.AminoAcid(r.Aa)), ring, pt, 11, 11);
            v.Say(dc, r.Aa.ToString(), pt, 12, FontWeights.Black, Colors.White);
            if (r.Alpha < 0.99) dc.Pop();
        }
        if (s.Residues.Count > 1 || F.Stage >= GXStage.TlEnd)
            Text(dc, "N-terminus", new Point(pts[0].X - 16, pts[0].Y), 10, FontWeights.Bold, anchor: GXDraw.Anchor.Trailing);
        if (F.Result > 0.01)
            Text(dc, "C-terminus", new Point(pts[^1].X + 16, pts[^1].Y), 10, FontWeights.Bold, anchor: GXDraw.Anchor.Leading, alpha: F.Result);
    }

    /// <summary>In a bacterium a ribosome can start on the RNA while it is still being made: sketched once the start codon is out.</summary>
    private void Coupling(DrawingContext dc)
    {
        if (G.Cell != GXCell.Prokaryote || F.Stage != GXStage.TxElong || G.CdsStart is not { } s0 || F.Made < s0 + GXProcess.CouplingLag) return;
        var pl = Place(s0 + 1);
        var sw = 8 * cell;
        var colour = GXColors.Ribosome.ToColor();
        GXDraw.Round(dc, GXDraw.Brush(colour, 0.18), GXDraw.Dashed(colour, 1.4, 4, 3), new Rect(pl.X - sw / 2, pl.Y + 9, sw, 22), 11);
        Text(dc, "a ribosome can start already", new Point(pl.X - sw / 2 + 2, pl.Y + 42), 10.5, FontWeights.Bold, colour, GXDraw.Anchor.Leading);
    }

    // ---- a tooltip on an amino acid or a codon ----

    private void Tooltip(DrawingContext dc)
    {
        if (hover is not { } h) return;
        string? line = null;
        var pts = ResiduePoints();
        var k = pts.FindIndex(q => Math.Sqrt((q.X - h.X) * (q.X - h.X) + (q.Y - h.Y) * (q.Y - h.Y)) < 12);
        if (k >= 0)
        {
            var r = s.Residues[k];
            if (BioAminoAcids.ByOne.TryGetValue(r.Aa, out var aa) && r.Index < G.Codons.Count)
                line = $"{aa.Three} ({aa.Name.ToLowerInvariant()}, {aa.Group.Label().ToLowerInvariant()}) · codon {r.Index + 1}, {G.Codons[r.Index].Codon}";
        }
        else if (F.Export > 0.9 && G.CdsStart is { } s0 && Math.Abs(h.Y - Y(Y4)) < 13)
        {
            var col = (h.X - x0) / cell;
            var m = (int)Math.Round(col - G.TxStart, MidpointRounding.AwayFromZero) - s0;
            if (m >= 0 && m / 3 < G.Codons.Count)
            {
                var cd = G.Codons[m / 3];
                var what = cd.Aa == '*' ? "stop" : (BioAminoAcids.ByOne.TryGetValue(cd.Aa, out var a2) ? $"{a2.Three} ({a2.Name.ToLowerInvariant()})" : "?");
                line = $"Codon {cd.Index + 1}: {cd.Codon} → {what}";
            }
        }
        if (line == null) return;
        var t = GXDraw.Text(line, 11.5, FontWeights.Medium, primary, false, v.Ppd);
        var at = new Point(Math.Min(h.X + 14, right - t.Width - 12), h.Y - 26);
        var rect = new Rect(at.X - 8, at.Y - t.Height / 2 - 5, t.Width + 16, t.Height + 10);
        GXDraw.Round(dc, GXDraw.Brush(v.Ground), GXDraw.Pen(primary, 1, 0.18), rect, rect.Height / 2);
        GXDraw.Draw(dc, t, at, GXDraw.Anchor.Leading);
    }

    // ---- the lanes' names ----

    private void GutterLabels(DrawingContext dc)
    {
        const double gx = Gutter - 2;
        void Label(string title, string? sub, double yy, double alpha = 1)
        {
            if (alpha <= 0.01) return;
            Text(dc, title, new Point(gx, sub == null ? yy : yy - 7), 11.5, FontWeights.SemiBold, primary, GXDraw.Anchor.Trailing, alpha * 0.85);
            if (sub != null) Text(dc, sub, new Point(gx, yy + 7), 10.5, FontWeights.Medium, secondary, GXDraw.Anchor.Trailing, alpha);
        }
        Label("Whole gene", null, Y(YOverview));
        Label("Coding (sense) strand", "5′→3′", Y(Y1));
        Label("Template (antisense)", "strand 3′→5′", Y(Y2));
        var made = (s.Incoming ?? (int)(F.Made + 1e-6)) > 0 || s.Incoming != null;
        if (made)
        {
            var mature = G.Introns.Count == 0 || F.Splice > 0.5;
            Label(mature ? "mRNA" : "pre-mRNA", "5′→3′", Y(Y3), 1 - F.Export);
        }
        if (G.Cell == GXCell.Eukaryote)
        {
            Text(dc, "NUCLEUS ↑", new Point(gx, Y(YEnvelope) - 11), 10, FontWeights.Bold, anchor: GXDraw.Anchor.Trailing);
            Text(dc, "CYTOPLASM ↓", new Point(gx, Y(YEnvelope) + 11), 10, FontWeights.Bold, anchor: GXDraw.Anchor.Trailing);
        }
        else Label("No nucleus:", "all in the cytoplasm", Y(YEnvelope));
        if (F.Export > 0.01) Label("mRNA 5′→3′", "the ribosome's lane", Y(Y4), Math.Min(F.Export * 1.5, 1));
    }
}
