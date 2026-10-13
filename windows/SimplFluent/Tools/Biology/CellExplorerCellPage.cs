using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
/// A cell in 3D (spec §4.1), the chemistry builders' three columns (BioUI.Work): at the left the parts list — every part in
/// its group with its colour and a box to hide it, a press opening its card — then Cutaway, Labels, Scale and Explode (and
/// the bacterium's Gram-positive wall); in the middle the cell in its card, its name and scale badge above it, the scale
/// bar over its corner, a banner while one part is isolated; at the right the part's card (its function, structure, size,
/// where it is found, its common mix-up; Isolate, Zoom to, Look inside), the quiz, or "About this cell". Click a part (or
/// its label, or its row) for its card, double-click to zoom to it, Escape to let go (or show all again), Tab through the
/// parts list to open each card in turn.
/// </summary>
internal sealed class CellPage : Grid
{
    private readonly ToolContext c;
    private CEKind kind = CEKind.Animal;
    private readonly BioSceneView scene = new("3D model of a cell") { PickSelects = false };
    private readonly Stack left = K.V(14);
    private readonly Stack right = K.V(14);
    private readonly ContentControl barTitle = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl banner = new() { Focusable = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
    private readonly CellScaleBar scaleBar;
    private readonly Border sceneCard;

    // (the left column's controls, made once and kept, so a slider being dragged or a row with the focus is not made again)
    private readonly ToggleSwitch cutaway = new(), labels = new(), gram = new();
    private readonly SelectorBar scale = new(new[] { "Teaching scale", "True scale" }, 0);
    private readonly Slider explode = new() { Minimum = 0, Maximum = 1, SmallChange = 0.05, LargeChange = 0.2, Value = 0 };
    private readonly TextBlock explodeValue = K.Text("0%", Ty.Caption, B.Text2);
    private readonly Stack partsList = K.V(2);
    private readonly Dictionary<string, PartRow> rows = new();
    private readonly FrameworkElement gramRow;
    private CEKind? rowsKind;
    private bool syncing;

    private readonly DispatcherTimer explodeLater = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private readonly DispatcherTimer wrongLater = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private string? flash;
    private string rightSignature = "";

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
        sceneHost.Children.Add(banner);
        var bar = K.Columns("*, auto", barTitle.Col(0), BioSceneCard.Buttons(scene).Col(1));
        sceneCard = CellUI.SceneCard(bar, sceneHost);

        // (the controls)
        cutaway.Checked += (_, _) => SetOption(() => CellExplorerModel.Cutaway = true);
        cutaway.Unchecked += (_, _) => SetOption(() => CellExplorerModel.Cutaway = false);
        labels.Checked += (_, _) => SetOption(() => CellExplorerModel.Labels = true);
        labels.Unchecked += (_, _) => SetOption(() => CellExplorerModel.Labels = false);
        gram.Checked += (_, _) => SetOption(() => CellExplorerModel.GramPositive = true, save: false);
        gram.Unchecked += (_, _) => SetOption(() => CellExplorerModel.GramPositive = false, save: false);
        scale.Changed += i => SetOption(() => CellExplorerModel.TrueScale = i == 1);
        explode.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            CellExplorerModel.Explode = e.NewValue;
            explodeValue.Text = $"{Math.Round(e.NewValue * 100)}%";
            explodeLater.Stop();
            explodeLater.Start();
        };
        explodeLater.Tick += (_, _) => { explodeLater.Stop(); ShowScene(); };
        wrongLater.Tick += (_, _) => { wrongLater.Stop(); flash = null; ApplySelection(); };
        explode.ToolTip = "Pull the organelles apart from the cell's centre";
        System.Windows.Automation.AutomationProperties.SetName(explode, "Explode");
        scale.ToolTip = "Teaching scale enlarges the tiny parts and shows fewer of them; true scale draws them at their real sizes";
        gramRow = CellUI.Toggle("Gram-positive wall", gram, "A thick peptidoglycan wall and no outer membrane, in place of the Gram-negative envelope");

        left.Children.Add(BioUI.Heading("Parts"));
        left.Children.Add(BioUI.Caption("Press a part to learn about it. Untick it to hide it."));
        left.Children.Add(partsList);
        left.Children.Add(BioUI.Heading("View").Margin(0, 6, 0, 0));
        left.Children.Add(CellUI.Toggle("Cutaway", cutaway, "Cut the front of the cell away to see inside"));
        left.Children.Add(CellUI.Toggle("Labels", labels, "Name the parts, with lines to them"));
        left.Children.Add(gramRow);
        left.Children.Add(scale);
        left.Children.Add(K.V(4, K.Columns("*, auto", K.Text("Explode", Ty.Body).Col(0), explodeValue.Col(1)), explode));

        Children.Add(BioUI.Work(left, sceneCard, right));

        CellUI.Escape(this, LetGo);
        void Changed() => Refresh();
        void Look() { rowsKind = null; rightSignature = ""; Refresh(); }
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
            if (CellExplorerModel.Quiz is { } q && q.Kind != value) CellExplorerModel.EndQuiz();
            Refresh();
        }
    }

    private CECellState State => CellExplorerModel.Cell(kind);

    private void SetOption(Action set, bool save = true)
    {
        if (syncing) return;
        set();
        if (save) CellExplorerSettings.Save();
        CellExplorerModel.Tell();
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
        scene.Show(key, () => CEScene.Content(CEBuildCache.Model(o), v), frameKey: $"{kind.Id()}|{v.Isolate}|{v.LookInside}");
        // (when the cell is built, what the parts list and the card say of it)
        if (CEBuildCache.Built(o) == null)
            Task.Run(() => CEBuildCache.Model(o)).ContinueWith(_ => Dispatcher.BeginInvoke(() => { if (Options == o) { rightSignature = ""; Controls(); Right(); } }), TaskScheduler.Default);
        var quiz = CellExplorerModel.Quiz;
        scene.ShowLabels = CellExplorerModel.Labels && quiz == null;
        ApplySelection();
        var model = CEBuildCache.Built(o);
        scene.Accessibility = CEScene.Accessibility(model, kind, o.Cutaway && v.Isolate == null, State.Hidden, State.Selected);
        scaleBar.NotToScale = o.TrueScale ? null : "Not to scale";
        Banner();
    }

    /// <summary>What glows: the selected part (all of it), the quiz's part in "Name it", a wrong answer's part for a moment;
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

    private void Banner()
    {
        var s = State;
        if (s.Isolated is not { } iso || CEParts.Part(kind, iso) is not { } p) { banner.Content = null; return; }
        var words = K.Text(s.LookInside ? $"Inside: {p.Name}" : $"Only: {p.Name}", Ty.BodyStrong);
        words.VerticalAlignment = VerticalAlignment.Center;
        var all = K.Button("Show all", () => CellExplorerModel.Isolate(kind, null), icon: SymbolRegular.ArrowExpand20);
        all.MinWidth = 0;
        all.ToolTip = "Show the whole cell again (Escape)";
        var row = K.H(12, CellUI.Swatch(p.Colour.ToColor(), 14), words, all);
        var card = K.Card(row, new Thickness(14, 8, 8, 8), 20);
        card.Opacity = 0.96;
        banner.Content = card;
    }

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
            if (Math.Abs(explode.Value - CellExplorerModel.Explode) > 1e-9) explode.Value = CellExplorerModel.Explode;
            explodeValue.Text = $"{Math.Round(CellExplorerModel.Explode * 100)}%";
            var isolated = State.Isolated != null;
            explode.IsEnabled = !isolated;
            explode.Opacity = isolated ? 0.5 : 1;
        }
        finally { syncing = false; }
        Bar();
        Parts();
    }

    private void Bar()
    {
        var o = Options;
        var badge = o.TrueScale ? K.Chip("True scale", K.Tone("green"), strong: true) : K.Chip("Not to scale", K.Tone("orange"), strong: true);
        badge.ToolTip = o.TrueScale ? "Every part at its real size" : "Tiny parts are drawn larger, and fewer of the many, so you can see them";
        barTitle.Content = K.H(10, K.Text(kind.Title(), Ty.Subtitle), badge);
    }

    // ---- the parts list ----

    /// <summary>A row of the parts list: its box (shown or hidden), and its button (its swatch and name; a press opens its card).</summary>
    private sealed class PartRow
    {
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
            partsList.Children.Clear();
            var parts = CEParts.All(kind);
            foreach (var g in CEGroups.All)
            {
                var inGroup = parts.Where(p => p.Group == g).ToList();
                if (inGroup.Count == 0) continue;
                partsList.Children.Add(K.Text(g.Title(), Ty.Caption, B.Text2).Strong().Margin(2, partsList.Children.Count == 0 ? 0 : 10, 0, 2));
                foreach (var p in inGroup) partsList.Children.Add(Row(p));
            }
        }
        var model = CEBuildCache.Built(Options);
        var s = State;
        var quiz = CellExplorerModel.Quiz != null;
        foreach (var (id, r) in rows)
        {
            var drawn = model == null || model.PartsShown.Contains(id);
            r.Box.IsChecked = !s.Hidden.Contains(id);
            r.Box.IsEnabled = drawn && !quiz;
            r.Button.IsEnabled = !quiz;
            r.Name.Opacity = drawn ? 1 : 0.55;
            var on = s.Selected == id;
            r.Wash.Opacity = on ? 0.16 : 0;
            r.Name.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            System.Windows.Automation.AutomationProperties.SetName(r.Button, on ? $"{r.Name.Text}, selected" : r.Name.Text);
        }
    }

    private FrameworkElement Row(CEPart p)
    {
        var id = p.Id;
        var box = new CheckBox { IsChecked = true, MinWidth = 0, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        box.ToolTip = $"Show or hide the {p.Name.ToLowerInvariant()}";
        System.Windows.Automation.AutomationProperties.SetName(box, $"Show {p.Name}");
        box.Checked += (_, _) => { if (!syncing) CellExplorerModel.SetVisible(kind, id, true); };
        box.Unchecked += (_, _) => { if (!syncing) CellExplorerModel.SetVisible(kind, id, false); };
        var name = K.Text(p.Name, Ty.Body);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var face = K.Columns("auto, *", CellUI.Swatch(p.Colour.ToColor()).Margin(0, 0, 10, 0).Col(0), name.Col(1));
        var b = K.Subtle(face, () => Select(id), new Thickness(8, 5, 8, 6), 4);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.ToolTip = CellUI.Plain(p.Function);
        // (Tab through the list opens each part's card in turn: keyboard access to every card)
        b.GotKeyboardFocus += (_, _) =>
        {
            if (InputManager.Current.MostRecentInputDevice is KeyboardDevice && State.Selected != id && CellExplorerModel.Quiz == null) Select(id);
        };
        var wash = new Border { CornerRadius = new CornerRadius(4), Opacity = 0, IsHitTestVisible = false };
        wash.SetResourceReference(Border.BackgroundProperty, B.Accent);
        var holder = new Grid { Children = { wash, b } };
        rows[id] = new PartRow { Box = box, Button = b, Wash = wash, Name = name };
        syncing = true;
        try { box.IsChecked = !State.Hidden.Contains(id); }
        finally { syncing = false; }
        return K.Columns("auto, *", box.Col(0), holder.Col(1));
    }

    // ---- choosing ----

    /// <summary>The part an object of the scene, or a label, belongs to ("organelle:mito:3", "inside:mito:cristae" → "mito").</summary>
    private static string? PartOf(string? id)
    {
        if (id == null) return null;
        var bits = id.Split(':');
        return bits.Length >= 2 && bits[0] is "organelle" or "inside" ? bits[1] : null;
    }

    private void Select(string? part)
    {
        if (part != null && CEParts.Part(kind, part) == null) return;
        CellExplorerModel.Select(kind, part);
    }

    private void OnPicked(string? id)
    {
        var part = PartOf(id);
        if (CellExplorerModel.Quiz is { } q && !q.Done)
        {
            if (q.Mode != CEQuizMode.Find || part == null) return;
            Answer(part);
            return;
        }
        if (part == null && State.Isolated != null) return;
        Select(part);
    }

    private void OnHover(string? id)
    {
        // (the hovered part's name as a tooltip, for when its label is hidden; never while a quiz would give it away)
        var part = PartOf(id) is { } p ? CEParts.Part(kind, p) : null;
        scene.ToolTip = part != null && CellExplorerModel.Quiz == null ? part.Name : null;
    }

    /// <summary>Escape: the quiz ended, else Isolate left, else the card closed (false: nothing to let go, the sheet closes).</summary>
    private bool LetGo()
    {
        var s = State;
        if (CellExplorerModel.Quiz != null) CellExplorerModel.EndQuiz();
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
            q == null ? "-" : $"{q.Mode}{q.Index}{q.Score}{q.MissedThis}{CellExplorerModel.QuizFeedback}", string.Join(",", s.Hidden));
        if (sig == rightSignature) return;
        rightSignature = sig;
        right.Children.Clear();
        if (q != null) Quiz(q);
        else if (s.Selected is { } id && CEParts.Part(kind, id) is { } p) Card(p);
        else About();
    }

    /// <summary>A part's card (spec §4.1 item 3, §5.1.6): its function word for word, its membranes, its real size and what
    /// the model draws, where it is found, its common mix-up; Isolate, Zoom to, Look inside.</summary>
    private void Card(CEPart p)
    {
        var s = State;
        var model = CEBuildCache.Built(Options);
        var trueScale = CellExplorerModel.TrueScale;
        var rows = new List<UIElement>
        {
            CellUI.Fact("Function", p.Function),
            CellUI.Fact("Structure", p.StructureText),
            CellUI.Fact("Size", p.Size),
            CellUI.Fact("In this model", CEParts.InThisModel(p, trueScale) + DrawnCount(p, model)),
        };
        var found = p.FoundInText + (p.FoundInNotes.Count > 0 ? "\n" + string.Join("\n", p.FoundInNotes) : "");
        rows.Add(CellUI.Fact("Found in", found));
        if (p.Mixup is { } mix) rows.Add(MixUp(mix));
        // (the actions)
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        void Act(string text, SymbolRegular icon, string tip, Action a, bool enabled = true, bool accent = false)
        {
            var b = K.Button(text, a, accent, icon);
            b.MinWidth = 0;
            b.Margin = new Thickness(0, 0, 8, 8);
            b.ToolTip = tip;
            b.IsEnabled = enabled;
            actions.Children.Add(b);
        }
        var drawn = model == null || model.PartsShown.Contains(p.Id);
        if (s.Isolated == p.Id) Act("Show all", SymbolRegular.ArrowExpand20, "Show the whole cell again (Escape)", () => CellExplorerModel.Isolate(kind, null), accent: true);
        else Act("Isolate", SymbolRegular.Filter20, "Show only this part, framed and enlarged", () => CellExplorerModel.Isolate(kind, p.Id), drawn);
        if (p.LookInside && !(s.Isolated == p.Id && s.LookInside))
            Act("Look inside", SymbolRegular.ZoomIn20, p.Id == "nucleus" ? "The nucleus cut open: its pores, nucleolus and chromatin" : "Cut lengthwise, to see inside",
                () => CellExplorerModel.Isolate(kind, p.Id, lookInside: true), drawn);
        Act("Zoom to", SymbolRegular.CenterVertical20, "Bring the camera to it (or double-click it)", () => ZoomTo(p.Id), drawn && !(s.Isolated == p.Id && s.LookInside));
        rows.Add(actions);
        var close = K.Link("Close card", () => CellExplorerModel.Select(kind, null), Ty.Caption);
        close.HorizontalAlignment = HorizontalAlignment.Left;
        close.ToolTip = "Escape";
        rows.Add(close);
        var card = BioUI.Card(p.Name, p.Colour.ToColor(), kind.Title(), rows.ToArray());
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

    /// <summary>" 24 drawn here; 3 cut away." — how many the model placed and how many show.</summary>
    private static string DrawnCount(CEPart p, CEModel? m)
    {
        if (m == null) return "";
        var placed = m.Placed.GetValueOrDefault(p.Id);
        var shown = m.Shown.GetValueOrDefault(p.Id);
        if (p.Id == "ribosome")
        {
            var free = m.Extra.GetValueOrDefault("ribosome.free");
            var er = m.Extra.GetValueOrDefault("ribosome.er");
            var line = er > 0 ? $" Here {free:#,0} free and {er:#,0} on the rough ER" : $" Here {free:#,0}";
            return line + (shown < free + er ? $"; {free + er - shown:#,0} in the cut-away part." : ".");
        }
        if (placed <= 1) return "";
        return $" Here {placed:#,0}" + (shown < placed ? $"; {placed - shown} in the cut-away part." : ".");
    }

    private void ZoomTo(string part)
    {
        if (CEBuildCache.Built(Options) is not { } m) return;
        if (m.Anchors.TryGetValue(part, out var a)) scene.ZoomTo(m.Instances[a.Instance].Id);
        else scene.ZoomTo("organelle:" + part);
        scene.Focus();
    }

    /// <summary>"About this cell" (spec §4.1 UI layout): its size, what makes it distinctive, three things to try; why cells
    /// are small (its SA:V, and the Bio Lab Calculator's); the quiz; flashcards of its parts.</summary>
    private void About()
    {
        var about = CEAbout.Of(kind);
        var model = CEBuildCache.Built(Options);
        var tries = K.V(6);
        foreach (var t in about.TryThis)
        {
            var dot = K.Text("•", Ty.Body, B.Text2).Margin(0, 0, 8, 0);
            tries.Children.Add(K.Columns("auto, *", dot.Col(0), K.Wrapped(t, Ty.Body).Col(1)));
        }
        right.Children.Add(BioUI.Card($"About this {kind.Title().ToLowerInvariant()}", BioColors.Tool("cell"), null,
            CellUI.Fact("Size", about.Size), CellUI.Fact("What makes it distinctive", about.Distinctive), K.V(6, K.Text("Things to try", Ty.Caption, B.Text2).Strong(), tries)));
        // (why cells are small: this cell's surface area for its volume)
        if (model != null && model.Volume > 0)
        {
            var sav = model.Area / model.Volume;
            var facts = BioUI.Facts(new (string, string, Color?)[]
            {
                ("Surface area", $"about {CellUI.Number(model.Area)} µm²", null),
                ("Volume", $"about {CellUI.Number(model.Volume)} µm³", null),
                ("SA:V", $"{sav.ToString(sav < 1 ? "0.00" : "0.0", CultureInfo.InvariantCulture)} µm⁻¹", null),
            });
            var why = K.Link("Why are cells small?", SurfaceToVolume);
            why.HorizontalAlignment = HorizontalAlignment.Left;
            why.ToolTip = "Work out surface area to volume in the Bio Lab Calculator, with this cell's size";
            right.Children.Add(K.V(6, facts, BioUI.Caption("A smaller cell has more membrane for each unit of volume it must supply. Compare the bacterium's SA:V with this."), why));
        }
        // (the quiz and the flashcards)
        var find = ChemKit.Wide("Find the …", () => StartQuiz(CEQuizMode.Find), icon: SymbolRegular.Search20);
        find.ToolTip = "Ten questions: press the part named";
        var name = ChemKit.Wide("Name it", () => StartQuiz(CEQuizMode.Name), icon: SymbolRegular.QuestionCircle20);
        name.ToolTip = "Ten questions: one part glows, choose its name";
        var cards = ChemKit.Wide("Make flashcards", MakeFlashcards, icon: SymbolRegular.Library20);
        cards.ToolTip = $"A Flashcards set of this cell's parts and what they do";
        right.Children.Add(ChemKit.Card(K.V(10, K.Text("Test yourself", Ty.BodyStrong), BioUI.Caption("Labels hide while you answer. Nothing is kept after the session."),
            K.Columns("*, 8, *", find.Col(0), name.Col(2)), cards)));
    }

    /// <summary>The Bio Lab Calculator's surface area and volume, with this cell's size as the AP sheet's nearest shape (the
    /// animal cell a sphere of its volume, the plant cell a box, the bacterium a cylinder).</summary>
    private void SurfaceToVolume()
    {
        var info = new Dictionary<string, string> { ["calc"] = "sav" };
        switch (kind)
        {
            case CEKind.Animal:
                info["mode"] = "sphere";
                info["r"] = Math.Pow(11 * 8 * 9.5, 1.0 / 3).ToString("0.0", CultureInfo.InvariantCulture);
                break;
            case CEKind.Plant:
                info["mode"] = "rect";
                info["l"] = "44";
                info["w"] = "26";
                info["h"] = "26";
                break;
            default:
                info["mode"] = "cylinder";
                info["r"] = "0.5";
                info["h"] = "2";
                break;
        }
        BioTools.Open(c, "biolab", info);
    }

    /// <summary>A Flashcards set "Organelles (animal)": each part's name and its function (spec §4.1 Nice). One already made
    /// is opened rather than made again.</summary>
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
        BioTools.Open(c, "fc", new Dictionary<string, string> { ["deck"] = id });
    }

    // ---- the quiz (spec §4.1 Nice) ----

    private void StartQuiz(CEQuizMode mode)
    {
        var model = CEBuildCache.Built(Options);
        var s = State;
        var drawn = (model?.PartsShown ?? CEParts.All(kind).Select(p => p.Id).ToHashSet()).Where(p => !s.Hidden.Contains(p)).ToList();
        CellExplorerModel.StartQuiz(kind, mode, drawn);
        scene.Focus();
    }

    private void Answer(string given)
    {
        if (CellExplorerModel.Quiz is not { Current: { } q } quiz) return;
        var right = quiz.Answer(given);
        // (what is named back: the part pressed in "Find", the right one in "Name it")
        var shown = CEParts.Part(kind, quiz.Mode == CEQuizMode.Find ? given : q.Part)?.Name ?? given;
        if (quiz.Mode == CEQuizMode.Find)
        {
            CellExplorerModel.QuizFeedback = right ? $"Right: that is the {shown.ToLowerInvariant()}." : $"That is the {shown.ToLowerInvariant()}. Try again.";
            if (!right)
            {
                // (a wrong press: the part pressed glows a moment, the view shakes, and its name is said)
                flash = given;
                wrongLater.Stop();
                wrongLater.Start();
                if (Motion.On) Motion.Shake(sceneCard);
            }
        }
        else CellExplorerModel.QuizFeedback = right ? $"Right: the {shown.ToLowerInvariant()}." : $"It was the {shown.ToLowerInvariant()}.";
        Say(CellExplorerModel.QuizFeedback);
        CellExplorerModel.Tell();
    }

    private void Quiz(CEQuiz q)
    {
        var v = K.V(12);
        if (q.Done)
        {
            v.Children.Add(K.Text($"{q.Score} of {q.Questions.Count}", Ty.Title));
            v.Children.Add(K.Wrapped(q.Score == q.Questions.Count ? "Every one right." : "The ones to look at again:", Ty.Body, B.Text2));
            foreach (var miss in q.Misses.Distinct())
            {
                if (CEParts.Part(kind, miss) is not { } p) continue;
                var row = K.Subtle(K.H(10, CellUI.Swatch(p.Colour.ToColor()), K.Text(p.Name, Ty.Body)), () => { CellExplorerModel.EndQuiz(); Select(miss); }, new Thickness(8, 5, 8, 6));
                row.HorizontalContentAlignment = HorizontalAlignment.Left;
                row.ToolTip = $"Open the {p.Name.ToLowerInvariant()}'s card";
                v.Children.Add(row);
            }
            var again = K.Button("Try again", () => StartQuiz(q.Mode), accent: true, icon: SymbolRegular.ArrowRepeatAll20);
            var done = K.Button("Done", CellExplorerModel.EndQuiz);
            v.Children.Add(K.H(8, again, done));
            right.Children.Add(BioUI.Card(q.Mode == CEQuizMode.Find ? "Find the part: done" : "Name it: done", BioColors.Tool("cell"), null, v));
            return;
        }
        var current = q.Current!;
        var part = CEParts.Part(kind, current.Part);
        v.Children.Add(K.Text($"Question {q.Index + 1} of {q.Questions.Count} · {q.Score} right", Ty.Caption, B.Text2));
        if (q.Mode == CEQuizMode.Find)
        {
            v.Children.Add(K.Wrapped($"Find the {part?.Name.ToLowerInvariant() ?? current.Part}.", Ty.BodyLarge).Strong());
            v.Children.Add(BioUI.Caption("Press it in the cell. Turn and zoom to look for it."));
        }
        else
        {
            v.Children.Add(K.Wrapped("What is the part that glows?", Ty.BodyLarge).Strong());
            var choices = K.V(6);
            foreach (var choice in current.Choices)
            {
                var name = choice;
                var b = ChemKit.Wide(choice, () => Answer(name));
                choices.Children.Add(b);
            }
            v.Children.Add(choices);
        }
        if (CellExplorerModel.QuizFeedback is { } said)
        {
            var good = said.StartsWith("Right", StringComparison.Ordinal);
            v.Children.Add(ChemKit.Line(good ? SymbolRegular.CheckmarkCircle20 : SymbolRegular.Info20, said, good ? K.Tone("green") : K.Tone("orange")));
        }
        var skip = K.Button("Skip", () => { q.Skip(); CellExplorerModel.QuizFeedback = null; CellExplorerModel.Tell(); });
        skip.MinWidth = 0;
        var end = K.Button("End quiz", CellExplorerModel.EndQuiz);
        end.MinWidth = 0;
        end.ToolTip = "Escape";
        v.Children.Add(K.H(8, skip, end));
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
