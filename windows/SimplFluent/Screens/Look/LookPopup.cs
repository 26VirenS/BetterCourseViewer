using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// (0.3.94, the Mac's 1.3.199 LookQuizScreen) A Macmillan Learning or ALEKS assignment in a popup over the window, as a
/// Knewton Alta one is — no longer the tool window 0.3.92 and 0.3.93 opened it in: the assignment's name and when it is
/// due across the top, with Simpl Look, Reload, Open in window and Close; down the left, a rail of the assignment as Simpl
/// reads it from the tool's own page — Macmillan's questions as tiles (the one open filled, a solved one ticked, one tried
/// and not solved marked so), how many are done and the points; ALEKS's topic, its progress step by step, and ALEKS's
/// word on the last answer — and in the rest, the tool's own page in Simpl's look (PageLook's rail mode), where every
/// question is answered as on the tool's own page. A tile clicked opens that question (Macmillan's own row clicked in the
/// page). The page is zoomed out, as the browser's own zoom would, while the popup is narrower than the tool lays it out.
/// </summary>
public static class LookPopup
{
    public static void Show(Engine.Engine engine, LookLaunch launch, Window over) => new LookPopupView(engine, launch, over).Open();
}

internal sealed class LookPopupView
{
    private sealed class LaunchAnswer
    {
        public string Url { get; set; } = "";
        public bool Sessionless { get; set; } = true;
    }

    private sealed record Question(int N, string Kind, string Label, string Sub, string Points)
    {
        public bool Solved => Kind == "success";
        public bool Tried => !Solved && Kind != "default" && Kind.Length > 0;
    }

    private readonly Engine.Engine engine;
    private readonly LookLaunch launch;
    private readonly Window over;
    private readonly PageLook.Kind kind;
    private readonly Grid root = new();
    private readonly ColumnDefinition railColumn = new() { Width = new GridLength(240) };
    private readonly ScrollViewer rail = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly WebView2 web = new() { Visibility = Visibility.Hidden };
    private readonly ContentControl state = new() { Focusable = false };
    private readonly Grid holder = new();
    private readonly TextBlock title = K.Text("", Ty.Subtitle);
    private readonly TextBlock subtitle = K.Text("", Ty.Body, B.Text2);
    private readonly Wpf.Ui.Controls.Button lookButton;
    private readonly FrameworkElement header;
    private PageLook? look;
    private Sheet? sheet;
    private bool closed, heard, loadedOnce, logged;
    private string? due;
    private double wide;
    private string railSig = "";

    // what the page has said (Macmillan's, then ALEKS's)
    private List<Question> questions = new();
    private int here, done = -1, total;
    private string score = "", slice = "", topic = "", status = "";
    private bool policy, onQuestion;
    private List<double> fills = new();
    /// <summary>The share done and the steps' fills as last drawn: the ring and the bars grow from there.</summary>
    private double drawnShare = -1;
    private List<double> drawnFills = new();
    private int drawnHere;

    public LookPopupView(Engine.Engine engine, LookLaunch launch, Window over)
    {
        this.engine = engine;
        this.launch = launch;
        this.over = over;
        kind = PageLook.Named(launch.Look);
        due = launch.Due;
        title.Text = launch.Title;
        root.Bg(B.Solid);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        lookButton = K.Button("Simpl Look", ToggleLook, icon: SymbolRegular.Sparkle24);
        var reload = K.IconButton(SymbolRegular.ArrowClockwise24, () => { if (web.CoreWebView2 is { } c) c.Reload(); }, "Reload");
        var window = K.IconButton(SymbolRegular.WindowNew24, OpenInWindow, "Open in a window of its own, with Back, Forward and Open in browser");
        header = AltaLook.Header(engine, K.IconTile(Symbol, Color, 32), title, subtitle, K.H(8, lookButton, reload, window), Close, $"Close — your work is kept by {kind.Title}");
        root.Children.Add(header.Row(0));

        var body = new Grid();
        body.ColumnDefinitions.Add(railColumn);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(rail.Col(0));
        holder.Margin = new Thickness(0, 0, FloatPopup.EdgeRoom, 8);
        holder.Children.Add(web);
        holder.Children.Add(state);
        holder.SizeChanged += (_, _) => Zoom();
        body.Children.Add(AltaLook.Frame(holder).Col(1));
        root.Children.Add(body.Row(1));
        PaintLook();
        PaintSubtitle();
        DrawRail();
    }

    private SymbolRegular Symbol => kind.Name == PageLook.Aleks.Name ? SymbolRegular.ChartMultiple24 : SymbolRegular.HatGraduation24;
    private Color Color => kind.Name == PageLook.Aleks.Name ? Color.FromRgb(0x00, 0x82, 0x98) : Color.FromRgb(0x58, 0x56, 0xD6);
    private string FullName => kind.Name == PageLook.Aleks.Name ? "ALEKS" : "Macmillan Learning";

    public void Open()
    {
        sheet = AltaLook.Popup(root, header, Close, FullName, "look");
        root.SizeChanged += (_, e) => railColumn.Width = new GridLength(e.NewSize.Width >= 960 ? 284 : 240);
        sheet.Closed += Cleanup;
        sheet.Closed += Updates.Hold(FullName, $"when you close {FullName}").Dispose;
        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            root.Loaded -= loaded;
            AltaLook.Settle(root);
        };
        root.Loaded += loaded;
        state.Content = AltaLook.Opening($"Opening {FullName}…");
        Dialogs.Show(sheet, over);
        Start();
        if (due == null) ReadDue();
    }

    private async void ReadDue()
    {
        if (await AltaLook.Assignment(engine, new AltaLaunch(launch.Course, launch.Assignment, launch.Title)) is not { } a || closed) return;
        due = a.DueLine ?? "";
        PaintSubtitle();
    }

    private void PaintSubtitle() => subtitle.Text = string.Join(" · ", new[] { FullName, due ?? "" }.Where(s => s.Length > 0));

    /// <summary>The web view made, the look put in from the page's start (its rail mode), Canvas's one-time launch asked
    /// for, and the tool's page opened in it.</summary>
    private async void Start()
    {
        try
        {
            var env = await Engine.Engine.WebEnvironment();
            if (closed) return;
            await web.EnsureCoreWebView2Async(env);
            if (closed) return;
            var core = web.CoreWebView2;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.DownloadStarting += (_, e) => { e.Handled = true; FilePreview.Downloading(e); };
            core.NavigationCompleted += (_, e) =>
            {
                LightPages.Tell(core, $"{kind.Name} popup");
                if (e.IsSuccess && !loadedOnce) { loadedOnce = true; Ready(); }
            };
            // (a window the page opens — a sign-in, a resource — shown in place, as a tab would be)
            core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); };
            await LightPages.Apply(core, $"{kind.Name} popup");
            look = await PageLook.Attach(core, rail: true);
            look.Rail += o => root.Dispatcher.BeginInvoke(() => Take(o));
            PaintLook();
            var a = await engine.Call<LaunchAnswer>("toolLaunch", new Dictionary<string, object> { ["course"] = launch.Course, ["assignment"] = launch.Assignment });
            if (closed) return;
            if (!Uri.TryCreate(a.Url, UriKind.Absolute, out var u)) throw new InvalidOperationException("Simpl could not find where this assignment opens.");
            if (!a.Sessionless)
            {
                // (the school's own page for the tool, its launch form to be sent: the tool window does that)
                Log.Write($"{kind.Name} popup: no one-time launch, opened in the tool window");
                OpenInWindow();
                return;
            }
            Log.Write($"{kind.Name} popup: opened");
            web.Visibility = Visibility.Visible;
            core.Navigate(u.ToString());
        }
        catch (Exception e)
        {
            Log.Write($"{kind.Name} popup: could not open: {e.Message}");
            if (closed) return;
            web.Visibility = Visibility.Hidden;
            state.Content = AltaLook.Failure($"{FullName} could not open", e.Message);
        }
    }

    /// <summary>The page there (loaded once, or heard from): the opening card fades away over it.</summary>
    private void Ready()
    {
        if (closed || state.Content == null || web.Visibility != Visibility.Visible) return;
        if (state.Content is FrameworkElement f) Motion.FadeOut(f, () => state.Content = null, Motion.Normal);
        else state.Content = null;
    }

    private void OpenInWindow()
    {
        engine.OpenTool(ToolLaunch.Assignment(launch.Course, launch.Assignment, launch.Title));
        Close();
    }

    private void Close()
    {
        if (sheet != null) sheet.Close();
        else Cleanup();
    }

    private void Cleanup()
    {
        if (closed) return;
        closed = true;
        web.Visibility = Visibility.Collapsed;
        try { web.Dispose(); } catch { /* (gone) */ }
    }

    // ---- Simpl Look ----

    private async void ToggleLook()
    {
        if (look == null) return;
        await look.Set(kind, !look.On(kind));
        PaintLook();
    }

    private void PaintLook()
    {
        var on = look?.On(kind) ?? true;
        lookButton.Appearance = on ? ControlAppearance.Primary : ControlAppearance.Secondary;
        lookButton.IsEnabled = look != null;
        lookButton.ToolTip = on
            ? $"Simpl's look is on: {FullName}'s page on glass, its actions in a dock. Click to show it as {FullName} draws it."
            : $"Show {FullName}'s page in Simpl's look";
        System.Windows.Automation.AutomationProperties.SetName(lookButton, on ? "Simpl Look, on" : "Simpl Look, off");
    }

    // ---- what the page says ----

    private void Take(JsonElement o)
    {
        if (closed) return;
        static string S(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        static int I(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : -1;
        static bool F(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
        var which = S(o, "rail");
        if (which == PageLook.Macmillan.Name)
        {
            var list = new List<Question>();
            if (o.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
                foreach (var r in rows.EnumerateArray())
                {
                    var n = I(r, "n");
                    if (n < 1) continue;
                    var k = S(r, "kind");
                    list.Add(new Question(n, k.Length > 0 ? k : "default", S(r, "label") is { Length: > 0 } l ? l : $"Q{n}", S(r, "sub"), S(r, "points")));
                }
            questions = list;
            here = Math.Max(0, I(o, "here"));
            done = I(o, "done");
            total = Math.Max(0, I(o, "total"));
            score = S(o, "score");
            policy = F(o, "policy");
        }
        else
        {
            slice = S(o, "slice");
            topic = S(o, "topic");
            done = I(o, "done");
            total = Math.Max(0, I(o, "total"));
            status = S(o, "status");
            onQuestion = F(o, "question");
            fills = o.TryGetProperty("fills", out var fs) && fs.ValueKind == JsonValueKind.Array
                ? fs.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => Math.Clamp(x.GetDouble(), 0, 1)).ToList()
                : new List<double>();
        }
        heard = true;
        if (!logged)
        {
            logged = true;
            Log.Write(which == PageLook.Macmillan.Name
                ? $"{which} popup: rail heard, {questions.Count} questions, {Math.Max(done, 0)} done, question {here} open"
                : $"{which} popup: rail heard, topic progress {Math.Max(done, 0)} of {total}");
        }
        if (o.TryGetProperty("wide", out var w) && w.ValueKind == JsonValueKind.Number && w.GetDouble() is var wd && wd > 0 && Math.Abs(wd - wide) > 0.5)
        {
            wide = wd;
            Zoom();
        }
        Ready();
        DrawRail();
    }

    private double zoomSaid = -1;

    /// <summary>The tool's page zoomed out (never in) as far as it takes to fit its width into the popup's — less the
    /// page's own scroll bar, so it never scrolls sideways for want of that — not past 70%.</summary>
    private void Zoom()
    {
        if (closed || wide <= 0 || web.CoreWebView2 == null) return;
        var room = holder.ActualWidth - 18;
        if (room <= 0) return;
        var z = Math.Clamp(room / wide, 0.7, 1);
        if (Math.Abs(web.ZoomFactor - z) >= 0.004) web.ZoomFactor = z;
        if (Math.Abs(zoomSaid - z) < 0.004) return;
        zoomSaid = z;
        Log.Write($"look popup: page at {Math.Round(z * 100)}% (its {wide:0} wide in {room:0})");
    }

    private async void Go(int n)
    {
        if (web.CoreWebView2 is not { } c || n < 1) return;
        try { await c.ExecuteScriptAsync($"window.__simplMMDo && window.__simplMMDo({{ go: {n} }})"); } catch { /* (gone) */ }
    }

    private async void ShowPolicy()
    {
        if (web.CoreWebView2 is not { } c) return;
        try { await c.ExecuteScriptAsync("window.__simplMMDo && window.__simplMMDo({ policy: 1 })"); } catch { /* (gone) */ }
    }

    // ---- the rail ----

    private void DrawRail()
    {
        var sig = JsonSerializer.Serialize(new object[] { heard, questions.Select(q => $"{q.N}{q.Kind}{q.Sub}{q.Points}"), here, done, total, score, policy, slice, topic, status, onQuestion, fills });
        if (sig == railSig) return;
        railSig = sig;
        var v = K.V(16);
        if (kind.Name == PageLook.Aleks.Name) Aleks(v); else Macmillan(v);
        v.Margin = new Thickness(18, 12, 16, 24);
        rail.Content = v;
    }

    private int DoneCount => done >= 0 ? done : questions.Count(q => q.Solved);
    private int TotalCount => total > 0 ? total : questions.Count;

    private void Macmillan(Panel v)
    {
        // how much is done: a ring, the count beside it, the points under
        var share = TotalCount > 0 ? (double)DoneCount / TotalCount : 0;
        var words = K.V(2,
            K.Text(heard ? $"{DoneCount} of {TotalCount}" : "—", Ty.Subtitle),
            K.Text("questions done", Ty.Caption, B.Text2),
            Points() is { } p ? K.Text(p, Ty.Caption, B.Text2) : null);
        words.VerticalAlignment = VerticalAlignment.Center;
        v.Children.Add(K.Card(K.Columns("auto, *", Ring(share, drawnShare < 0 ? share : drawnShare).Col(0), words.Margin(16, 0, 0, 0).Col(1)), new Thickness(14), 12));
        drawnShare = share;

        // the questions as tiles
        var head = K.Columns("*, auto", K.Text("Questions", Ty.BodyStrong).Col(0), Legend().Col(1));
        FrameworkElement tiles;
        if (questions.Count == 0) tiles = new Grid { MinHeight = 34, Children = { K.Spinner(16) } };
        else
        {
            var grid = new UniformGrid { Columns = 5, Margin = new Thickness(-3) };
            foreach (var q in questions) grid.Children.Add(Tile(q, q.N == here, q.N == here && drawnHere != 0 && drawnHere != here).Margin(3));
            tiles = grid;
        }
        drawnHere = here;
        v.Children.Add(K.Card(K.V(12, head, tiles), new Thickness(14), 12));

        // the question open, and the ones either side of it
        if (questions.FindIndex(q => q.N == here) is var at && at >= 0)
        {
            var q = questions[at];
            var before = at > 0 ? questions[at - 1].N : (int?)null;
            var after = at + 1 < questions.Count ? questions[at + 1].N : (int?)null;
            var top = K.Columns("*, auto", K.Text($"Question {q.N}", Ty.BodyStrong).Col(0),
                (q.Points.Length > 0 ? K.Chip(q.Points, Colors.SeaGreen) : null)?.Col(1));
            var (word, icon, brush) = q.Solved ? ("Solved", SymbolRegular.CheckmarkCircle24, B.Success)
                : q.Tried ? ("Tried, not solved yet", SymbolRegular.ErrorCircle24, B.Caution) : ("Not answered yet", SymbolRegular.Circle24, B.Text2);
            var line = K.H(6, K.Icon(icon, 16, brush, filled: q.Solved || q.Tried), K.Text(word, Ty.Body, brush));
            var prev = K.Button("Previous", () => { if (before is { } b) Go(b); }, icon: SymbolRegular.ChevronLeft24);
            prev.IsEnabled = before != null;
            prev.HorizontalAlignment = HorizontalAlignment.Stretch;
            var next = K.Button("Next", () => { if (after is { } a) Go(a); }, icon: SymbolRegular.ChevronRight24);
            next.IsEnabled = after != null;
            next.HorizontalAlignment = HorizontalAlignment.Stretch;
            var card = K.Card(K.V(10, top, line, q.Sub.Length > 0 ? K.Wrapped(q.Sub, Ty.Caption, B.Text2) : null,
                K.Columns("*, 8, *", prev.Col(0), next.Col(2))), new Thickness(14), 12);
            v.Children.Add(card);
            if (drawnHere != 0) Motion.FadeIn(card, Motion.Normal);
        }

        if (policy)
        {
            var b = K.Button("Grading policy", ShowPolicy, icon: SymbolRegular.ClipboardTextLtr24);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.ToolTip = "Macmillan's settings for this assignment: attempts, penalties and how it is scored";
            v.Children.Add(b);
        }
    }

    /// <summary>"1,196 of 2,300 points", from Macmillan's "+1196 pts/2300".</summary>
    private string? Points()
    {
        var nums = Regex.Matches(score, @"\d+(?:\.\d+)?").Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        string F(double d) => d.ToString("#,0.#", CultureInfo.InvariantCulture);
        return nums.Count switch { 0 => null, 1 => $"{F(nums[0])} points", _ => $"{F(nums[0])} of {F(nums[1])} points" };
    }

    private static FrameworkElement Legend()
    {
        FrameworkElement Mark(Color c, string w) => K.H(4, new Ellipse { Width = 6, Height = 6, Fill = K.Brush(c), VerticalAlignment = VerticalAlignment.Center }, K.Text(w, Ty.Caption, B.Text2));
        var h = K.H(10, Mark(Colors.SeaGreen, "Solved"), Mark(Colors.DarkOrange, "Tried"));
        h.VerticalAlignment = VerticalAlignment.Center;
        return h;
    }

    /// <summary>How much is done, as a ring: the accent round a faint track, the share in its middle, growing from where
    /// it was last drawn.</summary>
    private static FrameworkElement Ring(double value, double from)
    {
        const double size = 64, thick = 7;
        var units = Math.PI * (size - thick) / thick; // (the circle's length in stroke widths, as dashes are measured)
        var track = new Ellipse { Width = size, Height = size, StrokeThickness = thick };
        track.SetResourceReference(Shape.StrokeProperty, B.Subtle);
        var arc = new Ellipse
        {
            Width = size, Height = size, StrokeThickness = thick, StrokeDashCap = PenLineCap.Round,
            StrokeDashArray = new DoubleCollection { units, units }, RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(-90),
        };
        arc.SetResourceReference(Shape.StrokeProperty, B.Accent);
        double Offset(double x) => units * (1 - Math.Clamp(x, 0, 1));
        arc.StrokeDashOffset = Offset(value);
        arc.Visibility = value > 0 ? Visibility.Visible : Visibility.Hidden;
        if (Math.Abs(value - from) > 0.0005) Motion.FromTo(arc, Shape.StrokeDashOffsetProperty, Offset(from), Offset(value), 600, Motion.Decelerate);
        var pct = K.Text($"{(int)Math.Round(value * 100)}%", Ty.Body).Strong();
        pct.HorizontalAlignment = HorizontalAlignment.Center;
        pct.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid { Width = size, Height = size, Children = { track, arc, pct } };
        System.Windows.Automation.AutomationProperties.SetName(g, $"Done, {(int)Math.Round(value * 100)} percent");
        return g;
    }

    /// <summary>One question's tile: its number, the one open filled in the accent (springing in as it becomes the one
    /// open), a solved one washed green with a tick, one tried and not solved washed orange. A button: the question opens.</summary>
    private FrameworkElement Tile(Question q, bool current, bool arrived)
    {
        var face = new Border { CornerRadius = new CornerRadius(6), Height = 34, Cursor = Cursors.Hand, Focusable = true, FocusVisualStyle = null };
        if (current) face.Bg(B.Accent);
        else if (q.Solved) face.Background = K.Brush(K.WithAlpha(Colors.SeaGreen, 0.18));
        else if (q.Tried) face.Background = K.Brush(K.WithAlpha(Colors.DarkOrange, 0.2));
        else face.Bg(B.Subtle);
        UIElement mark = q.Solved && !current
            ? K.Icon(SymbolRegular.Checkmark24, 14, Colors.SeaGreen)
            : K.Text(q.N.ToString(CultureInfo.InvariantCulture), Ty.Body, current ? B.OnAccent : q.Tried ? B.Caution : B.Text2).Strong().Center();
        if (mark is FrameworkElement m) m.VerticalAlignment = VerticalAlignment.Center;
        face.Child = mark;
        var scale = new ScaleTransform(1, 1);
        face.RenderTransformOrigin = new Point(0.5, 0.5);
        face.RenderTransform = scale;
        void To(double s, double ms) { Motion.FromTo(scale, ScaleTransform.ScaleXProperty, scale.ScaleX, s, ms, Motion.Decelerate); Motion.FromTo(scale, ScaleTransform.ScaleYProperty, scale.ScaleY, s, ms, Motion.Decelerate); }
        face.MouseEnter += (_, _) => { if (!current) To(1.05, Motion.Fast); };
        face.MouseLeave += (_, _) => To(1, Motion.Fast);
        face.MouseLeftButtonDown += (_, e) => { To(0.93, Motion.Fast); e.Handled = true; };
        face.MouseLeftButtonUp += (_, e) => { To(1, Motion.Normal); e.Handled = true; Go(q.N); };
        face.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; Go(q.N); } };
        if (arrived && Motion.On) { scale.ScaleX = scale.ScaleY = 0.85; To(1, Motion.Slow); }
        face.ToolTip = q.Sub.Length > 0 ? $"{q.Label} — {q.Sub}" : q.Label;
        System.Windows.Automation.AutomationProperties.SetName(face, $"Question {q.N}, {(q.Solved ? "solved" : q.Tried ? "tried" : "not answered")}{(current ? ", open" : "")}");
        return face;
    }

    private void Aleks(Panel v)
    {
        var group = K.Text(slice.Length > 0 ? slice.ToUpperInvariant() : "ALEKS", Ty.Caption, B.Text2).Strong();
        var name = K.Wrapped(topic.Length > 0 ? topic : heard ? "Choose a class or a topic in ALEKS" : "Your topic", Ty.Subtitle);
        v.Children.Add(K.Card(K.V(6, group, name), new Thickness(14), 12));
        if (total <= 0 && heard) return;

        var n = heard ? total : 5;
        var steps = fills.Count == total && total > 0 ? fills : Enumerable.Range(0, n).Select(i => i < done ? 1.0 : 0.0).ToList();
        var head = K.Columns("*, auto", K.Text("Topic progress", Ty.BodyStrong).Col(0), K.Text($"{Math.Max(done, 0)} of {total}", Ty.BodyStrong).Col(1));
        var bars = new UniformGrid { Rows = 1, Columns = Math.Max(steps.Count, 1), Margin = new Thickness(-2, 0, -2, 0) };
        for (var i = 0; i < steps.Count; i++) bars.Children.Add(Step(steps[i], i < drawnFills.Count ? drawnFills[i] : steps[i]).Margin(2, 0, 2, 0));
        drawnFills = steps;
        var parts = K.V(12, head, bars);
        if (status.Length > 0)
        {
            var again = status.Contains("try", StringComparison.OrdinalIgnoreCase) || status.Contains("incorrect", StringComparison.OrdinalIgnoreCase);
            var chip = K.Chip(status, again ? Colors.DarkOrange : Colors.SeaGreen, strong: true);
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            parts.Children.Add(chip);
            Motion.Pop(chip);
        }
        parts.Children.Add(K.Wrapped("A right answer fills a step; a wrong one can take one back. Fill them all to learn the topic.", Ty.Caption, B.Text2));
        v.Children.Add(K.Card(parts, new Thickness(14), 12));
    }

    /// <summary>One step of ALEKS's topic progress: a rounded bar, filling green from the left as ALEKS fills it.</summary>
    private static FrameworkElement Step(double value, double from)
    {
        const double h = 10;
        var g = new Grid { Height = h };
        var track = new Border { CornerRadius = new CornerRadius(h / 2) }.Bg(B.Subtle);
        var fill = new Border { CornerRadius = new CornerRadius(h / 2), HorizontalAlignment = HorizontalAlignment.Left, Background = K.Brush(Colors.SeaGreen) };
        g.Children.Add(track);
        g.Children.Add(fill);
        var grown = false;
        g.SizeChanged += (_, e) =>
        {
            double W(double x) => e.NewSize.Width * Math.Clamp(x, 0, 1);
            if (!grown && Math.Abs(value - from) > 0.0005) Motion.FromTo(fill, FrameworkElement.WidthProperty, W(from), W(value), 600, Motion.Decelerate);
            else { fill.BeginAnimation(FrameworkElement.WidthProperty, null); fill.Width = W(value); }
            grown = true;
        };
        return g;
    }
}
