using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// New Message (the Mac's ComposeSheet), as a Windows 11 dialog over the window: who it is to (found as you type, in a
/// flyout under the field — the arrow keys and Enter choose, Escape puts it away — and each one a token that can be taken
/// out again, Backspace in the empty field taking the last), the course it is about (optional — it narrows the people
/// found to those in it), a subject and the message, each a line with its name at its start; Cancel and Send at the foot.
/// Ctrl+Enter sends; Escape cancels, asking first when something is written.
/// </summary>
public sealed class ComposeSheet
{
    private const double Side = 24, LabelWidth = 70, Gap = 12;

    private readonly Engine.Engine engine;
    private readonly string? context;
    private readonly Action? sent;
    private readonly Sheet sheet;
    private readonly List<Recipient> people;
    private string? course;
    private List<ComposeContext> contexts = new();
    private List<Recipient> found = new();
    /// <summary>The person found that Enter adds (the arrow keys and the pointer move it).</summary>
    private int highlighted;
    private bool sending, filling;
    private string? error;
    private int searches;
    /// <summary>The person just added (their token pops in).</summary>
    private string? added;

    private readonly TextBox toField, subject, message;
    private readonly WrapPanel tokens = new() { Orientation = Orientation.Horizontal };
    private readonly ComboBox courseBox = new() { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly FrameworkElement toRow, courseRow;
    private readonly Canvas overlay = new() { ClipToBounds = false };
    private readonly Border panel;
    private readonly Stack panelRows = new() { Orientation = Orientation.Vertical, Spacing = 0 };
    private readonly List<(Button B, FrameworkElement Plus)> panelItems = new();
    private readonly ContentControl status = new() { Focusable = false, IsTabStop = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly Wpf.Ui.Controls.Button cancelButton, sendButton;
    private readonly Grid confirm = new() { Visibility = Visibility.Collapsed };
    private readonly Border confirmCard;
    private Wpf.Ui.Controls.Button? keepButton;
    /// <summary>"Discard this message?" is up (not on its way out).</summary>
    private bool asking;

    /// <summary>A new message to anyone (the window's New Message, Ctrl+Shift+N, the Inbox's button).</summary>
    public static void Show(Engine.Engine engine, Window over) => Show(engine, over, Array.Empty<Recipient>());

    /// <summary>A new message to someone already chosen (a course's People), about a course ("course_101"), and what to do once it is sent.</summary>
    public static void Show(Engine.Engine engine, Window over, IReadOnlyList<Recipient> to, string? context = null, Action? sent = null)
    {
        var c = new ComposeSheet(engine, to, context, sent);
        Shell.Dialogs.Show(c.sheet, over);
    }

    private ComposeSheet(Engine.Engine engine, IReadOnlyList<Recipient> to, string? context, Action? sent)
    {
        this.engine = engine;
        this.context = context;
        this.sent = sent;
        people = to.ToList();
        course = context;

        // ---- the lines: To (its tokens over its field), Course, Subject ----
        toField = new TextBox { ClearButtonEnabled = false, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(toField, "To");
        toField.TextChanged += (_, _) => Search();
        toField.PreviewKeyDown += ToKey;
        var toStack = K.V(8, tokens, toField);
        toRow = Line("To:", toStack, top: true);
        toRow.SizeChanged += (_, _) => PlacePanel();

        courseBox.SelectionChanged += (_, _) =>
        {
            if (filling || courseBox.SelectedItem is not ComboBoxItem item) return;
            course = item.Tag as string;
            Search();
        };
        System.Windows.Automation.AutomationProperties.SetName(courseBox, "Course");
        courseRow = Line("Course:", courseBox);

        subject = new TextBox { PlaceholderText = "Optional", ClearButtonEnabled = false, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(subject, "Subject");
        subject.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; message!.Focus(); } };
        var subjectRow = Line("Subject:", subject);

        message = new TextBox
        {
            PlaceholderText = "Write your message", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, ClearButtonEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            MinHeight = 160,
        };
        System.Windows.Automation.AutomationProperties.SetName(message, "Message");
        message.TextChanged += (_, _) => Paint();

        // ---- the foot: what is happening, Cancel and Send ----
        cancelButton = K.Button("Cancel", Cancel);
        sendButton = K.Button("Send", Send, accent: true, icon: SymbolRegular.Send24);
        sendButton.ToolTip = "Send (Ctrl+Enter)";
        var footRow = K.Columns("*, auto, auto", status.Col(0), cancelButton.Margin(12, 0, 0, 0).Col(1), sendButton.Margin(8, 0, 0, 0).Col(2));
        var foot = new Border { Child = footRow, Padding = new Thickness(Side, 18, Side, 18), BorderThickness = new Thickness(0, 1, 0, 0) };
        foot.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
        foot.SetResourceReference(Border.BorderBrushProperty, "ContentDialogSeparatorBorderBrush");

        var title = K.Text("New Message", Ty.Subtitle).Margin(Side, 20, Side, 14);
        var body = K.Rows("auto, auto, auto, auto, auto, *, auto",
            title.Row(0),
            K.Divider().Row(1),
            toRow.Margin(Side, 14, Side, 6).Row(2),
            courseRow.Margin(Side, 6, Side, 6).Row(3),
            subjectRow.Margin(Side, 6, Side, 6).Row(4),
            message.Margin(Side, 6, Side, 18).Row(5),
            foot.Row(6));

        // ---- the people found, floating just under the To field over what is below it ----
        panel = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Effect = K.Shadow(24, 6, 0.22), Visibility = Visibility.Collapsed, Child = panelRows };
        panel.Bg(B.Flyout).Stroke(B.FlyoutStroke);
        overlay.Children.Add(panel);
        overlay.SizeChanged += (_, _) => PlacePanel();

        // ---- "Discard this message?" over the sheet ----
        confirmCard = DiscardCard();
        var smoke = new Border { Background = new SolidColorBrush(Color.FromArgb(Appearance.Dark ? (byte)110 : (byte)70, 0, 0, 0)) };
        smoke.MouseLeftButtonDown += (_, e) => e.Handled = true;
        confirm.Children.Add(smoke);
        confirm.Children.Add(confirmCard);

        var layers = new Grid { Children = { body, overlay, confirm } };
        layers.PreviewKeyDown += OnKey;
        // (someone already chosen: the typing starts at the subject)
        if (people.Count > 0) layers.Loaded += (_, _) => layers.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => subject.Focus());

        sheet = new Sheet { Body = layers, Bare = true, Modal = true, Width = 720, Height = 640 };
        ShowTokens();
        FillCourses();
        Paint();
        LoadContexts();
    }

    /// <summary>A line: its name at its start, as a mail app's are, then what is written there.</summary>
    private static FrameworkElement Line(string label, FrameworkElement field, bool top = false)
    {
        var name = K.Text(label, Ty.Body, B.Text2);
        name.TextAlignment = TextAlignment.Right;
        name.Width = LabelWidth;
        name.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center;
        if (top) name.Margin = new Thickness(0, 6, 0, 0);
        return K.Columns($"{LabelWidth}, {Gap}, *", name.Col(0), field.Col(2));
    }

    // ---- who it is to ----

    /// <summary>The people chosen as tokens over the field (which then asks for someone else).</summary>
    private void ShowTokens()
    {
        tokens.Children.Clear();
        foreach (var p in people) tokens.Children.Add(Token(p).Margin(0, 0, 6, 6));
        tokens.Visibility = people.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        tokens.Margin = new Thickness(0, 0, 0, -6);
        toField.PlaceholderText = people.Count == 0 ? "Search for a person or a course" : "Add someone else";
        Paint();
    }

    /// <summary>A person chosen: their name (a course's or a group's symbol before it), and the button that takes them out again.</summary>
    private FrameworkElement Token(Recipient p)
    {
        var name = K.Text(p.Name, Ty.Body);
        name.FontWeight = FontWeights.Medium;
        name.MaxWidth = 260;
        var remove = K.Subtle(K.Icon(SymbolRegular.DismissCircle24, 15, B.Text2, filled: true), () => Remove(p), new Thickness(0), 9);
        remove.Width = 20;
        remove.Height = 20;
        remove.Focusable = false;
        remove.HorizontalContentAlignment = HorizontalAlignment.Center;
        remove.ToolTip = $"Remove {p.Name}";
        System.Windows.Automation.AutomationProperties.SetName(remove, $"Remove {p.Name}");
        var row = K.H(4, p.Id.Contains('_') ? K.Icon(SymbolRegular.PeopleTeam24, 13, B.AccentText, filled: true) : null, name, remove);
        var accent = Appearance.Accent;
        var chip = new Border
        {
            Child = row, Padding = new Thickness(10, 2, 3, 2), CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.FromArgb(Appearance.Dark ? (byte)56 : (byte)36, accent.R, accent.G, accent.B)),
        };
        chip.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            var m = new ContextMenu();
            m.Items.Add(InboxUi.Item("Remove", SymbolRegular.Dismiss24, () => Remove(p)));
            InboxUi.Open(m, chip, atPointer: true);
        };
        if (p.Id == added) Motion.Pop(chip);
        return chip;
    }

    /// <summary>The people found and not yet added: the first six.</summary>
    private List<Recipient> Suggestions => found.Where(f => people.All(p => p.Id != f.Id)).Take(6).ToList();

    /// <summary>People found as the field is typed in (two letters at least; within the course chosen), the last search the one shown.</summary>
    private async void Search()
    {
        var q = toField.Text.Trim();
        var mine = ++searches;
        if (q.Length < 2)
        {
            found = new();
            ShowFound();
            return;
        }
        await Task.Delay(300); // (typed on meanwhile: this search gives way to the next)
        if (mine != searches) return;
        try
        {
            var r = await engine.Call<RecipientsData>("recipients", new { q, context = course });
            if (mine != searches) return;
            found = r.Rows;
            highlighted = 0;
            ShowFound();
        }
        catch { /* nothing found this time */ }
    }

    private void ShowFound()
    {
        var list = Suggestions;
        panelRows.Children.Clear();
        panelItems.Clear();
        highlighted = Math.Clamp(highlighted, 0, Math.Max(0, list.Count - 1));
        for (var i = 0; i < list.Count; i++)
        {
            if (i > 0) panelRows.Children.Add(K.Divider().Margin(50, 0, 8, 0));
            panelRows.Children.Add(Suggestion(list[i], i));
        }
        var on = list.Count > 0;
        if (on != (panel.Visibility == Visibility.Visible))
        {
            panel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) Motion.FadeIn(panel, Motion.Fast);
        }
        PaintHighlight();
        PlacePanel();
    }

    /// <summary>Someone found: a click (or Enter while it is marked) adds them; the pointer marks it.</summary>
    private Button Suggestion(Recipient r, int index)
    {
        // (a course, a group or a section reaches many people: its own symbol, not initials)
        FrameworkElement leading = r.Id.Contains('_') ? K.IconTile(SymbolRegular.PeopleTeam24, Appearance.Accent, 30) : InboxUi.Avatar(r.Name, null, 30);
        leading.VerticalAlignment = VerticalAlignment.Center;
        var words = K.V(1, K.Text(r.Name, Ty.Body));
        if (!string.IsNullOrEmpty(r.Sub)) words.Children.Add(K.Text(r.Sub!, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var plus = K.Icon(SymbolRegular.AddCircle24, 16, B.AccentText);
        var g = K.Columns("auto, *, auto", leading.Col(0), words.Margin(10, 0, 8, 0).Col(1), plus.Col(2));
        var b = K.Subtle(g, () => Add(r), new Thickness(8, 6, 10, 6), 6);
        b.Focusable = false;
        b.MouseEnter += (_, _) => { if (highlighted != index) { highlighted = index; PaintHighlight(); } };
        System.Windows.Automation.AutomationProperties.SetName(b, $"Add {r.Name}");
        panelItems.Add((b, plus));
        return b;
    }

    private void PaintHighlight()
    {
        var accent = Appearance.Accent;
        for (var i = 0; i < panelItems.Count; i++)
        {
            var on = i == highlighted;
            if (on) panelItems[i].B.Background = new SolidColorBrush(Color.FromArgb(Appearance.Dark ? (byte)56 : (byte)36, accent.R, accent.G, accent.B));
            else panelItems[i].B.ClearValue(Control.BackgroundProperty);
            panelItems[i].Plus.Opacity = on ? 1 : 0.5;
        }
    }

    /// <summary>The flyout just under the To field, from its words' edge.</summary>
    private void PlacePanel()
    {
        if (panel.Visibility != Visibility.Visible || !toRow.IsLoaded || !overlay.IsLoaded) return;
        Point p;
        try { p = toRow.TranslatePoint(new Point(0, toRow.ActualHeight), overlay); } catch { return; }
        var x = Side + LabelWidth + Gap - 8;
        Canvas.SetLeft(panel, x);
        Canvas.SetTop(panel, p.Y + 2);
        panel.Width = Math.Max(220, Math.Min(480, overlay.ActualWidth - x - Side));
    }

    private void Add(Recipient r)
    {
        if (people.All(p => p.Id != r.Id)) { people.Add(r); added = r.Id; }
        found = new();
        highlighted = 0;
        toField.Text = "";
        ShowTokens();
        added = null;
        ShowFound();
        toField.Focus();
    }

    private void Remove(Recipient r)
    {
        people.RemoveAll(p => p.Id == r.Id);
        ShowTokens();
        ShowFound();
    }

    /// <summary>The To field's keys: the arrows move the mark, Enter adds the person marked (with nothing typed, on to the
    /// subject), Backspace in the empty field takes out the last person, as a token field does.</summary>
    private void ToKey(object sender, KeyEventArgs e)
    {
        var list = Suggestions;
        switch (e.Key)
        {
            case Key.Down when list.Count > 0:
                highlighted = Math.Min(highlighted + 1, list.Count - 1);
                PaintHighlight();
                e.Handled = true;
                break;
            case Key.Up when list.Count > 0:
                highlighted = Math.Max(highlighted - 1, 0);
                PaintHighlight();
                e.Handled = true;
                break;
            case Key.Enter:
                if (highlighted >= 0 && highlighted < list.Count) Add(list[highlighted]);
                else if (toField.Text.Trim().Length == 0 && people.Count > 0) subject.Focus();
                e.Handled = true;
                break;
            case Key.Back when toField.Text.Length == 0 && people.Count > 0:
                Remove(people[^1]);
                e.Handled = true;
                break;
        }
    }

    // ---- the course ----

    /// <summary>Any course, or one of the student's (and "This Course" for the one it was opened from, if it is not among them).</summary>
    private void FillCourses()
    {
        filling = true;
        courseBox.Items.Clear();
        courseBox.Items.Add(new ComboBoxItem { Content = "Any Course", Tag = null });
        if (context != null && contexts.All(c => c.Code != context)) courseBox.Items.Add(new ComboBoxItem { Content = "This Course", Tag = context });
        foreach (var c in contexts) courseBox.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Code });
        courseBox.SelectedItem = courseBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == course) ?? courseBox.Items[0];
        filling = false;
        courseRow.Visibility = contexts.Count > 0 || context != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void LoadContexts()
    {
        try
        {
            var c = await engine.Call<ComposeContextsData>("composeContexts");
            contexts = c.Rows;
            FillCourses();
            if (courseRow.Visibility == Visibility.Visible) Motion.FadeIn(courseRow, Motion.Normal);
        }
        catch { /* no courses to offer: the line stays away */ }
    }

    // ---- sending ----

    private bool CanSend => !sending && people.Count > 0 && message.Text.Trim().Length > 0;

    /// <summary>Send ready once it is to someone and says something; while it goes, a word that it is going; why it did not, if not.</summary>
    private void Paint()
    {
        sendButton.IsEnabled = CanSend;
        cancelButton.IsEnabled = !sending;
        if (sending) status.Content = K.H(8, K.Spinner(16), K.Text("Sending…", Ty.Body, B.Text2));
        else if (error != null)
        {
            var words = K.Wrapped(error, Ty.Body, B.Critical, 2);
            status.Content = K.Columns("auto, *", K.Icon(SymbolRegular.Warning24, 16, B.Critical, filled: true).Margin(0, 2, 8, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Top).Col(0), words.Col(1));
        }
        else status.Content = null;
    }

    private async void Send()
    {
        if (!CanSend) return;
        sending = true;
        error = null;
        Paint();
        try
        {
            await engine.Call<OK>("sendMessage", new { recipients = people.Select(p => p.Id).ToList(), subject = subject.Text, body = message.Text, context = course });
            sending = false;
            sent?.Invoke();
            engine.DataChanged();
            sheet.Close();
            return;
        }
        catch (Exception e)
        {
            error = e.Message;
        }
        sending = false;
        Paint();
    }

    /// <summary>Cancel (Escape): gone at once when nothing is written, else asked first. Not while it is sending.</summary>
    private void Cancel()
    {
        if (sending) return;
        if (message.Text.Trim().Length == 0) { sheet.Close(); return; }
        asking = true;
        confirm.IsHitTestVisible = true;
        confirm.Visibility = Visibility.Visible;
        Motion.FadeIn(confirm, Motion.Normal);
        Motion.Pop(confirmCard);
        confirm.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => keepButton?.Focus());
    }

    /// <summary>"Discard this message? It has not been sent." — Discard, or Cancel to go on writing.</summary>
    private Border DiscardCard()
    {
        var discard = K.Button("Discard", () => sheet.Close(), accent: true);
        discard.HorizontalAlignment = HorizontalAlignment.Stretch;
        keepButton = K.Button("Cancel", KeepWriting);
        keepButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        var buttons = K.Columns("*, 8, *", discard.Col(0), keepButton.Col(2));
        var v = K.V(12, K.Wrapped("Discard this message?", Ty.Subtitle), K.Wrapped("It has not been sent.", Ty.Body, B.Text2), buttons.Margin(0, 12, 0, 0));
        var card = new Border
        {
            Child = v, Width = 340, Padding = new Thickness(24), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = K.Shadow(40, 12, 0.28),
        };
        card.SetResourceReference(Border.BackgroundProperty, "ContentDialogBackground");
        card.SetResourceReference(Border.BorderBrushProperty, "ContentDialogBorderBrush");
        return card;
    }

    /// <summary>Back to writing: the question goes as the sheet's own dialogs do — the smoke fading, the card a touch larger
    /// as it fades (SheetHost.Close) — and the typing goes on meanwhile.</summary>
    private void KeepWriting()
    {
        asking = false;
        confirm.IsHitTestVisible = false;
        message.Focus();
        Motion.FadeOut(confirm, () =>
        {
            if (asking) return; // (asked again while it was going)
            confirm.BeginAnimation(UIElement.OpacityProperty, null);
            confirm.Visibility = Visibility.Collapsed;
        }, Motion.Normal);
        Motion.Unpop(confirmCard, () => { });
    }

    /// <summary>The sheet's keys: Escape puts away the people found (the sheet stays), else cancels — or, while asked about
    /// discarding, goes back to writing; Ctrl+Enter sends.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (asking)
        {
            if (e.Key == Key.Escape) { KeepWriting(); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (Suggestions.Count > 0) { found = new(); ShowFound(); }
            else Cancel();
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            Send();
        }
    }
}
