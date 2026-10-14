using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// The pieces the biology tools draw their pages with (spec §9), in the chemistry tools' look (the Mac's BioUI.swift):
// the three-column layout, a side column's heading, the card in the right column (a title with its swatch, short
// labelled rows, actions), a facts list, a legend, a sequence in groups of ten with its 5′ and 3′ ends, the quick "Open"
// a pin shows for a tool without a quick version, and the placeholder a tool shows until it is built.

public static class BioUI
{
    /// <summary>The chemistry builders' layout (ChemLayout, the Mac's PTWorkLayout): controls, the drawing and the facts
    /// side by side from 1100 wide; below that the drawing over the two, as tall as the page shows (460 to 640) — on the
    /// Tools page, beside the navigation pane, a 1366 window is narrower than that.</summary>
    public static FrameworkElement Work(FrameworkElement left, FrameworkElement centre, FrameworkElement right) => new ChemLayout(left, centre, right, tall: true);

    /// <summary>A side column's heading, as the molecule builder's ("Preset", "View").</summary>
    public static TextBlock Heading(string text)
    {
        var t = K.Text(text, Ty.BodyStrong);
        System.Windows.Automation.AutomationProperties.SetHeadingLevel(t, System.Windows.Automation.AutomationHeadingLevel.Level3);
        return t;
    }

    /// <summary>A short line that says what something does.</summary>
    public static TextBlock Caption(string text) => K.Wrapped(text, Ty.Caption, B.Text2);

    /// <summary>The right column's card (spec §9 Cards): its title (with a colour swatch) and a line under it, then its
    /// rows and actions; text wraps, nothing scrolls sideways.</summary>
    public static Border Card(string title, Color? swatch, string? subtitle, params UIElement?[] content)
    {
        var words = K.V(2, K.Wrapped(title, Ty.BodyLarge).Strong());
        if (subtitle != null) words.Children.Add(K.Wrapped(subtitle, Ty.Body, B.Text2));
        UIElement head = words;
        if (swatch is { } c)
        {
            var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), Background = K.Brush(c), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 10, 0) };
            head = K.Columns("auto, *", dot.Col(0), words.Col(1));
        }
        var v = K.V(12, head);
        foreach (var e in content) if (e != null) v.Children.Add(e);
        return K.Card(v, new Thickness(16));
    }

    /// <summary>A labelled row of a card ("Function", "Structure", "Common mix-up"): its label small over its text.</summary>
    public static FrameworkElement FactRow(string label, string text, Color? tint = null)
    {
        var l = K.Text(label, Ty.Caption, B.Text2).Strong();
        if (tint is { } t) l.Foreground = K.Brush(K.Darkened(t));
        var v = K.V(3, l, K.Wrapped(text, Ty.Body));
        System.Windows.Automation.AutomationProperties.SetName(v, $"{label}: {text}");
        return v;
    }

    /// <summary>A facts list: a row each, its name at the left and its value at the right in its colour, hairlines between
    /// (the molecule builder's facts card).</summary>
    public static Border Facts(IEnumerable<(string Name, string Value, Color? Tone)> rows) => ChemKit.Facts(rows);

    /// <summary>A legend: a swatch and its words for each colour used (never colour alone).</summary>
    public static FrameworkElement Legend(IEnumerable<(Color Color, string Text)> items)
    {
        var v = K.V(6);
        foreach (var (c, text) in items)
        {
            var dot = new Ellipse { Width = 10, Height = 10, Fill = K.Brush(c), StrokeThickness = 0.5, VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shape.StrokeProperty, B.StrongStroke);
            v.Children.Add(K.H(8, dot, K.Text(text, Ty.Caption)));
        }
        return v;
    }

    /// <summary>The monospaced face sequences are written in (Cascadia Mono, as the app's code blocks).</summary>
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    /// <summary>
    /// A nucleic acid written as §3.7 has it: monospaced, in groups of ten, each line's first position numbered, the 5′ and
    /// 3′ ends labelled; each base in its colour if asked (its letter always shown). <paramref name="fivePrimeFirst"/> off:
    /// a strand written 3′→5′ under another.
    /// </summary>
    public static FrameworkElement SequenceText(string sequence, bool fivePrimeFirst = true, bool coloured = true, int groupsPerLine = 6)
    {
        var lines = BioSeq.Lines(sequence, perLine: groupsPerLine);
        var v = K.V(4);
        for (var i = 0; i < lines.Count; i++)
        {
            var row = new TextBlock { FontFamily = Mono, FontSize = 14, TextWrapping = TextWrapping.NoWrap };
            row.SetResourceReference(TextBlock.ForegroundProperty, B.Text);
            var number = new Run($"{lines[i].Start,4} ") { FontSize = 11 };
            number.SetResourceReference(TextElement.ForegroundProperty, B.Text2);
            row.Inlines.Add(number);
            if (i == 0) row.Inlines.Add(End(fivePrimeFirst ? "5′ " : "3′ "));
            for (var g = 0; g < lines[i].Groups.Count; g++)
            {
                if (g > 0) row.Inlines.Add(new Run(" "));
                foreach (var ch in lines[i].Groups[g])
                {
                    var r = new Run(ch.ToString()) { FontWeight = coloured ? FontWeights.SemiBold : FontWeights.Normal };
                    if (coloured) r.Foreground = K.Brush(BioColors.Base(ch));
                    row.Inlines.Add(r);
                }
            }
            if (i == lines.Count - 1) row.Inlines.Add(End(fivePrimeFirst ? " 3′" : " 5′"));
            v.Children.Add(row);
        }
        System.Windows.Automation.AutomationProperties.SetName(v, $"{(fivePrimeFirst ? "5 prime to 3 prime" : "3 prime to 5 prime")}: {string.Join(" ", sequence.Select(c => c.ToString()))}");
        return v;
    }

    private static Run End(string s)
    {
        var r = new Run(s) { FontSize = 11, FontWeight = FontWeights.SemiBold };
        r.SetResourceReference(TextElement.ForegroundProperty, B.Text2);
        return r;
    }
}

/// <summary>A pin's flyout for a biology tool without a quick version (the Mac's BioQuickOpen): its tile, its line, and Open.</summary>
public sealed class BioQuickOpen : ContentControl
{
    public BioQuickOpen(ToolContext c)
    {
        Focusable = false;
        var kind = BioTools.Kind(c.Key);
        var name = kind?.Name ?? c.Key;
        var words = K.V(3, K.Text(name, Ty.BodyStrong), K.Wrapped(kind?.Note ?? "", Ty.Body, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var top = K.Columns("auto, *", (kind != null ? K.IconTile(kind.Symbol, kind.Color, 40) : new Border()).Margin(0, 0, 12, 0).Col(0), words.Col(1));
        var open = K.Button($"Open {name}", () => c.OpenFull(null, null), accent: true);
        open.HorizontalAlignment = HorizontalAlignment.Stretch;
        open.HorizontalContentAlignment = HorizontalAlignment.Center;
        open.MinHeight = 36;
        open.IsDefault = true;
        open.ToolTip = $"Open {name} full size (Enter)";
        Content = K.V(12, top, open);
    }
}

/// <summary>What a biology tool shows until it is built (the Mac's BioToolPlaceholder): its tile, its name, its line, the
/// page chosen in its header, and what the launch asked of it, if anything.</summary>
public sealed class BioToolPlaceholder : Grid
{
    private readonly string key;
    private readonly ContentControl pageHost = new() { Focusable = false, HorizontalAlignment = HorizontalAlignment.Center };

    public BioToolPlaceholder(ToolContext c, BioLaunchState? state)
    {
        key = c.Key;
        var kind = BioTools.Kind(key);
        var v = K.V(14);
        if (kind != null) v.Children.Add(K.IconTile(kind.Symbol, kind.Color, 64).Align(HorizontalAlignment.Center));
        v.Children.Add(K.Text(kind?.Name ?? key, Ty.Subtitle).Center());
        v.Children.Add(K.Wrapped(kind?.Note ?? "", Ty.Body, B.Text2).Center());
        v.Children.Add(pageHost);
        if (state is { Raw.Length: > 0 }) v.Children.Add(K.Wrapped($"Opened with “{state.Raw}”", Ty.Caption, B.Text3).Center());
        var card = K.Card(v, new Thickness(40), 20);
        card.MaxWidth = 520;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.Margin = new Thickness(40, 0, 40, 40);
        Children.Add(card);
        System.Windows.Automation.AutomationProperties.SetName(this, $"{kind?.Name ?? key}. {kind?.Note}");
        Draw();
        void Changed(string k) { if (k == key) Draw(); }
        Loaded += (_, _) => { BioNav.Changed -= Changed; BioNav.Changed += Changed; Draw(); };
        Unloaded += (_, _) => BioNav.Changed -= Changed;
    }

    private void Draw()
    {
        var pages = BioTools.Pages(key);
        var page = pages.FirstOrDefault(p => p.Id == BioNav.Page(key));
        if (page == null || pages.Count < 2) { pageHost.Content = null; return; }
        var tint = BioColors.Tool(key);
        var chip = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 6), Background = K.Brush(K.WithAlpha(tint, 0.18)),
            Child = K.H(6, K.Icon(page.Symbol, 14, B.Text), K.Text(page.Title, Ty.BodyStrong)),
        };
        pageHost.Content = chip;
    }
}
