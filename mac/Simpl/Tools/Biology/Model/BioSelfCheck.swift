import Foundation

// The biology toolkit's self-check (spec §10.1): the shared data and algorithms recomputed and compared with the
// specification's test vectors, a line each — `bio-check <name> <value> <OK|FAIL>` — on the console with
// -SimplBioCheck YES (the screenshot suite's console files keep it), and run whole by the Mac app's workflow (Models,
// scripts/dev/swift-checks), which fails on any FAIL. Each tool adds its own checks in its own model file
// (BioSelfCheck.cellChecks … energyChecks), so the tools' builders never edit this one.

/// The self-check's lines and counts.
struct BioCheckReport {
    private(set) var lines: [String] = []
    private(set) var passed = 0
    private(set) var failed = 0

    /// A check: its name (no spaces), what was found (spaces become _), and whether it is right.
    mutating func check(_ name: String, _ value: String, _ ok: Bool) {
        let v = value.isEmpty ? "-" : value.replacingOccurrences(of: " ", with: "_")
        lines.append("bio-check \(name) \(v) \(ok ? "OK" : "FAIL")")
        if ok { passed += 1 } else { failed += 1 }
    }

    /// A number within `tolerance` of what it should be.
    mutating func near(_ name: String, _ value: Double, _ expected: Double, _ tolerance: Double, digits: Int = 6) {
        check(name, String(format: "%.\(digits)f", value), abs(value - expected) <= tolerance)
    }

    /// Two texts the same.
    mutating func same(_ name: String, _ value: String, _ expected: String) {
        check(name, value, value == expected)
    }
}

enum BioSelfCheck {
    /// Every check, the foundation's then the tools' — each group's lines told as it ends, if asked.
    static func run(told: ((ArraySlice<String>) -> Void)? = nil) -> BioCheckReport {
        var r = BioCheckReport()
        let groups: [(inout BioCheckReport) -> Void] = [
            geneticCode, aminoAcids, molecules, bdna, randomness, chiSquare, crosses, sequences, structures, geometry,
            // (the tools' own, each in its model file under Biology/Model)
            cellChecks, dnaChecks, geneChecks, biomolChecks, crossChecks, divideChecks, popgenChecks, biolabChecks, enzymeChecks, energyChecks,
        ]
        for group in groups {
            let before = r.lines.count
            group(&r)
            told?(r.lines[before...])
        }
        return r
    }

    /// (-SimplBioCheck YES) Every line to standard error (unbuffered, so the screenshot suite's console file keeps
    /// it), each group's as it ends — a debug build takes about a minute for all of them, longer than a picture's
    /// launch lasts, and a check printed whole at the end never reached the console — then a summary.
    static func printAll() {
        let r = run { lines in
            guard !lines.isEmpty else { return }
            FileHandle.standardError.write(Data((lines.joined(separator: "\n") + "\n").utf8))
        }
        FileHandle.standardError.write(Data("bio-check summary \(r.passed)/\(r.passed + r.failed) \(r.failed == 0 ? "OK" : "FAIL")\n".utf8))
    }

    // MARK: 1. The genetic code

    static func geneticCode(_ r: inout BioCheckReport) {
        let e = BioCode.entries
        r.check("code.count", "\(e.count)", e.count == 64 && Set(e.map(\.codon)).count == 64)
        r.check("code.AUG", String(BioCode.aminoAcid("AUG") ?? "?"), BioCode.aminoAcid("AUG") == "M")
        r.check("code.UGG", String(BioCode.aminoAcid("UGG") ?? "?"), BioCode.aminoAcid("UGG") == "W")
        let stops = ["UAA", "UAG", "UGA"].map { BioCode.aminoAcid($0) ?? "?" }
        r.check("code.stops", String(stops), stops == ["*", "*", "*"] && BioCode.codons(for: "*") == ["UAA", "UAG", "UGA"])
        // (§6.1's counts: Leu, Ser, Arg 6; Ala, Gly, Pro, Thr, Val 4; Ile 3; stop 3; Phe … Glu 2; Met, Trp 1)
        let want: [Character: Int] = ["L": 6, "S": 6, "R": 6, "A": 4, "G": 4, "P": 4, "T": 4, "V": 4, "I": 3, "*": 3,
                                      "F": 2, "Y": 2, "C": 2, "H": 2, "Q": 2, "N": 2, "K": 2, "D": 2, "E": 2, "M": 1, "W": 1]
        let wrong = want.keys.sorted().filter { BioCode.codons(for: $0).count != want[$0] }
        r.check("code.counts", wrong.isEmpty ? "all" : String(wrong), wrong.isEmpty)
        // (the NCBI string, index 16·b1 + 4·b2 + b3 over T C A G, says the same for every codon)
        let ncbi = BioData.ncbiText.split(separator: "\n").map { $0.split(separator: "=").last.map { $0.trimmingCharacters(in: .whitespaces) } ?? "" }
        var agree = ncbi.count == 4 && ncbi[0].count == 64
        if agree {
            let aas = Array(ncbi[0]), b1 = Array(ncbi[1]), b2 = Array(ncbi[2]), b3 = Array(ncbi[3])
            for i in 0..<64 where BioCode.aminoAcid(String([b1[i], b2[i], b3[i]])) != aas[i] { agree = false }
        }
        r.check("code.ncbi", agree ? "64" : "differs", agree)
        r.same("code.anticodon", BioCode.anticodon3to5("AUG") + "/" + BioCode.anticodon5to3("AUG"), "UAC/CAU")
        r.check("code.DNA-letters", String(BioCode.aminoAcid("ATG") ?? "?") + String(BioCode.aminoAcid("NNN") ?? "?"),
                BioCode.aminoAcid("ATG") == "M" && BioCode.aminoAcid("ANG") == "X")
    }

    // MARK: 2. The amino acids and their templates

    static func aminoAcids(_ r: inout BioCheckReport) {
        let all = BioAminoAcids.all
        r.check("aa.count", "\(all.count)", all.count == 20 && Set(all.map(\.one)).count == 20)
        let codonsWrong = all.filter { $0.codonCount != BioCode.codons(for: $0.one).count }.map(\.three)
        r.check("aa.codons", codonsWrong.isEmpty ? "all" : codonsWrong.joined(separator: ","), codonsWrong.isEmpty)
        let massWrong = all.filter { abs($0.mass - 18.02 - $0.residueMass) > 0.015 }.map(\.three)
        r.check("aa.residue-mass", massWrong.isEmpty ? "all" : massWrong.joined(separator: ","), massWrong.isEmpty)
        let groups = BioAAClass.allCases.map { c in all.filter { $0.group == c }.count }
        r.check("aa.groups", groups.map(String.init).joined(separator: ","), groups == [9, 6, 2, 3])
        r.check("aa.find", BioAminoAcids.find("lys")?.name ?? "-", BioAminoAcids.find("lys")?.one == "K" && BioAminoAcids.find("W")?.three == "Trp"
                && BioAminoAcids.find("glutamic acid")?.one == "E")

        let ts = BioData.residues
        r.check("aa.templates", "\(ts.count)", ts.count == 20 && Set(ts.map(\.code)).count == 20
                && all.allSatisfy { BioData.residue(String($0.one)) != nil })
        // (the frame: CA at the origin, C on +x, N in the xy-plane with y > 0; every L-amino acid's CB at z < 0)
        var badFrame: [String] = [], badCB: [String] = [], badBond: [String] = [], badChi: [String] = []
        for t in ts {
            guard let ca = t.pos("CA"), let c = t.pos("C"), let n = t.pos("N") else { badFrame.append(t.code); continue }
            if BioV.length(ca) > 1e-3 || abs(c.y) > 1e-3 || abs(c.z) > 1e-3 || c.x <= 0 || abs(n.z) > 1e-3 || n.y <= 0 { badFrame.append(t.code) }
            if t.code != "GLY" { if (t.pos("CB")?.z ?? 1) >= 0 { badCB.append(t.code) } }
            badBond += t.model(ph7: true, hydrogens: true).oddBonds().map { "\(t.code):\($0)" }
            badBond += t.model(ph7: false, hydrogens: true).oddBonds().map { "\(t.code):\($0)" }
            for chi in t.chis {
                guard let v = t.chiValue(chi.index) else { badChi.append("\(t.code)χ\(chi.index)"); continue }
                var d = abs(v - chi.degrees).truncatingRemainder(dividingBy: 360)
                if d > 180 { d = 360 - d }
                if d > 0.5 { badChi.append("\(t.code)χ\(chi.index)=\(String(format: "%.1f", v))") }
            }
        }
        r.check("aa.frame", badFrame.isEmpty ? "all" : badFrame.joined(separator: ","), badFrame.isEmpty)
        r.check("aa.L-chirality", badCB.isEmpty ? "CB.z<0" : badCB.joined(separator: ","), badCB.isEmpty)
        r.check("aa.bond-lengths", badBond.isEmpty ? "0.9-1.9" : badBond.prefix(4).joined(separator: ","), badBond.isEmpty)
        r.check("aa.chi", badChi.isEmpty ? "within0.5" : badChi.joined(separator: ","), badChi.isEmpty)
        // (the pH 7 forms: Asp −1 at OD2, Lys +1 at NZ; turning χ1 and back leaves the template as it was)
        let asp = BioData.residue("D"), lys = BioData.residue("K")
        r.check("aa.pH7", "Asp\(asp?.ph7?.charges["OD2"] ?? 0),Lys\(lys?.ph7?.charges["NZ"] ?? 0)",
                asp?.ph7?.charges["OD2"] == -1 && asp?.ph7?.remove.contains("HD2") == true && lys?.ph7?.charges["NZ"] == 1)
        if let leu = BioData.residue("L"), let v0 = leu.chiValue(1) {
            let turned = leu.settingChi(1, to: 60)
            let back = turned.settingChi(1, to: v0)
            let drift = zip(leu.atoms, back.atoms).map { BioV.distance($0.pos, $1.pos) }.max() ?? 1
            r.check("aa.set-chi", String(format: "%.1f,%.4f", turned.chiValue(1) ?? 0, drift), abs((turned.chiValue(1) ?? 0) - 60) < 0.01 && drift < 1e-6)
        } else {
            r.check("aa.set-chi", "no-Leu", false)
        }
        let alts = ts.first { $0.code == "ARG" }?.alternatives.count ?? 0
        r.check("aa.rotamers", "ARG\(alts)", alts == 32 && ts.first { $0.code == "ASN" }?.betaRotamer?.chis == [-65, -75])
    }

    // MARK: Nucleotides, sugars, lipids

    static func molecules(_ r: inout BioCheckReport) {
        let groups: [(String, [BioMolecule], [String])] = [
            ("nucleotides", BioData.nucleotides, ["DA", "DC", "DG", "DT", "A", "C", "G", "U", "ATP"]),
            ("sugars", BioData.sugars, ["GLC", "BGC", "GAL", "FRU", "BDR", "MAL", "CBI", "SUC", "LAT"]),
            ("lipids", BioData.lipids, ["GOL", "PLM", "STE", "OLA", "TGL", "POV", "CLR"]),
        ]
        for (name, ms, codes) in groups {
            let odd = ms.flatMap { m in m.model(ph7: false).oddBonds().map { "\(m.code):\($0)" } }
            let unbonded = ms.filter { m in
                let bonded = Set(m.bonds.flatMap { [$0.a, $0.b] })
                return m.atoms.contains { !bonded.contains($0.name) }
            }.map(\.code)
            r.check("mol.\(name)", "\(ms.count)", ms.map(\.code) == codes && ms.allSatisfy { $0.mass != nil })
            r.check("mol.\(name).bonds", odd.isEmpty && unbonded.isEmpty ? "0.9-1.9" : (odd + unbonded).prefix(4).joined(separator: ","),
                    odd.isEmpty && unbonded.isEmpty)
        }
        // (the formula's atoms are the block's atoms: dAMP C10 H14 N5 O6 P)
        var formulaWrong: [String] = []
        for m in BioData.nucleotides + BioData.sugars + BioData.lipids {
            guard let f = m.formula else { formulaWrong.append(m.code); continue }
            var want: [String: Int] = [:]
            for part in f.split(separator: " ") {
                let el = String(part.prefix { $0.isLetter }), n = Int(part.drop { $0.isLetter }) ?? 1
                want[el, default: 0] += n
            }
            var have: [String: Int] = [:]
            for a in m.atoms { have[a.element, default: 0] += 1 }
            if want != have { formulaWrong.append(m.code) }
        }
        r.check("mol.formulas", formulaWrong.isEmpty ? "all" : formulaWrong.joined(separator: ","), formulaWrong.isEmpty)
        let atp = BioData.molecule("ATP")
        r.check("mol.ATP.pH7", "\(atp?.ph7?.remove.count ?? 0)", atp?.ph7?.remove == ["HOG2", "HOG3", "HOB2", "HOA2"])
    }

    // MARK: 5. B-DNA

    static func bdna(_ r: inout BioCheckReport) {
        let ac = BioBDNA.build("AC")
        var worst = 0.0
        var missing: [String] = []
        for t in BioData.bdnaTests {
            guard let p = ac.pos(strand: t.strand, pair: t.pair, name: t.name) else { missing.append(t.name); continue }
            worst = max(worst, BioV.distance(p, t.pos))
        }
        r.check("dna.test-vectors", String(format: "%ld,worst%.4f", BioData.bdnaTests.count, worst),
                BioData.bdnaTests.count == 10 && missing.isEmpty && worst <= 0.002)
        let d = BioBDNA.build("ACGTACGT")
        var links: [Double] = []
        for k in 0..<7 {
            if let o = d.pos(strand: 1, pair: k, name: "O3'"), let p = d.pos(strand: 1, pair: k + 1, name: "P") { links.append(BioV.distance(o, p)) }
            if let o = d.pos(strand: 2, pair: k + 1, name: "O3'"), let p = d.pos(strand: 2, pair: k, name: "P") { links.append(BioV.distance(o, p)) }
        }
        let off = links.map { abs($0 - 1.580) }.max() ?? 1
        r.check("dna.O3'-P", String(format: "%ld,%.4f", links.count, links.first ?? 0), links.count == 14 && off <= 0.002)
        let bonded = Set(d.bonds.flatMap { [$0.a, $0.b] })
        r.check("dna.bonds", "\(d.atoms.count)atoms,\(d.bonds.count)bonds", bonded.count == d.atoms.count && d.model.oddBonds(lo: 1.1, hi: 1.7).isEmpty)
        func hb(_ s: String, _ x: String, _ y: String) -> Double {
            let dd = BioBDNA.build(s)
            guard let a = dd.pos(strand: 1, pair: 0, name: x), let b = dd.pos(strand: 2, pair: 0, name: y) else { return 0 }
            return BioV.distance(a, b)
        }
        r.near("dna.AT.N1-N3", hb("A", "N1", "N3"), 2.91, 0.01, digits: 3)
        r.near("dna.GC.N1-N3", hb("G", "N1", "N3"), 2.87, 0.01, digits: 3)
        r.check("dna.H-bonds", "\(d.hBonds.count)", d.hBonds.count == 4 * 2 + 4 * 3)
        // (right-handed: going up strand 1, each P turns the same way θ grows — X = r·sin θ, Z = r·cos θ)
        let p0 = d.phosphates(strand: 1)
        let turns = zip(p0, p0.dropFirst()).map { a, b in BioV.cross(BioVec(a.x, 0, a.z), BioVec(b.x, 0, b.z)).y }
        r.check("dna.right-handed", "\(turns.count)", !turns.isEmpty && turns.allSatisfy { $0 > 0 })
        let pairs = BioData.basePairs2D
        r.check("dna.pairs-2D", "AT\(pairs["AT"]?.count ?? 0),GC\(pairs["GC"]?.count ?? 0)", pairs["AT"]?.count == 30 && pairs["GC"]?.count == 29)
    }

    // MARK: 6. SplitMix64 and chi-square

    static func randomness(_ r: inout BioCheckReport) {
        func words(_ seed: UInt64) -> [UInt64] {
            var g = BioRandom(seed: seed)
            return [g.next(), g.next(), g.next()]
        }
        let w0 = words(0), w42 = words(42)
        r.check("rng.seed0", w0.map { String($0, radix: 16) }.joined(separator: ","),
                w0 == [0xe220a8397b1dcdaf, 0x6e789e6aa1b965f4, 0x06c45d188009454f])
        r.check("rng.seed42", w42.map { String($0, radix: 16) }.joined(separator: ","),
                w42 == [0xbdd732262feb6e95, 0x28efe333b266f103, 0x47526757130f9f52])
        var g = BioRandom(seed: 42)
        let u = [g.uniform(), g.uniform(), g.uniform()]
        let want = [0.7415648787718233, 0.1599103928769201, 0.27860113025513866]
        r.check("rng.uniform42", u.map { String(format: "%.16f", $0) }.joined(separator: ","), zip(u, want).allSatisfy { abs($0 - $1) < 1e-15 })
    }

    static func chiSquare(_ r: inout BioCheckReport) {
        let vectors: [(Double, Int, Double)] = [(3.84, 1, 0.050044), (3.841459, 1, 0.050000), (5.99, 2, 0.050037), (7.81, 3, 0.050106),
                                                (6.63, 1, 0.010028), (0.6, 1, 0.438578), (2.0, 3, 0.572407), (11.07, 5, 0.050010)]
        for (x, df, p) in vectors {
            r.near("chi2.p(\(x),\(df))", BioChiSquare.pValue(x, df: df), p, 1e-6)
        }
        // (the table's p = 0.10 … 0.001 rows, recomputed by bisection, agree to its two decimals)
        var off: [String] = []
        for df in 1...10 {
            for (i, p) in BioChiSquare.tableP.enumerated() {
                let c = BioChiSquare.criticalComputed(df: df, p: p), t = BioChiSquare.table[df]?[i] ?? -1
                if abs(c - t) > 0.0051 { off.append("\(df)@\(p)=\(String(format: "%.3f", c))") }
            }
        }
        r.check("chi2.critical", off.isEmpty ? "40" : off.prefix(3).joined(separator: ","), off.isEmpty && BioChiSquare.table.count == 10)
        // (the Hardy–Weinberg vector of §5.4.2: 50, 30, 20 against p = 0.65, df 1)
        let t = BioChiSquare.test(observed: [50, 30, 20], expected: [42.25, 45.5, 12.25], df: 1)
        r.check("chi2.HWE", String(format: "%.4f,%.5f", t.chi2, t.p), abs(t.chi2 - 11.6049) < 1e-4 && abs(t.p - 0.00066) < 5e-6 && t.reject)
        let fit = BioChiSquare.test(observed: [315, 108, 101, 32], expected: BioChiSquare.expected(ratio: [9, 3, 3, 1], total: 556))
        r.check("chi2.conclusion", String(format: "%.3f", fit.chi2), fit.df == 3 && !fit.reject && fit.conclusion.contains("fail to reject"))
    }

    // MARK: 7. Crosses

    static func crosses(_ r: inout BioCheckReport) {
        func cross(_ text: String, _ adjust: ((inout BioCross) -> Void)? = nil) -> BioCross? {
            guard var c = BioCross.parse(text).cross else { return nil }
            adjust?(&c)
            return c
        }
        func row(_ name: String, _ text: String, geno: String, pheno: [Int], phenoText: String? = nil, _ adjust: ((inout BioCross) -> Void)? = nil) {
            guard let c = cross(text, adjust) else { r.check("cross.\(name)", "unparsed", false); return }
            let res = c.result()
            let ok = res.genotypicRatioText == geno && res.phenotypicRatio == pheno && (phenoText == nil || res.phenotypicRatioText == phenoText)
            r.check("cross.\(name)", res.genotypicRatioText + "|" + res.phenotypicRatioText, ok)
        }
        row("Aa×Aa", "Aa × Aa", geno: "1 AA : 2 Aa : 1 aa", pheno: [3, 1], phenoText: "3 A_ : 1 aa")
        row("Aa×aa", "Aa × aa", geno: "1 Aa : 1 aa", pheno: [1, 1])
        row("AA×aa", "AA × aa", geno: "1 Aa", pheno: [1], phenoText: "1 A_")
        row("incomplete", "C^R C^W × C^R C^W", geno: "1 C^R C^R : 2 C^R C^W : 1 C^W C^W", pheno: [1, 2, 1], phenoText: "1 red : 2 pink : 1 white")
        row("AaBb×AaBb", "AaBb × AaBb", geno: "1 AABB : 2 AABb : 1 AAbb : 2 AaBB : 4 AaBb : 2 Aabb : 1 aaBB : 2 aaBb : 1 aabb",
            pheno: [9, 3, 3, 1], phenoText: "9 A_B_ : 3 A_bb : 3 aaB_ : 1 aabb")
        row("AaBb×aabb", "AaBb x aabb", geno: "1 AaBb : 1 Aabb : 1 aaBb : 1 aabb", pheno: [1, 1, 1, 1])
        if let tri = cross("AaBbCc × AaBbCc") {
            let res = tri.result()
            r.check("cross.AaBbCc", "\(res.genotypes.count)|" + res.phenotypicRatio.map(String.init).joined(separator: ":"),
                    res.genotypes.count == 27 && res.phenotypicRatio == [27, 9, 9, 9, 3, 3, 3, 1]
                    && res.phenotypes.map(\.text) == ["A_B_C_", "A_B_cc", "A_bbC_", "aaB_C_", "A_bbcc", "aaB_cc", "aabbC_", "aabbcc"])
            r.check("cross.gametes", "\(tri.gametes1.count)", tri.gametes1.count == 8 && tri.gametes1.allSatisfy { $0.p == BioFrac(1, 8) })
            let p = tri.probability(of: [.homRecessive, .dominant, .heterozygous])
            r.check("cross.product-rule", p.factors.map(\.text).joined(separator: "×") + "=" + p.product.text,
                    p.factors == [BioFrac(1, 4), BioFrac(3, 4), BioFrac(1, 2)] && p.product == BioFrac(3, 32) && p.product.percentText == "9.4%")
        } else {
            r.check("cross.AaBbCc", "unparsed", false)
        }
        row("ABO-AO×BO", "I^A i × I^B i", geno: "1 I^A I^B : 1 I^A i : 1 I^B i : 1 ii", pheno: [1, 1, 1, 1], phenoText: "1 AB : 1 A : 1 B : 1 O")
        row("ABO-AB×AB", "I^A I^B × I^A I^B", geno: "1 I^A I^A : 2 I^A I^B : 1 I^B I^B", pheno: [1, 2, 1], phenoText: "1 A : 2 AB : 1 B")
        func xlinked(_ name: String, _ text: String, daughters: String, dPheno: [Int], dCarriers: BioFrac, sons: String, sPheno: String) {
            guard let c = cross(text) else { r.check("cross.\(name)", "unparsed", false); return }
            let d = c.result(sex: .female), s = c.result(sex: .male)
            let carriers = d.phenotypes.first?.carriers ?? .zero
            r.check("cross.\(name)", d.genotypicRatioText + "|" + s.genotypicRatioText + "|" + s.phenotypicRatioText,
                    d.genotypicRatioText == daughters && d.phenotypicRatio == dPheno && carriers == dCarriers
                    && s.genotypicRatioText == sons && s.phenotypicRatioText == sPheno && d.phenotypes.first?.text == "unaffected")
        }
        xlinked("X-carrier×affected", "X^A X^a × X^a Y", daughters: "1 X^A X^a : 1 X^a X^a", dPheno: [1, 1], dCarriers: BioFrac(1, 2),
                sons: "1 X^A Y : 1 X^a Y", sPheno: "1 unaffected : 1 affected")
        xlinked("X-carrier×normal", "X^A X^a × X^A Y", daughters: "1 X^A X^A : 1 X^A X^a", dPheno: [1], dCarriers: BioFrac(1, 2),
                sons: "1 X^A Y : 1 X^a Y", sPheno: "1 unaffected : 1 affected")
        if let c = cross("A^Y a × A^Y a", { $0.genes[0].pattern = .lethalDominantHomozygote; $0.genes[0].traitNames = ["yellow", "non-yellow"] }) {
            let res = c.result()
            r.check("cross.lethal", res.genotypicRatioText + "|" + res.bornGenotypicRatio.map(String.init).joined(separator: ":") + "|" + res.phenotypicRatioText,
                    res.genotypicRatioText == "1 A^Y A^Y : 2 A^Y a : 1 aa" && res.genotypes.first?.dies == true
                    && res.bornGenotypicRatio == [2, 1] && res.phenotypicRatioText == "2 yellow : 1 non-yellow" && res.bornFraction == BioFrac(3, 4))
        } else {
            r.check("cross.lethal", "unparsed", false)
        }
        let bad = BioCross.parse("Aa")
        r.check("cross.parse-error", bad.error == nil ? "none" : "said", bad.cross == nil && bad.error != nil && BioCross.parse("aA × Aa").cross?.parent1 == [[0, 1]])
        r.same("frac.text", BioFrac(6, 64).text + "," + BioFrac(2, -4).text + "," + BioRatio.text(BioRatio.reduce([2, 4, 2])), "3/32,-1/2,1 : 2 : 1")
        // (every number shown rounded half up on its decimal digits, as on Windows: a true tie up, not to even (1/16 is
        // 6.25%), and 3.425 up though it is held as 3.42499…; the other conversions as they were)
        let shown = [String(halfUp: "%.2f", 3.425), String(halfUp: "%.2f", 0.125), BioFrac(1, 16).percentText, String(halfUp: "%+.1f", -2.25),
                     String(halfUp: "%.0f%%", 2.5), String(halfUp: "%%A + %%G = %.1f%%", 50.05), String(halfUp: "%d of %.1f", 3, 0.15),
                     String(halfUp: "%.3f", -0.0004), String(halfUp: "%.2f × 10", 9.995)]
        r.same("format.half-up", shown.joined(separator: "|"), "3.43|0.13|6.3%|-2.3|3%|%A + %G = 50.1%|3 of 0.2|0.000|10.00 × 10")
    }

    // MARK: 10. Sequences and genes

    static func sequences(_ r: inout BioCheckReport) {
        let g = BioGenes.teaching
        let c = Array(g.coding)
        func span(_ a: Int, _ b: Int) -> String { a >= 1 && b <= c.count && a <= b ? String(c[(a - 1)..<b]) : "" }
        r.check("gene.length", "\(c.count)", c.count == 112 && g.parts.last?.end == 112 && g.parts.map(\.start) == [1, 32, 45, 56, 83, 102])
        r.same("gene.marks", [span(5, 11), span(45, 47), span(99, 101), span(104, 109)].joined(separator: ","), "TATAAAA,ATG,TAA,AATAAA")
        r.check("gene.template", "\(g.template.count)", g.template == BioSeq.templateStrand(g.coding))
        let pre = BioSeq.transcribe(codingStrand: span(32, 112))
        r.check("gene.pre-mRNA", "\(pre.count)nt", pre == g.preMRNA)
        let mature = BioSeq.transcribe(codingStrand: span(32, 55) + span(83, 112))
        r.check("gene.mature-mRNA", "\(mature.count)nt", mature == g.matureMRNA)
        let cds = span(45, 55) + span(83, 101)
        r.check("gene.CDS", cds, cds == g.cds)
        r.same("gene.protein", BioSeq.translate(g.cds), g.protein + "*")
        r.same("gene.unspliced", (BioSeq.proteinFromFirstStart(g.preMRNA) ?? "") + "*", g.unsplicedProtein)
        let orfs = BioSeq.orfs(g.matureMRNA, minCodons: 3, reverseStrand: false)
        r.check("gene.ORF", orfs.first.map { "\($0.frameLabel)@\($0.start)-\($0.end)" } ?? "none",
                orfs.first?.frame == 2 && orfs.first?.start == 14 && orfs.first?.protein == g.protein && orfs.first?.hasStop == true)
        // (the mutation table's mutant sequences translate to the proteins it lists)
        var wrong: [String] = []
        for m in g.mutations {
            let protein = BioSeq.translate(m.cds.replacingOccurrences(of: "**", with: ""))
            let core = String(protein.prefix { $0 != "*" })
            if m.protein.contains("…") {
                // ("MAIQRLAERL… (no stop before the end)": its start, and no stop)
                let listed = m.protein.components(separatedBy: "…").first ?? ""
                if !core.hasPrefix(listed) || protein.contains("*") { wrong.append(m.mutation) }
            } else {
                // ("MAFSDWQKG" or "MAFSDW*": up to the stop, which is there)
                let listed = m.protein.hasSuffix("*") ? String(m.protein.dropLast()) : m.protein
                if core != listed || !protein.contains("*") { wrong.append(m.mutation) }
            }
        }
        r.check("gene.mutations", wrong.isEmpty ? "\(g.mutations.count)" : wrong.joined(separator: ","), wrong.isEmpty && g.mutations.count == 6)
        // (HBB: its CDS translates to NP_000509.1; c.20A>T makes codon 7 GTG, Glu → Val)
        r.check("gene.HBB", "\(BioGenes.hbbCDS.count)nt", BioSeq.translate(BioGenes.hbbCDS) == BioGenes.hbbProtein + "*")
        var hbb = Array(BioGenes.hbbCDS)
        let was = hbb[19]
        hbb[19] = "T"
        let mutant = BioSeq.translate(String(hbb))
        r.check("gene.sickle", String(mutant.prefix(10)), was == "A" && mutant.hasPrefix(BioGenes.sickle.proteinStart)
                && BioSeq.codons(String(hbb))[6] == "GTG")
        // (cleaning: a FASTA header, digits and spaces dropped; U in DNA offered as RNA; bad letters placed; a pasted 5′-…-3′
        // taken off before the digits go)
        let a = BioSequenceInput(">x\n atg c12gt", as: .dna)
        let b = BioSequenceInput("AUGC", as: .dna)
        let bad = BioSequenceInput("ATGXQ", as: .dna)
        let ends = BioSequenceInput("5′-ACGTACGT-3′", as: .dna, cap: 4), ends2 = BioSequenceInput("5'-ATG C-3'", as: .dna)
        r.check("seq.clean", a.letters + "," + (b.suggestion?.rawValue ?? "-") + "," + bad.invalid.map { "\($0.letter)\($0.positions)" }.joined(),
                a.sequence == "ATGCGT" && b.suggestion == .rna && bad.invalid.count == 2 && bad.invalid[0].positions == [4] && bad.sequence == nil
                    && ends.sequence == "ACGT" && ends.truncated && ends2.sequence == "ATGC")
        r.same("seq.reverse-complement", BioSeq.reverseComplement("ATGCN") + "," + BioSeq.complement("ATGC"), "NGCAT,TACG")
        let frames = (0..<3).map { BioSeq.translate("ATGGCCTAA", frame: $0) } + (0..<3).map { BioSeq.translate("ATGGCCTAA", frame: $0, reverse: true) }
        r.same("seq.frames", frames.joined(separator: ","), "MA*,WP,GL,LGH,*A,RP")
        r.near("seq.mass", BioSeq.mass("ACGT"), 331.22 + 307.20 + 347.22 + 322.21 - 3 * 18.02, 1e-9, digits: 2)
        r.near("seq.Tm", BioSeq.meltingTemperature("ATGCATGCATGC"), 36, 1e-9, digits: 1)
        r.near("seq.Chargaff", BioSeq.chargaff("AAAGC").purinePercent, 50, 1e-9, digits: 1)
        let lines = BioSeq.lines(String(repeating: "A", count: 75))
        r.check("seq.groups", lines.map { "\($0.start):\($0.groups.count)" }.joined(separator: ","), lines.count == 2 && lines[1].start == 61 && lines[1].groups == ["AAAAAAAAAA", "AAAAA"])
    }

    // MARK: Proteins and tRNA

    static func structures(_ r: inout BioCheckReport) {
        let u = BioData.ubiquitin
        r.check("ubiquitin", "\(u.count)", u.count == 76 && u.map(\.i) == Array(1...76) && u.allSatisfy { $0.centroid != nil })
        let h = BioData.hemoglobin
        let chains = ["A", "B", "C", "D"].map { c in h.residues.filter { String($0.chain) == c }.count }
        r.check("hemoglobin", chains.map(String.init).joined(separator: ",") + ",hemes\(h.hemes.count)",
                chains == [141, 146, 141, 146] && h.hemes.count == 4 && h.hemes.allSatisfy { $0.n.count == 4 })
        let beta = String(h.residues.filter { $0.chain == "B" }.map(\.aa))
        r.check("hemoglobin.beta=HBB", String(beta.prefix(10)), beta == String(BioGenes.hbbProtein.dropFirst()))
        let caca = zip(u, u.dropFirst()).map { BioV.distance($0.ca, $1.ca) }
        r.check("ubiquitin.CA-CA", String(format: "%.2f-%.2f", caca.min() ?? 0, caca.max() ?? 0), (caca.min() ?? 0) > 3.6 && (caca.max() ?? 9) < 4.0)
        let t = BioData.trna
        let seq = String(t.nucleotides.map(\.base))
        r.check("tRNA", "\(t.nucleotides.count)", t.nucleotides.count == 76 && seq == BioData.trnaSequence)
        let ok = t.pairs.allSatisfy { i, j in
            let a = Array(BioData.trnaSequence)[i - 1], b = Array(BioData.trnaSequence)[j - 1]
            let pr = String([a, b])
            return ["AU", "UA", "GC", "CG", "GU", "UG"].contains(pr)
        }
        r.check("tRNA.pairs", "\(t.pairs.count)", t.pairs.count == 21 && ok)
        r.check("tRNA.modified", "\(t.modified.count)", t.modified.count == 14 && t.modified.first?.position == 10 && t.modified.last?.label == "m¹A")
        if t.nucleotides.count == 76 {
            let anticodon = (t.nucleotides[33].p + t.nucleotides[34].p + t.nucleotides[35].p) / 3
            r.near("tRNA.anticodon-3'end", BioV.distance(anticodon, t.nucleotides[75].p), 72, 1.0, digits: 1)
        }
    }

    // MARK: The foundation's geometry and labels

    static func geometry(_ r: inout BioCheckReport) {
        func volume(_ m: BioMesh) -> Double {
            var v = 0.0
            for k in stride(from: 0, to: m.indices.count - 2, by: 3) {
                let p = [m.indices[k], m.indices[k + 1], m.indices[k + 2]].map { m.positions[Int($0)] }.map { BioVec(Double($0.x), Double($0.y), Double($0.z)) }
                v += BioV.dot(p[0], BioV.cross(p[1], p[2])) / 6
            }
            return v
        }
        let ball = 4.0 / 3 * Double.pi
        let ico = BioShapes.icosphere(radius: 1, subdivisions: 1)
        r.check("mesh.icosphere", "\(ico.triangleCount)", ico.triangleCount == 80 && volume(ico) > 0)
        let s = BioShapes.sphere(radius: 1, rings: 32, segments: 64)
        r.near("mesh.sphere-volume", volume(s) / ball, 1, 0.01, digits: 4)
        // (a cut solid keeps 7/8; a cut shell keeps 7/8 of its wall — so the caps are there and face out)
        let cut = BioShapes.radial(rings: 32, segments: 64, cut: .octant, radius: BioShapes.sphere(1))
        r.near("mesh.octant-cut", volume(cut) / ball, 0.875, 0.01, digits: 4)
        let shell = BioShapes.radial(rings: 32, segments: 64, cut: .octant, inner: BioShapes.sphere(0.9), radius: BioShapes.sphere(1))
        r.near("mesh.cut-shell", volume(shell) / (ball * (1 - 0.729)), 0.875, 0.015, digits: 4)
        let half = BioShapes.capsule(radius: 0.45, length: 1.3, rings: 24, segments: 48, cut: .half)
        let capsuleV = Double.pi * 0.45 * 0.45 * 1.3 + 4.0 / 3 * Double.pi * pow(0.45, 3)
        r.near("mesh.capsule-half", volume(half) / capsuleV, 0.5, 0.01, digits: 4)
        let box = BioShapes.roundedBox(BioVec(2, 1, 1), corner: 0.25, rings: 32, segments: 64)
        let boxV = 4.0 * 2 * 2 - (8 - 4.0 / 3 * Double.pi) * pow(0.25, 3) - 0.25 * 0.25 * (4 - Double.pi) * (4 * 1.5 + 4 * 0.5 + 4 * 0.5)
        r.near("mesh.rounded-box", volume(box) / boxV, 1, 0.02, digits: 4)
        let tube = BioPath.tube([BioVec(0, 0, 0), BioVec(0, 1, 0), BioVec(0, 2, 0)], radius: 0.1, sides: 24, samples: 4)
        r.near("mesh.tube", volume(tube) / (Double.pi * 0.01 * 2), 1, 0.02, digits: 4)
        let ring = BioPath.tube((0..<12).map { k in BioVec(cos(Double(k) * .pi / 6), 0, sin(Double(k) * .pi / 6)) }, radius: 0.05, sides: 12, closed: true)
        r.check("mesh.closed-tube", String(format: "%.4f", volume(ring)), volume(ring) > 0)
        let ribbon = BioPath.ribbon([BioVec(0, 0, 0), BioVec(1, 0, 0), BioVec(2, 0, 0), BioVec(3, 0, 0)], width: 1.6, thickness: 0.4, arrow: (from: 2, width: 3.2))
        r.check("mesh.ribbon", String(format: "%.3f", volume(ribbon)), volume(ribbon) > 0)
        let patch = BioShapes.shellPatch(theta: 0.25 * .pi...0.85 * .pi, phi: 0...1.1 * .pi, thickness: 0.06, radius: { _, _ in 3.55 })
        r.check("mesh.shell-patch", String(format: "%.3f", volume(patch)), volume(patch) > 0)
        let torus = BioShapes.torus(ring: 0.07, pipe: 0.02, ringSegments: 96, pipeSegments: 48)
        r.near("mesh.torus", volume(torus) / (2 * Double.pi * Double.pi * 0.07 * 0.0004), 1, 0.01, digits: 4)
        let cyl = BioShapes.cylinder(from: BioVec(1, 1, 1), to: BioVec(1, 3, 1), radius: 0.5, segments: 48)
        r.near("mesh.cylinder", volume(cyl) / (Double.pi * 0.25 * 2), 1, 0.01, digits: 4)
        let blob = BioBlob(seed: 0xC311, amplitude: 0.06)
        let amp = blob.terms.reduce(0) { $0 + $1.a }
        r.near("mesh.blob-amplitude", amp, 0.06, 1e-12)
        if let ala = BioData.residue("A") {
            let m = ala.model(ph7: true)
            let items = BioMoleculeMeshes.items(m, style: .ballStick, radius: { _ in 0.3 }, color: { _ in "#909090" })
            let atoms = items.filter { $0.id != nil }.count
            r.check("mesh.molecule", "\(atoms)atoms,\(items.count - atoms)bonds",
                    atoms == 13 && m.atoms.count == 13 && items.count - atoms == m.bonds.count + m.bonds.filter { $0.order == 2 }.count)
        }
        var batch = BioMeshBatch()
        batch.add(ico, id: "a")
        batch.add(s, id: "b")
        r.check("mesh.batch", (batch.object(atTriangle: 79) ?? "-") + (batch.object(atTriangle: 80) ?? "-"),
                batch.object(atTriangle: 79) == "a" && batch.object(atTriangle: 80) == "b" && batch.object(atTriangle: batch.mesh.triangleCount) == nil)
        // (labels: each side's column, at least 22 pt apart, inside the view; 14 at most)
        let inputs = (0..<20).map { k in BioLabelInput(id: "o\(k)", text: "Part \(k)", x: k % 2 == 0 ? 300 : 500, y: 380 + Double(k), area: Double(k)) }
        let (placed, more) = BioLabelLayout.layout(inputs, cx: 400, cy: 400, r: 150, width: 800, height: 800)
        let lefts = placed.filter(\.left).sorted { $0.y < $1.y }
        let gapsOK = zip(lefts, lefts.dropFirst()).allSatisfy { $1.y - $0.y >= 22 - 1e-9 }
        let inside = placed.allSatisfy { $0.frame.x >= 16 - 1e-9 && $0.frame.x + $0.frame.width <= 800 - 16 + 1e-9 && $0.frame.y >= 16 - 1e-9 }
        r.check("labels.layout", "\(placed.count)+\(more)", placed.count == 14 && more == 6 && gapsOK && inside
                && lefts.allSatisfy { $0.edgeX == 400 - 174 && $0.elbowX == 400 - 162 })
    }
}
