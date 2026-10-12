using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Kit;

public enum SheetButtonKind { Primary, Secondary, Close }

/// <summary>A button at a sheet's foot: its words, what it does, and whether the sheet closes after.</summary>
public sealed class SheetButton
{
    public string Text { get; set; }
    public SheetButtonKind Kind { get; set; }
    public Action? Click { get; set; }
    public bool Closes { get; set; } = true;
    internal Wpf.Ui.Controls.Button? Control;

    public SheetButton(string text, SheetButtonKind kind, Action? click = null, bool closes = true)
    {
        Text = text;
        Kind = kind;
        Click = click;
        Closes = closes;
    }

    public bool Enabled
    {
        get => Control?.IsEnabled ?? true;
        set { if (Control != null) Control.IsEnabled = value; }
    }

    public void Relabel(string text)
    {
        Text = text;
        if (Control != null) Control.Content = text;
    }
}

/// <summary>
/// What a sheet shows (Windows 11's ContentDialog, as the Mac's sheets and alerts are): a title, what it is about, and
/// its buttons at the foot — the accent one the default (Enter), Close or Cancel the way out (Escape).
/// </summary>
public class Sheet
{
    public string? Title { get; set; }
    public FrameworkElement Body { get; set; } = new Grid();
    public List<SheetButton> Buttons { get; } = new();
    public double Width { get; set; } = 448;
    public double? Height { get; set; }
    /// <summary>The body fills the sheet edge to edge, its own padding its own (a step of the setup, What's New).</summary>
    public bool Bare { get; set; }
    /// <summary>Escape and a click outside do nothing (a step that must be answered).</summary>
    public bool Modal { get; set; }
    public event Action? Closed;
    internal SheetHost? Host;
    internal Border? Card;

    public void Close() => Host?.Close(this);

    internal void RaiseClosed() => Closed?.Invoke();

    public SheetButton Add(string text, SheetButtonKind kind, Action? click = null, bool closes = true)
    {
        var b = new SheetButton(text, kind, click, closes);
        Buttons.Add(b);
        return b;
    }
}

/// <summary>
/// Where a window's sheets show: over everything in it, the window dimmed behind (Windows' smoke), one sheet at a time
/// (another waits its turn). Arrives as WinUI's dialog does — from a touch larger, fading in — and leaves the same way.
/// </summary>
public sealed class SheetHost : Grid
{
    private readonly Border smoke = new();
    private readonly Queue<Sheet> waiting = new();
    private Sheet? current;
    private IInputElement? focusBefore;

    /// <summary>A sheet came or went (the window's web view, a window of its own, steps aside meanwhile).</summary>
    public event Action<bool>? OpenChanged;

    public bool IsOpen => current != null;

    /// <summary>A point on the screen (device pixels) is on the open sheet's card — the window's title bar lets it be
    /// the card's (a large sheet reaches up into the title bar's band).</summary>
    public bool Covers(Point screen)
    {
        if (current?.Card is not { IsLoaded: true } card || PresentationSource.FromVisual(card) == null) return false;
        try
        {
            var p = card.PointFromScreen(screen);
            return p.X >= 0 && p.Y >= 0 && p.X <= card.ActualWidth && p.Y <= card.ActualHeight;
        }
        catch (InvalidOperationException) { return false; }
    }
    public Sheet? Current => current;

    public SheetHost()
    {
        Visibility = Visibility.Collapsed;
        smoke.Background = new SolidColorBrush(Color.FromArgb(Support.Appearance.Dark ? (byte)140 : (byte)102, 0, 0, 0));
        smoke.MouseLeftButtonDown += (_, e) => { e.Handled = true; };
        Children.Add(smoke);
        Panel.SetZIndex(this, 1000);
        PreviewKeyDown += OnKey;
        SizeChanged += (_, _) => { if (current?.Card is { } c) Fit(c); };
    }

    public void Show(Sheet s)
    {
        if (current != null) { waiting.Enqueue(s); return; }
        current = s;
        s.Host = this;
        var card = Build(s);
        s.Card = card;
        focusBefore = Keyboard.FocusedElement;
        Children.Add(card);
        Visibility = Visibility.Visible;
        smoke.Background = new SolidColorBrush(Color.FromArgb(Support.Appearance.Dark ? (byte)140 : (byte)102, 0, 0, 0));
        Motion.FadeIn(smoke, Motion.Normal);
        Motion.Pop(card);
        OpenChanged?.Invoke(true);
        Support.Shot.Moved();
        Dispatcher.BeginInvoke(() =>
        {
            // the first thing to type in, else the default button
            var first = FirstFocusable(s.Body) ?? (IInputElement?)s.Buttons.FirstOrDefault(b => b.Kind == SheetButtonKind.Primary)?.Control;
            if (first is UIElement u) { u.Focus(); Keyboard.Focus(first); }
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    public void Close(Sheet s)
    {
        if (current != s) { if (waiting.Contains(s)) { var rest = waiting.Where(w => w != s).ToList(); waiting.Clear(); foreach (var w in rest) waiting.Enqueue(w); } return; }
        current = null;
        var card = s.Card;
        void Gone()
        {
            if (card != null) Children.Remove(card);
            s.RaiseClosed();
            if (waiting.Count > 0) { Show(waiting.Dequeue()); return; }
            if (current == null)
            {
                Visibility = Visibility.Collapsed;
                OpenChanged?.Invoke(false);
                if (focusBefore is UIElement f && f.IsVisible) f.Focus();
            }
        }
        if (waiting.Count > 0 || card == null) { Gone(); return; }
        Motion.FadeOut(smoke, () => { }, Motion.Normal);
        Motion.Unpop(card, Gone);
    }

    private static bool OpenPopupAround(DependencyObject f)
    {
        for (var p = f; p != null; p = VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p))
        {
            if (p is System.Windows.Controls.ComboBox { IsDropDownOpen: true }) return true;
            if (p is System.Windows.Controls.ContextMenu { IsOpen: true } or System.Windows.Controls.MenuItem { IsSubmenuOpen: true }) return true;
            if (p is System.Windows.Controls.Primitives.Popup { IsOpen: true }) return true;
            if (p is SheetHost) return false;
        }
        return false;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (current == null) return;
        if (e.Key == Key.Escape && !current.Modal)
        {
            // (Escape in an open drop-down or menu inside the sheet closes that, not the sheet)
            if (Keyboard.FocusedElement is DependencyObject f && OpenPopupAround(f)) return;
            var cancel = current.Buttons.LastOrDefault(b => b.Kind == SheetButtonKind.Close);
            if (cancel != null) Press(current, cancel); else Close(current);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not System.Windows.Controls.TextBox { AcceptsReturn: true })
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.Button) return; // (a focused button presses itself)
            var primary = current.Buttons.FirstOrDefault(b => b.Kind == SheetButtonKind.Primary);
            if (primary?.Control is { IsEnabled: true }) { Press(current, primary); e.Handled = true; }
        }
    }

    private void Press(Sheet s, SheetButton b)
    {
        b.Click?.Invoke();
        if (b.Closes) Close(s);
    }

    private Border Build(Sheet s)
    {
        var top = new Stack(Orientation.Vertical, 12, Array.Empty<UIElement>());
        if (!string.IsNullOrEmpty(s.Title))
        {
            var title = K.Text(s.Title!, Ty.Subtitle, B.Text, TextWrapping.Wrap);
            title.TextTrimming = TextTrimming.None;
            top.Children.Add(title);
        }
        if (!s.Bare) top.Children.Add(s.Body);
        var content = new Border { Child = s.Bare ? s.Body : top, Padding = s.Bare ? new Thickness(0) : new Thickness(24, 24, 24, 24) };
        content.SetResourceReference(Border.BackgroundProperty, "ContentDialogTopOverlay");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(content);
        if (s.Buttons.Count > 0)
        {
            var row = new Grid { Margin = new Thickness(24) };
            for (var i = 0; i < s.Buttons.Count; i++)
            {
                if (i > 0) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            if (s.Buttons.Count == 1) row.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var col = s.Buttons.Count == 1 ? 1 : 0;
            foreach (var b in s.Buttons)
            {
                var btn = new Wpf.Ui.Controls.Button
                {
                    Content = b.Text, HorizontalAlignment = HorizontalAlignment.Stretch,
                    Appearance = b.Kind == SheetButtonKind.Primary ? ControlAppearance.Primary : ControlAppearance.Secondary,
                };
                b.Control = btn;
                var bb = b;
                btn.Click += (_, _) => Press(s, bb);
                Grid.SetColumn(btn, col);
                col += 2;
                row.Children.Add(btn);
            }
            var foot = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0) };
            foot.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
            foot.SetResourceReference(Border.BorderBrushProperty, "ContentDialogSeparatorBorderBrush");
            Grid.SetRow(foot, 1);
            grid.Children.Add(foot);
        }
        var card = new Border
        {
            Child = grid, Width = s.Width, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = K.Shadow(64, 24, 0.32), SnapsToDevicePixels = true, ClipToBounds = false,
        };
        if (s.Height is { } h) card.Height = h;
        card.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
        card.SetResourceReference(Border.BorderBrushProperty, "ContentDialogBorderBrush");
        // (the corners clip what is inside them: the top's overlay and the foot follow the card's rounding)
        grid.Clip = null;
        grid.SizeChanged += (_, e) => grid.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(card, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(card, KeyboardNavigationMode.Cycle);
        card.MouseLeftButtonDown += (_, e) => e.Handled = true;
        Fit(card); // (kept inside a small window: see the host's SizeChanged)
        return card;
    }

    /// <summary>A card kept inside the window, 24 from each edge.</summary>
    private void Fit(Border card)
    {
        card.MaxHeight = Math.Max(200, ActualHeight > 0 ? ActualHeight - 48 : 2000);
        card.MaxWidth = Math.Max(280, ActualWidth > 0 ? ActualWidth - 48 : 2000);
    }

    private static IInputElement? FirstFocusable(DependencyObject root)
    {
        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) return null; // (a document, a run: nothing to focus)
        if (root is System.Windows.Controls.TextBox or System.Windows.Controls.PasswordBox or Wpf.Ui.Controls.TextBox or Wpf.Ui.Controls.PasswordBox)
            return ((UIElement)root).IsEnabled && ((UIElement)root).IsVisible ? (IInputElement)root : null;
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
            if (FirstFocusable(VisualTreeHelper.GetChild(root, i)) is { } f) return f;
        if (root is FrameworkElement fe && n == 0)
            foreach (var c in LogicalTreeHelper.GetChildren(fe).OfType<DependencyObject>())
                if (FirstFocusable(c) is { } f2) return f2;
        return null;
    }
}
