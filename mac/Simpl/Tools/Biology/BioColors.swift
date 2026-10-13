import AppKit
import SwiftUI

// The biology toolkit's colours as colours (spec §3.8; the hex pairs are BioPalette's): SwiftUI colours that follow
// the appearance on their own, and AppKit colours resolved for light or dark (a SceneKit scene is made again when the
// appearance changes, as the chemistry scenes are).

extension BioHexPair {
    /// The colour in light and in dark, as the view is drawn.
    var color: Color { Theme.dynamic(light: NSColor(ptHex: light), dark: NSColor(ptHex: dark)) }

    /// The AppKit colour for light or dark.
    func nsColor(dark isDark: Bool) -> NSColor { NSColor(ptHex: hex(dark: isDark)) }
}

extension BioAAClass {
    /// Its colour (nonpolar amber, polar green, acidic red, basic blue).
    var color: Color { BioPalette.aminoAcid(self).color }
}

enum BioColor {
    /// A base's colour by its letter (always drawn with the letter: never colour alone).
    static func base(_ b: Character) -> Color { BioPalette.base(b).color }
    /// An amino acid's class colour by its one-letter code.
    static func aminoAcid(_ one: Character) -> Color { BioPalette.aminoAcid(one: one).color }
    /// A tool's tile colour.
    static func tool(_ k: ToolKind) -> Color { Color(hex: BioTools.info(k).hex) }
}
