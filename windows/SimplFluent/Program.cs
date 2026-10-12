using System.Threading;
using System.Windows;
using Simpl.Support;

namespace Simpl;

/// <summary>
/// Simpl for Windows: the launch arguments read, one copy of the app at a time (a second one opened brings the first
/// back, from the notification area too), Windows 11's theme put on, then the window.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Launch.Parse(args);
        Instance.WaitForPrevious();       // (opened again by Reset Everything: the copy before it gone first)
        EraseAll.FinishIfAsked();         // (and what it could not delete, deleted now)
        if (!Instance.Claim()) return;    // (already open: that one is brought forward instead)
        var app = new App();
        app.InitializeComponent();
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write("unhandled: " + e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("fatal: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Write("task: " + e.Exception); e.SetObserved(); };
        Log.Write($"Simpl for Windows {WhatsNew.Current} starting: {string.Join(' ', args)}");
        Appearance.Apply();
        var main = new Shell.MainWindow();
        Instance.ShowRequested += main.Bring;
        app.Run(main);
    }
}

/// <summary>
/// One Simpl at a time for each place it keeps its files (the screenshot suite's copies each have their own): a second
/// copy opened tells the first to come forward, then quits.
/// </summary>
public static class Instance
{
    private static Mutex? mutex;
    public static event Action? ShowRequested;

    private static string Key
    {
        get
        {
            var h = 0;
            foreach (var c in AppPaths.Root.ToLowerInvariant()) h = unchecked(h * 31 + c);
            return $"Simpl-{h:x8}";
        }
    }

    /// <summary>The first copy: true. Another already open: it is asked to come forward, and false.</summary>
    public static bool Claim()
    {
        mutex = new Mutex(true, @"Local\" + Key, out var first);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + Key + "-show");
        if (!first)
        {
            show.Set();
            return false;
        }
        var t = new Thread(() =>
        {
            while (true)
            {
                show.WaitOne();
                Application.Current?.Dispatcher.BeginInvoke(() => ShowRequested?.Invoke());
            }
        }) { IsBackground = true, Name = "Simpl show" };
        t.Start();
        return true;
    }

    /// <summary>--after &lt;pid&gt;: the copy that opened this one (Reset Everything) has finished quitting.</summary>
    public static void WaitForPrevious()
    {
        if (!int.TryParse(Launch.Get("after"), out var pid)) return;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            p.WaitForExit(20_000);
        }
        catch { /* (already gone) */ }
    }
}
