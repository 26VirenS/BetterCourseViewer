import Foundation

// The biology toolkit's colours (spec §3.8), as light and dark hex pairs — dark brighter, for dark backgrounds — used
// in 2D and in 3D materials alike (BioColors.swift makes them colours). Never colour alone: a base always shows its
// letter, an amino acid its class's name or a legend, an organelle its label. Atoms stay CPK (MBChem.color); the
// organelles' own colours are the Cell Explorer's (§5.1).

/// A colour in light and in dark.
struct BioHexPair: Equatable, Hashable {
    let light: String
    let dark: String

    init(_ light: String, _ dark: String) {
        self.light = light
        self.dark = dark
    }

    func hex(dark isDark: Bool) -> String { isDark ? dark : light }
}

enum BioPalette {
    // Bases
    static let adenine = BioHexPair("#2e9e48", "#30d158")
    static let thymine = BioHexPair("#d63a34", "#ff6961")
    /// Orange, so U never looks like T.
    static let uracil = BioHexPair("#e07800", "#ffa133")
    static let guanine = BioHexPair("#c79a00", "#ffd60a")
    static let cytosine = BioHexPair("#1f6fd1", "#5ea8ff")

    // Strands
    static let strand1 = BioHexPair("#7d5cf5", "#a58bff")
    static let strand2 = BioHexPair("#3aa3b8", "#6fd3e6")
    static let rna = BioHexPair("#e07800", "#ffa133")

    // Amino-acid classes
    static let nonpolar = BioHexPair("#c08a2b", "#f0b44a")
    static let polar = BioHexPair("#2e9e78", "#4fd1a5")
    static let acidic = BioHexPair("#d63a34", "#ff6961")
    static let basic = BioHexPair("#1f6fd1", "#5ea8ff")

    // Protein cartoons
    static let helix = BioHexPair("#e8508a", "#ff7aa8")
    static let strand = BioHexPair("#c79a00", "#ffd60a")
    static let coil = BioHexPair("#8e8e93", "#aeaeb2")

    // Chromosomes
    static let maternal = BioHexPair("#e5484d", "#ff6b6b")
    static let paternal = BioHexPair("#3e63dd", "#6e8bff")

    /// H-bond dashes (1.5 pt in 2D, 0.15 Å in 3D).
    static let hBond = BioHexPair("#8e8e93", "#aeaeb2")

    /// A base's colour by its letter (A, C, G, T, U); grey for anything else (N).
    static func base(_ b: Character) -> BioHexPair {
        switch Character(b.uppercased()) {
        case "A": return adenine
        case "T": return thymine
        case "U": return uracil
        case "G": return guanine
        case "C": return cytosine
        default: return coil
        }
    }

    /// An amino acid's class colour.
    static func aminoAcid(_ c: BioAAClass) -> BioHexPair {
        switch c {
        case .nonpolar: return nonpolar
        case .polar: return polar
        case .acidic: return acidic
        case .basic: return basic
        }
    }

    /// An amino acid's colour by its one-letter code (grey for X, a stop or anything else).
    static func aminoAcid(one: Character) -> BioHexPair {
        guard let aa = BioAminoAcids.byOne[Character(one.uppercased())] else { return coil }
        return aminoAcid(aa.group)
    }

    /// A secondary-structure code's colour (H helix, E strand, else coil).
    static func secondary(_ ss: Character) -> BioHexPair { ss == "H" ? helix : (ss == "E" ? strand : coil) }

    /// "#2e9e48" → (r, g, b) in 0…1 (grey for anything unreadable).
    static func rgb(_ hex: String) -> (r: Double, g: Double, b: Double) {
        var s = hex.trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        guard s.count == 6, let v = UInt32(s, radix: 16) else { return (0.56, 0.56, 0.58) }
        return (Double((v >> 16) & 0xff) / 255, Double((v >> 8) & 0xff) / 255, Double(v & 0xff) / 255)
    }
}
