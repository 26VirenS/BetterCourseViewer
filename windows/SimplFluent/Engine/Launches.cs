namespace Simpl.Engine;

/// <summary>An external tool, or a page of the school's own, to open in a window of its own (the Mac's ToolLaunch).</summary>
public sealed record ToolLaunch(string Title, IReadOnlyDictionary<string, string> Args)
{
    public Guid Id { get; } = Guid.NewGuid();
    public static ToolLaunch Assignment(string course, string id, string title) => new(title, new Dictionary<string, string> { ["course"] = course, ["assignment"] = id });
    public static ToolLaunch ModuleItem(string course, string id, string title) => new(title, new Dictionary<string, string> { ["course"] = course, ["moduleItem"] = id });
    public static ToolLaunch CourseTool(string course, string id, string title) => new(title, new Dictionary<string, string> { ["course"] = course, ["tool"] = id });
    public static ToolLaunch Page(string url, string title) => new(title, new Dictionary<string, string> { ["page"] = url });
}

/// <summary>A Classic quiz in the app's own quiz screen (the Mac's QuizLaunch).</summary>
public sealed record QuizLaunch(string Course, string Quiz, string Title)
{
    public Guid Id { get; } = Guid.NewGuid();
    public bool Begin { get; init; }
    public int? StartAt { get; init; }
    public bool Feedback { get; init; }
    public bool Review { get; init; }
}

/// <summary>A Knewton Alta or Macmillan Learning assignment in its popup (the Mac's AltaLaunch).</summary>
public sealed record AltaLaunch(string Course, string Assignment, string Title, string? Page = null)
{
    public Guid Id { get; } = Guid.NewGuid();
}
