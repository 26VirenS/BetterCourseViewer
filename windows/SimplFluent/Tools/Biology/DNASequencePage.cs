using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// DNA & RNA's Sequence page (spec §4.2 Sequence, §3.7): one strand typed or pasted 5′→3′ (cleaned as every biology tool
// cleans a sequence: whitespace, digits and a FASTA header dropped; U offered as a switch, never converted silently; any
// other letter refused with its positions), shown with its complementary strand aligned under it and written 3′→5′,
// its reverse complement (5′→3′), and its facts — length, GC content, the A/T/G/C counts with the Chargaff check, the
// molar mass worked from the nucleotides and a rough melting temperature. Show in 3D, Transcribe (to Gene Expression as
// the coding strand) and Random (20 bp, seeded by the time).

internal sealed class DnaSequencePage : Grid
{
    private readonly ToolContext c;
    private readonly TextBox box;
    private readonly ContentControl message = new() { Focusable = false };
    private readonly ContentControl strands = new() { Focusable = false };
    private readonly Stack right = K.V(14);
    private readonly TextBlock heading = K.Text("", Ty.Subtitle);
    private readonly DispatcherTimer typing = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private string? drawn;

    public DnaSequencePage(ToolContext c)
    {
        this.c = c;
        box = new TextBox
        {
            Text = DNAToolModel.Text, PlaceholderText = "Type or paste a strand, 5′→3′", ClearButtonEnabled = false, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, FontFamily = BioUI.Mono, FontSize = 14, MinHeight = 96, MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        System.Windows.Automation.AutomationProperties.SetName(box, "Strand 1, 5 prime to 3 prime");
        box.ToolTip = "Strand 1, 5′→3′ (Ctrl+F)";
        box.TextChanged += (_, _) => { typing.Stop(); typing.Start(); };
        typing.Tick += (_, _) => { typing.Stop(); DNAToolModel.SetText(box.Text); };

        var show = K.Button("Show in 3D", () => BioNav.Set("dna", "helix"), accent: true, icon: SymbolRegular.Cube20);
        var transcribe = K.Button("Transcribe", Transcribe, icon: SymbolRegular.Translate20);
        transcribe.ToolTip = "Open Gene Expression with this strand as the coding strand";
        var random = K.Button("Random", () => DNAToolModel.SetSequence(DNAToolModel.RandomStrand((ulong)DateTime.UtcNow.Ticks)), icon: SymbolRegular.ArrowShuffle20);
        random.ToolTip = "A random strand of 20 bases";
        var reset = K.Subtle(K.Text("Back to the example", Ty.Body, B.Text2), () => DNAToolModel.SetSequence(DNAToolModel.DefaultSequence), new Thickness(8, 5, 8, 6));
        foreach (var b in new[] { show, transcribe, random }) { b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Center; b.MinHeight = 36; }

        var left = K.V(14,
            BioUI.Heading("Strand 1, 5′→3′"),
            box,
            message,
            BioUI.Caption("A, C, G and T (N for a base not known). Spaces, numbers, a FASTA header and 5′- … -3′ are left out. Up to 3,000 letters; the double helix draws the first 40."),
            show, transcribe, random, reset);
        var centre = K.Card(K.Rows("auto, *", heading.Margin(16, 16, 16, 8).Row(0),
            new ScrollViewer { Content = strands, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(16, 0, 16, 16), Focusable = false }.Row(1)), new Thickness(0));
        Children.Add(BioUI.Work(left, centre, right));

        void Changed() => Refresh();
        void Theme() { drawn = null; Refresh(); }
        Loaded += (_, _) =>
        {
            DNAToolModel.Changed -= Changed;
            DNAToolModel.Changed += Changed;
            Appearance.Changed -= Theme;
            Appearance.Changed += Theme;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            DNAToolModel.Changed -= Changed;
            Appearance.Changed -= Theme;
            // (what was typed is kept, even when the page goes before the pause)
            if (typing.IsEnabled) { typing.Stop(); DNAToolModel.SetText(box.Text); }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { box.Focus(); box.SelectAll(); e.Handled = true; }
        };
        Refresh();
    }

    private void Transcribe()
    {
        var s = DNAToolModel.Sequence;
        BioTools.Open(c, "gene", new Dictionary<string, string> { ["sequence"] = s });
    }

    private void Refresh()
    {
        // (a strand set elsewhere — Random, the example, a hand-off — written into the field; what is being typed left alone)
        if (!typing.IsEnabled && box.Text != DNAToolModel.Text) box.Text = DNAToolModel.Text;
        var input = DNAToolModel.Input;
        message.Content = Message(input);
        var s = DNAToolModel.Sequence;
        var key = $"{s}|{Appearance.Dark}";
        if (key == drawn) return;
        drawn = key;
        heading.Text = $"{s.Length:#,##0} base pair{(s.Length == 1 ? "" : "s")}";
        // (the words wrap to the card's width; only the letters, in lines of 30, scroll sideways when the card is narrower —
        //  the wheel still scrolls the card, Shift and the wheel the letters)
        strands.Content = K.V(18,
            BioUI.Heading("Both strands"),
            BioUI.Caption("Strand 1 written 5′→3′, its complementary strand under it written 3′→5′: the two run in opposite directions."),
            CrossKit.Sideways(Duplex(s)),
            BioUI.Heading("Reverse complement, 5′→3′"),
            BioUI.Caption("Strand 2 read in its own 5′→3′ direction. A complement is not the same as a reverse complement."),
            CrossKit.Sideways(Strand(BioSeq.ReverseComplement(s))));
        Facts(s);
    }

    private FrameworkElement? Message(BioSequenceInput input)
    {
        if (input.Message is not { } text) return null;
        var note = ToolKit.Note(text, SymbolRegular.Warning24);
        if (input.Suggestion == BioSeqKind.Rna)
        {
            var fix = K.Button("Write it as DNA (U → T)", () => DNAToolModel.SetSequence(BioSeq.ToDna(input.Letters)), icon: SymbolRegular.ArrowSwap20);
            fix.MinWidth = 0;
            fix.HorizontalAlignment = HorizontalAlignment.Left;
            return K.V(8, note, BioUI.Caption("This page works on DNA. RNA has U where DNA has T."), fix);
        }
        if (input.Sequence == null && input.Letters.Length > 0) return K.V(6, note, BioUI.Caption("Until it is fixed, the pages show the last strand that was all A, C, G and T."));
        return note;
    }

    /// <summary>The strand and its complement, a line of 30 at a time: 5′ … 3′ over 3′ … 5′, a bar between each pair.</summary>
    private static FrameworkElement Duplex(string s, int perLine = 30)
    {
        var v = K.V(12);
        var comp = BioSeq.Complement(s);
        for (var start = 0; start < s.Length; start += perLine)
        {
            var top = s.Substring(start, Math.Min(perLine, s.Length - start));
            var bottom = comp.Substring(start, top.Length);
            var bars = new string(top.Select(ch => ch == 'N' ? ' ' : '|').ToArray());
            var block = K.V(0, Line(start + 1, top, "5′", "3′"), Line(null, bars, "  ", "  ", plain: true), Line(null, bottom, "3′", "5′"));
            System.Windows.Automation.AutomationProperties.SetName(block, $"Base pairs {start + 1} to {start + top.Length}: strand 1 {string.Join(" ", top.ToCharArray())}; strand 2 {string.Join(" ", bottom.ToCharArray())}");
            v.Children.Add(block);
        }
        return v;
    }

    /// <summary>One strand, 5′→3′, a line of 30 at a time, each line's ends marked (§3.7).</summary>
    private static FrameworkElement Strand(string s, int perLine = 30)
    {
        var v = K.V(4);
        for (var start = 0; start < s.Length; start += perLine)
            v.Children.Add(Line(start + 1, s.Substring(start, Math.Min(perLine, s.Length - start)), "5′", "3′"));
        System.Windows.Automation.AutomationProperties.SetName(v, $"5 prime to 3 prime: {string.Join(" ", s.ToCharArray())}");
        return v;
    }

    private static TextBlock Line(int? number, string letters, string leftEnd, string rightEnd, bool plain = false)
    {
        var row = new TextBlock { FontFamily = BioUI.Mono, FontSize = 14, TextWrapping = TextWrapping.NoWrap, LineHeight = 19, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        row.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
        var num = new Run((number is { } n ? $"{n}" : "").PadLeft(5) + " ") { FontSize = 11 };
        num.SetResourceReference(TextElement.ForegroundProperty, B.Text2);
        row.Inlines.Add(num);
        var l = new Run(leftEnd + " ") { FontSize = 11, FontWeight = FontWeights.SemiBold };
        l.SetResourceReference(TextElement.ForegroundProperty, B.Text2);
        row.Inlines.Add(l);
        for (var i = 0; i < letters.Length; i++)
        {
            if (i > 0 && i % 10 == 0) row.Inlines.Add(new Run(" "));
            var ch = letters[i];
            var r = new Run(ch.ToString());
            if (plain) { r.SetResourceReference(TextElement.ForegroundProperty, B.Text3); }
            else { r.FontWeight = FontWeights.SemiBold; r.Foreground = K.Brush(BioColors.Base(ch)); }
            row.Inlines.Add(r);
        }
        var e = new Run(" " + rightEnd) { FontSize = 11, FontWeight = FontWeights.SemiBold };
        e.SetResourceReference(TextElement.ForegroundProperty, B.Text2);
        row.Inlines.Add(e);
        return row;
    }

    private void Facts(string s)
    {
        right.Children.Clear();
        var k = BioSeq.Counts(s);
        var n = s.Length;
        string Share(int x) => n == 0 ? "0" : $"{x} ({x * 100.0 / n:0.#}%)";
        var rows = new List<(string, string, Color?)>
        {
            ("Length", $"{n:#,##0} nt · {n:#,##0} bp", null),
            ("GC content", $"{BioSeq.GcContent(s) * 100:0.#}%", null),
            ("A", Share(k.A), BioColors.Base('A')),
            ("T", Share(k.T), BioColors.Base('T')),
            ("G", Share(k.G), BioColors.Base('G')),
            ("C", Share(k.C), BioColors.Base('C')),
        };
        if (k.N > 0) rows.Add(("N (unknown)", Share(k.N), null));
        right.Children.Add(BioUI.Facts(rows));

        var (both, purines) = BioSeq.Chargaff(s);
        right.Children.Add(BioUI.Card("Chargaff’s rule", null, "In double-stranded DNA A = T and G = C, so %A + %G = 50%",
            BioUI.FactRow("This double strand", $"A {both.A} = T {both.T}, G {both.G} = C {both.C}: purines (A + G) are {purines:0.0}%.", K.Tone("green")),
            BioUI.FactRow("This strand alone", n == 0 ? "—" : $"A {k.A * 100.0 / n:0.#}%, T {k.T * 100.0 / n:0.#}%, G {k.G * 100.0 / n:0.#}%, C {k.C * 100.0 / n:0.#}%. One strand need not follow the rule; the two together always do.")));

        double m1 = BioSeq.Mass(s), m2 = BioSeq.Mass(BioSeq.Complement(s));
        right.Children.Add(BioUI.Card("Molar mass", null, "Worked from the nucleotides",
            BioUI.FactRow("Strand 1", $"{DnaWpf.N(m1)} g/mol"),
            BioUI.FactRow("Its complementary strand", $"{DnaWpf.N(m2)} g/mol"),
            BioUI.FactRow("Double-stranded", $"{DnaWpf.N(m1 + m2)} g/mol"),
            BioUI.FactRow("How", "The nucleotide monophosphates’ masses (dAMP 331.22, dCMP 307.20, dGMP 347.22, dTMP 322.21 g/mol, an N counted as their mean), less 18.02 for each phosphodiester bond: one water lost a link."),
            BioUI.FactRow("Quick lab estimate", $"About 650 g/mol a base pair (for the sodium salt): ≈ {DnaWpf.N(650.0 * n, "#,##0")} g/mol.")));

        var tm = BioSeq.MeltingTemperature(s);
        var acgt = k.A + k.C + k.G + k.T;
        right.Children.Add(BioUI.Card("Melting temperature", null, acgt == 0 ? "—" : $"≈ {tm:0.0} °C",
            BioUI.FactRow("How", acgt <= 14 ? "The Wallace rule, for 14 nt or fewer: Tm = 2·(A + T) + 4·(G + C) °C." : "For more than 14 nt: Tm = 64.9 + 41·(G + C − 16.4) / N °C."),
            BioUI.FactRow("Rough", "An estimate for a primer in 50 mM salt; the real value depends on the salt and the concentration.")));
    }
}
