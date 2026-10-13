import SceneKit
import SwiftUI

// The Biomolecule Builder's Carbohydrates page (spec §4.4 Carbohydrates, §6.8): five monosaccharides in their ring forms
// with their carbons numbered 1–6 and α or β read from the atoms (α- and β-glucose side by side if asked); four
// disaccharides with the glycosidic bond lit and named, split by water (hydrolysis) and joined again (dehydration
// synthesis); and the polysaccharides — amylose, amylopectin, glycogen, cellulose, chitin — as hexagon glyphs, each
// link's linkage shown on press.

struct BiomolCarbPage: View {
    @ObservedObject private var s = BiomolState.shared
    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private func look(_ page: String, atoms: Int) -> BiomolLook {
        BiomolLook(style: s.style, hydrogens: s.hydrogens(page, atoms: atoms), ph7: s.ph7, colourBy: s.colourBy == .aaClass ? .part : s.colourBy,
                   labels: s.labels, dark: scheme == .dark)
    }

    var body: some View {
        PTWorkLayout {
            VStack(alignment: .leading, spacing: 14) {
                let modes: [(id: BiomolState.CarbMode, title: String, colour: Color?)] = [
                    (id: .mono, title: "Monosaccharides", colour: nil), (id: .di, title: "Disaccharides", colour: nil), (id: .poly, title: "Polysaccharides", colour: nil),
                ]
                BiomolChoices(items: modes, selected: s.carbMode, columns: 1) { s.carbMode = $0 }
                switch s.carbMode {
                case .mono: monoControls
                case .di: diControls
                case .poly: polyControls
                }
            }
        } centre: {
            switch s.carbMode {
            case .mono: monoScene
            case .di: diScene
            case .poly: polyScene
            }
        } right: {
            VStack(alignment: .leading, spacing: 14) {
                switch s.carbMode {
                case .mono: monoFacts
                case .di: diFacts
                case .poly: polyFacts
                }
            }
        }
    }

    // MARK: - Monosaccharides

    private var monoControls: some View {
        let items: [(id: String, title: String, colour: Color?)] = BiomolSugar.monosaccharides.map { (id: $0, title: BiomolSugar.shortName($0), colour: nil) }
        let atoms = BioData.molecule(s.mono)?.atoms.count ?? 0
        return VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 8) {
                BioHeading("Sugar")
                BiomolChoices(items: items, selected: s.mono, columns: 2) { s.mono = $0 }
                Toggle("α- and β-glucose side by side", isOn: $s.compareAnomers)
                    .font(.sCallout)
                    .help("Both glucose rings together: only C1's –OH differs")
            }
            BiomolLookControls(page: "sugar", atoms: atoms, colours: [.element, .part], noPH: "Sugars have no groups that ionise at pH 7.", handOff: {
                guard let m = BioData.molecule(s.compareAnomers ? "GLC" : s.mono) else { return nil }
                return (m.model(ph7: s.ph7), BiomolSugar.shortName(m.code))
            })
        }
    }

    private var monoScene: some View {
        let pair = s.compareAnomers
        let code = s.mono
        let mol = BioData.molecule(code)
        let lk = look("sugar", atoms: mol?.atoms.count ?? 24)
        let chem = BiomolNodes.chem
        let ring = mol.flatMap { BiomolSugar.ring($0) }
        let alpha = mol.flatMap { m in ring.map { BiomolSugar.isAlpha(m, $0) } }
        return BiomolSceneCard(
            title: pair ? "α- and β-glucose" : BiomolSugar.shortName(code),
            subtitle: pair ? "The same atoms; C1's –OH on opposite faces" : (mol?.title ?? ""),
            badge: pair ? nil : (alpha == true ? "α anomer" : (alpha == false ? "β anomer" : nil)),
            key: "mono/\(pair ? "pair" : code)/\(lk.key)",
            frameKey: "mono/\(pair ? "pair" : code)",
            build: BioSceneBuild { pair ? BiomolScenes.anomerPair(look: lk, chem: chem) : BiomolScenes.monosaccharide(code, look: lk, chem: chem) },
            extras: {
                guard lk.labels && lk.style != .sticks else { return [] }
                let codes = pair ? ["GLC", "BGC"] : [code]
                var nodes: [SCNNode] = []
                for c in codes {
                    guard let m = BioData.molecule(c) else { continue }
                    let shift = pair ? BiomolScenes.anomerShift(c) : BioVec(0, 0, 0)
                    nodes += BiomolNodes.atomLabels(m.model(ph7: lk.ph7).moved(by: shift), look: lk) { a in
                        // (ring carbons by number, 1–6)
                        a.element == "C" ? a.name.filter { $0.isNumber } : a.element
                    }
                }
                return nodes
            },
            accessibility: pair ? "3D models of α-glucose and β-glucose side by side; their C1 hydroxyl groups point to opposite faces of the ring."
                : "3D model of \(BiomolSugar.shortName(code).lowercased()), a \(ring?.atoms.count ?? 6)-membered ring with its carbons numbered.")
    }

    private var monoFacts: some View {
        let code = s.mono
        let mol = BioData.molecule(code)
        let ring = mol.flatMap { BiomolSugar.ring($0) }
        let alpha = mol.flatMap { m in ring.map { BiomolSugar.isAlpha(m, $0) } } ?? false
        let ketose = code == "FRU"
        return VStack(alignment: .leading, spacing: 14) {
            if let id = s.selection, let k = BiomolText.atomIndex(id), let m = mol, !s.compareAnomers {
                let model = m.model(ph7: s.ph7)
                if k < model.atoms.count {
                    BiomolAtomCard(title: "\(BiomolSugar.shortName(code)) · \(model.atoms[k].name)", element: model.atoms[k].element,
                                   detail: BiomolScenes.sugarPart(model.atoms[k].name, in: m, ring: ring).name)
                }
            }
            BioCard(title: "α or β", swatch: BiomolScenes.anomericPart.colour.color) {
                Text("α has C1's –OH on the opposite face of the ring from C6 (trans); β on the same face (cis). That one difference decides starch (α links) from cellulose (β links).")
                    .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                if !s.compareAnomers, let r = ring {
                    BioFactRow(label: "This one", text: "C\(r.anomeric.filter { $0.isNumber })'s –OH is on the \(alpha ? "opposite face to" : "same face as") C\(r.reference.filter { $0.isNumber }): \(alpha ? "α" : "β").")
                }
            }
            if !s.compareAnomers {
                BioCard(title: BiomolSugar.shortName(code), subtitle: mol?.title) {
                    BiomolFacts(rows: [
                        ("Formula", BiomolText.formula(mol?.formula ?? "")),
                        ("Molar mass", String(format: "%.2f g/mol", mol?.mass ?? 0)),
                        ("Ring", "\(ring?.atoms.count ?? 0) atoms (\(ring?.atoms.count == 5 ? "furanose" : "pyranose"))"),
                        ("Kind", ketose ? "Ketose (C=O at C2 when open)" : "Aldose (C=O at C1 when open)"),
                    ])
                    BioFactRow(label: "Numbering", text: "Ring carbons are numbered 1–6 from the end nearest the C=O. (1′–5′ is for the sugar of a nucleotide.)")
                    if code == "BDR" { BioFactRow(label: "In RNA", text: "Ribose is RNA's sugar; DNA's deoxyribose lacks the O on C2.") }
                    if code == "GAL" { BioFactRow(label: "Galactose", text: "Glucose with C4's –OH on the other face. With glucose it makes lactose.") }
                    if code == "FRU" { BioFactRow(label: "Fructose", text: "A five-membered ring: its C=O was at C2. With glucose it makes sucrose.") }
                }
            }
            BiomolPartsList(items: s.compareAnomers ? [(id: "alpha", title: "α-glucose"), (id: "beta", title: "β-glucose")]
                            : [(id: "part:ring", title: "Ring"), (id: "part:anomeric", title: "Anomeric –OH"),
                               (id: "part:ch2oh", title: "CH₂OH"), (id: "part:oh", title: "Other –OH groups")])
            BiomolColourLegend(items: s.colourBy == .element ? BiomolColourLegend.elements(["C", "H", "O"])
                               : BiomolColourLegend.parts([BiomolScenes.ringPart, BiomolScenes.anomericPart, BiomolScenes.referencePart, BiomolScenes.hydroxylPart]))
        }
    }

    // MARK: - Disaccharides

    private var diControls: some View {
        let items: [(id: String, title: String, colour: Color?)] = BiomolSugar.disaccharides.map { (id: $0, title: BiomolSugar.shortName($0), colour: nil) }
        let atoms = BioData.molecule(s.di)?.atoms.count ?? 0
        return VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 8) {
                BioHeading("Disaccharide")
                BiomolChoices(items: items, selected: s.di, columns: 2) { s.di = $0 }
                Button {
                    s.toggleSplit()
                } label: {
                    Label(s.split ? "Join them (release water)" : "Hydrolyse (add water)", systemImage: s.split ? "link" : "drop")
                        .frame(maxWidth: .infinity)
                }
                .glassButton(prominent: s.split)
                .controlSize(.large)
                .disabled(s.motion([.sugarSplit, .sugarJoined]) != nil)
                .help(s.split ? "Dehydration synthesis: the glycosidic bond forms and water is released"
                      : "Hydrolysis: water splits the glycosidic bond into two monosaccharides")
            }
            BiomolLookControls(page: "sugar2", atoms: atoms, colours: [.element, .part], noPH: "Sugars have no groups that ionise at pH 7.", handOff: {
                guard let m = BioData.molecule(s.di) else { return nil }
                if s.split, let sp = BiomolSugar.split(m) { return (sp.model, "\(BiomolSugar.shortName(m.code)), hydrolysed") }
                return (m.model(ph7: s.ph7), BiomolSugar.shortName(m.code))
            })
        }
    }

    private var diScene: some View {
        let code = s.di
        let split = s.split
        let mol = BioData.molecule(code)
        let lk = look("sugar2", atoms: mol?.atoms.count ?? 45)
        let chem = BiomolNodes.chem
        let motion = reduceMotion ? nil : s.motion([.sugarSplit, .sugarJoined])
        let names = BiomolSugar.residues[code] ?? ("", "")
        let frame = BiomolScenes.disaccharideFrame(code)
        let linkage = BiomolSugar.linkage[code] ?? ""
        return BiomolSceneCard(
            title: split ? "\(names.first) + \(names.second)" : BiomolSugar.shortName(code),
            subtitle: split ? "Split by water (hydrolysis)" : "\(names.first) \(linkage) \(names.second)",
            badge: split ? "+ H₂O" : "\(linkage) glycosidic bond",
            key: "di/\(code)/\(split)/\(lk.key)/\(motion?.serial ?? 0)",
            frameKey: "di/\(code)",
            build: BioSceneBuild {
                if motion != nil { return BioSceneContent(items: [], labels: [], frame: frame) }
                var c = BiomolScenes.disaccharide(code, split: split, look: lk, chem: chem)
                c.frame = frame
                return c
            },
            extras: {
                if let m = motion { return BiomolNodes.motion(BiomolScenes.disaccharideMotion(code, toSplit: m.kind == .sugarSplit, look: lk, chem: chem)) }
                guard lk.labels && lk.style != .sticks, let mol else { return [] }
                let model = split ? (BiomolSugar.split(mol)?.model ?? mol.model()) : mol.model(ph7: lk.ph7)
                return BiomolNodes.atomLabels(model, look: lk) { a in
                    a.element == "C" ? a.name.filter { $0.isNumber } : a.element
                }
            },
            accessibility: split ? "3D model of \(names.first.lowercased()) and \(names.second.lowercased()) apart, after water split the glycosidic bond."
                : "3D model of \(BiomolSugar.shortName(code).lowercased()): \(names.first.lowercased()) and \(names.second.lowercased()) joined by a \(linkage) glycosidic bond.")
    }

    private var diFacts: some View {
        let code = s.di
        let mol = BioData.molecule(code)
        let names = BiomolSugar.residues[code] ?? ("", "")
        let linkage = BiomolSugar.linkage[code] ?? ""
        let g = mol.flatMap { BiomolSugar.glycosidic($0) }
        return VStack(alignment: .leading, spacing: 14) {
            if let id = s.selection, id.hasPrefix("glyco") {
                BioCard(title: "Glycosidic bond", swatch: BiomolParts.bondHighlight.color) {
                    Text("C–O–C between two sugars: one sugar's C1 (its anomeric carbon) and an –OH of the other, joined as water leaves.")
                        .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
            }
            BioCard(title: BiomolSugar.shortName(code), subtitle: mol?.title) {
                BiomolFacts(rows: [
                    ("Made of", "\(names.first) + \(names.second)"),
                    ("Linkage", linkage),
                    ("Bridging O", g?.o ?? "—"),
                    ("Formula", BiomolText.formula(mol?.formula ?? "")),
                    ("Molar mass", String(format: "%.2f g/mol", mol?.mass ?? 0)),
                ])
                BioFactRow(label: "Reading the linkage", text: linkageText(code))
                BioFactRow(label: "Making and breaking it", text: "Dehydration synthesis joins the two and releases one H₂O (C₆H₁₂O₆ + C₆H₁₂O₆ → C₁₂H₂₂O₁₁ + H₂O). Hydrolysis uses one H₂O to split them.")
            }
            if s.split {
                BioCard(title: "After hydrolysis", swatch: BiomolParts.bondHighlight.color) {
                    Text("The water's –OH went to \(names.first.lowercased())'s anomeric carbon and its H to the bridging O, now an –OH of \(names.second.lowercased()).")
                        .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
            }
            BiomolPartsList(items: [(id: "res:0", title: names.first), (id: "res:1", title: names.second)]
                            + (s.split ? [] : [(id: "glyco", title: "Glycosidic bond")]))
            BiomolColourLegend(items: s.colourBy == .element ? BiomolColourLegend.elements(["C", "H", "O"])
                               : [(BiomolSugar.residueColours[0].color, names.first), (BiomolSugar.residueColours[1].color, names.second),
                                  (BiomolParts.bondHighlight.color, s.split ? "Atoms from water" : "Glycosidic bond")])
        }
    }

    private func linkageText(_ code: String) -> String {
        switch code {
        case "MAL": return "α(1→4): C1 of the first glucose, its bond on the α face, to C4 of the second. The link in starch."
        case "CBI": return "β(1→4): C1 of the first glucose, on the β face, to C4 of the second — every other glucose flipped. The link in cellulose."
        case "SUC": return "α(1→2)β: glucose's C1 (α) to fructose's C2 (β): both anomeric carbons are used, so sucrose has no free one."
        case "LAT": return "β(1→4): galactose's C1 (β face) to glucose's C4. Milk sugar."
        default: return ""
        }
    }

    // MARK: - Polysaccharides

    private var polyControls: some View {
        let items: [(id: BiomolPolysaccharide, title: String, colour: Color?)] = BiomolPolysaccharide.allCases.map { (id: $0, title: $0.short, colour: nil) }
        return VStack(alignment: .leading, spacing: 14) {
            BioHeading("Polysaccharide")
            BiomolChoices(items: items, selected: s.poly, columns: 2) { s.poly = $0 }
            BiomolNote(text: "Each hexagon is one sugar ring. Press a link to see its linkage.")
        }
    }

    private var polyScene: some View {
        BiomolFlatCard(title: s.poly.title, subtitle: s.poly == .cellulose || s.poly == .chitin ? "β(1→4): straight chains" : "α(1→4) chains", hint: "Press a link to see its linkage") {
            BiomolPolyDiagram(kind: s.poly)
        }
    }

    private var polyFacts: some View {
        let p = s.poly
        return VStack(alignment: .leading, spacing: 14) {
            BioCard(title: p.title) {
                Text(p.summary).font(.sCallout).fixedSize(horizontal: false, vertical: true)
            }
            BioCard(title: "Starch, glycogen and cellulose") {
                BioFactRow(label: "Storage (α links)", text: "Starch (amylose and amylopectin) in plants, glycogen in animals: easy for enzymes to take apart.")
                BioFactRow(label: "Structure (β links)", text: "Cellulose in plant cell walls, chitin in exoskeletons: straight chains that pack and H-bond. Most animals cannot digest β(1→4) links.")
            }
        }
    }
}

/// A polysaccharide as hexagon glyphs: each sugar a hexagon with its ring O (flipped ones upside down, N-acetyl ones
/// tagged "NAc"), the links between them; pressing a link shows its linkage.
struct BiomolPolyDiagram: View {
    let kind: BiomolPolysaccharide
    @State private var shown: Int?

    var body: some View {
        GeometryReader { g in
            let l = kind.layout
            let margin: CGFloat = 44
            let unit = min((g.size.width - 2 * margin) / CGFloat(max(l.width, 1)), (g.size.height - 2 * margin) / CGFloat(max(l.height + 1, 1)), 64)
            let ox = (g.size.width - CGFloat(l.width) * unit) / 2 + unit / 2
            let oy = (g.size.height - CGFloat(l.height) * unit) / 2 + unit * 0.3
            let pts = l.glyphs.map { CGPoint(x: ox + CGFloat($0.x) * unit, y: oy + CGFloat($0.y) * unit) }
            let r = unit * 0.34
            ZStack(alignment: .topLeading) {
                Canvas { ctx, _ in
                    for link in l.links {
                        let a = pts[link.from], b = pts[link.to]
                        var path = Path()
                        path.move(to: a)
                        if link.kind == .branch {
                            path.addLine(to: CGPoint(x: a.x, y: (a.y + b.y) / 2))
                            path.addLine(to: CGPoint(x: b.x, y: (a.y + b.y) / 2))
                            path.addLine(to: b)
                        } else {
                            path.addLine(to: b)
                        }
                        let colour: Color = link.kind == .hBond ? BioPalette.hBond.color : (link.kind == .branch ? BiomolParts.bondHighlight.color : Color.primary.opacity(0.55))
                        let style = link.kind == .hBond || link.kind == .gap ? StrokeStyle(lineWidth: 1.5, dash: [4, 3]) : StrokeStyle(lineWidth: 2)
                        ctx.stroke(path, with: .color(colour), style: style)
                    }
                    for (k, glyph) in l.glyphs.enumerated() {
                        let c = pts[k]
                        var hex = Path()
                        for j in 0..<6 {
                            let ang = Double(j) * .pi / 3 + .pi / 6
                            let p = CGPoint(x: c.x + r * CGFloat(cos(ang)), y: c.y + r * CGFloat(sin(ang)) * (glyph.flipped ? -1 : 1))
                            if j == 0 { hex.move(to: p) } else { hex.addLine(to: p) }
                        }
                        hex.closeSubpath()
                        ctx.fill(hex, with: .color(BiomolSugar.residueColours[0].color.opacity(0.22)))
                        ctx.stroke(hex, with: .color(BiomolSugar.residueColours[0].color), lineWidth: 1.6)
                        // (the ring O at the upper right corner — the lower right when flipped)
                        let oAngle = -Double.pi / 6
                        let o = CGPoint(x: c.x + r * CGFloat(cos(oAngle)), y: c.y + r * CGFloat(sin(oAngle)) * (glyph.flipped ? -1 : 1))
                        ctx.draw(Text("O").font(.system(size: max(8, r * 0.42), weight: .bold)).foregroundColor(.red), at: o)
                        if let tag = glyph.tag {
                            ctx.draw(Text(tag).font(.system(size: max(8, r * 0.38), weight: .semibold)).foregroundColor(.secondary),
                                     at: CGPoint(x: c.x, y: c.y + (glyph.flipped ? -1 : 1) * (r + 9)))
                        }
                    }
                }
                ForEach(l.links.indices, id: \.self) { i in
                    let link = l.links[i]
                    let a = pts[link.from], b = pts[link.to]
                    let mid = link.kind == .branch ? CGPoint(x: (a.x + b.x) / 2, y: (a.y + b.y) / 2) : CGPoint(x: (a.x + b.x) / 2, y: (a.y + b.y) / 2)
                    Button { withAnimation(Motion.snappy) { shown = shown == i ? nil : i } } label: {
                        Circle().fill(Color.primary.opacity(shown == i ? 0.25 : 0.001)).frame(width: 18, height: 18)
                    }
                    .buttonStyle(.plain)
                    .position(mid)
                    .help(link.text)
                    .accessibilityLabel("Link: \(link.text)")
                    if shown == i {
                        Text(link.text)
                            .font(.sCaption.weight(.semibold))
                            .padding(.horizontal, 8)
                            .padding(.vertical, 4)
                            .background(.regularMaterial, in: Capsule())
                            .position(x: mid.x, y: mid.y - 22)
                            .transition(.opacity)
                            .allowsHitTesting(false)
                    }
                }
                ForEach(l.notes.indices, id: \.self) { i in
                    let n = l.notes[i]
                    Text(n.text).font(.sCaption).foregroundStyle(.secondary)
                        .position(x: ox + CGFloat(n.x) * unit, y: oy + CGFloat(n.y) * unit)
                }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("\(kind.title): \(kind.summary)")
        .onChange(of: kind) { _, _ in shown = nil }
    }
}
