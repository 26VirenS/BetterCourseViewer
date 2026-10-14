using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Tools;

// DNA & RNA's Replication page (spec §4.2 Replication, §3.6): a replication fork drawn left to right and stepped through
// — origin, helicase, primase, DNA polymerase III, the leading strand, the lagging strand's Okazaki fragments, DNA
// polymerase I, ligase, the semiconservative result — with the step-through's controls under it (⏮ ◀ ▶/⏸ ▶ ⏭, the dots,
// the speed; ← → Space Home End and 1–5 for its stages). Each change is drawn moving from one keyframe to the next
// (600 ms, eased; back plays it in reverse), or crossfaded with Windows' animations off. Every base is lettered; the
// strands' 5′ and 3′ ends are marked; the right column says which enzyme acts and where the energy comes from.

internal sealed class DnaReplicationPage : Grid
{
    private readonly ToolContext c;
    private readonly DnaForkDrawing drawing = new();
    private readonly BioStepAnimator animator;
    private readonly Stack right = K.V(14);
    private int shownStep = -1;
    private bool? shownDark;

    public DnaReplicationPage(ToolContext c)
    {
        this.c = c;
        var steps = DNAToolModel.Replication;
        animator = new BioStepAnimator(steps, phase => { drawing.Phase = phase; drawing.InvalidateVisual(); });
        drawing.Focusable = true;
        drawing.FocusVisualStyle = null;
        drawing.MouseLeftButtonDown += (_, _) => drawing.Focus();
        BioStepKeys.Attach(drawing, steps);
        var head = K.Text("A replication fork", Ty.Subtitle).Margin(16, 16, 16, 4);
        var sub = K.Text("Parent strands in violet and teal, new DNA in pink, RNA primers in orange", Ty.Caption, B.Text2).Margin(16, 0, 16, 8);
        var card = K.Card(K.Rows("auto, auto, *", head.Row(0), sub.Row(1), drawing.Margin(8, 0, 8, 8).Row(2)), new Thickness(0));
        var controls = new BioStepControls(steps, DnaReplication.Groups) { Margin = new Thickness(0, 14, 0, 0) };
        var main = K.Rows("*, auto", card.Row(0), controls.Row(1));
        Children.Add(new DnaTwoColumns(main, right));

        void Changed() => Refresh();
        void Theme() { shownDark = null; drawing.InvalidateVisual(); Refresh(); }
        Loaded += (_, _) =>
        {
            animator.Start();
            steps.Changed -= Changed;
            steps.Changed += Changed;
            Appearance.Changed -= Theme;
            Appearance.Changed += Theme;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            animator.Stop();
            steps.Changed -= Changed;
            Appearance.Changed -= Theme;
            steps.Pause();
        };
        Refresh();
    }

    private void Refresh()
    {
        var steps = DNAToolModel.Replication;
        var i = steps.Index;
        System.Windows.Automation.AutomationProperties.SetName(drawing, $"Replication fork. {BioStepTiming.Caption(i, steps.Steps)}. {steps.Current?.Text}");
        if (i == shownStep && shownDark == Appearance.Dark) return;
        shownStep = i;
        shownDark = Appearance.Dark;
        right.Children.Clear();
        right.Children.Add(BioUI.Card($"Step {i + 1} · {steps.Current?.Title}", null, null,
            BioUI.FactRow("Acting now", DnaReplication.Enzymes[Math.Clamp(i, 0, DnaReplication.Enzymes.Length - 1)])));
        // (the enzymes at the fork, the ones at work now first in weight)
        var jobs = new (string Name, string Job, int[] Steps)[]
        {
            ("Helicase", "Unwinds the helix and separates the strands, breaking the hydrogen bonds.", new[] { 1, 2, 3, 4, 5 }),
            ("Topoisomerase", "Relieves the twisting that builds up ahead of the fork.", new[] { 1, 2, 3, 4 }),
            ("Single-strand binding proteins", "Hold the separated strands apart.", new[] { 1, 2, 3, 4 }),
            ("Primase", "Lays down short RNA primers, so DNA polymerase has a 3′ end to add to.", new[] { 2, 5 }),
            ("DNA polymerase III", "Adds nucleotides to a 3′ end, reading the template 3′→5′ and building 5′→3′.", new[] { 3, 4, 5 }),
            ("DNA polymerase I", "Removes the RNA primers and fills the gaps with DNA.", new[] { 6 }),
            ("DNA ligase", "Seals the nicks with phosphodiester bonds.", new[] { 7 }),
        };
        var list = K.V(10);
        foreach (var (name, job, at) in jobs)
        {
            var on = at.Contains(i);
            var t = K.Text(name, Ty.BodyStrong);
            if (!on) t.Fg(B.Text2);
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 6, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            if (on) dot.Background = K.Brush(Appearance.Accent); else dot.Bg(B.Divider);
            list.Children.Add(K.Columns("auto, *", dot.Col(0), K.V(2, t, K.Wrapped(job, Ty.Caption, B.Text2)).Col(1)));
        }
        right.Children.Add(BioUI.Card("Enzymes at the fork", null, null, list));
        right.Children.Add(BioUI.Card("Colours", null, null, BioUI.Legend(new[]
        {
            (BioPalette.Strand1.ToColor(), "Parent strand (top), written 3′→5′"),
            (BioPalette.Strand2.ToColor(), "Parent strand (bottom), written 5′→3′"),
            (DnaForkDrawing.NewDna.ToColor(), "New DNA"),
            (BioPalette.Rna.ToColor(), "RNA primer (U, not T)"),
            (K.Hex("#ff9f0a"), "◆ a nick: a missing backbone bond"),
        })));
        var go = K.Link("Open the Biomolecule Builder", () => BioTools.Open(c, "biomol"));
        right.Children.Add(BioUI.Card("Where the energy comes from", null, null,
            BioUI.FactRow("dNTPs", "The cell’s nucleotides arrive as dNTPs: deoxynucleoside triphosphates. As each joins a strand’s 3′ end it lets go of two of its phosphates as pyrophosphate, and that pays for the new phosphodiester bond."),
            BioUI.FactRow("Dehydration", "The Biomolecule Builder shows the same bond as a dehydration reaction (a water lost): the textbook simplification."),
            go));
    }
}

/// <summary>
/// The replication fork drawn for a phase of the step-through: the parent duplex parting at the fork, each base a lettered
/// tile, the new strands built on each template (the leading strand toward the fork, the lagging strand's fragments away
/// from it), primers in orange with U, the enzymes where they act, the 5′ and 3′ ends marked.
/// </summary>
internal sealed class DnaForkDrawing : DnaDrawing
{
    /// <summary>New DNA's colour.</summary>
    public static readonly BioHexPair NewDna = new("#e8508a", "#ff7aa8");

    public BioStepPhase Phase { get; set; } = BioStepPhase.At(0);

    // (what it draws lies from y 49 — "fork moves →" — to 422, the old strand's words under the bottom rail once the two
    //  molecules part: the 470 it was drawn in left 50 empty above and below, and in a tool's popup, where its room is
    //  wide and short, the fork came out a third smaller than it needed to, its words 7 px tall)
    protected override Size Design => new(960, 400);
    protected override double DesignTop => 36;

    private const double X0 = 64, Dx = 26, TileW = 20, TileH = 18;

    private static double X(double i) => X0 + i * Dx + Dx / 2;

    private readonly Dictionary<(string, double, Color, bool), FormattedText> texts = new();

    private FormattedText Cached(string s, double size, Color colour, bool strong)
    {
        var key = (s, size, colour, strong);
        if (texts.TryGetValue(key, out var t)) return t;
        if (texts.Count > 600) texts.Clear();
        return texts[key] = Words(s, size, colour, strong, mono: strong && s.Length == 1);
    }

    private void Centred(DrawingContext dc, string s, double x, double y, double size, Color colour, bool strong = false, double align = 0.5)
    {
        var t = Cached(s, size, colour, strong);
        dc.DrawText(t, new Point(x - t.Width * align, y - t.Height / 2));
    }

    protected override void Draw(DrawingContext dc)
    {
        var p = Phase;
        if (p.Crossfade && p.From != p.To)
        {
            dc.PushOpacity(1 - p.T);
            Frame(dc, DnaReplication.Frames[p.From]);
            dc.Pop();
            dc.PushOpacity(p.T);
            Frame(dc, DnaReplication.Frames[p.To]);
            dc.Pop();
            return;
        }
        Frame(dc, DnaReplication.At(p));
    }

    private void Frame(DrawingContext dc, DnaForkFrame f)
    {
        var dark = Dark;
        var text = TextColour;
        var text2 = Text2Colour;
        var n = DnaReplication.Length;
        string top = DnaReplication.Top, bottom = DnaReplication.Bottom;
        double Open(double x) => DnaReplication.Open(x, f.Fork);
        double Apart(double x) => 24 * f.Apart * Open(x);
        double TopRail(double x) => 210 + (96 - 210) * Open(x) - Apart(x);
        double BottomRail(double x) => 262 + (376 - 262) * Open(x) + Apart(x);
        double LeadRail(double x) => TopRail(x) + 52;
        double LagRail(double x) => BottomRail(x) - 52;
        Color s1 = BioPalette.Strand1.ToColor(dark), s2 = BioPalette.Strand2.ToColor(dark), fresh = NewDna.ToColor(dark), rna = BioPalette.Rna.ToColor(dark);

        // (the parent strands' backbones, parting at the fork)
        void Rail(Func<double, double> y, Color colour)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(X(-0.45), y(-0.45)), false, false);
                for (var x = -0.45; x <= n - 0.55 + 1e-9; x += 0.25) g.LineTo(new Point(X(x), y(x)), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(null, PenOf(colour, 3), geo);
        }
        Rail(x => TopRail(x), s1);
        Rail(x => BottomRail(x), s2);

        // (the tiles: each base lettered, the parents' facing each other, H-bond ticks where a pair is joined)
        void Tile(double x, double y, char b, double alpha = 1)
        {
            if (alpha <= 0.01) return;
            if (alpha < 1) dc.PushOpacity(alpha);
            var c = Base(b);
            dc.DrawRoundedRectangle(K.Brush(c), null, new Rect(x - TileW / 2, y - TileH / 2, TileW, TileH), 4, 4);
            var white = DnaWpf.WhiteOn(BioColors.Hex(c));
            Centred(dc, $"{b}", x, y, 12, white ? Colors.White : Color.FromRgb(0x1c, 0x1c, 0x1e), strong: true);
            if (alpha < 1) dc.Pop();
        }
        var tick = PenOf(text2, 1.2);
        void Ticks(double x, double y1, double y2, char b, double alpha = 1)
        {
            if (alpha <= 0.05) return;
            var count = b is 'G' or 'C' ? 3 : 2;
            if (alpha < 1) dc.PushOpacity(alpha);
            for (var k = 0; k < count; k++)
            {
                var dx = (k - (count - 1) / 2.0) * 4.5;
                dc.DrawLine(tick, new Point(x + dx, y1), new Point(x + dx, y2));
            }
            if (alpha < 1) dc.Pop();
        }
        for (var i = 0; i < n; i++)
        {
            var xc = i + 0.5;
            var x = X(i);
            double yt = TopRail(xc) + 13, yb = BottomRail(xc) - 13;
            Tile(x, yt, top[i]);
            Tile(x, yb, bottom[i]);
            var o = Open(xc);
            if (o < 0.05) Ticks(x, yt + TileH / 2 + 1, yb - TileH / 2 - 1, top[i], 1 - o * 20);
        }

        // (the new strands: their backbones, their tiles, the ticks to their templates)
        var lead = Enumerable.Range(0, n).Select(i => DnaReplication.Leading(i, f)).ToArray();
        var lag = Enumerable.Range(0, n).Select(i => DnaReplication.Lagging(i, f)).ToArray();
        void NewRail(int i, double y, bool isRna, double amount, bool joinLeft, bool joinRight)
        {
            var x = X(i);
            var pen = PenOf(isRna ? rna : fresh, 3);
            dc.PushOpacity(amount);
            dc.DrawLine(pen, new Point(x - (joinLeft ? Dx / 2 : TileW / 2), y), new Point(x + (joinRight ? Dx / 2 : TileW / 2), y));
            dc.Pop();
        }
        var sealedNow = f.Sealed >= 0.5;
        // (a nick: where pieces meet once the primers are replaced, until ligase seals it)
        bool LeadNick(int b) => !sealedNow && f.Replaced >= 0.5 && DnaReplication.LeadingNicks.Contains(b);
        bool LagNick(int b) => !sealedNow && f.Replaced >= 0.5 && DnaReplication.LaggingNicks.Contains(b);
        for (var i = 0; i < n; i++)
        {
            var xc = i + 0.5;
            if (lead[i] is { } l)
            {
                var y = LeadRail(xc);
                var isRna = l.Kind == DnaPieceKind.Rna;
                NewRail(i, y, isRna, l.Amount, i > 0 && lead[i - 1] != null && !LeadNick(i), i < n - 1 && lead[i + 1] != null && !LeadNick(i + 1));
                Tile(X(i), y - 13, DnaReplication.NewLetter(true, i, isRna), l.Amount);
                Ticks(X(i), TopRail(xc) + 13 + TileH / 2 + 1, y - 13 - TileH / 2 - 1, top[i], l.Amount);
            }
            if (lag[i] is { } g)
            {
                var y = LagRail(xc);
                var isRna = g.Kind == DnaPieceKind.Rna;
                // (within a fragment joined but at a nick; between fragments only once DNA polymerase I has filled across)
                bool Joined(int a, int b) => a >= 0 && b < n && lag[a] is { } la && lag[b] is { } lb
                                              && (la.Fragment == lb.Fragment ? !LagNick(b) : f.Replaced >= 0.5);
                NewRail(i, y, isRna, g.Amount, Joined(i - 1, i), Joined(i, i + 1));
                Tile(X(i), y + 13, DnaReplication.NewLetter(false, i, isRna), g.Amount);
                Ticks(X(i), y + 13 + TileH / 2 + 1, BottomRail(xc) - 13 - TileH / 2 - 1, bottom[i], g.Amount);
            }
        }

        // (the nicks: where a new piece meets the next before ligase seals it)
        var nickAlpha = f.Nicks * (1 - f.Sealed);
        if (nickAlpha > 0.02 && f.Replaced >= 0.5)
        {
            dc.PushOpacity(nickAlpha);
            var nickBrush = K.Brush(K.Hex("#ff9f0a"));
            void Diamond(double x, double y)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(x, y - 6), true, true);
                    g.LineTo(new Point(x + 6, y), true, false);
                    g.LineTo(new Point(x, y + 6), true, false);
                    g.LineTo(new Point(x - 6, y), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(nickBrush, null, geo);
            }
            foreach (var i in DnaReplication.LeadingNicks) Diamond(X(i - 0.5), LeadRail(i));
            foreach (var i in DnaReplication.LaggingNicks) Diamond(X(i - 0.5), LagRail(i));
            dc.Pop();
        }

        // (the direction each new strand grows: an arrowhead at each 3′ end)
        void Arrow(double x, double y, int dir, Color colour)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(x + dir * 9, y), true, true);
                g.LineTo(new Point(x - dir * 2, y - 6), true, false);
                g.LineTo(new Point(x - dir * 2, y + 6), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(K.Brush(colour), null, geo);
        }
        var leadLast = Array.FindLastIndex(lead, x => x != null);
        if (leadLast >= 0 && lead[leadLast] is { Kind: DnaPieceKind.Dna } && f.LeadDna > DnaReplication.PrimerLength + 0.5 && f.Apart < 0.5)
        {
            Arrow(X(leadLast) + Dx / 2 + 1, LeadRail(leadLast + 0.5), 1, fresh);
            if (leadLast < n - 1) Centred(dc, "3′", X(leadLast) + Dx + 6, LeadRail(leadLast + 0.5) - 13, 13, text, true);
        }
        if (lead[0] != null) Centred(dc, "5′", X(0) - 30, LeadRail(0.5), 13, text, true);
        for (var j = 0; j < DnaReplication.Fragments.Length; j++)
        {
            var (start, end) = DnaReplication.Fragments[j];
            var first = Enumerable.Range(start, end - start).FirstOrDefault(i => lag[i] is { Kind: DnaPieceKind.Dna } piece && piece.Fragment == j, -1);
            if (first >= 0 && !sealedNow && f.Apart < 0.5) Arrow(X(first) - Dx / 2 - 1, LagRail(first + 0.5), -1, fresh);
        }
        var lagFirst = Array.FindIndex(lag, x => x != null);
        var lagLast = Array.FindLastIndex(lag, x => x != null);
        if (lagFirst >= 0)
        {
            if (lag[lagFirst] is { Kind: DnaPieceKind.Dna }) Centred(dc, "3′", lagFirst == 0 ? X(0) - 30 : X(lagFirst) - Dx - 6, LagRail(lagFirst + 0.5) + (lagFirst == 0 ? 0 : 13), 13, text, true);
            Centred(dc, "5′", lagLast == n - 1 ? X(n - 1) + 30 : X(lagLast) + Dx + 4, LagRail(lagLast + 0.5) + (lagLast == n - 1 ? 0 : 13), 13, text, true);
        }
        if (lead[n - 1] != null) Centred(dc, "3′", X(n - 1) + 30, LeadRail(n - 0.5), 13, text, true);

        // (the parents' ends)
        Centred(dc, "3′", X(0) - 30, TopRail(-0.45), 13, text, true);
        Centred(dc, "5′", X(n - 1) + 30, TopRail(n - 0.55), 13, text, true);
        Centred(dc, "5′", X(0) - 30, BottomRail(-0.45), 13, text, true);
        Centred(dc, "3′", X(n - 1) + 30, BottomRail(n - 0.55), 13, text, true);

        // ---- the enzymes ----
        void Blob(double cx, double cy, double w, double h, string hex, double alpha, double corner)
        {
            if (alpha <= 0.02) return;
            dc.PushOpacity(alpha);
            var c = K.Hex(hex);
            dc.DrawRoundedRectangle(K.Brush(K.WithAlpha(c, dark ? 0.42 : 0.32)), PenOf(c, 1.8), new Rect(cx - w / 2, cy - h / 2, w, h), corner, corner);
            dc.Pop();
        }
        void Label(string s, double x, double y, double alpha, double align = 0.5)
        {
            if (alpha <= 0.02) return;
            // (kept inside the drawing: a label near an end slides in)
            var w = Cached(s, 12.5, text, true).Width;
            var left = Math.Clamp(x - w * align, 8, Design.Width - 8 - w);
            dc.PushOpacity(alpha);
            Centred(dc, s, left, y, 12.5, text, true, 0);
            dc.Pop();
        }
        // helicase: a ring at the fork
        if (f.Helicase > 0.02 && f.Fork > 0.5)
        {
            var hx = Math.Min(X(f.Fork - 1.2), 920);
            var hy = (TopRail(f.Fork - 1.2) + BottomRail(f.Fork - 1.2)) / 2;
            dc.PushOpacity(f.Helicase);
            var hc = K.Hex("#5e5ce6");
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                for (var k = 0; k < 6; k++)
                {
                    var a = Math.PI / 3 * k + Math.PI / 6;
                    var pt = new Point(hx + 22 * Math.Cos(a), hy + 22 * Math.Sin(a));
                    if (k == 0) g.BeginFigure(pt, true, true); else g.LineTo(pt, true, false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(K.Brush(K.WithAlpha(hc, dark ? 0.45 : 0.35)), PenOf(hc, 2), geo);
            dc.Pop();
            Label("Helicase", hx - 30, hy, f.Helicase, 1);
            if (f.Fork < DnaReplication.Length) Label("fork moves →", Math.Min(X(f.Fork) + 30, 900), 58, f.Helicase);
        }
        // topoisomerase: a clamp on the duplex ahead
        if (f.Topo > 0.02 && f.Fork + 3 < DnaReplication.Length)
        {
            var tx = X(f.Fork + 2.5);
            Blob(tx, 236, 30, 82, "#30b0c7", f.Topo, 10);
            Label("Topoisomerase", tx, 292, f.Topo);
        }
        // single-strand binding proteins: on bare template, every other base
        if (f.Ssb > 0.02)
        {
            var sc = K.Hex("#a2845e");
            dc.PushOpacity(f.Ssb);
            for (var i = 0; i < n; i += 2)
            {
                var xc = i + 0.5;
                if (Open(xc) < 0.999) continue;
                if (lead[i] == null) dc.DrawEllipse(K.Brush(K.WithAlpha(sc, 0.75)), null, new Point(X(i), TopRail(xc) + 30), 6.5, 6.5);
                if (lag[i] == null) dc.DrawEllipse(K.Brush(K.WithAlpha(sc, 0.75)), null, new Point(X(i), BottomRail(xc) - 30), 6.5, 6.5);
            }
            dc.Pop();
            if (f.LagDna[0] >= 7.9) Label("Single-strand binding proteins", X(0) - 10, 262, f.Ssb, 0);
        }
        // primase: on the primer it is laying
        if (f.Primase > 0.02)
        {
            if (f.LeadDna <= DnaReplication.PrimerLength + 0.5 && lead[1] != null) { Blob(X(1.5), LeadRail(2) + 16, 60, 26, "#bf5af2", f.Primase, 13); Label("Primase", X(1.5), 190, f.Primase); }
            for (var j = DnaReplication.Fragments.Length - 1; j >= 0; j--)
            {
                if (f.LagPrimer[j] <= 0.5 || lag[DnaReplication.PrimerStart(j)] == null) continue;
                var px = X(DnaReplication.PrimerStart(j) + 1.5);
                Blob(px, LagRail(DnaReplication.PrimerStart(j) + 2) - 16, 60, 26, "#bf5af2", f.Primase, 13);
                Label("Primase", px, 284, f.Primase);
                break;
            }
        }
        // DNA polymerase III: at each strand's growing 3′ end
        if (f.Pol3 > 0.02)
        {
            if (leadLast >= DnaReplication.PrimerLength)
            {
                var px = X(leadLast) + 6;
                Blob(px, LeadRail(leadLast + 0.5) + 16, 64, 30, "#34aadc", f.Pol3, 15);
                Label(PolymeraseWords(f, leadLast + 1, true), px, 190, f.Pol3);
                Incoming(dc, f, leadLast + 1, true, LeadRail(leadLast + 1.5) - 13 + 20);
            }
            if (DnaReplication.LaggingEnd(f) is { } e && e >= 0 && e < n && lag[e] is { Kind: DnaPieceKind.Dna })
            {
                var px = X(e) - 6;
                Blob(px, LagRail(e + 0.5) - 16, 64, 30, "#34aadc", f.Pol3, 15);
                Label(PolymeraseWords(f, e - 1, false), px, 284, f.Pol3);
                Incoming(dc, f, e - 1, false, LagRail(e - 0.5) + 13 - 20);
            }
        }
        // DNA polymerase I: at each primer it replaces
        if (f.Pol1 > 0.02)
        {
            Blob(X(1.5), LeadRail(2) + 16, 54, 26, "#8bbf3f", f.Pol1, 13);
            for (var j = 0; j < DnaReplication.Fragments.Length; j++)
                Blob(X(DnaReplication.PrimerStart(j) + 1.5), LagRail(DnaReplication.PrimerStart(j) + 2) - 16, 54, 26, "#8bbf3f", f.Pol1, 13);
            Label("DNA polymerase I", X(1.5), 190, f.Pol1);
            Label("DNA polymerase I", X(DnaReplication.PrimerStart(1) + 1.5), 284, f.Pol1);
        }
        // DNA ligase: at each nick
        if (f.Ligase > 0.02)
        {
            foreach (var i in DnaReplication.LeadingNicks) Blob(X(i - 0.5), LeadRail(i) + 15, 22, 22, "#c27c4a", f.Ligase, 6);
            foreach (var i in DnaReplication.LaggingNicks) Blob(X(i - 0.5), LagRail(i) - 15, 22, 22, "#c27c4a", f.Ligase, 6);
            Label("DNA ligase", X(DnaReplication.LeadingNicks[0] - 0.5), 190, f.Ligase);
            Label("DNA ligase", X(DnaReplication.LaggingNicks[1] - 0.5), 284, f.Ligase);
        }

        // ---- the words ----
        Label("Origin", X(0) - 6, 70, 1 - f.Apart);
        if (f.Apart < 0.5)
        {
            if (f.LeadDna > DnaReplication.PrimerLength + 2.5) Label("Leading strand: continuous →", X(0) - 10, 214, Math.Min(1, (f.LeadDna - 6.5) / 2), 0);
            if (f.LagDna[0] < 7.9) Label("← Lagging strand: Okazaki fragments", X(0) - 10, 258, Math.Min(1, (7.9 - f.LagDna[0]) / 2), 0);
        }
        if (f.Apart > 0.02)
        {
            Label("old strand", 480, TopRail(n) - 13, f.Apart);
            Label("new strand", 480, LeadRail(n) + 13, f.Apart);
            Label("new strand", 480, LagRail(n) - 13, f.Apart);
            Label("old strand", 480, BottomRail(n) + 13, f.Apart);
            Label("Each new DNA molecule: one old strand + one new strand (semiconservative)", 480, 236, f.Apart);
        }
    }

    /// <summary>The polymerase's label: in step 4, which nucleotide it is adding and what that pairs with.</summary>
    private static string PolymeraseWords(DnaForkFrame f, int i, bool leading)
    {
        if (f.Incoming <= 0.5 || i < 0 || i >= DnaReplication.Length) return "DNA polymerase III";
        var b = DnaReplication.NewLetter(leading, i, false);
        var partner = leading ? DnaReplication.Top[i] : DnaReplication.Bottom[i];
        return $"DNA polymerase III adds d{b}TP: {b} pairs with {partner}";
    }

    /// <summary>The next nucleotide arriving (step 4), held by the polymerase just short of its place on the template.</summary>
    private void Incoming(DrawingContext dc, DnaForkFrame f, int i, bool leading, double y)
    {
        if (f.Incoming <= 0.02 || i < 0 || i >= DnaReplication.Length) return;
        var b = DnaReplication.NewLetter(leading, i, false);
        var x = X(i);
        dc.PushOpacity(f.Incoming);
        var c = Base(b);
        dc.DrawRoundedRectangle(K.Brush(c), PenOf(TextColour, 1.2), new Rect(x - TileW / 2, y - TileH / 2, TileW, TileH), 4, 4);
        Centred(dc, $"{b}", x, y, 12, DnaWpf.WhiteOn(BioColors.Hex(c)) ? Colors.White : Color.FromRgb(0x1c, 0x1c, 0x1e), true);
        dc.Pop();
    }
}
