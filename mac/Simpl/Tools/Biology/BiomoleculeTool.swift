import SwiftUI

// Biomolecule Builder (spec §4.4): amino acids and proteins, nucleic acids, sugars and lipids, built and seen in 3D.
// Five pages, picked in the header (BioHeaderControls): Amino acids, Proteins (a peptide builder and the four levels of
// structure), Nucleic acids, Carbohydrates and Lipids — each in the chemistry tools' layout (controls, the 3D card,
// facts), sharing one state (BiomolState) so a page keeps its choices while the tool comes and goes. A launch's state
// (-SimplTool biomol -SimplToolState <page>:<options>, see `apply`) sets a page up for the screenshot suite; another
// tool can hand it a sequence or a molecule (["sequence": "MAFSDWQKG"], ["molecule": "TRP"]).

struct BiomoleculeTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.biomol)`.
    static let pages: [BioPage] = [
        BioPage(id: "amino", title: "Amino acids", symbol: "a.circle", help: "The 20 amino acids"),
        BioPage(id: "proteins", title: "Proteins", symbol: "scribble", help: "Build a peptide; the four levels of structure"),
        BioPage(id: "nucleic", title: "Nucleic acids", symbol: "link", help: "Nucleotides joined into a strand"),
        BioPage(id: "carbs", title: "Carbohydrates", symbol: "hexagon", help: "Sugars and the glycosidic bond"),
        BioPage(id: "lipids", title: "Lipids", symbol: "drop", help: "Fatty acids, fats, phospholipids and cholesterol"),
    ]

    @ObservedObject private var nav = BioNav.shared
    @ObservedObject private var s = BiomolState.shared

    var body: some View {
        let page = nav.page(.biomol)
        Group {
            switch page {
            case "proteins": BiomolProteinPage()
            case "nucleic": BiomolNucleicPage()
            case "carbs": BiomolCarbPage()
            case "lipids": BiomolLipidPage()
            default: BiomolAminoPage()
            }
        }
        .onAppear {
            BiomoleculeTool.apply(state)
            BiomoleculeTool.takeHandOff()
        }
        .onChange(of: page) { _, _ in
            s.selection = nil
        }
    }

    @MainActor private static var launchApplied = false

    /// A launch's state, once a launch (the screenshot suite's flags): the page first (`amino`, `proteins`, `nucleic`,
    /// `carbs`, `lipids`), then any of — shared: `style=ball|space|sticks`, `colour=element|part|class`, `neutral`,
    /// `nolabels`, `h=on|off`, `select=<id>`; amino acids: `aa=W`, `dform`; proteins: `build`, `level=primary|secondary|
    /// tertiary|quaternary`, `seq=…` (-SimplBioSeq), `ss=helix|strand|coil|<one letter a residue>`, `nohbonds`,
    /// `ribbon=off|over|only`, `sheet`, `core`, `surface`, `denatured`; nucleic acids: `nt=DA`, `strand`, `seq=ATGC`,
    /// `complement`; carbohydrates: `mono=GLC`, `anomers`, `di=MAL`, `split`, `poly=cellulose`; lipids: `lipid=POV` (or
    /// `lipid=compare`).
    @MainActor
    static func apply(_ st: BioLaunchState?) {
        guard let st, !launchApplied else { return }
        launchApplied = true
        let s = BiomolState.shared
        let page = st.page ?? BioNav.shared.page(.biomol)
        if let v = st.value("style") { s.style = v == "space" ? .spaceFill : (v == "sticks" ? .sticks : .ballStick) }
        if let v = st.value("colour") ?? st.value("color"), let c = BiomolColourBy(rawValue: v) { s.colourBy = c }
        if st.has("neutral") { s.ph7 = false }
        if st.has("nolabels") { s.labels = false }
        if let v = st.value("h") {
            for p in ["amino", "peptide", "secondary", "nucleotide", "sugar", "sugar2", "lipid"] { s.setHydrogens(p, v == "on") }
        }
        // Amino acids
        if let v = st.value("aa"), let c = v.uppercased().first, BioAminoAcids.byOne[c] != nil { s.aminoAcid = c }
        if st.has("dform") { s.dForm = true }
        // Proteins
        if st.has("build") { s.proteinMode = .build }
        if let v = st.value("level"), let l = BiomolState.Level(rawValue: v) {
            s.proteinMode = .levels
            s.level = l
        }
        if let seq = st.value("seq") {
            if page == "nucleic" {
                s.nucleicMode = .strand
                s.applyStrand(seq, animate: false)
            } else {
                s.applySequence(seq, animate: false)
            }
        }
        if let v = st.value("ss") {
            if let ss = BiomolSS(rawValue: v) { s.setStructure(ss, at: Array(0..<s.sequence.count)) } else { s.setStructure(v.uppercased()) }
        }
        if st.has("nohbonds") { s.hBonds = false }
        if let v = st.value("ribbon"), let r = BiomolRibbon(rawValue: v) { s.ribbon = r }
        if st.has("sheet") { s.sheetDemo = true }
        if st.has("core") { s.show = .core }
        if st.has("surface") { s.show = .surface }
        if st.has("denatured") { s.setDenatured(true) }
        // Nucleic acids
        if let v = st.value("nt")?.uppercased(), BiomolNucleotide.codes.contains(v) {
            s.nucleicMode = .nucleotides
            s.nucleotide = v
        }
        if st.has("strand") { s.nucleicMode = .strand }
        if st.has("complement") { s.complement = true }
        // Carbohydrates
        if let v = st.value("mono")?.uppercased(), BiomolSugar.monosaccharides.contains(v) {
            s.carbMode = .mono
            s.mono = v
        }
        if st.has("anomers") {
            s.carbMode = .mono
            s.compareAnomers = true
        }
        if let v = st.value("di")?.uppercased(), BiomolSugar.disaccharides.contains(v) {
            s.carbMode = .di
            s.di = v
        }
        if st.has("split") { s.split = true }
        if let v = st.value("poly"), let p = BiomolPolysaccharide(rawValue: v.lowercased()) {
            s.carbMode = .poly
            s.poly = p
        }
        // Lipids
        if let v = st.value("lipid")?.uppercased() {
            if v == "COMPARE" { s.lipid = "STE+OLA" } else if BiomolLipid.codes.contains(v) { s.lipid = v }
        }
        if let v = st.value("select") { s.selection = v }
    }

    /// What another tool handed over (§3.2 hand-off), taken once: a sequence (a protein's one-letter codes, or a short DNA
    /// strand) or a molecule's code (an amino acid "TRP", a nucleotide "DA", a sugar "MAL", a lipid "POV").
    @MainActor
    static func takeHandOff() {
        let tools = ToolsCenter.shared
        let s = BiomolState.shared
        let nav = BioNav.shared
        if let seq = tools.takeInfo("sequence")?.uppercased().filter({ !$0.isWhitespace }), !seq.isEmpty {
            if seq.count <= BiomolStrand.maxLength && seq.allSatisfy({ "ACGT".contains($0) }) {
                nav.set(.biomol, "nucleic")
                s.nucleicMode = .strand
                s.applyStrand(seq, animate: false)
            } else {
                nav.set(.biomol, "proteins")
                s.proteinMode = .build
                s.sheetDemo = false
                s.applySequence(seq.replacingOccurrences(of: "*", with: ""), animate: false)
            }
        }
        if let code = tools.takeInfo("molecule")?.uppercased() {
            // (an amino acid by name or three letters: one letter would mistake AMP's "A" for alanine)
            if code.count >= 3, let aa = BioAminoAcids.find(code) {
                nav.set(.biomol, "amino")
                s.aminoAcid = aa.one
            } else if BiomolNucleotide.codes.contains(code) {
                nav.set(.biomol, "nucleic")
                s.nucleicMode = .nucleotides
                s.nucleotide = code
            } else if BiomolSugar.monosaccharides.contains(code) {
                nav.set(.biomol, "carbs")
                s.carbMode = .mono
                s.mono = code
            } else if BiomolSugar.disaccharides.contains(code) {
                nav.set(.biomol, "carbs")
                s.carbMode = .di
                s.di = code
            } else if BiomolLipid.codes.contains(code) {
                nav.set(.biomol, "lipids")
                s.lipid = code
            }
        }
    }
}
