import AppKit
import SwiftUI

// (1.3.51) What the full Periodic Table knows of each element beyond PeriodicData.swift (PeriodicExtraData.swift), the
// properties it can colour the table by and the trends they follow, and each element's electrons worked out: its
// configuration in full, its subshells, and how the electrons sit in their orbitals.

/// More of an element: ionization energies, electron affinity, radii, shells, its CPK colour, appearance, molar heat and
/// who named it.
struct PTExtra {
    let ionization: [Double]
    let affinity: Double?
    let radiusCalculated: Double?
    let radiusEmpirical: Double?
    let covalent: Double?
    let vanDerWaals: Double?
    let metallic: Double?
    let shells: [Int]
    let cpk: String
    let appearance: String
    let molarHeat: Double?
    let namedBy: String
}

enum PeriodicExtra {
    static let byNumber: [Int: PTExtra] = {
        guard let data = PeriodicExtraData.json.data(using: .utf8),
              let rows = (try? JSONSerialization.jsonObject(with: data)) as? [[Any]] else { return [:] }
        var out: [Int: PTExtra] = [:]
        for r in rows where r.count >= 13 {
            func num(_ i: Int) -> Double? { (r[i] as? NSNumber)?.doubleValue }
            func str(_ i: Int) -> String { (r[i] as? String) ?? "" }
            guard let n = (r[0] as? NSNumber)?.intValue else { continue }
            let ie = ((r[1] as? [Any]) ?? []).compactMap { ($0 as? NSNumber)?.doubleValue }
            let shells = ((r[8] as? [Any]) ?? []).compactMap { ($0 as? NSNumber)?.intValue }
            out[n] = PTExtra(ionization: ie, affinity: num(2), radiusCalculated: num(3), radiusEmpirical: num(4), covalent: num(5),
                             vanDerWaals: num(6), metallic: num(7), shells: shells, cpk: str(9), appearance: str(10),
                             molarHeat: num(11), namedBy: str(12))
        }
        return out
    }()
}

extension PTElement {
    var extra: PTExtra? { PeriodicExtra.byNumber[number] }
    /// First ionization energy, kJ/mol.
    var ionization: Double? { extra?.ionization.first }
    /// Atomic radius, pm (calculated, else empirical).
    var radius: Double? { extra?.radiusCalculated ?? extra?.radiusEmpirical }
    var affinity: Double? { extra?.affinity }
    /// Neutrons in its commonest (or longest-lived) isotope, by its mass.
    var neutrons: Int { max(0, Int(mass.rounded()) - number) }
    var shells: [Int] { extra?.shells ?? [] }
    var cpkColor: Color { Color(hex: extra?.cpk.isEmpty == false ? extra?.cpk : "#c8c8c8") }

    /// Valence electrons: by group for the main groups (helium 2); the outer s and the d under it for the d-block.
    var valenceElectrons: Int? {
        if number == 2 { return 2 }
        guard let g = group else { return nil }
        if g <= 2 { return g }
        if g >= 13 { return g - 10 }
        if block == "d" {
            let subs = PTConfig.subshells(of: self)
            guard let top = subs.map(\.n).max() else { return nil }
            return subs.filter { ($0.n == top && $0.l == 0) || ($0.n == top - 1 && $0.l == 2) }.map(\.count).reduce(0, +)
        }
        return nil
    }

    /// The chips' "State" word for 25 °C.
    var stateWord: String { phase.isEmpty ? "Unknown" : phase }
}

// MARK: - Colour by

/// What the table can be coloured by: its kind, or a property on a scale (with its trend across a period and up a
/// group), its orbital block, or its state at 25 °C.
enum PTColouring: String, CaseIterable, Identifiable {
    case category, electronegativity, radius, ionization, affinity, melting, boiling, density, block, state

    var id: String { rawValue }

    var label: String {
        switch self {
        case .category: return "Category"
        case .electronegativity: return "Electronegativity"
        case .radius: return "Atomic radius"
        case .ionization: return "Ionization energy"
        case .affinity: return "Electron affinity"
        case .melting: return "Melting point"
        case .boiling: return "Boiling point"
        case .density: return "Density"
        case .block: return "Orbital block"
        case .state: return "State at 25 °C"
        }
    }

    var unit: String {
        switch self {
        case .electronegativity: return "Pauling scale"
        case .radius: return "pm"
        case .ionization, .affinity: return "kJ/mol"
        case .melting, .boiling: return "K"
        case .density: return "g/cm³"
        default: return ""
        }
    }

    /// A property on a scale (the others colour by kind).
    var scaled: Bool { ![.category, .block, .state].contains(self) }

    func value(_ e: PTElement) -> Double? {
        switch self {
        case .electronegativity: return e.en
        case .radius: return e.radius
        case .ionization: return e.ionization
        case .affinity: return e.affinity
        case .melting: return e.melt
        case .boiling: return e.boil
        // (gases' densities are per litre: per cm³ here, so the scale is one)
        case .density: return e.density.map { e.phase == "Gas" ? $0 / 1000 : $0 }
        default: return nil
        }
    }

    /// The value as the cell and the card show it.
    func text(_ e: PTElement) -> String { PTColouring.texts[self]?[e.number] ?? makeText(e) }

    private func makeText(_ e: PTElement) -> String {
        switch self {
        case .category: return e.kind.label
        case .block: return "\(e.block)-block"
        case .state: return e.stateWord
        case .density: return e.density.map { PTColouring.short($0) + (e.phase == "Gas" ? " g/L" : "") } ?? "—"
        default: return value(e).map { PTColouring.short($0) } ?? "—"
        }
    }

    static func short(_ v: Double) -> String {
        if abs(v) >= 1000 { return String(Int(v.rounded())) }
        if abs(v) >= 100 { return String(format: "%.0f", v) }
        if abs(v) >= 10 { return String(format: "%.1f", v) }
        return String(format: "%.2f", v)
    }

    /// Spread over a log scale (density, ionization: a few run far above the rest).
    private var logScale: Bool { self == .density }

    /// (1.3.65) Each view's scale, worked out once — it was worked out again for every cell, every time the table drew.
    private static let ranges: [PTColouring: (Double, Double)] = {
        var out: [PTColouring: (Double, Double)] = [:]
        for c in PTColouring.allCases {
            let vs = PeriodicTable.elements.compactMap { c.value($0) }.map { c.logScale ? log10(max($0, 1e-4)) : $0 }
            out[c] = (vs.min() ?? 0, vs.max() ?? 1)
        }
        return out
    }()

    private var range: (Double, Double) { PTColouring.ranges[self] ?? (0, 1) }

    /// Where a value falls on the scale, 0…1.
    func fraction(_ e: PTElement) -> Double? {
        guard let v = value(e) else { return nil }
        let (lo, hi) = range
        let x = logScale ? log10(max(v, 1e-4)) : v
        return hi > lo ? min(max((x - lo) / (hi - lo), 0), 1) : 0.5
    }

    private static let ends: [PTColouring: (String, String)] = {
        var out: [PTColouring: (String, String)] = [:]
        for c in PTColouring.allCases {
            let vs = PeriodicTable.elements.compactMap { c.value($0) }
            out[c] = (vs.min().map { PTColouring.short($0) } ?? "", vs.max().map { PTColouring.short($0) } ?? "")
        }
        return out
    }()

    var lowText: String { PTColouring.ends[self]?.0 ?? "" }
    var highText: String { PTColouring.ends[self]?.1 ?? "" }

    /// The colour of a fraction of the scale: deep blue (low) through teal and green to amber and red (high).
    static func ramp(_ t: Double) -> Color {
        let stops: [(Double, Double, Double)] = [(0.13, 0.27, 0.55), (0.12, 0.45, 0.55), (0.2, 0.52, 0.3), (0.62, 0.55, 0.15), (0.72, 0.22, 0.2)]
        let x = min(max(t, 0), 1) * Double(stops.count - 1)
        let i = min(Int(x), stops.count - 2)
        let f = x - Double(i)
        let a = stops[i], b = stops[i + 1]
        return Color(red: a.0 + (b.0 - a.0) * f, green: a.1 + (b.1 - a.1) * f, blue: a.2 + (b.2 - a.2) * f)
    }

    static let blockColors: [String: String] = ["s": "#c0392b", "p": "#c9a227", "d": "#2e86c1", "f": "#27ae60"]
    static let stateColors: [String: String] = ["Solid": "#5d6d7e", "Liquid": "#2e86c1", "Gas": "#17a589"]

    /// (1.3.65) Every element's colour and words under every view, made once (a cell asks for them each time it draws).
    private static let colours: [PTColouring: [Int: Color]] = {
        var out: [PTColouring: [Int: Color]] = [:]
        for c in PTColouring.allCases {
            var m: [Int: Color] = [:]
            for e in PeriodicTable.elements { m[e.number] = c.makeColor(e) }
            out[c] = m
        }
        return out
    }()

    private static let texts: [PTColouring: [Int: String]] = {
        var out: [PTColouring: [Int: String]] = [:]
        for c in PTColouring.allCases {
            var m: [Int: String] = [:]
            for e in PeriodicTable.elements { m[e.number] = c.makeText(e) }
            out[c] = m
        }
        return out
    }()

    /// An element's colour under this view.
    func color(_ e: PTElement) -> Color { PTColouring.colours[self]?[e.number] ?? makeColor(e) }

    private func makeColor(_ e: PTElement) -> Color {
        switch self {
        case .category: return e.kind.color
        case .block: return Color(hex: PTColouring.blockColors[e.block] ?? "#8e8e93")
        case .state: return Color(hex: PTColouring.stateColors[e.phase] ?? "#8e8e93")
        default: return fraction(e).map { PTColouring.ramp($0) } ?? Color(white: 0.35)
        }
    }

    /// Its trend: across a period (left to right) and up a group — +1 rising, −1 falling, nil no simple trend — and in
    /// words.
    var trend: (across: Int, up: Int, acrossWords: String, upWords: String)? {
        switch self {
        case .electronegativity: return (1, 1, "Electronegativity increases across a period", "Increases up a group")
        case .radius: return (-1, -1, "Atomic radius decreases across a period", "Increases down a group")
        case .ionization: return (1, 1, "Ionization energy increases across a period", "Increases up a group")
        case .affinity: return (1, 1, "Electron affinity generally increases across a period", "Generally increases up a group")
        default: return nil
        }
    }
}

// MARK: - Electrons

/// A subshell and its electrons: 2p⁴ is n 2, l 1 (s 0, p 1, d 2, f 3), count 4.
struct PTSubshell: Hashable, Identifiable {
    let n: Int
    let l: Int
    let count: Int

    var id: String { "\(n)\(letter)" }
    var letter: String { ["s", "p", "d", "f"][min(max(l, 0), 3)] }
    var capacity: Int { 2 * (2 * l + 1) }
    var orbitals: Int { 2 * l + 1 }
    var name: String { "\(n)\(letter)" }

    /// Its electrons in its orbitals, by Hund's rule (each orbital one before any takes two): each orbital's count.
    var filling: [Int] {
        (0..<orbitals).map { i in (count > i ? 1 : 0) + (count > orbitals + i ? 1 : 0) }
    }
}

enum PTConfig {
    private static let cores: [String: Int] = ["He": 2, "Ne": 10, "Ar": 18, "Kr": 36, "Xe": 54, "Rn": 86]

    /// The configuration as written ("[He] 2s2 2p2"), its subshells read: digits after a letter taken while they fit the
    /// subshell (the data's "5d16s2" is 5d¹ 6s²).
    static func parts(_ config: String) -> (core: String?, subs: [PTSubshell]) {
        var s = config.replacingOccurrences(of: "*", with: "")
        var core: String?
        if let open = s.firstIndex(of: "["), let close = s.firstIndex(of: "]"), open < close {
            core = String(s[s.index(after: open)..<close])
            s.removeSubrange(open...close)
        }
        var subs: [PTSubshell] = []
        let chars = Array(s.filter { !$0.isWhitespace })
        var i = 0
        while i < chars.count {
            guard let n = chars[i].wholeNumberValue, i + 1 < chars.count, let l = ["s", "p", "d", "f"].firstIndex(of: String(chars[i + 1])) else { i += 1; continue }
            let cap = 2 * (2 * l + 1)
            var j = i + 2
            var count = 0
            while j < chars.count, let d = chars[j].wholeNumberValue, count * 10 + d <= cap {
                count = count * 10 + d
                j += 1
                // (one digit is enough when the next makes a new subshell's n: 2p6 3s…)
                if count * 10 > cap { break }
            }
            subs.append(PTSubshell(n: n, l: l, count: count))
            i = j
        }
        return (core, subs)
    }

    /// Every subshell, the noble-gas core written out, in order of n then l.
    /// (1.3.65) each element's subshells worked out once (an element's page asks for them many times as it draws)
    private static var subshellCache: [Int: [PTSubshell]] = [:]
    private static var valenceCache: [Int: [PTSubshell]] = [:]

    static func subshells(of e: PTElement) -> [PTSubshell] {
        if let c = subshellCache[e.number] { return c }
        let out = makeSubshells(e)
        subshellCache[e.number] = out
        return out
    }

    private static func makeSubshells(_ e: PTElement) -> [PTSubshell] {
        var all = expand(e.config, depth: 0)
        var merged: [String: PTSubshell] = [:]
        for s in all { merged[s.id] = PTSubshell(n: s.n, l: s.l, count: (merged[s.id]?.count ?? 0) + s.count) }
        all = Array(merged.values)
        return all.sorted { ($0.n, $0.l) < ($1.n, $1.l) }
    }

    private static func expand(_ config: String, depth: Int) -> [PTSubshell] {
        let p = parts(config)
        var out = p.subs
        if let c = p.core, depth < 8, let n = cores[c], let coreElement = PeriodicTable.byNumber(n) {
            out = expand(coreElement.config, depth: depth + 1) + out
        }
        return out
    }

    /// The outer subshells (those written after the noble-gas core).
    static func valence(of e: PTElement) -> [PTSubshell] {
        if let c = valenceCache[e.number] { return c }
        let p = parts(e.config)
        let out = p.subs.isEmpty ? subshells(of: e) : p.subs
        valenceCache[e.number] = out
        return out
    }

    static func superscript(_ n: Int) -> String {
        let map: [Character: Character] = ["0": "⁰", "1": "¹", "2": "²", "3": "³", "4": "⁴", "5": "⁵", "6": "⁶", "7": "⁷", "8": "⁸", "9": "⁹"]
        return String(String(n).map { map[$0] ?? $0 })
    }

    /// "[He] 2s² 2p²".
    static func pretty(_ e: PTElement) -> String {
        let p = parts(e.config)
        let subs = p.subs.map { "\($0.name)\(superscript($0.count))" }.joined(separator: " ")
        return [p.core.map { "[\($0)]" }, subs.isEmpty ? nil : subs].compactMap { $0 }.joined(separator: " ")
    }

    /// "1s² 2s² 2p²".
    static func full(_ e: PTElement) -> String {
        subshells(of: e).map { "\($0.name)\(superscript($0.count))" }.joined(separator: " ")
    }
}

// MARK: - Comparison

enum PTRank {
    /// The share of elements with a value that this one is above, 0…1.
    static func percentile(_ c: PTColouring, _ e: PTElement) -> Double? {
        guard let v = c.value(e) else { return nil }
        let all = PeriodicTable.elements.compactMap { c.value($0) }
        guard all.count > 1 else { return nil }
        return Double(all.filter { $0 < v }.count) / Double(all.count - 1)
    }
}

extension NSColor {
    /// "ff0d0d" → the colour; anything else → a light gray.
    convenience init(ptHex: String) {
        var s = ptHex.trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        let v = UInt32(s, radix: 16) ?? 0xc8c8c8
        self.init(srgbRed: CGFloat((v >> 16) & 0xff) / 255, green: CGFloat((v >> 8) & 0xff) / 255, blue: CGFloat(v & 0xff) / 255, alpha: 1)
    }
}
