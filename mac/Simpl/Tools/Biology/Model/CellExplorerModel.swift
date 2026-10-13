import Foundation

// Cell Explorer's own model (spec §4.1, §5.1): the three cells' parts as the cards tell them — each part's Function line
// word for word from §5.1's tables (CellExplorerText.swift, copied out of the specification by a script), its structure
// line (double, single or no membrane), its real size, what the model draws of it and at what scale, where it is found
// (from the Compare table, §5.1.4) and its common mix-up — the parts list's groups, the Compare page's rows, each cell's
// "About" card, and the quiz. Foundation only, so the Mac app's workflow (Models) compiles and checks it with the rest of
// Biology/Model; the cells' geometry is CellExplorerBuild.swift's, the membrane page's CellMembraneModel.swift's.

/// The three cells.
enum CEKind: String, CaseIterable, Sendable {
    case animal, plant, bacterium

    /// §4.1 Rendering: each cell's seed, so it is the same picture on every launch (and on Windows).
    var seed: UInt64 {
        switch self {
        case .animal: return 0xC311
        case .plant: return 0x91A7
        case .bacterium: return 0xBAC7
        }
    }

    var title: String {
        switch self {
        case .animal: return "Animal cell"
        case .plant: return "Plant cell"
        case .bacterium: return "Bacterial cell"
        }
    }

    /// Its column in the Compare table (§5.1.4): 1 animal, 2 plant, 3 bacterium.
    var compareColumn: Int {
        switch self {
        case .animal: return 1
        case .plant: return 2
        case .bacterium: return 3
        }
    }

    /// "Found in"'s name for it.
    var foundName: String {
        switch self {
        case .animal: return "animal"
        case .plant: return "plant"
        case .bacterium: return "bacteria"
        }
    }
}

/// How many membranes wrap a part: the card's structure line (§5.1.6).
enum CEMembranes: Sendable {
    case double, single, none

    var line: String {
        switch self {
        case .double: return "Double membrane"
        case .single: return "Single membrane"
        case .none: return "No membrane"
        }
    }
}

/// The parts list's groups (§4.1 item 6), in its order.
enum CEGroup: Int, CaseIterable, Sendable {
    case double, single, none, outside

    var title: String {
        switch self {
        case .double: return "Membrane-bound (double membrane)"
        case .single: return "Membrane-bound (single membrane)"
        case .none: return "Not membrane-bound"
        case .outside: return "Outside the membrane"
        }
    }
}

/// A mark in the Compare table: present, absent, or partly (some cells or species; related proteins only).
enum CEMark: Sendable {
    case yes, no, partly

    var symbol: String {
        switch self {
        case .yes: return "✓"
        case .no: return "✗"
        case .partly: return "~"
        }
    }

    /// A Compare cell read: its mark, and its note (what follows the mark, if anything).
    static func read(_ cell: String) -> (mark: CEMark, note: String) {
        let t = cell.trimmingCharacters(in: .whitespaces)
        if t.hasPrefix("✓") { return (.yes, String(t.dropFirst()).trimmingCharacters(in: .whitespaces)) }
        if t.hasPrefix("✗") { return (.no, String(t.dropFirst()).trimmingCharacters(in: .whitespaces)) }
        if t.hasPrefix("~") { return (.partly, String(t.dropFirst()).trimmingCharacters(in: .whitespaces)) }
        // ("some species", "some cells (sperm), 9 + 2 microtubules", "10–30 µm")
        return (.partly, t)
    }
}

/// A part of a cell: an organelle type, as its card, its row in the parts list and its label tell it.
struct CEPart: Sendable, Identifiable {
    /// Its id in the cell ("mito"); the scene's objects are "organelle:<id>:<instance>".
    let id: String
    let kind: CEKind
    /// The card's title (§5.1's Structure column, tidied).
    let name: String
    /// Its label on the 3D view.
    let label: String
    /// The Function line, verbatim (§5.1.6); `**term**` marks the term to bold.
    let function: String
    let membranes: CEMembranes
    /// What the structure line adds ("the nuclear envelope, with nuclear pores"), or a line of its own (`structureLine`).
    let structureNote: String
    let structureLine: String?
    /// The real size and count (§5.1's column).
    let size: String
    let colour: BioHexPair
    /// Its translucency from §5.1, if it has one.
    let alpha: Double?
    /// The common mix-up, if §5.1 has one.
    let mixup: String?
    let group: CEGroup
    /// Where it is found: animal, plant, bacteria, each a mark and a note (from §5.1.4).
    let foundIn: [(mark: CEMark, note: String)]
    /// The Compare row it belongs to (its index in CESpecText.compare), if any.
    let compareRow: Int?
    /// Shown when the cell is first built (§5.1's "(Nice)" parts that are off by default are not).
    let defaultVisible: Bool
    /// §5.1's "(Nice)".
    let nice: Bool
    /// When more than 14 labels could show, the higher keep theirs (§3.5 item 6).
    let priority: Double
    /// Teaching scale's enlargement (drawn ÷ true size; 1 when drawn at its true size).
    let factor: Double
    /// "Look inside": a mitochondrion or chloroplast cut lengthwise, the nucleus cut open.
    let lookInside: Bool

    /// The name inside a sentence ("Find the golgi…" no: "Find the Golgi apparatus", "the rough ER", "the nucleus").
    var nameInSentence: String { CEPart.inSentence(name) }
    var labelInSentence: String { CEPart.inSentence(label) }

    /// A name with its first letter lowered, unless it begins a proper name or an abbreviation (Golgi, ER, DNA).
    static func inSentence(_ s: String) -> String {
        let first = s.split(separator: " ").first.map(String.init) ?? s
        if first.hasPrefix("Golgi") || first.count > 1 && first == first.uppercased() { return s }
        return s.prefix(1).lowercased() + s.dropFirst()
    }

    /// The card's structure line: "Double membrane · the nuclear envelope, with nuclear pores".
    var structureText: String {
        if let structureLine { return structureLine }
        return structureNote.isEmpty ? membranes.line : membranes.line + " · " + structureNote
    }

    /// "Found in: animal ✓ · plant ✓ · bacteria ✗" (§5.1.6).
    var foundInText: String {
        zip(CEKind.allCases, foundIn).map { "\($0.foundName) \($1.mark.symbol)" }.joined(separator: " · ")
    }

    /// The Compare table's notes for each cell, where it has one ("plant: 80S (+70S in chloroplasts and mitochondria)").
    var foundInNotes: [String] {
        zip(CEKind.allCases, foundIn).compactMap { k, f in f.note.isEmpty ? nil : "\(k.foundName): \(f.note)" }
    }
}

// MARK: - The catalogue

enum CEParts {
    /// A cell's parts in the parts list's order (by group, then as §5.1 lists them).
    static func all(_ k: CEKind) -> [CEPart] { catalogue[k] ?? [] }

    static func part(_ k: CEKind, _ id: String) -> CEPart? { all(k).first { $0.id == id } }

    private static let catalogue: [CEKind: [CEPart]] = {
        var out: [CEKind: [CEPart]] = [:]
        for k in CEKind.allCases {
            let parts = specs(k).map { make($0, k) }
            out[k] = CEGroup.allCases.flatMap { g in parts.filter { $0.group == g } }
        }
        return out
    }()

    /// What the catalogue knows of a part beyond §5.1's words: its id, which row it comes from, its label, its
    /// structure, its group, its Compare row (or its own Found in), and how it is drawn.
    private struct Spec {
        let id: String
        /// The §5.1 row it reads (the row's Structure, as written there).
        let row: String
        var name: String? = nil
        let label: String
        let membranes: CEMembranes
        var note = ""
        var line: String? = nil
        let group: CEGroup
        /// The Compare row's Structure; nil with `found`.
        var compare: String? = nil
        var found: [String]? = nil
        var visible = true
        var priority: Double
        var factor = 1.0
        var inside = false
        /// The Function line when §5.1 gives one row for several parts or a row's own needs completing.
        var function: String? = nil
    }

    private static func specs(_ k: CEKind) -> [Spec] {
        switch k {
        case .animal:
            return [
                Spec(id: "pm", row: "Plasma membrane", label: "Plasma membrane", membranes: .single,
                     note: "it is the membrane: one phospholipid bilayer with proteins", group: .single, compare: "Plasma membrane", priority: 96),
                Spec(id: "nucleus", row: "Nucleus", label: "Nucleus", membranes: .double, note: "the nuclear envelope, with nuclear pores",
                     group: .double, compare: "Nucleus (nuclear envelope)", priority: 100, inside: true),
                Spec(id: "mito", row: "Mitochondria", label: "Mitochondria", membranes: .double,
                     note: "a smooth outer membrane and a folded inner one (the cristae)", group: .double, compare: "Mitochondria", priority: 94, inside: true),
                Spec(id: "rer", row: "Rough ER", name: "Rough endoplasmic reticulum", label: "Rough ER", membranes: .single,
                     note: "flattened sacs (cisternae) studded with ribosomes", group: .single, compare: "Endoplasmic reticulum", priority: 92),
                Spec(id: "ser", row: "Smooth ER", name: "Smooth endoplasmic reticulum", label: "Smooth ER", membranes: .single,
                     note: "a network of tubules, no ribosomes", group: .single, compare: "Endoplasmic reticulum", priority: 88),
                Spec(id: "golgi", row: "Golgi apparatus", label: "Golgi apparatus", membranes: .single, note: "one around each flattened sac (cisterna)",
                     group: .single, compare: "Golgi apparatus", priority: 90),
                Spec(id: "lysosome", row: "Lysosomes", label: "Lysosomes", membranes: .single, group: .single, compare: "Lysosomes", priority: 86),
                Spec(id: "peroxisome", row: "Peroxisomes", label: "Peroxisomes", membranes: .single, group: .single, compare: "Peroxisomes", priority: 80),
                Spec(id: "vesicle", row: "Vesicles", label: "Vesicles", membranes: .single, group: .single, compare: "Vesicles", priority: 70, factor: 1.5),
                Spec(id: "nucleolus", row: "Nucleolus", label: "Nucleolus", membranes: .none, note: "a dense region inside the nucleus",
                     group: .none, compare: "Nucleolus", priority: 84),
                Spec(id: "chromatin", row: "Chromatin", label: "Chromatin", membranes: .none, note: "inside the nucleus",
                     group: .none, compare: "Nucleus (nuclear envelope)", priority: 60),
                Spec(id: "pore", row: "Nuclear pores", label: "Nuclear pores", membranes: .none,
                     note: "protein channels through both membranes of the envelope", group: .none, compare: "Nucleus (nuclear envelope)", priority: 55),
                Spec(id: "ribosome", row: "Ribosomes (free and bound)", name: "Ribosomes", label: "Ribosomes", membranes: .none,
                     note: "two subunits of RNA and protein", group: .none, compare: "Ribosomes", priority: 82, factor: 4),
                Spec(id: "centrosome", row: "Centrosome and centrioles", label: "Centrosome", membranes: .none,
                     note: "two centrioles, each 9 triplets of microtubules", group: .none, compare: "Centrioles / centrosome", priority: 78),
                Spec(id: "microtubule", row: "Cytoskeleton: microtubules", name: "Microtubules (cytoskeleton)", label: "Microtubules", membranes: .none,
                     note: "hollow tubes of tubulin", group: .none, compare: "Cytoskeleton", priority: 66, factor: 4),
                Spec(id: "actin", row: "Cytoskeleton: actin (microfilaments)", name: "Actin filaments (microfilaments)", label: "Actin filaments",
                     membranes: .none, note: "two twisted chains of actin", group: .none, compare: "Cytoskeleton", priority: 58, factor: 25.0 / 3.5),
                Spec(id: "intermediate", row: "Cytoskeleton: intermediate filaments", name: "Intermediate filaments", label: "Intermediate filaments",
                     membranes: .none, note: "rope-like protein fibres", group: .none, compare: "Cytoskeleton", priority: 52, factor: 7),
                Spec(id: "cytoplasm", row: "Cytoplasm / cytosol", name: "Cytoplasm and cytosol", label: "Cytoplasm", membranes: .none,
                     line: "Not an organelle · fills the cell inside the plasma membrane", group: .none, compare: "Cytoplasm", priority: 64),
                Spec(id: "flagellum", row: "Cilia / flagella (Nice)", name: "Cilia and flagella", label: "Flagellum", membranes: .none,
                     line: "Covered by the plasma membrane · 9 + 2 microtubules inside", group: .outside, compare: "Flagella", visible: false, priority: 50),
            ]
        case .plant:
            return [
                Spec(id: "nucleus", row: "Nucleus, nucleolus, pores", name: "Nucleus", label: "Nucleus", membranes: .double,
                     note: "the nuclear envelope, with nuclear pores", group: .double, compare: "Nucleus (nuclear envelope)", priority: 100, inside: true),
                Spec(id: "chloroplast", row: "Chloroplasts", label: "Chloroplasts", membranes: .double,
                     note: "an outer and an inner envelope, and thylakoid membranes inside", group: .double, compare: "Chloroplasts", priority: 98, inside: true),
                Spec(id: "mito", row: "Mitochondria", label: "Mitochondria", membranes: .double,
                     note: "a smooth outer membrane and a folded inner one (the cristae)", group: .double, compare: "Mitochondria", priority: 93, inside: true),
                Spec(id: "amyloplast", row: "Amyloplasts (Nice)", name: "Amyloplasts", label: "Amyloplasts", membranes: .double,
                     note: "plastids full of starch grains", group: .double, found: ["✗", "✓ (roots, tubers)", "✗"], visible: false, priority: 50),
                Spec(id: "pm", row: "Plasma membrane", label: "Plasma membrane", membranes: .single,
                     note: "it is the membrane: one phospholipid bilayer with proteins", group: .single, compare: "Plasma membrane", priority: 95),
                Spec(id: "vacuole", row: "Central vacuole", label: "Central vacuole", membranes: .single, note: "the tonoplast",
                     group: .single, compare: "Central vacuole", priority: 99),
                Spec(id: "rer", row: "Rough and smooth ER", name: "Rough endoplasmic reticulum", label: "Rough ER", membranes: .single,
                     note: "flattened sacs (cisternae) studded with ribosomes", group: .single, compare: "Endoplasmic reticulum", priority: 88),
                Spec(id: "ser", row: "Rough and smooth ER", name: "Smooth endoplasmic reticulum", label: "Smooth ER", membranes: .single,
                     note: "a network of tubules, no ribosomes", group: .single, compare: "Endoplasmic reticulum", priority: 80),
                Spec(id: "golgi", row: "Golgi (dictyosomes)", name: "Golgi apparatus (dictyosomes)", label: "Golgi (dictyosomes)", membranes: .single,
                     note: "one around each flattened sac (cisterna)", group: .single, compare: "Golgi apparatus", priority: 86),
                Spec(id: "peroxisome", row: "Peroxisomes", label: "Peroxisomes", membranes: .single, group: .single, compare: "Peroxisomes", priority: 76),
                Spec(id: "vesicle", row: "Vesicles", label: "Vesicles", membranes: .single, group: .single, compare: "Vesicles", priority: 60, factor: 1.5),
                Spec(id: "nucleolus", row: "Nucleolus", label: "Nucleolus", membranes: .none, note: "a dense region inside the nucleus",
                     group: .none, compare: "Nucleolus", priority: 74),
                Spec(id: "chromatin", row: "Chromatin", label: "Chromatin", membranes: .none, note: "inside the nucleus",
                     group: .none, compare: "Nucleus (nuclear envelope)", priority: 56),
                Spec(id: "pore", row: "Nuclear pores", label: "Nuclear pores", membranes: .none,
                     note: "protein channels through both membranes of the envelope", group: .none, compare: "Nucleus (nuclear envelope)", priority: 54),
                Spec(id: "ribosome", row: "Ribosomes", label: "Ribosomes", membranes: .none, note: "two subunits of RNA and protein",
                     group: .none, compare: "Ribosomes", priority: 78, factor: 4),
                Spec(id: "microtubule", row: "Cytoskeleton", name: "Microtubules (cytoskeleton)", label: "Microtubules", membranes: .none,
                     note: "hollow tubes of tubulin, in a cortical array under the membrane", group: .none, compare: "Cytoskeleton", priority: 62, factor: 4),
                Spec(id: "actin", row: "Cytoskeleton", name: "Actin filaments (microfilaments)", label: "Actin filaments", membranes: .none,
                     note: "two twisted chains of actin", group: .none, compare: "Cytoskeleton", priority: 57, factor: 25.0 / 3.5),
                Spec(id: "cytoplasm", row: "Cytoplasm / cytosol", name: "Cytoplasm and cytosol", label: "Cytoplasm", membranes: .none,
                     line: "Not an organelle · a thin layer between the membrane and the vacuole", group: .none, compare: "Cytoplasm", priority: 66),
                Spec(id: "wall", row: "Cell wall", label: "Cell wall", membranes: .none, note: "outside the plasma membrane; cellulose fibres in a matrix",
                     group: .outside, compare: "Cell wall", priority: 97),
                Spec(id: "plasmodesma", row: "Plasmodesmata", label: "Plasmodesmata", membranes: .none,
                     line: "Lined by the plasma membrane · channels through the wall", group: .outside, compare: "Plasmodesmata", priority: 64),
            ]
        case .bacterium:
            return [
                Spec(id: "pm", row: "Plasma membrane", label: "Plasma membrane", membranes: .single,
                     note: "it is the membrane: one phospholipid bilayer with proteins", group: .single, compare: "Plasma membrane", priority: 95),
                Spec(id: "nucleoid", row: "Nucleoid", label: "Nucleoid (DNA)", membranes: .none, note: "a region of the cytoplasm, not an organelle",
                     group: .none, compare: "DNA", priority: 100),
                Spec(id: "plasmid", row: "Plasmids", label: "Plasmids", membranes: .none, note: "small loops of DNA in the cytoplasm",
                     group: .none, found: ["✗", "✗", "✓ (many species)"], priority: 88),
                Spec(id: "ribosome", row: "Ribosomes (70S)", label: "Ribosomes (70S)", membranes: .none, note: "two subunits (30S and 50S) of RNA and protein",
                     group: .none, compare: "Ribosomes", priority: 92, factor: 2),
                Spec(id: "cytoplasm", row: "Cytoplasm", label: "Cytoplasm", membranes: .none,
                     line: "Not an organelle · fills the cell inside the plasma membrane", group: .none, compare: "Cytoplasm", priority: 70),
                Spec(id: "capsule", row: "Capsule (some species; toggle, default on)", name: "Capsule", label: "Capsule", membranes: .none,
                     note: "a polysaccharide layer outside the wall", group: .outside, compare: "Capsule", priority: 90),
                Spec(id: "outer", row: "Outer membrane (Gram-negative)", label: "Outer membrane", membranes: .single,
                     line: "It is a membrane · a second lipid bilayer, outside the thin wall", group: .outside,
                     found: ["✗", "✗", "~ (Gram-negative bacteria only)"], priority: 86),
                Spec(id: "wall", row: "Cell wall (peptidoglycan)", label: "Cell wall (peptidoglycan)", membranes: .none,
                     note: "a mesh of peptidoglycan outside the plasma membrane", group: .outside, compare: "Cell wall", priority: 94),
                Spec(id: "flagellum", row: "Flagella", label: "Flagella", membranes: .none, note: "a protein filament on a rotary motor in the envelope",
                     group: .outside, compare: "Flagella", priority: 96, factor: 3),
                Spec(id: "fimbria", row: "Fimbriae (pili)", label: "Fimbriae (pili)", membranes: .none, note: "short protein hairs",
                     group: .outside, compare: "Fimbriae / pili", priority: 84, factor: 3),
                Spec(id: "sexpilus", row: "Sex pilus (Nice)", name: "Sex pilus", label: "Sex pilus", membranes: .none, note: "a long protein tube",
                     group: .outside, compare: "Fimbriae / pili", priority: 80, factor: 3),
            ]
        }
    }

    /// A §5.1 row by its Structure, in a cell's table.
    private static func row(_ k: CEKind, _ structure: String) -> CESpecRow? {
        let table: [CESpecRow]
        switch k {
        case .animal: table = CESpecText.animal
        case .plant: table = CESpecText.plant
        case .bacterium: table = CESpecText.bacterium
        }
        return table.first { $0.structure == structure }
    }

    /// The parts a plant row's "As animal" stands for: the animal's row for each.
    private static let animalRow: [String: String] = [
        "nucleus": "Nucleus", "nucleolus": "Nucleolus", "pore": "Nuclear pores", "chromatin": "Chromatin", "rer": "Rough ER",
        "ser": "Smooth ER", "golgi": "Golgi apparatus", "peroxisome": "Peroxisomes", "ribosome": "Ribosomes (free and bound)",
        "pm": "Plasma membrane", "mito": "Mitochondria", "vesicle": "Vesicles", "cytoplasm": "Cytoplasm / cytosol",
        "microtubule": "Cytoskeleton: microtubules", "actin": "Cytoskeleton: actin (microfilaments)",
    ]

    /// "—" and "as animal" read as nothing.
    private static func given(_ s: String?) -> String? {
        guard let s else { return nil }
        let t = s.trimmingCharacters(in: .whitespaces)
        if t.isEmpty || t == "—" || t.lowercased().hasPrefix("as animal") { return nil }
        return t
    }

    /// A plant row's Function: its own words, or ("As animal; also …") the animal's followed by what it adds.
    private static func plantFunction(_ own: String, animal: String?) -> String {
        let lower = own.lowercased()
        guard let animal, lower.hasPrefix("as animal") || lower.hasPrefix("as in animals") else { return own }
        var rest = String(own.dropFirst(lower.hasPrefix("as animal") ? "As animal".count : "As in animals".count))
            .trimmingCharacters(in: .whitespaces)
        if rest.hasPrefix(";") || rest.hasPrefix(".") { rest = String(rest.dropFirst()).trimmingCharacters(in: .whitespaces) }
        if rest.isEmpty { return animal }
        if rest.hasPrefix("("), let close = rest.lastIndex(of: ")") {
            // ("As animal (80S in cytosol; 70S in chloroplasts and mitochondria).")
            return animal + " In plant cells: " + rest[rest.index(after: rest.startIndex)..<close] + "."
        }
        return animal + " " + rest.prefix(1).uppercased() + rest.dropFirst()
    }

    private static func make(_ s: Spec, _ k: CEKind) -> CEPart {
        // (its own row in its cell's table — a plant part may have none — and, for a plant part, the animal's row)
        let own = row(k, s.row)
        let animal = k == .plant ? row(.animal, animalRow[s.id] ?? "") : nil
        // (rows §5.1.2 gives for several parts at once, saying "As animal": each part has the animal's words)
        let shared = s.row == "Nucleus, nucleolus, pores" || s.row == "Rough and smooth ER"
        let usesAnimal = k == .plant && (own == nil || shared || own?.function.lowercased().hasPrefix("as animal") == true)
        var function = s.function ?? own?.function ?? animal?.function ?? ""
        if k == .plant {
            if let own, !shared {
                function = plantFunction(own.function, animal: animal?.function)
            } else {
                function = animal?.function ?? function
            }
        }
        let size = given(own?.size) ?? given(animal?.size) ?? "—"
        // (the colour: the row's own, else the animal's)
        let colourRow = (own?.colours.isEmpty == false) ? own : (animal ?? own)
        let first = colourRow?.colours.first
        let colour = BioHexPair(first?.light ?? "#8e8e93", first?.dark ?? "#aeaeb2")
        let alpha = colourRow?.alpha
        // (a mix-up: the row's own; "As animal" rows the animal's, but never the animal plasma membrane's "no wall")
        var mixup = given(own?.mixup)
        if mixup == nil && usesAnimal && s.id != "pm" { mixup = given(animal?.mixup) }
        let compareIndex = s.compare.flatMap { c in CESpecText.compare.firstIndex { $0[0] == c } }
        let found: [(mark: CEMark, note: String)]
        if let f = s.found {
            found = f.map { CEMark.read($0) }
        } else if let i = compareIndex {
            found = (1...3).map { CEMark.read(CESpecText.compare[i][$0]) }
        } else {
            found = [(CEMark.no, ""), (CEMark.no, ""), (CEMark.no, "")]
        }
        var name = s.name ?? s.row
        if name.hasSuffix(" (Nice)") { name = String(name.dropLast(7)) }
        return CEPart(id: s.id, kind: k, name: name, label: s.label, function: function, membranes: s.membranes, structureNote: s.note,
                      structureLine: s.line, size: size, colour: colour, alpha: alpha, mixup: mixup, group: s.group, foundIn: found,
                      compareRow: compareIndex, defaultVisible: s.visible,
                      nice: [own, animal].contains { r in r.map { $0.structure.hasSuffix("(Nice)") || $0.model.contains("(Nice)") } ?? false },
                      priority: s.priority, factor: s.factor, lookInside: s.inside)
    }

    /// A part's other colours (§5.1's colour cells naming several: the nucleus's nucleoplasm, the chloroplast's grana
    /// and stroma), by name.
    static func colour(_ k: CEKind, _ id: String, _ name: String) -> BioHexPair? {
        let rowName: String
        switch id {
        case "nucleus": rowName = "Nucleus"
        case "chloroplast": rowName = "Chloroplasts"
        default: return nil
        }
        let r = (k == .plant ? row(.plant, rowName) : nil) ?? row(.animal, rowName)
        guard let c = r?.colours.first(where: { $0.name == name }) else { return nil }
        return BioHexPair(c.light, c.dark)
    }
}

// MARK: - "In this model" (§5.1.6)

extension CEModel {
    /// The card's "In this model:" line: how many the model has and how big they are drawn, at this scale (§4.1 item 7:
    /// the selected organelle's size on its card), and how many the cutaway hides.
    func modelLine(_ p: CEPart) -> String {
        let n = placed[p.id] ?? 0
        let teaching = !options.trueScale
        func num(_ x: Int) -> String { CEModel.count(x) }
        func enlarged(_ trueText: String) -> String {
            teaching && p.factor > 1 ? " — drawn \(p.factor >= 5 ? "about \(Int(p.factor.rounded()))" : (p.factor == 1.5 ? "1.5" : "\(Int(p.factor))"))× their true size (real: \(trueText))"
                : (p.factor > 1 ? " — at their true size" : "")
        }
        var s: String
        switch (options.kind, p.id) {
        case (_, "pm"):
            s = options.kind == .bacterium ? "A shell 0.01 µm thick round the cytoplasm" : "A shell 0.08 µm thick, translucent (a real bilayer is 4–5 nm: too thin to see at this size)"
        case (_, "nucleus"): s = "One, \(options.kind == .animal ? "6.4" : "6.0") µm across, its envelope's two membranes drawn 0.05 µm apart"
        case (_, "pore"): s = "\(num(n)) rings 0.18 µm across (real nuclei have thousands)"
        case (_, "nucleolus"): s = "One, about \(options.kind == .animal ? "1.8" : "1.7") µm across"
        case (_, "chromatin"): s = "\(n) strands, drawn as tubes 0.12 µm thick"
        case (_, "rer"): s = "\(n) curved sheets round the nucleus, 0.06 µm thick, studded with \(num(extra["ribosome.er"] ?? 0)) ribosomes"
        case (_, "ser"): s = "A network of \(extra["ser.segments"] ?? 0) branching tubes 0.1 µm across"
        case (.animal, "golgi"): s = "One stack of 5 cisternae, about 1.6 µm across, beside the nucleus"
        case (_, "golgi"): s = "\(n) small stacks of 4 cisternae, 0.8 µm across"
        case (_, "mito"): s = "\(n) mitochondria, each 2.2 µm long and 0.9 µm wide"
        case (_, "lysosome"): s = "\(n), each 0.6 µm across"
        case (_, "peroxisome"): s = "\(n), each 0.5 µm across, with a crystalline core"
        case (_, "vesicle"):
            let free = extra["vesicle.free"] ?? 0, golgi = extra["vesicle.golgi"] ?? 0
            s = (free > 0 ? "\(free) in the cytoplasm and \(golgi) at the Golgi" : "\(golgi) at the Golgi stacks")
                + (teaching ? ", 0.12–0.3 µm across" : ", 0.08–0.2 µm across") + enlarged("50–100 nm for transport vesicles")
        case (.bacterium, "ribosome"):
            s = "\(num(extra["ribosome.free"] ?? 0)), \(teaching ? "0.04" : "0.02") µm across, outside the nucleoid" + enlarged("about 20 nm; 10,000–70,000 in a cell")
        case (_, "ribosome"):
            s = "\(num(extra["ribosome.free"] ?? 0)) free and \(num(extra["ribosome.er"] ?? 0)) on the rough ER, each \(teaching ? "0.12" : "0.03") µm across"
                + enlarged("25–30 nm; millions in a cell")
        case (_, "centrosome"): s = "Two centrioles at right angles, each 0.2 × 0.45 µm, in a cloud of protein"
        case (.plant, "microtubule"): s = "\(n) in a cortical array just inside the membrane" + enlarged("25 nm wide")
        case (_, "microtubule"): s = "\(n) running from the centrosome to the cortex" + enlarged("25 nm wide")
        case (_, "actin"): s = "\(n) short filaments \(options.kind == .plant ? "in the cytoplasm" : "under the membrane")" + enlarged("about 7 nm wide")
        case (_, "intermediate"): s = "\(n) wavy filaments from the nucleus outward" + enlarged("8–12 nm wide")
        case (_, "cytoplasm"): s = "The tinted faces where the cutaway cuts the cell"
        case (.animal, "flagellum"): s = "One, 10 µm long and 0.25 µm across"
        case (_, "flagellum"): s = "\(n), each 6 µm long, a helix 0.4 µm across with a hook and a basal body" + enlarged("about 20 nm wide")
        case (_, "fimbria"): s = "\(n) hairs 0.4–0.9 µm long" + enlarged("3–10 nm wide")
        case (_, "sexpilus"): s = "One, 2 µm long" + enlarged("about 9 nm wide")
        case (_, "wall"): s = options.kind == .plant ? "A wall 0.5 µm thick round the whole cell"
            : (options.gramPositive ? "A thick wall, 0.05 µm (Gram-positive)" : "A thin wall, 0.02 µm, under the outer membrane (Gram-negative)")
        case (_, "plasmodesma"): s = "\(n) channels 0.12 µm across through the wall"
        case (_, "vacuole"): s = "One, 37 × 19 × 19 µm: about half the cell's volume here, up to 80–90% in a mature cell"
        case (_, "chloroplast"): s = "\(n) lenses 5.0 × 2.4 µm, each with 14 grana (real: 40–60) of 10 thylakoids"
        case (_, "amyloplast"): s = "\(n), each 2 µm long, with starch grains"
        case (_, "capsule"): s = "A layer 0.15 µm thick, translucent"
        case (_, "outer"): s = "A shell 0.012 µm thick, outside the thin wall"
        case (_, "nucleoid"): s = "One chromosome drawn as a tangle of 600 steps in a region 1.3 × 0.64 µm"
        case (_, "plasmid"): s = "\(n) small loops 0.14–0.2 µm across"
        default: s = n > 0 ? "\(n)" : ""
        }
        let hidden = n - (shown[p.id] ?? 0)
        if options.cutaway && hidden > 0 && n > 1 { s += ". \(num(hidden)) of them are in the part cut away" }
        return s.isEmpty ? "" : s + "."
    }

    /// A count with its thousands grouped as the Mac's region writes them ("1,500").
    static func count(_ x: Int) -> String {
        let f = NumberFormatter()
        f.numberStyle = .decimal
        return f.string(from: NSNumber(value: x)) ?? "\(x)"
    }

    /// The cell's surface area, volume and their ratio, as a line ("Surface area 1,100 µm² · volume 3,500 µm³ ·
    /// SA:V 0.32 per µm").
    var surfaceLine: String {
        func fmt(_ x: Double) -> String {
            let f = NumberFormatter()
            f.numberStyle = .decimal
            f.maximumSignificantDigits = 3
            f.usesSignificantDigits = true
            return f.string(from: NSNumber(value: x)) ?? String(format: "%.3g", x)
        }
        return "Surface area \(fmt(area)) µm² · volume \(fmt(volume)) µm³ · SA:V \(fmt(volume > 0 ? area / volume : 0)) per µm"
    }
}

// MARK: - The Compare page (§4.1 item 8, §5.1.4)

struct CECompareRow: Sendable, Identifiable {
    let id: Int
    let structure: String
    /// Animal, plant, bacterium: the table's words.
    let cells: [String]
    /// The cell to open and the part to select when the row is pressed (the first cell that has it).
    let open: (kind: CEKind, part: String)?
}

enum CECompare {
    /// §5.1.4 exactly, each row knowing which cell and part to open.
    static let rows: [CECompareRow] = CESpecText.compare.enumerated().map { i, r in
        CECompareRow(id: i, structure: r[0], cells: Array(r[1...3]), open: target(r[0], cells: Array(r[1...3])))
    }

    /// The part a Compare row opens, in the first cell (animal, plant, bacterium) whose cell is not ✗.
    private static func target(_ structure: String, cells: [String]) -> (kind: CEKind, part: String)? {
        let byRow: [String: [CEKind: String]] = [
            "Plasma membrane": [.animal: "pm", .plant: "pm", .bacterium: "pm"],
            "Cytoplasm": [.animal: "cytoplasm", .plant: "cytoplasm", .bacterium: "cytoplasm"],
            "Ribosomes": [.animal: "ribosome", .plant: "ribosome", .bacterium: "ribosome"],
            "DNA": [.animal: "chromatin", .plant: "chromatin", .bacterium: "nucleoid"],
            "Nucleus (nuclear envelope)": [.animal: "nucleus", .plant: "nucleus"],
            "Nucleolus": [.animal: "nucleolus", .plant: "nucleolus"],
            "Endoplasmic reticulum": [.animal: "rer", .plant: "rer"],
            "Golgi apparatus": [.animal: "golgi", .plant: "golgi"],
            "Mitochondria": [.animal: "mito", .plant: "mito"],
            "Chloroplasts": [.plant: "chloroplast"],
            "Lysosomes": [.animal: "lysosome"],
            "Peroxisomes": [.animal: "peroxisome", .plant: "peroxisome"],
            "Central vacuole": [.plant: "vacuole"],
            "Vesicles": [.animal: "vesicle", .plant: "vesicle"],
            "Cytoskeleton": [.animal: "microtubule", .plant: "microtubule"],
            "Centrioles / centrosome": [.animal: "centrosome"],
            "Cell wall": [.plant: "wall", .bacterium: "wall"],
            "Capsule": [.bacterium: "capsule"],
            "Flagella": [.animal: "flagellum", .bacterium: "flagellum"],
            "Cilia": [.animal: "flagellum"],
            "Fimbriae / pili": [.bacterium: "fimbria"],
            "Plasmodesmata": [.plant: "plasmodesma"],
        ]
        guard let parts = byRow[structure] else { return nil }
        for (k, cell) in zip(CEKind.allCases, cells) where CEMark.read(cell).mark != .no {
            if let p = parts[k] { return (k, p) }
        }
        return nil
    }
}

// MARK: - About each cell (the right column with nothing selected)

struct CEAbout: Sendable {
    let size: String
    let distinctive: String
    let tryThis: [String]

    static func of(_ k: CEKind) -> CEAbout {
        switch k {
        case .animal:
            return CEAbout(size: "About 22 × 16 × 19 µm in this model. Real animal cells are typically 10–30 µm; a red blood cell is 7–8 µm across, a human egg about 120 µm.",
                           distinctive: "No cell wall, no chloroplasts and no central vacuole. It has centrioles and lysosomes, and its shape is held by the cytoskeleton, so animal cells can be round, flat or branched.",
                           tryThis: ["Press the nucleus, then Look inside to see the pores, the nucleolus and the chromatin.",
                                     "Drag Explode to pull the organelles apart; the nucleus moves least because it is near the centre.",
                                     "Switch to True scale: ribosomes shrink to 25–30 nm and the filaments almost vanish."])
        case .plant:
            return CEAbout(size: "About 44 × 26 × 26 µm in this model. Leaf mesophyll cells are typically 20–80 µm long; plant cells are usually larger than animal cells.",
                           distinctive: "A cellulose cell wall outside the membrane, chloroplasts, and a central vacuole that fills most of the cell and keeps it firm by turgor pressure. It has mitochondria too, and no centrioles.",
                           tryThis: ["Press a chloroplast and choose Look inside to see the grana, stacks of thylakoids.",
                                     "Turn the cell with the arrow keys to find the nucleus pressed against the wall, denting the vacuole.",
                                     "Drag Explode: the wall fades and the thin layer of cytoplasm round the vacuole comes apart."])
        case .bacterium:
            return CEAbout(size: "About 1 µm wide and 2 µm long, like E. coli: 10–20 times smaller than an animal cell. The Compare page shows the three at their true sizes.",
                           distinctive: "A prokaryote: no nucleus and no membrane-bound organelles. Its DNA is one circular chromosome in the nucleoid, its ribosomes are 70S, and its wall is peptidoglycan, not cellulose.",
                           tryThis: ["Press the nucleoid: there is no membrane round the DNA.",
                                     "Turn on Gram-positive wall: a thick wall and no outer membrane.",
                                     "Look at the flagella: helical protein filaments turned by a motor, not 9 + 2 microtubules."])
        }
    }
}

// MARK: - The quiz (§4.1 Nice: "Find the …" and "Name it")

/// A quiz of ten questions on one cell's parts, for this session only (no storage).
struct CEQuiz: Sendable {
    enum Mode: String, Sendable { case find, name }

    struct Question: Sendable {
        let part: String
        /// "Name it": four names, the right one among them.
        let choices: [String]
    }

    let mode: Mode
    let kind: CEKind
    let questions: [Question]
    private(set) var index = 0
    private(set) var score = 0
    /// The parts missed (each once).
    private(set) var misses: [String] = []
    /// Whether the current question was missed already (a "Find" question is answered when the right part is found).
    private(set) var missedThis = false

    var done: Bool { index >= questions.count }
    var current: Question? { done ? nil : questions[index] }

    /// Ten questions on the parts given (ids), with a generator — a fresh one each time.
    init<G: RandomNumberGenerator>(mode: Mode, kind: CEKind, parts: [String], count: Int = 10, using g: inout G) {
        self.mode = mode
        self.kind = kind
        var pool = parts.shuffled(using: &g)
        while pool.count < count && !parts.isEmpty { pool += parts.shuffled(using: &g) }
        let names = Dictionary(uniqueKeysWithValues: CEParts.all(kind).map { ($0.id, $0.name) })
        questions = pool.prefix(count).map { id in
            let others = parts.filter { $0 != id && names[$0] != names[id] }.shuffled(using: &g).prefix(3).compactMap { names[$0] }
            return Question(part: id, choices: ([names[id] ?? id] + others).shuffled(using: &g))
        }
    }

    /// An answer (a part's id for "Find", a name for "Name it"): right moves on; wrong is remembered as missed.
    @discardableResult
    mutating func answer(_ given: String) -> Bool {
        guard let q = current else { return false }
        let right: Bool
        switch mode {
        case .find: right = given == q.part
        case .name: right = given == (CEParts.part(kind, q.part)?.name ?? q.part)
        }
        if right {
            if !missedThis { score += 1 }
            index += 1
            missedThis = false
        } else {
            if !missedThis { misses.append(q.part) }
            missedThis = true
            // ("Name it" moves on after a wrong choice, having shown the right one)
            if mode == .name { index += 1; missedThis = false }
        }
        return right
    }

    /// Skips the current question (counted as missed).
    mutating func skip() {
        guard let q = current else { return }
        if !missedThis { misses.append(q.part) }
        index += 1
        missedThis = false
    }
}

// MARK: - Self-check

extension BioSelfCheck {
    /// Cell Explorer's own `bio-check` lines: the parts' words against §5.1, the Compare table, each cell built from its
    /// seed (counts, the triangle budget, nothing outside the membrane, the same twice), and the membrane patch.
    static func cellChecks(_ r: inout BioCheckReport) {
        // (every part's Function line is §5.1's own, or for a plant "As animal" row the animal's)
        let animal = CEParts.all(.animal), plant = CEParts.all(.plant), bacterium = CEParts.all(.bacterium)
        r.check("cell.parts", "\(animal.count),\(plant.count),\(bacterium.count)", animal.count == 19 && plant.count == 20 && bacterium.count == 11)
        let words = Set((CESpecText.animal + CESpecText.plant + CESpecText.bacterium).map(\.function))
        let animalWords = Set(CESpecText.animal.map(\.function))
        let verbatim = (animal + bacterium).allSatisfy { words.contains($0.function) }
            && plant.allSatisfy { p in words.contains(p.function) || animalWords.contains { p.function.hasPrefix($0) } }
        r.check("cell.functions-verbatim", verbatim ? "yes" : "no", verbatim)
        let mito = CEParts.part(.animal, "mito")
        r.same("cell.mito-function", mito?.function ?? "-",
               "Cellular respiration: makes most of the cell's ATP. Has its own DNA and 70S-type ribosomes (endosymbiosis).")
        r.same("cell.mito-found", mito?.foundInText ?? "-", "animal ✓ · plant ✓ · bacteria ✗")
        r.same("cell.plant-golgi", CEParts.part(.plant, "golgi")?.function ?? "-",
               "Modifies, sorts and packages proteins and lipids from the ER; ships them in vesicles (to the membrane, lysosomes or outside). Also makes wall polysaccharides and builds the cell plate.")
        r.check("cell.plant-pm-mixup", CEParts.part(.plant, "pm")?.mixup ?? "none", CEParts.part(.plant, "pm")?.mixup == nil)
        let sentences = ["golgi", "rer", "mito"].compactMap { CEParts.part(.animal, $0)?.labelInSentence }
        r.same("cell.names-in-sentences", sentences.joined(separator: ","), "Golgi apparatus,rough ER,mitochondria")
        r.same("cell.nucleoid-structure", CEParts.part(.bacterium, "nucleoid")?.structureText ?? "-", "No membrane · a region of the cytoplasm, not an organelle")
        let doubles = CEKind.allCases.flatMap { CEParts.all($0) }.filter { $0.membranes == .double }.map(\.id)
        r.check("cell.double-membranes", Set(doubles).sorted().joined(separator: ","), Set(doubles) == ["nucleus", "mito", "chloroplast", "amyloplast"])
        r.same("cell.colours", "\(CEParts.part(.animal, "mito")?.colour.light ?? "-")/\(CEParts.part(.plant, "pm")?.colour.light ?? "-")/\(CEParts.part(.bacterium, "capsule")?.alpha ?? 0)",
               "#e2594a/#e8b04a/0.25")
        // (the Compare table, row for row, and each row's part)
        r.check("cell.compare", "\(CECompare.rows.count)", CECompare.rows.count == 23 && CECompare.rows[0].structure == "Plasma membrane"
                && CECompare.rows[22].cells == ["10–30 µm", "10–100 µm", "1–5 µm"])
        let opens = CECompare.rows.filter { $0.open != nil }.count
        r.check("cell.compare-opens", "\(opens)", opens == 22 && CECompare.rows.first { $0.structure == "Chloroplasts" }?.open?.kind == .plant
                && CECompare.rows.first { $0.structure == "Capsule" }?.open?.kind == .bacterium)
        // (the quiz: ten questions, four choices each, the right one among them)
        var g = CEQuizRandom(seed: 7)
        var quiz = CEQuiz(mode: .name, kind: .animal, parts: animal.map(\.id), using: &g)
        let fair = quiz.questions.count == 10 && quiz.questions.allSatisfy { q in q.choices.count == 4 && q.choices.contains(CEParts.part(.animal, q.part)?.name ?? "?") }
        quiz.answer(CEParts.part(.animal, quiz.questions[0].part)?.name ?? "")
        quiz.answer("Not a part")
        r.check("cell.quiz", "\(quiz.score)/\(quiz.index)", fair && quiz.score == 1 && quiz.index == 2 && quiz.misses.count == 1)
        cellBuildChecks(&r)
        cellMembraneChecks(&r)
    }
}

/// The self-check's own generator for the quiz (SplitMix64 behind RandomNumberGenerator).
struct CEQuizRandom: RandomNumberGenerator {
    var rng: BioRandom
    init(seed: UInt64) { rng = BioRandom(seed: seed) }
    mutating func next() -> UInt64 { rng.next() }
}
