import AppKit
import SceneKit
import SwiftUI

// (1.3.51) The 3D molecule builder, in the full Periodic Table: atoms added to the one selected with a single, double or
// triple bond, hydrogens filled in, presets, and a molecule from the Lewis maker — shaped as VSEPR has it: each atom's
// bonds and lone pairs (modelled as points of their own) pushing apart while the bonds keep their lengths, so water
// bends, ammonia forms a pyramid and methane a tetrahedron. Turned and zoomed by dragging and scrolling; an atom
// selected by a click. Shown as ball and stick, space filling or sticks, with its lone pairs and labels if asked, and
// turning by itself if asked; beside it, its formula, molar mass, counts, a valence check, its polarity, and the selected
// atom's bonds, shape and bond angles as the model has them.

struct MBAtom: Identifiable, Equatable {
    let id: Int
    var symbol: String
    var pos: SIMD3<Double>
    /// (1.3.54) Its lone electrons as the Lewis builder placed them (else worked out from its bonds).
    var lone: Int? = nil
}

struct MBBond: Identifiable, Equatable {
    let id: Int
    var a: Int
    var b: Int
    var order: Int
}

struct MBLonePair: Equatable {
    let atom: Int
    var pos: SIMD3<Double>
}

enum MBChem {
    /// The bond counts an atom may have (its usual first).
    static let allowed: [String: [Int]] = ["H": [1], "C": [4], "N": [3, 4], "O": [2], "F": [1], "Cl": [1, 3, 5, 7], "Br": [1, 3, 5],
                                           "I": [1, 3, 5, 7], "S": [2, 4, 6], "P": [3, 5], "B": [3], "Si": [4], "Se": [2, 4, 6], "Xe": [2, 4, 6, 8]]

    static func usual(_ s: String) -> Int { allowed[s]?.first ?? 4 }
    static func most(_ s: String) -> Int { allowed[s]?.max() ?? 4 }
    static func element(_ s: String) -> PTElement? { PeriodicTable.elements.first { $0.symbol == s } }
    static func valence(_ s: String) -> Int { LChem.valence(s) }
    static func covalent(_ s: String) -> Double { (element(s)?.extra?.covalent ?? 75) / 100 }
    static func vdw(_ s: String) -> Double { (element(s)?.extra?.vanDerWaals ?? 170) / 100 }
    static func color(_ s: String) -> NSColor { s == "H" ? NSColor(white: 0.95, alpha: 1) : NSColor(ptHex: element(s)?.extra?.cpk ?? "c8c8c8") }
    static func swatch(_ s: String) -> Color { Color(nsColor: color(s)) }
}

final class MoleculeModel: ObservableObject {
    enum Style: String, CaseIterable, Identifiable {
        case ballStick, spaceFill, sticks
        var id: String { rawValue }
        var label: String { self == .ballStick ? "Ball & stick" : (self == .spaceFill ? "Space filling" : "Sticks") }
    }

    @Published private(set) var atoms: [MBAtom] = []
    @Published private(set) var bonds: [MBBond] = []
    @Published private(set) var lonePairs: [MBLonePair] = []
    @Published var selected: Int?
    @Published var bondOrder = 1
    @Published var style: Style = .ballStick
    @Published var showLonePairs = true
    @Published var showLabels = true
    /// (1.3.58) On from the start: the model turns slowly until it is switched off.
    @Published var autoRotate = true
    @Published var message: String?
    @Published private(set) var title = ""
    /// Raised when the model is new (a preset, the Lewis maker's): the view frames it afresh.
    @Published private(set) var framing = 0
    private var nextID = 1

    static let presets = ["Water", "Ammonia", "Methane", "Carbon dioxide", "Formaldehyde", "Ethylene", "Acetylene", "Ethanol",
                          "Acetic acid", "Hydrogen peroxide", "Benzene", "Sulfur hexafluoride"]
    static let presetSections: [(String, [String])] = [
        ("Small molecules", ["Water", "Ammonia", "Methane", "Carbon dioxide", "Formaldehyde", "Hydrogen peroxide", "Sulfur hexafluoride"]),
        ("Carbon compounds", ["Ethylene", "Acetylene", "Ethanol", "Acetic acid", "Benzene"]),
    ]

    init(empty: Bool = false) { if !empty { loadPreset("Water") } }

    // MARK: Questions

    func atom(_ id: Int) -> MBAtom? { atoms.first { $0.id == id } }
    func bondSum(_ id: Int) -> Int { bonds.filter { $0.a == id || $0.b == id }.map(\.order).reduce(0, +) }
    func neighbours(_ id: Int) -> [Int] { bonds.compactMap { $0.a == id ? $0.b : ($0.b == id ? $0.a : nil) } }

    func lonePairCount(_ a: MBAtom) -> Int {
        if let l = a.lone { return l / 2 }
        if a.symbol == "H" || a.symbol == "B" { return 0 }
        return max(0, (MBChem.valence(a.symbol) - bondSum(a.id)) / 2)
    }

    /// Hill order: carbon, hydrogen, then the rest by symbol (all by symbol with no carbon).
    var formula: String {
        var counts: [String: Int] = [:]
        for a in atoms { counts[a.symbol, default: 0] += 1 }
        let keys = counts.keys.sorted()
        let order = counts["C"] != nil ? ["C", "H"].filter { counts[$0] != nil } + keys.filter { $0 != "C" && $0 != "H" } : keys
        let sub: [Character: Character] = ["0": "₀", "1": "₁", "2": "₂", "3": "₃", "4": "₄", "5": "₅", "6": "₆", "7": "₇", "8": "₈", "9": "₉"]
        return order.map { k in k + ((counts[k] ?? 0) > 1 ? String(String(counts[k] ?? 0).map { sub[$0] ?? $0 }) : "") }.joined()
    }

    var molarMass: Double { atoms.compactMap { MBChem.element($0.symbol)?.mass }.reduce(0, +) }

    /// Atoms whose bonds are not one of their allowed counts.
    var valenceIssues: [String] {
        atoms.compactMap { a in
            let n = bondSum(a.id)
            let ok = MBChem.allowed[a.symbol] ?? [MBChem.usual(a.symbol)]
            if ok.contains(n) { return nil }
            return "\(a.symbol) has \(n) bond\(n == 1 ? "" : "s"), wants \(ok.map(String.init).joined(separator: " or "))"
        }
    }

    /// The dipole: each bond toward its more electronegative atom, each lone pair outward; polar if they do not cancel.
    var dipole: Double {
        var v = SIMD3<Double>(0, 0, 0)
        for b in bonds {
            guard let x = atom(b.a), let y = atom(b.b) else { continue }
            let d = y.pos - x.pos
            let len = simd_length(d)
            guard len > 1e-6 else { continue }
            v += d / len * ((MBChem.element(y.symbol)?.en ?? 2.5) - (MBChem.element(x.symbol)?.en ?? 2.5))
        }
        for p in lonePairs {
            guard let a = atom(p.atom) else { continue }
            let d = p.pos - a.pos
            let len = simd_length(d)
            if len > 1e-6 { v += d / len * 0.6 }
        }
        return simd_length(v)
    }

    /// The angles between the selected atom's bonds, as the model has them ("H–O–H 104.6°").
    func angles(at id: Int) -> [String] {
        guard let c = atom(id) else { return [] }
        let ns = neighbours(id).compactMap { atom($0) }
        var out: [String] = []
        for i in 0..<ns.count {
            for j in (i + 1)..<ns.count {
                let u = simd_normalize(ns[i].pos - c.pos), v = simd_normalize(ns[j].pos - c.pos)
                let a = acos(min(max(simd_dot(u, v), -1), 1)) * 180 / .pi
                out.append("\(ns[i].symbol)–\(c.symbol)–\(ns[j].symbol) \(String(format: "%.1f", a))°")
            }
        }
        return Array(Set(out)).sorted()
    }

    /// The shape round an atom, by its bonded atoms and lone pairs.
    func shape(at id: Int) -> String {
        guard let a = atom(id) else { return "" }
        return LChem.shape(bonded: neighbours(id).count, lone: lonePairCount(a)).name
    }

    // MARK: Changes

    func select(_ id: Int?) {
        selected = selected == id ? nil : id
    }

    /// An atom bonded to the selected one (or the first; or alone, the molecule empty).
    func add(_ symbol: String) {
        message = nil
        guard !atoms.isEmpty else {
            let a = MBAtom(id: nextID, symbol: symbol, pos: .zero)
            nextID += 1
            atoms = [a]
            selected = a.id
            relax()
            return
        }
        guard let anchor = atom(selected ?? atoms[0].id) else { return }
        let order = bondOrder
        if bondSum(anchor.id) + order > MBChem.most(anchor.symbol) {
            message = "\(MBChem.element(anchor.symbol)?.name ?? anchor.symbol) has no room for another \(order == 1 ? "" : order == 2 ? "double " : "triple ")bond."
            return
        }
        if order > MBChem.most(symbol) {
            message = "\(MBChem.element(symbol)?.name ?? symbol) cannot make a \(order == 2 ? "double" : "triple") bond."
            return
        }
        place(symbol, on: anchor.id, order: order)
        relax()
    }

    @discardableResult
    private func place(_ symbol: String, on anchorID: Int, order: Int) -> Int {
        guard let anchor = atom(anchorID) else { return -1 }
        // (out the way the anchor's other bonds leave open)
        var away = SIMD3<Double>(0, 0, 0)
        for n in neighbours(anchorID) { if let x = atom(n) { away -= simd_normalize(x.pos - anchor.pos + SIMD3(1e-6, 0, 0)) } }
        let seed = Double(nextID)
        let jitter = SIMD3<Double>(sin(seed * 1.7), cos(seed * 2.3), sin(seed * 3.1)) * 0.35
        var dir = away + jitter
        if simd_length(dir) < 1e-3 { dir = SIMD3(1, 0.2, 0.1) }
        dir = simd_normalize(dir)
        let length = (MBChem.covalent(anchor.symbol) + MBChem.covalent(symbol)) * bondFactor(order)
        let a = MBAtom(id: nextID, symbol: symbol, pos: anchor.pos + dir * length)
        nextID += 1
        atoms.append(a)
        bonds.append(MBBond(id: nextID, a: anchorID, b: a.id, order: order))
        nextID += 1
        return a.id
    }

    func fillHydrogen() {
        message = nil
        for a in atoms where a.symbol != "H" {
            var room = MBChem.usual(a.symbol) - bondSum(a.id)
            while room > 0 {
                place("H", on: a.id, order: 1)
                room -= 1
            }
        }
        relax()
    }

    func deleteSelected() {
        guard let id = selected else { return }
        atoms.removeAll { $0.id == id }
        bonds.removeAll { $0.a == id || $0.b == id }
        selected = nil
        message = nil
        relax()
    }

    func clear() {
        atoms = []
        bonds = []
        lonePairs = []
        selected = nil
        message = nil
        title = ""
        framing += 1
    }

    // MARK: Presets and the Lewis maker

    func loadPreset(_ name: String) {
        clear()
        title = name
        func chain(_ spec: [(String, Int)]) -> [Int] {
            var ids: [Int] = []
            for (k, s) in spec.enumerated() {
                if k == 0 {
                    let a = MBAtom(id: nextID, symbol: s.0, pos: .zero)
                    nextID += 1
                    atoms.append(a)
                    ids.append(a.id)
                } else {
                    ids.append(place(s.0, on: ids[k - 1], order: s.1))
                }
            }
            return ids
        }
        switch name {
        case "Water": _ = chain([("O", 0)])
        case "Ammonia": _ = chain([("N", 0)])
        case "Methane": _ = chain([("C", 0)])
        case "Carbon dioxide":
            let c = chain([("C", 0)])
            place("O", on: c[0], order: 2)
            place("O", on: c[0], order: 2)
        case "Formaldehyde": _ = chain([("C", 0), ("O", 2)])
        case "Ethylene": _ = chain([("C", 0), ("C", 2)])
        case "Acetylene": _ = chain([("C", 0), ("C", 3)])
        case "Ethanol": _ = chain([("C", 0), ("C", 1), ("O", 1)])
        case "Acetic acid":
            let c = chain([("C", 0), ("C", 1), ("O", 1)])
            place("O", on: c[1], order: 2)
        case "Hydrogen peroxide": _ = chain([("O", 0), ("O", 1)])
        case "Benzene":
            var ring: [Int] = []
            for k in 0..<6 {
                let a = Double(k) * .pi / 3
                let atom = MBAtom(id: nextID, symbol: "C", pos: SIMD3(1.4 * cos(a), 1.4 * sin(a), 0))
                nextID += 1
                atoms.append(atom)
                ring.append(atom.id)
            }
            for k in 0..<6 {
                bonds.append(MBBond(id: nextID, a: ring[k], b: ring[(k + 1) % 6], order: k % 2 == 0 ? 2 : 1))
                nextID += 1
            }
            // (each hydrogen straight out from its carbon, in the ring's plane)
            for k in 0..<6 {
                let a = Double(k) * .pi / 3
                let h = MBAtom(id: nextID, symbol: "H", pos: SIMD3(2.48 * cos(a), 2.48 * sin(a), 0))
                nextID += 1
                atoms.append(h)
                bonds.append(MBBond(id: nextID, a: ring[k], b: h.id, order: 1))
                nextID += 1
            }
        case "Sulfur hexafluoride":
            let s = chain([("S", 0)])
            for _ in 0..<6 { place("F", on: s[0], order: 1) }
        default: break
        }
        if name != "Benzene" && name != "Sulfur hexafluoride" && name != "Carbon dioxide" { fillHydrogen() }
        selected = atoms.first?.id
        relax(iterations: 900)
        framing += 1
    }

    /// (1.3.54) The Lewis builder's molecule: its atoms where they were drawn (lifted into 3D), its bonds with their
    /// orders, each atom's lone electrons as placed — then shaped.
    func load(graph g: LGraph, structure st: LStructure) {
        clear()
        title = LChem.formula(g.atoms.map(\.symbol), charge: g.charge)
        var map: [Int: Int] = [:]
        for a in g.atoms {
            let z = sin(Double(a.id) * 1.7) * 0.4
            atoms.append(MBAtom(id: nextID, symbol: a.symbol, pos: SIMD3(Double(a.pos.x) / 55, -Double(a.pos.y) / 55, z), lone: st.lone[a.id] ?? 0))
            map[a.id] = nextID
            nextID += 1
        }
        for b in g.bonds {
            guard let x = map[b.a], let y = map[b.b] else { continue }
            bonds.append(MBBond(id: nextID, a: x, b: y, order: st.orders[b.id] ?? b.order))
            nextID += 1
        }
        selected = atoms.first?.id
        relax(iterations: 900)
        framing += 1
    }

    // MARK: The shape

    private func bondFactor(_ order: Int) -> Double { order >= 3 ? 0.78 : (order == 2 ? 0.87 : 1) }

    /// VSEPR by forces: bonds springs at their lengths; round each atom, its bonds and lone pairs pushing apart (lone
    /// pairs harder); atoms not bonded kept from crowding. The lone pairs are kept for drawing.
    func relax(iterations: Int = 500) {
        let n = atoms.count
        guard n > 0 else { lonePairs = []; return }
        var p = atoms.map(\.pos)
        let index = Dictionary(uniqueKeysWithValues: atoms.enumerated().map { ($0.element.id, $0.offset) })
        var lp: [MBLonePair] = []
        for a in atoms {
            let k = lonePairCount(a)
            for j in 0..<k {
                let t = Double(a.id * 7 + j * 3)
                let d = simd_normalize(SIMD3<Double>(sin(t), cos(t * 1.3), sin(t * 0.7) + 0.3))
                lp.append(MBLonePair(atom: a.id, pos: a.pos + d * 0.55))
            }
        }
        var q = lp.map(\.pos)
        let lpOf = Dictionary(grouping: lp.indices, by: { lp[$0].atom })
        let pairs: [(Int, Int, Double)] = bonds.compactMap { b in
            guard let i = index[b.a], let j = index[b.b] else { return nil }
            return (i, j, (MBChem.covalent(atoms[i].symbol) + MBChem.covalent(atoms[j].symbol)) * bondFactor(b.order))
        }
        var bonded = Set<Int>()
        var neighbours: [[Int]] = Array(repeating: [], count: n)
        for (i, j, _) in pairs {
            bonded.insert(i * 10000 + j)
            bonded.insert(j * 10000 + i)
            if !neighbours[i].contains(j) { neighbours[i].append(j) }
            if !neighbours[j].contains(i) { neighbours[j].append(i) }
        }
        for step in 0..<iterations {
            var f = Array(repeating: SIMD3<Double>(0, 0, 0), count: n)
            var g = Array(repeating: SIMD3<Double>(0, 0, 0), count: q.count)
            for (i, j, target) in pairs {
                let d = p[j] - p[i]
                let len = max(simd_length(d), 1e-6)
                let force = d / len * (len - target) * 0.8
                f[i] += force
                f[j] -= force
            }
            for k in q.indices {
                guard let a = index[lp[k].atom] else { continue }
                let d = q[k] - p[a]
                let len = max(simd_length(d), 1e-6)
                let force = d / len * (len - 0.55) * 0.9
                g[k] -= force
                f[a] += force
            }
            // (round each atom: its domains — bonded atoms and lone pairs — as directions, pushed apart)
            for c in 0..<n {
                var dirs: [(SIMD3<Double>, Bool, Int)] = neighbours[c].map { (p[$0] - p[c], false, $0) }
                for k in lpOf[atoms[c].id] ?? [] { dirs.append((q[k] - p[c], true, k)) }
                guard dirs.count > 1 else { continue }
                let units = dirs.map { simd_normalize($0.0 + SIMD3(1e-9, 0, 0)) }
                for x in 0..<dirs.count {
                    for y in (x + 1)..<dirs.count {
                        let diff = units[x] - units[y]
                        let weight = (dirs[x].1 ? 1.25 : 1) * (dirs[y].1 ? 1.25 : 1)
                        let push = diff / (simd_length_squared(diff) + 0.05) * 0.22 * weight
                        if dirs[x].1 { g[dirs[x].2] += push * 0.55 } else { f[dirs[x].2] += push * simd_length(dirs[x].0) }
                        if dirs[y].1 { g[dirs[y].2] -= push * 0.55 } else { f[dirs[y].2] -= push * simd_length(dirs[y].0) }
                    }
                }
            }
            // (atoms not bonded kept apart)
            for i in 0..<n {
                for j in (i + 1)..<n where !bonded.contains(i * 10000 + j) {
                    let d = p[j] - p[i]
                    let len = max(simd_length(d), 0.3)
                    if len < 3.2 {
                        let force = d / len * (0.25 / (len * len))
                        f[i] -= force
                        f[j] += force
                    }
                }
            }
            let rate = step < iterations / 2 ? 0.08 : 0.04
            for i in 0..<n {
                var m = f[i] * rate
                if simd_length(m) > 0.2 { m = simd_normalize(m) * 0.2 }
                p[i] += m
            }
            for k in q.indices {
                var m = g[k] * rate
                if simd_length(m) > 0.2 { m = simd_normalize(m) * 0.2 }
                q[k] += m
            }
        }
        // (centred on the middle)
        let centre = p.reduce(SIMD3<Double>(0, 0, 0), +) / Double(n)
        for i in 0..<n { atoms[i].pos = p[i] - centre }
        for k in lp.indices { lp[k].pos = q[k] - centre }
        lonePairs = lp
    }
}

// MARK: - The builder

struct MoleculeBuilder: View {
    @ObservedObject var model: MoleculeModel

    private static let palette = ["H", "C", "N", "O", "F", "Cl", "Br", "I", "S", "P", "B", "Si"]

    var body: some View {
        PTWorkLayout {
            controls
        } centre: {
            VStack(alignment: .leading, spacing: 0) {
                HStack {
                    Text(model.formula.isEmpty ? "Empty" : model.formula).font(.system(size: 22, weight: .bold))
                    if !model.title.isEmpty && model.title != model.formula {
                        Text(model.title).font(.sCallout).foregroundStyle(.secondary)
                    }
                    Spacer()
                    // (1.3.59) turning by itself, on or off, at the viewer's top right
                    Button {
                        withAnimation(Motion.snappy) { model.autoRotate.toggle() }
                    } label: {
                        Label(model.autoRotate ? "Turning" : "Still", systemImage: model.autoRotate ? "rotate.3d" : "pause")
                            .font(.sCallout.weight(.medium))
                    }
                    .glassButton(prominent: model.autoRotate)
                    .help(model.autoRotate ? "Stop the model turning by itself" : "Let the model turn by itself")
                }
                .padding(16)
                MoleculeSceneView(model: model)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                Text("Drag to turn · scroll to zoom · click an atom to select it")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity)
                    .padding(12)
            }
            .card(radius: 20)
            .tourTarget("molViewer")
        } right: {
            facts
        }
    }

    private func heading(_ t: String) -> some View {
        Text(t.uppercased()).font(.system(size: 11.5, weight: .semibold)).tracking(0.6).foregroundStyle(.secondary)
    }

    private var controls: some View {
        VStack(alignment: .leading, spacing: 14) {
            Group {
            heading("Preset")
            PresetMenu(sections: MoleculeModel.presetSections,
                       selected: MoleculeModel.presets.contains(model.title) ? model.title : nil) { name in
                withAnimation(Motion.snappy) { model.loadPreset(name) }
            }
            heading("Add atom")
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 6), count: 4), spacing: 6) {
                ForEach(Self.palette, id: \.self) { s in
                    Button { model.add(s) } label: {
                        HStack(spacing: 6) {
                            Circle().fill(MBChem.swatch(s)).frame(width: 11, height: 11).overlay(Circle().strokeBorder(Color.primary.opacity(0.25), lineWidth: 0.5))
                            Text(s).font(.sCallout.weight(.bold))
                        }
                        .frame(maxWidth: .infinity, minHeight: 34)
                        .background(Color.primary.opacity(0.07), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .help("Add \(MBChem.element(s)?.name ?? s), bonded to the selected atom")
                }
            }
            Text("Each joins the selected atom with the bond below.").font(.sCaption).foregroundStyle(.secondary)
            }
            Group {
            heading("New bond")
            Picker("", selection: $model.bondOrder) {
                Text("Single").tag(1)
                Text("Double").tag(2)
                Text("Triple").tag(3)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            if let m = model.message {
                Label(m, systemImage: "exclamationmark.circle").font(.sCaption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
            }
            Button { model.fillHydrogen() } label: { Text("Fill with hydrogen").frame(maxWidth: .infinity) }
                .glassButton()
                .controlSize(.large)
            Button { model.deleteSelected() } label: { Text("Delete selected atom").frame(maxWidth: .infinity).foregroundStyle(.red) }
                .glassButton()
                .controlSize(.large)
                .disabled(model.selected == nil)
            Button("Clear") { model.clear() }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity)
            }
            Group {
            heading("View")
            Picker("", selection: $model.style) {
                ForEach(MoleculeModel.Style.allCases) { s in Text(s.label).tag(s) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            Toggle("Show lone pairs", isOn: $model.showLonePairs)
            Toggle("Show atom labels", isOn: $model.showLabels)
            }
        }
        .font(.sCallout)
    }

    private var facts: some View {
        let issues = model.valenceIssues
        let polar = model.dipole > 0.3
        return VStack(alignment: .leading, spacing: 14) {
            VStack(spacing: 0) {
                row("Molar mass", String(format: "%.2f g/mol", model.molarMass))
                row("Atoms", "\(model.atoms.count)")
                row("Bonds", "\(model.bonds.count)")
                row("Lone pairs", "\(model.lonePairs.count)", .yellow)
                row("Polarity", model.bonds.isEmpty ? "—" : (polar ? "Polar" : "Nonpolar"), polar ? .orange : .green)
                row("Valence check", issues.isEmpty ? "All good" : "\(issues.count) to fix", issues.isEmpty ? .green : .orange, last: true)
            }
            .card(radius: 14)
            if !issues.isEmpty {
                VStack(alignment: .leading, spacing: 4) {
                    ForEach(issues, id: \.self) { Text("• \($0)").font(.sCaption).foregroundStyle(.secondary) }
                }
            }
            if let id = model.selected, let a = model.atom(id) {
                let el = MBChem.element(a.symbol)
                VStack(alignment: .leading, spacing: 10) {
                    HStack(spacing: 12) {
                        Text(a.symbol)
                            .font(.system(size: 15, weight: .bold))
                            .foregroundStyle(a.symbol == "H" ? Color.black : Color.white)
                            .frame(width: 38, height: 38)
                            .background(Circle().fill(MBChem.swatch(a.symbol)))
                        VStack(alignment: .leading, spacing: 2) {
                            Text(el?.name ?? a.symbol).font(.sHeadline)
                            Text("\(model.bondSum(id)) bond\(model.bondSum(id) == 1 ? "" : "s") used of \(MBChem.usual(a.symbol)) · \(model.lonePairCount(a)) lone pair\(model.lonePairCount(a) == 1 ? "" : "s")")
                                .font(.sCaption)
                                .foregroundStyle(.secondary)
                        }
                    }
                    if model.neighbours(id).count >= 2 {
                        Text("Shape round it: \(model.shape(at: id))").font(.sCallout.weight(.semibold))
                        ForEach(model.angles(at: id), id: \.self) { Text($0).font(.sCaption.monospacedDigit()).foregroundStyle(.secondary) }
                    }
                }
                .padding(14)
                .frame(maxWidth: .infinity, alignment: .leading)
                .card(radius: 14)
            }
            Text("Lone pairs are modelled as points of their own that push on the bonds, so water bends and ammonia forms a pyramid. Bond angles are the model's, near the real ones.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func row(_ k: String, _ v: String, _ color: Color = .primary, last: Bool = false) -> some View {
        VStack(spacing: 0) {
            HStack {
                Text(k).font(.sCallout).foregroundStyle(.secondary)
                Spacer()
                Text(v).font(.sCallout.weight(.semibold)).foregroundStyle(color)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)
            if !last { Divider().padding(.leading, 14) }
        }
    }
}

// MARK: - The 3D view

/// (1.3.59) The molecule's view, telling when it is handled (dragged, scrolled, pinched) so turning by itself can wait.
final class MoleculeSCNView: SCNView {
    var handled: (() -> Void)?
    override func mouseDown(with event: NSEvent) { handled?(); super.mouseDown(with: event) }
    override func mouseDragged(with event: NSEvent) { handled?(); super.mouseDragged(with: event) }
    override func rightMouseDragged(with event: NSEvent) { handled?(); super.rightMouseDragged(with: event) }
    override func scrollWheel(with event: NSEvent) { handled?(); super.scrollWheel(with: event) }
    override func magnify(with event: NSEvent) { handled?(); super.magnify(with: event) }
    override func rotate(with event: NSEvent) { handled?(); super.rotate(with: event) }
}

struct MoleculeSceneView: NSViewRepresentable {
    @ObservedObject var model: MoleculeModel

    final class Coordinator: NSObject {
        var model: MoleculeModel
        var framing = -1
        weak var view: SCNView?
        private var resume: DispatchWorkItem?
        init(model: MoleculeModel) { self.model = model }

        /// The model handled: its turning paused, taken up again three seconds after the last touch.
        func handled() {
            PTTour.shared.did(.turned)
            guard let spin = view?.scene?.rootNode.childNode(withName: "spin", recursively: false) else { return }
            spin.isPaused = true
            resume?.cancel()
            let work = DispatchWorkItem { [weak self] in
                guard let self, let spin = self.view?.scene?.rootNode.childNode(withName: "spin", recursively: false) else { return }
                spin.isPaused = false
            }
            resume = work
            DispatchQueue.main.asyncAfter(deadline: .now() + 3, execute: work)
        }

        @objc func click(_ g: NSClickGestureRecognizer) {
            guard let v = view else { return }
            let hits = v.hitTest(g.location(in: v), options: [SCNHitTestOption.searchMode: SCNHitTestSearchMode.closest.rawValue])
            for h in hits {
                var n: SCNNode? = h.node
                while let x = n {
                    if let name = x.name, name.hasPrefix("atom-"), let id = Int(name.dropFirst(5)) {
                        model.select(id)
                        return
                    }
                    n = x.parent
                }
            }
        }
    }

    func makeCoordinator() -> Coordinator { Coordinator(model: model) }

    func makeNSView(context: Context) -> SCNView {
        let v = MoleculeSCNView(frame: .zero)
        v.handled = { [weak coordinator = context.coordinator] in coordinator?.handled() }
        v.allowsCameraControl = true
        v.autoenablesDefaultLighting = false
        v.antialiasingMode = .multisampling4X
        v.backgroundColor = .clear
        v.isPlaying = true
        v.loops = true
        let scene = SCNScene()
        scene.background.contents = NSColor.clear
        let cam = SCNNode()
        cam.name = "camera"
        cam.camera = SCNCamera()
        cam.camera?.fieldOfView = 40
        cam.camera?.zNear = 0.05
        cam.camera?.zFar = 400
        scene.rootNode.addChildNode(cam)
        let ambient = SCNNode()
        ambient.light = SCNLight()
        ambient.light?.type = .ambient
        ambient.light?.intensity = 380
        scene.rootNode.addChildNode(ambient)
        let key = SCNNode()
        key.light = SCNLight()
        key.light?.type = .directional
        key.light?.intensity = 1000
        key.eulerAngles = SCNVector3(-0.6, 0.5, 0)
        scene.rootNode.addChildNode(key)
        let fill = SCNNode()
        fill.light = SCNLight()
        fill.light?.type = .directional
        fill.light?.intensity = 350
        fill.eulerAngles = SCNVector3(0.5, -2.4, 0)
        scene.rootNode.addChildNode(fill)
        let spin = SCNNode()
        spin.name = "spin"
        scene.rootNode.addChildNode(spin)
        v.scene = scene
        v.pointOfView = cam
        let click = NSClickGestureRecognizer(target: context.coordinator, action: #selector(Coordinator.click(_:)))
        v.addGestureRecognizer(click)
        context.coordinator.view = v
        update(v, context.coordinator)
        return v
    }

    func updateNSView(_ v: SCNView, context: Context) {
        context.coordinator.model = model
        update(v, context.coordinator)
    }

    private func update(_ v: SCNView, _ c: Coordinator) {
        guard let root = v.scene?.rootNode, let spin = root.childNode(withName: "spin", recursively: false) else { return }
        spin.childNodes.forEach { $0.removeFromParentNode() }
        spin.addChildNode(MoleculeScene.node(model))
        if model.autoRotate {
            if spin.action(forKey: "turn") == nil { spin.runAction(.repeatForever(.rotateBy(x: 0, y: .pi * 2, z: 0, duration: 14)), forKey: "turn") }
        } else {
            spin.removeAction(forKey: "turn")
        }
        // (a new molecule: the camera back in front of it, far enough to see it all)
        if c.framing != model.framing, let cam = root.childNode(withName: "camera", recursively: false) {
            c.framing = model.framing
            let extent = model.atoms.map { simd_length($0.pos) }.max() ?? 1
            cam.position = SCNVector3(0, 0, CGFloat(max(3.6, extent * 2.3 + 2.4)))
            cam.eulerAngles = SCNVector3(0, 0, 0)
            spin.eulerAngles = SCNVector3(0, 0, 0)
            v.pointOfView = cam
        }
    }
}

enum MoleculeScene {
    static func vec(_ v: SIMD3<Double>) -> SCNVector3 { SCNVector3(CGFloat(v.x), CGFloat(v.y), CGFloat(v.z)) }
    static func f(_ v: SIMD3<Double>) -> SIMD3<Float> { SIMD3<Float>(Float(v.x), Float(v.y), Float(v.z)) }

    static func radius(_ s: String, _ style: MoleculeModel.Style) -> Double {
        switch style {
        case .ballStick: return 0.24 + 0.36 * MBChem.covalent(s)
        case .spaceFill: return MBChem.vdw(s) * 0.9
        case .sticks: return 0.13
        }
    }

    static func node(_ m: MoleculeModel) -> SCNNode {
        let root = SCNNode()
        let selectedColor = NSColor.controlAccentColor
        for a in m.atoms {
            let r = radius(a.symbol, m.style)
            let sphere = SCNSphere(radius: CGFloat(r))
            sphere.segmentCount = 36
            sphere.materials = [AtomScene.material(MBChem.color(a.symbol))]
            let n = SCNNode(geometry: sphere)
            n.name = "atom-\(a.id)"
            n.position = vec(a.pos)
            // (1.3.59) the selected atom ringed, the ring always facing the viewer — not a see-through shell, which
            // fought the spheres round it and broke into jagged patches
            if m.selected == a.id { n.addChildNode(ring(radius: r, color: selectedColor)) }
            if m.showLabels && m.style != .sticks {
                n.addChildNode(label(a.symbol, size: max(0.32, r * 0.75), radius: r, dark: a.symbol == "H" || a.symbol == "F" || a.symbol == "Cl"))
            }
            root.addChildNode(n)
        }
        if m.style != .spaceFill {
            let bondColor = NSColor(white: 0.62, alpha: 1)
            for b in m.bonds {
                guard let x = m.atom(b.a), let y = m.atom(b.b) else { continue }
                let d = y.pos - x.pos
                let len = simd_length(d)
                guard len > 1e-6 else { continue }
                let dir = d / len
                var ref = simd_cross(dir, SIMD3<Double>(0, 0, 1))
                if simd_length(ref) < 0.1 { ref = simd_cross(dir, SIMD3<Double>(0, 1, 0)) }
                let perp = simd_normalize(ref)
                let thick = m.style == .sticks ? 0.13 : (b.order == 1 ? 0.09 : 0.06)
                let offsets: [Double] = m.style == .sticks || b.order == 1 ? [0] : (b.order == 2 ? [-0.11, 0.11] : [-0.16, 0, 0.16])
                for o in offsets {
                    let cyl = SCNCylinder(radius: CGFloat(thick), height: CGFloat(len))
                    cyl.radialSegmentCount = 20
                    cyl.materials = [AtomScene.material(m.style == .sticks ? MBChem.color(x.symbol) : bondColor)]
                    let n = SCNNode(geometry: cyl)
                    let mid = (x.pos + y.pos) / 2 + perp * o
                    n.simdPosition = f(mid)
                    n.simdLook(at: f(y.pos + perp * o), up: f(perp), localFront: SIMD3<Float>(0, 1, 0))
                    root.addChildNode(n)
                }
            }
        }
        if m.showLonePairs && m.style != .spaceFill {
            let lobe = SCNSphere(radius: 0.2)
            lobe.materials = [AtomScene.material(NSColor.systemYellow, alpha: 0.42)]
            for p in m.lonePairs {
                guard let a = m.atom(p.atom) else { continue }
                let d = p.pos - a.pos
                let len = simd_length(d)
                guard len > 1e-6 else { continue }
                let dir = d / len
                let n = SCNNode(geometry: lobe)
                n.scale = SCNVector3(0.7, 1.25, 0.7)
                let at = a.pos + dir * (radius(a.symbol, m.style) + 0.22)
                n.simdPosition = f(at)
                n.simdLook(at: f(at + dir), up: abs(dir.y) > 0.9 ? SIMD3<Float>(1, 0, 0) : SIMD3<Float>(0, 1, 0), localFront: SIMD3<Float>(0, 1, 0))
                root.addChildNode(n)
            }
        }
        return root
    }

    /// A ring round an atom, facing the viewer: the selected atom's mark.
    static func ring(radius r: Double, color: NSColor) -> SCNNode {
        let torus = SCNTorus(ringRadius: CGFloat(r * 1.18 + 0.04), pipeRadius: CGFloat(max(0.018, r * 0.05)))
        torus.ringSegmentCount = 72
        let mat = SCNMaterial()
        mat.diffuse.contents = color
        mat.lightingModel = .constant
        torus.materials = [mat]
        let n = SCNNode(geometry: torus)
        // (a torus lies flat in x–z; stood up to face along z, which the billboard turns to the camera)
        n.eulerAngles = SCNVector3(CGFloat.pi / 2, 0, 0)
        let holder = SCNNode()
        holder.addChildNode(n)
        holder.constraints = [SCNBillboardConstraint()]
        return holder
    }

    /// An atom's symbol, always facing the camera, on the near side of its sphere — so it shows on its own atom and
    /// is hidden, like the atom, behind nearer ones (1.3.59: it was drawn over everything).
    static func label(_ s: String, size: Double, radius r: Double, dark: Bool) -> SCNNode {
        let text = SCNText(string: s, extrusionDepth: 0)
        text.font = NSFont.systemFont(ofSize: 1, weight: .bold)
        text.flatness = 0.02
        let mat = SCNMaterial()
        mat.diffuse.contents = dark ? NSColor(white: 0.12, alpha: 1) : NSColor.white
        mat.lightingModel = .constant
        mat.writesToDepthBuffer = false
        text.materials = [mat]
        let n = SCNNode(geometry: text)
        let (lo, hi) = n.boundingBox
        n.pivot = SCNMatrix4MakeTranslation((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, 0)
        n.scale = SCNVector3(CGFloat(size), CGFloat(size), CGFloat(size))
        n.position = SCNVector3(0, 0, CGFloat(r * 1.03))
        n.renderingOrder = 10
        let holder = SCNNode()
        holder.addChildNode(n)
        holder.constraints = [SCNBillboardConstraint()]
        return holder
    }
}
