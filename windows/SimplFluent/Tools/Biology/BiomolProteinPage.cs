using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Simpl.Kit;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

/// <summary>
/// The Proteins page (spec §4.4 Proteins): build a peptide from its sequence — each residue's secondary structure painted
/// under it (coil, α-helix, β-strand, and the rarer ones in a menu), the chain built exactly as §6.4 says (NeRF backbone,
/// side chains from the templates, clash relief, proline's rule) with its results (primary structure, peptide bonds,
/// waters released, mass, net charge at pH 7, GRAVY, counts by class); a residue added or taken off at the C-terminus
/// plays dehydration synthesis or hydrolysis; backbone H-bonds as dashes, a ribbon over or instead of the atoms, and the
/// β-sheet demo. Its other half is the four levels of structure: the sequence as beads, the helix and sheet side by side,
/// ubiquitin's fold (its hydrophobic core, its surface, and denaturing it), and haemoglobin's four chains and hemes.
/// </summary>
internal sealed class BiomolProteinPage : BiomolPageView
{
    private static readonly BiomolSS[] Chips = { BiomolSS.Coil, BiomolSS.Helix, BiomolSS.Strand };
    private static readonly BiomolSS[] More = { BiomolSS.Helix310, BiomolSS.Parallel, BiomolSS.LeftHelix };

    private readonly SelectorBar viewBar = new(new[] { "Build", "Structure levels" }, 0);
    private readonly FrameworkElement buildPanel, levelsPanel, tertiaryPanel, quaternaryPanel;
    private readonly Wpf.Ui.Controls.TextBox seqBox;
    private readonly TextBlock message = BioUI.Caption("");
    private readonly BiomolStructureEditor editor = new();
    private readonly Dictionary<BiomolSS, Wpf.Ui.Controls.Button> chipButtons = new();
    private readonly Wpf.Ui.Controls.Button moreButton;
    private readonly ContentControl prolineHost = new() { Focusable = false };
    private readonly ToggleSwitch hbonds = new(), sheet = new();
    private readonly SelectorBar ribbon = new(BiomolRibbons.All.Select(r => r.Label()).ToList(), 0);
    private readonly BiomolList levels = new();
    private readonly SelectorBar centroids = new(new[] { "All", "Core", "Surface" }, 0);
    private readonly Wpf.Ui.Controls.Button denature;
    private readonly BiomolViewControls view = new();
    private readonly DispatcherTimer typed = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private BiomolSS paint = BiomolSS.Helix;
    private bool syncing;
    // (the scene shown while a motion plays: its key and how it is made)
    private (string Key, Func<BiomolScene> Build)? during;
    private int motionGen;

    private static string Seq => BiomoleculeBuilderModel.Sequence;
    private static bool Levels => BiomoleculeBuilderModel.ProteinView == "levels";

    protected override string HydrogenKey => Levels ? "levels" : "proteins";

    public BiomolProteinPage(ToolContext c) : base(c, "3D model of a peptide")
    {
        viewBar.Changed += i =>
        {
            if (syncing) return;
            Stage.Stop();
            BiomoleculeBuilderModel.ProteinView = i == 0 ? "build" : "levels";
            Forget();
            BiomolPrefs.Changed();
        };
        viewBar.ToolTip = "Build a peptide, or explore the four levels of protein structure";

        // ---- the builder ----
        seqBox = ToolKit.Box(Seq, "One-letter codes, as MKTAYIAKQ");
        seqBox.FontFamily = BioUI.Mono;
        seqBox.CharacterCasing = CharacterCasing.Upper;
        seqBox.ToolTip = "One-letter codes, N-terminus first, up to 40 (Ctrl+F comes here)";
        System.Windows.Automation.AutomationProperties.SetName(seqBox, "Peptide sequence, one-letter codes, up to 40 residues");
        seqBox.TextChanged += (_, _) =>
        {
            if (syncing) return;
            typed.Stop();
            typed.Start();
        };
        seqBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            typed.Stop();
            ApplyTyped();
            e.Handled = true;
        };
        typed.Tick += (_, _) => { typed.Stop(); ApplyTyped(); };
        message.Visibility = Visibility.Collapsed;
        message.SetResourceReference(TextBlock.ForegroundProperty, B.Critical);

        var palette = new Grid();
        for (var k = 0; k < 10; k++) palette.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        palette.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        palette.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
        palette.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var all = BioAminoAcids.All;
        for (var k = 0; k < all.Count; k++)
        {
            var aa = all[k];
            var colour = BioColors.Of(aa.Group);
            var t = new TextBlock { Text = aa.One.ToString(), FontFamily = BioUI.Mono, FontWeight = FontWeights.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Foreground = K.Brush(K.Darkened(colour)) };
            var face = new Border { Background = K.Brush(K.WithAlpha(colour, Simpl.Support.Appearance.Dark ? 0.22 : 0.14)), CornerRadius = new CornerRadius(4), Padding = new Thickness(0, 4, 0, 4), Child = t };
            var b = K.Subtle(face, () => Append(aa.One), new Thickness(1), 4);
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            b.ToolTip = $"Add {aa.Name} ({aa.Three}, {aa.One}) at the C-terminus";
            System.Windows.Automation.AutomationProperties.SetName(b, $"Add {aa.Name}");
            Grid.SetRow(b, k < 10 ? 0 : 2);
            Grid.SetColumn(b, k % 10);
            palette.Children.Add(b);
        }
        var remove = K.Button("Remove last", RemoveLast, icon: SymbolRegular.Backspace20);
        remove.MinWidth = 0;
        remove.ToolTip = "Take the C-terminal residue off (hydrolysis: water splits the peptide bond)";
        var reset = K.Link("Start again", () =>
        {
            Stage.Stop();
            BiomoleculeBuilderModel.Sequence = BiomolPeptideBuilder.DefaultSequence;
            BiomoleculeBuilderModel.Structure = "CCHHHHHHHHHHHHCEEEE";
            BiomoleculeBuilderModel.SheetDemo = false;
            SetBox(Seq);
            Forget();
            BiomolPrefs.Changed();
        });
        reset.ToolTip = $"Back to the example, {BiomolPeptideBuilder.DefaultSequence}";

        var chipRow = new WrapPanel();
        foreach (var s in Chips)
        {
            var ss = s;
            var b = PTKit.Choice(s.Label(), s == paint, () => { paint = ss; SyncChips(); });
            b.Margin = new Thickness(0, 0, 6, 6);
            b.ToolTip = $"Paint residues as {s.Label().ToLowerInvariant()} (φ {s.Angles().Phi}°, ψ {s.Angles().Psi}°)";
            chipButtons[s] = b;
            chipRow.Children.Add(b);
        }
        moreButton = PTKit.Choice("More", false, OpenMore);
        moreButton.Margin = new Thickness(0, 0, 6, 6);
        moreButton.Icon = new SymbolIcon { Symbol = SymbolRegular.ChevronDown16 };
        moreButton.ToolTip = "3₁₀ helix, parallel β-strand, left-handed α";
        chipRow.Children.Add(moreButton);
        editor.Painted += ss =>
        {
            BiomoleculeBuilderModel.Structure = BiomolSSes.Text(ss);
            BiomoleculeBuilderModel.SheetDemo = false;
            Forget();
            BiomolPrefs.Changed();
        };
        FrameworkElement Quick(string text, char letter)
        {
            var l = K.Link(text, () =>
            {
                BiomoleculeBuilderModel.Structure = new string(letter, Seq.Length);
                BiomoleculeBuilderModel.SheetDemo = false;
                Forget();
                BiomolPrefs.Changed();
            }, Ty.Caption);
            l.ToolTip = $"Every residue as {text.Replace("All ", "")}";
            return l;
        }
        var quick = new WrapPanel { Children = { Quick("All coil", 'C'), Quick("All helix", 'H'), Quick("All strand", 'E') } };
        hbonds.ToolTip = "Dashed lines from each backbone N–H to the C=O it bonds to";
        BiomolKit.OnToggle(hbonds, () => syncing, on => { BiomoleculeBuilderModel.ShowHBonds = on; BiomolPrefs.Changed(); });
        BiomolKit.OnToggle(sheet, () => syncing, on => { Stage.Stop(); BiomoleculeBuilderModel.SheetDemo = on; Forget(); BiomolPrefs.Changed(); });
        ribbon.Changed += i => { if (syncing) return; BiomoleculeBuilderModel.Ribbon = BiomolRibbons.All[i]; BiomolPrefs.Changed(); };
        ribbon.ToolTip = "Atoms, the ribbon (cartoon) over them, or the ribbon alone";
        buildPanel = K.V(12,
            BioUI.Heading("Sequence"), seqBox, message, palette,
            K.Columns("auto, *", remove.Col(0), reset.Margin(8, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(1)),
            BioUI.Heading("Secondary structure"), BioUI.Caption("Choose one, then drag across the residues (or use the arrow keys and Space; C, H and E set one)."),
            chipRow, editor, quick, prolineHost,
            BioUI.Heading("Show"),
            BiomolKit.Toggle("Backbone H-bonds", hbonds, "Dashed: each backbone N–H to the C=O it bonds to"),
            K.V(6, K.Text("Ribbon", Ty.Caption, B.Text2).Strong(), ribbon),
            BiomolKit.Toggle("β-sheet demo", sheet, "Three antiparallel strands with their H-bonds"));

        // ---- the levels ----
        levels.Heading("The four levels");
        var dark = Simpl.Support.Appearance.Dark;
        foreach (var (key, i, caption) in new[] { ("primary", 1, "The sequence: amino acids in order"), ("secondary", 2, "α-helices and β-sheets, held by H-bonds"), ("tertiary", 3, "The whole fold: ubiquitin"), ("quaternary", 4, "Subunits together: haemoglobin") })
            levels.Row(key, $"{BiomoleculeBuilderModel.LevelTitle(key)} structure", caption, BiomolKit.Letter($"{i}", Simpl.Support.Appearance.Accent, 24), $"{caption} (press {i})");
        levels.Picked += k =>
        {
            Stage.Stop();
            BiomoleculeBuilderModel.Level = k;
            Forget();
            BiomolPrefs.Changed();
        };
        centroids.Changed += i =>
        {
            if (syncing) return;
            BiomoleculeBuilderModel.Centroids = (BiomolCentroids)i;
            if (BiomoleculeBuilderModel.Denatured) { Stage.Stop(); BiomoleculeBuilderModel.Denatured = false; }
            BiomolPrefs.Changed();
        };
        centroids.ToolTip = "Which side chains stand out: all, the nonpolar ones (the hydrophobic core) or the polar and charged ones (the surface)";
        denature = ChemKit.Wide("Denature", Denature, icon: SymbolRegular.Fire20);
        tertiaryPanel = K.V(10, BioUI.Heading("Side chains"), centroids, BioUI.Caption("Spheres are the side chains' centres, coloured by class."), denature);
        var sickle = ChemKit.Wide("Sickle-cell mutation", () => BiomolHandOver.ToTool(C, "gene", "mutations", new Dictionary<string, string> { ["mutation"] = "sickle" }), icon: SymbolRegular.ArrowSwap20);
        sickle.ToolTip = "Opens Gene Expression's Mutations page: HBB c.20A>T, β6 Glu → Val";
        quaternaryPanel = K.V(10, BioUI.Heading("Haemoglobin"), BioUI.Caption("Four chains in their colours; each heme a dark red disk with its iron."), sickle);
        levelsPanel = K.V(14, levels, tertiaryPanel, quaternaryPanel);

        Left.Children.Add(viewBar);
        Left.Children.Add(buildPanel);
        Left.Children.Add(levelsPanel);
        Left.Children.Add(view);
        view.Molecule = () =>
        {
            if (Levels) return BiomoleculeBuilderModel.Level == "secondary" ? (SecondaryModel(), "α-helix and β-sheet") : null;
            if (BiomoleculeBuilderModel.SheetDemo) return (SheetModel(), "β-sheet (3 strands of Ala₇)");
            return (BiomoleculeBuilderModel.CurrentPeptide.Model().Model, $"peptide {Seq}");
        };
        view.Open = OpenInBuilder;
        PreviewKeyDown += (_, e) =>
        {
            if (!Levels || Keyboard.Modifiers != ModifierKeys.None || Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
            var k = e.Key is >= Key.D1 and <= Key.D4 ? e.Key - Key.D1 : (e.Key is >= Key.NumPad1 and <= Key.NumPad4 ? e.Key - Key.NumPad1 : -1);
            if (k < 0) return;
            Stage.Stop();
            BiomoleculeBuilderModel.Level = BiomoleculeBuilderModel.Levels[k];
            Forget();
            BiomolPrefs.Changed();
            e.Handled = true;
        };
    }

    public override bool FocusField()
    {
        if (Levels) return false;
        seqBox.Focus();
        seqBox.SelectAll();
        return true;
    }

    /// <summary>What was picked let go (a new molecule).</summary>
    private void Forget()
    {
        Picked = null;
        Stage.View.Selection = null;
    }

    private void SetBox(string text)
    {
        syncing = true;
        seqBox.Text = text;
        seqBox.CaretIndex = text.Length;
        syncing = false;
    }

    /// <summary>The rarer structures' menu, under its chip.</summary>
    private void OpenMore()
    {
        var m = new ContextMenu { PlacementTarget = moreButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var s in More)
        {
            var ss = s;
            var item = new MenuItem { Header = $"{s.Label()} (φ {s.Angles().Phi}°, ψ {s.Angles().Psi}°)", IsChecked = paint == s };
            item.Click += (_, _) => { paint = ss; SyncChips(); };
            m.Items.Add(item);
        }
        m.IsOpen = true;
    }

    private void SyncChips()
    {
        foreach (var (s, b) in chipButtons)
        {
            b.Appearance = s == paint ? ControlAppearance.Primary : ControlAppearance.Secondary;
            b.FontWeight = s == paint ? FontWeights.SemiBold : FontWeights.Normal;
        }
        var more = More.Contains(paint);
        moreButton.Appearance = more ? ControlAppearance.Primary : ControlAppearance.Secondary;
        moreButton.Content = more ? paint.Label() : "More";
        editor.PaintWith = paint;
    }

    // ---- the sequence changed ----

    private void ApplyTyped()
    {
        var (seq, msg) = BiomolInput.Protein(seqBox.Text);
        if (seq.Length == 0) msg = "Type at least one amino acid (one-letter codes).";
        message.Text = msg ?? "";
        message.Visibility = msg == null ? Visibility.Collapsed : Visibility.Visible;
        if (seq.Length > 0) Change(seq);
    }

    private void Append(char one)
    {
        if (Seq.Length >= BiomolPeptideBuilder.MaxResidues)
        {
            message.Text = $"A peptide here is at most {BiomolPeptideBuilder.MaxResidues} residues.";
            message.Visibility = Visibility.Visible;
            return;
        }
        Change(Seq + one);
        SetBox(Seq);
    }

    private void RemoveLast()
    {
        if (Seq.Length <= 1) return;
        Change(Seq[..^1]);
        SetBox(Seq);
    }

    /// <summary>The sequence changed: one residue added at the C-terminus plays dehydration synthesis, one taken off plays
    /// hydrolysis; anything else is built afresh.</summary>
    private void Change(string seq)
    {
        var old = Seq;
        if (seq == old) return;
        var oldPeptide = BiomoleculeBuilderModel.CurrentPeptide;
        BiomoleculeBuilderModel.SetSequence(seq);
        BiomoleculeBuilderModel.SheetDemo = false;
        Forget();
        if (!Levels && Stage.CanPlay && old.Length >= 1 && seq.Length >= 1)
        {
            // (the new peptide laid out for the view first, the old one then laid out the same way, so the rest stays put)
            var p = BiomoleculeBuilderModel.ShownPeptide;
            var look = PeptideLook(p);
            var gen = ++motionGen;
            void Ended()
            {
                if (gen == motionGen) during = null;
                if (IsLoaded) Draw();
            }
            if (seq.Length == old.Length + 1 && seq.StartsWith(old, StringComparison.Ordinal))
            {
                var hidden = new HashSet<int> { p.Count - 1 };
                during = ($"{PeptideKey(p, look)}|adding", () => PeptideScene(p, look, hidden: hidden));
                Stage.Play(BiomolScenes.PeptideAdded(p, look, Chem), PeptideKey(p, look), Ended);
            }
            else if (old.Length == seq.Length + 1 && old.StartsWith(seq, StringComparison.Ordinal))
            {
                during = ($"{PeptideKey(p, look)}|removing", () => PeptideScene(p, look, hideOH: true));
                Stage.Play(BiomolScenes.PeptideRemoved(BiomoleculeBuilderModel.LaidOut(oldPeptide), look, Chem), PeptideKey(p, look), Ended);
            }
        }
        BiomolPrefs.Changed();
    }

    // ---- scenes ----

    private static BiomolLook PeptideLook(BiomolPeptide p) => LookFor("proteins", p.Atoms.Count);

    private static string PeptideKey(BiomolPeptide p, BiomolLook look) =>
        $"pep|{p.Sequence}|{BiomolSSes.Text(p.Asked)}|{look.Key}|{BiomoleculeBuilderModel.ShowHBonds}|{BiomoleculeBuilderModel.Ribbon}|{BiomoleculeBuilderModel.PeptideFrame}";

    /// <summary>The peptide's scene: its atoms (all but <paramref name="hidden"/>, and without its C-terminal OH when
    /// <paramref name="hideOH"/>), H-bonds and ribbon as chosen, its charges marked, framed round the whole peptide.</summary>
    private static BiomolScene PeptideScene(BiomolPeptide p, BiomolLook look, HashSet<int>? hidden = null, bool hideOH = false)
    {
        var ribbon = BiomoleculeBuilderModel.Ribbon;
        var c = BiomolScenes.Peptide(p, look, new BiomolScenes.PeptideOptions(BiomoleculeBuilderModel.ShowHBonds, ribbon, hidden, hideOH), Chem);
        var b = BioV.BoundingSphere(p.Atoms.Select(a => a.Pos).ToList());
        var marks = look.Labels && ribbon != BiomolRibbon.Only ? BiomolScenes.PeptideChargeMarks(p, look, Chem, hidden) : new List<BiomolMark>();
        if (hideOH) marks.RemoveAll(m => m.Text == "−" && p.AtomIndex(p.Count - 1, "OXT") is { } oxt && BioV.Distance(m.Pos, p.Atoms[oxt].Pos) < 3);
        return new BiomolScene { Content = new BioSceneContent { Items = c.Items, Labels = c.Labels, Frame = new BioSphere(b.Centre, b.Radius + 2.5) }, Marks = marks };
    }

    /// <summary>The β-sheet demo's atoms, as its scene has them.</summary>
    private static BioMoleculeModel SheetModel()
    {
        var s = BiomolScenes.SheetUpright(new BiomolLook() with { Ph7 = BiomoleculeBuilderModel.Ph7 });
        var m = new BioMoleculeModel();
        foreach (var strand in s.Strands) m.Append(strand.Model().Model);
        return m;
    }

    /// <summary>The secondary level's atoms (the helix and the sheet), as its scene has them.</summary>
    private static BioMoleculeModel SecondaryModel()
    {
        var (helix, sheet) = BiomolScenes.SecondaryPeptides(BiomoleculeBuilderModel.Ph7);
        var m = helix.Model().Model;
        foreach (var strand in sheet.Strands) m.Append(strand.Model().Model);
        return m;
    }

    protected override void Draw()
    {
        var levelsShown = Levels;
        syncing = true;
        try
        {
            viewBar.Select(levelsShown ? 1 : 0, false);
            buildPanel.Visibility = levelsShown ? Visibility.Collapsed : Visibility.Visible;
            levelsPanel.Visibility = levelsShown ? Visibility.Visible : Visibility.Collapsed;
            BiomolKit.Set(hbonds, BiomoleculeBuilderModel.ShowHBonds);
            BiomolKit.Set(sheet, BiomoleculeBuilderModel.SheetDemo);
            ribbon.Select(Array.IndexOf(BiomolRibbons.All, BiomoleculeBuilderModel.Ribbon), false);
            levels.Select(BiomoleculeBuilderModel.Level);
            centroids.Select((int)BiomoleculeBuilderModel.Centroids, false);
            tertiaryPanel.Visibility = BiomoleculeBuilderModel.Level == "tertiary" ? Visibility.Visible : Visibility.Collapsed;
            quaternaryPanel.Visibility = BiomoleculeBuilderModel.Level == "quaternary" ? Visibility.Visible : Visibility.Collapsed;
            denature.Content = BiomoleculeBuilderModel.Denatured ? "Refold" : "Denature";
            denature.ToolTip = BiomoleculeBuilderModel.Denatured ? "Fold it back up (as it would only if conditions allow)" : "Unfold ubiquitin into a random coil: its shape lost, its sequence kept";
            if (!seqBox.IsKeyboardFocusWithin && BiomolPeptideBuilder.Clean(seqBox.Text) != Seq) SetBox(Seq);
            SyncChips();
        }
        finally { syncing = false; }
        if (levelsShown) DrawLevel(); else DrawBuild();
        DrawFacts();
    }

    private void DrawBuild()
    {
        var p = BiomoleculeBuilderModel.ShownPeptide;
        editor.Show(p.Sequence, BiomoleculeBuilderModel.StructureFor(p.Sequence), p.ProlineBreaks);
        prolineHost.Content = p.ProlineBreaks.Count == 0 ? null
            : ChemKit.Line(SymbolRegular.Warning16, $"Proline breaks α-helices: {string.Join(", ", p.ProlineBreaks.Select(i => $"Pro {i + 1}"))} and the residue before {(p.ProlineBreaks.Count == 1 ? "it are" : "each are")} built as coil. Proline has no N–H, and its ring bumps the C=O four back.", ChemKit.Orange);
        if (BiomoleculeBuilderModel.SheetDemo)
        {
            var sheetLook = LookFor("proteins", 3 * 70);
            view.Sync("proteins", 3 * 70, colourHint: ColourHint());
            Title("β-sheet demo", "Three antiparallel strands of Ala₇, with the H-bonds between them");
            Stage.View.Accessibility = "3D model of a β-pleated sheet: three antiparallel strands of alanine with backbone hydrogen bonds between them";
            Stage.Show($"sheet|{sheetLook.Key}", () =>
            {
                var c = BiomolScenes.SheetScene(BiomolScenes.SheetUpright(sheetLook), sheetLook, Chem);
                return new BiomolScene { Content = c };
            }, "sheet");
            return;
        }
        var look = PeptideLook(p);
        view.Sync("proteins", p.Atoms.Count, colourHint: ColourHint());
        var n = p.Count;
        var segs = BiomolCartoon.Segments(p.Built.Select(s => s.Cartoon()).ToList());
        var shape = segs.Where(s => s.Kind != 'C').Select(s => $"{BiomolCartoon.Name(s.Kind)} {s.First + 1}–{s.Last + 1}").ToList();
        Title($"Peptide · {n} residue{(n == 1 ? "" : "s")}", shape.Count == 0 ? "All coil" : string.Join(" · ", shape));
        Stage.View.Accessibility = $"3D model of a peptide of {n} amino acids, {p.Sequence}; {(shape.Count == 0 ? "all coil" : string.Join(", ", shape))}";
        // (the camera frames the peptide afresh only when its layout is chosen again: a residue added or a structure changed
        //  inside the ball last framed keeps the view where it is)
        var frameKey = $"pep|{BiomoleculeBuilderModel.PeptideFrame}";
        if (during is { } d) Stage.Show(d.Key, d.Build, frameKey);
        else Stage.Show(PeptideKey(p, look), () => PeptideScene(p, look), frameKey);
    }

    private void DrawLevel()
    {
        var level = BiomoleculeBuilderModel.Level;
        var dark = Dark;
        switch (level)
        {
            case "primary":
            {
                view.Sync("levels", 0, noAtoms: true);
                var seq = Seq;
                var look = LookFor("levels", 0);
                Title("Primary structure", $"{seq.Length} amino acids, N-terminus first, joined by peptide bonds");
                Stage.View.Accessibility = $"The primary structure of a peptide as beads: {string.Join(" ", seq.ToCharArray())}";
                Stage.Show($"primary|{seq}|{look.Key}", () => BiomolScenes.Primary(seq, look), $"primary|{seq.Length}");
                break;
            }
            case "secondary":
            {
                const int atoms = 12 * 10 + 3 * 70;
                view.Sync("levels", atoms, colourHint: ColourHint());
                var look = LookFor("levels", atoms);
                Title("Secondary structure", "An α-helix (Ala₁₂) and a β-pleated sheet (3 × Ala₇), held by backbone H-bonds");
                Stage.View.Accessibility = "3D models of an alpha helix and a beta pleated sheet side by side, with their backbone hydrogen bonds dashed";
                Stage.Show($"secondary|{look.Key}", () => new BiomolScene { Content = BiomolScenes.Secondary(look, Chem) }, "secondary");
                break;
            }
            case "quaternary":
            {
                view.Sync("levels", 0, noAtoms: true);
                var labels = BiomoleculeBuilderModel.Labels;
                Title("Quaternary structure", "Haemoglobin (PDB 2HHB): two α and two β chains, four hemes");
                Stage.View.Accessibility = "3D cartoon of haemoglobin: four chains, alpha 1 and 2, beta 1 and 2, each holding a heme with its iron";
                Stage.Show($"quaternary|{dark}|{labels}", () => BiomolProteins.Quaternary(dark, labels), "quaternary");
                break;
            }
            default:
            {
                view.Sync("levels", 0, noAtoms: true);
                var labels = BiomoleculeBuilderModel.Labels;
                var show = BiomoleculeBuilderModel.Centroids;
                var denatured = BiomoleculeBuilderModel.Denatured;
                Title(denatured ? "Tertiary structure, lost" : "Tertiary structure", denatured ? "Ubiquitin denatured: the same chain, unfolded into a random coil" : $"Ubiquitin (PDB 1UBQ), 76 amino acids · side chains: {show.Label().ToLowerInvariant()}");
                Stage.View.Accessibility = denatured ? "3D model of ubiquitin denatured into a random coil" : $"3D cartoon of ubiquitin: an alpha helix packed against a beta sheet, with its side chains as spheres coloured by class; showing {show.Label().ToLowerInvariant()}";
                if (during is { } d) Stage.Show(d.Key, d.Build, "tertiary");
                else if (denatured) Stage.Show($"denatured|{dark}|{labels}", () => BiomolProteins.Denatured(dark, labels), "tertiary");
                else Stage.Show($"tertiary|{show}|{dark}|{labels}", () => BiomolProteins.Tertiary(dark, show, labels), "tertiary");
                break;
            }
        }
    }

    /// <summary>Ubiquitin unfolded (or folded back): its chain morphing between the fold and the random coil over 1.5 s.</summary>
    private void Denature()
    {
        Stage.Stop();
        var unfolding = !BiomoleculeBuilderModel.Denatured;
        BiomoleculeBuilderModel.Denatured = unfolding;
        Forget();
        var dark = Dark;
        var labels = BiomoleculeBuilderModel.Labels;
        if (Stage.CanPlay)
        {
            var (parts, frame) = BiomolProteins.DenatureMeshes(dark);
            var gen = ++motionGen;
            during = ($"unfolding|{unfolding}|{dark}", () => new BiomolScene { Content = new BioSceneContent { Frame = frame } });
            var endKey = unfolding ? $"denatured|{dark}|{labels}" : $"tertiary|{BiomoleculeBuilderModel.Centroids}|{dark}|{labels}";
            Stage.Morph(parts.Select(p => unfolding ? (p.Hex, p.Native, p.Coil) : (p.Hex, p.Coil, p.Native)).ToList(), 1.5, endKey, () =>
            {
                if (gen == motionGen) during = null;
                if (IsLoaded) Draw();
            });
        }
        BiomolPrefs.Changed();
    }

    private static string? ColourHint() => BiomoleculeBuilderModel.ColourBy switch
    {
        BiomolColourBy.Part => "Backbone slate; R groups in their class colours; peptide bonds glowing pink.",
        BiomolColourBy.Class => "Each residue in its class's colour.",
        _ => null,
    };

    // ---- the facts ----

    protected override void DrawFacts()
    {
        Right.Children.Clear();
        if (Levels) LevelFacts(); else BuildFacts();
    }

    /// <summary>The sequence written N→C between its ends ("H₃N⁺– MKTAY … –COO⁻"), each letter in its class's colour, in
    /// groups of ten.</summary>
    private static TextBlock SequenceLine(string seq, bool ph7)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = BioUI.Mono, FontSize = 14, LineHeight = 22 };
        t.SetResourceReference(TextBlock.ForegroundProperty, Simpl.Kit.B.Text);
        Run End(string s)
        {
            var r = new Run(s) { FontFamily = K.TextFace, FontSize = 12, FontWeight = FontWeights.SemiBold };
            r.SetResourceReference(TextElement.ForegroundProperty, Simpl.Kit.B.Text2);
            return r;
        }
        t.Inlines.Add(End(ph7 ? "H₃N⁺– " : "H₂N– "));
        for (var i = 0; i < seq.Length; i++)
        {
            if (i > 0 && i % 10 == 0) t.Inlines.Add(new Run(" "));
            t.Inlines.Add(new Run(seq[i].ToString()) { FontWeight = FontWeights.SemiBold, Foreground = K.Brush(K.Darkened(BioColors.AminoAcid(seq[i]))) });
        }
        t.Inlines.Add(End(ph7 ? " –COO⁻" : " –COOH"));
        System.Windows.Automation.AutomationProperties.SetName(t, $"N-terminus, {string.Join(" ", seq.ToCharArray())}, C-terminus");
        return t;
    }

    private void BuildFacts()
    {
        if (BiomoleculeBuilderModel.SheetDemo)
        {
            var sheet = BiomolSheet.Build("AAAAAAA", BiomoleculeBuilderModel.Ph7);
            int h12 = sheet.HBonds.Count(h => (h.Donor, h.Acceptor) is (0, 1) or (1, 0)), h23 = sheet.HBonds.Count - h12;
            Right.Children.Add(BioUI.Card("β-pleated sheet", BioPalette.Strand.ToColor(), "Three antiparallel strands of Ala₇",
                BioUI.FactRow("Hydrogen bonds", $"{h12} between strands 1 and 2, {h23} between strands 2 and 3: each a backbone N–H to a backbone C=O on the next strand, 2.9 Å."),
                BioUI.FactRow("Antiparallel", "Neighbouring strands run in opposite directions (N→C up, then down)."),
                BioUI.FactRow("R groups", "They point above and below the sheet, alternately, and take no part in its H-bonds."),
                BioUI.FactRow("In real proteins", "Strands are joined through turns and loops of the same chain; here each strand stands alone so the H-bonds show.")));
            Right.Children.Add(Legend(false));
            return;
        }
        var p = BiomoleculeBuilderModel.CurrentPeptide;
        var seq = p.Sequence;
        var r = BiomoleculeBuilderModel.Results(seq);
        Right.Children.Add(BioUI.Card("Primary structure", null, "N-terminus to C-terminus", SequenceLine(seq, BiomoleculeBuilderModel.Ph7),
            BioUI.FactRow("Ends", $"N-terminus {p.ResidueName(0)} · C-terminus {p.ResidueName(p.Count - 1)}")));
        Right.Children.Add(BioUI.Facts(new (string, string, Color?)[]
        {
            ("Residues", $"{r.Residues}", null),
            ("Peptide bonds", $"{r.Bonds}", null),
            ("Water released", $"{r.Waters} H₂O", null),
            ("Molar mass", $"{BiomolKit.N(r.Mass, "#,##0.00")} g/mol", null),
            ("Net charge at pH 7", BiomolText.Signed(r.Charge), r.Charge > 0 ? BioPalette.Basic.ToColor() : (r.Charge < 0 ? BioPalette.Acidic.ToColor() : null)),
            ("GRAVY", $"{BiomolKit.Signed(r.Gravy, "0.00")} ({(r.Gravy > 0 ? "hydrophobic" : "hydrophilic")})", null),
        }));
        Right.Children.Add(BioUI.Legend(BioAAClasses.All.Select(cl => (BioColors.Of(cl), $"{cl.Label()}: {r.Classes[cl]}"))));
        Right.Children.Add(BioUI.Caption("Dehydration synthesis: each peptide bond releases one water, so n residues release n − 1. Mass = the residues' masses + 18.02 g/mol (one water's worth for the two ends). Net charge: Lys, Arg +1; Asp, Glu −1; the N-terminus +1 and C-terminus −1 cancel; His counted as 0 (about 4% is protonated at pH 7.4)."));
        if (BiomoleculeBuilderModel.ShowHBonds)
        {
            var hb = p.HBonds;
            var helical = hb.Count(h => h.Donor - h.Acceptor == 4);
            Right.Children.Add(BioUI.Card("Backbone H-bonds", BioPalette.HBond.ToColor(), $"{hb.Count} in this chain",
                BioUI.FactRow("Where", helical > 0 ? $"{helical} in helices: the C=O of residue i to the N–H of residue i + 4. {(hb.Count > helical ? $"{hb.Count - helical} others." : "")}" : "Each a backbone N–H to a backbone C=O (N···O under 3.3 Å)."),
                BioUI.FactRow("Common mix-up", "Helix H-bonds are between backbone atoms, never side chains: the R groups point outward from the helix.")));
        }
        AddPicked(p);
        Right.Children.Add(Legend(BiomoleculeBuilderModel.Ribbon != BiomolRibbon.Off));
    }

    private void AddPicked(BiomolPeptide p)
    {
        if (PickedAtom(Picked) is { } k && k < p.Atoms.Count)
        {
            var a = p.Atoms[k];
            var part = BiomolScenes.BackboneNames.Contains(a.Name) ? "backbone" : "R group (side chain)";
            Right.Children.Add(PickedCard($"{p.ResidueName(a.Residue)} · {a.Name}", a.Element, part));
        }
    }

    private static FrameworkElement Legend(bool cartoon)
    {
        var dark = Dark;
        var items = new List<(Color, string)>();
        switch (BiomoleculeBuilderModel.ColourBy)
        {
            case BiomolColourBy.Part:
                items.Add((K.Hex(BiomolParts.Backbone.Colour.HexFor(dark)), "Backbone"));
                items.AddRange(BioAAClasses.All.Select(c => (BioColors.Of(c), $"R group: {c.Label()}")));
                items.Add((K.Hex(BiomolParts.BondHighlight.HexFor(dark)), "Peptide bond"));
                break;
            case BiomolColourBy.Class:
                items.AddRange(BioAAClasses.All.Select(c => (BioColors.Of(c), c.Label())));
                break;
            default:
                items.AddRange(new[] { "C", "H", "N", "O", "S" }.Select(s => (MBChem.ColorOf(s), $"{s} · {MBChem.Element(s)?.Name ?? s}")));
                break;
        }
        if (cartoon) items.AddRange(new[] { ('H', "α-helix ribbon"), ('E', "β-strand arrow"), ('C', "Coil") }.Select(x => (BioPalette.Secondary(x.Item1).ToColor(), x.Item2)));
        if (BiomoleculeBuilderModel.ShowHBonds) items.Add((BioPalette.HBond.ToColor(), "H-bond (dashed)"));
        return K.V(8, K.Text($"Colour by {BiomoleculeBuilderModel.ColourBy.Label().ToLowerInvariant()}", Ty.Caption, Simpl.Kit.B.Text2).Strong(), BioUI.Legend(items));
    }

    private void LevelFacts()
    {
        var dark = Dark;
        switch (BiomoleculeBuilderModel.Level)
        {
            case "primary":
            {
                var seq = Seq;
                Right.Children.Add(BioUI.Card("Primary structure", null, "Level 1",
                    BioUI.FactRow("What it is", "The order of the amino acids, N-terminus to C-terminus, joined by peptide bonds (covalent). The gene's DNA sequence sets it."),
                    SequenceLine(seq, true),
                    BioUI.FactRow("This chain", $"{seq.Length} amino acids, {Math.Max(seq.Length - 1, 0)} peptide bonds"),
                    BioUI.FactRow("Why it matters", "Every higher level follows from it. One change can change the whole shape: sickle-cell haemoglobin has Val in place of Glu at β6.")));
                Right.Children.Add(BioUI.Legend(BioAAClasses.All.Select(c => (BioColors.Of(c), c.Label()))));
                Right.Children.Add(BioUI.Caption("The sequence is the one on the Build side; change it there."));
                if (Picked is { } id && id.StartsWith("bead:", StringComparison.Ordinal) && int.TryParse(id[5..], out var i) && i < seq.Length && BioAminoAcids.ByOne.GetValueOrDefault(seq[i]) is { } aa)
                    Right.Children.Add(BioUI.Card($"{aa.Three} {i + 1}", BioColors.Of(aa.Group), $"{aa.Name} · {aa.Group.Label()}", BioUI.Caption(aa.Note)));
                break;
            }
            case "secondary":
            {
                Right.Children.Add(BioUI.Card("Secondary structure", null, "Level 2",
                    BioUI.FactRow("What it is", "Local folds of the backbone, held by hydrogen bonds between backbone atoms."),
                    BioUI.FactRow("α-helix", "A right-handed coil: the C=O of each residue H-bonds to the N–H four residues on (i → i + 4). 3.6 residues a turn, 1.5 Å rise a residue; the R groups point outward."),
                    BioUI.FactRow("β-pleated sheet", "Strands side by side, H-bonded strand to strand (here antiparallel). The R groups point above and below the sheet."),
                    BioUI.FactRow("Common mix-up", "These H-bonds are between backbone atoms, not R groups. R groups matter at the tertiary level.")));
                Right.Children.Add(BioUI.Facts(new (string, string, Color?)[]
                {
                    ("α-helix (Ala₁₂) H-bonds", "8", null),
                    ("β-sheet H-bonds", "8 + 6", null),
                    ("Helix rise", "1.56 Å a residue", null),
                    ("Strand rise", "3.47 Å a residue", null),
                }));
                break;
            }
            case "quaternary":
            {
                Right.Children.Add(BioUI.Card("Haemoglobin", K.Hex(BiomolProteins.ChainHex['A']), "PDB 2HHB (deoxy) · level 4",
                    BioUI.FactRow("Subunits", "Four polypeptide chains: two α (141 amino acids each) and two β (146 each)."),
                    BioUI.FactRow("Hemes", "Four, one in each chain. Each heme's iron (Fe²⁺) binds one O₂, so a haemoglobin carries up to four."),
                    BioUI.FactRow("Held together by", "The same weak bonds as tertiary structure, between the chains: no peptide bond joins them."),
                    BioUI.FactRow("Sickle-cell", "β6 Glu → Val: a charged R group becomes a nonpolar one, so deoxy-haemoglobin sticks together into long fibres that bend red cells."),
                    BioUI.FactRow("Its gene", $"The β chains here are the HBB gene's protein without its first Met: {BioGenes.HbbProtein[1..11]}…"),
                    BiomolKit.Action("Open the sickle-cell mutation", SymbolRegular.ArrowSwap20, () => BiomolHandOver.ToTool(C, "gene", "mutations", new Dictionary<string, string> { ["mutation"] = "sickle" }), "Gene Expression ▸ Mutations")));
                Right.Children.Add(BioUI.Legend(new[] { 'A', 'B', 'C', 'D' }.Select(ch => (K.Hex(BiomolProteins.ChainHex[ch]), $"{BiomolProteins.ChainName[ch]} chain ({(ch is 'A' or 'C' ? "α" : "β")})"))
                    .Append((K.Hex(BiomolProteins.HemeHex), "Heme")).Append((K.Hex(BiomolProteins.IronHex), "Iron (Fe²⁺)")).Append((BioPalette.Acidic.ToColor(), "β6 Glu"))));
                if (Picked is { } id && id.Split(':') is { Length: >= 2 } f)
                {
                    var what = f[0] switch
                    {
                        "chain" when f[1].Length == 1 && BiomolProteins.ChainName.TryGetValue(f[1][0], out var nm) => $"{nm} chain ({BiomolProteins.Chain(f[1][0]).Count} amino acids)",
                        "heme" => $"Heme of chain {f[1]}: Fe²⁺ held by four N",
                        "glu6" => "β6 Glu: the residue sickle-cell changes to Val",
                        _ => null,
                    };
                    if (what != null) Right.Children.Add(BioUI.Card("Picked", null, what));
                }
                break;
            }
            default:
            {
                var u = BioData.Ubiquitin;
                var useq = new string(u.Select(x => x.Aa).ToArray());
                if (BiomoleculeBuilderModel.Denatured)
                    Right.Children.Add(BioUI.Card("Denatured", K.Hex(BioPalette.Acidic.HexFor(dark)), "Heat, pH or chemicals",
                        BioUI.FactRow("What changed", "The R-group interactions and H-bonds that held the fold broke, and the chain unfolded into a random coil."),
                        BioUI.FactRow("What did not", "The primary structure: every peptide bond is still there, the same 76 amino acids in the same order. Only the shape is lost."),
                        BioUI.FactRow("Why it matters", "A protein's function depends on its shape, so a denatured enzyme stops working.")));
                var (np, other, npn, othern) = BiomolProteins.CoreDistances();
                Right.Children.Add(BioUI.Card("Ubiquitin", K.Hex(BioPalette.Helix.HexFor(dark)), "PDB 1UBQ · level 3",
                    BioUI.FactRow("Size", $"{u.Count} amino acids, {BiomolKit.N(BioAminoAcids.PeptideMass(useq) / 1000, "0.0")} kDa"),
                    BioUI.FactRow("Its fold", "An α-helix packed against a β-sheet of five strands, with loops between."),
                    BioUI.FactRow("What holds it", "Interactions between R groups: hydrophobic interactions (nonpolar R groups cluster inside, away from water), hydrogen bonds, ionic bonds between + and − R groups, and disulfide bridges (S–S between Cys)."),
                    BioUI.FactRow("No disulfides", useq.Contains('C') ? "It has Cys." : "Ubiquitin has no cysteine, so no disulfide bridges."),
                    BioUI.FactRow("Hydrophobic core", $"Its {npn} nonpolar side chains sit {BiomolKit.N(np, "0.0")} Å from the centre on average; its {othern} polar and charged ones {BiomolKit.N(other, "0.0")} Å, nearer the water.")));
                Right.Children.Add(BioUI.Legend(BioAAClasses.All.Select(c => (BioColors.Of(c), $"Side chain: {c.Label()}"))
                    .Concat(new[] { ('H', "α-helix"), ('E', "β-strand"), ('C', "Coil (loop)") }.Select(x => (BioPalette.Secondary(x.Item1).ToColor(), x.Item2)))));
                if (Picked is { } id && id.StartsWith("res:", StringComparison.Ordinal) && int.TryParse(id[4..], out var i) && u.FirstOrDefault(x => x.I == i) is { } res && BioAminoAcids.ByOne.GetValueOrDefault(res.Aa) is { } aa)
                    Right.Children.Add(BioUI.Card($"{aa.Three} {i}", BioColors.Of(aa.Group), $"{aa.Name} · {aa.Group.Label()} · {(res.Ss == 'H' ? "in the helix" : res.Ss == 'E' ? "in a strand" : "in a loop")}", BioUI.Caption(aa.Note)));
                break;
            }
        }
    }
}
