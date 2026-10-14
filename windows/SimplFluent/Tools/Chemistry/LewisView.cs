using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>How the molecule is made: quickly (a central atom and the atoms round it) or drawn freely.</summary>
internal enum LWay { Quick, Draw }

/// <summary>What a press on the drawing does.</summary>
internal enum LTool { Draw, Move, Erase }

/// <summary>
/// The Lewis maker's state (the Mac's LewisMaker @State): the molecule, the way it is made, the element and tool, what is
/// selected and which structure is shown, the ranking, and the quick way's own choices — one for the app, so the molecule
/// is still there when the maker opens again, and kept on this PC for next time (ChemPrefs <c>lewis.draft</c>, written as
/// it changes: the Mac's). <see cref="Changed"/> asks the views to show it again; <see cref="Moved"/> only the drawing
/// (an atom being dragged).
/// </summary>
internal sealed class LewisState
{
    public static LewisState Shared { get; } = Kept();

    public static readonly string[] Outers = { "H", "O", "N", "C", "F", "Cl", "Br", "I", "S" };

    public LWay Way { get; set; } = LWay.Quick;
    public LTool Tool { get; set; } = LTool.Draw;
    public string Element { get; set; } = "C";
    public LGraph Graph { get; set; } = new();
    public int? Selected { get; set; }
    public int Shown { get; set; }
    public LewisSolver.Mode Mode { get; set; } = LewisSolver.Mode.Formal;
    // (the quick way's own choices)
    public string Central { get; set; } = "O";
    public Dictionary<string, int> Counts { get; set; } = new() { ["H"] = 2 };
    public int Charge { get; set; }
    public string? Preset { get; set; } = "H₂O";

    private readonly LewisMemo memo = new();
    private string lastTopology = "";
    private EventHandler? tween;

    public event Action? Changed;
    public event Action? Moved;

    public void Change() => Changed?.Invoke();
    public void Move() => Moved?.Invoke();

    // ---- kept for next time ----

    private sealed record DraftAtom(int Id, string Symbol, double X, double Y);
    private sealed record DraftBond(int Id, int A, int B, int Order);
    private sealed record Draft(string Way, string Mode, string Central, Dictionary<string, int> Counts, int Charge, string? Preset,
        List<DraftAtom> Atoms, List<DraftBond> Bonds, bool Manual, int NextId);

    /// <summary>The maker as it was left (its molecule, its way, its quick choices, its ranking), else water.</summary>
    private static LewisState Kept()
    {
        var s = new LewisState();
        try
        {
            if (ChemPrefs.String("lewis", "draft") is { Length: > 0 } text && System.Text.Json.JsonSerializer.Deserialize<Draft>(text) is { } d && d.Atoms.Count > 0)
            {
                s.Way = Enum.TryParse<LWay>(d.Way, out var w) ? w : LWay.Quick;
                s.Mode = Enum.TryParse<LewisSolver.Mode>(d.Mode, out var m) ? m : LewisSolver.Mode.Formal;
                s.Central = d.Central;
                s.Counts = new Dictionary<string, int>(d.Counts);
                s.Charge = d.Charge;
                s.Preset = d.Preset;
                s.Graph = new LGraph
                {
                    Atoms = d.Atoms.Select(a => new LAtom { Id = a.Id, Symbol = a.Symbol, Pos = new Point(a.X, a.Y) }).ToList(),
                    Bonds = d.Bonds.Select(b => new LBond { Id = b.Id, A = b.A, B = b.B, Order = b.Order }).ToList(),
                    Charge = d.Charge, Manual = d.Manual, NextId = d.NextId,
                };
            }
        }
        catch (Exception e) { Support.Log.Write("lewis: the kept molecule could not be read: " + e.Message); }
        // (written as it changes — never mid-drag, which only moves the drawing)
        s.Changed += s.Keep;
        return s;
    }

    private void Keep()
    {
        var d = new Draft(Way.ToString(), Mode.ToString(), Central, Counts, Graph.Charge, Preset,
            Graph.Atoms.Select(a => new DraftAtom(a.Id, a.Symbol, Math.Round(a.Pos.X, 1), Math.Round(a.Pos.Y, 1))).ToList(),
            Graph.Bonds.Select(b => new DraftBond(b.Id, b.A, b.B, b.Order)).ToList(), Graph.Manual, Graph.NextId);
        ChemPrefs.Set("lewis", "draft", System.Text.Json.JsonSerializer.Serialize(d));
    }

    /// <summary>The drawing changed by hand: no longer a preset, and a quick molecule becomes a drawing.</summary>
    public void Edited()
    {
        Preset = null;
        if (Way == LWay.Quick) Way = LWay.Draw;
        Change();
    }

    /// <summary>Its electrons — worked out when the molecule or the ranking changes, not each time it is drawn.</summary>
    public LResult Result()
    {
        var k = Graph.TopologyKey;
        if (k != lastTopology)
        {
            // (another molecule: its major structure shown)
            lastTopology = k;
            Shown = 0;
        }
        return memo.Result(Graph, Mode);
    }

    /// <summary>The quick way's molecule: a central atom and the atoms round it, set out as its shape is drawn on paper.</summary>
    private static (LGraph Graph, int Central, List<string> Around) Quick(string central, IEnumerable<string> around, int charge, LewisSolver.Mode mode)
    {
        var g = new LGraph { Charge = charge };
        var c = g.Add(central, new Point());
        var list = around.ToList();
        var outer = new List<int>();
        for (var k = 0; k < list.Count; k++)
        {
            var a0 = -Math.PI / 2 + (double)k / Math.Max(list.Count, 1) * 2 * Math.PI;
            var t = g.Add(list[k], new Point(90 * Math.Cos(a0), 90 * Math.Sin(a0)));
            g.Connect(c, t);
            outer.Add(t);
        }
        // (water bent, ammonia a fan under its lone pair — once the electrons show how many lone pairs the centre has)
        var lone = LewisSolver.Solve(g, mode).Major?.LoneOf(c) ?? 0;
        var angles = QuickAngles(outer.Count, (lone + 1) / 2);
        for (var k = 0; k < outer.Count; k++)
        {
            var a = angles[k] * Math.PI / 180;
            if (g.Atom(outer[k]) is { } atom) atom.Pos = new Point(90 * Math.Cos(a), 90 * Math.Sin(a));
        }
        return (g, c, list);
    }

    public void BuildQuick()
    {
        var around = Outers.SelectMany(s => Enumerable.Repeat(s, Counts.GetValueOrDefault(s))).ToList();
        var (g, c, _) = Quick(Central, around, Charge, Mode);
        Graph = g;
        Selected = c;
        var sorted = around.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Preset = LewisPresets.All.FirstOrDefault(p => p.Quick is { } q && q.Central == Central && p.Charge == Charge &&
                                                      q.Around.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(sorted))?.Name;
    }

    /// <summary>Directions (degrees, y down) for the atoms round a centre, by how many there are and its lone pairs.</summary>
    public static double[] QuickAngles(int n, int l) => (n, l) switch
    {
        (1, _) => new double[] { 0 },
        (2, 0) => new double[] { 180, 0 },
        (2, _) => new double[] { 142, 38 },
        (3, 0) => new double[] { -90, 30, 150 },
        (3, 1) => new double[] { 150, 90, 30 },
        (3, _) => new double[] { -90, 90, 180 },
        (4, 1) => new double[] { -90, 90, 150, 210 },
        (4, _) => new double[] { -90, 0, 90, 180 },
        (6, _) => new double[] { -90, 90, 30, 150, 210, 330 },
        _ => Enumerable.Range(0, n).Select(i => -90 + i * 360.0 / Math.Max(n, 1)).ToArray(),
    };

    /// <summary>A preset loaded: the quick way's choices set for a simple one, its drawing tidied for the rest.</summary>
    public void Load(string name)
    {
        if (LewisPresets.Named(name) is not { } p) return;
        StopTween();
        Preset = name;
        if (p.Quick is { } q)
        {
            Way = LWay.Quick;
            Central = q.Central;
            var c = new Dictionary<string, int>();
            foreach (var s in q.Around) c[s] = c.GetValueOrDefault(s) + 1;
            Counts = c;
            Charge = p.Charge;
            BuildQuick();
            Preset = name;
        }
        else
        {
            Way = LWay.Draw;
            Graph = GraphOf(p);
            Selected = null;
            Charge = p.Charge;
        }
    }

    /// <summary>A preset's molecule, as the maker would show it.</summary>
    public static LGraph GraphOf(LewisPresets.Preset p)
    {
        if (p.Quick is { } q) return Quick(q.Central, q.Around, p.Charge, LewisSolver.Mode.Formal).Graph;
        var g = p.Build();
        g.Charge = p.Charge;
        g.Tidy();
        return g;
    }

    /// <summary>
    /// Begun from what someone asked for (the Periodic Table, a launch flag): a preset by its name or formula; else an
    /// element, as the quick way's central atom. False when it is neither.
    /// </summary>
    public bool Start(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (LewisPresets.Find(text) is { } p)
        {
            Load(p.Name);
            return true;
        }
        if (ChemElements.Of(text.Trim()) is { } e)
        {
            StopTween();
            Way = LWay.Quick;
            Central = e.Symbol;
            BuildQuick();
            return true;
        }
        return false;
    }

    /// <summary>Laid out afresh, the atoms gliding to their places.</summary>
    public void Tidy()
    {
        StopTween();
        var atoms = Graph.Atoms.ToList();
        var before = atoms.Select(a => a.Pos).ToArray();
        Graph.Tidy();
        var after = atoms.Select(a => a.Pos).ToArray();
        if (!Motion.On) { Change(); return; }
        for (var i = 0; i < atoms.Count; i++) atoms[i].Pos = before[i];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        tween = (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / 420);
            var k = Motion.Gentle.Ease(t);
            for (var i = 0; i < atoms.Count; i++)
                atoms[i].Pos = new Point(before[i].X + (after[i].X - before[i].X) * k, before[i].Y + (after[i].Y - before[i].Y) * k);
            if (t >= 1)
            {
                StopTween();
                Change();
            }
            else Move();
        };
        CompositionTarget.Rendering += tween;
    }

    private void StopTween()
    {
        if (tween == null) return;
        CompositionTarget.Rendering -= tween;
        tween = null;
    }
}

/// <summary>
/// The Lewis structure maker (the Mac's LewisMaker): the way it is made, a preset, the quick way's central atom and the
/// atoms round it (or the drawing's element and tool), the charge and the ranking at the left; the formula, the drawing
/// and its resonance structures in the middle; and at the right its electrons, polarity, notes and warnings, the selected
/// atom's shape, every atom's electrons, the average bond orders, and Open in Molecule Builder.
/// </summary>
internal sealed class LewisMakerView : Grid
{
    private readonly LewisState st = LewisState.Shared;
    private readonly Action openIn3D;
    private readonly Stack controls = K.V(14);
    private readonly Stack facts = K.V(14);
    private readonly ContentControl head = new() { Focusable = false };
    private readonly ContentControl drawing = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly ContentControl strip = new() { Focusable = false };
    private readonly LewisEditor editor;
    private readonly SelectorBar wayBar, toolBar, modeBar;
    private readonly ChemLayout layout;

    /// <summary>The drawing over the rest (a narrow window): its way and preset are in its own heading.</summary>
    private bool InHead => !layout.Wide;

    /// <param name="start">A preset or an element to begin from (null: the molecule it had).</param>
    /// <param name="openIn3D">Where Open in Molecule Builder goes, the molecule already in the builder's model (the
    /// Molecule Builder, in this tool's popup's place: Chemistry.Lewis).</param>
    public LewisMakerView(string? start, Action openIn3D)
    {
        this.openIn3D = openIn3D;
        if (st.Graph.Atoms.Count == 0) st.BuildQuick();
        st.Start(start);
        editor = new LewisEditor(st);
        wayBar = new SelectorBar(new[] { "Quick", "Draw" }, (int)st.Way);
        wayBar.Changed += i =>
        {
            st.Way = (LWay)i;
            st.Change();
        };
        toolBar = new SelectorBar(new[] { "Draw", "Move", "Erase" }, (int)st.Tool);
        toolBar.Changed += i => { st.Tool = (LTool)i; st.Change(); };
        modeBar = new SelectorBar(new[] { LewisSolver.Label(LewisSolver.Mode.Octet), LewisSolver.Label(LewisSolver.Mode.Formal) }, st.Mode == LewisSolver.Mode.Octet ? 0 : 1);
        modeBar.Changed += i => { st.Mode = i == 0 ? LewisSolver.Mode.Octet : LewisSolver.Mode.Formal; st.Change(); };

        var legend = K.H(16,
            K.H(5, ChemKit.Dot(ChemKit.Yellow, 7), K.Text("Lone electrons", Ty.Caption, B.Text2)),
            K.H(5, ChemKit.Dot(ChemKit.Orange, 7), K.Text("Unpaired", Ty.Caption, B.Text2)),
            K.Text("Small numbers: formal charges", Ty.Caption, B.Text2));
        legend.HorizontalAlignment = HorizontalAlignment.Center;
        legend.Margin = new Thickness(12);
        var card = K.Card(K.Rows("auto, *, auto, auto", head.Row(0), drawing.Row(1), strip.Row(2), legend.Row(3)), new Thickness(0));
        layout = new ChemLayout(controls, card, facts);
        // (one over the other: Quick or Draw and the preset in the drawing's own heading, in view with it)
        layout.WideChanged += Refresh;
        Children.Add(layout);

        void Changed() => Refresh();
        void Moved() => editor.InvalidateVisual();
        Loaded += (_, _) =>
        {
            st.Changed -= Changed;
            st.Changed += Changed;
            st.Moved -= Moved;
            st.Moved += Moved;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            st.Changed -= Changed;
            st.Moved -= Moved;
        };
        Refresh();
    }

    private LStructure? ShownStructure(LResult r) => st.Shown >= 0 && st.Shown < r.Structures.Count ? r.Structures[st.Shown] : r.Major;

    private void Refresh()
    {
        var r = st.Result();
        var s = ShownStructure(r);
        Controls();
        Centre(r, s);
        Facts(r, s);
    }

    // ---- controls ----

    private void Controls()
    {
        controls.Children.Clear();
        foreach (var bar in new[] { wayBar, toolBar, modeBar }) ChemKit.Detach(bar);
        wayBar.Select((int)st.Way, false);
        toolBar.Select((int)st.Tool, false);
        modeBar.Select(st.Mode == LewisSolver.Mode.Octet ? 0 : 1, false);
        if (!InHead)
        {
            controls.Children.Add(wayBar);
            controls.Children.Add(ChemKit.Heading("Preset"));
            controls.Children.Add(Presets());
        }
        controls.Children.Add(st.Way == LWay.Quick ? QuickControls() : DrawControls());
        controls.Children.Add(ChemKit.Heading("Charge"));
        controls.Children.Add(K.Columns("*, auto",
            K.Text("Total charge", Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0),
            ChemKit.Stepper(st.Graph.Charge, -4, 4, v =>
            {
                st.Graph.Charge = v;
                st.Charge = v;
                st.Preset = null;
                st.Change();
            }, "Lower the charge", "Raise the charge").Col(1)));
        controls.Children.Add(ChemKit.Heading("Rank by"));
        controls.Children.Add(modeBar);
        controls.Children.Add(ChemKit.Caption(st.Mode == LewisSolver.Mode.Octet
            ? "Every atom kept to its octet (none past eight)."
            : "The smallest formal charges, period 3 atoms and below holding up to twelve."));
    }

    /// <summary>The presets' menu.</summary>
    private FrameworkElement Presets() => ChemKit.PresetMenu(LewisPresets.Sections, st.Preset, name =>
    {
        st.Load(name);
        st.Change();
    });

    private FrameworkElement QuickControls()
    {
        var v = K.V(14);
        v.Children.Add(ChemKit.Heading("Central atom"));
        v.Children.Add(ChemKit.ElementPicker(st.Central, s =>
        {
            st.Central = s;
            st.BuildQuick();
            st.Change();
        }));
        v.Children.Add(ChemKit.Heading("Surrounding atoms"));
        // (two columns of small cards: the element — a dot of its colour, the symbol in the text colour so a dark one stays
        // readable — and its count)
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var outers = LewisState.Outers;
        for (var i = 0; i < outers.Length; i++)
        {
            if (i % 2 == 0)
            {
                if (i > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            var cell = OuterCell(outers[i]);
            Grid.SetRow(cell, grid.RowDefinitions.Count - 1);
            Grid.SetColumn(cell, i % 2 == 0 ? 0 : 2);
            grid.Children.Add(cell);
        }
        v.Children.Add(grid);
        return v;
    }

    private FrameworkElement OuterCell(string s)
    {
        var n = st.Counts.GetValueOrDefault(s);
        var symbol = K.Text(s, Ty.BodyStrong);
        symbol.MinWidth = 18;
        symbol.VerticalAlignment = VerticalAlignment.Center;
        var row = K.Columns("auto, auto, *, auto",
            ChemKit.Dot(LChem.ColorOf(s), 7).Margin(0, 0, 6, 0).Col(0),
            symbol.Col(1),
            ChemKit.Stepper(n, 0, 6, v =>
            {
                st.Counts[s] = v;
                st.BuildQuick();
                st.Change();
            }, $"One {s} fewer", $"One {s} more").Col(3));
        row.Margin = new Thickness(10, 6, 6, 6);
        return ChemKit.Tint(row, n > 0 ? B.Accent : B.Text, n > 0 ? 0.12 : 0.05, 8);
    }

    private FrameworkElement DrawControls()
    {
        var v = K.V(14);
        v.Children.Add(ChemKit.Heading("Element"));
        v.Children.Add(ChemKit.ElementPicker(st.Element, s =>
        {
            st.Element = s;
            // (an atom selected takes the element picked)
            if (st.Selected is { } id && st.Graph.Atom(id) is { } a)
            {
                a.Symbol = s;
                st.Preset = null;
            }
            st.Change();
        }));
        v.Children.Add(toolBar);
        v.Children.Add(ChemKit.Caption(st.Tool switch
        {
            LTool.Draw => "Click to place an atom (bonded to the one selected). Drag from an atom to another to bond them, or to empty space for a new bonded atom. Click a bond to make it double, triple, then single again.",
            LTool.Move => "Drag atoms to move them.",
            _ => "Click an atom or a bond to remove it.",
        }));
        Wpf.Ui.Controls.Button Small(string text, Action click)
        {
            var b = K.Button(text, click);
            b.MinWidth = 0;
            b.Padding = new Thickness(11, 4, 11, 5);
            return b;
        }
        var row = K.Columns("auto, auto, *, auto", Small("Tidy", st.Tidy).Col(0));
        if (st.Graph.Manual)
        {
            var auto = Small("Auto bonds", () => { st.Graph.Manual = false; st.Change(); });
            auto.ToolTip = "Let the builder choose the bond orders again";
            auto.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(auto.Col(1));
        }
        row.Children.Add(Small("Clear", () =>
        {
            st.Graph = new LGraph { Charge = st.Graph.Charge };
            st.Selected = null;
            st.Preset = null;
            st.Change();
        }).Col(3));
        v.Children.Add(row);
        return v;
    }

    // ---- the drawing ----

    private void Centre(LResult r, LStructure? s)
    {
        var formula = K.Text(LChem.Formula(st.Graph.Atoms.Select(a => a.Symbol), st.Graph.Charge), Ty.Subtitle);
        var top = K.Columns("auto, auto, *, auto", formula.Col(0));
        if (r.Ionic) top.Children.Add(K.Text("ionic", Ty.Body, B.Text2).Margin(10, 0, 0, 2).Align(HorizontalAlignment.Left, VerticalAlignment.Bottom).Col(1));
        if (r.Structures.Count > 1)
        {
            var which = st.Shown == 0 ? "Major contributor" : st.Shown < r.Equivalent ? "Equivalent structure" : "Minor contributor";
            var label = ChemKit.Paint(K.Text(which, Ty.Caption).Strong(), st.Shown < Math.Max(r.Equivalent, 1) ? ChemKit.Green : ChemKit.Orange, tone: true);
            top.Children.Add(label.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(3));
        }
        top.Margin = new Thickness(16);
        if (!InHead) head.Content = top;
        else
        {
            // (one over the other: Quick or Draw and the preset under the formula, in view with the drawing)
            ChemKit.Detach(wayBar);
            wayBar.VerticalAlignment = VerticalAlignment.Center;
            var ways = K.Columns("auto, auto, auto, *", wayBar.Col(0),
                ChemKit.Heading("Preset").Margin(16, 0, 10, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1),
                Presets().Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(2));
            ways.Margin = new Thickness(16, 0, 16, 12);
            top.Margin = new Thickness(16, 16, 16, 10);
            head.Content = K.V(0, top, ways);
        }

        if (r.Drawable)
        {
            editor.Graph = st.Graph;
            editor.Structure = s;
            editor.Result = r;
            editor.Selected = st.Selected;
            editor.Editable = st.Way == LWay.Draw;
            if (!ReferenceEquals(drawing.Content, editor)) drawing.Content = editor;
            editor.InvalidateVisual();
        }
        else
        {
            var v = K.V(12, K.Icon(SymbolRegular.ChatWarning24, 34, B.Text2));
            foreach (var n in r.Notes)
            {
                var t = K.Wrapped(n, Ty.Body).Center();
                t.MaxWidth = 460;
                v.Children.Add(t);
            }
            v.HorizontalAlignment = HorizontalAlignment.Center;
            v.VerticalAlignment = VerticalAlignment.Center;
            v.Margin = new Thickness(30);
            drawing.Content = v;
        }
        strip.Content = r.Drawable && r.Structures.Count > 1 ? Resonance(r) : null;
        // (the drawing over the rest, with its resonance structures under it in the card: room for both, so the
        //  structure itself is never smaller than its own thumbnails)
        layout.Floor = strip.Content != null ? 480 : 320;
    }

    /// <summary>The resonance structures side by side, ↔ between them; a press shows one large.</summary>
    private FrameworkElement Resonance(LResult r)
    {
        var row = K.H(6);
        for (var k = 0; k < r.Structures.Count; k++)
        {
            if (k > 0)
            {
                var arrow = K.Text("↔", Ty.BodyLarge, B.Text2).Strong();
                arrow.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(arrow);
            }
            var thumb = new LewisCanvas { Graph = st.Graph, Structure = r.Structures[k], Result = r, MaxScale = 0.42, Width = 130, Height = 92, IsHitTestVisible = false };
            var word = k == 0 ? "major" : k < r.Equivalent ? "equivalent" : "minor";
            var label = ChemKit.Paint(K.Text(word, Ty.Caption), k < Math.Max(r.Equivalent, 1) ? ChemKit.Green : ChemKit.Orange, tone: true).Center();
            var inner = K.V(2, thumb, label);
            inner.Margin = new Thickness(4);
            var shown = k == st.Shown;
            var n = k;
            var b = K.Subtle(ChemKit.Tint(inner, shown ? B.Accent : B.Text, shown ? 0.14 : 0.04, 8), () =>
            {
                st.Shown = n;
                st.Change();
            }, new Thickness(0), 8);
            b.ToolTip = k == 0 ? "The major structure" : $"Structure {k + 1} ({word})";
            System.Windows.Automation.AutomationProperties.SetName(b, $"Resonance structure {k + 1}, {word}");
            row.Children.Add(b);
        }
        row.Margin = new Thickness(14, 0, 14, 0);
        return new ScrollViewer
        {
            Content = row, Height = 118, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, VerticalContentAlignment = VerticalAlignment.Center,
        };
    }

    // ---- facts ----

    private void Facts(LResult r, LStructure? s)
    {
        facts.Children.Clear();
        var g = st.Graph;
        var bondingPairs = s == null ? 0 : g.Bonds.Sum(b => s.Order(b.Id));
        var loneElectrons = s == null ? 0 : g.Atoms.Sum(a => s.LoneOf(a.Id));
        var radicals = s == null ? 0 : g.Atoms.Count(a => s.LoneOf(a.Id) % 2 == 1);
        var polar = Polarity(r, s);
        var rows = new List<(string, string, Color?)>
        {
            ("Valence electrons", $"{r.TotalElectrons}", null),
            ("Bonding pairs", $"{bondingPairs}", null),
            ("Lone pairs", $"{loneElectrons / 2}", ChemKit.Yellow),
        };
        if (radicals > 0) rows.Add(("Unpaired electrons", $"{radicals}", ChemKit.Orange));
        rows.Add(("Resonance structures", ResonanceText(r), null));
        rows.Add(("Polarity", polar, polar == "Polar" ? ChemKit.Orange : polar == "Nonpolar" ? ChemKit.Green : null));
        facts.Children.Add(ChemKit.Facts(rows));
        foreach (var n in r.Notes) facts.Children.Add(ChemKit.Line(SymbolRegular.Info16, n, null));
        foreach (var w in r.Warnings) facts.Children.Add(ChemKit.Line(SymbolRegular.Warning16, w, ChemKit.Orange));
        if (s != null && r.Drawable)
        {
            if (SelectedAtom(s) is { } sel) facts.Children.Add(sel);
            facts.Children.Add(AtomTable(s));
            if (Averages(r) is { } av) facts.Children.Add(av);
        }
        // (the structure shown, built in 3D: the Molecule Builder in this tool's place, named for the tool it opens)
        var open = ChemKit.Wide("Open in Molecule Builder", () =>
        {
            var rr = st.Result();
            if (ShownStructure(rr) is not { } shown) return;
            MoleculeModel.Shared.Load(st.Graph, shown);
            openIn3D();
        }, accent: true, icon: SymbolRegular.CubeRotate20);
        open.ToolTip = "Build this structure in 3D";
        open.MinHeight = 40;
        open.IsEnabled = s != null && r.Drawable && g.Atoms.Count >= 2;
        facts.Children.Add(open);
    }

    private string Polarity(LResult r, LStructure? s)
    {
        if (s == null || !r.Drawable || st.Graph.Atoms.Count <= 1) return "—";
        if (r.Ionic) return "Ionic";
        return LGeometry.Dipole(st.Graph, s) > 0.3 ? "Polar" : "Nonpolar";
    }

    private static string ResonanceText(LResult r)
    {
        if (r.Structures.Count <= 1) return "1";
        var eq = Math.Max(r.Equivalent, 1);
        return $"{eq} equivalent" + (r.Structures.Count > eq ? $" + {r.Structures.Count - eq} minor" : "");
    }

    private FrameworkElement? SelectedAtom(LStructure s)
    {
        var g = st.Graph;
        var id = st.Selected ?? g.Atoms.MaxBy(a => g.Degree(a.Id))?.Id;
        if (id is not { } i || g.Atom(i) is not { } a) return null;
        var d = LGeometry.Domains(g, s, i);
        var shape = LChem.Shape(d.Bonded, d.Lone);
        var symbol = ChemKit.Paint(K.Text(a.Symbol, Ty.BodyLarge).Strong(), LChem.ColorOf(a.Symbol));
        var top = K.Columns("auto, auto, *, auto",
            symbol.Col(0),
            K.Text(LChem.Name(a.Symbol), Ty.Body, B.Text2).Margin(8, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1),
            K.Text(st.Selected == null ? "most bonded atom" : "selected", Ty.Caption, B.Text2).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(3));
        var shapeText = K.Text($"Shape: {shape.Name}", Ty.BodyStrong, B.AccentText);
        return ChemKit.Card(K.V(6, top,
            K.Wrapped($"Electron groups: {d.Bonded + d.Lone} ({d.Bonded} bonded, {d.Lone} lone)", Ty.Body),
            shapeText,
            K.Text($"Bond angle: {shape.Angle}", Ty.Body)));
    }

    private FrameworkElement AtomTable(LStructure s)
    {
        var v = K.V(2, ChemKit.Heading("Every atom").Margin(0, 0, 0, 4));
        foreach (var a in st.Graph.Atoms) v.Children.Add(AtomRow(a, s));
        return ChemKit.Card(v);
    }

    private FrameworkElement AtomRow(LAtom a, LStructure s)
    {
        var e = s.Electrons(st.Graph, a.Id);
        var fc = s.Formal.GetValueOrDefault(a.Id);
        var lone = s.LoneOf(a.Id);
        var (word, tone, accent) = Status(e, LChem.Target(a.Symbol), a.Symbol);
        var symbol = ChemKit.Paint(K.Text(a.Symbol, Ty.Body).Strong(), LChem.ColorOf(a.Symbol));
        symbol.Width = 26;
        var detail = $"{e} e⁻" + (fc != 0 ? $" · FC {(fc > 0 ? "+" : "")}{fc}" : "") + (lone % 2 == 1 ? " · radical" : "");
        var status = K.Text(word, Ty.Caption).Strong();
        if (accent) status.Fg(B.AccentText); else ChemKit.Paint(status, tone, tone: true);
        var row = K.Columns("auto, *, auto", symbol.Col(0), K.Text(detail, Ty.Caption).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1),
            status.Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(2));
        row.Margin = new Thickness(4, 2, 4, 2);
        var chosen = st.Selected == a.Id;
        var b = K.Subtle(chosen ? ChemKit.Tint(row, B.Accent, 0.1, 4) : row, () => { st.Selected = a.Id; st.Change(); }, new Thickness(0), 4);
        System.Windows.Automation.AutomationProperties.SetName(b, $"{LChem.Name(a.Symbol)}: {detail}, {word}");
        return b;
    }

    /// <summary>An atom's electrons against what it aims for: its word, and its tone (or the accent, for an expanded octet).</summary>
    private static (string Word, Color? Tone, bool Accent) Status(int e, int want, string sym)
    {
        if (e == want) return (want == 2 ? "Duet" : want == 8 ? "Octet" : "Full", ChemKit.Green, false);
        if (e > want) return LChem.Period(sym) > 2 ? ("Expanded", null, true) : ("Over", ChemKit.Red, false);
        return ("Short", ChemKit.Orange, false);
    }

    private FrameworkElement? Averages(LResult r)
    {
        var eq = r.Structures.Take(Math.Max(r.Equivalent, 1)).ToList();
        var varying = st.Graph.Bonds.Where(b => eq.Select(x => x.Order(b.Id)).Distinct().Count() > 1).ToList();
        if (varying.Count == 0) return null;
        var v = K.V(4, ChemKit.Heading("Average bond orders"));
        foreach (var b in varying)
        {
            var t = K.Text($"{st.Graph.Atom(b.A)?.Symbol ?? ""}–{st.Graph.Atom(b.B)?.Symbol ?? ""}  {(r.AverageOrder.TryGetValue(b.Id, out var o) ? o : 1).ToString("0.00", CultureInfo.InvariantCulture)}", Ty.Caption);
            t.FontFeatures();
            v.Children.Add(t);
        }
        return ChemKit.Card(v);
    }
}
