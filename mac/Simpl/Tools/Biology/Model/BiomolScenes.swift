import Foundation

// The Biomolecule Builder's scenes as values (spec §4.4, §3.4, §9): each page's molecule drawn as the chemistry builder
// draws one (CPK colours and radii from the chemistry tables, handed in as `BiomolChem`; bonds by order), coloured by
// element, part or class, with the bonds a page is about highlighted (peptide, glycosidic, phosphodiester, ester, the
// cis double bond), backbone H-bonds as dashes, cartoons over or instead of the atoms, and labels with leader lines;
// and the short motions a page plays (a residue sliding in as water lifts off, a sugar split by water) as groups of
// items with where they go and how they fade. Ids name what a click picks: a part ("part:amino"), a residue
// ("ss:2:res:7"), an atom ("…:atom:31") — so a label pressed selects its whole part.

/// The chemistry tables atoms are drawn with: CPK colours as "#rrggbb" and the covalent and van der Waals radii (Å).
/// The view hands in the chemistry builder's (MBChem); the self-check uses `plain`.
struct BiomolChem: @unchecked Sendable {
    let cpk: (String) -> String
    let covalent: (String) -> Double
    let vdw: (String) -> Double

    static let plain = BiomolChem(
        cpk: { ["H": "#f2f2f2", "C": "#909090", "N": "#3050f8", "O": "#ff0d0d", "P": "#ff8000", "S": "#ffff30"][$0] ?? "#c8c8c8" },
        covalent: { ["H": 0.31, "C": 0.76, "N": 0.71, "O": 0.66, "P": 1.07, "S": 1.05][$0] ?? 0.75 },
        vdw: { ["H": 1.2, "C": 1.7, "N": 1.55, "O": 1.52, "P": 1.8, "S": 1.8][$0] ?? 1.7 })
}

/// What colours the atoms (the shared "Colour by" control).
enum BiomolColourBy: String, CaseIterable, Sendable {
    case element, part, aaClass = "class"

    var label: String { self == .element ? "Element" : (self == .part ? "Part" : "Class") }
}

/// The shared controls' choices, as a scene needs them.
struct BiomolLook: Equatable, Sendable {
    var style: BioAtomStyle = .ballStick
    var hydrogens = true
    var ph7 = true
    var colourBy: BiomolColourBy = .element
    var labels = true
    var dark = false

    var key: String { "\(style.rawValue)/\(hydrogens)/\(ph7)/\(colourBy.rawValue)/\(labels)/\(dark)" }
}

/// A short text drawn at a point in a scene (a ring carbon's number, a charge), facing the camera.
struct BiomolMark: Equatable, Sendable {
    let pos: BioVec
    let text: String
    /// "#rrggbb", or nil for the atom-symbol white.
    var hex: String?
    /// Its size (Å).
    var size = 0.42
    /// (1.3.79) The radius of the atom at `pos` it belongs to (a charge): the mark then sits on the atom's upper right
    /// edge as the camera sees it, as a charge is written — nil: at `pos` itself.
    var atomRadius: Double?
}

/// A short motion (§4.4 Animate dehydration, the sugar's hydrolysis): groups of items, each moved from one offset to
/// another and faded, after a delay; a `pulse` flashes in and out (a new bond).
struct BiomolMotion: Sendable {
    struct Group: Sendable {
        var items: [BioSceneItem]
        var from = BioVec(0, 0, 0)
        var to = BioVec(0, 0, 0)
        var fromAlpha = 1.0
        var toAlpha = 1.0
        var delay = 0.0
        var duration = 0.6
        var pulse = false
        /// When (s from the motion's start) it fades out, 0.2 s, after its move (the water a hydrolysis uses up).
        var fadeOutAt: Double?
    }

    var groups: [Group] = []
    /// How long it runs (s).
    var length: Double { groups.map { max($0.delay + $0.duration, ($0.fadeOutAt ?? 0) + 0.2) }.max() ?? 0 }
}

/// A cartoon over the atoms, instead of them, or none.
enum BiomolRibbon: String, CaseIterable, Sendable {
    case off, over, only

    var label: String { self == .off ? "Atoms" : (self == .over ? "Both" : "Ribbon") }
}

enum BiomolScenes {
    // MARK: Atoms

    /// A model's atoms and bonds as items (the chemistry builder's look), each atom's id given by `id`, its colour by
    /// `colour` (nil: CPK); H left out when the look hides them, and whatever `skip` says.
    static func atomItems(_ m: BioMoleculeModel, look: BiomolLook, chem: BiomolChem, colour: (BioMoleculeModel.Atom) -> String?,
                          id: (BioMoleculeModel.Atom) -> String, skip: (BioMoleculeModel.Atom) -> Bool = { _ in false }) -> [BioSceneItem] {
        var cpk: [String: String] = [:]
        var radii: [String: Double] = [:]
        for a in m.atoms where cpk[a.element] == nil {
            cpk[a.element] = chem.cpk(a.element)
            radii[a.element] = BioMoleculeMeshes.radius(style: look.style, covalent: chem.covalent(a.element), vdw: chem.vdw(a.element))
        }
        let items = BioMoleculeMeshes.items(m, style: look.style, radius: { radii[$0.element] ?? 0.3 },
                                            color: { colour($0) ?? cpk[$0.element] ?? "#c8c8c8" },
                                            skip: { (!look.hydrogens && $0.element == "H") || skip($0) })
        return items.map { it in
            guard let s = it.id, s.hasPrefix("atom:"), let k = Int(s.dropFirst(5)), k < m.atoms.count else { return it }
            var out = it
            out.id = id(m.atoms[k])
            return out
        }
    }

    /// The drawn radius of an atom (Å) in a look.
    static func radius(_ element: String, look: BiomolLook, chem: BiomolChem) -> Double {
        BioMoleculeMeshes.radius(style: look.style, covalent: chem.covalent(element), vdw: chem.vdw(element))
    }

    /// A glowing sleeve round a bond (or a path of bonds): the highlighted peptide, glycosidic, ester or phosphodiester
    /// bond, translucent so the bond shows through.
    static func sleeve(_ pts: [BioVec], id: String, hex: String, radius: Double = 0.26) -> BioSceneItem {
        let mesh = pts.count == 2 ? BioShapes.cylinder(from: pts[0], to: pts[1], radius: radius, segments: 16)
            : BioPath.tube(pts, radius: radius, sides: 12, smooth: false)
        return BioSceneItem(id: id, mesh: mesh, material: BioMaterial(hex, alpha: 0.5, roughness: 0.3, emission: 0.45), order: 3)
    }

    /// An H-bond's dashes (§3.8: 0.15 Å thick).
    static func hBondItem(_ a: BioVec, _ b: BioVec, id: String, dark: Bool) -> BioSceneItem {
        BioSceneItem(id: id, mesh: BioMoleculeMeshes.dashes(from: a, to: b, radius: 0.075, dash: 0.28, gap: 0.2),
                     material: BioMaterial(BioPalette.hBond.hex(dark: dark), roughness: 0.5))
    }

    /// The atom of a group nearest the group's centre (a label's anchor sits on an atom, so the ray test finds it).
    static func anchor(_ pts: [BioVec]) -> BioVec? {
        guard !pts.isEmpty else { return nil }
        let c = BiomolMath.centroid(pts)
        return pts.min { BioV.distance($0, c) < BioV.distance($1, c) }
    }

    /// The charge marks of a form ("+" blue, "−" red), each on its atom's upper right edge as the camera sees it.
    /// (1.3.79: placed 0.42 Å out from the atom, away from the molecule's centre, a mark stood among the –NH₃⁺'s
    /// hydrogens or in the air beside its atom, whichever way the molecule was turned.)
    static func chargeMarks(_ m: BioMoleculeModel, charges: [String: Int], look: BiomolLook, chem: BiomolChem) -> [BiomolMark] {
        guard !charges.isEmpty else { return [] }
        return m.atoms.compactMap { a in
            guard let q = charges[a.name], q != 0 else { return nil }
            return BiomolMark(pos: a.pos, text: q > 0 ? "+" : "−", hex: (q > 0 ? BioPalette.basic : BioPalette.acidic).hex(dark: look.dark),
                              size: 0.62, atomRadius: radius(a.element, look: look, chem: chem))
        }
    }

    // MARK: Amino acids (§4.4 Amino acids)

    /// A free amino acid: each atom "part:<amino|alpha|carboxyl|r>:atom:<id>", coloured by element, part, or class (the R
    /// group in its class's colour, the rest grey); its parts labelled. Mirrored, it is the D-form.
    static func aminoAcid(_ one: Character, look: BiomolLook, mirrored: Bool, chem: BiomolChem) -> BioSceneContent {
        guard let t = BiomolAminoAcid.template(one) else { return BioSceneContent() }
        let m = BiomolAminoAcid.model(one, ph7: look.ph7, mirrored: mirrored)
        let cls = BioAminoAcids.byOne[Character(one.uppercased())]?.group ?? .nonpolar
        let parts = m.atoms.map { BiomolAminoAcid.part($0.name, in: t) }
        let items = atomItems(m, look: look, chem: chem, colour: { a in
            switch look.colourBy {
            case .element: return nil
            case .part: return parts[a.id].colour.hex(dark: look.dark)
            case .aaClass: return parts[a.id] == BiomolParts.rGroup ? BioPalette.aminoAcid(cls).hex(dark: look.dark) : BiomolParts.grey.hex(dark: look.dark)
            }
        }, id: { "part:\(parts[$0.id].id):atom:\($0.id)" })
        var labels: [BioLabel3D] = []
        if look.labels {
            for p in [BiomolParts.amino, BiomolParts.alpha, BiomolParts.carboxyl, BiomolParts.rGroup] {
                let pts = m.atoms.filter { parts[$0.id] == p && (look.hydrogens || $0.element != "H") }.map(\.pos)
                guard let a = anchor(pts) else { continue }
                var text = p.name
                switch p {
                case BiomolParts.amino: text = look.ph7 ? "Amino group (–NH₃⁺)" : "Amino group (–NH₂)"
                case BiomolParts.carboxyl: text = look.ph7 ? "Carboxyl group (–COO⁻)" : "Carboxyl group (–COOH)"
                case BiomolParts.rGroup: text = t.code == "GLY" ? "R group (just H)" : (t.code == "PRO" ? "R group (ring back to N)" : "R group (side chain)")
                default: break
                }
                labels.append(BioLabel3D(id: "part:\(p.id)", text: text, anchor: a, area: Double(pts.count)))
            }
        }
        return BioSceneContent(items: items, labels: labels)
    }

    // MARK: Peptides (§4.4 Proteins)

    /// What a peptide scene shows besides its atoms.
    struct PeptideOptions: Equatable, Sendable {
        var hBonds = false
        var ribbon = BiomolRibbon.off
        /// Residues left out (one sliding in or out).
        var hidden: Set<Int> = []
        /// The C-terminal OXT and HXT left out (water arriving to make them).
        var hideTerminalOH = false
        var labelSegments = true
    }

    /// The segment (§ cartoon) each residue is in.
    static func segmentOf(_ p: BiomolPeptide) -> (segments: [BiomolCartoon.Segment], of: [Int]) {
        let segs = BiomolCartoon.segments(p.built.map(\.cartoon))
        var of = [Int](repeating: 0, count: p.count)
        for (k, s) in segs.enumerated() { for i in s.first...s.last { of[i] = k } }
        return (segs, of)
    }

    static let backboneNames: Set<String> = ["N", "CA", "C", "O", "OXT", "H", "H2", "H3", "HXT", "HA", "HA2", "HA3"]

    /// A peptide: atoms "<prefix>ss:<k>:res:<i>:atom:<n>" (n its index in the peptide), coloured by element, part
    /// (backbone slate, R groups in their class colours, peptide bonds glowing) or class (each residue in its class
    /// colour); its backbone H-bonds ("<prefix>hbond:<k>"); its cartoon ("<prefix>ss:<k>:cartoon"); its labels.
    static func peptide(_ p: BiomolPeptide, look: BiomolLook, options: PeptideOptions = PeptideOptions(), chem: BiomolChem,
                        prefix: String = "", labelPrefix: String = "") -> BioSceneContent {
        guard p.count > 0 else { return BioSceneContent() }
        let (segs, of) = segmentOf(p)
        let (m, index) = p.model(hydrogens: true)
        var items: [BioSceneItem] = []
        let last = p.count - 1
        func hiddenAtom(_ k: Int) -> Bool {
            let a = p.atoms[k]
            if options.hidden.contains(a.residue) { return true }
            if options.hideTerminalOH && a.residue == last && (a.name == "OXT" || a.name == "HXT") { return true }
            return false
        }
        if options.ribbon != .only {
            items += atomItems(m, look: look, chem: chem, colour: peptideColour(p, index: index, look: look), id: { a in
                let k = index[a.id]
                let r = p.atoms[k].residue
                return "\(prefix)ss:\(of[r]):res:\(r):atom:\(k)"
            }, skip: { hiddenAtom(index[$0.id]) })
            if look.colourBy == .part {
                for b in p.bonds where b.peptide && !hiddenAtom(b.a) && !hiddenAtom(b.b) {
                    items.append(sleeve([p.atoms[b.a].pos, p.atoms[b.b].pos], id: "\(prefix)pbond:\(p.atoms[b.b].residue)",
                                        hex: BiomolParts.bondHighlight.hex(dark: look.dark), radius: 0.22))
                }
            }
        }
        if options.hBonds {
            for (k, h) in p.hBonds.enumerated() where !options.hidden.contains(h.donor) && !options.hidden.contains(h.acceptor) {
                let from = look.hydrogens && options.ribbon != .only ? p.atoms[h.h].pos : p.atoms[h.n].pos
                items.append(hBondItem(from, p.atoms[h.o].pos, id: "\(prefix)hbond:\(k)", dark: look.dark))
            }
        }
        if options.ribbon != .off {
            let shown = options.hidden.isEmpty ? p.caTrace : Array(p.caTrace.prefix(p.count - options.hidden.count))
            let segsShown = BiomolCartoon.segments(Array(p.built.map(\.cartoon).prefix(shown.count)))
            let meshes = BiomolCartoon.meshes(shown, segments: segsShown)
            for (k, s) in segsShown.enumerated() {
                items.append(BioSceneItem(id: "\(prefix)ss:\(k):cartoon", mesh: meshes[k],
                                          material: BioMaterial(BiomolCartoon.hex(s.kind, dark: look.dark), alpha: options.ribbon == .over ? 0.72 : 1, roughness: 0.45),
                                          order: options.ribbon == .over ? 1 : 0))
            }
        }
        var labels: [BioLabel3D] = []
        if look.labels {
            let visible = (0..<p.count).filter { !options.hidden.contains($0) }
            // (a cartoon alone: the ends named on the cartoon's segments, at their CA)
            let cartoonOnly = options.ribbon == .only
            if let first = visible.first {
                labels.append(BioLabel3D(id: cartoonOnly ? "\(prefix)ss:\(of[first])" : "\(prefix)ss:\(of[first]):res:\(first)",
                                         text: "\(labelPrefix)N-terminus (\(p.residueName(first)))",
                                         anchor: p.atoms[cartoonOnly ? p.backbone[first].ca : p.backbone[first].n].pos, area: 3))
            }
            if let lastShown = visible.last, lastShown != visible.first {
                let c = cartoonOnly ? p.backbone[lastShown].ca : (p.atom(lastShown, "OXT") ?? p.backbone[lastShown].c)
                labels.append(BioLabel3D(id: cartoonOnly ? "\(prefix)ss:\(of[lastShown])" : "\(prefix)ss:\(of[lastShown]):res:\(lastShown)",
                                         text: "\(labelPrefix)C-terminus (\(p.residueName(lastShown)))",
                                         anchor: p.atoms[c].pos, area: 3))
            }
            if options.labelSegments {
                for (k, s) in segs.enumerated() where s.kind != "C" && s.count >= 3 && !options.hidden.contains(s.first) {
                    let mid = (s.first + s.last) / 2
                    labels.append(BioLabel3D(id: "\(prefix)ss:\(k)", text: "\(labelPrefix)\(BiomolCartoon.name(s.kind)) (\(s.first + 1)–\(s.last + 1))",
                                             anchor: p.atoms[p.backbone[mid].ca].pos, area: Double(s.count)))
                }
            }
            if options.hBonds, let h = p.hBonds.first(where: { !options.hidden.contains($0.donor) }) {
                labels.append(BioLabel3D(id: "\(prefix)hbond", text: "Backbone H-bond (N–H···O=C)",
                                         anchor: (p.atoms[h.h].pos + p.atoms[h.o].pos) / 2, area: 2))
            }
            if look.colourBy == .part && options.ribbon != .only, let b = p.bonds.first(where: { $0.peptide && !hiddenAtom($0.b) }) {
                labels.append(BioLabel3D(id: "\(prefix)pbond", text: "Peptide bond (C–N)", anchor: (p.atoms[b.a].pos + p.atoms[b.b].pos) / 2, area: 2))
            }
            for pro in p.prolineBreaks where !options.hidden.contains(pro) {
                labels.append(BioLabel3D(id: "\(prefix)ss:\(of[pro]):res:\(pro)", text: "Pro \(pro + 1) breaks the helix",
                                         anchor: p.atoms[p.backbone[pro].ca].pos, area: 2))
            }
        }
        return BioSceneContent(items: items, labels: labels)
    }

    /// A peptide's charges at pH 7 (its atoms by index): the N-terminus +1, the C-terminus −1, and each ionised side
    /// chain's (Asp, Glu −1; Lys, Arg +1; His neutral). None in the neutral form.
    static func peptideCharges(_ p: BiomolPeptide) -> [(atom: Int, charge: Int)] {
        guard p.ph7, p.count > 0 else { return [] }
        var out: [(atom: Int, charge: Int)] = [(p.backbone[0].n, 1)]
        if let o = p.atom(p.count - 1, "OXT") { out.append((o, -1)) }
        for i in 0..<p.count {
            guard let t = BioData.residue(String(p.sequence[i])) else { continue }
            for (name, q) in (t.ph7?.charges ?? [:]).sorted(by: { $0.key < $1.key }) where name != "N" && name != "OXT" {
                if let k = p.atom(i, name) { out.append((k, q)) }
            }
        }
        return out
    }

    /// Those charges as marks on their atoms (as `chargeMarks` places them), leaving out hidden residues.
    static func peptideMarks(_ p: BiomolPeptide, look: BiomolLook, chem: BiomolChem, hidden: Set<Int> = []) -> [BiomolMark] {
        peptideCharges(p).compactMap { c in
            let a = p.atoms[c.atom]
            guard !hidden.contains(a.residue) else { return nil }
            return BiomolMark(pos: a.pos, text: c.charge > 0 ? "+" : "−", hex: (c.charge > 0 ? BioPalette.basic : BioPalette.acidic).hex(dark: look.dark),
                              size: 0.62, atomRadius: radius(a.element, look: look, chem: chem))
        }
    }

    /// A rotation (rows) taking `along` to +y and `across` (made perpendicular) to +x, right-handed, so a helix or a
    /// sheet stands up facing the camera without being mirrored.
    static func uprightRows(along: BioVec, across: BioVec) -> [[Double]] {
        let y = BioV.unit(along)
        var x = across - BioV.dot(across, y) * y
        if BioV.length(x) < 1e-6 { x = BioV.cross(y, abs(y.z) < 0.9 ? BioVec(0, 0, 1) : BioVec(1, 0, 0)) }
        x = BioV.unit(x)
        let z = BioV.cross(x, y)
        return [[x.x, x.y, x.z], [y.x, y.y, y.z], [z.x, z.y, z.z]]
    }

    /// A peptide stood upright (its long axis +y) and centred at `at`.
    static func upright(_ p: BiomolPeptide, along: BioVec, across: BioVec, centre: BioVec, at: BioVec) -> BiomolPeptide {
        let r = uprightRows(along: along, across: across)
        let moved = p.moved(by: -centre)
        return moved.transformed(r, at)
    }

    /// The secondary level (§4.4 item 6): an α-helix (Ala₁₂) and the β-sheet demo (Ala₇ × 3) side by side, upright,
    /// with their backbone H-bonds.
    static func secondary(look: BiomolLook, chem: BiomolChem) -> BioSceneContent {
        let helix0 = BiomolPeptideBuilder.build(String(repeating: "A", count: 12), ss: Array(repeating: .helix, count: 12), ph7: look.ph7)
        let ca = helix0.caTrace
        let axis = BiomolCartoon.axes(ca).reduce(BioVec(0, 0, 0)) { $0 + (BioV.dot($1, ca[ca.count - 1] - ca[0]) < 0 ? -$1 : $1) }
        let hc = BiomolMath.centroid(ca)
        let helix = upright(helix0, along: axis, across: ca[0] - hc, centre: hc, at: BioVec(-10, 0, 0))
        let sheet = sheetUpright(look: look, at: BioVec(9, 0, 0))
        var c = peptide(helix, look: look, options: PeptideOptions(hBonds: true, labelSegments: false), chem: chem, prefix: "helix:", labelPrefix: "α-helix ")
        c.labels = c.labels.filter { !$0.text.contains("terminus") }
        c.labels.append(BioLabel3D(id: "helix:ss", text: "α-helix (3.6 residues a turn)", anchor: helix.caTrace[6], area: 12))
        var sheetContent = sheetScene(sheet, look: look, chem: chem)
        sheetContent.labels = sheetContent.labels.filter { !$0.text.contains("terminus") }
        c.items += sheetContent.items
        c.labels += sheetContent.labels
        return c
    }

    /// The β-sheet demo stood upright: strands along +y, side by side across x, the sheet facing the camera.
    static func sheetUpright(look: BiomolLook, at: BioVec = BioVec(0, 0, 0)) -> BiomolSheet {
        let s = BiomolSheet.build("AAAAAAA", ph7: look.ph7)
        let s1 = s.strands[0].caTrace, s3 = s.strands[2].caTrace
        let c1 = BiomolMath.centroid(s1), c3 = BiomolMath.centroid(s3)
        let centre = BiomolMath.centroid(s.strands.flatMap(\.caTrace))
        let r = uprightRows(along: s1[6] - s1[0], across: c3 - c1)
        let strands = s.strands.map { $0.moved(by: -centre).transformed(r, at) }
        return BiomolSheet(strands: strands, hBonds: s.hBonds)
    }

    /// The β-sheet demo's scene: its three strands ("sheet:<s>:…") and the H-bonds between them ("sheet:hbond:<k>").
    static func sheetScene(_ s: BiomolSheet, look: BiomolLook, chem: BiomolChem) -> BioSceneContent {
        var c = BioSceneContent()
        for (k, strand) in s.strands.enumerated() {
            let part = peptide(strand, look: look, options: PeptideOptions(hBonds: false, labelSegments: false), chem: chem,
                               prefix: "sheet:\(k):", labelPrefix: "Strand \(k + 1) ")
            c.items += part.items
            if k == 1 { c.labels += part.labels.filter { $0.text.contains("N-terminus") || $0.text.contains("C-terminus") } }
        }
        for (k, h) in s.hBonds.enumerated() {
            let d = s.strands[h.donor], a = s.strands[h.acceptor]
            let from = look.hydrogens ? d.atoms[h.bond.h].pos : d.atoms[h.bond.n].pos
            c.items.append(hBondItem(from, a.atoms[h.bond.o].pos, id: "sheet:hbond:\(k)", dark: look.dark))
        }
        if look.labels {
            if let h = s.hBonds.first {
                let d = s.strands[h.donor], a = s.strands[h.acceptor]
                c.labels.append(BioLabel3D(id: "sheet:hbond", text: "H-bond between strands (backbone)",
                                           anchor: (d.atoms[h.bond.h].pos + a.atoms[h.bond.o].pos) / 2, area: 2))
            }
            c.labels.append(BioLabel3D(id: "sheet:0", text: "β-pleated sheet (antiparallel)", anchor: s.strands[0].caTrace[3], area: 14))
        }
        return c
    }

    // MARK: Nucleotides and a strand (§4.4 Nucleic acids)

    /// A nucleotide: atoms "part:<phosphate|sugar|base>:atom:<id>", coloured by element, part (phosphate violet, sugar
    /// teal, base in its letter's colour) or class (the base in its colour, the rest grey); its parts labelled.
    static func nucleotide(_ code: String, look: BiomolLook, chem: BiomolChem) -> BioSceneContent {
        guard let mol = BioData.molecule(code) else { return BioSceneContent() }
        let m = mol.model(ph7: look.ph7)
        let letter = BiomolNucleotide.baseLetter(code)
        let parts = m.atoms.map { BiomolNucleotide.part($0.name) }
        let baseHex = BioPalette.base(letter).hex(dark: look.dark)
        let items = atomItems(m, look: look, chem: chem, colour: { a in
            switch look.colourBy {
            case .element: return nil
            case .part: return parts[a.id] == BiomolParts.base ? baseHex : parts[a.id].colour.hex(dark: look.dark)
            case .aaClass: return parts[a.id] == BiomolParts.base ? baseHex : BiomolParts.grey.hex(dark: look.dark)
            }
        }, id: { "part:\(parts[$0.id].id):atom:\($0.id)" })
        var labels: [BioLabel3D] = []
        if look.labels {
            let deoxy = BiomolNucleotide.isDeoxy(code)
            let texts: [(BiomolPart, String)] = [
                (BiomolParts.phosphate, code == "ATP" ? "Three phosphates (α, β, γ)" : "Phosphate (on the 5′ carbon)"),
                (BiomolParts.sugar, deoxy ? "Deoxyribose (H at 2′)" : "Ribose (OH at 2′)"),
                (BiomolParts.base, "\(BiomolNucleotide.baseName(letter)) (\(BiomolNucleotide.isPurine(letter) ? "purine: 2 rings" : "pyrimidine: 1 ring"))"),
            ]
            for (p, text) in texts {
                let pts = m.atoms.filter { parts[$0.id] == p && $0.element != "H" }.map(\.pos)
                if let a = anchor(pts) { labels.append(BioLabel3D(id: "part:\(p.id)", text: text, anchor: a, area: Double(pts.count))) }
            }
            if let o3 = m.atoms.first(where: { $0.name == "O3'" }) {
                labels.append(BioLabel3D(id: "part:sugar:atom:\(o3.id)", text: "3′-OH (the next nucleotide joins here)", anchor: o3.pos, area: 1))
            }
        }
        return BioSceneContent(items: items, labels: labels)
    }

    /// The sugar carbons' numbers 1′–5′ on a nucleotide (and a sugar's 1–6 on a sugar, by `sugarStyle`).
    static func carbonMarks(_ m: BioMoleculeModel, primes: Bool, look: BiomolLook, chem: BiomolChem) -> [BiomolMark] {
        m.atoms.compactMap { a in
            guard a.element == "C" else { return nil }
            if primes {
                guard a.name.hasSuffix("'"), let d = a.name.dropFirst().first, d.isNumber else { return nil }
                return BiomolMark(pos: a.pos, text: "\(d)′")
            }
            let digits = a.name.filter { $0.isNumber }
            return digits.isEmpty ? nil : BiomolMark(pos: a.pos, text: digits)
        }
    }

    /// What a strand scene shows besides its atoms.
    struct StrandOptions: Equatable, Sendable {
        /// Strand 1's pair left out (a nucleotide sliding in or out), with its partner.
        var hiddenPair: Int?
    }

    /// The strand (and its complement): atoms "nt:<strand>:<pair>:atom:<n>", coloured by element, part (phosphate,
    /// sugar, base) or class (each strand's backbone in its colour, bases by letter); the phosphodiester bonds glowing
    /// ("pdiester:<strand>:<k>"); the base pairs' H-bonds ("bp:<k>"); labels for the 5′ phosphate, the 3′-OH and the
    /// phosphodiester bond.
    static func strand(_ d: BioDuplex, look: BiomolLook, options: StrandOptions = StrandOptions(), chem: BiomolChem) -> BioSceneContent {
        let m = d.model
        let n = d.sequence.count
        guard n > 0 else { return BioSceneContent() }
        let parts = d.atoms.map { BiomolNucleotide.part($0.name) }
        func hidden(_ k: Int) -> Bool { options.hiddenPair.map { d.atoms[k].pair == $0 } ?? false }
        var items = atomItems(m, look: look, chem: chem, colour: strandColour(d, look: look), id: { a in "nt:\(d.atoms[a.id].strand):\(d.atoms[a.id].pair):atom:\(a.id)" }, skip: { hidden($0.id) })
        for s in d.hasStrand2 ? [1, 2] : [1] {
            for (k, link) in BiomolStrand.linkages(d, strand: s).enumerated() where !link.contains(where: hidden) {
                items.append(sleeve(link.map { d.atoms[$0].pos }, id: "pdiester:\(s):\(k)", hex: BiomolParts.bondHighlight.hex(dark: look.dark), radius: 0.3))
            }
        }
        for (k, h) in d.hBonds.enumerated() where !hidden(h.a) {
            items.append(hBondItem(d.atoms[h.a].pos, d.atoms[h.b].pos, id: "bp:\(k)", dark: look.dark))
        }
        var labels: [BioLabel3D] = []
        if look.labels {
            if let p = d.index(strand: 1, pair: 0, name: "P") {
                labels.append(BioLabel3D(id: "nt:1:0", text: "5′ end (phosphate)", anchor: d.atoms[p].pos, area: 2))
            }
            let lastPair = (options.hiddenPair.map { $0 - 1 } ?? (n - 1))
            if lastPair >= 0, let o = d.index(strand: 1, pair: lastPair, name: "O3'") {
                labels.append(BioLabel3D(id: "nt:1:\(lastPair)", text: "3′ end (–OH)", anchor: d.atoms[o].pos, area: 2))
            }
            if let link = BiomolStrand.linkages(d, strand: 1).first, !link.contains(where: hidden) {
                labels.append(BioLabel3D(id: "pdiester", text: "Phosphodiester bond", anchor: d.atoms[link[2]].pos, area: 3))
            }
            if d.hasStrand2 {
                let mid = n / 2
                if let p = d.index(strand: 2, pair: mid, name: "P") ?? d.index(strand: 2, pair: mid, name: "C4'") {
                    labels.append(BioLabel3D(id: "nt:2", text: "Complementary strand (runs 3′→5′ alongside)", anchor: d.atoms[p].pos, area: 6))
                }
                if let h = d.hBonds.first {
                    labels.append(BioLabel3D(id: "bp", text: "Base pairs: A·T 2 H-bonds, G·C 3", anchor: (d.atoms[h.a].pos + d.atoms[h.b].pos) / 2, area: 3))
                }
            }
            // (each base named by its letter, on strand 1)
            for k in 0..<n where k != options.hiddenPair && n <= 6 {
                let names = d.atoms.indices.filter { d.atoms[$0].strand == 1 && d.atoms[$0].pair == k && parts[$0] == BiomolParts.base }
                if let a = anchor(names.map { d.atoms[$0].pos }) {
                    labels.append(BioLabel3D(id: "nt:1:\(k)", text: "\(BiomolNucleotide.baseName(d.atoms[names[0]].base)) \(k + 1)", anchor: a, area: 1))
                }
            }
        }
        return BioSceneContent(items: items, labels: labels)
    }

    // MARK: Sugars (§4.4 Carbohydrates)

    /// A monosaccharide's parts, for "Colour by Part": its ring, the anomeric –OH, the CH₂OH it is judged against, and
    /// its other –OH groups.
    static let ringPart = BiomolPart(id: "ring", name: "Ring", colour: BioHexPair("#5b6b8c", "#a3b1cf"))
    static let anomericPart = BiomolPart(id: "anomeric", name: "Anomeric –OH", colour: BioHexPair("#e8508a", "#ff7aa8"))
    static let referencePart = BiomolPart(id: "ch2oh", name: "CH₂OH", colour: BioHexPair("#3aa3b8", "#6fd3e6"))
    static let hydroxylPart = BiomolPart(id: "oh", name: "–OH groups", colour: BioHexPair("#d63a34", "#ff6961"))

    static func sugarPart(_ name: String, in mol: BioMolecule, ring: BiomolSugar.Ring?) -> BiomolPart {
        guard let r = ring else { return ringPart }
        let heavy = BiomolGraph.heavyNeighbour(mol)[name] ?? name
        if heavy == r.anomericO { return anomericPart }
        if heavy == r.reference { return referencePart }
        if r.atoms.contains(heavy) { return ringPart }
        // (the reference carbon's own O goes with it; any other carbon off the ring — fructose's C1 — is drawn with the
        // ring, its O with the –OH groups)
        if mol.bonds.contains(where: { ($0.a == r.reference && $0.b == heavy) || ($0.b == r.reference && $0.a == heavy) }) { return referencePart }
        if mol.atom(heavy)?.element == "C" { return ringPart }
        return hydroxylPart
    }

    /// A monosaccharide: atoms "part:<ring|anomeric|ch2oh|oh>:atom:<id>", its ring carbons numbered (marks), the
    /// anomeric –OH labelled with which face of the ring it is on.
    static func monosaccharide(_ code: String, look: BiomolLook, chem: BiomolChem) -> BioSceneContent {
        guard let mol = BioData.molecule(code) else { return BioSceneContent() }
        let m = mol.model(ph7: look.ph7)
        let ring = BiomolSugar.ring(mol)
        let parts = m.atoms.map { sugarPart($0.name, in: mol, ring: ring) }
        let items = atomItems(m, look: look, chem: chem, colour: { a in
            look.colourBy == .element ? nil : parts[a.id].colour.hex(dark: look.dark)
        }, id: { "part:\(parts[$0.id].id):atom:\($0.id)" })
        var labels: [BioLabel3D] = []
        if look.labels, let r = ring {
            let alpha = BiomolSugar.isAlpha(mol, r)
            let cNum = r.anomeric.filter { $0.isNumber }, refNum = r.reference.filter { $0.isNumber }
            if let o = m.atoms.first(where: { $0.name == r.anomericO }) {
                labels.append(BioLabel3D(id: "part:anomeric", text: "C\(cNum) –OH: \(alpha ? "opposite face to" : "same face as") C\(refNum) (\(alpha ? "α" : "β"))",
                                         anchor: o.pos, area: 3))
            }
            if let c = m.atoms.first(where: { $0.name == r.reference }) {
                labels.append(BioLabel3D(id: "part:ch2oh", text: "C\(refNum) (CH₂OH)", anchor: c.pos, area: 2))
            }
            if let o = m.atoms.first(where: { $0.name == r.ringO }) {
                labels.append(BioLabel3D(id: "part:ring", text: "Ring O (\(r.atoms.count)-membered ring)", anchor: o.pos, area: 4))
            }
        }
        return BioSceneContent(items: items, labels: labels)
    }

    /// α- and β-glucose side by side ("alpha:…", "beta:…"), each centred 4.6 Å either side of the middle, so the one
    /// difference — which face of the ring C1's –OH is on — can be seen.
    static func anomerPair(look: BiomolLook, chem: BiomolChem) -> BioSceneContent {
        var c = BioSceneContent()
        for (code, prefix, x) in [("GLC", "alpha", -4.6), ("BGC", "beta", 4.6)] {
            guard let mol = BioData.molecule(code) else { continue }
            let centre = BiomolMath.centroid(mol.atoms.map(\.pos))
            let one = monosaccharide(code, look: look, chem: chem)
            let shift = BioVec(x, 0, 0) - centre
            c.items += one.items.map { it in
                var m = it
                m.mesh = it.mesh.translated(shift)
                m.id = it.id.map { "\(prefix):\($0)" }
                return m
            }
            c.labels += one.labels.filter { $0.id == "part:anomeric" }.map { l in
                BioLabel3D(id: "\(prefix):\(l.id)", text: code == "GLC" ? "α-glucose: C1 –OH opposite C6" : "β-glucose: C1 –OH beside C6",
                           anchor: l.anchor + shift, area: l.area)
            }
        }
        return c
    }

    /// The offset `anomerPair` gives a sugar (to put its marks where its atoms are).
    static func anomerShift(_ code: String) -> BioVec {
        guard let mol = BioData.molecule(code) else { return BioVec(0, 0, 0) }
        return BioVec(code == "GLC" ? -4.6 : 4.6, 0, 0) - BiomolMath.centroid(mol.atoms.map(\.pos))
    }

    /// A disaccharide, joined or split by water: atoms "res:<0|1>:atom:<id>" coloured by element or by residue; the
    /// glycosidic bond glowing ("glyco:<k>") and labelled with its linkage; each sugar named. Split, the water's atoms
    /// are labelled where they went.
    static func disaccharide(_ code: String, split: Bool, look: BiomolLook, chem: BiomolChem) -> BioSceneContent {
        guard let mol = BioData.molecule(code), let g = BiomolSugar.glycosidic(mol) else { return BioSceneContent() }
        let names = BiomolSugar.residues[code] ?? ("Sugar 1", "Sugar 2")
        var m: BioMoleculeModel
        var res: [Int]
        var new: [Int] = []
        if split, let s = BiomolSugar.split(mol) {
            m = s.model
            res = s.residue
            new = s.newAtoms
        } else {
            m = mol.model(ph7: look.ph7)
            res = m.atoms.map { BiomolSugar.residue($0.name, in: mol, glycosidicO: g.o) }
        }
        var items = atomItems(m, look: look, chem: chem, colour: { a in
            if look.colourBy == .element { return nil }
            if new.contains(a.id) { return BiomolParts.bondHighlight.hex(dark: look.dark) }
            return BiomolSugar.residueColours[res[a.id]].hex(dark: look.dark)
        }, id: { "res:\(res[$0.id]):atom:\($0.id)" })
        if !split, let o = m.atoms.first(where: { $0.name == g.o }), let c1 = m.atoms.first(where: { $0.name == g.anomeric }),
           let c2 = m.atoms.first(where: { $0.name == g.other }) {
            items.append(sleeve([c1.pos, o.pos, c2.pos], id: "glyco:0", hex: BiomolParts.bondHighlight.hex(dark: look.dark), radius: 0.3))
        }
        var labels: [BioLabel3D] = []
        if look.labels {
            for (r, name) in [(0, names.first), (1, names.second)] {
                let ring = BiomolSugar.ring(mol, primed: r == 1)
                let pts = m.atoms.filter { res[$0.id] == r && (ring?.atoms.contains($0.name) ?? ($0.element != "H")) }.map(\.pos)
                if let a = anchor(pts) { labels.append(BioLabel3D(id: "res:\(r)", text: name, anchor: a, area: 6)) }
            }
            if split {
                for k in new {
                    let a = m.atoms[k]
                    if a.element == "O" { labels.append(BioLabel3D(id: "res:\(res[k]):atom:\(k)", text: "New –OH (from water)", anchor: a.pos, area: 1)) }
                }
                if let h = new.first { labels.append(BioLabel3D(id: "res:1:atom:\(h)", text: "H (from water)", anchor: m.atoms[h].pos, area: 1)) }
            } else if let o = m.atoms.first(where: { $0.name == g.o }) {
                labels.append(BioLabel3D(id: "glyco", text: "\(BiomolSugar.linkage[code] ?? "") glycosidic bond", anchor: o.pos, area: 3))
            }
        }
        return BioSceneContent(items: items, labels: labels)
    }

    // MARK: Lipids (§4.4 Lipids)

    /// A lipid (or stearic and oleic acids side by side, "STE+OLA"): atoms "part:<slug>:atom:<id>" ("mol:<k>:…" when two
    /// are shown), coloured by element or by part (hydrophilic head blue, hydrophobic tails tan); its ester bonds and
    /// cis double bond glowing; its parts labelled.
    static func lipid(_ code: String, look: BiomolLook, chem: BiomolChem) -> BioSceneContent {
        let codes = code == "STE+OLA" ? ["STE", "OLA"] : [code]
        var c = BioSceneContent()
        for (k, cc) in codes.enumerated() {
            guard let mol = BioData.molecule(cc) else { continue }
            let shift = codes.count == 2 ? BioVec(k == 0 ? -4.5 : 4.5, 0, 0) : BioVec(0, 0, 0)
            let m = mol.model(ph7: look.ph7).moved(by: shift)
            let detail = m.atoms.map { BiomolLipid.detail($0.name, in: mol) }
            let prefix = codes.count == 2 ? "mol:\(k):" : ""
            func slug(_ s: String) -> String { s.lowercased().replacingOccurrences(of: " ", with: "-") }
            c.items += atomItems(m, look: look, chem: chem, colour: { a in
                guard look.colourBy != .element else { return nil }
                return (BiomolLipid.isHydrophilic(detail[a.id]) ? BiomolParts.hydrophilic : BiomolParts.hydrophobic).colour.hex(dark: look.dark)
            }, id: { "\(prefix)part:\(slug(detail[$0.id])):atom:\($0.id)" })
            func pos(_ n: String) -> BioVec? { m.atoms.first { $0.name == n }?.pos }
            for (j, b) in BiomolLipid.esterBonds(cc).enumerated() {
                if let x = pos(b.0), let y = pos(b.1) { c.items.append(sleeve([x, y], id: "\(prefix)ester:\(j)", hex: BiomolParts.bondHighlight.hex(dark: look.dark))) }
            }
            for (j, b) in BiomolLipid.cisBonds(cc).enumerated() {
                if let x = pos(b.0), let y = pos(b.1) { c.items.append(sleeve([x, y], id: "\(prefix)cis:\(j)", hex: BioPalette.strand.hex(dark: look.dark), radius: 0.3)) }
            }
            guard look.labels else { continue }
            if codes.count == 2 {
                let heavy = m.atoms.filter { $0.element != "H" }.map(\.pos)
                if let top = heavy.max(by: { $0.y < $1.y }) {
                    labels(&c, id: "\(prefix)part", text: cc == "STE" ? "Stearic acid: saturated, straight" : "Oleic acid: cis double bond, kinked", at: top, area: 10)
                }
                if cc == "OLA", let x = pos("C9") { labels(&c, id: "\(prefix)cis", text: "cis C9=C10 (the kink)", at: x, area: 3) }
                continue
            }
            var seen = Set<String>()
            for d in detail where !d.isEmpty && !seen.contains(d) {
                seen.insert(d)
                let pts = m.atoms.filter { detail[$0.id] == d && $0.element != "H" }.map(\.pos)
                guard let a = anchor(pts) else { continue }
                var text = d
                if d == "Ester bond" { text = cc == "TGL" ? "Ester bonds (3)" : "Ester bonds (2)" }
                if d == "Stearate tail" { text = "Stearate tails (3, saturated)" }
                if d == "Oleate tail" { text = "Oleate tail (cis kink)" }
                if d == "Hydrocarbon tail" && (cc == "PLM" || cc == "STE") { text = "Hydrocarbon tail (saturated)" }
                if d == "Hydrocarbon tail" && cc == "OLA" { text = "Hydrocarbon tail (unsaturated)" }
                labels(&c, id: "\(prefix)part:\(slug(d))", text: text, at: a, area: Double(pts.count))
            }
            if let b = BiomolLipid.cisBonds(cc).first, let x = pos(b.0) {
                labels(&c, id: "\(prefix)cis", text: "cis double bond (kink)", at: x, area: 2)
            }
        }
        return c
    }

    private static func labels(_ c: inout BioSceneContent, id: String, text: String, at: BioVec, area: Double) {
        c.labels.append(BioLabel3D(id: id, text: text, anchor: at, area: area))
    }

    // MARK: Motions (§4.4 Animate dehydration, Dehydration/hydrolysis)

    /// A water molecule (O + two H at 0.96 Å and 104.5°, its H atoms below the O), as items.
    static func water(at o: BioVec, look: BiomolLook, chem: BiomolChem) -> [BioSceneItem] {
        var m = BioMoleculeModel()
        let half = 104.5 / 2 * Double.pi / 180
        let oi = m.add("O", o, name: "O")
        let h1 = m.add("H", o + BioVec(sin(half), -cos(half), 0) * 0.96, name: "H")
        let h2 = m.add("H", o + BioVec(-sin(half), -cos(half), 0) * 0.96, name: "H")
        m.bond(oi, h1)
        m.bond(oi, h2)
        var l = look
        l.hydrogens = true
        return atomItems(m, look: l, chem: chem, colour: { _ in nil }, id: { "water:\($0.id)" })
    }

    /// Some atoms of a model and the bonds among them, as items coloured as the page colours them (what moves).
    static func itemsFor(_ m: BioMoleculeModel, keep: (BioMoleculeModel.Atom) -> Bool, look: BiomolLook, chem: BiomolChem,
                         colour: (BioMoleculeModel.Atom) -> String?) -> [BioSceneItem] {
        var sub = BioMoleculeModel()
        var map: [Int: Int] = [:]
        var back: [Int] = []
        for a in m.atoms where keep(a) {
            map[a.id] = sub.add(a.element, a.pos, name: a.name, tag: a.tag)
            back.append(a.id)
        }
        for b in m.bonds {
            if let x = map[b.a], let y = map[b.b] { sub.bond(x, y, order: b.order) }
        }
        return atomItems(sub, look: look, chem: chem, colour: { colour(m.atoms[back[$0.id]]) }, id: { "motion:\(back[$0.id])" })
    }

    /// A single bond drawn as the look draws bonds (a grey rod; sticks: each half in its atom's colour; none in space
    /// filling), for a bond that moves with one of its atoms.
    static func bondItems(_ a: BioVec, _ b: BioVec, hexA: String, hexB: String, look: BiomolLook) -> [BioSceneItem] {
        switch look.style {
        case .spaceFill: return []
        case .sticks:
            let mid = (a + b) / 2
            return [BioSceneItem(id: nil, mesh: BioShapes.cylinder(from: a, to: mid, radius: 0.13, segments: 16, caps: false), material: BioMaterial(hexA)),
                    BioSceneItem(id: nil, mesh: BioShapes.cylinder(from: mid, to: b, radius: 0.13, segments: 16, caps: false), material: BioMaterial(hexB))]
        case .ballStick:
            return [BioSceneItem(id: nil, mesh: BioShapes.cylinder(from: a, to: b, radius: 0.09, segments: 16, caps: false), material: BioMaterial("#9e9e9e"))]
        }
    }

    /// A peptide's atom colours in a look (by the model's atom id, `index` mapping it to the peptide's atom).
    static func peptideColour(_ p: BiomolPeptide, index: [Int], look: BiomolLook) -> (BioMoleculeModel.Atom) -> String? {
        { a in
            let atom = p.atoms[index[a.id]]
            let cls = BioAminoAcids.byOne[p.sequence[atom.residue]]?.group ?? .nonpolar
            switch look.colourBy {
            case .element: return nil
            case .part: return backboneNames.contains(atom.name) ? BiomolParts.backbone.colour.hex(dark: look.dark) : BioPalette.aminoAcid(cls).hex(dark: look.dark)
            case .aaClass: return BioPalette.aminoAcid(cls).hex(dark: look.dark)
            }
        }
    }

    /// A strand's atom colours in a look (by atom index in the duplex).
    static func strandColour(_ d: BioDuplex, look: BiomolLook) -> (BioMoleculeModel.Atom) -> String? {
        { a in
            let atom = d.atoms[a.id]
            let part = BiomolNucleotide.part(atom.name)
            switch look.colourBy {
            case .element: return nil
            case .part: return part == BiomolParts.base ? BioPalette.base(atom.base).hex(dark: look.dark) : part.colour.hex(dark: look.dark)
            case .aaClass:
                if part == BiomolParts.base { return BioPalette.base(atom.base).hex(dark: look.dark) }
                return (atom.strand == 1 ? BioPalette.strand1 : BioPalette.strand2).hex(dark: look.dark)
            }
        }
    }

    /// A residue added at the C-terminus (dehydration synthesis): it slides in from the right as the water made from the
    /// last residue's OH and the new residue's N's H lifts off, and the new peptide bond flashes. The scene meanwhile
    /// leaves the new residue out (`PeptideOptions.hidden`).
    static func peptideAdded(_ p: BiomolPeptide, look: BiomolLook, chem: BiomolChem) -> BiomolMotion {
        guard p.count >= 2 else { return BiomolMotion() }
        let n = p.count - 1
        let (m, index) = p.model(hydrogens: true)
        let residueItems = itemsFor(m, keep: { p.atoms[index[$0.id]].residue == n }, look: look, chem: chem,
                                    colour: peptideColour(p, index: index, look: look))
        let bb = p.backbone
        let oxt = BioV.place(p.atoms[bb[n - 1].n].pos, p.atoms[bb[n - 1].ca].pos, p.atoms[bb[n - 1].c].pos, length: 1.25, angle: 117.0, torsion: p.psi[n - 1])
        let bond = sleeve([p.atoms[bb[n - 1].c].pos, p.atoms[bb[n].n].pos], id: "flash", hex: BiomolParts.bondHighlight.hex(dark: look.dark), radius: 0.3)
        return BiomolMotion(groups: [
            BiomolMotion.Group(items: residueItems, from: BioVec(7, 0, 0), to: BioVec(0, 0, 0), fromAlpha: 0, toAlpha: 1, delay: 0, duration: 0.42),
            BiomolMotion.Group(items: water(at: oxt, look: look, chem: chem), from: BioVec(0, 0, 0), to: BioVec(0, 3, 0), fromAlpha: 1, toAlpha: 0, delay: 0.3, duration: 0.6),
            BiomolMotion.Group(items: [bond], fromAlpha: 0, toAlpha: 1, delay: 0.32, duration: 0.6, pulse: true),
        ])
    }

    /// The last residue taken off (hydrolysis): water comes down to the peptide bond, which flashes, and the residue
    /// slides away to the right, leaving the chain its C-terminal OH. The scene meanwhile is the shorter peptide without
    /// its C-terminal OH (`PeptideOptions.hideTerminalOH`).
    static func peptideRemoved(old: BiomolPeptide, look: BiomolLook, chem: BiomolChem) -> BiomolMotion {
        guard old.count >= 2 else { return BiomolMotion() }
        let n = old.count - 1
        let (m, index) = old.model(hydrogens: true)
        let leaving = itemsFor(m, keep: { old.atoms[index[$0.id]].residue == n }, look: look, chem: chem,
                               colour: peptideColour(old, index: index, look: look))
        let bb = old.backbone
        let oxt = BioV.place(old.atoms[bb[n - 1].n].pos, old.atoms[bb[n - 1].ca].pos, old.atoms[bb[n - 1].c].pos, length: 1.25, angle: 117.0, torsion: old.psi[n - 1])
        let bond = sleeve([old.atoms[bb[n - 1].c].pos, old.atoms[bb[n].n].pos], id: "flash", hex: BiomolParts.bondHighlight.hex(dark: look.dark), radius: 0.3)
        return BiomolMotion(groups: [
            BiomolMotion.Group(items: water(at: oxt, look: look, chem: chem), from: BioVec(0, 3, 0), to: BioVec(0, 0, 0), fromAlpha: 0, toAlpha: 1, delay: 0, duration: 0.42),
            BiomolMotion.Group(items: [bond], fromAlpha: 0, toAlpha: 1, delay: 0.25, duration: 0.5, pulse: true),
            BiomolMotion.Group(items: leaving, from: BioVec(0, 0, 0), to: BioVec(7, 0, 0), fromAlpha: 1, toAlpha: 0, delay: 0.4, duration: 0.45),
        ])
    }

    /// The items of a strand's last nucleotide (and its partner), as the page colours them.
    private static func lastNucleotide(_ d: BioDuplex, look: BiomolLook, chem: BiomolChem) -> [BioSceneItem] {
        let n = d.sequence.count
        return itemsFor(d.model, keep: { d.atoms[$0.id].pair == n - 1 }, look: look, chem: chem, colour: strandColour(d, look: look))
    }

    /// A nucleotide added at the 3′ end: it slides down onto the strand as its phosphate bonds to the 3′-OH, the
    /// phosphodiester bond flashes, and water drifts away. The scene meanwhile leaves it out (`StrandOptions.hiddenPair`).
    static func strandAdded(_ d: BioDuplex, look: BiomolLook, chem: BiomolChem) -> BiomolMotion {
        guard d.sequence.count >= 2, let link = BiomolStrand.linkages(d, strand: 1).last else { return BiomolMotion() }
        let site = BiomolStrand.waterSite(d, linkage: link)
        let outward = BioV.unit(BioVec(site.x, 0, site.z))
        return BiomolMotion(groups: [
            BiomolMotion.Group(items: lastNucleotide(d, look: look, chem: chem), from: BioVec(0, 7, 0), to: BioVec(0, 0, 0), fromAlpha: 0, toAlpha: 1,
                               delay: 0, duration: 0.42),
            BiomolMotion.Group(items: [sleeve(link.map { d.atoms[$0].pos }, id: "flash", hex: BiomolParts.bondHighlight.hex(dark: look.dark), radius: 0.34)],
                               fromAlpha: 0, toAlpha: 1, delay: 0.32, duration: 0.6, pulse: true),
            BiomolMotion.Group(items: water(at: site, look: look, chem: chem), from: BioVec(0, 0, 0), to: outward * 3 + BioVec(0, 1, 0),
                               fromAlpha: 1, toAlpha: 0, delay: 0.3, duration: 0.6),
        ])
    }

    /// The 3′ nucleotide taken off (hydrolysis): water comes in, the bond flashes, and the nucleotide lifts away.
    static func strandRemoved(old d: BioDuplex, look: BiomolLook, chem: BiomolChem) -> BiomolMotion {
        guard d.sequence.count >= 2, let link = BiomolStrand.linkages(d, strand: 1).last else { return BiomolMotion() }
        let site = BiomolStrand.waterSite(d, linkage: link)
        let outward = BioV.unit(BioVec(site.x, 0, site.z))
        return BiomolMotion(groups: [
            BiomolMotion.Group(items: water(at: site, look: look, chem: chem), from: outward * 3, to: BioVec(0, 0, 0), fromAlpha: 0, toAlpha: 1, delay: 0, duration: 0.4),
            BiomolMotion.Group(items: [sleeve(link.map { d.atoms[$0].pos }, id: "flash", hex: BiomolParts.bondHighlight.hex(dark: look.dark), radius: 0.34)],
                               fromAlpha: 0, toAlpha: 1, delay: 0.25, duration: 0.5, pulse: true),
            BiomolMotion.Group(items: lastNucleotide(d, look: look, chem: chem), from: BioVec(0, 0, 0), to: BioVec(0, 7, 0), fromAlpha: 1, toAlpha: 0,
                               delay: 0.4, duration: 0.45),
        ])
    }

    /// A disaccharide split by water (hydrolysis) or joined with water released (dehydration synthesis): the two
    /// sugars move 4 Å apart (or together) along the glycosidic bond, the water arrives and is used up (or forms and
    /// drifts away), the new –OH and H fade in (or out) with their sugars, and the bond flashes. The scene meanwhile
    /// shows nothing but its frame (everything moves).
    static func disaccharideMotion(_ code: String, toSplit: Bool, look: BiomolLook, chem: BiomolChem) -> BiomolMotion {
        guard let mol = BioData.molecule(code), let g = BiomolSugar.glycosidic(mol), let c1 = mol.pos(g.anomeric), let og = mol.pos(g.o),
              let c2 = mol.pos(g.other), let s = BiomolSugar.split(mol) else { return BiomolMotion() }
        let d = BioV.unit(og - c1)
        let joined = mol.model(ph7: look.ph7)
        let res = joined.atoms.map { BiomolSugar.residue($0.name, in: mol, glycosidicO: g.o) }
        func colour(_ r: Int) -> String? { look.colourBy == .element ? nil : BiomolSugar.residueColours[r].hex(dark: look.dark) }
        let r0 = itemsFor(joined, keep: { res[$0.id] == 0 }, look: look, chem: chem, colour: { _ in colour(0) })
        let r1 = itemsFor(joined, keep: { res[$0.id] == 1 }, look: look, chem: chem, colour: { _ in colour(1) })
        // (the water's atoms where they end up, each with its bond to the sugar it joins)
        let newSet = Set(s.newAtoms)
        let highlight = look.colourBy == .element ? nil : BiomolParts.bondHighlight.hex(dark: look.dark)
        func newItems(_ r: Int) -> [BioSceneItem] {
            var items = itemsFor(s.model, keep: { newSet.contains($0.id) && s.residue[$0.id] == r }, look: look, chem: chem, colour: { _ in highlight })
            for b in s.model.bonds {
                let (x, y) = newSet.contains(b.a) ? (b.a, b.b) : (b.b, b.a)
                guard newSet.contains(x), !newSet.contains(y), s.residue[x] == r else { continue }
                let ax = s.model.atoms[x], ay = s.model.atoms[y]
                items += bondItems(ay.pos, ax.pos, hexA: colour(r) ?? chem.cpk(ay.element), hexB: highlight ?? chem.cpk(ax.element), look: look)
            }
            return items
        }
        let new0 = newItems(0), new1 = newItems(1)
        let glyco = sleeve([c1, og, c2], id: "flash", hex: BiomolParts.bondHighlight.hex(dark: look.dark), radius: 0.3)
        var side = BioV.cross(d, BioVec(0, 0, 1))
        if BioV.length(side) < 0.2 { side = BioV.cross(d, BioVec(0, 1, 0)) }
        side = BioV.unit(side)
        let site = (c1 + og) / 2 + side * 1.6
        if toSplit {
            return BiomolMotion(groups: [
                BiomolMotion.Group(items: water(at: site, look: look, chem: chem), from: side * 4, to: BioVec(0, 0, 0), fromAlpha: 0, toAlpha: 1, delay: 0,
                                   duration: 0.4, fadeOutAt: 0.45),
                BiomolMotion.Group(items: [glyco], fromAlpha: 0, toAlpha: 1, delay: 0.2, duration: 0.4, pulse: true),
                BiomolMotion.Group(items: r0, from: BioVec(0, 0, 0), to: d * -2, delay: 0.4, duration: 0.5),
                BiomolMotion.Group(items: r1, from: BioVec(0, 0, 0), to: d * 2, delay: 0.4, duration: 0.5),
                BiomolMotion.Group(items: new0, from: d * 2, to: BioVec(0, 0, 0), fromAlpha: 0, toAlpha: 1, delay: 0.4, duration: 0.5),
                BiomolMotion.Group(items: new1, from: d * -2, to: BioVec(0, 0, 0), fromAlpha: 0, toAlpha: 1, delay: 0.4, duration: 0.5),
            ])
        }
        return BiomolMotion(groups: [
            BiomolMotion.Group(items: r0, from: d * -2, to: BioVec(0, 0, 0), delay: 0, duration: 0.5),
            BiomolMotion.Group(items: r1, from: d * 2, to: BioVec(0, 0, 0), delay: 0, duration: 0.5),
            BiomolMotion.Group(items: new0, from: BioVec(0, 0, 0), to: d * 2, fromAlpha: 1, toAlpha: 0, delay: 0, duration: 0.5),
            BiomolMotion.Group(items: new1, from: BioVec(0, 0, 0), to: d * -2, fromAlpha: 1, toAlpha: 0, delay: 0, duration: 0.5),
            BiomolMotion.Group(items: water(at: site, look: look, chem: chem), from: BioVec(0, 0, 0), to: side * 4, fromAlpha: 1, toAlpha: 0, delay: 0.45, duration: 0.6),
            BiomolMotion.Group(items: [glyco], fromAlpha: 0, toAlpha: 1, delay: 0.45, duration: 0.5, pulse: true),
        ])
    }

    /// The ball a disaccharide's scenes are framed in, joined or split (so the camera holds still as it splits).
    static func disaccharideFrame(_ code: String) -> BioSphere? {
        guard let mol = BioData.molecule(code), let s = BiomolSugar.split(mol) else { return nil }
        let b = BioV.boundingSphere(mol.atoms.map(\.pos) + s.model.atoms.map(\.pos))
        return BioSphere(centre: b.centre, radius: b.radius + 1.2)
    }
}

// MARK: - Open in Molecule Builder

/// A molecule handed to the chemistry builder (§4.4 shared controls: "Open in Molecule Builder", its VSEPR relax not run
/// on it): its title, then a line an atom ("atom C x y z") and a line a bond ("bond i j order"), atoms numbered from 0 —
/// the text the Periodic Table tool's builder reads (MoleculeModel.load(handOff:)).
enum BiomolHandOff {
    static func text(_ m: BioMoleculeModel, title: String, hydrogens: Bool = true) -> String {
        var lines = ["title \(title)"]
        var map: [Int: Int] = [:]
        for a in m.atoms where hydrogens || a.element != "H" {
            map[a.id] = map.count
            lines.append("atom \(a.element) " + String(format: "%.3f %.3f %.3f", a.pos.x, a.pos.y, a.pos.z))
        }
        for b in m.bonds {
            if let x = map[b.a], let y = map[b.b] { lines.append("bond \(x) \(y) \(b.order)") }
        }
        return lines.joined(separator: "\n")
    }
}
