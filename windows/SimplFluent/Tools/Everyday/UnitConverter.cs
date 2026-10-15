using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// The unit converter (the Mac's own: the web has none): a value in one unit, shown in another and beside it in every
// unit of its kind — lengths, weights, temperatures, volumes, areas, speeds, times, energy, pressure, data and angles.

#pragma warning disable CA2255
internal static class UnitConverterRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("units", c => new UnitConverterTool(c).View, c => new UnitConverterCompact(c).View);
}
#pragma warning restore CA2255

/// <summary>
/// A unit: its name, its symbol, and how it stands to its kind's base unit — so many base units each, and an offset
/// (temperatures' only) — as Foundation's Dimension converters have them on the Mac.
/// </summary>
internal sealed record UnitChoice(string Name, string Symbol, double Coefficient, double Constant = 0)
{
    public double ToBase(double v) => v * Coefficient + Constant;
    public double FromBase(double v) => (v - Constant) / Coefficient;
}

/// <summary>A kind of unit (the Mac's UnitCategory): its name, its symbol, its units, and the pair it starts on.</summary>
internal sealed record UnitCategory(string Name, SymbolRegular Symbol, UnitChoice[] Units, int From, int To)
{
    private static UnitChoice U(string name, string symbol, double coefficient, double constant = 0) => new(name, symbol, coefficient, constant);

    /// <summary>
    /// Every kind, its units by their exact definitions (the international yard and pound, US customary volumes, SI
    /// prefixes; binary prefixes for KiB, MiB, GiB).
    /// </summary>
    public static readonly UnitCategory[] All =
    {
        new("Length", SymbolRegular.Ruler24, new[]
        {
            U("Millimetres", "mm", 0.001), U("Centimetres", "cm", 0.01), U("Metres", "m", 1),
            U("Kilometres", "km", 1000), U("Inches", "in", 0.0254), U("Feet", "ft", 0.3048),
            U("Yards", "yd", 0.9144), U("Miles", "mi", 1609.344), U("Nautical miles", "nmi", 1852),
            U("Micrometres", "µm", 1e-6), U("Nanometres", "nm", 1e-9), U("Light years", "ly", 9.4607304725808e15),
        }, 2, 5),
        new("Mass", SymbolRegular.Scales24, new[]
        {
            U("Milligrams", "mg", 1e-6), U("Grams", "g", 0.001), U("Kilograms", "kg", 1),
            U("Metric tons", "t", 1000), U("Ounces", "oz", 0.028349523125), U("Pounds", "lb", 0.45359237),
            U("Stones", "st", 6.35029318), U("Short tons", "ton", 907.18474),
        }, 2, 5),
        new("Temperature", SymbolRegular.Temperature24, new[]
        {
            U("Celsius", "°C", 1, 273.15), U("Fahrenheit", "°F", 5.0 / 9, 273.15 - 32 * 5.0 / 9), U("Kelvin", "K", 1),
        }, 0, 1),
        new("Volume", SymbolRegular.Drop24, new[]
        {
            U("Millilitres", "mL", 0.001), U("Litres", "L", 1), U("Cubic metres", "m³", 1000),
            U("Cubic centimetres", "cm³", 0.001), U("Teaspoons", "tsp", 0.00492892159375), U("Tablespoons", "tbsp", 0.01478676478125),
            U("Fluid ounces", "fl oz", 0.0295735295625), U("Cups", "cup", 0.2365882365), U("Pints", "pt", 0.473176473),
            U("Quarts", "qt", 0.946352946), U("Gallons", "gal", 3.785411784),
        }, 1, 10),
        new("Area", SymbolRegular.SquareHint24, new[]
        {
            U("Square metres", "m²", 1), U("Square kilometres", "km²", 1e6), U("Square centimetres", "cm²", 1e-4),
            U("Hectares", "ha", 1e4), U("Square feet", "ft²", 0.09290304), U("Square inches", "in²", 0.00064516),
            U("Square miles", "mi²", 2589988.110336), U("Acres", "ac", 4046.8564224),
        }, 0, 4),
        new("Speed", SymbolRegular.TopSpeed24, new[]
        {
            U("Metres per second", "m/s", 1), U("Kilometres per hour", "km/h", 1 / 3.6),
            U("Miles per hour", "mph", 0.44704), U("Knots", "kn", 1852.0 / 3600),
        }, 1, 2),
        new("Time", SymbolRegular.Clock24, new[]
        {
            U("Milliseconds", "ms", 0.001), U("Seconds", "s", 1), U("Minutes", "min", 60), U("Hours", "h", 3600),
        }, 3, 2),
        new("Energy", SymbolRegular.Flash24, new[]
        {
            U("Joules", "J", 1), U("Kilojoules", "kJ", 1000), U("Calories", "cal", 4.184),
            U("Kilocalories", "kcal", 4184), U("Kilowatt-hours", "kWh", 3.6e6),
        }, 3, 1),
        new("Pressure", SymbolRegular.Gauge24, new[]
        {
            U("Pascals", "Pa", 1), U("Kilopascals", "kPa", 1000), U("Bars", "bar", 1e5),
            U("Millibars", "mbar", 100), U("Atmospheres", "atm", 101325),
            U("Millimetres of mercury", "mmHg", 133.322387415), U("Pounds per square inch", "psi", 6894.757293168),
        }, 4, 1),
        new("Data", SymbolRegular.HardDrive20, new[]
        {
            U("Bits", "b", 1), U("Bytes", "B", 8), U("Kilobytes", "kB", 8e3),
            U("Megabytes", "MB", 8e6), U("Gigabytes", "GB", 8e9), U("Terabytes", "TB", 8e12),
            U("Kibibytes", "KiB", 8 * 1024.0), U("Mebibytes", "MiB", 8 * 1048576.0), U("Gibibytes", "GiB", 8 * 1073741824.0),
        }, 4, 3),
        new("Angle", SymbolRegular.ArrowRotateClockwise24, new[]
        {
            U("Degrees", "°", 1), U("Radians", "rad", 180 / Math.PI), U("Gradians", "grad", 0.9),
            U("Revolutions", "rev", 360), U("Arc minutes", "′", 1.0 / 60), U("Arc seconds", "″", 1.0 / 3600),
        }, 0, 1),
    };
}

/// <summary>The sums and the numbers' words (the Mac's UnitMath).</summary>
internal static class UnitMath
{
    /// <summary>A value from one unit in another of its kind.</summary>
    public static double Convert(double v, UnitChoice from, UnitChoice to) => to.FromBase(from.ToBase(v));

    public static double? Number(string s)
    {
        var t = s.Replace(",", "").Replace("−", "-").Trim();
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    }

    /// <summary>Up to ten figures, thousands grouped; e-notation for a very large or very small one.</summary>
    public static string Text(double v)
    {
        if (!double.IsFinite(v)) return "—";
        var a = Math.Abs(v);
        if (a != 0 && (a >= 1e15 || a < 1e-6)) return v.ToString("0.#####e+00", CultureInfo.InvariantCulture).Replace("-", "−");
        return CalcEngine.Significant(v, 10, true).Replace("-", "−");
    }

    /// <summary>A number with its unit, as the Mac's: "212 °F", "3.28 ft" — the angle marks close up, as written: "90°", "30′".</summary>
    public static string WithUnit(double v, UnitChoice u) => u.Symbol is "°" or "′" or "″" ? Text(v) + u.Symbol : $"{Text(v)} {u.Symbol}";
}

/// <summary>
/// What a converter is showing (the Mac's @State): the kind, the two units and the value — the whole tool's and the
/// pin's flyout's each kept as left while their views come and go.
/// </summary>
internal sealed class UnitsState
{
    public static UnitsState Full { get; } = new();
    public static UnitsState Quick { get; } = new();

    public UnitCategory Category { get; private set; } = UnitCategory.All[0];
    public UnitChoice From { get; private set; } = UnitCategory.All[0].Units[UnitCategory.All[0].From];
    public UnitChoice To { get; private set; } = UnitCategory.All[0].Units[UnitCategory.All[0].To];
    public string Value { get; private set; } = "1";

    public event Action? Changed;

    public double? Result => UnitMath.Number(Value) is { } v ? UnitMath.Convert(v, From, To) : null;

    /// <summary>A kind picked: its starting pair.</summary>
    public void Pick(UnitCategory c)
    {
        Category = c;
        From = c.Units[c.From];
        To = c.Units[c.To];
        Changed?.Invoke();
    }

    public void SetFrom(UnitChoice u) { if (From == u) return; From = u; Changed?.Invoke(); }
    public void SetTo(UnitChoice u) { if (To == u) return; To = u; Changed?.Invoke(); }
    public void SetValue(string v) { if (Value == v) return; Value = v; Changed?.Invoke(); }

    public void Swap()
    {
        (From, To) = (To, From);
        Changed?.Invoke();
    }

    /// <summary>(0.3.5) As it starts: the first kind, its starting pair, 1.</summary>
    public bool Untouched => Category == UnitCategory.All[0] && From == Category.Units[Category.From] && To == Category.Units[Category.To] && Value == "1";

    /// <summary>(0.3.5) Another's kind, units and value taken on — the whole converter's taking the pin's, its flyout
    /// opened full screen.</summary>
    public void Take(UnitsState from)
    {
        Category = from.Category;
        From = from.From;
        To = from.To;
        Value = from.Value;
        Changed?.Invoke();
    }

    /// <summary>The screenshot suite's start (--units-kind temperature --units-value 100), for the converter first opened.</summary>
    public void FromLaunch()
    {
        if (Launch.Take("", "units-kind") is { Length: > 0 } k && UnitCategory.All.FirstOrDefault(c => c.Name.Equals(k, StringComparison.OrdinalIgnoreCase)) is { } c) Pick(c);
        if (Launch.Take("", "units-value") is { Length: > 0 } v) SetValue(v);
    }
}

/// <summary>
/// Unit converter (the Mac's UnitConverterTool): the kind of unit across the top; one column — the value, From, Swap
/// (⇅) between From and To, To — and the answer large with its unit ("212 °F"), what it came from under it ("= 100 °C");
/// and beside it on a wide window, the value in every unit of its kind (a press makes that the unit wanted).
/// </summary>
internal sealed class UnitConverterTool
{
    private readonly UnitsState s = UnitsState.Full;
    private readonly WrapPanel kinds = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox value;
    private readonly ComboBox from = UnitsParts.Picker(260), to = UnitsParts.Picker(260);
    private readonly TextBlock result = Everyday.Figures("—", 56, FontWeights.Bold);
    private readonly TextBlock line = K.Wrapped("", Ty.Body, B.Text2);
    private readonly Wpf.Ui.Controls.Button copy;
    private readonly ContentControl every = new() { Focusable = false };
    private UnitCategory? shownKinds, shownUnits;
    private bool syncing;

    public FrameworkElement View { get; }

    public UnitConverterTool(ToolContext c)
    {
        // (0.3.5) opened full screen from its pin: the pin's kind, units and value
        if (c.TakeInfo("carry") == "pin") s.Take(UnitsState.Quick);
        s.FromLaunch();
        value = ToolKit.Box(s.Value, "1", t => { if (!syncing) s.SetValue(t); });
        from.SelectionChanged += (_, _) => { if (!syncing && UnitsParts.Unit(from) is { } u) s.SetFrom(u); };
        to.SelectionChanged += (_, _) => { if (!syncing && UnitsParts.Unit(to) is { } u) s.SetTo(u); };
        AutomationProperties.SetName(from, "From");
        AutomationProperties.SetName(to, "To");
        // (Swap between From and To, in one column with them: ⇅)
        var swap = K.IconButton(SymbolRegular.ArrowSort24, () => s.Swap(), "Swap From and To", 16, 36);
        swap.HorizontalAlignment = HorizontalAlignment.Left;
        copy = K.Button("Copy", () => { if (s.Result is { } r) Everyday.Copy(UnitMath.WithUnit(r, s.To).Replace("−", "-")); }, icon: SymbolRegular.Copy24);
        copy.HorizontalAlignment = HorizontalAlignment.Left;

        var valueField = ToolKit.Field("Value", value);
        valueField.Width = 260;
        valueField.HorizontalAlignment = HorizontalAlignment.Left;
        var column = K.V(12, valueField, UnitsParts.Labelled("From", from), swap, UnitsParts.Labelled("To", to));
        result.ContextMenu = MoreKit.Menu(("Copy", SymbolRegular.Copy24, () => { if (s.Result is { } r) Everyday.Copy(UnitMath.WithUnit(r, s.To).Replace("−", "-")); }));
        var answer = K.Card(K.V(6, Everyday.Shrinks(result), line, copy.Margin(0, 6, 0, 0)), new Thickness(24));
        var converter = K.V(20, column, answer);
        View = Everyday.Page(K.V(20, kinds, ToolKit.Columns(converter, every, 380, 860)));
        Update();
        Everyday.Follow(View, () => { s.Changed += Update; Update(); }, () => s.Changed -= Update);
    }

    private void Update()
    {
        syncing = true;
        if (shownKinds != s.Category)
        {
            shownKinds = s.Category;
            kinds.Children.Clear();
            foreach (var cat in UnitCategory.All)
            {
                var k = cat;
                var b = K.Button(cat.Name, () => s.Pick(k), accent: cat == s.Category, icon: cat.Symbol);
                b.MinWidth = 0;
                b.Margin = new Thickness(0, 0, 8, 8);
                if (cat == s.Category) b.FontWeight = FontWeights.SemiBold;
                kinds.Children.Add(b);
            }
        }
        if (shownUnits != s.Category)
        {
            shownUnits = s.Category;
            UnitsParts.Fill(from, s.Category, full: true);
            UnitsParts.Fill(to, s.Category, full: true);
        }
        UnitsParts.Show(from, s.From);
        UnitsParts.Show(to, s.To);
        if (value.Text != s.Value) value.Text = s.Value;
        syncing = false;

        // ("212 °F" large, with its unit — never a bare number to copy without one — and "= 100 °C" under it)
        var r = s.Result;
        result.Text = r is { } x ? UnitMath.WithUnit(x, s.To) : "—";
        line.Text = UnitMath.Number(s.Value) is { } v ? $"= {UnitMath.WithUnit(v, s.From)}" : "";
        line.Visibility = line.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        copy.IsEnabled = r != null;
        copy.ToolTip = r is { } y ? $"Copy {UnitMath.WithUnit(y, s.To)}" : "Copy";
        AutomationProperties.SetName(result, r is { } z ? $"{UnitMath.Text(z)} {s.To.Name.ToLowerInvariant()}" : "No answer yet");
        every.Content = Everyday.Section("In every unit", EveryUnit());
    }

    /// <summary>The value in every unit of its kind: a press makes that the unit wanted.</summary>
    private UIElement EveryUnit()
    {
        if (UnitMath.Number(s.Value) is not { } v) return K.Wrapped("Type a number to see it in every unit.", Ty.Body, B.Text2);
        var rows = K.V(0);
        foreach (var u in s.Category.Units)
        {
            if (rows.Children.Count > 0) rows.Children.Add(K.Divider(14));
            var amount = K.Text($"{UnitMath.Text(UnitMath.Convert(v, s.From, u))} {u.Symbol}", Ty.Body, u == s.To ? B.AccentText : B.Text2);
            Typography.SetNumeralAlignment(amount, FontNumeralAlignment.Tabular);
            var unit = u;
            var b = K.Subtle(K.Columns("auto, *", K.Text(u.Name, Ty.Body).Col(0), amount.Margin(12, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1)),
                () => s.SetTo(unit), new Thickness(8, 9, 8, 9));
            b.ToolTip = $"Convert to {u.Name.ToLowerInvariant()}";
            rows.Children.Add(b);
        }
        return K.Card(rows, new Thickness(6));
    }
}

/// <summary>The unit converter's pin, opened (the Mac's UnitConverterCompact): the kind, the value and its unit, the unit wanted, and the answer.</summary>
internal sealed class UnitConverterCompact
{
    private readonly UnitsState s = UnitsState.Quick;
    private readonly ComboBox kind = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox value;
    private readonly ComboBox from = UnitsParts.Picker(null), to = UnitsParts.Picker(null);
    private readonly TextBlock result = Everyday.Figures("—", 30, FontWeights.Bold);
    private UnitCategory? shownUnits;
    private bool syncing;

    public FrameworkElement View { get; }

    public UnitConverterCompact(ToolContext c)
    {
        s.FromLaunch();
        foreach (var cat in UnitCategory.All) kind.Items.Add(Everyday.Item(cat.Name, cat.Name));
        kind.SelectionChanged += (_, _) =>
        {
            if (!syncing && Everyday.Chosen(kind) is { } n && UnitCategory.All.FirstOrDefault(x => x.Name == n) is { } cat && cat != s.Category) s.Pick(cat);
        };
        AutomationProperties.SetName(kind, "Kind");
        value = ToolKit.Box(s.Value, "1", t => { if (!syncing) s.SetValue(t); });
        AutomationProperties.SetName(value, "Value");
        from.SelectionChanged += (_, _) => { if (!syncing && UnitsParts.Unit(from) is { } u) s.SetFrom(u); };
        to.SelectionChanged += (_, _) => { if (!syncing && UnitsParts.Unit(to) is { } u) s.SetTo(u); };
        AutomationProperties.SetName(from, "From");
        AutomationProperties.SetName(to, "To");
        var row = K.Columns("90, 8, *, auto, *", value.Col(0), from.Col(2), K.Icon(SymbolRegular.ArrowRight24, 14, B.Text2).Margin(8, 0, 8, 0).Col(3), to.Col(4));
        result.ContextMenu = MoreKit.Menu(("Copy", SymbolRegular.Copy24, () => { if (s.Result is { } r) Everyday.Copy(UnitMath.Text(r).Replace("−", "-")); }));
        View = K.V(10, kind, row, Everyday.Shrinks(result));
        // (0.3.5) Open full screen hands the whole converter this kind, these units and this value, once changed
        c.Carry = () => s.Untouched ? null : new Dictionary<string, string> { ["carry"] = "pin" };
        Update();
        Everyday.Follow(View, () => { s.Changed += Update; Update(); }, () => s.Changed -= Update);
    }

    private void Update()
    {
        syncing = true;
        Everyday.Choose(kind, s.Category.Name);
        if (shownUnits != s.Category)
        {
            shownUnits = s.Category;
            UnitsParts.Fill(from, s.Category, full: false);
            UnitsParts.Fill(to, s.Category, full: false);
        }
        UnitsParts.Show(from, s.From);
        UnitsParts.Show(to, s.To);
        if (value.Text != s.Value) value.Text = s.Value;
        syncing = false;
        result.Text = s.Result is { } r ? UnitMath.WithUnit(r, s.To) : "—";
    }
}

/// <summary>The unit menus the converter's two views share.</summary>
internal static class UnitsParts
{
    public static ComboBox Picker(double? width)
    {
        var box = new ComboBox { HorizontalAlignment = width == null ? HorizontalAlignment.Stretch : HorizontalAlignment.Left, MinWidth = 0 };
        if (width is { } w) box.Width = w;
        return box;
    }

    /// <summary>A menu's units: "Metres (m)" on the whole tool, the symbol alone in the flyout.</summary>
    public static void Fill(ComboBox box, UnitCategory c, bool full)
    {
        box.Items.Clear();
        foreach (var u in c.Units) box.Items.Add(new ComboBoxItem { Content = full ? $"{u.Name} ({u.Symbol})" : u.Symbol, Tag = u, ToolTip = full ? null : u.Name });
    }

    public static void Show(ComboBox box, UnitChoice u)
    {
        var item = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as UnitChoice == u);
        if (!ReferenceEquals(box.SelectedItem, item)) box.SelectedItem = item;
    }

    public static UnitChoice? Unit(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as UnitChoice;

    /// <summary>A menu with its label over it.</summary>
    public static FrameworkElement Labelled(string label, ComboBox box) => K.V(6, K.Text(label, Ty.BodyStrong, B.Text2), box);
}
