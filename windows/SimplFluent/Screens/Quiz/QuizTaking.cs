using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

// The attempt of the quiz popup (the Mac's QuizTaking.swift): the question on the page, its answers as rows, the
// questions as numbered tiles (a strip over the page, or a column beside it on a wide popup) and the foot of the page.

/// <summary>
/// The attempt, one question at a time: the questions (a strip at the top, or down the side of a wide popup), the
/// question on the page (it slides the way the student moves), and at the foot Previous, how much is answered and
/// whether it is saved, Submit and Next. Keys: ← and → move, 1–9 pick an answer, Enter goes on (to the review after the
/// last), F flags — none of them while an answer is being typed.
/// </summary>
internal sealed class QuizTakePane : IQuizPane
{
    private readonly QuizSheet s;
    private readonly bool wide;
    private readonly ContentControl pageHost = new() { Focusable = false, ClipToBounds = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private QuizQuestionPage? page;
    private readonly List<QuizTile> tiles = new();
    private readonly TextBlock status = K.Text("", Ty.Body, B.Text2).Digits();
    private readonly ProgressRing statusSpinner = K.Spinner(14);
    private readonly Wpf.Ui.Controls.Button previous, submit, next;
    private Button? review;
    // the side column's count, bar and save word
    private TextBlock? cardCount, railCount, railSave;
    private readonly ContentControl railBar = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };

    public FrameworkElement View { get; }

    public QuizTakePane(QuizSheet s, bool wide)
    {
        this.s = s;
        this.wide = wide;
        previous = QuizUi.Button("Previous", Back, icon: SymbolRegular.ChevronLeft24);
        previous.ToolTip = "Previous question (←)";
        submit = QuizUi.Button("Submit…", s.AskSubmit);
        submit.ToolTip = "Submit this attempt (Ctrl+Enter)";
        next = QuizUi.Button("Next", Next, accent: true, icon: SymbolRegular.ChevronRight24, after: true);
        var foot = K.Columns("auto, *, auto, auto",
            previous.Col(0),
            K.H(6, statusSpinner, status).Align(HorizontalAlignment.Center, VerticalAlignment.Center).Margin(10, 0, 10, 0).Col(1),
            submit.Col(2),
            next.Margin(10, 0, 0, 0).Col(3));
        if (wide)
        {
            var rail = QuizUi.Scroll(Rail());
            var line = new Border { Width = 1 }.Bg(B.Divider);
            var right = K.Rows("*, auto", pageHost.Row(0), QuizUi.Foot(foot).Row(1));
            View = K.Columns("256, 1, *", rail.Col(0), line.Col(1), right.Col(2));
        }
        else
        {
            View = K.Rows("auto, *, auto", Strip().Row(0), pageHost.Row(1), QuizUi.Foot(foot).Row(2));
        }
        ShowPage(false);
        Paint();
    }

    private QuizRun Run => s.Run;

    public Action? Default => null; // (Enter is the attempt's own key: on)

    /// <summary>How many questions the tiles were made for.</summary>
    public int Tiles => tiles.Count;

    public void Update(QuizChange c)
    {
        if ((c & (QuizChange.Attempt | QuizChange.Index)) != 0) ShowPage(c.HasFlag(QuizChange.Index));
        if ((c & (QuizChange.Answer | QuizChange.Flag | QuizChange.Uploading)) != 0) page?.Repaint();
        if ((c & (QuizChange.Attempt | QuizChange.Index | QuizChange.Answer | QuizChange.Flag | QuizChange.Saving | QuizChange.Moving)) != 0) Paint();
    }

    // ---- the page ----

    /// <summary>The question showing, on its page: it slides in from the side the student went to.</summary>
    private void ShowPage(bool moved)
    {
        if (Run.Current is not { } q)
        {
            page = null;
            var empty = K.V(12, K.Icon(SymbolRegular.QuestionCircle24, 40, B.Text3), K.Text("This quiz has no questions.", Ty.Subtitle).Center());
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center;
            pageHost.Content = empty;
            return;
        }
        page = new QuizQuestionPage(s, q, Choose, Flag, Next);
        pageHost.Content = page.View;
        if (moved) Motion.SlideIn(page.View, Run.Forward, 80);
        // (the tiles follow the question showing)
        pageHost.Dispatcher.BeginInvoke(() => { if (Run.Idx < tiles.Count) tiles[Run.Idx].BringIntoView(); }, DispatcherPriority.Loaded);
        Support.Shot.Moved();
    }

    /// <summary>The tiles, the counts, the save word and the foot as things stand now.</summary>
    private void Paint()
    {
        var qs = Run.Questions;
        var moving = Run.Moving;
        var noBack = Run.Attempt?.NoBack == true;
        for (var k = 0; k < tiles.Count && k < qs.Count; k++)
        {
            var q = qs[k];
            var sealedOff = noBack && k < Run.Idx;
            tiles[k].Paint(q, k == Run.Idx, q.IsAnswered && q.Kind != "info", sealedOff, moving == k, moving == null, s.Tint);
        }
        var done = Run.AnsweredCount;
        var total = qs.Count;
        var word = Run.SaveWord;
        status.Text = $"{done} of {total} answered" + (word.Length > 0 ? $" · {word}" : "");
        statusSpinner.Visibility = moving != null ? Visibility.Visible : Visibility.Collapsed;
        if (cardCount != null) cardCount.Text = $"{done} of {total}";
        if (railCount != null) railCount.Text = $"{done} of {total} answered";
        if (railSave != null) { railSave.Text = word; railSave.Visibility = word.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }
        railBar.Content = K.Bar(total > 0 ? (double)done / total : 0, s.Tint, 4);
        previous.IsEnabled = Run.CanGoBack && moving == null;
        next.IsEnabled = moving == null;
        if (review != null) review.IsEnabled = moving == null;
        var last = Run.IsLast;
        if (next.Tag as bool? != last)
        {
            next.Tag = last;
            next.Content = K.H(8, new TextBlock { Text = last ? "Review Answers" : "Next", VerticalAlignment = VerticalAlignment.Center },
                new SymbolIcon { Symbol = last ? SymbolRegular.TextBulletListSquare24 : SymbolRegular.ChevronRight24, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            next.ToolTip = last ? "Review your answers (Enter)" : "Next question (Enter or →)";
        }
        // (Canvas's own page is moving to the question: a beat, then it is here)
        pageHost.Opacity = moving != null ? 0.5 : 1;
        pageHost.IsHitTestVisible = moving == null;
    }

    // ---- the questions as tiles ----

    /// <summary>Every question's number as a tile in a strip over the page (a narrow popup), Review at its end.</summary>
    private FrameworkElement Strip()
    {
        var row = new Stack { Orientation = Orientation.Horizontal, Spacing = 6 };
        for (var k = 0; k < Run.Questions.Count; k++)
        {
            var t = new QuizTile(k + 1, 36, Jump);
            tiles.Add(t);
            row.Children.Add(t);
        }
        var card = K.Card(row, new Thickness(8));
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.Margin = new Thickness(22, 10, 12, 10);
        var scroll = new ScrollViewer
        {
            Content = card, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, PanningMode = PanningMode.HorizontalOnly,
        };
        // (the wheel scrolls the strip sideways)
        scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta / 3.0); e.Handled = true; };
        review = QuizUi.Button("Review", Review, icon: SymbolRegular.TextBulletListSquare24);
        review.ToolTip = "Review every answer before submitting";
        review.Margin = new Thickness(0, 0, 22, 0);
        review.VerticalAlignment = VerticalAlignment.Center;
        return K.Columns("*, auto", scroll.Col(0), review.Col(1));
    }

    /// <summary>The questions down the side of a wide popup: every question's number in a small card, what the tiles
    /// mean, how much is answered (and whether it is saved), and the review.</summary>
    private FrameworkElement Rail()
    {
        // the card of tiles, five across, with how many are answered at its head
        var grid = new UniformGrid { Columns = 5, Margin = new Thickness(-3) };
        for (var k = 0; k < Run.Questions.Count; k++)
        {
            var t = new QuizTile(k + 1, null, Jump) { Margin = new Thickness(3) };
            tiles.Add(t);
            grid.Children.Add(t);
        }
        cardCount = K.Text("", Ty.Caption, B.Text2).Digits();
        var head = K.Columns("*, auto", K.Text("Questions", Ty.BodyStrong).Col(0), cardCount.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
        var card = K.Card(K.V(12, head, grid), new Thickness(14));

        var legend = K.V(8,
            Key(Face.Current, "On screen"),
            Key(Face.Done, "Answered"),
            Key(Face.Flagged, "Flagged"),
            Key(Face.Blank, "Not answered"));

        railCount = K.Text("", Ty.BodyStrong).Digits();
        railSave = K.Text("", Ty.Caption, B.Text2);
        review = QuizUi.Button("Review Answers", Review, icon: SymbolRegular.TextBulletListSquare24);
        review.HorizontalAlignment = HorizontalAlignment.Stretch;
        review.ToolTip = "Review every answer before submitting";
        review.Margin = new Thickness(0, 6, 0, 0);
        var progress = K.V(10, railCount, railBar, railSave, review);

        var v = K.V(18, card, legend, K.Divider(), progress);
        v.Margin = new Thickness(22, 14, 22, 24);
        return v;
    }

    private enum Face { Current, Done, Flagged, Blank }

    private FrameworkElement Key(Face f, string text)
    {
        var swatch = new Border { Width = 13, Height = 13, CornerRadius = new CornerRadius(3.5) };
        switch (f)
        {
            case Face.Current: swatch.Background = K.Brush(s.Tint); break;
            case Face.Done: swatch.Background = K.Brush(K.WithAlpha(s.Tint, 0.2)); break;
            case Face.Flagged: swatch.Bg(B.Subtle); swatch.BorderBrush = K.Brush(QuizUi.Orange); swatch.BorderThickness = new Thickness(1.5); break;
            default: swatch.Bg(B.Subtle).Stroke(B.CardStroke); break;
        }
        return K.H(10, swatch, K.Text(text, Ty.Caption, B.Text2));
    }

    // ---- what the keys and buttons do ----

    public void Keys(KeyEventArgs e)
    {
        if (QuizUi.Typing() || Keyboard.Modifiers != ModifierKeys.None) return;
        switch (e.Key)
        {
            case System.Windows.Input.Key.Left:
                Back();
                e.Handled = true;
                return;
            case System.Windows.Input.Key.Right:
                Next();
                e.Handled = true;
                return;
            case System.Windows.Input.Key.Enter:
                if (Keyboard.FocusedElement is ButtonBase) return; // (a focused button presses itself)
                Next();
                e.Handled = true;
                return;
        }
        if (Run.Current is not { } q || Run.Moving != null) return;
        if (e.Key == System.Windows.Input.Key.F && q.Kind is not ("info" or "pending"))
        {
            Flag(q);
            e.Handled = true;
            return;
        }
        var n = e.Key switch
        {
            >= System.Windows.Input.Key.D1 and <= System.Windows.Input.Key.D9 => e.Key - System.Windows.Input.Key.D1 + 1,
            >= System.Windows.Input.Key.NumPad1 and <= System.Windows.Input.Key.NumPad9 => e.Key - System.Windows.Input.Key.NumPad1 + 1,
            _ => 0,
        };
        if (n > 0 && q.Kind is "choice" or "multi" && n - 1 < q.Options.Count)
        {
            Choose(q, q.Options[n - 1]);
            e.Handled = true;
        }
    }

    /// <summary>On to the next question, or — from the last — to the review.</summary>
    private void Next()
    {
        s.FocusKeys();
        if (Run.Moving != null) return;
        if (Run.IsLast) _ = Run.ToReview();
        else _ = Run.Next();
    }

    private void Back()
    {
        s.FocusKeys();
        if (!Run.CanGoBack || Run.Moving != null) return;
        _ = Run.Back();
    }

    private void Jump(int k)
    {
        s.FocusKeys();
        _ = Run.Go(k);
    }

    private void Review() => _ = Run.ToReview();

    /// <summary>An option clicked or its number pressed: one answer picked, or a box ticked or cleared. Saved at once.</summary>
    private void Choose(QuizQuestion q, QuizQuestion.Option o)
    {
        s.FocusKeys();
        var multi = q.Kind == "multi";
        Run.Set(q.Id, x =>
        {
            if (multi) { if (!x.Picks.Remove(o.Id)) x.Picks.Add(o.Id); }
            else x.Pick = o.Id;
        });
    }

    private void Flag(QuizQuestion q)
    {
        s.FocusKeys();
        _ = Run.Flag(q.Id);
    }
}

/// <summary>
/// One question's tile as a button (the Mac's QuizChip and QuizNumberTile): its number in a small rounded square — the
/// one showing filled with the quiz's colour, an answered one washed in it, one not yet answered a quiet grey; a flagged
/// one edged in orange with its flag in the corner, one on its way a spinner, one sealed behind the student (no going
/// back) dimmed and shut. A touch brighter under the pointer, a little smaller under a click.
/// </summary>
internal sealed class QuizTile : Button
{
    private readonly int number;
    private readonly Border face = new() { Height = 32, CornerRadius = new CornerRadius(6), SnapsToDevicePixels = true };
    private readonly TextBlock text;
    private readonly SymbolIcon flag = K.Icon(SymbolRegular.Flag24, 9, QuizUi.Orange, filled: true);
    private readonly ProgressRing spinner = K.Spinner(14);
    private readonly Border hover = new() { CornerRadius = new CornerRadius(6), Visibility = Visibility.Hidden, IsHitTestVisible = false };
    private readonly ScaleTransform scale = new(1, 1);

    public QuizTile(int number, double? width, Action<int> jump)
    {
        this.number = number;
        SetResourceReference(StyleProperty, "SubtleButton");
        K.SetCorner(this, new CornerRadius(6));
        Padding = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        text = K.Text(number.ToString(), Ty.Body).Digits().Center();
        text.FontWeight = FontWeights.SemiBold;
        text.VerticalAlignment = VerticalAlignment.Center;
        flag.HorizontalAlignment = HorizontalAlignment.Right;
        flag.VerticalAlignment = VerticalAlignment.Top;
        flag.Margin = new Thickness(0, 3, 4, 0);
        spinner.Visibility = Visibility.Collapsed;
        face.Child = new Grid { Children = { text, flag, spinner } };
        hover.Bg(B.Subtle);
        Content = new Grid { Children = { face, hover } };
        if (width is { } w) Width = w;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = scale;
        MouseEnter += (_, _) => hover.Visibility = Visibility.Visible;
        MouseLeave += (_, _) => { hover.Visibility = Visibility.Hidden; Press(false); };
        PreviewMouseLeftButtonDown += (_, _) => Press(true);
        PreviewMouseLeftButtonUp += (_, _) => Press(false);
        // (the one showing stays a button: a click on it does nothing, and asks nothing)
        Click += (_, _) => jump(this.number - 1);
    }

    private void Press(bool down)
    {
        Motion.To(scale, ScaleTransform.ScaleXProperty, down ? 0.94 : 1, Motion.Fast, Motion.Standard);
        Motion.To(scale, ScaleTransform.ScaleYProperty, down ? 0.94 : 1, Motion.Fast, Motion.Standard);
    }

    public void Paint(QuizQuestion q, bool current, bool done, bool sealedOff, bool loading, bool free, Color tint)
    {
        var flagged = q.Flagged;
        if (current)
        {
            face.Background = K.Brush(tint);
            text.Foreground = Brushes.White; // (on the quiz's own colour)
        }
        else if (done)
        {
            face.Background = K.Brush(K.WithAlpha(tint, Support.Appearance.Dark ? 0.32 : 0.18));
            text.Foreground = QuizUi.Ink(tint);
        }
        else
        {
            face.Bg(B.Subtle);
            text.Fg(B.Text2);
        }
        if (flagged && !current) { face.BorderBrush = K.Brush(QuizUi.Orange); face.BorderThickness = new Thickness(1.5); }
        else if (!current && !done) face.Stroke(B.CardStroke);
        else face.BorderThickness = new Thickness(0);
        flag.Visibility = flagged ? Visibility.Visible : Visibility.Collapsed;
        flag.Foreground = K.Brush(current ? Colors.White : QuizUi.Orange);
        spinner.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        text.Visibility = loading ? Visibility.Hidden : Visibility.Visible;
        face.Opacity = sealedOff ? 0.35 : 1;
        IsEnabled = !sealedOff && free;
        var tip = $"Question {number}";
        if (sealedOff) tip += " — sealed";
        else if (q.Kind == "info") tip += " — information only";
        else tip += q.IsAnswered ? " — answered" : " — not answered";
        if (flagged) tip += ", flagged";
        ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(this, $"Question {number}{(done ? ", answered" : "")}{(flagged ? ", flagged" : "")}{(current ? ", showing" : "")}");
    }
}

/// <summary>
/// One question on the page (the Mac's QuizQuestionPage), with no box round it: its number and points, the flag, its
/// words (Canvas's own, pictures included; formulas as their TeX) at a reading size, and its answer in the shape its
/// kind takes — a row per choice, a box per answer that applies, a field, a list per match or blank, a file chosen or
/// dropped, or Canvas's own page for a kind it answers there.
/// </summary>
internal sealed class QuizQuestionPage
{
    private readonly QuizSheet s;
    private readonly QuizQuestion q;
    private readonly Action<QuizQuestion, QuizQuestion.Option> choose;
    private readonly Action<QuizQuestion> flag;
    /// <summary>Enter in a one-line answer: on to the next question.</summary>
    private readonly Action onReturn;
    private readonly List<(QuizQuestion.Option O, Border Wash, ToggleButton Mark, TextBlock Letter)> rows = new();
    private Wpf.Ui.Controls.Button? flagButton;
    private readonly ContentControl fileArea = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private bool dropping;
    private Rectangle? dropEdge;
    private SymbolIcon? dropIcon;
    private TextBlock? dropWords;

    public FrameworkElement View { get; }

    public QuizQuestionPage(QuizSheet s, QuizQuestion q, Action<QuizQuestion, QuizQuestion.Option> choose, Action<QuizQuestion> flag, Action onReturn)
    {
        this.s = s;
        this.q = q;
        this.choose = choose;
        this.flag = flag;
        this.onReturn = onReturn;
        var column = new Stack { Orientation = Orientation.Vertical, Spacing = 30, MaxWidth = QuizUi.Column };
        column.Children.Add(Header());
        if (q.Kind == "pending")
        {
            var loading = QuizUi.Working("Loading the question…");
            loading.HorizontalAlignment = HorizontalAlignment.Center;
            column.Children.Add(new Grid { MinHeight = 160, Children = { loading.Align(HorizontalAlignment.Center, VerticalAlignment.Center) } });
        }
        else
        {
            column.Children.Add(QuizUi.Rich(s.Engine, q.Html, 18));
            if (Answer() is { } a) column.Children.Add(a);
            if (q.Hint.Length > 0)
                column.Children.Add(K.Columns("auto, *", K.Icon(SymbolRegular.Info24, 16, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Margin(0, 0, 8, 0).Col(0), K.Wrapped(q.Hint, Ty.Caption, B.Text2).Col(1)));
        }
        var pad = new Border { Child = column, Padding = new Thickness(56, 30, 56, 40), Background = Brushes.Transparent };
        // (a click off the answers: the keys are the attempt's again)
        pad.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == pad || e.OriginalSource == column) s.FocusKeys(); };
        View = QuizUi.Scroll(pad);
    }

    private QuizRun Run => s.Run;

    private FrameworkElement Header()
    {
        var name = K.Text($"Question {q.N}", Ty.Title);
        var of = K.Text(OfLine(), Ty.Body, B.Text2).Digits().Margin(12, 0, 0, 5).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom);
        var g = K.Columns("auto, *, auto", name.Col(0), of.Col(1));
        if (q.Kind is not ("info" or "pending"))
        {
            flagButton = K.Button("Flag", () => flag(q));
            flagButton.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(flagButton.Col(2));
            PaintFlag();
        }
        return K.V(14, g, K.Divider());
    }

    private string OfLine()
    {
        var total = Run.Questions.Count;
        if (q.Points is not { } p) return $"of {total}";
        return $"of {total} · {QuizFormat.Points(p)} {(p == 1 ? "point" : "points")}";
    }

    private void PaintFlag()
    {
        if (flagButton == null) return;
        flagButton.Content = q.Flagged ? "Flagged" : "Flag";
        var icon = new SymbolIcon { Symbol = SymbolRegular.Flag24, Filled = q.Flagged };
        if (q.Flagged) icon.Foreground = K.Brush(QuizUi.Orange);
        flagButton.Icon = icon;
        flagButton.ToolTip = q.Flagged ? "Remove the flag (F)" : "Flag this question to come back to it (F)";
        System.Windows.Automation.AutomationProperties.SetName(flagButton, q.Flagged ? "Flagged for review" : "Flag for review");
    }

    /// <summary>The answer changed (a pick, a flag, an upload): its rows, the flag and the file shown as they are now —
    /// never a field being typed in.</summary>
    public void Repaint()
    {
        PaintFlag();
        foreach (var (o, wash, mark, letter) in rows)
        {
            var on = q.Kind == "multi" ? q.Picks.Contains(o.Id) : q.Pick == o.Id;
            mark.IsChecked = on;
            wash.Background = on ? K.Brush(K.WithAlpha(s.Tint, Support.Appearance.Dark ? 0.24 : 0.14)) : Brushes.Transparent;
            if (on) letter.Foreground = QuizUi.Ink(s.Tint); else letter.Fg(B.Text2);
        }
        if (q.Kind == "file") FileArea();
    }

    private FrameworkElement? Answer() => q.Kind switch
    {
        "choice" => Options(false),
        "multi" => K.V(12, Options(true), K.Text("Pick every answer that applies.", Ty.Caption, B.Text2)),
        "text" => Line(false),
        "number" => Line(true),
        "essay" => Essay(),
        "match" => Matching(),
        "drops" or "blanks" => Blanks(),
        "file" => File(),
        "info" => null,
        _ => Elsewhere(),
    };

    // ---- choices ----

    /// <summary>A row per option with its radio (one answer) or its box (every answer that applies), the whole row the
    /// target; its number on the keyboard picks it. The rows reach a little past the column, so their words start where
    /// the question's do.</summary>
    private FrameworkElement Options(bool multi)
    {
        var list = new Stack { Orientation = Orientation.Vertical, Spacing = 8, Margin = new Thickness(-20, 0, -20, 0) };
        for (var k = 0; k < q.Options.Count; k++)
        {
            var o = q.Options[k];
            ToggleButton mark = multi ? new CheckBox() : new RadioButton();
            mark.IsHitTestVisible = false;
            mark.Focusable = false;
            mark.IsTabStop = false;
            mark.MinWidth = 0;
            mark.Padding = new Thickness(0);
            mark.VerticalAlignment = VerticalAlignment.Center;
            var letter = K.Text(o.Letter, Ty.BodyStrong, B.Text2);
            letter.MinWidth = 18;
            letter.VerticalAlignment = VerticalAlignment.Center;
            var g = K.Columns("auto, auto, *, auto", mark.Col(0), letter.Margin(8, 0, 16, 0).Col(1), QuizUi.OptionText(s.Engine, o.Text, o.Html).Col(2));
            if (k < 9)
            {
                var key = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 0, 6, 1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), Child = K.Text($"{k + 1}", Ty.Caption, B.Text3) }.Stroke(B.ControlStroke);
                key.ToolTip = $"Press {k + 1} to choose this answer";
                g.Children.Add(key.Col(3));
            }
            var wash = new Border { Child = g, Padding = new Thickness(20, 14, 20, 14), CornerRadius = new CornerRadius(8), MinHeight = 56, Background = Brushes.Transparent };
            var row = K.Subtle(wash, () => choose(q, o), new Thickness(0), 8);
            System.Windows.Automation.AutomationProperties.SetName(row, QuizFormat.OptionName(o));
            rows.Add((o, wash, mark, letter));
            list.Children.Add(row);
        }
        Repaint();
        return list;
    }

    // ---- fields ----

    /// <summary>A field no wider than it needs to be, at the left.</summary>
    private static FrameworkElement Capped(FrameworkElement e, double max)
    {
        var g = K.Columns("*, auto", e.Col(0));
        g.ColumnDefinitions[0].MaxWidth = max;
        return g;
    }

    private FrameworkElement Line(bool numeric)
    {
        var box = new TextBox { PlaceholderText = numeric ? "Number" : "Your answer", Text = q.Text, ClearButtonEnabled = false };
        System.Windows.Documents.Typography.SetNumeralAlignment(box, FontNumeralAlignment.Tabular);
        System.Windows.Automation.AutomationProperties.SetName(box, numeric ? "Your answer, a number" : "Your answer");
        box.TextChanged += (_, _) => Run.Set(q.Id, x => x.Text = box.Text, typed: true);
        box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { onReturn(); e.Handled = true; } };
        return numeric ? Capped(box, 300) : box;
    }

    private FrameworkElement Essay()
    {
        var box = new TextBox
        {
            Text = q.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 300, ClearButtonEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
        };
        System.Windows.Automation.AutomationProperties.SetName(box, "Your answer");
        box.TextChanged += (_, _) => Run.Set(q.Id, x => x.Text = box.Text, typed: true);
        return K.V(8, box, K.Wrapped("A blank line starts a new paragraph; lines starting with • or 1. become a list.", Ty.Caption, B.Text2));
    }

    /// <summary>A list to pick from: "Select…" first (never the blank's own name — an instructor often names it after its
    /// answer — so an empty one never looks answered), then the choices.</summary>
    private ComboBox Picker(string name, IEnumerable<QuizQuestion.Choice> choices, string? picked, Action<string?> set)
    {
        var box = new ComboBox { MinWidth = 200 };
        box.Items.Add(new ComboBoxItem { Content = "Select…" });
        var at = 0;
        foreach (var c in choices)
        {
            box.Items.Add(new ComboBoxItem { Content = c.Text, Tag = c.Id });
            if (c.Id == picked) at = box.Items.Count - 1;
        }
        box.SelectedIndex = at;
        System.Windows.Automation.AutomationProperties.SetName(box, name);
        box.SelectionChanged += (_, _) => set((box.SelectedItem as ComboBoxItem)?.Tag as string);
        box.DropDownOpened += (_, _) => s.Hold(true);
        box.DropDownClosed += (_, _) => s.Hold(false);
        return box;
    }

    /// <summary>Matching: a row per item with a list of the matches beside it, a hairline between the rows.</summary>
    private FrameworkElement Matching()
    {
        var v = new Stack { Orientation = Orientation.Vertical };
        for (var k = 0; k < q.Options.Count; k++)
        {
            var o = q.Options[k];
            if (k > 0) v.Children.Add(K.Divider());
            var picker = Picker(QuizFormat.OptionName(o), q.Matches, q.Map.TryGetValue(o.Id, out var m) ? m : null, id =>
                Run.Set(q.Id, x => { if (id == null) x.Map.Remove(o.Id); else x.Map[o.Id] = id; }));
            picker.Width = 280;
            picker.VerticalAlignment = VerticalAlignment.Center;
            var g = K.Columns("*, 18, auto", QuizUi.OptionText(s.Engine, o.Text, o.Html).Col(0), picker.Col(2));
            g.Margin = new Thickness(0, 12, 0, 12);
            v.Children.Add(g);
        }
        return v;
    }

    /// <summary>One field per blank, numbered as its mark in the question's words: a list to pick from, or a line to type.</summary>
    private FrameworkElement Blanks()
    {
        var v = K.V(12);
        foreach (var b in q.Blanks)
        {
            var badge = new Border
            {
                MinWidth = 28, Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(6, 0, 6, 0),
                Background = K.Brush(K.WithAlpha(s.Tint, 0.16)), VerticalAlignment = VerticalAlignment.Center,
            };
            var n = K.Text($"{b.N}", Ty.Caption).Digits().Center();
            n.FontWeight = FontWeights.Bold;
            n.Foreground = QuizUi.Ink(s.Tint);
            n.VerticalAlignment = VerticalAlignment.Center;
            badge.Child = n;
            FrameworkElement field;
            if (q.Kind == "drops")
            {
                var blank = b;
                field = Capped(Picker(b.Label, b.Options, q.Map.TryGetValue(b.Id, out var m) ? m : null, id =>
                    Run.Set(q.Id, x => { if (id == null) x.Map.Remove(blank.Id); else x.Map[blank.Id] = id; })), 340);
            }
            else
            {
                var box = new TextBox { PlaceholderText = "Your answer", Text = q.Map.TryGetValue(b.Id, out var t) ? t : "", ClearButtonEnabled = false };
                System.Windows.Automation.AutomationProperties.SetName(box, $"Blank {b.N}");
                var id = b.Id;
                box.TextChanged += (_, _) => Run.Set(q.Id, x => x.Map[id] = box.Text, typed: true);
                box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { onReturn(); e.Handled = true; } };
                field = Capped(box, 380);
            }
            v.Children.Add(K.Columns("auto, 14, *", badge.Col(0), field.Col(2)));
        }
        return v;
    }

    // ---- a file ----

    /// <summary>A file chosen, dragged in from File Explorer, or a photo picked; uploaded to the student's own quiz files
    /// on Canvas, then replaced or removed.</summary>
    private FrameworkElement File()
    {
        FileArea();
        return fileArea;
    }

    private void FileArea()
    {
        var v = K.V(16);
        if (Run.Uploading == q.Id) v.Children.Add(QuizUi.Working("Uploading…"));
        else if (q.Files.FirstOrDefault() is { } f)
        {
            var remove = K.Button("Remove", () => Run.Set(q.Id, x => x.Files = new()));
            remove.Appearance = ControlAppearance.Danger;
            var words = K.V(2, K.Text(f.Name, Ty.BodyStrong), K.Text("Handed in with this attempt when you submit.", Ty.Caption, B.Text2));
            words.VerticalAlignment = VerticalAlignment.Center;
            v.Children.Add(K.Columns("auto, *, auto",
                K.Icon(SymbolRegular.Document24, 28, K.Darkened(s.Tint), filled: true).Margin(0, 0, 12, 0).Col(0),
                words.Col(1),
                remove.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2)));
        }
        v.Children.Add(DropZone());
        fileArea.Content = v;
    }

    /// <summary>Where a file is dropped: the one outline on the page, dashed, in the quiz's colour while a file is over it.</summary>
    private FrameworkElement DropZone()
    {
        dropEdge = new Rectangle { RadiusX = 8, RadiusY = 8, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 2.7 }, SnapsToDevicePixels = true };
        dropIcon = new SymbolIcon { Symbol = SymbolRegular.DocumentArrowDown20, FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center };
        dropWords = K.Text("", Ty.Body, B.Text2).Center();
        var busy = Run.Uploading != null;
        var chooseFile = QuizUi.Button(q.Files.Count == 0 ? "Choose File…" : "Replace…", PickFile, icon: SymbolRegular.Folder24);
        var photo = QuizUi.Button("Photo…", PickPhoto, icon: SymbolRegular.Image24);
        chooseFile.IsEnabled = photo.IsEnabled = !busy;
        var buttons = K.H(10, chooseFile, photo);
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        var content = K.V(12, dropIcon, dropWords, buttons);
        content.Margin = new Thickness(24);
        var zone = new Grid { AllowDrop = true, Background = Brushes.Transparent, Children = { dropEdge, content } };
        zone.DragEnter += Dragging;
        zone.DragOver += Dragging;
        zone.DragLeave += (_, _) => { dropping = false; PaintDrop(); };
        zone.Drop += (_, e) =>
        {
            dropping = false;
            PaintDrop();
            if (Run.Uploading != null || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
            Take(paths[0]);
        };
        PaintDrop();
        return zone;
    }

    private void Dragging(object sender, DragEventArgs e)
    {
        var ok = Run.Uploading == null && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (ok != dropping) { dropping = ok; PaintDrop(); }
    }

    private void PaintDrop()
    {
        if (dropEdge == null || dropIcon == null || dropWords == null) return;
        if (dropping)
        {
            dropEdge.Stroke = K.Brush(s.Tint);
            dropEdge.Fill = K.Brush(K.WithAlpha(s.Tint, 0.1));
            dropIcon.Foreground = K.Brush(s.Tint);
        }
        else
        {
            dropEdge.SetResourceReference(Shape.StrokeProperty, B.StrongStroke);
            dropEdge.Fill = Brushes.Transparent;
            dropIcon.SetResourceReference(Control.ForegroundProperty, B.Text2);
        }
        dropWords.Text = dropping ? "Drop to upload it" : q.Files.Count == 0 ? "Drag a file here, or" : "Drag another file here to replace it, or";
    }

    private void PickFile()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = q.Files.Count == 0 ? "Choose a File" : "Replace the File", CheckFileExists = true };
        if (d.ShowDialog(Window.GetWindow(View)) == true) Take(d.FileName);
    }

    private void PickPhoto()
    {
        var d = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a Photo", CheckFileExists = true,
            Filter = "Photos and videos|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.mp4;*.mov;*.m4v;*.avi;*.wmv|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (d.ShowDialog(Window.GetWindow(View)) == true) Take(d.FileName);
    }

    /// <summary>A file to hand in with this question: read, then uploaded.</summary>
    private void Take(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        byte[] data;
        try { data = System.IO.File.ReadAllBytes(path); }
        catch
        {
            Run.Tell($"{name} could not be read.", error: true);
            return;
        }
        _ = Run.Upload(q.Id, name, Mime(System.IO.Path.GetExtension(path)), data);
    }

    private static string Mime(string ext) => ext.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".rtf" => "application/rtf",
        ".zip" => "application/zip",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        ".heif" => "image/heif",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".m4v" => "video/x-m4v",
        ".avi" => "video/x-msvideo",
        ".wmv" => "video/x-ms-wmv",
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".wav" => "audio/wav",
        _ => "application/octet-stream",
    };

    // ---- a kind the app does not answer ----

    /// <summary>A kind Canvas adds that the app does not know yet: Canvas's own page answers it, on the same attempt.</summary>
    private FrameworkElement Elsewhere()
    {
        var go = QuizUi.Button("Answer on Canvas", s.OpenCanvas, accent: true, icon: SymbolRegular.Globe24);
        go.HorizontalAlignment = HorizontalAlignment.Left;
        return K.V(12, K.Wrapped("This kind of question is answered on Canvas’s quiz page. Your other answers are already saved there.", Ty.Body, B.Text2), go);
    }
}
