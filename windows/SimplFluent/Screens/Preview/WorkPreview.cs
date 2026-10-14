using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>A piece of work as its row knows it (the Mac's PreviewItem): enough for the card to come up at once, the
/// rest read as it opens.</summary>
public sealed class PreviewItem
{
    public string Id = "";
    public string Title = "";
    /// <summary>What it is ("Assignment", "Quiz", "Discussion", "Page", "Event", "My task" …).</summary>
    public string? Kind;
    public SymbolRegular Symbol = SymbolRegular.DocumentText24;
    public string? Course;
    public string? Color;
    /// <summary>When it is due or happens ("Due today at 11:59 PM").</summary>
    public string? When;
    public string? Points;
    public List<WorkFlag> Flags = new();
    public string? Detail;
    public string? Excerpt;
    public bool? Done;
    public string? Url;
    /// <summary>A kind of work that waits on you: Hand In, Take Quiz, Reply (by its kind), or See Feedback once handed in.</summary>
    public (string? Kind, bool HandedIn, bool Open)? Work;
    /// <summary>Ticks it off or back (rows with a tick): Mark Done on the card.</summary>
    public Action<bool>? Toggle;
    /// <summary>What a click on the row did before the preview: the work opened whole.</summary>
    public Action? OpenWhole;
    public string OpenLabel = "Open";
}

/// <summary>
/// A quick look at a piece of work (the Mac's WorkPreview and WorkPreviewOverlay; the web's preview.js): a click on a row
/// of work grows a card out of that row with what the work is — its kind and course, its title, when it is due, its
/// points, where it stands and the first words of what it says — with the way through to it (Open, or Enter), Hand In or
/// Take Quiz where it waits on you, and Mark Done for a row with a tick. A click round it, Escape or Close folds it back
/// into the row. A double-click (or a Ctrl+click) on a row opens the work whole. On Windows the card is a flyout surface
/// over the page (the shell's overlay), rising out of its row as the Dashboard's counters do.
/// </summary>
public static class WorkPreview
{
    private static Canvas? host;
    private static Engine.Engine? engine;
    private static Border? card;
    private static FrameworkElement? anchor;
    private static PreviewItem? item;
    private static (string Id, DateTime At)? lastPress;
    private static int token;

    /// <summary>The card's width (the Mac's about 460 points).</summary>
    private const double CardWidth = 440;

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDoubleClickTime();

    /// <summary>The shell's overlay over the page, where the card is drawn.</summary>
    public static void Attach(Canvas overlay, Engine.Engine e)
    {
        host = overlay;
        engine = e;
        overlay.Background = null;
    }

    public static bool IsOpen => card != null;

    /// <summary>A row pressed: its preview — or, a double-click or Ctrl+click, the work itself. The row whose card is
    /// out, pressed again, folds it back.</summary>
    public static void Press(PreviewItem it, FrameworkElement from)
    {
        var now = DateTime.UtcNow;
        var again = lastPress is { } lp && lp.Id == it.Id && (now - lp.At).TotalMilliseconds <= GetDoubleClickTime();
        lastPress = (it.Id, now);
        if (it.OpenWhole != null && (again || (Keyboard.Modifiers & ModifierKeys.Control) != 0))
        {
            lastPress = null;
            Dismiss();
            it.OpenWhole();
            return;
        }
        if (again) return;
        if (item?.Id == it.Id && card != null) { Close(); return; }
        Open(it, from);
    }

    private static void Open(PreviewItem it, FrameworkElement from)
    {
        if (host == null || engine == null) { it.OpenWhole?.Invoke(); return; }
        Dismiss();
        item = it;
        anchor = from;
        var t = ++token;
        var room = host.ActualWidth;
        // (one width, whatever it grew from — a month's narrow chip as a Dashboard row — as the Mac's card has: its
        // header, its course and its Close side by side; narrower only on a page too narrow for it)
        var w = Math.Min(CardWidth, Math.Max(280, room - 24));
        var from0 = RowRect(from);
        var face = new CardFace(it, engine, w);
        face.Measure(new Size(w, double.PositiveInfinity));
        var h = face.DesiredSize.Height;
        // below its row where there is room, else over it; level with the row's start, kept inside the page (a row no
        // longer shown — the page drawn again under the press — has no place: the middle of the page, a third down)
        double x, y;
        if (from0.IsEmpty)
        {
            x = Math.Max(12, (room - w) / 2);
            y = Math.Max(12, (host.ActualHeight - h) / 3);
        }
        else
        {
            x = Math.Clamp(from0.X, 12, Math.Max(12, room - w - 12));
            var below = from0.Bottom + 6;
            y = below + h <= host.ActualHeight - 12 ? below : Math.Max(12, from0.Y - h - 6);
        }
        if (y + h > host.ActualHeight - 12) y = Math.Max(12, host.ActualHeight - 12 - h);
        var target = new Rect(x, y, w, h);
        card = new Border
        {
            CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = new Canvas { Children = { face } },
            Effect = K.Shadow(36, 12, 0.24), SnapsToDevicePixels = true,
        };
        card.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        Shell.Tour.Mark(card, Shell.TourSpot.Preview);
        card.MouseDown += (_, e) => e.Handled = true;
        host.Children.Add(card);
        face.Resized += hh =>
        {
            if (card == null || t != token || host == null) return;
            // (grown with what it read: still inside the page — moved up as far as it must, no taller than the page)
            var room2 = host.ActualHeight - 24;
            var nh = Math.Min(hh + 2, room2);
            var top = Canvas.GetTop(card);
            var bottom = host.ActualHeight - 12;
            var ny = top + nh > bottom ? Math.Max(12, bottom - nh) : top;
            Motion.To(card, FrameworkElement.HeightProperty, nh, Motion.Medium, Motion.Decelerate);
            if (Math.Abs(ny - top) > 0.5) Motion.To(card, Canvas.TopProperty, ny, Motion.Medium, Motion.Decelerate);
        };
        if (!Motion.On || from0.IsEmpty) { Place(card, target); return; }
        Place(card, from0);
        face.Opacity = 0;
        Motion.FromTo(card, Canvas.LeftProperty, from0.X, target.X, Motion.Slow + 67, Motion.Decelerate);
        Motion.FromTo(card, Canvas.TopProperty, from0.Y, target.Y, Motion.Slow + 67, Motion.Decelerate);
        Motion.FromTo(card, FrameworkElement.WidthProperty, from0.Width, target.Width, Motion.Slow + 67, Motion.Decelerate);
        Motion.FromTo(card, FrameworkElement.HeightProperty, from0.Height, target.Height, Motion.Slow + 67, Motion.Decelerate);
        Motion.FadeIn(face, Motion.Medium, 90);
        Support.Shot.Moved();
    }

    private static Rect RowRect(FrameworkElement row)
    {
        if (host == null || !row.IsLoaded || !row.IsVisible) return Rect.Empty;
        try
        {
            var p = row.TranslatePoint(new Point(0, 0), host);
            return new Rect(p, new Size(row.ActualWidth, row.ActualHeight));
        }
        catch { return Rect.Empty; }
    }

    private static void Place(FrameworkElement e, Rect r)
    {
        foreach (var dp in new[] { Canvas.LeftProperty, Canvas.TopProperty, FrameworkElement.WidthProperty, FrameworkElement.HeightProperty }) e.BeginAnimation(dp, null);
        Canvas.SetLeft(e, r.X);
        Canvas.SetTop(e, r.Y);
        e.Width = r.Width;
        e.Height = r.Height;
    }

    /// <summary>Folded back into its row, then gone.</summary>
    public static void Close()
    {
        var c = card;
        var row = anchor;
        if (c == null || host == null) return;
        card = null;
        item = null;
        lastPress = null;
        var to = row != null ? RowRect(row) : Rect.Empty;
        void Gone() => host?.Children.Remove(c);
        if (!Motion.On || to.IsEmpty) { Gone(); return; }
        if (c.Child is Canvas cv && cv.Children.Count > 0) Motion.FadeOut((UIElement)cv.Children[0], () => { }, Motion.Fast);
        var from = new Rect(Canvas.GetLeft(c), Canvas.GetTop(c), c.ActualWidth, c.ActualHeight);
        Motion.FromTo(c, Canvas.LeftProperty, from.X, to.X, Motion.Medium, Motion.Standard);
        Motion.FromTo(c, Canvas.TopProperty, from.Y, to.Y, Motion.Medium, Motion.Standard);
        Motion.FromTo(c, FrameworkElement.WidthProperty, from.Width, to.Width, Motion.Medium, Motion.Standard);
        Motion.FromTo(c, FrameworkElement.HeightProperty, from.Height, to.Height, Motion.Medium, Motion.Standard, 0, Gone);
        Motion.FadeOut(c, () => { }, Motion.Medium);
    }

    /// <summary>Gone at once (the work opened whole, another screen shown).</summary>
    public static void Dismiss()
    {
        token++;
        if (card != null) host?.Children.Remove(card);
        card = null;
        item = null;
        anchor = null;
    }

    /// <summary>Open: the work whole, as its row opened it before.</summary>
    internal static void OpenWhole()
    {
        var open = item?.OpenWhole;
        Dismiss();
        open?.Invoke();
    }

    /// <summary>A click anywhere outside the card folds it back (the click still does what it does).</summary>
    public static void ClickedAt(Point p)
    {
        if (card == null) return;
        var r = new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), card.ActualWidth, card.ActualHeight);
        if (!r.Contains(p) && !(anchor?.IsMouseOver ?? false)) Close();
    }

    // ---- from the rows (the Mac's PreviewItem.work, .dash, .sheet) ----

    /// <summary>A planner item's kind in words, by its type.</summary>
    public static string? PlannerKind(string? type) => (type ?? "") switch
    {
        "assignment" or "" => "Assignment", "quiz" => "Quiz", "discussion_topic" => "Discussion", "wiki_page" => "Page",
        "calendar_event" => "Event", "planner_note" => "My task", "announcement" => "Announcement", "assessment_request" => "Peer review",
        _ => null,
    };

    /// <summary>A planner type that is work to hand in (it is due, and has a Hand In or a Take Quiz).</summary>
    public static bool IsWork(string? type) => (type ?? "") is "assignment" or "quiz" or "discussion_topic" or "";

    /// <summary>"Due today at 11:59 PM", "Saturday at 3:00 PM", "Tomorrow, all day".</summary>
    public static string? When(string? day, string? time, bool due)
    {
        var d = (day ?? "").Trim();
        var t = (time ?? "").Trim();
        if (d.Length == 0 && t.Length == 0) return null;
        var phrase = d.Length == 0 ? t : t.Length == 0 ? d : t.Equals("all day", StringComparison.OrdinalIgnoreCase) ? $"{d}, all day" : $"{d} at {t}";
        if (!due) return phrase;
        if (d is "Today" or "Tomorrow" or "Yesterday") return "Due " + d.ToLowerInvariant() + phrase[d.Length..];
        return "Due " + phrase;
    }

    /// <summary>"10 pts" out of a row's line ("Assignment · 10 pts · due 11:59 PM").</summary>
    public static string? PointsIn(string? text) =>
        text?.Split(" · ").Select(x => x.Trim()).FirstOrDefault(x => x.EndsWith(" pts") || x.EndsWith(" pt"));

    /// <summary>A row of the Dashboard's List, under its day.</summary>
    public static PreviewItem Dash(DashRow row, string? day, Engine.Engine engine, Action<bool>? toggle)
    {
        var type = row.Type ?? row.Id.Split(':')[0];
        var custom = row.Custom == true;
        var kind = string.IsNullOrWhiteSpace(row.Kind) ? PlannerKind(type) : row.Kind;
        var it = new PreviewItem
        {
            Id = "dash:" + row.Id, Title = row.Title, Kind = kind, Symbol = Glyph.Work(type), Color = row.Color,
            Course = row.Course == kind ? null : row.Course, When = When(day, row.Time, !custom && IsWork(type)), Points = row.Points,
            Flags = row.Flags ?? new(), Done = row.Done, Toggle = toggle,
        };
        if (!custom && !string.IsNullOrWhiteSpace(row.Url))
        {
            it.Url = row.Url;
            if (IsWork(type)) it.Work = (type, row.Done, true);
            it.OpenWhole = () => engine.OpenWeb(row.Url!, row.Title);
        }
        return it;
    }

    /// <summary>A row of work in Today's list or To Do (its planner kind from its id, "assignment:12").</summary>
    public static PreviewItem Work(WorkRow row, Engine.Engine engine, Action<bool>? toggle)
    {
        var type = row.Type ?? row.Id.Split(':')[0];
        var custom = row.Custom == true;
        var kind = custom ? "My task" : PlannerKind(type);
        var it = new PreviewItem
        {
            Id = "work:" + row.Id, Title = row.Title, Kind = kind, Symbol = custom ? SymbolRegular.CheckboxChecked24 : Glyph.Work(type),
            Color = row.Color, Course = new[] { row.CourseName, row.Course }.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && c != kind),
            When = When(DayWord(row.Date), row.Time, !custom && IsWork(type)), Points = PointsIn(row.Meta ?? row.Sub),
            Flags = row.Flag != null ? new() { row.Flag } : new(), Detail = custom ? row.Meta : null, Done = row.Done, Toggle = toggle,
        };
        if (!custom && !string.IsNullOrWhiteSpace(row.Url))
        {
            it.Url = row.Url;
            if (IsWork(type)) it.Work = (type, row.Done, true);
            it.OpenWhole = () => engine.OpenWeb(row.Url!, row.Title);
        }
        return it;
    }

    /// <summary>A row of a counter's panel: its line under the title is "course · kind · points · when".</summary>
    public static PreviewItem Sheet(SheetRow row, string counter, Engine.Engine engine)
    {
        var parts = (row.Sub ?? "").Split(" · ").Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        var (kind, sym) = counter == "unread" ? ("Announcement", SymbolRegular.Megaphone24) : KindOf(row.Url) ?? ("Assignment", SymbolRegular.DocumentText24);
        var kindWords = new HashSet<string> { "Assignment", "Quiz", "Discussion", "graded", "Page", "Event", "Peer review", "Announcement", "My task" };
        var rest = parts.Skip(1).Where(x => !kindWords.Contains(x) && !x.EndsWith(" pts") && !x.EndsWith(" pt")).ToList();
        var it = new PreviewItem
        {
            Id = $"sheet:{counter}:{row.Key ?? row.Url ?? row.Title}", Title = row.Title, Kind = kind, Symbol = sym, Color = row.Color,
            Course = parts.FirstOrDefault() is { } c && c != "—" ? c : null, Points = PointsIn(row.Sub),
            Detail = rest.Count == 0 ? null : char.ToUpperInvariant(string.Join(" · ", rest)[0]) + string.Join(" · ", rest)[1..],
        };
        if (counter == "overdue" && row.Quiet != true) it.Flags = new() { new WorkFlag { Word = "Overdue", Kind = "bad" } };
        if (!string.IsNullOrWhiteSpace(row.Url))
        {
            it.Url = row.Url;
            it.OpenWhole = () => engine.OpenWeb(row.Url!, row.Title);
            if (counter != "unread") it.Work = (kind.ToLowerInvariant(), counter == "graded" || row.Quiet == true, true);
        }
        return it;
    }

    /// <summary>What an address is, by its path (…/assignments/12 is an assignment), with its symbol.</summary>
    public static (string, SymbolRegular)? KindOf(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(new Uri("https://canvas.invalid"), url, out var u)) return null;
        var p = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3) return null;
        return p[2] switch
        {
            "assignments" => ("Assignment", SymbolRegular.DocumentText24),
            "quizzes" => ("Quiz", SymbolRegular.TaskListLtr24),
            "discussion_topics" => ("Discussion", SymbolRegular.ChatMultiple24),
            "announcements" => ("Announcement", SymbolRegular.Megaphone24),
            "pages" or "wiki" => ("Page", SymbolRegular.DocumentOnePage24),
            "files" => ("File", SymbolRegular.Document24),
            "calendar_events" => ("Event", SymbolRegular.CalendarLtr24),
            _ => null,
        };
    }

    /// <summary>A day in words, as the Dashboard's List names them: Today, Tomorrow, a weekday this week, else its date.</summary>
    public static string? DayWord(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso) || !DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at)) return null;
        var d = at.LocalDateTime.Date;
        var today = DateTime.Today;
        if (d == today) return "Today";
        if (d == today.AddDays(1)) return "Tomorrow";
        if (d == today.AddDays(-1)) return "Yesterday";
        var apart = (d - today).TotalDays;
        if (apart > 0 && apart < 7) return d.ToString("dddd", CultureInfo.InvariantCulture);
        return d.Year == today.Year ? d.ToString("MMM d", CultureInfo.InvariantCulture) : d.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }

    // ---- the card's face ----

    private sealed class CardFace : Border
    {
        private readonly PreviewItem item;
        private readonly Engine.Engine engine;
        private readonly double width;
        private AssignmentData? assignment;
        private PageData? page;
        private string? excerpt;
        /// <summary>What the work says, as its HTML (its lists, bold and links kept), once it is read.</summary>
        private string? excerptHtml;
        private bool loading;
        public event Action<double>? Resized;

        public CardFace(PreviewItem item, Engine.Engine engine, double width)
        {
            this.item = item;
            this.engine = engine;
            this.width = width;
            Width = width - 2;
            Draw();
            _ = Load();
        }

        private Color Tint => K.Hex(assignment?.Color ?? page?.Color ?? item.Color, Support.Appearance.Accent);

        private void Draw()
        {
            var kicker = string.Join(" · ", new[] { Text(assignment?.Kind) ?? item.Kind, Text(assignment?.Context) ?? Text(page?.Context) ?? item.Course }.Where(s => !string.IsNullOrEmpty(s)));
            var kick = K.Text(kicker, Ty.Body);
            kick.FontWeight = FontWeights.SemiBold;
            kick.Foreground = K.Brush(K.Darkened(Tint));
            var close = K.Button("Close", Close, icon: SymbolRegular.Dismiss24);
            close.MinWidth = 0;
            close.ToolTip = "Close (Esc, or click anywhere else)";
            var head = K.Columns("auto, *, auto, auto",
                K.IconTile(item.Symbol, Tint, 26).Col(0),
                kick.Margin(9, 0, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1),
                loading ? K.Spinner(14).Margin(0, 0, 10, 0).Col(2) : null,
                close.Col(3));
            var title = K.Wrapped(assignment?.Title ?? page?.Title ?? item.Title, Ty.BodyLarge, B.Text, 3);
            title.FontWeight = FontWeights.SemiBold;
            var v = K.V(12, head, title);

            var chips = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var f in Chips()) chips.Children.Add(K.Chip(f.Word, K.Tone(f.Kind)).Margin(0, 0, 6, 6));
            if (chips.Children.Count > 0) v.Children.Add(chips);

            var facts = K.V(6);
            foreach (var (sym, text, strong) in Facts())
            {
                var t = K.Wrapped(text, Ty.Body, strong ? B.Text : B.Text2);
                if (strong) t.FontWeight = FontWeights.Medium;
                facts.Children.Add(K.Columns("auto, *", K.Icon(sym, 14, B.Text2).Margin(0, 3, 9, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), t.Col(1)));
            }
            if (facts.Children.Count > 0) v.Children.Add(facts);
            if (excerptHtml is { Length: > 0 } html && Formatted(html) is { } rich) v.Children.Add(rich);
            else if ((excerpt ?? item.Excerpt) is { Length: > 0 } ex) v.Children.Add(K.Wrapped(ex, Ty.Body, B.Text2, 5));

            // what can be done: Mark Done at the start; its own action and Open at the end
            var actions = K.Columns("auto, *, auto");
            if (item.Toggle != null && item.Done is { } done)
            {
                var mark = K.Button(done ? "Mark not done" : "Mark done", () => { item.Done = !done; item.Toggle?.Invoke(!done); Redraw(); }, icon: done ? SymbolRegular.ArrowUndo24 : SymbolRegular.Checkmark24);
                actions.Children.Add(mark.Col(0));
            }
            var right = K.H(8);
            if (Action() is { } a)
            {
                var run = a.Run;
                right.Children.Add(K.Button(a.Title, () => { Dismiss(); run(); }, icon: a.Symbol));
            }
            if (item.OpenWhole != null)
            {
                var open = K.Button(item.OpenLabel, OpenWhole, accent: true);
                open.IsDefault = true;
                right.Children.Add(open);
            }
            actions.Children.Add(right.Col(2));
            v.Children.Add(actions.Margin(0, 6, 0, 0));
            Child = v;
            Padding = new Thickness(20, 14, 14, 18);
        }

        private void Redraw()
        {
            Draw();
            Measure(new Size(width - 2, double.PositiveInfinity));
            Resized?.Invoke(DesiredSize.Height);
        }

        private static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        /// <summary>
        /// Where it stands: the assignment's own word and grade once read (with the row's Feedback or New), else the
        /// row's. A graded piece says its score once — one chip, the engine's "13/16 · B−" — with no "Graded" (or, from a
        /// page a version older, the same score again) beside it; Late, Missing and Submitted stay, and a late penalty
        /// ("−3 pts late") says Late for itself.
        /// </summary>
        private IEnumerable<WorkFlag> Chips()
        {
            if (assignment is not { } d) return item.Flags;
            var outList = new List<WorkFlag>();
            var graded = d.Grade?.Score != null;
            var late = Text(d.Grade?.Late);
            if (d.Status is { Word.Length: > 0 } s)
            {
                var word = s.Word.ToLowerInvariant();
                var sameScore = graded && s.Word.Replace(" ", "") == $"{Num(d.Grade!.Score!.Value)}/{Num(d.Grade.Possible ?? 0)}";
                if (!(graded && word == "graded") && !sameScore && !(late != null && word == "late"))
                    outList.Add(new WorkFlag { Word = s.Word, Kind = s.Kind });
            }
            if (d.Grade is { Score: { } score } g)
            {
                var text = $"{Num(score)}/{Num(g.Possible ?? 0)}";
                if (Text(g.Letter) is { } letter) text += $" · {letter}";
                outList.Add(new WorkFlag { Word = Text(g.Chip) ?? text, Kind = "info" });
            }
            if (late != null) outList.Add(new WorkFlag { Word = late, Kind = "warn" });
            var words = outList.Select(x => x.Word).ToHashSet();
            foreach (var f in item.Flags) if (f.Word.ToLowerInvariant() is "feedback" or "new" && !words.Contains(f.Word)) outList.Add(f);
            return outList;
        }

        private static string Num(double v) => v % 1 == 0 ? ((long)v).ToString(CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);

        private IEnumerable<(SymbolRegular, string, bool)> Facts()
        {
            if (item.When is { } w) yield return (SymbolRegular.Clock24, w, true);
            else if (assignment is { } d) yield return (SymbolRegular.Clock24, Text(d.Due) is { } due ? $"Due {due}" : "No due date", true);
            if ((item.Points ?? Text(assignment?.Points)) is { } p) yield return (SymbolRegular.NumberSymbolSquare24, p, false);
            if (Text(assignment?.Submitted) is { } s) yield return (SymbolRegular.CheckmarkStarburst24, s, false);
            if (Text(assignment?.Available) is { } a) yield return (SymbolRegular.LockOpen24, a, false);
            if (Text(page?.Edited) is { } e) yield return (SymbolRegular.Edit24, e, false);
            if (Text(item.Detail) is { } dt) yield return (SymbolRegular.Info24, dt, false);
        }

        private (string Title, SymbolRegular Symbol, Action Run)? Action()
        {
            if (item.Url is not { } url) return null;
            var title = item.Title;
            var e = engine;
            if (assignment is { } d)
            {
                var handedIn = Text(d.Submitted) != null || d.Grade != null;
                if (Text(d.QuizUrl) is { } q && !handedIn) return ("Take quiz", SymbolRegular.TaskListLtr24, () => e.ActOn(q, title, false));
                if (handedIn) return ("See feedback", SymbolRegular.Comment24, () => e.ActOn(url, title, true));
                if (Text(d.DiscussionUrl) is { } disc) return ("Reply", SymbolRegular.Chat24, () => e.OpenWeb(disc, title));
                if (d.CanSubmit) return (d.Resubmit == true ? "Resubmit" : "Hand in", SymbolRegular.ArrowUpload24, () => e.ActOn(url, title, false));
                if (d.ToolUrl != null) return d.LtiQuiz == true ? ("Take quiz", SymbolRegular.TaskListLtr24, () => e.ActOn(url, title, false)) : ("Open tool", SymbolRegular.PuzzlePiece24, () => e.ActOn(url, title, false));
                return null;
            }
            if (item.Work is not { } wk) return null;
            if (wk.HandedIn) return ("See feedback", SymbolRegular.Comment24, () => e.ActOn(url, title, true));
            if (!wk.Open) return null;
            return (wk.Kind ?? "").ToLowerInvariant() switch
            {
                "quiz" => ("Take quiz", SymbolRegular.TaskListLtr24, () => e.ActOn(url, title, false)),
                "discussion" or "discussion_topic" => ("Reply", SymbolRegular.Chat24, () => e.ActOn(url, title, false)),
                "tool" => ("Open", SymbolRegular.PuzzlePiece24, () => e.ActOn(url, title, false)),
                _ => ("Hand in", SymbolRegular.ArrowUpload24, () => e.ActOn(url, title, false)),
            };
        }

        /// <summary>The assignment (or the page) behind the row, as its own screen reads it: what was kept from last
        /// time at once, then the school's answer. Anything else shows what its row knows.</summary>
        private async Task Load()
        {
            if (item.Url is not { } url) return;
            try
            {
                if (QuizIds(url) is { } ids)
                {
                    var args = new { course = ids.Course, quiz = ids.Quiz };
                    if (engine.Kept<AssignmentData>("quizAssignment", args) is { } kept) Show(kept, null);
                    loading = true;
                    Redraw();
                    Show(await engine.Call<AssignmentData>("quizAssignment", args), null);
                }
                else if (engine.NativeRoute(url, item.Title) is { } route)
                {
                    if (route.Kind == RouteKind.Assignment)
                    {
                        var args = new { course = route.A, id = route.B };
                        if (engine.Kept<AssignmentData>("assignment", args) is { } kept) Show(kept, null);
                        loading = true;
                        Redraw();
                        Show(await engine.Call<AssignmentData>("assignment", args), null);
                    }
                    else if (route.Kind == RouteKind.Page)
                    {
                        object args = route.B.Length == 0 ? new { ctx = route.A } : new { ctx = route.A, slug = route.B };
                        if (engine.Kept<PageData>("page", args) is { } kept) Show(null, kept);
                        loading = true;
                        Redraw();
                        Show(null, await engine.Call<PageData>("page", args));
                    }
                }
            }
            catch { /* the row's own words stay */ }
            loading = false;
            Redraw();
        }

        private void Show(AssignmentData? a, PageData? p)
        {
            if (a != null) { assignment = a; excerpt = Plain(a.Html); excerptHtml = Lean(a.Html); }
            if (p != null) { page = p; excerpt = Plain(p.Html); excerptHtml = Lean(p.Html); }
            Redraw();
        }

        /// <summary>About five lines of what the work says, as the school wrote it — its bullets, numbers, bold and links
        /// (a link opened through the app, the card folding away) — in the card's quieter colour; what runs on fades out
        /// at the fifth line rather than stopping mid-word. Null when there are no words in it.</summary>
        private FrameworkElement? Formatted(string html)
        {
            if (excerpt == null) return null;
            var e = engine;
            var text = new RichText(html, url => { Dismiss(); e.GoTo(url); }, engine.BaseUrl, 14);
            text.Document.SetResourceReference(System.Windows.Documents.FlowDocument.ForegroundProperty, B.Text2);
            // (five of its lines: 14 at 1.45 each)
            var most = Math.Ceiling(14 * 1.45 * 5) + 2;
            text.Measure(new Size(Math.Max(120, width - 2 - 34), double.PositiveInfinity));
            var holder = new Border { Child = text, MaxHeight = most, ClipToBounds = true };
            if (text.DesiredSize.Height > most + 1)
            {
                var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                fade.GradientStops.Add(new GradientStop(Colors.Black, 0));
                fade.GradientStops.Add(new GradientStop(Colors.Black, 0.72));
                fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
                fade.Freeze();
                holder.OpacityMask = fade;
            }
            return holder;
        }

        /// <summary>The work's HTML for the card: its words and their formatting only — pictures, frames, media and
        /// tables left out (the whole page has them) — and no more than its first few thousand characters, cut at a tag's
        /// end.</summary>
        private static string? Lean(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;
            var s = Regex.Replace(html, @"<(script|style|table|iframe|video|audio|object|figure)\b[^>]*>.*?</\1\s*>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<(img|embed|source|iframe|input)\b[^>]*>", " ", RegexOptions.IgnoreCase);
            if (s.Length > 4000)
            {
                var cut = s.LastIndexOf('>', 4000);
                s = cut > 0 ? s[..(cut + 1)] : s[..4000];
            }
            return s;
        }

        private (string Course, string Quiz)? QuizIds(string raw)
        {
            if (engine.Absolute(raw) is not { } u || !engine.IsSchool(u)) return null;
            var p = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 4 || p[0] != "courses" || p[2] != "quizzes" || !Engine.Engine.Numeric(p[1]) || !Engine.Engine.Numeric(p[3])) return null;
            return (p[1], p[3]);
        }

        /// <summary>The first words of what it says: its HTML as plain text.</summary>
        private static string? Plain(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;
            var s = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<br\s*/?>|</p>|</div>|</li>|</h\d>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<[^>]+>", " ");
            s = System.Net.WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, @"[ \t ]+", " ");
            s = Regex.Replace(s, @"\s*\n\s*", "\n").Trim();
            return s.Length == 0 ? null : s.Length > 600 ? s[..600] + "…" : s;
        }
    }
}
