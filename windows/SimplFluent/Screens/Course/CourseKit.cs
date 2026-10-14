using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// The pieces a course's screens share (the Mac's Rows.swift and Components.swift as the course screens use them): a
/// section's heading over one card of rows, rows of work and of posts, the facts at a home's head, a quiet note where a
/// list is empty, two columns on a wide page, and the menus — Open, Open in Canvas, Copy link; Hand in or See feedback.
/// </summary>
internal static class CourseKit
{
    // ---- symbols (the Mac's Glyph) ----

    /// <summary>A section of a course or a group by its kind (the Mac's Glyph.section).</summary>
    public static SymbolRegular Section(string? kind) => kind switch
    {
        "collaborations" => SymbolRegular.PeopleTeam24,
        "conferences" => SymbolRegular.Video24,
        "home" or "announcements" or "assignments" or "discussions" or "grades" or "modules" or "pages" or "files"
            or "quizzes" or "people" or "groups" or "syllabus" or "calendar" => Glyph.Section(kind),
        _ => SymbolRegular.Apps24,
    };

    /// <summary>A kind of item in a list or a module (the Mac's Glyph.item).</summary>
    public static SymbolRegular Item(string? type) => type switch
    {
        "Assignment" or "assignment" => SymbolRegular.DocumentText24,
        "Quiz" or "quiz" => SymbolRegular.Quiz24, // (its own: the checklist is To Do's)
        "Discussion" or "discussion" or "discussion_topic" => SymbolRegular.ChatMultiple24,
        "Page" or "page" => SymbolRegular.DocumentOnePage24,
        "File" or "file" => SymbolRegular.Document24,
        "ExternalUrl" or "link" => SymbolRegular.Link24,
        "ExternalTool" or "Tool" or "tool" => SymbolRegular.PuzzlePiece24,
        "SubHeader" => SymbolRegular.TextFont24,
        "announcement" => SymbolRegular.Megaphone24,
        "folder" => SymbolRegular.Folder24,
        "event" => SymbolRegular.CalendarLtr24,
        "task" or "My task" => SymbolRegular.CheckmarkCircle24,
        _ => SymbolRegular.Document24,
    };

    /// <summary>A status's colour by its tone (the Mac's StatusChip: bad, warn, good, info, purple; else grey).</summary>
    public static Color Tone(string? tone) => tone switch
    {
        "bad" => K.Tone("red"),
        "warn" => K.Tone("orange"),
        "good" => K.Tone("green"),
        "info" => K.Tone("blue"),
        "purple" => K.Tone("purple"),
        _ => K.Tone(null),
    };

    public static Color Red => K.Tone("red");
    public static Color Green => K.Tone("green");
    public static Color Gray => K.Tone(null);

    /// <summary>A course's colour made to read as words or a symbol on the page (lighter on dark, deeper on light).</summary>
    public static SolidColorBrush Ink(Color c) => new(K.Darkened(c));

    // ---- where a section is ----

    /// <summary>A section's name as its course gives it (its tab, as the pane lists it), else Simpl's own.</summary>
    public static string SectionTitle(Engine.Engine engine, string ctx, string kind, string fallback) =>
        engine.Sections.TryGetValue(ctx, out var list) && list.FirstOrDefault(s => s.Kind == kind) is { } s ? s.Label : fallback;

    /// <summary>A section's own page on the school's site, for Open in Canvas and Copy link.</summary>
    public static string? SectionUrl(Engine.Engine engine, string ctx, string kind) => engine.SchoolUrl(Place.Section(ctx, kind))?.ToString();

    /// <summary>The id in a context: "courses/101" → "101".</summary>
    public static string IdOf(string ctx) => ctx.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";

    /// <summary>A word with each part capitalised (Swift's capitalized), for a section with no name of its own.</summary>
    public static string Capitalized(string s) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.Replace('_', ' '));

    /// <summary>A course's or a group's own colour, as the pane has it (its square), for the mark before a page's title;
    /// null for a context the pane does not list.</summary>
    public static Color? ContextColor(Engine.Engine engine, string ctx)
    {
        var id = IdOf(ctx);
        string? hex = ctx.StartsWith("courses/", StringComparison.Ordinal) ? engine.Courses.FirstOrDefault(c => c.Id == id)?.Color
            : ctx.StartsWith("groups/", StringComparison.Ordinal) ? engine.Groups.FirstOrDefault(g => g.Id == id)?.Color
            : null;
        return Support.Appearance.Hex(hex);
    }

    // ---- names ----

    /// <summary>Whether a course's other name says something none of the lines shown does — compared as the engine
    /// compares names (its saysMore): case, spaces, dashes, underscores, dots and a term prefix ("F26-", "SP26 ",
    /// "2026FA-") set aside, and a name that is part of one shown saying nothing new ("MATH-021-20" under
    /// "F26-MATH 021 20").</summary>
    public static bool SaysMore(string? name, params string?[] shown)
    {
        var key = NameKey(name);
        return key.Length > 0 && !shown.Any(s => NameKey(s).Contains(key, StringComparison.Ordinal));
    }

    private static readonly System.Text.RegularExpressions.Regex TermPrefix = new(
        @"^(?:(?:F|S|W|U|FA|SP|SU|WI|SM|FL|SPR|FAL|SUM|WIN)\d{2,4}|\d{4}(?:FA|SP|SU|WI|F|S|U|W)?|\d{6})[-_\s:]+(?=\S)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex NameGaps = new(@"[\s\-_–—.]+", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>A name as names are compared: its term prefix off, lower case, no spaces, dashes, underscores or dots.</summary>
    internal static string NameKey(string? s) => NameGaps.Replace(TermPrefix.Replace((s ?? "").Trim(), "").ToLowerInvariant(), "");

    // ---- surfaces ----

    /// <summary>A row of a card that opens something (the Mac's RowLink): the pointer's wash, the whole row the target.</summary>
    public static Button RowLink(UIElement content, Action click)
    {
        var b = K.Subtle(content, click, new Thickness(8, 9, 8, 9), 4);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        return b;
    }

    /// <summary>A row shown as a link is, with nowhere to go (the Mac's OptionalRowLink, disabled).</summary>
    public static FrameworkElement StillRow(FrameworkElement content)
    {
        content.Margin = new Thickness(8, 9, 8, 9);
        return content;
    }

    /// <summary>The hairline between two rows of a card, starting where their words do (the Mac's RowDivider).</summary>
    public static Border Divider(double inset = 52)
    {
        var d = K.Divider(inset);
        d.Margin = new Thickness(inset, 0, 8, 0);
        return d;
    }

    /// <summary>A card's rows with the hairline between each two (the Mac's DividedRows).</summary>
    public static Stack Divided<T>(IEnumerable<T> items, Func<T, UIElement> row, double inset = 52)
    {
        var v = new Stack { Orientation = Orientation.Vertical };
        var first = true;
        foreach (var item in items)
        {
            if (!first) v.Children.Add(Divider(inset));
            v.Children.Add(row(item));
            first = false;
        }
        return v;
    }

    /// <summary>A section of a page (the Mac's CardSection): its heading — a count, a link at its end — and ONE card
    /// under it holding its rows.</summary>
    public static FrameworkElement CardSection(string title, string? trailing, UIElement? accessory, UIElement content, double padding = 10)
    {
        var card = K.Card(content, new Thickness(padding));
        return K.V(0, Heading.Section(title, trailing, accessory), card);
    }

    /// <summary>The one card of a section's rows, with no heading of its own (the screen's title names them): it ends
    /// after its last row, however tall the page is (a card running on, empty, to the window's foot read as more to come).</summary>
    public static Border RowsCard(UIElement content)
    {
        var card = K.Card(content, new Thickness(10));
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    /// <summary>What a section says when it has nothing in it: quiet and centred where its rows would be, no card round
    /// nothing (the Mac's EmptyCard).</summary>
    public static FrameworkElement EmptyCard(string text, SymbolRegular icon)
    {
        var v = K.V(12, K.Icon(icon, 34, B.Text3), K.Wrapped(text, Ty.Body, B.Text2).Center());
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.Margin = new Thickness(0, 48, 0, 48);
        return v;
    }

    /// <summary>A link-styled button at a section heading's end (All announcements, All 12).</summary>
    public static FrameworkElement HeadLink(string text, Action click) => K.Link(text, click);

    // ---- rows ----

    /// <summary>A row with a title, a line under it and something at its end (the Mac's InfoRow): the shape most lists
    /// here share.</summary>
    public static FrameworkElement InfoRow(string title, string? sub, SymbolRegular? symbol, Color tint, UIElement? trailing = null, bool unread = false)
    {
        var name = K.Wrapped(title, Ty.Body, B.Text, 2);
        if (unread) name.FontWeight = FontWeights.SemiBold;
        var words = K.V(2, name);
        if (!string.IsNullOrEmpty(sub)) words.Children.Add(K.Wrapped(sub!, Ty.Caption, B.Text2, 2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var g = K.Columns("auto, *, auto");
        if (symbol is { } s) g.Children.Add(K.IconTile(s, tint, 32).Margin(0, 0, 12, 0).Col(0));
        g.Children.Add(words.Col(1));
        if (trailing is FrameworkElement t)
        {
            t.Margin = new Thickness(10, 0, 0, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(t.Col(2));
        }
        return g;
    }

    /// <summary>The mark at the end of what opens in a Simpl window of its own (a tool, another site): the one symbol for
    /// it everywhere — a course's More, a module's tool, External Tools' course chips — and never the ↗ of a link that
    /// leaves for the browser.</summary>
    public static FrameworkElement OwnWindowMark(double size = 12, string brush = B.Text3)
    {
        var i = K.Icon(SymbolRegular.WindowNew20, size, brush).Tip("Opens in a window of its own");
        System.Windows.Automation.AutomationProperties.SetName(i, "Opens in a window of its own");
        return i;
    }

    /// <summary>A status in words on a wash of its colour (Missing, Late, Graded, Submitted, Locked …).</summary>
    public static FrameworkElement StatusChip(string text, string? tone) => K.Chip(text, Tone(tone), strong: true);

    /// <summary>A piece of work in a list (the Mac's ARowView): its kind's symbol in the course's colour, its name, its
    /// facts, and where it stands only when that is news — Submitted, Missing, Late, a score, Excused. Plain work still to
    /// do has no chip (the engine sends none since 2.99.33; a "Not submitted" from an answer kept from before is let go):
    /// a column of the same grey pill hid the rows that do say something.</summary>
    public static FrameworkElement ARowView(ARow row, Color color) =>
        InfoRow(row.Title, row.Sub, Item(row.Kind ?? "assignment"), color,
            row.Status is { Word.Length: > 0 } s && !IsPlainToDo(s) ? StatusChip(s.Word, s.Kind) : null);

    /// <summary>The state of work merely not handed in yet: nothing to flag in a list of work.</summary>
    public static bool IsPlainToDo(Status s) => string.Equals(s.Word, "Not submitted", StringComparison.OrdinalIgnoreCase);

    /// <summary>A round picture or the initials of a person; a figure when there is no name (the Mac's PersonAvatar).</summary>
    public static FrameworkElement Avatar(string name, string? image, double size)
    {
        if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrEmpty(image)) return K.Avatar(name, null, image, size);
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(new Ellipse().Bg(B.StrongFill));
        g.Children.Add(K.Icon(SymbolRegular.Person24, size * 0.5, "TextFillColorInverseBrush", filled: true));
        return g;
    }

    /// <summary>An announcement or a discussion in a list (the Mac's PostRowView): who posted it, when, its first lines,
    /// its replies and how many are new. A Brightspace topic names its forum, not a person: "in Weekly Discussions", on
    /// the discussion's symbol in the course's colour — never the forum's initials, as if someone called that wrote it.</summary>
    public static FrameworkElement PostRowView(PostRow row, Color color, int previewLines = 2)
    {
        var unread = row.Unread == true;
        var forum = string.IsNullOrEmpty(row.Author) && row.Forum is { Length: > 0 } f ? $"in {f}" : null;
        var avatar = forum != null ? K.IconTile(SymbolRegular.ChatMultiple24, color, 34) : Avatar(row.Author ?? "", row.Avatar, 34);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        avatar.Margin = new Thickness(0, 2, 12, 0);
        var title = K.Wrapped(row.Title, Ty.Body, B.Text, 2);
        if (unread) title.FontWeight = FontWeights.SemiBold;
        FrameworkElement head = title;
        if (unread)
        {
            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 7, 0) }.Bg(B.Accent);
            System.Windows.Automation.AutomationProperties.SetName(dot, "Unread");
            head = K.Columns("auto, *", dot.Col(0), title.Col(1));
        }
        var words = K.V(3, head);
        var by = string.Join(" · ", new[] { forum ?? row.Author, row.When }.Where(s => !string.IsNullOrEmpty(s)));
        if (by.Length > 0) words.Children.Add(K.Text(by, Ty.Caption, B.Text2));
        if (!string.IsNullOrEmpty(row.Preview)) words.Children.Add(K.Wrapped(row.Preview!, Ty.Caption, B.Text2, previewLines));
        if (row.Graded == true || (row.Replies ?? 0) > 0 || (row.UnreadCount ?? 0) > 0)
        {
            var facts = K.H(10);
            if (row.Graded == true) facts.Children.Add(StatusChip("Graded", "purple"));
            if (row.Replies is int n && n > 0)
                facts.Children.Add(K.H(4, K.Icon(SymbolRegular.Chat24, 12, B.Text2), K.Text(n.ToString(CultureInfo.CurrentCulture), Ty.Caption, B.Text2)).Tip($"{n} {(n == 1 ? "reply" : "replies")}"));
            if (row.UnreadCount is int u && u > 0)
            {
                var t = K.Text($"{u} new", Ty.Caption).Strong();
                t.Foreground = Ink(color);
                t.VerticalAlignment = VerticalAlignment.Center;
                facts.Children.Add(t);
            }
            facts.Margin = new Thickness(0, 2, 0, 0);
            words.Children.Add(facts);
        }
        return K.Columns("auto, *", avatar.Col(0), words.Col(1));
    }

    /// <summary>A fact with its symbol at a home's head (a term, the teachers, how much is still to do): every line's
    /// words start together (the Mac's Fact).</summary>
    public static FrameworkElement Fact(SymbolRegular icon, string text, Color? tint = null)
    {
        var i = tint is { } c ? K.Icon(icon, 16, K.Darkened(c)) : K.Icon(icon, 16, B.Text2);
        i.Width = 20;
        i.VerticalAlignment = VerticalAlignment.Top;
        i.Margin = new Thickness(0, 2, 9, 0);
        var t = K.Wrapped(text, Ty.Body, B.Text2);
        if (tint is { } tc) t.Foreground = Ink(tc);
        return K.Columns("auto, *", i.Col(0), t.Col(1));
    }

    /// <summary>Content kept to a reading width at the page's left (the Mac's Page(maxWidth:)), narrower with the page.</summary>
    public static FrameworkElement Capped(FrameworkElement e, double max)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1000, GridUnitType.Star), MaxWidth = max });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(e.Col(0));
        return g;
    }

    // ---- two columns on a wide page ----

    /// <summary>A main column with a side column beside it once there is room for both (the side a set width, never more
    /// than 45% of the room), else the side under the main (the Mac's SideSplit).</summary>
    public static FrameworkElement SideSplit(FrameworkElement main, FrameworkElement side, double width, double sideWidth = 340, double from = 860, double gap = 24, double spacing = 22)
    {
        if (width < from) return K.V(spacing, main, side);
        var s = Math.Min(sideWidth, Math.Round(width * 0.45));
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(s) });
        main.VerticalAlignment = VerticalAlignment.Top;
        side.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(main.Col(0));
        g.Children.Add(side.Col(2));
        return g;
    }

    /// <summary>A screen's sections in two columns on a wide page, balanced, in their order (the first ones down the left);
    /// in one column on a narrow page, or when there is only one (the Mac's SectionColumns).</summary>
    public static FrameworkElement SectionColumns<T>(IReadOnlyList<T> items, Func<T, int> weight, Func<T, UIElement> content, double width, double from = 900)
    {
        if (width < from || items.Count < 2) return K.V(22, items.Select(content).ToArray());
        var (left, right) = BalancedSplit(items, weight);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = K.V(22, left.Select(content).ToArray());
        var r = K.V(22, right.Select(content).ToArray());
        l.VerticalAlignment = VerticalAlignment.Top;
        r.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(l.Col(0));
        g.Children.Add(r.Col(2));
        return g;
    }

    /// <summary>Sections cut into two columns where they balance best, their order kept (`weight`: how tall each one is
    /// likely to be).</summary>
    public static (List<T> Left, List<T> Right) BalancedSplit<T>(IReadOnlyList<T> items, Func<T, int> weight)
    {
        if (items.Count < 2) return (items.ToList(), new List<T>());
        var weights = items.Select(i => Math.Max(weight(i), 1)).ToList();
        var total = weights.Sum();
        int best = 1, bestGap = int.MaxValue, running = 0;
        for (var k = 1; k < items.Count; k++)
        {
            running += weights[k - 1];
            var gap = Math.Abs(total - 2 * running);
            if (gap < bestGap) { bestGap = gap; best = k; }
        }
        return (items.Take(best).ToList(), items.Skip(best).ToList());
    }

    // ---- menus ----

    public static MenuItem MenuEntry(string text, SymbolRegular icon, Action act, bool enabled = true)
    {
        var mi = new MenuItem { Header = text, Icon = new SymbolIcon { Symbol = icon }, IsEnabled = enabled };
        mi.Click += (_, _) => act();
        return mi;
    }

    /// <summary>A right-click menu on an element, its items made as it opens (so they say what is true then).</summary>
    public static void Menu(FrameworkElement e, Action<ItemCollection> fill)
    {
        var cm = new ContextMenu();
        e.ContextMenu = cm;
        e.ContextMenuOpening += (_, a) =>
        {
            cm.Items.Clear();
            fill(cm.Items);
            if (cm.Items.Count == 0) a.Handled = true; // (nothing to offer: no menu)
        };
    }

    /// <summary>The menu of a row that opens something (the Mac's canvasRowMenu): open it, the school's own page for it
    /// in a window of its own, or its link on the clipboard.</summary>
    public static void CanvasRowMenu(FrameworkElement e, Engine.Engine engine, string title, string? url, Action open) => Menu(e, items =>
    {
        items.Add(MenuEntry("Open", SymbolRegular.DocumentArrowRight24, open));
        if (string.IsNullOrEmpty(url)) return;
        items.Add(new Separator());
        items.Add(MenuEntry($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(url!, title)));
        items.Add(MenuEntry("Copy link", SymbolRegular.Link24, () => CopyLink(engine, url)));
    });

    /// <summary>The menu of a piece of work (the Mac's workMenu with WorkAction): open it; Hand in — Take quiz, Reply,
    /// Open for a quiz, a discussion, a tool — while it waits on you, See feedback once it is handed in or graded; the
    /// school's own page; its link.</summary>
    public static void WorkMenu(FrameworkElement e, Engine.Engine engine, ARow r) => Menu(e, items =>
    {
        if (string.IsNullOrEmpty(r.Url)) return;
        var k = r.Status?.Kind ?? "";
        var word = r.Status?.Word ?? "";
        var handedIn = k == "good" || word.StartsWith("Submitted", StringComparison.Ordinal);
        var open = k != "muted";
        items.Add(MenuEntry("Open", SymbolRegular.DocumentArrowRight24, () => engine.OpenWeb(r.Url, r.Title)));
        if (handedIn) items.Add(MenuEntry("See feedback", SymbolRegular.Comment24, () => engine.ActOn(r.Url, r.Title, true)));
        else if (open)
        {
            var (label, icon) = (r.Kind ?? "").ToLowerInvariant() switch
            {
                "quiz" => ("Take quiz", SymbolRegular.Quiz24),
                "discussion" or "discussion_topic" => ("Reply", SymbolRegular.Chat24),
                "tool" => ("Open", SymbolRegular.PuzzlePiece24),
                _ => ("Hand in", SymbolRegular.ArrowUpload24),
            };
            items.Add(MenuEntry(label, icon, () => engine.ActOn(r.Url, r.Title, false)));
        }
        items.Add(new Separator());
        items.Add(MenuEntry($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(r.Url, r.Title)));
        items.Add(MenuEntry("Copy link", SymbolRegular.Link24, () => CopyLink(engine, r.Url)));
    });

    /// <summary>An address of the school's on the clipboard, whole.</summary>
    public static void CopyLink(Engine.Engine engine, string? url)
    {
        if (engine.Absolute(url) is not { } u) return;
        try { Clipboard.SetText(u.ToString()); } catch { /* the clipboard busy: nothing copied */ }
    }

    /// <summary>The button beside a screen's title that opens the place on the school's own site, or copies its link
    /// (the Mac's CanvasMenu, "More": Open in Canvas, Copy link).</summary>
    public static FrameworkElement MoreButton(Engine.Engine engine, Func<string?> url, Func<string> title)
    {
        Button b = null!;
        b = K.IconButton(SymbolRegular.MoreHorizontal24, () =>
        {
            var u = url();
            var menu = new ContextMenu { PlacementTarget = b, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MinWidth = 200 };
            menu.Items.Add(MenuEntry($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => { if (u != null) engine.OpenWebScreen(u, title()); }, u != null));
            menu.Items.Add(MenuEntry("Copy link", SymbolRegular.Link24, () => CopyLink(engine, u), u != null));
            menu.IsOpen = true;
        }, "More");
        b.IsEnabled = url() != null;
        return b;
    }

    // ---- reading ----

    /// <summary>The school's rich text, its links going through the app (the school's own to its screens, another site
    /// to Simpl's browser window).</summary>
    public static RichText Rich(Engine.Engine engine, string html) => new(html, u => engine.GoTo(u), engine.BaseUrl);

    /// <summary>A line why it could not be read, or a spinner, while a screen's answer is not in.</summary>
    public static FrameworkElement Loading<T>(Loader<T> l, Action retry) where T : class => K.LoadState(l.Error, retry);
}
