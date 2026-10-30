using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

public sealed partial class DashboardScreen
{
    private static bool previewShot, previewPressed;
    /// <summary>(the screenshot suite's --open preview) the first row's id, and its quick look as that row is now: the
    /// page can be drawn again between the row's coming and the press.</summary>
    private static string? previewId;
    private static FrameworkElement? previewRow;
    private static Action? previewPress;

    private FrameworkElement Main(Today d, double w) => View switch
    {
        2 => ActivitySection(w),
        _ => Upcoming(w),
    };

    // ---- Coming up: the List view (the Mac's DashDayList) ----

    private FrameworkElement Upcoming(double w)
    {
        var l = list.Data;
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var heading = UpcomingHeading(l);
        v.Children.Add(heading);
        if (l == null)
        {
            v.Children.Add(list.Error is { } err ? K.Empty(SymbolRegular.ErrorCircle24, err) : K.Reading("Reading your planner…"));
            return v;
        }
        if (l.Days.Count == 0) { v.Children.Add(K.Card(K.Empty(SymbolRegular.WeatherSunny24, l.Empty ?? "Nothing coming up."))); return v; }
        var wide = w >= 760;
        var days = new Stack { Orientation = Orientation.Vertical, Spacing = 22 };
        var blocks = new List<DayRefs>();
        foreach (var day in l.Days)
        {
            var b = DayBlock(day, wide);
            days.Children.Add(b.Block);
            blocks.Add(b);
        }
        v.Children.Add(days);
        if (noting != null) noting.Upcoming = new UpcomingRefs(v, heading, UpcomingSaid(l), days, wide, blocks);
        return v;
    }

    /// <summary>Coming up's heading: what is left to do and what is done, and Hide (or Show) completed.</summary>
    private FrameworkElement UpcomingHeading(DashListData? l)
    {
        string? trailing = l is { Total: > 0 } ? $"{l.Total - (l.Done ?? 0)} to do · {l.Done ?? 0} done" : null;
        UIElement? action = null;
        if (l != null && ((l.Done ?? 0) > 0 || l.HideDone == true))
        {
            var hiding = l.HideDone == true;
            action = K.Button(hiding ? $"Show completed · {l.Done ?? 0}" : "Hide completed", () => { hideDone = !hiding; list.Again(); Refresh(); },
                icon: hiding ? SymbolRegular.Eye24 : SymbolRegular.CheckmarkCircle24);
        }
        return Heading.Section("Coming up", trailing, action);
    }

    /// <summary>A day of Coming up: its name and date, how many items, and its rows in a card.</summary>
    private DayRefs DayBlock(DashDay day, bool wide)
    {
        // (the day's date beside its name, saying only what the name does not — none beside Today: the greeting over
        //  the counters says it already)
        var date = string.IsNullOrEmpty(day.Date) || day.Title == "Today" ? "" : day.Date!.StartsWith(day.Title + ", ") ? day.Date![(day.Title.Length + 2)..] : day.Date!;
        var count = K.Text(Items(day.Rows.Count), Ty.Caption, B.Text3);
        var head = K.Columns("auto, *, auto",
            K.Text(day.Title, Ty.BodyStrong).Col(0),
            K.Text(date, Ty.Body, B.Text2).Margin(10, 0, 0, 0).Col(1),
            count.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2));
        head.Margin = new Thickness(4, 0, 4, 8);
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var kept = RowsInto(rows, 84, day.Rows.Select(r => (r.Id, DashLook(r, day.Title, wide), (FrameworkElement)WorkLine(r, day.Title, wide))));
        return new DayRefs(DayKey(day), K.V(0, head, K.Card(rows, new Thickness(4))), count, kept);
    }

    private static string Items(int n) => $"{n} {(n == 1 ? "item" : "items")}";

    /// <summary>
    /// The Dashboard's one row of work — Coming up's and Today's alike, so the same work reads the same wherever it
    /// shows: its kind's symbol on a tile of its course's colour; its course and kind (with its flags) over its title;
    /// its points and when it is due at its right — side by side in a wide list, the time over the points where the row
    /// is narrow (the rail beside the view).
    /// </summary>
    internal static Grid WorkFace(Color color, SymbolRegular symbol, string? course, string? kind, IEnumerable<WorkFlag>? flags, TextBlock title,
        string? points, string? when, bool stacked, double tileSize = 32, double whenWidth = 0, WorkPlan? plan = null)
    {
        var tile = K.IconTile(symbol, color, tileSize);
        tile.VerticalAlignment = VerticalAlignment.Center;
        var meta = new WrapPanel { Orientation = Orientation.Horizontal };
        // (the course and the kind as two pieces, so a narrow row — the rail — takes the kind onto a second line rather
        //  than cutting it: "F26-MATH 021 20 ·" then "Assignment")
        var hasCourse = !string.IsNullOrEmpty(course);
        var hasKind = !string.IsNullOrEmpty(kind);
        if (hasCourse) meta.Children.Add(K.Text(hasKind ? course + " ·" : course!, Ty.Caption, B.Text2).Margin(0, 0, hasKind ? 4 : 8, 0));
        if (hasKind) meta.Children.Add(K.Text(kind!, Ty.Caption, B.Text2).Margin(0, 0, 8, 0));
        foreach (var f in flags ?? Enumerable.Empty<WorkFlag>()) meta.Children.Add(K.Chip(f.Word, K.Tone(f.Kind)).Margin(0, 0, 6, 0));
        var words = K.V(2, title);
        if (meta.Children.Count > 0) words.Children.Insert(0, meta);
        // (0.3.60) the time it is planned for, pulled to
        if (plan != null) words.Children.Add(PlanRail.Pill(plan).Margin(0, 2, 0, 0));
        words.VerticalAlignment = VerticalAlignment.Center;
        if (stacked)
        {
            var right = K.V(1);
            right.VerticalAlignment = VerticalAlignment.Center;
            right.MaxWidth = 150;
            if (!string.IsNullOrEmpty(when)) right.Children.Add(K.Text(when!, Ty.Body, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Center));
            if (!string.IsNullOrEmpty(points)) right.Children.Add(K.Text(points!, Ty.Caption, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Center));
            return K.Columns("auto, *, auto", tile.Margin(0, 0, 12, 0).Col(0), words.Col(1), right.Margin(10, 0, 0, 0).Col(2));
        }
        var due = K.Text(when ?? "", Ty.Body, B.Text2);
        due.TextAlignment = TextAlignment.Right;
        due.MinWidth = whenWidth;
        var pts = K.Text(points ?? "", Ty.Body, B.Text2);
        pts.TextAlignment = TextAlignment.Right;
        pts.MinWidth = whenWidth >= 112 && !string.IsNullOrEmpty(points) ? 64 : 0;
        return K.Columns("auto, *, auto, auto",
            tile.Margin(0, 0, 12, 0).Col(0), words.Col(1),
            pts.Margin(8, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2),
            due.Margin(8, 0, 6, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(3));
    }

    /// <summary>A row of Coming up (the Mac's DashWorkLine): the check, then the Dashboard's row of work.</summary>
    private FrameworkElement WorkLine(DashRow row, string day, bool wide)
    {
        var done = Done(row.Id, row.Done);
        var check = new CheckCircle(done, done ? "Mark as not done" : "Mark as done");
        check.Toggled += on => Tick(row.Id, on);
        var title = K.Wrapped(row.Title, Ty.Body, done ? B.Text2 : B.Text, 2);
        if (done) title.Struck();
        var face = WorkFace(K.Hex(row.Color), Glyph.Work(row.Type ?? row.Kind), row.Course, row.Kind, row.Flags, title, row.Points, row.Due ?? row.Time, stacked: false, whenWidth: wide ? 112 : 90,
            plan: done ? null : row.Plan);
        var g = K.Columns("auto, *", check.Margin(6, 0, 12, 0).Col(0), face.Col(1));
        // (a click: its quick look, grown out of the row; a double-click: the work whole)
        var b = K.Subtle(g, null, new Thickness(4, 10, 4, 10));
        b.Click += (_, _) => WorkPreview.Press(WorkPreview.Dash(row, day, engine, on => Tick(row.Id, on)), b);
        // (the screenshot suite: --open preview opens the first row's quick look once it is on screen — that row as it
        //  is then, drawn again or not)
        if (Launch.Get("open") == "preview")
        {
            if (!previewShot)
            {
                previewShot = true;
                previewId = row.Id;
            }
            if (row.Id == previewId)
            {
                previewRow = b;
                previewPress = () => WorkPreview.Press(WorkPreview.Dash(row, day, engine, on => Tick(row.Id, on)), b);
                b.Loaded += async (_, _) =>
                {
                    if (previewPressed) return;
                    previewPressed = true;
                    await Task.Delay(1500);
                    for (var i = 0; i < 30 && previewRow is { IsLoaded: false }; i++) await Task.Delay(100);
                    previewPress?.Invoke();
                };
            }
        }
        if (done) return b;
        // (0.3.60) pulled to the right, the free times hang under it, and letting go on one plans it: the row held in a
        // holder that takes the mouse from it, so a pull is never also its click
        var holder = new Grid { Background = Brushes.Transparent, Children = { b } };
        PlanRail.Attach(holder, row.Id, row.Title, engine);
        var menu = new ContextMenu();
        holder.ContextMenu = menu;
        holder.ContextMenuOpening += (_, _) =>
        {
            menu.Items.Clear();
            var planned = row.Plan != null;
            menu.Items.Add(CalendarKit.Item(planned ? "Move Plan…" : "Plan a Time…", SymbolRegular.Clock24, () => PlanRail.Open(holder, row.Id, row.Title, engine)));
            if (planned) menu.Items.Add(CalendarKit.Item("Clear Plan", SymbolRegular.CalendarCancel24, () => PlanRail.Clear(row.Id, engine)));
        };
        return holder;
    }

    private static Brush Res(string key) => (Brush)(Application.Current.TryFindResource(key) ?? Brushes.Gray);

    private static Border Pill(string text, Color back, Color fore, SymbolRegular? icon = null)
    {
        var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = K.Brush(fore), VerticalAlignment = VerticalAlignment.Center };
        var row = K.H(4, icon is { } i ? K.Icon(i, 12, fore, filled: true) : null, t);
        return new Border { Background = K.Brush(back), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 3), Child = row };
    }

    // ---- Activity (the Mac's DashActivityList) ----

    private FrameworkElement ActivitySection(double w)
    {
        var a = activity.Data;
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var heading = Heading.Section("Recent activity", ActivitySaid(a));
        v.Children.Add(heading);
        if (a == null)
        {
            v.Children.Add(activity.Error is { } err ? K.Empty(SymbolRegular.ErrorCircle24, err) : K.Reading("Reading recent activity…"));
            return v;
        }
        if (a.Rows.Count == 0) { v.Children.Add(K.Card(K.Empty(SymbolRegular.Clock24, a.Empty ?? "No recent activity."))); return v; }
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var kept = RowsInto(rows, 72, a.Rows.Select(r => (r.Id, ActivityLook(r), (FrameworkElement)ActivityRow(r))));
        v.Children.Add(K.Card(rows, new Thickness(4)));
        if (noting != null) noting.Activity = new ActivityRefs(v, heading, ActivitySaid(a), kept);
        return v;
    }

    /// <summary>Recent activity's heading line: how many are new ("3 new"), or none.</summary>
    private string? ActivitySaid(DashActivityData? a)
    {
        var fresh = a?.Rows.Count(r => r.Unread == true && !seen.Contains(r.Id)) ?? 0;
        return fresh > 0 ? $"{fresh} new" : null;
    }

    private FrameworkElement ActivityRow(DashActivityRow row)
    {
        var unread = row.Unread == true && !seen.Contains(row.Id);
        var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 14, 8, 0), Visibility = unread ? Visibility.Visible : Visibility.Hidden };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, B.Accent);
        var tile = K.IconTile(Glyph.Activity(row.Type), K.Hex(row.Color), 34);
        tile.VerticalAlignment = VerticalAlignment.Top;
        var title = K.Wrapped(row.Title, Ty.Body, B.Text, 2);
        if (unread) title.FontWeight = FontWeights.SemiBold;
        var head = K.Columns("*, auto", title.Col(0), K.Text(row.When ?? "", Ty.Caption, B.Text2).Margin(8, 2, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Top).Col(1));
        var words = K.V(2, head);
        var sub = string.Join(" · ", new[] { row.Kind, row.Course }.Where(s => !string.IsNullOrEmpty(s)));
        if (sub.Length > 0) words.Children.Add(K.Text(sub, Ty.Caption, B.Text2));
        if (!string.IsNullOrEmpty(row.Preview)) words.Children.Add(K.Wrapped(row.Preview!, Ty.Caption, B.Text2, 2));
        var g = K.Columns("auto, auto, *", dot.Col(0), tile.Margin(0, 0, 12, 0).Col(1), words.Col(2));
        var r = row;
        return K.Subtle(g, () => OpenActivity(r), new Thickness(4, 10, 10, 10));
    }

    /// <summary>An activity item opened: its dot goes (here and on the web Dashboard), and it opens.</summary>
    private void OpenActivity(DashActivityRow row)
    {
        if (row.Unread == true && seen.Add(row.Id)) { seenTurn++; _ = engine.Act("dashSeen", new { id = row.Id }); Render(); }
        if (!string.IsNullOrEmpty(row.Url)) engine.OpenWeb(row.Url!, row.Title);
    }
}
