using System.Windows;
using Simpl.Support;

namespace Simpl;

/// <summary>Simpl for Windows: the launch arguments read, then the window.</summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Launch.Parse(args);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write("unhandled: " + e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("fatal: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Write("task: " + e.Exception); e.SetObserved(); };
        Log.Write($"Simpl for Windows starting: {string.Join(' ', args)}");
        app.Run(new Shell.MainWindow());
    }
}
