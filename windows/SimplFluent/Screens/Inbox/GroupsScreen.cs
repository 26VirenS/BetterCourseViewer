using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Models;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Screens;

/// <summary>
/// Groups (the Mac's GroupsView): the student's groups — the current ones, then the past ones, each heading with how
/// many it holds (no count again under the title) — each a card washed in its course's colour, with its course by name
/// (its code in the tooltip) and how many are in it, that opens the group's own home. On a wide page, more to a row
/// (cards 300 to 460 wide). A right-click opens it, opens it in Canvas or copies its link.
/// </summary>
public sealed class GroupsScreen : Screen
{
    private readonly Loader<GroupsData> model;

    public GroupsScreen(Engine.Engine engine) : base(engine) { model = Watch(new Loader<GroupsData>()); }

    public override string Title => "Groups";

    public override FrameworkElement? Commands => InboxUi.CanvasMenu(engine, () => "/groups", () => "Groups");

    public override void Refresh() => model.Ensure(engine, "groups");

    private const double Gap = 16, Least = 300, Most = 460;

    private static int Cols(double w) => Math.Max(1, (int)Math.Floor((Math.Max(Least, w) + Gap) / (Least + Gap)));

    protected override bool LayoutChanges(double before, double now) => Cols(before) != Cols(now);


    protected override FrameworkElement Build()
    {
        if (model.Data is not { } d) return K.LoadState(model.Error, () => model.Reload(engine, "groups"));
        if (d.Current.Count == 0 && d.Past.Count == 0)
            return new Grid { MinHeight = 360, Children = { InboxUi.Unavailable(SymbolRegular.PeopleTeam24, "No groups", d.Empty ?? "You are not in any groups.") } };
        var page = new Stack { Orientation = Orientation.Vertical, Spacing = 28 };
        if (d.Current.Count > 0) page.Children.Add(Section("Current", d.Current));
        if (d.Past.Count > 0) page.Children.Add(Section("Past", d.Past));
        return page;
    }

    /// <summary>A section's heading with how many, over its cards in as many columns as fit.</summary>
    private FrameworkElement Section(string title, List<GroupRow> rows)
    {
        var w = Math.Max(Least, Width);
        var cols = Cols(w);
        var colW = Math.Min(Most, (w - (cols - 1) * Gap) / cols);
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        for (var c = 0; c < cols; c++)
        {
            if (c > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colW) });
        }
        var rowsN = (rows.Count + cols - 1) / cols;
        for (var r = 0; r < rowsN; r++)
        {
            if (r > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Gap) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var k = 0; k < rows.Count; k++)
        {
            var card = Card(rows[k]);
            Grid.SetColumn(card, (k % cols) * 2);
            Grid.SetRow(card, (k / cols) * 2);
            grid.Children.Add(card);
        }
        return K.V(0, Heading.Section(title, rows.Count.ToString()), grid);
    }

    /// <summary>
    /// A group as a card (the Mac's GroupsCard): its symbol on its course's colour, its name, its course by name — cut
    /// short with an ellipsis where it is long, whole in its tooltip with its code ("ASRS") — with the colour's dot, how
    /// many are in it, a chevron; the card washed in the colour from its left. The group's home names the course the same
    /// way.
    /// </summary>
    private Button Card(GroupRow g)
    {
        var color = K.Hex(g.Color);
        // its line in parts ("Academic Success Resource Site (2026-27) · 4 members")
        var parts = (g.Sub ?? "").Split(" · ", StringSplitOptions.RemoveEmptyEntries);
        var members = parts.FirstOrDefault(p => p.Contains("member"));
        var course = g.Course is { Length: > 0 } named ? named : parts.FirstOrDefault(p => !p.Contains("member"));

        var name = K.Wrapped(g.Name, Ty.BodyLarge, B.Text, 2);
        name.FontWeight = FontWeights.SemiBold;
        var words = K.V(5, name);
        if (course != null)
        {
            var courseText = K.Text(course, Ty.Body, B.Text2);
            var courseLine = K.Columns("auto, *", K.Dot(color, 8).Margin(0, 0, 6, 0).Col(0), courseText.Col(1));
            courseLine.ToolTip = g.CourseCode is { Length: > 0 } code ? $"{course} ({code})" : course;
            words.Children.Add(courseLine);
        }
        if (members != null) words.Children.Add(K.H(6, K.Icon(SymbolRegular.People24, 14, B.Text2), K.Text(members, Ty.Body, B.Text2)));
        words.VerticalAlignment = VerticalAlignment.Center;
        var row = K.Columns("auto, *, auto",
            K.IconTile(SymbolRegular.PeopleTeam24, color, 46).Col(0),
            words.Margin(14, 0, 8, 0).Col(1),
            K.Icon(SymbolRegular.ChevronRight20, 14, B.Text3).Col(2));
        row.Margin = new Thickness(18);
        row.MinHeight = 64;

        var wash = new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(Appearance.Dark ? (byte)56 : (byte)40, color.R, color.G, color.B), 0),
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.7),
                },
                new Point(0, 0), new Point(1, 1)),
            IsHitTestVisible = false,
        };
        var content = new Grid { Children = { wash, row } };
        content.SizeChanged += (_, e) => content.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 7, 7);
        var id = g.Id;
        var b = K.CardButton(content, () => engine.Go(Place.Home($"groups/{id}")), 8, new Thickness(0));
        b.ToolTip = $"Open {g.Name}";
        System.Windows.Automation.AutomationProperties.SetName(b, string.Join(", ", new[] { g.Name, course, members }.Where(s => !string.IsNullOrEmpty(s))));
        b.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            var m = new ContextMenu();
            m.Items.Add(InboxUi.Item("Open", SymbolRegular.Open24, () => engine.Go(Place.Home($"groups/{id}"))));
            m.Items.Add(new Separator());
            m.Items.Add(InboxUi.Item($"Open in {engine.LmsName}", SymbolRegular.Globe24, () => engine.OpenWebScreen($"/groups/{id}", g.Name)));
            m.Items.Add(InboxUi.Item("Copy link", SymbolRegular.Link24, () => InboxUi.CopyLink(engine, $"/groups/{id}")));
            InboxUi.Open(m, b, atPointer: true);
        };
        return b;
    }
}
