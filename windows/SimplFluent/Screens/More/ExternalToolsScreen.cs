using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Shell;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Screens;

/// <summary>
/// External Tools, in the navigation pane under Tools (the Mac's ExternalToolsView): every tool the school provides in
/// the student's courses — a course's own (its navigation's) and those found only in its assignments and modules — a card
/// a tool, in columns that fill as a masonry does (each card at the foot of the shortest column so far): its letter, its
/// name, its site and where it is found. A tool used in work — Knewton Alta's assignments, a publisher's module items —
/// is a button whole: it says the next of them still to do, and a press opens the list of every place it is used, to
/// choose the one to open (ExternalToolsList.cs). A tool that is only a course's tab has each course it is in a press
/// away, as its chips. Knewton Alta opens in its own window, the rest in the tool window. The filter
/// beside the title narrows the tools by name, site or course.
/// </summary>
public sealed partial class ExternalToolsScreen : Screen
{
    /// <summary>What the page's `externalTools` gives (the Mac's ExternalToolsData).</summary>
    internal sealed class Data
    {
        public List<Tool> Rows { get; set; } = new();
        public string? Sub { get; set; }
        public string? Empty { get; set; }
    }

    internal sealed class Tool
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Its site — empty when the tool is known only by it (its name says it already).</summary>
        public string Host { get; set; } = "";
        public List<string> Kinds { get; set; } = new();
        public int Assignments { get; set; }
        /// <summary>How many places it is used: its assignments and its module items, in every course.</summary>
        public int Instances { get; set; }
        /// <summary>The soonest of its assignments still to do, if one is.</summary>
        public Soonest? Next { get; set; }
        public List<Course> Courses { get; set; } = new();
    }

    /// <summary>A tool's next piece of work still to do: its title, the line under it ("Due Oct 18 at 11:59 PM · 20 pts")
    /// and its course's name.</summary>
    internal sealed class Soonest
    {
        public string Title { get; set; } = "";
        /// <summary>(2.99.33) Its title with the tool's name taken off ("Unit 2" for "Knewton Alta: Unit 2").</summary>
        public string? Short { get; set; }
        public string? Sub { get; set; }
        public string? Course { get; set; }
    }

    internal sealed class Course
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Color { get; set; } = "";
        /// <summary>How it opens in this course: `course` with `tool` (its tab), `assignment` or `moduleItem`.</summary>
        public Dictionary<string, string> Open { get; set; } = new();
        /// <summary>Every place the tool is used in the course, in the order shown: its assignments still to do, the soonest
        /// first, then those with no date, then the rest, the latest first; then its module items.</summary>
        public List<Item> Items { get; set; } = new();
    }

    /// <summary>A place a tool is used: an assignment (`assignment`) or a module item (`moduleItem`), the line under its
    /// title ("Due Oct 18 at 11:59 PM · 20 pts", "In Week 2: Forces"), where the work stands (as every list of work says
    /// it), and whether it is still to do and due ahead.</summary>
    internal sealed class Item
    {
        public string Kind { get; set; } = "";
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        /// <summary>(2.99.33) Its title with the tool's name taken off ("Unit 2").</summary>
        public string? Short { get; set; }
        public string? Due { get; set; }
        public string? Sub { get; set; }
        public Simpl.Models.Status? Status { get; set; }
        public bool Upcoming { get; set; }
    }

    /// <summary>The narrowest a card is laid out, and the room between cards (across and down alike).</summary>
    private const double MinCard = 360, Gap = 16;

    private static readonly Color Gray = Color.FromRgb(0x8e, 0x8e, 0x93);

    private readonly Loader<Data> model;
    private TextBox? filter;
    private string query = "";
    private bool hadRows;
    private string? lastSub;
    /// <summary>The cards as drawn, by tool: where the keyboard goes back to as a tool's list closes.</summary>
    private readonly Dictionary<string, FrameworkElement> cards = new();

    public ExternalToolsScreen(Engine.Engine engine) : base(engine) => model = Watch(new Loader<Data>());

    public override string Title => "External tools";

    public override string? Subtitle => model.Data is { Rows.Count: > 0 } d ? d.Sub : null;

    /// <summary>The filter, beside the title once there are tools (the search box is the app's own).</summary>
    public override FrameworkElement? Commands
    {
        get
        {
            if (model.Data is not { Rows.Count: > 0 }) return null;
            if (filter != null) return filter;
            filter = new TextBox
            {
                PlaceholderText = "Filter tools", Icon = new SymbolIcon { Symbol = SymbolRegular.Filter24 }, IconPlacement = ElementPlacement.Left,
                Width = 220, MinHeight = 32, VerticalAlignment = VerticalAlignment.Center,
            };
            filter.TextChanged += (_, _) =>
            {
                if (filter.Text == query) return;
                query = filter.Text;
                Render();
            };
            return filter;
        }
    }

    public override void Refresh() => model.Ensure(engine, "externalTools");

    /// <summary>Gone from view: a list open folds away with it.</summary>
    public override void Hide()
    {
        base.Hide();
        CloseList();
    }

    /// <summary>Two columns once two cards of <see cref="MinCard"/> fit side by side, three once three do; else one.</summary>
    private static int Columns(double width) => width >= 3 * MinCard + 2 * Gap ? 3 : width >= 2 * MinCard + Gap ? 2 : 1;

    /// <summary>Built again only when the columns change: within a column count the cards simply take the width.</summary>
    protected override bool LayoutChanges(double before, double now) => Columns(before) != Columns(now);

    protected override FrameworkElement Build()
    {
        var has = model.Data is { Rows.Count: > 0 };
        if (has != hadRows || Subtitle != lastSub) { hadRows = has; lastSub = Subtitle; HeaderChange(); }
        cards.Clear();
        if (model.Data is not { } d) { CloseList(); return K.LoadState(model.Error, () => model.Reload(engine, "externalTools")); }
        if (d.Rows.Count == 0)
        {
            CloseList();
            return MoreKit.Unavailable(SymbolRegular.PuzzlePiece24, "No external tools", d.Empty ?? "None of your courses has an external tool.", 420);
        }
        var shown = Filtered(d.Rows);
        if (shown.Count == 0)
        {
            CloseList();
            return K.Text($"No tool matches “{query.Trim()}”.", Ty.Body, B.Text2);
        }
        var page = new Masonry(Columns(Width), Gap);
        foreach (var t in shown) page.Children.Add(cards[t.Id] = Card(t));
        Rehang(shown);
        LaunchList(shown);
        return page;
    }

    private List<Tool> Filtered(List<Tool> rows)
    {
        var q = query.Trim();
        if (q.Length == 0) return rows;
        bool Has(string? s) => s?.Contains(q, StringComparison.CurrentCultureIgnoreCase) == true;
        return rows.Where(t => Has(t.Name) || Has(t.Host) || t.Courses.Any(c => Has(c.Name))).ToList();
    }

    /// <summary>The tool by its id, as the page last said it.</summary>
    private Tool? ToolOf(string id) => model.Data?.Rows.FirstOrDefault(t => t.Id == id);

    /// <summary>A tool used in work (its assignments, its module items): its card opens the list of them.</summary>
    private static bool Chooser(Tool t) => t.Instances > 0 && t.Courses.Any(c => c.Items.Count > 0);

    // ---- a card ----

    /// <summary>
    /// A tool: its letter on its first course's colour, its name, its site (when it has one of its own) and where it is
    /// found. A tool used in work is a button whole: under its head the next of its work still to do, the courses whose
    /// tab it also is (each opens that tab, not the list), and at its foot how many places it is used, with the chevron
    /// that turns over while their list is open. Any other tool is a card with each course it is in, a press away.
    /// </summary>
    private FrameworkElement Card(Tool t)
    {
        // (the tool's name the card's strongest words — a size up from the body, semibold — over its course chips, which
        //  are regular: a wall of course names must not outweigh what the card is about)
        var name = K.Wrapped(t.Name, Ty.BodyStrong, B.Text, 2);
        name.FontSize = 16;
        name.LineHeight = 22;
        name.MaxHeight = 2 * 22 + 1;
        var words = K.V(2, name);
        if (!string.IsNullOrEmpty(t.Host)) words.Children.Add(K.Text(t.Host, Ty.Caption, B.Text2));
        words.Children.Add(K.Wrapped(WhereFound(t), Ty.Caption, B.Text2, 2));
        var head = K.Columns("auto, *", Monogram(t, 40).Margin(0, 0, 12, 0).Col(0), words.Col(1));
        var body = K.V(12, head);
        if (!Chooser(t))
        {
            body.Children.Add(Chips(t, t.Courses));
            return K.Card(body, new Thickness(16));
        }
        if (t.Next is { Title.Length: > 0 } n) body.Children.Add(NextLine(t, n));
        var tabs = t.Courses.Where(c => c.Open.ContainsKey("tool")).ToList();
        if (t.Kinds.Contains("Course navigation") && tabs.Count > 0) body.Children.Add(Chips(t, tabs));
        body.Children.Add(Foot(t));
        PressCard card = null!;
        card = new PressCard(body, () => ToggleList(t.Id, card));
        var noun = Noun(t);
        card.ToolTip = $"Choose which {noun} to open";
        AutomationProperties.SetName(card, $"{t.Name}, {Places(t)}");
        AutomationProperties.SetHelpText(card, $"Opens the list to choose which {noun} to open");
        return card;
    }

    /// <summary>A tool's letter, white, on a rounded square of its first course's colour (its card's head, and its list's).</summary>
    private static Border Monogram(Tool t, double size)
    {
        var tint = K.Hex(t.Courses.FirstOrDefault()?.Color, Gray);
        var letter = new TextBlock
        {
            Text = t.Name.Length > 0 ? t.Name[..1].ToUpperInvariant() : "•", FontSize = Math.Round(size * 0.48), FontWeight = FontWeights.Bold, FontFamily = K.DisplayFace,
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 5), Child = letter, VerticalAlignment = VerticalAlignment.Top,
            Background = new LinearGradientBrush(K.Mix(tint, Colors.White, 0.12), K.Mix(tint, Colors.Black, 0.12), 90),
        };
    }

    /// <summary>The next of the tool's work still to do, on a quiet wash across the card: its course's dot, its title, and
    /// — after the Next pill its list wears too — when it is due, in which course.</summary>
    private static FrameworkElement NextLine(Tool t, Soonest n)
    {
        var dot = K.Dot(K.Hex(t.Courses.FirstOrDefault(c => c.Name == n.Course)?.Color, Gray), 8);
        dot.VerticalAlignment = VerticalAlignment.Top;
        dot.Margin = new Thickness(0, 6, 10, 0);
        var title = K.Text(n.Short is { Length: > 0 } s ? s : Short(t, n.Title), Ty.Body);
        // (when it is due, without its points: the line is about what comes next)
        var when = string.Join(" · ", new[] { (n.Sub ?? "").Split(" · ")[0], n.Course }.Where(s => !string.IsNullOrEmpty(s)));
        var line = K.Columns("auto, *", K.Chip("Next", Appearance.Accent, strong: true).Margin(0, 0, 6, 0).Col(0), K.Text(when, Ty.Caption, B.Text2).Col(1));
        line.Margin = new Thickness(0, 2, 0, 0);
        var well = new Border
        {
            Child = K.Columns("auto, *", dot.Col(0), K.V(1, title, line).Col(1)),
            Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(6),
        };
        return well.Bg(B.Subtle);
    }

    /// <summary>A card's foot, under a hairline: how many places the tool is used, and the chevron of its list.</summary>
    private FrameworkElement Foot(Tool t)
    {
        // (0.3.100: to the right — the list opens in a popup of its own, no longer under the card)
        var chevron = K.Icon(SymbolRegular.ChevronRight20, 12, B.Text2);
        var row = K.Columns("*, auto", K.Text(Places(t), Ty.Caption, B.Text2).Col(0), chevron.Col(1));
        return K.V(10, K.Divider(), row);
    }

    /// <summary>The courses' chips, as many to a line as fit: none wider than the card's inside, a long course's name
    /// ending in an ellipsis.</summary>
    private FrameworkElement Chips(Tool t, IEnumerable<Course> courses)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, -6) };
        foreach (var c in courses) wrap.Children.Add(CoursePill(t, c));
        // (each chip kept to the width the card has inside, as that width changes)
        wrap.SizeChanged += (_, e) =>
        {
            foreach (FrameworkElement chip in wrap.Children) chip.MaxWidth = Math.Max(0, e.NewSize.Width - chip.Margin.Left - chip.Margin.Right);
        };
        return wrap;
    }

    /// <summary>A course the tool is in: its dot, its name (what room there is, in regular weight under the tool's name),
    /// and the symbol of a Simpl window of its own — the one the tool opens in (a course's More and a module's tool wear it
    /// too; ↗ would say it leaves for the browser). A press opens the tool there (on a card that opens a list, that press
    /// is the chip's alone).</summary>
    private FrameworkElement CoursePill(Tool t, Course c)
    {
        var name = K.Text(c.Name, Ty.Caption);
        var row = K.Columns("auto, *, auto",
            K.Dot(K.Hex(c.Color), 7).Margin(0, 0, 6, 0).Col(0),
            name.Col(1),
            CourseKit.OwnWindowMark().Margin(6, 0, 0, 0).Col(2));
        // (K.Subtle marks its press handled: the card round it is not pressed, nor opened, by it)
        var b = K.Subtle(row, () => { CloseList(); Open(engine, t, c); }, new Thickness(10, 5, 10, 6), 12);
        b.SetResourceReference(Control.BackgroundProperty, B.Subtle3);
        b.Stroke(B.ControlStroke);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.Margin = new Thickness(0, 0, 6, 6);
        b.ToolTip = $"Open {t.Name} in {c.Name}";
        AutomationProperties.SetName(b, $"Open {t.Name} in {c.Name}");
        return b;
    }

    /// <summary>Where it was found: in how many courses, a course's navigation; for a tool whose card is not a list, its
    /// assignments (how many) and its modules — a list's card says how many places at its foot.</summary>
    private static string WhereFound(Tool t)
    {
        var parts = new List<string> { Simpl.Tools.ToolKit.Plural(t.Courses.Count, "course") };
        if (t.Kinds.Contains("Course navigation")) parts.Add("Course navigation");
        if (!Chooser(t))
        {
            if (t.Assignments > 0) parts.Add(Simpl.Tools.ToolKit.Plural(t.Assignments, "assignment"));
            if (t.Kinds.Contains("Modules")) parts.Add("Modules");
        }
        return string.Join(" · ", parts);
    }

    /// <summary>How many places a tool is used: "5 assignments", "3 module items", or "5 places" when it is both.</summary>
    private static string Places(Tool t)
    {
        var items = t.Courses.SelectMany(c => c.Items).ToList();
        var n = items.Count;
        var assignments = items.Count(i => i.Kind != "moduleItem");
        return assignments == n ? Simpl.Tools.ToolKit.Plural(n, "assignment")
            : assignments == 0 ? Simpl.Tools.ToolKit.Plural(n, "module item")
            : Simpl.Tools.ToolKit.Plural(n, "place");
    }

    /// <summary>What a tool's list chooses between: an assignment, a module item, or one of either.</summary>
    private static string Noun(Tool t)
    {
        var items = t.Courses.SelectMany(c => c.Items).ToList();
        return items.All(i => i.Kind != "moduleItem") ? "assignment" : items.All(i => i.Kind == "moduleItem") ? "module item" : "one";
    }

    /// <summary>A title with the tool's name taken off its front: under Knewton Alta, "Knewton Alta: Unit 2" is "Unit 2".</summary>
    private static string Short(Tool t, string title)
    {
        if (t.Name.Length == 0 || !title.StartsWith(t.Name, StringComparison.CurrentCultureIgnoreCase)) return title;
        var rest = title[t.Name.Length..].TrimStart();
        if (rest.Length == 0 || rest[0] is not (':' or '-' or '–' or '—')) return title;
        rest = rest[1..].Trim();
        return rest.Length > 0 ? rest : title;
    }

    // ---- opening ----

    /// <summary>The window a tool's assignments open in: Knewton Alta's own; (0.3.97) Macmillan Learning's and ALEKS's
    /// own popup ("look"), the tool known by the engine's provider or the host it launches on (lmslink.bfwpub.com,
    /// secure.aleks.com); else null — the tool window.</summary>
    private static string? WindowOf(Tool t)
    {
        var label = (t.Name + " " + t.Host).ToLowerInvariant();
        if (label.Contains("knewton")) return "alta";
        if (LookOf(t) != null) return "look";
        return null;
    }

    private static PageLook.Kind? LookOf(Tool t) => PageLook.ForAssignment(t.Id == "macmillan" ? true : null, t.Id == "aleks" ? true : null, t.Host);

    /// <summary>Opened in that course (a chip), or on one place it is used (<paramref name="it"/>, a row of its list) — as
    /// the search's /open opens it too (0.3.56).</summary>
    internal static void Open(Engine.Engine engine, Tool t, Course c, Item? it = null)
    {
        if (it != null) { OpenItem(engine, t, c, it); return; }
        // (Knewton Alta's assignments in their own window, the rest — a course's tab first — in the tool window)
        if (c.Open.TryGetValue("assignment", out var a) && c.Open.TryGetValue("course", out var course))
        {
            switch (WindowOf(t))
            {
                case "alta": engine.OpenAlta(new AltaLaunch(course, a, t.Name)); return;
                case "look": Log.Write($"{LookOf(t)!.Name} popup: opened from External Tools (its tool on {t.Host})"); engine.OpenLook(new LookLaunch(LookOf(t)!.Name, course, a, t.Name)); return;
            }
        }
        engine.OpenTool(new ToolLaunch(t.Name, c.Open));
    }

    /// <summary>One place the tool is used, opened (a row of its list): a module item in the tool window, as the course's
    /// Modules opens it; an assignment of Knewton Alta's in its own window, any other in the tool window.</summary>
    private static void OpenItem(Engine.Engine engine, Tool t, Course c, Item it)
    {
        var course = c.Open.GetValueOrDefault("course") is { Length: > 0 } id ? id : c.Id;
        // (0.3.99: a module item of Macmillan Learning's or ALEKS's in its popup, as their assignments)
        if (it.Kind == "moduleItem") { engine.OpenModuleTool(course, it.Id, it.Title, it.Sub, WindowOf(t) == "look" ? t.Host : null); return; }
        switch (WindowOf(t))
        {
            case "alta": engine.OpenAlta(new AltaLaunch(course, it.Id, it.Title) { Due = DueOf(it) }); return;
            case "look": Log.Write($"{LookOf(t)!.Name} popup: opened from External Tools (its tool on {t.Host})"); engine.OpenLook(new LookLaunch(LookOf(t)!.Name, course, it.Id, it.Title) { Due = DueOf(it) }); return;
        }
        engine.OpenTool(ToolLaunch.Assignment(course, it.Id, it.Title));
    }

    /// <summary>(0.3.21) An assignment's due line as its row has it, for the popup's header — the engine writes
    /// the row's line from the same words as the assignment's ("Due Oct 18 at 11:59 PM · 20 pts"); "" with no due date,
    /// null where the row does not say.</summary>
    private static string? DueOf(Item it)
    {
        if (it.Kind != "assignment") return null;
        if (string.IsNullOrEmpty(it.Due)) return "";
        var first = (it.Sub ?? "").Split(" · ")[0];
        return first.StartsWith("Due ", StringComparison.Ordinal) ? first : null;
    }

    /// <summary>
    /// The screenshot suite: --tool-list knewton opens the list of the first tool (of those that have one) whose name
    /// holds the word — else whose site does — a moment after the page draws; the picture waits for it.
    /// </summary>
    private void LaunchList(List<Tool> shown)
    {
        if (Launch.Get("tool-list") == null || Launch.Take("", "tool-list") is not { Length: > 0 } word) return;
        var tool = shown.FirstOrDefault(t => Chooser(t) && t.Name.Contains(word, StringComparison.OrdinalIgnoreCase))
            ?? shown.FirstOrDefault(t => Chooser(t) && (t.Host ?? "").Contains(word, StringComparison.OrdinalIgnoreCase));
        if (tool == null) return;
        var id = tool.Id;
        Shot.Began();
        var wait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            Shot.Ended();
            if (listId != id && ToolOf(id) is { } t && cards.TryGetValue(id, out var card) && card.IsLoaded) OpenList(t, card);
        };
        wait.Start();
    }

    // ---- the page's columns ----

    /// <summary>
    /// A card that is a button whole (the kit's CardButton style: Windows 11's card, lighter under the pointer, a shade down
    /// while pressed) and gives a little under the press — to 0.98 while it is held, by the pointer or the Space bar, and
    /// back as it is let go (Motion.Fast in, Motion.Normal out, decelerating; still, with Windows' animation effects off).
    /// It is the card's face that gives, not the button.
    /// A button inside it (a course's chip) takes its own press: the card is neither pressed nor opened by it.
    /// </summary>
    private sealed class PressCard : Button
    {
        private readonly ScaleTransform scale = new(1, 1);

        public PressCard(UIElement content, Action click)
        {
            Content = content;
            SetResourceReference(StyleProperty, "CardButton");
            Padding = new Thickness(16);
            VerticalContentAlignment = VerticalAlignment.Top;
            Click += (_, e) => { e.Handled = true; click(); };
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            var face = GetTemplateChild("Surface") as FrameworkElement ?? this;
            face.RenderTransformOrigin = new Point(0.5, 0.5);
            face.RenderTransform = scale;
        }

        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsPressedChanged(e);
            if (!Motion.On) return;
            var ms = IsPressed ? Motion.Fast : Motion.Normal;
            Motion.To(scale, ScaleTransform.ScaleXProperty, IsPressed ? 0.98 : 1, ms, Motion.Decelerate);
            Motion.To(scale, ScaleTransform.ScaleYProperty, IsPressed ? 0.98 : 1, ms, Motion.Decelerate);
        }
    }

    /// <summary>
    /// Cards in columns of one width, each put in turn at the foot of the column that is shortest so far (the leftmost of
    /// two alike) — a masonry: a short card leaves no gap under it for its row's tallest. The room between cards is the
    /// same across and down. How many columns is the screen's, for the page's width.
    /// </summary>
    private sealed class Masonry : Panel
    {
        private readonly int columns;
        private readonly double gap;

        public Masonry(int columns, double gap)
        {
            this.columns = Math.Max(1, columns);
            this.gap = gap;
        }

        private double ColumnWidth(double width) => Math.Max(0, (width - gap * (columns - 1)) / columns);

        /// <summary>Where each card goes, in order (its column and its top), and how tall the tallest column is.</summary>
        private (List<(int Column, double Top)> Spots, double Height) Place()
        {
            var tops = new double[columns];
            var spots = new List<(int, double)>(InternalChildren.Count);
            foreach (UIElement c in InternalChildren)
            {
                var col = 0;
                for (var i = 1; i < columns; i++) if (tops[i] < tops[col] - 0.5) col = i;
                spots.Add((col, tops[col]));
                tops[col] += c.DesiredSize.Height + gap;
            }
            return (spots, InternalChildren.Count == 0 ? 0 : Math.Max(0, tops.Max() - gap));
        }

        protected override Size MeasureOverride(Size available)
        {
            var width = double.IsInfinity(available.Width) ? columns * MinCard + gap * (columns - 1) : available.Width;
            var w = ColumnWidth(width);
            foreach (UIElement c in InternalChildren) c.Measure(new Size(w, double.PositiveInfinity));
            return new Size(width, Place().Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            var w = ColumnWidth(final.Width);
            var (spots, _) = Place();
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                var c = InternalChildren[i];
                c.Arrange(new Rect(spots[i].Column * (w + gap), spots[i].Top, w, c.DesiredSize.Height));
            }
            return final;
        }
    }
}
