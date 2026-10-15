using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Vectors (tools; the Mac's VectorsTool): up to six vectors, each by its components or by its magnitude and angle, in
// 2D or 3D; each one's magnitude and direction, the resultant, a pair's dot product and the angle between them, their
// cross product, unit vectors and the projection of one onto the other; and a drawing — arrows from the origin or tip
// to tail with the resultant dashed, 3D as an isometric view. Degrees or radians. The maths is in
// Tools/Physics/Model/VectorModel.cs.

#pragma warning disable CA2255
internal static class VectorsToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("vectors", c => new VectorsTool(c), c => new VectorsQuick(c));
}
#pragma warning restore CA2255

/// <summary>The colours the vectors are drawn in, A to F: told apart in light and dark alike.</summary>
internal static class VectorColours
{
    public static readonly Color[] All =
    {
        Color.FromRgb(0x0a, 0x84, 0xff), Color.FromRgb(0xff, 0x95, 0x00), Color.FromRgb(0x34, 0xc7, 0x59),
        Color.FromRgb(0xaf, 0x52, 0xde), Color.FromRgb(0xff, 0x2d, 0x55), Color.FromRgb(0x00, 0xb3, 0xa4),
    };

    public static Color Of(int i) => All[i % All.Length];
}

/// <summary>Angles as the tool writes them: degrees, or radians when asked.</summary>
internal static class VectorWords
{
    public static string Angle(double degrees, bool radians) => radians ? PhysMath.WithUnit(degrees * PhysMath.DegToRad, "rad") : PhysMath.WithUnit(degrees, "°");

    /// <summary>A launch state read: "3d:" before the list asks for 3D; the list itself is VectorMath.Parse's.</summary>
    public static (bool? ThreeD, List<Vec> Vectors) Launch(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return (null, new List<Vec>());
        var s = state.Trim();
        bool? threeD = null;
        if (s.StartsWith("3d:", StringComparison.OrdinalIgnoreCase)) { threeD = true; s = s[3..]; }
        else if (s.StartsWith("2d:", StringComparison.OrdinalIgnoreCase)) { threeD = false; s = s[3..]; }
        var vs = VectorMath.Parse(s);
        if (threeD == null && vs.Any(v => Math.Abs(v.Z) > 1e-12)) threeD = true;
        return (threeD, vs);
    }
}

/// <summary>One vector's row: its letter, how it is given (components, or magnitude and angle), its fields and its remove.</summary>
internal sealed class VectorRow : ContentControl
{
    private readonly ComboBox how = new() { Width = 170 };
    private readonly Stack fields = K.H(8);
    private readonly TextBox x = ToolKit.Box(), y = ToolKit.Box(), z = ToolKit.Box(), r = ToolKit.Box(), th = ToolKit.Box();
    private readonly TextBlock angleUnit = K.Text("°", Ty.Body, B.Text2);
    private readonly Action changed;
    private bool threeD, radians, polar, syncing;

    public int Index { get; set; }

    public VectorRow(int index, Vec start, bool polar, bool threeD, bool radians, Action changed, Action remove)
    {
        Focusable = false;
        Index = index;
        this.threeD = threeD;
        this.radians = radians;
        this.polar = polar;
        this.changed = changed;
        how.Items.Add(Everyday.Item("Components", "c"));
        how.Items.Add(Everyday.Item("Magnitude & angle", "p"));
        Everyday.Choose(how, polar ? "p" : "c");
        how.SelectionChanged += (_, _) => { if (!syncing && Everyday.Chosen(how) is { } tag) SetPolar(tag == "p"); };
        AutomationProperties.SetName(how, "How the vector is given");
        foreach (var (b, name) in new[] { (x, "x"), (y, "y"), (z, "z"), (r, "magnitude"), (th, "angle") })
        {
            b.Width = 84;
            b.TextChanged += (_, _) => { if (!syncing) changed(); };
            AutomationProperties.SetName(b, $"{VectorMath.Name(index)} {name}");
        }
        Fill(start);
        var gone = K.IconButton(SymbolRegular.Dismiss24, remove, $"Remove vector {VectorMath.Name(index)}", 14, 32);
        gone.VerticalAlignment = VerticalAlignment.Bottom;
        Lay();
        var letter = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = K.Brush(VectorColours.Of(index)), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2),
            Child = new TextBlock { Text = VectorMath.Name(index), FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var howField = K.V(6, K.Text("Given as", Ty.BodyStrong, B.Text2), how);
        Content = K.Columns("auto, auto, *, auto", letter.Margin(0, 0, 10, 0).Col(0), howField.Margin(0, 0, 12, 0).Col(1), fields.Col(2), gone.Margin(8, 0, 0, 0).Col(3));
    }

    /// <summary>The letter the row goes by changes when one before it is removed.</summary>
    public void Renamed(int index)
    {
        Index = index;
        if (Content is Grid g && g.Children[0] is Border b && b.Child is TextBlock t) { t.Text = VectorMath.Name(index); b.Background = K.Brush(VectorColours.Of(index)); }
    }

    public bool Polar => polar;

    private void Lay()
    {
        fields.Children.Clear();
        if (polar)
        {
            fields.Children.Add(ToolKit.Field("Magnitude", r, width: 84));
            var angle = K.Columns("auto, auto", th.Col(0), angleUnit.Margin(6, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1));
            fields.Children.Add(K.V(6, K.Text("Angle from +x", Ty.BodyStrong, B.Text2), angle));
        }
        else
        {
            fields.Children.Add(ToolKit.Field("x", x, width: 84));
            fields.Children.Add(ToolKit.Field("y", y, width: 84));
            if (threeD) fields.Children.Add(ToolKit.Field("z", z, width: 84));
        }
        angleUnit.Text = radians ? "rad" : "°";
    }

    /// <summary>The fields filled from a vector, in the row's own way.</summary>
    private void Fill(Vec v)
    {
        syncing = true;
        x.Text = PhysMath.Ascii(PhysMath.Text(v.X, 6));
        y.Text = PhysMath.Ascii(PhysMath.Text(v.Y, 6));
        z.Text = PhysMath.Ascii(PhysMath.Text(v.Z, 6));
        r.Text = PhysMath.Ascii(PhysMath.Text(v.Magnitude, 6));
        th.Text = PhysMath.Ascii(PhysMath.Text(radians ? v.AngleDeg * PhysMath.DegToRad : v.AngleDeg, 6));
        syncing = false;
    }

    /// <summary>Given the other way: the same vector, written in the other fields (a 3D vector loses nothing: z is kept).</summary>
    private void SetPolar(bool on)
    {
        if (on == polar) return;
        var v = Value ?? Vec.Zero;
        polar = on;
        Fill(v);
        Lay();
        changed();
    }

    /// <summary>2D or 3D, degrees or radians, as the tool is set: the fields shown and their words follow.</summary>
    public void Settings(bool threeD, bool radians)
    {
        var v = Value;
        var unitChanged = this.radians != radians;
        this.threeD = threeD;
        this.radians = radians;
        if (unitChanged && v is { } keep) Fill(keep);
        Lay();
    }

    /// <summary>The vector, or null while a field is not a number.</summary>
    public Vec? Value
    {
        get
        {
            if (polar)
            {
                double? m = PhysMath.Number(r.Text), a = PhysMath.Number(th.Text);
                if (m == null || a == null) return null;
                var v = VectorMath.Polar(m.Value, radians ? a.Value * PhysMath.RadToDeg : a.Value);
                var zz = threeD ? PhysMath.Number(z.Text) ?? 0 : 0;
                return new Vec(v.X, v.Y, zz);
            }
            double? px = PhysMath.Number(x.Text), py = PhysMath.Number(y.Text), pz = threeD ? PhysMath.Number(z.Text) : 0;
            if (px == null || py == null || pz == null) return null;
            return new Vec(px.Value, py.Value, pz.Value);
        }
    }
}

/// <summary>Vectors whole: the settings, the rows, the drawing and the answers.</summary>
internal sealed class VectorsTool : ContentControl
{
    private const string Tool = "vectors";
    private readonly List<VectorRow> rows = new();
    private readonly Stack list = K.V(10);
    private readonly SelectorBar dims, angles;
    private readonly ToggleSwitch tipToTail = new() { Content = "Tip to tail", VerticalAlignment = VerticalAlignment.Center };
    private readonly Wpf.Ui.Controls.Button add, copy;
    private readonly ComboBox pair = new() { Width = 110 };
    private readonly Stack each = K.V(6), resultant = K.V(4), pairOut = K.V(4);
    private readonly TextBlock note = K.Wrapped("", Ty.Body, B.Text2);
    private readonly VectorDiagram diagram = new();
    private string said = "";
    private string copyText = "", baseCopy = "";
    private bool fillingPairs;

    private bool ThreeD => dims.Selected == 1;
    private bool Radians => angles.Selected == 1;

    public VectorsTool(ToolContext c)
    {
        Focusable = false;
        // (what the launch asked — --tool vectors --tool-state 3,4;1,2 — or what its pin handed over; else what it had)
        var (launchThreeD, launched) = VectorWords.Launch(PhysTools.LaunchState(Tool) ?? c.TakeInfo("vectors"));
        var mode = Launch.Take("", "vectors-mode");
        var threeD = mode != null ? mode.Equals("3d", StringComparison.OrdinalIgnoreCase) : launchThreeD ?? PhysPrefs.Bool(Tool, "threeD", false);
        var radians = PhysPrefs.Bool(Tool, "radians", false);
        var start = launched.Count > 0 ? launched : VectorMath.Parse(PhysPrefs.String(Tool, "list"));
        var polar = (launched.Count > 0 ? "" : PhysPrefs.String(Tool, "polar") ?? "").Split(',');
        if (start.Count == 0) start = new List<Vec> { new(3, 4), new(1, 2) };
        dims = new SelectorBar(new[] { "2D", "3D" }, threeD ? 1 : 0);
        angles = new SelectorBar(new[] { "Degrees", "Radians" }, radians ? 1 : 0);
        dims.Changed += _ => Settings();
        angles.Changed += _ => Settings();
        tipToTail.IsChecked = PhysPrefs.Bool(Tool, "tipToTail", false);
        tipToTail.Checked += (_, _) => Update();
        tipToTail.Unchecked += (_, _) => Update();
        tipToTail.ToolTip = "Draw each vector from the tip of the one before";
        AutomationProperties.SetName(tipToTail, "Tip to tail");
        add = K.Button("Add vector", () => { Add(Vec.Zero, false); Update(); }, icon: SymbolRegular.Add24);
        var clear = K.Button("Clear", () => { rows.Clear(); list.Children.Clear(); Add(Vec.Zero, false); Update(); }, icon: SymbolRegular.Eraser24);
        copy = K.Button("Copy", () => Everyday.Copy(PhysMath.Ascii(copyText)), icon: SymbolRegular.Copy24);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        pair.SelectionChanged += (_, _) => Pair();
        AutomationProperties.SetName(pair, "The pair of vectors");
        for (var i = 0; i < start.Count; i++) Add(start[i], i < polar.Length && polar[i] == "p");

        var top = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var e in new FrameworkElement[] { dims, angles, tipToTail, add, clear }) { e.Margin = new Thickness(0, 0, 14, 8); e.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(e); }
        diagram.Height = 340;
        var work = K.V(16, list, ToolKit.Section("Drawing", diagram));
        var pairHead = K.Columns("*, auto", K.Text("A pair", Ty.BodyStrong).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), pair.Col(1));
        var side = K.Card(K.V(14, K.V(6, K.Text("Each vector", Ty.BodyStrong), each), K.V(6, K.Text("Resultant", Ty.BodyStrong), resultant), K.V(8, pairHead, pairOut), note, copy), new Thickness(20));
        Content = ToolKit.Page(K.V(16, top, ToolKit.Columns(work, side, 400, 860)));
        Update();
    }

    private void Add(Vec v, bool polar)
    {
        if (rows.Count >= 6) { add.IsEnabled = false; return; }
        VectorRow? row = null;
        row = new VectorRow(rows.Count, v, polar, ThreeD, Radians, Update, () => { if (row != null) Remove(row); });
        rows.Add(row);
        list.Children.Add(row);
        add.IsEnabled = rows.Count < 6;
    }

    private void Remove(VectorRow row)
    {
        rows.Remove(row);
        list.Children.Remove(row);
        for (var i = 0; i < rows.Count; i++) rows[i].Renamed(i);
        if (rows.Count == 0) Add(Vec.Zero, false);
        add.IsEnabled = rows.Count < 6;
        Update();
    }

    private void Settings()
    {
        PhysPrefs.Set(Tool, "threeD", ThreeD);
        PhysPrefs.Set(Tool, "radians", Radians);
        foreach (var r in rows) r.Settings(ThreeD, Radians);
        Update();
    }

    private List<(int Index, Vec V)> Values() => rows.Select(r => (r.Index, r.Value)).Where(p => p.Value != null).Select(p => (p.Index, p.Value!.Value)).ToList();

    private void Update()
    {
        var threeD = ThreeD;
        var radians = Radians;
        var vs = Values();
        PhysPrefs.Set(Tool, "list", VectorMath.Write(rows.Select(r => r.Value ?? Vec.Zero), true));
        PhysPrefs.Set(Tool, "polar", string.Join(",", rows.Select(r => r.Polar ? "p" : "c")));
        PhysPrefs.Set(Tool, "tipToTail", tipToTail.IsChecked == true);
        var text = new List<string>();
        each.Children.Clear();
        foreach (var r in rows)
        {
            var name = VectorMath.Name(r.Index);
            if (r.Value is not { } v) { each.Children.Add(Line($"{name}: fill in its numbers", VectorColours.Of(r.Index), quiet: true)); continue; }
            var line = threeD
                ? $"{name} = {v.Text(true)} · |{name}| = {PhysMath.Text(v.Magnitude)} · {name}̂ = {VectorMath.Unit(v).Text(true)}"
                : $"{name} = {v.Text(false)} · |{name}| = {PhysMath.Text(v.Magnitude)} at {VectorWords.Angle(v.AngleDeg, radians)}";
            each.Children.Add(Line(line, VectorColours.Of(r.Index)));
            text.Add(line);
        }
        resultant.Children.Clear();
        var sum = VectorMath.Sum(vs.Select(p => p.V));
        if (vs.Count > 0)
        {
            var big = Everyday.Figures($"R = {sum.Text(threeD)}", 22, FontWeights.SemiBold);
            var rl = threeD ? $"|R| = {PhysMath.Text(sum.Magnitude)} · R̂ = {VectorMath.Unit(sum).Text(true)}" : $"|R| = {PhysMath.Text(sum.Magnitude)} at {VectorWords.Angle(sum.AngleDeg, radians)}";
            MotionWords.Copyable(big, () => $"R = {sum.Text(threeD)} · {rl}");
            AutomationProperties.SetName(big, $"Resultant {sum.Text(threeD)}");
            resultant.Children.Add(Everyday.Shrinks(big));
            resultant.Children.Add(K.Wrapped(rl, Ty.Body, B.Text2).Marked());
            text.Add($"R = {string.Join(" + ", vs.Select(p => VectorMath.Name(p.Index)))} = {sum.Text(threeD)} · {rl}");
        }
        else resultant.Children.Add(K.Text("—", Ty.Subtitle, B.Text3));
        // (the pairs there are, the one chosen kept)
        var chosen = Everyday.Chosen(pair);
        fillingPairs = true;
        pair.Items.Clear();
        for (var i = 0; i < vs.Count; i++)
            for (var j = i + 1; j < vs.Count; j++)
                pair.Items.Add(Everyday.Item($"{VectorMath.Name(vs[i].Index)} and {VectorMath.Name(vs[j].Index)}", $"{vs[i].Index}:{vs[j].Index}"));
        if (pair.Items.Count > 0)
        {
            Everyday.Choose(pair, chosen);
            if (pair.SelectedItem == null) pair.SelectedIndex = 0;
        }
        fillingPairs = false;
        pair.IsEnabled = pair.Items.Count > 0;
        baseCopy = string.Join("\n", text);
        Pair();
        note.Text = vs.Count == 0 ? "Fill in a vector's numbers." : threeD ? "Angles from +x are in the xy-plane; 3D directions are given as unit vectors." : "Angles are measured from +x, counter-clockwise.";
        copy.IsEnabled = vs.Count > 0;
        diagram.Show(vs.Select(p => (VectorMath.Name(p.Index), p.V, VectorColours.Of(p.Index))).ToList(), vs.Count > 1 ? sum : null, tipToTail.IsChecked == true, threeD);
        var now = vs.Count == 0 ? "nothing" : $"{vs.Count} vectors, resultant {PhysMath.Ascii(sum.Text(threeD))} |R|={PhysMath.Ascii(PhysMath.Text(sum.Magnitude))}";
        if (now != said)
        {
            said = now;
            if (Shot.Asked) Log.Write($"vectors: shows {now}");
            Shot.Moved();
        }
    }

    /// <summary>The pair chosen: dot, angle between, cross, projection, unit vectors.</summary>
    private void Pair()
    {
        if (fillingPairs) return;
        pairOut.Children.Clear();
        copyText = baseCopy;
        var threeD = ThreeD;
        var radians = Radians;
        var tag = Everyday.Chosen(pair);
        if (tag == null || tag.Split(':') is not { Length: 2 } parts) { pairOut.Children.Add(K.Wrapped("Two vectors or more, and their dot and cross products are here.", Ty.Body, B.Text2)); return; }
        var ia = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var ib = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var ra = rows.FirstOrDefault(r => r.Index == ia);
        var rb = rows.FirstOrDefault(r => r.Index == ib);
        if (ra?.Value is not { } a || rb?.Value is not { } b) return;
        string A = VectorMath.Name(ia), B2 = VectorMath.Name(ib);
        var lines = new List<string>
        {
            $"{A}·{B2} = {PhysMath.Text(VectorMath.Dot(a, b))}",
            VectorMath.AngleBetween(a, b) is { } ang ? $"Angle between: {VectorWords.Angle(ang, radians)}" : "Angle between: — (a zero vector has no direction)",
        };
        var cross = VectorMath.Cross(a, b);
        lines.Add(threeD ? $"{A}×{B2} = {cross.Text(true)} · |{A}×{B2}| = {PhysMath.Text(cross.Magnitude)}"
            : $"{A}×{B2} = {PhysMath.Text(cross.Z)} k̂ ({(cross.Z >= 0 ? "out of" : "into")} the page)");
        lines.Add(VectorMath.Projection(a, b) is { } proj ? $"proj of {A} on {B2} = {proj.Text(threeD)} · {PhysMath.Text(VectorMath.ScalarProjection(a, b) ?? 0)} along {B2}̂" : $"proj of {A} on {B2}: — ({B2} is zero)");
        lines.Add($"{A}̂ = {VectorMath.Unit(a).Text(threeD)} · {B2}̂ = {VectorMath.Unit(b).Text(threeD)}");
        foreach (var l in lines) pairOut.Children.Add(K.Wrapped(l, Ty.Body).Marked());
        copyText += "\n" + string.Join("\n", lines);
    }

    private static FrameworkElement Line(string text, Color colour, bool quiet = false)
    {
        var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = K.Brush(colour), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 8, 0) };
        var t = K.Wrapped(text, Ty.Body, quiet ? B.Text2 : B.Text).Marked();
        return K.Columns("auto, *", dot.Col(0), t.Col(1));
    }
}

/// <summary>
/// The vectors drawn: in 2D, arrows from the origin (or each from the tip of the one before) over axes scaled to fit them,
/// the resultant dashed; in 3D, the same in an isometric view with the three axes. Drawn in the theme's colours, each
/// vector in its own.
/// </summary>
internal sealed class VectorDiagram : FrameworkElement
{
    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(VectorDiagram),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty Text2BrushProperty = DependencyProperty.Register(nameof(Text2Brush), typeof(Brush), typeof(VectorDiagram),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush Text2Brush { get => (Brush)GetValue(Text2BrushProperty); set => SetValue(Text2BrushProperty, value); }

    private static readonly Typeface Face = new(K.TextFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface Small = new(K.SmallFace, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private List<(string Name, Vec V, Color Colour)> vectors = new();
    private Vec? resultant;
    private bool tipToTail, threeD;

    public VectorDiagram()
    {
        SetResourceReference(TextBrushProperty, B.Text);
        SetResourceReference(Text2BrushProperty, B.Text2);
        ClipToBounds = true;
        AutomationProperties.SetName(this, "The vectors drawn");
    }

    public void Show(List<(string Name, Vec V, Color Colour)> vs, Vec? sum, bool chain, bool threeD)
    {
        vectors = vs;
        resultant = sum;
        tipToTail = chain;
        this.threeD = threeD;
        InvalidateVisual();
    }

    /// <summary>A 3D point on the page: an isometric view, x to the right and down, y to the left and down, z up.</summary>
    private static Point Iso(Vec v) => new((v.X - v.Y) * 0.866, (v.X + v.Y) * 0.5 - v.Z);

    private static Point Flat(Vec v) => new(v.X, -v.Y);

    private Point Project(Vec v) => threeD ? Iso(v) : Flat(v);

    private void Text(DrawingContext dc, string s, Point at, Brush brush, Typeface face, double size)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2));
    }

    private static void Arrow(DrawingContext dc, Point from, Point to, Brush brush, bool dashed)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        var pen = new Pen(brush, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (dashed) pen.DashStyle = new DashStyle(new[] { 3.0, 2.5 }, 0);
        pen.Freeze();
        if (len < 1) { dc.DrawEllipse(brush, null, from, 3, 3); return; }
        var ux = dx / len;
        var uy = dy / len;
        var head = Math.Min(11, len * 0.5);
        var tail = new Point(to.X - ux * head * 0.8, to.Y - uy * head * 0.8);
        dc.DrawLine(pen, from, tail);
        var p1 = new Point(to.X - ux * head - uy * head * 0.45, to.Y - uy * head + ux * head * 0.45);
        var p2 = new Point(to.X - ux * head + uy * head * 0.45, to.Y - uy * head - ux * head * 0.45);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(to, true, true);
            g.LineTo(p1, false, false);
            g.LineTo(p2, false, false);
        }
        geo.Freeze();
        dc.DrawGeometry(brush, null, geo);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width < 10 || size.Height < 10) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));
        // (the points to fit: every arrow's two ends, the resultant's, and the origin)
        var pts = new List<Point> { new(0, 0) };
        var at = Vec.Zero;
        var arrows = new List<(string Name, Vec From, Vec To, Color Colour)>();
        foreach (var (name, v, colour) in vectors)
        {
            var from = tipToTail ? at : Vec.Zero;
            var to = from + v;
            arrows.Add((name, from, to, colour));
            at = to;
        }
        foreach (var a in arrows) { pts.Add(Project(a.From)); pts.Add(Project(a.To)); }
        if (resultant is { } r) pts.Add(Project(r));
        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        var span = Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-9);
        // (the origin kept in view, the drawing filling most of the room, the same scale both ways)
        const double pad = 36;
        var scale = Math.Min((size.Width - 2 * pad) / Math.Max(maxX - minX, 1e-9), (size.Height - 2 * pad) / Math.Max(maxY - minY, 1e-9));
        if (!double.IsFinite(scale) || span < 1e-6) scale = 40;
        scale = Math.Min(scale, (Math.Min(size.Width, size.Height) - 2 * pad) / 2);
        var cx = size.Width / 2 - (minX + maxX) / 2 * scale;
        var cy = size.Height / 2 - (minY + maxY) / 2 * scale;
        Point P(Point p) => new(cx + p.X * scale, cy + p.Y * scale);
        // (the axes)
        var axis = new Pen(Text2Brush, 1);
        axis.Freeze();
        var origin = P(new Point(0, 0));
        if (threeD)
        {
            var reach = Math.Max(1, arrows.Count == 0 ? 1 : arrows.Max(a => Math.Max(Math.Abs(a.To.X), Math.Max(Math.Abs(a.To.Y), Math.Abs(a.To.Z)))) * 1.15);
            foreach (var (axisName, dir) in new[] { ("x", new Vec(reach, 0, 0)), ("y", new Vec(0, reach, 0)), ("z", new Vec(0, 0, reach)) })
            {
                var end = P(Iso(dir));
                dc.DrawLine(axis, P(Iso(-0.4 * dir)), end);
                Text(dc, axisName, new Point(end.X + (axisName == "z" ? 0 : axisName == "x" ? 10 : -10), end.Y + (axisName == "z" ? -9 : 9)), Text2Brush, Small, 11);
            }
        }
        else
        {
            dc.DrawLine(axis, new Point(0, origin.Y), new Point(size.Width, origin.Y));
            dc.DrawLine(axis, new Point(origin.X, 0), new Point(origin.X, size.Height));
            Text(dc, "x", new Point(size.Width - 10, origin.Y - 10), Text2Brush, Small, 11);
            Text(dc, "y", new Point(origin.X + 10, 9), Text2Brush, Small, 11);
            // (a unit's tick, when the scale shows one)
            if (scale > 14)
            {
                var step = scale < 30 ? 5 : 1;
                for (var i = -40; i <= 40; i += step)
                {
                    if (i == 0) continue;
                    var tx = origin.X + i * scale;
                    var ty = origin.Y - i * scale;
                    if (tx > 0 && tx < size.Width) dc.DrawLine(axis, new Point(tx, origin.Y - 3), new Point(tx, origin.Y + 3));
                    if (ty > 0 && ty < size.Height) dc.DrawLine(axis, new Point(origin.X - 3, ty), new Point(origin.X + 3, ty));
                }
            }
        }
        // (the vectors, and the resultant dashed from the origin)
        foreach (var (name, from, to, colour) in arrows)
        {
            var brush = K.Brush(colour);
            var a = P(Project(from));
            var b = P(Project(to));
            Arrow(dc, a, b, brush, false);
            var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            var nx = -(b.Y - a.Y);
            var ny = b.X - a.X;
            var nl = Math.Max(1, Math.Sqrt(nx * nx + ny * ny));
            Text(dc, name, new Point(mid.X + nx / nl * 11, mid.Y + ny / nl * 11), brush, Face, 13);
        }
        if (resultant is { } sum)
        {
            var b = P(Project(sum));
            Arrow(dc, origin, b, TextBrush, true);
            Text(dc, "R", new Point(b.X + 10, b.Y - 10), TextBrush, Face, 13);
        }
        dc.DrawEllipse(Text2Brush, null, origin, 2.5, 2.5);
    }
}

/// <summary>
/// Vectors in its pin's flyout (the Mac's VectorsCompact): two 2D vectors by their components → the resultant, its
/// magnitude and its angle; Open full screen hands the whole tool the two.
/// </summary>
internal sealed class VectorsQuick : ContentControl
{
    public VectorsQuick(ToolContext c)
    {
        Focusable = false;
        var kept = VectorMath.Parse(PhysPrefs.String("vectors", "quick"));
        var a = kept.Count > 0 ? kept[0] : new Vec(3, 4);
        var b = kept.Count > 1 ? kept[1] : new Vec(1, 2);
        var result = Everyday.Figures("—", 24, FontWeights.Bold);
        var line = K.Wrapped("", Ty.Body, B.Text2);
        TextBox ax = null!, ay = null!, bx = null!, by = null!;
        ax = MotionWords.Box(PhysMath.Ascii(PhysMath.Text(a.X, 6)), "x", Update);
        ay = MotionWords.Box(PhysMath.Ascii(PhysMath.Text(a.Y, 6)), "y", Update);
        bx = MotionWords.Box(PhysMath.Ascii(PhysMath.Text(b.X, 6)), "x", Update);
        by = MotionWords.Box(PhysMath.Ascii(PhysMath.Text(b.Y, 6)), "y", Update);
        AutomationProperties.SetName(ax, "A x");
        AutomationProperties.SetName(ay, "A y");
        AutomationProperties.SetName(bx, "B x");
        AutomationProperties.SetName(by, "B y");
        FrameworkElement Row(string name, TextBox x, TextBox y, Color colour)
        {
            var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = K.Brush(colour), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            return K.Columns("auto, *, 8, *", dot.Margin(0, 0, 10, 0).Col(0), x.Col(1), y.Col(3));
        }
        void Update()
        {
            if (ax == null || ay == null || bx == null || by == null) return;
            double? x1 = PhysMath.Number(ax.Text), y1 = PhysMath.Number(ay.Text), x2 = PhysMath.Number(bx.Text), y2 = PhysMath.Number(by.Text);
            if (x1 == null || y1 == null || x2 == null || y2 == null) { result.Text = "—"; line.Text = "Fill in both vectors."; return; }
            var va = new Vec(x1.Value, y1.Value);
            var vb = new Vec(x2.Value, y2.Value);
            PhysPrefs.Set("vectors", "quick", VectorMath.Write(new[] { va, vb }, false));
            var r = va + vb;
            result.Text = $"A + B = {r.Text(false)}";
            line.Text = $"|R| = {PhysMath.Text(r.Magnitude)} at {VectorWords.Angle(r.AngleDeg, false)} · A·B = {PhysMath.Text(VectorMath.Dot(va, vb))}";
            AutomationProperties.SetName(result, $"Resultant {r.Text(false)}");
        }
        MotionWords.Copyable(result, () => $"{result.Text} · {line.Text}");
        c.Carry = () => PhysMath.Number(ax.Text) is { } x1 && PhysMath.Number(ay.Text) is { } y1 && PhysMath.Number(bx.Text) is { } x2 && PhysMath.Number(by.Text) is { } y2
            ? new Dictionary<string, string> { ["vectors"] = VectorMath.Write(new[] { new Vec(x1, y1), new Vec(x2, y2) }, false) }
            : null;
        Update();
        Content = K.V(10, Row("A", ax, ay, VectorColours.Of(0)), Row("B", bx, by, VectorColours.Of(1)), Everyday.Shrinks(result), line);
    }
}
