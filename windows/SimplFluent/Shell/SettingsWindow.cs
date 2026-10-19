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
using Adorner = System.Windows.Documents.Adorner;
using AdornerLayer = System.Windows.Documents.AdornerLayer;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;
using PlacementMode = System.Windows.Controls.Primitives.PlacementMode;
using Popup = System.Windows.Controls.Primitives.Popup;
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
/// icons, who is signed in at its head and "Find a setting" under it, which lists the settings that match and takes you
/// to the one picked, lit; the category's title and its rows — Windows 11's settings cards, a symbol, a name over a
/// line that says what it does, and what changes it at the right — on the right, under headers that never say the
/// page's or a row's name again. One window at a time; asked for again, it comes forward on the category asked for.
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
        ("general", "General", SymbolRegular.Settings24, Color.FromRgb(0x76, 0x76, 0x7c), "school sign out change saved sign-in password credential windows hello face fingerprint lock courses goals set up background notification area close taskbar pin uninstall remove delete"),
        ("notifications", "Notifications", SymbolRegular.Alert24, Color.FromRgb(0xe3, 0x6c, 0x0a), "reminders due alerts posts activity announcements"),
        ("grades", "Grades", SymbolRegular.DataBarVertical24, Color.FromRgb(0x88, 0x64, 0xd8), "gpa goal history record what-if track"),
        ("data", "Data", SymbolRegular.Database24, Color.FromRgb(0x00, 0x99, 0xbc), "export import reset erase everything backup encryption encrypted security privacy tpm"),
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
        if (Shot.Asked || Fx.Opaque) SetResourceReference(BackgroundProperty, B.Window);
        if (Shot.Asked) ToolTipService.SetIsEnabled(this, false);
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
        find = new TextBox { PlaceholderText = "Find a setting", Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 }, IconPlacement = ElementPlacement.Right, Margin = new Thickness(16, 0, 16, 12), MinHeight = 34 };
        find.TextChanged += (_, _) => Find(find.Text);
        find.PreviewKeyDown += OnFindKey;
        findPopup = new Popup
        {
            PlacementTarget = find, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border { Child = findList, Padding = new Thickness(4), CornerRadius = new CornerRadius(8), Effect = K.Shadow(24, 6, 0.2), Margin = new Thickness(0, 4, 12, 14) }.Bg(B.Flyout).Stroke(B.FlyoutStroke),
        };
        var pane = K.Rows("auto, auto, *", who.Row(0), find.Row(1), new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }.Row(2));
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(288) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(pane.Col(0));

        // the category: its title, then its rows
        var page = K.Rows("auto, *", titleHost.Margin(40, 4, 40, 20).Row(0), scroller.Row(1));
        scroller.Padding = new Thickness(40, 0, 40, 0);
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
        LoginVault.Changed += OnSignIn;
        Hello.Changed += OnSignIn;
        DevMode.Changed += OnDevMode;
        // (coming forward — back from Windows' Settings, perhaps, where Windows Hello was just set up: asked again)
        Activated += (_, _) => _ = Hello.CheckAsync(fresh: true);
        Closed += (_, _) =>
        {
            Updates.Changed -= OnUpdates;
            Updates.ProgressChanged -= OnProgress;
            Appearance.Changed -= OnAppearance;
            LoginVault.Changed -= OnSignIn;
            Hello.Changed -= OnSignIn;
            DevMode.Changed -= OnDevMode;
            open = null;
            main.Activate();
        };
        SizeChanged += (_, _) => Placement.Save(this, "settings");
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || sheets.IsOpen) return;
            // (Escape puts the matching settings away first, then closes the window)
            if (findPopup.IsOpen) findPopup.IsOpen = false; else Close();
            e.Handled = true;
        };
        Go(tab);
        if (engine.Phase == Phase.Native) _ = LoadInfo();
        // (--settings-sheet uninstall: the screenshot suite's picture of Uninstall Simpl's question; customcolour: Custom
        //  colour's; --settings-find <words>: Find a setting's list, as if typed)
        if (Launch.Take("uninstall", "settings-sheet") != null) Loaded += (_, _) => AskUninstall();
        // (the picture's list stays open while other copies of Simpl, started beside it, take the focus: StaysOpen in a
        //  picture run only — a student's closes as Windows' own does, on a click elsewhere)
        if (Launch.Get("settings-find") is { Length: > 0 } words) Loaded += (_, _) => Dispatcher.BeginInvoke(() => { findPopup.StaysOpen = true; find.Focus(); find.Text = words; find.CaretIndex = words.Length; }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnUpdates() => Dispatcher.BeginInvoke(() => { if (tab == "updates") Render(); });
    private void OnProgress() => Dispatcher.BeginInvoke(() =>
    {
        if (downloadBar != null) downloadBar.Value = Updates.Progress * 100;
        if (downloadWords != null) downloadWords.Text = DownloadWords();
    });
    private void OnDevMode() => Dispatcher.BeginInvoke(() => { if (open == this && tab == "about") Render(); });
    private void OnAppearance() => Dispatcher.BeginInvoke(Pane);
    /// <summary>The saved sign-in kept, locked or forgotten, or Windows Hello set up or not: General and Data say it.</summary>
    private void OnSignIn() => Dispatcher.BeginInvoke(() => { if (open == this && tab is "general" or "data") Render(); });

    private void Pane()
    {
        nav.Set(Tabs.Select(t => new NavEntry { Key = t.Key, Title = t.Title, Icon = t.Icon, Tint = t.Tint, Go = () => Go(t.Key) }), tab);
    }

    // ---- Find a setting (onboarding-27: Windows' own Settings search — the matching settings listed, not a page jumped to) ----

    private readonly TextBox find;
    private readonly Popup findPopup;
    private readonly Stack findList = K.V(2);
    private List<Spot> found = new();
    private int foundAt;

    /// <summary>A setting to be found: its page, its name in the list, the words on its page that mark its row (the
    /// first that is there is the one shown), and other words it answers to.</summary>
    private sealed record Spot(string Tab, string Name, string[] Marks, string Words);

    private IEnumerable<Spot> Spots() => new Spot[]
    {
        new("general", "Change school", new[] { engine.LmsName }, "school canvas brightspace address"),
        new("general", "Sign out", new[] { "Sign out" }, "log out logout"),
        new("general", "Saved sign-in", new[] { "Not saved", "Saved for " }, "password username credential stay logged in save"),
        new("general", "Windows Hello", new[] { "Windows Hello", "Sign-in" }, "face fingerprint pin lock hello"),
        new("general", "Courses shown and goals", new[] { "Courses shown and goals" }, "courses set up setup goals nicknames favourites favorites"),
        new("general", "Keep Simpl running when its window is closed", new[] { "Keep Simpl running when its window is closed" }, "background notification area close tray quit"),
        new("general", "Pin to taskbar", new[] { "Taskbar" }, "taskbar pin"),
        new("general", "Uninstall Simpl", new[] { "Uninstall Simpl" }, "remove delete uninstall"),
        new("notifications", "Due-date reminders", new[] { "Due-date reminders" }, "reminders due alerts notifications"),
        new("notifications", "Remind me", new[] { "Remind me", "Due-date reminders" }, "before hour day reminders"),
        new("notifications", "Alerts for new posts", new[] { "Alerts for new posts" }, "activity announcements grades comments inbox discussions alerts"),
        new("grades", "Keep grade history", new[] { "Keep grade history" }, "history track snapshot"),
        new("grades", "Term GPA goal", new[] { "Term GPA goal" }, "gpa goal target"),
        new("grades", "Show what-if scores", new[] { "Show what-if scores" }, "what-if whatif scores"),
        new("grades", "Import or export grade history", new[] { "Grade history" }, "csv import export history"),
        new("grades", "Record before this term", new[] { "Record before this term" }, "record csv past courses"),
        new("data", "Encryption", new[] { "Encryption" }, "encrypted encryption security privacy tpm"),
        new("data", "Export or import settings", new[] { "Settings" }, "export import backup file settings"),
        new("data", "Erase everything", new[] { "Erase everything" }, "reset erase delete everything"),
        new("updates", "Check for updates", new[] { "Check for updates automatically" }, "update updates check version automatic"),
        new("updates", "Get beta updates", new[] { "Get beta updates" }, "beta preview"),
        new("updates", "Earlier versions", new[] { "Earlier versions" }, "rollback older earlier previous version install"),
        new("about", "Version", new[] { "Version" }, "version interface build about"),
        new("about", "Report a bug", new[] { "Report a bug" }, "bug feedback problem report"),
    };

    /// <summary>The settings matching what is typed (two letters on), listed under the box as Windows' Settings lists
    /// them — a name starting with it first, then one holding it, then one it describes — or "No matching settings".</summary>
    private void Find(string text)
    {
        var q = text.Trim().ToLowerInvariant();
        if (q.Length < 2) { findPopup.IsOpen = false; return; }
        int Rank(Spot s)
        {
            var name = s.Name.ToLowerInvariant();
            if (name.StartsWith(q)) return 0;
            if (name.Contains(q)) return 1;
            if (s.Words.Split(' ').Any(w => w.StartsWith(q))) return 2;
            // (the page's own name or words: each of its settings, after the ones named for it)
            var page = Tabs.First(t => t.Key == s.Tab);
            return page.Title.ToLowerInvariant().StartsWith(q) || page.Words.Split(' ').Any(w => w.StartsWith(q)) ? 3 : 9;
        }
        found = Spots().Select(s => (s, r: Rank(s))).Where(x => x.r < 9).OrderBy(x => x.r).Select(x => x.s).Take(8).ToList();
        foundAt = 0;
        FoundList();
        // (wide enough for a setting's whole name beside its page's — "Check for updates automatically · Updates" — as
        //  Windows Settings' suggestions are, over the pane's edge if need be)
        findPopup.Width = Math.Max(340, find.ActualWidth);
        findPopup.IsOpen = true;
    }

    private void FoundList()
    {
        findList.Children.Clear();
        if (found.Count == 0)
        {
            findList.Children.Add(K.Text("No matching settings", Ty.Body, B.Text2).Margin(10, 8, 10, 8));
            return;
        }
        for (var i = 0; i < found.Count; i++)
        {
            var spot = found[i];
            var name = K.Text(spot.Name, Ty.Body);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var page = K.Text(Tabs.First(t => t.Key == spot.Tab).Title, Ty.Caption, B.Text2);
            page.VerticalAlignment = VerticalAlignment.Center;
            var row = K.Subtle(K.Columns("*, auto", name.Col(0), page.Margin(12, 0, 0, 0).Col(1)), () => Reach(spot), new Thickness(10, 7, 10, 7));
            row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            if (i == foundAt) row.SetResourceReference(Control.BackgroundProperty, B.Subtle);
            System.Windows.Automation.AutomationProperties.SetName(row, $"{spot.Name}, in {Tabs.First(t => t.Key == spot.Tab).Title}");
            findList.Children.Add(row);
        }
    }

    /// <summary>↓ and ↑ move through the settings found, Enter opens the one picked (the field keeps the keyboard).</summary>
    private void OnFindKey(object sender, KeyEventArgs e)
    {
        if (!findPopup.IsOpen || found.Count == 0) return;
        if (e.Key is Key.Down or Key.Up)
        {
            foundAt = Math.Clamp(foundAt + (e.Key == Key.Down ? 1 : -1), 0, found.Count - 1);
            FoundList();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Reach(found[foundAt]);
            e.Handled = true;
        }
    }

    /// <summary>A setting chosen: its page opened, its row scrolled into view (a third of the way down) and lit for a
    /// moment in the accent, so the eye lands on it.</summary>
    private void Reach(Spot spot)
    {
        findPopup.IsOpen = false;
        if (spot.Tab != tab) Go(spot.Tab);
        Dispatcher.BeginInvoke(() =>
        {
            if (scroller.Content is not FrameworkElement page) return;
            page.UpdateLayout();
            FrameworkElement? hit = null;
            foreach (var mark in spot.Marks)
                if ((hit = Marked(page, mark)) != null) break;
            if (hit == null) return;
            var row = RowOf(hit, page);
            try
            {
                var y = row.TranslatePoint(new Point(0, 0), page).Y;
                scroller.ScrollToVerticalOffset(Math.Max(0, y - scroller.ViewportHeight / 3));
            }
            catch { row.BringIntoView(); }
            Flash(row);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The first text in sight on the page that says a mark (all of it; or, for a mark ending in a space, its
    /// start) — not one in a row folded away (Remind me with reminders off).</summary>
    private static FrameworkElement? Marked(DependencyObject at, string mark)
    {
        if (at is TextBlock t && t.IsVisible && (t.Text == mark || (mark.EndsWith(' ') && t.Text.StartsWith(mark, StringComparison.Ordinal)))) return t;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(at); i++)
            if (Marked(VisualTreeHelper.GetChild(at, i), mark) is { } hit) return hit;
        return null;
    }

    /// <summary>The row a text is in — the card, row button or expander standing in its group — or, for a section's
    /// header, the section under it.</summary>
    private static FrameworkElement RowOf(FrameworkElement hit, FrameworkElement page)
    {
        if (VisualTreeHelper.GetParent(hit) is Panel p && ReferenceEquals(p, page))
        {
            var i = p.Children.IndexOf(hit);
            return i + 1 < p.Children.Count && p.Children[i + 1] is FrameworkElement next ? next : hit;
        }
        FrameworkElement e = hit;
        for (var depth = 0; depth < 14 && VisualTreeHelper.GetParent(e) is FrameworkElement up; depth++)
        {
            if (up is Panel && e is Border or Control && e is not TextBox) return e;
            e = up;
        }
        return hit;
    }

    /// <summary>A row lit in the accent (a ring and a tint over it), fading after a moment: an adorner drawn over it,
    /// its opacity alone animated (at once gone, after the moment, with animations off).</summary>
    private static async void Flash(FrameworkElement row)
    {
        if (AdornerLayer.GetAdornerLayer(row) is not { } layer) return;
        var lit = new Lit(row);
        layer.Add(lit);
        await Task.Delay(900);
        if (!Motion.On) { layer.Remove(lit); return; }
        Motion.FromTo(lit, OpacityProperty, 1, 0, 600, Motion.Out, 0, () => layer.Remove(lit));
    }

    /// <summary>The ring and tint of a row found.</summary>
    private sealed class Lit : Adorner
    {
        public Lit(UIElement row) : base(row) { IsHitTestVisible = false; }

        protected override void OnRender(DrawingContext dc)
        {
            var accent = Appearance.Accent;
            var r = new Rect(AdornedElement.RenderSize);
            r.Inflate(1, 1);
            dc.DrawRoundedRectangle(K.Brush(K.WithAlpha(accent, 0.12)), new Pen(K.Brush(accent), 2), r, 6, 6);
        }
    }

    private void Go(string key)
    {
        if (Tabs.All(t => t.Key != key)) key = "general";
        tab = key;
        Pane();
        Render(enter: true);
        scroller.ScrollToTop();
        if (Launch.Get("settings-scroll") is { } s && double.TryParse(s, out var off)) Dispatcher.BeginInvoke(() => scroller.ScrollToVerticalOffset(off), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The page drawn again: sliding in only when you arrive at it (a tab chosen); a change on it (a toggle, an
    /// answer, a check for updates) redraws it in place, where it was scrolled to, with nothing moving.</summary>
    private void Render(bool enter = false)
    {
        var t = Tabs.First(x => x.Key == tab);
        titleHost.Content = K.Text(t.Title, Ty.Title);
        FrameworkElement content = tab switch
        {
            "notifications" => NotificationsPane(),
            "grades" => GradesPane(),
            "data" => DataPane(),
            "updates" => UpdatesPane(),
            "about" => AboutPane(),
            _ => GeneralPane(),
        };
        content.MaxWidth = 1000;
        content.HorizontalAlignment = HorizontalAlignment.Left;
        // (the room at the foot is the pane's own, so it scrolls with it)
        content.Margin = new Thickness(content.Margin.Left, content.Margin.Top, content.Margin.Right, content.Margin.Bottom + 40);
        var first = scroller.Content == null;
        var offset = scroller.VerticalOffset;
        scroller.Content = content;
        if (first || enter) Motion.Enter(content, 0, 20, 360);
        else Dispatcher.BeginInvoke(() => scroller.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);
        Shot.Moved();
    }

    private static Stack Column(params UIElement?[] kids) => K.V(0, kids);

    private static TextBlock Foot(string text) => K.Wrapped(text, Ty.Caption, B.Text2).Margin(2, 8, 2, 0);

    // ---- General ----

    private FrameworkElement GeneralPane()
    {
        var change = K.Button("Change school…", () => { Close(); main.ChangeSchool(); });
        var signOut = K.Button("Sign out…", () => Dialogs.Confirm(this, $"Sign out of {engine.LmsName} on this PC?",
            $"Your {engine.LmsName} session and any saved sign-in are cleared; settings stay.", "Sign out", () => { engine.SignOut(); Close(); }));
        var v = Column(
            K.SectionHeader("School", 0),
            K.Group(
                K.Row(SymbolRegular.BuildingBank24, engine.LmsName, engine.Host, change),
                K.Row(SymbolRegular.SignOut24, "Sign out", $"Your {engine.LmsName} session and any saved sign-in are cleared; settings stay.", signOut)));

        v.Children.Add(SignInSection());

        if (engine.Phase == Phase.Native)
        {
            // (onboarding-1, onboarding-19) one name for what the setup's ticks do, under a header that is not the app's
            // own name
            v.Children.Add(K.SectionHeader("Courses"));
            v.Children.Add(K.Row(SymbolRegular.Target24, "Courses shown and goals", "Which courses Simpl shows, and what you aim for in each.", K.Button("Set up…", () => { engine.AskSetup(); main.Activate(); })));
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

        // (taking Simpl off this PC from here, as Windows' Settings ▸ Apps would: the installer's own uninstaller for the
        // copy it put here; a copy run from its zip is its folder)
        v.Children.Add(K.SectionHeader("This PC"));
        v.Children.Add(UninstallRow());
        return v;
    }

    /// <summary>
    /// Sign-in: the saved sign-in (Support/LoginVault.cs) and how it is locked. Locked with Windows Hello: said so.
    /// Kept in the vault where Windows Hello is set up: Lock with Windows Hello beside Forget…. Nothing saved: not a dead
    /// end (onboarding-5) — Save sign-in… says how (a sign-in is saved from Simpl's card, so Simpl signs out and the card
    /// comes back) and does the signing out. No Windows Hello here: its row says what it would do, its button opens
    /// Windows' own Sign-in options. One line under them (onboarding-17): how a saved sign-in is kept, once there is one.
    /// </summary>
    private FrameworkElement SignInSection()
    {
        var v = Column(K.SectionHeader("Sign-in"));
        var saved = LoginVault.Load();
        var hello = Hello.Supported;
        var locked = saved?.LockedWithHello == true;
        FrameworkElement row;
        if (saved != null)
        {
            var forget = K.Button("Forget…", () => Dialogs.Confirm(this, "Forget the saved sign-in?", "Simpl will ask for your username and password the next time your school does.", "Forget", () => { LoginVault.Clear(); Render(); }));
            UIElement actions = !locked && hello == true ? K.H(8, K.Button("Lock with Windows Hello", () => _ = LockSignIn()), forget) : forget;
            row = K.Row(SymbolRegular.Key24, $"Saved for {saved.User}", $"Sign-in page: {saved.Host}", actions);
        }
        else
        {
            var save = K.Button("Save sign-in…", AskSaveSignIn);
            save.IsEnabled = engine.Phase == Phase.Native;
            row = K.Row(SymbolRegular.Key24, "Not saved", "A saved sign-in lets Simpl sign you in by itself when your school asks again.", save);
        }
        var group = K.Group(row);
        if (!locked && hello == false)
            group.Children.Add(K.Row(SymbolRegular.Fingerprint24, "Windows Hello", "Not set up on this PC. Set it up to lock your saved sign-in behind your face, fingerprint or PIN.",
                K.Button("Sign-in options", () => Engine.Engine.OpenExternally(new Uri("ms-settings:signinoptions")))));
        v.Children.Add(group);
        var foot = locked
            ? "Locked with Windows Hello: Simpl asks for your face, fingerprint or PIN at most once every 30 days. Kept on this PC only, never synced."
            : saved != null ? "Kept on this PC only, encrypted, never synced."
            : hello == true ? "Saved sign-ins stay on this PC only, locked with Windows Hello."
            : "Saved sign-ins stay on this PC only.";
        v.Children.Add(Foot(foot));
        return v;
    }

    /// <summary>Save sign-in…: a sign-in is saved from Simpl's card (Stay logged in), so the way to save one now is to
    /// sign out and sign in again on it — said, and done for the student.</summary>
    private void AskSaveSignIn()
    {
        var kept = Hello.Supported == true ? "kept on this PC, locked with Windows Hello" : "kept on this PC, encrypted";
        Dialogs.Confirm(this, "Save your sign-in?",
            $"Simpl signs you out of {engine.LmsName} on this PC. Sign in again on Simpl’s card and choose Stay logged in: your username and password are then {kept}.",
            "Sign out and sign in again", () => { engine.SignOut(); Close(); });
    }

    /// <summary>Lock with Windows Hello: it asks; locked, said so. Cancelled, nothing is said; refused, why.</summary>
    private async Task LockSignIn()
    {
        if (busy) return;
        busy = true;
        var result = await LoginVault.LockAsync(LoginVault.LockReason);
        busy = false;
        if (open != this) return; // (Settings closed meanwhile)
        if (result == HelloResult.Success) Say("Your saved sign-in is locked with Windows Hello.");
        else if (result != HelloResult.Canceled) Say("Windows Hello couldn’t lock your saved sign-in on this PC.", true);
        if (tab == "general") Render();
    }

    /// <summary>Uninstall Simpl: Uninstall… for the installed copy (off in a development run, as Updates are); for a copy
    /// run from its zip, its folder, deleting which is uninstalling it.</summary>
    private FrameworkElement UninstallRow()
    {
        if (!Updates.Installed && !Updates.IsDevelopmentRun)
            return K.Row(SymbolRegular.Delete24, "Uninstall Simpl",
                "This copy wasn’t installed with Simpl-Setup.exe, so it’s removed by deleting its folder once Simpl is quit.",
                K.Button("Open folder", () => OpenFolder(AppPaths.AppDir), icon: SymbolRegular.FolderOpen24));
        return K.Row(SymbolRegular.Delete24, "Uninstall Simpl",
            Updates.IsDevelopmentRun ? "Uninstalling is off in a development run." : "Removes Simpl from this PC, as Windows’ Settings ▸ Apps does; you choose whether your settings and sign-in go too.",
            Enabled(K.Button("Uninstall…", AskUninstall), Uninstall.Available));
    }

    /// <summary>Open folder: a folder in File Explorer (this copy's, or where Simpl keeps what it stores).</summary>
    private void OpenFolder(string dir)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir.TrimEnd('\\', '/')}\"") { UseShellExecute = true }); }
        catch (Exception e) { Say("Could not open the folder: " + e.Message, true); }
    }

    /// <summary>
    /// Uninstall…: Windows 11's question — Simpl removed from this PC, and closing now — with what it keeps kept unless
    /// the box says otherwise; Uninstall in the critical colour and never the default, Cancel the way out. Where what it
    /// keeps lives is a link that opens the folder (onboarding-29), not a path printed in the dialog.
    /// </summary>
    private void AskUninstall()
    {
        const string kept = "Left unticked, they’re kept on this PC, encrypted, in case you reinstall Simpl.";
        var erased = $"Erased from this PC first, with your goals, saved pages and flashcards, and Simpl’s encryption key. This can’t be undone. Your {engine.LmsName} account is not affected.";
        var note = K.Wrapped(kept, Ty.Caption, B.Text2);
        var where = K.Link("Open folder", () => OpenFolder(AppPaths.Root), Ty.Caption);
        where.HorizontalAlignment = HorizontalAlignment.Left;
        where.ToolTip = "Where Simpl keeps your settings, grade history and saved sign-in";
        const string eraseWords = "Also erase my settings, grade history and saved sign-in";
        var erase = new CheckBox { Content = K.Wrapped(eraseWords, Ty.Body, B.Text), IsChecked = false };
        System.Windows.Automation.AutomationProperties.SetName(erase, eraseWords);
        erase.Checked += (_, _) => { note.Text = erased; where.Visibility = Visibility.Collapsed; };
        erase.Unchecked += (_, _) => { note.Text = kept; where.Visibility = Visibility.Visible; };
        var s = new Sheet
        {
            Title = "Uninstall Simpl?",
            Body = K.V(12,
                K.Wrapped("Simpl will be removed from this PC, with its shortcuts and its entry in Windows’ Settings ▸ Apps. Simpl closes now; the uninstaller shows its progress.", Ty.Body, B.Text),
                erase, K.V(2, note, where)),
        };
        s.Add("Uninstall", SheetButtonKind.Primary, () => _ = UninstallNow(erase.IsChecked == true)).Danger = true;
        s.Add("Cancel", SheetButtonKind.Close);
        sheets.Show(s);
    }

    /// <summary>Uninstall pressed: a sheet that says it is happening (nothing to press meanwhile), then Simpl gone and its
    /// uninstaller at work. The uninstaller could not start: said, and Simpl stays as it was.</summary>
    private async Task UninstallNow(bool erase)
    {
        var s = new Sheet { Title = "Uninstalling…", Body = K.H(12, K.Spinner(20), K.Text(erase ? "Erasing what Simpl keeps, then Simpl closes." : "Simpl closes in a moment.", Ty.Body)), Modal = true, Width = 360 };
        sheets.Show(s);
        await Task.Delay(300);
        if (await Uninstall.Run(engine, erase) is not { } problem) return;
        s.Close();
        Say(problem, true);
    }

    private async Task PinToTaskbar()
    {
        // (asked for here: a No from Windows, which may be Windows not asking at all, is followed by Simpl's dialog)
        var answer = await Taskbar.Ask(this, asked: true);
        if (open != this) return; // (Settings closed meanwhile)
        if (answer == Taskbar.Answer.AlreadyPinned) Say("Simpl is on your taskbar already.");
        else if (answer == Taskbar.Answer.Pinned) Say("Simpl is on your taskbar.");
        if (tab == "general") Render();
    }

    // (0.3.31) Appearance is no longer a page here: the account menu's Appearance… (Shell/AppearanceMode.cs).

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
        // (onboarding-19: the switch named for what it does, not the header's words again)
        v.Children.Add(K.Group(K.Row(SymbolRegular.Megaphone24, "Alerts for new posts", "When something is posted in one of your courses.", act), kindsCard));
        // (onboarding-20: what both kinds share said once, under the last of them)
        var foot = "Simpl looks about every 3 minutes while it runs, with its window closed too. Reminders and alerts are both worked out on this PC: nothing is sent anywhere.";
        if (engine.OnBrightspace) foot = "Brightspace has no activity stream for Simpl to read: alerts for new posts are for Canvas schools. " + foot;
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
        // (onboarding-20: two decimals always, "4.00"; kept half up on its decimal value, as the engine keeps it)
        // (three places kept by the box, so it never rounds a typed 3.425 its own way first: GoalFigure rounds it, and shows two)
        var goal = new NumberBox { Value = GoalFigure.Value(i.Goal), Minimum = 0, Maximum = 4, SmallChange = 0.05, LargeChange = 0.5, MaxDecimalPlaces = 3, Width = 140, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, NumberFormatter = new GoalFigure.Formatter() };
        var goalTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        goalTimer.Tick += async (_, _) =>
        {
            goalTimer.Stop();
            var g = GoalFigure.Value(goal.Value ?? 4);
            try { await engine.Call<SettingsInfo>("settingsSave", new { goal = g }); i.Goal = g; }
            catch (Exception e) { Say("Could not save the goal: " + e.Message, true); }
        };
        goal.ValueChanged += (_, _) => { goalTimer.Stop(); goalTimer.Start(); };
        var whatIf = new ToggleSwitch { IsChecked = i.WhatIf };
        whatIf.Checked += (_, _) => { i.WhatIf = true; Save(new { whatIf = true }); };
        whatIf.Unchecked += (_, _) => { i.WhatIf = false; Save(new { whatIf = false }); };
        // (onboarding-19: no header saying "Grades" again under the page of that name; onboarding-20: the switch called
        //  what the setup calls it)
        var v = Column(
            K.Group(
                K.Row(SymbolRegular.DataTrending24, "Keep grade history", "One snapshot a day, on this PC.", track),
                K.Row(SymbolRegular.Target24, "Term GPA goal", "What the Grades page measures your term against.", goal),
                K.Row(SymbolRegular.Calculator24, "Show what-if scores", "Try a score on any assignment to see what it would do to your grade.", whatIf)));

        v.Children.Add(K.SectionHeader("Grade history"));
        var hist = K.H(8, K.Button("Import…", () => Import("historyImport", "CSV|*.csv;*.txt|All files|*.*")), Enabled(K.Button("Export…", () => _ = Export("historyExport", "csv", "CSV|*.csv")), i.Days > 0));
        // (with Keep grade history off, the row says no more than its name: the switch above already says it keeps one
        //  snapshot a day — onboarding-20; on, its line says since when)
        v.Children.Add(K.Row(SymbolRegular.History24, i.History, i.Tracking && i.HistoryNote.Length > 0 ? i.HistoryNote : null, hist));
        v.Children.Add(Foot("Import a grade-history CSV you exported earlier; days you already have are kept."));

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

    /// <summary>Data ▸ Encryption's words, plain (onboarding-21: no cipher's or mechanism's name): Simpl's data is
    /// encrypted and who can open it (Support/Vault.cs), then how the saved sign-in is locked (Support/LoginVault.cs) —
    /// nothing of that while Windows has not yet said whether Windows Hello is set up.</summary>
    private static string EncryptionWords()
    {
        var data = Vault.Protection switch
        {
            VaultProtection.Tpm => "Your Simpl data on this PC is encrypted and can only be opened on this PC, from your Windows account.",
            VaultProtection.WindowsAccount => "Your Simpl data on this PC is encrypted and can only be opened from your Windows account.",
            _ => "Your Simpl data on this PC is encrypted, but Windows couldn’t keep its key, so what Simpl stores lasts only while it runs.",
        };
        var saved = LoginVault.Load();
        var signIn = saved?.LockedWithHello == true ? "Your saved sign-in is locked with Windows Hello."
            : Hello.Supported switch
            {
                true when saved == null => "A sign-in you save is locked with Windows Hello.",
                true => "Your saved sign-in isn’t locked with Windows Hello yet: lock it in General ▸ Sign-in.",
                false when saved == null => "A sign-in you save is encrypted the same way: Windows Hello isn’t set up on this PC.",
                false => "Your saved sign-in is encrypted the same way: Windows Hello isn’t set up on this PC.",
                null => null,
            };
        return signIn == null ? data : data + " " + signIn;
    }

    private FrameworkElement DataPane()
    {
        // (onboarding-19: no header saying "Data" again under the page of that name)
        var v = Column();
        // (what Simpl keeps here is encrypted, and what holds its key: Support/Vault.cs; the saved sign-in's lock)
        var encryption = K.Row(SymbolRegular.ShieldLock24, "Encryption", EncryptionWords());
        if (engine.Phase == Phase.Native)
        {
            var buttons = K.H(8, K.Button("Import…", () => Import("settingsImport", "JSON|*.json;*.txt|All files|*.*")), K.Button("Export…", () => _ = Export("settingsExport", "json", "JSON|*.json")));
            v.Children.Add(K.Group(encryption, K.Row(SymbolRegular.ArrowSwap24, "Settings", "Your preferences, grade history and goals, as a file.", buttons)));
            v.Children.Add(Foot(engine.OnBrightspace
                ? "Brightspace data is never kept here: only your preferences, grade history and goals, and the course nicknames, colours, tasks and ticks Brightspace has no place for. Your Brightspace account and favourites live on Brightspace."
                : "Canvas data is never kept here: only your preferences, grade history and goals. Your Canvas account, favourites and course nicknames live on Canvas."));
        }
        else v.Children.Add(K.Group(encryption, K.Row(SymbolRegular.Info24, "Not yet", $"Data settings appear once {engine.LmsName} has loaded and you are signed in.")));

        // (onboarding-19: the row named for what it does, not the header's word again)
        v.Children.Add(K.SectionHeader("Reset"));
        var reset = K.Button("Erase…", () => Dialogs.Confirm(this, "Erase everything and restart Simpl?",
            $"Your settings, grade history, goals, saved pages, reminders and {engine.LmsName} sign-in are erased from this PC. Simpl then restarts as if it were new. This can’t be undone.",
            "Erase and restart", () => _ = Erase()));
        v.Children.Add(K.Row(SymbolRegular.ArrowReset24, "Erase everything", "Erases everything Simpl keeps on this PC, signs you out and restarts Simpl.", reset));
        return v;
    }

    /// <summary>Erase and restart: a sheet that says it is happening (nothing to press meanwhile), then Simpl again from new.</summary>
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
        var dev = Updates.Off;
        var found = Updates.Found;
        var critical = Updates.Critical && found != null;
        // (it goes in by itself: a Critical Update always; another while updates are not paused and it is not one an
        //  earlier version put back stays back from — 0.3.7: Install updates automatically is always on)
        var byItself = Updates.InstallsHere && Updates.GoesInByItself;
        // (a beta says so, as Earlier versions' rows do: "Version v113 (beta) is available")
        var name = found is { Beta: true } ? $"{found.Version} (beta)" : found?.Version;
        var v = Column(K.SectionHeader("Simpl for Windows", 0));
        var status = Updates.State switch
        {
            Updates.Mode.Checking => "Checking…",
            Updates.Mode.UpToDate => "You’re up to date",
            Updates.Mode.Available => critical ? $"Critical update {name} is available" : $"Version {name} is available",
            Updates.Mode.Downloading => $"Downloading version {name}…",
            Updates.Mode.Ready when byItself => critical ? $"Critical update {name} is ready" : $"Version {name} is ready",
            Updates.Mode.Ready => $"Version {name} is ready to install",
            Updates.Mode.Installing => $"Installing version {name}…",
            Updates.Mode.Failed => Updates.Message ?? "Could not check.",
            _ => dev ? "Updates are off in a development run." : "Not checked yet",
        };
        var when = Updates.State switch
        {
            Updates.Mode.Downloading => byItself ? "It installs by itself once it’s here, and Simpl opens again where you were." : null,
            Updates.Mode.Ready when byItself => Updates.Waiting is { When: { } w }
                ? $"Simpl updates {w}, and opens again where you were."
                : "Simpl updates at the next quiet moment, and opens again where you were.",
            Updates.Mode.Ready => "Simpl closes, updates and opens again in a few seconds, your settings and sign-in as they were.",
            Updates.Mode.Installing => "Simpl closes, and opens again by itself where you were.",
            Updates.Mode.Available when Updates.HeldForMetered => "This PC is on a metered connection: Simpl waits for another to download it, or choose Download.",
            Updates.Mode.Failed when found != null && byItself => "Simpl tries again in a while." + (Updates.LastCheck is { } l ? " Last checked " + Ago(l) : ""),
            _ => Updates.LastCheck is { } last ? "Last checked " + Ago(last) : null,
        };
        if (critical && Updates.State is Updates.Mode.Available or Updates.Mode.Downloading or Updates.Mode.Ready)
            when = (when != null ? when + " " : "") + "A critical update installs whatever these settings say.";
        if (Updates.Paused && !dev) when = (when != null ? when + " " : "") + "Updates are paused (--pause-updates): only a critical update installs by itself.";
        // (Download: fetched here for a copy the installer put here, then Restart to Update; else the version's page.
        //  Update now, for one an earlier version put back stays back from: fetched and put in at once, the stay let go)
        FrameworkElement action = Updates.State switch
        {
            Updates.Mode.Checking or Updates.Mode.Installing => K.Spinner(20),
            Updates.Mode.Downloading => new Border(),
            Updates.Mode.Available or Updates.Mode.Failed when found != null && Updates.KeptBack && Updates.InstallsHere => K.Button("Update now", () => _ = Updates.UpdateNow(), accent: true),
            Updates.Mode.Available or Updates.Mode.Failed when found != null => K.Button("Download", () => _ = Updates.Download(), accent: true),
            Updates.Mode.Ready => K.Button("Restart to update", Updates.InstallAndRestart, accent: true),
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
            _ when critical && Updates.State != Updates.Mode.Failed => SymbolRegular.Warning24,
            Updates.Mode.Available or Updates.Mode.Downloading => SymbolRegular.ArrowDownload24,
            Updates.Mode.Ready or Updates.Mode.Installing => SymbolRegular.ArrowClockwise24,
            Updates.Mode.Failed => SymbolRegular.ErrorCircle24,
            _ => SymbolRegular.ArrowSyncCheckmark24,
        };
        var orange = Updates.State == Updates.Mode.Failed || critical;
        var big = K.Columns("auto, *, auto",
            K.IconTile(icon, orange ? Color.FromRgb(0xe3, 0x6c, 0x0a) : Appearance.Accent, 48).Col(0),
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
            K.Row(SymbolRegular.ArrowSync24, "Check for updates automatically", "Simpl looks for a new version when it opens and every hour, for a critical update every minute, and says so here when there is one.", auto),
            // (0.3.7: where Install updates automatically was — an update always installs itself now: an earlier version
            //  put back says it stays, and until when; a copy run from its zip says why it can't put a version in)
            StayingRow(found) ?? (Updates.Installed || dev ? null
                : K.Row(SymbolRegular.ArrowDownload24, "Installing updates", "This copy wasn’t installed with Simpl-Setup.exe, so it can’t put a new version in its own place: Download opens the version’s page.")),
            K.Row(SymbolRegular.Beaker24, "Get beta updates", "Get test versions of Simpl before everyone else. A beta may have rough edges. Turn this off to stop getting betas: Simpl keeps the version you have until a newer regular version comes out.", beta)));

        // the versions published before (onboarding-6): folded away, as the Mac's are; for the copy the installer put here,
        // each one installs in this one's place (the Mac's Install…), else opens on the website
        v.Children.Add(new Border { Height = 4 });
        var list = K.V(4);
        var known = Updates.Known;
        if (!versionsAsked && known == null && !dev) { versionsAsked = true; _ = LoadVersions(); }
        if (known == null) list.Children.Add(versionsProblem != null ? K.Row(SymbolRegular.ErrorCircle24, "Couldn’t read the versions", versionsProblem, K.Button("Try again", () => { versionsProblem = null; versionsAsked = false; Render(); }))
            : K.Row(null, dev ? "Not in a development run" : "Reading the versions…", null, dev ? null : K.Spinner(20)));
        else if (!known.Any(r => r.Run <= Updates.CurrentRun)) list.Children.Add(K.Row(null, "No earlier versions are published.", null));
        // (this one and those before it: a newer one is the update above, never put in from here)
        else foreach (var r in known.Where(r => r.Run <= Updates.CurrentRun).Take(12))
        {
            var installed = r.Run == Updates.CurrentRun;
            var inPlace = Updates.Installed && r.Setup != null;
            list.Children.Add(K.Row(SymbolRegular.Box24, $"Simpl {r.Version}{(r.Beta ? " (beta)" : "")}", r.Detail,
                installed ? K.Text("Installed", Ty.Body, B.Text2)
                : inPlace ? Enabled(K.Button("Install…", () => AskInstallEarlier(r)), !dev)
                : Enabled(K.Button("Open…", () => Updates.Open(r)), !dev)));
        }
        var earlier = new CardExpander
        {
            Header = K.V(0, K.Text("Earlier versions", Ty.Body), K.Text(Updates.Installed ? "Put an earlier version back in this one’s place." : "Each opens on the website, with its downloads.", Ty.Caption, B.Text2)),
            Icon = new SymbolIcon { Symbol = SymbolRegular.History24 },
            Content = list,
            IsExpanded = Launch.Flag("settings-earlier"),
        };
        v.Children.Add(earlier);
        // (a copy from a zip: the expander's own line already says where earlier versions open — no footnote saying it
        //  again)
        if (Updates.Installed) v.Children.Add(Foot("Updates install by themselves at a quiet moment — never while a quiz, a tool or a dialog is open — and are checked first. After you install an earlier version, Simpl stays on it until a newer version comes out or you choose Update now."));
        return v;
    }

    /// <summary>
    /// "Staying on 0.3.5 (v130)" (0.3.7): an earlier version put back stays (Updates.StayingFrom), in the row where
    /// Install updates automatically was — what doesn't come back by itself, and what does; Update now is the card's
    /// button. Null when Simpl stays on nothing, or for a copy the installer didn't put here.
    /// </summary>
    private static FrameworkElement? StayingRow(Updates.Release? found)
    {
        if (!Updates.Installed || Updates.StayingFrom is not { } from) return null;
        var back = $"v{from:000}";
        var words = Updates.KeptBack && found != null
            ? $"You went back to this version, so {found.Version} doesn’t install by itself: choose Update now to install it. A version newer than {back} installs by itself, as usual."
            : Updates.Critical && found != null
                ? $"You went back to this version, but {found.Version} is a critical update: it installs by itself all the same."
                : $"You went back to this version, so {back} doesn’t install by itself. A version newer than it does, as usual.";
        return K.Row(SymbolRegular.History24, $"Staying on {Updates.Version}", words);
    }

    /// <summary>"12.4 of 61.2 MB": how far the installer's download has come.</summary>
    private static string DownloadWords()
    {
        var size = Updates.Found?.Setup?.Size ?? 0;
        return size > 0 ? $"{Updates.Progress * size / 1048576.0:0.0} of {size / 1048576.0:0.0} MB" : $"{Updates.Progress * 100:0}%";
    }

    /// <summary>Install… on an earlier version (the Mac's): asked first — Simpl closes and opens again on it, and stays
    /// there (0.3.7: Updates.StayingFrom, the version it goes back from not put in again by itself) — then downloaded,
    /// checked and put in, as an update is.</summary>
    private void AskInstallEarlier(Updates.Release r)
    {
        // (the version Simpl stays back from: this one, or the one an earlier stay went back from when that is newer)
        var back = $"v{Math.Max(Updates.StayingFrom ?? 0, Updates.CurrentRun):000}";
        Dialogs.Confirm(this, $"Install Simpl {r.Version}?",
            $"Simpl closes and opens again on {r.Version}, your settings and sign-in as they were. It stays on {r.Version} until a version newer than {back} comes out, or you choose Update now in Settings ▸ Updates.",
            "Install", async () =>
            {
                if (await Updates.InstallEarlier(r) is { } problem && open == this) Say(problem, true);
            });
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
        // (dates as every date is written: "Oct 12", the year only when it is not this one)
        var local = at.ToLocalTime();
        return local.ToString(local.Year == DateTime.Now.Year ? "MMM d" : "MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---- About ----

    private FrameworkElement AboutPane()
    {
        var icon = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 56, Height = 56 };
        var head = K.Card(K.Columns("auto, *", icon.Col(0),
            K.V(2, K.Text("Simpl", Ty.Subtitle), K.Text($"{engine.LmsName}, as a Windows app.", Ty.Body, B.Text2)).Margin(16, 0, 0, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1)), new Thickness(20), 4);
        var v = Column(head, new Border { Height = 4 },
            K.Group(
                // (onboarding-21: one row, the interface's version said in it — not a row of its own explaining itself)
                // (0.3.27) seven clicks on it within four seconds: the developer tools (Support/DevMode)
                K.Row(SymbolRegular.Info24, "Version", null, K.Text(VersionLine, Ty.Body, B.Text2), DevMode.Tap, chevron: false),
                DevMode.On ? K.Row(SymbolRegular.WindowDevTools24, "Developer tools", "Logs, system use, stats, the engine’s calls and preferences.", null, () => DeveloperWindow.Show(main, engine)) : null,
                K.Row(SymbolRegular.Bug24, "Report a bug", "Tell us what went wrong, and what you were doing.", null, () => Engine.Engine.OpenExternally(new Uri("https://simplcourses.com/report/")))));
        // (onboarding-29: where the way out is — there is no Sign out on this page — and no folder's path printed)
        v.Children.Add(Foot(Updates.Installed
            ? $"To take Simpl off this PC, use General ▸ Uninstall Simpl…, which can erase what it stored too; or uninstall it in Windows’ Settings ▸ Apps ▸ Installed apps, which keeps that. Your {engine.LmsName} account is not affected."
            : $"To take Simpl off this PC with everything it stored, use Data ▸ Erase everything… first (it signs you out too), then quit Simpl and delete its folder (General ▸ Uninstall Simpl ▸ Open folder). Your {engine.LmsName} account is not affected."));
        return v;
    }

    /// <summary>"0.3.0 (v060) · interface 2.99.33": the app's version and the version of the extension's scripts it
    /// carries, in one row (the Mac's).</summary>
    private static string VersionLine =>
        ScriptBundle.ExtensionVersion is { Length: > 0 } ext ? $"{Updates.Version} · interface {ext}" : Updates.Version;

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
