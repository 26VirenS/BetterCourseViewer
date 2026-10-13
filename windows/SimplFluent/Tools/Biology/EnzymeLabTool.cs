using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// Enzyme Lab (spec §4.9, §5.7): a simulator of how substrate, inhibitors, temperature and pH change an enzyme's rate,
// laid out as the Mac's. At the left, the settings: the enzyme's temperature curve (a human enzyme, Taq polymerase, an
// Antarctic fish's) and pH curve (pepsin, salivary amylase, catalase, trypsin); the conditions ([S], temperature, pH);
// the kinetics (Vmax, Km); no inhibitor, a competitive or a noncompetitive one, with [I] and Ki. In the middle, three
// linked graphs: rate against [S] (with the uninhibited curve dashed beside an inhibited one, Vmax and Km marked, the
// working written out), and the relative rate against temperature and against pH, every preset's curve drawn and the
// chosen one in the tool's colour — each marking where the conditions are now. At the right, the active site in 2D (the
// substrate fitting and the enzyme closing round it — "Show binding" plays it; a competitive inhibitor in the site; a
// noncompetitive one elsewhere, the site misshapen; the enzyme unfolded once heated past Tm + 2w, for good — its outline
// wavering as heat or pH strain it), the numbers now, the activation-energy diagram (ΔG unchanged) and the common
// mix-ups. The models are illustrative, as §5.7 says. The settings are EnzymeLabModel's, so they outlive this view.
// Launch state (--tool enzyme --tool-state …), the Mac's keys and a few more: none | competitive | noncompetitive,
// enzyme=human|taq|fish (or preset=), phpreset=pepsin|amylase|catalase|trypsin, s=<mM>, i=<mM>, ph=<pH> (or a pH
// preset's id), denatured (heated past Tm + 2w and cooled back), temp=<°C> (or t=).

#pragma warning disable CA2255
internal static class EnzymeLabToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("enzyme", (c, state) => new EnzymeLabTool(c, state), null);
}
#pragma warning restore CA2255

/// <summary>Enzyme Lab: its settings, its three linked graphs, the active site and the energy diagram.</summary>
internal sealed class EnzymeLabTool : Grid
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    // (the substrate blue, a competitive inhibitor red, a noncompetitive one violet — each named beside it, never colour alone)
    internal static readonly BioHexPair Substrate = new("#1f6fd1", "#5ea8ff");
    internal static readonly BioHexPair Competitive = new("#d63a34", "#ff6961");
    internal static readonly BioHexPair Allosteric = new("#7d5cf5", "#a58bff");

    private readonly Stack controls = K.V(14);
    private readonly LabPlot rateS = new() { XLabel = "Substrate concentration [S] (mM)", YLabel = "Rate v (µmol/min)" };
    private readonly LabPlot rateT = new() { XLabel = "Temperature (°C)", YLabel = "Relative rate" };
    private readonly LabPlot ratePh = new() { XLabel = "pH", YLabel = "Relative rate" };
    private readonly LabPlot energy = new() { XLabel = "Progress of the reaction →", YLabel = "Energy", HideXTicks = true, HideYTicks = true, Height = 180 };
    private readonly TextBlock sLine = Line(), sNote = Line(), tLine = Line(), phLine = Line();
    private readonly EnzymeSiteView site = new() { Height = 190 };
    private readonly TextBlock siteText = K.Wrapped("", Ty.Body);
    private readonly TextBlock strainText = K.Wrapped("", Ty.Caption, B.Text2);
    private readonly ContentControl facts = new() { Focusable = false };
    private readonly Dictionary<string, (Slider Slider, TextBlock Label, FrameworkElement Row, Func<double> Value, Func<double, string> Text)> sliders = new();
    private SelectorBar? inhibitorBar;
    private ComboBox? tempMenu, phMenu;
    private Wpf.Ui.Controls.Button? fresh, play;
    private bool syncing;

    public EnzymeLabTool(ToolContext c, BioLaunchState? state)
    {
        if (state != null) FromLaunch(state);
        Build();
        AutomationProperties.SetName(this, "Enzyme Lab");
        Draw();
        void Changed() => Draw();
        void Look() { Build(); Draw(); }
        Loaded += (_, _) =>
        {
            EnzymeLabModel.Changed -= Changed;
            EnzymeLabModel.Changed += Changed;
            Appearance.Changed -= Look;
            Appearance.Changed += Look;
            Draw();
        };
        Unloaded += (_, _) => { EnzymeLabModel.Changed -= Changed; Appearance.Changed -= Look; };
    }

    private static TextBlock Line()
    {
        var t = K.Wrapped("", Ty.Caption, B.Text2);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    /// <summary>The three columns made (again when light or dark changes: the legends' colours are made for one).</summary>
    private void Build()
    {
        Children.Clear();
        foreach (var e in new FrameworkElement[] { rateS, rateT, ratePh, energy, sLine, sNote, tLine, phLine, site, siteText, strainText, facts }) ChemKit.Detach(e);
        BuildControls();

        rateS.MinHeight = 110;
        rateT.MinHeight = 100;
        ratePh.MinHeight = 100;
        var substrateCard = PlotCard("Rate against substrate", sLine, rateS, sNote);
        var tCard = PlotCard("Rate against temperature", tLine, rateT, null);
        var phCard = PlotCard("Rate against pH", phLine, ratePh, null);
        var pair = K.Columns("*, 14, *", tCard.Col(0), phCard.Col(2));
        var centre = K.Rows("1.2*, 14, *", substrateCard.Row(0), pair.Row(2));

        var tint = BioColors.Tool("enzyme");
        play = K.Button("Show binding", Play, icon: SymbolRegular.Play24);
        play.ToolTip = "The substrate arriving and the enzyme closing round it";
        var siteHead = K.Columns("*, auto", K.Wrapped("Active site", Ty.BodyLarge).Strong().Col(0), play.Col(1));
        AutomationProperties.SetHeadingLevel(siteHead.Children[0], AutomationHeadingLevel.Level3);
        var siteCard = K.Card(K.V(10, siteHead, site, siteText, strainText,
            BioUI.Legend(new[] { (tint, "Enzyme"), (Substrate.ToColor(), "Substrate"), (Competitive.ToColor(), "Competitive inhibitor (in the active site)"), (Allosteric.ToColor(), "Noncompetitive inhibitor (elsewhere)") })), new Thickness(16));
        var energyCard = BioUI.Card("Activation energy", null, null, energy,
            BioUI.Legend(new[] { (tint, "With the enzyme: Ea 40"), (Quiet, "Without it: Ea 75 (dashed)") }),
            BioUI.Caption("With the enzyme the barrier is lower (Ea 40 instead of 75), so more molecules get over it each second. The products end at the same level: ΔG is unchanged."));
        AutomationProperties.SetName(energy, "Energy diagram: the enzyme lowers the activation energy from 75 to 40; ΔG is unchanged at −30. Illustrative units.");
        var mixups = BioUI.Card("Common mix-ups", null, null, EnzymeLabModel.MixUps.Select(m => (UIElement)BioUI.FactRow(m.Title, m.Text)).ToArray());
        var right = K.V(14, siteCard, facts, energyCard, mixups);

        ChemKit.Detach(controls);
        Children.Add(BioUI.Work(controls, centre, right));
    }

    private static Color Quiet => K.Hex(Appearance.Dark ? "#aeaeb2" : "#8e8e93");

    private static Border PlotCard(string title, TextBlock line, LabPlot plot, TextBlock? note)
    {
        var head = K.Wrapped(title, Ty.BodyStrong);
        AutomationProperties.SetHeadingLevel(head, AutomationHeadingLevel.Level3);
        line.MaxHeight = 40;
        var rows = K.Rows("auto, auto, 8, *, auto", head.Row(0), line.Row(1), plot.Row(3));
        if (note != null)
        {
            note.Margin = new Thickness(0, 8, 0, 0);
            rows.Children.Add(note.Row(4));
        }
        return K.Card(rows, new Thickness(16, 14, 16, 12));
    }

    private static void FromLaunch(BioLaunchState state)
    {
        double? N(string k) => double.TryParse(state.Value(k), NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v : null;
        if (state.Has("none")) EnzymeLabModel.SetInhibitor(EnzymeInhibitor.None);
        if (state.Has("competitive")) EnzymeLabModel.SetInhibitor(EnzymeInhibitor.Competitive);
        if (state.Has("noncompetitive")) EnzymeLabModel.SetInhibitor(EnzymeInhibitor.Noncompetitive);
        foreach (var k in new[] { "enzyme", "preset" })
            if (state.Value(k) is { } e && EnzymeLabModel.TemperaturePresets.Any(p => p.Id == e)) EnzymeLabModel.SetTemperaturePreset(e);
        if (state.Value("phpreset") is { } pp && EnzymeLabModel.PhPresets.Any(p => p.Id == pp)) EnzymeLabModel.SetPhPreset(pp);
        if (state.Value("ph") is { } ph && EnzymeLabModel.PhPresets.Any(p => p.Id == ph)) EnzymeLabModel.SetPhPreset(ph);
        else if (N("ph") is { } phv) EnzymeLabModel.SetPh(phv);
        if (N("s") is { } s) EnzymeLabModel.SetSubstrate(s);
        if (N("i") is { } i) EnzymeLabModel.SetInhibitorConcentration(i);
        if (state.Has("denatured"))
        {
            // (heated past Tm + 2w, then cooled back to where it was: the dashed "cooled back down" curve)
            var was = EnzymeLabModel.Temperature;
            EnzymeLabModel.SetTemperature(EnzymeLabModel.DenaturesAbove(EnzymeLabModel.Preset) + 2);
            EnzymeLabModel.SetTemperature(was);
        }
        foreach (var k in new[] { "temp", "t" })
            if (N(k) is { } t) EnzymeLabModel.SetTemperature(t);
    }

    // ---- the settings ----

    private void BuildControls()
    {
        controls.Children.Clear();
        sliders.Clear();
        controls.Children.Add(BioUI.Heading("Enzyme"));
        tempMenu = Menu(EnzymeLabModel.TemperaturePresets.Select(p => (p.Id, $"{p.Name}, best at {F(EnzymeLabModel.PeakTemperature(p), 0)} °C")), EnzymeLabModel.TemperatureId, EnzymeLabModel.SetTemperaturePreset, "Temperature curve");
        phMenu = Menu(EnzymeLabModel.PhPresets.Select(p => (p.Id, $"{p.Name}, best at pH {F(p.Optimum, 1)}")), EnzymeLabModel.PhId, EnzymeLabModel.SetPhPreset, "pH curve");
        controls.Children.Add(K.V(6, K.Text("Temperature curve", Ty.Caption, B.Text2), tempMenu, K.Text("pH curve", Ty.Caption, B.Text2), phMenu));

        controls.Children.Add(BioUI.Heading("Conditions"));
        controls.Children.Add(SliderRow("s", "Substrate [S]", 0, EnzymeLabModel.SubstrateMax, 0.5, () => EnzymeLabModel.Substrate, v => $"{N(v)} mM", EnzymeLabModel.SetSubstrate, "How much substrate there is"));
        controls.Children.Add(SliderRow("t", "Temperature", 0, EnzymeLabModel.TemperatureMax, 0.5, () => EnzymeLabModel.Temperature, v => $"{N(v)} °C", EnzymeLabModel.SetTemperature,
            $"Past {N(EnzymeLabModel.DenaturesAbove(EnzymeLabModel.Preset))} °C this enzyme unfolds for good"));
        controls.Children.Add(SliderRow("ph", "pH", 0, 14, 0.1, () => EnzymeLabModel.Ph, v => F(v, 1), EnzymeLabModel.SetPh, "Acidity: each unit is a tenfold change in H⁺"));

        controls.Children.Add(BioUI.Heading("Kinetics"));
        controls.Children.Add(SliderRow("vmax", "Vmax", 10, 200, 1, () => EnzymeLabModel.Vmax, v => $"{N(v)} µmol/min", EnzymeLabModel.SetVmax, "The fastest rate, with every active site full"));
        controls.Children.Add(SliderRow("km", "Km", 0.5, 20, 0.5, () => EnzymeLabModel.Km, v => $"{N(v)} mM", EnzymeLabModel.SetKm, "The [S] at which the rate is half Vmax: lower means the enzyme binds substrate more tightly"));

        controls.Children.Add(BioUI.Heading("Inhibitor"));
        inhibitorBar = new SelectorBar(new[] { "None", "Competitive", "Noncompetitive" }, (int)EnzymeLabModel.Inhibitor);
        inhibitorBar.Changed += i => EnzymeLabModel.SetInhibitor((EnzymeInhibitor)i);
        inhibitorBar.ToolTip = "None, one that competes for the active site, or one that binds elsewhere";
        AutomationProperties.SetName(inhibitorBar, "Inhibitor");
        controls.Children.Add(inhibitorBar);
        controls.Children.Add(SliderRow("i", "Inhibitor [I]", 0, 20, 0.5, () => EnzymeLabModel.InhibitorConcentration, v => $"{N(v)} mM", EnzymeLabModel.SetInhibitorConcentration, "How much inhibitor there is"));
        controls.Children.Add(SliderRow("ki", "Ki", 0.5, 20, 0.5, () => EnzymeLabModel.Ki, v => $"{N(v)} mM", EnzymeLabModel.SetKi, "How tightly the inhibitor binds: lower is tighter"));

        fresh = K.Button("Fresh enzyme", EnzymeLabModel.Fresh, icon: SymbolRegular.ArrowReset24);
        fresh.ToolTip = "A new sample of the enzyme, never heated";
        var reset = K.Button("Reset", EnzymeLabModel.Reset);
        reset.ToolTip = "Every setting back to where it started";
        controls.Children.Add(K.H(8, fresh, reset));
        controls.Children.Add(BioUI.Caption("These curves are illustrative models, not measured data."));
    }

    private FrameworkElement SliderRow(string key, string name, double min, double max, double step, Func<double> value, Func<double, string> text, Action<double> set, string help)
    {
        var label = K.Text(text(value()), Ty.Body, B.Text2);
        var head = K.Columns("*, auto", K.Text(name, Ty.BodyStrong).Col(0), label.Col(1));
        var slider = new Slider { Minimum = min, Maximum = max, Value = value(), SmallChange = step, LargeChange = Math.Max(step, (max - min) / 10), IsMoveToPointEnabled = true };
        slider.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            // (rounded to the step here: a stepped slider would draw a tick for every step)
            set(Math.Clamp(Math.Round(e.NewValue / step) * step, min, max));
        };
        slider.ToolTip = help;
        AutomationProperties.SetName(slider, name);
        AutomationProperties.SetHelpText(slider, help);
        var row = K.V(4, head, slider);
        sliders[key] = (slider, label, row, value, text);
        return row;
    }

    private static ComboBox Menu(IEnumerable<(string Id, string Text)> items, string chosen, Action<string> pick, string name)
    {
        var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
        foreach (var (id, text) in items) box.Items.Add(Everyday.Item(text, id));
        Everyday.Choose(box, chosen);
        box.SelectionChanged += (_, _) => { if (Everyday.Chosen(box) is { } id) pick(id); };
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static string F(double v, int d) => LabNumbers.Minus(v.ToString("F" + d, Inv));

    /// <summary>A setting's number as the Mac writes it: whole numbers bare, else to a decimal or two ("2.5", "0.75").</summary>
    private static string N(double v) => LabNumbers.Minus(Math.Round(v, 2).ToString("0.##", Inv));

    private void Play() => site.Play();

    // ---- what they give ----

    private void Draw()
    {
        syncing = true;
        foreach (var (_, (slider, label, _, value, text)) in sliders)
        {
            var now = value();
            if (Math.Abs(slider.Value - now) > 1e-9) slider.Value = now;
            label.Text = text(now);
        }
        if (sliders.TryGetValue("t", out var ts)) ts.Slider.ToolTip = $"Past {N(EnzymeLabModel.DenaturesAbove(EnzymeLabModel.Preset))} °C this enzyme unfolds for good";
        inhibitorBar?.Select((int)EnzymeLabModel.Inhibitor, false);
        var on = EnzymeLabModel.Inhibitor != EnzymeInhibitor.None;
        foreach (var k in new[] { "i", "ki" }) if (sliders.TryGetValue(k, out var s)) s.Row.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (tempMenu != null) Everyday.Choose(tempMenu, EnzymeLabModel.TemperatureId);
        if (phMenu != null) Everyday.Choose(phMenu, EnzymeLabModel.PhId);
        syncing = false;
        if (fresh != null)
        {
            fresh.IsEnabled = EnzymeLabModel.Denatured;
            fresh.Appearance = EnzymeLabModel.Denatured ? ControlAppearance.Primary : ControlAppearance.Secondary;
        }

        var tint = BioColors.Tool("enzyme");
        var accent = Appearance.Accent;
        var quiet = Quiet;
        var faint = K.WithAlpha(quiet, Appearance.Dark ? 0.45 : 0.35);
        var p = EnzymeLabModel.Preset;
        var limit = EnzymeLabModel.DenaturesAbove(p);
        var a = EnzymeLabModel.ApparentNow;
        var v = EnzymeLabModel.VmaxNow;
        var rate = EnzymeLabModel.RateNow;

        // rate against [S]
        sLine.Text = $"v = Vmax·[S] / (Km + [S]) = {N(v)} × {N(EnzymeLabModel.Substrate)} / ({N(a.Km)} + {N(EnzymeLabModel.Substrate)}) = {F(rate, 1)} µmol/min";
        sNote.Text = EnzymeLabModel.Inhibitor switch
        {
            EnzymeInhibitor.Competitive => $"Dashed: without the inhibitor. Competitive: Km rises to {N(a.Km)} mM, Vmax stays; enough substrate wins.",
            EnzymeInhibitor.Noncompetitive => $"Dashed: without the inhibitor. Noncompetitive: Vmax falls to {N(v)}, Km stays; more substrate doesn't help.",
            _ => "",
        };
        sNote.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        rateS.Clear();
        rateS.XMin = 0;
        rateS.XMax = EnzymeLabModel.SubstrateMax;
        rateS.YMin = 0;
        rateS.YMax = Math.Max(EnzymeLabModel.Vmax * 1.12, 1);
        if (on) rateS.Lines.Add(new PlotLine(EnzymeLabModel.RateCurve(withInhibitor: false), quiet, Dashed: true, Name: "Without inhibitor", Width: 1.5));
        rateS.Lines.Add(new PlotLine(EnzymeLabModel.RateCurve(), tint, Name: on ? (EnzymeLabModel.Inhibitor == EnzymeInhibitor.Competitive ? "Competitive" : "Noncompetitive") : null, Width: 2.6));
        rateS.Guides.Add(new PlotGuide(v, true, $"Vmax {N(v)}", tint));
        if (a.Km <= EnzymeLabModel.SubstrateMax) rateS.Guides.Add(new PlotGuide(a.Km, false, $"Km {N(a.Km)}", tint));
        rateS.Marks.Add(new PlotMark(EnzymeLabModel.Substrate, rate, accent, $"v = {F(rate, 1)}"));
        rateS.Redraw();
        AutomationProperties.SetName(rateS, $"Rate against substrate concentration: it rises steeply, then levels off toward Vmax {N(v)}; half Vmax at Km {N(a.Km)} mM. Now {F(rate, 1)} at {N(EnzymeLabModel.Substrate)} millimolar.");

        // rate against temperature: every preset's curve, the chosen one in the tool's colour
        tLine.Text = EnzymeLabModel.Denatured ? "Denatured: dashed is the enzyme cooled back down, at a tenth." : "Faster as it warms, until it unfolds.";
        rateT.Clear();
        rateT.XMin = 0;
        rateT.XMax = EnzymeLabModel.TemperatureMax;
        rateT.YMin = 0;
        rateT.YMax = 1.15;
        rateT.HideYTicks = false;
        rateT.Bands.Add(new PlotBand(limit, EnzymeLabModel.TemperatureMax, K.WithAlpha(K.Tone("red"), Appearance.Dark ? 0.12 : 0.06)));
        foreach (var q in EnzymeLabModel.TemperaturePresets.Where(q => q.Id != p.Id))
            rateT.Lines.Add(new PlotLine(EnzymeLabModel.TemperatureCurve(q), faint, Width: 1.2));
        rateT.Lines.Add(new PlotLine(EnzymeLabModel.TemperatureCurve(p), tint, Width: 2.4));
        if (EnzymeLabModel.Denatured) rateT.Lines.Add(new PlotLine(EnzymeLabModel.TemperatureCurve(p, cooled: true), tint, Dashed: true, Name: "Cooled back down", Width: 1.6));
        foreach (var q in EnzymeLabModel.TemperaturePresets) rateT.Texts.Add(new PlotText(EnzymeLabModel.PeakTemperature(q), 1, Short(q), q.Id == p.Id));
        rateT.Guides.Add(new PlotGuide(EnzymeLabModel.Temperature, false, "", K.WithAlpha(accent, 0.45)));
        rateT.Marks.Add(new PlotMark(EnzymeLabModel.Temperature, EnzymeLabModel.TemperatureNow, accent));
        rateT.Redraw();
        AutomationProperties.SetName(rateT, $"Rate against temperature for {p.Name}: best at {F(EnzymeLabModel.PeakTemperature(p), 0)} degrees; now {F(EnzymeLabModel.TemperatureNow * 100, 0)} percent of the best.{(EnzymeLabModel.Denatured ? " Denatured: cooled back down it keeps a tenth." : "")}");

        // rate against pH: every preset's bell, the chosen one in the tool's colour (and only it named: three sit close)
        var ph = EnzymeLabModel.PhEnzyme;
        phLine.Text = "Each enzyme works best near its own pH.";
        ratePh.Clear();
        ratePh.XMin = 0;
        ratePh.XMax = 14;
        ratePh.YMin = 0;
        ratePh.YMax = 1.15;
        foreach (var q in EnzymeLabModel.PhPresets.Where(q => q.Id != ph.Id))
            ratePh.Lines.Add(new PlotLine(EnzymeLabModel.PhCurve(q), faint, Width: 1.2));
        ratePh.Lines.Add(new PlotLine(EnzymeLabModel.PhCurve(ph), tint, Width: 2.4));
        ratePh.Texts.Add(new PlotText(ph.Optimum, 1, $"{ph.Name}, pH {F(ph.Optimum, 1)}", true));
        ratePh.Guides.Add(new PlotGuide(EnzymeLabModel.Ph, false, "", K.WithAlpha(accent, 0.45)));
        ratePh.Marks.Add(new PlotMark(EnzymeLabModel.Ph, EnzymeLabModel.PhNow, accent));
        ratePh.Redraw();
        AutomationProperties.SetName(ratePh, $"Rate against pH for {ph.Name}: best at pH {F(ph.Optimum, 1)}; now {F(EnzymeLabModel.PhNow * 100, 0)} percent of the best.");

        // the active site
        var shown = EnzymeLabModel.Site;
        site.Show(shown, EnzymeLabModel.Strain);
        siteText.Text = EnzymeLabModel.SiteText(shown);
        var why = shown == EnzymeSite.Denatured ? null : EnzymeLabModel.StrainNote;
        strainText.Text = why ?? "";
        strainText.Visibility = why == null ? Visibility.Collapsed : Visibility.Visible;
        if (play != null) play.Visibility = shown == EnzymeSite.Bound ? Visibility.Visible : Visibility.Collapsed;

        // the numbers now
        var percent = EnzymeLabModel.Vmax > 0 ? rate / EnzymeLabModel.Vmax * 100 : 0;
        facts.Content = BioUI.Facts(new (string, string, Color?)[]
        {
            ("Rate v", $"{F(rate, 1)} µmol/min", accent),
            ("Of the most it could be", $"{F(percent, 0)}%", null),
            ("Vmax now", $"{N(v)} µmol/min", null),
            ("Km now", $"{N(a.Km)} mM", null),
            ("Temperature factor", F(EnzymeLabModel.TemperatureNow, 2) + (EnzymeLabModel.Denatured ? " (denatured)" : ""), EnzymeLabModel.Denatured ? K.Tone("red") : null),
            ("pH factor", F(EnzymeLabModel.PhNow, 2), null),
        });

        // the energy diagram
        energy.Clear();
        energy.XMin = 0;
        energy.XMax = 1;
        energy.YMin = -40;
        energy.YMax = 88;
        energy.Lines.Add(new PlotLine(EnzymeLabModel.Uncatalysed.Select(x => (x.X, x.G)).ToList(), quiet, Dashed: true, Width: 1.8));
        energy.Lines.Add(new PlotLine(EnzymeLabModel.Catalysed.Select(x => (x.X, x.G)).ToList(), tint, Width: 2.4));
        energy.Guides.Add(new PlotGuide(EnzymeLabModel.EaUncatalysed, true, "Ea without enzyme", quiet));
        energy.Guides.Add(new PlotGuide(EnzymeLabModel.EaCatalysed, true, "Ea with enzyme", tint));
        energy.Guides.Add(new PlotGuide(0, true, "Reactants", quiet));
        energy.Guides.Add(new PlotGuide(EnzymeLabModel.DeltaG, true, "Products: ΔG unchanged", quiet));
        energy.Redraw();
        Shot.Moved();
    }

    /// <summary>A temperature preset's short name for its curve's label.</summary>
    private static string Short(EnzymeTemperaturePreset p) => p.Id switch { "taq" => "Taq", "fish" => "Fish", _ => "Human" };
}

/// <summary>
/// The active site in 2D (spec §4.9): the enzyme with its pocket and the substrate; the substrate arriving and the enzyme
/// closing slightly round it (induced fit); a competitive inhibitor in the pocket and the substrate kept out; a
/// noncompetitive one at an allosteric site and the pocket misshapen; the enzyme unfolded once denatured. The outline
/// wavers as heat or a pH far from the best strains it. A change of state is drawn in over 600 ms, and "Show binding"
/// plays the substrate's arrival over 1.6 s; with Windows' animations off the picture before binding is shown for a
/// moment instead.
/// </summary>
internal sealed class EnzymeSiteView : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(EnzymeSiteView),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty Text2BrushProperty = DependencyProperty.Register(nameof(Text2Brush), typeof(Brush), typeof(EnzymeSiteView),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CardBrushProperty = DependencyProperty.Register(nameof(CardBrush), typeof(Brush), typeof(EnzymeSiteView),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Typeface Face = new(K.TextFace, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private EnzymeSite shown = EnzymeSite.Bound;
    private double strain;
    private bool first = true;
    private DispatcherTimer? apart;

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public Brush Text2Brush { get => (Brush)GetValue(Text2BrushProperty); set => SetValue(Text2BrushProperty, value); }
    public Brush CardBrush { get => (Brush)GetValue(CardBrushProperty); set => SetValue(CardBrushProperty, value); }

    public EnzymeSiteView()
    {
        SetResourceReference(Text2BrushProperty, B.Text2);
        SetResourceReference(CardBrushProperty, B.Card);
        ClipToBounds = true;
    }

    /// <summary>A state shown (drawn in when it changed), and how much heat or pH strain the enzyme's outline.</summary>
    public void Show(EnzymeSite s, double strain)
    {
        AutomationProperties.SetName(this, "Active site: " + s switch
        {
            EnzymeSite.Bound => "substrate bound, the enzyme closed round it",
            EnzymeSite.Competitive => "a competitive inhibitor in the active site, the substrate kept out",
            EnzymeSite.Noncompetitive => "a noncompetitive inhibitor at another site, the active site misshapen",
            _ => "the enzyme unfolded",
        });
        this.strain = Math.Clamp(strain, 0, 1);
        if (s == shown && !first) { InvalidateVisual(); return; }
        shown = s;
        if (first) { first = false; Progress = 1; return; }
        Motion.FromTo(this, ProgressProperty, 0, 1, 600, Motion.Out);
    }

    /// <summary>The substrate arriving and the enzyme closing round it, played again.</summary>
    public void Play()
    {
        if (shown != EnzymeSite.Bound) return;
        if (Motion.On)
        {
            Motion.FromTo(this, ProgressProperty, 0, 1, 1600, new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut });
            return;
        }
        // (animations off: the picture before binding for a moment, then bound again)
        BeginAnimation(ProgressProperty, null);
        Progress = 0;
        apart?.Stop();
        apart = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        apart.Tick += (_, _) => { apart?.Stop(); Progress = 1; };
        apart.Start();
    }

    private FormattedText Words(string s) => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 11, Text2Brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static double Ease(double u)
    {
        var c = Math.Clamp(u, 0, 1);
        return c * c * (3 - 2 * c);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(size));
        if (size.Width < 40 || size.Height < 40) return;
        // (drawn on a 300 × 190 board, scaled to fit and centred)
        const double W = 300, H = 190;
        var k = Math.Min(size.Width / W, size.Height / H);
        dc.PushTransform(new TranslateTransform((size.Width - W * k) / 2, (size.Height - H * k) / 2));
        dc.PushTransform(new ScaleTransform(k, k));
        var dark = Appearance.Dark;
        var enzyme = BioColors.Tool("enzyme");
        var ink = K.Darkened(enzyme);
        var t = Math.Clamp(Progress, 0, 1);
        // (the substrate's way in, then the enzyme closing round it)
        var approach = Ease(t / 0.6);
        var close = Ease((t - 0.6) / 0.4);
        var e = 1 - Math.Pow(1 - t, 3);
        var body = new SolidColorBrush(Color.FromArgb(dark ? (byte)110 : (byte)80, enzyme.R, enzyme.G, enzyme.B));
        var edge = new Pen(new SolidColorBrush(ink), 2);
        var substrate = new SolidColorBrush(EnzymeLabTool.Substrate.ToColor(dark));
        var red = new SolidColorBrush(EnzymeLabTool.Competitive.ToColor(dark));
        var violet = new SolidColorBrush(EnzymeLabTool.Allosteric.ToColor(dark));
        var lead = new Pen(Text2Brush, 1) { DashStyle = new DashStyle(new[] { 2.0, 2.0 }, 0) };
        var outline = new Pen(CardBrush, 1.5);

        // the pocket's shape: wide at the top, narrowing in (a substrate-shaped wedge), at the enzyme's top centre
        Geometry Wedge(double cx, double top, double width, double depth, double skew = 0)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(cx - width / 2, top), true, true);
                c.LineTo(new Point(cx + width / 2, top), true, true);
                c.LineTo(new Point(cx + width * 0.27 + skew, top + depth), true, true);
                c.LineTo(new Point(cx - width * 0.27 + skew, top + depth), true, true);
            }
            g.Freeze();
            return g;
        }
        Geometry Block(double cx, double top, double width, double depth, bool square = false)
        {
            if (square) return new RectangleGeometry(new Rect(cx - width * 0.3, top, width * 0.6, depth), 3, 3);
            return Wedge(cx, top, width, depth);
        }
        void Label(string s, Point at, Point to)
        {
            var ft = Words(s);
            dc.DrawLine(lead, new Point(at.X + (to.X < at.X ? 0 : ft.Width), at.Y + ft.Height / 2), to);
            dc.DrawText(ft, at);
        }

        const double cx = 150, cy = 120, rx = 112, ry = 58;
        const double top = cy - ry - 8, depth = 40, sw = 50;
        if (shown == EnzymeSite.Denatured)
        {
            // (unfolded: the chain a loose wiggle, the pocket gone)
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(40, 130), false, false);
                var pts = new List<Point>();
                for (var i = 1; i <= 120; i++)
                {
                    var u = i / 120.0;
                    var amp = 0.6 + 0.4 * e;
                    pts.Add(new Point(40 + 220 * u, 130 + amp * 34 * (0.3 * Math.Sin(9 * u + 0.5) + 0.14 * Math.Sin(23 * u + 1.2)) * 2.2));
                }
                c.PolyLineTo(pts, true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(ink), 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, g);
            dc.DrawGeometry(substrate, outline, Block(232, 22, sw, depth * 0.8));
            Label("Unfolded enzyme", new Point(18, 168), new Point(70, 146));
            Label("Substrate: no site to fit", new Point(60, 18), new Point(205, 38));
        }
        else
        {
            // the enzyme: a lumpy oval (wavering as heat or pH strain it) with its pocket cut out — a touch wider before the
            // substrate binds (induced fit)
            var open = shown switch
            {
                EnzymeSite.Bound => 1.28 - 0.28 * close,
                EnzymeSite.Competitive => 1.05,
                _ => 0.82,
            };
            var skew = shown == EnzymeSite.Noncompetitive ? 9 * e : 0;
            var pocket = Wedge(cx, top, sw * open + 6, depth + 2, skew);
            var oval = new StreamGeometry();
            using (var c = oval.Open())
            {
                var pts = Enumerable.Range(0, 240).Select(i =>
                {
                    var a = 2 * Math.PI * i / 240;
                    var r = 1 + 0.02 * Math.Sin(3 * a + 0.4) + 0.015 * Math.Cos(5 * a) + strain * 0.06 * Math.Sin(7 * a);
                    return new Point(cx + rx * r * Math.Cos(a), cy + ry * r * Math.Sin(a));
                }).ToList();
                c.BeginFigure(pts[0], true, true);
                c.PolyLineTo(pts.Skip(1).ToList(), true, true);
            }
            oval.Freeze();
            Geometry shape = new CombinedGeometry(GeometryCombineMode.Exclude, oval, pocket);
            if (shown == EnzymeSite.Noncompetitive)
                shape = new CombinedGeometry(GeometryCombineMode.Exclude, shape, new EllipseGeometry(new Point(cx - 64, cy + ry - 2), 15, 13));
            dc.DrawGeometry(body, edge, shape);
            Label("Enzyme", new Point(232, 150), new Point(cx + rx * 0.62, cy + ry * 0.55));
            Label("Active site", new Point(6, 36), new Point(cx - sw / 2, top + 14));
            switch (shown)
            {
                case EnzymeSite.Bound:
                {
                    // (from up and to the right, into the pocket)
                    var dx = 70 * (1 - approach);
                    var dy = -50 * (1 - approach);
                    dc.PushOpacity(0.45 + 0.55 * approach);
                    dc.DrawGeometry(substrate, outline, Block(cx + dx, top + 2 + dy, sw - 2, depth));
                    dc.Pop();
                    Label("Substrate", new Point(226, 14), new Point(cx + sw / 2 + dx, top + 10 + dy));
                    break;
                }
                case EnzymeSite.Competitive:
                {
                    var dy = -46 * (1 - e);
                    dc.DrawGeometry(red, outline, Block(cx, top + 2 + dy, sw, depth, square: true));
                    dc.DrawGeometry(substrate, outline, Block(250, 14, sw * 0.8, depth * 0.8));
                    Label("Inhibitor", new Point(66, 8), new Point(cx - 8, top + 12 + dy));
                    Label("Substrate, kept out", new Point(180, 62), new Point(240, 40));
                    break;
                }
                default:
                {
                    var dx = -40 * (1 - e);
                    dc.DrawEllipse(violet, outline, new Point(cx - 64 + dx, cy + ry + 4), 12, 12);
                    dc.DrawGeometry(substrate, outline, Block(cx + 6, top - 34, sw - 2, depth));
                    Label("Inhibitor, elsewhere", new Point(150, 172), new Point(cx - 52 + dx, cy + ry + 6));
                    Label("Substrate: no longer fits", new Point(192, 4), new Point(cx + 24, top - 24));
                    break;
                }
            }
        }
        dc.Pop();
        dc.Pop();
    }
}
