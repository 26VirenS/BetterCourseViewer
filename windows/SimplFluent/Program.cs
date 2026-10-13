using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Simpl.Support;

namespace Simpl;

/// <summary>
/// Simpl for Windows: who it is to Windows (one app id, before anything shows), the launch arguments read, one copy of
/// the app at a time (a second one opened brings the first back, from the notification area too), Windows 11's theme
/// put on, then the window.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Perf.Begin();
        AppIdentity.Claim();              // (first of all: the window, its taskbar button and the shortcuts are one app)
        Launch.Parse(args);
        Instance.WaitForPrevious();       // (opened again by Reset Everything: the copy before it gone first)
        EraseAll.FinishIfAsked();         // (and what it could not delete, deleted now)
        if (!Instance.Claim()) return;    // (already open: that one is brought forward instead)
        Vault.Preload();                  // (the vault's key unwrapped by the TPM and Windows meanwhile, off this thread)
        Engine.BridgeStore.Preload();     // (the extension's storage read meanwhile, once the vault is open)
        LoginVault.Preload();             // (and the saved sign-in, an earlier Simpl's moved into the vault)
        Tools.FlashcardStore.SealSoon();  // (and flashcards an earlier Simpl kept in plain text, sealed)
        var app = new App();
        app.InitializeComponent();
        Kit.FlatResources.Apply(app);     // (WPF-UI's 91 dictionaries of styles made one while the window is idle: every lookup a few reads, not 91)
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write("unhandled: " + e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("fatal: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Write("task: " + e.Exception); e.SetObserved(); };
        Log.Write($"Simpl for Windows {WhatsNew.Current} starting{(Updates.Installed ? " (installed)" : "")}: {Launch.Said(args)}");
        // (opened again by an update: where the window was, so it opens there; and the Mac's Pause updates, asked for here)
        Updates.ReadResume();
        if (Launch.Get("pause-updates") is { } pause) Updates.Paused = pause is "on" or "yes" or "true";
        if (!Launch.IsDevelopment)
        {
            AppIdentity.Register();
            // (an update's installer, done with by now: deleted a minute on)
            _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromMinutes(1)); Updates.Tidy(); });
        }
        Appearance.Apply();
        var main = new Shell.MainWindow();
        Perf.Watch(main);
        Instance.ShowRequested += main.Bring;
        Instance.QuitRequested += () =>
        {
            // (Uninstall Simpl's own uninstaller, asking while Simpl erases what it keeps: it quits by itself once done)
            if (Uninstall.Erasing) return;
            Log.Write("quit: asked by Simpl's installer");
            EraseAll.Quit();
        };
        app.Run(main);
    }
}

/// <summary>
/// Who Simpl is to Windows: one app id (an AppUserModelID) for its windows and their taskbar button, the installer's
/// shortcuts (Installer/Simpl.iss) and what the taskbar pins — so they are one app, and pinning it pins Simpl — and the
/// name and picture its notifications carry for that id.
/// </summary>
public static class AppIdentity
{
    public const string Id = "SimplCourses.Simpl";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    /// <summary>Before anything shows or is read (Windows asks it be set first).</summary>
    public static void Claim()
    {
        try { SetCurrentProcessExplicitAppUserModelID(Id); } catch { /* (nothing to set it with: Windows' own grouping) */ }
    }

    /// <summary>
    /// The app id's name and picture, as Windows shows them on Simpl's notifications (the installer writes the same
    /// when it installs, and takes it away when it uninstalls; a copy run from its zip has no shortcut to say them, so
    /// it says them here).
    /// </summary>
    public static void Register()
    {
        try
        {
            var icon = System.IO.Path.Combine(AppPaths.AppDir, "Assets", "Simpl-256.png");
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + Id);
            if (k.GetValue("DisplayName") as string != "Simpl") k.SetValue("DisplayName", "Simpl");
            if (System.IO.File.Exists(icon) && k.GetValue("IconUri") as string != icon) k.SetValue("IconUri", icon);
        }
        catch (Exception e) { Log.Write("app id: " + e.Message); }
    }
}

/// <summary>
/// One Simpl at a time for each place it keeps its files (the screenshot suite's copies each have their own): a second
/// copy opened tells the first to come forward, then quits. Every copy that runs also holds the mutex Simpl's installer
/// looks for, and listens for its installer's or uninstaller's request to quit (Installer/Simpl.iss).
/// </summary>
public static class Instance
{
    /// <summary>Held while any Simpl runs: the installer's AppMutex.</summary>
    public const string AppMutexName = "SimplCourses.Simpl.App";
    /// <summary>Set by the installer and the uninstaller: the Simpl that takes it quits (one signal, one copy).</summary>
    public const string QuitEventName = @"Local\SimplCourses.Simpl.Quit";

    private static Mutex? mutex, appMutex;
    public static event Action? ShowRequested;
    public static event Action? QuitRequested;

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
        // (the mutex kept in a field for as long as the app runs: a handle let go would end it, and the installer's check)
        EventWaitHandle? quit = null;
        try
        {
            appMutex = new Mutex(false, AppMutexName);
            quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
            quit.Reset(); // (a request meant for a copy that has gone since is not this one's)
        }
        catch (Exception e) { Log.Write("installer's mutex: " + e.Message); }
        var handles = quit != null ? new WaitHandle[] { show, quit } : new WaitHandle[] { show };
        var t = new Thread(() =>
        {
            while (true)
            {
                var which = WaitHandle.WaitAny(handles);
                Application.Current?.Dispatcher.BeginInvoke(() => (which == 0 ? ShowRequested : QuitRequested)?.Invoke());
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
