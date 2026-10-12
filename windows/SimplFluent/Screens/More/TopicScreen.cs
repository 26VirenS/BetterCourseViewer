using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// One announcement or discussion, as a page of its own (the Mac's TopicView): the post — who wrote it and when, its
/// words, its files, the assignment it is graded as — then every reply in reading order, threaded by depth, each step
/// further in on a line in the course's colour. Reply writes under the post, and a reply's own Reply writes under that
/// reply: Ctrl+Enter posts, Escape puts the box away. A discussion that wants your post first says so, and a closed one
/// says why. Previous and Next, Reply and More (Open in Canvas, Copy Link) beside the title. Opening it marks it read.
/// </summary>
public sealed class TopicScreen : Screen
{
    /// <summary>The items either side of this one (the page's `neighbours`; the Mac's ItemNeighbours).</summary>
    private sealed class Neighbours
    {
        public Item? Prev { get; set; }
        public Item? Next { get; set; }
    }

    private sealed class Item
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
    }

    /// <summary>How much further in each step of a thread sits.</summary>
    private const double Indent = 20;

    private readonly string ctx, id;
    private readonly Loader<TopicData> model;
    /// <summary>Where a reply is being written: "topic" under the post, else the id of the reply it answers.</summary>
    private string? replyingTo;
    private TopicComposer? composer;
    private string? composerFor;
    /// <summary>The box to bring into view (and give the keys to) once it is laid out.</summary>
    private bool jump;
    private string? lastTitle, lastSub;
    private FrameworkElement? commands;
    private Wpf.Ui.Controls.Button? prevButton, nextButton, replyButton;
    private FrameworkElement? around;
    private Neighbours? neighbours;
    private string? neighboursFor;

    public TopicScreen(Engine.Engine engine, string ctx, string id) : base(engine)
    {
        this.ctx = ctx;
        this.id = id;
        model = Watch(new Loader<TopicData>());
    }

    private object Args => new { ctx, id };

    public override string Title => model.Data?.Title is { Length: > 0 } t ? t : "Discussion";

    public override string? Subtitle => model.Data is { } d ? KindLine(d) : null;

    /// <summary>(The Mac's 1.2) A reading width: a thread is read line by line.</summary>
    public override double? MaxContentWidth => 1000;

    public override void Refresh() => model.Ensure(engine, "topic", Args); // (the page marks it read as it answers)

    /// <summary>Escape puts the reply box away (not while it is posting).</summary>
    public override bool Escape()
    {
        if (replyingTo == null || composer?.Sending == true) return false;
        Cancel();
        return true;
    }

    // ---- beside the title: Previous and Next, Reply, More ----

    private string Url => $"/{ctx}/discussion_topics/{id}?bcv=native";

    public override FrameworkElement? Commands
    {
        get
        {
            if (commands != null) return commands;
            prevButton = Step(false);
            nextButton = Step(true);
            around = K.H(4, prevButton, nextButton);
            replyButton = K.Button("Reply", () => Reply(null), icon: SymbolRegular.ArrowReply24);
            ToolTipService.SetShowOnDisabled(replyButton, true);
            System.Windows.Controls.Button? more = null;
            more = K.IconButton(SymbolRegular.MoreHorizontal24, () => MoreKit.Open(MoreKit.Menu(
                ($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen(Url, model.Data?.Title ?? "Discussion")),
                ("Copy Link", SymbolRegular.Link24, () => MoreKit.Copy(engine.Absolute(Url)?.ToString()))), more!), "More");
            commands = K.H(8, around, replyButton, more);
            PaintCommands();
            return commands;
        }
    }

    /// <summary>Previous or Next: words on them, not bare chevrons, so they are never taken for Back and Forward.</summary>
    private Wpf.Ui.Controls.Button Step(bool next)
    {
        var words = new TextBlock { Text = next ? "Next" : "Previous", VerticalAlignment = VerticalAlignment.Center };
        var chevron = new SymbolIcon { Symbol = next ? SymbolRegular.ChevronRight20 : SymbolRegular.ChevronLeft20, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (next) { row.Children.Add(words); row.Children.Add(chevron.Margin(6, 1, 0, 0)); }
        else { row.Children.Add(chevron.Margin(0, 1, 6, 0)); row.Children.Add(words); }
        var b = new Wpf.Ui.Controls.Button { Content = row, Appearance = ControlAppearance.Secondary };
        b.Click += (_, _) => { if ((next ? neighbours?.Next : neighbours?.Prev) is { } it) engine.OpenWeb(it.Url, it.Name); };
        ToolTipService.SetShowOnDisabled(b, true);
        return b;
    }

    /// <summary>The commands as the answers stand: Reply only where replies are open; Previous and Next once asked for
    /// (none shown where there is nothing either side).</summary>
    private void PaintCommands()
    {
        var d = model.Data;
        if (d != null) LoadNeighbours(d.Announcement == true ? "Announcement" : "Discussion");
        if (replyButton != null)
        {
            replyButton.IsEnabled = d?.CanReply == true;
            replyButton.ToolTip = d == null ? "Reply" : d.CanReply != true ? "Replies are closed" : d.Announcement == true ? "Reply to this announcement" : "Reply to this discussion";
        }
        if (around == null || prevButton == null || nextButton == null) return;
        around.Visibility = d == null || (neighbours != null && neighbours.Prev == null && neighbours.Next == null) ? Visibility.Collapsed : Visibility.Visible;
        prevButton.IsEnabled = neighbours?.Prev != null;
        nextButton.IsEnabled = neighbours?.Next != null;
        prevButton.ToolTip = neighbours?.Prev is { } p ? $"Previous: {p.Name}" : "Nothing before this";
        nextButton.ToolTip = neighbours?.Next is { } n ? $"Next: {n.Name}" : "Nothing after this";
    }

    private async void LoadNeighbours(string type)
    {
        var key = $"{ctx}|{type}|{id}";
        if (neighboursFor == key || id.Length == 0) return;
        neighboursFor = key;
        try { neighbours = await engine.Call<Neighbours>("neighbours", new { ctx, type, id }); }
        catch { neighbours = null; }
        PaintCommands();
    }

    // ---- the page ----

    protected override FrameworkElement Build()
    {
        if (Title != lastTitle || Subtitle != lastSub) { lastTitle = Title; lastSub = Subtitle; HeaderChange(); }
        PaintCommands();
        if (model.Data is not { } d) return K.LoadState(model.Error, () => model.Reload(engine, "topic", Args));
        var color = K.Hex(d.Color, Appearance.Accent);
        var page = new Stack { Orientation = Orientation.Vertical, Spacing = 22 };
        page.Children.Add(PostCard(d, color));
        if (replyingTo == "topic")
        {
            var box = ComposerFor("topic", d.Announcement == true ? "Reply to the announcement" : "Reply to the discussion", null);
            box.Margin = new Thickness(0);
            var card = K.Card(box, new Thickness(18));
            page.Children.Add(card);
            if (jump) Motion.Enter(card, 0, -12, 250);
        }
        page.Children.Add(Replies(d, color));
        if (jump && composer != null)
        {
            jump = false;
            var c = composer;
            c.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                c.BringIntoView(new Rect(0, -80, Math.Max(1, c.ActualWidth), c.ActualHeight + 160));
                c.FocusBox();
            });
        }
        return page;
    }

    private static string KindLine(TopicData d) =>
        string.Join(" · ", new[] { d.Announcement == true ? "Announcement" : "Discussion", d.Context ?? "" }.Where(s => s.Length > 0));

    private void Link(string url) => engine.GoTo(url);

    /// <summary>The post: who wrote it (or, when the school names nobody, the course, on its own colour), when, the
    /// assignment it is graded as, its words and its files.</summary>
    private FrameworkElement PostCard(TopicData d, Color color)
    {
        var who = string.IsNullOrEmpty(d.Author) ? null : d.Author;
        var line = string.Join(" · ", new[] { who == null ? null : d.Context, d.When }.Where(s => !string.IsNullOrEmpty(s)));
        FrameworkElement picture = who != null
            ? K.Avatar(who, null, d.Avatar, 42)
            : K.IconTile(d.Announcement == true ? SymbolRegular.Megaphone24 : SymbolRegular.ChatMultiple24, color, 42);
        var names = K.V(1, K.Text(who ?? d.Context ?? "", Ty.BodyStrong));
        if (line.Length > 0) names.Children.Add(K.Text(line, Ty.Caption, B.Text2));
        names.VerticalAlignment = VerticalAlignment.Center;
        var post = K.V(16, K.Columns("auto, *", picture.Margin(0, 0, 12, 0).Col(0), names.Col(1)));
        if (d.Graded is { Length: > 0 } g) post.Children.Add(GradedLink(g, d));
        if (d.Html.Length > 0) post.Children.Add(new RichText(d.Html, Link, engine.BaseUrl));
        if (d.Attachments is { Count: > 0 } files) post.Children.Add(Attachments(files));
        return K.Card(post, new Thickness(22, 20, 22, 20));
    }

    /// <summary>Graded: its points and due date, and a way to the assignment it is.</summary>
    private FrameworkElement GradedLink(string g, TopicData d)
    {
        var row = K.H(6, K.Chip("Graded", K.Tone("purple"), strong: true), K.Text(g, Ty.Body, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
        if (d.AssignmentUrl != null) row.Children.Add(K.Icon(SymbolRegular.ChevronRight20, 12, B.Text3));
        var b = K.Subtle(row, () => engine.GoTo(d.AssignmentUrl, d.Title), new Thickness(6, 4, 8, 4), 8);
        b.IsEnabled = d.AssignmentUrl != null;
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.Margin = new Thickness(-6, -4, 0, -4);
        b.ToolTip = "Open the assignment";
        return b;
    }

    /// <summary>The post's files: each opened in the app Windows has for it.</summary>
    private FrameworkElement Attachments(List<Attachment> files)
    {
        var v = K.V(4, K.Divider().Margin(8, 0, 8, 4), K.Text("Attached", Ty.Caption, B.Text2).Strong().Margin(8, 0, 0, 2));
        foreach (var f in files) v.Children.Add(FileRow(f));
        // (the rows' words line up with the post's, their wash reaching into the card's margin)
        v.Margin = new Thickness(-8, 0, -8, 0);
        return v;
    }

    /// <summary>A file attached to the post or to a reply.</summary>
    private FrameworkElement FileRow(Attachment file)
    {
        void Open() { if (engine.Absolute(file.Url) is { } u) FilePreview.Open(u, file.Name, engine.Web); }
        var g = K.Columns("auto, *", K.IconTile(SymbolRegular.Attach24, K.Tone("blue"), 30).Margin(0, 0, 12, 0).Col(0),
            K.Wrapped(file.Name, Ty.Body, B.Text, 2).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1));
        var b = K.Subtle(g, Open, new Thickness(8, 7, 8, 7), 6);
        b.ToolTip = $"Open {file.Name}";
        b.ContextMenu = MoreKit.Menu(
            ("Open", SymbolRegular.Open24, Open),
            ("Copy Link", SymbolRegular.Link24, () => MoreKit.Copy(engine.Absolute(file.Url)?.ToString())));
        return b;
    }

    // ---- the replies ----

    /// <summary>Every reply in reading order, threaded by depth; or why there are none to see.</summary>
    private FrameworkElement Replies(TopicData d, Color color)
    {
        var list = new Stack { Orientation = Orientation.Vertical, Spacing = 0 };
        if (RepliesNote(d) is { } note) list.Children.Add(note);
        for (var i = 0; i < d.Entries.Count; i++)
        {
            var e = d.Entries[i];
            if (i > 0) list.Children.Add(K.Divider().Margin(47 + Math.Max(e.Depth, 0) * Indent, 0, 8, 0));
            list.Children.Add(EntryRow(e, d, color));
            if (replyingTo == e.Id) list.Children.Add(InlineComposer(e, color));
        }
        var count = d.Count is > 0 and var n ? n.ToString() : null;
        return K.V(0, Heading.Section("Replies", count), K.Card(list, new Thickness(10)));
    }

    private FrameworkElement? RepliesNote(TopicData d)
    {
        if (d.Locked == true && d.LockText is { Length: > 0 } t) return MoreKit.Note(SymbolRegular.LockClosed24, t).Margin(8, 10, 8, 10);
        if (d.NeedFirst == true)
        {
            var g = K.Columns("*, auto", MoreKit.Note(SymbolRegular.EyeOff24, "Reply to see what others wrote.").Col(0));
            if (d.CanReply == true) g.Children.Add(K.Link("Write Your Reply", () => Reply(null)).Margin(8, -4, 0, -4).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
            return g.Margin(8, 10, 8, 10);
        }
        if (d.Entries.Count == 0) return K.Empty(SymbolRegular.Chat24, d.Announcement == true ? "No replies." : "No replies yet. Be the first.").Margin(8, 10, 8, 10);
        return null;
    }

    /// <summary>A reply: who wrote it and when, its words (the school's own where they hold pictures, tables or formulas),
    /// and Reply; further in by its depth, on the thread's line. A deleted one says so.</summary>
    private FrameworkElement EntryRow(Entry e, TopicData d, Color color)
    {
        var depth = Math.Max(e.Depth, 0);
        FrameworkElement inner;
        if (e.Deleted == true)
        {
            var t = K.Wrapped(e.Text, Ty.Body, B.Text2);
            t.FontStyle = FontStyles.Italic;
            inner = t;
        }
        else
        {
            var head = K.H(9, K.Avatar(e.Author, null, e.Avatar, 30), K.Text(e.Author, Ty.BodyStrong).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
            if (e.When is { Length: > 0 } w) head.Children.Add(K.Text(w, Ty.Caption, B.Text2).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
            var v = K.V(6, head);
            if (e.Rich == true && e.Html is { Length: > 0 } || e.Text.Length > 0)
            {
                FrameworkElement body = e.Rich == true && e.Html is { Length: > 0 } h
                    ? new RichText(h, Link, engine.BaseUrl)
                    : new RichText(WebUtility.HtmlEncode(e.Text).Replace("\r\n", "\n").Replace("\n", "<br>"), Link, engine.BaseUrl);
                body.Margin = new Thickness(39 - 4, 0, -4, 0); // (RichText's own inset of 4)
                v.Children.Add(body);
            }
            // (the files the reply came with — a reply may be a file and nothing else)
            if (e.Attachments is { Count: > 0 } files)
            {
                var list = K.V(0);
                foreach (var f in files) list.Children.Add(FileRow(f));
                list.Margin = new Thickness(39 - 8, 0, 0, 0); // (39, less the row's own 8 of wash)
                v.Children.Add(list);
            }
            if (d.CanReply == true) v.Children.Add(ReplyLink(e));
            inner = v;
        }
        inner.Margin = new Thickness(depth * Indent, 0, 0, 0);
        var g = new Grid();
        if (depth > 0)
            g.Children.Add(new Border
            {
                Width = 2.5, CornerRadius = new CornerRadius(1.25), HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(K.WithAlpha(color, 0.35)), Margin = new Thickness((depth - 1) * Indent + 8, 0, 0, 0),
            });
        g.Children.Add(inner);
        var row = new Border { Child = g, Padding = new Thickness(8, 10, 8, 10), Background = Brushes.Transparent };
        if (e.Deleted != true)
        {
            var items = new List<(string, SymbolRegular, Action)?>();
            if (d.CanReply == true) items.Add(("Reply", SymbolRegular.ArrowReply24, () => Reply(e)));
            items.Add(("Copy Text", SymbolRegular.Copy24, () => MoreKit.Copy(e.Text)));
            row.ContextMenu = MoreKit.Menu(items.ToArray());
        }
        return row;
    }

    private FrameworkElement ReplyLink(Entry e)
    {
        var row = K.H(5, K.Icon(SymbolRegular.ArrowReply20, 13, B.AccentText), K.Text("Reply", Ty.Caption, B.AccentText).Strong());
        var b = K.Subtle(row, () => Reply(e), new Thickness(6, 3, 8, 4), 4);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.Margin = new Thickness(39 - 6, 0, 0, 0);
        b.ToolTip = $"Reply to {e.Author}";
        return b;
    }

    /// <summary>(The Mac's 1.2) A reply to a reply: in the thread, one step in on its line in the course's colour — no box of its own.</summary>
    private FrameworkElement InlineComposer(Entry e, Color color)
    {
        var box = ComposerFor(e.Id, e.Author.Length == 0 ? "Reply" : $"Reply to {e.Author}", e.Id);
        box.Margin = new Thickness(14, 4, 0, 4);
        var bar = new Border { Width = 2.5, CornerRadius = new CornerRadius(1.25), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(K.WithAlpha(color, 0.6)) };
        var g = new Grid { Children = { bar, box } };
        g.Margin = new Thickness(8 + Math.Min(Math.Max(e.Depth, 0) + 1, 4) * Indent, 0, 8, 10);
        if (jump) Motion.Enter(g, 0, -12, 250);
        return g;
    }

    /// <summary>The box for where a reply is being written: the one already there, kept as it is (its words, the keys),
    /// or a new one.</summary>
    private TopicComposer ComposerFor(string target, string heading, string? parent)
    {
        if (composer != null && composerFor == target)
        {
            var focused = composer.IsKeyboardFocusWithin;
            MoreKit.Detach(composer);
            if (focused) { var c = composer; c.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, c.FocusBox); }
            return composer;
        }
        composerFor = target;
        composer = new TopicComposer(heading, text => Post(text, parent), Cancel);
        return composer;
    }

    // ---- doing ----

    /// <summary>A box to write in: under the post (null), or under the reply it answers.</summary>
    private void Reply(Entry? e)
    {
        if (model.Data?.CanReply != true) return;
        var target = e?.Id ?? "topic";
        if (replyingTo != target) { replyingTo = target; composer = null; composerFor = null; }
        jump = true;
        Render();
    }

    private void Cancel()
    {
        replyingTo = null;
        composer = null;
        composerFor = null;
        Render();
    }

    /// <summary>Posted: the box goes, the thread is read again with the reply in it, and the rest of the app hears.</summary>
    private async Task Post(string text, string? parent)
    {
        await engine.Call<OK>("reply", new { ctx, id, text, parent });
        replyingTo = null;
        composer = null;
        composerFor = null;
        model.Reload(engine, "topic", Args);
        engine.DataChanged();
        Render();
    }
}

/// <summary>
/// Writing a reply in place — under the post, or under the reply it answers (the Mac's TopicComposer): the box takes the
/// keys at once, Ctrl+Enter posts, Escape puts it away. It stays until the words are posted, and says so if they could
/// not be.
/// </summary>
internal sealed class TopicComposer : Border
{
    private readonly TextBox box;
    private readonly Wpf.Ui.Controls.Button post, cancel;
    private readonly FrameworkElement errorRow;
    private readonly TextBlock errorText;
    private readonly Func<string, Task> send;
    private bool sending;

    public bool Sending => sending;

    public TopicComposer(string heading, Func<string, Task> send, Action cancel)
    {
        this.send = send;
        box = new TextBox
        {
            PlaceholderText = "Write a reply", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 140,
            VerticalContentAlignment = VerticalAlignment.Top, ClearButtonEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14,
        };
        box.TextChanged += (_, _) => Paint();
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; Go(); }
        };
        errorText = K.Wrapped("", Ty.Body, B.Critical);
        errorRow = K.Columns("auto, *", K.Icon(SymbolRegular.ErrorCircle24, 16, B.Critical, filled: true).Margin(0, 2, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), errorText.Col(1));
        errorRow.Visibility = Visibility.Collapsed;
        this.cancel = K.Button("Cancel", cancel);
        this.cancel.ToolTip = "Escape";
        post = K.Button("Post", Go, accent: true);
        post.ToolTip = "Ctrl+Enter";
        var hint = K.Text("Ctrl+Enter to post · Esc to cancel", Ty.Caption, B.Text3).Align(HorizontalAlignment.Left, VerticalAlignment.Center);
        var foot = K.Columns("*, auto, auto", hint.Col(0), this.cancel.Margin(8, 0, 0, 0).Col(1), post.Margin(8, 0, 0, 0).Col(2));
        Child = K.V(10, K.Text(heading, Ty.BodyStrong), box, errorRow, foot);
        Paint();
    }

    /// <summary>The box takes the keys.</summary>
    public void FocusBox()
    {
        if (!box.IsVisible) return;
        box.Focus();
        Keyboard.Focus(box);
    }

    private async void Go()
    {
        var text = box.Text;
        if (string.IsNullOrWhiteSpace(text) || sending) return;
        sending = true;
        errorRow.Visibility = Visibility.Collapsed;
        Paint();
        try { await send(text); }
        catch (Exception e)
        {
            errorText.Text = e.Message;
            errorRow.Visibility = Visibility.Visible;
        }
        sending = false;
        Paint();
    }

    private void Paint()
    {
        box.IsReadOnly = sending;
        cancel.IsEnabled = !sending;
        post.IsEnabled = !sending && box.Text.Trim().Length > 0;
        post.Content = sending ? K.Spinner(16) : "Post";
    }
}
