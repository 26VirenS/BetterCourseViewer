using System.Diagnostics;
using System.IO;

namespace Simpl.Support;

/// <summary>
/// Settings ▸ General ▸ Uninstall Simpl (Windows' own Settings ▸ Apps ▸ Uninstall, asked from Simpl): for a copy
/// Simpl-Setup.exe put here, its uninstaller — the one its entry in Settings ▸ Apps names — started with its progress
/// showing, and Simpl quit at once, for real (not to the notification area), so the uninstaller finds it gone and takes
/// the program, its shortcuts and that entry away. What Simpl keeps stays (%LOCALAPPDATA%\Simpl, sealed by the vault,
/// the saved sign-in with it) unless it is erased first, as Reset Everything erases it — the vault's key too (vault.key
/// and the TPM's), the flashcards with it, and a sign-in an earlier Simpl kept in Credential Manager and this one never
/// moved in (LoginVault.Clear deletes that as well). A copy run from its zip is removed by deleting its folder; a
/// development run never uninstalls anything.
/// </summary>
public static class Uninstall
{
    /// <summary>
    /// How the uninstaller is run (Installer/Simpl.iss): /SILENT shows its progress and neither of its own messages —
    /// "Uninstall Simpl from this PC?" (Simpl's sheet has asked) and "Simpl was removed…", which says what is kept and
    /// would be wrong after an erase. Not /SUPPRESSMSGBOXES: the one box /SILENT still shows is "Simpl is still open"
    /// (should Simpl not have gone when the uninstaller looks), which it would answer with Cancel; shown, OK tries again.
    /// /NORESTART: Windows is never restarted. Its log goes with Windows' temporary files.
    /// </summary>
    private static string Arguments => $"/SILENT /NORESTART \"/LOG={LogFile}\"";

    public static string LogFile => Path.Combine(Path.GetTempPath(), "Simpl-Uninstall.log");

    /// <summary>Uninstalling, and erasing what Simpl keeps before it quits: the uninstaller's request to quit (made each
    /// second while it waits for Simpl to go) is not taken meanwhile — Simpl quits by itself once the erasing is done.</summary>
    public static bool Erasing { get; private set; }

    /// <summary>Uninstall is offered: the copy the installer put here, outside a development run.</summary>
    public static bool Available => Updates.Installed && !Updates.IsDevelopmentRun;

    /// <summary>
    /// This copy's uninstaller, as its entry in Settings ▸ Apps names it (UninstallString, else QuietUninstallString:
    /// its path in quotes, then any switches), else unins000.exe beside Simpl.exe; null when there is none.
    /// </summary>
    public static string? Find()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Updates.EntryKey);
            foreach (var name in new[] { "UninstallString", "QuietUninstallString" })
                if (k?.GetValue(name) is string command && ProgramOf(command) is { } exe && File.Exists(exe)) return exe;
        }
        catch (Exception e) { Log.Write("uninstall: reading its entry: " + e.Message); }
        var beside = Path.Combine(AppPaths.AppDir, "unins000.exe");
        return File.Exists(beside) ? beside : null;
    }

    /// <summary>The program a command runs: "C:\…\unins000.exe" /SILENT → C:\…\unins000.exe.</summary>
    private static string? ProgramOf(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? command[..(exe + 4)] : null;
    }

    /// <summary>
    /// Uninstall, confirmed (its sheet in Settings ▸ General, or --uninstall yes): the uninstaller started; what Simpl
    /// keeps erased, if asked, while the uninstaller waits for Simpl to have gone (its AppMutex check, Installer/Simpl.iss)
    /// — so it is erased before anything is uninstalled, and nothing is erased if the uninstaller cannot start; then
    /// Simpl quit. Null once that is under way; else why it could not be, nothing changed and Simpl still open.
    /// </summary>
    public static async Task<string?> Run(Engine.Engine? engine, bool erase)
    {
        if (!Available) return Updates.IsDevelopmentRun ? "Uninstalling is off in a development run." : "This copy of Simpl wasn’t installed with Simpl-Setup.exe.";
        if (Find() is not { } exe) return "Simpl’s uninstaller isn’t there. Uninstall Simpl from Windows’ Settings ▸ Apps instead.";
        try
        {
            // (through the shell, so it inherits nothing of Simpl's; in Windows' temporary folder, not the one it removes)
            Process.Start(new ProcessStartInfo(exe, Arguments) { UseShellExecute = true, WorkingDirectory = Path.GetTempPath() });
        }
        catch (Exception e)
        {
            Log.Write("uninstall: " + e.Message);
            return "Couldn’t start Simpl’s uninstaller: " + e.Message;
        }
        Log.Write($"uninstall: {exe} started{(erase ? ", erasing first" : "")}, and quitting");
        if (erase)
        {
            Erasing = true;
            await EraseAll.Erase(engine);
            // (and a sign-in an earlier Simpl kept in Credential Manager, never moved into the vault: gone too)
            LoginVault.ForgetOld();
        }
        EraseAll.Quit();
        return null;
    }

    /// <summary>
    /// --uninstall yes (CI's last trial of the installer): Uninstall pressed on its sheet, what Simpl keeps kept — the
    /// same Run — once the window is up. Only the copy the installer put here, never a development run.
    /// </summary>
    public static async void IfAsked()
    {
        if (!Launch.Flag("uninstall")) return;
        if (!Available)
        {
            Log.Write("uninstall: --uninstall left alone (" + (Updates.IsDevelopmentRun ? "a development run" : "not installed") + ")");
            return;
        }
        await Task.Delay(2000); // (settled first, as it is when asked for in Settings)
        if (await Run(null, erase: false) is { } problem) Log.Write("uninstall: " + problem);
    }
}
