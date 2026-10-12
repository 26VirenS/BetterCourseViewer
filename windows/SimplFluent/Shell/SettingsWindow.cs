using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Shell;

/// <summary>What the page answers about the grade settings (settingsInfo).</summary>
public sealed class SettingsInfo
{
    public bool Tracking { get; set; }
    public double Goal { get; set; } = 4;
    public bool WhatIf { get; set; }
    public int Days { get; set; }
    public string History { get; set; } = "";
    public string HistoryNote { get; set; } = "";
    public string? Record { get; set; }
    public string RecordNote { get; set; } = "";
}

public sealed class SettingsMessage
{
    public string Message { get; set; } = "";
    public SettingsInfo Info { get; set; } = new();
}

public sealed class SettingsFile
{
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>
/// Simpl's Settings (the Mac's Settings window and its tabs — General, Appearance, Notifications, Grades, Data, Updates,
/// About), laid out as Windows 11's own Settings app: the categories down a pane at the left with their colourful
/// icons, who is signed in at its head and "Find a setting" under it; the category's title and its rows — Windows 11's
/// settings cards, a symbol, a name over a line that says what it does, and what changes it at the right — on the
/// right. One window at a time; asked for again, it comes forward on the category asked for.
/// </summary>
public sealed class SettingsWindow : FluentWindow, ISheetWindow
{
    private static SettingsWindow? open;

    private readonly Engine.Engine engine;
    private readonly MainWindow main;
    private readonly SheetHost sheets = new();
    private readonly NavList nav = new();
    private readonly ContentControl titleHost = new() { Focusable = false };
    private readonly ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Border said = new();
    private readonly TextBlock saidText;
    private readonly SymbolIcon saidIcon;
    private string tab = "general";
    private SettingsInfo? info;
    private string? infoError;
    private bool busy;

    public SheetHost Sheets => sheets;

    private static readonly (string Key, string Title, SymbolRegular Icon, Color Tint, string Words)[] Tabs =
    {
        ("general", "General", SymbolRegular.Settings24, Color.FromRgb(0x76, 0x76, 0x7c), "school sign out change saved sign-in password credential courses goals set up background notification area close taskbar pin"),
        ("appearance", "Appearance", SymbolRegular.PaintBrush24, Color.FromRgb(0xd1, 0x34, 0x8b), "light dark mode theme colour color accent photo picture"),
        ("notifications", "Notifications", SymbolRegular.Alert24, Color.FromRgb(0xe3, 0x6c, 0x0a), "reminders due alerts activity announcements"),
        ("grades", "Grades", SymbolRegular.DataBarVertical24, Color.FromRgb(0x88, 0x64, 0xd8), "gpa goal history record what-if track"),
        ("data", "Data", SymbolRegular.Database24, Color.FromRgb(0x00, 0x99, 0xbc), "export import reset erase everything backup"),
        ("updates", "Updates", SymbolRegular.ArrowSync24, Color.FromRgb(0x10, 0x89, 0x3e), "version update beta automatic check earlier download install restart"),
        ("about", "About", SymbolRegular.Info24, Color.FromRgb(0x00, 0x78, 0xd4), "version interface uninstall"),
    };

    public static void Show(MainWindow owner, Engine.Engine engine, string? tab)
    {
        if (open != null)
        {
            if (tab != null) open.Go(tab);
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Show();
            open.Activate();
            return;
        }
        open = new SettingsWindow(owner, engine, tab ?? "general");
        open.Show();
    }

    private SettingsWindow(MainWindow owner, Engine.Engine engine, string tab)
    {
        this.engine = engine;
        main = owner;
        Owner = owner;
        Title = "Settings";
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png"));
        WindowBackdropType = Appearance.Backdrop;
        ExtendsContentIntoTitleBar = true;
        WindowCornerPreference = WindowCornerPreference.Round;
        FontFamily = K.TextFace;
        SetResourceReference(ForegroundProperty, B.Text);
        if (Shot.Asked) { SetResourceReference(BackgroundProperty, B.Window); ToolTipService.SetIsEnabled(this, false); }
        if (Shot.Asked && Launch.Get("window-size") is { } ws && ws.Split('x') is { Length: 2 } p && double.TryParse(p[0], out var w0) && double.TryParse(p[1], out var h0))
        {
            Width = w0;
            Height = h0;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else Placement.Restore(this, "settings", 1000, 720, 560, 420);
        if (Launch.Get("window-size") == null && !Prefs.Bool("window:settings:max") && Prefs.String("window:settings") == null) WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var bar = new TitleBar
        {
            Title = "Settings", Height = 48, Margin = new Thickness(8, 0, 0, 0),
            Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 16, Height = 16 },
        };
        root.Children.Add(bar);

        // the pane: who is signed in, "Find a setting", the categories
        var me = engine.Snapshot?.Me;
        var name = me?.Name ?? "Simpl";
        var who = K.Columns("auto, *", K.Avatar(name, me?.Initials, me?.Avatar, 64).Col(0),
            K.V(0, K.Text(name, Ty.BodyStrong), K.Text(me?.Email ?? engine.Host, Ty.Caption, B.Text2)).Margin(14, 0, 0, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1));
        who.Margin = new Thickness(16, 8, 16, 16);
        var find = new TextBox { PlaceholderText = "Find a setting", Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 }, IconPlacement = ElementPlacement.Right, Margin = new Thickness(16, 0, 16, 12), MinHeight = 34 };
        find.TextChanged += (_, _) => Find(find.Text);
        var pane = K.Rows("auto, auto, *", who.Row(0), find.Row(1), new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }.Row(2));
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(288) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(pane.Col(0));

        // the category: its title, then its rows
        var page = K.Rows("auto, *", titleHost.Margin(40, 4, 40, 20).Row(0), scroller.Row(1));
        scroller.Padding = new Thickness(40, 0, 40, 40);
        body.Children.Add(page.Col(1));
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // a word at the foot (Windows 11's snackbar): Saved …, Imported, what went wrong
        saidIcon = K.Icon(SymbolRegular.CheckmarkCircle24, 16, B.Success, filled: true);
        saidText = K.Text("", Ty.Body);
        said.Child = K.H(10, saidIcon, saidText);
        said.Padding = new Thickness(16, 10, 20, 10);
        said.CornerRadius = new CornerRadius(8);
        said.HorizontalAlignment = HorizontalAlignment.Center;
        said.VerticalAlignment = VerticalAlignment.Bottom;
        said.Margin = new Thickness(0, 0, 0, 20);
        said.Effect = K.Shadow(24, 6, 0.2);
        said.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        said.Visibility = Visibility.Collapsed;
        Grid.SetRow(said, 1);
        root.Children.Add(said);

        Grid.SetRowSpan(sheets, 2);
        root.Children.Add(sheets);
        Content = root;

        Updates.Changed += OnUpdates;
        Updates.ProgressChanged += OnProgress;
        Appearance.Changed += OnAppearance;
        Closed += (_, _) =>
        {
            Updates.Changed -= OnUpdates;
            Updates.ProgressChanged -= OnProgress;
            Appearance.Changed -= OnAppearance;
            open = null;
            main.Activate();
        };
        SizeChanged += (_, _) => Placement.Save(this, "settings");
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !sheets.IsOpen) { Close(); e.Handled = true; } };
        Go(tab);
        if (engine.Phase == Phase.Native) _ = LoadInfo();
    }

    private void OnUpdates() => Dispatcher.BeginInvoke(() => { if (tab == "updates") Render(); });
    private void OnProgress() => Dispatcher.BeginInvoke(() =>
    {
        if (downloadBar != null) downloadBar.Value = Updates.Progress * 100;
        if (downloadWords != null) downloadWords.Text = DownloadWords();
    });
    private void OnAppearance() => Dispatcher.BeginInvoke(() => { Pane(); if (tab == "appearance") Render(); });

    private void Pane()
    {
        nav.Set(Tabs.Select(t => new NavEntry { Key = t.Key, Title = t.Title, Icon = t.Icon, Tint = t.Tint, Go = () => Go(t.Key) }), tab);
    }

    private void Find(string text)
    {
        var q = text.Trim().ToLowerInvariant();
        if (q.Length < 2) return;
        var hit = Tabs.FirstOrDefault(t => t.Title.ToLowerInvariant().Contains(q) || t.Words.Split(' ').Any(w => w.StartsWith(q)));
        if (hit.Key != null && hit.Key != tab) Go(hit.Key);
    }

    private void Go(string key)
    {
        if (Tabs.All(t => t.Key != key)) key = "general";
        tab = key;
        Pane();
        Render();
        scroller.ScrollToTop();
        if (Launch.Get("settings-scroll") is { } s && double.TryParse(s, out var off)) Dispatcher.BeginInvoke(() => scroller.ScrollToVerticalOffset(off), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Render()
    {
        var t = Tabs.First(x => x.Key == tab);
        titleHost.Content = K.Text(t.Title, Ty.Title);
        FrameworkElement content = tab switch
        {
            "appearance" => AppearancePane(),
            "notifications" => NotificationsPane(),
            "grades" => GradesPane(),
            "data" => DataPane(),
            "updates" => UpdatesPane(),
            "about" => AboutPane(),
            _ => GeneralPane(),
        };
        content.MaxWidth = 1000;
        content.HorizontalAlignment = HorizontalAlignment.Left;
        var first = scroller.Content == null;
        scroller.Content = content;
        if (first || Motion.On) Motion.Enter(content, 0, 20, 360);
        Shot.Moved();
    }

    private static Stack Column(params UIElement?[] kids) => K.V(0, kids);

    private static TextBlock Foot(string text) => K.Wrapped(text, Ty.Caption, B.Text2).Margin(2, 8, 2, 0);

    // ---- General ----

    private FrameworkElement GeneralPane()
    {
        var change = K.Button("Change school…", () => { Close(); main.ChangeSchool(); });
        var signOut = K.Button("Sign out…", () => Dialogs.Confirm(this, $"Sign out of {engine.LmsName} on this PC?",
            $"Your {engine.LmsName} session and any saved sign-in are cleared; settings stay.", "Sign Out", () => { engine.SignOut(); Close(); }));
        var v = Column(
            K.SectionHeader("School", 0),
            K.Group(
                K.Row(SymbolRegular.BuildingBank24, engine.LmsName, engine.Host, change),
                K.Row(SymbolRegular.SignOut24, "Sign out", $"Your {engine.LmsName} session and any saved sign-in are cleared; settings stay.", signOut)));

        v.Children.Add(K.SectionHeader("Sign-in"));
        var saved = LoginVault.Load();
        if (saved != null)
        {
            var forget = K.Button("Forget…", () => Dialogs.Confirm(this, "Forget the saved sign-in?", "Simpl will ask for your username and password the next time your school does.", "Forget", () => { LoginVault.Clear(); Render(); }));
            v.Children.Add(K.Group(K.Row(SymbolRegular.Key24, $"Saved for {saved.User}", $"Sign-in page: {saved.Host}", forget)));
        }
        else v.Children.Add(K.Row(SymbolRegular.Key24, "Not saved", "When your school’s sign-in page comes up, sign in with Simpl’s card and answer Stay Logged In."));
        v.Children.Add(Foot("Kept in Windows’ Credential Manager on this PC only: never synced, and only ever typed into your school’s own sign-in page. Signing out forgets it too."));

        if (engine.Phase == Phase.Native)
        {
            v.Children.Add(K.SectionHeader("Simpl"));
            v.Children.Add(K.Row(SymbolRegular.Target24, "Courses and goals", "The courses that count and what you aim for in each.", K.Button("Set up…", () => { engine.AskSetup(); main.Activate(); })));
        }

        v.Children.Add(K.SectionHeader("Window"));
        var keep = new ToggleSwitch { IsChecked = Prefs.Bool("keepRunning", true) };
        keep.Checked += (_, _) => Prefs.Set("keepRunning", null);
        keep.Unchecked += (_, _) => Prefs.Set("keepRunning", false);
        // (Simpl on the taskbar: said when it is; else the way to put it there — what the tour's ending asks)
        var pinned = Taskbar.IsPinned();
        v.Children.Add(K.Group(
            K.Row(SymbolRegular.WindowArrowUp24, "Keep Simpl running when its window is closed",
                "Simpl stays in the notification area, so alerts keep coming. Quit it there, or from the account menu.", keep),
            K.Row(SymbolRegular.Pin24, "Taskbar", pinned ? "Simpl is on your taskbar, one click away." : "Keep Simpl one click away.",
                pinned ? K.Text("Pinned", Ty.Body, B.Text2) : K.Button("Pin to taskbar…", () => _ = PinToTaskbar()))));
        return v;
    }

    private async Task PinToTaskbar()
    {
        var answer = await Taskbar.Ask(this);
        if (answer == Taskbar.Answer.AlreadyPinned) Say("Simpl is on your taskbar already.");
        else if (answer == Taskbar.Answer.Pinned) Say("Simpl is on your taskbar.");
        if (tab == "general") Render();
    }

    // ---- Appearance ----

    private FrameworkElement AppearancePane()
    {
        var mode = new ComboBox { MinWidth = 200, ItemsSource = new[] { "Light", "Dark", "As Windows is set" } };
        mode.SelectedIndex = Appearance.Look switch { "light" => 0, "dark" => 1, _ => 2 };
        mode.SelectionChanged += (_, _) => Appearance.Look = mode.SelectedIndex switch { 0 => "light", 1 => "dark", _ => "system" };
        var v = Column(
            K.Row(SymbolRegular.DarkTheme24, "Choose your mode", "Light or dark, or as Windows is set (Settings ▸ Personalization ▸ Colors).", mode));

        // the ready themes: a tile each, its colour and its name; the one worn outlined in the accent
        v.Children.Add(K.SectionHeader("Theme"));
        var themes = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var r in Appearance.ReadyThemes)
        {
            var on = Appearance.ThemeName == r.Name;
            var color = r.Accent is { } h ? K.Hex(h) : Appearance.Accent;
            // (its drawing inked in its own colour, as it would show on a counter; Default: Windows' accent, no drawing)
            var swatch = new Border
            {
                Height = 64, CornerRadius = new CornerRadius(6), ClipToBounds = true,
                Background = r.Accent == null ? Res("ControlStrongFillColorDefaultBrush") : null,
                Child = r.Scene is { } sc ? Scenes.Swatch(sc, "Card0", color) : K.Icon(SymbolRegular.Color24, 24, B.OnAccent),
            };
            swatch.Loaded += (_, _) => swatch.Clip = new RectangleGeometry(new Rect(0, 0, swatch.ActualWidth, swatch.ActualHeight), 6, 6);
            var tile = K.V(8, swatch, K.Text(r.Name, Ty.Body).Center());
            var b = K.CardButton(tile, () => Appearance.Wear(r), 8, new Thickness(8, 8, 8, 10));
            b.Width = 132;
            b.Margin = new Thickness(0, 0, 8, 8);
            if (on) { b.SetResourceReference(Control.BorderBrushProperty, B.Accent); b.BorderThickness = new Thickness(2); }
            b.ToolTip = r.Accent == null ? "Windows’ own accent colour" : $"{r.Name}: its colour throughout";
            themes.Children.Add(b);
        }
        v.Children.Add(themes);

        // the accent: Windows' own, or one of Simpl's colours (Windows 11's colour swatches)
        v.Children.Add(K.SectionHeader("Accent colour"));
        var swatches = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        swatches.Children.Add(ColourDot(null, ApplicationAccent(), "Windows accent"));
        foreach (var c in Appearance.Colours) swatches.Children.Add(ColourDot(c.Hex, K.Hex(c.Hex), c.Name));
        var accentCard = K.Card(K.V(10,
            K.Columns("auto, *", K.Icon(SymbolRegular.Color24, 20).Margin(2, 0, 20, 0).Col(0),
                K.V(0, K.Text("Accent colour", Ty.Body), K.Wrapped("The navigation pane’s marks, the buttons, the links and every selection take it.", Ty.Caption, B.Text2)).Col(1)),
            swatches.Margin(42, 0, 0, 0)), new Thickness(16, 14, 16, 16), 4);
        v.Children.Add(accentCard);
        v.Children.Add(PhotosSection());
        return v;
    }

    // ---- Photos: what each place shows (the Mac's PhotoSlotEditor) ----

    private string photoSlot = Launch.Get("photo-slot") is { } ps && Appearance.Slots.Any(s => s.Key == ps) ? ps : "side";

    private FrameworkElement PhotosSection()
    {
        var v = Column(K.SectionHeader("Photos"));
        var slot = photoSlot;
        var place = new ComboBox { MinWidth = 200, ItemsSource = Appearance.Slots.Select(s => s.Name).ToList(), SelectedIndex = Array.FindIndex(Appearance.Slots, s => s.Key == slot) };
        place.SelectionChanged += (_, _) => { photoSlot = Appearance.Slots[Math.Max(0, place.SelectedIndex)].Key; Render(); };
        v.Children.Add(K.Row(SymbolRegular.Image24, "Place", "The navigation pane, or one of the Dashboard’s six counters.", place));

        // the place, small, as it shows now
        var side = slot == "side";
        var preview = new Border { Width = side ? 96 : 200, Height = side ? 228 : 114, CornerRadius = new CornerRadius(8), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
        preview.Bg(side ? B.Window : B.Card).Stroke(B.CardStroke);
        preview.Child = Scenes.Backdrop(slot, frost: false) ?? (FrameworkElement)K.Text("Nothing here", Ty.Caption, B.Text3).Center().Align(HorizontalAlignment.Center, VerticalAlignment.Center);
        preview.Loaded += (_, _) => preview.Clip = new RectangleGeometry(new Rect(0, 0, preview.ActualWidth, preview.ActualHeight), 7, 7);

        var kind = new ComboBox { MinWidth = 200, ItemsSource = new[] { "The theme’s drawing", "My photo", "Nothing" } };
        var has = Appearance.Photo(slot) != null;
        kind.SelectedIndex = Appearance.PhotoKind(slot) switch { "photo" => 1, "none" => 2, _ => 0 };
        kind.SelectionChanged += (_, _) =>
        {
            var k = kind.SelectedIndex switch { 1 => "photo", 2 => "none", _ => "drawing" };
            if (k == "photo" && Appearance.Photo(slot) == null) { ChoosePhoto(slot); return; }
            Appearance.SetKind(slot, k);
        };
        var choose = K.Button(has ? "Replace photo…" : "Choose photo…", () => ChoosePhoto(slot), icon: SymbolRegular.FolderOpen24);
        var remove = K.Button("Remove", () => { Appearance.RemovePhoto(slot); Scenes.Forget(); });
        remove.IsEnabled = has;
        var zoom = new Slider { Minimum = 0.5, Maximum = 3, Value = Appearance.Zoom(slot), Width = 200, SmallChange = 0.05, LargeChange = 0.25 };
        var zoomTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        zoomTimer.Tick += (_, _) => { zoomTimer.Stop(); Appearance.SetZoom(slot, Math.Round(zoom.Value, 2)); };
        zoom.ValueChanged += (_, _) => { zoomTimer.Stop(); zoomTimer.Start(); };
        var controls = K.V(4,
            K.Row(SymbolRegular.ImageMultiple24, "Shows", "A theme’s drawing, a photo of your own (inked in two tones, as on the web), or nothing.", kind),
            K.Row(SymbolRegular.FolderOpen24, "Photo", "Kept in Simpl’s folder on this PC, never sent anywhere.", K.H(8, choose, remove)),
            K.Row(SymbolRegular.ZoomIn24, "Zoom", "How large it is drawn in its place.", zoom));
        v.Children.Add(K.Columns("auto, 16, *", preview.Col(0), controls.Col(2)));
        return v;
    }

    private void ChoosePhoto(string slot)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.heic;*.webp|All files|*.*" };
        if (dlg.ShowDialog(this) != true) { Render(); return; }
        Scenes.Forget();
        if (Appearance.SetPhoto(slot, dlg.FileName)) Say("Photo set."); else Say("The picture could not be read.", true);
    }

    private static Color ApplicationAccent() => Wpf.Ui.Appearance.ApplicationAccentColorManager.SystemAccent;

    private FrameworkElement ColourDot(string? hex, Color colour, string name)
    {
        var on = Appearance.AccentHex == hex;
        var dot = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(colour) };
        if (on) dot.Child = K.Icon(SymbolRegular.Checkmark24, 18, Colors.White);
        var ring = new Border { Padding = new Thickness(3), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2), Child = dot, Margin = new Thickness(0, 0, 6, 6) };
        if (on) ring.SetResourceReference(Border.BorderBrushProperty, B.Text); else ring.BorderBrush = Brushes.Transparent;
        var b = K.Subtle(ring, () => Appearance.SetAccent(hex), new Thickness(0), 6);
        b.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        return b;
    }

    private static Brush Res(string key) => (Brush)(Application.Current.TryFindResource(key) ?? Brushes.Gray);

    // ---- Notifications ----

    private static readonly string[] Leads = { "1 hour before", "3 hours before", "1 day before", "1 day and 1 hour before", "2 days before" };
    private static readonly (string Key, string Label)[] ActivityKinds =
    {
        ("announcements", "Announcements"), ("grades", "Grades"), ("comments", "Comments on your work"), ("inbox", "Inbox messages"), ("discussions", "Discussions"),
    };

    private FrameworkElement NotificationsPane()
    {
        var v = Column(K.SectionHeader("Reminders", 0));
        var on = new ToggleSwitch { IsChecked = Prefs.Bool("reminders:on") };
        var lead = new ComboBox { MinWidth = 220, ItemsSource = Leads, SelectedItem = Prefs.String("reminders:lead") ?? Leads[3] };
        lead.SelectionChanged += (_, _) => { Prefs.Set("reminders:lead", lead.SelectedItem as string); Notices.SettingsChanged(); };
        var leadRow = K.Row(SymbolRegular.Clock24, "Remind me", null, lead);
        leadRow.Visibility = on.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        on.Checked += (_, _) => { Prefs.Set("reminders:on", true); leadRow.Visibility = Visibility.Visible; Notices.SettingsChanged(); };
        on.Unchecked += (_, _) => { Prefs.Set("reminders:on", false); leadRow.Visibility = Visibility.Collapsed; Notices.SettingsChanged(); };
        v.Children.Add(K.Group(K.Row(SymbolRegular.AlertOn24, "Due-date reminders", "Alerts for work still to hand in over the next three weeks.", on), leadRow));
        v.Children.Add(Foot("Windows notifications from Simpl while it runs — and it keeps running in the notification area with its window closed. Nothing is sent anywhere; new work is picked up as it comes."));

        v.Children.Add(K.SectionHeader("New activity"));
        var act = new ToggleSwitch { IsChecked = Prefs.Bool("activity:on") };
        var kinds = (Prefs.String("activity:kinds") ?? "announcements,grades,comments,inbox").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var boxes = K.V(4);
        foreach (var (key, label) in ActivityKinds)
        {
            var cb = new CheckBox { Content = label, IsChecked = kinds.Contains(key) };
            void Save()
            {
                if (cb.IsChecked == true) kinds.Add(key); else kinds.Remove(key);
                Prefs.Set("activity:kinds", string.Join(",", ActivityKinds.Select(k => k.Key).Where(kinds.Contains)));
            }
            cb.Checked += (_, _) => Save();
            cb.Unchecked += (_, _) => Save();
            boxes.Children.Add(cb);
        }
        var kindsCard = K.Card(boxes.Margin(42, 0, 0, 0), new Thickness(16, 12, 16, 12), 4);
        kindsCard.Visibility = act.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        act.Checked += (_, _) => { Prefs.Set("activity:on", true); kindsCard.Visibility = Visibility.Visible; Notices.SettingsChanged(); };
        act.Unchecked += (_, _) => { Prefs.Set("activity:on", false); kindsCard.Visibility = Visibility.Collapsed; };
        v.Children.Add(K.Group(K.Row(SymbolRegular.Megaphone24, "New activity", "Alerts when something is posted in your courses.", act), kindsCard));
        var foot = engine.OnBrightspace
            ? "Brightspace has no activity stream for Simpl to read: these alerts are for Canvas schools."
            : "Simpl looks about every 3 minutes while it runs — with its window closed too — reading with the sign-in on this PC: nothing is sent anywhere.";
        v.Children.Add(Foot(foot));
        return v;
    }

    // ---- Grades ----

    private FrameworkElement GradesPane()
    {
        if (engine.Phase != Phase.Native) return K.Row(SymbolRegular.Info24, "Not yet", $"Grade settings appear once {engine.LmsName} has loaded and you are signed in.");
        if (info == null)
        {
            if (infoError != null) return K.Row(SymbolRegular.ErrorCircle24, "Couldn’t read your settings", infoError, K.Button("Try again", () => _ = LoadInfo()));
            return K.Row(null, "Reading your settings…", null, K.Spinner(20));
        }
        var i = info;
        var track = new ToggleSwitch { IsChecked = i.Tracking };
        track.Checked += (_, _) => { i.Tracking = true; Save(new { tracking = true }); };
        track.Unchecked += (_, _) => { i.Tracking = false; Save(new { tracking = false }); };
        var goal = new NumberBox { Value = i.Goal, Minimum = 0, Maximum = 4, SmallChange = 0.05, LargeChange = 0.5, MaxDecimalPlaces = 2, Width = 140, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var goalTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        goalTimer.Tick += async (_, _) =>
        {
            goalTimer.Stop();
            var g = Math.Clamp(Math.Round((goal.Value ?? 4) * 100) / 100, 0, 4);
            try { await engine.Call<SettingsInfo>("settingsSave", new { goal = g }); i.Goal = g; }
            catch (Exception e) { Say("Could not save the goal: " + e.Message, true); }
        };
        goal.ValueChanged += (_, _) => { goalTimer.Stop(); goalTimer.Start(); };
        var whatIf = new ToggleSwitch { IsChecked = i.WhatIf };
        whatIf.Checked += (_, _) => { i.WhatIf = true; Save(new { whatIf = true }); };
        whatIf.Unchecked += (_, _) => { i.WhatIf = false; Save(new { whatIf = false }); };
        var v = Column(
            K.SectionHeader("Grades", 0),
            K.Group(
                K.Row(SymbolRegular.DataTrending24, "Track grades over time", "One snapshot a day, kept on this PC.", track),
                K.Row(SymbolRegular.Target24, "Term GPA goal", "What the Grades page measures your term against.", goal),
                K.Row(SymbolRegular.Calculator24, "Show what-if scores", "Try a score on any assignment to see what it would do to your grade.", whatIf)));

        v.Children.Add(K.SectionHeader("Grade history"));
        var hist = K.H(8, K.Button("Import…", () => Import("historyImport", "CSV|*.csv;*.txt|All files|*.*")), Enabled(K.Button("Export…", () => _ = Export("historyExport", "csv", "CSV|*.csv")), i.Days > 0));
        v.Children.Add(K.Row(SymbolRegular.History24, i.History, i.HistoryNote.Length > 0 ? i.HistoryNote : null, hist));
        v.Children.Add(Foot("A CSV exported before (a date and a GPA a line) adds the days it holds; a day already here is kept as it is."));

        v.Children.Add(K.SectionHeader("Record before this term"));
        var rec = K.H(8, K.Button(i.Record == null ? "Upload CSV…" : "Replace…", () => Import("recordImport", "CSV|*.csv;*.txt|All files|*.*")));
        if (i.Record != null) rec.Children.Add(K.Button("Clear", () => _ = ClearRecord()));
        v.Children.Add(K.Row(SymbolRegular.DocumentTable24, i.Record ?? "No record before this term", null, rec));
        if (i.RecordNote.Length > 0) v.Children.Add(Foot(i.RecordNote));
        return v;
    }

    private static T Enabled<T>(T e, bool on) where T : UIElement { e.IsEnabled = on; return e; }

    private async Task LoadInfo()
    {
        try { info = await engine.Call<SettingsInfo>("settingsInfo"); infoError = null; }
        catch (Exception e) { infoError = e.Message; }
        if (tab == "grades") Render();
    }

    private async void Save(object change)
    {
        try { info = await engine.Call<SettingsInfo>("settingsSave", change); }
        catch (Exception e) { Say("Could not save: " + e.Message, true); await LoadInfo(); }
    }

    private async Task ClearRecord()
    {
        busy = true;
        try { info = await engine.Call<SettingsInfo>("recordClear"); Say("Record cleared; tracking stays on."); }
        catch (Exception e) { Say("Could not clear it: " + e.Message, true); }
        busy = false;
        Render();
    }

    /// <summary>A file the page writes (the GPA history, the settings), saved where the student says.</summary>
    private async Task Export(string call, string ext, string filter)
    {
        if (busy) return;
        busy = true;
        try
        {
            var f = await engine.Call<SettingsFile>(call);
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = f.Name, DefaultExt = ext, Filter = filter };
            if (dlg.ShowDialog(this) == true)
            {
                await File.WriteAllTextAsync(dlg.FileName, f.Text);
                Say($"Saved {Path.GetFileName(dlg.FileName)}.");
            }
        }
        catch (Exception e) { Say("Could not export: " + e.Message, true); }
        busy = false;
    }

    private async void Import(string call, string filter)
    {
        if (busy) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
        if (dlg.ShowDialog(this) != true) return;
        string text;
        try
        {
            var fi = new FileInfo(dlg.FileName);
            if (fi.Length > 5 * 1024 * 1024) throw new Exception("too large");
            text = await File.ReadAllTextAsync(dlg.FileName);
        }
        catch { Say($"{Path.GetFileName(dlg.FileName)} could not be read.", true); return; }
        busy = true;
        try
        {
            if (call == "settingsImport")
            {
                await engine.Call<OK>("settingsImport", new { text });
                await LoadInfo();
                Say("Imported.");
            }
            else
            {
                var r = await engine.Call<SettingsMessage>(call, new { text, name = Path.GetFileName(dlg.FileName) });
                info = r.Info;
                Say(r.Message);
                Render();
            }
        }
        catch (Exception e) { Say(e.Message, true); }
        busy = false;
    }

    // ---- Data ----

    private FrameworkElement DataPane()
    {
        var v = Column(K.SectionHeader("Data", 0));
        if (engine.Phase == Phase.Native)
        {
            var buttons = K.H(8, K.Button("Import…", () => Import("settingsImport", "JSON|*.json;*.txt|All files|*.*")), K.Button("Export…", () => _ = Export("settingsExport", "json", "JSON|*.json")));
            v.Children.Add(K.Row(SymbolRegular.ArrowSwap24, "Settings", "Your preferences, grade history and goals, as a file.", buttons));
            v.Children.Add(Foot(engine.OnBrightspace
                ? "Brightspace data is never kept here: only your preferences, grade history and goals, and the course nicknames, colours, tasks and ticks Brightspace has no place for. Your Brightspace account and favourites live on Brightspace."
                : "Canvas data is never kept here: only your preferences, grade history and goals. Your Canvas account, favourites and course nicknames live on Canvas."));
        }
        else v.Children.Add(K.Row(SymbolRegular.Info24, "Not yet", $"Data settings appear once {engine.LmsName} has loaded and you are signed in."));

        v.Children.Add(K.SectionHeader("Reset"));
        var reset = K.Button("Reset everything…", () => Dialogs.Confirm(this, "Erase everything and restart Simpl?",
            $"Your settings, grade history, goals, saved pages, reminders and {engine.LmsName} sign-in are erased from this PC. Simpl then restarts as if it were new. This can’t be undone.",
            "Erase and Restart", () => _ = Erase()));
        v.Children.Add(K.Row(SymbolRegular.ArrowReset24, "Reset", "Erases everything Simpl keeps on this PC, signs you out and restarts Simpl.", reset));
        return v;
    }

    /// <summary>Erase and Restart: a sheet that says it is happening (nothing to press meanwhile), then Simpl again from new.</summary>
    private async Task Erase()
    {
        var s = new Sheet { Title = "Erasing…", Body = K.H(12, K.Spinner(20), K.Text("Simpl restarts in a moment.", Ty.Body)), Modal = true, Width = 360 };
        sheets.Show(s);
        await Task.Delay(300);
        await EraseAll.Run(engine);
    }

    // ---- Updates ----

    private bool versionsAsked;
    private string? versionsProblem;
    private System.Windows.Controls.ProgressBar? downloadBar;
    private TextBlock? downloadWords;

    private FrameworkElement UpdatesPane()
    {
        var dev = Updates.IsDevelopmentRun;
        var found = Updates.Found;
        var v = Column(K.SectionHeader("Simpl for Windows", 0));
        var status = Updates.State switch
        {
            Updates.Mode.Checking => "Checking…",
            Updates.Mode.UpToDate => "You’re up to date",
            Updates.Mode.Available => $"Version {found?.Version} is available",
            Updates.Mode.Downloading => $"Downloading version {found?.Version}…",
            Updates.Mode.Ready => $"Version {found?.Version} is ready to install",
            Updates.Mode.Failed => Updates.Message ?? "Could not check.",
            _ => dev ? "Updates are off in a development run." : "Not checked yet",
        };
        var when = Updates.State switch
        {
            Updates.Mode.Downloading => null,
            Updates.Mode.Ready => "Simpl closes, updates and opens again in a few seconds, your settings and sign-in as they were.",
            _ => Updates.LastCheck is { } last ? "Last checked " + Ago(last) : null,
        };
        // (Download: fetched here for a copy the installer put here, then Restart to Update; else the version's page)
        FrameworkElement action = Updates.State switch
        {
            Updates.Mode.Checking => K.Spinner(20),
            Updates.Mode.Downloading => new Border(),
            Updates.Mode.Available or Updates.Mode.Failed when found != null => K.Button("Download", () => _ = Updates.Download(), accent: true),
            Updates.Mode.Ready => K.Button("Restart to Update", Updates.InstallAndRestart, accent: true),
            _ => Enabled(K.Button("Check for updates", () => _ = Updates.Check()), !dev),
        };
        var words = K.V(2, K.Text(status, Ty.Subtitle), when != null ? K.Wrapped(when, Ty.Caption, B.Text2) : null);
        downloadBar = null;
        downloadWords = null;
        if (Updates.State == Updates.Mode.Downloading)
        {
            // (moved on by Updates.ProgressChanged, not by building the page again)
            downloadBar = new System.Windows.Controls.ProgressBar
            {
                Minimum = 0, Maximum = 100, Value = Updates.Progress * 100, MaxWidth = 360,
                Margin = new Thickness(0, 10, 0, 6), HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            downloadWords = K.Text(DownloadWords(), Ty.Caption, B.Text2);
            words.Children.Add(downloadBar);
            words.Children.Add(downloadWords);
        }
        var icon = Updates.State switch
        {
            Updates.Mode.Available or Updates.Mode.Downloading => SymbolRegular.ArrowDownload24,
            Updates.Mode.Ready => SymbolRegular.ArrowClockwise24,
            Updates.Mode.Failed => SymbolRegular.ErrorCircle24,
            _ => SymbolRegular.ArrowSyncCheckmark24,
        };
        var big = K.Columns("auto, *, auto",
            K.IconTile(icon, Updates.State == Updates.Mode.Failed ? Color.FromRgb(0xe3, 0x6c, 0x0a) : Appearance.Accent, 48).Col(0),
            words.Margin(16, 0, 16, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1),
            action.Col(2));
        v.Children.Add(K.Card(big, new Thickness(20), 4));
        v.Children.Add(new Border { Height = 8 });
        var auto = new ToggleSwitch { IsChecked = Updates.Automatic, IsEnabled = !dev };
        auto.Checked += (_, _) => Updates.Automatic = true;
        auto.Unchecked += (_, _) => Updates.Automatic = false;
        var beta = new ToggleSwitch { IsChecked = Updates.Beta, IsEnabled = !dev };
        beta.Checked += (_, _) => Updates.Beta = true;
        beta.Unchecked += (_, _) => Updates.Beta = false;
        v.Children.Add(K.Group(
            K.Row(SymbolRegular.Info24, "Version", Updates.Installed ? "Installed with Simpl-Setup.exe" : null, K.Text(Updates.Version, Ty.Body, B.Text2)),
            K.Row(SymbolRegular.ArrowSync24, "Check for updates automatically", "Simpl looks for a new version now and then, and says so here when there is one.", auto),
            K.Row(SymbolRegular.Beaker24, "Get beta updates", "Try new features before everyone else. A beta may have rough edges; turn this off to go back to the usual releases when the next one comes out.", beta)));

        // the versions published before
        v.Children.Add(K.SectionHeader("Earlier versions"));
        var list = K.V(4);
        var known = Updates.Known;
        if (!versionsAsked && known == null && !dev) { versionsAsked = true; _ = LoadVersions(); }
        if (known == null) list.Children.Add(versionsProblem != null ? K.Row(SymbolRegular.ErrorCircle24, "Couldn’t read the versions", versionsProblem, K.Button("Try again", () => { versionsProblem = null; versionsAsked = false; Render(); }))
            : K.Row(null, dev ? "Not in a development run" : "Reading the versions…", null, dev ? null : K.Spinner(20)));
        else if (known.Count == 0) list.Children.Add(K.Row(null, "No versions are published yet.", null));
        else foreach (var r in known.Take(12))
        {
            var installed = r.Run == Updates.CurrentRun;
            list.Children.Add(K.Row(SymbolRegular.Box24, $"Simpl {r.Version}{(r.Beta ? " (preview)" : "")}", r.Detail,
                installed ? K.Text("Installed", Ty.Body, B.Text2) : Enabled(K.Button("Open…", () => Updates.Open(r)), !dev)));
        }
        v.Children.Add(list);
        v.Children.Add(Foot(Updates.Installed
            ? "Simpl was installed with Simpl-Setup.exe: a new version downloads here, and Restart to Update installs it. An earlier version opens on its own page."
            : "A version opens on its own page, its downloads there: Simpl-Setup.exe installs Simpl (and from then on, updates install from here), or unzip Simpl-Windows-x64.zip over this copy to take its place."));
        return v;
    }

    /// <summary>"12.4 of 61.2 MB": how far the installer's download has come.</summary>
    private static string DownloadWords()
    {
        var size = Updates.Found?.Setup?.Size ?? 0;
        return size > 0 ? $"{Updates.Progress * size / 1048576.0:0.0} of {size / 1048576.0:0.0} MB" : $"{Updates.Progress * 100:0}%";
    }

    private async Task LoadVersions()
    {
        try { await Updates.Releases(); }
        catch (Exception e) { versionsProblem = e.Message; }
        if (tab == "updates") Render();
    }

    private static string Ago(DateTime at)
    {
        var d = DateTime.UtcNow - at.ToUniversalTime();
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} hr ago";
        return at.ToLocalTime().ToString("MMM d");
    }

    // ---- About ----

    private FrameworkElement AboutPane()
    {
        var icon = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 56, Height = 56 };
        var head = K.Card(K.Columns("auto, *", icon.Col(0),
            K.V(2, K.Text("Simpl", Ty.Subtitle), K.Text($"{engine.LmsName}, as a Windows app.", Ty.Body, B.Text2)).Margin(16, 0, 0, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1)), new Thickness(20), 4);
        var v = Column(head, new Border { Height = 4 },
            K.Group(
                K.Row(SymbolRegular.Info24, "Version", null, K.Text(Updates.Version, Ty.Body, B.Text2)),
                K.Row(SymbolRegular.Code24, "Interface", "The extension’s own scripts, as the Mac and iPhone apps carry them.", K.Text(ScriptBundle.ExtensionVersion, Ty.Body, B.Text2)),
                K.Row(SymbolRegular.Bug24, "Report a bug", "Tell us what went wrong, and what you were doing.", null, () => Engine.Engine.OpenExternally(new Uri("https://simplcourses.com/report/")))));
        v.Children.Add(Foot(Updates.Installed
            ? $"To take Simpl off this PC, uninstall it in Windows’ Settings ▸ Apps ▸ Installed apps. What it stored stays in {AppPaths.Root}: to erase that too, use Data ▸ Reset everything first. Your {engine.LmsName} account is not affected."
            : $"To take Simpl off this PC with everything it stored, sign out here first, then delete Simpl’s folder and {AppPaths.Root}. Your {engine.LmsName} account is not affected."));
        return v;
    }

    // ---- the word at the foot ----

    private int saidVersion;

    public async void Say(string text, bool error = false)
    {
        var n = ++saidVersion;
        saidText.Text = text;
        saidIcon.Symbol = error ? SymbolRegular.ErrorCircle24 : SymbolRegular.CheckmarkCircle24;
        saidIcon.SetResourceReference(Control.ForegroundProperty, error ? B.Critical : B.Success);
        said.Visibility = Visibility.Visible;
        Motion.Enter(said, 0, 16, 300);
        await Task.Delay(3200);
        if (n != saidVersion) return;
        Motion.FadeOut(said, () => { if (n == saidVersion) said.Visibility = Visibility.Collapsed; }, Motion.Normal);
    }
}
