using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Simpl.Kit;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>The Alta popup's own pieces (the Mac's AltaRail, AltaTile, AltaObjectiveRow, AltaMasteryBar, AltaStartScreen, AltaSkeleton).</summary>
internal static class AltaParts
{
    public static int Percent(double v) => (int)Math.Round(v * 100, MidpointRounding.AwayFromZero);

    /// <summary>(0.3.5) A button of Alta's pop-up in the words Windows writes it in: Alta's in capitals ("GOT IT") in sentence
    /// case ("Got it"), the rest — "OK", "Not now" — as they are.</summary>
    public static string ButtonWords(string words) => words.Any(char.IsLower) || words.Count(char.IsLetter) <= 3 ? words : Capital(words);

    // ---- the mastery bar (where the quiz has its progress bar): Windows 11's progress bar, filling to its value ----

    /// <summary>The mastery bar: Windows 11's thin track, the accent's rounded bar over it — growing from where it was.</summary>
    public static FrameworkElement Bar(double value, double? from, double height)
    {
        var g = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        var track = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true }.Bg(B.StrongStroke);
        var fill = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left }.Bg(B.Accent);
        g.Children.Add(track);
        g.Children.Add(fill);
        var f = Math.Clamp(value, 0, 1);
        var f0 = Math.Clamp(from ?? f, 0, 1);
        var grown = false;
        g.SizeChanged += (_, e) =>
        {
            double W(double x) => Math.Max(x > 0 ? height : 0, e.NewSize.Width * x);
            if (!grown && Math.Abs(f - f0) > 0.0005)
            {
                grown = true;
                Motion.FromTo(fill, FrameworkElement.WidthProperty, W(f0), W(f), 600, Motion.Decelerate);
                return;
            }
            grown = true;
            fill.BeginAnimation(FrameworkElement.WidthProperty, null);
            fill.Width = W(f);
        };
        System.Windows.Automation.AutomationProperties.SetName(g, $"Mastery, {Percent(f)} percent");
        return g;
    }

    // ---- the rail ----

    /// <summary>
    /// Down the left: the objectives as the quiz's question tiles (how many are mastered beside them), each objective by
    /// name with its own bar, the total mastery, and — once a question is on — Alta's estimate of the questions its
    /// objective takes. Bars and tiles fill from where they were last drawn.
    /// </summary>
    public static FrameworkElement Rail(List<AltaObjective> list, double mastery, (double Low, double High)? estimate, bool reported, Dictionary<string, double> was)
    {
        var v = K.V(18);
        v.Children.Add(ObjectivesCard(list, was));
        var rows = K.V(12);
        foreach (var o in list) rows.Children.Add(ObjectiveRow(o, was.TryGetValue(o.Id, out var w) ? w : null));
        if (list.Count > 0) v.Children.Add(rows);
        v.Children.Add(K.Divider());
        v.Children.Add(MasteryBlock(list, mastery, was.TryGetValue("", out var m) ? m : null));
        if (reported && estimate is { } e)
        {
            int low = (int)e.Low, high = (int)e.High;
            var words = low == high ? $"About {low} questions on this objective" : $"About {low}–{high} questions on this objective";
            v.Children.Add(K.Columns("auto, *",
                K.Icon(SymbolRegular.TextNumberListLtr24, 14, B.Text2).Margin(0, 1, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0),
                K.Wrapped(words, Ty.Caption, B.Text2).Col(1)));
        }
        v.Margin = new Thickness(20, 4, 16, 24);
        return v;
    }

    /// <summary>The objectives as tiles (how many are mastered is said once, under the mastery bar).</summary>
    private static FrameworkElement ObjectivesCard(List<AltaObjective> list, Dictionary<string, double> was)
    {
        var head = K.Text("Objectives", Ty.BodyStrong);
        FrameworkElement body;
        if (list.Count == 0) body = new Grid { MinHeight = 32, Children = { K.Spinner(16) } };
        else
        {
            var tiles = new UniformGrid { Columns = 5, Margin = new Thickness(-3) };
            foreach (var o in list) tiles.Children.Add(Tile(o, was.TryGetValue(o.Id, out var w) ? w : null).Margin(3));
            body = tiles;
        }
        return K.Card(K.V(12, head, body), new Thickness(14));
    }

    /// <summary>One objective's tile: its number, the one being worked on filled, a mastered one washed with a tick, one
    /// under way filled from the bottom as far as it is mastered. Not a button: Alta chooses what comes next.</summary>
    private static FrameworkElement Tile(AltaObjective o, double? from)
    {
        var accent = AltaLook.Accent;
        var tile = new Grid { Height = 32, ClipToBounds = true };
        var face = new Border { CornerRadius = new CornerRadius(4) };
        if (o.Current) face.Bg(B.Accent);
        else if (o.Mastered) face.Background = K.Brush(K.WithAlpha(accent, 0.18));
        else face.Bg(B.Subtle);
        tile.Children.Add(face);
        if (!o.Current && !o.Mastered && o.Mastery > 0)
        {
            var rise = new Border { VerticalAlignment = VerticalAlignment.Bottom, Background = K.Brush(K.WithAlpha(accent, 0.16)) };
            tile.Children.Add(rise);
            var f = o.Mastery;
            var f0 = Math.Clamp(from ?? f, 0, 1);
            tile.SizeChanged += (_, e) =>
            {
                if (Math.Abs(f - f0) > 0.0005) { Motion.FromTo(rise, FrameworkElement.HeightProperty, e.NewSize.Height * f0, e.NewSize.Height * f, 600, Motion.Decelerate); f0 = f; }
                else { rise.BeginAnimation(FrameworkElement.HeightProperty, null); rise.Height = e.NewSize.Height * f; }
            };
            tile.Clip = null;
            tile.SizeChanged += (_, e) => tile.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 4, 4);
        }
        if (o.Mastered && !o.Current) tile.Children.Add(K.Icon(SymbolRegular.Checkmark24, 14, B.AccentText));
        else
        {
            var n = K.Text(o.Number.ToString(CultureInfo.InvariantCulture), Ty.Body, o.Current ? B.OnAccent : B.Text2).Strong();
            n.HorizontalAlignment = HorizontalAlignment.Center;
            n.VerticalAlignment = VerticalAlignment.Center;
            tile.Children.Add(n);
        }
        var pct = Percent(o.Mastery);
        tile.ToolTip = $"{o.Name} — {pct}% mastered{(o.Current ? ", working on it now" : "")}";
        System.Windows.Automation.AutomationProperties.SetName(tile, $"Objective {o.Number}, {o.Name}, {pct} percent mastered{(o.Current ? ", working on it now" : "")}");
        return tile;
    }

    /// <summary>An objective by name, with its own bar.</summary>
    private static FrameworkElement ObjectiveRow(AltaObjective o, double? from)
    {
        var number = K.Text(o.Number.ToString(CultureInfo.InvariantCulture), Ty.Caption, o.Current ? B.AccentText : B.Text2).Strong();
        number.Margin = new Thickness(0, 2, 0, 0);
        var name = K.Wrapped(o.Name, Ty.Body, o.Current ? B.Text : B.Text2, maxLines: 2);
        if (o.Current) name.FontWeight = FontWeights.SemiBold;
        var pct = K.Text($"{Percent(o.Mastery)}%", Ty.Caption, B.Text2).Margin(8, 2, 0, 0);
        var head = K.Columns("24, *, auto", number.Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), name.Col(1), pct.Align(HorizontalAlignment.Right, VerticalAlignment.Top).Col(2));
        return K.V(6, head, Bar(o.Mastery, from, 4).Margin(24, 0, 0, 0));
    }

    private static FrameworkElement MasteryBlock(List<AltaObjective> list, double m, double? from)
    {
        var done = list.Count(o => o.Mastered);
        var head = K.Columns("*, auto", K.Text("Mastery", Ty.BodyStrong).Col(0), K.Text($"{Percent(m)}%", Ty.BodyStrong).Col(1));
        var line = K.Text(list.Count == 0 ? "Toward this assignment" : $"{done} of {list.Count} objectives mastered", Ty.Caption, B.Text2);
        return K.V(10, head, Bar(m, from, 6), line);
    }

    // ---- (1.3.59) the start screen ----

    /// <summary>
    /// The assignment's start screen, saying what the header and the rail do not: a card with its status and grade, its
    /// note (the extra credit for finishing early), a large Start (named as Alta names it — Start, Continue, Resume) and
    /// Review instruction; under it what the work covers — each topic with Alta's estimate of its questions and its
    /// objectives by name. The name and the due date are the header's; each objective's mastery is the rail's.
    /// </summary>
    public static FrameworkElement StartScreen(AltaCover cover, bool going, Action start, Action review)
    {
        var v = K.V(18, StartHeader(cover, going, start, review));
        if (cover.Topics.Count > 0) v.Children.Add(StartObjectives(cover));
        v.MaxWidth = 780;
        v.Margin = new Thickness(32, 28, 32, 28);
        v.HorizontalAlignment = HorizontalAlignment.Stretch;
        var center = new Grid { Children = { v } };
        return new ScrollViewer { Content = center, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    }

    public static string StartLabel(AltaCover cover)
    {
        var t = cover.Start.Trim();
        return t.Length == 0 ? "Start" : Capital(t);
    }

    /// <summary>A label of Alta's ("STATUS", "START ASSIGNMENT") in Windows' sentence case: "Status", "Start assignment".</summary>
    internal static string Capital(string s)
    {
        var t = s.Trim();
        if (t.Length == 0) return t;
        var lower = t.ToLower(CultureInfo.CurrentCulture);
        return char.ToUpper(lower[0], CultureInfo.CurrentCulture) + lower[1..];
    }

    private static FrameworkElement StartHeader(AltaCover cover, bool going, Action start, Action review)
    {
        var v = K.V(16);
        // (its status and grade: its due date is the header's, already)
        var shown = cover.Details.Where(d => !d.Label.Contains("due", StringComparison.OrdinalIgnoreCase)).ToList();
        if (shown.Count > 0)
        {
            var details = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var d in shown)
            {
                // (its label in Windows' sentence case — "Status", "Grade" — not Alta's capitals; its times as every
                //  screen writes them: "Last updated Oct 10 at 8:08 PM")
                var col = K.V(3, K.Text(Capital(d.Label), Ty.Caption, B.Text2).Strong(), K.Wrapped(AltaLook.Times(d.Value), Ty.BodyStrong));
                if (!string.IsNullOrEmpty(d.Sub)) col.Children.Add(K.Wrapped(AltaLook.Times(d.Sub!), Ty.Caption, B.Text2));
                col.Margin = new Thickness(0, 0, 28, 6);
                col.MaxWidth = 260;
                details.Children.Add(col);
            }
            v.Children.Add(details);
        }
        if (!string.IsNullOrEmpty(cover.Note))
        {
            var orange = K.Darkened(K.Tone("orange"));
            var note = K.Wrapped(AltaLook.Times(cover.Note), Ty.Body);
            note.Foreground = K.Brush(orange);
            v.Children.Add(K.Columns("auto, *", K.Icon(SymbolRegular.Sparkle24, 16, orange).Margin(0, 2, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), note.Col(1)));
        }
        var label = StartLabel(cover);
        var words = new TextBlock { Text = label, FontSize = 16, FontWeight = FontWeights.SemiBold, FontFamily = K.TextFace, VerticalAlignment = VerticalAlignment.Center };
        UIElement mark = going ? K.Spinner(16) : new SymbolIcon { Symbol = SymbolRegular.Play24, Filled = true, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        var go = new Wpf.Ui.Controls.Button
        {
            Content = K.H(8, mark, words), Appearance = ControlAppearance.Primary, MinWidth = 150, MinHeight = 40, Padding = new Thickness(20, 6, 20, 7),
            IsEnabled = !going, ToolTip = $"{label} the assignment (Enter)",
        };
        go.Click += (_, _) => start();
        var row = K.H(12, go);
        if (cover.Review)
        {
            var r = K.Button("Review instruction", review, icon: SymbolRegular.Book24);
            r.MinHeight = 40;
            r.ToolTip = "Alta’s instruction for these objectives";
            row.Children.Add(r);
        }
        row.Margin = new Thickness(0, 4, 0, 0);
        v.Children.Add(row);
        return K.Card(v, new Thickness(24));
    }

    /// <summary>What the work covers: each topic with Alta's estimate of its questions, and its objectives by name (their
    /// mastery is the rail's, beside it).</summary>
    private static FrameworkElement StartObjectives(AltaCover cover)
    {
        var v = K.V(14, K.Text("What it covers", Ty.Subtitle));
        for (var k = 0; k < cover.Topics.Count; k++)
        {
            var t = cover.Topics[k];
            if (k > 0) v.Children.Add(K.Divider());
            var topic = K.V(10);
            var head = K.Columns("*, auto", K.Wrapped(t.Name, Ty.BodyStrong).Col(0));
            if (!string.IsNullOrEmpty(t.Estimate))
            {
                var pill = new Border { Child = K.Text(t.Estimate, Ty.Caption, B.Text2), Padding = new Thickness(9, 2, 9, 3), CornerRadius = new CornerRadius(10), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                pill.Bg(B.Subtle);
                head.Children.Add(pill.Col(1));
            }
            topic.Children.Add(head);
            foreach (var o in t.Objectives)
                topic.Children.Add(K.Columns("auto, *",
                    K.Icon(SymbolRegular.Target24, 14, B.Text2).Margin(0, 2, 10, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0),
                    K.Wrapped(o.Name, Ty.Body, B.Text2).Col(1)));
            v.Children.Add(topic);
        }
        return K.Card(v, new Thickness(22));
    }

    // ---- (1.3.68) the skeleton ----

    /// <summary>
    /// While Alta's page is out of sight (loading, or between one part and the next): the question's shape — a panel for
    /// what it asks and one for its answer, their lines pulsing gently — where the question will be.
    /// </summary>
    public static FrameworkElement Skeleton()
    {
        static Border Line(double? width, double height)
        {
            var b = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = width == null ? HorizontalAlignment.Stretch : HorizontalAlignment.Left };
            if (width is { } w) b.Width = w;
            return b.Bg(B.Subtle);
        }
        var block = new Border { Height = 64, CornerRadius = new CornerRadius(8) }.Bg(B.Subtle);
        var asks = K.Card(K.V(18, Line(150, 22), K.V(10, Line(null, 13), Line(null, 13), Line(260, 13))), new Thickness(24));
        var answer = K.Card(K.V(18, Line(190, 14), block), new Thickness(24));
        var v = K.V(32, asks, answer);
        v.MaxWidth = 860;
        v.Margin = new Thickness(40, 34, 40, 0);
        v.VerticalAlignment = VerticalAlignment.Top;
        System.Windows.Automation.AutomationProperties.SetName(v, "Loading the question");
        var g = new Grid { Children = { v } };
        // (pulsing while it shows, unless Windows' animation effects are off)
        g.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && Motion.On)
                v.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.55, Motion.Ms(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            else v.BeginAnimation(UIElement.OpacityProperty, null);
        };
        return g;
    }
}
