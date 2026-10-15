using System.Numerics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// The Matrix Calculator (tools; the Mac's MatrixCalculatorTool): two matrices A and B in editors of cells (1 to 6 rows
// and columns, Tab from cell to cell, a block pasted fills one), an operation chosen, and the answer — exact fractions
// when the cells are integers or fractions, decimals otherwise or at a toggle — with the row operations of a reduction
// listed. The maths is Model/Matrix.cs.

#pragma warning disable CA2255
internal static class MatrixCalculatorRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("matrix", c => new MatrixCalculatorTool(c).View, c => new MatrixCalculatorCompact(c).View);
}
#pragma warning restore CA2255

/// <summary>
/// What the Matrix Calculator is showing, kept on this PC (Prefs <c>SimplMatrix.&lt;field&gt;</c>): A and B as text
/// ("1 2; 3 4"), the operation, k and n, and whether answers show as fractions.
/// </summary>
internal sealed class MatrixState
{
    public static MatrixState Shared { get; } = new();

    /// <summary>The operations in the order shown, by key.</summary>
    public static readonly (string Key, string Label)[] Ops =
    {
        ("add", "A + B"), ("sub", "A − B"), ("mul", "A × B"), ("scale", "kA"), ("transpose", "Aᵀ"), ("det", "det A"),
        ("inverse", "A⁻¹"), ("rank", "rank A"), ("rref", "RREF A"), ("trace", "trace A"), ("solve", "Solve A·x = B"),
        ("eigen", "Eigenvalues of A"), ("power", "Aⁿ"),
    };

    public string A { get; private set; }
    public string B { get; private set; }
    public string Op { get; private set; }
    public string K { get; private set; }
    public string N { get; private set; }
    public bool Fractions { get; private set; }

    public event Action? Changed;

    private MatrixState()
    {
        A = Prefs.String("SimplMatrix.a") ?? "1 2; 3 4";
        B = Prefs.String("SimplMatrix.b") ?? "3; 5";
        Op = Prefs.String("SimplMatrix.op") is { } op && Ops.Any(o => o.Key == op) ? op : "det";
        K = Prefs.String("SimplMatrix.k") ?? "2";
        N = Prefs.String("SimplMatrix.n") ?? "2";
        Fractions = Prefs.Bool("SimplMatrix.fractions", true);
    }

    private void Keep(string field, string value) { Prefs.Set($"SimplMatrix.{field}", value); Changed?.Invoke(); }

    public void SetA(string t) { if (A == t) return; A = t; Keep("a", t); }
    public void SetB(string t) { if (B == t) return; B = t; Keep("b", t); }
    public void SetOp(string op) { if (Op == op || Ops.All(o => o.Key != op)) return; Op = op; Keep("op", op); }
    public void SetK(string t) { if (K == t) return; K = t; Keep("k", t); }
    public void SetN(string t) { if (N == t) return; N = t; Keep("n", t); }
    public void SetFractions(bool on) { if (Fractions == on) return; Fractions = on; Prefs.Set("SimplMatrix.fractions", on); Changed?.Invoke(); }

    /// <summary>The screenshot suite's start: --matrix-a "1 2; 3 4", --matrix-b "3; 5", --matrix-op det (or the
    /// operation as --tool-state).</summary>
    public void FromLaunch(string? state)
    {
        if (Launch.Take("", "matrix-a") is { Length: > 0 } a) SetA(a);
        if (Launch.Take("", "matrix-b") is { Length: > 0 } b) SetB(b);
        var op = Launch.Take("", "matrix-op") ?? state;
        if (op is { Length: > 0 }) SetOp(op.Trim().ToLowerInvariant());
    }
}

/// <summary>What an operation came to: a matrix, a number, eigenvalues, with the steps of a reduction — or why not.</summary>
internal sealed class MatrixAnswer
{
    public string Title = "";
    public Mat? Matrix;
    public Rational? Scalar;
    public Complex[]? Eigenvalues;
    public List<string>? Steps;
    public string? Error;
    public string? Note;
    /// <summary>Every input was an integer or a fraction, so the answer is exact.</summary>
    public bool Exact = true;
}

/// <summary>The sums behind the views: the operation chosen done on A and B as typed.</summary>
internal static class MatrixWork
{
    public static MatrixAnswer Do(string op, string aText, string bText, string kText, string nText)
    {
        var r = new MatrixAnswer { Title = MatrixState.Ops.FirstOrDefault(o => o.Key == op).Label ?? op };
        var a = MatrixParse.Parse(aText, out var exactA, out var errA);
        if (a == null) { r.Error = errA ?? "Fill in A."; return r; }
        r.Exact = exactA;
        Mat? NeedB()
        {
            var b = MatrixParse.Parse(bText, out var exactB, out var errB);
            if (b == null) r.Error = errB ?? "Fill in B.";
            else if (!exactB) r.Exact = false;
            return b;
        }
        try
        {
            switch (op)
            {
                case "add": if (NeedB() is { } b1) r.Matrix = a.Add(b1); break;
                case "sub": if (NeedB() is { } b2) r.Matrix = a.Subtract(b2); break;
                case "mul": if (NeedB() is { } b3) r.Matrix = a.Multiply(b3); break;
                case "scale":
                    var k = Rational.Parse(kText, out var exactK);
                    if (k == null) { r.Error = "Give k, the number to multiply A by."; break; }
                    if (!exactK) r.Exact = false;
                    r.Matrix = a.Scale(k.Value);
                    r.Title = $"{k.Value.Text()}A";
                    break;
                case "transpose": r.Matrix = a.Transpose(); break;
                case "det": r.Scalar = a.Determinant(); break;
                case "inverse":
                    r.Matrix = a.Inverse();
                    if (r.Matrix == null) r.Error = "A is singular (its determinant is 0): it has no inverse.";
                    break;
                case "rank": r.Scalar = new Rational(a.Rank()); break;
                case "rref":
                    var (rref, steps, rank, _) = a.Rref();
                    r.Matrix = rref;
                    r.Steps = steps;
                    r.Note = $"Rank {rank}" + (steps.Count == 0 ? ": already in reduced row echelon form." : $", in {ToolKit.Plural(steps.Count, "row operation")}.");
                    break;
                case "trace": r.Scalar = a.Trace(); break;
                case "solve":
                    if (NeedB() is { } b4)
                    {
                        var (x, why) = a.Solve(b4);
                        r.Matrix = x;
                        r.Error = why;
                        if (b4.Cols > 1) r.Note = "B's first column is b.";
                    }
                    break;
                case "eigen":
                    r.Eigenvalues = a.Eigenvalues();
                    r.Exact = false;
                    break;
                case "power":
                    if (!int.TryParse(nText.Trim().Replace("−", "-"), out var n) || Math.Abs(n) > 64) { r.Error = "Give n, a whole number from −64 to 64."; break; }
                    r.Matrix = a.Power(n);
                    r.Title = n < 0 ? $"A⁻{Superscript(-n)}" : $"A{Superscript(n)}";
                    break;
                default: r.Error = "Choose an operation."; break;
            }
        }
        catch (MatrixError e) { r.Error = e.Message; }
        return r;
    }

    /// <summary>"²", "¹²".</summary>
    public static string Superscript(int n) => string.Concat(n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(ch => "⁰¹²³⁴⁵⁶⁷⁸⁹"[ch - '0']));

    /// <summary>"₁", "₁₂" (an eigenvalue's number).</summary>
    public static string Subscript(int n) => string.Concat(n.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(ch => "₀₁₂₃₄₅₆₇₈₉"[ch - '0']));

    /// <summary>A cell as words: a fraction, or a decimal to six figures.</summary>
    public static string Cell(Rational v, bool fractions) => fractions ? v.Text() : StatsMath.Sig(v.ToDouble());

    /// <summary>The answer as plain text for the clipboard.</summary>
    public static string Report(MatrixAnswer r, bool fractions)
    {
        if (r.Error != null) return r.Error;
        if (r.Matrix != null) return r.Matrix.Tabbed(fractions);
        if (r.Scalar is { } s) return fractions ? s.Ascii() : StatsMath.Ascii(s.ToDouble());
        if (r.Eigenvalues != null) return string.Join("\n", r.Eigenvalues.Select(Eigen.Text)).Replace("−", "-");
        return "";
    }

    /// <summary>Matrix Calculator's own colour (its tile's).</summary>
    public static System.Windows.Media.Color Tint => ToolsCenter.KindOf("matrix")?.Color ?? K.Hex("#5e5ce6");
}

/// <summary>
/// A matrix in cells (the Mac's MatrixEditor): its name and size over a grid of boxes between brackets, − and + for its
/// rows and columns (1 to 6), Tab moving from cell to cell, a block pasted into any cell (rows on lines or after
/// semicolons, cells apart by spaces, commas or tabs) filling and sizing the whole matrix.
/// </summary>
internal sealed class MatrixEditor
{
    private readonly Grid cells = new();
    private readonly TextBlock size = K.Text("", Ty.Body, B.Text2);
    private readonly Action<string> changed;
    private readonly string name;
    private readonly int most;
    private TextBox[,] boxes = new TextBox[0, 0];
    private int rows, cols;
    private bool syncing;

    public FrameworkElement View { get; }

    public MatrixEditor(string name, string text, Action<string> changed, int most = 6, bool steppers = true)
    {
        this.name = name;
        this.changed = changed;
        this.most = most;
        size.FontFeatures();
        var head = K.H(10, K.Text(name, Ty.BodyStrong), size);
        var stepper = K.H(4,
            K.Text("Rows", Ty.Caption, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center),
            K.IconButton(SymbolRegular.Subtract24, () => Resize(rows - 1, cols), $"{name}: one row fewer", 14, 28),
            K.IconButton(SymbolRegular.Add24, () => Resize(rows + 1, cols), $"{name}: one row more", 14, 28),
            K.Text("Columns", Ty.Caption, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Margin(8, 0, 0, 0),
            K.IconButton(SymbolRegular.Subtract24, () => Resize(rows, cols - 1), $"{name}: one column fewer", 14, 28),
            K.IconButton(SymbolRegular.Add24, () => Resize(rows, cols + 1), $"{name}: one column more", 14, 28));
        var left = new Border { Width = 8, BorderThickness = new Thickness(2, 2, 0, 2), CornerRadius = new CornerRadius(4, 0, 0, 4), Margin = new Thickness(0, 0, 4, 0) };
        var right = new Border { Width = 8, BorderThickness = new Thickness(0, 2, 2, 2), CornerRadius = new CornerRadius(0, 4, 4, 0), Margin = new Thickness(4, 0, 0, 0) };
        left.SetResourceReference(Border.BorderBrushProperty, B.Text2);
        right.SetResourceReference(Border.BorderBrushProperty, B.Text2);
        var bracketed = K.Columns("auto, auto, auto", left.Col(0), cells.Col(1), right.Col(2));
        bracketed.HorizontalAlignment = HorizontalAlignment.Left;
        View = K.V(8, steppers ? K.Columns("*, auto", head.Col(0), stepper.Col(1)) : head, bracketed);
        SetText(text);
    }

    /// <summary>The cells as text: rows after semicolons, cells apart by spaces, an empty cell 0.</summary>
    public string Text
    {
        get
        {
            var lines = new List<string>();
            for (var i = 0; i < rows; i++)
            {
                var parts = new List<string>();
                for (var j = 0; j < cols; j++) parts.Add(boxes[i, j].Text.Trim().Length == 0 ? "0" : boxes[i, j].Text.Trim());
                lines.Add(string.Join(" ", parts));
            }
            return string.Join("; ", lines);
        }
    }

    /// <summary>The cells filled from text (a launch's, a paste, the state's): the grid sized to it.</summary>
    public void SetText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries)).Where(l => l.Length > 0).ToList();
        var r = Math.Clamp(lines.Count, 1, most);
        var c = Math.Clamp(lines.Count == 0 ? 1 : lines.Max(l => l.Length), 1, most);
        Build(r, c);
        syncing = true;
        for (var i = 0; i < rows; i++)
            for (var j = 0; j < cols; j++)
                boxes[i, j].Text = i < lines.Count && j < lines[i].Length ? lines[i][j] : "0";
        syncing = false;
    }

    private void Resize(int r, int c)
    {
        r = Math.Clamp(r, 1, most);
        c = Math.Clamp(c, 1, most);
        if (r == rows && c == cols) return;
        var kept = new string[r, c];
        for (var i = 0; i < r; i++) for (var j = 0; j < c; j++) kept[i, j] = i < rows && j < cols ? boxes[i, j].Text : "0";
        Build(r, c);
        syncing = true;
        for (var i = 0; i < r; i++) for (var j = 0; j < c; j++) boxes[i, j].Text = kept[i, j];
        syncing = false;
        changed(Text);
    }

    private void Build(int r, int c)
    {
        if (r == rows && c == cols) return;
        rows = r;
        cols = c;
        size.Text = $"{rows} × {cols}";
        cells.Children.Clear();
        cells.RowDefinitions.Clear();
        cells.ColumnDefinitions.Clear();
        for (var i = 0; i < rows; i++) cells.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var j = 0; j < cols; j++) cells.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        boxes = new TextBox[rows, cols];
        for (var i = 0; i < rows; i++)
            for (var j = 0; j < cols; j++)
            {
                var box = new TextBox { Text = "0", PlaceholderText = "0", Width = 64, MinHeight = 32, ClearButtonEnabled = false, TextAlignment = TextAlignment.Center, Margin = new Thickness(j == 0 ? 0 : 6, i == 0 ? 0 : 6, 0, 0) };
                AutomationProperties.SetName(box, $"{name} row {i + 1} column {j + 1}");
                box.TextChanged += (_, _) => { if (!syncing) changed(Text); };
                box.GotKeyboardFocus += (_, _) => box.SelectAll();
                DataObject.AddPastingHandler(box, (_, e) =>
                {
                    // (a block pasted — rows on lines, cells apart by tabs — fills the whole matrix, as a spreadsheet's does)
                    if (e.DataObject.GetData(DataFormats.UnicodeText) is not string t || !(t.Contains('\n') || t.Contains('\t') || t.Contains(';') || t.Trim().Contains(' '))) return;
                    e.CancelCommand();
                    SetText(t);
                    changed(Text);
                });
                boxes[i, j] = box;
                cells.Children.Add(box.Row(i).Col(j));
            }
    }
}

/// <summary>
/// The Matrix Calculator (the Mac's MatrixCalculatorTool): A and B in their editors with k and n beside them, the
/// operations as a row of buttons (the one chosen in the accent), and the answer at the right — a matrix between
/// brackets, a number large, or the eigenvalues listed — as fractions or decimals, with a reduction's steps under it.
/// </summary>
internal sealed class MatrixCalculatorTool
{
    private readonly MatrixState s = MatrixState.Shared;
    private readonly MatrixEditor a, b;
    private readonly TextBox k, n;
    private readonly WrapPanel ops = new() { Orientation = Orientation.Horizontal };
    private readonly SelectorBar shown;
    private readonly TextBlock title = K.Text("", Ty.BodyStrong);
    private readonly ContentControl answer = new() { Focusable = false }, steps = new() { Focusable = false };
    private readonly TextBlock note = K.Wrapped("", Ty.Caption, B.Text2);
    private readonly Wpf.Ui.Controls.Button copy;
    private MatrixAnswer? last;
    private bool syncing;
    private string said = "";

    public FrameworkElement View { get; }

    public MatrixCalculatorTool(ToolContext c)
    {
        s.FromLaunch(Everyday.LaunchState("matrix"));
        // (opened full screen from its pin: the pin's A)
        if (c.TakeInfo("a") is { Length: > 0 } carried) s.SetA(carried);
        a = new MatrixEditor("A", s.A, t => { if (!syncing) s.SetA(t); });
        b = new MatrixEditor("B", s.B, t => { if (!syncing) s.SetB(t); });
        k = ToolKit.Box(s.K, "2", t => { if (!syncing) s.SetK(t); });
        n = ToolKit.Box(s.N, "2", t => { if (!syncing) s.SetN(t); });
        AutomationProperties.SetName(k, "k, the number to multiply A by");
        AutomationProperties.SetName(n, "n, the power");
        var numbers = K.H(16, ToolKit.Field("k", k, width: 90), ToolKit.Field("n", n, width: 90));
        var editors = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var e in new[] { a.View, b.View, numbers }) { e.Margin = new Thickness(0, 0, 28, 12); editors.Children.Add(e); }
        var hint = K.Wrapped("Tab moves from cell to cell. Paste a block of numbers into any cell to fill a matrix. Fractions like 3/4 stay exact.", Ty.Caption, B.Text3);
        foreach (var (key, label) in MatrixState.Ops)
        {
            var op = key;
            var button = K.Button(label, () => s.SetOp(op));
            button.MinWidth = 0;
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Tag = op;
            ops.Children.Add(button);
        }
        var main = K.V(20, ToolKit.Section("Matrices", K.V(4, editors, hint)), ToolKit.Section("Operation", ops));

        shown = new SelectorBar(new[] { "Fractions", "Decimals" }, s.Fractions ? 0 : 1) { HorizontalAlignment = HorizontalAlignment.Right };
        shown.Changed += i => s.SetFractions(i == 0);
        copy = K.Button("Copy", () => { if (last != null) Everyday.Copy(MatrixWork.Report(last, s.Fractions && last.Exact)); }, icon: SymbolRegular.Copy24);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        var head = K.Columns("*, auto", title.Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), shown.Col(1));
        var side = K.Card(K.V(12, head, answer, note, steps, copy), new Thickness(16));
        View = Everyday.Page(ToolKit.Columns(main, side, 400, 900, sideFirst: true));
        Update();
        Everyday.Follow(View, () => { s.Changed += Update; Update(); }, () => s.Changed -= Update);
    }

    private void Update()
    {
        syncing = true;
        if (a.Text != s.A) a.SetText(s.A);
        if (b.Text != s.B) b.SetText(s.B);
        if (k.Text != s.K) k.Text = s.K;
        if (n.Text != s.N) n.Text = s.N;
        syncing = false;
        foreach (var child in ops.Children.OfType<Wpf.Ui.Controls.Button>())
        {
            var on = child.Tag as string == s.Op;
            child.Appearance = on ? ControlAppearance.Primary : ControlAppearance.Secondary;
            child.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
        var r = MatrixWork.Do(s.Op, s.A, s.B, s.K, s.N);
        last = r;
        var fractions = s.Fractions && r.Exact;
        if (shown.Selected != (fractions ? 0 : 1)) shown.Select(fractions ? 0 : 1, false);
        shown.IsEnabled = r.Exact;
        shown.ToolTip = r.Exact ? null : "Decimals: an input has decimals, or the answer is not exact.";
        title.Text = r.Title;
        answer.Content = MatrixParts.Show(r, fractions);
        note.Text = r.Note ?? "";
        note.Visibility = r.Note == null ? Visibility.Collapsed : Visibility.Visible;
        steps.Content = r.Steps is { Count: > 0 } list
            ? new Expander { Header = $"Steps ({list.Count})", IsExpanded = true, Content = K.V(4, list.Select((step, i) => (UIElement)K.Text($"{i + 1}.  {step}", Ty.Body, B.Text2)).ToArray()).Margin(8, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Stretch }
            : null;
        copy.IsEnabled = r.Error == null;
        if (Shot.Asked && said != s.Op) { said = s.Op; Log.Write($"matrix: shows {s.Op}"); }
    }
}

/// <summary>The answer drawn: a matrix between brackets, a number large, eigenvalues listed, or the trouble.</summary>
internal static class MatrixParts
{
    public static FrameworkElement Show(MatrixAnswer r, bool fractions)
    {
        if (r.Error != null) return ToolKit.Note(r.Error);
        if (r.Matrix != null) return Everyday.Shrinks(Bracketed(r.Matrix, fractions));
        if (r.Scalar is { } v)
        {
            var big = Everyday.Figures(MatrixWork.Cell(v, fractions), 40, FontWeights.Bold);
            big.Foreground = K.Brush(MatrixWork.Tint);
            AutomationProperties.SetName(big, $"{r.Title}: {MatrixWork.Cell(v, fractions)}");
            return Everyday.Shrinks(big);
        }
        if (r.Eigenvalues != null)
        {
            var list = K.V(0);
            for (var i = 0; i < r.Eigenvalues.Length; i++)
            {
                if (i > 0) list.Children.Add(K.Divider());
                list.Children.Add(StatsPage.Row($"λ{MatrixWork.Subscript(i + 1)}", Eigen.Text(r.Eigenvalues[i])));
            }
            return list;
        }
        return K.Wrapped("", Ty.Body);
    }

    /// <summary>A matrix's cells in a grid between brackets, each cell in tabular figures.</summary>
    public static FrameworkElement Bracketed(Mat m, bool fractions, double size = 16)
    {
        var grid = new Grid();
        for (var i = 0; i < m.Rows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var j = 0; j < m.Cols; j++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < m.Rows; i++)
            for (var j = 0; j < m.Cols; j++)
            {
                var t = Everyday.Figures(MatrixWork.Cell(m[i, j], fractions), size, FontWeights.Normal);
                t.TextAlignment = TextAlignment.Right;
                t.HorizontalAlignment = HorizontalAlignment.Right;
                t.Margin = new Thickness(j == 0 ? 6 : 14, 3, j == m.Cols - 1 ? 6 : 0, 3);
                grid.Children.Add(t.Row(i).Col(j));
            }
        var left = new Border { Width = 8, BorderThickness = new Thickness(2, 2, 0, 2), CornerRadius = new CornerRadius(4, 0, 0, 4) };
        var right = new Border { Width = 8, BorderThickness = new Thickness(0, 2, 2, 2), CornerRadius = new CornerRadius(0, 4, 4, 0) };
        left.SetResourceReference(Border.BorderBrushProperty, B.Text);
        right.SetResourceReference(Border.BorderBrushProperty, B.Text);
        var g = K.Columns("auto, auto, auto", left.Col(0), grid.Col(1), right.Col(2));
        g.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(g, m.Tabbed(fractions).Replace('\t', ' ').Replace('\n', ';'));
        return g;
    }
}

/// <summary>The Matrix Calculator's pin, opened (the Mac's MatrixCalculatorCompact): a 2×2 or 3×3 A with its
/// determinant and inverse; Open full screen hands the whole tool this A.</summary>
internal sealed class MatrixCalculatorCompact
{
    /// <summary>A as typed, kept while the flyout comes and goes.</summary>
    private static string keptA = "1 2; 3 4";

    private readonly MatrixEditor a;
    private readonly SelectorBar size;
    private readonly TextBlock det = Everyday.Figures("—", 26, FontWeights.Bold);
    private readonly ContentControl inverse = new() { Focusable = false };
    private bool syncing;

    public FrameworkElement View { get; }

    public MatrixCalculatorCompact(ToolContext c)
    {
        if (Launch.Take("", "matrix-a") is { Length: > 0 } launched) keptA = launched;
        a = new MatrixEditor("A", keptA, t => { if (!syncing) { keptA = t; Update(); } }, most: 3, steppers: false);
        var rows = keptA.Split(';').Length;
        size = new SelectorBar(new[] { "2 × 2", "3 × 3" }, rows >= 3 ? 1 : 0) { HorizontalAlignment = HorizontalAlignment.Left };
        size.Changed += i =>
        {
            var want = i == 0 ? 2 : 3;
            var m = MatrixParse.Parse(keptA, out _, out _);
            var next = new Mat(want, want);
            for (var r = 0; r < want; r++) for (var col = 0; col < want; col++) next[r, col] = m != null && r < m.Rows && col < m.Cols ? m[r, col] : (r == col ? Rational.One : Rational.Zero);
            keptA = next.Ascii();
            syncing = true;
            a.SetText(keptA);
            syncing = false;
            Update();
        };
        det.ContextMenu = MoreKit.Menu(("Copy", SymbolRegular.Copy24, () => Everyday.Copy(det.Text.Replace("−", "-"))));
        var figures = K.Columns("auto, 16, *", K.V(0, Everyday.Shrinks(det), K.Text("det A", Ty.Caption, B.Text2)).Col(0), K.V(4, inverse, K.Text("A⁻¹", Ty.Caption, B.Text2)).Col(2));
        View = K.V(10, size, a.View, figures);
        c.Carry = () => new Dictionary<string, string> { ["a"] = keptA };
        Update();
    }

    private void Update()
    {
        var m = MatrixParse.Parse(keptA, out var exact, out var error);
        if (m == null) { det.Text = "—"; inverse.Content = K.Wrapped(error ?? "Fill in A.", Ty.Caption, B.Text2); return; }
        try
        {
            det.Text = MatrixWork.Cell(m.Determinant(), exact);
            var inv = m.Inverse();
            inverse.Content = inv == null ? K.Wrapped("None: A is singular.", Ty.Caption, B.Text2) : Everyday.Shrinks(MatrixParts.Bracketed(inv, exact, 13));
        }
        catch (MatrixError e) { det.Text = "—"; inverse.Content = K.Wrapped(e.Message, Ty.Caption, B.Text2); }
    }
}
