using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;

namespace Simpl.Screens;

/// <summary>
/// Notifications (the Mac's NotificationsView): what is overdue, due soon, graded, commented on, said and announced, by
/// day, with a filter by kind. A row opens its work and is marked read; under the pointer it offers Mark read and Clear,
/// and its context menu has them too. Read and cleared marks stay on this PC; cleared ones can be brought back. (1.2) On a
/// wide page the kinds sit down a side column with their counts, and Restore under them; on a narrow one they are a
/// SelectorBar over the list (a drop-down when there is no room for every one). Mark all read and Clear all are the
/// title's, at any width: one copy of each.
/// </summary>
public sealed class NotificationsScreen : Screen
{
    /// <summary>Room for the kinds beside the list (the Mac's 860).</summary>
    private const double Wide = 860;

    private readonly Loader<NotificationsData> data;
    private string filter = "all";
    private FrameworkElement? commands;
    private Wpf.Ui.Controls.Button? markAllButton, clearAllButton;
    /// <summary>The narrow page's kinds as a SelectorBar need this much room; less, a drop-down.</summary>
    private double filtersWidth = double.MaxValue;
    private string? toldSub;

    public NotificationsScreen(Engine.Engine engine) : base(engine) => data = Watch(new Loader<NotificationsData>());

    public override string Title => "Notifications";

    public override string? Subtitle => data.Data is { } d
        ? $"{d.Total} {(d.Total == 1 ? "notification" : "notifications")} · {d.Unread} unread"
        : null;

    /// <summary>Mark all read and Clear all (the Mac's toolbar), beside the title.</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            if (commands != null) return commands;
            markAllButton = K.Button("Mark all read", () => Mark("all", read: true), icon: SymbolRegular.MailRead24);
            markAllButton.ToolTip = "Mark every notification read";
            clearAllButton = K.Button("Clear all", ConfirmClear, icon: SymbolRegular.Delete24);
            clearAllButton.ToolTip = "Clear every notification (they can be restored)";
            commands = K.H(8, markAllButton, clearAllButton);
            PaintCommands();
            return commands;
        }
    }

    private void PaintCommands()
    {
        var d = data.Data;
        if (markAllButton != null) markAllButton.IsEnabled = (d?.Unread ?? 0) > 0;
        if (clearAllButton != null) clearAllButton.IsEnabled = (d?.Total ?? 0) > 0;
    }

    public override void Refresh() => data.Ensure(engine, "notifications");

    protected override bool LayoutChanges(double before, double now) =>
        (before >= Wide) != (now >= Wide) || (now < Wide && (before >= filtersWidth) != (now >= filtersWidth));

    protected override FrameworkElement Build()
    {
        if (Subtitle != toldSub)
        {
            toldSub = Subtitle;
            HeaderChange();
        }
        PaintCommands();
        if (data.Data is not { } d) return K.LoadState(data.Error, () => data.Reload(engine, "notifications"));
        if (Width >= Wide) return K.Columns("270, 24, *", Kinds(d).Col(0), Feed(d, true).Col(2));
        return K.V(22, Filters(d), Feed(d, false));
    }

    // ---- the kinds ----

    /// <summary>(1.2) The kinds down the side: each with its symbol, its colour and how many there are, the one shown as
    /// Windows marks a list's selection; under them, Restore when something was cleared (Mark all read and Clear all are
    /// the title's: not here again).</summary>
    private FrameworkElement Kinds(NotificationsData d)
    {
        var list = new Stack { Orientation = Orientation.Vertical, Spacing = 2 };
        list.Children.Add(KindRow("all", "All", SymbolRegular.Alert24, null, d.Total));
        foreach (var c in d.Cats) list.Children.Add(KindRow(c.Key, c.Label, Icon(c.Key), Tone(c.Key), c.Count));
        var v = K.V(14, K.Card(list, new Thickness(6)));
        if (d.Cleared is > 0 and var n)
            v.Children.Add(Wide2(K.Button($"Restore {n} cleared", () => Mark(Array.Empty<string>(), restore: true), icon: SymbolRegular.ArrowUndo24)));
        return v;

        static Wpf.Ui.Controls.Button Wide2(Wpf.Ui.Controls.Button b)
        {
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            return b;
        }
    }

    private FrameworkElement KindRow(string key, string label, SymbolRegular symbol, Color? color, int count)
    {
        var on = filter == key;
        var icon = color is { } c ? K.Icon(symbol, 16, c, filled: true) : K.Icon(symbol, 16, B.Accent, filled: true);
        icon.Width = 22;
        var name = K.Text(label, Ty.Body);
        if (on) name.FontWeight = FontWeights.SemiBold;
        var g = K.Columns("auto, *, auto", icon.Margin(0, 0, 10, 0).Col(0), name.Col(1), K.Text(count.ToString(), Ty.Body, B.Text2).Margin(6, 0, 0, 0).Col(2));
        var b = K.Subtle(g, () => { filter = key; Render(); }, new Thickness(10, 8, 10, 8), 4);
        AutomationProperties.SetName(b, $"{label}, {count}");
        if (!on) return b;
        AutomationProperties.SetItemStatus(b, "Selected");
        b.Bg(B.Subtle);
        var pill = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }.Bg(B.Accent);
        return new Grid { Children = { b, pill } };
    }

    /// <summary>The kinds over the list on a narrow page (the Mac's segmented picker), folding to a drop-down when the
    /// page is too narrow for every one.</summary>
    private FrameworkElement Filters(NotificationsData d)
    {
        var keys = new List<string> { "all" };
        var names = new List<string> { d.Total > 0 ? $"All {d.Total}" : "All" };
        foreach (var c in d.Cats)
        {
            keys.Add(c.Key);
            names.Add(c.Count > 0 ? $"{c.Label} {c.Count}" : c.Label);
        }
        var index = Math.Max(0, keys.IndexOf(filter));
        var bar = new SelectorBar(names, index) { HorizontalAlignment = HorizontalAlignment.Left };
        bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        filtersWidth = Math.Max(1, bar.DesiredSize.Width);
        if (filtersWidth <= Width)
        {
            bar.Changed += i => { filter = keys[i]; Render(); };
            return bar;
        }
        var box = new ComboBox { ItemsSource = names, SelectedIndex = index, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(box, "Show");
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0 || keys[box.SelectedIndex] == filter) return;
            filter = keys[box.SelectedIndex];
            Render();
        };
        return box;
    }

    // ---- the days ----

    /// <summary>The days with something of the kind picked, each a section of its own.</summary>
    private FrameworkElement Feed(NotificationsData d, bool wide)
    {
        var days = d.Days
            .Select(day => (day.Title, Rows: day.Rows.Where(r => filter == "all" || r.Cat == filter).ToList()))
            .Where(x => x.Rows.Count > 0)
            .ToList();
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 22 };
        if (days.Count == 0) v.Children.Add(Nothing(d.Total > 0 ? "Nothing in this category." : "Cleared notifications do not come back."));
        foreach (var (title, rows) in days)
        {
            var list = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
            for (var k = 0; k < rows.Count; k++)
            {
                if (k > 0) list.Children.Add(K.Divider(52));
                list.Children.Add(Row(rows[k]));
            }
            v.Children.Add(K.V(0, Heading.Section(title, rows.Count.ToString()), K.Card(list, new Thickness(4))));
        }
        if (!wide && d.Cleared is > 0 and var n)
        {
            var restore = K.Link($"Restore {n} cleared {(n == 1 ? "notification" : "notifications")}", () => Mark(Array.Empty<string>(), restore: true));
            restore.HorizontalAlignment = HorizontalAlignment.Left;
            v.Children.Add(restore);
        }
        return v;
    }

    /// <summary>Nothing to show (the Mac's ContentUnavailableView).</summary>
    private static FrameworkElement Nothing(string text)
    {
        var v = K.V(10, K.Icon(SymbolRegular.AlertOff24, 40, B.Text3), K.Text("Nothing here", Ty.Subtitle).Center(), K.Wrapped(text, Ty.Body, B.Text2).Center());
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.Margin = new Thickness(0, 30, 0, 30);
        return v;
    }

    /// <summary>A notification: its kind's symbol on its colour, its title (heavier while unread, with the accent's dot)
    /// and its line; under the pointer, Mark read and Clear at its end. The kind is said by its symbol alone — its word
    /// is the symbol's tooltip and is spoken — so most rows are two lines.</summary>
    private FrameworkElement Row(NotifRow n)
    {
        var tone = Tone(n.Cat);
        var tile = K.IconTile(Icon(n.Cat), tone, 32);
        tile.VerticalAlignment = VerticalAlignment.Top;
        tile.ToolTip = n.CatLabel;
        var title = K.Wrapped(n.Title, Ty.Body, B.Text, 2);
        if (!n.Read) title.FontWeight = FontWeights.SemiBold;
        var words = K.V(3, title);
        if (!string.IsNullOrEmpty(n.Sub)) words.Children.Add(K.Wrapped(n.Sub!, Ty.Body, B.Text2, 3));

        // at its end: the unread dot — or, under the pointer, Mark read (unread) and Clear
        var dot = new Ellipse { Width = 9, Height = 9, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 4, 0) }.Bg(B.Accent);
        dot.Visibility = n.Read ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(dot, "Unread");
        var mark = K.IconButton(n.Read ? SymbolRegular.MailUnread24 : SymbolRegular.MailRead24, () => Mark(new[] { n.Id }, read: !n.Read), n.Read ? "Mark unread" : "Mark read", 16, 28);
        var clear = K.IconButton(SymbolRegular.Dismiss24, () => Mark(new[] { n.Id }, gone: true), "Clear", 14, 28);
        var tools = K.H(4, mark, clear);
        tools.HorizontalAlignment = HorizontalAlignment.Right;
        tools.VerticalAlignment = VerticalAlignment.Top;
        tools.Visibility = Visibility.Hidden;
        var end = new Grid { Width = 60, Children = { dot, tools } };

        var g = K.Columns("auto, *, auto", tile.Margin(0, 0, 12, 0).Col(0), words.Col(1), end.Margin(8, 0, 0, 0).Col(2));
        var b = K.Subtle(g, () => Open(n), new Thickness(8, 9, 8, 9));
        b.MouseEnter += (_, _) =>
        {
            dot.Visibility = Visibility.Hidden;
            tools.Visibility = Visibility.Visible;
            Motion.FadeIn(tools, Motion.Fast);
        };
        b.MouseLeave += (_, _) =>
        {
            tools.Visibility = Visibility.Hidden;
            dot.Visibility = n.Read ? Visibility.Collapsed : Visibility.Visible;
        };
        b.ContextMenu = Menu(n);
        AutomationProperties.SetName(b, string.Join(", ", new[] { n.CatLabel, n.Read ? "" : "unread", n.Title, n.Sub ?? "" }.Where(x => x.Length > 0)));
        return b;
    }

    /// <summary>A row's own menu: Open, Mark read (or unread), Clear.</summary>
    private ContextMenu Menu(NotifRow n)
    {
        var m = new ContextMenu();
        var open = CalendarKit.Item("Open", SymbolRegular.Open24, () => Open(n));
        open.IsEnabled = n.Url != null;
        m.Items.Add(open);
        m.Items.Add(CalendarKit.Item(n.Read ? "Mark unread" : "Mark read", n.Read ? SymbolRegular.MailUnread24 : SymbolRegular.MailRead24, () => Mark(new[] { n.Id }, read: !n.Read)));
        m.Items.Add(new Separator());
        m.Items.Add(CalendarKit.Item("Clear", SymbolRegular.Dismiss24, () => Mark(new[] { n.Id }, gone: true)));
        return m;
    }

    // ---- what is done to them ----

    /// <summary>A row opened: marked read, and its work opened.</summary>
    private void Open(NotifRow n)
    {
        if (!n.Read) Mark(new[] { n.Id }, read: true);
        if (n.Url != null) engine.OpenWeb(n.Url, n.Title);
    }

    private void ConfirmClear() =>
        Dialogs.Confirm(Window.GetWindow(View), "Clear all notifications?", "They can be restored from the foot of the list.", "Clear all", () => Mark("all", gone: true));

    /// <summary>Read or unread, cleared, or the cleared brought back (ids: "all", or the rows'): the page keeps the marks on
    /// this PC; the list read again, and the navigation pane's count with it.</summary>
    private async void Mark(object ids, bool? read = null, bool gone = false, bool restore = false)
    {
        var args = new JsonObject
        {
            ["ids"] = ids is string all ? JsonValue.Create(all) : new JsonArray(((IEnumerable<string>)ids).Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()),
        };
        if (read is { } r) args["read"] = r;
        if (gone) args["gone"] = true;
        if (restore) args["restore"] = true;
        await engine.Act("notifMark", args);
        data.Reload(engine, "notifications");
        await engine.RefreshSnapshot();
    }

    // ---- kinds' symbols and colours ----

    private static SymbolRegular Icon(string cat) => cat switch
    {
        "overdue" => SymbolRegular.Warning24,
        "soon" => SymbolRegular.Clock24,
        "graded" => SymbolRegular.DataBarVertical24,
        "feedback" => SymbolRegular.Comment24,
        "message" => SymbolRegular.Mail24,
        "discuss" => SymbolRegular.People24,
        "announce" => SymbolRegular.Megaphone24,
        _ => SymbolRegular.Settings24,
    };

    private static Color Tone(string cat) => cat switch
    {
        "overdue" => K.Tone("red"),
        "soon" => K.Tone("orange"),
        "graded" => K.Tone("green"),
        "feedback" => K.Tone("blue"),
        "message" => Color.FromRgb(0x00, 0x99, 0xbc),
        "discuss" => K.Tone("purple"),
        "announce" => Color.FromRgb(0x5e, 0x5c, 0xe6),
        _ => K.Tone("gray"),
    };
}
