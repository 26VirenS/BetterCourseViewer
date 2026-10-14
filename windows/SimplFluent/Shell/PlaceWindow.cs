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
/// plainest form): one place's screen — a course's Grades, its Assignments, To Do — beside the main window, to keep in
/// sight while working in the other (the instructions while handing in, the gradebook during Alta). Its screen reads,
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

    /// <summary>The place opened in a window of its own.</summary>
    public static void Open(Engine.Engine engine, Place place) => new PlaceWindow(engine, place).Show();

    private PlaceWindow(Engine.Engine engine, Place place)
    {
        this.engine = engine;
        screen = ScreenFactory.For(engine, place);
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

    private double ContentWidth => Math.Max(200, page.ActualWidth - (scroller != null ? 80 : 0));

    private void Widths()
    {
        if (page.ActualWidth <= 0) return;
        if (screen is { Scrolls: true, MaxContentWidth: { } mw }) screen.View.Width = Math.Min(mw, ContentWidth);
        screen.SetWidth(ContentWidth, ActualWidth);
    }

    /// <summary>The screen's title (its line under it) and its commands, as it says them now; the window named after it.</summary>
    private void Header()
    {
        var words = K.V(2, K.Text(screen.Title, Ty.Title));
        if (screen.Subtitle is { Length: > 0 } sub) words.Children.Add(K.Text(sub, Ty.Body, B.Text2));
        titleHost.Content = words;
        commandsHost.Content = screen.Commands;
        Title = screen.Title;
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

    /// <summary>Escape: the screen's (a panel folded back), else nothing — the window stays until it is closed.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !sheets.IsOpen && screen.Escape()) e.Handled = true;
    }
}
