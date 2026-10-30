using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// A tool's list, its card pressed: every place the tool is used, to choose the one to open — (0.3.100) in a popup of
/// its own over the window, as the app's dialogs are: centred, 480 wide, the window dimmed behind it, arriving from a
/// touch larger as it fades in (a flyout hung under its card before, cramped by the room under it).
/// (0.3.98) A viewer of its own rather than a plain list: at its head, on a wash of the tool's colour, its tile, its name,
/// its site, how many places in how many courses, and how many are still to do; To do and All when it has both (To do to
/// start with); a filter once there are more than eight; then the next still to do on a card of its own with Open, and
/// the rest — by course, under the course's name and how many it has there, when it is in more than one — each with a
/// mark of where it stands (to do, done, missing, late, a module item), its title, its line and its status, a chevron
/// under the pointer; at its foot, the keys it takes. ↑ and ↓ move between them, Enter opens one, Escape, its × or a
/// press anywhere outside it closes it, and opening one closes it first.
/// </summary>
public sealed partial class ExternalToolsScreen
{
    private const double ListWidth = 480, ListHeight = 560;

    private Sheet? list;
    private string? listId;
    private FrameworkElement? listCard;
    private Grid? listSurface;
    private TextBox? listFilter;
    /// <summary>Where the rows go (the list's scrolling stack).</summary>
    private Stack? rowsHost;
    /// <summary>The rows shown, in order, each with what it opens: ↑ and ↓ move between them, Enter opens one.</summary>
    private readonly List<(Button Row, Action Open)> listRows = new();
    /// <summary>(0.3.98) To do (0) or All (1), when the tool has both (<see cref="listScoped"/>).</summary>
    private int listScope;
    private bool listScoped;

    /// <summary>A card pressed: its list.</summary>
    private void ToggleList(string id, FrameworkElement card)
    {
        if (listId == id) return;
        if (ToolOf(id) is { } t) OpenList(t, card);
    }

    /// <summary>Escape: the list closed, the keyboard back on its card.</summary>
    public override bool Escape()
    {
        if (list == null) return false;
        CloseList(returnFocus: true);
        return true;
    }

    /// <summary>
    /// The list of a tool's places, in its popup over the window: 480 wide, as tall as its rows up to 560 (then they
    /// scroll), and no taller than the window has room for. The filter takes the keyboard when there is one, else the
    /// first row.
    /// </summary>
    private void OpenList(Tool t, FrameworkElement card)
    {
        CloseList();
        var places = t.Courses.Where(c => c.Items.Count > 0).ToList();
        if (places.Count == 0) return;
        var count = places.Sum(c => c.Items.Count);

        // (0.3.98) its head, on a wash of its colour: the tool's letter, its name, its site, how many places in how many
        // courses — and how many still to do
        var tint = K.Hex(t.Courses.FirstOrDefault()?.Color, Gray);
        var tile = Monogram(t, 38);
        tile.VerticalAlignment = VerticalAlignment.Center;
        var where = places.Count == 1 ? "1 course" : Simpl.Tools.ToolKit.Plural(places.Count, "course");
        var about = $"{Places(t)} in {where}";
        if (t.Host.Length > 0 && !string.Equals(t.Host, t.Name, StringComparison.OrdinalIgnoreCase)) about = $"{t.Host} · {about}";
        var words = K.V(1, K.Text(t.Name, Ty.BodyStrong), K.Text(about, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var toDo = places.Sum(c => c.Items.Count(i => i.Upcoming));
        FrameworkElement? still = null;
        if (toDo > 0)
        {
            var n = K.Text($"{toDo}", Ty.Subtitle);
            n.HorizontalAlignment = HorizontalAlignment.Right;
            var w = K.Text("to do", Ty.Caption, B.Text2);
            w.HorizontalAlignment = HorizontalAlignment.Right;
            still = K.V(0, n, w).Margin(12, 0, 0, 0);
            still.VerticalAlignment = VerticalAlignment.Center;
        }
        // (0.3.100: its × at the right, as a popup's)
        var close = K.IconButton(SymbolRegular.Dismiss24, () => CloseList(returnFocus: true), "Close (Esc)", 14, 30);
        close.VerticalAlignment = VerticalAlignment.Center;
        close.Margin = new Thickness(10, 0, -6, 0);
        var head = new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            Background = new LinearGradientBrush(K.WithAlpha(tint, Appearance.Dark ? 0.26 : 0.18), K.WithAlpha(tint, 0.02), 90),
            Child = K.Columns("auto, *, auto, auto", tile.Margin(0, 0, 12, 0).Col(0), words.Col(1), still?.Col(2), close.Col(3)),
        };

        // (0.3.98) To do and All, when it has both: To do to start with
        listScope = 0;
        listScoped = toDo > 0 && toDo < count;
        SelectorBar? scope = null;
        if (listScoped)
        {
            scope = new SelectorBar(new[] { $"To do · {toDo}", $"All · {count}" }, 0) { Margin = new Thickness(10, 2, 10, 6) };
            scope.Changed += i => { listScope = i; FillList(t); };
        }

        if (count > 8)
        {
            listFilter = new TextBox
            {
                PlaceholderText = Noun(t) switch { "assignment" => "Filter assignments", "module item" => "Filter module items", _ => "Filter" },
                Icon = new SymbolIcon { Symbol = SymbolRegular.Filter24 }, IconPlacement = ElementPlacement.Left,
                MinHeight = 32, Margin = new Thickness(12, scope == null ? 8 : 0, 12, 8),
            };
            listFilter.TextChanged += (_, _) => FillList(t);
        }
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 2 };
        var scroller = new ScrollViewer
        {
            Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, Margin = new Thickness(6, 4, 4, 6), Padding = new Thickness(0, 0, 2, 0),
        };
        var content = K.Rows("auto, auto, auto, *, auto", head.Row(0), scope?.Row(1), listFilter?.Row(2), scroller.Row(3), Foot().Row(4));

        var surface = new Grid { MaxHeight = ListHeight, Children = { content } };
        surface.SetResourceReference(TextElement.ForegroundProperty, B.Text);
        AutomationProperties.SetName(surface, $"{t.Name}: {Places(t)}");
        surface.PreviewKeyDown += OnListKey;
        // (a light one: a press on the dimmed window round it closes it; Escape is the dialog's own)
        var sheet = new Sheet { Body = surface, Bare = true, Width = ListWidth, Light = true, Name = t.Name };
        sheet.Closed += () => { if (list == sheet) Forget(); };
        list = sheet;
        listId = t.Id;
        listCard = card;
        listSurface = surface;
        rowsHost = rows;
        FillList(t);
        Dialogs.Show(sheet, Window.GetWindow(card));
        // (its card's "Choose which …" is said: it would only cover the list)
        ToolTipService.SetIsEnabled(card, false);
        surface.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (listSurface != surface) return;
            if (listFilter != null) { listFilter.Focus(); listFilter.CaretIndex = listFilter.Text.Length; }
            else if (listRows.Count > 0) listRows[0].Row.Focus();
        });
        if (Shot.Asked) Log.Write($"tool list shown: {t.Name}");
    }

    /// <summary>
    /// The list's rows for the choice and the filter's words (all of them with none): the next still to do first, on its
    /// card, while nothing is typed; then by course when the tool is in more than one — the course's dot, name and count
    /// over its own — each place a row.
    /// </summary>
    private void FillList(Tool t)
    {
        if (rowsHost is not { } rows) return;
        rows.Children.Clear();
        listRows.Clear();
        var q = (listFilter?.Text ?? "").Trim();
        var places = t.Courses.Where(c => c.Items.Count > 0).ToList();
        var grouped = places.Count > 1;
        // (the soonest still to do — as the card says it — else the first still to do as listed)
        var next = t.Next is { } n ? places.FirstOrDefault(c => c.Name == n.Course)?.Items.FirstOrDefault(i => i.Upcoming && i.Title == n.Title) : null;
        next ??= places.SelectMany(c => c.Items).FirstOrDefault(i => i.Upcoming);
        var toDoOnly = listScoped && listScope == 0;
        bool Has(string? s) => s?.Contains(q, StringComparison.CurrentCultureIgnoreCase) == true;
        List<Item> Shown(Course c) => c.Items.Where(i => (!toDoOnly || i.Upcoming) && (q.Length == 0 || Has(i.Title) || Has(i.Sub) || Has(i.Status?.Word) || Has(c.Name))).ToList();
        var lead = q.Length == 0 && next != null && places.Any(c => Shown(c).Contains(next));
        if (lead && places.FirstOrDefault(c => c.Items.Contains(next!)) is { } nc) rows.Children.Add(UpNext(t, nc, next!, grouped));
        var any = false;
        foreach (var c in places)
        {
            var items = Shown(c).Where(i => !(lead && ReferenceEquals(i, next))).ToList();
            if (items.Count == 0) continue;
            if (grouped) rows.Children.Add(GroupHead(c, items.Count, first: !any && !lead));
            else if (lead && !any) rows.Children.Add(Label(toDoOnly ? "Later" : Noun(t) switch { "assignment" => "Assignments", "module item" => "Module items", _ => "All" }));
            foreach (var it in items) rows.Children.Add(ListRow(t, c, it, ReferenceEquals(it, next)));
            any = true;
        }
        if (!any && !lead) rows.Children.Add(K.Wrapped(q.Length == 0 ? "Nothing still to do." : $"Nothing matches “{q}”.", Ty.Body, B.Text2).Margin(10, 6, 10, 12));
    }

    /// <summary>A course's name over its places in the list: its dot, its name and how many it has there, small and quiet.</summary>
    private static FrameworkElement GroupHead(Course c, int count, bool first)
    {
        var name = K.Text(c.Name, Ty.Caption, B.Text2);
        name.FontWeight = FontWeights.Medium;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var many = K.Text($"{count}", Ty.Caption, B.Text3);
        var h = K.Columns("auto, *, auto", K.Dot(K.Hex(c.Color), 7).Margin(0, 0, 8, 0).Col(0), name.Col(1), many.Margin(8, 0, 0, 0).Col(2));
        h.Margin = new Thickness(10, first ? 2 : 12, 10, 4);
        AutomationProperties.SetName(h, $"{c.Name}, {count}");
        return h;
    }

    /// <summary>A quiet word over the places after the next one's card, when they are all in one course.</summary>
    private static FrameworkElement Label(string word)
    {
        var t = K.Text(word, Ty.Caption, B.Text2);
        t.FontWeight = FontWeights.Medium;
        return t.Margin(10, 10, 10, 4);
    }

    /// <summary>(0.3.98) Where a place stands, at a glance: a tick for work done, a mark for work missing, a clock for work
    /// late, a calendar for work still to do, a list for a module item — in its status's colour, or its course's.</summary>
    private static Border Mark(Course c, Item it, double size = 28)
    {
        var (symbol, tone) = it.Kind == "moduleItem" ? (SymbolRegular.TextBulletListLtr24, (string?)null) : (it.Status?.Kind ?? "") switch
        {
            "good" => (SymbolRegular.Checkmark24, "good"),
            "bad" => (SymbolRegular.Important24, "bad"),
            "warn" => (SymbolRegular.Clock24, "warn"),
            _ => (it.Upcoming ? SymbolRegular.CalendarLtr24 : SymbolRegular.Document24, (string?)null),
        };
        var tile = K.IconTile(symbol, tone != null ? K.Tone(tone) : K.Hex(c.Color, Gray), size);
        tile.CornerRadius = new CornerRadius(size * 0.3);
        return tile;
    }

    /// <summary>(0.3.98) The next still to do, on a card of its own: Up next, its mark, its title, its line (and its course
    /// when there are several), and Open.</summary>
    private Button UpNext(Tool t, Course c, Item it, bool grouped)
    {
        var accent = Appearance.Accent;
        var kicker = K.Text("UP NEXT", Ty.Caption);
        kicker.FontWeight = FontWeights.Bold;
        kicker.Foreground = K.Brush(K.Darkened(accent));
        var title = K.Wrapped(it.Short is { Length: > 0 } s0 ? s0 : Short(t, it.Title), Ty.BodyStrong, B.Text, 2);
        var line = string.Join(" · ", new[] { it.Sub, grouped ? c.Name : null }.Where(x => !string.IsNullOrEmpty(x)));
        var words = K.V(2, title, line.Length > 0 ? K.Text(line, Ty.Caption, B.Text2) : null);
        words.VerticalAlignment = VerticalAlignment.Center;
        var openWord = K.Text("Open", Ty.Body);
        openWord.FontWeight = FontWeights.SemiBold;
        openWord.Foreground = Brushes.White;
        var open = new Border
        {
            Background = K.Brush(accent), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 4, 10, 5), VerticalAlignment = VerticalAlignment.Center,
            Child = K.H(4, openWord, K.Icon(SymbolRegular.ChevronRight16, 12, Colors.White)),
        };
        var g = K.Columns("auto, *, auto", Mark(c, it, 36).Margin(0, 0, 12, 0).Col(0), words.Col(1), open.Margin(10, 0, 0, 0).Col(2));
        var face = new Border
        {
            Padding = new Thickness(12, 10, 12, 12), CornerRadius = new CornerRadius(8),
            Background = K.Brush(K.WithAlpha(accent, Appearance.Dark ? 0.16 : 0.08)), BorderBrush = K.Brush(K.WithAlpha(accent, 0.3)), BorderThickness = new Thickness(1),
            Child = K.V(8, kicker, g),
        };
        void Go()
        {
            CloseList();
            Open(engine, t, c, it);
        }
        var b = K.Subtle(face, Go, new Thickness(0), 8);
        b.Margin = new Thickness(0, 0, 0, 4);
        AutomationProperties.SetName(b, string.Join(", ", new[] { "Up next", it.Title, it.Sub, grouped ? c.Name : null }.Where(x => !string.IsNullOrEmpty(x))));
        listRows.Add((b, Go));
        return b;
    }

    /// <summary>A place in the list: its mark, its title (two lines at most), the line under it (when it is due and its
    /// points, or its module), Next before that line for the next one to do (when it is not on its card, a filter typed),
    /// and where the work stands at its end, as every list of work shows it, a chevron after it under the pointer. A press
    /// opens it.</summary>
    private Button ListRow(Tool t, Course c, Item it, bool next)
    {
        var title = K.Wrapped(it.Short is { Length: > 0 } s0 ? s0 : Short(t, it.Title), Ty.Body, B.Text, 2);
        var sub = K.Text(it.Sub ?? "", Ty.Caption, B.Text2);
        FrameworkElement line = sub;
        if (next) line = K.Columns("auto, *", K.Chip("Next", Appearance.Accent, strong: true).Margin(0, 0, 6, 0).Col(0), sub.Col(1));
        var words = K.V(2, title, line);
        words.VerticalAlignment = VerticalAlignment.Center;
        var chevron = K.Icon(SymbolRegular.ChevronRight16, 12, B.Text3);
        chevron.Margin = new Thickness(8, 0, 0, 0);
        chevron.Opacity = 0;
        var g = K.Columns("auto, *, auto, auto", Mark(c, it).Margin(0, 0, 11, 0).Col(0), words.Col(1), chevron.Col(3));
        if (it.Status is { Word.Length: > 0 } s && !CourseKit.IsPlainToDo(s))
        {
            var chip = CourseKit.StatusChip(s.Word, s.Kind);
            chip.Margin = new Thickness(10, 0, 0, 0);
            chip.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(chip.Col(2));
        }
        void Go()
        {
            CloseList();
            Open(engine, t, c, it);
        }
        var b = K.Subtle(g, Go, new Thickness(10, 7, 10, 7), 6);
        void Lit() => chevron.Opacity = b.IsMouseOver || b.IsKeyboardFocusWithin ? 1 : 0;
        b.MouseEnter += (_, _) => Lit();
        b.MouseLeave += (_, _) => Lit();
        b.IsKeyboardFocusWithinChanged += (_, _) => Lit();
        AutomationProperties.SetName(b, string.Join(", ", new[] { it.Title, next ? "next" : null, it.Sub, it.Status?.Word }.Where(x => !string.IsNullOrEmpty(x))));
        listRows.Add((b, Go));
        return b;
    }

    /// <summary>(0.3.98) The keys the list takes, at its foot: Enter, ↑ ↓ and Esc, each in a key's outline.</summary>
    private static FrameworkElement Foot()
    {
        static FrameworkElement Key(string cap, string word)
        {
            var k = K.Text(cap, Ty.Caption, B.Text2);
            k.FontSize = 11;
            var box = new Border { Child = k, Padding = new Thickness(5, 0, 5, 1), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center }.Stroke(B.CardStroke);
            return K.H(5, box, K.Text(word, Ty.Caption, B.Text2));
        }
        var row = K.H(16, Key("Enter", "Open"), Key("↑ ↓", "Choose"), Key("Esc", "Close"));
        row.Margin = new Thickness(14, 7, 14, 8);
        var foot = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0), CornerRadius = new CornerRadius(0, 0, 8, 8) }.Stroke(B.Divider);
        AutomationProperties.SetName(foot, "Enter opens, the arrow keys choose, Escape closes");
        return foot;
    }

    /// <summary>The list's keys: ↓ from the filter to the first row and down the rows, ↑ back up to the filter, Home and
    /// End to the first and last rows, Enter opens the row with the keyboard (from the filter, the first one shown),
    /// Escape folds the list away.</summary>
    private void OnListKey(object sender, KeyEventArgs e)
    {
        if (list == null) return;
        var inFilter = listFilter?.IsKeyboardFocusWithin == true;
        var at = listRows.FindIndex(r => r.Row.IsKeyboardFocusWithin);
        switch (e.Key)
        {
            case Key.Down when listRows.Count > 0:
                FocusRow(at < 0 ? 0 : Math.Min(at + 1, listRows.Count - 1));
                e.Handled = true;
                break;
            case Key.Up when at > 0:
                FocusRow(at - 1);
                e.Handled = true;
                break;
            case Key.Up when at == 0 && listFilter != null:
                listFilter.Focus();
                e.Handled = true;
                break;
            case Key.Home when at >= 0:
                FocusRow(0);
                e.Handled = true;
                break;
            case Key.End when at >= 0:
                FocusRow(listRows.Count - 1);
                e.Handled = true;
                break;
            case Key.Enter when at >= 0:
                listRows[at].Open();
                e.Handled = true;
                break;
            case Key.Enter when inFilter && listRows.Count > 0:
                listRows[0].Open();
                e.Handled = true;
                break;
            case Key.Escape:
                CloseList(returnFocus: true);
                e.Handled = true;
                break;
        }
    }

    private void FocusRow(int i)
    {
        var row = listRows[i].Row;
        row.Focus();
        row.BringIntoView();
    }

    /// <summary>The list closed (what it was for has been chosen, or left); <paramref name="returnFocus"/>: the keyboard
    /// back on its card.</summary>
    private void CloseList(bool returnFocus = false)
    {
        if (list is not { } sheet) return;
        var card = listCard;
        Forget();
        sheet.Close();
        if (returnFocus) card?.Focus();
    }

    /// <summary>What the open list kept, let go: it has closed, or is closing.</summary>
    private void Forget()
    {
        if (list == null) return;
        list = null;
        listCard?.ClearValue(ToolTipService.IsEnabledProperty);
        listId = null;
        listCard = null;
        listSurface = null;
        listFilter = null;
        rowsHost = null;
        listRows.Clear();
    }

    /// <summary>
    /// The page built again with a list open (a new answer, the columns changed, the look): the list stays open over it —
    /// its card now the new one — or closes, its tool gone.
    /// </summary>
    private void Rehang(List<Tool> shown)
    {
        if (listId is not { } id) return;
        if (shown.FirstOrDefault(t => t.Id == id) is not { } tool || !Chooser(tool)) { CloseList(); return; }
        if (cards.TryGetValue(id, out var card)) listCard = card;
    }
}
