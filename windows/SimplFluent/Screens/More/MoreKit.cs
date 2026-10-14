using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Simpl.Screens;

/// <summary>
/// The pieces Search, External Tools, Tools and a discussion share (the Mac's ContentUnavailableView, its notes with a
/// symbol, a row's context menu, Copy Link), in Windows 11's terms.
/// </summary>
internal static class MoreKit
{
    /// <summary>The Mac's ContentUnavailableView: a quiet symbol, what is missing, and why — in the middle of the page.</summary>
    public static FrameworkElement Unavailable(SymbolRegular icon, string title, string? description, double minHeight = 320)
    {
        var v = K.V(12, K.Icon(icon, 44, B.Text3), K.Text(title, Ty.Subtitle).Center());
        if (!string.IsNullOrEmpty(description)) v.Children.Add(K.Wrapped(description!, Ty.Body, B.Text2).Center());
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.MaxWidth = 440;
        return new Grid { MinHeight = minHeight, Children = { v } };
    }

    /// <summary>A line with its symbol, in the secondary colour (a note over a list: locked, reply first).</summary>
    public static FrameworkElement Note(SymbolRegular icon, string text)
    {
        var g = K.Columns("auto, *", K.Icon(icon, 16, B.Text2).Margin(0, 2, 10, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0),
            K.Wrapped(text, Ty.Body, B.Text2).Col(1));
        return g;
    }

    /// <summary>A context menu of Windows 11's, each item with its symbol.</summary>
    public static ContextMenu Menu(params (string Text, SymbolRegular Icon, Action Act)?[] items)
    {
        var menu = new ContextMenu { MinWidth = 200 };
        foreach (var it in items)
        {
            if (it is not { } i) { menu.Items.Add(new Separator()); continue; }
            var mi = new MenuItem { Header = i.Text, Icon = new SymbolIcon { Symbol = i.Icon } };
            mi.Click += (_, _) => i.Act();
            menu.Items.Add(mi);
        }
        return menu;
    }

    /// <summary>The menu opened under a button (a toolbar's More).</summary>
    public static void Open(ContextMenu menu, FrameworkElement at)
    {
        menu.PlacementTarget = at;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Copy Link, Copy Text: onto the clipboard (another app holding it a moment: let go).</summary>
    public static void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); } catch { /* the clipboard was busy */ }
    }

    /// <summary>An element let go of by the page it was on, to be put on the page built again (a reply being written).</summary>
    public static void Detach(FrameworkElement e)
    {
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case Decorator d: d.Child = null; break;
            case ContentControl c: c.Content = null; break;
        }
    }
}

/// <summary>
/// SwiftUI's adaptive grid (LazyVGrid with GridItem.adaptive): as many columns as fit at their narrowest, each as wide
/// as the room shares out (no wider than <see cref="MaxColumn"/>, the grid then set at the left); a row as tall as its
/// tallest, each cell its own height at its top unless <see cref="Stretch"/>.
/// </summary>
internal sealed class MoreGrid : Panel
{
    public double MinColumn { get; set; } = 300;
    public double MaxColumn { get; set; } = double.PositiveInfinity;
    public double Spacing { get; set; } = 16;
    public double RowSpacing { get; set; } = 16;
    public bool Stretch { get; set; }

    /// <summary>Rows kept full: as many columns as fit, less any that would leave a last row short (four tiles two
    /// across rather than three and one alone).</summary>
    public bool Even { get; set; }

    public MoreGrid(double minColumn, double spacing, IEnumerable<UIElement> kids, double maxColumn = double.PositiveInfinity, bool stretch = false)
    {
        MinColumn = minColumn;
        MaxColumn = maxColumn;
        Spacing = spacing;
        RowSpacing = spacing;
        Stretch = stretch;
        foreach (var k in kids) Children.Add(k);
    }

    private (int Cols, double Width) Columns(double width)
    {
        if (double.IsInfinity(width) || width <= 0) width = MinColumn;
        var cols = Math.Max(1, (int)Math.Floor((width + Spacing) / (MinColumn + Spacing)));
        var n = InternalChildren.Count;
        if (Even && n > 0)
        {
            cols = Math.Min(cols, n);
            while (cols > 1 && n % cols != 0) cols--;
        }
        var w = Math.Min(MaxColumn, (width - Spacing * (cols - 1)) / cols);
        return (cols, Math.Max(0, w));
    }

    protected override Size MeasureOverride(Size available)
    {
        var (cols, w) = Columns(available.Width);
        double height = 0, row = 0;
        var n = 0;
        foreach (UIElement c in InternalChildren)
        {
            c.Measure(new Size(w, double.PositiveInfinity));
            row = Math.Max(row, c.DesiredSize.Height);
            if (++n % cols == 0) { height += row + RowSpacing; row = 0; }
        }
        if (n % cols != 0) height += row; else if (n > 0) height -= RowSpacing;
        var used = n == 0 ? 0 : Math.Min(n, cols) * w + (Math.Min(n, cols) - 1) * Spacing;
        return new Size(double.IsInfinity(available.Width) ? used : available.Width, Math.Max(0, height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (cols, w) = Columns(final.Width);
        var kids = InternalChildren.Cast<UIElement>().ToList();
        double y = 0;
        for (var start = 0; start < kids.Count; start += cols)
        {
            var line = kids.Skip(start).Take(cols).ToList();
            var h = line.Max(k => k.DesiredSize.Height);
            for (var i = 0; i < line.Count; i++)
                line[i].Arrange(new Rect(i * (w + Spacing), y, w, Stretch ? h : line[i].DesiredSize.Height));
            y += h + RowSpacing;
        }
        return final;
    }
}
