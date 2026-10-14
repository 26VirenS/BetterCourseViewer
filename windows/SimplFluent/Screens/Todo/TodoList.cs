using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Simpl.Screens;

public sealed partial class TodoScreen
{
    /// <summary>The row ids drawn last time, and how the list was grouped: what is new arrives, a regrouping enters anew.</summary>
    private readonly HashSet<string> known = new();
    private string? lastShape;
    private bool listDrawn;

    // ---- the list: a card per group, the way through underneath ----

    private FrameworkElement List(TodoData d)
    {
        var group = string.IsNullOrEmpty(d.Group) ? "date" : d.Group;
        var shape = $"{group}|{d.ShowDone}";
        var regrouped = lastShape != null && shape != lastShape;
        lastShape = shape;
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 22 };
        var cards = new List<UIElement>();
        var units = new List<(string Id, UIElement Row)>();
        if (d.Sections.Count == 0)
        {
            var empty = K.Card(K.Empty(SymbolRegular.CheckmarkCircle24, d.Empty ?? "Nothing to do."), new Thickness(18, 2, 18, 2));
            firstCard = empty;
            cards.Add(empty);
            v.Children.Add(empty);
        }
        foreach (var s in d.Sections)
        {
            var card = SectionCard(s, group, units);
            cards.Add(card);
            v.Children.Add(card);
        }
        v.Children.Add(K.Wrapped($"Click a task for a quick look, double-click it to open it, or right-click it for more. Priority is yours alone and never reaches {engine.LmsName}.", Ty.Caption, B.Text2).Margin(2, 0, 2, 0));
        // grouped anew, the cards come in one after another; a row that was not there before (a new task) arrives
        if (regrouped) Motion.Stagger(cards, 40, 16);
        else if (listDrawn)
            foreach (var (id, row) in units)
                if (!known.Contains(id)) Motion.Enter(row, 0, 12, Motion.Slow);
        listDrawn = true;
        known.Clear();
        foreach (var (id, _) in units) known.Add(id);
        return v;
    }

    /// <summary>A group's heading and its count, and one card of its rows.</summary>
    private FrameworkElement SectionCard(TodoSection section, string group, List<(string, UIElement)> units)
    {
        var byCourse = group == "course";
        // (a card that is not one day — another grouping, or Overdue — gives each row its day as well as its time)
        var withDay = group != "date" || section.Title == "Overdue";
        // (the flag's slot as wide as a priority's pill only where a row of the card shows one; else the faint flag's own
        // width, the rest given to the titles)
        var pills = section.Rows.Any(r => (r.Pri ?? 0) > 0);
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        for (var i = 0; i < section.Rows.Count; i++)
        {
            if (i > 0)
            {
                var div = K.Divider();
                div.Margin = new Thickness(44, 0, 8, 0);
                rows.Children.Add(div);
            }
            var unit = TaskRow(section.Rows[i], !byCourse, withDay, pills ? 74 : 38);
            units.Add((section.Rows[i].Id, unit));
            rows.Children.Add(unit);
        }
        var card = K.Card(rows, new Thickness(4));
        firstCard ??= card;
        return K.V(0, Heading.Section(section.Title, section.Note), card);
    }

    // ---- a row ----

    /// <summary>What a row shows under its title, its course and its time (the Mac's TodoView.shown): what it is under its
    /// title (its course is its chip, or the card it is in), and its day with its time where the card is not one day.</summary>
    private static (string? Sub, string? Course, string? Time) Shown(WorkRow row, bool withCourse, bool withDay)
    {
        var sub = TodoWords.Text(row.Meta) ?? TodoWords.Text(row.Sub);
        var course = withCourse && TodoWords.Text(row.CourseName) is { } name ? name : TodoWords.Text(row.Course);
        var time = withDay ? TodoWords.DayAndTime(row) : TodoWords.Text(row.Time);
        return (sub, course, time);
    }

    /// <summary>
    /// A row of To Do (the Mac's TodoTaskRow over WorkRowView): its tick, its title and what it is, where it stands, its
    /// course, its time — and its priority's flag at its end, in a slot <paramref name="slot"/> wide. A click opens its
    /// quick look under it (a double-click opens the work); a right-click opens it, hands it in or shows its feedback,
    /// marks it done, sets its priority, deletes a task of your own, or opens it in Canvas.
    /// </summary>
    private FrameworkElement TaskRow(WorkRow row, bool showCourse, bool withDay, double slot)
    {
        var (sub, course, time) = Shown(row, showCourse, withDay);
        var check = new CheckCircle(row.Done, row.Done ? "Mark not done" : "Mark done");
        check.Toggled += on => Toggle(row, on);
        var title = K.Wrapped(row.Title, Ty.Body, row.Done ? B.Text2 : B.Text, 2);
        if (row.Done) title.Struck();
        var words = K.V(1, title);
        if (sub != null) words.Children.Add(K.Text(sub, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var line = K.Columns("auto, *, auto, auto, auto", check.Margin(4, 0, 12, 0).Col(0), words.Col(1));
        if (row.Flag is { } f && f.Word.Length > 0) line.Children.Add(K.Chip(f.Word, TodoWords.Tone(f.Kind), true).Margin(8, 0, 0, 0).Col(2));
        if (showCourse && course != null) line.Children.Add(K.Chip(course, K.Hex(row.Color), true).Margin(8, 0, 0, 0).Col(3));
        if (time != null)
        {
            var t = K.Text(time, Ty.Body, B.Text2);
            t.TextAlignment = TextAlignment.Right;
            t.MinWidth = 58;
            TodoWords.Tabular(t);
            line.Children.Add(t.Margin(10, 0, 2, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(4));
        }
        var press = K.Subtle(line, () => Press(row), new Thickness(4, 9, 4, 9), 6);
        AutomationProperties.SetName(press, row.Title);
        AutomationProperties.SetHelpText(press, "Shows a quick look; a double-click opens it");

        var (flag, faint) = PriorityFlag(row);
        var unit = new Grid();
        unit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        unit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(slot) });
        unit.Children.Add(press.Col(0));
        unit.Children.Add(flag.Col(1));
        unit.Background = Brushes.Transparent;
        if (faint != null)
        {
            // (a row with no priority keeps a faint flag, so it can be found; plain under the pointer, or while it has the
            // keyboard)
            unit.MouseEnter += (_, _) => Motion.To(faint, UIElement.OpacityProperty, 1, Motion.Fast);
            unit.MouseLeave += (_, _) => { if (!flag.IsKeyboardFocused) Motion.To(faint, UIElement.OpacityProperty, FaintFlag, Motion.Fast); };
            flag.GotKeyboardFocus += (_, _) => Motion.To(faint, UIElement.OpacityProperty, 1, Motion.Fast);
            flag.LostKeyboardFocus += (_, _) => { if (!unit.IsMouseOver) Motion.To(faint, UIElement.OpacityProperty, FaintFlag, Motion.Fast); };
        }
        // (its menu is filled as it opens, from where the row stands then)
        var menu = new ContextMenu();
        unit.ContextMenu = menu;
        unit.ContextMenuOpening += (_, _) => FillMenu(menu, row);

        rowButtons[row.Id] = press;
        return unit;
    }

    /// <summary>How faint a row's flag is with no priority set, away from the pointer.</summary>
    private const double FaintFlag = 0.35;

    /// <summary>A row's priority as a flag at its end, a menu of the four levels under it (the Mac's TodoPriorityMenu): the
    /// level's colour and word when it has one; a faint flag when not — always there to be found, plain under the
    /// pointer.</summary>
    private (Button Flag, UIElement? Faint) PriorityFlag(WorkRow row)
    {
        var level = row.Pri ?? 0;
        var p = TodoPriority.Of(level);
        var word = string.IsNullOrEmpty(row.PriShort) ? p.Short : row.PriShort!;
        UIElement content;
        UIElement? faint = null;
        if (level > 0) content = Pill(word, p.Color, SymbolRegular.Flag24);
        else
        {
            var icon = K.Icon(SymbolRegular.Flag24, 14, B.Text2);
            icon.Opacity = FaintFlag;
            faint = icon;
            content = new Border { Child = icon, Padding = new Thickness(9, 4, 9, 4) };
        }
        Button? b = null;
        b = K.Subtle(content, () => PriorityMenu(b!, level, l => SetPriority(row, l)), new Thickness(0), 11);
        b.HorizontalAlignment = HorizontalAlignment.Right;
        b.VerticalAlignment = VerticalAlignment.Center;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.Margin = new Thickness(2, 0, 4, 0);
        b.ToolTip = "Priority";
        AutomationProperties.SetName(b, level > 0 ? $"Priority: {p.Label}" : "Priority: none");
        return (b, faint);
    }

    private static void PriorityMenu(FrameworkElement at, int level, Action<int> set)
    {
        var menu = new ContextMenu { PlacementTarget = at, Placement = PlacementMode.Bottom };
        foreach (var item in PriorityChoices(level, set)) menu.Items.Add(item);
        menu.IsOpen = true;
    }

    /// <summary>The four levels as checked menu items (a priority is yours alone; it never reaches Canvas).</summary>
    private static IEnumerable<MenuItem> PriorityChoices(int level, Action<int> set)
    {
        foreach (var p in TodoPriority.All)
        {
            var mi = new MenuItem { Header = p.Label, IsCheckable = true, IsChecked = level == p.Id };
            var id = p.Id;
            mi.Click += (_, _) => set(id);
            yield return mi;
        }
    }

    /// <summary>The work's own menu (as the Mac's workMenu has it) with the task's: done, priority, delete.</summary>
    private void FillMenu(ContextMenu menu, WorkRow row)
    {
        menu.Items.Clear();
        MenuItem Item(string text, SymbolRegular icon, Action? act)
        {
            var mi = new MenuItem { Header = text, Icon = new SymbolIcon { Symbol = icon } };
            if (act != null) mi.Click += (_, _) => act();
            return mi;
        }
        var url = row.Custom == true ? null : TodoWords.Text(row.Url);
        if (url != null)
        {
            menu.Items.Add(Item("Open", SymbolRegular.Open24, () => engine.OpenWeb(url, row.Title)));
            if (Shell.PlaceWindow.MenuItem(engine, url, row.Title) is { } apart) menu.Items.Add(apart);
            if (row.Done) menu.Items.Add(Item("See feedback", SymbolRegular.Comment24, () => engine.ActOn(url, row.Title, true)));
            else
            {
                var (label, icon) = TodoWords.ActionOf(row.Type);
                menu.Items.Add(Item(label, icon, () => engine.ActOn(url, row.Title, false)));
            }
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(Item(row.Done ? "Mark not done" : "Mark done", row.Done ? SymbolRegular.ArrowUndo24 : SymbolRegular.CheckmarkCircle24, () => Toggle(row, !row.Done)));
        var priority = Item("Priority", SymbolRegular.Flag24, null);
        foreach (var choice in PriorityChoices(row.Pri ?? 0, l => SetPriority(row, l))) priority.Items.Add(choice);
        menu.Items.Add(priority);
        if (row.Custom == true)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Delete task…", SymbolRegular.Delete24, () => AskDelete(row)));
        }
        if (url != null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(url, row.Title)));
            menu.Items.Add(Item("Copy link", SymbolRegular.Link24, () => CopyLink(url)));
        }
    }

    private void CopyLink(string url)
    {
        if (engine.Absolute(url) is not { } u) return;
        try { Clipboard.SetText(u.ToString()); }
        catch (Exception e) { Log.Write("copy link: " + e.Message); }
    }

    /// <summary>A task of your own on its way out: the question first (and, when it repeats, whether the whole series goes).</summary>
    private void AskDelete(WorkRow row)
    {
        var note = engine.OnBrightspace ? "This removes it from your To Do." : "This removes it from your Canvas planner.";
        var s = new Sheet { Title = $"Delete “{row.Title}”?", Body = K.Wrapped(note, Ty.Body, B.Text) };
        s.Add("Delete task", SheetButtonKind.Primary, () => Delete(row, false));
        if (row.Series is int n && n > 0) s.Add($"Delete all {n + 1} repeats", SheetButtonKind.Secondary, () => Delete(row, true));
        s.Add("Cancel", SheetButtonKind.Close);
        Simpl.Shell.Dialogs.Show(s, Window.GetWindow(View));
    }

    /// <summary>A word with its symbol in a pill of its colour (a priority): the tint behind, the colour itself for the word.</summary>
    private static Border Pill(string text, Color tint, SymbolRegular icon)
    {
        var fore = K.Darkened(tint);
        var t = K.Text(text, Ty.Caption);
        t.Foreground = K.Brush(fore);
        t.FontWeight = FontWeights.SemiBold;
        TodoWords.Tabular(t);
        return new Border
        {
            Child = K.H(4, K.Icon(icon, 12, fore, filled: true), t),
            Padding = new Thickness(8, 2, 9, 3), CornerRadius = new CornerRadius(10),
            Background = K.Brush(Color.FromArgb(Appearance.Dark ? (byte)56 : (byte)36, tint.R, tint.G, tint.B)),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
        };
    }
}
