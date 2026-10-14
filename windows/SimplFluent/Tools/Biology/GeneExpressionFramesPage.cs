using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// Gene Expression's Reading frames page (spec §4.3, §7 item 12), the Mac's GXFramesPage: any DNA or mRNA in its three
/// forward frames (+1, +2, +3, starting at the 1st, 2nd and 3rd nucleotide) and, for DNA, the three reverse frames read on
/// the reverse complement (−1, −2, −3), each amino acid under the codon it comes from — stops as * on red, Met as M on
/// green — and the ORF finder: every AUG… stop of at least L codons, longest first, its positions on the forward strand.
/// </summary>
internal sealed class GXFramesPage : Grid
{
    private readonly ContentControl presetHost = new() { Focusable = false };
    private readonly SelectorBar kindBar;
    private readonly Wpf.Ui.Controls.TextBox field;
    private readonly ContentControl messageHost = new() { Focusable = false };
    private readonly TextBlock count = BioUI.Caption("");
    private readonly ContentControl stepperHost = new() { Focusable = false };
    private readonly Wpf.Ui.Controls.ToggleSwitch reverseSwitch;
    private readonly FrameworkElement reverseRow;
    private readonly TextBlock title = K.Text("", Ty.Subtitle);
    private readonly TextBlock hoverLine = K.Text("", Ty.Body, B.Text2);
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly GXFramesView view;
    private readonly TextBlock emptyText = K.Wrapped("", Ty.Body, B.Text2).Center();
    private readonly Stack orfList = K.V(14);
    private const string HoverHint = "Each amino acid sits under its codon. Point at one to see the codon it comes from.";

    public GXFramesPage()
    {
        kindBar = GXKit.Choice(new[] { "DNA", "mRNA" }, GeneExpressionModel.FramesKind == BioSeqKind.Rna ? 1 : 0, i => GeneExpressionModel.FramesKind = i == 1 ? BioSeqKind.Rna : BioSeqKind.Dna);
        kindBar.ToolTip = "DNA shows six frames (three on each strand); mRNA, a single strand, three";
        field = GXKit.Field(GeneExpressionModel.FramesText, "ATGGCC… or AUGGCC…", t => GeneExpressionModel.FramesText = t, multiline: true);
        System.Windows.Automation.AutomationProperties.SetName(field, "Sequence, 5 prime to 3 prime");
        field.ToolTip = "DNA or mRNA, 5′→3′ (Ctrl+F)";
        var (row, sw) = GXKit.Toggle("Reverse frames (−1, −2, −3)", GeneExpressionModel.ShowReverse, on => GeneExpressionModel.ShowReverse = on);
        reverseRow = row;
        reverseSwitch = sw;
        var accent = Appearance.Accent;
        var left = K.V(14,
            BioUI.Heading("Sequence"), presetHost, kindBar, field, messageHost, count,
            BioUI.Heading("Open reading frames"), stepperHost, reverseRow,
            BioUI.Legend(new[]
            {
                (GXColors.Start.ToColor(), "M: Met, where an ORF can start"),
                (GXColors.Stop.ToColor(), "*: a stop codon"),
                (accent, "Bar: an ORF (bold: the one picked)"),
            }));

        view = new GXFramesView(scroll);
        view.HoverChanged += text =>
        {
            hoverLine.Text = text ?? HoverHint;
            hoverLine.Fg(text == null ? B.Text2 : B.Text);
        };
        scroll.Content = view;
        hoverLine.Text = HoverHint;
        var head = K.V(2, title, hoverLine);
        head.Margin = new Thickness(16, 14, 16, 8);
        var body = new Grid { Children = { scroll, emptyText } };
        emptyText.VerticalAlignment = VerticalAlignment.Center;
        emptyText.MaxWidth = 360;
        var card = K.Card(K.Rows("auto, *", head.Row(0), body.Row(1)), new Thickness(0));

        Children.Add(BioUI.Work(left, card, orfList));
        void Model() => Refresh();
        Loaded += (_, _) => { GeneExpressionModel.Changed -= Model; GeneExpressionModel.Changed += Model; Refresh(); };
        Unloaded += (_, _) => GeneExpressionModel.Changed -= Model;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                field.Focus();
                e.Handled = true;
            }
        };
        Refresh();
    }

    private int? shownOrf;

    private void Refresh()
    {
        var input = GeneExpressionModel.FramesInput;
        var dna = GeneExpressionModel.FramesKind == BioSeqKind.Dna;
        var reverse = dna && GeneExpressionModel.ShowReverse;
        var preset = GXReadingFrames.Presets.FirstOrDefault(p => p.Sequence == input.Letters && p.Dna == dna);
        presetHost.Content = ChemKit.PresetMenu(new[] { ("Examples", (IReadOnlyList<string>)GXReadingFrames.Presets.Select(p => p.Name).ToList()) }, preset?.Name, name =>
        {
            if (GXReadingFrames.Presets.FirstOrDefault(p => p.Name == name) is not { } p) return;
            GeneExpressionModel.FramesKind = p.Dna ? BioSeqKind.Dna : BioSeqKind.Rna;
            GeneExpressionModel.FramesText = p.Sequence;
        });
        kindBar.Select(dna ? 0 : 1, false);
        GXKit.Keep(field, GeneExpressionModel.FramesText);
        messageHost.Content = input.Message is { } m
            ? GXKit.Problem(m, input.Suggestion != null ? "Switch" : null, input.Suggestion is { } s ? () => GeneExpressionModel.FramesKind = s : null)
            : null;
        count.Text = $"{input.Letters.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} nt, written 5′→3′ · up to 3,000 · FASTA, spaces and numbers are fine";
        var min = GeneExpressionModel.MinCodons;
        var stepper = ChemKit.Stepper(min, 2, 300, v => GeneExpressionModel.MinCodons = v, "Fewer codons", "More codons");
        var words = K.Text($"At least {min} codons", Ty.Body);
        words.VerticalAlignment = VerticalAlignment.Center;
        var stepRow = K.Columns("*, auto", words.Col(0), stepper.Col(1));
        stepRow.ToolTip = "An ORF counts its codons from the AUG up to and including the stop";
        stepperHost.Content = stepRow;
        if (reverseSwitch.IsChecked != GeneExpressionModel.ShowReverse) reverseSwitch.IsChecked = GeneExpressionModel.ShowReverse;
        reverseRow.IsEnabled = dna;
        reverseRow.Opacity = dna ? 1 : 0.45;
        reverseRow.ToolTip = dna ? "Read the reverse complement too" : "An mRNA is single-stranded: three frames";
        title.Text = reverse ? "Six reading frames" : "Three reading frames";
        var seq = input.Sequence;
        view.Set(seq, GeneExpressionModel.FrameRows, GeneExpressionModel.Orfs, GeneExpressionModel.PickedOrf, reverse);
        scroll.Visibility = seq != null ? Visibility.Visible : Visibility.Collapsed;
        emptyText.Visibility = seq == null ? Visibility.Visible : Visibility.Collapsed;
        emptyText.Text = input.Letters.Length == 0 ? "Type or paste a DNA or mRNA sequence on the left." : "Fix the sequence on the left to see its frames.";
        Orfs();
        if (GeneExpressionModel.PickedOrf != shownOrf)
        {
            shownOrf = GeneExpressionModel.PickedOrf;
            if (shownOrf is { } k) view.ScrollTo(k);
        }
    }

    private void Orfs()
    {
        orfList.Children.Clear();
        var orfs = GeneExpressionModel.Orfs;
        var rows = new List<UIElement?>();
        if (orfs.Count == 0) rows.Add(K.Wrapped("None that long. Lower the length, or check that the sequence has an ATG (AUG).", Ty.Body, B.Text2));
        for (var i = 0; i < Math.Min(orfs.Count, 40); i++) rows.Add(OrfRow(i, orfs[i]));
        if (orfs.Count > 40) rows.Add(K.Text($"+{orfs.Count - 40} more", Ty.Caption, B.Text2));
        orfList.Children.Add(BioUI.Card("Open reading frames", null, $"{orfs.Count} of at least {GeneExpressionModel.MinCodons} codons, longest first", rows.ToArray()));
        orfList.Children.Add(BioUI.Card("Which frame is used?", null, null,
            K.Wrapped("Only one frame is actually translated: usually the longest ORF, starting at the first AUG in a good context. A shift of 1 or 2 nt changes every amino acid after it.", Ty.Body),
            BioUI.Caption("Positions are 1-based on the strand as typed. A reverse-frame ORF runs from its start (the higher number) down to its end.")));
    }

    private static FrameworkElement OrfRow(int i, BioORF o)
    {
        var on = GeneExpressionModel.PickedOrf == i;
        var head = K.Columns("auto, auto, *, auto, auto");
        head.Children.Add(K.Text($"Frame {o.FrameLabel}", Ty.BodyStrong).Col(0));
        var span = K.Text($"{o.Start}–{o.End}", Ty.Body, B.Text2).Margin(8, 0, 0, 0);
        span.FontFeatures();
        head.Children.Add(span.Col(1));
        if (i == 0) head.Children.Add(GXKit.Badge("Longest", Appearance.Accent).Margin(6, 0, 6, 0).Col(3));
        var codons = K.Text($"{o.Codons} codons", Ty.Caption, B.Text2);
        codons.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(codons.Col(4));
        var protein = new TextBlock { Text = o.Protein + (o.HasStop ? "" : "…"), FontFamily = BioUI.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxHeight = 34, TextTrimming = TextTrimming.CharacterEllipsis };
        protein.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
        var v = K.V(4, head, protein);
        if (!o.HasStop) v.Children.Add(K.Text("open (no stop codon)", Ty.Caption).Fg(B.Caution));
        v.Margin = new Thickness(10);
        var face = ChemKit.Tint(v, on ? B.Accent : B.Text, on ? 0.16 : 0.05, 10);
        if (on) face.Children.Add(new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.5), IsHitTestVisible = false }.Stroke(B.Accent, 1.5));
        var b = K.Subtle(face, () => GeneExpressionModel.PickedOrf = on ? null : i, new Thickness(0), 10);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.ToolTip = "Show this ORF in the frames";
        System.Windows.Automation.AutomationProperties.SetName(b, $"Frame {o.FrameLabel}, {o.Start} to {o.End}, {o.Codons} codons{(o.HasStop ? "" : ", open, no stop codon")}{(i == 0 ? ", the longest" : "")}{(on ? ", picked" : "")}");
        return b;
    }
}

/// <summary>
/// The frames, line by line (the Mac's GXFrameBlock, one under another): for each run of the sequence, its positions,
/// the strand 5′→3′, the three forward frames, and (DNA) the complement 3′→5′ with the three reverse frames — each amino
/// acid centred under its codon, codons boxed in turn, ORFs barred. Only the lines in view are drawn (3,000 nt make a
/// hundred of them), again as the page scrolls.
/// </summary>
internal sealed class GXFramesView : GXCanvas
{
    private const double Gutter = 40, Cw = 14, RowH = 19, Gap = 16;
    private readonly ScrollViewer scroll;
    private string? sequence;
    private IReadOnlyList<GXFrameRow> rows = Array.Empty<GXFrameRow>();
    private IReadOnlyList<BioORF> orfs = Array.Empty<BioORF>();
    private int? picked;
    private bool reverse;
    private string? hovered;

    /// <summary>What is under the pointer, in words (null: nothing).</summary>
    public event Action<string?>? HoverChanged;

    public GXFramesView(ScrollViewer scroll)
    {
        this.scroll = scroll;
        Margin = new Thickness(16, 0, 16, 16);
        scroll.ScrollChanged += (_, _) => InvalidateVisual();
        System.Windows.Automation.AutomationProperties.SetName(this, "Reading frames");
    }

    public void Set(string? seq, IReadOnlyList<GXFrameRow> rows, IReadOnlyList<BioORF> orfs, int? picked, bool reverse)
    {
        var resize = seq?.Length != sequence?.Length || reverse != this.reverse;
        sequence = seq;
        this.rows = rows;
        this.orfs = orfs;
        this.picked = picked;
        this.reverse = reverse;
        if (resize) InvalidateMeasure();
        InvalidateVisual();
        System.Windows.Automation.AutomationProperties.SetHelpText(this, string.Join("; ", Shown.Select(r => $"frame {r.Label}: {new string(r.Cells.Select(c => c.Aa).ToArray())}")));
    }

    private IEnumerable<GXFrameRow> Shown => rows.Where(r => reverse || r.Frame > 0);
    private double BlockHeight => 16 + RowH * (1 + 3 + (reverse ? 4 : 0)) + 4;

    /// <summary>Bases to a line: as many tens as fit.</summary>
    private int Per(double width) => Math.Max(10, (int)((width - Gutter - 30) / Cw) / 10 * 10);

    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsInfinity(available.Width) ? 600 : available.Width;
        if (sequence is not { Length: > 0 } s) return new Size(w, 0);
        var blocks = (s.Length + Per(w) - 1) / Per(w);
        return new Size(w, blocks * BlockHeight + Math.Max(0, blocks - 1) * Gap);
    }

    /// <summary>The block an ORF begins in, scrolled to the top.</summary>
    public void ScrollTo(int k)
    {
        if (k < 0 || k >= orfs.Count || ActualWidth <= 0) return;
        var o = orfs[k];
        var b = (Math.Min(o.Start, o.End) - 1) / Per(ActualWidth);
        scroll.ScrollToVerticalOffset(b * (BlockHeight + Gap));
    }

    private static double X(int col, int start) => Gutter + (col - start + 0.5) * Cw;
    private static double Y(int line) => 16 + RowH * (line + 0.5);
    private static int Line(GXFrameRow r) => r.Frame > 0 ? r.Frame : 4 - r.Frame;

    protected override void Draw(DrawingContext dc, Size size)
    {
        if (sequence is not { Length: > 0 } seq) return;
        var per = Per(size.Width);
        var blocks = (seq.Length + per - 1) / per;
        var step = BlockHeight + Gap;
        // (only the blocks in view: where this sits in the scroll, and how much of it shows)
        double top;
        try { top = TranslatePoint(new Point(0, 0), scroll).Y; } catch { top = 0; }
        double from = -top - step, to = -top + scroll.ViewportHeight + step;
        var pickedOrf = picked is { } pk && pk >= 0 && pk < orfs.Count ? orfs[pk] : null;
        for (var b = Math.Max(0, (int)(from / step)); b < blocks && b * step < to; b++)
        {
            dc.PushTransform(new TranslateTransform(0, b * step));
            Block(dc, seq, b * per, Math.Min(per, seq.Length - b * per), pickedOrf);
            dc.Pop();
        }
    }

    private void Block(DrawingContext dc, string seq, int start, int count, BioORF? pickedOrf)
    {
        var end = start + count;
        var secondary = Secondary;
        var ppd = Ppd;
        // (positions: the line's first, then every tenth)
        for (var col = start; col < end; col++)
            if (col == start || (col + 1) % 10 == 0) Say(dc, $"{col + 1}", new Point(X(col, start), 7), 9.5, FontWeights.Normal, secondary, mono: true);
        // (the strand, and under the frames its complement)
        void Strand(int line, Func<int, char> letter, string l, string r)
        {
            Say(dc, l, new Point(Gutter - 14, Y(line)), 10.5, FontWeights.SemiBold, secondary);
            for (var col = start; col < end; col++)
            {
                var ch = letter(col);
                GXDraw.Draw(dc, GXDraw.Text(ch.ToString(), 12.5, FontWeights.SemiBold, BioColors.Base(ch), true, ppd), new Point(X(col, start), Y(line)));
            }
            Say(dc, r, new Point(X(end - 1, start) + Cw / 2 + 12, Y(line)), 10.5, FontWeights.SemiBold, secondary);
        }
        Strand(0, i => seq[i], "5′", "3′");
        if (reverse) Strand(4, i => BioSeq.Pair(seq[i], rna: false), "3′", "5′");
        // (the frames)
        var stop = GXColors.Stop.ToColor();
        var start0 = GXColors.Start.ToColor();
        var primary = Primary;
        var accent = Accent;
        foreach (var r in Shown)
        {
            var yy = Y(Line(r));
            Say(dc, r.Label, new Point(Gutter - 16, yy), 10.5, FontWeights.Bold, secondary, mono: true);
            var frameOrfs = orfs.Select((o, i) => (o, i)).Where(x => x.o.Frame == r.Frame).ToList();
            for (var k = 0; k < r.Cells.Count; k++)
            {
                var c = r.Cells[k];
                if (c.Column + 2 < start || c.Column >= end) continue;
                int a = Math.Max(c.Column, start), b = Math.Min(c.Column + 2, end - 1);
                var rect = new Rect(X(a, start) - Cw / 2 + 1, yy - RowH / 2 + 1.5, (b - a + 1) * Cw - 2, RowH - 3);
                var fill = c.Aa == '*' ? GXDraw.Brush(stop, 0.28) : c.Aa == 'M' ? GXDraw.Brush(start0, 0.26) : GXDraw.Brush(primary, k % 2 == 0 ? 0.05 : 0.1);
                GXDraw.Round(dc, fill, null, rect, 4);
                // (an ORF's bar under its codons; the picked one bold)
                foreach (var (o, i) in frameOrfs)
                {
                    int lo = Math.Min(o.Start, o.End) - 1, hi = Math.Max(o.Start, o.End) - 1;
                    if (c.Column < lo || c.Column + 2 > hi) continue;
                    var isPicked = ReferenceEquals(o, pickedOrf);
                    var h = isPicked ? 3 : 2;
                    dc.DrawRectangle(GXDraw.Brush(accent, isPicked ? 1 : (i == 0 ? 0.75 : 0.4)), null, new Rect(rect.X, rect.Bottom - h, rect.Width, h));
                }
                var mid = c.Column + 1;
                if (mid >= start && mid < end)
                {
                    var colour = c.Aa == '*' ? stop : c.Aa == 'M' ? start0 : primary;
                    var weight = c.Aa is 'M' or '*' ? FontWeights.Black : FontWeights.Medium;
                    GXDraw.Draw(dc, GXDraw.Text(c.Aa.ToString(), 12.5, weight, colour, true, ppd), new Point(X(mid, start), yy));
                }
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var text = Describe(e.GetPosition(this));
        if (text == hovered) return;
        hovered = text;
        HoverChanged?.Invoke(text);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        hovered = null;
        HoverChanged?.Invoke(null);
    }

    /// <summary>What is under the pointer, in words: its frame, codon, amino acid and positions.</summary>
    private string? Describe(Point p)
    {
        if (sequence is not { Length: > 0 } seq) return null;
        var per = Per(ActualWidth);
        var step = BlockHeight + Gap;
        var b = (int)Math.Floor(p.Y / step);
        var y = p.Y - b * step;
        var start = b * per;
        var count = Math.Min(per, seq.Length - start);
        if (b < 0 || count <= 0 || y > BlockHeight) return null;
        var col = start + (int)Math.Floor((p.X - Gutter) / Cw);
        if (col < start || col >= start + count) return null;
        var ln = (int)Math.Floor((y - 16) / RowH);
        var r = Shown.FirstOrDefault(x => Line(x) == ln);
        if (r == null) return ln == 0 ? $"Position {col + 1}: {seq[col]}" : null;
        var c = r.Cells.FirstOrDefault(x => col >= x.Column && col <= x.Column + 2);
        if (c == null) return null;
        var what = c.Aa == '*' ? "stop" : $"{GXKit.Three(c.Aa)} ({GXKit.Name(c.Aa).ToLowerInvariant()})";
        var strand = r.Frame > 0 ? $"nt {c.Column + 1}–{c.Column + 3}" : $"nt {c.Column + 3}–{c.Column + 1} on the reverse strand";
        return $"Frame {r.Label} · codon {c.Number} · {c.Codon} → {what} · {strand}";
    }
}
