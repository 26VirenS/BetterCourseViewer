using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Shell;

/// <summary>
/// A popup that floats over the window without holding it — every popup but a quiz being taken, the first setup, the
/// sign-in's questions, the tour and the questions that must be answered first ("Discard changes?"), which stay sheets
/// over the window (Kit/Sheets.cs). A sheet marked <see cref="Sheet.Floats"/> shows here, in a window of its own owned
/// by the window it was asked over: kept above that window, minimized and brought back with it, gone with it. Nothing
/// takes the window's clicks while it is up — no dimming, only the shadow Windows draws round a window — so the pane,
/// the title bar, the search and the page behind it all go on working, and a place opened under it leaves it where it
/// was. It is moved by its heading (a press there that is not on a control), closed by its × (or its own Close), by
/// Escape while it has the keyboard, or by Alt+F4. It opens centred over the page beside the pane — a large one (a tool,
/// Knewton Alta, Macmillan) as large as that page less a margin, resized with the window — and moves with the window,
/// keeping the place it was moved to. Its own questions show over it, not over the window.
/// </summary>
public sealed class FloatPopup : FluentWindow, ISheetWindow
{
    private static readonly List<FloatPopup> All = new();

    private readonly Sheet sheet;
    private readonly Window over;
    private readonly SheetHost sheets = new();
    private readonly Border card = new();
    private readonly Grid root = new();
    /// <summary>Where it was moved to, from where it opens (centred over the page).</summary>
    private Vector offset;
    private bool placing, closing, ended, shown;

    public SheetHost Sheets => sheets;

    /// <summary>The popup a sheet shows in, over a window (the one it was asked over; asked over another popup, that
    /// popup's window).</summary>
    public static FloatPopup Show(Sheet s, Window over)
    {
        while (over is FloatPopup { Owner: { } under }) over = under;
        var w = new FloatPopup(s, over);
        All.Add(w);
        if (over.IsVisible) w.Open();
        else
        {
            // (the window out of sight — kept in the notification area: the popup comes up with it, never alone)
            void Visible(object? sender, DependencyPropertyChangedEventArgs e)
            {
                if (!over.IsVisible) return;
                over.IsVisibleChanged -= Visible;
                w.Open();
            }
            over.IsVisibleChanged += Visible;
        }
        return w;
    }

    /// <summary>Shown over its window — the first time where it opens, before it shows; again (brought back with its
    /// window) where it was.</summary>
    private void Open()
    {
        if (ended) return;
        if (!shown)
        {
            shown = true;
            Owner = over;
            PlaceFirst();
            Show();
            Shot.Moved();
        }
        else if (!IsVisible)
        {
            Show();
            Place();
        }
    }

    /// <summary>The popups over a window.</summary>
    public static IReadOnlyList<FloatPopup> Over(Window w) => All.Where(p => ReferenceEquals(p.over, w)).ToList();

    /// <summary>The popups over a window hidden with it (the window closed to the notification area), shown again with it.</summary>
    public static void ShowAgain(Window w)
    {
        foreach (var p in Over(w)) p.Open();
    }

    /// <summary>An update waits while it is open (What's New does not hold one).</summary>
    public bool HoldsUpdates => sheet.HoldsUpdates;

    /// <summary>What it shows.</summary>
    public Sheet Sheet => sheet;

    private FloatPopup(Sheet s, Window over)
    {
        sheet = s;
        this.over = over;
        s.Window = this;
        s.CloseFloat = End;
        Title = s.Title ?? s.Name ?? "Simpl";
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowBackdropType = Appearance.Backdrop;
        ExtendsContentIntoTitleBar = true;
        WindowCornerPreference = WindowCornerPreference.Round;
        FontFamily = K.TextFace;
        SetResourceReference(ForegroundProperty, B.Text);
        SetResourceReference(BackgroundProperty, B.Window);
        if (Shot.Asked) ToolTipService.SetIsEnabled(this, false);

        // what it shows: the sheet's title (and × beside it), what it holds, its buttons — or a sheet of its own making
        // edge to edge, the × over its top right corner unless its heading has a Close of its own
        var close = s.OwnClose ? null : K.IconButton(SymbolRegular.Dismiss24, Dismiss, "Close (Esc)", 16, 36);
        var titled = !s.Bare && !string.IsNullOrEmpty(s.Title);
        var content = SheetHost.Content(s, Press, titled ? close : null);
        root.Children.Add(content);
        if (!titled && close is FrameworkElement x)
        {
            x.HorizontalAlignment = HorizontalAlignment.Right;
            x.VerticalAlignment = VerticalAlignment.Top;
            x.Margin = new Thickness(0, 10, 10, 0);
            root.Children.Add(x);
        }
        root.Children.Add(sheets);
        card.Child = root;
        card.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
        card.Width = s.Width;
        if (s.Height is { } h) card.Height = h;
        s.Card = card;
        // (the card at its own size on a canvas, the window made its size as it changes — not the window's SizeToContent,
        //  which a window without Windows' frame round it measures with the frame's room added; what a tool floats over
        //  everything in its window — the Periodic Table's search results and tour, PTLayer — goes in beside it)
        Content = new Canvas { Children = { card } };
        card.SizeChanged += (_, e) => SizeTo(e.NewSize);
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(card, KeyboardNavigationMode.Cycle);

        PreviewMouseLeftButtonDown += Pressed;
        PreviewKeyDown += OnPreviewKey;
        KeyDown += OnKey;
        Closing += OnClosing;
        Closed += (_, _) => Ended();
        SizeChanged += (_, _) => Place();
        SourceInitialized += (_, _) => { Fit(); Place(); };
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => { if (!ended && !sheets.IsOpen) SheetHost.FocusFirst(s, card); }, DispatcherPriority.Input);
        over.LocationChanged += OverMoved;
        over.SizeChanged += OverSized;
        over.StateChanged += OverMoved;
        over.Closed += OverClosed;
        if (over is MainWindow m) m.PopupRoomChanged += OverMoved;
        Fit();
    }

    // ---- where it is, and how large ----

    /// <summary>Where it is centred, in screen units: the page beside the pane in Simpl's window (MainWindow.PopupRoom),
    /// else everything under the title bar of the window it is over.</summary>
    private Rect Room()
    {
        if (over.Content is not FrameworkElement content || PresentationSource.FromVisual(content) is not { CompositionTarget: { } target }) return Rect.Empty;
        var r = over is MainWindow m ? m.PopupRoom : new Rect(0, 32, content.ActualWidth, Math.Max(0, content.ActualHeight - 32));
        if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) return Rect.Empty;
        try
        {
            var a = target.TransformFromDevice.Transform(content.PointToScreen(r.TopLeft));
            var b = target.TransformFromDevice.Transform(content.PointToScreen(r.BottomRight));
            return new Rect(a, b);
        }
        catch (InvalidOperationException) { return Rect.Empty; }
    }

    /// <summary>The window it is over, in screen units (kept on it as it is moved).</summary>
    private Rect Whole()
    {
        if (over.Content is not FrameworkElement content || PresentationSource.FromVisual(content) is not { CompositionTarget: { } target }) return Rect.Empty;
        try
        {
            var a = target.TransformFromDevice.Transform(content.PointToScreen(new Point(0, 0)));
            var b = target.TransformFromDevice.Transform(content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight)));
            return new Rect(a, b);
        }
        catch (InvalidOperationException) { return Rect.Empty; }
    }

    /// <summary>The room kept round a popup: 24, or 16 on a narrow page.</summary>
    private static double Gap(Rect r) => r.Width >= 900 ? 24 : 16;

    /// <summary>Its size for the window: a large one the page less a margin; another as it asks, kept inside the window.</summary>
    private void Fit()
    {
        var room = Room();
        var whole = Whole();
        if (room.IsEmpty || whole.IsEmpty) return;
        var m = Gap(room);
        if (sheet.Large)
        {
            card.Width = Math.Max(320, room.Width - 2 * m);
            card.Height = Math.Max(280, room.Height - 2 * m);
            return;
        }
        card.MaxWidth = Math.Max(280, whole.Width - 2 * m);
        card.MaxHeight = Math.Max(200, room.Height - 2 * m);
    }

    /// <summary>Where it first shows, before it is shown (never a moment in a corner of the screen first): its size as
    /// its card asks.</summary>
    private void PlaceFirst()
    {
        var room = Room();
        var whole = Whole();
        if (room.IsEmpty || whole.IsEmpty) return;
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SizeTo(card.DesiredSize);
        var at = Centre(room, whole, card.DesiredSize);
        Left = at.X;
        Top = at.Y;
    }

    /// <summary>The window as large as its card.</summary>
    private void SizeTo(Size size)
    {
        if (size.Width > 0 && (double.IsNaN(Width) || Math.Abs(Width - size.Width) > 0.5)) Width = size.Width;
        if (size.Height > 0 && (double.IsNaN(Height) || Math.Abs(Height - size.Height) > 0.5)) Height = size.Height;
    }

    /// <summary>Centred over the page, where it was moved to from there, and kept on the window: its heading never
    /// further than the window's edges, so it can always be taken hold of again.</summary>
    private void Place()
    {
        if (placing || ended) return;
        var room = Room();
        var whole = Whole();
        if (room.IsEmpty || whole.IsEmpty || ActualWidth <= 0) return;
        placing = true;
        try
        {
            var at = Centre(room, whole, new Size(ActualWidth, ActualHeight)) + offset;
            const double keep = 120;
            var x = Math.Clamp(at.X, whole.Left - ActualWidth + keep, Math.Max(whole.Left - ActualWidth + keep, whole.Right - keep));
            var y = Math.Clamp(at.Y, whole.Top, Math.Max(whole.Top, whole.Bottom - 48));
            if (Math.Abs(Left - x) > 0.5) Left = x;
            if (Math.Abs(Top - y) > 0.5) Top = y;
        }
        finally { placing = false; }
    }

    /// <summary>Where it opens: centred over the page; a popup wider than the page moved over the pane as far as it must
    /// to keep inside the window.</summary>
    private Point Centre(Rect room, Rect whole, Size size)
    {
        var m = Gap(room);
        var x = room.Left + (room.Width - size.Width) / 2;
        var y = room.Top + (room.Height - size.Height) / 2;
        x = Math.Max(whole.Left + m, Math.Min(x, whole.Right - m - size.Width));
        y = Math.Max(room.Top + (sheet.Large ? m : 0), y);
        return new Point(x, y);
    }

    private void OverMoved(object? sender, EventArgs e) { Fit(); Place(); }

    private void OverSized(object? sender, SizeChangedEventArgs e) { Fit(); Place(); }

    private void OverClosed(object? sender, EventArgs e) => End();

    // ---- moved by its heading ----

    /// <summary>A press on its heading that is not on a control: it follows the pointer until it is let go.</summary>
    private void Pressed(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1 || sheets.IsOpen || e.OriginalSource is not DependencyObject source) return;
        var onHandle = sheet.Handle is { } handle ? Drag.Within(source, handle) : e.GetPosition(card).Y <= 60;
        if (!onHandle || Drag.OnControl(source)) return;
        e.Handled = true;
        var room = Room();
        var whole = Whole();
        try { DragMove(); }
        catch (InvalidOperationException) { return; }
        if (!room.IsEmpty && !whole.IsEmpty) offset = new Point(Left, Top) - Centre(room, whole, new Size(ActualWidth, ActualHeight));
        Place();
    }

    // ---- the keyboard ----

    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        // (Enter presses the default button, as a sheet's does; never in a box that takes new lines, nor a button itself)
        if (sheets.IsOpen || e.Key != Key.Enter) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox { AcceptsReturn: true } or ButtonBase) return;
        var primary = sheet.Buttons.FirstOrDefault(b => b.Kind == SheetButtonKind.Primary);
        if (primary is not { Danger: false, Control.IsEnabled: true }) return;
        Press(primary);
        e.Handled = true;
    }

    /// <summary>Escape, once whatever has the keyboard inside it has not taken it (a field's list, a menu, a tool's own):
    /// closed — unless what it shows must be answered first (Modal), when it is the sheet's own to deal with.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sheets.IsOpen || sheet.Modal) return;
        if (Keyboard.FocusedElement is DependencyObject f && SheetHost.OpenPopupAround(f)) return;
        e.Handled = true;
        Dismiss();
    }

    // ---- closing ----

    private void Press(SheetButton b)
    {
        b.Click?.Invoke();
        if (b.Closes) End();
    }

    /// <summary>The ×, Escape, Alt+F4: what the sheet says (asked first), else its Close or Cancel pressed, else closed.</summary>
    private void Dismiss()
    {
        if (ended) return;
        if (sheet.Dismiss is { } d) { d(); return; }
        if (sheet.Buttons.LastOrDefault(b => b.Kind == SheetButtonKind.Close) is { } cancel)
        {
            if (cancel.Enabled) Press(cancel);
            return;
        }
        End();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // (Alt+F4 is the student's ×; Simpl quitting, or the window under it closing, closes it as it is)
        if (closing || MainWindow.Quitting || Dispatcher.HasShutdownStarted || !over.IsLoaded) return;
        e.Cancel = true;
        Dispatcher.BeginInvoke(Dismiss);
    }

    /// <summary>Closed for good (the sheet's Close, a button that closes it, the window under it gone).</summary>
    private void End()
    {
        if (closing) return;
        closing = true;
        try { Close(); }
        catch (InvalidOperationException) { Ended(); } // (already gone with the window it was over)
    }

    private void Ended()
    {
        if (ended) return;
        ended = true;
        closing = true;
        All.Remove(this);
        over.LocationChanged -= OverMoved;
        over.SizeChanged -= OverSized;
        over.StateChanged -= OverMoved;
        over.Closed -= OverClosed;
        if (over is MainWindow m) m.PopupRoomChanged -= OverMoved;
        sheet.CloseFloat = null;
        sheet.RaiseClosed();
        // (the keyboard back with the window it was over, when it was in front)
        if (over.IsVisible && Application.Current?.Windows.OfType<Window>().Any(w => w.IsActive) != true) over.Activate();
    }
}
