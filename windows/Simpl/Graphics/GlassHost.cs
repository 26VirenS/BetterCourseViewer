using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Simpl.UI;
using Vortice.Mathematics;

namespace Simpl.Graphics;

/// <summary>
/// The app's own screens on the window: a child window over the engine's web view, drawn by Simpl's renderer (Direct3D
/// 11 and Direct2D, with Simpl's glass) at the display's refresh rate while anything moves, and not at all while
/// nothing does. The pointer and the keys go to the screens (<see cref="Ui"/>), built afresh each frame by
/// <see cref="Build"/>.
/// </summary>
public sealed class GlassHost : HwndHost
{
    public readonly Ui Ui = new();
    /// <summary>The window's screens, a frame of them.</summary>
    public Action<Ui>? Build;
    /// <summary>The colour under everything.</summary>
    public Func<Color4> Ground = () => new Color4(0.08f, 0.08f, 0.09f, 1);
    public Gpu? Gpu { get; private set; }
    public string? Failure { get; private set; }

    private IntPtr hwnd;
    private bool scheduled, dirty = true, tracking;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double last;
    private int failures;
    private static WndProcDelegate? classProc; // (kept alive: the window class points at it)

    /// <summary>Something the screens show changed: a frame soon.</summary>
    public void Invalidate()
    {
        dirty = true;
        Schedule();
    }

    private void Schedule()
    {
        if (scheduled || hwnd == IntPtr.Zero) return;
        scheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, Frame);
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        const string cls = "SimplGlass";
        if (classProc == null)
        {
            classProc = DefWindowProcW;
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = 0x0008 /* CS_DBLCLKS */,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(classProc),
                hInstance = GetModuleHandleW(null),
                hCursor = LoadCursorW(IntPtr.Zero, 32512 /* IDC_ARROW */),
                lpszClassName = cls,
            };
            RegisterClassExW(ref wc);
        }
        hwnd = CreateWindowExW(0, cls, "", 0x40000000 /* WS_CHILD */ | 0x10000000 /* WS_VISIBLE */ | 0x02000000 /* WS_CLIPCHILDREN */,
            0, 0, Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight), parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        try
        {
            GetClientRect(hwnd, out var rc);
            Gpu = new Gpu(hwnd, rc.Right - rc.Left, rc.Bottom - rc.Top, GetDpiForWindow(hwnd));
            Ui.Attach(Gpu);
        }
        catch (Exception e) { Failure = e.Message; Support.Log.Write("graphics: " + e); }
        if (Ui.GlassError != null) Support.Log.Write("glass: " + Ui.GlassError);
        if (Gpu != null) Support.Log.Write($"graphics: {Gpu.Adapter}, {Gpu.Width}x{Gpu.Height} at {Gpu.Dpi} dpi");
        Schedule();
        return new HandleRef(this, hwnd);
    }

    protected override void DestroyWindowCore(HandleRef h)
    {
        Ui.Detach();
        Gpu?.Dispose();
        Gpu = null;
        DestroyWindow(h.Handle);
        hwnd = IntPtr.Zero;
    }

    /// <summary>Kept on top of the web view under it (both are windows of their own).</summary>
    public void BringToTop()
    {
        if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE | NOMOVE | NOACTIVATE */);
    }

    private void Frame()
    {
        scheduled = false;
        if (Gpu == null || hwnd == IntPtr.Zero || !IsVisible) return;
        var now = clock.Elapsed.TotalSeconds;
        var dt = (float)(now - last);
        last = now;
        GetClientRect(hwnd, out var rc);
        var dpi = GetDpiForWindow(hwnd);
        var w = Math.Max(1, rc.Right - rc.Left);
        var h = Math.Max(1, rc.Bottom - rc.Top);
        if (w != Gpu.Width || h != Gpu.Height || Math.Abs(dpi - Gpu.Dpi) > 0.01f)
        {
            Gpu.Resize(w, h, dpi);
            Ui.Resized();
        }
        dirty = false;
        var scale = Gpu.Scale;
        Ui.BeginFrame(dt, w / scale, h / scale, scale);
        try { Build?.Invoke(Ui); }
        catch (Exception e) { if (failures++ < 5) Support.Log.Write("a frame failed: " + e); }
        Ui.Render(Ground());
        Ui.EndFrame();
        Gpu.Present(true);
        if (Ui.Animating || dirty || Ui.Down) Schedule();
    }

    /// <summary>The screens as they are now (the screenshot suite's picture): BGRA rows.</summary>
    public (byte[] Pixels, int Width, int Height)? Pixels() => Gpu?.ReadBack();

    // ---- the pointer and the keys ----

    protected override IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var ui = Ui;
        var scale = Gpu?.Scale ?? 1;
        switch (msg)
        {
            case 0x0084: // WM_NCHITTEST
                handled = true;
                return (IntPtr)1; // HTCLIENT
            case 0x0014: // WM_ERASEBKGND
                handled = true;
                return (IntPtr)1;
            case 0x0087: // WM_GETDLGCODE: every key is the screens'
                handled = true;
                return (IntPtr)0x0004;
            case 0x0200: // WM_MOUSEMOVE
                ui.Mouse = new Vector2((short)(lParam.ToInt64() & 0xffff) / scale, (short)((lParam.ToInt64() >> 16) & 0xffff) / scale);
                if (!tracking)
                {
                    var t = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = 0x00000002 /* TME_LEAVE */, hwndTrack = h };
                    TrackMouseEvent(ref t);
                    tracking = true;
                }
                Invalidate();
                break;
            case 0x02A3: // WM_MOUSELEAVE
                tracking = false;
                if (!ui.Down) ui.Mouse = new Vector2(-1, -1);
                Invalidate();
                break;
            case 0x0201: // WM_LBUTTONDOWN
            case 0x0203: // WM_LBUTTONDBLCLK
                ui.Mouse = new Vector2((short)(lParam.ToInt64() & 0xffff) / scale, (short)((lParam.ToInt64() >> 16) & 0xffff) / scale);
                ui.Down = ui.Pressed = true;
                SetCapture(h);
                SetFocus(h);
                Invalidate();
                handled = true;
                return IntPtr.Zero;
            case 0x0202: // WM_LBUTTONUP
                ui.Down = false;
                ui.Released = true;
                ReleaseCapture();
                Invalidate();
                handled = true;
                return IntPtr.Zero;
            case 0x0204: // WM_RBUTTONDOWN
                ui.RightPressed = true;
                Invalidate();
                break;
            case 0x020A: // WM_MOUSEWHEEL
                ui.Wheel += (short)((wParam.ToInt64() >> 16) & 0xffff) / 120f * 96f;
                Invalidate();
                handled = true;
                return IntPtr.Zero;
            case 0x0102: // WM_CHAR
                ui.Typed.Add((char)wParam.ToInt64());
                Invalidate();
                handled = true;
                return IntPtr.Zero;
            case 0x0100: // WM_KEYDOWN
            {
                ui.Ctrl = (GetKeyState(0x11) & 0x8000) != 0;
                ui.Shift = (GetKeyState(0x10) & 0x8000) != 0;
                var k = (int)wParam.ToInt64() switch
                {
                    0x08 => Key.Back, 0x2E => Key.Delete, 0x0D => Key.Enter, 0x1B => Key.Escape, 0x25 => Key.Left, 0x27 => Key.Right,
                    0x26 => Key.Up, 0x28 => Key.Down, 0x24 => Key.Home, 0x23 => Key.End, 0x09 => Key.Tab, 0x41 => Key.A, 0x43 => Key.C,
                    0x56 => Key.V, 0x58 => Key.X, 0x46 => Key.F, 0x52 => Key.R, 0x4E => Key.N, 0x57 => Key.W, 0xBC => Key.Comma,
                    _ => Key.None,
                };
                if (k != Key.None) ui.Keys.Add(k);
                Invalidate();
                break;
            }
            case 0x0005: // WM_SIZE
                Invalidate();
                break;
        }
        return base.WndProc(h, msg, wParam, lParam, ref handled);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Invalidate();
    }

    // ---- Win32 ----

    private delegate IntPtr WndProcDelegate(IntPtr h, uint msg, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(int ex, string cls, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT t);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr inst, int id);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
}
