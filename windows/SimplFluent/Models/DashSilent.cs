#nullable enable
namespace Simpl.Models;

/// <summary>(0.3.84) What the engine answers for Silent mode (native-app.js dashSilent): what is still ahead, soonest first.</summary>
public sealed class DashSilentData
{
    public List<DashSilentRow> Rows { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class DashSilentRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>When it is, in milliseconds since 1970.</summary>
    public double At { get; set; }
    /// <summary>"Due", "Starts" (an event), or nothing (a to-do date, a task of one's own).</summary>
    public string? Verb { get; set; }
    public string? Course { get; set; }
    public string? Url { get; set; }
    public bool Done { get; set; }

    public DateTime When => DateTimeOffset.FromUnixTimeMilliseconds((long)At).LocalDateTime;
}
