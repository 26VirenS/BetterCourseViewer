using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// The rubric over the window (the Mac's RubricPopup): one at a time, put up by an assignment's page — its rubric row or
/// the grade card's rubric button — and taken down with that page. A popup floating over the window (nothing dimmed), as
/// large as the window has room for, at most 1180 by 880, and sized by its edges.
/// </summary>
internal static class RubricPopup
{
    private static Sheet? sheet;

    /// <summary>The rubric shown (one at a time): on the ring (or, for the screenshot suite, the grid, or opened on a
    /// criterion). It stays while another screen is opened under it, until it is closed.</summary>
    public static void Show(AssignmentData d, int? open = null, string? view = null)
    {
        if (d.Rubric.Count == 0) return;
        sheet?.Close();
        var win = Application.Current?.MainWindow;
        var w = win is { ActualWidth: > 0 } ? win.ActualWidth : 1280;
        var h = win is { ActualHeight: > 0 } ? win.ActualHeight : 820;
        var v = new RubricView(d, open, view);
        // (floating over the window: the assignment and the rest of Simpl at hand beside it; Escape is its own. A large
        //  popup — the window under its title bar less a margin, over the pane too, at most 1180 by 880 — sized by its
        //  edges, the size kept for the next rubric)
        var s = new Sheet
        {
            Bare = true, Modal = true, Body = v, Width = Math.Clamp(w - 64, 360, 1180), Height = Math.Clamp(h - 96, 360, 880), Floats = true, Large = true,
            Most = new Size(1180, 880), SizeKey = "rubric", OwnClose = true, Name = "Rubric",
        };
        v.Close = () => s.Close();
        v.Loaded += (_, _) =>
        {
            // (the dialog as wide as the window has room for, past a dialog's usual most)
            if (s.Card is { } card && s.Host is { ActualWidth: > 0 } host) card.MaxWidth = Math.Max(280, host.ActualWidth - 48);
        };
        s.Closed += () =>
        {
            v.Stop();
            if (sheet == s) sheet = null;
        };
        sheet = s;
        Dialogs.Show(s, win);
    }

}

/// <summary>
/// The rubric as a ring (the Mac's RubricPopupView over RubricStage.swift): one slice per criterion, as long as its share
/// of the points, blooming in and — marked — bending to its marks and turning to its grades' colours; the total in the
/// middle, or the criterion under the pointer. A slice, its name (a chip beside it), or Enter opens that criterion into
/// its bar: its levels beside it at the height of their points (a leader to one that had to move), the one given marked, the marker's
/// note under them, ‹ 1 of 2 › to switch, and "Ring" beside × to go back (the little ring on top only pictures where the bar came from). ← → walk the
/// criteria; Escape steps back, then closes; a press beside the ring closes it too. Ring or Grid at the top right
/// (RubricGrid.cs).
/// </summary>
internal sealed class RubricView : Grid
{
    private readonly AssignmentData data;
    private readonly RubricModel model;
    private readonly RubricGeometry geo;
    private readonly RubricClock clock;
    private readonly string eyebrow;
    public Action? Close;

    private string view;
    private int sel, hov = -1, ringHit = -1, paintedHov = -2;
    private bool open, stopped;

    private readonly Grid stageHost = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private readonly Viewbox stageBox = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
    private readonly Canvas ringLayer = new() { Width = RubricRing.Width, Height = RubricRing.Height, Background = Brushes.Transparent };
    private readonly Canvas barLayer = new() { Width = RubricRing.Width, Height = RubricRing.Height, Background = Brushes.Transparent, Visibility = Visibility.Hidden };
    private readonly RubricRingVisual ringVisual;
    private readonly RubricPinPanel labels = new();
    private readonly List<(Button B, StackPanel Words, TextBlock Name, TextBlock Pts)> labelParts = new();
    private readonly RubricPinPanel centreHost = new();
    private readonly Border centre = new() { Width = 220 };
    private readonly RubricRingBack back;
    /// <summary>The way back from a criterion to the ring, in words, beside ×.</summary>
    private readonly Wpf.Ui.Controls.Button backButton;
    private readonly FrameworkElement header;
    private readonly TextBlock foot;
    private FrameworkElement? gridPanel;

    private const double RowWidth = RubricRing.Width - RubricRing.RowsX - RubricRing.RowsRight;

    public RubricView(AssignmentData d, int? openOn, string? startView)
    {
        data = d;
        model = new RubricModel(d.Rubric);
        geo = new RubricGeometry(model);
        clock = new RubricClock(model.N, model.Graded);
        // (every rubric opens on the ring; the grid is one press away and never kept)
        view = startView == "grid" ? "grid" : "ring";
        var title = (d.RubricTitle ?? "").Trim();
        var name = title.Length == 0 || title.Equals("rubric", StringComparison.OrdinalIgnoreCase) ? "Rubric" : title;
        eyebrow = string.IsNullOrEmpty(d.Context) ? name : $"{name} · {d.Context}";
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        System.Windows.Automation.AutomationProperties.SetName(this, $"{d.Title} rubric");

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // the title over the ring, × at the top left, Ring / Grid at the top right
        var close = K.IconButton(SymbolRegular.Dismiss24, () => Close?.Invoke(), "Close the rubric (Esc)", 16, 36);
        backButton = K.Button("Ring", () => ToRing(), icon: SymbolRegular.ChevronLeft24);
        backButton.MinWidth = 0;
        backButton.ToolTip = "Back to the ring (Esc)";
        backButton.Visibility = Visibility.Collapsed;
        backButton.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(backButton, "Back to the ring");
        var corner = K.H(8, close, backButton);
        corner.HorizontalAlignment = HorizontalAlignment.Left;
        corner.VerticalAlignment = VerticalAlignment.Top;
        var switcher = new SelectorBar(new[] { "Ring", "Grid" }, view == "grid" ? 1 : 0) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        switcher.ToolTip = "Show the rubric as a ring or a grid";
        switcher.Changed += i => SetView(i == 1 ? "grid" : "ring");
        var head = K.V(2,
            K.Text(eyebrow, Ty.Caption, B.Text2).Center(),
            K.Text(d.Title, Ty.Subtitle).Center(),
            K.Text(Status, Ty.Body, B.Text2).Center());
        head.HorizontalAlignment = HorizontalAlignment.Center;
        head.Margin = new Thickness(130, 2, 130, 0);
        head.IsHitTestVisible = false;
        header = head;
        var chrome = new Grid { Margin = new Thickness(16, 14, 16, 0), Children = { head, corner, switcher } };
        Children.Add(chrome.Row(0));

        // the stage: the web ring's 760 × 560 units, as large as the room allows
        ringVisual = new RubricRingVisual(geo, clock);
        ringLayer.Children.Add(ringVisual);
        BuildLabels();
        ringLayer.Children.Add(labels);
        centreHost.Children.Add(centre);
        centreHost.Pin(0, new Point(RubricRing.Cx, RubricRing.Cy), 0.5, 0.5);
        centreHost.IsHitTestVisible = false;
        centre.RenderTransformOrigin = new Point(0.5, 0.5);
        centre.RenderTransform = new ScaleTransform(1, 1);
        ringLayer.Children.Add(centreHost);
        ringLayer.RenderTransformOrigin = new Point(0.5, 0.5);
        ringLayer.MouseMove += RingPointed;
        ringLayer.MouseLeave += (_, _) =>
        {
            if (ringHit >= 0 && hov == ringHit) Hover(-1);
            ringHit = -1;
        };
        ringLayer.MouseLeftButtonUp += RingPressed;
        barLayer.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToRing(); };
        back = new RubricRingBack(model, ToRing);
        var stage = new Grid { Width = RubricRing.Width, Height = RubricRing.Height, Children = { ringLayer, barLayer } };
        stageBox.Child = stage;
        stageBox.Margin = new Thickness(12, 6, 12, 0);
        stageBox.RenderTransformOrigin = new Point(0.5, 0.5);
        stageHost.Children.Add(stageBox);
        stageHost.MouseLeftButtonUp += (_, e) =>
        {
            // (a press beside the ring closes it; beside the bar, rolls the bar back up)
            if (e.Handled) return;
            e.Handled = true;
            if (view == "ring" && open) ToRing(); else Close?.Invoke();
        };
        Children.Add(stageHost.Row(1));

        foot = K.Wrapped(FootWords, Ty.Caption, B.Text2).Center();
        foot.Margin = new Thickness(24, 6, 24, 16);
        foot.IsHitTestVisible = false;
        Children.Add(foot.Row(2));

        clock.Frame += OnFrame;
        PlaceLabels();
        PaintCentre();
        FadeCentre();
        if (view == "grid") ShowGrid(false);

        PreviewKeyDown += OnKey;
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);

        // (the screenshot suite: opened on a criterion, once the ring is in)
        if (openOn is { } k && view == "ring" && k >= 0 && k < model.N)
        {
            if (clock.Instant) Select(k);
            else
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
                t.Tick += (_, _) => { t.Stop(); if (!stopped) Select(k); };
                t.Start();
            }
        }
    }

    public void Stop()
    {
        stopped = true;
        clock.Frame -= OnFrame;
        clock.Stop();
    }

    // ---- words ----

    private string Status
    {
        get
        {
            if (model.Graded) return $"Graded · {model.Many}";
            if (data.Held == true) return $"{model.Many} · marks not posted yet";
            return $"{model.Many} · not graded yet";
        }
    }

    /// <summary>What the picture shows, in words true to it — the controls say for themselves what they do.</summary>
    private string FootWords
    {
        get
        {
            if (open) return model.Graded ? "The bar fills to your mark; each tick is a level’s points." : "Each tick on the bar is a level’s points.";
            if (model.Graded) return "Each criterion’s colour is its mark: green for full marks, yellow to orange for part marks, red where most was lost.";
            return "Each criterion’s stretch is its share of the points. Click one to see its levels.";
        }
    }

    // ---- the ring: its labels and its middle ----

    private double NameSize => model.N <= 6 ? 14 : model.N <= 10 ? 13 : 12;

    /// <summary>A criterion's short name and points beside its slice: a chip (what the keys, the pointer and a screen
    /// reader work, and what reads as the thing to click), riding round with the ring — its edge in the criterion's colour
    /// under the pointer.</summary>
    private void BuildLabels()
    {
        for (var k = 0; k < model.N; k++)
        {
            var c = model.Criteria[k];
            var name = K.Text(c.Short, Ty.BodyStrong);
            name.FontSize = NameSize;
            var pts = K.Text(model.PointsLabel(c), Ty.Caption, B.Text2);
            if (model.N > 10) pts.FontSize = 11;
            var words = new StackPanel { Children = { name } };
            if (model.N <= 15) words.Children.Add(pts);
            var n = k;
            var b = K.Subtle(words, () => Select(n), new Thickness(8, 4, 8, 4), 8);
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.BorderThickness = new Thickness(1);
            b.SetResourceReference(Control.BackgroundProperty, B.Control);
            b.SetResourceReference(Control.BorderBrushProperty, B.ControlStroke);
            b.ToolTip = c.Name;
            b.Cursor = Cursors.Hand;
            System.Windows.Automation.AutomationProperties.SetName(b, model.Spoken(c));
            System.Windows.Automation.AutomationProperties.SetHelpText(b, "Opens this criterion’s levels and the comment on it");
            b.MouseEnter += (_, _) => Hover(n);
            b.MouseLeave += (_, _) => { if (hov == n) Hover(-1); };
            labelParts.Add((b, words, name, pts));
            labels.Children.Add(b);
        }
    }

    private void OnFrame()
    {
        if (stopped) return;
        ringVisual.InvalidateVisual();
        PlaceLabels();
        FadeCentre();
    }

    private void PlaceLabels()
    {
        var now = clock.Now;
        var g = clock.Marks(now);
        var go = geo.Bends(g);
        var tones = geo.Tones(g);
        var spots = geo.LabelSpots(sel, hov, go, k => clock.Swell(k, now));
        var a = RubricRing.Span(clock.Bloom(now), 0.45, 0.9);
        for (var k = 0; k < labelParts.Count; k++)
        {
            var s = spots[k];
            var (b, words, name, pts) = labelParts[k];
            labels.Pin(k, s.At, s.AnchorX, s.AnchorY);
            b.Opacity = s.Shown ? a : 0;
            b.IsHitTestVisible = s.Shown && !open;
            b.Focusable = s.Shown && !open;
            name.TextAlignment = pts.TextAlignment = s.Align;
            var h = s.Align switch { TextAlignment.Left => HorizontalAlignment.Left, TextAlignment.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
            name.HorizontalAlignment = pts.HorizontalAlignment = h;
            if (paintedHov != hov || k == hov)
            {
                if (k == hov && !open)
                {
                    name.Foreground = K.Brush(K.Darkened(tones[k].Color));
                    b.BorderBrush = K.Brush(tones[k].Color);
                }
                else
                {
                    name.Fg(B.Text);
                    b.SetResourceReference(Control.BorderBrushProperty, B.ControlStroke);
                }
                pts.Fg(k == hov && !open ? B.Text : B.Text2);
            }
        }
        paintedHov = hov;
        labels.InvalidateArrange();
    }

    /// <summary>The total in the middle of the ring — or, while the pointer or the keys are on a slice, that criterion.</summary>
    private void PaintCentre()
    {
        var hot = hov >= 0 && hov < model.N && !open ? hov : -1;
        var v = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        TextBlock Big(string s)
        {
            var t = K.Text(s, Ty.Display);
            t.FontSize = 46;
            t.LineHeight = 54;
            t.FontWeight = FontWeights.Bold;
            return t.Center();
        }
        if (hot >= 0)
        {
            var c = model.Criteria[hot];
            var tones = geo.Tones(clock.Marks(clock.Now));
            var shortName = K.Text(c.Short, Ty.BodyStrong).Center();
            shortName.Foreground = K.Brush(K.Darkened(tones[hot].Color));
            v.Children.Add(shortName);
            v.Children.Add(Big(RubricRing.Num(c.Score ?? c.Worth)));
            string line;
            if (!model.Graded) line = c.Worth == 1 ? "point" : "points";
            else if (c.Score == null) line = $"of {RubricRing.Num(c.Worth)} · not marked";
            else line = $"of {RubricRing.Num(c.Worth)}" + (c.Mark >= 0 ? $" · {c.Levels[c.Mark].Label}{(c.Adjusted ? ", adjusted" : "")}" : "");
            v.Children.Add(K.Wrapped(line, Ty.Body, B.Text2, 2).Center());
        }
        else
        {
            v.Children.Add(K.Text(model.Graded ? "Score" : "Total", Ty.Caption, B.Text2).Strong().Center());
            v.Children.Add(Big(RubricRing.Num(model.Graded ? model.Earned : model.Possible)));
            v.Children.Add(K.Text(model.Graded ? $"of {RubricRing.Num(model.Possible)} · {model.Percent}%" : model.Possible == 1 ? "point" : "points", Ty.Body, B.Text2).Center());
            // (what to do, in words that match what is on the screen: the names round the ring)
            v.Children.Add(K.Wrapped(model.Graded ? "Click a criterion to see its levels and comment" : "Click a criterion to see its levels", Ty.Caption, B.Text3).Margin(0, 8, 0, 0).Center());
        }
        centre.Child = v;
    }

    private void FadeCentre()
    {
        var fade = RubricRing.Span(clock.Bloom(clock.Now), 0.3, 0.75);
        centre.Opacity = fade;
        if (centre.RenderTransform is ScaleTransform s) s.ScaleX = s.ScaleY = 0.9 + 0.1 * fade;
    }

    // ---- presses, the pointer, the keys ----

    private void RingPointed(object sender, MouseEventArgs e)
    {
        if (view != "ring" || open) return;
        var p = e.GetPosition(ringLayer);
        var k = geo.Slice(p.X, p.Y) ?? -1;
        if (k == ringHit) return;
        if (k >= 0) Hover(k);
        else if (hov == ringHit) Hover(-1);
        ringHit = k;
    }

    /// <summary>A slice opens; a press inside the ring is nothing; beside it, the ring closes.</summary>
    private void RingPressed(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Focus();
        if (view != "ring") { Close?.Invoke(); return; }
        if (open) { ToRing(); return; }
        var p = e.GetPosition(ringLayer);
        if (geo.Slice(p.X, p.Y) is { } k) { Select(k); return; }
        var dx = p.X - RubricRing.Cx;
        var dy = p.Y - RubricRing.Cy;
        if (Math.Sqrt(dx * dx + dy * dy) < RubricRing.Radius + 50) return; // (a press inside the ring is not beside it)
        Close?.Invoke();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows);
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            if (view == "ring" && open) ToRing(); else Close?.Invoke();
            return;
        }
        if (mods != 0 || view != "ring") return;
        switch (e.Key)
        {
            case System.Windows.Input.Key.Left or System.Windows.Input.Key.Up:
                Step(-1);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Right or System.Windows.Input.Key.Down:
                Step(1);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space when !open && Keyboard.FocusedElement == this:
                Select(sel);
                e.Handled = true;
                break;
        }
    }

    // ---- moving ----

    private void Hover(int k)
    {
        if (hov == k) return;
        hov = k;
        ringVisual.Hov = k;
        clock.Aim(open ? -1 : k);
        PaintCentre();
        PlaceLabels();
        ringVisual.InvalidateVisual();
    }

    /// <summary>← → : along the ring (the slice swells and its criterion shows in the middle), or along the bar.</summary>
    private void Step(int d)
    {
        var n = model.N;
        if (n == 0) return;
        var k = ((sel + d) % n + n) % n;
        if (open) { Select(k); return; }
        sel = k;
        ringVisual.Sel = k;
        Hover(k);
    }

    /// <summary>A criterion opened (a slice, its label, Enter): from the ring, its bar comes in; with the bar out, the bar
    /// switches to it in place.</summary>
    private void Select(int raw)
    {
        var n = model.N;
        if (n == 0) return;
        var k = ((raw % n) + n) % n;
        if (open)
        {
            if (k == sel) return;
            sel = k;
            ringVisual.Sel = k;
            BuildBar(swap: true);
            KeepKeys();
            return;
        }
        sel = k;
        ringVisual.Sel = k;
        open = true;
        clock.Aim(-1);
        // the ring sinks back, the criterion's bar comes up beside where it was
        Leave(ringLayer, 0.94);
        barLayer.BeginAnimation(OpacityProperty, null);
        barLayer.Opacity = 1;
        barLayer.Visibility = Visibility.Visible;
        barLayer.IsHitTestVisible = true;
        BuildBar(swap: false);
        PlaceLabels();
        PaintCentre();
        foot.Text = FootWords;
        ShowBack(true);
        KeepKeys();
    }

    /// <summary>"Ring" beside × while a criterion is open (a short fade; at once with Windows' animation effects off).</summary>
    private void ShowBack(bool on)
    {
        if (on == (backButton.Visibility == Visibility.Visible)) return;
        backButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) Motion.FadeIn(backButton, Motion.Fast);
    }

    /// <summary>The keys kept on the rubric when what had them went (a label, a dot built anew).</summary>
    private void KeepKeys() => Dispatcher.BeginInvoke(() => { if (!stopped && !IsKeyboardFocusWithin) Focus(); }, DispatcherPriority.Input);

    /// <summary>The bar rolls back up into the ring.</summary>
    private void ToRing()
    {
        if (!open) return;
        open = false;
        barLayer.IsHitTestVisible = false;
        Motion.FromTo(barLayer, OpacityProperty, barLayer.Opacity, 0, Motion.Normal, Motion.Accelerate, 0, () => { if (!open) barLayer.Visibility = Visibility.Hidden; });
        Arrive(ringLayer);
        clock.Aim(hov);
        PlaceLabels();
        PaintCentre();
        foot.Text = FootWords;
        ShowBack(false);
        KeepKeys();
    }

    private static void Leave(FrameworkElement e, double scale)
    {
        e.IsHitTestVisible = false;
        var s = e.RenderTransform as ScaleTransform;
        if (s == null || s.IsFrozen) { s = new ScaleTransform(1, 1); e.RenderTransform = s; }
        Motion.FromTo(e, OpacityProperty, e.Opacity, 0, Motion.Normal, Motion.Accelerate);
        Motion.To(s, ScaleTransform.ScaleXProperty, scale, Motion.Medium, Motion.Accelerate);
        Motion.To(s, ScaleTransform.ScaleYProperty, scale, Motion.Medium, Motion.Accelerate);
    }

    private static void Arrive(FrameworkElement e)
    {
        e.BeginAnimation(OpacityProperty, null);
        e.Opacity = 1;
        e.Visibility = Visibility.Visible;
        e.IsHitTestVisible = true;
        if (e.RenderTransform is ScaleTransform { IsFrozen: false } s)
        {
            s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            s.ScaleX = s.ScaleY = 1;
        }
        Motion.DrillIn(e);
    }

    // ---- the bar's side ----

    /// <summary>The open criterion: its bar, its levels beside it at the height of their points (a leader to one that had to move) (the one
    /// given marked — and the points given, where they are not that level's), which criterion this is with ‹ 1 of 2 ›
    /// to switch, the whole of its description when the levels still fit under it, the marker's note, and the little
    /// ring to go back.</summary>
    private void BuildBar(bool swap)
    {
        barLayer.Children.Clear();
        var k = sel;
        var c = model.Criteria[k];
        var tone = model.Colors[k];
        var levels = geo.Levels(k);
        var lines = levels.Count <= 4 ? 2 : levels.Count <= 6 ? 1 : 0;
        var marked = model.Graded && c.Score != null;
        var rows = new List<FrameworkElement>();
        var heights = new List<double>();
        for (var i = 0; i < levels.Count; i++)
        {
            var picked = geo.Picked(k, i);
            var row = LevelRow(levels[i], picked, marked && !picked, tone, lines, c.MarkText);
            row.Width = RowWidth;
            row.Measure(new Size(RowWidth, double.PositiveInfinity));
            rows.Add(row);
            heights.Add(row.DesiredSize.Height);
        }
        var foot = geo.Bottoms[k];
        // (the whole description, where the levels still fit under it; else two lines of it and More)
        FrameworkElement head = Head(k, tone, full: false);
        double? top = null;
        if (c.Desc.Length > 0)
        {
            var whole = Head(k, tone, full: true);
            whole.Width = RowWidth;
            whole.Measure(new Size(RowWidth, double.PositiveInfinity));
            if (RubricGeometry.FullHeadTop(foot, heights, whole.DesiredSize.Height) is { } t) { head = whole; top = t; }
        }
        var places = geo.Places(k, foot, heights, top);
        var markY = geo.MarkY(k, foot);
        var bar = new RubricBarVisual(tone, model.Graded ? c.Frac : 1, foot, places, markY, markY != null && c.Score is { } given ? RubricRing.Num(given) : null);
        barLayer.Children.Add(bar);
        for (var i = 0; i < rows.Count; i++)
        {
            Canvas.SetLeft(rows[i], RubricRing.RowsX);
            Canvas.SetTop(rows[i], places[i].Row - heights[i] / 2);
            barLayer.Children.Add(rows[i]);
        }
        head.Width = RowWidth;
        Canvas.SetLeft(head, RubricRing.RowsX);
        Canvas.SetTop(head, 24);
        barLayer.Children.Add(head);
        FrameworkElement? note = null;
        if (c.Comment.Length > 0)
        {
            note = Note(c.Comment);
            note.Width = RowWidth;
            note.Measure(new Size(RowWidth, double.PositiveInfinity));
            Canvas.SetLeft(note, RubricRing.RowsX);
            Canvas.SetTop(note, RubricRing.Height - 14 - note.DesiredSize.Height);
            barLayer.Children.Add(note);
        }
        Canvas.SetLeft(back, RubricRing.MiniX - 22);
        Canvas.SetTop(back, RubricRing.MiniY - 22);
        barLayer.Children.Add(back);
        back.Show(model, k, swap);
        if (!Motion.On) return;
        // it grows up from its foot, its rows coming out from beside it one after another
        Motion.FromTo(bar, RubricBarVisual.GrowProperty, swap ? 0.6 : 0, 1, swap ? 420 : 760, Motion.Decelerate);
        for (var i = 0; i < rows.Count; i++)
        {
            var t = new TranslateTransform(-18, 0);
            rows[i].RenderTransform = t;
            var delay = (swap ? 60 : 300) + 60 * i;
            rows[i].Opacity = 0;
            Motion.FromTo(rows[i], OpacityProperty, 0, 1, Motion.Medium, Motion.Decelerate, delay);
            Motion.FromTo(t, TranslateTransform.XProperty, -18, 0, Motion.Slow, Motion.Decelerate, delay);
        }
        Motion.Enter(head, swap ? 0 : 220, 8, 360);
        if (note != null) Motion.FadeIn(note, Motion.Medium, swap ? 80 : 360);
        if (!swap)
        {
            back.Opacity = 0;
            Motion.FadeIn(back, Motion.Medium, 420);
        }
        Support.Shot.Moved();
    }

    /// <summary>A level of the open criterion: its points in the criterion's colour, its name ("Your mark" on the one
    /// given — "Your mark · 4 pts" where the points given are not the level's) and what it asks for, on a card of its own
    /// beside the bar.</summary>
    private static FrameworkElement LevelRow(RubricModel.Level level, bool picked, bool dim, RubricRGB tone, int lines, string mark = "Your mark")
    {
        var pts = new TextBlock
        {
            Text = level.Pts is { } p ? RubricRing.Num(p) : "–", FontSize = 21, FontWeight = FontWeights.Bold, FontFamily = K.DisplayFace,
            Width = 48, VerticalAlignment = VerticalAlignment.Center,
        };
        if (dim) pts.Fg(B.Text3); else pts.Foreground = K.Brush(K.Darkened(tone.Color));
        var label = K.Wrapped(level.Label, Ty.BodyStrong, dim ? B.Text2 : B.Text, 2);
        var top = K.Columns("*, auto", label.Col(0), picked ? YourMark(tone, mark).Margin(8, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Top).Col(1) : null);
        var words = K.V(2, top);
        if (lines > 0 && level.Text.Length > 0) words.Children.Add(K.Wrapped(level.Text, Ty.Body, dim ? B.Text3 : B.Text2, lines));
        var box = new Border { Child = words, Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8), SnapsToDevicePixels = true };
        if (picked)
        {
            box.Background = K.Brush(tone.WithAlpha(Support.Appearance.Dark ? 0.22 : 0.14));
            box.BorderBrush = K.Brush(tone.Color);
            box.BorderThickness = new Thickness(1.5);
        }
        else box.Bg(B.Card).Stroke(B.CardStroke);
        var g = K.Columns("48, 14, *", pts.Col(0), box.Col(2));
        g.Background = Brushes.Transparent;
        g.MouseLeftButtonUp += (_, e) => e.Handled = true;
        if (picked) System.Windows.Automation.AutomationProperties.SetItemStatus(g, mark);
        return g;
    }

    /// <summary>"Your mark" (or "Your mark · 4 pts", the points given where they are not the level's), on the criterion's
    /// colour.</summary>
    public static Border YourMark(RubricRGB tone, string text = "Your mark")
    {
        var t = K.Text(text, Ty.Caption);
        t.FontWeight = FontWeights.SemiBold;
        t.Foreground = K.Brush(tone.Ink);
        return new Border { Child = t, Background = K.Brush(tone.Color), CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 1, 7, 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Which criterion is open: its colour and its points, its name, its description — two lines and More, or
    /// <paramref name="full"/>, all of it — and ‹ 1 of 2 › to switch to the one before or after in place, each arrow
    /// saying which it opens.</summary>
    private FrameworkElement Head(int k, RubricRGB tone, bool full)
    {
        var c = model.Criteria[k];
        var square = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = K.Brush(tone.Color), VerticalAlignment = VerticalAlignment.Center };
        var left = K.V(5,
            K.H(8, square, K.Text(model.PointsLabel(c), Ty.Caption, B.Text2).Strong()),
            K.Wrapped(c.Name, Ty.Subtitle, B.Text, 2));
        if (c.Desc.Length > 0)
            left.Children.Add(full ? (FrameworkElement)AssignKit.Selectable(c.Desc, Ty.Body, B.Text2) : Description(c.Desc, c.Name.Length > 44 ? 1 : 2)); // (a long name: a line less)
        var g = K.Columns("*, auto", left.Margin(0, 0, 14, 0).Col(0), Stepper(k).Align(HorizontalAlignment.Right, VerticalAlignment.Top).Col(1));
        g.Background = Brushes.Transparent;
        g.MouseLeftButtonUp += (_, e) => e.Handled = true; // (a press on the head is not a press beside the bar)
        return g;
    }

    /// <summary>‹ 1 of 2 ›: the criterion before and after, each arrow naming which it opens (← and → do it too). It took
    /// the place of a dot per criterion, too small to find and to hit.</summary>
    private FrameworkElement Stepper(int k)
    {
        var n = model.N;
        var before = model.Criteria[(k - 1 + n) % n];
        var after = model.Criteria[(k + 1) % n];
        Button Arrow(SymbolRegular symbol, RubricModel.Criterion to, string word, string key)
        {
            var b = K.IconButton(symbol, () => Select(to.Index), $"{word}: {to.Name} ({key})", 14, 32);
            System.Windows.Automation.AutomationProperties.SetName(b, $"{word} criterion: {to.Name}");
            return b;
        }
        var count = K.Text($"{k + 1} of {n}", Ty.BodyStrong, B.Text2).Margin(2, 0, 2, 0);
        count.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Documents.Typography.SetNumeralAlignment(count, FontNumeralAlignment.Tabular);
        System.Windows.Automation.AutomationProperties.SetName(count, $"Criterion {k + 1} of {n}");
        var box = new Border { Child = K.H(0, Arrow(SymbolRegular.ChevronLeft24, before, "Previous", "←"), count, Arrow(SymbolRegular.ChevronRight24, after, "Next", "→")), CornerRadius = new CornerRadius(6), Padding = new Thickness(1) };
        box.Bg(B.Control).Stroke(B.ControlStroke);
        box.Visibility = n > 1 ? Visibility.Visible : Visibility.Collapsed;
        return box;
    }

    private static FrameworkElement Description(string text, int lines)
    {
        var v = K.V(2, K.Wrapped(text, Ty.Body, B.Text2, lines));
        v.MaxWidth = 470;
        v.HorizontalAlignment = HorizontalAlignment.Left;
        if (text.Length > lines * 66)
        {
            var pop = new Popup { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
            var more = K.Link("More", () => pop.IsOpen = true);
            more.HorizontalAlignment = HorizontalAlignment.Left;
            more.Margin = new Thickness(-6, 0, 0, 0);
            more.ToolTip = "The whole description";
            pop.PlacementTarget = more;
            var words = AssignKit.Selectable(text, Ty.Body);
            var card = new Border
            {
                Child = new ScrollViewer { Content = words, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16) },
                Width = 420, CornerRadius = new CornerRadius(8), Margin = new Thickness(12, 4, 12, 16), Effect = K.Shadow(24, 6, 0.22),
            };
            card.Bg(B.Flyout).Stroke(B.FlyoutStroke);
            pop.Child = card;
            v.Children.Add(more);
            v.Children.Add(pop);
        }
        return v;
    }

    /// <summary>The marker's note on the open criterion, under its levels: as tall as its words, at most 92, then they scroll.</summary>
    private static FrameworkElement Note(string text)
    {
        var icon = K.Icon(SymbolRegular.Comment24, 15, B.Text2, filled: true);
        icon.VerticalAlignment = VerticalAlignment.Top;
        var words = AssignKit.Selectable(text, Ty.Body);
        var scroll = new ScrollViewer { Content = words, MaxHeight = 92 - 18, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var box = new Border { Child = K.Columns("auto, *", icon.Margin(0, 2, 9, 0).Col(0), scroll.Col(1)), Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8) };
        box.Bg(B.Card).Stroke(B.CardStroke);
        box.MouseLeftButtonUp += (_, e) => e.Handled = true;
        System.Windows.Automation.AutomationProperties.SetName(box, "Comment: " + text);
        return box;
    }

    // ---- Ring or Grid ----

    private void SetView(string v)
    {
        if (v == view) return;
        view = v;
        if (v == "grid") ShowGrid(true);
        else
        {
            if (gridPanel != null)
            {
                var gp = gridPanel;
                gp.IsHitTestVisible = false;
                Motion.FadeOut(gp, () => { if (view == "ring") gp.Visibility = Visibility.Collapsed; });
            }
            header.Visibility = Visibility.Visible;
            foot.Visibility = Visibility.Visible;
            ShowBack(open);
            Arrive(stageBox);
            Focus();
        }
    }

    private void ShowGrid(bool animate)
    {
        Hover(-1);
        ShowBack(false); // (the grid has no criterion open to go back from)
        if (gridPanel == null)
        {
            // (as tall as its rows, in the middle of the room: not a sheet left mostly blank under two criteria)
            gridPanel = new RubricGridPanel(model, eyebrow, data.Title) { Margin = new Thickness(24, 0, 24, 20), VerticalAlignment = VerticalAlignment.Center };
            stageHost.Children.Add(gridPanel);
        }
        header.Visibility = Visibility.Hidden;
        foot.Visibility = Visibility.Hidden;
        if (animate) Leave(stageBox, 0.94);
        else { stageBox.Opacity = 0; stageBox.IsHitTestVisible = false; }
        Arrive(gridPanel);
    }
}
