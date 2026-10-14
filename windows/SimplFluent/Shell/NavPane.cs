using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Shell;

/// <summary>An entry in the navigation pane: a place to go, its symbol and colour (Windows 11 Settings' colourful
/// icons), a count beside it, and — for a course — its sections under it.</summary>
public sealed class NavEntry
{
    public string Key = "";
    public string Title = "";
    public SymbolRegular Icon;
    public Color Tint;
    /// <summary>A course's own colour: a rounded square with its first letter, in place of a symbol.</summary>
    public Color? Square;
    public string? Letter;
    public int Badge;
    public Action? Go;
    public List<NavEntry>? Children;
    public bool Expanded;
    public Action<bool>? Toggle;
    /// <summary>A heading over entries (Courses, Groups), not a place.</summary>
    public bool Header;
    public bool Separator;
    /// <summary>The tour's name for this row, if it lights it.</summary>
    public TourSpot? Spot;
    public Action? Hover;
    /// <summary>Its screen got ready ahead of a click: read as a pointer rests on the row, built as it is pressed (Kit.Ahead).</summary>
    public Action<bool>? Prepare;
}

/// <summary>
/// The navigation pane's list, as Windows 11's Settings draws it: 36-tall rows rounded 4, a symbol at 16 and the name at
/// 48, the row under the pointer filled softly, the chosen one filled and marked by the accent's pill at its left — the
/// pill sliding from the row it was on to the one chosen, stretching on the way (WinUI's NavigationView), in the same
/// motion and time as the page coming in (Motion.Out, Motion.Page), moved and stretched by its transforms alone. A
/// course's sections fold out under it.
/// </summary>
public sealed class NavList : Grid
{
    private readonly Stack items = new() { Orientation = Orientation.Vertical, Spacing = 4 };
    private readonly Canvas overlay = new() { IsHitTestVisible = false };
    private readonly Border pill = new() { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5) };
    private readonly TranslateTransform pillMove = new();
    private readonly ScaleTransform pillStretch = new(1, 1);
    private readonly Dictionary<string, Button> rows = new();
    private string? selected;
    private double? pillAt;
    /// <summary>What the rows built last draw (<see cref="Signature"/>).</summary>
    private string? drawn;

    public NavList()
    {
        pill.SetResourceReference(Border.BackgroundProperty, B.Accent);
        pill.RenderTransformOrigin = new Point(0.5, 0.5);
        pill.RenderTransform = new TransformGroup { Children = { pillStretch, pillMove } };
        Canvas.SetTop(pill, 0);
        overlay.Children.Add(pill);
        pill.Visibility = Visibility.Hidden;
        Children.Add(items);
        Children.Add(overlay);
        LayoutUpdated += (_, _) => PlacePill(animate: false);
    }

    /// <summary>The rows built again from the entries, the one chosen marked.</summary>
    public void Set(IEnumerable<NavEntry> entries, string? chosen)
    {
        var list = entries as IReadOnlyList<NavEntry> ?? entries.ToList();
        Set(list, chosen, Signature(list));
    }

    /// <summary>
    /// The entries shown: the rows are built again only when something they draw changed (a count, a course, a course's
    /// sections folded out or in); otherwise they stay as they are — their tour marks and keyboard focus with them — and
    /// only the choice moves, the pill sliding to it.
    /// </summary>
    public void Show(IReadOnlyList<NavEntry> entries, string? chosen)
    {
        var sign = Signature(entries);
        if (sign == drawn) Select(chosen);
        else Set(entries, chosen, sign);
    }

    private void Set(IReadOnlyList<NavEntry> entries, string? chosen, string sign)
    {
        drawn = sign;
        items.Children.Clear();
        rows.Clear();
        foreach (var e in entries) Add(e, 0);
        Select(chosen);
    }

    /// <summary>Everything the entries' rows draw, in order, as words: the same words, the same rows. A course's sections
    /// count only while they are folded out (they are drawn only then).</summary>
    public static string Signature(IEnumerable<NavEntry> entries)
    {
        var s = new System.Text.StringBuilder();
        void Sign(NavEntry e, int depth)
        {
            s.Append(depth).Append(e.Separator ? 'S' : e.Header ? 'H' : 'R').Append('\u001f')
                .Append(e.Key).Append('\u001f').Append(e.Title).Append('\u001f')
                .Append((int)e.Icon).Append('\u001f').Append(e.Tint.ToString()).Append('\u001f')
                .Append(e.Square?.ToString()).Append('\u001f').Append(e.Letter).Append('\u001f')
                .Append(e.Badge).Append('\u001f').Append(e.Expanded ? '+' : '-').Append(e.Toggle != null ? 'T' : '_')
                .Append(e.Hover != null ? 'h' : '_').Append('\u001f').Append(e.Spot?.ToString()).Append('\u001e');
            if (e.Expanded && e.Children != null)
                foreach (var c in e.Children) Sign(c, depth + 1);
        }
        foreach (var e in entries) Sign(e, 0);
        return s.ToString();
    }

    private void Add(NavEntry e, int depth)
    {
        if (e.Separator)
        {
            items.Children.Add(K.Divider().Margin(16, 6, 16, 6));
            return;
        }
        if (e.Header)
        {
            items.Children.Add(K.Text(e.Title, Ty.BodyStrong).Margin(16, 14, 16, 4));
            return;
        }
        var row = Row(e, depth);
        rows[e.Key] = row;
        if (e.Spot is { } spot) row.Mark(spot);
        if (e.Hover is { } hover) row.MouseEnter += (_, _) => hover();
        if (e.Prepare is { } prepare) Ahead.On(row, prepare);
        items.Children.Add(row);
        if (e.Expanded && e.Children != null)
            foreach (var c in e.Children) Add(c, depth + 1);
    }

    private Button Row(NavEntry e, int depth)
    {
        FrameworkElement glyph;
        if (e.Square is { } sq)
        {
            // (its letter white only where white reads on the course's colour, else near-black: the course cards' rule)
            var letter = new TextBlock { Text = e.Letter ?? "", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = K.Brush(K.InkOn(sq)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFamily = K.SmallFace };
            glyph = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Background = K.Brush(sq), Child = letter, VerticalAlignment = VerticalAlignment.Center };
        }
        else glyph = K.Icon(e.Icon, 16, e.Tint, filled: true);
        var title = K.Text(e.Title, Ty.Body);
        var grid = K.Columns("auto, *, auto, auto", glyph.Col(0), title.Margin(14, 0, 8, 0).Col(1));
        if (e.Badge > 0)
        {
            var badge = new Border
            {
                MinWidth = 16, Height = 16, CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = e.Badge > 99 ? "99+" : e.Badge.ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            badge.SetResourceReference(Border.BackgroundProperty, B.Accent);
            ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, B.OnAccent);
            grid.Children.Add(badge.Col(2));
        }
        if (e.Toggle != null)
        {
            var chev = K.Icon(e.Expanded ? SymbolRegular.ChevronUp20 : SymbolRegular.ChevronDown20, 12, B.Text2);
            var t = K.Subtle(chev, () => e.Toggle(!e.Expanded), new Thickness(6), 4);
            t.Width = 28;
            t.Height = 28;
            t.Margin = new Thickness(4, 0, -6, 0);
            t.ToolTip = e.Expanded ? "Hide sections" : "Show sections";
            grid.Children.Add(t.Col(3));
        }
        var b = K.Subtle(grid, () => e.Go?.Invoke(), new Thickness(12 + depth * 28, 0, 8, 0));
        b.Height = 36;
        b.Margin = new Thickness(4, 0, 4, 0);
        b.ToolTip = e.Title.Length > 26 ? e.Title : null;
        System.Windows.Automation.AutomationProperties.SetName(b, e.Title);
        return b;
    }

    public void Select(string? key)
    {
        if (selected != null && rows.TryGetValue(selected, out var old)) old.Background = Brushes.Transparent;
        selected = key;
        if (key != null && rows.TryGetValue(key, out var row)) row.SetResourceReference(Control.BackgroundProperty, B.Subtle);
        PlacePill(animate: true);
    }

    private void PlacePill(bool animate)
    {
        if (selected == null || !rows.TryGetValue(selected, out var row) || !row.IsVisible)
        {
            pill.Visibility = Visibility.Hidden;
            pillAt = null;
            return;
        }
        Point p;
        try { p = row.TranslatePoint(new Point(0, 0), overlay); } catch { return; }
        var y = p.Y + (row.ActualHeight - 16) / 2;
        if (Math.Abs(Canvas.GetLeft(pill) - p.X) > 0.1 || double.IsNaN(Canvas.GetLeft(pill))) Canvas.SetLeft(pill, p.X);
        if (pillAt is { } from && Math.Abs(from - y) < 0.5 && pill.Visibility == Visibility.Visible) return;
        pill.Visibility = Visibility.Visible;
        if (animate && pillAt is { } start && Math.Abs(start - y) > 1 && Motion.On)
        {
            // (WinUI's: the pill stretches toward the row chosen, then settles into it — from wherever it is, if it
            // was on its way somewhere else)
            Motion.To(pillMove, TranslateTransform.YProperty, y, Motion.Page, Motion.Out);
            var stretch = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { Duration = Motion.Ms(Motion.Page) };
            stretch.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(Math.Min(2.5, 1 + Math.Abs(y - start) / 48), System.Windows.Media.Animation.KeyTime.FromPercent(0.35)) { EasingFunction = Motion.Out });
            stretch.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1, System.Windows.Media.Animation.KeyTime.FromPercent(1)) { EasingFunction = Motion.Out });
            pillStretch.BeginAnimation(ScaleTransform.ScaleYProperty, stretch);
        }
        else
        {
            pillMove.BeginAnimation(TranslateTransform.YProperty, null);
            pillStretch.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            pillMove.Y = y;
        }
        pillAt = y;
    }
}
