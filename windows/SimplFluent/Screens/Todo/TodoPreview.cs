using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;

namespace Simpl.Screens;

/// <summary>
/// A row's quick look (the Mac's work preview, 1.2.3, in its list as 1.2.15 has it): a click on a row opens a card under
/// it with what the work is — its kind and course, where it stands, when it is due, its points, the first words of what
/// it says (an assignment's or a page's, read as it opens) — with Mark done, its own action (Hand in, Take quiz, Reply,
/// See feedback) and Open. Its Close, Escape, a click anywhere round it, or the row pressed again fold it back; a
/// double-click (or a Ctrl-click) on a row opens the work whole.
/// </summary>
public sealed partial class TodoScreen
{
    /// <summary>The row whose quick look is open (one at a time), and the card it is on for (a new one is a new card).</summary>
    private string? previewId;
    private int previewToken;
    /// <summary>What the card read beyond the row: the assignment behind it, or the page; still reading.</summary>
    private AssignmentData? previewAssignment;
    private PageData? previewPage;
    private bool previewLoading;
    /// <summary>The last row pressed, and when (a second press within a double-click's time opens the work).</summary>
    private (string Id, long At)? lastPress;
    private readonly Dictionary<string, Button> rowButtons = new();
    private readonly Dictionary<string, Border> slots = new();

    /// <summary>A row pressed: its quick look — or, a double-click or a Ctrl-click, the work itself. The row whose quick look
    /// is open, pressed again, folds it back.</summary>
    private void Press(WorkRow row)
    {
        var now = Environment.TickCount64;
        var again = lastPress is { } last && last.Id == row.Id && now - last.At <= TodoWords.DoubleClickMs;
        lastPress = (row.Id, now);
        var url = row.Custom == true ? null : TodoWords.Text(row.Url);
        if (url != null && (again || (Keyboard.Modifiers & ModifierKeys.Control) != 0))
        {
            lastPress = null;
            ClosePreview(animated: false);
            engine.OpenWeb(url, row.Title);
            return;
        }
        if (again) return; // (nothing to open whole, a task of your own: its quick look stays)
        if (previewId == row.Id)
        {
            ClosePreview();
            return;
        }
        OpenPreview(row);
    }

    private void OpenPreview(WorkRow row)
    {
        ClosePreview(animated: false, keepPress: true);
        previewId = row.Id;
        previewAssignment = null;
        previewPage = null;
        previewLoading = false;
        var token = ++previewToken;
        if (slots.TryGetValue(row.Id, out var slot))
        {
            slot.Child = PreviewCard(row);
            slot.Visibility = Visibility.Visible;
            Motion.Enter(slot, 0, -10, Motion.Slow); // (out of its row, down into place)
            Shot.Moved();
        }
        _ = LoadPreview(row, token);
    }

    /// <summary>Folded back into its row (its Close, Escape, a click round it), or gone at once (the work opened whole).</summary>
    private void ClosePreview(bool animated = true, bool keepPress = false)
    {
        if (previewId == null) return;
        var id = previewId;
        previewId = null;
        previewToken++;
        if (!keepPress) lastPress = null; // (a row pressed after this is a press of its own, never a double-click's second)
        if (!slots.TryGetValue(id, out var slot)) return;
        void Gone()
        {
            if (previewId == id) return; // (opened again meanwhile)
            slot.Visibility = Visibility.Collapsed;
            slot.Child = null;
        }
        if (animated && Motion.On) Motion.FadeOut(slot, Gone, Motion.Fast); else Gone();
    }

    /// <summary>A click anywhere round the open quick look folds it back (the click still does what it does); its own row's
    /// click is the row's (it folds it, or opens the work on a double-click).</summary>
    private void OutsideClick(object sender, MouseButtonEventArgs e)
    {
        if (previewId == null || e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject o) return;
        if (slots.TryGetValue(previewId, out var slot) && IsInside(o, slot)) return;
        if (rowButtons.TryGetValue(previewId, out var row) && IsInside(o, row)) return;
        ClosePreview();
    }

    private static bool IsInside(DependencyObject o, DependencyObject container)
    {
        for (var p = o; p != null; p = p is Visual or Visual3D ? VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p == container) return true;
        return false;
    }

    /// <summary>The card again, as what it shows has changed (what it read arrived), when it is still the one open.</summary>
    private void RedrawPreview(int token)
    {
        if (token != previewToken || previewId is not { } id || !slots.TryGetValue(id, out var slot) || FindRow(id) is not { } row) return;
        slot.Child = PreviewCard(row);
        Shot.Moved();
    }

    // ---- reading ----

    /// <summary>The assignment (or the page) behind the row, as its own screen reads it: what was kept from last time at
    /// once, then the school's answer. A quiz reads its own assignment, so a graded one shows its score. Anything else (a
    /// discussion, an event, a task of your own) shows what its row knows.</summary>
    private async Task LoadPreview(WorkRow row, int token)
    {
        if (row.Custom == true || TodoWords.Text(row.Url) is not { } url) return;
        if (QuizIds(url) is { } q)
        {
            await Detail<AssignmentData>(token, "quizAssignment", new { course = q.Course, quiz = q.Quiz }, a => previewAssignment = a);
            return;
        }
        switch (engine.NativeRoute(url, row.Title))
        {
            case { Kind: RouteKind.Assignment } r:
                await Detail<AssignmentData>(token, "assignment", new { course = r.A, id = r.B }, a => previewAssignment = a);
                break;
            case { Kind: RouteKind.Page } r:
                object args = string.IsNullOrEmpty(r.B) ? new { ctx = r.A } : new { ctx = r.A, slug = r.B };
                await Detail<PageData>(token, "page", args, p => previewPage = p);
                break;
        }
    }

    private async Task Detail<T>(int token, string name, object args, Action<T> keep) where T : class
    {
        if (engine.Kept<T>(name, args) is { } kept) keep(kept);
        previewLoading = true;
        RedrawPreview(token);
        T? fresh = null;
        try { fresh = await engine.Call<T>(name, args); }
        catch (Exception e) { Log.Write($"todo preview {name}: {e.Message}"); }
        if (token != previewToken) return;
        if (fresh != null) keep(fresh);
        previewLoading = false;
        RedrawPreview(token);
    }

    /// <summary>A Canvas quiz's course and quiz, from its address (…/courses/1/quizzes/2).</summary>
    private (string Course, string Quiz)? QuizIds(string raw)
    {
        if (engine.Absolute(raw) is not { } u || !engine.IsSchool(u)) return null;
        var p = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 4 || p[0] != "courses" || p[2] != "quizzes" || !Engine.Engine.Numeric(p[1]) || !Engine.Engine.Numeric(p[3])) return null;
        return (p[1], p[3]);
    }

    // ---- the card ----

    /// <summary>The card's face (the Mac's PreviewCard, inline): what the work is and where, where it stands, its facts, the
    /// first words of what it says, and what can be done with it.</summary>
    private FrameworkElement PreviewCard(WorkRow row)
    {
        var a = previewAssignment;
        var page = previewPage;
        var type = TodoWords.Text(row.Type) ?? row.Id.Split(':')[0];
        var custom = row.Custom == true;
        var kind = custom ? "My task" : TodoWords.PlannerKind(type);
        var course = new[] { row.CourseName, row.Course }.Select(TodoWords.Text).FirstOrDefault(c => c != null && c != kind);
        var symbol = custom ? SymbolRegular.CheckmarkCircle24 : Glyph.Work(type);
        var url = custom ? null : TodoWords.Text(row.Url);
        var title = row.Title;
        var tint = K.Hex(a?.Color ?? page?.Color ?? row.Color, Appearance.Accent);

        // "Assignment · BIO 101", a spinner while the school is asked again, Close
        var kicker = string.Join(" · ", new[] { TodoWords.Text(a?.Kind) ?? kind, TodoWords.Text(a?.Context) ?? TodoWords.Text(page?.Context) ?? course }.Where(s => s != null));
        var kick = K.Text(kicker, Ty.BodyStrong);
        kick.Foreground = new SolidColorBrush(K.Darkened(tint));
        var close = K.Button("Close", () => ClosePreview(), icon: SymbolRegular.DismissCircle24);
        close.MinWidth = 0;
        close.ToolTip = "Close (Esc, or click anywhere else)";
        var head = K.Columns("auto, *, auto, auto", K.IconTile(symbol, tint, 26).Col(0), kick.Margin(9, 0, 8, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1), close.Col(3));
        if (previewLoading)
        {
            var spin = K.Spinner(14);
            spin.ToolTip = $"Updating from {engine.LmsName}…";
            head.Children.Add(spin.Margin(0, 0, 10, 0).Col(2));
        }

        // where it stands: the assignment's own word and grade once read (with the row's Feedback or New), else the row's
        var chips = new List<WorkFlag>();
        if (a == null)
        {
            if (row.Flag is { } rf && rf.Word.Length > 0) chips.Add(rf);
        }
        else
        {
            if (a.Status is { } st && st.Word.Length > 0) chips.Add(new WorkFlag { Word = st.Word, Kind = st.Kind });
            if (a.Grade is { Score: { } score } g)
            {
                var text = $"{TodoWords.Number(score)} / {TodoWords.Number(g.Possible ?? 0)}";
                if (TodoWords.Text(g.Letter) is { } letter) text += $" · {letter}";
                chips.Add(new WorkFlag { Word = text, Kind = "info" });
            }
            if (TodoWords.Text(a.Grade?.Late) is { } late) chips.Add(new WorkFlag { Word = late, Kind = "warn" });
            var words = chips.Select(c => c.Word).ToHashSet();
            if (row.Flag is { } f && f.Word.ToLowerInvariant() is "feedback" or "new" && !words.Contains(f.Word)) chips.Add(f);
        }

        // its facts: when, points, handed in, open from, edited, what else its row said
        var facts = new List<(SymbolRegular Icon, string Text, bool Strong)>();
        var day = TodoWords.Date(row.Date) is { } date ? TodoWords.DayWord(date) : null;
        if (TodoWords.When(day, row.Time, !custom && TodoWords.IsWork(type)) is { } when) facts.Add((SymbolRegular.Clock24, when, true));
        else if (a != null) facts.Add((SymbolRegular.Clock24, TodoWords.Text(a.Due) is { } due ? $"Due {due}" : "No due date", true));
        if ((TodoWords.Points(row.Meta ?? row.Sub) ?? TodoWords.Text(a?.Points)) is { } pts) facts.Add((SymbolRegular.NumberSymbolSquare24, pts, false));
        if (TodoWords.Text(a?.Submitted) is { } submitted) facts.Add((SymbolRegular.CheckmarkStarburst24, submitted, false));
        if (TodoWords.Text(a?.Available) is { } available) facts.Add((SymbolRegular.LockOpen24, available, false));
        if (TodoWords.Text(page?.Edited) is { } edited) facts.Add((SymbolRegular.Edit24, edited, false));
        if (custom && TodoWords.Text(row.Meta) is { } detail) facts.Add((SymbolRegular.Info24, detail, false));

        var info = new Stack { Orientation = Orientation.Vertical, Spacing = 12 };
        if (chips.Count > 0)
        {
            var flow = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var c in chips) flow.Children.Add(K.Chip(c.Word, TodoWords.Tone(c.Kind), true).Margin(0, 0, 6, 6));
            flow.Margin = new Thickness(0, 0, 0, -6);
            info.Children.Add(flow);
        }
        if (facts.Count > 0)
        {
            var list = new Stack { Orientation = Orientation.Vertical, Spacing = 6 };
            foreach (var (icon, text, strong) in facts)
                list.Children.Add(K.Columns("18, *",
                    K.Icon(icon, 14, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Margin(0, 3, 0, 0).Col(0),
                    K.Wrapped(text, strong ? Ty.BodyStrong : Ty.Body, strong ? B.Text : B.Text2).Margin(9, 0, 0, 0).Col(1)));
            info.Children.Add(list);
        }
        if (TodoWords.Plain(a?.Html ?? page?.Html) is { } excerpt) info.Children.Add(K.Wrapped(excerpt, Ty.Body, B.Text2, 5));

        // what can be done with it: Mark done, its own action, Open
        var actions = K.Columns("auto, *, auto, auto");
        var mark = K.Button(row.Done ? "Mark not done" : "Mark done", () => Toggle(row, !row.Done), icon: row.Done ? SymbolRegular.ArrowUndo24 : SymbolRegular.Checkmark24);
        mark.ToolTip = row.Done ? "Mark it not done" : "Mark it done";
        actions.Children.Add(mark.Col(0));
        if (OwnAction(row, a, url, type, title) is { } act)
        {
            var b = K.Button(act.Label, () => { ClosePreview(animated: false); act.Run(); }, icon: act.Icon);
            actions.Children.Add(b.Margin(8, 0, 0, 0).Col(2));
        }
        if (url != null)
        {
            var open = K.Button("Open", () => { ClosePreview(animated: false); engine.OpenWeb(url, title); }, accent: true);
            actions.Children.Add(open.Margin(8, 0, 0, 0).Col(3));
        }

        var body = K.V(0, head.Margin(14, 12, 12, 0));
        if (info.Children.Count > 0) body.Children.Add(info.Margin(16, 12, 16, 0));
        body.Children.Add(actions.Margin(16, 16, 16, 16));
        var card = new Border { Child = body, CornerRadius = new CornerRadius(8), Margin = new Thickness(4, 2, 4, 8), SnapsToDevicePixels = true }.Bg(B.Subtle).Stroke(B.CardStroke);
        AutomationProperties.SetName(card, $"Preview of {title}");
        return card;
    }

    /// <summary>Its own action beside Open, as its screen would put first: Take quiz, See feedback, Reply, Hand in, Open tool
    /// — from the assignment once read, else from the row.</summary>
    private (string Label, SymbolRegular Icon, Action Run)? OwnAction(WorkRow row, AssignmentData? a, string? url, string type, string title)
    {
        if (url == null) return null;
        if (a != null)
        {
            var handedIn = TodoWords.Text(a.Submitted) != null || a.Grade != null;
            if (TodoWords.Text(a.QuizUrl) is { } quiz && !handedIn) return ("Take quiz", SymbolRegular.TaskListLtr24, () => engine.ActOn(quiz, title, false));
            if (handedIn) return ("See feedback", SymbolRegular.Comment24, () => engine.ActOn(url, title, true));
            if (TodoWords.Text(a.DiscussionUrl) is { } disc) return ("Reply", SymbolRegular.Chat24, () => engine.OpenWeb(disc, title));
            if (a.CanSubmit) return (a.Resubmit == true ? "Resubmit" : "Hand in", SymbolRegular.ArrowUpload24, () => engine.ActOn(url, title, false));
            if (a.ToolUrl != null)
            {
                var lti = a.LtiQuiz == true;
                return (lti ? "Take quiz" : "Open tool", lti ? SymbolRegular.TaskListLtr24 : SymbolRegular.PuzzlePiece24, () => engine.ActOn(url, title, false));
            }
            return null;
        }
        if (!TodoWords.IsWork(type)) return null;
        if (row.Done) return ("See feedback", SymbolRegular.Comment24, () => engine.ActOn(url, title, true));
        var (label, icon) = TodoWords.ActionOf(row.Type);
        return (label, icon, () => engine.ActOn(url, title, false));
    }
}
