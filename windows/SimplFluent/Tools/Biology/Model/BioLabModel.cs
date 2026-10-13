namespace Simpl.Tools;

// Bio Lab Calculator's own model (spec §4.8, §5.5): what the tool works out and keeps without a window — the
// calculators' formulas — in a static class, so it outlives the view being built again (the tools' rule: a view
// subscribes to Changed on Loaded and lets go on Unloaded). Plain .NET, no WPF, so the unit tests reach it. Its builder
// fills this in, with its own BioSelfCheck lines below (BiolabChecks, run by BioSelfCheck.Run and by the unit tests
// with the others).

/// <summary>Bio Lab Calculator's state, kept across its views (a placeholder until the tool is built).</summary>
public static class BioLabModel
{
    /// <summary>Something the views show changed: they draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();
}

public static partial class BioSelfCheck
{
    /// <summary>Bio Lab Calculator's own bio-check lines, against its section's test vectors (none yet).</summary>
    static partial void BiolabChecks(BioCheckReport r) { }
}
