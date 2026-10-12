using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Simpl.Support;

/// <summary>
/// Settings ▸ Data ▸ Reset Everything (the Mac's EraseAll): everything Simpl keeps on this PC erased — the preferences,
/// the extension's storage, the answers kept, the saved sign-in, the school's cookies and site data, the photos — and
/// Simpl opened again as if it were new.
/// </summary>
public static class EraseAll
{
    public static async Task Run(Engine.Engine? engine)
    {
        LoginVault.Clear();
        if (engine?.Web.CoreWebView2 is { } core)
        {
            try
            {
                core.CookieManager.DeleteAllCookies();
                await core.Profile.ClearBrowsingDataAsync();
            }
            catch (Exception e) { Log.Write("erase: " + e.Message); }
        }
        Engine.Bridge.Shared.EraseStorage();
        foreach (var f in new[] { AppPaths.PrefsFile, AppPaths.Storage })
        {
            try { File.Delete(f); } catch { }
        }
        foreach (var d in new[] { AppPaths.Answers, Appearance.PhotosDir })
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        }
        // (the web data folder is in use until the app is gone: the app opened again clears what is left of it)
        try
        {
            var exe = Environment.ProcessPath;
            if (exe != null) Process.Start(new ProcessStartInfo(exe, "--erased") { UseShellExecute = true });
        }
        catch (Exception e) { Log.Write("restart: " + e.Message); }
        Application.Current.Shutdown();
    }

    /// <summary>Opened again after a reset: what the running app could not delete, deleted now.</summary>
    public static void FinishIfAsked()
    {
        if (!Launch.Flag("erased")) return;
        try { if (Directory.Exists(AppPaths.WebData)) Directory.Delete(AppPaths.WebData, true); } catch { }
    }
}
