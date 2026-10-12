using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Shell;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// (Mac 1.3.17) A Knewton Alta assignment in a popup over the window, as a quiz is taken (the Mac's AltaQuizScreen): the
/// assignment's name and Close across the top; down the left, its objectives as the quiz's question tiles — the one
/// being worked on filled, a mastered one ticked, the others filling as they are mastered — each by name, and the total
/// mastery bar. In the rest, Alta's own page, the student working on it there: its question alone on panels, its buttons
/// as the popup's own at its foot, its cover as the popup's start screen, its pop-ups as the popup's dialogs, and the
/// popup's skeleton while it loads. AltaSession reads it all from the page.
/// </summary>
public static class AltaPopup
{
    public static void Show(Engine.Engine engine, AltaLaunch launch, Window over) => new AltaPopupView(engine, launch, over).Open();
}

internal sealed class AltaPopupView
{
    /// <summary>A pop-up's way out, as Alta words it: Escape presses it.</summary>
    private static readonly HashSet<string> CancelWords = new() { "close", "cancel", "got it", "ok", "okay", "dismiss", "no thanks", "not now", "done" };

    private readonly Window over;
    private readonly AltaSession session;
    private readonly Grid root = new();
    private readonly ColumnDefinition railColumn = new() { Width = new GridLength(240) };
    private readonly ContentControl tileSlot = new() { Focusable = false };
    private readonly TextBlock title = K.Text("", Ty.Subtitle);
    private readonly TextBlock subtitle = K.Text("", Ty.Body, B.Text2);
    private readonly ContentControl logsSlot = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer rail = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly Border webHolder = new();
    private readonly ContentControl startSlot = new() { Focusable = false, Visibility = Visibility.Collapsed };
    private readonly FrameworkElement skeleton = AltaParts.Skeleton();
    private readonly ContentControl stateSlot = new() { Focusable = false, Visibility = Visibility.Collapsed };
    private readonly Border footer = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(16, 12, 16, 12), BorderThickness = new Thickness(0, 1, 0, 0) };
    private readonly AltaLayer layer = new();
    private readonly Dictionary<string, double> drawn = new();
    private Sheet? sheet;
    private string railSig = "", startSig = "", footerSig = "", stateSig = "";
    private int logsCount = -1, dialogShown = -1;
    private bool going, saving, closing, closed;
    private DispatcherTimer? goingBack;
    private System.Drawing.Color? ground;

    public AltaPopupView(Engine.Engine engine, AltaLaunch launch, Window over)
    {
        this.over = over;
        session = new AltaSession(engine, launch, over);
        root.Bg(B.Solid);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        tileSlot.Content = Tile(32);
        title.Text = launch.Title;
        root.Children.Add(AltaLook.Header(tileSlot, title, subtitle, logsSlot, Close, "Close — your progress is kept by Alta").Row(0));

        // the rail, and Alta's page in its frame (the popup's own in its place while it loads, and its buttons under it)
        var body = new Grid();
        body.ColumnDefinitions.Add(railColumn);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(rail.Col(0));
        webHolder.Child = session.Web;
        webHolder.Margin = new Thickness(0, 0, 0, 8);
        var area = new Grid { Children = { webHolder, skeleton, startSlot, stateSlot } };
        skeleton.Visibility = Visibility.Collapsed;
        footer.SetResourceReference(Border.BorderBrushProperty, B.Divider);
        var page = K.Rows("*, auto", area.Row(0), footer.Row(1));
        body.Children.Add(AltaLook.Frame(page).Col(1));
        root.Children.Add(body.Row(1));
        Grid.SetRowSpan(layer, 2);
        root.Children.Add(layer);
    }

    private FrameworkElement Tile(double size) => K.IconTile(SymbolRegular.Brain24, AltaLook.Accent, size);

    public void Open()
    {
        sheet = AltaLook.Popup(root, over);
        railColumn.Width = new GridLength(sheet.Width >= 960 ? 290 : 240);
        sheet.Closed += Cleanup;
        session.Changed += Update;
        layer.Gone += Update;
        Support.Appearance.Changed += OnAppearance;
        over.PreviewKeyDown += OnKey;
        // (begun once the popup is up: it may wait its turn behind another sheet)
        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            root.Loaded -= loaded;
            AltaLook.Settle(root);
            session.Start(Support.Appearance.Dark);
        };
        root.Loaded += loaded;
        Update();
        Dialogs.Show(sheet, over);
    }

    /// <summary>Close (the button, Escape): the page goes first — nothing of the app's can fade a web view out — then the popup.</summary>
    private void Close()
    {
        if (closing) return;
        closing = true;
        session.Web.Visibility = Visibility.Collapsed;
        session.ClosePic();
        if (sheet != null) sheet.Close();
        else Cleanup();
    }

    private void Cleanup()
    {
        if (closed) return;
        closed = true;
        closing = true;
        over.PreviewKeyDown -= OnKey;
        Support.Appearance.Changed -= OnAppearance;
        session.Changed -= Update;
        layer.Gone -= Update;
        goingBack?.Stop();
        session.Close();
    }

    private void OnAppearance()
    {
        if (closed) return;
        session.Appearance(Support.Appearance.Dark);
        tileSlot.Content = Tile(32);
        railSig = startSig = footerSig = stateSig = "";
        if (layer.Kind == "alta") dialogShown = -1;
        ground = null;
        Update();
    }

    // ---- what shows, as the session says ----

    private bool updating, again;

    private void Update()
    {
        if (updating) { again = true; return; }
        updating = true;
        try
        {
            do { again = false; UpdateNow(); } while (again && !closing);
        }
        finally { updating = false; }
    }

    private void UpdateNow()
    {
        if (closing) return;
        var s = session;
        title.Text = s.Report?.Name ?? s.Overview?.Name ?? s.Launch.Title;
        subtitle.Text = Subtitle();
        UpdateLogs();
        UpdateRail();
        // (1.3.59) the cover as the start screen; (1.3.60) the skeleton while Alta's page is out of sight
        var showStart = s.Cover != null && !s.Focused && s.Dialog == null && s.Failure == null && !s.Opening;
        var showSkeleton = !s.PageShown && s.Cover == null && !s.Opening && s.Failure == null;
        UpdateStart(showStart);
        Reveal(skeleton, showSkeleton);
        UpdateState();
        UpdateDialog();
        UpdateFooter();
        // Alta's page in sight only while nothing of the popup's own is in its place — on white when it is shown as it
        // is, else on the popup's ground (its parts' panels drawn on it)
        var asItIs = !s.Focused && s.PageShown && s.Cover == null;
        var want = AltaLook.Drawing(asItIs ? Colors.White : AltaLook.Ground());
        if (ground != want) { ground = want; s.Web.DefaultBackgroundColor = want; }
        var covered = showStart || showSkeleton || s.Failure != null || s.Opening || layer.Covers;
        var vis = covered ? Visibility.Hidden : Visibility.Visible;
        if (s.Web.Visibility != vis) s.Web.Visibility = vis;
    }

    private static void Reveal(UIElement e, bool show)
    {
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        if (e.Visibility == vis) return;
        e.Visibility = vis;
        if (show) Motion.FadeIn(e, Motion.Medium);
    }

    private string Subtitle()
    {
        var s = session;
        var parts = new List<string> { "Knewton Alta" };
        if ((s.Report?.Due ?? s.Overview?.Due) is { } due)
        {
            try
            {
                var d = DateTimeOffset.FromUnixTimeMilliseconds((long)due).LocalDateTime;
                parts.Add($"Due {d.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} at {d.ToString("t", CultureInfo.CurrentCulture)}");
            }
            catch { }
        }
        else if (s.Overview?.DueText is { Length: > 0 } words) parts.Add("Due " + words);
        if ((s.Report?.Current.Source ?? "").ToUpperInvariant() == "PRACTICE") parts.Add("Practice");
        return string.Join(" · ", parts);
    }

    private void UpdateRail()
    {
        var s = session;
        var list = s.Objectives;
        var m = s.Mastery;
        var est = s.Estimate;
        var reported = s.Report != null;
        var sig = string.Join("|", list.Select(o => $"{o.Id}\u0001{o.Number}\u0001{o.Name}\u0001{o.Mastery:0.0000}\u0001{o.Current}")) + $"#{m:0.0000}#{est}#{reported}";
        if (sig == railSig) return;
        railSig = sig;
        rail.Content = AltaParts.Rail(list, m, est, reported, drawn);
        // (what was drawn, for the bars to fill from next time)
        drawn.Clear();
        foreach (var o in list) drawn[o.Id] = o.Mastery;
        drawn[""] = m;
    }

    // ---- the start screen ----

    private void UpdateStart(bool show)
    {
        if (!show && going)
        {
            // (Alta's page moved on: the next start screen begins afresh)
            going = false;
            goingBack?.Stop();
        }
        if (show && session.Cover is { } c)
        {
            var sig = JsonSerializer.Serialize(c) + going;
            if (sig != startSig)
            {
                startSig = sig;
                startSlot.Content = AltaParts.StartScreen(c, going, StartCover, session.ReviewCover);
            }
        }
        Reveal(startSlot, show);
    }

    private void StartCover()
    {
        if (going) return;
        going = true;
        session.StartCover();
        // (back to the button if Alta's page did not move on)
        goingBack?.Stop();
        goingBack = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        goingBack.Tick += (_, _) =>
        {
            goingBack?.Stop();
            going = false;
            Update();
        };
        goingBack.Start();
        Update();
    }

    // ---- opening, or why it could not be ----

    private void UpdateState()
    {
        var s = session;
        var sig = s.Failure != null ? "fail:" + s.Failure : s.Opening ? "opening" : "";
        if (sig == stateSig) return;
        stateSig = sig;
        stateSlot.Content = s.Failure is { } why ? AltaLook.Failure("Knewton Alta could not be opened", why)
            : s.Opening ? AltaLook.Opening("Opening Knewton Alta…") : null;
        Reveal(stateSlot, sig.Length > 0);
    }

    // ---- the parts' buttons, and the small graphs' ----

    /// <summary>
    /// (1.3.53) The parts' buttons, whatever they say, as the popup's own at the foot of Alta's page — Alta's main one
    /// the accent's (Ctrl+Enter), a greyed-out one greyed out — each pressing Alta's; (1.3.70) and beside them, while a
    /// question has small graphs, a button to open each large and, in dark mode, Light or Dark for their colours. (The
    /// Mac floats them over the page; nothing can be drawn over a web view, so they sit under it.)
    /// </summary>
    private void UpdateFooter()
    {
        var s = session;
        var buttons = s.Focused ? s.Buttons : new List<AltaButton>();
        var pics = s.Split && s.PageShown ? s.Pics : new List<AltaPic>();
        var dark = Support.Appearance.Dark;
        var sig = string.Join("|", buttons) + "#" + pics.Count + "#" + (dark ? s.PicsLight.ToString() : "");
        if (sig == footerSig) return;
        footerSig = sig;
        var show = buttons.Count > 0 || pics.Count > 0;
        var was = footer.Visibility == Visibility.Visible;
        footer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        webHolder.Margin = new Thickness(0, 0, 0, show ? 0 : 8);
        if (!show) { footer.Child = null; return; }
        var left = K.H(8);
        foreach (var p in pics)
        {
            var id = p.Id;
            var b = K.Button(pics.Count > 1 ? $"Graph {id + 1}" : "Open Graph", () => _ = session.OpenPic(id), icon: SymbolRegular.ArrowMaximize24);
            b.ToolTip = "Open the graph large, in a window you can move";
            left.Children.Add(b);
        }
        if (pics.Count > 0 && dark)
        {
            var light = s.PicsLight;
            left.Children.Add(K.IconButton(light ? SymbolRegular.WeatherMoon24 : SymbolRegular.WeatherSunny24, () => session.SetPicsLight(!light),
                light ? "Graphs in dark colours" : "Graphs in their own light colours"));
        }
        var right = K.H(8);
        foreach (var b in buttons)
        {
            var id = b.Id;
            var btn = K.Button(b.Label, () => session.PressButton(id), accent: b.Primary);
            btn.MinWidth = 86;
            btn.IsEnabled = b.Enabled;
            btn.ToolTip = b.Enabled ? $"{b.Label} (Alta’s own button{(b.Primary ? ", Ctrl+Enter" : "")})" : "Not ready yet: answer the question first";
            ToolTipService.SetShowOnDisabled(btn, true);
            right.Children.Add(btn);
        }
        footer.Child = K.Columns("auto, *, auto", left.Col(0), right.Align(HorizontalAlignment.Right).Col(2));
        if (!was) Motion.FadeIn(footer, Motion.Normal);
    }

    // ---- (1.3.51) Alta's pop-ups as the popup's own ----

    private void UpdateDialog()
    {
        var s = session;
        if (s.Dialog is { } d)
        {
            if (layer.Kind is "logs" or "saved") return; // (the student's own first; Alta's waits for it)
            if (dialogShown == s.DialogVersion && layer.Kind == "alta") return;
            dialogShown = s.DialogVersion;
            var cancel = d.Buttons.FindIndex(b => CancelWords.Contains(b.ToLowerInvariant()));
            var last = d.Buttons.Count - 1;
            layer.Show("alta", AltaDialogCard(d, cancel),
                escape: () => { if (cancel >= 0) session.PressDialog(cancel); else session.DialogDismissed(); },
                enter: last >= 0 && last != cancel ? () => session.PressDialog(last) : null);
        }
        else if (layer.Kind == "alta")
        {
            dialogShown = -1;
            layer.Hide();
        }
    }

    /// <summary>A pop-up of Alta's as a dialog of the popup's: Alta's own picture of it, and its buttons as the app's —
    /// each pressing Alta's. Escape presses its Close, Got it or OK, if it has one, else gives the pop-up back to the page.</summary>
    private Border AltaDialogCard(AltaDialog d, int cancel)
    {
        var body = K.V(16, AltaLook.DialogTitle(d.Title.Length == 0 ? "Knewton Alta" : d.Title, Tile(26)));
        if (d.Picture is { } pic)
        {
            var img = new Image
            {
                Source = pic, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = Math.Min(d.PictureSize.Width > 0 ? d.PictureSize.Width : 640, 640), MaxHeight = Math.Min(d.PictureSize.Height > 0 ? d.PictureSize.Height : 560, 560),
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            img.SizeChanged += (_, e) => img.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 8, 8);
            body.Children.Add(img);
        }
        else if (d.Text.Length > 0) body.Children.Add(AltaLook.Selectable(d.Text));
        var choices = new List<AltaLook.Choice>();
        if (cancel < 0) choices.Add(new("Show in Alta’s Page", session.DialogDismissed));
        for (var i = 0; i < d.Buttons.Count; i++)
        {
            var n = i;
            choices.Add(new(d.Buttons[i], () => session.PressDialog(n), Accent: i == d.Buttons.Count - 1 && i != cancel));
        }
        return AltaLook.Dialog(body, choices, 380, 700);
    }

    // ---- (1.3.47) the Error Logs, to developers ----

    private void UpdateLogs()
    {
        var n = Support.Launch.IsDevelopment ? session.ErrorLogs.Count : 0;
        if (n == logsCount) return;
        logsCount = n;
        if (n == 0) { logsSlot.Content = null; return; }
        var b = K.Button($"Error Logs ({n})", ShowLogs, icon: SymbolRegular.Warning24);
        b.ToolTip = "Alta’s page was not recognised, so it is shown as it is: see why";
        logsSlot.Content = b;
        Motion.FadeIn(b);
    }

    /// <summary>Each time Alta's page was not recognised — when, why, what it held — to copy, or to save with the page's code.</summary>
    private void ShowLogs()
    {
        var text = AltaLook.Selectable(session.ErrorLogText, 11, new FontFamily("Cascadia Mono, Consolas"));
        var box = new Border { Child = new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Height = 280, Padding = new Thickness(8), CornerRadius = new CornerRadius(8) }.Bg(B.Subtle);
        var body = K.V(12,
            AltaLook.DialogTitle("Error Logs"),
            K.Wrapped("Alta’s page was not recognised, so it is shown as it is. Send these (with the page code) to get it recognised.", Ty.Caption, B.Text2),
            box);
        var choices = new List<AltaLook.Choice>
        {
            new("Clear", () => { session.ClearErrorLogs(); HideLayer(); }),
            new("Copy", () => { try { Clipboard.SetText(session.ErrorLogText); } catch { } }),
            new("Save with Page Code", () => { HideLayer(); SaveCode(true); }, Accent: true, Enabled: !saving),
        };
        layer.Show("logs", AltaLook.Dialog(body, choices, 480, 480), HideLayer, null);
        Update();
    }

    private void HideLayer()
    {
        layer.Hide();
        Update();
    }

    private async void SaveCode(bool withLogs)
    {
        if (saving) return;
        saving = true;
        var file = await session.SavePageCode(withLogs);
        saving = false;
        if (closing) return;
        if (file == null) { System.Media.SystemSounds.Beep.Play(); return; }
        var body = K.V(12,
            AltaLook.DialogTitle("Page code saved"),
            K.Wrapped($"Every bit of Alta’s page code — the page, each frame in it, and the scripts and style sheets they load — is in Downloads as “{Path.GetFileName(file)}”.", Ty.Body));
        var choices = new List<AltaLook.Choice>
        {
            new("Show in File Explorer", () => { HideLayer(); try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true }); } catch { } }),
            new("OK", HideLayer, Accent: true),
        };
        layer.Show("saved", AltaLook.Dialog(body, choices, 400, 520), HideLayer, HideLayer);
        Update();
    }

    // ---- the keyboard ----

    /// <summary>
    /// Escape: the popup's dialog's way out, else Close (the page keeps its own Escape while it has the keyboard: a
    /// keypad of Alta's, a menu). Enter: the dialog's default, or Start on the start screen. Ctrl+Enter: Alta's main button.
    /// </summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (closing || !root.IsVisible) return;
        var inPage = ReferenceEquals(e.OriginalSource, session.Web) || ReferenceEquals(Keyboard.FocusedElement, session.Web);
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (e.Key == Key.Escape)
        {
            if (layer.IsUp) { layer.Escape?.Invoke(); e.Handled = true; return; }
            if (inPage) return;
            Close();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        if (ctrl)
        {
            if (!layer.IsUp && footer.Visibility == Visibility.Visible && session.Focused && session.Buttons.FirstOrDefault(b => b.Primary && b.Enabled) is { } main)
            {
                session.PressButton(main.Id);
                e.Handled = true;
            }
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None || inPage || Keyboard.FocusedElement is ButtonBase) return;
        if (layer.IsUp)
        {
            if (layer.Enter is { } go) { go(); e.Handled = true; }
            return;
        }
        if (startSlot.Visibility == Visibility.Visible && !going)
        {
            StartCover();
            e.Handled = true;
        }
    }
}
