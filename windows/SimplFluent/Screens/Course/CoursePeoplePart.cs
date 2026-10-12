using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Shell;
using Wpf.Ui.Controls;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// The people of a course or a group by role, teachers first (the Mac's PeopleList): each with their picture and
/// pronouns, and a Message button (and menu item) that writes to them — in as many columns as fit, 260 wide.
/// </summary>
internal sealed class CoursePeoplePart : CourseListPart<PeopleData>
{
    public CoursePeoplePart(Engine.Engine engine, string ctx) : base(engine, ctx, "people", "people", "People") { }

    protected override string? Context(PeopleData d) => d.Context;

    /// <summary>How many people sit side by side in a card on a page this wide (the card's own padding taken off).</summary>
    private static int Cols(double w) => Math.Max(1, (int)Math.Floor((Math.Max(280, w) - 22 + 10) / (260 + 10)));

    public override bool LayoutChanges(double before, double now) => Cols(before) != Cols(now);

    protected override FrameworkElement Content(PeopleData d)
    {
        var v = K.V(22);
        if (d.Sections.Count == 0) v.Children.Add(CourseKit.EmptyCard(d.Empty ?? "Nobody to show", SymbolRegular.People24));
        foreach (var s in d.Sections) v.Children.Add(CourseKit.CardSection(s.Title, Count(s.Rows.Count), null, PeopleGrid(s.Rows)));
        return v;
    }

    private FrameworkElement PeopleGrid(List<PersonRow> rows)
    {
        var cols = Cols(Width());
        var g = new Grid();
        for (var i = 0; i < cols; i++)
        {
            if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        var n = (rows.Count + cols - 1) / cols;
        for (var r = 0; r < n; r++)
        {
            if (r > 0) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var k = 0; k < rows.Count; k++)
        {
            var p = Person(rows[k]);
            Grid.SetColumn(p, (k % cols) * 2);
            Grid.SetRow(p, (k / cols) * 2);
            g.Children.Add(p);
        }
        return g;
    }

    private FrameworkElement Person(PersonRow p)
    {
        var words = K.V(1, K.Text(p.Name, Ty.Body));
        if (!string.IsNullOrEmpty(p.Pronouns)) words.Children.Add(K.Text(p.Pronouns!, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var mail = K.IconButton(SymbolRegular.Mail24, () => Write(p), $"Message {p.Name}", 16, 30);
        mail.Height = 30;
        var g = K.Columns("auto, *, auto", CourseKit.Avatar(p.Name, p.Avatar, 38).Margin(0, 0, 12, 0).Col(0), words.Col(1), mail.Margin(4, 0, 0, 0).Col(2));
        g.Margin = new Thickness(8, 7, 8, 7);
        g.Background = Brushes.Transparent;
        CourseKit.Menu(g, items => items.Add(CourseKit.MenuEntry($"Message {p.Name}…", SymbolRegular.MailEdit24, () => Write(p))));
        return g;
    }

    private void Write(PersonRow p) =>
        CourseCompose.Show(engine, new Recipient { Id = p.Id, Name = p.Name }, ctx.StartsWith("courses/", StringComparison.Ordinal) ? $"course_{CourseKit.IdOf(ctx)}" : null);
}

/// <summary>
/// Writing to someone from a course's People (the Mac's ComposeSheet, addressed to them): New Message, to them, about
/// the course, with a subject if you like and the message — Send, or Cancel (which asks first once something is
/// written). Where the Inbox's own New Message is wired in (<see cref="Hook"/>), it is that instead.
/// </summary>
public static class CourseCompose
{
    /// <summary>The Inbox's New Message, addressed to one person about a course (its context code, "course_101", or null).</summary>
    public static Action<Recipient, string?>? Hook;

    public static void Show(Engine.Engine engine, Recipient to, string? context)
    {
        if (Hook is { } hook) { hook(to, context); return; }
        var token = new Border
        {
            Child = K.Text(to.Name, Ty.Body).Strong(), Padding = new Thickness(10, 3, 10, 4), CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(K.WithAlpha(Support.Appearance.Accent, Support.Appearance.Dark ? 0.3 : 0.16)),
        };
        var subject = new TextBox { PlaceholderText = "Optional", MaxLength = 255 };
        var message = new TextBox
        {
            PlaceholderText = "Write your message", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180, MaxHeight = 320,
            VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var status = new ContentControl { Focusable = false };
        FrameworkElement Line(string label, UIElement field) =>
            K.Columns("70, *", K.Text(label, Ty.Body, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Margin(0, 0, 12, 0).Col(0), field.Col(1));
        var body = K.V(14, Line("To:", token), Line("Subject:", subject), message, status);
        var sheet = new Sheet { Title = "New Message", Body = body, Width = 620 };
        var sending = false;
        var confirming = false;
        SheetButton? send = null, cancel = null;
        void Status(UIElement? e) => status.Content = e;
        void CanSend() { if (send != null) send.Enabled = !sending && message.Text.Trim().Length > 0; }
        async void Send()
        {
            if (sending || message.Text.Trim().Length == 0) return;
            sending = true;
            CanSend();
            Status(K.H(8, K.Spinner(16), K.Text("Sending…", Ty.Caption, B.Text2)));
            try
            {
                var args = new Dictionary<string, object> { ["recipients"] = new[] { to.Id }, ["subject"] = subject.Text, ["body"] = message.Text };
                if (context != null) args["context"] = context;
                await engine.Call<OK>("sendMessage", args);
                engine.DataChanged();
                sheet.Close();
            }
            catch (Exception e)
            {
                var why = K.Wrapped(e.Message, Ty.Caption);
                why.Foreground = new SolidColorBrush(K.Tone("red"));
                Status(K.Columns("auto, *", K.Icon(SymbolRegular.Warning24, 14, K.Tone("red"), filled: true).Margin(0, 1, 8, 0).Col(0), why.Col(1)));
            }
            sending = false;
            CanSend();
        }
        void Cancel()
        {
            if (sending) return;
            // (something written: asked first, as the Mac's "Discard this message?")
            if (message.Text.Trim().Length == 0 || confirming) { sheet.Close(); return; }
            confirming = true;
            cancel?.Relabel("Discard");
            Status(K.Text("Discard this message? It has not been sent.", Ty.Caption, B.Text2));
        }
        send = sheet.Add("Send", SheetButtonKind.Primary, Send, closes: false);
        cancel = sheet.Add("Cancel", SheetButtonKind.Close, Cancel, closes: false);
        message.TextChanged += (_, _) =>
        {
            CanSend();
            if (confirming) { confirming = false; cancel?.Relabel("Cancel"); Status(null); }
        };
        // (Ctrl+Enter sends, as ⌘Return does on the Mac; Enter in the subject goes on to the message)
        message.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0) { e.Handled = true; Send(); }
        };
        subject.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; message.Focus(); } };
        body.Loaded += (_, _) => CanSend();
        Dialogs.Show(sheet);
    }
}
