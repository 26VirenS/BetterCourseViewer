using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

// The ends of the quiz popup (the Mac's QuizResults.swift): the review before handing in, the receipt, and the feedback
// on a finished attempt — lists in Windows 11's cards with hairlines between their rows, no card per question.

/// <summary>
/// The attempt before it is handed in (the Mac's QuizReviewPane): every question as a row — whether it is answered, its
/// words, its answer at the row's end (or that it is blank, or flagged) — a click on one to change it; on a wide popup a
/// column beside it of how much is answered. Back to questions and Submit (Ctrl+Enter) at the foot.
/// </summary>
internal sealed class QuizReviewPane : IQuizPane
{
    private readonly QuizSheet s;
    private readonly Wpf.Ui.Controls.Button backButton, submitButton;
    private readonly FrameworkElement submitting = QuizUi.Working("Submitting…", Ty.Caption);

    public FrameworkElement View { get; }

    public QuizReviewPane(QuizSheet s, bool wide)
    {
        this.s = s;
        var left = K.V(20, Heading(wide), List(), K.Wrapped(Note, Ty.Caption, B.Text2));
        FrameworkElement page;
        if (wide)
        {
            var g = K.Columns("*, 52, 230", left.Col(0), Summary().Margin(0, 10, 0, 0).Col(2));
            g.MaxWidth = QuizUi.Column + 52 + 230;
            page = g;
        }
        else
        {
            left.MaxWidth = QuizUi.Column;
            page = left;
        }
        var pad = new Border { Child = page, Padding = new Thickness(40, 22, 40, 32) };

        backButton = QuizUi.Button("Back to questions", () => _ = Run.KeepWorking(), icon: SymbolRegular.ChevronLeft24);
        backButton.ToolTip = "Back to the question you were on";
        submitButton = QuizUi.Button(Run.Attempt?.Survey == true ? "Submit survey…" : "Submit quiz…", s.AskSubmit, accent: true, icon: SymbolRegular.Send24);
        submitButton.ToolTip = "Submit this attempt (Ctrl+Enter)";
        submitting.VerticalAlignment = VerticalAlignment.Center;
        var foot = K.Columns("auto, *, auto, auto", backButton.Col(0), submitting.Margin(0, 0, 14, 0).Col(2), submitButton.Col(3));
        View = K.Rows("*, auto", QuizUi.Scroll(pad).Row(0), QuizUi.Foot(foot).Row(1));
        Update(QuizChange.Stage);
    }

    private QuizRun Run => s.Run;
    private bool NoBack => Run.Attempt?.NoBack == true;
    private int Blank => Run.Questions.Count - Run.AnsweredCount;
    private int Flagged => Run.Questions.Count(q => q.Flagged);

    public Action? Default => null;

    public void Keys(KeyEventArgs e) { }

    /// <summary>Handing it in: Submitting… at the foot, its buttons shut meanwhile.</summary>
    public void Update(QuizChange c)
    {
        var busy = Run.Now == QuizRun.Stage.Submitting;
        submitting.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        backButton.IsEnabled = !busy;
        submitButton.IsEnabled = !busy;
    }

    private string CountLine => $"{Run.AnsweredCount} of {Run.Questions.Count} answered · {(Blank == 0 ? "nothing left blank" : $"{Blank} left blank")}";

    private string Note => NoBack ? "This quiz seals each question once you leave it." : "Click a question to change it. Submitting ends the attempt; blank questions are graded as incorrect.";

    private FrameworkElement Heading(bool wide)
    {
        var v = K.V(6, K.Text("Review your answers", Ty.Title));
        if (!wide) v.Children.Add(K.Text(CountLine, Ty.Body, B.Text2).Digits());
        return v;
    }

    /// <summary>The questions, a row each with a hairline between: on a quiz that lets the student go back, each row a button.</summary>
    private FrameworkElement List()
    {
        var rows = K.V(0);
        var qs = Run.Questions;
        for (var k = 0; k < qs.Count; k++)
        {
            if (k > 0) rows.Children.Add(K.Divider(46));
            var q = qs[k];
            var at = k;
            if (NoBack) rows.Children.Add(Row(q).Margin(10, 12, 10, 12));
            else
            {
                var b = K.Subtle(Row(q), () => _ = Run.KeepWorking(at), new Thickness(10, 12, 10, 12));
                b.ToolTip = $"Change your answer to question {q.N}";
                System.Windows.Automation.AutomationProperties.SetName(b, $"Question {q.N}, {(q.Kind == "info" ? "information only" : q.Summary ?? "not answered")}{(q.Flagged ? ", flagged" : "")}");
                rows.Children.Add(b);
            }
        }
        return K.Card(rows, new Thickness(4));
    }

    private FrameworkElement Row(QuizQuestion q)
    {
        var line = K.Wrapped(q.Plain.Length == 0 ? q.Name : q.Plain, Ty.Body, B.Text, 2);
        var meta = K.H(6, K.Text($"Question {q.N}", Ty.Caption, B.Text2));
        if (q.Flagged)
        {
            meta.Children.Add(K.Icon(SymbolRegular.Flag24, 12, QuizUi.Orange, filled: true));
            var word = K.Text("Flagged", Ty.Caption);
            word.Foreground = QuizUi.Ink(QuizUi.Orange);
            meta.Children.Add(word);
        }
        var words = K.V(4, line, meta);
        var answer = Answer(q);
        answer.MaxWidth = 260;
        answer.HorizontalAlignment = HorizontalAlignment.Right;
        answer.VerticalAlignment = VerticalAlignment.Top;
        return K.Columns("auto, *, auto", Glyph(q).Col(0), words.Col(1), answer.Margin(16, 0, 0, 0).Col(2));
    }

    /// <summary>Answered (a tick in the quiz's colour), blank (a mark in red), or only words to read.</summary>
    private FrameworkElement Glyph(QuizQuestion q)
    {
        SymbolIcon icon;
        if (q.Kind == "info") icon = K.Icon(SymbolRegular.Info24, 20, B.Text2);
        else if (q.IsAnswered) icon = K.Icon(SymbolRegular.CheckmarkCircle24, 20, s.Tint, filled: true);
        else icon = K.Icon(SymbolRegular.ErrorCircle24, 20, QuizUi.Red);
        icon.Width = 22;
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 0, 14, 0);
        return icon;
    }

    private FrameworkElement Answer(QuizQuestion q)
    {
        if (q.Kind == "info") return K.Text("Information only", Ty.Caption, B.Text2);
        TextBlock t;
        if (q.Summary is { } sum) { t = K.Wrapped(sum, Ty.BodyStrong, B.Text, 2); t.Foreground = QuizUi.Ink(s.Tint); }
        else { t = K.Text("Not answered", Ty.BodyStrong); t.Foreground = QuizUi.Ink(QuizUi.Red); }
        t.TextAlignment = TextAlignment.Right;
        return t;
    }

    /// <summary>Beside the list on a wide popup: a ring of how much is answered, and what is blank and flagged.</summary>
    private FrameworkElement Summary()
    {
        var total = Run.Questions.Count;
        var done = Run.AnsweredCount;
        var inside = K.V(0, K.Text($"{done}", Ty.Title).Digits().Center(), K.Text($"of {total}", Ty.Caption, B.Text2).Center());
        inside.VerticalAlignment = VerticalAlignment.Center;
        var ring = K.Ring(total > 0 ? (double)done / total : 0, s.Tint, 116, 9, inside);
        ring.HorizontalAlignment = HorizontalAlignment.Left;
        var facts = K.V(10,
            Fact(K.Icon(SymbolRegular.CheckmarkCircle24, 18, s.Tint, filled: true), $"{done} answered"),
            Fact(Blank == 0 ? K.Icon(SymbolRegular.ErrorCircle24, 18, B.Text2) : K.Icon(SymbolRegular.ErrorCircle24, 18, QuizUi.Red), Blank == 0 ? "Nothing left blank" : $"{Blank} left blank"),
            Flagged > 0 ? Fact(K.Icon(SymbolRegular.Flag24, 18, QuizUi.Orange, filled: true), $"{Flagged} flagged") : null);
        return K.V(18, ring, facts);
    }

    private static FrameworkElement Fact(SymbolIcon icon, string text) => K.H(10, icon, K.Text(text, Ty.Body));
}

/// <summary>
/// The attempt handed in (the Mac's QuizReceiptPane): the tick, what Canvas says of it (the questions answered, the
/// score, the points for taking part) as rows of a card, and Done or the feedback. Its pieces arrive one after another.
/// </summary>
internal sealed class QuizReceiptPane : IQuizPane
{
    private readonly QuizSheet s;

    public FrameworkElement View { get; }

    public QuizReceiptPane(QuizSheet s)
    {
        this.s = s;
        var r = s.Run.Receipt;
        var tick = K.Icon(SymbolRegular.CheckmarkCircle24, 72, QuizUi.Green, filled: true);
        var title = K.Wrapped(QuizUi.Sentence(r?.Title ?? "Attempt submitted"), Ty.Title).Center();
        title.TextTrimming = TextTrimming.None;
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 18, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 560 };
        v.Children.Add(tick);
        v.Children.Add(title);
        if (!string.IsNullOrEmpty(r?.Lead))
        {
            var lead = K.Wrapped(r!.Lead!, Ty.Body, B.Text2).Center();
            lead.MaxWidth = 520;
            v.Children.Add(lead);
        }
        var facts = Facts(r);
        facts.Margin = new Thickness(0, 10, 0, 0);
        v.Children.Add(facts);
        var buttons = Buttons(r);
        buttons.Margin = new Thickness(0, 16, 0, 0);
        v.Children.Add(buttons);
        var pad = new Border { Child = v, Padding = new Thickness(40, 44, 40, 40) };
        View = QuizUi.Scroll(pad);
        // (a rare moment's pieces, each rising a little and fading in a beat after the one before)
        Motion.Stagger(v.Children.OfType<UIElement>().ToList(), 60, 8);
    }

    public Action? Default => s.Run.Receipt?.Feedback == true ? SeeFeedback : s.Dismiss;

    public void Update(QuizChange c) { }

    public void Keys(KeyEventArgs e) { }

    private void SeeFeedback() => _ = s.Run.OpenFeedback(s.Run.Receipt?.Attempt);

    private static FrameworkElement Facts(QuizReceipt? r)
    {
        var rows = K.V(0, Fact("Questions answered", r?.Answered ?? ""));
        if (r?.Score is { } sc) { rows.Children.Add(K.Divider()); rows.Children.Add(Fact("Score", sc)); }
        if (r?.TakingPart is { } tp) { rows.Children.Add(K.Divider()); rows.Children.Add(Fact("For taking part", tp)); }
        var card = K.Card(rows, new Thickness(16, 0, 16, 0));
        card.Width = 460;
        card.MaxWidth = 460;
        return card;
    }

    private static FrameworkElement Fact(string k, string v)
    {
        var g = K.Columns("*, auto", K.Text(k, Ty.Body, B.Text2).Col(0), K.Text(v, Ty.BodyStrong).Digits().Col(1));
        g.Margin = new Thickness(0, 13, 0, 13);
        return g;
    }

    private FrameworkElement Buttons(QuizReceipt? r)
    {
        var row = K.H(10);
        row.HorizontalAlignment = HorizontalAlignment.Center;
        if (r?.Feedback == true)
        {
            row.Children.Add(K.Button("Done", s.Dismiss));
            row.Children.Add(K.Button("See feedback", SeeFeedback, accent: true));
        }
        else row.Children.Add(K.Button("Done", s.Dismiss, accent: true));
        return row;
    }
}

/// <summary>
/// A finished attempt question by question (the Mac's QuizFeedbackPane): the score as a ring, the attempt to show, a
/// filter (all, to review, correct — each with its count), the instructor's comments, and each question with what was
/// answered, the correct answer where the quiz shows it, every option, and the worked solution. The questions are
/// sections of the page with a hairline between them; on a wide popup the score, the filter and a tile per question (a
/// click goes to it) stay in a column at the side.
/// </summary>
internal sealed class QuizFeedbackPane : IQuizPane
{
    private static readonly string[] FilterNames = { "All", "To review", "Correct" };
    private readonly QuizSheet s;
    private readonly bool wide;
    private readonly ContentControl host = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private int filter;
    private ScrollViewer? main, rail;
    private readonly Dictionary<int, FrameworkElement> anchors = new();

    public FrameworkElement View => host;

    public QuizFeedbackPane(QuizSheet s, bool wide)
    {
        this.s = s;
        this.wide = wide;
        Build();
    }

    private QuizRun Run => s.Run;

    public Action? Default => null;

    public void Update(QuizChange c) { }

    public void Keys(KeyEventArgs e) { }

    private void Build()
    {
        var railAt = rail?.VerticalOffset ?? 0;
        if (Run.Feedback is not { } fb)
        {
            host.Content = K.LoadState(Run.FeedbackError, () => _ = Run.OpenFeedback());
            return;
        }
        if (fb.Hidden is { } why)
        {
            var v = K.V(12, K.Icon(SymbolRegular.EyeOff24, 40, B.Text3), K.Text("Results not released", Ty.Subtitle).Center(), K.Wrapped(why, Ty.Body, B.Text2).Center());
            v.HorizontalAlignment = HorizontalAlignment.Center;
            v.VerticalAlignment = VerticalAlignment.Center;
            v.MaxWidth = 440;
            host.Content = new Grid { Children = { v } };
            return;
        }
        anchors.Clear();
        var content = new Border { Child = Main(fb), Padding = new Thickness(40, 22, 40, 48) };
        main = QuizUi.Scroll(content);
        if (wide)
        {
            var side = new Border { Child = Rail(fb), Padding = new Thickness(22, 14, 22, 28) };
            rail = QuizUi.Scroll(side);
            var line = new Border { Width = 1 }.Bg(B.Divider);
            host.Content = K.Columns("284, 1, *", rail.Col(0), line.Col(1), main.Col(2));
            if (railAt > 0) rail.ScrollToVerticalOffset(railAt);
        }
        else host.Content = main;
        Support.Shot.Moved();
    }

    private List<QuizFeedback.Row> Rows => Run.Feedback?.Rows ?? new List<QuizFeedback.Row>();

    private bool Passes(QuizFeedback.Row r) => filter switch
    {
        1 => r.Verdict is "wrong" or "partial",
        2 => r.Verdict == "right",
        _ => true,
    };

    private int[] Counts(List<QuizFeedback.Row> rows) => new[] { rows.Count, rows.Count(r => r.Verdict is "wrong" or "partial"), rows.Count(r => r.Verdict == "right") };

    private void SetFilter(int k)
    {
        if (k == filter) return;
        filter = k;
        Build();
        Motion.FadeIn(main!, Motion.Normal);
    }

    private FrameworkElement Main(QuizFeedback fb)
    {
        var rows = fb.Rows ?? new List<QuizFeedback.Row>();
        var shown = rows.Where(Passes).ToList();
        var v = new Stack { Orientation = Orientation.Vertical, Spacing = 28, MaxWidth = 800 };
        if (!wide)
        {
            var head = K.Columns("auto, auto, *, auto", ScoreRing(fb, 96).Col(0), ScoreWords(fb).Margin(22, 0, 0, 0).Col(1));
            if (AttemptPicker(fb) is { } picker) head.Children.Add(picker.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(3));
            v.Children.Add(head);
            v.Children.Add(FilterBar(fb, rows));
        }
        else if (fb.Released != true) v.Children.Add(NotReleased());
        if (fb.Comments is { Count: > 0 } cs) v.Children.Add(Comments(cs));
        v.Children.Add(Questions(shown));
        return v;
    }

    // ---- the column at the side ----

    private FrameworkElement Rail(QuizFeedback fb)
    {
        var rows = fb.Rows ?? new List<QuizFeedback.Row>();
        var v = K.V(22, K.V(14, ScoreRing(fb, 108), ScoreWords(fb), AttemptPicker(fb)));
        if (fb.Released == true) v.Children.Add(FilterRows(rows));
        if (rows.Count > 0)
        {
            // the questions as a small card of tiles, as the attempt's are (how many were right is the filter's count)
            var grid = new UniformGrid { Columns = 5, Margin = new Thickness(-3) };
            foreach (var r in rows) grid.Children.Add(VerdictTile(r));
            v.Children.Add(K.Card(K.V(12, K.Text("Questions", Ty.BodyStrong), grid), new Thickness(14)));
        }
        return v;
    }

    /// <summary>A question's tile in the column: its number washed in the colour of how it went (green, red, orange; grey
    /// where it was not marked). A click goes to it.</summary>
    private FrameworkElement VerdictTile(QuizFeedback.Row r)
    {
        var face = new Border { Height = 32, CornerRadius = new CornerRadius(6) };
        var n = K.Text($"{r.N}", Ty.Body).Digits().Center();
        n.FontWeight = FontWeights.SemiBold;
        n.VerticalAlignment = VerticalAlignment.Center;
        if (Ink(r.Verdict) is { } c)
        {
            face.Background = K.Brush(K.WithAlpha(c, Support.Appearance.Dark ? 0.3 : 0.18));
            n.Foreground = QuizUi.Ink(c);
        }
        else
        {
            face.Bg(B.Subtle).Stroke(B.CardStroke);
            n.Fg(B.Text2);
        }
        face.Child = n;
        face.Opacity = Passes(r) ? 1 : 0.4;
        var b = K.Subtle(face, () => GoTo(r), new Thickness(0), 6);
        b.Margin = new Thickness(3);
        b.ToolTip = $"Question {r.N} · {QuizUi.Sentence(r.VerdictText)}";
        System.Windows.Automation.AutomationProperties.SetName(b, $"Question {r.N}, {QuizUi.Sentence(r.VerdictText)}");
        return b;
    }

    /// <summary>A tile clicked: the page goes to its question (shown again if the filter had hidden it).</summary>
    private void GoTo(QuizFeedback.Row r)
    {
        if (!Passes(r)) { filter = 0; Build(); }
        host.Dispatcher.BeginInvoke(() =>
        {
            if (main?.Content is not UIElement content || !anchors.TryGetValue(r.N, out var a) || !a.IsVisible) return;
            var y = a.TranslatePoint(new Point(0, 0), content).Y;
            main.ScrollToVerticalOffset(Math.Max(0, y - 8));
        }, DispatcherPriority.Loaded);
    }

    // ---- the score ----

    private Color ScoreColor(QuizFeedback fb) => fb.Color is { Length: > 0 } hex ? K.Hex(hex, s.Tint) : s.Tint;

    /// <summary>The ring's whole percent: the engine's, from the one-decimal figure shown elsewhere (81.25 → 81.3% → 81),
    /// else half up on the decimal value — never banker's rounding.</summary>
    private static int? Whole(QuizFeedback fb) =>
        fb.PctWhole ?? (fb.Pct is { } p ? (int)Math.Round(Math.Round((decimal)p, 1, MidpointRounding.AwayFromZero), MidpointRounding.AwayFromZero) : null);

    private FrameworkElement ScoreRing(QuizFeedback fb, double size)
    {
        var c = ScoreColor(fb);
        var pct = K.Text(Whole(fb) is { } w ? $"{w}%" : "–", Ty.Subtitle).Digits().Center();
        pct.FontWeight = FontWeights.Bold;
        pct.Foreground = QuizUi.Ink(c);
        pct.VerticalAlignment = VerticalAlignment.Center;
        var ring = K.Ring(fb.Pct is { } f ? f / 100 : null, c, size, 9, pct);
        ring.HorizontalAlignment = HorizontalAlignment.Left;
        if (fb.PctText is { Length: > 0 } text) ring.ToolTip = text;
        return ring;
    }

    private static FrameworkElement ScoreWords(QuizFeedback fb)
    {
        var v = K.V(4, K.Text($"{fb.Score ?? "–"} / {fb.Possible ?? "–"}", Ty.Title).Digits(), K.Text($"Attempt {fb.Attempt ?? 1}", Ty.BodyStrong));
        if (fb.Summary is { Length: > 0 } sum) v.Children.Add(K.Wrapped(sum, Ty.Caption, B.Text2));
        v.VerticalAlignment = VerticalAlignment.Center;
        return v;
    }

    /// <summary>Another attempt to show, when there is more than one.</summary>
    private FrameworkElement? AttemptPicker(QuizFeedback fb)
    {
        if (fb.Attempts is not { Count: > 1 } atts) return null;
        var box = new ComboBox { MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Show another attempt" };
        var current = fb.Attempt ?? 1;
        foreach (var a in atts)
        {
            box.Items.Add(new ComboBoxItem { Content = $"Attempt {a}", Tag = a });
            if (a == current) box.SelectedIndex = box.Items.Count - 1;
        }
        System.Windows.Automation.AutomationProperties.SetName(box, "Attempt");
        // (once the list has closed: the pane is drawn again for that attempt)
        box.SelectionChanged += (_, _) => { if ((box.SelectedItem as ComboBoxItem)?.Tag is int a && a != current) box.Dispatcher.BeginInvoke(() => _ = Run.OpenFeedback(a), DispatcherPriority.Background); };
        box.DropDownOpened += (_, _) => s.Hold(true);
        box.DropDownClosed += (_, _) => s.Hold(false);
        return box;
    }

    // ---- the filter ----

    /// <summary>A narrow popup's filter: Windows 11's SelectorBar, or why there is none yet.</summary>
    private FrameworkElement FilterBar(QuizFeedback fb, List<QuizFeedback.Row> rows)
    {
        if (fb.Released != true) return NotReleased();
        var n = Counts(rows);
        var bar = new SelectorBar(new[] { $"All {n[0]}", $"To review {n[1]}", $"Correct {n[2]}" }, filter) { HorizontalAlignment = HorizontalAlignment.Left };
        bar.Changed += SetFilter;
        return bar;
    }

    /// <summary>The column's filter: a row each, the one showing washed in the quiz's colour, as a navigation pane's are.</summary>
    private FrameworkElement FilterRows(List<QuizFeedback.Row> rows)
    {
        var n = Counts(rows);
        var v = K.V(2);
        v.Margin = new Thickness(-12, 0, -12, 0);
        for (var k = 0; k < 3; k++)
        {
            var on = filter == k;
            var name = K.Text(FilterNames[k], on ? Ty.BodyStrong : Ty.Body);
            var g = K.Columns("*, auto", name.Col(0), K.Text($"{n[k]}", Ty.Body, B.Text2).Digits().Col(1));
            var wash = new Border { Child = g, Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(6) };
            wash.Background = on ? K.Brush(K.WithAlpha(s.Tint, Support.Appearance.Dark ? 0.24 : 0.15)) : Brushes.Transparent;
            var at = k;
            var b = K.Subtle(wash, () => SetFilter(at), new Thickness(0), 6);
            System.Windows.Automation.AutomationProperties.SetName(b, $"{FilterNames[k]}, {n[k]}{(on ? ", showing" : "")}");
            v.Children.Add(b);
        }
        return v;
    }

    private static FrameworkElement NotReleased() => K.Columns("auto, *",
        K.Icon(SymbolRegular.Info24, 16, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Margin(0, 0, 8, 0).Col(0),
        K.Wrapped("Canvas has not released the question results for this attempt yet — your score and the questions are shown as they stand.", Ty.Caption, B.Text2).Col(1));

    // ---- the page ----

    private static FrameworkElement Comments(List<QuizFeedback.Comment> cs)
    {
        var rows = K.V(0);
        for (var k = 0; k < cs.Count; k++)
        {
            if (k > 0) rows.Children.Add(K.Divider(46));
            var c = cs[k];
            var words = K.V(3, K.Text(c.Author, Ty.BodyStrong), K.Wrapped(c.Text, Ty.Body));
            var avatar = K.Avatar(c.Author, null, null, 34);
            avatar.VerticalAlignment = VerticalAlignment.Top;
            var g = K.Columns("auto, *", avatar.Margin(0, 0, 12, 0).Col(0), words.Col(1));
            g.Margin = new Thickness(0, 10, 0, 10);
            rows.Children.Add(g);
        }
        return QuizUi.Section("Instructor comments", K.Card(rows, new Thickness(16, 2, 16, 2)));
    }

    private FrameworkElement Questions(List<QuizFeedback.Row> shown)
    {
        var v = K.V(0);
        if (shown.Count == 0) v.Children.Add(K.Empty(SymbolRegular.CheckmarkCircle24, filter switch
        {
            1 => "Nothing to review — every question was answered correctly.",
            2 => "No question in this attempt was answered correctly.",
            _ => "This attempt has no questions to show.",
        }));
        for (var k = 0; k < shown.Count; k++)
        {
            if (k > 0) v.Children.Add(K.Divider().Margin(0, 28, 0, 28));
            var q = new QuizFeedbackQuestion(s, shown[k]).View;
            anchors[shown[k].N] = q;
            v.Children.Add(q);
        }
        return v;
    }

    /// <summary>How a question went, as a colour (null where it was not marked).</summary>
    public static Color? Ink(string verdict) => verdict switch
    {
        "right" => QuizUi.Green,
        "wrong" => QuizUi.Red,
        "partial" => QuizUi.Orange,
        _ => null,
    };
}

/// <summary>
/// One question of a finished attempt (the Mac's QuizFeedbackQuestion), a section of the page: its verdict and points, its
/// words, what was answered (and the correct answer beside it where the quiz shows it, a matching question as its
/// pairs), every option on request, and the worked solution. A coloured line at their edge marks the answers and the
/// solution — no wash, no box.
/// </summary>
internal sealed class QuizFeedbackQuestion
{
    private readonly QuizSheet s;
    private readonly QuizFeedback.Row row;

    public FrameworkElement View { get; }

    public QuizFeedbackQuestion(QuizSheet s, QuizFeedback.Row row)
    {
        this.s = s;
        this.row = row;
        View = K.V(18, Header(), QuizUi.Rich(s.Engine, row.Html, 16), Answers(), Options(), Solution());
    }

    private Color? Verdict => QuizFeedbackPane.Ink(row.Verdict);

    private SymbolRegular Mark => row.Verdict switch
    {
        "right" => SymbolRegular.CheckmarkCircle24,
        "wrong" => SymbolRegular.DismissCircle24,
        "partial" => SymbolRegular.SubtractCircle24,
        _ => SymbolRegular.QuestionCircle24,
    };

    private T Inked<T>(T t) where T : TextBlock
    {
        if (Verdict is { } c) t.Foreground = QuizUi.Ink(c); else t.Fg(B.Text2);
        return t;
    }

    private FrameworkElement Header()
    {
        var icon = Verdict is { } c ? K.Icon(Mark, 22, c, filled: true) : K.Icon(Mark, 22, B.Text2, filled: true);
        var g = K.Columns("auto, auto, auto, *, auto",
            icon.Margin(0, 0, 10, 0).Col(0),
            K.Text($"Question {row.N}", Ty.Subtitle).Col(1),
            Inked(K.Text(QuizUi.Sentence(row.VerdictText), Ty.BodyStrong)).Margin(12, 0, 0, 3).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom).Col(2));
        if (row.Score.Length > 0) g.Children.Add(Inked(K.Text(row.Score, Ty.BodyStrong).Digits()).Align(HorizontalAlignment.Right, VerticalAlignment.Bottom).Margin(12, 0, 0, 3).Col(4));
        return g;
    }

    private FrameworkElement Answers()
    {
        if (row.Match is { } m) return MatchTable(m);
        var mine = row.Verdict == "none" ? null : Verdict;
        if (row.Essay || row.Right.Count == 0)
        {
            var v = K.V(16, Answer("Your answer", row.Yours, mine, "No answer"));
            if (row.Right.Count > 0) v.Children.Add(Answer("Correct answer", row.Right, QuizUi.Green, "–"));
            return v;
        }
        return K.Columns("*, 28, *", Answer("Your answer", row.Yours, mine, "No answer").Col(0), Answer("Correct answer", row.Right, QuizUi.Green, "–").Col(2));
    }

    /// <summary>An answer under its label, a line of the verdict's colour at its edge; its words in that colour (an
    /// essay's in the text's own).</summary>
    private FrameworkElement Answer(string label, List<QuizFeedback.Part> parts, Color? color, string empty)
    {
        var v = K.V(6, K.Text(label, Ty.Caption, B.Text2).Strong());
        if (parts.Count == 0)
        {
            var none = K.Text(empty, Ty.Body, B.Text2);
            none.FontStyle = FontStyles.Italic;
            v.Children.Add(none);
        }
        foreach (var p in parts)
        {
            if (p.Text.Length > 0)
            {
                var t = K.Wrapped(p.Text, row.Essay ? Ty.Body : Ty.BodyStrong);
                t.TextTrimming = TextTrimming.None;
                if (!row.Essay && color is { } c) t.Foreground = QuizUi.Ink(c);
                v.Children.Add(t);
            }
            else if (p.Html.Length > 0) v.Children.Add(QuizUi.Rich(s.Engine, p.Html, 15));
        }
        return color is { } edge ? QuizUi.Edge(v, edge) : QuizUi.QuietEdge(v);
    }

    /// <summary>A matching question's pairs: a row each with a hairline between — the item, the match set (ticked or
    /// crossed), and the right one where it was missed and the quiz shows it.</summary>
    private FrameworkElement MatchTable(QuizFeedback.MatchTable m)
    {
        var v = K.V(0);
        for (var k = 0; k < m.Rows.Count; k++)
        {
            if (k > 0) v.Children.Add(K.Divider());
            var r = m.Rows[k];
            SymbolIcon mark = r.Ok switch
            {
                true => K.Icon(SymbolRegular.Checkmark24, 14, QuizUi.Green),
                false => K.Icon(SymbolRegular.Dismiss24, 14, QuizUi.Red),
                _ => K.Icon(SymbolRegular.ArrowRight24, 14, B.Text2),
            };
            mark.VerticalAlignment = VerticalAlignment.Top;
            mark.Margin = new Thickness(0, 3, 8, 0);
            var said = K.V(3, K.Wrapped(r.Mine ?? "No match set", Ty.Body, r.Mine == null ? B.Text2 : B.Text));
            if (m.ShowRight && r.Right is { } right && r.Ok != true)
            {
                var correct = K.Wrapped($"Correct: {right}", Ty.Caption);
                correct.FontWeight = FontWeights.Medium;
                correct.Foreground = QuizUi.Ink(QuizUi.Green);
                said.Children.Add(correct);
            }
            var g = K.Columns("*, 18, *", K.Wrapped(r.Left, Ty.BodyStrong).Col(0), K.Columns("auto, *", mark.Col(0), said.Col(1)).Col(2));
            g.Margin = new Thickness(0, 11, 0, 11);
            v.Children.Add(g);
        }
        return v;
    }

    /// <summary>Every option, folded away until asked for: the student's marked Yours, the right ones Correct.</summary>
    private FrameworkElement? Options()
    {
        if (row.Options is not { Count: > 0 } opts) return null;
        var list = K.V(0);
        for (var k = 0; k < opts.Count; k++)
        {
            if (k > 0) list.Children.Add(K.Divider(36));
            var o = opts[k];
            var letter = K.Text(o.Letter, Ty.Caption, B.Text2).Strong();
            letter.Width = 24;
            letter.VerticalAlignment = VerticalAlignment.Center;
            var chips = K.H(6);
            chips.VerticalAlignment = VerticalAlignment.Center;
            if (o.Mine) chips.Children.Add(K.Chip("Yours", s.Tint, strong: true));
            if (o.Right) chips.Children.Add(K.Chip("Correct", QuizUi.Green, strong: true));
            var g = K.Columns("auto, *, auto", letter.Margin(0, 0, 12, 0).Col(0), QuizUi.OptionText(s.Engine, o.Text, o.Html).Col(1), chips.Margin(12, 0, 0, 0).Col(2));
            g.Margin = new Thickness(0, 9, 0, 9);
            list.Children.Add(g);
        }
        list.Visibility = Visibility.Collapsed;
        list.Margin = new Thickness(0, 6, 0, 0);
        var chevron = K.Icon(SymbolRegular.ChevronRight20, 12, B.Text2);
        var words = K.Text($"Show all {opts.Count} options", Ty.BodyStrong);
        var toggle = K.Subtle(K.H(10, chevron, words), null, new Thickness(8, 6, 10, 6));
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Margin = new Thickness(-8, 0, 0, 0);
        toggle.Click += (_, _) =>
        {
            var open = list.Visibility != Visibility.Visible;
            list.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.Symbol = open ? SymbolRegular.ChevronDown20 : SymbolRegular.ChevronRight20;
            words.Text = open ? "Hide the options" : $"Show all {opts.Count} options";
            if (open) Motion.Enter(list, 0, 8, 250);
        };
        return K.V(0, toggle, list);
    }

    private FrameworkElement? Solution()
    {
        if (row.Solution is { } sol)
        {
            var head = K.H(8, K.Icon(SymbolRegular.Lightbulb24, 16, B.Text), K.Text("Worked solution", Ty.BodyStrong));
            FrameworkElement text = sol.Html.Length > 0 ? QuizUi.Rich(s.Engine, sol.Html, 15) : K.Wrapped(sol.Text, Ty.Body);
            return QuizUi.Edge(K.V(8, head, text), s.Tint, 0.55);
        }
        if (row.NoSolution) return K.Wrapped("Your instructor left no worked solution for this question.", Ty.Caption, B.Text2);
        return null;
    }
}
