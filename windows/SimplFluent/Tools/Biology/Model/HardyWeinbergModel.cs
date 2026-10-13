namespace Simpl.Tools;

// Hardy–Weinberg's own model (spec §4.7, §5.4): what the tool works out and keeps without a window — the calculator,
// the HWE test and the simulation — in a static class, so it outlives the view being built again (the tools' rule: a
// view subscribes to Changed on Loaded and lets go on Unloaded). Plain .NET, no WPF, so the unit tests reach it. Its
// builder fills this in, with its own BioSelfCheck lines below (PopgenChecks, run by BioSelfCheck.Run and by the unit
// tests with the others).

/// <summary>Hardy–Weinberg's state, kept across its views (a placeholder until the tool is built).</summary>
public static class HardyWeinbergModel
{
    /// <summary>Something the views show changed: they draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();
}

public static partial class BioSelfCheck
{
    /// <summary>Hardy–Weinberg's own bio-check lines, against its section's test vectors (none yet).</summary>
    static partial void PopgenChecks(BioCheckReport r) { }
}
