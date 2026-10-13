using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Simpl.Support;

/// <summary>
/// Settings ▸ Data ▸ Reset Everything (the Mac's EraseAll): everything Simpl keeps on this PC erased — the preferences,
/// the extension's storage, the answers kept, the saved sign-in, the school's cookies and site data, the photos, and
/// the vault's key (Vault.Erase: what it sealed can never be opened again, wherever a copy went) — and Simpl opened again
/// as if it were new. The flashcard sets are the student's own work and stay, as on the Mac: sealed again under the new
/// vault. No step may hold it up: each has a few seconds, then the next goes on; the new copy waits for this one to have
/// quit (--after) before it clears the web data this one was still using. Uninstall Simpl's "Also erase…" erases the
/// same, the flashcards too (no vault is left to keep them), then quits instead.
/// </summary>
public static class EraseAll
{
    public static async Task Run(Engine.Engine? engine)
    {
        byte[]? cards = null;
        try { cards = Tools.FlashcardStore.Take(); } catch (Exception e) { Log.Write("erase: flashcards: " + e.Message); }
        await Erase(engine);
        if (cards != null)
        {
            Vault.Renew();
            Tools.FlashcardStore.Put(cards);
        }
        Restart(erased: true);
    }

    /// <summary>Everything Simpl keeps erased, this copy left running only to restart or quit: nothing it still holds is
    /// written back (the preferences and the extension's storage stay erased, the answers stop being kept, and the vault
    /// seals nothing more).</summary>
    public static async Task Erase(Engine.Engine? engine)
    {
        Log.Write("erase: everything");
        engine?.Answers.Stop();
        try { LoginVault.Clear(); } catch (Exception e) { Log.Write("erase sign-in: " + e.Message); }
        if (engine?.Web.CoreWebView2 is { } core)
        {
            try
            {
                core.CookieManager.DeleteAllCookies();
                await Task.WhenAny(core.Profile.ClearBrowsingDataAsync(), Task.Delay(4000));
            }
            catch (Exception e) { Log.Write("erase web: " + e.Message); }
        }
        try { Engine.Bridge.Shared.EraseStorage(); } catch (Exception e) { Log.Write("erase storage: " + e.Message); }
        Prefs.Erase();
        foreach (var f in new[] { AppPaths.Storage, AppPaths.Login, AppPaths.Flashcards })
        {
            try { File.Delete(f); } catch { }
            try { File.Delete(f + ".tmp"); } catch { }
        }
        var dirs = new List<string> { AppPaths.Answers, Appearance.PhotosDir };
        // (what a vault that could not be opened once set aside: sealed with a key that is gone, or going now)
        try { dirs.AddRange(Directory.EnumerateDirectories(AppPaths.Root, "Unreadable-*")); } catch { }
        foreach (var d in dirs)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        }
        try { Vault.Erase(); } catch (Exception e) { Log.Write("erase: the vault: " + e.Message); }
    }

    /// <summary>Simpl opened afresh: a new copy started (it waits for this one), and this one gone at once.</summary>
    public static void Restart(bool erased)
    {
        try
        {
            var exe = Environment.ProcessPath;
            var args = new List<string> { "--after", Environment.ProcessId.ToString() };
            if (erased) args.Add("--erased");
            if (Launch.Get("profile") is { } p) { args.Add("--profile"); args.Add(p); }
            if (exe != null)
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
                foreach (var a in args) psi.ArgumentList.Add(a);
                Process.Start(psi);
            }
        }
        catch (Exception e) { Log.Write("restart: " + e.Message); }
        Quit();
    }

    /// <summary>The app quits now: its windows closed, and the process ended even if something holds on.</summary>
    public static void Quit()
    {
        Shell.MainWindow.Quitting = true;
        try { Application.Current.Shutdown(); } catch { }
        // (whatever may still hold the process — a web view winding down — has two seconds)
        _ = Task.Run(async () => { await Task.Delay(2000); Environment.Exit(0); });
    }

    /// <summary>Opened again after a reset: what the copy before could not delete, deleted now.</summary>
    public static void FinishIfAsked()
    {
        if (!Launch.Flag("erased")) return;
        for (var i = 0; i < 10; i++)
        {
            try { if (Directory.Exists(AppPaths.WebData)) Directory.Delete(AppPaths.WebData, true); break; }
            catch { Thread.Sleep(300); } // (the old web view's browser can take a moment to let go of its files)
        }
    }
}
