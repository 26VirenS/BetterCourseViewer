using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Shell;

/// <summary>
/// The main window's side of updates (Support/Updates.cs): what it has open that an update must not cut off — a dialog
/// over it (Hand In, the setup, a message, a task, a tool opened whole), the sign-in, the tour, a window of a page of the
/// school's or a tool's, a dialog in Settings, a menu or a flyout, the quick look, a file on its way; where it is, kept
/// as Simpl quits for an update; how it opens again after one — in front, minimized, or in the notification area, as it
/// was left; and the notice at the top of the window (the Mac's CriticalUpdateNotice, for every update here): an update
/// waiting for something to close, for a few seconds (a Critical Update's stays), and one going in.
/// </summary>
public static class UpdateWatch
{
    private static Border? pill;
    private static TextBlock? words;
    private static ContentControl? mark;
    private static string? briefShown;
    private static int turn;

    /// <summary>The window's part, as it is made: before it shows (how it opens after an update), once it has (the checks
    /// begin), and the notice over everything in it.</summary>
    public static void Attach(MainWindow main, Grid root, Func<Engine.Engine?> engine, Func<bool> touring)
    {
        Reopen(main);
        Updates.Watch = () => Holding(main, engine(), touring());
        Updates.WindowShown = () => main.IsVisible && main.WindowState != WindowState.Minimized;
        Updates.Where = () => Where(main, engine());

        words = K.Text("", Ty.Body);
        words.TextTrimming = TextTrimming.CharacterEllipsis;
        mark = new ContentControl { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        pill = new Border
        {
            Child = K.Columns("auto, *", mark.Col(0), words.Margin(10, 0, 0, 0).Col(1)),
            Padding = new Thickness(12, 6, 16, 7), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Effect = K.Shadow(16, 4, 0.18), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        };
        pill.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        // (over everything in the window, the sheets' smoke too: it says what an open quiz is holding up)
        Grid.SetRowSpan(pill, 2);
        Panel.SetZIndex(pill, 1001);
        root.Children.Add(pill);
        main.SizeChanged += (_, _) => Fit(main);
        Fit(main);

        Updates.Changed += () => main.Dispatcher.BeginInvoke(() => Say());
        var began = false;
        main.Loaded += (_, _) =>
        {
            if (began) return;
            began = true;
            Updates.Start();
            Test(main);
        };
    }

    // ---- opening again after an update ----

    /// <summary>
    /// Opened again by an update: (0.3.79) in front, over whatever else is open, however the window was left — before,
    /// one left behind other windows or in the notification area opened again minimized or out of sight there, and an
    /// update, which goes in by itself when the window is not in use, looked like Simpl had closed (its What's New over a
    /// window nobody saw). Raised over the others even when Windows keeps the focus where it is.
    /// </summary>
    private static void Reopen(MainWindow main)
    {
        if (Updates.Resumed is not { } r) return;
        void Front(object? sender, EventArgs e)
        {
            main.ContentRendered -= Front;
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            main.Topmost = true; // (over the other windows, whether or not Windows lets it take the focus)
            main.Activate();
            main.Topmost = false;
            Log.Write($"update: opened again in front (the window was {r.Window})");
        }
        main.ContentRendered += Front;
    }

    /// <summary>Where the window is: its place and what is opened on it, and how it stands.</summary>
    private static Updates.Resume Where(MainWindow main, Engine.Engine? e)
    {
        var r = new Updates.Resume();
        if (e is { Phase: Phase.Native })
        {
            r.Place = e.Nav.Current.Place.Name;
            r.Path = Updates.Kept(e.Nav.Current.Path);
        }
        var inUse = Application.Current?.Windows.OfType<Window>().Any(w => w.IsActive) == true;
        r.Window = !main.IsVisible ? "hidden" : main.WindowState == WindowState.Minimized || !inUse ? "behind" : "front";
        return r;
    }

    // ---- what holds an update back ----

    /// <summary>What the window has open that an update waits for (Updates.HeldBy asks; the quiz, Alta and a
    /// page of the school's in the window hold by themselves).</summary>
    private static Updates.Holding? Holding(MainWindow main, Engine.Engine? e, bool touring)
    {
        // (Simpl opening, a moment; the sign-in while it is on screen — finding the school, the card, "Logging you in",
        // the school's own page — not one left waiting in the notification area)
        if (e is { Phase: Phase.Starting }) return new Updates.Holding("Simpl opening", null);
        if (main.IsVisible && main.WindowState != WindowState.Minimized && (e == null || e.Login.Covers || e.Phase == Phase.Web))
            return new Updates.Holding("the sign-in", "when you’ve signed in");
        if (touring) return new Updates.Holding("the tour", "when the tour is over");
        if (Simpl.Screens.HandInSheet.Open) return new Updates.Holding("Hand In", "when you close Hand In");
        if (main.Sheets.Current is { HoldsUpdates: true }) return new Updates.Holding("a dialog", "when you close the dialog");
        foreach (var w in Application.Current.Windows.OfType<Window>())
        {
            if (w == main || !w.IsVisible) continue;
            if (w is SettingsWindow settings)
            {
                if (settings.Sheets.IsOpen) return new Updates.Holding("a dialog in Settings", "when you close the dialog in Settings");
                continue;
            }
            // (a popup floating over the window — Hand In, a tool, New message — by its name; What's New holds nothing)
            if (w is FloatPopup popup)
            {
                if (!popup.HoldsUpdates) continue;
                return new Updates.Holding($"a popup: {w.Title}", $"when you close {(w.Title == "Simpl" ? "the popup" : w.Title)}");
            }
            // (a page of the school's or a tool's in a window of its own, Alta's graph: by its title)
            var title = string.IsNullOrWhiteSpace(w.Title) || w.Title == "Simpl" ? "the page’s window" : $"“{w.Title}”";
            return new Updates.Holding($"a window: {w.Title}", $"when you close {title}");
        }
        if (Simpl.Screens.WorkPreview.IsOpen) return new Updates.Holding("the quick look", null);
        if (FilePreview.Shared.Status != null) return new Updates.Holding("a file on its way", null);
        if (MenuOpen()) return new Updates.Holding("a menu", null);
        return null;
    }

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);

    /// <summary>A menu, a flyout, a drop-down or the search's suggestions open (each a window of its own, drawn by WPF;
    /// a tooltip is not one): an update waits for it, and so does the live check (Shell/LiveCheck.cs).</summary>
    internal static bool MenuOpen()
    {
        foreach (var s in PresentationSource.CurrentSources.OfType<HwndSource>())
        {
            if (s.IsDisposed || s.RootVisual is Window or null || !IsWindowVisible(s.Handle)) continue;
            if (Tip(s.RootVisual)) continue;
            return true;
        }
        return false;
    }

    private static bool Tip(DependencyObject d, int depth = 0)
    {
        if (d is System.Windows.Controls.ToolTip) return true;
        if (depth > 4) return false;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d) && i < 4; i++) if (Tip(VisualTreeHelper.GetChild(d, i), depth + 1)) return true;
        return false;
    }

    // ---- the notice ----

    /// <summary>The notice at the top of the window: in the title bar's band where it has room, under it where it has not.</summary>
    private static void Fit(MainWindow main)
    {
        if (pill == null) return;
        var room = main.ActualWidth > 0 ? main.ActualWidth : main.Width;
        if (double.IsNaN(room) || room <= 0) room = 1024;
        var inBand = room - 580 >= 360;
        pill.Margin = new Thickness(0, inBand ? 7 : MainWindow.TitleBarHeight + 8, 0, 0);
        pill.MaxWidth = inBand ? room - 580 : Math.Max(200, room - 48);
    }

    /// <summary>
    /// What the notice says now: an update going in (until Simpl quits); one downloaded and waiting for something to
    /// close — a few seconds, once for each thing it waits for, or for as long as it waits when it is a Critical Update;
    /// a Critical Update that could not go in yet, a few seconds. Else nothing.
    /// </summary>
    private static void Say()
    {
        if (pill == null || words == null || mark == null) return;
        var v = Updates.Found?.Version ?? "";
        string? text = null;
        var stays = false;
        var critical = Updates.Critical;
        FrameworkElement icon = critical ? K.Icon(SymbolRegular.Warning24, 16, Color.FromRgb(0xe3, 0x6c, 0x0a), filled: true) : K.Icon(SymbolRegular.ArrowSync24, 16, B.AccentText);
        if (Updates.State == Updates.Mode.Installing)
        {
            text = critical ? $"Installing critical update {v} — Simpl opens again in a moment." : $"Updating Simpl to {v} — it opens again in a moment.";
            icon = K.Spinner(16);
            stays = true;
        }
        else if (Updates.State == Updates.Mode.Ready && Updates.Waiting is { When: { } when })
        {
            text = critical ? $"Critical update {v} installs {when}." : $"Simpl updates to {v} {when}.";
            stays = critical;
        }
        else if (critical && Updates.State == Updates.Mode.Failed && Updates.Found != null)
            text = $"Critical update {v} couldn’t be installed yet; Simpl tries again shortly.";

        if (text == null) { Hide(); return; }
        if (!stays && text == briefShown && pill.Visibility != Visibility.Visible) return; // (said once already)
        var again = pill.Visibility == Visibility.Visible && words.Text == text;
        words.Text = text;
        mark.Content = icon;
        if (!again)
        {
            pill.BeginAnimation(UIElement.OpacityProperty, null);
            pill.Opacity = 1;
            pill.Visibility = Visibility.Visible;
            Motion.Enter(pill, 0, -10, 333);
            Shot.Moved();
        }
        var mine = ++turn;
        if (stays || Shot.Asked) return;
        briefShown = text;
        _ = Later(TimeSpan.FromSeconds(8), () => { if (mine == turn) Hide(); });
    }

    private static void Hide()
    {
        if (pill is not { Visibility: Visibility.Visible }) return;
        var mine = ++turn;
        Motion.FadeOut(pill, () => { if (mine == turn && pill != null) pill.Visibility = Visibility.Collapsed; }, Motion.Normal);
    }

    private static async Task Later(TimeSpan wait, Action then)
    {
        await Task.Delay(wait);
        then();
    }

    // ---- an update played through (--update-test) ----

    /// <summary>
    /// For CI's run of an update played through: --update-test-close &lt;seconds&gt; closes what is open over the window
    /// (the quiz) after that long, as the student would; --update-test-tray closes the window to the notification area a
    /// moment after it opens, as its close button does.
    /// </summary>
    private static void Test(MainWindow main)
    {
        if (!Updates.Testing) return;
        if (Launch.Flag("update-test-tray"))
            _ = Later(TimeSpan.FromSeconds(2), () =>
            {
                Log.Write("update test: the window closed to the notification area");
                main.Close();
            });
        if (Launch.Number("update-test-close", 0) is > 0 and var after)
            _ = Later(TimeSpan.FromSeconds(after), () =>
            {
                if (main.Sheets.Current is not { } open) { Log.Write("update test: nothing open over the window to close"); return; }
                Log.Write("update test: closing what is open over the window");
                open.Close();
            });
    }
}
