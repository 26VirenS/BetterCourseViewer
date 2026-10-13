using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// The Inbox (the Mac's InboxView), as Windows 11's mail apps show a mailbox: the conversations in a list at the left — a
/// mailbox at a time (Inbox, Unread, Starred, Sent, Archived, from the switcher at the list's head), narrowed by words
/// typed in its filter (Ctrl+E) or to one course — each with who it is with and a picture of them, how many messages,
/// its pencil, star and paperclip, when, the subject (bold while unread, the accent's dot beside it), its last words and
/// its course in its colour; and the one chosen in the reading pane at the right, with its reply box. The list keeps a
/// width of its own (about a third of the Inbox, dragged wider or narrower at the line between them, double-clicked
/// back); on a narrow page the list is shown alone and a conversation opens on a screen of its own. Choosing one reads it
/// (Canvas marks it read) and the arrow keys walk the list; a double-click or Enter opens it on a screen of its own; the
/// star in its hover actions, or a right-click, stars it; New Message writes one.
/// </summary>
public sealed class InboxScreen : Screen
{
    /// <summary>A mailbox of the Inbox, as Canvas keeps them.</summary>
    private static readonly (string Id, string Label, SymbolRegular Icon)[] Mailboxes =
    {
        ("inbox", "Inbox", SymbolRegular.MailInbox24),
        ("unread", "Unread", SymbolRegular.MailUnread24),
        ("starred", "Starred", SymbolRegular.Star24),
        ("sent", "Sent", SymbolRegular.Send24),
        ("archived", "Archived", SymbolRegular.Archive24),
    };

    // how the Inbox shares its width: the list about a third of it (320–420 as the page suggests, 300–560 as dragged),
    // the conversation at least 400; below 720, the list alone
    private const double TwoPanes = 720, LeastThread = 400;

    private readonly Loader<InboxData> inbox = new();
    /// <summary>The mailbox showing: another one on its way leaves it, dimmed, until the new one swaps in (no blank list).</summary>
    private InboxData? shown;
    private string scope = "inbox";
    private string? selection;
    private bool switching, dropMissing, mounted, listening;
    /// <summary>Conversations opened here that Canvas has not yet said are read (it is told as each one opens).</summary>
    private readonly HashSet<string> opened = new();
    private string query = "";
    private string? course;
    private string? lastTitle, lastSubtitle;
    /// <summary>The list's width as last dragged (0: as the page's width suggests).</summary>
    private double listWidth = Prefs.Double("inboxListWidth", 0);

    // the page's pieces, made once: what is typed in the filter stays as the list changes under it
    private readonly Grid root = new();
    private readonly Border listPane, readerPane;
    private readonly ScrollViewer listScroll;
    private readonly Stack rowsPanel = new() { Orientation = Orientation.Vertical, Spacing = 0 };
    private readonly ContentControl emptyHost = new() { Focusable = false, IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly ContentControl readerHost = new() { Focusable = false, IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly ContentControl chipHost = new() { Focusable = false, IsTabStop = false };
    private readonly InboxSwitcher switcher;
    private readonly TextBox filter;
    private readonly Button courseButton;
    private readonly FrameworkElement handle;
    private readonly Dictionary<string, RowView> rowViews = new();
    /// <summary>The rows made for the answer shown, kept while the filter or the course narrows the list: a conversation
    /// shown again has its row put back, not made anew (a new answer, or the page built again, makes them afresh).</summary>
    private readonly Dictionary<string, RowView> made = new();
    private readonly List<string> order = new();
    private InboxThreadView? reader;

    public InboxScreen(Engine.Engine engine) : base(engine)
    {
        inbox.Changed += () => Arrived();
        WhenReading(() => inbox.Loading);
        if (View is ContentControl page) page.VerticalContentAlignment = VerticalAlignment.Stretch; // (the panes fill the page's height)

        // ---- the list: the switcher, the filter and the course menu, the course chosen; then the conversations ----
        switcher = new InboxSwitcher(Mailboxes.Select(m => (m.Label, m.Icon)).ToList(), 0);
        switcher.Changed += i => SwitchMailbox(Mailboxes[i].Id);
        filter = new TextBox
        {
            PlaceholderText = "Filter conversations", Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 }, ClearButtonEnabled = true,
            MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Filter Conversations (Ctrl+E)",
        };
        System.Windows.Automation.AutomationProperties.SetName(filter, "Filter Conversations");
        filter.TextChanged += (_, _) =>
        {
            if (filter.Text == query) return;
            query = filter.Text;
            ShowRows(false);
        };
        courseButton = K.Subtle(new Grid(), CourseMenu, new Thickness(0), 4);
        courseButton.Width = 36;
        courseButton.Height = 34;
        courseButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(courseButton, "Course");
        var tools = K.Columns("*, auto", filter.Col(0), courseButton.Margin(6, 0, 0, 0).Col(1));
        var head = K.V(10, switcher, tools, chipHost);
        head.Margin = new Thickness(12, 12, 12, 10);

        listScroll = new ScrollViewer
        {
            Content = rowsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(4, 2, 4, 6), Focusable = false,
        };
        // (a right-click under the conversations: New Message)
        listScroll.MouseRightButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            e.Handled = true;
            var m = new ContextMenu();
            m.Items.Add(InboxUi.Item("New Message", SymbolRegular.Compose24, engine.AskNewMessage));
            InboxUi.Open(m, listScroll, atPointer: true);
        };
        var listArea = new Grid { Children = { listScroll, emptyHost } };
        listArea.PreviewKeyDown += ListKey;
        var listGrid = K.Rows("auto, auto, *", head.Row(0), K.Divider().Row(1), listArea.Row(2));
        listPane = K.Card(listGrid, new Thickness(0));
        listPane.ClipToBounds = true;

        // ---- the reading pane ----
        readerPane = K.Card(readerHost, new Thickness(0));
        readerPane.ClipToBounds = true;
        handle = Handle();

        root.SizeChanged += (_, e) => { if (root.ColumnDefinitions.Count == 3) root.ColumnDefinitions[0].Width = new GridLength(ListWidth(e.NewSize.Width)); };
        root.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.E && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { filter.Focus(); filter.SelectAll(); e.Handled = true; }
        };
    }

    // ---- the header ----

    private (string Id, string Label, SymbolRegular Icon) Shown
    {
        get
        {
            foreach (var m in Mailboxes) if (m.Id == scope) return m;
            return Mailboxes[0];
        }
    }

    public override string Title => Shown.Label;

    /// <summary>Under the title: how many are unread, else how many there are.</summary>
    public override string? Subtitle
    {
        get
        {
            if (shown is not { Rows.Count: > 0 } d) return null;
            var n = d.Rows.Count(IsUnread);
            if (n > 0) return $"{n} unread";
            return $"{d.Rows.Count} {(d.Rows.Count == 1 ? "conversation" : "conversations")}";
        }
    }

    /// <summary>New Message, and the Canvas menu for the conversation chosen (else the Inbox).</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            var write = K.Button("New Message", engine.AskNewMessage, icon: SymbolRegular.Compose24);
            write.ToolTip = "New Message (Ctrl+Shift+N)";
            var more = InboxUi.CanvasMenu(engine, () => selection != null ? InboxUi.Address(selection) : "/conversations", () => SelectedRow?.Subject ?? Shown.Label);
            return K.H(8, write, more);
        }
    }

    public override bool Scrolls => false;

    private void Header()
    {
        if (Title == lastTitle && Subtitle == lastSubtitle) return;
        lastTitle = Title;
        lastSubtitle = Subtitle;
        HeaderChange();
    }

    // ---- reading ----

    public override void Refresh()
    {
        InboxStore.Bind(engine);
        if (!listening) { InboxStore.DraftChanged += OnDraft; listening = true; }
        inbox.Ensure(engine, "inbox", new { scope });
        if (inbox.Data is { } kept && !ReferenceEquals(kept, shown)) Adopt(kept, fresh: false, animate: false);
        if (mounted) foreach (var v in rowViews.Values) Paint(v); // (a reply begun on a conversation's own screen: its pencil)
        reader?.Refresh();
    }

    public override void Hide()
    {
        base.Hide();
        if (listening) { InboxStore.DraftChanged -= OnDraft; listening = false; }
    }

    /// <summary>Read ahead, unseen: the mailbox's conversations, nothing drawn (shown, the answer kept is taken up).</summary>
    protected override void ReadAhead() => inbox.Ensure(engine, "inbox", new { scope });

    private void OnDraft(string id)
    {
        if (rowViews.TryGetValue(id, out var v)) Paint(v);
    }

    /// <summary>The school answered (or could not).</summary>
    private void Arrived()
    {
        if (Away && !mounted) return; // (read ahead: taken up when the screen is shown)
        if (inbox.Data is { } d) { Adopt(d, fresh: true, animate: switching); return; }
        switching = false;
        Dim(false);
        if (!mounted) Render();
    }

    /// <summary>
    /// A mailbox's answer shown (as last read at once, if it has been; then the school's): a conversation opened here
    /// stays read until Canvas says so too; after another mailbox is picked, a conversation not in it is put away.
    /// </summary>
    private void Adopt(InboxData d, bool fresh, bool animate)
    {
        if (!ReferenceEquals(d, shown)) made.Clear();
        shown = d;
        switching = false;
        opened.IntersectWith(d.Rows.Where(r => r.Unread == true).Select(r => r.Id));
        if (fresh && dropMissing)
        {
            dropMissing = false;
            if (selection != null && d.Rows.All(r => r.Id != selection)) selection = null;
        }
        if (!mounted) { Render(); }
        else
        {
            ShowRows(animate);
            ShowReader();
            Header();
        }
        // (the screenshot suite: --open conversation:<id>, or conversation:first, opens one beside the list)
        if (fresh && Launch.Take("conversation:") is { } pick)
        {
            var id = pick == "first" ? d.Rows.FirstOrDefault()?.Id : pick;
            if (id != null) Later(() => Choose(id));
        }
    }

    private void Later(Action a) => root.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, a);

    /// <summary>The list dimmed while another mailbox is on its way.</summary>
    private void Dim(bool on) => Motion.To(listScroll, UIElement.OpacityProperty, on ? 0.5 : 1, Motion.Normal);

    // ---- the page ----

    private double Side(double w) => w + 80 < 560 ? 20 : 40;
    private bool Compact(double w) => w + 80 - 2 * Side(w) < TwoPanes;
    private bool IsCompact => Compact(Width);

    protected override bool LayoutChanges(double before, double now) => Compact(before) != Compact(now) || Side(before) != Side(now);

    protected override FrameworkElement Build()
    {
        if (shown == null)
        {
            mounted = false;
            return K.LoadState(inbox.Error, () => inbox.Reload(engine, "inbox", new { scope }));
        }
        mounted = true;
        // (the conversation beside the list put away: on a narrow page one opens on a screen of its own)
        if (IsCompact) selection = null;
        Arrange();
        made.Clear(); // (built again for the look or the width: every row made afresh)
        ShowRows(false);
        ShowReader();
        Header();
        return root;
    }

    /// <summary>The list and the conversation side by side, the list at its own width; on a narrow page the list alone.</summary>
    private void Arrange()
    {
        var side = Side(Width);
        root.Margin = new Thickness(side, 0, side, side == 40 ? 28 : 20);
        root.Children.Clear();
        root.ColumnDefinitions.Clear();
        if (IsCompact)
        {
            root.Children.Add(listPane.Col(0));
            return;
        }
        var avail = Width + 80 - 2 * side;
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ListWidth(avail)) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(listPane.Col(0));
        root.Children.Add(handle.Col(1));
        root.Children.Add(readerPane.Col(2));
    }

    private static (double Lo, double Hi) ListRange(double w) => (300, Math.Max(300, Math.Min(560, w - LeastThread)));

    private double ListWidth(double w)
    {
        var (lo, hi) = ListRange(w);
        var wanted = listWidth > 0 ? listWidth : Math.Min(Math.Max(w * 0.36, 320), 420);
        return Math.Clamp(wanted, lo, hi);
    }

    /// <summary>
    /// The line between the list and the conversation: dragged, the list is wider or narrower (and stays so);
    /// double-clicked, it goes back to the width the page suggests; the arrow keys move it too. Its grip shows under the pointer.
    /// </summary>
    private FrameworkElement Handle()
    {
        var grip = new Border { Width = 4, Height = 40, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
        grip.Bg(B.StrongFill);
        var h = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeWE, Child = grip, Focusable = true, FocusVisualStyle = null };
        h.ToolTip = "Drag to resize the list · double-click for its own width";
        System.Windows.Automation.AutomationProperties.SetName(h, "Conversation list width");
        double from = 0, start = 0;
        void Show(bool on) => Motion.To(grip, UIElement.OpacityProperty, on ? 1 : 0, Motion.Fast);
        void Set(double w, bool keep)
        {
            if (root.ColumnDefinitions.Count != 3) return;
            var avail = root.ActualWidth;
            var (lo, hi) = ListRange(avail);
            listWidth = w <= 0 ? 0 : Math.Clamp(w, lo, hi);
            root.ColumnDefinitions[0].Width = new GridLength(ListWidth(avail));
            if (keep) Prefs.Set("inboxListWidth", listWidth);
        }
        h.MouseEnter += (_, _) => Show(true);
        h.MouseLeave += (_, _) => { if (!h.IsMouseCaptured) Show(false); };
        h.GotKeyboardFocus += (_, _) => Show(true);
        h.LostKeyboardFocus += (_, _) => { if (!h.IsMouseOver) Show(false); };
        h.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2) { Set(0, true); return; }
            from = e.GetPosition(root).X;
            start = root.ColumnDefinitions.Count == 3 ? root.ColumnDefinitions[0].ActualWidth : 0;
            h.CaptureMouse();
        };
        h.MouseMove += (_, e) => { if (h.IsMouseCaptured) Set(start + e.GetPosition(root).X - from, false); };
        h.MouseLeftButtonUp += (_, _) =>
        {
            if (!h.IsMouseCaptured) return;
            h.ReleaseMouseCapture();
            Prefs.Set("inboxListWidth", listWidth);
            if (!h.IsMouseOver) Show(false);
        };
        h.KeyDown += (_, e) =>
        {
            if (root.ColumnDefinitions.Count != 3) return;
            var w = root.ColumnDefinitions[0].ActualWidth;
            if (e.Key == Key.Left) { Set(w - 24, true); e.Handled = true; }
            else if (e.Key == Key.Right) { Set(w + 24, true); e.Handled = true; }
        };
        return h;
    }

    // ---- the list ----

    private string Trimmed => query.Trim();

    /// <summary>The mailbox's conversations, narrowed to the course chosen and to those with the words typed (who, the
    /// subject, the last words, the course).</summary>
    private List<ConvRow> Visible(InboxData d)
    {
        var q = Trimmed;
        if (course == null && q.Length == 0) return d.Rows;
        return d.Rows.Where(r =>
        {
            if (course != null && r.Context != course) return false;
            if (q.Length == 0) return true;
            return new[] { r.Who, r.Subject, r.Preview ?? "", r.Context ?? "" }.Any(s => s.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        }).ToList();
    }

    /// <summary>The conversations drawn again (the mailbox's answer, the filter, the course), the one focused kept focused:
    /// a conversation already drawn for this answer has its row put back (painted as it is now), the others are made.</summary>
    private void ShowRows(bool animate)
    {
        if (shown is not { } d) return;
        var focused = rowViews.FirstOrDefault(kv => kv.Value.Button.IsKeyboardFocusWithin).Key;
        var rows = Visible(d);
        rowsPanel.Children.Clear();
        rowViews.Clear();
        order.Clear();
        for (var i = 0; i < rows.Count; i++)
        {
            var c = rows[i];
            if (i > 0) rowsPanel.Children.Add(K.Divider().Margin(77, 1, 10, 1));
            RowView v;
            if (!rowViews.ContainsKey(c.Id) && made.TryGetValue(c.Id, out var kept) && ReferenceEquals(kept.Row, c)) { v = kept; Paint(v); }
            else made[c.Id] = v = Row(c);
            rowViews[c.Id] = v;
            order.Add(c.Id);
            rowsPanel.Children.Add(v.Button);
        }
        Dim(switching);
        emptyHost.Content = rows.Count == 0 ? EmptyList(d) : null;
        ShowCourse(d);
        if (focused != null && rowViews.TryGetValue(focused, out var again)) Later(() => again.Button.Focus());
        if (animate) Motion.Stagger(rowsPanel.Children.OfType<Button>(), 30, 14);
    }

    /// <summary>The list with nothing in it: the mailbox empty, nothing matching the words typed, or nothing from the course.</summary>
    private FrameworkElement EmptyList(InboxData d)
    {
        if (d.Rows.Count == 0)
        {
            var text = d.Empty ?? "No messages.";
            return InboxUi.Unavailable(Shown.Icon, text.EndsWith('.') ? text[..^1] : text, null);
        }
        if (Trimmed.Length > 0) return InboxUi.Unavailable(SymbolRegular.Search24, $"No Results for “{Trimmed}”", "Check the spelling or try a new search.");
        return InboxUi.Unavailable(SymbolRegular.Filter24, "Nothing from This Course", $"{Shown.Label} holds no conversations about {course ?? "this course"}.",
            K.Button("Show Every Course", () => SetCourse(null)));
    }

    /// <summary>The course menu's button (its filter filled in the accent while one course is shown) and the chip of the course chosen.</summary>
    private void ShowCourse(InboxData d)
    {
        var on = course != null;
        courseButton.Content = K.Icon(SymbolRegular.Filter24, 18, on ? B.AccentText : B.Text2, filled: on);
        courseButton.IsEnabled = on || CourseNames(d).Count > 0;
        courseButton.ToolTip = on ? $"Showing {course}" : "Show One Course";
        if (course == null) { chipHost.Content = null; chipHost.Visibility = Visibility.Collapsed; return; }
        chipHost.Visibility = Visibility.Visible;
        chipHost.Content = CourseChip(course);
    }

    /// <summary>The courses the mailbox's conversations are about, by name (and the one chosen, though none here is about it).</summary>
    private List<string> CourseNames(InboxData d)
    {
        var names = new HashSet<string>(d.Rows.Select(r => r.Context).Where(c => !string.IsNullOrEmpty(c))!);
        if (course != null) names.Add(course);
        return names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>The courses to narrow the list to one, checked as a menu's items are.</summary>
    private void CourseMenu()
    {
        if (shown is not { } d) return;
        var m = new ContextMenu();
        var every = new System.Windows.Controls.MenuItem { Header = "Every Course", IsCheckable = true, IsChecked = course == null };
        every.Click += (_, _) => SetCourse(null);
        m.Items.Add(every);
        var names = CourseNames(d);
        if (names.Count > 0) m.Items.Add(new Separator());
        foreach (var n in names)
        {
            var name = n;
            var mi = new System.Windows.Controls.MenuItem { Header = name, IsCheckable = true, IsChecked = course == name };
            mi.Click += (_, _) => SetCourse(name);
            m.Items.Add(mi);
        }
        InboxUi.Open(m, courseButton);
    }

    private void SetCourse(string? c)
    {
        if (c == course) return;
        course = c;
        ShowRows(false);
        if (chipHost.Content is FrameworkElement chip) Motion.Enter(chip, 0, -8, 250);
    }

    /// <summary>The course the list is narrowed to, in its colour; a click shows every course again.</summary>
    private FrameworkElement CourseChip(string name)
    {
        var tone = CourseColor(name) ?? Appearance.Accent;
        var words = K.Text(name, Ty.Body);
        words.FontWeight = FontWeights.Medium;
        var content = K.H(7, K.Dot(tone, 8), words, K.Icon(SymbolRegular.Dismiss24, 10, B.Text2));
        var b = K.Subtle(content, () => SetCourse(null), new Thickness(12, 4, 10, 5), 14);
        b.Background = K.Brush(Color.FromArgb(Appearance.Dark ? (byte)60 : (byte)40, tone.R, tone.G, tone.B));
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.ToolTip = "Show Every Course";
        System.Windows.Automation.AutomationProperties.SetName(b, $"{name}, show every course");
        return b;
    }

    /// <summary>A course's colour, by the name Canvas gives the conversation (one of the sidebar's courses).</summary>
    private Color? CourseColor(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var c = engine.Courses.FirstOrDefault(c => c.Name == name || c.Original == name || c.Nickname == name || c.Code == name);
        return c?.Color is { } hex ? K.Hex(hex) : null;
    }

    // ---- a conversation in the list ----

    /// <summary>A row's parts that change while it shows (read, starred, chosen, a reply begun).</summary>
    private sealed class RowView
    {
        public required ConvRow Row;
        public required Button Button;
        public required Ellipse Dot;
        public required TextBlock Subject;
        public required FrameworkElement Pencil, StarMark, Pill, When, Actions, ReadAction;
        public required ContentControl StarAction;
    }

    /// <summary>
    /// A conversation as Windows 11's mail apps draw a message (the Mac's InboxRowView): the accent's dot while it is
    /// unread, a picture of who it is with, their name and how many messages it holds, a pencil for a reply begun, its
    /// star and paperclip, when (under the pointer, Star and Mark as Read in its place); the subject, its last words, its
    /// course in its colour. Chosen, the list's fill with the accent's pill at its left.
    /// </summary>
    private RowView Row(ConvRow c)
    {
        var id = c.Id;
        var dot = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 8, 0) };
        dot.SetResourceReference(Shape.FillProperty, B.Accent);
        System.Windows.Automation.AutomationProperties.SetName(dot, "Unread");
        var avatar = InboxUi.Avatar(c.Who, null, 38);

        // the top line: who, how many; the marks; when (or, under the pointer, what can be done)
        var who = K.Text(c.Who, Ty.BodyStrong);
        UIElement? count = null;
        if (c.Count is > 1 and var n)
        {
            var t = K.Text(n.ToString(), Ty.Caption, B.Text2);
            t.FontWeight = FontWeights.SemiBold;
            count = new Border { Child = t, Padding = new Thickness(6, 0, 6, 1), CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center, ToolTip = $"{n} messages" }.Bg(B.Subtle3);
        }
        var nameLine = new InboxNameLine(who, count);
        var pencil = K.Icon(SymbolRegular.Edit24, 14, B.Text2).Tip("A reply begun here");
        var starMark = K.Icon(SymbolRegular.Star24, 14, K.Tone("yellow"), filled: true).Tip("Starred");
        var clip = c.Attachment == true ? K.Icon(SymbolRegular.Attach24, 14, B.Text2).Tip("Has an attachment") : null;
        var marks = K.H(6, pencil, starMark, clip);
        marks.VerticalAlignment = VerticalAlignment.Center;
        var when = K.Text(c.When ?? "", Ty.Caption, B.Text2);
        when.VerticalAlignment = VerticalAlignment.Center;
        var starAction = new ContentControl { Focusable = false, IsTabStop = false };
        var starButton = K.Subtle(starAction, () => Star(id, !(Find(id)?.Starred == true)), new Thickness(0), 4);
        starButton.Width = 28;
        starButton.Height = 20;
        starButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        var readAction = K.Subtle(K.Icon(SymbolRegular.MailRead24, 14, B.Text), () => MarkRead(id), new Thickness(0), 4);
        readAction.Width = 28;
        readAction.Height = 20;
        readAction.HorizontalContentAlignment = HorizontalAlignment.Center;
        readAction.ToolTip = "Mark as Read";
        System.Windows.Automation.AutomationProperties.SetName(readAction, "Mark as Read");
        var actions = K.H(2, starButton, readAction);
        actions.Visibility = Visibility.Collapsed;
        var corner = new Grid { Children = { when, actions }, Margin = new Thickness(8, 0, 0, 0) };
        var top = K.Columns("*, auto, auto", nameLine.Col(0), marks.Margin(6, 0, 0, 0).Col(1), corner.Col(2));
        top.MinHeight = 20;

        var subject = K.Text(c.Subject, Ty.Body);
        var words = K.V(2, top, subject);
        if (!string.IsNullOrEmpty(c.Preview)) words.Children.Add(K.Wrapped(c.Preview!.Replace('\n', ' '), Ty.Caption, B.Text2, 2));
        if (!string.IsNullOrEmpty(c.Context))
        {
            var tone = CourseColor(c.Context!);
            var cdot = tone is { } col ? K.Dot(col, 7) : new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center }.Bg(B.Text3);
            words.Children.Add(K.H(6, cdot, K.Text(c.Context!, Ty.Caption, B.Text2)).Margin(0, 2, 0, 0));
        }
        var grid = K.Columns("auto, auto, *", dot.Col(0), avatar.Col(1), words.Margin(12, 0, 0, 0).Col(2));
        var pill = new Border { Width = 3, Height = 18, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-7, 0, 0, 0) };
        pill.Bg(B.Accent);
        var content = new Grid { Children = { grid, pill } };

        var b = K.Subtle(content, () => Choose(id), new Thickness(10, 10, 10, 10), 4);
        b.MouseDoubleClick += (_, e) =>
        {
            // (not a double-click on its hover actions)
            if (e.OriginalSource is DependencyObject o && Within(o, actions)) return;
            e.Handled = true;
            OpenAlone(id);
        };
        b.MouseEnter += (_, _) => { when.Visibility = Visibility.Collapsed; actions.Visibility = Visibility.Visible; };
        b.MouseLeave += (_, _) => { when.Visibility = Visibility.Visible; actions.Visibility = Visibility.Collapsed; };
        b.MouseRightButtonUp += (_, e) => { e.Handled = true; RowMenu(id, b); };
        var parts = new List<string> { c.Who, c.Subject };
        if (!string.IsNullOrEmpty(c.When)) parts.Add(c.When!);
        if (!string.IsNullOrEmpty(c.Context)) parts.Add(c.Context!);
        System.Windows.Automation.AutomationProperties.SetName(b, string.Join(", ", parts));
        var v = new RowView
        {
            Row = c, Button = b, Dot = dot, Subject = subject, Pencil = pencil, StarMark = starMark, Pill = pill, When = when, Actions = actions,
            ReadAction = readAction, StarAction = starAction,
        };
        Paint(v);
        return v;
    }

    private static bool Within(DependencyObject o, DependencyObject container)
    {
        for (var p = o; p != null; p = VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p)) if (p == container) return true;
        return false;
    }

    private void Paint(RowView v)
    {
        var unread = IsUnread(v.Row);
        var starred = v.Row.Starred == true;
        var chosen = v.Row.Id == selection && !IsCompact;
        v.Dot.Visibility = unread ? Visibility.Visible : Visibility.Hidden;
        v.Subject.FontWeight = unread ? FontWeights.SemiBold : FontWeights.Normal;
        v.StarMark.Visibility = starred ? Visibility.Visible : Visibility.Collapsed;
        v.Pencil.Visibility = InboxStore.HasDraft(v.Row.Id) ? Visibility.Visible : Visibility.Collapsed;
        v.ReadAction.Visibility = unread ? Visibility.Visible : Visibility.Collapsed;
        v.StarAction.Content = starred ? K.Icon(SymbolRegular.StarOff24, 14, B.Text) : K.Icon(SymbolRegular.Star24, 14, B.Text);
        if (v.StarAction.Parent is FrameworkElement sb)
        {
            sb.ToolTip = starred ? "Unstar" : "Star";
            System.Windows.Automation.AutomationProperties.SetName(sb, starred ? "Unstar" : "Star");
        }
        v.Pill.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
        if (chosen) v.Button.SetResourceReference(Control.BackgroundProperty, B.Subtle);
        else v.Button.ClearValue(Control.BackgroundProperty);
    }

    /// <summary>A conversation's menu: open it on a screen of its own, mark it read, star it, show its course alone, open it
    /// in Canvas, copy its link.</summary>
    private void RowMenu(string id, FrameworkElement at)
    {
        if (Find(id) is not { } c) return;
        var m = new ContextMenu();
        m.Items.Add(InboxUi.Item("Open", SymbolRegular.Open24, () => OpenAlone(id)));
        if (IsUnread(c)) m.Items.Add(InboxUi.Item("Mark as Read", SymbolRegular.MailRead24, () => MarkRead(id)));
        m.Items.Add(InboxUi.Item(c.Starred == true ? "Unstar" : "Star", c.Starred == true ? SymbolRegular.StarOff24 : SymbolRegular.Star24, () => Star(id, c.Starred != true)));
        if (!string.IsNullOrEmpty(c.Context) && course != c.Context)
        {
            var ctx = c.Context!;
            m.Items.Add(InboxUi.Item($"Show Only {ctx}", SymbolRegular.Filter24, () => SetCourse(ctx)));
        }
        m.Items.Add(new Separator());
        m.Items.Add(InboxUi.Item($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(InboxUi.Address(id), c.Subject)));
        m.Items.Add(InboxUi.Item("Copy Link", SymbolRegular.Link24, () => InboxUi.CopyLink(engine, InboxUi.Address(id))));
        InboxUi.Open(m, at, atPointer: true);
    }

    /// <summary>The arrow keys walk the list (choosing as they go, beside the conversation); Enter opens one on a screen of its own.</summary>
    private void ListKey(object sender, KeyEventArgs e)
    {
        if (order.Count == 0) return;
        var focused = rowViews.FirstOrDefault(kv => kv.Value.Button.IsKeyboardFocusWithin).Key ?? selection;
        if (e.Key is Key.Down or Key.Up)
        {
            var i = focused == null ? -1 : order.IndexOf(focused);
            var next = i < 0 ? 0 : Math.Clamp(i + (e.Key == Key.Down ? 1 : -1), 0, order.Count - 1);
            var id = order[next];
            if (!IsCompact) Choose(id);
            if (rowViews.TryGetValue(id, out var v)) { v.Button.Focus(); v.Button.BringIntoView(); }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && focused != null)
        {
            OpenAlone(focused);
            e.Handled = true;
        }
    }

    // ---- the reading pane ----

    private ConvRow? Find(string id) => shown?.Rows.FirstOrDefault(r => r.Id == id);

    private ConvRow? SelectedRow => selection == null ? null : Find(selection);

    /// <summary>The conversation chosen, or a word on choosing one.</summary>
    private void ShowReader()
    {
        if (IsCompact) { reader = null; readerHost.Content = null; return; }
        if (selection == null)
        {
            reader = null;
            var write = K.Button("New Message", engine.AskNewMessage, accent: true, icon: SymbolRegular.Compose24);
            readerHost.Content = InboxUi.Unavailable(SymbolRegular.MailRead24, "No Conversation Selected",
                "Choose a conversation to read it, or write a new message to a teacher, a classmate or a course.", write);
            return;
        }
        if (reader?.Id == selection) return;
        var id = selection;
        reader = new InboxThreadView(engine, id, new InboxLink(() => Find(id) is { } r ? r.Starred == true : null, on => Star(id, on)));
        readerHost.Content = reader.View;
        reader.Refresh();
        Motion.FadeIn(reader.View, Motion.Normal);
    }

    // ---- what it does ----

    private bool IsUnread(ConvRow c) => c.Unread == true && !opened.Contains(c.Id);

    /// <summary>A conversation chosen in the list: read beside it, or — on a narrow page — on a screen of its own.</summary>
    private void Choose(string id)
    {
        if (IsCompact) { OpenAlone(id); return; }
        if (selection == id) return;
        var before = selection;
        selection = id;
        if (before != null && rowViews.TryGetValue(before, out var old)) Paint(old);
        MarkOpened(id);
        if (rowViews.TryGetValue(id, out var now)) Paint(now);
        ShowReader();
    }

    /// <summary>A conversation chosen is read: its dot goes at once (Canvas is told as it opens).</summary>
    private void MarkOpened(string id)
    {
        if (Find(id) is not { } c || !IsUnread(c)) return;
        opened.Add(id);
        if (rowViews.TryGetValue(id, out var v)) Paint(v);
        Header();
    }

    /// <summary>A conversation on a screen of its own (a double-click, Enter, Open; any choice on a narrow page).</summary>
    private void OpenAlone(string id)
    {
        MarkOpened(id);
        engine.Push(Route.Conversation(id));
    }

    /// <summary>Read without reading it: Canvas marks a conversation read as the page opens it, so the page opens it, out of sight.</summary>
    private async void MarkRead(string id)
    {
        opened.Add(id);
        if (rowViews.TryGetValue(id, out var v)) Paint(v);
        Header();
        try { await engine.Call<ConversationData>("conversation", new { id }); }
        catch
        {
            opened.Remove(id);
            if (rowViews.TryGetValue(id, out var back)) Paint(back);
            Header();
            return;
        }
        await Task.Delay(2000); // (the page tells Canvas just after it answers)
        await engine.RefreshSnapshot();
    }

    /// <summary>Starred at once (the star shows before Canvas answers), and back if Canvas says no.</summary>
    private async void Star(string id, bool on)
    {
        SetStar(id, on);
        if (await engine.Act("star", new { id, on })) inbox.Reload(engine, "inbox", new { scope });
        else SetStar(id, !on);
    }

    private void SetStar(string id, bool on)
    {
        if (Find(id) is { } c) c.Starred = on;
        if (rowViews.TryGetValue(id, out var v))
        {
            Paint(v);
            if (on) Motion.FadeIn(v.StarMark, Motion.Normal);
        }
        if (reader?.Id == id) reader.StarChanged();
    }

    /// <summary>Another mailbox: as last read at once if it has been (read afresh meanwhile), else the list shown stays,
    /// dimmed, until the new one is in; a conversation not in it is put away. From its top, its newest first.</summary>
    private void SwitchMailbox(string id)
    {
        if (id == scope) return;
        scope = id;
        dropMissing = true;
        inbox.Ensure(engine, "inbox", new { scope });
        if (inbox.Data is { } kept) Adopt(kept, fresh: false, animate: true);
        else
        {
            switching = true;
            Dim(true);
        }
        listScroll.ScrollToTop();
        Header();
    }

    public override bool Escape()
    {
        // (Escape in the filter: the words gone, the list again)
        if (!filter.IsKeyboardFocusWithin) return false;
        filter.Text = "";
        if (selection != null && rowViews.TryGetValue(selection, out var v)) v.Button.Focus();
        else Keyboard.ClearFocus();
        return true;
    }
}

/// <summary>A name and the count beside it: the name gives way (an ellipsis) so the count keeps its place right after it.</summary>
internal sealed class InboxNameLine : Panel
{
    private readonly UIElement name;
    private readonly UIElement? count;

    public InboxNameLine(UIElement name, UIElement? count)
    {
        this.name = name;
        this.count = count;
        Children.Add(name);
        if (count != null) Children.Add(count);
    }

    protected override Size MeasureOverride(Size available)
    {
        var c = new Size();
        if (count != null) { count.Measure(new Size(double.PositiveInfinity, available.Height)); c = count.DesiredSize; }
        var gap = count != null ? 6 : 0;
        name.Measure(new Size(Math.Max(0, available.Width - c.Width - gap), available.Height));
        var w = name.DesiredSize.Width + gap + c.Width;
        return new Size(double.IsInfinity(available.Width) ? w : Math.Min(w, available.Width), Math.Max(name.DesiredSize.Height, c.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var c = count?.DesiredSize ?? new Size();
        var gap = count != null ? 6 : 0;
        var nw = Math.Min(name.DesiredSize.Width, Math.Max(0, final.Width - c.Width - gap));
        name.Arrange(new Rect(0, 0, nw, final.Height));
        count?.Arrange(new Rect(nw + gap, (final.Height - c.Height) / 2, c.Width, c.Height));
        return final;
    }
}

/// <summary>
/// The mailboxes as one switcher (the Mac's InboxMailboxSwitcher, in Windows 11's SelectorBar's manner): each its symbol,
/// the one showing filled in the accent with its name too, the accent's short bar under it sliding from one to the next.
/// </summary>
internal sealed class InboxSwitcher : Grid
{
    private readonly Grid row = new();
    private readonly Canvas overlay = new() { IsHitTestVisible = false };
    private readonly Border bar = new() { Height = 3, Width = 16, CornerRadius = new CornerRadius(1.5) };
    private readonly List<(Button B, SymbolIcon I, TextBlock T)> items = new();
    private int selected;
    private double? barAt;
    private bool slide;

    public event Action<int>? Changed;

    public InboxSwitcher(IReadOnlyList<(string Label, SymbolRegular Icon)> boxes, int selected)
    {
        this.selected = selected;
        bar.SetResourceReference(Border.BackgroundProperty, B.Accent);
        overlay.Children.Add(bar);
        for (var i = 0; i < boxes.Count; i++)
        {
            var n = i;
            var icon = K.Icon(boxes[i].Icon, 16, B.Text2);
            var label = K.Text(boxes[i].Label, Ty.Body, B.AccentText);
            label.FontWeight = FontWeights.SemiBold;
            label.Margin = new Thickness(7, 0, 0, 0);
            var b = K.Subtle(K.H(0, icon, label), () => Select(n), new Thickness(9, 6, 9, 9));
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.ToolTip = boxes[i].Label;
            System.Windows.Automation.AutomationProperties.SetName(b, boxes[i].Label);
            items.Add((b, icon, label));
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(b.Col(i));
        }
        Children.Add(row);
        Children.Add(overlay);
        Paint();
        LayoutUpdated += (_, _) => Place();
    }

    public void Select(int i)
    {
        if (i == selected) return;
        selected = i;
        slide = true;
        Paint();
        Changed?.Invoke(i);
    }

    private void Paint()
    {
        for (var i = 0; i < items.Count; i++)
        {
            var on = i == selected;
            items[i].I.Filled = on;
            items[i].I.SetResourceReference(Control.ForegroundProperty, on ? B.AccentText : B.Text2);
            items[i].T.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            row.ColumnDefinitions[i].Width = on ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            System.Windows.Automation.AutomationProperties.SetItemStatus(items[i].B, on ? "Selected" : "");
        }
    }

    /// <summary>The bar under the one showing (once the row has been laid out with its name in it).</summary>
    private void Place()
    {
        if (selected >= items.Count) return;
        var b = items[selected].B;
        if (!b.IsLoaded || b.ActualWidth <= 0) return;
        Point p;
        try { p = b.TranslatePoint(new Point(0, 0), overlay); } catch { return; }
        var x = p.X + (b.ActualWidth - 16) / 2;
        Canvas.SetTop(bar, p.Y + b.ActualHeight - 3);
        if (barAt is { } from && Math.Abs(from - x) < 0.5) return;
        if (slide && barAt is { } start) Motion.FromTo(bar, Canvas.LeftProperty, start, x, Motion.Slow, Motion.Standard);
        else { bar.BeginAnimation(Canvas.LeftProperty, null); Canvas.SetLeft(bar, x); }
        slide = false;
        barAt = x;
    }
}
