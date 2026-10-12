namespace Simpl.UI;

/// <summary>
/// A spring, as SwiftUI's (the showcase's Spring, windows/GlassNative): a value moving to its target with a response
/// (seconds) and a damping ratio (1: no overshoot), from where it is and with the velocity it has — so motion can be
/// grabbed and redirected at any moment.
/// </summary>
public sealed class Spring
{
    public float Value, Velocity, Target;
    public float Response, Damping;
    public long LastUsed;

    public Spring(float value, float response = 0.35f, float damping = 0.86f)
    {
        Value = Target = value;
        Response = response;
        Damping = damping;
    }

    public void To(float target) => Target = target;

    public void To(float target, float velocity) { Target = target; Velocity = velocity; }

    public void Snap(float v) { Value = Target = v; Velocity = 0; }

    public bool Settled => MathF.Abs(Value - Target) < 0.0005f && MathF.Abs(Velocity) < 0.005f;

    public void Step(float dt)
    {
        if (Settled) { Value = Target; Velocity = 0; return; }
        var k = MathF.Pow(2 * MathF.PI / MathF.Max(Response, 0.01f), 2);
        var c = 4 * MathF.PI * Damping / MathF.Max(Response, 0.01f);
        var steps = Math.Max(1, (int)MathF.Ceiling(dt / 0.004f));
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            var a = -k * (Value - Target) - c * Velocity;
            Velocity += a * h;
            Value += Velocity * h;
        }
    }

    /// <summary>Where a flick heads (Apple's projection: momentum decaying as a scroll view's does).</summary>
    public static float Project(float v, float rate = 0.998f) => v / 1000f * rate / (1 - rate);
}
