using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// (0.3.63) To Do beside the week (the web's 2.99.110 and the Mac's 1.3.169): what is open over the next seven days, by
/// day, Overdue first (native-app.js <c>todo</c>, side: To Do's own settings untouched), each row ticked off where it is
/// and its quick look opened with a click. A row dragged onto a day of the week shows where it would go — an hour's
/// block, a quarter of an hour at a time under the pointer, its times on it — and let go, the work is planned there (the
/// page's planPut), "Planned …" said under To Do. The commands' To Do button hides or shows it (kept); a page too narrow
/// for both shows the week alone.
/// </summary>
public sealed partial class CalendarScreen
{
    private const double TodoWidth = 260;
    /// <summary>To Do beside the week from a page this wide: the week's grid keeps 640 of it.</summary>
    private const double TodoBesideFrom = 920;
    private const string TodoFormat = "simpl-work";
    private const string TodoPref = "calTodo";

    private TodoData? todoSide;
    private bool todoFailed;
    /// <summary>The calendar's answer To Do was last read for: a new one (a plan made, a day ticked off) reads it again.</summary>
    private CalendarData? todoFor;
    private Border? todoHost;
    private Button? todoButton;
    private string? todoSaid;
    private int todoSaidTicket;
    /// <summary>Where a row dragged over the week would go (or, let go, is going): an hour's block in its day.</summary>
    private Grid? dropMark;

    private static bool TodoOn => Prefs.Bool(TodoPref, true);
    private bool TodoBeside => mode == "week" && TodoOn && Width >= TodoBesideFrom;

    private void ToggleTodo()
    {
        Prefs.Set(TodoPref, !TodoOn);
        PaintCommands();
        RenderNow();
    }

    /// <summary>The week with To Do beside it, as tall as the week (its list scrolling within).</summary>
    private FrameworkElement WithTodo(FrameworkElement week)
    {
        if (!TodoBeside) return week;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TodoWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        todoHost = new Border { VerticalAlignment = VerticalAlignment.Top };
        todoHost.SetBinding(FrameworkElement.HeightProperty, new Binding(nameof(FrameworkElement.ActualHeight)) { Source = week });
        PaintTodo();
        g.Children.Add(todoHost.Col(0));
        g.Children.Add(week.Col(2));
        if (cal.Data != null && !ReferenceEquals(cal.Data, todoFor))
        {
            todoFor = cal.Data;
            _ = LoadTodo();
        }
        return g;
    }

    private async Task LoadTodo()
    {
        try
        {
            todoSide = await engine.Call<TodoData>("todo", new { side = true });
            todoFailed = false;
        }
        catch (Exception e)
        {
            Log.Write("calendar: to do beside the week " + e.Message);
            todoFailed = todoSide == null;
        }
        PaintTodo();
    }

    private void PaintTodo()
    {
        if (todoHost == null) return;
        var sections = todoSide?.Sections ?? new List<TodoSection>();
        var open = sections.Sum(s => s.Rows.Count);
        var dock = new DockPanel { LastChildFill = true };

        var title = K.Columns("*, auto",
            K.Text("To Do", Ty.BodyStrong).Col(0),
            K.Text(todoSide == null ? "" : $"{open} open", Ty.Caption, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
        var head = K.V(2, title, K.Text("Drag a row onto a time to plan it.", Ty.Caption, B.Text2));
        head.Margin = new Thickness(14, 12, 14, 10);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        var line = K.Divider();
        DockPanel.SetDock(line, Dock.Top);
        dock.Children.Add(line);

        if (todoSaid != null)
        {
            var said = K.Columns("auto, *",
                K.Icon(SymbolRegular.CheckmarkCircle24, 14, B.Success, filled: true).Margin(0, 0, 7, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0),
                K.Text(todoSaid, Ty.Caption, B.Success).Col(1));
            var foot = new Border { Child = said, Padding = new Thickness(14, 8, 14, 9), BorderThickness = new Thickness(0, 1, 0, 0) };
            foot.SetResourceReference(Border.BorderBrushProperty, B.Divider);
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(foot);
            Motion.FadeIn(foot);
        }

        if (todoSide == null)
            dock.Children.Add(todoFailed
                ? K.Wrapped("To Do could not be read.", Ty.Body, B.Text2).Margin(14)
                : K.Spinner(18).Margin(0, 24, 0, 0).Align(HorizontalAlignment.Center, VerticalAlignment.Top));
        else if (open == 0)
            dock.Children.Add(K.Wrapped(todoSide.Empty ?? "Nothing to do in the next seven days.", Ty.Body, B.Text2).Margin(14));
        else
        {
            var list = new Stack { Orientation = Orientation.Vertical, Spacing = 0, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var s in sections)
            {
                var name = K.Text(s.Title.ToUpper(CultureInfo.CurrentCulture), Ty.Caption, s.Title == "Overdue" ? B.Critical : B.Text2).Strong();
                var count = K.Text(s.Rows.Count.ToString(CultureInfo.InvariantCulture), Ty.Caption, B.Text3);
                list.Children.Add(K.Columns("*, auto", name.Col(0), count.Col(1)).Margin(14, 10, 14, 4));
                foreach (var r in s.Rows) list.Children.Add(TodoRow(r));
            }
            dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        }
        todoHost.Child = K.Card(dock, new Thickness(0), 12);
        AutomationProperties.SetName(todoHost, "To Do");
    }

    private FrameworkElement TodoRow(WorkRow r)
    {
        var tick = K.IconButton(r.Done ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.Circle24, () => _ = Tick(r), r.Done ? "Mark as not done" : "Mark as done", 16, 24);
        tick.VerticalAlignment = VerticalAlignment.Top;
        var name = K.Wrapped(r.Title, Ty.Body, B.Text, 2);
        name.FontWeight = FontWeights.Medium;
        var dot = new Ellipse { Width = 6, Height = 6, Fill = K.Brush(K.Hex(r.Color)), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        var meta = K.Text(string.Join(" · ", new[] { r.CourseName, r.Time }.Where(x => !string.IsNullOrEmpty(x))), Ty.Caption, B.Text2);
        meta.TextTrimming = TextTrimming.CharacterEllipsis;
        var words = K.V(2, name, K.Columns("auto, *", dot.Col(0), meta.Col(1)));
        if (r.Plan is { } p)
            words.Children.Add(K.H(4, K.Icon(SymbolRegular.Clock24, 11, p.Past == true ? B.Text2 : B.AccentText), K.Text(p.Words, Ty.Caption, p.Past == true ? B.Text2 : B.AccentText)));
        var grip = K.Icon(SymbolRegular.ReOrderDotsVertical24, 14, B.Text3);
        grip.VerticalAlignment = VerticalAlignment.Top;
        grip.Margin = new Thickness(4, 3, 0, 0);
        var row = K.Columns("auto, *, auto", tick.Margin(0, 0, 8, 0).Col(0), words.Col(1), grip.Col(2));
        Button? b = null;
        b = K.Subtle(row, () => WorkPreview.Press(WorkPreview.Work(r, engine, on => _ = Tick(r, on)), b!), new Thickness(10, 7, 10, 7), 6);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.Margin = new Thickness(4, 0, 4, 0);
        b.ToolTip = "Drag onto a time on the week to plan it";
        AutomationProperties.SetName(b, r.Title);
        AutomationProperties.SetHelpText(b, "Shows a preview; drag it onto a time on the week to plan it");

        // the drag: past a few pixels, the row is lifted (dimmed where it was) and carried by Windows' own drag
        Point? from = null;
        b.PreviewMouseLeftButtonDown += (_, e) => from = e.GetPosition(b);
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
                // (let go off the week, or Escape: nothing planned, nothing left showing where it would have gone)
                if (dropMark?.Tag is not "placing") HideDrop();
            }
        };
        b.PreviewMouseLeftButtonUp += (_, _) => from = null;
        return b;
    }

    private async Task Tick(WorkRow r, bool? on = null)
    {
        if (await engine.Act("complete", new { id = r.Id, done = on ?? !r.Done }))
        {
            await LoadTodo();
            engine.DataChanged();
        }
    }

    // ---- the week taking a row ----

    /// <summary>A day of the week taking a row of To Do dragged over it: where it would go follows the pointer a quarter of
    /// an hour at a time (the hour's block centred under it); let go, the work is planned there.</summary>
    private void TodoDrops(Grid cell, DateTime day, int first)
    {
        cell.Background ??= Brushes.Transparent;
        cell.AllowDrop = true;
        int MinutesAt(DragEventArgs e)
        {
            var y = e.GetPosition(cell).Y - GridInset;
            var m = (int)Math.Round((first * 60 + y / HourPx * 60 - 30) / Snap) * Snap;
            return Math.Clamp(m, first * 60, DayEnd - 60);
        }
        void Over(object _, DragEventArgs e)
        {
            e.Handled = true;
            if (!TodoBeside || !e.Data.GetDataPresent(TodoFormat)) { e.Effects = DragDropEffects.None; return; }
            e.Effects = DragDropEffects.Copy;
            ShowDrop(cell, day, MinutesAt(e), first, placing: false);
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
            if (!TodoBeside || e.Data.GetData(TodoFormat) is not string id) return;
            e.Handled = true;
            var m = MinutesAt(e);
            ShowDrop(cell, day, m, first, placing: true);
            _ = PlanFromTodo(id, day.AddMinutes(m));
        };
    }

    /// <summary>An hour's block in a day at <paramref name="minutes"/>, its times on it: dashed while it is only where a row
    /// would go, solid once let go.</summary>
    private void ShowDrop(Grid cell, DateTime day, int minutes, int first, bool placing)
    {
        if (dropMark == null)
        {
            dropMark = new Grid { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top, Height = HourPx - 2 };
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
        dropMark.Margin = new Thickness(3, GridInset + WeekY(minutes, first), 3, 0);
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
            todoSaid = $"Planned {at.ToString("dddd", Culture)} {ClockWords(at)}";
            var mine = ++todoSaidTicket;
            PaintTodo();
            Read();
            engine.DataChanged();
            await Task.Delay(4000);
            if (mine != todoSaidTicket) return;
            todoSaid = null;
            PaintTodo();
        }
        else
        {
            Log.Write($"calendar: could not plan {id} from To Do");
            HideDrop();
        }
    }

    /// <summary>The screenshot suite's row of To Do held over the week (--open cal-todo-drop): over its fourth day at 1 PM,
    /// where it would go.</summary>
    private void ShotTodoDrop(List<Grid> columns, List<DateTime> days, int first)
    {
        if (columns.Count < 4 || Launch.Take("cal-todo-drop") == null) return;
        page.Dispatcher.BeginInvoke(() => ShowDrop(columns[3], days[3], 13 * 60, first, placing: false), System.Windows.Threading.DispatcherPriority.Background);
    }
}
