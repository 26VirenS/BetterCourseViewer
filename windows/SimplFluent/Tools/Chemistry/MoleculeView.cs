using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// The 3D molecule builder (the Mac's MoleculeBuilder): a preset, atoms to add (each bonded to the selected one with the
/// bond chosen), Fill with hydrogen, Delete, Clear, and the view's style, lone pairs and labels at the left; the formula,
/// the model turning in 3D and Turning/Still in the middle; and at the right its molar mass, counts, polarity and valence
/// check, the selected atom's bonds, shape and bond angles as the model has them.
/// </summary>
internal sealed class MoleculeBuilderView : Grid
{
    private static readonly string[] Palette = { "H", "C", "N", "O", "F", "Cl", "Br", "I", "S", "P", "B", "Si" };

    private readonly MoleculeModel m = MoleculeModel.Shared;
    private readonly Stack controls = K.V(14);
    private readonly Stack facts = K.V(14);
    private readonly ContentControl head = new() { Focusable = false };
    private readonly SelectorBar orderBar, styleBar;
    private readonly ToggleSwitch lonePairs = new(), labels = new();

    /// <param name="start">A preset, a Lewis preset or an element to begin from (null: the molecule it had).</param>
    public MoleculeBuilderView(string? start)
    {
        m.Start(start);
        orderBar = new SelectorBar(new[] { "Single", "Double", "Triple" }, m.BondOrder - 1);
        orderBar.Changed += i => m.BondOrder = i + 1;
        styleBar = new SelectorBar(new[] { MBStyle.BallStick, MBStyle.SpaceFill, MBStyle.Sticks }.Select(MoleculeModel.StyleLabel).ToList(), (int)m.Style);
        styleBar.Changed += i => m.Style = (MBStyle)i;
        lonePairs.IsChecked = m.ShowLonePairs;
        lonePairs.Checked += (_, _) => m.ShowLonePairs = true;
        lonePairs.Unchecked += (_, _) => m.ShowLonePairs = false;
        labels.IsChecked = m.ShowLabels;
        labels.Checked += (_, _) => m.ShowLabels = true;
        labels.Unchecked += (_, _) => m.ShowLabels = false;
        System.Windows.Automation.AutomationProperties.SetName(lonePairs, "Show lone pairs");
        System.Windows.Automation.AutomationProperties.SetName(labels, "Show atom labels");

        var foot = K.Text("Drag to turn · scroll to zoom · click an atom to select it", Ty.Caption, B.Text2);
        foot.HorizontalAlignment = HorizontalAlignment.Center;
        foot.Margin = new Thickness(12);
        var card = K.Card(K.Rows("auto, *, auto", head.Row(0), new MoleculeSceneView(m).TourTarget("molViewer").Row(1), foot.Row(2)), new Thickness(0));
        Children.Add(new ChemLayout(controls, card, facts));

        void Changed() => Refresh();
        Loaded += (_, _) =>
        {
            m.Changed -= Changed;
            m.Changed += Changed;
            Refresh();
        };
        Unloaded += (_, _) => m.Changed -= Changed;
        Refresh();
    }

    private void Refresh()
    {
        Head();
        Controls();
        Facts();
    }

    private void Head()
    {
        var top = K.Columns("auto, auto, *, auto", K.Text(m.Formula.Length == 0 ? "Empty" : m.Formula, Ty.Subtitle).Col(0));
        if (m.Title.Length > 0 && m.Title != m.Formula)
            top.Children.Add(K.Text(m.Title, Ty.Body, B.Text2).Margin(10, 0, 0, 2).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom).Col(1));
        // (turning by itself, on or off, at the viewer's top right)
        var turning = K.Button(m.AutoRotate ? "Turning" : "Still", () => m.AutoRotate = !m.AutoRotate, accent: m.AutoRotate,
            icon: m.AutoRotate ? SymbolRegular.CubeRotate20 : SymbolRegular.Pause20);
        turning.MinWidth = 0;
        turning.ToolTip = m.AutoRotate ? "Stop the model turning by itself" : "Let the model turn by itself";
        top.Children.Add(turning.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(3));
        top.Margin = new Thickness(16);
        head.Content = top;
    }

    private void Controls()
    {
        controls.Children.Clear();
        foreach (var e in new FrameworkElement[] { orderBar, styleBar, lonePairs, labels }) ChemKit.Detach(e);
        orderBar.Select(m.BondOrder - 1, false);
        styleBar.Select((int)m.Style, false);
        if (lonePairs.IsChecked != m.ShowLonePairs) lonePairs.IsChecked = m.ShowLonePairs;
        if (labels.IsChecked != m.ShowLabels) labels.IsChecked = m.ShowLabels;

        controls.Children.Add(ChemKit.Heading("Preset"));
        controls.Children.Add(ChemKit.PresetMenu(MoleculeModel.PresetSections, MoleculeModel.Presets.Contains(m.Title) ? m.Title : null, m.LoadPreset));
        controls.Children.Add(ChemKit.Heading("Add atom"));
        var grid = new Grid();
        for (var c = 0; c < 4; c++)
        {
            if (c > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (var i = 0; i < Palette.Length; i++)
        {
            if (i % 4 == 0)
            {
                if (i > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            var b = AtomButton(Palette[i]);
            Grid.SetRow(b, grid.RowDefinitions.Count - 1);
            Grid.SetColumn(b, i % 4 * 2);
            grid.Children.Add(b);
        }
        controls.Children.Add(grid);
        controls.Children.Add(ChemKit.Caption("Each joins the selected atom with the bond below."));
        controls.Children.Add(ChemKit.Heading("New bond"));
        controls.Children.Add(orderBar);
        if (m.Message is { } msg) controls.Children.Add(ChemKit.Line(SymbolRegular.ErrorCircle16, msg, ChemKit.Orange));
        controls.Children.Add(ChemKit.Wide("Fill with hydrogen", m.FillHydrogen));
        var delete = ChemKit.Wide("Delete selected atom", m.DeleteSelected);
        delete.IsEnabled = m.Selected != null;
        if (delete.IsEnabled) delete.Fg(B.Critical);
        controls.Children.Add(delete);
        var clear = K.Subtle(K.Text("Clear", Ty.Body, B.Text2).Center(), m.Clear, new Thickness(8, 5, 8, 6));
        clear.HorizontalContentAlignment = HorizontalAlignment.Center;
        controls.Children.Add(clear);
        controls.Children.Add(ChemKit.Heading("View"));
        controls.Children.Add(styleBar);
        controls.Children.Add(Toggle("Show lone pairs", lonePairs));
        controls.Children.Add(Toggle("Show atom labels", labels));
    }

    private static FrameworkElement Toggle(string text, ToggleSwitch s)
    {
        s.VerticalAlignment = VerticalAlignment.Center;
        return K.Columns("*, auto", K.Text(text, Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), s.Col(1));
    }

    private FrameworkElement AtomButton(string s)
    {
        var swatch = new Ellipse { Width = 11, Height = 11, Fill = new SolidColorBrush(MBChem.ColorOf(s)), StrokeThickness = 0.5, VerticalAlignment = VerticalAlignment.Center };
        swatch.SetResourceReference(Shape.StrokeProperty, B.StrongStroke);
        var face = K.H(6, swatch, K.Text(s, Ty.BodyStrong));
        face.HorizontalAlignment = HorizontalAlignment.Center;
        face.VerticalAlignment = VerticalAlignment.Center;
        var inner = ChemKit.Tint(face, B.Text, 0.07, 8);
        inner.MinHeight = 34;
        var b = K.Subtle(inner, () => m.Add(s), new Thickness(0), 8);
        var name = MBChem.Element(s)?.Name ?? s;
        b.ToolTip = $"Add {name}, bonded to the selected atom";
        System.Windows.Automation.AutomationProperties.SetName(b, $"Add {name}");
        return b;
    }

    private void Facts()
    {
        facts.Children.Clear();
        var issues = m.ValenceIssues;
        var polar = m.Dipole > 0.3;
        facts.Children.Add(ChemKit.Facts(new (string, string, Color?)[]
        {
            ("Molar mass", m.MolarMass.ToString("0.00", CultureInfo.InvariantCulture) + " g/mol", null),
            ("Atoms", $"{m.Atoms.Count}", null),
            ("Bonds", $"{m.Bonds.Count}", null),
            ("Lone pairs", $"{m.LonePairs.Count}", ChemKit.Yellow),
            ("Polarity", m.Bonds.Count == 0 ? "—" : polar ? "Polar" : "Nonpolar", m.Bonds.Count == 0 ? null : polar ? ChemKit.Orange : ChemKit.Green),
            ("Valence check", issues.Count == 0 ? "All good" : $"{issues.Count} to fix", issues.Count == 0 ? ChemKit.Green : ChemKit.Orange),
        }));
        if (issues.Count > 0)
        {
            var list = K.V(4);
            foreach (var i in issues) list.Children.Add(K.Wrapped("• " + i, Ty.Caption, B.Text2));
            facts.Children.Add(list);
        }
        if (m.Selected is { } id && m.Atom(id) is { } a)
        {
            var colour = MBChem.ColorOf(a.Symbol);
            var letter = new TextBlock
            {
                Text = a.Symbol, FontSize = 15, FontWeight = FontWeights.Bold, FontFamily = K.TextFace, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Foreground = a.Symbol == "H" ? Brushes.Black : Brushes.White,
            };
            var disc = new Grid { Width = 38, Height = 38, Children = { new Ellipse { Fill = new SolidColorBrush(colour) }, letter } };
            var bonds = m.BondSum(id);
            var lone = m.LonePairCount(a);
            var words = K.V(2, K.Text(MBChem.Element(a.Symbol)?.Name ?? a.Symbol, Ty.BodyStrong),
                K.Wrapped($"{bonds} bond{(bonds == 1 ? "" : "s")} used of {MBChem.Usual(a.Symbol)} · {lone} lone pair{(lone == 1 ? "" : "s")}", Ty.Caption, B.Text2));
            words.VerticalAlignment = VerticalAlignment.Center;
            var v = K.V(10, K.Columns("auto, *", disc.Margin(0, 0, 12, 0).Col(0), words.Col(1)));
            if (m.Neighbours(id).Count >= 2)
            {
                v.Children.Add(K.Wrapped($"Shape round it: {m.Shape(id)}", Ty.BodyStrong));
                var angles = K.V(2);
                foreach (var t in m.Angles(id))
                {
                    var line = K.Text(t, Ty.Caption, B.Text2);
                    line.FontFeatures();
                    angles.Children.Add(line);
                }
                v.Children.Add(angles);
            }
            facts.Children.Add(ChemKit.Card(v));
        }
        facts.Children.Add(ChemKit.Caption("Each atom's bonds and lone pairs are set out as VSEPR has them, lone pairs squeezing the bonds a little, so water bends and ammonia forms a pyramid. Bond angles are the model's, near the real ones."));
    }
}
