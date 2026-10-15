using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace Simpl.Tools;

#pragma warning disable CA2255
internal static class AtomsToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => ToolRegistry.Register("atoms", c => new AtomsTool(c), c => new AtomsQuick(c));
}
#pragma warning restore CA2255

/// <summary>
/// What Atoms &amp; Orbitals shows, kept while its view comes and goes: the element and the view — remembered on this PC
/// for next time (ChemPrefs <c>SimplChem.atoms.element</c>, its number; <c>SimplChem.atoms.view</c>: atom, orbitals or
/// cloud — the Mac's keys) — and, for that element, the subshell picked for its orbitals and the one picked for its cloud.
/// </summary>
internal static class AtomState
{
    /// <summary>The element shown: the one it showed last, else the one last opened in the Periodic Table, else carbon
    /// (as the Mac's).</summary>
    public static PTElement Element
    {
        get => PeriodicTable.Named(ChemPrefs.String("atoms", "element")) ?? PeriodicTable.Named(ChemPrefs.String("ptable", "element"))
            ?? PeriodicTable.ByNumber(6) ?? PeriodicTable.Elements[0];
        set
        {
            var number = value.Number.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(ChemPrefs.String("atoms", "element"), number, StringComparison.Ordinal)) return;
            ChemPrefs.Set("atoms", "element", number);
            // (another element: its own outer subshell, and all of its cloud)
            Subshell = null;
            CloudFocus = null;
        }
    }

    /// <summary>The view shown: the one last shown, else the atom.</summary>
    public static PTModel View
    {
        get => Parse(ChemPrefs.String("atoms", "view")) ?? PTModel.Atom;
        set => ChemPrefs.Set("atoms", "view", value.ToString().ToLowerInvariant());
    }

    /// <summary>The Orbitals view's subshell ("3d"; null: the outer one being filled).</summary>
    public static string? Subshell;

    /// <summary>The Cloud view's subshell ("2p"; null: every electron) — apart from the Orbitals view's.</summary>
    public static string? CloudFocus;

    /// <summary>"orbitals" → the view (as the launch flag and the setting name it), else null.</summary>
    public static PTModel? Parse(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "atom" or "shells" => PTModel.Atom,
        "orbitals" or "orbital" => PTModel.Orbitals,
        "cloud" => PTModel.Cloud,
        _ => null,
    };

    /// <summary>A launch's state (--tool atoms --tool-state Fe:cloud:3d) set, its parts in any order: an element by its
    /// symbol, name or number, a view, and a subshell for the view ("all": the whole cloud).</summary>
    public static void Apply(string? state)
    {
        string? subshell = null;
        foreach (var part in (state ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Parse(part) is { } view) View = view;
            else if (PeriodicTable.Named(part) is { } e) Element = e;
            else subshell = part;
        }
        // (after the element, which clears the subshells picked for another)
        if (subshell == null) return;
        if (View == PTModel.Cloud) CloudFocus = subshell == "all" ? null : subshell;
        else Subshell = subshell;
    }
}

/// <summary>
/// Atoms &amp; Orbitals (0.3.2; an element page's model until then): an element — typed by its symbol, name or number, or
/// the one before or after — as its atom in 3D (the nucleus, and each shell a ring with its electrons going round), a
/// subshell's orbitals, or its electron cloud, to turn and zoom. At the left, the element and the view, and the view's own
/// parts (the atom's colours; the subshells and their orbitals' electrons; the cloud's subshells, colours and words); in
/// the middle, the model; at the right, its electrons shell by shell and orbital by orbital, a few of its numbers, and Open
/// in Periodic Table for the rest of its facts. Laid out as the builders are (ChemLayout): the controls and the facts in
/// sight at every size, and with the model over the rest, the element and the view in the model's own heading. ← and →
/// step through the elements. Light and dark redraw it; Windows' animation effects off, the model holds still.
/// </summary>
internal sealed class AtomsTool : Grid
{
    private readonly ToolContext ctx;
    private readonly Atom3DView atom = new();
    private readonly SelectorBar views;
    private readonly TextBox field;
    private readonly TextBlock pickNote;
    private readonly Wpf.Ui.Controls.Button before, after;
    /// <summary>The field with the element before and after either side, and the line under it.</summary>
    private readonly FrameworkElement picker;
    private readonly Stack controls = K.V(14);
    /// <summary>The controls' top: the element and the view (moved to the model's heading while it is over the rest).</summary>
    private readonly Stack choose = K.V(14);
    /// <summary>The view's own parts, under them.</summary>
    private readonly ContentControl parts = new() { Focusable = false };
    private readonly Stack facts = K.V(14);
    private readonly ContentControl title = new() { Focusable = false };
    /// <summary>The model's heading's second line, while the model is over the rest: the element and the view.</summary>
    private readonly Grid headRow = K.Columns("*, 16, auto");
    private readonly ChemLayout layout;
    private PTElement e;
    /// <summary>The look last drawn in (light or dark, and the accent of the chosen chip).</summary>
    private bool? shownDark;
    private Color? shownAccent;
    private string said = "";

    /// <summary>The model over the rest (a narrow window): the element and the view are in its own heading.</summary>
    private bool InHead => !layout.Wide;

    public AtomsTool(ToolContext c)
    {
        ctx = c;
        // (what the launch asked — --tool atoms --tool-state Fe:cloud:3d — then what it was handed: an element page's See
        //  it in 3D, its element; else the element and the view it last showed)
        AtomState.Apply(ChemTools.LaunchState("atoms"));
        if (PeriodicTable.Named(c.TakeInfo("element")) is { } handed) AtomState.Element = handed;
        e = AtomState.Element;

        views = new SelectorBar(new[] { "Atom", "Orbitals", "Cloud" }, (int)AtomState.View);
        views.Changed += i =>
        {
            AtomState.View = (PTModel)i;
            Refresh();
        };

        // (no magnifier in it: between the element before and after, in the controls' column of a 1024 or 1366 popup, it
        //  left the field room for "Symbol, name or numbe")
        field = new TextBox
        {
            PlaceholderText = "Symbol, name or number", MinHeight = 32,
            ClearButtonEnabled = false, VerticalAlignment = VerticalAlignment.Center,
        };
        field.TextChanged += (_, _) => Note();
        field.PreviewKeyDown += (_, a) =>
        {
            if (a.Key == Key.Enter)
            {
                if (PeriodicTable.Find(field.Text).FirstOrDefault() is { } found)
                {
                    field.Text = "";
                    Show(found);
                }
                a.Handled = true;
            }
            // (Escape clears what is typed first; with nothing typed it closes the popup, as everywhere)
            else if (a.Key == Key.Escape && field.Text.Length > 0)
            {
                field.Text = "";
                a.Handled = true;
            }
        };
        field.ToolTip = "Type a symbol, a name or a number, then press Enter";
        System.Windows.Automation.AutomationProperties.SetName(field, "Find an element by its symbol, name or number");
        before = PTKit.Choice("The element before", false, () => Step(-1), SymbolRegular.ChevronLeft24, squished: true);
        before.ToolTip = "The element before (←)";
        after = PTKit.Choice("The element after", false, () => Step(1), SymbolRegular.ChevronRight24, squished: true);
        after.ToolTip = "The element after (→)";
        pickNote = K.Wrapped("", Ty.Caption, B.Text2);
        // (Narrator reads it as it changes — what Enter would show, nothing matching, the element stepped to)
        System.Windows.Automation.AutomationProperties.SetLiveSetting(pickNote, System.Windows.Automation.AutomationLiveSetting.Polite);
        picker = K.V(6, K.Columns("auto, *, auto", before.Col(0), field.Margin(6, 0, 6, 0).Col(1), after.Col(2)), pickNote);

        var hint = K.Text("Drag to turn · scroll to zoom", Ty.Caption, B.Text2);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Margin = new Thickness(12);
        // (the model given what the card leaves it: on the smallest window the card is held to the page, its heading over
        //  a model still large enough to turn — the hint giving way first)
        atom.MinHeight = 100;
        headRow.Margin = new Thickness(16, 0, 16, 12);
        controls.Children.Add(choose);
        controls.Children.Add(parts);
        var card = K.Card(K.Rows("auto, *, auto", K.V(0, title, headRow).Row(0), atom.Row(1), hint.Row(2)), new Thickness(0));
        card.SizeChanged += (_, _) => hint.Visibility = card.ActualHeight < 330 ? Visibility.Collapsed : Visibility.Visible;
        // (one over the other: the element and the view in the model's own heading, in view with it — and the card given
        //  room for them over a model still large enough to turn)
        layout = new ChemLayout(controls, card, facts) { Floor = 460 };
        layout.WideChanged += Refresh;
        Children.Add(layout);

        PreviewKeyDown += OnKey;
        // (the keyboard kept in the tool, as an element's page keeps it: given to it as it opens when nothing in it has it,
        //  and when the model is pressed — so ← and → step through the elements)
        Focusable = true;
        FocusVisualStyle = null;
        PreviewMouseDown += (_, a) => { if (!IsKeyboardFocusWithin && a.OriginalSource is not TextBoxBase) Focus(); };
        // (light and dark: the cloud's dots and the legends' colours follow — and a change made while the tool was away
        //  is caught up when it comes back)
        Loaded += (_, _) =>
        {
            Appearance.Changed -= OnAppearance;
            Appearance.Changed += OnAppearance;
            if (shownDark != Appearance.Dark || shownAccent != Appearance.Accent) Refresh();
            Dispatcher.BeginInvoke(() => { if (!IsKeyboardFocusWithin) Focus(); }, System.Windows.Threading.DispatcherPriority.Input);
        };
        Unloaded += (_, _) => Appearance.Changed -= OnAppearance;
        Refresh();
    }

    private void OnAppearance() => Refresh();

    /// <summary>Another element: drawn afresh, remembered for next time.</summary>
    private void Show(PTElement next)
    {
        if (next.Number == e.Number) return;
        e = next;
        AtomState.Element = next;
        Refresh();
        Motion.FadeIn(facts, Motion.Normal);
    }

    private void Step(int d)
    {
        if (PeriodicTable.ByNumber(e.Number + d) is { } next) Show(next);
    }

    private void OnKey(object sender, KeyEventArgs a)
    {
        // (in the element's field only while it is empty: with something typed in it, the arrows move its caret)
        if ((a.OriginalSource is TextBoxBase box && !ReferenceEquals(box, field)) || (Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
        if (ReferenceEquals(a.OriginalSource, field) && field.Text.Length > 0) return;
        if (a.Key == Key.Left) { Step(-1); a.Handled = true; }
        else if (a.Key == Key.Right) { Step(1); a.Handled = true; }
    }

    /// <summary>The subshell whose orbitals are shown: the one picked, else the outer one being filled (3d for cobalt, not
    /// 4s).</summary>
    private PTSubshell? Subshell
    {
        get
        {
            var all = PTConfig.Subshells(e);
            var valence = PTConfig.Valence(e);
            foreach (var s in all) if (s.Id == AtomState.Subshell) return s;
            if (valence.Count > 0)
            {
                var top = valence.Max(s => s.L);
                return valence.Last(s => s.L == top);
            }
            return all.Count > 0 ? all[^1] : null;
        }
    }

    private void Refresh()
    {
        shownDark = Appearance.Dark;
        shownAccent = Appearance.Accent;
        var view = AtomState.View;
        var sub = Subshell;
        atom.Show(e, view, sub, AtomState.CloudFocus);
        views.Select((int)view, false);
        before.IsEnabled = e.Number > 1;
        after.IsEnabled = e.Number < PeriodicTable.Elements.Count;
        Note();
        Place();
        Title();
        Parts(view, sub);
        Facts();
        Shot.Moved();
        // (in a picture run, what it shows each time that changes: CI checks the element an element page handed it)
        var now = $"{e.Symbol} ({view.ToString().ToLowerInvariant()}{(view == PTModel.Cloud ? AtomState.CloudFocus is { } f ? " " + f : "" : view == PTModel.Orbitals && sub is { } s ? " " + s.Id : "")})";
        if (Shot.Asked && now != said) Log.Write($"atoms: shows {now}");
        said = now;
    }

    /// <summary>The line under the field: where the element is, or what Enter would show, or that nothing matches.</summary>
    private void Note()
    {
        var q = field.Text.Trim();
        var text = q.Length == 0 ? $"{e.Number} of {PeriodicTable.Elements.Count} · ← and → step through the elements"
            : PeriodicTable.Find(q).FirstOrDefault() is { } found ? $"Enter shows {found.Name} ({found.Number})"
            : $"No element matches “{q}”";
        if (text == pickNote.Text) return;
        pickNote.Text = text;
        // (said aloud: with nothing typed, the element stepped to)
        System.Windows.Automation.AutomationProperties.SetName(pickNote, q.Length == 0 ? $"{e.Name}, {e.Number} of {PeriodicTable.Elements.Count}" : text);
        if (UIElementAutomationPeer.FromElement(pickNote) is { } peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    // ---- the element and the view, and the model's heading ----

    /// <summary>
    /// The element's field and the view at the top of the controls, or — the model over the rest — on the model's heading's
    /// second line, in view with it. Moved only when the layout changes, so the field keeps the keyboard as an element is
    /// typed after another.
    /// </summary>
    private void Place()
    {
        var into = InHead ? (Panel)headRow : choose;
        if (picker.Parent == into) return;
        ChemKit.Detach(picker);
        ChemKit.Detach(views);
        choose.Children.Clear();
        headRow.Children.Clear();
        if (InHead)
        {
            views.VerticalAlignment = VerticalAlignment.Top;
            headRow.Children.Add(picker.Col(0));
            headRow.Children.Add(views.Col(2));
        }
        else
        {
            views.VerticalAlignment = VerticalAlignment.Stretch;
            choose.Children.Add(ChemKit.Heading("Element"));
            choose.Children.Add(picker);
            choose.Children.Add(ChemKit.Heading("Show"));
            choose.Children.Add(views);
        }
        headRow.Visibility = InHead ? Visibility.Visible : Visibility.Collapsed;
        choose.Visibility = InHead ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The element's tile, name, number, kind and mass over the model.</summary>
    private void Title()
    {
        var words = K.V(1, K.Text(e.Name, Ty.Subtitle), K.Text($"{e.Number} · {e.Kind.Label} · {e.MassText} u", Ty.Caption, B.Text2).Tabular());
        words.VerticalAlignment = VerticalAlignment.Center;
        var row = K.Columns("auto, *", PTKit.Tile(e, 40, 18, 8).Col(0), words.Margin(12, 0, 0, 0).Col(1));
        row.Margin = InHead ? new Thickness(16, 16, 16, 10) : new Thickness(16);
        title.Content = row;
    }

    // ---- the view's parts ----

    private void Parts(PTModel view, PTSubshell? sub)
    {
        parts.Content = view switch
        {
            PTModel.Cloud => K.V(14, ChemKit.Heading("Electrons"), CloudParts()),
            PTModel.Orbitals when sub is { } s => K.V(14, ChemKit.Heading("Subshell"), OrbitalParts(s)),
            _ => K.V(14, ChemKit.Heading("Parts"), Legend()),
        };
    }

    // ---- the cloud's parts (cloud-spec.md §4.2) ----

    /// <summary>All and each subshell to choose from (the chosen one coloured, the rest grey), the colours' legend, the
    /// chosen subshell's charge and size, and what the picture shows.</summary>
    private FrameworkElement CloudParts()
    {
        var cloud = ElectronClouds.For(e);
        var dark = Appearance.Dark;
        var chosen = cloud.Subs.FirstOrDefault(s => s.Name == AtomState.CloudFocus);
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
            System.Windows.Controls.Button b = null!;
            b = K.Subtle(pill, () =>
            {
                // (the chosen one again: nothing to do)
                if (on) return;
                var had = b.IsKeyboardFocusWithin;
                pick();
                Refresh();
                if (had) FocusChip(name);
            }, new Thickness(0), 11);
            b.Margin = new Thickness(0, 0, 6, 6);
            b.ToolTip = tip;
            b.Tag = name;
            System.Windows.Automation.AutomationProperties.SetName(b, on ? $"{name}, selected" : name);
            return b;
        }
        chips.Children.Add(Chip("All", null, chosen == null, "Show every electron", "All electrons", () => AtomState.CloudFocus = null));
        foreach (var s in cloud.Subs)
        {
            var one = s.Count == 1;
            var id = s.Name;
            chips.Children.Add(Chip($"{s.Name}{PTConfig.Superscript(s.Count)}", s.L, ReferenceEquals(chosen, s),
                one ? $"Show the {s.Name} electron on its own" : $"Show the {s.Name} electrons on their own",
                one ? $"{s.Name}, 1 electron" : $"{s.Name}, {s.Count.ToString(CultureInfo.InvariantCulture)} electrons",
                () => AtomState.CloudFocus = id));
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
        void Add(FrameworkElement part) => parts.Children.Add(part.Margin(0, 0, 14, 4));
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
        Add(Part("Nucleus", "Nucleus", K.Hex(dark ? CloudRaster.NucleusDark : CloudRaster.NucleusLight)));
        var legend = new ReadAsOne { Child = parts };
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

    // ---- the orbitals' parts ----

    /// <summary>Every subshell to pick (the one shown lit), its orbitals' names with their electrons — in rows as the
    /// column allows (an f subshell's seven names are long) — and what the colours mean.</summary>
    private FrameworkElement OrbitalParts(PTSubshell sub)
    {
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var s in PTConfig.Subshells(e))
        {
            var on = s.Id == sub.Id;
            var t = PTKit.Text($"{s.Name}{PTConfig.Superscript(s.Count)}", 12, on ? FontWeights.Bold : FontWeights.Normal, B.Text, PTKit.Mono);
            var pill = new Border { Child = t, Padding = new Thickness(9, 3, 9, 4), CornerRadius = new CornerRadius(11) };
            if (on) pill.Background = new SolidColorBrush(K.WithAlpha(Appearance.Accent, 0.25)); else pill.Bg(B.Subtle);
            var id = s.Id;
            var tag = $"The {s.Name} orbitals";
            System.Windows.Controls.Button b = null!;
            b = K.Subtle(pill, () =>
            {
                var had = b.IsKeyboardFocusWithin;
                AtomState.Subshell = id;
                Refresh();
                if (had) FocusChip(tag);
            }, new Thickness(0), 11);
            b.Margin = new Thickness(0, 0, 6, 6);
            b.ToolTip = $"The {s.Name} orbitals";
            b.Tag = tag;
            System.Windows.Automation.AutomationProperties.SetName(b, on ? $"The {s.Name} orbitals, shown" : $"The {s.Name} orbitals");
            chips.Children.Add(b);
        }
        var names = AtomScene.OrbitalNames(sub.L);
        var filling = sub.Filling;
        // (one row of them, as wide as each other, for s, p and d; an f subshell's seven long names in rows)
        var row = sub.L == 3 ? new PTFlowGrid(78, 6) : new PTFlowGrid(0, 6, columns: names.Length);
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

    /// <summary>The keyboard given back to a subshell's chip — its row is drawn again as one is chosen, and the chip that
    /// was pressed went with it (the keyboard thrown out to the window).</summary>
    private void FocusChip(string tag) => Dispatcher.BeginInvoke(() =>
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(parts);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is System.Windows.Controls.Button { Tag: string t } chip && t == tag) { chip.Focus(); return; }
            for (var i = VisualTreeHelper.GetChildrenCount(d) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(d, i));
        }
    }, System.Windows.Threading.DispatcherPriority.Input);

    // ---- the atom's parts ----

    /// <summary>The atom's colours: protons, neutrons, electrons — and, for a large nucleus, how many of its nucleons are
    /// drawn.</summary>
    private FrameworkElement Legend()
    {
        FrameworkElement Part(string name, Color c) => K.H(5, new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center }, K.Text(name, Ty.Caption)).Margin(0, 0, 14, 4);
        var parts = new WrapPanel { Orientation = Orientation.Horizontal, Children = { Part("Proton", AtomScene.ProtonColor), Part("Neutron", AtomScene.NeutronColor), Part("Electron", AtomScene.ElectronColor) } };
        var v = K.V(8, parts, K.Wrapped($"Shells {string.Join(" · ", e.Shells)}: each ring holds a shell’s electrons, going round.", Ty.Caption, B.Text2));
        var total = e.Number + e.Neutrons;
        if (total > 72) v.Children.Add(K.Text($"72 of {total} nucleons drawn", Ty.Caption, B.Text2));
        return v;
    }

    // ---- the facts ----

    /// <summary>Its electrons, a few of its numbers, and the way to the rest of its facts: its page in the Periodic Table,
    /// in this popup's place.</summary>
    private void Facts()
    {
        facts.Children.Clear();
        facts.Children.Add(PTElectrons.Card(e));
        facts.Children.Add(ChemKit.Facts(new (string, string, Color?)[]
        {
            ("Protons", $"{e.Number}", null),
            ("Neutrons (common)", $"{e.Neutrons}", null),
            ("Electrons", $"{e.Number}", null),
            ("Valence electrons", e.ValenceElectrons is { } ve ? $"{ve}" : "—", null),
            ("Atomic mass", $"{e.MassText} u", null),
        }));
        var open = ChemKit.Wide("Open in Periodic Table", () =>
            ToolsCenter.HandTo(ctx, "ptable", new Dictionary<string, string> { ["element"] = e.Number.ToString(CultureInfo.InvariantCulture) }), icon: SymbolRegular.SlideGrid24);
        open.ToolTip = $"{e.Name}’s page in the Periodic Table: every fact, where it sits, its neighbours";
        facts.Children.Add(open);
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
}

/// <summary>
/// Atoms &amp; Orbitals in its pin's flyout (the Mac's AtomsOrbitalsCompact): its tile and its line, then an element
/// typed by its symbol, name or number — the line under it saying what Enter opens, or that nothing matches — and Enter
/// (or Open) opens the whole tool on it; with nothing typed, on the element it showed last.
/// </summary>
internal sealed class AtomsQuick : ContentControl
{
    public AtomsQuick(ToolContext c)
    {
        Focusable = false;
        var kind = ToolsCenter.KindOf(c.Key);
        var name = kind?.Name ?? "Atoms & Orbitals";
        var words = K.V(3, K.Text(name, Ty.BodyStrong), K.Wrapped(kind?.Note ?? "", Ty.Body, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var top = K.Columns("auto, *", (kind != null ? K.IconTile(kind.Symbol, kind.Color, 40) : new Border()).Margin(0, 0, 12, 0).Col(0), words.Col(1));
        var field = new TextBox { PlaceholderText = "Symbol, name or number", MinHeight = 32, ClearButtonEnabled = false };
        field.ToolTip = "Type a symbol, a name or a number, then press Enter";
        System.Windows.Automation.AutomationProperties.SetName(field, "An element to open, by its symbol, name or number");
        var note = K.Wrapped("", Ty.Caption, B.Text2);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(note, System.Windows.Automation.AutomationLiveSetting.Polite);
        PTElement? Wanted()
        {
            var q = field.Text.Trim();
            return q.Length == 0 ? AtomState.Element : PeriodicTable.Find(q).FirstOrDefault();
        }
        void Note()
        {
            note.Text = Wanted() is { } e ? $"Enter opens {e.Name} ({e.Number})" : $"No element matches “{field.Text.Trim()}”";
            if (UIElementAutomationPeer.FromElement(note) is { } peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        void Go()
        {
            if (Wanted() is not { } e) return;
            c.OpenFull(new Dictionary<string, string> { ["element"] = e.Number.ToString(CultureInfo.InvariantCulture) }, null);
        }
        field.TextChanged += (_, _) => Note();
        field.PreviewKeyDown += (_, a) =>
        {
            if (a.Key != Key.Enter) return;
            Go();
            a.Handled = true;
        };
        var open = BioUI.OpenButton($"Open {name}", Go, $"Open {name} full size on this element (Enter)");
        // (0.3.5) Open full screen opens the tool on the element typed, the one Enter opens (nothing typed: nothing — the
        //  tool opens on its own, the one it showed last)
        c.Carry = () => field.Text.Trim().Length > 0 && Wanted() is { } e
            ? new Dictionary<string, string> { ["element"] = e.Number.ToString(CultureInfo.InvariantCulture) }
            : null;
        Note();
        Content = K.V(12, top, K.V(6, field, note), open);
    }
}
