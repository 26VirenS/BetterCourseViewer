using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using Popup = System.Windows.Controls.Primitives.Popup;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Shell;

/// <summary>
/// The navigation pane's search box as the web's search box (the Mac's SearchCommands.swift; 1.2, the extension's
/// search.js and hub.js), drawn as Windows 11's AutoSuggestBox: what is typed is answered in a list under the box as each
/// letter goes in — a sum worked out, the commands whose names it starts, your courses (and a course's section: "bio
/// files"), your groups, and Search for the words. "/" (or ">") lists every command the box knows, narrowing as its name
/// is typed; a command that takes something (/course, /grades, /due, /file …) lists what it can be once a space follows
/// its name. A suggestion picked — clicked, or ↑ ↓ and Enter — runs; Enter on words alone searches everything (on a sum,
/// copies its answer). Typing never moves the page: the full search runs only on Enter or Search for, so a quick answer
/// or a jump to a course leaves the student where they were. Ctrl+K opens the box on "/".
/// </summary>
public sealed partial class SearchPalette : IDisposable
{
    /// <summary>The widest the list grows (it may reach out of the pane over the page).</summary>
    public const double ListWidth = 480;
    // (room round the list for its shadow, inside the popup's own window)
    private const double Side = 16, Above = 8, Below = 28;

    private readonly TextBox box;
    private readonly Engine.Engine engine;
    private readonly MainWindow window;
    private readonly Popup popup;
    private readonly Grid surface;
    private readonly ScrollViewer scroller;
    private readonly Stack list = new() { Orientation = Orientation.Vertical, Spacing = 2 };
    private readonly List<(PaletteItem Item, Button Row, Border Pill)> rows = new();
    private List<PaletteSection> sections = new();
    private string? chosen;
    private bool commandMode;
    /// <summary>The screenshot suite's list, shown whether or not the box holds the keyboard.</summary>
    private bool forced;
    private int seq;
    private CancellationTokenSource? lookup;
    private string lastRaw = "";
    private Point? placedAt;

    /// <summary>The palette of the window showing (the Search screen's command buttons run through it).</summary>
    public static SearchPalette? Shared { get; private set; }

    /// <summary>A suggestion ran, or the words were searched: the pane, over the page while the window is narrow, can close.</summary>
    public event Action? Ran;

    public bool IsOpen => popup.IsOpen;

    /// <summary>The box's suggestions and commands, taking over its keys (↑ ↓ Enter Escape) and its text.</summary>
    public static SearchPalette Attach(TextBox box, Engine.Engine engine, MainWindow window)
    {
        var p = new SearchPalette(box, engine, window);
        Shared = p;
        return p;
    }

    private SearchPalette(TextBox box, Engine.Engine engine, MainWindow window)
    {
        this.box = box;
        this.engine = engine;
        this.window = window;

        // the list: Windows 11's flyout surface — its fill, its hairline, rounded 8, raised on a soft shadow (drawn by a
        // copy of the surface behind it, so the words keep their crisp ClearType)
        scroller = new ScrollViewer
        {
            Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, Padding = new Thickness(0, 4, 0, 6),
        };
        var shade = new Border { CornerRadius = new CornerRadius(8), Effect = K.Shadow(24, 6, 0.22) }.Bg(B.Flyout);
        var face = new Border { CornerRadius = new CornerRadius(8), Child = scroller, SnapsToDevicePixels = true }.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        surface = new Grid { Margin = new Thickness(Side, Above, Side, Below), Children = { shade, face } };
        popup = new Popup
        {
            Child = new Grid { Children = { surface } }, AllowsTransparency = true, StaysOpen = true, Focusable = false,
            PlacementTarget = box, Placement = PlacementMode.Bottom, HorizontalOffset = -Side, VerticalOffset = 4 - Above,
            PopupAnimation = PopupAnimation.None,
        };
        System.Windows.Automation.AutomationProperties.SetName(list, "Suggestions");

        box.PlaceholderText = Placeholder;
        box.TextChanged += OnText;
        box.PreviewKeyDown += OnKey;
        box.GotKeyboardFocus += OnFocus;
        box.LostKeyboardFocus += OnBlur;
        box.IsVisibleChanged += OnShown;
        window.PreviewMouseDown += OnWindowMouse;
        window.LocationChanged += OnWindowMoved;
        window.SizeChanged += OnWindowMoved;
        engine.PropertyChanged += OnEngine;
        LaunchList();
    }

    private string Placeholder => $"Search {engine.LmsName} or type /";

    /// <summary>Search Commands (Ctrl+K): the box with "/" in it, every command listed.</summary>
    public void OpenCommands()
    {
        if (box.Text == "/") Update("/"); else SetText("/");
        FocusBox();
    }

    /// <summary>Escape: the list closed, if it was open.</summary>
    public bool Escape()
    {
        if (!popup.IsOpen) return false;
        Close();
        return true;
    }

    // ---- the box's moves ----

    private void OnText(object? sender, TextChangedEventArgs e)
    {
        var raw = box.Text;
        if (engine.Query != raw) engine.Query = raw;
        // (the suggestions only: the page stays as it is until Enter or Search for asks for the full search)
        Update(raw);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (!popup.IsOpen)
                {
                    if (box.Text.Trim().Length == 0) return;
                    Update(box.Text);
                }
                else Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                if (!popup.IsOpen) return;
                Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                Submit();
                e.Handled = true;
                break;
            case Key.Escape:
                if (popup.IsOpen) Close();
                else
                {
                    // (a second Escape lets go of the box: a command left in it is cleared, words searched stay)
                    if (IsCommand(box.Text)) SetText("");
                    LetGo();
                }
                e.Handled = true;
                break;
        }
    }

    private void OnFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (box.Text.Trim().Length == 0) return;
        if (lastRaw == box.Text && sections.Count > 0) Open(); else Update(box.Text);
    }

    private void OnBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (forced) return;
        if (e.NewFocus is DependencyObject d && (Within(box, d) || Within(popup.Child, d))) return;
        Close();
    }

    private void OnShown(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!box.IsVisible) Close();
    }

    /// <summary>A press anywhere in the window but the box (or the list): the list closes (the box keeps what it holds).</summary>
    private void OnWindowMouse(object sender, MouseButtonEventArgs e)
    {
        if (!popup.IsOpen) return;
        if (e.OriginalSource is DependencyObject d && (Within(box, d) || Within(popup.Child, d))) return;
        Close();
    }

    private void OnWindowMoved(object? sender, EventArgs e)
    {
        if (!popup.IsOpen) return;
        Fit();
        Nudge();
    }

    private void OnEngine(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Engine.Engine.LmsName):
                box.PlaceholderText = Placeholder;
                break;
            case nameof(Engine.Engine.Courses) or nameof(Engine.Engine.Groups) or nameof(Engine.Engine.Sections):
                // (the courses, the groups or a course's sections came in: the list open says so)
                if (popup.IsOpen) Update(box.Text, keepChoice: true);
                break;
        }
    }

    /// <summary>Enter: the suggestion chosen runs; else a command's first suggestion (as the web box chooses its first row
    /// as you type), or the words are searched.</summary>
    private void Submit()
    {
        if (popup.IsOpen && chosen != null && rows.FirstOrDefault(r => r.Item.Id == chosen).Item is { } picked)
        {
            Perform(picked.Action, fromField: true);
            return;
        }
        var t = box.Text.Trim();
        if (t.Length == 0) return;
        if (IsCommand(t))
        {
            if (lastRaw != box.Text) Update(box.Text);
            if (sections.SelectMany(s => s.Items).FirstOrDefault(i => i.Action.Acts) is { } first) Perform(first.Action, fromField: true);
            return;
        }
        Close();
        engine.Search(t);
        Ran?.Invoke();
    }

    // ---- the suggestions for what the box holds ----

    /// <summary>The suggestions for what the box holds now: what the app holds at once, what it must ask for after a pause.</summary>
    private void Update(string raw, bool keepChoice = false)
    {
        seq++;
        var mine = seq;
        lookup?.Cancel();
        lookup = null;
        lastRaw = raw;
        commandMode = Typed.Of(raw).Command;
        var (built, job) = Suggest(raw);
        Publish(built, keepChoice);
        if (sections.Count == 0) Close();
        else if (box.IsKeyboardFocusWithin || forced) Open();
        if (job == null) return;
        var cts = lookup = new CancellationTokenSource();
        Later(job, built, mine, cts.Token);
    }

    private async void Later(Job job, List<PaletteSection> based, int mine, CancellationToken token)
    {
        PaletteSection? extra;
        try { extra = await Run(job, token); }
        catch { return; }
        if (token.IsCancellationRequested || seq != mine || extra == null) return;
        Publish(Merge(based, extra), keepChoice: true);
        Shot.Moved();
    }

    /// <summary>The sections shown (each row's id its own), and the row chosen: kept where it still is, else — for a
    /// command — the first that does something (what Enter runs), and for words none (Enter searches them).</summary>
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
        // (a sum's answer is what Enter takes — copied — never a search that finds nothing)
        if (!(keepChoice && chosen != null && acting.Contains(chosen))) chosen = commandMode ? acting.FirstOrDefault() : acting.Contains("answer") ? "answer" : null;
        Render(fresh: !keepChoice);
    }

    // ---- the list ----

    /// <summary>The list drawn again: a new one from its top, one that answered later where it was.</summary>
    private void Render(bool fresh)
    {
        list.Children.Clear();
        rows.Clear();
        var first = true;
        foreach (var s in sections)
        {
            list.Children.Add(K.Text(s.Title, Ty.BodyStrong).Margin(16, first ? 6 : 14, 16, 4));
            first = false;
            foreach (var it in s.Items) list.Children.Add(Row(it));
        }
        Highlight(scroll: false);
        if (fresh) scroller.ScrollToTop();
    }

    /// <summary>A suggestion (Windows 11's list item): its symbol — or a course's square — what it is (a command's name
    /// with what it takes after it as grey placeholders) over the line after it, a shortcut or a score at its end; the one
    /// chosen filled, with the accent's pill at its left.</summary>
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
        // (the box keeps the keyboard while a row is pressed)
        b.Focusable = false;
        b.MinHeight = 40;
        b.Margin = new Thickness(4, 0, 4, 0);
        b.ToolTip = it.Title.Length > 48 ? it.Title : null;
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

    /// <summary>↑ and ↓: the next suggestion that does something; ↑ from the first goes back to the words typed.</summary>
    private void Move(int step)
    {
        if (rows.Count == 0) return;
        var i = chosen == null ? -1 : rows.FindIndex(r => r.Item.Id == chosen);
        var next = i < 0 ? (step > 0 ? 0 : rows.Count - 1) : i + step;
        if (next < 0) chosen = commandMode ? rows[0].Item.Id : null;
        else chosen = rows[Math.Min(next, rows.Count - 1)].Item.Id;
        Highlight(scroll: true);
        if (chosen == null) scroller.ScrollToTop();
    }

    // ---- the list's window ----

    private void Open()
    {
        Fit();
        if (popup.IsOpen) return;
        popup.IsOpen = true;
        placedAt = Where();
        Motion.Enter(surface, 0, -8, Motion.Medium);
        Shot.Moved();
        Follow();
    }

    private void Close()
    {
        forced = false;
        if (popup.IsOpen) popup.IsOpen = false;
    }

    /// <summary>As wide as the room right of the box allows (never narrower than the box, at most <see cref="ListWidth"/>),
    /// and no taller than the window below it.</summary>
    private void Fit()
    {
        double left = 0, bottom = box.ActualHeight;
        try
        {
            var p = box.TranslatePoint(new Point(0, box.ActualHeight), window);
            left = p.X;
            bottom = p.Y;
        }
        catch (InvalidOperationException) { }
        var room = window.ActualWidth - Math.Max(0, left) - 16;
        surface.Width = Math.Max(box.ActualWidth, Math.Min(ListWidth, room));
        scroller.MaxHeight = Math.Clamp(window.ActualHeight - bottom - 24, 180, 560);
    }

    private Point? Where()
    {
        try { return box.IsVisible ? box.PointToScreen(new Point(0, 0)) : null; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>The list placed again under the box where it is now.</summary>
    private void Nudge()
    {
        var h = popup.HorizontalOffset;
        popup.HorizontalOffset = h + 0.01;
        popup.HorizontalOffset = h;
    }

    /// <summary>A moment after opening, the list follows the box if it is still moving (the pane sliding in over a
    /// narrow window).</summary>
    private async void Follow()
    {
        for (var i = 0; i < 10 && popup.IsOpen; i++)
        {
            await Task.Delay(40);
            if (!popup.IsOpen) return;
            var now = Where();
            if (now is { } p && placedAt is { } q && (Math.Abs(p.X - q.X) > 0.5 || Math.Abs(p.Y - q.Y) > 0.5))
            {
                Fit();
                Nudge();
            }
            placedAt = now;
        }
    }

    // ---- running one ----

    /// <summary>A suggestion's action. From the box (<paramref name="fromField"/>), the box is let go of first — emptied, or
    /// left holding the words searched — so its list closes and the keys go to the screen.</summary>
    public void Perform(PaletteAction a, bool fromField)
    {
        switch (a.Do)
        {
            case PaletteDo.Nothing:
                return;
            case PaletteDo.Fill:
                // (the box keeps the keyboard, listing what the command takes)
                if (box.Text == a.Text) Update(a.Text); else SetText(a.Text);
                FocusBox();
                return;
        }
        if (fromField)
        {
            LetGo();
            SetText(a.Do == PaletteDo.Search ? a.Text : "");
        }
        switch (a.Do)
        {
            case PaletteDo.Go: if (a.To is { } place) engine.Go(place); break;
            case PaletteDo.Open: engine.OpenFromSearch(a.Text, a.Title, a.External); break;
            case PaletteDo.Work:
                if (a.Text.Length > 0) engine.OpenWeb(a.Text, a.Title); else engine.Go(Place.Todo);
                break;
            case PaletteDo.Search: engine.Search(a.Text); break;
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

    // ---- the box itself ----

    private void SetText(string text)
    {
        box.Text = text;
        box.CaretIndex = text.Length;
    }

    private void FocusBox()
    {
        box.CaretIndex = box.Text.Length;
        box.Dispatcher.BeginInvoke(() =>
        {
            if (!box.IsKeyboardFocusWithin)
            {
                box.Focus();
                Keyboard.Focus(box);
            }
            box.CaretIndex = box.Text.Length;
            if (box.IsKeyboardFocusWithin && sections.Count > 0) Open();
        }, DispatcherPriority.Input);
    }

    /// <summary>The box let go of: its list closed, the keyboard to the window (and so the screen's shortcuts).</summary>
    private void LetGo()
    {
        Close();
        if (!box.IsKeyboardFocusWithin) return;
        window.Focus();
        if (box.IsKeyboardFocusWithin) Keyboard.ClearFocus();
    }

    /// <summary>The screenshot suite's way to the box's suggestions (--open palette:/gr): once the window has settled,
    /// that typed in the box, its list shown. An underscore stands for a space ("palette:bio_files", "palette:/due_"), as
    /// the suite's arguments cannot carry one.</summary>
    private void LaunchList()
    {
        if (Launch.Take("palette:") is not { } given) return;
        var text = given.Replace('_', ' ');
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            forced = true;
            box.Focus();
            Keyboard.Focus(box);
            if (box.Text == text) Update(text); else box.Text = text;
            box.CaretIndex = text.Length;
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
        Close();
        box.TextChanged -= OnText;
        box.PreviewKeyDown -= OnKey;
        box.GotKeyboardFocus -= OnFocus;
        box.LostKeyboardFocus -= OnBlur;
        box.IsVisibleChanged -= OnShown;
        window.PreviewMouseDown -= OnWindowMouse;
        window.LocationChanged -= OnWindowMoved;
        window.SizeChanged -= OnWindowMoved;
        engine.PropertyChanged -= OnEngine;
        if (Shared == this) Shared = null;
    }
}
