using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

/// <summary>
/// (0.3.105) Simpl Community's tools, browsed (docs/community-tools.md, The apps): a popup over the window as a tool's
/// is (Shell/FloatPopup.cs — large, sized by its edges, the size kept), its heading Community's, then a search and the
/// five groups to show one of, and the gallery's tools as cards — each its symbol on a wash of its colour, its name, who
/// made it, its line, Add (Added once it is on the Tools page; Update when the gallery has a later version of it), and
/// under them the tool itself, working: a live preview drawn with the same controls it opens with. While the gallery is
/// read, a ring and a line; offline or refused, why and Try again; with nothing shared yet, or nothing that matches, so.
/// One that needs a newer Simpl is listed as such, with no Add.
/// </summary>
internal sealed class CommunityBrowser
{
    private static CommunityBrowser? open;

    private static readonly string[] Filters = new[] { "All" }.Concat(ToolFormat.Groups.Select(CommunityTools.GroupName)).ToArray();

    private readonly Engine.Engine engine;
    private readonly Sheet sheet;
    private readonly ContentControl content = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly MoreGrid grid = new(330, 16, Array.Empty<UIElement>(), stretch: true);
    private readonly TextBlock count = K.Text("", Ty.Body, B.Text2);
    private readonly SelectorBar groups;
    private readonly TextBox search;
    /// <summary>Each tool's card, made once a gallery read (its preview keeps what was put in it while the filter changes).</summary>
    private readonly Dictionary<string, (FrameworkElement Card, ContentControl Button, TextBlock Problem)> cards = new(StringComparer.Ordinal);
    private IReadOnlyList<CommunityTools.Entry>? cardsFrom;
    private CommunityTools.Phase drawnPhase = (CommunityTools.Phase)(-1);
    private string query = "";
    private string said = "";

    private CommunityBrowser(Engine.Engine engine, string? asked)
    {
        this.engine = engine;
        var at = asked is { Length: > 0 } a ? Array.FindIndex(Filters, f => string.Equals(f, a, StringComparison.OrdinalIgnoreCase)) : -1;
        groups = new SelectorBar(Filters, Math.Max(0, at));
        groups.Changed += _ => Draw();
        search = new TextBox { PlaceholderText = "Search tools", Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 }, MinWidth = 240, ClearButtonEnabled = true };
        if (asked is { Length: > 0 } && at < 0 && asked is not ("yes" or "true")) { search.Text = asked; query = asked; }
        search.TextChanged += (_, _) => { query = search.Text.Trim(); Draw(); };
        AutomationProperties.SetName(search, "Search Simpl Community’s tools");

        var close = K.IconButton(SymbolRegular.Dismiss24, () => sheet!.Close(), "Close Simpl Community (Esc)", 16, 36);
        var words = K.V(2, K.Text("Simpl Community", Ty.Title), K.Text("Tools students made. Add one to keep it on your Tools page — it works offline too.", Ty.Body, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var head = K.Columns("auto, *, auto", K.IconTile(SymbolRegular.PeopleCommunity24, Appearance.Accent, 44).Margin(0, 0, 14, 0).Col(0), words.Col(1), close.Col(2));
        close.VerticalAlignment = VerticalAlignment.Center;
        head.Margin = new Thickness(28, 16, 14, 12);
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(28, 0, 28, 8) };
        groups.Margin = new Thickness(-12, 0, 24, 8);
        search.Margin = new Thickness(0, 0, 0, 8);
        search.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(groups);
        bar.Children.Add(search);
        var page = K.Rows("auto, auto, *", head.Row(0), bar.Row(1), ToolKit.Edged(PageOf(content)).Row(2));
        page.Bg(B.Window);
        sheet = new Sheet { Floats = true, Large = true, Bare = true, OwnClose = true, Body = page, Name = "Simpl Community", SizeKey = "community-browser", Handle = head, Most = new Size(1320, 980) };
        sheet.Closed += () =>
        {
            CommunityTools.Changed -= OnChanged;
            if (ReferenceEquals(open, this)) open = null;
        };
        CommunityTools.Changed += OnChanged;
        Draw();
    }

    private static ScrollViewer PageOf(UIElement c) => ToolKit.Page(c, 1320);

    /// <summary>The browser over the window (the one open comes forward): <paramref name="asked"/> a group to show (the
    /// screenshot suite's --community-browse science) or words to search for.</summary>
    public static void Show(Engine.Engine engine, Window? owner, string? asked = null)
    {
        if (open != null) { open.sheet.Window?.Activate(); return; }
        owner ??= Application.Current?.MainWindow;
        if (owner == null) return;
        open = new CommunityBrowser(engine, asked);
        Shell.FloatPopup.Show(open.sheet, owner);
        if (Shot.Asked || Launch.IsDevelopment) Log.Write("community browser: open");
        _ = CommunityTools.Load();
    }

    private void OnChanged() => content.Dispatcher.BeginInvoke(() =>
    {
        // (only a tool's button and its problem line when the gallery is as it was: what was put in the previews stays)
        if (CommunityTools.State == CommunityTools.Phase.Ready && drawnPhase == CommunityTools.Phase.Ready && ReferenceEquals(cardsFrom, CommunityTools.Gallery))
        {
            foreach (var e in CommunityTools.Gallery) if (cards.TryGetValue(e.Id, out var c)) Dress(e, c.Button, c.Problem);
            return;
        }
        Draw();
    }, DispatcherPriority.Background);

    private void Draw()
    {
        drawnPhase = CommunityTools.State;
        // (the grid, its cards and the count are kept between draws: taken out of the stack that held them first)
        if (content.Content is Panel old) old.Children.Clear();
        switch (CommunityTools.State)
        {
            case CommunityTools.Phase.Idle or CommunityTools.Phase.Loading:
                var ring = K.V(14, K.Spinner(28), K.Text("Reading Simpl Community…", Ty.Body, B.Text2).Center());
                ring.HorizontalAlignment = HorizontalAlignment.Center;
                ring.VerticalAlignment = VerticalAlignment.Center;
                Set(new Grid { MinHeight = 360, Children = { ring } });
                return;
            case CommunityTools.Phase.Failed:
                var again = K.Button("Try again", () => _ = CommunityTools.Load(force: true), accent: true, icon: SymbolRegular.ArrowClockwise24);
                again.HorizontalAlignment = HorizontalAlignment.Center;
                Set(K.V(0, MoreKit.Unavailable(CommunityTools.Offline ? SymbolRegular.WifiOff24 : SymbolRegular.ErrorCircle24,
                    CommunityTools.Offline ? "You’re offline" : "Simpl Community didn’t answer", CommunityTools.Why, 300), again));
                Said("failed");
                return;
        }
        var all = CommunityTools.Gallery;
        if (all.Count == 0)
        {
            var site = K.Link("Simpl Community on the web", () => engine.OpenBrowser(new Uri(Community.Site, "community"), "Simpl Community"));
            site.HorizontalAlignment = HorizontalAlignment.Center;
            Set(K.V(0, MoreKit.Unavailable(SymbolRegular.PeopleCommunity24, "No tools shared yet", "Tools students make show here once they’ve been looked at. Make one on the web.", 300), site));
            Said("empty");
            return;
        }
        if (!ReferenceEquals(cardsFrom, all))
        {
            cards.Clear();
            cardsFrom = all;
        }
        var group = groups.Selected == 0 ? null : ToolFormat.Groups[groups.Selected - 1];
        var shown = all.Where(e => (group == null || e.Group == group) && Matches(e, query)).ToList();
        grid.Children.Clear();
        foreach (var e in shown)
        {
            if (!cards.TryGetValue(e.Id, out var c)) cards[e.Id] = c = Card(e);
            grid.Children.Add(c.Card);
        }
        var where = group == null ? "" : $" in {CommunityTools.GroupName(group)}";
        count.Text = shown.Count == 1 ? $"1 tool{where}" : $"{shown.Count} tools{where}";
        var safe = MoreKit.Note(SymbolRegular.ShieldCheckmark24, "A Community tool is only numbers and formulas: it can’t reach your files, your school or the internet.");
        if (shown.Count == 0)
            Set(K.V(0, MoreKit.Unavailable(SymbolRegular.Search24, "No tools match", query.Length > 0 ? $"Nothing{where} matches “{query}”. Try another word." : $"No tools{where} yet.", 300)));
        else
            Set(K.V(16, count, grid, safe));
        Said($"{shown.Count} tools{(group != null ? $" ({group})" : "")}{(query.Length > 0 ? $" for “{query}”" : "")}");
    }

    private void Set(FrameworkElement e)
    {
        content.Content = e;
        Shot.Moved();
    }

    private void Said(string now)
    {
        if (now == said) return;
        said = now;
        if (Shot.Asked || Launch.IsDevelopment) Log.Write($"community browser: shows {now}");
    }

    private static bool Matches(CommunityTools.Entry e, string q) => q.Length == 0
        || e.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || e.Summary.Contains(q, StringComparison.CurrentCultureIgnoreCase)
        || e.Maker.Contains(q, StringComparison.CurrentCultureIgnoreCase) || CommunityTools.GroupName(e.Group).Contains(q, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>A tool's card: its tile, name, maker and group, Add; its line; why it could not be added; the tool working.</summary>
    private (FrameworkElement Card, ContentControl Button, TextBlock Problem) Card(CommunityTools.Entry e)
    {
        var colour = K.Hex(e.Tool?.Color, Color.FromRgb(0x8a, 0x8a, 0x8a));
        var symbol = e.Tool != null ? CommunityTools.SymbolOf(e.Tool.Symbol) : SymbolRegular.Apps24;
        var name = K.Wrapped(e.Name, Ty.BodyStrong, maxLines: 2);
        var about = string.Join(" · ", new[] { e.Maker.Length > 0 ? $"by {e.Maker}" : null, CommunityTools.GroupName(e.Group) }.Where(s => !string.IsNullOrEmpty(s)));
        var words = K.V(2, name, K.Text(about, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var button = new ContentControl { Focusable = false, VerticalAlignment = VerticalAlignment.Top };
        var problem = K.Wrapped("", Ty.Caption, B.Caution);
        problem.Visibility = Visibility.Collapsed;
        var head = K.Columns("auto, *, auto", K.IconTile(symbol, colour, 40).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Margin(0, 0, 12, 0).Col(0), words.Col(1), button.Margin(10, 0, 0, 0).Col(2));
        var v = K.V(12, head, K.Wrapped(e.Summary, Ty.Body, B.Text2), problem);
        if (e.Tool != null)
        {
            var preview = new CommunityToolView(e.Tool, preview: true);
            var well = new Border { Child = preview, Padding = new Thickness(0, 14, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0) };
            well.SetResourceReference(Border.BorderBrushProperty, B.Divider);
            v.Children.Add(well);
        }
        else v.Children.Add(MoreKit.Note(SymbolRegular.Info24, "This tool needs a newer Simpl. Update Simpl to add it."));
        Dress(e, button, problem);
        var card = K.Card(v, new Thickness(16));
        AutomationProperties.SetName(card, $"{e.Name}. {e.Summary}");
        return (card, button, problem);
    }

    /// <summary>A card's button as things stand: Add, Adding…, Added, Update — or none, for a tool this Simpl cannot use.</summary>
    private void Dress(CommunityTools.Entry e, ContentControl host, TextBlock problem)
    {
        var why = CommunityTools.Problems.TryGetValue(e.Id, out var p) ? $"Couldn’t add it: {p}" : "";
        problem.Text = why;
        problem.Visibility = why.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (e.Tool == null)
        {
            host.Content = K.Chip("Needs a newer Simpl", K.Tone("orange"));
            return;
        }
        var busy = CommunityTools.Busy;
        var have = CommunityTools.Has(e.Id);
        var newer = CommunityTools.Newer(e);
        Wpf.Ui.Controls.Button b;
        if (busy == e.Id)
        {
            b = K.Button(have ? "Updating…" : "Adding…", null);
            b.IsEnabled = false;
            b.Icon = null;
            b.Content = K.H(8, K.Spinner(14), K.Text(have ? "Updating…" : "Adding…", Ty.Body));
        }
        else if (have && !newer)
        {
            b = K.Button("Added", null, icon: SymbolRegular.Checkmark24);
            b.IsEnabled = false;
            b.ToolTip = $"{e.Name} is on your Tools page";
        }
        else
        {
            b = K.Button(newer ? "Update" : "Add", () => _ = Add(e), accent: true, icon: newer ? SymbolRegular.ArrowSync24 : SymbolRegular.Add24);
            b.IsEnabled = busy == null;
            b.ToolTip = newer ? $"Take the newest version of {e.Name}" : $"Keep {e.Name} on your Tools page";
        }
        b.MinWidth = 0;
        AutomationProperties.SetName(b, have && !newer ? $"{e.Name}, added" : $"{(newer ? "Update" : "Add")} {e.Name}");
        host.Content = b;
    }

    private static async Task Add(CommunityTools.Entry e)
    {
        if (await CommunityTools.Add(e.Id)) Shot.Moved();
    }
}
