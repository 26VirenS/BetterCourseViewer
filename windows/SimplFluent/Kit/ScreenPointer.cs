using System.Runtime.InteropServices;
using System.Windows;

namespace Simpl.Kit;

/// <summary>
/// Where the pointer is on the screen, from Windows itself — not from WPF's last mouse event, which a window that has
/// not had one yet answers with the element's own corner, so that a list or a table moving under a still pointer seems
/// to have the pointer moving with it. What chooses a row or an element only on the pointer's own movement (the
/// periodic table's hover card, the school picker's rows) asks here.
/// </summary>
internal static class ScreenPointer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);

    /// <summary>The pointer's place in screen pixels, or none where Windows cannot say.</summary>
    public static Point? At()
    {
        try { return GetCursorPos(out var p) ? new Point(p.X, p.Y) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>Whether the pointer has moved since <paramref name="last"/> (which it then holds): never the first time
    /// it is asked, so a pointer that was already there when something appeared under it chooses nothing.</summary>
    public static bool Moved(ref Point? last)
    {
        if (At() is not { } at) return true;
        var moved = last is { } was && (Math.Abs(was.X - at.X) > 0.5 || Math.Abs(was.Y - at.Y) > 0.5);
        last = at;
        return moved;
    }
}
