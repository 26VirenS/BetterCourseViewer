using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GlassShowcase;

/// <summary>A picture of a window as it is on screen (PrintWindow with full content: effects and all), for CI.</summary>
public static class Snapshot
{
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out R r);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [StructLayout(LayoutKind.Sequential)] private struct R { public int L, T, Rt, B; }

    public static void Take(Window w, string file)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        GetWindowRect(hwnd, out var r);
        int width = r.Rt - r.L, height = r.B - r.T;
        var wdc = GetWindowDC(hwnd);
        var mdc = CreateCompatibleDC(wdc);
        var bmp = CreateCompatibleBitmap(wdc, width, height);
        var old = SelectObject(mdc, bmp);
        PrintWindow(hwnd, mdc, 2);
        SelectObject(mdc, old);
        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
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
