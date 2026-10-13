using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// Cell Division (spec §4.6, §5.2), the Mac's CellDivisionTool.swift: mitosis and meiosis stage by stage on a 2D cell with
// the counts at every step — chromosomes, DNA molecules, ploidy and DNA amount, whether homologs are present and whether
// sisters are joined, each with the rule behind it (the misconceptions Kalas et al. measured: ploidy is sets, not
// chromatids; a replicated chromosome is one chromosome; DNA copies once, in S; meiosis I parts homologs and meiosis II
// sisters; homologs are there in mitosis too) — animal or plant, 2n = 4 or 6, and in meiosis crossing over (the chiasma
// where you put it), independent assortment and nondisjunction; the two side by side on the Compare page over §5.2.3's
// table; and the cell cycle's wheel with its checkpoints, cyclins and what goes wrong in cancer (CellCycleView.cs). The
// model, its keyframes, counts and drawing list are Model/CellDivision*.cs; the drawing's view is CellDivisionCanvas.cs.
// What it shows and where each step-through is live in CellDivisionModel, so they outlive this view; the settings are
// remembered in Prefs (SimplBio.divide.<field>, the Mac's fields).
//
// Launch state (--tool divide --tool-state <state>, parts split at ":"): a page (mitosis, meiosis, compare, cycle), then
// any of plant, 2n=6, nocross, chiasma=0.3, flip, nd=1 or nd=2, nolabels, step=<n> (1-based, on the page shown),
// pick=<chromatid index>, part=<label part>, gate=g1s|g2m|spindle or phase=g1|s|g2|m|g0 (Cell cycle), cancer — the
// Mac's words. --bio-cell plant and --bio-step <n> read the same.

#pragma warning disable CA2255
internal static class CellDivisionToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("divide", (c, state) => new CellDivisionTool(c, state), CellDivisionTool.Pages);
}
#pragma warning restore CA2255

/// <summary>Cell Division: the page chosen in its header, made when it is shown.</summary>
internal sealed class CellDivisionTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("mitosis", "Mitosis", SymbolRegular.ShapeUnion24, "Mitosis, stage by stage"),
        new("meiosis", "Meiosis", SymbolRegular.Shapes24, "Meiosis I and II, stage by stage"),
        new("compare", "Compare", SymbolRegular.SplitVertical24, "Mitosis and meiosis side by side"),
        new("cycle", "Cell cycle", SymbolRegular.ArrowSync24, "The cell cycle and its checkpoints"),
    };

    private const string Key = "divide";
    private static bool loaded;
    private readonly ToolContext c;
    private string? shown;

    public CellDivisionTool(ToolContext c, BioLaunchState? state)
    {
        this.c = c;
        Load();
        if (state != null) Apply(state);
        CellDivisionModel.Prime();
        Show();
        void Nav(string k) { if (k == Key) Show(); }
        Loaded += (_, _) => { BioNav.Changed -= Nav; BioNav.Changed += Nav; Show(); };
        Unloaded += (_, _) => BioNav.Changed -= Nav;
    }

    /// <summary>The settings remembered from last time (once a run).</summary>
    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        CellDivisionModel.ShowLabels = BioPrefs.Bool(Key, "labels", true);
        CellDivisionModel.Set(new DivisionOptions
        {
            Plant = BioPrefs.Bool(Key, "plant"),
            Pairs = BioPrefs.String(Key, "pairs") == "3" ? 3 : 2,
            CrossingOver = BioPrefs.Bool(Key, "crossing", true),
            Chiasma = BioPrefs.Double(Key, "chiasma", 0.5),
            Flipped = new[] { false, BioPrefs.Bool(Key, "flip1"), BioPrefs.Bool(Key, "flip2") },
            Nondisjunction = BioPrefs.String(Key, "nd") switch
            {
                "meiosisI" => DivisionNondisjunction.MeiosisI,
                "meiosisII" => DivisionNondisjunction.MeiosisII,
                _ => DivisionNondisjunction.None,
            },
        });
    }

    /// <summary>A change of options: worked out again and remembered (on this PC only).</summary>
    public static void Update(DivisionOptions o)
    {
        CellDivisionModel.Set(o);
        var x = CellDivisionModel.Options;
        BioPrefs.Set(Key, "plant", x.Plant);
        BioPrefs.Set(Key, "pairs", x.Pairs.ToString(CultureInfo.InvariantCulture));
        BioPrefs.Set(Key, "crossing", x.CrossingOver);
        BioPrefs.Set(Key, "chiasma", x.Chiasma);
        BioPrefs.Set(Key, "flip1", x.Flipped[1]);
        BioPrefs.Set(Key, "flip2", x.Flipped[2]);
        BioPrefs.Set(Key, "nd", x.Nondisjunction.Raw());
    }

    public static void SetLabels(bool on)
    {
        if (CellDivisionModel.ShowLabels == on) return;
        CellDivisionModel.ShowLabels = on;
        BioPrefs.Set(Key, "labels", on);
        CellDivisionModel.Tell();
    }

    /// <summary>What a launch asked (the pictures'), not remembered.</summary>
    private static void Apply(BioLaunchState s)
    {
        var o = CellDivisionModel.Options;
        var cell = s.Value("cell")?.ToLowerInvariant();
        if (s.Has("plant") || cell == "plant") o = o with { Plant = true };
        if (s.Has("animal") || cell == "animal") o = o with { Plant = false };
        if (s.Value("2n") == "6" || s.Has("six")) o = o with { Pairs = 3 };
        else if (s.Value("2n") == "4") o = o with { Pairs = 2 };
        if (s.Has("nocross")) o = o with { CrossingOver = false };
        if (double.TryParse(s.Value("chiasma"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ch)) o = o with { Chiasma = ch };
        if (s.Has("flip")) o = o with { Flipped = new[] { false, true, o.Flipped[2] } };
        o = s.Value("nd") switch
        {
            "1" => o with { Nondisjunction = DivisionNondisjunction.MeiosisI },
            "2" => o with { Nondisjunction = DivisionNondisjunction.MeiosisII },
            "0" => o with { Nondisjunction = DivisionNondisjunction.None },
            _ => o,
        };
        CellDivisionModel.Set(o);
        if (s.Has("nolabels") || s.Value("labels") == "off") CellDivisionModel.ShowLabels = false;
        if (int.TryParse(s.Value("step"), out var step))
        {
            CellDivisionModel.Prime();
            var steps = BioNav.Page(Key) switch
            {
                "meiosis" => CellDivisionModel.MeiosisSteps,
                "compare" => CellDivisionModel.CompareSteps,
                _ => CellDivisionModel.MitosisSteps,
            };
            steps.Go(Math.Clamp(step - 1, 0, steps.Steps.Count - 1), animated: false);
        }
        if (int.TryParse(s.Value("pick"), out var pick) && pick >= 0 && pick < 4 * CellDivisionModel.Options.Pairs) CellDivisionModel.Selected = $"chromatid:{pick}";
        if (s.Value("part") is { } part && DivisionModel.Parts.ContainsKey(part)) CellDivisionModel.Selected = part;
        if ((s.Value("gate") ?? s.Value("phase")) is { } g && CycleOrder.Ids.Contains(g)) CellDivisionModel.CycleSelected = g;
        if (s.Has("cancer")) CellDivisionModel.Cancer = true;
        launchedPick = CellDivisionModel.Selected;
    }

    /// <summary>A pick the launch asked for: kept when its page first shows (any other pick belongs to the page it was
    /// made on, and goes when the page changes).</summary>
    private static string? launchedPick;

    private void Show()
    {
        var page = BioNav.Page(Key);
        if (page == shown) return;
        if (shown != null || launchedPick == null) CellDivisionModel.Selected = null;
        launchedPick = null;
        shown = page;
        Children.Clear();
        FrameworkElement view = page switch
        {
            "meiosis" => new DivisionProcessPage(DivisionProcess.Meiosis),
            "compare" => new DivisionComparePage(),
            "cycle" => new CellCyclePage(c),
            _ => new DivisionProcessPage(DivisionProcess.Mitosis),
        };
        Children.Add(view);
        if (Motion.On) Motion.FadeIn(view, Motion.Medium);
    }
}

// ---- the cards a press shows ----

/// <summary>A labelled part's card (the model's words, the Mac's), or a picked chromosome's, worked out from where it is.</summary>
internal static class DivParts
{
    public static DivPartInfo? Of(string? part) => part != null && DivisionModel.Parts.TryGetValue(part, out var p) ? p : null;

    /// <summary>"Long chromosome, from the mother" for a chromatid of a sequence at a step.</summary>
    public static string ChromatidName(DivisionSequence? seq, int step, int i)
    {
        if (seq == null || seq.Keyframes.Count == 0) return "";
        var f = seq.Keyframes[Math.Clamp(step, 0, seq.Keyframes.Count - 1)].Frame;
        if (i < 0 || i >= f.Chromatids.Length) return "";
        var id = f.Chromatids[i].Id;
        return $"{Cap(DivGeometry.Pairs[id.Pair].Name)} chromosome, from the {(id.Homolog == 0 ? "mother" : "father")}";
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>A picked chromosome's card: which pair and parent, what it is right now, its homolog, and whether it crossed
    /// over.</summary>
    public static Border ChromosomeCard(DivChromatidKey key, DivisionFrame frame, Action close)
    {
        var id = key.Id;
        var pair = DivGeometry.Pairs[id.Pair];
        string parent = id.Homolog == 0 ? "mother" : "father", other = id.Homolog == 0 ? "father" : "mother";
        var sister = frame.Chromatids.FirstOrDefault(c => c.Id == new DivChromatidID(id.Pair, id.Homolog, 1 - id.Sister));
        var now = !key.Exists
            ? "Not made yet: this copy is made in S phase, when the DNA is replicated."
            : key.Joined
            ? "Two sister chromatids joined at one centromere: 1 chromosome, 2 DNA molecules. Its sister is an identical copy made in S phase."
            : !sister.Exists
                ? "One chromosome, one DNA molecule: not copied yet."
                : "A single chromatid with its own centromere, so it counts as a chromosome: 1 chromosome, 1 DNA molecule.";
        var rows = new List<UIElement?>
        {
            BioUI.FactRow("Right now", now),
            BioUI.FactRow("Its homolog", $"The {other}'s {pair.Name} chromosome ({(id.Homolog == 0 ? "blue" : "red")}): the same genes in the same order, perhaps different alleles. It is not a copy of this one."),
        };
        if (key.Recombined > 0.5)
            rows.Add(BioUI.FactRow("Crossed over", $"Beyond the chiasma this chromatid carries the {other}'s DNA: a recombinant chromatid, from crossing over in prophase I."));
        rows.Add(DivUI.Close(close));
        return BioUI.Card($"{Cap(pair.Name)} chromosome, from the {parent}", DivColors.Of(DivPainter.Parent(id.Homolog), Appearance.Dark),
            $"{pair.Units:0} units long; centromere {pair.Centromere * 100:0}% along", rows.ToArray());
    }

    /// <summary>A labelled part's card.</summary>
    public static Border PartCard(DivPartInfo info, Action close) =>
        BioUI.Card(info.Title, Appearance.Accent, null,
            BioUI.FactRow("What it is", info.What),
            info.MixUp is { } m ? BioUI.FactRow("Common mix-up", m, K.Tone("orange")) : null,
            DivUI.Close(close));

    /// <summary>The step's counts in a sentence (Narrator and the drawing's name).</summary>
    public static string Describe(DivisionSequence seq, int step)
    {
        if (step < 0 || step >= seq.Counts.Count) return "";
        var c = seq.Counts[step];
        return $"Chromosomes {c.Chromosomes}; DNA molecules {c.Chromatids}; ploidy {c.Ploidy}; DNA {c.Dna}.";
    }
}

// ---- the pieces the pages share ----

internal static class DivUI
{
    /// <summary>A labelled switch row (the molecule builder's).</summary>
    public static FrameworkElement Toggle(string text, ToggleSwitch s, string? tip = null)
    {
        s.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(s, text);
        if (tip != null) s.ToolTip = tip;
        return K.Columns("*, auto", K.Wrapped(text, Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), s.Col(1));
    }

    /// <summary>Text with the specification's **bold** key terms bold.</summary>
    public static TextBlock Rich(string s, Ty ty = Ty.Body, string brush = B.Text)
    {
        var t = K.Wrapped("", ty, brush);
        var parts = s.Split("**");
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            t.Inlines.Add(i % 2 == 1 ? new Bold(new Run(parts[i])) : new Run(parts[i]));
        }
        AutomationProperties.SetName(t, DivisionModel.Plain(s));
        return t;
    }

    /// <summary>A card's quiet Close.</summary>
    public static FrameworkElement Close(Action close)
    {
        var b = K.Subtle(K.Text("Close", Ty.Body, B.Text2), close, new Thickness(8, 4, 8, 5), 6);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.ToolTip = "Close this card (Esc)";
        return b;
    }

    /// <summary>The key to the drawing's colours and marks (never colour alone).</summary>
    public static FrameworkElement Legend() => BioUI.Legend(new[]
    {
        (BioPalette.Maternal.ToColor(), "From the mother (maternal)"),
        (BioPalette.Paternal.ToColor(), "From the father (paternal)"),
        (DivColors.Of(DivInk.Centromere, Appearance.Dark), "Centromere (one dot per chromosome)"),
        (DivColors.Of(DivInk.Microtubule, Appearance.Dark), "Spindle microtubules"),
    });

    /// <summary>A row of a list that goes somewhere when pressed (a stage, a phase): chosen, it is on the accent's wash.</summary>
    public static FrameworkElement ListItem(UIElement? lead, string text, bool on, Action press, string? tip = null)
    {
        var words = K.Wrapped(text, on ? Ty.BodyStrong : Ty.Body);
        var face = K.Columns("auto, *", lead == null ? null : ((FrameworkElement)lead).Margin(0, 0, 10, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), words.Col(1));
        face.Margin = new Thickness(8, 5, 8, 5);
        var inner = on ? ChemKit.Tint(face, B.Accent, 0.14, 8) : (FrameworkElement)face;
        var b = K.Subtle(inner, press, new Thickness(0), 8);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        if (tip != null) b.ToolTip = tip;
        AutomationProperties.SetName(b, on ? $"{text}, shown" : text);
        return b;
    }
}

/// <summary>
/// The counts panel (spec §4.6): always there, updated each step — chromosomes, DNA molecules, ploidy, DNA amount, homologs
/// and sisters, worded as §5.2's tables, a human cell's numbers under the model's, a dot where a value changed at this
/// step, and an info button on each that opens the rule behind it (§5.2's definitions).
/// </summary>
internal sealed class DivisionCountsCard : ContentControl
{
    private static readonly HashSet<string> Open = new();
    private DivisionCounts? counts, previous;

    public DivisionCountsCard() { Focusable = false; }

    public void Show(DivisionCounts c, DivisionCounts? before)
    {
        counts = c;
        previous = before;
        Draw();
    }

    private void Draw()
    {
        if (counts is not { } c) { Content = null; return; }
        var v = K.V(0);
        var head = K.Columns("*, auto", BioUI.Heading("Counts").Col(0), K.Text("model cell · human cell", Ty.Caption, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Bottom).Col(1));
        head.Margin = new Thickness(14, 12, 14, 6);
        v.Children.Add(head);
        if (c.Listing is { } l)
            v.Children.Add(ChemKit.Line(SymbolRegular.Warning16, $"Cells differ — values are {l}", ChemKit.Orange).Margin(14, 0, 14, 6));
        var defs = DivisionModel.Definitions;
        for (var i = 0; i < defs.Length; i++)
        {
            v.Children.Add(Row(defs[i].Id, defs[i].Label, defs[i].Rule, c, previous));
            if (i < defs.Length - 1) v.Children.Add(K.Divider(14));
        }
        Content = K.Card(v, new Thickness(0, 0, 0, 4));
        AutomationProperties.SetName(this, "Counts: " + string.Join("; ", defs.Select(d => $"{d.Label} {Value(d.Id, c)}")));
    }

    private static string Value(string id, DivisionCounts c) => id switch
    {
        "chromosomes" => c.Chromosomes,
        "chromatids" => c.Chromatids,
        "ploidy" => c.Ploidy,
        "dna" => c.Dna,
        "homologs" => c.Homologs,
        _ => c.Sisters,
    };

    private static string? Human(string id, DivisionCounts c) => id switch
    {
        "chromosomes" => c.ChromosomesHuman,
        "chromatids" => c.ChromatidsHuman,
        _ => null,
    };

    private FrameworkElement Row(string id, string label, string rule, DivisionCounts c, DivisionCounts? before)
    {
        var value = Value(id, c);
        var (main, note) = DivisionModel.Split(value);
        var changed = before != null && Value(id, before) != value;
        // (the value wraps; the dot that marks a change rides at the end of its last line)
        var big = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, FontFamily = K.TextFace, TextWrapping = TextWrapping.Wrap };
        big.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
        big.FontFeatures();
        big.Inlines.Add(new Run(main));
        if (changed)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Margin = new Thickness(6, 0, 0, 2), ToolTip = "Changed at this step" };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, B.Accent);
            big.Inlines.Add(new InlineUIContainer(dot) { BaselineAlignment = BaselineAlignment.Center });
        }
        var words = K.V(3, K.Text(label, Ty.Caption, B.Text2), big);
        if (note != null) words.Children.Add(K.Wrapped(note, Ty.Caption, B.Text2));
        if (Human(id, c) is { } h)
        {
            var ht = K.Wrapped($"Human: {h}", Ty.Caption, B.Text2);
            ht.FontFeatures();
            words.Children.Add(ht);
        }
        var open = Open.Contains(id);
        var info = K.IconButton(open ? SymbolRegular.ChevronUp16 : SymbolRegular.Info16, () =>
        {
            if (!Open.Remove(id)) Open.Add(id);
            Draw();
        }, open ? $"Hide the rule for {label.ToLowerInvariant()}" : $"The rule for {label.ToLowerInvariant()}", 14, 26);
        info.VerticalAlignment = VerticalAlignment.Top;
        var row = K.Columns("*, auto", words.Col(0), info.Col(1));
        var v = K.V(8, row);
        if (open)
        {
            var box = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Child = K.Wrapped(rule, Ty.Caption) };
            box.Bg(B.Subtle);
            v.Children.Add(box);
        }
        v.Margin = new Thickness(14, 9, 10, 9);
        if (changed && Motion.On) Motion.FadeIn(big, Motion.Medium);
        AutomationProperties.SetName(row, $"{label}: {value}" + (Human(id, c) is { } hv ? $"; in a human cell {hv}" : "") + (changed ? "; changed at this step" : ""));
        return v;
    }
}

/// <summary>A cell drawn small: each chromatid a bar as long as its chromosome, in its parent's colour, a recombinant part
/// in the other's, with its centromere (the cells a process ends with).</summary>
internal sealed class DivMiniCell : FrameworkElement
{
    private readonly IReadOnlyList<DivChromatidKey> chromatids;
    private readonly double chiasma;

    public DivMiniCell(IReadOnlyList<DivChromatidKey> chromatids, double chiasma)
    {
        this.chromatids = chromatids;
        this.chiasma = chiasma;
        Height = 70;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var dark = Appearance.Dark;
        var rect = new Rect(2, 2, Math.Max(0, ActualWidth - 4), Math.Max(0, ActualHeight - 4));
        dc.DrawRoundedRectangle(DivColors.Brush(DivColors.Of(DivInk.Cytoplasm, dark), 1.6), DivColors.Pen(DivColors.Of(DivInk.Membrane, dark), 1, 1), rect, 10, 10);
        var n = Math.Max(chromatids.Count, 1);
        var gap = rect.Width / (n + 1);
        var unit = (rect.Height - 16) / 7;
        for (var j = 0; j < chromatids.Count; j++)
        {
            var c = chromatids[j];
            var pair = DivGeometry.Pairs[c.Id.Pair];
            var len = unit * pair.Units;
            var x = rect.X + gap * (j + 1);
            var top = rect.Y + rect.Height / 2 - len / 2;
            var cy = top + len * pair.Centromere;
            dc.DrawLine(DivColors.Pen(DivColors.Of(DivPainter.Parent(c.Id.Homolog), dark), 1, 5), new Point(x, top), new Point(x, top + len));
            if (c.Recombined > 0.5)
            {
                // (the long arm, below the centromere: beyond the chiasma, the other parent's)
                var from = cy + (top + len - cy) * chiasma;
                dc.DrawLine(DivColors.Pen(DivColors.Of(DivPainter.Parent(1 - c.Id.Homolog), dark), 1, 5), new Point(x, from), new Point(x, top + len));
            }
            dc.DrawEllipse(DivColors.Brush(DivColors.Of(DivInk.Centromere, dark)), null, new Point(x, cy), 2.5, 2.5);
        }
    }
}

/// <summary>Mitosis or meiosis: the options and the stages at the left; the cell, its labels and the step-through in the
/// middle; the counts, the picked chromosome's or part's card, the step's card and at the end the cells it made at the
/// right (the chemistry tools' three columns, stacked when the window is narrow).</summary>
internal sealed class DivisionProcessPage : Grid
{
    private readonly DivisionProcess process;
    private readonly BioStepController steps;
    private readonly Stack right = K.V(14);
    private readonly Stack stages = K.V(2);
    private readonly DivisionCountsCard counts = new();
    private readonly TextBlock title = new() { FontSize = 22, FontWeight = FontWeights.Bold, FontFamily = K.DisplayFace, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock subtitle = K.Text("", Ty.Body, B.Text2);
    private readonly ContentControl labelsButton = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly SelectorBar cellBar, pairsBar;
    private readonly SelectorBar? ndBar;
    private readonly ToggleSwitch labels = new(), crossing = new();
    private readonly System.Windows.Controls.Slider chiasma = new() { Minimum = DivisionOptions.ChiasmaMin, Maximum = DivisionOptions.ChiasmaMax, SmallChange = 0.05, LargeChange = 0.1, TickFrequency = 0.05, IsSnapToTickEnabled = true };
    private readonly TextBlock chiasmaText = K.Text("", Ty.Caption, B.Text2);
    private readonly TextBlock pairsText = K.Wrapped("", Ty.Caption, B.Text2);
    private readonly TextBlock arrangement = K.Wrapped("", Ty.Caption);
    private readonly TextBlock assortments = K.Wrapped("", Ty.Caption, B.Text2);
    private readonly TextBlock ndText = K.Wrapped("", Ty.Caption);
    private readonly Wpf.Ui.Controls.Button flipShort, flipMiddle;
    private bool syncing;

    private DivisionSequence Seq => CellDivisionModel.Of(process);

    public DivisionProcessPage(DivisionProcess process)
    {
        this.process = process;
        steps = CellDivisionModel.StepsOf(process);
        var meiosis = process == DivisionProcess.Meiosis;
        var stage = new DivisionStageView(() => Seq, steps);

        var o = CellDivisionModel.Options;
        cellBar = new SelectorBar(new[] { "Animal", "Plant" }, o.Plant ? 1 : 0) { ToolTip = "Animal cells have centrioles and pinch in two; plant cells have none, and build a cell plate" };
        cellBar.Changed += i => Change(x => x with { Plant = i == 1 });
        pairsBar = new SelectorBar(new[] { "2n = 4", "2n = 6" }, o.Pairs == 3 ? 1 : 0) { ToolTip = "The model organism's chromosome number: two pairs, or three" };
        pairsBar.Changed += i => Change(x => x with { Pairs = i == 1 ? 3 : 2 });
        labels.Checked += (_, _) => { if (!syncing) CellDivisionTool.SetLabels(true); };
        labels.Unchecked += (_, _) => { if (!syncing) CellDivisionTool.SetLabels(false); };
        crossing.Checked += (_, _) => Change(x => x with { CrossingOver = true });
        crossing.Unchecked += (_, _) => Change(x => x with { CrossingOver = false });
        chiasma.ValueChanged += (_, e) =>
        {
            chiasmaText.Text = ChiasmaWords(e.NewValue);
            Change(x => x with { Chiasma = Math.Round(e.NewValue * 20, MidpointRounding.AwayFromZero) / 20 });
        };
        AutomationProperties.SetName(chiasma, "Chiasma position on the long pair");
        chiasma.ToolTip = "Where along the long chromosome's long arm the crossing over happens (20–80%). ← → move it.";
        flipShort = FlipButton(1);
        flipMiddle = FlipButton(2);
        if (meiosis)
        {
            ndBar = new SelectorBar(new[] { "None", "In meiosis I", "In meiosis II" }, (int)o.Nondisjunction) { ToolTip = "Let the short pair fail to separate, in meiosis I or meiosis II" };
            ndBar.Changed += i => Change(x => x with { Nondisjunction = (DivisionNondisjunction)i });
        }

        // (the left: the cell, the chromosomes, labels, the stages, meiosis's options, the key)
        var left = K.V(14,
            BioUI.Heading("Cell"), cellBar,
            BioUI.Heading("Chromosomes"), pairsBar, pairsText,
            DivUI.Toggle("Show labels", labels, "Name the parts, with leader lines"),
            BioUI.Heading("Stages"), stages);
        if (meiosis)
        {
            left.Children.Add(BioUI.Heading("Crossing over"));
            left.Children.Add(DivUI.Toggle("Crossing over in prophase I", crossing, "Non-sister chromatids swap the part beyond a chiasma"));
            var chiasmaHead = K.Columns("*, auto", K.Text("Chiasma on the long pair", Ty.Caption).Col(0), chiasmaText.Col(1));
            left.Children.Add(K.V(4, chiasmaHead, chiasma));
            left.Children.Add(BioUI.Heading("Independent assortment"));
            left.Children.Add(BioUI.Caption("Which homolog of each pair faces which pole in metaphase I is chance. Flip a pair to see the other gametes."));
            left.Children.Add(flipShort);
            left.Children.Add(flipMiddle);
            left.Children.Add(arrangement);
            left.Children.Add(assortments);
            left.Children.Add(BioUI.Heading("Nondisjunction"));
            left.Children.Add(ndBar!);
            left.Children.Add(ndText);
        }
        left.Children.Add(BioUI.Heading("Key"));
        left.Children.Add(DivUI.Legend());

        // (the middle: the step's title and the cell; the step-through across the foot of the page, under all three
        //  columns — its capsule of buttons, dots and caption is wider than the middle column on a 1366-wide window)
        var words = K.V(2, title, subtitle);
        var head = K.Columns("*, auto", words.Col(0), labelsButton.Col(1));
        head.Margin = new Thickness(16, 14, 16, 4);
        var groups = DivisionModel.Groups(process).Count;
        var hint = K.Wrapped($"Click a chromosome or a label to learn about it · ← → step · 1–{groups} jump to a stage", Ty.Caption, B.Text2).Center();
        hint.Margin = new Thickness(12, 0, 12, 12);
        var card = K.Card(K.Rows("auto, *, auto", head.Row(0), stage.Row(1), hint.Row(2)), new Thickness(0), 12);
        card.MinHeight = 380;
        var controls = new BioStepControls(steps, DivisionModel.Groups(process)).Margin(40, 0, 40, 16);

        Children.Add(K.Rows("*, auto", BioUI.Work(left, card, right).Row(0), controls.Row(1)));
        Sync();
        Right();

        void StepChanged() { Stages(); Right(); }
        void ModelChanged() { Sync(); Right(); }
        Loaded += (_, _) =>
        {
            steps.Changed -= StepChanged;
            steps.Changed += StepChanged;
            CellDivisionModel.Changed -= ModelChanged;
            CellDivisionModel.Changed += ModelChanged;
            Sync();
            Right();
            // (the step-through's keys work at once: ← → on the drawing)
            Dispatcher.BeginInvoke(() => { if (IsVisible && !IsKeyboardFocusWithin) stage.Drawing.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);
        };
        Unloaded += (_, _) =>
        {
            steps.Changed -= StepChanged;
            CellDivisionModel.Changed -= ModelChanged;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape || CellDivisionModel.Selected == null) return;
            CellDivisionModel.Selected = null;
            CellDivisionModel.Tell();
            e.Handled = true;
        };
    }

    private Wpf.Ui.Controls.Button FlipButton(int pair)
    {
        var b = K.Button($"Flip the {DivGeometry.Pairs[pair].Name} pair", () => Change(x =>
        {
            var f = x.Flipped.ToArray();
            f[pair] = !f[pair];
            return x with { Flipped = f };
        }), icon: SymbolRegular.ArrowSort20);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.MinHeight = 34;
        b.ToolTip = $"Turn the {DivGeometry.Pairs[pair].Name} pair round at metaphase I";
        return b;
    }

    private static string ChiasmaWords(double v) => $"{v * 100:0}% along its long arm";

    private void Change(Func<DivisionOptions, DivisionOptions> change)
    {
        if (syncing) return;
        CellDivisionTool.Update(change(CellDivisionModel.Options));
    }

    /// <summary>The controls set as the model is (without raising their changes).</summary>
    private void Sync()
    {
        syncing = true;
        var o = CellDivisionModel.Options;
        cellBar.Select(o.Plant ? 1 : 0, false);
        pairsBar.Select(o.Pairs == 3 ? 1 : 0, false);
        ndBar?.Select((int)o.Nondisjunction, false);
        pairsText.Text = $"A long pair and a short pair{(o.Pairs == 3 ? ", and a middle pair" : "")}: one of each from the mother (red), one from the father (blue). Human cells have 2n = 46.";
        if (labels.IsChecked != CellDivisionModel.ShowLabels) labels.IsChecked = CellDivisionModel.ShowLabels;
        if (crossing.IsChecked != o.CrossingOver) crossing.IsChecked = o.CrossingOver;
        if (Math.Abs(chiasma.Value - o.Chiasma) > 1e-9) chiasma.Value = o.Chiasma;
        chiasma.IsEnabled = o.CrossingOver;
        chiasmaText.Text = ChiasmaWords(o.Chiasma);
        chiasma.Opacity = chiasmaText.Opacity = o.CrossingOver ? 1 : 0.5;
        flipMiddle.Visibility = o.Pairs == 3 ? Visibility.Visible : Visibility.Collapsed;
        flipShort.Appearance = o.Flipped[1] ? ControlAppearance.Primary : ControlAppearance.Secondary;
        flipMiddle.Appearance = o.Flipped[2] ? ControlAppearance.Primary : ControlAppearance.Secondary;
        arrangement.Text = "Facing the top pole: " + string.Join(", ", Enumerable.Range(0, o.Pairs).Select(p => $"{DivGeometry.Pairs[p].Name} {(o.Flipped[p] ? "father's" : "mother's")}")) + ".";
        assortments.Text = DivisionModel.Assortments(o.Pairs);
        assortments.FontWeight = FontWeights.SemiBold;
        ndText.Text = o.Nondisjunction switch
        {
            DivisionNondisjunction.MeiosisI => "The short pair's homologs both go to one pole: gametes n+1, n+1, n−1, n−1.",
            DivisionNondisjunction.MeiosisII => "In one cell the short chromosome's sisters stay together: gametes n+1, n−1, n, n.",
            _ => "",
        };
        ndText.Foreground = K.Brush(K.Darkened(ChemKit.Orange));
        ndText.Visibility = ndText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var on = CellDivisionModel.ShowLabels;
        var lb = K.Button("Labels", () => CellDivisionTool.SetLabels(!CellDivisionModel.ShowLabels), accent: on, icon: SymbolRegular.Tag20);
        lb.MinWidth = 0;
        lb.ToolTip = on ? "Hide the labels" : "Show the labels";
        labelsButton.Content = lb;
        syncing = false;
        Stages();
    }

    /// <summary>The stages as a list: a press goes to a stage group's first step (as its number key does).</summary>
    private void Stages()
    {
        stages.Children.Clear();
        var current = steps.Current?.Group;
        foreach (var (g, name) in DivisionModel.Groups(process).OrderBy(x => x.Key))
        {
            var number = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), Child = K.Text($"{g}", Ty.Caption, B.Text2).Center() };
            number.Bg(B.Subtle);
            var group = g;
            stages.Children.Add(DivUI.ListItem(number, name, current == g, () => steps.JumpToGroup(group), $"{name} (key {g})"));
        }
    }

    /// <summary>The right: the counts, the picked chromosome's or part's card, the step's card, and at the end the cells it
    /// made.</summary>
    private void Right()
    {
        var seq = Seq;
        var i = Math.Clamp(steps.Index, 0, seq.Stages.Count - 1);
        var st = seq.Stages[i];
        title.Text = st.Title;
        title.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
        subtitle.Text = $"{process.Title()} · {(seq.Options.Plant ? "plant" : "animal")} cell · model 2n = {2 * seq.Options.Pairs} (human 2n = 46)";
        right.Children.Clear();
        counts.Show(seq.Counts[i], i > 0 ? seq.Counts[i - 1] : null);
        right.Children.Add(counts);
        void Close() { CellDivisionModel.Selected = null; CellDivisionModel.Tell(); }
        if (CellDivisionModel.Selected is { } sel)
        {
            var frame = seq.Keyframes[i].Frame;
            if (sel.StartsWith("chromatid:", StringComparison.Ordinal) && int.TryParse(sel[10..], out var k) && k >= 0 && k < frame.Chromatids.Length)
                right.Children.Add(DivParts.ChromosomeCard(frame.Chromatids[k], frame, Close));
            else if (DivParts.Of(sel) is { } info)
                right.Children.Add(DivParts.PartCard(info, Close));
        }
        right.Children.Add(StepCard(st));
        if (i == seq.Stages.Count - 1) right.Children.Add(Products(seq));
    }

    /// <summary>The step's card: what happens (the specification's bold key terms kept) and what to notice.</summary>
    private static FrameworkElement StepCard(DivisionStage st)
    {
        var notice = K.V(6, K.Text("What to notice", Ty.Caption, B.Text2).Strong());
        foreach (var n in st.Notice)
            notice.Children.Add(K.Columns("auto, *", K.Icon(SymbolRegular.Eye16, 12, B.AccentText).Margin(0, 3, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), K.Wrapped(n, Ty.Body).Col(1)));
        return BioUI.Card(st.Title, null, null, DivUI.Rich(st.Rich), st.Notice.Count > 0 ? notice : null);
    }

    /// <summary>The cells a process ends with, each drawn small with its chromosomes (spec §4.6: gametes labelled n, n+1, n−1).</summary>
    private static FrameworkElement Products(DivisionSequence seq)
    {
        var products = seq.Products;
        var meiosis = seq.Process == DivisionProcess.Meiosis;
        var o = seq.Options;
        string subtitle;
        if (!meiosis) subtitle = "Identical to each other and to the parent cell: 2n, 2c.";
        else if (o.Nondisjunction != DivisionNondisjunction.None) subtitle = "Nondisjunction: some have one chromosome too many (n+1) or too few (n−1).";
        else
        {
            var kinds = products.Select(p => string.Join("", p.Chromatids.Select(c => $"{c.Id.Pair}{c.Id.Homolog}{c.Recombined > 0.5}"))).Distinct().Count();
            subtitle = $"Haploid (n), 1c each; {kinds} different kinds{(o.CrossingOver ? " (crossing over and independent assortment)" : " (independent assortment only)")}.";
        }
        var grid = new Grid();
        for (var j = 0; j < products.Count; j++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var p = products[j];
            var ploidy = K.Text(p.Tally.Ploidy, Ty.BodyStrong).Center();
            ploidy.Foreground = K.Brush(K.Darkened(p.Tally.Ploidy.Length > 2 ? ChemKit.Orange : BioColors.Tool("divide")));
            var facts = K.Text($"{p.Tally.Chromosomes} chr · {DivisionModel.CText(p.Tally.C)}", Ty.Caption, B.Text2).Center();
            facts.FontFeatures();
            var cell = K.V(4, new DivMiniCell(p.Chromatids, o.Chiasma), ploidy, facts);
            cell.Margin = new Thickness(j == 0 ? 0 : 4, 0, j == products.Count - 1 ? 0 : 4, 0);
            AutomationProperties.SetName(cell, $"A cell: {p.Tally.Ploidy}, {p.Tally.Chromosomes} chromosomes");
            grid.Children.Add(cell.Col(j));
        }
        var rows = new List<UIElement?> { grid };
        if (meiosis && o.Nondisjunction != DivisionNondisjunction.None && DivParts.Of("nondisjunction")?.MixUp is { } m)
            rows.Add(BioUI.FactRow("Trisomy 21", m, ChemKit.Orange));
        return BioUI.Card(meiosis ? "The four cells (gametes)" : "The two daughter cells", null, subtitle, rows.ToArray());
    }
}

/// <summary>Mitosis and meiosis side by side, stepping together (prophase ↔ prophase I … telophase ↔ telophase II), over
/// §5.2.3's table with the rows about the step lit; the table beside them on a wide window.</summary>
internal sealed class DivisionComparePage : Grid
{
    private readonly BioStepController steps = CellDivisionModel.CompareSteps;
    private readonly ContentControl mitosisCounts = new() { Focusable = false }, meiosisCounts = new() { Focusable = false };
    private readonly TextBlock mitosisStep = K.Text("", Ty.Body, B.Text2), meiosisStep = K.Text("", Ty.Body, B.Text2);
    private readonly ContentControl mitosisDone = new() { Focusable = false };
    private readonly ContentControl table = new() { Focusable = false };
    private readonly FrameworkElement panels, controls;
    private bool? wide;

    public DivisionComparePage()
    {
        var mit = new DivisionStageView(() => CellDivisionModel.Mitosis, steps, i => DivisionModel.CompareSteps[Math.Clamp(i, 0, DivisionModel.CompareSteps.Length - 1)].Mitosis, labels: false);
        var mei = new DivisionStageView(() => CellDivisionModel.Meiosis, steps, i => DivisionModel.CompareSteps[Math.Clamp(i, 0, DivisionModel.CompareSteps.Length - 1)].Meiosis, labels: false);
        FrameworkElement Panel(string name, DivisionStageView view, TextBlock stepText, ContentControl countsHost, UIElement? trailing)
        {
            var head = K.Columns("auto, *, auto", K.Text(name, Ty.Subtitle).Margin(0, 0, 10, 0).Col(0), stepText.Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1), trailing?.Col(2));
            head.Margin = new Thickness(16, 14, 16, 0);
            countsHost.Margin = new Thickness(12, 0, 12, 12);
            view.MinHeight = 260;
            return K.Card(K.Rows("auto, *, auto", head.Row(0), view.Row(1), countsHost.Row(2)), new Thickness(0), 12);
        }
        panels = K.Columns("*, 14, *", Panel("Mitosis", mit, mitosisStep, mitosisCounts, mitosisDone).Col(0), Panel("Meiosis", mei, meiosisStep, meiosisCounts, null).Col(2));
        controls = new BioStepControls(steps, DivisionModel.CompareGroups);
        Margin = new Thickness(40, 8, 40, 24);
        MaxWidth = 1560;
        SizeChanged += (_, e) => Lay(e.NewSize);
        Draw();
        void Changed() => Draw();
        Loaded += (_, _) =>
        {
            steps.Changed -= Changed;
            steps.Changed += Changed;
            CellDivisionModel.Changed -= Changed;
            CellDivisionModel.Changed += Changed;
            Draw();
            Lay(RenderSize);
        };
        Unloaded += (_, _) =>
        {
            steps.Changed -= Changed;
            CellDivisionModel.Changed -= Changed;
        };
    }

    /// <summary>Wide (1250 and more): the two cells and the step-through at the left, the table beside them; else all one
    /// under another, scrolling.</summary>
    private void Lay(Size size)
    {
        if (size.Width <= 0) return;
        var w = size.Width >= 1250;
        if (wide == w) return;
        wide = w;
        foreach (var e in new[] { panels, controls, table }) ChemKit.Detach(e);
        Children.Clear();
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        var note = BioUI.Caption("Both copy their DNA once, in S phase. Mitosis divides once and keeps the chromosome sets; meiosis divides twice and halves them.");
        if (w)
        {
            panels.Height = double.NaN;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
            var leftSide = K.Rows("*, auto", panels.Row(0), controls.Margin(0, 14, 0, 0).Row(1));
            Children.Add(leftSide.Col(0));
            var side = new ScrollViewer { Content = K.V(12, table, note), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            Children.Add(side.Col(2));
        }
        else
        {
            panels.Height = Math.Max(420, Math.Min(560, size.Height * 0.62));
            controls.Margin = new Thickness(0);
            var all = K.V(14, panels, controls, table, note);
            all.Margin = new Thickness(0, 0, 0, 24);
            Children.Add(new ScrollViewer { Content = all, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false });
        }
    }

    private void Draw()
    {
        var cs = DivisionModel.CompareSteps[Math.Clamp(steps.Index, 0, DivisionModel.CompareSteps.Length - 1)];
        var mit = CellDivisionModel.Mitosis;
        var mei = CellDivisionModel.Meiosis;
        mitosisStep.Text = mit.Stages[cs.Mitosis].Title;
        meiosisStep.Text = mei.Stages[cs.Meiosis].Title;
        mitosisDone.Content = steps.Index >= 4
            ? K.H(4, K.Icon(SymbolRegular.CheckmarkCircle16, 14, B.Text2), K.Text("Finished: 2 cells", Ty.Caption, B.Text2))
            : null;
        mitosisCounts.Content = Tiles(mit.Counts[cs.Mitosis]);
        meiosisCounts.Content = Tiles(mei.Counts[cs.Meiosis]);
        table.Content = Table(cs.Rows);
    }

    /// <summary>The counts under each drawing: chromosomes, chromatids, ploidy, DNA (each tile's rule in its tooltip).</summary>
    private static FrameworkElement Tiles(DivisionCounts c)
    {
        var g = K.Columns("*, 8, *, 8, *, 8, *");
        var items = new[] { ("Chromosomes", c.Chromosomes, "chromosomes"), ("Chromatids", c.Chromatids, "chromatids"), ("Ploidy", c.Ploidy, "ploidy"), ("DNA", c.Dna, "dna") };
        for (var i = 0; i < items.Length; i++)
        {
            var (label, value, id) = items[i];
            var big = new TextBlock { Text = DivisionModel.Split(value).Main, FontSize = 15, FontWeight = FontWeights.SemiBold, FontFamily = K.TextFace, TextWrapping = TextWrapping.Wrap };
            big.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
            big.FontFeatures();
            var tile = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Child = K.V(2, K.Text(label, Ty.Caption, B.Text2), big) };
            tile.Bg(B.Subtle);
            var rule = DivisionModel.Definitions.First(d => d.Id == id).Rule;
            tile.ToolTip = $"{value}\n{rule}";
            AutomationProperties.SetName(tile, $"{label}: {value}");
            g.Children.Add(tile.Col(i * 2));
        }
        return g;
    }

    /// <summary>§5.2.3 as a table: the feature, mitosis, meiosis; the step's rows lit.</summary>
    private static FrameworkElement Table(int[] lit)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = new List<string[]> { new[] { "", "Mitosis", "Meiosis" } };
        rows.AddRange(DivisionModel.CompareTable);
        for (var r = 0; r < rows.Count; r++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var on = r > 0 && lit.Contains(r - 1);
            if (on)
            {
                var wash = new Border { Opacity = 0.1, IsHitTestVisible = false };
                wash.SetResourceReference(Border.BackgroundProperty, B.Accent);
                Grid.SetRow(wash, r);
                Grid.SetColumnSpan(wash, 3);
                g.Children.Add(wash);
            }
            for (var col = 0; col < 3; col++)
            {
                TextBlock t;
                if (r == 0) t = K.Wrapped(rows[r][col], Ty.BodyStrong);
                else if (col == 0) t = K.Wrapped(rows[r][col], Ty.Caption, on ? B.AccentText : B.Text2).Strong();
                else t = K.Wrapped(rows[r][col], Ty.Body);
                t.Margin = new Thickness(col == 0 ? 14 : 4, 9, col == 2 ? 14 : 4, 9);
                Grid.SetRow(t, r);
                Grid.SetColumn(t, col);
                g.Children.Add(t);
            }
            if (r < rows.Count - 1)
            {
                var line = K.Divider(r == 0 ? 0 : 14);
                line.VerticalAlignment = VerticalAlignment.Bottom;
                Grid.SetRow(line, r);
                Grid.SetColumnSpan(line, 3);
                g.Children.Add(line);
            }
        }
        AutomationProperties.SetName(g, "Mitosis compared with meiosis. " + string.Join(". ", DivisionModel.CompareTable.Select((x, i) => $"{x[0]}: mitosis {x[1]}; meiosis {x[2]}{(lit.Contains(i) ? " (this step)" : "")}")));
        return K.Card(g, new Thickness(0), 12);
    }
}

/// <summary>The cell cycle (spec §4.6): the phases and gates as a list at the left (the wheel's keyboard and Narrator twin)
/// with S phase up close; the wheel and the cyclins in the middle, with the "Cancer" switch; the chosen part's card at the
/// right.</summary>
internal sealed class CellCyclePage : Grid
{
    private readonly ToolContext c;
    private readonly Stack phases = K.V(2), gates = K.V(2);
    private readonly Stack right = K.V(14);
    private readonly ToggleSwitch cancer = new();
    private readonly CellCycleWheel wheel = new();
    private bool syncing;

    public CellCyclePage(ToolContext c)
    {
        this.c = c;
        wheel.Picked += id => Pick(id, toggle: false);
        cancer.Checked += (_, _) => { if (!syncing) { CellDivisionModel.Cancer = true; CellDivisionModel.Tell(); } };
        cancer.Unchecked += (_, _) => { if (!syncing) { CellDivisionModel.Cancer = false; CellDivisionModel.Tell(); } };
        cancer.ToolTip = "Show what goes wrong when the checkpoints fail";
        AutomationProperties.SetName(cancer, "Cancer");
        var replication = K.Button("What happens in S phase", OpenReplication, icon: SymbolRegular.BranchFork20);
        replication.HorizontalAlignment = HorizontalAlignment.Stretch;
        replication.HorizontalContentAlignment = HorizontalAlignment.Center;
        replication.MinHeight = 36;
        replication.ToolTip = "Open DNA & RNA's Replication page: a replication fork, step by step";
        var left = K.V(14, BioUI.Heading("Phases"), phases, BioUI.Heading("Checkpoints"), gates,
            BioUI.Heading("S phase up close"), replication,
            BioUI.Caption("Mitosis and meiosis, stage by stage, are on this tool's other pages."));

        var title = new TextBlock { Text = "The cell cycle", FontSize = 22, FontWeight = FontWeights.Bold, FontFamily = K.DisplayFace };
        title.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
        var words = K.V(2, title, K.Wrapped("A typical human cell, about 24 hours from one division to the next", Ty.Body, B.Text2));
        var cancerRow = K.H(8, K.Text("Cancer", Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center), cancer);
        cancerRow.VerticalAlignment = VerticalAlignment.Top;
        var head = K.Columns("*, auto", words.Col(0), cancerRow.Col(1));
        head.Margin = new Thickness(16, 14, 16, 4);
        var chartHead = K.Text("Cyclins rise and fall each cycle", Ty.BodyStrong).Margin(20, 2, 20, 0);
        var chart = new CyclinChart { Margin = new Thickness(20, 2, 20, 0) };
        var chartNote = K.Wrapped("Illustrative levels. Cyclins are made and destroyed each cycle; CDKs stay, working only when bound to a cyclin.", Ty.Caption, B.Text2).Margin(20, 2, 20, 14);
        wheel.MinHeight = 280;
        var card = K.Card(K.Rows("auto, *, auto, auto, auto", head.Row(0), wheel.Margin(12, 0, 12, 0).Row(1), chartHead.Row(2), chart.Row(3), chartNote.Row(4)), new Thickness(0), 12);
        card.MinHeight = 520;
        Children.Add(BioUI.Work(left, card, right));
        Draw();
        void Changed() => Draw();
        Loaded += (_, _) => { CellDivisionModel.Changed -= Changed; CellDivisionModel.Changed += Changed; Draw(); };
        Unloaded += (_, _) => CellDivisionModel.Changed -= Changed;
    }

    private static void Pick(string id, bool toggle)
    {
        CellDivisionModel.CycleSelected = toggle && CellDivisionModel.CycleSelected == id ? null : id;
        CellDivisionModel.Tell();
    }

    /// <summary>"What happens in S phase": DNA & RNA's Replication page.</summary>
    private void OpenReplication()
    {
        BioNav.Set("dna", "replication");
        BioTools.Open(c, "dna", new Dictionary<string, string> { ["page"] = "replication" });
    }

    private void Draw()
    {
        syncing = true;
        if (cancer.IsChecked != CellDivisionModel.Cancer) cancer.IsChecked = CellDivisionModel.Cancer;
        syncing = false;
        var selected = CellDivisionModel.CycleSelected;
        var dark = Appearance.Dark;
        phases.Children.Clear();
        foreach (var p in DivisionModel.CyclePhases.Append(DivisionModel.G0))
        {
            var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = K.Brush(CycleOrder.PhaseColor(p.Id, dark)) };
            var id = p.Id;
            phases.Children.Add(DivUI.ListItem(swatch, p.Hours > 0 ? $"{p.Title} · {p.Hours:0} h" : p.Title, selected == id, () => Pick(id, toggle: true)));
        }
        gates.Children.Clear();
        foreach (var cp in DivisionModel.Checkpoints)
        {
            var tone = CellDivisionModel.Cancer ? K.Tone("red") : K.Tone("green");
            var icon = K.Icon(CellDivisionModel.Cancer ? SymbolRegular.ShieldDismiss20 : SymbolRegular.ShieldCheckmark20, 14, tone, filled: true);
            var id = cp.Id;
            gates.Children.Add(DivUI.ListItem(icon, cp.Name, selected == id, () => Pick(id, toggle: true)));
        }
        wheel.InvalidateVisual();
        right.Children.Clear();
        right.Children.Add(Card(selected));
        right.Children.Add(BioUI.Card("Cyclins and CDKs", null, null, K.Wrapped(DivisionModel.CyclinRule, Ty.Body)));
        if (CellDivisionModel.Cancer)
            right.Children.Add(BioUI.Card("When checkpoints fail", K.Tone("red"), null,
                K.Wrapped("Cancer cells divide when they should not: mutations stick the accelerators on (oncogenes, from proto-oncogenes such as growth-factor receptors and Ras) and take the brakes off (tumour suppressors such as p53 and Rb). Damaged DNA is copied, unfinished DNA is divided, and chromosomes are shared out wrongly.", Ty.Body)));
    }

    /// <summary>The card for the gate or phase chosen, else how to read the wheel.</summary>
    private static FrameworkElement Card(string? id)
    {
        var dark = Appearance.Dark;
        var cancer = CellDivisionModel.Cancer;
        if (DivisionModel.Checkpoints.FirstOrDefault(x => x.Id == id) is { } cp)
        {
            var checks = K.V(4, K.Text("It checks", Ty.Caption, B.Text2).Strong());
            foreach (var q in cp.Checks)
                checks.Children.Add(K.Columns("auto, *", K.Icon(SymbolRegular.Checkmark12, 12, K.Tone("green")).Margin(0, 4, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), K.Wrapped(q, Ty.Body).Col(1)));
            return BioUI.Card(cp.Name, cancer ? K.Tone("red") : K.Tone("green"), null, checks,
                BioUI.FactRow("Why it matters", cp.Role),
                BioUI.FactRow("Cyclins and CDKs", cp.Cyclin),
                cancer ? BioUI.FactRow("When it fails", cp.Cancer, K.Tone("red")) : null);
        }
        if (DivisionModel.CyclePhases.Append(DivisionModel.G0).FirstOrDefault(x => x.Id == id) is { } p)
            return BioUI.Card(p.Title, CycleOrder.PhaseColor(p.Id, dark), p.Hours > 0 ? $"About {p.Hours:0} h of a 24 h cycle (Alberts)" : null,
                BioUI.FactRow("What happens", p.Text),
                p.Id == "s" ? BioUI.FactRow("Common mix-up", "DNA replicates here, in interphase, never in prophase; and it doubles the DNA, not the number of chromosomes.", ChemKit.Orange) : null);
        return BioUI.Card("Reading the wheel", null, null,
            BioUI.FactRow("In proportion", "Each phase's arc is its share of a typical 24 h human cell's cycle: interphase (G1, S, G2) takes 23 h, mitosis and cytokinesis about 1 h."),
            BioUI.FactRow("Checkpoints", "Press a gate (a bar across the ring) to see what it checks before letting the cell go on."));
    }
}
