using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// (0.3.28, the Mac's PTMini) An element pressed in the Periodic Table — the whole tool's or its pin's — opens in a small
/// window of its own beside the table: its square, name and kind, its electrons, the numbers most asked for, a line about
/// it, and the ways on (its full page, Atoms &amp; Orbitals, the elements either side). One such window: an element pressed
/// while it is open shows in it. ← and → step through the elements; Escape closes it.
/// </summary>
internal sealed class PTMiniWindow : FluentWindow
{
    private static PTMiniWindow? open;

    private readonly ContentControl body = new() { Focusable = false };
    private PTElement element;
    private Action<PTElement> openFull;
    private Action<PTElement> openAtoms;

    /// <summary>The element shown: in the open window, or a new one beside <paramref name="owner"/>.</summary>
    public static void Show(PTElement e, Window? owner, Action<PTElement> openFull, Action<PTElement> openAtoms)
    {
        if (open != null)
        {
            open.openFull = openFull;
            open.openAtoms = openAtoms;
            open.Set(e);
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        open = new PTMiniWindow(e, owner, openFull, openAtoms);
        open.Show();
    }

    /// <summary>The window put away (the table it came from has gone).</summary>
    public static void CloseOpen() => open?.Close();

    private PTMiniWindow(PTElement e, Window? owner, Action<PTElement> openFull, Action<PTElement> openAtoms)
    {
        element = e;
        this.openFull = openFull;
        this.openAtoms = openAtoms;
        if (owner != null && owner.IsVisible) Owner = owner;
        Title = e.Name;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png"));
        WindowBackdropType = Appearance.Backdrop;
        ExtendsContentIntoTitleBar = true;
        WindowCornerPreference = WindowCornerPreference.Round;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = K.TextFace;
        SetResourceReference(ForegroundProperty, B.Text);
        if (Fx.Opaque) SetResourceReference(BackgroundProperty, B.Window);
        Width = 360;
        Height = 500;
        Place(owner);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(new TitleBar { Title = "Element", Height = 40, ShowMaximize = false, ShowMinimize = false, Margin = new Thickness(8, 0, 0, 0) });
        root.Children.Add(body.Margin(18, 0, 18, 18).Row(1));
        Content = root;
        PreviewKeyDown += (_, k) =>
        {
            if (k.Key == Key.Escape) { Close(); k.Handled = true; }
            else if (k.Key == Key.Left && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase) { Step(-1); k.Handled = true; }
            else if (k.Key == Key.Right && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase) { Step(1); k.Handled = true; }
        };
        Appearance.Changed += OnAppearance;
        Closed += (_, _) => { Appearance.Changed -= OnAppearance; open = null; };
        Set(e);
    }

    private void OnAppearance() => Dispatcher.BeginInvoke(() => Set(element));

    /// <summary>Beside the table's window — to its right where the screen has room, else inside its right edge.</summary>
    private void Place(Window? owner)
    {
        var work = SystemParameters.WorkArea;
        if (owner == null || !owner.IsVisible) { WindowStartupLocation = WindowStartupLocation.CenterScreen; return; }
        var left = owner.Left + owner.ActualWidth + 12;
        if (left + Width > work.Right) left = owner.Left + owner.ActualWidth - Width - 24;
        Left = Math.Max(work.Left, left);
        Top = Math.Clamp(owner.Top + 72, work.Top, Math.Max(work.Top, work.Bottom - Height));
    }

    private void Step(int d)
    {
        if (PeriodicTable.ByNumber(element.Number + d) is { } next) Set(next);
    }

    private void Set(PTElement e)
    {
        element = e;
        Title = e.Name;
        var color = e.Kind.Color;
        var ink = PTKit.InkOn(color);

        var prev = K.IconButton(SymbolRegular.ChevronLeft24, () => Step(-1), "The element before (←)", 14, 30);
        var next = K.IconButton(SymbolRegular.ChevronRight24, () => Step(1), "The element after (→)", 14, 30);
        prev.IsEnabled = e.Number > 1;
        next.IsEnabled = e.Number < PeriodicTable.Elements.Count;

        var num = PTKit.Text(e.Number.ToString(CultureInfo.InvariantCulture), 12, FontWeights.SemiBold, ink).Tabular();
        num.HorizontalAlignment = HorizontalAlignment.Left;
        var sym = PTKit.Fit(PTKit.Text(e.Symbol, 34, FontWeights.Bold, ink));
        sym.VerticalAlignment = VerticalAlignment.Center;
        var mass = PTKit.Text(e.MassText, 10.5, FontWeights.Normal, ink).Tabular();
        mass.HorizontalAlignment = HorizontalAlignment.Center;
        var tile = new Border
        {
            Width = 84, Height = 84, CornerRadius = new CornerRadius(12), Padding = new Thickness(8),
            Background = new SolidColorBrush(color), Child = K.Rows("auto, *, auto", num.Row(0), sym.Row(1), mass.Row(2)),
        };
        var name = PTKit.Fit(PTKit.Text(e.Name, 24, FontWeights.SemiBold, B.Text, K.DisplayFace), HorizontalAlignment.Left);
        var kind = PTKit.Text(e.Kind.Label, 14, FontWeights.SemiBold, PTKit.TextIn(color));
        var place = K.Text($"Group {(e.Group is { } g ? g.ToString(CultureInfo.InvariantCulture) : "—")} · Period {e.Period} · {e.Block}-block", Ty.Caption, B.Text2);
        var head = K.Columns("auto, *", tile.Col(0), K.V(2, name, kind, place).Margin(14, 0, 0, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1));

        var config = PTKit.Fit(PTKit.Text(PTConfig.Pretty(e), 12.5, FontWeights.Normal, B.Text, PTKit.Mono), HorizontalAlignment.Left);

        var facts = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        foreach (var (k, v) in new[]
                 {
                     ("State", e.StateWord),
                     ("Electroneg.", e.En is { } en ? PTElement.Trim(en) : "—"),
                     ("Density", e.DensityText),
                     ("Melts", PTElement.Celsius(e.Melt)),
                     ("Boils", PTElement.Celsius(e.Boil)),
                     ("Valence e⁻", e.ValenceElectrons is { } ve ? ve.ToString(CultureInfo.InvariantCulture) : "—"),
                 })
        {
            var value = PTKit.Fit(PTKit.Text(v, 13.5, FontWeights.SemiBold, B.Text), HorizontalAlignment.Left);
            var box = new Border { Child = K.V(2, K.Text(k, Ty.Caption, B.Text2), value), Padding = new Thickness(8), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 6) };
            box.Bg(B.Subtle);
            facts.Children.Add(box);
        }

        var about = K.Wrapped(e.Summary.Length > 0 ? e.Summary : e.Found is { Length: > 0 } f ? $"Discovered by {f}." : "", Ty.Caption, B.Text2, maxLines: 4);
        about.MinHeight = 58;

        var full = K.Button("Full page", () => { var x = element; Close(); openFull(x); }, accent: true, icon: SymbolRegular.ArrowMaximize24);
        full.HorizontalAlignment = HorizontalAlignment.Stretch;
        full.ToolTip = $"{e.Name}'s whole page in the table";
        var atoms = K.Button("Atom in 3D", () => { var x = element; Close(); openAtoms(x); }, icon: SymbolRegular.Cube24);
        atoms.HorizontalAlignment = HorizontalAlignment.Stretch;
        atoms.ToolTip = $"{e.Name}'s atom, orbitals and electron cloud, in Atoms & Orbitals";
        var buttons = K.Columns("*, 8, *", full.Col(0), atoms.Col(2));

        var content = K.Rows("auto, auto, auto, auto, auto, *, auto",
            K.Columns("*, auto", new Border().Col(0), K.H(4, prev, next).Col(1)).Row(0),
            head.Margin(0, 0, 0, 12).Row(1),
            config.Margin(0, 0, 0, 10).Row(2),
            facts.Row(3),
            about.Margin(2, 4, 0, 0).Row(4),
            buttons.Margin(0, 12, 0, 0).Row(6));
        body.Content = content;
        if (IsLoaded) Motion.FadeIn(content);
        System.Windows.Automation.AutomationProperties.SetName(this, $"{e.Name}, element {e.Number}");
    }
}
