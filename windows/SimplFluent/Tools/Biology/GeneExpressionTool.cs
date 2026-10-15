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

// Gene Expression (spec §4.3), the Mac's GeneExpressionTool.swift: a gene transcribed and translated step by step
// (GeneExpressionExpressPage.cs, its scene GeneExpressionScene.cs), the codon table as a grid and a wheel with its lookup
// both ways (GeneExpressionCodonsPage.cs), the six reading frames and their ORFs (GeneExpressionFramesPage.cs), and what a
// mutation does to the protein (GeneExpressionMutationsPage.cs). This file holds the tool itself — its pages, what a
// launch or another tool asks of it — and its pin's quick codon lookup; what it keeps is GeneExpressionModel (remembered
// in Prefs SimplBio.gene.*), and the arithmetic is Model/GeneExpression*.cs.

#pragma warning disable CA2255
internal static class GeneExpressionToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register()
    {
        BioTools.Register("gene", (c, state) => new GeneExpressionTool(c, state), GeneExpressionTool.Pages, (c, state) => new GeneExpressionCompact(c, state));
        // (what the tool keeps is remembered on this PC: Prefs SimplBio.gene.<field>)
        GeneExpressionModel.Recall = f => BioPrefs.String("gene", f);
        GeneExpressionModel.Remember = (f, v) => BioPrefs.Set("gene", f, v);
    }
}
#pragma warning restore CA2255

/// <summary>
/// Gene Expression: its page chosen in its header (Transcribe &amp; translate, Codon table, Reading frames, Mutations), with
/// what a launch asked of it (--tool gene --tool-state express:step=40, codons:wheel:find=Lys, frames:seq=…, mutations:sickle)
/// and what another tool handed it ("sequence", "codon", "mutation", "page") applied as it opens.
/// </summary>
internal sealed class GeneExpressionTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("express", "Transcribe & translate", SymbolRegular.PlayCircle24, "A gene made into a protein, step by step"),
        new("codons", "Codon table", SymbolRegular.Table24, "The genetic code"),
        new("frames", "Reading frames", SymbolRegular.TextAlignLeft24, "Six frames and the open reading frames"),
        new("mutations", "Mutations", SymbolRegular.ArrowSwap24, "What a change in the DNA does to the protein"),
    };

    private readonly ToolContext c;
    private readonly ContentControl host = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private string? shown;

    public GeneExpressionTool(ToolContext c, BioLaunchState? state)
    {
        this.c = c;
        if (state != null) GeneExpressionModel.ApplyLaunch(BioNav.Page("gene"), state.Value, state.Has);
        if (GeneExpressionModel.TakeHandOff(c.TakeInfo) is { } page) BioNav.Set("gene", page);
        Children.Add(host);
        Show();
        void Nav(string k) { if (k == "gene") Show(); }
        Loaded += (_, _) =>
        {
            BioNav.Changed -= Nav;
            BioNav.Changed += Nav;
            Show();
            // (the screenshot suite: the picture is taken once the page has been drawn and nothing moves)
            Shot.Began();
            Dispatcher.BeginInvoke(Shot.Ended, DispatcherPriority.ApplicationIdle);
        };
        Unloaded += (_, _) => BioNav.Changed -= Nav;
    }

    private void Show()
    {
        var page = BioNav.Page("gene");
        if (page == shown && host.Content != null) return;
        shown = page;
        FrameworkElement view = page switch
        {
            "codons" => new GXCodonsPage(c),
            "frames" => new GXFramesPage(),
            "mutations" => new GXMutationsPage(),
            _ => new GXExpressPage(c),
        };
        host.Content = view;
        Motion.FadeIn(view, Motion.Medium);
    }
}

/// <summary>
/// Gene Expression in its pin's flyout (spec §4.3, §3.2 item 3: the codon lookup), the Mac's GeneExpressionCompact: a codon
/// field and its answer — the amino acid's name, three- and one-letter codes and class, Start or Stop — and the code as a
/// compact 4 × 4 grid under it (a press fills the field). An amino acid typed lights its codons. "Open in the codon table"
/// takes the lookup to the whole tool. (--pin gene --pin-open gene --tool-state codon=UGG fills the field.)
/// </summary>
internal sealed class GeneExpressionCompact : ContentControl
{
    private readonly ToolContext c;
    private readonly Wpf.Ui.Controls.TextBox field;
    private readonly ContentControl answer = new() { Focusable = false };
    private readonly ContentControl gridHost = new() { Focusable = false };

    public GeneExpressionCompact(ToolContext c, BioLaunchState? state)
    {
        this.c = c;
        Focusable = false;
        field = new Wpf.Ui.Controls.TextBox
        {
            Text = state?.Value("codon") ?? state?.Value("find") ?? "", PlaceholderText = "AUG", ClearButtonEnabled = false, Width = 86, MinHeight = 34,
            FontFamily = BioUI.Mono, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top,
        };
        field.ToolTip = "A codon (AUG or ATG) or an amino acid (Lys, K, lysine)";
        System.Windows.Automation.AutomationProperties.SetName(field, "Codon or amino acid");
        field.TextChanged += (_, _) => Draw();
        field.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Open();
            e.Handled = true;
        };
        var top = K.Columns("auto, *", field.Col(0), answer.Margin(12, 2, 0, 0).Col(1));
        // (the way to the whole tool an accent button across the flyout, as every biology pin's is)
        var open = BioUI.OpenButton("Open in the codon table", Open, "The whole codon table, with this lookup (Enter)");
        var note = K.Text("AUG starts translation and codes Met.", Ty.Caption, B.Text2);
        Content = K.V(12, top, gridHost, note, open);
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => { field.Focus(); field.CaretIndex = field.Text.Length; }, DispatcherPriority.Input);
        // (0.3.5) Open full screen carries the lookup to the codon table, as Open in the codon table does
        c.Carry = () => field.Text.Trim().Length > 0 ? new Dictionary<string, string> { ["codon"] = field.Text, ["page"] = "codons" } : null;
        Draw();
    }

    private void Open() => c.OpenFull(new Dictionary<string, string> { ["codon"] = field.Text, ["page"] = "codons" }, null);

    private void Draw()
    {
        var q = GXCodonTable.Parse(field.Text);
        var up = field.Text.ToUpperInvariant();
        var dna = q.Kind == GXQueryKind.Codon && up.Contains('T') && !up.Contains('U');
        answer.Content = GXCodonAnswer.Make(q, dna);
        gridHost.Content = GXMiniGrid.Make(GXCodonTable.Matches(q), dna, codon => { field.Text = GXCodonTable.Letters(codon, dna); field.CaretIndex = field.Text.Length; field.Focus(); });
    }
}

/// <summary>The genetic code small (the pin's): 4 × 4 blocks, first base down the side, second across the top, third within
/// a block; each codon with its amino acid's three letters on its class's tint. A press picks the codon.</summary>
internal static class GXMiniGrid
{
    public static FrameworkElement Make(HashSet<string> lit, bool dna, Action<string> pick)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        for (var i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock Letter(int i)
        {
            var s = GXCodonTable.Letters(BioCode.Bases[i].ToString(), dna);
            return new TextBlock { Text = s, FontFamily = BioUI.Mono, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = K.Brush(BioColors.Base(s[0])), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        for (var second = 0; second < 4; second++) g.Children.Add(Letter(second).Margin(0, 0, 0, 3).Row(0).Col(1 + second));
        var accent = Appearance.Accent;
        var start = GXColors.Start.ToColor();
        for (var first = 0; first < 4; first++)
        {
            g.Children.Add(Letter(first).Row(1 + first).Col(0));
            for (var second = 0; second < 4; second++)
            {
                var block = K.V(1);
                block.Margin = new Thickness(2);
                for (var third = 0; third < 4; third++)
                {
                    var codon = GXCodonTable.Codon(first, second, third);
                    var aa = BioCode.AminoAcid(codon) ?? '?';
                    var on = lit.Contains(codon);
                    var tint = aa == '*' ? GXColors.Stop.ToColor() : BioColors.AminoAcid(aa);
                    var shown = GXCodonTable.Letters(codon, dna);
                    var letters = new TextBlock { Text = shown, FontFamily = BioUI.Mono, FontSize = 10.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                    letters.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
                    var three = new TextBlock { Text = GXKit.Three(aa), FontSize = 10.5, FontWeight = codon == "AUG" || aa == '*' ? FontWeights.Bold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
                    if (aa == '*') three.Foreground = K.Brush(K.Darkened(GXColors.Stop.ToColor()));
                    else three.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
                    var row = K.Columns("auto, *, auto", letters.Col(0), three.Col(2));
                    row.Margin = new Thickness(4, 0, 4, 0);
                    var face = new Border
                    {
                        Child = row, Height = 16, CornerRadius = new CornerRadius(3), Background = K.Brush(GXDraw.Alpha(tint, on ? 0.4 : 0.13)),
                        BorderThickness = new Thickness(on ? 1.5 : 1),
                        BorderBrush = on ? K.Brush(accent) : codon == "AUG" ? K.Brush(GXDraw.Alpha(start, 0.7)) : Brushes.Transparent,
                        Opacity = lit.Count == 0 || on ? 1 : 0.45,
                    };
                    var c0 = codon;
                    var b = K.Subtle(face, () => pick(c0), new Thickness(0), 3);
                    b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    b.MinHeight = 0;
                    b.ToolTip = $"{shown} → {GXKit.Name(aa)}";
                    System.Windows.Automation.AutomationProperties.SetName(b, $"{string.Join(" ", shown.Select(x => x.ToString()))}: {GXKit.Name(aa)}");
                    block.Children.Add(b);
                }
                g.Children.Add(block.Row(1 + first).Col(1 + second));
            }
        }
        System.Windows.Automation.AutomationProperties.SetName(g, "The genetic code");
        return g;
    }
}
