using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// Focus timer (the Mac's FocusTimerTool): the dial — what is left of the phase, the time large in its colour, when it
/// ends — Start and Pause, Cancel and Skip; beside it on a wide window, each phase's minutes and the sessions towards a
/// long break.
/// </summary>
internal sealed class FocusTimerTool
{
    private readonly FocusTimer timer = FocusTimer.Shared;
    private readonly SelectorBar picker;
    private readonly FocusDial dial = new(360);
    private readonly FocusControls controls = new(large: true);
    private readonly ContentControl endedHost = new() { Focusable = false, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly FocusSettings settings = new();
    private string? endedShown;

    public FrameworkElement View { get; }

    public FocusTimerTool(ToolContext c)
    {
        picker = FocusParts.PhasePicker(timer);
        picker.HorizontalAlignment = HorizontalAlignment.Center;
        var foot = K.Wrapped("It keeps going while you use the rest of the app, and Windows tells you when a phase ends.", Ty.Caption, B.Text2).Center();
        dial.View.HorizontalAlignment = HorizontalAlignment.Center;
        controls.View.HorizontalAlignment = HorizontalAlignment.Center;
        var column = K.V(24, picker, dial.View, controls.View, endedHost, foot);
        column.Margin = new Thickness(0, 12, 0, 12);
        var page = Everyday.Page(ToolKit.Columns(column, settings.View, 380, 940));
        View = page;
        Update();
        Everyday.Follow(page, () => { timer.Changed += Update; Update(); }, () => timer.Changed -= Update);
        Everyday.Every(page, TimeSpan.FromSeconds(1), dial.Update);
    }

    private void Update()
    {
        picker.Select(Array.IndexOf(FocusTimer.Phases, timer.Current), false);
        dial.Update();
        controls.Update();
        settings.Update();
        // what just finished, said once (in the colour of the phase now going)
        var ended = timer.Ended;
        if (ended == endedShown) return;
        endedShown = ended;
        if (ended == null) { endedHost.Content = null; return; }
        var color = FocusTimer.ColorOf(timer.Current);
        var words = ended == "focus" ? $"Focus session done. {FocusTimer.Name(timer.Current)} started." : "Break over. Press Start when you are back.";
        var line = K.Text(words, Ty.BodyStrong);
        line.Foreground = new SolidColorBrush(color);
        var label = K.H(8, K.Icon(ended == "focus" ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.DrinkCoffee24, 16, color, filled: true), line);
        endedHost.Content = label;
        Motion.FadeIn(label, Motion.Medium);
    }
}

/// <summary>
/// The timer's pin, opened (the Mac's FocusTimerCompact): the phase, the time large, its controls, and the minutes when
/// nothing is going.
/// </summary>
internal sealed class FocusTimerCompact
{
    private readonly FocusTimer timer = FocusTimer.Shared;
    private readonly SelectorBar picker;
    private readonly TextBlock clock = Everyday.Figures("", 46, FontWeights.SemiBold);
    private readonly TextBlock ends = K.Text("", Ty.Body, B.Text2);
    private readonly FocusBar bar = new();
    private readonly FocusControls controls = new(large: false);
    private readonly TextBlock minutesName = K.Text("", Ty.Body);
    private readonly NumberBox minutes = FocusParts.MinutesBox();
    private readonly FrameworkElement minutesRow;
    private readonly TextBlock foot = K.Wrapped("", Ty.Caption, B.Text2);
    private bool syncing;

    public FrameworkElement View { get; }

    public FocusTimerCompact(ToolContext c)
    {
        picker = FocusParts.PhasePicker(timer);
        picker.HorizontalAlignment = HorizontalAlignment.Left;
        picker.Margin = new Thickness(-12, 0, 0, 0); // (its first word in line with the rest)
        ends.VerticalAlignment = VerticalAlignment.Bottom;
        ends.Margin = new Thickness(8, 0, 0, 10);
        var clockRow = K.Columns("*, auto", clock.Col(0), ends.Col(1));
        minutes.ValueChanged += (_, _) =>
        {
            if (!syncing && minutes.Value is { } v) timer.SetMinutes((int)Math.Round(v), timer.Current);
        };
        minutesRow = K.Columns("auto, auto, auto", minutesName.Margin(0, 0, 10, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0),
            minutes.Col(1), K.Text("minutes", Ty.Body).Margin(10, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(2));
        ((Grid)minutesRow).HorizontalAlignment = HorizontalAlignment.Left;
        controls.View.HorizontalAlignment = HorizontalAlignment.Left;
        var v = K.V(14, picker, K.V(8, clockRow, bar), controls.View, minutesRow, foot);
        View = v;
        Update();
        Everyday.Follow(v, () => { timer.Changed += Update; Update(); }, () => timer.Changed -= Update);
        Everyday.Every(v, TimeSpan.FromSeconds(1), Tick);
    }

    private void Tick()
    {
        var color = FocusTimer.ColorOf(timer.Current);
        clock.Text = FocusTimer.Clock(timer.RemainingSeconds());
        if (timer.Active) clock.Foreground = new SolidColorBrush(color); else clock.Fg(B.Text);
        ends.Text = timer.EndsText ?? (timer.Paused ? "Paused" : "");
        bar.Update(timer.Fraction, color);
        AutomationProperties.SetName(clock, $"{timer.PhaseName}, {clock.Text} left{(timer.Paused ? ", paused" : "")}");
    }

    private void Update()
    {
        picker.Select(Array.IndexOf(FocusTimer.Phases, timer.Current), false);
        Tick();
        controls.Update();
        minutesRow.Visibility = timer.Active ? Visibility.Collapsed : Visibility.Visible;
        minutesName.Text = FocusTimer.Name(timer.Current) + ":";
        AutomationProperties.SetName(minutes, $"{FocusTimer.Name(timer.Current)} minutes");
        syncing = true;
        if (minutes.Value is not { } m || (int)Math.Round(m) != timer.Minutes(timer.Current)) minutes.Value = timer.Minutes(timer.Current);
        syncing = false;
        foot.Text = $"{ToolKit.Plural(timer.DoneToday, "session")} today · {timer.CycleLine}";
    }
}

/// <summary>The pieces the timer's two views share.</summary>
internal static class FocusParts
{
    /// <summary>Focus, Short, Long: the phase, picked (a session going is stopped by a change of phase, as on the web).</summary>
    public static SelectorBar PhasePicker(FocusTimer timer)
    {
        var bar = new SelectorBar(FocusTimer.Phases.Select(FocusTimer.ShortName).ToList(), Array.IndexOf(FocusTimer.Phases, timer.Current));
        bar.Changed += i => timer.SetPhase(FocusTimer.Phases[i]);
        AutomationProperties.SetName(bar, "Phase");
        return bar;
    }

    /// <summary>A phase's minutes, 1 to 90, one at a time (the Mac's Stepper).</summary>
    public static NumberBox MinutesBox() => new()
    {
        Minimum = 1, Maximum = 90, SmallChange = 1, LargeChange = 5, MaxDecimalPlaces = 0, Width = 124,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, ClearButtonEnabled = false,
    };

    /// <summary>A button filled with a phase's colour (the Mac's prominent glass button, tinted).</summary>
    public static void Tint(Wpf.Ui.Controls.Button b, Color c)
    {
        b.Appearance = ControlAppearance.Secondary;
        b.Background = new SolidColorBrush(c);
        b.MouseOverBackground = new SolidColorBrush(K.WithAlpha(c, 0.9));
        b.PressedBackground = new SolidColorBrush(K.WithAlpha(c, 0.8));
        b.Foreground = Brushes.White;
        b.PressedForeground = new SolidColorBrush(Color.FromArgb(0xd0, 0xff, 0xff, 0xff));
        foreach (var dp in new[] { Control.BorderBrushProperty, Wpf.Ui.Controls.Button.MouseOverBorderBrushProperty, Wpf.Ui.Controls.Button.PressedBorderBrushProperty })
            b.SetResourceReference(dp, "AccentControlElevationBorderBrush");
    }
}

/// <summary>The dial (the Mac's FocusDial): a ring of what is left, the time in its middle, the phase over it and when it ends under it.</summary>
internal sealed class FocusDial
{
    private readonly FocusTimer timer = FocusTimer.Shared;
    private readonly double size, line;
    private readonly Ellipse track;
    private readonly Path arc;
    private readonly TextBlock phase, clock, ends;

    public Grid View { get; }

    public FocusDial(double size)
    {
        this.size = size;
        line = Math.Max(10, size * 0.045);
        track = new Ellipse { StrokeThickness = line };
        arc = new Path { StrokeThickness = line, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        phase = new TextBlock { FontSize = size * 0.045, FontWeight = FontWeights.SemiBold, FontFamily = K.TextFace, HorizontalAlignment = HorizontalAlignment.Center };
        clock = Everyday.Figures("", size * 0.22, FontWeights.SemiBold);
        ends = new TextBlock { FontSize = size * 0.045, FontFamily = K.TextFace, HorizontalAlignment = HorizontalAlignment.Center };
        ends.Fg(B.Text2);
        var words = K.V(6, phase, Everyday.Shrinks(clock, HorizontalAlignment.Center), ends);
        words.VerticalAlignment = VerticalAlignment.Center;
        words.Margin = new Thickness(line * 2);
        View = new Grid { Width = size, Height = size, Children = { track, arc, words } };
        Update();
    }

    public void Update()
    {
        var color = FocusTimer.ColorOf(timer.Current);
        track.Stroke = new SolidColorBrush(K.WithAlpha(color, 0.16));
        arc.Stroke = new SolidColorBrush(color);
        var f = timer.Fraction;
        arc.Data = f > 0 ? K.ArcGeometry(size, line, f) : null;
        phase.Text = timer.PhaseName.ToUpperInvariant();
        phase.Foreground = new SolidColorBrush(color);
        var left = timer.RemainingSeconds();
        clock.Text = FocusTimer.Clock(left);
        ends.Text = timer.EndsText ?? (timer.Paused ? "Paused" : $"{timer.Minutes(timer.Current)} minutes");
        AutomationProperties.SetName(View, $"{timer.PhaseName}, {FocusTimer.Clock(left)} left{(timer.Paused ? ", paused" : "")}");
    }
}

/// <summary>Start (Pause, Resume), Cancel and Skip (the Mac's FocusControls).</summary>
internal sealed class FocusControls
{
    private readonly FocusTimer timer = FocusTimer.Shared;
    private readonly Wpf.Ui.Controls.Button start, cancel, skip;

    public Stack View { get; }

    public FocusControls(bool large)
    {
        start = K.Button("Start Timer", () => timer.Toggle(), icon: SymbolRegular.Play24);
        cancel = K.Button("Cancel", () => timer.Reset());
        skip = K.Button("Skip", () => timer.Skip(), icon: SymbolRegular.Next24);
        start.FontWeight = FontWeights.SemiBold;
        start.MinWidth = large ? 150 : 96;
        foreach (var b in new[] { start, cancel, skip })
        {
            if (large) { b.MinHeight = 40; b.Padding = new Thickness(18, 8, 18, 9); b.FontSize = 15; }
            else if (b != start) b.MinWidth = 0; // (three to a row in the pin's flyout)
        }
        View = K.H(large ? 12 : 8, start, cancel, skip);
        Update();
    }

    public void Update()
    {
        var running = timer.Running;
        start.Content = running ? "Pause" : timer.Paused ? "Resume" : "Start Timer";
        start.Icon = new SymbolIcon { Symbol = running ? SymbolRegular.Pause24 : SymbolRegular.Play24, Filled = true, Foreground = Brushes.White };
        FocusParts.Tint(start, FocusTimer.ColorOf(timer.Current));
        var show = timer.Active ? Visibility.Visible : Visibility.Collapsed;
        if (cancel.Visibility != show)
        {
            cancel.Visibility = show;
            if (show == Visibility.Visible) Motion.FadeIn(cancel, Motion.Normal);
        }
        skip.ToolTip = $"Skip to {FocusTimer.Name(timer.NextPhase).ToLowerInvariant()}";
    }
}

/// <summary>The side panel (the Mac's FocusSettings): each phase's minutes, and the sessions towards a long break.</summary>
internal sealed class FocusSettings
{
    private readonly FocusTimer timer = FocusTimer.Shared;
    private readonly Dictionary<FocusTimer.Phase, NumberBox> boxes = new();
    private readonly ContentControl today = new() { Focusable = false };
    private bool syncing;

    public Stack View { get; }

    public FocusSettings()
    {
        var rows = K.V(0);
        foreach (var p in FocusTimer.Phases)
        {
            if (rows.Children.Count > 0) rows.Children.Add(K.Divider(16));
            var box = FocusParts.MinutesBox();
            var phase = p;
            box.ValueChanged += (_, _) =>
            {
                if (!syncing && box.Value is { } v) timer.SetMinutes((int)Math.Round(v), phase);
            };
            AutomationProperties.SetName(box, $"{FocusTimer.Name(p)} minutes");
            boxes[p] = box;
            var row = K.Columns("auto, *, auto, auto",
                K.Dot(FocusTimer.ColorOf(p), 10).Margin(0, 0, 12, 0).Col(0),
                K.Text(FocusTimer.Name(p), Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1),
                box.Margin(8, 0, 0, 0).Col(2),
                K.Text("min", Ty.Body, B.Text2).Margin(8, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(3));
            row.Margin = new Thickness(16, 8, 16, 8);
            rows.Children.Add(row);
        }
        View = K.V(26, Everyday.Section("Minutes", K.Card(rows, new Thickness(0, 4, 0, 4))), today);
        Update();
    }

    public void Update()
    {
        syncing = true;
        foreach (var (p, box) in boxes)
            if (box.Value is not { } v || (int)Math.Round(v) != timer.Minutes(p)) box.Value = timer.Minutes(p);
        syncing = false;

        var dots = K.H(8);
        for (var i = 0; i < 4; i++)
        {
            var dot = new Ellipse { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
            if (i < timer.Cycle || timer.Current == FocusTimer.Phase.Long) dot.Fill = new SolidColorBrush(FocusTimer.ColorOf(FocusTimer.Phase.Focus));
            else dot.SetResourceReference(Shape.FillProperty, "ControlStrongFillColorDisabledBrush");
            dots.Children.Add(dot);
        }
        dots.Children.Add(K.Text(timer.CycleLine, Ty.Body, B.Text2).Margin(6, 0, 0, 0));
        AutomationProperties.SetName(dots, timer.CycleLine);
        today.Content = Everyday.Section("Today", K.V(12, dots, K.Wrapped("After every fourth focus session the break is a long one.", Ty.Caption, B.Text2)),
            ToolKit.Plural(timer.DoneToday, "session"));
    }
}

/// <summary>Windows 11's progress bar in a phase's colour, moved as the time goes: a thin track, the rounded bar over it.</summary>
internal sealed class FocusBar : Grid
{
    private readonly Border fill = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
    private double fraction;

    public FocusBar()
    {
        Height = 4;
        var track = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center }.Bg(B.StrongStroke);
        Children.Add(track);
        Children.Add(fill);
        SizeChanged += (_, _) => Lay();
    }

    public void Update(double f, Color color)
    {
        fraction = Math.Clamp(f, 0, 1);
        fill.Background = new SolidColorBrush(color);
        Lay();
    }

    private void Lay() => fill.Width = Math.Max(fraction > 0 ? 4 : 0, ActualWidth * fraction);
}
