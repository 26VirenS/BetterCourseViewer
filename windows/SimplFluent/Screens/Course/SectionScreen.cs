using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;

namespace Simpl.Screens;

/// <summary>
/// A section of a course or a group (the Mac's SectionScreen): its place in the pane under the course, and its pill on
/// the course's home — announcements, discussions, assignments, quizzes, the syllabus, modules, pages, files, people;
/// a section Simpl has no screen of its own for says so and opens the school's page for it. (A course's grades are a
/// screen of their own.)
/// </summary>
public sealed class SectionScreen : CoursePartScreen
{
    public SectionScreen(Engine.Engine engine, string ctx, string kind) : base(engine, PartFor(engine, ctx, kind)) { }

    private static CoursePart PartFor(Engine.Engine engine, string ctx, string kind) => kind switch
    {
        "announcements" => new CourseAnnouncementsPart(engine, ctx),
        "discussions" => new CourseDiscussionsPart(engine, ctx),
        "assignments" => new CourseAssignmentsPart(engine, ctx),
        "modules" => new CourseModulesPart(engine, ctx),
        "pages" => new CoursePagesPart(engine, ctx),
        "files" => new CourseFilesPart(engine, ctx, "", "Files"),
        "people" => new CoursePeoplePart(engine, ctx),
        "quizzes" => new CourseQuizzesPart(engine, ctx),
        "syllabus" => new CourseSyllabusPart(engine, ctx),
        _ => new CourseOtherPart(engine, ctx, kind),
    };
}

/// <summary>
/// A section's screen (the Mac's SectionLoaded): its page once the answer is in (the loading or error state until then),
/// its name with its course's under it, and beside them the school's own page for it — read again whenever what the
/// screens show may have changed (Reload, a tick, a hand-in).
/// </summary>
internal abstract class CourseListPart<T> : CoursePart where T : class
{
    private readonly string kind, call, fallback;
    protected readonly Loader<T> model;
    private FrameworkElement? commands;

    protected CourseListPart(Engine.Engine engine, string ctx, string kind, string call, string fallback) : base(engine, ctx)
    {
        this.kind = kind;
        this.call = call;
        this.fallback = fallback;
        model = Watch(new Loader<T>());
    }

    public override string Title => CourseKit.SectionTitle(engine, ctx, kind, fallback);
    public override string? Subtitle => model.Data is { } d ? Context(d) : null;
    public override FrameworkElement? Commands => commands ??= CourseKit.MoreButton(engine, () => CourseKit.SectionUrl(engine, ctx, kind), () => Title);
    public override void Refresh() => model.Ensure(engine, call, new { ctx });

    public override FrameworkElement Build() =>
        model.Data is { } d ? Content(d) : K.LoadState(model.Error, () => model.Reload(engine, call, new { ctx }));

    /// <summary>The course (or group) the section is of, for the line under the title.</summary>
    protected abstract string? Context(T d);

    protected abstract FrameworkElement Content(T d);

    /// <summary>An announcement or a discussion as a row of a card (the Mac's PostLink): a click opens it; its menu opens
    /// it, opens the school's own page for it, or copies its link.</summary>
    protected FrameworkElement PostLink(PostRow row, Color color)
    {
        void Open() => engine.GoTo(row.Url, row.Title);
        var b = CourseKit.RowLink(CourseKit.PostRowView(row, color, 3), Open);
        CourseKit.CanvasRowMenu(b, engine, row.Title, row.Url, Open);
        return b;
    }

    /// <summary>A piece of work as a row of a card (the Mac's WorkLink): a click opens it; its menu hands it in (or takes
    /// the quiz, or replies) or shows its feedback, opens the school's own page for it, or copies its link.</summary>
    protected FrameworkElement WorkLink(ARow row, Color color)
    {
        var b = CourseKit.RowLink(CourseKit.ARowView(row, color), () => engine.GoTo(row.Url, row.Title));
        CourseKit.WorkMenu(b, engine, row);
        Ahead.On(b, build => engine.PrepareWeb(row.Url, build));
        return b;
    }

    protected static string Count(int n) => n.ToString(CultureInfo.CurrentCulture);
}

// ---- announcements and discussions ----

/// <summary>A course's or a group's announcements, newest first: who posted each and when, its first lines, its replies.</summary>
internal sealed class CourseAnnouncementsPart : CourseListPart<PostListData>
{
    public CourseAnnouncementsPart(Engine.Engine engine, string ctx) : base(engine, ctx, "announcements", "announcements", "Announcements") { }

    protected override string? Context(PostListData d) => d.Context;

    protected override FrameworkElement Content(PostListData d)
    {
        var color = K.Hex(d.Color);
        if (d.Rows.Count == 0) return CourseKit.EmptyCard(d.Empty ?? "No announcements", SymbolRegular.Megaphone24);
        return CourseKit.RowsCard(CourseKit.Divided(d.Rows, r => PostLink(r, color), 54));
    }
}

/// <summary>A course's or a group's discussions — pinned, open, closed for comments — each with who started it, its
/// latest activity, its replies and how many are new to you; the kinds side by side on a wide page.</summary>
internal sealed class CourseDiscussionsPart : CourseListPart<DiscussionsData>
{
    public CourseDiscussionsPart(Engine.Engine engine, string ctx) : base(engine, ctx, "discussions", "discussions", "Discussions") { }

    protected override string? Context(DiscussionsData d) => d.Context;

    public override bool LayoutChanges(double before, double now) => (before >= 900) != (now >= 900);

    protected override FrameworkElement Content(DiscussionsData d)
    {
        var color = K.Hex(d.Color);
        if (d.Sections.Count == 0) return CourseKit.EmptyCard(d.Empty ?? "No discussions", SymbolRegular.ChatMultiple24);
        return CourseKit.SectionColumns(d.Sections, s => s.Rows.Count + 2,
            s => CourseKit.CardSection(s.Title, Count(s.Rows.Count), null, CourseKit.Divided(s.Rows, r => PostLink(r, color), 54)), Width());
    }
}

// ---- assignments, quizzes, the syllabus ----

/// <summary>A course's assignments — overdue (in red), upcoming, undated, past — each with where it stands; its menu
/// hands it in or shows its feedback. On a wide page the kinds sit in two columns, in their order.</summary>
internal sealed class CourseAssignmentsPart : CourseListPart<AssignmentsData>
{
    public CourseAssignmentsPart(Engine.Engine engine, string ctx) : base(engine, ctx, "assignments", "assignments", "Assignments") { }

    protected override string? Context(AssignmentsData d) => d.Context;

    public override bool LayoutChanges(double before, double now) => (before >= 900) != (now >= 900);

    protected override FrameworkElement Content(AssignmentsData d)
    {
        var color = K.Hex(d.Color);
        if (d.Sections.Count == 0) return CourseKit.EmptyCard(d.Empty ?? "No assignments", SymbolRegular.DocumentText24);
        return CourseKit.SectionColumns(d.Sections, s => s.Rows.Count + 2,
            s => CourseKit.CardSection(s.Title, Count(s.Rows.Count), null,
                CourseKit.Divided(s.Rows, r => WorkLink(r, s.Title == "Overdue" ? CourseKit.Red : color))), Width());
    }
}

/// <summary>A course's quizzes, Classic and New: their points, due dates, time limits and attempts, and where each stands.</summary>
internal sealed class CourseQuizzesPart : CourseListPart<RowsData>
{
    public CourseQuizzesPart(Engine.Engine engine, string ctx) : base(engine, ctx, "quizzes", "quizzes", "Quizzes") { }

    protected override string? Context(RowsData d) => d.Context;

    protected override FrameworkElement Content(RowsData d)
    {
        var color = K.Hex(d.Color);
        if (d.Rows.Count == 0) return CourseKit.EmptyCard(d.Empty ?? "No quizzes", Glyph.Work("quiz"));
        return CourseKit.RowsCard(CourseKit.Divided(d.Rows, r => WorkLink(r, color)));
    }
}

/// <summary>A course's syllabus to read, at a reading width, and every piece of its work that has a date, in date
/// order — the dated work in a column beside the syllabus on a wide page, under it on a narrow one.</summary>
internal sealed class CourseSyllabusPart : CourseListPart<RowsData>
{
    public CourseSyllabusPart(Engine.Engine engine, string ctx) : base(engine, ctx, "syllabus", "syllabus", "Syllabus") { }

    protected override string? Context(RowsData d) => d.Context;

    public override bool LayoutChanges(double before, double now) => (before >= 940) != (now >= 940);

    protected override FrameworkElement Content(RowsData d)
    {
        var html = d.Html ?? "";
        if (html.Length > 0 && d.Rows.Count > 0) return CourseKit.SideSplit(SyllabusText(html), DatedWork(d), Width(), 420, 940);
        FrameworkElement one;
        if (html.Length > 0) one = SyllabusText(html);
        else if (d.Rows.Count > 0) one = DatedWork(d);
        else one = CourseKit.EmptyCard(d.Empty ?? "The syllabus is empty", SymbolRegular.TextBulletListSquare24);
        return CourseKit.Capped(one, 920); // (a reading width, as the Mac's page of 920)
    }

    private FrameworkElement SyllabusText(string html) => K.Card(CourseKit.Rich(engine, html), new Thickness(24, 20, 24, 20));

    private FrameworkElement DatedWork(RowsData d)
    {
        var color = K.Hex(d.Color);
        return CourseKit.CardSection("Dated work", Count(d.Rows.Count), null, CourseKit.Divided(d.Rows, r => WorkLink(r, color)));
    }
}

// ---- pages ----

/// <summary>A course's or a group's pages, the front page first, each with when it was last edited.</summary>
internal sealed class CoursePagesPart : CourseListPart<PagesData>
{
    public CoursePagesPart(Engine.Engine engine, string ctx) : base(engine, ctx, "pages", "pages", "Pages") { }

    protected override string? Context(PagesData d) => d.Context;

    protected override FrameworkElement Content(PagesData d)
    {
        var color = K.Hex(d.Color);
        if (d.Rows.Count == 0) return CourseKit.EmptyCard(d.Empty ?? "No pages", SymbolRegular.DocumentOnePage24);
        return CourseKit.RowsCard(CourseKit.Divided(d.Rows, p =>
        {
            void Open() => engine.Push(Route.Page(ctx, p.Slug));
            var b = CourseKit.RowLink(CourseKit.InfoRow(p.Title, p.Sub, SymbolRegular.DocumentOnePage24, color), Open);
            CourseKit.CanvasRowMenu(b, engine, p.Title, $"/{ctx}/pages/{p.Slug}", Open);
            return b;
        }));
    }
}

// ---- a section Simpl does not draw ----

/// <summary>A section Simpl has no screen of its own for (the Mac's OtherSection): what it is, and the school's own page
/// for it in a window.</summary>
internal sealed class CourseOtherPart : CoursePart
{
    private readonly string kind;

    public CourseOtherPart(Engine.Engine engine, string ctx, string kind) : base(engine, ctx) => this.kind = kind;

    public override string Title => CourseKit.SectionTitle(engine, ctx, kind, CourseKit.Capitalized(kind));

    public override void Refresh() { }

    public override FrameworkElement Build()
    {
        var url = CourseKit.SectionUrl(engine, ctx, kind);
        var title = Title;
        var open = K.Button($"Open in {engine.LmsName}", () => { if (url != null) engine.OpenWebScreen(url, title); }, accent: true, icon: SymbolRegular.Globe24);
        open.IsEnabled = url != null;
        open.HorizontalAlignment = HorizontalAlignment.Center;
        var v = K.V(12,
            K.Icon(CourseKit.Section(kind), 40, B.Text3),
            K.Text(title, Ty.Subtitle).Center(),
            K.Wrapped($"Simpl doesn’t show this part of {engine.LmsName} itself yet.", Ty.Body, B.Text2).Center(),
            open.Margin(0, 4, 0, 0));
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.MaxWidth = 420;
        return new Grid { MinHeight = 360, Children = { v } };
    }
}
