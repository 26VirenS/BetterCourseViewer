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
    /// <summary>The primary button in the critical colour: what it does cannot be undone (Uninstall). Not the default —
    /// Enter does not press it and Cancel has the focus when the sheet opens; it takes a click of its own.</summary>
    public bool Danger { get; set; }
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

    private bool visible = true;
    /// <summary>(0.3.50) Shown at the sheet's foot, or not — and not taking a place there while it is not (its column and
    /// the gap before it folded away, the other buttons sharing the row).</summary>
    public bool Visible
    {
        get => visible;
        set { visible = value; Apply(); }
    }

    internal void Apply()
    {
        if (Control == null) return;
        Control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (Control.Parent is not Grid row) return;
        var c = Grid.GetColumn(Control);
        if (c < row.ColumnDefinitions.Count) row.ColumnDefinitions[c].Width = visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (c > 0 && c - 1 < row.ColumnDefinitions.Count) row.ColumnDefinitions[c - 1].Width = new GridLength(visible ? 8 : 0);
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
    /// <summary>An update waits while it is open (Support/Updates.cs), and so does the live check: every sheet but those
    /// only there to be read when the student likes (What's New, History, Help), which would otherwise hold up every
    /// update for a Simpl kept in the notification area, and every check of the screen behind them.</summary>
    public bool HoldsUpdates { get; set; } = true;
    /// <summary>Used beside the page, and open all day if the student likes (a tool's popup): the live check does not
    /// wait for it as it waits for the other popups (Shell/LiveCheck.cs).</summary>
    public bool Beside { get; set; }

    /// <summary>
    /// A popup that floats over the window without holding it (Shell/FloatPopup.cs): nothing dimmed, the pane, the title
    /// bar and the page behind it still at hand, there until it is closed. Every popup is one but a quiz being taken, the
    /// first setup, the sign-in's questions and the questions that must be answered first ("Discard changes?").
    /// </summary>
    public bool Floats { get; set; }
    /// <summary>(0.3.79) Floating, opened beside this — a calendar item's popup by its block, as the Mac's popover is —
    /// on its right where there is room, else its left, its top level with it, kept inside the window; moved with the
    /// window after that as any popup is. None (or not in view): centred, as before.</summary>
    public FrameworkElement? Anchor { get; set; }
    /// <summary>(0.3.64, the owner's #241) Floating, a light one — something to read, or a switch or a button to press, no
    /// form to fill (What's New, the Calendar's Calendars and Find Appointment, the pane's History and Help): a click
    /// anywhere in the window it floats over closes it, and the click still does what it does there.</summary>
    public bool Light { get; set; }
    /// <summary>Floating, as large as the window under its title bar less a margin — over the navigation pane too —
    /// resized with the window, and sized by its edges and corners (a tool, Knewton Alta, feedback, a
    /// rubric).</summary>
    public bool Large { get; set; }
    /// <summary>Large, the most it opens at on a big window (a rubric's ring, feedback's file and comments), the size the
    /// student gave it apart: null, the whole room.</summary>
    public Size? Most { get; set; }
    /// <summary>Large, the name the size the student gives it by its edges is kept under, for the next time it opens (a
    /// tool's key, "rubric"): null, not kept. Changed while it is open — a tool handing over to another in its popup —
    /// that one's size is taken.</summary>
    public string? SizeKey
    {
        get => sizeKey;
        set
        {
            if (sizeKey == value) return;
            sizeKey = value;
            SizeKeyChanged?.Invoke();
        }
    }
    private string? sizeKey;
    internal Action? SizeKeyChanged;
    /// <summary>Floating, its heading has a Close of its own (×, Close, Done): the popup adds no × of its own.</summary>
    public bool OwnClose { get; set; }
    /// <summary>Floating, the part it is moved by (its heading): a press there that is not on a control drags it. Null: the
    /// top of it, as far down as a heading reaches.</summary>
    public FrameworkElement? Handle { get; set; }
    /// <summary>Floating, what its ×, Escape and Alt+F4 do (a message asks before it is thrown away); null: its Close or
    /// Cancel button pressed, else closed.</summary>
    public Action? Dismiss { get; set; }
    /// <summary>Floating, what it is called where its title does not say (the window's name, an update's notice).</summary>
    public string? Name { get; set; }
    /// <summary>Floating, the window it shows in (null until it shows): its own dialogs go over it.</summary>
    public Window? Window { get; internal set; }

    public event Action? Closed;
    internal SheetHost? Host;
    internal Border? Card;
    internal Action? CloseFloat;

    public void Close()
    {
        if (CloseFloat != null) CloseFloat();
        else Host?.Close(this);
    }

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
        smoke.Background = K.Brush(Color.FromArgb(Support.Appearance.Dark ? (byte)140 : (byte)102, 0, 0, 0));
        smoke.MouseLeftButtonDown += (_, e) => { e.Handled = true; };
        Children.Add(smoke);
        Panel.SetZIndex(this, 1000);
        PreviewKeyDown += OnKey;
        SizeChanged += (_, _) => { if (current?.Card is { } c) Fit(c); };
    }

    public void Show(Sheet s)
    {
        // (a popup that floats: a window of its own over the window this host is in, nothing of this one's dimmed)
        if (s.Floats && Window.GetWindow(this) is { } over) { Simpl.Shell.FloatPopup.Show(s, over); return; }
        if (current != null) { waiting.Enqueue(s); return; }
        current = s;
        s.Host = this;
        var card = Build(s);
        s.Card = card;
        focusBefore = Keyboard.FocusedElement;
        Children.Add(card);
        Visibility = Visibility.Visible;
        smoke.Background = K.Brush(Color.FromArgb(Support.Appearance.Dark ? (byte)140 : (byte)102, 0, 0, 0));
        Motion.FadeIn(smoke, Motion.Normal);
        Motion.Pop(card);
        OpenChanged?.Invoke(true);
        Support.Shot.Moved();
        Dispatcher.BeginInvoke(() => FocusFirst(s, card), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// The keyboard put in a sheet as it shows: the first thing to type in — if it shows without scrolling: focusing a
    /// field further down would scroll the sheet to it, the top of what it holds cut off — else the default button (Cancel,
    /// when the primary is the critical one), else (a sheet with no buttons of its own, a tool's: its heading's ×) the
    /// first thing the keyboard can reach in it, unless the sheet has put the keyboard somewhere itself.
    /// </summary>
    internal static void FocusFirst(Sheet s, FrameworkElement card)
    {
        var primary = s.Buttons.FirstOrDefault(b => b.Kind == SheetButtonKind.Primary);
        var first = FirstFocusable(s.Body) ?? (IInputElement?)(primary is { Danger: true } ? s.Buttons.LastOrDefault(b => b.Kind == SheetButtonKind.Close)?.Control : primary?.Control);
        if (first is UIElement u) { u.Focus(); Keyboard.Focus(first); }
        else if (!card.IsKeyboardFocusWithin) card.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
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

    /// <summary>The keyboard is in a drop-down, a menu or a flyout that is open inside a sheet (Escape is its, then).</summary>
    internal static bool OpenPopupAround(DependencyObject f)
    {
        for (var p = f; p != null; p = VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p))
        {
            if (p is System.Windows.Controls.ComboBox { IsDropDownOpen: true }) return true;
            if (p is System.Windows.Controls.ContextMenu { IsOpen: true } or System.Windows.Controls.MenuItem { IsSubmenuOpen: true }) return true;
            if (p is System.Windows.Controls.Primitives.Popup { IsOpen: true }) return true;
            if (p is SheetHost or Window) return false;
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
            // (the critical one is never Enter's: a tick box focused, Enter would uninstall)
            if (primary is { Danger: false, Control.IsEnabled: true }) { Press(current, primary); e.Handled = true; }
        }
    }

    private void Press(Sheet s, SheetButton b)
    {
        b.Click?.Invoke();
        if (b.Closes) Close(s);
    }

    private Border Build(Sheet s)
    {
        var grid = Content(s, b => Press(s, b), null);
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

    /// <summary>
    /// What a sheet shows, whether over the window or floating in a window of its own: its title (with
    /// <paramref name="close"/> — a floating sheet's × — at its right), what it is about, and its buttons at the foot.
    /// </summary>
    internal static Grid Content(Sheet s, Action<SheetButton> press, UIElement? close)
    {
        var top = new Stack(Orientation.Vertical, 12, Array.Empty<UIElement>());
        if (!string.IsNullOrEmpty(s.Title))
        {
            var title = K.Text(s.Title!, Ty.Subtitle, B.Text, TextWrapping.Wrap);
            title.TextTrimming = TextTrimming.None;
            // (Narrator reads it as the popup's heading, as the Mac's sheet title is one)
            System.Windows.Automation.AutomationProperties.SetHeadingLevel(title, System.Windows.Automation.AutomationHeadingLevel.Level1);
            if (close is FrameworkElement x && !s.Bare)
            {
                x.Margin = new Thickness(12, -6, -10, 0);
                x.VerticalAlignment = VerticalAlignment.Top;
                top.Children.Add(K.Columns("*, auto", title.Col(0), x.Col(1)));
            }
            else top.Children.Add(title);
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
                    Appearance = b.Kind != SheetButtonKind.Primary ? ControlAppearance.Secondary : b.Danger ? ControlAppearance.Danger : ControlAppearance.Primary,
                };
                b.Control = btn;
                var bb = b;
                btn.Click += (_, _) => press(bb);
                Grid.SetColumn(btn, col);
                col += 2;
                row.Children.Add(btn);
                b.Apply(); // (0.3.50: one not shown takes no place)
            }
            var foot = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0) };
            foot.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
            foot.SetResourceReference(Border.BorderBrushProperty, "ContentDialogSeparatorBorderBrush");
            Grid.SetRow(foot, 1);
            grid.Children.Add(foot);
        }
        return grid;
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
        {
            var u = (UIElement)root;
            if (!u.IsEnabled || !u.IsVisible) return null;
            // (words set out to be selected and copied — read-only — are not a field to type in)
            if (root is System.Windows.Controls.Primitives.TextBoxBase { IsReadOnly: true }) return null;
            return InFirstScreen(u) ? (IInputElement)root : null;
        }
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
            if (FirstFocusable(VisualTreeHelper.GetChild(root, i)) is { } f) return f;
        if (root is FrameworkElement fe && n == 0)
            foreach (var c in LogicalTreeHelper.GetChildren(fe).OfType<DependencyObject>())
                if (FirstFocusable(c) is { } f2) return f2;
        return null;
    }

    /// <summary>Whether an element shows whole in every scroller it is in, as the sheet opens (focusing one that does not
    /// would scroll the sheet down to it).</summary>
    private static bool InFirstScreen(UIElement u)
    {
        for (var p = VisualTreeHelper.GetParent(u); p != null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is SheetHost) break;
            if (p is not ScrollViewer sv || sv.ViewportHeight <= 0) continue;
            try
            {
                var at = u.TranslatePoint(new Point(0, 0), sv);
                if (at.Y < -0.5 || at.Y + u.RenderSize.Height > sv.ViewportHeight + 0.5) return false;
            }
            catch (InvalidOperationException) { return false; }
        }
        return true;
    }
}
