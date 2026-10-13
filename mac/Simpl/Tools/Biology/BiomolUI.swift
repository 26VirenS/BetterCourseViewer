import SceneKit
import SwiftUI

// The Biomolecule Builder's shared pieces (spec §4.4 Shared controls, §9): a page's 3D card (its title and badge, Turn
// and Reset view, the scene with the tool's selection), the View controls every page has (style, Colour by, Show H, the
// pH 7 form, labels, Open in Molecule Builder), a grid of choices, a colour legend for what the atoms are coloured by,
// and the card for a picked atom.

/// A page's 3D card: its title (with a badge such as "pH 7 form"), Turn and Reset view, and the scene.
struct BiomolSceneCard: View {
    let title: String
    var subtitle = ""
    var badge: String?
    /// Everything the scene depends on (the page, its choices, light or dark).
    let key: String
    /// The camera frames afresh when this changes.
    var frameKey: String?
    let build: BioSceneBuild
    var extras: (() -> [SCNNode])?
    var accessibility = "3D model"
    var hint = "Drag to turn · scroll to zoom · click a part to learn about it"
    @ObservedObject private var s = BiomolState.shared

    var body: some View {
        BioSceneCard(hint: hint) {
            VStack(alignment: .leading, spacing: 3) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(title).font(.system(size: 22, weight: .bold)).lineLimit(1).minimumScaleFactor(0.7)
                    if let badge {
                        Text(badge)
                            .font(.sCaption.weight(.semibold))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .padding(.horizontal, 8)
                            .padding(.vertical, 3)
                            .background(Capsule().fill(Color.primary.opacity(0.07)))
                    }
                }
                if !subtitle.isEmpty {
                    Text(subtitle).font(.sCallout).foregroundStyle(.secondary).lineLimit(1).minimumScaleFactor(0.8)
                }
            }
            .accessibilityElement(children: .combine)
            Spacer(minLength: 8)
            BioSceneButtons(turning: $s.turning, reset: { s.reset += 1 })
        } scene: {
            BioSceneView(key: key, build: build, extras: extras, selection: $s.selection, showLabels: s.labels, turning: s.turning,
                         frameKey: frameKey, reset: s.reset, accessibility: accessibility)
        }
    }
}

/// A card shaped like the 3D card, for a page drawn in 2D (the primary structure's beads, the polysaccharides).
struct BiomolFlatCard<Content: View>: View {
    let title: String
    var subtitle = ""
    var hint = ""
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 3) {
                Text(title).font(.system(size: 22, weight: .bold)).lineLimit(1).minimumScaleFactor(0.7)
                if !subtitle.isEmpty { Text(subtitle).font(.sCallout).foregroundStyle(.secondary).lineLimit(2) }
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            content
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            if !hint.isEmpty {
                Text(hint).font(.sCaption).foregroundStyle(.secondary).frame(maxWidth: .infinity).padding(12)
            }
        }
        .card(radius: 20)
    }
}

/// The View controls every page has (§4.4 Shared controls): style, Colour by, Show H, the pH 7 form, labels, and Open in
/// Molecule Builder (the atoms and bonds handed over as they are; the builder does not reshape them).
struct BiomolLookControls: View {
    /// The page (Show H is kept per page).
    let page: String
    /// How many atoms the page's molecule has with H (Show H's default: on up to 200).
    let atoms: Int
    var colours: [BiomolColourBy] = BiomolColourBy.allCases
    /// Why Show H cannot be changed here (nil: it can).
    var noHydrogens: String?
    /// Why the pH 7 form does not apply here (nil: it does).
    var noPH: String?
    /// What "Open in Molecule Builder" hands over (nil: the button is not shown).
    var handOff: (() -> (model: BioMoleculeModel, title: String)?)?
    @ObservedObject private var s = BiomolState.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            BioHeading("View")
            Picker("Style", selection: $s.style) {
                ForEach(BioAtomStyle.allCases, id: \.self) { st in Text(st.label).tag(st) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("Draw the atoms as balls and sticks, space filling or sticks, as in the molecule builder")
            if colours.count > 1 {
                HStack(spacing: 8) {
                    Text("Colour by").foregroundStyle(.secondary)
                    Picker("Colour by", selection: Binding(get: { colours.contains(s.colourBy) ? s.colourBy : .element }, set: { s.colourBy = $0 })) {
                        ForEach(colours, id: \.self) { c in Text(c.label).tag(c) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                }
                .help("Colour atoms by element (CPK), by part of the molecule, or by class")
            }
            Toggle("Show H atoms", isOn: Binding(get: { noHydrogens == nil && s.hydrogens(page, atoms: atoms) },
                                                 set: { s.setHydrogens(page, $0) }))
                .disabled(noHydrogens != nil)
                .help(noHydrogens ?? "Show the hydrogen atoms (on for single molecules, off for chains over 200 atoms)")
            Toggle("pH 7 form (as in cells)", isOn: $s.ph7)
                .disabled(noPH != nil)
                .help(noPH ?? "On: groups ionised as they are at cell pH (–NH₃⁺, –COO⁻). Off: the neutral form textbooks often draw.")
            Toggle("Labels", isOn: $s.labels)
                .help("Name the parts with labels and leader lines")
            if let handOff {
                Button {
                    guard let h = handOff() else { return }
                    let text = BiomolHandOff.text(h.model, title: h.title, hydrogens: noHydrogens == nil && s.hydrogens(page, atoms: atoms))
                    BioTools.open(.ptable, info: ["molecule": text])
                } label: {
                    Label("Open in Molecule Builder", systemImage: "atom").frame(maxWidth: .infinity)
                }
                .glassButton()
                .controlSize(.large)
                .help("See its formula, molar mass, polarity and bond angles in the Periodic Table's molecule builder (its shape kept as it is)")
            }
        }
        .font(.sCallout)
    }
}

/// A grid of choices (amino acids' pages use their own): each a rounded tile, the chosen one in the accent.
struct BiomolChoices<ID: Hashable>: View {
    let items: [(id: ID, title: String, colour: Color?)]
    let selected: ID?
    var columns = 3
    let pick: (ID) -> Void

    var body: some View {
        LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 6), count: columns), spacing: 6) {
            ForEach(items.indices, id: \.self) { i in
                let it = items[i]
                let on = it.id == selected
                Button {
                    withAnimation(Motion.snappy) { pick(it.id) }
                } label: {
                    HStack(spacing: 6) {
                        if let c = it.colour {
                            Circle().fill(c).frame(width: 9, height: 9).overlay(Circle().strokeBorder(Color.primary.opacity(0.2), lineWidth: 0.5))
                        }
                        Text(it.title).font(.sCallout.weight(.semibold)).lineLimit(1).minimumScaleFactor(0.7)
                    }
                    .padding(.horizontal, 6)
                    .frame(maxWidth: .infinity, minHeight: 34)
                    .background(RoundedRectangle(cornerRadius: 9, style: .continuous).fill(on ? Theme.accent.opacity(0.18) : Color.primary.opacity(0.07)))
                    .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(on ? Theme.accent.opacity(0.75) : Color.clear, lineWidth: 1.2))
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help(it.title)
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
    }
}

/// A segmented choice of a page's mode (Build or Structure levels; Nucleotides or Build a strand …).
struct BiomolModePicker<ID: Hashable>: View {
    let items: [(id: ID, title: String)]
    @Binding var selection: ID

    var body: some View {
        Picker("Show", selection: $selection) {
            ForEach(items.indices, id: \.self) { i in Text(items[i].title).tag(items[i].id) }
        }
        .pickerStyle(.segmented)
        .labelsHidden()
    }
}

/// The legend for what the atoms are coloured by (never colour alone, §3.8).
struct BiomolColourLegend: View {
    let items: [(color: Color, text: String)]

    var body: some View {
        if !items.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                BioHeading("Colours")
                BioLegend(items: items)
            }
            .padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card(radius: 14)
        }
    }

    /// CPK colours of the elements in a model.
    static func elements(_ symbols: [String]) -> [(color: Color, text: String)] {
        let order = ["C", "H", "N", "O", "P", "S"]
        let present = order.filter { symbols.contains($0) }
        return present.map { (Color(nsColor: MBChem.color($0)), "\($0) \(BiomolText.element($0).lowercased())") }
    }

    static func classes(_ list: [BioAAClass] = BioAAClass.allCases) -> [(color: Color, text: String)] {
        list.map { ($0.color, $0.label) }
    }

    static func parts(_ parts: [BiomolPart]) -> [(color: Color, text: String)] { parts.map { ($0.colour.color, $0.name) } }
}

/// The picked atom: its residue or part and name, its element.
struct BiomolAtomCard: View {
    let title: String
    let element: String
    var detail = ""

    var body: some View {
        HStack(spacing: 12) {
            Text(element)
                .font(.system(size: 15, weight: .bold))
                .foregroundStyle(element == "H" || element == "S" ? Color.black : Color.white)
                .frame(width: 38, height: 38)
                .background(Circle().fill(Color(nsColor: MBChem.color(element))))
                .overlay(Circle().strokeBorder(Color.primary.opacity(0.15), lineWidth: 0.5))
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.sHeadline)
                Text(detail.isEmpty ? BiomolText.element(element) : "\(BiomolText.element(element)) · \(detail)")
                    .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 14)
        .accessibilityElement(children: .combine)
    }
}

/// A short note under a column's controls.
struct BiomolNote: View {
    let text: String
    var warning = false

    var body: some View {
        Group {
            if warning {
                Label(text, systemImage: "exclamationmark.circle").foregroundStyle(.orange)
            } else {
                Text(text).foregroundStyle(.secondary)
            }
        }
        .font(.sCaption)
        .fixedSize(horizontal: false, vertical: true)
    }
}

/// A facts list in a card (the molecule builder's): name left, value right.
struct BiomolFacts: View {
    let rows: [(label: String, value: String)]

    var body: some View {
        VStack(spacing: 0) {
            ForEach(rows.indices, id: \.self) { i in
                BioValueRow(label: rows[i].label, value: rows[i].value, last: i == rows.count - 1)
            }
        }
        .card(radius: 14)
    }
}
