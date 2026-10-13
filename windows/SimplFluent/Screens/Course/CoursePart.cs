using System.Windows;

namespace Simpl.Screens;

/// <summary>
/// What one of a course's screens shows (a section's list, a folder of files): its title and the line under it, what
/// sits beside the title, what it reads and the content it builds — drawn by the <see cref="CoursePartScreen"/> that
/// holds it, as the Mac's section views sit inside its SectionScreen.
/// </summary>
internal abstract class CoursePart
{
    protected readonly Engine.Engine engine;
    protected readonly string ctx;

    /// <summary>Build the screen again (something was picked).</summary>
    internal Action Render = () => { };
    /// <summary>Build the screen again for an answer that came (several close together once; out of view, when shown).</summary>
    internal Action RenderSoon = () => { };
    /// <summary>The title, the line under it or the commands changed.</summary>
    internal Action HeaderChange = () => { };
    /// <summary>The width the page lays the content out in.</summary>
    internal Func<double> Width = () => 0;
    private readonly List<Action> drawn = new();
    private readonly List<Func<bool>> loading = new();

    /// <summary>A loader it watches is still reading.</summary>
    internal bool Reading => loading.Any(l => l());

    protected CoursePart(Engine.Engine engine, string ctx)
    {
        this.engine = engine;
        this.ctx = ctx;
    }

    public abstract string Title { get; }
    public virtual string? Subtitle => null;
    public virtual FrameworkElement? Commands => null;
    public virtual bool Scrolls => true;
    public virtual double? MaxContentWidth => null;
    public abstract void Refresh();
    public abstract FrameworkElement Build();
    public virtual bool LayoutChanges(double before, double now) => false;
    public virtual void Hide() { }

    /// <summary>A loader whose answers build the screen again (as a screen's own do: an answer that says what the last one
    /// did, not at all).</summary>
    protected Loader<T> Watch<T>(Loader<T> l) where T : class
    {
        l.Changed += () => { if (!l.LastSame) RenderSoon(); };
        drawn.Add(l.Drawn);
        loading.Add(() => l.Loading);
        return l;
    }

    /// <summary>The screen was built: the loaders watched are told (one still reading then shows its answer, whatever it says).</summary>
    internal void Drawn()
    {
        foreach (var d in drawn) d();
    }
}

/// <summary>
/// A screen that shows a <see cref="CoursePart"/>: its header follows what the part says (the course's own name for a
/// section, under its title, once the answer is in), and it is built again as the part's answers come.
/// </summary>
public abstract class CoursePartScreen : Screen
{
    private readonly CoursePart part;
    private string? shownTitle, shownSub;

    private protected CoursePartScreen(Engine.Engine engine, CoursePart part) : base(engine)
    {
        this.part = part;
        part.Render = Render;
        part.RenderSoon = RenderSoon;
        part.HeaderChange = HeaderChange;
        part.Width = () => Width;
        WhenBuilt(part.Drawn);
        WhenReading(() => part.Reading);
    }

    public override string Title => part.Title;
    public override string? Subtitle => part.Subtitle;
    public override FrameworkElement? Commands => part.Commands;
    public override bool Scrolls => part.Scrolls;
    public override double? MaxContentWidth => part.MaxContentWidth;
    public override void Refresh() => part.Refresh();
    protected override bool LayoutChanges(double before, double now) => part.LayoutChanges(before, now);

    public override void Hide()
    {
        base.Hide();
        part.Hide();
    }

    protected override FrameworkElement Build()
    {
        var content = part.Build();
        if (part.Title != shownTitle || part.Subtitle != shownSub)
        {
            shownTitle = part.Title;
            shownSub = part.Subtitle;
            HeaderChange();
        }
        return content;
    }
}
