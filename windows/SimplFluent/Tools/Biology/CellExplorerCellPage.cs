using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// A cell in 3D (spec §4.1), the chemistry builders' three columns (BioUI.Work), laid out as the Mac's: at the left the
/// parts list — every part in its group with its colour and a box to hide it, a press opening its card — then View
/// (Cutaway, Labels, the bacterium's Gram-positive wall, Scale, Explode) and Learn (the two quizzes, flashcards, why cells
/// are small); in the middle the cell in its card, its name, scale badge and "Only: …" above it, the scale bar over its
/// corner, the hovered part's name or the quiz's question over its top; at the right the part's card (its function,
/// structure, size, what the model draws, where it is found, its common mix-up; Isolate, Zoom to, Look inside, Done), the
/// quiz, or "About this cell". Click a part (or its label, or its row) for its card, double-click to zoom to it, click
/// nothing or press Escape to let go (or show all again); Tab and the arrow keys step through the parts list.
/// </summary>
internal sealed class CellPage : Grid
{
    private readonly ToolContext c;
    private CEKind kind = CEKind.Animal;
    private readonly BioSceneView scene = new("3D model of a cell") { PickSelects = false };
    private readonly Stack left = K.V(14);
    private readonly Stack right = K.V(14);
    private readonly ContentControl barTitle = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl overTop = new() { Focusable = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0), IsHitTestVisible = false };
    private readonly CellScaleBar scaleBar;

    // (the left column's controls, made once and kept, so a slider being dragged or a row with the focus is not made again)
    private readonly ToggleSwitch cutaway = new(), labels = new(), gram = new();
    private readonly SelectorBar scale = new(new[] { "Teaching", "True" }, 0);
    private readonly TextBlock scaleNote = K.Wrapped("", Ty.Caption, B.Text2);
    private readonly Slider explode = new() { Minimum = 0, Maximum = 1, SmallChange = 0.05, LargeChange = 0.2, Value = 0 };
    private readonly TextBlock explodeValue = K.Text("Off", Ty.Caption, B.Text2);
    private readonly Stack partsList = K.V(1);
    private readonly Dictionary<string, PartRow> rows = new();
    private readonly List<string> rowOrder = new();
    private readonly FrameworkElement gramRow;
    private readonly Wpf.Ui.Controls.Button findQuiz, nameQuiz;
    private readonly TextBlock madeNote = K.Wrapped("", Ty.Caption, B.Text2);
    private CEKind? rowsKind;
    private bool syncing;

    private readonly DispatcherTimer explodeLater = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private double framedExplode;
    /// <summary>A quiz answer shown a moment (the part it names glowing) before the quiz goes on.</summary>
    private readonly DispatcherTimer holdLater = new();
    private Action? afterHold;
    private string? flash;
    private string? hovered;
    private string rightSignature = "";
    private string overSignature = "";

    public CellPage(ToolContext c)
    {
        this.c = c;
        scaleBar = new CellScaleBar(scene, "µm");
        scene.Picked += OnPicked;
        scene.DoubleClicked += id => { if (CellExplorerModel.Quiz == null && PartOf(id) is { } p) Select(p); };
        scene.HoverChanged += OnHover;

        var sceneHost = new Grid { ClipToBounds = true };
        sceneHost.Children.Add(scene);
        sceneHost.Children.Add(scaleBar);
        sceneHost.Children.Add(overTop);
        var bar = K.Columns("*, auto", barTitle.Col(0), BioSceneCard.Buttons(scene).Col(1));
        var sceneCard = CellUI.SceneCard(bar, sceneHost);

        // (the controls)
        cutaway.Checked += (_, _) => SetOption(() => CellExplorerModel.Cutaway = true);
        cutaway.Unchecked += (_, _) => SetOption(() => CellExplorerModel.Cutaway = false);
        labels.Checked += (_, _) => SetOption(() => CellExplorerModel.Labels = true);
        labels.Unchecked += (_, _) => SetOption(() => CellExplorerModel.Labels = false);
        gram.Checked += (_, _) => SetOption(() => CellExplorerModel.GramPositive = true);
        gram.Unchecked += (_, _) => SetOption(() => CellExplorerModel.GramPositive = false);
        scale.Changed += i => SetOption(() => CellExplorerModel.TrueScale = i == 1);
        explode.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            CellExplorerModel.Explode = e.NewValue;
            explodeValue.Text = ExplodeWords(e.NewValue);
            explodeLater.Stop();
            explodeLater.Start();
        };
        explodeLater.Tick += (_, _) =>
        {
            explodeLater.Stop();
            if (Mouse.LeftButton == MouseButtonState.Released) Refit();
            ShowScene();
        };
        explode.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { Refit(); ShowScene(); }));
        holdLater.Tick += (_, _) =>
        {
            holdLater.Stop();
            var then = afterHold;
            afterHold = null;
            flash = null;
            then?.Invoke();
            CellExplorerModel.Tell();
        };
        explode.ToolTip = "Move every part outward from the centre; the membrane fades";
        System.Windows.Automation.AutomationProperties.SetName(explode, "Explode");
        scale.ToolTip = "Teaching scale draws tiny parts larger and fewer of them; true scale draws everything at its real size";
        System.Windows.Automation.AutomationProperties.SetName(scale, "Scale");
        gramRow = CellUI.Toggle("Gram-positive wall", gram, "A thick peptidoglycan wall and no outer membrane, instead of E. coli's thin wall and outer membrane");

        // (Learn: the quizzes, flashcards, why cells are small)
        findQuiz = ChemKit.Wide("Find the…", () => StartQuiz(CEQuizMode.Find), icon: SymbolRegular.Search20);
        findQuiz.ToolTip = "A quiz: press the part named, ten questions";
        nameQuiz = ChemKit.Wide("Name it", () => StartQuiz(CEQuizMode.Name), icon: SymbolRegular.QuestionCircle20);
        nameQuiz.ToolTip = "A quiz: one part glows; choose its name from four, ten questions";
        var cards = ChemKit.Wide("Make flashcards", MakeFlashcards, icon: SymbolRegular.Library20);
        cards.ToolTip = "A Flashcards set of this cell's parts and what each does";
        var why = ChemKit.Wide("Why are cells small?", WhySmall, icon: SymbolRegular.Cube20);
        why.ToolTip = "Surface area to volume, in the Bio Lab Calculator, with this cell's size";
        madeNote.Visibility = Visibility.Collapsed;

        left.Children.Add(BioUI.Heading("Parts"));
        left.Children.Add(partsList);
        left.Children.Add(BioUI.Heading("View").Margin(0, 6, 0, 0));
        left.Children.Add(CellUI.Toggle("Cutaway", cutaway, "Cut away the front upper-right eighth of the membrane, nuclear envelope and wall, showing how thick each is"));
        left.Children.Add(CellUI.Toggle("Labels", labels, "Name the parts at the sides, with lines to them"));
        left.Children.Add(gramRow);
        left.Children.Add(K.V(6, K.Text("Scale", Ty.Body), scale, scaleNote));
        left.Children.Add(K.V(4, K.Columns("*, auto", K.Text("Explode", Ty.Body).Col(0), explodeValue.Col(1)), explode));
        left.Children.Add(BioUI.Heading("Learn").Margin(0, 6, 0, 0));
        left.Children.Add(K.V(8, K.Columns("*, 8, *", findQuiz.Col(0), nameQuiz.Col(2)), cards, madeNote, why));

        Children.Add(BioUI.Work(left, sceneCard, right));

        CellUI.Escape(this, LetGo);
        void Changed() => Refresh();
        void Look() { rowsKind = null; rightSignature = ""; overSignature = ""; Refresh(); }
        Loaded += (_, _) =>
        {
            CellExplorerModel.Changed -= Changed;
            CellExplorerModel.Changed += Changed;
            Appearance.Changed -= Look;
            Appearance.Changed += Look;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            CellExplorerModel.Changed -= Changed;
            Appearance.Changed -= Look;
            explodeLater.Stop();
        };
    }

    /// <summary>The cell shown.</summary>
    public CEKind Kind
    {
        get => kind;
        set
        {
            if (kind == value && rowsKind != null) return;
            kind = value;
            framedExplode = CellExplorerModel.Explode;
            madeNote.Visibility = Visibility.Collapsed;
            if (CellExplorerModel.Quiz is { } q && q.Kind != value) CellExplorerModel.EndQuiz();
            Refresh();
        }
    }

    private CECellState State => CellExplorerModel.Cell(kind);

    private void SetOption(Action set)
    {
        if (syncing) return;
        set();
        CellExplorerSettings.Save();
        CellExplorerModel.Tell();
    }

    private static string ExplodeWords(double v) => v < 0.005 ? "Off" : $"{v * 100:0}%";

    /// <summary>After the explode slider is let go: the camera framing the cell as far apart as it now is (the Mac's).</summary>
    private void Refit()
    {
        if (Math.Abs(CellExplorerModel.Explode - framedExplode) <= 0.2) return;
        framedExplode = CellExplorerModel.Explode;
    }

    // ---- drawing ----

    private void Refresh()
    {
        ShowScene();
        Controls();
        Right();
    }

    private CEBuildOptions Options => CellExplorerModel.BuildOptions(kind);

    /// <summary>The scene for what is shown now (made again off the UI thread when it changes), and what lies over it.</summary>
    private void ShowScene()
    {
        var o = Options;
        var dark = Appearance.Dark;
        var v = CellExplorerModel.View(kind, dark) with { Explode = Math.Round(CellExplorerModel.Explode * 50) / 50 };
        var key = string.Join("|", kind.Id(), o.TrueScale ? "true" : "teaching", o.Cutaway ? "cut" : "whole", o.GramPositive ? "gram+" : "gram-", v.Key);
        var framed = Math.Round(framedExplode * 50) / 50;
        scene.Show(key, () => CEScene.Content(CEBuildCache.Model(o), v), frameKey: $"{kind.Id()}|{v.Isolate}|{v.LookInside}|{framed.ToString(CultureInfo.InvariantCulture)}");
        // (when the cell is built, what the parts list and the card say of it)
        if (CEBuildCache.Built(o) == null)
            Task.Run(() => CEBuildCache.Model(o)).ContinueWith(_ => Dispatcher.BeginInvoke(() => { if (Options == o) { rightSignature = ""; Controls(); Right(); } }), TaskScheduler.Default);
        var quiz = CellExplorerModel.Quiz;
        scene.ShowLabels = CellExplorerModel.Labels && quiz == null;
        ApplySelection();
        var model = CEBuildCache.Built(o);
        scene.Accessibility = CEScene.Accessibility(model, kind, o.Cutaway && v.Isolate == null, State.Hidden, State.Selected);
        scaleBar.NotToScale = o.TrueScale ? null : "Not to scale";
        OverTop();
    }

    /// <summary>What glows: the selected part (all of it), the quiz's part in "Name it", an answer's part for a moment;
    /// nothing while the isolated part is the selected one (there is nothing else to dim), but a pore or the nucleolus
    /// picked inside the nucleus cut open.</summary>
    private void ApplySelection()
    {
        var s = State;
        var sel = s.Isolated != null && s.Isolated == s.Selected ? null : s.Selected;
        if (CellExplorerModel.Quiz is { } q && !q.Done) sel = q.Mode == CEQuizMode.Name ? q.Current?.Part : null;
        if (flash != null) sel = flash;
        scene.Selection = sel == null ? null : "organelle:" + sel;
    }

    /// <summary>Over the scene's top: the quiz's question and what was said, else the name of the part under the pointer.</summary>
    private void OverTop()
    {
        var q = CellExplorerModel.Quiz;
        var hoveredPart = hovered != null && q == null ? CEParts.Part(kind, hovered) : null;
        var sig = string.Join("|", kind, q == null ? "-" : $"{q.Mode}{q.Index}{q.Done}", CellExplorerModel.QuizFeedback, hoveredPart?.Id, Appearance.Dark);
        if (sig == overSignature) return;
        overSignature = sig;
        if (q is { Current: { } current })
        {
            var name = CEParts.Part(kind, current.Part)?.Name ?? current.Part;
            var v = K.V(4, K.Text(q.Mode == CEQuizMode.Find ? $"Find the {CEPart.InSentence(name)}" : "Name the glowing part", Ty.BodyStrong).Align(HorizontalAlignment.Center, VerticalAlignment.Center));
            v.Children.Add(CellExplorerModel.QuizFeedback is { } said
                ? Said(said)
                : K.Text($"Question {q.Index + 1} of {q.Questions.Count}", Ty.Caption, B.Text2).Align(HorizontalAlignment.Center, VerticalAlignment.Center));
            var card = K.Card(v, new Thickness(16, 9, 16, 9), 14);
            card.Opacity = 0.96;
            overTop.Content = card;
        }
        else if (hoveredPart != null)
        {
            var words = K.Text(hoveredPart.Label, Ty.Caption);
            words.FontWeight = FontWeights.SemiBold;
            var pill = K.Card(words, new Thickness(10, 4, 10, 5), 12);
            pill.Opacity = 0.94;
            overTop.Content = pill;
        }
        else overTop.Content = null;
    }

    /// <summary>A quiz answer's words, green with a tick when right, orange with a cross when not.</summary>
    private static FrameworkElement Said(string said)
    {
        var good = Good(said);
        return ChemKit.Line(good ? SymbolRegular.CheckmarkCircle20 : SymbolRegular.DismissCircle20, said, good ? K.Tone("green") : K.Tone("orange"));
    }

    private static bool Good(string said) => said.StartsWith("Yes", StringComparison.Ordinal);

    private void Controls()
    {
        syncing = true;
        try
        {
            cutaway.IsChecked = CellExplorerModel.Cutaway;
            labels.IsChecked = CellExplorerModel.Labels;
            gram.IsChecked = CellExplorerModel.GramPositive;
            gramRow.Visibility = kind == CEKind.Bacterium ? Visibility.Visible : Visibility.Collapsed;
            scale.Select(CellExplorerModel.TrueScale ? 1 : 0, false);
            scaleNote.Text = CellExplorerModel.TrueScale
                ? "Every part at its real size, with 20,000 ribosomes."
                : "Ribosomes, vesicles and filaments drawn larger, and fewer of them, so you can see them.";
            if (Math.Abs(explode.Value - CellExplorerModel.Explode) > 1e-9) explode.Value = CellExplorerModel.Explode;
            explodeValue.Text = ExplodeWords(CellExplorerModel.Explode);
            var isolated = State.Isolated != null;
            explode.IsEnabled = !isolated;
            explode.Opacity = isolated ? 0.5 : 1;
            var quiz = CellExplorerModel.Quiz != null;
            findQuiz.IsEnabled = !quiz;
            nameQuiz.IsEnabled = !quiz;
        }
        finally { syncing = false; }
        Bar();
        Parts();
    }

    /// <summary>The scene card's top bar: the cell's name, its scale badge, and while one part is isolated "Only: …" (or
    /// "Inside: …"), which shows the whole cell again.</summary>
    private void Bar()
    {
        var o = Options;
        var badge = o.TrueScale ? K.Chip("True scale", K.Tone("green"), strong: true) : K.Chip("Teaching scale", K.Tone("orange"), strong: true);
        badge.ToolTip = o.TrueScale ? "Every part at its real size" : "Small parts drawn larger than they are, and fewer of them";
        var title = K.Text(kind.Title(), Ty.Subtitle);
        title.VerticalAlignment = VerticalAlignment.Center;
        var h = K.H(10, title, badge);
        var s = State;
        if (s.Isolated is { } iso && CEParts.Part(kind, iso) is { } p)
        {
            var only = K.Button(s.LookInside ? $"Inside: {p.Label}" : $"Only: {p.Label}", () => CellExplorerModel.Isolate(kind, null), accent: true, icon: SymbolRegular.DismissCircle20);
            only.MinWidth = 0;
            only.ToolTip = "Show the whole cell again (Escape)";
            h.Children.Add(only);
        }
        barTitle.Content = h;
    }

    // ---- the parts list ----

    /// <summary>A row of the parts list: its box (shown or hidden), and its button (its swatch and label; a press opens its card).</summary>
    private sealed class PartRow
    {
        public required CEPart Part { get; init; }
        public required CheckBox Box { get; init; }
        public required Button Button { get; init; }
        public required Border Wash { get; init; }
        public required TextBlock Name { get; init; }
    }

    private void Parts()
    {
        if (rowsKind != kind)
        {
            rowsKind = kind;
            rows.Clear();
            rowOrder.Clear();
            partsList.Children.Clear();
            var parts = CEParts.All(kind);
            foreach (var g in CEGroups.All)
            {
                var inGroup = parts.Where(p => p.Group == g).ToList();
                if (inGroup.Count == 0) continue;
                partsList.Children.Add(K.Text(g.Title(), Ty.Caption, B.Text2).Strong().Margin(2, partsList.Children.Count == 0 ? 0 : 11, 0, 3));
                foreach (var p in inGroup) partsList.Children.Add(Row(p));
            }
            System.Windows.Automation.AutomationProperties.SetName(partsList, $"Parts of the {kind.Title().ToLowerInvariant()}");
        }
        var model = CEBuildCache.Built(Options);
        var s = State;
        var quiz = CellExplorerModel.Quiz != null;
        foreach (var (id, r) in rows)
        {
            var on = !s.Hidden.Contains(id);
            var drawn = model == null || model.PartsShown.Contains(id);
            r.Box.IsChecked = on;
            r.Box.IsEnabled = drawn && !quiz;
            r.Box.ToolTip = on ? $"Hide the {r.Part.LabelInSentence}" : $"Show the {r.Part.LabelInSentence}";
            r.Button.IsEnabled = !quiz;
            r.Name.Opacity = on && drawn ? 1 : 0.55;
            var selected = s.Selected == id;
            r.Wash.Opacity = selected ? 0.16 : 0;
            r.Name.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            System.Windows.Automation.AutomationProperties.SetName(r.Button, selected ? $"{r.Part.Name}, selected" : r.Part.Name);
            System.Windows.Automation.AutomationProperties.SetHelpText(r.Button, on ? "Shows its card" : "Hidden in the model; shows its card");
        }
    }

    private FrameworkElement Row(CEPart p)
    {
        var id = p.Id;
        var box = new CheckBox { IsChecked = true, MinWidth = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(box, $"Show {p.Name}");
        box.Checked += (_, _) => { if (!syncing) SetVisible(id, true); };
        box.Unchecked += (_, _) => { if (!syncing) SetVisible(id, false); };
        var name = K.Text(p.Label, Ty.Body);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var face = K.Columns("auto, *", CellUI.Swatch(p.Colour.ToColor()).Margin(0, 0, 8, 0).Col(0), name.Col(1));
        var b = K.Subtle(face, () => Select(id, follow: true), new Thickness(6, 4, 6, 5), 7);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.ToolTip = p.Name;
        // (Tab through the list opens each part's card in turn; ↑ and ↓ step to the part before or after)
        b.GotKeyboardFocus += (_, _) =>
        {
            if (InputManager.Current.MostRecentInputDevice is KeyboardDevice && State.Selected != id && CellExplorerModel.Quiz == null) Select(id, follow: true);
        };
        b.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down) || CellExplorerModel.Quiz != null) return;
            var order = rowOrder.Where(o => !State.Hidden.Contains(o)).ToList();
            var i = order.IndexOf(id);
            var next = i < 0 ? 0 : Math.Clamp(i + (e.Key == Key.Up ? -1 : 1), 0, order.Count - 1);
            if (order.Count > 0 && rows.TryGetValue(order[next], out var r)) r.Button.Focus();
            e.Handled = true;
        };
        var wash = new Border { CornerRadius = new CornerRadius(7), Opacity = 0, IsHitTestVisible = false };
        wash.SetResourceReference(Border.BackgroundProperty, B.Accent);
        var holder = new Grid { Children = { wash, b } };
        rows[id] = new PartRow { Part = p, Box = box, Button = b, Wash = wash, Name = name };
        rowOrder.Add(id);
        syncing = true;
        try { box.IsChecked = !State.Hidden.Contains(id); }
        finally { syncing = false; }
        return K.Columns("auto, *", box.Col(0), holder.Col(1));
    }

    private void SetVisible(string id, bool visible)
    {
        CellExplorerModel.SetVisible(kind, id, visible);
        CellExplorerSettings.SaveHidden(kind);
    }

    // ---- choosing ----

    /// <summary>The part an object of the scene, or a label, belongs to ("organelle:mito:3", "inside:mito:cristae" → "mito").</summary>
    private static string? PartOf(string? id)
    {
        if (id == null) return null;
        var bits = id.Split(':');
        return bits.Length >= 2 && bits[0] is "organelle" or "inside" ? bits[1] : null;
    }

    /// <summary>A part's card. From the parts list (<paramref name="follow"/>) while another part is isolated, the new
    /// part is isolated instead; a click in the scene keeps the view as it is.</summary>
    private void Select(string? part, bool follow = false)
    {
        if (part != null && CEParts.Part(kind, part) == null) return;
        CellExplorerModel.Select(kind, part, follow);
    }

    /// <summary>A click in the scene (or on a label): its part's card, or the quiz's answer; on nothing, back out of
    /// isolation, or let the card go.</summary>
    private void OnPicked(string? id)
    {
        var part = PartOf(id);
        if (CellExplorerModel.Quiz is { } q)
        {
            if (!q.Done && q.Mode == CEQuizMode.Find && part != null && afterHold == null) AnswerFind(part);
            return;
        }
        if (id == null)
        {
            if (State.Isolated != null) CellExplorerModel.Isolate(kind, null);
            else Select(null);
            return;
        }
        if (part != null) Select(part);
    }

    private void OnHover(string? id)
    {
        hovered = PartOf(id);
        OverTop();
    }

    /// <summary>Escape: the quiz ended, else Isolate left, else the card closed (false: nothing to let go, the sheet closes).</summary>
    private bool LetGo()
    {
        var s = State;
        if (CellExplorerModel.Quiz != null) EndQuiz();
        else if (s.Isolated != null) CellExplorerModel.Isolate(kind, null);
        else if (s.Selected != null) CellExplorerModel.Select(kind, null);
        else return false;
        return true;
    }

    // ---- the right column ----

    private void Right()
    {
        var s = State;
        var q = CellExplorerModel.Quiz;
        var sig = string.Join("|", kind, s.Selected, s.Isolated, s.LookInside, CellExplorerModel.TrueScale, Appearance.Dark, CEBuildCache.Built(Options) != null,
            q == null ? "-" : $"{q.Mode}{q.Index}{q.Score}{q.MissedThis}{CellExplorerModel.QuizFeedback}{afterHold != null}", string.Join(",", s.Hidden));
        if (sig == rightSignature) return;
        rightSignature = sig;
        right.Children.Clear();
        if (q != null) Quiz(q);
        else if (s.Selected is { } id && CEParts.Part(kind, id) is { } p) Card(p);
        else About();
    }

    /// <summary>A part's card (spec §4.1 item 3, §5.1.6): its function word for word, its structure, its real size, what the
    /// model draws, where it is found, its common mix-up; Isolate, Zoom to, Look inside, Done.</summary>
    private void Card(CEPart p)
    {
        var s = State;
        var model = CEBuildCache.Built(Options);
        var trueScale = CellExplorerModel.TrueScale;
        var rows = new List<UIElement>
        {
            CellUI.Fact("Function", p.Function),
            CellUI.Fact("Structure", p.StructureText),
        };
        if (p.Size != "—") rows.Add(CellUI.Fact("Size", p.Size));
        // (what the model has of it, at this scale; §5.1's Model column until the cell is built)
        var line = model != null ? model.ModelLine(p) : CEParts.InThisModel(p, trueScale);
        if (line.Length > 0) rows.Add(CellUI.Fact("In this model", line));
        var found = K.V(3, K.Text("Found in", Ty.Caption, B.Text2).Strong(), K.Wrapped(p.FoundInText, Ty.Body));
        foreach (var n in p.FoundInNotes) found.Children.Add(K.Wrapped(n, Ty.Caption, B.Text2));
        System.Windows.Automation.AutomationProperties.SetName(found, "Found in: " + string.Join(" ", new[] { p.FoundInText }.Concat(p.FoundInNotes)));
        rows.Add(found);
        if (p.Mixup is { } mix) rows.Add(MixUp(mix));
        if (s.Hidden.Contains(p.Id))
        {
            var show = K.Button("Show it in the model", () => SetVisible(p.Id, true), icon: SymbolRegular.Eye20);
            show.HorizontalAlignment = HorizontalAlignment.Left;
            rows.Add(show);
        }
        // (the actions, two to a row)
        var drawn = (model == null || model.PartsShown.Contains(p.Id)) && !s.Hidden.Contains(p.Id);
        var buttons = new List<FrameworkElement>();
        Wpf.Ui.Controls.Button Act(string text, SymbolRegular icon, string tip, Action a, bool enabled = true, bool accent = false)
        {
            var b = ChemKit.Wide(text, a, accent, icon);
            b.ToolTip = tip;
            b.IsEnabled = enabled;
            buttons.Add(b);
            return b;
        }
        var isolated = s.Isolated == p.Id && !s.LookInside;
        var inside = s.Isolated == p.Id && s.LookInside;
        Act(isolated ? "Show all" : "Isolate", SymbolRegular.Filter20, $"Show only the {p.LabelInSentence}, framed and enlarged (Escape to come back)",
            () => CellExplorerModel.Isolate(kind, isolated ? null : p.Id), drawn || isolated, accent: isolated);
        Act("Zoom to", SymbolRegular.ZoomIn20, "Bring the camera to it (or double-click it)", () => ZoomTo(p.Id), drawn);
        if (CEScene.CanLookInside(p.Id))
            Act(inside ? "Whole cell" : "Look inside", SymbolRegular.Cut20, p.Id == "nucleus" ? "The nucleus cut open: envelope, pores, nucleolus and chromatin" : "One cut lengthwise, to see inside",
                () => { if (inside) CellExplorerModel.Isolate(kind, null); else CellExplorerModel.Isolate(kind, p.Id, lookInside: true); }, drawn || inside, accent: inside);
        Act("Done", SymbolRegular.Checkmark20, "Close the card (Escape)", () => CellExplorerModel.Select(kind, null));
        var grid = new UniformGrid { Columns = 2 };
        foreach (var b in buttons)
        {
            b.Margin = new Thickness(0, 0, 4, 8);
            grid.Children.Add(b);
        }
        rows.Add(grid);
        var card = BioUI.Card(p.Name, p.Colour.ToColor(), p.Group.Title(), rows.ToArray());
        right.Children.Add(card);
        if (Motion.On) Motion.FadeIn(card, 180);
    }

    private static FrameworkElement MixUp(string text)
    {
        var tone = K.Tone("orange");
        var icon = K.Icon(SymbolRegular.Lightbulb20, 16, tone, filled: true);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 1, 8, 0);
        var words = K.V(3, K.Text("Common mix-up", Ty.Caption, B.Text2).Strong(), CellUI.Rich(text));
        var row = K.Columns("auto, *", icon.Col(0), words.Col(1));
        var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Background = K.Brush(K.WithAlpha(tone, Appearance.Dark ? 0.16 : 0.1)), Child = row };
        System.Windows.Automation.AutomationProperties.SetName(b, "Common mix-up: " + CellUI.Plain(text));
        return b;
    }

    /// <summary>The camera brought to the part's instance its label points to (the part as a whole when looked inside).</summary>
    private void ZoomTo(string part)
    {
        var s = State;
        if (CEBuildCache.Built(Options) is { } m && m.Anchors.TryGetValue(part, out var a) && !(s.LookInside && s.Isolated == part)) scene.ZoomTo(m.Instances[a.Instance].Id);
        else scene.ZoomTo("organelle:" + part);
        scene.Focus();
    }

    /// <summary>"About this cell" (spec §4.1 UI layout): its size, what makes it distinctive, its surface for its volume,
    /// three things to try, and how to reach the cards.</summary>
    private void About()
    {
        var about = CEAbout.Of(kind);
        var model = CEBuildCache.Built(Options);
        var rows = new List<UIElement> { CellUI.Fact("Size", about.Size), CellUI.Fact("What makes it distinctive", about.Distinctive) };
        if (model != null)
            rows.Add(CellUI.Fact("Surface and volume", model.SurfaceLine + ". " + (kind == CEKind.Bacterium
                ? "A small cell has far more surface for its volume, so materials reach every part quickly."
                : "The bigger a cell, the less surface it has for each µm³ of inside to feed.")));
        var tries = K.V(6, K.Text("Try this", Ty.Caption, B.Text2).Strong());
        foreach (var t in about.TryThis)
        {
            var dot = K.Text("•", Ty.Body, B.Text2).Margin(0, 0, 6, 0);
            tries.Children.Add(K.Columns("auto, *", dot.Col(0), K.Wrapped(t, Ty.Body).Col(1)));
        }
        rows.Add(tries);
        rows.Add(BioUI.Caption("Click any part, its label or its row on the left for its card. Tab and the arrow keys step through the parts."));
        right.Children.Add(BioUI.Card("About this cell", BioColors.Tool("cell"), kind.Title(), rows.ToArray()));
    }

    /// <summary>The Bio Lab Calculator's surface area to volume (its Cells page), with this cell as the shape the AP sheet
    /// has for it: a sphere of the animal cell's volume, the plant cell's box, the bacterium's cylinder. Handed over in
    /// both apps' words (BioLabModel.TakeCells): savShape, savSize (sizes across: the sphere's diameter, the box's l,w,h,
    /// the cylinder's diameter and length), savUnit and from.</summary>
    private void WhySmall()
    {
        var info = new Dictionary<string, string> { ["from"] = "cell", ["savUnit"] = "µm" };
        switch (kind)
        {
            case CEKind.Animal:
                var v = CEBuildCache.Built(Options)?.Volume ?? 0;
                var d = v > 0 ? Math.Cbrt(6 * v / Math.PI) : 19;
                info["savShape"] = "sphere";
                info["savSize"] = d.ToString("0.0", CultureInfo.InvariantCulture);
                break;
            case CEKind.Plant:
                info["savShape"] = "box";
                info["savSize"] = "44,26,26";
                break;
            default:
                info["savShape"] = "cylinder";
                info["savSize"] = "1,2";
                break;
        }
        BioNav.Set("biolab", "cells", remember: false);
        BioTools.Open(c, "biolab", info);
    }

    /// <summary>A Flashcards set "Organelles (animal)": each part's name and its function (spec §4.1 Nice), said under the
    /// button and opened. One already made is opened rather than made again.</summary>
    private void MakeFlashcards()
    {
        var title = $"Organelles ({kind.Id()})";
        var deck = FlashcardStore.Decks.FirstOrDefault(d => d.Name == title);
        var id = deck?.Id ?? FlashcardStore.Add(new FCDeck
        {
            Name = title,
            Desc = $"The parts of {kind.Phrase()} and what they do, from Cell Explorer.",
            Cards = CEParts.All(kind).Select(p => new FCCard(p.Name, CellUI.Plain(p.Function))).ToList(),
        });
        madeNote.Text = $"Made “{title}” in Flashcards.";
        madeNote.Visibility = Visibility.Visible;
        BioTools.Open(c, "fc", new Dictionary<string, string> { ["deck"] = id });
    }

    // ---- the quiz (spec §4.1 Nice) ----

    private void StartQuiz(CEQuizMode mode)
    {
        holdLater.Stop();
        afterHold = null;
        flash = null;
        var model = CEBuildCache.Built(Options);
        var s = State;
        var drawn = (model?.PartsShown ?? CEParts.All(kind).Where(p => p.DefaultVisible).Select(p => p.Id).ToHashSet()).Where(p => !s.Hidden.Contains(p)).ToList();
        CellExplorerModel.StartQuiz(kind, mode, drawn);
        scene.Focus();
    }

    private void EndQuiz()
    {
        holdLater.Stop();
        afterHold = null;
        flash = null;
        CellExplorerModel.EndQuiz();
    }

    /// <summary>The answer glowing a moment, then <paramref name="then"/>.</summary>
    private void Hold(string part, double seconds, Action then)
    {
        flash = part;
        afterHold = then;
        holdLater.Stop();
        holdLater.Interval = TimeSpan.FromSeconds(seconds);
        holdLater.Start();
    }

    /// <summary>"Find the …": the part pressed. Right: on to the next. Wrong: its name, and the question wobbles.</summary>
    private void AnswerFind(string part)
    {
        if (CellExplorerModel.Quiz is not { Mode: CEQuizMode.Find, Current: not null } quiz) return;
        var name = CEPart.InSentence(CEParts.Part(kind, part)?.Name ?? part);
        var right = quiz.Answer(part);
        var said = right ? $"Yes: the {name}." : $"That’s the {name}.";
        CellExplorerModel.QuizFeedback = said;
        Hold(part, right ? 0.7 : 1.1, () => { if (right && CellExplorerModel.QuizFeedback == said) CellExplorerModel.QuizFeedback = null; });
        Say(said);
        CellExplorerModel.Tell();
        if (!right && Motion.On && overTop.Content is UIElement prompt) Motion.Shake(prompt);
    }

    /// <summary>"Name it": a name chosen; the answer shown a moment, then the next part glows.</summary>
    private void AnswerName(string chosen)
    {
        if (CellExplorerModel.Quiz is not { Mode: CEQuizMode.Name, Current: { } current } quiz) return;
        var rightName = CEParts.Part(kind, current.Part)?.Name ?? current.Part;
        var right = quiz.Answer(chosen);
        var said = right ? $"Yes: {rightName}." : $"It’s the {CEPart.InSentence(rightName)}.";
        CellExplorerModel.QuizFeedback = said;
        Hold(current.Part, right ? 0.8 : 1.6, () => { if (CellExplorerModel.QuizFeedback == said) CellExplorerModel.QuizFeedback = null; });
        Say(said);
        CellExplorerModel.Tell();
    }

    private void Skip(CEQuiz q)
    {
        holdLater.Stop();
        afterHold = null;
        flash = null;
        q.Skip();
        CellExplorerModel.QuizFeedback = null;
        CellExplorerModel.Tell();
    }

    /// <summary>The quiz in the right column: the question's choices ("Name it") or how to answer ("Find the …"), then the
    /// score and the parts missed.</summary>
    private void Quiz(CEQuiz q)
    {
        var v = K.V(12);
        var holding = afterHold != null;
        if (q.Done)
        {
            var score = K.Text($"{q.Score} of {q.Questions.Count}", Ty.Display);
            v.Children.Add(score);
            v.Children.Add(K.Wrapped(q.Score == q.Questions.Count ? "Every one right first time." : "Right first time. The parts below are worth a second look.", Ty.Body, B.Text2));
            var misses = q.Misses.Distinct().OrderBy(m => m, StringComparer.Ordinal).Select(m => CEParts.Part(kind, m)).OfType<CEPart>().ToList();
            if (misses.Count > 0)
            {
                var list = K.V(6, K.Text("To look at again", Ty.Caption, B.Text2).Strong());
                foreach (var p in misses)
                {
                    var id = p.Id;
                    var row = K.Subtle(K.V(2, K.Text(p.Name, Ty.BodyStrong), CellUI.Rich(p.Function, Ty.Caption, B.Text2)), () => { EndQuiz(); Select(id); }, new Thickness(6, 4, 6, 5), 7);
                    row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    row.ToolTip = $"Open the {p.NameInSentence}'s card";
                    list.Children.Add(row);
                }
                v.Children.Add(list);
            }
            var again = ChemKit.Wide("Try again", () => StartQuiz(q.Mode), accent: true);
            var done = ChemKit.Wide("Done", EndQuiz);
            v.Children.Add(K.Columns("*, 8, *", again.Col(0), done.Col(2)));
        }
        else
        {
            var current = q.Current!;
            var name = CEParts.Part(kind, current.Part)?.Name ?? current.Part;
            v.Children.Add(K.Text($"Question {q.Index + 1} of {q.Questions.Count} · {q.Score} right", Ty.Body, B.Text2));
            if (q.Mode == CEQuizMode.Find)
                v.Children.Add(K.Wrapped($"Press the {CEPart.InSentence(name)} in the model. Turn it and zoom in if you need to; the labels are off.", Ty.Body));
            else
            {
                v.Children.Add(K.Text("Which part is glowing?", Ty.Body));
                var choices = K.V(8);
                foreach (var choice in current.Choices)
                {
                    var chosen = choice;
                    var b = ChemKit.Wide(choice, () => AnswerName(chosen));
                    b.IsEnabled = !holding && CellExplorerModel.QuizFeedback == null;
                    choices.Children.Add(b);
                }
                v.Children.Add(choices);
            }
            if (CellExplorerModel.QuizFeedback is { } said) v.Children.Add(Said(said));
            var skip = ChemKit.Wide("Skip", () => Skip(q));
            var end = ChemKit.Wide("End quiz", EndQuiz);
            end.ToolTip = "Escape";
            v.Children.Add(K.Columns("*, 8, *", skip.Col(0), end.Col(2)));
        }
        right.Children.Add(BioUI.Card(q.Mode == CEQuizMode.Find ? "Find the part" : "Name it", BioColors.Tool("cell"), kind.Title(), v));
    }

    /// <summary>Narrator hears the quiz's answer.</summary>
    private void Say(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(this) ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(this);
        peer?.RaiseNotificationEvent(System.Windows.Automation.AutomationNotificationKind.Other,
            System.Windows.Automation.AutomationNotificationProcessing.ImportantMostRecent, text, "simpl-cell-quiz");
    }
}
