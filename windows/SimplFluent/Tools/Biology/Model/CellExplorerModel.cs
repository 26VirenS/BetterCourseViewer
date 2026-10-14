using System.Globalization;

namespace Simpl.Tools;

// Cell Explorer's own model (spec §4.1, §5.1), the Mac's CellExplorerModel.swift part for part: the three cells' parts as
// the cards tell them — each part's Function line word for word from §5.1's tables (CellExplorerText.cs, copied out of
// the specification by a script), its structure line (double, single or no membrane), its real size, what the model
// draws of it and at what scale, where it is found (from the Compare table, §5.1.4) and its common mix-up — the parts
// list's groups, the Compare page's rows, each cell's "About" card, the quiz, and what the tool keeps between its views
// being made (CellExplorerModel: the options, what each cell shows and what is selected). Plain .NET, no WPF, so the
// unit tests reach it; the cells' geometry is CellExplorerBuild.cs's, the membrane page's CellMembraneModel.cs's.

/// <summary>The three cells.</summary>
public enum CEKind
{
    Animal,
    Plant,
    Bacterium,
}

public static class CEKinds
{
    public static readonly CEKind[] All = { CEKind.Animal, CEKind.Plant, CEKind.Bacterium };

    /// <summary>§4.1 Rendering: each cell's seed, so it is the same picture on every launch (and on the Mac).</summary>
    public static ulong Seed(this CEKind k) => k switch { CEKind.Animal => 0xC311, CEKind.Plant => 0x91A7, _ => 0xBAC7 };

    /// <summary>Its page id ("animal", "plant", "bacterium").</summary>
    public static string Id(this CEKind k) => k switch { CEKind.Animal => "animal", CEKind.Plant => "plant", _ => "bacterium" };

    public static CEKind? FromId(string? id) => id switch { "animal" => CEKind.Animal, "plant" => CEKind.Plant, "bacterium" => CEKind.Bacterium, _ => null };

    public static string Title(this CEKind k) => k switch { CEKind.Animal => "Animal cell", CEKind.Plant => "Plant cell", _ => "Bacterial cell" };

    /// <summary>"Found in"'s name for it.</summary>
    public static string FoundName(this CEKind k) => k switch { CEKind.Animal => "animal", CEKind.Plant => "plant", _ => "bacteria" };

    /// <summary>"an animal cell" (the accessibility line's words).</summary>
    public static string Phrase(this CEKind k) => k switch { CEKind.Animal => "an animal cell", CEKind.Plant => "a plant cell", _ => "a bacterial cell" };
}

/// <summary>How many membranes wrap a part: the card's structure line (§5.1.6).</summary>
public enum CEMembranes
{
    Double,
    Single,
    None,
}

/// <summary>The parts list's groups (§4.1 item 6), in its order.</summary>
public enum CEGroup
{
    Double,
    Single,
    None,
    Outside,
}

public static class CEGroups
{
    public static readonly CEGroup[] All = { CEGroup.Double, CEGroup.Single, CEGroup.None, CEGroup.Outside };

    public static string Title(this CEGroup g) => g switch
    {
        CEGroup.Double => "Membrane-bound (double membrane)",
        CEGroup.Single => "Membrane-bound (single membrane)",
        CEGroup.None => "Not membrane-bound",
        _ => "Outside the membrane",
    };

    public static string Line(this CEMembranes m) => m switch
    {
        CEMembranes.Double => "Double membrane",
        CEMembranes.Single => "Single membrane",
        _ => "No membrane",
    };
}

/// <summary>A mark in the Compare table: present, absent, or partly (some cells or species; related proteins only).</summary>
public enum CEMark
{
    Yes,
    No,
    Partly,
}

public static class CEMarks
{
    public static string Symbol(this CEMark m) => m switch { CEMark.Yes => "✓", CEMark.No => "✗", _ => "~" };

    /// <summary>A Compare cell read: its mark, and its note (what follows the mark, if anything).</summary>
    public static (CEMark Mark, string Note) Read(string cell)
    {
        var t = cell.Trim();
        if (t.StartsWith('✓')) return (CEMark.Yes, t[1..].Trim());
        if (t.StartsWith('✗')) return (CEMark.No, t[1..].Trim());
        if (t.StartsWith('~')) return (CEMark.Partly, t[1..].Trim());
        // ("some species", "some cells (sperm), 9 + 2 microtubules", "10–30 µm")
        return (CEMark.Partly, t);
    }
}

/// <summary>A part of a cell: an organelle type, as its card, its row in the parts list and its label tell it.</summary>
/// <param name="Id">Its id in the cell ("mito"); the scene's objects are "organelle:&lt;id&gt;:&lt;instance&gt;".</param>
/// <param name="Name">The card's title (§5.1's Structure column, tidied).</param>
/// <param name="Label">Its label on the 3D view.</param>
/// <param name="Function">The Function line, verbatim (§5.1.6); **term** marks the term to bold.</param>
/// <param name="StructureNote">What the structure line adds ("the nuclear envelope, with nuclear pores").</param>
/// <param name="StructureLine">A structure line of its own, in place of the membranes' line and the note.</param>
/// <param name="Size">The real size and count (§5.1's column).</param>
/// <param name="Model">What the model draws (§5.1's Model column).</param>
/// <param name="Alpha">Its translucency from §5.1, if it has one.</param>
/// <param name="Mixup">The common mix-up, if §5.1 has one.</param>
/// <param name="FoundIn">Animal, plant, bacteria: each a mark and a note (from §5.1.4).</param>
/// <param name="CompareRow">The Compare row it belongs to (its index in CESpecText.Compare), if any.</param>
/// <param name="DefaultVisible">Shown when the cell is first built (§5.1's "(Nice)" parts that are off by default are not).</param>
/// <param name="Priority">When more than 14 labels could show, the higher keep theirs (§3.5 item 6).</param>
/// <param name="Factor">Teaching scale's enlargement (drawn ÷ true size; 1 when drawn at its true size).</param>
/// <param name="LookInside">"Look inside": a mitochondrion or chloroplast cut lengthwise, the nucleus cut open.</param>
public sealed record CEPart(string Id, CEKind Kind, string Name, string Label, string Function, CEMembranes Membranes, string StructureNote,
    string? StructureLine, string Size, string Model, BioHexPair Colour, double? Alpha, string? Mixup, CEGroup Group,
    IReadOnlyList<(CEMark Mark, string Note)> FoundIn, int? CompareRow, bool DefaultVisible, bool Nice, double Priority, double Factor,
    bool LookInside)
{
    /// <summary>The card's structure line: "Double membrane · the nuclear envelope, with nuclear pores".</summary>
    public string StructureText => StructureLine ?? (StructureNote.Length == 0 ? Membranes.Line() : Membranes.Line() + " · " + StructureNote);

    /// <summary>The name inside a sentence ("Find the Golgi apparatus", "the rough ER", "the nucleus").</summary>
    public string NameInSentence => InSentence(Name);

    public string LabelInSentence => InSentence(Label);

    /// <summary>A name with its first letter lowered, unless it begins a proper name or an abbreviation (Golgi, ER, DNA) —
    /// the Mac's CEPart.inSentence.</summary>
    public static string InSentence(string s)
    {
        var first = s.Split(' ')[0];
        if (first.StartsWith("Golgi", StringComparison.Ordinal) || first.Length > 1 && first == first.ToUpperInvariant()) return s;
        return s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
    }

    /// <summary>"animal ✓ · plant ✓ · bacteria ✗" (§5.1.6).</summary>
    public string FoundInText => string.Join(" · ", CEKinds.All.Zip(FoundIn).Select(z => $"{z.First.FoundName()} {z.Second.Mark.Symbol()}"));

    /// <summary>The Compare table's notes for each cell, where it has one ("plant: 80S (+70S in chloroplasts and mitochondria)").</summary>
    public IReadOnlyList<string> FoundInNotes => CEKinds.All.Zip(FoundIn).Where(z => z.Second.Note.Length > 0).Select(z => $"{z.First.FoundName()}: {z.Second.Note}").ToList();
}

// ---- the catalogue ----

public static class CEParts
{
    /// <summary>A cell's parts in the parts list's order (by group, then as §5.1 lists them).</summary>
    public static IReadOnlyList<CEPart> All(CEKind k) => Catalogue.Value[k];

    public static CEPart? Part(CEKind k, string id) => All(k).FirstOrDefault(p => p.Id == id);

    private static readonly Lazy<Dictionary<CEKind, IReadOnlyList<CEPart>>> Catalogue = new(() =>
    {
        var out_ = new Dictionary<CEKind, IReadOnlyList<CEPart>>();
        foreach (var k in CEKinds.All)
        {
            var parts = Specs(k).Select(s => Make(s, k)).ToList();
            out_[k] = CEGroups.All.SelectMany(g => parts.Where(p => p.Group == g)).ToList();
        }
        return out_;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>What the catalogue knows of a part beyond §5.1's words: its id, which row it comes from (its Structure, as
    /// written there), its label, its structure, its group, its Compare row's Structure (or its own Found in), and how it is
    /// drawn.</summary>
    private sealed record Spec(string Id, string Row, string Label, CEMembranes Membranes, CEGroup Group, double Priority, string? Name = null,
        string Note = "", string? Line = null, string? Compare = null, string[]? Found = null, bool Visible = true, double Factor = 1,
        bool Inside = false);

    private static Spec[] Specs(CEKind k) => k switch
    {
        CEKind.Animal => new Spec[]
        {
            new("pm", "Plasma membrane", "Plasma membrane", CEMembranes.Single, CEGroup.Single, 96,
                Note: "it is the membrane: one phospholipid bilayer with proteins", Compare: "Plasma membrane"),
            new("nucleus", "Nucleus", "Nucleus", CEMembranes.Double, CEGroup.Double, 100, Note: "the nuclear envelope, with nuclear pores",
                Compare: "Nucleus (nuclear envelope)", Inside: true),
            new("mito", "Mitochondria", "Mitochondria", CEMembranes.Double, CEGroup.Double, 94,
                Note: "a smooth outer membrane and a folded inner one (the cristae)", Compare: "Mitochondria", Inside: true),
            new("rer", "Rough ER", "Rough ER", CEMembranes.Single, CEGroup.Single, 92, Name: "Rough endoplasmic reticulum",
                Note: "flattened sacs (cisternae) studded with ribosomes", Compare: "Endoplasmic reticulum"),
            new("ser", "Smooth ER", "Smooth ER", CEMembranes.Single, CEGroup.Single, 88, Name: "Smooth endoplasmic reticulum",
                Note: "a network of tubules, no ribosomes", Compare: "Endoplasmic reticulum"),
            new("golgi", "Golgi apparatus", "Golgi apparatus", CEMembranes.Single, CEGroup.Single, 90, Note: "one around each flattened sac (cisterna)",
                Compare: "Golgi apparatus"),
            new("lysosome", "Lysosomes", "Lysosomes", CEMembranes.Single, CEGroup.Single, 86, Compare: "Lysosomes"),
            new("peroxisome", "Peroxisomes", "Peroxisomes", CEMembranes.Single, CEGroup.Single, 80, Compare: "Peroxisomes"),
            new("vesicle", "Vesicles", "Vesicles", CEMembranes.Single, CEGroup.Single, 70, Compare: "Vesicles", Factor: 1.5),
            new("nucleolus", "Nucleolus", "Nucleolus", CEMembranes.None, CEGroup.None, 84, Note: "a dense region inside the nucleus", Compare: "Nucleolus"),
            new("chromatin", "Chromatin", "Chromatin", CEMembranes.None, CEGroup.None, 60, Note: "inside the nucleus", Compare: "Nucleus (nuclear envelope)"),
            new("pore", "Nuclear pores", "Nuclear pores", CEMembranes.None, CEGroup.None, 55,
                Note: "protein channels through both membranes of the envelope", Compare: "Nucleus (nuclear envelope)"),
            new("ribosome", "Ribosomes (free and bound)", "Ribosomes", CEMembranes.None, CEGroup.None, 82, Name: "Ribosomes",
                Note: "two subunits of RNA and protein", Compare: "Ribosomes", Factor: 4),
            new("centrosome", "Centrosome and centrioles", "Centrosome", CEMembranes.None, CEGroup.None, 78,
                Note: "two centrioles, each 9 triplets of microtubules", Compare: "Centrioles / centrosome"),
            new("microtubule", "Cytoskeleton: microtubules", "Microtubules", CEMembranes.None, CEGroup.None, 66, Name: "Microtubules (cytoskeleton)",
                Note: "hollow tubes of tubulin", Compare: "Cytoskeleton", Factor: 4),
            new("actin", "Cytoskeleton: actin (microfilaments)", "Actin filaments", CEMembranes.None, CEGroup.None, 58,
                Name: "Actin filaments (microfilaments)", Note: "two twisted chains of actin", Compare: "Cytoskeleton", Factor: 25.0 / 3.5),
            new("intermediate", "Cytoskeleton: intermediate filaments", "Intermediate filaments", CEMembranes.None, CEGroup.None, 52,
                Name: "Intermediate filaments", Note: "rope-like protein fibres", Compare: "Cytoskeleton", Factor: 7),
            new("cytoplasm", "Cytoplasm / cytosol", "Cytoplasm", CEMembranes.None, CEGroup.None, 64, Name: "Cytoplasm and cytosol",
                Line: "Not an organelle · fills the cell inside the plasma membrane", Compare: "Cytoplasm"),
            new("flagellum", "Cilia / flagella (Nice)", "Flagellum", CEMembranes.None, CEGroup.Outside, 50, Name: "Cilia and flagella",
                Line: "Covered by the plasma membrane · 9 + 2 microtubules inside", Compare: "Flagella", Visible: false),
        },
        CEKind.Plant => new Spec[]
        {
            new("nucleus", "Nucleus, nucleolus, pores", "Nucleus", CEMembranes.Double, CEGroup.Double, 100, Name: "Nucleus",
                Note: "the nuclear envelope, with nuclear pores", Compare: "Nucleus (nuclear envelope)", Inside: true),
            new("chloroplast", "Chloroplasts", "Chloroplasts", CEMembranes.Double, CEGroup.Double, 98,
                Note: "an outer and an inner envelope, and thylakoid membranes inside", Compare: "Chloroplasts", Inside: true),
            new("mito", "Mitochondria", "Mitochondria", CEMembranes.Double, CEGroup.Double, 93,
                Note: "a smooth outer membrane and a folded inner one (the cristae)", Compare: "Mitochondria", Inside: true),
            new("amyloplast", "Amyloplasts (Nice)", "Amyloplasts", CEMembranes.Double, CEGroup.Double, 50, Name: "Amyloplasts",
                Note: "plastids full of starch grains", Found: new[] { "✗", "✓ (roots, tubers)", "✗" }, Visible: false),
            new("pm", "Plasma membrane", "Plasma membrane", CEMembranes.Single, CEGroup.Single, 95,
                Note: "it is the membrane: one phospholipid bilayer with proteins", Compare: "Plasma membrane"),
            new("vacuole", "Central vacuole", "Central vacuole", CEMembranes.Single, CEGroup.Single, 99, Note: "the tonoplast", Compare: "Central vacuole"),
            new("rer", "Rough and smooth ER", "Rough ER", CEMembranes.Single, CEGroup.Single, 88, Name: "Rough endoplasmic reticulum",
                Note: "flattened sacs (cisternae) studded with ribosomes", Compare: "Endoplasmic reticulum"),
            new("ser", "Rough and smooth ER", "Smooth ER", CEMembranes.Single, CEGroup.Single, 80, Name: "Smooth endoplasmic reticulum",
                Note: "a network of tubules, no ribosomes", Compare: "Endoplasmic reticulum"),
            new("golgi", "Golgi (dictyosomes)", "Golgi (dictyosomes)", CEMembranes.Single, CEGroup.Single, 86, Name: "Golgi apparatus (dictyosomes)",
                Note: "one around each flattened sac (cisterna)", Compare: "Golgi apparatus"),
            new("peroxisome", "Peroxisomes", "Peroxisomes", CEMembranes.Single, CEGroup.Single, 76, Compare: "Peroxisomes"),
            new("vesicle", "Vesicles", "Vesicles", CEMembranes.Single, CEGroup.Single, 60, Compare: "Vesicles", Factor: 1.5),
            new("nucleolus", "Nucleolus", "Nucleolus", CEMembranes.None, CEGroup.None, 74, Note: "a dense region inside the nucleus", Compare: "Nucleolus"),
            new("chromatin", "Chromatin", "Chromatin", CEMembranes.None, CEGroup.None, 56, Note: "inside the nucleus", Compare: "Nucleus (nuclear envelope)"),
            new("pore", "Nuclear pores", "Nuclear pores", CEMembranes.None, CEGroup.None, 54,
                Note: "protein channels through both membranes of the envelope", Compare: "Nucleus (nuclear envelope)"),
            new("ribosome", "Ribosomes", "Ribosomes", CEMembranes.None, CEGroup.None, 78, Note: "two subunits of RNA and protein", Compare: "Ribosomes", Factor: 4),
            new("microtubule", "Cytoskeleton", "Microtubules", CEMembranes.None, CEGroup.None, 62, Name: "Microtubules (cytoskeleton)",
                Note: "hollow tubes of tubulin, in a cortical array under the membrane", Compare: "Cytoskeleton", Factor: 4),
            new("actin", "Cytoskeleton", "Actin filaments", CEMembranes.None, CEGroup.None, 57, Name: "Actin filaments (microfilaments)",
                Note: "two twisted chains of actin", Compare: "Cytoskeleton", Factor: 25.0 / 3.5),
            new("cytoplasm", "Cytoplasm / cytosol", "Cytoplasm", CEMembranes.None, CEGroup.None, 66, Name: "Cytoplasm and cytosol",
                Line: "Not an organelle · a thin layer between the membrane and the vacuole", Compare: "Cytoplasm"),
            new("wall", "Cell wall", "Cell wall", CEMembranes.None, CEGroup.Outside, 97, Note: "outside the plasma membrane; cellulose fibres in a matrix",
                Compare: "Cell wall"),
            new("plasmodesma", "Plasmodesmata", "Plasmodesmata", CEMembranes.None, CEGroup.Outside, 64,
                Line: "Lined by the plasma membrane · channels through the wall", Compare: "Plasmodesmata"),
        },
        _ => new Spec[]
        {
            new("pm", "Plasma membrane", "Plasma membrane", CEMembranes.Single, CEGroup.Single, 95,
                Note: "it is the membrane: one phospholipid bilayer with proteins", Compare: "Plasma membrane"),
            new("nucleoid", "Nucleoid", "Nucleoid (DNA)", CEMembranes.None, CEGroup.None, 100, Note: "a region of the cytoplasm, not an organelle", Compare: "DNA"),
            new("plasmid", "Plasmids", "Plasmids", CEMembranes.None, CEGroup.None, 88, Note: "small loops of DNA in the cytoplasm",
                Found: new[] { "✗", "✗", "✓ (many species)" }),
            new("ribosome", "Ribosomes (70S)", "Ribosomes (70S)", CEMembranes.None, CEGroup.None, 92, Note: "two subunits (30S and 50S) of RNA and protein",
                Compare: "Ribosomes", Factor: 2),
            new("cytoplasm", "Cytoplasm", "Cytoplasm", CEMembranes.None, CEGroup.None, 70, Line: "Not an organelle · fills the cell inside the plasma membrane",
                Compare: "Cytoplasm"),
            new("capsule", "Capsule (some species; toggle, default on)", "Capsule", CEMembranes.None, CEGroup.Outside, 90, Name: "Capsule",
                Note: "a polysaccharide layer outside the wall", Compare: "Capsule"),
            new("outer", "Outer membrane (Gram-negative)", "Outer membrane", CEMembranes.Single, CEGroup.Outside, 86,
                Line: "It is a membrane · a second lipid bilayer, outside the thin wall", Found: new[] { "✗", "✗", "~ (Gram-negative bacteria only)" }),
            new("wall", "Cell wall (peptidoglycan)", "Cell wall (peptidoglycan)", CEMembranes.None, CEGroup.Outside, 94,
                Note: "a mesh of peptidoglycan outside the plasma membrane", Compare: "Cell wall"),
            new("flagellum", "Flagella", "Flagella", CEMembranes.None, CEGroup.Outside, 96, Note: "a protein filament on a rotary motor in the envelope",
                Compare: "Flagella", Factor: 3),
            new("fimbria", "Fimbriae (pili)", "Fimbriae (pili)", CEMembranes.None, CEGroup.Outside, 84, Note: "short protein hairs", Compare: "Fimbriae / pili",
                Factor: 3),
            new("sexpilus", "Sex pilus (Nice)", "Sex pilus", CEMembranes.None, CEGroup.Outside, 80, Name: "Sex pilus", Note: "a long protein tube",
                Compare: "Fimbriae / pili", Factor: 3),
        },
    };

    /// <summary>A §5.1 row by its Structure, in a cell's table.</summary>
    private static CESpecRow? Row(CEKind k, string structure)
    {
        var table = k switch { CEKind.Animal => CESpecText.Animal, CEKind.Plant => CESpecText.Plant, _ => CESpecText.Bacterium };
        return table.FirstOrDefault(r => r.Structure == structure);
    }

    /// <summary>The parts a plant row's "As animal" stands for: the animal's row for each.</summary>
    private static readonly Dictionary<string, string> AnimalRow = new()
    {
        ["nucleus"] = "Nucleus", ["nucleolus"] = "Nucleolus", ["pore"] = "Nuclear pores", ["chromatin"] = "Chromatin", ["rer"] = "Rough ER",
        ["ser"] = "Smooth ER", ["golgi"] = "Golgi apparatus", ["peroxisome"] = "Peroxisomes", ["ribosome"] = "Ribosomes (free and bound)",
        ["pm"] = "Plasma membrane", ["mito"] = "Mitochondria", ["vesicle"] = "Vesicles", ["cytoplasm"] = "Cytoplasm / cytosol",
        ["microtubule"] = "Cytoskeleton: microtubules", ["actin"] = "Cytoskeleton: actin (microfilaments)",
    };

    /// <summary>"—" and "as animal" read as nothing.</summary>
    private static string? Given(string? s)
    {
        if (s == null) return null;
        var t = s.Trim();
        if (t.Length == 0 || t == "—" || t.StartsWith("as animal", StringComparison.OrdinalIgnoreCase)) return null;
        return t;
    }

    /// <summary>A plant row's Function: its own words, or ("As animal; also …") the animal's followed by what it adds.</summary>
    private static string PlantFunction(string own, string? animal)
    {
        var lower = own.ToLowerInvariant();
        var asAnimal = lower.StartsWith("as animal", StringComparison.Ordinal);
        var asInAnimals = lower.StartsWith("as in animals", StringComparison.Ordinal);
        if (animal == null || !(asAnimal || asInAnimals)) return own;
        var rest = own[(asAnimal ? "As animal".Length : "As in animals".Length)..].Trim();
        if (rest.StartsWith(';') || rest.StartsWith('.')) rest = rest[1..].Trim();
        if (rest.Length == 0) return animal;
        if (rest.StartsWith('(') && rest.LastIndexOf(')') is var close and > 0)
            // ("As animal (80S in cytosol; 70S in chloroplasts and mitochondria).")
            return animal + " In plant cells: " + rest[1..close] + ".";
        return animal + " " + char.ToUpperInvariant(rest[0]) + rest[1..];
    }

    private static CEPart Make(Spec s, CEKind k)
    {
        // (its own row in its cell's table — a plant part may have none — and, for a plant part, the animal's row)
        var own = Row(k, s.Row);
        var animal = k == CEKind.Plant ? Row(CEKind.Animal, AnimalRow.GetValueOrDefault(s.Id, "")) : null;
        // (rows §5.1.2 gives for several parts at once, saying "As animal": each part has the animal's words)
        var shared = s.Row is "Nucleus, nucleolus, pores" or "Rough and smooth ER";
        var usesAnimal = k == CEKind.Plant && (own == null || shared || own.Function.StartsWith("as animal", StringComparison.OrdinalIgnoreCase));
        var function = own?.Function ?? animal?.Function ?? "";
        if (k == CEKind.Plant) function = own != null && !shared ? PlantFunction(own.Function, animal?.Function) : animal?.Function ?? function;
        var size = Given(own?.Size) ?? Given(animal?.Size) ?? "—";
        var modelText = Given(own?.Model) ?? Given(animal?.Model) ?? "—";
        // (the colour: the row's own, else the animal's)
        var colourRow = own is { Colours.Count: > 0 } ? own : animal ?? own;
        var first = colourRow?.Colours.FirstOrDefault();
        var colour = new BioHexPair(first?.Light ?? "#8e8e93", first?.Dark ?? "#aeaeb2");
        // (a mix-up: the row's own; "As animal" rows the animal's, but never the animal plasma membrane's "no wall")
        var mixup = Given(own?.Mixup);
        if (mixup == null && usesAnimal && s.Id != "pm") mixup = Given(animal?.Mixup);
        int? compareIndex = null;
        if (s.Compare != null)
            for (var i = 0; i < CESpecText.Compare.Count; i++)
                if (CESpecText.Compare[i][0] == s.Compare) { compareIndex = i; break; }
        IReadOnlyList<(CEMark, string)> found = s.Found != null ? s.Found.Select(CEMarks.Read).ToList()
            : compareIndex is { } ci ? Enumerable.Range(1, 3).Select(j => CEMarks.Read(CESpecText.Compare[ci][j])).ToList()
            : new List<(CEMark, string)> { (CEMark.No, ""), (CEMark.No, ""), (CEMark.No, "") };
        var name = s.Name ?? s.Row;
        if (name.EndsWith(" (Nice)", StringComparison.Ordinal)) name = name[..^7];
        var nice = new[] { own, animal }.Any(r => r != null && (r.Structure.EndsWith("(Nice)", StringComparison.Ordinal) || r.Model.Contains("(Nice)", StringComparison.Ordinal)));
        return new CEPart(s.Id, k, name, s.Label, function, s.Membranes, s.Note, s.Line, size, modelText, colour, colourRow?.Alpha, mixup, s.Group, found,
            compareIndex, s.Visible, nice, s.Priority, s.Factor, s.Inside);
    }

    /// <summary>A part's other colours (§5.1's colour cells naming several: the nucleus's nucleoplasm, the chloroplast's grana
    /// and stroma), by name.</summary>
    public static BioHexPair? Colour(CEKind k, string id, string name)
    {
        var rowName = id switch { "nucleus" => "Nucleus", "chloroplast" => "Chloroplasts", _ => null };
        if (rowName == null) return null;
        var r = (k == CEKind.Plant ? Row(CEKind.Plant, rowName) : null) ?? Row(CEKind.Animal, rowName);
        var c = r?.Colours.FirstOrDefault(x => x.Name == name);
        return c == null ? null : new BioHexPair(c.Light, c.Dark);
    }

    /// <summary>"In this model": what the model draws of a part, in the scale shown (§4.1 item 7: the size of the selected
    /// organelle is on its card).</summary>
    public static string InThisModel(CEPart p, bool trueScale)
    {
        var text = p.Model.Replace(" (Nice)", "", StringComparison.Ordinal);
        if (p.Factor <= 1) return text;
        var times = p.Factor.ToString(p.Factor % 1 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture);
        return trueScale ? $"{text}. Drawn at its true size here (teaching scale draws it {times}× larger)."
            : $"{text}. Drawn {times}× its true size in teaching scale.";
    }
}

// ---- "In this model" (§5.1.6) ----

public sealed partial class CEModel
{
    /// <summary>The card's "In this model:" line: how many the model has and how big they are drawn, at this scale (§4.1
    /// item 7: the selected organelle's size on its card), and how many the cutaway hides (the Mac's words, word for word).</summary>
    public string ModelLine(CEPart p)
    {
        var n = Placed.GetValueOrDefault(p.Id);
        var teaching = !Options.TrueScale;
        var kind = Options.Kind;
        string Enlarged(string trueText)
        {
            if (teaching && p.Factor > 1)
            {
                var times = p.Factor >= 5 ? $"about {(int)Math.Round(p.Factor, MidpointRounding.AwayFromZero)}" : Math.Abs(p.Factor - 1.5) < 1e-9 ? "1.5" : $"{(int)p.Factor}";
                return $" — drawn {times}× their true size (real: {trueText})";
            }
            return p.Factor > 1 ? " — at their true size" : "";
        }
        var free = Extra.GetValueOrDefault("vesicle.free");
        var golgi = Extra.GetValueOrDefault("vesicle.golgi");
        var s = (kind, p.Id) switch
        {
            (_, "pm") => kind == CEKind.Bacterium ? "A shell 0.01 µm thick round the cytoplasm" : "A shell 0.08 µm thick, translucent (a real bilayer is 4–5 nm: too thin to see at this size)",
            (_, "nucleus") => $"One, {(kind == CEKind.Animal ? "6.4" : "6.0")} µm across, its envelope's two membranes drawn 0.05 µm apart",
            (_, "pore") => $"{Count(n)} rings 0.18 µm across (real nuclei have thousands)",
            (_, "nucleolus") => $"One, about {(kind == CEKind.Animal ? "1.8" : "1.7")} µm across",
            (_, "chromatin") => $"{n} strands, drawn as tubes 0.12 µm thick",
            (_, "rer") => $"{n} curved sheets round the nucleus, 0.06 µm thick, studded with {Count(Extra.GetValueOrDefault("ribosome.er"))} ribosomes",
            (_, "ser") => $"A network of {Extra.GetValueOrDefault("ser.segments")} branching tubes 0.1 µm across",
            (CEKind.Animal, "golgi") => "One stack of 5 cisternae, about 1.6 µm across, beside the nucleus",
            (_, "golgi") => $"{n} small stacks of 4 cisternae, 0.8 µm across",
            (_, "mito") => $"{n} mitochondria, each 2.2 µm long and 0.9 µm wide",
            (_, "lysosome") => $"{n}, each 0.6 µm across",
            (_, "peroxisome") => $"{n}, each 0.5 µm across, with a crystalline core",
            (_, "vesicle") => (free > 0 ? $"{free} in the cytoplasm and {golgi} at the Golgi" : $"{golgi} at the Golgi stacks")
                + (teaching ? ", 0.12–0.3 µm across" : ", 0.08–0.2 µm across") + Enlarged("50–100 nm for transport vesicles"),
            (CEKind.Bacterium, "ribosome") => $"{Count(Extra.GetValueOrDefault("ribosome.free"))}, {(teaching ? "0.04" : "0.02")} µm across, outside the nucleoid"
                + Enlarged("about 20 nm; 10,000–70,000 in a cell"),
            (_, "ribosome") => $"{Count(Extra.GetValueOrDefault("ribosome.free"))} free and {Count(Extra.GetValueOrDefault("ribosome.er"))} on the rough ER, each {(teaching ? "0.12" : "0.03")} µm across"
                + Enlarged("25–30 nm; millions in a cell"),
            (_, "centrosome") => "Two centrioles at right angles, each 0.2 × 0.45 µm, in a cloud of protein",
            (CEKind.Plant, "microtubule") => $"{n} in a cortical array just inside the membrane" + Enlarged("25 nm wide"),
            (_, "microtubule") => $"{n} running from the centrosome to the cortex" + Enlarged("25 nm wide"),
            (_, "actin") => $"{n} short filaments {(kind == CEKind.Plant ? "in the cytoplasm" : "under the membrane")}" + Enlarged("about 7 nm wide"),
            (_, "intermediate") => $"{n} wavy filaments from the nucleus outward" + Enlarged("8–12 nm wide"),
            (_, "cytoplasm") => "The tinted faces where the cutaway cuts the cell",
            (CEKind.Animal, "flagellum") => "One, 10 µm long and 0.25 µm across",
            (_, "flagellum") => $"{n}, each 6 µm long, a helix 0.4 µm across with a hook and a basal body" + Enlarged("about 20 nm wide"),
            (_, "fimbria") => $"{n} hairs 0.4–0.9 µm long" + Enlarged("3–10 nm wide"),
            (_, "sexpilus") => "One, 2 µm long" + Enlarged("about 9 nm wide"),
            (_, "wall") => kind == CEKind.Plant ? "A wall 0.5 µm thick round the whole cell"
                : Options.GramPositive ? "A thick wall, 0.05 µm (Gram-positive)" : "A thin wall, 0.02 µm, under the outer membrane (Gram-negative)",
            (_, "plasmodesma") => $"{n} channels 0.12 µm across through the wall",
            (_, "vacuole") => "One, 37 × 19 × 19 µm: about half the cell's volume here, up to 80–90% in a mature cell",
            (_, "chloroplast") => $"{n} lenses 5.0 × 2.4 µm, each with 14 grana (real: 40–60) of 10 thylakoids",
            (_, "amyloplast") => $"{n}, each 2 µm long, with starch grains",
            (_, "capsule") => "A layer 0.15 µm thick, translucent",
            (_, "outer") => Options.GramPositive
                ? "None: a Gram-positive bacterium has no outer membrane, only its thick wall (turn off Gram-positive wall to see one)"
                : "A shell 0.012 µm thick, outside the thin wall",
            (_, "nucleoid") => "One chromosome drawn as a tangle of 600 steps in a region 1.3 × 0.64 µm",
            (_, "plasmid") => $"{n} small loops 0.14–0.2 µm across",
            _ => n > 0 ? $"{n}" : "",
        };
        var hidden = n - Shown.GetValueOrDefault(p.Id);
        if (Options.Cutaway && hidden > 0 && n > 1) s += $". {Count(hidden)} of them are in the part cut away";
        return s.Length == 0 ? "" : s + ".";
    }

    /// <summary>A count with its thousands grouped ("1,500").</summary>
    public static string Count(int x) => x.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>The cell's surface area, volume and their ratio, to three significant figures ("Surface area 2,410 µm² ·
    /// volume 3,540 µm³ · SA:V 0.681 per µm").</summary>
    public string SurfaceLine => $"Surface area {Sig3(Area)} µm² · volume {Sig3(Volume)} µm³ · SA:V {Sig3(Volume > 0 ? Area / Volume : 0)} per µm";

    /// <summary>A number to three significant figures, its thousands grouped ("2,410", "0.33").</summary>
    public static string Sig3(double x)
    {
        if (x == 0 || double.IsNaN(x) || double.IsInfinity(x)) return "0";
        var place = 2 - (int)Math.Floor(Math.Log10(Math.Abs(x)));
        // (rounded half-up, as every bio tool rounds: BioRound)
        var rounded = place >= 0 ? BioRound.Value(x, Math.Min(place, 15)) : BioRound.Value(x / Math.Pow(10, -place), 0) * Math.Pow(10, -place);
        return rounded.ToString("#,0.###############", CultureInfo.InvariantCulture);
    }
}

// ---- the Compare page (§4.1 item 8, §5.1.4) ----

/// <summary>A Compare row: the table's words (animal, plant, bacterium) and the cell and part a press opens (the first
/// cell that has it).</summary>
public sealed record CECompareRow(int Index, string Structure, IReadOnlyList<string> Cells, (CEKind Kind, string Part)? Open);

public static class CECompare
{
    /// <summary>§5.1.4 exactly, each row knowing which cell and part to open.</summary>
    public static readonly IReadOnlyList<CECompareRow> Rows = CESpecText.Compare.Select((r, i) => new CECompareRow(i, r[0], r[1..4], Target(r[0], r[1..4]))).ToList();

    /// <summary>The part a Compare row opens, in the first cell (animal, plant, bacterium) whose cell is not ✗.</summary>
    private static (CEKind, string)? Target(string structure, IReadOnlyList<string> cells)
    {
        var byRow = new Dictionary<string, Dictionary<CEKind, string>>
        {
            ["Plasma membrane"] = new() { [CEKind.Animal] = "pm", [CEKind.Plant] = "pm", [CEKind.Bacterium] = "pm" },
            ["Cytoplasm"] = new() { [CEKind.Animal] = "cytoplasm", [CEKind.Plant] = "cytoplasm", [CEKind.Bacterium] = "cytoplasm" },
            ["Ribosomes"] = new() { [CEKind.Animal] = "ribosome", [CEKind.Plant] = "ribosome", [CEKind.Bacterium] = "ribosome" },
            ["DNA"] = new() { [CEKind.Animal] = "chromatin", [CEKind.Plant] = "chromatin", [CEKind.Bacterium] = "nucleoid" },
            ["Nucleus (nuclear envelope)"] = new() { [CEKind.Animal] = "nucleus", [CEKind.Plant] = "nucleus" },
            ["Nucleolus"] = new() { [CEKind.Animal] = "nucleolus", [CEKind.Plant] = "nucleolus" },
            ["Endoplasmic reticulum"] = new() { [CEKind.Animal] = "rer", [CEKind.Plant] = "rer" },
            ["Golgi apparatus"] = new() { [CEKind.Animal] = "golgi", [CEKind.Plant] = "golgi" },
            ["Mitochondria"] = new() { [CEKind.Animal] = "mito", [CEKind.Plant] = "mito" },
            ["Chloroplasts"] = new() { [CEKind.Plant] = "chloroplast" },
            ["Lysosomes"] = new() { [CEKind.Animal] = "lysosome" },
            ["Peroxisomes"] = new() { [CEKind.Animal] = "peroxisome", [CEKind.Plant] = "peroxisome" },
            ["Central vacuole"] = new() { [CEKind.Plant] = "vacuole" },
            ["Vesicles"] = new() { [CEKind.Animal] = "vesicle", [CEKind.Plant] = "vesicle" },
            ["Cytoskeleton"] = new() { [CEKind.Animal] = "microtubule", [CEKind.Plant] = "microtubule" },
            ["Centrioles / centrosome"] = new() { [CEKind.Animal] = "centrosome" },
            ["Cell wall"] = new() { [CEKind.Plant] = "wall", [CEKind.Bacterium] = "wall" },
            ["Capsule"] = new() { [CEKind.Bacterium] = "capsule" },
            ["Flagella"] = new() { [CEKind.Animal] = "flagellum", [CEKind.Bacterium] = "flagellum" },
            ["Cilia"] = new() { [CEKind.Animal] = "flagellum" },
            ["Fimbriae / pili"] = new() { [CEKind.Bacterium] = "fimbria" },
            ["Plasmodesmata"] = new() { [CEKind.Plant] = "plasmodesma" },
        };
        if (!byRow.TryGetValue(structure, out var parts)) return null;
        for (var i = 0; i < 3; i++)
            if (CEMarks.Read(cells[i]).Mark != CEMark.No && parts.TryGetValue(CEKinds.All[i], out var p)) return (CEKinds.All[i], p);
        return null;
    }
}

// ---- about each cell (the right column with nothing selected) ----

/// <summary>A cell's "About this cell" (§4.1 UI layout): its size, what makes it distinctive, three things to try.</summary>
public sealed record CEAbout(string Size, string Distinctive, IReadOnlyList<string> TryThis)
{
    public static CEAbout Of(CEKind k) => k switch
    {
        CEKind.Animal => new CEAbout(
            "About 22 × 16 × 19 µm in this model. Real animal cells are typically 10–30 µm; a red blood cell is 7–8 µm across, a human egg about 120 µm.",
            "No cell wall, no chloroplasts and no central vacuole. It has centrioles and lysosomes, and its shape is held by the cytoskeleton, so animal cells can be round, flat or branched.",
            new[]
            {
                "Press the nucleus, then Look inside to see the pores, the nucleolus and the chromatin.",
                "Drag Explode to pull the organelles apart; the nucleus moves least because it is near the centre.",
                "Switch to True scale: ribosomes shrink to 25–30 nm and the filaments almost vanish.",
            }),
        CEKind.Plant => new CEAbout(
            "About 44 × 26 × 26 µm in this model. Leaf mesophyll cells are typically 20–80 µm long; plant cells are usually larger than animal cells.",
            "A cellulose cell wall outside the membrane, chloroplasts, and a central vacuole that fills most of the cell and keeps it firm by turgor pressure. It has mitochondria too, and no centrioles.",
            new[]
            {
                "Press a chloroplast and choose Look inside to see the grana, stacks of thylakoids.",
                "Turn the cell with the arrow keys to find the nucleus pressed against the wall, denting the vacuole.",
                "Drag Explode: the wall fades and the thin layer of cytoplasm round the vacuole comes apart.",
            }),
        _ => new CEAbout(
            "About 1 µm wide and 2 µm long, like E. coli: 10–20 times smaller than an animal cell. The Compare page shows the three at their true sizes.",
            "A prokaryote: no nucleus and no membrane-bound organelles. Its DNA is one circular chromosome in the nucleoid, its ribosomes are 70S, and its wall is peptidoglycan, not cellulose.",
            new[]
            {
                "Press the nucleoid: there is no membrane round the DNA.",
                "Turn on Gram-positive wall: a thick wall and no outer membrane.",
                "Look at the flagella: helical protein filaments turned by a motor, not 9 + 2 microtubules.",
            }),
    };
}

// ---- the quiz (§4.1 Nice: "Find the …" and "Name it") ----

public enum CEQuizMode
{
    Find,
    Name,
}

/// <summary>A quiz of ten questions on one cell's parts, for this session only (no storage).</summary>
public sealed class CEQuiz
{
    /// <summary>A question: the part, and for "Name it" four names, the right one among them.</summary>
    public sealed record Question(string Part, IReadOnlyList<string> Choices);

    public CEQuizMode Mode { get; }
    public CEKind Kind { get; }
    public IReadOnlyList<Question> Questions { get; }
    public int Index { get; private set; }
    public int Score { get; private set; }
    /// <summary>The parts missed (each once).</summary>
    public List<string> Misses { get; } = new();
    /// <summary>Whether the current question was missed already (a "Find" question is answered when the right part is found).</summary>
    public bool MissedThis { get; private set; }

    public bool Done => Index >= Questions.Count;
    public Question? Current => Done ? null : Questions[Index];

    /// <summary>Ten questions on the parts given (ids), shuffled by a generator (a fresh seed each time it is asked).</summary>
    public CEQuiz(CEQuizMode mode, CEKind kind, IReadOnlyList<string> parts, ref BioRandom g, int count = 10)
    {
        Mode = mode;
        Kind = kind;
        var pool = Shuffled(parts, ref g);
        while (pool.Count < count && parts.Count > 0) pool.AddRange(Shuffled(parts, ref g));
        var names = CEParts.All(kind).ToDictionary(p => p.Id, p => p.Name);
        var qs = new List<Question>();
        foreach (var id in pool.Take(count))
        {
            var others = Shuffled(parts.Where(p => p != id && names.GetValueOrDefault(p) != names.GetValueOrDefault(id)).ToList(), ref g)
                .Take(3).Select(p => names.GetValueOrDefault(p, p)).ToList();
            qs.Add(new Question(id, Shuffled(new[] { names.GetValueOrDefault(id, id) }.Concat(others).ToList(), ref g)));
        }
        Questions = qs;
    }

    /// <summary>Fisher–Yates with SplitMix64.</summary>
    private static List<T> Shuffled<T>(IReadOnlyList<T> items, ref BioRandom g)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = g.Int(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>An answer (a part's id for "Find", a name for "Name it"): right moves on; wrong is remembered as missed
    /// ("Name it" moves on after a wrong choice, having shown the right one).</summary>
    public bool Answer(string given)
    {
        if (Current is not { } q) return false;
        var right = Mode == CEQuizMode.Find ? given == q.Part : given == (CEParts.Part(Kind, q.Part)?.Name ?? q.Part);
        if (right)
        {
            if (!MissedThis) Score++;
            Index++;
            MissedThis = false;
        }
        else
        {
            if (!MissedThis) Misses.Add(q.Part);
            MissedThis = true;
            if (Mode == CEQuizMode.Name) { Index++; MissedThis = false; }
        }
        return right;
    }

    /// <summary>The current question skipped (counted as missed).</summary>
    public void Skip()
    {
        if (Current is not { } q) return;
        if (!MissedThis) Misses.Add(q.Part);
        Index++;
        MissedThis = false;
    }
}

// ---- what the tool keeps ----

/// <summary>One cell's state in the tool: the parts hidden in the parts list, the part selected (its card open), the part
/// isolated, and whether it is looked inside.</summary>
public sealed class CECellState
{
    public HashSet<string> Hidden { get; } = new();
    public string? Selected { get; set; }
    public string? Isolated { get; set; }
    public bool LookInside { get; set; }
}

/// <summary>
/// Cell Explorer's state, kept across its views (the tools' rule: a view subscribes to <see cref="Changed"/> on Loaded and
/// lets go on Unloaded): the options the three cells share (cutaway, labels, scale, explode, the bacterium's wall), each
/// cell's own state (<see cref="CECellState"/>), the quiz, and the membrane page's toggles and selection.
/// </summary>
public static class CellExplorerModel
{
    /// <summary>Something the views show changed: they draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();

    /// <summary>§4.1 item 4: on by default.</summary>
    public static bool Cutaway { get; set; } = true;
    /// <summary>§4.1 item 2: on by default.</summary>
    public static bool Labels { get; set; } = true;
    /// <summary>§4.1 item 7: teaching scale by default.</summary>
    public static bool TrueScale { get; set; }
    /// <summary>§4.1 item 5: 0–1.</summary>
    public static double Explode { get; set; }
    /// <summary>The bacterium's thick Gram-positive wall (no outer membrane) in place of the Gram-negative envelope.</summary>
    public static bool GramPositive { get; set; }

    private static readonly Dictionary<CEKind, CECellState> cells = new();

    /// <summary>A cell's state (its hidden parts start as §5.1's "(Nice)" parts that are off by default).</summary>
    public static CECellState Cell(CEKind k)
    {
        lock (cells)
        {
            if (cells.TryGetValue(k, out var s)) return s;
            s = new CECellState();
            foreach (var p in CEParts.All(k).Where(p => !p.DefaultVisible)) s.Hidden.Add(p.Id);
            cells[k] = s;
            return s;
        }
    }

    public static CEBuildOptions BuildOptions(CEKind k) => new(k, TrueScale, Cutaway, k == CEKind.Bacterium && GramPositive);

    /// <summary>What the view asks of the built cell now.</summary>
    public static CEView View(CEKind k, bool dark)
    {
        var s = Cell(k);
        return new CEView(Explode, s.Hidden.OrderBy(h => h, StringComparer.Ordinal).ToArray(), s.Isolated, s.Isolated != null && s.LookInside, dark);
    }

    /// <summary>A part selected (its card opened), or none; one outside what is isolated shows the whole cell again. Chosen
    /// from the parts list (<paramref name="follow"/>) while another part is isolated, the new part is isolated instead (seen
    /// inside only if it can be), as on the Mac; a click in the scene keeps the view (the nucleolus inside the nucleus).</summary>
    public static void Select(CEKind k, string? part, bool follow = false)
    {
        var s = Cell(k);
        if (s.Selected == part) return;
        s.Selected = part;
        if (follow && part != null && s.Isolated != null && s.Isolated != part)
        {
            s.Isolated = part;
            s.LookInside = s.LookInside && CEScene.CanLookInside(part);
            s.Hidden.Remove(part);
        }
        else if (part == null || (s.Isolated != null && !InView(s, part))) { s.Isolated = null; s.LookInside = false; }
        Tell();
    }

    /// <summary>Whether a part is drawn in the isolated view: the part itself, or inside the nucleus cut open (its pores,
    /// nucleolus and chromatin).</summary>
    private static bool InView(CECellState s, string part) =>
        s.Isolated == part || (s.LookInside && s.Isolated == "nucleus" && part is "pore" or "nucleolus" or "chromatin");

    /// <summary>Only this part shown, framed and enlarged (Isolate), or everything again (null).</summary>
    public static void Isolate(CEKind k, string? part, bool lookInside = false)
    {
        var s = Cell(k);
        s.Isolated = part;
        s.LookInside = part != null && lookInside;
        if (part != null) { s.Selected = part; s.Hidden.Remove(part); }
        Tell();
    }

    public static void SetVisible(CEKind k, string part, bool visible)
    {
        var s = Cell(k);
        if (visible ? !s.Hidden.Remove(part) : !s.Hidden.Add(part)) return;
        if (!visible && s.Selected == part) s.Selected = null;
        if (!visible && s.Isolated == part) { s.Isolated = null; s.LookInside = false; }
        Tell();
    }

    // ---- the quiz ----

    /// <summary>The quiz being taken (on the cell it was begun on), if any.</summary>
    public static CEQuiz? Quiz { get; private set; }
    /// <summary>What the last answer showed ("That is the Golgi apparatus."), shown under the question.</summary>
    public static string? QuizFeedback { get; set; }

    /// <summary>A quiz begun on a cell's parts as drawn (a fresh order each time, unless a seed is given: a picture's).</summary>
    public static void StartQuiz(CEKind k, CEQuizMode mode, IReadOnlyCollection<string> drawnParts, ulong? seed = null)
    {
        var parts = CEParts.All(k).Where(p => drawnParts.Contains(p.Id) && p.Id != "cytoplasm").Select(p => p.Id).ToList();
        if (parts.Count < 4) return;
        var g = new BioRandom(seed ?? (ulong)Environment.TickCount64 ^ k.Seed());
        Quiz = new CEQuiz(mode, k, parts, ref g);
        QuizFeedback = null;
        var s = Cell(k);
        s.Isolated = null;
        s.LookInside = false;
        s.Selected = null;
        Tell();
    }

    public static void EndQuiz()
    {
        Quiz = null;
        QuizFeedback = null;
        Tell();
    }

    // ---- the membrane page ----

    /// <summary>The membrane part selected (its card open), if any.</summary>
    public static string? MembraneSelected { get; set; }
    public static bool MembraneLabels { get; set; } = true;
    public static bool MembraneCholesterol { get; set; } = true;
    public static bool MembraneSugars { get; set; } = true;
    /// <summary>The channel and the carrier at work: the carrier rocking open to one side, then the other (§4.1 Nice).</summary>
    public static bool MembraneTransport { get; set; } = true;
    /// <summary>Osmosis: water filing down the aquaporin beside an arrow (§4.1 Nice).</summary>
    public static bool MembraneOsmosis { get; set; }
    /// <summary>POPC asked for over the membrane page (a launch's "popc"), taken when the page shows.</summary>
    public static bool MembranePopc { get; set; }
}

public static partial class BioSelfCheck
{
    /// <summary>Cell Explorer's own bio-check lines: the parts' words against §5.1, the Compare table, the quiz, each cell
    /// built from its seed (counts, the triangle budget, nothing outside the membrane, the same twice) and the membrane
    /// patch (CellExplorerChecks.cs).</summary>
    static partial void CellChecks(BioCheckReport r)
    {
        // (every part's Function line is §5.1's own, or for a plant "As animal" row the animal's)
        var animal = CEParts.All(CEKind.Animal);
        var plant = CEParts.All(CEKind.Plant);
        var bacterium = CEParts.All(CEKind.Bacterium);
        r.Check("cell.parts", $"{animal.Count},{plant.Count},{bacterium.Count}", animal.Count == 19 && plant.Count == 20 && bacterium.Count == 11);
        var words = CESpecText.Animal.Concat(CESpecText.Plant).Concat(CESpecText.Bacterium).Select(w => w.Function).ToHashSet();
        var animalWords = CESpecText.Animal.Select(w => w.Function).ToList();
        var verbatim = animal.Concat(bacterium).All(p => words.Contains(p.Function))
            && plant.All(p => words.Contains(p.Function) || animalWords.Any(a => p.Function.StartsWith(a, StringComparison.Ordinal)));
        r.Check("cell.functions-verbatim", verbatim ? "yes" : "no", verbatim);
        var mito = CEParts.Part(CEKind.Animal, "mito");
        r.Same("cell.mito-function", mito?.Function ?? "-",
            "Cellular respiration: makes most of the cell's ATP. Has its own DNA and 70S-type ribosomes (endosymbiosis).");
        r.Same("cell.mito-found", mito?.FoundInText ?? "-", "animal ✓ · plant ✓ · bacteria ✗");
        r.Same("cell.plant-golgi", CEParts.Part(CEKind.Plant, "golgi")?.Function ?? "-",
            "Modifies, sorts and packages proteins and lipids from the ER; ships them in vesicles (to the membrane, lysosomes or outside). Also makes wall polysaccharides and builds the cell plate.");
        r.Check("cell.plant-pm-mixup", CEParts.Part(CEKind.Plant, "pm")?.Mixup ?? "none", CEParts.Part(CEKind.Plant, "pm")?.Mixup == null);
        var sentences = new[] { "golgi", "rer", "mito" }.Select(id => CEParts.Part(CEKind.Animal, id)?.LabelInSentence).OfType<string>();
        r.Same("cell.names-in-sentences", string.Join(",", sentences), "Golgi apparatus,rough ER,mitochondria");
        r.Same("cell.nucleoid-structure", CEParts.Part(CEKind.Bacterium, "nucleoid")?.StructureText ?? "-", "No membrane · a region of the cytoplasm, not an organelle");
        var doubles = CEKinds.All.SelectMany(CEParts.All).Where(p => p.Membranes == CEMembranes.Double).Select(p => p.Id).ToHashSet();
        r.Check("cell.double-membranes", string.Join(",", doubles.OrderBy(d => d, StringComparer.Ordinal)),
            doubles.SetEquals(new[] { "nucleus", "mito", "chloroplast", "amyloplast" }));
        r.Same("cell.colours", $"{CEParts.Part(CEKind.Animal, "mito")?.Colour.Light}/{CEParts.Part(CEKind.Plant, "pm")?.Colour.Light}/{(CEParts.Part(CEKind.Bacterium, "capsule")?.Alpha ?? 0).ToString(CultureInfo.InvariantCulture)}",
            "#e2594a/#e8b04a/0.25");
        // (the Compare table, row for row, and each row's part)
        var rows = CECompare.Rows;
        r.Check("cell.compare", $"{rows.Count}", rows.Count == 23 && rows[0].Structure == "Plasma membrane" && rows[22].Cells.SequenceEqual(new[] { "10–30 µm", "10–100 µm", "1–5 µm" }));
        var opens = rows.Count(x => x.Open != null);
        r.Check("cell.compare-opens", $"{opens}", opens == 22 && rows.First(x => x.Structure == "Chloroplasts").Open?.Kind == CEKind.Plant
            && rows.First(x => x.Structure == "Capsule").Open?.Kind == CEKind.Bacterium);
        // (the quiz: ten questions, four choices each, the right one among them)
        var g = new BioRandom(7);
        var quiz = new CEQuiz(CEQuizMode.Name, CEKind.Animal, animal.Select(p => p.Id).ToList(), ref g);
        var fair = quiz.Questions.Count == 10 && quiz.Questions.All(q => q.Choices.Count == 4 && q.Choices.Contains(CEParts.Part(CEKind.Animal, q.Part)?.Name ?? "?"));
        quiz.Answer(CEParts.Part(CEKind.Animal, quiz.Questions[0].Part)?.Name ?? "");
        quiz.Answer("Not a part");
        r.Check("cell.quiz", $"{quiz.Score}/{quiz.Index}", fair && quiz.Score == 1 && quiz.Index == 2 && quiz.Misses.Count == 1);
        CellBuildChecks(r);
        CellMembraneChecks(r);
    }
}
