using System.Diagnostics;

namespace Simpl.Support;

/// <summary>
/// (0.3.83) An update played through from the developer tools (Developer ▸ Updates ▸ Simulate an update), as from any
/// earlier version to this one, with everything a student sees: the download (its progress in the developer tools — over
/// the window, nothing, as for a real one), the notice at the top of the window as it goes in (a Critical Update's words
/// when it is one), Simpl quitting and opening again as an update opens it — in front, where it was (<see cref="Resume"/>)
/// — and what that version brings: What's New with every version since it, or To Do &amp; Calendar's intro in its place
/// when it is from before 0.3.80. Nothing is downloaded or installed, and no real update starts meanwhile. With Restart
/// off, the popups come up at once, in place.
/// </summary>
public static partial class Updates
{
    public enum Rehearsal { Downloading, Installing }

    /// <summary>The step an update played through is at (null: none under way).</summary>
    public static Rehearsal? Rehearsing { get; private set; }

    /// <summary>The update played through is a Critical Update.</summary>
    public static bool RehearsingCritical { get; private set; }

    /// <summary>How far its download has come, 0 to 1.</summary>
    public static double RehearsalProgress { get; private set; }

    /// <summary>
    /// An update from <paramref name="from"/> played through: <paramref name="moved"/> told at each step (the developer
    /// tools' page), and with <paramref name="restart"/> off, <paramref name="inPlace"/> bringing the popups up instead.
    /// </summary>
    public static async Task Rehearse(string from, bool critical, bool restart, Action inPlace, Action? moved = null)
    {
        if (Rehearsing != null || restarting) return;
        restarting = true; // (no real update goes in meanwhile)
        Log.Write($"update rehearsal: from {from} to {Version}{(critical ? ", a Critical Update" : "")}{(restart ? "" : ", in place")}");
        RehearsingCritical = critical;
        Rehearsing = Rehearsal.Downloading;
        for (var i = 0; i <= 40; i++)
        {
            RehearsalProgress = i / 40.0;
            moved?.Invoke();
            await Task.Delay(60);
        }
        // (going in: the notice says so for as long as a real update's does before Simpl quits, GoIn)
        Rehearsing = Rehearsal.Installing;
        Changed?.Invoke();
        moved?.Invoke();
        await Task.Delay(2500);
        WhatsNew.Pretend(from);
        MergedIntro.Pretend(from);
        if (restart && Reopen()) return;
        Rehearsing = null;
        restarting = false;
        Changed?.Invoke();
        moved?.Invoke();
        inPlace();
    }

    /// <summary>Where the window is noted, as an update notes it, and Simpl opened again (the copy started waits for this
    /// one to have quit) — with what this run was started for kept (its profile, the mock, a demo).</summary>
    private static bool Reopen()
    {
        try
        {
            if (Environment.ProcessPath is not { } exe) return false;
            Remember(Version);
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            psi.ArgumentList.Add("--after");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            foreach (var keep in new[] { "profile", "base-url" })
                if (Launch.Get(keep) is { } v) { psi.ArgumentList.Add("--" + keep); psi.ArgumentList.Add(v); }
            if (Launch.Flag("demo")) psi.ArgumentList.Add("--demo");
            Prefs.Flush();
            Process.Start(psi);
        }
        catch (Exception e)
        {
            Prefs.Remove(ResumeKey);
            Log.Write("update rehearsal: could not open Simpl again: " + e.Message + " — the popups shown in place");
            return false;
        }
        Log.Write("update rehearsal: quitting, to open again");
        EraseAll.Quit();
        return true;
    }
}
