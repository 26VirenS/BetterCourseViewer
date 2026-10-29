namespace Simpl.Support;

/// <summary>
/// (0.3.84) Silent mode, as the web Dashboard and the Mac have it: the Dashboard with nothing on it but what comes next,
/// in large type on black, and the pane in words alone (DashboardScreen's SilentPage, AppShell.SilentLook). Kept on this
/// PC; the tour and Appearance put it by while they are on (both show the whole Dashboard), its choice kept. (The
/// screenshot suite: --dash-silent.)
/// </summary>
public static class SilentMode
{
    private static bool? on;

    /// <summary>Chosen (Silent, by the Dashboard's view), kept on this PC.</summary>
    public static bool On => on ??= Launch.Flag("dash-silent") || Prefs.Bool("dashSilent");

    /// <summary>Put by for now (the tour, Appearance).</summary>
    public static bool Held { get; private set; }

    /// <summary>The Dashboard shows Silent mode.</summary>
    public static bool Shown => On && !Held;

    /// <summary>Chosen or put back, or put by or brought back.</summary>
    public static event Action? Changed;

    public static void Set(bool value)
    {
        if (value == On) return;
        on = value;
        Prefs.Set("dashSilent", value);
        Changed?.Invoke();
    }

    public static void Hold(bool value)
    {
        if (value == Held) return;
        Held = value;
        Changed?.Invoke();
    }
}
