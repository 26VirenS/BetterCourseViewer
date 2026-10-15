using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

/// <summary>
/// What the everyday tools — the focus timer, the calculator, the unit converter, Grade needed, the Word Counter — share beside
/// <see cref="ToolKit"/>: their page as wide as the window allows, a section with its heading (the Mac's PageSection),
/// the clipboard, menus' items, and following a model while the view is on screen.
/// </summary>
internal static class Everyday
{
    /// <summary>
    /// The Mac's ToolPage: the tool's body scrolling under its header, as wide as the window allows up to its widest, and
    /// centred past that.
    /// </summary>
    public static ScrollViewer Page(FrameworkElement content, double maxWidth = 1480) => ToolKit.Page(content, maxWidth);

    /// <summary>The Mac's PageSection: its heading (and what is at its right) over its content.</summary>
    public static Stack Section(string title, UIElement content, string? trailing = null, UIElement? action = null) =>
        K.V(0, Heading.Section(title, trailing, action), content);

    /// <summary>Copy Result, Copy: onto the clipboard (another app holding it a moment: let go).</summary>
    public static void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); } catch { /* the clipboard was busy */ }
    }

    /// <summary>A model followed while the view is on screen (the Mac's @ObservedObject): on when it arrives, off when it goes.</summary>
    public static void Follow(FrameworkElement view, Action on, Action off)
    {
        view.Loaded += (_, _) => { off(); on(); };
        view.Unloaded += (_, _) => off();
    }

    /// <summary>Something done every so often while the view is on screen (the Mac's TimelineView).</summary>
    public static void Every(FrameworkElement view, TimeSpan interval, Action tick)
    {
        var t = new DispatcherTimer(DispatcherPriority.Normal, view.Dispatcher) { Interval = interval };
        t.Tick += (_, _) => tick();
        view.Loaded += (_, _) => { tick(); t.Start(); };
        view.Unloaded += (_, _) => t.Stop();
    }

    /// <summary>Text in the display face, its figures one width (a clock, a result that does not shake as it changes).</summary>
    public static TextBlock Figures(string text, double size, FontWeight weight, string brush = B.Text)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight, FontFamily = K.DisplayFace, TextTrimming = TextTrimming.None };
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        t.Fg(brush);
        return t;
    }

    /// <summary>Large text that shrinks to fit the width it has (the Mac's minimumScaleFactor), kept to its left.</summary>
    public static Viewbox Shrinks(UIElement child, HorizontalAlignment align = HorizontalAlignment.Left) =>
        new() { Child = child, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = align };

    /// <summary>A menu's choice, its tag what it stands for.</summary>
    public static ComboBoxItem Item(string text, string tag) => new() { Content = text, Tag = tag };

    /// <summary>The line between a menu's groups (the Mac's Divider in a Picker), not a choice.</summary>
    public static ComboBoxItem Divider() => new()
    {
        Content = new Separator { Margin = new Thickness(0) }, IsEnabled = false, Focusable = false, IsHitTestVisible = false,
        Padding = new Thickness(8, 0, 8, 0), MinHeight = 0, Height = 9,
    };

    /// <summary>The tag of the choice made.</summary>
    public static string? Chosen(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>The choice with this tag made (none, if no choice has it).</summary>
    public static void Choose(ComboBox box, string? tag)
    {
        var item = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == tag);
        if (!ReferenceEquals(box.SelectedItem, item)) box.SelectedItem = item;
    }

    /// <summary>
    /// The Mac's ToolField with a hint that changes: its label over the box (and a word after it), and the hint under it.
    /// </summary>
    public static (FrameworkElement View, TextBlock Hint) Field(string label, TextBox box, string? suffix = null, double? width = null)
    {
        UIElement row = box;
        if (suffix != null)
            row = K.Columns("*, auto", box.Col(0), K.Text(suffix, Ty.Body, B.Text2).Margin(8, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1));
        var r = (FrameworkElement)row;
        r.HorizontalAlignment = width == null ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        if (width is { } w) r.Width = w;
        var hint = K.Wrapped("", Ty.Caption, B.Text2);
        hint.Visibility = Visibility.Collapsed;
        return (K.V(6, K.Text(label, Ty.BodyStrong, B.Text2), r, hint), hint);
    }

    /// <summary>A hint's words, or none.</summary>
    public static void Say(TextBlock t, string? text)
    {
        t.Text = text ?? "";
        t.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// (tools) What a launch asked of an everyday tool, once, and only of the tool the launch opened (--tool &lt;key&gt;
    /// --tool-state …), as ChemTools.LaunchState: the Word Counter's text, the Statistics tab, the Matrix Calculator's
    /// operation. Logged for the pictures' checks.
    /// </summary>
    public static string? LaunchState(string key)
    {
        if (Launch.Get("tool") != key) return null;
        var state = Launch.Take("", "tool-state");
        if (state is { Length: > 0 }) Log.Write($"{key}: launched with state \"{state}\"");
        return state is { Length: > 0 } ? state : null;
    }

    /// <summary>
    /// A card tinted with a colour (the Mac's card(tint:)): the card's fill and edge, a wash of the colour over them, and
    /// its content.
    /// </summary>
    public static Grid TintedCard(UIElement content, Color? tint, Thickness pad, double corner = 8)
    {
        var card = K.Card(null, new Thickness(0), corner);
        var wash = new Border { CornerRadius = new CornerRadius(corner), BorderThickness = new Thickness(1) };
        if (tint is { } c)
        {
            wash.Background = new SolidColorBrush(K.WithAlpha(c, Appearance.Dark ? 0.14 : 0.08));
            wash.BorderBrush = new SolidColorBrush(K.WithAlpha(c, 0.3));
        }
        var inside = new Border { Child = content, Padding = pad };
        return new Grid { Children = { card, wash, inside } };
    }
}

/// <summary>
/// Kept to the left at no wider than its widest (the Mac's frame(maxWidth:) in a leading column), and as wide as the
/// room allows below that — a stretched element with a MaxWidth would be centred instead.
/// </summary>
internal sealed class LeftMax : Decorator
{
    public double Max { get; init; } = double.PositiveInfinity;

    protected override Size MeasureOverride(Size available)
    {
        var w = Math.Min(available.Width, Max);
        Child?.Measure(new Size(w, available.Height));
        var d = Child?.DesiredSize ?? default;
        return new Size(double.IsInfinity(w) ? d.Width : w, d.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Child?.Arrange(new Rect(0, 0, Math.Min(final.Width, Max), final.Height));
        return final;
    }
}
