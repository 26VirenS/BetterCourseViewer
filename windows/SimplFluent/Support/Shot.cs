using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Simpl.Support;

/// <summary>
/// The screenshot suite's picture (the Mac's Shot): with --shot file.png, once the window has settled — no answer asked
/// of the page for a moment — or after --shot-after seconds at most, the app's frontmost window is pictured into the
/// file with whatever floats over it (a menu, a flyout), and the app quits.
/// </summary>
public static class Shot
{
    private static int inFlight;
    private static DateTime lastActivity = DateTime.UtcNow;
    private static bool armed, everBegan;

    /// <summary>Something asked that the picture waits for (an answer from the page; a biology tool's 3D scene being made
    /// off the UI thread, BioSceneView.Show; its self-check, --bio-check): on the UI thread, with an Ended for each.</summary>
    public static void Began() { inFlight++; everBegan = true; lastActivity = DateTime.UtcNow; }

    public static void Ended() { inFlight = Math.Max(0, inFlight - 1); lastActivity = DateTime.UtcNow; }

    /// <summary>Something moved on screen (a step of the sign-in, a sheet coming up): the picture waits a moment more.</summary>
    public static void Moved() => lastActivity = DateTime.UtcNow;

    public static bool Asked => Launch.Get("shot") != null;

    /// <summary>A picture of a tool (--tool, --pin): the page's calls waiting for it to load count as asked
    /// (Engine.CallText), so the window round the tool — the account, the pane's courses — is read before it is pictured;
    /// on a slow start a biology tool was pictured with "Signed in · localhost" over an empty list. Other pictures keep
    /// their timing (some show what a slow start looks like).</summary>
    public static bool WaitsForPage => Asked && (Launch.Get("tool") != null || Launch.Get("pin") != null);

    public static void ArmIfAsked()
    {
        var file = Launch.Get("shot");
        if (file == null || armed) return;
        armed = true;
        var after = Launch.Number("shot-after", 14);
        var settle = Launch.Get("shot-settle") != "no";
        var start = DateTime.UtcNow;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        t.Tick += (_, _) =>
        {
            var since = (DateTime.UtcNow - start).TotalSeconds;
            // (settled: something was asked of the page, and nothing has been for a moment — never before the first ask)
            var quiet = everBegan && inFlight == 0 && (DateTime.UtcNow - lastActivity).TotalSeconds > 2.5;
            if (since < 3 || (since < after && !(settle && quiet && since > 8))) return;
            t.Stop();
            Log.Write($"picture at {since:0.0}s (in flight {inFlight}, quiet {quiet})");
            try { Take(file); } catch (Exception e) { Log.Write("picture failed: " + e); }
            Application.Current.Shutdown();
        };
        t.Start();
    }

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 r);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int L, T, R, B; }

    /// <summary>A window's pixels as Windows composes them (PW_RENDERFULLCONTENT: the web views' too), and where it is.</summary>
    private static (BitmapSource Image, Rect32 At)? Print(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var r);
        int width = r.R - r.L, height = r.B - r.T;
        if (width <= 0 || height <= 0) return null;
        var wdc = GetWindowDC(hwnd);
        var mdc = CreateCompatibleDC(wdc);
        var bmp = CreateCompatibleBitmap(wdc, width, height);
        var old = SelectObject(mdc, bmp);
        try
        {
            PrintWindow(hwnd, mdc, 2);
            SelectObject(mdc, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return (src, r);
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mdc);
            ReleaseDC(hwnd, wdc);
        }
    }

    private static bool FindTip(DependencyObject d)
    {
        if (d is System.Windows.Controls.ToolTip) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d) && i < 4; i++) if (FindTip(VisualTreeHelper.GetChild(d, i))) return true;
        return false;
    }

    /// <summary>The app's frontmost window (Settings, a popup window, else the main one) with its menus and flyouts over it.
    /// With none of them active — another copy of Simpl, started beside it, has the focus — the one opened last: a place
    /// in a window of its own has no owner, and would otherwise lose to the main window.</summary>
    public static void Take(string file)
    {
        // (a small window kept on top of the others — a tool pulled out of its pin, ToolFloat — or a popup floating over
        //  the window, Shell/FloatPopup.cs, is pictured over the window under it, never on its own — and the Periodic Table's
        //  mini element window over it in turn, as it is on the screen)
        static bool Floating(Window x) => x is Shell.FloatPopup || x is Simpl.Tools.PTMiniWindow || (x.Topmost && !x.ShowInTaskbar && x.Owner != null);
        var visible = Application.Current.Windows.OfType<Window>().Select((x, i) => (Window: x, Order: i)).Where(x => x.Window.IsVisible).ToList();
        var w = visible.Where(x => !Floating(x.Window))
                    .OrderByDescending(x => x.Window.IsActive).ThenByDescending(x => x.Window.Owner != null).ThenByDescending(x => x.Order)
                    .Select(x => x.Window).FirstOrDefault()
                ?? Application.Current.MainWindow;
        if (w == null) return;
        var hwnd = new WindowInteropHelper(w).Handle;
        if (Print(hwnd) is not { } main) return;
        var (image, at) = main;
        int width = at.R - at.L, height = at.B - at.T;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(image, new Rect(0, 0, width, height));
            foreach (var f in visible.Where(x => Floating(x.Window)).OrderBy(x => x.Order).Select(x => x.Window))
            {
                if (Print(new WindowInteropHelper(f).Handle) is not { } over) continue;
                var r = new Rect(over.At.L - at.L, over.At.T - at.T, over.At.R - over.At.L, over.At.B - over.At.T);
                if (f is not Shell.FloatPopup) { dc.DrawImage(over.Image, r); continue; }
                // (a popup as Windows draws it: its soft shadow, its rounded corners and its hairline — none of which
                //  PrintWindow gives)
                var radius = 8 * r.Width / Math.Max(1, f.ActualWidth); // (Windows 11's 8, at the display's scale)
                for (var i = 14; i >= 1; i--)
                {
                    var spread = new Rect(r.X - i, r.Y - i + 6, r.Width + 2 * i, r.Height + 2 * i);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(Appearance.Dark ? (byte)9 : (byte)5, 0, 0, 0)), null, spread, radius + i, radius + i);
                }
                dc.PushClip(new RectangleGeometry(r, radius, radius));
                dc.DrawImage(over.Image, r);
                dc.Pop();
                var hair = new Pen(new SolidColorBrush(Appearance.Dark ? Color.FromArgb(0x40, 255, 255, 255) : Color.FromArgb(0x26, 0, 0, 0)), 1);
                dc.DrawRoundedRectangle(null, hair, new Rect(r.X + 0.5, r.Y + 0.5, r.Width - 1, r.Height - 1), radius, radius);
            }
            // what floats over it: a menu, a flyout (each a window of its own, drawn by WPF — pictured as WPF draws it,
            // its see-through edges and shadow kept)
            foreach (var s in PresentationSource.CurrentSources.OfType<HwndSource>().ToList())
            {
                if (s.Handle == hwnd || s.IsDisposed || !IsWindowVisible(s.Handle) || s.RootVisual is Window or null) continue;
                if (s.RootVisual is FrameworkElement root && FindTip(root)) continue;
                GetWindowRect(s.Handle, out var r);
                int pw = r.R - r.L, ph = r.B - r.T;
                if (pw <= 0 || ph <= 0) continue;
                var m = s.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
                var rtb = new RenderTargetBitmap(pw, ph, 96 * m.M11, 96 * m.M22, PixelFormats.Pbgra32);
                rtb.Render(s.RootVisual);
                dc.DrawImage(rtb, new Rect(r.L - at.L, r.T - at.T, pw, ph));
            }
        }
        var all = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        all.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(all));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
