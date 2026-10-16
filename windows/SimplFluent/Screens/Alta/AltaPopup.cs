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
/// assignment's name and Close across the top; down the left, (0.3.5) Light or Dark for Alta's page and Show browser, then its objectives
/// as the quiz's question tiles — the one being worked on filled, a mastered one ticked, the others filling as they are
/// mastered — each by name, and the total mastery bar. In the rest, Alta's own page, the student working on it there: its
/// question alone on panels, its buttons as the popup's own at its foot, its cover as the popup's start screen, its
/// pop-ups as the popup's dialogs, and the popup's skeleton while it loads — all of it in the page's look, light unless
/// the student chose dark; or, with Show browser on, Alta's own page as it is, as in a browser. AltaSession reads it all
/// from the page.
/// </summary>
public static class AltaPopup
{
    // (0.3.21: the moment it was asked for, where the open's timeline starts)
    public static void Show(Engine.Engine engine, AltaLaunch launch, Window over)
    {
        var asked = Stopwatch.GetTimestamp();
        engine.KnowAlta(); // (0.3.22: an account that opens Alta has its next popup's web view made ahead)
        new AltaPopupView(engine, launch, over, asked).Open();
    }
}

internal sealed class AltaPopupView
{
    /// <summary>A pop-up's way out, as Alta words it: Escape presses it.</summary>
    private static readonly HashSet<string> CancelWords = new() { "close", "cancel", "got it", "ok", "okay", "dismiss", "no thanks", "not now", "done" };

    private readonly Window over;
    private readonly Engine.Engine engine;
    private readonly AltaSession session;
    private readonly Grid root = new();
    private readonly ColumnDefinition railColumn = new() { Width = new GridLength(240) };
    private readonly ContentControl tileSlot = new() { Focusable = false };
    private readonly TextBlock title = K.Text("", Ty.Subtitle);
    private readonly TextBlock subtitle = K.Text("", Ty.Body, B.Text2);
    private readonly ContentControl logsSlot = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer rail = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly Border webHolder = new();
    /// <summary>(0.3.5) Where Alta's page and what the popup shows in its place are, dressed in the page's look
    /// (AltaLook.Look): an opaque ground of the look's own under the frame, its corner rounded as the frame's.</summary>
    private readonly Border region = new() { CornerRadius = new CornerRadius(8, 0, 0, 0) };
    private readonly AltaPageBar pageBar;
    private readonly ContentControl startSlot = new() { Focusable = false, Visibility = Visibility.Collapsed };
    private readonly FrameworkElement skeleton = AltaParts.Skeleton();
    private readonly ContentControl stateSlot = new() { Focusable = false, Visibility = Visibility.Collapsed };
    private readonly Border footer = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(16, 12, 16, 12), BorderThickness = new Thickness(0, 1, 0, 0) };
    private readonly AltaLayer layer = new();
    private readonly Dictionary<string, double> drawn = new();
    private Sheet? sheet;
    private readonly FrameworkElement header;
    private string railSig = "", startSig = "", footerSig = "", stateSig = "";
    /// <summary>(2.99.33) The Canvas assignment's due line ("Due Oct 12 at 11:59 PM"), once read: the header's, in place of
    /// the publisher's own words for it.</summary>
    private string? dueLine;
    private int logsCount = -1, dialogShown = -1;
    private bool going, saving, closing, closed;
    private DispatcherTimer? goingBack;
    private System.Drawing.Color? ground;

    public AltaPopupView(Engine.Engine engine, AltaLaunch launch, Window over, long? asked = null)
    {
        this.over = over;
        this.engine = engine;
        // (0.3.22: in the web view made ahead for it, when there is one)
        session = new AltaSession(engine, launch, over, asked, AltaSpare.Take(engine));
        dueLine = launch.Due;
        root.Bg(B.Solid);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        tileSlot.Content = Tile(32);
        title.Text = launch.Title;
        header = AltaLook.Header(engine, tileSlot, title, subtitle, logsSlot, Close, "Close — your progress is kept by Alta");
        root.Children.Add(header.Row(0));

        // the rail under Light or Dark for the page, and Alta's page in its frame (the popup's own in its place while it
        // loads, and its buttons under it)
        var body = new Grid();
        body.ColumnDefinitions.Add(railColumn);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pageBar = new AltaPageBar(session.Dark, session.Browser, SetDark, SetBrowser) { Margin = new Thickness(20, 4, 16, 14) };
        body.Children.Add(K.Rows("auto, *", pageBar.Row(0), rail.Row(1)).Col(0));
        webHolder.Child = session.Web;
        // (clear of the popup's right edge, which sizes it: FloatPopup.EdgeRoom)
        webHolder.Margin = new Thickness(0, 0, FloatPopup.EdgeRoom, 8);
        var area = new Grid { Children = { webHolder, skeleton, startSlot, stateSlot } };
        skeleton.Visibility = Visibility.Collapsed;
        footer.SetResourceReference(Border.BorderBrushProperty, B.Divider);
        var page = K.Rows("*, auto", area.Row(0), footer.Row(1));
        region.Child = AltaLook.Frame(page);
        region.Bg(B.Solid);
        session.Region = region;
        Dress(session.Dark);
        body.Children.Add(region.Col(1));
        root.Children.Add(body.Row(1));
        Grid.SetRowSpan(layer, 2);
        root.Children.Add(layer);
    }

    private FrameworkElement Tile(double size) => K.IconTile(SymbolRegular.BrainCircuit24, AltaLook.Accent, size);

    public void Open()
    {
        sheet = AltaLook.Popup(root, header, Close, "Knewton Alta", "alta");
        // (the rail wider in a wide popup)
        root.SizeChanged += (_, e) => railColumn.Width = new GridLength(e.NewSize.Width >= 960 ? 290 : 240);
        sheet.Closed += Cleanup;
        // (no update goes in while it is open: Support/Updates.cs, the Mac's UpdateHold)
        sheet.Closed += Support.Updates.Hold("Knewton Alta", "when you close Knewton Alta").Dispose;
        session.Changed += Update;
        layer.Gone += Update;
        Support.Appearance.Changed += OnAppearance;
        root.PreviewKeyDown += OnKey;
        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            root.Loaded -= loaded;
            AltaLook.Settle(root);
        };
        root.Loaded += loaded;
        // (0.3.21: begun before the popup shows, where it was begun only once the popup was up — the web view's
        //  environment asked for while the popup is laid out; the web view itself is made only once the popup's window is
        //  there, which WebView2 waits for. The launch was asked already, as the session was made.)
        session.Start();
        Update();
        Dialogs.Show(sheet, over);
        if (sheet.Window is { } w) session.ShowsIn(w);
        if (dueLine == null) ReadDue();
    }

    /// <summary>When it is due, from the Canvas assignment (the same words as its page and every list), for the header —
    /// (0.3.21) only when the screen it was opened from did not say, and only once the launch is on its way, so
    /// the engine answers the launch first.</summary>
    private async void ReadDue()
    {
        await session.Launched;
        if (closed) return;
        if (await AltaLook.Assignment(engine, session.Launch) is not { } a || closed) return;
        dueLine = a.DueLine ?? "";
        Update();
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
        root.PreviewKeyDown -= OnKey;
        Support.Appearance.Changed -= OnAppearance;
        session.Changed -= Update;
        layer.Gone -= Update;
        goingBack?.Stop();
        session.Close();
    }

    private void OnAppearance()
    {
        if (closed) return;
        Dress(session.Dark);
        session.Appearance();
        tileSlot.Content = Tile(32);
        railSig = startSig = footerSig = stateSig = "";
        if (layer.Kind == "alta") dialogShown = -1;
        ground = null;
        Update();
    }

    /// <summary>(0.3.5) Alta's page in light or dark, as the student chose it above the objectives: the page told, and
    /// what the popup shows in its place dressed again.</summary>
    private void SetDark(bool dark)
    {
        if (closed || dark == session.Dark) return;
        Dress(dark);
        session.SetDark(dark);
        pageBar.Show(dark, session.Browser);
        startSig = footerSig = stateSig = "";
        ground = null;
        Update();
    }

    /// <summary>(0.3.5) Show browser, on or off: Alta's own page as it is in place of the question laid out — its buttons,
    /// pop-ups and cover its own, on white — or the question laid out again; the objectives stay.</summary>
    private void SetBrowser(bool on)
    {
        if (closed || on == session.Browser) return;
        session.SetBrowser(on);
        pageBar.Show(session.Dark, on);
        startSig = footerSig = stateSig = "";
        Update();
    }

    /// <summary>(0.3.5) The page's part of the popup dressed in the page's look — nothing of its own when that is the
    /// app's (AltaLook.Look) — before the page's panels are drawn again in it.</summary>
    private void Dress(bool dark) => region.Resources = AltaLook.Look(dark) ?? new ResourceDictionary();

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
        // (1.3.59) the cover as the start screen; (1.3.60) the skeleton while Alta's page is out of sight; (0.3.5) Alta's
        // own page while a pop-up of Alta's is shown in it, and all the while the browser is shown
        var showStart = !s.Browser && s.Cover != null && !s.Focused && s.Dialog == null && !s.DialogInPage && s.Failure == null && !s.Opening;
        var showSkeleton = !s.Browser && !s.PageShown && s.Cover == null && !s.Opening && s.Failure == null;
        UpdateStart(showStart);
        Reveal(skeleton, showSkeleton);
        UpdateState();
        UpdateDialog();
        UpdateFooter();
        // Alta's page in sight only while nothing of the popup's own is in its place — on white when it is shown as it
        // is, else on the popup's ground (its parts' panels drawn on it)
        var asItIs = s.Browser || (!s.Focused && s.PageShown && (s.Cover == null || s.DialogInPage));
        var want = AltaLook.Drawing(asItIs ? Colors.White : AltaLook.Ground(region));
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

    /// <summary>"Knewton Alta · Due Oct 12 at 11:59 PM": the Canvas assignment's due line; until it is read (or where it
    /// cannot be), Alta's own due time written the same way — the publisher's words for it ("Monday, Oct 12 11:59pm PDT")
    /// only as the last resort, and even then read as a date where they can be (AltaLook.DueOf).</summary>
    private string Subtitle()
    {
        var s = session;
        var parts = new List<string> { "Knewton Alta" };
        if (dueLine is { Length: > 0 } line) parts.Add(line);
        else if (dueLine == null && (s.Report?.Due ?? s.Overview?.Due) is { } due)
        {
            try { parts.Add(AltaLook.DueText(DateTimeOffset.FromUnixTimeMilliseconds((long)due).LocalDateTime)); }
            catch { }
        }
        // (the publisher's words read as a date and written as every due line is; failing that, their time tidied and
        //  their time zone left out)
        else if (dueLine == null && s.Overview?.DueText is { Length: > 0 } words) parts.Add(AltaLook.DueOf(words) ?? "Due " + AltaLook.Times(words));
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
                startSlot.Content = AltaParts.StartScreen(c, going, StartCover, session.ReviewCover, session.Dark);
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
        var buttons = s.Focused && !s.Browser ? s.Buttons : new List<AltaButton>();
        var pics = s.Split && s.PageShown ? s.Pics : new List<AltaPic>();
        // (the graphs' Light or Dark while Alta's page is dark — 0.3.5: as chosen for the page, not the app's appearance)
        var dark = s.Dark;
        // (0.3.19) Alta's verdict on the question, Correct or Incorrect, as a pill at the left of the buttons while Alta
        // shows its strip
        var verdict = buttons.Count > 0 ? s.Verdict : "";
        var sig = string.Join("|", buttons) + "#" + pics.Count + "#" + (dark ? s.PicsLight.ToString() : "") + "#" + verdict;
        if (sig == footerSig) return;
        var verdictWas = footerSig.EndsWith("#" + verdict, StringComparison.Ordinal);
        footerSig = sig;
        var show = buttons.Count > 0 || pics.Count > 0;
        var was = footer.Visibility == Visibility.Visible;
        footer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        webHolder.Margin = new Thickness(0, 0, FloatPopup.EdgeRoom, show ? 0 : 8);
        if (!show) { footer.Child = null; return; }
        var left = K.H(8);
        foreach (var p in pics)
        {
            var id = p.Id;
            var b = K.Button(pics.Count > 1 ? $"Graph {id + 1}" : "Open graph", () => _ = session.OpenPic(id), icon: SymbolRegular.ArrowMaximize24);
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
        if (verdict != "")
        {
            var pill = VerdictPill(verdict == "right");
            if (!verdictWas && was) Motion.FadeIn(pill, Motion.Normal);
            right.Children.Add(pill);
        }
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

    /// <summary>(0.3.19) Alta's verdict on the question as the popup's own pill: a green one with a tick and "Correct",
    /// or a red one with a cross and "Incorrect" — the kit's tones, the tint behind, the colour itself for the word.</summary>
    private static Border VerdictPill(bool right)
    {
        var tint = K.Tone(right ? "good" : "bad");
        var fore = K.Darkened(tint);
        var word = right ? "Correct" : "Incorrect";
        var t = K.Text(word, Ty.Body);
        t.Foreground = K.Brush(fore);
        t.FontWeight = FontWeights.SemiBold;
        t.VerticalAlignment = VerticalAlignment.Center;
        var pill = new Border
        {
            Child = K.H(6, K.Icon(right ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.DismissCircle24, 16, fore, filled: true), t),
            Padding = new Thickness(12, 5, 14, 6), CornerRadius = new CornerRadius(16),
            Background = K.Brush(Color.FromArgb(Support.Appearance.Dark ? (byte)56 : (byte)36, tint.R, tint.G, tint.B)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(pill, word);
        return pill;
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
            var (card, escape, enter) = AltaDialogCard(d);
            layer.Show("alta", card, escape, enter);
        }
        else if (layer.Kind == "alta")
        {
            dialogShown = -1;
            layer.Hide();
        }
    }

    /// <summary>
    /// A pop-up of Alta's as a dialog of the popup's (0.3.5: drawn as the app's own words, no longer Alta's picture of it):
    /// its title by Alta's tile; its body as Canvas's rich text is drawn — paragraphs, lists, tables, its formulas typeset,
    /// its pictures as the page made them, its links followed in Alta's page — scrolled when it is long; and its buttons
    /// as the app's, in Alta's order, Alta's main one the accent's (Enter), a greyed-out one greyed out, each pressing
    /// Alta's. Escape presses its close button (×), else its Close, Cancel, Got it or OK; with neither, Escape — and Show
    /// in Alta's page beside its buttons — gives the pop-up back to the page.
    /// </summary>
    private (Border Card, Action Escape, Action? Enter) AltaDialogCard(AltaDialog d)
    {
        var buttons = d.Buttons;
        var close = buttons.FindIndex(b => b.Close);
        var cancel = close >= 0 ? close : buttons.FindIndex(b => CancelWords.Contains(b.Label.Trim().ToLowerInvariant()));
        var shown = Enumerable.Range(0, buttons.Count).Where(i => !buttons[i].Close).ToList();
        var accent = shown.FirstOrDefault(i => buttons[i].Primary && buttons[i].Enabled, -1);
        if (accent < 0) accent = shown.LastOrDefault(i => i != cancel && buttons[i].Enabled, -1);
        var text = new RichText(d.Html, session.DialogLink);
        var scroll = new ScrollViewer
        {
            Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, MaxHeight = Math.Max(200, root.ActualHeight - 260),
        };
        var body = K.V(16, AltaLook.DialogTitle(d.Title.Length == 0 ? "Knewton Alta" : d.Title, Tile(26)), scroll);
        var choices = new List<AltaLook.Choice>();
        if (cancel < 0) choices.Add(new("Show in Alta’s page", session.DialogDismissed));
        foreach (var i in shown)
        {
            var n = i;
            choices.Add(new(AltaParts.ButtonWords(buttons[i].Label), () => session.PressDialog(n), Accent: i == accent, Enabled: buttons[i].Enabled));
        }
        if (shown.Count == 0 && close >= 0) choices.Add(new("Close", () => session.PressDialog(close), Accent: true));
        // (as wide as Windows' own dialogs, wider for a picture that needs it)
        var widest = System.Text.RegularExpressions.Regex.Matches(d.Html, @"<img\b[^>]*\swidth=""(\d+)""").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
        var card = AltaLook.Dialog(body, choices, 380, Math.Clamp(widest + 48, 560, 760));
        Action escape = cancel >= 0 ? () => session.PressDialog(cancel) : session.DialogDismissed;
        Action? enter = accent >= 0 ? () => session.PressDialog(accent) : null;
        return (card, escape, enter);
    }

    // ---- (1.3.47) the Error Logs, to developers ----

    private void UpdateLogs()
    {
        var n = Support.Launch.IsDevelopment ? session.ErrorLogs.Count : 0;
        if (n == logsCount) return;
        logsCount = n;
        if (n == 0) { logsSlot.Content = null; return; }
        var b = K.Button($"Error logs ({n})", ShowLogs, icon: SymbolRegular.Warning24);
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
            AltaLook.DialogTitle("Error logs"),
            K.Wrapped("Alta’s page was not recognised, so it is shown as it is. Send these (with the page code) to get it recognised.", Ty.Caption, B.Text2),
            box);
        var choices = new List<AltaLook.Choice>
        {
            new("Clear", () => { session.ClearErrorLogs(); HideLayer(); }),
            new("Copy", () => { try { Clipboard.SetText(session.ErrorLogText); } catch { } }),
            new("Save with page code", () => { HideLayer(); SaveCode(true); }, Accent: true, Enabled: !saving),
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
            if (!layer.IsUp && footer.Visibility == Visibility.Visible && session.Focused && !session.Browser && session.Buttons.FirstOrDefault(b => b.Primary && b.Enabled) is { } main)
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
