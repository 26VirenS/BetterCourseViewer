using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// A task of your own (a Canvas planner note; the Mac's NewTaskSheet), as a Windows 11 dialog over the window: what it
/// is, its day, a course and a repeat if wanted (and until when), and its priority. The Dashboard's New task (which opens
/// To Do &amp; Calendar under it), To Do's Add a task, Ctrl+N and the Calendar's New task on a day put it up; Enter adds
/// it, Escape puts it away.
/// </summary>
public sealed class NewTaskSheet
{
    /// <summary>The day the next sheet opens on (the Calendar's New task on a day), taken once; today when nothing is set.</summary>
    public static DateTime? StartDay { get; set; }

    private static readonly int[] Levels = { 3, 2, 1, 0 };
    /// <summary>The sheet up now (one at a time).</summary>
    private static NewTaskSheet? showing;

    private readonly Engine.Engine engine;
    private readonly Sheet sheet = new() { Bare = true, Width = 520, Floats = true, Name = "New task" };
    private readonly SheetButton add;
    private readonly TextBox title;
    private readonly DatePicker day, untilPicker;
    private readonly ComboBox course, repeat;
    private readonly FrameworkElement untilRow;
    private readonly ContentControl failure = new() { Focusable = false };
    private List<NamedColor> courses = new();
    private List<RepeatChoice> repeats = new();
    private DateTime date, until;
    private int priority = 2;
    private bool busy;

    public static void Show(Engine.Engine engine, Window over)
    {
        if (showing != null) return;
        var s = new NewTaskSheet(engine);
        showing = s;
        s.sheet.Closed += () => { if (showing == s) showing = null; };
        Simpl.Shell.Dialogs.Show(s.sheet, over);
        _ = s.LoadChoices();
    }

    private NewTaskSheet(Engine.Engine engine)
    {
        this.engine = engine;
        date = DateTime.Now;
        if (StartDay is { } start)
        {
            StartDay = null;
            date = start.Date + DateTime.Now.TimeOfDay;
        }
        until = date.AddDays(56);

        var head = K.H(10, K.IconTile(SymbolRegular.CheckmarkCircle24, K.Tone("purple"), 28), K.Text("New task", Ty.Subtitle));
        head.Margin = new Thickness(24, 22, 24, 10);

        // what it is, and its day
        title = new TextBox { PlaceholderText = "What do you need to do?", MaxLength = 250 };
        AutomationProperties.SetName(title, "Task");
        title.TextChanged += (_, _) => Ready();
        day = new DatePicker { SelectedDate = date.Date, SelectedDateFormat = DatePickerFormat.Short, Width = 170 };
        day.SelectedDateChanged += (_, _) => { if (day.SelectedDate is { } d) DayChanged(d); };
        var first = K.V(6, K.Group(title, Compact(K.Row(SymbolRegular.CalendarLtr24, "Day", null, day))),
            Note("It’s due by the end of that day."));

        // a course, and a repeat (with the day it ends)
        course = new ComboBox { MinWidth = 210 };
        repeat = new ComboBox { MinWidth = 210 };
        FillCourses();
        FillRepeats();
        repeat.SelectionChanged += (_, _) => RepeatChanged();
        untilPicker = new DatePicker { SelectedDate = until.Date, DisplayDateStart = date.Date, SelectedDateFormat = DatePickerFormat.Short, Width = 170 };
        untilPicker.SelectedDateChanged += (_, _) => { if (untilPicker.SelectedDate is { } u) until = u.Date + date.TimeOfDay; };
        untilRow = Compact(K.Row(SymbolRegular.CalendarArrowRight24, "Until", null, untilPicker));
        untilRow.Visibility = Visibility.Collapsed;
        var second = K.Group(
            Compact(K.Row(SymbolRegular.BookOpen24, "Course", null, course)),
            Compact(K.Row(SymbolRegular.ArrowRepeatAll24, "Repeat", null, repeat)),
            untilRow);

        // its priority (Windows 11's SelectorBar for the Mac's segmented picker)
        var levels = new SelectorBar(new[] { "High", "Medium", "Low", "None" }, Array.IndexOf(Levels, priority));
        levels.Changed += i => priority = Levels[i];
        AutomationProperties.SetName(levels, "Priority");
        var third = K.V(6, Compact(K.Row(SymbolRegular.Flag24, "Priority", null, levels)),
            Note($"Priority is yours alone and never reaches {engine.LmsName}."));

        var form = K.V(18, first, second, third, failure);
        form.Margin = new Thickness(24, 4, 24, 20);
        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var body = K.Rows("auto, *", head.Row(0), scroll.Row(1));
        body.Loaded += (_, _) => Ready();
        sheet.Body = body;
        add = sheet.Add("Add", SheetButtonKind.Primary, Add, closes: false);
        sheet.Add("Cancel", SheetButtonKind.Close);
    }

    /// <summary>A settings row a little lower than the Settings app's own, for a dialog.</summary>
    private static FrameworkElement Compact(FrameworkElement row)
    {
        row.MinHeight = 52;
        return row;
    }

    private static FrameworkElement Note(string text) => K.Wrapped(text, Ty.Caption, B.Text2).Margin(2, 0, 2, 0);

    /// <summary>Add is there once the task has words, and while nothing is on its way.</summary>
    private void Ready() => add.Enabled = !busy && title.Text.Trim().Length > 0;

    private void DayChanged(DateTime d)
    {
        date = d.Date + date.TimeOfDay;
        // (a repeat ends after it starts)
        if (until < date)
        {
            until = date.AddDays(56);
            untilPicker.SelectedDate = until.Date;
        }
        untilPicker.DisplayDateStart = date.Date;
    }

    private string RepeatKey => (repeat.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    private string CourseId => (course.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>The Until row comes in with a repeat, and goes with it.</summary>
    private void RepeatChanged()
    {
        var on = RepeatKey.Length > 0;
        if (on == (untilRow.Visibility == Visibility.Visible)) return;
        untilRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) Motion.Enter(untilRow, 0, -8, Motion.Medium);
        Shot.Moved();
    }

    private void FillCourses()
    {
        var keep = CourseId;
        course.Items.Clear();
        course.Items.Add(new ComboBoxItem { Content = "No course", Tag = "" });
        foreach (var c in courses) course.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Id });
        course.SelectedItem = course.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == keep) ?? course.Items[0];
    }

    private void FillRepeats()
    {
        var keep = RepeatKey;
        repeat.Items.Clear();
        repeat.Items.Add(new ComboBoxItem { Content = "Doesn’t repeat", Tag = "" });
        foreach (var r in repeats) repeat.Items.Add(new ComboBoxItem { Content = r.Label, Tag = r.Key });
        repeat.SelectedItem = repeat.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == keep) ?? repeat.Items[0];
    }

    /// <summary>The courses and the repeats to choose from, as To Do has them (what was kept from last time at once).</summary>
    private async Task LoadChoices()
    {
        if (engine.Kept<TodoData>("todo") is { } kept) Choices(kept);
        try { Choices(await engine.Call<TodoData>("todo")); }
        catch (Exception e) { Log.Write("new task choices: " + e.Message); }
    }

    private void Choices(TodoData d)
    {
        courses = d.Courses ?? new();
        repeats = d.Repeats ?? new();
        FillCourses();
        FillRepeats();
    }

    private async void Add()
    {
        var t = title.Text.Trim();
        if (t.Length == 0 || busy) return;
        // (Enter that picks a day or a choice in an open list is the list's, not Add's)
        if (day.IsDropDownOpen || untilPicker.IsDropDownOpen || course.IsDropDownOpen || repeat.IsDropDownOpen) return;
        busy = true;
        failure.Content = null;
        add.Relabel("Adding…");
        Ready();
        var args = new JsonObject { ["title"] = t, ["date"] = TodoWords.Iso(date), ["priority"] = priority };
        if (CourseId is { Length: > 0 } id) args["courseId"] = id;
        if (RepeatKey is { Length: > 0 } key)
        {
            args["repeat"] = key;
            args["until"] = TodoWords.Iso(until);
        }
        try
        {
            await engine.Call<OK>("addTask", args);
            sheet.Close();
            engine.DataChanged();
        }
        catch (Exception e)
        {
            failure.Content = new InfoBar { Title = "Couldn’t add the task", Message = e.Message, Severity = InfoBarSeverity.Error, IsOpen = true, IsClosable = false };
            busy = false;
            add.Relabel("Add");
            Ready();
        }
    }
}
