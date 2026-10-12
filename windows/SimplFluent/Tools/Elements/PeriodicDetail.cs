using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// An element's own page, grown out of its square in the table (the Mac's PTElementPage): its tile, name and kinds; its
/// atom in 3D (or its orbitals' shapes) to turn; its electrons, shell by shell and orbital by orbital; its facts; how it
/// compares with every element; its successive ionization energies; where it sits in the table; its neighbours; and its
/// story. ← and → step through the elements; Table (or Ctrl+[, or Escape) returns to the table.
/// </summary>
internal sealed class PTElementPage : Grid
{
    private readonly Action close;
    private readonly Action<int> go;
    private readonly ContentControl body = new() { Focusable = false };
    private readonly ScrollViewer scroller;
    private PTElement e;
    private PTModel show = PTModel.Atom;
    private string? subshellId;
    /// <summary>The Cloud view's chosen subshell ("2p"; null: every electron) — apart from the Orbitals view's.</summary>
    private string? cloudFocus;
    private int layout = -1;
    private ModelCard? model;

    public PTElementPage(PTElement element, Action close, Action<int> go)
    {
        e = element;
        this.close = close;
        this.go = go;
        scroller = ToolKit.Page(body, 1480);
        Children.Add(scroller);
        Focusable = true;
        FocusVisualStyle = null;
        scroller.SizeChanged += (_, _) => Relayout();
        PreviewKeyDown += OnKey;
        PreviewMouseDown += (_, a) => { if (!IsKeyboardFocusWithin && a.OriginalSource is not System.Windows.Controls.Primitives.TextBoxBase) Focus(); };
        Loaded += (_, _) =>
        {
            PTKeys.Hook();
            PTKeys.Pages.Add(this);
            Dispatcher.BeginInvoke(() => { if (!IsKeyboardFocusWithin) Focus(); }, DispatcherPriority.Input);
        };
        Unloaded += (_, _) => PTKeys.Pages.Remove(this);
        Build();
    }

    public PTElement Element => e;

    /// <summary>Back to the table.</summary>
    public void Back() => close();

    /// <summary>Another element on the page (its neighbour, the next): drawn afresh, the page where it was.</summary>
    public void Show(PTElement next)
    {
        if (next.Number == e.Number) return;
        e = next;
        subshellId = null;
        cloudFocus = null;
        Build();
        Motion.FadeIn(body, Motion.Normal);
        Shot.Moved();
    }

    /// <summary>The model's Atom, Orbitals or Cloud, and a subshell for it ("3d"; "all" for the whole cloud) — the screenshot
    /// suite's --ptable-orbitals, --ptable-cloud and --ptable-subshell.</summary>
    public void ShowModel(PTModel m, string? subshell = null)
    {
        show = m;
        if (subshell != null)
        {
            if (m == PTModel.Cloud) cloudFocus = subshell == "all" ? null : subshell;
            else subshellId = subshell;
        }
        model?.Sync();
    }

    private void Step(int d)
    {
        var n = e.Number + d;
        if (n >= 1 && n <= PeriodicTable.Elements.Count) go(n);
    }

    private void OnKey(object sender, KeyEventArgs a)
    {
        if (a.OriginalSource is TextBoxBase) return;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (a.Key == Key.Left && !ctrl) { Step(-1); a.Handled = true; }
        else if (a.Key == Key.Right && !ctrl) { Step(1); a.Handled = true; }
        else if (ctrl && a.Key == Key.OemOpenBrackets) { close(); a.Handled = true; }
    }

    /// <summary>By the page's width: wide, the atom and its electrons in a column beside the facts; middle, the atom and its
    /// electrons side by side, as tall as each other, the facts below; narrow, one column.</summary>
    private void Relayout()
    {
        var w = Math.Min(1480, scroller.ActualWidth - scroller.Padding.Left - scroller.Padding.Right);
        var l = w >= 1000 ? 2 : w >= 620 ? 1 : 0;
        if (l == layout) return;
        layout = l;
        Build();
    }

    private void Build()
    {
        if (layout < 0)
        {
            var w = scroller.ActualWidth > 0 ? scroller.ActualWidth - 80 : 1100;
            layout = w >= 1000 ? 2 : w >= 620 ? 1 : 0;
        }
        model = new ModelCard(this);
        var config = ConfigCard();
        FrameworkElement columns;
        if (layout == 2)
        {
            var left = K.V(18, model.Card, config);
            left.VerticalAlignment = VerticalAlignment.Top;
            var right = Right();
            right.VerticalAlignment = VerticalAlignment.Top;
            columns = K.Columns("430, 18, *", left.Col(0), right.Col(2));
        }
        else if (layout == 1)
            columns = K.V(18, K.Columns("*, 18, *", model.Card.Col(0), config.Col(2)), Right());
        else columns = K.V(18, model.Card, config, Right());
        body.Content = K.V(18, Header(), Title(), columns);
    }

    // ---- the top ----

    private FrameworkElement Header()
    {
        var back = K.Button("Table", close, icon: SymbolRegular.ChevronLeft24);
        back.MinWidth = 0;
        back.ToolTip = "Back to the table (Ctrl+[)";
        var count = PeriodicTable.Elements.Count;
        var prev = PTKit.Choice("The element before", false, () => Step(-1), SymbolRegular.ChevronLeft24, squished: true);
        prev.IsEnabled = e.Number > 1;
        prev.ToolTip = "The element before (←)";
        var next = PTKit.Choice("The element after", false, () => Step(1), SymbolRegular.ChevronRight24, squished: true);
        next.IsEnabled = e.Number < count;
        next.ToolTip = "The element after (→)";
        var at = K.Text($"{e.Number} / {count}", Ty.Body, B.Text2).Tabular();
        at.VerticalAlignment = VerticalAlignment.Center;
        at.Margin = new Thickness(6, 0, 6, 0);
        return K.Columns("auto, *, auto", back.Col(0), K.H(6, prev, at, next).Col(2));
    }

    private FrameworkElement Title()
    {
        var num = PTKit.Text(e.Number.ToString(CultureInfo.InvariantCulture), 15, FontWeights.SemiBold, Colors.White).Tabular();
        num.HorizontalAlignment = HorizontalAlignment.Left;
        var sym = PTKit.Fit(PTKit.Text(e.Symbol, 46, FontWeights.Bold, Colors.White));
        sym.VerticalAlignment = VerticalAlignment.Center;
        var mass = PTKit.Text(e.MassText, 12.5, FontWeights.Normal, Colors.White).Tabular();
        mass.HorizontalAlignment = HorizontalAlignment.Center;
        var tile = new Border
        {
            Width = 112, Height = 112, CornerRadius = new CornerRadius(12), Padding = new Thickness(12),
            Background = new SolidColorBrush(K.WithAlpha(e.Kind.Color, 0.85)), Child = K.Rows("auto, *, auto", num.Row(0), sym.Row(1), mass.Row(2)),
        };
        var name = PTKit.Fit(PTKit.Text(e.Name, 40, FontWeights.SemiBold, B.Text, K.DisplayFace), HorizontalAlignment.Left);
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var chip in new[]
                 {
                     PTKit.Chip(e.Kind.Label, e.Kind.Color),
                     PTKit.Chip($"{e.Block}-block", PTColourings.BlockColor(e.Block)),
                     PTKit.Chip($"Group {(e.Group is { } g ? g.ToString(CultureInfo.InvariantCulture) : "—")} · Period {e.Period}", null),
                     PTKit.Chip(e.StateWord, PTColourings.StateColor(e.Phase)),
                 })
        {
            chip.Margin = new Thickness(0, 0, 8, 6);
            chips.Children.Add(chip);
        }
        var words = K.V(10, name, chips);
        words.VerticalAlignment = VerticalAlignment.Center;
        return K.Columns("auto, *", tile.Col(0), words.Margin(22, 0, 0, 0).Col(1));
    }

    private static TextBlock Section(string t) => K.Text(t, Ty.BodyStrong);

    // ---- the model ----

    /// <summary>The subshell whose orbitals are shown: the one picked, else the outer one being filled (3d for cobalt, not 4s).</summary>
    private PTSubshell? Subshell
    {
        get
        {
            var all = PTConfig.Subshells(e);
            var valence = PTConfig.Valence(e);
            foreach (var s in all) if (s.Id == subshellId) return s;
            if (valence.Count > 0)
            {
                var top = valence.Max(s => s.L);
                return valence.Last(s => s.L == top);
            }
            return all.Count > 0 ? all[^1] : null;
        }
    }

    /// <summary>The atom in 3D (or a subshell's orbitals, or the electron cloud) with Atom, Orbitals and Cloud over it; under
    /// it, the parts' colours, or the subshells to pick from and the orbitals' electrons, or the cloud's subshells, colours
    /// and words.</summary>
    private sealed class ModelCard
    {
        public readonly Border Card;
        private readonly PTElementPage page;
        private readonly SelectorBar selector;
        private readonly Atom3DView atom = new();
        private readonly ContentControl lower = new() { Focusable = false };
        /// <summary>The look last drawn in (light or dark, and the accent of the chosen chip).</summary>
        private bool? shownDark;
        private Color? shownAccent;

        public ModelCard(PTElementPage page)
        {
            this.page = page;
            selector = new SelectorBar(new[] { "Atom", "Orbitals", "Cloud" }, (int)page.show);
            selector.Changed += i =>
            {
                page.show = (PTModel)i;
                // (the tour's Orbitals step completes on Orbitals only: the Cloud is no step of it)
                if (page.show == PTModel.Orbitals) PTTour.Did(PTTourEvent.Orbitals);
                Refresh();
            };
            // (the hint wraps beside the three choices when the card is narrow, rather than being cut off)
            var hint = K.Wrapped("Drag to turn · scroll to zoom", Ty.Caption, B.Text2);
            hint.TextAlignment = TextAlignment.Right;
            hint.VerticalAlignment = VerticalAlignment.Center;
            var head = K.Columns("auto, *", selector.Col(0), hint.Margin(12, 0, 0, 0).Col(1));
            var frame = new Border { Height = 330, Child = atom, CornerRadius = new CornerRadius(8) };
            PTKit.RoundClip(frame, 8);
            Card = K.Card(K.V(10, head, frame, lower), new Thickness(16));
            Card.TourTarget("ptModel");
            // (light and dark: the cloud's dots and the legend's colours follow — and a change made while the card was
            // away, the app keeping its screens, is caught up when it comes back)
            Card.Loaded += (_, _) =>
            {
                Appearance.Changed -= OnAppearance;
                Appearance.Changed += OnAppearance;
                if (shownDark != Appearance.Dark || shownAccent != Appearance.Accent) Refresh();
            };
            Card.Unloaded += (_, _) => Appearance.Changed -= OnAppearance;
            Refresh();
        }

        private void OnAppearance() => Refresh();

        /// <summary>The selector set to the page's choice (asked for from outside), and the model with it.</summary>
        public void Sync()
        {
            selector.Select((int)page.show, false);
            Refresh();
        }

        public void Refresh()
        {
            shownDark = Appearance.Dark;
            shownAccent = Appearance.Accent;
            var sub = page.Subshell;
            atom.Show(page.e, page.show, sub, page.cloudFocus);
            lower.Content = page.show switch
            {
                PTModel.Cloud => CloudParts(),
                PTModel.Orbitals when sub is { } s => OrbitalParts(s),
                _ => Legend(),
            };
            Shot.Moved();
        }

        // ---- the cloud's parts (cloud-spec.md §4.2) ----

        /// <summary>Under the cloud: All and each subshell to choose from (the chosen one coloured, the rest grey), the
        /// colours' legend, the chosen subshell's charge and size, and what the picture shows.</summary>
        private FrameworkElement CloudParts()
        {
            var cloud = ElectronClouds.For(page.e);
            var dark = Appearance.Dark;
            var chosen = cloud.Subs.FirstOrDefault(s => s.Name == page.cloudFocus);
            Color TypeColor(int l) => K.Hex((dark ? CloudRaster.DarkHex : CloudRaster.LightHex)[Math.Clamp(l, 0, 3)]);

            // (the chips: All, then every subshell in order, core included, each with its colour's dot)
            var chips = new WrapPanel { Orientation = Orientation.Horizontal };
            FrameworkElement Chip(string label, int? l, bool on, string tip, string name, Action pick)
            {
                var t = PTKit.Text(label, 12, on ? FontWeights.Bold : FontWeights.Normal, B.Text, PTKit.Mono);
                t.VerticalAlignment = VerticalAlignment.Center;
                UIElement content = t;
                if (l is { } type)
                    content = K.H(5, new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(TypeColor(type)), VerticalAlignment = VerticalAlignment.Center }, t);
                var pill = new Border { Child = content, Padding = new Thickness(9, 3, 9, 4), CornerRadius = new CornerRadius(11) };
                if (on) pill.Background = new SolidColorBrush(K.WithAlpha(Appearance.Accent, 0.25)); else pill.Bg(B.Subtle);
                var b = K.Subtle(pill, () =>
                {
                    // (the chosen one again: nothing to do)
                    if (on) return;
                    pick();
                    Refresh();
                }, new Thickness(0), 11);
                b.Margin = new Thickness(0, 0, 6, 6);
                b.ToolTip = tip;
                System.Windows.Automation.AutomationProperties.SetName(b, on ? $"{name}, selected" : name);
                return b;
            }
            chips.Children.Add(Chip("All", null, chosen == null, "Show every electron", "All electrons", () => page.cloudFocus = null));
            foreach (var s in cloud.Subs)
            {
                var one = s.Count == 1;
                var id = s.Name;
                chips.Children.Add(Chip($"{s.Name}{PTConfig.Superscript(s.Count)}", s.L, ReferenceEquals(chosen, s),
                    one ? $"Show the {s.Name} electron on its own" : $"Show the {s.Name} electrons on their own",
                    one ? $"{s.Name}, 1 electron" : $"{s.Name}, {s.Count.ToString(CultureInfo.InvariantCulture)} electrons",
                    () => page.cloudFocus = id));
            }

            // (the legend: each type's electrons, or the chosen subshell's and the rest's; the nucleus on the right — read
            // out as one, in words: "s, 2 electrons; p, 4 electrons; Nucleus")
            var said = new List<string>();
            static string Electrons(int count) => count == 1 ? "1 electron" : $"{count.ToString(CultureInfo.InvariantCulture)} electrons";
            FrameworkElement Part(string text, string words, Color c, bool outlined = false)
            {
                said.Add(words);
                var dot = new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center };
                if (outlined)
                {
                    // (the rest's grey is quiet on the card by design: an edge so its legend dot can be seen)
                    dot.SetResourceReference(Shape.StrokeProperty, B.Text2);
                    dot.StrokeThickness = 1;
                }
                return K.H(5, dot, K.Text(text, Ty.Caption));
            }
            var parts = new WrapPanel { Orientation = Orientation.Horizontal };
            void Add(FrameworkElement part) => parts.Children.Add(part.Margin(0, 0, 14, 0));
            if (chosen == null)
            {
                for (var t = 0; t < 4; t++)
                {
                    var kind = t;
                    var count = cloud.Subs.Where(s => s.L == kind).Sum(s => s.Count);
                    if (count > 0) Add(Part($"{"spdf"[t]} · {count.ToString(CultureInfo.InvariantCulture)} e⁻", $"{"spdf"[t]}, {Electrons(count)}", TypeColor(t)));
                }
            }
            else
            {
                Add(Part($"{chosen.Name} · {chosen.Count.ToString(CultureInfo.InvariantCulture)} e⁻", $"{chosen.Name}, {Electrons(chosen.Count)}", TypeColor(chosen.L)));
                var rest = cloud.Subs.Where(s => !ReferenceEquals(s, chosen)).Sum(s => s.Count);
                if (rest > 0) Add(Part($"Rest · {rest.ToString(CultureInfo.InvariantCulture)} e⁻", $"Rest, {Electrons(rest)}", K.Hex(dark ? CloudRaster.RestDark : CloudRaster.RestLight), outlined: true));
            }
            var nucleus = Part("Nucleus", "Nucleus", K.Hex(dark ? CloudRaster.NucleusDark : CloudRaster.NucleusLight));
            nucleus.VerticalAlignment = VerticalAlignment.Top;
            var legend = new ReadAsOne { Child = K.Columns("*, auto", parts.Col(0), nucleus.Col(1)) };
            System.Windows.Automation.AutomationProperties.SetName(legend, string.Join("; ", said));

            var detail = chosen == null ? null : K.Wrapped(CloudDetail(chosen), Ty.Caption, B.Text).Tabular();
            var caption = K.Wrapped(CloudCaption(cloud, chosen), Ty.Caption, B.Text2);
            return K.V(10, chips, legend, detail, caption);
        }

        /// <summary>"2p² · effective nuclear charge 3.25 · on average 81 pm from the nucleus" (the Mac's ElectronCloud.detail).</summary>
        private static string CloudDetail(ElectronCloud.Sub s)
        {
            var v = s.MeanRadius * ElectronCloud.BohrPm;
            var pm = v < 9.95 ? v.ToString("0.0", CultureInfo.InvariantCulture) : ((int)Math.Round(v, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            return $"{s.Name}{PTConfig.Superscript(s.Count)} · effective nuclear charge {s.Zeff.ToString("0.00", CultureInfo.InvariantCulture)} · on average {pm} pm from the nucleus";
        }

        /// <summary>What the cloud shows: every electron, or only the chosen subshell's, and how much larger it is drawn than
        /// in All (the Mac's ElectronCloud.caption, word for word).</summary>
        private static string CloudCaption(ElectronCloud.Cloud c, ElectronCloud.Sub? s)
        {
            if (s == null)
                return "Each dot is a place an electron could be found; the dots are densest where one is most likely to be. Electrons follow no paths. The centre dot is the nucleus, not to scale.";
            var t = s.Count == 1 ? $"Only the {s.Name} electron is coloured" : $"Only the {s.Name} electrons are coloured";
            t += c.Subs.Count > 1 ? "; the rest of the atom is grey." : ".";
            t += " The dots are densest where an electron is most likely to be.";
            var k = c.FrameAll / s.Radius(0.95);
            if (k >= 1.5)
            {
                var times = k < 10 ? k.ToString("0.0", CultureInfo.InvariantCulture) : ((int)Math.Round(k, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
                t += $" Drawn {times}× larger than in All.";
            }
            return t;
        }

        private FrameworkElement OrbitalParts(PTSubshell sub)
        {
            var chips = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var s in PTConfig.Subshells(page.e))
            {
                var on = s.Id == sub.Id;
                var t = PTKit.Text($"{s.Name}{PTConfig.Superscript(s.Count)}", 12, on ? FontWeights.Bold : FontWeights.Normal, B.Text, PTKit.Mono);
                var pill = new Border { Child = t, Padding = new Thickness(9, 3, 9, 4), CornerRadius = new CornerRadius(11) };
                if (on) pill.Background = new SolidColorBrush(K.WithAlpha(Appearance.Accent, 0.25)); else pill.Bg(B.Subtle);
                var id = s.Id;
                var b = K.Subtle(pill, () => { page.subshellId = id; Refresh(); }, new Thickness(0), 11);
                b.Margin = new Thickness(0, 0, 6, 6);
                b.ToolTip = $"The {s.Name} orbitals";
                chips.Children.Add(b);
            }
            var names = AtomScene.OrbitalNames(sub.L);
            var filling = sub.Filling;
            var row = new UniformGrid { Columns = names.Length, Rows = 1 };
            for (var i = 0; i < names.Length; i++)
            {
                var filled = i < filling.Length ? filling[i] : 0;
                var n = K.Text(names[i], Ty.Caption);
                n.FontWeight = FontWeights.SemiBold;
                n.HorizontalAlignment = HorizontalAlignment.Center;
                var f = K.Text(filled == 0 ? "empty" : filled == 1 ? "1 e⁻" : "2 e⁻", Ty.Caption, B.Text2);
                f.HorizontalAlignment = HorizontalAlignment.Center;
                row.Children.Add(K.V(2, n, f));
            }
            var note = K.Wrapped(sub.L == 3 ? "The f shapes are drawn simply. Blue and orange are the wave’s two signs." : "Each orbital holds two electrons; blue and orange are the wave’s two signs. A faint orbital is empty.", Ty.Caption, B.Text2);
            return K.V(10, chips, row, note);
        }

        private FrameworkElement Legend()
        {
            FrameworkElement Part(string name, Color c) => K.H(5, new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center }, K.Text(name, Ty.Caption));
            var g = K.Columns("auto, auto, auto, *, auto", Part("Proton", AtomScene.ProtonColor).Col(0), Part("Neutron", AtomScene.NeutronColor).Margin(14, 0, 0, 0).Col(1), Part("Electron", AtomScene.ElectronColor).Margin(14, 0, 0, 0).Col(2));
            var total = page.e.Number + page.e.Neutrons;
            if (total > 72) g.Children.Add(K.Text($"72 of {total} nucleons drawn", Ty.Caption, B.Text2).Col(4));
            return g;
        }
    }

    /// <summary>Parts read out as one piece of text, by its automation name (the Mac's .accessibilityElement(children:
    /// .combine)): a screen reader hears the whole legend, not each dot's words on their own.</summary>
    private sealed class ReadAsOne : Border
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

        private sealed class Peer : FrameworkElementAutomationPeer
        {
            public Peer(FrameworkElement owner) : base(owner) { }
            protected override List<AutomationPeer>? GetChildrenCore() => null;
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
            protected override bool IsControlElementCore() => true;
            protected override bool IsContentElementCore() => true;
        }
    }

    // ---- electrons ----

    private FrameworkElement ConfigCard()
    {
        var shells = K.H(6, K.Text("Shells", Ty.Body, B.Text2), K.Text(string.Join(" · ", e.Shells), Ty.BodyStrong).Tabular());
        // (the outer subshells' orbitals as boxes, each electron an arrow: one up in every box before any pairs)
        var boxes = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var s in PTConfig.Valence(e))
        {
            var row = K.H(2);
            foreach (var f in s.Filling)
            {
                var arrow = K.Text(f == 2 ? "↑↓" : f == 1 ? "↑" : " ", Ty.Body, B.AccentText);
                arrow.FontSize = 13;
                arrow.FontWeight = FontWeights.Bold;
                arrow.HorizontalAlignment = HorizontalAlignment.Center;
                arrow.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(4), Child = arrow }.Stroke(B.StrongStroke));
            }
            var name = PTKit.Text(s.Name, 12, FontWeights.Normal, B.Text2, PTKit.Mono);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            var group = K.V(4, row, name);
            group.Margin = new Thickness(0, 0, 16, 6);
            boxes.Children.Add(group);
        }
        var v = K.V(12,
            Section("Electron configuration"),
            PTKit.Selectable(PTConfig.Pretty(e), 22, FontWeights.SemiBold, B.Text, PTKit.Mono),
            PTKit.Selectable(PTConfig.Full(e), 14, FontWeights.Normal, B.Text2, PTKit.Mono),
            shells,
            boxes,
            K.Wrapped("Arrows are electrons: each orbital takes one before any takes a second (Hund’s rule), the second with the opposite spin.", Ty.Caption, B.Text2));
        return K.Card(v, new Thickness(16));
    }

    // ---- the right ----

    private FrameworkElement Right()
    {
        var v = K.V(18, Stats(), Compare());
        if ((e.Extra?.Ionization.Count ?? 0) > 1) v.Children.Add(Ionizations());
        var where = WhereItSits();
        where.Width = 280;
        where.VerticalAlignment = VerticalAlignment.Top;
        var near = Neighbours();
        near.VerticalAlignment = VerticalAlignment.Top;
        v.Children.Add(K.Columns("auto, 18, *", where.Col(0), near.Col(2)));
        v.Children.Add(About());
        return v;
    }

    private FrameworkElement Stats()
    {
        string Opt(double? v, Func<double, string> f) => v is { } x ? f(x) : "—";
        var x = e.Extra;
        var grid = new PTFlowGrid(150, 10);
        foreach (var (k, v, sub) in new (string, string, string?)[]
                 {
                     ("Atomic mass", $"{e.MassText} u", null),
                     ("Electronegativity", Opt(e.En, PTElement.Trim), null),
                     ("1st ionization", Opt(e.Ionization, i => $"{PTColourings.Short(i)} kJ/mol"), null),
                     ("Atomic radius", Opt(e.Radius, r => $"{(int)r} pm"), null),
                     ("Electron affinity", Opt(e.Affinity, a => $"{PTColourings.Short(a)} kJ/mol"), null),
                     ("Melting point", Opt(e.Melt, m => $"{PTColourings.Short(m)} K"), PTElement.Celsius(e.Melt)),
                     ("Boiling point", Opt(e.Boil, b => $"{PTColourings.Short(b)} K"), PTElement.Celsius(e.Boil)),
                     ("Density", e.DensityText, null),
                     ("Protons", $"{e.Number}", null),
                     ("Neutrons (common)", $"{e.Neutrons}", null),
                     ("Electrons", $"{e.Number}", null),
                     ("Valence electrons", e.ValenceElectrons is { } ve ? $"{ve}" : "—", null),
                     ("Covalent radius", Opt(x?.Covalent, r => $"{(int)r} pm"), null),
                     ("Van der Waals radius", Opt(x?.VanDerWaals, r => $"{(int)r} pm"), null),
                     ("Molar heat", Opt(x?.MolarHeat, h => $"{PTColourings.Short(h)} J/(mol·K)"), null),
                     ("Block · group · period", $"{e.Block} · {(e.Group is { } g ? g.ToString(CultureInfo.InvariantCulture) : "—")} · {e.Period}", null),
                 })
            grid.Children.Add(Stat(k, v, sub));
        return grid;
    }

    private static FrameworkElement Stat(string k, string v, string? sub)
    {
        // (a line kept for the note under the value whether there is one or not, so every box is the same size)
        var note = sub is null or "—" ? null : sub;
        var n = K.Text(note ?? " ", Ty.Caption, B.Text2);
        n.Opacity = note == null ? 0 : 1;
        var value = PTKit.Fit(PTKit.Text(v, 19, FontWeights.SemiBold).Tabular(), HorizontalAlignment.Left);
        return K.Card(K.V(4, K.Text(k, Ty.Caption, B.Text2), value, n), new Thickness(12));
    }

    private FrameworkElement Compare()
    {
        var v = K.V(10, Section("Compared with every element"));
        foreach (var c in new[] { PTColouring.Electronegativity, PTColouring.Radius, PTColouring.Ionization, PTColouring.Affinity, PTColouring.Melting, PTColouring.Boiling, PTColouring.Density })
        {
            var track = new Border { Height = 7, CornerRadius = new CornerRadius(3.5), VerticalAlignment = VerticalAlignment.Center }.Bg(B.Subtle);
            var bar = new Grid { Height = 7, VerticalAlignment = VerticalAlignment.Center, Children = { track } };
            if (c.Fraction(e) is { } f)
            {
                var fill = new Border { Height = 7, CornerRadius = new CornerRadius(3.5), Background = new SolidColorBrush(PTColourings.Ramp(f)), HorizontalAlignment = HorizontalAlignment.Left };
                bar.Children.Add(fill);
                bar.SizeChanged += (_, a) => fill.Width = Math.Max(6, a.NewSize.Width * f);
            }
            var label = K.Text(c.Label(), Ty.Body);
            label.VerticalAlignment = VerticalAlignment.Center;
            var text = K.Text(CompareText(c), Ty.Caption, B.Text2).Tabular();
            text.TextAlignment = TextAlignment.Right;
            text.HorizontalAlignment = HorizontalAlignment.Right;
            text.VerticalAlignment = VerticalAlignment.Center;
            v.Children.Add(K.Columns("150, *, 200", label.Col(0), bar.Margin(14, 0, 14, 0).Col(1), text.Col(2)));
        }
        return K.Card(v, new Thickness(16));
    }

    private string CompareText(PTColouring c)
    {
        if (c.Value(e) == null) return "not known";
        var v = $"{c.Text(e)}{(c.Unit().Length == 0 || c == PTColouring.Electronegativity ? "" : " " + c.Unit())}";
        return PTRank.Percentile(c, e) is { } p ? $"{v} · above {(int)Math.Round(p * 100, MidpointRounding.AwayFromZero)}%" : v;
    }

    /// <summary>Successive ionization energies, on a log scale: the jump after the outer electrons shows where the core starts.</summary>
    private FrameworkElement Ionizations()
    {
        var ies = e.Extra?.Ionization ?? Array.Empty<double>();
        var top = Math.Log10(ies.DefaultIfEmpty(10).Max());
        var bottom = Math.Log10(Math.Max(ies.DefaultIfEmpty(1).Min() * 0.5, 1));
        int? jump = null;
        for (var i = 1; i < ies.Count; i++) if (ies[i] / ies[i - 1] > 2.8) { jump = i; break; }
        var bars = new UniformGrid { Rows = 1, Columns = ies.Count, Height = 150 };
        for (var i = 0; i < ies.Count; i++)
        {
            var value = PTKit.Fit(K.Text(PTColourings.Short(ies[i]), Ty.Caption, B.Text2).Tabular());
            var h = Math.Max(6, (Math.Log10(ies[i]) - bottom) / Math.Max(top - bottom, 0.01) * 110);
            var bar = new Border { Height = h, CornerRadius = new CornerRadius(5) };
            if (jump is { } j && i >= j) bar.Background = new SolidColorBrush(K.Hex("#ff9f0a")); else bar.Bg(B.Accent);
            var ord = K.Text(PTKit.Ordinal(i + 1), Ty.Caption, B.Text2);
            ord.HorizontalAlignment = HorizontalAlignment.Center;
            var col = K.V(4, value, bar, ord);
            col.VerticalAlignment = VerticalAlignment.Bottom;
            col.Margin = new Thickness(5, 0, 5, 0);
            bars.Children.Add(col);
        }
        var note = jump is { } jj
            ? $"kJ/mol. The big jump after the {PTKit.Ordinal(jj)} electron: the first {jj} are its outer electrons; the rest are held in the core."
            : "kJ/mol, on a log scale.";
        return K.Card(K.V(10, Section("Successive ionization energies"), bars, K.Wrapped(note, Ty.Caption, B.Text2)), new Thickness(16));
    }

    /// <summary>The table in miniature, the element lit, its group and period washed.</summary>
    private Border WhereItSits()
    {
        var words = e.Y <= 7 ? $"Group {(e.Group is { } g ? g.ToString(CultureInfo.InvariantCulture) : "—")}, period {e.Period}" : $"{e.Kind.Label}s, under the table (period {e.Period})";
        return K.Card(K.V(10, Section("Where it sits"), new PTMiniTable(e), K.Wrapped(words, Ty.Caption, B.Text2)), new Thickness(16));
    }

    private Border Neighbours()
    {
        var above = PeriodicTable.Step(e, 0, -1);
        var below = e.Y < 7 ? PeriodicTable.Step(e, 0, 1) : null;
        var before = PeriodicTable.ByNumber(e.Number - 1);
        var after = PeriodicTable.ByNumber(e.Number + 1);
        var grid = new PTFlowGrid(0, 10, columns: 2);
        if (above is { Y: <= 7 } && e.Y <= 7) grid.Children.Add(Neighbour("Above", above));
        if (below is { Y: <= 7 }) grid.Children.Add(Neighbour("Below", below));
        if (before != null) grid.Children.Add(Neighbour("Before", before));
        if (after != null) grid.Children.Add(Neighbour("After", after));
        return K.Card(K.V(10, Section("Neighbours"), grid), new Thickness(16));
    }

    private FrameworkElement Neighbour(string word, PTElement x)
    {
        var words = K.V(1, K.Text(word, Ty.Caption, B.Text2), K.Text(x.Name, Ty.BodyStrong));
        words.VerticalAlignment = VerticalAlignment.Center;
        var b = K.Subtle(K.Columns("auto, *", PTKit.Tile(x, 34, 15, 8).Col(0), words.Margin(10, 0, 0, 0).Col(1)), () => go(x.Number), new Thickness(8), 8);
        b.SetResourceReference(Control.BackgroundProperty, B.Control2);
        b.ToolTip = $"Open {x.Name}";
        System.Windows.Automation.AutomationProperties.SetName(b, $"{word}: {x.Name}");
        return b;
    }

    private FrameworkElement About()
    {
        FrameworkElement Note(SymbolRegular s, string text)
        {
            var t = K.Wrapped(text, Ty.Body, B.Text2);
            var icon = K.Icon(s, 14, B.Text2);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 3, 6, 0);
            return K.Columns("auto, *", icon.Col(0), t.Col(1));
        }
        var v = K.V(10, Section($"About {e.Name}"), PTKit.Selectable(e.Summary, 14, FontWeights.Normal));
        if (e.Extra is { Appearance.Length: > 0 } x) v.Children.Add(Note(SymbolRegular.Eye24, $"Looks: {x.Appearance}"));
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        if (e.Found is { Length: > 0 } found) row.Children.Add(Note(SymbolRegular.Sparkle24, $"Found by {found}").Margin(0, 0, 16, 4));
        if (e.Extra is { NamedBy.Length: > 0 } nx) row.Children.Add(Note(SymbolRegular.BookLetter24, $"Named by {nx.NamedBy}").Margin(0, 0, 16, 4));
        var wiki = new Uri($"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(e.Name)}");
        var link = K.Subtle(K.H(6, K.Icon(SymbolRegular.Open24, 14, B.AccentText), K.Text("Wikipedia", Ty.Body, B.AccentText)), () => Engine.Engine.OpenExternally(wiki), new Thickness(4, 1, 4, 2));
        link.Cursor = Cursors.Hand;
        link.ToolTip = wiki.ToString();
        row.Children.Add(link);
        v.Children.Add(row);
        return K.Card(v, new Thickness(16));
    }
}

/// <summary>The table in miniature (the element page's "Where it sits"): the element in yellow, its group and its period in
/// the accent, the rest faint.</summary>
internal sealed class PTMiniTable : FrameworkElement
{
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(PTMiniTable),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush), typeof(PTMiniTable),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly PTElement e;

    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }

    public PTMiniTable(PTElement e)
    {
        this.e = e;
        Height = 120;
        SetResourceReference(AccentProperty, B.Accent);
        SetResourceReference(InkProperty, B.Text);
        System.Windows.Automation.AutomationProperties.SetName(this, $"{e.Name} in the periodic table");
    }

    protected override void OnRender(DrawingContext dc)
    {
        const double gap = 2;
        var c = Math.Min((ActualWidth - 17 * gap) / 18, (ActualHeight - 9 * gap) / 10);
        if (c <= 0) return;
        var yellow = new SolidColorBrush(K.Hex(Appearance.Dark ? "#ffd60a" : "#ffcc00"));
        foreach (var x in PeriodicTable.Elements)
        {
            var row = x.Y <= 7 ? x.Y - 1 : x.Y - 1 + 0.4;
            var r = new Rect((x.X - 1) * (c + gap), row * (c + gap), c, c);
            var same = x.X == e.X || (x.Y == e.Y && e.Y <= 7);
            if (x.Number == e.Number) { dc.DrawRoundedRectangle(yellow, null, r, 1.5, 1.5); continue; }
            dc.PushOpacity(same ? 0.7 : 0.14);
            dc.DrawRoundedRectangle(same ? Accent : Ink, null, r, 1.5, 1.5);
            dc.Pop();
        }
    }
}
