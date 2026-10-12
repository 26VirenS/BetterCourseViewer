using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Simpl.Support;

namespace Simpl.Theme;

/// <summary>
/// The app's colours, light and dark (the Mac's Theme): a quiet page, cards lifted off it, a hairline edge, the accent.
/// They are resources every screen reads with DynamicResource, so light and dark change everywhere at once — following
/// Windows' own setting (or --appearance light/dark), as the Mac follows its own.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    private static Color Rgb(byte r, byte g, byte b, double a = 1) => Color.FromArgb((byte)Math.Round(a * 255), r, g, b);

    public static void Apply()
    {
        var forced = Launch.Get("appearance") ?? Prefs.String("appearance");
        IsDark = forced switch { "dark" => true, "light" => false, _ => SystemIsDark() };
        var res = Application.Current.Resources;
        void Brush(string key, Color light, Color dark)
        {
            var b = new SolidColorBrush(IsDark ? dark : light);
            b.Freeze();
            res[key] = b;
            res[key + "Color"] = IsDark ? dark : light;
        }
        Brush("Page", Rgb(245, 245, 247), Rgb(22, 22, 24));
        Brush("Card", Rgb(255, 255, 255), Rgb(36, 36, 38));
        Brush("CardHover", Rgb(250, 250, 252), Rgb(44, 44, 47));
        Brush("Well", Rgb(118, 118, 128, 0.08), Rgb(118, 118, 128, 0.2));
        Brush("WellStrong", Rgb(118, 118, 128, 0.14), Rgb(118, 118, 128, 0.3));
        Brush("Edge", Rgb(0, 0, 0, 0.06), Rgb(255, 255, 255, 0.08));
        Brush("EdgeStrong", Rgb(0, 0, 0, 0.12), Rgb(255, 255, 255, 0.16));
        Brush("Shadow", Rgb(0, 0, 0, 0.07), Rgb(0, 0, 0, 0.35));
        Brush("Text", Rgb(0, 0, 0, 0.88), Rgb(255, 255, 255, 0.92));
        Brush("Text2", Rgb(60, 60, 67, 0.62), Rgb(235, 235, 245, 0.62));
        Brush("Text3", Rgb(60, 60, 67, 0.32), Rgb(235, 235, 245, 0.32));
        Brush("Accent", Rgb(0, 122, 255), Rgb(10, 132, 255));
        Brush("AccentSoft", Rgb(0, 122, 255, 0.12), Rgb(10, 132, 255, 0.22));
        Brush("OnAccent", Rgb(255, 255, 255), Rgb(255, 255, 255));
        Brush("Red", Rgb(255, 59, 48), Rgb(255, 69, 58));
        Brush("Orange", Rgb(255, 149, 0), Rgb(255, 159, 10));
        Brush("Green", Rgb(52, 199, 89), Rgb(48, 209, 88));
        Brush("Sidebar", Rgb(240, 240, 243, 0.72), Rgb(30, 30, 32, 0.72));
        Brush("SidebarSelected", Rgb(0, 0, 0, 0.08), Rgb(255, 255, 255, 0.1));
        Brush("Glass", Rgb(255, 255, 255, 0.62), Rgb(58, 58, 62, 0.55));
        Brush("GlassEdge", Rgb(255, 255, 255, 0.85), Rgb(255, 255, 255, 0.14));
        Brush("Scrim", Rgb(0, 0, 0, 0.18), Rgb(0, 0, 0, 0.45));
        foreach (Window w in Application.Current.Windows) Frame(w);
        Changed?.Invoke();
    }

    /// <summary>Windows' own light or dark ("Choose your app mode").</summary>
    public static bool SystemIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>Following Windows as it changes.</summary>
    public static void Follow()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) Application.Current.Dispatcher.BeginInvoke(Apply);
        };
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>
    /// A window's frame in the app's light or dark, and (Windows 11) Mica behind it — the sidebar's frosted ground, as
    /// the Mac's sidebar is translucent. Windows 10 keeps its plain frame.
    /// </summary>
    public static void Frame(Window w, bool mica = false)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        if (mica && Environment.OSVersion.Version.Build >= 22000)
        {
            var backdrop = 2; // DWMSBT_MAINWINDOW (Mica)
            DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)); // DWMWA_SYSTEMBACKDROP_TYPE
        }
        var corner = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
    }

    public static bool HasMica => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>A colour from the page's hex ("#34c759"), or the fallback.</summary>
    public static Color Hex(string? hex, Color? fallback = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex)) return (Color)ColorConverter.ConvertFromString(hex.StartsWith('#') ? hex : "#" + hex);
        }
        catch { }
        return fallback ?? Color.FromRgb(142, 142, 147);
    }

    public static SolidColorBrush HexBrush(string? hex, double opacity = 1)
    {
        var c = Hex(hex);
        var b = new SolidColorBrush(Color.FromArgb((byte)(c.A * opacity), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
