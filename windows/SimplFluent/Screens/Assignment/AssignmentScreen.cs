using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// One assignment, as a page of its own (the Mac's AssignmentView): its kind, its course and where your work stands over
/// the page; then the instructions, what you handed in and the comments on the left, and on the right the one thing to
/// do next — Hand In, Take Quiz, Open Discussion, Open Tool — with the grade and the facts: due, points, when it is open,
/// how it is handed in, the attempts. The rubric is one row at the top (and a button on the grade card) that brings the
/// rubric ring up over the window. On a wide page the right column stays put beside the left one as it scrolls, its top
/// level with the left one's first card; a narrow page shows the right's cards first, in one column. A file handed in
/// opens with its feedback; files dropped on the page open Hand In with them already in it; arriving from a piece of
/// work's Hand In or See Feedback does that at once. Previous and Next, the next step and More are beside the title.
/// </summary>
public sealed partial class AssignmentScreen : Screen
{
    private readonly string course, id;
    private readonly Loader<AssignmentData> model;
    private QuizIntroLite? quizIntro;
    /// <summary>The quiz's intro could not be read: its buttons as the assignment says, nothing waiting on it.</summary>
    private bool quizIntroFailed;
    private ItemNeighbours? around;
    private bool aroundAsked;
    private bool commenting;
    private AssignComposer? composer;
    /// <summary>A part of the page to go to once the next answer is in ("work" after a hand-in), and whether its seal bounces.</summary>
    private string? jumpAfterLoad;
    private bool bounceAfterLoad;
    private string? headerKey;
    private bool dropShown;

    // the page's frame, kept from build to build so the scroll stays where it was
    private readonly Grid root = new() { Background = Brushes.Transparent, AllowDrop = true };
    private readonly ScrollViewer one = Scroller(), left = Scroller(), side = Scroller();
    private readonly Grid dropOverlay = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private FrameworkElement? sideContent, headerPart;
    private double pinTop;
    /// <summary>The page laid out in two columns (the right's cards beside the left's), as last built.</summary>
    private bool wide;

    // the next step on the page, and its copy in the header — shown only while the page's own is out of sight
    private FrameworkElement? actionPart;
    private Wpf.Ui.Controls.Button? headerStep;
    private bool actionInView = true;
    private int headerStepTurn;

    // the parts of this build a jump goes to
    private FrameworkElement? workPart, gradePart, commentsPart, seal;
    private Border? gradeLit, commentsLit;

    public AssignmentScreen(Engine.Engine engine, string course, string id) : base(engine)
    {
        this.course = course;
        this.id = id;
        model = Watch(new Loader<AssignmentData>());
        model.Changed += Answered;
        root.PreviewDragEnter += DragOver;
        root.PreviewDragOver += DragOver;
        root.PreviewDragLeave += DragLeave;
        root.PreviewDrop += Drop;
        side.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        side.SizeChanged += (_, _) => PinSide();
        one.ScrollChanged += (_, _) => WatchAction();
        side.ScrollChanged += (_, _) => WatchAction();
    }

    private static ScrollViewer Scroller() => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
    };

    private object Args => new { course, id };

    private string CanvasUrl => model.Data?.CanvasUrl ?? $"/courses/{course}/assignments/{id}?bcv=native";

    public override string Title => model.Data?.Title ?? "Assignment";

    /// <summary>A course's assignment: checked every ten minutes (its own submission is always read fresh).</summary>
    public override bool CoursePage => true;
    // (no line under the title: the course is named by the chip under it — its way back — and, opened from the course,
    // by the breadcrumb; a long name wraps to a second line rather than being cut)
    public override int TitleLines => 2;
    /// <summary>The course's colour before the title, as every course page wears it (the Mac's heading square).</summary>
    public override Color? TitleColor => Appearance.Hex(model.Data?.Color) ?? CourseKit.ContextColor(engine, $"courses/{course}");
    public override bool Scrolls => false;

    public override void Refresh()
    {
        model.Ensure(engine, "assignment", Args);
        if (!aroundAsked) LoadNeighbours();
        // (shown again with an arrival waiting — a press elsewhere on this same assignment's Hand In or See Feedback)
        if (model.Data is { } d && !model.Loading && engine.Arrival is { } a && a.Id == id)
            root.Dispatcher.BeginInvoke(() => Arrived(d), DispatcherPriority.ContextIdle);
    }

    /// <summary>Escape puts the comment being written away.</summary>
    public override bool Escape()
    {
        if (!commenting) return false;
        StopCommenting();
        return true;
    }

    // ---- the answers ----

    /// <summary>An answer came: what the screenshot suite asks to have open, an arrival's Hand In or feedback, a taken
    /// quiz's intro, and the part of the page to go to after a hand-in.</summary>
    private void Answered()
    {
        if (model.Data is not { } d) return;
        if (d.CanSubmit && Launch.Take("handin") != null) OpenHandIn(d);
        if (d.Rubric.Count > 0 && Launch.Take("rubric") is { } shot) OpenRubricShot(shot, d);
        Arrived(d);
        if (HasWork(d) && Launch.Take("feedback") != null) Review(d, d.Submission?.Files?.FirstOrDefault());
        LoadQuizIntro(d);
        if (jumpAfterLoad is { } j)
        {
            jumpAfterLoad = null;
            var bounce = bounceAfterLoad;
            bounceAfterLoad = false;
            Later(80, () => Jump(j, bounce));
        }
        if (Launch.IsDevelopment)
            Log.Write($"SimplAssignment {id}: files {d.Submission?.Files?.Count ?? 0}, comments {d.Comments.Count}, steps {string.Join("+", NextSteps(d).Select(s => s.Title))}, error {model.Error ?? "-"}");
    }

    private void Later(double ms, Action act)
    {
        var t = new DispatcherTimer(DispatcherPriority.ContextIdle, root.Dispatcher) { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); act(); };
        t.Start();
    }

    private sealed class QuizIntroLite
    {
        public string Begin { get; set; } = "";
        public bool CanStart { get; set; }
        public LastAttempt? Last { get; set; }
        public sealed class LastAttempt { public bool Feedback { get; set; } }
        /// <summary>(2.99.33) The attempts as the quiz itself counts them — the count New attempt follows — for the facts
        /// ("1 of 2 attempts used"), and the one-column facts line with them.</summary>
        public string? AttemptsText { get; set; }
        public string? FactsLine { get; set; }
    }

    private sealed class ItemNeighbours
    {
        public Item? Prev { get; set; }
        public Item? Next { get; set; }
        public sealed class Item { public string Name { get; set; } = ""; public string Url { get; set; } = ""; }
    }

    /// <summary>A taken quiz's intro, read once the page is: whether its feedback shows and another attempt may begin.</summary>
    private async void LoadQuizIntro(AssignmentData d)
    {
        if (d.QuizId is not { } q || !QuizTaken(d)) return;
        try
        {
            quizIntro = await engine.Call<QuizIntroLite>("quizIntro", new { course, quiz = q });
            quizIntroFailed = false;
        }
        catch { quizIntroFailed = quizIntro == null; /* as the quiz was taken: its feedback */ }
        Render();
    }

    /// <summary>The assignments either side of this one (the web's Previous and Next), asked for once the screen is up.</summary>
    private async void LoadNeighbours()
    {
        aroundAsked = true;
        try
        {
            around = await engine.Call<ItemNeighbours>("neighbours", new { ctx = $"courses/{course}", type = "Assignment", id });
            HeaderChange();
        }
        catch { }
    }

    // ---- the header's commands (the Mac's toolbar) ----

    /// <summary>Previous and Next (each naming where it goes), the next step — only while the page's own button is out of
    /// sight: two of the same would compete, and the title needs the room — and More (Open in Canvas, Copy link).</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            var bar = K.H(8);
            if (around == null || around.Prev != null || around.Next != null)
            {
                var prev = K.Button("Previous", () => OpenNeighbour(around?.Prev), icon: SymbolRegular.ChevronLeft24);
                prev.MinWidth = 0;
                prev.IsEnabled = around?.Prev != null;
                prev.ToolTip = around?.Prev is { } p ? $"Previous: {p.Name}" : "Nothing before this";
                ToolTipService.SetShowOnDisabled(prev, true);
                var next = new Wpf.Ui.Controls.Button
                {
                    Content = K.H(8, new TextBlock { Text = "Next", VerticalAlignment = VerticalAlignment.Center }, new SymbolIcon { Symbol = SymbolRegular.ChevronRight24, FontSize = 14 }),
                    Appearance = ControlAppearance.Secondary, IsEnabled = around?.Next != null,
                    ToolTip = around?.Next is { } n ? $"Next: {n.Name}" : "Nothing after this",
                };
                ToolTipService.SetShowOnDisabled(next, true);
                next.Click += (_, _) => OpenNeighbour(around?.Next);
                bar.Children.Add(K.H(4, prev, next));
            }
            headerStep = null;
            if (model.Data is { } d && NextStepOf(d) is { } step)
            {
                var b = K.Button(step.Title, () => Act(d, step), icon: step.Symbol);
                b.MinWidth = 0;
                b.ToolTip = step.Help;
                b.IsEnabled = !step.Pending && !step.Off;
                b.Visibility = actionInView ? Visibility.Collapsed : Visibility.Visible;
                headerStep = b;
                bar.Children.Add(b);
            }
            Button? more = null;
            more = K.IconButton(SymbolRegular.MoreHorizontal24, () =>
            {
                var m = new ContextMenu { PlacementTarget = more, Placement = PlacementMode.Bottom };
                // (the instructions in a window of their own, kept in sight while handing in or working elsewhere)
                var apart = new System.Windows.Controls.MenuItem { Header = "Open in new window", Icon = new SymbolIcon { Symbol = SymbolRegular.WindowNew24 }, InputGestureText = Shell.PlaceWindow.Gesture };
                apart.Click += (_, _) => Shell.PlaceWindow.Open(engine, Route.Assignment(course, id));
                m.Items.Add(apart);
                m.Items.Add(new Separator());
                m.Items.Add(AssignKit.Item($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(CanvasUrl, Title)));
                m.Items.Add(AssignKit.Item("Copy link", SymbolRegular.Link24, () => { if (engine.Absolute(CanvasUrl) is { } u) AssignKit.Copy(u.ToString()); }));
                m.IsOpen = true;
            }, "More");
            bar.Children.Add(more);
            return bar;
        }
    }

    /// <summary>
    /// Whether the page's own next step is in sight (scrolled or not): out of it, its copy fades into the header beside
    /// Next — 200 ms, the pages' strong ease-out, its opacity only — and back in sight it goes again. Under Windows'
    /// "Animation effects" off it simply shows and goes.
    /// </summary>
    private void WatchAction()
    {
        var inView = true;
        if (actionPart is { IsLoaded: true } part && (wide ? side : one) is { IsLoaded: true } scroller)
        {
            try { inView = part.TranslatePoint(new Point(0, part.ActualHeight), scroller).Y > 8; }
            catch (InvalidOperationException) { }
        }
        if (inView == actionInView) return;
        actionInView = inView;
        if (headerStep is not { } b) return;
        var turn = ++headerStepTurn;
        if (!inView)
        {
            b.Visibility = Visibility.Visible;
            Motion.FromTo(b, UIElement.OpacityProperty, 0, 1, 200, Motion.Out);
        }
        else Motion.To(b, UIElement.OpacityProperty, 0, 120, Motion.Out, done: () => { if (turn == headerStepTurn && actionInView) b.Visibility = Visibility.Collapsed; });
    }

    private void OpenNeighbour(ItemNeighbours.Item? item)
    {
        if (item != null) engine.OpenWeb(item.Url, item.Name);
    }

    /// <summary>The header drawn again when what it says has changed (the title, the course, the next step).</summary>
    private void HeaderCheck(AssignmentData? d)
    {
        var key = d == null ? "" : $"{d.Title}|{d.Context}|{NextStepOf(d)?.Title}|{NextStepOf(d)?.Pending}";
        if (key == headerKey) return;
        headerKey = key;
        HeaderChange();
    }

    // ---- the page ----

    private double PageWidth => Width + 80;
    private double Pad => PadOf(Width);
    private double Room => RoomOf(Width);
    private const double Gap = 28, TwoFrom = 780;
    /// <summary>The widest the left column grows (the instructions and comments: lines that can still be read).</summary>
    private const double ReadMax = 760;

    private static double PadOf(double w) => w + 80 < 560 ? 20 : 40;
    private static double RoomOf(double w) => Math.Min(1480, w + 80 - 2 * PadOf(w));

    /// <summary>The right column's width for the page's room (a little wider as the window grows).</summary>
    private static double SideWidth(double room) => Math.Min(440, Math.Max(290, Math.Round((room - Gap) * 0.32)));

    /// <summary>The page's own width for a width the shell gives: the room, but in two columns no wider than a reading
    /// column and the side beside it (on a large window the rest stays empty at the right, as Windows' Settings pages
    /// do, rather than drawing lines too long to read).</summary>
    private static double RootWidthOf(double w)
    {
        var room = RoomOf(w);
        var used = room >= TwoFrom ? Math.Min(room, ReadMax + Gap + SideWidth(room)) : room;
        return Math.Min(w + 80, used + 2 * PadOf(w));
    }

    /// <summary>The heading as wide as the two columns (a reading column and the side beside it), so Previous, Next and ⋯
    /// end where the side column does on a wide window, not at the window's far edge.</summary>
    public override double? HeaderWidth => Width > 0 ? Math.Max(0, RootWidthOf(Width) - 2 * Pad) : null;

    protected override bool LayoutChanges(double before, double now)
    {
        var wideNow = RoomOf(now) >= TwoFrom;
        if (now > 0) root.Width = RootWidthOf(now);
        // (the side column follows the width without the page being built again)
        if (wideNow && (RoomOf(before) >= TwoFrom) && root.ColumnDefinitions.Count == 4 && PadOf(before) == PadOf(now))
        {
            root.ColumnDefinitions[2].Width = new GridLength(SideWidth(RoomOf(now)));
            return false;
        }
        return (RoomOf(before) >= TwoFrom) != wideNow || PadOf(before) != PadOf(now) || before == 0;
    }

    protected override FrameworkElement Build()
    {
        var d = model.Data;
        HeaderCheck(d);
        if (d == null)
        {
            root.ColumnDefinitions.Clear();
            root.Children.Clear();
            root.Children.Add(K.LoadState(model.Error, () => model.Reload(engine, "assignment", Args)));
            return root;
        }
        var offsets = (one.VerticalOffset, left.VerticalOffset, side.VerticalOffset);
        var pad = Pad;
        var room = Room;
        wide = room >= TwoFrom;
        headerPart = Header(d);
        var main = MainColumn(d);
        root.ColumnDefinitions.Clear();
        root.Children.Clear();
        one.Content = left.Content = side.Content = null;
        // (as wide as the page, up to a reading column and the side beside it, from the left: sized, never shrunk to
        // what it holds)
        root.MaxWidth = 1480 + 2 * pad;
        root.Width = RootWidthOf(Width);
        root.HorizontalAlignment = HorizontalAlignment.Left;
        if (wide)
        {
            // the left scrolls; the right stays put beside it, its top level with the left's first card
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap - 12) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SideWidth(room)) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pad) });
            var leftContent = K.V(32, headerPart, main);
            leftContent.Margin = new Thickness(pad, 0, 12, 40); // (the left's scroll bar in the gap between the columns)
            left.Content = leftContent;
            sideContent = SideColumn(d);
            sideContent.Margin = new Thickness(0, pinTop, 0, 24);
            side.Content = sideContent;
            headerPart.SizeChanged += (_, _) => PinSide();
            sideContent.SizeChanged += (_, _) => PinSide();
            root.Children.Add(left.Col(0));
            root.Children.Add(side.Col(2));
        }
        else
        {
            sideContent = null;
            var stack = K.V(32, headerPart, NarrowColumn(d, main));
            stack.Margin = new Thickness(pad, 0, pad, 40);
            one.Content = stack;
            root.Children.Add(one);
        }
        BuildDropOverlay(d);
        root.Children.Add(dropOverlay);
        Grid.SetColumnSpan(dropOverlay, Math.Max(1, root.ColumnDefinitions.Count));
        // (the scroll where it was, the page built anew under it; then whether the next step is in sight)
        root.Dispatcher.BeginInvoke(() =>
        {
            if (offsets.Item1 > 0) one.ScrollToVerticalOffset(offsets.Item1);
            if (offsets.Item2 > 0) left.ScrollToVerticalOffset(offsets.Item2);
            if (offsets.Item3 > 0) side.ScrollToVerticalOffset(offsets.Item3);
            WatchAction();
        }, DispatcherPriority.Loaded);
        return root;
    }

    /// <summary>The side column's top level with the left's first card; grown taller than the room under that, it moves up
    /// (its foot kept clear of the window's), and only taller than the whole window does it scroll (the Mac's PinnedSide).</summary>
    private void PinSide()
    {
        if (sideContent == null || headerPart == null) return;
        var top = headerPart.ActualHeight + 32;
        var at = Math.Max(18, Math.Min(top, side.ActualHeight - sideContent.ActualHeight - 24));
        if (Math.Abs(at - pinTop) < 0.5 && Math.Abs(sideContent.Margin.Top - at) < 0.5) return;
        pinTop = at;
        sideContent.Margin = new Thickness(0, at, 0, 24);
    }

    // ---- the heading: its kind, its course, where your work stands, the rubric ----

    private static Color Tint(AssignmentData d) => K.Hex(d.Color, Appearance.Accent);

    private static SymbolRegular KindGlyph(string? kind) => (kind ?? "").ToLowerInvariant() switch
    {
        "quiz" => Glyph.Work("quiz"),
        "discussion" => SymbolRegular.ChatMultiple24,
        "tool" => SymbolRegular.PuzzlePiece24,
        _ => SymbolRegular.DocumentText24,
    };

    private FrameworkElement Header(AssignmentData d)
    {
        var color = Tint(d);
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(K.IconTile(KindGlyph(d.Kind), color, 28).Margin(0, 2, 10, 2));
        var kind = K.Text(d.Kind ?? "Assignment", Ty.BodyStrong);
        kind.Foreground = K.Brush(K.Darkened(color));
        kind.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(kind.Margin(0, 0, 10, 0));
        if (!string.IsNullOrEmpty(d.Context))
        {
            var chip = K.Subtle(K.Chip(d.Context!, color, strong: true), () => engine.Go(Place.Home($"courses/{course}")), new Thickness(0));
            K.SetCorner(chip, new CornerRadius(10));
            chip.VerticalAlignment = VerticalAlignment.Center;
            chip.ToolTip = $"Open {d.Context}";
            chip.Cursor = Cursors.Hand;
            row.Children.Add(chip.Margin(0, 0, 8, 0));
        }
        if (d.Status is { Word.Length: > 0 } s) row.Children.Add(K.Chip(s.Word, AssignKit.StatusTone(s.Kind), strong: true).Margin(0, 0, 8, 0));
        var v = K.V(10, row);
        // (the rubric once on the page: here — unless two columns put the grade card beside, which has it)
        if (d.Rubric.Count > 0 && (!wide || d.Grade == null)) v.Children.Add(RubricSummaryRow(d).Margin(0, 4, 0, 0));
        return v;
    }

    // ---- the rubric ----

    /// <summary>Marked and posted, the rubric is the feedback.</summary>
    private static bool RubricMarked(AssignmentData d) => d.Rubric.Count > 0 && new RubricModel(d.Rubric).Graded;

    private void OpenRubric(AssignmentData d, int? open = null, string? view = null) => RubricPopup.Show(d, open, view);

    /// <summary>The screenshot suite's way to the rubric: "" opens it, ":2" opened on the second criterion, ":grid" as the grid.</summary>
    private void OpenRubricShot(string shot, AssignmentData d)
    {
        if (shot == ":grid") OpenRubric(d, view: "grid");
        else if (shot.Length > 1 && int.TryParse(shot[1..], out var k) && k >= 1 && k <= d.Rubric.Count) OpenRubric(d, k - 1, "ring");
        else OpenRubric(d);
    }

    /// <summary>"Lab rubric · 8 / 10", or "Rubric · 10 pts" before it is marked.</summary>
    private static string RubricHeadline(AssignmentData d, RubricModel m)
    {
        var t = (d.RubricTitle ?? "").Trim();
        var name = t.Length == 0 ? "Rubric" : t;
        var score = m.Graded ? $"{RubricRing.Num(m.Earned)} / {RubricRing.Num(m.Possible)}" : $"{RubricRing.Num(m.Possible)} {(m.Possible == 1 ? "pt" : "pts")}";
        return $"{name} · {score}";
    }

    private static string RubricSubline(AssignmentData d, RubricModel m)
    {
        if (m.Graded) return $"{m.Many} · {m.Percent}% · how it was marked";
        if (d.Held == true) return $"{m.Many} · marks not posted yet";
        return $"{m.Many} · how it will be marked";
    }

    /// <summary>The rubric on the page (the Mac's RubricSummaryRow): one row — the ring in miniature, the rubric's name
    /// and score, how many criteria — that brings the ring up over the window; the little ring turns under the pointer.</summary>
    private FrameworkElement RubricSummaryRow(AssignmentData d)
    {
        var m = new RubricModel(d.Rubric);
        var head = RubricHeadline(d, m);
        var sub = RubricSubline(d, m);
        var ring = new RubricMiniRing(m, 36) { RenderTransformOrigin = new Point(0.5, 0.5) };
        var rotate = new RotateTransform(0);
        var scale = new ScaleTransform(1, 1);
        ring.RenderTransform = new TransformGroup { Children = { rotate, scale } };
        var words = K.V(1, K.Text(head, Ty.BodyStrong), K.Text(sub, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var g = K.H(12, ring, words, K.Icon(SymbolRegular.ArrowMaximize24, 14, B.Text2).Margin(8, 0, 0, 0));
        var b = K.CardButton(g, () => OpenRubric(d), 28, new Thickness(10, 9, 18, 9));
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.ToolTip = "Open the rubric";
        System.Windows.Automation.AutomationProperties.SetName(b, $"{head}, {sub}");
        System.Windows.Automation.AutomationProperties.SetHelpText(b, "Opens the rubric ring over the window");
        b.MouseEnter += (_, _) =>
        {
            Motion.To(rotate, RotateTransform.AngleProperty, 18, 500, Motion.Gentle);
            Motion.To(scale, ScaleTransform.ScaleXProperty, 1.08, 500, Motion.Gentle);
            Motion.To(scale, ScaleTransform.ScaleYProperty, 1.08, 500, Motion.Gentle);
        };
        b.MouseLeave += (_, _) =>
        {
            Motion.To(rotate, RotateTransform.AngleProperty, 0, 500, Motion.Gentle);
            Motion.To(scale, ScaleTransform.ScaleXProperty, 1, 500, Motion.Gentle);
            Motion.To(scale, ScaleTransform.ScaleYProperty, 1, 500, Motion.Gentle);
        };
        return b;
    }

    // ---- doing ----

    /// <summary>Something handed in (or graded) to look back at.</summary>
    private static bool HasWork(AssignmentData d) =>
        !string.IsNullOrEmpty(d.Submitted) || d.Grade != null || (d.Submission?.Files?.Count ?? 0) > 0
        || !string.IsNullOrEmpty(d.Submission?.Text) || !string.IsNullOrEmpty(d.Submission?.Url);

    /// <summary>A quiz with an attempt in: handed in, or graded.</summary>
    private static bool QuizTaken(AssignmentData d) => !string.IsNullOrEmpty(d.Submitted) || d.Grade != null;

    /// <summary>The one thing to do next (the lit one, where there are two; never a step that cannot be taken, where there
    /// is another).</summary>
    private AssignStep? NextStepOf(AssignmentData d) => NextSteps(d) is var all && all.LastOrDefault(s => !s.Off) is { } one ? one : all.LastOrDefault();

    /// <summary>New attempt was held beside See feedback while the quiz's own count was read.</summary>
    private bool againHeld;

    /// <summary>What to do next: hand in (again), take the quiz, open the discussion or the tool. A quiz taken: See
    /// feedback where its results can be seen, and New attempt beside it while an attempt is left — New attempt alone where
    /// its results are hidden. Until the quiz's own intro is in, New attempt waits beside See feedback (greyed, a spinner
    /// in it) wherever the assignment says an attempt is left, so the buttons never move when it answers. Handed in, and
    /// it may be handed in again: what was handed in (its feedback, once there is any) beside Hand in again.</summary>
    private List<AssignStep> NextSteps(AssignmentData d)
    {
        if (d.QuizId != null && QuizTaken(d))
        {
            var underWay = quizIntro?.Begin.StartsWith("Continue") == true;
            if (underWay) return new() { new AssignStep("Continue quiz", Glyph.Work("quiz"), "Go back to the attempt under way", Again: true) };
            // (before the intro is in: its feedback, as the quiz was taken)
            var asking = quizIntro == null && !quizIntroFailed;
            var feedback = quizIntro == null || quizIntro.Last?.Feedback == true;
            var again = quizIntro != null ? quizIntro.CanStart : asking && AttemptLeft(d);
            var o = new List<AssignStep>();
            if (feedback) o.Add(new AssignStep("See feedback", SymbolRegular.Comment24, "See your last attempt’s results", "Feedback", Feedback: true));
            if (again)
            {
                o.Add(new AssignStep("New attempt", SymbolRegular.ArrowCounterclockwise24, asking ? "Checking the attempts left…" : "Take this quiz again", "Retake", Again: true, Pending: asking));
                if (asking) againHeld = true;
            }
            // (the place New attempt was held in while the quiz was asked, kept once it says no attempt is left — the
            //  button greyed, saying so — so See feedback never grows into its room as the answer comes)
            else if (againHeld && quizIntro != null && feedback)
                o.Add(new AssignStep("No attempts left", SymbolRegular.ArrowCounterclockwise24, string.IsNullOrEmpty(quizIntro.AttemptsText) ? "Every attempt this quiz allows is used" : quizIntro.AttemptsText!, "None left", Again: true, Off: true));
            if (o.Count > 0) return o;
        }
        if (d.CanSubmit && d.Resubmit == true && HasWork(d))
        {
            var marked = d.Grade != null || d.Comments.Count > 0;
            var look = marked
                ? new AssignStep("See feedback", SymbolRegular.Comment24, "Your work, with your teacher's marks and comments", "Feedback", Feedback: true)
                : new AssignStep("View submission", SymbolRegular.DocumentSearch24, "What you handed in", "Submission", Feedback: true);
            if (NextStepOne(d) is { } again) return new() { look, again };
        }
        return NextStepOne(d) is { } one ? new() { one } : new();
    }

    /// <summary>Whether the assignment's own count leaves an attempt ("1 of 2 attempts used"; no limit: "", "Attempt 3").</summary>
    private static bool AttemptLeft(AssignmentData d)
    {
        var m = System.Text.RegularExpressions.Regex.Match(d.AttemptsText ?? "", @"^(\d+) of (\d+)");
        return !m.Success || int.Parse(m.Groups[1].Value) < int.Parse(m.Groups[2].Value);
    }

    private static AssignStep? NextStepOne(AssignmentData d)
    {
        if (d.CanSubmit)
            return d.Resubmit == true
                ? new AssignStep("Hand in again", SymbolRegular.ArrowUpload24, "Hand in this assignment again", "Hand in")
                : new AssignStep("Hand in", SymbolRegular.ArrowUpload24, "Hand in this assignment");
        if (d.QuizUrl != null) return new AssignStep("Take quiz", Glyph.Work("quiz"), "Take this quiz");
        if (d.DiscussionUrl != null) return new AssignStep("Open discussion", SymbolRegular.ChatMultiple24, "Open this assignment’s discussion");
        if (d.ToolUrl != null)
            return d.LtiQuiz == true
                ? new AssignStep("Take quiz", Glyph.Work("quiz"), "Open this quiz in a window of its own")
                : new AssignStep("Open tool", SymbolRegular.PuzzlePiece24, "Open this assignment’s tool in a window of its own");
        return null;
    }

    private void Act(AssignmentData d, AssignStep? step = null)
    {
        if (step is { Pending: true } or { Off: true }) return; // (the quiz's attempts still being read)
        if (step is { Feedback: true } && d.QuizId == null) Review(d, d.Submission?.Files?.FirstOrDefault()); // (work handed in, looked at in the popup)
        else if (step != null && d.QuizId is { } q && (step.Feedback || step.Again))
            engine.OpenQuiz(new QuizLaunch(course, q, d.Title) { Begin = step.Again, Feedback = step.Feedback });
        else if (NextSteps(d).LastOrDefault(x => !x.Pending && !x.Off) is { } s && (s.Feedback || s.Again)) Act(d, s);
        else if (d.CanSubmit) OpenHandIn(d);
        else if (d.QuizId is { } quiz) engine.OpenQuiz(new QuizLaunch(course, quiz, d.Title));
        else if (d.QuizUrl is { } qu) engine.GoTo(qu, d.Title);
        else if (d.DiscussionUrl is { } du) engine.GoTo(du, d.Title);
        else if (d.ToolUrl != null)
        {
            if (d.Alta == true) engine.OpenAlta(new AltaLaunch(course, id, d.Title)); // (Knewton Alta, as a quiz)
            else if (d.Macmillan == true) engine.OpenMacmillan(new AltaLaunch(course, id, d.Title)); // (Macmillan Learning)
            else engine.OpenTool(ToolLaunch.Assignment(course, id, d.Title));
        }
    }

    /// <summary>Arrived from a piece of work's own action: its Hand In done at once, or its feedback shown (a quiz's in the
    /// quiz screen; a marked rubric's ring over the window; a file handed in with its comments; else the page goes to the
    /// grade or the comments, and lights it).</summary>
    private void Arrived(AssignmentData d)
    {
        if (engine.Arrival is not { } a || a.Id != id) return;
        engine.Arrival = null;
        if (a.Feedback)
        {
            if (d.QuizId is { } q) engine.OpenQuiz(new QuizLaunch(course, q, d.Title) { Feedback = true });
            else if (d.Grade != null && RubricMarked(d)) OpenRubric(d);
            else if (d.Submission?.Files?.FirstOrDefault() is { } f) Review(d, f);
            else if (d.Grade != null) Later(80, () => Jump("grade", false));
            else if (d.Comments.Count > 0) Later(80, () => Jump("comments", false));
        }
        else if (NextStepOf(d) != null) Act(d);
    }

    private void OpenHandIn(AssignmentData d, IReadOnlyList<string>? files = null, string? link = null)
    {
        if (HandInSheet.Open) return;
        HandInSheet.Show(engine, d, files ?? Array.Empty<string>(), link, HandedInHere);
    }

    private void Review(AssignmentData d, Attachment? file) => SubmissionReview.Show(engine, d, file);

    /// <summary>The sheet away first, then the work arrives where it can be seen: the page goes to it and its seal bounces.</summary>
    private async void HandedInHere()
    {
        await Task.Delay(350);
        jumpAfterLoad = "work";
        bounceAfterLoad = true;
        model.Again();
        engine.DataChanged();
    }

    private void StartCommenting()
    {
        commenting = true;
        composer = new AssignComposer(SendComment, StopCommenting);
        Render();
    }

    private void StopCommenting()
    {
        commenting = false;
        composer = null;
        Render();
    }

    private async Task SendComment(string text)
    {
        await engine.Call<OK>("commentOn", new { course, id, text });
        commenting = false;
        composer = null;
        model.Reload(engine, "assignment", Args);
        Render();
    }

    /// <summary>A web address handed in: the app's own screen when it is one of the school's, else the browser.</summary>
    private void Visit(string link) => engine.GoTo(link, "");

    /// <summary>A part of the page brought into view (the work after a hand-in; the grade or the comments for feedback):
    /// lit in the course's colour for a moment, or — the work — its seal bouncing.</summary>
    private void Jump(string target, bool bounce)
    {
        var el = target switch { "work" => workPart, "grade" => gradePart, "comments" => commentsPart, _ => null };
        if (el == null || !el.IsLoaded) return;
        if (FindScroller(el) is { Content: UIElement content } sv)
        {
            try
            {
                var p = el.TranslatePoint(new Point(0, 0), content);
                AssignScroll.To(sv, p.Y - 12);
            }
            catch { }
        }
        if (target == "work")
        {
            if (bounce && seal != null) Later(300, () => { if (seal != null) AssignBounce.Bounce(seal); });
            return;
        }
        var lit = target == "grade" ? gradeLit : commentsLit;
        if (lit == null) return;
        Motion.To(lit, UIElement.OpacityProperty, 1, Motion.Medium);
        Later(1600, () => Motion.To(lit, UIElement.OpacityProperty, 0, Motion.Slow));
    }

    private static ScrollViewer? FindScroller(DependencyObject o)
    {
        for (var p = VisualTreeHelper.GetParent(o); p != null; p = VisualTreeHelper.GetParent(p))
            if (p is ScrollViewer s) return s;
        return null;
    }

    // ---- files dropped on the page ----

    /// <summary>Whether something dropped on the page can be handed in: files, or a web address, as the assignment takes them.</summary>
    private static bool TakesDrops(AssignmentData d) => d.CanSubmit && (d.Types.Contains("online_upload") || d.Types.Contains("online_url"));

    internal static (List<string> Files, List<string> Links) Dropped(IDataObject data)
    {
        var files = new List<string>();
        var links = new List<string>();
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths) files.AddRange(paths);
            string? text = null;
            if (data.GetDataPresent("UniformResourceLocatorW") && data.GetData("UniformResourceLocatorW") is System.IO.MemoryStream ms)
                text = System.Text.Encoding.Unicode.GetString(ms.ToArray()).TrimEnd('\0');
            else if (data.GetDataPresent(DataFormats.UnicodeText)) text = data.GetData(DataFormats.UnicodeText) as string;
            if (text != null)
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (Uri.TryCreate(line, UriKind.Absolute, out var u) && u.Scheme is "http" or "https") links.Add(u.ToString());
        }
        catch { }
        return (files, links);
    }

    private void DragOver(object sender, DragEventArgs e)
    {
        var (files, links) = Dropped(e.Data);
        if (files.Count == 0 && links.Count == 0) return; // (words dragged: the comment box takes them)
        if (model.Data is not { } d || !TakesDrops(d) || HandInSheet.Open)
        {
            if (files.Count > 0) { e.Effects = DragDropEffects.None; e.Handled = true; }
            return;
        }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        if (dropShown) return;
        dropShown = true;
        dropOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        dropOverlay.Visibility = Visibility.Visible;
        Motion.FadeIn(dropOverlay, Motion.Fast);
    }

    private void DragLeave(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(root);
        if (p.X > 2 && p.Y > 2 && p.X < root.ActualWidth - 2 && p.Y < root.ActualHeight - 2) return; // (from one part of the page to another)
        HideDrop();
    }

    private void HideDrop()
    {
        if (!dropShown) return;
        dropShown = false;
        Motion.FadeOut(dropOverlay, () => { if (!dropShown) dropOverlay.Visibility = Visibility.Collapsed; });
    }

    private void Drop(object sender, DragEventArgs e)
    {
        HideDrop();
        if (model.Data is not { } d || !TakesDrops(d) || HandInSheet.Open) return;
        var (files, links) = Dropped(e.Data);
        if (files.Count == 0 && links.Count == 0) return;
        e.Handled = true;
        OpenHandIn(d, files, links.FirstOrDefault());
    }

    /// <summary>While files are dragged over the page: where they go.</summary>
    private void BuildDropOverlay(AssignmentData d)
    {
        var color = Tint(d);
        dropOverlay.Children.Clear();
        var frame = new Rectangle
        {
            RadiusX = 12, RadiusY = 12, Stroke = K.Brush(color), StrokeThickness = 2.5, StrokeDashArray = new DoubleCollection { 4, 2.6 },
            Fill = K.Brush(K.WithAlpha(color, 0.06)), Margin = new Thickness(14),
        };
        dropOverlay.Children.Add(frame);
        var words = K.V(8,
            K.Icon(SymbolRegular.ArrowUpload24, 32, color, filled: true),
            K.Text(d.Resubmit == true ? "Drop to hand in again" : "Drop to hand in", Ty.Subtitle).Center(),
            K.Text(d.Title, Ty.Body, B.Text2).Center());
        var card = new Border { Child = words, Padding = new Thickness(30, 22, 30, 22), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = K.Shadow(32, 8, 0.2), MaxWidth = 420 };
        card.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        dropOverlay.Children.Add(card);
        if (!dropShown) dropOverlay.Visibility = Visibility.Collapsed;
    }
}
