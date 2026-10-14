using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// Flashcards' four ways to study a set (the Mac's FCRoundView, FCLearnView, FCTestView, FCMatchView).

/// <summary>A part of a study page drawn again as it changes; the set's own changes it makes itself do not draw it twice.</summary>
internal abstract class FCStudyPart : ContentControl
{
    protected readonly string deckID;
    private bool muted;

    protected FCStudyPart(string deckID, bool watch = true)
    {
        this.deckID = deckID;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        if (watch) StudyKit.Watch(this, h => FlashcardStore.Changed += h, h => FlashcardStore.Changed -= h, () => { if (!muted) Render(); });
    }

    protected FCDeck? Deck => FlashcardStore.Deck(deckID);

    protected abstract void Render();

    /// <summary>Something done here (the set changed with it), then the page drawn once.</summary>
    protected void Act(Action a)
    {
        muted = true;
        try { a(); } finally { muted = false; }
        Render();
    }

    /// <summary>A study page's column: as wide as the window allows up to its own width, in the middle.</summary>
    protected static T Column<T>(T e, double max) where T : FrameworkElement
    {
        e.MaxWidth = max;
        e.HorizontalAlignment = HorizontalAlignment.Stretch;
        return e;
    }

    /// <summary>A big count in its colour over what it counts.</summary>
    protected static FrameworkElement BigCount(int n, string label, Color color)
    {
        var num = StudyKit.Figure(n.ToString(CultureInfo.InvariantCulture), Ty.TitleLarge, color);
        num.HorizontalAlignment = HorizontalAlignment.Center;
        return K.V(2, num, K.Text(label, Ty.Body, B.Text2).Center());
    }

    /// <summary>Buttons in a row in the middle (the end of a round).</summary>
    protected static FrameworkElement Centered(params UIElement[] buttons)
    {
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var b in buttons)
        {
            if (b is FrameworkElement f) f.Margin = new Thickness(5, 0, 5, 8);
            row.Children.Add(b);
        }
        return row;
    }

    /// <summary>The keys go to the page (Space, the arrows) as it is drawn.</summary>
    protected void TakeKeys(FrameworkElement e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!e.IsVisible) return;
            e.Focus();
            Keyboard.Focus(e);
        }, DispatcherPriority.Input);
    }
}

// ---- Flashcards: know it, or still learning it ----

/// <summary>
/// Flashcards (the Mac's FCRoundView): one card at a time; flip it, then mark it Know or Still learning (→ and ←, Space
/// flips); Undo takes the last mark back. At the end, the counts, and Keep Reviewing runs the ones still being learned.
/// </summary>
internal sealed class FCRoundView : FCStudyPart
{
    private readonly Action back;
    private List<string> ids = new();
    private int i;
    private readonly List<string> know = new(), learning = new();
    private readonly List<(string Id, bool Know)> history = new();
    private bool flipped, starOnly, started, slide;
    private FCFlipCard? card;
    private FrameworkElement? keys;

    public FCRoundView(string deckID, Action back) : base(deckID)
    {
        this.back = back;
        if (FlashcardDemo.FlipFirst) { FlashcardDemo.FlipFirst = false; flipped = true; }
        Render();
    }

    private void Begin(FCDeck d, List<string>? only)
    {
        var starred = d.Cards.Where(c => c.Star).ToList();
        ids = only ?? (starOnly && starred.Count > 0 ? starred : d.Cards).Select(c => c.Id).ToList();
        i = 0;
        know.Clear();
        learning.Clear();
        history.Clear();
        flipped = false;
        started = true;
    }

    protected override void Render()
    {
        var hadKeys = keys?.IsKeyboardFocusWithin == true || keys == null;
        if (Deck is not { } d) { Content = null; keys = null; return; }
        if (!started)
        {
            var keep = flipped;
            Begin(d, null);
            flipped = keep;
        }
        // (a card taken out of the set meanwhile is passed over)
        while (i < ids.Count && d.Cards.All(c => c.Id != ids[i])) i++;
        if (i >= ids.Count)
        {
            keys = null;
            Content = Finished(d);
            return;
        }
        var cur = d.Cards.First(c => c.Id == ids[i]);
        var v = Round(d, cur);
        Content = v;
        if (slide) { slide = false; Motion.SlideIn(card!, true, 36); }
        if (hadKeys) TakeKeys(v);
    }

    private FrameworkElement Round(FCDeck d, FCCard c)
    {
        var progress = K.Bar((double)i / Math.Max(1, ids.Count), FCKit.Tint);
        var count = StudyKit.Figure($"{i + 1} / {ids.Count}", Ty.Body, brush: B.Text2).Margin(14, 0, 14, 0);
        var shuffle = StudyKit.IconBtn(SymbolRegular.ArrowShuffle24, () => Act(() =>
        {
            ids = StudyKit.Shuffled(ids.Skip(i));
            i = 0;
            know.Clear();
            learning.Clear();
            history.Clear();
            flipped = false;
        }), "Shuffle what is left");
        var top = K.Columns("*, auto, auto, auto", progress.Col(0), count.Col(1), shuffle.Col(2));
        if (d.Cards.Any(x => x.Star))
        {
            var only = K.Button("Starred only", () => Act(() => { starOnly = !starOnly; Begin(d, null); }), accent: starOnly, icon: SymbolRegular.Star24);
            only.MinWidth = 0;
            only.Margin = new Thickness(8, 0, 0, 0);
            only.ToolTip = starOnly ? "Every card" : "Only the cards you starred";
            top.Children.Add(only.Col(3));
        }

        card = new FCFlipCard(c, flipped, 380);
        card.Toggled += f => flipped = f;

        var undo = StudyKit.IconBtn(SymbolRegular.ArrowUndo24, Undo, "Undo");
        undo.IsEnabled = history.Count > 0;
        var stillCount = StudyKit.Figure($"{learning.Count}", Ty.Subtitle, StudyKit.Orange).Margin(0, 0, 18, 0);
        var still = StudyKit.Tinted("Still learning", StudyKit.Orange, () => Mark(c, false), SymbolRegular.Dismiss24);
        still.MinWidth = 150;
        still.Margin = new Thickness(0, 0, 18, 0);
        still.ToolTip = "Still learning (←)";
        var knowB = StudyKit.Tinted("Know", StudyKit.Green, () => Mark(c, true), SymbolRegular.Checkmark24);
        knowB.MinWidth = 150;
        knowB.Margin = new Thickness(0, 0, 18, 0);
        knowB.ToolTip = "Know (→)";
        var knowCount = StudyKit.Figure($"{know.Count}", Ty.Subtitle, StudyKit.Green);
        var row = K.Columns("auto, *, auto, auto, auto, auto, *, 44", undo.Col(0), stillCount.Col(2), still.Col(3), knowB.Col(4), knowCount.Col(5));

        var hint = K.Text("Space flips. ← still learning, → know.", Ty.Caption, B.Text2).Center();
        var v = Column(K.V(18, top, card, row, hint), 900);
        v.Focusable = true;
        v.FocusVisualStyle = null;
        v.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Space: card?.Flip(); break;
                case Key.Left: Mark(c, false); break;
                case Key.Right: Mark(c, true); break;
                default: return;
            }
            e.Handled = true;
        };
        keys = v;
        return v;
    }

    private void Mark(FCCard c, bool k)
    {
        Act(() =>
        {
            history.Add((c.Id, k));
            if (k) know.Add(c.Id); else learning.Add(c.Id);
            i++;
            flipped = false;
            slide = true;
            FlashcardStore.ChangeCard(deckID, c.Id, x => x.Know = k);
        });
    }

    private void Undo()
    {
        if (history.Count == 0) return;
        var last = history[^1];
        history.RemoveAt(history.Count - 1);
        know.RemoveAll(x => x == last.Id);
        learning.RemoveAll(x => x == last.Id);
        i = Math.Max(0, i - 1);
        flipped = false;
        Render();
    }

    private FrameworkElement Finished(FCDeck d)
    {
        var all = learning.Count == 0;
        var buttons = new List<UIElement>();
        if (!all) buttons.Add(K.Button($"Keep Reviewing {ToolKit.Plural(learning.Count, "Term")}", () => Act(() => Begin(d, learning.ToList())), accent: true));
        buttons.Add(K.Button("Restart flashcards", () => Act(() => Begin(d, null)), accent: all));
        buttons.Add(K.Button("Back to set", back));
        var counts = K.H(40, BigCount(know.Count, "Know", StudyKit.Green), BigCount(learning.Count, "Still learning", StudyKit.Orange));
        counts.HorizontalAlignment = HorizontalAlignment.Center;
        var v = K.V(18,
            K.Icon(SymbolRegular.CheckmarkCircle24, 54, StudyKit.Green, filled: true),
            K.Text(all ? "You know them all!" : "Nice work!", Ty.Title).Center(),
            K.Wrapped(all ? "Every card marked Know." : $"{ToolKit.Plural(learning.Count, "term")} still to learn.", Ty.Body, B.Text2).Center(),
            counts,
            Centered(buttons.ToArray()));
        v.Margin = new Thickness(0, 30, 0, 30);
        Motion.Enter(v, 0, 16);
        return v;
    }
}

// ---- Learn: multiple choice, then typed from memory ----

/// <summary>
/// Learn (the Mac's FCLearnView): every card asked as multiple choice first, then typed from memory; two right in a row
/// learns it (a wrong answer at either step sends it back to the start). A learned card comes back for a typed check
/// after a day, then at growing gaps.
/// </summary>
internal sealed class FCLearnView : FCStudyPart
{
    private readonly Action back;
    private List<string>? queue;
    private string? asked, picked, preparedFor;
    private FCMark? feedback;
    private List<string> choices = new();
    private string typed = "";

    public FCLearnView(string deckID, Action back) : base(deckID)
    {
        this.back = back;
        Render();
    }

    private static List<string> LearnQueue(FCDeck d)
    {
        var now = DateTime.UtcNow;
        return d.Cards.Where(c => c.Level < 2).Concat(d.Cards.Where(c => c.DueNow(now))).Select(c => c.Id).ToList();
    }

    /// <summary>The card asked: the queue's first — or, with the feedback showing, the one just answered.</summary>
    private FCCard? Current(FCDeck d)
    {
        if (feedback != null && asked != null) return d.Cards.FirstOrDefault(c => c.Id == asked);
        if (queue?.FirstOrDefault() is not { } first) return null;
        return d.Cards.FirstOrDefault(c => c.Id == first);
    }

    protected override void Render()
    {
        if (Deck is not { } d) { Content = null; return; }
        queue ??= LearnQueue(d);
        var learned = StudyKit.Figure($"{d.Mastered} of {d.Cards.Count} learned", Ty.Body, brush: B.Text2).Margin(14, 0, 0, 0);
        var head = K.Columns("*, auto", K.Bar((double)d.Mastered / Math.Max(1, d.Cards.Count), FCKit.Learn).Col(0), learned.Col(1));
        var v = Column(K.V(20, head), 900);
        if (Current(d) is { } c)
        {
            if (feedback == null && preparedFor != c.Id) Prepare(d, c);
            v.Children.Add(Question(d, c));
        }
        else if (queue != null) v.Children.Add(AllLearned());
        Content = v;
    }

    private void Prepare(FCDeck d, FCCard c)
    {
        if (feedback != null) return;
        asked = c.Id;
        picked = null;
        typed = "";
        preparedFor = c.Id;
        var wrong = d.Cards.Where(x => x.Id != c.Id && FCText.Norm(x.Def) != FCText.Norm(c.Def))
            .OrderByDescending(x => FCText.Alike(x.Def, c.Def)).Take(3).Select(x => x.Def);
        choices = StudyKit.Shuffled(wrong.Append(c.Def));
    }

    private FrameworkElement Question(FCDeck d, FCCard c)
    {
        var written = c.Level >= 1;
        var termAsked = new StringInfo(c.Term).LengthInTextElements <= 40;
        var want = written ? (termAsked ? c.Term : c.Def) : c.Def;
        var v = K.V(18,
            K.Text(written ? (termAsked ? "Type the term" : "Type the definition") : "Choose the definition", Ty.BodyStrong, B.Text2),
            StudyKit.Selectable(written && termAsked ? c.Def : c.Term, Ty.Title));
        if (written) v.Children.Add(Written(d, c, want));
        else v.Children.Add(Choices(d, c));
        if (feedback is { } f) v.Children.Add(FeedbackRow(f, want));
        return K.Card(v, new Thickness(26));
    }

    private FrameworkElement Choices(FCDeck d, FCCard c)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var n = 0; n < choices.Count; n++)
        {
            if (n % 2 == 0)
            {
                if (n > 0) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            var choice = choices[n];
            var num = StudyKit.Figure($"{n + 1}", Ty.BodyStrong, brush: B.Text2);
            num.VerticalAlignment = VerticalAlignment.Top;
            num.Margin = new Thickness(0, 0, 10, 0);
            var words = K.Columns("auto, *", num.Col(0), K.Wrapped(choice, Ty.Body).Col(1));
            var well = new StudyKit.Well(words, () =>
            {
                if (feedback != null) return;
                picked = choice;
                Answer(c, choice == c.Def);
            }, new Thickness(14));
            well.Root.MinHeight = 64;
            AutomationProperties.SetName(well.Button, choice);
            if (feedback != null)
            {
                well.Button.IsHitTestVisible = false;
                if (choice == c.Def) well.Tint(StudyKit.Green, 0.22);
                else if (choice == picked) well.Tint(StudyKit.Red, 0.2);
            }
            g.Children.Add(well.Root.Row(n / 2 * 2).Col(n % 2 * 2));
        }
        return g;
    }

    private FrameworkElement Written(FCDeck d, FCCard c, string want)
    {
        var box = ToolKit.Box(typed, "Your answer", t => typed = t);
        box.IsEnabled = feedback == null;
        AutomationProperties.SetName(box, "Your answer");
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || feedback != null) return;
            e.Handled = true;
            var m = FCText.Mark(typed, want);
            Answer(c, m != FCMark.Wrong, m == FCMark.Close);
        };
        var dontKnow = K.Button("Don’t know", () => Answer(c, false));
        dontKnow.IsEnabled = feedback == null;
        dontKnow.Margin = new Thickness(10, 0, 0, 0);
        if (feedback == null) TakeKeys(box);
        return K.Columns("*, auto", box.Col(0), dontKnow.Col(1));
    }

    private FrameworkElement FeedbackRow(FCMark f, string want)
    {
        var icon = f == FCMark.Wrong ? K.Icon(SymbolRegular.DismissCircle24, 24, StudyKit.Red, filled: true) : K.Icon(SymbolRegular.CheckmarkCircle24, 24, StudyKit.Green, filled: true);
        icon.Margin = new Thickness(0, 0, 12, 0);
        var words = K.V(2, K.Text(f == FCMark.Right ? "Correct" : f == FCMark.Close ? "Close enough" : "Not quite", Ty.BodyStrong));
        if (f != FCMark.Right) words.Children.Add(K.Wrapped($"The answer: {want}", Ty.Body, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var go = K.Button("Continue", Continue, accent: true);
        go.Margin = new Thickness(12, 0, 0, 0);
        var row = K.Columns("auto, *, auto", icon.Col(0), words.Col(1), go.Col(2));
        Motion.FadeIn(row, Motion.Normal);
        TakeKeys(go);
        return row;
    }

    private void Continue()
    {
        feedback = null;
        asked = null;
        typed = "";
        picked = null;
        preparedFor = null;
        if (Deck is { } d && Current(d) is { } next) Prepare(d, next);
        Render();
    }

    /// <summary>
    /// The answer kept on the card: two right in a row learns it, a wrong one sends it back to the start; a learned card
    /// right again waits two and a half times as long before its next check. In the queue, a wrong card comes round again
    /// after two others, a first right answer after three.
    /// </summary>
    private void Answer(FCCard c, bool ok, bool close = false)
    {
        var review = c.Level >= 2;
        var fresh = c.Level == 0;
        var now = DateTime.UtcNow;
        Act(() =>
        {
            var q = (queue ?? new List<string>()).Where(x => x != c.Id).ToList();
            if (!ok) q.Insert(Math.Min(2, q.Count), c.Id);
            else if (!review && fresh) q.Insert(Math.Min(3, q.Count), c.Id);
            queue = q;
            asked = c.Id;
            feedback = ok ? (close ? FCMark.Close : FCMark.Right) : FCMark.Wrong;
            FlashcardStore.ChangeCard(deckID, c.Id, y =>
            {
                if (!ok)
                {
                    y.Level = 0;
                    y.Iv = null;
                    y.Due = null;
                }
                else if (review)
                {
                    var iv = Math.Max(1, Math.Round((y.Iv ?? 1) * 2.5, MidpointRounding.AwayFromZero));
                    y.Iv = iv;
                    y.DueAt = now.AddDays(iv);
                }
                else
                {
                    y.Level = Math.Min(2, y.Level + 1);
                    if (y.Level == 2)
                    {
                        y.Iv = 1;
                        y.DueAt = now.AddDays(1);
                    }
                }
            });
        });
    }

    private FrameworkElement AllLearned()
    {
        var start = K.Button("Start over", () => Act(() =>
        {
            FlashcardStore.Change(deckID, d =>
            {
                foreach (var c in d.Cards)
                {
                    c.Level = 0;
                    c.Iv = null;
                    c.Due = null;
                }
            });
            if (Deck is { } d) queue = LearnQueue(d);
            preparedFor = null;
        }), accent: true);
        var v = K.V(16,
            K.Icon(SymbolRegular.CheckmarkStarburst24, 54, StudyKit.Green, filled: true),
            K.Text("You’ve learned them all!", Ty.Title).Center(),
            K.Wrapped("Every card right twice in a row. They come back for a quick check in a day, then less and less often.", Ty.Body, B.Text2).Center(),
            Centered(start, K.Button("Back to set", back)));
        v.Margin = new Thickness(0, 30, 0, 30);
        Motion.Enter(v, 0, 16);
        return v;
    }
}

// ---- Test ----

internal enum FCQKind { Choice, TrueFalse, Written }

/// <summary>A question of a test: its kind, its card, what a true-or-false one shows beside the term, and its choices.</summary>
internal sealed record FCQuestion(Guid Id, FCQKind Kind, FCCard Card, string Shown, List<string> Choices);

/// <summary>
/// Test (the Mac's FCTestView): up to twenty questions made from the set — multiple choice, true or false, written —
/// answered in any order and marked at the end, the right answers shown.
/// </summary>
internal sealed class FCTestView : FCStudyPart
{
    private readonly Action back;
    private List<FCQuestion> questions = new();
    private readonly Dictionary<Guid, string> answers = new();
    private bool marked;

    public FCTestView(string deckID, Action back) : base(deckID, watch: false)
    {
        this.back = back;
        Make();
        Render();
    }

    private void Make()
    {
        if (Deck is not { } d || d.Cards.Count == 0) return;
        var picked = StudyKit.Shuffled(d.Cards).Take(20).ToList();
        questions = picked.Select((c, n) =>
        {
            var others = d.Cards.Where(x => x.Id != c.Id).ToList();
            var kind = others.Count < 3 ? (n % 2 == 0 ? FCQKind.Written : FCQKind.TrueFalse) : new[] { FCQKind.Choice, FCQKind.TrueFalse, FCQKind.Written }[n % 3];
            var shown = kind == FCQKind.TrueFalse && Random.Shared.Next(2) == 0 && others.Count > 0 ? others[Random.Shared.Next(others.Count)].Def : c.Def;
            var choices = kind == FCQKind.Choice ? StudyKit.Shuffled(StudyKit.Shuffled(others).Take(3).Select(x => x.Def).Append(c.Def)) : new List<string>();
            return new FCQuestion(Guid.NewGuid(), kind, c, shown, choices);
        }).ToList();
        answers.Clear();
        marked = false;
    }

    private bool Right(FCQuestion q)
    {
        var a = answers.TryGetValue(q.Id, out var x) ? x : "";
        return q.Kind switch
        {
            FCQKind.Choice => a == q.Card.Def,
            FCQKind.TrueFalse => a == (q.Shown == q.Card.Def ? "true" : "false"),
            _ => FCText.Mark(a, q.Card.Term) != FCMark.Wrong,
        };
    }

    protected override void Render()
    {
        var v = Column(K.V(18), 900);
        if (marked)
        {
            var n = questions.Count(Right);
            var score = StudyKit.Figure($"{n} / {questions.Count}", Ty.TitleLarge, StudyKit.Orange);
            var words = K.Wrapped(n == questions.Count ? "Every one right." : "The right answers are marked below.", Ty.Body, B.Text2);
            words.VerticalAlignment = VerticalAlignment.Center;
            words.Margin = new Thickness(16, 0, 0, 0);
            v.Children.Add(K.Columns("auto, *", score.Col(0), words.Col(1)));
        }
        for (var n = 0; n < questions.Count; n++) v.Children.Add(QuestionCard(n, questions[n]));
        if (marked)
            v.Children.Add(K.H(10, K.Button("New test", () => { Make(); Render(); }, accent: true), K.Button("Back to set", back)));
        else
        {
            var check = K.Button("Check answers", () => { marked = true; Render(); }, accent: true);
            check.IsEnabled = questions.Count > 0;
            v.Children.Add(check.Align(HorizontalAlignment.Left));
        }
        Content = v;
    }

    private FrameworkElement QuestionCard(int n, FCQuestion q)
    {
        var host = StudyKit.Host();
        void Draw()
        {
            var head = K.Columns("*, auto", K.Text($"{n + 1} of {questions.Count}", Ty.Caption, B.Text2).Strong().Col(0));
            if (marked)
                head.Children.Add((Right(q)
                    ? K.Icon(SymbolRegular.CheckmarkCircle24, 18, StudyKit.Green, filled: true)
                    : K.Icon(SymbolRegular.DismissCircle24, 18, StudyKit.Red, filled: true)).Col(1));
            var v = K.V(12, head);
            switch (q.Kind)
            {
                case FCQKind.Choice:
                    v.Children.Add(K.Wrapped(q.Card.Term, Ty.BodyLarge));
                    foreach (var ch in q.Choices) v.Children.Add(Choice(q, ch, ch, Draw));
                    break;
                case FCQKind.TrueFalse:
                    v.Children.Add(K.Wrapped(q.Card.Term, Ty.BodyLarge));
                    v.Children.Add(K.Wrapped(q.Shown, Ty.Body, B.Text2));
                    v.Children.Add(K.Columns("*, 10, *", Choice(q, "true", "True", Draw).Col(0), Choice(q, "false", "False", Draw).Col(2)));
                    break;
                default:
                    v.Children.Add(K.Wrapped(q.Card.Def, Ty.BodyLarge));
                    var box = ToolKit.Box(answers.TryGetValue(q.Id, out var a) ? a : "", "Type the term", t => answers[q.Id] = t);
                    box.IsEnabled = !marked;
                    AutomationProperties.SetName(box, $"Question {n + 1}: the term");
                    v.Children.Add(box);
                    if (marked && !Right(q))
                    {
                        var answer = K.Wrapped($"The answer: {q.Card.Term}", Ty.Body);
                        answer.Foreground = new SolidColorBrush(K.Darkened(StudyKit.Green));
                        v.Children.Add(answer);
                    }
                    break;
            }
            host.Content = K.Card(v, new Thickness(20));
        }
        Draw();
        return host;
    }

    private FrameworkElement Choice(FCQuestion q, string value, string label, Action redraw)
    {
        var chosen = answers.TryGetValue(q.Id, out var a) && a == value;
        var correct = q.Kind == FCQKind.Choice ? value == q.Card.Def : value == (q.Shown == q.Card.Def ? "true" : "false");
        var well = new StudyKit.Well(K.Wrapped(label, Ty.Body), () =>
        {
            if (marked) return;
            answers[q.Id] = value;
            redraw();
        }, new Thickness(12));
        AutomationProperties.SetName(well.Button, label);
        if (marked)
        {
            well.Button.IsHitTestVisible = false;
            if (correct) well.Tint(StudyKit.Green, 0.22);
            else if (chosen) well.Tint(StudyKit.Red, 0.2);
        }
        else if (chosen) well.Tint(StudyKit.Orange, 0.22);
        return well.Root;
    }
}

// ---- Match ----

/// <summary>A tile of Match: a card's term or its definition.</summary>
internal sealed record FCTile(string Id, string Card, string Text, bool IsTerm);

/// <summary>
/// Match (the Mac's FCMatchView): up to six terms and their definitions as tiles; press a term and its definition (or the
/// other way round) to clear them — a wrong pair flashes red — against the clock, the best time kept with the set.
/// </summary>
internal sealed class FCMatchView : FCStudyPart
{
    private readonly Action back;
    private List<FCTile> tiles = new();
    private readonly HashSet<string> gone = new();
    private HashSet<string> wrong = new();
    private string? picked;
    private DateTime started = DateTime.UtcNow;
    private double? finished;
    private bool newBest;
    private readonly Dictionary<string, StudyKit.Well> wells = new();
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(0.1) };
    private TextBlock? time;

    public FCMatchView(string deckID, Action back) : base(deckID, watch: false)
    {
        this.back = back;
        clock.Tick += (_, _) => { if (finished == null && time != null) time.Text = Seconds((DateTime.UtcNow - started).TotalSeconds); };
        Loaded += (_, _) => { if (finished == null) clock.Start(); };
        Unloaded += (_, _) => clock.Stop();
        Make();
    }

    private static string Seconds(double s) => string.Format(CultureInfo.InvariantCulture, "{0:0.0} s", s);

    private void Make()
    {
        if (Deck is not { } d) { Content = null; return; }
        tiles = StudyKit.Shuffled(StudyKit.Shuffled(d.Cards).Take(6).SelectMany(c => new[]
        {
            new FCTile("t" + c.Id, c.Id, c.Term, true), new FCTile("d" + c.Id, c.Id, c.Def, false),
        }));
        gone.Clear();
        picked = null;
        wrong = new();
        finished = null;
        newBest = false;
        started = DateTime.UtcNow;
        if (IsLoaded) clock.Start();
        Render();
    }

    protected override void Render()
    {
        time = StudyKit.Figure(Seconds(finished ?? (DateTime.UtcNow - started).TotalSeconds), Ty.Count);
        time.FontSize = 34;
        time.FontWeight = FontWeights.Bold;
        var head = K.Columns("*, auto", time.Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0));
        if (Deck?.Best is { } best) head.Children.Add(K.Text(string.Format(CultureInfo.InvariantCulture, "Best {0:0.0} s", best), Ty.Body, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
        var v = Column(K.V(18, head), 1000);
        if (finished != null)
        {
            var done = K.V(12, K.Text(newBest ? "A new best time!" : "All matched.", Ty.Subtitle).Center(),
                Centered(K.Button("Play again", Make, accent: true), K.Button("Back to set", back)));
            done.Margin = new Thickness(0, 20, 0, 20);
            Motion.Enter(done, 0, 16);
            v.Children.Add(done);
        }
        else
        {
            wells.Clear();
            var grid = new Simpl.Screens.MoreGrid(200, 12, tiles.Select(t => (UIElement)Tile(t)), stretch: true);
            v.Children.Add(grid);
            Paint();
        }
        Content = v;
    }

    private FrameworkElement Tile(FCTile t)
    {
        var words = K.Wrapped(t.Text, t.IsTerm ? Ty.BodyStrong : Ty.Body).Center();
        words.VerticalAlignment = VerticalAlignment.Center;
        var well = new StudyKit.Well(words, () => Press(t), new Thickness(14));
        well.Root.MinHeight = 96;
        well.Root.RenderTransformOrigin = new Point(0.5, 0.5);
        well.Root.RenderTransform = new ScaleTransform(1, 1);
        AutomationProperties.SetName(well.Button, t.Text);
        wells[t.Id] = well;
        return well.Root;
    }

    /// <summary>Each tile as it is now: picked in green, a wrong pair in red, a matched pair shrinking away.</summary>
    private void Paint()
    {
        foreach (var (id, well) in wells)
        {
            if (wrong.Contains(id)) well.Tint(StudyKit.Red, 0.22);
            else if (picked == id) well.Tint(StudyKit.Green, 0.24);
            else well.Tint(null, 0);
            if (gone.Contains(id) && well.Root.IsEnabled)
            {
                well.Root.IsEnabled = false;
                well.Root.IsHitTestVisible = false;
                if (well.Root.RenderTransform is ScaleTransform s)
                {
                    Motion.To(s, ScaleTransform.ScaleXProperty, 0.85, Motion.Medium);
                    Motion.To(s, ScaleTransform.ScaleYProperty, 0.85, Motion.Medium);
                }
                Motion.To(well.Root, OpacityProperty, 0, Motion.Medium);
            }
        }
    }

    private void Press(FCTile t)
    {
        if (finished != null) return;
        if (picked is not { } p || tiles.FirstOrDefault(x => x.Id == p) is not { } first)
        {
            picked = t.Id;
            Paint();
            return;
        }
        if (first.Id == t.Id)
        {
            picked = null;
            Paint();
            return;
        }
        if (first.Card == t.Card)
        {
            gone.Add(first.Id);
            gone.Add(t.Id);
            picked = null;
            Paint();
            if (gone.Count == tiles.Count)
            {
                var took = (DateTime.UtcNow - started).TotalSeconds;
                newBest = took < (Deck?.Best ?? double.PositiveInfinity);
                finished = took;
                clock.Stop();
                FlashcardStore.Change(deckID, d =>
                {
                    if (d.Best == null || took < d.Best) d.Best = Math.Round(took * 10, MidpointRounding.AwayFromZero) / 10;
                });
                // (the last pair seen leaving before the result)
                var t2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Motion.On ? 260 : 0) };
                t2.Tick += (_, _) => { t2.Stop(); Render(); };
                t2.Start();
            }
            return;
        }
        var pair = new HashSet<string> { first.Id, t.Id };
        wrong = pair;
        picked = null;
        Paint();
        var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.45) };
        later.Tick += (_, _) =>
        {
            later.Stop();
            if (!ReferenceEquals(wrong, pair)) return;
            wrong = new();
            Paint();
        };
        later.Start();
    }
}
