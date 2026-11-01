using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Kit;

/// <summary>
/// (the UI review's W9) One heading for every popup — Knewton Alta, Macmillan, ALEKS, a quiz, feedback, the rubric (a
/// tool's, ToolHeading, is laid out the same) — as Windows 11 heads a window's content: on the left, a way back where
/// there is one, the popup's tile, and its title over a line about it; on the right, what it adds (the tools pinned to
/// the title bar, a clock, Ring | Grid), then its icon buttons, each with its tooltip (Simpl look, Reload, Open in a
/// window…), and a single × at the far right. Each popup had its own set before: "× Close", "Close", "Done", a bare ×,
/// a switch and a "Browser" button. The popup is moved by it, a press on it that is not on a control.
/// </summary>
public sealed class PopupHeader : Grid
{
    private readonly StackPanel extra = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl lead = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly ContentControl tile = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The popup's ×: Escape's too, and Alt+F4's (its tooltip says what closing keeps).</summary>
    public Button Close { get; }

    /// <summary>The heading: <paramref name="title"/> over <paramref name="subtitle"/> (the popup's own, kept up to date by
    /// it; a line with no words takes no room), <paramref name="tileContent"/> before them, and × closing it as
    /// <paramref name="close"/> does — its tooltip <paramref name="closeTip"/>.</summary>
    public PopupHeader(TextBlock title, TextBlock? subtitle, Action close, string closeTip, UIElement? tileContent = null)
    {
        K.Style(title, Ty.Subtitle);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.TextWrapping = TextWrapping.NoWrap;
        // (Narrator reads it as the popup's heading)
        System.Windows.Automation.AutomationProperties.SetHeadingLevel(title, System.Windows.Automation.AutomationHeadingLevel.Level1);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(title);
        if (subtitle != null)
        {
            K.Style(subtitle, Ty.Body);
            subtitle.Fg(B.Text2);
            subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            subtitle.TextWrapping = TextWrapping.NoWrap;
            words.Children.Add(subtitle);
        }
        tile.Content = tileContent;
        tile.Visibility = tileContent == null ? Visibility.Collapsed : Visibility.Visible;
        Close = Icon(SymbolRegular.Dismiss24, closeTip, close);
        foreach (var w in new[] { "auto", "auto", "*", "auto", "auto", "auto" }) ColumnDefinitions.Add(new ColumnDefinition { Width = K.Length(w) });
        Children.Add(lead.Col(0));
        Children.Add(tile.Margin(0, 0, 12, 0).Col(1));
        Children.Add(words.Col(2));
        Children.Add(extra.Margin(12, 0, 0, 0).Col(3));
        Children.Add(buttons.Margin(4, 0, 0, 0).Col(4));
        Children.Add(Close.Margin(4, 0, 0, 0).Col(5));
        Margin = new Thickness(20, 12, 12, 12);
        // (a press on it that is not on a control moves the popup: its empty room takes the press)
        Background = System.Windows.Media.Brushes.Transparent;
    }

    /// <summary>The popup's tile (a vendor's, a quiz's colour), changed.</summary>
    public UIElement? Tile
    {
        get => tile.Content as UIElement;
        set { tile.Content = value; tile.Visibility = value == null ? Visibility.Collapsed : Visibility.Visible; }
    }

    /// <summary>A way back at its far left (a quiz's feedback back to the quiz, a criterion back to the ring), shown or
    /// not by its own Visibility (taking no room while it is not).</summary>
    public PopupHeader Lead(FrameworkElement back)
    {
        back.Margin = new Thickness(0, 0, 8, 0);
        lead.Content = back;
        lead.Visibility = Visibility.Visible;
        return this;
    }

    /// <summary>What the popup adds left of its buttons (the tools pinned to the title bar, a clock, Ring | Grid).</summary>
    public PopupHeader Extra(params UIElement?[] parts)
    {
        foreach (var p in parts)
        {
            if (p == null) continue;
            if (p is FrameworkElement f) { f.VerticalAlignment = VerticalAlignment.Center; if (extra.Children.Count > 0 && f.Margin == default) f.Margin = new Thickness(8, 0, 0, 0); }
            extra.Children.Add(p);
        }
        return this;
    }

    /// <summary>Its icon buttons, in this order, before the × (each made with <see cref="Icon"/> or <see cref="Toggle"/>).</summary>
    public PopupHeader Buttons(params UIElement?[] parts)
    {
        foreach (var p in parts) if (p != null) buttons.Children.Add(p);
        return this;
    }

    /// <summary>A popup heading's icon button: its symbol alone in a 36 square, its tooltip — what it does, its key — read
    /// out as its name too.</summary>
    public static Button Icon(SymbolRegular symbol, string tip, Action click)
    {
        var b = K.IconButton(symbol, click, tip, 16, 36);
        b.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetShowOnDisabled(b, true);
        return b;
    }

    /// <summary>A popup heading's toggle (an icon button that stays on: Simpl look): its symbol filled in the accent while
    /// on, its tooltip saying which it is and what a click does, Narrator told on or off.</summary>
    public static Button Toggle(SymbolRegular symbol, string name, Action click)
    {
        var b = Icon(symbol, name, click);
        b.Tag = symbol;
        Paint(b, false, name, name);
        return b;
    }

    /// <summary>A toggle (<see cref="Toggle"/>) shown on or off, with what its tooltip says now.</summary>
    public static void Paint(Button b, bool on, string name, string tip)
    {
        var symbol = b.Tag is SymbolRegular s ? s : SymbolRegular.Sparkle24;
        b.Content = K.Icon(symbol, 16, on ? B.AccentText : B.Text, filled: on);
        b.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        System.Windows.Automation.AutomationProperties.SetItemStatus(b, on ? "On" : "Off");
    }

    /// <summary>
    /// The one look control of every publisher's popup (Knewton Alta, Macmillan, ALEKS — and their pages in a tool's
    /// window): Simpl look, on — the page laid out by Simpl, its buttons the popup's own — or off, the page as the
    /// publisher draws it. Shown with <see cref="PaintLook"/>.
    /// </summary>
    public static Button Look(Action click) => Toggle(SymbolRegular.Sparkle24, "Simpl look", click);

    /// <summary>The look control (<see cref="Look"/>) shown on or off for <paramref name="vendor"/>'s page.</summary>
    public static void PaintLook(Button b, bool on, string vendor) =>
        Paint(b, on, on ? "Simpl look, on" : "Simpl look, off", on
            ? $"Simpl look is on: {vendor}’s page laid out by Simpl. Click to see it as {vendor} draws it."
            : $"Simpl look is off: {vendor}’s page as {vendor} draws it. Click to lay it out in Simpl look.");
}
