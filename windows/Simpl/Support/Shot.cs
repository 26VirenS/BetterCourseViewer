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
/// of the page for a moment — or after --shot-after seconds at most, the frontmost window of the app (with what is over
/// it) is pictured into the file, and the app quits. PrintWindow with full content takes the web views too.
/// </summary>
public static class Shot
{
    private static int inFlight;
    private static DateTime lastActivity = DateTime.UtcNow;
    private static bool armed;

    private static bool everBegan;

    public static void Began() { inFlight++; everBegan = true; lastActivity = DateTime.UtcNow; }

    public static void Ended() { inFlight = Math.Max(0, inFlight - 1); lastActivity = DateTime.UtcNow; }

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
            Take(file);
            Application.Current.Shutdown();
        };
        t.Start();
    }

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 r);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int L, T, R, B; }

    /// <summary>The app's own screens, as they are drawn (BGRA rows), while they are up: pictured from the renderer.</summary>
    public static Func<(byte[] Pixels, int Width, int Height)?>? Frame;

    /// <summary>The app's frontmost window (a tool's, a popup's, else the main one), pictured.</summary>
    public static void Take(string file)
    {
        if (Frame?.Invoke() is { } f)
        {
            var bmp0 = BitmapSource.Create(f.Width, f.Height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, f.Pixels, f.Width * 4);
            var enc0 = new PngBitmapEncoder();
            enc0.Frames.Add(BitmapFrame.Create(bmp0));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            using var fs0 = File.Create(file);
            enc0.Save(fs0);
            return;
        }
        var w = Application.Current.Windows.OfType<Window>().Where(x => x.IsVisible).OrderByDescending(x => x.IsActive).ThenByDescending(x => x.Topmost).FirstOrDefault()
                ?? Application.Current.MainWindow;
        if (w == null) return;
        var hwnd = new WindowInteropHelper(w).Handle;
        GetWindowRect(hwnd, out var r);
        int width = r.R - r.L, height = r.B - r.T;
        if (width <= 0 || height <= 0) return;
        var wdc = GetWindowDC(hwnd);
        var mdc = CreateCompatibleDC(wdc);
        var bmp = CreateCompatibleBitmap(wdc, width, height);
        var old = SelectObject(mdc, bmp);
        PrintWindow(hwnd, mdc, 2); // PW_RENDERFULLCONTENT: the web views' content too
        SelectObject(mdc, old);
        try
        {
            BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // the app's own windows kept over this one (the sign-in's note over the school's page): drawn on, where they are
            var overlays = Application.Current.Windows.OfType<Window>().Where(o => o != w && o.IsVisible && o.Owner == w && o.AllowsTransparency && o.Content is Visual).ToList();
            if (overlays.Count > 0)
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawImage(src, new Rect(0, 0, width, height));
                    foreach (var o in overlays)
                    {
                        var m = PresentationSource.FromVisual(o)?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
                        var pw = (int)Math.Ceiling(o.ActualWidth * m.M11);
                        var ph = (int)Math.Ceiling(o.ActualHeight * m.M22);
                        if (pw <= 0 || ph <= 0) continue;
                        var rtb = new RenderTargetBitmap(pw, ph, 96 * m.M11, 96 * m.M22, System.Windows.Media.PixelFormats.Pbgra32);
                        rtb.Render((Visual)o.Content);
                        var at = o.PointToScreen(new Point(0, 0));
                        dc.DrawImage(rtb, new Rect(at.X - r.L, at.Y - r.T, pw, ph));
                    }
                }
                var all = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                all.Render(dv);
                src = all;
            }
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            using var fs = File.Create(file);
            enc.Save(fs);
        }
        finally
        {
            DeleteObject(bmp);
            DeleteDC(mdc);
            ReleaseDC(hwnd, wdc);
        }
    }
}
