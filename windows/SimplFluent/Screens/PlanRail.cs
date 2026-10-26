using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using Popup = System.Windows.Controls.Primitives.Popup;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// (0.3.60) Pull to plan, as the web (extension/content/app/plan.js) and the Mac (PlanRail.swift) have it: a piece of work
/// pulled to the right on To Do or the Dashboard opens a rail of the free times between now and when it is due under it —
/// found by the page (native-app.js <c>planSlots</c>: 8 am to 11 pm, clear of the linked calendars, the classes on
/// Canvas's calendar and the other plans) — the further it is pulled the later the time, and let go on one, it is planned
/// there. Pulled back short, or Escape, nothing. The same rail opens from the work's menu (Plan a Time…) and from P on a
/// row, where the times are clicked, or chosen with the arrows and Enter; Move Plan… and Clear Plan once it is planned.
/// (0.3.64: To Do's rows, beside the calendar, are dragged onto it instead; the rail opens from their menu and P.)
/// </summary>
internal static class PlanRail
{
    private const double Engage = 14; // px sideways before a press is a pull (a press with less is the row's own)
    private const double Lead = 26; // px of pull before the first time: let go short of it and nothing is planned
    private const double Step = 26; // px of pull a time
    private const double Chip = 98;
    private const double Gap = 6;
    private const double DayGap = 8; // (0.3.62) a little more room before each new day's first time
    private const double Most = 64; // the furthest the row goes, however far it is pulled (it resists)
    private static readonly int[] Lengths = { 30, 60, 90, 120 };

    /// <summary>The rail open now (one at a time).</summary>
    private static Rail? shown;

    public static string LengthWord(int m) => m < 60 ? $"{m}m" : m % 60 == 0 ? $"{m / 60}h" : $"{m / 60}½h";

    /// <summary>The row's plan: "Planned Tue 4:00 PM", a small capsule in the accent (grey once its time has gone by).</summary>
    public static FrameworkElement Pill(WorkPlan plan)
    {
        var past = plan.Past == true;
        var color = past ? K.Tone("gray") : Support.Appearance.Accent;
        var fore = past ? (Brush)K.Brush(K.Tone("gray")) : K.Brush(K.Darkened(color));
        var words = K.Text(plan.Words, Ty.Caption);
        words.Foreground = fore;
        words.FontWeight = FontWeights.SemiBold;
        var icon = K.Icon(SymbolRegular.Clock24, 11, past ? K.Tone("gray") : K.Darkened(color));
        var pill = new Border
        {
            Child = K.H(4, icon, words), Padding = new Thickness(7, 1, 8, 2), CornerRadius = new CornerRadius(9),
            Background = K.Brush(Color.FromArgb(Support.Appearance.Dark ? (byte)52 : (byte)32, color.R, color.G, color.B)),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        };
        pill.ToolTip = $"{LengthWord(plan.Mins)} set aside · move or clear it from the work's menu";
        AutomationProperties.SetName(pill, plan.Words);
        return pill;
    }

    /// <summary>
    /// A row of work made pullable: <paramref name="row"/> follows the pointer (and then resists) with the rail under it.
    /// It must not be the button pressed itself, but hold it: the pull takes the mouse from the button, so a pull is never
    /// also a click. P on the row (or within it) opens the rail in the choosing kind.
    /// </summary>
    public static void Attach(FrameworkElement row, string id, string title, Engine.Engine engine, Action? planned = null)
    {
        var shift = new TranslateTransform();
        row.RenderTransform = shift;
        Point? start = null;
        Rail? pull = null;

        double Rubber(double x) => x <= 0 ? 0 : Most * x / (x + Most);

        PullReveal? reveal = null;

        void Back(Action? done = null)
        {
            if (Motion.On) Motion.To(shift, TranslateTransform.XProperty, 0, Motion.Medium, Motion.Out, done: done);
            else { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.X = 0; done?.Invoke(); }
        }

        void End(bool commit)
        {
            start = null;
            if (pull is not { } r) return;
            pull = null;
            if (row.IsMouseCaptured) row.ReleaseMouseCapture();
            var gone = reveal;
            reveal = null;
            gone?.Follow();
            Back(() => gone?.Remove());
            if (commit && r.Chosen is { } s) r.Plan(s);
            else r.Close();
        }

        // (where the pointer is, on the window: the row itself moves with the pull)
        IInputElement? Basis() => Window.GetWindow(row);
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (shown?.Kind == RailKind.Pull) return;
            start = e.GetPosition(Basis());
        };
        row.PreviewMouseMove += (_, e) =>
        {
            if (start is not { } from) return;
            if (e.LeftButton != MouseButtonState.Pressed) { End(false); return; }
            var at = e.GetPosition(Basis());
            var dx = at.X - from.X;
            var dy = at.Y - from.Y;
            if (pull == null)
            {
                if (dx <= Engage || dx <= Math.Abs(dy) * 1.4)
                {
                    if (Math.Abs(dy) > Engage) start = null; // (a scroll or a press that went down: not a pull)
                    return;
                }
                // (the pull takes the mouse from the row's button, so letting go is not also its click)
                row.CaptureMouse();
                shift.BeginAnimation(TranslateTransform.XProperty, null);
                pull = Rail.Open(row, id, title, engine, RailKind.Pull, planned);
                reveal = PullReveal.On(row, shift);
            }
            var pulled = Math.Max(0, dx - Engage);
            shift.X = Rubber(pulled);
            pull.Pulled(pulled);
            reveal?.Chose(pull.Chosen != null);
            e.Handled = true;
        };
        row.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (pull != null) e.Handled = true;
            End(true);
        };
        // (the mouse taken away mid-pull — another window, a menu: what is under the lens is still planned)
        row.LostMouseCapture += (_, e) => { if (pull != null && ReferenceEquals(e.OriginalSource, row)) End(true); };
        row.PreviewKeyDown += (_, e) =>
        {
            if (pull != null && e.Key == Key.Escape) { e.Handled = true; End(false); return; }
            if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.None && pull == null)
            {
                e.Handled = true;
                Open(row, id, title, engine, planned);
            }
        };
        row.Unloaded += (_, _) => { if (pull != null) End(false); };
    }

    /// <summary>The rail in the choosing kind, under <paramref name="at"/> (the menu's Plan a Time…, Move Plan…, and P).
    /// <paramref name="held"/>: kept open as the app goes behind another (the screenshot suite's picture).</summary>
    public static void Open(FrameworkElement at, string id, string title, Engine.Engine engine, Action? planned = null, bool held = false)
    {
        var r = Rail.Open(at, id, title, engine, RailKind.Pick, planned);
        if (held) r.Hold();
    }

    /// <summary>A plan cleared (the menu's Clear Plan), and every screen read again.</summary>
    public static async void Clear(string id, Engine.Engine engine, Action? planned = null)
    {
        await engine.Act("planClear", new { id });
        planned?.Invoke();
        engine.DataChanged();
    }

    private enum RailKind { Pull, Pick }

    /// <summary>The rail itself: the work's name and how long, the times (the one chosen lit), and a line saying what
    /// letting go (or Enter) does.</summary>
    private sealed class Rail
    {
        public RailKind Kind { get; }
        private readonly FrameworkElement at;
        private readonly string id, title;
        private readonly Engine.Engine engine;
        private readonly Action? planned;
        private readonly Popup popup;
        private readonly Grid surface;
        private readonly Border railHost = new() { Height = 50, ClipToBounds = true };
        private readonly TextBlock status;
        private readonly Border clearHost = new();
        private readonly SelectorBar lengths;
        private List<PlanSlot>? slots;
        private string? plannedAt, dueWords, error;
        private int sel = -1, mins = 60, asked;
        private double pulled;
        private StackPanel? strip;
        private readonly TranslateTransform stripShift = new();
        private readonly List<Border> chips = new();
        private readonly List<Button> chipButtons = new();
        /// <summary>(0.3.62) Where each time is along the rail: the days are not all one gap apart.</summary>
        private readonly List<double> lefts = new();
        private ScrollViewer? scroller;

        public PlanSlot? Chosen => slots is { } s && sel >= 0 && sel < s.Count ? s[sel] : null;

        private Rail(FrameworkElement at, string id, string title, Engine.Engine engine, RailKind kind, Action? planned)
        {
            this.at = at;
            this.id = id;
            this.title = title;
            this.engine = engine;
            this.planned = planned;
            Kind = kind;

            var head = K.Text($"Plan {title}", Ty.BodyStrong);
            head.TextTrimming = TextTrimming.CharacterEllipsis;
            lengths = new SelectorBar(Lengths.Select(LengthWord).ToList(), 1) { VerticalAlignment = VerticalAlignment.Center, ToolTip = "How long to set aside" };
            lengths.Changed += i => { mins = Lengths[i]; slots = null; Paint(); _ = Load(mins); };
            lengths.IsEnabled = kind == RailKind.Pick;
            var top = K.Columns("auto, *, auto, auto",
                K.Icon(SymbolRegular.Clock24, 16, Support.Appearance.Accent).Margin(0, 0, 8, 0).Col(0),
                head.Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1),
                lengths.Margin(8, -4, 0, -4).Col(2),
                clearHost.Margin(6, 0, 0, 0).Col(3));
            status = K.Text("Finding free time…", Ty.Caption, B.Text2);
            status.TextTrimming = TextTrimming.CharacterEllipsis;
            var content = K.V(10, top, railHost, status);
            content.Margin = new Thickness(14);

            var width = Math.Clamp(at.ActualWidth, 420, 560);
            var shade = new Border { CornerRadius = new CornerRadius(8), Effect = K.Shadow(24, 6, 0.22) }.Bg(B.Flyout);
            var face = new Border { CornerRadius = new CornerRadius(8), Child = content, SnapsToDevicePixels = true }.Bg(B.Flyout).Stroke(B.FlyoutStroke);
            surface = new Grid { Width = width, Margin = new Thickness(12, 4, 12, 16), Children = { shade, face }, Focusable = kind == RailKind.Pick };
            surface.SetResourceReference(TextElement.ForegroundProperty, B.Text);
            AutomationProperties.SetName(surface, $"Plan {title}");
            surface.MouseLeftButtonDown += (_, e) => e.Handled = true;
            surface.PreviewKeyDown += OnKey;
            FocusVisualStyleOff(surface);

            popup = new Popup
            {
                Child = surface, AllowsTransparency = true, StaysOpen = kind == RailKind.Pull, Focusable = kind == RailKind.Pick,
                PopupAnimation = PopupAnimation.None, PlacementTarget = at, Placement = PlacementMode.Bottom, HorizontalOffset = -12,
            };
            popup.Closed += (_, _) => { if (ReferenceEquals(shown, this)) shown = null; };
        }

        private static void FocusVisualStyleOff(FrameworkElement e) => e.FocusVisualStyle = null;

        public static Rail Open(FrameworkElement at, string id, string title, Engine.Engine engine, RailKind kind, Action? planned)
        {
            shown?.Close();
            var r = new Rail(at, id, title, engine, kind, planned);
            shown = r;
            r.Paint();
            r.popup.IsOpen = true;
            if (Motion.On)
            {
                // (a flyout arriving: up the last few pixels into place as it fades in)
                var rise = new TranslateTransform(0, 8);
                r.surface.RenderTransform = rise;
                r.surface.Opacity = 0;
                Motion.To(rise, TranslateTransform.YProperty, 0, Motion.Normal, Motion.Decelerate);
                Motion.To(r.surface, UIElement.OpacityProperty, 1, Motion.Normal, Motion.Decelerate);
            }
            if (kind == RailKind.Pick) r.surface.Dispatcher.BeginInvoke(() => r.surface.Focus(), System.Windows.Threading.DispatcherPriority.Input);
            Support.Shot.Moved();
            _ = r.Load(null);
            return r;
        }

        public void Hold() => popup.StaysOpen = true;

        public void Close()
        {
            popup.IsOpen = false;
            if (ReferenceEquals(shown, this)) shown = null;
        }

        private async Task Load(int? want)
        {
            var mine = ++asked;
            PlanSlots? r = null;
            Support.Shot.Began();
            try { r = await engine.Call<PlanSlots>("planSlots", new { id, mins = want }); } catch { }
            finally { Support.Shot.Ended(); }
            if (mine != asked) return;
            error = r?.Error ?? (r == null ? "The free times could not be found." : null);
            slots = r?.Slots ?? new List<PlanSlot>();
            mins = r?.Mins ?? mins;
            plannedAt = r?.Planned;
            dueWords = r?.DueWords ?? "";
            var li = Array.IndexOf(Lengths, mins);
            if (li >= 0) lengths.Select(li, false);
            if (Kind == RailKind.Pick)
            {
                sel = slots.FindIndex(s => s.At == plannedAt);
                if (sel < 0) sel = slots.Count > 0 ? 0 : -1;
            }
            else Follow();
            Paint();
        }

        /// <summary>In the pull: the time under the lens, from how far the row has gone.</summary>
        public void Pulled(double px)
        {
            pulled = px;
            var was = sel;
            Follow();
            if (sel != was) Move();
            Status();
        }

        private void Follow()
        {
            var n = slots?.Count ?? 0;
            if (n == 0) { sel = -1; return; }
            var f = Math.Min(n - 1, Math.Max(-1, (pulled - Lead) / Step));
            sel = f < -0.5 ? -1 : (int)Math.Round(f);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (Kind != RailKind.Pick) return;
            var n = slots?.Count ?? 0;
            switch (e.Key)
            {
                case Key.Left when n > 0:
                    sel = Math.Max(0, sel - 1);
                    Move();
                    break;
                case Key.Right when n > 0:
                    sel = Math.Min(n - 1, sel + 1);
                    Move();
                    break;
                case Key.Up:
                case Key.Down:
                {
                    var i = Math.Clamp(Array.IndexOf(Lengths, mins) + (e.Key == Key.Up ? 1 : -1), 0, Lengths.Length - 1);
                    if (Lengths[i] == mins) break;
                    lengths.Select(i, true);
                    break;
                }
                case Key.Enter:
                    if (Chosen is { } s) Plan(s);
                    break;
                case Key.Escape:
                    Close();
                    at.Focus();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        public async void Plan(PlanSlot s)
        {
            Close();
            Support.Shot.Began();
            try
            {
                if (await engine.Act("planPut", new { id, at = s.At, mins }))
                {
                    planned?.Invoke();
                    engine.DataChanged();
                }
            }
            finally { Support.Shot.Ended(); }
        }

        // ---- drawing ----

        private void Paint()
        {
            clearHost.Child = null;
            if (Kind == RailKind.Pick && plannedAt != null)
            {
                var clear = K.Button("Clear", () => { Close(); PlanRail.Clear(id, engine, planned); });
                clear.MinWidth = 0;
                clear.ToolTip = "Clear this plan";
                clearHost.Child = clear;
            }
            chips.Clear();
            chipButtons.Clear();
            strip = null;
            scroller = null;
            if (slots == null)
            {
                railHost.Child = K.H(8, K.Spinner(16), K.Text("Finding free time…", Ty.Body, B.Text2)).Align(HorizontalAlignment.Left, VerticalAlignment.Center);
                Status();
                return;
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            lefts.Clear();
            var left = 0.0;
            for (var i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                // (0.3.62) the day said once, over the first of its times, and a little more room before each new day:
                // the times read as a day's handful, not "Today" four times over
                var newDay = i == 0 || slots[i - 1].Day != s.Day;
                var lead = newDay && i > 0 ? DayGap : 0;
                left += lead;
                lefts.Add(left);
                left += Chip + Gap;
                var face = ChipFace(s, newDay);
                chips.Add(face);
                if (Kind == RailKind.Pick)
                {
                    var n = i;
                    var b = K.Subtle(face, () => Plan(s), new Thickness(0), 12);
                    b.Margin = new Thickness(lead, 0, Gap, 0);
                    b.ToolTip = $"{s.Day} {s.Time}, {LengthWord(mins)}";
                    b.GotKeyboardFocus += (_, _) => { if (sel != n) { sel = n; Move(); } };
                    AutomationProperties.SetName(b, $"{s.Day} {s.Time}");
                    chipButtons.Add(b);
                    row.Children.Add(b);
                }
                else
                {
                    face.Margin = new Thickness(lead, 0, Gap, 0);
                    row.Children.Add(face);
                }
            }
            if (!string.IsNullOrEmpty(dueWords))
            {
                var dueText = K.Text(dueWords!, Ty.Caption).Strong();
                dueText.Foreground = K.Brush(K.Tone("red"));
                dueText.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(new Border
                {
                    Child = dueText, Padding = new Thickness(12, 0, 12, 0), Height = 50,
                    CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5),
                    BorderBrush = K.Brush(K.WithAlpha(K.Tone("red"), 0.45)),
                });
            }
            if (Kind == RailKind.Pull)
            {
                // the time chosen slides into the lens (the second place along: the time before it still in view)
                row.RenderTransform = stripShift;
                strip = row;
                var lens = new Border
                {
                    Width = Chip, Height = 50, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(Chip + Gap, 0, 0, 0), IsHitTestVisible = false,
                };
                railHost.Child = new Grid { Children = { row, lens } };
                Move(animate: false);
            }
            else
            {
                scroller = new ScrollViewer
                {
                    Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Focusable = false, PanningMode = PanningMode.HorizontalOnly,
                };
                scroller.PreviewMouseWheel += (_, e) => { scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta); e.Handled = true; };
                railHost.Child = scroller;
                Move(animate: false);
            }
            Status();
        }

        private Border ChipFace(PlanSlot s, bool newDay)
        {
            var day = K.Text(s.Day.ToUpperInvariant(), Ty.Caption);
            day.FontSize = 10.5;
            day.FontWeight = FontWeights.SemiBold;
            if (!newDay) day.Visibility = Visibility.Hidden; // (its line kept, so every time sits as low)
            var time = K.Text(s.Time, Ty.Body);
            time.FontSize = 14;
            time.FontWeight = FontWeights.Bold;
            Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
            var face = new Border
            {
                Width = Chip, Height = 50, CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 0, 8, 0),
                Child = K.V(2, day, time).Align(HorizontalAlignment.Left, VerticalAlignment.Center), BorderThickness = new Thickness(1.5),
                Tag = newDay,
            };
            return face;
        }

        /// <summary>The chips lit as the choice stands (the one chosen on the accent; a new day's name in it), and the
        /// rail moved to it.</summary>
        private void Move(bool animate = true)
        {
            var accent = Support.Appearance.Accent;
            for (var i = 0; i < chips.Count; i++)
            {
                var c = chips[i];
                var on = i == sel;
                var s = slots![i];
                c.Background = on ? K.Brush(accent) : K.Brush(Color.FromArgb(Support.Appearance.Dark ? (byte)26 : (byte)16, 0x80, 0x80, 0x80));
                c.BorderBrush = s.At == plannedAt && !on ? K.Brush(accent) : Brushes.Transparent;
                if (c.Child is Stack v && v.Children.Count == 2 && v.Children[0] is TextBlock day && v.Children[1] is TextBlock time)
                {
                    if (on) { day.Foreground = K.Brush(Color.FromArgb(0xd9, 0xff, 0xff, 0xff)); time.Foreground = Brushes.White; }
                    else
                    {
                        if (c.Tag is true) day.Foreground = K.Brush(K.Darkened(accent)); else day.SetResourceReference(TextBlock.ForegroundProperty, B.Text2);
                        time.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
                    }
                }
            }
            if (strip != null && railHost.Child is Grid g && g.Children.Count > 1 && g.Children[1] is Border lens)
            {
                lens.BorderBrush = sel >= 0 ? K.Brush(accent) : K.Brush(K.WithAlpha(K.Tone("gray"), 0.5));
                lens.BorderThickness = new Thickness(2);
                // (the chosen one in the second place along; before any is, the first one place further on)
                var x = sel < 0 || sel >= lefts.Count ? 2 * (Chip + Gap) : Chip + Gap - lefts[sel];
                if (animate && Motion.On) Motion.To(stripShift, TranslateTransform.XProperty, x, 160, Motion.Out);
                else { stripShift.BeginAnimation(TranslateTransform.XProperty, null); stripShift.X = x; }
            }
            if (scroller != null && sel >= 0 && sel < chipButtons.Count)
            {
                var b = chipButtons[sel];
                b.Dispatcher.BeginInvoke(() =>
                {
                    if (!b.IsLoaded) return;
                    var left = b.TranslatePoint(new Point(0, 0), (UIElement)scroller.Content).X;
                    scroller.ScrollToHorizontalOffset(Math.Max(0, left - (scroller.ViewportWidth - Chip) / 2));
                    if (surface.IsKeyboardFocusWithin && !b.IsKeyboardFocused) b.Focus();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            Status();
        }

        private void Status()
        {
            string words;
            var lit = false;
            if (error != null) words = error;
            else if (slots == null) words = "Finding free time…";
            else if (slots.Count == 0) words = string.IsNullOrEmpty(dueWords) || dueWords == "Past due" ? "No free time in the week ahead." : "No free time left before it is due.";
            else if (Kind == RailKind.Pull)
            {
                if (Chosen is { } c) { words = $"Let go to plan {c.Day} {c.Time}"; lit = true; }
                else words = "Pull further to choose a time";
            }
            else words = "← → to choose · ↑ ↓ for how long · Enter to plan";
            status.Text = words;
            if (lit) status.Foreground = K.Brush(K.Darkened(Support.Appearance.Accent)); else status.SetResourceReference(TextBlock.ForegroundProperty, B.Text2);
        }
    }

    /// <summary>
    /// (0.3.62) What a pull uncovers behind its row (the web's and the Mac's): a rounded well in the accent with a clock in
    /// it, the clock growing with the pull and filling once a time is chosen — so a pull says what it does before it is let
    /// go. Drawn on the row's adorner layer in the room the row has moved out of, kept to it as the row slides back.
    /// </summary>
    private sealed class PullReveal : Adorner
    {
        private readonly TranslateTransform shift;
        private readonly AdornerLayer layer;
        private bool chosen;

        private PullReveal(UIElement row, TranslateTransform shift, AdornerLayer layer) : base(row)
        {
            this.shift = shift;
            this.layer = layer;
            IsHitTestVisible = false;
            shift.Changed += Moved;
        }

        public static PullReveal? On(UIElement row, TranslateTransform shift)
        {
            if (AdornerLayer.GetAdornerLayer(row) is not { } layer) return null;
            var r = new PullReveal(row, shift, layer);
            layer.Add(r);
            return r;
        }

        public void Chose(bool on)
        {
            if (on == chosen) return;
            chosen = on;
            InvalidateVisual();
        }

        /// <summary>Drawn again each frame while the row slides back over it.</summary>
        public void Follow() => CompositionTarget.Rendering += Moved;

        public void Remove()
        {
            shift.Changed -= Moved;
            CompositionTarget.Rendering -= Moved;
            layer.Remove(this);
        }

        private void Moved(object? sender, EventArgs e) => InvalidateVisual();

        protected override void OnRender(DrawingContext dc)
        {
            var w = shift.X;
            var h = AdornedElement.RenderSize.Height;
            if (w < 10 || h < 16) return;
            var k = Math.Clamp(w / Most, 0, 1);
            var accent = Support.Appearance.Accent;
            // (the row has moved w to the right, and this layer with it: the room it left is left of 0)
            var well = new Rect(-w + 3, 3, w - 7, h - 6);
            dc.DrawRoundedRectangle(K.Brush(K.WithAlpha(accent, 0.1 + 0.14 * k)), null, well, 10, 10);
            var r = Math.Min(9.5, well.Height / 2 - 3) * (0.6 + 0.4 * k);
            if (r < 4 || well.Width < 2 * r + 4) return;
            var c = new Point(well.Left + well.Width / 2, well.Top + well.Height / 2);
            var ink = K.Brush(K.WithAlpha(accent, 0.45 + 0.55 * k));
            var face = new Pen(chosen ? Brushes.White : ink, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (chosen) dc.DrawEllipse(K.Brush(accent), null, c, r + 1, r + 1);
            else dc.DrawEllipse(null, face, c, r, r);
            dc.DrawLine(face, c, new Point(c.X, c.Y - r * 0.55));
            dc.DrawLine(face, c, new Point(c.X + r * 0.42, c.Y + r * 0.18));
        }
    }
}
