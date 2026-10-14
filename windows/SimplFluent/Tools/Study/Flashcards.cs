using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Screens;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

// Flashcards, the way the web's are (and Quizlet's) — the Mac's Flashcards.swift, its views. A set's page shows its four
// ways to study — Flashcards, Learn, Test, Match — over a big card that flips, with the list of terms beside it, each
// with a star.
//   Flashcards: one card at a time; flip it, then mark it Know or Still learning (the arrow keys do too); at the end, the
//   counts, and Keep Reviewing runs the ones still being learned again.
//   Learn: every card asked as multiple choice first, then typed from memory; two right in a row learns it (a wrong
//   answer at either step sends it back to the start). A learned card comes back for a typed check after a day, then at
//   growing gaps.
//   Test: up to twenty questions made from the set — multiple choice, true or false, written — marked at the end.
//   Match: up to six terms and their definitions as tiles; pair them up against the clock, and the best time is kept.

#pragma warning disable CA2255
internal static class FlashcardsRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("fc", c => new FlashcardsTool(c), c => new FlashcardsCompact(c));
}
#pragma warning restore CA2255

/// <summary>What the flashcards' views share: the tool's colour, the CSV files' filter.</summary>
internal static class FCKit
{
    public static Color Tint => StudyKit.ToolColor("fc", "#0a84ff");
    public static readonly Color Learn = K.Hex("#5856d6");
    public const string CsvFilter = "CSV and text files|*.csv;*.txt;*.tsv|All files|*.*";
    public const string CsvSave = "CSV file|*.csv";

    /// <summary>A file's text, or null when it cannot be read.</summary>
    public static string? Read(string path)
    {
        try { return File.ReadAllText(path); } catch (Exception e) { Log.Write($"flashcards: {e.Message}"); return null; }
    }

    public static void Save(string path, string text, Window? owner)
    {
        try { File.WriteAllText(path, text); }
        catch (Exception e) { Simpl.Shell.Dialogs.Fail("Couldn’t Save the File", e.Message, owner); }
    }

    /// <summary>A set's cards saved as a CSV of two columns, term and definition.</summary>
    public static void Export(FCDeck d, Window? owner)
    {
        if (Files.SaveAs(StudyKit.FileName(d.Name.Length == 0 ? "flashcards" : d.Name) + ".csv", CsvSave, owner) is { } path) Save(path, FCText.Csv(d), owner);
    }
}

/// <summary>
/// Flashcards (the Mac's FlashcardsTool): your sets; a set's page; its editor; and the four ways to study it — with where
/// you are among the sets and the way back across the top. Where you were is kept while the app runs, so the tool opened
/// again comes back to it.
/// </summary>
internal sealed class FlashcardsTool : ContentControl
{
    private static FCWhere last = FCWhere.Sets;
    private FCWhere where;
    private string? note;
    private readonly ToolContext c;
    private readonly ScrollViewer page;
    private readonly ContentControl bar = StudyKit.Host(), noteHost = StudyKit.Host(), content = StudyKit.Host();

    public FlashcardsTool(ToolContext c)
    {
        this.c = c;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        FlashcardDemo.SeedIfAsked();
        where = last.Deck != null && FlashcardStore.Deck(last.Deck) == null ? FCWhere.Sets : last;
        if (c.TakeInfo("deck") is { } id && FlashcardStore.Deck(id) != null) where = new FCWhere("page", id);
        if (Launch.Take("", "fc-demo") is { } demo) where = FlashcardDemo.Where(demo);
        last = where;
        page = ToolKit.Page(K.V(20, bar, noteHost, content), 1240);
        Content = page;
        Bar();
        Note();
        Show();
        StudyKit.Watch(this, h => FlashcardStore.Changed += h, h => FlashcardStore.Changed -= h, Bar);
    }

    private void Go(FCWhere to, string? withNote = null)
    {
        note = withNote;
        where = to;
        last = to;
        Bar();
        Note();
        Show();
        page.ScrollToTop();
    }

    /// <summary>
    /// Where you are among the sets, and the way back: in a set, "‹ Your sets" and the set's name; studying it, "‹ Cell
    /// Biology" and the way of studying alone ("Learn") — the set named once, on the way back. Inside a set the tool's line
    /// under its name ("Flip, learn, test, match.") makes way (ToolContext.ShowNote): the question wants the room.
    /// </summary>
    private void Bar()
    {
        c.ShowNote(where.Deck == null);
        if (where.Deck is not { } id || FlashcardStore.Deck(id) is not { } d) { StudyKit.Put(bar, null); return; }
        var back = K.Button(where.Kind is "page" or "edit" ? "Your sets" : d.Title, Back, icon: SymbolRegular.ChevronLeft24);
        back.MinWidth = 0;
        var title = K.Text(where.Kind switch
        {
            "cards" => "Flashcards",
            "learn" => "Learn",
            "test" => "Test",
            "match" => "Match",
            _ => d.Title,
        }, Ty.Subtitle);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(12, 0, 0, 0);
        StudyKit.Put(bar, K.Columns("auto, *", back.Col(0), title.Col(1)));
    }

    private void Back()
    {
        if (where.Kind is "page" or "edit") Go(FCWhere.Sets);
        else if (where.Deck is { } id) Go(new FCWhere("page", id));
    }

    private void Note() => StudyKit.Put(noteHost, note == null ? null : ToolKit.Note(note, SymbolRegular.Info24, K.Tone("blue")));

    private void Show()
    {
        var id = where.Deck ?? "";
        void Study(string mode) => Go(new FCWhere(mode is "cards" or "learn" or "test" ? mode : "match", id));
        void ToSet() => Go(new FCWhere("page", id));
        FrameworkElement view = where.Kind switch
        {
            "page" => new FCSetView(id, Study, () => Go(new FCWhere("edit", id))),
            "edit" => new FCEditView(id, ToSet, n => { note = n; Note(); }),
            "cards" => new FCRoundView(id, ToSet),
            "learn" => new FCLearnView(id, ToSet),
            "test" => new FCTestView(id, ToSet),
            "match" => new FCMatchView(id, ToSet),
            _ => new FCSetsView(d => Go(new FCWhere("page", d)), (d, n) => Go(new FCWhere("edit", d), n)),
        };
        content.Content = view;
        Motion.FadeIn(view, Motion.Medium);
    }
}

// ---- your sets ----

/// <summary>Your sets (the Mac's FCSetsView): Create a set, Import CSV…, Template, and a tile for each set.</summary>
internal sealed class FCSetsView : ContentControl
{
    private readonly Action<string> open;
    private readonly Action<string, string?> edit;

    public FCSetsView(Action<string> open, Action<string, string?> edit)
    {
        this.open = open;
        this.edit = edit;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Render();
        StudyKit.Watch(this, h => FlashcardStore.Changed += h, h => FlashcardStore.Changed -= h, Render);
    }

    private void Render()
    {
        var create = K.Button("Create a set", () => edit(FlashcardStore.Add(new FCDeck { Name = "Untitled set" }), null), accent: true, icon: SymbolRegular.Add24);
        var import = K.Button("Import CSV…", Import, icon: SymbolRegular.ArrowDownload24);
        var template = K.Button("Template", SaveTemplate, icon: SymbolRegular.DocumentTable24);
        template.ToolTip = "Save a CSV to fill in: two columns, term and definition";
        var buttons = new WrapPanel();
        foreach (var b in new[] { create, import, template })
        {
            b.Margin = new Thickness(0, 0, 10, 0);
            buttons.Children.Add(b);
        }
        var decks = FlashcardStore.Decks;
        FrameworkElement body = decks.Count == 0
            ? StudyKit.Unavailable(SymbolRegular.SquareMultiple24, "No sets yet", "Make one, paste your notes into one, or import a CSV with two columns: term, definition.", 260)
            : new MoreGrid(300, 16, decks.Select(d => (UIElement)Tile(d)), stretch: true);
        Content = K.V(22, buttons, body);
    }

    private FrameworkElement Tile(FCDeck d)
    {
        var name = K.Text(d.Title, Ty.BodyStrong);
        var count = K.Text(ToolKit.Plural(d.Cards.Count, "term"), Ty.Body, B.Text2).Margin(10, 0, 0, 0);
        var v = K.V(10, K.Columns("*, auto", name.Col(0), count.Col(1)));
        if (!string.IsNullOrEmpty(d.Desc)) v.Children.Add(K.Wrapped(d.Desc!, Ty.Body, B.Text2, 2));
        var pct = K.Text($"{d.LearnedPct}% learned", Ty.Caption, B.Text2).Margin(10, 0, 0, 0);
        v.Children.Add(K.Columns("*, auto", K.Bar(d.LearnedPct / 100.0, FCKit.Tint).Col(0), pct.Col(1)));
        var tile = K.CardButton(v, () => open(d.Id), 8, new Thickness(18));
        tile.VerticalContentAlignment = VerticalAlignment.Top;
        AutomationProperties.SetName(tile, $"{d.Title}, {ToolKit.Plural(d.Cards.Count, "term")}");
        tile.ContextMenu = MoreKit.Menu(
            ("Study", SymbolRegular.BookOpen24, () => open(d.Id)),
            ("Edit", SymbolRegular.Edit24, () => edit(d.Id, null)),
            ("Export CSV…", SymbolRegular.ArrowUpload24, () => FCKit.Export(d, Window.GetWindow(this))),
            null,
            ("Delete set", SymbolRegular.Delete24, () => FlashcardStore.Delete(d.Id)));
        return tile;
    }

    private void Import()
    {
        var owner = Window.GetWindow(this);
        if (Files.Choose(FCKit.CsvFilter, false, owner).FirstOrDefault() is not { } path || FCKit.Read(path) is not { } text) return;
        var cards = FCText.CardsFromCsv(text);
        if (cards.Count == 0) return;
        var id = FlashcardStore.Add(new FCDeck { Name = Path.GetFileNameWithoutExtension(path), Cards = cards });
        edit(id, $"{ToolKit.Plural(cards.Count, "card")} imported.");
    }

    private void SaveTemplate()
    {
        var owner = Window.GetWindow(this);
        if (Files.SaveAs("flashcard-template.csv", FCKit.CsvSave, owner) is not { } path) return;
        FCKit.Save(path, "Term,Definition\nPower rule,\"d/dx x^n = n*x^(n-1)\"\nChain rule,\"(f of g)' = f'(g(x))*g'(x)\"\n", owner);
    }
}

// ---- the card that flips ----

/// <summary>
/// The face of a card that flips (the Mac's FCFlipCard): the term, and the definition on its back, in the tool's wash.
/// A click or Space turns it over — the card folds flat and opens again on its other side.
/// </summary>
internal sealed class FCFlipCard : Grid
{
    private readonly FCCard card;
    private readonly Button button;
    private readonly FrameworkElement front, back;
    private readonly Border wash;
    private readonly ScaleTransform scale = new(1, 1);
    private bool flipped, turning;

    public event Action<bool>? Toggled;

    public FCFlipCard(FCCard card, bool flipped, double height)
    {
        this.card = card;
        this.flipped = flipped;
        front = Side("Term", card.Term, true);
        back = Side("Definition", card.Def, false);
        wash = new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(K.WithAlpha(FCKit.Tint, 0.1)), IsHitTestVisible = false };
        var face = new Grid { Children = { wash, front, back } };
        button = K.CardButton(face, Flip, 8, new Thickness(0));
        button.Height = height;
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        button.RenderTransform = scale;
        AutomationProperties.SetHelpText(button, "Flips the card");
        Children.Add(button);
        Paint();
    }

    public bool Flipped => flipped;

    /// <summary>The card turned over: folded flat (accelerating), its other side shown, opened again (decelerating).</summary>
    public void Flip()
    {
        if (!Motion.On)
        {
            flipped = !flipped;
            Paint();
            Toggled?.Invoke(flipped);
            return;
        }
        if (turning) return;
        turning = true;
        Motion.FromTo(scale, ScaleTransform.ScaleXProperty, 1, 0.97, 130, Motion.Accelerate);
        Motion.FromTo(scale, ScaleTransform.ScaleYProperty, 1, 0, 130, Motion.Accelerate, 0, () =>
        {
            flipped = !flipped;
            Paint();
            Toggled?.Invoke(flipped);
            Motion.FromTo(scale, ScaleTransform.ScaleXProperty, 0.97, 1, 220, Motion.Decelerate);
            Motion.FromTo(scale, ScaleTransform.ScaleYProperty, 0, 1, 220, Motion.Decelerate, 0, () => turning = false);
        });
    }

    private void Paint()
    {
        front.Visibility = flipped ? Visibility.Hidden : Visibility.Visible;
        back.Visibility = flipped ? Visibility.Visible : Visibility.Hidden;
        wash.Visibility = flipped ? Visibility.Visible : Visibility.Hidden;
        if (flipped) button.BorderBrush = new SolidColorBrush(K.WithAlpha(FCKit.Tint, 0.45));
        else button.SetResourceReference(Control.BorderBrushProperty, B.CardStroke);
        AutomationProperties.SetName(button, flipped ? $"Definition: {card.Def}" : $"Term: {card.Term}");
    }

    private static FrameworkElement Side(string label, string text, bool big)
    {
        var words = new TextBlock
        {
            Text = text.Length == 0 ? "—" : text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            FontSize = big ? 34 : 24, FontWeight = big ? FontWeights.SemiBold : FontWeights.Normal, FontFamily = big ? K.DisplayFace : K.TextFace,
        };
        words.Fg(B.Text);
        // (a long side shrinks to fit the card, its lines kept to the card's width)
        var fit = new Viewbox { Child = words, StretchDirection = StretchDirection.DownOnly, Stretch = Stretch.Uniform, Margin = new Thickness(30, 14, 30, 14) };
        var cap = StudyKit.Caps(label);
        cap.HorizontalAlignment = HorizontalAlignment.Center;
        var hint = K.Text("Click or press Space to flip", Ty.Caption, B.Text3).Center();
        var g = K.Rows("*, auto, auto, auto, *", cap.Row(1), fit.Row(2), hint.Row(3));
        g.Margin = new Thickness(0, 16, 0, 16);
        g.SizeChanged += (_, e) =>
        {
            words.MaxWidth = Math.Max(60, e.NewSize.Width - 60);
            fit.MaxHeight = Math.Max(40, e.NewSize.Height - 70);
        };
        return g;
    }
}

// ---- a set's page ----

/// <summary>
/// A set's page (the Mac's FCSetView): its four ways to study as tiles, a big card that flips (← and → move along it,
/// Space turns it), and the terms beside it, each with a star.
/// </summary>
internal sealed class FCSetView : ContentControl
{
    private static readonly (string Key, string Name, SymbolRegular Symbol, string Hex)[] Modes =
    {
        ("cards", "Flashcards", SymbolRegular.SquareMultiple24, "#0a84ff"), ("learn", "Learn", SymbolRegular.Flash24, "#5856d6"),
        ("test", "Test", SymbolRegular.DocumentQuestionMark24, "#ff9500"), ("match", "Match", SymbolRegular.Grid24, "#34c759"),
    };

    private readonly string deckID;
    private readonly Action<string> start;
    private readonly Action edit;
    private readonly ContentControl tiles = StudyKit.Host(), preview = StudyKit.Host(), terms = StudyKit.Host();
    private readonly FrameworkElement columns;
    private int idx;
    private bool flipped, focusedOnce;
    private FCFlipCard? card;
    private FrameworkElement? keys;

    public FCSetView(string deckID, Action<string> start, Action edit)
    {
        this.deckID = deckID;
        this.start = start;
        this.edit = edit;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        columns = ToolKit.Columns(K.V(20, tiles, preview), terms, 380, 860);
        Render();
        StudyKit.Watch(this, h => FlashcardStore.Changed += h, h => FlashcardStore.Changed -= h, Render);
    }

    private void Render()
    {
        if (FlashcardStore.Deck(deckID) is not { } d)
        {
            Content = StudyKit.EmptyNote("This set is gone.", SymbolRegular.SquareMultiple24);
            return;
        }
        Content = columns;
        Tiles(d);
        Preview(d);
        Terms(d);
    }

    private void Tiles(FCDeck d)
    {
        var due = d.Cards.Count(c => c.DueNow());
        var list = Modes.Select(m =>
        {
            var color = K.Hex(m.Hex);
            var v = K.V(10, K.IconTile(m.Symbol, color, 38).Align(HorizontalAlignment.Left, VerticalAlignment.Top), K.Text(m.Name, Ty.BodyStrong));
            if (m.Key == "learn" && due > 0)
            {
                var t = K.Text($"{due} to review", Ty.Caption).Strong();
                t.Foreground = new SolidColorBrush(color);
                v.Children.Add(t);
            }
            var key = m.Key;
            var b = K.CardButton(v, () => start(key), 8, new Thickness(16));
            b.VerticalContentAlignment = VerticalAlignment.Top;
            b.IsEnabled = d.Cards.Count > 0;
            AutomationProperties.SetName(b, m.Name);
            return (UIElement)b;
        });
        tiles.Content = new MoreGrid(150, 12, list, stretch: true);
    }

    private void Preview(FCDeck d)
    {
        var hadFocus = keys?.IsKeyboardFocusWithin == true;
        if (d.Cards.Count == 0)
        {
            keys = null;
            card = null;
            preview.Content = StudyKit.Unavailable(SymbolRegular.NoteEdit24, "No terms yet", "Press Edit to add some, or paste your notes in.", 280, K.Button("Edit", edit));
            return;
        }
        var i = Math.Min(idx, d.Cards.Count - 1);
        var c = d.Cards[i];
        card = new FCFlipCard(c, flipped, 340);
        card.Toggled += f => flipped = f;
        var shuffle = StudyKit.IconBtn(SymbolRegular.ArrowShuffle24, () =>
        {
            idx = 0;
            flipped = false;
            FlashcardStore.Change(deckID, x => x.Cards = StudyKit.Shuffled(x.Cards));
        }, "Shuffle the set");
        var prev = StudyKit.IconBtn(SymbolRegular.ChevronLeft24, () => Move(-1), "Previous card");
        prev.IsEnabled = i > 0;
        var count = StudyKit.Figure($"{i + 1} / {d.Cards.Count}", Ty.BodyStrong).Center();
        count.MinWidth = 70;
        var next = StudyKit.IconBtn(SymbolRegular.ChevronRight24, () => Move(1), "Next card");
        next.IsEnabled = i < d.Cards.Count - 1;
        var star = StudyKit.IconBtn(SymbolRegular.Star24, () => FlashcardStore.ChangeCard(deckID, c.Id, x => x.Star = !x.Star), c.Star ? "Unstar" : "Star", c.Star, c.Star ? StudyKit.Yellow : null);
        var row = K.Columns("auto, *, auto, auto, auto, *, auto", shuffle.Col(0), prev.Col(2), count.Margin(14, 0, 14, 0).Col(3), next.Col(4), star.Col(6));
        var v = K.V(14, card, row);
        v.Focusable = true;
        v.FocusVisualStyle = null;
        v.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Left: Move(-1); break;
                case Key.Right: Move(1); break;
                case Key.Space: card?.Flip(); break;
                default: return;
            }
            e.Handled = true;
        };
        keys = v;
        preview.Content = v;
        // (the card takes the keys as the page opens, and keeps them as it changes)
        if (hadFocus || !focusedOnce)
        {
            // (the first time, without scrolling the page down to it: WPF brings what takes the focus into view, and the
            //  set opened scrolled past its title and its four ways to study)
            var first = !focusedOnce;
            focusedOnce = true;
            void Stay(object sender, RequestBringIntoViewEventArgs e) => e.Handled = true;
            Dispatcher.BeginInvoke(() =>
            {
                if (keys != v || !v.IsVisible) return;
                if (first) v.RequestBringIntoView += Stay;
                v.Focus();
                Keyboard.Focus(v);
                if (first) v.RequestBringIntoView -= Stay;
            }, DispatcherPriority.Input);
        }
    }

    private void Move(int step)
    {
        if (FlashcardStore.Deck(deckID) is not { } d) return;
        idx = Math.Clamp(Math.Min(idx, d.Cards.Count - 1) + step, 0, Math.Max(0, d.Cards.Count - 1));
        flipped = false;
        Preview(d);
    }

    private void Terms(FCDeck d)
    {
        var edit = K.Link("Edit", this.edit);
        var head = Heading.Section("Terms in this set", d.Cards.Count.ToString(), edit);
        if (d.Cards.Count == 0)
        {
            terms.Content = K.V(0, head, K.Text("No terms yet.", Ty.Body, B.Text2));
            return;
        }
        var rows = K.V(0);
        for (var i = 0; i < d.Cards.Count; i++)
        {
            var c = d.Cards[i];
            var n = i;
            if (i > 0) rows.Children.Add(K.Divider(14));
            var words = K.V(3, K.Wrapped(c.Term, Ty.BodyStrong), K.Wrapped(c.Def, Ty.Body, B.Text2));
            var star = StudyKit.Star(c.Star, () => FlashcardStore.ChangeCard(deckID, c.Id, x => x.Star = !x.Star));
            star.VerticalAlignment = VerticalAlignment.Top;
            star.Margin = new Thickness(12, -4, -6, 0);
            var row = K.Subtle(K.Columns("*, auto", words.Col(0), star.Col(1)), () =>
            {
                idx = n;
                flipped = false;
                if (FlashcardStore.Deck(deckID) is { } dd) Preview(dd);
                keys?.Focus();
            }, new Thickness(14, 10, 14, 10));
            row.ToolTip = "Show this card";
            AutomationProperties.SetName(row, c.Term);
            rows.Children.Add(row);
        }
        terms.Content = K.V(0, head, K.Card(rows, new Thickness(4)));
    }
}

// ---- the editor ----

/// <summary>
/// A set's editor (the Mac's FCEditView): its title and line, Paste Terms (a box for your notes, one card a line),
/// Import and Export CSV, a row for each card — term, definition, delete — Add Card, and Done.
/// </summary>
internal sealed class FCEditView : ContentControl
{
    private readonly string deckID;
    private readonly Action done;
    private readonly Action<string?> note;
    private readonly ContentControl actions = StudyKit.Host(), pasteHost = StudyKit.Host(), rows = StudyKit.Host();
    private readonly Dictionary<string, TextBox> termBoxes = new(), defBoxes = new();
    private readonly TextBox? title, desc;
    private List<string> shown = new();
    private bool pasteOpen;
    private string paste = "";

    public FCEditView(string deckID, Action done, Action<string?> note)
    {
        this.deckID = deckID;
        this.done = done;
        this.note = note;
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        if (FlashcardStore.Deck(deckID) is not { } d) return;

        title = new TextBox { Text = d.Name, PlaceholderText = "Title", FontSize = 24, FontWeight = FontWeights.SemiBold, ClearButtonEnabled = false, FontFamily = K.DisplayFace };
        title.TextChanged += (_, _) => FlashcardStore.Change(deckID, x => x.Name = title!.Text, later: true);
        AutomationProperties.SetName(title, "Title");
        desc = new TextBox { Text = d.Desc ?? "", PlaceholderText = "Description (optional)", ClearButtonEnabled = false };
        desc.TextChanged += (_, _) => FlashcardStore.Change(deckID, x => x.Desc = desc!.Text, later: true);
        AutomationProperties.SetName(desc, "Description");
        var meta = K.V(10, title, desc, K.Divider());

        var add = K.Button("Add card", AddCard, icon: SymbolRegular.Add24);
        var finish = K.Button("Done", () => { FlashcardStore.Write(); done(); }, accent: true);
        finish.MinWidth = 80;
        var foot = K.Columns("auto, *, auto", add.Col(0), finish.Col(2));

        Content = K.V(18, meta, actions, pasteHost, rows, foot);
        Actions();
        Paste();
        Rows();
        StudyKit.Watch(this, h => FlashcardStore.Changed += h, h => FlashcardStore.Changed -= h, Changed);
        Unloaded += (_, _) => FlashcardStore.Write();
    }

    /// <summary>
    /// Typing changes the set without drawing it again; a card added or taken away does. Words changed elsewhere (the
    /// editor open twice: in the tool's popup and in its pin's flyout) are taken into the boxes not being typed in.
    /// </summary>
    private void Changed()
    {
        if (FlashcardStore.Deck(deckID) is not { } d) { Content = null; return; }
        if (!d.Cards.Select(c => c.Id).SequenceEqual(shown))
        {
            Actions();
            Rows();
            return;
        }
        static void Sync(TextBox? b, string v)
        {
            if (b != null && !b.IsKeyboardFocusWithin && b.Text != v) b.Text = v;
        }
        Sync(title, d.Name);
        Sync(desc, d.Desc ?? "");
        foreach (var c in d.Cards)
        {
            Sync(termBoxes.GetValueOrDefault(c.Id), c.Term);
            Sync(defBoxes.GetValueOrDefault(c.Id), c.Def);
        }
    }

    private void Actions()
    {
        var d = FlashcardStore.Deck(deckID);
        var toggle = K.Button(pasteOpen ? "Close paste" : "Paste terms", () => { pasteOpen = !pasteOpen; Actions(); Paste(); }, icon: SymbolRegular.ClipboardPaste24);
        var import = K.Button("Import CSV…", Import, icon: SymbolRegular.ArrowDownload24);
        var export = K.Button("Export CSV…", () => { if (FlashcardStore.Deck(deckID) is { } x) FCKit.Export(x, Window.GetWindow(this)); }, icon: SymbolRegular.ArrowUpload24);
        export.IsEnabled = d?.Cards.Count > 0;
        var row = new WrapPanel();
        foreach (var b in new[] { toggle, import, export })
        {
            b.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(b);
        }
        actions.Content = row;
    }

    private void Paste()
    {
        if (!pasteOpen) { StudyKit.Put(pasteHost, null); return; }
        var box = new TextBox
        {
            Text = paste, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, MinHeight = 130, MaxHeight = 220,
            VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, ClearButtonEnabled = false,
            PlaceholderText = "Mitochondria, makes the cell’s energy",
        };
        AutomationProperties.SetName(box, "Terms to paste");
        var found = FCText.CardsFromText(paste).Count;
        var count = K.Text(ToolKit.Plural(found, "card"), Ty.Body, B.Text2);
        count.VerticalAlignment = VerticalAlignment.Center;
        var addThese = K.Button("Add these cards", AddPasted, accent: true);
        addThese.IsEnabled = found > 0;
        box.TextChanged += (_, _) =>
        {
            paste = box.Text;
            var n = FCText.CardsFromText(paste).Count;
            count.Text = ToolKit.Plural(n, "card");
            addThese.IsEnabled = n > 0;
        };
        var v = K.V(10, box, K.Wrapped("One card a line. Put a tab, a comma or a dash between the term and the definition.", Ty.Caption, B.Text2), K.H(10, addThese, count));
        StudyKit.Put(pasteHost, v);
        Motion.Enter(v, 0, -10, 250);
    }

    private void AddPasted()
    {
        var add = FCText.CardsFromText(paste);
        paste = "";
        pasteOpen = false;
        FlashcardStore.Change(deckID, d => d.Cards.AddRange(add));
        Actions();
        Paste();
        note($"{ToolKit.Plural(add.Count, "card")} added.");
    }

    private void Rows()
    {
        termBoxes.Clear();
        defBoxes.Clear();
        if (FlashcardStore.Deck(deckID) is not { } d) return;
        shown = d.Cards.Select(c => c.Id).ToList();
        var v = K.V(0);
        if (d.Cards.Count == 0) v.Children.Add(K.Text("No cards yet.", Ty.Body, B.Text2).Margin(16));
        for (var i = 0; i < d.Cards.Count; i++)
        {
            if (i > 0) v.Children.Add(K.Divider(16));
            v.Children.Add(Row(i, d.Cards[i]));
        }
        rows.Content = K.Card(v, new Thickness(0, 4, 0, 4));
    }

    private FrameworkElement Row(int i, FCCard c)
    {
        var n = StudyKit.Figure($"{i + 1}", Ty.Body, brush: B.Text2);
        n.TextAlignment = TextAlignment.Right;
        n.Width = 28;
        n.VerticalAlignment = VerticalAlignment.Top;
        n.Margin = new Thickness(0, 6, 14, 0);
        var id = c.Id;
        var term = ToolKit.Box(c.Term, "Term", t => FlashcardStore.ChangeCard(deckID, id, x => x.Term = t, later: true));
        term.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetName(term, $"Term {i + 1}");
        termBoxes[id] = term;
        var def = ToolKit.Box(c.Def, "Definition", t => FlashcardStore.ChangeCard(deckID, id, x => x.Def = t, later: true));
        def.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetName(def, $"Definition {i + 1}");
        defBoxes[id] = def;
        var delete = K.IconButton(SymbolRegular.Delete24, () => FlashcardStore.Change(deckID, d => d.Cards.RemoveAll(x => x.Id == id)), "Delete card");
        delete.VerticalAlignment = VerticalAlignment.Top;
        delete.Margin = new Thickness(14, 0, 0, 0);
        var g = K.Columns("auto, *, 14, *, auto",
            n.Col(0),
            K.V(4, term, StudyKit.Caps("Term", B.Text3, 10.5)).Col(1),
            K.V(4, def, StudyKit.Caps("Definition", B.Text3, 10.5)).Col(3),
            delete.Col(4));
        g.Margin = new Thickness(14, 10, 14, 10);
        return g;
    }

    private void AddCard()
    {
        var c = new FCCard();
        FlashcardStore.Change(deckID, d => d.Cards.Add(c));
        if (shown.LastOrDefault() != c.Id) { Actions(); Rows(); }
        Dispatcher.BeginInvoke(() =>
        {
            if (!termBoxes.TryGetValue(c.Id, out var b)) return;
            b.Focus();
            Keyboard.Focus(b);
            b.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void Import()
    {
        var owner = Window.GetWindow(this);
        if (Files.Choose(FCKit.CsvFilter, false, owner).FirstOrDefault() is not { } path || FCKit.Read(path) is not { } text) return;
        var add = FCText.CardsFromCsv(text);
        if (add.Count == 0)
        {
            note("No cards found. Use two columns: term, definition.");
            return;
        }
        FlashcardStore.Change(deckID, d => d.Cards.AddRange(add));
        note($"{ToolKit.Plural(add.Count, "card")} imported.");
    }
}

// ---- the pin ----

/// <summary>Flashcards' pin, opened (the Mac's FlashcardsCompact): the sets as rows, a press opening one to study.</summary>
internal sealed class FlashcardsCompact : ContentControl
{
    private readonly ToolContext c;

    public FlashcardsCompact(ToolContext c)
    {
        this.c = c;
        Focusable = false;
        FlashcardDemo.SeedIfAsked();
        Render();
        StudyKit.Watch(this, h => FlashcardStore.Changed += h, h => FlashcardStore.Changed -= h, Render);
    }

    private void Render()
    {
        var decks = FlashcardStore.Decks;
        if (decks.Count == 0)
        {
            Content = K.Wrapped("No sets yet — the tool makes one from your notes.", Ty.Body, B.Text2);
            return;
        }
        var v = K.V(2);
        foreach (var d in decks.Take(5))
        {
            var id = d.Id;
            var count = K.Text(ToolKit.Plural(d.Cards.Count, "term"), Ty.Body, B.Text2).Margin(10, 0, 0, 0);
            var row = K.Subtle(K.Columns("*, auto", K.Text(d.Title, Ty.Body).Col(0), count.Col(1)), () => c.OpenFull(new Dictionary<string, string> { ["deck"] = id }, null), new Thickness(8, 9, 8, 9));
            row.ToolTip = $"Study {d.Title}";
            AutomationProperties.SetName(row, d.Title);
            v.Children.Add(row);
        }
        v.Margin = new Thickness(-8, 0, -8, 0);
        Content = v;
    }
}
