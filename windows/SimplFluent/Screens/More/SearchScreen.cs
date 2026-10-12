using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// Search, from the search box (the Mac's SearchView): its sources — your courses, their work, announcements, pages,
/// discussions and files, and people — each a section of the page under its own heading, its results one card of rows,
/// in as many columns as the page holds. Chips across the top narrow the page to one kind; the commands whose names the
/// words start sit over the results. Searched again for new words after a pause (what is shown stays, dimmed, until the
/// new answer is in). The window keeps one Search screen for every search: the words are the place's.
/// </summary>
public sealed class SearchScreen : Screen
{
    private string query;
    private string? asked;
    private string answered = "";
    private SearchData? data;
    private bool searching, stale = true, fresh;
    private int generation;
    /// <summary>One kind of result alone (its chip pressed); null for all of them.</summary>
    private string? only;

    public SearchScreen(Engine.Engine engine, string query) : base(engine) => this.query = (query ?? "").Trim();

    private string Q => query;

    public override string Title => Q.Length == 0 ? "Search" : $"Results for “{Q}”";

    public override string? Subtitle
    {
        get
        {
            if (data == null || Q.Length == 0) return null;
            var n = data.Groups.Sum(g => g.Rows.Count);
            if (n == 0) return null;
            var kinds = data.Groups.Count;
            return $"{n} {(n == 1 ? "result" : "results")}" + (kinds > 1 ? $" in {kinds} kinds" : "");
        }
    }

    /// <summary>Shown (first, again after Back, or for new words): the words are the place's now, searched.</summary>
    public override void Refresh()
    {
        var at = engine.Nav.Current;
        if (at.Place.Kind == PlaceKind.Search && at.Path.Count == 0) query = (at.Place.Arg ?? "").Trim();
        // (the Mac searches when the words change and each time the screen comes back, not when other data changes)
        if (Q == asked && !stale) return;
        stale = false;
        asked = Q;
        HeaderChange();
        Run(Q);
    }

    public override void Hide()
    {
        base.Hide();
        stale = true;
    }

    /// <summary>The search for the words, after a pause when something is shown already (newer words win).</summary>
    private async void Run(string q)
    {
        var gen = ++generation;
        if (q.Length == 0)
        {
            data = null;
            searching = false;
            Render();
            return;
        }
        if (data != null)
        {
            await Task.Delay(280);
            if (gen != generation) return;
        }
        searching = true;
        Render();
        SearchData? d = null;
        try { d = await engine.Call<SearchData>("search", new { q }); } catch { /* nothing found is said as no results */ }
        if (gen != generation) return;
        searching = false;
        answered = q;
        data = d ?? new SearchData();
        fresh = true;
        HeaderChange();
        Render();
    }

    protected override FrameworkElement Build()
    {
        if (Q.Length == 0) return Start();
        if (data is { } d0 && d0.Groups.Count == 0 && !searching)
            return K.V(26, Actions(Q), MoreKit.Unavailable(SymbolRegular.Search24, $"No Results for “{answered}”", "Check the spelling or try a new search.", 240));
        if (data is { } d) return Results(d);
        return new Grid { MinHeight = 200, Children = { K.Spinner(32) } };
    }

    // ---- the results ----

    private FrameworkElement Results(SearchData d)
    {
        // (a kind chosen that this answer no longer has: all of them again)
        var kept = only != null && d.Groups.Any(g => g.Title == only) ? only : null;
        var shown = kept == null ? d.Groups : d.Groups.Where(g => g.Title == kept).ToList();
        var page = new Stack { Orientation = Orientation.Vertical, Spacing = 26 };
        if (d.Groups.Count > 1) page.Children.Add(Kinds(d, kept));
        if (Actions(Q) is { } actions) page.Children.Add(actions);
        var columns = new SearchColumns();
        foreach (var g in shown) columns.Children.Add(Group(g));
        page.Children.Add(columns);
        if (searching) Motion.FromTo(page, UIElement.OpacityProperty, 1, 0.55, 150, Motion.Decelerate);
        else if (fresh)
        {
            fresh = false;
            Motion.Stagger(columns.Children.OfType<UIElement>(), 40, 20);
        }
        return page;
    }

    /// <summary>The kinds found, as chips: one pressed shows it alone (pressed again, or All, every kind).</summary>
    private FrameworkElement Kinds(SearchData d, string? kept)
    {
        var total = d.Groups.Sum(g => g.Rows.Count);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(KindChip("All", total, SymbolRegular.Grid24, kept == null, () => Choose(null)));
        foreach (var g in d.Groups)
        {
            var title = g.Title;
            row.Children.Add(KindChip(title, g.Rows.Count, SearchGlyph(title), kept == title, () => Choose(kept == title ? null : title)));
        }
        return row;
    }

    private void Choose(string? kind)
    {
        only = kind;
        fresh = true;
        Render();
    }

    /// <summary>A kind as a chip, its count beside its name; the accent's fill while it is the one shown.</summary>
    private static FrameworkElement KindChip(string title, int count, SymbolRegular symbol, bool on, Action press)
    {
        var fore = on ? B.OnAccent : B.Text;
        var count0 = K.Text(count.ToString(), Ty.Body, fore);
        count0.Opacity = 0.7;
        var content = K.H(7, K.Icon(symbol, 14, fore, filled: true), K.Text(title, Ty.Body, fore), count0);
        var b = new Wpf.Ui.Controls.Button
        {
            Content = content, Appearance = on ? ControlAppearance.Primary : ControlAppearance.Secondary,
            CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 6, 14, 7), Margin = new Thickness(0, 0, 8, 8),
        };
        b.Click += (_, _) => press();
        System.Windows.Automation.AutomationProperties.SetName(b, $"{title}, {count}");
        return b;
    }

    /// <summary>One kind of result: its heading on the page, its rows in one card.</summary>
    private FrameworkElement Group(SearchGroup g)
    {
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        var symbol = SearchGlyph(g.Title);
        for (var i = 0; i < g.Rows.Count; i++)
        {
            if (i > 0) rows.Children.Add(K.Divider().Margin(58, 0, 8, 0));
            rows.Children.Add(Row(g.Rows[i], symbol));
        }
        return K.V(0, Heading.Section(g.Title, g.Rows.Count.ToString()), K.Card(rows, new Thickness(6)));
    }

    /// <summary>A result: its kind's symbol in its course's colour, its title and the line under it, and where it opens.</summary>
    private FrameworkElement Row(SearchRow row, SymbolRegular symbol)
    {
        var external = row.External == true;
        var title = K.Wrapped(row.Title, Ty.Body, B.Text, 2);
        title.FontWeight = FontWeights.Medium;
        var words = K.V(3, title);
        if (!string.IsNullOrEmpty(row.Sub)) words.Children.Add(K.Text(row.Sub!, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        FrameworkElement trailing = external
            ? K.Icon(SymbolRegular.Open24, 16, B.Text3).Tip("Opens in a browser window")
            : K.Icon(SymbolRegular.ChevronRight20, 12, B.Text3);
        var color = row.Color is { Length: > 0 } ? K.Hex(row.Color) : Appearance.Accent;
        var g = K.Columns("auto, *, auto", K.IconTile(symbol, color, 36).Margin(0, 0, 14, 0).Col(0), words.Col(1), trailing.Margin(6, 0, 4, 0).Col(2));
        var r = row;
        void Open() => engine.OpenFromSearch(r.Url, r.Title, external);
        var b = K.Subtle(g, Open, new Thickness(8, 9, 8, 9), 6);
        var menu = new List<(string, SymbolRegular, Action)?> { ("Open", SymbolRegular.Open24, Open) };
        if (!external) menu.Add(($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(r.Url, r.Title)));
        menu.Add(("Copy Link", SymbolRegular.Link24, () => MoreKit.Copy(engine.Absolute(r.Url)?.ToString())));
        b.ContextMenu = MoreKit.Menu(menu.ToArray());
        return b;
    }

    /// <summary>The symbol for a kind of result (the Mac's SearchGlyph).</summary>
    private static SymbolRegular SearchGlyph(string group) => group switch
    {
        "Courses" => SymbolRegular.Library24,
        "Assignments" => SymbolRegular.DocumentText24,
        "Announcements" => SymbolRegular.Megaphone24,
        "Pages" => SymbolRegular.DocumentOnePage24,
        "Discussions" => SymbolRegular.ChatMultiple24,
        "Files" => SymbolRegular.Folder24,
        "People" => SymbolRegular.PersonCircle24,
        _ => SymbolRegular.Search24,
    };

    // ---- the commands ----

    /// <summary>The commands whose names the words start, over the results (none for words that start none) — the search
    /// box's own (the Mac's SearchPalette.commandItems), each a press away.</summary>
    private FrameworkElement? Actions(string words)
    {
        // (one that takes something gets the words with it: "Discussions" puts "/discussion dis" in the box, its list)
        var items = Shell.SearchPalette.CommandItems(words, engine)
            .Select(i => i.Action.Do == Shell.PaletteDo.Fill ? i with { Action = Shell.PaletteAction.Fill(i.Action.Text + words) } : i).ToList();
        if (items.Count == 0) return null;
        return K.V(0, Heading.Section("Commands"), Pills(items));
    }

    /// <summary>Nothing typed yet: what Search finds, and every command, each a press away.</summary>
    private FrameworkElement Start()
    {
        var intro = K.Wrapped($"Find courses, assignments, quizzes, announcements, pages, discussions, files and people in {engine.LmsName} — or type / in the search box for a command.", Ty.Body, B.Text2);
        return K.V(26, intro, K.V(0, Heading.Section("Commands"), Pills(Shell.SearchPalette.CommandItems("", engine))));
    }

    private static FrameworkElement Pills(IReadOnlyList<Shell.PaletteItem> items) =>
        new MoreGrid(230, 10, items.Select(Shell.SearchPalette.CommandPill), stretch: true);
}

/// <summary>
/// The kinds of result in as many columns as the width holds — one under about 900 wide, up to three on a wide page —
/// each kind going to the column that is shortest so far, so the columns end near each other (the Mac's ResultColumns).
/// </summary>
internal sealed class SearchColumns : Panel
{
    private const double MinColumn = 420, Spacing = 28, RowSpacing = 30;
    private const int MaxColumns = 3;

    private List<Rect> Frames(double width)
    {
        if (double.IsInfinity(width) || width <= 0) width = 900;
        var count = InternalChildren.Count;
        var fit = (int)((width + Spacing) / (MinColumn + Spacing));
        var n = Math.Max(1, Math.Min(MaxColumns, Math.Min(fit, Math.Max(count, 1))));
        var cw = (width - Spacing * (n - 1)) / n;
        var heights = new double[n];
        var used = new bool[n];
        var frames = new List<Rect>();
        foreach (UIElement s in InternalChildren)
        {
            var h = s.DesiredSize.Height;
            var c = 0;
            for (var k = 1; k < n; k++) if (heights[k] < heights[c]) c = k;
            var y = used[c] ? heights[c] + RowSpacing : 0;
            frames.Add(new Rect(c * (cw + Spacing), y, cw, h));
            heights[c] = y + h;
            used[c] = true;
        }
        return frames;
    }

    private double ColumnWidth(double width)
    {
        if (double.IsInfinity(width) || width <= 0) width = 900;
        var fit = (int)((width + Spacing) / (MinColumn + Spacing));
        var n = Math.Max(1, Math.Min(MaxColumns, Math.Min(fit, Math.Max(InternalChildren.Count, 1))));
        return (width - Spacing * (n - 1)) / n;
    }

    protected override Size MeasureOverride(Size available)
    {
        var cw = ColumnWidth(available.Width);
        foreach (UIElement s in InternalChildren) s.Measure(new Size(cw, double.PositiveInfinity));
        var frames = Frames(available.Width);
        var h = frames.Count == 0 ? 0 : frames.Max(f => f.Bottom);
        return new Size(double.IsInfinity(available.Width) ? cw : available.Width, h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var frames = Frames(final.Width);
        for (var i = 0; i < frames.Count; i++) InternalChildren[i].Arrange(frames[i]);
        return final;
    }
}
