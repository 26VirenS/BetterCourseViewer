using System.Globalization;
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
/// the student's courses — a course's own (its navigation's) and those found only in its assignments and modules.
/// (0.3.110) As the watch's honeycomb (ToolBubbles.cs): a bubble a tool, the one needed soonest in the middle and the rest
/// in rings round it, each filled with its tool's colour from the logo on its site, the logo's mark in white on it and its
/// name along its lower edge; a badge says how many are still to do (red with something missing, orange with something
/// due today). A press opens the list of every place it is used — a course's own tab a row of its own — to choose the one
/// to open (ExternalToolsList.cs); a tool that is one course's tab and nothing else opens it at once. Knewton Alta opens
/// in its own window, the rest in the tool window. The filter beside the title narrows the tools by name, site or course.
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

    private static readonly Color Gray = Color.FromRgb(0x8e, 0x8e, 0x93);

    private readonly Loader<Data> model;
    private TextBox? filter;
    private string query = "";
    private bool hadRows;
    private string? lastSub;
    /// <summary>The bubbles as drawn, by tool: where the keyboard goes back to as a tool's list closes.</summary>
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

    /// <summary>Built again only when the honeycomb's rows hold another number across, or are drawn at another size.</summary>
    protected override bool LayoutChanges(double before, double now) => HoneyPlan.KeyOf(before) != HoneyPlan.KeyOf(now);

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
        // (0.3.110) the honeycomb, centred across the page
        var specs = Specs(shown);
        var plan = HoneyPlan.Make(specs.Count, Width > 0 ? Width : 900);
        var field = new Canvas { Width = plan.Width, Height = plan.Height, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 8) };
        for (var i = 0; i < specs.Count && i < plan.Cells.Count; i++)
        {
            var cell = plan.Cells[i];
            var b = Bubble(specs[i], cell.Size);
            Canvas.SetLeft(b, cell.X - cell.Size / 2);
            Canvas.SetTop(b, cell.Y - cell.Size / 2);
            field.Children.Add(b);
            cards[specs[i].Tool.Id] = b;
        }
        Rehang(shown);
        LaunchList(shown);
        return field;
    }

    /// <summary>One tool as its bubble: its badge's number and colour, its tooltip and what a screen reader says.</summary>
    private sealed record BubbleSpec(Tool Tool, int Count, BubbleTone Tone, string Help, string Label);

    /// <summary>(0.3.110) Each tool as its bubble: the one needed soonest first (the middle), then those with something
    /// missing, then those with places to choose from, then a course's own tabs — by name within each. Its badge: how many
    /// are still to do (else how many are missing), red with something missing, orange with something due today.</summary>
    private static List<BubbleSpec> Specs(List<Tool> rows)
    {
        var today = DateTime.Now.Date;
        return rows.Select(t =>
            {
                var items = t.Courses.SelectMany(c => c.Items).ToList();
                var due = items.Where(i => i.Upcoming).Select(i => DueAt(i.Due)).Where(d => d != null).Min();
                var missing = items.Count(i => i.Status?.Kind == "bad");
                var group = due != null ? 0 : missing > 0 ? 1 : items.Count > 0 ? 2 : 3;
                return (t, items, due, missing, group);
            })
            .OrderBy(x => x.group).ThenBy(x => x.due ?? DateTime.MaxValue).ThenBy(x => x.t.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x =>
            {
                var toDo = x.items.Count(i => i.Upcoming);
                var tone = x.missing > 0 ? BubbleTone.Late : x.due is { } d && d.Date == today ? BubbleTone.Today : BubbleTone.Plain;
                var help = x.items.Count == 0 ? $"Open {x.t.Name}" : $"Choose which {Noun(x.t)} to open";
                if (x.t.Next is { Title.Length: > 0 } n)
                {
                    var title = n.Short is { Length: > 0 } s ? s : Short(x.t, n.Title);
                    var when = string.Join(" · ", new[] { (n.Sub ?? "").Split(" · ")[0], n.Course }.Where(w => !string.IsNullOrEmpty(w)));
                    help = when.Length > 0 ? $"Next: {title} · {when}" : $"Next: {title}";
                }
                var said = new List<string> { x.t.Name };
                if (toDo > 0) said.Add($"{toDo} to do");
                if (x.missing > 0) said.Add($"{x.missing} missing");
                return new BubbleSpec(x.t, toDo > 0 ? toDo : x.missing, tone, help, string.Join(", ", said));
            })
            .ToList();
    }

    /// <summary>A due date as Canvas gives it ("2026-11-23T07:59:00Z"), in local time; null for none.</summary>
    private static DateTime? DueAt(string? s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.LocalDateTime : null;

    /// <summary>A tool's bubble, a button: a press opens its list (or, a tool that is one course's tab and nothing else,
    /// that tab).</summary>
    private FrameworkElement Bubble(BubbleSpec s, double size)
    {
        var t = s.Tool;
        var orb = new ToolOrb(t.Name, t.Host, t.Courses.FirstOrDefault()?.Color ?? "", size);
        BubbleButton b = null!;
        b = new BubbleButton(orb, s.Count > 0 ? BubbleButton.Badge(s.Count, s.Tone) : null, size, () => Press(t, b));
        b.ToolTip = s.Help;
        AutomationProperties.SetName(b, s.Label);
        AutomationProperties.SetHelpText(b, s.Help);
        return b;
    }

    /// <summary>A bubble pressed: its list — or, with nothing to choose (one course's tab and no work), that tab at once.</summary>
    private void Press(Tool t, FrameworkElement bubble)
    {
        var tabs = t.Courses.Where(c => c.Open.ContainsKey("tool")).ToList();
        if (t.Courses.All(c => c.Items.Count == 0))
        {
            if (tabs.Count == 1) { CloseList(); Open(engine, t, tabs[0]); return; }
            if (tabs.Count == 0) { if (t.Courses.FirstOrDefault() is { } c) { CloseList(); Open(engine, t, c); } return; }
        }
        ToggleList(t.Id, bubble);
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
}
