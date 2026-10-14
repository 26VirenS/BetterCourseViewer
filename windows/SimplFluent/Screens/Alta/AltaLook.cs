using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// What the Knewton Alta and Macmillan Learning popups share (the Mac's quiz-style popup): a sheet as large as the
/// window less a margin, its own header row — the vendor's tile, the assignment's name over a line about it, and Close —
/// the ground under the vendor's page, the popup's Loading and failure in its place, and dialogs of the popup's own (a
/// Windows 11 ContentDialog over the popup, the page stepped aside: nothing can be drawn over a web view).
/// </summary>
internal static class AltaLook
{
    /// <summary>A full-size sheet over the window, its body edge to edge; Escape is the popup's to answer.</summary>
    public static Sheet Popup(FrameworkElement body, Window over)
    {
        var room = over.Content as FrameworkElement;
        var w = room?.ActualWidth > 0 ? room.ActualWidth : over.ActualWidth;
        var h = room?.ActualHeight > 0 ? room.ActualHeight : over.ActualHeight;
        return new Sheet { Body = body, Bare = true, Modal = true, Width = Math.Max(480, w - 48), Height = Math.Max(360, h - 48) };
    }

    /// <summary>
    /// Up over the window: the sheet's card as large as the window allows (a card is first held to a dialog's width until
    /// the window is resized), and the keyboard inside the popup — never on a button of the window's behind it.
    /// </summary>
    public static void Settle(FrameworkElement body)
    {
        for (DependencyObject? d = body; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is not Border card || VisualTreeHelper.GetParent(card) is not SheetHost host) continue;
            if (host.ActualWidth > 0) card.MaxWidth = Math.Max(280, host.ActualWidth - 48);
            if (host.ActualHeight > 0) card.MaxHeight = Math.Max(200, host.ActualHeight - 48);
            break;
        }
        body.Focusable = true;
        body.FocusVisualStyle = null;
        body.Focus();
        Keyboard.Focus(body);
    }

    /// <summary>The popup's header row: the vendor's tile, the assignment's name over its line, what the popup adds, the
    /// tools pinned to the title bar (which the popup covers), and Close.</summary>
    public static FrameworkElement Header(Engine.Engine engine, FrameworkElement tile, TextBlock title, TextBlock subtitle, UIElement? extra, Action close, string closeTip)
    {
        var words = K.V(0, title, subtitle);
        words.VerticalAlignment = VerticalAlignment.Center;
        var closeButton = K.Button("Close", close, icon: SymbolRegular.Dismiss24);
        closeButton.ToolTip = closeTip;
        var trailing = K.H(8, extra, new Simpl.Tools.PinBar(engine, inPopup: true), closeButton);
        trailing.VerticalAlignment = VerticalAlignment.Center;
        var row = K.Columns("auto, *, auto", tile.Margin(0, 0, 14, 0).Col(0), words.Col(1), trailing.Margin(12, 0, 0, 0).Col(2));
        row.Margin = new Thickness(24, 14, 16, 14);
        return row;
    }

    /// <summary>(2.99.33) The Canvas assignment a vendor's popup opens — its name, and its due line as every screen writes it
    /// ("Due Oct 12 at 11:59 PM", "" with none) — or null where it cannot be read.</summary>
    public static async Task<Simpl.Models.AssignmentData?> Assignment(Engine.Engine engine, Simpl.Engine.AltaLaunch launch)
    {
        if (string.IsNullOrEmpty(launch.Course) || string.IsNullOrEmpty(launch.Assignment)) return null;
        try { return await engine.Call<Simpl.Models.AssignmentData>("assignment", new { course = launch.Course, id = launch.Assignment }); }
        catch { return null; }
    }

    /// <summary>A due time the popup writes itself (no Canvas assignment to say it), as the engine writes one: "Due Oct 12
    /// at 11:59 PM", the year only when it is not this year.</summary>
    public static string DueText(DateTime d) =>
        $"Due {d.ToString(d.Year == DateTime.Now.Year ? "MMM d" : "MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)} at {d.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>A time zone's letters after a time ("11:59pm PDT"): the page runs in the PC's own zone, so it says nothing.</summary>
    private static readonly System.Text.RegularExpressions.Regex Zone = new(@"(?<=\d\s?[AaPp]\.?[Mm]\.?)\s+(?:[A-Z]{2,4}T|UTC|GMT)(?:[+-]\d{1,2}(?::?\d{2})?)?\b");

    /// <summary>A time in the publisher's words ("11:59pm", "8:08 pm", "12am") as every screen writes one ("11:59 PM",
    /// "8:08 PM", "12:00 AM"), its time zone left out (the engine's canvasTimes rule, for Alta's own lines).</summary>
    public static string Times(string words)
    {
        var s = Zone.Replace(words, "");
        return System.Text.RegularExpressions.Regex.Replace(s, @"\b(\d{1,2})(?::(\d{2}))?\s?([AaPp])\.?[Mm]\b\.?",
            m => $"{m.Groups[1].Value}:{(m.Groups[2].Success ? m.Groups[2].Value : "00")} {char.ToUpperInvariant(m.Groups[3].Value[0])}M");
    }

    /// <summary>
    /// Alta's own due words ("Monday, Oct 12 11:59pm PDT") read as a date and written as every screen writes a due line
    /// ("Due Oct 12 at 11:59 PM"); null when they cannot be read — then <see cref="Times"/> at least tidies their time.
    /// </summary>
    public static string? DueOf(string words)
    {
        var s = Times(words).Trim();
        // (the weekday at its head says nothing the date does not)
        s = System.Text.RegularExpressions.Regex.Replace(s, @"^(?:Mon|Tue|Tues|Wed|Thu|Thur|Thurs|Fri|Sat|Sun)[a-z]*\.?,?\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = s.Replace(" at ", " ");
        string[] formats = { "MMM d h:mm tt", "MMM d, h:mm tt", "MMMM d h:mm tt", "MMMM d, h:mm tt", "MMM d, yyyy h:mm tt", "MMMM d, yyyy h:mm tt", "M/d/yyyy h:mm tt", "M/d h:mm tt" };
        return DateTime.TryParseExact(s, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var d)
            ? DueText(d) : null;
    }

    /// <summary>The popup's Loading while the vendor's page opens (the Mac's ProgressView with its words).</summary>
    public static FrameworkElement Opening(string words)
    {
        var v = K.V(14, K.Spinner(32), K.Text(words, Ty.Body, B.Text2).Center());
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        return new Grid { Children = { v } };
    }

    /// <summary>Why the vendor's page could not be opened (the Mac's ContentUnavailableView).</summary>
    public static FrameworkElement Failure(string title, string why)
    {
        var v = K.V(12,
            K.Icon(SymbolRegular.Warning24, 40, B.Text3),
            K.Wrapped(title, Ty.Subtitle).Center(),
            K.Wrapped(why, Ty.Body, B.Text2).Center());
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.MaxWidth = 460;
        v.Margin = new Thickness(24);
        return new Grid { Children = { v } };
    }

    /// <summary>
    /// The frame the vendor's page sits in, as Windows 11 frames an app's content: a layer over the popup's ground, its
    /// top-left corner rounded, a hairline along its top and left.
    /// </summary>
    public static Border Frame(UIElement child)
    {
        var b = new Border { Child = child, CornerRadius = new CornerRadius(8, 0, 0, 0), SnapsToDevicePixels = true };
        b.Bg(B.Layer).Stroke(B.CardStroke);
        b.BorderThickness = new Thickness(1, 1, 0, 0);
        return b;
    }

    // ---- colours read from the theme (the web view's ground, the page's panels) ----

    public static Color? Resolve(string key) => Application.Current?.TryFindResource(key) switch
    {
        SolidColorBrush b => b.Color,
        Color c => c,
        _ => null,
    };

    /// <summary>The popup's page ground as one opaque colour: the layer over the base, as they show (a web view's own
    /// ground must be opaque, and match what is round it).</summary>
    public static Color Ground()
    {
        var bottom = Resolve(B.Solid) ?? (Support.Appearance.Dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xf3, 0xf3, 0xf3));
        if (Resolve(B.Layer) is not { } top) return bottom;
        var a = top.A / 255.0;
        return Color.FromRgb((byte)(top.R * a + bottom.R * (1 - a)), (byte)(top.G * a + bottom.G * (1 - a)), (byte)(top.B * a + bottom.B * (1 - a)));
    }

    public static System.Drawing.Color Drawing(Color c) => System.Drawing.Color.FromArgb(255, c.R, c.G, c.B);

    /// <summary>A theme brush as CSS (for what the page draws in the theme's colours).</summary>
    public static string Css(string key)
    {
        var c = Resolve(key) ?? Colors.Transparent;
        return $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})";
    }

    /// <summary>The accent as the theme draws it (its fill), for tints of it.</summary>
    public static Color Accent => Resolve(B.Accent) ?? Support.Appearance.Accent;

    // ---- the popup's own dialogs ----

    public sealed record Choice(string Text, Action Click, bool Accent = false, bool Enabled = true);

    /// <summary>
    /// A Windows 11 ContentDialog as the popup shows one over itself: its body on the dialog's top layer, its buttons in
    /// the foot, the accent one the default. (A sheet of the window's would wait its turn behind the popup.)
    /// </summary>
    public static Border Dialog(FrameworkElement body, IReadOnlyList<Choice> choices, double minWidth, double maxWidth)
    {
        var top = new Border { Child = body, Padding = new Thickness(24) };
        top.SetResourceReference(Border.BackgroundProperty, "ContentDialogTopOverlay");
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(top);
        if (choices.Count > 0)
        {
            var row = new UniformGrid { Rows = 1, Columns = choices.Count, Margin = new Thickness(24 - 4, 24, 24 - 4, 24) };
            foreach (var c in choices)
            {
                var b = new Wpf.Ui.Controls.Button
                {
                    Content = c.Text, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 0, 4, 0),
                    Appearance = c.Accent ? ControlAppearance.Primary : ControlAppearance.Secondary, IsEnabled = c.Enabled,
                };
                var go = c.Click;
                b.Click += (_, _) => go();
                row.Children.Add(b);
            }
            var foot = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0) };
            foot.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
            foot.SetResourceReference(Border.BorderBrushProperty, "ContentDialogSeparatorBorderBrush");
            grid.Children.Add(foot.Row(1));
        }
        grid.SizeChanged += (_, e) => grid.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        var card = new Border
        {
            Child = grid, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), MinWidth = minWidth, MaxWidth = maxWidth,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24),
            Effect = K.Shadow(64, 24, 0.32), SnapsToDevicePixels = true,
        };
        card.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
        card.SetResourceReference(Border.BorderBrushProperty, "ContentDialogBorderBrush");
        card.MouseLeftButtonDown += (_, e) => e.Handled = true;
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        return card;
    }

    /// <summary>A dialog's title row: the vendor's tile beside its words.</summary>
    public static FrameworkElement DialogTitle(string text, FrameworkElement? tile = null)
    {
        var t = K.Wrapped(text, Ty.Subtitle);
        t.TextTrimming = TextTrimming.None;
        if (tile == null) return t;
        return K.Columns("auto, *", tile.Margin(0, 0, 12, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), t.Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1));
    }

    /// <summary>Words that can be selected and copied, drawn as text (the Mac's textSelection).</summary>
    public static System.Windows.Controls.TextBox Selectable(string text, double size = 14, FontFamily? face = null, bool wrap = true)
    {
        var t = new System.Windows.Controls.TextBox
        {
            Text = text, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, FontSize = size, FontFamily = face ?? K.TextFace,
            IsTabStop = false, FocusVisualStyle = null,
        };
        t.Style = new Style(typeof(System.Windows.Controls.TextBox));
        t.SetResourceReference(Control.ForegroundProperty, B.Text);
        t.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, B.Accent);
        return t;
    }
}

/// <summary>
/// Where a popup's own dialogs show: over the whole popup, the popup dimmed behind (Windows' smoke), one at a time — the
/// vendor's page put out of sight meanwhile by whoever shows it. Escape and Enter are the dialog's while it is up.
/// </summary>
internal sealed class AltaLayer : Grid
{
    private readonly Border smoke = new();
    private Border? card;

    public Action? Escape { get; private set; }
    public Action? Enter { get; private set; }
    public string? Kind { get; private set; }
    public bool IsUp => card != null;
    /// <summary>Over the popup now, or still on its way out (the page waits for it to be gone).</summary>
    public bool Covers => Visibility == Visibility.Visible;
    /// <summary>The last dialog has gone (its way out done).</summary>
    public event Action? Gone;

    public AltaLayer()
    {
        Visibility = Visibility.Collapsed;
        smoke.SetResourceReference(Border.BackgroundProperty, B.Smoke);
        smoke.MouseLeftButtonDown += (_, e) => e.Handled = true;
        Children.Add(smoke);
        Panel.SetZIndex(this, 100);
    }

    /// <summary>A dialog shown (in place of the one up, if any: animated in only when none was).</summary>
    public void Show(string kind, Border dialog, Action? escape, Action? enter)
    {
        var was = card != null;
        if (card != null) Children.Remove(card);
        card = dialog;
        Kind = kind;
        Escape = escape;
        Enter = enter;
        Children.Add(dialog);
        Visibility = Visibility.Visible;
        if (!was)
        {
            Motion.FadeIn(smoke, Motion.Normal);
            Motion.Pop(dialog);
        }
        Support.Shot.Moved();
    }

    public void Hide()
    {
        if (card == null) return;
        var gone = card;
        card = null;
        Kind = null;
        Escape = null;
        Enter = null;
        Motion.FadeOut(smoke, () => { }, Motion.Normal);
        Motion.Unpop(gone, () =>
        {
            Children.Remove(gone);
            if (card != null) return;
            Visibility = Visibility.Collapsed;
            Gone?.Invoke();
        });
    }
}
