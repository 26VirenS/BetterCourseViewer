using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

#pragma warning disable CA2255
internal static class CommunityToolRegistration
{
    // (every Simpl Community tool added, "community:<id>", opens whole in its popup as this view)
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.RegisterFamily(CommunityTools.Prefix, CommunityToolView.Whole);
}
#pragma warning restore CA2255

/// <summary>
/// (0.3.105) A Simpl Community tool drawn with Simpl's own controls (docs/community-tools.md): its inputs — a number box
/// with its unit and a stepper that keeps to its min, max and step; a choice as buttons side by side when it has a few
/// options, a menu when it has many; a toggle — and its results worked out as anything is put in (ToolFormat.Run), the
/// first one shown large and the rest under it; then its table and its note, and who made it. Whole, in the tool's popup,
/// it remembers the values last put in (CommunityTools.Values); as a preview in the browser it is the same tool working,
/// smaller, its values its own and forgotten.
/// </summary>
internal sealed class CommunityToolView : ContentControl
{
    private readonly CommunityTool tool;
    private readonly bool preview;
    private readonly Dictionary<string, object?> values;
    private readonly Stack results = K.V(0);
    private readonly Color tint;
    private string? plain;
    private string said = "";

    /// <summary>The tool whole, for its popup (its key "community:&lt;id&gt;").</summary>
    public static FrameworkElement Whole(ToolContext c)
    {
        var id = c.Key[CommunityTools.Prefix.Length..];
        if (CommunityTools.Find(id) is { } tool) return new CommunityToolView(tool, preview: false);
        var gone = K.V(12, K.Icon(SymbolRegular.PeopleCommunity24, 40, B.Text3), K.Text("This tool isn’t on this PC any more", Ty.Subtitle).Center(),
            K.Wrapped("Add it again from Simpl Community on the Tools page.", Ty.Body, B.Text2).Center());
        gone.HorizontalAlignment = HorizontalAlignment.Center;
        gone.VerticalAlignment = VerticalAlignment.Center;
        return new Grid { Children = { gone } };
    }

    public CommunityToolView(CommunityTool tool, bool preview)
    {
        this.tool = tool;
        this.preview = preview;
        Focusable = false;
        tint = K.Hex(tool.Color);
        values = preview || tool.Id == null ? new(StringComparer.Ordinal) : CommunityTools.Values(tool.Id);
        var inputs = K.V(preview ? 10 : 14, tool.Inputs.Select(Field).ToArray());
        var card = Everyday.TintedCard(results, tint, new Thickness(preview ? 14 : 20));
        Recompute();
        if (preview)
        {
            Content = K.V(14, inputs, card);
            return;
        }
        var side = K.V(12, card, ChemCalc.Copy(() => plain, "Copy results"));
        var parts = new List<UIElement> { ToolKit.Columns(inputs, side, 380, 820, sideFirst: true) };
        if (tool.Table is { } table) parts.Add(Table(table));
        parts.Add(Foot());
        Content = ToolKit.Page(K.V(24, parts.ToArray()), 1180);
        ChemCalc.CopyOnCtrlC(this, () => plain);
    }

    // ---- the inputs ----

    private FrameworkElement Field(ToolInput input) => input.Kind switch
    {
        ToolInputKind.Number => NumberField(input),
        ToolInputKind.Choice => ChoiceField(input),
        _ => ToggleField(input),
    };

    private (object? Given, bool Has) Given(ToolInput input) => values.TryGetValue(input.Id, out var g) ? (g, true) : (null, false);

    private static FrameworkElement Labelled(string label, UIElement control, UIElement? hint = null) =>
        K.V(6, K.Text(label, Ty.BodyStrong, B.Text2), control, hint);

    /// <summary>A number as the box shows it: up to ten decimals, in the decimal sign Windows is set to.</summary>
    private static string Plain(double v)
    {
        var s = v.ToString("0.##########", CultureInfo.CurrentCulture);
        return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var back) && back == v ? s : v.ToString("R", CultureInfo.CurrentCulture);
    }

    /// <summary>What was typed, as a number: in Windows' own way of writing one, or with a point; none for anything else.</summary>
    private static double? Read(string text)
    {
        var t = text.Trim().Replace('−', '-');
        if (t.Length == 0) return null;
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) && double.IsFinite(v)) return v;
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v)) return v;
        return null;
    }

    /// <summary>How many decimals a number is written with (a step's: 0.1 → 1), ten at most.</summary>
    private static int DecimalsOf(double x)
    {
        var s = Math.Abs(x).ToString("R", CultureInfo.InvariantCulture);
        var e = s.IndexOf('E');
        var exp = e >= 0 ? int.Parse(s[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0;
        var mantissa = e >= 0 ? s[..e] : s;
        var dot = mantissa.IndexOf('.');
        var places = (dot < 0 ? 0 : mantissa.Length - dot - 1) - exp;
        return Math.Clamp(places, 0, 10);
    }

    private static string Range(ToolInput input) => (input.Min, input.Max) switch
    {
        ({ } lo, { } hi) => $"{Plain(lo)} to {Plain(hi)}",
        ({ } lo, null) => $"At least {Plain(lo)}",
        (null, { } hi) => $"At most {Plain(hi)}",
        _ => "",
    };

    /// <summary>A number box, its unit after it, and − and + that step it by its step, kept to its min and max; ↑ and ↓
    /// step it too. What is not a number is its default in the formulas, and one out of range the nearest end — said under it.</summary>
    private FrameworkElement NumberField(ToolInput input)
    {
        var (given, has) = Given(input);
        var start = has && given is double d && double.IsFinite(d) ? d : input.Default;
        var box = ToolKit.Box(Plain(start), Plain(input.Default));
        box.MinWidth = 0;
        AutomationProperties.SetName(box, input.Unit.Length > 0 ? $"{input.Label}, in {input.Unit}" : input.Label);
        var range = Range(input);
        var hint = K.Wrapped(range, Ty.Caption, B.Text2);
        hint.Visibility = range.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Button? less = null, more = null;
        void Steppers()
        {
            var now = ToolFormat.InputValue(input, Read(box.Text));
            if (less != null) less.IsEnabled = input.Min is not { } lo || now > lo;
            if (more != null) more.IsEnabled = input.Max is not { } hi || now < hi;
        }
        void StepBy(int dir)
        {
            var now = ToolFormat.InputValue(input, Read(box.Text));
            var next = now + dir * input.Step;
            // (to the step's own decimals, or the number's: 0.1 + 0.2 is 0.3, not 0.30000000000000004)
            next = Math.Round(next, Math.Max(DecimalsOf(input.Step), DecimalsOf(now)), MidpointRounding.AwayFromZero);
            next = Math.Min(input.Max ?? double.PositiveInfinity, Math.Max(input.Min ?? double.NegativeInfinity, next));
            box.Text = Plain(next);
            box.CaretIndex = box.Text.Length;
        }
        Button Step(SymbolRegular s, int dir, string tip)
        {
            var b = K.Subtle(K.Icon(s, 12, B.Text), () => StepBy(dir), new Thickness(0), 4);
            b.Width = 32;
            b.Height = 32;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.SetResourceReference(Control.BackgroundProperty, B.Subtle);
            b.ToolTip = tip;
            AutomationProperties.SetName(b, tip);
            return b;
        }
        var by = Plain(input.Step);
        less = Step(SymbolRegular.Subtract16, -1, $"Less {input.Label} (by {by})");
        more = Step(SymbolRegular.Add16, 1, $"More {input.Label} (by {by})");
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down)) return;
            e.Handled = true;
            StepBy(e.Key == Key.Up ? 1 : -1);
        };
        box.TextChanged += (_, _) =>
        {
            var typed = Read(box.Text);
            if (typed is { } t) values[input.Id] = t; else values.Remove(input.Id);
            var used = ToolFormat.InputValue(input, typed);
            if (box.Text.Trim().Length > 0 && typed == null) Say(hint, $"Not a number: {Plain(input.Default)} is used.", true);
            else if (typed is { } x && x != used) Say(hint, $"{(range.Length > 0 ? range : "Out of range")}: {Plain(used)} is used.", true);
            else Say(hint, range, false);
            Steppers();
            Recompute();
        };
        Steppers();
        var row = K.Columns("*, auto, auto", box.Col(0));
        if (input.Unit.Length > 0) row.Children.Add(K.Text(input.Unit, Ty.Body, B.Text2).Margin(8, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1));
        var steppers = K.H(4, less, more);
        steppers.Margin = new Thickness(8, 0, 0, 0);
        steppers.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(steppers.Col(2));
        return Labelled(input.Label, row, hint);
    }

    private static void Say(TextBlock hint, string text, bool caution)
    {
        hint.Text = text;
        hint.Fg(caution ? B.Caution : B.Text2);
        hint.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A choice: its options side by side when there are four or fewer and they are short, a menu otherwise.</summary>
    private FrameworkElement ChoiceField(ToolInput input)
    {
        var (given, has) = Given(input);
        var chosen = has ? ToolFormat.InputValue(input, given) : input.Default;
        if (input.Options.Count <= 4 && input.Options.Sum(o => o.Label.Length) <= (preview ? 30 : 44))
        {
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            void Draw()
            {
                row.Children.Clear();
                foreach (var o in input.Options)
                {
                    var option = o;
                    var on = o.Value == chosen;
                    var b = PTKit.Choice(o.Label, on, () =>
                    {
                        chosen = option.Value;
                        values[input.Id] = option.Value;
                        Draw();
                        Recompute();
                    });
                    b.Margin = new Thickness(0, 0, 6, 6);
                    AutomationProperties.SetName(b, $"{input.Label}: {o.Label}{(on ? ", chosen" : "")}");
                    row.Children.Add(b);
                }
            }
            Draw();
            return Labelled(input.Label, row);
        }
        var box = new ComboBox { MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = input.Options.Select(o => o.Label).ToList() };
        box.SelectedIndex = Math.Max(0, input.Options.ToList().FindIndex(o => o.Value == chosen));
        AutomationProperties.SetName(box, input.Label);
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0) return;
            values[input.Id] = input.Options[box.SelectedIndex].Value;
            Recompute();
        };
        return Labelled(input.Label, box);
    }

    private FrameworkElement ToggleField(ToolInput input)
    {
        var (given, has) = Given(input);
        var sw = new ToggleSwitch { Content = input.Label, IsChecked = ToolFormat.InputValue(input, given, has) == 1 };
        void Changed()
        {
            values[input.Id] = sw.IsChecked == true;
            Recompute();
        }
        sw.Checked += (_, _) => Changed();
        sw.Unchecked += (_, _) => Changed();
        return sw;
    }

    // ---- the results ----

    /// <summary>The results worked out for what is put in now, the first one shown large; whole, the values remembered.</summary>
    private void Recompute()
    {
        var all = ToolFormat.Run(tool, values);
        if (!preview && tool.Id != null) CommunityTools.Remember(tool.Id, values);
        results.Children.Clear();
        var shown = all.Where(r => r.Shown).ToList();
        static string WithUnit(ToolResult r) => r.Unit.Length > 0 ? $"{r.Text} {r.Unit}" : r.Text;
        plain = shown.Count == 0 ? null : string.Join(Environment.NewLine, shown.Select(r => $"{r.Label}: {WithUnit(r)}"));
        if (shown.Count == 0)
        {
            results.Children.Add(K.Wrapped("Nothing to show for these values.", Ty.Body, B.Text2));
        }
        else
        {
            var first = shown[0];
            var big = Everyday.Figures(first.Text, preview ? 30 : 44, FontWeights.SemiBold);
            var line = K.H(0, big);
            if (first.Unit.Length > 0)
            {
                var unit = K.Text(first.Unit, preview ? Ty.Body : Ty.BodyLarge, B.Text2).Margin(8, 0, 0, preview ? 5 : 8);
                unit.VerticalAlignment = VerticalAlignment.Bottom;
                line.Children.Add(unit);
            }
            var top = K.V(2, K.Text(first.Label, preview ? Ty.Caption : Ty.Body, B.Text2), Everyday.Shrinks(line));
            AutomationProperties.SetName(top, $"{first.Label}: {WithUnit(first)}");
            results.Children.Add(top);
            for (var i = 1; i < shown.Count; i++)
            {
                var r = shown[i];
                var value = K.Text(WithUnit(r), Ty.BodyStrong);
                value.FontFeatures();
                value.Margin = new Thickness(12, 0, 0, 0);
                var row = K.Columns("*, auto", K.Wrapped(r.Label, Ty.Body, B.Text2).Col(0), value.Col(1));
                row.Margin = new Thickness(0, preview ? 6 : 8, 0, preview ? 6 : 8);
                AutomationProperties.SetName(row, $"{r.Label}: {WithUnit(r)}");
                results.Children.Add(K.Divider().Margin(0, i == 1 ? (preview ? 10 : 14) : 0, 0, 0));
                results.Children.Add(row);
            }
        }
        // (in a picture run, what the tool shows — CI reads it from the log — once per change)
        if (preview || tool.Id == null || !(Shot.Asked || Launch.IsDevelopment)) return;
        var now = shown.Count == 0 ? "nothing" : string.Join(" | ", shown.Select(r => $"{r.Label} {WithUnit(r)}"));
        if (now == said) return;
        said = now;
        Log.Write($"community tool {tool.Id}: shows {now}");
        Shot.Moved();
    }

    // ---- the table, the note, who made it ----

    private static FrameworkElement Table(ToolTable table)
    {
        var g = new Grid();
        foreach (var _ in table.Columns) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Row(IReadOnlyList<string> cells, int at, bool head)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < cells.Count; c++)
            {
                var t = K.Wrapped(cells[c], head ? Ty.BodyStrong : Ty.Body, head ? B.Text2 : B.Text);
                t.Margin = new Thickness(c == 0 ? 0 : 12, 8, 0, 8);
                Grid.SetRow(t, at);
                Grid.SetColumn(t, c);
                g.Children.Add(t);
            }
            if (at == 0) return;
            var line = K.Divider();
            line.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(line, at);
            Grid.SetColumnSpan(line, cells.Count);
            g.Children.Add(line);
        }
        Row(table.Columns, 0, true);
        for (var i = 0; i < table.Rows.Count; i++) Row(table.Rows[i], i + 1, false);
        AutomationProperties.SetName(g, table.Title.Length > 0 ? table.Title : "Table");
        return table.Title.Length > 0 ? ToolKit.Section(table.Title, g) : K.Card(g, new Thickness(16));
    }

    private FrameworkElement Foot()
    {
        var v = K.V(8);
        if (tool.Note is { } note) v.Children.Add(K.Wrapped(note, Ty.Body, B.Text2));
        var by = tool.Maker.Length > 0 ? $"Made by {tool.Maker} · Simpl Community" : "Shared in Simpl Community";
        v.Children.Add(K.Columns("auto, *", K.Icon(SymbolRegular.PeopleCommunity24, 14, B.Text3).Margin(0, 1, 8, 0).Col(0), K.Wrapped(by, Ty.Caption, B.Text3).Col(1)));
        return v;
    }
}
