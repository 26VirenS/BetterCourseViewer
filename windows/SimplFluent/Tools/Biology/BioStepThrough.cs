using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// The step-through every biology process uses (spec §3.6, §9), the Mac's BioStepThrough.swift: transcription and
// translation, replication, mitosis and meiosis, the energy pathways. The controller (BioStepModel.cs) holds the steps,
// where it is and whether it is playing; the controls sit centred under the scene in a capsule — ⏮ ◀ ▶/⏸ ▶ ⏭, a dot per
// step, "Step 4 of 12 · Elongation" and the speed — with the step's explanation above them. Keys (when the scene or the
// controls have focus): ← →, Space, Home, End, and 1–9 for a tool's stage groups. Between steps a tool draws
// controller.Phase() frame by frame (BioStepAnimator: 600 ms, eased; back plays in reverse); with Windows' animations
// off it crossfades in 200 ms instead. Each change is said by Narrator.

/// <summary>The step-through's controls (spec §9): the step's explanation, then a capsule with the buttons, the dots, the
/// caption and the speed. Number keys jump to <c>groups</c> (1 = its first name …) when the tool has them.</summary>
public sealed class BioStepControls : ContentControl
{
    private readonly BioStepController c;
    private readonly IReadOnlyDictionary<int, string> groups;
    private readonly TextBlock explanation = K.Wrapped("", Ty.Body, B.Text, maxLines: 3);
    private readonly ContentControl capsuleHost = new() { Focusable = false, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly DispatcherTimer player = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int shownIndex = -1;

    public BioStepControls(BioStepController controller, IReadOnlyDictionary<int, string>? groups = null)
    {
        c = controller;
        this.groups = groups ?? new Dictionary<int, string>();
        Focusable = true;
        IsTabStop = true;
        FocusVisualStyle = null;
        explanation.FontSize = 15;
        explanation.LineHeight = 21;
        explanation.MaxHeight = 21 * 3 + 1;
        explanation.MaxWidth = 640;
        explanation.TextAlignment = TextAlignment.Center;
        explanation.HorizontalAlignment = HorizontalAlignment.Center;
        Content = K.V(10, explanation, capsuleHost);
        player.Tick += (_, _) => c.Tick();
        BioStepKeys.Attach(this, c);
        // (the capsule made again for a new width: its caption fitted to it)
        SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) capsuleHost.Content = Capsule(); };
        Draw();
        Loaded += (_, _) =>
        {
            c.ReduceMotion = !Motion.On;
            c.Changed -= Changed;
            c.Changed += Changed;
            c.Announce -= Say;
            c.Announce += Say;
            Draw();
        };
        Unloaded += (_, _) =>
        {
            c.Changed -= Changed;
            c.Announce -= Say;
            player.Stop();
        };
    }

    private void Changed()
    {
        Shot.Moved();
        Draw();
    }

    private void Draw()
    {
        if (c.Playing && !player.IsEnabled) player.Start();
        if (!c.Playing && player.IsEnabled) player.Stop();
        var text = c.Current?.Text ?? "";
        if (shownIndex != c.Index)
        {
            shownIndex = c.Index;
            explanation.Text = text;
            if (Motion.On) Motion.FadeIn(explanation, 200);
        }
        capsuleHost.Content = Capsule();
    }

    private FrameworkElement Capsule()
    {
        Button Key(SymbolRegular s, string tip, bool enabled, Action a)
        {
            var b = K.IconButton(s, a, tip, 16, 30);
            b.IsEnabled = enabled;
            b.Opacity = enabled ? 1 : 0.35;
            return b;
        }
        var row = K.H(6,
            Key(SymbolRegular.Previous20, "First step (Home)", !c.AtStart, c.First),
            Key(SymbolRegular.ChevronLeft20, "Back (←)", !c.AtStart, c.Back),
            Key(c.Playing ? SymbolRegular.Pause20 : SymbolRegular.Play20, c.Playing ? "Pause (Space)" : "Play (Space)", c.Steps.Count > 1, c.Toggle),
            Key(SymbolRegular.ChevronRight20, "Next (→)", !c.AtEnd, c.Next),
            Key(SymbolRegular.Next20, "Last step (End)", !c.AtEnd, c.Last));
        var divider = new Border { Width = 1, Height = 18, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        divider.Bg(B.Divider);
        row.Children.Add(divider);
        row.Children.Add(Dots());
        var words = BioStepTiming.Caption(c.Index, c.Steps);
        var caption = K.Text(words, Ty.BodyStrong);
        caption.MinWidth = 120;
        caption.VerticalAlignment = VerticalAlignment.Center;
        caption.Margin = new Thickness(6, 0, 4, 0);
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.ToolTip = words;
        caption.FontFeatures();
        row.Children.Add(caption);
        var speed = Speed();
        row.Children.Add(speed);
        row.VerticalAlignment = VerticalAlignment.Center;
        // (the caption gives way when the capsule would be wider than the controls — a long step title under a narrow
        //  scene — so the speed stays in view; the whole caption is in its tooltip)
        if (ActualWidth > 0)
        {
            var others = 0.0;
            foreach (UIElement e in row.Children)
            {
                if (ReferenceEquals(e, caption)) continue;
                e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                others += e.DesiredSize.Width;
            }
            // (the row's spacing, the capsule's padding and edge, the caption's margins)
            others += 6 * row.Children.Count + 24 + 10;
            caption.MaxWidth = Math.Max(caption.MinWidth, ActualWidth - others);
        }
        var capsule = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(10, 6, 12, 6), Child = row, BorderThickness = new Thickness(1) };
        capsule.Bg(B.Card).Stroke(B.CardStroke);
        capsule.ToolTip = groups.Count == 0
            ? "← → step · Space plays · Home and End"
            : "← → step · Space plays · " + string.Join(" · ", groups.OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Value}"));
        return capsule;
    }

    /// <summary>A dot per step (a press goes there); at most 24 drawn, else a thin bar of progress.</summary>
    private FrameworkElement Dots()
    {
        var n = c.Steps.Count;
        if (n > 24)
        {
            var bar = K.Bar((c.Index + 1.0) / Math.Max(n, 1), Appearance.Accent);
            bar.Width = 120;
            return bar;
        }
        var row = K.H(1);
        row.VerticalAlignment = VerticalAlignment.Center;
        for (var i = 0; i < n; i++)
        {
            var at = i;
            var dot = new Ellipse { Width = 7, Height = 7 };
            if (i == c.Index) dot.Fill = K.Brush(Appearance.Accent);
            else { dot.SetResourceReference(Shape.FillProperty, B.Text); dot.Opacity = i < c.Index ? 0.45 : 0.18; }
            var b = K.Subtle(dot, () => { c.Pause(); c.Go(at); }, new Thickness(3), 6);
            b.MinWidth = 0;
            b.MinHeight = 0;
            b.ToolTip = c.Steps[i].Title;
            AutomationProperties.SetName(b, $"Step {i + 1}: {c.Steps[i].Title}");
            row.Children.Add(b);
        }
        return row;
    }

    private FrameworkElement Speed()
    {
        var face = K.Text(BioStepTiming.SpeedText(c.Speed), Ty.BodyStrong);
        face.FontFeatures();
        Button? b = null;
        b = K.Subtle(face, () =>
        {
            var m = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Top };
            foreach (var s in BioStepTiming.Speeds)
            {
                var speed = s;
                var item = new MenuItem { Header = BioStepTiming.SpeedText(s), IsChecked = s == c.Speed };
                item.Click += (_, _) => c.Speed = speed;
                m.Items.Add(item);
            }
            m.IsOpen = true;
        }, new Thickness(8, 4, 8, 4), 6);
        b.ToolTip = "Speed";
        AutomationProperties.SetName(b, $"Speed {BioStepTiming.SpeedText(c.Speed)}");
        return b;
    }

    /// <summary>The step's title and explanation, said by Narrator.</summary>
    private void Say(string text)
    {
        var peer = UIElementAutomationPeer.FromElement(this) ?? UIElementAutomationPeer.CreatePeerForElement(this);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, "simpl-bio-step");
    }
}

/// <summary>The step-through's keys on an element that can take focus (the scene, or the controls): ← → back and next,
/// Space play and pause, Home and End, 1–9 the stage groups.</summary>
public static class BioStepKeys
{
    public static void Attach(UIElement target, BioStepController c)
    {
        target.Focusable = true;
        target.KeyDown += (_, e) =>
        {
            if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
            switch (e.Key)
            {
                case Key.Left: c.Back(); break;
                case Key.Right: c.Next(); break;
                case Key.Space: c.Toggle(); break;
                case Key.Home: c.First(); break;
                case Key.End: c.Last(); break;
                case >= Key.D1 and <= Key.D9 when c.JumpToGroup(e.Key - Key.D0): break;
                case >= Key.NumPad1 and <= Key.NumPad9 when c.JumpToGroup(e.Key - Key.NumPad0): break;
                default: return;
            }
            e.Handled = true;
        };
    }
}

/// <summary>
/// A tool's drawing between steps (the Mac's TimelineView over controller.phase): <c>draw</c> is called with the phase
/// every frame while a change is drawn, and once more at rest — frame by frame only while it moves. Attach it while the
/// view is up: <c>Loaded += (_, _) =&gt; animator.Start(); Unloaded += (_, _) =&gt; animator.Stop();</c>.
/// </summary>
public sealed class BioStepAnimator
{
    private readonly BioStepController c;
    private readonly Action<BioStepPhase> draw;
    private bool running, started;

    public BioStepAnimator(BioStepController controller, Action<BioStepPhase> draw)
    {
        c = controller;
        this.draw = draw;
    }

    public void Start()
    {
        if (started) return;
        started = true;
        c.Changed += Changed;
        draw(c.Phase());
    }

    public void Stop()
    {
        if (!started) return;
        started = false;
        c.Changed -= Changed;
        Run(false);
    }

    private void Changed()
    {
        draw(c.Phase());
        Run(c.IsMoving());
    }

    private void Run(bool on)
    {
        if (on == running) return;
        running = on;
        if (on) CompositionTarget.Rendering += Frame;
        else CompositionTarget.Rendering -= Frame;
    }

    private void Frame(object? sender, EventArgs e)
    {
        var moving = c.IsMoving();
        draw(c.Phase());
        if (!moving) Run(false);
    }
}
