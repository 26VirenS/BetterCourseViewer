using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>The words of Silent mode, as the web's (dashboard.js silentDay, silentWords) and the Mac's.</summary>
public static class SilentWords
{
    private static readonly System.Globalization.CultureInfo En = System.Globalization.CultureInfo.GetCultureInfo("en-US");

    /// <summary>"11:59 PM"</summary>
    public static string Time(DateTime d) => d.ToString("h:mm tt", En);

    /// <summary>"Today", "Tomorrow", "Friday" (within the week), else "November 3" (or the brief forms: "Fri", "Nov 3").</summary>
    public static string Day(DateTime d, DateTime at, bool brief = false)
    {
        var n = (d.Date - at.Date).Days;
        if (n == 0) return "Today";
        if (n == 1) return "Tomorrow";
        if (n < 7) return d.ToString(brief ? "ddd" : "dddd", En);
        return d.ToString(brief ? "MMM d" : "MMMM d", En);
    }

    /// <summary>The next one's line: how soon, in words that count down within the day ("Due in 4 h 40 min"; an event
    /// "Starts…", a task of one's own just "In…"), then the day and time when that is not already said, and its course.</summary>
    public static (string Lead, List<string> After) Next(DashSilentRow r, DateTime at)
    {
        var left = (r.When - at).TotalSeconds;
        var mins = (int)(left % 3600 / 60);
        string soon;
        if (left < 3600)
        {
            var m = Math.Max(1, (int)Math.Round(left / 60, MidpointRounding.AwayFromZero));
            soon = $"in {m} {(m == 1 ? "minute" : "minutes")}";
        }
        else if (left < 86400) soon = $"in {(int)(left / 3600)} h" + (mins > 0 ? $" {mins} min" : "");
        else soon = "";
        var stamp = $"{Day(r.When, at)} at {Time(r.When)}";
        var verb = r.Verb ?? "";
        string lead;
        if (verb.Length > 0)
        {
            var said = stamp.StartsWith("Today", StringComparison.Ordinal) || stamp.StartsWith("Tomorrow", StringComparison.Ordinal) ? char.ToLowerInvariant(stamp[0]) + stamp[1..] : stamp;
            lead = $"{verb} {(soon.Length == 0 ? said : soon)}";
        }
        else lead = soon.Length == 0 ? stamp : "I" + soon[1..];
        var rest = new List<string>();
        if (soon.Length > 0) rest.Add(stamp);
        if (!string.IsNullOrEmpty(r.Course)) rest.Add(r.Course!);
        return (lead, rest);
    }

    /// <summary>A row's day and time: "Tomorrow, 9:00 AM", "Fri, 11:59 PM".</summary>
    public static string Row(DashSilentRow r, DateTime at) => $"{Day(r.When, at, brief: true)}, {Time(r.When)}";
}

/// <summary>
/// (0.3.84) Silent mode, as the web Dashboard (2.99.125–126) and the Mac (1.3.190) have it: the Dashboard with nothing
/// on it but what comes next — black, "Next" and the next piece of work (or event, or task of one's own) in large type,
/// how soon it is, its day and its course, then the four after it under "Then". No greeting, no counters, no cards; the
/// page's header goes (<see cref="Bare"/>) and the pane goes black, in words alone (AppShell.SilentLook). A press on an
/// item's words strikes it out (marked done): a line drawn through them and the item dimmed, still listed while Silent
/// stays up, to put back with a press. Open, beside the words, opens it. Exit Silent Mode, at the top right, brings the
/// whole Dashboard back. Kept on this PC (<see cref="SilentMode"/>).
/// </summary>
public sealed partial class DashboardScreen
{
    private Loader<DashSilentData> silentRead = null!;
    /// <summary>Struck out (or put back) here: listed so, whatever the next read says, while Silent stays up.</summary>
    private readonly Dictionary<string, DashSilentRow> struck = new();
    /// <summary>Its words count down: drawn again each half minute while it shows, when they say something else.</summary>
    private DispatcherTimer? silentClock;
    private string? silentSaid;
    /// <summary>Its pieces have come in once (each rises a little into place, one after another).</summary>
    private bool silentEntered;
    /// <summary>The item whose words had the keyboard, given it again in the next build; and the one that has it now.</summary>
    private string? silentFocus, silentFocused;

    /// <summary>How many come after the next one.</summary>
    private const int SilentMore = 4;

    /// <summary>The Dashboard shows Silent mode.</summary>
    public bool Silent => SilentMode.Shown;

    public override bool Bare => Silent;

    public override bool Scrolls => !Silent;

    private static readonly Color Ink = Colors.White;
    private static Color White(double a) => Color.FromArgb((byte)Math.Round(a * 255), 255, 255, 255);

    /// <summary>Silent mode chosen or put back (or put by for the tour and Appearance): the page and the shell's look
    /// with it.</summary>
    private void OnSilent()
    {
        if (openKey != null) SetOpen(null);
        if (!Silent) struck.Clear();
        silentEntered = false;
        silentSaid = null;
        ChecksForView();
        Refresh();
        HeaderChange();
        Render();
    }

    public override void Release()
    {
        SilentMode.Changed -= OnSilent;
        silentClock?.Stop();
        base.Release();
    }

    /// <summary>What Silent mode reads: what is still ahead.</summary>
    private void RefreshSilent() => silentRead.Ensure(engine, "dashSilent");

    /// <summary>What is still ahead now, soonest first: the last read's, with what was struck out or put back here.</summary>
    private List<DashSilentRow> SilentAhead(DateTime at)
    {
        var rows = (silentRead.Data?.Rows ?? new List<DashSilentRow>()).Where(r => !struck.ContainsKey(r.Id)).ToList();
        rows.AddRange(struck.Values);
        return rows.Where(r => r.When >= at).OrderBy(r => r.At).ToList();
    }

    /// <summary>Everything the page says now, in words: drawn again when this changes (the countdown).</summary>
    private string SilentWordsNow(DateTime at)
    {
        var ahead = SilentAhead(at);
        if (ahead.Count == 0) return silentRead.Data == null ? "?" : "none";
        var (lead, rest) = SilentWords.Next(ahead[0], at);
        return string.Join("\u001f", ahead.Take(SilentMore + 1).Select(r => r.Id + (r.Done ? "+" : "-") + SilentWords.Row(r, at)).Append(lead).Concat(rest));
    }

    // ---- the page ----

    private FrameworkElement SilentPage()
    {
        var at = DateTime.Now;
        silentSaid = SilentWordsNow(at);
        var w = Math.Max(320, Width + 80);
        double Clamp(double v, double lo, double hi) => Math.Min(Math.Max(v, lo), hi);
        var title = Clamp(w * 0.076, 44, 96);
        var quiet = Clamp(w * 0.046, 30, 52);
        var whenSize = Clamp(w * 0.023, 18, 26);
        var rowTitle = Clamp(w * 0.029, 22, 34);
        var rowWhen = Clamp(w * 0.017, 15, 20);
        var pad = Clamp(w * 0.07, 32, 128);
        var room = Math.Max(200, Math.Min(1100, w - 2 * pad));

        var inner = new StackPanel { Orientation = Orientation.Vertical, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var pieces = new List<FrameworkElement>();
        void Piece(FrameworkElement e) { inner.Children.Add(e); pieces.Add(e); }
        var ahead = SilentAhead(at);
        var buttons = new Dictionary<string, Button>();
        if (silentRead.Data == null && silentRead.Error != null)
            Piece(Words(silentRead.Error!, quiet, FontWeights.Bold, 3, White(0.7)).Text);
        else if (silentRead.Data != null && ahead.Count == 0)
        {
            Piece(Label("Next"));
            Piece(Words("Nothing coming up.", title, FontWeights.Bold, 2, Ink).Text);
            var none = Line(silentRead.Data.Empty ?? "Nothing is due in the next three weeks.", whenSize, White(0.48));
            none.Margin = new Thickness(0, 22, 0, 0);
            Piece(none);
        }
        else if (ahead.Count > 0)
        {
            var next = ahead[0];
            Piece(Label("Next"));
            var (lead, rest) = SilentWords.Next(next, at);
            var words = Words(next.Title, title, FontWeights.Bold, 3, next.Done ? White(0.34) : Ink, next.Done);
            var line = new TextBlock { FontFamily = K.TextFace, FontSize = whenSize, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap, MaxWidth = Math.Max(160, room - 80) };
            var leadInk = new SolidColorBrush(next.Done ? White(0.26) : Ink);
            var restInk = new SolidColorBrush(White(next.Done ? 0.26 : 0.48));
            line.Inlines.Add(new Run(lead) { Foreground = leadInk });
            foreach (var r in rest) line.Inlines.Add(new Run($" · {r}") { Foreground = restInk });
            var open = OpenButton(next);
            var whenRow = new Grid { Margin = new Thickness(0, 22, 0, 0) };
            whenRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            whenRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            whenRow.Children.Add(line);
            if (open != null) { Grid.SetColumn(open, 1); open.VerticalAlignment = VerticalAlignment.Bottom; whenRow.Children.Add(open); }
            var strike = StrikeButton(next, words, () => new[] { leadInk, restInk }, (0.26, 0.48, 1.0));
            buttons[next.Id] = strike;
            var item = new StackPanel { Orientation = Orientation.Vertical, Background = Brushes.Transparent };
            item.Children.Add(strike);
            item.Children.Add(whenRow);
            ShowOpenWith(item, open);
            inner.Children.Add(item);
            pieces.Add(strike);
            pieces.Add(whenRow);
            var then = ahead.Skip(1).Take(SilentMore).ToList();
            if (then.Count > 0)
            {
                var block = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 72, 0, 0) };
                var head = Label("Then");
                head.Margin = new Thickness(0, 0, 0, 8);
                block.Children.Add(head);
                foreach (var r in then)
                {
                    var rw = Words(r.Title, rowTitle, FontWeights.SemiBold, 1, r.Done ? White(0.34) : White(0.84), r.Done);
                    var time = new TextBlock { Text = SilentWords.Row(r, at), FontFamily = K.TextFace, FontSize = rowWhen, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(White(r.Done ? 0.26 : 0.42)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 0, 0) };
                    var rowOpen = OpenButton(r);
                    var row = new Grid { Margin = new Thickness(0, 9, 0, 9), Background = Brushes.Transparent };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var press = StrikeButton(r, rw, () => new[] { (SolidColorBrush)time.Foreground }, (0.26, 0.42, 0.84));
                    buttons[r.Id] = press;
                    row.Children.Add(press);
                    Grid.SetColumn(time, 1);
                    row.Children.Add(time);
                    if (rowOpen != null) { Grid.SetColumn(rowOpen, 2); rowOpen.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(rowOpen); }
                    ShowOpenWith(row, rowOpen);
                    block.Children.Add(row);
                }
                Piece(block);
            }
        }
        // (while it reads: black, and nothing)

        var holder = new Grid { Margin = new Thickness(pad, 72, pad, 72) };
        holder.Children.Add(inner);
        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = holder, Focusable = false };
        scroller.SizeChanged += (_, e) => holder.MinHeight = Math.Max(0, e.NewSize.Height - 144);
        var exit = ExitButton();
        var page = new Grid { Background = Brushes.Black };
        page.Children.Add(scroller);
        page.Children.Add(exit);
        AutomationProperties.SetName(page, "Silent mode: what comes next");

        // coming in: each piece rises a little into place, one after another (a fade alone without motion)
        if (!silentEntered && silentRead.Data != null)
        {
            silentEntered = true;
            if (Motion.On)
            {
                for (var i = 0; i < pieces.Count; i++) Motion.Enter(pieces[i], i * 55, 10, 300);
                Motion.FromTo(exit, UIElement.OpacityProperty, 0, 1, 300, Motion.Decelerate, 120);
            }
        }
        // (the keyboard back on the words it was on before this build)
        if (silentFocus != null && buttons.TryGetValue(silentFocus, out var had))
            page.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (had.IsVisible) had.Focus(); });
        silentFocus = null;
        SilentClock();
        return page;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text.ToUpperInvariant(), FontFamily = K.TextFace, FontSize = 13, FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(White(0.42)), Margin = new Thickness(0, 0, 0, 18),
    };

    private static TextBlock Line(string text, double size, Color ink) => new()
    {
        Text = text, FontFamily = K.TextFace, FontSize = size, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(ink), TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>An item's words in large type, and the line through them as it is struck out: its two inner stops move
    /// from the left edge to the right to draw it (each line of the words at once).</summary>
    private static (TextBlock Text, GradientStop Edge, GradientStop After) Words(string text, double size, FontWeight weight, int lines, Color ink, bool struckOut = false)
    {
        var t = new TextBlock
        {
            Text = text, FontFamily = K.DisplayFace, FontSize = size, FontWeight = weight, Foreground = new SolidColorBrush(ink),
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = lines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        if (lines > 1)
        {
            t.LineHeight = Math.Round(size * 1.08);
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            t.MaxHeight = t.LineHeight * lines + 1;
        }
        var line = White(0.72);
        var edge = new GradientStop(line, struckOut ? 1 : 0);
        var after = new GradientStop(Colors.Transparent, struckOut ? 1 : 0);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        brush.GradientStops.Add(new GradientStop(line, 0));
        brush.GradientStops.Add(edge);
        brush.GradientStops.Add(after);
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        var pen = new Pen(brush, Math.Max(1.5, Math.Round(size * 0.075, 1)));
        t.TextDecorations = new TextDecorationCollection { new TextDecoration(TextDecorationLocation.Strikethrough, pen, 0, TextDecorationUnit.FontRecommended, TextDecorationUnit.Pixel) };
        return (t, edge, after);
    }

    /// <summary>A button drawn as nothing but what is in it (the words, Open, Exit), on a ground that takes the pointer.</summary>
    private static readonly ControlTemplate Plain = MakePlain();

    private static ControlTemplate MakePlain()
    {
        var t = new ControlTemplate(typeof(Button));
        var ground = new FrameworkElementFactory(typeof(Border));
        ground.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        ground.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        ground.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
        ground.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        t.VisualTree = ground;
        t.Seal();
        return t;
    }

    /// <summary>An item's words as a press that strikes it out — marked done, as its tick does elsewhere — and, pressed
    /// again, puts it back: the line drawn through them left to right, the words and their line dimmed.</summary>
    private Button StrikeButton(DashSilentRow r, (TextBlock Text, GradientStop Edge, GradientStop After) words, Func<SolidColorBrush[]> lines, (double Done, double Line, double Words) inks)
    {
        var b = new Button
        {
            Content = words.Text, Template = Plain, Background = Brushes.Transparent, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand, FocusVisualStyle = null,
        };
        void Say(bool on)
        {
            b.ToolTip = on ? "Done: press to put it back" : "Press to strike it out";
            AutomationProperties.SetName(b, $"{(on ? "Done" : "Mark done")}: {r.Title}");
        }
        Say(r.Done);
        b.MouseEnter += (_, _) => b.Opacity = 0.72;
        b.MouseLeave += (_, _) => b.Opacity = 1;
        b.GotKeyboardFocus += (_, _) => { b.Background = new SolidColorBrush(White(0.08)); silentFocused = r.Id; };
        b.LostKeyboardFocus += (_, _) => { b.Background = Brushes.Transparent; if (silentFocused == r.Id) silentFocused = null; };
        b.Click += async (_, _) =>
        {
            var was = struck.TryGetValue(r.Id, out var s) ? s.Done : r.Done;
            var on = !was;
            struck[r.Id] = Copy(r, on);
            Paint(on);
            Say(on);
            var ok = await engine.Act("complete", new { id = r.Id, done = on });
            if (!ok)
            {
                struck[r.Id] = Copy(r, was);
                Paint(was);
                Say(was);
                return;
            }
            silentFocus = silentFocused;
            engine.DataChanged();
        };
        return b;

        void Paint(bool on)
        {
            var to = on ? 1.0 : 0.0;
            var ms = Motion.On ? 260 : 0;
            foreach (var stop in new[] { words.Edge, words.After })
            {
                try
                {
                    if (ms > 0) stop.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Motion.Decelerate });
                    else { stop.BeginAnimation(GradientStop.OffsetProperty, null); stop.Offset = to; }
                }
                catch (InvalidOperationException) { stop.Offset = to; }
            }
            Fade((SolidColorBrush)words.Text.Foreground, White(on ? 0.34 : inks.Words));
            var ls = lines();
            for (var i = 0; i < ls.Length; i++)
                Fade(ls[i], i == 0 && ls.Length > 1 ? (on ? White(inks.Done) : Ink) : White(on ? inks.Done : inks.Line));
        }
    }

    private static void Fade(SolidColorBrush brush, Color to)
    {
        if (brush.IsFrozen) return;
        if (Motion.On) brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(200)));
        else brush.Color = to;
    }

    private static DashSilentRow Copy(DashSilentRow r, bool done) => new() { Id = r.Id, Title = r.Title, At = r.At, Verb = r.Verb, Course = r.Course, Url = r.Url, Done = done };

    /// <summary>"Open", beside the words: the item itself (shown while the pointer is on the item, or the keyboard is).</summary>
    private Button? OpenButton(DashSilentRow r)
    {
        if (string.IsNullOrEmpty(r.Url)) return null;
        var text = new TextBlock { Text = "Open", FontFamily = K.TextFace, FontSize = 13, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(White(0.45)) };
        var b = new Button
        {
            Content = text, Template = Plain, Background = Brushes.Transparent, Padding = new Thickness(6, 2, 6, 2), BorderThickness = new Thickness(0),
            Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand, Opacity = 0, ToolTip = $"Open {r.Title}",
        };
        AutomationProperties.SetName(b, $"Open {r.Title}");
        b.MouseEnter += (_, _) => Fade((SolidColorBrush)text.Foreground, White(0.92));
        b.MouseLeave += (_, _) => Fade((SolidColorBrush)text.Foreground, White(0.45));
        b.Click += (_, _) => engine.OpenWeb(r.Url!, r.Title);
        return b;
    }

    /// <summary>Open shows while the pointer is on its item, or the keyboard is in it.</summary>
    private static void ShowOpenWith(FrameworkElement item, Button? open)
    {
        if (open == null) return;
        void Show() => open.Opacity = item.IsMouseOver || item.IsKeyboardFocusWithin ? 1 : 0;
        item.MouseEnter += (_, _) => Show();
        item.MouseLeave += (_, _) => Show();
        item.IsKeyboardFocusWithinChanged += (_, _) => Show();
    }

    /// <summary>Exit Silent Mode, at the top right: grey words and the moon; under the pointer white, on a faint pill.</summary>
    private Button ExitButton()
    {
        var words = new TextBlock { Text = "Exit Silent Mode", FontFamily = K.TextFace, FontSize = 13, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        var moon = new SymbolIcon { Symbol = SymbolRegular.WeatherMoon24, FontSize = 14, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        var ink = new SolidColorBrush(White(0.45));
        words.Foreground = ink;
        moon.Foreground = ink;
        var b = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { moon, words } },
            Template = Plain, Background = Brushes.Transparent, Padding = new Thickness(13, 0, 13, 0), Height = 30, BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 16, 20, 0),
            Cursor = Cursors.Hand, ToolTip = "Back to the whole Dashboard",
        };
        AutomationProperties.SetName(b, "Exit Silent Mode");
        b.MouseEnter += (_, _) => { Fade(ink, White(0.92)); b.Background = new SolidColorBrush(White(0.1)); };
        b.MouseLeave += (_, _) => { Fade(ink, White(0.45)); b.Background = Brushes.Transparent; };
        b.Click += (_, _) => SilentMode.Set(false);
        return b;
    }

    /// <summary>The countdown: each half minute while Silent mode shows, drawn again when its words say something else
    /// (the next one past, "in 5 minutes" now "in 4 minutes").</summary>
    private void SilentClock()
    {
        if (silentClock != null) { if (!silentClock.IsEnabled) silentClock.Start(); return; }
        silentClock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        silentClock.Tick += (_, _) =>
        {
            if (!Silent) { silentClock.Stop(); return; }
            if (Away) return;
            if (SilentWordsNow(DateTime.Now) != silentSaid)
            {
                silentFocus = silentFocused;
                Render();
            }
        };
        silentClock.Start();
    }
}
