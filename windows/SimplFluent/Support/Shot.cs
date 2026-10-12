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

    public static void Began() { inFlight++; everBegan = true; lastActivity = DateTime.UtcNow; }

    public static void Ended() { inFlight = Math.Max(0, inFlight - 1); lastActivity = DateTime.UtcNow; }

    /// <summary>Something moved on screen (a step of the sign-in, a sheet coming up): the picture waits a moment more.</summary>
    public static void Moved() => lastActivity = DateTime.UtcNow;

    public static bool Asked => Launch.Get("shot") != null;

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

    /// <summary>The app's frontmost window (Settings, a popup window, else the main one) with its menus and flyouts over it.</summary>
    public static void Take(string file)
    {
        var w = Application.Current.Windows.OfType<Window>().Where(x => x.IsVisible).OrderByDescending(x => x.IsActive).ThenByDescending(x => x.Owner != null).FirstOrDefault()
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
            // what floats over it: a menu, a flyout, a tooltip (each a window of its own)
            foreach (var s in PresentationSource.CurrentSources.OfType<HwndSource>().ToList())
            {
                if (s.Handle == hwnd || s.IsDisposed || !IsWindowVisible(s.Handle) || s.RootVisual is Window) continue;
                if (Print(s.Handle) is not { } p) continue;
                dc.DrawImage(p.Image, new Rect(p.At.L - at.L, p.At.T - at.T, p.At.R - p.At.L, p.At.B - p.At.T));
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
