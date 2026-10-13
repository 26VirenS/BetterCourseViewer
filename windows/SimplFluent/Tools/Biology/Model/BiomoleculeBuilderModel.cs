namespace Simpl.Tools;

// Biomolecule Builder's own model (spec §4.4, §6.4): what the tool works out and keeps without a window — the peptide
// builder (NeRF, side chains, clash relief), the strand and sugar builders — in a static class, so it outlives the view
// being built again (the tools' rule: a view subscribes to Changed on Loaded and lets go on Unloaded). Plain .NET, no
// WPF, so the unit tests reach it. Its builder fills this in, with its own BioSelfCheck lines below (BiomolChecks, run
// by BioSelfCheck.Run and by the unit tests with the others).

/// <summary>Biomolecule Builder's state, kept across its views (a placeholder until the tool is built).</summary>
public static class BiomoleculeBuilderModel
{
    /// <summary>Something the views show changed: they draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();
}

public static partial class BioSelfCheck
{
    /// <summary>Biomolecule Builder's own bio-check lines, against its section's test vectors (none yet).</summary>
    static partial void BiomolChecks(BioCheckReport r) { }
}
