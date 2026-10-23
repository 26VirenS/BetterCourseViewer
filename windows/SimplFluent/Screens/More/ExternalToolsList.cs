using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using Popup = System.Windows.Controls.Primitives.Popup;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// A tool's list, its card pressed: every place the tool is used, to choose the one to open — a flyout hung under its
/// card, as a pinned tool's hangs under its pin (Windows 11's flyout surface: its fill, its hairline, rounded 8, on a
/// soft shadow drawn by a copy of it behind, so its words keep their crisp ClearType), rising into place as it fades in.
/// At its head the tool and how many places it is in; a filter once there are more than eight; then each place — by
/// course, under the course's name, when it is in more than one — its title, when it is due, where the work stands, the
/// next one still to do marked Next. ↑ and ↓ move between them, Enter opens one, Escape or a press anywhere else folds it
/// away, and opening one folds it away first. It goes too when the page under it scrolls, and when Simpl is left for
/// another app (a popup would otherwise float over it); it follows its card as the window moves or changes size.
/// </summary>
public sealed partial class ExternalToolsScreen
{
    private const double ListWidth = 400, ListHeight = 460;
    /// <summary>Room round the list for its shadow, inside the popup's own window (the gap under the card is the top).</summary>
    private const double Side = 12, Above = 4, Below = 20;

    private Popup? list;
    private string? listId;
    private FrameworkElement? listCard;
    private Grid? listSurface;
    private TextBox? listFilter;
    /// <summary>Where the rows go (the list's scrolling stack).</summary>
    private Stack? rowsHost;
    /// <summary>The rows shown, in order, each with what it opens: ↑ and ↓ move between them, Enter opens one.</summary>
    private readonly List<(Button Row, Action Open)> listRows = new();
    private Window? listWindow;
    private ScrollViewer? listPage;

    /// <summary>A card pressed: its list, or — its list open already — folded away.</summary>
    private void ToggleList(string id, FrameworkElement card)
    {
        if (listId == id) { CloseList(); return; }
        if (ToolOf(id) is { } t) OpenList(t, card);
    }

    /// <summary>A press anywhere in the window outside the list folds it away (the press still does what it does); a
    /// press on its own card folds it by the card's click.</summary>
    public override void Pressed(MouseButtonEventArgs e)
    {
        if (list == null) return;
        if (listSurface?.IsMouseOver == true || listCard?.IsMouseOver == true) return;
        CloseList();
    }

    /// <summary>Escape: the list folded away, the keyboard back on its card.</summary>
    public override bool Escape()
    {
        if (list == null) return false;
        CloseList(returnFocus: true);
        return true;
    }

    /// <summary>
    /// The list of a tool's places, hung from its card: centred under it, 400 wide, as tall as its rows up to 460 (then
    /// they scroll) — above the card where there is no room below (WPF's own fallback). The filter takes the keyboard when
    /// there is one, else the first row. <paramref name="keep"/>: the filter's words, when it is hung again.
    /// </summary>
    private void OpenList(Tool t, FrameworkElement card, string keep = "", bool animate = true)
    {
        CloseList();
        var places = t.Courses.Where(c => c.Items.Count > 0).ToList();
        if (places.Count == 0) return;
        var count = places.Sum(c => c.Items.Count);

        // its head: the tool's letter on its colour, its name, and how many places in how many courses
        var tile = Monogram(t, 24);
        tile.VerticalAlignment = VerticalAlignment.Center;
        var where = places.Count == 1 ? places[0].Name : Simpl.Tools.ToolKit.Plural(places.Count, "course");
        var words = K.V(0, K.Text(t.Name, Ty.BodyStrong), K.Text($"{Places(t)} in {where}", Ty.Caption, B.Text2));
        var head = K.Columns("auto, *", tile.Margin(0, 0, 10, 0).Col(0), words.Col(1));
        head.Margin = new Thickness(16, 14, 16, 12);

        if (count > 8)
        {
            listFilter = new TextBox
            {
                PlaceholderText = Noun(t) switch { "assignment" => "Filter assignments", "module item" => "Filter module items", _ => "Filter" },
                Icon = new SymbolIcon { Symbol = SymbolRegular.Filter24 }, IconPlacement = ElementPlacement.Left,
                MinHeight = 32, Margin = new Thickness(12, 0, 12, 8), Text = keep,
            };
            listFilter.TextChanged += (_, _) => FillList(t);
        }
        var rows = new Stack { Orientation = Orientation.Vertical, Spacing = 2 };
        var scroller = new ScrollViewer
        {
            Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, Margin = new Thickness(6, 0, 4, 6), Padding = new Thickness(0, 0, 2, 0),
        };
        var content = K.Rows("auto, auto, *", head.Row(0), listFilter?.Row(1), scroller.Row(2));

        var shade = new Border { CornerRadius = new CornerRadius(8), Effect = K.Shadow(24, 6, 0.22) }.Bg(B.Flyout);
        var face = new Border { CornerRadius = new CornerRadius(8), Child = content, SnapsToDevicePixels = true }.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        var surface = new Grid { Width = ListWidth, MaxHeight = ListHeight, Margin = new Thickness(Side, Above, Side, Below), Children = { shade, face } };
        surface.SetResourceReference(TextElement.ForegroundProperty, B.Text);
        KeyboardNavigation.SetTabNavigation(surface, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(surface, $"{t.Name}: {Places(t)}");
        surface.PreviewKeyDown += OnListKey;
        // (a popup's events go on through its card: a press on the list's own ground stops here — it does not press the
        // card under it, which would fold the list — and the wheel over the list is the list's, never the page's)
        surface.MouseLeftButtonDown += (_, e) => e.Handled = true;
        surface.MouseWheel += (_, e) => e.Handled = true;
        // (the keyboard taken elsewhere in Simpl — the search box, Ctrl+F — folds it; not the card's own press, whose
        // click does, nor Simpl merely left for another app)
        surface.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            if ((bool)e.NewValue || Shot.Asked) return;
            surface.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (listSurface == surface && !surface.IsKeyboardFocusWithin && Keyboard.FocusedElement is DependencyObject now && !ReferenceEquals(now, listCard))
                    CloseList();
            });
        };

        list = new Popup
        {
            Child = surface, AllowsTransparency = true, StaysOpen = true, Focusable = false, PopupAnimation = PopupAnimation.None,
            PlacementTarget = card, Placement = PlacementMode.Bottom, HorizontalOffset = (card.ActualWidth - (ListWidth + 2 * Side)) / 2,
        };
        listId = t.Id;
        listCard = card;
        listSurface = surface;
        rowsHost = rows;
        FillList(t);

        listWindow = Window.GetWindow(card);
        if (listWindow != null)
        {
            listWindow.LocationChanged += OnListWindowMoved;
            listWindow.SizeChanged += OnListWindowMoved;
        }
        if (Application.Current != null) Application.Current.Deactivated += OnAppLeft;
        listPage = Ancestor<ScrollViewer>(card);
        if (listPage != null) listPage.ScrollChanged += OnPageScrolled;

        list.IsOpen = true;
        // (its card's "Choose which …" is said: it would only cover the list)
        ToolTipService.SetIsEnabled(card, false);
        if (chevrons.TryGetValue(t.Id, out var turn))
        {
            if (animate) Motion.To(turn, RotateTransform.AngleProperty, 180, Motion.Normal, Motion.Decelerate);
            else { turn.BeginAnimation(RotateTransform.AngleProperty, null); turn.Angle = 180; }
        }
        if (animate && Motion.On)
        {
            // (Windows 11's flyout arriving: up the last few pixels into place as it fades in, decelerating)
            var rise = new TranslateTransform(0, 8);
            surface.RenderTransform = rise;
            surface.Opacity = 0;
            Motion.To(rise, TranslateTransform.YProperty, 0, Motion.Normal, Motion.Decelerate);
            Motion.To(surface, UIElement.OpacityProperty, 1, Motion.Normal, Motion.Decelerate);
        }
        surface.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (listSurface != surface) return;
            if (listFilter != null) { listFilter.Focus(); listFilter.CaretIndex = listFilter.Text.Length; }
            else if (listRows.Count > 0) listRows[0].Row.Focus();
        });
        Shot.Moved();
    }

    /// <summary>
    /// The list's rows for the filter's words (all of them with none): by course when the tool is in more than one — the
    /// course's dot and name over its own — each place a row; the one the card names Next marked so.
    /// </summary>
    private void FillList(Tool t)
    {
        if (rowsHost is not { } rows) return;
        rows.Children.Clear();
        listRows.Clear();
        var q = (listFilter?.Text ?? "").Trim();
        var places = t.Courses.Where(c => c.Items.Count > 0).ToList();
        var grouped = places.Count > 1;
        // (the soonest still to do — as the card says it — else the first still to do as listed)
        var next = t.Next is { } n ? places.FirstOrDefault(c => c.Name == n.Course)?.Items.FirstOrDefault(i => i.Upcoming && i.Title == n.Title) : null;
        next ??= places.SelectMany(c => c.Items).FirstOrDefault(i => i.Upcoming);
        bool Has(string? s) => s?.Contains(q, StringComparison.CurrentCultureIgnoreCase) == true;
        var any = false;
        foreach (var c in places)
        {
            var items = q.Length == 0 ? c.Items : c.Items.Where(i => Has(i.Title) || Has(i.Sub) || Has(i.Status?.Word) || Has(c.Name)).ToList();
            if (items.Count == 0) continue;
            if (grouped) rows.Children.Add(GroupHead(c, first: !any));
            foreach (var it in items) rows.Children.Add(ListRow(t, c, it, ReferenceEquals(it, next)));
            any = true;
        }
        if (!any) rows.Children.Add(K.Wrapped($"Nothing matches “{q}”.", Ty.Body, B.Text2).Margin(10, 6, 10, 12));
    }

    /// <summary>A course's name over its places in the list: its dot and its name, small and quiet.</summary>
    private static FrameworkElement GroupHead(Course c, bool first)
    {
        var name = K.Text(c.Name, Ty.Caption, B.Text2);
        name.FontWeight = FontWeights.Medium;
        var h = K.Columns("auto, *", K.Dot(K.Hex(c.Color), 7).Margin(0, 0, 8, 0).Col(0), name.Col(1));
        h.Margin = new Thickness(10, first ? 2 : 12, 10, 4);
        return h;
    }

    /// <summary>A place in the list: its title (two lines at most), the line under it (when it is due and its points, or
    /// its module), Next before that line for the next one to do, and where the work stands at its end, as every list of
    /// work shows it. A press opens it.</summary>
    private Button ListRow(Tool t, Course c, Item it, bool next)
    {
        var title = K.Wrapped(it.Short is { Length: > 0 } s0 ? s0 : Short(t, it.Title), Ty.Body, B.Text, 2);
        var sub = K.Text(it.Sub ?? "", Ty.Caption, B.Text2);
        FrameworkElement line = sub;
        if (next) line = K.Columns("auto, *", K.Chip("Next", Appearance.Accent, strong: true).Margin(0, 0, 6, 0).Col(0), sub.Col(1));
        var g = K.Columns("*, auto", K.V(2, title, line).Col(0));
        if (it.Status is { Word.Length: > 0 } s && !CourseKit.IsPlainToDo(s))
        {
            var chip = CourseKit.StatusChip(s.Word, s.Kind);
            chip.Margin = new Thickness(10, 0, 0, 0);
            chip.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(chip.Col(1));
        }
        void Go()
        {
            CloseList();
            Open(engine, t, c, it);
        }
        var b = K.Subtle(g, Go, new Thickness(10, 8, 10, 8), 4);
        AutomationProperties.SetName(b, string.Join(", ", new[] { it.Title, next ? "next" : null, it.Sub, it.Status?.Word }.Where(x => !string.IsNullOrEmpty(x))));
        listRows.Add((b, Go));
        return b;
    }

    /// <summary>The list's keys: ↓ from the filter to the first row and down the rows, ↑ back up to the filter, Home and
    /// End to the first and last rows, Enter opens the row with the keyboard (from the filter, the first one shown),
    /// Escape folds the list away.</summary>
    private void OnListKey(object sender, KeyEventArgs e)
    {
        if (list == null) return;
        var inFilter = listFilter?.IsKeyboardFocusWithin == true;
        var at = listRows.FindIndex(r => r.Row.IsKeyboardFocusWithin);
        switch (e.Key)
        {
            case Key.Down when listRows.Count > 0:
                FocusRow(at < 0 ? 0 : Math.Min(at + 1, listRows.Count - 1));
                e.Handled = true;
                break;
            case Key.Up when at > 0:
                FocusRow(at - 1);
                e.Handled = true;
                break;
            case Key.Up when at == 0 && listFilter != null:
                listFilter.Focus();
                e.Handled = true;
                break;
            case Key.Home when at >= 0:
                FocusRow(0);
                e.Handled = true;
                break;
            case Key.End when at >= 0:
                FocusRow(listRows.Count - 1);
                e.Handled = true;
                break;
            case Key.Enter when at >= 0:
                listRows[at].Open();
                e.Handled = true;
                break;
            case Key.Enter when inFilter && listRows.Count > 0:
                listRows[0].Open();
                e.Handled = true;
                break;
            case Key.Escape:
                CloseList(returnFocus: true);
                e.Handled = true;
                break;
        }
    }

    private void FocusRow(int i)
    {
        var row = listRows[i].Row;
        row.Focus();
        row.BringIntoView();
    }

    /// <summary>The list folded away (at once: what it was for has been chosen, or left), its card's chevron turned back;
    /// <paramref name="returnFocus"/>: the keyboard back on the card.</summary>
    private void CloseList(bool returnFocus = false)
    {
        if (list == null) return;
        var (popup, card, id) = (list, listCard, listId);
        list = null;
        listId = null;
        listCard = null;
        listSurface = null;
        listFilter = null;
        rowsHost = null;
        listRows.Clear();
        if (listWindow != null)
        {
            listWindow.LocationChanged -= OnListWindowMoved;
            listWindow.SizeChanged -= OnListWindowMoved;
            listWindow = null;
        }
        if (Application.Current != null) Application.Current.Deactivated -= OnAppLeft;
        if (listPage != null) { listPage.ScrollChanged -= OnPageScrolled; listPage = null; }
        popup.IsOpen = false;
        card?.ClearValue(ToolTipService.IsEnabledProperty);
        if (id != null && chevrons.TryGetValue(id, out var turn)) Motion.To(turn, RotateTransform.AngleProperty, 0, Motion.Normal, Motion.Decelerate);
        if (returnFocus) card?.Focus();
    }

    /// <summary>
    /// The page built again with a list open (a new answer, the columns changed, the look): the list hung again from its
    /// tool's new card once that is in place — its filter's words kept, without the entrance — or gone with its tool.
    /// </summary>
    private void Rehang(List<Tool> shown)
    {
        if (listId is not { } id) return;
        var keep = listFilter?.Text ?? "";
        CloseList();
        if (shown.FirstOrDefault(t => t.Id == id) is not { } tool || !Chooser(tool) || !cards.TryGetValue(id, out var card)) return;
        void Hang(object sender, RoutedEventArgs e)
        {
            card.Loaded -= Hang;
            if (list == null && cards.TryGetValue(id, out var now) && ReferenceEquals(now, card)) OpenList(tool, card, keep, animate: false);
        }
        card.Loaded += Hang;
    }

    /// <summary>The window moved or changed size: the list follows its card (WPF places a popup only as it opens).</summary>
    private void OnListWindowMoved(object? sender, EventArgs e)
    {
        if (list is not { } p || listCard is not { } card) return;
        p.HorizontalOffset = (card.ActualWidth - (ListWidth + 2 * Side)) / 2 + 0.01;
        p.HorizontalOffset -= 0.01;
    }

    /// <summary>The page scrolled under the list: it folds away, as a light-dismissed flyout does. (The list's own
    /// scrolling comes this way too — a popup's events go on through its card — and is the list's business.)</summary>
    private void OnPageScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, listPage)) return;
        if (e.VerticalChange != 0 || e.HorizontalChange != 0) CloseList();
    }

    /// <summary>Simpl left for another app: the list folds away rather than float over it (not while the screenshot
    /// suite pictures it — other copies of Simpl starting may take the foreground).</summary>
    private void OnAppLeft(object? sender, EventArgs e)
    {
        if (!Shot.Asked) CloseList();
    }

    private static T? Ancestor<T>(DependencyObject d) where T : DependencyObject
    {
        for (var p = VisualTreeHelper.GetParent(d); p != null; p = VisualTreeHelper.GetParent(p))
            if (p is T t) return t;
        return null;
    }
}
