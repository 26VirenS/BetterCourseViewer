import SceneKit
import SwiftUI

// The Biomolecule Builder's Lipids page (spec §4.4 Lipids, §6.9): glycerol; palmitic and stearic acids (saturated,
// straight) and oleic acid (its cis double bond at C9 kinking it); tristearin with its three ester bonds; POPC, a
// phospholipid, its hydrophilic head and hydrophobic tails coloured by part; cholesterol's four fused rings; stearic and
// oleic acids side by side; the saturated-versus-unsaturated card; and "Make a bilayer", which opens the Cell
// Explorer's Membrane page.

struct BiomolLipidPage: View {
    @ObservedObject private var s = BiomolState.shared
    @Environment(\.colorScheme) private var scheme

    private static let choices: [(id: String, title: String, colour: Color?)] =
        BiomolLipid.codes.map { (id: $0, title: BiomolLipid.shortName($0), colour: Color?.none) } + [(id: "STE+OLA", title: "Stearic vs oleic", colour: Color?.none)]

    private var codes: [String] { s.lipid == "STE+OLA" ? ["STE", "OLA"] : [s.lipid] }

    private var atomCount: Int { codes.compactMap { BioData.molecule($0)?.atoms.count }.reduce(0, +) }

    private func look() -> BiomolLook {
        BiomolLook(style: s.style, hydrogens: s.hydrogens("lipid", atoms: atomCount), ph7: s.ph7, colourBy: s.colourBy == .aaClass ? .part : s.colourBy,
                   labels: s.labels, dark: scheme == .dark)
    }

    var body: some View {
        PTWorkLayout {
            controls
        } centre: {
            scene
        } right: {
            facts
        }
    }

    private var controls: some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 8) {
                BioHeading("Lipid")
                BiomolChoices(items: Self.choices, selected: s.lipid, columns: 2) { s.lipid = $0 }
            }
            BiomolLookControls(page: "lipid", atoms: atomCount, colours: [.element, .part], handOff: {
                guard let m = BioData.molecule(codes[0]) else { return nil }
                return (m.model(ph7: s.ph7), BiomolLipid.shortName(m.code))
            })
            Button {
                BioNav.shared.set(.cell, "membrane")
                BioTools.open(.cell)
            } label: {
                Label("Make a bilayer", systemImage: "square.3.layers.3d").frame(maxWidth: .infinity)
            }
            .glassButton()
            .controlSize(.large)
            .help("Open the Cell Explorer's Membrane page: phospholipids in two layers, tails inward")
        }
    }

    private var scene: some View {
        let code = s.lipid
        let lk = look()
        let chem = BiomolNodes.chem
        let pair = codes.count == 2
        let mol = BioData.molecule(codes[0])
        let charged = codes.compactMap { BioData.molecule($0) }
        return BiomolSceneCard(
            title: pair ? "Stearic and oleic acids" : BiomolLipid.shortName(code),
            subtitle: pair ? "Saturated (straight) beside unsaturated (kinked)" : (mol?.title ?? ""),
            badge: badge,
            key: "lipid/\(code)/\(lk.key)",
            frameKey: "lipid/\(code)",
            build: BioSceneBuild { BiomolScenes.lipid(code, look: lk, chem: chem) },
            extras: {
                var nodes: [SCNNode] = []
                for (k, m) in charged.enumerated() {
                    let shift = pair ? BioVec(k == 0 ? -4.5 : 4.5, 0, 0) : BioVec(0, 0, 0)
                    let model = m.model(ph7: lk.ph7).moved(by: shift)
                    if lk.labels && lk.style != .sticks && model.atoms.count <= 200 { nodes += BiomolNodes.atomLabels(model, look: lk) }
                    nodes += BiomolNodes.marks(BiomolScenes.chargeMarks(model, charges: charges(m, ph7: lk.ph7), look: lk, chem: chem))
                }
                return nodes
            },
            accessibility: pair ? "3D models of stearic acid, straight, beside oleic acid, kinked at its cis double bond."
                : "3D model of \(BiomolLipid.shortName(code).lowercased()): \(mol?.title ?? "")")
    }

    /// The charges a lipid carries: a fatty acid's carboxylate at pH 7 (−1), POPC's choline N (+1) and phosphate O (−1).
    private func charges(_ m: BioMolecule, ph7: Bool) -> [String: Int] {
        switch m.code {
        case "PLM": return ph7 ? ["O1": -1] : [:]
        case "STE", "OLA": return ph7 ? ["O2": -1] : [:]
        case "POV":
            // (the phosphate O with neither H nor a carbon: the −1)
            let free = ["O13", "O14"].first { o in !m.bonds.contains { b in (b.a == o || b.b == o) && b.order == 2 } } ?? "O13"
            return ["N": 1, free: -1]
        default: return [:]
        }
    }

    private var badge: String? {
        switch s.lipid {
        case "OLA": return "cis-9: kinked"
        case "PLM", "STE": return "Saturated: straight"
        case "TGL": return "3 ester bonds"
        case "POV": return "Phospholipid"
        case "CLR": return "Steroid"
        case "STE+OLA": return "C18:0 vs C18:1"
        default: return nil
        }
    }

    private var facts: some View {
        VStack(alignment: .leading, spacing: 14) {
            if let id = s.selection { selectionCard(id) }
            if codes.count == 1, let m = BioData.molecule(codes[0]) {
                BioCard(title: BiomolLipid.shortName(m.code), subtitle: m.title) {
                    BiomolFacts(rows: [("Formula", BiomolText.formula(m.formula ?? "")), ("Molar mass", String(halfUp: "%.2f g/mol", m.mass ?? 0))])
                    ForEach(notes(m.code).indices, id: \.self) { i in
                        BioFactRow(label: notes(m.code)[i].0, text: notes(m.code)[i].1)
                    }
                }
            }
            if ["PLM", "STE", "OLA", "STE+OLA", "TGL"].contains(s.lipid) {
                BioCard(title: "Saturated vs unsaturated") {
                    BioFactRow(label: "Saturated", text: "No C=C double bonds: straight chains that pack tightly, so the fat is solid at room temperature (butter, lard).")
                    BioFactRow(label: "Unsaturated", text: "One or more cis double bonds: each kinks the chain, so they pack loosely and the fat is liquid (olive oil).")
                    BioFactRow(label: "Trans fats", text: "A trans double bond leaves the chain straight despite the double bond, so trans fats pack like saturated ones.")
                }
            }
            BiomolPartsList(items: parts)
            BiomolColourLegend(items: legend)
        }
    }

    /// The lipid's parts for the list (stearic and oleic acids: each molecule and the cis bond).
    private var parts: [(id: String, title: String)] {
        if codes.count == 2 {
            return [(id: "mol:0", title: "Stearic acid"), (id: "mol:1", title: "Oleic acid"), (id: "mol:1:cis", title: "cis double bond")]
        }
        guard let m = BioData.molecule(codes[0]) else { return [] }
        var out: [(id: String, title: String)] = []
        var seen = Set<String>()
        for a in m.atoms {
            let d = BiomolLipid.detail(a.name, in: m)
            guard !d.isEmpty, seen.insert(d).inserted else { continue }
            out.append((id: "part:" + d.lowercased().replacingOccurrences(of: " ", with: "-"), title: d))
        }
        if !BiomolLipid.esterBonds(m.code).isEmpty { out.append((id: "ester", title: "Ester bonds (\(BiomolLipid.esterBonds(m.code).count))")) }
        if !BiomolLipid.cisBonds(m.code).isEmpty { out.append((id: "cis", title: "cis double bond")) }
        return out
    }

    private func notes(_ code: String) -> [(String, String)] {
        switch code {
        case "GOL": return [("Glycerol", "Three carbons, each with an –OH. Fatty acids attach to these –OH groups by ester bonds.")]
        case "PLM": return [("Palmitic acid", "16 carbons, saturated. Its carboxyl head is polar; its long hydrocarbon tail is not."),
                            ("At pH 7", "The carboxyl group has lost its H⁺: –COO⁻.")]
        case "STE": return [("Stearic acid", "18 carbons, saturated: every carbon carries as many H as it can. All-trans, so the chain is straight.")]
        case "OLA": return [("Oleic acid", "18 carbons with one cis double bond between C9 and C10, which bends the chain by about 40–50°."),
                            ("Olive oil", "Mostly triglycerides of oleic acid: the kinks keep it liquid.")]
        case "TGL": return [("A triglyceride", "Glycerol + 3 fatty acids (here three stearic acids), joined by 3 ester bonds."),
                            ("Dehydration synthesis", "Each ester bond releases one H₂O, so making it releases 3 H₂O."),
                            ("Energy store", "Fats store about twice the energy of carbohydrates per gram.")]
        case "POV": return [("A phospholipid", "Glycerol with two fatty acids (palmitate and oleate) and a phosphate carrying choline."),
                            ("Amphipathic", "Its head (choline and phosphate, + and −) is hydrophilic; its two tails are hydrophobic."),
                            ("In membranes", "In water they form a bilayer: heads outward to the water on both sides, tails inward.")]
        case "CLR": return [("A steroid", "Four fused carbon rings with a small –OH head and a hydrocarbon tail."),
                            ("In membranes", "It sits among the phospholipid tails with its –OH at the heads, keeping the membrane's fluidity steady.")]
        default: return []
        }
    }

    private func selectionCard(_ id: String) -> some View {
        Group {
            if id.contains("ester") {
                BioCard(title: "Ester bond", swatch: BiomolParts.bondHighlight.color) {
                    Text("–C(=O)–O–C–: a fatty acid's carboxyl joined to one of glycerol's –OH, one water released for each.")
                        .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
            } else if id.contains("cis") {
                BioCard(title: "cis double bond", swatch: BioPalette.strand.color) {
                    Text("C=C with both chain parts on the same side, so the chain bends there. Each one makes an unsaturated fat pack less tightly.")
                        .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
            } else if let k = BiomolText.atomIndex(id) {
                let parts = id.split(separator: ":")
                let mi = parts.count > 1 && parts[0] == "mol" ? Int(parts[1]) ?? 0 : 0
                if mi < codes.count, let m = BioData.molecule(codes[mi]) {
                    let model = m.model(ph7: s.ph7)
                    if k < model.atoms.count {
                        BiomolAtomCard(title: "\(BiomolLipid.shortName(m.code)) · \(model.atoms[k].name)", element: model.atoms[k].element,
                                       detail: BiomolLipid.detail(model.atoms[k].name, in: m))
                    }
                }
            }
        }
    }

    private var legend: [(color: Color, text: String)] {
        var out: [(color: Color, text: String)] = []
        if s.colourBy == .element {
            out = BiomolColourLegend.elements(codes.flatMap { BioData.molecule($0)?.atoms.map(\.element) ?? [] })
        } else {
            out.append((BiomolParts.hydrophilic.colour.color, "Hydrophilic head"))
            out.append((BiomolParts.hydrophobic.colour.color, "Hydrophobic tail"))
        }
        if codes.contains("TGL") || codes.contains("POV") { out.append((BiomolParts.bondHighlight.color, "Ester bond")) }
        if codes.contains("OLA") || codes.contains("POV") { out.append((BioPalette.strand.color, "cis double bond")) }
        if s.ph7 && codes.contains(where: { ["PLM", "STE", "OLA", "POV"].contains($0) }) {
            out.append((BioPalette.basic.color, "+ charge"))
            out.append((BioPalette.acidic.color, "− charge"))
        }
        return out
    }
}
