using IEnumerator = System.Collections.IEnumerator;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Screens;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// Calculator (the Mac's CalculatorTool): the display over Apple's scientific keys, in Windows 11 Calculator's dress —
/// the digits on the control fill, the rest a shade quieter, = in the accent — and the keyboard on it; beside it on a
/// wide window, the sums worked out with = (a press puts a result back on the line), and Copy Result.
/// </summary>
internal sealed class CalculatorTool
{
    private readonly CalcEngine eng = CalcEngine.Full;
    private readonly CalcSurface surface;
    private readonly CalcDisplay display;
    private readonly CalcKeypad keypad;
    private readonly Wpf.Ui.Controls.Button copy;
    private readonly ContentControl history = new() { Focusable = false };
    private int historyCount = -1;
    private CalcEngine.Past? historyTop;

    public FrameworkElement View { get; }

    public CalculatorTool(ToolContext c)
    {
        CalcEngine.FeedLaunch(eng);
        surface = new CalcSurface(eng, quick: false);
        display = new CalcDisplay(eng, 60);
        keypad = new CalcKeypad(eng, CalcKey.Scientific, 52, 8, surface.Refocus);
        copy = K.Button("Copy result", () =>
        {
            if (eng.Value is { } v) Everyday.Copy(CalcEngine.Ascii(v));
            surface.Refocus();
        }, icon: SymbolRegular.Copy24);
        var foot = K.Wrapped("Type on the keyboard too: Enter is =, Backspace takes the last thing off, Escape clears.", Ty.Caption, B.Text2);
        foot.TextAlignment = TextAlignment.Right;
        var bottom = K.Columns("auto, *", copy.Col(0), foot.Margin(16, 0, 0, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1));
        surface.Children.Add(K.V(16, K.Card(display.View, new Thickness(22, 18, 22, 18)), keypad.View, bottom));
        var main = new LeftMax { Max = 820, Child = surface };
        View = Everyday.Page(ToolKit.Columns(main, history, 360, 1020));
        Update();
        Everyday.Follow(View, () => { eng.Changed += Update; Update(); }, () => eng.Changed -= Update);
    }

    private void Update()
    {
        display.Update();
        keypad.Update();
        copy.IsEnabled = eng.Value != null;
        ShowHistory();
    }

    /// <summary>The sums worked out with =, newest first: a press puts its result back on the line.</summary>
    private void ShowHistory()
    {
        var top = eng.History.FirstOrDefault();
        if (historyCount == eng.History.Count && ReferenceEquals(historyTop, top)) return;
        historyCount = eng.History.Count;
        historyTop = top;
        UIElement body;
        if (eng.History.Count == 0)
            body = K.Wrapped("Sums worked out with = show here. A press puts the result back on the line.", Ty.Body, B.Text2);
        else
        {
            var rows = K.V(0);
            foreach (var past in eng.History)
            {
                if (rows.Children.Count > 0) rows.Children.Add(K.Divider(12));
                rows.Children.Add(Row(past));
            }
            body = K.Card(rows, new Thickness(6));
        }
        var clear = eng.History.Count == 0 ? null : K.Link("Clear", () => { eng.ClearHistory(); surface.Refocus(); });
        history.Content = Everyday.Section("History", body, null, clear);
    }

    private FrameworkElement Row(CalcEngine.Past past)
    {
        var sum = K.Wrapped(past.Sum + " =", Ty.Body, B.Text2, 2);
        sum.TextAlignment = TextAlignment.Right;
        Typography.SetNumeralAlignment(sum, FontNumeralAlignment.Tabular);
        var value = Everyday.Figures(CalcEngine.Format(past.Value), 20, FontWeights.SemiBold);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        var b = K.Subtle(K.V(2, sum, value), () => { eng.Insert(past.Value); surface.Refocus(); }, new Thickness(8, 9, 8, 9));
        b.ToolTip = "Put the result back on the line";
        AutomationProperties.SetName(b, $"{past.Sum} = {CalcEngine.Format(past.Value)}");
        b.ContextMenu = MoreKit.Menu(
            ("Copy result", SymbolRegular.Copy24, () => Everyday.Copy(CalcEngine.Ascii(past.Value))),
            ("Copy sum", SymbolRegular.Copy24, () => Everyday.Copy(past.Sum + " = " + CalcEngine.Format(past.Value))));
        return b;
    }
}

/// <summary>The calculator's pin, opened (the Mac's CalculatorCompact): the display and the basic keys, the keyboard on it.</summary>
internal sealed class CalculatorCompact
{
    private readonly CalcEngine eng = CalcEngine.Quick;
    private readonly CalcDisplay display;
    private readonly CalcKeypad keypad;

    public FrameworkElement View { get; }

    public CalculatorCompact(ToolContext c)
    {
        CalcEngine.FeedLaunch(eng);
        var surface = new CalcSurface(eng, quick: true);
        display = new CalcDisplay(eng, 38);
        keypad = new CalcKeypad(eng, CalcKey.Basic, 38, 6, surface.Refocus);
        surface.Children.Add(K.V(12, display.View.Margin(6, 0, 6, 0), keypad.View));
        View = surface;
        Update();
        Everyday.Follow(View, () => { eng.Changed += Update; Update(); }, () => eng.Changed -= Update);
    }

    private void Update()
    {
        display.Update();
        keypad.Update();
    }
}

/// <summary>
/// Where the calculator's keyboard types: it keeps the focus (its keys do not take it, so Enter is never a key pressed
/// again), reads the characters typed, Enter, Backspace and Delete — and Escape, which clears a line on the whole tool
/// before the window takes it to go back or close (a second Escape, the line clear, is the window's again). In the pin's
/// flyout Escape is left to the flyout, as on the Mac.
/// </summary>
internal sealed class CalcSurface : Grid
{
    public CalcEngine Engine { get; }
    public bool Quick { get; }

    static CalcSurface()
    {
        // (a window's own handler of a key runs after its class's: Escape reaches the calculator before the window)
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnWindowKey));
    }

    public CalcSurface(CalcEngine engine, bool quick)
    {
        Engine = engine;
        Quick = quick;
        Focusable = true;
        FocusVisualStyle = null;
        Background = Brushes.Transparent;
        AutomationProperties.SetName(this, "Calculator");
        KeyDown += OnKey;
        TextInput += OnText;
        MouseDown += (_, _) => Refocus();
        Loaded += (_, _) => Dispatcher.BeginInvoke(Refocus, DispatcherPriority.Input);
    }

    public void Refocus()
    {
        if (!IsVisible) return;
        Focus();
        Keyboard.Focus(this);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods != ModifierKeys.None && mods != ModifierKeys.Shift)
        {
            // Ctrl+C: the result, as Windows' calculator copies it
            if (mods == ModifierKeys.Control && e.Key == Key.C && Engine.Value is { } v) { Everyday.Copy(CalcEngine.Ascii(v)); e.Handled = true; }
            return;
        }
        if (CalcEngine.KeyFor(e.Key) is not { } key || key == "ac") return;
        Engine.Press(key);
        e.Handled = true;
    }

    private void OnText(object sender, TextCompositionEventArgs e)
    {
        if (CalcEngine.KeyFor(e.Text) is not { } key) return;
        Engine.Press(key);
        e.Handled = true;
    }

    private static void OnWindowKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        if (Keyboard.FocusedElement is not CalcSurface { Quick: false } c || !c.IsVisible || !c.Engine.HasLine) return;
        c.Engine.Press("ac");
        e.Handled = true;
    }
}

/// <summary>
/// The display (the Mac's CalcDisplay): the small line over the large one, both kept to their ends — the large one
/// shrinking to fit as it grows (to a third), then losing its start rather than its end; Rad, M and 2nd as badges.
/// </summary>
internal sealed class CalcDisplay
{
    private readonly CalcEngine eng;
    private readonly Stack badges = K.H(6);
    private readonly TextBlock sub, line;
    private string badgesShown = "";

    public FrameworkElement View { get; }

    public CalcDisplay(CalcEngine eng, double big)
    {
        this.eng = eng;
        sub = Everyday.Figures(" ", big * 0.4, FontWeights.Normal, B.Text2);
        line = Everyday.Figures("0", big, FontWeights.SemiBold);
        badges.VerticalAlignment = VerticalAlignment.Center;
        var top = K.Columns("auto, *", badges.Col(0), new FitLine(sub, big * 0.4, 1).Margin(8, 0, 0, 0).Col(1));
        var v = K.V(4, top, new FitLine(line, big, 0.35));
        v.Background = Brushes.Transparent;
        v.ContextMenu = MoreKit.Menu(("Copy", SymbolRegular.Copy24, () => { if (eng.Value is { } x) Everyday.Copy(CalcEngine.Ascii(x)); }));
        View = v;
        Update();
    }

    public void Update()
    {
        var (l, s) = eng.Shown;
        line.Text = l;
        sub.Text = s.Length == 0 ? " " : s;
        sub.Fg(s == "Error" ? B.Critical : B.Text2);
        var marks = string.Join(",", new[] { eng.Deg ? null : "Rad", eng.Mem != 0 ? "M" : null, eng.Second ? "2nd" : null }.Where(m => m != null));
        if (marks != badgesShown)
        {
            badgesShown = marks;
            badges.Children.Clear();
            foreach (var m in marks.Split(',', StringSplitOptions.RemoveEmptyEntries)) badges.Children.Add(Badge(m));
        }
        AutomationProperties.SetName(View, s.Length == 0 ? l : $"{l}, {s}");
    }

    /// <summary>A word in a pill of the accent's tint.</summary>
    private static FrameworkElement Badge(string text)
    {
        var t = K.Text(text, Ty.Caption, B.AccentText);
        t.FontWeight = FontWeights.SemiBold;
        t.Margin = new Thickness(7, 1, 7, 2);
        var wash = new Border { CornerRadius = new CornerRadius(10), Opacity = 0.15 }.Bg(B.Accent);
        return new Grid { Children = { wash, t }, VerticalAlignment = VerticalAlignment.Center };
    }
}

/// <summary>
/// One line of text at the right of the room it has: at its size while it fits, then smaller down to a part of it, then
/// its start cut off at the left (the Mac's minimumScaleFactor and truncationMode(.head)). Always the height of its full
/// size, so the display does not jump as the line grows.
/// </summary>
internal sealed class FitLine : FrameworkElement
{
    private readonly TextBlock text;
    private readonly double big, min;

    public FitLine(TextBlock text, double big, double minScale)
    {
        this.text = text;
        this.big = big;
        min = minScale;
        ClipToBounds = true;
        AddVisualChild(text);
        AddLogicalChild(text);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => text;
    protected override IEnumerator LogicalChildren => new object[] { text }.GetEnumerator();

    protected override Size MeasureOverride(Size available)
    {
        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        if (text.FontSize != big) text.FontSize = big;
        text.Measure(inf);
        var full = text.DesiredSize;
        if (!double.IsInfinity(available.Width) && full.Width > available.Width && full.Width > 0)
        {
            var size = Math.Floor(big * Math.Max(min, available.Width / full.Width) * 2) / 2;
            if (size < big)
            {
                text.FontSize = size;
                text.Measure(inf);
            }
        }
        return new Size(double.IsInfinity(available.Width) ? text.DesiredSize.Width : available.Width, full.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var d = text.DesiredSize;
        text.Arrange(new Rect(final.Width - d.Width, Math.Max(0, final.Height - d.Height), d.Width, d.Height));
        return final;
    }
}

/// <summary>
/// The keys (the Mac's CalcKeypad), laid out in a grid, "0" two wide; 2nd turns the keys that have one to their other
/// function, and lights up while it is on.
/// </summary>
internal sealed class CalcKeypad
{
    private readonly CalcEngine eng;
    private readonly List<(CalcKey Key, Wpf.Ui.Controls.Button Button, TextBlock? Label)> keys = new();
    private bool? secondShown;

    public Grid View { get; }

    public CalcKeypad(CalcEngine eng, CalcKey[][] rows, double keyHeight, double spacing, Action pressed)
    {
        this.eng = eng;
        var g = new Grid { Margin = new Thickness(-spacing / 2) };
        var cols = rows.Max(r => r.Sum(k => k.Wide ? 2 : 1));
        for (var c = 0; c < cols; c++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < rows.Length; r++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var col = 0;
            foreach (var key in rows[r])
            {
                var (b, label) = Make(key, keyHeight, spacing, pressed);
                Grid.SetRow(b, r);
                Grid.SetColumn(b, col);
                if (key.Wide) Grid.SetColumnSpan(b, 2);
                col += key.Wide ? 2 : 1;
                g.Children.Add(b);
                keys.Add((key, b, label));
            }
        }
        View = g;
        Update();
    }

    private (Wpf.Ui.Controls.Button, TextBlock?) Make(CalcKey key, double height, double spacing, Action pressed)
    {
        var b = new Wpf.Ui.Controls.Button
        {
            Focusable = false, IsTabStop = false, Height = height, MinWidth = 0, Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(spacing / 2), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        TextBlock? label = null;
        if (key.Id == "back") b.Content = new SymbolIcon { Symbol = SymbolRegular.Backspace24, FontSize = height * 0.42 };
        else
        {
            label = new TextBlock
            {
                Text = key.Label, FontFamily = key.Kind == CalcKey.Kinds.Fn ? K.TextFace : K.DisplayFace,
                FontSize = height * key.Kind switch { CalcKey.Kinds.Num or CalcKey.Kinds.Op => 0.42, CalcKey.Kinds.Top => 0.36, _ => 0.33 },
                FontWeight = key.Id == "=" ? FontWeights.SemiBold : FontWeights.Normal,
            };
            Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
            b.Content = Everyday.Shrinks(label, HorizontalAlignment.Center);
        }
        Paint(b, key, key.Id == "=");
        b.Click += (_, e) =>
        {
            e.Handled = true;
            eng.Press(IdOf(key));
            pressed();
        };
        return (b, label);
    }

    /// <summary>What a key presses now (its other function under 2nd).</summary>
    private string IdOf(CalcKey key) => eng.Second && CalcKey.Alternates.TryGetValue(key.Id, out var alt) ? alt.Id : key.Id;

    public void Update()
    {
        foreach (var (key, b, label) in keys)
        {
            var id = IdOf(key);
            var alt = eng.Second && CalcKey.Alternates.TryGetValue(key.Id, out var a) ? a.Label : null;
            var text = key.Id == "rad" ? (eng.Deg ? "Rad" : "Deg") : alt ?? key.Label;
            if (label != null && label.Text != text) label.Text = text;
            var title = key.Id == "rad" ? (eng.Deg ? "Switch to radians" : "Switch to degrees") : CalcKey.Titles.TryGetValue(id, out var t) ? t : text;
            if (b.ToolTip as string != title)
            {
                b.ToolTip = title;
                AutomationProperties.SetName(b, title);
            }
        }
        if (secondShown != eng.Second)
        {
            secondShown = eng.Second;
            foreach (var (key, b, _) in keys.Where(k => k.Key.Id == "second")) Paint(b, key, eng.Second);
        }
    }

    /// <summary>
    /// A key's face, as Windows 11's Calculator has them: a digit on the control fill, the operators, top keys and
    /// functions a shade quieter, = (and 2nd while it is on) in the accent.
    /// </summary>
    private static void Paint(Wpf.Ui.Controls.Button b, CalcKey key, bool accent)
    {
        foreach (var dp in new[] { Control.BackgroundProperty, Wpf.Ui.Controls.Button.MouseOverBackgroundProperty, Wpf.Ui.Controls.Button.PressedBackgroundProperty, Wpf.Ui.Controls.Button.PressedForegroundProperty })
            b.ClearValue(dp);
        b.Appearance = accent ? ControlAppearance.Primary : ControlAppearance.Secondary;
        if (accent || key.Kind == CalcKey.Kinds.Num) return;
        b.SetResourceReference(Control.BackgroundProperty, "ControlFillColorSecondaryBrush");
        b.SetResourceReference(Wpf.Ui.Controls.Button.MouseOverBackgroundProperty, "ControlFillColorTertiaryBrush");
        b.SetResourceReference(Wpf.Ui.Controls.Button.PressedBackgroundProperty, "ControlFillColorTertiaryBrush");
        b.SetResourceReference(Wpf.Ui.Controls.Button.PressedForegroundProperty, B.Text2);
    }
}
