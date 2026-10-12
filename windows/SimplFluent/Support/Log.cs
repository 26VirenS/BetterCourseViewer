using System.IO;

namespace Simpl.Support;

/// <summary>What went wrong, written down (simpl.log beside the app's settings): for a report, and for CI's pictures.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static void Write(string line)
    {
        System.Diagnostics.Debug.WriteLine("[Simpl] " + line);
        try
        {
            lock (Gate) File.AppendAllText(Path.Combine(AppPaths.Root, "simpl.log"), $"{DateTime.Now:HH:mm:ss.fff} {line}\n");
        }
        catch { /* nowhere to write: the debugger has it */ }
    }
}
