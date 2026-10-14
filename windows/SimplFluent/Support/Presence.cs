using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Simpl.Support;

/// <summary>
/// Whether anyone can be in front of Simpl's window, past its own state: the PC locked (or its session disconnected), or
/// the display off — when a window still showing is as good as away. The live check pauses then as it does for a
/// minimized window (and checks at once when the student is back after more than a minute), and the memory given back
/// while minimized is given back then too; a Dashboard left up on a locked PC overnight no longer reads the school every
/// minute. Watched from the main window's making: the session's lock from Windows' session events, the display from its
/// power notifications (GUID_CONSOLE_DISPLAY_STATE: off, on or dimmed — dimmed is still in front of someone).
/// </summary>
public static class Presence
{
    private const int WM_POWERBROADCAST = 0x0218, PBT_POWERSETTINGCHANGE = 0x8013;
    private static Guid displayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public uint Data;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);

    private static bool locked, displayOff, watching;

    /// <summary>Nobody can be at the window: the PC locked, or its display off.</summary>
    public static bool Away => locked || displayOff;

    /// <summary>The PC locked or unlocked, the display off or on again (raised on the window's thread).</summary>
    public static event Action? Changed;

    /// <summary>Watched from the main window: its handle for the display's notifications, once it has one.</summary>
    public static void Watch(Window main)
    {
        // (not in a picture run: nobody sits at a runner, whose display may say it is off — its pictures and their live
        //  checks are of what the window shows)
        if (watching || Shot.Asked) return;
        watching = true;
        try { SystemEvents.SessionSwitch += OnSession; }
        catch (Exception e) { Log.Write("presence: the session's lock is not watched: " + e.Message); }
        if (PresentationSource.FromVisual(main) is HwndSource ready) Hook(ready);
        else main.SourceInitialized += (_, _) => { if (PresentationSource.FromVisual(main) is HwndSource src) Hook(src); };
    }

    private static void Hook(HwndSource src)
    {
        src.AddHook(OnMessage);
        // (Windows says the display's state at once on registering, then whenever it changes)
        if (RegisterPowerSettingNotification(src.Handle, ref displayState, 0) == IntPtr.Zero) Log.Write("presence: the display's state is not watched");
    }

    private static void OnSession(object sender, SessionSwitchEventArgs e)
    {
        var now = e.Reason switch
        {
            SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect => true,
            SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect => false,
            _ => locked,
        };
        // (Windows' session events come on a thread of their own)
        Application.Current?.Dispatcher.BeginInvoke(() => Set(ref locked, now));
    }

    private static IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_POWERBROADCAST || wParam.ToInt64() != PBT_POWERSETTINGCHANGE || lParam == IntPtr.Zero) return IntPtr.Zero;
        var s = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
        if (s.PowerSetting == displayState) Set(ref displayOff, s.Data == 0);
        return IntPtr.Zero;
    }

    private static void Set(ref bool field, bool value)
    {
        if (field == value) return;
        field = value;
        Log.Write(Away ? $"presence: away ({(locked ? "locked" : "the display is off")})" : "presence: back");
        Changed?.Invoke();
    }
}
