using System.Diagnostics;

namespace Simpl.Tools;

// The step-through's controller (spec §3.6), the Mac's BioStepController with its clock handed in: the steps, where it
// is, whether it is playing and at what speed, and the move being drawn between two keyframes. No WPF here, so a tool
// can keep its controller in its static model (it outlives the view) and the unit tests can drive it with a clock of
// their own. BioStepThrough.cs gives it its controls (⏮ ◀ ▶/⏸ ▶ ⏭, the dots, the caption, the speed), its keys, its
// frame-by-frame drawing (BioStepAnimator) and its Narrator announcements.

/// <summary>A process's position and playing, for a tool's drawing and its controls.</summary>
public sealed class BioStepController
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();
    private readonly Func<double> clock;
    private (int From, int To, double Start, double Duration, bool Crossfade)? transition;
    private double? nextAdvance;
    private double speed = 1;

    /// <param name="steps">The process's steps, in order.</param>
    /// <param name="start">The step it begins on.</param>
    /// <param name="clock">Seconds, rising (the unit tests' own; by default the app's).</param>
    public BioStepController(IReadOnlyList<BioStep> steps, int start = 0, Func<double>? clock = null)
    {
        Steps = steps;
        this.clock = clock ?? (() => Watch.Elapsed.TotalSeconds);
        Index = Math.Min(Math.Max(start, 0), Math.Max(steps.Count - 1, 0));
    }

    public IReadOnlyList<BioStep> Steps { get; private set; }
    public int Index { get; private set; }
    public bool Playing { get; private set; }

    /// <summary>0.5, 1 or 2.</summary>
    public double Speed
    {
        get => speed;
        set
        {
            if (speed == value) return;
            speed = value;
            if (Playing) nextAdvance = clock() + Wait;
            Changed?.Invoke();
        }
    }

    /// <summary>Crossfade instead of moving (Windows' animations off; BioStepControls keeps it so).</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>The step, playing or speed changed (the controls and the tool draw again).</summary>
    public event Action? Changed;

    /// <summary>A step arrived at: its caption and explanation, for Narrator ("Step 4 of 12 · Elongation. RNA polymerase …").</summary>
    public event Action<string>? Announce;

    public BioStep? Current => Index >= 0 && Index < Steps.Count ? Steps[Index] : null;
    public bool AtStart => Index == 0;
    public bool AtEnd => Index >= Steps.Count - 1;

    private double Wait => (BioStepTiming.Dwell + BioStepTiming.Move) / Math.Max(Speed, 0.1);

    /// <summary>Where the drawing is at a moment: at rest on the step, or part way between two (eased; linear when crossfading).</summary>
    public BioStepPhase Phase(double? at = null)
    {
        if (transition is not { } t) return BioStepPhase.At(Index);
        var now = at ?? clock();
        var u = t.Duration > 0 ? (now - t.Start) / t.Duration : 1;
        if (u >= 1) return BioStepPhase.At(t.To);
        return new BioStepPhase(t.From, t.To, t.Crossfade ? Math.Max(u, 0) : BioStepTiming.Ease(u), t.Crossfade);
    }

    /// <summary>Whether a change is still being drawn (a tool's frame-by-frame drawing stops when it is not).</summary>
    public bool IsMoving(double? at = null) => transition is { } t && (at ?? clock()) - t.Start < t.Duration;

    /// <summary>The steps changed (another gene, prokaryote mode): kept where they were if asked and possible.</summary>
    public void SetSteps(IReadOnlyList<BioStep> steps, bool keepIndex = false)
    {
        Pause(quiet: true);
        Steps = steps;
        Index = keepIndex ? Math.Min(Index, Math.Max(steps.Count - 1, 0)) : 0;
        transition = null;
        Changed?.Invoke();
    }

    public void Go(int i, bool animated = true)
    {
        if (i < 0 || i >= Steps.Count || i == Index) return;
        var from = Index;
        Index = i;
        if (animated)
        {
            var d = (ReduceMotion ? BioStepTiming.Crossfade : BioStepTiming.Move) / Math.Max(Speed, 0.1);
            transition = (from, i, clock(), d, ReduceMotion);
        }
        else transition = null;
        Changed?.Invoke();
        if (Current is { } s) Announce?.Invoke($"{BioStepTiming.Caption(Index, Steps)}. {s.Text}");
    }

    public void Next() => Go(Index + 1);
    public void Back() => Go(Index - 1);
    public void First() { Pause(); Go(0); }
    public void Last() { Pause(); Go(Steps.Count - 1); }

    /// <summary>The first step of a stage group (number keys).</summary>
    public bool JumpToGroup(int g)
    {
        if (BioStepTiming.StartOfGroup(g, Steps) is not { } i) return false;
        Pause();
        Go(i);
        return true;
    }

    public void Toggle()
    {
        if (Playing) Pause(); else Play();
    }

    /// <summary>Play: a step every 2.4 s plus its move (at 1×), stopping at the last. From the last, it starts again at the first.</summary>
    public void Play()
    {
        if (Steps.Count < 2) return;
        if (AtEnd) Go(0, animated: false);
        Playing = true;
        nextAdvance = clock() + Wait;
        Changed?.Invoke();
    }

    public void Pause() => Pause(quiet: false);

    private void Pause(bool quiet)
    {
        if (!Playing) return;
        Playing = false;
        nextAdvance = null;
        if (!quiet) Changed?.Invoke();
    }

    /// <summary>When playing, the next step once its time has come (the controls call this a few times a second).</summary>
    public void Tick(double? at = null)
    {
        if (!Playing || nextAdvance is not { } due) return;
        var now = at ?? clock();
        if (now < due) return;
        if (AtEnd) { Pause(); return; }
        Next();
        if (AtEnd) Pause(); else nextAdvance = now + Wait;
    }
}
