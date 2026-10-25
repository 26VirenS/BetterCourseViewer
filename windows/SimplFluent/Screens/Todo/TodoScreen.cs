using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Screens;

/// <summary>
/// To Do (the Mac's TodoView): the next seven days of work and your own tasks, grouped by date, priority or course, a
/// card of rows under each group's heading. A tick marks one done (it stays a moment, then moves where it now belongs),
/// the flag at a row's end sets its priority, a click opens its quick look under it (a double-click opens the work), a
/// right-click has the rest — and New task adds a task of your own. On a narrow page how much is done and the grouping
/// sit over the list; on a wide one the list scrolls in its own column and a side column stays put beside it, level with
/// the list's first card: how much is done, the grouping, what is left by day, by course and by priority, and New task.
/// </summary>
public sealed partial class TodoScreen : Screen
{
    /// <summary>How wide the page is before the side column stands beside the list (the Mac's sideAt).</summary>
    private const double SideAt = 940;

    private readonly Loader<TodoData> data;
    /// <summary>The grouping and Show completed as just chosen, shown at once while the page regroups.</summary>
    private string? pendingGroup;
    private bool? pendingShowDone;
    private int regrouping;
    private FrameworkElement? commands;
    private string? subtitle;
    private bool shotAsked;

    // the page's own scrolling parts (kept from build to build, so where they are scrolled to is kept too)
    private readonly ScrollViewer listScroller;
    private readonly ScrollViewer sideScroller;
    private readonly ContentControl listHost = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    /// <summary>The list's first card, and the side column: its top is kept level with that card's.</summary>
    private FrameworkElement? firstCard, side;
    private double sideTop = 38;

    public TodoScreen(Engine.Engine engine) : base(engine)
    {
        data = Watch(new Loader<TodoData>());
        listScroller = new ScrollViewer { Content = listHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        sideScroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        sideScroller.SizeChanged += (_, _) => PlaceSide();
    }

    public override string Title => "To Do";

    /// <summary>What is open across how many courses (the Mac's heading line and window subtitle).</summary>
    public override string? Subtitle => subtitle;

    /// <summary>The list and the side column scroll on their own.</summary>
    public override bool Scrolls => false;

    /// <summary>Calendar and New task, beside the title (the Mac's toolbar).</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            if (commands != null) return commands;
            var calendar = K.Button("Calendar", () => engine.Go(Place.Calendar), icon: SymbolRegular.CalendarLtr24);
            calendar.ToolTip = "Calendar";
            var add = K.Button("New task", () => engine.AskNewTask(), icon: SymbolRegular.Add24);
            add.ToolTip = "New task (Ctrl+N)";
            return commands = K.H(8, calendar, add);
        }
    }

    public override void Refresh() => data.Ensure(engine, "todo");


    // ---- the page ----

    private static bool Wide(double w) => w + 80 >= SideAt;
    private static bool SideBySide(double w) => w >= 520;
    private static double Pad(double w) => w + 80 < 560 ? 20 : 40;

    protected override bool LayoutChanges(double before, double now) =>
        Wide(before) != Wide(now) || SideBySide(before) != SideBySide(now) || Pad(before) != Pad(now);

    protected override FrameworkElement Build()
    {
        var d = data.Data;
        // (what is open across how many courses; how much is done is the progress card's to say, once)
        var sub = d == null ? null : TodoWords.Text(d.Sub);
        if (sub != subtitle) { subtitle = sub; HeaderChange(); }
        if (d == null) return K.LoadState(data.Error, () => data.Reload(engine, "todo"));

        var width = Math.Max(320, Width);
        var pad = Pad(width);
        rowButtons.Clear();
        planRows.Clear();
        firstCard = null;
        side = null;
        var root = new Grid();
        if (Wide(width))
        {
            // the list in its own column; the side column beside it, its top level with the list's first card
            foreach (var c in new[] { "*", "12", "320", pad.ToString(System.Globalization.CultureInfo.InvariantCulture) })
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = K.Length(c) });
            listScroller.Padding = new Thickness(pad, 0, 12, 0);
            listHost.Margin = new Thickness(0, 0, 0, 40); // (room at the foot that scrolls with the list)
            listHost.Content = List(d);
            var overview = Overview(d);
            overview.Margin = new Thickness(0, sideTop, 0, 24);
            overview.SizeChanged += (_, _) => PlaceSide();
            side = overview;
            sideScroller.Content = overview;
            root.Children.Add(Adopt(listScroller).Col(0));
            root.Children.Add(Adopt(sideScroller).Col(2));
            if (firstCard != null) firstCard.Loaded += (_, _) => PlaceSide();
        }
        else
        {
            listScroller.Padding = new Thickness(pad, 0, pad, 0);
            listHost.Margin = new Thickness(0, 0, 0, 40);
            sideScroller.Content = null;
            listHost.Content = K.V(22, Summary(d, width), List(d));
            root.Children.Add(Adopt(listScroller));
        }
        ShotPreview(d);
        ShotPlan(d);
        return root;
    }

    /// <summary>Out of the page it was last in (an element has one parent): the scrollers go from build to build.</summary>
    private static T Adopt<T>(T e) where T : FrameworkElement
    {
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case Decorator dc: dc.Child = null; break;
            case ContentControl cc: cc.Content = null; break;
        }
        return e;
    }

    /// <summary>(The Mac's PinnedSide) the side column's top level with the list's first card; taller than the room under
    /// that, it moves up, its foot kept clear of the window's — and only taller than the whole page does it scroll.</summary>
    private void PlaceSide()
    {
        if (firstCard is not { IsLoaded: true } card || side is not { IsLoaded: true } s) return;
        double top;
        try { top = card.TranslatePoint(new Point(0, 0), listHost).Y; }
        catch (InvalidOperationException) { return; }
        var at = Math.Max(0, Math.Min(top, sideScroller.ActualHeight - s.ActualHeight - 24));
        if (Math.Abs(s.Margin.Top - at) < 0.5) return;
        sideTop = at;
        s.Margin = new Thickness(0, at, 0, 24);
    }

    // ---- reading and acting ----

    private WorkRow? FindRow(string id) => data.Data?.Sections.SelectMany(s => s.Rows).FirstOrDefault(r => r.Id == id);

    /// <summary>Grouped another way, or completed work shown or hidden: the choice shows at once, and the page regroups.</summary>
    private async void Regroup(string? group = null, bool? showDone = null)
    {
        if (group != null) pendingGroup = group;
        if (showDone != null) pendingShowDone = showDone;
        var ticket = ++regrouping;
        Render();
        var args = new JsonObject();
        if (group != null) args["group"] = group;
        if (showDone is { } sd) args["showDone"] = sd;
        try
        {
            var (d, text) = await engine.CallWithText<TodoData>("todo", args);
            if (ticket == regrouping) data.Take(d, text);
        }
        catch (Exception e) { Log.Write("todo regroup: " + e.Message); }
        if (ticket != regrouping) return; // (a later choice is on its way: it clears these)
        pendingGroup = null;
        pendingShowDone = null;
        Render();
    }

    /// <summary>Ticked off (or back on): shown at once, the count with it; the school told; read again after a moment, so the
    /// tick shows before the row moves. Refused, it is put back.</summary>
    private async void Toggle(WorkRow row, bool done)
    {
        var id = row.Id;
        Mutate(id, r => r.Done = done);
        var act = engine.Act("complete", new { id, done });
        await Task.Delay(Motion.On ? Motion.Medium : 0); // (the tick fills before the row is drawn again)
        Render();
        if (!await act)
        {
            Mutate(id, r => r.Done = !done);
            Render();
            return;
        }
        await Task.Delay(450);
        data.Again();
        engine.DataChanged();
    }

    /// <summary>A priority set: shown at once, kept by the page (it never reaches the school), read again.</summary>
    private async void SetPriority(WorkRow row, int level)
    {
        Mutate(row.Id, r =>
        {
            r.Pri = level;
            r.PriShort = TodoPriority.Of(level).Short;
        });
        Render();
        await engine.Act("setPriority", new { id = row.Id, level });
        data.Reload(engine, "todo");
    }

    /// <summary>A task of your own deleted (with the rest of its repeats, if asked), and the page read again.</summary>
    private async void Delete(WorkRow row, bool series)
    {
        if (await engine.Act("deleteTask", new { id = row.Id, series }))
        {
            data.Again();
            engine.DataChanged();
        }
    }

    /// <summary>A row changed before the page has said so (a tick, a priority), and the count of what is done with it.</summary>
    private void Mutate(string id, Action<WorkRow> change)
    {
        if (data.Data is not { } d) return;
        foreach (var s in d.Sections)
        {
            if (s.Rows.FirstOrDefault(r => r.Id == id) is not { } row) continue;
            var was = row.Done;
            change(row);
            if (was != row.Done)
            {
                d.Done = Math.Clamp(d.Done + (row.Done ? 1 : -1), 0, d.Total);
                d.Pct = d.Total > 0 ? (int)Math.Round((double)d.Done / d.Total * 100, MidpointRounding.AwayFromZero) : 0;
                // (the progress card's words with it, as the page writes them, until it says so itself)
                d.Left = Math.Max(0, d.Total - d.Done);
                d.LeftText = $"{d.Left} left";
                d.DoneText = $"{d.Done} of {d.Total} done";
                if (d.Overdue is int late && TodoWords.Date(row.Date) is { } due && due < DateTime.Now)
                {
                    d.Overdue = Math.Max(0, late + (row.Done ? -1 : 1));
                    d.OverdueText = d.Overdue > 0 ? $"{d.Overdue} overdue" : "";
                }
            }
            break;
        }
    }

    /// <summary>(0.3.60) The screenshot suite (--open plan): the rail of free times under the first piece of work still
    /// to do that is not late, once the page has settled.</summary>
    private async void ShotPlan(TodoData d)
    {
        if (shotAsked || Launch.Get("open") != "plan") return;
        // (near the top, as the Mac's picture: the rail has room under it)
        if (d.Sections.Where(s => s.Title != "Overdue").SelectMany(s => s.Rows).FirstOrDefault(r => !r.Done && r.Custom != true) is not { } row) return;
        shotAsked = true;
        // (the picture waits for the rail: it is not taken while this is under way)
        Shot.Began();
        try
        {
            await Task.Delay(1500);
            if (Launch.Take("plan") == null) return;
            if (planRows.TryGetValue(row.Id, out var at) && at.IsLoaded)
            {
                at.BringIntoView();
                await Task.Delay(400); // (scrolled to first: the rail hangs where the row is)
                // (held open: the suite starts other copies of Simpl beside this one, and their taking the focus would
                //  close it, as SettingsWindow's find list for its picture)
                if (at.IsLoaded) PlanRail.Open(at, row.Id, row.Title, engine, held: true);
            }
        }
        finally { Shot.Ended(); }
    }

    /// <summary>The screenshot suite (--open preview): the first row's quick look, once the page has settled.</summary>
    private async void ShotPreview(TodoData d)
    {
        if (shotAsked || Launch.Get("open") != "preview") return;
        if (d.Sections.FirstOrDefault(s => s.Rows.Count > 0)?.Rows[0] is not { } first) return;
        shotAsked = true;
        await Task.Delay(1200);
        if (Launch.Take("preview") == null) return;
        if (FindRow(first.Id) is { } row) Press(row);
    }
}
