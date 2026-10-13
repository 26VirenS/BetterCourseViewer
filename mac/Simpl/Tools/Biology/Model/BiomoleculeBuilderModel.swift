import Foundation

// Biomolecule Builder's own model (spec §4.4, §6.4): what the tool works out without a window — the peptide builder
// (BiomolPeptide.swift), the cartoons and the structure levels (BiomolProteins.swift), the monomers' forms and parts,
// the strand, sugar and lipid builders (BiomolMolecules.swift), and the scenes the view shows (BiomolScenes.swift) —
// Foundation only, so the Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of
// Biology/Model. The self-check (BioSelfCheck.run) calls `biomolChecks` with the others.

/// What the sequence fields take (§3.7's cleaning): a peptide's one-letter codes (the 20, up to 40) and a strand's DNA
/// letters (A, C, G, T, up to 12), with the inline message when something is left out.
enum BiomolInput {
    static func protein(_ text: String) -> (sequence: String, message: String?) {
        let input = BioSequenceInput(text, as: .protein, cap: BiomolPeptideBuilder.maxResidues)
        var bad: [(letter: Character, positions: [Int])] = input.invalid
        for c in ["X", "*"].map({ Character($0) }) {
            let at = input.letters.enumerated().filter { $0.element == c }.map { $0.offset + 1 }
            if !at.isEmpty { bad.append((c, at)) }
        }
        let seq = String(input.letters.filter { BioAminoAcids.byOne[$0] != nil })
        if !bad.isEmpty {
            let what = bad.map { "“\($0.letter)” at \($0.positions.prefix(6).map(String.init).joined(separator: ", "))" }.joined(separator: "; ")
            return (seq, "Use the 20 amino-acid letters. Left out: \(what).")
        }
        if input.truncated { return (seq, "Only the first \(BiomolPeptideBuilder.maxResidues) residues are built.") }
        return (seq, nil)
    }

    static func dna(_ text: String) -> (sequence: String, message: String?) {
        let input = BioSequenceInput(text, as: .dna, cap: BiomolStrand.maxLength)
        let seq = String(input.letters.filter { "ACGT".contains($0) })
        if input.suggestion == .rna { return (seq, "This strand is DNA: use T, not U (RNA's U is left out).") }
        var bad = input.invalid
        let ns = input.letters.enumerated().filter { $0.element == "N" }.map { $0.offset + 1 }
        if !ns.isEmpty { bad.append(("N", ns)) }
        if !bad.isEmpty {
            let what = bad.map { "“\($0.letter)” at \($0.positions.prefix(6).map(String.init).joined(separator: ", "))" }.joined(separator: "; ")
            return (seq, "A strand is built from A, C, G and T. Left out: \(what).")
        }
        if input.truncated { return (seq, "Only the first \(BiomolStrand.maxLength) nucleotides are built.") }
        return (seq, nil)
    }
}

/// Texts the pages show.
enum BiomolText {
    /// A formula with subscript digits: "C9H11NO2" or "C10 H14 N5 O6 P" → "C₉H₁₁NO₂", "C₁₀H₁₄N₅O₆P".
    static func formula(_ f: String) -> String {
        let sub: [Character: Character] = ["0": "₀", "1": "₁", "2": "₂", "3": "₃", "4": "₄", "5": "₅", "6": "₆", "7": "₇", "8": "₈", "9": "₉"]
        return String(f.filter { $0 != " " }.map { sub[$0] ?? $0 })
    }

    /// A signed whole number: "+1", "0", "−2".
    static func signed(_ n: Int) -> String { n > 0 ? "+\(n)" : (n < 0 ? "−\(-n)" : "0") }

    /// An element's name for an atom card.
    static func element(_ symbol: String) -> String {
        ["H": "Hydrogen", "C": "Carbon", "N": "Nitrogen", "O": "Oxygen", "P": "Phosphorus", "S": "Sulfur", "Fe": "Iron"][symbol] ?? symbol
    }

    /// The atom an id names ("…:atom:31" → 31).
    static func atomIndex(_ id: String?) -> Int? {
        guard let id, let r = id.range(of: ":atom:", options: .backwards) else {
            if let id, id.hasPrefix("atom:") { return Int(id.dropFirst(5)) }
            return nil
        }
        return Int(id[r.upperBound...])
    }
}

/// The app's motion curve for the builder's short motions: a strong ease-out, cubic-bezier(0.23, 1, 0.32, 1).
enum BiomolEase {
    static func strongOut(_ t: Double) -> Double { cubicBezier(0.23, 1, 0.32, 1, t) }

    /// y at x = t on the cubic Bézier from (0, 0) to (1, 1) with control points (x1, y1) and (x2, y2) (Newton's method
    /// on x, then bisection if it stalls).
    static func cubicBezier(_ x1: Double, _ y1: Double, _ x2: Double, _ y2: Double, _ t: Double) -> Double {
        let x = min(max(t, 0), 1)
        func bez(_ s: Double, _ a: Double, _ b: Double) -> Double { 3 * (1 - s) * (1 - s) * s * a + 3 * (1 - s) * s * s * b + s * s * s }
        func slope(_ s: Double, _ a: Double, _ b: Double) -> Double { 3 * (1 - s) * (1 - s) * a + 6 * (1 - s) * s * (b - a) + 3 * s * s * (1 - b) }
        var s = x
        for _ in 0..<8 {
            let dx = bez(s, x1, x2) - x
            let d = slope(s, x1, x2)
            if abs(dx) < 1e-7 { return bez(s, y1, y2) }
            if abs(d) < 1e-6 { break }
            s -= dx / d
        }
        var lo = 0.0, hi = 1.0
        s = x
        for _ in 0..<40 {
            let v = bez(s, x1, x2)
            if abs(v - x) < 1e-7 { break }
            if v < x { lo = s } else { hi = s }
            s = (lo + hi) / 2
        }
        return bez(s, y1, y2)
    }
}

extension BioSelfCheck {
    /// Biomolecule Builder's own `bio-check` lines, against §6.4's test vectors and §10.1 item 3–4.
    static func biomolChecks(_ r: inout BioCheckReport) {
        peptideChecks(&r)
        monomerChecks(&r)
        sugarLipidChecks(&r)
        sceneChecks(&r)
    }

    // MARK: Amino acids and nucleotides

    static func monomerChecks(_ r: inout BioCheckReport) {
        // (each free amino acid's neutral form has §6.2's formula; its pH 7 form the same less an H for Asp and Glu, plus
        // one for Lys and Arg, its net charge 0, −1 or +1 as §6.2 says)
        var formulaWrong: [String] = [], chargeWrong: [String] = []
        for aa in BioAminoAcids.all {
            guard let t = BiomolAminoAcid.template(aa.one) else { formulaWrong.append(aa.three); continue }
            var want: [String: Int] = [:]
            var el = "", digits = ""
            func flush() { if !el.isEmpty { want[el, default: 0] += Int(digits) ?? 1 }; el = ""; digits = "" }
            for ch in aa.formula {
                if ch.isUppercase { flush(); el = String(ch) } else if ch.isLowercase { el.append(ch) } else if ch.isNumber { digits.append(ch) }
            }
            flush()
            var have: [String: Int] = [:]
            for a in BiomolAminoAcid.atoms(t, ph7: false) { have[a.element, default: 0] += 1 }
            if have != want { formulaWrong.append(aa.three) }
            let h7 = BiomolAminoAcid.atoms(t, ph7: true).filter { $0.element == "H" }.count
            let shift = aa.one == "D" || aa.one == "E" ? -1 : (aa.one == "K" || aa.one == "R" ? 1 : 0)
            let net = BiomolAminoAcid.charges(aa.one, ph7: true).values.reduce(0, +)
            if h7 != (want["H"] ?? 0) + shift || net != aa.charge { chargeWrong.append("\(aa.three)\(net)") }
        }
        r.check("biomol.aa-formulas", formulaWrong.isEmpty ? "20" : formulaWrong.joined(separator: ","), formulaWrong.isEmpty)
        r.check("biomol.aa-pH7-charge", chargeWrong.isEmpty ? "20" : chargeWrong.joined(separator: ","), chargeWrong.isEmpty)
        // (CORN: every L-amino acid's CO → R → N runs clockwise seen from its Hα; the mirror image counter-clockwise)
        var cornWrong: [String] = []
        for aa in BioAminoAcids.all where aa.isChiral {
            let l = BiomolAminoAcid.cornClockwise(BiomolAminoAcid.model(aa.one, ph7: true))
            let d = BiomolAminoAcid.cornClockwise(BiomolAminoAcid.model(aa.one, ph7: true, mirrored: true))
            if l != true || d != false { cornWrong.append(aa.three) }
        }
        let gly = BiomolAminoAcid.cornClockwise(BiomolAminoAcid.model("G", ph7: true))
        r.check("biomol.CORN", cornWrong.isEmpty ? "19L" : cornWrong.joined(separator: ","), cornWrong.isEmpty && gly == nil)
        // (the parts: every amino acid has all four; glycine's R group is one H; proline's N is in its amino group)
        var partsWrong: [String] = []
        for aa in BioAminoAcids.all {
            guard let t = BiomolAminoAcid.template(aa.one) else { continue }
            let ps = Set(t.atoms.map { BiomolAminoAcid.part($0.name, in: t).id })
            if ps != ["amino", "alpha", "carboxyl", "r"] { partsWrong.append(aa.three) }
        }
        if let g = BiomolAminoAcid.template("G") {
            let rg = g.atoms.filter { BiomolAminoAcid.part($0.name, in: g) == BiomolParts.rGroup }.map(\.name)
            if rg.count != 1 { partsWrong.append("GLY-R\(rg.count)") }
        }
        if let p = BiomolAminoAcid.template("P"), BiomolAminoAcid.part("N", in: p) != BiomolParts.amino { partsWrong.append("PRO-N") }
        r.check("biomol.aa-parts", partsWrong.isEmpty ? "20" : partsWrong.joined(separator: ","), partsWrong.isEmpty)
        // (the R-group sketches: one per amino acid, bonds about one unit long, the R group above CA)
        var sketchWrong: [String] = []
        for aa in BioAminoAcids.all {
            let s = BiomolAminoAcid.sketch(aa.one)
            let lengths = s.bonds.map { b in ((s.atoms[b.a].x - s.atoms[b.b].x) * (s.atoms[b.a].x - s.atoms[b.b].x) + (s.atoms[b.a].y - s.atoms[b.b].y) * (s.atoms[b.a].y - s.atoms[b.b].y)).squareRoot() }
            let up = s.atoms.filter { !$0.attachment }.map(\.y).reduce(0, +)
            if s.atoms.count < 2 || lengths.contains(where: { $0 < 0.35 || $0 > 1.3 }) || up <= 0 { sketchWrong.append(aa.three) }
        }
        r.check("biomol.aa-sketches", sketchWrong.isEmpty ? "20" : sketchWrong.joined(separator: ","), sketchWrong.isEmpty)
        // (nucleotides: each has a phosphate, a sugar and a base; deoxy ones have no O2′; the strand's linkages)
        var ntWrong: [String] = []
        for code in BiomolNucleotide.codes {
            guard let m = BioData.molecule(code) else { ntWrong.append(code); continue }
            let ps = Set(m.atoms.map { BiomolNucleotide.part($0.name).id })
            let hasO2 = m.atom("O2'") != nil
            if ps != ["phosphate", "sugar", "base"] || hasO2 == BiomolNucleotide.isDeoxy(code) { ntWrong.append(code) }
        }
        r.check("biomol.nucleotide-parts", ntWrong.isEmpty ? "9" : ntWrong.joined(separator: ","), ntWrong.isEmpty)
        let d = BiomolStrand.build("ATGCGT", complement: true)
        let links = BiomolStrand.linkages(d, strand: 1) + BiomolStrand.linkages(d, strand: 2)
        let op = links.map { BioV.distance(d.atoms[$0[1]].pos, d.atoms[$0[2]].pos) }
        r.check("biomol.strand", "\(links.count)links,\(d.hBonds.count)H-bonds," + String(format: "O3'P%.3f", op.first ?? 0),
                links.count == 10 && op.allSatisfy { abs($0 - 1.58) < 0.005 } && d.hBonds.count == 2 + 2 + 3 + 3 + 3 + 2)
    }

    // MARK: Sugars and lipids

    static func sugarLipidChecks(_ r: inout BioCheckReport) {
        // (α-glucose's C1 OH is trans to C6, β-glucose's, galactose's, fructose's and ribose's cis; the rings' sizes)
        var anomers: [String] = []
        for code in BiomolSugar.monosaccharides {
            guard let m = BioData.molecule(code), let ring = BiomolSugar.ring(m) else { anomers.append("\(code)?"); continue }
            anomers.append("\(code)\(BiomolSugar.isAlpha(m, ring) ? "α" : "β")\(ring.atoms.count)")
        }
        r.same("biomol.anomers", anomers.joined(separator: ","), "GLCα6,BGCβ6,GALβ6,FRUβ5,BDRβ5")
        // (each disaccharide's glycosidic O, and its linkage's α or β read from the atoms)
        var links: [String] = []
        for code in BiomolSugar.disaccharides {
            guard let m = BioData.molecule(code), let g = BiomolSugar.glycosidic(m), let ring = BiomolSugar.ring(m) else { links.append("\(code)?"); continue }
            var text = "\(code):\(g.o):\(g.anomeric)-\(g.other):\(BiomolSugar.isAlpha(m, ring) ? "α" : "β")"
            if code == "SUC", let fr = BiomolSugar.ring(m, primed: true) { text += BiomolSugar.isAlpha(m, fr) ? "α" : "β" }
            links.append(text)
        }
        r.same("biomol.glycosidic", links.joined(separator: ","), "MAL:O1:C1-C4':α,CBI:O4':C1-C4':β,SUC:O1:C1-C2':αβ,LAT:O1:C1-C4':β")
        // (hydrolysis: two monosaccharides' atoms, C6H12O6 each; the sugars 4 Å further apart; the new O 1.43 Å from C1)
        var splitWrong: [String] = []
        for code in BiomolSugar.disaccharides {
            guard let m = BioData.molecule(code), let g = BiomolSugar.glycosidic(m), let s = BiomolSugar.split(m) else { splitWrong.append(code); continue }
            var counts: [String: Int] = [:]
            for a in s.model.atoms { counts[a.element, default: 0] += 1 }
            let c1 = s.model.atoms.first { $0.name == g.anomeric }?.pos ?? BioVec(0, 0, 0)
            let og = s.model.atoms.first { $0.name == g.o }?.pos ?? BioVec(0, 0, 0)
            let ow = s.model.atoms.first { $0.name == "O (water)" }?.pos ?? BioVec(0, 0, 0)
            let before = BioV.distance(m.pos(g.anomeric) ?? c1, m.pos(g.o) ?? og)
            if counts != ["C": 12, "H": 24, "O": 12] || abs(BioV.distance(c1, og) - before - 4) > 1e-6 || abs(BioV.distance(c1, ow) - 1.43) > 1e-6
                || !s.model.oddBonds().isEmpty { splitWrong.append(code) }
        }
        r.check("biomol.hydrolysis", splitWrong.isEmpty ? "4" : splitWrong.joined(separator: ","), splitWrong.isEmpty)
        // (lipids: oleic acid bends at its cis double bond, stearic acid does not; tristearin's 3 ester bonds and POPC's 2
        // are bonds; cholesterol has four rings)
        let ola = BioData.molecule("OLA").flatMap(BiomolLipid.kinkAngle) ?? 0
        let ste = BioData.molecule("STE").flatMap(BiomolLipid.kinkAngle) ?? 99
        r.check("biomol.oleic-kink", String(format: "%.0f/%.0f", ola, ste), ola > 25 && ste < 10)
        var esterWrong: [String] = []
        for code in ["TGL", "POV", "OLA"] {
            guard let m = BioData.molecule(code) else { continue }
            for (a, b) in BiomolLipid.esterBonds(code) + BiomolLipid.cisBonds(code) {
                if !m.bonds.contains(where: { Set([$0.a, $0.b]) == Set([a, b]) }) { esterWrong.append("\(code)\(a)-\(b)") }
            }
        }
        let clr = BioData.molecule("CLR")
        let heavy = clr?.atoms.filter { $0.element != "H" }.map(\.name) ?? []
        let heavyBonds = clr?.bonds.filter { heavy.contains($0.a) && heavy.contains($0.b) }.count ?? 0
        let rings = heavyBonds - heavy.count + 1
        r.check("biomol.lipid-bonds", esterWrong.isEmpty ? "esters,rings\(rings)" : esterWrong.joined(separator: ","), esterWrong.isEmpty && rings == 4)
        var headTail: [String] = []
        for code in BiomolLipid.codes {
            guard let m = BioData.molecule(code) else { continue }
            let d = m.atoms.map { BiomolLipid.detail($0.name, in: m) }
            let philic = d.filter(BiomolLipid.isHydrophilic).count, phobic = d.count - philic
            if code == "GOL" ? phobic != 0 : (philic == 0 || phobic == 0) { headTail.append(code) }
        }
        r.check("biomol.lipid-parts", headTail.isEmpty ? "7" : headTail.joined(separator: ","), headTail.isEmpty)
        // (the polysaccharide diagrams: their glyphs, links and branches)
        let p = BiomolPolysaccharide.allCases.map { k -> String in
            let l = k.layout
            return "\(l.glyphs.count)/\(l.links.filter { $0.kind == .branch }.count)/\(l.links.filter { $0.kind == .hBond }.count)"
        }
        r.same("biomol.polysaccharides", p.joined(separator: ","), "12/0/0,17/1/0,23/3/0,24/0/8,8/0/0")
    }

    // MARK: Scenes

    static func sceneChecks(_ r: inout BioCheckReport) {
        let chem = BiomolChem.plain
        var look = BiomolLook()
        // (every amino acid's scene: an item per atom, ids its parts', four labels)
        var aaWrong: [String] = []
        for aa in BioAminoAcids.all {
            let c = BiomolScenes.aminoAcid(aa.one, look: look, mirrored: false, chem: chem)
            let atoms = c.items.filter { ($0.id ?? "").contains(":atom:") }.count
            if atoms != BiomolAminoAcid.model(aa.one, ph7: true).atoms.count || c.labels.count != 4 { aaWrong.append(aa.three) }
        }
        r.check("biomol.scene-amino", aaWrong.isEmpty ? "20" : aaWrong.joined(separator: ","), aaWrong.isEmpty)
        // (the default peptide, coloured by part, with H-bonds and the ribbon over it: atoms without H, peptide bonds,
        // its helix's H-bonds, a cartoon piece per segment)
        look.hydrogens = false
        look.colourBy = .part
        let seq = BiomolPeptideBuilder.defaultSequence
        let p = BiomolPeptideBuilder.build(seq, ss: BiomolSS.parse("CCHHHHHHHHHHHHCEEEE", count: seq.count))
        let c = BiomolScenes.peptide(p, look: look, options: BiomolScenes.PeptideOptions(hBonds: true, ribbon: .over), chem: chem)
        let atoms = c.items.filter { ($0.id ?? "").contains(":atom:") }.count
        let pbonds = c.items.filter { ($0.id ?? "").hasPrefix("pbond:") }.count
        let hb = c.items.filter { ($0.id ?? "").hasPrefix("hbond:") }.count
        let cartoon = c.items.filter { ($0.id ?? "").hasSuffix(":cartoon") }.count
        r.check("biomol.scene-peptide", "\(atoms)atoms,\(pbonds)pbonds,\(hb)H-bonds,\(cartoon)cartoon,\(c.labels.count)labels",
                atoms == p.atoms.filter { $0.element != "H" }.count && pbonds == seq.count - 1 && hb == p.hBonds.count && hb >= 8 && cartoon == 4
                && c.labels.count <= 14)
        // (the structure levels: ubiquitin's 76 centroids, haemoglobin's 4 chains and 4 hemes; denaturing morphs mesh to
        // matching mesh)
        let t = BiomolProteins.tertiary(dark: false, show: .core)
        let q = BiomolProteins.quaternary(dark: false)
        let hemes = q.items.filter { ($0.id ?? "").hasSuffix(":disk") }.count
        let chains = Set(q.items.compactMap { $0.id }.filter { $0.hasPrefix("chain:") }.map { $0.split(separator: ":")[1] })
        let den = BiomolProteins.denatureMeshes(dark: false)
        let matching = den.parts.allSatisfy { $0.native.vertexCount == $0.coil.vertexCount && $0.native.indices == $0.coil.indices }
        let coil = BiomolProteins.randomCoil(BiomolProteins.ubiquitinCA)
        let steps = zip(coil, coil.dropFirst()).map { BioV.distance($0, $1) }
        r.check("biomol.scene-levels", "\(t.items.filter { ($0.id ?? "").hasPrefix("res:") }.count)centroids,\(chains.count)chains,\(hemes)hemes",
                t.items.filter { ($0.id ?? "").hasPrefix("res:") }.count == 76 && chains.count == 4 && hemes == 4 && matching
                && steps.allSatisfy { abs($0 - 3.8) < 1e-9 } && coil.count == 76)
        let sec = BiomolScenes.secondary(look: look, chem: chem)
        let secH = sec.items.filter { ($0.id ?? "").contains("hbond") }.count
        r.check("biomol.scene-secondary", "\(secH)H-bonds", secH == 8 + 14)
        // (the other pages' scenes are not empty; the motions have their groups)
        look.hydrogens = true
        let counts = [BiomolScenes.nucleotide("ATP", look: look, chem: chem).items.count,
                      BiomolScenes.strand(BiomolStrand.build("ATGC", complement: true), look: look, chem: chem).items.count,
                      BiomolScenes.monosaccharide("GLC", look: look, chem: chem).items.count,
                      BiomolScenes.disaccharide("SUC", split: true, look: look, chem: chem).items.count,
                      BiomolScenes.lipid("POV", look: look, chem: chem).items.count,
                      BiomolScenes.lipid("STE+OLA", look: look, chem: chem).items.count]
        let motions = [BiomolScenes.peptideAdded(p, look: look, chem: chem).groups.count,
                       BiomolScenes.peptideRemoved(old: p, look: look, chem: chem).groups.count,
                       BiomolScenes.strandAdded(BiomolStrand.build("ATGC", complement: false), look: look, chem: chem).groups.count,
                       BiomolScenes.disaccharideMotion("MAL", toSplit: true, look: look, chem: chem).groups.count]
        r.check("biomol.scenes", counts.map(String.init).joined(separator: ",") + "|" + motions.map(String.init).joined(separator: ","),
                counts.allSatisfy { $0 > 20 } && motions == [3, 3, 3, 6])
        // (the fields' cleaning and the motion curve)
        let pin = BiomolInput.protein("mkt ay1x*b")
        let din = BiomolInput.dna("atgun")
        r.check("biomol.input", pin.sequence + "|" + din.sequence, pin.sequence == "MKTAY" && pin.message?.contains("“X” at 6") == true
                && pin.message?.contains("“B” at 8") == true && din.sequence == "ATG" && din.message != nil
                && BiomolInput.protein(String(repeating: "A", count: 45)).sequence.count == 40)
        r.same("biomol.text", BiomolText.formula("C10 H14 N5 O6 P") + "," + BiomolText.signed(-2) + "," + String(BiomolText.atomIndex("ss:1:res:3:atom:31") ?? -1),
               "C₁₀H₁₄N₅O₆P,−2,31")
        let ease = [0.0, 0.5, 1.0].map(BiomolEase.strongOut)
        r.check("biomol.ease", ease.map { String(format: "%.3f", $0) }.joined(separator: ","), ease[0] == 0 && abs(ease[2] - 1) < 1e-6 && ease[1] > 0.85)
        let text = BiomolHandOff.text(BiomolAminoAcid.model("W", ph7: true), title: "Tryptophan")
        let lines = text.split(separator: "\n")
        r.check("biomol.hand-off", "\(lines.count)lines", lines.first == "title Tryptophan" && lines.filter { $0.hasPrefix("atom ") }.count == 27
                && lines.filter { $0.hasPrefix("bond ") }.count == 28)
    }

    // MARK: The peptide builder (§6.4, §10.1 items 3 and 4)

    static func peptideChecks(_ r: inout BioCheckReport) {
        // (Ala₄ as an α-helix against the test vector: every listed atom within 0.002 Å)
        let ala4 = BiomolPeptideBuilder.build("AAAA", ss: Array(repeating: .helix, count: 4), ph7: true)
        var worst = 0.0, missing: [String] = []
        var listed = 0
        for line in BioData.peptideTestText.split(separator: "\n").map(String.init) {
            let f = line.split(separator: " ").map(String.init)
            guard f.count == 6, f[1] == "ALA", let i = Int(f[0]), let x = Double(f[3]), let y = Double(f[4]), let z = Double(f[5]) else { continue }
            listed += 1
            guard let k = ala4.atom(i, f[2]) else { missing.append("\(i)\(f[2])"); continue }
            worst = max(worst, BioV.distance(ala4.atoms[k].pos, BioVec(x, y, z)))
        }
        r.check("biomol.Ala4-helix", String(format: "%ld,worst%.4f", listed, worst), listed == 25 && missing.isEmpty && worst <= 0.002)

        // (poly-Ala₂₀: 3.62 residues a turn, 1.56 Å a residue, right-handed; O(i)···N(i+4) 3.10 Å for every i)
        let ala20 = BiomolPeptideBuilder.build(String(repeating: "A", count: 20), ss: Array(repeating: .helix, count: 20))
        let hp = BiomolPeptideBuilder.helixParameters(ala20.caTrace)
        r.near("biomol.helix-per-turn", hp.perTurn, 3.62, 0.01, digits: 3)
        r.near("biomol.helix-rise", hp.rise, 1.56, 0.01, digits: 3)
        r.near("biomol.helix-radius", hp.radius, 2.28, 0.01, digits: 3)
        var on: [Double] = []
        for i in 0..<16 {
            on.append(BioV.distance(ala20.atoms[ala20.backbone[i].o].pos, ala20.atoms[ala20.backbone[i + 4].n].pos))
        }
        let onOff = on.map { abs($0 - 3.10) }.max() ?? 1
        r.check("biomol.helix-O(i)N(i+4)", String(format: "%ld,%.3f", on.count, on.first ?? 0), on.count == 16 && onOff <= 0.01)
        let hb = ala20.hBonds
        r.check("biomol.helix-H-bonds", "\(hb.count)", hb.count == 16 && hb.allSatisfy { $0.donor - $0.acceptor == 4 })
        let caca = zip(ala20.caTrace, ala20.caTrace.dropFirst()).map { BioV.distance($0, $1) }
        r.near("biomol.CA-CA", caca.reduce(0, +) / Double(caca.count), 3.80, 0.01, digits: 3)
        // (a β-strand rises 3.47 Å a residue)
        let strand = BiomolPeptideBuilder.build(String(repeating: "A", count: 12), ss: Array(repeating: .strand, count: 12))
        let st = strand.caTrace
        r.near("biomol.strand-rise", BioV.distance(st[0], st[10]) / 10, 3.47, 0.02, digits: 3)

        // (the β-sheet: S2's CA atoms against the test vector; 8 S1–S2 H-bonds of 2.90 Å, 6 between S2 and S3)
        let sheet = BiomolSheet.build("GGGGGGG")
        var s2worst = 0.0, s2listed = 0
        var section = 0
        for line in BioData.peptideTestText.split(separator: "\n").map(String.init) {
            if line.hasPrefix("Gly7") { section = 1; continue }
            if line.hasPrefix("S2") { section = 2; continue }
            let f = line.split(separator: " ").map(String.init)
            guard section == 2, f.count == 5, f[1] == "CA", let i = Int(f[0]), let x = Double(f[2]), let y = Double(f[3]), let z = Double(f[4]) else { continue }
            s2listed += 1
            s2worst = max(s2worst, BioV.distance(sheet.strands[1].caTrace[i], BioVec(x, y, z)))
        }
        r.check("biomol.sheet-S2", String(format: "%ld,worst%.4f", s2listed, s2worst), s2listed == 7 && s2worst <= 0.002)
        let h12 = sheet.hBonds.filter { Set([$0.donor, $0.acceptor]) == [0, 1] }.map(\.bond.distance)
        let h23 = sheet.hBonds.filter { Set([$0.donor, $0.acceptor]) == [1, 2] }.map(\.bond.distance)
        r.check("biomol.sheet-H-bonds", String(format: "%ld+%ld,%.3f-%.3f", h12.count, h23.count, h12.min() ?? 0, h12.max() ?? 0),
                h12.count == 8 && h12.allSatisfy { abs($0 - 2.90) <= 0.01 } && h23.count == 6)

        // (the default peptide as a helix: the clash relief leaves no heavy atoms of different residues closer than
        // 2.6 Å — but for backbone neighbours and the backbone N···O pairs — and every bond 0.9–1.9 Å)
        let seq = BiomolPeptideBuilder.defaultSequence
        let helix = BiomolPeptideBuilder.build(seq, ss: Array(repeating: .helix, count: seq.count))
        r.check("biomol.default-bonds", "\(helix.atoms.count)atoms", helix.model().model.oddBonds().isEmpty && helix.atoms.count > 300)
        let contact = worstContact(helix)
        r.check("biomol.clash-helix", String(format: "%.2f", contact), contact >= 2.6)
        let beta = BiomolPeptideBuilder.build(seq, ss: Array(repeating: .strand, count: seq.count))
        let contactB = worstContact(beta)
        r.check("biomol.clash-strand", String(format: "%.2f", contactB), contactB >= 2.6)
        // (proline: four into a helix it breaks it; the first three it does not)
        let pro = BiomolPeptideBuilder.build("AAAAPAAAAA", ss: Array(repeating: .helix, count: 10))
        let early = BiomolPeptideBuilder.build("AAPAAA", ss: Array(repeating: .helix, count: 6))
        r.check("biomol.proline-rule", "\(pro.prolineBreaks)" + BiomolSS.text(pro.built),
                pro.prolineBreaks == [4] && pro.built[3] == .coil && pro.built[4] == .coil && pro.built[5] == .helix && early.prolineBreaks.isEmpty
                && pro.atom(4, "H") == nil)
        // (a Pro in a strand keeps its ring's φ: its CD no nearer the C before it than the bonded 1–3 distance)
        let proStrand = BiomolPeptideBuilder.build("AAAPAAA", ss: Array(repeating: .strand, count: 7))
        let cd = proStrand.atom(3, "CD").map { BioV.distance(proStrand.atoms[$0].pos, proStrand.atoms[proStrand.backbone[2].c].pos) } ?? 0
        r.check("biomol.proline-strand", String(format: "phi%.0f,C-CD%.2f", proStrand.phi[3], cd), proStrand.phi[3] == -75 && cd > 2.2)
        // (the forms: Ala₃ at pH 7 has H3 on its N-terminus and no HXT; the neutral one HXT and no H3; Lys's NZ keeps
        // three H at pH 7 and two in the neutral form)
        let k7 = BiomolPeptideBuilder.build("KAK", ss: [.coil, .coil, .coil], ph7: true)
        let k0 = BiomolPeptideBuilder.build("KAK", ss: [.coil, .coil, .coil], ph7: false)
        let hz7 = k7.atoms.filter { $0.name.hasPrefix("HZ") }.count, hz0 = k0.atoms.filter { $0.name.hasPrefix("HZ") }.count
        let hxt = k0.atom(2, "HXT").map { BioV.distance(k0.atoms[$0].pos, k0.atoms[k0.atom(2, "OXT") ?? 0].pos) } ?? 0
        r.check("biomol.forms", "HZ\(hz7)/\(hz0)," + String(format: "OH%.2f", hxt),
                k7.atom(0, "H3") != nil && k7.atom(2, "HXT") == nil && k0.atom(0, "H3") == nil && hz7 == 6 && hz0 == 4 && abs(hxt - 0.97) < 0.02)
    }

    /// The closest approach of heavy atoms in different residues, but for backbone atoms of neighbouring residues and the
    /// backbone's own N···O pairs (the script's measure).
    static func worstContact(_ p: BiomolPeptide) -> Double {
        let heavy = p.atoms.filter { $0.element != "H" }
        let bb: Set<String> = ["N", "CA", "C", "O"]
        var w = 9.0
        for x in 0..<heavy.count {
            for y in (x + 1)..<heavy.count {
                let a = heavy[x], b = heavy[y]
                let di = abs(a.residue - b.residue)
                if di == 0 { continue }
                if di == 1 && bb.contains(a.name) && bb.contains(b.name) { continue }
                if (a.name == "N" || a.name == "O") && (b.name == "N" || b.name == "O") { continue }
                w = min(w, BioV.distance(a.pos, b.pos))
            }
        }
        return w
    }
}
