import AppKit
import SwiftUI

// (1.3.80) Atoms & Orbitals: the 3D view that was on each element's page in the Periodic Table, as a tool of its own —
// any element's atom (its nucleus, and each shell a ring with its electrons going round it), the shapes of a subshell's
// orbitals, or its electron cloud (Atom3D.swift, ElectronCloud.swift), turned and zoomed by dragging and scrolling. The
// element is chosen by typing its symbol, name or number, or stepped through one by one. Laid out as the Lewis maker and
// the molecule builder are (PTWorkLayout): the element and the view on the left, the model in the middle, what it shows
// on the right — each in sight at every window size. An element's facts page opens it on that element (in the same
// popup), and "Open in Periodic Table" goes back to the facts.

struct AtomsOrbitalsTool: View {
    /// The element shown, by number.
    @State private var number = AtomsOrbitalsTool.firstElement
    /// Atom, Orbitals or Cloud: kept from element to element, and from one opening to the next.
    @State private var model = AtomsOrbitalsTool.firstModel
    /// The Orbitals view's subshell ("3d"); nil: the outer one being filled (3d for cobalt, not 4s).
    @State private var subshellID: String? = AtomsOrbitalsTool.launchSubshell(cloud: false)
    /// The Cloud view's chosen subshell ("3d"); nil: the whole atom. Its own, apart from the Orbitals' subshell, and back
    /// to the whole atom on every element.
    @State private var cloudFocus: String? = AtomsOrbitalsTool.launchSubshell(cloud: true)
    /// What is typed in the element field.
    @State private var query = ""
    /// The element handed over has been taken: the 3D view is made for it, and not first for another.
    @State private var ready = false
    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var element: PTElement? { PeriodicTable.byNumber(number) ?? PeriodicTable.elements.first }

    var body: some View {
        if let e = element {
            PTWorkLayout {
                controls(e)
            } centre: {
                viewer(e)
            } right: {
                facts(e)
            }
            .onAppear(perform: appeared)
        }
    }

    // MARK: Opening

    /// Whether the launch opened this tool (-SimplTool atoms, or the old -SimplTool ptable -SimplPTModel cloud): only then
    /// are its flags this tool's to read — -SimplPTElement 26, -SimplPTModel cloud, -SimplPTSubshell 3d.
    private static var launchedHere: Bool { ChemTools.launched == .atoms }

    /// The element it opens on when none is handed to it: the one it showed last; else the last opened in the Periodic
    /// Table; else carbon.
    static var remembered: PTElement? {
        let n = ChemPrefs.int(.atoms, "element") ?? ChemPrefs.int(.ptable, "element") ?? 6
        return PeriodicTable.byNumber(n) ?? PeriodicTable.elements.first
    }

    private static var firstElement: Int {
        let n = UserDefaults.standard.integer(forKey: "SimplPTElement")
        if launchedHere, PeriodicTable.byNumber(n) != nil { return n }
        return remembered?.number ?? 1
    }

    private static var firstModel: PTModel {
        if launchedHere, let m = PTModel(rawValue: UserDefaults.standard.string(forKey: "SimplPTModel") ?? "") { return m }
        return ChemPrefs.string(.atoms, "view").flatMap { PTModel(rawValue: $0) } ?? .atom
    }

    /// The launch's -SimplPTSubshell: in the Cloud view its chosen subshell (all: the whole atom), in another the
    /// Orbitals'.
    private static func launchSubshell(cloud: Bool) -> String? {
        guard launchedHere, let s = UserDefaults.standard.string(forKey: "SimplPTSubshell") else { return nil }
        if cloud { return firstModel == .cloud && s != "all" ? s : nil }
        return firstModel == .cloud ? nil : s
    }

    /// An element handed over (an element's facts page, the pin's) shown, and kept as one chosen here is.
    private func appeared() {
        if let raw = ToolsCenter.shared.takeInfo("element"), let n = Int(raw), PeriodicTable.byNumber(n) != nil, n != number {
            number = n
            subshellID = nil
            cloudFocus = nil
        }
        ChemPrefs.set(.atoms, "element", String(number))
        ready = true
        // (1.3.76) the screenshot suite's -SimplCloudCheck YES: the cloud model's test vectors on the console
        if UserDefaults.standard.bool(forKey: "SimplCloudCheck") { ElectronCloud.selfCheck() }
    }

    private func pick(_ n: Int) {
        guard n != number, PeriodicTable.byNumber(n) != nil else { return }
        withAnimation(reduceMotion ? nil : Motion.gentle) {
            number = n
            subshellID = nil
            cloudFocus = nil
        }
        ChemPrefs.set(.atoms, "element", String(n))
    }

    private func step(_ d: Int) { pick(ChemLaunch.step(number, by: d, count: PeriodicTable.elements.count)) }

    private func show(_ m: PTModel) {
        withAnimation(reduceMotion ? nil : Motion.gentle) { model = m }
        ChemPrefs.set(.atoms, "view", m.rawValue)
    }

    /// The subshell whose orbitals are shown: the one picked, else the outer one being filled.
    private func subshell(_ e: PTElement) -> PTSubshell? {
        let all = PTConfig.subshells(of: e)
        if let picked = all.first(where: { $0.id == subshellID }) { return picked }
        let valence = PTConfig.valence(of: e)
        let top = valence.map(\.l).max() ?? 0
        return valence.last { $0.l == top } ?? all.last
    }

    // MARK: The left: the element and the view

    private func controls(_ e: PTElement) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Element")
            elementCard(e)
            finder
            BioHeading("View")
            Picker("View", selection: Binding(get: { model }, set: { show($0) })) {
                Text("Atom").tag(PTModel.atom)
                Text("Orbitals").tag(PTModel.orbitals)
                Text("Cloud").tag(PTModel.cloud)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            switch model {
            case .atom:
                EmptyView()
            case .orbitals:
                BioHeading("Subshell")
                orbitalChips(e)
            case .cloud:
                BioHeading("Subshell")
                cloudChips(e)
            }
            Button {
                ToolsCenter.shared.popUp(.ptable, info: ["element": String(e.number)])
            } label: {
                Label("Open in Periodic Table", systemImage: ToolKind.ptable.symbol).frame(maxWidth: .infinity)
            }
            .glassButton()
            .controlSize(.large)
            .help("Every fact about \(e.name), on its page in the Periodic Table")
        }
    }

    /// The element: its tile, name, kind and mass, and the one before and after.
    private func elementCard(_ e: PTElement) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 12) {
                VStack(spacing: 0) {
                    Text("\(e.number)")
                        .font(.system(size: 11, weight: .semibold).monospacedDigit())
                        .frame(maxWidth: .infinity, alignment: .leading)
                    Text(e.symbol)
                        .font(.system(size: 26, weight: .bold))
                        .minimumScaleFactor(0.6)
                }
                .padding(8)
                .frame(width: 64, height: 64)
                .foregroundStyle(.white)
                .background(e.kind.color.opacity(0.85), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                VStack(alignment: .leading, spacing: 2) {
                    Text(e.name)
                        .font(.sTitle3)
                        .lineLimit(1)
                        .minimumScaleFactor(0.7)
                    Text("\(e.kind.label) · \(e.massText) u")
                        .font(.sCaption)
                        .foregroundStyle(e.kind.color)
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                }
                Spacer(minLength: 0)
            }
            .accessibilityElement(children: .combine)
            GlassGroup(spacing: 6) {
                HStack(spacing: 6) {
                    Button { step(-1) } label: { Image(systemName: "chevron.left").frame(width: 18) }
                        .glassButton()
                        .disabled(e.number <= 1)
                        .help("The element before (↑ in the field)")
                        .accessibilityLabel("The element before")
                    Text("\(e.number) / \(PeriodicTable.elements.count)")
                        .font(.sCallout.monospacedDigit())
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity)
                    Button { step(1) } label: { Image(systemName: "chevron.right").frame(width: 18) }
                        .glassButton()
                        .disabled(e.number >= PeriodicTable.elements.count)
                        .help("The element after (↓ in the field)")
                        .accessibilityLabel("The element after")
                }
            }
            .controlSize(.large)
        }
        .padding(14)
        .card(radius: 16)
    }

    /// The element typed: its symbol, name or number, and Return shows the first it finds (the table's search: an exact
    /// symbol, name or number first); ↑ and ↓ step through the elements.
    private var finder: some View {
        let q = query.trimmingCharacters(in: .whitespaces)
        let match = q.isEmpty ? nil : PeriodicTable.find(q).first
        return VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                TextField("Symbol, name or number", text: $query)
                    .textFieldStyle(.plain)
                    .font(.sBody)
                    .onSubmit {
                        guard let m = PeriodicTable.find(query.trimmingCharacters(in: .whitespaces)).first else { return }
                        pick(m.number)
                        query = ""
                    }
                    .onKeyPress(.upArrow) { step(-1); return .handled }
                    .onKeyPress(.downArrow) { step(1); return .handled }
                    .accessibilityLabel("Find an element")
            }
            .padding(.horizontal, 12)
            .frame(height: 34)
            .background(Color.primary.opacity(0.07), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            Group {
                if q.isEmpty {
                    Text("Type a symbol, name or number. ↑ and ↓ step through the elements.")
                        .foregroundStyle(.secondary)
                } else if let m = match {
                    Text("Return shows \(m.name) (\(m.symbol), \(m.number))")
                        .foregroundStyle(.secondary)
                } else {
                    Text("No element matches “\(q)”")
                        .foregroundStyle(.orange)
                }
            }
            .font(.sCaption)
            .fixedSize(horizontal: false, vertical: true)
        }
    }

    /// The Orbitals view's subshells, each with its electrons: the chosen one lit.
    private func orbitalChips(_ e: PTElement) -> some View {
        let current = subshell(e)?.id
        return LazyVGrid(columns: [GridItem(.adaptive(minimum: 54), spacing: 6)], alignment: .leading, spacing: 6) {
            ForEach(PTConfig.subshells(of: e)) { s in
                chip("\(s.name)\(PTConfig.superscript(s.count))", dot: nil, on: s.id == current, tip: "The \(s.name) orbitals",
                     spoken: s.count == 1 ? "\(s.name), 1 electron" : "\(s.name), \(s.count) electrons") { subshellID = s.id }
            }
        }
    }

    /// (1.3.76) The Cloud view's: All, then the subshells in order (n, then l; the core's too), each with its colour's
    /// dot — choosing one colours it alone, greys the rest and frames the view by it.
    private func cloudChips(_ e: PTElement) -> some View {
        let cloud = ElectronCloud.cloud(for: e)
        let dark = scheme == .dark
        // (a subshell the element has not — -SimplPTSubshell 5f on iron — is the whole atom, its chip the one lit)
        let chosen = cloud.subs.first { $0.id == cloudFocus }
        return LazyVGrid(columns: [GridItem(.adaptive(minimum: 62), spacing: 6)], alignment: .leading, spacing: 6) {
            chip("All", dot: nil, on: chosen == nil, tip: "Show every electron", spoken: "All electrons") { cloudFocus = nil }
            ForEach(cloud.subs, id: \.id) { s in
                chip("\(s.id)\(PTConfig.superscript(s.count))", dot: Color(nsColor: ElectronCloud.color(s.l, dark: dark)),
                     on: chosen?.id == s.id, tip: ElectronCloud.help(s),
                     spoken: s.count == 1 ? "\(s.id), 1 electron" : "\(s.id), \(s.count) electrons") { cloudFocus = s.id }
            }
        }
    }

    /// A subshell's chip (a dot of its colour before its name in the Cloud view); the one already chosen does nothing.
    private func chip(_ text: String, dot: Color?, on: Bool, tip: String, spoken: String, pick: @escaping () -> Void) -> some View {
        Button {
            if !on { withAnimation(reduceMotion ? nil : Motion.snappy) { pick() } }
        } label: {
            HStack(spacing: 5) {
                if let dot { Circle().fill(dot).frame(width: 7, height: 7) }
                Text(text).font(.sCaption.weight(on ? .bold : .regular).monospaced())
            }
            .padding(.horizontal, 9)
            .padding(.vertical, 4)
            .frame(maxWidth: .infinity)
            .background(on ? Theme.accent.opacity(0.25) : Color.primary.opacity(0.06), in: Capsule())
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .help(tip)
        .accessibilityLabel(spoken)
        .accessibilityAddTraits(on ? AccessibilityTraits.isSelected : [])
    }

    // MARK: The middle: the model

    private func viewer(_ e: PTElement) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Text(e.name).font(.system(size: 22, weight: .bold))
                Text(viewTitle(e)).font(.sCallout).foregroundStyle(.secondary).lineLimit(1)
                Spacer(minLength: 0)
            }
            .padding(16)
            ZStack {
                if ready {
                    Atom3DView(element: e, model: model, subshell: subshell(e), focus: cloudFocus, dark: scheme == .dark)
                        .accessibilityElement()
                        .accessibilityLabel(modelLabel(e))
                        .accessibilityHint("Drag to turn, scroll to zoom")
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            Text("Drag to turn · scroll to zoom")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity)
                .padding(12)
                .accessibilityHidden(true)
        }
        .card(radius: 20)
        // (laid out narrow, the card can be as short as 300 points: its model is framed to the room it has)
        .preference(key: PTDrawingMinKey.self, value: 300)
    }

    /// What the view shows, beside the element's name.
    private func viewTitle(_ e: PTElement) -> String {
        switch model {
        case .atom: return "Atom"
        case .orbitals: return "\(subshell(e)?.name ?? "1s") orbitals"
        case .cloud: return cloudFocus.map { "\($0) electrons as a cloud" } ?? "Electron cloud"
        }
    }

    /// (1.3.76) What the 3D view shows, for VoiceOver.
    private func modelLabel(_ e: PTElement) -> String {
        switch model {
        case .atom:
            return "\(e.name)'s atom in 3D"
        case .orbitals:
            return "The \(subshell(e)?.name ?? "1s") orbitals of \(e.name) in 3D"
        case .cloud:
            guard let s = ElectronCloud.cloud(for: e).subs.first(where: { $0.id == cloudFocus }) else {
                return "\(e.name)'s electron cloud in 3D"
            }
            let word = s.count == 1 ? "electron" : "electrons"
            return "The \(s.id) \(word) of \(e.name) as a cloud in 3D"
        }
    }

    // MARK: The right: what it shows

    private func facts(_ e: PTElement) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            switch model {
            case .atom: atomFacts(e)
            case .orbitals: orbitalFacts(e)
            case .cloud: cloudFacts(e)
            }
            configFacts(e)
        }
    }

    /// The Atom view's: what the atom is made of, and its colours.
    private func atomFacts(_ e: PTElement) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(spacing: 0) {
                BioValueRow(label: "Protons", value: "\(e.number)")
                BioValueRow(label: "Neutrons (common)", value: "\(e.neutrons)")
                BioValueRow(label: "Electrons", value: "\(e.number)")
                BioValueRow(label: "Shells", value: e.shells.map(String.init).joined(separator: " · "), last: true)
            }
            .card(radius: 14)
            BioLegend(items: [(color: Color(nsColor: AtomScene.protonColor), text: "Proton"),
                              (color: Color(nsColor: AtomScene.neutronColor), text: "Neutron"),
                              (color: Color(nsColor: AtomScene.electronColor), text: "Electron")])
            Text(e.number + e.neutrons > 72
                 ? "Each ring is a shell, its electrons going round it. 72 of the \(e.number + e.neutrons) nucleons are drawn, in their proportion."
                 : "Each ring is a shell, its electrons going round it. The nucleus is drawn far larger than it is.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    /// The Orbitals view's: the chosen subshell's orbitals with their electrons, and what the colours are.
    @ViewBuilder
    private func orbitalFacts(_ e: PTElement) -> some View {
        if let sub = subshell(e) {
            let names = AtomScene.orbitalNames(sub.l)
            VStack(alignment: .leading, spacing: 12) {
                BioHeading("The \(sub.name) orbitals")
                VStack(spacing: 0) {
                    ForEach(Array(names.enumerated()), id: \.offset) { i, name in
                        let filled = i < sub.filling.count ? sub.filling[i] : 0
                        BioValueRow(label: "\(sub.n)\(name)", value: filled == 0 ? "empty" : (filled == 1 ? "1 e⁻" : "2 e⁻"), last: i == names.count - 1)
                    }
                }
                .card(radius: 14)
                Text(sub.l == 3 ? "The f shapes are drawn simply. Blue and orange are the wave's two signs."
                     : "Each orbital holds two electrons; blue and orange are the wave's two signs. A faint orbital is empty.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    /// (1.3.76) The Cloud view's: each kind's electrons in its colour (or the chosen subshell's, and the grey rest's) and
    /// the nucleus; the chosen subshell's charge and size; and what the dots are.
    private func cloudFacts(_ e: PTElement) -> some View {
        let cloud = ElectronCloud.cloud(for: e)
        let dark = scheme == .dark
        let chosen = cloud.subs.first { $0.id == cloudFocus }
        var items: [(color: Color, text: String)] = []
        if let s = chosen {
            items.append((color: Color(nsColor: ElectronCloud.color(s.l, dark: dark)), text: "\(s.id) · \(s.count) e⁻"))
            if cloud.electrons > s.count {
                items.append((color: Color(nsColor: ElectronCloud.restColor(dark: dark)), text: "Rest · \(cloud.electrons - s.count) e⁻"))
            }
        } else {
            for t in 0..<4 {
                let n = cloud.subs.filter { $0.l == t }.map(\.count).reduce(0, +)
                if n > 0 { items.append((color: Color(nsColor: ElectronCloud.color(t, dark: dark)), text: "\(ElectronCloud.letters[t]) · \(n) e⁻")) }
            }
        }
        items.append((color: Color(nsColor: ElectronCloud.nucleusColor(dark: dark)), text: "Nucleus"))
        return VStack(alignment: .leading, spacing: 12) {
            BioLegend(items: items)
            if let s = chosen {
                Text(ElectronCloud.detail(s))
                    .font(.sCaption.monospacedDigit())
                    .fixedSize(horizontal: false, vertical: true)
            }
            Text(ElectronCloud.caption(cloud, chosen: chosen))
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    /// Its electrons written out, in every view.
    private func configFacts(_ e: PTElement) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            BioHeading("Electron configuration")
            Text(PTConfig.pretty(e))
                .font(.system(size: 18, weight: .semibold).monospaced())
                .textSelection(.enabled)
                .fixedSize(horizontal: false, vertical: true)
            Text(PTConfig.full(e))
                .font(.sCaption.monospaced())
                .foregroundStyle(.secondary)
                .textSelection(.enabled)
                .fixedSize(horizontal: false, vertical: true)
            if let v = e.valenceElectrons {
                Text("Valence electrons: \(v)").font(.sCallout)
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 14)
    }
}

/// (1.3.80) Atoms & Orbitals' pin, opened: an element typed — its symbol, name or number — and Return, or a press on it,
/// opens the tool on that element; until something is typed, the element it showed last.
struct AtomsOrbitalsCompact: View {
    var openFull: ([String: String]) -> Void
    @State private var query = ""

    private var typed: String { query.trimmingCharacters(in: .whitespaces) }
    private var found: PTElement? { typed.isEmpty ? AtomsOrbitalsTool.remembered : PeriodicTable.find(typed).first }

    /// (1.3.83) What Open Full Screen carries to the whole tool: the element typed, the one Return opens (nothing typed:
    /// nothing — the tool opens on its own, the one it showed last).
    private var carried: [String: String] {
        guard !typed.isEmpty, let e = found else { return [:] }
        return ["element": String(e.number)]
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            TextField("Symbol, name or number", text: $query)
                .textFieldStyle(.roundedBorder)
                .font(.sCallout)
                .onSubmit { if let e = found { show(e) } }
            if let e = found {
                Button { show(e) } label: {
                    HStack(spacing: 12) {
                        Text(e.symbol)
                            .font(.system(size: 17, weight: .bold))
                            .foregroundStyle(.white)
                            .frame(width: 40, height: 40)
                            .background(e.kind.color.opacity(0.85), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                        VStack(alignment: .leading, spacing: 2) {
                            Text(e.name).font(.sHeadline)
                            Text("Shells \(e.shells.map(String.init).joined(separator: " · "))")
                                .font(.sCaption.monospacedDigit())
                                .foregroundStyle(.secondary)
                        }
                        Spacer(minLength: 8)
                        Image(systemName: "chevron.right")
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundStyle(.tertiary)
                    }
                    .padding(10)
                    .background(Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help("\(e.name)'s atom, orbitals and electron cloud in Atoms & Orbitals (Return)")
            } else {
                Text("No element matches “\(typed)”")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
        }
        .quickCarry(carried)
    }

    /// The tool opened whole on the element.
    private func show(_ e: PTElement) {
        openFull(["element": String(e.number)])
    }
}
