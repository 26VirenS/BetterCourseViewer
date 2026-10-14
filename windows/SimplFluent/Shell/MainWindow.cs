using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.SignIn;
using Simpl.Support;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using Image = System.Windows.Controls.Image;

namespace Simpl.Shell;

/// <summary>
/// The window (the Mac's main window), in Windows 11's clothes: Mica behind it, rounded corners, the title bar Windows
/// 11's apps have (Back at its left, the app's name, the caption buttons with snap layouts). Under the title bar, what
/// the moment calls for, as on the Mac: finding the school, the school confirmed, the sign-in card or "Logging you in",
/// the school's own page (while something there is the student's to do), or — signed in — the app itself (AppShell).
/// The school's site runs underneath in the engine's web view all along. Closing the window keeps Simpl running in the
/// notification area (as the Mac's does in the Dock), unless that is turned off in Settings.
/// </summary>
public sealed class MainWindow : FluentWindow, ISheetWindow
{
    public const double TitleBarHeight = 48;

    private readonly Grid root = new();
    private readonly Grid stage = new();
    private readonly Grid pageArea = new();
    private readonly ContentControl cover = new() { Focusable = false };
    private readonly SheetHost sheets = new();
    private readonly TitleBar titleBar;
    private readonly System.Windows.Controls.Button back;
    private readonly System.Windows.Controls.Button refresh;
    private readonly RotateTransform refreshTurn = new();
    private readonly System.Windows.Controls.Button menuButton;
    private readonly System.Windows.Controls.Button searchButton;
    /// <summary>Where the pane's search box goes while the window is too narrow for the pane (AppShell.OpenSearchBar).</summary>
    private readonly ContentControl searchSlot = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    private readonly ContentControl trailing = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly HandoffBar handoff;
    private Tray? tray;
    private TourOverlay? tour;

    /// <summary>The title bar's place for the search box on a narrow window.</summary>
    public ContentControl SearchSlot => searchSlot;

    /// <summary>The search box came up into the title bar, or went back to the pane: the title bar laid out again.</summary>
    public void SearchMoved() => UpdateTitleButtons();

    /// <summary>The account menu's Take the tour.</summary>
    public void StartTour() => tour?.Start();

    private Engine.Engine? engine;

    /// <summary>The school's engine, once there is one (a sheet over the window that puts the title bar's pins in its own
    /// heading: ChemSheet).</summary>
    internal Engine.Engine? School => engine;

    private AppShell? shell;
    private SchoolPickerView? picker;
    private SignInCard? card;
    private LoggingIn? working;
    private UIElement? splash;
    private string? phaseLogged;
    private bool stayAsked;

    /// <summary>The app is quitting for real (Quit Simpl, Reset Everything): the window closes instead of hiding.</summary>
    public static bool Quitting;

    public SheetHost Sheets => sheets;

    public MainWindow()
    {
        Title = "Simpl";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png"));
        WindowBackdropType = Appearance.Backdrop;
        ExtendsContentIntoTitleBar = true;
        WindowCornerPreference = WindowCornerPreference.Round;
        FontFamily = K.TextFace;
        SetResourceReference(ForegroundProperty, B.Text);
        if (Shot.Asked || Fx.Opaque) SetResourceReference(BackgroundProperty, B.Window);
        if (Shot.Asked) ToolTipService.SetIsEnabled(this, false);
        Placement.Restore(this, "main", 1280, 820, 720, 520);

        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Content = root;

        // the title bar: Back and Refresh at its left (outside the drag area) — with the pane's button and Search while
        // the window is too narrow for the pane — then the app's icon and name, a Start over while the school's sign-in
        // shows, and Windows' caption buttons
        back = TitleButton(SymbolRegular.ArrowLeft24, () => engine?.GoBack(), "Back (Alt+Left)", 14);
        refresh = TitleButton(SymbolRegular.ArrowClockwise24, () => engine?.Refresh(), "Refresh (F5)", 16);
        if (refresh.Content is UIElement turning)
        {
            turning.RenderTransformOrigin = new Point(0.5, 0.5);
            turning.RenderTransform = refreshTurn;
        }
        menuButton = TitleButton(SymbolRegular.LineHorizontal320, () => shell?.TogglePane(), "Navigation", 16);
        searchButton = TitleButton(SymbolRegular.Search24, () => shell?.OpenSearchBar(), "Search (Ctrl+F)", 16);
        searchButton.Mark(TourSpot.Search); // (the tour's Search on a narrow window, the pane's box folded away)
        titleBar = new TitleBar
        {
            Title = "Simpl", Height = TitleBarHeight, Margin = new Thickness(8, 0, 0, 0),
            Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 16, Height = 16 },
            TrailingContent = trailing, CloseWindowByDoubleClickOnIcon = false,
        };
        titleBar.CloseClicked += (_, _) => { };
        // (a sheet over the window that reaches up into the title bar's band takes its own clicks there, not a drag)
        titleBar.WndProcInvoked += (_, e) =>
        {
            const int WM_NCHITTEST = 0x0084, HTCLIENT = 1;
            if (e.Message != WM_NCHITTEST || !sheets.IsOpen) return;
            var l = e.LParam.ToInt64();
            var at = new Point((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF));
            if (!sheets.Covers(at)) return;
            e.ReturnValue = (IntPtr)HTCLIENT;
            e.Handled = true;
        };
        root.Children.Add(titleBar);
        root.Children.Add(back);
        root.Children.Add(refresh);
        root.Children.Add(menuButton);
        root.Children.Add(searchButton);
        root.Children.Add(searchSlot);

        Grid.SetRow(stage, 1);
        root.Children.Add(stage);
        // the school's own page, with the note over it that says what to do there (under the title bar, not over the
        // page: the page is a window of its own, and nothing drawn here can go over it)
        handoff = new HandoffBar();
        pageArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pageArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        pageArea.Children.Add(handoff);
        pageArea.Visibility = Visibility.Collapsed;
        stage.Children.Add(pageArea);
        stage.Children.Add(cover);

        Grid.SetRowSpan(sheets, 2);
        root.Children.Add(sheets);
        sheets.OpenChanged += _ => Phased();

        AddTray();
        MemoryTidy.Watch(this, () => engine);
        SourceInitialized += (_, _) => Appearance.Apply();
        Appearance.Changed += () => shell?.Retheme();
        Loaded += (_, _) => Begin();
        ContentRendered += (_, _) => Fx.Decide(); // (the render tier is known once a frame is drawn)
        Closing += OnClosing;
        StateChanged += (_, _) => Placement.Save(this, "main");
        SizeChanged += (_, _) => { Placement.Save(this, "main"); shell?.WindowWidth(ActualWidth); UpdateTitleButtons(); };
        LocationChanged += (_, _) => Placement.Save(this, "main");
        PreviewKeyDown += OnKey;
        PreviewMouseDown += (_, e) =>
        {
            // (a press on the title bar, outside the shell, folds back what the shell has open too)
            if (shell != null && !shell.IsMouseOver) shell.Pressed(e);
            if (e.ChangedButton == MouseButton.XButton1) { engine?.GoBack(); e.Handled = true; }
            if (e.ChangedButton == MouseButton.XButton2) { engine?.GoForward(); e.Handled = true; }
        };
        // (updates: what the window has open that one waits for, where it is, its notice — Shell/UpdateWatch.cs)
        UpdateWatch.Attach(this, root, () => engine, () => tour?.On == true);
    }

    // ---- beginning: the school, then its engine ----

    private void Begin()
    {
        Shot.ArmIfAsked();
        Uninstall.IfAsked();
        if (Launch.DevBaseUrl is { } dev) { Open(dev.Host); return; }
        if (Prefs.String("school") is { Length: > 0 } school) { Open(school); return; }
        ShowPicker();
    }

    private void ShowPicker()
    {
        picker = new SchoolPickerView(Open, this);
        SetCover(picker);
    }

    /// <summary>The school chosen: its site in the engine underneath, the app's screens over it once it is ready.</summary>
    private void Open(string schoolHost)
    {
        picker = null;
        if (Launch.DevBaseUrl == null) Prefs.Set("school", schoolHost);
        engine = new Engine.Engine(schoolHost);
        Perf.Engine(engine);
        Grid.SetRow(engine.Web, 1);
        pageArea.Children.Add(engine.Web);
        handoff.Bind(engine);
        engine.PropertyChanged += OnEngine;
        engine.ToolRequested += t => { if (engine is { } e) new WebWindow(e, t, this).Show(); };
        engine.QuizRequested += q => Simpl.Screens.QuizPopup.Show(engine, q, this);
        engine.AltaRequested += a => Simpl.Screens.AltaPopup.Show(engine, a, this);
        engine.MacmillanRequested += a => Simpl.Screens.MacmillanPopup.Show(engine, a, this);
        engine.SettingsRequested += OpenSettings;
        engine.WhatsNewRequested += d => { WhatsNew.MarkSeen(); WhatsNewSheet.Show(d, this); };
        engine.SetupRequested += () => SetupSheet.Show(engine, this);
        engine.NewMessageRequested += () => Simpl.Screens.ComposeSheet.Show(engine, this);
        Simpl.Screens.CourseCompose.Hook = (to, context) => Simpl.Screens.ComposeSheet.Show(engine, this, new[] { to }, context);
        engine.NewTaskRequested += () =>
        {
            if (Simpl.Screens.CalendarScreen.TakeNewTaskDay() is { } day) Simpl.Screens.NewTaskSheet.StartDay = day;
            Simpl.Screens.NewTaskSheet.Show(engine, this);
        };
        Bridge.Shared.OpenSettingsRequested += OpenSettings;
        // (pictures in the school's rich text come with its session)
        var e0 = engine;
        RichText.Cookies = async u => e0.Web.CoreWebView2 is { } core
            ? string.Join("; ", (await core.CookieManager.GetCookiesAsync(u.ToString())).Select(c => $"{c.Name}={c.Value}"))
            : null;
        engine.Web.CoreWebView2InitializationCompleted += (_, _) => Phased();
        Phased();
        engine.Start();
    }

    private void OnEngine(object? sender, PropertyChangedEventArgs e)
    {
        var n = e.PropertyName ?? "";
        if (n == nameof(Engine.Engine.Phase) || n.StartsWith("Login", StringComparison.Ordinal) || n == nameof(Engine.Engine.Snapshot)) Phased();
        if (n == nameof(Engine.Engine.NavVersion)) UpdateTitleButtons();
        if (n == nameof(Engine.Engine.Refreshing)) Refreshing();
        if (n == "Login.AskStay" && engine?.Login.AskStay == true) AskStay();
    }

    /// <summary>Another school (the sign-in card's "Change school…", Settings ▸ General): this one forgotten, its
    /// engine let go, the search again.</summary>
    public void ChangeSchool()
    {
        if (engine == null) return;
        Prefs.Remove("school");
        SchoolName.Forget(engine.Host);
        Prefs.Remove(Engine.Engine.SetupLaterKey); // (another school's first setup is put up as any first setup is)
        var old = engine;
        engine = null;
        old.PropertyChanged -= OnEngine;
        Notices.Stop();
        shell?.Dispose();
        shell = null;
        if (tour != null) { root.Children.Remove(tour); tour = null; }
        card = null;
        working = null;
        pageArea.Children.Remove(old.Web);
        Bridge.Shared.Unregister(old.Web); // (no longer told of storage changes: telling a disposed view failed every save)
        try { old.Web.Dispose(); } catch { }
        handoff.Bind(null);
        ShowPicker();
        Phased();
    }

    // ---- what shows ----

    private void SetCover(FrameworkElement? e)
    {
        if (cover.Content == e) return;
        var was = cover.Content;
        cover.Content = e;
        // (the splash hands off to the app as the Mac's does: the app fades in where it was, over Motion.Slow — only then;
        // the picker, the sign-in card and "Logging you in" come and go at once)
        if (was != null && ReferenceEquals(was, splash) && e != null && ReferenceEquals(e, shell)) Motion.FadeIn(e, Motion.Slow);
        Shot.Moved();
    }

    /// <summary>What the engine and the sign-in say now, shown: the picker, the card, "Logging you in", the page, the app.</summary>
    private void Phased()
    {
        if (engine == null)
        {
            pageArea.Visibility = Visibility.Collapsed;
            UpdateTitleButtons();
            return;
        }
        var login = engine.Login;
        if (Launch.IsDevelopment)
        {
            var now = $"phase {engine.Phase}, sign-in {login.Phase}" + (login.Handoff ? ", handoff" : "") + (login.AskStay ? ", stay?" : "");
            if (now != phaseLogged) { phaseLogged = now; Log.Write(now); }
        }
        if (login.Covers)
        {
            // the app's own sign-in card, or "Logging you in", over the page
            if (login.Phase == LoginAssist.Mode.Capture)
            {
                working = null;
                card ??= new SignInCard(engine, ChangeSchool);
                card.Update();
                SetCover(card);
            }
            else
            {
                card = null;
                working ??= new LoggingIn(engine);
                SetCover(working);
            }
            pageArea.Visibility = Visibility.Collapsed;
        }
        else if (engine.Phase == Phase.Native)
        {
            card = null;
            working = null;
            if (shell == null)
            {
                // (made on the page's answers, late in the start: WPF-UI's styles finished as one dictionary first, so its
                //  pages are made with it — not on what was kept, which comes before the window has had a moment)
                if (engine.CountsLive) Kit.FlatResources.Finish();
                shell = new AppShell(engine, this);
                Notices.Start(engine, this);
                tour = new TourOverlay(engine, this);
                // (onboarding-4: Keep Simpl on your taskbar? is asked the third time Simpl is opened, not after the tour)
                Taskbar.Opened(this, Busy);
                // (over the title bar too: its buttons dimmed with the page and out of reach, all but Windows' own caption buttons,
                //  dimmed but still working)
                Grid.SetRow(tour, 0);
                Grid.SetRowSpan(tour, 2);
                root.Children.Insert(root.Children.IndexOf(sheets), tour);
                tour.AutoStart();
                Taskbar.ShowIfAsked(this);
            }
            shell.WindowWidth(ActualWidth);
            SetCover(shell);
            pageArea.Visibility = Visibility.Collapsed;
        }
        else if (engine.Phase == Phase.Starting)
        {
            card = null;
            working = null;
            splash ??= Splash();
            SetCover((FrameworkElement)splash);
            pageArea.Visibility = Visibility.Collapsed;
        }
        else
        {
            // the school's own page: what the student needs now (a two-step code, a page the reader could not fill)
            card = null;
            working = null;
            SetCover(null);
            pageArea.Visibility = Visibility.Visible;
        }
        // (a sheet over the window: the page, a window of its own, steps aside meanwhile)
        if (sheets.IsOpen && pageArea.Visibility == Visibility.Visible) pageArea.Visibility = Visibility.Hidden;
        shell?.SheetOpen(sheets.IsOpen);
        var app = engine.Phase == Phase.Native && !login.Covers;
        // (Start over in the title bar only over the school's own page with nothing of Simpl's on it, as on the Mac: the
        //  sign-in card has Change school, "Logging you in" and the hand-off note their own Start over)
        trailing.Content = !app && !login.Covers && login.OnSchoolPage && !login.Handoff ? StartOverButton() : app ? Pins() : null;
        UpdateTitleButtons();
        if (login.AskStay && !stayAsked) AskStay();
    }

    private FrameworkElement? startOver;
    private Simpl.Tools.PinBar? pins;

    /// <summary>The tools pinned to the title bar, while the app shows.</summary>
    private FrameworkElement? Pins() => engine == null ? null : pins ??= new Simpl.Tools.PinBar(engine);

    private FrameworkElement StartOverButton()
    {
        if (startOver != null) return startOver;
        var b = K.Button("Start over", () => engine?.Login.Restart(), icon: SymbolRegular.ArrowCounterclockwise24);
        b.Margin = new Thickness(0, 0, 8, 0);
        b.ToolTip = "Begin signing in again from the start";
        return startOver = b;
    }

    /// <summary>A button of the title bar's left end (Back, Refresh, the pane's, Search): a symbol in a 40 by 36 box.</summary>
    private static System.Windows.Controls.Button TitleButton(SymbolRegular symbol, Action click, string tip, double size)
    {
        var b = K.IconButton(symbol, click, tip, size, 40);
        b.Height = 36;
        b.VerticalAlignment = VerticalAlignment.Top;
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.Visibility = Visibility.Collapsed;
        return b;
    }

    /// <summary>Back and Refresh in the title bar while the app shows (Back enabled when there is somewhere to go back
    /// to); the pane's button and Search beside them while the window is too narrow for the pane — so Search is in sight
    /// at every width, as on the Mac — and, while it is asked for there, the search box itself in Search's place, as wide
    /// as the title bar spares (the app's name and the pinned tools moved along, Windows' caption buttons kept clear).</summary>
    private void UpdateTitleButtons()
    {
        var app = engine != null && engine.Phase == Phase.Native && !engine.Login.Covers;
        var narrow = app && shell != null && shell.Narrow;
        var searching = narrow && searchSlot.Content != null;
        var x = 4.0;
        foreach (var (b, shown) in new[] { (back, app), (refresh, app), (menuButton, narrow), (searchButton, narrow && !searching) })
        {
            b.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (!shown) continue;
            b.Margin = new Thickness(x, 6, 0, 6);
            x += 44;
        }
        searchSlot.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        if (searching)
        {
            // (Windows' caption buttons take 138 at the right, the pinned tools theirs, the app's name about 90)
            var room = ActualWidth - x - 138 - trailing.ActualWidth - 90 - 8;
            searchSlot.Width = Math.Clamp(room, 160, 360);
            searchSlot.Margin = new Thickness(x + 2, 7, 0, 7);
            x += searchSlot.Width + 10;
        }
        back.IsEnabled = engine?.Nav.CanGoBack == true;
        titleBar.Margin = new Thickness(8 + (x - 4), 0, 0, 0);
        Refreshing();
    }

    /// <summary>Refresh while Simpl reads everything again: its arrow turns (Motion off: it dims instead), and it says so.</summary>
    private void Refreshing()
    {
        var on = engine?.Refreshing == true;
        var tip = on ? "Refreshing…" : "Refresh (F5)";
        if (Equals(refresh.ToolTip, tip)) return;
        refresh.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(refresh, tip);
        if (refresh.Content is not UIElement arrow) return;
        if (on && Motion.On)
        {
            // (constant motion, so linear: a turn every 0.9 s for as long as it reads)
            refreshTurn.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, Motion.Ms(900)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        }
        else
        {
            refreshTurn.BeginAnimation(RotateTransform.AngleProperty, null);
            refreshTurn.Angle = 0;
        }
        arrow.Opacity = on && !Motion.On ? 0.45 : 1;
    }

    /// <summary>While the first page loads: the app's mark and its name, not a blank window (the Mac's Splash).</summary>
    private static UIElement Splash()
    {
        var icon = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 96, Height = 96 };
        var v = K.V(16, icon, K.Text("Simpl", Ty.Title).Center(), K.Spinner(20));
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.Margin = new Thickness(0, 0, 0, TitleBarHeight);
        Motion.DrillIn(v);
        return new Grid { Children = { v } };
    }

    /// <summary>"Stay logged in?", once Canvas is reached with details typed in the card (the Mac's alert): where Windows
    /// Hello is set up, the details are locked with it (Stay Logged In has Windows Hello ask once: LoginVault).</summary>
    private void AskStay()
    {
        if (engine == null || stayAsked) return;
        stayAsked = true;
        var e = engine;
        var icon = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left };
        var words = Hello.Supported == true
            ? "Simpl keeps your username and password on this PC only, locked with Windows Hello, and signs you in with your face, fingerprint or PIN when your school asks again. Two-step codes still come to you."
            : "Simpl keeps your username and password on this PC only, encrypted inside Simpl, and signs you in when your school asks again. Two-step codes still come to you.";
        var body = K.V(14, icon, K.Wrapped(words, Ty.Body, B.Text));
        var s = new Sheet { Title = "Stay logged in?", Body = body, Width = 420 };
        s.Add("Stay logged in", SheetButtonKind.Primary, () => e.Login.Stay(true));
        s.Add("Not now", SheetButtonKind.Close, () => e.Login.Stay(false));
        s.Closed += () =>
        {
            stayAsked = false;
            if (e.Login.AskStay) e.Login.Stay(false);
        };
        sheets.Show(s);
        // (the screenshot suite's sign-in, answered as it would be: kept)
        if (Launch.Get("sign-in") != null && Launch.Get("stay") == "yes") Dispatcher.BeginInvoke(() => { e.Login.Stay(true); s.Close(); });
    }

    // ---- Settings, the account's own windows ----

    public void OpenSettings()
    {
        if (engine == null) return;
        SettingsWindow.Show(this, engine, Launch.Get("settings-tab"));
    }

    // ---- the keyboard ----

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (sheets.IsOpen) return;
        // (Escape ends the tour wherever the keyboard is; it counts as seen)
        if (e.Key == Key.Escape && tour?.On == true) { tour.Skip(); e.Handled = true; return; }
        // (the app's own shortcuts wait while the tour is up, as its veil keeps the pointer off the app: Ctrl+K's palette
        //  would take the keyboard from the tour's card; the card's Enter and arrows are its own)
        if (tour?.On == true) return;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        if (e.Key == Key.Escape && shell != null && shell.Escape()) { e.Handled = true; return; }
        if (alt && e.SystemKey == Key.Left) { engine?.GoBack(); e.Handled = true; return; }
        if (alt && e.SystemKey == Key.Right) { engine?.GoForward(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.OemComma) { OpenSettings(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.R || e.Key == Key.F5) { engine?.Refresh(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.F) { shell?.FocusSearch(); e.Handled = true; return; }
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (ctrl && e.Key == Key.K) { shell?.OpenCommands(); e.Handled = true; return; }
        if (ctrl && shift && e.Key == Key.N) { if (engine is { OnBrightspace: false }) engine.AskNewMessage(); e.Handled = true; return; }
        // (what shows — an assignment, a course's Grades — in a window of its own, to keep beside the work)
        if (ctrl && shift && e.Key == Key.O) { if (engine != null) PlaceWindow.OpenCurrent(engine); e.Handled = true; return; }
        // (the Mac's ⌘1–⌘7: the pane's places in order)
        if (ctrl && !shift && engine != null && e.Key is >= Key.D1 and <= Key.D7)
        {
            Place? p = (e.Key - Key.D1) switch
            {
                0 => Place.Dashboard, 1 => Place.Todo, 2 => Place.Calendar, 3 => Place.Grades, 4 => Place.Notifications,
                5 => engine.OnBrightspace ? null : Place.Inbox, _ => Place.Tools,
            };
            if (p != null) engine.Go(p);
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.N) { engine?.AskNewTask(); e.Handled = true; }
    }

    // ---- the notification area, and closing ----

    private void AddTray()
    {
        if (Shot.Asked) return;
        try
        {
            tray = new Tray();
            tray.Open += Bring;
            tray.Settings += () => { Bring(); OpenSettings(); };
            tray.Quit += EraseAll.Quit;
            Application.Current.Exit += (_, _) => tray?.Dispose();
        }
        catch (Exception e) { Log.Write("tray: " + e.Message); }
    }

    /// <summary>A Windows notification from Simpl (a reminder, new activity), from its icon in the notification area.</summary>
    public void Notify(string title, string text, Action? click = null) => tray?.Notify(title, text, click);

    /// <summary>Closed: kept running in the notification area (Settings ▸ General can turn that off), or quit.</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        Placement.Save(this, "main");
        if (Quitting || Shot.Asked || !Prefs.Bool("keepRunning", true) || tray == null)
        {
            Quitting = true;
            tray?.Dispose();
            tray = null;
            Application.Current.Shutdown();
            return;
        }
        e.Cancel = true;
        Hide();
        foreach (Window w in Application.Current.Windows) if (w != this && w.Owner == this) w.Hide();
        if (!Prefs.Bool("backgroundNoted"))
        {
            // (the first time: where it went, and how to quit it)
            Prefs.Set("backgroundNoted", true);
            tray?.Notify("Simpl is still running", "It’s here in the notification area, so reminders and alerts keep coming. Right-click its icon to quit.");
        }
    }

    /// <summary>Something of Simpl's over the window that a question of its own should wait for: a dialog, the tour, the
    /// sign-in. (Not the tour's offer: it dims nothing and may be left unanswered all day.)</summary>
    private bool Busy() => sheets.IsOpen || tour?.On == true || engine == null || engine.Login.Covers || engine.Phase != Phase.Native;

    /// <summary>Brought back (the notification area's icon, Simpl opened again): shown, restored, in front.</summary>
    public void Bring()
    {
        if (!IsVisible && shell != null) Taskbar.Opened(this, Busy);
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
}
