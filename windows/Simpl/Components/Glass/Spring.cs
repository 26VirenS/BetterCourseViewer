using System.Windows.Media;

namespace Simpl.Components.Glass;

/// <summary>
/// A spring, as SwiftUI's: a value that moves toward its target with a <see cref="Response"/> (seconds, how quickly)
/// and a <see cref="Damping"/> ratio (1: no overshoot; under 1: a little bounce). It starts from where it is and keeps
/// its velocity when the target moves, so motion can be grabbed and redirected at any moment — never restarted.
/// Driven by the display's frame clock (CompositionTarget.Rendering), and still once it has settled.
/// </summary>
public sealed class Spring
{
    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; private set; }
    public double Response { get; set; }
    public double Damping { get; set; }
    public event Action<double>? Changed;

    private bool running;
    private TimeSpan last;

    public Spring(double value, double response = 0.35, double damping = 0.86)
    {
        Value = Target = value;
        Response = response;
        Damping = damping;
    }

    /// <summary>On its way to <paramref name="target"/>, from where it is (with a velocity handed over, a flick's).</summary>
    public void To(double target, double? velocity = null)
    {
        Target = target;
        if (velocity.HasValue) Velocity = velocity.Value;
        Start();
    }

    /// <summary>Right there, at once (a drag: the value follows the pointer one to one).</summary>
    public void Snap(double value, double velocity = 0)
    {
        Value = Target = value;
        Velocity = velocity;
        Changed?.Invoke(Value);
    }

    private void Start()
    {
        if (running) return;
        running = true;
        last = TimeSpan.Zero;
        CompositionTarget.Rendering += Tick;
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (last == TimeSpan.Zero) { last = now; return; }
        var dt = Math.Min((now - last).TotalSeconds, 1.0 / 20);
        last = now;
        if (dt <= 0) return;
        // SwiftUI's spring: stiffness (2π / response)², damping 4π·ratio / response, unit mass; small steps for stability
        var k = Math.Pow(2 * Math.PI / Math.Max(Response, 0.01), 2);
        var c = 4 * Math.PI * Damping / Math.Max(Response, 0.01);
        var steps = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            var a = -k * (Value - Target) - c * Velocity;
            Velocity += a * h;
            Value += Velocity * h;
        }
        if (Math.Abs(Value - Target) < 0.0005 && Math.Abs(Velocity) < 0.005)
        {
            Value = Target;
            Velocity = 0;
            running = false;
            CompositionTarget.Rendering -= Tick;
        }
        Changed?.Invoke(Value);
    }

    /// <summary>The resting place a flick heads for (Apple's projection: momentum decaying as a scroll view's does).</summary>
    public static double Project(double velocity, double decelerationRate = 0.998) =>
        velocity / 1000.0 * decelerationRate / (1 - decelerationRate);
}
