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
/// the grade card's rubric button — and taken down with that page. A Windows 11 dialog over the dimmed window, as large
/// as the window has room for.
/// </summary>
internal static class RubricPopup
{
    private static Sheet? sheet;
    private static string? owner;

    /// <summary>The rubric shown: on the ring (or, for the screenshot suite, the grid, or opened on a criterion).</summary>
    public static void Show(AssignmentData d, string who, int? open = null, string? view = null)
    {
        if (d.Rubric.Count == 0) return;
        sheet?.Close();
        var win = Application.Current?.MainWindow;
        var w = win is { ActualWidth: > 0 } ? win.ActualWidth : 1280;
        var h = win is { ActualHeight: > 0 } ? win.ActualHeight : 820;
        var v = new RubricView(d, open, view);
        var s = new Sheet { Bare = true, Modal = true, Body = v, Width = Math.Clamp(w - 64, 360, 1180), Height = Math.Clamp(h - 96, 360, 880) };
        v.Close = () => s.Close();
        v.Loaded += (_, _) =>
        {
            // (the dialog as wide as the window has room for, past a dialog's usual most)
            if (s.Card is { } card && s.Host is { ActualWidth: > 0 } host) card.MaxWidth = Math.Max(280, host.ActualWidth - 48);
        };
        s.Closed += () =>
        {
            v.Stop();
            if (sheet != s) return;
            sheet = null;
            owner = null;
        };
        sheet = s;
        owner = who;
        Dialogs.Show(s, win);
    }

    /// <summary>The rubric taken down if it is the one this assignment's page put up (the page went away).</summary>
    public static void Dismiss(string who)
    {
        if (owner == who) sheet?.Close();
    }
}

/// <summary>
/// The rubric as a ring (the Mac's RubricPopupView over RubricStage.swift): one slice per criterion, as long as its share
/// of the points, blooming in and — marked — bending to its marks and turning to its grades' colours; the total in the
/// middle, or the criterion under the pointer. A slice, its label, or Enter opens that criterion into its bar: its levels
/// at the height of their points, the one given marked, the marker's note under them, a dot per criterion to switch, and
/// the little ring on top to go back. ← → walk the criteria; Escape steps back, then closes; a press beside the ring
/// closes it too. Ring or Grid at the top right (RubricGrid.cs).
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
        close.HorizontalAlignment = HorizontalAlignment.Left;
        close.VerticalAlignment = VerticalAlignment.Top;
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
        var chrome = new Grid { Margin = new Thickness(16, 14, 16, 0), Children = { head, close, switcher } };
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

    private string FootWords
    {
        get
        {
            if (open) return "Height on the bar is points. Switch with the dots; the little ring at the top or Esc goes back.";
            if (model.Graded) return "The ring pushes out where you scored well and pulls in where you lost points.";
            return "Each colour’s stretch is its share of the points. Pick one to open it.";
        }
    }

    // ---- the ring: its labels and its middle ----

    private double NameSize => model.N <= 6 ? 14 : model.N <= 10 ? 13 : 12;

    /// <summary>A criterion's short name and points beside its slice: a button (what the keys, the pointer and a screen
    /// reader work), riding round with the ring.</summary>
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
            var b = K.Subtle(words, () => Select(n), new Thickness(6, 3, 6, 3));
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
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
                if (k == hov && !open) name.Foreground = new SolidColorBrush(K.Darkened(tones[k].Color));
                else name.Fg(B.Text);
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
            shortName.Foreground = new SolidColorBrush(K.Darkened(tones[hot].Color));
            v.Children.Add(shortName);
            v.Children.Add(Big(RubricRing.Num(c.Score ?? c.Worth)));
            string line;
            if (!model.Graded) line = c.Worth == 1 ? "point" : "points";
            else if (c.Score == null) line = $"of {RubricRing.Num(c.Worth)} · not marked";
            else line = $"of {RubricRing.Num(c.Worth)}" + (c.Mark >= 0 ? $" · {c.Levels[c.Mark].Label}" : "");
            v.Children.Add(K.Wrapped(line, Ty.Body, B.Text2, 2).Center());
        }
        else
        {
            v.Children.Add(K.Text(model.Graded ? "Score" : "Total", Ty.Caption, B.Text2).Strong().Center());
            v.Children.Add(Big(RubricRing.Num(model.Graded ? model.Earned : model.Possible)));
            v.Children.Add(K.Text(model.Graded ? $"of {RubricRing.Num(model.Possible)} · {model.Percent}%" : model.Possible == 1 ? "point" : "points", Ty.Body, B.Text2).Center());
            v.Children.Add(K.Text("Pick a colour to open it", Ty.Caption, B.Text3).Margin(0, 8, 0, 0).Center());
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
        KeepKeys();
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

    /// <summary>The open criterion: its bar, its levels at the height of their points (the one given marked), which
    /// criterion this is with the dots to switch, the marker's note, and the little ring to go back.</summary>
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
            var row = LevelRow(levels[i], picked, marked && !picked, tone, lines);
            row.Width = RowWidth;
            row.Measure(new Size(RowWidth, double.PositiveInfinity));
            rows.Add(row);
            heights.Add(row.DesiredSize.Height);
        }
        var foot = geo.Bottoms[k];
        var places = geo.Places(k, foot, heights);
        var bar = new RubricBarVisual(tone, model.Graded ? c.Frac : 1, foot, places);
        barLayer.Children.Add(bar);
        for (var i = 0; i < rows.Count; i++)
        {
            Canvas.SetLeft(rows[i], RubricRing.RowsX);
            Canvas.SetTop(rows[i], places[i].Row - heights[i] / 2);
            barLayer.Children.Add(rows[i]);
        }
        var head = Head(k, tone);
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
    /// given) and what it asks for, on a card of its own beside the bar.</summary>
    private static FrameworkElement LevelRow(RubricModel.Level level, bool picked, bool dim, RubricRGB tone, int lines)
    {
        var pts = new TextBlock
        {
            Text = level.Pts is { } p ? RubricRing.Num(p) : "–", FontSize = 21, FontWeight = FontWeights.Bold, FontFamily = K.DisplayFace,
            Width = 48, VerticalAlignment = VerticalAlignment.Center,
        };
        if (dim) pts.Fg(B.Text3); else pts.Foreground = new SolidColorBrush(K.Darkened(tone.Color));
        var label = K.Wrapped(level.Label, Ty.BodyStrong, dim ? B.Text2 : B.Text, 2);
        var top = K.Columns("*, auto", label.Col(0), picked ? YourMark(tone).Margin(8, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Top).Col(1) : null);
        var words = K.V(2, top);
        if (lines > 0 && level.Text.Length > 0) words.Children.Add(K.Wrapped(level.Text, Ty.Body, dim ? B.Text3 : B.Text2, lines));
        var box = new Border { Child = words, Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8), SnapsToDevicePixels = true };
        if (picked)
        {
            box.Background = new SolidColorBrush(tone.WithAlpha(Support.Appearance.Dark ? 0.22 : 0.14));
            box.BorderBrush = new SolidColorBrush(tone.Color);
            box.BorderThickness = new Thickness(1.5);
        }
        else box.Bg(B.Card).Stroke(B.CardStroke);
        var g = K.Columns("48, 14, *", pts.Col(0), box.Col(2));
        g.Background = Brushes.Transparent;
        g.MouseLeftButtonUp += (_, e) => e.Handled = true;
        if (picked) System.Windows.Automation.AutomationProperties.SetItemStatus(g, "Your mark");
        return g;
    }

    /// <summary>"Your mark", on the criterion's colour.</summary>
    public static Border YourMark(RubricRGB tone)
    {
        var t = K.Text("Your mark", Ty.Caption);
        t.FontWeight = FontWeights.SemiBold;
        t.Foreground = new SolidColorBrush(tone.Ink);
        return new Border { Child = t, Background = new SolidColorBrush(tone.Color), CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 1, 7, 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Which criterion is open: its colour, which of how many and its points, its name, its description (More
    /// shows the rest), and a dot per criterion to switch to another in place.</summary>
    private FrameworkElement Head(int k, RubricRGB tone)
    {
        var c = model.Criteria[k];
        var square = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(tone.Color), VerticalAlignment = VerticalAlignment.Center };
        var left = K.V(5,
            K.H(8, square, K.Text($"Criterion {k + 1} of {model.N} · {model.PointsLabel(c)}", Ty.Caption, B.Text2).Strong()),
            K.Wrapped(c.Name, Ty.Subtitle, B.Text, 2));
        if (c.Desc.Length > 0) left.Children.Add(Description(c.Desc, c.Name.Length > 44 ? 1 : 2)); // (a long name: a line less)
        var dots = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var n = model.N;
        foreach (var other in model.Criteria)
        {
            var on = other.Index == k;
            double d = on ? (n > 10 ? 11 : 14) : n > 15 ? 6 : n > 10 ? 8 : 11;
            double hit = n > 15 ? 17 : 23;
            var colour = new SolidColorBrush(model.Colors[other.Index].Color);
            var face = new Grid { Width = hit, Height = hit };
            face.Children.Add(new Ellipse { Width = d, Height = d, Fill = colour, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            if (on) face.Children.Add(new Ellipse { Width = Math.Min(hit, d + 8), Height = Math.Min(hit, d + 8), Stroke = colour, StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var idx = other.Index;
            var b = K.Subtle(face, () => Select(idx), new Thickness(0), hit / 2);
            b.Width = hit;
            b.Height = hit;
            b.Margin = new Thickness(1);
            b.ToolTip = other.Name;
            System.Windows.Automation.AutomationProperties.SetName(b, $"{other.Name}, criterion {other.Index + 1} of {n}");
            dots.Children.Add(b);
        }
        var g = K.Columns("*, 150", left.Margin(0, 0, 14, 0).Col(0), dots.Col(1));
        g.Background = Brushes.Transparent;
        g.MouseLeftButtonUp += (_, e) => e.Handled = true; // (a press on the head is not a press beside the bar)
        return g;
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
            Arrive(stageBox);
            Focus();
        }
    }

    private void ShowGrid(bool animate)
    {
        Hover(-1);
        if (gridPanel == null)
        {
            gridPanel = new RubricGridPanel(model, eyebrow, data.Title) { Margin = new Thickness(24, 0, 24, 20) };
            stageHost.Children.Add(gridPanel);
        }
        header.Visibility = Visibility.Hidden;
        foot.Visibility = Visibility.Hidden;
        if (animate) Leave(stageBox, 0.94);
        else { stageBox.Opacity = 0; stageBox.IsHitTestVisible = false; }
        Arrive(gridPanel);
    }
}
