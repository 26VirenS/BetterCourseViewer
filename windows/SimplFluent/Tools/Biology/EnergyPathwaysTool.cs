using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// Photosynthesis & Respiration (spec §4.10, §5.8): a 2D map of a plant cell's chloroplast and mitochondrion with the
// cytosol between them, on four pages picked in the header (the Mac's):
// - Map: press a stage — the light reactions, the Calvin cycle, glycolysis, pyruvate oxidation, the citric acid cycle,
//   oxidative phosphorylation, and fermentation as a branch — to light where it happens and read what goes in and out
//   and its counts per glucose (§5.8's table, word for word); "Plants respire too" shows the cell by day and by night.
// - Respiration: one glucose step by step, with running totals of ATP, NADH, FADH₂ and CO₂, and where its six carbons
//   are (or fermenting without O₂, to lactate or ethanol).
// - Photosynthesis: one turn of the light reactions and the Calvin cycle, to one G3P.
// - Follow the atoms: the O in O₂ back to water (not CO₂), and the C in glucose from CO₂ and back to CO₂.
// A token carries the molecule along the map as the step-through goes. The data and running totals are
// EnergyPathwaysModel's, which keeps where each step-through is, so it outlives this view.
// Launch (--tool energy --tool-state …): map[:stage=<id>][:respire][:night] | respiration[:step=<n>][:ferment=lactate|ethanol]
// | photosynthesis[:step=<n>] | atoms[:trace=oxygen|carbon][:step=<n>].

#pragma warning disable CA2255
internal static class EnergyPathwaysToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("energy", (c, state) => new EnergyPathwaysTool(c, state), EnergyPathwaysTool.Pages);
}
#pragma warning restore CA2255

/// <summary>Photosynthesis & Respiration: its four pages over one map.</summary>
internal sealed class EnergyPathwaysTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("map", "Map", SymbolRegular.Map24, "Where each stage happens, and what goes in and out"),
        new("respiration", "Respiration", SymbolRegular.Fire24, "Follow one glucose through cellular respiration"),
        new("photosynthesis", "Photosynthesis", SymbolRegular.WeatherSunny24, "One turn of the light reactions and the Calvin cycle, to one G3P"),
        new("atoms", "Follow the atoms", SymbolRegular.Molecule24, "The O in O₂ and the C in glucose, atom by atom"),
    };

    private readonly Stack left = K.V(12);
    private readonly Stack right = K.V(14);
    private readonly EnergyMap map = new();
    private readonly ContentControl stepsHost = new() { Focusable = false };
    private readonly TextBlock mapCaption = K.Wrapped("", Ty.Caption, B.Text2);
    private readonly TextBlock mapTitle = K.Text("", Ty.BodyStrong);
    private readonly Dictionary<BioStepController, BioStepAnimator> animators = new();
    private BioStepController? stepping;
    private string? builtFor, builtLeft;
    /// <summary>A stage pressed on a step page: its card shown until the step changes.</summary>
    private string? pressed;

    public EnergyPathwaysTool(ToolContext c, BioLaunchState? state)
    {
        if (state != null) FromLaunch(state);
        var head = K.Columns("*, auto", mapTitle.Col(0), K.Text("Not to scale", Ty.Caption, B.Text3).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
        head.Margin = new Thickness(16, 14, 16, 6);
        mapCaption.Margin = new Thickness(16, 4, 16, 12);
        map.Margin = new Thickness(10, 0, 10, 0);
        map.MinHeight = 260;
        map.StagePressed += Pressed;
        // (the map takes a step-through's keys, whichever page's it is: ← → step there, Space plays)
        foreach (var sc in new[] { EnergyPathwaysModel.Respiring, EnergyPathwaysModel.Photosynthesising, EnergyPathwaysModel.Tracing })
            map.KeyDown += (_, e) => { if (ReferenceEquals(stepping, sc)) StepKeys(sc, e); };
        var card = K.Card(K.Rows("auto, *, auto", head.Row(0), map.Row(1), mapCaption.Row(2)), new Thickness(0));
        var centre = K.Rows("*, auto", card.Row(0), stepsHost.Margin(0, 14, 0, 0).Row(1));
        Children.Add(BioUI.Work(left, centre, right));
        AutomationProperties.SetName(this, "Photosynthesis and Respiration");

        void Changed() => Draw();
        void Nav(string key) { if (key == "energy") Draw(); }
        Loaded += (_, _) =>
        {
            EnergyPathwaysModel.Changed -= Changed;
            EnergyPathwaysModel.Changed += Changed;
            Appearance.Changed -= Changed;
            Appearance.Changed += Changed;
            BioNav.Changed -= Nav;
            BioNav.Changed += Nav;
            Draw();
        };
        Unloaded += (_, _) =>
        {
            EnergyPathwaysModel.Changed -= Changed;
            Appearance.Changed -= Changed;
            BioNav.Changed -= Nav;
            Follow(null);
        };
        Draw();
    }

    private static string Page => BioNav.Page("energy");

    private static void FromLaunch(BioLaunchState state)
    {
        var page = state.Page ?? (state.Has("atoms") ? "atoms" : state.Has("photosynthesis") ? "photosynthesis" : state.Has("respiration") ? "respiration" : null);
        if (page != null) BioNav.Set("energy", page, remember: false);
        var f = state.Value("ferment") ?? (state.Has("ethanol") ? "ethanol" : state.Has("lactate") ? "lactate" : null);
        if (f != null) EnergyPathwaysModel.SetFermentation(f == "ethanol" ? EnergyFermentation.Ethanol : EnergyFermentation.Lactate);
        if (state.Value("trace") is { } t) EnergyPathwaysModel.SetTrace(t == "carbon" ? EnergyTrace.Carbon : EnergyTrace.Oxygen);
        if (state.Value("stage") is { } stage) EnergyPathwaysModel.Select(stage);
        EnergyPathwaysModel.SetPlantsRespire(state.Has("respire") || state.Has("night"));
        EnergyPathwaysModel.SetDark(state.Has("night"));
        if (int.TryParse(state.Value("step"), out var step) && Controller(page ?? "map") is { } sc)
            sc.Go(Math.Clamp(step - 1, 0, sc.Steps.Count - 1), animated: false);
    }

    /// <summary>A page's step-through (none on the map).</summary>
    private static BioStepController? Controller(string page) => page switch
    {
        "respiration" => EnergyPathwaysModel.Respiring,
        "photosynthesis" => EnergyPathwaysModel.Photosynthesising,
        "atoms" => EnergyPathwaysModel.Tracing,
        _ => null,
    };

    /// <summary>The step-through keys on the map (BioStepKeys' own, for the page's controller only).</summary>
    private static void StepKeys(BioStepController c, KeyEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
        switch (e.Key)
        {
            case Key.Left: c.Back(); break;
            case Key.Right: c.Next(); break;
            case Key.Space: c.Toggle(); break;
            case Key.Home: c.First(); break;
            case Key.End: c.Last(); break;
            case >= Key.D1 and <= Key.D9 when c.JumpToGroup(e.Key - Key.D0): break;
            case >= Key.NumPad1 and <= Key.NumPad9 when c.JumpToGroup(e.Key - Key.NumPad0): break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>The step-through followed (its steps drawn as they change), or none.</summary>
    private void Follow(BioStepController? c)
    {
        if (ReferenceEquals(c, stepping) && (c == null || IsLoaded)) return;
        if (stepping != null)
        {
            stepping.Changed -= Stepped;
            if (animators.TryGetValue(stepping, out var old)) old.Stop();
        }
        stepping = c;
        if (c == null) return;
        c.Changed -= Stepped;
        c.Changed += Stepped;
        if (!animators.TryGetValue(c, out var a))
            animators[c] = a = new BioStepAnimator(c, phase => { if (ReferenceEquals(stepping, c)) map.Token(PlaceOf(phase.From), PlaceOf(phase.To), phase, Label(phase.Current)); });
        a.Start();
    }

    private void Stepped()
    {
        pressed = null;
        Draw();
    }

    private void Pressed(string stage)
    {
        if (Page == "map") EnergyPathwaysModel.Select(stage);
        else
        {
            pressed = pressed == stage ? null : stage;
            Draw();
        }
    }

    // ---- what each page shows ----

    private static (IReadOnlyList<EnergyStepInfo>? Info, IReadOnlyList<EnergyTraceStep>? Trace) Data => Page switch
    {
        "respiration" => (EnergyPathwaysModel.RespirationInfo, null),
        "photosynthesis" => (EnergyPathwaysModel.PhotosynthesisInfo, null),
        "atoms" => (null, EnergyPathwaysModel.TraceNow),
        _ => (null, null),
    };

    private static string? PlaceOf(int i)
    {
        var (info, trace) = Data;
        if (info != null) return i >= 0 && i < info.Count ? info[i].Place : null;
        if (trace != null) return i >= 0 && i < trace.Count ? trace[i].Place : null;
        return null;
    }

    private static string Label(int i)
    {
        var (info, trace) = Data;
        if (info != null && i >= 0 && i < info.Count) return info[i].Token;
        if (trace != null && i >= 0 && i < trace.Count) return trace[i].Formula.Replace("[", "").Replace("]", "");
        return "";
    }

    /// <summary>The stage whose card shows: the one pressed, else the step's, else (the map) the one selected.</summary>
    private string ShownStage()
    {
        if (Page == "map") return EnergyPathwaysModel.Selected;
        if (pressed != null) return pressed;
        var (info, trace) = Data;
        var i = stepping?.Index ?? 0;
        if (info != null && i < info.Count)
        {
            if (info[i].Stage is { } s) return s;
            // (a summary step: the last stage it came through)
            return Page == "photosynthesis" ? "calvin" : EnergyPathwaysModel.Fermentation == EnergyFermentation.None ? "oxphos" : "fermentation";
        }
        if (trace != null && i < trace.Count && trace[i].Place is { } place)
            return trace[i].Process switch
            {
                "Light reactions" or "Taken up by the roots" => "light",
                "Calvin cycle" or "Sugar made from G3P" when place == "stroma" => "calvin",
                "Glycolysis" or "Sugar made from G3P" or "Diffusion" when place == "cytosol" => "glycolysis",
                "Pyruvate oxidation" => "pyruvate",
                "Oxidative phosphorylation" => "oxphos",
                _ => EnergyPathwaysModel.Pressable.First(x => x.Place == place).Id,
            };
        return "glycolysis";
    }

    private void Draw()
    {
        var page = Page;
        var c = Controller(page);
        Follow(c);
        var key = $"{page}|{EnergyPathwaysModel.Fermentation}|{EnergyPathwaysModel.Traced}";
        if (builtFor != key)
        {
            builtFor = key;
            stepsHost.Content = c == null ? null : new BioStepControls(c, page switch
            {
                "respiration" => EnergyPathwaysModel.RespirationGroups(EnergyPathwaysModel.Fermentation),
                "photosynthesis" => EnergyPathwaysModel.PhotosynthesisGroups,
                _ => null,
            });
        }
        var shown = ShownStage();
        var i = c?.Index ?? 0;
        // the map
        var trail = page == "atoms" ? EnergyPathwaysModel.TraceNow.Take(i + 1).Select(t => t.Place).ToList() : null;
        var lit = page == "atoms" ? PlaceOf(i) : null;
        var respire = page == "map" && EnergyPathwaysModel.PlantsRespire;
        map.Draw(new EnergyMapState(shown, lit, respire, respire && EnergyPathwaysModel.Dark, trail, page == "atoms" ? EnergyPathwaysModel.Traced : null));
        if (c != null) map.Token(PlaceOf(c.Phase().From), PlaceOf(c.Phase().To), c.Phase(), Label(c.Phase().Current));
        else map.Token(null, null, BioStepPhase.At(0), "");
        mapTitle.Text = page switch
        {
            "respiration" => EnergyPathwaysModel.Fermentation == EnergyFermentation.None ? "One glucose through respiration" : "One glucose, without oxygen",
            "photosynthesis" => "One turn of photosynthesis, to one G3P",
            "atoms" => EnergyPathwaysModel.Traced == EnergyTrace.Oxygen ? "Follow the atoms: the O in O₂" : "Follow the atoms: the C in glucose",
            _ => "A plant cell: chloroplast, cytosol, mitochondrion",
        };
        mapCaption.Text = page switch
        {
            "map" when EnergyPathwaysModel.PlantsRespire => EnergyPathwaysModel.Dark
                ? "Night: the chloroplast is idle — no light, so no ATP or NADPH for the Calvin cycle. The mitochondrion goes on respiring."
                : "Day: both work. The chloroplast makes sugar and O₂; the mitochondrion respires, day and night.",
            "map" => "Press a stage to see where it happens and what goes in and out.",
            "atoms" => "The trail shows where the atom has been. ← → step through.",
            _ => "The token carries the molecule along. ← → step through; Space plays.",
        };
        Left(page, c);
        Right(page, c, shown);
        Shot.Moved();
    }

    // ---- the left column ----

    private void Left(string page, BioStepController? c)
    {
        var key = $"{page}|{EnergyPathwaysModel.Fermentation}|{EnergyPathwaysModel.Traced}|{EnergyPathwaysModel.PlantsRespire}|{EnergyPathwaysModel.Dark}|{ShownStage()}|{c?.Index}|{Appearance.Dark}";
        if (key == builtLeft) return;
        builtLeft = key;
        var focused = Keyboard.FocusedElement is Visual f && IsWithin(f, left) ? AutomationProperties.GetName(f) : null;
        left.Children.Clear();
        switch (page)
        {
            case "map":
                left.Children.Add(BioUI.Heading("Stages"));
                left.Children.Add(StageList());
                left.Children.Add(BioUI.Heading("Day and night"));
                left.Children.Add(Toggle("Plants respire too", "Which organelle works in light and in the dark", EnergyPathwaysModel.PlantsRespire, EnergyPathwaysModel.SetPlantsRespire));
                if (EnergyPathwaysModel.PlantsRespire)
                {
                    var light = new SelectorBar(new[] { "Day", "Night" }, EnergyPathwaysModel.Dark ? 1 : 0);
                    light.Changed += n => EnergyPathwaysModel.SetDark(n == 1);
                    left.Children.Add(light);
                    left.Children.Add(BioUI.Caption(EnergyPathwaysModel.RespireText));
                }
                break;
            case "respiration":
            {
                left.Children.Add(BioUI.Heading("Oxygen"));
                var o2 = new SelectorBar(new[] { "With O₂", "Without O₂" }, EnergyPathwaysModel.Fermentation == EnergyFermentation.None ? 0 : 1);
                o2.Changed += n => EnergyPathwaysModel.SetFermentation(n == 0 ? EnergyFermentation.None : EnergyFermentation.Lactate);
                left.Children.Add(o2);
                if (EnergyPathwaysModel.Fermentation != EnergyFermentation.None)
                {
                    var kind = new SelectorBar(new[] { "Lactate (animals)", "Ethanol (yeast)" }, EnergyPathwaysModel.Fermentation == EnergyFermentation.Ethanol ? 1 : 0);
                    kind.Changed += n => EnergyPathwaysModel.SetFermentation(n == 1 ? EnergyFermentation.Ethanol : EnergyFermentation.Lactate);
                    left.Children.Add(kind);
                }
                left.Children.Add(BioUI.Heading("Steps"));
                if (c != null) left.Children.Add(StepList(c));
                break;
            }
            case "photosynthesis":
                left.Children.Add(BioUI.Heading("Steps"));
                if (c != null) left.Children.Add(StepList(c));
                left.Children.Add(BioUI.Caption("The light reactions make the ATP and NADPH that the Calvin cycle spends. It takes 3 CO₂ to make one G3P; two G3P make a glucose."));
                break;
            default:
            {
                left.Children.Add(BioUI.Heading("Follow"));
                var bar = new SelectorBar(new[] { "The O in O₂", "The C in glucose" }, EnergyPathwaysModel.Traced == EnergyTrace.Oxygen ? 0 : 1);
                bar.Changed += n => EnergyPathwaysModel.SetTrace(n == 0 ? EnergyTrace.Oxygen : EnergyTrace.Carbon);
                left.Children.Add(bar);
                left.Children.Add(BioUI.Heading("Steps"));
                if (c != null) left.Children.Add(StepList(c));
                break;
            }
        }
        if (!string.IsNullOrEmpty(focused) && Find(left, focused) is IInputElement again) Dispatcher.BeginInvoke(() => Keyboard.Focus(again), System.Windows.Threading.DispatcherPriority.Input);
    }

    private static bool IsWithin(DependencyObject d, DependencyObject root)
    {
        for (var p = d; p != null; p = VisualTreeHelper.GetParent(p)) if (ReferenceEquals(p, root)) return true;
        return false;
    }

    private static DependencyObject? Find(DependencyObject root, string name)
    {
        if (root is UIElement u && u.Focusable && AutomationProperties.GetName(root) == name) return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find(VisualTreeHelper.GetChild(root, i), name) is { } hit) return hit;
        return null;
    }

    /// <summary>The seven stages as a list (the map's parts, for the keyboard and Narrator too).</summary>
    private FrameworkElement StageList()
    {
        var list = K.V(2);
        foreach (var s in EnergyPathwaysModel.Pressable)
        {
            var shown = ShownStage() == s.Id;
            var dot = new Ellipse { Width = 10, Height = 10, Fill = K.Brush(EnergyMap.StageColour(s.Id)), VerticalAlignment = VerticalAlignment.Center };
            var words = K.V(0, K.Text(s.Name, shown ? Ty.BodyStrong : Ty.Body), K.Text(s.Where, Ty.Caption, B.Text2));
            var face = K.Columns("auto, *", dot.Margin(0, 0, 10, 0).Col(0), words.Col(1));
            var id = s.Id;
            var b = K.Subtle(shown ? ChemKit.Tint(face, B.Accent, 0.12, 6) : face, () => Pressed(id), new Thickness(8, 6, 8, 6), 6);
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            b.ToolTip = $"{s.Name}: {s.Where}";
            AutomationProperties.SetName(b, shown ? $"{s.Name}, shown" : s.Name);
            AutomationProperties.SetHelpText(b, s.Where);
            list.Children.Add(b);
        }
        return list;
    }

    /// <summary>A step-through's steps as a list: a press goes there.</summary>
    private static FrameworkElement StepList(BioStepController c)
    {
        var list = K.V(2);
        foreach (var s in c.Steps)
        {
            var at = s.Index;
            var now = at == c.Index;
            var n = K.Text($"{at + 1}", Ty.Caption, now ? B.AccentText : B.Text2).Margin(0, 1, 10, 0);
            n.MinWidth = 16;
            var face = K.Columns("auto, *", n.Col(0), K.Wrapped(s.Title, now ? Ty.BodyStrong : Ty.Body).Col(1));
            var b = K.Subtle(now ? ChemKit.Tint(face, B.Accent, 0.12, 6) : face, () => { c.Pause(); c.Go(at); }, new Thickness(8, 5, 8, 5), 6);
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(b, now ? $"Step {at + 1}: {s.Title}, shown" : $"Step {at + 1}: {s.Title}");
            list.Children.Add(b);
        }
        return list;
    }

    private static FrameworkElement Toggle(string text, string help, bool on, Action<bool> set)
    {
        var s = new ToggleSwitch { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        s.Checked += (_, _) => set(true);
        s.Unchecked += (_, _) => set(false);
        s.ToolTip = help;
        AutomationProperties.SetName(s, text);
        AutomationProperties.SetHelpText(s, help);
        return K.Columns("*, auto", K.Text(text, Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), s.Col(1));
    }

    // ---- the right column ----

    private void Right(string page, BioStepController? c, string shownId)
    {
        right.Children.Clear();
        var (info, trace) = Data;
        var i = c?.Index ?? 0;
        if (c != null && info != null && i < info.Count)
        {
            var step = info[i];
            var per = page == "photosynthesis" ? "for one G3P, so far" : "per glucose, so far";
            right.Children.Add(BioUI.Card("Running totals", BioColors.Tool("energy"), $"{BioStepTiming.Caption(i, c.Steps)} · {per}", Totals(step)));
            var parts = new List<UIElement> { BioUI.FactRow("Where", step.Place == null ? "The whole cell (a summary)" : EnergyPathwaysModel.WhereText(step.Place)) };
            if (step.Inputs.Count > 0) parts.Add(Fact("In", string.Join(", ", step.Inputs)));
            if (step.Outputs.Count > 0) parts.Add(Fact("Out", string.Join(", ", step.Outputs)));
            right.Children.Add(BioUI.Card("This step", null, c.Steps[i].Title, parts.ToArray()));
            if (step.Carbons.Count > 0)
                right.Children.Add(BioUI.Card("Where the carbons are", null, page == "photosynthesis" ? "The cycle's carbons, 18 in all" : "Glucose's six carbons", Carbons(step.Carbons, null)));
        }
        if (c != null && trace != null && i < trace.Count)
        {
            var t = trace[i];
            var atom = EnergyPathwaysModel.Traced == EnergyTrace.Oxygen ? "O" : "C";
            var parts = new List<UIElement> { Formula(t.Formula, t.Molecule) };
            if (t.Carbons > 0) parts.Add(Carbons(new[] { new EnergyCarbons(t.Molecule, 1, t.Carbons) }, t.Traced));
            parts.Add(BioUI.FactRow("Where", t.Place == null ? "Outside the cell" : EnergyPathwaysModel.WhereText(t.Place)));
            parts.Add(BioUI.FactRow("How it got there", t.Process));
            right.Children.Add(BioUI.Card($"The {atom} atom is in {t.Molecule.ToLowerInvariant()}", BioColors.Tool("energy"), BioStepTiming.Caption(i, c.Steps), parts.ToArray()));
            right.Children.Add(BioUI.Card("Water on both sides", null, "Photosynthesis with all its water written out",
                BioUI.FactRow("Photosynthesis", EnergyPathwaysModel.PhotosynthesisFull),
                BioUI.Caption("The 6 O₂ come from the 12 H₂O split in the light reactions; CO₂'s oxygen ends up in the sugar and the new water.")));
        }
        if (page != "atoms") right.Children.Add(StageCard(shownId, page));
        if (page == "map")
        {
            right.Children.Add(BioUI.Card("Overall", null, null,
                BioUI.FactRow("Photosynthesis", EnergyPathwaysModel.PhotosynthesisEquation),
                BioUI.FactRow("Cellular respiration", EnergyPathwaysModel.RespirationEquation)));
            right.Children.Add(BioUI.Card("Common mix-ups", null, null, EnergyPathwaysModel.MixUps.Select(m => (UIElement)Fact(m.Title, m.Text)).ToArray()));
        }
    }

    private UIElement StageCard(string id, string page)
    {
        var s = EnergyPathwaysModel.Stage(id);
        var content = new List<UIElement>
        {
            BioUI.FactRow("Where", s.Where),
            Fact("In (per glucose unless stated)", s.In),
            Fact("Out", s.Out),
        };
        if (s.Notes.Length > 0) content.Add(Fact("Notes", s.Notes));
        if (s.Id == "oxphos") content.Add(Fact("In all", EnergyPathwaysModel.Stage("total").Out));
        if (s.MixUp != null) content.Add(Fact("Common mix-up", s.MixUp, K.Tone("orange")));
        if (page == "map" && !EnergyPathwaysModel.Working(s.Id)) content.Add(BioUI.FactRow("At night", "Idle: without light there is no ATP or NADPH from the light reactions.", K.Tone("orange")));
        var line = page == "map" ? "Pressed on the map" : pressed != null ? "Pressed: back to the step's own at the next step" : "The step's stage";
        return BioUI.Card(s.Name, EnergyMap.StageColour(s.Id), line, content.ToArray());
    }

    /// <summary>The running totals: each count, and what this step changed.</summary>
    private static FrameworkElement Totals(EnergyStepInfo step)
    {
        var v = K.V(0);
        for (var k = 0; k < step.Counts.Count; k++)
        {
            var c = step.Counts[k];
            var value = K.Text(c.Low == c.High ? c.Text : "about " + c.Text, Ty.BodyStrong);
            value.FontFeatures();
            var change = K.Text(c.ChangeText, Ty.Caption, B.AccentText);
            change.Margin = new Thickness(8, 0, 0, 0);
            change.VerticalAlignment = VerticalAlignment.Center;
            change.MinWidth = 46;
            change.TextAlignment = TextAlignment.Right;
            var row = K.Columns("*, auto, auto", K.Text(c.Name, Ty.Body, B.Text2).Col(0), value.Col(1), change.Col(2));
            row.Margin = new Thickness(0, 6, 0, 6);
            AutomationProperties.SetName(row, $"{c.Name}: {value.Text}{(c.ChangeText.Length > 0 ? $", {c.ChangeText} this step" : "")}");
            v.Children.Add(row);
            if (k < step.Counts.Count - 1) v.Children.Add(K.Divider());
        }
        return v;
    }

    /// <summary>Carbons as dots, molecule by molecule ("2 pyruvate ●●● ●●●"); one dot lit when an atom is followed.</summary>
    private static FrameworkElement Carbons(IEnumerable<EnergyCarbons> groups, int? traced)
    {
        var v = K.V(8);
        var carbon = K.Hex(Appearance.Dark ? "#c7c7cc" : "#4b4b52");
        foreach (var g in groups)
        {
            var dots = new WrapPanel { Orientation = Orientation.Horizontal };
            for (var m = 0; m < g.Count; m++)
            {
                var chain = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 10, 2) };
                for (var n = 0; n < g.CarbonsEach; n++)
                {
                    var lit = traced == n && m == 0;
                    chain.Children.Add(new Ellipse
                    {
                        Width = lit ? 12 : 9, Height = lit ? 12 : 9, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
                        Fill = K.Brush(lit ? Appearance.Accent : carbon),
                    });
                }
                dots.Children.Add(chain);
            }
            var label = g.Count == 1 ? g.Molecule : $"{g.Count} {g.Molecule}";
            var name = K.Text(label, Ty.Body);
            var each = K.Text($"{g.CarbonsEach} C each", Ty.Caption, B.Text2);
            var row = K.V(3, K.Columns("*, auto", name.Col(0), each.Col(1)), dots);
            AutomationProperties.SetName(row, $"{label}, {g.CarbonsEach} carbons each{(traced is { } t ? $"; the carbon followed is carbon {t + 1}" : "")}");
            v.Children.Add(row);
        }
        return v;
    }

    /// <summary>A formula with the followed atom (in [ ]) bold and in the accent: "H₂[O]".</summary>
    private static FrameworkElement Formula(string f, string molecule)
    {
        var t = new TextBlock { FontFamily = K.DisplayFace, FontSize = 26, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        t.Fg(B.Text);
        var rest = f;
        while (rest.IndexOf('[') is var open && open >= 0 && rest.IndexOf(']', open) is var close && close > open)
        {
            t.Inlines.Add(new Run(rest[..open]));
            t.Inlines.Add(new Run(rest[(open + 1)..close]) { Foreground = K.Brush(Appearance.Accent), FontWeight = FontWeights.Bold, TextDecorations = TextDecorations.Underline });
            rest = rest[(close + 1)..];
        }
        t.Inlines.Add(new Run(rest));
        AutomationProperties.SetName(t, $"{molecule}: {f.Replace("[", "").Replace("]", "")}");
        return K.V(2, t, K.Text(molecule, Ty.Caption, B.Text2));
    }

    /// <summary>A card's labelled row whose words may have **bold** in them (the specification's emphasis).</summary>
    private static FrameworkElement Fact(string label, string text, Color? tint = null)
    {
        var l = K.Text(label, Ty.Caption, B.Text2).Strong();
        if (tint is { } c) l.Foreground = K.Brush(K.Darkened(c));
        var words = new TextBlock { TextWrapping = TextWrapping.Wrap };
        K.Style(words, Ty.Body);
        words.Fg(B.Text);
        var parts = text.Split("**");
        for (var k = 0; k < parts.Length; k++)
            if (parts[k].Length > 0) words.Inlines.Add(new Run(parts[k]) { FontWeight = k % 2 == 1 ? FontWeights.SemiBold : FontWeights.Normal });
        var v = K.V(3, l, words);
        AutomationProperties.SetName(v, $"{label}: {text.Replace("**", "")}");
        return v;
    }
}

/// <summary>What the map shows: the stage outlined, the place lit (atoms), "Plants respire too" by day or night, and the
/// trail of places an atom has been through.</summary>
internal sealed record EnergyMapState(string Stage, string? Lit, bool Respire, bool Night, IReadOnlyList<string?>? Trail, EnergyTrace? Trace);

/// <summary>
/// The map: a plant cell's chloroplast (thylakoid stacks in the stroma, two membranes), the cytosol, and a mitochondrion
/// (its folded inner membrane round the matrix). The shapes are drawn on a 960 × 470 board that scales to the room; the
/// words, the stages' pills and the token sit over it at their places but keep their own size, so they stay readable on
/// a small window. Each stage is a pressable part and a pill (Tab reaches it); the stage shown is outlined in the accent;
/// arrows carry the molecules between the parts; the step's token moves along as the step-through goes.
/// </summary>
internal sealed class EnergyMap : Grid
{
    private const double W = 960, H = 470;
    private readonly Canvas board = new() { Width = W, Height = H, ClipToBounds = true };
    private readonly Canvas overlay = new() { ClipToBounds = true };
    private readonly Dictionary<string, List<Shape>> places = new();
    /// <summary>What sits over the board: the element, its place on the board, and whether it is centred there (in a
    /// box of <see cref="Centre"/> size) or hangs from it by its top left.</summary>
    private readonly List<(FrameworkElement E, Point At, bool Centred)> over = new();
    private readonly Border token = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 4), IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock tokenText = new() { FontWeight = FontWeights.SemiBold, FontSize = 13.5 };
    private readonly Grid tokenHost;
    private Point tokenAt = new(W / 2, H / 2);
    private readonly List<UIElement> chloroplastParts = new();
    private double scale = 1, ox, oy;

    /// <summary>A stage's part or pill pressed (its id).</summary>
    public event Action<string>? StagePressed;

    // ---- colours (light, dark) ----
    private static readonly BioHexPair Wall = new("#5f9a1e", "#7fbf3a");
    private static readonly BioHexPair Stroma = new("#e2f1cf", "#2c3d1c");
    private static readonly BioHexPair Thylakoid = new("#4f8f1b", "#6fbf2a");
    private static readonly BioHexPair ChloroplastEdge = new("#3f7a12", "#8fd14f");
    private static readonly BioHexPair Matrix = new("#fde2c4", "#4a3020");
    private static readonly BioHexPair Intermembrane = new("#fff3e4", "#33261b");
    private static readonly BioHexPair Inner = new("#d9822b", "#f0a050");
    private static readonly BioHexPair Outer = new("#b8651c", "#e8954a");
    private static readonly BioHexPair OxygenAtom = new("#d63a34", "#ff6961");
    private static readonly BioHexPair CarbonAtom = new("#4b4b52", "#c7c7cc");

    /// <summary>A stage's colour (its pill's dot, its card's swatch, its row in the list).</summary>
    public static Color StageColour(string id) => (id switch
    {
        "light" => Thylakoid,
        "calvin" => ChloroplastEdge,
        "glycolysis" or "fermentation" => new BioHexPair("#8a8a8e", "#aeaeb2"),
        "pyruvate" or "krebs" => Outer,
        "oxphos" => Inner,
        _ => Wall,
    }).ToColor();

    /// <summary>Where a token sits in each place (on the board).</summary>
    private static readonly Dictionary<string, Point> Anchors = new()
    {
        ["thylakoid"] = new(205, 212), ["stroma"] = new(232, 318), ["cytosol"] = new(488, 245), ["matrix"] = new(745, 292), ["inner"] = new(872, 292),
    };

    public EnergyMap()
    {
        Focusable = true;
        ClipToBounds = true;
        FocusVisualStyle = null;
        // (a background, so a press anywhere on the map gives it the keys)
        Background = Brushes.Transparent;
        token.Child = tokenText;
        tokenHost = new Grid { Width = Centre.Width, Height = Centre.Height, IsHitTestVisible = false, Children = { token } };
        Children.Add(new Viewbox { Child = board, Stretch = Stretch.Uniform });
        Children.Add(overlay);
        SizeChanged += (_, _) => Lay();
        PreviewMouseDown += (_, _) => Focus();
        AutomationProperties.SetName(this, "Map of a plant cell: chloroplast at the left, cytosol in the middle, mitochondrion at the right. ← → step through.");
    }

    private static SolidColorBrush Br(BioHexPair p, double alpha = 1) => K.Brush(K.WithAlpha(p.ToColor(), alpha));

    /// <summary>The overlay put where the board is now drawn (it scales with the room; the words keep their size).</summary>
    private void Lay()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        scale = Math.Min(ActualWidth / W, ActualHeight / H);
        ox = (ActualWidth - W * scale) / 2;
        oy = (ActualHeight - H * scale) / 2;
        foreach (var (e, at, centred) in over) Put(e, at, centred);
        Put(tokenHost, tokenAt, true);
    }

    private void Put(FrameworkElement e, Point at, bool centred)
    {
        var x = ox + at.X * scale;
        var y = oy + at.Y * scale;
        Canvas.SetLeft(e, centred ? x - Centre.Width / 2 : x);
        Canvas.SetTop(e, centred ? y - Centre.Height / 2 : y);
        // (the words shrink with a board drawn small, to no less than 72% — at their full size over a board drawn at half
        //  of its own, the pills ran into each other and off the cell's edge)
        var words = Math.Clamp(scale, 0.72, 1);
        e.RenderTransformOrigin = centred ? new Point(0.5, 0.5) : new Point(0, 0);
        e.RenderTransform = words < 1 ? new ScaleTransform(words, words) : Transform.Identity;
    }

    /// <summary>The room a pill, a chip or a word is centred in (its own size isn't known until it is laid out).</summary>
    private static readonly Size Centre = new(280, 40);

    /// <summary>Something over the board at a place of it.</summary>
    private void Over(FrameworkElement e, Point at, bool centred = true, bool chloro = false)
    {
        if (centred)
        {
            e.HorizontalAlignment = HorizontalAlignment.Center;
            e.VerticalAlignment = VerticalAlignment.Center;
            var box = new Grid { Width = Centre.Width, Height = Centre.Height, Children = { e } };
            over.Add((box, at, true));
            overlay.Children.Add(box);
            if (chloro) chloroplastParts.Add(box);
            return;
        }
        over.Add((e, at, false));
        overlay.Children.Add(e);
        if (chloro) chloroplastParts.Add(e);
    }

    /// <summary>The map drawn as asked (the stage shown, the place lit, day or night, an atom's trail; light or dark).</summary>
    public void Draw(EnergyMapState state)
    {
        board.Children.Clear();
        overlay.Children.Clear();
        over.Clear();
        places.Clear();
        chloroplastParts.Clear();
        var shown = EnergyPathwaysModel.Stage(state.Stage);
        var accent = Appearance.Accent;

        // the cell: its wall and membrane, the cytosol inside
        var cell = new Rectangle { Width = W - 16, Height = H - 16, RadiusX = 38, RadiusY = 38, StrokeThickness = 7, Stroke = Br(Wall, 0.55), Fill = K.Brush(K.WithAlpha(BioColors.Tool("energy"), Appearance.Dark ? 0.08 : 0.05)) };
        Canvas.SetLeft(cell, 8);
        Canvas.SetTop(cell, 8);
        board.Children.Add(cell);
        Region("cytosol", new Path { Data = new RectangleGeometry(new Rect(16, 16, W - 32, H - 32), 32, 32), Fill = Brushes.Transparent, StrokeThickness = 0 }, only: "glycolysis");
        Words("cytosol", new Point(488, 438), 12, B.Text2);
        Words("cell wall", new Point(892, 26), 11, B.Text2);

        // the chloroplast: two membranes, the stroma, the thylakoid stacks
        var cc = new Point(232, 238);
        Add(new Path { Data = new EllipseGeometry(cc, 196, 138), Fill = Br(Stroma), Stroke = Br(ChloroplastEdge), StrokeThickness = 3 }, chloro: true);
        Region("stroma", new Path { Data = new EllipseGeometry(cc, 188, 130), Fill = Br(Stroma), Stroke = Br(ChloroplastEdge, 0.8), StrokeThickness = 1.6 }, chloro: true);
        Add(new Path { Stroke = Br(Thylakoid, 0.7), StrokeThickness = 3, Data = Geometry.Parse("M 158,205 L 182,180 M 238,180 L 262,205 M 150,226 L 160,252 M 280,226 L 268,252 M 203,272 L 234,272") }, chloro: true);
        var grana = new GeometryGroup();
        foreach (var (x, y) in new[] { (130.0, 205.0), (210.0, 172.0), (290.0, 205.0), (175.0, 270.0), (262.0, 272.0) })
            for (var i = 0; i < 5; i++) grana.Children.Add(new RectangleGeometry(new Rect(x - 28, y - 30 + i * 12, 56, 10), 5, 5));
        Region("thylakoid", new Path { Data = grana, Fill = Br(Thylakoid), Stroke = Br(ChloroplastEdge), StrokeThickness = 1 }, chloro: true);
        Words("Chloroplast", new Point(232, 92), 13.5, B.Text, strong: true, chloro: true);
        Words("stroma", new Point(330, 268), 11, B.Text2, chloro: true);
        Words("thylakoids", new Point(330, 150), 11, B.Text2, chloro: true);

        // the mitochondrion: its outer membrane, the intermembrane space, the folded inner membrane round the matrix
        var mc = new Point(742, 238);
        Add(new Path { Data = new EllipseGeometry(mc, 186, 120), Fill = Br(Intermembrane), Stroke = Br(Outer), StrokeThickness = 3 });
        var folds = new StreamGeometry();
        using (var g = folds.Open())
        {
            for (var i = 0; i <= 360; i++)
            {
                var th = i * Math.PI / 180;
                var s = 1 - 0.32 * Math.Pow(0.5 + 0.5 * Math.Cos(12 * th), 7);
                var p = new Point(mc.X + 168 * s * Math.Cos(th), mc.Y + 102 * s * Math.Sin(th));
                if (i == 0) g.BeginFigure(p, true, true); else g.LineTo(p, true, true);
            }
        }
        folds.Freeze();
        Region("matrix", new Path { Data = folds, Fill = Br(Matrix), StrokeThickness = 0 }, only: "krebs");
        Region("inner", new Path { Data = folds, Fill = Brushes.Transparent, Stroke = Br(Inner), StrokeThickness = 4 }, strokeOnly: true);
        Words("Mitochondrion", new Point(742, 102), 13.5, B.Text, strong: true);
        Words("matrix", new Point(668, 298), 11, B.Text2);
        Words("inner membrane", new Point(830, 370), 11, B.Text2);

        // the molecules' way between the parts
        var ink = K.Hex(Appearance.Dark ? "#c7c7cc" : "#5b5b60");
        var sun = K.Icon(SymbolRegular.WeatherSunny24, 30, K.Hex("#e8a400"), filled: true);
        sun.IsHitTestVisible = false;
        Over(sun, new Point(52, 52), chloro: true);
        Arrow(new Point(76, 76), new Point(160, 150), new Point(108, 128), "light", ink, chloro: true, dashed: true);
        Arrow(new Point(30, 340), new Point(112, 240), new Point(50, 272), "H₂O", ink, chloro: true);
        Arrow(new Point(232, 142), new Point(620, 120), new Point(420, 36), "O₂", ink, chloro: true);
        Arrow(new Point(668, 352), new Point(332, 350), new Point(500, 470), "CO₂", ink);
        Arrow(new Point(408, 225), new Point(452, 225), new Point(430, 222), "sugar", ink, chloro: true);
        Arrow(new Point(522, 225), new Point(568, 225), new Point(545, 222), "pyruvate", ink);
        Arrow(new Point(488, 262), new Point(488, 322), new Point(492, 292), "no O₂", ink, dashed: true);
        Arrow(new Point(616, 302), new Point(540, 340), new Point(566, 298), "ATP", ink);

        // the stages' pills
        Pill("light", new Point(210, 118), shown.Id, state.Night);
        Pill("calvin", new Point(232, 352), shown.Id, state.Night);
        Pill("glycolysis", new Point(488, 165), shown.Id, state.Night);
        Pill("fermentation", new Point(488, 392), shown.Id, state.Night);
        Pill("pyruvate", new Point(650, 188), shown.Id, state.Night);
        Pill("krebs", new Point(745, 250), shown.Id, state.Night);
        // (oxidative phosphorylation's long name kept inside the cell's wall when the board is drawn small)
        Pill("oxphos", new Point(812, 332), shown.Id, state.Night);

        // "Plants respire too": what works by day and by night
        if (state.Respire)
        {
            Chip(state.Night ? "Idle at night" : "In light only", new Point(232, 64), state.Night ? SymbolRegular.WeatherMoon24 : SymbolRegular.WeatherSunny24, state.Night);
            Chip("Day and night", new Point(742, 64), SymbolRegular.ArrowRepeatAll24, false);
        }
        if (state.Night) foreach (var e in chloroplastParts) e.Opacity = 0.35;

        // the place lit: outlined in the accent
        var litPlace = state.Lit ?? shown.Place;
        if (litPlace.Length > 0 && places.TryGetValue(litPlace, out var lit))
            foreach (var s in lit)
            {
                s.Stroke = K.Brush(accent);
                s.StrokeThickness = litPlace == "inner" ? 6 : litPlace == "cytosol" ? 4 : 3.5;
                if (litPlace == "cytosol") s.StrokeDashArray = new DoubleCollection { 3, 2 };
            }

        if (state.Trail is { Count: > 0 } trail && state.Trace is { } trace) Trail(trail, trace);
        overlay.Children.Add(tokenHost);
        token.Background = K.Brush(accent);
        tokenText.Foreground = K.Brush(PTKit.InkOn(accent));
        Lay();
    }

    /// <summary>The token between two places, part way along (crossfaded with animations off); hidden with none.</summary>
    public void Token(string? from, string? to, BioStepPhase phase, string label)
    {
        Point? a = from != null && Anchors.TryGetValue(from, out var pa) ? pa : null;
        Point? b = to != null && Anchors.TryGetValue(to, out var pb) ? pb : null;
        var here = phase.Current == phase.To ? b ?? a : a ?? b;
        if (here is not { } at || label.Length == 0) { tokenHost.Visibility = Visibility.Collapsed; return; }
        tokenHost.Visibility = Visibility.Visible;
        tokenAt = a is { } p && b is { } q && !phase.Crossfade ? new Point(p.X + (q.X - p.X) * phase.T, p.Y + (q.Y - p.Y) * phase.T) : at;
        tokenText.Text = label;
        tokenHost.Opacity = phase.Crossfade ? Math.Abs(1 - 2 * phase.T) * 0.9 + 0.1 : 1;
        Put(tokenHost, tokenAt, true);
        AutomationProperties.SetName(tokenHost, label);
    }

    // ---- drawing helpers ----

    private void Add(UIElement e, bool chloro = false)
    {
        board.Children.Add(e);
        if (chloro) chloroplastParts.Add(e);
    }

    /// <summary>A pressable part of the map: a press shows its stage (<paramref name="only"/>: which, where two share it).</summary>
    private void Region(string place, Path shape, bool chloro = false, string? only = null, bool strokeOnly = false)
    {
        Add(shape, chloro);
        if (!places.TryGetValue(place, out var list)) places[place] = list = new List<Shape>();
        list.Add(shape);
        var stage = EnergyPathwaysModel.Pressable.FirstOrDefault(s => s.Place == place && (only == null || s.Id == only));
        if (stage == null) return;
        shape.Cursor = Cursors.Hand;
        shape.ToolTip = $"{stage.Name}: {stage.Where}";
        // (the inner membrane by its line only: the matrix inside it is the citric acid cycle's)
        if (strokeOnly) shape.Fill = null;
        var id = stage.Id;
        shape.MouseLeftButtonUp += (_, e) => { e.Handled = true; StagePressed?.Invoke(id); };
    }

    private void Words(string text, Point at, double size, string brush, bool strong = false, bool chloro = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontFamily = K.TextFace, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, IsHitTestVisible = false };
        t.Fg(brush);
        Over(t, at, chloro: chloro);
    }

    /// <summary>A stage's pill: its name, pressable (Tab reaches it), in the accent when it is the stage shown.</summary>
    private void Pill(string id, Point at, string shownId, bool night)
    {
        var s = EnergyPathwaysModel.Stage(id);
        var shown = shownId == id;
        var dot = new Ellipse { Width = 9, Height = 9, Fill = K.Brush(StageColour(id)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        var words = new TextBlock { Text = s.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, FontFamily = K.TextFace, VerticalAlignment = VerticalAlignment.Center };
        words.Fg(B.Text);
        var b = new System.Windows.Controls.Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, words } },
            Padding = new Thickness(9, 3, 11, 4), Cursor = Cursors.Hand, BorderThickness = new Thickness(shown ? 2 : 1),
        };
        b.SetResourceReference(FrameworkElement.StyleProperty, "SubtleButton");
        K.SetCorner(b, new CornerRadius(13));
        if (shown)
        {
            b.BorderBrush = K.Brush(Appearance.Accent);
            b.Background = K.Brush(K.WithAlpha(Appearance.Accent, 0.2));
        }
        else
        {
            b.SetResourceReference(Control.BackgroundProperty, B.Card);
            b.SetResourceReference(Control.BorderBrushProperty, B.CardStroke);
        }
        b.Click += (_, e) => { e.Handled = true; StagePressed?.Invoke(id); };
        b.ToolTip = $"{s.Name}: {s.Where}";
        AutomationProperties.SetName(b, shown ? $"{s.Name}, shown" : s.Name);
        AutomationProperties.SetHelpText(b, s.Where);
        Over(b, at, chloro: night && s.NeedsLight);
    }

    /// <summary>An arrow from one part to another, curving through a control point, with its words.</summary>
    private void Arrow(Point from, Point to, Point via, string label, Color colour, bool chloro = false, bool dashed = false)
    {
        var geo = new PathGeometry();
        var fig = new PathFigure { StartPoint = from, IsClosed = false };
        fig.Segments.Add(new QuadraticBezierSegment(via, to, true));
        geo.Figures.Add(fig);
        var line = new Path { Data = geo, Stroke = K.Brush(K.WithAlpha(colour, 0.75)), StrokeThickness = 2, IsHitTestVisible = false };
        if (dashed) line.StrokeDashArray = new DoubleCollection { 3, 2 };
        Add(line, chloro);
        // (the head along the curve's last tangent)
        var d = new Vector(to.X - via.X, to.Y - via.Y);
        d.Normalize();
        var n = new Vector(-d.Y, d.X);
        var head = new Path
        {
            Data = new PathGeometry(new[] { new PathFigure(to, new PathSegment[] { new LineSegment(to - d * 11 + n * 5.5, true), new LineSegment(to - d * 11 - n * 5.5, true) }, true) }),
            Fill = K.Brush(K.WithAlpha(colour, 0.85)), IsHitTestVisible = false,
        };
        Add(head, chloro);
        var mid = new Point(0.25 * from.X + 0.5 * via.X + 0.25 * to.X, 0.25 * from.Y + 0.5 * via.Y + 0.25 * to.Y);
        var t = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = K.TextFace, IsHitTestVisible = false, Foreground = K.Brush(colour) };
        var bg = new Border { Child = t, Padding = new Thickness(4, 0, 4, 1), CornerRadius = new CornerRadius(4), IsHitTestVisible = false };
        bg.SetResourceReference(Border.BackgroundProperty, B.Card);
        // (the card's colour laid over the solid ground, so a membrane's line under the words does not show through them:
        //  "sugar" and "pyruvate" sit where their arrows cross the chloroplast's and the mitochondrion's edges)
        var ground = new Border { CornerRadius = new CornerRadius(4), IsHitTestVisible = false };
        ground.SetResourceReference(Border.BackgroundProperty, B.Solid);
        Over(new Grid { IsHitTestVisible = false, Children = { ground, bg } }, mid, chloro: chloro);
    }

    private void Chip(string text, Point at, SymbolRegular symbol, bool off)
    {
        var tint = off ? K.Hex("#8e8e93") : BioColors.Tool("energy");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(K.Icon(symbol, 14, K.Darkened(tint), filled: true).Margin(0, 0, 6, 0));
        var t = K.Text(text, Ty.Caption);
        t.FontWeight = FontWeights.SemiBold;
        t.Foreground = K.Brush(K.Darkened(tint));
        row.Children.Add(t);
        var chip = new Border { Child = row, Padding = new Thickness(9, 3, 10, 4), CornerRadius = new CornerRadius(11), IsHitTestVisible = false, BorderThickness = new Thickness(1) };
        chip.SetResourceReference(Border.BackgroundProperty, B.Card);
        chip.BorderBrush = K.Brush(K.WithAlpha(tint, 0.6));
        Over(chip, at);
    }

    /// <summary>"Follow the atoms": the trail of places the atom has been through so far, a badge at its start.</summary>
    private void Trail(IReadOnlyList<string?> trail, EnergyTrace trace)
    {
        var colour = (trace == EnergyTrace.Oxygen ? OxygenAtom : CarbonAtom).ToColor();
        var start = trace == EnergyTrace.Oxygen ? new Point(40, 345) : new Point(690, 400);
        var pts = new List<Point> { start };
        foreach (var p in trail)
            if (p != null && Anchors.TryGetValue(p, out var a) && pts[^1] != a) pts.Add(a);
        if (pts.Count >= 2)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(pts[0], false, false);
                g.PolyLineTo(pts.Skip(1).ToList(), true, true);
            }
            geo.Freeze();
            board.Children.Add(new Path { Data = geo, Stroke = K.Brush(K.WithAlpha(colour, 0.9)), StrokeThickness = 3.5, StrokeDashArray = new DoubleCollection { 1.6, 1.2 }, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false });
        }
        var badge = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = K.Brush(colour), IsHitTestVisible = false,
            Child = new TextBlock { Text = trace == EnergyTrace.Oxygen ? "O" : "C", Foreground = K.Brush(PTKit.InkOn(colour)), FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        Over(badge, start);
        var where = new TextBlock { Text = trace == EnergyTrace.Oxygen ? "from the roots" : "from the air", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = K.Brush(K.Darkened(colour)) };
        var bg = new Border { Child = where, Padding = new Thickness(5, 1, 5, 2), CornerRadius = new CornerRadius(5), IsHitTestVisible = false };
        bg.SetResourceReference(Border.BackgroundProperty, B.Card);
        Over(bg, new Point(start.X + 16, start.Y - 10), centred: false);
    }
}
