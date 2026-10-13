using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// Gene Expression's Mutations page (spec §4.3 Mutations, §7 items 16–17), the Mac's GXMutationsPage: a coding sequence (the
/// teaching gene's, human β-globin's or one's own) changed by a press — a base substituted, 1–6 bases inserted at a gap,
/// 1–6 deleted — or typed as genetics writes it (c.20A&gt;T); the original and the mutant lined up base for base, with their
/// mRNA codons and their proteins, the changes lit; and what kind of mutation it is, with its consequence. The sickle-cell
/// preset (HBB c.20A&gt;T) explains its numbering and its charge change.
/// </summary>
internal sealed class GXMutationsPage : Grid
{
    private readonly ContentControl geneHost = new() { Focusable = false };
    private readonly TextBlock sourceNote = BioUI.Caption("");
    private readonly ContentControl sickleHost = new() { Focusable = false };
    private readonly ContentControl toolsHost = new() { Focusable = false };
    private readonly Wpf.Ui.Controls.TextBox typedField;
    private readonly ContentControl typedNote = new() { Focusable = false };
    private readonly ContentControl examplesHost = new() { Focusable = false };
    private readonly ContentControl backHost = new() { Focusable = false };
    private readonly TextBlock title = K.Text("", Ty.Subtitle);
    private readonly TextBlock subtitle = K.Text("", Ty.Body, B.Text2);
    private readonly ContentControl kindHost = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly GXMutationRowsView rows = new();
    private readonly Border centre;
    private readonly Stack cards = K.V(14);

    // (the original's bases picked — a first and a last, at most 6 apart — or the gap after a base, for an insertion)
    private int? selStart, selEnd, gapAfter;
    private string insertText = "";
    private string? typedError;
    private GXMutationGene shownGene;
    private bool shownOwn;

    public GXMutationsPage()
    {
        typedField = GXKit.Field("", "c.20A>T, c.6_7insA, c.7del…", _ => { if (typedError != null) { typedError = null; Notes(); } });
        System.Windows.Automation.AutomationProperties.SetName(typedField, "A mutation, as genetics writes it");
        typedField.ToolTip = "Enter applies it (Ctrl+F)";
        typedField.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            ApplyTyped();
            e.Handled = true;
        };
        var left = K.V(14,
            BioUI.Heading("Coding sequence"), geneHost, sourceNote, sickleHost,
            BioUI.Heading("Make a change"), toolsHost,
            BioUI.Heading("Or type it"), typedField, typedNote,
            BioUI.Heading("Try one"), examplesHost, backHost);

        var head = K.Columns("*, auto", K.V(2, title, subtitle).Col(0), kindHost.Col(1));
        head.Margin = new Thickness(16, 14, 16, 8);
        scroll.Content = rows;
        rows.Picked += Press;
        centre = K.Card(K.Rows("auto, *", head.Row(0), scroll.Row(1)), new Thickness(0), 12);
        centre.Focusable = true;
        centre.FocusVisualStyle = null;
        centre.PreviewKeyDown += OnKeys;
        rows.MouseDown += (_, _) => centre.Focus();

        Children.Add(BioUI.Work(left, centre, cards));
        shownGene = GeneExpressionModel.MutationGene;
        void Model() => Refresh();
        Loaded += (_, _) => { GeneExpressionModel.Changed -= Model; GeneExpressionModel.Changed += Model; Refresh(); };
        Unloaded += (_, _) => GeneExpressionModel.Changed -= Model;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                typedField.Focus();
                e.Handled = true;
            }
        };
        Refresh();
    }

    private static GXMutationSource Source => GeneExpressionModel.MutationSource;
    private static string Original => Source.Cds;

    private (int Lo, int Hi)? Selection => selStart is { } a ? (Math.Min(a, selEnd ?? a), Math.Max(a, selEnd ?? a)) : null;

    private void ClearSelection()
    {
        selStart = null;
        selEnd = null;
        gapAfter = null;
        insertText = "";
    }

    // ---- what the page shows ----

    private void Refresh()
    {
        if (GeneExpressionModel.MutationGene != shownGene)
        {
            shownGene = GeneExpressionModel.MutationGene;
            ClearSelection();
        }
        // (the choice of coding sequence: "Your gene" only when there is one)
        var own = GeneExpressionModel.HasOwnCDS;
        var genes = Enum.GetValues<GXMutationGene>().Where(g => g != GXMutationGene.Current || own).ToList();
        if (geneHost.Content == null || own != shownOwn)
        {
            shownOwn = own;
            var bar = GXKit.Choice(genes.Select(g => g.Label()).ToList(), Math.Max(0, genes.IndexOf(GeneExpressionModel.MutationGene)), i => GeneExpressionModel.MutationGene = genes[i]);
            bar.ToolTip = "The teaching gene's, human β-globin's, or your own sequence's coding sequence (Transcribe & translate page)";
            geneHost.Content = bar;
        }
        if (geneHost.Content is SelectorBar sb) sb.Select(Math.Max(0, genes.IndexOf(GeneExpressionModel.MutationGene)), false);
        var o = Original;
        sourceNote.Text = $"{Source.Name}: {o.Length} nt, {o.Length / 3} codons, ATG to the stop.";
        var sickleOn = GeneExpressionModel.MutationGene == GXMutationGene.Hbb && Equals(GeneExpressionModel.Edit, GXMutations.SickleEdit);
        var sickle = ChemKit.Wide("Sickle-cell (HBB c.20A>T)", () => { ClearSelection(); GeneExpressionModel.Sickle(); }, accent: sickleOn, icon: SymbolRegular.Drop24);
        sickle.ToolTip = "β-globin with the sickle-cell mutation: GAG → GTG in codon 7";
        sickleHost.Content = sickle;
        Tools();
        Notes();
        Examples();
        var back = ChemKit.Wide("Back to the original", () => GeneExpressionModel.Edit = null, icon: SymbolRegular.ArrowUndo20);
        back.IsEnabled = GeneExpressionModel.Edit != null;
        backHost.Content = back;
        Comparison();
        Cards();
    }

    private void Tools()
    {
        var o = Original;
        if (gapAfter is { } g && g < o.Length - 1)
        {
            var field = GXKit.Field(insertText, "A", t => { insertText = t; }, mono: true);
            System.Windows.Automation.AutomationProperties.SetName(field, "Bases to insert");
            field.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                Insert();
                e.Handled = true;
            };
            var go = K.Button("Insert", Insert, accent: true);
            go.MinWidth = 0;
            go.Margin = new Thickness(8, 0, 0, 0);
            toolsHost.Content = K.V(8,
                K.Text($"Insert between c.{g + 1} and c.{g + 2}:", Ty.BodyStrong),
                K.Columns("*, auto", field.Col(0), go.Col(1)),
                BioUI.Caption($"1–{GXEdit.MaxLength} bases of A, C, G and T. Or type them with the gap picked."));
            return;
        }
        if (Selection is { } r)
        {
            var v = K.V(8);
            if (r.Lo == r.Hi)
            {
                var b = o[r.Lo];
                v.Children.Add(K.Wrapped($"c.{r.Lo + 1} is {b} (codon {r.Lo / 3 + 1}). Change it to:", Ty.BodyStrong));
                var grid = new Grid();
                for (var i = 0; i < 4; i++)
                {
                    if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var ch = "ACGT"[i];
                    var letter = new TextBlock { Text = ch.ToString(), FontFamily = BioUI.Mono, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = K.Brush(BioColors.Base(ch)), HorizontalAlignment = HorizontalAlignment.Center };
                    var btn = new Wpf.Ui.Controls.Button { Content = letter, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 34, MinWidth = 0, Padding = new Thickness(0) };
                    var at = r.Lo;
                    btn.Click += (_, e) => { e.Handled = true; Apply(new GXEdit.Substitute(at, ch)); };
                    btn.IsEnabled = ch != b;
                    btn.ToolTip = ch == b ? $"It is {ch} already" : $"Substitute {b} with {ch} (or press {ch})";
                    System.Windows.Automation.AutomationProperties.SetName(btn, ch == b ? $"{ch}, already" : $"Change to {ch}");
                    grid.Children.Add(btn.Col(i * 2));
                }
                v.Children.Add(grid);
            }
            else v.Children.Add(K.Wrapped($"c.{r.Lo + 1}–{r.Hi + 1}: {o[r.Lo..(r.Hi + 1)]} ({r.Hi - r.Lo + 1} bases)", Ty.BodyStrong));
            var del = ChemKit.Wide(r.Lo == r.Hi ? $"Delete c.{r.Lo + 1}" : $"Delete these {r.Hi - r.Lo + 1} bases", () => Apply(new GXEdit.Delete(r.Lo, r.Hi)), icon: SymbolRegular.Delete20);
            del.Fg(B.Critical);
            del.ToolTip = "Delete (Del)";
            v.Children.Add(del);
            toolsHost.Content = v;
            return;
        }
        toolsHost.Content = BioUI.Caption($"Press a base of the original to change or delete it (Shift-press to take up to {GXEdit.MaxLength}), or press between two bases to insert there. Keys: A C G T, Del, ← →.");
    }

    private void Notes()
    {
        typedNote.Content = typedError is { } e
            ? ChemKit.Line(SymbolRegular.ErrorCircle16, e, ChemKit.Orange)
            : BioUI.Caption("Substitution c.20A>T · insertion c.6_7insA · deletion c.7del or c.13_15del · duplication c.5dup. Positions count from the A of ATG.");
    }

    private void Examples()
    {
        var list = GXMutations.Examples(GeneExpressionModel.MutationGene, Original.Length);
        var face = K.Text("Examples", Ty.BodyStrong);
        var drop = ChemKit.DropDown(face, m =>
        {
            foreach (var (label, edit) in list)
            {
                var item = new MenuItem { Header = label, IsChecked = Equals(edit, GeneExpressionModel.Edit) };
                var e = edit;
                item.Click += (_, _) => { ClearSelection(); GeneExpressionModel.Edit = e; };
                m.Items.Add(item);
            }
        }, "Mutations of this coding sequence, one of each kind");
        examplesHost.Content = drop;
    }

    private void Comparison()
    {
        var res = GeneExpressionModel.Result;
        var o = Original;
        title.Text = res == null ? "The original" : "Original and mutant";
        subtitle.Text = res != null ? $"{res.Hgvs} · {res.Edit.Words(o)}" : "Codons boxed in turn from ATG; each amino acid under its codon.";
        kindHost.Content = res != null ? GXKit.Badge(res.Kind.Title(), GXColors.Kind(res.Kind)) : null;
        rows.Set(o, res, Selection, gapAfter);
    }

    // ---- making a change ----

    private bool InsertValid(out string bases)
    {
        bases = new string(insertText.ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        return bases.Length is >= 1 and <= GXEdit.MaxLength && bases.All(c => c is 'A' or 'C' or 'G' or 'T');
    }

    private void Insert()
    {
        if (gapAfter is not { } g || !InsertValid(out var bases)) return;
        Apply(new GXEdit.Insert(g, bases));
    }

    private void Apply(GXEdit e)
    {
        if (!e.Fits(Original)) return;
        ClearSelection();
        typedError = null;
        if (Equals(GeneExpressionModel.Edit, e)) Refresh();
        else GeneExpressionModel.Edit = e;
        rows.ScrollToChange(scroll);
    }

    private void ApplyTyped()
    {
        var (edit, error) = GXEdit.Parse(typedField.Text, Original);
        if (edit != null) Apply(edit);
        else
        {
            typedError = error;
            Notes();
        }
    }

    /// <summary>A press on the original: a base (Shift extends the pick, up to 6) or the gap after one.</summary>
    private void Press(GXMutationRowsView.Pick pick)
    {
        centre.Focus();
        if (pick.Gap)
        {
            gapAfter = pick.Index;
            selStart = null;
            selEnd = null;
        }
        else
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && selStart is { } a)
                selEnd = Math.Clamp(pick.Index, a - (GXEdit.MaxLength - 1), a + (GXEdit.MaxLength - 1));
            else
            {
                selStart = pick.Index;
                selEnd = null;
            }
            gapAfter = null;
        }
        Tools();
        rows.Set(Original, GeneExpressionModel.Result, Selection, gapAfter);
    }

    private void OnKeys(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBox || Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return;
        var n = Original.Length;
        char? b = e.Key switch { Key.A => 'A', Key.C => 'C', Key.G => 'G', Key.T => 'T', _ => null };
        if (b is { } ch && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (gapAfter is { } g) Apply(new GXEdit.Insert(g, ch.ToString()));
            else if (Selection is { } r && r.Lo == r.Hi) Apply(new GXEdit.Substitute(r.Lo, ch));
            else return;
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Delete or Key.Back when Selection is { } r:
                Apply(new GXEdit.Delete(r.Lo, r.Hi));
                break;
            case Key.Escape when selStart != null || gapAfter != null:
                ClearSelection();
                Tools();
                Comparison();
                break;
            case Key.Left or Key.Right when n > 0:
                var at = (selStart ?? gapAfter ?? -1) + (e.Key == Key.Left ? -1 : 1);
                selStart = Math.Clamp(at, 0, n - 1);
                selEnd = null;
                gapAfter = null;
                Tools();
                Comparison();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // ---- right: what kind of mutation, and what it does ----

    private void Cards()
    {
        cards.Children.Clear();
        if (GeneExpressionModel.Result is { } res)
        {
            var parts = new List<UIElement?>();
            foreach (var line in res.Lines) parts.Add(PTKit.Selectable(line, 14, FontWeights.Normal));
            if (res.From is { } a && res.To is { } b)
                parts.Add(K.H(8, Chip(a), K.Icon(SymbolRegular.ArrowRight16, 14, B.Text2), Chip(b)));
            if (res.Kept is { } k)
            {
                var bar = K.Bar(k, GXColors.Stop.ToColor(), 6);
                parts.Add(K.V(4, bar, K.Text($"{(int)Math.Round(k * 100, MidpointRounding.AwayFromZero)}% of the protein kept", Ty.Caption, B.Text2)));
            }
            parts.Add(BioUI.Facts(new (string, string, Color?)[]
            {
                ("Protein", $"{res.Original.Protein.Length} → {res.Mutant.Protein.Length} aa{(res.Mutant.StopAt == null ? " (no stop)" : "")}", null),
            }));
            cards.Children.Add(BioUI.Card(res.Kind.Title(), GXColors.Kind(res.Kind), res.Hgvs + (res.Change.Length == 0 ? "" : " · " + res.Change), parts.ToArray()));
            if (GeneExpressionModel.MutationGene == GXMutationGene.Hbb && Equals(GeneExpressionModel.Edit, GXMutations.SickleEdit)) cards.Children.Add(SickleCard());
        }
        else
        {
            cards.Children.Add(BioUI.Card("Make a mutation", null, null,
                K.Wrapped("Press a base, a gap or an example on the left. The card names the kind of mutation and what it does to the protein.", Ty.Body, B.Text2)));
        }
        cards.Children.Add(BioUI.Card("Good to know", null, null,
            K.Wrapped("Most point mutations are silent or missense, not frameshifts. Mutations do not “denature” proteins; they change the sequence, which may change folding. Many mutations have no effect.", Ty.Body)));
        cards.Children.Add(BioUI.Card("The kinds", null, null,
            Kind(GXMutationKind.Silent, "Silent", "codon changed, same amino acid"),
            Kind(GXMutationKind.Missense, "Missense", "a different amino acid"),
            Kind(GXMutationKind.Nonsense, "Nonsense", "a sense codon becomes a stop: the protein is cut short"),
            Kind(GXMutationKind.Frameshift, "Frameshift", "an insertion or deletion not a multiple of 3: every codon after it changes"),
            Kind(GXMutationKind.InFrameDeletion, "In-frame", "a multiple of 3 inserted or deleted: amino acids added or lost, the rest unchanged"),
            Kind(GXMutationKind.StartLoss, "Start-loss", "AUG destroyed: translation begins at the next AUG"),
            Kind(GXMutationKind.StopLoss, "Stop-loss", "the stop becomes a sense codon: translation runs on")));
    }

    private static FrameworkElement Kind(GXMutationKind k, string name, string text)
    {
        var words = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, FontFamily = K.SmallFace };
        words.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
        words.Inlines.Add(new System.Windows.Documents.Run(name) { FontWeight = FontWeights.SemiBold });
        words.Inlines.Add(new System.Windows.Documents.Run(": " + text));
        var dot = K.Dot(GXColors.Kind(k), 8);
        dot.VerticalAlignment = VerticalAlignment.Top;
        dot.Margin = new Thickness(0, 5, 8, 0);
        return K.Columns("auto, *", dot.Col(0), words.Col(1));
    }

    private static FrameworkElement Chip(BioAminoAcid a)
    {
        var colour = BioColors.Of(a.Group);
        var t = K.Text($"{a.Three} · {GXCodonTable.ClassWords(a)}", Ty.Caption);
        t.FontWeight = FontWeights.Medium;
        return new Border
        {
            Child = K.H(5, K.Dot(colour, 9), t), Padding = new Thickness(8, 3, 8, 4), CornerRadius = new CornerRadius(12),
            Background = K.Brush(GXDraw.Alpha(colour, 0.14)), VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static FrameworkElement SickleCard() =>
        BioUI.Card("Sickle-cell haemoglobin (HbS)", BioPalette.Thymine.ToColor(), null,
            BioUI.FactRow("Numbering", "Codon 7 counting ATG as codon 1 (p.Glu7Val in HGVS) is the classic Glu6Val (E6V): the β-globin chain in red cells has lost its first Met, so its Glu is number 6."),
            BioUI.FactRow("The change", "One base, A → T (GAG → GTG): a missense mutation, not a frameshift. The protein keeps its 147 amino acids (146 once the Met is removed)."),
            BioUI.FactRow("Charge", "Glu is negatively charged at cell pH; Val is nonpolar and uncharged. Each β chain loses a negative charge and gains a hydrophobic patch on its surface."),
            BioUI.FactRow("What it does", "When deoxygenated, HbS molecules stick to each other through that patch and form long fibres that bend red cells into sickles."),
            BioUI.Caption("This tool uses only HBB's coding sequence (its 3 exons joined)."));
}

/// <summary>
/// The original and the mutant lined up (the Mac's GXMutationRows, run under run): the codon numbers; the original DNA
/// (pressable), its mRNA and its protein; then the mutant's DNA (gaps where bases went), mRNA and protein — codons boxed
/// in turn on each, the changed bases, codons and amino acids lit.
/// </summary>
internal sealed class GXMutationRowsView : GXCanvas
{
    /// <summary>A base of the original pressed, or the gap after one.</summary>
    public readonly record struct Pick(int Index, bool Gap);

    private const double Gutter = 66, Cw = 17, RowH = 20, Gap = 18;
    private string original = "";
    private GXMutationResult? result;
    private (int Lo, int Hi)? selection;
    private int? gapAfter;
    private int? hover;
    private List<GXAlignedBase> aligned = new();
    private GXTranslation translation = new("");

    public event Action<Pick>? Picked;
    public event Action? HoverChanged;

    public GXMutationRowsView()
    {
        Margin = new Thickness(16, 0, 16, 16);
        Cursor = Cursors.Hand;
    }

    public void Set(string original, GXMutationResult? result, (int Lo, int Hi)? selection, int? gapAfter)
    {
        var resize = original != this.original || !ReferenceEquals(result, this.result);
        this.original = original;
        this.result = result;
        this.selection = selection;
        this.gapAfter = gapAfter;
        aligned = result?.Aligned.ToList() ?? original.Select((b, i) => new GXAlignedBase(b, b, i, i)).ToList();
        translation = result?.Original ?? new GXTranslation(original);
        if (resize) InvalidateMeasure();
        InvalidateVisual();
        var mutant = new string(aligned.Where(a => a.Mutant != null).Select(a => a.Mutant!.Value).ToArray());
        System.Windows.Automation.AutomationProperties.SetName(this, result == null ? $"Original {original}" : $"Original {original}, mutant {mutant}");
    }

    private int Lines => result == null ? 4 : 7;
    private double BlockHeight => Lines * RowH + 6;
    private int Per(double width) => Math.Max(15, (int)((width - Gutter - 8) / Cw) / 3 * 3);

    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsInfinity(available.Width) ? 600 : available.Width;
        var blocks = (aligned.Count + Per(w) - 1) / Per(w);
        return new Size(w, blocks * BlockHeight + Math.Max(0, blocks - 1) * Gap);
    }

    /// <summary>The first change brought into view.</summary>
    public void ScrollToChange(ScrollViewer scroll)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var first = aligned.FindIndex(a => a.Changed);
            if (first < 0 || ActualWidth <= 0) return;
            var b = first / Per(ActualWidth);
            var y = b * (BlockHeight + Gap);
            if (y < scroll.VerticalOffset || y + BlockHeight > scroll.VerticalOffset + scroll.ViewportHeight)
                scroll.ScrollToVerticalOffset(Math.Max(0, y - scroll.ViewportHeight / 2 + BlockHeight / 2));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static double X(int col, int from) => Gutter + (col - from + 0.5) * Cw;
    private static double Y(int line) => RowH * (line + 0.5) + 2;

    protected override void Draw(DrawingContext dc, Size size)
    {
        var per = Per(size.Width);
        for (int from = 0, b = 0; from < aligned.Count; from += per, b++)
        {
            dc.PushTransform(new TranslateTransform(0, b * (BlockHeight + Gap)));
            Block(dc, from, Math.Min(per, aligned.Count - from));
            dc.Pop();
        }
    }

    /// <summary>Codon spans on the aligned columns: for each codon of one side, the columns its three bases are in.</summary>
    private List<(int Codon, int First, int Last)> Spans(bool mutant)
    {
        var o = new SortedDictionary<int, (int, int)>();
        for (var col = 0; col < aligned.Count; col++)
        {
            if ((mutant ? aligned[col].MutantIndex : aligned[col].OriginalIndex) is not { } i) continue;
            var k = i / 3;
            o[k] = o.TryGetValue(k, out var s) ? (s.Item1, col) : (col, col);
        }
        return o.Select(kv => (kv.Key, kv.Value.Item1, kv.Value.Item2)).ToList();
    }

    private void Block(DrawingContext dc, int from, int count)
    {
        var end = from + count;
        var names = result == null ? new[] { "codon", "Original", "mRNA", "protein" } : new[] { "codon", "Original", "mRNA", "protein", "Mutant", "mRNA", "protein" };
        for (var k = 0; k < names.Length; k++)
        {
            var bold = names[k] is "Original" or "Mutant";
            Say(dc, names[k], new Point(Gutter - 8, Y(k)), 10.5, bold ? FontWeights.Bold : FontWeights.Medium, bold ? Primary : Secondary, GXDraw.Anchor.Trailing);
        }
        // (the original: codon numbers, boxes, bases, mRNA, protein)
        var oSpans = Spans(false);
        var o = translation;
        Side(dc, oSpans, 1, a => a.Original, a => a.OriginalIndex, o.StopAt ?? int.MaxValue, o.Codons.Count * 3, o.Aa,
            result?.ChangedOriginal ?? new HashSet<int>(), from, end, true);
        foreach (var s in oSpans.Where(s => s.First >= from && s.First < end && (s.Codon == 0 || (s.Codon + 1) % 5 == 0)))
            Say(dc, $"{s.Codon + 1}", new Point((X(s.First, from) + X(Math.Min(s.Last, end - 1), from)) / 2, Y(0)), 9.5, FontWeights.Normal, Secondary, mono: true);
        if (result is { } r)
            Side(dc, Spans(true), 4, a => a.Mutant, a => a.MutantIndex, r.Mutant.StopAt ?? int.MaxValue, r.Mutant.Codons.Count * 3, r.Mutant.Aa,
                r.ChangedMutant, from, end, false);
    }

    private void Side(DrawingContext dc, List<(int Codon, int First, int Last)> spans, int line, Func<GXAlignedBase, char?> baseOf,
        Func<GXAlignedBase, int?> indexOf, int stopAt, int total, Func<int, char> aa, IReadOnlySet<int> changedCodons, int from, int end, bool interactive)
    {
        double dnaY = Y(line), rnaY = Y(line + 1), aaY = Y(line + 2);
        var changed = GXColors.Changed;
        var primary = Primary;
        var ppd = Ppd;
        // (codon boxes in turn, a changed codon lit, codons past the stop dashed: not translated)
        foreach (var s in spans.Where(s => s.Last >= from && s.First < end))
        {
            int a = Math.Max(s.First, from), b = Math.Min(s.Last, end - 1);
            foreach (var yy in new[] { dnaY, rnaY })
            {
                var rect = new Rect(X(a, from) - Cw / 2 + 1, yy - RowH / 2 + 2, (b - a + 1) * Cw - 2, RowH - 4);
                if (s.Codon > stopAt) GXDraw.Round(dc, null, GXDraw.Dashed(Secondary, 1, 2, 2, 0.35), rect, 4);
                else if (changedCodons.Contains(s.Codon)) GXDraw.Round(dc, GXDraw.Brush(changed, 0.2), GXDraw.Pen(changed, 1, 0.8), rect, 4);
                else GXDraw.Round(dc, GXDraw.Brush(primary, s.Codon % 2 == 0 ? 0.05 : 0.1), null, rect, 4);
            }
            // (the amino acid under the codon's middle; a last codon of fewer than three bases has none)
            var mid = (s.First + s.Last) / 2;
            if (mid < from || mid >= end || 3 * s.Codon + 2 >= total) continue;
            var letter = aa(s.Codon);
            if (s.Codon > stopAt)
            {
                Say(dc, "·", new Point(X(mid, from), aaY), 12, FontWeights.Normal, Secondary);
                continue;
            }
            var colour = letter == '*' ? GXColors.Stop.ToColor() : BioColors.AminoAcid(letter);
            if (letter != ' ') dc.DrawEllipse(GXDraw.Brush(colour, 0.85), null, new Point(X(mid, from), aaY), 8.5, 8.5);
            if (changedCodons.Contains(s.Codon)) dc.DrawEllipse(null, GXDraw.Pen(changed, 1.5), new Point(X(mid, from), aaY), 10.5, 10.5);
            GXDraw.Draw(dc, GXDraw.Text(letter.ToString(), 11, FontWeights.Bold, Colors.White, true, ppd), new Point(X(mid, from), aaY));
        }
        // (the bases: DNA, then mRNA (U for T); gaps as dashes; changed bases lit)
        var accent = Accent;
        for (var col = from; col < end; col++)
        {
            var a = aligned[col];
            var i = indexOf(a);
            var cellRect = new Rect(X(col, from) - Cw / 2 + 1, dnaY - RowH / 2 + 1, Cw - 2, RowH - 2);
            if (interactive && i is { } ix)
            {
                if (selection is { } r && ix >= r.Lo && ix <= r.Hi) GXDraw.Round(dc, GXDraw.Brush(accent, 0.35), null, cellRect, 4);
                else if (hover == ix) GXDraw.Round(dc, GXDraw.Brush(accent, 0.15), null, cellRect, 4);
                if (gapAfter == ix)
                {
                    var gx = X(col, from) + Cw / 2;
                    GXDraw.Round(dc, GXDraw.Brush(accent), null, new Rect(gx - 1.5, dnaY - RowH / 2, 3, RowH), 1.5);
                }
            }
            if (baseOf(a) is not { } b)
            {
                Say(dc, "–", new Point(X(col, from), dnaY), 13, FontWeights.Bold, changed, mono: true);
                Say(dc, "–", new Point(X(col, from), rnaY), 12, FontWeights.Normal, changed, mono: true);
                continue;
            }
            if (a.Changed && !interactive)
                GXDraw.Round(dc, GXDraw.Brush(changed, 0.45), null, new Rect(X(col, from) - Cw / 2 + 2, dnaY - RowH / 2 + 2, Cw - 4, RowH - 4), 3);
            GXDraw.Draw(dc, GXDraw.Text(b.ToString(), 13, FontWeights.SemiBold, BioColors.Base(b), true, ppd), new Point(X(col, from), dnaY));
            var rb = b == 'T' ? 'U' : b;
            GXDraw.Draw(dc, GXDraw.Text(rb.ToString(), 12, FontWeights.Normal, BioColors.Base(rb), true, ppd), new Point(X(col, from), rnaY));
        }
    }

    /// <summary>What a point on the original's DNA is: a base, or (near a base's edge) the gap after the base to its left.</summary>
    private Pick? Locate(Point p)
    {
        var per = Per(ActualWidth);
        var step = BlockHeight + Gap;
        var blk = (int)Math.Floor(p.Y / step);
        var y = p.Y - blk * step;
        if (blk < 0 || y < Y(1) - RowH / 2 || y > Y(1) + RowH / 2) return null;
        var from = blk * per;
        var count = Math.Min(per, aligned.Count - from);
        var u = (p.X - Gutter) / Cw;
        var col = from + (int)Math.Floor(u);
        if (col < from || col >= from + count) return null;
        var into = u - Math.Floor(u);
        var n = aligned.Count(a => a.OriginalIndex != null);
        int? OriginalLeft(int c)
        {
            for (var k = c; k >= 0; k--) if (aligned[k].OriginalIndex is { } i) return i;
            return null;
        }
        if (into > 0.82 && aligned[col].OriginalIndex is { } i1 && i1 < n - 1) return new Pick(i1, true);
        if (into < 0.18 && col > 0 && OriginalLeft(col - 1) is { } i2 && i2 < n - 1) return new Pick(i2, true);
        if (aligned[col].OriginalIndex is { } i3) return new Pick(i3, false);
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var at = Locate(e.GetPosition(this));
        int? h = at is { Gap: false } pk ? pk.Index : null;
        if (h == hover) return;
        hover = h;
        ToolTip = h is { } ix && ix < original.Length ? $"c.{ix + 1} {original[ix]} (codon {ix / 3 + 1})" : null;
        InvalidateVisual();
        HoverChanged?.Invoke();
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
        if (Locate(e.GetPosition(this)) is { } pick) Picked?.Invoke(pick);
    }
}
