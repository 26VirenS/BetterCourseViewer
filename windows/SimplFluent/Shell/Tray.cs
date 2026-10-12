using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Simpl.Support;

namespace Simpl.Shell;

/// <summary>
/// Simpl's icon in the notification area (the Mac's Dock icon, for an app that keeps running with its window closed):
/// a click brings the window back, a right-click offers Open, Settings and Quit (Windows' own menu), and Simpl's
/// notifications — a reminder, new activity — come from it as Windows' own, the click on one opening what it is about.
/// </summary>
public sealed class Tray : IDisposable
{
    private const int WM_APP = 0x8000, WM_TRAY = WM_APP + 1, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B;
    private const int NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401, NIN_BALLOONUSERCLICK = 0x0405;
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const int NIIF_USER = 4, NIIF_LARGE_ICON = 0x20;
    private const uint TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002, MF_STRING = 0, MF_SEPARATOR = 0x800, MF_DEFAULT = 0x1000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, int n);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPos);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr p);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    private readonly HwndSource window;
    private readonly IntPtr icon, bigIcon;
    private bool added;
    private Action? clickNote;

    public event Action? Open;
    public event Action? Settings;
    public event Action? Quit;

    public Tray()
    {
        // a window of its own, never shown, for the notification area's messages
        window = new HwndSource(new HwndSourceParameters("Simpl tray") { Width = 0, Height = 0, WindowStyle = 0 });
        window.AddHook(Hook);
        ExtractIconEx(Environment.ProcessPath ?? "", 0, out bigIcon, out icon, 1);
        Add();
    }

    private NOTIFYICONDATA Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = window.Handle, uID = 1, uFlags = flags, uCallbackMessage = WM_TRAY,
        hIcon = icon, szTip = "Simpl", szInfo = "", szInfoTitle = "",
    };

    private void Add()
    {
        var d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        added = Shell_NotifyIcon(NIM_ADD, ref d);
        d.uVersion = 4;
        Shell_NotifyIcon(NIM_SETVERSION, ref d);
    }

    /// <summary>A Windows notification from Simpl: its title and words; a click on it does what it is about.</summary>
    public void Notify(string title, string text, Action? click = null)
    {
        if (!added) return;
        clickNote = click;
        var d = Data(NIF_INFO | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        d.szInfoTitle = Clip(title, 63);
        d.szInfo = Clip(string.IsNullOrEmpty(text) ? " " : text, 255);
        d.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
        d.hBalloonIcon = bigIcon;
        Shell_NotifyIcon(NIM_MODIFY, ref d);
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAY)
        {
            var ev = (int)(lParam.ToInt64() & 0xFFFF);
            switch (ev)
            {
                case WM_LBUTTONUP or NIN_SELECT or NIN_KEYSELECT: Open?.Invoke(); break;
                case WM_RBUTTONUP or WM_CONTEXTMENU: Menu(); break;
                case NIN_BALLOONUSERCLICK: { var a = clickNote; clickNote = null; Open?.Invoke(); a?.Invoke(); break; }
            }
            handled = true;
        }
        else if (msg == RegisterTaskbarCreated)
        {
            // (Explorer started again: the icon put back)
            Add();
        }
        return IntPtr.Zero;
    }

    private static readonly int RegisterTaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string s);

    /// <summary>Windows' own menu at the pointer: Open Simpl (the default), Settings…, Quit Simpl.</summary>
    private void Menu()
    {
        var m = CreatePopupMenu();
        AppendMenu(m, MF_STRING, (UIntPtr)1, "Open Simpl");
        AppendMenu(m, MF_STRING, (UIntPtr)2, "Settings…");
        AppendMenu(m, MF_SEPARATOR, UIntPtr.Zero, null);
        AppendMenu(m, MF_STRING, (UIntPtr)3, "Quit Simpl");
        SetMenuDefaultItem(m, 1, 0);
        GetCursorPos(out var p);
        SetForegroundWindow(window.Handle);
        var pick = TrackPopupMenuEx(m, TPM_RETURNCMD | TPM_RIGHTBUTTON, p.X, p.Y, window.Handle, IntPtr.Zero);
        PostMessage(window.Handle, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(m);
        switch (pick)
        {
            case 1: Open?.Invoke(); break;
            case 2: Settings?.Invoke(); break;
            case 3: Quit?.Invoke(); break;
        }
    }

    public void Dispose()
    {
        if (added)
        {
            var d = Data(0);
            Shell_NotifyIcon(NIM_DELETE, ref d);
            added = false;
        }
        if (icon != IntPtr.Zero) DestroyIcon(icon);
        if (bigIcon != IntPtr.Zero) DestroyIcon(bigIcon);
        try { window.Dispose(); } catch { }
    }
}
