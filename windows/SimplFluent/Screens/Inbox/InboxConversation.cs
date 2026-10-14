using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using Message = Simpl.Models.Message;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// A conversation on a screen of its own (the Mac's ConversationView pushed on a place): the subject as the page's title,
/// its course under it, Star (Ctrl+Shift+L) and the Canvas menu beside it; the thread, and on a wide page its people,
/// course and files in a column at the right; the reply box at its foot.
/// </summary>
public sealed class ConversationScreen : Screen
{
    private readonly string id;
    private readonly InboxThreadView thread;

    public ConversationScreen(Engine.Engine engine, string id) : base(engine)
    {
        this.id = id;
        thread = new InboxThreadView(engine, id, null) { Side = () => Side(Width) };
        if (View is ContentControl page) page.VerticalContentAlignment = VerticalAlignment.Stretch; // (the thread fills the page's height, its reply box at the foot)
        thread.Changed += HeaderChange;
    }

    public override string Title => thread.Data?.Subject ?? "Message";
    public override string? Subtitle => thread.Data?.Context is { Length: > 0 } c ? c : null;
    public override bool Scrolls => false;

    /// <summary>Star (or Unstar) and the Canvas menu.</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            var on = thread.Starred;
            var star = K.Button(on ? "Unstar" : "Star", () => thread.SetStarred(!thread.Starred));
            var icon = new SymbolIcon { Symbol = SymbolRegular.Star24, Filled = on };
            if (on) icon.Foreground = K.Brush(K.Tone("yellow"));
            star.Icon = icon;
            star.ToolTip = on ? "Unstar (Ctrl+Shift+L)" : "Star (Ctrl+Shift+L)";
            star.IsEnabled = thread.Data != null;
            var more = InboxUi.CanvasMenu(engine, () => InboxUi.Address(id), () => thread.Data?.Subject ?? "Message");
            return K.H(8, star, more);
        }
    }

    public override void Refresh() => thread.Refresh();

    protected override FrameworkElement Build() => thread.View;

    private static double Side(double w) => w + 80 < 560 ? 20 : 40;

    protected override bool LayoutChanges(double before, double now)
    {
        if (Side(before) != Side(now)) thread.Relayout();
        return false;
    }
}

/// <summary>
/// One conversation (the Mac's ConversationView), beside the Inbox's list or on a screen of its own: its subject and star
/// (beside the list), who is in it and its course, then its messages in order as a mail app shows a thread — each with
/// its sender's picture and name, when it was sent, its words (selectable, their web addresses live) and its files as
/// chips, a hairline between one and the next — and the reply box at its foot (Attach at its start, the files going with
/// it as chips over it; a line that grows with what is written, to six, then scrolls; Ctrl+Enter sends). The messages
/// keep a readable width however wide the pane; from 1,100 wide,
/// who is in it, its course and every file sent in it stand in a column at the right. It opens at its newest message.
/// </summary>
internal sealed class InboxThreadView
{
    private const double Column = 780, SideFrom = 1100, SideColumn = 290;

    public string Id { get; }
    /// <summary>On a screen of its own: the page's side margin (the thread then sits under the page's title, not centred).</summary>
    public Func<double>? Side { get; init; }
    /// <summary>What it shows changed (its subject, course or star): the screen's header follows.</summary>
    public event Action? Changed;

    private readonly Engine.Engine engine;
    private readonly InboxLink? link;
    private readonly Loader<ConversationData> model = new();
    private readonly ContentControl outer = new() { Focusable = false, IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Grid root = new();
    private readonly ScrollViewer scroller;
    private readonly Stack column = new() { Orientation = Orientation.Vertical, Spacing = 0 };
    private readonly Border details = new() { BorderThickness = new Thickness(1, 0, 0, 0), Width = SideColumn };
    private readonly ContentControl starSlot = new() { Focusable = false, IsTabStop = false, VerticalAlignment = VerticalAlignment.Top };
    private readonly Border barHolder = new();
    private readonly InboxColumn readArea = new(), barArea = new();
    private readonly TextBox reply;
    private readonly Wpf.Ui.Controls.Button send;
    private readonly Button attach;
    /// <summary>The files going with the reply, uploaded as they are picked, and their chips over it.</summary>
    private readonly MessageFiles files;
    private readonly ContentControl filesHost = new() { Focusable = false, IsTabStop = false, Visibility = Visibility.Collapsed };
    private readonly HashSet<Guid> chipsSeen = new();
    private readonly TextBlock hint;
    private readonly InfoBar errorBar;
    private bool sending, counted, wide;
    private string? error;
    private ConversationData? drawn;
    private string? drawnError;
    private int drawnCount = -1;

    public InboxThreadView(Engine.Engine engine, string id, InboxLink? link)
    {
        this.engine = engine;
        this.link = link;
        Id = id;
        model.Changed += Answered;

        readArea.Children.Add(column);
        scroller = new ScrollViewer { Content = readArea, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        details.SetResourceReference(Border.BorderBrushProperty, B.Divider);

        // ---- the reply box ----
        reply = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinLines = 1, MaxLines = 6, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            PlaceholderText = "Write a reply", ClearButtonEnabled = false, Text = InboxStore.Draft(id), MaxHeight = 168,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(reply, "Reply");
        reply.TextChanged += (_, _) =>
        {
            InboxStore.SetDraft(Id, reply.Text);
            Bar();
        };
        reply.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; Send(); }
        };
        hint = K.Text("Ctrl+Enter", Ty.Caption, B.Text3);
        hint.VerticalAlignment = VerticalAlignment.Center;
        send = K.Button("Send", Send, accent: true, icon: SymbolRegular.Send24);
        send.MinWidth = 88;
        send.MinHeight = 32;
        send.VerticalAlignment = VerticalAlignment.Bottom;
        send.ToolTip = "Send (Ctrl+Enter)";
        errorBar = new InfoBar { Title = "Not sent", Severity = InfoBarSeverity.Error, IsClosable = false, IsOpen = false, Margin = new Thickness(0, 0, 0, 8) };
        files = new MessageFiles(engine);
        files.Changed += () =>
        {
            filesHost.Visibility = files.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            filesHost.Content = files.Items.Count > 0 ? files.Chips(chipsSeen) : null;
            Bar();
        };
        attach = K.IconButton(SymbolRegular.Attach24, ChooseFiles, "Attach files to this reply", 16, 32);
        attach.VerticalAlignment = VerticalAlignment.Bottom;
        System.Windows.Automation.AutomationProperties.SetName(attach, "Attach");
        var line = K.Columns("auto, *, auto, auto", attach.Margin(0, 0, 6, 0).Col(0), reply.Col(1), hint.Margin(10, 0, 10, 0).Col(2), send.Col(3));
        barHolder.Child = K.V(0, errorBar, filesHost.Margin(0, 0, 0, 2), line);
        // (files dropped on the reply bar go with the reply, as Attach's do)
        barHolder.AllowDrop = true;
        barHolder.PreviewDragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !sending ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        barHolder.PreviewDrop += (_, e) =>
        {
            if (sending || e.Data.GetData(DataFormats.FileDrop) is not string[] dropped) return;
            e.Handled = true;
            files.Add(dropped.Where(System.IO.File.Exists));
        };
        barArea.Children.Add(barHolder);

        var thread = K.Rows("*, auto", scroller.Row(0), barArea.Row(1));
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(thread.Col(0));
        root.Children.Add(details.Col(1));

        outer.SizeChanged += (_, e) =>
        {
            var w = e.NewSize.Width >= SideFrom;
            if (w == wide) return;
            wide = w;
            if (model.Data != null) Draw(force: true);
        };
        outer.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.L && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift) && model.Data != null)
            {
                e.Handled = true;
                SetStarred(!Starred);
            }
        };
        Bar();
    }

    public FrameworkElement View => outer;
    public ConversationData? Data => model.Data;

    /// <summary>The star shown: the Inbox list's while it holds the conversation, else this one's own.</summary>
    public bool Starred => link?.Starred() ?? (model.Data?.Starred == true);

    /// <summary>The conversation read (as last read at once, if it has been, while Canvas is asked again).</summary>
    public void Refresh()
    {
        InboxStore.Bind(engine);
        // (a reply begun on another of its views — beside the list, on a screen of its own — is this one's too)
        if (!reply.IsKeyboardFocusWithin && reply.Text != InboxStore.Draft(Id)) reply.Text = InboxStore.Draft(Id);
        model.Ensure(engine, "conversation", new { id = Id });
        Draw(force: false);
    }

    private void Answered()
    {
        Draw(force: false);
        if (model.Data != null) files.TakeLaunchFile(); // (the screenshot suite's --attach, once the reply bar shows)
        if (counted || model.Data == null) return;
        counted = true;
        _ = Count();
    }

    /// <summary>The page tells Canvas it is read just after answering: the sidebar's count once it has.</summary>
    private async Task Count()
    {
        await Task.Delay(2000);
        await engine.RefreshSnapshot();
    }

    // ---- drawing ----

    private double Pad => Side?.Invoke() ?? 28;

    /// <summary>The side margins again (the page's width crossed a step).</summary>
    public void Relayout() => Draw(force: true);

    private void Draw(bool force)
    {
        var d = model.Data;
        if (d == null)
        {
            if (!force && drawnError == model.Error && outer.Content != null && drawn == null) return;
            drawnError = model.Error;
            drawn = null;
            outer.Content = K.LoadState(model.Error, () => model.Reload(engine, "conversation", new { id = Id }));
            return;
        }
        if (!force && ReferenceEquals(d, drawn)) return;
        var first = drawn == null;
        var atEnd = scroller.ScrollableHeight - scroller.VerticalOffset < 8;
        drawn = d;
        if (link?.Starred() is { } on) d.Starred = on; // (the list's star is the one shown; kept here too)
        outer.Content = root;

        // the thread's column: a readable width, under the page's title on a screen of its own, centred beside the list
        var pad = Pad;
        var alone = Side != null;
        column.Children.Clear();
        readArea.Set(Column + 2 * pad, centre: !alone);
        column.Margin = new Thickness(pad, alone ? 4 : 24, pad, 14);
        column.Children.Add(Head(d).Margin(0, 0, 0, 18));
        foreach (var m in d.Messages) column.Children.Add(Letter(m));
        // (a reply sent rises in at the foot)
        if (drawnCount >= 0 && d.Messages.Count > drawnCount && column.Children.Count > 0 && column.Children[^1] is FrameworkElement last) Motion.Enter(last, 60, 24, 333);
        drawnCount = d.Messages.Count;

        barArea.Set(Column + 2 * pad, centre: !alone);
        barHolder.Padding = new Thickness(pad, 6, pad, alone ? 24 : 16);
        reply.PlaceholderText = Prompt(d);

        details.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        details.Child = wide ? Details(d) : null;
        PaintStar();
        Bar();
        // (it opens at its newest message, and keeps it in view as replies come)
        if (first || atEnd) scroller.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => scroller.ScrollToEnd());
        Changed?.Invoke();
    }

    /// <summary>
    /// Beside the list: the subject and the star; under them (unless the column at the right says so) who is in it and its
    /// course. On a screen of its own the subject is the page's title: who and the course, and the star's mark if starred.
    /// </summary>
    private FrameworkElement Head(ConversationData d)
    {
        Detach(starSlot);
        var facts = K.V(6);
        if (!wide)
        {
            if (!string.IsNullOrEmpty(d.People)) facts.Children.Add(InboxUi.Fact(SymbolRegular.People24, d.People!));
            if (!string.IsNullOrEmpty(d.Context)) facts.Children.Add(InboxUi.Fact(SymbolRegular.Book24, d.Context!));
        }
        if (link != null)
        {
            var subject = K.Wrapped(d.Subject, Ty.Subtitle);
            subject.TextTrimming = TextTrimming.None;
            if (wide) { subject.FontSize = 24; subject.LineHeight = 32; }
            var top = K.Columns("*, auto", subject.Margin(0, 4, 12, 0).Col(0), starSlot.Col(1));
            return facts.Children.Count > 0 ? K.V(10, top, facts) : top;
        }
        return K.Columns("*, auto", facts.Col(0), starSlot.Margin(12, 0, 0, 0).Col(1));
    }

    /// <summary>The star: a button beside the Inbox's list (the commands there are the Inbox's); on a screen of its own
    /// the mark alone, as its commands have the button.</summary>
    private void PaintStar()
    {
        var on = Starred;
        var yellow = K.Tone("yellow");
        if (link != null)
        {
            var icon = on ? K.Icon(SymbolRegular.Star24, 20, yellow, filled: true) : K.Icon(SymbolRegular.Star24, 20, B.Text2);
            var b = K.Subtle(icon, () => SetStarred(!Starred), new Thickness(0), 20);
            b.Width = 40;
            b.Height = 40;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.ToolTip = on ? "Unstar (Ctrl+Shift+L)" : "Star (Ctrl+Shift+L)";
            System.Windows.Automation.AutomationProperties.SetName(b, on ? "Unstar" : "Star");
            starSlot.Content = b;
        }
        else starSlot.Content = on ? K.Icon(SymbolRegular.Star24, 22, yellow, filled: true).Tip("Starred") : null;
    }

    /// <summary>The list's star changed (beside the Inbox's list): kept here too, and shown.</summary>
    public void StarChanged()
    {
        if (link?.Starred() is { } on && model.Data is { } d) d.Starred = on;
        PaintStar();
        if (starSlot.Content is FrameworkElement s && Starred) Motion.FadeIn(s, Motion.Normal);
    }

    private static void Detach(FrameworkElement e)
    {
        if (e.Parent is Panel p) p.Children.Remove(e);
        else if (e.Parent is ContentControl c) c.Content = null;
        else if (e.Parent is Decorator dec) dec.Child = null;
    }

    /// <summary>
    /// A message, as a mail app shows one in a thread (no bubble, no box; the Mac's InboxLetter): a hairline above it, the
    /// sender's picture, their name ("You" for yours, in the accent) and when it was sent, its words, its files as chips.
    /// </summary>
    private FrameworkElement Letter(Message m)
    {
        var name = K.Text(m.Mine ? "You" : m.Author, Ty.BodyStrong, m.Mine ? B.AccentText : B.Text);
        name.ToolTip = m.Author;
        var heading = K.Columns("*, auto", name.Col(0));
        if (!string.IsNullOrEmpty(m.When)) heading.Children.Add(K.Text(m.When!, Ty.Caption, B.Text2).Margin(8, 2, 0, 0).Col(1));
        var words = K.V(8, heading);
        if (m.Body.Length > 0) words.Children.Add(new RichText(InboxUi.Html(m.Body), u => engine.GoTo(u), engine.BaseUrl));
        if (m.Attachments is { Count: > 0 } files)
        {
            var flow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, -8) };
            foreach (var f in files) flow.Children.Add(Chip(f).Margin(0, 0, 8, 8));
            words.Children.Add(flow);
        }
        var g = K.Columns("auto, *", InboxUi.Avatar(m.Author, m.Avatar, 40).Margin(0, 0, 14, 0).Col(0), words.Col(1));
        g.Margin = new Thickness(0, 16, 0, 18);
        return K.V(0, K.Divider(), g);
    }

    /// <summary>A file sent with a message, as a chip with its kind's symbol: a click opens it; a right-click copies its link.</summary>
    private FrameworkElement Chip(Attachment f)
    {
        var name = K.Text(f.Name, Ty.Body);
        name.MaxWidth = 260;
        var b = K.CardButton(K.H(8, K.Icon(InboxUi.FileSymbol(f.Name), 16, B.AccentText), name), () => InboxUi.OpenFile(engine, f.Url, f.Name), 4, new Thickness(11, 6, 13, 7));
        b.ToolTip = $"Open {f.Name}";
        System.Windows.Automation.AutomationProperties.SetName(b, $"Open {f.Name}");
        b.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            var menu = new ContextMenu();
            menu.Items.Add(InboxUi.Item("Open", SymbolRegular.Open24, () => InboxUi.OpenFile(engine, f.Url, f.Name)));
            menu.Items.Add(InboxUi.Item("Copy link", SymbolRegular.Link24, () => InboxUi.CopyLink(engine, f.Url)));
            InboxUi.Open(menu, b, atPointer: true);
        };
        return b;
    }

    // ---- the column at the right (the Mac's InboxThreadDetails) ----

    /// <summary>
    /// Beside a conversation on a wide pane: who is in it (their pictures as the messages show them), its course, how many
    /// messages and when, and every file sent in it — no boxes, a hairline at its left.
    /// </summary>
    private FrameworkElement Details(ConversationData d)
    {
        var people = (d.People ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries);
        var v = K.V(26);
        if (people.Length > 0)
        {
            var list = K.V(8);
            foreach (var p in people)
            {
                var avatar = d.Messages.FirstOrDefault(m => m.Author == p && m.Avatar != null)?.Avatar;
                var row = K.Columns("auto, *", InboxUi.Avatar(p, avatar, 30).Col(0), K.Text(p, Ty.Body).Margin(10, 0, 0, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Center).Col(1));
                list.Children.Add(row);
            }
            v.Children.Add(Section("People", people.Length.ToString(), list));
        }
        if (!string.IsNullOrEmpty(d.Context)) v.Children.Add(Section("Course", null, InboxUi.Fact(SymbolRegular.Book24, d.Context!)));

        var said = K.V(8);
        if (d.Messages.FirstOrDefault()?.When is { Length: > 0 } started) said.Children.Add(InboxUi.Fact(SymbolRegular.Clock24, $"Started {started}"));
        if (d.Messages.Count > 1 && d.Messages[^1].When is { Length: > 0 } latest) said.Children.Add(InboxUi.Fact(SymbolRegular.ArrowReply24, $"Latest {latest}"));
        var mine = d.Messages.Count(m => m.Mine);
        said.Children.Add(InboxUi.Fact(SymbolRegular.PersonCircle24, mine switch { 0 => "None from you yet", 1 => "1 from you", _ => $"{mine} from you" }));
        v.Children.Add(Section("Messages", d.Messages.Count.ToString(), said));

        // every file sent in it, once each
        var seen = new HashSet<string>();
        var files = d.Messages.SelectMany(m => m.Attachments ?? new()).Where(f => seen.Add(f.Url)).ToList();
        if (files.Count > 0)
        {
            var list = K.V(0);
            foreach (var f in files)
            {
                var file = f;
                var name = K.Text(f.Name, Ty.Body);
                var row = K.Columns("auto, *", K.Icon(InboxUi.FileSymbol(f.Name), 16, B.AccentText).Margin(0, 0, 9, 0).Col(0), name.Col(1));
                var b = K.Subtle(row, () => InboxUi.OpenFile(engine, file.Url, file.Name), new Thickness(6, 7, 6, 7));
                b.ToolTip = $"Open {f.Name}";
                list.Children.Add(b);
            }
            v.Children.Add(Section("Files", files.Count.ToString(), list.Margin(-6, 0, -6, 0)));
        }
        v.Margin = new Thickness(22, 28, 22, 28);
        return new ScrollViewer { Content = v, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    }

    private static FrameworkElement Section(string title, string? trailing, FrameworkElement content) =>
        K.V(0, Heading.Section(title, trailing, null, Ty.BodyStrong), content);

    // ---- the reply ----

    /// <summary>The reply's placeholder: to whom it goes.</summary>
    private static string Prompt(ConversationData d)
    {
        var people = (d.People ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries);
        return people.Length switch
        {
            0 => "Write a reply",
            1 => $"Reply to {people[0]}",
            _ => $"Reply to all {people.Length} people",
        };
    }

    /// <summary>Windows' Open dialog, any kind of file, several at once.</summary>
    private void ChooseFiles()
    {
        if (sending) return;
        var picked = Simpl.Tools.Files.Choose("All files|*.*", true, Window.GetWindow(outer), "Attach files");
        if (picked.Count > 0) files.Add(picked);
    }

    private bool CanSend => !sending && model.Data != null && reply.Text.Trim().Length > 0 && !files.Uploading;

    /// <summary>Send ready when there is something to send and every file is up; while it goes, a ring in its place; why
    /// it did not go, above.</summary>
    private void Bar()
    {
        send.IsEnabled = CanSend;
        attach.IsEnabled = !sending;
        send.ToolTip = files.Uploading ? "Send once the files are up" : "Send (Ctrl+Enter)";
        hint.Visibility = reply.Text.Length > 0 && !sending ? Visibility.Visible : Visibility.Collapsed;
        if (sending)
        {
            send.Icon = null;
            send.Content = K.Spinner(16);
        }
        else if (send.Content is not string)
        {
            send.Content = "Send";
            send.Icon = new SymbolIcon { Symbol = SymbolRegular.Send24 };
        }
        errorBar.Message = error ?? "";
        errorBar.IsOpen = error != null;
        errorBar.Visibility = error != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Send()
    {
        if (!CanSend) return;
        var text = reply.Text;
        sending = true;
        error = null;
        Bar();
        try
        {
            await engine.Call<OK>("sendReply", new { id = Id, body = text, attachments = files.Ids });
            reply.Text = "";
            files.Clear();
            model.Reload(engine, "conversation", new { id = Id });
            engine.DataChanged();
        }
        catch (Exception e)
        {
            error = e.Message;
        }
        sending = false;
        Bar();
    }

    /// <summary>Starred at once, and back if Canvas says no. Beside the Inbox's list, the list's own star does it (one star).</summary>
    public async void SetStarred(bool on)
    {
        if (link != null && link.Starred() != null)
        {
            link.Star(on);
            return;
        }
        if (model.Data is not { } d) return;
        d.Starred = on;
        PaintStar();
        Changed?.Invoke();
        if (await engine.Act("star", new { id = Id, on })) model.Reload(engine, "conversation", new { id = Id });
        else
        {
            d.Starred = !on;
            PaintStar();
            Changed?.Invoke();
        }
    }
}

/// <summary>A readable column: its one child at most so wide, at the left of its room or in the middle of it.</summary>
internal sealed class InboxColumn : Panel
{
    private double most = 840;
    private bool centre;

    public void Set(double most, bool centre)
    {
        if (Math.Abs(most - this.most) < 0.5 && centre == this.centre) return;
        this.most = most;
        this.centre = centre;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsInfinity(available.Width) ? most : Math.Min(most, available.Width);
        double h = 0;
        foreach (UIElement c in InternalChildren)
        {
            c.Measure(new Size(w, available.Height));
            h = Math.Max(h, c.DesiredSize.Height);
        }
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var w = Math.Min(most, final.Width);
        var x = centre ? Math.Max(0, (final.Width - w) / 2) : 0;
        foreach (UIElement c in InternalChildren) c.Arrange(new Rect(x, 0, w, final.Height));
        return final;
    }
}
