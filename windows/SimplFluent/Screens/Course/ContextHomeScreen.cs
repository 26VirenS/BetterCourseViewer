using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;

namespace Simpl.Screens;

/// <summary>
/// A course's home, or a group's (the Mac's ContextHome): under its title, on the page itself, its name, its term, its
/// teachers and how much is still to do, with its grade ring at the right (clicked, the course's grades); its sections
/// as pills that run along a line and wrap, each a place in the pane; then the work still to do, the latest
/// announcements and what it says about itself, with the front page, what was handed in lately and its other links in a
/// column beside them on a wide page (under them on a narrow one).
/// </summary>
public sealed class ContextHomeScreen : Screen
{
    private readonly string ctx;
    private readonly Loader<HomeData> data;
    private FrameworkElement? commands;
    private string? shownTitle;

    public ContextHomeScreen(Engine.Engine engine, string ctx) : base(engine)
    {
        this.ctx = ctx;
        data = Watch(new Loader<HomeData>());
    }

    public override string Title => data.Data?.Title is { Length: > 0 } t ? t : ScreenFactory.ContextName(engine, ctx);

    /// <summary>Its colour, in the square before its title.</summary>
    public override Color? TitleColor => Support.Appearance.Hex(data.Data?.Color) ?? CourseKit.ContextColor(engine, ctx);

    /// <summary>The school's own page for it, and its link (the Mac's CanvasMenu).</summary>
    public override FrameworkElement? Commands => commands ??= CourseKit.MoreButton(engine, () => "/" + ctx, () => Title);

    public override void Refresh() => data.Ensure(engine, "home", new { ctx });

    protected override bool LayoutChanges(double before, double now) => (before >= 860) != (now >= 860);

    protected override FrameworkElement Build()
    {
        if (data.Data is not { } d) return K.LoadState(data.Error, () => data.Reload(engine, "home", new { ctx }));
        if (Title != shownTitle) { shownTitle = Title; HeaderChange(); }
        var color = K.Hex(d.Color);
        var page = K.V(24, Head(d, color));
        var tiles = d.Sections.Where(s => s.Kind != "home").ToList(); // (the home is where this is)
        if (tiles.Count > 0) page.Children.Add(Pills(tiles, color));
        if (Lower(d, color) is { } lower) page.Children.Add(lower);
        return page;
    }

    // ---- the head of the home ----

    /// <summary>Its name (only when it says something the title and the term line do not — "MATH-021-20" under
    /// "F26-MATH 021 20" is the same name twice), its term, its teachers, how much is still to do — every line with its
    /// symbol in one column — and its grade ring (a group: its symbol on a tile of its colour).</summary>
    private FrameworkElement Head(HomeData d, Color color)
    {
        var facts = K.V(8);
        if (CourseKit.SaysMore(d.Name, d.Title, d.Sub)) facts.Children.Add(K.Wrapped(d.Name!, Ty.BodyLarge, B.Text2, 2));
        if (!string.IsNullOrEmpty(d.Sub)) facts.Children.Add(CourseKit.Fact(d.Kind == "groups" ? SymbolRegular.People24 : SymbolRegular.Book24, d.Sub!));
        if (!string.IsNullOrEmpty(d.Teachers)) facts.Children.Add(CourseKit.Fact(SymbolRegular.PersonCircle24, d.Teachers!));
        if (d.OpenCount is int n && n > 0) facts.Children.Add(CourseKit.Fact(SymbolRegular.TaskListLtr24, $"{n} still to do", color));
        facts.VerticalAlignment = VerticalAlignment.Center;
        FrameworkElement right = d.Kind == "courses" ? GradeRing(d, color) : K.IconTile(SymbolRegular.People24, color, 72);
        right.VerticalAlignment = VerticalAlignment.Center;
        return K.Columns("*, auto", facts.Margin(0, 0, 24, 0).Col(0), right.Col(1));
    }

    /// <summary>The grade ring as a button (the Mac's RingButtonStyle): a wash under the pointer and a little lift, a
    /// give under a click (the wash alone with Windows' animation effects off).</summary>
    private FrameworkElement GradeRing(HomeData d, Color color)
    {
        var letter = d.Letter ?? (d.Score == null ? "–" : null);
        var inside = K.V(1);
        if (letter != null)
        {
            var t = K.Text(letter, Ty.Subtitle).Center();
            t.Foreground = CourseKit.Ink(color);
            inside.Children.Add(t);
        }
        if (!string.IsNullOrEmpty(d.ScoreText)) inside.Children.Add(K.Text(d.ScoreText!, Ty.Caption, B.Text2).Center());
        inside.HorizontalAlignment = HorizontalAlignment.Center;
        inside.VerticalAlignment = VerticalAlignment.Center;
        inside.Margin = new Thickness(12);
        // (the track in the course's own colour, as on its card; with no score, why — "No grade yet" — in the tooltip)
        var ring = K.Ring(d.Score is { } s ? s / 100 : null, color, 104, 9, inside, tinted: true);
        var b = K.Subtle(ring, () => engine.Go(Place.Section(ctx, "grades")), new Thickness(0), 52);
        b.Width = 104;
        b.Height = 104;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        var why = d.Score == null && d.ScoreWhy is { Length: > 0 } w ? w : null;
        b.ToolTip = why != null ? $"Grades · {why}" : "Grades";
        AutomationProperties.SetName(b, $"Grades, {d.ScoreText ?? why ?? "no score"}{(d.Letter != null ? ", " + d.Letter : "")}");
        var scale = new ScaleTransform(1, 1);
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        b.RenderTransform = scale;
        void To(double v)
        {
            if (!Motion.On) return;
            Motion.To(scale, ScaleTransform.ScaleXProperty, v, Motion.Normal, Motion.Decelerate);
            Motion.To(scale, ScaleTransform.ScaleYProperty, v, Motion.Normal, Motion.Decelerate);
        }
        b.MouseEnter += (_, _) => To(1.03);
        b.MouseLeave += (_, _) => To(1);
        b.PreviewMouseLeftButtonDown += (_, _) => To(0.96);
        b.PreviewMouseLeftButtonUp += (_, _) => To(b.IsMouseOver ? 1.03 : 1);
        return b;
    }

    /// <summary>The sections as pills that run along a line and wrap: a click opens one, its menu has the school's page.</summary>
    private FrameworkElement Pills(List<SectionLink> sections, Color color)
    {
        var flow = new WrapPanel { Margin = new Thickness(0, 0, -10, -10) };
        foreach (var s in sections)
        {
            var kind = s.Kind;
            void Open() => engine.Go(Place.Section(ctx, kind));
            var label = K.Text(s.Label, Ty.Body);
            label.FontWeight = FontWeights.Medium;
            var pill = K.CardButton(K.H(8, K.Icon(CourseKit.Section(kind), 16, K.Darkened(color)), label), Open, 18, new Thickness(14, 8, 16, 9));
            pill.Margin = new Thickness(0, 0, 10, 10);
            pill.HorizontalAlignment = HorizontalAlignment.Left;
            CourseKit.CanvasRowMenu(pill, engine, s.Label, CourseKit.SectionUrl(engine, ctx, kind), Open);
            flow.Children.Add(pill);
        }
        return flow;
    }

    // ---- the two columns ----

    /// <summary>The work, the news and what it says about itself in the main column; the front page, what was handed in
    /// and the other links in the side column — under the main on a narrow page, or the only one when the other is empty.</summary>
    private FrameworkElement? Lower(HomeData d, Color color)
    {
        var main = d.Kind == "courses" || d.Announcements.Count > 0 || !string.IsNullOrEmpty(d.Html);
        var side = d.Front != null || d.Done is { Count: > 0 } || d.More is { Count: > 0 };
        if (main && side) return CourseKit.SideSplit(MainColumn(d, color), SideColumn(d, color), Width, 360, 860, 26, 24);
        if (main) return MainColumn(d, color);
        if (side) return SideColumn(d, color);
        return null;
    }

    private FrameworkElement MainColumn(HomeData d, Color color)
    {
        var v = K.V(24);
        if (d.Kind == "courses") v.Children.Add(StillToDo(d, color));
        if (d.Announcements.Count > 0) v.Children.Add(Announcements(d, color));
        if (!string.IsNullOrEmpty(d.Html)) v.Children.Add(CourseKit.CardSection("About", null, null, CourseKit.Rich(engine, d.Html!), 18));
        return v;
    }

    private FrameworkElement SideColumn(HomeData d, Color color)
    {
        var v = K.V(24);
        if (d.Front is { } f) v.Children.Add(FrontPageCard(f));
        if (d.Done is { Count: > 0 } done) v.Children.Add(CourseKit.CardSection("Handed in lately", null, null, CourseKit.Divided(done, r => WorkRow(r, color))));
        if (d.More is { Count: > 0 } more) v.Children.Add(MoreLinks(more, d, color));
        return v;
    }

    private FrameworkElement StillToDo(HomeData d, Color color)
    {
        UIElement? all = d.OpenCount is int n && n > d.Open.Count ? CourseKit.HeadLink($"All {n}", () => engine.Go(Place.Section(ctx, "assignments"))) : null;
        var body = K.V(0);
        if (d.Open.Count == 0) body.Children.Add(K.Empty(SymbolRegular.CheckmarkCircle24, "You are all caught up here."));
        body.Children.Add(CourseKit.Divided(d.Open, r => WorkRow(r, color)));
        return CourseKit.CardSection("Still to do", null, all, body);
    }

    private FrameworkElement Announcements(HomeData d, Color color)
    {
        UIElement? all = d.Sections.Any(s => s.Kind == "announcements") ? CourseKit.HeadLink("All announcements", () => engine.Go(Place.Section(ctx, "announcements"))) : null;
        var rows = CourseKit.Divided(d.Announcements, a =>
        {
            void Open() => engine.OpenWeb(a.Url, a.Title);
            var row = CourseKit.RowLink(CourseKit.PostRowView(a, color, 3), Open);
            CourseKit.CanvasRowMenu(row, engine, a.Title, a.Url, Open);
            return row;
        }, 54);
        return CourseKit.CardSection("Latest announcements", null, all, rows);
    }

    private FrameworkElement FrontPageCard(FrontPage f)
    {
        var slug = f.Slug ?? "";
        void Open() => engine.Push(Route.Page(ctx, slug));
        var words = K.V(5, K.Wrapped(f.Title, Ty.BodyStrong));
        if (!string.IsNullOrEmpty(f.Excerpt)) words.Children.Add(K.Wrapped(f.Excerpt!, Ty.Caption, B.Text2, 6));
        var row = CourseKit.RowLink(words, Open);
        CourseKit.CanvasRowMenu(row, engine, f.Title, slug.Length == 0 ? $"/{ctx}/wiki" : $"/{ctx}/pages/{slug}", Open);
        return CourseKit.CardSection("Front page", null, null, row);
    }

    /// <summary>The course's other links: a tool, or another site, opens in a Simpl window of its own — the window's
    /// symbol at its end, as External Tools' chips and a module's tool have it —; one of the school's own pages opens
    /// here, a chevron at its end.</summary>
    private FrameworkElement MoreLinks(List<MoreLink> more, HomeData d, Color color)
    {
        var rows = CourseKit.Divided(more, m =>
        {
            void Open() => OpenMore(m, d);
            var window = m.Tool != null || m.External == true;
            var end = window ? CourseKit.OwnWindowMark() : K.Icon(SymbolRegular.ChevronRight20, 12, B.Text3);
            var row = CourseKit.RowLink(CourseKit.InfoRow(m.Label, null, m.External == true ? SymbolRegular.PuzzlePiece24 : SymbolRegular.Link24, color, end), Open);
            CourseKit.CanvasRowMenu(row, engine, m.Label, m.Url, Open);
            return row;
        });
        return CourseKit.CardSection("More", null, null, rows);
    }

    private FrameworkElement WorkRow(ARow r, Color color)
    {
        var row = CourseKit.RowLink(CourseKit.ARowView(r, color), () => engine.GoTo(r.Url, r.Title));
        CourseKit.WorkMenu(row, engine, r);
        Ahead.On(row, build => engine.PrepareWeb(r.Url, build));
        return row;
    }

    private void OpenMore(MoreLink m, HomeData d)
    {
        if (m.Tool is { } t && d.Kind == "courses") engine.OpenTool(ToolLaunch.CourseTool(CourseKit.IdOf(ctx), t, m.Label));
        else engine.GoTo(m.Url, m.Label);
    }
}
