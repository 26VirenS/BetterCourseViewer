using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// The Compare page (spec §4.1 item 8, §5.1.4): the three cells side by side at their true relative sizes — a bacterium
/// 2 µm long, an animal cell 20 µm, a plant cell 40 µm, the "how big is each" lesson — and under them §5.1.4's table
/// exactly, structure by structure (✓, ✗ or a note for each cell), each row opening that structure in the first cell that
/// has it. Pressing a cell opens it.
/// </summary>
internal sealed class CellComparePage : Grid
{
    private readonly BioSceneView scene = new("3D model of a bacterium, an animal cell and a plant cell side by side at their true relative sizes") { PickSelects = false };
    private readonly ContentControl table = new() { Focusable = false };
    private bool? drawnDark;

    public CellComparePage()
    {
        var reset = K.Button("Reset view", scene.ResetView, icon: SymbolRegular.ArrowCounterclockwise20);
        reset.MinWidth = 0;
        reset.ToolTip = "Put the camera back (or double-click empty space)";
        var chip = K.Chip("To scale", K.Tone("green"), strong: true);
        chip.ToolTip = "All three drawn at the same scale";
        var bar = K.Columns("*, auto", K.H(10, K.Text("At their true relative sizes", Ty.Subtitle), chip).Col(0), reset.Col(1));
        var host = new Grid { ClipToBounds = true, Height = 300 };
        host.Children.Add(scene);
        host.Children.Add(new CellScaleBar(scene, "µm"));
        var card = CellUI.SceneCard(bar, host, "Drag to turn · scroll to zoom · press a cell to open it");
        scene.Picked += Open;
        scene.ShowLabels = true;

        var v = K.V(16, card,
            BioUI.Caption("A bacterium is about 2 µm long, an animal cell about 20 µm across and a plant cell about 40 µm long: the bacterium is 10–20 times smaller than an animal cell. Textbook diagrams draw them the same size."),
            K.V(6, BioUI.Heading("Which cells have which parts"), BioUI.Caption("Press a row to see that part in a cell that has it.")),
            table);
        v.Margin = new Thickness(40, 8, 40, 32);
        v.MaxWidth = 1100;
        var scroll = new ScrollViewer { Content = v, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        Children.Add(scroll);

        void Look() => Draw();
        Loaded += (_, _) => { Appearance.Changed -= Look; Appearance.Changed += Look; Draw(); };
        Unloaded += (_, _) => Appearance.Changed -= Look;
    }

    private void Draw()
    {
        var dark = Appearance.Dark;
        scene.Show($"compare|{dark}", () => CEBuild.CompareScene(dark), "compare");
        if (drawnDark == dark) return;
        drawnDark = dark;
        table.Content = Table();
    }

    /// <summary>A cell pressed (or its label): that cell's page.</summary>
    private void Open(string? id)
    {
        if (id?.StartsWith("cell:", StringComparison.Ordinal) != true || CEKinds.FromId(id[5..]) is not { } k) return;
        BioNav.Set("cell", k.Id());
    }

    /// <summary>§5.1.4 as a table: the structure, then a mark and its note for each cell.</summary>
    private static FrameworkElement Table()
    {
        var v = K.V(0);
        const string cols = "2*, 2*, 2*, 2*";
        var head = K.Columns(cols, K.Text("Structure", Ty.Caption, B.Text2).Strong().Col(0), K.Text("Animal", Ty.Caption, B.Text2).Strong().Col(1),
            K.Text("Plant", Ty.Caption, B.Text2).Strong().Col(2), K.Text("Bacterium", Ty.Caption, B.Text2).Strong().Col(3));
        head.Margin = new Thickness(14, 10, 14, 8);
        v.Children.Add(head);
        foreach (var row in CECompare.Rows)
        {
            v.Children.Add(K.Divider(14));
            var g = K.Columns(cols, K.Wrapped(row.Structure, Ty.BodyStrong).Margin(0, 0, 10, 0).Col(0));
            for (var i = 0; i < 3; i++) g.Children.Add(Cell(row.Cells[i]).Col(i + 1));
            g.Margin = new Thickness(6, 2, 6, 2);
            var open = row.Open;
            if (open is { } o && CEParts.Part(o.Kind, o.Part) is { } part)
            {
                var b = K.Subtle(g, () =>
                {
                    BioNav.Set("cell", o.Kind.Id());
                    CellExplorerModel.Cell(o.Kind).Hidden.Remove(o.Part);
                    CellExplorerModel.Select(o.Kind, o.Part);
                }, new Thickness(8, 7, 8, 8));
                b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                b.ToolTip = $"See the {part.LabelInSentence} in the {o.Kind.Title().ToLowerInvariant()}";
                System.Windows.Automation.AutomationProperties.SetName(b, $"{row.Structure}: animal {row.Cells[0]}, plant {row.Cells[1]}, bacterium {row.Cells[2]}");
                v.Children.Add(b);
            }
            else
            {
                g.Margin = new Thickness(14, 9, 14, 10);
                v.Children.Add(g);
            }
        }
        return K.Card(v, new Thickness(0, 0, 0, 6));
    }

    /// <summary>A cell of the table: its mark (✓ green, ✗ quiet, ~ amber) and its note under it.</summary>
    private static FrameworkElement Cell(string text)
    {
        var (mark, note) = CEMarks.Read(text);
        var plainNote = mark == CEMark.Partly && !text.TrimStart().StartsWith('~');
        var v = K.V(2);
        if (!plainNote)
        {
            var tone = mark switch { CEMark.Yes => K.Tone("green"), CEMark.No => (Color?)null, _ => K.Tone("orange") };
            var m = K.Text(mark.Symbol(), Ty.BodyStrong);
            if (tone is { } t) m.Foreground = K.Brush(K.Darkened(t)); else m.Fg(B.Text3);
            v.Children.Add(m);
        }
        if (note.Length > 0) v.Children.Add(K.Wrapped(note, plainNote ? Ty.Body : Ty.Caption, plainNote ? B.Text : B.Text2));
        v.Margin = new Thickness(0, 0, 10, 0);
        return v;
    }
}
