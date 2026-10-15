using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// What the chemistry calculators share (the Equation Balancer, Molar Mass & Stoichiometry, Gas Laws, Solutions & pH):
// a formula or an equation typeset with its subscripts and charges, a field that remembers itself and computes as it is
// typed in (a moment after the last key, or at once on Return), example chips, a table with its hairlines, a result
// row, a problem said in plain words under the field, and Ctrl+C copying the result when no field has the keyboard.

/// <summary>The pieces the chemistry calculators are made of.</summary>
internal static class ChemCalc
{
    /// <summary>Something done a moment after the last of a run of changes (typing), or at once (Return).</summary>
    public sealed class Later
    {
        private readonly DispatcherTimer timer;
        private readonly Action act;

        public Later(Action act, int ms = 150)
        {
            this.act = act;
            timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += (_, _) => { timer.Stop(); act(); };
        }

        public void Ask() { timer.Stop(); timer.Start(); }
        public void Now() { timer.Stop(); act(); }
    }

    /// <summary>A formula typeset in a text block: counts as subscripts, the charge as a superscript.</summary>
    public static void AddFormula(InlineCollection into, IEnumerable<ChemRun> runs, double size, FontWeight weight)
    {
        foreach (var r in runs)
        {
            var run = new Run(r.Text) { FontSize = r.Sub || r.Sup ? size * 0.68 : size, FontWeight = weight };
            if (r.Sub) run.BaselineAlignment = BaselineAlignment.Subscript;
            if (r.Sup) run.BaselineAlignment = BaselineAlignment.Superscript;
            into.Add(run);
        }
    }

    /// <summary>A formula on its own, typeset.</summary>
    public static TextBlock Formula(IEnumerable<ChemRun> runs, double size, FontWeight? weight = null, string brush = B.Text)
    {
        var t = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap, FontFamily = K.TextFace };
        t.Fg(brush);
        AddFormula(t.Inlines, runs, size, weight ?? FontWeights.Normal);
        return t;
    }

    /// <summary>
    /// An equation typeset: each species with its coefficient in bold before it (a coefficient of 1 left out), + between
    /// species and an arrow between the sides.
    /// </summary>
    public static TextBlock Equation(BalanceResult r, double size, bool coefficients = true)
    {
        var t = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap, FontFamily = K.TextFace };
        t.Fg(B.Text);
        void Side(IReadOnlyList<ChemFormula> side, int offset)
        {
            for (var i = 0; i < side.Count; i++)
            {
                if (i > 0) t.Inlines.Add(new Run(" + ") { FontSize = size });
                var c = coefficients && r.Coefficients.Count > offset + i ? r.Coefficients[offset + i] : 1;
                if (c != 1) t.Inlines.Add(new Run(c.ToString(System.Globalization.CultureInfo.InvariantCulture)) { FontSize = size, FontWeight = FontWeights.Bold });
                AddFormula(t.Inlines, side[i].Runs, size, FontWeights.Normal);
                if (side[i].Phase is { } p) t.Inlines.Add(new Run($"({p})") { FontSize = size * 0.68, BaselineAlignment = BaselineAlignment.Subscript });
            }
        }
        Side(r.Reactants, 0);
        var arrow = new Run(" → ") { FontSize = size };
        arrow.SetResourceReference(TextElement.ForegroundProperty, B.Text2);
        t.Inlines.Add(arrow);
        Side(r.Products, r.Reactants.Count);
        AutomationProperties.SetName(t, r.PlainText);
        return t;
    }

    /// <summary>
    /// A field that remembers what was typed in it (ChemPrefs, per tool and field) and asks for the sums a moment after
    /// each change — Return asks at once. The text given is what it starts with; null, what it remembers.
    /// </summary>
    public static TextBox Field(string tool, string field, string placeholder, Later later, string? start = null, string? name = null)
    {
        var box = ToolKit.Box(start ?? ChemPrefs.String(tool, field) ?? "", placeholder);
        box.TextChanged += (_, _) => { ChemPrefs.Set(tool, field, box.Text); later.Ask(); };
        box.PreviewKeyDown += (_, a) => { if (a.Key == Key.Enter) { later.Now(); a.Handled = true; } };
        AutomationProperties.SetName(box, name ?? placeholder);
        return box;
    }

    /// <summary>A menu of units (or choices) that remembers itself; the tag of each is its text.</summary>
    public static ComboBox Choice(string tool, string field, IReadOnlyList<string> choices, string fallback, Later later, string? start = null, double? width = null)
    {
        var box = new ComboBox { MinWidth = 0 };
        if (width is { } w) box.Width = w; else box.HorizontalAlignment = HorizontalAlignment.Stretch;
        foreach (var c in choices) box.Items.Add(Everyday.Item(c, c));
        var chosen = start ?? ChemPrefs.String(tool, field);
        Everyday.Choose(box, choices.Contains(chosen ?? "") ? chosen : fallback);
        box.SelectionChanged += (_, _) => { if (Everyday.Chosen(box) is { } c) { ChemPrefs.Set(tool, field, c); later.Now(); } };
        AutomationProperties.SetName(box, field);
        return box;
    }

    public static string Chosen(ComboBox box, string fallback) => Everyday.Chosen(box) ?? fallback;

    /// <summary>A field with a unit menu after it ("Volume [____] [L ▾]").</summary>
    public static FrameworkElement WithUnit(string label, TextBox box, ComboBox units, string? hint = null)
    {
        units.Width = 92;
        units.Margin = new Thickness(8, 0, 0, 0);
        var row = K.Columns("*, auto", box.Col(0), units.Col(1));
        var v = K.V(6, K.Text(label, Ty.BodyStrong, B.Text2), row);
        if (hint != null) v.Children.Add(K.Wrapped(hint, Ty.Caption, B.Text2));
        return v;
    }

    /// <summary>Example chips: a press puts the example in the field.</summary>
    public static FrameworkElement Chips(IEnumerable<(string Text, Action Pick)> chips, string? label = null)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        if (label != null) wrap.Children.Add(K.Text(label, Ty.Caption, B.Text2).Margin(0, 0, 8, 8).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
        foreach (var (text, pick) in chips)
        {
            var b = new Button { Content = text, Appearance = ControlAppearance.Secondary, MinWidth = 0, Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 8, 8), FontSize = 13 };
            b.Click += (_, e) => { e.Handled = true; pick(); };
            b.ToolTip = $"Try {text}";
            wrap.Children.Add(b);
        }
        return wrap;
    }

    /// <summary>A table: its headings in the caption colour, a hairline under each row, numbers right-aligned in tabular
    /// figures. A cell can be any element (a typeset formula) or a string.</summary>
    public static FrameworkElement Table(string[] headings, IEnumerable<object?[]> rows, bool[]? numeric = null)
    {
        var cols = string.Join(", ", headings.Select((_, i) => i == 0 ? "*" : "auto"));
        var v = K.V(0);
        var head = K.Columns(cols);
        for (var i = 0; i < headings.Length; i++)
        {
            var t = K.Text(headings[i], Ty.Caption, B.Text2);
            if (i > 0) t.Margin = new Thickness(16, 0, 0, 0);
            if (numeric != null && numeric[i]) t.TextAlignment = TextAlignment.Right;
            head.Children.Add(t.Col(i));
        }
        head.Margin = new Thickness(12, 8, 12, 6);
        v.Children.Add(head);
        var any = false;
        foreach (var row in rows)
        {
            v.Children.Add(K.Divider(12));
            var g = K.Columns(cols);
            for (var i = 0; i < row.Length && i < headings.Length; i++)
            {
                FrameworkElement cell;
                if (row[i] is FrameworkElement fe) cell = fe;
                else
                {
                    var t = K.Text(row[i]?.ToString() ?? "", Ty.Body);
                    if (numeric != null && numeric[i]) { t.TextAlignment = TextAlignment.Right; Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular); }
                    cell = t;
                }
                if (i > 0) cell.Margin = new Thickness(16, 0, 0, 0);
                cell.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(cell.Col(i));
            }
            g.Margin = new Thickness(12, 7, 12, 7);
            v.Children.Add(g);
            any = true;
        }
        if (!any) v.Children.Add(K.Divider(12));
        return K.Card(v, new Thickness(0));
    }

    /// <summary>A tick (equal, right) or a cross (not yet) in its tone, for a table's last column.</summary>
    public static FrameworkElement Mark(bool ok)
    {
        var i = K.Icon(ok ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.DismissCircle24, 16, ok ? ChemKit.Green : ChemKit.Red, filled: true);
        AutomationProperties.SetName(i, ok ? "equal" : "not equal");
        return i;
    }

    /// <summary>A result: its name at the left, its value (tabular figures) at the right, in bold.</summary>
    public static FrameworkElement Result(string name, string value, Color? tone = null)
    {
        var v = K.Text(value, Ty.BodyStrong);
        v.TextAlignment = TextAlignment.Right;
        v.TextWrapping = TextWrapping.Wrap;
        Typography.SetNumeralAlignment(v, FontNumeralAlignment.Tabular);
        if (tone is { } t) v.Foreground = new SolidColorBrush(K.Darkened(t));
        return K.Columns("auto, *", K.Text(name, Ty.Body, B.Text2).Margin(0, 0, 12, 0).Col(0), v.Col(1));
    }

    /// <summary>A problem said under the field, in plain words: never a dialog.</summary>
    public static FrameworkElement Problem(string text) => ToolKit.Note(text, SymbolRegular.ErrorCircle24, ChemKit.Red);

    /// <summary>A quiet line under a field (what to type, what Return does).</summary>
    public static TextBlock Hint(string text)
    {
        var t = K.Wrapped(text, Ty.Caption, B.Text2);
        AutomationProperties.SetLiveSetting(t, AutomationLiveSetting.Polite);
        return t;
    }

    /// <summary>The words under a result as it changes: a note, a problem, or nothing.</summary>
    public static void Say(ContentControl host, string? text, bool problem = false)
    {
        if (string.IsNullOrEmpty(text)) { host.Content = null; host.Visibility = Visibility.Collapsed; return; }
        host.Content = problem ? Problem(text) : Hint(text);
        host.Visibility = Visibility.Visible;
    }

    /// <summary>Ctrl+C with no field holding the keyboard copies the tool's result (plain text).</summary>
    public static void CopyOnCtrlC(UIElement root, Func<string?> text)
    {
        root.PreviewKeyDown += (_, a) =>
        {
            if (a.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) == 0 || a.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase) return;
            if (text() is { Length: > 0 } s) { Everyday.Copy(s); a.Handled = true; }
        };
    }

    /// <summary>The Copy button of a result: copies plain text; disabled with nothing to copy.</summary>
    public static Button Copy(Func<string?> text, string what = "Copy")
    {
        var b = K.Button(what, () => Everyday.Copy(text()), icon: SymbolRegular.Copy24);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        return b;
    }

    /// <summary>A number for the display: the minus sign proper.</summary>
    public static string Shown(string s) => s.Replace("-", "−");

    /// <summary>The tabs across a tool's top (the Mac's segmented control): the one chosen remembered per tool.</summary>
    public static SelectorBar Tabs(string tool, IReadOnlyList<string> names, IReadOnlyList<string> keys, string? asked, Action<int> changed)
    {
        var remembered = ChemPrefs.String(tool, "tab");
        var at = Math.Max(0, keys.ToList().IndexOf(asked ?? remembered ?? keys[0]));
        var bar = new SelectorBar(names, at);
        bar.Changed += i => { ChemPrefs.Set(tool, "tab", keys[i]); changed(i); };
        return bar;
    }

    /// <summary>In a picture run, what the tool shows (CI reads it from the log) — once per change.</summary>
    public static void Shows(string tool, ref string said, string now)
    {
        if (now == said) return;
        said = now;
        if (Shot.Asked || Launch.IsDevelopment) Log.Write($"{tool}: shows {now}");
        Shot.Moved();
    }

    /// <summary>A launch's value for a tool's own flag (--balance "…", --gas-values "…"), once.</summary>
    public static string? Flag(string name) => Launch.Take("", name) is { Length: > 0 } v ? v : null;

    /// <summary>"P=1 atm,V=22.4 L" read into its pairs (a value with its unit after a space).</summary>
    public static Dictionary<string, string> Pairs(string? s)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(s)) return d;
        foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) d[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        return d;
    }

    /// <summary>A value with its unit after a space ("22.4 L"): the number and the unit, the unit matched to the choices
    /// given (none: the fallback).</summary>
    public static (string Value, string Unit) Split(string s, IReadOnlyList<string> units, string fallback)
    {
        var t = s.Trim();
        foreach (var u in units.OrderByDescending(u => u.Length))
            if (t.EndsWith(u, StringComparison.OrdinalIgnoreCase)) return (t[..^u.Length].Trim(), u);
        return (t, fallback);
    }
}
