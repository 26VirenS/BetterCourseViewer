using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// (0.3.63) To Do beside the week — (0.3.64) To Do itself, in To Do &amp; Calendar's panel beside the calendar in every view
/// (the web's 2.99.112, todo.js in its panel mode; the To Do screen is gone): over its list a small ring of how much of the
/// week is done with the most pressing word beside it ("1 overdue", "3 due today", "12 to do", "All done") and "n of m
/// done" under it; Add a task (New task's sheet: what, its day, a course, a repeat and until when, its priority); then the
/// groups — by date, by priority or by course, completed work shown or hidden, behind the ⋯ in its head (To Do's own
/// choices, kept by the page). A row is ticked off where it is, its quick look opened with a click, its own ⋯ (shown under
/// the pointer) or a right-click holding its priority, a time planned, Submit (a plain assignment), and Delete (a task of
/// your own; one of a series asks which) or Dismiss and Restore (Canvas's work); a small flag on it says its priority. A
/// row dragged onto a time of the week is planned there; onto a day of the month or the list, at that day's first free
/// time (native-app.js planDay); "Planned …" said under the list. Made, moved or cleared, a plan is said on its row as the
/// list is read again, in place. The commands' To Do button hides or shows it (kept); a page too narrow for both has it
/// over the calendar.
/// </summary>
public sealed partial class CalendarScreen
{
    private const double TodoWidth = 272;
    private const double TodoGap = 16;
    /// <summary>To Do beside the calendar from a page this wide: the calendar keeps 632 of it.</summary>
    private const double TodoBesideFrom = 920;
    /// <summary>To Do over the calendar, on a page too narrow for both side by side: this tall, its list scrolling within.</summary>
    private const double TodoAbove = 300;
    private const string TodoFormat = "simpl-work";
    private const string TodoPref = "calTodo";

    /// <summary>What the panel reads: To Do's own list with the panel's own (native-app.js todo's <c>panel</c>: what was
    /// dismissed this week among the completed, to restore).</summary>
    internal static object TodoArgs => new { panel = true };

    private readonly Loader<TodoData> todo = new();
    /// <summary>The grouping and Show completed as just chosen, shown at once while the list is read for them.</summary>
    private string? pendingGroup;
    private bool? pendingShowDone;
    private int regrouping;
    /// <summary>The panel, kept from build to build (the calendar is drawn again round it), and its list's scroller, so
    /// where the list is scrolled to is kept as it is drawn again.</summary>
    private readonly Border todoCard = K.Card(null, new Thickness(0), 12);
    private readonly ScrollViewer todoScroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private Button? todoButton;
    /// <summary>What came of the last plan made from To Do ("Planned Wed 4:00 PM", or why not), said under the list a moment.</summary>
    private (string Text, bool Problem)? todoSaid;
    private int todoSaidTicket;
    /// <summary>Each row as drawn, for the rail its menu opens under it and the screenshot suite's pictures.</summary>
    private readonly Dictionary<string, FrameworkElement> todoRows = new();
    /// <summary>Where a row dragged over the week would go (or, let go, is going): an hour's block in its day; and the day
    /// of the month or the list a row is held over.</summary>
    private Grid? dropMark, dropDay;
    private bool todoShotsAsked;
    /// <summary>The panel's ⋯ as drawn now.</summary>
    private Button? todoMore;

    private static bool TodoOn => Prefs.Bool(TodoPref, true);
    private bool TodoBeside => TodoOn && Width >= TodoBesideFrom;

    /// <summary>(0.3.64) To Do asked for (the pane's old To Do, /todo, the Dashboard's New task): shown beside the calendar.</summary>
    public static void ShowTodo() => Prefs.Set(TodoPref, true);

    /// <summary>The panel's list followed: each answer — a regrouping, a tick, a plan made, a live check — drawn in place,
    /// the calendar round it left as it is; To Do's count in the pane with it.</summary>
    private void WatchTodo()
    {
        todo.Changed += () =>
        {
            if (todo.Data is { Left: { } open }) engine.TodoOpen = open;
            if (!todo.LastSame) PaintTodo();
        };
        Checked(todo);
        WhenReading(() => todo.Loading);
        todoCard.Child = new Grid();
        AutomationProperties.SetName(todoCard, "To Do");
    }

    private void ToggleTodo()
    {
        Prefs.Set(TodoPref, !TodoOn);
        if (TodoOn) todo.Ensure(engine, "todo", TodoArgs);
        PaintCommands();
        RenderNow();
    }

    /// <summary>The panel, out of the page it was last in, drawn for what is read now.</summary>
    private Border TodoPanel()
    {
        BindingOperations.ClearBinding(todoCard, FrameworkElement.HeightProperty);
        if (todoCard.Child is Grid { Children.Count: 0 }) PaintTodo();
        return Adopt(todoCard);
    }

    // ---- the panel ----

    private void PaintTodo()
    {
        var d = todo.Data;
        todoRows.Clear();
        var dock = new DockPanel { LastChildFill = true };

        // the head: To Do, what is open, and ⋯ (the grouping, Show completed)
        var open = d == null ? 0 : d.Left ?? d.Sections.Sum(s => s.Rows.Count(r => !r.Done && r.Dismissed != true));
        var sub = K.Text(d == null ? "" : open > 0 ? $"{open} open · drag onto the calendar" : "Nothing left to do", Ty.Caption, B.Text2);
        sub.TextTrimming = TextTrimming.CharacterEllipsis;
        var more = K.Subtle(K.Icon(SymbolRegular.MoreHorizontal24, 16, B.Text), null, new Thickness(0), 4);
        more.Width = more.Height = 30;
        more.HorizontalContentAlignment = HorizontalAlignment.Center;
        more.ToolTip = "Group and show";
        more.Click += (_, e) => { e.Handled = true; TodoMenu(more); };
        AutomationProperties.SetName(more, "To Do options");
        more.IsEnabled = d != null;
        todoMore = more;
        var head = K.Columns("*, auto", K.V(1, K.Text("To Do", Ty.BodyStrong), sub).Col(0), more.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
        head.Margin = new Thickness(14, 10, 8, 9);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        var line = K.Divider();
        DockPanel.SetDock(line, Dock.Top);
        dock.Children.Add(line);

        // what came of a plan made from here, a moment, under the list
        if (todoSaid is { } said)
        {
            var words = K.Wrapped(said.Text, Ty.Caption, said.Problem ? B.Critical : B.Success);
            var foot = new Border
            {
                Child = K.Columns("auto, *", K.Icon(said.Problem ? SymbolRegular.Warning24 : SymbolRegular.CheckmarkCircle24, 14, said.Problem ? B.Critical : B.Success, filled: true).Margin(0, 0, 7, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), words.Col(1)),
                Padding = new Thickness(14, 8, 14, 9), BorderThickness = new Thickness(0, 1, 0, 0),
            };
            foot.SetResourceReference(Border.BorderBrushProperty, B.Divider);
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(foot);
            Motion.FadeIn(foot);
        }

        if (d == null)
            dock.Children.Add(todo.Error != null
                ? K.V(8, K.Wrapped("To Do could not be read.", Ty.Body, B.Text2), K.Link("Try again", () => todo.Reload(engine, "todo", TodoArgs))).Margin(14)
                : K.Spinner(18).Margin(0, 24, 0, 0).Align(HorizontalAlignment.Center, VerticalAlignment.Top));
        else
        {
            var list = new Stack { Orientation = Orientation.Vertical, Spacing = 0, Margin = new Thickness(0, 0, 0, 10) };
            list.Children.Add(TodoSummary(d));
            list.Children.Add(AddTaskRow());
            if (d.Sections.Count == 0)
            {
                var empty = K.Columns("auto, *", K.Icon(SymbolRegular.CheckmarkCircle24, 18, K.Tone("green"), filled: true).Margin(0, 0, 8, 0).Col(0),
                    K.Wrapped(d.ShowDone ? "Nothing this week." : "Nothing left to do this week.", Ty.Body, B.Text2).Col(1));
                list.Children.Add(empty.Margin(14, 14, 14, 6));
            }
            foreach (var s in d.Sections)
            {
                var late = s.Title == "Overdue";
                var name = K.Text(s.Title.ToUpper(CultureInfo.CurrentCulture), Ty.Caption, late ? B.Critical : B.Text2).Strong();
                var left = s.Rows.Count(r => !r.Done && r.Dismissed != true);
                var count = K.Text(left > 0 ? left.ToString(CultureInfo.InvariantCulture) : "", Ty.Caption, B.Text3);
                list.Children.Add(K.Columns("*, auto", name.Col(0), count.Col(1)).Margin(14, 12, 14, 4));
                foreach (var r in s.Rows) list.Children.Add(TodoRow(r, d.Group == "date" && !late));
            }
            todoScroller.Content = list;
            dock.Children.Add(Adopt(todoScroller));
        }
        todoCard.Child = dock;
        Shot.Moved();
        if (d != null) TodoShots();
    }

    /// <summary>The Mac's card over the list, small (the web's 2.99.43, compact): a ring of how much of the week is done,
    /// the most pressing word beside it — what is overdue in red, else what is due today, else what is left, else all done
    /// — and "n of m done" under it.</summary>
    private static FrameworkElement TodoSummary(TodoData d)
    {
        var rows = d.Sections.SelectMany(s => s.Rows).Where(r => !r.Done && r.Dismissed != true).ToList();
        var now = DateTime.Now;
        var overdue = d.Overdue ?? rows.Count(r => TodoWords.Date(r.Date) is { } at && at < now && r.Custom != true);
        var today = rows.Count(r => TodoWords.Date(r.Date) is { } at && at.Date == DateTime.Today && at >= now);
        var left = d.Left ?? rows.Count;
        string words;
        Color? tone = null;
        if (overdue > 0) { words = $"{overdue} overdue"; tone = K.Darkened(K.Tone("red")); }
        else if (today > 0) words = $"{today} due today";
        else if (left > 0) words = $"{left} to do";
        else { words = "All done"; tone = K.Darkened(K.Tone("green")); }
        var big = K.Text(words, Ty.BodyStrong);
        if (tone is { } c) big.Foreground = K.Brush(c);
        var done = K.Text(TodoWords.Text(d.DoneText) ?? $"{d.Done} of {d.Total} done", Ty.Caption, B.Text2);
        TodoWords.Tabular(big);
        TodoWords.Tabular(done);
        var ring = K.Ring(d.Total > 0 ? d.Pct / 100.0 : 0, K.Tone("green"), 30, 4, tinted: true);
        var row = K.Columns("auto, *", ring.Col(0), K.V(0, big, done).Margin(10, 0, 0, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1));
        var card = new Border { Child = row, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(8), Margin = new Thickness(8, 8, 8, 4) }.Bg(B.Subtle);
        AutomationProperties.SetName(card, $"{words}, {done.Text}");
        return card;
    }

    /// <summary>Add a task: New task's sheet (what, its day, a course, a repeat and until when, its priority), the list
    /// read again once it is added.</summary>
    private FrameworkElement AddTaskRow()
    {
        var plus = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Child = K.Icon(SymbolRegular.Add24, 13, B.AccentText) };
        plus.Background = K.Brush(K.WithAlpha(Appearance.Accent, Appearance.Dark ? 0.26 : 0.14));
        var b = K.Subtle(K.H(10, plus, K.Text("Add a task", Ty.Body, B.AccentText).Align(HorizontalAlignment.Left, VerticalAlignment.Center)), () => engine.AskNewTask(), new Thickness(10, 7, 10, 7), 6);
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        b.Margin = new Thickness(4, 0, 4, 2);
        b.ToolTip = "A task of your own (Ctrl+N)";
        AutomationProperties.SetName(b, "Add a task");
        return b;
    }

    /// <summary>
    /// A row of To Do beside the calendar: its tick, its title and what it is — its course (a task of your own's, or "My
    /// task"), a repeat's, and when it is due ("Was due …" once past) — the time it is planned for, a small flag in its
    /// priority's colour, and ⋯ (shown under the pointer, or with the keyboard on the row) for its own menu, as a
    /// right-click has it. A click opens its quick look; P, the free times to plan it in; dragged, it is carried onto the
    /// calendar by Windows' own drag (only what is still to do).
    /// </summary>
    private FrameworkElement TodoRow(WorkRow r, bool dayGroup)
    {
        var dismissed = r.Dismissed == true;
        var done = r.Done || dismissed;
        var due = TodoWords.Date(r.Date);
        var late = !done && due is { } dd && dd < DateTime.Now && r.Custom != true;
        var check = new CheckCircle(r.Done, r.Done ? "Mark not done" : "Mark done", 18);
        check.Toggled += on => _ = Tick(r, on);
        check.VerticalAlignment = VerticalAlignment.Top;
        check.Margin = new Thickness(0, 1, 9, 0);
        check.IsEnabled = !dismissed;
        var title = K.Wrapped(r.Title, Ty.Body, done ? B.Text2 : B.Text, 2);
        title.FontWeight = FontWeights.Medium;
        if (r.Done) title.Struck();
        // (its day too, where its group is not one day: the Overdue group, another grouping)
        var when = dayGroup ? TodoWords.Text(r.Time) : TodoWords.DayAndTime(r);
        var meta = string.Join(" · ", new[]
        {
            TodoWords.Text(r.CourseName),
            TodoWords.Text(r.Meta) is { } m && m.Contains("repeats", StringComparison.Ordinal) ? m.Split(" · ").FirstOrDefault(x => x.StartsWith("repeats", StringComparison.Ordinal)) : null,
            dismissed ? "Dismissed" : when == null ? null : late ? $"Was due {when}" : r.Custom == true ? when : $"Due {when}",
        }.Where(x => !string.IsNullOrEmpty(x)));
        var metaText = K.Text(meta, Ty.Caption, late ? B.Critical : B.Text2);
        metaText.TextTrimming = TextTrimming.CharacterEllipsis;
        var dot = new Ellipse { Width = 6, Height = 6, Fill = K.Brush(K.Hex(r.Color)), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        var words = K.V(2, title, K.Columns("auto, *", dot.Col(0), metaText.Col(1)));
        if (r.Plan is { } plan && !done) words.Children.Add(PlanRail.Pill(plan).Margin(0, 3, 0, 1));

        var level = r.Pri ?? 0;
        var flag = level > 0 ? K.Icon(SymbolRegular.Flag24, 13, TodoPriority.Of(level).Color, filled: true) : null;
        if (flag != null)
        {
            flag.VerticalAlignment = VerticalAlignment.Top;
            flag.Margin = new Thickness(4, 3, 0, 0);
            flag.ToolTip = $"{TodoPriority.Of(level).Label} priority";
        }
        var menu = new ContextMenu();
        var dots = K.Subtle(K.Icon(SymbolRegular.MoreHorizontal24, 14, B.Text), null, new Thickness(0), 4);
        dots.Width = dots.Height = 24;
        dots.HorizontalContentAlignment = HorizontalAlignment.Center;
        dots.ToolTip = "More";
        AutomationProperties.SetName(dots, $"More for {r.Title}");
        dots.VerticalAlignment = VerticalAlignment.Top;
        dots.Margin = new Thickness(2, -1, -4, 0);
        dots.Opacity = 0;
        var row = K.Columns("auto, *, auto, auto", check.Col(0), words.Col(1), flag?.Col(2), dots.Col(3));
        var b = K.Subtle(row, null, new Thickness(10, 7, 8, 7), 6);
        b.Click += (_, e) => { e.Handled = true; WorkPreview.Press(WorkPreview.Work(r, engine, on => _ = Tick(r, on)), b); };
        dots.Click += (_, e) =>
        {
            e.Handled = true;
            RowMenu(menu, r, b);
            menu.PlacementTarget = dots;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.Margin = new Thickness(4, 0, 4, 0);
        if (done) b.Opacity = dismissed ? 0.6 : 0.85;
        AutomationProperties.SetName(b, $"{r.Title}{(meta.Length > 0 ? ", " + meta : "")}{(level > 0 ? $", {TodoPriority.Of(level).Label} priority" : "")}");
        AutomationProperties.SetHelpText(b, done ? "Shows a quick look" : "Shows a quick look; drag it onto the calendar to plan it");
        if (!done) b.ToolTip = "Drag onto a day or a time to plan it";
        b.ContextMenu = menu;
        b.ContextMenuOpening += (_, _) =>
        {
            // (a right-click: at the pointer, wherever its ⋯ last opened it)
            menu.ClearValue(ContextMenu.PlacementTargetProperty);
            menu.ClearValue(ContextMenu.PlacementProperty);
            RowMenu(menu, r, b);
        };
        // (its ⋯ under the pointer, or with the keyboard on the row, or while its menu is open)
        void Dots() => dots.Opacity = b.IsMouseOver || b.IsKeyboardFocusWithin || menu.IsOpen ? 1 : 0;
        b.MouseEnter += (_, _) => Dots();
        b.MouseLeave += (_, _) => Dots();
        b.IsKeyboardFocusWithinChanged += (_, _) => Dots();
        menu.Closed += (_, _) => Dots();
        b.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.P || Keyboard.Modifiers != ModifierKeys.None || done) return;
            e.Handled = true;
            PlanRail.Open(b, r.Id, r.Title, engine);
        };
        todoRows[r.Id] = b;
        if (!done) Carried(b, r, check, dots);
        return b;
    }

    /// <summary>A row carried onto the calendar: past a few pixels the row is lifted (dimmed where it was) and carried by
    /// Windows' own drag — never from its tick or its ⋯.</summary>
    private void Carried(Button b, WorkRow r, FrameworkElement check, FrameworkElement dots)
    {
        Point? from = null;
        b.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var on = e.OriginalSource as DependencyObject;
            from = on != null && (Drag.Within(on, check) || Drag.Within(on, dots)) ? null : e.GetPosition(b);
        };
        b.PreviewMouseMove += (_, e) =>
        {
            if (from is not { } f || e.LeftButton != MouseButtonState.Pressed) return;
            var at = e.GetPosition(b);
            if (Math.Abs(at.X - f.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(at.Y - f.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            from = null;
            e.Handled = true;
            peek.Dismiss();
            b.Opacity = 0.45;
            try { DragDrop.DoDragDrop(b, new DataObject(TodoFormat, r.Id), DragDropEffects.Copy); }
            finally
            {
                b.Opacity = 1;
                // (let go off the calendar, or Escape: nothing planned, nothing left showing where it would have gone)
                if (dropMark?.Tag is not "placing") HideDrop();
                HideDayDrop();
            }
        };
        b.PreviewMouseLeftButtonUp += (_, _) => from = null;
    }

    /// <summary>The panel's ⋯: the grouping (by date, priority or course), then Show completed — with how many — or Hide.</summary>
    private void TodoMenu(FrameworkElement at, bool held = false)
    {
        if (todo.Data is not { } d) return;
        var group = pendingGroup ?? (string.IsNullOrEmpty(d.Group) ? "date" : d.Group);
        var showing = pendingShowDone ?? d.ShowDone;
        var menu = new ContextMenu { PlacementTarget = at, Placement = PlacementMode.Bottom, StaysOpen = held };
        foreach (var (key, words) in new[] { ("date", "By date"), ("priority", "By priority"), ("course", "By course") })
        {
            var mi = new MenuItem { Header = words, IsCheckable = true, IsChecked = group == key };
            mi.Click += (_, _) => Regroup(group: key);
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        var count = d.Done + (d.Dismissed ?? 0);
        var shown = new MenuItem
        {
            Header = showing ? "Hide completed" : count > 0 ? $"Show completed · {count}" : "Show completed",
            Icon = new SymbolIcon { Symbol = showing ? SymbolRegular.EyeOff24 : SymbolRegular.Eye24 },
            ToolTip = "Done and dismissed, this week",
        };
        shown.Click += (_, _) => Regroup(showDone: !showing);
        menu.Items.Add(shown);
        menu.IsOpen = true;
    }

    /// <summary>A row's own menu, filled as it opens from where the row stands then: the four priorities, Plan a time…,
    /// Submit (a plain assignment), and Delete task (a task of your own) or Dismiss — Restore, once dismissed.</summary>
    private void RowMenu(ContextMenu menu, WorkRow r, FrameworkElement row)
    {
        menu.Items.Clear();
        var dismissed = r.Dismissed == true;
        var level = r.Pri ?? 0;
        foreach (var p in TodoPriority.All)
        {
            // (checked as To Do's own priority menu was: a priority is yours alone, it never reaches the school)
            var mi = new MenuItem { Header = p.Id > 0 ? $"{p.Label} priority" : "No priority", IsCheckable = true, IsChecked = level == p.Id };
            var id = p.Id;
            mi.Click += (_, _) => _ = SetPriority(r, id);
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        if (!r.Done && !dismissed)
        {
            var plan = CalendarKit.Item(r.Plan != null ? "Change its plan…" : "Plan a time…", SymbolRegular.Clock24, () => PlanRail.Open(row, r.Id, r.Title, engine));
            plan.InputGestureText = "P";
            plan.ToolTip = "Or drag it onto the calendar";
            menu.Items.Add(plan);
        }
        if (r.Type == "assignment" && !r.Done && !dismissed && TodoWords.Text(r.Url) is { } url && System.Text.RegularExpressions.Regex.IsMatch(url, @"/assignments/\d+$"))
            menu.Items.Add(CalendarKit.Item("Submit", SymbolRegular.ArrowUpload24, () => engine.ActOn(url, r.Title, false)));
        if (menu.Items[^1] is not Separator) menu.Items.Add(new Separator());
        if (r.Custom == true) menu.Items.Add(CalendarKit.Item("Delete task…", SymbolRegular.Delete24, () => AskDelete(r)));
        else if (dismissed) menu.Items.Add(CalendarKit.Item("Restore", SymbolRegular.ArrowUndo24, () => _ = Dismiss(r, false)));
        else
        {
            var off = CalendarKit.Item("Dismiss", SymbolRegular.Dismiss24, () => _ = Dismiss(r, true));
            off.ToolTip = "Off To Do, not done";
            menu.Items.Add(off);
        }
    }

    // ---- what a row does ----

    /// <summary>Ticked off (or back on): shown at once, the school told, the list and the calendar read again; refused, put back.</summary>
    private async Task Tick(WorkRow r, bool on)
    {
        if (await engine.Act("complete", new { id = r.Id, done = on })) engine.DataChanged();
        else PaintTodo();
    }

    /// <summary>A priority set: shown at once, kept by the page (it never reaches the school), read again.</summary>
    private async Task SetPriority(WorkRow r, int level)
    {
        r.Pri = level;
        r.PriShort = TodoPriority.Of(level).Short;
        PaintTodo();
        await engine.Act("setPriority", new { id = r.Id, level });
        todo.Reload(engine, "todo", TodoArgs);
    }

    /// <summary>Canvas's work off To Do (dismissed, not done), or back on it.</summary>
    private async Task Dismiss(WorkRow r, bool off)
    {
        if (await engine.Act(off ? "todoDismiss" : "todoRestore", new { id = r.Id })) engine.DataChanged();
    }

    /// <summary>A task of your own on its way out: the question first — and, one of a series, which: this one, or every one
    /// still on the planner.</summary>
    private void AskDelete(WorkRow r)
    {
        var note = engine.OnBrightspace ? "This removes it from your To Do." : "This removes it from your Canvas planner.";
        var s = new Sheet { Title = $"Delete “{r.Title}”?", Body = K.Wrapped(note, Ty.Body, B.Text) };
        if (r.Series is int n && n > 0)
        {
            s.Add("Delete this task", SheetButtonKind.Primary, () => _ = Delete(r, false));
            s.Add($"Delete all {n + 1} repeats", SheetButtonKind.Secondary, () => _ = Delete(r, true));
        }
        else s.Add("Delete task", SheetButtonKind.Primary, () => _ = Delete(r, false));
        s.Add("Cancel", SheetButtonKind.Close);
        Dialogs.Show(s, Window.GetWindow(page));
    }

    private async Task Delete(WorkRow r, bool series)
    {
        if (await engine.Act("deleteTask", new { id = r.Id, series })) engine.DataChanged();
    }

    /// <summary>Grouped another way, or completed work shown or hidden — To Do's own choices, kept by the page: the menu
    /// says so at once, the list as its answer comes.</summary>
    private async void Regroup(string? group = null, bool? showDone = null)
    {
        if (group != null) pendingGroup = group;
        if (showDone != null) pendingShowDone = showDone;
        var ticket = ++regrouping;
        var args = new JsonObject { ["panel"] = true };
        if (group != null) args["group"] = group;
        if (showDone is { } sd) args["showDone"] = sd;
        try
        {
            var (d, text) = await engine.CallWithText<TodoData>("todo", args);
            if (ticket == regrouping) todo.Take(d, text);
        }
        catch (Exception e) { Log.Write("calendar: to do regroup " + e.Message); }
        if (ticket != regrouping) return; // (a later choice is on its way: it clears these)
        pendingGroup = null;
        pendingShowDone = null;
        PaintTodo();
    }

    /// <summary>What came of a plan made from To Do, said under its list for a few seconds.</summary>
    private async void Said(string text, bool problem = false)
    {
        todoSaid = (text, problem);
        var mine = ++todoSaidTicket;
        PaintTodo();
        await Task.Delay(4000);
        if (mine != todoSaidTicket) return;
        todoSaid = null;
        PaintTodo();
    }

    // ---- the week taking a row ----

    /// <summary>A day of the week taking a row of To Do dragged over it: where it would go follows the pointer a quarter of
    /// an hour at a time (the hour's block centred under it); let go, the work is planned there.</summary>
    private void TodoDrops(Grid cell, DateTime day)
    {
        cell.Background ??= Brushes.Transparent;
        cell.AllowDrop = true;
        int MinutesAt(DragEventArgs e)
        {
            var y = e.GetPosition(cell).Y - GridInset;
            var m = (int)Math.Round((y / hourPx * 60 - 30) / Snap) * Snap;
            return Math.Clamp(m, 0, DayEnd - 60);
        }
        void Over(object _, DragEventArgs e)
        {
            e.Handled = true;
            if (!e.Data.GetDataPresent(TodoFormat)) { e.Effects = DragDropEffects.None; return; }
            e.Effects = DragDropEffects.Copy;
            ShowDrop(cell, day, MinutesAt(e), placing: false);
        }
        cell.DragEnter += Over;
        cell.DragOver += Over;
        cell.DragLeave += (_, e) =>
        {
            // (moving onto a block in the day is still over the day)
            var p = e.GetPosition(cell);
            if (p.X >= 0 && p.Y >= 0 && p.X < cell.ActualWidth && p.Y < cell.ActualHeight) return;
            if (dropMark?.Parent == cell && dropMark.Tag is not "placing") HideDrop();
        };
        cell.Drop += (_, e) =>
        {
            if (e.Data.GetData(TodoFormat) is not string id) return;
            e.Handled = true;
            var m = MinutesAt(e);
            ShowDrop(cell, day, m, placing: true);
            _ = PlanFromTodo(id, day.AddMinutes(m));
        };
    }

    /// <summary>An hour's block in a day at <paramref name="minutes"/>, its times on it: dashed while it is only where a row
    /// would go, solid once let go.</summary>
    private void ShowDrop(Grid cell, DateTime day, int minutes, bool placing)
    {
        if (dropMark == null)
        {
            dropMark = new Grid { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
            dropMark.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = K.Brush(K.WithAlpha(Appearance.Accent, 0.16)) });
            dropMark.Children.Add(new Rectangle { RadiusX = 6, RadiusY = 6, StrokeThickness = 1.5, Stroke = K.Brush(Appearance.Accent) });
            var t = K.Text("", Ty.Caption, B.AccentText);
            t.Margin = new Thickness(7, 4, 4, 0);
            t.FontWeight = FontWeights.SemiBold;
            dropMark.Children.Add(t);
            Panel.SetZIndex(dropMark, 6);
        }
        if (dropMark.Parent is Panel old && old != cell) old.Children.Remove(dropMark);
        if (dropMark.Parent == null) cell.Children.Add(dropMark);
        dropMark.Tag = placing ? "placing" : null;
        dropMark.Height = Math.Max(20, hourPx - 2);
        dropMark.Margin = new Thickness(3, GridInset + WeekY(minutes), 3, 0);
        if (dropMark.Children[1] is Rectangle edge) edge.StrokeDashArray = placing ? null : new DoubleCollection { 4, 3 };
        if (dropMark.Children[2] is TextBlock words)
        {
            var at = day.AddMinutes(minutes);
            words.Text = $"{ClockWords(at)} – {ClockWords(at.AddHours(1))}\n{(placing ? "Planning it…" : "Plan it here")}";
        }
    }

    private void HideDrop()
    {
        if (dropMark?.Parent is Panel p) p.Children.Remove(dropMark);
        dropMark = null;
    }

    /// <summary>A row let go on the week: planned there for an hour; the block stays where it was let go until the week
    /// is read again, and what came of it is said under To Do.</summary>
    private async Task PlanFromTodo(string id, DateTime at)
    {
        var iso = DateTime.SpecifyKind(at, DateTimeKind.Local).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        if (await engine.Act("planPut", new { id, at = iso, mins = 60 }))
        {
            Log.Write($"calendar: planned from To Do {id} at {iso}");
            Said($"Planned {at.ToString("dddd", Culture)} {ClockWords(at)}");
            Read();
            engine.DataChanged();
        }
        else
        {
            Log.Write($"calendar: could not plan {id} from To Do");
            HideDrop();
        }
    }

    // ---- (0.3.64) a day of the month or the list taking a row ----

    /// <summary>A day of the month or the list taking a row of To Do dragged over it — ringed in the accent, "Plan here" in
    /// it (a day already over takes nothing); let go, the work is planned at that day's first free time (native-app.js
    /// planDay: from now on, today) and when is said under To Do.</summary>
    private void TodoDayDrops(Grid cell, DateTime day, double corner, double inset = 0)
    {
        cell.AllowDrop = true;
        cell.Background ??= Brushes.Transparent;
        void Over(object _, DragEventArgs e)
        {
            e.Handled = true;
            if (!e.Data.GetDataPresent(TodoFormat) || day < DateTime.Today) { e.Effects = DragDropEffects.None; return; }
            e.Effects = DragDropEffects.Copy;
            ShowDayDrop(cell, corner, inset);
        }
        cell.DragEnter += Over;
        cell.DragOver += Over;
        cell.DragLeave += (_, e) =>
        {
            var p = e.GetPosition(cell);
            if (p.X >= 0 && p.Y >= 0 && p.X < cell.ActualWidth && p.Y < cell.ActualHeight) return;
            if (dropDay?.Parent == cell) HideDayDrop();
        };
        cell.Drop += (_, e) =>
        {
            if (e.Data.GetData(TodoFormat) is not string id || day < DateTime.Today) return;
            e.Handled = true;
            HideDayDrop();
            _ = PlanOnDay(id, day);
        };
    }

    private void ShowDayDrop(Grid cell, double corner, double inset)
    {
        if (dropDay?.Parent == cell) return;
        HideDayDrop();
        var ring = new Border { CornerRadius = new CornerRadius(corner), BorderThickness = new Thickness(2), Margin = new Thickness(inset), Background = K.Brush(K.WithAlpha(Appearance.Accent, 0.12)), BorderBrush = K.Brush(Appearance.Accent) };
        var tag = K.Text("Plan here", Ty.Caption, B.OnAccent);
        tag.FontWeight = FontWeights.SemiBold;
        var pill = new Border { Child = tag, Padding = new Thickness(8, 1, 8, 2), CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(inset + 6) }.Bg(B.Accent);
        dropDay = new Grid { IsHitTestVisible = false, Children = { ring, pill } };
        Panel.SetZIndex(dropDay, 6);
        cell.Children.Add(dropDay);
    }

    private void HideDayDrop()
    {
        if (dropDay?.Parent is Panel p) p.Children.Remove(dropDay);
        dropDay = null;
    }

    /// <summary>A row let go on a day: planned at that day's first free time, when said under To Do (or why not).</summary>
    private async Task PlanOnDay(string id, DateTime day)
    {
        try
        {
            var r = await engine.Call<PlanDay>("planDay", new { id, day = Iso(day) });
            Log.Write($"calendar: planned from To Do {id} on {day:yyyy-MM-dd}, at {r.At}");
            Said($"Planned {r.Words ?? CalendarKit.DateWords(day, weekday: true)}");
            Read();
            engine.DataChanged();
        }
        catch (Exception e)
        {
            Log.Write($"calendar: could not plan {id} on {day:yyyy-MM-dd}: {e.Message}");
            Said($"Could not plan it: {e.Message}", problem: true);
        }
    }

    // ---- the screenshot suite ----

    /// <summary>The screenshot suite's row of To Do held over the week (--open cal-todo-drop): over its Wednesday at 1 PM,
    /// where it would go.</summary>
    private void ShotTodoDrop(List<Grid> columns, List<DateTime> days)
    {
        var at = days.FindIndex(d => d.DayOfWeek == DayOfWeek.Wednesday);
        if (at < 0 || Launch.Take("cal-todo-drop") == null) return;
        page.Dispatcher.BeginInvoke(() => ShowDrop(columns[at], days[at], 13 * 60, placing: false), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>(0.3.64) The screenshot suite's pictures of To Do, once its list is drawn: its ⋯ menu (--open
    /// cal-todo-menu); and, To Do asked for (--place todo), a row's quick look (--open preview) or the free times under the
    /// first piece of work still to do that is not late (--open plan), held open as the suite's other copies of Simpl take
    /// the focus.</summary>
    private void TodoShots()
    {
        if (todoShotsAsked || !Shot.Asked) return;
        var open = Launch.Get("open");
        if (open != "cal-todo-menu" && !(TodoAskedAtLaunch && open is "preview" or "plan")) return;
        todoShotsAsked = true;
        _ = Later();

        async Task Later()
        {
            Shot.Began();
            try
            {
                // (once the panel is on screen with the page round it, and has settled)
                for (var i = 0; i < 40 && todoMore is not { IsLoaded: true }; i++) await Task.Delay(250);
                await Task.Delay(2000);
                if (hidden || todo.Data is not { } d) return;
                if (Launch.Take("cal-todo-menu") != null)
                {
                    if (todoMore is { IsLoaded: true } more)
                    {
                        TodoMenu(more, held: true);
                        Log.Write($"calendar: To Do's menu open, grouped by {(string.IsNullOrEmpty(d.Group) ? "date" : d.Group)}");
                    }
                    return;
                }
                var rows = d.Sections.Where(s => s.Title != "Overdue").SelectMany(s => s.Rows).Where(r => !r.Done && r.Dismissed != true).ToList();
                if (Launch.Take("preview") != null && rows.FirstOrDefault() is { } first && todoRows.TryGetValue(first.Id, out var at) && at.IsLoaded)
                    WorkPreview.Press(WorkPreview.Work(first, engine, on => _ = Tick(first, on)), at);
                else if (Launch.Take("plan") != null && rows.FirstOrDefault(r => r.Custom != true) is { } work && todoRows.TryGetValue(work.Id, out var row) && row.IsLoaded)
                {
                    row.BringIntoView();
                    await Task.Delay(400); // (scrolled to first: the rail hangs where the row is)
                    if (row.IsLoaded) PlanRail.Open(row, work.Id, work.Title, engine, held: true);
                }
            }
            finally { Shot.Ended(); }
        }
    }
}
