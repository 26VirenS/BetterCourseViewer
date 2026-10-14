import Foundation

// The double helix as DNA & RNA draws it (spec §4.2 Double helix, §6.7): B-DNA from the shared atomic model
// (BioBDNA: 3.40 Å and 360°/10.5 a base pair, right-handed, strand 1 running 5′→3′ up and strand 2 down), drawn as a
// cartoon — each backbone a tube through its strand's phosphorus atoms, each base a rounded box from its C1′ toward its
// partner's (the purine's box longer than the pyrimidine's), 2 or 3 white dashes across the gap — or atom by atom
// (ball and stick, or space filling, with the H-bonds dashed between the exact donor and acceptor atoms). Overlays:
// arrowheads 5′→3′ along each backbone and the four ends' 5′ and 3′; the grooves as translucent helical bands; three
// measurements as dimension lines; and the unzip, which opens the base pairs from one end. Pressing a base selects its
// pair (both nucleotides, sugar and phosphate too); pressing a backbone selects that nucleotide's stretch of it.
//
// Ids: "pair:<k>:<strand>:base" and "pair:<k>:<strand>:bb" (a nucleotide's base and its backbone; in the atom views
// each atom adds ":a<index>", and an arrowhead ":arrow"), "pair:<k>:hb" (a pair's H-bonds), "groove:major",
// "groove:minor". Selecting "pair:<k>" lights both nucleotides of base pair k.

/// How the helix is drawn.
enum DNAHelixStyle: String, CaseIterable, Sendable {
    case cartoon, atoms, spaceFill

    var label: String {
        switch self {
        case .cartoon: return "Cartoon"
        case .atoms: return "Atoms"
        case .spaceFill: return "Space filling"
        }
    }

    /// The chemistry builder's style for the atom views.
    var atomStyle: BioAtomStyle { self == .spaceFill ? .spaceFill : .ballStick }
}

/// What the helix is drawn with: everything its scene depends on.
struct DNAHelixOptions: Hashable, Sendable {
    /// Strand 1, 5′→3′ (the first 40 of its A, C, G and T are drawn).
    var sequence = DNAModel.defaultSequence
    var style: DNAHelixStyle = .cartoon
    /// Arrowheads 5′→3′ along each backbone, and the four ends' 5′ and 3′.
    var ends = true
    var grooves = false
    var measure = false
    var hBonds = true
    /// 0 closed … 1 every pair open.
    var unzip = 0.0
    var dark = false

    /// The scene's key (the view makes it again when this changes).
    var key: String {
        "helix|\(DNAModel.helixBases(sequence))|\(style.rawValue)|\(ends)|\(grooves)|\(measure)|\(hBonds)|\(String(format: "%.3f", unzip))|\(dark)"
    }
}

/// A part of the helix a press can name.
enum DNAHelixPart: Equatable {
    /// A base pair (0 at strand 1's 5′ end).
    case pair(Int)
    /// A nucleotide's stretch of sugar–phosphate backbone.
    case backbone(strand: Int, pair: Int)
    case groove(major: Bool)
}

/// A base pair's card: its bases, numbers and H-bonds as the model has them.
struct DNAPairFacts: Equatable {
    /// 0 at strand 1's 5′ end.
    let pair: Int
    let base1: Character
    let base2: Character
    /// Each base's number from its own strand's 5′ end.
    let number1: Int
    let number2: Int
    /// The Watson–Crick H-bonds (§6.5: donor → acceptor) and their lengths in the model, heavy atom to heavy atom (Å).
    let hBonds: [(text: String, distance: Double)]
    /// C1′ to C1′ (Å): the same for every pair, which keeps the helix 2 nm wide.
    let c1c1: Double

    var name: String { "\(base1)·\(base2)" }
    var isGC: Bool { base1 == "G" || base1 == "C" }

    static func == (a: DNAPairFacts, b: DNAPairFacts) -> Bool {
        a.pair == b.pair && a.base1 == b.base1 && a.base2 == b.base2 && a.hBonds.map(\.text) == b.hBonds.map(\.text)
    }
}

enum DNAHelix {
    /// The cartoon's backbone tube (Å).
    static let tubeRadius = 1.1
    /// The gap between a pair's two boxes, where the H-bond dashes go.
    static let gap = 1.0
    static let boxThickness = 1.8
    static let boxWidth = 4.2
    /// The purine's share of the C1′–C1′ distance less the gap (the pyrimidine's is the rest, 42%).
    static let purineShare = 0.58
    /// How far an unzipped pair's nucleotides move apart (Å), each half of it.
    static let openDistance = 8.0
    /// The angle round the axis from strand 1's backbone to strand 2's at the same height, across the minor groove
    /// (the narrow side): 148.45°. The major groove is the rest, 211.55° (§6.7).
    static let minorArc: Double = {
        let a = (BioBDNA.dyadD - BioBDNA.dyadC / BioBDNA.rise * BioBDNA.twist).truncatingRemainder(dividingBy: 360)
        return a < 0 ? a + 360 : a
    }()
    /// The bands' radius: between the bases' outer edges and the backbones, so each band winds down its groove.
    static let grooveRadius = 6.6
    /// The atoms of each nucleotide's sugar–phosphate backbone (the rest are its base's).
    static let backboneNames: Set<String> = ["P", "OP1", "OP2", "O5'", "C5'", "C4'", "O4'", "C3'", "O3'", "C2'", "C1'"]

    /// One full turn's height (Å): 10.5 × 3.40 = 35.7.
    static var pitch: Double { BioBDNA.rise * 360 / BioBDNA.twist }
    /// Turns in a helix of n base pairs.
    static func turns(_ n: Int) -> Double { Double(n) * BioBDNA.twist / 360 }

    /// How open base pair k of n is at an unzip of `unzip` (0…1): it starts to separate once unzip > k/n, and is wide
    /// open 1/n later — so the helix opens from strand 1's 5′ end.
    static func opening(_ k: Int, of n: Int, unzip: Double) -> Double {
        min(max(unzip * Double(n) - Double(k), 0), 1)
    }

    /// Each atom's place once unzipped: each nucleotide of an opening pair moved away from its partner along their C1′–C1′
    /// line, up to 4 Å each.
    static func displaced(_ d: BioDuplex, unzip: Double) -> [BioVec] {
        guard unzip > 0, d.hasStrand2 else { return d.atoms.map(\.pos) }
        let n = d.sequence.count
        var dirs: [Int: BioVec] = [:]
        for k in 0..<n {
            if let a = d.pos(strand: 1, pair: k, name: "C1'"), let b = d.pos(strand: 2, pair: k, name: "C1'") { dirs[k] = BioV.unit(b - a) }
        }
        return d.atoms.map { a in
            let f = opening(a.pair, of: n, unzip: unzip)
            guard f > 0, let u = dirs[a.pair] else { return a.pos }
            return a.pos + u * ((a.strand == 1 ? -1 : 1) * f * openDistance / 2)
        }
    }

    // MARK: Picking

    /// What an id from a press names (an item's or a label's).
    static func part(_ id: String?) -> DNAHelixPart? {
        guard let id else { return nil }
        let f = id.split(separator: ":").map(String.init)
        if f.count == 2, f[0] == "groove" { return .groove(major: f[1] == "major") }
        guard f.count >= 2, f[0] == "pair", let k = Int(f[1]) else { return nil }
        if f.count >= 4, let s = Int(f[2]), f[3] == "bb" { return .backbone(strand: s, pair: k) }
        return .pair(k)
    }

    /// The selection a part lights: a pair's both nucleotides, a nucleotide's backbone, a groove.
    static func selection(_ p: DNAHelixPart) -> String {
        switch p {
        case .pair(let k): return "pair:\(k)"
        case .backbone(let s, let k): return "pair:\(k):\(s):bb"
        case .groove(let major): return major ? "groove:major" : "groove:minor"
        }
    }

    /// The atom an atom view's id names (":a<index>"), if any.
    static func atomIndex(_ id: String?) -> Int? {
        guard let last = id?.split(separator: ":").last, last.hasPrefix("a") else { return nil }
        return Int(last.dropFirst())
    }

    // MARK: Facts

    /// Base pair k's card (nil if the helix has no such pair).
    static func pairFacts(_ sequence: String, pair k: Int) -> DNAPairFacts? {
        let seq = DNAModel.helixBases(sequence)
        guard k >= 0, k < seq.count else { return nil }
        let d = BioBDNA.build(seq)
        let b1 = Array(seq)[k], b2 = BioSeq.pair(b1, rna: false)
        func at(_ base: Character, _ name: String) -> BioVec? { d.pos(strand: base == b1 ? 1 : 2, pair: k, name: name) }
        // (§6.5's donor → acceptor: A·T 2, G·C 3)
        let spec: [(text: String, a: (Character, String), b: (Character, String))]
        if b1 == "A" || b1 == "T" {
            spec = [("A N6–H → T O4", ("A", "N6"), ("T", "O4")), ("T N3–H → A N1", ("T", "N3"), ("A", "N1"))]
        } else {
            spec = [("G N1–H → C N3", ("G", "N1"), ("C", "N3")), ("C N4–H → G O6", ("C", "N4"), ("G", "O6")),
                    ("G N2–H → C O2", ("G", "N2"), ("C", "O2"))]
        }
        let hb: [(text: String, distance: Double)] = spec.map { s in
            guard let p = at(s.a.0, s.a.1), let q = at(s.b.0, s.b.1) else { return (s.text, 0) }
            return (s.text, BioV.distance(p, q))
        }
        let c = (d.pos(strand: 1, pair: k, name: "C1'"), d.pos(strand: 2, pair: k, name: "C1'"))
        let c1c1 = c.0.flatMap { a in c.1.map { BioV.distance(a, $0) } } ?? 0
        return DNAPairFacts(pair: k, base1: b1, base2: b2, number1: k + 1, number2: seq.count - k, hBonds: hb, c1c1: c1c1)
    }

    /// The grooves' widths in this model (Å), phosphate to phosphate across the groove less 5.8 Å for the phosphate
    /// groups (§6.7's convention): minor about 6.6, major about 11.6.
    static let grooveWidths: (minor: Double, major: Double) = {
        guard let p = BioData.bdna.backbone.first(where: { $0.name == "P" }) else { return (0, 0) }
        let k = 10
        let p1 = BioBDNA.place(p, strand: 1, pair: k)
        let minor = ((k - 6)...(k - 2)).map { BioV.distance(p1, BioBDNA.place(p, strand: 2, pair: $0)) }.min() ?? 0
        let major = ((k + 1)...(k + 5)).map { BioV.distance(p1, BioBDNA.place(p, strand: 2, pair: $0)) }.min() ?? 0
        return (minor - 5.8, major - 5.8)
    }()

    // MARK: The scene

    /// The helix's scene for these options; atoms look as `look` has them (the chemistry tables, from the view).
    static func build(_ o: DNAHelixOptions, look: DNAAtomLook = .standard) -> DNAScene {
        let seq = DNAModel.helixBases(o.sequence)
        guard !seq.isEmpty else { return DNAScene(content: BioSceneContent(), marks: []) }
        let d = BioBDNA.build(seq)
        let n = seq.count
        let pos = displaced(d, unzip: o.unzip)
        var index: [String: Int] = [:]
        for (i, a) in d.atoms.enumerated() { index["\(a.strand)/\(a.pair)/\(a.name)"] = i }
        func at(_ s: Int, _ k: Int, _ name: String) -> BioVec? { index["\(s)/\(k)/\(name)"].map { pos[$0] } }
        func rest(_ s: Int, _ k: Int, _ name: String) -> BioVec? { index["\(s)/\(k)/\(name)"].map { d.atoms[$0].pos } }

        let strandHex = [1: BioPalette.strand1.hex(dark: o.dark), 2: BioPalette.strand2.hex(dark: o.dark)]
        var items: [BioSceneItem] = []
        var labels: [BioLabel3D] = []
        var marks: [DNATextMark] = []

        // (each strand's backbone path, 5′→3′: its P atoms, then the last nucleotide's O3′ — and which pair each span is)
        let paths = [1, 2].map { s -> (points: [BioVec], pairs: [Int]) in
            let order = s == 1 ? Array(0..<n) : Array((0..<n).reversed())
            var pts = order.compactMap { at(s, $0, "P") }
            if let last = order.last, let o3 = at(s, last, "O3'") { pts.append(o3) }
            return (pts, order)
        }
        let samples = 6
        let splines = paths.map { BioPath.catmullRom($0.points, samples: samples) }

        // (a pair's frame: u from strand 1's C1′ to strand 2's, n along the axis, w toward the major groove — and where
        // its bases sit off the C1′–C1′ line)
        struct PairFrame { let u, n, w: BioVec; let offW, offN, length: Double }
        var frames: [Int: PairFrame] = [:]
        for k in 0..<n {
            guard let c1 = rest(1, k, "C1'"), let c2 = rest(2, k, "C1'") else { continue }
            let len = BioV.distance(c1, c2)
            let u = (c2 - c1) / len
            let y = BioVec(0, 1, 0)
            let nn = BioV.unit(y - u * BioV.dot(u, y))
            var w = BioV.cross(u, nn)
            let mid = (c1 + c2) / 2
            let baseAtoms = d.atoms.filter { $0.pair == k && !backboneNames.contains($0.name) }.map(\.pos)
            let centroid = baseAtoms.isEmpty ? mid : baseAtoms.reduce(BioVec(0, 0, 0), +) / Double(baseAtoms.count)
            // (w points to the bases' side of the C1′–C1′ line: the major groove's)
            if BioV.dot(centroid - mid, w) < 0 { w = -w }
            frames[k] = PairFrame(u: u, n: nn, w: w, offW: BioV.dot(centroid - mid, w), offN: BioV.dot(centroid - mid, nn), length: len)
        }

        let bases = Array(seq)
        if o.style == .cartoon {
            // Backbones: a tube through each strand's P atoms, a piece per nucleotide.
            for (si, s) in [1, 2].enumerated() {
                let path = splines[si]
                let spans = paths[si].pairs.indices.map { i in (i * samples)...min((i + 1) * samples, path.count - 1) }
                let pieces = DNAMesh.tubePieces(path, radius: tubeRadius, sides: 12, ranges: spans)
                for (i, k) in paths[si].pairs.enumerated() {
                    var mesh = pieces[i]
                    // (a stick from the base's C1′ to the backbone, at the span's point nearest the sugar's C4′)
                    if let c1 = at(s, k, "C1'"), let c4 = at(s, k, "C4'") {
                        let span = spans[i]
                        let near = span.min { BioV.distance(path[$0], c4) < BioV.distance(path[$1], c4) } ?? span.lowerBound
                        mesh.append(BioShapes.cylinder(from: c1, to: path[near], radius: 0.45, segments: 10))
                    }
                    items.append(BioSceneItem(id: "pair:\(k):\(s):bb", mesh: mesh, material: BioMaterial(strandHex[s] ?? "#7d5cf5", roughness: 0.45)))
                }
            }
            // Bases: a rounded box each, the purine's longer; their letters; the H-bond dashes across the gap.
            let d0 = frames.values.first?.length ?? 10.86
            let purineLength = purineShare * (d0 - gap), pyrimidineLength = (1 - purineShare) * (d0 - gap)
            let boxes = [true: DNAMesh.roundedBox(BioVec(purineLength / 2, boxThickness / 2, boxWidth / 2), corner: 0.42),
                         false: DNAMesh.roundedBox(BioVec(pyrimidineLength / 2, boxThickness / 2, boxWidth / 2), corner: 0.42)]
            for k in 0..<n {
                guard let f = frames[k], let c1 = at(1, k, "C1'"), let c2 = at(2, k, "C1'") else { continue }
                let b1 = bases[k], b2 = BioSeq.pair(b1, rna: false)
                let l1 = DNAModel.isPurine(b1) ? purineLength : pyrimidineLength
                let l2 = (d0 - gap) - l1
                let side = f.w * f.offW + f.n * f.offN
                for (s, b, start, dir, len) in [(1, b1, c1, f.u, l1), (2, b2, c2, -f.u, l2)] {
                    let centre = start + dir * (len / 2) + side
                    let box = boxes[DNAModel.isPurine(b)] ?? BioMesh()
                    // (x along the pair, y along the axis, z across: s = 2's box turned round, so it stays right-handed)
                    let frame = BioFrame(origin: centre, x: dir, y: f.n, z: BioV.cross(dir, f.n))
                    items.append(BioSceneItem(id: "pair:\(k):\(s):base", mesh: box.transformed(frame),
                                              material: BioMaterial(BioPalette.base(b).hex(dark: o.dark), roughness: 0.4)))
                    let ink = (o.dark || b == "G") ? "#1c1c1e" : "#ffffff"
                    marks.append(DNATextMark(text: String(b), pos: centre, size: 2.2, hex: ink, nearOnly: 60, lift: 2.4))
                }
                let open = opening(k, of: n, unzip: o.unzip)
                if o.hBonds && open < 0.95 {
                    let g1 = c1 + f.u * l1, g2 = c2 - f.u * l2
                    let count = BioBDNA.hBondAtoms(b1).count
                    // (evenly across the box's width, major-groove side first, as the bonds themselves lie)
                    let slots: [Double] = count == 2 ? [0.9, -0.9] : [1.35, 0, -1.35]
                    var mesh = BioMesh()
                    for slot in slots {
                        let lateral = f.w * (f.offW + slot) + f.n * f.offN
                        mesh.append(BioMoleculeMeshes.dashes(from: g1 + lateral - f.u * 0.05, to: g2 + lateral + f.u * 0.05, radius: 0.13, dash: 0.3, gap: 0.2))
                    }
                    let alpha = open > 0 ? max(1, ((1 - open) * 4).rounded()) / 4 : 1
                    items.append(BioSceneItem(id: "pair:\(k):hb", mesh: mesh, material: BioMaterial("#ffffff", alpha: alpha, roughness: 0.5, emission: 0.25)))
                }
            }
        } else {
            // The atom views: every heavy atom, as the chemistry builder draws it; each atom picked as its nucleotide's.
            var model = d.model
            for i in model.atoms.indices { model.atoms[i].pos = pos[i] }
            let style = o.style.atomStyle
            var atomItems = BioMoleculeMeshes.items(model, style: style, radius: { look.radius($0.element, style) },
                                                    color: { look.colour($0.element) })
            for j in atomItems.indices {
                guard let id = atomItems[j].id, id.hasPrefix("atom:"), let i = Int(id.dropFirst(5)) else { continue }
                let a = d.atoms[i]
                atomItems[j].id = "pair:\(a.pair):\(a.strand):\(backboneNames.contains(a.name) ? "bb" : "base"):a\(i)"
            }
            items += atomItems
            if o.hBonds {
                var byPair: [Int: BioMesh] = [:]
                for h in d.hBonds where opening(h.pair, of: n, unzip: o.unzip) < 0.95 {
                    byPair[h.pair, default: BioMesh()].append(BioMoleculeMeshes.dashes(from: pos[h.a], to: pos[h.b], radius: 0.08, dash: 0.3, gap: 0.2))
                }
                for (k, mesh) in byPair.sorted(by: { $0.key < $1.key }) {
                    let open = opening(k, of: n, unzip: o.unzip)
                    let alpha = open > 0 ? max(1, ((1 - open) * 4).rounded()) / 4 : 1
                    items.append(BioSceneItem(id: "pair:\(k):hb", mesh: mesh, material: BioMaterial(BioPalette.hBond.hex(dark: o.dark), alpha: alpha)))
                }
            }
        }

        // Ends: an arrowhead 5′→3′ along each backbone every five nucleotides, and each end's 5′ or 3′.
        if o.ends {
            let outward = o.style == .cartoon ? 0.0 : (o.style == .spaceFill ? 3.6 : 2.4)
            // (on the cartoon's tube a long arrowhead; beside the atoms a smaller one)
            let (back, ahead, radius) = o.style == .cartoon ? (2.6, 3.4, 2.1) : (1.6, 2.4, 1.25)
            for (si, s) in [1, 2].enumerated() {
                let path = splines[si]
                let spans = paths[si].pairs.count
                var chosen = Array(stride(from: 2, to: spans, by: 5))
                if chosen.isEmpty { chosen = [spans / 2] }
                for i in chosen {
                    let mid = min(i * samples + samples / 2, path.count - 2)
                    let dir = BioV.unit(path[min(mid + 1, path.count - 1)] - path[max(mid - 1, 0)])
                    let radial = BioV.unit(BioVec(path[mid].x, 0, path[mid].z))
                    let c = path[mid] + radial * outward
                    let k = paths[si].pairs[i]
                    items.append(BioSceneItem(id: "pair:\(k):\(s):bb:arrow", mesh: DNAMesh.cone(from: c - dir * back, to: c + dir * ahead, radius: radius),
                                              material: BioMaterial(strandHex[s] ?? "#7d5cf5", roughness: 0.4)))
                }
            }
            // (strand 1: 5′ at the bottom, 3′ at the top; strand 2 the other way round — antiparallel)
            let ends: [(Int, Int, String, String, Double)] = [(1, 0, "P", "5′", -1), (1, n - 1, "O3'", "3′", 1),
                                                             (2, n - 1, "P", "5′", 1), (2, 0, "O3'", "3′", -1)]
            for (s, k, name, text, up) in ends {
                guard let p = at(s, k, name) else { continue }
                let radial = BioV.unit(BioVec(p.x, 0, p.z))
                marks.append(DNATextMark(text: text, pos: p + BioVec(0, 3.2 * up, 0) + radial * (1.6 + outward), size: 3.8,
                                         hex: strandHex[s] ?? "#7d5cf5", onTop: true))
            }
        }

        // Grooves: translucent bands between the backbones, the narrow side (minor) and the wide (major).
        let top = Double(n - 1) * BioBDNA.rise + 4.2
        if o.grooves && o.unzip <= 0 && n >= 2 {
            let y0 = 1.2, y1 = Double(n - 1) * BioBDNA.rise + 3.0
            for major in [false, true] {
                // (the middle of each groove, so each reads as a band winding round the helix, not a sleeve)
                let width = major ? 360 - minorArc : minorArc
                let margin = width * 0.2
                let from = major ? minorArc + margin : margin, to = major ? 360 - margin : minorArc - margin
                let mesh = grooveBand(y0: y0, y1: y1, from: from, to: to, radius: grooveRadius)
                let hex = major ? (o.dark ? "#7d7aff" : "#5e5ce6") : (o.dark ? "#ff7aa8" : "#e8508a")
                items.append(BioSceneItem(id: major ? "groove:major" : "groove:minor", mesh: mesh,
                                          material: BioMaterial(hex, alpha: 0.3, roughness: 0.25), order: major ? 2 : 1))
                // (labelled where the groove's middle faces the camera most)
                let midAngle = (from + to) / 2
                var bestY = (y0 + y1) / 2, best = -2.0
                var y = y0 + 3
                while y <= y1 - 3 {
                    let t = (y / BioBDNA.rise * BioBDNA.twist + midAngle) * .pi / 180
                    if cos(t) > best { best = cos(t); bestY = y }
                    y += 0.5
                }
                let t = (bestY / BioBDNA.rise * BioBDNA.twist + midAngle) * .pi / 180
                let normal = BioVec(sin(t), 0, cos(t))
                labels.append(BioLabel3D(id: major ? "groove:major" : "groove:minor", text: major ? "Major groove" : "Minor groove",
                                         anchor: normal * grooveRadius + BioVec(0, bestY, 0), normal: normal, area: 2))
            }
        }

        // Measurements: the width, the rise between base pairs and a turn, as dimension lines with their words.
        if o.measure {
            let ink = o.dark ? "#d1d1d6" : "#3a3a3c"
            var mesh = BioMesh()
            let yd = top + 5
            mesh.append(DNAMesh.line(BioVec(-10, yd, 0), BioVec(10, yd, 0)))
            for x in [-10.0, 10.0] { mesh.append(DNAMesh.line(BioVec(x, yd - 1.2, 0), BioVec(x, yd + 1.2, 0))) }
            // (the words at 3–3.2 Å: at 2.3–2.6 they were too small to read at the narrow window's size)
            marks.append(DNATextMark(text: "2 nm wide", pos: BioVec(0, yd + 3, 0), size: 3.2, hex: ink, onTop: true, bold: false))
            func pairHeight(_ k: Int) -> Double {
                let ys = d.atoms.filter { $0.pair == k && !backboneNames.contains($0.name) }.map(\.pos.y)
                return ys.isEmpty ? Double(k) * BioBDNA.rise + 2 : ys.reduce(0, +) / Double(ys.count)
            }
            if n >= 2 {
                // (a fifth of the way up a long helix, under the labels the middle's backbones have on that side)
                let k = n >= 10 ? n / 5 : max(0, n / 2 - 1)
                let ya = pairHeight(k), yb = pairHeight(k + 1)
                mesh.append(DNAMesh.line(BioVec(13, ya, 0), BioVec(13, yb, 0)))
                for y in [ya, yb] { mesh.append(DNAMesh.line(BioVec(10.5, y, 0), BioVec(14, y, 0))) }
                marks.append(DNATextMark(text: "0.34 nm between base pairs", pos: BioVec(15.5, (ya + yb) / 2, 0), size: 3, hex: ink,
                                         align: .leading, onTop: true, bold: false))
            }
            if Double(n - 1) * BioBDNA.rise >= pitch - 0.01 {
                let ya = 0.0, yb = pitch
                mesh.append(DNAMesh.line(BioVec(-13, ya, 0), BioVec(-13, yb, 0)))
                for y in [ya, yb] { mesh.append(DNAMesh.line(BioVec(-14, y, 0), BioVec(-10.5, y, 0))) }
                marks.append(DNATextMark(text: "≈3.6 nm per turn (10.5 bp)", pos: BioVec(-15.5, (ya + yb) / 2, 0), size: 3, hex: ink,
                                         align: .trailing, onTop: true, bold: false))
            }
            items.append(BioSceneItem(id: nil, mesh: mesh, material: BioMaterial(ink, unlit: true)))
        }

        // Labels with leader lines: each backbone, a base pair and its H-bonds, where they face the camera.
        let middle = n >= 3 ? Array((n / 3)...max(n / 3, (2 * n) / 3)) : Array(0..<n)
        func front(_ s: Int, _ name: String, in ks: [Int]) -> Int? {
            ks.max { (at(s, $0, name)?.z ?? -99) < (at(s, $1, name)?.z ?? -99) }
        }
        for s in [1, 2] {
            if let k = front(s, "P", in: middle), let p = at(s, k, "P") {
                labels.append(BioLabel3D(id: "pair:\(k):\(s):bb", text: "Backbone · strand \(s)", anchor: p, area: 3))
            }
        }
        // (each strand's 5′-terminal phosphate, kept and named, §6.7)
        if o.ends {
            for (s, k) in [(1, 0), (2, n - 1)] {
                let id = "pair:\(k):\(s):bb"
                if let p = at(s, k, "P"), !labels.contains(where: { $0.id == id }) {
                    labels.append(BioLabel3D(id: id, text: "5′ phosphate · strand \(s)", anchor: p, area: 1.5))
                }
            }
        }
        func baseCentre(_ k: Int, strand s: Int) -> BioVec? {
            let ps = d.atoms.indices.filter { d.atoms[$0].pair == k && d.atoms[$0].strand == s && !backboneNames.contains(d.atoms[$0].name) }.map { pos[$0] }
            return ps.isEmpty ? nil : ps.reduce(BioVec(0, 0, 0), +) / Double(ps.count)
        }
        if let kb = middle.max(by: { (baseCentre($0, strand: 1)?.z ?? -99) < (baseCentre($1, strand: 1)?.z ?? -99) }),
           let c = baseCentre(kb, strand: 1) {
            let b1 = bases[kb]
            labels.append(BioLabel3D(id: "pair:\(kb)", text: "Base pair \(b1)·\(BioSeq.pair(b1, rna: false))", anchor: c, area: 2))
            let kh = kb + 1 < n ? kb + 1 : kb - 1
            if o.hBonds, kh >= 0, opening(kh, of: n, unzip: o.unzip) < 0.95 {
                let hs = d.hBonds.filter { $0.pair == kh }
                if !hs.isEmpty {
                    let m = hs[hs.count / 2]
                    labels.append(BioLabel3D(id: "pair:\(kh):hb", text: "Hydrogen bonds (\(hs.count))", anchor: (pos[m.a] + pos[m.b]) / 2, area: 1))
                }
            }
        }

        // (the camera's ball: the helix with room for its ends and measurements)
        let yLo = -4.0, yHi = top + 8
        let half = (yHi - yLo) / 2
        let frame = BioSphere(centre: BioVec(0, (yLo + yHi) / 2, 0), radius: (half * half + 14 * 14).squareRoot() * 0.98)
        return DNAScene(content: BioSceneContent(items: items, labels: labels, frame: frame), marks: marks)
    }

    /// A groove's band: part of a cylinder of `radius` about the axis, from `from`° to `to`° past strand 1's backbone
    /// at each height (so it winds with the helix), from y0 to y1.
    static func grooveBand(y0: Double, y1: Double, from: Double, to: Double, radius: Double) -> BioMesh {
        var m = BioMesh()
        let rows = max(2, Int(((y1 - y0) / 0.8).rounded(.up)))
        let cols = max(2, Int(((to - from) / 9).rounded(.up)))
        for i in 0...rows {
            let y = y0 + (y1 - y0) * Double(i) / Double(rows)
            let base = y / BioBDNA.rise * BioBDNA.twist
            for j in 0...cols {
                let t = (base + from + (to - from) * Double(j) / Double(cols)) * .pi / 180
                let normal = BioVec(sin(t), 0, cos(t))
                m.vertex(normal * radius + BioVec(0, y, 0), normal)
            }
        }
        let w = cols + 1
        for i in 0..<rows {
            for j in 0..<cols {
                let a = UInt32(i * w + j), b = UInt32(i * w + j + 1), c = UInt32((i + 1) * w + j + 1), dd = UInt32((i + 1) * w + j)
                m.triangle(a, b, c)
                m.triangle(a, c, dd)
            }
        }
        return m
    }

    // MARK: Checks

    static func checks(_ r: inout BioCheckReport) {
        let seq = DNAModel.defaultSequence
        let n = seq.count
        let cartoon = build(DNAHelixOptions(sequence: seq, style: .cartoon, ends: true, grooves: true, measure: true))
        let items = cartoon.content.items

        // (right-handed: going along each backbone, the turn about +Y and the climb along it agree in sign)
        func handedness(_ strand: Int) -> Bool {
            let order = strand == 1 ? Array(0..<n) : Array((0..<n).reversed())
            let d = BioBDNA.build(seq)
            let ps = order.compactMap { d.pos(strand: strand, pair: $0, name: "P") }
            return zip(ps, ps.dropFirst()).allSatisfy { a, b in (a.z * b.x - a.x * b.z) * (b.y - a.y) > 0 }
        }
        r.check("dna.helix.right-handed", "\(handedness(1)),\(handedness(2))", handedness(1) && handedness(2))
        r.near("dna.helix.turn", 10.5 * BioBDNA.twist, 360, 1e-9, digits: 4)
        r.near("dna.helix.pitch", pitch, 35.7, 1e-9, digits: 3)

        // (the cartoon: a backbone piece and a base box per nucleotide; 2 dashes for A·T, 3 for G·C)
        let bb = items.filter { ($0.id ?? "").hasSuffix(":bb") }.count
        let boxes = items.filter { ($0.id ?? "").hasSuffix(":base") }.count
        r.check("dna.cartoon.parts", "\(bb)bb,\(boxes)bases", bb == 2 * n && boxes == 2 * n)
        let gc = seq.filter { $0 == "G" || $0 == "C" }.count
        let dashLines = items.filter { ($0.id ?? "").hasSuffix(":hb") }.count
        let facts = (0..<n).compactMap { DNAHelix.pairFacts(seq, pair: $0) }
        let wanted = 2 * (n - gc) + 3 * gc
        r.check("dna.cartoon.H-bonds", "\(facts.map(\.hBonds.count).reduce(0, +))", dashLines == n && facts.map(\.hBonds.count).reduce(0, +) == wanted)
        // (the purine's box 58% of C1′–C1′ less the gap, the pyrimidine's 42%: with the gap they fill it exactly)
        if let f = facts.first {
            let purine = purineShare * (f.c1c1 - gap), pyrimidine = (1 - purineShare) * (f.c1c1 - gap)
            r.near("dna.cartoon.box-lengths", purine + gap + pyrimidine, f.c1c1, 1e-9, digits: 3)
            r.near("dna.C1'-C1'", f.c1c1, 10.86, 0.02, digits: 3)
            r.check("dna.pair-facts", "\(f.name),\(f.hBonds.count),N1-N3=\(String(format: "%.2f", f.hBonds.last?.distance ?? 0))",
                    f.name == "A·T" && f.hBonds.count == 2 && abs((f.hBonds.last?.distance ?? 0) - 2.91) < 0.01 && f.number2 == n)
        }
        if let g = facts.first(where: { $0.base1 == "G" }) {
            r.check("dna.pair-facts.GC", "\(g.name),\(g.hBonds.map { String(format: "%.2f", $0.distance) }.joined(separator: ","))",
                    g.hBonds.count == 3 && abs(g.hBonds[0].distance - 2.87) < 0.01 && abs(g.hBonds[1].distance - 2.79) < 0.01 && abs(g.hBonds[2].distance - 3.00) < 0.01)
        }

        // (grooves: 148° and 212° of arc; 6.6 and 11.6 Å wide less the phosphates)
        r.near("dna.groove.minor-arc", minorArc, 148.45, 0.05, digits: 2)
        r.near("dna.groove.minor", grooveWidths.minor, 6.6, 0.1, digits: 2)
        r.near("dna.groove.major", grooveWidths.major, 11.6, 0.1, digits: 2)
        r.check("dna.groove.bands", "\(items.filter { ($0.id ?? "").hasPrefix("groove:") }.count)",
                items.filter { ($0.id ?? "").hasPrefix("groove:") }.count == 2 && cartoon.content.labels.filter { $0.id.hasPrefix("groove:") }.count == 2)

        // (antiparallel: strand 1's 5′ below its 3′, strand 2's above; the arrowheads point up on strand 1, down on 2)
        let s1 = BioPalette.strand1.hex(dark: false), s2 = BioPalette.strand2.hex(dark: false)
        func endY(_ text: String, _ hex: String) -> Double? { cartoon.marks.first { $0.text == text && $0.hex == hex }?.pos.y }
        let ends = (endY("5′", s1) ?? 0) < (endY("3′", s1) ?? 0) && (endY("5′", s2) ?? 0) > (endY("3′", s2) ?? 0)
        func arrowsUp(_ s: Int) -> [Bool] {
            items.filter { ($0.id ?? "").hasSuffix(":\(s):bb:arrow") }.map { it in
                // (the tip is the vertex furthest from the cone's centroid; it climbs or falls with the strand)
                let ps = it.mesh.positions.map { BioVec(Double($0.x), Double($0.y), Double($0.z)) }
                let c = ps.reduce(BioVec(0, 0, 0), +) / Double(max(ps.count, 1))
                let tip = ps.max { BioV.distance($0, c) < BioV.distance($1, c) } ?? c
                return tip.y > c.y
            }
        }
        let up1 = arrowsUp(1), up2 = arrowsUp(2)
        r.check("dna.antiparallel", "ends\(ends),arrows\(up1.count)+\(up2.count)", ends && !up1.isEmpty && up1.allSatisfy { $0 } && !up2.isEmpty && up2.allSatisfy { !$0 })

        // (the measurements: 2 nm, 0.34 nm, and a turn of 3.57 nm)
        let measure = cartoon.marks.filter { $0.onTop && !$0.bold }.map(\.text)
        r.check("dna.measure", measure.joined(separator: "/"), measure.count == 3 && abs(pitch - 35.7) < 1e-9)

        // (unzipping half way opens the first half's pairs 8 Å and leaves the rest)
        let d = BioBDNA.build(seq)
        let half = displaced(d, unzip: 0.5)
        func c1c1(_ k: Int, _ p: [BioVec]) -> Double {
            guard let a = d.index(strand: 1, pair: k, name: "C1'"), let b = d.index(strand: 2, pair: k, name: "C1'") else { return 0 }
            return BioV.distance(p[a], p[b])
        }
        let rest = d.atoms.map(\.pos)
        r.check("dna.unzip", String(format: "%.3f,%.3f", c1c1(0, half) - c1c1(0, rest), c1c1(n - 1, half) - c1c1(n - 1, rest)),
                abs(c1c1(0, half) - c1c1(0, rest) - openDistance) < 1e-6 && abs(c1c1(n - 1, half) - c1c1(n - 1, rest)) < 1e-9
                && opening(9, of: n, unzip: 0.5) == 1 && opening(10, of: n, unzip: 0.5) == 0)

        // (the atom view: every heavy atom, each picked as its nucleotide's base or backbone)
        let atoms = build(DNAHelixOptions(sequence: seq, style: .atoms))
        let atomItems = atoms.content.items.filter { ($0.id ?? "").contains(":a") && !($0.id ?? "").hasSuffix(":arrow") }
        let parts = atomItems.compactMap { part($0.id) }
        r.check("dna.atoms", "\(atomItems.count)", atomItems.count == d.atoms.count && parts.count == atomItems.count
                && atoms.content.items.filter { ($0.id ?? "").hasSuffix(":hb") }.count == n)

        // (picking: a base names its pair, a backbone its nucleotide, a groove itself; every label names an item)
        let picks = [part("pair:3:1:base:a12") == .pair(3), part("pair:3:2:bb") == .backbone(strand: 2, pair: 3),
                     part("pair:3:2:bb:arrow") == .backbone(strand: 2, pair: 3), part("groove:minor") == .groove(major: false),
                     part("pair:4:hb") == .pair(4), atomIndex("pair:3:1:base:a12") == 12, selection(.pair(3)) == "pair:3"]
        let labelled = (cartoon.content.labels + atoms.content.labels).allSatisfy { l in
            (cartoon.content.items + atoms.content.items).contains { BioSceneContent.matches($0.id, l.id) }
        }
        r.check("dna.pick", "\(picks.filter { $0 }.count)/\(picks.count),labels\(labelled)", picks.allSatisfy { $0 } && labelled)
    }
}
