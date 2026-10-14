using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// Gene Expression's Transcribe &amp; translate page (spec §4.3), the Mac's GXExpressPage: a gene made into a protein step by
/// step — the gene, a nucleotide a press through transcription, the RNA processed (eukaryote), a codon a press through
/// translation, and the protein — in the four-lane scene (GeneExpressionScene.cs) with the shared step-through controls
/// under it. Left: the gene (the made-up teaching gene, β-globin's coding sequence, or one's own), the cell type, the
/// stage groups and the colours. Right: what is happening now, the mRNA's parts, and the rules the model keeps. Run plays
/// an elongation through quickly; Skip jumps to its end.
/// </summary>
internal sealed class GXExpressPage : Grid
{
    private readonly ToolContext c;
    private readonly BioStepController steps = GeneExpressionModel.Steps;
    private readonly GXSceneView scene = new();
    private readonly BioStepAnimator animator;
    private readonly ContentControl presetHost = new() { Focusable = false };
    private readonly TextBlock geneNote = BioUI.Caption("");
    private readonly Stack ownEditor = K.V(8);
    private readonly SelectorBar ownKind;
    private readonly Wpf.Ui.Controls.TextBox ownField;
    private readonly ContentControl ownMessage = new() { Focusable = false };
    private readonly TextBlock ownCount = BioUI.Caption("");
    private readonly SelectorBar cellBar;
    private readonly TextBlock cellNote = BioUI.Caption("");
    private readonly ContentControl groupsHost = new() { Focusable = false };
    private readonly ContentControl titleHost = new() { Focusable = false };
    private readonly TextBlock summary = K.Text("", Ty.Body, B.Text2);
    private readonly ContentControl runHost = new() { Focusable = false };
    private readonly Grid sceneArea = new();
    private readonly FrameworkElement empty;
    private readonly TextBlock emptyText = K.Wrapped("", Ty.Body, B.Text2).Center();
    private readonly BioStepControls controls;
    private readonly Stack facts = K.V(14);
    private readonly DispatcherTimer runner = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private (int Lo, int Hi)? running;
    private int runExpected;
    private double? speedBeforeRun;
    private int drawnIndex = -1;

    public GXExpressPage(ToolContext c)
    {
        this.c = c;
        animator = new BioStepAnimator(steps, Draw);

        // (left: the gene, the cell, the stages, the colours)
        ownKind = GXKit.Choice(new[] { "Coding-strand DNA", "mRNA" }, GeneExpressionModel.OwnKind == BioSeqKind.Rna ? 1 : 0,
            i => { StopRun(); GeneExpressionModel.OwnKind = i == 1 ? BioSeqKind.Rna : BioSeqKind.Dna; });
        ownField = GXKit.Field(GeneExpressionModel.OwnText, "ATGGCC…", t => { StopRun(); GeneExpressionModel.OwnText = t; }, multiline: true);
        System.Windows.Automation.AutomationProperties.SetName(ownField, "Your sequence, 5 prime to 3 prime");
        ownField.ToolTip = "Coding-strand DNA or mRNA, 5′→3′ (Ctrl+F)";
        ownEditor.Children.Add(ownKind);
        ownEditor.Children.Add(ownField);
        ownEditor.Children.Add(ownMessage);
        ownEditor.Children.Add(ownCount);
        cellBar = GXKit.Choice(new[] { GXCell.Eukaryote.Label(), GXCell.Prokaryote.Label() }, GeneExpressionModel.Cell == GXCell.Prokaryote ? 1 : 0,
            i => { StopRun(); GeneExpressionModel.Cell = i == 1 ? GXCell.Prokaryote : GXCell.Eukaryote; });
        cellBar.ToolTip = "A eukaryote processes its RNA in the nucleus; a prokaryote translates as it transcribes";
        var left = K.V(14,
            BioUI.Heading("Gene"), presetHost, geneNote, ownEditor,
            BioUI.Heading("Cell type"), cellBar, cellNote,
            BioUI.Heading("Go to"), groupsHost,
            BioUI.Heading("Colours"), BaseChips(),
            BioUI.Legend(BioAAClasses.All.Select(k => (BioColors.Of(k), k.Label()))));

        // (centre: the scene and the controls)
        // (the title and its badges beside Run and Skip, the badges onto a line of their own when the card is narrow; the
        //  summary under both, across the card's width — beside the buttons they were cut short)
        titleHost.VerticalAlignment = VerticalAlignment.Center;
        runHost.VerticalAlignment = VerticalAlignment.Top;
        summary.TextWrapping = TextWrapping.Wrap;
        var top = K.Rows("auto, auto", K.Columns("*, auto", titleHost.Col(0), runHost.Margin(8, 0, 0, 0).Col(1)).Row(0), summary.Margin(0, 3, 0, 0).Row(1));
        top.Margin = new Thickness(16, 14, 16, 4);
        scene.ToolTip = "← → step · Space plays · 1–5 the stages · drag to look aside";
        BioStepKeys.Attach(scene, steps);
        var emptyIcon = K.Icon(SymbolRegular.TextT24, 28, B.Text2);
        var emptyStack = K.V(10, emptyIcon, emptyText);
        emptyStack.HorizontalAlignment = HorizontalAlignment.Center;
        emptyStack.VerticalAlignment = VerticalAlignment.Center;
        emptyStack.MaxWidth = 360;
        empty = emptyStack;
        sceneArea.Children.Add(scene);
        sceneArea.Children.Add(empty);
        controls = new BioStepControls(steps, GXProcess.Groups);
        controls.Margin = new Thickness(16, 4, 16, 14);
        var card = K.Card(K.Rows("auto, *, auto", top.Row(0), sceneArea.Row(1), controls.Row(2)), new Thickness(0));

        Children.Add(BioUI.Work(left, card, facts));
        runner.Tick += (_, _) => RunTick();
        void Model() => Refresh();
        void Stepped() => StepChanged();
        Loaded += (_, _) =>
        {
            GeneExpressionModel.Changed -= Model;
            GeneExpressionModel.Changed += Model;
            steps.Changed -= Stepped;
            steps.Changed += Stepped;
            animator.Start();
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            GeneExpressionModel.Changed -= Model;
            steps.Changed -= Stepped;
            animator.Stop();
            StopRun();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && GeneExpressionModel.Source == GXSource.Own)
            {
                ownField.Focus();
                e.Handled = true;
            }
        };
        Refresh();
    }

    private static GXProcess? Process => GeneExpressionModel.Process;

    private GXFrame? Frame => Process is { } p && steps.Index >= 0 && steps.Index < p.Frames.Count ? p.Frames[steps.Index] : null;

    private void Draw(BioStepPhase phase) => scene.Show(Process, phase);

    // ---- what the page shows ----

    private void Refresh()
    {
        var m = GeneExpressionModel.Process;
        presetHost.Content = ChemKit.PresetMenu(new[] { ("Genes", (IReadOnlyList<string>)Enum.GetValues<GXSource>().Select(s => s.Label()).ToList()) },
            GeneExpressionModel.Source.Label(), name =>
            {
                foreach (var s in Enum.GetValues<GXSource>())
                    if (s.Label() == name)
                    {
                        StopRun();
                        GeneExpressionModel.Source = s;
                    }
            });
        geneNote.Text = m?.Gene.Note ?? "Type a coding-strand DNA or an mRNA sequence below.";
        var own = GeneExpressionModel.Source == GXSource.Own;
        ownEditor.Visibility = own ? Visibility.Visible : Visibility.Collapsed;
        if (own)
        {
            GXKit.Keep(ownField, GeneExpressionModel.OwnText);
            ownKind.Select(GeneExpressionModel.OwnKind == BioSeqKind.Rna ? 1 : 0, false);
            var input = GeneExpressionModel.OwnInput;
            ownMessage.Content = input.Message is { } msg
                ? GXKit.Problem(msg, input.Suggestion != null ? "Switch" : null, input.Suggestion is { } sg ? () => GeneExpressionModel.OwnKind = sg : null)
                : null;
            ownCount.Text = $"{input.Letters.Length} nt, 5′→3′ · up to {GXProcess.Cap} for the step-through";
        }
        cellBar.Select(GeneExpressionModel.Cell == GXCell.Prokaryote ? 1 : 0, false);
        cellNote.Text = GeneExpressionModel.Cell == GXCell.Eukaryote
            ? "A nucleus: the RNA is capped, tailed and spliced, then exported; the ribosome scans from the cap to the first AUG."
            : "No nucleus and no introns: ribosomes bind the Shine–Dalgarno site, and can start before transcription ends.";

        // (the centre: the scene, or what to do when there is nothing to show)
        var title = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = K.Text(m?.Gene.Title ?? "Your sequence", Ty.Subtitle);
        name.Margin = new Thickness(0, 0, 8, 2);
        name.VerticalAlignment = VerticalAlignment.Center;
        title.Children.Add(name);
        void Badge(FrameworkElement b)
        {
            b.Margin = new Thickness(0, 0, 8, 2);
            b.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(b);
        }
        if (GeneExpressionModel.Source == GXSource.Teaching) Badge(GXKit.Badge("Made up for teaching", Colors.Gray));
        Badge(GXKit.Badge(GeneExpressionModel.Cell.Label(), Appearance.Accent));
        titleHost.Content = title;
        summary.Text = m != null ? Summary(m.Gene) : "";
        scene.Visibility = m != null ? Visibility.Visible : Visibility.Collapsed;
        controls.Visibility = m != null ? Visibility.Visible : Visibility.Collapsed;
        empty.Visibility = m == null ? Visibility.Visible : Visibility.Collapsed;
        emptyText.Text = GeneExpressionModel.OwnInput.Letters.Length == 0
            ? "Type a sequence on the left to transcribe and translate it."
            : "Fix the sequence on the left to transcribe and translate it.";
        drawnIndex = -1;
        scene.Show(m, steps.Phase());
        StepChanged();
    }

    /// <summary>What changes with each step: the stage buttons, Run and Skip, the facts, and the scene's words for Narrator.</summary>
    private void StepChanged()
    {
        // (a step taken: the view back on the work, from wherever a drag left it)
        if (drawnIndex != steps.Index)
        {
            drawnIndex = steps.Index;
            scene.Recentre();
        }
        Groups();
        Run();
        Facts();
        if (Process is { } p)
        {
            System.Windows.Automation.AutomationProperties.SetName(scene, $"Gene expression: {BioStepTiming.Caption(steps.Index, steps.Steps)}");
            System.Windows.Automation.AutomationProperties.SetHelpText(scene, Describe(p));
        }
    }

    private static string Summary(GXGene g)
    {
        var parts = new List<string> { $"{g.Coding.Length} bp" };
        if (g.Introns.Count > 0) parts.Add($"{g.PreMRNA.Length}-nt pre-mRNA");
        parts.Add($"{g.Mature.Length}-nt mRNA");
        parts.Add(g.Protein.Length == 0 ? "no protein" : $"{g.Protein.Length} amino acid{(g.Protein.Length == 1 ? "" : "s")}");
        return string.Join(" · ", parts);
    }

    /// <summary>The scene in words (Narrator), for the step shown.</summary>
    private string Describe(GXProcess p)
    {
        if (Frame is not { } f) return "";
        var g = p.Gene;
        switch (f.Stage)
        {
            case GXStage.Gene: return $"{g.Title}: {Summary(g)}.";
            case GXStage.TxInit or GXStage.TxElong or GXStage.TxEnd:
            {
                var rna = p.RnaAt(steps.Index);
                return rna.Length == 0 ? "The DNA opened at the start site." : $"RNA made so far, 5 prime to 3 prime: {string.Join(" ", rna[Math.Max(0, rna.Length - 30)..].Select(x => x.ToString()))}.";
            }
            case GXStage.Cap or GXStage.Tail or GXStage.Splice or GXStage.Export:
                return $"The mRNA: {g.Mature.Length} nucleotides{(f.Cap > 0.5 ? ", capped" : "")}{(f.Tail > 0.5 ? ", with a poly-A tail" : "")}.";
            case GXStage.Result: return $"The protein: {string.Join(" ", g.Protein.Select(x => x.ToString()))}.";
            default: return $"Polypeptide so far, N to C: {string.Join(" ", p.ChainAt(steps.Index).Select(x => x.ToString()))}.";
        }
    }

    // ---- left: the stage groups, the colours ----

    private void Groups()
    {
        var v = K.V(6);
        var currentGroup = steps.Current?.Group;
        foreach (var (g, name) in GXProcess.Groups.OrderBy(kv => kv.Key))
        {
            var available = BioStepTiming.StartOfGroup(g, steps.Steps) != null;
            var current = currentGroup == g;
            var key = K.Text($"{g}", Ty.Caption);
            key.FontFamily = BioUI.Mono;
            key.FontWeight = FontWeights.Bold;
            key.HorizontalAlignment = HorizontalAlignment.Center;
            key.VerticalAlignment = VerticalAlignment.Center;
            var keyBox = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Child = key };
            keyBox.Stroke(B.StrongStroke);
            var label = K.Text(name, current ? Ty.BodyStrong : Ty.Body);
            label.VerticalAlignment = VerticalAlignment.Center;
            var row = K.Columns("auto, *, auto", keyBox.Margin(0, 0, 10, 0).Col(0), label.Col(1));
            if (current) row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = K.Brush(Appearance.Accent), VerticalAlignment = VerticalAlignment.Center }.Col(2));
            var inner = ChemKit.Tint(row.Margin(10, 0, 10, 0), current ? B.Accent : B.Text, current ? 0.14 : 0.045, 8);
            inner.MinHeight = 32;
            row.VerticalAlignment = VerticalAlignment.Center;
            var group = g;
            var b = K.Subtle(inner, () => { StopRun(); steps.JumpToGroup(group); scene.Focus(); }, new Thickness(0), 8);
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            b.IsEnabled = available;
            b.Opacity = available ? 1 : 0.4;
            b.ToolTip = available ? $"{name} (key {g})" : "A prokaryote has no RNA processing: no nucleus, no introns";
            System.Windows.Automation.AutomationProperties.SetName(b, current ? $"{name}, shown" : name);
            v.Children.Add(b);
        }
        groupsHost.Content = v;
    }

    private static FrameworkElement BaseChips()
    {
        var row = K.H(8);
        var names = new Dictionary<char, string> { ['A'] = "Adenine", ['T'] = "Thymine (DNA only)", ['U'] = "Uracil (RNA only)", ['G'] = "Guanine", ['C'] = "Cytosine" };
        foreach (var b in "ATUGC")
        {
            var t = new TextBlock { Text = b.ToString(), FontFamily = BioUI.Mono, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = K.Brush(BioColors.Base(b)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Border { Width = 26, Height = 24, CornerRadius = new CornerRadius(5), Background = K.Brush(GXDraw.Alpha(BioColors.Base(b), 0.17)), Child = t, ToolTip = names[b] });
        }
        System.Windows.Automation.AutomationProperties.SetName(row, "Bases: A adenine, T thymine (DNA), U uracil (RNA), G guanine, C cytosine");
        return row;
    }

    // ---- Run and Skip ----

    /// <summary>The elongation the step is in or before (what Run plays and Skip jumps over).</summary>
    private ((int Lo, int Hi) Range, bool Transcription)? RunRange
    {
        get
        {
            if (Process is not { } p || Frame is not { } f) return null;
            if (f.Stage <= GXStage.TxElong && p.StepsOf(GXStage.TxElong) is { } r && steps.Index < r.Hi) return (r, true);
            if (f.Stage >= GXStage.TxEnd && f.Stage <= GXStage.TlElong && p.StepsOf(GXStage.TlElong) is { } r2 && steps.Index < r2.Hi) return (r2, false);
            return null;
        }
    }

    private void Run()
    {
        if (RunRange is not { } run)
        {
            runHost.Content = null;
            return;
        }
        var on = running != null;
        var go = K.Button(on ? "Stop" : (run.Transcription ? "Run transcription" : "Run translation"), () => ToggleRun(run.Range), accent: on,
            icon: on ? SymbolRegular.Stop20 : SymbolRegular.FastForward20);
        go.MinWidth = 0;
        go.ToolTip = run.Transcription ? "Add the nucleotides one after another, quickly" : "Read the codons one after another, quickly";
        var skip = K.Button("", () =>
        {
            StopRun();
            steps.Pause();
            steps.Go(run.Range.Hi);
        }, icon: SymbolRegular.Next20);
        skip.Content = null;
        skip.MinWidth = 0;
        skip.Padding = new Thickness(10, 6, 10, 6);
        var skipText = run.Transcription ? "Skip to the end of transcription" : "Skip to the end of translation";
        skip.ToolTip = skipText;
        System.Windows.Automation.AutomationProperties.SetName(skip, skipText);
        runHost.Content = K.H(8, go, skip);
    }

    /// <summary>Run: one nucleotide (or codon) after another, each move straight after the last, faster than Play (at least 3×).</summary>
    private void ToggleRun((int Lo, int Hi) r)
    {
        if (running != null)
        {
            StopRun();
            return;
        }
        steps.Pause();
        speedBeforeRun = steps.Speed;
        running = r;
        steps.Speed = Math.Max(steps.Speed, 3);
        if (steps.Index < r.Lo - 1) steps.Go(r.Lo - 1, animated: false);
        runExpected = steps.Index;
        scene.Focus();
        runner.Start();
        Run();
    }

    private void RunTick()
    {
        if (running is not { } r || steps.Index != runExpected || steps.Index >= r.Hi)
        {
            StopRun();
            return;
        }
        if (!steps.IsMoving())
        {
            steps.Next();
            runExpected = steps.Index;
        }
    }

    private void StopRun()
    {
        runner.Stop();
        if (running == null) return;
        running = null;
        if (speedBeforeRun is { } s) steps.Speed = s;
        speedBeforeRun = null;
        Run();
    }

    // ---- right: what is happening, the mRNA, the rules ----

    private void Facts()
    {
        facts.Children.Clear();
        if (Process is { } p && Frame is { } f)
        {
            facts.Children.Add(NowCard(p, f));
            if (f.Made >= 1) facts.Children.Add(MrnaCard(p.Gene, f));
        }
        facts.Children.Add(Rules());
    }

    private FrameworkElement NowCard(GXProcess p, GXFrame f)
    {
        var g = p.Gene;
        switch (f.Stage)
        {
            case GXStage.Gene:
                return BioUI.Card(g.Title, null, g.Source == GXSource.Teaching ? "Made up for this tool" : null,
                    BioUI.Facts(new (string, string, Color?)[]
                    {
                        ("Coding strand", $"{g.Coding.Length} bp", null),
                        ("Transcribed (from +1)", $"{g.PreMRNA.Length} nt", null),
                        ("Introns", $"{g.Introns.Count}", null),
                        ("Mature mRNA", $"{g.Mature.Length} nt", null),
                        ("Protein", g.Protein.Length == 0 ? "none" : $"{g.Protein.Length} aa", null),
                    }));
            case GXStage.TxInit or GXStage.TxElong or GXStage.TxEnd:
            {
                var k = (int)f.Made;
                var rows = new List<UIElement?>();
                if (f.Stage == GXStage.TxElong && k >= 1)
                {
                    var col = g.TxStart + k - 1;
                    var arrow = K.Icon(SymbolRegular.ArrowRight20, 16, B.Text2);
                    rows.Add(K.H(14, PairBox("Template", g.Template[col], "3′→5′"), arrow, PairBox("Added to RNA", g.PreMRNA[k - 1], "at its 3′ end")));
                    rows.Add(BioUI.FactRow("Pairing", "Template A → U, T → A, G → C, C → G. The RNA matches the coding strand, with U for T."));
                }
                var rna = p.RnaAt(steps.Index);
                rows.Add(BioUI.FactRow($"RNA so far ({rna.Length} nt)", rna.Length == 0 ? "None yet." : "5′-" + (rna.Length > 24 ? "…" : "") + rna[Math.Max(0, rna.Length - 24)..] + "-3′"));
                return BioUI.Card("Transcription", GXColors.Polymerase.ToColor(), g.Cell == GXCell.Eukaryote ? "RNA polymerase II, in the nucleus" : "RNA polymerase, in the cytoplasm", rows.ToArray());
            }
            case GXStage.Cap or GXStage.Tail or GXStage.Splice or GXStage.Export:
                return BioUI.Card("RNA processing", BioPalette.Rna.ToColor(), "In the nucleus, before the mRNA leaves",
                    BioUI.FactRow("5′ cap", f.Cap > 0.5 ? "Added: m⁷G, joined 5′ to 5′." : "Not yet."),
                    BioUI.FactRow("Poly-A tail", f.Tail > 0.5 ? "Added: about 200 A (no template)." : "Not yet."),
                    BioUI.FactRow("Splicing", f.Splice > 0.5 ? (g.Introns.Count == 0 ? "Nothing to cut in the sequence shown." : "The intron is out; the exons are joined.") : "Not yet."),
                    BioUI.FactRow("Export", f.Export > 0.5 ? "Through a nuclear pore, to the cytoplasm." : "Not yet."));
            case GXStage.TlInit or GXStage.TlElong:
            {
                var j = (int)Math.Round(f.PSite, MidpointRounding.AwayFromZero);
                var rows = new List<UIElement?>();
                if (j >= 0 && j < g.Codons.Count) rows.AddRange(CodonFacts("In the P site", g.Codons[j]));
                if (j + 1 < g.Codons.Count)
                {
                    var next = g.Codons[j + 1];
                    rows.Add(BioUI.FactRow("Next, in the A site", next.Aa == '*' ? $"{next.Codon}: a stop codon (read by a release factor)" : $"{next.Codon} → {GXKit.Three(next.Aa)}"));
                }
                rows.Add(ChainRow(p.ChainAt(steps.Index)));
                return BioUI.Card("Translation", GXColors.Ribosome.ToColor(), "The ribosome reads the mRNA 5′→3′, a codon at a time", rows.ToArray());
            }
            case GXStage.TlEnd or GXStage.NoStart:
            {
                var rows = new List<UIElement?>();
                if (f.Stage != GXStage.NoStart && g.HasStop && g.Codons.Count > 0)
                {
                    var stop = g.Codons[^1];
                    rows.Add(BioUI.FactRow("Stop codon", $"{stop.Codon} (“{BioCode.StopNames.GetValueOrDefault(stop.Codon, "stop")}”): no tRNA reads it; a release factor does."));
                }
                rows.Add(ChainRow(p.ChainAt(steps.Index)));
                return BioUI.Card(f.Stage == GXStage.NoStart ? "No start codon" : (g.HasStop ? "Termination" : "No stop codon"), GXColors.Stop.ToColor(), null, rows.ToArray());
            }
            default:
                return ResultCard(g);
        }
    }

    private static FrameworkElement PairBox(string label, char b, string sub)
    {
        var colour = BioColors.Base(b);
        var letter = new TextBlock { Text = b.ToString(), FontFamily = BioUI.Mono, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = K.Brush(colour), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var box = new Border { Width = 44, Height = 40, CornerRadius = new CornerRadius(9), Background = K.Brush(GXDraw.Alpha(colour, 0.16)), Child = letter, HorizontalAlignment = HorizontalAlignment.Center };
        var v = K.V(3, K.Text(label, Ty.Caption, B.Text2).Strong().Center(), box, K.Text(sub, Ty.Caption, B.Text2).Center());
        System.Windows.Automation.AutomationProperties.SetName(v, $"{label}: {b}, {sub}");
        return v;
    }

    private static IEnumerable<UIElement> CodonFacts(string label, GXCodon cd)
    {
        var codon = K.V(3, K.Text(label, Ty.Caption, B.Text2).Strong(), GXKit.Bases(cd.Codon, 18, FontWeights.Bold), K.Text($"codon {cd.Index + 1}, 5′→3′", Ty.Caption, B.Text2));
        var anti = new TextBlock { TextWrapping = TextWrapping.NoWrap };
        var pre = new System.Windows.Documents.Run("3′-") { FontSize = 12 };
        pre.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, B.Text2);
        anti.Inlines.Add(pre);
        foreach (var ch in BioCode.Anticodon3To5(cd.Codon))
            anti.Inlines.Add(new System.Windows.Documents.Run(ch.ToString()) { FontFamily = BioUI.Mono, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = K.Brush(BioColors.Base(ch)) });
        var post = new System.Windows.Documents.Run("-5′") { FontSize = 12 };
        post.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, B.Text2);
        anti.Inlines.Add(post);
        var antiBox = K.V(3, K.Text("Anticodon", Ty.Caption, B.Text2).Strong(), anti, K.Wrapped($"5′-{BioCode.Anticodon5To3(cd.Codon)}-3′ in tRNA tables", Ty.Caption, B.Text2));
        var pair = K.Columns("auto, *", codon.Margin(0, 0, 16, 0).Col(0), antiBox.Col(1));
        System.Windows.Automation.AutomationProperties.SetName(pair, $"{label}: codon {cd.Codon}, anticodon 3 prime {BioCode.Anticodon3To5(cd.Codon)} 5 prime");
        yield return pair;
        if (BioAminoAcids.ByOne.TryGetValue(cd.Aa, out var aa))
            yield return K.H(8, K.Dot(BioColors.Of(aa.Group), 10), K.Wrapped($"{aa.Name} ({aa.Three}, {aa.One}) · {aa.Group.Label()}", Ty.Body));
    }

    private static FrameworkElement ChainRow(string chain) =>
        K.V(3, K.Text($"Polypeptide so far ({chain.Length} aa, N→C)", Ty.Caption, B.Text2).Strong(), GXKit.Protein(chain, 14, "N-"));

    private FrameworkElement ResultCard(GXGene g)
    {
        if (g.Protein.Length == 0)
            return BioUI.Card("The protein", Appearance.Accent, "None", K.Wrapped("Without an AUG there is no reading frame, so no protein.", Ty.Body, B.Text2));
        var protein = PTKit.Selectable(g.Protein, 14, FontWeights.SemiBold, B.Text, BioUI.Mono);
        var open = ChemKit.Wide("Open in Biomolecule Builder", () => BioTools.Open(c, "biomol", new() { ["protein"] = g.Protein, ["page"] = "proteins" }), accent: true, icon: SymbolRegular.Cube24);
        open.ToolTip = "Build this chain in 3D (folded as an α-helix to start)";
        return BioUI.Card("The protein", Appearance.Accent, "N- to C-terminus", protein,
            BioUI.Facts(new (string, string, Color?)[]
            {
                ("Length", $"{g.Protein.Length} amino acids", null),
                ("Mass", BioAminoAcids.PeptideMass(g.Protein).ToString("0", CultureInfo.InvariantCulture) + " g/mol", null),
                ("Net charge at pH 7", BioAminoAcids.NetCharge(g.Protein).ToString("+0;−0;0", CultureInfo.InvariantCulture), null),
            }),
            open);
    }

    /// <summary>The mRNA as a labelled strip (§4.2): 5′ cap, 5′ UTR, coding sequence, 3′ UTR and poly-A tail — the intron
    /// too while it is still in.</summary>
    private static FrameworkElement MrnaCard(GXGene g, GXFrame f)
    {
        var spliced = g.Introns.Count == 0 || f.Splice > 0.5;
        var euk = g.Cell == GXCell.Eukaryote;
        var parts = new List<(string Name, int Length, Color Colour)>();
        if (euk && f.Cap > 0.5) parts.Add(("5′ cap", 0, BioPalette.Guanine.ToColor()));
        foreach (var r in g.Regions.OrderBy(r => r.Range.Lo))
        {
            int lo = Math.Max(r.Range.Lo, g.TxStart), hi = Math.Min(r.Range.Hi, g.TxEnd);
            if (lo > hi || (spliced && r.Kind == GXRegionKind.Intron)) continue;
            parts.Add((r.Name, hi - lo + 1, GXColors.RegionSolid(r.Kind)));
        }
        if (euk && f.Tail > 0.5) parts.Add(("Poly-A tail", 0, GXDraw.Alpha(BioPalette.Adenine.ToColor(), 0.7)));
        var strip = new Grid { Height = 16 };
        for (var i = 0; i < parts.Count; i++)
        {
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(parts[i].Length, 8), GridUnitType.Star) });
            strip.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = K.Brush(parts[i].Colour), Margin = new Thickness(i == 0 ? 0 : 1, 0, i == parts.Count - 1 ? 0 : 1, 0) }.Col(i));
        }
        var list = K.V(4);
        foreach (var part in parts)
        {
            var swatch = new Border { Width = 12, Height = 10, CornerRadius = new CornerRadius(3), Background = K.Brush(part.Colour), VerticalAlignment = VerticalAlignment.Center };
            var len = K.Text(part.Length > 0 ? $"{part.Length} nt" : (part.Name.StartsWith("Poly", StringComparison.Ordinal) ? "≈200 A" : "m⁷G"), Ty.Caption, B.Text2);
            len.FontFeatures();
            list.Children.Add(K.Columns("auto, *, auto", swatch.Margin(0, 0, 8, 0).Col(0), K.Text(part.Name, Ty.Caption).Col(1), len.Col(2)));
        }
        var title = spliced ? "The mRNA" : "The pre-mRNA";
        var sub = $"{(spliced ? g.Mature.Length : g.PreMRNA.Length)} nt{(euk && f.Cap > 0.5 ? ", plus cap" : "")}{(euk && f.Tail > 0.5 ? " and tail" : "")}";
        return BioUI.Card(title, null, sub, strip, list);
    }

    private static readonly string[] RuleLines =
    {
        "Transcription copies the template strand, so the mRNA equals the coding strand with U for T.",
        "The ribosome reads codons 5′→3′ from the first AUG: never the DNA, never 3′→5′.",
        "Anticodons pair antiparallel: codon 5′-AUG-3′ with anticodon 3′-UAC-5′ (5′-CAU-3′ in tRNA tables).",
        "Stop codons code for no amino acid; release factors, not tRNAs, read them.",
        "AUG both starts translation and codes Met inside a protein.",
        "The codon chooses the amino acid through its tRNA, which brings it; the ribosome does not make amino acids.",
    };

    private static FrameworkElement Rules() =>
        BioUI.Card("The rules it follows", null, null, RuleLines.Select(l => (UIElement?)K.Columns("auto, *", K.Text("•", Ty.Caption, B.Text2).Margin(0, 0, 6, 0).Col(0), K.Wrapped(l, Ty.Caption).Col(1))).ToArray());
}
