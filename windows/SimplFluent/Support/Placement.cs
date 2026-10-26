using System.Globalization;
using System.Windows;

namespace Simpl.Support;

/// <summary>
/// Where a window opens and how large: as it was left (and maximized if it was), never larger than the screen's room
/// (the work area, the taskbar left out) nor off it; the first time, its usual size or the screen's room less a margin,
/// whichever is smaller, centred. The screenshot suite's --window-size is taken as it is.
/// </summary>
public static class Placement
{
    public static void Restore(Window w, string key, double width, double height, double minWidth, double minHeight)
    {
        w.MinWidth = minWidth;
        w.MinHeight = minHeight;
        var area = SystemParameters.WorkArea;
        if (Launch.Get("window-size") is { } asked && key == "main")
        {
            var p = asked.Split('x');
            if (p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var aw) && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ah))
            {
                w.Width = aw;
                w.Height = ah;
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }
        }
        var room = new Size(Math.Max(minWidth, area.Width - 48), Math.Max(minHeight, area.Height - 48));
        if (Saved(key) is { } r)
        {
            var wdt = Math.Min(Math.Max(r.Width, minWidth), room.Width);
            var hgt = Math.Min(Math.Max(r.Height, minHeight), room.Height);
            w.Width = wdt;
            w.Height = hgt;
            w.Left = Math.Clamp(r.X, area.Left, Math.Max(area.Left, area.Right - wdt));
            w.Top = Math.Clamp(r.Y, area.Top, Math.Max(area.Top, area.Bottom - hgt));
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            if (Prefs.Bool($"window:{key}:max")) w.Loaded += (_, _) => w.WindowState = WindowState.Maximized;
            return;
        }
        w.Width = Math.Min(width, room.Width);
        w.Height = Math.Min(height, room.Height);
        w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    public static void Save(Window w, string key)
    {
        if (!w.IsLoaded || Launch.Get("window-size") != null || w.WindowState == WindowState.Minimized) return;
        Prefs.Set($"window:{key}:max", w.WindowState == WindowState.Maximized ? true : null);
        if (w.WindowState != WindowState.Normal) return;
        var r = new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight);
        if (r.Width < 100 || double.IsNaN(r.X)) return;
        Prefs.Set($"window:{key}", string.Create(CultureInfo.InvariantCulture, $"{r.X:0},{r.Y:0},{r.Width:0},{r.Height:0}"));
    }

    private static Rect? Saved(string key)
    {
        var s = Prefs.String($"window:{key}");
        if (s == null) return null;
        var p = s.Split(',').Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToArray();
        return p.Length == 4 && p.All(d => !double.IsNaN(d)) ? new Rect(p[0], p[1], p[2], p[3]) : null;
    }
}
