import AppKit
import SwiftUI

// (1.3.51) The Lewis structure maker, in the full Periodic Table: a central atom, the atoms round it and the charge give
// the structure — every atom single-bonded to the centre, the outer atoms' octets filled with lone pairs, the rest on the
// centre, then outer lone pairs turned into double or triple bonds until the centre has its octet (boron and beryllium
// left short, as taught), and for a centre from period 3 down, bonds added while that lowers the formal charges. From it:
// the electron groups, the VSEPR shape and bond angle, the polarity, each atom's formal charge and octet — drawn, and
// opened in the 3D builder on request.

struct LewisAtom: Identifiable, Hashable {
    let id: Int
    let symbol: String
    var bond: Int
    var lonePairs: Int
    var formal: Int = 0
}

struct LewisStructure {
    let central: String
    var centralLonePairs: Int
    var radical: Bool
    var terminals: [LewisAtom]
    let charge: Int
    let valenceTotal: Int
    var centralFormal: Int
    var problem: String?

    var bondingPairs: Int { terminals.map(\.bond).reduce(0, +) }
    var lonePairs: Int { centralLonePairs + terminals.map(\.lonePairs).reduce(0, +) }
    var groups: Int { terminals.count + centralLonePairs + (radical ? 1 : 0) }
    var centralElectrons: Int { 2 * bondingPairs + 2 * centralLonePairs + (radical ? 1 : 0) }

    /// "OH₂", "NO₃⁻".
    var formula: String {
        var counts: [(String, Int)] = []
        for t in terminals {
            if let i = counts.firstIndex(where: { $0.0 == t.symbol }) { counts[i].1 += 1 } else { counts.append((t.symbol, 1)) }
        }
        let sub: [Character: Character] = ["0": "₀", "1": "₁", "2": "₂", "3": "₃", "4": "₄", "5": "₅", "6": "₆", "7": "₇", "8": "₈", "9": "₉"]
        let body = central + counts.map { $0.0 + ($0.1 > 1 ? String(String($0.1).map { sub[$0] ?? $0 }) : "") }.joined()
        guard charge != 0 else { return body }
        let mag = abs(charge) == 1 ? "" : PTConfig.superscript(abs(charge))
        return body + mag + (charge > 0 ? "⁺" : "⁻")
    }

    /// The VSEPR shape and bond angle, by bonded atoms and lone pairs on the centre (an odd electron counted as a group).
    var shape: (name: String, angle: String) {
        let b = terminals.count, l = centralLonePairs + (radical ? 1 : 0)
        switch (b, l) {
        case (0, _): return ("A lone atom", "—")
        case (1, _): return ("Linear", "—")
        case (2, 0): return ("Linear", "180°")
        case (2, 1): return ("Bent", "about 117°")
        case (2, 2): return ("Bent", "about 104.5°")
        case (2, 3): return ("Linear", "180°")
        case (3, 0): return ("Trigonal planar", "120°")
        case (3, 1): return ("Trigonal pyramidal", "about 107°")
        case (3, 2): return ("T-shaped", "about 90°")
        case (4, 0): return ("Tetrahedral", "109.5°")
        case (4, 1): return ("Seesaw", "about 90° and 120°")
        case (4, 2): return ("Square planar", "90°")
        case (5, 0): return ("Trigonal bipyramidal", "90° and 120°")
        case (5, 1): return ("Square pyramidal", "about 90°")
        case (6, 0): return ("Octahedral", "90°")
        default: return ("\(b + l) electron groups", "—")
        }
    }

    /// Polar or not: a symmetric shape with every outer atom the same cancels out; otherwise any difference in
    /// electronegativity (or a lone pair's push) leaves it polar.
    var polar: Bool {
        let b = terminals.count, l = centralLonePairs + (radical ? 1 : 0)
        let same = Set(terminals.map(\.symbol)).count <= 1
        let symmetric = [(2, 0), (3, 0), (4, 0), (5, 0), (6, 0), (4, 2), (2, 3)].contains { $0.0 == b && $0.1 == l }
        if symmetric && same { return false }
        if b == 1, let t = terminals.first { return Lewis.en(t.symbol) != Lewis.en(central) }
        return true
    }

    /// Electrons round an atom (its bonds' and lone pairs'): a full octet, or two for hydrogen.
    func around(_ t: LewisAtom) -> Int { 2 * t.bond + 2 * t.lonePairs }
}

enum Lewis {
    static let valence: [String: Int] = ["H": 1, "Be": 2, "B": 3, "C": 4, "Si": 4, "N": 5, "P": 5, "As": 5, "O": 6, "S": 6, "Se": 6,
                                         "F": 7, "Cl": 7, "Br": 7, "I": 7, "Xe": 8, "Kr": 8]

    static func en(_ s: String) -> Double { PeriodicTable.elements.first { $0.symbol == s }?.en ?? 2.5 }

    static func period(_ s: String) -> Int { PeriodicTable.elements.first { $0.symbol == s }?.period ?? 2 }

    static func solve(central c: String, around: [String], charge q: Int) -> LewisStructure {
        let vc = valence[c] ?? 4
        let total = vc + around.reduce(0) { $0 + (valence[$1] ?? 1) } - q
        var terms = around.enumerated().map { LewisAtom(id: $0.offset, symbol: $0.element, bond: 1, lonePairs: 0) }
        var left = total - 2 * terms.count
        var problem: String?
        if left < 0 {
            problem = "Not enough electrons to bond every atom to the centre."
            left = 0
        }
        // (the outer atoms' octets, with lone pairs)
        for i in terms.indices where terms[i].symbol != "H" {
            let give = min(3, left / 2)
            terms[i].lonePairs = give
            left -= 2 * give
        }
        // (the rest on the centre; an odd one left unpaired)
        var centralLP = left / 2
        let radical = left % 2 == 1
        func centreElectrons() -> Int { 2 * terms.map(\.bond).reduce(0, +) + 2 * centralLP + (radical ? 1 : 0) }
        let halogenOrH: Set<String> = ["H", "F", "Cl", "Br", "I"]
        func canShare(_ t: LewisAtom) -> Bool { !halogenOrH.contains(t.symbol) && t.lonePairs > 0 && t.bond < 3 }
        // (the centre's octet, by sharing outer lone pairs — boron and beryllium left short)
        if !["H", "Be", "B"].contains(c) {
            while centreElectrons() < 8 {
                guard let i = terms.indices.filter({ canShare(terms[$0]) }).max(by: { terms[$0].lonePairs < terms[$1].lonePairs }) else { break }
                terms[i].lonePairs -= 1
                terms[i].bond += 1
            }
        }
        func formal(_ t: LewisAtom) -> Int { (valence[t.symbol] ?? 1) - 2 * t.lonePairs - t.bond }
        func centreFormal() -> Int { vc - 2 * centralLP - (radical ? 1 : 0) - terms.map(\.bond).reduce(0, +) }
        // (from period 3 down, the centre may hold more than eight: bonds added while that lowers the formal charges)
        if period(c) >= 3 {
            while centreFormal() > 0, let i = terms.indices.first(where: { formal(terms[$0]) < 0 && canShare(terms[$0]) }) {
                terms[i].lonePairs -= 1
                terms[i].bond += 1
            }
        }
        for i in terms.indices { terms[i].formal = formal(terms[i]) }
        // (a centre that cannot reach its octet even so)
        if centralLP < 0 { centralLP = 0 }
        var s = LewisStructure(central: c, centralLonePairs: centralLP, radical: radical, terminals: terms, charge: q, valenceTotal: total,
                               centralFormal: centreFormal(), problem: problem)
        if s.problem == nil, period(c) == 2, s.centralElectrons > 8 {
            s.problem = "\(c) cannot hold more than eight electrons: too many atoms round it."
        }
        return s
    }

    struct Preset: Identifiable {
        let name: String
        let central: String
        let around: [String]
        let charge: Int
        var id: String { name }
    }

    static let presets: [Preset] = [
        Preset(name: "H₂O", central: "O", around: ["H", "H"], charge: 0),
        Preset(name: "CO₂", central: "C", around: ["O", "O"], charge: 0),
        Preset(name: "NH₃", central: "N", around: ["H", "H", "H"], charge: 0),
        Preset(name: "CH₄", central: "C", around: ["H", "H", "H", "H"], charge: 0),
        Preset(name: "HCN", central: "C", around: ["H", "N"], charge: 0),
        Preset(name: "CH₂O", central: "C", around: ["H", "H", "O"], charge: 0),
        Preset(name: "O₃", central: "O", around: ["O", "O"], charge: 0),
        Preset(name: "SO₂", central: "S", around: ["O", "O"], charge: 0),
        Preset(name: "NO₃⁻", central: "N", around: ["O", "O", "O"], charge: -1),
        Preset(name: "CO₃²⁻", central: "C", around: ["O", "O", "O"], charge: -2),
        Preset(name: "SO₄²⁻", central: "S", around: ["O", "O", "O", "O"], charge: -2),
        Preset(name: "NH₄⁺", central: "N", around: ["H", "H", "H", "H"], charge: 1),
        Preset(name: "BF₃", central: "B", around: ["F", "F", "F"], charge: 0),
        Preset(name: "PCl₅", central: "P", around: ["Cl", "Cl", "Cl", "Cl", "Cl"], charge: 0),
        Preset(name: "SF₆", central: "S", around: ["F", "F", "F", "F", "F", "F"], charge: 0),
        Preset(name: "ClF₃", central: "Cl", around: ["F", "F", "F"], charge: 0),
        Preset(name: "XeF₂", central: "Xe", around: ["F", "F"], charge: 0),
        Preset(name: "XeF₄", central: "Xe", around: ["F", "F", "F", "F"], charge: 0),
    ]

    static let centrals = ["C", "N", "O", "S", "P", "B", "Be", "Si", "Cl", "Xe", "I", "Se"]
    static let outers = ["H", "O", "N", "C", "F", "Cl", "Br", "I", "S"]

    /// An atom's CPK colour — the primary colour for the pale ones (hydrogen, sulfur's yellow, boron's pink…), which do
    /// not read on a light page — and carbon's gray as the primary colour too.
    static func color(_ s: String) -> Color {
        if s == "H" || s == "C" { return .primary }
        guard let hex = PeriodicTable.elements.first(where: { $0.symbol == s })?.extra?.cpk, let v = UInt32(hex, radix: 16) else { return .primary }
        let r = Double((v >> 16) & 0xff) / 255, g = Double((v >> 8) & 0xff) / 255, b = Double(v & 0xff) / 255
        if 0.2126 * r + 0.7152 * g + 0.0722 * b > 0.6 { return .primary }
        return Color(red: r, green: g, blue: b)
    }
}

/// The maker: presets, the centre, the atoms round it and the charge down the left; the structure drawn in the middle;
/// its counts, shape and polarity, and each atom's octet, on the right.
struct LewisMaker: View {
    var openIn3D: (LewisStructure) -> Void
    @State private var central = "O"
    @State private var counts: [String: Int] = ["H": 2]
    @State private var charge = 0

    private var around: [String] { Lewis.outers.flatMap { s in Array(repeating: s, count: counts[s] ?? 0) } }
    private var structure: LewisStructure { Lewis.solve(central: central, around: around, charge: charge) }

    var body: some View {
        PTWorkLayout {
            controls
        } centre: {
            drawing
        } right: {
            facts
        }
    }

    // MARK: Controls

    private var controls: some View {
        VStack(alignment: .leading, spacing: 16) {
            heading("Presets")
            FlowChips(items: Lewis.presets.map(\.name), selected: presetMatch) { name in
                guard let p = Lewis.presets.first(where: { $0.name == name }) else { return }
                withAnimation(Motion.snappy) {
                    central = p.central
                    var c: [String: Int] = [:]
                    for s in p.around { c[s, default: 0] += 1 }
                    counts = c
                    charge = p.charge
                }
            }
            heading("Central atom")
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 6), count: 6), spacing: 6) {
                ForEach(Lewis.centrals, id: \.self) { s in
                    Button { withAnimation(Motion.snappy) { central = s } } label: {
                        Text(s).font(.sCallout.weight(.bold)).foregroundStyle(central == s ? Color.white : Lewis.color(s))
                            .frame(maxWidth: .infinity, minHeight: 32)
                            .background(central == s ? Theme.accent : Color.primary.opacity(0.07), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                    }
                    .buttonStyle(.plain)
                }
            }
            heading("Surrounding atoms")
            VStack(spacing: 0) {
                ForEach(Lewis.outers, id: \.self) { s in
                    HStack {
                        Text(s).font(.sCallout.weight(.bold)).foregroundStyle(Lewis.color(s))
                        Spacer()
                        stepper(value: counts[s] ?? 0, min: 0, max: 6) { v in counts[s] = v }
                    }
                    .padding(.vertical, 5)
                    .padding(.horizontal, 12)
                    if s != Lewis.outers.last { Divider() }
                }
            }
            .card(radius: 14)
            HStack {
                Text("Charge").font(.sCallout.weight(.semibold))
                Spacer()
                stepper(value: charge, min: -3, max: 3) { v in charge = v }
            }
            .padding(.vertical, 8)
            .padding(.horizontal, 12)
            .card(radius: 14)
        }
    }

    private var presetMatch: String? {
        Lewis.presets.first { p in
            p.central == central && p.charge == charge && p.around.sorted() == around.sorted()
        }?.name
    }

    private func stepper(value: Int, min lo: Int, max hi: Int, set: @escaping (Int) -> Void) -> some View {
        HStack(spacing: 8) {
            Button { withAnimation(Motion.snappy) { set(Swift.max(lo, value - 1)) } } label: { Image(systemName: "minus").frame(width: 22, height: 22) }
                .buttonStyle(.plain)
                .background(Color.primary.opacity(0.08), in: RoundedRectangle(cornerRadius: 6))
                .disabled(value <= lo)
            Text(value > 0 && lo < 0 ? "+\(value)" : "\(value)").font(.sCallout.monospacedDigit().weight(.semibold)).frame(width: 26)
            Button { withAnimation(Motion.snappy) { set(Swift.min(hi, value + 1)) } } label: { Image(systemName: "plus").frame(width: 22, height: 22) }
                .buttonStyle(.plain)
                .background(Color.primary.opacity(0.08), in: RoundedRectangle(cornerRadius: 6))
                .disabled(value >= hi)
        }
    }

    private func heading(_ t: String) -> some View {
        Text(t.uppercased()).font(.system(size: 11.5, weight: .semibold)).tracking(0.6).foregroundStyle(.secondary)
    }

    // MARK: The drawing

    private var drawing: some View {
        let s = structure
        return VStack(alignment: .leading, spacing: 0) {
            Text(s.formula).font(.system(size: 22, weight: .bold)).padding(16)
            LewisCanvas(structure: s)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .animation(Motion.gentle, value: s.formula)
            HStack(spacing: 16) {
                HStack(spacing: 5) { Circle().fill(Color.yellow).frame(width: 7, height: 7); Text("Lone pair electrons") }
                HStack(spacing: 5) { Rectangle().frame(width: 16, height: 2); Text("Bonding pair") }
                Text("Small numbers are formal charges")
            }
            .font(.sCaption)
            .foregroundStyle(.secondary)
            .frame(maxWidth: .infinity)
            .padding(14)
        }
        .card(radius: 20)
    }

    // MARK: Facts

    private var facts: some View {
        let s = structure
        return VStack(alignment: .leading, spacing: 14) {
            VStack(spacing: 0) {
                row("Valence electrons", "\(s.valenceTotal)")
                row("Bonding pairs", "\(s.bondingPairs)")
                row("Lone pairs", "\(s.lonePairs)", .yellow)
                row("Electron groups on \(s.central)", "\(s.groups)")
                row("Shape (VSEPR)", s.shape.name, Theme.accent)
                row("Bond angle", s.shape.angle)
                row("Polarity", s.terminals.isEmpty ? "—" : (s.polar ? "Polar" : "Nonpolar"), s.polar ? .orange : .green, last: true)
            }
            .card(radius: 14)
            if let p = s.problem {
                Label(p, systemImage: "exclamationmark.triangle.fill")
                    .font(.sCallout)
                    .foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
            VStack(alignment: .leading, spacing: 8) {
                heading("Octets")
                octet(s.central, s.centralElectrons, hydrogen: s.central == "H", formal: s.centralFormal)
                ForEach(Array(Set(s.terminals.map(\.symbol))).sorted(), id: \.self) { sym in
                    if let t = s.terminals.first(where: { $0.symbol == sym }) {
                        octet(sym, s.around(t), hydrogen: sym == "H", formal: t.formal)
                    }
                }
            }
            .padding(14)
            .card(radius: 14)
            Button {
                openIn3D(s)
            } label: {
                Label("Open in 3D builder", systemImage: "rotate.3d").frame(maxWidth: .infinity)
            }
            .glassButton(prominent: true)
            .controlSize(.large)
            .disabled(s.terminals.isEmpty)
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

    private func octet(_ sym: String, _ n: Int, hydrogen: Bool, formal: Int) -> some View {
        let want = hydrogen ? 2 : 8
        let word = n == want ? "Full" : (n > want ? "Expanded" : "Short")
        return HStack {
            Text(sym).font(.sCallout.weight(.bold)).foregroundStyle(Lewis.color(sym)).frame(width: 28, alignment: .leading)
            Text("\(n) electrons round it\(formal != 0 ? " · formal charge \(formal > 0 ? "+" : "")\(formal)" : "")").font(.sCallout)
            Spacer()
            Text(word).font(.sCallout.weight(.semibold)).foregroundStyle(n == want ? .green : (n > want ? Theme.accent : .orange))
        }
    }
}

/// Chips that wrap onto as many lines as they need.
struct FlowChips: View {
    let items: [String]
    let selected: String?
    let tap: (String) -> Void

    var body: some View {
        FlowLayout(spacing: 6) {
            ForEach(items, id: \.self) { item in
                Button { tap(item) } label: {
                    Text(item)
                        .font(.sCallout.weight(.semibold))
                        .padding(.horizontal, 11)
                        .padding(.vertical, 5)
                        .foregroundStyle(selected == item ? Color.white : Color.primary)
                        .background(selected == item ? Theme.accent : Color.primary.opacity(0.08), in: Capsule())
                }
                .buttonStyle(.plain)
            }
        }
    }
}

/// Views laid left to right, onto a new line when the next would not fit.
struct FlowLayout: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? 300
        var x: CGFloat = 0, y: CGFloat = 0, line: CGFloat = 0, widest: CGFloat = 0
        for v in subviews {
            let s = v.sizeThatFits(.unspecified)
            if x > 0 && x + s.width > width { x = 0; y += line + spacing; line = 0 }
            x += s.width + spacing
            line = max(line, s.height)
            widest = max(widest, x - spacing)
        }
        return CGSize(width: min(width, widest), height: y + line)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX, y = bounds.minY, line: CGFloat = 0
        for v in subviews {
            let s = v.sizeThatFits(.unspecified)
            if x > bounds.minX && x + s.width > bounds.maxX { x = bounds.minX; y += line + spacing; line = 0 }
            v.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(s))
            x += s.width + spacing
            line = max(line, s.height)
        }
    }
}

/// The structure drawn: the centre in the middle, the outer atoms round it (laid out for its shape), each bond one to
/// three lines, each lone pair two dots, an odd electron one, and formal charges small beside their atoms.
struct LewisCanvas: View {
    let structure: LewisStructure

    /// The directions of the bonds (degrees, 0 to the right, counter-clockwise), for the bonded atoms and lone pairs.
    private func bondAngles(_ b: Int, _ l: Int) -> [Double] {
        switch (b, l) {
        case (1, _): return [0]
        case (2, 0), (2, 3): return [180, 0]
        case (2, _): return [210, 330]
        case (3, 0): return [90, 210, 330]
        case (3, _): return [180, 0, 270]
        case (4, _): return [90, 180, 270, 0]
        case (5, _): return [90, 162, 234, 306, 18]
        case (6, _): return [0, 60, 120, 180, 240, 300]
        default: return (0..<b).map { Double($0) * 360 / Double(max(b, 1)) }
        }
    }

    /// Lone pairs' directions on the centre: as far from every bond as they can be.
    private func pairAngles(_ count: Int, from bonds: [Double]) -> [Double] {
        var taken = bonds
        var out: [Double] = []
        let candidates: [Double] = [90, 270, 45, 135, 225, 315, 180, 0, 120, 60, 240, 300]
        for _ in 0..<count {
            func gap(_ a: Double) -> Double {
                taken.map { t -> Double in let d = abs((a - t).truncatingRemainder(dividingBy: 360)); return Swift.min(d, 360 - d) }.min() ?? 180
            }
            guard let best = candidates.max(by: { gap($0) < gap($1) }) else { break }
            out.append(best)
            taken.append(best)
        }
        return out
    }

    var body: some View {
        Canvas { ctx, size in
            let s = structure
            let c = CGPoint(x: size.width / 2, y: size.height / 2)
            let r = min(size.width, size.height) * 0.3
            let l = s.centralLonePairs
            let angles = bondAngles(s.terminals.count, l + (s.radical ? 1 : 0))
            func point(_ a: Double, _ d: CGFloat, from o: CGPoint) -> CGPoint {
                CGPoint(x: o.x + d * CGFloat(cos(a * .pi / 180)), y: o.y - d * CGFloat(sin(a * .pi / 180)))
            }
            let atomR: CGFloat = 20
            func label(_ sym: String, at p: CGPoint, formal: Int) {
                ctx.draw(Text(sym).font(.system(size: 34, weight: .bold)).foregroundColor(Lewis.color(sym)), at: p)
                if formal != 0 {
                    let t = (formal > 0 ? "+" : "−") + (abs(formal) > 1 ? "\(abs(formal))" : "")
                    ctx.draw(Text(t).font(.system(size: 13, weight: .bold)).foregroundColor(.secondary), at: CGPoint(x: p.x + 22, y: p.y - 20))
                }
            }
            func pair(at a: Double, around p: CGPoint, distance: CGFloat = 30) {
                let m = point(a, distance, from: p)
                let perp = a + 90
                for k in [-1.0, 1.0] {
                    let dot = point(perp, CGFloat(k) * 5, from: m)
                    ctx.fill(Path(ellipseIn: CGRect(x: dot.x - 3.2, y: dot.y - 3.2, width: 6.4, height: 6.4)), with: .color(.yellow))
                }
            }
            // (bonds)
            for (i, t) in s.terminals.enumerated() where i < angles.count {
                let a = angles[i]
                let p = point(a, r, from: c)
                let from = point(a, atomR, from: c), to = point(a, r - atomR, from: c)
                let offsets: [CGFloat] = t.bond == 1 ? [0] : (t.bond == 2 ? [-4, 4] : [-7, 0, 7])
                for o in offsets {
                    var path = Path()
                    path.move(to: point(a + 90, o, from: from))
                    path.addLine(to: point(a + 90, o, from: to))
                    ctx.stroke(path, with: .color(.primary.opacity(0.75)), style: StrokeStyle(lineWidth: 2.5, lineCap: .round))
                }
                label(t.symbol, at: p, formal: t.formal)
                // (its lone pairs: away from the centre, then to either side)
                let around = [a, a + 90, a - 90]
                for k in 0..<min(t.lonePairs, 3) { pair(at: around[k], around: p) }
            }
            label(s.central, at: c, formal: s.centralFormal)
            let pairs = pairAngles(l + (s.radical ? 1 : 0), from: Array(angles.prefix(s.terminals.count)))
            for (k, a) in pairs.enumerated() {
                if k == pairs.count - 1 && s.radical {
                    let m = point(a, 30, from: c)
                    ctx.fill(Path(ellipseIn: CGRect(x: m.x - 3.2, y: m.y - 3.2, width: 6.4, height: 6.4)), with: .color(.yellow))
                } else {
                    pair(at: a, around: c)
                }
            }
            if s.charge != 0 {
                let t = "\(abs(s.charge) > 1 ? "\(abs(s.charge))" : "")\(s.charge > 0 ? "+" : "−")"
                ctx.draw(Text("ion, charge \(t)").font(.system(size: 13, weight: .semibold)).foregroundColor(.secondary), at: CGPoint(x: size.width - 70, y: 16))
            }
        }
    }
}
