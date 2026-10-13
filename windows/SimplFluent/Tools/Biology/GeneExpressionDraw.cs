using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// What Gene Expression's drawings are made with (the Mac's Canvas drawing in GeneExpression*.swift): a drawn element that
// follows the theme (its text, secondary text, accent and ground as resources, and the biology colours drawn again when
// light or dark changes), words drawn at a point by their centre or either end, brushes and pens of a colour at an
// opacity (kept, so a frame drawn sixty times a second makes none), and the tool's own colours for the molecules it
// draws — proteins in quiet colours, so the bases' and amino acids' stay the loudest.

/// <summary>The tool's own colours (the Mac's GXColors), light and dark.</summary>
internal static class GXColors
{
    public static readonly BioHexPair Polymerase = new("#9b8ec4", "#b9aee3");
    public static readonly BioHexPair Ribosome = new("#c49a6c", "#d9b48f");
    public static readonly BioHexPair Trna = new("#6f8296", "#a3b4c6");
    public static readonly BioHexPair ReleaseFactor = new("#c27c9b", "#e09bbb");
    public static readonly BioHexPair Factor = new("#b08bd9", "#c9aaf0");
    public static readonly BioHexPair Start = new("#2e9e48", "#30d158");
    public static readonly BioHexPair Stop = new("#d63a34", "#ff6961");

    private static readonly Color Gray = Color.FromRgb(0x8e, 0x8e, 0x93);
    private static readonly Color Blue = Color.FromRgb(0x5e, 0xa8, 0xff);
    private static readonly Color Green = Color.FromRgb(0x30, 0xd1, 0x58);
    private static readonly Color Violet = Color.FromRgb(0xa5, 0x8b, 0xff);

    /// <summary>A band's tint under the strands (and in the mRNA strip).</summary>
    public static Color Region(GXRegionKind k) => k switch
    {
        GXRegionKind.Promoter => GXDraw.Alpha(Gray, 0.16),
        GXRegionKind.Utr5 or GXRegionKind.Utr3 => GXDraw.Alpha(Blue, 0.13),
        GXRegionKind.Exon or GXRegionKind.Cds => GXDraw.Alpha(Green, 0.13),
        GXRegionKind.Intron => GXDraw.Alpha(Violet, 0.18),
        _ => GXDraw.Alpha(Gray, 0.08),
    };

    /// <summary>A band's colour at full strength (the overview strip, the mRNA strip).</summary>
    public static Color RegionSolid(GXRegionKind k) => k switch
    {
        GXRegionKind.Promoter => GXDraw.Alpha(Gray, 0.55),
        GXRegionKind.Utr5 or GXRegionKind.Utr3 => GXDraw.Alpha(Blue, 0.7),
        GXRegionKind.Exon or GXRegionKind.Cds => GXDraw.Alpha(Green, 0.75),
        GXRegionKind.Intron => GXDraw.Alpha(Violet, 0.75),
        _ => GXDraw.Alpha(Gray, 0.4),
    };

    /// <summary>A mutation kind's colour (its badge and its card's swatch).</summary>
    public static Color Kind(GXMutationKind k) => k switch
    {
        GXMutationKind.None => Gray,
        GXMutationKind.Silent => BioPalette.Adenine.ToColor(),
        GXMutationKind.Missense => BioPalette.Guanine.ToColor(),
        GXMutationKind.Nonsense or GXMutationKind.StartLoss => BioPalette.Thymine.ToColor(),
        GXMutationKind.StopLoss => BioPalette.Uracil.ToColor(),
        GXMutationKind.Frameshift => BioPalette.Strand1.ToColor(),
        _ => BioPalette.Cytosine.ToColor(),
    };

    /// <summary>What changed, lit (a changed codon, a gap): Windows' orange.</summary>
    public static Color Changed => K.Tone("orange");
}

/// <summary>Brushes, pens and words for drawing, kept once made.</summary>
internal static class GXDraw
{
    public static readonly FontFamily Mono = BioUI.Mono;
    private static readonly Dictionary<Color, SolidColorBrush> Brushes = new();
    private static readonly Dictionary<(Color, double, bool), Pen> Pens = new();
    private static readonly Dictionary<(string, double, int, Color, bool), FormattedText> Words = new();
    private static readonly Dictionary<(bool Mono, int Weight), Typeface> Faces = new();

    /// <summary>A colour at an opacity (its own alpha kept: the theme's text colours are see-through).</summary>
    public static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Round(c.A * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);

    public static SolidColorBrush Brush(Color c, double a = 1)
    {
        var k = a >= 1 ? c : Alpha(c, a);
        if (Brushes.TryGetValue(k, out var b)) return b;
        if (Brushes.Count > 2048) Brushes.Clear();
        b = new SolidColorBrush(k);
        b.Freeze();
        Brushes[k] = b;
        return b;
    }

    public static Pen Pen(Color c, double width, double a = 1, bool dashed = false)
    {
        var col = a >= 1 ? c : Alpha(c, a);
        if (!dashed && Pens.TryGetValue((col, width, false), out var p)) return p;
        p = new Pen(Brush(col), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        p.Freeze();
        if (!dashed)
        {
            if (Pens.Count > 1024) Pens.Clear();
            Pens[(col, width, false)] = p;
        }
        return p;
    }

    /// <summary>A dashed pen (dash and gap in multiples of its width, as WPF counts them).</summary>
    public static Pen Dashed(Color c, double width, double dash, double gap, double a = 1)
    {
        var col = a >= 1 ? c : Alpha(c, a);
        var p = new Pen(Brush(col), width) { DashStyle = new DashStyle(new[] { dash / width, gap / width }, 0), DashCap = PenLineCap.Flat };
        p.Freeze();
        return p;
    }

    private static Typeface Face(bool mono, FontWeight weight)
    {
        var key = (mono, weight.ToOpenTypeWeight());
        if (Faces.TryGetValue(key, out var f)) return f;
        f = new Typeface(mono ? Mono : K.TextFace, FontStyles.Normal, weight, FontStretches.Normal);
        Faces[key] = f;
        return f;
    }

    /// <summary>Words to draw, made once for their size, weight and colour.</summary>
    public static FormattedText Text(string s, double size, FontWeight weight, Color colour, bool mono, double ppd)
    {
        var key = (s, size, weight.ToOpenTypeWeight(), colour, mono);
        if (Words.TryGetValue(key, out var t)) return t;
        if (Words.Count > 6000) Words.Clear();
        t = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face(mono, weight), Math.Max(1, size), Brush(colour), ppd);
        Words[key] = t;
        return t;
    }

    /// <summary>Where words go: by their centre, their left end or their right end (at the point's height).</summary>
    public enum Anchor { Centre, Leading, Trailing }

    public static void Draw(DrawingContext dc, FormattedText t, Point at, Anchor anchor = Anchor.Centre)
    {
        var x = anchor switch { Anchor.Leading => at.X, Anchor.Trailing => at.X - t.Width, _ => at.X - t.Width / 2 };
        dc.DrawText(t, new Point(x, at.Y - t.Height / 2));
    }

    public static StreamGeometry Lines(params Point[][] runs)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
            foreach (var run in runs)
            {
                if (run.Length < 2) continue;
                c.BeginFigure(run[0], false, false);
                c.PolyLineTo(run[1..], true, true);
            }
        g.Freeze();
        return g;
    }

    public static StreamGeometry Polygon(params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true, true);
            c.PolyLineTo(pts[1..], true, true);
        }
        g.Freeze();
        return g;
    }

    /// <summary>A rounded rectangle's corner, kept within half its shorter side.</summary>
    public static void Round(DrawingContext dc, Brush? fill, Pen? pen, Rect r, double radius)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        var k = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        dc.DrawRoundedRectangle(fill, pen, r, k, k);
    }
}

/// <summary>
/// A drawn element that follows the theme (the Lewis maker's canvas's way): the text, secondary text, accent and solid
/// ground as resources (light, dark and the accent change it at once), the biology colours drawn again when the look is
/// worn again (Appearance.Changed), and the whole of it taking the pointer.
/// </summary>
internal abstract class GXCanvas : FrameworkElement
{
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(GXCanvas),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty Text2BrushProperty = DependencyProperty.Register(nameof(Text2Brush), typeof(Brush), typeof(GXCanvas),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(GXCanvas),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GroundBrushProperty = DependencyProperty.Register(nameof(GroundBrush), typeof(Brush), typeof(GXCanvas),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush Text2Brush { get => (Brush)GetValue(Text2BrushProperty); set => SetValue(Text2BrushProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush GroundBrush { get => (Brush)GetValue(GroundBrushProperty); set => SetValue(GroundBrushProperty, value); }

    protected GXCanvas()
    {
        SetResourceReference(TextBrushProperty, B.Text);
        SetResourceReference(Text2BrushProperty, B.Text2);
        SetResourceReference(AccentBrushProperty, B.Accent);
        SetResourceReference(GroundBrushProperty, B.Solid);
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        void Look() => InvalidateVisual();
        Loaded += (_, _) => { Appearance.Changed -= Look; Appearance.Changed += Look; };
        Unloaded += (_, _) => Appearance.Changed -= Look;
    }

    protected static Color ColourOf(Brush b, Color fallback) => b is SolidColorBrush s ? Color.FromArgb((byte)(s.Color.A * Math.Clamp(s.Opacity, 0, 1)), s.Color.R, s.Color.G, s.Color.B) : fallback;

    /// <summary>The theme's text colour (Mac: .primary).</summary>
    public Color Primary => ColourOf(TextBrush, Colors.Black);
    /// <summary>The theme's secondary text colour (Mac: .secondary).</summary>
    public Color Secondary => ColourOf(Text2Brush, Colors.Gray);
    public Color Accent => ColourOf(AccentBrush, Colors.DodgerBlue);
    public Color Ground => ColourOf(GroundBrush, Colors.White);
    public double Ppd => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    /// <summary>Words drawn at a point (by their centre, or an end), at an opacity.</summary>
    public void Say(DrawingContext dc, string s, Point at, double size = 11, FontWeight? weight = null, Color? colour = null,
        GXDraw.Anchor anchor = GXDraw.Anchor.Centre, double alpha = 1, bool mono = false)
    {
        if (alpha <= 0.01 || s.Length == 0) return;
        var t = GXDraw.Text(s, size, weight ?? FontWeights.SemiBold, colour ?? Secondary, mono, Ppd);
        if (alpha < 0.99) dc.PushOpacity(alpha);
        GXDraw.Draw(dc, t, at, anchor);
        if (alpha < 0.99) dc.Pop();
    }

    /// <summary>How wide words are drawn.</summary>
    public double Measure(string s, double size, FontWeight weight, bool mono = false) => GXDraw.Text(s, size, weight, Colors.Black, mono, Ppd).Width;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(RenderSize));
        Draw(dc, RenderSize);
    }

    protected abstract void Draw(DrawingContext dc, Size size);
}

/// <summary>The small pieces Gene Expression's pages share.</summary>
internal static class GXKit
{
    /// <summary>A small capsule badge ("Start", "Stop", "Made up for teaching").</summary>
    public static Border Badge(string text, Color colour)
    {
        var t = K.Text(text, Ty.Caption);
        t.FontSize = 11;
        t.FontWeight = FontWeights.Bold;
        t.Foreground = new SolidColorBrush(K.Darkened(colour));
        return new Border
        {
            Child = t, Padding = new Thickness(7, 0, 7, 1), CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(GXDraw.Alpha(colour, 0.14)), BorderBrush = new SolidColorBrush(GXDraw.Alpha(colour, 0.4)),
            BorderThickness = new Thickness(0.75), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    /// <summary>Bases in their colours, monospaced (each letter always shown: never colour alone).</summary>
    public static TextBlock Bases(string s, double size = 13, FontWeight? weight = null)
    {
        var t = new TextBlock { FontFamily = BioUI.Mono, FontSize = size, FontWeight = weight ?? FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        foreach (var ch in s)
        {
            var r = new System.Windows.Documents.Run(ch.ToString());
            if ("ACGTUN".Contains(ch)) r.Foreground = new SolidColorBrush(BioColors.Base(ch));
            else r.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, B.Text2);
            t.Inlines.Add(r);
        }
        System.Windows.Automation.AutomationProperties.SetName(t, string.Join(" ", s.Select(c => c.ToString())));
        return t;
    }

    /// <summary>A protein's letters in their classes' colours (N→C), wrapping.</summary>
    public static TextBlock Protein(string protein, double size = 14, string? prefix = null)
    {
        var t = new TextBlock { FontFamily = BioUI.Mono, FontSize = size, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        if (prefix != null)
        {
            var p = new System.Windows.Documents.Run(prefix) { FontFamily = K.TextFace, FontSize = 12, FontWeight = FontWeights.Normal };
            p.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, B.Text2);
            t.Inlines.Add(p);
        }
        foreach (var ch in protein) t.Inlines.Add(new System.Windows.Documents.Run(ch.ToString()) { Foreground = new SolidColorBrush(BioColors.AminoAcid(ch)) });
        System.Windows.Automation.AutomationProperties.SetName(t, protein.Length == 0 ? "none" : string.Join(" ", protein.Select(c => c.ToString())));
        return t;
    }

    /// <summary>A wide button's caption with its symbol, as the chemistry builders' wide buttons have them.</summary>
    public static Wpf.Ui.Controls.Button Wide(string text, Action click, bool accent = false, Wpf.Ui.Controls.SymbolRegular? icon = null) => ChemKit.Wide(text, click, accent, icon);

    /// <summary>A labelled row whose value sits at the right (a card's facts).</summary>
    public static FrameworkElement Facts(IEnumerable<(string Name, string Value, Color? Tone)> rows) => BioUI.Facts(rows);

    /// <summary>The amino acid's three letters ("Lys"), "Stop" for a stop.</summary>
    public static string Three(char aa) => aa == '*' ? "Stop" : (BioAminoAcids.ByOne.TryGetValue(aa, out var a) ? a.Three : "?");

    /// <summary>The amino acid's name ("lysine"), "stop" for a stop.</summary>
    public static string Name(char aa) => aa == '*' ? "stop" : (BioAminoAcids.ByOne.TryGetValue(aa, out var a) ? a.Name : "?");

    /// <summary>A text field that keeps its own words while the page draws again (the caret stays where it is).</summary>
    public static Wpf.Ui.Controls.TextBox Field(string text, string placeholder, Action<string> changed, bool mono = true, bool multiline = false)
    {
        var b = new Wpf.Ui.Controls.TextBox { Text = text, PlaceholderText = placeholder, ClearButtonEnabled = false, MinHeight = 32 };
        if (mono) { b.FontFamily = BioUI.Mono; b.FontSize = 13; }
        if (multiline)
        {
            b.AcceptsReturn = true;
            b.TextWrapping = TextWrapping.Wrap;
            b.VerticalContentAlignment = VerticalAlignment.Top;
            b.Height = 104;
            b.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        b.TextChanged += (_, _) => changed(b.Text);
        return b;
    }

    /// <summary>A field's words set from the model only when they differ (so typing is never interrupted).</summary>
    public static void Keep(System.Windows.Controls.TextBox b, string text)
    {
        if (b.Text != text) b.Text = text;
    }

    /// <summary>A line that says something is wrong, with a button that fixes it if there is one.</summary>
    public static FrameworkElement Problem(string text, string? fix = null, Action? doFix = null)
    {
        var line = ChemKit.Line(Wpf.Ui.Controls.SymbolRegular.ErrorCircle16, text, ChemKit.Orange);
        if (fix == null || doFix == null) return line;
        var b = K.Button(fix, doFix);
        b.MinWidth = 0;
        b.Padding = new Thickness(10, 3, 10, 4);
        b.Margin = new Thickness(8, 0, 0, 0);
        b.VerticalAlignment = VerticalAlignment.Top;
        return K.Columns("*, auto", line.Col(0), b.Col(1));
    }

    /// <summary>A set of choices as Windows 11's SelectorBar, its choice set from the model without telling it back.</summary>
    public static SelectorBar Choice(IReadOnlyList<string> choices, int selected, Action<int> pick)
    {
        var bar = new SelectorBar(choices, selected) { HorizontalAlignment = HorizontalAlignment.Left };
        bar.Changed += pick;
        return bar;
    }

    /// <summary>A switch with its words at the left (the molecule builder's toggles).</summary>
    public static (FrameworkElement Row, Wpf.Ui.Controls.ToggleSwitch Switch) Toggle(string text, bool on, Action<bool> set, string? tip = null)
    {
        var s = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        s.Checked += (_, _) => set(true);
        s.Unchecked += (_, _) => set(false);
        System.Windows.Automation.AutomationProperties.SetName(s, text);
        var words = K.Wrapped(text, Ty.Body).Margin(0, 0, 12, 0);
        words.VerticalAlignment = VerticalAlignment.Center;
        var row = K.Columns("*, auto", words.Col(0), s.Col(1));
        if (tip != null) row.ToolTip = tip;
        return (row, s);
    }

    /// <summary>A card's body text that wraps (a sentence or two).</summary>
    public static TextBlock Line(string text, string brush = B.Text, Ty ty = Ty.Body) => K.Wrapped(text, ty, brush);
}
