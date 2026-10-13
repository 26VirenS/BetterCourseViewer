namespace Simpl.Tools;

// The step-through's model (spec §3.6), the Mac's BioSteps.swift: a process as an ordered list of steps — each a short
// title, a one-to-three sentence explanation, an optional stage group (number keys jump to groups) and what to notice —
// and the timing every process tool shares: 600 ms between keyframes at 1× (ease in-out; back plays it in reverse),
// 2.4 s on a step while playing, 0.5×, 1× and 2× speeds, and a 200 ms crossfade instead of motion when animations are
// off. The controller and its controls (BioStepThrough.cs) drive a tool's drawing through BioStepPhase.

/// <summary>A step of a process: its index, its title (at most 40 characters: "Elongation"), its explanation (one to three
/// sentences, naming the molecule at work), the stage group the number keys jump to (1 = Interphase … 6 = Cytokinesis),
/// if the tool has groups, and what to notice (the tool lights these).</summary>
public sealed record BioStep(int Index, string Title, string Text, int? Group = null, IReadOnlyList<string>? Notice = null)
{
    public IReadOnlyList<string> Notices => Notice ?? Array.Empty<string>();
}

/// <summary>Where a step-through is between two keyframes: from step <c>From</c> to step <c>To</c>, <c>T</c> of the way
/// (0…1, eased). A tool draws step From moved T toward step To — or, with <c>Crossfade</c>, both, faded (animations off).</summary>
public readonly record struct BioStepPhase(int From, int To, double T, bool Crossfade)
{
    /// <summary>At rest on a step.</summary>
    public static BioStepPhase At(int i) => new(i, i, 1, false);

    public bool IsMoving => From != To && T < 1;
    /// <summary>The step it is nearest.</summary>
    public int Current => T < 0.5 ? From : To;

    /// <summary>A value between two keyframes' values.</summary>
    public double Mix(double a, double b) => a + (b - a) * T;
}

/// <summary>The timing the step-throughs share.</summary>
public static class BioStepTiming
{
    /// <summary>Between keyframes at 1× (seconds).</summary>
    public const double Move = 0.6;
    /// <summary>On a step while playing, at 1×, before the next.</summary>
    public const double Dwell = 2.4;
    /// <summary>The crossfade when animations are off.</summary>
    public const double Crossfade = 0.2;
    public static readonly double[] Speeds = { 0.5, 1, 2 };

    /// <summary>Ease in-out (cubic), 0…1 → 0…1.</summary>
    public static double Ease(double t)
    {
        var x = Math.Min(Math.Max(t, 0), 1);
        return x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;
    }

    /// <summary>The step a group starts at (the first step in it), null if no step is in it.</summary>
    public static int? StartOfGroup(int g, IReadOnlyList<BioStep> steps) => steps.FirstOrDefault(s => s.Group == g)?.Index;

    /// <summary>"Step 4 of 12 · Elongation".</summary>
    public static string Caption(int i, IReadOnlyList<BioStep> steps) => i >= 0 && i < steps.Count ? $"Step {i + 1} of {steps.Count} · {steps[i].Title}" : "";

    /// <summary>"0.5×", "1×", "2×".</summary>
    public static string SpeedText(double s) => s == 0.5 ? "0.5×" : $"{(int)s}×";
}
