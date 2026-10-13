using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// Genetics Crosses' pieces (spec §4.5, §9), the Mac's CrossesParts.swift: alleles written with their superscripts, the
// phenotypes' tints, the Punnett square, the forked-line diagram, a parent's gametes, the tool's menus, chips and
// segmented choices, the picture behind "Why?" — two chromosome pairs lining up either way at metaphase I (independent
// assortment) — and the critical-value table the chi-square pages share with Hardy–Weinberg's test.

/// <summary>Alleles as genetics writes them: "C^R" with its R raised; "X^a Y". The model keeps the caret; these draw it.</summary>
internal static class CrossInk
{
    /// <summary>The text with each "^x" raised and smaller, in the text colour (or <paramref name="brush"/>).</summary>
    public static TextBlock Text(string s, double size = 15, FontWeight? weight = null, string brush = B.Text)
    {
        var t = new TextBlock { FontFamily = K.TextFace, FontSize = size, FontWeight = weight ?? FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        t.Fg(brush);
        Fill(t.Inlines, s, size);
        AutomationProperties.SetName(t, CrossText.Spoken(s));
        return t;
    }

    /// <summary>The same in a colour of its own.</summary>
    public static TextBlock Text(string s, double size, FontWeight weight, Color colour)
    {
        var t = Text(s, size, weight);
        t.Foreground = K.Brush(colour);
        return t;
    }

    /// <summary>Wrapping text with superscripts (a question, a card's line).</summary>
    public static TextBlock Wrapped(string s, double size = 15, FontWeight? weight = null, string brush = B.Text)
    {
        var t = Text(s, size, weight, brush);
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        return t;
    }

    public static void Fill(InlineCollection inlines, string s, double size)
    {
        var run = new StringBuilder();
        void Flush()
        {
            if (run.Length == 0) return;
            inlines.Add(new Run(run.ToString()));
            run.Clear();
        }
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '^' && i + 1 < s.Length)
            {
                Flush();
                inlines.Add(new Run(s[i + 1].ToString()) { FontSize = Math.Max(7, size * 0.66), BaselineAlignment = BaselineAlignment.Superscript });
                i++;
            }
            else run.Append(s[i]);
        }
        Flush();
    }
}

/// <summary>The phenotypes' tints (never colour alone: every cell and row also says its phenotype): eight categorical hues
/// in a fixed order (the order of the phenotypic ratio), validated for colour-vision deficiency on light and dark (the
/// Mac's CrossColors); flower and coat colours wear their own.</summary>
internal static class CrossColors
{
    private static readonly BioHexPair[] Phenotypes =
    {
        new("#2a78d6", "#3987e5"), new("#eb6834", "#d95926"), new("#1baf7a", "#199e70"), new("#eda100", "#c98500"),
        new("#e87ba4", "#d55181"), new("#008300", "#008300"), new("#6250d6", "#9085e9"), new("#e34948", "#e66767"),
    };

    private static readonly Dictionary<string, BioHexPair> Named = new()
    {
        ["red"] = new("#d63a34", "#ff6961"), ["pink"] = new("#e87ba4", "#ff9fc4"), ["white"] = new("#b8b8c0", "#d8d8de"), ["roan"] = new("#a0603a", "#d0906a"),
    };

    /// <summary>A phenotype's tint by its place in the phenotypic ratio (and its name, for red, pink, white and roan); grey
    /// for none.</summary>
    public static Color Phenotype(int? index, string? label = null)
    {
        if (label != null && Named.TryGetValue(label, out var c)) return c.ToColor();
        if (index is not { } i) return BioPalette.Coil.ToColor();
        return Phenotypes[i % Phenotypes.Length].ToColor();
    }

    /// <summary>The tint of each phenotype of a result, by name.</summary>
    public static Dictionary<string, Color> Tints(BioCrossResult r)
    {
        var outs = new Dictionary<string, Color>();
        for (var i = 0; i < r.Phenotypes.Count; i++) outs[r.Phenotypes[i].Text] = Phenotype(i, r.Phenotypes[i].Text);
        return outs;
    }

    /// <summary>The tool's own colour (its tile's).</summary>
    public static Color Tool => BioColors.Tool("cross");

    /// <summary>Diagonal hatching for a cell that dies before birth.</summary>
    public static Brush Hatching()
    {
        var pen = new Pen(K.Brush(K.WithAlpha(BioPalette.Coil.ToColor(), 0.6)), 1);
        var d = new GeometryDrawing(null, pen, Geometry.Parse("M0,7 L7,0 M-1,1 L1,-1 M6,8 L8,6"));
        var b = new DrawingBrush(d) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 7, 7), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 7, 7), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        b.Freeze();
        return b;
    }
}

/// <summary>The tool's small controls: menus, chips and segmented choices, in the chemistry builders' look.</summary>
internal static class CrossKit
{
    /// <summary>A menu in the tool's style (the Lewis maker's preset menu): its face and a chevron; <paramref name="fill"/>
    /// lists its items as it opens.</summary>
    public static Wpf.Ui.Controls.Button Menu(UIElement face, Action<ContextMenu> fill, string tip, string? uid = null)
    {
        var b = ChemKit.DropDown(face, fill, tip);
        if (uid != null) b.Uid = uid;
        return b;
    }

    /// <summary>A menu's item: its words (superscripts drawn), ticked when chosen, greyed when it can't be had.</summary>
    public static MenuItem Item(string text, bool on, Action pick, bool enabled = true)
    {
        var header = CrossInk.Text(text, 14, FontWeights.Normal);
        var i = new MenuItem { Header = header, IsChecked = on, IsEnabled = enabled };
        AutomationProperties.SetName(i, CrossText.Spoken(text));
        i.Click += (_, _) => pick();
        return i;
    }

    /// <summary>A chip (the Mac's glass chip): its words, tinted in <paramref name="tint"/> when chosen.</summary>
    public static Button Chip(string text, bool on, Action click, Color tint, string? tip = null, string? uid = null)
    {
        var face = CrossInk.Text(text, 13, FontWeights.SemiBold);
        face.HorizontalAlignment = HorizontalAlignment.Center;
        var b = K.Subtle(face, click, new Thickness(10, 5, 10, 6), 14);
        b.MinWidth = 52;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.BorderThickness = new Thickness(1);
        if (on)
        {
            b.Background = K.Brush(K.WithAlpha(tint, Appearance.Dark ? 0.34 : 0.2));
            b.BorderBrush = K.Brush(K.WithAlpha(tint, 0.75));
            face.Foreground = K.Brush(K.Darkened(tint));
        }
        else
        {
            b.SetResourceReference(Control.BackgroundProperty, B.Control);
            b.SetResourceReference(Control.BorderBrushProperty, B.ControlStroke);
        }
        b.ToolTip = tip ?? CrossText.Plain(text);
        if (uid != null) b.Uid = uid;
        AutomationProperties.SetName(b, CrossText.Spoken(text) + (on ? ", chosen" : ""));
        return b;
    }

    /// <summary>A few choices side by side, one chosen (the Mac's segmented picker): Windows 11's buttons, the chosen one
    /// in the accent.</summary>
    public static FrameworkElement Segmented(IReadOnlyList<string> titles, int selected, Action<int> pick, string? tip = null, bool stretch = false, string? uid = null)
    {
        var g = new UniformGrid1(stretch);
        for (var i = 0; i < titles.Count; i++)
        {
            var n = i;
            var b = PTKit.Choice(titles[i], i == selected, () => pick(n));
            b.Padding = new Thickness(12, 5, 12, 6);
            b.HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            if (tip != null) b.ToolTip = tip;
            if (uid != null) b.Uid = $"{uid}:{i}";
            AutomationProperties.SetName(b, titles[i] + (i == selected ? ", chosen" : ""));
            g.Children.Add(b);
        }
        return g;
    }

    /// <summary>A place a part of a page is drawn into, again and again: its content as wide and tall as the place.</summary>
    public static ContentControl Host() => new()
    {
        Focusable = false, IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
    };

    /// <summary>The buttons inside a control made elsewhere (a stepper's − and +) given keys, in order.</summary>
    public static T Keyed<T>(T e, string uid) where T : FrameworkElement
    {
        var i = 0;
        void Walk(DependencyObject d)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
            {
                if (child is System.Windows.Controls.Primitives.ButtonBase b) b.Uid = $"{uid}:{i++}";
                else Walk(child);
            }
        }
        if (e is System.Windows.Controls.Primitives.ButtonBase self) self.Uid = uid; else Walk(e);
        return e;
    }

    /// <summary>
    /// A page drawn again without losing the keyboard's place: the control that had the focus (or whose menu had it) is
    /// found again in what was drawn, by its key (its Uid), and focused once the new controls are on screen.
    /// </summary>
    public static void KeepFocus(FrameworkElement root, Action draw)
    {
        var uid = FocusKey(root);
        draw();
        if (uid == null) return;
        root.Dispatcher.BeginInvoke(() =>
        {
            if (Keyboard.FocusedElement is FrameworkElement { IsVisible: true } now && now.Uid == uid && root.IsAncestorOf(now)) return;
            FindUid(root, uid)?.Focus();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>The key of the control with the focus, if it is in <paramref name="root"/> (a menu's item: its menu's button's).</summary>
    private static string? FocusKey(FrameworkElement root)
    {
        var d = Keyboard.FocusedElement as DependencyObject;
        if (d is MenuItem item)
        {
            DependencyObject? p = item;
            while (p != null && p is not ContextMenu) p = LogicalTreeHelper.GetParent(p) ?? (p is MenuItem m ? ItemsControl.ItemsControlFromItemContainer(m) : null);
            d = (p as ContextMenu)?.PlacementTarget;
        }
        return d is FrameworkElement { Uid.Length: > 0 } f && root.IsAncestorOf(f) ? f.Uid : null;
    }

    private static UIElement? FindUid(DependencyObject d, string uid)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is FrameworkElement { IsVisible: true, IsEnabled: true } f && f.Uid == uid) return f;
            if (FindUid(c, uid) is { } found) return found;
        }
        return null;
    }

    /// <summary>A heading over a group of controls (the side columns').</summary>
    public static TextBlock Heading(string text) => BioUI.Heading(text);

    /// <summary>A line of help under the controls.</summary>
    public static TextBlock Caption(string text) => BioUI.Caption(text);

    /// <summary>A problem said in red, with its symbol.</summary>
    public static FrameworkElement Problem(string text) => ChemKit.Line(SymbolRegular.ErrorCircle16, text, K.Tone("red"));

    /// <summary>A note said quietly, with its symbol.</summary>
    public static FrameworkElement Note(string text, SymbolRegular symbol = SymbolRegular.Info16) => ChemKit.Line(symbol, text, null);

    /// <summary>A box's frame turned red when what is in it is wrong (and back).</summary>
    public static void Wrong(Control box, bool wrong)
    {
        if (wrong) box.BorderBrush = K.Brush(K.Tone("red"));
        else box.ClearValue(Control.BorderBrushProperty);
    }

    /// <summary>The centre column's card: rounded 12, its content scrolling up and down inside it (and wrapping to its
    /// width: only a wide drawing scrolls sideways, in <see cref="Sideways"/>).</summary>
    public static Border CentreCard(UIElement content, bool scroll = true)
    {
        UIElement inside = content;
        if (scroll) inside = Scroller(content, new Thickness(20));
        return K.Card(inside, new Thickness(scroll ? 0 : 20), 12);
    }

    /// <summary>Content that scrolls up and down, as wide as the room it has (its words wrap).</summary>
    public static ScrollViewer Scroller(UIElement content, Thickness pad) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = pad, Focusable = false,
    };

    /// <summary>A drawing wider than its room (a 64-cell square, a forked line), scrolling sideways on its own; the mouse
    /// wheel still scrolls the page (Shift and the wheel scroll it sideways).</summary>
    public static ScrollViewer Sideways(UIElement content)
    {
        var sv = new ScrollViewer
        {
            Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, Padding = new Thickness(0, 0, 0, 4),
        };
        sv.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
                e.Handled = true;
                return;
            }
            // (handed up to the page: a ScrollViewer keeps every turn of the wheel to itself)
            e.Handled = true;
            var up = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sv };
            (VisualTreeHelper.GetParent(sv) as UIElement)?.RaiseEvent(up);
        };
        return sv;
    }

    /// <summary>A caption small over a value (the working's "The question", "The product rule").</summary>
    public static TextBlock Label(string text) => K.Text(text, Ty.Caption, B.Text2).Strong();

    /// <summary>Text that can be selected and copied, figures one width.</summary>
    public static FrameworkElement Figures(string text, double size, FontWeight weight, Color? colour = null)
    {
        var t = Everyday.Figures(text, size, weight);
        if (colour is { } c) t.Foreground = K.Brush(c);
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    /// <summary>A conclusion's card: a tick or a stop sign, its words, tinted green or orange.</summary>
    public static FrameworkElement Conclusion(bool reject, IEnumerable<UIElement> lines)
    {
        var tone = reject ? K.Tone("orange") : K.Tone("green");
        var icon = K.Icon(reject ? SymbolRegular.DismissCircle24 : SymbolRegular.CheckmarkCircle24, 20, tone, filled: true);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 1, 10, 0);
        var v = K.V(8);
        foreach (var l in lines) v.Children.Add(l);
        return Everyday.TintedCard(K.Columns("auto, *", icon.Col(0), v.Col(1)), tone, new Thickness(14), 12);
    }

    /// <summary>The page's keys: Ctrl+F focuses its field.</summary>
    public static void FindKey(FrameworkElement page, Func<Control?> field)
    {
        page.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (field() is { } f)
            {
                f.Focus();
                if (f is System.Windows.Controls.TextBox t) t.SelectAll();
                e.Handled = true;
            }
        };
    }
}

/// <summary>Children in one row, each as wide as the widest (stretched to share the row when asked) — a segmented choice.</summary>
internal sealed class UniformGrid1 : Panel
{
    private readonly bool stretch;
    private const double Gap = 4;

    public UniformGrid1(bool stretch) => this.stretch = stretch;

    protected override Size MeasureOverride(Size available)
    {
        double w = 0, h = 0;
        foreach (UIElement c in InternalChildren)
        {
            c.Measure(new Size(double.PositiveInfinity, available.Height));
            w = Math.Max(w, c.DesiredSize.Width);
            h = Math.Max(h, c.DesiredSize.Height);
        }
        var n = InternalChildren.Count;
        var total = n * w + Math.Max(0, n - 1) * Gap;
        if (stretch && !double.IsInfinity(available.Width)) total = available.Width;
        return new Size(Math.Min(total, double.IsInfinity(available.Width) ? total : Math.Max(available.Width, 0)), h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var n = InternalChildren.Count;
        if (n == 0) return final;
        var w = (final.Width - Math.Max(0, n - 1) * Gap) / n;
        if (!stretch) w = Math.Min(w, InternalChildren.Cast<UIElement>().Max(c => c.DesiredSize.Width));
        for (var i = 0; i < n; i++) InternalChildren[i].Arrange(new Rect(i * (w + Gap), 0, Math.Max(0, w), final.Height));
        return final;
    }
}

// ---- gametes ----

/// <summary>A parent's gametes: its title and genotype, then each gamete as a cell — its alleles in a circle, its chance
/// under it.</summary>
internal static class GameteRow
{
    public static FrameworkElement Make(string title, string genotype, IReadOnlyList<CrossGamete> gametes, Color tint)
    {
        var words = K.H(6, K.Text(title, Ty.Caption, B.Text2).Strong(), CrossInk.Text(genotype, 13.5),
            K.Text($"· {ToolKit.Plural(gametes.Count, "kind")} of gamete", Ty.Caption, B.Text2));
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var g in gametes) row.Children.Add(Bubble(g, tint).Margin(0, 0, 8, 6));
        var v = K.V(8, words, row);
        AutomationProperties.SetName(v, $"{title}, {CrossText.Spoken(genotype)}: {gametes.Count} kinds of gamete");
        return v;
    }

    /// <summary>A gamete drawn as a cell: its alleles in a circle, its chance under it.</summary>
    public static FrameworkElement Bubble(CrossGamete g, Color tint, double size = 46)
    {
        var text = CrossInk.Text(g.Text, g.Text.Length > 4 ? 12 : 14.5, FontWeights.Bold);
        var circle = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), BorderThickness = new Thickness(1.2),
            Background = K.Brush(K.WithAlpha(tint, 0.14)), BorderBrush = K.Brush(K.WithAlpha(tint, 0.55)),
            Child = new Viewbox { Child = text, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(5) },
        };
        var p = K.Text(g.P.Text, Ty.Caption, B.Text2).Center();
        p.Tabular();
        var v = K.V(4, circle, p);
        AutomationProperties.SetName(v, $"Gamete {CrossText.Spoken(g.Text)}, chance {g.P.Text}");
        return v;
    }
}

// ---- the Punnett square ----

/// <summary>
/// A Punnett square: parent 1's gametes down the left, parent 2's across the top, each cell its genotype, tinted by its
/// phenotype (and named under it while there is room); a cell that dies before birth is hatched grey. Pointing at a cell
/// lights every cell with the same genotype. <c>highlight</c>, if given, lights the cells that meet a target and dims the
/// rest (the Probability page's small squares).
/// </summary>
internal sealed class PunnettGrid : ContentControl
{
    private readonly List<(Border Cell, string Key, Color Tint, bool Lit)> cells = new();
    private readonly bool highlighting;
    private string? hovered;

    public PunnettGrid(CrossPunnett square, Func<int[][], bool>? highlight = null, bool small = false)
    {
        Focusable = false;
        highlighting = highlight != null;
        var n = Math.Max(square.Rows.Count, square.Columns.Count);
        double cw = small ? 52 : (n <= 2 ? 96 : (n <= 4 ? 84 : 70));
        double ch = small ? 40 : (n <= 2 ? 72 : (n <= 4 ? 58 : 44));
        double font = small ? 13 : (n <= 2 ? 18 : (n <= 4 ? 15 : 12.5));
        var named = !small && n <= 4;
        var head = small ? 30.0 : 44.0;
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in square.Columns) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in square.Rows) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // (the corner: whose gametes go which way)
        var corner = K.V(0, K.Text("♂ →", Ty.Caption, B.Text3).Center(), K.Text("♀ ↓", Ty.Caption, B.Text3).Center());
        corner.VerticalAlignment = VerticalAlignment.Center;
        corner.Width = head;
        AutomationProperties.SetName(corner, "");
        g.Children.Add(corner);
        for (var j = 0; j < square.Columns.Count; j++) g.Children.Add(Header(square.Columns[j], head, small).Row(0).Col(j + 1));
        for (var i = 0; i < square.Rows.Count; i++)
        {
            g.Children.Add(Header(square.Rows[i], head, small).Row(i + 1).Col(0));
            for (var j = 0; j < square.Columns.Count; j++)
            {
                var c = square.Cells[i][j];
                var lit = highlight?.Invoke(c.Genotype) ?? true;
                g.Children.Add(Cell(square, i, j, c, cw, ch, font, named, lit).Row(i + 1).Col(j + 1));
            }
        }
        Content = g;
        AutomationProperties.SetName(this, $"Punnett square, {square.Rows.Count} by {square.Columns.Count}");
    }

    private static FrameworkElement Header(CrossGamete gamete, double size, bool small)
    {
        var t = CrossInk.Text(gamete.Text, small ? 12 : 14, FontWeights.Bold);
        var dot = new Border { Width = size - 4, Height = size - 4, CornerRadius = new CornerRadius((size - 4) / 2) };
        dot.Bg(B.Subtle);
        var box = new Grid { MinWidth = size, MinHeight = size, Margin = new Thickness(2) };
        box.Children.Add(dot);
        box.Children.Add(new Viewbox { Child = t, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(4, 0, 4, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetName(box, "Gamete " + CrossText.Spoken(gamete.Text));
        return box;
    }

    private FrameworkElement Cell(CrossPunnett sq, int i, int j, CrossPunnett.Cell c, double w, double h, double font, bool named, bool lit)
    {
        var tint = c.Dies ? BioPalette.Coil.ToColor() : CrossColors.Phenotype(c.Tint, c.Phenotype);
        var line = K.H(2);
        line.HorizontalAlignment = HorizontalAlignment.Center;
        if (c.Sex is { } s)
        {
            var sex = K.Text(s == BioSex.Male ? "♂" : "♀", Ty.Caption, B.Text2);
            sex.FontSize = font * 0.7;
            sex.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(sex);
        }
        var geno = CrossInk.Text(c.Text, font, FontWeights.SemiBold);
        if (c.Dies) geno.TextDecorations = TextDecorations.Strikethrough;
        line.Children.Add(geno);
        var words = K.V(2, new Viewbox { Child = line, StretchDirection = StretchDirection.DownOnly });
        if (named)
        {
            var label = c.Dies ? "dies" : (c.Carrier ? $"{c.Phenotype} · carrier" : c.Phenotype);
            var under = CrossInk.Text(label, 10.5, FontWeights.Medium, B.Text2);
            under.HorizontalAlignment = HorizontalAlignment.Center;
            words.Children.Add(new Viewbox { Child = under, StretchDirection = StretchDirection.DownOnly });
        }
        words.VerticalAlignment = VerticalAlignment.Center;
        words.Margin = new Thickness(3, 0, 3, 0);
        var inside = new Grid();
        if (c.Dies) inside.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = CrossColors.Hatching(), Opacity = 0.5 });
        inside.Children.Add(words);
        var b = new Border
        {
            Width = w, Height = h, Margin = new Thickness(2), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = inside,
            Background = K.Brush(K.WithAlpha(tint, c.Dies ? 0.08 : (lit ? 0.24 : 0.06))), Opacity = lit ? 1 : 0.45, SnapsToDevicePixels = true,
        };
        var key = CrossPunnett.Key(c.Genotype);
        cells.Add((b, key, tint, lit));
        Paint(b, tint, lit, false);
        b.ToolTip = c.Dies ? $"{CrossText.Plain(c.Text)} dies before birth" : $"{CrossText.Plain(c.Text)}: {c.Phenotype}{(c.Carrier ? ", a carrier" : "")}";
        b.MouseEnter += (_, _) => Light(key);
        b.MouseLeave += (_, _) => { if (hovered == key) Light(null); };
        AutomationProperties.SetName(b, $"{CrossText.Spoken(sq.Rows[i].Text)} with {CrossText.Spoken(sq.Columns[j].Text)}: {CrossText.Spoken(c.Text)}, {(c.Dies ? "dies before birth" : c.Phenotype)}{(c.Carrier ? ", carrier" : "")}");
        return b;
    }

    /// <summary>Every cell with this genotype lit (null: none).</summary>
    private void Light(string? key)
    {
        hovered = key;
        foreach (var (cell, k, tint, lit) in cells) Paint(cell, tint, lit, k == key);
    }

    /// <summary>A cell's edge: dark under the pointer, the accent when it meets the target, else its tint.</summary>
    private void Paint(Border b, Color tint, bool lit, bool hover)
    {
        b.BorderThickness = new Thickness(hover || (highlighting && lit) ? 2 : 1);
        if (hover) b.SetResourceReference(Border.BorderBrushProperty, B.Text);
        else if (highlighting && lit) b.SetResourceReference(Border.BorderBrushProperty, B.Accent);
        else b.BorderBrush = K.Brush(K.WithAlpha(tint, 0.45));
    }
}

// ---- the forked line ----

/// <summary>The forked-line (branch) diagram: each gene's outcomes in a column, every path through them a row, its chance
/// the product along the path — the product rule drawn out.</summary>
internal static class ForkedLineView
{
    private const double RowHeight = 30, Column = 136, LeafWidth = 250, Top = 30;

    public static FrameworkElement Make(CrossForkedLine line, IReadOnlyList<BioGene> genes, IReadOnlyDictionary<string, Color> tints)
    {
        var rows = Math.Max(line.Leaves.Count, 1);
        var height = rows * RowHeight + Top;
        var width = line.Levels.Count * Column + LeafWidth;
        var canvas = new Canvas { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left };
        int SpanBelow(int k) => line.Levels.Skip(k + 1).Aggregate(1, (a, l) => a * Math.Max(l.Count, 1));
        int NodeCount(int k) => line.Levels.Take(k + 1).Aggregate(1, (a, l) => a * Math.Max(l.Count, 1));
        double CentreY(int node, int span) => Top + (node * span + span / 2.0) * RowHeight;
        // (the branches: across, down, across)
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (var k = 0; k < line.Levels.Count; k++)
            {
                var span = SpanBelow(k);
                for (var node = 0; node < NodeCount(k); node++)
                {
                    var y = CentreY(node, span);
                    var xEnd = k * Column + Column - 14;
                    if (k + 1 < line.Levels.Count)
                    {
                        var childSpan = SpanBelow(k + 1);
                        for (var c = 0; c < line.Levels[k + 1].Count; c++)
                        {
                            var cy = CentreY(node * line.Levels[k + 1].Count + c, childSpan);
                            var x0 = (k + 1) * Column;
                            ctx.BeginFigure(new Point(xEnd, y), false, false);
                            ctx.PolyLineTo(new[] { new Point(x0 - 8, y), new Point(x0 - 8, cy), new Point(x0 + 2, cy) }, true, false);
                        }
                    }
                    else
                    {
                        ctx.BeginFigure(new Point(xEnd, y), false, false);
                        ctx.LineTo(new Point(line.Levels.Count * Column + 2, y), true, false);
                    }
                }
            }
        }
        geo.Freeze();
        var path = new Path { Data = geo, StrokeThickness = 1, Opacity = 0.55 };
        path.SetResourceReference(Shape.StrokeProperty, B.Text2);
        canvas.Children.Add(path);
        void Put(UIElement e, double x, double centreY, double w)
        {
            if (e is FrameworkElement fe) { fe.Width = w; fe.Height = 22; }
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, centreY - 11);
            canvas.Children.Add(e);
        }
        for (var k = 0; k < genes.Count && k < line.Levels.Count; k++)
            Put(K.Text($"Gene {genes[k].Letter}", Ty.Caption, B.Text2).Strong(), k * Column + 2, 9, Column - 16);
        Put(K.Text("Offspring", Ty.Caption, B.Text2).Strong(), line.Levels.Count * Column + 8, 9, LeafWidth - 12);
        // (each node: its chance and its outcome)
        for (var k = 0; k < line.Levels.Count; k++)
        {
            var level = line.Levels[k];
            if (level.Count == 0) continue;
            var span = SpanBelow(k);
            for (var node = 0; node < NodeCount(k); node++)
            {
                var b = level[node % level.Count];
                var p = K.Text(b.P.Text, Ty.Caption, B.Text2).Tabular();
                p.VerticalAlignment = VerticalAlignment.Center;
                var t = CrossInk.Text(b.Label, 13);
                t.VerticalAlignment = VerticalAlignment.Center;
                var row = K.H(5, p, new Viewbox { Child = t, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
                row.VerticalAlignment = VerticalAlignment.Center;
                Put(new Border { Child = row }, k * Column + 2, CentreY(node, span), Column - 22);
            }
        }
        // (the leaves: a dot of the phenotype's tint, the chance, the outcome)
        for (var i = 0; i < line.Leaves.Count; i++)
        {
            var l = line.Leaves[i];
            var tint = tints.TryGetValue(l.Label, out var c) ? c : BioPalette.Coil.ToColor();
            var dot = new Ellipse { Width = 8, Height = 8, Fill = K.Brush(l.Dies ? K.WithAlpha(BioPalette.Coil.ToColor(), 0.4) : tint), VerticalAlignment = VerticalAlignment.Center };
            var p = Everyday.Figures(l.P.Text, 14, FontWeights.SemiBold);
            p.VerticalAlignment = VerticalAlignment.Center;
            var label = CrossInk.Text(l.Label, 13.5, FontWeights.Medium);
            label.VerticalAlignment = VerticalAlignment.Center;
            var row = K.H(7, dot, p, new Viewbox { Child = label, StretchDirection = StretchDirection.DownOnly });
            if (l.Dies) row.Children.Add(K.Text("dies", Ty.Caption, B.Text2));
            row.VerticalAlignment = VerticalAlignment.Center;
            var holder = new Border { Child = row, Background = Brushes.Transparent };
            AutomationProperties.SetName(holder, $"{CrossText.Spoken(l.Label)}: {l.P.Text}{(l.Dies ? ", dies" : "")}");
            Put(holder, line.Levels.Count * Column + 8, Top + (i + 0.5) * RowHeight, LeafWidth - 12);
        }
        AutomationProperties.SetName(canvas, $"Forked-line diagram, {line.Leaves.Count} outcomes");
        return canvas;
    }
}

// ---- Why? Independent assortment ----

/// <summary>
/// "Why?": the parent's genes on different chromosome pairs, lining up at metaphase I one way or the other — each way as
/// likely — so every combination of alleles reaches a gamete equally often (independent assortment). Shown in the Cross
/// page itself (the tool may already be in a sheet). Turning a pair round slides its homologs past each other.
/// </summary>
internal sealed class AssortmentPanel : ContentControl
{
    private readonly ToolContext c;
    private readonly CrossSetup setup;
    private int parent = 1;
    private readonly bool[] flips = new bool[3];
    /// <summary>Each pair's two homologs as drawn, and how far apart their places are, to slide them when the pair turns.</summary>
    private readonly List<(FrameworkElement A, FrameworkElement B, double Shift)> pairs = new();

    private sealed record Pair(BioGene Gene, string A, string B, bool Flipped)
    {
        public bool Het => A != B;
        public string Left => Flipped ? B : A;
        public string Right => Flipped ? A : B;
    }

    public AssortmentPanel(ToolContext c, CrossSetup setup)
    {
        this.c = c;
        this.setup = setup;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Draw(null);
    }

    private List<Pair> Pairs()
    {
        var genotype = parent == 1 ? setup.Parent1 : setup.Parent2;
        return setup.Genes.Select((gene, k) =>
        {
            var g = genotype[k];
            return new Pair(gene, gene.Alleles[g[0]], gene.Alleles[g.Length > 1 ? g[1] : g[0]], k < flips.Length && flips[k]);
        }).ToList();
    }

    private static string Explanation(int hets) => hets switch
    {
        0 => "This parent is homozygous for every gene, so every gamete gets the same alleles.",
        1 => "The two alleles of the heterozygous gene sit on a pair of homologous chromosomes. Meiosis I pulls the pair apart, so half the gametes get one allele and half the other (segregation).",
        _ => $"Each gene is on a different pair of chromosomes. At metaphase I each pair lines up at the plate with either homolog facing either pole, independently of the others, so all {1 << hets} combinations reach the gametes equally often (independent assortment).",
    };

    /// <summary>Drawn again; the pairs whose arrangement differs from <paramref name="before"/> slide to their new places.</summary>
    private void Draw(bool[]? before)
    {
        var ps = Pairs();
        var hets = ps.Count(p => p.Het);
        var x = setup.XIndex != null;
        var who = CrossKit.Segmented(new[] { x ? "Mother" : "Parent 1", x ? "Father" : "Parent 2" }, parent - 1, i =>
        {
            parent = i + 1;
            Array.Clear(flips);
            Redraw(null);
        }, uid: "assort:parent");
        var turn = K.Button("Line up the other way", () => FlipOne(Pairs()), accent: true, icon: SymbolRegular.ArrowSwap24);
        turn.Uid = "assort:turn";
        turn.IsEnabled = hets > 0;
        turn.ToolTip = "Turn a pair round at the plate: the other arrangement, just as likely";
        var divide = K.Button("See it in Cell Division", () =>
        {
            BioNav.Set("divide", "meiosis");
            BioTools.Open(c, "divide", new Dictionary<string, string> { ["focus"] = "assortment" });
        }, icon: SymbolRegular.ArrowSplit24);
        divide.ToolTip = "Open meiosis in Cell Division";
        divide.Uid = "assort:divide";
        var made = Made(ps);
        var gives = new WrapPanel();
        gives.Children.Add(K.Text("This arrangement gives", Ty.Body, B.Text2).Margin(0, 4, 8, 4));
        foreach (var g in made)
        {
            var chip = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(0, 0, 6, 0), Background = K.Brush(K.WithAlpha(CrossColors.Tool, 0.16)), Child = CrossInk.Text(g, 15, FontWeights.Bold) };
            gives.Children.Add(chip);
        }
        AutomationProperties.SetName(gives, "This arrangement gives " + string.Join(" and ", made.Select(CrossText.Spoken)));
        var buttons = new WrapPanel();
        buttons.Children.Add(turn.Margin(0, 0, 8, 6));
        buttons.Children.Add(divide.Margin(0, 0, 8, 6));
        var head = K.Columns("*, auto", K.Text("Why these gametes?", Ty.BodyLarge).Strong().Col(0),
            K.IconButton(SymbolRegular.Dismiss24, () => CrossesModel.SetWhyShown(false), "Close (the square again)").Col(1));
        var v = K.V(12, head, who.Align(HorizontalAlignment.Left), K.Wrapped(Explanation(hets), Ty.Body), Metaphase(ps), buttons, gives);
        Content = Everyday.TintedCard(v, CrossColors.Tool, new Thickness(16), 12);
        if (before != null) Slide(before);
    }

    /// <summary>Drawn again, the keyboard's place kept.</summary>
    private void Redraw(bool[]? before) => CrossKit.KeepFocus(this, () => Draw(before));

    /// <summary>The first heterozygous pair stays put and the others count through every arrangement, like a binary
    /// counter; with one such pair, it simply turns round.</summary>
    private void FlipOne(List<Pair> ps)
    {
        var hets = Enumerable.Range(0, ps.Count).Where(k => ps[k].Het && k < flips.Length).ToList();
        if (hets.Count == 0) return;
        var before = flips.ToArray();
        if (hets.Count == 1) flips[hets[0]] = !flips[hets[0]];
        else
            foreach (var k in hets.Skip(1))
            {
                if (flips[k]) flips[k] = false;
                else { flips[k] = true; break; }
            }
        Redraw(before);
    }

    private static List<string> Made(List<Pair> ps)
    {
        static string Join(IEnumerable<string> parts)
        {
            var l = parts.ToList();
            return l.All(p => p.Length == 1) ? string.Concat(l) : string.Join(" ", l);
        }
        string l = Join(ps.Select(p => p.Left)), r = Join(ps.Select(p => p.Right));
        return l == r ? new List<string> { l } : new List<string> { l, r };
    }

    private FrameworkElement Metaphase(List<Pair> ps)
    {
        double[] lengths = { 150, 96, 122 };
        pairs.Clear();
        var rows = K.V(14);
        for (var k = 0; k < ps.Count; k++)
        {
            var len = lengths[k % lengths.Length];
            FrameworkElement a = Chromosome(ps[k].A, true, len), b = Chromosome(ps[k].B, false, len);
            // (each homolog keeps its place in the row; a turned pair is drawn swapped, and slides there when it turns)
            var g = new Canvas { Width = 2 * len + 10, Height = 22, HorizontalAlignment = HorizontalAlignment.Center };
            var shift = len + 10;
            Canvas.SetLeft(a, ps[k].Flipped ? shift : 0);
            Canvas.SetLeft(b, ps[k].Flipped ? 0 : shift);
            g.Children.Add(a);
            g.Children.Add(b);
            pairs.Add((a, b, shift));
            AutomationProperties.SetName(g, $"Pair {k + 1}: {CrossText.Spoken(ps[k].Left)} facing the left pole, {CrossText.Spoken(ps[k].Right)} facing the right");
            rows.Children.Add(g);
        }
        rows.Margin = new Thickness(0, 30, 0, 14);
        rows.HorizontalAlignment = HorizontalAlignment.Center;
        var plate = new Border { Width = 1.5, Margin = new Thickness(0, 26, 0, 10), HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.5 };
        plate.Bg(B.Text2);
        var ground = new Border { CornerRadius = new CornerRadius(14) };
        ground.Bg(B.Subtle);
        var poles = K.Columns("auto, *, auto", K.Text("pole", Ty.Caption, B.Text3).Col(0), K.Text("metaphase plate", Ty.Caption, B.Text3).Center().Col(1), K.Text("pole", Ty.Caption, B.Text3).Col(2));
        poles.Margin = new Thickness(14, 8, 14, 0);
        poles.VerticalAlignment = VerticalAlignment.Top;
        var g2 = new Grid { MinHeight = Math.Max(150, ps.Count * 56 + 50), Children = { ground, plate, poles, rows } };
        return g2;
    }

    /// <summary>The pairs that just turned, slid from where they were (Windows' animations on): each homolog from the
    /// other's place, quick to start and long to settle (the app's strong ease-out).</summary>
    private void Slide(bool[] before)
    {
        if (!Motion.On) return;
        for (var k = 0; k < pairs.Count && k < before.Length; k++)
        {
            if (before[k] == flips[k]) continue;
            var (a, b, shift) = pairs[k];
            // (a homolog now at the left came from the right, and the other way)
            var fromA = flips[k] ? -shift : shift;
            var ta = new TranslateTransform(fromA, 0);
            var tb = new TranslateTransform(-fromA, 0);
            a.RenderTransform = ta;
            b.RenderTransform = tb;
            Motion.FromTo(ta, TranslateTransform.XProperty, fromA, 0, Motion.Page, Motion.Out);
            Motion.FromTo(tb, TranslateTransform.XProperty, -fromA, 0, Motion.Page, Motion.Out);
        }
    }

    /// <summary>A replicated chromosome lying across the spindle: two sister chromatids (maternal red, paternal blue), its
    /// allele on it.</summary>
    private static FrameworkElement Chromosome(string allele, bool maternal, double length)
    {
        var c = maternal ? BioPalette.Maternal.ToColor() : BioPalette.Paternal.ToColor();
        var arms = K.V(2,
            new Border { Width = length, Height = 9, CornerRadius = new CornerRadius(4.5), Background = K.Brush(c) },
            new Border { Width = length, Height = 9, CornerRadius = new CornerRadius(4.5), Background = K.Brush(K.WithAlpha(c, 0.85)) });
        var label = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 1), Background = K.Brush(c), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Child = CrossInk.Text(allele, 13, FontWeights.Bold, Colors.White),
        };
        var g = new Grid { Children = { arms, label } };
        AutomationProperties.SetName(g, $"{(maternal ? "maternal" : "paternal")} homolog with {CrossText.Spoken(allele)}");
        return g;
    }
}

// ---- the critical values (shared with Hardy–Weinberg's test) ----

/// <summary>§5.3.4's critical values (the AP sheet's p = 0.05 and 0.01 columns, with 0.10 and 0.001), the row for this df
/// lit and where χ² falls in it said.</summary>
internal static class ChiCriticalTable
{
    public static FrameworkElement Make(int df, double? chi2, Color tint)
    {
        var g = new Grid();
        for (var i = 0; i < 1 + BioChiSquare.TableP.Length; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r <= 10; r++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Cell(string s, int row, int col, bool bold)
        {
            var t = K.Text(s, row == 0 ? Ty.Caption : Ty.Body, row == 0 || row != df ? B.Text2 : B.Text);
            if (bold) t.FontWeight = FontWeights.Bold;
            if (row == 0) t.FontWeight = FontWeights.SemiBold;
            t.Tabular();
            t.TextAlignment = col == 0 ? TextAlignment.Left : TextAlignment.Right;
            var b = new Border { Child = t, Padding = new Thickness(6, 2, 6, 2) };
            if (row == df) b.Background = K.Brush(K.WithAlpha(tint, 0.14));
            g.Children.Add(b.Row(row).Col(col));
        }
        Cell("df", 0, 0, true);
        for (var i = 0; i < BioChiSquare.TableP.Length; i++)
            Cell(BioChiSquare.TableP[i] == 0.001 ? "0.001" : HWFormat.Fixed(BioChiSquare.TableP[i], 2), 0, i + 1, true);
        for (var d = 1; d <= 10; d++)
        {
            Cell($"{d}", d, 0, d == df);
            var row = BioChiSquare.Table.TryGetValue(d, out var v) ? v : new double[4];
            for (var i = 0; i < BioChiSquare.TableP.Length; i++) Cell(HWFormat.Fixed(row[i], 2), d, i + 1, d == df && BioChiSquare.TableP[i] == 0.05);
        }
        AutomationProperties.SetName(g, $"Critical values of chi-square; df {df} lit");
        var lines = new List<UIElement?> { g };
        if (df > 10) lines.Add(BioUI.Caption($"df {df}: the critical value at p = 0.05 is {HWFormat.Fixed(BioChiSquare.Critical(df), 2)} (computed)."));
        if (chi2 is { } x) lines.Add(K.Wrapped(Placement(df, x), Ty.Body));
        var card = BioUI.Card("Critical values", null, "The χ² a difference must pass, by df and p", lines.ToArray());
        return K.V(14, card, BioUI.Caption("p is the chance of a difference at least this large if the null hypothesis is true. Below 0.05, the difference is called significant."));
    }

    /// <summary>Where χ² falls in this df's row.</summary>
    public static string Placement(int df, double chi2)
    {
        if (!BioChiSquare.Table.TryGetValue(df, out var row) || row.Length != BioChiSquare.TableP.Length) return "";
        int? beyond = null;
        for (var i = 0; i < row.Length; i++) if (chi2 > row[i]) beyond = i;
        if (beyond is not { } b) return $"χ² = {HWFormat.Fixed(chi2, 2)} is below {HWFormat.Fixed(row[0], 2)}, so p > 0.10.";
        var p = BioChiSquare.TableP[b];
        return $"χ² = {HWFormat.Fixed(chi2, 2)} is beyond {HWFormat.Fixed(row[b], 2)}, so p < {(p == 0.001 ? "0.001" : HWFormat.Fixed(p, 2))}.";
    }
}

/// <summary>Observed counts as bars against expected ones as lines (the chi-square pages'): each category named under its
/// bar, the values on hover, a legend under it.</summary>
internal sealed class ObservedChart : Grid
{
    private readonly Canvas plot = new() { ClipToBounds = false };
    private readonly IReadOnlyList<string> names;
    private readonly IReadOnlyList<double> observed, expected;
    private readonly IReadOnlyList<Color> colours;

    public ObservedChart(IReadOnlyList<string> names, IReadOnlyList<double> observed, IReadOnlyList<double> expected, IReadOnlyList<Color> colours, string expectedWords = "expected")
    {
        this.names = names;
        this.observed = observed;
        this.expected = expected;
        this.colours = colours;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(170) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(plot.Row(0));
        var key = new Rectangle { Width = 14, Height = 3, VerticalAlignment = VerticalAlignment.Center };
        key.SetResourceReference(Shape.FillProperty, B.Text);
        var swatch = new Border { Width = 12, Height = 10, CornerRadius = new CornerRadius(2), Background = K.Brush(colours.Count > 0 ? colours[0] : CrossColors.Tool), VerticalAlignment = VerticalAlignment.Center };
        // (bars of one colour: its swatch; bars of each category's own colour are said in words)
        var bars = colours.Distinct().Count() > 1 ? (UIElement)K.Text("bars: observed", Ty.Caption, B.Text2) : K.H(5, swatch, K.Text("observed", Ty.Caption, B.Text2));
        var legend = K.H(14, bars, K.H(5, key, K.Text(expectedWords, Ty.Caption, B.Text2)));
        legend.Margin = new Thickness(0, 6, 0, 0);
        Children.Add(legend.Row(1));
        AutomationProperties.SetName(this, "Observed counts as bars, expected counts as lines: " + string.Join("; ", names.Select((n, i) => $"{CrossText.Spoken(n)} observed {ChiSquareWork.Number(observed[i])}, expected {HWFormat.Fixed(expected[i], 2)}")));
        plot.SizeChanged += (_, _) => Draw();
    }

    private void Draw()
    {
        plot.Children.Clear();
        double w = plot.ActualWidth, h = plot.ActualHeight;
        if (w <= 0 || h <= 0 || names.Count == 0) return;
        const double bottom = 22, left = 36;
        var top = 8.0;
        var max = Math.Max(1, Math.Max(observed.DefaultIfEmpty(0).Max(), expected.DefaultIfEmpty(0).Max())) * 1.1;
        var plotH = h - bottom - top;
        double Y(double v) => top + plotH * (1 - v / max);
        // (three quiet grid lines and their values)
        var step = Nice(max / 3);
        for (var v = 0.0; v <= max; v += step)
        {
            var line = new Rectangle { Width = w - left, Height = 1, Opacity = v == 0 ? 0.7 : 0.35 };
            line.SetResourceReference(Shape.FillProperty, B.Divider);
            Canvas.SetLeft(line, left);
            Canvas.SetTop(line, Y(v));
            plot.Children.Add(line);
            var t = K.Text(ChiSquareWork.Number(Math.Round(v, 6)), Ty.Caption, B.Text3).Tabular();
            t.Width = left - 6;
            t.TextAlignment = TextAlignment.Right;
            Canvas.SetLeft(t, 0);
            Canvas.SetTop(t, Y(v) - 8);
            plot.Children.Add(t);
        }
        var slot = (w - left) / names.Count;
        for (var i = 0; i < names.Count; i++)
        {
            var cx = left + slot * (i + 0.5);
            var bw = Math.Min(64, slot * 0.55);
            var c = colours[i % colours.Count];
            var barH = Math.Max(0, Y(0) - Y(observed[i]));
            var bar = new Border { Width = bw, Height = barH, CornerRadius = new CornerRadius(4, 4, 0, 0), Background = K.Brush(K.WithAlpha(c, 0.8)) };
            bar.ToolTip = $"{CrossText.Plain(names[i])}: observed {ChiSquareWork.Number(observed[i])}, expected {HWFormat.Fixed(expected[i], 2)}";
            Canvas.SetLeft(bar, cx - bw / 2);
            Canvas.SetTop(bar, Y(observed[i]));
            plot.Children.Add(bar);
            var mark = new Rectangle { Width = Math.Min(84, slot * 0.75), Height = 3, RadiusX = 1.5, RadiusY = 1.5, IsHitTestVisible = false, Opacity = 0.8 };
            mark.SetResourceReference(Shape.FillProperty, B.Text);
            Canvas.SetLeft(mark, cx - mark.Width / 2);
            Canvas.SetTop(mark, Y(expected[i]) - 1.5);
            plot.Children.Add(mark);
            var label = CrossInk.Text(names[i], 12, FontWeights.Medium, B.Text2);
            label.TextAlignment = TextAlignment.Center;
            var box = new Viewbox { Child = label, Width = Math.Max(1, slot - 4), Height = 16, StretchDirection = StretchDirection.DownOnly };
            Canvas.SetLeft(box, cx - box.Width / 2);
            Canvas.SetTop(box, h - bottom + 4);
            plot.Children.Add(box);
        }
    }

    /// <summary>A round step near <paramref name="x"/>: 1, 2 or 5 times a power of ten.</summary>
    private static double Nice(double x)
    {
        if (x <= 0) return 1;
        var e = Math.Pow(10, Math.Floor(Math.Log10(x)));
        var f = x / e;
        return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * e;
    }
}
