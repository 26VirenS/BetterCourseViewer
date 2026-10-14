using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Shell;

/// <summary>An entry in the navigation pane: a place to go, its symbol and colour (Windows 11 Settings' colourful
/// icons), a count beside it saying what it counts, and — for a course — its sections under it (for Groups, the groups).</summary>
public sealed class NavEntry
{
    public string Key = "";
    public string Title = "";
    public SymbolRegular Icon;
    public Color Tint;
    /// <summary>A course's own colour: a rounded square with its first letter, in place of a symbol. With no letter, the
    /// square holds <see cref="Picture"/>, else the symbol (a tool of the school's own on the colour of Canvas's nav).</summary>
    public Color? Square;
    public string? Letter;
    /// <summary>The school's own picture for it, on its <see cref="Square"/> (a tool's icon in Canvas's nav).</summary>
    public ImageSource? Picture;
    /// <summary>It opens in a Simpl window of its own (a tool, another site): the window's mark at the row's end, as a
    /// course's More and External Tools have it.</summary>
    public bool OwnWindow;
    public int Badge;
    /// <summary>What the count counts, in words ("2 unread announcements"): its tooltip, and what a screen reader says.</summary>
    public string? BadgeLabel;
    /// <summary>The row's tooltip ("Settings (Ctrl+,)"); a long name is its own otherwise.</summary>
    public string? Tip;
    /// <summary>What folds out under it, for its chevron's tooltip: "sections", "groups".</summary>
    public string Folds = "sections";
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
    /// <summary>The place in a window of its own (the row's menu: Open in new window; Shift+click), or null.</summary>
    public Action? OpenApart;
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
                .Append(e.Picture?.GetHashCode()).Append(e.OwnWindow ? 'W' : '_').Append('\u001f')
                .Append(e.Badge).Append('\u001f').Append(e.BadgeLabel).Append('\u001f').Append(e.Tip).Append('\u001f').Append(e.Folds).Append('\u001f').Append(e.Expanded ? '+' : '-').Append(e.Toggle != null ? 'T' : '_')
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
            FrameworkElement inside = e.Letter != null
                ? new TextBlock { Text = e.Letter, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = K.Brush(K.InkOn(sq)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFamily = K.SmallFace }
                : e.Picture is { } picture ? Picture(picture)
                : K.Icon(e.Icon, 12, K.InkOn(sq), filled: true);
            inside.HorizontalAlignment = HorizontalAlignment.Center;
            inside.VerticalAlignment = VerticalAlignment.Center;
            glyph = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Background = K.Brush(sq), Child = inside, VerticalAlignment = VerticalAlignment.Center };
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
            badge.ToolTip = e.BadgeLabel;
            grid.Children.Add(badge.Col(2));
        }
        if (e.OwnWindow) grid.Children.Add(Screens.CourseKit.OwnWindowMark(12, B.Text3).Col(2));
        if (e.Toggle != null)
        {
            var chev = K.Icon(e.Expanded ? SymbolRegular.ChevronUp20 : SymbolRegular.ChevronDown20, 12, B.Text2);
            var t = K.Subtle(chev, () => e.Toggle(!e.Expanded), new Thickness(6), 4);
            t.Width = 28;
            t.Height = 28;
            t.Margin = new Thickness(4, 0, -6, 0);
            t.ToolTip = e.Expanded ? $"Hide {e.Folds}" : $"Show {e.Folds}";
            System.Windows.Automation.AutomationProperties.SetName(t, (e.Expanded ? $"Hide {e.Folds} of " : $"Show {e.Folds} of ") + e.Title);
            grid.Children.Add(t.Col(3));
        }
        // (Shift+click, as Windows opens a link in a window of its own)
        var b = K.Subtle(grid, () =>
        {
            if (e.OpenApart is { } apart && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Shift) apart();
            else e.Go?.Invoke();
        }, new Thickness(12 + depth * 28, 0, 8, 0));
        b.Height = 36;
        b.Margin = new Thickness(4, 0, 4, 0);
        b.ToolTip = e.Tip ?? (e.Title.Length > 26 ? e.Title : e.Badge > 0 ? e.BadgeLabel : null);
        System.Windows.Automation.AutomationProperties.SetName(b, e.Badge > 0 && e.BadgeLabel != null ? $"{e.Title}, {e.BadgeLabel}" : e.Title);
        if (e.OpenApart is { } open)
        {
            var menu = new ContextMenu();
            var item = new System.Windows.Controls.MenuItem { Header = "Open in new window", Icon = new SymbolIcon { Symbol = SymbolRegular.WindowNew24 }, InputGestureText = "Shift+click" };
            item.Click += (_, _) => open();
            menu.Items.Add(item);
            b.ContextMenu = menu;
        }
        return b;
    }

    /// <summary>The school's own picture on a row's square: 13 across, made smaller smoothly from the 64 it comes at.</summary>
    private static FrameworkElement Picture(ImageSource source)
    {
        var image = new System.Windows.Controls.Image { Source = source, Width = 13, Height = 13, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    /// <summary>
    /// The row chosen marked. A course's section whose row is not drawn (its sections not read yet, or folded away)
    /// marks its course instead, so the pane always says where the page is; and a row newly chosen is scrolled into
    /// sight (a course's Grades far down its sections).
    /// </summary>
    public void Select(string? key)
    {
        if (selected != null && rows.TryGetValue(selected, out var old)) old.Background = Brushes.Transparent;
        if (key != null && !rows.ContainsKey(key) && key.IndexOf(':') is > 0 and var colon && rows.ContainsKey(key[..colon])) key = key[..colon];
        selected = key;
        if (key != null && rows.TryGetValue(key, out var row))
        {
            row.SetResourceReference(Control.BackgroundProperty, B.Subtle);
            if (key != shownKey)
            {
                shownKey = key;
                // (once the pane has settled — its foot and its rows laid out — and with a little room round it, so the row
                //  is never left half under the foot; only when the choice moved, so a pane scrolled by hand stays put. The
                //  row is looked up then, not now: the rows may be built again meanwhile, as counts and sections arrive)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                {
                    if (selected != null && rows.TryGetValue(selected, out var shown) && shown.IsVisible)
                        shown.BringIntoView(new Rect(0, -8, shown.ActualWidth, shown.ActualHeight + 16));
                });
            }
        }
        PlacePill(animate: true);
    }

    /// <summary>The row last scrolled into sight for its choice.</summary>
    private string? shownKey;

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
