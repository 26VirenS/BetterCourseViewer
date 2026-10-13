using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Kit;

/// <summary>Windows 11's type ramp (Segoe UI Variable): the sizes and weights WinUI names.</summary>
public enum Ty { Caption, Body, BodyStrong, BodyLarge, Subtitle, Title, TitleLarge, Display, Count }

/// <summary>The theme brushes by the names Windows 11 gives them (WPF-UI's resources): follow light, dark and the accent.</summary>
public static class B
{
    public const string Text = "TextFillColorPrimaryBrush";
    public const string Text2 = "TextFillColorSecondaryBrush";
    public const string Text3 = "TextFillColorTertiaryBrush";
    public const string TextDisabled = "TextFillColorDisabledBrush";
    public const string Accent = "AccentFillColorDefaultBrush";
    public const string AccentText = "AccentTextFillColorPrimaryBrush";
    public const string OnAccent = "TextOnAccentFillColorPrimaryBrush";
    public const string Card = "CardBackgroundFillColorDefaultBrush";
    public const string Card2 = "CardBackgroundFillColorSecondaryBrush";
    public const string CardStroke = "CardStrokeColorDefaultBrush";
    public const string Control = "ControlFillColorDefaultBrush";
    public const string Control2 = "ControlFillColorSecondaryBrush";
    public const string ControlStroke = "ControlStrokeColorDefaultBrush";
    public const string StrongStroke = "ControlStrongStrokeColorDefaultBrush";
    public const string StrongFill = "ControlStrongFillColorDefaultBrush";
    public const string Subtle = "SubtleFillColorSecondaryBrush";
    public const string Subtle3 = "SubtleFillColorTertiaryBrush";
    public const string Divider = "DividerStrokeColorDefaultBrush";
    public const string Layer = "LayerFillColorDefaultBrush";
    public const string LayerAlt = "LayerFillColorAltBrush";
    public const string Solid = "SolidBackgroundFillColorBaseBrush";
    public const string Solid2 = "SolidBackgroundFillColorSecondaryBrush";
    public const string Solid3 = "SolidBackgroundFillColorTertiaryBrush";
    public const string Flyout = "FlyoutBackground";
    public const string FlyoutStroke = "SurfaceStrokeColorFlyoutBrush";
    public const string Smoke = "SmokeFillColorDefaultBrush";
    public const string Critical = "SystemFillColorCriticalBrush";
    public const string CriticalBack = "SystemFillColorCriticalBackgroundBrush";
    public const string Success = "SystemFillColorSuccessBrush";
    public const string SuccessBack = "SystemFillColorSuccessBackgroundBrush";
    public const string Caution = "SystemFillColorCautionBrush";
    public const string CautionBack = "SystemFillColorCautionBackgroundBrush";
    public const string Attention = "SystemFillColorAttentionBrush";
    public const string AttentionBack = "SystemFillColorAttentionBackgroundBrush";
    public const string Window = "ApplicationBackgroundBrush";
}

/// <summary>
/// Simpl's kit of Windows 11 pieces, made in code: text in Windows' type ramp, Fluent icons, cards and the Settings
/// app's rows, rings and bars, chips and icon tiles, and the loading and empty states — every colour a theme resource.
/// </summary>
public static class K
{
    // ---- the corner a kit surface is rounded to (Styles.xaml's buttons read it) ----

    public static readonly DependencyProperty CornerProperty = DependencyProperty.RegisterAttached(
        "Corner", typeof(CornerRadius), typeof(K), new FrameworkPropertyMetadata(new CornerRadius(4)));

    public static CornerRadius GetCorner(DependencyObject o) => (CornerRadius)o.GetValue(CornerProperty);
    public static void SetCorner(DependencyObject o, CornerRadius v) => o.SetValue(CornerProperty, v);

    // ---- faces ----

    public static readonly FontFamily TextFace = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily SmallFace = new("Segoe UI Variable Small, Segoe UI");
    public static readonly FontFamily DisplayFace = new("Segoe UI Variable Display, Segoe UI");

    public static (double Size, FontWeight Weight, FontFamily Face, double Line) Ramp(Ty ty) => ty switch
    {
        Ty.Caption => (12, FontWeights.Normal, SmallFace, 16),
        Ty.Body => (14, FontWeights.Normal, TextFace, 20),
        Ty.BodyStrong => (14, FontWeights.SemiBold, TextFace, 20),
        Ty.BodyLarge => (18, FontWeights.Normal, TextFace, 24),
        Ty.Subtitle => (20, FontWeights.SemiBold, DisplayFace, 28),
        Ty.Title => (28, FontWeights.SemiBold, DisplayFace, 36),
        Ty.TitleLarge => (40, FontWeights.SemiBold, DisplayFace, 52),
        Ty.Display => (68, FontWeights.SemiBold, DisplayFace, 92),
        Ty.Count => (34, FontWeights.SemiBold, DisplayFace, 40),
        _ => (14, FontWeights.Normal, TextFace, 20),
    };

    // ---- theme colours on any element ----

    public static T Fg<T>(this T e, string key) where T : FrameworkElement
    {
        switch (e)
        {
            case TextBlock t: t.SetResourceReference(TextBlock.ForegroundProperty, key); break;
            case Control c: c.SetResourceReference(Control.ForegroundProperty, key); break;
            case Shape s: s.SetResourceReference(Shape.FillProperty, key); break;
            default: e.SetResourceReference(TextElement.ForegroundProperty, key); break;
        }
        return e;
    }

    public static T Bg<T>(this T e, string key) where T : FrameworkElement
    {
        switch (e)
        {
            case Border b: b.SetResourceReference(Border.BackgroundProperty, key); break;
            case Panel p: p.SetResourceReference(Panel.BackgroundProperty, key); break;
            case Control c: c.SetResourceReference(Control.BackgroundProperty, key); break;
            case Shape s: s.SetResourceReference(Shape.FillProperty, key); break;
        }
        return e;
    }

    public static T Stroke<T>(this T e, string key, double thickness = 1) where T : FrameworkElement
    {
        switch (e)
        {
            case Border b: b.SetResourceReference(Border.BorderBrushProperty, key); b.BorderThickness = new Thickness(thickness); break;
            case Control c: c.SetResourceReference(Control.BorderBrushProperty, key); c.BorderThickness = new Thickness(thickness); break;
            case Shape s: s.SetResourceReference(Shape.StrokeProperty, key); s.StrokeThickness = thickness; break;
        }
        return e;
    }

    public static T Margin<T>(this T e, double l, double t, double r, double b) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }
    public static T Margin<T>(this T e, double all) where T : FrameworkElement { e.Margin = new Thickness(all); return e; }
    public static T Align<T>(this T e, HorizontalAlignment h, VerticalAlignment v = VerticalAlignment.Stretch) where T : FrameworkElement { e.HorizontalAlignment = h; e.VerticalAlignment = v; return e; }
    public static T Col<T>(this T e, int c, int span = 1) where T : UIElement { Grid.SetColumn(e, c); if (span > 1) Grid.SetColumnSpan(e, span); return e; }
    public static T Row<T>(this T e, int r, int span = 1) where T : UIElement { Grid.SetRow(e, r); if (span > 1) Grid.SetRowSpan(e, span); return e; }
    public static T Tip<T>(this T e, string tip) where T : FrameworkElement { e.ToolTip = tip; return e; }

    // ---- text ----

    public static TextBlock Text(string s, Ty ty = Ty.Body, string brush = B.Text, TextWrapping wrap = TextWrapping.NoWrap)
    {
        var t = new TextBlock { Text = s, TextWrapping = wrap, TextTrimming = TextTrimming.CharacterEllipsis };
        Style(t, ty);
        t.Fg(brush);
        return t;
    }

    /// <summary>Text that wraps, at most so many lines (0: as many as it needs).</summary>
    public static TextBlock Wrapped(string s, Ty ty = Ty.Body, string brush = B.Text, int maxLines = 0)
    {
        var t = Text(s, ty, brush, TextWrapping.Wrap);
        if (maxLines > 0) t.MaxHeight = Ramp(ty).Line * maxLines + 1;
        return t;
    }

    public static void Style(TextBlock t, Ty ty)
    {
        var (size, weight, face, line) = Ramp(ty);
        t.FontSize = size;
        t.FontWeight = weight;
        t.FontFamily = face;
        t.LineHeight = line;
        t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
    }

    public static TextBlock Strong(this TextBlock t) { t.FontWeight = FontWeights.SemiBold; return t; }
    public static TextBlock Center(this TextBlock t) { t.TextAlignment = TextAlignment.Center; t.HorizontalAlignment = HorizontalAlignment.Center; return t; }
    public static TextBlock Struck(this TextBlock t) { t.TextDecorations = TextDecorations.Strikethrough; return t; }

    // ---- icons (Fluent System Icons, carried in the app: the same on every PC) ----

    public static SymbolIcon Icon(SymbolRegular s, double size = 16, string brush = B.Text, bool filled = false)
    {
        var i = new SymbolIcon { Symbol = s, Filled = filled, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        i.SetResourceReference(Control.ForegroundProperty, brush);
        return i;
    }

    public static SymbolIcon Icon(SymbolRegular s, double size, Color color, bool filled = false)
    {
        var i = new SymbolIcon { Symbol = s, Filled = filled, FontSize = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        return i;
    }

    // ---- layout ----

    public static Stack V(double spacing, params UIElement?[] kids) => new Stack(Orientation.Vertical, spacing, kids);
    public static Stack H(double spacing, params UIElement?[] kids) => new Stack(Orientation.Horizontal, spacing, kids);

    /// <summary>A grid of columns: "auto", "*", "2*" or a number of DIPs, and its children (placed with .Col()).</summary>
    public static Grid Columns(string cols, params UIElement?[] kids)
    {
        var g = new Grid();
        foreach (var c in cols.Split(',', StringSplitOptions.TrimEntries))
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = Length(c) });
        foreach (var k in kids) if (k != null) g.Children.Add(k);
        return g;
    }

    public static Grid Rows(string rows, params UIElement?[] kids)
    {
        var g = new Grid();
        foreach (var r in rows.Split(',', StringSplitOptions.TrimEntries))
            g.RowDefinitions.Add(new RowDefinition { Height = Length(r) });
        foreach (var k in kids) if (k != null) g.Children.Add(k);
        return g;
    }

    public static GridLength Length(string s) => s switch
    {
        "auto" => GridLength.Auto,
        "*" => new GridLength(1, GridUnitType.Star),
        _ when s.EndsWith('*') => new GridLength(double.Parse(s[..^1], System.Globalization.CultureInfo.InvariantCulture), GridUnitType.Star),
        _ => new GridLength(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)),
    };

    // ---- surfaces ----

    /// <summary>Windows 11's card: the card fill, its hairline edge, rounded 8 (4 for the Settings app's rows).</summary>
    public static Border Card(UIElement? child, Thickness? pad = null, double corner = 8)
    {
        var b = new Border { Child = child, Padding = pad ?? new Thickness(16), CornerRadius = new CornerRadius(corner), SnapsToDevicePixels = true };
        b.Bg(B.Card).Stroke(B.CardStroke);
        return b;
    }

    /// <summary>A card a click opens something from.</summary>
    public static Button CardButton(UIElement child, Action click, double corner = 8, Thickness? pad = null)
    {
        var b = new Button { Content = child };
        b.SetResourceReference(FrameworkElement.StyleProperty, "CardButton");
        SetCorner(b, new CornerRadius(corner));
        if (pad != null) b.Padding = pad.Value;
        b.Click += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    /// <summary>A quiet surface that shows itself under the pointer (a row, a link, an item).</summary>
    public static Button Subtle(UIElement child, Action? click, Thickness? pad = null, double corner = 4)
    {
        var b = new Button { Content = child };
        b.SetResourceReference(FrameworkElement.StyleProperty, "SubtleButton");
        SetCorner(b, new CornerRadius(corner));
        if (pad != null) b.Padding = pad.Value;
        // (a press handled where it lands: a button inside a row's own button does not open the row too)
        if (click != null) b.Click += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    /// <summary>Windows 11's button: standard, or the accent's for the one thing a step is for.</summary>
    public static Wpf.Ui.Controls.Button Button(string text, Action? click, bool accent = false, SymbolRegular? icon = null)
    {
        var b = new Wpf.Ui.Controls.Button { Content = text, Appearance = accent ? ControlAppearance.Primary : ControlAppearance.Secondary, MinWidth = 96 };
        if (icon is { } i) b.Icon = new SymbolIcon { Symbol = i };
        if (click != null) b.Click += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    /// <summary>A button that is only its words in the accent colour (WinUI's HyperlinkButton).</summary>
    public static Wpf.Ui.Controls.Button Link(string text, Action click, Ty ty = Ty.Body)
    {
        var t = Text(text, ty, B.AccentText);
        var b = new Wpf.Ui.Controls.Button { Content = t, Appearance = ControlAppearance.Transparent, Padding = new Thickness(6, 3, 6, 4), BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
        b.SetResourceReference(Control.BackgroundProperty, "HyperlinkButtonBackground");
        b.Click += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    /// <summary>A subtle button that is only a symbol (a toolbar's, a card's corner).</summary>
    public static Button IconButton(SymbolRegular s, Action click, string tip, double size = 16, double box = 32)
    {
        var b = Subtle(Icon(s, size, B.Text), null, new Thickness(0), 4);
        b.Width = box;
        b.Height = box;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, e) => { e.Handled = true; click(); };
        return b;
    }

    // ---- the Settings app's rows ----

    /// <summary>A section's heading over its rows (Windows 11 Settings: Body Strong, 30 above, 8 below).</summary>
    public static TextBlock SectionHeader(string text, double top = 30) => Text(text, Ty.BodyStrong).Margin(1, top, 0, 8);

    /// <summary>
    /// A row as Windows 11's Settings draws one (its SettingsCard): an icon, the name over a line that says what it is,
    /// and what changes it at the right; one that opens something has the card's pointer-over fill and a chevron.
    /// </summary>
    public static FrameworkElement Row(SymbolRegular? icon, string header, string? description, UIElement? action = null, Action? click = null, bool chevron = true)
    {
        var grid = Columns("auto, *, auto, auto");
        if (icon is { } ic) grid.Children.Add(Icon(ic, 20, B.Text).Margin(2, 0, 20, 0).Col(0));
        var words = V(0, Text(header, Ty.Body, B.Text, TextWrapping.Wrap));
        if (!string.IsNullOrEmpty(description)) words.Children.Add(Wrapped(description, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        words.Margin = new Thickness(0, 13, 16, 13);
        grid.Children.Add(words.Col(1));
        if (action is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(fe.Col(2)); }
        if (click != null)
        {
            if (chevron) grid.Children.Add(Icon(SymbolRegular.ChevronRight20, 12, B.Text2).Margin(12, 0, 0, 0).Col(3));
            var b = new Button { Content = grid };
            b.SetResourceReference(FrameworkElement.StyleProperty, "RowButton");
            b.Click += (_, e) => { e.Handled = true; click(); };
            return b;
        }
        var card = Card(grid, new Thickness(16, 0, 16, 0), 4);
        card.MinHeight = 68;
        return card;
    }

    /// <summary>Rows that belong together (the Settings app's spacing between cards: 4).</summary>
    public static Stack Group(params UIElement?[] rows) => V(4, rows);

    // ---- small pieces ----

    /// <summary>A word in a pill, tinted its colour (a flag, a state): the tint behind, the colour itself for the word.</summary>
    public static Border Chip(string text, Color tint, bool strong = false)
    {
        var t = Text(text, Ty.Caption);
        t.Foreground = Brush(Darkened(tint));
        t.FontWeight = strong ? FontWeights.SemiBold : FontWeights.Medium;
        return new Border
        {
            Child = t, Padding = new Thickness(8, 1, 8, 2), CornerRadius = new CornerRadius(10),
            Background = Brush(Color.FromArgb(Support.Appearance.Dark ? (byte)56 : (byte)36, tint.R, tint.G, tint.B)),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    /// <summary>A colour made to read as words on its own tint: lighter on dark, deeper on light.</summary>
    public static Color Darkened(Color c)
    {
        if (Support.Appearance.Dark) return Mix(c, Colors.White, 0.25);
        return Mix(c, Colors.Black, 0.28);
    }

    /// <summary>A symbol on a rounded square of its colour's tint (a kind of work, an activity, a setting).</summary>
    public static Border IconTile(SymbolRegular s, Color color, double size = 32, bool solid = false)
    {
        var icon = Icon(s, size * 0.5, solid ? Colors.White : color, filled: true);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.25),
            Background = solid ? Brush(color) : Brush(Color.FromArgb(Support.Appearance.Dark ? (byte)52 : (byte)30, color.R, color.G, color.B)),
            Child = icon, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>A dot of a colour (a course's).</summary>
    public static Ellipse Dot(Color c, double size = 8) => new() { Width = size, Height = size, Fill = Brush(c), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Windows 11's progress bar: a thin track, a rounded bar of the colour over it.</summary>
    public static FrameworkElement Bar(double fraction, Color color, double height = 4)
    {
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2) }.Bg(B.StrongFill);
        track.Opacity = 1;
        var fill = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Background = Brush(color), HorizontalAlignment = HorizontalAlignment.Left };
        var g = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        var trackHolder = new Border { Height = 1, CornerRadius = new CornerRadius(0.5), VerticalAlignment = VerticalAlignment.Center }.Bg(B.StrongStroke);
        g.Children.Add(trackHolder);
        g.Children.Add(fill);
        var f = Math.Clamp(fraction, 0, 1);
        g.SizeChanged += (_, e) => fill.Width = Math.Max(f > 0 ? height : 0, e.NewSize.Width * f);
        return g;
    }

    /// <summary>A ring of a colour round a value (a course's score): the track faint, the arc as far as the fraction.</summary>
    public static FrameworkElement Ring(double? fraction, Color color, double size, double thickness = 4, UIElement? inside = null)
    {
        var g = new Grid { Width = size, Height = size };
        var track = new Ellipse { StrokeThickness = thickness, Margin = new Thickness(0) };
        track.SetResourceReference(Shape.StrokeProperty, B.Subtle3);
        g.Children.Add(track);
        if (fraction is { } f && f > 0)
            g.Children.Add(new Path
            {
                Data = ArcGeometry(size, thickness, Math.Clamp(f, 0, 1)), Stroke = Brush(color), StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
        if (inside != null) g.Children.Add(inside);
        return g;
    }

    /// <summary>An arc from the top, clockwise, as far round as the fraction, inside a square of the size.</summary>
    public static Geometry ArcGeometry(double size, double thickness, double fraction)
    {
        var r = (size - thickness) / 2;
        var c = size / 2;
        var sweep = Math.Min(359.99, 360 * fraction);
        var a = (sweep - 90) * Math.PI / 180;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Cos(a), c + r * Math.Sin(a));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    /// <summary>A person's picture, or their initials on a circle (Windows' PersonPicture).</summary>
    public static FrameworkElement Avatar(string name, string? initials, string? image, double size)
    {
        var g = new Grid { Width = size, Height = size };
        var circle = new Ellipse().Bg(B.StrongFill);
        circle.SetResourceReference(Shape.FillProperty, "ControlStrongFillColorDefaultBrush");
        g.Children.Add(circle);
        var letters = initials;
        if (string.IsNullOrEmpty(letters))
        {
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            letters = string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
        }
        var t = new TextBlock { Text = letters, FontSize = size * 0.38, FontWeight = FontWeights.SemiBold, FontFamily = DisplayFace, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorInverseBrush");
        g.Children.Add(t);
        if (!string.IsNullOrEmpty(image) && Uri.TryCreate(image, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
        {
            try
            {
                var bmp = AvatarImage(u, (int)(size * 2));
                var photo = new Ellipse { Fill = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill } };
                // (the initials until the picture is there: at once when it was read before)
                if (bmp.IsDownloading) bmp.DownloadCompleted += (_, _) => t.Visibility = Visibility.Hidden;
                else t.Visibility = Visibility.Hidden;
                g.Children.Add(photo);
            }
            catch { }
        }
        return g;
    }

    private static readonly Dictionary<(string Url, int Width), BitmapImage> Avatars = new();

    /// <summary>A person's picture at a size, fetched and decoded once — at the size it is drawn, never larger — and kept
    /// while Simpl runs (the last hundred or so); one that could not be read is asked for again the next time.</summary>
    private static BitmapImage AvatarImage(Uri u, int width)
    {
        var key = (u.AbsoluteUri, width);
        if (Avatars.TryGetValue(key, out var have)) return have;
        if (Avatars.Count >= 96) Avatars.Clear();
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = u;
        bmp.DecodePixelWidth = width;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        void Drop() { if (Avatars.TryGetValue(key, out var b) && ReferenceEquals(b, bmp)) Avatars.Remove(key); }
        bmp.DownloadFailed += (_, _) => Drop();
        bmp.DecodeFailed += (_, _) => Drop();
        Avatars[key] = bmp;
        return bmp;
    }

    /// <summary>Windows 11's indeterminate progress ring.</summary>
    public static ProgressRing Spinner(double size = 24) => new() { IsIndeterminate = true, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    /// <summary>A small spinner beside a line (a list still reading).</summary>
    public static FrameworkElement Reading(string text) => H(10, Spinner(16), Text(text, Ty.Body, B.Text2)).Margin(4, 8, 0, 8);

    /// <summary>The Mac's LoadState: a spinner in the middle of the page, or why it could not load and Try Again.</summary>
    public static FrameworkElement LoadState(string? error, Action retry)
    {
        if (error == null) return new Grid { MinHeight = 320, Children = { Spinner(32) }, Tag = PlaceholderMark };
        var again = Button("Try Again", retry, accent: true);
        again.HorizontalAlignment = HorizontalAlignment.Center;
        var v = V(12,
            Icon(SymbolRegular.ErrorCircle24, 40, B.Text3),
            Text("Couldn’t Load", Ty.Subtitle).Center(),
            Wrapped(error, Ty.Body, B.Text2).Center(),
            again);
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.MaxWidth = 420;
        return new Grid { MinHeight = 360, Children = { v } };
    }

    private static readonly object PlaceholderMark = new();

    /// <summary>A screen's content that is only the spinner it shows while nothing has been read (<see cref="LoadState"/>).</summary>
    public static bool IsPlaceholder(FrameworkElement e) => ReferenceEquals(e.Tag, PlaceholderMark);

    /// <summary>Nothing to show: a quiet symbol and a line, inside a card's room.</summary>
    public static FrameworkElement Empty(SymbolRegular icon, string text)
    {
        var row = H(12, Icon(icon, 20, B.Text3), Wrapped(text, Ty.Body, B.Text2));
        row.Margin = new Thickness(4, 10, 4, 10);
        return row;
    }

    /// <summary>A hairline between rows, set in from the left.</summary>
    public static Border Divider(double left = 0) => new Border { Height = 1, Margin = new Thickness(left, 0, 0, 0), SnapsToDevicePixels = true }.Bg(B.Divider);

    // ---- colours ----

    public static Color Hex(string? hex, Color? fallback = null) => Support.Appearance.Hex(hex) ?? fallback ?? Color.FromRgb(0x8a, 0x8a, 0x8a);

    private static readonly Dictionary<Color, SolidColorBrush> BrushesMade = new();

    /// <summary>A colour's brush: made once and frozen — shared by everything drawn in that colour, and drawn without WPF
    /// watching it for changes (a brush that changes is made with new SolidColorBrush, as the periodic table's are).</summary>
    public static SolidColorBrush Brush(Color c)
    {
        if (BrushesMade.TryGetValue(c, out var b)) return b;
        if (BrushesMade.Count >= 4096) BrushesMade.Clear();
        b = new SolidColorBrush(c);
        b.Freeze();
        BrushesMade[c] = b;
        return b;
    }

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);

    /// <summary>A flag's colour by its kind (the page's words: red, orange, green, blue, purple, gray).</summary>
    public static Color Tone(string? kind) => kind switch
    {
        "red" or "late" or "missing" or "danger" or "bad" => Color.FromRgb(0xe8, 0x4a, 0x3f),
        "orange" or "warn" or "warning" => Color.FromRgb(0xf7, 0x8c, 0x1a),
        "green" or "ok" or "done" or "success" or "good" => Color.FromRgb(0x2f, 0xa8, 0x4f),
        "blue" or "info" => Color.FromRgb(0x29, 0x7b, 0xe6),
        "purple" => Color.FromRgb(0x8e, 0x5c, 0xd9),
        "yellow" => Color.FromRgb(0xd8, 0xa8, 0x00),
        _ => Color.FromRgb(0x86, 0x86, 0x8b),
    };

    /// <summary>A soft shadow under a raised surface (Windows 11's flyout elevation); none where this PC draws shadows on
    /// the processor (Fx), the surface's hairline edge alone.</summary>
    public static DropShadowEffect? Shadow(double blur = 28, double depth = 8, double opacity = 0.24) =>
        Fx.Shadows ? new() { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity, Color = Colors.Black, RenderingBias = RenderingBias.Quality } : null;

    /// <summary>A faint shadow under words on a colour (a course card's code), so they lift off it; none where shadows
    /// are not drawn (Fx).</summary>
    public static DropShadowEffect? TextShadow() =>
        Fx.Shadows ? new() { BlurRadius = 4, ShadowDepth = 1, Direction = 270, Opacity = 0.18, Color = Colors.Black } : null;
}

/// <summary>A stack with room between its children (WinUI's StackPanel.Spacing).</summary>
public sealed class Stack : Panel
{
    public Orientation Orientation { get; set; }
    public double Spacing { get; set; }

    public Stack() { }

    public Stack(Orientation o, double spacing, IEnumerable<UIElement?> kids)
    {
        Orientation = o;
        Spacing = spacing;
        foreach (var k in kids) if (k != null) Children.Add(k);
    }

    protected override Size MeasureOverride(Size available)
    {
        double main = 0, cross = 0;
        var n = 0;
        var vertical = Orientation == Orientation.Vertical;
        var slot = vertical ? new Size(available.Width, double.PositiveInfinity) : new Size(double.PositiveInfinity, available.Height);
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) continue;
            c.Measure(slot);
            var d = c.DesiredSize;
            main += vertical ? d.Height : d.Width;
            cross = Math.Max(cross, vertical ? d.Width : d.Height);
            n++;
        }
        main += Math.Max(0, n - 1) * Spacing;
        return vertical ? new Size(cross, main) : new Size(main, cross);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double at = 0;
        var vertical = Orientation == Orientation.Vertical;
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) continue;
            var d = c.DesiredSize;
            if (vertical)
            {
                c.Arrange(new Rect(0, at, final.Width, d.Height));
                at += d.Height + Spacing;
            }
            else
            {
                c.Arrange(new Rect(at, 0, d.Width, final.Height));
                at += d.Width + Spacing;
            }
        }
        return final;
    }
}
