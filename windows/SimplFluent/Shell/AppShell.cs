using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Shell;

/// <summary>
/// The app once the school is ready (the Mac's MainShell), laid out as Windows 11's Settings app: the navigation pane at
/// the left — the search box at its head; the places (Dashboard, To Do, Calendar, Grades, Notifications, Inbox, Tools,
/// External Tools), the courses (the one open with its sections under it), one Groups row that lists the groups and what
/// the school added to Canvas's own navigation (More from Canvas: History, its tools, Help); and at its foot Settings,
/// then who is signed in, where and as whom (the Mac's account bar, with the account menu) — and the page at the right:
/// its title (a breadcrumb once something is opened on it), its commands, and the screen. Below 880 wide the pane folds
/// away behind the title bar's button, and Search moves up into the title bar.
/// </summary>
public sealed partial class AppShell : Grid, IDisposable
{
    public const double PaneWidth = 304;
    public const double NarrowBelow = 880;

    private readonly Engine.Engine engine;
    private readonly MainWindow window;
    private readonly Grid pane = new();
    private readonly Border paneHolder = new();
    private readonly Grid page = new();
    private readonly NavList nav = new();
    /// <summary>The pane's foot: Settings, over who is signed in (Windows 11's NavigationView keeps Settings there).</summary>
    private readonly NavList footNav = new();
    private readonly ScrollViewer navScroll;
    private readonly Border foot;
    private readonly ContentControl paneScene = new() { Focusable = false, IsHitTestVisible = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    /// <summary>Where the pane's picture was last masked for: the foot of its rows and the top of its foot (MaskScene).</summary>
    private (double Rows, double Foot)? sceneMasked;
    private readonly ContentControl account = new() { Focusable = false };
    private readonly TextBox searchBox;
    /// <summary>Where the search box is while it is lent to the title bar (a narrow window's Search); null at the pane's head.</summary>
    private ContentControl? searchLent;
    private readonly SearchPalette palette;
    private readonly Grid header = new();
    private readonly ContentControl titleHost = new() { Focusable = false };
    // (a little room between the title and the first command, so a long title never runs into "Previous")
    private readonly ContentControl commandsHost = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly Border toast = new();
    private readonly TextBlock toastText;
    private readonly TranslateTransform toastShift = new();
    private bool toastShown;
    private int toastTurn;
    private readonly Border scrim = new() { Visibility = Visibility.Collapsed };
    /// <summary>Where a piece of work's quick look is drawn, over the page (WorkPreview).</summary>
    private readonly Canvas previewLayer = new() { ClipToBounds = true };
    private readonly HashSet<string> expanded = new();
    /// <summary>The row of the course (or the groups) open that its chevron folded away, and where the window was then:
    /// it stays folded until another place is opened.</summary>
    private (string Key, string? At)? folded;
    private readonly Dictionary<Place, Screen> placeScreens = new();
    private readonly Dictionary<string, Screen> routeScreens = new();
    /// <summary>When each kept screen was last shown (a count of showings), so the least lately shown are let go first.</summary>
    private readonly Dictionary<Screen, long> shownAt = new();
    private long showings;
    private readonly Prefetcher prefetch;
    /// <summary>The screen on show checked against the school while the window shows (Shell/LiveCheck.cs).</summary>
    private readonly LiveCheck live;
    private Screen? current;
    private bool narrow, paneOpen, sheetOpen;
    private int lastNav = -1;
    /// <summary>Who the account tile shows, as last drawn (null: to be drawn).</summary>
    private (string? Name, string? Initials, string? Avatar, string? Email, string Host, string? Site)? accountDrawn;
    /// <summary>The header as last drawn: the screen, its breadcrumb's words and line under it, its commands.</summary>
    private (Screen Screen, string Words, FrameworkElement? Commands)? headerDrawn;
    /// <summary>What the school added to Canvas's own navigation (More from Canvas), as last read: what was read last time
    /// until the page says.</summary>
    private MoreNav? more;
    private bool disposed;

    public bool Narrow => narrow;

    /// <summary>Where the page is — beside the pane, or the whole width on a narrow window — in <paramref name="to"/>'s
    /// terms: what a popup over the window is centred on (MainWindow.PopupRoom).</summary>
    public Rect PageArea(Visual to)
    {
        if (!page.IsLoaded || page.ActualWidth <= 0 || !to.IsAncestorOf(page)) return Rect.Empty;
        var at = page.TransformToAncestor(to).Transform(new Point(0, 0));
        return new Rect(at, new Size(page.ActualWidth, page.ActualHeight));
    }

    public AppShell(Engine.Engine engine, MainWindow window)
    {
        this.engine = engine;
        this.window = window;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PaneWidth) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ---- the pane ----
        searchBox = new TextBox
        {
            PlaceholderText = $"Search {engine.LmsName}", Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 }, IconPlacement = ElementPlacement.Right,
            ClearButtonEnabled = false, Margin = new Thickness(16, 4, 16, 12), MinHeight = 34,
        };
        // (its suggestions and commands as you type, the Mac's SearchPalette: Enter runs the one chosen, or searches)
        palette = SearchPalette.Attach(searchBox, engine, window);
        palette.Ran += () =>
        {
            if (narrow) ClosePane();
            // (lent to the title bar: once it has run, the box goes back to the pane and the keys to the screen)
            if (searchLent != null) { window.Focus(); ReturnSearch(); }
        };
        searchBox.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (searchLent != null && !searchBox.IsKeyboardFocusWithin && !palette.IsOpen) ReturnSearch();
        }, System.Windows.Threading.DispatcherPriority.Input);
        navScroll = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 8) };
        // (the Mac's sidebar: the search at the top, the places and courses; at the foot Settings — Windows 11's place
        // for it — and who is signed in, whose menu holds Settings too, as the Mac's account bar does)
        searchBox.Margin = SearchAtHead;
        footNav.Show(new[]
        {
            new NavEntry { Key = "settings", Title = "Settings", Icon = SymbolRegular.Settings24, Tint = Gray, Tip = "Settings (Ctrl+,)", Go = () => { if (narrow) ClosePane(); window.OpenSettings(); } },
        }, null);
        foot = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = K.V(2, footNav, account), Padding = new Thickness(0, 6, 0, 6) };
        foot.SetResourceReference(Border.BorderBrushProperty, B.Divider);
        pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pane.Children.Add(searchBox.Row(0));
        pane.Children.Add(navScroll.Row(1));
        pane.Children.Add(foot.Row(2));
        paneHolder.Child = new Grid { Children = { paneScene, pane } };
        // (the picture behind the pane's words kept faint where the rows are: masked again as they move)
        nav.SizeChanged += (_, _) => MaskScene();
        navScroll.ScrollChanged += (_, _) => MaskScene();
        foot.SizeChanged += (_, _) => MaskScene();
        paneHolder.SizeChanged += (_, _) => { MaskScene(); FootFits(); };
        pane.Mark(TourSpot.Pane);
        searchBox.Mark(TourSpot.Search);
        Children.Add(paneHolder.Col(0));

        // ---- the page: its header, then the screen ----
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(titleHost.Col(0));
        header.Children.Add(commandsHost.Col(1));
        header.Margin = new Thickness(40, 4, 40, 20);
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.Children.Add(header.Row(0));
        page.Children.Add(deck.Row(1));
        page.SizeChanged += (_, _) => { Widths(); window.PopupRoomMoved(); };
        Children.Add(page.Col(1));

        // a file on its way (FilePreview's word), at the foot of the page, as Windows 11's snackbar
        toastText = K.Text("", Ty.Body);
        toast.Child = K.H(12, K.Spinner(16), toastText);
        toast.Padding = new Thickness(16, 12, 20, 12);
        toast.CornerRadius = new CornerRadius(8);
        toast.HorizontalAlignment = HorizontalAlignment.Center;
        toast.VerticalAlignment = VerticalAlignment.Bottom;
        toast.Margin = new Thickness(0, 0, 0, 24);
        toast.Effect = K.Shadow(24, 6, 0.2);
        toast.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        toast.RenderTransform = toastShift;
        toast.Visibility = Visibility.Collapsed;
        Children.Add(toast.Col(1));
        Children.Add(previewLayer.Col(1));
        WorkPreview.Attach(previewLayer, engine);
        PreviewMouseDown += (_, e) => Pressed(e);
        FilePreview.Shared.PropertyChanged += OnFile;

        // the pane over the page while the window is narrow, and what dims the page behind it
        scrim.Background = K.Brush(Color.FromArgb(1, 0, 0, 0));
        scrim.MouseLeftButtonDown += (_, _) => ClosePane();
        Grid.SetColumnSpan(scrim, 2);
        Children.Add(scrim);

        engine.PropertyChanged += OnEngine;
        engine.PrepareRequested += OnPrepare;
        prefetch = new Prefetcher(engine, loc => ScreenAt(loc, make: false) != null, () => (ContentWidth, WindowActualWidth));
        live = new LiveCheck(engine, window, () => current, main: true);
        Updates();
        Navigate();
        paneScene.Content = Scenes.Backdrop("side");
        more = engine.Kept<MoreNav>("moreFromCanvas");
        BuildPane();
        _ = engine.LoadSidebar();
        _ = LoadMore();
        prefetch.Start();
        // (the screenshot suite: --place-window section:courses/101:grades opens that place in a window of its own, over
        // the main one, once it is up)
        // (or a page of the school's by its address — --place-window /courses/101/assignments/1012 — on its own screen)
        if (Launch.Take("", "place-window") is { Length: > 0 } apart)
        {
            if (Engine.Engine.PlaceNamed(apart) is { } elsewhere)
                Dispatcher.BeginInvoke(() => PlaceWindow.Open(engine, elsewhere), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            else if (apart.StartsWith('/') && engine.NativeRoute(apart, "") is { } page)
                Dispatcher.BeginInvoke(() => PlaceWindow.Open(engine, page), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>A screen is being read ahead now (a pointer resting on another waits its turn).</summary>
    private bool PrefetchBusy => prefetch.Busy;

    // ---- what the engine says ----

    private void OnEngine(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Engine.Engine.NavVersion): Navigate(); BuildPane(); break;
            case nameof(Engine.Engine.Courses) or nameof(Engine.Engine.Groups) or nameof(Engine.Engine.Sections) or nameof(Engine.Engine.Snapshot): BuildPane(); Header(); break;
            case nameof(Engine.Engine.DataVersion): current?.Refresh(); _ = LoadMore(); break;
            case nameof(Engine.Engine.Query): if (searchBox.Text != engine.Query && !searchBox.IsKeyboardFocusWithin) searchBox.Text = engine.Query; break;
        }
    }

    private void OnFile(object? sender, PropertyChangedEventArgs e)
    {
        var s = FilePreview.Shared.Status;
        if (s != null) toastText.Text = s; // (its words kept while it goes)
        var on = s != null;
        if (on == toastShown) return;
        toastShown = on;
        var turn = ++toastTurn;
        if (on && toast.Visibility == Visibility.Visible)
        {
            // (back while it was going: it turns round from where it is)
            Motion.To(toastShift, TranslateTransform.YProperty, 0, Motion.Normal, Motion.Decelerate);
            Motion.To(toast, OpacityProperty, 1, Motion.Normal, Motion.Decelerate);
        }
        else if (on)
        {
            toast.Visibility = Visibility.Visible;
            Motion.Enter(toast, 0, 24, 333);
        }
        else
        {
            // it goes the way it came, quicker: down a little, fading (Motion.Normal, decelerating), then folds away
            Motion.To(toastShift, TranslateTransform.YProperty, 12, Motion.Normal, Motion.Decelerate);
            Motion.To(toast, OpacityProperty, 0, Motion.Normal, Motion.Decelerate, 0, () =>
            {
                if (turn != toastTurn) return;
                toast.Visibility = Visibility.Collapsed;
                toast.BeginAnimation(OpacityProperty, null);
                toast.Opacity = 1;
                toastShift.BeginAnimation(TranslateTransform.YProperty, null);
                toastShift.Y = 0;
            });
        }
    }

    /// <summary>Light, dark or the accent changed: what is drawn with colours of its own is drawn again.</summary>
    public void Retheme()
    {
        paneScene.Content = Scenes.Backdrop("side");
        sceneMasked = null;
        MaskScene();
        accountDrawn = null;
        BuildPane(redraw: true);
        headerDrawn = null;
        Header();
        RestyleScreens();
    }

    // ---- the pane ----

    private static readonly Color Blue = Color.FromRgb(0x00, 0x78, 0xd4), Green = Color.FromRgb(0x10, 0x89, 0x3e), Red = Color.FromRgb(0xd1, 0x34, 0x38),
        Purple = Color.FromRgb(0x88, 0x64, 0xd8), Orange = Color.FromRgb(0xe3, 0x6c, 0x0a), Teal = Color.FromRgb(0x00, 0x99, 0xbc), Gray = Color.FromRgb(0x76, 0x76, 0x7c),
        Gold = Color.FromRgb(0xc1, 0x8a, 0x00), Orchid = Color.FromRgb(0xc2, 0x39, 0xb3);

    /// <summary>
    /// The pane brought up to date (after every step and every new count): who is signed in drawn again only when they
    /// changed (their picture is not fetched again), the rows only when what they draw changed — else only the choice
    /// moves. Its scene is drawn at start and with the look (<see cref="Retheme"/>). Redraw builds every row again.
    /// </summary>
    private void BuildPane(bool redraw = false)
    {
        var me = engine.Snapshot?.Me;
        var who = (me?.Name, me?.Initials, me?.Avatar, me?.Email, engine.Host, engine.Snapshot?.Site);
        if (accountDrawn is not { } drawn || !drawn.Equals(who))
        {
            accountDrawn = who;
            account.Content = AccountTile();
        }
        var snap = engine.Snapshot;
        // (a pointer resting on a row reads its screen; pressed, builds it: Kit.Ahead)
        Action<bool> Ready(Place p) => build => engine.Prepare(p, build);
        var notes = snap?.NotifUnread ?? 0;
        var mail = snap?.InboxUnread ?? 0;
        var list = new List<NavEntry>
        {
            new() { Key = "dashboard", Title = "Dashboard", Icon = SymbolRegular.Home24, Tint = Blue, Go = () => GoPlace(Place.Dashboard), Prepare = Ready(Place.Dashboard), Spot = TourSpot.DashboardRow },
            new() { Key = "todo", Title = "To Do", Icon = SymbolRegular.TaskListLtr24, Tint = Green, Go = () => GoPlace(Place.Todo), Prepare = Ready(Place.Todo) },
            new() { Key = "calendar", Title = "Calendar", Icon = SymbolRegular.CalendarLtr24, Tint = Red, Go = () => GoPlace(Place.Calendar), Prepare = Ready(Place.Calendar) },
            new() { Key = "grades", Title = "Grades", Icon = SymbolRegular.DataBarVertical24, Tint = Purple, Go = () => GoPlace(Place.Grades), Prepare = Ready(Place.Grades), Spot = TourSpot.GradesRow },
            new() { Key = "notifications", Title = "Notifications", Icon = SymbolRegular.Alert24, Tint = Orange, Badge = notes, BadgeLabel = notes == 1 ? "1 new notification" : $"{notes} new notifications", Go = () => GoPlace(Place.Notifications), Prepare = Ready(Place.Notifications) },
            new() { Key = "inbox", Title = "Inbox", Icon = SymbolRegular.Mail24, Tint = Teal, Badge = mail, BadgeLabel = mail == 1 ? "1 unread message" : $"{mail} unread messages", Go = () => GoPlace(Place.Inbox), Prepare = Ready(Place.Inbox) },
            new() { Key = "tools", Title = "Tools", Icon = SymbolRegular.Toolbox24, Tint = Gray, Go = () => GoPlace(Place.Tools), Prepare = Ready(Place.Tools), Spot = TourSpot.ToolsRow },
        };
        // (the course open: its row folds out, as the Mac's sidebar unfolds the one open — the chevron folds out any other)
        var openCtx = engine.Nav.Current.Place.Ctx;
        if (folded is { } f && f.At != openCtx) folded = null;
        // (the sections of a course folded out, asked for once the rows are shown: read once, and at once when kept)
        var toRead = new List<string>();
        // (the Mac's: right under Tools, every tool the school provides in the courses — Canvas's, so not on Brightspace)
        if (!engine.OnBrightspace)
            list.Add(new NavEntry { Key = "externaltools", Title = "External tools", Icon = SymbolRegular.PuzzlePiece24, Tint = Orchid, Go = () => GoPlace(Place.ExternalTools), Prepare = Ready(Place.ExternalTools) });
        var courses = engine.Courses;
        if (courses.Count > 0)
        {
            list.Add(new NavEntry { Header = true, Title = "Courses" });
            foreach (var c in courses)
            {
                var ctx = $"courses/{c.Id}";
                var label = string.IsNullOrEmpty(c.Code) ? c.Name : c.Code;
                var isOpen = expanded.Contains(ctx) || (openCtx == ctx && folded?.Key != ctx);
                if (isOpen) toRead.Add(ctx);
                // (its unread announcements, drawn as the Notifications badge is, saying what it counts)
                var unread = engine.CoursesLive ? c.Unread ?? 0 : 0;
                var entry = new NavEntry
                {
                    Key = ctx, Title = label, Square = K.Hex(c.Color, Blue), Letter = Initial(label),
                    Badge = unread,
                    BadgeLabel = !string.IsNullOrEmpty(c.UnreadLabel) ? c.UnreadLabel : unread == 1 ? "1 unread announcement" : $"{unread} unread announcements",
                    Go = () => GoPlace(Place.Home(ctx)),
                    Prepare = Ready(Place.Home(ctx)),
                    Expanded = isOpen,
                    // (the chevron folds the course open away too, until another place is opened)
                    Toggle = open =>
                    {
                        if (open) { expanded.Add(ctx); if (folded?.Key == ctx) folded = null; _ = engine.LoadSections(ctx); }
                        else { expanded.Remove(ctx); if (openCtx == ctx) folded = (ctx, openCtx); }
                        BuildPane();
                    },
                    Spot = c == courses[0] ? TourSpot.FirstCourse : null,
                    Hover = () => Tour.Did(TourEvent.CourseHover),
                };
                if (engine.Sections.TryGetValue(ctx, out var secs))
                    entry.Children = secs.Where(s => s.Kind != "home").Select(s => new NavEntry
                    {
                        Key = $"{ctx}:{s.Kind}", Title = s.Label, Icon = Glyph.Section(s.Kind), Tint = K.Hex(c.Color, Blue),
                        Go = () => GoPlace(Place.Section(ctx, s.Kind)),
                        Prepare = Ready(Place.Section(ctx, s.Kind)),
                    }).ToList();
                list.Add(entry);
            }
            list.Add(new NavEntry { Key = "courses", Title = "All courses", Icon = SymbolRegular.Library24, Tint = Gold, Go = () => GoPlace(Place.Courses), Prepare = Ready(Place.Courses) });
        }
        var groups = engine.Groups;
        if (groups.Count > 0 && !engine.OnBrightspace)
        {
            // (the Mac's: one Groups row that opens the Groups page, its chevron listing the groups under it — never
            //  each one pinned to the pane; the group open is listed under it as a course's section is)
            var groupOpen = groups.Any(g => openCtx == $"groups/{g.Id}");
            list.Add(new NavEntry
            {
                Key = "groups", Title = "Groups", Icon = SymbolRegular.People24, Tint = Teal, Folds = "groups",
                Go = () => GoPlace(Place.Groups), Prepare = Ready(Place.Groups),
                Expanded = expanded.Contains("groups") || (groupOpen && folded?.Key != "groups"),
                Toggle = open =>
                {
                    if (open) { expanded.Add("groups"); if (folded?.Key == "groups") folded = null; }
                    else { expanded.Remove("groups"); if (groupOpen) folded = ("groups", openCtx); }
                    BuildPane();
                },
                Children = groups.Select(g =>
                {
                    var ctx = $"groups/{g.Id}";
                    return new NavEntry { Key = ctx, Title = g.Name, Icon = SymbolRegular.PeopleTeam24, Tint = K.Hex(g.Color, Teal), Go = () => GoPlace(Place.Home(ctx)), Prepare = Ready(Place.Home(ctx)) };
                }).ToList(),
            });
        }
        // (what the school added to Canvas's own navigation — History, its tools, Help —, under Simpl's own places as the
        //  extension's sidebar has it; nothing when it adds nothing)
        list.AddRange(MoreFromCanvas.Entries(more, it => { if (narrow) ClosePane(); MoreFromCanvas.Open(engine, it, window); }));
        foreach (var e in list) Apart(e);
        if (redraw) nav.Set(list, SelectedKey(engine.Nav.Current.Place));
        else nav.Show(list, SelectedKey(engine.Nav.Current.Place));
        foreach (var ctx in toRead) _ = engine.LoadSections(ctx);
    }

    /// <summary>More from Canvas read again, at start and whenever what the screens show may have changed (quick: the
    /// page's menu is read as it is, a tool's picture drawn once); the pane's rows are built again only if what they show
    /// changed.</summary>
    private async Task LoadMore()
    {
        try
        {
            var read = await engine.Call<MoreNav>("moreFromCanvas");
            if (disposed) return;
            more = read;
            BuildPane();
            // (a picture run says what the section listed, for the screenshot suite's checks)
            if (Shot.Asked) Log.Write($"more from canvas: {string.Join(", ", read.Items.Select(i => i.Label + (MoreFromCanvas.Picture(i) != null ? " (picture)" : "")))} ({read.From})");
        }
        catch { /* the section stays as it was */ }
        MoreAsked();
    }

    /// <summary>The screenshot suite: --open more brings the section into sight once it is read; --open more:history,
    /// more:help, more:tool presses that item's row too, as a click does.</summary>
    private async void MoreAsked()
    {
        if (disposed || narrow || !MoreFromCanvas.Shown(more) || Launch.Take("more") is not { } asked) return;
        // (once the first screen is up and the pane has marked its row — which scrolls that row into sight —, as a
        //  student's press comes after; the section can be read before the first screen shows. And once WPF-UI's styles
        //  are in their one dictionary: put in place, they lay the pane out afresh at its top — on a slow runner that came
        //  just after the scroll, and the picture showed the pane's first rows. The picture waits for the scroll.)
        Shot.Began();
        try
        {
            for (var i = 0; i < 60 && current == null; i++) await Task.Delay(250);
            await Task.Delay(1200);
            for (var i = 0; i < 50 && FlatResources.Copying; i++) await Task.Delay(100);
            if (disposed) return;
            navScroll.ScrollToEnd();
            if (asked.TrimStart(':') is { Length: > 0 } kind && more!.Items.FirstOrDefault(i => i.Kind == kind) is { } it) nav.Press("more:" + it.Id);
        }
        finally { Shot.Ended(); }
    }

    /// <summary>A row's place in a window of its own (its menu's Open in new window, Shift+click): every place row, its
    /// sections too — the place read back from the row's key.</summary>
    private void Apart(NavEntry e)
    {
        if (!e.Header && !e.Separator && PlaceOfKey(e.Key) is { } place) e.OpenApart = () => PlaceWindow.Open(engine, place);
        foreach (var c in e.Children ?? new List<NavEntry>()) Apart(c);
    }

    /// <summary>The place a pane row's key names (SelectedKey backwards): "grades", "courses/101", "courses/101:assignments".</summary>
    private static Place? PlaceOfKey(string key)
    {
        if (key.Length == 0) return null;
        if (key.StartsWith("courses/") || key.StartsWith("groups/"))
        {
            var at = key.IndexOf(':');
            return at < 0 ? Place.Home(key) : Place.Section(key[..at], key[(at + 1)..]);
        }
        return Engine.Engine.PlaceNamed(key);
    }

    private static string Initial(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s ?? "", @"^(?:[A-Za-z]{1,3}\d{2,4}[-_ :]+)?(?<rest>.*)$");
        var rest = m.Success ? m.Groups["rest"].Value : s ?? "";
        var c = rest.FirstOrDefault(char.IsLetter);
        if (c == default) c = (s ?? "").FirstOrDefault(char.IsLetterOrDigit);
        return c != default ? char.ToUpperInvariant(c).ToString() : "•";
    }

    private static string? SelectedKey(Place p) => p.Kind switch
    {
        PlaceKind.Home => p.Ctx,
        PlaceKind.Section => $"{p.Ctx}:{p.Arg}",
        PlaceKind.Search => null,
        _ => p.Kind.ToString().ToLowerInvariant(),
    };

    /// <summary>A screen's line under its title with the parts the breadcrumb already says taken out (parts are " · "
    /// apart); null when nothing else is left.</summary>
    internal static string? WithoutCrumbs(string? subtitle, IEnumerable<string> crumbs)
    {
        if (string.IsNullOrEmpty(subtitle)) return subtitle;
        var said = crumbs.Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (said.Count == 0) return subtitle;
        var parts = subtitle.Split(" · ").Where(p => !said.Contains(p.Trim())).ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private void GoPlace(Place p)
    {
        if (engine.Nav.Current.Place == p && engine.Nav.Current.Path.Count > 0) engine.PopToRoot();
        else engine.Go(p);
        if (narrow) ClosePane();
    }

    /// <summary>The pane shorter than this (a 1024×681 window, a 1366×768 laptop) keeps its foot compact, so the courses
    /// keep their room: Settings a gear beside the account's ⋯ instead of a row of its own, and the email and the school
    /// on one line.</summary>
    private const double CompactFootBelow = 760;

    private bool footCompact;

    /// <summary>The foot as compact as the pane's height asks (drawn again only when that changes).</summary>
    private void FootFits()
    {
        var compact = paneHolder.ActualHeight > 0 && paneHolder.ActualHeight < CompactFootBelow;
        if (compact == footCompact) return;
        footCompact = compact;
        footNav.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (accountDrawn != null) account.Content = AccountTile();
    }

    /// <summary>Who is signed in, at the pane's foot (the Mac's account bar): their name, the email they sign in with, and
    /// the school they are signed in to — said once, here — and a click opens the account menu. In a short pane
    /// (<see cref="FootFits"/>), the email and the school on one line and Settings a gear beside the ⋯.</summary>
    private FrameworkElement AccountTile()
    {
        var me = engine.Snapshot?.Me;
        var name = me?.Name ?? "Signed in";
        var school = engine.Snapshot?.Site is { Length: > 0 } site ? site : engine.Host;
        var avatar = K.Avatar(name, me?.Initials, me?.Avatar, 36);
        var nameText = K.Text(name, Ty.BodyStrong);
        nameText.TextTrimming = TextTrimming.CharacterEllipsis;
        var words = K.V(0, nameText);
        var lines = new List<string>();
        if (me?.Email is { Length: > 0 } email) lines.Add(email);
        if (!lines.Contains(school, StringComparer.OrdinalIgnoreCase)) lines.Add(school);
        foreach (var line in footCompact ? new[] { string.Join(" · ", lines) } : lines.ToArray())
        {
            var t = K.Text(line, Ty.Caption, B.Text2);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            words.Children.Add(t);
        }
        words.VerticalAlignment = VerticalAlignment.Center;
        // (the ⋯ that says the tile opens a menu: what the tour's last step lights, alone — where Report a bug is)
        var more = new Border { Width = 28, Height = 28, Child = K.Icon(SymbolRegular.MoreHorizontal20, 16, B.Text2), VerticalAlignment = VerticalAlignment.Center };
        more.Mark(TourSpot.AccountMenu);
        var grid = K.Columns("auto, *, auto", avatar.Col(0), words.Margin(12, 0, 4, 0).Col(1), more.Col(2));
        Button? tile = null;
        tile = K.Subtle(grid, () => AccountMenu(tile!), new Thickness(10, 8, 10, 8), 6);
        tile.Margin = new Thickness(4, 0, 4, 0);
        tile.ToolTip = string.Join("\n", lines.Prepend(name));
        System.Windows.Automation.AutomationProperties.SetName(tile, $"{string.Join(", ", lines.Prepend(name))}, account menu");
        accountTile = tile;
        tile.Mark(TourSpot.Account);
        if (!footCompact) return tile;
        // (a short pane: Settings as a gear at the account's right, the row it had given back to the courses)
        var gear = K.IconButton(SymbolRegular.Settings24, () => { if (narrow) ClosePane(); window.OpenSettings(); }, "Settings (Ctrl+,)", 16, 36);
        gear.Height = 36;
        gear.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(gear, "Settings");
        tile.Margin = new Thickness(4, 0, 0, 0);
        return K.Columns("*, auto", tile.Col(0), gear.Margin(2, 0, 6, 0).Col(1));
    }

    private Button? accountTile;

    /// <summary>The account menu (the Mac's ⋯ by your name), over the account bar, in Windows 11's sentence case:
    /// Settings…, What's new, Take the tour, the school's profile, Report a bug…, Sign out…, and Quit Simpl (closing the
    /// window only puts Simpl in the notification area).</summary>
    private void AccountMenu(FrameworkElement at)
    {
        var menu = new ContextMenu { PlacementTarget = at, Placement = System.Windows.Controls.Primitives.PlacementMode.Top, MinWidth = 260 };
        MenuItem Item(string text, SymbolRegular icon, Action act)
        {
            var mi = new MenuItem { Header = text, Icon = new SymbolIcon { Symbol = icon } };
            mi.Click += (_, _) => act();
            return mi;
        }
        menu.Items.Add(Item("Settings…", SymbolRegular.Settings24, window.OpenSettings));
        menu.Items.Add(Item("What’s new", SymbolRegular.Sparkle24, () => engine.ShowWhatsNew(WhatsNew.All)));
        menu.Items.Add(Item("Take the tour", SymbolRegular.Map24, window.StartTour));
        menu.Items.Add(Item($"{engine.LmsName} profile", SymbolRegular.Person24, () => engine.OpenWebScreen("/profile", "Profile")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Report a bug…", SymbolRegular.Bug24, () => Engine.Engine.OpenExternally(new Uri("https://simplcourses.com/report/"))));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Sign out…", SymbolRegular.SignOut24, () => Dialogs.Confirm(window, $"Sign out of {engine.LmsName} on this PC?",
            $"Your {engine.LmsName} session and any saved sign-in are cleared; settings stay.", "Sign out", engine.SignOut)));
        menu.Items.Add(Item("Quit Simpl", SymbolRegular.Power24, EraseAll.Quit));
        menu.IsOpen = true;
    }

    /// <summary>A press anywhere in the window: the quick look and the screen's own panel fold back when it is outside them.</summary>
    public void Pressed(MouseButtonEventArgs e)
    {
        current?.Pressed(e);
    }

    /// <summary>Search (Ctrl+F): the box at the pane's head, or — the window too narrow for the pane — up in the title bar.</summary>
    public void FocusSearch()
    {
        if (narrow) { OpenSearchBar(); return; }
        searchBox.Focus();
        searchBox.SelectAll();
    }

    /// <summary>The search box's place at the pane's head.</summary>
    private static readonly Thickness SearchAtHead = new(16, 12, 16, 12);

    /// <summary>
    /// Search on a window too narrow for the pane (the title bar's Search, Ctrl+F, Ctrl+K): the pane's own search box
    /// lent to the title bar, its suggestions under it as ever, without the pane sliding over the page. It goes back to
    /// the pane's head once it is let go of (the keyboard elsewhere, a suggestion run), the pane opens or the window widens.
    /// </summary>
    public void OpenSearchBar()
    {
        if (!narrow) { FocusSearch(); return; }
        if (paneOpen) ClosePane();
        if (searchLent == null)
        {
            var slot = window.SearchSlot;
            pane.Children.Remove(searchBox);
            searchBox.Margin = new Thickness(0);
            slot.Content = searchBox;
            searchLent = slot;
            window.SearchMoved();
        }
        Dispatcher.BeginInvoke(() =>
        {
            if (searchLent == null) return;
            searchBox.Focus();
            Keyboard.Focus(searchBox);
            searchBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>The search box back at the pane's head, from the title bar.</summary>
    private void ReturnSearch()
    {
        if (searchLent is not { } slot) return;
        searchLent = null;
        slot.Content = null;
        searchBox.Margin = SearchAtHead;
        Grid.SetRow(searchBox, 0);
        pane.Children.Insert(0, searchBox);
        window.SearchMoved();
    }

    /// <summary>
    /// The pane's picture (a theme's scene, or a photo of the student's) kept faint behind the pane's words — at about a
    /// sixth of its strength behind the rows and the foot — and whole only in the room the rows leave empty above the
    /// foot, so its lines never cross a label (one mask, moved as the rows grow, fold or scroll).
    /// </summary>
    private void MaskScene()
    {
        if (paneScene.Content == null) { paneScene.OpacityMask = null; sceneMasked = null; return; }
        var h = paneHolder.ActualHeight;
        if (h < 1 || !paneScene.IsVisible) return;
        double rows, footTop;
        try
        {
            rows = Math.Min(nav.TranslatePoint(new Point(0, nav.ActualHeight), paneScene).Y, navScroll.TranslatePoint(new Point(0, navScroll.ActualHeight), paneScene).Y);
            footTop = foot.TranslatePoint(new Point(0, 0), paneScene).Y;
        }
        catch (InvalidOperationException) { return; }
        rows = Math.Clamp(Math.Round(rows), 0, h);
        footTop = Math.Clamp(Math.Round(footTop), rows, h);
        if (sceneMasked is { } was && was == (rows, footTop) && paneScene.OpacityMask != null) return;
        sceneMasked = (rows, footTop);
        const double faint = 0.15, ramp = 32;
        var mask = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, h) };
        void Stop(double y, double a) => mask.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(a * 255), 0, 0, 0), Math.Clamp(y / h, 0, 1)));
        Stop(0, faint);
        // (room enough under the rows for the picture to show: whole there, fading in below the last row and out above
        //  the foot; else faint all through)
        var start = rows + 8;
        if (footTop - start > 2 * ramp)
        {
            Stop(start, faint);
            Stop(start + ramp, 1);
            Stop(footTop - ramp / 2, 1);
            Stop(footTop, faint);
        }
        Stop(h, faint);
        mask.Freeze();
        paneScene.OpacityMask = mask;
    }

    // ---- the page ----

    /// <summary>The title (a breadcrumb: the place, then what is opened on it — each part but the last goes back to it),
    /// the line under it, and the screen's own commands.</summary>
    private void Header()
    {
        if (current == null) return;
        var loc = engine.Nav.Current;
        var crumbs = new List<(string Title, Action? Go)>();
        if (loc.Path.Count > 0)
        {
            var root = ScreenFactory.TitleOf(engine, loc.Place);
            crumbs.Add((root, () => engine.PopToRoot()));
            for (var i = 0; i < loc.Path.Count - 1; i++)
            {
                var n = i + 1;
                var r = loc.Path[i];
                var t = CrumbTitle(r);
                crumbs.Add((t, () => engine.SetPath(loc.Path.Take(n).ToList())));
            }
        }
        foreach (var (t, go) in current.Before) crumbs.Add((t, go));
        crumbs.Add((current.Title, null));
        // (the line under the title never names again what the breadcrumb already does: "Discussion", not "Discussion ·
        //  F26-MATH 021 20" under "F26-MATH 021 20 ›")
        var subtitle = WithoutCrumbs(current.Subtitle, crumbs.Take(crumbs.Count - 1).Select(c => c.Title));
        var commands = current.Commands;
        window.Title = current.Title == "Dashboard" ? "Simpl" : $"{current.Title} – Simpl";
        // (the same screen with the same words, the same steps under it and the same commands: drawn already — kept, its
        // buttons with it; a screen that makes its commands anew each time is drawn again each time)
        var words = string.Join("\u001f", crumbs.Select(c => (c.Go != null ? ">" : "") + c.Title).Concat(loc.Path.Select(r => r.ToString())).Append(subtitle ?? "").Append(current.TitleColor?.ToString() ?? ""));
        (Screen Screen, string Words, FrameworkElement? Commands) drawn = (current, words, commands);
        if (headerDrawn is { } was && was.Equals(drawn)) return;
        headerDrawn = drawn;
        // (one line, as Windows 11's BreadcrumbBar: steps that do not fit fold into "…" from the first, so the title never
        //  sits alone under a "›" — CrumbLine)
        var steps = new List<(FrameworkElement Step, string Title, Action Go)>();
        FrameworkElement? titleText = null;
        for (var i = 0; i < crumbs.Count; i++)
        {
            var (title, go) = crumbs[i];
            var last = i == crumbs.Count - 1;
            var t = last && current.TitleLines > 1 ? K.Wrapped(title, Ty.Title, B.Text, current.TitleLines) : K.Text(title, Ty.Title, last ? B.Text : B.Text2);
            if (last) TipWhenCut(t);
            if (go != null && !last)
            {
                var b = K.Subtle(t, go, new Thickness(2, 0, 2, 0));
                b.Cursor = Cursors.Hand;
                steps.Add((b, title, go));
            }
            else titleText = t;
        }
        // (a page of a course's: a square in its colour at the heading's start, as the Mac's headings have — the page's
        //  course at a glance, the colour of its card and its square in the pane — beside the title's first line, or,
        //  where a breadcrumb leads ("F26-MATH 021 20 › Grades"), before the breadcrumb, never between its parts)
        Border? square = current.TitleColor is { } tint
            ? new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), Background = K.Brush(tint), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 11, 10, 0) }
            : null;
        var line = new CrumbLine(square, steps, titleText ?? K.Text(current.Title, Ty.Title));
        // (a title that may wrap keeps the commands at the top right, beside its first line)
        commandsHost.VerticalAlignment = current.TitleLines > 1 ? VerticalAlignment.Top : VerticalAlignment.Center;
        var stack = K.V(2, line);
        if (subtitle is { Length: > 0 } sub) stack.Children.Add(K.Text(sub, Ty.Body, B.Text2));
        titleHost.Content = stack;
        commandsHost.Content = commands;
        HeaderWidth();
    }

    /// <summary>The title's whole words as its tooltip, while it is cut short (too long for the header beside the
    /// commands); none while it all shows.</summary>
    private static void TipWhenCut(TextBlock t)
    {
        t.SizeChanged += (_, _) =>
        {
            if (t.ActualWidth <= 0) return;
            var face = new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch);
            var full = new FormattedText(t.Text, System.Globalization.CultureInfo.CurrentUICulture, t.FlowDirection, face, t.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(t).PixelsPerDip);
            if (t.TextWrapping != TextWrapping.NoWrap) full.MaxTextWidth = t.ActualWidth;
            var cut = t.TextWrapping == TextWrapping.NoWrap ? full.Width > t.ActualWidth + 0.5 : full.Height > t.ActualHeight + 1;
            t.ToolTip = cut ? t.Text : null;
        };
    }

    private double ContentWidth => Math.Max(200, page.ActualWidth - 80);

    /// <summary>The window's width, the pane's and the page's together: what the Dashboard's counters follow.</summary>
    private double WindowActualWidth => window.ActualWidth;

    private void Widths()
    {
        PageWidths();
        header.Margin = page.ActualWidth < 560 ? new Thickness(20, 4, 20, 16) : new Thickness(40, 4, 40, 20);
        HeaderWidth();
    }

    /// <summary>The heading as wide as the screen's content where the screen keeps that to a width of its own, so the
    /// commands at its right line up with the content's right edge, not the window's.</summary>
    private void HeaderWidth()
    {
        // (a width, not a greatest width: a heading held to the left is otherwise only as wide as its words, and the
        //  commands would follow the title rather than end where the content does)
        var w = current?.HeaderWidth;
        header.MaxWidth = double.PositiveInfinity;
        if (w is { } x && x > 0)
        {
            var room = page.ActualWidth - header.Margin.Left - header.Margin.Right;
            header.Width = room > 0 ? Math.Min(x, room) : x;
            header.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            header.Width = double.NaN;
            header.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    /// <summary>The room under a page that scrolls with the shell.</summary>
    private double Foot => page.ActualWidth < 560 ? 32 : 40;

    public bool Escape()
    {
        if (palette.Escape()) return true;
        if (paneOpen) { ClosePane(); return true; }
        if (WorkPreview.IsOpen) { WorkPreview.Close(); return true; }
        return current?.Escape() == true;
    }

    /// <summary>A sheet over the window: a page's own web view (a window of its own) steps aside meanwhile.</summary>
    public void SheetOpen(bool open)
    {
        if (sheetOpen == open) return;
        sheetOpen = open;
        WebScreen.SheetOver(open);
    }

    // ---- narrow: the pane over the page ----

    public void WindowWidth(double w)
    {
        var n = w < NarrowBelow;
        if (n == narrow) return;
        if (!n) ReturnSearch();
        narrow = n;
        if (narrow)
        {
            ColumnDefinitions[0].Width = new GridLength(0);
            Grid.SetColumn(paneHolder, 0);
            Grid.SetColumnSpan(paneHolder, 2);
            paneHolder.HorizontalAlignment = HorizontalAlignment.Left;
            paneHolder.Width = PaneWidth;
            paneHolder.Visibility = Visibility.Collapsed;
            Grid.SetColumn(page, 0);
            Grid.SetColumnSpan(page, 2);
            Grid.SetColumn(toast, 0);
            Grid.SetColumnSpan(toast, 2);
            Grid.SetColumn(previewLayer, 0);
            Grid.SetColumnSpan(previewLayer, 2);
        }
        else
        {
            ClosePane();
            ColumnDefinitions[0].Width = new GridLength(PaneWidth);
            Grid.SetColumnSpan(paneHolder, 1);
            paneHolder.Width = double.NaN;
            paneHolder.Visibility = Visibility.Visible;
            paneHolder.Background = null;
            paneHolder.Effect = null;
            paneHolder.BorderThickness = new Thickness(0);
            Grid.SetColumn(page, 1);
            Grid.SetColumnSpan(page, 1);
            Grid.SetColumn(toast, 1);
            Grid.SetColumnSpan(toast, 1);
            Grid.SetColumn(previewLayer, 1);
            Grid.SetColumnSpan(previewLayer, 1);
        }
        Panel.SetZIndex(paneHolder, narrow ? 10 : 0);
        Panel.SetZIndex(scrim, 9);
    }

    public void TogglePane() { if (paneOpen) ClosePane(); else OpenPane(); }

    private void OpenPane()
    {
        if (!narrow) return;
        ReturnSearch();
        paneOpen = true;
        paneHolder.Visibility = Visibility.Visible;
        paneHolder.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        paneHolder.BorderThickness = new Thickness(0, 1, 1, 0);
        paneHolder.CornerRadius = new CornerRadius(0, 8, 0, 0);
        paneHolder.Effect = K.Shadow(32, 0, 0.28);
        scrim.Visibility = Visibility.Visible;
        WebScreen.SheetOver(true);
        var t = new TranslateTransform(-PaneWidth, 0);
        paneHolder.RenderTransform = t;
        Motion.FromTo(t, TranslateTransform.XProperty, -PaneWidth, 0, Motion.Medium, Motion.Decelerate);
    }

    private void ClosePane()
    {
        if (!paneOpen) return;
        paneOpen = false;
        scrim.Visibility = Visibility.Collapsed;
        WebScreen.SheetOver(sheetOpen);
        if (paneHolder.RenderTransform is TranslateTransform t)
            Motion.FromTo(t, TranslateTransform.XProperty, 0, -PaneWidth, Motion.Normal, Motion.Accelerate, 0, () => { if (!paneOpen && narrow) paneHolder.Visibility = Visibility.Collapsed; });
        else paneHolder.Visibility = Visibility.Collapsed;
    }

    private void Updates()
    {
        // (a picture asked for with the narrow window: the pane shown open)
        Loaded += async (_, _) =>
        {
            if (narrow && Launch.Flag("pane-open")) OpenPane();
            // (the screenshot suite: --search-bar has a narrow window's title bar hold the search box, as its Search
            //  button does — a moment before --open palette:… types into it)
            if (narrow && Launch.Flag("search-bar")) { await Task.Delay(2500); OpenSearchBar(); }
            // (the screenshot suite: --open menu shows the account menu once the window has settled)
            if (Launch.Get("open") == "menu") { await Task.Delay(3000); if (accountTile != null) AccountMenu(accountTile); }
        };
    }

    /// <summary>The search box's commands (Ctrl+K): on a narrow window, the box up in the title bar for them.</summary>
    public void OpenCommands()
    {
        if (narrow && !paneOpen) OpenSearchBar();
        palette.OpenCommands();
    }

    public void Dispose()
    {
        disposed = true;
        prefetch.Stop();
        live.Dispose();
        palette.Dispose();
        engine.PropertyChanged -= OnEngine;
        engine.PrepareRequested -= OnPrepare;
        FilePreview.Shared.PropertyChanged -= OnFile;
        foreach (var s in placeScreens.Values.Concat(routeScreens.Values).ToList()) Release(s);
        current = null;
    }
}
