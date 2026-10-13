using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Simpl.Tools;

// Biomolecule Builder (spec §4.4), the Mac's BiomoleculeTool: amino acids and proteins, nucleic acids, sugars and lipids,
// built and seen in 3D with the chemistry molecule builder's look (CPK atoms, bonds by order, its lights and radii,
// through BioMoleculeScene). Five pages, chosen in the tool's header — Amino acids, Proteins, Nucleic acids,
// Carbohydrates, Lipids — each the chemistry builders' three columns: its choices and the shared View controls at the
// left (style, Show H, pH 7 form, Colour by, labels, Open in Molecule Builder), the 3D view in the middle, and its card
// and facts at the right. What it keeps is in BiomoleculeBuilderModel (remembered in Prefs SimplBio.biomol.*); what a
// launch asks of it (--tool biomol --tool-state proteins:levels:level=quaternary) is read once, below.

#pragma warning disable CA2255
internal static class BiomoleculeToolRegistration
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() =>
        BioTools.Register("biomol", (c, state) => new BiomoleculeTool(c, state), BiomoleculeTool.Pages);
}
#pragma warning restore CA2255

/// <summary>Biomolecule Builder: its page (the header's choice) shown, and Ctrl+F to the page's sequence field.</summary>
internal sealed class BiomoleculeTool : Grid
{
    /// <summary>Its pages, picked in its header (the Mac's ids, so a launch's state names the same page on both).</summary>
    public static readonly BioPage[] Pages =
    {
        new("amino", "Amino acids", SymbolRegular.TextCaseTitle24, "The 20 amino acids"),
        new("proteins", "Proteins", SymbolRegular.Diagram24, "Build a peptide; the four levels of structure"),
        new("nucleic", "Nucleic acids", SymbolRegular.Link24, "Nucleotides joined into a strand"),
        new("carbs", "Carbohydrates", SymbolRegular.FoodGrains24, "Sugars and the glycosidic bond"),
        new("lipids", "Lipids", SymbolRegular.Drop24, "Fatty acids, fats, phospholipids and cholesterol"),
    };

    private readonly ToolContext c;
    private readonly ContentControl host = new() { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private string? shown;
    private BiomolPageView? current;

    public BiomoleculeTool(ToolContext c, BioLaunchState? state)
    {
        this.c = c;
        BiomolPrefs.Load();
        if (state != null) BiomolPrefs.Apply(state);
        // (what another tool handed over, as on the Mac: a sequence — a short DNA strand, else a protein's one-letter
        // codes — or a molecule's code)
        if ((c.TakeInfo("sequence") ?? c.TakeInfo("protein")) is { Length: > 0 } handed) BiomolPrefs.HandSequence(handed);
        if (c.TakeInfo("molecule") is { Length: > 0 } molecule) BiomolPrefs.Hand(molecule);
        Children.Add(host);
        Show();
        void Nav(string k) { if (k == "biomol") Show(); }
        // (light or dark: the page made again, so its colours — the class colours of its tiles and letters too — follow)
        var dark = Appearance.Dark;
        void Look()
        {
            if (Appearance.Dark == dark) return;
            dark = Appearance.Dark;
            Dispatcher.BeginInvoke(() => { shown = null; Show(); });
        }
        Loaded += (_, _) =>
        {
            BioNav.Changed -= Nav;
            BioNav.Changed += Nav;
            Appearance.Changed -= Look;
            Appearance.Changed += Look;
            Show();
        };
        Unloaded += (_, _) =>
        {
            BioNav.Changed -= Nav;
            Appearance.Changed -= Look;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && current?.FocusField() == true) e.Handled = true;
        };
    }

    private void Show()
    {
        var page = BioNav.Page("biomol");
        if (page == shown && current != null) return;
        shown = page;
        current?.Leave();
        current = page switch
        {
            "proteins" => new BiomolProteinPage(c),
            "nucleic" => new BiomolNucleicPage(c),
            "carbs" => new BiomolCarbPage(c),
            "lipids" => new BiomolLipidPage(c),
            _ => new BiomolAminoPage(c),
        };
        host.Content = current;
        if (Motion.On) Motion.FadeIn(current, Motion.Normal);
    }
}

// ---- what is remembered, and what a launch asks for ----

/// <summary>The builder's choices in Prefs (SimplBio.biomol.&lt;field&gt;, on this PC only), read once a run; and a launch's
/// state or another tool's hand-off applied to the model (not remembered).</summary>
internal static class BiomolPrefs
{
    private const string Key = "biomol";
    private static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        if (Str("style") is { } st && Style(st) is { } s) BiomoleculeBuilderModel.Style = s;
        BiomoleculeBuilderModel.Ph7 = BioPrefs.Bool(Key, "ph7", true);
        if (BiomolColourBys.Parse(Str("colour")) is { } cb) BiomoleculeBuilderModel.ColourBy = cb;
        BiomoleculeBuilderModel.Labels = BioPrefs.Bool(Key, "labels", true);
        foreach (var page in BiomoleculeTool.Pages.Select(p => p.Id).Concat(new[] { "levels", "strand" }))
            if (Str("h." + page) is { } h) BiomoleculeBuilderModel.HydrogensChosen[page] = h == "on";
        if (Str("aa") is { Length: 1 } aa && BioAminoAcids.ByOne.ContainsKey(aa[0])) BiomoleculeBuilderModel.AminoAcid = aa[0];
        BiomoleculeBuilderModel.Mirrored = BioPrefs.Bool(Key, "mirror");
        if (Str("proteinView") is "build" or "levels") BiomoleculeBuilderModel.ProteinView = Str("proteinView")!;
        if (Str("seq") is { } seq && BiomolPeptideBuilder.Clean(seq) is { Length: > 0 } clean)
        {
            BiomoleculeBuilderModel.Sequence = clean;
            BiomoleculeBuilderModel.Structure = Str("ss") ?? new string('C', clean.Length);
        }
        BiomoleculeBuilderModel.ShowHBonds = BioPrefs.Bool(Key, "hbonds", true);
        if (Ribbon(Str("ribbon")) is { } rb) BiomoleculeBuilderModel.Ribbon = rb;
        BiomoleculeBuilderModel.SheetDemo = BioPrefs.Bool(Key, "sheet");
        if (Str("level") is { } lv && BiomoleculeBuilderModel.Levels.Contains(lv)) BiomoleculeBuilderModel.Level = lv;
        if (Centroids(Str("centroids")) is { } ce) BiomoleculeBuilderModel.Centroids = ce;
        if (Str("nucleicView") is "nucleotides" or "strand") BiomoleculeBuilderModel.NucleicView = Str("nucleicView")!;
        if (Str("nt") is { } nt && BiomolNucleotide.Codes.Contains(nt)) BiomoleculeBuilderModel.Nucleotide = nt;
        if (Str("strand") is { } sd && BiomolInput.Dna(sd).Sequence is { Length: > 0 } dna) BiomoleculeBuilderModel.Strand = dna;
        BiomoleculeBuilderModel.Complement = BioPrefs.Bool(Key, "complement");
        if (Str("carbView") is "mono" or "di" or "poly") BiomoleculeBuilderModel.CarbView = Str("carbView")!;
        if (Str("mono") is { } mono && BiomolSugar.Monosaccharides.Contains(mono)) BiomoleculeBuilderModel.Monosaccharide = mono;
        BiomoleculeBuilderModel.CompareAnomers = BioPrefs.Bool(Key, "anomers");
        if (Str("di") is { } di && BiomolSugar.Disaccharides.Contains(di)) BiomoleculeBuilderModel.Disaccharide = di;
        BiomoleculeBuilderModel.Split = BioPrefs.Bool(Key, "split");
        if (Poly(Str("poly")) is { } ps) BiomoleculeBuilderModel.Polysaccharide = ps;
        if (Str("lipid") is { } lp && BiomoleculeBuilderModel.LipidChoices.Contains(lp)) BiomoleculeBuilderModel.Lipid = lp;
    }

    /// <summary>Everything chosen, remembered (Prefs writes only what changed).</summary>
    public static void Save()
    {
        BioPrefs.Set(Key, "style", StyleKey(BiomoleculeBuilderModel.Style));
        BioPrefs.Set(Key, "ph7", BiomoleculeBuilderModel.Ph7);
        BioPrefs.Set(Key, "colour", BiomoleculeBuilderModel.ColourBy.Key());
        BioPrefs.Set(Key, "labels", BiomoleculeBuilderModel.Labels);
        foreach (var (page, on) in BiomoleculeBuilderModel.HydrogensChosen) BioPrefs.Set(Key, "h." + page, on ? "on" : "off");
        BioPrefs.Set(Key, "aa", BiomoleculeBuilderModel.AminoAcid.ToString());
        BioPrefs.Set(Key, "mirror", BiomoleculeBuilderModel.Mirrored);
        BioPrefs.Set(Key, "proteinView", BiomoleculeBuilderModel.ProteinView);
        BioPrefs.Set(Key, "seq", BiomoleculeBuilderModel.Sequence);
        BioPrefs.Set(Key, "ss", BiomoleculeBuilderModel.Structure);
        BioPrefs.Set(Key, "hbonds", BiomoleculeBuilderModel.ShowHBonds);
        BioPrefs.Set(Key, "ribbon", BiomoleculeBuilderModel.Ribbon.Key());
        BioPrefs.Set(Key, "sheet", BiomoleculeBuilderModel.SheetDemo);
        BioPrefs.Set(Key, "level", BiomoleculeBuilderModel.Level);
        BioPrefs.Set(Key, "centroids", CentroidsKey(BiomoleculeBuilderModel.Centroids));
        BioPrefs.Set(Key, "nucleicView", BiomoleculeBuilderModel.NucleicView);
        BioPrefs.Set(Key, "nt", BiomoleculeBuilderModel.Nucleotide);
        BioPrefs.Set(Key, "strand", BiomoleculeBuilderModel.Strand);
        BioPrefs.Set(Key, "complement", BiomoleculeBuilderModel.Complement);
        BioPrefs.Set(Key, "carbView", BiomoleculeBuilderModel.CarbView);
        BioPrefs.Set(Key, "mono", BiomoleculeBuilderModel.Monosaccharide);
        BioPrefs.Set(Key, "anomers", BiomoleculeBuilderModel.CompareAnomers);
        BioPrefs.Set(Key, "di", BiomoleculeBuilderModel.Disaccharide);
        BioPrefs.Set(Key, "split", BiomoleculeBuilderModel.Split);
        BioPrefs.Set(Key, "poly", BiomoleculeBuilderModel.Polysaccharide.Key());
        BioPrefs.Set(Key, "lipid", BiomoleculeBuilderModel.Lipid);
    }

    /// <summary>A change made: remembered, and the views told.</summary>
    public static void Changed()
    {
        Save();
        BiomoleculeBuilderModel.Tell();
    }

    private static string? Str(string field) => BioPrefs.String(Key, field);

    public static string StyleKey(BioAtomStyle s) => s switch { BioAtomStyle.SpaceFill => "spacefill", BioAtomStyle.Sticks => "sticks", _ => "ballstick" };

    public static BioAtomStyle? Style(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "ballstick" or "ball" or "ball-and-stick" => BioAtomStyle.BallStick,
        "spacefill" or "space" or "space-filling" => BioAtomStyle.SpaceFill,
        "sticks" or "stick" => BioAtomStyle.Sticks,
        _ => null,
    };

    public static BiomolRibbon? Ribbon(string? s) => BiomolRibbons.All.Cast<BiomolRibbon?>().FirstOrDefault(r => r!.Value.Key() == s?.Trim().ToLowerInvariant());

    public static string CentroidsKey(BiomolCentroids c) => c switch { BiomolCentroids.Core => "core", BiomolCentroids.Surface => "surface", _ => "all" };

    public static BiomolCentroids? Centroids(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "all" => BiomolCentroids.All,
        "core" => BiomolCentroids.Core,
        "surface" => BiomolCentroids.Surface,
        _ => null,
    };

    public static BiomolPolysaccharide? Poly(string? s) => BiomolPolysaccharides.All.Cast<BiomolPolysaccharide?>().FirstOrDefault(p => p!.Value.Key() == s?.Trim().ToLowerInvariant());

    /// <summary>
    /// A launch's state (spec §9; pictures), the same words as the Mac's: the page first (BioTools.Register opens it),
    /// then any of — shared: style=ball|space|sticks, colour=element|part|class, neutral (or ph7=off), nolabels,
    /// h=on|off, select=&lt;id&gt;; amino acids: aa=W, dform (or mirror); proteins: build, levels,
    /// level=primary|secondary|tertiary|quaternary, seq=…, ss=helix|strand|coil (or a letter a residue: CCHHH…), hbonds,
    /// nohbonds, ribbon=off|over|only, sheet, core, surface, denatured; nucleic acids: nt=DA, strand, seq=ATGC, complement;
    /// carbohydrates: mono=GLC, anomers, di=MAL (or sugar=GLC|MAL), split, poly=cellulose; lipids: lipid=POV (or
    /// lipid=compare, stearic beside oleic).
    /// </summary>
    public static void Apply(BioLaunchState s)
    {
        var page = s.Page ?? BioNav.Page(Key);
        // (shared)
        if (Style(s.Value("style")) is { } st) BiomoleculeBuilderModel.Style = st;
        if (BiomolColourBys.Parse(s.Value("colour") ?? s.Value("color")) is { } cb) BiomoleculeBuilderModel.ColourBy = cb;
        if (s.Has("neutral")) BiomoleculeBuilderModel.Ph7 = false;
        if (s.Value("ph7") is { } ph) BiomoleculeBuilderModel.Ph7 = ph is "on" or "1" or "true";
        if (s.Has("nolabels")) BiomoleculeBuilderModel.Labels = false;
        if (s.Value("h") is { } h)
            foreach (var p in new[] { "amino", "proteins", "levels", "nucleic", "strand", "carbs", "lipids" }) BiomoleculeBuilderModel.HydrogensChosen[p] = h is "on" or "1" or "true";
        if (s.Value("select") is { Length: > 0 } select) Selection = select;
        // (amino acids)
        if (s.Value("aa") is { Length: > 0 } aa && BioAminoAcids.Find(aa) is { } a) BiomoleculeBuilderModel.AminoAcid = a.One;
        if (s.Has("mirror") || s.Has("dform")) BiomoleculeBuilderModel.Mirrored = true;
        // (proteins)
        if (s.Value("seq") is { Length: > 0 } seq)
        {
            if (page == "nucleic" || s.Has("strand"))
            {
                if (BiomolInput.Dna(seq).Sequence is { Length: > 0 } d) BiomoleculeBuilderModel.Strand = d;
                BiomoleculeBuilderModel.NucleicView = "strand";
            }
            else if (BiomolInput.Protein(seq).Sequence is { Length: > 0 } p) { BiomoleculeBuilderModel.Sequence = p; BiomoleculeBuilderModel.Structure = new string('C', p.Length); }
        }
        if (s.Value("ss") is { Length: > 0 } ss)
        {
            var n = BiomoleculeBuilderModel.Sequence.Length;
            BiomoleculeBuilderModel.Structure = ss.ToLowerInvariant() switch
            {
                "helix" => new string('H', n),
                "strand" => new string('E', n),
                "coil" => new string('C', n),
                _ => BiomolSSes.Text(BiomolSSes.Parse(ss, n)),
            };
        }
        if (s.Has("hbonds")) BiomoleculeBuilderModel.ShowHBonds = true;
        if (s.Has("nohbonds")) BiomoleculeBuilderModel.ShowHBonds = false;
        if (Ribbon(s.Value("ribbon")) is { } rb) BiomoleculeBuilderModel.Ribbon = rb;
        if (s.Has("sheet")) { BiomoleculeBuilderModel.SheetDemo = true; BiomoleculeBuilderModel.ProteinView = "build"; }
        if (s.Has("build")) BiomoleculeBuilderModel.ProteinView = "build";
        if (s.Has("levels")) BiomoleculeBuilderModel.ProteinView = "levels";
        if (s.Value("level") is { } lv && BiomoleculeBuilderModel.Levels.Contains(lv.ToLowerInvariant()))
        {
            BiomoleculeBuilderModel.Level = lv.ToLowerInvariant();
            BiomoleculeBuilderModel.ProteinView = "levels";
        }
        foreach (var name in new[] { "core", "surface" })
            if (s.Has(name)) BiomoleculeBuilderModel.Centroids = Centroids(name)!.Value;
        if (s.Has("denatured")) BiomoleculeBuilderModel.Denatured = true;
        // (nucleic acids)
        if (s.Value("nt") is { } nt && BiomolNucleotide.Codes.Contains(nt.ToUpperInvariant())) { BiomoleculeBuilderModel.Nucleotide = nt.ToUpperInvariant(); BiomoleculeBuilderModel.NucleicView = "nucleotides"; }
        if (s.Has("strand")) BiomoleculeBuilderModel.NucleicView = "strand";
        if (s.Has("complement")) BiomoleculeBuilderModel.Complement = true;
        // (carbohydrates)
        foreach (var sugar in new[] { s.Value("sugar"), s.Value("mono"), s.Value("di") })
        {
            var code = sugar?.ToUpperInvariant();
            if (code == null) continue;
            if (BiomolSugar.Monosaccharides.Contains(code)) { BiomoleculeBuilderModel.Monosaccharide = code; BiomoleculeBuilderModel.CarbView = "mono"; }
            if (BiomolSugar.Disaccharides.Contains(code)) { BiomoleculeBuilderModel.Disaccharide = code; BiomoleculeBuilderModel.CarbView = "di"; }
        }
        if (s.Has("split")) { BiomoleculeBuilderModel.Split = true; BiomoleculeBuilderModel.CarbView = "di"; }
        if (s.Has("anomers"))
        {
            BiomoleculeBuilderModel.CompareAnomers = true;
            BiomoleculeBuilderModel.CarbView = "mono";
            if (BiomoleculeBuilderModel.Monosaccharide is not ("GLC" or "BGC")) BiomoleculeBuilderModel.Monosaccharide = "GLC";
        }
        if (Poly(s.Value("poly")) is { } poly) { BiomoleculeBuilderModel.Polysaccharide = poly; BiomoleculeBuilderModel.CarbView = "poly"; }
        // (lipids)
        if (s.Value("lipid") is { } lipid)
        {
            if (string.Equals(lipid, "compare", StringComparison.OrdinalIgnoreCase)) BiomoleculeBuilderModel.Lipid = "STE+OLA";
            else if (BiomoleculeBuilderModel.LipidChoices.FirstOrDefault(x => string.Equals(x, lipid, StringComparison.OrdinalIgnoreCase)) is { } lc) BiomoleculeBuilderModel.Lipid = lc;
        }
    }

    /// <summary>What a launch asked to have picked (select=&lt;id&gt;): the page shows it picked when it first draws, once.</summary>
    public static string? Selection { get; set; }

    /// <summary>The page whose "Show H" a choice is kept for (the structure levels and the strand have their own).</summary>
    public static string HydrogenPage(string page) =>
        page == "proteins" && BiomoleculeBuilderModel.ProteinView == "levels" ? "levels" : (page == "nucleic" && BiomoleculeBuilderModel.NucleicView == "strand" ? "strand" : page);

    /// <summary>A sequence another tool handed over: up to 12 of A, C, G and T is a DNA strand to build; anything else a
    /// protein's one-letter codes (a stop's * dropped).</summary>
    public static void HandSequence(string text)
    {
        var seq = new string(text.ToUpperInvariant().Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        if (seq.Length <= BiomolStrand.MaxLength && seq.All(ch => "ACGT".Contains(ch)))
        {
            BiomoleculeBuilderModel.Strand = seq;
            BiomoleculeBuilderModel.NucleicView = "strand";
            BioNav.Set(Key, "nucleic", remember: false);
            return;
        }
        if (BiomolInput.Protein(seq.Replace("*", "")).Sequence is not { Length: > 0 } p) return;
        BiomoleculeBuilderModel.ProteinView = "build";
        BiomoleculeBuilderModel.SheetDemo = false;
        BiomoleculeBuilderModel.Sequence = p;
        BiomoleculeBuilderModel.Structure = new string('C', p.Length);
        BioNav.Set(Key, "proteins", remember: false);
    }

    /// <summary>A molecule another tool handed over ("TRP", "lysine", "DA", "ATP", "GLC", "POV"): its page, showing it. An
    /// amino acid comes by its name or three letters (as on the Mac): one letter would take AMP's "A" for alanine.</summary>
    public static void Hand(string molecule)
    {
        var code = molecule.Trim();
        var up = code.ToUpperInvariant();
        if (code.Length >= 3 && BioAminoAcids.Find(code) is { } aa)
        {
            BiomoleculeBuilderModel.AminoAcid = aa.One;
            BiomoleculeBuilderModel.Mirrored = false;
            BioNav.Set(Key, "amino", remember: false);
        }
        else if (BiomolNucleotide.Codes.Contains(up))
        {
            BiomoleculeBuilderModel.Nucleotide = up;
            BiomoleculeBuilderModel.NucleicView = "nucleotides";
            BioNav.Set(Key, "nucleic", remember: false);
        }
        else if (BiomolSugar.Monosaccharides.Contains(up))
        {
            BiomoleculeBuilderModel.Monosaccharide = up;
            BiomoleculeBuilderModel.CarbView = "mono";
            BioNav.Set(Key, "carbs", remember: false);
        }
        else if (BiomolSugar.Disaccharides.Contains(up))
        {
            BiomoleculeBuilderModel.Disaccharide = up;
            BiomoleculeBuilderModel.CarbView = "di";
            BioNav.Set(Key, "carbs", remember: false);
        }
        else if (BiomoleculeBuilderModel.LipidChoices.Contains(up))
        {
            BiomoleculeBuilderModel.Lipid = up;
            BioNav.Set(Key, "lipids", remember: false);
        }
    }
}

// ---- the pieces the pages share ----

/// <summary>
/// A page of the builder (the chemistry builders' three columns, BioUI.Work): its choices at the left, its 3D view in its
/// card in the middle (title at the top left, Turn and Reset view at the top right), its card and facts at the right.
/// It draws again when the model changes or the app goes light or dark.
/// </summary>
internal abstract class BiomolPageView : Grid
{
    protected readonly ToolContext C;
    protected readonly BiomolStage Stage;
    protected readonly Stack Left = K.V(14), Right = K.V(14);
    /// <summary>The middle column: the 3D view's card, and anything a page shows instead of it.</summary>
    protected readonly Grid Centre = new();
    protected readonly Border SceneCard;
    private readonly TextBlock title = K.Text("", Ty.Subtitle), subtitle = K.Text("", Ty.Body, B.Text2);
    /// <summary>What a press on the 3D view picked (its id), shown in the facts.</summary>
    protected string? Picked;

    protected BiomolPageView(ToolContext c, string accessibility)
    {
        C = c;
        Stage = new BiomolStage(accessibility);
        Stage.View.Picked += id => { Picked = id; DrawFacts(); };
        var words = K.V(2, title, subtitle);
        words.VerticalAlignment = VerticalAlignment.Center;
        subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        var bar = K.Columns("*, auto", words.Col(0), BioSceneCard.Buttons(Stage.View).Margin(12, 0, 0, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center).Col(1));
        SceneCard = BioSceneCard.Make(bar, Stage.View, "Drag to turn · scroll to zoom · click an atom or a label to pick it");
        Centre.Children.Add(SceneCard);
        Children.Add(BioUI.Work(Left, Centre, Right));
        // (a change drawn once, however many were told in a row)
        var pending = false;
        void Changed()
        {
            if (pending) return;
            pending = true;
            Dispatcher.BeginInvoke(() => { pending = false; if (IsLoaded) Draw(); });
        }
        Loaded += (_, _) =>
        {
            BiomoleculeBuilderModel.Changed -= Changed;
            BiomoleculeBuilderModel.Changed += Changed;
            Draw();
            // (what a launch asked to have picked, once)
            if (BiomolPrefs.Selection is { } chosen)
            {
                BiomolPrefs.Selection = null;
                Picked = chosen;
                Stage.View.Selection = chosen;
                DrawFacts();
            }
        };
        Unloaded += (_, _) =>
        {
            BiomoleculeBuilderModel.Changed -= Changed;
            Stage.Stop();
        };
    }

    /// <summary>The scene card's title and the line under it.</summary>
    protected void Title(string t, string sub)
    {
        title.Text = t;
        subtitle.Text = sub;
        subtitle.ToolTip = sub.Length > 40 ? sub : null;
    }

    /// <summary>Everything drawn again from the model: the left column's choices synced, the scene asked for, the facts.</summary>
    protected abstract void Draw();

    /// <summary>The right column drawn again (a pick, a change).</summary>
    protected abstract void DrawFacts();

    /// <summary>Ctrl+F: the page's sequence field focused, if it has one.</summary>
    public virtual bool FocusField() => false;

    /// <summary>The page let go (another page chosen).</summary>
    public virtual void Leave() => Stage.Stop();

    protected static bool Dark => Appearance.Dark;

    /// <summary>The chemistry tables, as the scenes take them (MBChem's CPK colours and radii).</summary>
    protected static readonly BiomolChem Chem = new(BioMoleculeScene.CpkHex, MBChem.Covalent, MBChem.Vdw);

    /// <summary>A look for this page: the shared choices, H as the page has them.</summary>
    protected static BiomolLook LookFor(string hydrogenPage, int atoms) =>
        BiomoleculeBuilderModel.Look(BiomoleculeBuilderModel.Hydrogens(hydrogenPage, atoms), Dark);

    /// <summary>The atom a picked id names ("…:atom:31" → 31), if it names one.</summary>
    protected static int? PickedAtom(string? id) => BiomolText.AtomIndex(id);

    /// <summary>An element's name ("Nitrogen").</summary>
    protected static string ElementName(string symbol) => MBChem.Element(symbol)?.Name ?? symbol;

    /// <summary>The facts' "Picked" card for an atom: "Lys 12 · NZ", its element and what it is part of.</summary>
    protected static FrameworkElement PickedCard(string where, string element, string? part = null) =>
        BioUI.Card(where, MBChem.ColorOf(element), $"{ElementName(element)}{(part != null ? " · " + part : "")}",
            BioUI.Caption("Picked in the 3D view. Press Escape or empty space to let go."));

    /// <summary>Opens the atoms in the chemistry molecule builder (§4.4: as they are; its VSEPR relax is not run on them).</summary>
    protected void OpenInBuilder(BioMoleculeModel model, string name) => BiomolHandOver.ToMoleculeBuilder(C, model, name, BiomoleculeBuilderModel.Hydrogens(HydrogenKey, model.Atoms.Count));

    /// <summary>The page whose "Show H" applies now.</summary>
    protected abstract string HydrogenKey { get; }
}

/// <summary>The builder's molecules handed to the chemistry builder and its pages handed to the other biology tools.</summary>
internal static class BiomolHandOver
{
    /// <summary>
    /// The atoms and bonds (with H if shown) in the chemistry builder's molecule, as they are — §4.4: its VSEPR relax is not
    /// run on them — centred, ids clear of the builder's own; then the builder opened over the window.
    /// </summary>
    public static void ToMoleculeBuilder(ToolContext c, BioMoleculeModel model, string name, bool hydrogens)
    {
        var (_, atoms, bonds) = BiomolHandOff.Parse(BiomolHandOff.Text(model, name, hydrogens));
        if (atoms.Count == 0) return;
        var centre = BioV.BoundingSphere(atoms.Select(a => a.Pos).ToList()).Centre;
        var m = MoleculeModel.Shared;
        m.Clear();
        // (ids far above the builder's own count, so atoms it adds later never share one)
        const int first = 1_000_000;
        for (var i = 0; i < atoms.Count; i++)
        {
            var p = atoms[i].Pos - centre;
            m.Atoms.Add(new MBAtom { Id = first + i, Symbol = atoms[i].Element, Pos = new Vector3D(p.X, p.Y, p.Z) });
        }
        for (var k = 0; k < bonds.Count; k++)
            m.Bonds.Add(new MBBond { Id = first + atoms.Count + k, A = first + bonds[k].A, B = first + bonds[k].B, Order = bonds[k].Order });
        m.Selected = null;
        Log.Write($"biomol: {name} handed to the molecule builder ({atoms.Count} atoms, {bonds.Count} bonds)");
        if ((c.Owner ?? Application.Current?.MainWindow) is { } owner) Chemistry.OpenMolecule(owner, c.Engine, null);
    }

    /// <summary>Another biology tool opened at a page, with something for it (BioTools.Open closes this one's sheet).</summary>
    public static void ToTool(ToolContext c, string key, string page, Dictionary<string, string>? info = null)
    {
        BioNav.Set(key, page);
        BioTools.Open(c, key, info);
    }
}

/// <summary>
/// The View controls every page has (§4.4 Shared controls): style (Ball &amp; stick, Space filling, Sticks), Show H, pH 7
/// form, Colour by (Element, Part, Class), labels, and Open in Molecule Builder — made once and kept in step with the
/// model, so keyboard focus stays where it is when they change.
/// </summary>
internal sealed class BiomolViewControls : ContentControl
{
    private readonly SelectorBar style = new(new[] { BioAtomStyle.BallStick, BioAtomStyle.SpaceFill, BioAtomStyle.Sticks }.Select(s => s.Label()).ToList(), 0);
    private readonly SelectorBar colour = new(BiomolColourBys.All.Select(c => c.Label()).ToList(), 0);
    private readonly ToggleSwitch hydrogens = new(), ph7 = new(), labels = new();
    private readonly TextBlock hydrogensNote = BioUI.Caption(""), ph7Note = BioUI.Caption(""), colourNote = BioUI.Caption("");
    private readonly Wpf.Ui.Controls.Button open;
    private readonly FrameworkElement atomRows, colourRows;
    private string page = "amino";
    private int atoms;
    private bool syncing;

    /// <summary>What "Open in Molecule Builder" hands over (null: nothing to open).</summary>
    public Func<(BioMoleculeModel Model, string Name)?>? Molecule { get; set; }
    public Action<BioMoleculeModel, string>? Open { get; set; }

    public BiomolViewControls()
    {
        Focusable = false;
        style.Changed += i =>
        {
            if (syncing) return;
            BiomoleculeBuilderModel.Style = (BioAtomStyle)i;
            BiomolPrefs.Changed();
        };
        colour.Changed += i =>
        {
            if (syncing) return;
            BiomoleculeBuilderModel.ColourBy = BiomolColourBys.All[i];
            BiomolPrefs.Changed();
        };
        void Toggle(ToggleSwitch s, string name, Action<bool> set)
        {
            System.Windows.Automation.AutomationProperties.SetName(s, name);
            s.Checked += (_, _) => { if (!syncing) { set(true); BiomolPrefs.Changed(); } };
            s.Unchecked += (_, _) => { if (!syncing) { set(false); BiomolPrefs.Changed(); } };
        }
        Toggle(hydrogens, "Show H atoms", on => BiomoleculeBuilderModel.HydrogensChosen[page] = on);
        Toggle(ph7, "pH 7 form", on => BiomoleculeBuilderModel.Ph7 = on);
        Toggle(labels, "Labels", on => BiomoleculeBuilderModel.Labels = on);
        open = ChemKit.Wide("Open in Molecule Builder", () =>
        {
            if (Molecule?.Invoke() is { } m) Open?.Invoke(m.Model, m.Name);
        }, icon: SymbolRegular.Molecule20);
        open.ToolTip = "See its formula, molar mass, polarity and bond angles in the chemistry builder (the atoms stay as they are here)";
        style.ToolTip = "How the atoms are drawn";
        colour.ToolTip = "Colour the atoms by element (CPK), by part, or by class";
        atomRows = K.V(10, style, Row("Show H atoms", hydrogens, "Hydrogen atoms (off by default for chains over 200 atoms)"), hydrogensNote);
        colourRows = K.V(8, K.Text("Colour by", Ty.Caption, B.Text2).Strong(), colour, colourNote);
        ph7Rows = K.V(6, Row("pH 7 form", ph7, "On: charged groups as cells have them (–NH₃⁺, –COO⁻). Off: the neutral form textbooks often draw."), ph7Note);
        Content = K.V(12, BioUI.Heading("View"), atomRows, ph7Rows, colourRows, Row("Labels", labels, "Labels with leader lines on the parts"), open);
    }

    private readonly FrameworkElement ph7Rows;

    private static FrameworkElement Row(string text, ToggleSwitch s, string tip)
    {
        s.VerticalAlignment = VerticalAlignment.Center;
        var words = K.Text(text, Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center);
        var row = K.Columns("*, auto", words.Col(0), s.Col(1));
        row.ToolTip = tip;
        return row;
    }

    /// <summary>The controls set as the model has them, for a page (whose "Show H" they show) and a molecule of so many
    /// atoms; a reason given turns a control off with it said under it.</summary>
    public void Sync(string hydrogenPage, int atomCount, string? noHydrogens = null, string? noPh7 = null, bool noAtoms = false, string? colourHint = null)
    {
        syncing = true;
        try
        {
            page = hydrogenPage;
            atoms = atomCount;
            style.Select((int)BiomoleculeBuilderModel.Style, false);
            colour.Select(Array.IndexOf(BiomolColourBys.All, BiomoleculeBuilderModel.ColourBy), false);
            var h = noHydrogens == null && BiomoleculeBuilderModel.Hydrogens(page, atoms);
            if (hydrogens.IsChecked != h) hydrogens.IsChecked = h;
            hydrogens.IsEnabled = noHydrogens == null;
            hydrogensNote.Text = noHydrogens ?? "";
            hydrogensNote.Visibility = noHydrogens == null ? Visibility.Collapsed : Visibility.Visible;
            if (ph7.IsChecked != BiomoleculeBuilderModel.Ph7) ph7.IsChecked = BiomoleculeBuilderModel.Ph7;
            ph7.IsEnabled = noPh7 == null;
            ph7Note.Text = noPh7 ?? "";
            ph7Note.Visibility = noPh7 == null ? Visibility.Collapsed : Visibility.Visible;
            colourNote.Text = colourHint ?? "";
            colourNote.Visibility = colourHint == null ? Visibility.Collapsed : Visibility.Visible;
            if (labels.IsChecked != BiomoleculeBuilderModel.Labels) labels.IsChecked = BiomoleculeBuilderModel.Labels;
            atomRows.Visibility = noAtoms ? Visibility.Collapsed : Visibility.Visible;
            ph7Rows.Visibility = noAtoms ? Visibility.Collapsed : Visibility.Visible;
            colourRows.Visibility = noAtoms ? Visibility.Collapsed : Visibility.Visible;
            var can = !noAtoms && Molecule?.Invoke() != null;
            open.IsEnabled = can;
            open.Visibility = noAtoms ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { syncing = false; }
    }
}

/// <summary>
/// A list of choices (the nucleotides, the sugars, the lipids, the structure levels): rows of a title and a line under it,
/// with what leads each (a letter, a swatch); headings between groups; the chosen row tinted in the accent with a bar at
/// its left (Windows 11's list selection). Made once; <see cref="Select"/> only changes which row looks chosen, so
/// keyboard focus stays on the row pressed.
/// </summary>
internal sealed class BiomolList : ContentControl
{
    private readonly Stack stack = K.V(2);
    private readonly List<(string Key, Border Wash, Border Mark, Button Button, string Title)> rows = new();
    public event Action<string>? Picked;

    public BiomolList()
    {
        Focusable = false;
        Content = stack;
    }

    public void Heading(string text)
    {
        var t = K.Text(text, Ty.Caption, B.Text2).Strong();
        t.Margin = new Thickness(4, stack.Children.Count == 0 ? 0 : 10, 0, 4);
        System.Windows.Automation.AutomationProperties.SetHeadingLevel(t, System.Windows.Automation.AutomationHeadingLevel.Level4);
        stack.Children.Add(t);
    }

    public void Row(string key, string title, string? caption = null, UIElement? lead = null, string? tip = null)
    {
        var wash = new Border { CornerRadius = new CornerRadius(4), Opacity = 0.14, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        wash.SetResourceReference(Border.BackgroundProperty, B.Accent);
        var mark = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        mark.SetResourceReference(Border.BackgroundProperty, B.Accent);
        var words = K.V(1, K.Text(title, Ty.Body));
        if (caption != null) words.Children.Add(K.Text(caption, Ty.Caption, B.Text2));
        words.VerticalAlignment = VerticalAlignment.Center;
        var face = K.Columns("auto, *", (lead as FrameworkElement)?.Margin(0, 0, 10, 0).Col(0), words.Col(1));
        face.Margin = new Thickness(10, 6, 8, 6);
        var g = new Grid { Children = { wash, face, mark } };
        var b = K.Subtle(g, () => Picked?.Invoke(key), new Thickness(0), 4);
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.ToolTip = tip ?? caption;
        System.Windows.Automation.AutomationProperties.SetName(b, caption != null ? $"{title}, {caption}" : title);
        rows.Add((key, wash, mark, b, title));
        stack.Children.Add(b);
    }

    /// <summary>The chosen row (null: none).</summary>
    public void Select(string? key)
    {
        foreach (var (k, wash, mark, button, title) in rows)
        {
            var on = k == key;
            wash.Visibility = mark.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, on ? "chosen" : "");
        }
    }
}

/// <summary>Small pieces the pages draw with.</summary>
internal static class BiomolKit
{
    /// <summary>A letter on a rounded tile of its colour's tint (a base, an amino acid's code).</summary>
    public static Border Letter(string text, Color colour, double size = 26)
    {
        var t = new TextBlock { Text = text, FontSize = size * 0.5, FontWeight = FontWeights.Bold, FontFamily = BioUI.Mono, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = K.Brush(K.Darkened(colour)) };
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.25), Background = K.Brush(K.WithAlpha(colour, Appearance.Dark ? 0.24 : 0.16)), Child = t, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>A toggle with its words (the left column's rows).</summary>
    public static FrameworkElement Toggle(string text, ToggleSwitch s, string? tip = null)
    {
        s.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(s, text);
        var row = K.Columns("*, auto", K.Wrapped(text, Ty.Body).Align(HorizontalAlignment.Left, VerticalAlignment.Center).Col(0), s.Col(1));
        if (tip != null) row.ToolTip = tip;
        return row;
    }

    /// <summary>A toggle's changes, but not those made while it is being set from the model.</summary>
    public static void OnToggle(ToggleSwitch s, Func<bool> syncing, Action<bool> set)
    {
        s.Checked += (_, _) => { if (!syncing()) set(true); };
        s.Unchecked += (_, _) => { if (!syncing()) set(false); };
    }

    /// <summary>A toggle set without its handlers acting.</summary>
    public static void Set(ToggleSwitch s, bool on)
    {
        if (s.IsChecked != on) s.IsChecked = on;
    }

    /// <summary>A link-like action in a card ("See in DNA &amp; RNA").</summary>
    public static FrameworkElement Action(string text, SymbolRegular icon, System.Action click, string? tip = null)
    {
        var b = K.Button(text, click, icon: icon);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.MinWidth = 0;
        if (tip != null) b.ToolTip = tip;
        return b;
    }

    /// <summary>"1,234.56" with the app's number style (invariant).</summary>
    public static string N(double v, string format) => v.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A minus written as a minus sign ("−3.9").</summary>
    public static string Signed(double v, string format) => v < 0 ? "−" + N(-v, format) : (v > 0 ? "+" + N(v, format) : N(v, format));
}
