using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Kinematics & Projectiles (tools; the Mac's MotionTool): two tabs. 1D motion — the five quantities of motion in a
// straight line, any three filled in, the other two solved with the equation each came from, and the position–time and
// velocity–time graphs of that motion. Projectile — a launch speed, angle and height under g, the time of flight, range,
// highest point and impact found, the path drawn with a dot flying along it (still with Windows' animations off), and
// the two angles that hit a target at a distance. SI in; ft and mph beside the answers when asked. The maths is in
// Tools/Physics/Model/MotionModel.cs.

#pragma warning disable CA2255
internal static class MotionToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("motion", c => new MotionTool(c), c => new MotionQuick(c));
}
#pragma warning restore CA2255

/// <summary>
/// What a launch (or a pin's Open full screen) asks of the tool: the tab ("1d" or "projectile") and values by name —
/// <c>--tool-state projectile:v0=20,angle=45,h=0</c>, <c>--tool-state 1d:v0=0,a=2,t=5</c>, the values alone in
/// <c>--motion-values</c> (the Mac's -SimplMotionValues); the pin hands over the same words.
/// </summary>
internal sealed class MotionLaunch
{
    public string? Tab { get; private init; }
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static MotionLaunch Parse(string? state, string? values = null)
    {
        string? tab = null;
        var rest = "";
        if (!string.IsNullOrWhiteSpace(state))
        {
            var s = state.Trim();
            var colon = s.IndexOf(':');
            var head = colon < 0 ? s : s[..colon];
            rest = colon < 0 ? "" : s[(colon + 1)..];
            if (head.Equals("1d", StringComparison.OrdinalIgnoreCase) || head.Equals("projectile", StringComparison.OrdinalIgnoreCase)) tab = head.ToLowerInvariant();
            else rest = s;
        }
        var l = new MotionLaunch { Tab = tab };
        Take(l.Values, rest);
        Take(l.Values, values);
        return l;
    }

    private static void Take(Dictionary<string, string> into, string? list)
    {
        if (string.IsNullOrWhiteSpace(list)) return;
        foreach (var part in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            into[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
    }

    /// <summary>A value by any of its names ("v0" or "u"; "x" or "dx").</summary>
    public string? Get(params string[] names)
    {
        foreach (var n in names) if (Values.TryGetValue(n, out var v)) return v;
        return null;
    }

    public bool Any => Values.Count > 0;
}

/// <summary>Kinematics &amp; Projectiles whole: its two tabs, the ft-and-mph switch, and each tab's fields, answer and drawing.</summary>
internal sealed class MotionTool : ContentControl
{
    private const string Tool = "motion";

    private readonly SelectorBar tabs;
    private readonly ToggleSwitch imperial = new() { Content = "ft and mph", VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl page = new() { Focusable = false };
    private readonly OneDPage oneD;
    private readonly ProjectilePage projectile;
    private string said = "";

    public MotionTool(ToolContext c)
    {
        Focusable = false;
        // (what the launch asked — --tool motion --tool-state projectile:v0=20,angle=45 — or what its pin handed over;
        //  else what it showed last)
        var launch = MotionLaunch.Parse(PhysTools.LaunchState(Tool) ?? c.TakeInfo("motion"), Launch.Take("", "motion-values"));
        var tab = launch.Tab ?? PhysPrefs.String(Tool, "tab") ?? "1d";
        imperial.IsChecked = PhysPrefs.Bool(Tool, "imperial", false);
        oneD = new OneDPage(launch, imperial.IsChecked == true, Said);
        projectile = new ProjectilePage(launch, imperial.IsChecked == true, Said);
        tabs = new SelectorBar(new[] { "1D motion", "Projectile" }, tab == "projectile" ? 1 : 0) { HorizontalAlignment = HorizontalAlignment.Left };
        tabs.Changed += _ => Show();
        imperial.Checked += (_, _) => Units();
        imperial.Unchecked += (_, _) => Units();
        imperial.ToolTip = "Show the answers in feet and miles per hour as well";
        AutomationProperties.SetName(imperial, "Show feet and miles per hour");
        var top = K.Columns("*, auto", tabs.Col(0), imperial.Col(1));
        Content = ToolKit.Page(K.V(16, top, page));
        Show();
    }

    private void Units()
    {
        var on = imperial.IsChecked == true;
        PhysPrefs.Set(Tool, "imperial", on);
        oneD.Imperial = on;
        projectile.Imperial = on;
    }

    private void Show()
    {
        var proj = tabs.Selected == 1;
        PhysPrefs.Set(Tool, "tab", proj ? "projectile" : "1d");
        page.Content = proj ? (UIElement)projectile : oneD;
        if (proj) projectile.Shown(); else oneD.Shown();
    }

    /// <summary>In a picture run, what it shows each time that changes (CI reads it).</summary>
    private void Said(string now)
    {
        if (now == said) return;
        said = now;
        if (Shot.Asked) Log.Write($"motion: shows {now}");
        Shot.Moved();
    }
}

/// <summary>The words a result is written with: SI, and the imperial beside it when asked.</summary>
internal static class MotionWords
{
    /// <summary>"40.77 m", with " (133.8 ft)" after it when imperial is on; "s" stays as it is.</summary>
    public static string Length(double m, bool imperial) => PhysMath.WithUnit(m, "m") + (imperial ? $" ({PhysMath.WithUnit(m * PhysMath.FeetPerMetre, "ft")})" : "");
    public static string Speed(double ms, bool imperial) => PhysMath.WithUnit(ms, "m/s") + (imperial ? $" ({PhysMath.WithUnit(ms * PhysMath.MphPerMetrePerSecond, "mph")})" : "");
    public static string Accel(double a, bool imperial) => PhysMath.WithUnit(a, "m/s²") + (imperial ? $" ({PhysMath.WithUnit(a * PhysMath.FeetPerMetre, "ft/s²")})" : "");
    public static string Time(double s) => PhysMath.WithUnit(s, "s");
    public static string Angle(double deg) => PhysMath.WithUnit(deg, "°");

    public static string Quantity(string key, double v, bool imperial) => key switch
    {
        "x" => Length(v, imperial), "u" or "v" => Speed(v, imperial), "a" => Accel(v, imperial), _ => Time(v),
    };

    /// <summary>A field as the tools have them, Enter moving on to the next field (the answer is live as you type).</summary>
    public static TextBox Box(string text, string placeholder, Action changed)
    {
        var b = ToolKit.Box(text, placeholder, _ => changed());
        b.PreviewKeyDown += (s, e) =>
        {
            if (e.Key != Key.Enter) return;
            b.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        };
        return b;
    }

    /// <summary>A result that can be copied from its menu, and with Ctrl+C while it has the keyboard.</summary>
    public static TextBlock Copyable(TextBlock t, Func<string> text)
    {
        t.ContextMenu = MoreKit.Menu(("Copy", SymbolRegular.Copy24, () => Everyday.Copy(PhysMath.Ascii(text()))));
        t.Focusable = true;
        t.KeyDown += (_, e) => { if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Everyday.Copy(PhysMath.Ascii(text())); e.Handled = true; } };
        return t;
    }
}

/// <summary>
/// The 1D motion tab: Δx, v₀, v, a and t — any three filled in — the two solved shown large with the equation each came
/// from, a g preset, and the motion's position–time and velocity–time graphs.
/// </summary>
internal sealed class OneDPage : ContentControl
{
    private const string Tool = "motion";
    private readonly Dictionary<string, TextBox> boxes = new();
    private readonly Stack answers = K.V(10);
    private readonly TextBlock note = K.Wrapped("", Ty.Body, B.Text2);
    private readonly Wpf.Ui.Controls.Button copy;
    private readonly MotionGraph position = new(MotionGraph.Of.Position), velocity = new(MotionGraph.Of.Velocity);
    private readonly Action<string> said;
    private Kinematics? result;
    private bool imperial;

    public OneDPage(MotionLaunch launch, bool imperial, Action<string> said)
    {
        Focusable = false;
        this.imperial = imperial;
        this.said = said;
        // (the launch's values, else the ones kept from last time: "x=,u=0,v=,a=2,t=5")
        var kept = MotionLaunch.Parse(PhysPrefs.String(Tool, "oneD"));
        var fields = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var key in KinematicsSolver.Keys)
        {
            var k = key;
            var text = launch.Any ? launch.Get(Names(k)) ?? "" : kept.Get(k) ?? "";
            var box = MotionWords.Box(text, "", Update);
            AutomationProperties.SetName(box, $"{KinematicsSolver.Name(k)} in {KinematicsSolver.Unit(k)}");
            boxes[k] = box;
            var f = ToolKit.Field(KinematicsSolver.Name(k), box, suffix: KinematicsSolver.Unit(k));
            f.Width = 150;
            f.Margin = new Thickness(0, 0, 14, 12);
            fields.Children.Add(f);
        }
        var g = K.Button("g preset", () => { boxes["a"].Text = "−9.81"; }, icon: SymbolRegular.ArrowDown24);
        g.ToolTip = "Free fall: a = −9.81 m/s² (down is negative)";
        var clear = K.Button("Clear", () => { foreach (var b in boxes.Values) b.Text = ""; }, icon: SymbolRegular.Eraser24);
        copy = K.Button("Copy", () => Everyday.Copy(PhysMath.Ascii(CopyText())), icon: SymbolRegular.Copy24);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        var inputs = K.V(8, K.Text("Fill in any three", Ty.BodyStrong, B.Text2), fields, K.H(8, g, clear));
        var card = K.Card(K.V(10, answers, note, copy.Margin(0, 6, 0, 0)), new Thickness(20));
        var graphs = ToolKit.Section("Graphs", K.Columns("*, 16, *", position.Col(0), velocity.Col(2)));
        Content = K.V(20, ToolKit.Columns(inputs, card, 400, 860, sideFirst: true), graphs);
        Update();
    }

    private static string[] Names(string key) => key switch
    {
        "x" => new[] { "x", "dx", "Δx" }, "u" => new[] { "v0", "u" }, "v" => new[] { "v" }, "a" => new[] { "a" }, _ => new[] { "t" },
    };

    public bool Imperial { set { imperial = value; Update(); } }

    public void Shown() => Say();

    private double? Of(string key) => PhysMath.Number(boxes[key].Text);

    private void Update()
    {
        var (r, error) = KinematicsSolver.Solve(Of("x"), Of("u"), Of("v"), Of("a"), Of("t"));
        result = r;
        PhysPrefs.Set(Tool, "oneD", string.Join(",", KinematicsSolver.Keys.Select(k => $"{k}={boxes[k].Text.Trim()}")));
        answers.Children.Clear();
        if (r != null)
        {
            foreach (var (key, equation) in r.Solved)
            {
                var k = key;
                var big = Everyday.Figures(MotionWords.Quantity(k, r.Of(k), imperial), 30, FontWeights.Bold);
                MotionWords.Copyable(big, () => $"{KinematicsSolver.Name(k)} = {MotionWords.Quantity(k, r.Of(k), imperial)}");
                var label = K.Text(KinematicsSolver.Name(k), Ty.BodyStrong, B.Text2);
                AutomationProperties.SetName(big, $"{KinematicsSolver.Name(k)} = {MotionWords.Quantity(k, r.Of(k), imperial)}");
                answers.Children.Add(K.V(2, label, Everyday.Shrinks(big), K.Text($"from {equation}", Ty.Caption, B.Text2)));
            }
            var given = KinematicsSolver.Keys.Where(k => !r.Solved.Any(s => s.Key == k)).Select(k => $"{KinematicsSolver.Name(k)} = {MotionWords.Quantity(k, r.Of(k), imperial)}");
            note.Text = "Given " + string.Join(", ", given) + (r.T < 0 ? " · the time comes out negative: this motion happened before t = 0." : "");
        }
        else
        {
            answers.Children.Add(K.Text("—", Ty.Title, B.Text3));
            note.Text = error ?? "";
        }
        copy.IsEnabled = r != null;
        position.Motion = r;
        velocity.Motion = r;
        Say();
    }

    private string CopyText() => result is not { } r ? "" :
        string.Join(", ", r.Solved.Select(s => $"{KinematicsSolver.Name(s.Key)} = {MotionWords.Quantity(s.Key, r.Of(s.Key), imperial)} ({s.Equation})"));

    private void Say() => said(result is { } r
        ? "1d " + string.Join(" ", r.Solved.Select(s => $"{s.Key}={PhysMath.Ascii(PhysMath.Text(r.Of(s.Key)))}"))
        : "1d nothing solved");
}

/// <summary>
/// The Projectile tab: launch speed, angle, height and g; the flight's time, range, highest point and impact; the path
/// drawn with its apex and landing marked and a dot flying along it; and the two angles that hit a target at a distance.
/// </summary>
internal sealed class ProjectilePage : ContentControl
{
    private const string Tool = "motion";
    private readonly TextBox speed, angle, height, gravity, target;
    private readonly Stack answers = K.V(8);
    private readonly TextBlock note = K.Wrapped("", Ty.Body, B.Text2);
    private readonly TextBlock aim = K.Wrapped("", Ty.Body);
    private readonly Wpf.Ui.Controls.Button copy;
    private readonly TrajectoryView path = new();
    private readonly Action<string> said;
    private Flight? flight;
    private bool imperial;

    public ProjectilePage(MotionLaunch launch, bool imperial, Action<string> said)
    {
        Focusable = false;
        this.imperial = imperial;
        this.said = said;
        var kept = MotionLaunch.Parse(PhysPrefs.String(Tool, "projectile"));
        string Start(string fallback, params string[] names) => (launch.Any ? launch.Get(names) : kept.Get(names)) ?? fallback;
        speed = MotionWords.Box(Start("20", "v0", "v", "speed"), "20", Update);
        angle = MotionWords.Box(Start("45", "angle", "theta"), "45", Update);
        height = MotionWords.Box(Start("0", "h", "height"), "0", Update);
        gravity = MotionWords.Box(Start("9.81", "g"), "9.81", Update);
        target = MotionWords.Box(Start("", "d", "target"), "", Update);
        AutomationProperties.SetName(speed, "Launch speed in metres per second");
        AutomationProperties.SetName(angle, "Launch angle in degrees");
        AutomationProperties.SetName(height, "Launch height in metres");
        AutomationProperties.SetName(gravity, "g in metres per second squared");
        AutomationProperties.SetName(target, "Target distance in metres");
        var fields = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, box, unit) in new[] { ("Launch speed", speed, "m/s"), ("Angle", angle, "°"), ("Launch height", height, "m"), ("g", gravity, "m/s²") })
        {
            var f = ToolKit.Field(label, box, suffix: unit);
            f.Width = 150;
            f.Margin = new Thickness(0, 0, 14, 12);
            fields.Children.Add(f);
        }
        var again = K.Button("Launch again", () => path.Fly(), icon: SymbolRegular.Play24);
        again.ToolTip = "Fly the dot along the path again";
        var aimField = ToolKit.Field("Hit a target at", target, suffix: "m");
        aimField.Width = 150;
        var aimRow = K.Columns("auto, *", aimField.Col(0), aim.Margin(16, 24, 0, 0).Col(1));
        copy = K.Button("Copy", () => Everyday.Copy(PhysMath.Ascii(CopyText())), icon: SymbolRegular.Copy24);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        var inputs = K.V(8, K.Text("Launch", Ty.BodyStrong, B.Text2), fields, again.Align(HorizontalAlignment.Left), K.Text("Target", Ty.BodyStrong, B.Text2).Margin(0, 8, 0, 0), aimRow);
        var card = K.Card(K.V(10, answers, note, copy.Margin(0, 6, 0, 0)), new Thickness(20));
        path.Height = 300;
        var drawing = ToolKit.Section("Path", path);
        Content = K.V(20, ToolKit.Columns(inputs, card, 400, 860, sideFirst: true), drawing);
        Update();
    }

    public bool Imperial { set { imperial = value; Update(); } }

    public void Shown()
    {
        path.Fly();
        Say();
    }

    private void Update()
    {
        double? v0 = PhysMath.Number(speed.Text), th = PhysMath.Number(angle.Text), h = PhysMath.Number(height.Text), g = PhysMath.Number(gravity.Text);
        PhysPrefs.Set(Tool, "projectile", $"v0={speed.Text.Trim()},angle={angle.Text.Trim()},h={height.Text.Trim()},g={gravity.Text.Trim()},d={target.Text.Trim()}");
        answers.Children.Clear();
        flight = null;
        string? error = null;
        if (v0 == null || th == null || h == null || g == null) error = "Fill in the launch speed, angle, height and g.";
        else if (!(g > 0)) error = "g must be more than 0 — 9.81 m/s² on Earth.";
        else if (v0 < 0) error = "The launch speed can't be negative — turn the angle round instead.";
        else if (h < 0) error = "The launch height is measured from the ground: 0 or more.";
        else flight = Projectile.Solve(v0.Value, th.Value, h.Value, g.Value);
        double s0 = v0 ?? 0, t0 = th ?? 0, h0 = h ?? 0, g0 = g ?? Projectile.G;
        if (flight is { } f)
        {
            void Row(string label, string value)
            {
                var t = Everyday.Figures(value, 22, FontWeights.SemiBold);
                MotionWords.Copyable(t, () => $"{label}: {value}");
                AutomationProperties.SetName(t, $"{label} {value}");
                answers.Children.Add(K.V(0, K.Text(label, Ty.Caption, B.Text2), Everyday.Shrinks(t)));
            }
            Row("Time of flight", MotionWords.Time(f.Time));
            Row("Range", MotionWords.Length(f.Range, imperial));
            Row("Maximum height", MotionWords.Length(f.MaxHeight, imperial) + (f.ApexTime > 0 ? $" at {MotionWords.Time(f.ApexTime)}" : ""));
            Row("Impact", $"{MotionWords.Speed(f.ImpactSpeed, imperial)} at {MotionWords.Angle(Math.Abs(f.ImpactAngle))} below");
            note.Text = $"v₀ = {MotionWords.Speed(s0, imperial)} at {MotionWords.Angle(t0)} from {MotionWords.Length(h0, imperial)} · g = {PhysMath.Text(g0)} m/s² · air ignored";
            path.Show(s0, t0, h0, g0, f);
        }
        else
        {
            answers.Children.Add(K.Text("—", Ty.Title, B.Text3));
            note.Text = error ?? "No flight: the launch is straight down from the ground.";
            path.Show(null, 0, 0, Projectile.G, null);
        }
        // (the target: the two angles that land on it, from this speed and height)
        var d = PhysMath.Number(target.Text);
        if (d is { } dist && v0 is { } s && h is { } hh && g is { } gg && gg > 0 && s > 0)
        {
            var angles = dist > 0 ? Projectile.TargetAngles(s, dist, hh, gg) : Array.Empty<double>();
            aim.Text = dist <= 0 ? "A distance more than 0."
                : angles.Length == 0 ? $"Out of reach at {MotionWords.Speed(s, imperial)}."
                : angles.Length == 1 ? $"Only {MotionWords.Angle(angles[0])} reaches it — the very edge of its reach."
                : $"Launch at {MotionWords.Angle(angles[0])} or {MotionWords.Angle(angles[1])}.";
            path.Target = dist > 0 ? dist : null;
        }
        else
        {
            aim.Text = target.Text.Trim().Length == 0 ? "A distance, and the two angles that hit it are given." : "A distance in metres.";
            path.Target = null;
        }
        copy.IsEnabled = flight != null;
        Say();
    }

    private string CopyText() => flight is not { } f ? "" :
        $"Time of flight {MotionWords.Time(f.Time)}, range {MotionWords.Length(f.Range, imperial)}, maximum height {MotionWords.Length(f.MaxHeight, imperial)}, impact {MotionWords.Speed(f.ImpactSpeed, imperial)} at {MotionWords.Angle(Math.Abs(f.ImpactAngle))} below the horizontal";

    private void Say() => said(flight is { } f
        ? $"projectile range={PhysMath.Ascii(PhysMath.Text(f.Range))} height={PhysMath.Ascii(PhysMath.Text(f.MaxHeight))} time={PhysMath.Ascii(PhysMath.Text(f.Time))}"
        : "projectile nothing solved");
}

/// <summary>
/// A motion's graph (position–time or velocity–time): the curve over its axes, drawn in the theme's colours, the time
/// from 0 to t along the bottom.
/// </summary>
internal sealed class MotionGraph : FrameworkElement
{
    public enum Of { Position, Velocity }

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(MotionGraph),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty Text2BrushProperty = DependencyProperty.Register(nameof(Text2Brush), typeof(Brush), typeof(MotionGraph),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(MotionGraph),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush Text2Brush { get => (Brush)GetValue(Text2BrushProperty); set => SetValue(Text2BrushProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    private static readonly Typeface Face = new(K.SmallFace, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Of of;
    private Kinematics? motion;

    public MotionGraph(Of of)
    {
        this.of = of;
        Height = 170;
        SetResourceReference(TextBrushProperty, B.Text);
        SetResourceReference(Text2BrushProperty, B.Text2);
        SetResourceReference(AccentBrushProperty, B.Accent);
        AutomationProperties.SetName(this, of == Of.Position ? "Position–time graph" : "Velocity–time graph");
    }

    public Kinematics? Motion { set { motion = value; InvalidateVisual(); } }

    private void Label(DrawingContext dc, string s, Point at, bool right = false)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 11, Text2Brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(right ? at.X - ft.Width : at.X, at.Y));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width < 10 || size.Height < 10) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));
        var title = of == Of.Position ? "Position–time" : "Velocity–time";
        var tf = new FormattedText(title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(K.TextFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 13, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(tf, new Point(0, 0));
        const double left = 44, bottom = 22, top = 26, right = 10;
        var plot = new Rect(left, top, Math.Max(10, size.Width - left - right), Math.Max(10, size.Height - top - bottom));
        var axis = new Pen(Text2Brush, 1);
        if (motion is not { } m || !(Math.Abs(m.T) > 0))
        {
            dc.DrawLine(axis, plot.BottomLeft, plot.BottomRight);
            dc.DrawLine(axis, plot.TopLeft, plot.BottomLeft);
            Label(dc, "Solve a motion to graph it", new Point(plot.Left + 8, plot.Top + 8));
            return;
        }
        var tEnd = Math.Abs(m.T);
        const int n = 60;
        var ys = new double[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var t = tEnd * i / n;
            ys[i] = of == Of.Position ? m.XAt(t) : m.VAt(t);
        }
        double dataLo = ys.Min(), dataHi = ys.Max();
        double lo = Math.Min(0, dataLo), hi = Math.Max(0, dataHi);
        if (hi - lo < 1e-9) { hi = lo + 1; }
        var pad = (hi - lo) * 0.08;
        lo -= pad;
        hi += pad;
        double X(double t) => plot.Left + plot.Width * t / tEnd;
        double Y(double y) => plot.Bottom - plot.Height * (y - lo) / (hi - lo);
        // (the axes: time along the bottom, the quantity up the side, zero marked)
        var zero = Y(0);
        var faint = new Pen(Text2Brush, 1) { DashStyle = new DashStyle(new[] { 2.0, 3.0 }, 0) };
        faint.Freeze();
        dc.DrawLine(axis, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        dc.DrawLine(faint, new Point(plot.Left, zero), new Point(plot.Right, zero));
        dc.DrawLine(axis, plot.BottomLeft, plot.BottomRight);
        Label(dc, "0", new Point(plot.Left - 4, zero - 7), right: true);
        // (the highest and lowest the quantity reaches, where they fall on the side, kept clear of the zero)
        if (Math.Abs(Y(dataHi) - zero) > 14) Label(dc, PhysMath.Text(dataHi, 3), new Point(plot.Left - 4, Y(dataHi) - 7), right: true);
        if (Math.Abs(Y(dataLo) - zero) > 14 && Math.Abs(dataLo - dataHi) > 1e-9) Label(dc, PhysMath.Text(dataLo, 3), new Point(plot.Left - 4, Y(dataLo) - 7), right: true);
        Label(dc, $"t = {PhysMath.Text(tEnd, 3)} s", new Point(plot.Right, plot.Bottom + 4), right: true);
        Label(dc, of == Of.Position ? "Δx (m)" : "v (m/s)", new Point(plot.Left + 4, plot.Top - 16));
        // (the curve)
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(X(0), Y(ys[0])), false, false);
            for (var i = 1; i <= n; i++) g.LineTo(new Point(X(tEnd * i / n), Y(ys[i])), true, true);
        }
        geo.Freeze();
        var pen = new Pen(AccentBrush, 2.2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geo);
        // (the end marked)
        dc.DrawEllipse(AccentBrush, null, new Point(X(tEnd), Y(ys[n])), 4, 4);
        var end = of == Of.Position ? PhysMath.Text(m.X, 3) + " m" : PhysMath.Text(m.V, 3) + " m/s";
        Label(dc, end, new Point(X(tEnd) - 4, Y(ys[n]) + (ys[n] >= (hi + lo) / 2 ? 8 : -20)), right: true);
    }
}

/// <summary>
/// A projectile's path: the parabola from its launch to its landing, the ground, the apex and the landing marked, a
/// target when there is one, and a dot that flies along the path over a second and a half (still, at the landing, with
/// Windows' animations off or in a picture). Drawn in the theme's colours.
/// </summary>
internal sealed class TrajectoryView : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(TrajectoryView),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(TrajectoryView),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty Text2BrushProperty = DependencyProperty.Register(nameof(Text2Brush), typeof(Brush), typeof(TrajectoryView),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(TrajectoryView),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush Text2Brush { get => (Brush)GetValue(Text2BrushProperty); set => SetValue(Text2BrushProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    private static readonly Typeface Face = new(K.SmallFace, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Bezier Linear = new(0, 0, 1, 1);
    private static readonly Color Orange = Color.FromRgb(0xf7, 0x8c, 0x1a), Green = Color.FromRgb(0x2f, 0xa8, 0x4f);

    private double? v0;
    private double th, h, g;
    private Flight? flight;
    private double? targetAt;

    public TrajectoryView()
    {
        SetResourceReference(TextBrushProperty, B.Text);
        SetResourceReference(Text2BrushProperty, B.Text2);
        SetResourceReference(AccentBrushProperty, B.Accent);
        ClipToBounds = true;
        AutomationProperties.SetName(this, "The projectile's path");
        Unloaded += (_, _) => BeginAnimation(ProgressProperty, null);
    }

    public double? Target { set { targetAt = value; InvalidateVisual(); } }

    public void Show(double? speed, double angle, double height, double gravity, Flight? f)
    {
        var changed = v0 != speed || th != angle || h != height || g != gravity;
        v0 = speed;
        th = angle;
        h = height;
        g = gravity;
        flight = f;
        InvalidateVisual();
        if (changed) Fly();
    }

    /// <summary>The dot flown along the path once more (at once to the landing with animations off).</summary>
    public void Fly()
    {
        if (flight == null || !(flight.Time > 0)) { BeginAnimation(ProgressProperty, null); Progress = 1; return; }
        Motion.FromTo(this, ProgressProperty, 0, 1, 1500, Linear);
    }

    private void Label(DrawingContext dc, string s, Point at, Brush brush, bool centre = false)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(centre ? at.X - ft.Width / 2 : at.X, at.Y));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width < 10 || size.Height < 10) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));
        const double left = 40, right = 16, top = 24, bottom = 26;
        var plot = new Rect(left, top, Math.Max(10, size.Width - left - right), Math.Max(10, size.Height - top - bottom));
        var ground = new Pen(Text2Brush, 1);
        if (v0 is not { } s || flight is not { } f)
        {
            dc.DrawLine(ground, plot.BottomLeft, plot.BottomRight);
            Label(dc, "Fill in a launch to draw its path", new Point(plot.Left + 8, plot.Top + 8), Text2Brush);
            return;
        }
        // (the same scale both ways, so the parabola keeps its shape; a target further than the landing stretches the ground)
        var xMax = Math.Max(f.Range, targetAt ?? 0);
        var yMax = Math.Max(f.MaxHeight, h);
        if (xMax < 1e-9) xMax = Math.Max(1, yMax);
        if (yMax < 1e-9) yMax = xMax * 0.2;
        var scale = Math.Min(plot.Width / (xMax * 1.06), plot.Height / (yMax * 1.12));
        double X(double x) => plot.Left + x * scale;
        double Y(double y) => plot.Bottom - y * scale;
        dc.DrawLine(ground, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        // (the launch height's post)
        if (h > 0) dc.DrawLine(new Pen(Text2Brush, 2), new Point(X(0), Y(0)), new Point(X(0), Y(h)));
        // (the path)
        const int n = 80;
        var geo = new StreamGeometry();
        using (var gg = geo.Open())
        {
            gg.BeginFigure(new Point(X(0), Y(h)), false, false);
            for (var i = 1; i <= n; i++)
            {
                var (x, y) = Projectile.At(s, th, h, f.Time * i / n, g);
                gg.LineTo(new Point(X(x), Y(Math.Max(0, y))), true, true);
            }
        }
        geo.Freeze();
        var pen = new Pen(AccentBrush, 2.2) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geo);
        // (the apex and the landing)
        if (f.ApexTime > 0)
        {
            var apex = new Point(X(f.ApexX), Y(f.MaxHeight));
            dc.DrawEllipse(K.Brush(Orange), null, apex, 4, 4);
            Label(dc, $"{PhysMath.Text(f.MaxHeight)} m", new Point(apex.X, apex.Y - 18), K.Brush(Orange), centre: true);
        }
        var land = new Point(X(f.Range), Y(0));
        dc.DrawEllipse(K.Brush(Green), null, land, 4, 4);
        Label(dc, $"{PhysMath.Text(f.Range)} m · {PhysMath.Text(f.Time)} s", new Point(land.X, land.Y + 6), K.Brush(Green), centre: true);
        if (targetAt is { } d)
        {
            var at = new Point(X(d), Y(0));
            var tp = new Pen(K.Brush(Orange), 1.5);
            dc.DrawLine(tp, new Point(at.X - 6, at.Y), new Point(at.X + 6, at.Y));
            dc.DrawLine(tp, new Point(at.X, at.Y - 8), new Point(at.X, at.Y + 1));
            Label(dc, "target", new Point(at.X, at.Y - 22), K.Brush(Orange), centre: true);
        }
        Label(dc, "0", new Point(plot.Left - 10, plot.Bottom + 4), Text2Brush);
        Label(dc, "x (m)", new Point(plot.Right - 30, plot.Bottom + 6), Text2Brush);
        Label(dc, "y (m)", new Point(2, plot.Top - 6), Text2Brush);
        // (the dot, where it is now)
        if (f.Time > 0)
        {
            var p = Math.Clamp(Progress, 0, 1);
            var (dx, dy) = Projectile.At(s, th, h, f.Time * p, g);
            dc.DrawEllipse(AccentBrush, new Pen(Brushes.White, 1.5), new Point(X(dx), Y(Math.Max(0, dy))), 6, 6);
        }
    }
}

/// <summary>
/// Kinematics &amp; Projectiles in its pin's flyout (the Mac's MotionCompact): a launch speed and angle (from the ground,
/// g = 9.81) → the range and the highest point; Open full screen hands the whole tool these.
/// </summary>
internal sealed class MotionQuick : ContentControl
{
    public MotionQuick(ToolContext c)
    {
        Focusable = false;
        var kept = MotionLaunch.Parse(PhysPrefs.String("motion", "quick"));
        var range = Everyday.Figures("—", 26, FontWeights.Bold);
        var height = K.Wrapped("", Ty.Body, B.Text2);
        TextBox speed = null!, angle = null!;
        speed = MotionWords.Box(kept.Get("v0") ?? "20", "20", Update);
        angle = MotionWords.Box(kept.Get("angle") ?? "45", "45", Update);
        AutomationProperties.SetName(speed, "Launch speed in metres per second");
        AutomationProperties.SetName(angle, "Launch angle in degrees");
        var row = K.Columns("*, 10, *", ToolKit.Field("Speed", speed, suffix: "m/s").Col(0), ToolKit.Field("Angle", angle, suffix: "°").Col(2));
        void Update()
        {
            if (speed == null || angle == null) return;
            double? v0 = PhysMath.Number(speed.Text), th = PhysMath.Number(angle.Text);
            PhysPrefs.Set("motion", "quick", $"v0={speed.Text.Trim()},angle={angle.Text.Trim()}");
            var f = v0 is { } s && th is { } t && s >= 0 ? Projectile.Solve(s, t, 0) : null;
            range.Text = f is { } r ? $"Range {MotionWords.Length(r.Range, false)}" : "—";
            height.Text = f is { } r2 ? $"Highest point {MotionWords.Length(r2.MaxHeight, false)} · {MotionWords.Time(r2.Time)} in the air" : "Type a speed and an angle.";
            AutomationProperties.SetName(range, f is { } r3 ? $"Range {MotionWords.Length(r3.Range, false)}" : "No answer yet");
        }
        MotionWords.Copyable(range, () => $"{range.Text} · {height.Text}");
        c.Carry = () => new Dictionary<string, string> { ["motion"] = $"projectile:v0={speed.Text.Trim()},angle={angle.Text.Trim()},h=0" };
        Update();
        Content = K.V(10, row, Everyday.Shrinks(range), height);
    }
}
