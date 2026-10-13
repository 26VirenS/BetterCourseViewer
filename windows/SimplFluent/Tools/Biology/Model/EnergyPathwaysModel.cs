namespace Simpl.Tools;

// Photosynthesis & Respiration's own model (spec §4.10, §5.8): what the tool works out and keeps without a window — the
// stages' inputs, outputs and running totals — in a static class, so it outlives the view being built again (the tools'
// rule: a view subscribes to Changed on Loaded and lets go on Unloaded). Plain .NET, no WPF, so the unit tests reach
// it. Its builder fills this in, with its own BioSelfCheck lines below (EnergyChecks, run by BioSelfCheck.Run and by
// the unit tests with the others).

/// <summary>Photosynthesis & Respiration's state, kept across its views (a placeholder until the tool is built).</summary>
public static class EnergyPathwaysModel
{
    /// <summary>Something the views show changed: they draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();
}

public static partial class BioSelfCheck
{
    /// <summary>Photosynthesis & Respiration's own bio-check lines, against its section's test vectors (none yet).</summary>
    static partial void EnergyChecks(BioCheckReport r) { }
}
