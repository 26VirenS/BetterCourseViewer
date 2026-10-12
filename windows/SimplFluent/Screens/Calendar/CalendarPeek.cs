using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Screens;

/// <summary>
/// A quick look at a piece of work or an event of the calendar (the Mac's WorkPreview card, as PreviewItem.event makes
/// it): a click on an item — on the month's grid, in the day, the week or the list — opens Windows 11's flyout out of it
/// with what it is: its kind and course, its title, when it is due or happens, its points, where it stands and the first
/// words of what it says, with the way through to it (Open, or Open in Canvas for an event of Canvas's own) and Hand in,
/// Take quiz, Reply or See feedback where it is work. A click round it, Escape or its Close folds it back; the item that
/// opened it, pressed again, folds it back too. A double-click (or a Ctrl+click) on an item opens it whole.
/// </summary>
internal sealed class CalendarPeek
{
    private const double CardWidth = 380;
    /// <summary>The room round the card for its shadow.</summary>
    private const double Shade = 18;

    private readonly Engine.Engine engine;
    private Popup? popup;
    private Border? frame;
    private Window? window;
    private Peeked? item;
    private AssignmentData? assignment;
    private PageData? page;
    private string? excerpt;
    private bool loading;
    private int generation;
    /// <summary>The last item pressed, and when (its second press within a double-click's time opens it whole).</summary>
    private (string Key, DateTime At)? lastPress;

    public CalendarPeek(Engine.Engine engine) => this.engine = engine;

    /// <summary>An item's own key, on its button (Tag) as on its card.</summary>
    public static string KeyOf(CalEvent ev) => "cal:" + ev.Id;

    public bool IsOpen => popup?.IsOpen == true;

    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();

    private static double DoubleClickMs
    {
        get { try { return GetDoubleClickTime(); } catch { return 500; } }
    }

    // ---- what a calendar item is, for its card ----

    private sealed record PeekAction(string Title, SymbolRegular Icon, Action Run);

    private sealed class Peeked
    {
        public string Key = "";
        public string Title = "";
        public string Kind = "Event";
        public SymbolRegular Symbol;
        public string? Course, Color, When, Points, Detail, Url;
        public List<WorkFlag> Flags = new();
        /// <summary>Work to hand in: its kind ("assignment", "quiz", "discussion") and whether it is handed in.</summary>
        public (string Kind, bool HandedIn)? Work;
        /// <summary>What a click on the row did before: the item opened whole.</summary>
        public Action? OpenWhole;
        public string OpenLabel = "Open";
    }

    /// <summary>
    /// (1.2.3) A piece of work or an event of the calendar: its line under the title is "course · kind · points · place"
    /// (native-app.js calendar). An event whose address is the calendar itself opens on Canvas's page.
    /// </summary>
    private Peeked Of(CalEvent ev)
    {
        var kind = CalendarKit.Text(ev.Kind) ?? "Event";
        var work = CalendarKit.IsWork(kind);
        var parts = (ev.Sub ?? "").Split(" · ").Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var course = parts.FirstOrDefault() is { } c && c != kind && !c.EndsWith(" pts") ? c : null;
        var place = parts.Where(p => p != course && p != kind && !p.EndsWith(" pts") && !p.EndsWith(" pt")).ToList();
        var it = new Peeked { Key = KeyOf(ev), Title = ev.Title, Kind = kind, Symbol = CalendarKit.Glyph(kind), Course = course, Color = ev.Color };
        var day = CalendarKit.Date(ev.Date) is { } d ? CalendarKit.DayWord(d) : null;
        it.When = CalendarKit.When(day, ev.Time, work);
        it.Points = CalendarKit.Points(ev.Sub);
        it.Detail = place.Count == 0 ? null : string.Join(" · ", place);
        if (ev.Missing == true) it.Flags.Add(new WorkFlag { Word = "Missing", Kind = "bad" });
        else if (ev.Excused == true) it.Flags.Add(new WorkFlag { Word = "Excused", Kind = "muted" });
        else if (work && ev.Done == true) it.Flags.Add(new WorkFlag { Word = "Submitted", Kind = "good" });
        if (CalendarKit.Text(ev.Url) is { } url)
        {
            it.Url = url;
            var e = engine;
            if (e.TabName(url) == "calendar")
            {
                // (Canvas's own page for an event: here the calendar would only open on itself)
                it.OpenLabel = $"Open in {e.LmsName}";
                it.OpenWhole = () => e.OpenWebScreen(url, ev.Title);
            }
            else it.OpenWhole = () => e.OpenWeb(url, ev.Title);
            if (work) it.Work = (kind.ToLowerInvariant(), ev.Done == true && ev.Missing != true);
        }
        return it;
    }

    // ---- pressed, opened, folded back ----

    /// <summary>An item pressed: its preview — or, for a double-click or a Ctrl+click, the item itself. The item whose card
    /// is out, pressed again, folds it back.</summary>
    public void Press(CalEvent ev, FrameworkElement from)
    {
        var key = KeyOf(ev);
        var now = DateTime.UtcNow;
        var again = lastPress is { } lp && lp.Key == key && (now - lp.At).TotalMilliseconds <= DoubleClickMs;
        lastPress = (key, now);
        var it = Of(ev);
        if (it.OpenWhole is { } open && (again || (Keyboard.Modifiers & ModifierKeys.Control) != 0))
        {
            lastPress = null;
            Dismiss();
            open();
            return;
        }
        if (again) return; // (nothing to open whole: its card stays)
        if (IsOpen && item?.Key == key)
        {
            Close();
            return;
        }
        Show(it, from);
    }

    /// <summary>A card for an item, at once (the screenshot suite's preview).</summary>
    public void Open(CalEvent ev, FrameworkElement from) => Show(Of(ev), from);

    /// <summary>The screen built again with the card out: the card stays with its item's new button.</summary>
    public void Anchor(FrameworkElement b, CalEvent ev)
    {
        if (!IsOpen || item?.Key != KeyOf(ev)) return;
        b.Loaded += (_, _) =>
        {
            if (IsOpen && item?.Key == KeyOf(ev) && popup != null) popup.PlacementTarget = b;
        };
    }

    private void Show(Peeked it, FrameworkElement from)
    {
        Dismiss();
        var gen = ++generation;
        item = it;
        assignment = null;
        page = null;
        excerpt = null;
        loading = false;
        window = Window.GetWindow(from);
        frame = new Border
        {
            Width = CardWidth, CornerRadius = new CornerRadius(8), Margin = new Thickness(Shade), SnapsToDevicePixels = true,
            Effect = K.Shadow(28, 8, 0.26), Child = Card(),
        };
        frame.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        frame.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };
        System.Windows.Automation.AutomationProperties.SetName(frame, "Preview of " + it.Title);
        popup = new Popup
        {
            AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Custom, PlacementTarget = from,
            PopupAnimation = PopupAnimation.None, Child = frame,
        };
        popup.CustomPopupPlacementCallback = Place;
        Hook(true);
        popup.IsOpen = true;
        Motion.DrillIn(frame);
        Shot.Moved();
        Read(gen);
    }

    /// <summary>Folded back: its words fade, then it is gone.</summary>
    public void Close()
    {
        if (popup == null || frame == null) return;
        var p = popup;
        var gen = generation;
        lastPress = null; // (an item pressed after this is a press of its own, never a double-click's second)
        Hook(false);
        Motion.FadeOut(frame, () =>
        {
            p.IsOpen = false;
            if (gen == generation) Gone();
        }, Motion.Fast);
    }

    /// <summary>Gone at once (the item opened whole, the calendar moved, the screen left).</summary>
    public void Dismiss()
    {
        if (popup == null) return;
        Hook(false);
        popup.IsOpen = false;
        generation++;
        Gone();
    }

    private void Gone()
    {
        popup = null;
        frame = null;
        item = null;
        window = null;
    }

    /// <summary>While the card is out: a click anywhere else in the window folds it back (the click still does what it
    /// does), and so does the window going to the back, moving or being resized, and the page scrolling under it.</summary>
    private void Hook(bool on)
    {
        if (window == null) return;
        if (on)
        {
            window.PreviewMouseDown += OnWindowMouse;
            window.PreviewMouseWheel += OnWindowWheel;
            window.Deactivated += OnDeactivated;
            window.LocationChanged += OnWindowMoved;
            window.SizeChanged += OnWindowSized;
        }
        else
        {
            window.PreviewMouseDown -= OnWindowMouse;
            window.PreviewMouseWheel -= OnWindowWheel;
            window.Deactivated -= OnDeactivated;
            window.LocationChanged -= OnWindowMoved;
            window.SizeChanged -= OnWindowSized;
        }
    }

    private void OnWindowMouse(object sender, MouseButtonEventArgs e)
    {
        if (!IsOpen || item == null) return;
        // (its own item: the press decides — a second press folds it back, a double-click opens it whole)
        if (e.OriginalSource is DependencyObject o && OnItem(o, item.Key)) return;
        Close();
    }

    private void OnWindowWheel(object sender, MouseWheelEventArgs e) => Close();

    private void OnWindowMoved(object? sender, EventArgs e) => Dismiss();

    private void OnWindowSized(object sender, SizeChangedEventArgs e) => Dismiss();

    private void OnDeactivated(object? sender, EventArgs e)
    {
        var f = frame;
        f?.Dispatcher.BeginInvoke(() =>
        {
            if (frame != f || f == null || f.IsMouseOver || f.IsKeyboardFocusWithin) return;
            Dismiss();
        });
    }

    private static bool OnItem(DependencyObject o, string key)
    {
        for (DependencyObject? p = o; p != null; p = p is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p is FrameworkElement { Tag: string t } && t == key) return true;
        return false;
    }

    /// <summary>
    /// Under its item, from its left edge, kept inside the window (over the item when there is no room under it) — the
    /// way Windows 11's flyouts open from what they belong to. (The callback's sizes are the screen's pixels.)
    /// </summary>
    private CustomPopupPlacement[] Place(Size popupSize, Size targetSize, Point offset)
    {
        var from = popup?.PlacementTarget as FrameworkElement;
        var s = from != null ? VisualTreeHelper.GetDpi(from).DpiScaleX : 1.0;
        var shade = Shade * s;
        var gap = 4 * s;
        var edge = 8 * s;
        var under = new Point(-shade, targetSize.Height + gap - shade);
        var over = new Point(-shade, -popupSize.Height + shade - gap);
        if (from == null || window == null || PresentationSource.FromVisual(from) == null || PresentationSource.FromVisual(window) == null)
            return new[] { new CustomPopupPlacement(under, PopupPrimaryAxis.Horizontal), new CustomPopupPlacement(over, PopupPrimaryAxis.Horizontal) };
        Point at;
        try { at = from.TranslatePoint(new Point(0, 0), window); }
        catch { return new[] { new CustomPopupPlacement(under, PopupPrimaryAxis.Horizontal) }; }
        var ax = at.X * s;
        var ay = at.Y * s;
        var ww = window.ActualWidth * s;
        var wh = window.ActualHeight * s;
        var cardW = popupSize.Width - 2 * shade;
        var cardH = popupSize.Height - 2 * shade;
        var left = Math.Clamp(ax, edge, Math.Max(edge, ww - edge - cardW));
        var x = left - ax - shade;
        var fitsUnder = ay + targetSize.Height + gap + cardH <= wh - edge;
        var fitsOver = ay - gap - cardH >= edge;
        double y;
        if (fitsUnder || !fitsOver)
        {
            y = targetSize.Height + gap - shade;
            if (!fitsUnder)
            {
                // (room neither under nor over it: as much of the card as the window holds)
                var top = Math.Clamp(ay + targetSize.Height + gap, edge, Math.Max(edge, wh - edge - cardH));
                y = top - ay - shade;
            }
        }
        else y = -cardH - gap - shade;
        return new[] { new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal) };
    }

    // ---- the card ----

    /// <summary>The card's face: what the item is and where, its title, where it stands, its facts, the first words of
    /// what it says, and what can be done with it.</summary>
    private FrameworkElement Card()
    {
        var it = item!;
        var a = assignment;
        var p = page;
        var tint = K.Hex(a?.Color ?? p?.Color ?? it.Color, Appearance.Accent);

        // what it is and where ("Assignment · BIO 101"), and Close
        var kicker = string.Join(" · ", new[] { CalendarKit.Text(a?.Kind) ?? it.Kind, CalendarKit.Text(a?.Context) ?? CalendarKit.Text(p?.Context) ?? it.Course }
            .Where(w => !string.IsNullOrWhiteSpace(w)));
        var kick = K.Text(kicker, Ty.Body);
        kick.FontWeight = FontWeights.SemiBold;
        kick.Foreground = new SolidColorBrush(K.Darkened(tint));
        kick.VerticalAlignment = VerticalAlignment.Center;
        var close = K.Button("Close", Close, icon: SymbolRegular.Dismiss24);
        close.MinWidth = 0;
        close.ToolTip = "Close (Esc, or click anywhere else)";
        var top = K.Columns("auto, *, auto, auto", K.IconTile(it.Symbol, tint, 26).Col(0), kick.Margin(9, 0, 8, 0).Col(1), close.Col(3));
        if (loading)
        {
            // (Canvas being asked again: a small spinner on this line, not a line of its own)
            var spin = K.Spinner(14);
            spin.ToolTip = $"Updating from {engine.LmsName}…";
            top.Children.Add(spin.Margin(0, 0, 10, 0).Col(2));
        }
        var title = K.Wrapped(a?.Title ?? p?.Title ?? it.Title, Ty.Subtitle, B.Text, 3);
        var head = K.V(8, top, title);
        head.Margin = new Thickness(20, 14, 14, 0);

        // where it stands, its facts, the first words of what it says
        var info = new Stack { Orientation = Orientation.Vertical, Spacing = 12, Margin = new Thickness(20, 12, 20, 0) };
        var flags = Chips();
        if (flags.Count > 0)
        {
            var flow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, -6) };
            foreach (var f in flags) flow.Children.Add(K.Chip(f.Word, CalendarKit.Tone(f.Kind)).Margin(0, 0, 6, 6));
            info.Children.Add(flow);
        }
        var facts = Facts();
        if (facts.Count > 0)
        {
            var list = new Stack { Orientation = Orientation.Vertical, Spacing = 6 };
            foreach (var (symbol, text, strong) in facts)
            {
                var words = K.Wrapped(text, Ty.Body, strong ? B.Text : B.Text2);
                if (strong) words.FontWeight = FontWeights.Medium;
                list.Children.Add(K.Columns("auto, *", K.Icon(symbol, 14, B.Text2).Margin(0, 3, 10, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), words.Col(1)));
            }
            info.Children.Add(list);
        }
        if (excerpt != null) info.Children.Add(K.Wrapped(excerpt, Ty.Body, B.Text2, 5));

        // what can be done with it: its own action, then Open
        var actions = K.H(8);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        actions.Margin = new Thickness(20, 18, 20, 18);
        if (OwnAction() is { } act)
        {
            var b = K.Button(act.Title, () => { Dismiss(); act.Run(); }, icon: act.Icon);
            b.MinWidth = 0;
            actions.Children.Add(b);
        }
        if (it.OpenWhole is { } open)
        {
            var b = K.Button(it.OpenLabel, () => { Dismiss(); open(); }, accent: true);
            b.ToolTip = it.OpenLabel;
            actions.Children.Add(b);
        }
        if (actions.Children.Count == 0) actions.Margin = new Thickness(0, 18, 0, 0); // (nothing to do with it: its foot only)
        return K.V(0, head, info, actions);
    }

    /// <summary>Where it stands: the assignment's own word and grade once read (with the row's Feedback or New), else the row's.</summary>
    private List<WorkFlag> Chips()
    {
        if (assignment is not { } d) return item!.Flags;
        var outs = new List<WorkFlag>();
        if (d.Status is { } s && s.Word.Length > 0) outs.Add(new WorkFlag { Word = s.Word, Kind = s.Kind });
        if (d.Grade is { Score: { } score } g)
        {
            var text = $"{CalendarKit.Number(score)} / {CalendarKit.Number(g.Possible ?? 0)}";
            if (CalendarKit.Text(g.Letter) is { } letter) text += $" · {letter}";
            outs.Add(new WorkFlag { Word = text, Kind = "info" });
        }
        if (CalendarKit.Text(d.Grade?.Late) is { } late) outs.Add(new WorkFlag { Word = late, Kind = "warn" });
        var words = outs.Select(f => f.Word).ToHashSet();
        foreach (var f in item!.Flags)
            if (f.Word.ToLowerInvariant() is "feedback" or "new" && !words.Contains(f.Word)) outs.Add(f);
        return outs;
    }

    private List<(SymbolRegular Symbol, string Text, bool Strong)> Facts()
    {
        var it = item!;
        var a = assignment;
        var outs = new List<(SymbolRegular, string, bool)>();
        if (it.When is { } w) outs.Add((SymbolRegular.Clock24, w, true));
        else if (a != null) outs.Add((SymbolRegular.Clock24, CalendarKit.Text(a.Due) is { } due ? $"Due {due}" : "No due date", true));
        if ((it.Points ?? CalendarKit.Text(a?.Points)) is { } pts) outs.Add((SymbolRegular.NumberSymbol24, pts, false));
        if (CalendarKit.Text(a?.Submitted) is { } sub) outs.Add((SymbolRegular.CheckmarkStarburst24, sub, false));
        if (CalendarKit.Text(a?.Available) is { } avail) outs.Add((SymbolRegular.LockOpen24, avail, false));
        if (CalendarKit.Text(page?.Edited) is { } edited) outs.Add((SymbolRegular.Edit24, edited, false));
        if (CalendarKit.Text(it.Detail) is { } detail) outs.Add((SymbolRegular.Info24, detail, false));
        return outs;
    }

    /// <summary>Its own action beside Open, as its screen would put first: Take quiz, See feedback, Reply, Hand in, Open tool.</summary>
    private PeekAction? OwnAction()
    {
        var it = item!;
        if (it.Url is not { } url) return null;
        var title = it.Title;
        var e = engine;
        if (assignment is { } d)
        {
            var handedIn = CalendarKit.Text(d.Submitted) != null || d.Grade != null;
            if (CalendarKit.Text(d.QuizUrl) is { } q && !handedIn) return new("Take quiz", SymbolRegular.TaskListLtr24, () => e.ActOn(q, title, false));
            if (handedIn) return new("See feedback", SymbolRegular.CommentText24, () => e.ActOn(url, title, true));
            if (CalendarKit.Text(d.DiscussionUrl) is { } disc) return new("Reply", SymbolRegular.Chat24, () => e.OpenWeb(disc, title));
            if (d.CanSubmit) return new(d.Resubmit == true ? "Resubmit" : "Hand in", SymbolRegular.ArrowUpload24, () => e.ActOn(url, title, false));
            if (d.ToolUrl != null)
            {
                var quiz = d.LtiQuiz == true;
                return new(quiz ? "Take quiz" : "Open tool", quiz ? SymbolRegular.TaskListLtr24 : SymbolRegular.PuzzlePiece24, () => e.ActOn(url, title, false));
            }
            return null;
        }
        if (it.Work is not { } w) return null;
        if (w.HandedIn) return new("See feedback", SymbolRegular.CommentText24, () => e.ActOn(url, title, true));
        return w.Kind switch
        {
            "quiz" => new("Take quiz", SymbolRegular.TaskListLtr24, () => e.ActOn(url, title, false)),
            "discussion" or "discussion_topic" => new("Reply", SymbolRegular.Chat24, () => e.ActOn(url, title, false)),
            "tool" => new("Open", SymbolRegular.PuzzlePiece24, () => e.ActOn(url, title, false)),
            _ => new("Hand in", SymbolRegular.ArrowUpload24, () => e.ActOn(url, title, false)),
        };
    }

    // ---- reading ----

    /// <summary>The assignment (or the page) behind the item, as its own screen reads it: what was kept from last time at
    /// once, then the school's answer. Anything else (an event) shows what its row knows.</summary>
    private async void Read(int gen)
    {
        if (item?.Url is not { } url) return;
        var title = item.Title;
        // (1.3.32) a quiz: its own assignment, as the assignment's screen reads it — its grade, when it was handed in
        if (QuizIds(url) is { } ids)
        {
            var args = new { course = ids.Course, quiz = ids.Quiz };
            if (engine.Kept<AssignmentData>("quizAssignment", args) is { } kept) Shown(gen, kept, null);
            await Fetch<AssignmentData>(gen, "quizAssignment", args, a => Shown(gen, a, null));
            return;
        }
        if (engine.NativeRoute(url, title) is not { } route) return;
        if (route.Kind == RouteKind.Assignment)
        {
            var args = new { course = route.A, id = route.B };
            if (engine.Kept<AssignmentData>("assignment", args) is { } kept) Shown(gen, kept, null);
            await Fetch<AssignmentData>(gen, "assignment", args, a => Shown(gen, a, null));
        }
        else if (route.Kind == RouteKind.Page)
        {
            object args = string.IsNullOrEmpty(route.B) ? new { ctx = route.A } : new { ctx = route.A, slug = route.B };
            if (engine.Kept<PageData>("page", args) is { } kept) Shown(gen, null, kept);
            await Fetch<PageData>(gen, "page", args, pg => Shown(gen, null, pg));
        }
    }

    private async Task Fetch<T>(int gen, string name, object args, Action<T> shown) where T : class
    {
        SetLoading(gen, true);
        try
        {
            var fresh = await engine.Call<T>(name, args);
            if (gen == generation) shown(fresh);
        }
        catch { /* the card shows what its row knows */ }
        SetLoading(gen, false);
    }

    private void SetLoading(int gen, bool on)
    {
        if (gen != generation || loading == on) return;
        loading = on;
        Redraw();
    }

    private void Shown(int gen, AssignmentData? a, PageData? p)
    {
        if (gen != generation) return;
        assignment = a;
        page = p;
        excerpt = CalendarKit.Plain(a?.Html ?? p?.Html);
        Redraw();
    }

    private void Redraw()
    {
        if (frame == null || item == null) return;
        frame.Child = Card();
        Shot.Moved();
    }

    /// <summary>A Canvas quiz's course and quiz, from its address (…/courses/1/quizzes/2).</summary>
    private (string Course, string Quiz)? QuizIds(string raw)
    {
        if (engine.Absolute(raw) is not { } u || !engine.IsSchool(u)) return null;
        var p = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 4 || p[0] != "courses" || p[2] != "quizzes" || !Engine.Engine.Numeric(p[1]) || !Engine.Engine.Numeric(p[3])) return null;
        return (p[1], p[3]);
    }
}
