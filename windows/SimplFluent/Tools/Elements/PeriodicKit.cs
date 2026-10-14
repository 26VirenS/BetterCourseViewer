using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace Simpl.Tools;

// What the Periodic Table's views share across the window (the Mac's PTSearch, PTNav and the tool's own state), and the
// small pieces its views are drawn with. (Its Lewis structures and Molecule builder pages, and its element page's atom in
// 3D, are tools of their own since 0.3.2: Tools/Chemistry/ChemTools.cs.)

/// <summary>The Periodic Table's tour asked for in the tool's heading (its Tour, beside the search; the Mac's PTNav),
/// heard by the table on screen.</summary>
public static class PTNav
{
    /// <summary>The heading's Tour pressed.</summary>
    public static event Action? TourAsked;

    public static void AskTour() => TourAsked?.Invoke();
}

/// <summary>
/// The Periodic Table's search, kept here so the tool's header can hold its field (left of the pin) and the table light
/// what it finds (the Mac's PTSearch): what is typed, whether the field is open, Return pressed, a result picked.
/// </summary>
public static class PTSearch
{
    private static string query = "";
    private static bool searching;

    /// <summary>What is typed, or the field opened or shut.</summary>
    public static event Action? Changed;
    /// <summary>Return in the field with no result lit: the table opens the first element it finds.</summary>
    public static event Action? Submitted;
    /// <summary>An element picked from the field's results: the table opens it.</summary>
    public static event Action<int>? Picked;

    public static string Query
    {
        get => query;
        set { value ??= ""; if (query == value) return; query = value; Changed?.Invoke(); }
    }

    /// <summary>The field open (a glass button opened into a field, on the Mac).</summary>
    public static bool Searching
    {
        get => searching;
        set { if (searching == value) return; searching = value; Changed?.Invoke(); }
    }

    public static void Submit() => Submitted?.Invoke();

    public static void Pick(int number) => Picked?.Invoke(number);

    /// <summary>The elements a search lights, or null when nothing is typed.</summary>
    public static HashSet<int>? Matches(string q) => q.Trim().Length == 0 ? null : PeriodicTable.Find(q).Select(e => e.Number).ToHashSet();
}

/// <summary>What the table is coloured by and the kind it lights, kept while its views come and go (a pin's sheet, a
/// visit elsewhere) — the colouring on this PC for next time too (ChemPrefs <c>ptable.colour</c>, as the Mac keeps it) —
/// and the element last opened on its page (<c>ptable.element</c>, its number: Atoms &amp; Orbitals opens on it until it
/// has shown one of its own).</summary>
internal static class PTState
{
    private static PTColouring? colouring;

    public static PTColouring Colouring
    {
        get => colouring ??= PTColourings.Parse(ChemPrefs.String("ptable", "colour")) ?? PTColouring.Category;
        set
        {
            colouring = value;
            ChemPrefs.Set("ptable", "colour", value.ToString().ToLowerInvariant());
        }
    }

    public static string? Category;

    /// <summary>An element's page opened: kept as the last.</summary>
    public static void Opened(PTElement e) => ChemPrefs.Set("ptable", "element", e.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>The small pieces the Periodic Table's views are drawn with.</summary>
internal static class PTKit
{
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    /// <summary>Something done after a moment, once (the Mac's asyncAfter); the timer returned can stop it first.</summary>
    public static DispatcherTimer After(double seconds, Action a)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, _) => { t.Stop(); a(); };
        t.Start();
        return t;
    }

    /// <summary>Text at a size of its own (a cell's, a tile's), in the app's face.</summary>
    public static TextBlock Text(string s, double size, FontWeight weight, string brush = B.Text, FontFamily? face = null)
    {
        var t = new TextBlock { Text = s, FontSize = Math.Max(1, size), FontWeight = weight, FontFamily = face ?? K.TextFace, TextTrimming = TextTrimming.CharacterEllipsis };
        t.Fg(brush);
        return t;
    }

    /// <summary>Text in a fixed colour (on a tile of an element's colour).</summary>
    public static TextBlock Text(string s, double size, FontWeight weight, Color color, FontFamily? face = null)
    {
        var t = new TextBlock { Text = s, FontSize = Math.Max(1, size), FontWeight = weight, FontFamily = face ?? K.TextFace, Foreground = new SolidColorBrush(color) };
        return t;
    }

    /// <summary>Figures of one width.</summary>
    public static T Tabular<T>(this T t) where T : TextBlock { Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); return t; }

    /// <summary>One line that shrinks to fit rather than run over (the Mac's lineLimit(1) with a minimumScaleFactor).</summary>
    public static Viewbox Fit(UIElement child, HorizontalAlignment h = HorizontalAlignment.Center) =>
        new() { Child = child, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = h };

    /// <summary>Words that can be selected and copied (the Mac's textSelection): a borderless read-only box, the page's
    /// scroll wheel passing over it.</summary>
    public static TextBox Selectable(string text, double size, FontWeight weight, string brush = B.Text, FontFamily? face = null, bool wrap = true)
    {
        var b = new TextBox
        {
            Text = text, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0),
            FontSize = size, FontWeight = weight, FontFamily = face ?? K.TextFace, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Cursor = Cursors.IBeam, FocusVisualStyle = null, IsUndoEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        b.Style = new Style(typeof(TextBox));
        b.Template = BareTemplate;
        b.SetResourceReference(Control.ForegroundProperty, brush);
        b.SetResourceReference(TextBox.SelectionBrushProperty, B.Accent);
        b.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            var up = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = b };
            (VisualTreeHelper.GetParent(b) as UIElement)?.RaiseEvent(up);
        };
        return b;
    }

    /// <summary>A text box drawn as only its words (no frame, no focus line).</summary>
    private static readonly ControlTemplate BareTemplate = MakeBare();

    private static ControlTemplate MakeBare()
    {
        var t = new ControlTemplate(typeof(TextBox));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(ScrollViewer.FocusableProperty, false);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        host.SetValue(Control.PaddingProperty, new Thickness(0));
        t.VisualTree = host;
        t.Seal();
        return t;
    }

    /// <summary>A word with a dot of its colour in a quiet pill (the element page's kind, block, group and state).</summary>
    public static Border Chip(string text, Color? dot, string? dotBrush = null)
    {
        var d = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        if (dot is { } c) d.Fill = new SolidColorBrush(c); else d.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, dotBrush ?? B.Text2);
        var t = K.Text(text, Ty.Body);
        t.FontWeight = FontWeights.Medium;
        var b = new Border { Child = K.H(6, d, t), Padding = new Thickness(11, 4, 11, 5), CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Center };
        b.Bg(B.Subtle);
        return b;
    }

    /// <summary>An element's tile: its symbol in white on its kind's colour.</summary>
    public static Border Tile(PTElement e, double size, double font, double corner)
    {
        var sym = Text(e.Symbol, font, FontWeights.Bold, Colors.White);
        sym.HorizontalAlignment = HorizontalAlignment.Center;
        sym.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(corner), Background = new SolidColorBrush(K.WithAlpha(e.Kind.Color, 0.85)), Child = sym };
    }

    /// <summary>A button of a set where one is chosen (the Mac's glass buttons, the chosen one prominent): Windows 11's
    /// standard button, the accent's for the chosen; a symbol alone when room is wanted (the Mac's SquishLabel).</summary>
    public static Wpf.Ui.Controls.Button Choice(string text, bool on, Action click, SymbolRegular? icon = null, bool squished = false)
    {
        var b = K.Button(text, click, accent: on, icon: icon);
        b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        if (squished && icon != null)
        {
            b.Content = null;
            b.MinWidth = 0;
            b.Padding = new Thickness(10, 6, 10, 6);
            System.Windows.Automation.AutomationProperties.SetName(b, text);
        }
        else b.MinWidth = 0;
        return b;
    }

    /// <summary>
    /// A row of chips kept on one line (the table's key, its colourings): scrolled sideways where it is wider than the
    /// page — with its scroll bar, or the wheel with Shift — the wheel alone still scrolling the page under it. While it
    /// scrolls, room is kept under the chips for the bar (Windows 11's bar is drawn over a scroller's content, and would
    /// otherwise cut through the chips' lower edge).
    /// </summary>
    public static ScrollViewer ChipRow(UIElement row)
    {
        var sv = new ScrollViewer
        {
            Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, Padding = new Thickness(0, 0, 0, 2),
        };
        sv.ScrollChanged += (_, _) =>
        {
            var room = sv.ScrollableWidth > 0.5 ? 14 : 2;
            if (Math.Abs(sv.Padding.Bottom - room) > 0.1) sv.Padding = new Thickness(0, 0, 0, room);
        };
        sv.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { sv.ScrollToHorizontalOffset(Math.Clamp(sv.HorizontalOffset - e.Delta, 0, sv.ScrollableWidth)); return; }
            var up = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sv };
            (VisualTreeHelper.GetParent(sv) as UIElement)?.RaiseEvent(up);
        };
        return sv;
    }

    /// <summary>A rounded clip that follows an element's size (the Mac's clipShape(RoundedRectangle)).</summary>
    public static void RoundClip(FrameworkElement e, double radius)
    {
        void Clip() => e.Clip = new RectangleGeometry(new Rect(0, 0, e.ActualWidth, e.ActualHeight), radius, radius);
        e.SizeChanged += (_, _) => Clip();
    }

    /// <summary>"1st", "2nd", "3rd", "4th" … "11th".</summary>
    public static string Ordinal(int n)
    {
        var suffix = n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix}";
    }

    /// <summary>White on a deep colour, near-black on a light one (a kind's chip filled in its colour: yellow, cyan).</summary>
    public static Color InkOn(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B > 170 ? Color.FromRgb(0x1b, 0x1b, 0x1b) : Colors.White;
}

/// <summary>
/// The top layer of the window the Periodic Table is in (its popup, Shell/FloatPopup.cs), where its tour and its search's
/// results float over everything: a layer of its own added to the window once, that only its own pieces take presses on.
/// </summary>
internal static class PTLayer
{
    private static readonly ConditionalWeakTable<Window, Canvas> Layers = new();

    public static Canvas? Of(DependencyObject inWindow)
    {
        var w = Window.GetWindow(inWindow);
        if (w == null) return null;
        if (Layers.TryGetValue(w, out var c)) return c;
        if (w.Content is not Panel root) return null;
        c = new Canvas { Focusable = false };
        if (root is Grid g)
        {
            if (g.RowDefinitions.Count > 1) { Grid.SetRow(c, 1); Grid.SetRowSpan(c, g.RowDefinitions.Count - 1); }
            if (g.ColumnDefinitions.Count > 1) Grid.SetColumnSpan(c, g.ColumnDefinitions.Count);
        }
        else if (root is Canvas)
        {
            // (a popup's window holds its card on a canvas, and a canvas gives what it holds no size: the layer is made the
            //  window's size itself, or the tour had no room to measure — its card stuck at the popup's corner over the
            //  heading, nothing dimmed or lit)
            c.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding(nameof(FrameworkElement.ActualWidth)) { Source = root });
            c.SetBinding(FrameworkElement.HeightProperty, new System.Windows.Data.Binding(nameof(FrameworkElement.ActualHeight)) { Source = root });
        }
        Panel.SetZIndex(c, 2000);
        root.Children.Add(c);
        Layers.Add(w, c);
        return c;
    }
}

/// <summary>
/// Children in rows of as many columns as fit, each at least so wide, sharing the width (the Mac's LazyVGrid with adaptive
/// or flexible columns): each row as tall as its tallest.
/// </summary>
internal sealed class PTFlowGrid : Panel
{
    private readonly double minWidth;
    private readonly double spacing;
    private readonly int? columns;
    private readonly List<double> rowHeights = new();
    private int cols = 1;

    /// <param name="minWidth">An adaptive column's least width (ignored when <paramref name="columns"/> is set).</param>
    /// <param name="columns">A fixed number of flexible columns.</param>
    public PTFlowGrid(double minWidth, double spacing, int? columns = null)
    {
        this.minWidth = minWidth;
        this.spacing = spacing;
        this.columns = columns;
    }

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? minWidth * 4 + spacing * 3 : available.Width;
        cols = columns ?? Math.Max(1, (int)Math.Floor((width + spacing) / (minWidth + spacing)));
        var colWidth = Math.Max(0, (width - spacing * (cols - 1)) / cols);
        rowHeights.Clear();
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        for (var i = 0; i < visible.Count; i += cols)
        {
            double h = 0;
            for (var k = i; k < Math.Min(i + cols, visible.Count); k++)
            {
                visible[k].Measure(new Size(colWidth, double.PositiveInfinity));
                h = Math.Max(h, visible[k].DesiredSize.Height);
            }
            rowHeights.Add(h);
        }
        var total = rowHeights.Sum() + spacing * Math.Max(0, rowHeights.Count - 1);
        return new Size(double.IsInfinity(available.Width) ? width : available.Width, total);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var colWidth = Math.Max(0, (final.Width - spacing * (cols - 1)) / cols);
        var visible = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        double y = 0;
        for (var r = 0; r * cols < visible.Count && r < rowHeights.Count; r++)
        {
            for (var k = 0; k < cols && r * cols + k < visible.Count; k++)
                visible[r * cols + k].Arrange(new Rect(k * (colWidth + spacing), y, colWidth, rowHeights[r]));
            y += rowHeights[r] + spacing;
        }
        return final;
    }
}
