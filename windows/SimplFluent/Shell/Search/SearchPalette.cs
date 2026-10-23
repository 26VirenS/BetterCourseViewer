using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Shell;

/// <summary>
/// The search as the web's box afloat (0.3.56; the extension's search.js and hub.js, the Mac's FloatingSearch.swift): a
/// press on the navigation pane's search box — or Ctrl+F, Ctrl+K, the title bar's Search on a narrow window — brings a
/// large field over the top of the window with its results under it, and nothing else changes: no screen of its own, the
/// page stays where it is. Before anything is typed, the four kinds to search in (Courses, Work, Files, Actions:
/// Ctrl+1 to Ctrl+4); a kind chosen sits in the field as a chip and lists its own things at once — your courses, what is
/// due this week, the files changed last, every command — what is typed then searching that kind alone. Each letter is
/// answered at once from what the app and the engine's index hold (searchLocal), and by the school once the typing pauses
/// (search): a sum worked out, the commands whose names it starts, your courses (and a course's section: "bio files"),
/// your groups, then assignments, announcements, pages, discussions, files and people. "/" (or ">") lists every command,
/// narrowing as its name is typed; a command that takes something (/course, /grades, /due, /open …) lists what it can be
/// once a space follows its name. The first row is chosen as you type: Enter runs it, ↑ ↓ walk the rows, Tab completes a
/// command, Escape steps back (the kind, then the search goes), Backspace on an empty field lets the kind go; a press
/// anywhere else puts it away.
/// </summary>
public sealed partial class SearchPalette : IDisposable
{
    /// <summary>The widest the panel grows, and how far under the title bar it sits.</summary>
    public const double PanelWidth = 680, Top = 40;

    private readonly TextBox box;
    private readonly Engine.Engine engine;
    private readonly MainWindow window;
    private readonly Grid layer;
    private readonly Grid panel;
    private readonly Border chip;
    private readonly TextBlock chipText;
    private readonly FrameworkElement spin;
    private readonly Border divider;
    private readonly ScrollViewer scroller;
    private readonly FrameworkElement foot;
    private readonly Stack list = new() { Orientation = Orientation.Vertical, Spacing = 2 };
    private readonly List<(PaletteItem Item, Button Row, Border Pill)> rows = new();
    private List<PaletteSection> sections = new();
    private string? chosen;
    private bool commandMode;
    private SearchScope? scope;
    /// <summary>The screenshot suite's search, shown whether or not its field holds the keyboard.</summary>
    private bool forced;
    private int seq;
    private CancellationTokenSource? lookup;
    private string lastRaw = "";
    /// <summary>Where the pointer was last seen over the list: a row is chosen by the pointer only when it moves, never
    /// because rows came up under a pointer resting there (WPF says the pointer entered them) — Enter would open another.</summary>
    private Point? pointer;

    /// <summary>The palette of the window showing (the Search screen's command buttons run through it).</summary>
    public static SearchPalette? Shared { get; private set; }

    /// <summary>A row ran: the pane, over the page while the window is narrow, can close.</summary>
    public event Action? Ran;

    public bool IsOpen => layer.Visibility == Visibility.Visible;

    /// <summary>The floating search, over the page (the window puts it in its layers, under its sheets).</summary>
    public FrameworkElement Layer => layer;

    /// <summary>The search for this window: its field, its kinds and commands, its results.</summary>
    public static SearchPalette Attach(Engine.Engine engine, MainWindow window)
    {
        var p = new SearchPalette(engine, window);
        Shared = p;
        return p;
    }

    private SearchPalette(Engine.Engine engine, MainWindow window)
    {
        this.engine = engine;
        this.window = window;

        // the field: Windows 11's search box, large, with the kind searched in as a chip before it and a ring while
        // the school is asked
        box = new TextBox
        {
            PlaceholderText = Placeholder, Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 }, IconPlacement = ElementPlacement.Left,
            ClearButtonEnabled = false, FontSize = 18, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center,
        };
        chipText = K.Text("", Ty.BodyStrong, B.OnAccent);
        chip = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 8, 0), Child = chipText, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center }.Bg(B.Accent);
        spin = K.Spinner(16).Margin(10, 0, 4, 0);
        spin.Visibility = Visibility.Collapsed;
        var field = K.Columns("auto, *, auto", chip.Col(0), box.Col(1), spin.Col(2));
        field.Margin = new Thickness(12, 12, 12, 10);

        divider = new Border { Height = 1, Visibility = Visibility.Collapsed }.Bg(B.Divider);
        scroller = new ScrollViewer
        {
            Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, Padding = new Thickness(0, 4, 0, 6), Visibility = Visibility.Collapsed,
        };
        System.Windows.Automation.AutomationProperties.SetName(list, "Results");
        foot = K.Text("↑ ↓ to choose · Enter to open · Tab to complete · Esc to close", Ty.Caption, B.Text3).Margin(16, 6, 16, 10);
        var face = new Grid { RowDefinitions = { new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto } } };
        face.Children.Add(field.Row(0));
        face.Children.Add(divider.Row(1));
        face.Children.Add(scroller.Row(2));
        face.Children.Add(foot.Row(3));

        // the panel: Windows 11's flyout surface — its fill, its hairline, rounded 8, raised on a soft shadow (drawn by a
        // copy of the surface behind it, so the words keep their crisp ClearType)
        var shade = new Border { CornerRadius = new CornerRadius(8), Effect = K.Shadow(32, 8, 0.26) }.Bg(B.Flyout);
        var surface = new Border { CornerRadius = new CornerRadius(8), Child = face, SnapsToDevicePixels = true }.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        panel = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Top, 0, 0), Children = { shade, surface } };

        // (a press beside the panel puts it away, and goes no further: the page under it is not pressed too)
        var catcher = new Border { Background = Brushes.Transparent };
        catcher.MouseDown += (_, e) => { e.Handled = true; Hide(); };
        layer = new Grid { Visibility = Visibility.Collapsed, Children = { catcher, panel } };
        layer.SizeChanged += (_, _) => Fit();

        box.TextChanged += OnText;
        box.PreviewKeyDown += OnKey;
        box.LostKeyboardFocus += OnBlur;
        engine.PropertyChanged += OnEngine;
        LaunchList();
    }

    private string Placeholder => scope != null ? $"Search {scope.Title.ToLowerInvariant()}" : $"Search {engine.LmsName} or type /";

    /// <summary>The search brought up, holding what it held (or <paramref name="text"/>), its field given the keyboard.</summary>
    public void Show(string? text = null, bool selectAll = false)
    {
        var opening = !IsOpen;
        if (opening)
        {
            pointer = null;
            layer.Visibility = Visibility.Visible;
            Fit();
            // (the engine's index taken now, so the first letters answer from it)
            _ = engine.Act("searchWarm");
        }
        if (text != null && box.Text != text) SetText(text);
        else if (opening || text != null) Update(box.Text);
        FocusBox(selectAll);
        Shot.Moved();
    }

    /// <summary>Search Commands (Ctrl+K): the field with "/" in it, every command listed.</summary>
    public void OpenCommands()
    {
        if (scope != null) SetScope(null);
        if (box.Text == "/") Show(); else Show("/");
    }

    /// <summary>Put away: emptied, and searching everything again next time.</summary>
    public void Hide()
    {
        forced = false;
        if (!IsOpen) return;
        lookup?.Cancel();
        lookup = null;
        seq++;
        layer.Visibility = Visibility.Collapsed;
        scope = null;
        ShowScope();
        if (box.Text.Length > 0) SetText("");
        spin.Visibility = Visibility.Collapsed;
        if (box.IsKeyboardFocusWithin)
        {
            window.Focus();
            if (box.IsKeyboardFocusWithin) Keyboard.ClearFocus();
        }
    }

    /// <summary>Escape: a step back — the kind let go of, else the search put away. False when it was not up.</summary>
    public bool Escape()
    {
        if (!IsOpen) return false;
        if (scope != null) SetScope(null); else Hide();
        return true;
    }

    /// <summary>Ctrl+1 to Ctrl+4 while the search is up: that kind searched in. False when it is not up.</summary>
    public bool ScopeKey(int index)
    {
        if (!IsOpen || index < 0 || index >= SearchScope.All.Count) return false;
        SetScope(SearchScope.All[index]);
        return true;
    }

    private void SetScope(SearchScope? s)
    {
        scope = s;
        ShowScope();
        // (a kind chosen from the list starts its search afresh; a command typed gives way to it)
        if (s != null && IsCommand(box.Text)) SetText(""); else Update(box.Text);
        FocusBox(false);
    }

    private void ShowScope()
    {
        chip.Visibility = scope != null ? Visibility.Visible : Visibility.Collapsed;
        chipText.Text = scope?.Title ?? "";
        box.PlaceholderText = Placeholder;
    }

    // ---- the field's moves ----

    private void OnText(object? sender, TextChangedEventArgs e)
    {
        var raw = box.Text;
        if (engine.Query != raw) engine.Query = raw;
        Update(raw);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                Submit();
                e.Handled = true;
                break;
            case Key.Tab:
                // (a command completed — "/gr" to "/grades " — and the keys kept in the search)
                if (Chosen() is { Action.Do: PaletteDo.Fill } fill) Perform(fill.Action, fromField: true);
                e.Handled = true;
                break;
            case Key.Back:
                if (box.Text.Length == 0 && scope != null) { SetScope(null); e.Handled = true; }
                break;
        }
    }

    private void OnBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (forced || !IsOpen) return;
        if (e.NewFocus is DependencyObject d && Within(layer, d)) return;
        Hide();
    }

    /// <summary>The keyboard is in the search's field (<paramref name="focused"/> is it, or in it): a live check waits
    /// for it only while keys go into it.</summary>
    public bool Holds(DependencyObject focused) => Within(box, focused);

    private void OnEngine(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Engine.Engine.LmsName):
                box.PlaceholderText = Placeholder;
                break;
            case nameof(Engine.Engine.Courses) or nameof(Engine.Engine.Groups) or nameof(Engine.Engine.Sections):
                // (the courses, the groups or a course's sections came in: the search up says so)
                if (IsOpen) Update(box.Text, keepChoice: true);
                break;
        }
    }

    private PaletteItem? Chosen() => chosen != null ? rows.FirstOrDefault(r => r.Item.Id == chosen).Item : null;

    /// <summary>Enter: the row chosen runs (the first that does something, as you type); a command typed whose list is
    /// still to come, its first row once it has.</summary>
    private void Submit()
    {
        if (Chosen() is { } picked) { Perform(picked.Action, fromField: true); return; }
        if (box.Text.Trim().Length == 0) return;
        if (lastRaw != box.Text) Update(box.Text);
        if (sections.SelectMany(s => s.Items).FirstOrDefault(i => i.Action.Acts) is { } first) Perform(first.Action, fromField: true);
    }

    // ---- the results for what the field holds ----

    /// <summary>The results for what the field holds now: what the app holds at once, the engine's index at once, what it
    /// must ask the school for after a pause — each put up as it comes.</summary>
    private void Update(string raw, bool keepChoice = false)
    {
        seq++;
        var mine = seq;
        lookup?.Cancel();
        lookup = null;
        lastRaw = raw;
        commandMode = Typed.Of(raw).Command;
        var (built, job) = Suggest(raw);
        spin.Visibility = job?.Kind == JobKind.Everything ? Visibility.Visible : Visibility.Collapsed;
        Publish(built, keepChoice);
        if (job == null) return;
        var cts = lookup = new CancellationTokenSource();
        Later(job, built, mine, cts.Token);
    }

    private async void Later(Job job, List<PaletteSection> based, int mine, CancellationToken token)
    {
        if (job.Kind == JobKind.Everything)
        {
            try { await Everything(job, based, () => !token.IsCancellationRequested && seq == mine, token); }
            catch { /* cancelled: a newer search answers */ }
            if (seq == mine) spin.Visibility = Visibility.Collapsed;
            return;
        }
        PaletteSection? extra;
        try { extra = await Run(job, token); }
        catch { return; }
        if (token.IsCancellationRequested || seq != mine || extra == null) return;
        Publish(Merge(based, extra), keepChoice: true);
        Shot.Moved();
    }

    /// <summary>The sections shown (each row's id its own), and the row chosen: kept where it still is, else the first
    /// that does something (what Enter runs) — a sum's answer before it.</summary>
    private void Publish(List<PaletteSection> built, bool keepChoice)
    {
        var seen = new HashSet<string>();
        var outList = new List<PaletteSection>();
        foreach (var s in built)
        {
            if (s.Items.Count == 0) continue;
            var items = new List<PaletteItem>();
            foreach (var it in s.Items)
            {
                var id = it.Id;
                for (var n = 2; !seen.Add(id); n++) id = $"{it.Id}#{n}";
                items.Add(id == it.Id ? it : it with { Id = id });
            }
            outList.Add(new PaletteSection(s.Title, items));
        }
        sections = outList;
        var acting = outList.SelectMany(s => s.Items).Where(i => i.Action.Acts).Select(i => i.Id).ToList();
        if (!(keepChoice && chosen != null && acting.Contains(chosen))) chosen = acting.Contains("answer") ? "answer" : acting.FirstOrDefault();
        Render(fresh: !keepChoice);
    }

    // ---- the list ----

    /// <summary>The list drawn again: a new one from its top, one that answered later where it was.</summary>
    private void Render(bool fresh)
    {
        // (the pointer where it is now counts as seen: rows drawn under it do not take the choice)
        pointer = IsOpen ? Mouse.GetPosition(layer) : null;
        list.Children.Clear();
        rows.Clear();
        var first = true;
        foreach (var s in sections)
        {
            list.Children.Add(K.Text(s.Title, Ty.BodyStrong).Margin(16, first ? 6 : 14, 16, 4));
            first = false;
            foreach (var it in s.Items) list.Children.Add(Row(it));
        }
        var any = sections.Count > 0;
        scroller.Visibility = divider.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        Highlight(scroll: false);
        if (fresh) scroller.ScrollToTop();
    }

    /// <summary>A row (Windows 11's list item): its symbol — or a course's square — what it is (a command's name with what
    /// it takes after it as grey placeholders) over the line after it, a shortcut or a score at its end; the one chosen
    /// filled, with the accent's pill at its left. Pointed at, it is the one chosen.</summary>
    private FrameworkElement Row(PaletteItem it)
    {
        var grid = K.Columns("auto, *, auto", Symbol(it).Col(0));
        var name = K.Text(it.Title, Ty.Body, it.Action.Acts ? B.Text : B.Text2);
        if (it.Args is { Length: > 0 } args)
        {
            var placeholders = new System.Windows.Documents.Run("  " + args);
            placeholders.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, B.Text3);
            name.Inlines.Add(placeholders);
        }
        var words = K.V(0, name);
        if (it.Sub is { Length: > 0 } sub) words.Children.Add(K.Text(sub, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(words.Margin(12, 0, 0, 0).Col(1));
        if (it.Trailing is { Length: > 0 } t) grid.Children.Add(K.Text(t, Ty.Caption, B.Text2).Margin(16, 0, 2, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2));
        if (!it.Action.Acts)
            return new Border { Child = grid, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(4, 0, 4, 0), MinHeight = 40 };
        var pill = new Border
        {
            Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Hidden, Margin = new Thickness(-12, 0, 0, 0),
        };
        pill.SetResourceReference(Border.BackgroundProperty, B.Accent);
        var item = it;
        var b = K.Subtle(new Grid { Children = { grid, pill } }, () => Perform(item.Action, fromField: true), new Thickness(12, 8, 12, 8));
        // (the field keeps the keyboard while a row is pressed)
        b.Focusable = false;
        b.MinHeight = 40;
        b.Margin = new Thickness(4, 0, 4, 0);
        b.ToolTip = it.Title.Length > 60 ? it.Title : null;
        b.MouseMove += (_, e) =>
        {
            var at = e.GetPosition(layer);
            if (pointer == at) return;
            pointer = at;
            if (chosen == item.Id) return;
            chosen = item.Id;
            Highlight(scroll: false);
        };
        var said = it.Args is { Length: > 0 } a2 ? $"{it.Title} {a2.Replace('‹', ' ').Replace('›', ' ').Trim()}" : it.Title;
        System.Windows.Automation.AutomationProperties.SetName(b, it.Sub is { Length: > 0 } s2 ? $"{said}, {s2}" : said);
        rows.Add((item, b, pill));
        return b;
    }

    private static FrameworkElement Symbol(PaletteItem it)
    {
        FrameworkElement g;
        if (it.Id == "looking") g = K.Spinner(16);
        else if (it.Square)
        {
            var letter = new TextBlock
            {
                Text = Initial(it.Title), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, FontFamily = K.SmallFace,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            g = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Background = K.Brush(K.Hex(it.Color, Color.FromRgb(0x00, 0x78, 0xd4))), Child = letter };
        }
        else if (it.Color is { Length: > 0 } c) g = K.Icon(it.Symbol, 16, K.Hex(c), it.Filled);
        else g = K.Icon(it.Symbol, 16, B.AccentText, it.Filled);
        g.VerticalAlignment = VerticalAlignment.Center;
        g.HorizontalAlignment = HorizontalAlignment.Center;
        return new Grid { Width = 20, Children = { g } };
    }

    /// <summary>A course's letter on its square (the navigation pane's): the first letter after a code like "BIO101 ".</summary>
    private static string Initial(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s ?? "", @"^(?:[A-Za-z]{1,3}\d{2,4}[-_ :]+)?(?<rest>.*)$");
        var rest = m.Success ? m.Groups["rest"].Value : s ?? "";
        var ch = rest.FirstOrDefault(char.IsLetter);
        if (ch == default) ch = (s ?? "").FirstOrDefault(char.IsLetterOrDigit);
        return ch != default ? char.ToUpperInvariant(ch).ToString() : "•";
    }

    private void Highlight(bool scroll)
    {
        foreach (var (it, row, pill) in rows)
        {
            var on = it.Id == chosen;
            if (on) row.SetResourceReference(Control.BackgroundProperty, B.Subtle); else row.ClearValue(Control.BackgroundProperty);
            pill.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            if (on && scroll) row.BringIntoView();
        }
    }

    /// <summary>↑ and ↓: the next row that does something, held at either end.</summary>
    private void Move(int step)
    {
        if (rows.Count == 0) return;
        var i = chosen == null ? -1 : rows.FindIndex(r => r.Item.Id == chosen);
        var next = i < 0 ? (step > 0 ? 0 : rows.Count - 1) : Math.Clamp(i + step, 0, rows.Count - 1);
        chosen = rows[next].Item.Id;
        Highlight(scroll: true);
        if (next == 0) scroller.ScrollToTop();
    }

    /// <summary>As wide as the window spares (at most <see cref="PanelWidth"/>), the list no taller than about two thirds
    /// of the window.</summary>
    private void Fit()
    {
        var w = layer.ActualWidth > 0 ? layer.ActualWidth : window.ActualWidth;
        var h = layer.ActualHeight > 0 ? layer.ActualHeight : window.ActualHeight;
        panel.Width = Math.Clamp(w - 48, 280, PanelWidth);
        scroller.MaxHeight = Math.Max(160, Math.Min(h * 0.62, h - Top - 140));
    }

    // ---- running one ----

    /// <summary>A row's action. From the search (<paramref name="fromField"/>), it is put away first, so the keys go to the
    /// screen — but for a command completed, a kind chosen or words searched, which stay in the search.</summary>
    public void Perform(PaletteAction a, bool fromField)
    {
        switch (a.Do)
        {
            case PaletteDo.Nothing:
                return;
            case PaletteDo.Fill:
                // (the field keeps the keyboard, listing what the command takes)
                Show(a.Text);
                return;
            case PaletteDo.Search:
                // (everything for the words, in the search itself: no screen of its own)
                Show(a.Text);
                return;
            case PaletteDo.Scope:
                if (!IsOpen) Show();
                SetScope(SearchScope.Of(a.Text));
                return;
        }
        if (fromField) Hide();
        switch (a.Do)
        {
            case PaletteDo.Go: if (a.To is { } place) engine.Go(place); break;
            case PaletteDo.Open: engine.OpenFromSearch(a.Text, a.Title, a.External); break;
            case PaletteDo.Work:
                if (a.Text.Length > 0) engine.OpenWeb(a.Text, a.Title); else engine.Go(Place.Todo);
                break;
            case PaletteDo.NewTask: engine.AskNewTask(); break;
            case PaletteDo.AddTask: AddTask(a.Text); break;
            case PaletteDo.NewMessage: engine.AskNewMessage(); break;
            case PaletteDo.Refresh: engine.Refresh(); break;
            case PaletteDo.ReloadPage: engine.Reload(); break;
            case PaletteDo.Settings: window.OpenSettings(); break;
            case PaletteDo.WhatsNew: engine.ShowWhatsNew(WhatsNew.All); break;
            case PaletteDo.Setup: engine.AskSetup(); break;
            case PaletteDo.Tour: window.StartTour(); break;
            case PaletteDo.Look: Support.Appearance.Look = a.Text; break;
            case PaletteDo.Back: engine.GoBack(); break;
            case PaletteDo.Forward: engine.GoForward(); break;
            case PaletteDo.Browser: Engine.Engine.OpenExternally(engine.BaseUrl); break;
            case PaletteDo.Tool: Screens.ToolsScreen.OpenTool(engine, a.Text); break;
            case PaletteDo.ToolPlace: if (a.Pick is { } pick) OpenToolPlace(pick); break;
            case PaletteDo.Copy:
                try { Clipboard.SetText(a.Text); } catch { /* the clipboard busy with another app */ }
                break;
        }
        if (fromField) Ran?.Invoke();
    }

    /// <summary>/note: a task for today in To Do, then To Do; if the page will not take it, New Task with the rest to fill in.</summary>
    private async void AddTask(string title)
    {
        if (await engine.Act("addTask", new { title }))
        {
            engine.DataChanged();
            engine.Go(Place.Todo);
        }
        else engine.AskNewTask();
    }

    /// <summary>A command as a button (the Search screen's, the Mac's CommandPill): pressed, it runs.</summary>
    public static FrameworkElement CommandPill(PaletteItem item)
    {
        var icon = item.Color is { Length: > 0 } c ? K.Icon(item.Symbol, 16, K.Hex(c), item.Filled) : K.Icon(item.Symbol, 16, B.AccentText, item.Filled);
        var title = K.Text(item.Title, Ty.Body);
        title.VerticalAlignment = VerticalAlignment.Center;
        var row = K.H(8, icon, title);
        if (item.Trailing is { Length: > 0 } t)
        {
            var key = K.Text(t, Ty.Caption, B.Text2);
            key.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(key);
        }
        var b = K.CardButton(row, () => Shared?.Perform(item.Action, fromField: false), 4, new Thickness(14, 9, 14, 9));
        b.ToolTip = item.Sub ?? item.Title;
        System.Windows.Automation.AutomationProperties.SetName(b, item.Title);
        return b;
    }

    // ---- the field itself ----

    private void SetText(string text)
    {
        box.Text = text;
        box.CaretIndex = text.Length;
    }

    private void FocusBox(bool selectAll)
    {
        box.Dispatcher.BeginInvoke(() =>
        {
            if (!IsOpen) return;
            if (!box.IsKeyboardFocusWithin)
            {
                box.Focus();
                Keyboard.Focus(box);
            }
            if (selectAll) box.SelectAll(); else box.CaretIndex = box.Text.Length;
        }, DispatcherPriority.Input);
    }

    /// <summary>The screenshot suite's way to the search (--open palette:/gr): once the window has settled, that typed in
    /// the field, its results shown. An underscore stands for a space ("palette:bio_files", "palette:/due_"), as the
    /// suite's arguments cannot carry one; "palette:" alone is the search as it opens.</summary>
    private void LaunchList()
    {
        if (Launch.Take("palette:") is not { } given) return;
        var text = given.Replace('_', ' ');
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            forced = true;
            Show(text);
        };
        timer.Start();
    }

    private static bool Within(DependencyObject? ancestor, DependencyObject d)
    {
        if (ancestor == null) return false;
        for (DependencyObject? p = d; p != null; p = p is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p == ancestor) return true;
        return false;
    }

    public void Dispose()
    {
        lookup?.Cancel();
        Hide();
        box.TextChanged -= OnText;
        box.PreviewKeyDown -= OnKey;
        box.LostKeyboardFocus -= OnBlur;
        engine.PropertyChanged -= OnEngine;
        if (layer.Parent is Panel p) p.Children.Remove(layer);
        if (Shared == this) Shared = null;
    }
}
