import AppKit
import SwiftUI

// Themes (1.3): the web's look, on the Mac. A theme is one accent and, for a ready-made one, a drawn scene — the web's
// own six drawings (extension/lib/theme.js), drawn to the window's shape and inked as the web inks them (the outlines
// and the darkest masses only: scripts/dev/mac-theme-art.mjs makes them, Theme<Name> in the asset catalogue). The ink
// sits on the page behind everything, the sidebar and the cards' glass over it, coloured tone on tone: the page cast
// with the accent (the paper), the drawing a third of the way from the paper to the accent (the ink). One accent
// throughout: every control, link and selection, and every sidebar glyph, in it — Regular is the Mac's own accent and
// the sidebar's glyphs in their own colours, as the web's Regular is.

@MainActor
final class ThemeStore: ObservableObject {
    static let shared = ThemeStore()

    /// A ready-made theme: its name, its accent and its scene (Default: neither).
    struct Ready: Identifiable, Hashable {
        let name: String
        let accent: String?
        let scene: String?
        var id: String { name }
    }

    /// The web's ready-made themes (content/app/personalize.js READY), each a colour and its scene.
    static let ready: [Ready] = [
        Ready(name: "Default", accent: nil, scene: nil),
        Ready(name: "Dusk", accent: "#ff375f", scene: "Dusk"),
        Ready(name: "Ocean", accent: "#40c8e0", scene: "Ocean"),
        Ready(name: "Forest", accent: "#30d158", scene: "Forest"),
        Ready(name: "Sand", accent: "#ff9f0a", scene: "Sand"),
        Ready(name: "Peaks", accent: "#5e5ce6", scene: "Peaks"),
        Ready(name: "City", accent: "#bf5af2", scene: "City"),
    ]

    /// The colours on offer (lib/theme.js PRESETS): each readable as words and as a glyph by day and by night.
    struct Swatch: Identifiable, Hashable {
        let name: String
        let hex: String
        var id: String { hex }
    }

    static let colours: [Swatch] = [
        Swatch(name: "Pink", hex: "#ff375f"), Swatch(name: "Red", hex: "#ff453a"), Swatch(name: "Amber", hex: "#ff9f0a"),
        Swatch(name: "Green", hex: "#30d158"), Swatch(name: "Teal", hex: "#40c8e0"), Swatch(name: "Indigo", hex: "#5e5ce6"),
        Swatch(name: "Purple", hex: "#bf5af2"),
    ]

    /// The scene behind the window ("Dusk"…), or none.
    @Published private(set) var scene: String?
    /// The accent, as a six-digit hex; nil: Regular (the Mac's own accent).
    @Published private(set) var accentHex: String?
    /// Raised at every change: the window drawn again whole, in the new colours.
    @Published private(set) var revision = 0

    /// The accent the dynamic colours read as they are drawn (nil: Regular).
    nonisolated(unsafe) static var accentNow: NSColor?

    private static let sceneKey = "themeScene", accentKey = "themeAccent"

    private init() {
        let d = UserDefaults.standard
        // (the screenshot suite: -SimplTheme Dusk wears a ready-made theme for that run)
        if let name = d.string(forKey: "SimplTheme"), let r = Self.ready.first(where: { $0.name == name }) {
            scene = r.scene
            accentHex = r.accent
        } else {
            scene = d.string(forKey: Self.sceneKey).flatMap { s in Self.ready.contains { $0.scene == s } ? s : nil }
            accentHex = d.string(forKey: Self.accentKey).flatMap(Self.normalize)
        }
        Self.accentNow = accentHex.map { NSColor(Color(hex: $0)) }
    }

    /// A ready-made theme worn: its colour and its scene.
    func wear(_ r: Ready) { set(scene: r.scene, accent: r.accent) }

    /// A colour of the student's own (nil: Regular), the scene kept.
    func setAccent(_ hex: String?) { set(scene: scene, accent: hex.flatMap(Self.normalize)) }

    /// The scene alone changed (nil: none), the colour kept.
    func setScene(_ s: String?) { set(scene: s, accent: accentHex) }

    /// The ready-made theme being worn, if what is worn is one.
    var wearing: Ready? { Self.ready.first { $0.scene == scene && $0.accent == accentHex } }

    private func set(scene s: String?, accent a: String?) {
        guard s != scene || a != accentHex else { return }
        scene = s
        accentHex = a
        Self.accentNow = a.map { NSColor(Color(hex: $0)) }
        let d = UserDefaults.standard
        if let s { d.set(s, forKey: Self.sceneKey) } else { d.removeObject(forKey: Self.sceneKey) }
        if let a { d.set(a, forKey: Self.accentKey) } else { d.removeObject(forKey: Self.accentKey) }
        revision += 1
    }

    /// A six-digit lower-case hex, or nil.
    nonisolated static func normalize(_ hex: String) -> String? {
        var s = hex.trimmingCharacters(in: .whitespaces).lowercased()
        if s.hasPrefix("#") { s.removeFirst() }
        if s.count == 3 { s = s.map { "\($0)\($0)" }.joined() }
        guard s.count == 6, UInt32(s, radix: 16) != nil else { return nil }
        return "#" + s
    }

    // MARK: - A colour of one's own

    /// The nearest colour to `color` the picker offers (lib/theme.js nearest()): grey given a quarter of saturation, and
    /// the lightness moved into the band where it stands 3:1 against both the light card and the dark one, so every
    /// shade drawn from it keeps its hue.
    nonisolated static func readable(_ color: NSColor) -> String {
        let c = color.usingColorSpace(.sRGB) ?? .systemBlue
        var (h, s, l) = hsl(c.redComponent, c.greenComponent, c.blueComponent)
        s = max(s, 0.25)
        var lo: Double?, hi: Double?
        for i in 5...95 {
            let t = rgb(h, s, Double(i) / 100)
            if contrast(t, (1, 1, 1)) >= 3, contrast(t, (28 / 255, 28 / 255, 30 / 255)) >= 3 {
                if lo == nil { lo = Double(i) / 100 }
                hi = Double(i) / 100
            }
        }
        l = min(max(l, lo ?? 0.4), hi ?? 0.4)
        let (r, g, b) = rgb(h, s, l)
        return String(format: "#%02x%02x%02x", Int((r * 255).rounded()), Int((g * 255).rounded()), Int((b * 255).rounded()))
    }

    private nonisolated static func hsl(_ r: Double, _ g: Double, _ b: Double) -> (Double, Double, Double) {
        let mx = max(r, g, b), mn = min(r, g, b), l = (mx + mn) / 2
        guard mx != mn else { return (0, 0, l) }
        let d = mx - mn
        let s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn)
        var h: Double
        if mx == r { h = (g - b) / d + (g < b ? 6 : 0) } else if mx == g { h = (b - r) / d + 2 } else { h = (r - g) / d + 4 }
        h *= 60
        return (h, s, l)
    }

    private nonisolated static func rgb(_ h: Double, _ s: Double, _ l: Double) -> (Double, Double, Double) {
        guard s > 0 else { return (l, l, l) }
        let q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q, hh = h.truncatingRemainder(dividingBy: 360) / 360
        func f(_ t0: Double) -> Double {
            var t = t0
            if t < 0 { t += 1 }
            if t > 1 { t -= 1 }
            if t < 1 / 6 { return p + (q - p) * 6 * t }
            if t < 1 / 2 { return q }
            if t < 2 / 3 { return p + (q - p) * (2 / 3 - t) * 6 }
            return p
        }
        return (f(hh + 1 / 3), f(hh), f(hh - 1 / 3))
    }

    private nonisolated static func contrast(_ a: (Double, Double, Double), _ b: (Double, Double, Double)) -> Double {
        func lum(_ c: (Double, Double, Double)) -> Double {
            func ch(_ v: Double) -> Double { v <= 0.03928 ? v / 12.92 : pow((v + 0.055) / 1.055, 2.4) }
            return 0.2126 * ch(c.0) + 0.7152 * ch(c.1) + 0.0722 * ch(c.2)
        }
        let x = lum(a), y = lum(b)
        return (max(x, y) + 0.05) / (min(x, y) + 0.05)
    }
}

// MARK: - The colours drawn from the accent

extension Theme {
    /// The accent in force: the theme's, or the Mac's own (Regular).
    static let accent = Color(nsColor: NSColor(name: "SimplAccent") { _ in ThemeStore.accentNow ?? .controlAccentColor })

    /// `a` moved `t` of the way to `b`, in sRGB.
    static func mix(_ a: NSColor, _ b: NSColor, _ t: CGFloat) -> NSColor {
        let x = a.usingColorSpace(.sRGB) ?? a, y = b.usingColorSpace(.sRGB) ?? b
        return NSColor(srgbRed: x.redComponent + (y.redComponent - x.redComponent) * t,
                       green: x.greenComponent + (y.greenComponent - x.greenComponent) * t,
                       blue: x.blueComponent + (y.blueComponent - x.blueComponent) * t,
                       alpha: x.alphaComponent + (y.alphaComponent - x.alphaComponent) * t)
    }

    /// A ground cast with the accent, as the web casts its greys (lib/theme.js CAST): `light` and `dark` are how much
    /// of the accent each look takes. Regular: the ground as it is.
    static func cast(_ light: NSColor, _ dark: NSColor, by k: (light: CGFloat, dark: CGFloat)) -> NSColor {
        NSColor(name: nil) { appearance in
            let isDark = appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            let base = isDark ? dark : light
            guard let a = ThemeStore.accentNow else { return base }
            return mix(base, a, isDark ? k.dark : k.light)
        }
    }

    /// The page's own greys, by day and by night (Theme.page before it is cast).
    static let lightPage = NSColor(srgbRed: 245 / 255, green: 245 / 255, blue: 247 / 255, alpha: 1)
    static let darkPage = NSColor(srgbRed: 22 / 255, green: 22 / 255, blue: 24 / 255, alpha: 1)

    /// The paper the scene is inked on: the page, cast (Theme.page).
    static var paper: Color { page }

    /// The scene's ink: the paper a third of the way to the accent (lib/theme.js SCENE_INK), a little more by night.
    static let ink = Color(nsColor: NSColor(name: nil) { appearance in
        let isDark = appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
        let base = isDark ? darkPage : lightPage
        guard let a = ThemeStore.accentNow else { return base }
        let paper = mix(base, a, isDark ? 0.1 : 0.12)
        return mix(paper, a, isDark ? 0.34 : 0.3)
    })
}

// MARK: - The page's ground

/// Where the window's canvas is, in the window: the scene behind every screen is drawn to it, so each screen's ground
/// shows its own part of one picture, and a screen pushed over another lines up with it.
private struct ThemeCanvasKey: EnvironmentKey {
    static let defaultValue: CGRect? = nil
}

extension EnvironmentValues {
    var themeCanvas: CGRect? {
        get { self[ThemeCanvasKey.self] }
        set { self[ThemeCanvasKey.self] = newValue }
    }
}

extension View {
    /// The window's canvas measured here (the root of a window), for the scene drawn on every ground under it.
    func themeCanvas() -> some View { modifier(ThemeCanvas()) }
}

private struct ThemeCanvas: ViewModifier {
    @State private var canvas: CGRect?

    func body(content: Content) -> some View {
        content
            .environment(\.themeCanvas, canvas)
            .background {
                GeometryReader { g in
                    Color.clear
                        .onAppear { canvas = g.frame(in: .global) }
                        .onChange(of: g.frame(in: .global)) { _, f in canvas = f }
                }
                .ignoresSafeArea()
            }
    }
}

/// A screen's ground: the page, cast with the accent — and, with a scene, the scene inked on it, the part of it that is
/// behind this screen.
struct PageGround: View {
    @ObservedObject private var theme = ThemeStore.shared

    var body: some View {
        if let scene = theme.scene {
            ThemeBackdrop(scene: scene)
        } else {
            Theme.page
        }
    }
}

/// The scene inked on its paper, drawn to the window's canvas and showing the part of it behind this view.
struct ThemeBackdrop: View {
    let scene: String
    @Environment(\.themeCanvas) private var canvas

    var body: some View {
        GeometryReader { g in
            let me = g.frame(in: .global)
            let whole = canvas ?? me
            ZStack(alignment: .topLeading) {
                Theme.paper
                Image("Theme\(scene)")
                    .resizable()
                    .renderingMode(.template)
                    .aspectRatio(contentMode: .fill)
                    .foregroundStyle(Theme.ink)
                    .frame(width: max(whole.width, 1), height: max(whole.height, 1))
                    .clipped()
                    .offset(x: whole.minX - me.minX, y: whole.minY - me.minY)
            }
            .frame(width: g.size.width, height: g.size.height, alignment: .topLeading)
            .clipped()
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }
}

/// A theme's small picture (Settings ▸ Appearance): its scene inked on its paper in its own colour, or the page.
struct ThemeSwatch: View {
    let ready: ThemeStore.Ready
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let accent = ready.accent.map { NSColor(Color(hex: $0)) }
        let dark = scheme == .dark
        let base = dark ? Theme.darkPage : Theme.lightPage
        let paper = accent.map { Theme.mix(base, $0, dark ? 0.1 : 0.12) } ?? base
        let ink = accent.map { Theme.mix(paper, $0, dark ? 0.34 : 0.3) } ?? paper
        ZStack {
            Color(nsColor: paper)
            if let s = ready.scene {
                Image("Theme\(s)")
                    .resizable()
                    .renderingMode(.template)
                    .aspectRatio(contentMode: .fill)
                    .foregroundStyle(Color(nsColor: ink))
            } else {
                Image(systemName: "circle.lefthalf.filled")
                    .font(.system(size: 22, weight: .light))
                    .foregroundStyle(.secondary)
            }
        }
        .clipped()
    }
}
