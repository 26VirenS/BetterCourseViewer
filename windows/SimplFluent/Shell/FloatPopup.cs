using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
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
/// Escape while it has the keyboard, or by Alt+F4. It opens in the middle of the window (0.3.4), and moves with the
/// window, keeping the place it was moved to. A large one (a tool, Knewton Alta, feedback, a rubric) spans the
/// window under its title bar less a margin all round — over the pane too, the owner's choice — sized again with the
/// window, one over another a step down and along (and that much smaller, inside the window); it is sized by its edges
/// and corners too (Windows' own sizing), and the size the student gives it is kept for its kind (<see
/// cref="Sheet.SizeKey"/>, a tool's key) and taken the next time it opens, kept to the window and the screen — a sizing
/// the student takes back (Escape, or a right-click, while sizing) keeps nothing. Its own questions show over it, not over
/// the window.
/// </summary>
public sealed class FloatPopup : FluentWindow, ISheetWindow
{
    private static readonly List<FloatPopup> All = new();

    private readonly Sheet sheet;
    private readonly Window over;
    private readonly SheetHost sheets = new();
    private readonly Border card = new();
    private readonly Grid root = new();
    /// <summary>Where it was moved to, from where it opens (the middle of the window).</summary>
    private Vector offset;
    /// <summary>A large one's step down and along from the large ones already open over its window (none: the first).</summary>
    private Vector cascade;
    /// <summary>A large one's size as the student gave it by its edges — now, or an earlier time (kept by its sheet's
    /// SizeKey): null, as large as its room.</summary>
    private Size? chosen;
    /// <summary>Being sized by its edges (Windows' sizing under way): the card follows the window, not the window the card.</summary>
    private bool sizing;
    /// <summary>Its size, and the size the student had given it, as the sizing by its edges began.</summary>
    private Size sizedFrom;
    private Size? chosenBefore;
    private bool placing, closing, ended, shown;

    /// <summary>The least a large one is sized to by its edges — less than the room of the smallest window Simpl allows (720
    /// by 520: 688 by 440).</summary>
    private static readonly Size LeastLarge = new(480, 340);

    /// <summary>The band along a large one's edges that sizes it (WPF-UI's 4). A web view there — a window of its own —
    /// takes the pointer instead, so a page shown edge to edge (Knewton Alta's, feedback's file) keeps this
    /// far in from the popup's sides and foot.</summary>
    public const double EdgeRoom = 4;

    public SheetHost Sheets => sheets;

    /// <summary>The popup a sheet shows in, over a window (the one it was asked over; asked over another popup, that
    /// popup's window).</summary>
    public static FloatPopup Show(Sheet s, Window over)
    {
        while (over is FloatPopup { Owner: { } under }) over = under;
        var w = new FloatPopup(s, over);
        // (a large one over another opens a step down and along, so both are seen — the Mac's 28 — the fifth back where
        //  the first is; made that much smaller, its right and bottom edges where the first's are: at its room's whole
        //  size and moved along, its edges that size it lay outside the window — off the screen with the window maximized)
        if (s.Large)
        {
            w.cascade = new Vector(28, 28) * (All.Count(p => p.sheet.Large && ReferenceEquals(p.over, over)) % 4);
            w.offset = w.cascade / 2;
            // (sized again for its step, before it first shows)
            w.Fit();
        }
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
        // (a large one sized by its edges and corners: WPF-UI gives a window that can be resized 4 of edge to take hold
        //  of — never maximized or minimized, NoBoxes)
        ResizeMode = s.Large ? ResizeMode.CanResize : ResizeMode.NoResize;
        // (WPF-UI's window style holds a window to 460 × 320 at least: a popup shorter than that — History, Help — had a
        //  strip of bare window under its card)
        MinWidth = s.Large ? LeastLarge.Width : 0;
        MinHeight = s.Large ? LeastLarge.Height : 0;
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
        SizeChanged += (_, _) => Sized();
        SourceInitialized += (_, _) =>
        {
            if (s.Large && PresentationSource.FromVisual(this) is HwndSource src)
            {
                NoBoxes();
                src.AddHook(Hook);
            }
            Fit();
            Place();
        };
        // (never maximized or minimized by Windows' keys: it is a popup over its window)
        StateChanged += (_, _) => { if (s.Large && WindowState != WindowState.Normal) WindowState = WindowState.Normal; };
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => { if (!ended && !sheets.IsOpen) SheetHost.FocusFirst(s, card); }, DispatcherPriority.Input);
        over.LocationChanged += OverMoved;
        over.SizeChanged += OverSized;
        over.StateChanged += OverMoved;
        over.Closed += OverClosed;
        if (over is MainWindow m) m.PopupRoomChanged += OverMoved;
        if (s.Large)
        {
            chosen = Remembered(s.SizeKey);
            // (a tool handing over to another in its popup: that one's size, as the student last gave it)
            s.SizeKeyChanged = () =>
            {
                chosen = Remembered(sheet.SizeKey);
                Fit();
                Place();
                Said("changed to");
            };
            if (Launch.IsDevelopment && Launch.Get("popup-move") is { } move) MoveSoon(move);
            else if (Launch.IsDevelopment && Launch.Get("popup-drag") is { } drag) DragSoon(drag);
        }
        Fit();
    }

    // ---- where it is, and how large ----

    /// <summary>Where it is centred, in screen units: in Simpl's window everything under its title bar, over the pane too
    /// (MainWindow.LargePopupRoom) — 0.3.4: every popup, not only a large one, in the middle of the whole window (the
    /// owner: "popups dont show up in the middle") — else everything under the title bar of the window it is over.</summary>
    private Rect Room()
    {
        if (over.Content is not FrameworkElement content || PresentationSource.FromVisual(content) is not { CompositionTarget: { } target }) return Rect.Empty;
        var r = over is MainWindow m ? m.LargePopupRoom : new Rect(0, 32, content.ActualWidth, Math.Max(0, content.ActualHeight - 32));
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

    /// <summary>Its size for the window: a large one its room less a margin all round — or the size the student gave it
    /// by its edges, or its sheet's most, where smaller — and never larger than the screen's room; another as it asks,
    /// kept inside the window.</summary>
    private void Fit()
    {
        var room = Room();
        var whole = Whole();
        if (room.IsEmpty || whole.IsEmpty) return;
        var m = Gap(room);
        if (sheet.Large)
        {
            var screen = Screen();
            var most = new Size(Math.Max(LeastLarge.Width, Math.Min(room.Width, screen.Width) - 2 * m), Math.Max(LeastLarge.Height, Math.Min(room.Height, screen.Height) - 2 * m));
            // (its edges stop there; a window made smaller takes it in with it, and larger again gives back what was asked)
            MaxWidth = most.Width;
            MaxHeight = most.Height;
            var want = chosen ?? sheet.Most ?? most;
            // (one a step down and along: that much smaller, so its edges stay inside the window)
            var fits = new Size(Math.Max(LeastLarge.Width, most.Width - cascade.X), Math.Max(LeastLarge.Height, most.Height - cascade.Y));
            card.Width = Math.Clamp(want.Width, LeastLarge.Width, fits.Width);
            card.Height = Math.Clamp(want.Height, LeastLarge.Height, fits.Height);
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
        var at = Centre(room, whole, card.DesiredSize) + offset;
        Left = at.X;
        Top = at.Y;
        Said("opens at");
    }

    /// <summary>The window as large as its card (not while the student sizes it by its edges: the card follows it then).</summary>
    private void SizeTo(Size size)
    {
        if (sizing) return;
        if (size.Width > 0 && (double.IsNaN(Width) || Math.Abs(Width - size.Width) > 0.5)) Width = size.Width;
        if (size.Height > 0 && (double.IsNaN(Height) || Math.Abs(Height - size.Height) > 0.5)) Height = size.Height;
    }

    /// <summary>The window sized: by its edges (a large one), its card follows it and the size is the student's — kept,
    /// and the popup placed where it now is, once let go (<see cref="Hook"/>); else as its card asked, placed again where
    /// it was moved to.</summary>
    private void Sized()
    {
        if (sizing)
        {
            Follow(new Size(ActualWidth, ActualHeight));
            return;
        }
        // (a large one sized by Windows itself — its keys' snap to a side of the screen — put back to its card's size)
        if (sheet.Large && card.ActualWidth > 0 && (Math.Abs(ActualWidth - card.ActualWidth) > 1.5 || Math.Abs(ActualHeight - card.ActualHeight) > 1.5))
            SizeTo(card.RenderSize);
        Place();
    }

    /// <summary>The card as large as the window sized by its edges, that size the student's.</summary>
    private void Follow(Size size)
    {
        if (!(size.Width > 0 && size.Height > 0)) return;
        card.Width = size.Width;
        card.Height = size.Height;
        chosen = size;
    }

    /// <summary>Let go after sizing it by its edges (once what that laid out has been): its size as Windows left it — read
    /// from the window itself, as with Windows' "Show window contents while dragging" off it is sized only as it is let
    /// go — kept for its kind, for the next time it opens; its place kept where it now is (as a popup moved by its
    /// heading keeps its), on the window.</summary>
    private void SizedByEdges()
    {
        sizing = false;
        if (ended) return;
        var now = WindowSize();
        // (taken back — Escape or a right-click while sizing: Windows puts it back as it was — the size before kept, and
        //  nothing kept as the student's that they did not give it)
        if (now is not { } given || (Math.Abs(given.Width - sizedFrom.Width) <= 1 && Math.Abs(given.Height - sizedFrom.Height) <= 1))
        {
            chosen = chosenBefore;
            Fit();
            Place();
            Said("sizing taken back, at");
            return;
        }
        Follow(given);
        if (chosen is not { } size) return;
        Remember(sheet.SizeKey, size);
        var room = Room();
        var whole = Whole();
        if (!room.IsEmpty && !whole.IsEmpty) offset = new Point(Left, Top) - Centre(room, whole, new Size(ActualWidth, ActualHeight));
        Fit();
        Place();
        Said("sized by its edges to");
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

    /// <summary>Where it opens: centred in the window, kept inside it.</summary>
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

    /// <summary>For the screenshot suite and the log (a development run's): a large one's size, and whose it is.</summary>
    private void Said(string what)
    {
        if (!sheet.Large || !Launch.IsDevelopment) return;
        // ("as sized", never "the student's": CI checks the logs never hold that word — the mock's sign-in)
        var whose = chosen != null ? "as sized" : sheet.Most != null ? "its most" : "its room's";
        Log.Write(string.Create(CultureInfo.InvariantCulture, $"large popup {sheet.SizeKey ?? sheet.Name ?? "?"} {what} {card.Width:0}×{card.Height:0} ({whose}) at {Left:0},{Top:0}"));
    }

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
        // (a large one not sizable while it is moved: Windows snaps a window that is to a side of the screen, or
        //  maximizes it at the top)
        if (sheet.Large) ResizeMode = ResizeMode.NoResize;
        try { DragMove(); }
        catch (InvalidOperationException) { return; }
        finally
        {
            if (sheet.Large && !ended)
            {
                ResizeMode = ResizeMode.CanResize;
                NoBoxes();
            }
        }
        if (!room.IsEmpty && !whole.IsEmpty) offset = new Point(Left, Top) - Centre(room, whole, new Size(ActualWidth, ActualHeight));
        Place();
    }

    // ---- sized by its edges (a large one) ----

    private const int WM_SIZING = 0x0214, WM_EXITSIZEMOVE = 0x0232, GWL_STYLE = -16, WS_MAXIMIZEBOX = 0x00010000, WS_MINIMIZEBOX = 0x00020000;

    /// <summary>Windows' sizing by its edges: under way from its first step (a move by its heading has none) until it is
    /// let go and what that laid out has been — its size as that began noted, for a sizing taken back.</summary>
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SIZING && !sizing)
        {
            // (its first step, before Windows has sized it: what it was, for a sizing taken back)
            sizing = true;
            sizedFrom = WindowSize() ?? new Size(card.ActualWidth, card.ActualHeight);
            chosenBefore = chosen;
        }
        else if (msg == WM_EXITSIZEMOVE && sizing) Dispatcher.BeginInvoke(SizedByEdges, DispatcherPriority.Background);
        return IntPtr.Zero;
    }

    /// <summary>The window's size on the screen, in screen units (its frame is its card: no border round it).</summary>
    private Size? WindowSize()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r) || PresentationSource.FromVisual(this) is not { CompositionTarget: { } target }) return null;
        var a = target.TransformFromDevice.Transform(new Point(r.L, r.T));
        var b = target.TransformFromDevice.Transform(new Point(r.R, r.B));
        return b.X > a.X && b.Y > a.Y ? new Size(Math.Round(b.X - a.X), Math.Round(b.Y - a.Y)) : null;
    }

    /// <summary>Sizable by its edges, but never maximized or minimized (a window that can be resized has both boxes, though
    /// it shows neither): it is a popup over its window.</summary>
    private void NoBoxes()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLong(hwnd, GWL_STYLE);
        var without = style & ~(WS_MAXIMIZEBOX | WS_MINIMIZEBOX);
        if (without != style) SetWindowLong(hwnd, GWL_STYLE, without);
    }

    /// <summary>The size the student last gave a large popup of this kind by its edges (Prefs "popup:&lt;key&gt;", its
    /// width and height) — in a development run, --popup-size 900x560 as if they had: null, none.</summary>
    /// <summary>(0.3.30) The size a tool opens at before its student sizes it, where filling the room would only stretch
    /// it: the Calculator (GeoGebra's) narrower than the window.</summary>
    private static readonly Dictionary<string, Size> Opening = new() { ["tool:calc"] = new Size(900, 860) };

    private static Size? Remembered(string? key)
    {
        if (key == null) return null;
        var said = (Launch.IsDevelopment ? Launch.Get("popup-size") : null) ?? Prefs.String($"popup:{key}");
        if (said == null) return Opening.TryGetValue(key, out var o) ? o : null;
        var p = said.Split(',', 'x');
        if (p is not { Length: 2 }
            || !double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
            || !(w > 0 && h > 0 && double.IsFinite(w) && double.IsFinite(h))) return null;
        return new Size(w, h);
    }

    private static void Remember(string? key, Size size)
    {
        if (key != null) Prefs.Set($"popup:{key}", string.Create(CultureInfo.InvariantCulture, $"{size.Width:0},{size.Height:0}"));
    }

    /// <summary>The room of the screen the window it is over is on (its work area, the taskbar left out), in screen units:
    /// a large popup is never larger. Unknown: no limit.</summary>
    private Size Screen()
    {
        var none = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var hwnd = new WindowInteropHelper(over).Handle;
        if (hwnd == IntPtr.Zero || PresentationSource.FromVisual(over) is not { CompositionTarget: { } target }) return none;
        var monitor = MonitorFromWindow(hwnd, 2); // (the nearest)
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return none;
        var a = target.TransformFromDevice.Transform(new Point(info.Work.L, info.Work.T));
        var b = target.TransformFromDevice.Transform(new Point(info.Work.R, info.Work.B));
        return b.X > a.X && b.Y > a.Y ? new Size(b.X - a.X, b.Y - a.Y) : none;
    }

    /// <summary>
    /// --popup-drag 300 (CI's check that its edges size it; a development run's alone): six seconds after it opens, it is
    /// sized as the student sizes it — the pointer pressed on its right edge, moved that far left a step at a time and let
    /// go, Windows' own sizing between — and the log says the size it was given and the size kept (run with no other copy
    /// of Simpl beside it, which could take the pointer's press).
    /// </summary>
    private void DragSoon(string said)
    {
        if (!double.TryParse(said, NumberStyles.Float, CultureInfo.InvariantCulture, out var by) || by <= 0) by = 300;
        After(6, () => ByEdge(by));
    }

    /// <summary>
    /// --popup-move -120,40 (CI's check that a large one moved by its heading is still sized by its edges; a development
    /// run's alone): six seconds after it opens, the pointer pressed on its heading (a tool's tile), moved that far a step
    /// at a time and let go — Windows' own move, with sizing turned off for it and on again after — then sized by its right
    /// edge 200 narrower (--popup-drag's way); the log says where it went and the size kept.
    /// </summary>
    private void MoveSoon(string said)
    {
        var p = said.Split(',');
        double dx = -120, dy = 40;
        if (p.Length == 2)
        {
            double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out dx);
            double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out dy);
        }
        After(6, () =>
        {
            Activate();
            var (left, top) = (Left, Top);
            // (on the heading where nothing is a control: a tool's tile at its top left — the heading's empty room takes no
            //  press — or, with no heading named, its top)
            var at = sheet.Handle is FrameworkElement h && h.IsVisible ? h.TranslatePoint(new Point(22, Math.Min(28, h.ActualHeight / 2)), this) : new Point(ActualWidth / 2, 20);
            var start = PointToScreen(at);
            var end = PointToScreen(new Point(at.X + dx, at.Y + dy));
            Log.Write($"popup move: on the heading at {(int)start.X},{(int)start.Y}");
            PointerDrag(start, end, () => After(1.5, () =>
            {
                Log.Write(string.Create(CultureInfo.InvariantCulture, $"popup move: moved by {Left - left:0},{Top - top:0} to {Left:0},{Top:0}; sizable: {ResizeMode}"));
                After(0.5, () => ByEdge(200));
            }));
        });
    }

    /// <summary>Sized by its right edge as the student sizes it, <paramref name="by"/> narrower, the size kept said in the log.</summary>
    private void ByEdge(double by)
    {
        Activate();
        var start = PointToScreen(new Point(ActualWidth - 2, ActualHeight / 2));
        var end = PointToScreen(new Point(ActualWidth - 2 - by, ActualHeight / 2));
        Log.Write($"popup drag: on the right edge at {(int)start.X},{(int)start.Y}");
        PointerDrag(start, end, () => After(1.5, () => Log.Write(string.Create(CultureInfo.InvariantCulture,
            $"popup drag: {sheet.SizeKey} is {ActualWidth:0}×{ActualHeight:0}, kept as {Prefs.String($"popup:{sheet.SizeKey}") ?? "nothing"}"))));
    }

    /// <summary>The real pointer pressed at <paramref name="start"/>, moved to <paramref name="end"/> in twelve steps and let
    /// go (Windows' own moving or sizing between, its loop letting these steps run), then <paramref name="done"/>.</summary>
    private void PointerDrag(Point start, Point end, Action done)
    {
        SetCursorPos((int)start.X, (int)start.Y);
        After(0.4, () =>
        {
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // (left down)
            var k = 0;
            void Step()
            {
                k++;
                SetCursorPos((int)(start.X + (end.X - start.X) * k / 12), (int)(start.Y + (end.Y - start.Y) * k / 12));
                mouse_event(0x0001, 0, 0, 0, UIntPtr.Zero); // (a move, where it now is)
                if (k < 12) { After(0.05, Step); return; }
                After(0.3, () =>
                {
                    mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // (left up)
                    Log.Write("popup drag: let go");
                    done();
                });
            }
            After(0.2, Step);
        });
    }

    private void After(double seconds, Action a)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, _) => { t.Stop(); if (!ended) a(); };
        t.Start();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect32 Whole, Work;
        public int Flags;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

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
        sheet.SizeKeyChanged = null;
        sheet.RaiseClosed();
        // (the keyboard back with the window it was over, when it was in front)
        if (over.IsVisible && Application.Current?.Windows.OfType<Window>().Any(w => w.IsActive) != true) over.Activate();
    }
}
