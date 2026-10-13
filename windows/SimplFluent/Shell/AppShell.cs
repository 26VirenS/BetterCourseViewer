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
/// the left — the search box at its head, who is signed in at its foot (the Mac's account bar, with the account menu that holds Settings), the
/// places (Dashboard, To Do, Calendar, Grades, Notifications, Inbox, Tools, External Tools), the courses with their
/// sections and the groups, and Settings at its foot — and the page at the right: its title (a breadcrumb once something
/// is opened on it), its commands, and the screen. Below 880 wide the pane folds away behind the title bar's button.
/// </summary>
public sealed class AppShell : Grid, IDisposable
{
    public const double PaneWidth = 304;
    public const double NarrowBelow = 880;

    private readonly Engine.Engine engine;
    private readonly MainWindow window;
    private readonly Grid pane = new();
    private readonly Border paneHolder = new();
    private readonly Grid page = new();
    private readonly NavList nav = new();
    private readonly ContentControl paneScene = new() { Focusable = false, IsHitTestVisible = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly ContentControl account = new() { Focusable = false };
    private readonly TextBox searchBox;
    private readonly SearchPalette palette;
    private readonly Grid header = new();
    private readonly ContentControl titleHost = new() { Focusable = false };
    private readonly ContentControl commandsHost = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl body = new() { Focusable = false };
    private readonly ScrollViewer scroller;
    private readonly Border toast = new();
    private readonly TextBlock toastText;
    private readonly TranslateTransform toastShift = new();
    private bool toastShown;
    private int toastTurn;
    private readonly Border scrim = new() { Visibility = Visibility.Collapsed };
    /// <summary>Where a piece of work's quick look is drawn, over the page (WorkPreview).</summary>
    private readonly Canvas previewLayer = new() { ClipToBounds = true };
    private readonly HashSet<string> expanded = new();
    private readonly Dictionary<Place, Screen> placeScreens = new();
    private readonly Dictionary<string, Screen> routeScreens = new();
    /// <summary>When each kept screen was last shown (a count of showings), so the least lately shown are let go first.</summary>
    private readonly Dictionary<Screen, long> shownAt = new();
    private long showings;
    /// <summary>How many screens are kept to come back to: those pushed on a place, and courses' and groups' pages.</summary>
    private const int KeptRoutes = 12, KeptPages = 8;
    private Screen? current;
    private bool narrow, paneOpen, sheetOpen;
    private int lastNav = -1;
    /// <summary>Who the account tile shows, as last drawn (null: to be drawn).</summary>
    private (string? Name, string? Initials, string? Avatar, string? Email, string Host)? accountDrawn;
    /// <summary>The header as last drawn: the screen, its breadcrumb's words and line under it, its commands.</summary>
    private (Screen Screen, string Words, FrameworkElement? Commands)? headerDrawn;

    public bool Narrow => narrow;

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
        palette.Ran += () => { if (narrow) ClosePane(); };
        var navScroll = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 8) };
        // (the Mac's sidebar: the search at the top, the places and courses, and who is signed in at the foot — their
        // menu holds Settings, as the Mac's account bar does)
        searchBox.Margin = new Thickness(16, 12, 16, 12);
        var foot = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = account, Padding = new Thickness(0, 6, 0, 6) };
        foot.SetResourceReference(Border.BorderBrushProperty, B.Divider);
        pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pane.Children.Add(searchBox.Row(0));
        pane.Children.Add(navScroll.Row(1));
        pane.Children.Add(foot.Row(2));
        paneHolder.Child = new Grid { Children = { paneScene, pane } };
        pane.Mark(TourSpot.Pane);
        searchBox.Mark(TourSpot.Search);
        Children.Add(paneHolder.Col(0));

        // ---- the page: its header, then the screen ----
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(titleHost.Col(0));
        header.Children.Add(commandsHost.Col(1));
        header.Margin = new Thickness(40, 4, 40, 20);
        scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(40, 0, 40, 0) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.Children.Add(header.Row(0));
        page.Children.Add(body.Row(1));
        page.SizeChanged += (_, _) => Widths();
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
        scrim.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        scrim.MouseLeftButtonDown += (_, _) => ClosePane();
        Grid.SetColumnSpan(scrim, 2);
        Children.Add(scrim);

        engine.PropertyChanged += OnEngine;
        Updates();
        Navigate();
        paneScene.Content = Scenes.Backdrop("side");
        BuildPane();
        _ = engine.LoadSidebar();
    }

    // ---- what the engine says ----

    private void OnEngine(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Engine.Engine.NavVersion): Navigate(); BuildPane(); break;
            case nameof(Engine.Engine.Courses) or nameof(Engine.Engine.Groups) or nameof(Engine.Engine.Sections) or nameof(Engine.Engine.Snapshot): BuildPane(); Header(); break;
            case nameof(Engine.Engine.DataVersion): current?.Refresh(); break;
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
        accountDrawn = null;
        BuildPane(redraw: true);
        headerDrawn = null;
        Header();
        current?.Render();
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
        var who = (me?.Name, me?.Initials, me?.Avatar, me?.Email, engine.Host);
        if (accountDrawn is not { } drawn || !drawn.Equals(who))
        {
            accountDrawn = who;
            account.Content = AccountTile();
        }
        var snap = engine.Snapshot;
        var list = new List<NavEntry>
        {
            new() { Key = "dashboard", Title = "Dashboard", Icon = SymbolRegular.Home24, Tint = Blue, Go = () => GoPlace(Place.Dashboard), Spot = TourSpot.DashboardRow },
            new() { Key = "todo", Title = "To Do", Icon = SymbolRegular.TaskListLtr24, Tint = Green, Go = () => GoPlace(Place.Todo) },
            new() { Key = "calendar", Title = "Calendar", Icon = SymbolRegular.CalendarLtr24, Tint = Red, Go = () => GoPlace(Place.Calendar) },
            new() { Key = "grades", Title = "Grades", Icon = SymbolRegular.DataBarVertical24, Tint = Purple, Go = () => GoPlace(Place.Grades), Spot = TourSpot.GradesRow },
            new() { Key = "notifications", Title = "Notifications", Icon = SymbolRegular.Alert24, Tint = Orange, Badge = snap?.NotifUnread ?? 0, Go = () => GoPlace(Place.Notifications) },
            new() { Key = "inbox", Title = "Inbox", Icon = SymbolRegular.Mail24, Tint = Teal, Badge = snap?.InboxUnread ?? 0, Go = () => GoPlace(Place.Inbox) },
            new() { Key = "tools", Title = "Tools", Icon = SymbolRegular.Toolbox24, Tint = Gray, Go = () => GoPlace(Place.Tools), Spot = TourSpot.ToolsRow },
        };
        // (the Mac's: right under Tools, every tool the school provides in the courses — Canvas's, so not on Brightspace)
        if (!engine.OnBrightspace)
            list.Add(new NavEntry { Key = "externaltools", Title = "External Tools", Icon = SymbolRegular.PuzzlePiece24, Tint = Orchid, Go = () => GoPlace(Place.ExternalTools) });
        var courses = engine.Courses;
        if (courses.Count > 0)
        {
            list.Add(new NavEntry { Header = true, Title = "Courses" });
            foreach (var c in courses)
            {
                var ctx = $"courses/{c.Id}";
                var label = string.IsNullOrEmpty(c.Code) ? c.Name : c.Code;
                var entry = new NavEntry
                {
                    Key = ctx, Title = label, Square = K.Hex(c.Color, Blue), Letter = Initial(label),
                    Go = () => { expanded.Add(ctx); GoPlace(Place.Home(ctx)); },
                    Expanded = expanded.Contains(ctx),
                    Toggle = open => { if (open) { expanded.Add(ctx); _ = engine.LoadSections(ctx); } else expanded.Remove(ctx); BuildPane(); },
                    Spot = c == courses[0] ? TourSpot.FirstCourse : null,
                    Hover = () => Tour.Did(TourEvent.CourseHover),
                };
                if (engine.Sections.TryGetValue(ctx, out var secs))
                    entry.Children = secs.Where(s => s.Kind != "home").Select(s => new NavEntry
                    {
                        Key = $"{ctx}:{s.Kind}", Title = s.Label, Icon = Glyph.Section(s.Kind), Tint = K.Hex(c.Color, Blue),
                        Go = () => GoPlace(Place.Section(ctx, s.Kind)),
                    }).ToList();
                list.Add(entry);
            }
            list.Add(new NavEntry { Key = "courses", Title = "All Courses", Icon = SymbolRegular.Library24, Tint = Gold, Go = () => GoPlace(Place.Courses) });
        }
        var groups = engine.Groups;
        if (groups.Count > 0)
        {
            list.Add(new NavEntry { Header = true, Title = "Groups" });
            foreach (var g in groups)
            {
                var ctx = $"groups/{g.Id}";
                list.Add(new NavEntry { Key = ctx, Title = g.Name, Icon = SymbolRegular.People24, Tint = Teal, Go = () => GoPlace(Place.Home(ctx)) });
            }
        }
        if (redraw) nav.Set(list, SelectedKey(engine.Nav.Current.Place));
        else nav.Show(list, SelectedKey(engine.Nav.Current.Place));
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

    private void GoPlace(Place p)
    {
        if (engine.Nav.Current.Place == p && engine.Nav.Current.Path.Count > 0) engine.PopToRoot();
        else engine.Go(p);
        if (narrow) ClosePane();
    }

    /// <summary>Who is signed in, at the pane's foot (the Mac's account bar): a click opens the account menu, which holds
    /// Settings.</summary>
    private FrameworkElement AccountTile()
    {
        var me = engine.Snapshot?.Me;
        var name = me?.Name ?? "Signed in";
        var avatar = K.Avatar(name, me?.Initials, me?.Avatar, 36);
        var nameText = K.Text(name, Ty.BodyStrong);
        nameText.TextTrimming = TextTrimming.CharacterEllipsis;
        var mail = K.Text(me?.Email ?? engine.Host, Ty.Caption, B.Text2);
        mail.TextTrimming = TextTrimming.CharacterEllipsis;
        var words = K.V(0, nameText, mail);
        words.VerticalAlignment = VerticalAlignment.Center;
        var grid = K.Columns("auto, *, auto", avatar.Col(0), words.Margin(12, 0, 4, 0).Col(1), K.Icon(SymbolRegular.MoreHorizontal20, 16, B.Text2).Col(2));
        Button? tile = null;
        tile = K.Subtle(grid, () => AccountMenu(tile!), new Thickness(10, 8, 10, 8), 6);
        tile.Margin = new Thickness(4, 0, 4, 0);
        tile.ToolTip = "Your account";
        System.Windows.Automation.AutomationProperties.SetName(tile, $"{name}, account menu");
        accountTile = tile;
        tile.Mark(TourSpot.Account);
        return tile;
    }

    private Button? accountTile;

    /// <summary>The account menu (the Mac's ⋯ by your name), over the account bar: Settings…, What's New, the school's profile, Report a Bug…,
    /// Sign Out…, and Quit Simpl (closing the window only puts Simpl in the notification area).</summary>
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
        menu.Items.Add(Item("What’s New", SymbolRegular.Sparkle24, () => engine.ShowWhatsNew(WhatsNew.All)));
        menu.Items.Add(Item("Take the tour", SymbolRegular.Map24, window.StartTour));
        menu.Items.Add(Item($"{engine.LmsName} Profile", SymbolRegular.Person24, () => engine.OpenWebScreen("/profile", "Profile")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Report a Bug…", SymbolRegular.Bug24, () => Engine.Engine.OpenExternally(new Uri("https://simplcourses.com/report/"))));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Sign Out…", SymbolRegular.SignOut24, () => Dialogs.Confirm(window, $"Sign out of {engine.LmsName} on this PC?",
            $"Your {engine.LmsName} session and any saved sign-in are cleared; settings stay.", "Sign Out", engine.SignOut)));
        menu.Items.Add(Item("Quit Simpl", SymbolRegular.Power24, EraseAll.Quit));
        menu.IsOpen = true;
    }

    /// <summary>A press anywhere in the window: the quick look and the screen's own panel fold back when it is outside them.</summary>
    public void Pressed(MouseButtonEventArgs e)
    {
        WorkPreview.ClickedAt(e.GetPosition(previewLayer));
        current?.Pressed(e);
    }

    public void FocusSearch()
    {
        if (narrow && !paneOpen) OpenPane();
        searchBox.Focus();
        searchBox.SelectAll();
    }

    // ---- the page ----

    /// <summary>The place and what is pushed on it, shown: its screen (kept for the place, made anew for a pushed one),
    /// the header with its breadcrumb and commands.</summary>
    private void Navigate()
    {
        if (engine.NavVersion == lastNav && current != null) return;
        lastNav = engine.NavVersion;
        var loc = engine.Nav.Current;
        Screen next;
        if (loc.Path.Count > 0)
        {
            var r = loc.Path[^1];
            var key = r.ToString();
            if (!routeScreens.TryGetValue(key, out next!)) routeScreens[key] = next = ScreenFactory.For(engine, r);
        }
        else
        {
            var id = loc.Place.Identity;
            if (!placeScreens.TryGetValue(id, out next!)) placeScreens[id] = next = ScreenFactory.For(engine, loc.Place);
        }
        WorkPreview.Dismiss();
        if (next != current)
        {
            if (current != null) { current.Hide(); current.HeaderChanged -= Header; }
            current = next;
            current.HeaderChanged += Header;
            Mount(current);
        }
        current.SetWidth(ContentWidth);
        current.Show();
        shownAt[current] = ++showings;
        Trim(loc);
        Header();
        scroller.ScrollToTop();
    }

    /// <summary>
    /// The screens kept to come back to, bounded: past <see cref="KeptRoutes"/> pushed screens and <see cref="KeptPages"/>
    /// courses' and groups' pages, the least lately shown are let go (hidden already; opened again, they start afresh).
    /// Never the one showing, the place and the screens under it (Back), the app's own places, or Search.
    /// </summary>
    private void Trim(Location loc)
    {
        if (routeScreens.Count > KeptRoutes)
        {
            var under = loc.Path.Select(r => r.ToString()).ToHashSet();
            foreach (var key in Oldest(routeScreens, routeScreens.Count - KeptRoutes, k => !under.Contains(k)))
            {
                shownAt.Remove(routeScreens[key]);
                routeScreens.Remove(key);
            }
        }
        static bool Page(Place p) => p.Kind is PlaceKind.Home or PlaceKind.Section;
        var pages = placeScreens.Keys.Count(Page);
        if (pages > KeptPages)
        {
            var here = loc.Place.Identity;
            foreach (var key in Oldest(placeScreens, pages - KeptPages, k => Page(k) && k != here))
            {
                shownAt.Remove(placeScreens[key]);
                placeScreens.Remove(key);
            }
        }
    }

    /// <summary>Up to <paramref name="over"/> of the kept screens that may go, the least lately shown first (never the one showing).</summary>
    private List<TKey> Oldest<TKey>(Dictionary<TKey, Screen> kept, int over, Func<TKey, bool> may) where TKey : notnull =>
        kept.Where(kv => kv.Value != current && may(kv.Key))
            .OrderBy(kv => shownAt.GetValueOrDefault(kv.Value))
            .Take(over)
            .Select(kv => kv.Key)
            .ToList();

    private void Mount(Screen s)
    {
        scroller.Content = null;
        if (s.Scrolls)
        {
            var v = s.View;
            // (a reading width: as wide as the page up to it, from the left — sized, so it never shrinks to its words)
            if (s.MaxContentWidth is { } mw) { v.MaxWidth = mw; v.Width = Math.Min(mw, ContentWidth); v.HorizontalAlignment = HorizontalAlignment.Left; }
            else { v.ClearValue(FrameworkElement.WidthProperty); v.ClearValue(FrameworkElement.MaxWidthProperty); v.HorizontalAlignment = HorizontalAlignment.Stretch; }
            // (the room at the page's foot is the page's own, so it scrolls: a scroller's padding would stand still under it)
            v.Margin = new Thickness(0, 0, 0, Foot);
            scroller.Content = v;
            body.Content = scroller;
        }
        else
        {
            s.View.Margin = new Thickness(0);
            body.Content = s.View;
        }
    }

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
                var t = routeScreens.TryGetValue(r.ToString(), out var rs) ? rs.Title : ScreenFactory.TitleOf(r);
                crumbs.Add((t, () => engine.SetPath(loc.Path.Take(n).ToList())));
            }
        }
        foreach (var (t, go) in current.Before) crumbs.Add((t, go));
        crumbs.Add((current.Title, null));
        var subtitle = current.Subtitle;
        var commands = current.Commands;
        window.Title = current.Title == "Dashboard" ? "Simpl" : $"{current.Title} – Simpl";
        // (the same screen with the same words, the same steps under it and the same commands: drawn already — kept, its
        // buttons with it; a screen that makes its commands anew each time is drawn again each time)
        var words = string.Join("\u001f", crumbs.Select(c => (c.Go != null ? ">" : "") + c.Title).Concat(loc.Path.Select(r => r.ToString())).Append(subtitle ?? ""));
        (Screen Screen, string Words, FrameworkElement? Commands) drawn = (current, words, commands);
        if (headerDrawn is { } was && was.Equals(drawn)) return;
        headerDrawn = drawn;
        var line = new WrapPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < crumbs.Count; i++)
        {
            var (title, go) = crumbs[i];
            var last = i == crumbs.Count - 1;
            var t = K.Text(title, Ty.Title, last ? B.Text : B.Text2);
            if (go != null)
            {
                var b = K.Subtle(t, go, new Thickness(2, 0, 2, 0));
                b.Cursor = Cursors.Hand;
                line.Children.Add(b);
                line.Children.Add(K.Icon(SymbolRegular.ChevronRight20, 16, B.Text2).Margin(10, 6, 10, 0));
            }
            else line.Children.Add(t);
        }
        var stack = K.V(2, line);
        if (subtitle is { Length: > 0 } sub) stack.Children.Add(K.Text(sub, Ty.Body, B.Text2));
        titleHost.Content = stack;
        commandsHost.Content = commands;
    }

    private double ContentWidth => Math.Max(200, page.ActualWidth - 80);

    private void Widths()
    {
        if (current is { Scrolls: true, MaxContentWidth: { } mw }) current.View.Width = Math.Min(mw, ContentWidth);
        current?.SetWidth(ContentWidth);
        header.Margin = page.ActualWidth < 560 ? new Thickness(20, 4, 20, 16) : new Thickness(40, 4, 40, 20);
        scroller.Padding = page.ActualWidth < 560 ? new Thickness(20, 0, 20, 0) : new Thickness(40, 0, 40, 0);
        if (current is { Scrolls: true }) current.View.Margin = new Thickness(0, 0, 0, Foot);
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
        Simpl.Tools.GraphWeb.Cover(open);
    }

    // ---- narrow: the pane over the page ----

    public void WindowWidth(double w)
    {
        var n = w < NarrowBelow;
        if (n == narrow) return;
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
        paneOpen = true;
        paneHolder.Visibility = Visibility.Visible;
        paneHolder.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        paneHolder.BorderThickness = new Thickness(0, 1, 1, 0);
        paneHolder.CornerRadius = new CornerRadius(0, 8, 0, 0);
        paneHolder.Effect = K.Shadow(32, 0, 0.28);
        scrim.Visibility = Visibility.Visible;
        WebScreen.SheetOver(true);
        Simpl.Tools.GraphWeb.Cover(true);
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
        Simpl.Tools.GraphWeb.Cover(sheetOpen);
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
            // (the screenshot suite: --open menu shows the account menu once the window has settled)
            if (Launch.Get("open") == "menu") { await Task.Delay(3000); if (accountTile != null) AccountMenu(accountTile); }
        };
    }

    /// <summary>The search box's commands (Ctrl+K), the pane opened for it on a narrow window.</summary>
    public void OpenCommands()
    {
        if (narrow && !paneOpen) OpenPane();
        palette.OpenCommands();
    }

    public void Dispose()
    {
        palette.Dispose();
        engine.PropertyChanged -= OnEngine;
        FilePreview.Shared.PropertyChanged -= OnFile;
        current?.Hide();
    }
}
