using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Simpl.Theme;

namespace Simpl.Components;

/// <summary>
/// A small SwiftUI-like way to build the app's views in code, so each Mac screen (SwiftUI) ports almost line for line:
/// VStack, HStack (with Spacer), Text in the type scale, Icon (SF Symbol names), Card, Button, Divider, Circle.
/// Colours are the theme's resources ("Text2", "Accent", …) and follow light and dark.
/// </summary>
public static class UI
{
    /// <summary>Marks the flexible space in an <see cref="HStack"/>.</summary>
    public sealed class SpacerElement : FrameworkElement { }

    public static SpacerElement Spacer(double min = 0) => new() { MinWidth = min };

    public static T Res<T>(string key) where T : class => (Application.Current.Resources[key] as T)!;

    /// <summary>A theme colour, following light and dark (a DynamicResource).</summary>
    public static T Themed<T>(this T e, DependencyProperty p, string key) where T : FrameworkElement
    {
        e.SetResourceReference(p, key);
        return e;
    }

    public static TextBlock Text(string? text, string style = "Body", string? color = null, bool wrap = false, int maxLines = 0)
    {
        var t = new TextBlock { Text = text ?? "" };
        t.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (color != null) t.SetResourceReference(TextBlock.ForegroundProperty, color);
        if (wrap || maxLines > 1)
        {
            t.TextWrapping = TextWrapping.Wrap;
            if (maxLines > 1) t.MaxHeight = maxLines * t.FontSize * 1.42;
        }
        return t;
    }

    public static TextBlock Bold(this TextBlock t, FontWeight? w = null) { t.FontWeight = w ?? FontWeights.SemiBold; return t; }

    public static TextBlock Size(this TextBlock t, double size) { t.FontSize = size; return t; }

    public static TextBlock Colored(this TextBlock t, Brush b) { t.Foreground = b; return t; }

    /// <summary>An SF Symbol's Windows counterpart, in the icon face.</summary>
    public static TextBlock Icon(string symbol, double size = 15, string color = "Text2")
    {
        var t = new TextBlock { Text = Glyphs.Of(symbol), FontSize = size };
        t.SetResourceReference(FrameworkElement.StyleProperty, "Icon");
        t.SetResourceReference(TextBlock.ForegroundProperty, color);
        return t;
    }

    public static TextBlock Icon(string symbol, double size, Brush brush)
    {
        var t = new TextBlock { Text = Glyphs.Of(symbol), FontSize = size, Foreground = brush };
        t.SetResourceReference(FrameworkElement.StyleProperty, "Icon");
        return t;
    }

    public static StackPanel VStack(double spacing, params UIElement?[] children) => Stack(Orientation.Vertical, spacing, children);

    private static StackPanel Stack(Orientation o, double spacing, IEnumerable<UIElement?> children)
    {
        var s = new StackPanel { Orientation = o };
        var first = true;
        foreach (var c in children)
        {
            if (c == null) continue;
            if (!first && spacing > 0 && c is FrameworkElement fe)
            {
                var m = fe.Margin;
                fe.Margin = o == Orientation.Vertical ? new Thickness(m.Left, m.Top + spacing, m.Right, m.Bottom) : new Thickness(m.Left + spacing, m.Top, m.Right, m.Bottom);
            }
            s.Children.Add(c);
            first = false;
        }
        return s;
    }

    public static StackPanel VStack(double spacing, IEnumerable<UIElement?> children) => Stack(Orientation.Vertical, spacing, children);

    /// <summary>A row: each child its own width, a <see cref="Spacer"/> taking what is left; centred vertically.</summary>
    public static Grid HStack(double spacing, params UIElement?[] children) => HStack(spacing, (IEnumerable<UIElement?>)children);

    public static Grid HStack(double spacing, IEnumerable<UIElement?> children)
    {
        var g = new Grid();
        var i = 0;
        foreach (var c in children)
        {
            if (c == null) continue;
            var spacer = c is SpacerElement;
            var fill = c is FrameworkElement { Tag: "fill" };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = spacer || fill ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            if (c is FrameworkElement fe)
            {
                if (fe.VerticalAlignment == VerticalAlignment.Stretch) fe.VerticalAlignment = VerticalAlignment.Center;
                if (i > 0 && spacing > 0) fe.Margin = new Thickness(fe.Margin.Left + spacing, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            }
            Grid.SetColumn(c, i++);
            g.Children.Add(c);
        }
        return g;
    }

    /// <summary>Takes the room left in an <see cref="HStack"/> (and trims its text there).</summary>
    public static T Fill<T>(this T e) where T : FrameworkElement { e.Tag = "fill"; return e; }

    public static T Margin<T>(this T e, double all) where T : FrameworkElement { e.Margin = new Thickness(all); return e; }
    public static T Margin<T>(this T e, double h, double v) where T : FrameworkElement { e.Margin = new Thickness(h, v, h, v); return e; }
    public static T Margin<T>(this T e, double l, double t, double r, double b) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }
    public static T Width<T>(this T e, double w) where T : FrameworkElement { e.Width = w; return e; }
    public static T Height<T>(this T e, double h) where T : FrameworkElement { e.Height = h; return e; }
    public static T MaxW<T>(this T e, double w) where T : FrameworkElement { e.MaxWidth = w; return e; }
    public static T Left<T>(this T e) where T : FrameworkElement { e.HorizontalAlignment = HorizontalAlignment.Left; return e; }
    public static T Right<T>(this T e) where T : FrameworkElement { e.HorizontalAlignment = HorizontalAlignment.Right; return e; }
    public static T Center<T>(this T e) where T : FrameworkElement { e.HorizontalAlignment = HorizontalAlignment.Center; return e; }
    public static T Top<T>(this T e) where T : FrameworkElement { e.VerticalAlignment = VerticalAlignment.Top; return e; }
    public static T Tip<T>(this T e, string? tip) where T : FrameworkElement { if (!string.IsNullOrEmpty(tip)) e.ToolTip = tip; return e; }
    public static T Opacity<T>(this T e, double o) where T : UIElement { e.Opacity = o; return e; }

    /// <summary>A card: the content on a rounded surface lifted off the page.</summary>
    public static Border Card(UIElement content, double padding = 16, double radius = 18)
    {
        var b = new Border { Child = content, Padding = new Thickness(padding), CornerRadius = new CornerRadius(radius) };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        return b;
    }

    /// <summary>Glass (until the Liquid Glass renderer): a veil over what is behind, a bright hairline.</summary>
    public static Border Glass(UIElement content, double padding = 12, double radius = 18)
    {
        var b = new Border { Child = content, Padding = new Thickness(padding), CornerRadius = new CornerRadius(radius) };
        b.SetResourceReference(FrameworkElement.StyleProperty, "GlassCard");
        return b;
    }

    /// <summary>A well: a quiet rounded ground (a chip, a field, a count).</summary>
    public static Border Well(UIElement content, double h = 10, double v = 4, double radius = 999, string brush = "Well")
    {
        var b = new Border { Child = content, Padding = new Thickness(h, v, h, v), CornerRadius = new CornerRadius(radius) };
        b.SetResourceReference(Border.BackgroundProperty, brush);
        return b;
    }

    public static Button Button(object label, Action click, string style = "GlassButton", string? tip = null)
    {
        var b = new Button { Content = label is string s ? Text(s, "Callout").Bold(FontWeights.Medium) : label };
        if (label is string && style == "ProminentButton") ((TextBlock)b.Content).SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
        b.SetResourceReference(FrameworkElement.StyleProperty, style);
        b.Click += (_, _) => click();
        if (tip != null) b.ToolTip = tip;
        return b;
    }

    /// <summary>A label with its glyph before it ("Label(\"Open\", systemImage: …)").</summary>
    public static Grid Label(string text, string symbol, string style = "Callout", string color = "Text", double gap = 7) =>
        HStack(gap, Icon(symbol, 14, color), Text(text, style, color));

    /// <summary>A whole-row button: the content, pressed anywhere in it.</summary>
    public static Button Row(UIElement content, Action click, double padding = 10)
    {
        var b = new Button { Content = content, Padding = new Thickness(padding, padding * 0.7, padding, padding * 0.7) };
        b.SetResourceReference(FrameworkElement.StyleProperty, "RowButton");
        b.Click += (_, _) => click();
        return b;
    }

    public static Border Divider(double inset = 0)
    {
        var d = new Border { Height = 1, Margin = new Thickness(inset, 0, 0, 0) };
        d.SetResourceReference(Border.BackgroundProperty, "Edge");
        return d;
    }

    public static Ellipse Dot(string? hex, double size = 9) => new() { Width = size, Height = size, Fill = ThemeManager.HexBrush(hex), VerticalAlignment = VerticalAlignment.Center };

    public static Ellipse Dot(Brush b, double size = 9) => new() { Width = size, Height = size, Fill = b, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A rounded tile with a glyph (a course's colour, a kind of work).</summary>
    public static Border IconTile(string symbol, Color color, double size = 30, double radius = 8)
    {
        var bg = new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B));
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(radius), Background = bg,
            Child = Icon(symbol, size * 0.5, new SolidColorBrush(color)).Center(),
        };
    }

    /// <summary>Initials in a coloured circle (an avatar with no picture).</summary>
    public static Border Initials(string? initials, double size = 32, string? hex = "#007aff")
    {
        var t = Text(initials ?? "", "Callout").Bold();
        t.Foreground = Brushes.White;
        t.FontSize = size * 0.38;
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = ThemeManager.HexBrush(hex), Child = t };
    }

    /// <summary>A bar filled to a fraction (a score, a progress), in a colour, on a well.</summary>
    public static Grid Bar(double? fraction, Brush fill, double height = 6)
    {
        var g = new Grid { Height = height };
        var track = new Border { CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, "Well");
        g.Children.Add(track);
        if (fraction is { } f)
        {
            var bar = new Border { CornerRadius = new CornerRadius(height / 2), Background = fill, HorizontalAlignment = HorizontalAlignment.Left };
            g.Children.Add(bar);
            g.SizeChanged += (_, e) =>
            {
                var w = Math.Max(height, e.NewSize.Width * Math.Clamp(f, 0, 1));
                var anim = new DoubleAnimation(w, TimeSpan.FromMilliseconds(600)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                bar.BeginAnimation(FrameworkElement.WidthProperty, anim);
            };
        }
        return g;
    }

    /// <summary>A page: a centred column with room around it, scrolling as a whole.</summary>
    public static ScrollViewer Page(UIElement content, double maxWidth = 1100, double padding = 32)
    {
        if (content is FrameworkElement fe) { fe.MaxWidth = maxWidth; fe.HorizontalAlignment = HorizontalAlignment.Stretch; fe.Margin = new Thickness(padding, 20, padding, 40); }
        var host = new Grid();
        host.Children.Add(content);
        return new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.VerticalOnly };
    }

    /// <summary>A screen's heading: its title, and a line under it.</summary>
    public static StackPanel Heading(string title, string? sub = null, string? colorHex = null)
    {
        var title1 = Text(title, "LargeTitle");
        UIElement head = title1;
        if (colorHex != null)
        {
            var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3.5), Background = ThemeManager.HexBrush(colorHex), VerticalAlignment = VerticalAlignment.Center };
            head = HStack(10, dot, title1).Left();
        }
        return VStack(2, head, string.IsNullOrEmpty(sub) ? null : Text(sub, "Secondary"));
    }

    /// <summary>A section of a page: its heading (and what is at its right), then its content.</summary>
    public static StackPanel Section(string title, UIElement content, string? trailing = null, UIElement? accessory = null) =>
        VStack(10,
            HStack(10, Text(title, "Title3"), Spacer(), string.IsNullOrEmpty(trailing) ? null : Text(trailing, "Secondary"), accessory),
            content);

    /// <summary>Nothing to show: a glyph and a line, quietly.</summary>
    public static StackPanel Empty(string text, string symbol = "info.circle") =>
        VStack(8, Icon(symbol, 26, "Text3").Center(), Text(text, "Secondary", wrap: true).Center()).Margin(0, 30, 0, 30);

    /// <summary>Fades and lifts a view in (a screen arriving): 180 ms, ease-out.</summary>
    public static T FadeIn<T>(this T e, double fromY = 6) where T : UIElement
    {
        if (SystemParameters.ClientAreaAnimation == false) return e;
        e.Opacity = 0;
        var tt = new TranslateTransform(0, fromY);
        e.RenderTransform = tt;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        return e;
    }

    /// <summary>A pointer hand over a clickable view, and a click handler.</summary>
    public static T OnClick<T>(this T e, Action click) where T : UIElement
    {
        if (e is FrameworkElement fe) fe.Cursor = Cursors.Hand;
        e.MouseLeftButtonUp += (_, _) => click();
        return e;
    }
}
