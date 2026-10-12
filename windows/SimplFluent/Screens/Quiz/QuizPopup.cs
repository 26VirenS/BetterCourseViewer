using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// A Classic Quiz taken in the app, in a popup over the window (the Mac's QuizScreen sheet): the intro (its rules, the
/// access code, Begin or Resume), the attempt one question at a time (the questions as numbered tiles to move between
/// them, flags, the clock, every kind of question answered in place), the review before handing in, the receipt, and
/// the feedback on a finished attempt. Answers save as they are given; nothing is handed in unless the student says so.
/// In an attempt ← and → move between the questions, 1–9 pick an answer, Enter goes on, F flags, Ctrl+Enter submits and
/// Escape closes (asking first).
/// </summary>
public static class QuizPopup
{
    /// <summary>The quiz over the window, as large as the window less a margin; modal while an attempt is under way.</summary>
    public static void Show(Engine.Engine engine, Simpl.Engine.QuizLaunch launch, Window over) => new QuizSheet(engine, launch, over).Open();
}

/// <summary>What a stage of the popup shows (the intro, the attempt, the review, the receipt, the feedback).</summary>
internal interface IQuizPane
{
    FrameworkElement View { get; }
    /// <summary>Something changed that the pane shows in place (an answer, a save, a move under way).</summary>
    void Update(QuizChange c);
    /// <summary>The pane's own keys (the attempt's).</summary>
    void Keys(KeyEventArgs e);
    /// <summary>What Enter does when nothing else takes it (Begin, Done), or null.</summary>
    Action? Default { get; }
}

/// <summary>
/// The popup (the Mac's QuizScreen): its own header — the quiz and where it stands, the clock and the instructions during
/// an attempt, the quiz on Canvas in its ⋯ menu, Close — then the stage's pane, a note at its top when a save fails or the
/// clock runs low, and the questions it asks over itself (Leave the quiz?, Submit this attempt?).
/// </summary>
internal sealed class QuizSheet
{
    public Engine.Engine Engine { get; }
    public QuizRun Run { get; }
    private readonly Simpl.Engine.QuizLaunch launch;
    private readonly Window over;
    private readonly Sheet sheet;
    private readonly Grid root = new() { Focusable = true, FocusVisualStyle = null };
    private readonly Grid body = new() { ClipToBounds = true };
    private readonly ContentControl paneHost = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Border bannerHost = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(24, 10, 24, 0), MaxWidth = 620, Visibility = Visibility.Collapsed };
    private readonly QuizAskLayer ask = new();
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private IQuizPane? pane;
    private bool? paneWide;
    private int holds;
    private bool closed, askingCode;
    private bool? clockLow;
    private DateTime instructionsClosedAt;

    // the header
    private readonly Button back;
    private readonly ContentControl tileHost = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = K.Text("", Ty.Subtitle);
    private readonly TextBlock subtitle = K.Text("", Ty.Caption, B.Text2);
    private readonly Border clockPill = new() { CornerRadius = new CornerRadius(15), Padding = new Thickness(12, 5, 14, 6), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock clockText = K.Text("", Ty.BodyStrong).Digits();
    private readonly SymbolIcon clockIcon = new() { Symbol = SymbolRegular.Timer24, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button instructionsButton;
    private readonly Button moreButton;
    private readonly Wpf.Ui.Controls.Button closeButton;
    private Popup? instructions;

    public QuizSheet(Engine.Engine engine, Simpl.Engine.QuizLaunch launch, Window over)
    {
        Engine = engine;
        this.launch = launch;
        this.over = over;
        Run = new QuizRun(engine, launch.Course, launch.Quiz);
        var room = Room();
        sheet = new Sheet { Bare = true, Width = room.Width, Height = room.Height, Body = root };

        back = K.IconButton(SymbolRegular.ArrowLeft24, () => Run.LeaveFeedback(), "Back", 16, 36);
        instructionsButton = K.IconButton(SymbolRegular.BookOpen24, ToggleInstructions, "Instructions", 18, 36);
        moreButton = K.IconButton(SymbolRegular.MoreHorizontal24, MoreMenu, "More", 18, 36);
        closeButton = K.Button("Close", Close);
        closeButton.MinWidth = 80;
        clockPill.Child = K.H(8, clockIcon, clockText);

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildHeader().Row(0));
        root.Children.Add(K.Divider().Row(1));
        body.Children.Add(paneHost);
        body.Children.Add(bannerHost);
        root.Children.Add(body.Row(2));
        Grid.SetRowSpan(ask, 3);
        root.Children.Add(ask);
        root.PreviewKeyDown += OnKey;
        bannerHost.MouseLeftButtonUp += (_, _) => Run.DismissBanner();
        bannerHost.ToolTip = "Click to dismiss";
        ask.OpenChanged += () => { Modality(); if (!ask.IsOpen) FocusKeys(); };
        body.SizeChanged += (_, e) => { if (paneWide is { } w && w != (e.NewSize.Width >= QuizUi.Wide)) Present(false); };
        root.Loaded += (_, _) => Fit();

        Run.Changed += OnChange;
        tick.Tick += (_, _) => Clock();
        sheet.Closed += Gone;
    }

    /// <summary>The quiz's colour: its course's.</summary>
    public Color Tint => QuizUi.TintOf(Run.Attempt?.Color ?? Run.Intro?.Color);

    /// <summary>The width a pane is laid out in.</summary>
    public double Width => body.ActualWidth > 0 ? body.ActualWidth : sheet.Width;

    private bool InAttempt => Run.Now is QuizRun.Stage.Taking or QuizRun.Stage.Review or QuizRun.Stage.Submitting;

    public void Open()
    {
        Simpl.Shell.Dialogs.Show(sheet, over);
        over.SizeChanged += WindowSized;
        Engine.PropertyChanged += EngineChanged;
        Present(false);
        Header();
        tick.Start();
        root.Dispatcher.BeginInvoke(new Action(Fit), DispatcherPriority.Loaded);
        _ = Start();
    }

    /// <summary>The intro, then wherever the launch asked for: the last attempt's feedback, or straight into the attempt (on
    /// to a question, or its review).</summary>
    private async Task Start()
    {
        await Run.LoadIntro();
        if (closed) return;
        if (launch.Feedback && Run.Intro?.Last is { Feedback: true } last) await Run.OpenFeedback(last.Attempt);
        else if (launch.Begin && Run.Intro is { CanStart: true, NeedsCode: false })
        {
            await Run.Begin();
            if (Run.Now != QuizRun.Stage.Taking) return;
            if (launch.StartAt is { } k) await Run.Go(k);
            if (launch.Review) await Run.ToReview();
        }
    }

    // ---- the popup's size: the window's, less a margin ----

    private Size Room()
    {
        var fe = over.Content as FrameworkElement;
        var w = fe is { ActualWidth: > 0 } ? fe.ActualWidth : over.ActualWidth;
        var h = fe is { ActualHeight: > 0 } ? fe.ActualHeight : over.ActualHeight;
        return new Size(Math.Max(360, w - 48), Math.Max(320, h - 48));
    }

    private void WindowSized(object? sender, SizeChangedEventArgs e) => Fit();

    /// <summary>The popup kept at the window's size as the window changes (maximised, snapped, made smaller).</summary>
    private void Fit()
    {
        if (closed || sheet.Card is not { } card) return;
        var r = Room();
        card.MaxWidth = r.Width;
        card.MaxHeight = r.Height;
        card.Width = r.Width;
        card.Height = r.Height;
    }

    // ---- what changed ----

    private void OnChange(QuizChange c)
    {
        if (closed) return;
        if (c.HasFlag(QuizChange.Stage)) Present(true);
        else if (c.HasFlag(QuizChange.Intro) && Run.Now == QuizRun.Stage.Intro) Present(false); // (read again: View ▸ Reload)
        else if (c.HasFlag(QuizChange.Refused) && Run.Now == QuizRun.Stage.Intro) Present(false);
        else if (c.HasFlag(QuizChange.Feedback) && Run.Now == QuizRun.Stage.Feedback) Present(false);
        else if (c.HasFlag(QuizChange.Attempt) && pane is QuizTakePane tp && tp.Tiles != Run.Questions.Count) Present(false); // (another list of questions)
        else pane?.Update(c);
        if ((c & (QuizChange.Stage | QuizChange.Intro | QuizChange.Attempt | QuizChange.Feedback)) != 0) Header();
        if (c.HasFlag(QuizChange.Banner)) Banner();
        if (c.HasFlag(QuizChange.AskCode) && Run.AskCode) AskCode();
        if (c.HasFlag(QuizChange.Missing) && Run.Missing.Count > 0) AskMissing();
        if (c.HasFlag(QuizChange.TimeUp) && Run.TimeUp) AskTimeUp();
    }

    private void EngineChanged(object? sender, PropertyChangedEventArgs e)
    {
        // (View ▸ Reload: the intro read again)
        if (e.PropertyName == nameof(Simpl.Engine.Engine.DataVersion) && Run.Now == QuizRun.Stage.Intro) _ = Run.LoadIntro();
    }

    /// <summary>The stage's pane: each step — the intro, starting, the attempt, the review, the receipt, the feedback —
    /// fades in over the last; the review arrives over the attempt.</summary>
    private void Present(bool animate)
    {
        var stage = Run.Now;
        var wide = Width >= QuizUi.Wide;
        // (the review and handing it in are one pane: Submitting only changes its foot)
        if (animate && pane is QuizReviewPane && stage is QuizRun.Stage.Review or QuizRun.Stage.Submitting && paneWide == wide)
        {
            pane.Update(QuizChange.Stage);
            Modality();
            return;
        }
        var focusCode = Keyboard.FocusedElement is TextBox { Tag: "code" } t && t.IsKeyboardFocusWithin;
        holds = instructions is { IsOpen: true } ? 1 : 0; // (a list open in the pane going away goes with it)
        pane = stage switch
        {
            QuizRun.Stage.Intro when Run.Intro is { } i => new QuizIntroPane(this, i, wide),
            QuizRun.Stage.Taking => new QuizTakePane(this, wide),
            QuizRun.Stage.Review or QuizRun.Stage.Submitting => new QuizReviewPane(this, wide),
            QuizRun.Stage.Receipt => new QuizReceiptPane(this),
            QuizRun.Stage.Feedback => new QuizFeedbackPane(this, wide),
            _ => null,
        };
        paneWide = wide;
        FrameworkElement view = pane?.View ?? (stage == QuizRun.Stage.Starting ? Starting() : K.LoadState(Run.IntroError, () => _ = Run.LoadIntro()));
        paneHost.Content = view;
        if (animate)
        {
            if (stage is QuizRun.Stage.Review) Motion.DrillIn(view, Motion.Medium);
            else Motion.FadeIn(view, Motion.Medium);
        }
        Modality();
        Support.Shot.Moved();
        if (pane is QuizIntroPane ip) ip.Arrived(focusCode);
        else if (!ask.IsOpen) FocusKeys();
    }

    private FrameworkElement Starting()
    {
        var resuming = Run.Intro?.Begin.StartsWith("Continue", StringComparison.Ordinal) == true;
        var v = QuizUi.Working(resuming ? "Resuming your attempt…" : "Starting your attempt…");
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        return new Grid { Children = { v } };
    }

    /// <summary>Escape is the popup's own (and nothing closes it by itself) during an attempt, and while a menu, a list or
    /// a question is open over it.</summary>
    private void Modality() => sheet.Modal = InAttempt || holds > 0 || ask.IsOpen;

    /// <summary>A menu or a list opened over the popup (or closed): Escape is its own meanwhile.</summary>
    public void Hold(bool on)
    {
        holds = Math.Max(0, holds + (on ? 1 : -1));
        Modality();
    }

    /// <summary>The keyboard back with the popup itself: the attempt's keys (after a pick, a move, a click off the answers).</summary>
    public void FocusKeys()
    {
        root.Dispatcher.BeginInvoke(() =>
        {
            if (closed || ask.IsOpen || !root.IsVisible) return;
            root.Focus();
            Keyboard.Focus(root);
        }, DispatcherPriority.Input);
    }

    // ---- the header ----

    private FrameworkElement BuildHeader()
    {
        var words = K.V(2, title, subtitle);
        words.VerticalAlignment = VerticalAlignment.Center;
        var trailing = K.H(8, clockPill, instructionsButton, moreButton, closeButton);
        trailing.VerticalAlignment = VerticalAlignment.Center;
        back.VerticalAlignment = VerticalAlignment.Center;
        back.Margin = new Thickness(0, 0, 8, 0);
        var g = K.Columns("auto, auto, *, auto", back.Col(0), tileHost.Col(1), words.Margin(14, 0, 16, 0).Col(2), trailing.Col(3));
        // (the window's title bar takes the clicks in its band: the header's buttons sit below it)
        g.Margin = new Thickness(20, 30, 16, 14);
        return g;
    }

    private string TitleNow => Run.Attempt?.Title ?? Run.Intro?.Title ?? launch.Title;

    private void Header()
    {
        var stage = Run.Now;
        title.Text = TitleNow;
        tileHost.Content = K.IconTile(SymbolRegular.TaskListLtr24, Tint, 32);
        var context = Run.Attempt?.Context ?? Run.Intro?.Context ?? "";
        var attempt = Run.Attempt is { } a ? $"Attempt {a.Attempt}" : "";
        string[] parts = stage switch
        {
            QuizRun.Stage.Intro or QuizRun.Stage.Starting => new[] { context },
            QuizRun.Stage.Taking => new[] { attempt, context },
            QuizRun.Stage.Review or QuizRun.Stage.Submitting => new[] { "Review", attempt },
            QuizRun.Stage.Receipt => new[] { "Submitted", context },
            _ => new[] { "Feedback", Run.Feedback?.Attempt is { } n ? $"Attempt {n}" : "" },
        };
        subtitle.Text = string.Join(" · ", parts.Where(p => p.Length > 0));
        subtitle.Visibility = subtitle.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        back.Visibility = stage == QuizRun.Stage.Feedback ? Visibility.Visible : Visibility.Collapsed;
        instructionsButton.Visibility = InAttempt ? Visibility.Visible : Visibility.Collapsed;
        closeButton.IsEnabled = stage != QuizRun.Stage.Submitting;
        closeButton.ToolTip = InAttempt ? "Save your answers and close — the attempt stays open" : "Close the quiz";
        Clock();
    }

    /// <summary>The attempt's clock: the time left on a timed attempt (red in its last five minutes), or the time taken so
    /// far — and its warnings, once each.</summary>
    private void Clock()
    {
        if (closed) return;
        var now = DateTimeOffset.UtcNow;
        Run.CheckClock(now);
        var left = Run.Remaining(now);
        var shown = left ?? Run.Elapsed(now);
        if (!InAttempt || shown is not { } s)
        {
            clockPill.Visibility = Visibility.Collapsed;
            return;
        }
        var low = (left ?? double.PositiveInfinity) < 300;
        var time = QuizTime.Clock(s);
        clockText.Text = time;
        clockIcon.Symbol = left != null ? SymbolRegular.Timer24 : SymbolRegular.Clock24;
        clockPill.Visibility = Visibility.Visible;
        if (low != clockLow)
        {
            // (its last five minutes in Windows' critical colours)
            clockPill.Bg(low ? B.CriticalBack : B.Subtle);
            clockText.Fg(low ? B.Critical : B.Text);
            clockIcon.SetResourceReference(Control.ForegroundProperty, low ? B.Critical : B.Text2);
            clockLow = low;
        }
        clockPill.ToolTip = left != null ? "Time left in this attempt" : "Time taken so far";
        System.Windows.Automation.AutomationProperties.SetName(clockPill, left != null ? $"{time} left" : $"{time} so far");
    }

    /// <summary>The quiz's instructions, a click away the whole way through an attempt: a flyout under the button.</summary>
    private void ToggleInstructions()
    {
        if (instructions is { IsOpen: true }) { instructions.IsOpen = false; return; }
        // (a click on the button while it is open closes it as a click outside: not opened again by the same click)
        if ((DateTime.UtcNow - instructionsClosedAt).TotalMilliseconds < 300) return;
        var html = Run.Attempt?.Html ?? Run.Intro?.Html ?? "";
        FrameworkElement text = html.Length == 0 ? K.Wrapped("This quiz came with no instructions.", Ty.Body, B.Text2) : QuizUi.Rich(Engine, html, 15);
        var scroll = QuizUi.Scroll(new Border { Child = text, Padding = new Thickness(20) });
        var content = K.Rows("auto, auto, *", K.Text("Instructions", Ty.Subtitle).Margin(20, 16, 20, 10).Row(0), K.Divider().Row(1), scroll.Row(2));
        var frame = new Border { Child = content, Width = 500, Height = 460, CornerRadius = new CornerRadius(8), Effect = K.Shadow(32, 10, 0.22), Margin = new Thickness(16) };
        frame.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        content.SizeChanged += (_, e) => content.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        if (instructions != null) root.Children.Remove(instructions);
        instructions = new Popup
        {
            Child = frame, PlacementTarget = instructionsButton, Placement = PlacementMode.Bottom, StaysOpen = false,
            AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
            HorizontalOffset = instructionsButton.ActualWidth - (500 + 32) + 16, VerticalOffset = -10,
        };
        instructions.Opened += (_, _) => Hold(true);
        instructions.Closed += (_, _) => { instructionsClosedAt = DateTime.UtcNow; Hold(false); FocusKeys(); };
        root.Children.Add(instructions); // (in the popup's tree: Escape and the theme reach it)
        instructions.IsOpen = true;
    }

    /// <summary>⋯: Review Answers and Submit… during the attempt; the quiz on Canvas and its link.</summary>
    private void MoreMenu()
    {
        var menu = new ContextMenu { PlacementTarget = moreButton, Placement = PlacementMode.Bottom, MinWidth = 220 };
        MenuItem Item(string text, SymbolRegular icon, Action act)
        {
            var mi = new MenuItem { Header = text, Icon = new SymbolIcon { Symbol = icon } };
            mi.Click += (_, _) => act();
            return mi;
        }
        if (Run.Now == QuizRun.Stage.Taking)
        {
            menu.Items.Add(Item("Review Answers", SymbolRegular.TextBulletListSquare24, () => _ = Run.ToReview()));
            menu.Items.Add(Item("Submit…", SymbolRegular.Send24, AskSubmit));
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(Item("Open in Canvas", SymbolRegular.Globe24, OpenCanvas));
        menu.Items.Add(Item("Copy Link", SymbolRegular.Link24, CopyLink));
        menu.Opened += (_, _) => Hold(true);
        menu.Closed += (_, _) => Hold(false);
        menu.IsOpen = true;
    }

    // ---- the note at the top ----

    /// <summary>A save that failed, a warning of the clock: Windows' InfoBar at the top (red for a failure), gone by itself
    /// or with a click.</summary>
    private void Banner()
    {
        if (Run.Banner is not { } b)
        {
            if (bannerHost.Visibility != Visibility.Visible) return;
            Motion.FadeOut(bannerHost, () => { if (Run.Banner == null) { bannerHost.Visibility = Visibility.Collapsed; bannerHost.Child = null; } }, Motion.Normal);
            return;
        }
        var bar = new InfoBar
        {
            Title = "", Message = b.Text, Severity = b.Error ? InfoBarSeverity.Error : InfoBarSeverity.Warning, IsOpen = true, IsClosable = false,
        };
        var face = new Border { Child = bar, CornerRadius = new CornerRadius(4), Effect = K.Shadow(20, 6, 0.18) }.Bg(B.Solid);
        bannerHost.Child = face;
        bannerHost.Opacity = 1;
        bannerHost.Visibility = Visibility.Visible;
        Motion.Enter(bannerHost, 0, -16, 300);
        Support.Shot.Moved();
    }

    // ---- what the buttons do ----

    /// <summary>Close: at once outside an attempt; during one, after asking (every answer is saved; the attempt stays open).</summary>
    public void Close()
    {
        if (Run.Now == QuizRun.Stage.Submitting) return;
        if (!InAttempt) { Dismiss(); return; }
        ask.Show(new QuizAskLayer.Ask
        {
            Title = "Leave the quiz?",
            Message = $"Every answer is saved with Canvas. The attempt stays open — come back to it from the quiz{(Run.Attempt?.Timed == true ? "; the clock keeps running" : "")}.",
            Go = "Save and Exit",
            OnGo = async () => { await Run.Flush(); Dismiss(); },
            Cancel = "Keep Working",
        });
    }

    public void Dismiss()
    {
        if (closed) return;
        sheet.Close();
    }

    /// <summary>The popup gone (Close, Escape): its clock stopped, and the screens read again after a hand-in.</summary>
    private void Gone()
    {
        if (closed) return;
        closed = true;
        tick.Stop();
        if (instructions != null) instructions.IsOpen = false;
        over.SizeChanged -= WindowSized;
        Engine.PropertyChanged -= EngineChanged;
        Run.Stop();
        if (Run.Submitted) Engine.DataChanged();
    }

    /// <summary>Submit (Ctrl+Enter): always asked first, with what is still blank.</summary>
    public void AskSubmit()
    {
        if (Run.Now is not (QuizRun.Stage.Taking or QuizRun.Stage.Review)) return;
        var blank = Run.Questions.Count - Run.AnsweredCount;
        ask.Show(new QuizAskLayer.Ask
        {
            Title = "Submit this attempt?",
            Message = blank > 0
                ? $"{blank} {(blank == 1 ? "question is" : "questions are")} still blank. Submitting ends the attempt; blank questions are graded as incorrect."
                : "Submitting ends the attempt.",
            Go = "Submit",
            OnGo = () => _ = Run.Submit(),
            Cancel = "Cancel",
        });
    }

    private void AskMissing()
    {
        var one = Run.Missing.Count == 1;
        var which = string.Join(", ", Run.Missing.Select(n => $"question {n}"));
        ask.Show(new QuizAskLayer.Ask
        {
            Title = "Some answers were not kept",
            Message = $"Canvas did not keep your {(one ? "answer" : "answers")} to {which}. Go back to answer {(one ? "it" : "them")} again, or submit as it is.",
            Go = "Submit Anyway",
            Danger = true,
            OnGo = () => { Run.Missing = new(); _ = Run.Submit(force: true); },
            Cancel = "Go Back",
            OnCancel = () => Run.Missing = new(),
        });
    }

    /// <summary>Canvas wants the quiz's access code to save an answer: asked once, the saves that waited on it sent again.</summary>
    private void AskCode()
    {
        if (askingCode) return;
        askingCode = true;
        var box = new TextBox { PlaceholderText = "Access code", Text = Run.Code, ClearButtonEnabled = false, Tag = "askcode" };
        box.TextChanged += (_, _) => Run.Code = box.Text;
        ask.Show(new QuizAskLayer.Ask
        {
            Title = "Access Code",
            Message = "Canvas wants the quiz’s access code to save your answers. It stays with this attempt.",
            Extra = box,
            Focus = box,
            Go = "Save Answer",
            OnGo = () => { askingCode = false; Run.AskCode = false; _ = Run.CodeGiven(); },
            Cancel = "Cancel",
            OnCancel = () => { askingCode = false; Run.AskCode = false; },
        });
    }

    private void AskTimeUp() => ask.Show(new QuizAskLayer.Ask
    {
        Title = "Time is up",
        Message = "Canvas closes the attempt at the time limit. Check your answers and submit.",
        Go = "OK",
        OnGo = () => Run.TimeUp = false,
        OnCancel = () => Run.TimeUp = false,
    });

    private string QuizPath => $"/courses/{launch.Course}/quizzes/{launch.Quiz}";

    /// <summary>Canvas's own page for the quiz, in a window of its own — during an attempt, the attempt as it stands (every
    /// answer saved first; nothing restarts).</summary>
    public async void OpenCanvas()
    {
        var take = Run.Attempt?.TakeUrl ?? "";
        var url = InAttempt && take.Length > 0 ? take : QuizPath;
        var name = TitleNow;
        await Run.Flush();
        Dismiss();
        Engine.OpenWebScreen(url, name);
    }

    private void CopyLink()
    {
        if (Engine.Absolute(QuizPath) is { } u)
            try { Clipboard.SetText(u.ToString()); } catch { /* the clipboard busy: nothing copied */ }
    }

    // ---- the keyboard ----

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (ask.IsOpen) { ask.Press(e); return; }
        var mods = Keyboard.Modifiers;
        if (e.Key == Key.Escape)
        {
            if (instructions is { IsOpen: true }) { instructions.IsOpen = false; e.Handled = true; return; }
            if (holds > 0 || Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true } or ComboBoxItem) return; // (the menu's or the list's own)
            if (sheet.Modal) { Close(); e.Handled = true; } // (outside an attempt the popup closes as any does)
            return;
        }
        if (e.Key == Key.Enter && mods == ModifierKeys.Control)
        {
            if (Run.Now is QuizRun.Stage.Taking or QuizRun.Stage.Review) { AskSubmit(); e.Handled = true; }
            return;
        }
        pane?.Keys(e);
        if (e.Handled) return;
        if (e.Key == Key.Enter && mods == ModifierKeys.None && !QuizUi.Typing() && Keyboard.FocusedElement is not ButtonBase && pane?.Default is { } go)
        {
            go();
            e.Handled = true;
        }
    }
}

// ---- the intro ----

/// <summary>
/// A quiz before an attempt (the Mac's QuizIntroPane): what it is, its instructions, its rules (the attempts, the time
/// limit, one question at a time), the access code when it wants one, why it cannot begin, the attempts so far, and the
/// one thing to do next at the foot. On a wide popup the instructions are on the left and the rules, the code and the
/// attempts in a column beside them.
/// </summary>
internal sealed class QuizIntroPane : IQuizPane
{
    private readonly QuizSheet s;
    private readonly QuizIntro intro;
    private readonly bool wide;
    private TextBox? codeBox;
    private readonly Stack actions = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };

    public FrameworkElement View { get; }

    public QuizIntroPane(QuizSheet s, QuizIntro intro, bool wide)
    {
        this.s = s;
        this.intro = intro;
        this.wide = wide;
        var page = wide ? Columns() : OneColumn();
        var pad = new Border { Child = page, Padding = new Thickness(wide ? 48 : 36, 24, wide ? 48 : 36, 36) };
        Actions();
        View = K.Rows("*, auto", QuizUi.Scroll(pad).Row(0), QuizUi.Foot(actions).Row(1));
    }

    private QuizRun Run => s.Run;

    public void Update(QuizChange c) { }

    public void Keys(KeyEventArgs e) { }

    public Action? Default => Feedback && !intro.CanStart ? new Action(OpenLastFeedback) : new Action(StartAttempt);

    /// <summary>Shown: a quiz that wants a code has its field ready to type in.</summary>
    public void Arrived(bool hadFocus)
    {
        if (codeBox == null) { s.FocusKeys(); return; }
        var box = codeBox;
        if (!hadFocus && !(intro.NeedsCode && Run.Code.Length == 0)) { s.FocusKeys(); return; }
        box.Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(hadFocus ? 0 : 120);
            if (!box.IsVisible) return;
            box.Focus();
            Keyboard.Focus(box);
            box.CaretIndex = box.Text.Length;
        }, DispatcherPriority.Input);
    }

    /// <summary>A wide popup: the quiz and its instructions, and beside them what it asks of the student.</summary>
    private FrameworkElement Columns()
    {
        var left = K.V(30, Heading(), Notices(), Instructions());
        left.MaxWidth = 680;
        left.HorizontalAlignment = HorizontalAlignment.Left;
        var right = K.V(30, Code(), Rules(), Attempts());
        right.Margin = new Thickness(0, 8, 0, 0);
        var g = K.Columns("*, 52, 300", left.Col(0), right.Col(2));
        g.MaxWidth = 1032;
        return g;
    }

    private FrameworkElement OneColumn()
    {
        var v = K.V(30, Heading(), Notices(), Code(), Rules(), Instructions(), Attempts());
        v.MaxWidth = 700;
        return v;
    }

    private FrameworkElement Heading()
    {
        var ink = QuizUi.Ink(s.Tint);
        var ctx = K.Text(intro.Context ?? "Quiz", Ty.BodyStrong);
        ctx.Foreground = ink;
        var label = K.H(8, K.Icon(SymbolRegular.TaskListLtr24, 16, K.Darkened(s.Tint)), ctx);
        var title = K.Wrapped(intro.Title, Ty.Title);
        title.TextTrimming = TextTrimming.None;
        var v = K.V(10, label, title);
        if (intro.Facts.Count > 0) v.Children.Add(K.Wrapped(string.Join(" · ", intro.Facts), Ty.Body, B.Text2));
        if (!string.IsNullOrEmpty(intro.Note)) v.Children.Add(K.Wrapped(intro.Note!, Ty.Caption, B.Text2));
        return v;
    }

    /// <summary>Why it cannot begin (Windows' InfoBar, red), and when it unlocks.</summary>
    private FrameworkElement? Notices()
    {
        var why = Run.Refused ?? "";
        var lockText = intro.LockText ?? "";
        if (why.Length == 0 && lockText.Length == 0) return null;
        var v = K.V(10);
        if (why.Length > 0) v.Children.Add(new InfoBar { Title = "", Message = why, Severity = InfoBarSeverity.Error, IsOpen = true, IsClosable = false });
        if (lockText.Length > 0)
            v.Children.Add(K.Columns("auto, *", K.Icon(SymbolRegular.LockClosed24, 16, B.Text2, filled: true).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Margin(0, 2, 10, 0).Col(0), K.Wrapped(lockText, Ty.Body, B.Text2).Col(1)));
        return v;
    }

    private FrameworkElement? Instructions() =>
        intro.Html.Length == 0 ? null : QuizUi.Section("Before You Start", QuizUi.Rich(s.Engine, intro.Html, 15));

    private FrameworkElement? Rules()
    {
        if (intro.Rules.Count == 0) return null;
        var v = K.V(12);
        foreach (var r in intro.Rules)
        {
            var icon = QuizUi.RuleIcon(r);
            icon.Width = 22;
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 1, 12, 0);
            v.Children.Add(K.Columns("auto, *", icon.Col(0), K.Wrapped(r.Text, Ty.Body).Col(1)));
        }
        return QuizUi.Section("Rules", v);
    }

    private FrameworkElement? Code()
    {
        if (!intro.NeedsCode) return null;
        var box = new TextBox { PlaceholderText = "Access code", Text = Run.Code, ClearButtonEnabled = false, Tag = "code" };
        System.Windows.Automation.AutomationProperties.SetName(box, "Access code");
        box.TextChanged += (_, _) => { Run.Code = box.Text; Actions(); };
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { StartAttempt(); e.Handled = true; } };
        codeBox = box;
        var field = K.Columns("*, auto", box.Col(0));
        field.ColumnDefinitions[0].MaxWidth = 320;
        return QuizUi.Section("Access Code", K.V(8, field, K.Wrapped("Your instructor gives the code. It stays with this attempt.", Ty.Caption, B.Text2)));
    }

    /// <summary>Every attempt, newest first, with its score and when it was handed in, the kept one marked (the Mac's
    /// 1.3.32); else the last one.</summary>
    private FrameworkElement? Attempts()
    {
        if (intro.Attempts is { Count: > 0 } list)
        {
            var rows = K.V(0);
            for (var k = 0; k < list.Count; k++)
            {
                if (k > 0) rows.Children.Add(K.Divider());
                rows.Children.Add(AttemptRow(list[k]));
            }
            var v = K.V(8, K.Card(rows, new Thickness(14, 2, 14, 2)));
            if (!string.IsNullOrEmpty(intro.KeptNote)) v.Children.Add(K.Wrapped(intro.KeptNote!, Ty.Caption, B.Text2));
            return QuizUi.Section(list.Count == 1 ? "Your Attempt" : "Your Attempts", v);
        }
        if (intro.Last is { } last)
        {
            var g = K.Columns("*, auto", K.Text($"Attempt {last.Attempt}", Ty.BodyStrong).Col(0));
            if (last.Score is { } sc) g.Children.Add(K.Text(sc, Ty.Body, B.Text2).Digits().Col(1));
            else if (!string.IsNullOrEmpty(last.Why)) g.Children.Add(K.Wrapped(last.Why!, Ty.Caption, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
            g.Margin = new Thickness(0, 12, 0, 12);
            return QuizUi.Section("Your Last Attempt", K.Card(g, new Thickness(14, 0, 14, 0)));
        }
        return null;
    }

    private static FrameworkElement AttemptRow(QuizIntro.PastAttempt a)
    {
        var name = K.H(6, K.Text($"Attempt {a.Attempt}", Ty.BodyStrong));
        if (a.Kept == true) name.Children.Add(K.Chip("Kept", K.Tone("success"), strong: true));
        var left = K.V(2, name);
        if (!string.IsNullOrEmpty(a.When)) left.Children.Add(K.Wrapped(a.When!, Ty.Caption, B.Text2));
        var g = K.Columns("*, auto", left.Col(0));
        if (a.Score is { } sc) g.Children.Add(K.Text(sc, Ty.BodyStrong, a.Kept == true ? B.Text : B.Text2).Digits().Align(HorizontalAlignment.Right, VerticalAlignment.Center).Margin(12, 0, 0, 0).Col(1));
        else if (!string.IsNullOrEmpty(a.Why))
        {
            var why = K.Wrapped(a.Why!, Ty.Caption, B.Text2);
            why.TextAlignment = TextAlignment.Right;
            g.Children.Add(why.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Margin(12, 0, 0, 0).Col(1));
        }
        g.Margin = new Thickness(0, 10, 0, 10);
        System.Windows.Automation.AutomationProperties.SetName(g, $"Attempt {a.Attempt}{(a.Kept == true ? ", kept" : "")}, {a.Score ?? a.Why ?? ""}");
        return g;
    }

    // ---- the foot: the one thing to do next ----

    private bool Feedback => intro.Last?.Feedback == true && !intro.Begin.StartsWith("Continue", StringComparison.Ordinal);
    private bool CodeMissing => intro.NeedsCode && Run.Code.Trim().Length == 0;
    private string TakeTitle => CodeMissing ? "Enter the Code" : intro.Survey ? "Take Survey" : "Take Quiz";
    private string BeginTitle => CodeMissing && intro.CanStart ? "Enter the Access Code" : intro.Begin;

    /// <summary>An attempt under way, Resume; a finished one with its feedback and another attempt left, Take Quiz and See
    /// Feedback side by side; with none left, See Feedback alone. Enter presses the accent one.</summary>
    private void Actions()
    {
        actions.Children.Clear();
        if (Feedback && intro.CanStart)
        {
            actions.Children.Add(K.Button("See Feedback", OpenLastFeedback));
            var take = K.Button(TakeTitle, StartAttempt, accent: true);
            take.IsEnabled = !CodeMissing;
            actions.Children.Add(take);
        }
        else if (Feedback)
        {
            actions.Children.Add(K.Button("See Feedback", OpenLastFeedback, accent: true, icon: SymbolRegular.Comment24));
        }
        else
        {
            var begin = K.Button(BeginTitle, StartAttempt, accent: true);
            begin.IsEnabled = intro.CanStart && !CodeMissing;
            actions.Children.Add(begin);
        }
    }

    private void StartAttempt()
    {
        if (!intro.CanStart || CodeMissing) return;
        if (Run.Now != QuizRun.Stage.Intro) return; // (Enter in the code field and the default button both ask: one begins)
        _ = Run.Begin();
    }

    private void OpenLastFeedback()
    {
        if (intro.Last is not { } last) return;
        _ = Run.OpenFeedback(last.Attempt);
    }
}
