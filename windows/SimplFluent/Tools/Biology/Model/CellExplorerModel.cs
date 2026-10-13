namespace Simpl.Tools;

// Cell Explorer's own model (spec §4.1, §5.1): what the tool works out and keeps without a window — the cells' recipes
// and placement, the organelles' facts, the cutaway and what is selected — in a static class, so it outlives the view
// being built again (the tools' rule: a view subscribes to Changed on Loaded and lets go on Unloaded). Plain .NET, no
// WPF, so the unit tests reach it. Its builder fills this in, with its own BioSelfCheck lines below (CellChecks, run by
// BioSelfCheck.Run and by the unit tests with the others).

/// <summary>Cell Explorer's state, kept across its views (a placeholder until the tool is built).</summary>
public static class CellExplorerModel
{
    /// <summary>Something the views show changed: they draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();
}

public static partial class BioSelfCheck
{
    /// <summary>Cell Explorer's own bio-check lines, against its section's test vectors (none yet).</summary>
    static partial void CellChecks(BioCheckReport r) { }
}
