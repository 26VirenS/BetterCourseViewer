using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Shell;

/// <summary>
/// A place of the app in a window of its own (the Mac's File ▸ New Window and a sidebar row's Open in New Window, in its
/// plainest form): one place's screen — a course's Grades, its Assignments, To Do — or one page's — an assignment, a page,
/// a discussion — beside the main window, to keep in sight while working in the other (the instructions while handing
/// in, the gradebook during Alta): from a pane row's or a list row's menu, a page's ⋯, or Ctrl+Shift+O for what shows. Its screen reads,
/// refreshes and follows the look as the main window's do; a row opened in it opens in the main window — the app keeps one
/// way through its places — and the main window comes forward to show it. Windows 11's window: Mica round the page, the
/// place's name in the title bar and over the page with its commands, its own dialogs over itself.
/// </summary>
public sealed class PlaceWindow : FluentWindow, ISheetWindow
{
    private readonly Engine.Engine engine;
    private readonly Screen screen;
    private readonly SheetHost sheets = new();
    private readonly TitleBar bar;
    private readonly ContentControl titleHost = new() { Focusable = false };
    private readonly ContentControl commandsHost = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid page = new();
    private readonly ScrollViewer? scroller;
    private int navAt;
    /// <summary>When the student last pressed something here: a move of the main window's right after is theirs, from here.</summary>
    private long pressedAt;

    public SheetHost Sheets => sheets;

    /// <summary>Open in new window's shortcut, here and in the main window: what shows, in a window of its own.</summary>
    public const string Gesture = "Ctrl+Shift+O";

    /// <summary>The place opened in a window of its own.</summary>
    public static void Open(Engine.Engine engine, Place place) => new PlaceWindow(engine, ScreenFactory.For(engine, place), () => Open(engine, place)).Show();

    /// <summary>A page opened on a place — an assignment, a page, a discussion, a course's section — in a window of its own:
    /// the instructions kept in sight while handing in elsewhere.</summary>
    public static void Open(Engine.Engine engine, Route route) => new PlaceWindow(engine, ScreenFactory.For(engine, route), () => Open(engine, route)).Show();

    /// <summary>What the main window shows (the page on top of its place, or the place) in a window of its own.</summary>
    public static void OpenCurrent(Engine.Engine engine)
    {
        var at = engine.Nav.Current;
        if (at.Path.Count > 0) Open(engine, at.Path[^1]);
        else Open(engine, at.Place);
    }

    /// <summary>"Open in new window" for a row or a page of the school's that has a screen of the app's own (an
    /// assignment, a page, a discussion, a course's section); null for one that has none (a file, another site).</summary>
    public static System.Windows.Controls.MenuItem? MenuItem(Engine.Engine engine, string? url, string title, bool gesture = false)
    {
        if (string.IsNullOrEmpty(url) || engine.NativeRoute(url, title) is not { } route) return null;
        var item = new System.Windows.Controls.MenuItem { Header = "Open in new window", Icon = new SymbolIcon { Symbol = SymbolRegular.WindowNew24 } };
        if (gesture) item.InputGestureText = Gesture;
        item.Click += (_, _) => Open(engine, route);
        return item;
    }

    /// <summary>This window's page opened again in another (its shortcut pressed here).</summary>
    private readonly Action again;

    private PlaceWindow(Engine.Engine engine, Screen screen, Action again)
    {
        this.engine = engine;
        this.screen = screen;
        this.again = again;
        Title = screen.Title;
        WindowBackdropType = Appearance.Backdrop;
        ExtendsContentIntoTitleBar = true;
        FontFamily = K.TextFace;
        SetResourceReference(ForegroundProperty, B.Text);
        if (Shot.Asked || Fx.Opaque) SetResourceReference(BackgroundProperty, B.Window);
        Width = 920;
        Height = 760;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        bar = new TitleBar { Title = Title, Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 16, Height = 16 } };
        var header = K.Columns("*, auto", titleHost.Col(0), commandsHost.Col(1));
        header.Margin = new Thickness(40, 4, 40, 20);
        var v = screen.View;
        if (screen.Scrolls)
        {
            if (screen.MaxContentWidth is { } mw) { v.MaxWidth = mw; v.HorizontalAlignment = HorizontalAlignment.Left; }
            v.Margin = new Thickness(0, 0, 0, 40);
            scroller = new ScrollViewer { Content = v, Padding = new Thickness(40, 0, 40, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            page.Children.Add(scroller);
        }
        else page.Children.Add(v);
        var root = K.Rows("auto, auto, *", bar.Row(0), header.Row(1), page.Row(2));
        Grid.SetRowSpan(sheets, 3);
        root.Children.Add(sheets);
        Content = root;

        screen.HeaderChanged += Header;
        page.SizeChanged += (_, _) => Widths();
        engine.PropertyChanged += OnEngine;
        Appearance.Changed += Restyle;
        PreviewMouseDown += (_, e) => { pressedAt = Environment.TickCount64; screen.Pressed(e); };
        PreviewKeyDown += OnKey;
        navAt = engine.NavVersion;
        Loaded += (_, _) =>
        {
            Widths();
            screen.Show(now: true);
            Header();
        };
        Closed += (_, _) =>
        {
            engine.PropertyChanged -= OnEngine;
            Appearance.Changed -= Restyle;
            screen.HeaderChanged -= Header;
            screen.Hide();
            screen.Release();
        };
    }

    /// <summary>The width a screen is given, as the main window gives it: the page's less its two 40 margins — a screen
    /// that scrolls on its own (an assignment) puts those margins in itself, and given the whole page would run 80 past
    /// the window's edge.</summary>
    private double ContentWidth => Math.Max(200, page.ActualWidth - 80);

    private void Widths()
    {
        if (page.ActualWidth <= 0) return;
        if (screen is { Scrolls: true, MaxContentWidth: { } mw }) screen.View.Width = Math.Min(mw, ContentWidth);
        screen.SetWidth(ContentWidth, ActualWidth);
    }

    /// <summary>The screen's title (its line under it) and its commands, as it says them now; the window named after it.
    /// What the main window's breadcrumb puts before the title comes first here too, with the course's colour — "F26-MATH
    /// 021 20 › Grades" — since a course's page in a window of its own could otherwise be any course's.</summary>
    private void Header()
    {
        var line = new WrapPanel { Orientation = Orientation.Horizontal };
        if (screen.TitleColor is { } tint)
            line.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), Background = K.Brush(tint), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 11, 10, 0) });
        foreach (var (crumb, _) in screen.Before)
        {
            line.Children.Add(K.Text(crumb, Ty.Title, B.Text2));
            line.Children.Add(K.Icon(SymbolRegular.ChevronRight20, 16, B.Text2).Margin(10, 6, 10, 0));
        }
        line.Children.Add(K.Text(screen.Title, Ty.Title));
        var words = K.V(2, line);
        if (screen.Subtitle is { Length: > 0 } sub) words.Children.Add(K.Text(sub, Ty.Body, B.Text2));
        titleHost.Content = words;
        commandsHost.Content = screen.Commands;
        Title = screen.Before.Count > 0 ? $"{screen.Title} – {screen.Before[^1].Title}" : screen.Title;
        bar.Title = Title;
    }

    private void OnEngine(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Engine.Engine.DataVersion): screen.Refresh(); break;
            case nameof(Engine.Engine.NavVersion):
                // (something opened from here opens in the main window: brought forward to show it)
                var mine = Environment.TickCount64 - pressedAt < 1500 && IsActive;
                if (engine.NavVersion != navAt && mine && Application.Current?.MainWindow is { } main && main != this)
                {
                    if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
                    main.Show();
                    main.Activate();
                }
                navAt = engine.NavVersion;
                break;
        }
    }

    private void Restyle()
    {
        screen.Restyle();
        Header();
    }

    /// <summary>Escape: the screen's (a panel folded back), else nothing — the window stays until it is closed;
    /// Ctrl+Shift+O, this page in another window.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !sheets.IsOpen && screen.Escape()) e.Handled = true;
        else if (e.Key == Key.O && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { again(); e.Handled = true; }
    }
}
