using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;

namespace Simpl.Shell;

/// <summary>
/// Simpl kept on the taskbar, one click away (as a Mac app is kept in the Dock): asked once, the third time Simpl is
/// opened, at a calm moment — not in the middle of the first run, straight after a setup and a tour — and any time from
/// Settings ▸ General's Pin to taskbar…. Nothing is asked when Simpl is pinned already. For the installed copy, where
/// Windows lets an app ask for itself (TaskbarManager), Windows' own question comes up; elsewhere — a copy run from its
/// zip, a PC whose Windows does not ask — a dialog shows where Pin to taskbar is, drawn, while Simpl's taskbar button
/// flashes until it is closed.
/// </summary>
public static class Taskbar
{
    /// <summary>What came of asking.</summary>
    public enum Answer { AlreadyPinned, Pinned, NotPinned, Shown }

    private const string AskedKey = "taskbar:asked";
    /// <summary>How many times Simpl has been opened: started with its screens up, or brought back from the
    /// notification area (where it keeps running, so a start alone would be rare).</summary>
    private const string OpensKey = "taskbar:opens";
    /// <summary>The opening that asks (onboarding-4: "for example on the 3rd launch").</summary>
    private const int AskOnOpen = 3;
    private static bool waiting;

    /// <summary>
    /// Simpl opened (its screens up at a start, or its window brought back): counted, and from the third on, asked once
    /// — a minute in, when nothing is over the window (<paramref name="busy"/>: a dialog, the tour, the sign-in) and the
    /// window is shown and in front. Never in a development run or a picture.
    /// </summary>
    public static async void Opened(MainWindow window, Func<bool> busy)
    {
        if (Prefs.Bool(AskedKey) || Shot.Asked || Launch.IsDevelopment) return;
        var opens = (int)Prefs.Double(OpensKey) + 1;
        Prefs.Set(OpensKey, (double)opens);
        if (opens < AskOnOpen || waiting) return;
        waiting = true;
        await Task.Delay(TimeSpan.FromMinutes(1));
        // (a calm moment: nothing over the window, the window shown and in front — looked for every few seconds, for as
        //  long as Simpl runs)
        while (busy() || !window.IsVisible || !window.IsActive || window.WindowState == WindowState.Minimized)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (Prefs.Bool(AskedKey)) return;
        }
        waiting = false;
        if (Prefs.Bool(AskedKey)) return;
        Prefs.Set(AskedKey, true);
        try { await Ask(window); }
        catch (Exception e) { Log.Write("taskbar: " + e.Message); }
    }

    /// <summary>--open taskbar (the screenshot suite): the dialog, once the window has settled — the picture waits for it.</summary>
    public static async void ShowIfAsked(MainWindow window)
    {
        if (Launch.Take("taskbar") == null) return;
        Shot.Began();
        try
        {
            await Task.Delay(3000);
            _ = ShowSheet(window);
        }
        finally { Shot.Ended(); }
    }

    /// <summary>
    /// Pinned already; else, for the copy the installer put here, Windows' own question where it lets an app ask (it
    /// needs a Start menu entry carrying Simpl's app id, which a copy run from its zip has not); else Simpl's dialog.
    /// <paramref name="asked"/>: the student asked (Settings ▸ General's Pin to taskbar…), so Windows' No is not the
    /// last word — it may be Windows declining to ask at all — and Simpl's dialog follows it.
    /// </summary>
    public static async Task<Answer> Ask(Window over, bool asked = false)
    {
        if (IsPinned()) return Answer.AlreadyPinned;
        if (Updates.Installed && await AskWindows() is { } pinned)
        {
            if (pinned) return Answer.Pinned;
            if (!asked) return Answer.NotPinned; // (asked by itself: the student's No, not asked twice)
        }
        await ShowSheet(over);
        return IsPinned() ? Answer.Pinned : Answer.Shown;
    }

    /// <summary>
    /// Windows' own "Pin Simpl to the taskbar?", where it lets an app ask: true pinned, false declined, null where it
    /// does not (an app not from the Store, unless Microsoft allows it; not in front; no Start menu entry) or cannot say.
    /// </summary>
    private static async Task<bool?> AskWindows()
    {
        try
        {
            var bar = global::Windows.UI.Shell.TaskbarManager.GetDefault();
            if (bar == null || !bar.IsSupported || !bar.IsPinningAllowed) return null;
            if (await bar.IsCurrentAppPinnedAsync()) return true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (await bar.RequestPinCurrentAppAsync()) return true;
            // (Windows turns a request it will not ask down at once, with no question shown and no error — a No that
            // quick is not the student's, so Simpl's dialog shows instead)
            if (clock.ElapsedMilliseconds < 750)
            {
                Log.Write($"taskbar: Windows did not ask ({clock.ElapsedMilliseconds} ms)");
                return null;
            }
            return false;
        }
        catch (Exception e)
        {
            Log.Write("taskbar: Windows' own question is not there: " + e.Message);
            return null;
        }
    }

    // ---- Simpl's dialog ----

    /// <summary>"Keep Simpl on your taskbar?": where Pin to taskbar is, drawn; Simpl's taskbar button flashing until it closes.</summary>
    public static Task ShowSheet(Window over)
    {
        var closed = new TaskCompletionSource();
        if ((over as ISheetWindow)?.Sheets is not { } host)
        {
            closed.TrySetResult();
            return closed.Task;
        }
        var s = new Sheet
        {
            Title = "Keep Simpl on your taskbar?",
            Body = K.V(16, Picture(), K.Wrapped("Right-click Simpl’s icon in the taskbar — it’s flashing now — and choose Pin to taskbar.", Ty.Body)),
        };
        s.Add("Done", SheetButtonKind.Primary);
        s.Add("Not now", SheetButtonKind.Close);
        // (the main window's button is Simpl's on the taskbar, whichever window the dialog is over: it flashes while the
        // dialog shows — not while it waits behind another; and once Simpl is pinned, the dialog has done its work and
        // goes by itself)
        var button = Application.Current?.MainWindow;
        DispatcherTimer? watch = null;
        var flashing = false;
        EventHandler? overClosed = null;
        // (one ending, whichever way it comes: the flashing and the watching stopped, the caller told — once)
        void Ended()
        {
            watch?.Stop();
            if (flashing)
            {
                flashing = false;
                Flash(button, false);
            }
            if (overClosed != null)
            {
                over.Closed -= overClosed;
                overClosed = null;
            }
            closed.TrySetResult();
        }
        void FlashIfShown()
        {
            if (flashing || watch == null || host.Current != s) return;
            flashing = true;
            Flash(button, true);
        }
        s.Closed += Ended;
        // (the window under it closed with the dialog still up — Alt+F4 on Settings: the sheet never says it closed)
        overClosed = (_, _) => Ended();
        over.Closed += overClosed;
        if (!Shot.Asked)
        {
            watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            watch.Tick += (_, _) =>
            {
                if (!over.IsVisible) return; // (its window hidden in the notification area: nothing to look at till it is back)
                if (!IsPinned())
                {
                    FlashIfShown();
                    return;
                }
                watch.Stop();
                if (host.Current == s) s.Close();
                else
                {
                    // (still waiting its turn behind another dialog: taken out of the line, never to show)
                    host.Close(s);
                    Ended();
                }
            };
            watch.Start();
        }
        host.Show(s);
        FlashIfShown();
        return closed.Task;
    }

    private static BitmapImage? logo;

    private static BitmapImage Logo
    {
        get
        {
            if (logo != null) return logo;
            logo = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png"));
            logo.Freeze();
            return logo;
        }
    }

    /// <summary>
    /// A strip of Windows 11's taskbar, drawn in the theme: Start, Search, two apps and Simpl — its button lit, as it is
    /// while it flashes — and over it Simpl's right-click menu, Pin to taskbar highlighted under the pointer.
    /// </summary>
    private static FrameworkElement Picture()
    {
        const double W = 398, H = 186, Bar = 48, Slot = 40, Gap = 4, MenuW = 208, Item = 32, Rule = 9, Pad = 4;
        var accent = Appearance.Accent;
        // the desktop: the card's fill with the accent blooming across it
        var c = new Canvas
        {
            Width = W, Height = H,
            Background = new LinearGradientBrush(K.WithAlpha(accent, 0.32), K.WithAlpha(accent, 0.04), new Point(0, 0), new Point(1, 1)),
            Clip = new RectangleGeometry(new Rect(0, 0, W, H), 7, 7),
        };

        // the taskbar, its icons in the middle (Windows 11's)
        var strip = new Border { Width = W, Height = Bar, BorderThickness = new Thickness(0, 1, 0, 0) }.Bg(B.Solid);
        strip.SetResourceReference(Border.BorderBrushProperty, B.CardStroke);
        Canvas.SetTop(strip, H - Bar);
        c.Children.Add(strip);
        const int slots = 5;
        var left = (W - (slots * Slot + (slots - 1) * Gap)) / 2;
        double SlotX(int i) => left + i * (Slot + Gap);
        Grid Place(int i, params UIElement[] kids)
        {
            var g = new Grid { Width = Slot, Height = Slot };
            foreach (var k in kids) g.Children.Add(k);
            Canvas.SetLeft(g, SlotX(i));
            Canvas.SetTop(g, H - Bar + (Bar - Slot) / 2);
            c.Children.Add(g);
            return g;
        }
        Place(0, StartMark(accent));
        Place(1, K.Icon(SymbolRegular.Search24, 18, B.Text2));
        Place(2, K.Icon(SymbolRegular.Folder24, 20, Color.FromRgb(0xE0, 0xA1, 0x1B)));
        Place(3, K.Icon(SymbolRegular.Globe24, 20, B.Text2));
        // Simpl: lit, as its button is while it flashes; the pill under it says it is open
        var lit = new Border { CornerRadius = new CornerRadius(4), Background = K.Brush(K.WithAlpha(accent, 0.26)) };
        var pill = new Border
        {
            Width = 16, Height = 3, CornerRadius = new CornerRadius(1.5), Background = K.Brush(accent),
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1),
        };
        Place(4, lit, new Image { Source = Logo, Width = 24, Height = 24 }, pill);
        if (Motion.On)
            lit.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.35, Motion.Ms(650))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Motion.Standard,
            });

        // Simpl's right-click menu over its button: its name, Pin to taskbar (highlighted), Close window
        var menu = new Border
        {
            Width = MenuW, CornerRadius = new CornerRadius(8), Padding = new Thickness(Pad), Effect = K.Shadow(16, 4, 0.18),
            Child = K.V(0,
                MenuRow(new Image { Source = Logo, Width = 16, Height = 16 }, "Simpl", false),
                new Border { Height = 1, Margin = new Thickness(8, (Rule - 1) / 2, 8, (Rule - 1) / 2) }.Bg(B.Divider),
                MenuRow(K.Icon(SymbolRegular.Pin20, 16, B.AccentText), "Pin to taskbar", true),
                MenuRow(K.Icon(SymbolRegular.Dismiss20, 16, B.Text), "Close window", false)),
        }.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        const double menuH = 1 + Pad + Item + Rule + Item + Item + Pad + 1;
        var menuLeft = Math.Min(W - MenuW - 8, SlotX(4) + Slot / 2 - MenuW / 2);
        var menuTop = H - Bar - 8 - menuH;
        Canvas.SetLeft(menu, menuLeft);
        Canvas.SetTop(menu, menuTop);
        c.Children.Add(menu);

        // the pointer, on Pin to taskbar
        var pointer = new Path
        {
            Data = Geometry.Parse("M0,0 L0,16.5 L4.2,12.6 L7,19 L9.6,17.9 L6.9,11.6 L12.2,11.6 Z"),
            Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, StrokeLineJoin = PenLineJoin.Round,
        };
        Canvas.SetLeft(pointer, menuLeft + 150);
        Canvas.SetTop(pointer, menuTop + 1 + Pad + Item + Rule + Item / 2 - 3);
        c.Children.Add(pointer);

        var frame = new Drawing { Width = W + 2, Height = H + 2, CornerRadius = new CornerRadius(8), Child = c, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(frame, "Simpl’s taskbar button, right-clicked, with Pin to taskbar chosen");
        return frame.Bg(B.Card2).Stroke(B.CardStroke);
    }

    /// <summary>
    /// The drawn taskbar, to a screen reader: one picture, named — not a menu whose words and icons it would read out as
    /// if they could be chosen.
    /// </summary>
    private sealed class Drawing : Border
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

        private sealed class Peer : FrameworkElementAutomationPeer
        {
            public Peer(Drawing d) : base(d) { }
            protected override List<AutomationPeer>? GetChildrenCore() => null;
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
            protected override string GetClassNameCore() => nameof(Drawing);
        }
    }

    /// <summary>Windows 11's Start: four squares, in the accent.</summary>
    private static FrameworkElement StartMark(Color accent)
    {
        var g = new Grid { Width = 18, Height = 18 };
        for (var i = 0; i < 4; i++)
            g.Children.Add(new Rectangle
            {
                Width = 8.4, Height = 8.4, RadiusX = 1.2, RadiusY = 1.2, Fill = K.Brush(accent),
                HorizontalAlignment = i % 2 == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = i < 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            });
        return g;
    }

    /// <summary>A row of the drawn menu: its symbol and words; the one to choose tinted in the accent.</summary>
    private static FrameworkElement MenuRow(FrameworkElement icon, string text, bool chosen)
    {
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var row = new Border
        {
            Height = 32, CornerRadius = new CornerRadius(4),
            Child = K.Columns("36, *", icon.Col(0), K.Text(text, Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1)),
        };
        if (chosen) row.Background = K.Brush(K.WithAlpha(Appearance.Accent, 0.18));
        return row;
    }

    // ---- the button's flashing ----

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    private const uint FLASHW_STOP = 0, FLASHW_TRAY = 0x2, FLASHW_TIMER = 0x4;

    /// <summary>
    /// A window's taskbar button flashing, until it is stopped. (FLASHW_TIMER, not FLASHW_TIMERNOFG: that one ends as
    /// soon as the window is in front — and it is, the dialog being in it.)
    /// </summary>
    private static void Flash(Window? w, bool on)
    {
        if (w == null) return;
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        var info = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = hwnd, dwFlags = on ? FLASHW_TRAY | FLASHW_TIMER : FLASHW_STOP };
        FlashWindowEx(ref info);
    }

    // ---- pinned already? ----

    /// <summary>Simpl is pinned: among the taskbar's pinned shortcuts, one that opens this very Simpl.exe.</summary>
    public static bool IsPinned()
    {
        try
        {
            var exe = Environment.ProcessPath;
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (exe == null || !System.IO.Directory.Exists(dir)) return false;
            foreach (var lnk in System.IO.Directory.EnumerateFiles(dir, "*.lnk"))
                if (Target(lnk) is { } target && SameFile(target, exe)) return true;
        }
        catch (Exception e) { Log.Write("taskbar: " + e.Message); }
        return false;
    }

    /// <summary>What a shortcut opens, as Windows' shell reads it (IShellLinkW), or null.</summary>
    private static string? Target(string lnk)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(lnk, 0); // (STGM_READ)
            var path = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(path, path.Capacity, IntPtr.Zero, 0);
            return path.Length > 0 ? Environment.ExpandEnvironmentVariables(path.ToString()) : null;
        }
        catch { return null; }
        finally { if (link != null) Marshal.FinalReleaseComObject(link); }
    }

    private static bool SameFile(string a, string b)
    {
        try { return string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>The shell's shortcut object (CLSID_ShellLink).</summary>
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    /// <summary>IShellLinkW, in its order (only GetPath is called).</summary>
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
