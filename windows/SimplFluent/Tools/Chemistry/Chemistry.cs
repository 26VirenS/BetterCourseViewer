using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// The Periodic Table's chemistry builders (the Mac's LewisMaker and MoleculeBuilder, the full Periodic Table's
/// "Lewis structures" and "Molecule builder" pages): as views to put in a page, or opened over the window in a sheet
/// nearly its size, the two builders a choice at its top as the Mac's tool header has them. Not tools of their own: the
/// Periodic Table opens them.
/// </summary>
public static class Chemistry
{
    /// <summary>
    /// The Lewis structure maker. <paramref name="start"/>: a preset by its name or formula ("H₂O", "NO3-", "Benzene"), or
    /// an element for the central atom ("N"); null keeps the molecule it had.
    /// </summary>
    public static FrameworkElement Lewis(ToolContext c, string? start) => new LewisMakerView(start, null);

    /// <summary>
    /// The 3D molecule builder. <paramref name="start"/>: a preset by its name or formula ("Methane", "CH4"), a Lewis
    /// preset with its electrons placed ("XeF4", "NO3-"), or an element with hydrogens filled in ("N"); null keeps the
    /// molecule it had.
    /// </summary>
    public static FrameworkElement Molecule(ToolContext c, string? start) => new MoleculeBuilderView(start);

    /// <summary>The Lewis structure maker over the window, in a sheet nearly its size (Done or Escape closes it).</summary>
    public static void OpenLewis(Window owner, Engine.Engine engine, string? start) => ChemSheet.Show(owner, false, start, engine);

    /// <summary>The 3D molecule builder over the window, in a sheet nearly its size (Done or Escape closes it).</summary>
    public static void OpenMolecule(Window owner, Engine.Engine engine, string? start) => ChemSheet.Show(owner, true, start, engine);

    /// <summary>
    /// The Lewis maker's "Open in 3D builder" when it is not in the builders' own sheet (in a page of the Periodic
    /// Table's, say): handed the Lewis view, with the molecule already in the 3D builder's model. Not set: the 3D
    /// builder opens over the window.
    /// </summary>
    public static Action<FrameworkElement>? OpenIn3D { get; set; }

    /// <summary>
    /// Something done in a builder that the Periodic Table's tour may wait for (the Mac's PTTour.did): "preset", "draw",
    /// "resonance", "turned".
    /// </summary>
    public static event Action<string>? Did;

    internal static void Tell(string what) => Did?.Invoke(what);
}

/// <summary>
/// The builders over the window (the Mac's ToolPopup, with the Periodic Table's page buttons for the two of them): Done,
/// the page's tile, name and line, the choice of builder and — the sheet covering the title bar — the title bar's pins
/// (PinBar in a popup, as a tool's sheet, a quiz and Alta have them); under them the builder, the whole sheet.
/// </summary>
internal static class ChemSheet
{
    private static readonly Color Tone = Color.FromRgb(0x30, 0xd1, 0x58); // (the Periodic Table's tile colour)

    public static void Show(Window? owner, bool molecule, string? start, Engine.Engine? engine = null)
    {
        var win = owner ?? Application.Current?.MainWindow;
        engine ??= (win as MainWindow)?.School ?? (Application.Current?.MainWindow as MainWindow)?.School;
        var w = Math.Max(760, (win?.ActualWidth ?? 1280) - 70);
        var h = Math.Max(560, (win?.ActualHeight ?? 860) - 90);
        var sheet = new Sheet { Bare = true, Width = w, Height = h };
        var host = new ContentControl { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var title = K.Text("", Ty.Title);
        var note = K.Text("", Ty.Body, B.Text2);
        var pages = new SelectorBar(new[] { "Lewis structures", "Molecule builder" }, molecule ? 1 : 0);
        void Page(bool mol, string? from)
        {
            title.Text = mol ? "Molecule builder" : "Lewis structures";
            note.Text = mol ? "Build a molecule and turn it in 3D" : "Draw a molecule's Lewis structure";
            host.Content = mol ? new MoleculeBuilderView(from) : new LewisMakerView(from, () => { pages.Select(1, false); Page(true, null); });
            if (host.Content is FrameworkElement fe) Motion.FadeIn(fe, Motion.Medium);
        }
        pages.Changed += i => Page(i == 1, null);
        var done = K.Button("Done", () => sheet.Close());
        done.ToolTip = "Close (Esc)";
        done.Margin = new Thickness(0, 0, 14, 0);
        done.VerticalAlignment = VerticalAlignment.Center;
        var words = K.V(2, title, note);
        words.VerticalAlignment = VerticalAlignment.Center;
        pages.VerticalAlignment = VerticalAlignment.Center;
        pages.Margin = new Thickness(12, 0, 0, 0);
        var head = K.Columns("auto, auto, *, auto, auto", done.Col(0), K.IconTile(SymbolRegular.Molecule24, Tone, 44).Margin(0, 0, 14, 0).Col(1), words.Col(2), pages.Col(3));
        if (engine != null) head.Children.Add(new PinBar(engine, inPopup: true).Margin(12, 0, 0, 0).Col(4));
        head.Margin = new Thickness(40, 20, 40, 12);
        var g = K.Rows("auto, *", head.Row(0), host.Row(1));
        g.Bg(B.Window);
        sheet.Body = g;
        Page(molecule, start);
        Dialogs.Show(sheet, win);
        // (a sheet starts held to 900 wide until its host is laid out again: this one as wide as it was made)
        if (sheet.Card is { } card && win != null) card.MaxWidth = Math.Max(card.MaxWidth, win.ActualWidth - 48);
    }
}

/// <summary>
/// The Lewis maker's and the molecule builder's layout (the Mac's PTWorkLayout): on a wide window, the controls, the
/// drawing and the facts side by side, each side scrolling on its own; on a narrower one, the drawing across the top —
/// about half the height, so the controls under it start in view (and the builder puts its preset and its way of making
/// in the drawing's own heading: <see cref="WideChanged"/>) — and the controls and facts side by side under it, all
/// scrolling as one. The biology tools (<c>tall</c>) give their drawing the page's whole height there, as the Mac's do
/// (PTWorkLayout without shortDrawing): a 3D model or a step-through squeezed into half a page lost its parts.
/// </summary>
internal sealed class ChemLayout : Grid
{
    private readonly FrameworkElement left, centre, right;
    private readonly bool tall;
    private readonly ScrollViewer leftScroll = Scroller(), rightScroll = Scroller(), oneScroll = Scroller();
    private readonly Grid narrow = new();
    private readonly Grid under = new();
    private bool? wide;

    /// <summary>The three side by side (true), or the drawing over the rest (false); not laid out yet: true.</summary>
    public bool Wide => wide != false;

    /// <summary>The layout changed between side by side and one over the other.</summary>
    public event Action? WideChanged;

    /// <summary>The drawing's height over the rest: about half the page, never so small the molecule is lost — or, tall,
    /// as tall as the page shows, from 460 to 640.</summary>
    private double NarrowHeight(double page) => tall ? Math.Max(460, Math.Min(640, page)) : Math.Max(Floor, Math.Min(Math.Max(560, Floor), page * 0.5));

    /// <summary>The least the drawing is given over the rest (320): more while the drawing's card holds more than the
    /// drawing — the Lewis maker's resonance structures under it, which left the structure itself a few lines tall.</summary>
    public double Floor
    {
        get => floor;
        set
        {
            if (Math.Abs(floor - value) < 0.5) return;
            floor = value;
            if (wide == false) centre.Height = NarrowHeight(page);
        }
    }

    private double floor = 320, page;

    public ChemLayout(FrameworkElement left, FrameworkElement centre, FrameworkElement right, bool tall = false)
    {
        this.left = left;
        this.centre = centre;
        this.right = right;
        this.tall = tall;
        Margin = Edges(true);
        MaxWidth = 1480;
        left.VerticalAlignment = VerticalAlignment.Top;
        right.VerticalAlignment = VerticalAlignment.Top;
        narrow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        narrow.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        narrow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        under.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        under.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        under.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(under, 2);
        narrow.Children.Add(under);
        narrow.Margin = new Thickness(0, 0, 0, 24);
        oneScroll.Content = narrow;
        SizeChanged += (_, e) => Lay(e.NewSize);
    }

    /// <summary>
    /// In a page that scrolls (no height to fill), the drawing side by side with the panels is given the Mac's narrow
    /// height rather than none.
    /// </summary>
    protected override Size MeasureOverride(Size available)
    {
        if (wide == true) centre.Height = double.IsInfinity(available.Height) ? 640 : double.NaN;
        return base.MeasureOverride(available);
    }

    private static ScrollViewer Scroller() => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
        HorizontalContentAlignment = HorizontalAlignment.Left,
    };

    /// <summary>
    /// Room round the page: side by side, all of it round the three columns; one over the other, the sides' room inside
    /// the scroller (its padding), so its scroll bar runs down the page's edge, not inside the drawing's card (the
    /// chemistry pages; the biology tools' pages keep theirs as they are).
    /// </summary>
    private Thickness Edges(bool wide) => wide || tall ? new Thickness(40, 8, 40, 24) : new Thickness(0, 8, 0, 0);

    private void Lay(Size size)
    {
        // (decided on the page's own width and height, the room round it put back: the room moves as the layout does)
        var width = size.Width + Margin.Left + Margin.Right;
        page = size.Height + Margin.Top + Margin.Bottom - 32;
        var w = width >= 1180;
        if (!w) centre.Height = NarrowHeight(page);
        if (wide == w) return;
        var first = wide == null;
        wide = w;
        Margin = Edges(w);
        oneScroll.Padding = w || tall ? new Thickness(0) : new Thickness(40, 0, 40, 0);
        // (told after this layout pass: the builders move their preset and way into the drawing's heading, or back)
        if (!first || !w) Dispatcher.BeginInvoke(() => WideChanged?.Invoke(), DispatcherPriority.Loaded);
        foreach (var e in new[] { left, centre, right }) ChemKit.Detach(e);
        Children.Clear();
        ColumnDefinitions.Clear();
        leftScroll.Content = null;
        rightScroll.Content = null;
        if (w)
        {
            // (each side column's content held to the column's width, less room for its scroll bar)
            centre.Height = double.NaN;
            left.Width = 284;
            right.Width = 300;
            left.Margin = new Thickness(0, 0, 0, 20);
            right.Margin = new Thickness(0, 0, 0, 20);
            left.HorizontalAlignment = HorizontalAlignment.Left;
            right.HorizontalAlignment = HorizontalAlignment.Left;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(316) });
            leftScroll.Content = left;
            rightScroll.Content = right;
            Children.Add(leftScroll.Col(0));
            Children.Add(centre.Col(2));
            Children.Add(rightScroll.Col(4));
        }
        else
        {
            left.Width = double.NaN;
            right.Width = double.NaN;
            left.Margin = new Thickness(0);
            right.Margin = new Thickness(0);
            left.HorizontalAlignment = HorizontalAlignment.Stretch;
            right.HorizontalAlignment = HorizontalAlignment.Stretch;
            centre.Height = NarrowHeight(page);
            Grid.SetColumn(centre, 0);
            Grid.SetRow(centre, 0);
            narrow.Children.Add(centre);
            under.Children.Add(left.Col(0));
            under.Children.Add(right.Col(2));
            Children.Add(oneScroll);
        }
    }
}

/// <summary>What the two builders are made of, so they look one family.</summary>
internal static class ChemKit
{
    public static Color Green => K.Tone("green");
    public static Color Orange => K.Tone("orange");
    public static Color Yellow => K.Tone("yellow");
    public static Color Red => K.Tone("red");

    /// <summary>A heading over a group of controls.</summary>
    public static TextBlock Heading(string t) => K.Text(t, Ty.BodyStrong);

    /// <summary>A small line that says what something does.</summary>
    public static TextBlock Caption(string t) => K.Wrapped(t, Ty.Caption, B.Text2);

    /// <summary>Words in a colour (an element's, a tone's), or in the text colour for none; a tone made to read as words.</summary>
    public static TextBlock Paint(TextBlock t, Color? c, bool tone = false)
    {
        if (c is { } x) t.Foreground = new SolidColorBrush(tone ? K.Darkened(x) : x);
        else t.Fg(B.Text);
        return t;
    }

    /// <summary>A dot of a colour, or of the text colour for none.</summary>
    public static Ellipse Dot(Color? c, double size)
    {
        var e = new Ellipse { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        if (c is { } x) e.Fill = new SolidColorBrush(x); else e.SetResourceReference(Shape.FillProperty, B.Text);
        return e;
    }

    /// <summary>Content on a faint wash of a theme colour (the accent's for one chosen, the text's for the rest).</summary>
    public static Grid Tint(UIElement content, string brush, double opacity, double corner)
    {
        var wash = new Border { CornerRadius = new CornerRadius(corner), Opacity = opacity, IsHitTestVisible = false };
        wash.SetResourceReference(Border.BackgroundProperty, brush);
        return new Grid { Children = { wash, content } };
    }

    /// <summary>An element let go of by whatever holds it, to be put somewhere else.</summary>
    public static void Detach(FrameworkElement e)
    {
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
        }
    }

    /// <summary>The facts' card: a row each, its name at the left and its value at the right in its colour, hairlines between.</summary>
    public static Border Facts(IEnumerable<(string Name, string Value, Color? Tone)> rows)
    {
        var list = rows.ToList();
        var v = K.V(0);
        for (var i = 0; i < list.Count; i++)
        {
            var value = Paint(K.Text(list[i].Value, Ty.BodyStrong), list[i].Tone, tone: true);
            value.TextAlignment = TextAlignment.Right;
            value.TextWrapping = TextWrapping.Wrap;
            var row = K.Columns("auto, *", K.Text(list[i].Name, Ty.Body, B.Text2).Margin(0, 0, 12, 0).Col(0), value.Col(1));
            row.Margin = new Thickness(14, 9, 14, 9);
            v.Children.Add(row);
            if (i < list.Count - 1) v.Children.Add(K.Divider(14));
        }
        return K.Card(v, new Thickness(0));
    }

    /// <summary>A small card of a tool's side panel.</summary>
    public static Border Card(UIElement content) => K.Card(content, new Thickness(14));

    /// <summary>A line with its symbol: a note (the secondary colour) or a warning (in its tone).</summary>
    public static FrameworkElement Line(SymbolRegular symbol, string text, Color? tone)
    {
        var icon = tone is { } t ? K.Icon(symbol, 14, t, filled: true) : K.Icon(symbol, 14, B.Text2);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 1, 8, 0);
        var words = K.Wrapped(text, Ty.Caption, B.Text2);
        if (tone is { } tt) words.Foreground = new SolidColorBrush(K.Darkened(tt));
        return K.Columns("auto, *", icon.Col(0), words.Col(1));
    }

    /// <summary>A button as wide as its column, a touch taller (the Mac's large control size).</summary>
    public static Wpf.Ui.Controls.Button Wide(string text, Action click, bool accent = false, SymbolRegular? icon = null)
    {
        var b = K.Button(text, click, accent, icon);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.MinHeight = 36;
        return b;
    }

    /// <summary>A button that opens a menu under it (Windows 11's drop-down button): what it shows, and a chevron.</summary>
    public static Wpf.Ui.Controls.Button DropDown(UIElement face, Action<ContextMenu> fill, string tip)
    {
        var chevron = K.Icon(SymbolRegular.ChevronDown16, 12, B.Text2);
        chevron.Margin = new Thickness(8, 0, 0, 0);
        var g = K.Columns("*, auto", face.Col(0), chevron.Col(1));
        var b = new Wpf.Ui.Controls.Button
        {
            Content = g, Appearance = ControlAppearance.Secondary, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 34, Padding = new Thickness(12, 5, 10, 6), ToolTip = tip,
        };
        b.Click += (_, e) =>
        {
            e.Handled = true;
            var m = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom, MinWidth = Math.Max(200, b.ActualWidth) };
            fill(m);
            m.IsOpen = true;
        };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        return b;
    }

    /// <summary>
    /// The presets in named groups, showing the one loaded (or "Your own"): each group a submenu of its presets, the
    /// loaded one ticked (the Mac's PresetMenu).
    /// </summary>
    public static FrameworkElement PresetMenu(IReadOnlyList<(string Title, IReadOnlyList<string> Names)> sections, string? selected, Action<string> pick)
    {
        var face = K.Text(selected ?? "Your own", Ty.BodyStrong);
        return DropDown(face, m =>
        {
            foreach (var (title, names) in sections)
            {
                var group = new MenuItem { Header = title };
                foreach (var name in names)
                {
                    var item = new MenuItem { Header = name, IsChecked = name == selected };
                    var n = name;
                    item.Click += (_, _) => pick(n);
                    group.Items.Add(item);
                }
                m.Items.Add(group);
            }
        }, "Choose a preset");
    }

    /// <summary>The palette's columns as groups: 1, 2, then 13 to 18.</summary>
    public static readonly IReadOnlyList<(string Title, List<string> Symbols)> Groups = MakeGroups();

    private static List<(string, List<string>)> MakeGroups()
    {
        var names = new[] { "Group 1", "Group 2", "Group 13", "Group 14", "Group 15", "Group 16", "Group 17", "Group 18" };
        var outs = new List<(string, List<string>)>();
        for (var col = 0; col < names.Length; col++)
        {
            var members = new List<string>();
            foreach (var row in LChem.Palette)
                if (col < row.Length && row[col] is { } s) members.Add(s);
            outs.Add((names[col], members));
        }
        return outs;
    }

    /// <summary>
    /// An element shown as a card (its symbol on a tile of its colour, its name, its valence electrons and how many it can
    /// hold) that opens a menu of the main-group elements, group by group, to change it (the Mac's LElementPicker).
    /// </summary>
    public static FrameworkElement ElementPicker(string selected, Action<string> pick)
    {
        var colour = LChem.ColorOf(selected);
        var symbol = Paint(new TextBlock { Text = selected, FontSize = 20, FontWeight = FontWeights.Bold, FontFamily = K.DisplayFace, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, colour);
        FrameworkElement tile;
        if (colour is { } c) tile = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(K.WithAlpha(c, 0.16)), Child = symbol };
        else tile = new Border { Width = 44, Height = 44, Child = Tint(symbol, B.Text, 0.12, 8) };
        var words = K.V(2, K.Text(LChem.Name(selected), Ty.BodyStrong), K.Text($"{LChem.Valence(selected)} valence e⁻ · max {LChem.MaxElectrons(selected)}", Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var chevron = K.Icon(SymbolRegular.ChevronDown16, 12, B.Text2);
        var face = K.Columns("auto, *, auto", tile.Margin(0, 0, 12, 0).Col(0), words.Col(1), chevron.Margin(4, 0, 2, 0).Col(2));
        Button? b = null;
        b = K.CardButton(face, () =>
        {
            var m = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom, MinWidth = Math.Max(200, b!.ActualWidth) };
            foreach (var (title, symbols) in Groups)
            {
                var group = new MenuItem { Header = title };
                foreach (var s in symbols)
                {
                    var item = new MenuItem { Header = $"{s}  {LChem.Name(s)}", IsChecked = s == selected };
                    var sym = s;
                    item.Click += (_, _) => pick(sym);
                    group.Items.Add(item);
                }
                m.Items.Add(group);
            }
            m.IsOpen = true;
        }, 8, new Thickness(10));
        b.ToolTip = "Change the element";
        System.Windows.Automation.AutomationProperties.SetName(b, $"Element: {LChem.Name(selected)}");
        return b;
    }

    /// <summary>A number with − and + either side, each pressable over its whole square.</summary>
    public static FrameworkElement Stepper(int value, int lo, int hi, Action<int> set, string less, string more)
    {
        Button Step(SymbolRegular s, bool enabled, int to, string tip)
        {
            var b = K.Subtle(K.Icon(s, 12, B.Text), () => set(to), new Thickness(0), 4);
            b.Width = 24;
            b.Height = 24;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.SetResourceReference(Control.BackgroundProperty, B.Subtle);
            b.IsEnabled = enabled;
            b.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            return b;
        }
        var shown = K.Text(value > 0 && lo < 0 ? $"+{value}" : $"{value}", Ty.BodyStrong).Center();
        shown.Width = 24;
        shown.VerticalAlignment = VerticalAlignment.Center;
        shown.FontFeatures();
        var row = K.H(4, Step(SymbolRegular.Subtract16, value > lo, Math.Max(lo, value - 1), less), shown, Step(SymbolRegular.Add16, value < hi, Math.Min(hi, value + 1), more));
        row.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }
}

/// <summary>
/// The screenshot suite's builders: --lewis "NO3-" or --molecule CH4 opens that builder over the window, with that
/// preset, once the app has come up. Hooked only when one of them is given.
/// </summary>
internal static class ChemistryLaunch
{
#pragma warning disable CA2255
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Hook()
#pragma warning restore CA2255
    {
        var args = Environment.GetCommandLineArgs();
        if (!args.Any(a => a.StartsWith("--lewis", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--molecule", StringComparison.OrdinalIgnoreCase))) return;
        // (the application is not made yet: waited for off the UI thread, then the rest is done on it)
        wait = new System.Threading.Timer(_ =>
        {
            if (Application.Current is not { } app || System.Threading.Interlocked.Exchange(ref handed, 1) == 1) return;
            wait?.Dispose();
            app.Dispatcher.BeginInvoke(Watch);
        }, null, 200, 200);
    }

    private static System.Threading.Timer? wait;
    private static int handed;

    private static void Watch()
    {
        var began = DateTime.UtcNow;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        t.Tick += (_, _) =>
        {
            if (Application.Current?.MainWindow is not MainWindow win || !win.IsLoaded) return;
            // (once the app shows — the sign-in done — or after a while whatever shows)
            var up = Find<AppShell>(win) != null;
            if (!up && (DateTime.UtcNow - began).TotalSeconds < 12) return;
            t.Stop();
            var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(up ? 1.5 : 0.1) };
            later.Tick += (_, _) =>
            {
                later.Stop();
                if (Launch.Take("", "molecule") is { Length: > 0 } m) ChemSheet.Show(win, true, m);
                else if (Launch.Take("", "lewis") is { Length: > 0 } l) ChemSheet.Show(win, false, l);
            };
            later.Start();
        };
        t.Start();
    }

    private static T? Find<T>(DependencyObject d) where T : DependencyObject
    {
        if (d is T found) return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (Find<T>(VisualTreeHelper.GetChild(d, i)) is { } f) return f;
        return null;
    }
}
